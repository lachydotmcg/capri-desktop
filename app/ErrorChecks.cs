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

        // The BRAIN note fits 60 characters, so the key and credit lines have short forms there that keep their fix.
        private void SmokeBrainNotes(List<string> checks)
        {
            string[,] lines =
            {
                { Brain.NoCredits, "Free key" }, { Brain.Meaning(402, ""), "Free key" },
                { Brain.PlusRejected, "platform.openai.com" }, { Brain.Meaning(401, "{\"error\":\"bad key\"}", false, true), "platform.openai.com" },
                { Brain.NotAKey, "Copy the whole key" }, { Brain.Unserved, "Free key" }, { Brain.NoFreeModels, "Refresh" },
                { Brain.Blocked, "Built-in" }
            };
            for (int i = 0; i < lines.GetLength(0); i++)
            {
                string brief = Brain.Brief(lines[i, 0]);
                if (brief == null || brief.Length > 60 || brief.EndsWith("...", StringComparison.Ordinal) || brief.IndexOf(lines[i, 1], StringComparison.Ordinal) < 0)
                    throw new Exception("The BRAIN note's short form lost its fix: " + lines[i, 0] + " became " + (brief ?? "nothing"));
            }
            // Lines that already fit are shown as they are.
            foreach (string fits in new[] { "Key rejected. Press Forget, then paste it again.", Brain.NewKey, "Nous Portal keys aren't supported yet.", "That key can't use any chat models." })
                if (fits.Length > 60 || Brain.Brief(fits) != null) throw new Exception("A key line that fits the BRAIN note got a short form or doesn't fit: " + fits);
            // Pasting something that isn't a key shows the whole short form, never one cut with "...".
            string status = mainframe.Status, noteWas = status.Substring(status.IndexOf(" | ", StringComparison.Ordinal) + 3);
            try
            {
                ConnectText("not-a-key-at-all");
                status = mainframe.Status;
                string shown = status.Substring(status.IndexOf(" | ", StringComparison.Ordinal) + 3);
                if (!shown.EndsWith(Brain.Brief(Brain.NotAKey), StringComparison.Ordinal) || shown.Contains("...")) throw new Exception("Pasting a key she doesn't know showed " + shown);
            }
            finally { mainframe.ShowProblem(noteWas); }
            checks.Add("PASS BRAIN note short forms: no credits, an OpenAI key without API credit, a pasted key she doesn't know, Gemini not offered where you are and OpenRouter with no free models each fit the note's 60 characters and keep their fix (Free key, platform.openai.com, copy the whole key, Refresh), lines that already fit are left alone, and pasting a non-key shows the whole short form");
        }

        private static bool Same(byte[] one, byte[] other)
        {
            if (one.Length != other.Length) return false;
            for (int i = 0; i < one.Length; i++) if (one[i] != other[i]) return false;
            return true;
        }
    }
}
