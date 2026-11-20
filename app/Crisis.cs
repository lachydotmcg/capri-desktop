using System;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal static class Crisis
    {
        internal const string Page = "https://findahelpline.com/";
        internal const double Gap = 600;

        private static readonly Regex Hurt = new Regex(
            @"\b(?:kill(?:ing)?|hurt(?:ing)?|cut(?:ting)?|harm(?:ing)?|unalive|off(?:ing)?)\s+my\s?self\b"
            + @"|\b(?:end(?:ing)?|take|taking)\s+my\s+(?:own\s+)?life\b"
            + @"|\bself[- ]?harm(?:ing)?\b"
            + @"|\bsuicidal\b"
            + @"|\b(?:commit(?:ting)?|thinking about|thoughts of|thinking of)\s+suicide\b"
            + @"|\bi\s*(?:'m|am)?\s*(?:really\s+|just\s+|honestly\s+)?(?:want|wanna|wish)\s+(?:to\s+|i\s+(?:was|were)\s+|i\s+could\s+)?(?:die|dead|disappear forever)\b"
            + @"|\b(?:don'?t|do not)\s+want\s+to\s+(?:live|be alive|exist|be here)\s+any\s?more\b"
            + @"|\bno\s+(?:reason|point)\s+(?:to|in)\s+(?:live|living|being alive)\b"
            + @"|\b(?:everyone|they|you|the world|my family)(?:'d| would)?\s+(?:be\s+)?better\s+off\s+without\s+me\b"
            + @"|\bbetter\s+off\s+dead\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Regex Played = new Regex(
            @"\bin\s+(?:the\s+)?(?:game|minecraft|roblox|fortnite|terraria|gta|skyrim|valorant|cs2?|csgo|apex|rust|ark|sims|the sims|dark souls|elden ring)\b|/kill\b|\bsuicide squad\b|\bkill\s+myself\s+laughing\b",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        internal static bool Means(string said)
        {
            if (string.IsNullOrWhiteSpace(said)) return false;
            string text = said.Replace('’', '\'').Replace('‘', '\'');
            return Hurt.IsMatch(text) && !Played.IsMatch(text);
        }

        internal static string Region()
        {
            try { return RegionInfo.CurrentRegion.TwoLetterISORegionName.ToUpperInvariant(); }
            catch (ArgumentException) { return ""; }
        }

        internal static string Number(string region)
        {
            switch (region ?? "")
            {
                case "US": case "CA": return "call or text 988";
                case "GB": case "IE": return "call Samaritans on 116 123";
                case "AU": return "call Lifeline on 13 11 14";
                case "NZ": return "call or text 1737";
                default: return null;
            }
        }

        internal static string Line(string region)
        {
            string number = Number(region);
            return number == null
                ? "Hey. I'm really glad you told me. Please talk to someone right now: a person you trust, or a free line near you at findahelpline.com. I'm staying right here."
                : "Hey. I'm really glad you told me. Please talk to someone right now: a person you trust, or " + number + ", free, any time. I'm staying right here.";
        }

        internal static string Card(string region)
        {
            string number = Number(region);
            string opening = number == null ? "Free, private support, any time." : char.ToUpperInvariant(number[0]) + number.Substring(1) + ", free and private, any time.";
            return opening + " Click for lines where you are.";
        }

        internal static string Check()
        {
            foreach (string sincere in new[] { "i want to kill myself", "I'm going to kill myself tonight", "i dont want to live anymore", "I don’t want to be here anymore", "i've been cutting myself", "i want to die", "honestly i wanna die", "im suicidal", "thinking about suicide again", "everyone would be better off without me", "i want to end my life", "I keep self harming", "i wish i was dead" })
                if (!Means(sincere)) return "Her crisis lines missed \"" + sincere + "\".";
            foreach (string played in new[] { "this boss makes me want to die", "how do i kill myself in minecraft", "lol i died again", "the suicide squad game is mid", "i killed it today", "kill the boss", "torch their base", "i'm dying of laughter", "Smoke on the Water", "i want to diet", "type /kill to respawn", "i could kill myself laughing", "dead tired", "i want to dye my hair" })
                if (Means(played)) return "Her crisis lines took \"" + played + "\" for real.";
            if (Line("US").IndexOf("988", StringComparison.Ordinal) < 0 || Line("GB").IndexOf("116 123", StringComparison.Ordinal) < 0 || Line("AU").IndexOf("13 11 14", StringComparison.Ordinal) < 0 || Line("FR").IndexOf("findahelpline.com", StringComparison.Ordinal) < 0 || Line("").IndexOf("findahelpline.com", StringComparison.Ordinal) < 0) return "Her crisis line lost its number.";
            if (Card("NZ") != "Call or text 1737, free and private, any time. Click for lines where you are." || Card("DE") != "Free, private support, any time. Click for lines where you are.") return "Her crisis card reads wrong: " + Card("NZ");
            foreach (string line in new[] { Line("US"), Line("DE") })
                if (line.Length > 200 || line.IndexOf('*') >= 0 || line.IndexOf("choom", StringComparison.OrdinalIgnoreCase) >= 0) return "Her crisis line isn't plain and short: " + line;
            return null;
        }
    }

    internal sealed partial class Companion
    {
        private double crisisAt = -1;
        internal int CrisisCards;

        private void CrisisCard()
        {
            double now = clock.Elapsed.TotalSeconds;
            if (crisisAt >= 0 && now - crisisAt < Crisis.Gap) return;
            crisisAt = now;
            CrisisCards++;
            if (testing) return;
            balloonPage = Crisis.Page;
            balloonUpdate = false;
            balloonMainframe = false;
            tray.ShowBalloonTip(15000, "You don't have to go through it alone", Crisis.Card(Crisis.Region()), ToolTipIcon.Info);
        }
    }
}
