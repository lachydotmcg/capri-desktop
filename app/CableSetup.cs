using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CapriDesktop
{
    // VB-CABLE's own setup, from the copy of VB-Audio's driver pack she carries (F66; Lachy: "It has to be pre packaged or easily
    // downloadable"). VB-Audio lets the pack be passed on as it is, but not folded into another program's install, so she never
    // installs it herself: on their click she unpacks the pack untouched and opens VB-Audio's own setup, Windows asks them to let
    // it run, and they click Install Driver in it. VB-CABLE is donationware from vb-cable.com, and Settings says so beside it.
    internal static class CableSetup
    {
        internal const string Resource = "Capri.VBCABLE.zip";
        // VBCABLE_Driver_Pack45.zip as VB-Audio publishes it.
        internal const string PackHash = "b950e39f01af1d04ea623c8f6d8eb9b6ea5c477c637295fabf20631c85116bfb";
        internal const string Credit = "VB-CABLE is donationware from vb-cable.com.";
        internal const string Running = "In VB-Audio's setup, click Install Driver.";
        internal const string Finish = "If it installed, restart your PC.";
        internal const string Declined = "Windows didn't let the setup run.";
        internal const string Failed = "VB-CABLE's setup didn't start.";
        // Opens a setup elevated. A real PC asks Windows; the smoke test hands it a stand-in.
        internal static Func<string, Process> Start = Elevated;

        // The pack's setup unpacked to a new folder of its own, after checking the pack is VB-Audio's, byte for byte. Throws when
        // it can't be.
        internal static string Unpack()
        {
            byte[] pack;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null) throw new IOException("She doesn't carry VB-CABLE's pack.");
                using (MemoryStream copy = new MemoryStream()) { stream.CopyTo(copy); pack = copy.ToArray(); }
            }
            string hash;
            using (SHA256 sha = SHA256.Create()) hash = Hex(sha.ComputeHash(pack));
            if (hash != PackHash) throw new IOException("Her copy of VB-CABLE's pack isn't VB-Audio's.");
            string folder = Path.Combine(Path.GetTempPath(), "Capri VB-CABLE " + Guid.NewGuid().ToString("N").Substring(0, 8));
            using (MemoryStream reading = new MemoryStream(pack))
            using (ZipArchive archive = new ZipArchive(reading, ZipArchiveMode.Read)) archive.ExtractToDirectory(folder);
            string setup = Path.Combine(folder, Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe");
            if (!File.Exists(setup)) throw new IOException("VB-CABLE's pack has no " + Path.GetFileName(setup) + ".");
            return setup;
        }

        // Unpacks the pack and opens its setup, unpacked to setup. The running setup comes back, or null with why in line.
        internal static Process Run(out string line, out string setup)
        {
            setup = null;
            try { setup = Unpack(); }
            catch (Exception error) { line = Failed; Brain.Log("vb-cable", -1, "Her VB-CABLE pack didn't unpack: " + error.Message); return null; }
            try
            {
                Process running = Start(setup);
                line = running == null ? Finish : Running;
                return running;
            }
            catch (Win32Exception error)
            {
                // ERROR_CANCELLED: they said no to Windows' prompt.
                line = error.NativeErrorCode == 1223 ? Declined : Failed;
                if (error.NativeErrorCode != 1223) Brain.Log("vb-cable", -1, "VB-CABLE's setup didn't start: " + error.Message);
                return null;
            }
            catch (Exception error) { line = Failed; Brain.Log("vb-cable", -1, "VB-CABLE's setup didn't start: " + error.Message); return null; }
        }

        // The folder a setup was unpacked into goes once the setup's done with it.
        internal static void Tidy(string setup)
        {
            try
            {
                string folder = Path.GetDirectoryName(setup);
                if (folder != null && Path.GetFileName(folder).StartsWith("Capri VB-CABLE ", StringComparison.Ordinal)) Directory.Delete(folder, true);
            }
            catch (Exception) { }
        }

        private static Process Elevated(string setup)
        {
            return Process.Start(new ProcessStartInfo(setup) { UseShellExecute = true, Verb = "runas", WorkingDirectory = Path.GetDirectoryName(setup) });
        }

        private static string Hex(byte[] bytes)
        {
            StringBuilder text = new StringBuilder(bytes.Length * 2);
            foreach (byte value in bytes) text.Append(value.ToString("x2"));
            return text.ToString();
        }
    }
}
