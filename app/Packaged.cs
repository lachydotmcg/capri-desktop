using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace CapriDesktop
{
    // The Microsoft Store copy (#114): the same Capri in an MSIX package, which Windows installs read-only and updates itself.
    // Whatever has to differ from a normal install asks here, and outside a package none of it changes anything.
    internal static class Packaged
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern int GetCurrentPackageFullName(ref int length, StringBuilder name);

        // Who updates her, in "Update through the Microsoft Store."
        internal const string Store = "the Microsoft Store";
        // The TaskId of the startup task in her package manifest.
        internal const string StartupId = "CapriStartup";
        internal const string StartupRefused = "Windows has Capri turned off under Startup apps. Turn her on in Settings > Apps > Startup.";
        internal const string StartupStuck = "Windows wouldn't change Capri's startup task.";
        private const int TooSmall = 122;
        // A call into Windows gets this long, so a stuck one can't hold her window up.
        private const int Patience = 2000;

        // Her package's full name, or null outside a package. The smoke test has no package, so it sets a stand-in.
        internal static string FullName = Read();
        // Windows' switch for starting with Windows. The smoke test swaps these: only a real package has one.
        internal static Func<bool> StartupOn = ReadStartup;
        internal static Func<bool, string> SetStartup = ChangeStartup;

        internal static bool Is { get { return FullName != null; } }

        private static string Read()
        {
            try
            {
                int length = 0;
                if (GetCurrentPackageFullName(ref length, null) != TooSmall) return null;
                StringBuilder name = new StringBuilder(length);
                return GetCurrentPackageFullName(ref length, name) == 0 ? name.ToString() : null;
            }
            catch (EntryPointNotFoundException) { return null; }
            catch (DllNotFoundException) { return null; }
        }

        // name_version_architecture_resource_publisher becomes name_publisher: her folder under Packages and her line in Windows'
        // microphone privacy settings. Null outside a package.
        internal static string Family
        {
            get
            {
                string[] parts = (FullName ?? "").Split('_');
                return parts.Length == 5 ? parts[0] + "_" + parts[4] : null;
            }
        }

        // Her data folder, under LocalApplicationData. Windows keeps the new folders a package makes in AppData in a private place
        // that only the package sees at the usual path, so File Explorer, Notepad and her brain's engine couldn't open what she
        // made there. A Store copy uses that private place by its real name, which everyone can open and which goes when she's
        // uninstalled. A folder the website's Capri made is real already, so she moves in and keeps everything in it.
        internal static string Home(string local)
        {
            string usual = Path.Combine(local, "Capri");
            string family = Family;
            if (family == null) return usual;
            Directory.CreateDirectory(usual);
            string kept = Path.Combine(local, "Packages", family, "LocalCache", "Local", "Capri");
            return Directory.Exists(kept) ? kept : usual;
        }

        // What to start her by. A package's folder changes with every update, so a Store copy goes by the execution alias in her
        // manifest, which stays put and starts her in her package. Everything else starts the file itself.
        internal static string Launcher(string exe)
        {
            return Is ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", Path.GetFileName(exe)) : exe;
        }

        // A package's Run entry goes in its private copy of the registry, where nothing runs it, so a Store copy starts with Windows
        // through her manifest's startup task. These run on a worker thread: her window's thread can't wait on Windows' own.
        private static bool ReadStartup()
        {
            try
            {
                Task<bool> reading = Task.Run(delegate
                {
                    StartupTaskState state = StartupTask.GetAsync(StartupId).AsTask().GetAwaiter().GetResult().State;
                    return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy;
                });
                return reading.Wait(Patience) && reading.Result;
            }
            catch (AggregateException) { return false; }
        }

        // Null when it worked, or the line to tell them. Someone who turned her off under Startup apps has to turn her back on there.
        private static string ChangeStartup(bool on)
        {
            try
            {
                Task<string> changing = Task.Run(delegate
                {
                    StartupTask task = StartupTask.GetAsync(StartupId).AsTask().GetAwaiter().GetResult();
                    if (!on) { task.Disable(); return null; }
                    StartupTaskState state = task.RequestEnableAsync().AsTask().GetAwaiter().GetResult();
                    return state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy ? null : state == StartupTaskState.DisabledByUser ? StartupRefused : StartupStuck;
                });
                return changing.Wait(Patience) ? changing.Result : StartupStuck;
            }
            catch (AggregateException) { return StartupStuck; }
        }
    }
}
