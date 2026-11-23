using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace CapriDesktop
{
    internal sealed partial class DiscordCompanion
    {
        private const string Prefix = "Capri.Discord.";
        private static Assembly AppAssembly { get { return typeof(DiscordCompanion).Assembly; } }

        internal static bool Included
        {
            get { using (Stream script = AppAssembly.GetManifestResourceStream(Prefix + "companion.mjs")) return script != null; }
        }

        private static ProcessStartInfo Command(string exe, string args, string directory)
        {
            ProcessStartInfo start = new ProcessStartInfo(exe, args)
            {
                WorkingDirectory = directory, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Utf8, StandardErrorEncoding = Utf8
            };
            start.EnvironmentVariables.Remove("NODE_OPTIONS");
            start.EnvironmentVariables.Remove("NODE_PATH");
            return start;
        }

        private static string FindNode()
        {
            string program = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string locations = Path.Combine(program, "nodejs") + ";" + Path.Combine(local, "Programs", "nodejs")
                + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");
            foreach (string entry in locations.Split(';'))
            {
                string path = Environment.ExpandEnvironmentVariables(entry.Trim().Trim('"'));
                if (path.Length < 3 || path[1] != ':' || (path[2] != '\\' && path[2] != '/')) continue;
                try
                {
                    string node = Path.Combine(path, "node.exe");
                    if (File.Exists(node)) return node;
                }
                catch (ArgumentException) { }
            }
            return null;
        }

        private static byte[] Resource(string name)
        {
            using (Stream stream = AppAssembly.GetManifestResourceStream(Prefix + name))
            using (MemoryStream result = new MemoryStream())
            {
                if (stream == null) throw new IOException("The Discord helper was missing from this build.");
                stream.CopyTo(result);
                return result.ToArray();
            }
        }

        private Process Launch(Run run, ProcessStartInfo info, bool bot, DataReceivedEventHandler output)
        {
            lock (gate)
            {
                if (active != run) return null;
                Process child = new Process { StartInfo = info };
                child.OutputDataReceived += output;
                child.ErrorDataReceived += delegate { };
                try
                {
                    if (!child.Start()) throw new IOException("Windows could not start the Discord helper.");
                    run.Child = child;
                    run.IsBot = bot;
                    child.BeginOutputReadLine();
                    child.BeginErrorReadLine();
                    return child;
                }
                catch
                {
                    Kill(child);
                    child.Dispose();
                    run.Child = null;
                    throw;
                }
            }
        }

        private static void Kill(Process child)
        {
            try { if (child != null && !child.HasExited) child.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static void End(Run run)
        {
            Process child = run.Child;
            if (child == null) return;
            try
            {
                if (run.IsBot)
                {
                    child.StandardInput.Close();
                    if (child.WaitForExit(1000)) return;
                }
            }
            catch (InvalidOperationException) { }
            catch (IOException) { }
            Kill(child);
        }

        private bool Tool(Run run, ProcessStartInfo info, int timeout, DataReceivedEventHandler output)
        {
            using (Process child = Launch(run, info, false, output))
            {
                if (child == null) return false;
                try
                {
                    if (!child.WaitForExit(timeout)) { Kill(child); return false; }
                    child.WaitForExit();
                    return child.ExitCode == 0;
                }
                finally { lock (gate) if (run.Child == child) run.Child = null; }
            }
        }

        private string Prepare(Run run, string node)
        {
            string versionLine = null;
            Version version;
            if (!Tool(run, Command(node, "--version", Path.GetDirectoryName(node)), 6000, delegate(object sender, DataReceivedEventArgs e)
                { if (e.Data != null) versionLine = e.Data; })
                || !Version.TryParse((versionLine ?? "").TrimStart('v'), out version) || version < new Version(22, 12))
                throw new IOException("Install Node.js 22.12 or newer, then reconnect.");

            byte[] package = Resource("package-lock.json");
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(package)).Replace("-", "").ToLowerInvariant();
            string where = Path.Combine(home, hash.Substring(0, 16));
            Directory.CreateDirectory(where);
            File.WriteAllBytes(Path.Combine(where, "companion.mjs"), Resource("companion.mjs"));
            File.WriteAllBytes(Path.Combine(where, "package.json"), Resource("package.json"));
            File.WriteAllBytes(Path.Combine(where, "package-lock.json"), package);

            string verified = Path.Combine(where, "verified");
            if (File.Exists(verified) &&
                Tool(run, Command(node, "companion.mjs --check", where), 12000, delegate { })) return where;

            lock (gate)
            {
                if (active != run) return null;
                Set("Installing the local Discord voice helper…", false);
            }
            string npm = Path.Combine(Path.GetDirectoryName(node), "node_modules", "npm", "bin", "npm-cli.js");
            if (!File.Exists(npm)) throw new IOException("Install Node.js with npm included, then reconnect.");
            if (!Tool(run, Command(node, PlanBrain.Quoted(npm) + " ci --ignore-scripts --no-audit --no-fund", where), 240000, delegate { }) ||
                !Tool(run, Command(node, "companion.mjs --check", where), 12000, delegate { }))
                throw new IOException("The voice helper could not install or load its audio dependencies. Check your connection and try again.");
            File.WriteAllText(verified, "", Utf8);
            return where;
        }

        private void Start(Run run)
        {
            string failure = null;
            try
            {
                lock (preparing)
                {
                    if (active != run) return;
                    string node = FindNode();
                    if (node == null) throw new IOException("Install Node.js 22.12 or newer, then reconnect.");
                    string where = Prepare(run, node);
                    if (where == null) return;
                    lock (gate)
                    {
                        if (active != run) return;
                        Set("Signing in to Discord…", false);
                    }
                    using (Process child = Launch(run, Command(node, "companion.mjs", where), true,
                        delegate(object sender, DataReceivedEventArgs e) { Receive(run, e.Data); }))
                    {
                        if (child == null) return;
                        try
                        {
                            string token;
                            lock (gate) token = run.Token;
                            if (token != null) Send(run, "{\"type\":\"start\",\"token\":" + PluginJson.Quote(token) + "}");
                            else End(run);
                            child.WaitForExit();
                        }
                        finally
                        {
                            Kill(child);
                            lock (gate) if (run.Child == child) run.Child = null;
                        }
                    }
                }
            }
            catch (IOException e) { failure = e.Message; }
            catch (Exception) { failure = "The local Discord helper could not start. Try reconnecting."; }
            finally
            {
                lock (gate)
                {
                    if (active == run)
                    {
                        active = null;
                        run.Token = null;
                        run.Voice.Change(false);
                        if (!problem) Set(failure ?? "The Discord helper went offline. Reconnect to start it again.", true);
                    }
                }
            }
        }

        private static void Send(Run run, string line)
        {
            lock (run.Output)
            {
                try
                {
                    Process child = run.Child;
                    if (child != null && !child.HasExited)
                    {
                        byte[] bytes = Utf8.GetBytes(line + "\n");
                        Stream output = child.StandardInput.BaseStream;
                        output.Write(bytes, 0, bytes.Length);
                        output.Flush();
                    }
                }
                catch (IOException) { }
                catch (InvalidOperationException) { }
            }
        }
    }
}
