using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // One app's share of their PC over the sample: its program, how much of the whole processor it used and the memory its
    // processes hold.
    internal sealed class AppUse
    {
        internal string App;
        internal double Cpu;
        internal long Memory;
    }

    // How busy their PC is right now: the whole processor from 0 to 100, memory in use and in all, the apps using it, each fixed
    // drive's free space, and a laptop's battery, null on a desktop.
    internal sealed class PcUse
    {
        internal double Cpu;
        internal long MemoryUsed, MemoryTotal;
        internal List<AppUse> Apps = new List<AppUse>();
        internal List<string> Drives = new List<string>();
        internal string Battery;
    }

    // Reads how their PC is doing over half a second, the way Task Manager does, without changing anything (Lachy, 3 Nov: "ensure
    // they have the tools they need to perform everything themself").
    internal static class Usage
    {
        internal static PcUse Read()
        {
            PcUse use = new PcUse();
            Dictionary<int, TimeSpan> before = new Dictionary<int, TimeSpan>();
            long idleBefore, kernelBefore, userBefore;
            bool timed = GetSystemTimes(out idleBefore, out kernelBefore, out userBefore);
            foreach (Process process in Process.GetProcesses())
            {
                try { before[process.Id] = process.TotalProcessorTime; }
                catch (Exception) { }
                finally { process.Dispose(); }
            }
            Stopwatch clock = Stopwatch.StartNew();
            Thread.Sleep(500);
            Dictionary<string, AppUse> apps = new Dictionary<string, AppUse>(StringComparer.OrdinalIgnoreCase);
            double spent = Math.Max(1, clock.Elapsed.TotalMilliseconds) * Environment.ProcessorCount;
            foreach (Process process in Process.GetProcesses())
            {
                try
                {
                    if (process.Id == 0) continue;
                    TimeSpan was;
                    double used = before.TryGetValue(process.Id, out was) ? Math.Max(0, (process.TotalProcessorTime - was).TotalMilliseconds) : 0;
                    long memory = process.WorkingSet64;
                    AppUse app;
                    if (!apps.TryGetValue(process.ProcessName, out app)) apps[process.ProcessName] = app = new AppUse { App = process.ProcessName };
                    app.Cpu += used / spent * 100;
                    app.Memory += memory;
                }
                catch (Exception) { }
                finally { process.Dispose(); }
            }
            use.Apps.AddRange(apps.Values);
            long idleAfter, kernelAfter, userAfter;
            if (timed && GetSystemTimes(out idleAfter, out kernelAfter, out userAfter))
            {
                // The kernel's time includes the idle time.
                double all = (kernelAfter - kernelBefore) + (userAfter - userBefore), idle = idleAfter - idleBefore;
                use.Cpu = all <= 0 ? 0 : Math.Max(0, Math.Min(100, (all - idle) / all * 100));
            }
            else
            {
                double sum = 0;
                foreach (AppUse app in use.Apps) if (!string.Equals(app.App, "Idle", StringComparison.OrdinalIgnoreCase)) sum += app.Cpu;
                use.Cpu = Math.Min(100, sum);
            }
            MemoryStatus memoryNow = new MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(MemoryStatus)) };
            if (GlobalMemoryStatusEx(ref memoryNow))
            {
                use.MemoryTotal = (long)memoryNow.TotalPhys;
                use.MemoryUsed = (long)(memoryNow.TotalPhys - memoryNow.AvailPhys);
            }
            foreach (DriveInfo drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    use.Drives.Add(drive.Name.TrimEnd('\\') + " " + Size(drive.AvailableFreeSpace) + " free of " + Size(drive.TotalSize));
                }
                catch (Exception) { }
            }
            PowerStatus power = SystemInformation.PowerStatus;
            if ((power.BatteryChargeStatus & BatteryChargeStatus.NoSystemBattery) == 0 && power.BatteryChargeStatus != BatteryChargeStatus.Unknown)
                use.Battery = (int)Math.Round(power.BatteryLifePercent * 100) + "%" + (power.PowerLineStatus == PowerLineStatus.Online ? ", plugged in" : ", on battery");
            return use;
        }

        // Bytes the way Task Manager says them: 45 GB, 1.2 TB.
        internal static string Size(long bytes)
        {
            double gigabytes = bytes / 1073741824.0;
            if (gigabytes >= 1000) return Math.Round(gigabytes / 1024, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " TB";
            return (gigabytes >= 10 ? Math.Round(gigabytes) : Math.Round(gigabytes, 1)).ToString(System.Globalization.CultureInfo.InvariantCulture) + " GB";
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MemoryStatus
        {
            internal uint Length, Load;
            internal ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
        }

        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
    }
}
