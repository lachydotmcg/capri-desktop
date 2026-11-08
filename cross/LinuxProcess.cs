using System;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    internal static class LinuxProcess
    {
        [StructLayout(LayoutKind.Explicit, Size = 256)]
        private struct FileIdentity
        {
            [FieldOffset(0)] internal uint Mask;
            [FieldOffset(20)] internal uint Uid;
            [FieldOffset(28)] internal ushort Mode;
            [FieldOffset(32)] internal ulong Inode;
            [FieldOffset(136)] internal uint DeviceMajor;
            [FieldOffset(140)] internal uint DeviceMinor;
        }
        [DllImport("libc", SetLastError = true)] private static extern int statx(int directory, string path, int flags, uint mask, out FileIdentity result);
        [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
        [DllImport("libc")] private static extern int close(int fd);
        [DllImport("libc")] private static extern uint getuid();
        [DllImport("libc", SetLastError = true)] private static extern int pidfd_open(int pid, uint flags);
        [DllImport("libc", SetLastError = true)] private static extern int pidfd_send_signal(int fd, int signal, IntPtr info, uint flags);

        private static FileIdentity Stat(int fd, string path, int flags)
        {
            FileIdentity result;
            if (statx(fd, path, flags, 0x7ff, out result) != 0 || (result.Mask & 0x107) != 0x107)
                throw new InvalidOperationException("The process identity is no longer readable.");
            return result;
        }

        private static string Start(string path, int pid)
        {
            string raw = File.ReadAllText(path + "/stat");
            int left = raw.IndexOf('('), right = raw.LastIndexOf(')');
            if (left < 1 || right < left || raw.Substring(0, left).Trim() != pid.ToString(CultureInfo.InvariantCulture))
                throw new InvalidOperationException("The process identity changed.");
            string[] fields = raw.Substring(right + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 20 || fields[0] == "Z") throw new InvalidOperationException("The process has exited.");
            return fields[19];
        }

        internal static string Identity(int pid)
        {
            if (pid <= 0) throw new InvalidOperationException("Invalid process identity.");
            string path = "/proc/" + pid.ToString(CultureInfo.InvariantCulture);
            string before = Start(path, pid);
            if (Stat(-100, path, 0).Uid != getuid()) throw new InvalidOperationException("The process belongs to another user.");
            int fd = open(path + "/exe", 0x200000 | 0x80000);
            if (fd < 0) throw new InvalidOperationException("The executable is no longer readable.");
            try
            {
                FileIdentity held = Stat(fd, "", 0x1000), now = Stat(-100, path + "/exe", 0);
                string executable = new FileInfo(path + "/exe").LinkTarget;
                if ((held.Mode & 0xf000) != 0x8000 || string.IsNullOrEmpty(executable) || !Path.IsPathFullyQualified(executable) || executable.Length > 4096
                    || held.Inode != now.Inode || held.DeviceMajor != now.DeviceMajor || held.DeviceMinor != now.DeviceMinor
                    || Stat(-100, path, 0).Uid != getuid() || Start(path, pid) != before)
                    throw new InvalidOperationException("The process or executable changed.");
                foreach (char value in executable) if (char.IsControl(value)) throw new InvalidOperationException("Invalid executable path.");
                return pid + ":" + before + ":" + getuid() + ":" + held.DeviceMajor + ":" + held.DeviceMinor + ":" + held.Inode + ":" + executable;
            }
            finally { close(fd); }
        }

        internal static void ForceClose(int pid, string expected)
        {
            if (pid <= 1 || pid == Environment.ProcessId) throw new InvalidOperationException("This process cannot be closed.");
            int fd = pidfd_open(pid, 0);
            if (fd < 0) throw new InvalidOperationException("An exact process handle could not be opened.");
            try
            {
                if (Identity(pid) != expected) throw new InvalidOperationException("The target process changed; no signal was sent.");
                if (pidfd_send_signal(fd, 9, IntPtr.Zero, 0) != 0) throw new InvalidOperationException("The process did not accept the signal.");
            }
            finally { close(fd); }
        }
    }
}
