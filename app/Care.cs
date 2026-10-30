using System;
using System.Collections.Generic;
using System.IO;

namespace CapriDesktop
{
    internal enum CareKind { Water, Stretch, Eyes, Late, Meal, Game, Bored }

    internal sealed class Care
    {
        internal const long Away = 600000;
        internal static readonly string[] Names = { "Water", "Stretch", "Look away", "Sleep nudge", "Meals", "Game break", "Bored" };
        private readonly Func<double> seconds;
        private readonly Func<DateTime> local;
        private readonly Func<CareKind, string> words;
        private readonly double[] next = new double[7];
        private readonly bool[] pending = new bool[7];
        private readonly bool[] enabled = new bool[7];
        private readonly int[] times = new int[7];
        private bool started, resting;
        private DateTime since;
        private string game;
        private long night, meal;
        private double attentionAt = -1, attentionRate = 1;
        private int turn;
        internal CareKind Said;

        internal Care(Func<double> seconds, Func<DateTime> local, Func<CareKind, string> words)
        {
            this.seconds = seconds;
            this.local = local;
            this.words = words;
        }

        internal string Check(Preferences settings, long quiet, bool free, string playing, double noticed, double attentionGap)
        {
            double now = seconds();
            DateTime today = local();
            if (!started || resting || quiet > Away)
            {
                since = today;
                game = playing;
                for (int reset = 0; reset < next.Length; reset++)
                {
                    CareKind resetKind = (CareKind)reset;
                    next[reset] = now + settings.CareTime(resetKind) * 60.0;
                    pending[reset] = false;
                    enabled[reset] = settings.CareOn(resetKind);
                    times[reset] = settings.CareTime(resetKind);
                }
                started = true;
                resting = quiet > Away;
                if (resting) return null;
            }
            for (int setting = 0; setting < next.Length; setting++)
            {
                CareKind settingKind = (CareKind)setting;
                bool on = settings.CareOn(settingKind);
                int time = settings.CareTime(settingKind);
                if (!on || on != enabled[setting] || time != times[setting])
                {
                    pending[setting] = false;
                    next[setting] = now + time * 60.0;
                }
                enabled[setting] = on;
                times[setting] = time;
            }
            if (noticed != attentionAt || attentionGap != attentionRate)
            {
                attentionAt = noticed;
                attentionRate = attentionGap;
                pending[(int)CareKind.Bored] = false;
                next[(int)CareKind.Bored] = (noticed < 0 ? now : noticed) + settings.CareTime(CareKind.Bored) * 60.0 * attentionGap;
            }
            if (!string.Equals(game, playing, StringComparison.OrdinalIgnoreCase))
            {
                if (playing != null) pending[(int)CareKind.Game] = false;
                game = playing;
                next[(int)CareKind.Game] = now + settings.CareTime(CareKind.Game) * 60.0;
            }
            for (int timer = 0; timer < next.Length; timer++)
            {
                CareKind timerKind = (CareKind)timer;
                if (timerKind == CareKind.Late || timerKind == CareKind.Meal || !enabled[timer]) continue;
                if (timerKind == CareKind.Game && game == null) continue;
                if (timerKind == CareKind.Bored && (noticed < 0 || now - noticed < times[timer] * 60.0 * attentionGap))
                {
                    pending[timer] = false;
                    continue;
                }
                if (now < next[timer]) continue;
                pending[timer] = true;
                next[timer] = now + times[timer] * 60.0 * (timerKind == CareKind.Bored ? attentionGap : 1);
            }
            DateTime evening = today.AddHours(-12).Date;
            int hour = settings.CareTime(CareKind.Late);
            DateTime bedtime = evening.AddDays(hour < 12 ? 1 : 0).AddHours(hour);
            bool late = today >= bedtime && today < evening.AddDays(1).AddHours(6);
            if (!late || night != evening.Ticks) pending[(int)CareKind.Late] = false;
            if (enabled[(int)CareKind.Late] && late && night != evening.Ticks && settings.CareNight != evening.Ticks)
            {
                night = evening.Ticks;
                pending[(int)CareKind.Late] = true;
            }
            DateTime lunch = today.Date.AddHours(12);
            int mealGap = settings.CareTime(CareKind.Meal);
            DateTime mealAt = today < lunch ? lunch : lunch.AddMinutes(Math.Floor((today - lunch).TotalMinutes / mealGap) * mealGap);
            bool mealtime = since < lunch && today >= mealAt && today < mealAt.AddHours(1) && mealAt.Hour < 21;
            if (!mealtime || meal != mealAt.Ticks) pending[(int)CareKind.Meal] = false;
            if (enabled[(int)CareKind.Meal] && mealtime && meal != mealAt.Ticks)
            {
                meal = mealAt.Ticks;
                pending[(int)CareKind.Meal] = true;
            }
            if (!free) return null;
            string line = null;
            for (int choice = 0; choice < pending.Length; choice++)
            {
                int index = (turn + choice) % pending.Length;
                if (!pending[index]) continue;
                string choiceLine = words((CareKind)index);
                if (string.IsNullOrWhiteSpace(choiceLine)) continue;
                line = choiceLine;
                Said = (CareKind)index;
                turn = (index + 1) % pending.Length;
                if (Said == CareKind.Late) settings.CareNight = evening.Ticks;
                break;
            }
            for (int cleared = 0; cleared < pending.Length; cleared++)
                if (pending[cleared]) next[cleared] = now + times[cleared] * 60.0 * (cleared == (int)CareKind.Bored ? attentionGap : 1);
            Array.Clear(pending, 0, pending.Length);
            return line;
        }
    }

