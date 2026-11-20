using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private void SmokePersonal(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            string folderWas = PersonalSettings.Folder, memoryWas = Brain.MemoryPath;
            try
            {
                PersonalSettings.Folder = Path.Combine(output, PersonalSettings.FolderName);
                string sys = PersonalSettings.Make();
                if (sys == null || !File.Exists(sys) || Path.GetFileName(sys) != PersonalSettings.SysName) throw new Exception("sys.md was not made in personalSettings.");
                string stock = File.ReadAllText(sys);
                if (stock.IndexOf(PersonalSettings.NeverWatchKey + ":", StringComparison.Ordinal) < 0 || stock.IndexOf(PersonalSettings.AskKey + ": 1", StringComparison.Ordinal) < 0) throw new Exception("sys.md is missing a setting: " + stock);
                if (PersonalSettings.NeverWatch().Length != 0 || PersonalSettings.Skipped("Discord.exe", "Discord") || !PersonalSettings.AsksAboutThem) throw new Exception("A fresh sys.md is not empty and asking.");
                checks.Add("PASS her personalSettings folder has sys.md, with no app hidden from her and her questions on");

                File.WriteAllText(sys, stock.Replace(PersonalSettings.NeverWatchKey + ": ", PersonalSettings.NeverWatchKey + ": discord, Brave.exe, x, \"qview\""));
                string[] never = PersonalSettings.NeverWatch();
                if (never.Length != 3 || never[0] != "discord" || never[1] != "Brave" || never[2] != "qview") throw new Exception("Her never watch list read as " + string.Join("|", never));
                if (!PersonalSettings.Skipped("Discord.exe", "") || !PersonalSettings.Skipped("", "New tab - Brave") || !PersonalSettings.Skipped("qview", "holiday.png")) throw new Exception("A program on her never watch list was watched.");
                if (PersonalSettings.Skipped("notepad.exe", "Untitled - Notepad") || PersonalSettings.Skipped(null, null)) throw new Exception("A program off her never watch list was skipped.");
                checks.Add("PASS sys.md's never watch list skips programs by part of their name or window title, and a one letter entry skips nothing");

                Thread.Sleep(20);
                File.WriteAllText(sys, File.ReadAllText(sys).Replace(PersonalSettings.AskKey + ": 1", PersonalSettings.AskKey + ": off"));
                if (PersonalSettings.AsksAboutThem) throw new Exception("ask about me: off did not stop her questions.");
                File.WriteAllText(sys, "# mine\n" + PersonalSettings.NeverWatchKey + ": steam\n");
                PersonalSettings.Make();
                string topped = File.ReadAllText(sys);
                if (!topped.StartsWith("# mine\n" + PersonalSettings.NeverWatchKey + ": steam\n", StringComparison.Ordinal) || topped.IndexOf(PersonalSettings.AskKey + ": 1", StringComparison.Ordinal) < 0 || topped.Split(new[] { PersonalSettings.NeverWatchKey + ":" }, StringSplitOptions.None).Length != 2) throw new Exception("An older sys.md was not topped up in place: " + topped);
                if (PersonalSettings.NeverWatch().Length != 1 || !PersonalSettings.AsksAboutThem) throw new Exception("The topped up sys.md lost their edit.");
                checks.Add("PASS ask about me: off stops her questions, and an older sys.md gains only the settings it lacks");

                Brain.MemoryPath = Path.Combine(output, "me.md");
                File.WriteAllText(Brain.MemoryPath, Brain.NotesHead);
                DateTime start = new DateTime(2026, 11, 16, 9, 0, 0, DateTimeKind.Utc);
                Probe ask = new Probe(start, new Random(7));
                ask.Known = delegate(string asked) { return Probe.Filled(File.ReadAllText(Brain.MemoryPath), asked); };
                if (ask.Due(start.AddMinutes(30), 5, false)) throw new Exception("She asked about them within half an hour of starting.");
                DateTime later = start.AddHours(2);
                if (ask.Due(later, 0.5, false) || ask.Due(later, 30, false) || ask.Due(later, -1, false) || ask.Due(later, 5, true)) throw new Exception("She asked outside a lull in their chat, or while upset with them.");
                if (!ask.Due(later, 5, false)) throw new Exception("She didn't ask in a lull after they chatted.");
                string question = ask.Ask(later);
                string label = ask.Label;
                if (Array.IndexOf(Probe.Questions, question) < 0 || !ask.Pending || ask.Due(later, 5, false) || ask.Next < later.AddHours(Probe.GapHoursMin)) throw new Exception("Her question wasn't one of hers, or another was due straight after.");
                bool kept;
                string back = ask.Take("mostly Cyberpunk and drawing lol", later.AddMinutes(1), out kept);
                string notes = File.ReadAllText(Brain.MemoryPath);
                if (back != null || !kept || ask.Pending || notes.IndexOf("- " + label + ": Cyberpunk and drawing\n", StringComparison.Ordinal) < 0) throw new Exception("Her answer went to me.md as " + notes);
                if (!Probe.Filled(notes, label)) throw new Exception("A filled line wasn't known.");
                checks.Add("PASS she asks about them only in a lull after they chat, and their answer is kept in me.md under its own line: " + label);

                DateTime next = ask.Next.AddMinutes(1);
                string second = ask.Ask(next);
                if (ask.Label == label && Probe.Asks.GetLength(0) > 1) throw new Exception("She asked about " + label + " again while other lines were blank.");
                if (ask.Take("nah", next, out kept) != Probe.Passed || kept) throw new Exception("A pass was kept.");
                ask.Ask(next.AddHours(9));
                if (ask.Take("what about you?", next.AddHours(9), out kept) != null || kept) throw new Exception("A question back was kept as an answer.");
                ask.Ask(next.AddHours(18));
                if (ask.Take("Cyberpunk", next.AddHours(18).AddMinutes(Probe.AnswerMinutes + 1), out kept) != null || kept) throw new Exception("A late reply was kept as an answer.");
                if (File.ReadAllText(Brain.MemoryPath) != notes) throw new Exception("me.md changed without an answer: " + File.ReadAllText(Brain.MemoryPath));
                checks.Add("PASS she asks what's still blank first, and a pass, a question back or a late reply isn't written down");
            }
            finally
            {
                PersonalSettings.Folder = folderWas;
                Brain.MemoryPath = memoryWas;
            }

            if (Math.Abs(Mainframe.FitZoom(1f, 2880, 2f, 760) - 1f) > 0.001f) throw new Exception("Her Mainframe shrank on a 2880 x 1800 screen at 200%, which 0.9.2 fit.");
            float shortFit = Mainframe.FitZoom(1f, 1366, 1f, 760);
            if (Math.Abs(shortFit - 1f) > 0.001f) throw new Exception("Her Mainframe on a 1366 x 768 screen was " + shortFit + " rather than scrolling at 100%.");
            if (Math.Abs(Mainframe.FitZoom(2f, 1366, 1f, 760) - 1366f / 760f) > 0.001f) throw new Exception("Her picked size ran off a narrow screen.");
            if (Math.Abs(Mainframe.FitZoom(1f, 300, 1f, 760) - Mainframe.SmallestFit) > 0.001f) throw new Exception("Her Mainframe shrank past legible.");
            if (Math.Abs(Mainframe.FitZoom(1.5f, 700, 1.25f, 760) - 700f / 760f / 1.25f) > 0.001f) throw new Exception("Her Mainframe ran off a narrow screen.");
            checks.Add("PASS her Mainframe only shrinks for a screen too narrow for Settings, never smaller than 60%, and keeps its size on a short screen, which scrolls instead");

            short[] clap = Sfx.Make(Cue.Clap, 60);
            int loudest = 0;
            foreach (short sample in clap) loudest = Math.Max(loudest, Math.Abs((int)sample));
            if (clap.Length < Sfx.Rate / 10 || loudest < 2000) throw new Exception("Her high five clap is silent.");
            if (new HashSet<string>(HighFiveLines).Count != HighFiveLines.Length) throw new Exception("Her high five lines repeat.");
            checks.Add("PASS ctrl and a click is a high five, with a clap and a line of its own");
        }
    }
}
