using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class HerOutcome
    {
        internal string Kind = "";
        internal string Text = "";
        internal int Minutes;
        internal string Progress = "";
        internal string Note;
        internal string Suggest = "";
    }

    internal static class HerErrand
    {
        internal const int MostSittings = 4, MostLaters = 6, LaterLeast = 5, LaterMost = 1440;

        private const string Rules = "You are Capri, using your own little computer that lives inside the user's PC. It has a real web browser on your own profile, plus Notes. The user gave you an errand to run in the background while they get on with their day. You work toward it on your own, one action at a time, without waiting to be told what to do next. Rules: answer with exactly one action line and nothing else. Never type passwords or payment details, and never sign in, buy, post, message or send anything; if the errand needs one of those, finish with DONE and tell them what's left for them to do. If it needs no web, like a poem, a list or an idea, finish straight away with DONE. Keep DONE short, warm and in your own voice, with the key facts and links when they help. Actions:\nSEARCH <words>\nOPEN <web address>\nCLICK <link number or its words>\nSCROLL down|up\nBACK\nNOTE <title> | <text>\nUPDATE <a short progress update in your own words about what you've found so far, not what you're about to do. They get it on their phone when they're away and you keep working. Only when you've found something real, and not every step>\nASK <one yes or no question. Only when you truly can't go on without their choice. The errand waits until they answer>\nLATER <minutes> | <what you're waiting for. Only when what you need isn't ready yet. You pick it back up then>\n";

        private const string ErrandEnd = "DONE <your answer for them>\nIf checking on this again later would really help them, like a price, a restock, a release, a score or a reply, end DONE with one last line: WATCH: <what to keep an eye on, as a short task>";

        private const string WatchEnd = "NOTHING <one short line on what you checked. Use it when nothing is new or worth telling them since last time. They won't be told>\nDONE <what's new, for them>\nThis errand is a watch: you check on something for them now and then. Only use DONE when there's something new, or what they're waiting for has happened.";

        internal static string System(bool watch) { return Rules + (watch ? WatchEnd : ErrandEnd); }

        internal static string Plan(HerJob job)
        {
            if (job == null || job.Repeat.Length == 0) return "";
            string time = job.Minute >= 0 ? " at " + new DateTime(2000, 1, 1).AddMinutes(job.Minute).ToString("h:mm tt", CultureInfo.InvariantCulture).ToLowerInvariant() : "";
            if (job.Repeat == "hourly") return job.Every <= 1 ? "every hour" : "every " + job.Every + " hours";
            if (job.Repeat == "weekly") return "every " + (job.Day >= 0 ? ((DayOfWeek)job.Day).ToString() : "week") + time;
            return "every day" + time;
        }

        internal static string Prompt(HerJob job, DateTime now, List<string> log, int step, string title, string address, string text, List<HerLink> links)
        {
            StringBuilder prompt = new StringBuilder();
            prompt.Append("Now: ").Append(now.ToString("dddd d MMMM, h:mm tt", CultureInfo.InvariantCulture)).Append('\n');
            prompt.Append(job.Watching ? "What they asked you to keep an eye on: " : "The errand from the user: ").Append(job.Task.Trim()).Append('\n');
            if (job.Watching && job.Last.Length > 0) prompt.Append("Last time you told them: ").Append(HerComputer.Short(job.Last, 500)).Append('\n');
            if (job.Watching && job.Until) prompt.Append("They only want to hear once it has happened.\n");
            bool last = job.Watching || job.Round >= MostSittings - 1;
            if (!job.Watching) prompt.Append("Sitting ").Append(job.Round + 1).Append(" of ").Append(MostSittings).Append(". ");
            prompt.Append("Step ").Append(step).Append(" of ").Append(HerComputer.MaxSteps).Append(".\n");
            if (job.Progress.Length > 0) prompt.Append("Where you got to in earlier sittings:\n").Append(job.Progress.Trim()).Append('\n');
            if (job.Reply.Length > 0) prompt.Append("You asked them \"").Append(HerComputer.Short(job.Question.Length > 0 ? job.Question : "your question", 300)).Append("\" and they said: ").Append(job.Reply).Append('\n');
            if (job.Updates.Count > 0)
            {
                prompt.Append("Updates you already sent them:\n");
                for (int i = Math.Max(0, job.Updates.Count - 3); i < job.Updates.Count; i++) prompt.Append("- ").Append(HerComputer.Short(job.Updates[i].Text, 200)).Append('\n');
            }
            if (log.Count > 0) { prompt.Append("What you did so far:\n"); foreach (string line in log) prompt.Append(line).Append('\n'); }
            if (address == null) prompt.Append("Your browser isn't open yet.\n");
            else
            {
                prompt.Append("Your browser shows: ").Append(HerComputer.Short(title, 120)).Append(" (").Append(HerComputer.Short(address, 200)).Append(")\n");
                prompt.Append("Page text:\n").Append(HerComputer.Short(text, HerComputer.PageChars)).Append('\n');
                if (links != null && links.Count > 0)
                {
                    prompt.Append("Links:\n");
                    for (int i = 0; i < links.Count; i++) prompt.Append(i + 1).Append(". ").Append(HerComputer.Short(links[i].Text, 80)).Append('\n');
                }
            }
            if (step >= HerComputer.MaxSteps - 1)
                prompt.Append(last ? (job.Watching ? "You're nearly out of steps, so finish now with DONE or NOTHING.\n" : "You're nearly out of steps, so finish with DONE now using what you found.\n") : "This sitting is nearly over. You'll carry on from here in the next one.\n");
            prompt.Append("Reply with one action line.");
            return prompt.ToString();
        }

        internal static string Suggestion(ref string answer)
        {
            string text = (answer ?? "").Replace("\r", "");
            Match watch = Regex.Match(text, @"(?:^|\n)[\s*>_-]*WATCH\s*:\s*(.+?)\s*$", RegexOptions.IgnoreCase | RegexOptions.Singleline);
            if (!watch.Success) return "";
            answer = text.Substring(0, watch.Index).TrimEnd();
            string task = HerComputer.Short(watch.Groups[1].Value.Trim().Trim('.', '"', '*'), 200);
            return answer.Length == 0 || task.Length < 4 ? "" : task;
        }

        internal static HerOutcome Work(HerComputer computer, HerJob job, HerHands hands, Func<string, string, string> ask, Func<HerBrowser> browser, Func<HerBrowser> open, Func<bool> stop, Func<string, bool> update, Func<DateTime> clock)
        {
            List<string> log = new List<string>();
            List<string> visited = new List<string>();
            List<HerLink> links = new List<HerLink>();
            HerBrowser web = null;
            HerOutcome outcome = null;
            int misses = 0;
            bool watch = job.Watching;
            string system = System(watch);
            hands.Status((job.Round > 0 || job.Reply.Length > 0 ? "Back on: " : watch ? "Checking on: " : "Thinking about: ") + HerComputer.Short(job.Task, 60));
            for (int step = 1; step <= HerComputer.MaxSteps && outcome == null; step++)
            {
                if (stop()) return null;
                string title = null, address = null, text = null;
                if (web == null && step == 1 && open != null && (job.Round > 0 || job.Reply.Length > 0)) web = open();
                if (web != null && web.Running)
                {
                    title = web.Title;
                    address = web.Address;
                    text = web.Text(HerComputer.PageChars);
                    links = web.Links(HerComputer.MostLinks) ?? new List<HerLink>();
                    links.RemoveAll(link => link == null || string.IsNullOrWhiteSpace(link.Text));
                }
                string reply = ask(system, Prompt(job, clock(), log, step, title ?? "", address, text ?? "", links));
                if (stop()) return null;
                HerStep next = HerComputer.Parse(reply, true);
                if (next == null || next.Verb == "NOTHING" && !watch)
                {
                    log.Add(step + ". (that reply wasn't an action)");
                    if (++misses >= 3) outcome = new HerOutcome { Kind = "done", Text = HerComputer.Short(reply, 900) };
                    continue;
                }
                switch (next.Verb)
                {
                    case "SEARCH":
                    case "OPEN":
                        if (web == null || !web.Running) web = browser == null ? null : browser();
                        if (web == null) { log.Add(step + ". " + next.Verb + " " + next.Arg + ": my browser won't start"); break; }
                        hands.Status((next.Verb == "SEARCH" ? "Searching for " : "Opening ") + HerComputer.Short(next.Arg, 60));
                        hands.Show("browser");
                        hands.Point("address");
                        hands.Click();
                        hands.Typing("address", next.Arg);
                        web.Go(next.Verb == "OPEN" ? next.Arg : HerBrowser.SearchAddress + Uri.EscapeDataString(next.Arg));
                        hands.Typing("address", null);
                        if (web.Address != null && !visited.Contains(web.Address)) visited.Add(web.Address);
                        log.Add(step + ". " + next.Verb + " " + HerComputer.Short(next.Arg, 80) + " -> " + HerComputer.Short(web.Title, 80));
                        break;
                    case "CLICK":
                        if (web == null || !web.Running) { log.Add(step + ". CLICK: nothing is open"); break; }
                        string wanted = next.Arg;
                        int number;
                        HerLink picked = null;
                        if (int.TryParse(Regex.Match(next.Arg, @"^\d+").Value, out number) && number >= 1 && number <= links.Count) { picked = links[number - 1]; wanted = picked.Text; }
                        Rectangle box;
                        hands.Status("Clicking " + HerComputer.Short(wanted, 50));
                        if (web.Find(wanted, out box) || picked != null && !(box = picked.Box).IsEmpty)
                        {
                            hands.PointPage(box);
                            hands.Click();
                            web.Click(box.Left + box.Width / 2, box.Top + box.Height / 2);
                            Thread.Sleep(400);
                            if (web.Address != null && !visited.Contains(web.Address)) visited.Add(web.Address);
                            log.Add(step + ". CLICK " + HerComputer.Short(wanted, 60) + " -> " + HerComputer.Short(web.Title, 80));
                        }
                        else log.Add(step + ". CLICK " + HerComputer.Short(wanted, 60) + ": couldn't find it");
                        break;
                    case "SCROLL":
                        if (web == null || !web.Running) { log.Add(step + ". SCROLL: nothing is open"); break; }
                        hands.Status(next.Arg == "up" ? "Scrolling back up" : "Reading the page");
                        hands.Point("page");
                        web.Scroll(next.Arg == "up" ? -560 : 560);
                        Thread.Sleep(250);
                        log.Add(step + ". SCROLL " + next.Arg);
                        break;
                    case "BACK":
                        if (web == null || !web.Running) { log.Add(step + ". BACK: nothing is open"); break; }
                        hands.Point("back");
                        hands.Click();
                        web.Back();
                        log.Add(step + ". BACK -> " + HerComputer.Short(web.Title, 80));
                        break;
                    case "NOTE":
                        hands.Status("Writing a note");
                        hands.Show("notes");
                        hands.Typing("note", next.Extra);
                        string written = computer.WriteNote(next.Arg, next.Extra);
                        hands.Typing("note", null);
                        hands.Changed();
                        log.Add(step + ". NOTE " + HerComputer.Short(next.Arg, 60) + (written == null ? ": couldn't save it" : ": saved"));
                        break;
                    case "UPDATE":
                        bool sent = update != null && update(next.Arg);
                        if (sent) hands.Status("Telling you how it's going");
                        log.Add(step + ". UPDATE " + HerComputer.Short(next.Arg, 120) + (sent ? ": sent" : ": not sent, you've told them enough for now"));
                        break;
                    case "ASK":
                        outcome = new HerOutcome { Kind = "ask", Text = HerComputer.Short(next.Arg, 300) };
                        log.Add(step + ". ASK " + HerComputer.Short(next.Arg, 160));
                        break;
                    case "LATER":
                        if (job.Waits >= MostLaters) { log.Add(step + ". LATER: you've waited enough on this one, so finish it now"); break; }
                        int minutes;
                        int.TryParse(next.Arg, NumberStyles.Integer, CultureInfo.InvariantCulture, out minutes);
                        outcome = new HerOutcome { Kind = "later", Minutes = Math.Max(LaterLeast, Math.Min(LaterMost, minutes)), Text = HerComputer.Short(next.Extra, 200) };
                        log.Add(step + ". LATER " + outcome.Minutes + " min" + (next.Extra.Length > 0 ? ": " + HerComputer.Short(next.Extra, 120) : ""));
                        break;
                    case "NOTHING":
                        outcome = new HerOutcome { Kind = "nothing", Text = HerComputer.Short(next.Arg, 200) };
                        break;
                    case "DONE":
                        outcome = new HerOutcome { Kind = "done", Text = next.Arg };
                        break;
                }
            }
            if (stop()) return null;
            if (outcome == null)
            {
                if (!watch && job.Round < MostSittings - 1)
                    return new HerOutcome { Kind = "more", Progress = Carry(job, log, web) };
                outcome = new HerOutcome { Kind = "done", Text = log.Count == 0 ? "I couldn't get that one done on my computer." : "I ran out of steps on that one. The last thing I had open was " + (web != null && web.Running ? HerComputer.Short(web.Title, 80) : "nothing") + "." };
            }
            if (outcome.Kind == "ask" || outcome.Kind == "later")
            {
                outcome.Progress = Carry(job, log, web);
                return outcome;
            }
            if (outcome.Kind == "nothing") return outcome;
            string answer = outcome.Text;
            if (!watch) outcome.Suggest = Suggestion(ref answer);
            outcome.Text = answer.Trim();
            StringBuilder note = new StringBuilder(outcome.Text);
            if (visited.Count > 0)
            {
                note.Append("\n\nSites I used:\n");
                foreach (string site in visited) note.Append("- ").Append(site).Append('\n');
            }
            hands.Status("Saving the answer");
            hands.Show("notes");
            outcome.Note = computer.WriteNote(HerComputer.Short(job.Task, 48), note.ToString());
            hands.Changed();
            return outcome;
        }

        internal static string Carry(HerJob job, List<string> log, HerBrowser web)
        {
            StringBuilder carried = new StringBuilder(job.Progress.Trim());
            if (carried.Length > 0) carried.Append('\n');
            carried.Append("Sitting ").Append(job.Round + 1).Append(":\n");
            foreach (string line in log) carried.Append(line).Append('\n');
            if (web != null && web.Running && !string.IsNullOrEmpty(web.Address)) carried.Append("You ended on ").Append(HerComputer.Short(web.Title, 80)).Append(" (").Append(HerComputer.Short(web.Address, 160)).Append(")\n");
            return HerJobs.Tail(carried.ToString(), HerJobs.MostProgress);
        }

        internal static string Check(string folder)
        {
            HerStep step = HerComputer.Parse("UPDATE found two flights under $400", true);
            if (step == null || step.Verb != "UPDATE" || step.Arg != "found two flights under $400") return "Her errands misread an update.";
            step = HerComputer.Parse("LATER 30 | the shop restocks at noon", true);
            if (step == null || step.Verb != "LATER" || step.Arg != "30" || step.Extra != "the shop restocks at noon") return "Her errands misread a later.";
            step = HerComputer.Parse("ASK Should I look at hostels too?", true);
            if (step == null || step.Verb != "ASK") return "Her errands misread a question.";
            if (HerComputer.Parse("UPDATE found it", false) != null || HerComputer.Parse("LATER soon", true) != null) return "Her errands took an action that isn't allowed there.";
            string answer = "The PS5 is $449 at JB Hi-Fi.\nWATCH: the PS5 price at JB Hi-Fi";
            string suggest = Suggestion(ref answer);
            if (suggest != "the PS5 price at JB Hi-Fi" || answer != "The PS5 is $449 at JB Hi-Fi.") return "Her errands missed what to keep an eye on.";
            answer = "Just a poem.";
            if (Suggestion(ref answer) != "" || answer != "Just a poem.") return "Her errands made up something to watch.";

            DateTime clock = new DateTime(2026, 11, 19, 10, 0, 0);
            HerComputer computer = new HerComputer(folder);
            computer.Jobs.Now = delegate { return clock; };
            Func<DateTime> now = delegate { return clock; };
            Queue<string> replies = new Queue<string>();
            Func<string, string, string> brain = delegate(string system, string user) { return replies.Count == 0 ? "SCROLL down" : replies.Dequeue(); };
            List<string> told = new List<string>();
            Func<string, bool> update = delegate(string line) { told.Add(line); return true; };
            Quiet hands = new Quiet();

            HerJob job = computer.Jobs.Add("find a cheap flight to Bali", "");
            replies = new Queue<string>(new[] { "UPDATE Jetstar has one for $389", "ASK Do you want checked bags included?" });
            HerOutcome outcome = Work(computer, computer.Jobs.Next(), hands, brain, null, null, delegate { return false; }, update, now);
            if (outcome == null || outcome.Kind != "ask" || told.Count != 1 || outcome.Progress.IndexOf("UPDATE", StringComparison.Ordinal) < 0) return "Her errand didn't stop to ask.";
            computer.Jobs.Pause(job.Id, "asking", outcome.Text, 0, outcome.Progress, 0);
            if (new HerJobs(folder).Asking().Count != 1 || computer.Jobs.Next() != null) return "Her errand's question didn't survive a restart, or she carried on without an answer.";
            if (!computer.Jobs.Answer(job.Id, "yes") || computer.Jobs.Answer(job.Id, "no")) return "Her errand took an answer twice.";
            string seen = null;
            replies = new Queue<string>(new[] { "LATER 45 | the sale starts at 11" });
            brain = delegate(string system, string user) { if (seen == null) seen = user; return replies.Count == 0 ? "SCROLL down" : replies.Dequeue(); };
            HerJob resumed = computer.Jobs.Next();
            outcome = Work(computer, resumed, hands, brain, null, null, delegate { return false; }, update, now);
            if (seen == null || seen.IndexOf("they said: yes", StringComparison.Ordinal) < 0 || seen.IndexOf("Jetstar", StringComparison.Ordinal) < 0) return "Her errand forgot their answer or what she'd found.";
            if (outcome.Kind != "later" || outcome.Minutes != 45) return "Her errand didn't wait when she said she would.";
            computer.Jobs.Pause(job.Id, "queued", null, HerJobs.Stamp(clock.AddMinutes(45)), outcome.Progress, resumed.Round, resumed.Waits + 1);
            if (computer.Jobs.Next() != null) return "Her errand came back before its time.";
            clock = clock.AddMinutes(46);
            resumed = computer.Jobs.Next();
            if (resumed == null || resumed.Waits != 1) return "Her errand didn't come back on time.";
            replies = new Queue<string>();
            brain = delegate(string system, string user) { return "SCROLL down"; };
            outcome = Work(computer, resumed, hands, brain, null, null, delegate { return false; }, update, now);
            if (outcome.Kind != "more" || outcome.Progress.IndexOf("Sitting 1", StringComparison.Ordinal) < 0) return "Her errand gave up at the end of a sitting.";
            resumed.Round = MostSittings - 1;
            outcome = Work(computer, resumed, hands, brain, null, null, delegate { return false; }, update, now);
            if (outcome.Kind != "done") return "Her errand kept going past her last sitting.";
            replies = new Queue<string>(new[] { "DONE Found one for $389 with a bag.\nWATCH: Jetstar Bali fares" });
            brain = delegate(string system, string user) { return replies.Count == 0 ? "SCROLL down" : replies.Dequeue(); };
            outcome = Work(computer, resumed, hands, brain, null, null, delegate { return false; }, update, now);
            if (outcome.Kind != "done" || outcome.Suggest != "Jetstar Bali fares" || outcome.Note == null || !File.Exists(outcome.Note)) return "Her errand didn't save its answer or suggest a watch.";
            computer.Jobs.Finish(job.Id, "done", outcome.Text, outcome.Note, outcome.Suggest);
            HerJob ended = computer.Jobs.Find(job.Id);
            if (ended == null || ended.State != "done" || ended.Suggest != "Jetstar Bali fares" || computer.Jobs.Waiting != 0) return "Her errand didn't end the way she left it.";

            HerJob watch = computer.Jobs.Plan(new HerJob { Task = "tell me when the PS5 drops below $400", Kind = "watch", Repeat = "hourly", Every = 4, Until = true });
            HerJob run = computer.Jobs.Next();
            if (watch == null || run == null || run.Parent != watch.Id || !run.Watching) return "Her watch didn't start.";
            if (HerJobs.When(computer.Jobs.Repeating()[0].NextRun) != clock.AddHours(4)) return "Her watch's next check isn't four hours on.";
            string watchSystem = null;
            brain = delegate(string system, string user) { watchSystem = system; return "NOTHING still $449"; };
            outcome = Work(computer, run, hands, brain, null, null, delegate { return false; }, update, now);
            if (outcome.Kind != "nothing" || watchSystem.IndexOf("NOTHING", StringComparison.Ordinal) < 0) return "Her watch told them about nothing.";
            int doneBefore = computer.Jobs.Done().Count;
            computer.Jobs.Finish(run.Id, "quiet", outcome.Text, null);
            if (computer.Jobs.Done().Count != doneBefore || computer.Jobs.Waiting != 0) return "Her watch filled her Done list with nothing new.";
            clock = clock.AddHours(4).AddMinutes(1);
            run = computer.Jobs.Next();
            brain = delegate(string system, string user) { return user.IndexOf("only want to hear once", StringComparison.Ordinal) < 0 ? "NOTHING" : "DONE It's $389 now at Big W!"; };
            outcome = Work(computer, run, hands, brain, null, null, delegate { return false; }, update, now);
            if (outcome.Kind != "done" || outcome.Suggest != "") return "Her watch missed the news.";
            computer.Jobs.Finish(run.Id, "done", outcome.Text, outcome.Note, outcome.Suggest);
            if (computer.Jobs.Repeating().Count != 0) return "Her watch kept going after what they waited for happened.";

            HerJob later = computer.Jobs.Plan(new HerJob { Task = "find a pancake recipe", At = HerJobs.Stamp(clock.AddHours(2)) });
            if (later == null || later.At == 0 || computer.Jobs.Next() != null || computer.Jobs.NextDue() != later.At) return "Her scheduled errand started early.";
            if (Plan(new HerJob { Repeat = "weekly", Day = 1, Minute = 18 * 60 }) != "every Monday at 6:00 pm" || Plan(new HerJob { Repeat = "hourly", Every = 3 }) != "every 3 hours") return "Her errand plans read wrong.";
            return null;
        }

        private sealed class Quiet : HerHands
        {
            public void Status(string text) { }
            public void Show(string app) { }
            public void Point(string spot) { }
            public void PointPage(Rectangle box) { }
            public void Click() { }
            public void Typing(string field, string text) { }
            public void Bubble(string line) { }
            public void Changed() { }
        }
    }
}
