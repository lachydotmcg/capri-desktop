using System;
using System.IO;
using Microsoft.Win32;

namespace CapriDesktop
{
    internal static class SetupFiles
    {
        // The package managers that can carry her. A silent install with --package <name> writes the name in Capri.package
        // in her folder, and her updater says to update through that manager instead of updating herself.
        internal static readonly string[] Managers = { "winget", "scoop", "chocolatey", "pacman" };
        internal const string Marker = "Capri.package";

        internal static int Mode(string[] args)
        {
            string package;
            return Mode(args, out package);
        }

        // 0 opens a window, 1 installs silently and 2 removes her silently. --package only goes with a silent install, and
        // anything else beside --silent or --package fails instead of opening a window a package manager can't click.
        internal static int Mode(string[] args, out string package)
        {
            bool forget;
            return Mode(args, out package, out forget);
        }

        internal static int Mode(string[] args, out string package, out bool forget)
        {
            package = null;
            forget = false;
            if (Array.IndexOf(args, "--silent") < 0 && Array.IndexOf(args, "--package") < 0) return 0;
            bool silent = false, remove = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--silent") silent = true;
                else if (args[i] == "--uninstall") remove = true;
                else if (args[i] == "--forget" && !forget) forget = true;
                else if (args[i] == "--package" && package == null && i + 1 < args.Length)
                {
                    package = args[++i];
                    if (Array.IndexOf(Managers, package) < 0) throw new ArgumentException("Capri doesn't know the package manager " + package + ". Use winget, scoop, chocolatey or pacman.");
                }
                else throw new ArgumentException("That silent setup command is invalid.");
            }
            if (!silent) throw new ArgumentException("--package goes with --silent.");
            if (remove && package != null) throw new ArgumentException("--package only goes with a silent install.");
            if (forget && !remove) throw new ArgumentException("--forget only goes with --uninstall.");
            return remove ? 2 : 1;
        }

        // The marker goes in whole or not at all: it's written under a new name nobody can guess, then swapped in, so a
        // failed write leaves the old one as it was.
        internal static void Mark(string folder, string package)
        {
            string marker = Path.Combine(folder, Marker);
            string temporary = marker + "." + Guid.NewGuid().ToString("N") + ".new";
            bool created = false;
            try
            {
                FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                created = true;
                using (stream)
                using (StreamWriter writer = new StreamWriter(stream)) writer.Write(package);
                if (File.Exists(marker)) File.Replace(temporary, marker, null);
                else File.Move(temporary, marker);
            }
            finally { if (created && File.Exists(temporary)) File.Delete(temporary); }
        }

        // Once she's removed no package manager carries her, so an install by hand later updates herself again.
        internal static void Unmark(string folder) { Patiently(delegate { File.Delete(Path.Combine(folder, Marker)); }); }

        internal static void ClearOld(string file) { Patiently(delegate { File.Delete(file + ".old"); }); }

        internal static void RemoveApp(string file)
        {
            Startup.Clear(file);
            Patiently(delegate { File.Delete(file); });
        }

        internal static void Retire(string file, bool running)
        {
            if (running)
            {
                ClearOld(file);
                File.Move(file, file + ".old");
            }
            else Patiently(delegate { File.Delete(file); });
            Gone(file);
        }

