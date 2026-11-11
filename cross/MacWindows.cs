using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal static class MacWindows
    {
        internal sealed class Display
        {
            internal PixelRect Bounds, Work;
            internal bool Primary, Pointed;
        }

        private sealed class Window
        {
            internal int Pid, Index;
            internal string Title;
            internal PixelRect Bounds;
            internal bool Minimized;
            internal string Key { get { return Pid + "\t" + (Title.Length == 0 ? "index:" + Index : "title:" + Title); } }
        }

        private sealed class Saved
        {
            internal PixelRect Before, Last;
        }

        private static readonly string[] Actions = { "maximize", "restore", "snap_left", "snap_right", "snap_top", "snap_bottom", "move_to_screen", "move", "resize" };
        private static readonly string[] Spots = { "center", "top_left", "top_right", "bottom_left", "bottom_right" };
        private static readonly object gate = new object();
        private static readonly Dictionary<string, Saved> saved = new Dictionary<string, Saved>();
        private static string permission;
        private const int Milliseconds = 10000;
        internal static Func<ProcessStartInfo, int, string> Run = Command;
        internal static Func<List<Display>> Displays = () => new List<Display>();
        private const string Accessibility = "Turn Capri on in System Settings > Privacy & Security > Accessibility.";
        private const string Automation = "Allow Capri to control System Events in System Settings > Privacy & Security > Automation.";
        internal const string Timeout = "System Events didn't answer. If the Mac is waiting on a permission prompt, answer it and ask again.";
        private const string Title = "set titleText to name of w\nif titleText is missing value then set titleText to \"\"\nset titleText to titleText as text\n";
        private const string Reply = "set pt to position of w\nset sz to size of w\nset minimized to false\nif exists attribute \"AXMinimized\" of w then set minimized to value of attribute \"AXMinimized\" of w\n" + Title + "return (unix id of p as text) & tab & (item 1 of pt as text) & tab & (item 2 of pt as text) & tab & (item 1 of sz as text) & tab & (item 2 of sz as text) & tab & (minimized as text) & tab & (wi as text) & tab & titleText\n";

        private static ProcessStartInfo Start(string script)
        {
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add(script);
            return start;
        }

        private static string Command(ProcessStartInfo start, int milliseconds)
        {
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (process == null) return null;
                    Task<string> output = process.StandardOutput.ReadToEndAsync(), error = process.StandardError.ReadToEndAsync();
                    var clock = Stopwatch.StartNew();
                    if (!process.WaitForExit(milliseconds)) { process.Kill(); return "timeout"; }
                    int remaining = Math.Max(0, milliseconds - (int)clock.ElapsedMilliseconds);
                    if (!Task.WaitAll(new Task[] { output, error }, remaining)) return "timeout";
                    return process.ExitCode == 0 ? output.Result : error.Result;
                }
            }
            catch (Exception) { return null; }
        }

        internal static List<Display> ReadDisplays(IReadOnlyList<Screen> screens, PixelPoint? mouse)
        {
            List<Display> found = new List<Display>();
            foreach (Screen screen in screens)
                found.Add(ReadDisplay(screen.Bounds, screen.WorkingArea, screen.Scaling, screen.IsPrimary, mouse));
            return found;
        }

        private static Display ReadDisplay(PixelRect bounds, PixelRect work, double scaling, bool primary, PixelPoint? mouse)
        {
            return new Display { Bounds = Points(bounds, scaling), Work = Points(work, scaling), Primary = primary, Pointed = mouse.HasValue && bounds.Contains(mouse.Value) };
        }

        private static PixelRect Points(PixelRect rect, double scaling)
        {
            return new PixelRect((int)Math.Round(rect.X / scaling), (int)Math.Round(rect.Y / scaling), (int)Math.Round(rect.Width / scaling), (int)Math.Round(rect.Height / scaling));
        }

        private static string Wrapped(string body)
        {
            return "try\ntell application \"System Events\"\nif not UI elements enabled then return \"accessibility\"\n" + body +
                "end tell\non error msg number n\nif n is -1743 then return \"automation\"\nif n is -25211 or n is -1719 or msg contains \"assistive access\" then return \"accessibility\"\nreturn \"error:\" & n\nend try";
        }

        private static string ReadScript(string name)
        {
            return Wrapped("set wanted to " + MacNative.AppleString(name) + "\nset stalled to false\nrepeat with rank from 1 to 3\nrepeat with p in (every application process whose background only is false)\ntry\n" +
                "if unix id of p is not " + Environment.ProcessId + " then\nwith timeout of 2 seconds\nrepeat with wi from 1 to (count of windows of p)\ntry\nset w to window wi of p\nset appName to name of p as text\n" + Title + "ignoring case\n" +
                "set fits to (rank is 1 and appName is wanted) or (rank is 2 and titleText is wanted) or (rank is 3 and (appName contains wanted or titleText contains wanted))\nend ignoring\n" +
                "if fits then\n" + Reply + "end if\non error msg number n\nif n is -1743 or n is -25211 then error msg number n\nif n is -1712 then set stalled to true\nend try\nend repeat\nend timeout\nend if\non error msg number n\nif n is -1743 or n is -25211 then error msg number n\nif n is -1712 then set stalled to true\nend try\nend repeat\nend repeat\nif stalled then return \"timeout\"\nreturn \"missing\"\n");
        }

        private static string PlaceScript(Window window, PixelRect to)
        {
            PixelRect before = window.Bounds;
            bool size = before.Width != to.Width || before.Height != to.Height;
            string find = window.Title.Length == 0 ? "if not (exists window wi of p) then return \"changed\"\nset w to window wi of p\n" + Title + "if titleText is not \"\" then return \"changed\"\n" :
                "set hits to every window of p whose name is " + MacNative.AppleString(window.Title) + "\nif (count of hits) is not 1 then return \"ambiguous\"\nset w to item 1 of hits\n";
            return Wrapped("set p to first application process whose unix id is " + window.Pid + "\nset wi to " + window.Index + "\n" + find +
                "if (position of w is not {" + before.X + ", " + before.Y + "}) or (size of w is not {" + before.Width + ", " + before.Height + "}) then return \"changed\"\n" +
                "if exists attribute \"AXFullScreen\" of w then\nif value of attribute \"AXFullScreen\" of w then return \"fullscreen\"\nend if\n" +
                (size ? "if not (settable of attribute \"AXSize\" of w) then return \"fixed\"\n" : "") +
                "set visible of p to true\nif exists attribute \"AXMinimized\" of w then\nif value of attribute \"AXMinimized\" of w then set value of attribute \"AXMinimized\" of w to false\nend if\n" +
                (size && (before.X != to.X || before.Y != to.Y) ? "set position of w to {" + to.X + ", " + to.Y + "}\n" : "") +
                (size ? "set size of w to {" + to.Width + ", " + to.Height + "}\n" : "") +
                "set position of w to {" + to.X + ", " + to.Y + "}\ndelay 0.15\n" + Reply);
        }

        private static Window Parse(string text)
        {
            if (text == null) return null;
            string[] parts = text.TrimEnd('\r', '\n').Split(new[] { '\t' }, 8);
            if (parts.Length != 8) return null;
            int[] n = new int[5];
            for (int i = 0; i < n.Length; i++) if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out n[i])) return null;
            if (n[0] <= 0 || n[3] <= 0 || n[4] <= 0 || parts[5] != "true" && parts[5] != "false") return null;
            int index;
            if (!int.TryParse(parts[6], out index) || index < 1) return null;
            return new Window { Index = index, Pid = n[0], Bounds = new PixelRect(n[1], n[2], n[3], n[4]), Minimized = parts[5] == "true", Title = parts[7] };
        }

        private static string Problem(string text)
        {
            string code = (text ?? "").Trim();
            if (Regex.IsMatch(code, @"(?<!\d)-(?:1719|25211)(?!\d)")) code = "accessibility";
            if (code == "accessibility" || code == "automation")
            {
                bool again = permission == code;
                permission = code;
                return again ? "Window arranging still needs " + (code == "accessibility" ? "Accessibility." : "Automation.") : code == "accessibility" ? Accessibility : Automation;
            }
            permission = null;
            if (code == "timeout") return Timeout;
            if (code == "fixed") return "That app won't let me resize its window.";
            if (code == "fullscreen") return "Take that window out of full screen before arranging it.";
            if (code == "ambiguous") return "Those windows have the same title. Give one a different title first.";
            if (code == "changed") return "That window moved while I was checking it. Ask again.";
            if (code == "missing") return "I couldn't find an open window with that name.";
            return "I couldn't arrange that window. The app may have refused the change.";
        }

        internal static Spot Perch(string name, out string problem)
        {
            lock (gate)
            {
                string reply = Run(Start(ReadScript(name)), Milliseconds);
                Window window = Parse(reply);
                if (window != null) permission = null;
                problem = window == null ? Problem(reply) : window.Minimized ? "That window is minimized. Open it first." : null;
                return problem == null ? new Spot { Pid = window.Pid, Bounds = window.Bounds } : null;
            }
        }

        internal static string Request(ToolArguments args)
        {
            if (args == null || Array.IndexOf(Actions, args.Action) < 0) return null;
            string name = Regex.Replace((args.Name ?? "").Trim(), @"^the |( app| application| game| tab| window)$", "", RegexOptions.IgnoreCase);
            if (string.IsNullOrWhiteSpace(name)) return "Which app?";
            lock (gate)
            {
                string read = Run(Start(ReadScript(name)), Milliseconds);
                Window window = Parse(read);
                if (window == null) return Problem(read);
                permission = null;
                List<Display> screens = Displays();
                if (screens == null || screens.Count == 0) return "I couldn't read your screens.";
                Saved remembered;
                saved.TryGetValue(window.Key, out remembered);
                if (remembered != null && remembered.Last != window.Bounds) { saved.Remove(window.Key); remembered = null; }
                if (args.Action == "restore" && remembered == null && !window.Minimized) return "I don't have earlier bounds for that window.";
                PixelRect to;
                string error = Target(window.Bounds, screens, args, remembered, out to);
                if (error != null) return error;
                if (args.Current != null && !args.Current()) return "The window request was cancelled.";
                string placed = Run(Start(PlaceScript(window, to)), Milliseconds);
                Window after = Parse(placed);
                if (after == null) return Problem(placed);
                if (after.Key != window.Key) return "I couldn't verify which window changed.";
                if (args.Action == "maximize" || args.Action.StartsWith("snap_", StringComparison.Ordinal))
                {
                    if (remembered == null) remembered = new Saved { Before = window.Bounds };
                    remembered.Last = after.Bounds;
                    saved[window.Key] = remembered;
                }
                else if (args.Action == "restore" && after.Bounds == to && !after.Minimized) saved.Remove(window.Key);
                else if (remembered != null) remembered.Last = after.Bounds;
                if (after.Minimized) return "That window stayed minimized.";
                string actual = after.Bounds.Width + " x " + after.Bounds.Height + " at " + after.Bounds.X + ", " + after.Bounds.Y + ".";
                if (after.Bounds != to) return "That app wouldn't take the requested bounds. Its window is " + actual;
                return (args.Action == "restore" ? "Restored" : args.Action == "maximize" ? "Maximized" : args.Action.StartsWith("snap_", StringComparison.Ordinal) ? "Snapped" : args.Action == "resize" ? "Resized" : "Moved") + " the window: " + actual;
            }
        }

        private static string Target(PixelRect before, List<Display> screens, ToolArguments args, Saved remembered, out PixelRect to)
        {
            to = before;
            string action = args.Action;
            if (action == "restore")
            {
                if (remembered != null) to = remembered.Before;
                return null;
            }
            int on = Under(before, screens), at = on;
            if (!string.IsNullOrWhiteSpace(args.Screen) || action == "move_to_screen") at = Pick(screens, on, args.Screen);
            if (at < 0) return "I couldn't find that screen.";
            if (action == "move_to_screen" && screens.Count < 2) return "There's only one screen.";
            PixelRect work = screens[at].Work;
            PixelRect from = at == on ? before : Carried(before, screens[on].Work, work);
            if (action == "maximize") to = work;
            else if (action.StartsWith("snap_", StringComparison.Ordinal))
            {
                int halfWide = work.Width / 2, halfHigh = work.Height / 2;
                string side = action.Substring(5);
                to = side == "left" ? new PixelRect(work.X, work.Y, halfWide, work.Height) : side == "right" ? new PixelRect(work.X + halfWide, work.Y, work.Width - halfWide, work.Height) :
                    side == "top" ? new PixelRect(work.X, work.Y, work.Width, halfHigh) : new PixelRect(work.X, work.Y + halfHigh, work.Width, work.Height - halfHigh);
            }
            else if (action == "move_to_screen") to = from;
            else if (action == "move")
            {
                int width = Math.Min(from.Width, work.Width), height = Math.Min(from.Height, work.Height);
                string spot = (args.Position ?? "").Trim().ToLowerInvariant().Replace(' ', '_').Replace("centre", "center");
                int x = from.X, y = from.Y;
                if (Array.IndexOf(Spots, spot) >= 0)
                {
                    x = spot.EndsWith("left", StringComparison.Ordinal) ? work.X : spot.EndsWith("right", StringComparison.Ordinal) ? work.Right - width : work.X + (work.Width - width) / 2;
                    y = spot.StartsWith("top", StringComparison.Ordinal) ? work.Y : spot.StartsWith("bottom", StringComparison.Ordinal) ? work.Bottom - height : work.Y + (work.Height - height) / 2;
                }
                else if (!string.IsNullOrWhiteSpace(args.Left) || !string.IsNullOrWhiteSpace(args.Top))
                {
                    int? left = Measure(args.Left, from.X - work.X, work.Width, true), top = Measure(args.Top, from.Y - work.Y, work.Height, true);
                    if (!left.HasValue || !top.HasValue) return "Use pixels, a percent, or a change like +100 for left and top.";
                    x = work.X + left.Value; y = work.Y + top.Value;
                }
                else if (at == on) return "Give a spot like center, a screen, or left and top.";
                to = Inside(new PixelRect(x, y, width, height), work);
            }
            else if (action == "resize")
            {
                int? w = Measure(args.Width, from.Width, work.Width, false), h = Measure(args.Height, from.Height, work.Height, false);
                if (!w.HasValue || !h.HasValue) return "Use pixels, a percent, or a change like +20% for width and height.";
                int width = Math.Min(work.Width, Math.Max(160, w.Value)), height = Math.Min(work.Height, Math.Max(160, h.Value));
                to = Inside(new PixelRect(from.X + from.Width / 2 - width / 2, from.Y + from.Height / 2 - height / 2, width, height), work);
            }
            return null;
        }

        private static int? Measure(string said, int now, int whole, bool place)
        {
            string text = Regex.Replace((said ?? "").Trim().ToLowerInvariant(), @"\s+|px$|pixels?$", "");
            if (text.Length == 0) return now;
            Match found = Regex.Match(text, @"^(?<sign>[+-])?(?<n>\d{1,5}(?:\.\d+)?)(?<percent>%)?$");
            if (!found.Success) return null;
            double n = double.Parse(found.Groups["n"].Value, CultureInfo.InvariantCulture);
            string sign = found.Groups["sign"].Value;
            double amount = found.Groups["percent"].Success ? (sign.Length > 0 && !place ? now : whole) * n / 100 : n;
            return (int)Math.Round(sign == "+" ? now + amount : sign == "-" ? now - amount : amount);
        }

        private static PixelRect Inside(PixelRect bounds, PixelRect work)
        {
            return new PixelRect(Math.Max(work.X, Math.Min(bounds.X, work.Right - bounds.Width)), Math.Max(work.Y, Math.Min(bounds.Y, work.Bottom - bounds.Height)), bounds.Width, bounds.Height);
        }

        private static PixelRect Carried(PixelRect bounds, PixelRect a, PixelRect b)
        {
            bool filling = (Math.Abs(bounds.Width - a.Width) <= 8 || Math.Abs(bounds.Width - a.Width / 2) <= 8) && (Math.Abs(bounds.Height - a.Height) <= 8 || Math.Abs(bounds.Height - a.Height / 2) <= 8) &&
                (Math.Abs(bounds.X - a.X) <= 8 || Math.Abs(bounds.Right - a.Right) <= 8) && (Math.Abs(bounds.Y - a.Y) <= 8 || Math.Abs(bounds.Bottom - a.Bottom) <= 8);
            if (filling)
            {
                int x = b.X + (int)Math.Round((bounds.X - a.X) * (double)b.Width / a.Width), y = b.Y + (int)Math.Round((bounds.Y - a.Y) * (double)b.Height / a.Height);
                int right = b.X + (int)Math.Round((bounds.Right - a.X) * (double)b.Width / a.Width), bottom = b.Y + (int)Math.Round((bounds.Bottom - a.Y) * (double)b.Height / a.Height);
                return new PixelRect(x, y, right - x, bottom - y);
            }
            int width = Math.Min(bounds.Width, b.Width), height = Math.Min(bounds.Height, b.Height);
            double across = a.Width > bounds.Width ? (bounds.X - a.X) / (double)(a.Width - bounds.Width) : 0.5, down = a.Height > bounds.Height ? (bounds.Y - a.Y) / (double)(a.Height - bounds.Height) : 0.5;
            return new PixelRect(b.X + (int)Math.Round((b.Width - width) * Math.Clamp(across, 0, 1)), b.Y + (int)Math.Round((b.Height - height) * Math.Clamp(down, 0, 1)), width, height);
        }

        private static int Under(PixelRect bounds, List<Display> screens)
        {
            int best = 0;
            long most = -1;
            double nearest = double.MaxValue;
            for (int i = 0; i < screens.Count; i++)
            {
                PixelRect screen = screens[i].Bounds;
                long area = (long)Math.Max(0, Math.Min(bounds.Right, screen.Right) - Math.Max(bounds.X, screen.X)) * Math.Max(0, Math.Min(bounds.Bottom, screen.Bottom) - Math.Max(bounds.Y, screen.Y));
                double dx = bounds.X + bounds.Width / 2.0 - (screen.X + screen.Width / 2.0), dy = bounds.Y + bounds.Height / 2.0 - (screen.Y + screen.Height / 2.0);
                double distance = dx * dx + dy * dy;
                if (area > most || area == most && distance < nearest) { best = i; most = area; nearest = distance; }
            }
            return best;
        }

        private static int Pick(List<Display> screens, int current, string which)
        {
            string said = Regex.Replace(Regex.Replace((which ?? "").Trim().ToLowerInvariant(), @"\b(?:the|my|your|ur|monitor|screen|display)\b", " "), @"\s+", " ").Trim();
            if (Regex.IsMatch(said, @"^(portrait|vertical|tall|upright|rotated|landscape|horizontal|wide|widescreen)(?: one)?$"))
            {
                bool tall = Regex.IsMatch(said, @"^(portrait|vertical|tall|upright|rotated)");
                int found = -1;
                for (int i = 0; i < screens.Count; i++) if ((screens[i].Bounds.Height > screens[i].Bounds.Width) == tall && (found < 0 || found == current)) found = i;
                return found;
            }
            if (said.Length > 0 && !Regex.IsMatch(said, @"^(next|other|another|previous|back|last|here|this|mouse|cursor|pointer|main|primary|left|right|top|upper|up|above|bottom|lower|down|below|first|1st|one|second|2nd|two|third|3rd|three|fourth|4th|four|[1-9])$")) return -1;
            int[] x = new int[screens.Count], y = new int[screens.Count];
            int primary = 0, pointed = current;
            for (int i = 0; i < screens.Count; i++)
            {
                PixelRect bounds = screens[i].Bounds;
                x[i] = bounds.X + bounds.Width / 2; y[i] = bounds.Y + bounds.Height / 2;
                if (screens[i].Primary) primary = i;
                if (screens[i].Pointed) pointed = i;
            }
            return Hands.PickScreen(x, y, null, primary, current, pointed, said);
        }

        internal static void Checks(List<string> checks)
        {
            var run = Run;
            Func<List<Display>> displays = Displays;
            Func<ToolArguments, string> native = Hands.NativeWindow;
            Func<bool> terminal = Hands.TerminalAvailable;
            bool mac = Hands.OnMac;
            string notice = permission;
            Dictionary<string, Saved> beforeSaved = new Dictionary<string, Saved>(saved);
            try
            {
                saved.Clear();
                permission = null;
                List<Display> screens = new List<Display>
                {
                    ReadDisplay(new PixelRect(0, 0, 2880, 1800), new PixelRect(0, 50, 2880, 1650), 2, true, new PixelPoint(2000, 1200)),
                    ReadDisplay(new PixelRect(-1200, -100, 1200, 1000), new PixelRect(-1200, -75, 1200, 975), 1, false, new PixelPoint(2000, 1200))
                };
                if (screens[0].Bounds != new PixelRect(0, 0, 1440, 900) || screens[0].Work != new PixelRect(0, 25, 1440, 825) || !screens[0].Pointed || screens[1].Bounds != new PixelRect(-1200, -100, 1200, 1000) || screens[1].Pointed) throw new Exception("Mac arranging did not convert each display to points or kept the pointer in points.");
                Displays = () => screens;
                Window window = new Window { Pid = 123, Index = 1, Title = "Notes \"tonight\"", Bounds = new PixelRect(100, 125, 600, 400) };
                PixelRect original = window.Bounds;
                string emitted = null, failure = null;
                bool refuse = false, timeOutPlace = false;
                int writes = 0, calls = 0;
                Run = (start, milliseconds) =>
                {
                    calls++;
                    if (start.FileName != "/usr/bin/osascript" || start.ArgumentList.Count != 2 || start.ArgumentList[0] != "-e" || milliseconds != 10000) throw new Exception("Mac arranging lost its command or ten second timeout.");
                    string script = start.ArgumentList[1];
                    if (!script.Contains("UI elements enabled") || !script.Contains("n is -1743")) throw new Exception("Mac arranging lost its permission checks.");
                    if (failure != null) return failure;
                    if (script.Contains("set p to first application process"))
                    {
                        writes++;
                        emitted = script;
                        if (window.Title.Length > 0 && (!script.Contains("unix id is 123") || !script.Contains("whose name is \"Notes \\\"tonight\\\"\"") || !script.Contains("count of hits") || !script.Contains("position of w is not"))) throw new Exception("Mac arranging did not bind its quoted window.");
                        Match size = Regex.Match(script, @"set size of w to \{(-?\d+), (-?\d+)\}");
                        Match position = Regex.Match(script, @"set position of w to \{(-?\d+), (-?\d+)\}");
                        if (!position.Success) throw new Exception("Mac arranging did not set a position.");
                        if (!refuse) window.Bounds = new PixelRect(int.Parse(position.Groups[1].Value), int.Parse(position.Groups[2].Value), size.Success ? int.Parse(size.Groups[1].Value) : window.Bounds.Width, size.Success ? int.Parse(size.Groups[2].Value) : window.Bounds.Height);
                        window.Minimized = false;
                        if (timeOutPlace) return "timeout";
                    }
                    return window.Pid + "\t" + window.Bounds.X + "\t" + window.Bounds.Y + "\t" + window.Bounds.Width + "\t" + window.Bounds.Height + "\t" + (window.Minimized ? "true" : "false") + "\t" + window.Index + "\t" + window.Title;
                };
                Hands.OnMac = true;
                Hands.TerminalAvailable = () => false;
                Hands.NativeWindow = Request;
                foreach (string action in Actions)
                {
                    if (!Hands.KitFor().Has("app_window:" + action) || Hands.Unable("app_window", new ToolArguments { Action = action }) != null) throw new Exception("Mac arranging stayed missing: " + action);
                }
                foreach (string action in new[] { "pin_on_top", "unpin" })
                    if (Hands.KitFor().Has("app_window:" + action) || Hands.Unable("app_window", new ToolArguments { Action = action }) != "macOS doesn't let me pin another app's window on top.") throw new Exception("Mac pinning lost its gap.");
                int noPinCalls = calls;
                if (Request(new ToolArguments { Action = "pin_on_top" }) != null || Request(new ToolArguments { Action = "unpin" }) != null || calls != noPinCalls) throw new Exception("Mac pinning tried a command.");
                foreach (bool left in new[] { false, true })
                {
                    PixelRect area = screens[left ? 1 : 0].Work;
                    foreach (string action in new[] { "maximize", "snap_left", "snap_right", "snap_top", "snap_bottom", "move", "resize", "move_to_screen" })
                    {
                        window.Bounds = original;
                        saved.Clear();
                        ToolArguments args = new ToolArguments { Name = "Notes", Action = action, Screen = left ? "left" : "main", Position = "bottom_right", Width = "50%", Height = "+20%" };
                        PixelRect expected;
                        if (action == "maximize") expected = area;
                        else if (action == "snap_left") expected = new PixelRect(area.X, area.Y, area.Width / 2, area.Height);
                        else if (action == "snap_right") expected = new PixelRect(area.X + area.Width / 2, area.Y, area.Width - area.Width / 2, area.Height);
                        else if (action == "snap_top") expected = new PixelRect(area.X, area.Y, area.Width, area.Height / 2);
                        else if (action == "snap_bottom") expected = new PixelRect(area.X, area.Y + area.Height / 2, area.Width, area.Height - area.Height / 2);
                        else if (action == "move") expected = new PixelRect(area.Right - 600, area.Bottom - 400, 600, 400);
                        else if (action == "move_to_screen") expected = left ? new PixelRect(-1129, 60, 600, 400) : original;
                        else expected = left ? new PixelRect(-1129, 20, 600, 480) : new PixelRect(40, 85, 720, 480);
                        string result = Hands.Do("app_window", args);
                        if (window.Bounds != expected || result == null || !emitted.Contains("set position of w to {" + expected.X + ", " + expected.Y + "}")) throw new Exception("Mac " + action + " missed its work area: " + window.Bounds + " expected " + expected);
                        if (action == "maximize" && !left && !emitted.Contains("set size of w to {1440, 825}")) throw new Exception("Mac Retina maximize did not set its point size.");
                        if (action == "maximize" || action.StartsWith("snap_", StringComparison.Ordinal))
                        {
                            Request(new ToolArguments { Name = "Notes", Action = "restore" });
                            if (window.Bounds != original || saved.Count != 0) throw new Exception("Mac restore lost the bounds before " + action);
                        }
                    }
                }
                checks.Add("PASS Mac arranging: scripts for maximize, all four halves, move, resize and screen transfers respect the menu bar, Dock and a screen to the left; restore remembers bounds");
                window.Bounds = original;
                saved.Clear();
                int beforeRestore = writes;
                if (Request(new ToolArguments { Name = "Notes", Action = "restore" }) != "I don't have earlier bounds for that window." || writes != beforeRestore) throw new Exception("Mac restore claimed bounds it did not remember.");
                window.Minimized = true;
                Request(new ToolArguments { Name = "Notes", Action = "restore" });
                if (window.Minimized || window.Bounds != original) throw new Exception("Mac restore did not bring a minimized window back up.");
                Request(new ToolArguments { Name = "Notes", Action = "maximize" });
                Request(new ToolArguments { Name = "Notes", Action = "snap_right" });
                Request(new ToolArguments { Name = "Notes", Action = "restore" });
                if (window.Bounds != original) throw new Exception("Mac snapping overwrote the original restore bounds.");
                Request(new ToolArguments { Name = "Notes", Action = "move", Left = "+100", Top = "25%" });
                if (window.Bounds != new PixelRect(200, 231, 600, 400)) throw new Exception("Mac move did not read relative pixels and percentages.");
                if (Measure("-20%", 500, 1440, false) != 400 || Measure("200px", 0, 1440, true) != 200 || Measure("nonsense", 0, 1440, true) != null) throw new Exception("Mac measures differ from Windows.");
                int stopped = writes;
                Request(new ToolArguments { Name = "Notes", Action = "move", Screen = "missing" });
                Request(new ToolArguments { Name = "Notes", Action = "resize", Width = "oops" });
                Request(new ToolArguments { Name = "Notes", Action = "maximize", Current = () => false });
                if (writes != stopped) throw new Exception("Invalid or cancelled Mac arranging changed a window.");
                window.Bounds = original;
                refuse = true;
                if (!Request(new ToolArguments { Name = "Notes", Action = "resize", Width = "800" }).Contains("wouldn't take")) throw new Exception("A Mac resize refusal was reported as success.");
                refuse = false;
                failure = "accessibility";
                if (Request(new ToolArguments { Name = "Notes", Action = "maximize" }) != Accessibility || Request(new ToolArguments { Name = "Notes", Action = "maximize" }) == Accessibility) throw new Exception("Mac Accessibility guidance was missing or repeated.");
                failure = "automation";
                if (Request(new ToolArguments { Name = "Notes", Action = "maximize" }) != Automation) throw new Exception("Mac Automation guidance was missing.");
                failure = null;
                if (Problem("fixed") != "That app won't let me resize its window." || Parse("123\t0\t0\tbad") != null) throw new Exception("Mac arranging accepted an unreadable reply or hid a fixed window.");
                Window quoted = Parse("123\t-100\t25\t600\t400\tfalse\t1\tA \"quote\"\tand a tab\n");
                if (quoted == null || quoted.Title != "A \"quote\"\tand a tab" || !ReadScript("A \"quote\"").Contains("set wanted to \"A \\\"quote\\\"\"")) throw new Exception("Mac arranging lost quotes in a title.");
                permission = null;
                if (Problem("error:-1719") != Accessibility || !Wrapped("").Contains("n is -1719")) throw new Exception("Mac arranging missed Accessibility error -1719.");
                failure = "timeout";
                if (!Request(new ToolArguments { Name = "Notes", Action = "maximize" }).Contains("ask again")) throw new Exception("Mac arranging hid a timeout.");
                failure = null;
                timeOutPlace = true;
                window.Bounds = original;
                if (!Request(new ToolArguments { Name = "Notes", Action = "maximize" }).Contains("System Events didn't answer") || window.Bounds != screens[0].Work) throw new Exception("A timeout after moving a window became an app refusal.");
                timeOutPlace = false;
                window.Title = "";
                window.Index = 2;
                window.Bounds = original;
                string untitled = Request(new ToolArguments { Name = "Notes", Action = "maximize" });
                if (!untitled.StartsWith("Maximized") || !emitted.Contains("set wi to 2") || !emitted.Contains("set w to window wi of p") || emitted.Contains("ambiguous") || !emitted.Contains("if exists attribute \"AXMinimized\" of w then\nif value")) throw new Exception("Mac arranging lost an untitled or non-minimizable window.");
                string readScript = ReadScript("Notes");
                if (!readScript.Contains("repeat with wi from 1 to (count of windows of p)\ntry\nset w to window wi of p") || !readScript.Contains("end try\nend repeat\nend timeout") || readScript.Contains("n is -1719 then error") || !readScript.Contains("if n is -1743 or n is -25211 then error msg number n")) throw new Exception("Mac window search treated a closed window as a permission failure.");
                checks.Add("PASS Mac arranging: per-screen Retina points, pixel pointer selection and maximize size; vanished window indices are skipped inside the walk");
                if (!readScript.Contains("false)\ntry\n") || !readScript.Contains("end try\nend repeat") || !readScript.Contains("if titleText is missing value") || !readScript.Contains("if exists attribute \"AXMinimized\" of w then")) throw new Exception("Mac window search lost its process guard or optional attributes.");
                if (Parse("123\t0\t25\t600\t400\tfalse\t2\t\n").Title != "" || Milliseconds != 10000) throw new Exception("Mac arranging lost untitled replies or its timeout.");
                checks.Add("PASS Mac arranging: ten second runner, timeout guidance, process guards, Accessibility -1719, optional minimization and untitled window indices");
                checks.Add("PASS Mac arranging: quoted titles, relative measures, cancellation, refused bounds and permission guidance use fake commands");
                Hands.OnMac = false;
                foreach (string action in Actions)
                    if (Hands.KitFor().Has("app_window:" + action) || Hands.Unable("app_window", new ToolArguments { Action = action }) != Hands.ArrangeLine) throw new Exception("Linux lost its arranging gap: " + action);
                foreach (string action in new[] { "pin_on_top", "unpin" })
                    if (Hands.KitFor().Has("app_window:" + action) || Hands.Unable("app_window", new ToolArguments { Action = action }) != Hands.ArrangeLine) throw new Exception("Linux lost its pinning gap.");
                checks.Add("PASS Mac arranging: native dispatch is wired, pinning stays missing on Mac, and Linux keeps its existing gaps");
            }
            finally
            {
                Run = run;
                Displays = displays;
                Hands.NativeWindow = native;
                Hands.TerminalAvailable = terminal;
                Hands.OnMac = mac;
                permission = notice;
                saved.Clear();
                foreach (KeyValuePair<string, Saved> item in beforeSaved) saved.Add(item.Key, item.Value);
            }
        }
    }
}