    internal sealed class CareLines
    {
        internal const string Defaults =
            "# Capri's check-ins\n\n" +
            "Edit the bullet lines under each heading to change what she says. Remove a bullet to stop using it, or all the bullets under a heading to silence that check-in. These lines stay on this PC.\n\n" +
            "## Water\n- Have a sip of water, choom. I'll keep your spot.\n- Water break. Even you need a refill.\n\n" +
            "## Stretch\n- Unfold yourself a little. Those legs still work.\n- Tiny stretch break. I'll be right here.\n\n" +
            "## Look away\n- Give your eyes 20 seconds looking away from the screen. I'll survive.\n- Look at something far away for 20 seconds. Yes, even farther than me.\n\n" +
            "## Sleep nudge\n- It's late, choom. Your bed misses you.\n\n" +
            "## Meals\n- Been here since before lunch. Go grab something to eat.\n- Food break. Can't run that brain on empty.\n\n" +
            "## Game break\n- Long session. Take a little break when you've got a good spot.\n- You've been at that game a while. Stretch those hands when you can.\n\n" +
            "## Bored\n- So boreddd.\n- I've counted your desktop icons twice now.\n- One of my minds is making its own entertainment. Worrying.\n- Tiny gremlin. Big boredom.\n\n" +
            "## Bored noticed\n- There you are.\n- Finally. Hi.\n\n" +
            "## Bored ignored\n- Fine. I'll entertain myself.\n- Cool. Cool cool cool.\n";
        private readonly string path;
        private readonly Dictionary<string, string> last = new Dictionary<string, string>();
        private readonly Random dice = new Random();

        internal CareLines(string path) { this.path = path; }

        internal bool Create()
        {
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (StreamWriter writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write))) writer.Write(Defaults);
                }
                else
                {
                    string existing = File.ReadAllText(path);
                    bool written = existing.IndexOf("# Capri's check-ins", StringComparison.Ordinal) >= 0;
                    foreach (string heading in Care.Names) if (existing.IndexOf("## " + heading, StringComparison.Ordinal) >= 0) written = true;
                    if (!written) File.AppendAllText(path, "\n\n" + Defaults);
                }
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal string Pick(CareKind kind)
        {
            return Pick(Care.Names[(int)kind]);
        }

        internal string Pick(string name)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            List<string> choices = new List<string>();
            bool section = false;
            using (StringReader reader = new StringReader(text))
            {
                string row;
                while ((row = reader.ReadLine()) != null)
                {
                    row = row.Trim();
                    if (row.StartsWith("#", StringComparison.Ordinal)) section = row == "## " + name;
                    else if (section && row.StartsWith("- ", StringComparison.Ordinal) && row.Substring(2).Trim().Length > 0)
                    {
                        string words = row.Substring(2).Trim();
                        if (!choices.Contains(words)) choices.Add(words);
                    }
                }
            }
            if (choices.Count == 0) return null;
            string previous;
            if (choices.Count > 1 && last.TryGetValue(name, out previous)) choices.Remove(previous);
            string picked = choices[dice.Next(choices.Count)];
            last[name] = picked;
            return picked;
        }
    }
}
