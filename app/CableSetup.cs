using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace CapriDesktop
{
    internal sealed class CableError : IOException
    {
        internal readonly string Line;
        internal CableError(string line, string detail) : base(detail) { Line = line; }
    }

    internal sealed class CableState
    {
        internal readonly List<MixDevice> Active, All;
        internal readonly string Detail;
        internal CableState(List<MixDevice> active, List<MixDevice> all, string detail) { Active = active; All = all; Detail = detail; }
    }

    // VB-CABLE's own setup, from the copy of VB-Audio's driver pack she carries (F66; Lachy: "It has to be pre packaged or easily
    // downloadable"). VB-Audio's licensing page (vb-audio.com/Services/licensing.htm, "VB-CABLE Distribution with other product")
    // lets other programs carry the pack, even inside their installs, as long as it stays donationware: people know it's VB-CABLE,
    // where it's from, and that they can donate. So Settings credits it with a link to VB-Audio's page, and on their click she
    // unpacks the pack untouched and opens VB-Audio's own setup, Windows asks them to let it run, and they click Install Driver in it.
    internal static class CableSetup
    {
        internal const string Resource = "Capri.VBCABLE.zip";
        // VBCABLE_Driver_Pack45.zip as VB-Audio publishes it.
        internal const string PackHash = "b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb";
        // VB-CABLE's page, where people can donate. Settings' credit opens it.
        internal const string Page = "https://vb-audio.com/Cable/";
        internal const string Credit = "VB-CABLE is donationware from vb-cable.com.";
        internal const string Better = "VB-CABLE usually sounds better than Steam's.";
        internal const string Running = "In VB-Audio's setup, click Install Driver.";
        internal const string Checking = "Setup closed. Checking for VB-CABLE.";
        // What Settings says once the setup has closed, by what she finds: the cable, a restart Windows is waiting on, an
        // error from the setup, or nothing new.
        internal const string Finish = "VB-CABLE is installed and ready.";
        internal const string Declined = "Windows didn't let the setup run.";
        internal const string Failed = "VB-CABLE's setup didn't start.";
        internal const string MissingPack = "No VB-CABLE pack here. Get it from VB-Audio.";
        internal const string BadPack = "Her VB-CABLE pack is damaged. Reinstall Capri.";
        internal const string TempFailed = "Couldn't unpack it to your temp folder.";
        internal const string TempDenied = "Windows won't let her use your temp folder.";
        internal const string TempFull = "Your temp drive is full. Free some space.";
        internal const string Removed = "The setup file is gone. Check your antivirus.";
        internal const string LongPath = "Your temp folder's path is too long for it.";
        internal const string Locked = "The setup's files are in use. Close it first.";
        internal const string NotInstalled = "No cable yet. If it installed, restart your PC.";
        internal const string RestartNeeded = "Restart your PC to finish installing VB-CABLE.";
        internal const string Unlisted = "Windows wouldn't list your sound devices.";
        internal const string WatchFailed = "Setup opened. She can't tell when it closes.";
        internal const string ExitFailed = "Setup ended with an error. Try it again.";
        internal static readonly string[] Lines = { Running, Checking, Finish, Declined, Failed, MissingPack, BadPack, TempFailed, TempDenied, TempFull, Removed, LongPath, Locked, NotInstalled, RestartNeeded, Unlisted, WatchFailed, ExitFailed };
        internal delegate string Unpacker(out FileStream kept);
        internal static Unpacker Extract = Unpack;
        internal static Func<Stream> Pack = delegate { return Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource); };
        // Opens a setup elevated. A real PC asks Windows; the smoke test hands it a stand-in.
        internal static Func<string, Process> Start = Elevated;
        internal static Func<bool?> Restart = ReadRestart;
        internal static Func<CableState> Inspect = Survey;
        internal static Action<int> Pause = Thread.Sleep;
        internal static Func<string, bool> InUse = Busy;
        private static readonly object folders = new object();
        private static readonly DateTime began = DateTime.UtcNow;

        // The pack's setup unpacked to a new folder of its own, after checking the pack is VB-Audio's, byte for byte. The setup
        // comes back held open in kept, read-only for everyone, and checked again through that hold, so nothing can change it
        // between here and Windows running it elevated. Its driver files carry their own signatures, which Windows checks before
        // it installs them. A folder that didn't unpack whole goes again. Throws when it can't be unpacked.
        internal static string Unpack(out FileStream kept)
        {
            lock (folders) return UnpackAt(out kept);
        }

        private static string UnpackAt(out FileStream kept)
        {
            kept = null;
            byte[] pack;
            using (Stream stream = Pack())
            {
                if (stream == null) throw new CableError(MissingPack, "Manifest resource " + Resource + " is missing.");
                using (MemoryStream copy = new MemoryStream()) { stream.CopyTo(copy); pack = copy.ToArray(); }
            }
            using (MemoryStream reading = new MemoryStream(pack))
                if (HashOf(reading) != PackHash) throw new CableError(BadPack, "Manifest resource " + Resource + " failed its SHA256 check.");
            string folder = Path.Combine(Path.GetTempPath(), "Capri VB-CABLE " + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string name = Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";
                string want;
                using (MemoryStream reading = new MemoryStream(pack))
                using (ZipArchive archive = new ZipArchive(reading, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = archive.GetEntry(name);
                    if (entry == null) throw new CableError(BadPack, "VB-CABLE's pack has no " + name + ".");
                    using (Stream inside = entry.Open()) want = HashOf(inside);
                    archive.ExtractToDirectory(folder);
                }
                string setup = Path.Combine(folder, name);
                kept = new FileStream(setup, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (HashOf(kept) != want) throw new CableError(Removed, name + " changed after it was unpacked.");
                return setup;
            }
            catch
            {
                if (kept != null) kept.Dispose();
                kept = null;
                Remove(folder);
                throw;
            }
        }

        // Unpacks the pack and opens its setup, unpacked to setup and held in kept. The running setup comes back, or null with why
        // in line.
        internal static Process Run(out string line, out string setup, out FileStream kept)
        {
            setup = null;
            kept = null;
            try { setup = Extract(out kept); }
            catch (Exception error) { line = Report(UnpackLine(error), "VB-CABLE pack didn't unpack: " + error); return null; }
            try
            {
                Process running = Start(setup);
                line = running == null ? Report(WatchFailed, "Starting " + setup + " returned no setup process; leaving the unpacked files for its window.") : Running;
                return running;
            }
            catch (Win32Exception error)
            {
                // ERROR_CANCELLED: they said no to Windows' prompt.
                line = Report(error.NativeErrorCode == 1223 ? Declined : error.NativeErrorCode == 2 || error.NativeErrorCode == 3 ? Removed : Failed,
                    "VB-CABLE setup didn't start (Windows " + error.NativeErrorCode + ", " + setup + "): " + error);
                return null;
            }
            catch (Exception error) { line = Report(error is FileNotFoundException ? Removed : Failed, "VB-CABLE setup didn't start (" + setup + "): " + error); return null; }
        }

        private static string UnpackLine(Exception error)
        {
            CableError own = error as CableError;
            if (own != null) return own.Line;
            if (error is PathTooLongException) return LongPath;
            if (error is FileNotFoundException) return Removed;
            if (error is UnauthorizedAccessException || (error.HResult & 65535) == 5) return TempDenied;
            if ((error.HResult & 65535) == 112) return TempFull;
            if ((error.HResult & 65535) == 32 || (error.HResult & 65535) == 33) return Locked;
            return TempFailed;
        }

        // The setup has closed. Windows can take a few seconds to show a new driver's devices, so she looks for up to 15.
        internal static string Closed(int? code)
        {
            bool? restart = null;
            try { restart = Restart(); }
            catch (Exception error) { Report(Checking, "Couldn't read Windows' restart flags: " + error); }
            if (code == 3010 || code == 1641) restart = true;
            CableState state = null;
            Exception failed = null;
            bool ready = false;
            for (int waited = 0; ; waited += 500)
            {
                try
                {
                    state = Inspect();
                    failed = null;
                    ready = state.Active.Exists(delegate(MixDevice cable) { return MicMix.IsVb(cable.Name) && cable.Far != null; });
                }
                catch (Exception error) { failed = error; }
                if (ready || waited >= 15000) break;
                Pause(500);
            }
            if (failed != null) return Report(Unlisted, "Couldn't check VB-CABLE after setup, exit " + code + ", restart " + restart + ": " + failed);
            string line = ready ? Finish : code == 3010 || code == 1641 ? RestartNeeded : code != null && code != 0 ? ExitFailed : NotInstalled;
            return Report(line, "Setup closed, exit " + (code == null ? "unknown" : code.ToString()) + "; restart " + (restart == null ? "unknown" : restart.ToString()) + "; " + state.Detail);
        }

        private static CableState Survey()
        {
            Endpoints pc = MicMix.Devices as Endpoints;
            if (pc != null) return pc.InspectCable();
            List<MixDevice> active = MicMix.Devices.Cables();
            return new CableState(active, active, Describe(active));
        }

        internal static string MissingFor(IMixDevices devices, string chosen, out string detail)
        {
            detail = "No active cable matched " + (chosen ?? "automatic") + ".";
            Endpoints pc = devices as Endpoints;
            if (pc != null)
            {
                CableState state = pc.InspectCable();
                detail += " " + state.Detail;
            }
            return MicMix.Missing;
        }

        internal static string Describe(List<MixDevice> devices)
        {
            List<string> names = new List<string>();
            foreach (MixDevice device in devices) names.Add(device.Name + " [" + device.Id + "] -> " + (device.Far ?? "no active far end"));
            return names.Count == 0 ? "no devices" : string.Join("; ", names.ToArray());
        }

        internal static string Report(string line, string detail) { Brain.Log("vb-cable", -1, line + " " + detail); return line; }

        private static bool? ReadRestart()
        {
            foreach (string path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending", @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired" })
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(path)) if (key != null) return true;
            using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager"))
            {
                string[] pending = key == null ? null : key.GetValue("PendingFileRenameOperations") as string[];
                if (pending != null && pending.Length > 0) return true;
            }
            return false;
        }

        // The hold on a setup lets go, and the folder it was unpacked into goes, once the setup's done with it.
        internal static void Tidy(string setup, FileStream kept)
        {
            if (kept != null) try { kept.Dispose(); } catch (Exception error) { Report(Locked, "Couldn't release the unpacked setup: " + error); }
            if (setup != null) Remove(Path.GetDirectoryName(setup));
        }

        // Folders an earlier run left behind, when Windows never said its setup had closed. One made since she started
        // belongs to a setup that's opening now.
        internal static void Clean()
        {
            try { lock (folders) CleanAt(Path.GetTempPath(), began); }
            catch (Exception error) { Report(Locked, "Couldn't check old VB-CABLE setup folders: " + error); }
        }

        internal static void Clean(string root)
        {
            lock (folders) CleanAt(root, DateTime.MaxValue);
        }

        private static void CleanAt(string root, DateTime before)
        {
            try
            {
                string parent = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                foreach (string folder in Directory.GetDirectories(parent, "Capri VB-CABLE *"))
                {
                    string full = Path.GetFullPath(folder);
                    if (!string.Equals(Path.GetDirectoryName(full), parent, StringComparison.OrdinalIgnoreCase)) continue;
                    try { if (OwnFolder(full) && Directory.GetCreationTimeUtc(full) < before && !InUse(full)) Remove(full); }
                    catch (Exception error) { Report(Locked, "Couldn't check whether setup still uses " + full + ": " + error); }
                }
            }
            catch (Exception error) { Report(Locked, "Couldn't list old VB-CABLE setup folders: " + error); }
        }

        // A folder she unpacked the pack into, and never any other.
        private static bool OwnFolder(string folder)
        {
            return folder != null && Regex.IsMatch(Path.GetFileName(folder), @"^Capri VB-CABLE [a-fA-F0-9]{8}$") && (File.GetAttributes(folder) & FileAttributes.ReparsePoint) == 0;
        }

        private static bool Busy(string folder)
        {
            foreach (string name in new[] { "VBCABLE_Setup", "VBCABLE_Setup_x64" })
                foreach (Process process in Process.GetProcessesByName(name))
                    using (process)
                    {
                        try
                        {
                            if (!process.HasExited && string.Equals(Path.GetDirectoryName(process.MainModule.FileName), folder, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        catch (Exception error)
                        {
                            Report(Locked, "Couldn't read a running VB-CABLE setup's path; keeping " + folder + ": " + error);
                            return true;
                        }
                    }
            return false;
        }

        private static void Remove(string folder)
        {
            try { if (folder != null && Directory.Exists(folder) && OwnFolder(folder)) Directory.Delete(folder, true); }
            catch (Exception error) { Report(Locked, "Couldn't remove unpacked folder " + folder + ": " + error); }
        }

        private static Process Elevated(string setup)
        {
            return Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup) });
        }

        private static string HashOf(Stream stream)
        {
            byte[] hash;
            using (SHA256 sha = SHA256.Create()) hash = sha.ComputeHash(stream);
            StringBuilder text = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash) text.Append(value.ToString("x2"));
            return text.ToString();
        }
    }
}
