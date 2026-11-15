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

        internal static Func<string> CpuTimes = () => File.ReadLines("/proc/stat").First();
        internal static string PowerFolder = "/sys/class/power_supply";

        // The whole processor's busy share between two /proc/stat "cpu" lines, the way top counts it: waiting on disks is idle.
        internal static double? CpuBusy(string before, string after)
        {
            long[] first = CpuFields(before), second = CpuFields(after);
            if (first == null || second == null) return null;
            long total = 0, idle = second[3] - first[3] + second[4] - first[4];
            for (int i = 0; i < 8; i++) total += second[i] - first[i];
            if (total <= 0) return null;
            return Math.Max(0, Math.Min(100, (total - idle) * 100.0 / total));
        }

        private static long[] CpuFields(string line)
        {
            string[] parts = (line ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 9 || parts[0] != "cpu") return null;
            long[] fields = new long[8];
            for (int i = 0; i < 8; i++)
                if (!long.TryParse(parts[i + 1], NumberStyles.None, CultureInfo.InvariantCulture, out fields[i])) return null;
            return fields;
        }

        // The laptop's own batteries, in the words her Windows check uses, or null on a desktop. A mouse's or headset's battery
        // has the Device scope and isn't counted.
        internal static string Battery(string folder)
        {
            long now = 0, full = 0;
            int batteries = 0, percents = 0;
            bool supplied = false, plugged = false, discharging = false, measured = true;
            string units = null;
            string[] supplies;
            try { supplies = Directory.GetDirectories(folder); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            foreach (string supply in supplies)
            {
                if (Supply(supply, "scope") == "Device") continue;
                if (Supply(supply, "type") != "Battery")
                {
                    string online = Supply(supply, "online");
                    if (online == null) continue;
                    supplied = true;
                    plugged |= online == "1";
                    continue;
                }
                if (Supply(supply, "present") == "0" || !int.TryParse(Supply(supply, "capacity"), NumberStyles.None, CultureInfo.InvariantCulture, out int capacity)) continue;
                batteries++;
                percents += Math.Min(100, capacity);
                discharging |= Supply(supply, "status") == "Discharging";
                string unit = Stored(supply, "energy") != null ? "energy" : "charge";
                long[] stored = Stored(supply, unit);
                if (stored == null || units != null && units != unit) measured = false;
                else { now += stored[0]; full += stored[1]; units = unit; }
            }
            if (batteries == 0) return null;
            long percent = measured && batteries > 1 ? (long)Math.Round(Math.Min(100.0, now * 100.0 / full)) : (long)Math.Round((double)percents / batteries);
            return percent + "%" + ((supplied ? plugged : !discharging) ? ", plugged in" : ", on battery");
        }

        private static long[] Stored(string supply, string unit)
        {
            if (!long.TryParse(Supply(supply, unit + "_now"), NumberStyles.None, CultureInfo.InvariantCulture, out long now) || !long.TryParse(Supply(supply, unit + "_full"), NumberStyles.None, CultureInfo.InvariantCulture, out long full) || full <= 0) return null;
            return new[] { now, full };
        }

        private static string Supply(string supply, string name)
        {
            try { return File.ReadAllText(Path.Combine(supply, name)).Trim(); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        internal static string System(ToolArguments args)
        {
            if (!OperatingSystem.IsLinux()) return "Linux system tools are unavailable on this platform.";
            string action = args.Action ?? "status";
            if (action == "overview") return System(new ToolArguments { Action="status" }) + "\n" + System(new ToolArguments { Action="processes" });
            if (action == "status")
            {
                string before = CpuTimes();
                Thread.Sleep(500);
                double? busy = CpuBusy(before, CpuTimes());
                string load = File.ReadAllText("/proc/loadavg").Split(' ')[0];
                string memory = string.Join("\n", File.ReadLines("/proc/meminfo").Where(line => line.StartsWith("MemTotal:", StringComparison.Ordinal) || line.StartsWith("MemAvailable:", StringComparison.Ordinal)));
                DriveInfo drive = new DriveInfo("/");
                string battery = Battery(PowerFolder);
                return "Observed as current user. CPU busy over half a second: " + (busy == null ? "unknown" : Math.Round(busy.Value).ToString(CultureInfo.InvariantCulture) + "%") + ". One-minute CPU load average (not CPU percent): " + load + ". Logical CPUs: " + Environment.ProcessorCount + ".\n" + memory + "\nRoot filesystem free bytes: " + drive.AvailableFreeSpace + (battery == null ? "" : "\nBattery: " + battery + ".");
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

        internal static void Checks(List<string> checks)
        {
            Func<string> timesWas = CpuTimes;
            string powerWas = PowerFolder;
            Func<string, NetUse> netWas = Hands.Net;
            string linkFolder = Network.LinkFolder;
            bool macWas = Hands.OnMac;
            string folder = Path.Combine(Path.GetTempPath(), "capri-linux-status-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (CpuBusy("cpu  100 0 100 800 0 0 0 0 0 0", "cpu  150 0 150 850 50 0 0 0 0 0") != 50 || CpuBusy("cpu  1 2 3 4 5 6 7 8 9 9", "cpu  1 2 3 14 5 6 7 8 9 9") != 0
                    || CpuBusy("cpu  1 1 1 1 1 1 1 1", "cpu  1 1 1 1 1 1 1 1 0 0") != null || CpuBusy("cpu0 1 1 1 1 1 1 1 1 0 0", "cpu0 2 2 2 2 2 2 2 2 0 0") != null || CpuBusy("cpu  1 1 1 1 1 1 1 1 0 0", "cpu  1 1 1 1 1 1 1 1 0 0") != null)
                    throw new Exception("Linux CPU use was not read from two /proc/stat samples the way top counts it.");
                void Supply(string name, params string[] values)
                {
                    string supply = Path.Combine(folder, name);
                    Directory.CreateDirectory(supply);
                    for (int i = 0; i < values.Length; i += 2) File.WriteAllText(Path.Combine(supply, values[i]), values[i + 1] + "\n");
                }
                Directory.CreateDirectory(folder);
                Supply("AC", "type", "Mains", "online", "1");
                Supply("hidpp_battery_0", "type", "Battery", "scope", "Device", "capacity", "5", "status", "Discharging");
                if (Battery(folder) != null) throw new Exception("A desktop or a mouse battery was reported as the PC's battery.");
                Supply("BAT0", "type", "Battery", "present", "1", "capacity", "65", "status", "Charging", "energy_now", "42955000", "energy_full", "66007000");
                string plugged = Battery(folder);
                Supply("AC", "online", "0");
                string unplugged = Battery(folder);
                Supply("BAT1", "type", "Battery", "capacity", "100", "status", "Full", "energy_now", "22000000", "energy_full", "22000000");
                string both = Battery(folder);
                Directory.Delete(Path.Combine(folder, "AC"), true);
                string noMains = Battery(folder);
                Supply("BAT1", "status", "Discharging");
                string draining = Battery(folder);
                if (plugged != "65%, plugged in" || unplugged != "65%, on battery" || both != "74%, on battery" || noMains != "74%, plugged in" || draining != "74%, on battery")
                    throw new Exception("Linux battery read wrong: " + plugged + " / " + unplugged + " / " + both + " / " + noMains + " / " + draining);
                if (OperatingSystem.IsLinux())
                {
                    string[] samples = { "cpu  100 0 100 800 0 0 0 0 0 0", "cpu  150 0 150 850 50 0 0 0 0 0" };
                    int sample = 0;
                    CpuTimes = () => samples[sample++ % 2];
                    PowerFolder = folder;
                    string status = System(new ToolArguments { Action = "status" });
                    if (!status.StartsWith("Observed as current user. CPU busy over half a second: 50%. One-minute CPU load average (not CPU percent): ", StringComparison.Ordinal)
                        || !status.Contains("\nMemTotal:") || !status.Contains("\nRoot filesystem free bytes: ") || !status.EndsWith("\nBattery: 74%, on battery.", StringComparison.Ordinal))
                        throw new Exception("Linux status lost its CPU use, memory, disk or battery line: " + status);
                    CpuTimes = timesWas;
                    PowerFolder = powerWas;
                    status = System(new ToolArguments { Action = "status" });
                    string real = Battery(PowerFolder);
                    if (!Regex.IsMatch(status, @"\AObserved as current user\. CPU busy over half a second: (?:\d{1,3}%|unknown)\. ") || status.Contains("Battery: ") != (real != null))
                        throw new Exception("This PC's Linux status didn't read: " + status);
                    checks.Add("PASS Linux status reads CPU use over half a second from /proc/stat like top, keeps load, memory and disk, and adds the laptop's battery in her Windows words, none on a desktop or for a mouse; this PC: " + status.Split('\n')[0] + (real == null ? " No battery." : " Battery " + real + "."));
                }

                Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                Supply("wlan0", "operstate", "up", "type", "1");
                Directory.CreateDirectory(Path.Combine(folder, "wlan0", "device"));
                Directory.CreateDirectory(Path.Combine(folder, "wlan0", "wireless"));
                Supply("enp3s0", "operstate", "unknown", "carrier", "1", "type", "1");
                Directory.CreateDirectory(Path.Combine(folder, "enp3s0", "device"));
                Supply("enp4s0", "operstate", "down", "type", "1");
                Directory.CreateDirectory(Path.Combine(folder, "enp4s0", "device"));
                Supply("docker0", "operstate", "up", "type", "1");
                Supply("wg0", "operstate", "unknown", "type", "65534");
                Supply("lo", "operstate", "unknown", "type", "772");
                List<string> links = Network.LinuxLinks(folder);
                if (string.Join(",", links) != "Ethernet,Wi-Fi" && string.Join(",", links) != "Wi-Fi,Ethernet") throw new Exception("Linux links counted a virtual or unplugged link: " + string.Join(",", links));
                NetUse now = null;
                List<string> pinged = new List<string>();
                Hands.Net = address => { pinged.Add(address ?? ""); return now; };
                Func<NetUse, string, string> check = (use, address) => { now = use; return Hands.Do("internet_status", new ToolArguments { Address = address }); };
                NetUse working = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 0, Average = 14, Fastest = 12, Names = true };
                working.Links.Add("Wi-Fi");
                NetUse dropping = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 2, Average = 180, Fastest = 150, Names = true };
                dropping.Links.Add("Wi-Fi");
                NetUse down = new NetUse { Pinged = "8.8.8.8", Sent = 4, Lost = 4, Names = false };
                down.Links.Add("Ethernet");
                Hands.OnMac = false;
                string fine = check(working, null), drops = check(dropping, null), dead = check(down, null), offline = check(new NetUse(), null), unread = check(null, null), site = check(working, "google.com");
                if (fine != "Their internet is working, over Wi-Fi: pings to 1.1.1.1 took 14 ms on average, 12 ms at best, and web addresses look up fine."
                    || drops != "Their internet is dropping out, over Wi-Fi: 2 of 4 pings to 1.1.1.1 were lost, and the rest took 180 ms on average, 150 ms at best, and web addresses look up fine."
                    || dead != "Their PC is connected over Ethernet, but nothing answered: all 4 pings to 8.8.8.8 were lost, and web addresses don't look up either, so the internet looks down."
                    || offline != "Their PC isn't connected to any network: no Ethernet or Wi-Fi is up." || unread != "Couldn't check their internet just now." || site != fine || string.Join(",", pinged) != ",,,,,google.com")
                    throw new Exception("Linux internet check didn't answer in her Windows words: " + fine + " / " + drops + " / " + dead + " / " + offline + " / " + unread);
                Hands.Kit linux = Hands.KitFor();
                if (!linux.Has("internet_status") || !linux.Tools.Contains("\"name\":\"internet_status\"") || !Hands.Valid("internet_status", new ToolArguments()) || !Hands.Checking("internet_status", new ToolArguments()) || Security.Needs("internet_status", new ToolArguments()) != -1)
                    throw new Exception("Linux didn't offer the internet check as a check that never asks.");
                Hands.OnMac = true;
                pinged.Clear();
                Hands.Kit mac = Hands.KitFor();
                if (mac.Has("internet_status") || mac.Tools.Contains("\"name\":\"internet_status\"") || Hands.Do("internet_status", new ToolArguments()) != "I can't check your internet on a Mac yet." || pinged.Count != 0)
                    throw new Exception("Mac offered or ran the Linux internet check.");
                Network.LinkFolder = Path.Combine(folder, "missing");
                Hands.Net = Network.Read;
                Hands.OnMac = false;
                string unreadable = OperatingSystem.IsLinux() ? Hands.Do("internet_status", new ToolArguments()) : null;
                Network.LinkFolder = linkFolder;
                if (Network.LinuxLinks(Path.Combine(folder, "missing")) != null || OperatingSystem.IsLinux() && unreadable != "Couldn't check their internet just now.") throw new Exception("An unreadable /sys/class/net was taken for no network: " + unreadable);
                List<string> here = Network.Links();
                if (here == null) throw new Exception("This PC's network links couldn't be read.");
                checks.Add("PASS Linux internet check answers in her Windows words, counts only hardware Ethernet and Wi-Fi links from /sys/class/net, says it couldn't check when that folder can't be read, never asks first, and stays missing on Mac; the checks never ping out; this PC's links: " + (here.Count == 0 ? "none" : string.Join(", ", here)));
            }
            finally
            {
                CpuTimes = timesWas;
                PowerFolder = powerWas;
                Hands.Net = netWas;
                Network.LinkFolder = linkFolder;
                Hands.OnMac = macWas;
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
            }
        }
    }
}