        // A removal that fails after the uninstaller retired itself puts it back under its own name, so her uninstall entry
        // can run it again: its old file when there is one, or a copy of the setup that's running, which is the same program.
        internal static void Unretire(string file, string running)
        {
            try
            {
                if (File.Exists(file)) return;
                if (File.Exists(file + ".old")) File.Move(file + ".old", file);
                else File.Copy(running, file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static void Gone(string file)
        {
            try { File.GetAttributes(file); }
            catch (FileNotFoundException) { return; }
            catch (DirectoryNotFoundException) { return; }
            throw new CapriSetup.SetupProblem("The uninstaller is still open. Close it and try removing Capri again.");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int id);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder name, ref int size);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        // A program started as administrator won't show its modules here, but Windows still names its file, so setup can tell
        // it's hers. held says this copy can't close it.
        internal static string FileOf(System.Diagnostics.Process process, out bool held)
        {
            held = false;
            try { return process.MainModule.FileName; }
            catch (System.ComponentModel.Win32Exception) { }
            catch (InvalidOperationException) { return null; }
            catch (NotSupportedException) { return null; }
            IntPtr handle = OpenProcess(0x1000, false, process.Id);
            if (handle == IntPtr.Zero) return null;
            try
            {
                System.Text.StringBuilder name = new System.Text.StringBuilder(1024);
                int size = name.Capacity;
                if (!QueryFullProcessImageName(handle, 0, name, ref size)) return null;
                held = true;
                return name.ToString(0, size);
            }
            finally { CloseHandle(handle); }
        }

        // The only folders setup's administrator copy will close things in: one named Capri with her install marker.
        internal static bool Closable(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            return string.Equals(Path.GetFileName(folder), "Capri", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(folder, ".capri-install"));
        }

        internal static void Patiently(Action remove)
        {
            for (int tries = 1; ; tries++)
            {
                try { remove(); return; }
                catch (IOException) { if (tries == 10) throw; }
                catch (UnauthorizedAccessException) { if (tries == 10) throw; }
                System.Threading.Thread.Sleep(300);
            }
        }
    }

    internal static class Forgetting
    {
        internal const string Room = "Capri Room";
        private static readonly string[] Browsers = { "msedge", "chrome", "brave", "chromium", "vivaldi", "opera" };

        internal static string[] Folders(string local) { return new[] { Path.Combine(local, "Capri"), Path.Combine(local, Room) }; }

        internal static bool Hers(string folder, string local)
        {
            if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(local)) return false;
            try
            {
                string full = Path.GetFullPath(folder).TrimEnd('\\');
                foreach (string mine in Folders(Path.GetFullPath(local))) if (string.Equals(full, mine.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            catch (PathTooLongException) { }
            return false;
        }

        internal static int Wipe(string local, string keep)
        {
            int left = 0;
            foreach (string folder in Folders(local)) left += Wipe(folder, local, keep);
            return left;
        }

        internal static int Wipe(string folder, string local, string keep)
        {
            if (!Hers(folder, local)) throw new IOException("Capri won't delete " + folder + ": it isn't one of her folders.");
            folder = Path.GetFullPath(folder).TrimEnd('\\');
            if (!Directory.Exists(folder)) return 0;
            if (keep != null) keep = Path.GetFullPath(keep);
            bool keeping = keep != null && keep.StartsWith(folder + "\\", StringComparison.OrdinalIgnoreCase);
            FreeBrowsers(folder);
            int left = 0;
            for (int pass = 1; pass <= 4; pass++)
            {
                left = Sweep(new DirectoryInfo(folder), keeping ? keep : null);
                if (left == 0 && !keeping && !Remove(new DirectoryInfo(folder))) left = 1;
                if (left == 0 || pass == 4) break;
                System.Threading.Thread.Sleep(500);
            }
            return left;
        }

        private static int Sweep(DirectoryInfo folder, string keep)
        {
            FileSystemInfo[] entries;
            try { entries = folder.GetFileSystemInfos(); }
            catch (DirectoryNotFoundException) { return 0; }
            catch (IOException) { return 1; }
            catch (UnauthorizedAccessException) { return 1; }
            int left = 0;
            foreach (FileSystemInfo entry in entries)
            {
                if (keep != null && string.Equals(entry.FullName, keep, StringComparison.OrdinalIgnoreCase)) continue;
                bool inside = (entry.Attributes & FileAttributes.Directory) != 0 && (entry.Attributes & FileAttributes.ReparsePoint) == 0;
                if (inside)
                {
                    left += Sweep((DirectoryInfo)entry, keep);
                    if (keep != null && keep.StartsWith(entry.FullName.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) continue;
                }
                if (!Remove(entry)) left++;
            }
            return left;
        }

        private static bool Remove(FileSystemInfo entry)
        {
            bool folder = (entry.Attributes & FileAttributes.Directory) != 0, link = (entry.Attributes & FileAttributes.ReparsePoint) != 0;
            for (int tries = 1; tries <= 2; tries++)
            {
                try
                {
                    if (folder) Directory.Delete(entry.FullName, false); else File.Delete(entry.FullName);
                    return true;
                }
                catch (DirectoryNotFoundException) { return true; }
                catch (FileNotFoundException) { return true; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (link || (entry.Attributes & FileAttributes.ReadOnly) == 0) return false;
                try { entry.Attributes &= ~FileAttributes.ReadOnly; }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
            }
            return false;
        }

        private static void FreeBrowsers(string folder)
        {
            System.Collections.Generic.List<string> locks = new System.Collections.Generic.List<string>();
            foreach (string profile in new[] { folder, Path.Combine(folder, "her-computer", "browser") })
                if (File.Exists(Path.Combine(profile, "lockfile"))) locks.Add(Path.Combine(profile, "lockfile"));
            if (locks.Count == 0) return;
            foreach (int id in Holders(locks.ToArray()))
            {
                try
                {
                    using (System.Diagnostics.Process browser = System.Diagnostics.Process.GetProcessById(id))
                    {
                        if (Array.IndexOf(Browsers, browser.ProcessName.ToLowerInvariant()) < 0) continue;
                        browser.Kill();
                        browser.WaitForExit(5000);
                    }
                }
                catch (ArgumentException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct Unique { internal int Id; internal System.Runtime.InteropServices.ComTypes.FILETIME Started; }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct Holder
        {
            internal Unique Process;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 256)] internal string App;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 64)] internal string Service;
            internal int Type;
            internal int Status;
            internal int Session;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)] internal bool Restartable;
        }

        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int RmStartSession(out int session, int flags, System.Text.StringBuilder key);
        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")] private static extern int RmEndSession(int session);
        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)] private static extern int RmRegisterResources(int session, int files, string[] names, int processes, IntPtr unique, int services, string[] serviceNames);
        [System.Runtime.InteropServices.DllImport("rstrtmgr.dll")] private static extern int RmGetList(int session, out int needed, ref int count, [System.Runtime.InteropServices.In, System.Runtime.InteropServices.Out] Holder[] holders, ref int reasons);

        internal static System.Collections.Generic.List<int> Holders(string[] files)
        {
            System.Collections.Generic.List<int> found = new System.Collections.Generic.List<int>();
            int session;
            try { if (RmStartSession(out session, 0, new System.Text.StringBuilder(64)) != 0) return found; }
            catch (DllNotFoundException) { return found; }
            catch (EntryPointNotFoundException) { return found; }
            try
            {
                if (RmRegisterResources(session, files.Length, files, 0, IntPtr.Zero, 0, null) != 0) return found;
                int needed, count = 0, reasons = 0;
                int result = RmGetList(session, out needed, ref count, null, ref reasons);
                if (result != 234 || needed <= 0) return found;
                Holder[] holders = new Holder[needed];
                count = needed;
                if (RmGetList(session, out needed, ref count, holders, ref reasons) != 0) return found;
                for (int i = 0; i < count && i < holders.Length; i++) if (!found.Contains(holders[i].Process.Id)) found.Add(holders[i].Process.Id);
            }
            finally { RmEndSession(session); }
            return found;
        }
    }

