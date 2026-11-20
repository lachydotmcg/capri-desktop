using System;
using System.Collections.Generic;

namespace CapriDesktop
{
    internal sealed class Outfit
    {
        internal readonly string Name;
        internal readonly string[] Colours;

        internal Outfit(string name, string hair, string accent, string eyes, string edges, string body)
        {
            Name = name;
            Colours = hair == null ? null : new[] { hair, accent, eyes, edges, body };
        }

        internal string Swatch { get { return Colours == null ? ArtColors.Usual[0] : Colours[0]; } }
    }

    internal sealed class FirstJob
    {
        internal readonly string Label;
        internal readonly string Said;

        internal FirstJob(string label, string said)
        {
            Label = label;
            Said = said;
        }
    }

    internal static class Onboarding
    {
        internal static readonly Outfit[] Looks =
        {
            new Outfit("Hers", null, null, null, null, null),
            new Outfit("Cyber Cyan", "#00D8FF", "#FF2BD6", "#FFD000", "#7A5CFF", null),
            new Outfit("Toxic", "#7CFF00", "#FF2E63", "#00E5FF", "#00A86B", "#0B1F12"),
            new Outfit("Royal", "#8A2BE2", "#FFD54A", "#00C2FF", "#FF4FA3", "#120A26"),
            new Outfit("Bubblegum", "#FF7AC6", "#7DF9FF", "#B14CFF", "#FFFFFF", "#3A1B3F")
        };

        internal static readonly string[] Vibes = { "Chill", "Balanced", "Chatty" };
        internal static readonly string[] VibeNotes = { "Short, easy replies.", "A bit more to say.", "Long replies when there's more to tell." };

        internal const string MeLine = "Make me yours. What do I call you, and how should I look?";
        internal const string JobLine = "Give me a real job and I'll do it right now.";
        internal const string JobWaitLine = "Pick my first job. I'll do it the second my brain's in.";
        internal const string JobQueuedLine = "Got it. I'll do that the second my brain's in.";
        internal const string NameCue = "Your name";
        internal const string OwnCue = "Or tell me your own";

        internal static int LookOf(string[] colours)
        {
            bool none = true;
            if (colours != null) foreach (string colour in colours) if (colour != null) { none = false; break; }
            if (none) return 0;
            for (int i = 1; i < Looks.Length; i++)
            {
                bool same = true;
                for (int slot = 0; slot < Looks[i].Colours.Length && same; slot++)
                    same = string.Equals(Looks[i].Colours[slot], slot < colours.Length ? colours[slot] : null, StringComparison.OrdinalIgnoreCase);
                if (same) return i;
            }
            return -1;
        }

        internal static int VibeOf(int wordiness) { return Math.Max(0, Math.Min(2, wordiness)); }

        internal static FirstJob[] Jobs(bool windows, bool mac, int hour)
        {
            List<FirstJob> jobs = new List<FirstJob>();
            jobs.Add(new FirstJob("Remind me to stretch", "Remind me to stretch in 20 minutes"));
            jobs.Add(hour >= 21 || hour < 5
                ? new FirstJob("Play something chill", "Open YouTube and play some lo-fi")
                : new FirstJob("Put on some music", "Open YouTube and play some lo-fi"));
            if (windows) jobs.Add(new FirstJob("How's my PC doing?", "How's my PC doing? Is anything using a lot?"));
            jobs.Add(new FirstJob("Find my Downloads", mac ? "Show me my Downloads folder in Finder" : "Show me my Downloads folder"));
            return jobs.ToArray();
        }

        internal static string Job(string typed)
        {
            string text = (typed ?? "").Trim();
            if (text.Length > 500) text = text.Substring(0, 500).TrimEnd();
            return text.Length == 0 ? null : text;
        }

        internal static List<string> Check()
        {
            List<string> checks = new List<string>();
            foreach (Outfit look in Looks)
            {
                if (look.Colours == null) continue;
                if (look.Colours.Length != ArtColors.Names.Length) throw new Exception("The " + look.Name + " look has " + look.Colours.Length + " colours.");
                foreach (string colour in look.Colours)
                    if (colour != null && !System.Text.RegularExpressions.Regex.IsMatch(colour, "^#[0-9A-Fa-f]{6}$")) throw new Exception("The " + look.Name + " look has a colour that isn't one: " + colour);
            }
            for (int i = 0; i < Looks.Length; i++) if (LookOf(Looks[i].Colours) != i) throw new Exception("The " + Looks[i].Name + " look isn't found again from its own colours.");
            if (LookOf(new[] { "#123456", null, null, null, null }) != -1) throw new Exception("Colours of their own were read as one of the looks.");
            checks.Add("PASS onboarding: every look is real colours in her five slots, Hers is no colours at all, and each is found again from what's saved");
            foreach (bool windows in new[] { true, false })
                foreach (int hour in new[] { 9, 23 })
                {
                    FirstJob[] jobs = Jobs(windows, !windows, hour);
                    if (jobs.Length < 3 || jobs.Length > 4) throw new Exception("The first job has " + jobs.Length + " picks.");
                    HashSet<string> seen = new HashSet<string>();
                    foreach (FirstJob job in jobs)
                        if (string.IsNullOrEmpty(job.Label) || job.Label.Length > 24 || Job(job.Said) != job.Said || !seen.Add(job.Said)) throw new Exception("A first job pick is empty, too long or twice: " + job.Label);
                    bool pc = Array.Exists(jobs, job => job.Said.StartsWith("How's my PC", StringComparison.Ordinal));
                    if (pc != windows) throw new Exception("How's my PC doing was " + (pc ? "offered off Windows." : "missing on Windows."));
                }
            if (Job("   ") != null || Job(" remind me ") != "remind me" || Job(new string('a', 900)).Length != 500) throw new Exception("A typed first job wasn't tidied.");
            checks.Add("PASS onboarding: the first job offers three or four real jobs her hands can do on this system, and a typed one is trimmed and capped");
            if (VibeOf(-1) != 0 || VibeOf(3) != 2 || Vibes.Length != 3 || VibeNotes.Length != 3) throw new Exception("Her vibe doesn't follow her reply length.");
            checks.Add("PASS onboarding: her vibe is her existing reply length, Chill to Chatty, with no setting of its own");
            return checks;
        }
    }
}
