using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    internal sealed class UseRow
    {
        internal string Name;
        internal int Count = 1;
        internal double Cpu;
        internal long Ram;
        internal double Gpu = -1;
        internal long Vram = -1;
    }

    internal sealed class UseView
    {
        internal double Cpu = -1;
        internal long RamUsed, RamTotal;
        internal double Gpu = -1;
        internal long VramUsed = -1, VramTotal;
        internal string GpuName;
        internal readonly List<UseRow> Capri = new List<UseRow>();
        internal readonly List<UseRow> Others = new List<UseRow>();
        internal readonly UseRow CapriTotal = new UseRow { Name = "Capri", Gpu = 0, Vram = 0 };
        internal readonly UseRow OthersTotal = new UseRow { Name = "Everything else", Gpu = 0, Vram = 0 };
        internal bool GpuKnown;

        internal double CapriCpu { get { return Math.Min(Math.Max(0, Cpu), CapriTotal.Cpu); } }

        internal static string Bytes(long bytes)
        {
            if (bytes < 0) return "-";
            if (bytes < (1L << 30)) return Math.Max(0, (int)Math.Round(bytes / 1048576.0)) + " MB";
            return (bytes / 1073741824.0).ToString(bytes < (10L << 30) ? "0.0" : "0") + " GB";
        }

        internal static string Of(long used, long total)
        {
            if (total < (1L << 30)) return Bytes(used).Replace(" MB", "") + " of " + Bytes(total);
            return (Math.Max(0, used) / 1073741824.0).ToString("0.0") + " of " + Bytes(total);
        }

        internal static string Percent(double value)
        {
            if (value < 0) return "-";
            if (value > 0 && value < 1) return "<1%";
            return Math.Round(Math.Min(100, value)) + "%";
        }
    }

    internal sealed class SystemUse
    {
        private sealed class Seen
        {
            internal long Created, Time;
        }

        private sealed class Proc
        {
            internal int Id, Parent;
            internal long Created, Time, Private;
            internal string Image;
        }

        [StructLayout(LayoutKind.Sequential)]
        private sealed class MemoryStatus
        {
            internal uint Length = 64;
            internal uint Load;
            internal ulong TotalPhysical;
            internal ulong AvailablePhysical;
            internal ulong TotalPageFile;
            internal ulong AvailablePageFile;
            internal ulong TotalVirtual;
            internal ulong AvailableVirtual;
            internal ulong AvailableExtended;
        }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus status);
        [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        [DllImport("ntdll.dll")] private static extern int NtQuerySystemInformation(int kind, IntPtr buffer, int size, out int needed);

        internal static readonly string[] Parts = { "llama-server.exe", "Her brain", "tts-server.exe", "HD voice", "ollama.exe", "Ollama", "claude.exe", "Claude Code", "codex.exe", "Codex", "ffmpeg.exe", "Recorder", "cloudflared.exe", "Tunnel" };

        private readonly int self = Self();
        private Dictionary<int, Seen> before = new Dictionary<int, Seen>();
        private long lastIdle, lastBusy, lastWall;
        private Dictionary<string, CounterSample> engines = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
        private bool gpuBroken;
        private int buffer = 1 << 19;
        internal string Problem;

        private static int Self() { using (Process current = Process.GetCurrentProcess()) return current.Id; }

        internal static Func<long> Wall = delegate { return DateTime.UtcNow.Ticks; };

        internal static void Memory(out long used, out long total)
        {
            used = total = 0;
            MemoryStatus status = new MemoryStatus();
            if (!GlobalMemoryStatusEx(status)) return;
            total = (long)status.TotalPhysical;
            used = total - (long)status.AvailablePhysical;
        }

        internal double TotalCpu()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) return -1;
            long busy = kernel + user;
            double share = -1;
            if (lastBusy > 0 && busy > lastBusy) share = Math.Max(0, Math.Min(100, 100.0 * ((busy - lastBusy) - (idle - lastIdle)) / (busy - lastBusy)));
            lastIdle = idle;
            lastBusy = busy;
            return share;
        }

        internal string NameOf(string image, int id, int brain)
        {
            if (id == self) return "Capri";
            if (id == brain) return "Her brain";
            for (int i = 0; i < Parts.Length; i += 2)
                if (string.Equals(image, Parts[i], StringComparison.OrdinalIgnoreCase)) return Parts[i + 1];
            return Trim(image);
        }

        internal static string Trim(string image)
        {
            if (string.IsNullOrEmpty(image)) return "System";
            return image.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? image.Substring(0, image.Length - 4) : image;
        }

        private List<Proc> Read()
        {
            List<Proc> found = new List<Proc>();
            for (int tries = 0; tries < 4; tries++)
            {
                IntPtr memory = Marshal.AllocHGlobal(buffer);
                try
                {
                    int needed;
                    int status = NtQuerySystemInformation(5, memory, buffer, out needed);
                    if (status == unchecked((int)0xC0000004))
                    {
                        buffer = Math.Max(buffer * 2, needed + (1 << 16));
                        continue;
                    }
                    if (status != 0) { Problem = "NtQuerySystemInformation 0x" + status.ToString("X8"); return found; }
                    bool wide = IntPtr.Size == 8;
                    int offset = 0;
                    while (true)
                    {
                        IntPtr at = new IntPtr(memory.ToInt64() + offset);
                        int next = Marshal.ReadInt32(at, 0);
                        Proc proc = new Proc();
                        proc.Private = Marshal.ReadInt64(at, 8);
                        proc.Created = Marshal.ReadInt64(at, 32);
                        proc.Time = Marshal.ReadInt64(at, 40) + Marshal.ReadInt64(at, 48);
                        int length = Marshal.ReadInt16(at, 56) & 0xFFFF;
                        IntPtr name = Marshal.ReadIntPtr(at, wide ? 64 : 60);
                        proc.Image = length > 0 && name != IntPtr.Zero ? Marshal.PtrToStringUni(name, length / 2) : null;
                        proc.Id = (int)Marshal.ReadIntPtr(at, wide ? 80 : 68).ToInt64();
                        proc.Parent = (int)Marshal.ReadIntPtr(at, wide ? 88 : 72).ToInt64();
                        if (proc.Id != 0) found.Add(proc);
                        if (next <= 0) break;
                        offset += next;
                    }
                    Problem = null;
                    return found;
                }
                finally { Marshal.FreeHGlobal(memory); }
            }
            Problem = "process list kept growing";
            return found;
        }

        internal UseView Sample(int brain, bool gpu)
        {
            UseView view = new UseView();
            Memory(out view.RamUsed, out view.RamTotal);
            view.Cpu = TotalCpu();
            long wall = Wall();
            double span = lastWall > 0 && wall > lastWall ? (wall - lastWall) * (double)Environment.ProcessorCount : 0;
            lastWall = wall;
            List<Proc> all = Read();
            Dictionary<int, Proc> byId = new Dictionary<int, Proc>();
            foreach (Proc proc in all) byId[proc.Id] = proc;
            Dictionary<int, Seen> now = new Dictionary<int, Seen>();
            Dictionary<int, double> cpu = new Dictionary<int, double>();
            foreach (Proc proc in all)
            {
                Seen was;
                if (span > 0 && before.TryGetValue(proc.Id, out was) && was.Created == proc.Created && proc.Time >= was.Time) cpu[proc.Id] = Math.Min(100, 100.0 * (proc.Time - was.Time) / span);
                now[proc.Id] = new Seen { Created = proc.Created, Time = proc.Time };
            }
            before = now;
            Dictionary<int, double> gpuOf = null;
            Dictionary<int, long> vramOf = null;
            if (gpu) Graphics(view, out gpuOf, out vramOf);
            HashSet<int> ours = new HashSet<int>();
            ours.Add(self);
            bool grew = true;
            while (grew)
            {
                grew = false;
                foreach (Proc proc in all)
                {
                    if (ours.Contains(proc.Id) || !ours.Contains(proc.Parent)) continue;
                    Proc parent;
                    if (!byId.TryGetValue(proc.Parent, out parent) || parent.Created > proc.Created) continue;
                    ours.Add(proc.Id);
                    grew = true;
                }
            }
            Dictionary<string, UseRow> mine = new Dictionary<string, UseRow>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, UseRow> theirs = new Dictionary<string, UseRow>(StringComparer.OrdinalIgnoreCase);
            foreach (Proc proc in all)
            {
                bool capri = ours.Contains(proc.Id);
                string name = capri ? NameOf(proc.Image, proc.Id, brain) : Trim(proc.Image);
                Dictionary<string, UseRow> rows = capri ? mine : theirs;
                UseRow row;
                if (!rows.TryGetValue(name, out row)) { rows[name] = row = new UseRow { Name = name, Count = 0 }; if (view.GpuKnown) { row.Gpu = 0; row.Vram = 0; } }
                row.Count++;
                double used;
                if (cpu.TryGetValue(proc.Id, out used)) row.Cpu += used;
                row.Ram += Math.Max(0, proc.Private);
                double busy;
                if (gpuOf != null && gpuOf.TryGetValue(proc.Id, out busy)) row.Gpu = Math.Min(100, row.Gpu + busy);
                long card;
                if (vramOf != null && vramOf.TryGetValue(proc.Id, out card)) row.Vram += card;
                UseRow total = capri ? view.CapriTotal : view.OthersTotal;
                total.Cpu += cpu.TryGetValue(proc.Id, out used) ? used : 0;
                total.Ram += Math.Max(0, proc.Private);
                if (gpuOf != null && gpuOf.TryGetValue(proc.Id, out busy)) total.Gpu = Math.Min(100, total.Gpu + busy);
                if (vramOf != null && vramOf.TryGetValue(proc.Id, out card)) total.Vram += card;
            }
            if (view.GpuKnown && (vramOf == null || vramOf.Count == 0))
            {
                foreach (UseRow row in mine.Values) row.Vram = -1;
                foreach (UseRow row in theirs.Values) row.Vram = -1;
                view.CapriTotal.Vram = view.OthersTotal.Vram = -1;
            }
            view.CapriTotal.Count = ours.Count;
            view.OthersTotal.Count = all.Count - ours.Count;
            if (!view.GpuKnown) { view.CapriTotal.Gpu = view.OthersTotal.Gpu = -1; view.CapriTotal.Vram = view.OthersTotal.Vram = -1; }
            view.Capri.AddRange(mine.Values);
            view.Capri.Sort(delegate(UseRow a, UseRow b) { return a.Name == "Capri" ? -1 : b.Name == "Capri" ? 1 : b.Ram.CompareTo(a.Ram); });
            List<UseRow> others = new List<UseRow>(theirs.Values);
            others.RemoveAll(delegate(UseRow row) { return row.Name == "System" || row.Name == "Idle"; });
            others.Sort(delegate(UseRow a, UseRow b) { int order = Weight(b, view).CompareTo(Weight(a, view)); return order != 0 ? order : string.CompareOrdinal(a.Name, b.Name); });
            view.Others.AddRange(others);
            if (view.Cpu < 0 && span > 0) view.Cpu = Math.Min(100, view.CapriTotal.Cpu + view.OthersTotal.Cpu);
            return view;
        }

        internal static double Weight(UseRow row, UseView view)
        {
            double ram = view.RamTotal > 0 ? 100.0 * row.Ram / view.RamTotal : 0;
            double vram = view.VramTotal > 0 && row.Vram > 0 ? 100.0 * row.Vram / view.VramTotal : 0;
            return Math.Max(row.Cpu, 0) + ram + Math.Max(row.Gpu, 0) + vram * 0.5;
        }

        internal static bool EngineName(string instance, out int id, out string engine)
        {
            id = 0;
            engine = null;
            if (instance == null || !instance.StartsWith("pid_", StringComparison.Ordinal)) return false;
            int end = instance.IndexOf('_', 4);
            if (end < 0 || !int.TryParse(instance.Substring(4, end - 4), out id)) return false;
            int luid = instance.IndexOf("_luid_", StringComparison.Ordinal);
            int type = instance.IndexOf("_engtype_", StringComparison.Ordinal);
            if (luid < 0 || type < 0) return false;
            int phys = instance.IndexOf("_phys_", luid, StringComparison.Ordinal);
            string card = phys > luid ? instance.Substring(luid + 6, phys - luid - 6) : "";
            engine = card + "|" + instance.Substring(type + 9);
            return true;
        }

        private void Graphics(UseView view, out Dictionary<int, double> gpuOf, out Dictionary<int, long> vramOf)
        {
            gpuOf = null;
            vramOf = null;
            if (gpuBroken) return;
            try
            {
                Dictionary<string, CounterSample> next = new Dictionary<string, CounterSample>(StringComparer.Ordinal);
                Dictionary<int, Dictionary<string, double>> perProcess = new Dictionary<int, Dictionary<string, double>>();
                Dictionary<string, double> perEngine = new Dictionary<string, double>(StringComparer.Ordinal);
                InstanceDataCollectionCollection read = new PerformanceCounterCategory("GPU Engine").ReadCategory();
                InstanceDataCollection busy = read["utilization percentage"];
                if (busy == null) { gpuBroken = true; return; }
                bool any = false;
                foreach (InstanceData data in busy.Values)
                {
                    int id;
                    string engine;
                    if (!EngineName(data.InstanceName, out id, out engine)) continue;
                    next[data.InstanceName] = data.Sample;
                    CounterSample was;
                    if (!engines.TryGetValue(data.InstanceName, out was)) continue;
                    float value = CounterSample.Calculate(was, data.Sample);
                    if (float.IsNaN(value) || value < 0) continue;
                    any = true;
                    Dictionary<string, double> mine;
                    if (!perProcess.TryGetValue(id, out mine)) perProcess[id] = mine = new Dictionary<string, double>(StringComparer.Ordinal);
                    double sum;
                    mine.TryGetValue(engine, out sum);
                    mine[engine] = sum + value;
                    perEngine.TryGetValue(engine, out sum);
                    perEngine[engine] = sum + value;
                }
                engines = next;
                gpuOf = new Dictionary<int, double>();
                foreach (KeyValuePair<int, Dictionary<string, double>> entry in perProcess)
                {
                    double most = 0;
                    foreach (double value in entry.Value.Values) most = Math.Max(most, value);
                    gpuOf[entry.Key] = Math.Min(100, most);
                }
                double top = 0;
                foreach (double value in perEngine.Values) top = Math.Max(top, value);
                view.Gpu = any || engines.Count > 0 ? Math.Min(100, top) : -1;
                view.GpuKnown = true;
                vramOf = new Dictionary<int, long>();
                try
                {
                    InstanceDataCollectionCollection memory = new PerformanceCounterCategory("GPU Process Memory").ReadCategory();
                    InstanceDataCollection dedicated = memory["dedicated usage"];
                    if (dedicated != null)
                        foreach (InstanceData data in dedicated.Values)
                        {
                            string instance = data.InstanceName;
                            if (instance == null || !instance.StartsWith("pid_", StringComparison.Ordinal)) continue;
                            int end = instance.IndexOf('_', 4), id;
                            if (end < 0 || !int.TryParse(instance.Substring(4, end - 4), out id)) continue;
                            long have;
                            vramOf.TryGetValue(id, out have);
                            vramOf[id] = have + data.RawValue;
                        }
                    InstanceDataCollectionCollection cards = new PerformanceCounterCategory("GPU Adapter Memory").ReadCategory();
                    InstanceDataCollection cardUse = cards["dedicated usage"];
                    if (cardUse != null)
                    {
                        long used = 0;
                        foreach (InstanceData data in cardUse.Values) used += data.RawValue;
                        view.VramUsed = used;
                    }
                }
                catch (InvalidOperationException) { }
                catch (UnauthorizedAccessException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
            catch (InvalidOperationException error) { gpuBroken = true; Problem = "GPU counters: " + error.Message; }
            catch (UnauthorizedAccessException error) { gpuBroken = true; Problem = "GPU counters: " + error.Message; }
            catch (System.ComponentModel.Win32Exception error) { gpuBroken = true; Problem = "GPU counters: " + error.Message; }
            catch (FormatException error) { gpuBroken = true; Problem = "GPU counters: " + error.Message; }
        }
    }
}
