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
            package = null;
            if (Array.IndexOf(args, "--silent") < 0 && Array.IndexOf(args, "--package") < 0) return 0;
            bool silent = false, remove = false;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--silent") silent = true;
                else if (args[i] == "--uninstall") remove = true;
                else if (args[i] == "--package" && package == null && i + 1 < args.Length)
                {
                    package = args[++i];
                    if (Array.IndexOf(Managers, package) < 0) throw new ArgumentException("Capri doesn't know the package manager " + package + ". Use winget, scoop, chocolatey or pacman.");
                }
                else throw new ArgumentException("That silent setup command is invalid.");
            }
            if (!silent) throw new ArgumentException("--package goes with --silent.");
            if (remove && package != null) throw new ArgumentException("--package only goes with a silent install.");
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
            throw new IOException("The uninstaller is still open. Close it and try removing Capri again.");
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
