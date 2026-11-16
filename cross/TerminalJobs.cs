using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Only owner chat dispatch admits jobs. The screen worker deliberately has no access.
    internal static partial class TerminalJobs
    {
        private sealed class Job
        {
            internal string Id, Directory, Cwd, Context;
            internal Process Launcher;
            internal bool MacTerminal;
            internal DateTime Started = DateTime.UtcNow;
        }
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Job> jobs = new Dictionary<string, Job>();
        internal static Func<string> Context = () => "local";
        private static string root;
        private static bool retiring;
        static TerminalJobs() { AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(); }
        private static readonly Timer monitor = new Timer(_ => CheckContext(), null, Timeout.Infinite, Timeout.Infinite);
        private static readonly UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        internal static bool Supported => OperatingSystem.IsLinux() || OperatingSystem.IsMacOS();
        internal static bool Available => Supported && File.Exists(Python) && File.Exists(Worker)
            && (!OperatingSystem.IsMacOS() || File.Exists(Path.Combine(AppContext.BaseDirectory, "bridge", "mac_process.py")));
        private static string Python => OperatingSystem.IsMacOS() ? MacNative.Python() : "/usr/bin/python3";
        private static string Worker => Path.Combine(AppContext.BaseDirectory, "bridge", "terminal_job.py");
        private static bool Current(ToolArguments args) => args.Current == null || args.Current();

        internal static string Intent(ToolArguments args)
        {
            if (args.FromFriend || args.FromAgent || string.IsNullOrWhiteSpace(args.Asked)) return "Terminal jobs require the desktop owner's own request.";
            string request = args.Asked.Trim();
            string framing = string.IsNullOrEmpty(args.Command) ? request : request.Replace(args.Command, "", StringComparison.Ordinal);
            if (!Regex.IsMatch(request, @"(?is)\A(?:capri[,!: ]+)?(?:please )?(?:(?:can|could|would) you )?(?:run|execute|launch|open|start)\b")
                || Regex.IsMatch(framing, @"(?i)\b(do not|don't|never|explain|describe|what does|if|unless)\b"))
                return "Ask me to run the command or terminal task you want; explanatory or conditional text does not execute.";
            // Defense in depth, not a shell sandbox: commands run with the owner's privileges.
            // Never infer privileged/destructive operations from an ordinary diagnostic request.
            foreach (string pattern in new[] { @"\b(sudo|doas|pkexec)\b", @"\b(rm|rmdir|unlink|shred|mkfs|wipefs|dd)\b", @"\b(reboot|shutdown|poweroff)\b" })
                if (Regex.IsMatch(args.Command ?? "", pattern) && args.Asked.IndexOf(args.Command, StringComparison.Ordinal) < 0) return "That privileged or destructive operation was not explicitly requested.";
            return null;
        }

        internal static string Run(ToolArguments args)
        {
            RemoteTurn phone = RemoteTurn.Working;
            if (phone != null) return phone.Current ? RemoteTurn.PcOnly : RemoteTurn.Ended;
            if (!Available) return "Native terminal jobs are unavailable: the packaged Python supervisor is missing.";
            if (string.IsNullOrWhiteSpace(args.Command) || args.Command.Length > 16000 || args.Command.Contains('\0')) return "Invalid command: supply a nonempty shell command of at most 16000 characters.";
            string denied = Intent(args);
            if (denied != null) return denied;
            if (!Current(args)) return "The terminal request was cancelled before dispatch.";
            string cwd = args.Cwd;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrEmpty(cwd)) cwd = home;
            else if (cwd == "~") cwd = home;
            else if (cwd.StartsWith("~/", StringComparison.Ordinal)) cwd = Path.Combine(home, cwd.Substring(2));
            if (!Path.IsPathFullyQualified(cwd) || !System.IO.Directory.Exists(cwd)) return "The requested working directory does not exist or is not absolute.";
            cwd = Path.GetFullPath(cwd);
            string shell = Environment.GetEnvironmentVariable("SHELL");
            if (string.IsNullOrEmpty(shell)) shell = "/bin/sh";
            if (!Path.IsPathFullyQualified(shell) || !File.Exists(shell)) return "The configured user shell is missing.";
            bool visible = args.Visible ?? true;
            bool mac = OperatingSystem.IsMacOS();
            string python = Python;
            string terminal = visible && !mac ? ArchApps.Executable(Environment.GetEnvironmentVariable("TERMINAL") ?? "kitty") : null;
            if (visible && !mac && terminal == null) return "The configured terminal is missing. Set TERMINAL to an executable (kitty, foot, alacritty or xterm).";
            string kind = terminal == null ? null : Path.GetFileName(terminal);
            if (visible && !mac && kind != "kitty" && kind != "foot" && kind != "alacritty" && kind != "xterm") return "This terminal has no qualified argv adapter. Supported: kitty, foot, alacritty, xterm.";
            Job job;
            lock (gate)
            {
                if (retiring) return "Native terminal jobs are shutting down.";
                Sweep();
                if (jobs.Count >= 32 || jobs.Values.Count(Active) >= 8) return "Too many retained or active terminal jobs. Stop an active job before starting another.";
                if (!Current(args)) return "The terminal request was cancelled before dispatch.";
                if (root == null)
                {
                    root = Path.Combine(Path.GetTempPath(), "capri-terminal-" + Guid.NewGuid().ToString("N"));
                    System.IO.Directory.CreateDirectory(root, Private);
                }
                string id = Guid.NewGuid().ToString("N"), directory = Path.Combine(root, id);
                System.IO.Directory.CreateDirectory(directory, Private);
                job = new Job { Id = id, Directory = directory, Cwd = cwd, Context = Context(), MacTerminal = mac && visible };
                string start = mac ? MacNative.ProcessIdentity(Environment.ProcessId) : File.ReadAllText("/proc/self/stat").Split(')').Last().Split(' ', StringSplitOptions.RemoveEmptyEntries)[19];
                string request = JsonSerializer.Serialize(new { job_id = id, command = args.Command, cwd, shell, visible, owner_pid = Environment.ProcessId, owner_start = start, timeout = 3600 });
                if (mac)
                {
                    var value = System.Text.Json.Nodes.JsonNode.Parse(request).AsObject();
                    value["launch_deadline"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 15;
                    request = value.ToJsonString();
                }
                WritePrivate(Path.Combine(directory, "request.json"), request);
                ProcessStartInfo info = mac && visible ? MacNative.Terminal(python, Worker, directory)
                    : new ProcessStartInfo(visible ? terminal : python) { UseShellExecute = false, WorkingDirectory = cwd, CreateNoWindow = !visible };
                if (visible && !mac)
                {
                    if (kind == "kitty") foreach (string s in new[] { "--hold", "--title", "Capri terminal " + id.Substring(0, 8), "--directory", cwd }) info.ArgumentList.Add(s);
                    else if (kind == "alacritty") foreach (string s in new[] { "--hold", "--working-directory", cwd, "-e" }) info.ArgumentList.Add(s);
                    else if (kind == "foot") foreach (string s in new[] { "--hold", "--working-directory=" + cwd }) info.ArgumentList.Add(s);
                    else foreach (string s in new[] { "-hold", "-e" }) info.ArgumentList.Add(s);
                    info.ArgumentList.Add("/usr/bin/python3");
                }
                if (mac && !visible) foreach (string flag in new[] { "-B", "-E", "-S" }) info.ArgumentList.Add(flag);
                if (!mac || !visible) { info.ArgumentList.Add(Worker); info.ArgumentList.Add(directory); }
                try { job.Launcher = Process.Start(info); jobs.Add(id, job); monitor.Change(500, 500); }
                catch { System.IO.Directory.Delete(directory, true); throw; }
            }
            // Called on the model worker; bound initial observation, never block the UI for a job.
            for (int i = 0; i < 20; i++)
            {
                if (!Current(args)) { Stop(job); return Read(job, 0); }
                if (File.Exists(Path.Combine(job.Directory, "status.json"))) break;
                Thread.Sleep(25);
            }
            return Read(job, 0);
        }

        internal static string Control(ToolArguments args)
        {
            if (args.FromFriend || args.FromAgent || string.IsNullOrWhiteSpace(args.Asked) || !Current(args)) return "Only a current owner request can inspect or stop terminal jobs.";
            if (args.Action != "status" && args.Action != "read" && args.Action != "stop" || args.Offset < 0) return "Invalid terminal job action or output offset.";
            lock (gate)
            {
                if (args.JobId == null || !jobs.TryGetValue(args.JobId, out Job job)) return "Unknown terminal job ID; no process was touched.";
                if (args.Action == "stop")
                {
                    if (!Regex.IsMatch(args.Asked.Trim(), @"(?i)\A(?:please )?(?:(?:can|could|would) you )?(?:stop|cancel|terminate)(?: (?:this|that|the|my))?(?: (?:terminal|command))?(?: (?:job|process))?(?: [a-f0-9]{32})?(?: please)?[.!?]?\z")) return "Stopping needs an explicit owner stop request; status did not stop the job.";
                    Match literalId = Regex.Match(args.Asked, @"(?i)\b[a-f0-9]{32}\b");
                    if (literalId.Success && !string.Equals(literalId.Value, job.Id, StringComparison.OrdinalIgnoreCase)) return "The requested job ID differs from the proposed job; no process was touched.";
                    Stop(job);
                    // A stop request is not completion. Briefly observe the supervisor on this
                    // worker so ordinary cooperative exits can return their actual exit status.
                    for (int i = 0; i < 20 && Active(job); i++) Thread.Sleep(25);
                }
                return Read(job, args.Offset ?? 0);
            }
        }
        private static void WritePrivate(string path, string text) { File.WriteAllText(path, text); File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
        private static void Stop(Job job) { WritePrivate(Path.Combine(job.Directory, "stop"), "stop\n"); }
        internal static bool StopAll() { lock (gate) { if (retiring) return false; var active = jobs.Values.Where(Active).ToArray(); foreach (Job job in active) Stop(job); return active.Length > 0; } }
        internal static void Shutdown()
        {
            lock (gate)
            {
                if (retiring) return;
                retiring = true; monitor.Change(Timeout.Infinite, Timeout.Infinite);
                foreach (Job job in jobs.Values)
                {
                    try
                    {
                        if (Active(job)) { WritePrivate(Path.Combine(job.Directory, "retire"), "retire\n"); Stop(job); }
                        else Clean(job.Directory);
                        job.Launcher?.Dispose();
                    }
                    catch (IOException error) { Brain.Log("terminal cleanup", -1, error.GetType().Name); }
                }
                jobs.Clear();
                if (root != null && System.IO.Directory.Exists(root) && !System.IO.Directory.EnumerateFileSystemEntries(root).Any()) System.IO.Directory.Delete(root);
            }
        }
        private static void Clean(string directory)
        {
            foreach (string name in new[] { "request.json", "status.json", "status.tmp", "transcript.bin", "stop", "retire" }) File.Delete(Path.Combine(directory, name));
            System.IO.Directory.Delete(directory);
        }
        internal static void CheckContext()
        {
            lock (gate)
            {
                if (retiring) return;
                var active = jobs.Values.Where(Active).ToArray();
                if (active.Length == 0) { monitor.Change(Timeout.Infinite, Timeout.Infinite); return; }
                string context;
                try { context = Context(); }
                catch (Exception) { foreach (Job job in active) Stop(job); return; }
                foreach (Job job in active) if (job.Context != context) Stop(job);
            }
        }

        private static bool Active(Job job)
        {
            try { using var d = JsonDocument.Parse(File.ReadAllText(Path.Combine(job.Directory, "status.json"))); string state = d.RootElement.GetProperty("state").GetString(); return state == "running" || state == "starting" || state == "stopping"; }
            catch (IOException)
            {
                bool waiting = DateTime.UtcNow - job.Started < TimeSpan.FromSeconds(15);
                if (!waiting && OperatingSystem.IsMacOS())
                {
                    try { Stop(job); }
                    catch (DirectoryNotFoundException) { }
                    catch (IOException) { return true; }
                }
                return waiting;
            }
        }
        private static void Sweep()
        {
            foreach (Job job in jobs.Values.Where(j => !Active(j)).OrderBy(j => j.Started).Take(Math.Max(0, jobs.Count - 24)).ToArray())
            { job.Launcher?.Dispose(); Clean(job.Directory); jobs.Remove(job.Id); }
        }
        private static string Tail(string output)
        {
            if (output.Length <= 8000) return output;
            int cut = output.Length - 8000;
            int start = StringInfo.ParseCombiningCharacters(output).Where(index => index >= cut).DefaultIfEmpty(output.Length).First();
            return output.Substring(start);
        }
        private static string WithStopScope(string response)
        {
            if (!OperatingSystem.IsMacOS()) return response;
            var value = System.Text.Json.Nodes.JsonNode.Parse(response).AsObject();
            value["stop_scope"] = "The command's original process group. Separate job-control groups must be stopped in Terminal; the job stays pending until they exit.";
            return value.ToJsonString();
        }

        private static string Read(Job job, long offset)
        {
            string path = Path.Combine(job.Directory, "status.json");
            if (!File.Exists(path))
            {
                bool rejected = job.MacTerminal && job.Launcher != null && job.Launcher.HasExited && job.Launcher.ExitCode != 0;
                if (rejected || OperatingSystem.IsMacOS() && DateTime.UtcNow - job.Started >= TimeSpan.FromSeconds(15)) Stop(job);
                return WithStopScope(JsonSerializer.Serialize(new { job_id = job.Id, state = rejected || DateTime.UtcNow - job.Started > TimeSpan.FromSeconds(15) ? "failed" : "starting", cwd = job.Cwd,
                    error = rejected ? "Terminal.app did not accept the job. Check Terminal and Capri's Automation permission in System Settings > Privacy & Security." : "No command-start receipt yet; launcher exit is not command completion." }));
            }
            using var d = JsonDocument.Parse(File.ReadAllText(path)); var state = d.RootElement;
            string output = state.GetProperty("output").GetString();
            long total = state.GetProperty("total_bytes").GetInt64();
            // Cursor is a byte observation watermark. Each changed read returns the bounded current tail,
            // explicitly allowing overlap; raw bytes remain in the private capped transcript.
            return WithStopScope(JsonSerializer.Serialize(new { job_id = job.Id, state = state.GetProperty("state").GetString(), cwd = job.Cwd, exit_code = state.GetProperty("exit_code"), output = total > offset ? Tail(output) : "", next_offset = total, tail_may_overlap = true, truncated = state.GetProperty("truncated").GetBoolean() || output.Length > 8000, interactive = state.GetProperty("interactive"), error = state.GetProperty("error"), stop_reason = state.GetProperty("stop_reason") }));
        }
    }
}
