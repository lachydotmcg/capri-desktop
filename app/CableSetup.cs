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
        internal const string Finish = "If it installed, restart your PC.";
        internal const string Declined = "Windows didn't let the setup run.";
        internal const string Failed = "VB-CABLE's setup didn't start.";
        // Opens a setup elevated. A real PC asks Windows; the smoke test hands it a stand-in.
        internal static Func<string, Process> Start = Elevated;

        // The pack's setup unpacked to a new folder of its own, after checking the pack is VB-Audio's, byte for byte. The setup
        // comes back held open in kept, read-only for everyone, and checked again through that hold, so nothing can change it
        // between here and Windows running it elevated. Its driver files carry their own signatures, which Windows checks before
        // it installs them. A folder that didn't unpack whole goes again. Throws when it can't be unpacked.
        internal static string Unpack(out FileStream kept)
        {
            kept = null;
            byte[] pack;
            using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource))
            {
                if (stream == null) throw new IOException("She doesn't carry VB-CABLE's pack.");
                using (MemoryStream copy = new MemoryStream()) { stream.CopyTo(copy); pack = copy.ToArray(); }
            }
            if (HashOf(new MemoryStream(pack)) != PackHash) throw new IOException("Her copy of VB-CABLE's pack isn't VB-Audio's.");
            string folder = Path.Combine(Path.GetTempPath(), "Capri VB-CABLE " + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                string name = Environment.Is64BitOperatingSystem ? "VBCABLE_Setup_x64.exe" : "VBCABLE_Setup.exe";
                string want;
                using (MemoryStream reading = new MemoryStream(pack))
                using (ZipArchive archive = new ZipArchive(reading, ZipArchiveMode.Read))
                {
                    ZipArchiveEntry entry = archive.GetEntry(name);
                    if (entry == null) throw new IOException("VB-CABLE's pack has no " + name + ".");
                    using (Stream inside = entry.Open()) want = HashOf(inside);
                    archive.ExtractToDirectory(folder);
                }
                string setup = Path.Combine(folder, name);
                kept = new FileStream(setup, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (HashOf(kept) != want) throw new IOException(name + " changed after it was unpacked.");
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
            try { setup = Unpack(out kept); }
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

        // The hold on a setup lets go, and the folder it was unpacked into goes, once the setup's done with it.
        internal static void Tidy(string setup, FileStream kept)
        {
            if (kept != null) try { kept.Dispose(); } catch (Exception) { }
            if (setup != null) Remove(Path.GetDirectoryName(setup));
        }

        // A folder she unpacked the pack into, and never any other.
        private static void Remove(string folder)
        {
            try { if (folder != null && Path.GetFileName(folder).StartsWith("Capri VB-CABLE ", StringComparison.Ordinal) && Directory.Exists(folder)) Directory.Delete(folder, true); }
            catch (Exception) { }
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
