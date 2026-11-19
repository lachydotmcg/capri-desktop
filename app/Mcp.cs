using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class PluginServer
    {
        internal string Name;
        internal string Command;
        internal string[] Args = new string[0];
        internal string Cwd;
        internal Dictionary<string, string> Env = new Dictionary<string, string>(StringComparer.Ordinal);

        private static string Shown(string text) { return Regex.Replace(text ?? "", @"\$\{(?:CLAUDE|CAPRI)_PLUGIN_ROOT\}", "<its folder>"); }

        internal string Line
        {
            get
            {
                List<string> parts = new List<string> { McpServer.Quoted(Shown(Command)) };
                foreach (string arg in Args) parts.Add(McpServer.Quoted(Shown(arg)));
                return string.Join(" ", parts.ToArray());
            }
        }
    }

    internal sealed class McpException : Exception
    {
        internal McpException(string message) : base(message) { }
    }

    internal sealed class McpServer : IDisposable
    {
        internal const string Protocol = "2025-06-18";
        private const int MostLine = 4 * 1024 * 1024, MostErrors = 4000;
        private readonly PluginServer spec;
        private readonly string root;
        private readonly object gate = new object(), sending = new object();
        private readonly Dictionary<long, Reply> waiting = new Dictionary<long, Reply>();
        private readonly StringBuilder errors = new StringBuilder();
        private Process process;
        private Stream input;
        private long next;
        private bool ended, started;
        internal DateTime Used = DateTime.UtcNow;
        internal volatile bool Stale;
        internal string Version, Instructions;

        private sealed class Reply
        {
            internal bool Done;
            internal object Result;
            internal string Error;
        }

        internal McpServer(PluginServer server, string pluginRoot)
        {
            spec = server;
            root = pluginRoot;
        }

        internal string Name { get { return spec.Name; } }

        internal bool Alive { get { lock (gate) return started && !ended; } }

        internal string Errors { get { lock (errors) return errors.ToString().Trim(); } }

        internal static bool OnWindows { get { return Environment.OSVersion.Platform == PlatformID.Win32NT; } }

        internal void Start(int timeout)
        {
            lock (gate)
            {
                if (started && !ended) return;
                if (started) throw new McpException(Ended());
                started = true;
            }
            ProcessStartInfo info = Launch(spec, root);
            Process made;
            try { made = Process.Start(info); }
            catch (System.ComponentModel.Win32Exception error) { Finish(); throw new McpException("Couldn't start " + spec.Command + ": " + error.Message); }
            catch (InvalidOperationException error) { Finish(); throw new McpException("Couldn't start " + spec.Command + ": " + error.Message); }
            if (made == null) { Finish(); throw new McpException("Couldn't start " + spec.Command + "."); }
#if !CROSS
            Engine.Bind(made);
#endif
            lock (gate)
            {
                process = made;
                input = made.StandardInput.BaseStream;
            }
            Thread reader = new Thread(delegate() { Read(made.StandardOutput); }) { IsBackground = true, Name = "Capri plug-in " + spec.Name };
            reader.Start();
            Thread drain = new Thread(delegate() { Drain(made.StandardError); }) { IsBackground = true, Name = "Capri plug-in errors " + spec.Name };
            drain.Start();
            object answer = Request("initialize", PluginJson.Make("protocolVersion", Protocol, "capabilities", PluginJson.Make(), "clientInfo", PluginJson.Make("name", "capri", "version", typeof(McpServer).Assembly.GetName().Version.ToString(3))), timeout);
            Version = PluginJson.Str(answer, "protocolVersion");
            Instructions = PluginJson.Str(answer, "instructions");
            Notify("notifications/initialized", null);
        }

        internal static ProcessStartInfo Launch(PluginServer server, string pluginRoot)
        {
            ProcessStartInfo info = new ProcessStartInfo();
            Dictionary<string, string> env = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, string> pair in server.Env) env[pair.Key] = Expand(pair.Value, pluginRoot);
            string file = Resolve(Expand(server.Command, pluginRoot), pluginRoot, env);
            List<string> args = new List<string>();
            foreach (string arg in server.Args) args.Add(Quoted(Expand(arg, pluginRoot)));
            string joined = string.Join(" ", args.ToArray());
            string extension = Path.GetExtension(file).ToLowerInvariant();
            if (OnWindows && (extension == ".cmd" || extension == ".bat"))
            {
                info.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                info.Arguments = "/d /s /c \"" + Quoted(file) + (joined.Length == 0 ? "" : " " + joined) + "\"";
            }
            else
            {
                info.FileName = file;
                info.Arguments = joined;
            }
            info.UseShellExecute = false;
            info.RedirectStandardInput = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            info.CreateNoWindow = true;
            info.StandardOutputEncoding = new UTF8Encoding(false);
            info.StandardErrorEncoding = new UTF8Encoding(false);
#if CROSS
            info.StandardInputEncoding = new UTF8Encoding(false);
#endif
            string asked = Expand(server.Cwd, pluginRoot);
            string cwd = string.IsNullOrEmpty(asked) ? pluginRoot : Path.IsPathRooted(asked) ? asked : Path.Combine(pluginRoot, asked);
            info.WorkingDirectory = Directory.Exists(cwd) ? cwd : pluginRoot;
            ModFloor.Scrub(info.EnvironmentVariables, server.Env.Keys);
            foreach (KeyValuePair<string, string> pair in env) info.EnvironmentVariables[pair.Key] = pair.Value;
            info.EnvironmentVariables["CLAUDE_PLUGIN_ROOT"] = pluginRoot;
            info.EnvironmentVariables["CAPRI_PLUGIN_ROOT"] = pluginRoot;
            return info;
        }

        internal static string Expand(string text, string pluginRoot)
        {
            return Regex.Replace(text ?? "", @"\$\{([A-Za-z_][A-Za-z0-9_]*)(?::-([^}]*))?\}", delegate(Match found)
            {
                string name = found.Groups[1].Value;
                if (name == "CLAUDE_PLUGIN_ROOT" || name == "CAPRI_PLUGIN_ROOT") return pluginRoot;
                string value = Environment.GetEnvironmentVariable(name);
                return string.IsNullOrEmpty(value) ? (found.Groups[2].Success ? found.Groups[2].Value : "") : value;
            });
        }

        internal static string Resolve(string command, string pluginRoot, Dictionary<string, string> env)
        {
            string asked = (command ?? "").Trim();
            if (asked.Length == 0) throw new McpException("A plug-in server has no command.");
            bool pathy = asked.IndexOf('/') >= 0 || asked.IndexOf('\\') >= 0;
            if (Path.IsPathRooted(asked)) return asked;
            if (pathy)
            {
                string local = Path.GetFullPath(Path.Combine(pluginRoot, asked));
                if (File.Exists(local) || !OnWindows) return local;
                foreach (string extension in Extensions()) if (File.Exists(local + extension)) return local + extension;
                return local;
            }
            string paths = null;
            if (env != null && env.ContainsKey("PATH")) paths = env["PATH"];
            if (paths == null) paths = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string folder in paths.Split(Path.PathSeparator))
            {
                string place = folder.Trim().Trim('"');
                if (place.Length == 0) continue;
                try
                {
                    string candidate = Path.Combine(place, asked);
                    if (OnWindows)
                    {
                        if (Path.GetExtension(asked).Length > 0 && File.Exists(candidate)) return candidate;
                        foreach (string extension in Extensions()) if (File.Exists(candidate + extension)) return candidate + extension;
                    }
                    else if (File.Exists(candidate)) return candidate;
                }
                catch (ArgumentException) { }
            }
            return asked;
        }

        private static string[] Extensions()
        {
            string listed = Environment.GetEnvironmentVariable("PATHEXT");
            if (string.IsNullOrEmpty(listed)) listed = ".COM;.EXE;.BAT;.CMD";
            List<string> found = new List<string>();
            foreach (string one in listed.Split(';')) if (one.Trim().Length > 0) found.Add(one.Trim().ToLowerInvariant());
            return found.ToArray();
        }

        internal static string Quoted(string arg)
        {
            string text = arg ?? "";
            if (text.Length > 0 && text.IndexOfAny(new[] { ' ', '\t', '\n', '"' }) < 0) return text;
            StringBuilder made = new StringBuilder("\"");
            int slashes = 0;
            foreach (char c in text)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { made.Append('\\', slashes * 2 + 1).Append('"'); slashes = 0; continue; }
                made.Append('\\', slashes).Append(c);
                slashes = 0;
            }
            made.Append('\\', slashes * 2).Append('"');
            return made.ToString();
        }

        internal List<Dictionary<string, object>> Tools(int timeout)
        {
            List<Dictionary<string, object>> found = new List<Dictionary<string, object>>();
            string cursor = null;
            for (int page = 0; page < 10; page++)
            {
                object answer = Request("tools/list", cursor == null ? PluginJson.Make() : PluginJson.Make("cursor", cursor), timeout);
                List<object> tools = PluginJson.List(PluginJson.Get(answer, "tools"));
                if (tools != null) foreach (object tool in tools) { Dictionary<string, object> one = PluginJson.Map(tool); if (one != null && PluginJson.Str(one, "name") != null) found.Add(one); }
                cursor = PluginJson.Str(answer, "nextCursor");
                if (string.IsNullOrEmpty(cursor)) break;
            }
            Stale = false;
            return found;
        }

        internal object Call(string tool, Dictionary<string, object> arguments, int timeout)
        {
            Used = DateTime.UtcNow;
            object answer = Request("tools/call", PluginJson.Make("name", tool, "arguments", arguments ?? PluginJson.Make()), timeout);
            Used = DateTime.UtcNow;
            return answer;
        }

        internal object Request(string method, object parameters, int timeout)
        {
            Reply reply = new Reply();
            long id;
            lock (gate)
            {
                if (ended) throw new McpException(Ended());
                id = ++next;
                waiting[id] = reply;
            }
            Dictionary<string, object> message = PluginJson.Make("jsonrpc", "2.0", "id", id, "method", method);
            if (parameters != null) message["params"] = parameters;
            Send(message);
            DateTime until = DateTime.UtcNow.AddMilliseconds(timeout);
            lock (reply)
            {
                while (!reply.Done)
                {
                    int left = (int)(until - DateTime.UtcNow).TotalMilliseconds;
                    if (left <= 0) break;
                    Monitor.Wait(reply, left);
                }
            }
            if (!reply.Done)
            {
                lock (gate) waiting.Remove(id);
                try { Notify("notifications/cancelled", PluginJson.Make("requestId", id, "reason", "Capri stopped waiting.")); }
                catch (McpException) { }
                throw new McpException(spec.Name + " took longer than " + timeout / 1000 + " seconds to answer.");
            }
            if (reply.Error != null) throw new McpException(reply.Error);
            return reply.Result;
        }

        private void Notify(string method, object parameters)
        {
            Dictionary<string, object> message = PluginJson.Make("jsonrpc", "2.0", "method", method);
            if (parameters != null) message["params"] = parameters;
            Send(message);
        }

        private void Send(object message)
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(PluginJson.Write(message) + "\n");
            Stream to;
            lock (gate) to = input;
            if (to == null) throw new McpException(Ended());
            try
            {
                lock (sending)
                {
                    to.Write(bytes, 0, bytes.Length);
                    to.Flush();
                }
            }
            catch (IOException) { throw new McpException(Ended()); }
            catch (ObjectDisposedException) { throw new McpException(Ended()); }
        }

        private void Read(StreamReader from)
        {
            try
            {
                string line;
                while ((line = from.ReadLine()) != null)
                {
                    if (line.Length > MostLine || line.Trim().Length == 0) continue;
                    object message;
                    if (!PluginJson.TryParse(line, out message) || PluginJson.Map(message) == null) continue;
                    Handle(PluginJson.Map(message));
                }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
            Finish();
        }

        private void Handle(Dictionary<string, object> message)
        {
            object id = PluginJson.Get(message, "id");
            string method = PluginJson.Str(message, "method");
            if (method != null)
            {
                if (method == "notifications/tools/list_changed") Stale = true;
                if (id == null) return;
                Dictionary<string, object> answer = PluginJson.Make("jsonrpc", "2.0", "id", id);
                if (method == "ping") answer["result"] = PluginJson.Make();
                else answer["error"] = PluginJson.Make("code", -32601, "message", "Capri doesn't offer " + method + ".");
                try { Send(answer); }
                catch (McpException) { }
                return;
            }
            if (!(id is double)) return;
            long number = (long)(double)id;
            Reply reply;
            lock (gate)
            {
                if (!waiting.TryGetValue(number, out reply)) return;
                waiting.Remove(number);
            }
            object error = PluginJson.Get(message, "error");
            lock (reply)
            {
                if (error != null) reply.Error = spec.Name + " said: " + (PluginJson.Str(error, "message") ?? "an error");
                else reply.Result = PluginJson.Get(message, "result");
                reply.Done = true;
                Monitor.PulseAll(reply);
            }
        }

        private void Drain(StreamReader from)
        {
            try
            {
                string line;
                while ((line = from.ReadLine()) != null)
                    lock (errors)
                    {
                        errors.Append(line).Append('\n');
                        if (errors.Length > MostErrors) errors.Remove(0, errors.Length - MostErrors);
                    }
            }
            catch (IOException) { }
            catch (ObjectDisposedException) { }
        }

        private string Ended()
        {
            string said = Errors;
            if (said.Length > 300) said = said.Substring(said.Length - 300);
            return spec.Name + " isn't running." + (said.Length == 0 ? "" : " It last said: " + said.Replace('\n', ' ').Trim());
        }

        private void Finish()
        {
            List<Reply> left;
            lock (gate)
            {
                ended = true;
                left = new List<Reply>(waiting.Values);
                waiting.Clear();
            }
            string why = Ended();
            foreach (Reply reply in left)
                lock (reply)
                {
                    reply.Error = why;
                    reply.Done = true;
                    Monitor.PulseAll(reply);
                }
        }

        public void Dispose()
        {
            Process running;
            Stream to;
            lock (gate)
            {
                running = process;
                to = input;
                input = null;
                process = null;
            }
            if (to != null) { try { to.Close(); } catch (IOException) { } }
            if (running != null)
            {
                try
                {
                    if (!running.WaitForExit(1500))
                    {
#if CROSS
                        running.Kill(true);
#else
                        Tree(running.Id);
                        if (!running.HasExited) running.Kill();
#endif
                        running.WaitForExit(2000);
                    }
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
                running.Dispose();
            }
            Finish();
        }

#if !CROSS
        private static void Tree(int id)
        {
            try
            {
                ProcessStartInfo info = new ProcessStartInfo("taskkill.exe", "/PID " + id + " /T /F") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (Process killing = Process.Start(info)) if (killing != null) killing.WaitForExit(3000);
            }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { }
        }
#endif
    }
}
