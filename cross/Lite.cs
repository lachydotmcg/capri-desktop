using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class SlowSigns
    {
        internal string Kind = "none", Name, Processor;
        internal bool Lite, Read, Running, CpuOnly, Watch;
        internal int Threads;
        internal long RamFree = -1, RamTotal = -1;
        internal double Busy = -1;

        internal bool OnPc { get { return Kind == "built-in" || Kind == "ollama"; } }
    }

    internal sealed partial class Pet
    {
        private const long LowFree = 1536L << 20;
        private const int SmallCpu = 4;
        private const double BusyCpu = 85;
        internal const string SlowUnread = "I couldn't check your PC just now. Try again in a moment.";
        private const string FreeKeyFix = "Press Free key under BRAIN in Capri Mainframe for a free cloud brain.";
        internal static Func<SlowSigns> FakedSigns;
        private bool slowChecking, liteResting;

        internal bool LiteOn { get { return Saved.Lite; } }

        // Lite mode on Mac and Linux: her brain on this PC stops and Watch my screen pauses. Windows also draws lighter, rests
        // her art and hands HD voice to her free voice; Mac and Linux have none of those, so they are not claimed here.
        internal void SetLite(bool on)
        {
            if (Saved.Lite == on) return;
            if (on)
            {
                Saved.LiteWatch = Saved.Watch;
                Saved.Lite = Brain.Lite = true;
                if (Saved.Watch) SetWatch(false, false);
                RestForLite();
            }
            else
            {
                Saved.Lite = Brain.Lite = false;
                if (Saved.LiteWatch && !Saved.Watch) SetWatch(true, false);
                Saved.LiteWatch = false;
                RestoreOwnBrain();
            }
            Save();
            Sync();
        }

        private void RestForLite()
        {
            Engine engine = ownEngine;
            if (engine != null && engine.Running && !liteResting)
            {
                liteResting = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { engine.Rest(0); }
                    finally { liteResting = false; }
                });
            }
            if (Brain.OllamaLoaded.Length > 0)
                ThreadPool.QueueUserWorkItem(delegate
                {
                    try { Brain.UnloadOllama(); }
                    catch (Exception) { }
                });
        }

        internal void CheckWhySlow()
        {
            if (slowChecking) return;
            slowChecking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string line;
                try { line = SlowFound(FakedSigns != null ? FakedSigns() : ReadSlowSigns()); }
                catch (Exception error) { Brain.Log("slow", -1, error.GetType().Name + ": " + error.Message); line = SlowUnread; }
                Dispatcher.UIThread.Post(delegate { slowChecking = false; Talk.Answer("(why slow?)", line); });
            });
        }

        internal SlowSigns ReadSlowSigns()
        {
            SlowSigns signs = new SlowSigns();
            signs.Lite = Saved.Lite;
            signs.Watch = Saved.Watch;
            signs.Threads = Environment.ProcessorCount;
            signs.Processor = ProcessorName();
            Cloud remote = Brain.Remote;
            if (Brain.Off) signs.Kind = "off";
            else if (remote != null) signs.Kind = remote.Kind == "local" ? "server" : "cloud";
            else if (Brain.Server != null) signs.Kind = "server";
            else if (Brain.Local != null && Brain.LocalPick != null)
            {
                signs.Kind = "built-in";
                signs.Name = Brain.LocalPick.Name;
                signs.CpuOnly = Brain.Local.OnCpu;
                signs.Running = Brain.Local.Running;
            }
            else if (Brain.Model != null) { signs.Kind = "ollama"; signs.Name = Brain.Model; }
            signs.RamTotal = Engine.ReadMachine().Ram;
            if (!OperatingSystem.IsLinux()) return signs;
            try
            {
                Match available = Regex.Match(File.ReadAllText("/proc/meminfo"), @"MemAvailable:\s+(\d+) kB");
                if (available.Success)
                {
                    signs.RamFree = long.Parse(available.Groups[1].Value, CultureInfo.InvariantCulture) * 1024;
                    signs.Read = true;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            try
            {
                string before = LinuxRoutes.CpuTimes();
                Thread.Sleep(500);
                double? busy = LinuxRoutes.CpuBusy(before, LinuxRoutes.CpuTimes());
                if (busy != null) { signs.Busy = busy.Value; signs.Read = true; }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return signs;
        }

        private static string ProcessorName()
        {
            if (!OperatingSystem.IsLinux()) return null;
            try
            {
                foreach (string line in File.ReadLines("/proc/cpuinfo"))
                    if (line.StartsWith("model name", StringComparison.Ordinal))
                    {
                        int at = line.IndexOf(':');
                        string name = at < 0 ? null : line.Substring(at + 1).Trim();
                        return string.IsNullOrWhiteSpace(name) ? null : name;
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return null;
        }

        internal static List<string[]> SlowReasons(SlowSigns s)
        {
            List<string[]> found = new List<string[]>();
            bool pc = s.OnPc && !s.Lite;
            if (s.OnPc && s.Lite) found.Add(new[] { "Lite mode is on, so my brain on your PC is switched off.", "Press Free key under BRAIN in Capri Mainframe for a free cloud brain, or turn Lite mode off." });
            if (pc && s.Kind == "built-in" && s.CpuOnly)
                found.Add(new[] { "My brain is set to run on your processor, so it's slower than on a graphics card.", "Turn on Lite mode to stop my brain on your PC, or press Free key under BRAIN for a free cloud brain." });
            if (pc && s.Kind == "built-in" && !s.Running)
                found.Add(new[] { "My brain on your PC rests after 15 minutes unused, so my next reply waits while it loads again.", "Only that first reply is slow. " + FreeKeyFix });
            if (s.Threads > 0 && s.Threads <= SmallCpu)
                found.Add(new[] { "Your processor" + (s.Processor == null ? "" : ", " + s.Processor + ",") + " has only " + s.Threads + (s.Threads == 1 ? " thread" : " threads") + ", which is small for " + (pc ? "running a brain." : "everything I do."), pc ? FreeKeyFix + " Lite mode helps too." : "Close apps you're not using to give me room." });
            if (s.RamTotal > 0 && s.RamFree >= 0 && (s.RamFree < LowFree || s.RamFree * 10 < s.RamTotal))
                found.Add(new[] { "Only " + Bytes(s.RamFree) + " of your " + Bytes(s.RamTotal) + " of RAM is free.", "Close apps or browser tabs you're not using" + (pc ? ", or turn on Lite mode." : ".") });
            if (s.Busy >= BusyCpu)
                found.Add(new[] { "Your processor is " + (int)Math.Round(s.Busy) + "% busy.", "Close what you're not using to give me room." });
            if (s.Watch && !s.Lite)
                found.Add(new[] { "Watch my screen is on, and it keeps working in the background.", "Turn on Lite mode to pause it, or turn it off under WATCH." });
            return found;
        }

        internal static string SlowNone(SlowSigns s)
        {
            if (s.Kind == "off") return "Nothing on your PC is slowing me down right now. No AI is on, so I'm not thinking at all.";
            if (s.Kind == "cloud") return "Nothing on your PC is slowing me down right now. My brain is in the cloud, so slow replies come from the service being busy. Picking another model under BRAIN can help.";
            if (s.Kind == "server") return "Nothing in Capri is slowing me down right now. Your own server does my thinking, so its speed is up to that server.";
            if (s.Kind == "none") return "Nothing on your PC is slowing me down right now. I don't have a brain yet: press Set up her brain or Free key under BRAIN in Capri Mainframe.";
            if (!s.Read) return "I can't read your memory or processor use here yet, so I only checked my brain and Watch my screen. Nothing in those is slowing me down right now.";
            return "I checked your PC and nothing is slowing me down right now.";
        }

        internal static string SlowLine(SlowSigns s)
        {
            List<string[]> reasons = SlowReasons(s);
            if (reasons.Count == 0) return SlowNone(s);
            string said = reasons[0][0] + " " + reasons[0][1];
            if (reasons.Count > 1) said += " " + reasons[1][0] + " " + reasons[1][1];
            return said;
        }

        internal static string SlowFound(SlowSigns s)
        {
            List<string[]> reasons = SlowReasons(s);
            string lite = s.Lite ? " Lite mode is on." : " Lite mode is off.";
            if (reasons.Count == 0) return "From a check of your PC just now: " + SlowNone(s) + lite;
            List<string> each = new List<string>();
            foreach (string[] reason in reasons) each.Add(reason[0] + " What helps: " + reason[1]);
            return "From a check of your PC just now: " + string.Join(" ", each.ToArray()) + lite;
        }

        internal static string Bytes(long bytes)
        {
            if (bytes < 0) return "-";
            if (bytes < (1L << 30)) return Math.Max(0, (int)Math.Round(bytes / 1048576.0)) + " MB";
            return (bytes / 1073741824.0).ToString(bytes < (10L << 30) ? "0.0" : "0", CultureInfo.InvariantCulture) + " GB";
        }
    }
}
