using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CapriDesktop
{
    internal static class MacNative
    {
        [StructLayout(LayoutKind.Explicit, Size = 136)]
        private struct ProcessInfo
        {
            [FieldOffset(120)] internal ulong Seconds;
            [FieldOffset(128)] internal ulong Microseconds;
        }

        [DllImport("/usr/lib/libproc.dylib")]
        private static extern int proc_pidinfo(int pid, int flavor, ulong arg, out ProcessInfo info, int size);

        internal static string ProcessIdentity(int pid)
        {
            if (proc_pidinfo(pid, 3, 0, out ProcessInfo info, 136) != 136)
                throw new IOException("Cannot bind the terminal job to Capri's process.");
            return info.Seconds.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + info.Microseconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        internal static string Python()
        {
            string found = ArchApps.Executable("python3");
            if (found != null) return found;
            foreach (string path in new[] { "/opt/homebrew/bin/python3", "/usr/local/bin/python3", "/usr/bin/python3" })
                if (File.Exists(path)) return path;
            return null;
        }

        internal static string ShellWord(string value) => "'" + value.Replace("'", "'\"'\"'") + "'";
        internal static string AppleString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";

        internal static ProcessStartInfo Terminal(string python, string worker, string directory)
        {
            string command = ShellWord(python) + " -B -E -S " + ShellWord(worker) + " " + ShellWord(directory);
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("-e");
            start.ArgumentList.Add("on run argv\ntell application \"Terminal\"\nactivate\ndo script (item 1 of argv)\nend tell\nend run");
            start.ArgumentList.Add(command);
            return start;
        }

        internal static ProcessStartInfo Browser()
        {
            var start = new ProcessStartInfo("/usr/bin/osascript") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            start.ArgumentList.Add("-");
            return start;
        }
    }
}
