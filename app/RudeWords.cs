using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // Words a companion pack can't use in its name, wake names or greetings. Each one is caught on its own or with an s on the
    // end, and still caught spelled with look-alikes, stretched letters or gaps between the letters. A * lets other letters
    // come before or after it, so only give one to a word that never turns up inside an ordinary one, the way cunt does in
    // Scunthorpe.
    internal static class RudeWords
    {
        internal static readonly string[] Swearing =
        {
            "*fuck*", "fuk", "fck", "shit", "shitty", "shitting", "shithead", "shithole", "bullshit", "cunt*", "*bitch*", "bastard",
            "asshole", "arsehole", "dumbass", "dickhead", "twat", "wank", "wanker", "wanking", "bollock*", "bellend", "piss", "pissed",
            "cocksucker"
        };

        internal static readonly string[] Slurs =
        {
            "nigger", "nigga", "niggaz", "chink", "gook", "spic", "kike", "wetback", "beaner", "paki", "wog", "raghead", "towelhead",
            "coon", "jigaboo", "darkie", "pikey", "fag", "faggot", "poofter", "tranny", "trannie", "shemale", "retard", "retarded",
            "spaz", "spastic", "mongoloid"
        };

        internal static readonly string[] Sexual =
        {
            "sex", "sexy", "sexting", "porn", "porno", "pornograph*", "nude", "boobs", "titty", "titties", "pussy", "dick", "cock",
            "penis", "vagina", "clit", "anal", "erotic*", "jizz", "whore", "slut", "slutty", "skank", "rape", "raped", "raping",
            "rapist", "*molest*", "pedo", "paedo", "*pedophil*", "*paedophil*", "incest*", "bdsm", "orgy", "orgies", "*orgasm*",
            "*dildo*", "*hentai*", "milf", "blow job", "hand job", "*masturbat*"
        };

        // Everyday phrases with one of those words in, like a rooster's cock-a-doodle-doo.
        private static readonly string[] Fine = { "cock a doodle", "maine coon" };

        private static readonly string[] LookAlikes = { "a4@*", "b8", "e3*", "g9", "i1!|*", "l1|", "o0*", "s5$", "t7+", "u*" };

        private const string Gap = "[._-]*", Edge = @"(?<![\p{L}\p{N}])", End = @"(?![\p{L}\p{N}])";
        private static readonly Regex Spaced = new Regex(@"(?<![\p{L}\p{N}])[^\s._-](?:[\s._-]+[^\s._-])+(?![\p{L}\p{N}])");
        private static readonly Regex Gaps = new Regex(@"[\s._-]+");
        private static readonly Regex Runs = new Regex(@"(.)\1{3,}");
        private static readonly Regex Ordinary = new Regex(Edge + "(?:" + string.Join("|", Array.ConvertAll(Fine, phrase => Regex.Escape(phrase).Replace(@"\ ", @"[\s._-]*"))) + ")");
        private static readonly Regex Rude = new Regex(Pattern());

        internal static bool In(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string plain = Spaced.Replace(Plain(text), delegate(Match letters) { return Gaps.Replace(letters.Value, ""); });
            return Rude.IsMatch(Ordinary.Replace(Runs.Replace(plain, "$1$1$1"), " "));
        }

        private static string Plain(string text)
        {
            string apart;
            try { apart = text.Normalize(NormalizationForm.FormKD); }
            catch (ArgumentException) { apart = text; }
            StringBuilder plain = new StringBuilder(apart.Length);
            foreach (char c in apart)
            {
                UnicodeCategory kind = CharUnicodeInfo.GetUnicodeCategory(c);
                if (kind == UnicodeCategory.NonSpacingMark || kind == UnicodeCategory.EnclosingMark || kind == UnicodeCategory.Format || kind == UnicodeCategory.Control && !char.IsWhiteSpace(c)) continue;
                plain.Append(char.ToLowerInvariant(c));
            }
            return plain.ToString();
        }

        private static string Pattern()
        {
            List<string> each = new List<string>();
            foreach (string[] words in new[] { Swearing, Slurs, Sexual })
                foreach (string word in words)
                    each.Add((word.StartsWith("*", StringComparison.Ordinal) ? "" : Edge) + Letters(word.Trim('*'))
                        + (word.EndsWith("*", StringComparison.Ordinal) ? "" : "(?:" + Gap + Letters("s") + ")?" + End));
            return string.Join("|", each.ToArray());
        }

        private static string Letters(string word)
        {
            StringBuilder pattern = new StringBuilder();
            int at = 0;
            while (at < word.Length)
            {
                int run = 1;
                while (at + run < word.Length && word[at + run] == word[at]) run++;
                if (word[at] == ' ') pattern.Append(@"[\s._-]*");
                else
                {
                    if (at > 0 && word[at - 1] != ' ') pattern.Append(Gap);
                    string letter = Like(word[at]);
                    pattern.Append(letter).Append("(?:").Append(Gap).Append(letter).Append("){").Append(run - 1).Append(",}");
                }
                at += run;
            }
            return pattern.ToString();
        }

        private static string Like(char letter)
        {
            foreach (string alike in LookAlikes)
                if (alike[0] == letter)
                {
                    StringBuilder set = new StringBuilder("[");
                    foreach (char c in alike) set.Append(char.IsLetterOrDigit(c) ? "" : "\\").Append(c);
                    return set.Append(']').ToString();
                }
            return Regex.Escape(letter.ToString());
        }
    }
}
