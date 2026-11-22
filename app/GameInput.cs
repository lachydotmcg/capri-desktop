using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class AntiCheat
    {
        private static readonly string[,] Known =
        {
            { "vgc", "Riot Vanguard" }, { "vgk", "Riot Vanguard" },
            { "EasyAntiCheat", "Easy Anti-Cheat" }, { "EasyAntiCheat_EOS", "Easy Anti-Cheat" },
            { "BEService", "BattlEye" }, { "BEService_x64", "BattlEye" }, { "BEDaisy", "BattlEye" },
            { "EAAntiCheat.GameServiceLauncher", "EA Javelin" }, { "EAAntiCheatService", "EA Javelin" },
            { "FACEIT", "FACEIT" }, { "FACEITService", "FACEIT" },
            { "ESEADriver2", "ESEA" },
            { "SGuard64", "ACE" }, { "SGuardSvc64", "ACE" }, { "ACE-Tray", "ACE" },
            { "GameMon", "nProtect GameGuard" }, { "GameMon64", "nProtect GameGuard" },
            { "xhunter1", "XIGNCODE3" },
            { "mhyprot2", "mhyprot" }, { "mhyprot3", "mhyprot" },
            { "cod", "Ricochet" }, { "ModernWarfare", "Ricochet" }, { "BlackOps6", "Ricochet" }, { "BlackOpsColdWar", "Ricochet" }, { "Vanguard", "Ricochet" }
        };

        private static readonly string[] Drivers = { "vgk", "vgc", "BEDaisy", "BEService", "EasyAntiCheat", "EasyAntiCheat_EOS", "FACEIT", "ESEADriver2", "xhunter1", "mhyprot2", "mhyprot3" };

        internal static Func<string[]> Processes = delegate
        {
            List<string> names = new List<string>();
            Process[] running;
            try { running = Process.GetProcesses(); }
            catch (InvalidOperationException) { return new string[0]; }
            foreach (Process process in running)
            {
                try { names.Add(process.ProcessName); }
                catch (InvalidOperationException) { }
                finally { process.Dispose(); }
            }
            return names.ToArray();
        };

        [StructLayout(LayoutKind.Sequential)]
        private struct ServiceStatus { public int Type, State, Accepted, ExitCode, SpecificExitCode, CheckPoint, WaitHint; }

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenSCManager(string machine, string database, int access);
        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr OpenService(IntPtr manager, string name, int access);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool QueryServiceStatus(IntPtr service, out ServiceStatus status);
        [DllImport("advapi32.dll", SetLastError = true)] private static extern bool CloseServiceHandle(IntPtr handle);

        internal static Func<string, bool> Loaded = delegate(string name)
        {
            IntPtr manager = OpenSCManager(null, null, 0x0001);
            if (manager == IntPtr.Zero) return false;
            try
            {
                IntPtr service = OpenService(manager, name, 0x0004);
                if (service == IntPtr.Zero) return false;
                try
                {
                    ServiceStatus status;
                    return QueryServiceStatus(service, out status) && status.State != 1;
                }
                finally { CloseServiceHandle(service); }
            }
            finally { CloseServiceHandle(manager); }
        };

        internal static string Running()
        {
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in Processes() ?? new string[0]) if (name != null) names.Add(name);
            for (int i = 0; i < Known.GetLength(0); i++)
                if (names.Contains(Known[i, 0])) return Known[i, 1];
            foreach (string name in names)
                if (name.StartsWith("EasyAntiCheat", StringComparison.OrdinalIgnoreCase)) return "Easy Anti-Cheat";
                else if (name.StartsWith("EAAntiCheat", StringComparison.OrdinalIgnoreCase)) return "EA Javelin";
            foreach (string driver in Drivers)
            {
                bool on;
                try { on = Loaded(driver); }
                catch (Exception) { on = false; }
                if (!on) continue;
                for (int i = 0; i < Known.GetLength(0); i++)
                    if (string.Equals(Known[i, 0], driver, StringComparison.OrdinalIgnoreCase)) return Known[i, 1];
            }
            return null;
        }

        internal static string Refusal(string name) { return "Won't do quick moves while " + name + " is running: anti-cheat like that can ban for them."; }
    }

    internal static partial class Computer
    {
        internal const int QuickMostSteps = 12;
        internal const double QuickLongest = 8;

        internal sealed class QuickStep
        {
            internal string Chord = "";
            internal short[] Codes = new short[0];
            internal string Button;
            internal double Seconds;
            internal bool Waiting;
            internal string Said;
        }

        private static string Clicker(string name)
        {
            switch (name)
            {
                case "click": case "left_click": case "lmb": case "mouse1": return "left";
                case "right_click": case "rmb": case "mouse2": return "right";
                case "middle_click": case "mmb": case "mouse3": return "middle";
            }
            return null;
        }

        private static string Secs(double seconds) { return seconds.ToString("0.##", CultureInfo.InvariantCulture) + " s"; }

        internal static List<QuickStep> QuickSteps(string text, out string problem)
        {
            problem = null;
            List<QuickStep> steps = new List<QuickStep>();
            double total = 0;
            foreach (string piece in Regex.Split((text ?? "").ToLowerInvariant(), @"[,;\r\n]+|\s+then\s+"))
            {
                string part = Regex.Replace(piece.Trim(), @"\s*\+\s*", "+");
                part = Regex.Replace(part, @"\b(left|right|middle)[ -]?click\b", "$1_click");
                part = Regex.Replace(part, @"^(hold|press|tap)\s+", "");
                if (part.Length == 0) continue;
                Match timed = Regex.Match(part, @"^(.+?)(?:\s+(?:for\s+)?(\d*\.?\d+)\s*(ms|s|secs?|seconds?)?)?$");
                string what = timed.Groups[1].Value.Trim();
                double held = 0;
                if (timed.Groups[2].Success)
                {
                    held = double.Parse(timed.Groups[2].Value, CultureInfo.InvariantCulture);
                    if (timed.Groups[3].Value == "ms") held /= 1000;
                }
                QuickStep step = new QuickStep { Seconds = held };
                if (what == "wait" || what == "pause")
                {
                    step.Waiting = true;
                    if (!timed.Groups[2].Success) step.Seconds = 0.2;
                    step.Said = "wait " + Secs(step.Seconds);
                }
                else
                {
                    List<string> keys = new List<string>();
                    foreach (string name in what.Split('+'))
                    {
                        string button = Clicker(name);
                        if (button == null) { keys.Add(name); continue; }
                        if (step.Button != null) { problem = "That didn't work: one step can only click one button. Put the next click in its own step."; return null; }
                        step.Button = button;
                    }
                    if (keys.Count > 0)
                    {
                        string named = string.Join("+", keys.ToArray());
                        string chord = Combo(named);
                        if (chord == null) { problem = "There's no key called " + named + "."; return null; }
                        step.Chord = chord;
                        Modifiers(chord, out step.Codes);
                    }
                    if (held > 0 && held < 0.05) step.Seconds = held = 0.05;
                    step.Said = what + (held > 0 ? " " + Secs(held) : "");
                }
                total += step.Seconds;
                steps.Add(step);
            }
            if (steps.Count == 0) { problem = "There's no step to do. Give steps like w 0.5, space, click."; return null; }
            if (steps.Count > QuickMostSteps) { problem = "That didn't work: a combo takes up to " + QuickMostSteps + " steps. Split it, and look in between."; return null; }
            if (total > QuickLongest) { problem = "That didn't work: a combo runs for up to " + QuickLongest + " seconds. Split it, and look in between."; return null; }
            return steps;
        }

        internal static string QuickBarred(string text)
        {
            string problem;
            List<QuickStep> steps = QuickSteps(text, out problem);
            if (steps != null) foreach (QuickStep step in steps) if (step.Chord.Length > 0 && Barred(step.Chord)) return step.Chord;
            return null;
        }

        internal static bool QuickEnters(string text)
        {
            string problem;
            List<QuickStep> steps = QuickSteps(text, out problem);
            if (steps != null) foreach (QuickStep step in steps) if (Regex.IsMatch(step.Chord, @"^((ctrl|alt|rightctrl|rightalt)\+)?(enter|numpadenter)$")) return true;
            return false;
        }

        private static string Listed(List<string> said)
        {
            if (said.Count < 2) return string.Join("", said.ToArray());
            return string.Join(", ", said.GetRange(0, said.Count - 1).ToArray()) + " and " + said[said.Count - 1];
        }

        internal static string Quick(List<QuickStep> steps, Sight sight)
        {
            foreach (QuickStep step in steps)
                if (step.Chord.Length > 0 && Barred(step.Chord)) return "Won't press " + step.Chord + ".";
            if (!StillFront(sight)) return "Stopped because the window I started on is no longer in front.";
            List<string> did = new List<string>();
            foreach (QuickStep step in steps)
            {
                if (interrupted) return Why;
                if (sight != null && Flinched) return "Stopped" + (did.Count > 0 ? " after " + Listed(did) : "") + ": it looked like something hit you.";
                if (!StillFront(sight)) return "Stopped because the window I started on is no longer in front.";
                int down = step.Button == "right" ? 0x0008 : step.Button == "middle" ? 0x0020 : 0x0002;
                bool esc = Array.IndexOf(step.Codes, (short)0x1B) >= 0, pressed = false, away = false;
                if (esc) escaping = true;
                try
                {
                    if (!step.Waiting)
                    {
                        Hold(step.Codes, true);
                        if (step.Button != null && !interrupted) { Mouse(down, 0); pressed = true; }
                    }
                    away = !HoldPause(step.Waiting || step.Seconds > 0 ? (int)Math.Round(step.Seconds * 1000) : 70, sight);
                }
                finally
                {
                    if (pressed) Mouse(down << 1, 0);
                    if (!step.Waiting) Hold(step.Codes, false);
                    if (esc) { Thread.Sleep(60); escaping = false; }
                }
                if (interrupted) return Why;
                if (away) return "Stopped because the window I started on is no longer in front.";
                did.Add(step.Said);
                Thread.Sleep(30);
            }
            Thread.Sleep(120);
            return interrupted ? Why : "Did " + Listed(did) + ".";
        }
    }
}