    // Starting with Windows is her value in the user's Run key, the same entry Task Manager's Startup apps turns on and off.
    internal static class Startup
    {
        internal static string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";
        internal static string ApprovedKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";
        internal const string Name = "Capri";

        internal static bool On(string exe)
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (run == null || !Starts(run.GetValue(Name) as string, exe)) return false;
                using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey))
                {
                    byte[] state = approved == null ? null : approved.GetValue(Name) as byte[];
                    return state == null || state.Length == 0 || (state[0] & 1) == 0;
                }
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            catch (IOException) { return false; }
        }

        internal static bool Set(string exe, bool on)
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (on) run.SetValue(Name, "\"" + Path.GetFullPath(exe) + "\"");
                    else run.DeleteValue(Name, false);
                }
                using (RegistryKey approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                    if (approved != null) approved.DeleteValue(Name, false);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (System.Security.SecurityException) { return false; }
            catch (IOException) { return false; }
        }

        // Removing a Capri only takes the entry away when it starts that Capri, not another copy.
        internal static void Clear(string exe)
        {
            string command = null;
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey))
                    if (run != null) command = run.GetValue(Name) as string;
            }
            catch (UnauthorizedAccessException) { return; }
            catch (System.Security.SecurityException) { return; }
            catch (IOException) { return; }
            if (Starts(command, exe)) Set(exe, false);
        }

        internal static bool Starts(string command, string exe)
        {
            if (string.IsNullOrWhiteSpace(command)) return false;
            string path = command.Trim();
            if (path.StartsWith("\"", StringComparison.Ordinal))
            {
                int end = path.IndexOf('"', 1);
                path = end > 1 ? path.Substring(1, end - 1) : path.Trim('"');
            }
            try { return string.Equals(Path.GetFullPath(path), Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase); }
            catch (ArgumentException) { return false; }
            catch (NotSupportedException) { return false; }
            catch (IOException) { return false; }
        }
    }
}
