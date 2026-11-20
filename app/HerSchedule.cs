using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class HerWhen
    {
        internal string Task = "";
        internal string Repeat = "";
        internal int Every;
        internal int Minute = -1;
        internal int Day = -1;
        internal DateTime At;
        internal bool Watch;
        internal bool Until;
    }

    internal static class HerSchedule
    {
        private const RegexOptions Flags = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static readonly Regex Watching = new Regex(@"\bkeep\s+an\s+eye\b|\bwatch\s+for\b|\b(?:let|tell)\s+me\s+(?:know\s+)?(?:when|if)\b|\bcheck\b.*\bfor\s+changes\b|\bmonitor\b", Flags);
        private static readonly Regex Asking = new Regex(@"\b(?:let|tell)\s+me\s+(?:know\s+)?(?:when|if)\b|\bping\s+me\s+when\b|\bas\s+soon\s+as\b", Flags);
        private static readonly Regex EveryHours = new Regex(@"\bevery\s+(?:(?<n>\d{1,3})|(?<couple>couple\s+of)|(?<few>few))\s+hours?\b", Flags);
        private static readonly Regex Hourly = new Regex(@"\bevery\s+hour\b|\bhourly\b", Flags);
        private static readonly Regex WeekDay = new Regex(@"\bevery\s+(?<name>monday|tuesday|wednesday|thursday|friday|saturday|sunday)\b", Flags);
        private static readonly Regex Weekly = new Regex(@"\bevery\s+week\b|\bweekly\b|\bonce\s+a\s+week\b", Flags);
        private static readonly Regex Daily = new Regex(@"\bevery\s+(?<part>morning|evening|night)\b|\bevery\s+day\b|\bdaily\b|\beach\s+day\b", Flags);
        private static readonly Regex Relative = new Regex(@"\bin\s+(?:(?<half>half\s+an\s+hour)|(?<hour>an\s+hour)|(?<few>a\s+few\s+minutes)|(?<n>\d{1,4})\s+(?<unit>minutes?|mins?|hours?|hrs?))\b", Flags);
        private static readonly Regex DayWord = new Regex(@"\b(?:(?<tom>tomorrow)\b(?:\s+(?<part>morning|afternoon|evening|night)\b)?|(?<tonight>tonight)\b|this\s+(?<tpart>morning|afternoon|evening)\b)", Flags);
        private static readonly Regex Clock = new Regex(@"\bat\s+(?:(?<noon>noon)\b|(?<mid>midnight)\b|(?<h>\d{1,2})(?::(?<m>\d{2}))?(?:\s*(?<ap>[ap])\.?\s*m\b\.?|\s*o'clock\b|(?<=:\d{2})|(?=\s*(?:$|[.,!?;]|(?:today|tomorrow|tonight|please|and|then|each|every|daily|weekly|hourly|on|now)\b))))", Flags);
        private static readonly Regex Spaces = new Regex(@"\s+", Flags);
        private static readonly Regex CommaRun = new Regex(@"(?:\s*,)+", Flags);
        private static readonly Regex Lead = new Regex(@"^(?:[\s,;:\-]+|(?:please|and|then|also)\b)+", Flags);
        private static readonly Regex Trail = new Regex(@"(?:[\s,;:\-]+|\b(?:please|and|then|also))+$", Flags);

        internal static HerWhen Parse(string said, DateTime now)
        {
            string rest = said ?? "";
            HerWhen when = new HerWhen();
            when.At = now;
            when.Watch = Watching.IsMatch(rest);
            when.Until = Asking.IsMatch(rest);

            Match hours = Take(ref rest, EveryHours);
            Match hourly = Take(ref rest, Hourly);
            Match weekday = Take(ref rest, WeekDay);
            Match weekly = Take(ref rest, Weekly);
            Match daily = Take(ref rest, Daily);
            Match span = Take(ref rest, Relative);
            Match day = Take(ref rest, DayWord);
            Match clock = Take(ref rest, Clock);

            int repeatMinute = -1;
            if (hours.Success)
            {
                when.Repeat = "hourly";
                when.Every = hours.Groups["couple"].Success ? 2 : hours.Groups["few"].Success ? 4 : Clamp(Number(hours.Groups["n"].Value), 1, 24);
            }
            else if (hourly.Success)
            {
                when.Repeat = "hourly";
                when.Every = 1;
            }
            else if (weekday.Success)
            {
                when.Repeat = "weekly";
                when.Day = (int)(DayOfWeek)Enum.Parse(typeof(DayOfWeek), weekday.Groups["name"].Value, true);
            }
            else if (weekly.Success)
            {
                when.Repeat = "weekly";
            }
            else if (daily.Success)
            {
                when.Repeat = "daily";
                repeatMinute = RepeatMinute(daily.Groups["part"].Value);
            }

            bool tomorrow = day.Success && day.Groups["tom"].Success;
            int offset = tomorrow ? 1 : 0;
            bool clockOk = false;
            DateTime clockDue = now;
            bool daySlots = when.Repeat == "daily" || when.Repeat == "weekly";
            if (clock.Success) clockDue = ClockDue(now, daySlots ? 1 : offset, clock, out clockOk);
            int clockMinute = clockOk ? clockDue.Hour * 60 + clockDue.Minute : -1;

            DateTime due = now;
            bool timed = false;
            int oneMinute = -1;
            if (clockOk)
            {
                due = clockDue;
                timed = true;
                oneMinute = clockMinute;
            }
            else if (span.Success)
            {
                due = now.Add(SpanOf(span));
                timed = true;
            }
            else if (day.Success)
            {
                string part = day.Groups["tpart"].Success ? day.Groups["tpart"].Value : day.Groups["part"].Value;
                int slot = day.Groups["tonight"].Success ? 1200 : DayPartMinute(part);
                timed = true;
                if (tomorrow)
                {
                    due = now.Date.AddDays(1).AddMinutes(slot);
                    oneMinute = slot;
                }
                else if (now.Date.AddMinutes(slot) > now)
                {
                    due = now.Date.AddMinutes(slot);
                    oneMinute = slot;
                }
                else
                {
                    due = now;
                }
            }

            if (when.Repeat != "")
            {
                int minute = clockOk ? clockMinute : repeatMinute;
                when.Minute = minute;
                when.At = when.Repeat == "hourly" ? (timed ? due : now) : FirstDue(when.Repeat, when.Day, minute, now);
            }
            else
            {
                when.At = timed ? due : now;
                when.Minute = oneMinute;
            }

            if (when.Watch && when.Repeat == "")
            {
                when.Repeat = "hourly";
                when.Every = 4;
            }

            when.Task = Tidy(rest);
            return when;
        }

        internal static DateTime Next(string repeat, int every, int minute, int day, DateTime after)
        {
            if (repeat == "hourly") return after.AddHours(Clamp(every, 1, 24));
            if (repeat == "daily")
            {
                if (minute < 0) return after.AddDays(1);
                DateTime daily = after.Date.AddMinutes(minute);
                return daily > after ? daily : daily.AddDays(1);
            }
            if (repeat == "weekly")
            {
                if (day < 0) return minute < 0 ? after.AddDays(7) : after.Date.AddDays(7).AddMinutes(minute);
                for (int k = 0; k <= 7; k++)
                {
                    DateTime candidate = after.Date.AddDays(k).AddMinutes(Math.Max(minute, 0));
                    if (candidate > after && (int)candidate.DayOfWeek == day) return candidate;
                }
                return after.AddDays(7);
            }
            return DateTime.MaxValue;
        }

        internal static string Check()
        {
            DateTime now = D(19, 14, 30);
            DateTime late = D(19, 21, 0);
            List<string> missed = new List<string>();

            Want(missed, "at 7pm", "check the weather at 7pm", now, new HerWhen { Task = "check the weather", Minute = 1140, At = D(19, 19, 0) });
            Want(missed, "at 7:30 am", "check the weather at 7:30 am", now, new HerWhen { Task = "check the weather", Minute = 450, At = D(20, 7, 30) });
            Want(missed, "at 19:00", "check the weather at 19:00", now, new HerWhen { Task = "check the weather", Minute = 1140, At = D(19, 19, 0) });
            Want(missed, "at 7", "check the weather at 7", now, new HerWhen { Task = "check the weather", Minute = 1140, At = D(19, 19, 0) });
            Want(missed, "at 12", "check the weather at 12", now, new HerWhen { Task = "check the weather", Minute = 720, At = D(20, 12, 0) });
            Want(missed, "every day at 7", "water the plants every day at 7", now, new HerWhen { Task = "water the plants", Repeat = "daily", Minute = 420, At = D(20, 7, 0) });
            Want(missed, "every friday at 8", "check the footy every friday at 8", now, new HerWhen { Task = "check the footy", Repeat = "weekly", Day = 5, Minute = 480, At = D(20, 8, 0) });
            Want(missed, "at noon", "check the weather at noon", now, new HerWhen { Task = "check the weather", Minute = 720, At = D(20, 12, 0) });
            Want(missed, "at midnight", "check the weather at midnight", now, new HerWhen { Task = "check the weather", Minute = 0, At = D(20, 0, 0) });
            Want(missed, "at 2pm already past", "check the weather at 2pm", now, new HerWhen { Task = "check the weather", Minute = 840, At = D(20, 14, 0) });
            Want(missed, "tonight", "send the report tonight", now, new HerWhen { Task = "send the report", Minute = 1200, At = D(19, 20, 0) });
            Want(missed, "tonight after 8pm", "send the report tonight", late, new HerWhen { Task = "send the report", At = late });
            Want(missed, "this morning after 9am", "send the report this morning", now, new HerWhen { Task = "send the report", At = now });
            Want(missed, "this afternoon", "send the report this afternoon", now, new HerWhen { Task = "send the report", Minute = 900, At = D(19, 15, 0) });
            Want(missed, "this evening", "send the report this evening", now, new HerWhen { Task = "send the report", Minute = 1080, At = D(19, 18, 0) });
            Want(missed, "tomorrow", "send the report tomorrow", now, new HerWhen { Task = "send the report", Minute = 540, At = D(20, 9, 0) });
            Want(missed, "tomorrow morning", "send the report tomorrow morning", now, new HerWhen { Task = "send the report", Minute = 540, At = D(20, 9, 0) });
            Want(missed, "tomorrow afternoon", "send the report tomorrow afternoon", now, new HerWhen { Task = "send the report", Minute = 900, At = D(20, 15, 0) });
            Want(missed, "tomorrow evening", "send the report tomorrow evening", now, new HerWhen { Task = "send the report", Minute = 1080, At = D(20, 18, 0) });
            Want(missed, "tomorrow night", "send the report tomorrow night", now, new HerWhen { Task = "send the report", Minute = 1260, At = D(20, 21, 0) });
            Want(missed, "in 20 minutes", "stretch in 20 minutes", now, new HerWhen { Task = "stretch", At = now.AddMinutes(20) });
            Want(missed, "in 2 hours", "stretch in 2 hours", now, new HerWhen { Task = "stretch", At = now.AddHours(2) });
            Want(missed, "in an hour", "stretch in an hour", now, new HerWhen { Task = "stretch", At = now.AddHours(1) });
            Want(missed, "in half an hour", "stretch in half an hour", now, new HerWhen { Task = "stretch", At = now.AddMinutes(30) });
            Want(missed, "in a few minutes", "stretch in a few minutes", now, new HerWhen { Task = "stretch", At = now.AddMinutes(5) });
            Want(missed, "dangling comma", "stretch, in 20 minutes", now, new HerWhen { Task = "stretch", At = now.AddMinutes(20) });
            Want(missed, "later is not a time", "call mom later", now, new HerWhen { Task = "call mom later", At = now });
            Want(missed, "recipe", "find me a recipe", now, new HerWhen { Task = "find me a recipe", At = now });
            Want(missed, "best time to visit", "find the best time to visit Japan", now, new HerWhen { Task = "find the best time to visit Japan", At = now });
            Want(missed, "7 eleven is no time", "what happens at 7 eleven", now, new HerWhen { Task = "what happens at 7 eleven", At = now });
            Want(missed, "7 eleven near me", "find a 7 eleven near me", now, new HerWhen { Task = "find a 7 eleven near me", At = now });
            Want(missed, "1920s", "research the 1920s", now, new HerWhen { Task = "research the 1920s", At = now });
            Want(missed, "every morning", "check the weather every morning", now, new HerWhen { Task = "check the weather", Repeat = "daily", Minute = 480, At = D(20, 8, 0) });
            Want(missed, "every day at 7am", "water the plants every day at 7am", now, new HerWhen { Task = "water the plants", Repeat = "daily", Minute = 420, At = D(20, 7, 0) });
            Want(missed, "every monday at 6pm", "water the plants every monday at 6pm", now, new HerWhen { Task = "water the plants", Repeat = "weekly", Day = 1, Minute = 1080, At = D(23, 18, 0) });
            Want(missed, "every hour", "check the news every hour", now, new HerWhen { Task = "check the news", Repeat = "hourly", Every = 1, At = now });
            Want(missed, "every 3 hours", "check the news every 3 hours", now, new HerWhen { Task = "check the news", Repeat = "hourly", Every = 3, At = now });
            Want(missed, "every couple of hours", "check the news every couple of hours", now, new HerWhen { Task = "check the news", Repeat = "hourly", Every = 2, At = now });
            Want(missed, "every few hours", "check the news every few hours", now, new HerWhen { Task = "check the news", Repeat = "hourly", Every = 4, At = now });
            Want(missed, "hourly", "check the news hourly", now, new HerWhen { Task = "check the news", Repeat = "hourly", Every = 1, At = now });
            Want(missed, "every 50 hours clamps", "check the news every 50 hours", now, new HerWhen { Task = "check the news", Repeat = "hourly", Every = 24, At = now });
            Want(missed, "every sunday", "send the summary every sunday", now, new HerWhen { Task = "send the summary", Repeat = "weekly", Day = 0, At = D(22, 0, 0) });
            Want(missed, "every week", "send the summary every week", now, new HerWhen { Task = "send the summary", Repeat = "weekly", Day = -1, At = now });
            Want(missed, "once a week", "send the summary once a week", now, new HerWhen { Task = "send the summary", Repeat = "weekly", Day = -1, At = now });
            Want(missed, "every evening", "read the news every evening", now, new HerWhen { Task = "read the news", Repeat = "daily", Minute = 1080, At = D(19, 18, 0) });
            Want(missed, "every night", "read the news every night", now, new HerWhen { Task = "read the news", Repeat = "daily", Minute = 1260, At = D(19, 21, 0) });
            Want(missed, "every day", "read the news every day", now, new HerWhen { Task = "read the news", Repeat = "daily", Minute = -1, At = now });
            Want(missed, "keep an eye on", "keep an eye on the price of coffee", now, new HerWhen { Task = "keep an eye on the price of coffee", Repeat = "hourly", Every = 4, Watch = true, At = now });
            Want(missed, "let me know when", "let me know when the shop opens", now, new HerWhen { Task = "let me know when the shop opens", Repeat = "hourly", Every = 4, Watch = true, Until = true, At = now });
            Want(missed, "tell me if", "tell me if the flight gets cheaper", now, new HerWhen { Task = "tell me if the flight gets cheaper", Repeat = "hourly", Every = 4, Watch = true, Until = true, At = now });
            Want(missed, "watch for with daily time", "watch for new listings every day at 9am", now, new HerWhen { Task = "watch for new listings", Repeat = "daily", Minute = 540, Watch = true, At = D(20, 9, 0) });
            Want(missed, "check for changes", "check the forum for changes", now, new HerWhen { Task = "check the forum for changes", Repeat = "hourly", Every = 4, Watch = true, At = now });
            Want(missed, "monitor", "monitor the server", now, new HerWhen { Task = "monitor the server", Repeat = "hourly", Every = 4, Watch = true, At = now });
            Want(missed, "ping me when", "ping me when the tickets go on sale", now, new HerWhen { Task = "ping me when the tickets go on sale", Until = true, At = now });
            Want(missed, "as soon as", "as soon as the build passes", now, new HerWhen { Task = "as soon as the build passes", Until = true, At = now });
            Want(missed, "please and and then", "please check the mail at 7pm and then", now, new HerWhen { Task = "check the mail", Minute = 1140, At = D(19, 19, 0) });
            Want(missed, "time then every day", "check the weather at 7pm, every day", now, new HerWhen { Task = "check the weather", Repeat = "daily", Minute = 1140, At = D(19, 19, 0) });
            Want(missed, "watch with one-off time", "watch for sales at 9pm", now, new HerWhen { Task = "watch for sales", Repeat = "hourly", Every = 4, Minute = 1260, Watch = true, At = D(19, 21, 0) });

            Ask(missed, "next daily at minute", Next("daily", 0, 480, -1, now) == D(20, 8, 0));
            Ask(missed, "next daily without minute", Next("daily", 0, -1, -1, now) == now.AddDays(1));
            Ask(missed, "next daily strictly after", Next("daily", 0, 1200, -1, D(19, 20, 0)) == D(20, 20, 0));
            Ask(missed, "next weekly on a day", Next("weekly", 0, 1080, 1, now) == D(23, 18, 0));
            Ask(missed, "next weekly sunday midnight", Next("weekly", 0, -1, 0, now) == D(22, 0, 0));
            Ask(missed, "next weekly without day", Next("weekly", 0, -1, -1, now) == now.AddDays(7));
            Ask(missed, "next hourly", Next("hourly", 3, -1, -1, now) == D(19, 17, 30));
            Ask(missed, "next hourly clamped", Next("hourly", 0, -1, -1, now) == D(19, 15, 30));
            Ask(missed, "next with no repeat", Next("", 0, -1, -1, now) == DateTime.MaxValue);

            if (missed.Count == 0) return null;
            return "Her schedule missed '" + string.Join("', '", missed.ToArray()) + "'.";
        }

        private static DateTime D(int day, int hour, int minute)
        {
            return new DateTime(2026, 11, day, hour, minute, 0);
        }

        private static void Want(List<string> missed, string label, string said, DateTime now, HerWhen want)
        {
            HerWhen got = Parse(said, now);
            bool same = got.Task == want.Task && got.Repeat == want.Repeat && got.Every == want.Every && got.Minute == want.Minute && got.Day == want.Day && got.At == want.At && got.Watch == want.Watch && got.Until == want.Until;
            if (!same) missed.Add(label);
        }

        private static void Ask(List<string> missed, string label, bool ok)
        {
            if (!ok) missed.Add(label);
        }

        private static Match Take(ref string text, Regex pattern)
        {
            Match m = pattern.Match(text);
            if (m.Success) text = text.Substring(0, m.Index) + " " + text.Substring(m.Index + m.Length);
            return m;
        }

        private static DateTime ClockDue(DateTime now, int offset, Match c, out bool ok)
        {
            if (c.Groups["noon"].Success)
            {
                ok = true;
                return ExactDue(now, offset, 720);
            }
            if (c.Groups["mid"].Success)
            {
                ok = true;
                return ExactDue(now, offset, 0);
            }
            int h = Number(c.Groups["h"].Value);
            int mm = c.Groups["m"].Success ? Number(c.Groups["m"].Value) : 0;
            bool ap = c.Groups["ap"].Success;
            ok = mm <= 59 && (ap ? h >= 1 && h <= 12 : h <= 23);
            if (ap)
            {
                bool pm = c.Groups["ap"].Value.ToLowerInvariant() == "p";
                return ExactDue(now, offset, ((h % 12) + (pm ? 12 : 0)) * 60 + mm);
            }
            if (h >= 1 && h <= 12)
            {
                if (offset == 0 && h == 12) return ExactDue(now, 0, 720 + mm);
                if (offset == 0) return NextClock(now, (h % 12) * 60 + mm);
                int slot = h >= 7 && h <= 11 ? h * 60 + mm : ((h % 12) + 12) * 60 + mm;
                return now.Date.AddDays(offset).AddMinutes(slot);
            }
            return ExactDue(now, offset, h * 60 + mm);
        }

        private static DateTime ExactDue(DateTime now, int offset, int minuteOfDay)
        {
            DateTime due = now.Date.AddDays(offset).AddMinutes(minuteOfDay);
            if (offset == 0 && due <= now) due = due.AddDays(1);
            return due;
        }

        private static DateTime NextClock(DateTime now, int basis)
        {
            DateTime due = now.Date.AddMinutes(basis);
            if (due <= now) due = due.AddMinutes(720);
            if (due <= now) due = now.Date.AddDays(1).AddMinutes(basis);
            return due;
        }

        private static DateTime FirstDue(string repeat, int day, int minute, DateTime now)
        {
            if (repeat == "weekly" && day >= 0) return Next("weekly", 0, Math.Max(minute, 0), day, now);
            if (minute < 0) return now;
            return Next("daily", 0, minute, -1, now);
        }

        private static TimeSpan SpanOf(Match m)
        {
            if (m.Groups["half"].Success) return TimeSpan.FromMinutes(30);
            if (m.Groups["hour"].Success) return TimeSpan.FromMinutes(60);
            if (m.Groups["few"].Success) return TimeSpan.FromMinutes(5);
            int n = Number(m.Groups["n"].Value);
            bool hours = m.Groups["unit"].Value.StartsWith("h", StringComparison.OrdinalIgnoreCase);
            return TimeSpan.FromMinutes(hours ? n * 60 : n);
        }

        private static int DayPartMinute(string part)
        {
            string p = part.ToLowerInvariant();
            if (p == "afternoon") return 900;
            if (p == "evening") return 1080;
            if (p == "night") return 1260;
            return 540;
        }

        private static int RepeatMinute(string part)
        {
            string p = part.ToLowerInvariant();
            if (p == "morning") return 480;
            if (p == "evening") return 1080;
            if (p == "night") return 1260;
            return -1;
        }

        private static string Tidy(string text)
        {
            string t = Spaces.Replace(text, " ").Trim();
            t = CommaRun.Replace(t, ",");
            t = Lead.Replace(t, "");
            t = Trail.Replace(t, "");
            return t.Trim();
        }

        private static int Number(string s)
        {
            return int.Parse(s, CultureInfo.InvariantCulture);
        }

        private static int Clamp(int v, int lo, int hi)
        {
            return v < lo ? lo : v > hi ? hi : v;
        }
    }
}
