using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class ResourceRow
    {
        internal string Name;
        internal int Count = 1;
        internal double Cpu;
        internal long Ram;
        internal double Gpu = -1;
        internal long Vram = -1;
    }

    internal sealed class ResourceView
    {
        internal double Cpu = -1;
        internal long RamUsed = -1, RamTotal;
        internal double Gpu = -1;
        internal long VramUsed = -1, VramTotal;
        internal string GpuName, Note;
        internal readonly List<ResourceRow> Capri = new List<ResourceRow>();
        internal readonly List<ResourceRow> Others = new List<ResourceRow>();
        // Her panel never learns each program's graphics card use on Linux or a Mac, so these stay - until a figure is measured.
        internal readonly ResourceRow CapriTotal = new ResourceRow { Name = "Capri", Gpu = -1, Vram = -1 };
        internal readonly ResourceRow OthersTotal = new ResourceRow { Name = "Everything else", Gpu = -1, Vram = -1 };

        internal double CapriCpu { get { return Math.Min(Math.Max(0, Cpu), CapriTotal.Cpu); } }

        internal static string Bytes(long bytes)
        {
            if (bytes < 0) return "-";
            if (bytes < (1L << 30)) return Math.Max(0, (int)Math.Round(bytes / 1048576.0)) + " MB";
            return (bytes / 1073741824.0).ToString(bytes < (10L << 30) ? "0.0" : "0", System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        internal static string Of(long used, long total)
        {
            if (used < 0 || total <= 0) return "-";
            if (total < (1L << 30)) return Bytes(used).Replace(" MB", "") + " of " + Bytes(total);
            return (Math.Max(0, used) / 1073741824.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " of " + Bytes(total);
        }

        internal static string Percent(double value)
        {
            if (value < 0) return "-";
            if (value > 0 && value < 1) return "<1%";
            return Math.Round(Math.Min(100, value)).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        }
    }

    internal sealed class ResourceSampler
    {
        internal const string MacNote = "macOS doesn't share CPU, memory or graphics card use with apps yet.";
        internal string Folder = "/proc";
        internal string Drm = "/sys/class/drm";
        internal Func<string> Stat, Mem;
        internal Func<long> Wall = delegate { return DateTime.UtcNow.Ticks; };
        internal Func<bool> NvidiaHere = delegate { return File.Exists("/proc/driver/nvidia/version") || Directory.Exists("/sys/module/nvidia"); };
        internal Func<string, string[], string> Run = Music.Run;

        private sealed class Proc
        {
            internal int Parent;
            internal long Ticks, Start, Ram;
            internal string Name;
        }

        private static readonly string[] Parts = { "llama-server", "Her brain", "tts-server", "HD voice", "ollama", "Ollama", "claude", "Claude Code", "codex", "Codex" };
        private readonly int self = Environment.ProcessId;
        private Dictionary<int, Proc> before = new Dictionary<int, Proc>();
        private string lastStat;
        private long lastProcWall, macRam;

        internal ResourceSampler()
        {
            Stat = delegate { return First(Path.Combine(Folder, "stat")); };
            Mem = delegate { return Text(Path.Combine(Folder, "meminfo")); };
        }

        internal ResourceView Read(bool processes)
        {
            ResourceView view = new ResourceView();
            if (OperatingSystem.IsMacOS())
            {
                if (macRam <= 0) macRam = Engine.ReadMachine().Ram;
                view.RamTotal = macRam;
                view.Note = MacNote;
                return view;
            }
            if (!OperatingSystem.IsLinux())
            {
                view.Note = "This computer doesn't share CPU or memory use with apps.";
                return view;
            }
            string stat = Stat();
            if (stat != null)
            {
                if (lastStat != null) view.Cpu = LinuxRoutes.CpuBusy(lastStat, stat) ?? -1;
                lastStat = stat;
            }
            Resources.Memory(Mem(), out view.RamUsed, out view.RamTotal);
            if (processes) Procs(view);
            return view;
        }

        private void Procs(ResourceView view)
        {
            long wall = Wall(), wasWall = lastProcWall;
            lastProcWall = wall;
            Dictionary<int, Proc> now = new Dictionary<int, Proc>();
            try
            {
                foreach (string path in Directory.EnumerateDirectories(Folder))
                {
                    int pid;
                    if (!int.TryParse(Path.GetFileName(path), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out pid)) continue;
                    Proc proc = One(pid);
                    if (proc != null && proc.Ram > 0) now[pid] = proc;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            HashSet<int> ours = new HashSet<int>();
            ours.Add(self);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (KeyValuePair<int, Proc> entry in now)
                {
                    // Like Windows: only her own tree counts. A process parented by anything else is another program's.
                    if (ours.Contains(entry.Key) || !ours.Contains(entry.Value.Parent)) continue;
                    Proc parent;
                    if (!now.TryGetValue(entry.Value.Parent, out parent) || parent.Start > entry.Value.Start) continue;
                    ours.Add(entry.Key);
                    grew = true;
                }
            }
            double span = wasWall > 0 && wall > wasWall ? (wall - wasWall) / 10000000.0 : 0;
            Dictionary<int, double> cpu = new Dictionary<int, double>();
            foreach (KeyValuePair<int, Proc> entry in now)
            {
                Proc was;
                if (span <= 0 || !before.TryGetValue(entry.Key, out was) || was.Start != entry.Value.Start || entry.Value.Ticks < was.Ticks) continue;
                cpu[entry.Key] = Math.Min(100, 100.0 * ((entry.Value.Ticks - was.Ticks) / 100.0) / (span * Environment.ProcessorCount));
            }
            before = now;
            Dictionary<string, ResourceRow> mine = new Dictionary<string, ResourceRow>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, ResourceRow> theirs = new Dictionary<string, ResourceRow>(StringComparer.OrdinalIgnoreCase);
            int mineCount = 0, theirsCount = 0;
            foreach (KeyValuePair<int, Proc> entry in now)
            {
                bool capri = ours.Contains(entry.Key);
                string name = capri ? NameOf(entry.Value.Name, entry.Key) : Trim(entry.Value.Name);
                Dictionary<string, ResourceRow> rows = capri ? mine : theirs;
                ResourceRow row;
                if (!rows.TryGetValue(name, out row)) rows[name] = row = new ResourceRow { Name = name, Count = 0 };
                row.Count++;
                double used;
                if (cpu.TryGetValue(entry.Key, out used)) row.Cpu += used;
                row.Ram += Math.Max(0, entry.Value.Ram);
                ResourceRow total = capri ? view.CapriTotal : view.OthersTotal;
                total.Cpu += cpu.TryGetValue(entry.Key, out used) ? used : 0;
                total.Ram += Math.Max(0, entry.Value.Ram);
                if (capri) mineCount++;
                else theirsCount++;
            }
            view.CapriTotal.Count = mineCount;
            view.OthersTotal.Count = theirsCount;
            view.Capri.AddRange(mine.Values);
            view.Capri.Sort(delegate(ResourceRow a, ResourceRow b) { return a.Name == "Capri" ? -1 : b.Name == "Capri" ? 1 : b.Ram.CompareTo(a.Ram); });
            List<ResourceRow> others = new List<ResourceRow>(theirs.Values);
            others.Sort(delegate(ResourceRow a, ResourceRow b)
            {
                int order = Weight(b, view).CompareTo(Weight(a, view));
                return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name);
            });
            view.Others.AddRange(others);
            if (view.Cpu < 0 && span > 0) view.Cpu = Math.Min(100, view.CapriTotal.Cpu + view.OthersTotal.Cpu);
            Gpu(view);
        }

        private static double Weight(ResourceRow row, ResourceView view)
        {
            double ram = view.RamTotal > 0 ? 100.0 * row.Ram / view.RamTotal : 0;
            return Math.Max(row.Cpu, 0) + ram;
        }

        private string NameOf(string image, int id)
        {
            if (id == self) return "Capri";
            for (int i = 0; i < Parts.Length; i += 2)
                if (string.Equals(image, Parts[i], StringComparison.Ordinal)) return Parts[i + 1];
            return Trim(image);
        }

        private static string Trim(string image) { return string.IsNullOrEmpty(image) ? "System" : image; }

        private Proc One(int pid)
        {
            string dir = Path.Combine(Folder, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
            string stat = Text(Path.Combine(dir, "stat"));
            if (stat == null) return null;
            int open = stat.IndexOf('('), close = stat.LastIndexOf(')');
            if (open < 0 || close <= open) return null;
            string[] rest = stat.Substring(close + 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (rest.Length < 20) return null;
            int parent;
            long user, system, start;
            if (!int.TryParse(rest[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out parent)
                || !long.TryParse(rest[11], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out user)
                || !long.TryParse(rest[12], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out system)
                || !long.TryParse(rest[19], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out start)) return null;
            Proc proc = new Proc { Name = stat.Substring(open + 1, close - open - 1), Parent = parent, Ticks = user + system, Start = start };
            string statm = Text(Path.Combine(dir, "statm"));
            if (statm != null)
            {
                string[] pages = statm.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                long resident;
                if (pages.Length >= 2 && long.TryParse(pages[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out resident)) proc.Ram = Math.Max(0, resident) * Environment.SystemPageSize;
            }
            return proc;
        }

        private void Gpu(ResourceView view)
        {
            if (NvidiaHere() && NvidiaCard(view)) return;
            if (Amd(view)) return;
            view.Gpu = -1;
            view.Note = "This computer doesn't share graphics card use.";
        }

        private bool NvidiaCard(ResourceView view)
        {
            string said = Run("nvidia-smi", new[] { "--query-gpu=utilization.gpu,memory.used,memory.total,name", "--format=csv,noheader,nounits" });
            double gpu;
            long used, total;
            string name;
            if (!Resources.Nvidia(said, out gpu, out used, out total, out name)) return false;
            view.Gpu = gpu; view.VramUsed = used; view.VramTotal = total; view.GpuName = name;
            view.Note = CardNote(view);
            return true;
        }

        private bool Amd(ResourceView view)
        {
            string[] cards;
            try { cards = Directory.GetDirectories(Drm); }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            foreach (string card in cards)
            {
                string name = Path.GetFileName(card);
                int at;
                if (name == null || !name.StartsWith("card", StringComparison.Ordinal) || name.Length <= 4
                    || !int.TryParse(name.Substring(4), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out at)) continue;
                string device = Path.Combine(card, "device");
                double busy;
                if (!double.TryParse(Number(Path.Combine(device, "gpu_busy_percent")), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out busy)) continue;
                view.Gpu = Math.Max(0, Math.Min(100, busy));
                long used, total;
                if (long.TryParse(Number(Path.Combine(device, "mem_info_vram_used")), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out used)) view.VramUsed = used;
                if (long.TryParse(Number(Path.Combine(device, "mem_info_vram_total")), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out total)) view.VramTotal = total;
                view.GpuName = "AMD card";
                view.Note = CardNote(view);
                return true;
            }
            return false;
        }

        private static string CardNote(ResourceView view)
        {
            string name = string.IsNullOrEmpty(view.GpuName) ? null : Shorter(view.GpuName);
            return "The card's total use" + (name == null ? "" : " · " + name) + (view.VramTotal > 0 ? " (" + ResourceView.Bytes(view.VramTotal) + ")" : "") + ". This computer doesn't name each program's share.";
        }

        private static string Shorter(string gpu) { return gpu.Replace("NVIDIA GeForce ", "").Replace("(R)", "").Replace("(TM)", "").Trim(); }

        private static string First(string file)
        {
            try
            {
                foreach (string line in File.ReadLines(file)) return line;
                return null;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string Text(string file)
        {
            try { return File.ReadAllText(file); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        private static string Number(string file)
        {
            string text = Text(file);
            return text == null ? null : text.Trim();
        }
    }

    internal static class Resources
    {
        private static readonly ResourceSampler line = new ResourceSampler();
        internal static Func<ResourceView> Source = delegate { return line.Read(false); };

        internal static string Line()
        {
            ResourceView view;
            try { view = Source() ?? new ResourceView(); }
            catch (Exception) { view = new ResourceView(); }
            return Words(view.Cpu, view.RamUsed, view.RamTotal);
        }

        internal static string Words(double cpu, long used, long total)
        {
            string ram = used >= 0 && total > 0 ? "RAM " + ResourceView.Of(used, total).Replace(" of ", "/") : "RAM -";
            return (cpu < 0 ? "CPU -" : "CPU " + ResourceView.Percent(cpu)) + " · " + ram;
        }

        internal static ResourceView Sample()
        {
            try { return new ResourceSampler().Read(true); }
            catch (Exception) { return new ResourceView(); }
        }

        internal static bool Memory(string meminfo, out long used, out long total)
        {
            used = -1;
            total = 0;
            long available = -1;
            if (meminfo != null)
                foreach (string line in meminfo.Split('\n'))
                {
                    if (line.StartsWith("MemTotal:", StringComparison.Ordinal)) total = Kilo(line);
                    else if (line.StartsWith("MemAvailable:", StringComparison.Ordinal)) available = Kilo(line);
                }
            if (total <= 0 || available < 0 || available > total) return false;
            used = total - available;
            return true;
        }

        private static long Kilo(string line)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            long kb;
            return parts.Length >= 2 && long.TryParse(parts[1], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out kb) ? kb * 1024 : -1;
        }

        internal static bool Nvidia(string said, out double gpu, out long used, out long total, out string name)
        {
            gpu = -1; used = -1; total = 0; name = null;
            if (string.IsNullOrWhiteSpace(said)) return false;
            // nvidia-smi prints one row per card; the panel shows one card, like Windows, so take the first row only.
            string first = null;
            foreach (string row in said.Split('\n'))
            {
                string trimmed = row.Trim();
                if (trimmed.Length > 0) { first = trimmed; break; }
            }
            if (first == null) return false;
            string[] parts = first.Split(',');
            if (parts.Length < 4) return false;
            double percent;
            long mibUsed, mibTotal;
            if (!double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out percent)
                || !long.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out mibUsed)
                || !long.TryParse(parts[2].Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out mibTotal)) return false;
            string card = string.Join(",", parts, 3, parts.Length - 3).Trim();
            if (card.Length == 0) return false;
            gpu = Math.Max(0, Math.Min(100, percent));
            used = mibUsed * (1L << 20);
            total = mibTotal * (1L << 20);
            name = card;
            return true;
        }

        internal static void Checks(List<string> checks)
        {
            long used, total;
            if (!Memory("MemTotal:       16777216 kB\nMemAvailable:   12582912 kB\n", out used, out total) || total != 16L << 30 || used != 4L << 30)
                throw new Exception("The Resources read of /proc/meminfo says " + used + " of " + total);
            if (Memory("MemTotal:       16777216 kB\nMemFree:        12582912 kB\n", out used, out total) || used >= 0 || total != 16L << 30)
                throw new Exception("The Resources read of /proc/meminfo without MemAvailable says " + used + " of " + total);
            if (ResourceView.Bytes(410L << 20) != "410 MB" || ResourceView.Bytes(3400L << 20) != "3.3 GB" || ResourceView.Of(12300L << 20, 32L << 30) != "12.0 of 32 GB" || ResourceView.Percent(0.4) != "<1%" || ResourceView.Percent(-1) != "-")
                throw new Exception("The Resources panel's numbers read wrong: " + ResourceView.Bytes(410L << 20) + " / " + ResourceView.Bytes(3400L << 20) + " / " + ResourceView.Of(12300L << 20, 32L << 30) + " / " + ResourceView.Percent(0.4) + " / " + ResourceView.Percent(-1));
            if (Words(14.2, 9800L << 20, 16L << 30) != "CPU 14% · RAM 9.6/16 GB" || Words(-1, 300L << 20, 800L << 20) != "CPU - · RAM 300/800 MB" || Words(-1, -1, 16L << 30) != "CPU - · RAM -")
                throw new Exception("The Mainframe's Resources line reads " + Words(14.2, 9800L << 20, 16L << 30) + " / " + Words(-1, 300L << 20, 800L << 20) + " / " + Words(-1, -1, 16L << 30));
            if (Nvidia("no card here", out double gpu, out long vram, out long card, out string cardName) || gpu != -1
                || !Nvidia("12, 1024, 12288, NVIDIA GeForce RTX 4070", out gpu, out vram, out card, out cardName) || gpu != 12 || vram != 1024L << 20 || card != 12288L << 20 || cardName != "NVIDIA GeForce RTX 4070")
                throw new Exception("The Resources read of nvidia-smi says " + gpu + ", " + vram + " of " + card + ", " + (cardName ?? "no card"));
            checks.Add("PASS resources numbers: memory from MemTotal and MemAvailable, MB and GB as her panel shows them, CPU 14% · RAM 9.6/16 GB and its honest - when a value is unread, and NVIDIA's own card figure");
            if (OperatingSystem.IsMacOS())
            {
                ResourceView mac = new ResourceSampler().Read(true);
                if (mac.Cpu >= 0 || mac.RamUsed >= 0 || mac.Gpu >= 0 || mac.VramUsed >= 0 || mac.Capri.Count != 0 || mac.Others.Count != 0 || mac.RamTotal < 0 || mac.Note != ResourceSampler.MacNote)
                    throw new Exception("The Mac Resources read invented a value: CPU " + mac.Cpu + ", RAM " + mac.RamUsed + " of " + mac.RamTotal + ", GPU " + mac.Gpu + ", " + mac.Capri.Count + " of hers, " + mac.Others.Count + " others, note " + (mac.Note ?? "none"));
                checks.Add(mac.RamTotal > 0
                    ? "PASS resources on a Mac: it reads the Mac's total memory (" + ResourceView.Bytes(mac.RamTotal) + ") and shows CPU, memory use, graphics card use and each program as -, since macOS doesn't share them with apps"
                    : "PASS resources on a Mac: macOS doesn't share CPU, memory or graphics card use with apps yet, so CPU, memory use, graphics card use and each program show -");
                string macHelp = SelfKnow.Matching("what's using my CPU?") ?? "";
                if (!macHelp.StartsWith("Resources: ", StringComparison.Ordinal) || !macHelp.Contains("macOS doesn't share CPU, memory or graphics card use with apps yet"))
                    throw new Exception("Her Mac Resources help says " + macHelp);
                checks.Add("PASS resources help on a Mac: " + macHelp);
                return;
            }
            if (!OperatingSystem.IsLinux()) return;
            string root = Path.Combine(Path.GetTempPath(), "capri-resources-" + Guid.NewGuid().ToString("N"));
            try
            {
                string proc = Path.Combine(root, "proc"), drm = Path.Combine(root, "drm");
                Directory.CreateDirectory(proc);
                File.WriteAllText(Path.Combine(proc, "stat"), "cpu  100 0 100 800 0 0 0 0 0 0\n");
                File.WriteAllText(Path.Combine(proc, "meminfo"), "MemTotal:       16777216 kB\nMemAvailable:   12582912 kB\n");
                int self = Environment.ProcessId;
                Action<int, string, int, long, long, long, long> Fake = delegate(int pid, string name, int parent, long user, long system, long start, long pages)
                {
                    string dir = Path.Combine(proc, pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Directory.CreateDirectory(dir);
                    File.WriteAllText(Path.Combine(dir, "stat"), pid + " (" + name + ") S " + parent + " 0 0 0 0 0 0 0 0 0 " + user + " " + system + " 0 0 0 0 8 0 " + start + " 0 0\n");
                    File.WriteAllText(Path.Combine(dir, "statm"), (pages + 1024) + " " + pages + " 0 0 0 0 0\n");
                };
                Fake(self, "Capri", 1, 200, 100, 900, 51200);
                Fake(101, "llama-server", self, 0, 0, 901, 25600);
                Fake(200, "chrome", 1, 0, 0, 800, 12800);
                Fake(201, "chrome", 1, 0, 0, 800, 2560);
                Fake(50, "kworker/0:0", 2, 0, 0, 1, 0);
                string cardDir = Path.Combine(drm, "card0", "device");
                Directory.CreateDirectory(cardDir);
                File.WriteAllText(Path.Combine(cardDir, "gpu_busy_percent"), "42\n");
                File.WriteAllText(Path.Combine(cardDir, "mem_info_vram_used"), (2L << 30).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                File.WriteAllText(Path.Combine(cardDir, "mem_info_vram_total"), (8L << 30).ToString(System.Globalization.CultureInfo.InvariantCulture) + "\n");
                ResourceSampler sampler = new ResourceSampler();
                sampler.Folder = proc; sampler.Drm = drm;
                sampler.NvidiaHere = delegate { return false; };
                long now = 10000000;
                sampler.Wall = delegate { return now; };
                ResourceView first = sampler.Read(true);
                if (first.Cpu != -1 || first.RamUsed != 4L << 30 || first.RamTotal != 16L << 30 || first.Capri.Count != 2 || first.Others.Count != 1
                    || first.Capri[0].Name != "Capri" || first.Capri[1].Name != "Her brain" || first.Capri[1].Ram != 100L << 20
                    || first.Others[0].Name != "chrome" || first.Others[0].Count != 2 || first.Others[0].Ram != 60L << 20
                    || first.CapriTotal.Count != 2 || first.OthersTotal.Count != 2 || first.CapriTotal.Ram != 300L << 20 || first.OthersTotal.Ram != 60L << 20)
                    throw new Exception("The first Resources sample reads CPU " + first.Cpu + ", RAM " + first.RamUsed + " of " + first.RamTotal + ", hers " + first.Capri.Count + " / " + first.CapriTotal.Count + " and others " + first.Others.Count + " / " + first.OthersTotal.Count + " (chrome " + (first.Others.Count == 0 ? "missing" : first.Others[0].Count + ", " + first.Others[0].Ram) + ")");
                if (first.Gpu != 42 || first.VramUsed != 2L << 30 || first.VramTotal != 8L << 30 || first.GpuName != "AMD card" || first.Note != "The card's total use · AMD card (8.0 GB). This computer doesn't name each program's share.")
                    throw new Exception("The Resources sample's AMD card reads " + first.Gpu + "%, " + first.VramUsed + " of " + first.VramTotal + ", " + (first.GpuName ?? "no name") + ", " + (first.Note ?? "no note"));
                File.WriteAllText(Path.Combine(proc, "stat"), "cpu  150 0 150 900 0 0 0 0 0 0\n");
                Fake(self, "Capri", 1, 300, 100, 900, 51200);
                now += 10000000;
                ResourceView second = sampler.Read(true);
                if (second.Cpu != 50 || Math.Abs(second.Capri[0].Cpu - 100.0 / Environment.ProcessorCount) > 0.001 || second.Capri[0].Ram != 200L << 20)
                    throw new Exception("The second Resources sample reads CPU " + second.Cpu + ", Capri " + second.Capri[0].Cpu + " and " + second.Capri[0].Ram);
                Fake(1, "systemd", 0, 0, 0, 1, 1);
                ResourceView withInit = sampler.Read(true);
                if (withInit.Capri.Exists(row => row.Name == "chrome")
                    || !withInit.Others.Exists(row => row.Name == "chrome" && row.Count == 2))
                    throw new Exception("An unrelated init child appeared under CAPRI.");
                if (withInit.CapriTotal.Gpu >= 0 || withInit.OthersTotal.Gpu >= 0)
                    throw new Exception("Unknown per-program GPU use became a number.");
                if (Nvidia("71, 768, 6144, RTX 3050\n11, 500, 12288, RTX 3060\n",
                    out _, out _, out _, out string multiple) && multiple.Contains('\n'))
                    throw new Exception("A second GPU's metrics leaked into the card name.");
                checks.Add("PASS resources sampler: /proc/stat's two samples give the processor's busy share and each program's CPU, /proc/statm its resident memory, her brain counts as Capri's, chrome groups ×2 and stays under OTHER PROGRAMS when an unrelated init process is listed, a kernel thread isn't a program, an AMD card's sysfs figure gives its use and memory, and per-program graphics card use stays - when it's unknown");
                sampler.NvidiaHere = delegate { return true; };
                sampler.Drm = Path.Combine(root, "no-card");
                string asked = null;
                sampler.Run = delegate(string file, string[] args) { asked = file + " " + string.Join(" ", args); return "12, 1024, 12288, NVIDIA GeForce RTX 4070"; };
                now += 10000000;
                ResourceView nvidia = sampler.Read(true);
                if (asked != "nvidia-smi --query-gpu=utilization.gpu,memory.used,memory.total,name --format=csv,noheader,nounits"
                    || nvidia.Gpu != 12 || nvidia.VramUsed != 1024L << 20 || nvidia.VramTotal != 12288L << 20 || nvidia.GpuName != "NVIDIA GeForce RTX 4070" || nvidia.Note != "The card's total use · RTX 4070 (12 GB). This computer doesn't name each program's share.")
                    throw new Exception("The Resources sample's NVIDIA card asked " + (asked ?? "nothing") + " and reads " + nvidia.Gpu + "%, " + nvidia.VramUsed + " of " + nvidia.VramTotal + ", " + (nvidia.GpuName ?? "no name") + ", " + (nvidia.Note ?? "no note"));
                sampler.NvidiaHere = delegate { return false; };
                sampler.Run = delegate(string file, string[] args) { throw new Exception("The Resources sample ran " + file + " with no card."); };
                now += 10000000;
                ResourceView none = sampler.Read(true);
                if (none.Gpu != -1 || none.VramUsed != -1 || none.Note != "This computer doesn't share graphics card use.")
                    throw new Exception("The Resources sample with no card reads " + none.Gpu + "%, " + none.VramUsed + ", " + (none.Note ?? "no note"));
                checks.Add("PASS resources graphics: NVIDIA's own tool is asked only when its driver is here, one card's row reads RTX 4070 (12 GB) and a second card's row doesn't leak into its name, and Intel graphics or no card say this computer doesn't share graphics card use, without running anything");
                string help = SelfKnow.Matching("what's using my CPU?") ?? "";
                if (!help.StartsWith("Resources: ", StringComparison.Ordinal) || !help.Contains("OTHER PROGRAMS lists") || !help.Contains("RAM is each program's resident memory") || !help.Contains("Intel graphics don't share"))
                    throw new Exception("Her Resources help says " + help);
                checks.Add("PASS resources help: " + help);
            }
            finally
            {
                try { Directory.Delete(root, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    internal sealed class UseBar : Control
    {
        private double ours, theirs;

        internal double Ours { get { return ours; } }
        internal double Theirs { get { return theirs; } }

        internal void Show(double capri, double rest)
        {
            ours = Math.Max(0, Math.Min(100, capri));
            theirs = Math.Max(0, Math.Min(100 - ours, rest));
            InvalidateVisual();
        }

        public override void Render(DrawingContext context)
        {
            Rect box = new Rect(Bounds.Size);
            context.DrawRectangle(MainframeTheme.Well, null, box);
            double inner = Math.Max(0, Bounds.Width - 2);
            double capri = inner * ours / 100, rest = inner * theirs / 100;
            double tall = Math.Max(0, Bounds.Height - 2);
            if (capri > 0) context.DrawRectangle(MainframeTheme.Cyan, null, new Rect(1, 1, Math.Max(1, capri), tall));
            if (rest > 0) context.DrawRectangle(MainframeTheme.Rose, null, new Rect(1 + capri, 1, rest, tall));
            context.DrawRectangle(null, new Pen(MainframeTheme.WellEdge, 1), new Rect(0.5, 0.5, Math.Max(0, Bounds.Width - 1), Math.Max(0, Bounds.Height - 1)));
        }
    }

    internal sealed class ResourceCells
    {
        internal NeonCaption Name, Cpu, Ram, Gpu, Vram;
    }

    internal sealed class ResourcesSheet : MainframeWindow
    {
        internal const int Wide = 380, Tall = 600, Mine = 5, Theirs = 6;
        private static readonly string[] Meters = { "CPU", "RAM", "GPU" };
        private readonly Pet pet;
        private readonly ResourceSampler sampler = new ResourceSampler();
        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2000) };
        private readonly UseBar[] bars = new UseBar[3];
        private readonly NeonCaption[] amounts = new NeonCaption[3];
        private readonly NeonCaption legend, gpuNote, capriTotal, othersTotal, summary, limits;
        internal readonly ResourceCells[] Capri = new ResourceCells[Mine], Others = new ResourceCells[Theirs];
        private bool closed, sampling, first = true;
        internal readonly Dictionary<string, Control> Rows = new Dictionary<string, Control>();

        internal ResourcesSheet(Pet owner) : base("resources")
        {
            pet = owner;
            ShowInTaskbar = false;
            Put(Page, new NeonCaption { Text = "RESOURCES >", Size = 20, Bold = true, Display = true }, 24, 15, 288, 29);
            var close = Put(Page, new NeonButton { Glyph = 2 }, Wide - 50, 20, 26, 22, "Close");
            close.Click += delegate { Close(); };
            Put(Page, new NeonCaption { Text = "What's using your PC right now", Ink = MainframeTheme.Faint, Size = 34.0 / 3 }, 28, 52, 328, 16);
            for (int i = 0; i < Meters.Length; i++)
            {
                int y = 80 + 26 * i;
                Put(Page, new NeonCaption { Text = Meters[i], Ink = MainframeTheme.Rose, Bold = true }, 28, y, 40, 18);
                bars[i] = Put(Page, new UseBar(), 72, y + 3, 144, 12, Meters[i] + " use");
                amounts[i] = Put(Page, new NeonCaption { Text = "-", Ink = MainframeTheme.White, Align = TextAlignment.Right, Ellipsis = true }, 222, y, 134, 18, Meters[i] + " amount");
                Rows[Meters[i] + " amount"] = amounts[i];
            }
            legend = Put(Page, new NeonCaption { Text = "Cyan is Capri, red is everything else", Ink = MainframeTheme.Faint, Size = 34.0 / 3 }, 28, 158, 328, 16, "Legend");
            Rows["Legend"] = legend;
            gpuNote = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Faint, Size = 34.0 / 3, Ellipsis = true }, 28, 174, 328, 16, "GPU note");
            Rows["GPU note"] = gpuNote;
            Put(Page, new NeonHead { Text = "CAPRI >" }, 14, 196, 352, 24, "Capri >");
            capriTotal = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Cyan, Size = 34.0 / 3, Align = TextAlignment.Right, Ellipsis = true }, 150, 200, 206, 18, "Capri total");
            Rows["Capri total"] = capriTotal;
            string[] heads = { "CPU", "RAM", "GPU", "VRAM" };
            for (int c = 0; c < heads.Length; c++)
                Put(Page, new NeonCaption { Text = heads[c], Ink = MainframeTheme.Faint, Size = 34.0 / 3, Align = TextAlignment.Right }, ColumnAt(c), 226, ColumnWide(c), 14);
            for (int i = 0; i < Mine; i++) Capri[i] = Row(244 + 20 * i, MainframeTheme.Cyan);
            Put(Page, new NeonHead { Text = "OTHER PROGRAMS >" }, 14, 356, 352, 24, "Other programs >");
            othersTotal = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Rose, Size = 34.0 / 3, Align = TextAlignment.Right, Ellipsis = true }, 150, 360, 206, 18, "Others total");
            Rows["Others total"] = othersTotal;
            for (int i = 0; i < Theirs; i++) Others[i] = Row(384 + 20 * i, MainframeTheme.White);
            summary = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Cyan, Size = 34.0 / 3, Wrap = true, Top = true }, 28, 508, 328, 34, "Summary");
            Rows["Summary"] = summary;
            limits = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Faint, Size = 34.0 / 3, Wrap = true, Top = true }, 28, 550, 328, 40, "Limits note");
            Rows["Limits note"] = limits;
            clock.Tick += delegate { Program.Guard(Sample); };
            Opened += delegate { Fit(Wide, Tall, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0); Sample(); clock.Start(); };
            Closed += delegate { closed = true; clock.Stop(); };
        }

        private static int ColumnAt(int c) { return c == 0 ? 164 : c == 1 ? 204 : c == 2 ? 262 : 298; }

        private static int ColumnWide(int c) { return c == 0 ? 36 : c == 1 ? 54 : c == 2 ? 32 : 58; }

        private ResourceCells Row(int y, IBrush ink)
        {
            ResourceCells row = new ResourceCells();
            row.Name = Put(Page, new NeonCaption { Text = "", Ink = ink, Ellipsis = true }, 28, y, 132, 18);
            row.Cpu = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.White, Size = 34.0 / 3, Align = TextAlignment.Right, Ellipsis = true }, ColumnAt(0), y, ColumnWide(0), 18);
            row.Ram = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Faint, Size = 34.0 / 3, Align = TextAlignment.Right, Ellipsis = true }, ColumnAt(1), y, ColumnWide(1), 18);
            row.Gpu = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Faint, Size = 34.0 / 3, Align = TextAlignment.Right, Ellipsis = true }, ColumnAt(2), y, ColumnWide(2), 18);
            row.Vram = Put(Page, new NeonCaption { Text = "", Ink = MainframeTheme.Faint, Size = 34.0 / 3, Align = TextAlignment.Right, Ellipsis = true }, ColumnAt(3), y, ColumnWide(3), 18);
            return row;
        }

        private void Sample()
        {
            if (closed || sampling) return;
            sampling = true;
            bool firstRead = first;
            ThreadPool.QueueUserWorkItem(delegate
            {
                ResourceView view = null;
                try
                {
                    view = sampler.Read(true);
                    if (firstRead) { Thread.Sleep(700); view = sampler.Read(true); first = false; }
                }
                catch (Exception error) { Brain.Log("resources", -1, error.GetType().Name + ": " + error.Message); }
                finally { sampling = false; }
                if (view != null) Dispatcher.UIThread.Post(delegate { if (!closed) Fill(view); });
            });
        }

        internal void Fill(ResourceView view)
        {
            if (view == null) return;
            bool any = view.Cpu >= 0 || view.RamUsed >= 0 || view.Gpu >= 0;
            legend.IsVisible = any;
            // When she can't read any of the meters, that one honest note belongs once, in the limits line.
            bool unread = view.Cpu < 0 && view.RamUsed < 0 && view.Gpu < 0;
            gpuNote.Text = unread ? "" : view.Note ?? "";
            gpuNote.IsVisible = gpuNote.Text.Length > 0;
            limits.Text = unread ? view.Note ?? "" : "";
            limits.IsVisible = limits.Text.Length > 0;
            bars[0].Show(view.Cpu >= 0 ? view.CapriCpu : 0, view.Cpu >= 0 ? Math.Max(0, view.Cpu - view.CapriCpu) : 0);
            amounts[0].Text = ResourceView.Percent(view.Cpu);
            if (view.RamUsed >= 0 && view.RamTotal > 0)
            {
                double total = view.RamTotal;
                double capriRam = 100.0 * view.CapriTotal.Ram / total;
                bars[1].Show(capriRam, Math.Max(0, 100.0 * view.RamUsed / total - capriRam));
                amounts[1].Text = ResourceView.Of(view.RamUsed, view.RamTotal);
            }
            else { bars[1].Show(0, 0); amounts[1].Text = "-"; }
            if (view.Gpu >= 0)
            {
                bars[2].Show(0, view.Gpu);
                amounts[2].Text = ResourceView.Percent(view.Gpu) + (view.VramUsed >= 0 && view.VramTotal > 0 ? " · " + ResourceView.Of(view.VramUsed, view.VramTotal) : "");
            }
            else { bars[2].Show(0, 0); amounts[2].Text = "-"; }
            capriTotal.Text = any ? Totals(view.CapriTotal) : "";
            othersTotal.Text = any ? Totals(view.OthersTotal) : "";
            Put(Capri, view.Capri);
            Put(Others, view.Others);
            summary.Text = Summary(view);
            foreach (Control control in new Control[] { legend, gpuNote, capriTotal, othersTotal, summary, limits }) control.InvalidateVisual();
        }

        private static string Totals(ResourceRow row)
        {
            string words = ResourceView.Percent(row.Cpu) + " CPU · " + ResourceView.Bytes(row.Ram);
            if (row.Gpu >= 0) words += " · " + ResourceView.Percent(row.Gpu) + " GPU";
            return words;
        }

        private void Put(ResourceCells[] rows, List<ResourceRow> found)
        {
            for (int i = 0; i < rows.Length; i++)
            {
                ResourceRow row = i < found.Count ? found[i] : null;
                rows[i].Name.Text = row == null ? "" : RowName(row);
                rows[i].Cpu.Text = row == null ? "" : ResourceView.Percent(row.Cpu);
                rows[i].Ram.Text = row == null ? "" : ResourceView.Bytes(row.Ram);
                rows[i].Gpu.Text = row == null ? "" : ResourceView.Percent(row.Gpu);
                rows[i].Vram.Text = row == null ? "" : row.Vram > 0 ? ResourceView.Bytes(row.Vram) : row.Vram == 0 ? "0 MB" : "-";
                rows[i].Name.InvalidateVisual(); rows[i].Cpu.InvalidateVisual(); rows[i].Ram.InvalidateVisual(); rows[i].Gpu.InvalidateVisual(); rows[i].Vram.InvalidateVisual();
            }
        }

        private static string RowName(ResourceRow row) { return row.Count > 1 ? row.Name + " ×" + row.Count : row.Name; }

        internal static string Summary(ResourceView view)
        {
            if (view.RamTotal <= 0 || view.RamUsed < 0 || view.Cpu < 0) return "";
            double ram = 100.0 * view.CapriTotal.Ram / view.RamTotal;
            string most = view.Others.Count > 0 ? view.Others[0].Name : null;
            return "Capri uses " + ResourceView.Percent(view.CapriCpu) + " of your CPU and " + ResourceView.Percent(ram) + " of your RAM." + (most == null ? "" : " Using the most: " + most + ".");
        }

        internal string Shows(ResourceView view)
        {
            if (amounts[0].Text != ResourceView.Percent(view.Cpu)) return "CPU says " + amounts[0].Text;
            if (amounts[1].Text != (view.RamUsed >= 0 && view.RamTotal > 0 ? ResourceView.Of(view.RamUsed, view.RamTotal) : "-")) return "RAM says " + amounts[1].Text;
            string gpu = view.Gpu < 0 ? "-" : ResourceView.Percent(view.Gpu) + (view.VramUsed >= 0 && view.VramTotal > 0 ? " · " + ResourceView.Of(view.VramUsed, view.VramTotal) : "");
            if (amounts[2].Text != gpu) return "GPU says " + amounts[2].Text;
            if (view.Capri.Count > 0 && Capri[0].Name.Text != RowName(view.Capri[0])) return "Capri's first row says " + Capri[0].Name.Text;
            if (view.Others.Count > 0 && Others[0].Name.Text != RowName(view.Others[0])) return "the first other program says " + Others[0].Name.Text;
            foreach (UseBar bar in bars) if (bar.Ours + bar.Theirs > 100.01) return "a bar runs past full";
            if (view.Cpu >= 0 && view.RamUsed >= 0 && summary.Text.Length == 0) return "no summary";
            foreach (NeonCaption amount in amounts) if (amount.TextFits() != null) return "a meter's amount is cut: " + amount.Text;
            foreach (ResourceCells[] rows in new[] { Capri, Others })
                foreach (ResourceCells row in rows)
                    foreach (NeonCaption cell in new[] { row.Cpu, row.Ram, row.Gpu, row.Vram })
                        if (!string.IsNullOrEmpty(cell.Text) && cell.TextFits() != null) return "a number is cut: " + cell.Text;
            return null;
        }

        internal string Crowded()
        {
            Rect page = new Rect(0, 0, Wide, Tall);
            for (int i = 0; i < Page.Children.Count; i++)
            {
                Control a = Page.Children[i];
                if (!a.IsVisible) continue;
                Rect box = Box(a);
                if (!page.Contains(box)) return "Outside: " + NameOf(a);
                NeonCaption caption = a as NeonCaption;
                string wrong = caption == null ? null : caption.TextFits();
                if (wrong != null) return "Text: " + wrong;
                for (int j = i + 1; j < Page.Children.Count; j++)
                {
                    Control b = Page.Children[j];
                    if (!b.IsVisible) continue;
                    Rect overlap = box.Intersect(Box(b));
                    if (overlap.Width > 0.5 && overlap.Height > 0.5) return NameOf(a) + " / " + NameOf(b);
                }
            }
            return null;
        }

        private static Rect Box(Control control)
        {
            double x = Canvas.GetLeft(control), y = Canvas.GetTop(control);
            NeonHead head = control as NeonHead;
            return head == null ? new Rect(x, y, control.Width, control.Height) : new Rect(x + 14, y + 5, MainframeTheme.Words(head.Text, 40.0 / 3, true).Width, 19);
        }

        private static string NameOf(Control control)
        {
            return AutomationProperties.GetName(control) ?? (control as NeonCaption == null ? control.GetType().Name : ((NeonCaption)control).Text);
        }
    }
}
