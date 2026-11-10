using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class LinuxRoutes
    {
        internal static KeyValuePair<string, ToolArguments> Request(string text)
        {
            string asked = (text ?? "").Trim();
            Match job = Regex.Match(asked, @"\A(?:please )?(?:(?:can|could|would) you )?(stop|cancel|terminate|read|inspect|check|show status of)(?: (?:this|that|the|my))?(?: (?:terminal|command))?(?: job)? ([a-f0-9]{32})(?: please)?[.!?]?\z", RegexOptions.IgnoreCase);
            if (job.Success)
            {
                string verb = job.Groups[1].Value.ToLowerInvariant();
                return new KeyValuePair<string, ToolArguments>("terminal_job", new ToolArguments { JobId=job.Groups[2].Value, Action=verb=="stop" || verb=="cancel" || verb=="terminate" ? "stop" : "read", Asked=text });
            }
            Match service = Regex.Match(asked, @"(?i)\A(?:please )?(?:(?:can|could|would) you )?(?:check|read|show|inspect|get)\b.*?\b(?:status|state)\b.*?(?<![a-z0-9_.@:/-])([a-z0-9_.@:-]+\.service)(?=$|\s|[.!?](?:\s|$))");
            if (service.Success && !Regex.IsMatch(asked, @"(?i)\b(how|explain|describe)\b"))
                return new KeyValuePair<string, ToolArguments>("linux_system", new ToolArguments { Action="service_status", Name=service.Groups[1].Value, Asked=text });
            if (Regex.IsMatch(asked, @"(?i)\A(?:please )?(?:(?:can|could|would) you )?(?:list|show|report|check)\b.*\b(?:running processes|process list|cpu|memory|system status)\b") || Regex.IsMatch(asked, @"(?i)\Awhat(?:'s| is) running[?!.]?\z"))
            {
                bool processes=Regex.IsMatch(asked,@"(?i)process|running"), status=Regex.IsMatch(asked,@"(?i)cpu|memory|system status");
                return new KeyValuePair<string,ToolArguments>("linux_system",new ToolArguments { Action=processes && status ? "overview" : processes ? "processes" : "status", Asked=text });
            }
            return default;
        }

        internal static ToolArguments Direct(string text)
        {
            if (HyprlandDesktop.Current == null) return null;
            Match match = Regex.Match(text ?? "", @"^(?:please )?(force close|force quit|kill|end task for|close|quit|switch to|minimize) (.+?)[.!?]*$", RegexOptions.IgnoreCase);
            if (!match.Success) return null;
            string verb = match.Groups[1].Value.ToLowerInvariant(), name = match.Groups[2].Value.Trim();
            if (!Selector(name) && verb != "force close" && verb != "force quit" && verb != "kill" && verb != "end task for") return null;
            return new ToolArguments { Name = name, Action = verb == "switch to" ? "switch_to" : verb == "minimize" ? "minimize" : verb == "close" || verb == "quit" ? "close" : "force_close", Asked = text };
        }

        private static bool Selector(string name) { return Regex.IsMatch(name ?? "", @"^(?:the |my )?(?:current|active|this|last|previous)(?: app| application| window)?$", RegexOptions.IgnoreCase); }

        internal static string Window(ToolArguments args)
        {
            if (HyprlandDesktop.Current == null || !Selector(args.Name) && args.Action != "force_close") return null;
            if (args.Action != "close" && args.Action != "force_close" && args.Action != "switch_to" && args.Action != "minimize") return "That action is not available for a relative native window selector.";
            if (args.Action == "force_close" && !Regex.IsMatch(args.Asked ?? "", @"(?i)\b(force (?:close|quit)|kill|end (?:the )?task)\b")) return "Force close needs an explicit request; graceful close was not escalated.";
            HyprlandDesktop desktop = HyprlandDesktop.Current;
            List<OpenWindow> windows = desktop.ListWindows();
            List<OpenWindow> matches;
            if (Selector(args.Name))
            {
                HyprlandSnapshot state = desktop.Snapshot;
                if (state == null) return "The current window snapshot is unavailable.";
                bool last = Regex.IsMatch(args.Name, @"(?i)last|previous");
                OpenWindow active = windows.Find(window => window.Handle.ToInt64() == state.ActiveAddress);
                OpenWindow picked = last ? windows.Find(window => window != active) : active ?? windows.FirstOrDefault();
                matches = picked == null ? new List<OpenWindow>() : new List<OpenWindow> { picked };
            }
            else
            {
                string name = (args.Name ?? "").Trim();
                matches = windows.Where(window => window.Program.Equals(name, StringComparison.OrdinalIgnoreCase) || window.Title.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            }
            if (matches.Count != 1) return matches.Count == 0 ? "There is no qualified matching window." : "Several windows match. Name one exact window title before force closing it.";
            OpenWindow target = matches[0];
            if (args.Current != null && !args.Current()) return "The request was cancelled before dispatch.";
            bool changed = desktop.Steer(target, args.Action == "switch_to" ? "focus" : args.Action);
            return changed ? (args.Action == "force_close" ? "Force closed the exact selected process; unsaved work in that process may be lost." : args.Action == "close" ? "The selected window closed." : args.Action == "minimize" ? "The selected window was minimized." : "The selected window is focused.") : desktop.LastError ?? "The window action was not verified; no retry was made.";
        }

        internal static string Website(ToolArguments args)
        {
            if (!Uri.TryCreate(args.Site, UriKind.Absolute, out Uri url) || !url.IsLoopback) return null;
            Match explicitUrl = Regex.Match(args.Asked ?? "", @"^(?:please )?(?:open|visit|show)(?: me)?\s+(https?://\S+?)(?: please)?$", RegexOptions.IgnoreCase);
            if ((url.Scheme != "http" && url.Scheme != "https") || url.UserInfo.Length != 0 || !string.IsNullOrEmpty(args.Search)
                || !explicitUrl.Success || !Uri.TryCreate(explicitUrl.Groups[1].Value, UriKind.Absolute, out Uri requested) || requested.AbsoluteUri != url.AbsoluteUri)
                return "Name the exact http or https loopback URL in an explicit open request.";
            if (args.Current != null && !args.Current()) return "The website request was cancelled.";
            Hands.Run(url.AbsoluteUri);
            return "Opened the explicitly requested local URL in your default browser.";
        }

        internal static bool ServiceRequested(string request, string operation, string unit)
        {
            if (operation != "start" && operation != "stop" && operation != "restart") return false;
            // The model may propose an operation, but only a complete affirmative owner request authorizes it.
            return Regex.IsMatch((request ?? "").Trim(), @"\A(?i:(?:please )?" + operation
                + @" (?:the )?(?:user )?(?:service )?)" + Regex.Escape(unit)
                + @"(?i:(?: service)?(?: please)?)[.!]?\z");
        }

        internal static string System(ToolArguments args)
        {
            if (!OperatingSystem.IsLinux()) return "Linux system tools are unavailable on this platform.";
            string action = args.Action ?? "status";
            if (action == "overview") return System(new ToolArguments { Action="status" }) + "\n" + System(new ToolArguments { Action="processes" });
            if (action == "status")
            {
                string load = File.ReadAllText("/proc/loadavg").Split(' ')[0];
                string memory = string.Join("\n", File.ReadLines("/proc/meminfo").Where(line => line.StartsWith("MemTotal:", StringComparison.Ordinal) || line.StartsWith("MemAvailable:", StringComparison.Ordinal)));
                DriveInfo drive = new DriveInfo("/");
                return "Observed as current user. One-minute CPU load average (not CPU percent): " + load + ". Logical CPUs: " + Environment.ProcessorCount + ".\n" + memory + "\nRoot filesystem free bytes: " + drive.AvailableFreeSpace;
            }
            if (action == "processes")
            {
                List<object> rows = new List<object>();
                foreach (string directory in Directory.EnumerateDirectories("/proc"))
                {
                    if (rows.Count >= 128) break;
                    if (!int.TryParse(Path.GetFileName(directory), out int pid)) continue;
                    try { LinuxProcess.Identity(pid); rows.Add(new { pid, name = File.ReadAllText(directory + "/comm").Trim() }); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (InvalidOperationException) { }
                }
                return "Observed owned process names and IDs; no command lines or environment: " + JsonSerializer.Serialize(rows);
            }
            if (action != "service_failed" && action != "service_status" && action != "service_start" && action != "service_stop" && action != "service_restart") return "Unsupported Linux system action.";
            string unit = args.Name ?? "";
            if (action != "service_failed" && !Regex.IsMatch(unit, @"\A[A-Za-z0-9][A-Za-z0-9_.@:-]{0,120}\.service\z")) return "Choose one exact user .service unit.";
            using CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            if (args.Current != null && !args.Current()) return "The request was cancelled.";
            if (action == "service_failed") return Encoding.UTF8.GetString(ComputerProcess.Bytes("/usr/bin/systemctl", new[] { "--user", "--failed", "--no-pager", "--no-legend", "--plain" }, cancel.Token, 10000, 16000));
            string operation = action.Substring("service_".Length);
            if (operation != "status" && !ServiceRequested(args.Asked, operation, unit)) return "A service change needs an explicit start, stop or restart request naming that exact user service.";
            if (operation != "status") ComputerProcess.Bytes("/usr/bin/systemctl", new[] { "--user", operation, "--", unit }, cancel.Token, 10000, 16000);
            string observed = Encoding.UTF8.GetString(ComputerProcess.Bytes("/usr/bin/systemctl", new[] { "--user", "show", "--property=Id,LoadState,ActiveState,SubState,Result", "--no-pager", "--", unit }, cancel.Token, 10000, 16000));
            return "Observed user service after " + operation + ":\n" + observed;
        }
    }
}
