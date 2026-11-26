using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class Probe
    {
        internal static readonly string[,] Asks =
        {
            { "Games", "What have you been playing lately?" },
            { "Games", "What's the last game that actually got you?" },
            { "Music", "What have you been listening to lately?" },
            { "Shows", "Watching anything good at the moment?" },
            { "Hobbies", "What do you get up to when you're not at the computer?" },
            { "Working on", "What are you building or working on these days?" },
            { "Apps they live in", "What do you spend most of your day in on here?" },
            { "Usual hours", "What hours are you usually up? So I don't bug you at the wrong time." },
            { "How they like me", "Am I about right for you, or would you change how I talk to you?" }
        };

        internal static string[] Questions
        {
            get
            {
                string[] all = new string[Asks.GetLength(0)];
                for (int each = 0; each < all.Length; each++) all[each] = Asks[each, 1];
                return all;
            }
        }

        internal const string Passed = "All good, choom. Forget I asked.";
        internal const double AnswerMinutes = 6;
        internal const double ChatMinutesMin = 2, ChatMinutesMax = 12;
        internal const double GapHoursMin = 3, GapHoursMax = 8;

        private static readonly Regex Pass = new Regex(@"^(skip|pass|no|nah|nope|none|nothing|not much|not really|idk|i don'?t know|dunno|not sure|rather not|i'?d rather not|prefer not to say|no thanks|not telling|mind your business|none of your business|stop|stop asking|not now|later)(\s+(it|this|that|one|please|pls|thanks|really|much|tbh|honestly|lol|me|choom|capri))*\W*$", RegexOptions.IgnoreCase);
        private static readonly Regex Asked = new Regex(@"^\W*(what|who|how|why|when|where|which|can|could|would|will|should|do|does|did|is|are|r u|have you)\b.*\?\W*$", RegexOptions.IgnoreCase);
        private static readonly Regex Lead = new Regex(@"^(?:i'?m (?:really |mostly |super )?into|i am (?:really |mostly )?into|i'?ve been|i have been|i (?:really )?(?:like|love|enjoy)|mostly|probably|honestly|lately|um+|uh+|hmm+|oh)[\s,:]+", RegexOptions.IgnoreCase);

        private readonly Random dice;
        private DateTime next;
        private DateTime asked = DateTime.MinValue;
        private int pick = -1;

        internal Func<string, bool> Known = delegate { return false; };

        internal Probe(DateTime start, Random dice = null)
        {
            this.dice = dice ?? new Random();
            next = start.ToUniversalTime().AddMinutes(45 + this.dice.NextDouble() * 45);
        }

        internal bool Pending { get { return pick >= 0; } }
        internal string Question { get { return pick >= 0 ? Asks[pick, 1] : null; } }
        internal string Label { get { return pick >= 0 ? Asks[pick, 0] : null; } }
        internal DateTime Next { get { return next; } }

        internal bool Due(DateTime now, double chatMinutes, bool upset)
        {
            DateTime utc = now.ToUniversalTime();
            if (pick >= 0 && (utc - asked).TotalMinutes > AnswerMinutes) pick = -1;
            if (pick >= 0 || upset || utc < next) return false;
            if (!PersonalSettings.AsksAboutThem) return false;
            return chatMinutes >= ChatMinutesMin && chatMinutes <= ChatMinutesMax;
        }

        internal string Ask(DateTime now)
        {
            List<int> blank = new List<int>(), rest = new List<int>();
            for (int each = 0; each < Asks.GetLength(0); each++)
            {
                bool got = false;
                try { got = Known(Asks[each, 0]); } catch (Exception) { }
                (got ? rest : blank).Add(each);
            }
            List<int> from = blank.Count > 0 ? blank : rest;
            pick = from[dice.Next(from.Count)];
            asked = now.ToUniversalTime();
            next = asked.AddHours(GapHoursMin + dice.NextDouble() * (GapHoursMax - GapHoursMin));
            return Asks[pick, 1];
        }

        internal void Drop() { pick = -1; }

        internal string Take(string said, DateTime now, out bool kept)
        {
            kept = false;
            if (pick < 0) return null;
            string label = Asks[pick, 0];
            bool fresh = (now.ToUniversalTime() - asked).TotalMinutes <= AnswerMinutes;
            pick = -1;
            string text = Regex.Replace((said ?? "").Trim(), @"\s+", " ");
            if (!fresh || text.Length == 0) return null;
            if (Pass.IsMatch(text)) return Passed;
            if (Asked.IsMatch(text) || Elsewhere(text)) return null;
            if (Regex.Matches(text, @"\S+").Count > 40) return null;
            string answer = Regex.Replace(Lead.Replace(text, ""), @"[\s,]+(lol|haha|lmao|tbh)\W*$|[?!.]+$", "", RegexOptions.IgnoreCase).Trim();
            if (Regex.Replace(answer, @"\W", "").Length == 0) return null;
            string problem = Brain.Jot(label, char.ToUpperInvariant(answer[0]) + answer.Substring(1));
            if (problem == Brain.Private) return problem;
            kept = problem == null;
            return null;
        }

        private static bool Elsewhere(string text)
        {
            try
            {
                string tool;
                ToolArguments args;
                if (Hands.Heard(text, out tool, out args)) return true;
            }
            catch (Exception) { }
            return false;
        }

        internal static bool Filled(string notes, string label)
        {
            if (string.IsNullOrEmpty(notes) || string.IsNullOrEmpty(label)) return false;
            Match found = Regex.Match(notes, @"(?mi)^[ \t]*-[ \t]*" + Regex.Escape(label) + @"[ \t]*:[ \t]*(\S.*)$");
            return found.Success;
        }
    }
}
