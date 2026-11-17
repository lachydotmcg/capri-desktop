using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class DoomSite
    {
        internal readonly string Name;
        internal readonly Regex Title;
        internal DoomSite(string name, string title) { Name = name; Title = new Regex(title, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant); }
    }

    internal enum DoomMove { None, Nudge, Perch, Sulk, Pout, Free }

    internal sealed class Doomscroll
    {
        internal const string ListKey = "doomscroll sites";
        internal const string Stock = "tiktok, youtube shorts, instagram, reddit, x";
        internal static readonly int[] Limits = { 30, 300, 600, 900, 1200, 1800, 2700, 3600 };
        internal static readonly string[] LimitNames = { "30 s test", "5 min", "10 min", "15 min", "20 min", "30 min", "45 min", "1 h" };
        internal const int DefaultLimit = 900;
        internal const int SnoozeFor = 300;
        internal const int FreeAfter = 20;
        internal const int MostPouts = 2;

        private static readonly Regex BrowserEnd = new Regex(@"\s[-–—]\s(?:Google Chrome(?: \(Incognito\))?|Microsoft Edge(?: InPrivate)?|Mozilla Firefox(?: Private Browsing)?|Brave(?: \(Private\))?|Opera(?: GX)?|Vivaldi|Arc)$", RegexOptions.IgnoreCase);
        private static readonly Regex Browsers = new Regex(@"^(chrome|msedge|firefox|brave|opera|vivaldi|arc|MicrosoftEdge|MicrosoftEdgeCP)$", RegexOptions.IgnoreCase);
        private static readonly Regex Own = new Regex(@"^capri(?:[\s._-].*)?$", RegexOptions.IgnoreCase);
        private static readonly Regex Snoozing = new Regex(@"^(?:(?:ok(?:ay)?|fine|pls|please|just|capri|choom|bro|bestie|come on|cmon|no|nah|wait|like|uh+|um+|chill)[\s,.!]+)*(?:5|five|a few|few|one|1|two|2|ten|10)\s+more\s+min(?:ute)?s?(?:[\s,.!]+(?:pls|please|capri|choom|bro|i promise|promise|lol|ok(?:ay)?))*[\s.!?]*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        private static readonly Dictionary<string, DoomSite> Known = new Dictionary<string, DoomSite>(StringComparer.OrdinalIgnoreCase)
        {
            { "tiktok", new DoomSite("TikTok", @"\btik ?tok\b") },
            { "youtube shorts", new DoomSite("YouTube Shorts", @"^(?=.*\byoutube\b)(?=.*(?:#|\b)shorts\b)") },
            { "shorts", new DoomSite("YouTube Shorts", @"^(?=.*\byoutube\b)(?=.*(?:#|\b)shorts\b)") },
            { "instagram", new DoomSite("Instagram", @"\binstagram\b") },
            { "reels", new DoomSite("Reels", @"\breels?\b") },
            { "reddit", new DoomSite("Reddit", @"\breddit\b|^r/\w+") },
            { "x", new DoomSite("X", @"(?:^|\s)/ X$|^X$|\bon X: ") },
            { "twitter", new DoomSite("X", @"\btwitter\b|(?:^|\s)/ X$|\bon X: ") },
            { "facebook", new DoomSite("Facebook", @"\bfacebook\b") },
            { "youtube", new DoomSite("YouTube", @"\byoutube\b") },
            { "twitch", new DoomSite("Twitch", @"\btwitch\b") },
            { "snapchat", new DoomSite("Snapchat", @"\bsnapchat\b") },
            { "threads", new DoomSite("Threads", @"\bthreads\b") },
            { "pinterest", new DoomSite("Pinterest", @"\bpinterest\b") }
        };

        internal static readonly string[] NudgeLines = {
            "{1} of {0}, choom. your thumb okay?",
            "hey. HEY. {1} of {0}. blink twice if you're trapped.",
            "{0} has had you for {1}. I'm getting jealous.",
            "the scroll is winning, choom. {1} of {0}.",
            "{1} of {0}. say five more minutes if you need it. I'll know if you lie."
        };
        internal static readonly string[] PerchLines = {
            "nope. I live on your {0} now. this is MY {0}.",
            "scroll past me. I dare you.",
            "look at me. LOOK at me. I'm way better content.",
            "I've been here the whole time, you know. the WHOLE time.",
            "new video just dropped: me. sitting on your {0}."
        };
        internal static readonly string[] SulkLines = {
            "fine. keep scrolling. I'll just be here. alone. in the dark.",
            "you picked {0} over me. I'll remember this.",
            "I'm not mad. I'm just... buffering.",
            "wow. {0} gets your whole attention and I get nothing."
        };
        internal static readonly string[] PoutLines = {
            "still here. still ignored. cool cool cool.",
            "I could be your algorithm, you know.",
            "this is me sulking. very dramatically. on your screen."
        };
        internal static readonly string[] TimeUpLines = {
            "that was five minutes. I counted every one.",
            "five minutes, choom. I watched the clock the WHOLE time."
        };
        internal static readonly string[] SnoozeLines = {
            "five minutes. I'm counting. out loud. in my head.",
            "fine. five. starting... now."
        };
        internal static readonly string[] SnoozeAgainLines = {
            "five MORE? okay. five. but I'm watching you.",
            "that's what you said last time. fine. five."
        };
        internal static readonly string[] RefusedLines = {
            "no. you said that twice. I'm not falling for it again.",
            "nice try. we both know it's never five."
        };
        internal static readonly string[] FreeLines = {
            "you put it down!! that's my choom.",
            "WE'RE FREE. hi. missed you.",
            "you escaped the scroll. proud of you."
        };

        internal Func<string> List = delegate { return PersonalSettings.Of(ListKey); };
        internal Func<string, string, bool> Skip = PersonalSettings.Skipped;

        private readonly Dictionary<string[], int> lastPick = new Dictionary<string[], int>();
        private readonly Random dice = new Random();
        private double scrolled, nextAt = -1, seenAt = -1;
        private int stage, snoozes, pouts, limitWas;
        private bool snoozed;
        private string site;

        internal int Stage { get { return stage; } }
        internal string Site { get { return site; } }
        internal double Scrolled { get { return scrolled; } }
        internal bool Active { get { return stage > 0; } }

        internal static int Limit(int seconds) { return Array.IndexOf(Limits, seconds) >= 0 ? seconds : DefaultLimit; }
        internal static int Gap(int limit) { return Math.Max(20, Math.Min(180, limit / 3)); }
        internal static int Break(int limit) { return Math.Max(60, Math.Min(300, limit / 2)); }

        internal static string Spoken(double seconds)
        {
            int whole = (int)Math.Round(seconds);
            if (whole < 90) return whole + " seconds";
            int minutes = (int)Math.Round(seconds / 60);
            if (minutes < 60) return minutes + " minutes";
            int hours = minutes / 60, left = minutes % 60;
            return (hours == 1 ? "an hour" : hours + " hours") + (left >= 5 ? " and " + left + " minutes" : "");
        }

        internal static string Plain(string program) { return Regex.Replace(program ?? "", @"\.exe$", "", RegexOptions.IgnoreCase); }

        internal List<DoomSite> Sites()
        {
            string written = null;
            try { written = List(); }
            catch (Exception) { }
            if (written == null) written = Stock;
            List<DoomSite> sites = new List<DoomSite>();
            foreach (string one in written.Split(',', ';'))
            {
                string name = Regex.Replace((one ?? "").Trim().Trim('"', '\''), @"\s+", " ");
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                name = Regex.Replace(name, @"^(?:https?://)?(?:www\.)?", "", RegexOptions.IgnoreCase).TrimEnd('/');
                string bare = Regex.Replace(name, @"\.(?:com|net|org|tv)$", "", RegexOptions.IgnoreCase);
                DoomSite known;
                if (Known.TryGetValue(bare, out known)) { sites.Add(known); continue; }
                if (name.Length < 2) continue;
                sites.Add(new DoomSite(char.ToUpperInvariant(name[0]) + name.Substring(1), Regex.Escape(name)));
            }
            return sites;
        }

        internal DoomSite Match(string program, string title)
        {
            string plain = Plain(program), shown = (title ?? "").Trim();
            if (plain.Length == 0 && shown.Length == 0) return null;
            if (Own.IsMatch(plain) || Regex.IsMatch(shown, @"^Capri(?: Mainframe| Workspace| says| task)?$", RegexOptions.IgnoreCase)) return null;
            bool skipped;
            try { skipped = Skip(program, title); }
            catch (Exception) { skipped = false; }
            if (skipped) return null;
            bool browser = Browsers.IsMatch(plain);
            if (browser) shown = BrowserEnd.Replace(shown, "").Trim('‎', '‏', ' ');
            shown = Regex.Replace(shown, @"^\(\d+\+?\)\s*", "");
            foreach (DoomSite one in Sites())
                if (one.Title.IsMatch(shown) || !browser && one.Title.IsMatch(plain)) return one;
            return null;
        }

        internal void Reset()
        {
            scrolled = 0;
            nextAt = -1;
            seenAt = -1;
            stage = snoozes = pouts = 0;
            snoozed = false;
            site = null;
        }

        internal DoomMove Tick(string program, string title, bool on, int limitSeconds, bool free, double now, out string line)
        {
            line = null;
            int limit = Limit(limitSeconds);
            if (!on) { Reset(); limitWas = limit; return DoomMove.None; }
            if (limit != limitWas && stage == 0) nextAt = -1;
            limitWas = limit;
            DoomSite hit = Match(program, title);
            if (hit == null)
            {
                if (seenAt < 0) return DoomMove.None;
                double away = now - seenAt;
                if (stage > 0 && away >= FreeAfter)
                {
                    bool cheer = free;
                    Reset();
                    if (!cheer) return DoomMove.None;
                    line = Pick(FreeLines);
                    return DoomMove.Free;
                }
                if (away > Break(limit)) Reset();
                return DoomMove.None;
            }
            if (seenAt >= 0 && now - seenAt > Break(limit)) Reset();
            if (seenAt >= 0) scrolled += now - seenAt <= 3 ? Math.Max(0, now - seenAt) : 1;
            seenAt = now;
            site = hit.Name;
            if (nextAt < 0) nextAt = limit;
            if (scrolled < nextAt || !free) return DoomMove.None;
            int gap = Gap(limit);
            if (snoozed)
            {
                snoozed = false;
                stage = 2;
                nextAt = scrolled + gap;
                line = Pick(TimeUpLines);
                return DoomMove.Perch;
            }
            if (stage == 0)
            {
                stage = 1;
                nextAt = scrolled + gap;
                line = string.Format(Pick(NudgeLines), site, Spoken(scrolled));
                return DoomMove.Nudge;
            }
            if (stage == 1)
            {
                stage = 2;
                nextAt = scrolled + gap;
                line = string.Format(Pick(PerchLines), site);
                return DoomMove.Perch;
            }
            if (stage == 2)
            {
                stage = 3;
                nextAt = scrolled + 2 * gap;
                line = string.Format(Pick(SulkLines), site);
                return DoomMove.Sulk;
            }
            if (pouts >= MostPouts) { nextAt = double.MaxValue; return DoomMove.None; }
            pouts++;
            nextAt = scrolled + 2 * gap;
            line = string.Format(Pick(PoutLines), site);
            return DoomMove.Pout;
        }

        internal static bool AskedToSnooze(string said)
        {
            string text = (said ?? "").Trim();
            return text.Length > 0 && text.Length <= 60 && Snoozing.IsMatch(text);
        }

        internal string Snooze(string said, out bool refused)
        {
            refused = false;
            if (stage == 0 || !AskedToSnooze(said)) return null;
            snoozes++;
            if (snoozes > 2)
            {
                refused = true;
                snoozed = false;
                stage = 3;
                nextAt = scrolled + 2 * Gap(limitWas);
                return Pick(RefusedLines);
            }
            snoozed = true;
            stage = 1;
            pouts = 0;
            nextAt = scrolled + SnoozeFor;
            return Pick(snoozes == 1 ? SnoozeLines : SnoozeAgainLines);
        }

        private string Pick(string[] lines)
        {
            int was;
            if (!lastPick.TryGetValue(lines, out was)) was = -1;
            int pick = dice.Next(lines.Length);
            if (lines.Length > 1 && pick == was) pick = (pick + 1 + dice.Next(lines.Length - 1)) % lines.Length;
            lastPick[lines] = pick;
            return lines[pick];
        }

        internal static string Check()
        {
            string list = Stock;
            string[] never = new string[0];
            Doomscroll guard = new Doomscroll();
            guard.List = delegate { return list; };
            guard.Skip = delegate(string program, string title)
            {
                foreach (string one in never)
                    if ((program ?? "").IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0 || (title ?? "").IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                return false;
            };
            string[,] fronts =
            {
                { "chrome.exe", "TikTok - Make Your Day - Google Chrome", "TikTok" },
                { "msedge", "(3) For You | TikTok - Microsoft Edge", "TikTok" },
                { "TikTok", "TikTok", "TikTok" },
                { "firefox", "funny cat #shorts - YouTube — Mozilla Firefox", "YouTube Shorts" },
                { "chrome", "YouTube Shorts - Google Chrome", "YouTube Shorts" },
                { "chrome", "Lofi beats to study to - YouTube - Google Chrome", null },
                { "brave", "Instagram - Brave", "Instagram" },
                { "chrome", "r/aww - Reddit - Google Chrome", "Reddit" },
                { "chrome", "(12) Home / X - Google Chrome", "X" },
                { "msedge", "someone on X: \"hello\" / X - Microsoft Edge", "X" },
                { "chrome", "Xbox Cloud Gaming - Google Chrome", null },
                { "notepad", "x.txt - Notepad", null },
                { "Discord", "#general | Friends - Discord", null },
                { "Capri", "Capri Mainframe", null },
                { "chrome", "Claude - Google Chrome", null }
            };
            for (int i = 0; i < fronts.GetLength(0); i++)
            {
                DoomSite got = guard.Match(fronts[i, 0], fronts[i, 1]);
                string name = got == null ? null : got.Name;
                if (name != fronts[i, 2]) return "Doomscroll guard matched " + fronts[i, 1] + " (" + fronts[i, 0] + ") as " + (name ?? "nothing") + " instead of " + (fronts[i, 2] ?? "nothing");
            }
            never = new[] { "tiktok" };
            if (guard.Match("chrome", "TikTok - Make Your Day - Google Chrome") != null) return "Doomscroll guard counted a window from the never watch list in sys.md.";
            never = new string[0];
            list = "Twitch.tv, www.netflix.com, \"hulu\"";
            if (guard.Match("chrome", "TikTok - Google Chrome") != null || guard.Match("chrome", "xQc - Twitch - Google Chrome") == null || guard.Match("chrome", "Home - netflix.com - Google Chrome") == null || guard.Match("hulu.exe", "") == null)
                return "Doomscroll guard didn't follow the doomscroll sites list in sys.md.";
            list = Stock;
            if (!AskedToSnooze("5 more minutes") || !AskedToSnooze("okay fine, five more minutes please") || !AskedToSnooze("Capri, 5 more mins!") || AskedToSnooze("I spent five more minutes on my essay than I planned") || AskedToSnooze("remind me in 5 minutes"))
                return "Doomscroll guard heard five more minutes wrong.";

            string line;
            DoomMove move;
            const string tiktok = "TikTok - Make Your Day - Google Chrome";
            guard.Reset();
            double now = 0;
            for (; now < 29; now++) if (guard.Tick("chrome", tiktok, true, 30, true, now, out line) != DoomMove.None) return "Doomscroll guard reacted before the limit.";
            if (guard.Tick("chrome", tiktok, false, 30, true, now, out line) != DoomMove.None || guard.Scrolled != 0) return "Doomscroll guard counted while it was off.";
            for (now = 100; now < 130; now++) guard.Tick("chrome", tiktok, true, 30, true, now, out line);
            move = guard.Tick("chrome", tiktok, true, 30, true, now, out line);
            if (move != DoomMove.Nudge || line == null || line.IndexOf("TikTok", StringComparison.Ordinal) < 0 || line.IndexOf("30 seconds", StringComparison.Ordinal) < 0) return "Doomscroll guard didn't nudge at the limit: " + move + " " + line;
            List<DoomMove> moves = new List<DoomMove>();
            for (now++; now < 400; now++)
            {
                move = guard.Tick("chrome", tiktok, true, 30, now < 160 || now > 175, now, out line);
                if (move != DoomMove.None) { moves.Add(move); if (string.IsNullOrEmpty(line)) return "Doomscroll guard moved without a line."; }
            }
            if (string.Join(",", moves.ConvertAll(one => one.ToString()).ToArray()) != "Perch,Sulk,Pout,Pout") return "Doomscroll guard went " + string.Join(",", moves.ConvertAll(one => one.ToString()).ToArray()) + " instead of perch, sulk and two pouts.";

            bool refused;
            if (guard.Snooze("five more minutes", out refused) == null || refused || guard.Stage != 1) return "Doomscroll guard didn't give five more minutes.";
            double snoozedAt = guard.Scrolled;
            for (move = DoomMove.None; move == DoomMove.None && now < 1000; now++) move = guard.Tick("chrome", tiktok, true, 30, true, now, out line);
            if (move != DoomMove.Perch || Array.IndexOf(TimeUpLines, line) < 0 || guard.Scrolled - snoozedAt < SnoozeFor - 1) return "Doomscroll guard's five more minutes didn't end with time's up: " + move + " after " + (guard.Scrolled - snoozedAt);
            if (guard.Snooze("5 more minutes", out refused) == null || refused) return "Doomscroll guard refused a second five more minutes.";
            if (Array.IndexOf(RefusedLines, guard.Snooze("5 more minutes", out refused)) < 0 || !refused || guard.Stage != 3) return "Doomscroll guard gave a third five more minutes.";

            for (move = DoomMove.None; now < 1200 && move == DoomMove.None; now++) move = guard.Tick("notepad", "notes.txt - Notepad", true, 30, true, now, out line);
            if (move != DoomMove.Free || Array.IndexOf(FreeLines, line) < 0 || guard.Active) return "Doomscroll guard didn't cheer when they put it down: " + move;

            guard.Reset();
            for (now = 2000; now < 2020; now++) guard.Tick("chrome", tiktok, true, 30, true, now, out line);
            for (; now < 2100; now++) guard.Tick("notepad", "notes.txt - Notepad", true, 30, true, now, out line);
            for (; now < 2015 + 100; now++) if (guard.Tick("chrome", tiktok, true, 30, true, now, out line) != DoomMove.None) return "Doomscroll guard carried scrolling over a long break.";
            guard.Reset();
            for (now = 3000; now < 3020; now++) guard.Tick("chrome", tiktok, true, 30, true, now, out line);
            for (; now < 3030; now++) guard.Tick("Discord", "general - Discord", true, 30, true, now, out line);
            for (move = DoomMove.None; now < 3045 && move == DoomMove.None; now++) move = guard.Tick("chrome", tiktok, true, 30, true, now, out line);
            if (move != DoomMove.Nudge) return "Doomscroll guard lost time over a quick reply in Discord.";
            if (Limit(0) != DefaultLimit || Limit(77) != DefaultLimit || Limit(300) != 300 || LimitNames.Length != Limits.Length) return "Doomscroll guard's limits are wrong.";
            return null;
        }
    }
}
