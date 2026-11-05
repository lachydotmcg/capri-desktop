using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    // Smoke checks for how her error lines read: what she says, what the Mainframe shows, and what only errors.txt keeps.
    internal sealed partial class Companion
    {
        // Settings that can't be read are copied as they are before anything can save over them, get a line in errors.txt, and
        // are told once. A missing file is a new install and says nothing.
        private void SmokeUnreadableSettings(List<string> checks, string output)
        {
            string folder = Path.Combine(output, "unreadable-settings");
            string settings = Path.Combine(folder, "settings.json"), kept = Path.Combine(folder, UnreadableName), log = Path.Combine(folder, "errors.txt");
            string logWas = Brain.LogPath, noticeWas = unreadableNotice;
            Action<string> shownWas = unreadableShown;
            try
            {
                if (Directory.Exists(folder)) Directory.Delete(folder, true);
                Directory.CreateDirectory(folder);
                Brain.LogPath = log;
                // A stand-in for an encrypted key: the copy keeps it byte for byte, and neither errors.txt nor the notice shows it.
                const string key = "AQAAANCMnd8BFdERjHoAwEstandin";
                byte[] broken = Encoding.UTF8.GetBytes("{\"Size\":300,\"Watch\":true,\"NvidiaKey\":\"" + key + "\",\"Tier\":1,\"Tie");
                File.WriteAllBytes(settings, broken);
                string notice;
                Preferences fresh = ReadPreferences(settings, out notice);
                bool copied = File.Exists(kept) && Same(File.ReadAllBytes(kept), broken) && Same(File.ReadAllBytes(settings), broken);
                bool started = fresh != null && fresh.Size == 240 && !fresh.Watch && fresh.NvidiaKey == null && fresh.Tier == 0;
                string[] logged = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
                bool noted = logged.Length == 1 && logged[0].Contains(" | startup | - | ") && logged[0].Contains("couldn't be read") && logged[0].Contains(UnreadableName) && !logged[0].Contains(key);
                bool told = notice != null && notice.StartsWith("Her settings couldn't be read, so they were reset.", StringComparison.Ordinal) && notice.Contains("Add your keys again")
                    && notice.Contains(UnreadableName + " in %LocalAppData%\\Capri.") && !notice.Contains(key) && notice.Length <= 200;
                // Told once: the tray shows it, and showing again has nothing left to say.
                int shown = 0;
                string shownText = null;
                unreadableShown = delegate(string text) { shown++; shownText = text; };
                unreadableNotice = notice;
                ShowUnreadableNotice();
                ShowUnreadableNotice();
                bool once = shown == 1 && shownText == notice && unreadableNotice == null;
                // Unreadable again later: the newest unreadable file replaces the old copy.
                byte[] again = Encoding.UTF8.GetBytes("not json at all");
                File.WriteAllBytes(settings, again);
                string againNotice;
                ReadPreferences(settings, out againNotice);
                bool replaced = againNotice != null && Same(File.ReadAllBytes(kept), again);
                // A new install has no file: fresh settings, no copy, no line and no notice.
                File.Delete(settings);
                File.Delete(kept);
                int linesBefore = File.ReadAllLines(log).Length;
                string newNotice;
                Preferences newInstall = ReadPreferences(settings, out newNotice);
                bool quietNew = newInstall != null && newNotice == null && !File.Exists(kept) && File.ReadAllLines(log).Length == linesBefore;
                // Readable settings load as they were, with nothing said.
                File.WriteAllText(settings, "{\"Size\":300}");
                string readNotice;
                bool readable = ReadPreferences(settings, out readNotice).Size == 300 && readNotice == null && !File.Exists(kept);
                if (!copied || !started || !noted || !told || !once || !replaced || !quietNew || !readable)
                    throw new Exception("Unreadable settings went wrong: copied " + copied + ", fresh " + started + ", logged " + noted + " (" + string.Join(" / ", logged) + "), told " + told + " (" + notice + "), once " + once + " (" + shown + "), replaced " + replaced + ", new install quiet " + quietNew + ", readable " + readable);
                checks.Add("PASS unreadable settings: settings.json she can't read is copied byte for byte to " + UnreadableName + " before anything saves over it (keys stay encrypted, and errors.txt and the notice never show them), errors.txt gets one startup line with only the error's kind, the tray says once in plain words that her settings were reset and where the old file is, a later unreadable file replaces the copy, and a new install or readable settings say nothing");
            }
            finally
            {
                Brain.LogPath = logWas;
                unreadableNotice = noticeWas;
                unreadableShown = shownWas;
                try { Directory.Delete(folder, true); } catch (IOException) { }
            }
        }

        private static bool Same(byte[] one, byte[] other)
        {
            if (one.Length != other.Length) return false;
            for (int i = 0; i < one.Length; i++) if (one[i] != other[i]) return false;
            return true;
        }
    }
}
