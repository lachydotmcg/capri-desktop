using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class SoundsLike
    {
        private static readonly string[] LeadIns = { "hey", "hei", "yo", "oh", "ok", "ay" };
        private static readonly Regex WordIn = new Regex(@"\p{L}+");
        private readonly List<Sound> names = new List<Sound>();

        private struct Sound
        {
            internal string Consonants;
            internal char Vowel, Ending;
        }

        internal SoundsLike(IEnumerable<string> called)
        {
            foreach (string name in called)
            {
                Sound sound = Of(name.Replace(" ", "").Replace("-", "").Replace("'", ""));
                if (sound.Consonants.Length > 0) names.Add(sound);
            }
        }

        internal bool Hears(string words)
        {
            MatchCollection found = WordIn.Matches((words ?? "").ToLowerInvariant());
            string before = null;
            foreach (Match match in found)
            {
                string word = match.Value;
                if (Fits(word, true)) return true;
                foreach (string lead in LeadIns)
                    if (word.Length > lead.Length + 3 && word.StartsWith(lead, StringComparison.Ordinal) && Fits(word.Substring(lead.Length), true)) return true;
                if (before != null && Of(word).Consonants.Length > 0)
                {
                    string joined = before + word;
                    if (Fits(joined, false)) return true;
                    foreach (string lead in LeadIns)
                        if (joined.StartsWith(lead, StringComparison.Ordinal) && Fits(joined.Substring(lead.Length), false)) return true;
                }
                before = word;
            }
            return false;
        }

        private bool Fits(string word, bool whole)
        {
            Sound heard = Of(word);
            foreach (Sound name in names)
            {
                if (heard.Consonants != name.Consonants || heard.Ending != name.Ending) continue;
                if (name.Consonants.Length >= 3) return true;
                if (whole && heard.Vowel == name.Vowel) return true;
            }
            return false;
        }

        private static Sound Of(string word)
        {
            string w = word.ToLowerInvariant();
            w = w.Replace("tch", "x").Replace("ch", "x").Replace("sh", "x").Replace("ph", "f").Replace("ck", "k").Replace("qu", "k").Replace("j", "x");
            w = Regex.Replace(w, "c(?=[eiy])", "s");
            StringBuilder consonants = new StringBuilder();
            char vowel = '-';
            char ending = '-';
            int at = 0;
            if (w.Length > 2 && w.EndsWith("e", StringComparison.Ordinal) && !IsVowel(w[w.Length - 2])) w = w.Substring(0, w.Length - 1);
            while (at < w.Length)
            {
                char c = w[at];
                if (IsVowel(c) || c == 'y' && at > 0 || c == 'w' && at > 0 && IsVowel(w[at - 1]))
                {
                    int start = at;
                    while (at < w.Length && (IsVowel(w[at]) || w[at] == 'y' || w[at] == 'w' && IsVowel(w[at - 1]))) at++;
                    char kind = Kind(w.Substring(start, at - start));
                    if (vowel == '-') vowel = kind;
                    ending = at == w.Length ? kind : '-';
                    continue;
                }
                char folded = Fold(c);
                at++;
                ending = '-';
                if (folded == '\0') continue;
                if (consonants.Length == 0 || consonants[consonants.Length - 1] != folded) consonants.Append(folded);
            }
            return new Sound { Consonants = consonants.ToString(), Vowel = vowel, Ending = ending };
        }

        private static bool IsVowel(char c) { return c == 'a' || c == 'e' || c == 'i' || c == 'o' || c == 'u'; }

        private static char Kind(string vowels)
        {
            switch (vowels)
            {
                case "oo": case "u": case "ew": case "ou": case "ue": case "ui": return 'U';
                case "i": case "y": case "ee": case "ie": case "ey": case "ea": case "ei": return 'I';
                default: return 'A';
            }
        }

        private static char Fold(char c)
        {
            switch (c)
            {
                case 'h': case 'w': case 'y': return '\0';
                case 'b': return 'p';
                case 'd': return 't';
                case 'g': case 'c': case 'q': return 'k';
                case 'v': return 'f';
                case 'z': return 's';
                default: return c;
            }
        }
    }
}
