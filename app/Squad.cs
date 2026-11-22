using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class SquadMate
    {
        internal int Number;
        internal string Name = "", Part = "", Doing = "", Answer, Note;
        internal string State = "waiting";
        internal string Persona;
        internal int Asked;
    }

    internal sealed class SquadFriend
    {
        internal string Name, Persona;
    }

    internal sealed class Squad
    {
        internal const int Most = 3, Steps = 8, MostChats = 5, BoardChars = 220;
        internal static Func<List<SquadFriend>> Friends = delegate { return new List<SquadFriend>(); };

        private const string PlanSystem = "You are Capri, a desktop companion who can split into a squad of copies of yourself. Each copy works on one part of the same job at the same time, in her own web browser, and you put what they find together at the end. Split the user's job into {0} parts that can be done side by side without waiting on each other, so together they cover the whole job. Give each copy a nickname of two or three lower case words that fits her part, like deal hunter or review nerd. Answer with one line a part, numbered from 1, as: number. nickname | what she does. Nothing else.";
        private const string MateLines = "\nYou're one of {0} copies of Capri in a squad, splitting one job from the user. The whole job: {1}\nYour nickname: {2}. Your part, and only your part: {3}\nLeave the other parts to the others. The squad board shows what they're doing and what they found, so don't repeat their work, and use what they found when it helps yours. Finish with DONE as soon as your part is covered: a few lines with the key facts and links for your part only.";
        private const string MergeSystem = "You are Capri. A squad of copies of you just split a job from the user and each did her part. Put what they found together into one answer for the user, in your own voice: short and warm, the key facts and the best links, no repeats, no headings. Say who found what only when it's fun or useful. If a part came back empty, say so plainly.";
        private const string ChatAsk = "You've split into a squad of {0} copies of yourself to do a job for the user: {1}\nYou're 0, the one who stays with the user. The others are working right now, each in her own browser:\n{2}Write what you all say to each other next, the way you really talk: funny, a bit chaotic, about what each of you is actually doing or has found right now. Two or three lines, each under twelve words. One line a row, as the speaker's number, a colon, then the words, like 2: words. Use numbers 0 to {0}. Nothing else.";

        internal readonly string Job;
        internal readonly List<SquadMate> Mates = new List<SquadMate>();
        internal readonly List<string> Sites = new List<string>();
        private readonly object gate = new object();
        private volatile bool stopped;
        internal int Chats;
        internal string LastChat;

        internal Squad(string job) { Job = (job ?? "").Trim(); }

        internal bool Stopped { get { return stopped; } }
        internal void Stop() { stopped = true; }

        internal bool Working { get { lock (gate) return Mates.Exists(mate => mate.State == "working"); } }

        // She only splits up when she's asked to straight out: the line starts with squad up, make or send a squad, get your
        // squad to, or split into a squad or into 3, or it ends with as a squad or with your squad. Even then it has to be a job
        // she can look up, so "squad up and win this" stays chat. A number is her squad's size only inside those words ("a
        // squad of 3", "split into 3"); any other number stays part of the job.
        private const string Count = @"(?<size>two|three|four|five|six|seven|eight|nine|ten|[2-9]|10)";
        private static readonly Regex Front = new Regex(@"^(?:squad up(?: with " + Count + @" of (?:you|yourselves|u)| into " + Count + @")?|(?:make|form|start|assemble|send(?: out)?|use|get) (?:a|your|ur) squad(?: of " + Count + @")?(?: together)?|(?:tell|ask) (?:your|ur) squad|split (?:up |yourself |urself )?into (?:a squad(?: of " + Count + @")?|" + Count + @"(?: of you| squads?)?))(?:\s*$|\s*[:,.!\-]\s*(?<job>.+)$|\s+(?:and|to|then|for)\s+(?<job>.+)$)", RegexOptions.IgnoreCase);
        private static readonly Regex Have = new Regex(@"^(?:have|let) (?:your|ur) squad (?<job>.+)$", RegexOptions.IgnoreCase);
        private static readonly Regex Back = new Regex(@"^(?<job>.+?)[\s,]+(?:as a squad(?: of " + Count + @")?|in a squad of " + Count + @"|(?:with|using) (?:your|ur) squad|(?:with|using) a squad of " + Count + @")$", RegexOptions.IgnoreCase);
        private static readonly Regex Lookup = new Regex(@"^(?:help (?:me|us) (?:to )?(?:pick|choose|decide|shop)\b|(?:help (?:me|us) (?:to )?)?(?:find|search|research|compare|check|plan|hunt|track|dig (?:up|into|through)|scout|browse|read|gather|collect|list|shortlist|work out|figure out|suss out|put together|pull together|round up|make (?:me |us )?(?:a |an )?(?:list|plan|shortlist|guide|itinerary|summary|comparison)|write|draft|summari[sz]e|sum up|explain|tell (?:me|us)|show (?:me|us)|see (?:what|if|whether|which|how|who|where|when|why)|learn|explore|investigate|review|rate|rank|price|shop around|look (?:for|up|into|at|around|through|over|out for)|get (?:me|us)|grab (?:me|us)|fetch|source|map out|go (?:through|over)|recommend|suggest|come up with|brainstorm|think up|scope out|weigh up|sort through|line up|estimate)\b)", RegexOptions.IgnoreCase);
        private static readonly Regex Recall = new Regex(@"^(?:(?:stop|cancel|end|disband|recall|call off|call back|pull back|bring back) (?:the|your|ur) squad|(?:call|bring|pull) (?:the|your|ur) squad (?:back|off|home|in)|send (?:the|your|ur) squad home)(?: (?:now|right now|already|for now|back|home|off))*$", RegexOptions.IgnoreCase);

        private static string Asked(string said)
        {
            string text = Regex.Replace((said ?? "").Trim(), @"\s+", " ");
            text = Regex.Replace(text, @"^(?:(?:hey|ok|okay|yo)\s+)?capri\b[,!.:]?\s*", "", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"^(?:(?:ok|okay|alright)[,!.]?\s+|(?:can|could|would|will) you\s+|please\s+|pls\s+|go\s+|i (?:want|need) you to\s+)+", "", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"[\s,]*(?:\b(?:please|pls))?[\s.!?]*$", "", RegexOptions.IgnoreCase).Trim();
        }

        internal static string JobIn(string said, out int count)
        {
            count = 0;
            string text = Asked(said);
            Match asked = Front.Match(text);
            if (!asked.Success) asked = Have.Match(text);
            if (!asked.Success) asked = Back.Match(text);
            if (!asked.Success) return null;
            string job = Regex.Replace(asked.Groups["job"].Value.Trim(), @"^(?:(?:and|to|then|please|go|also|can you|could you)\s+)+", "", RegexOptions.IgnoreCase);
            job = job.Trim().TrimEnd(',', ';', ':', '-').Trim();
            if (job.Length > 0 && !Lookup.IsMatch(job)) return null;
            switch (asked.Groups["size"].Value.ToLowerInvariant())
            {
                case "": count = 0; break;
                case "two": case "2": count = 2; break;
                case "three": case "3": count = 3; break;
                default: count = Most; break;
            }
            return job;
        }

        internal static bool StopAsked(string said) { return Recall.IsMatch(Asked(said)); }

        internal static string PlanPrompt(int count)
        {
            return string.Format(PlanSystem, count >= 2 ? (count == 2 ? "two" : "three") : "two or three (three only when it really helps)");
        }

        internal static List<SquadMate> Plan(string reply, int count)
        {
            List<SquadMate> found = new List<SquadMate>();
            int most = count >= 2 ? Math.Min(count, Most) : Most;
            foreach (string row in (reply ?? "").Split('\n'))
            {
                Match line = Regex.Match(row.Trim(), @"^(?:[-*]\s*)?(?:part\s*)?(\d{1,2})\s*[.:)\-]\s*(.+)$", RegexOptions.IgnoreCase);
                if (!line.Success) continue;
                string body = line.Groups[2].Value.Replace("**", "").Trim();
                int bar = body.IndexOf('|');
                string name = bar > 0 ? body.Substring(0, bar).Trim() : "";
                string part = bar > 0 ? body.Substring(bar + 1).Trim() : body;
                if (part.Length < 3) continue;
                name = Regex.Replace(name, @"[^\p{L}\p{N}' \-]", "").Trim().ToLowerInvariant();
                if (name.Length == 0 || name.Length > 24) name = "capri " + (found.Count + 2);
                found.Add(new SquadMate { Number = found.Count + 1, Name = name, Part = HerComputer.Short(part, 240) });
                if (found.Count == most) break;
            }
            return found;
        }

        internal bool Make(Func<string, string, string> lead, int count)
        {
            string reply = null;
            try { reply = lead(PlanPrompt(count) + "\n" + Brain.Safety, "The user's job: " + Job); }
            catch (Exception error) { Brain.Log("squad", -1, "plan failed: " + error.Message); }
            List<SquadMate> made = Plan(reply, count);
            if (made.Count == 0) made.Add(new SquadMate { Number = 1, Name = "capri 2", Part = Job });
            List<SquadFriend> friends = null;
            try { friends = Friends(); }
            catch (Exception) { }
            if (friends != null)
                for (int i = 0; i < friends.Count && i < made.Count; i++)
                    if (friends[i] != null && !string.IsNullOrWhiteSpace(friends[i].Name) && !string.IsNullOrWhiteSpace(friends[i].Persona))
                    {
                        made[made.Count - 1 - i].Name = HerComputer.Short(friends[i].Name.Trim(), 24);
                        made[made.Count - 1 - i].Persona = friends[i].Persona;
                    }
            lock (gate) { Mates.Clear(); Mates.AddRange(made); }
            return !stopped;
        }

        // The squad saves one note at the end, so her copies get her computer's steps without Notes.
        private static string MateSteps { get { return HerComputer.StepSystem.Replace(", plus Notes", "").Replace("\nNOTE <title> | <text>", ""); } }

        internal static string ChatSystem { get { return Brain.Character; } }

        internal string MateSystem(SquadMate mate)
        {
            string lines = string.Format(MateLines, Mates.Count, HerComputer.Short(Job, 300), mate.Name, mate.Part);
            if (mate.Persona == null) return MateSteps + lines + "\n" + Brain.Safety;
            return mate.Persona + "\n\nRight now you're helping Capri with a job on her own little computer, which has a real web browser on its own profile." + MateSteps.Substring(MateSteps.IndexOf(" You do jobs", StringComparison.Ordinal)) + lines.Replace("one of " + Mates.Count + " copies of Capri", "in Capri's squad of " + Mates.Count) + "\n" + Brain.Holds + "\n" + Brain.Safety;
        }

        internal string Board(SquadMate asking)
        {
            StringBuilder board = new StringBuilder();
            lock (gate)
                foreach (SquadMate mate in Mates)
                {
                    if (mate == asking) continue;
                    board.Append("- ").Append(mate.Name).Append(" (").Append(HerComputer.Short(mate.Part, 90)).Append("): ");
                    if (mate.State == "done") board.Append("done. Found: ").Append(HerComputer.Short(mate.Answer, BoardChars));
                    else if (mate.State == "working") board.Append(mate.Doing.Length > 0 ? HerComputer.Short(mate.Doing, 90) : "just started");
                    else board.Append(mate.State);
                    board.Append('\n');
                }
            return board.Length == 0 ? "" : "\nThe squad board:\n" + board;
        }

        internal Func<string, string, string> AskFor(SquadMate mate, Func<string, string, string> ask)
        {
            return delegate(string system, string user)
            {
                Interlocked.Increment(ref mate.Asked);
                return ask(MateSystem(mate), user + Board(mate));
            };
        }

        internal void Doing(SquadMate mate, string text) { lock (gate) mate.Doing = text ?? ""; }

        internal string Work(HerComputer computer, Func<string, string, string> cheap, Func<string, string, string> lead, Func<SquadMate, HerBrowser> browser, Func<SquadMate, HerHands> hands, Action<SquadMate> finished, out string notePath)
        {
            notePath = null;
            List<Thread> threads = new List<Thread>();
            List<SquadMate> mates;
            lock (gate) mates = new List<SquadMate>(Mates);
            foreach (SquadMate each in mates)
            {
                SquadMate mate = each;
                lock (gate) mate.State = "working";
                Thread thread = new Thread(delegate()
                {
                    string answer = null, note = null, state = "done";
                    try { answer = computer.Run(mate.Part, hands(mate), AskFor(mate, cheap), delegate { return browser(mate); }, delegate { return stopped; }, out note, Steps, false, Sites); }
                    catch (Exception error)
                    {
                        state = "stuck";
                        answer = "I hit a snag: " + HerComputer.Short(error.Message, 160);
                        Brain.Log("squad", -1, mate.Name + ": " + error.Message);
                    }
                    if (answer == null) { state = "stopped"; answer = ""; }
                    lock (gate) { mate.Answer = answer; mate.Note = note; mate.State = state; }
                    if (finished != null)
                        try { finished(mate); }
                        catch (Exception) { }
                }) { IsBackground = true, Name = "squad " + mate.Number };
                threads.Add(thread);
                thread.Start();
            }
            foreach (Thread thread in threads) thread.Join();
            if (stopped) return null;
            string merged = null;
            try { merged = lead(MergeSystem + "\n" + Brain.Safety, MergePrompt()); }
            catch (Exception error) { Brain.Log("squad", -1, "merge failed: " + error.Message); }
            if (stopped) return null;
            if (string.IsNullOrWhiteSpace(merged)) merged = Plain();
            merged = merged.Trim();
            try { notePath = computer.WriteNote("Squad - " + HerComputer.Short(Job, 40), NoteText(merged)); }
            catch (Exception error) { Brain.Log("squad", -1, "note failed: " + error.Message); }
            return merged;
        }

        internal string MergePrompt()
        {
            StringBuilder prompt = new StringBuilder("The user's job: ").Append(Job).Append("\nWhat each copy found:\n");
            lock (gate)
                foreach (SquadMate mate in Mates)
                    prompt.Append(mate.Number).Append(". ").Append(mate.Name).Append(" (").Append(HerComputer.Short(mate.Part, 160)).Append("): ").Append(string.IsNullOrWhiteSpace(mate.Answer) ? "nothing came back" : HerComputer.Short(mate.Answer, 900)).Append('\n');
            prompt.Append("Write the one answer for the user.");
            return prompt.ToString();
        }

        private string Plain()
        {
            StringBuilder plain = new StringBuilder();
            lock (gate)
                foreach (SquadMate mate in Mates)
                    if (!string.IsNullOrWhiteSpace(mate.Answer)) plain.Append(mate.Name).Append(": ").Append(mate.Answer.Trim()).Append("\n\n");
            return plain.Length == 0 ? "My squad came back empty handed on that one." : plain.ToString();
        }

        internal string NoteText(string merged)
        {
            StringBuilder note = new StringBuilder(merged).Append("\n\nThe squad:\n");
            lock (gate)
                foreach (SquadMate mate in Mates)
                    note.Append("- ").Append(mate.Name).Append(": ").Append(mate.Part).Append('\n');
            lock (Sites)
                if (Sites.Count > 0)
                {
                    note.Append("\nSites we used:\n");
                    foreach (string site in Sites) note.Append("- ").Append(site).Append('\n');
                }
            return note.ToString();
        }

        internal string ChatPrompt()
        {
            StringBuilder who = new StringBuilder();
            lock (gate)
                foreach (SquadMate mate in Mates)
                {
                    who.Append(mate.Number).Append(" (").Append(mate.Name).Append(", on ").Append(HerComputer.Short(mate.Part, 80)).Append("): ");
                    if (mate.State == "done") who.Append("finished, found ").Append(HerComputer.Short(mate.Answer, 140));
                    else who.Append(mate.Doing.Length > 0 ? HerComputer.Short(mate.Doing, 80) : "getting started");
                    if (mate.Persona != null) who.Append(" [she's a friend, not a copy: ").Append(HerComputer.Short(mate.Persona, 160)).Append(']');
                    who.Append('\n');
                }
            return string.Format(ChatAsk, Mates.Count, HerComputer.Short(Job, 200), who) + (LastChat == null ? "" : "\nLast thing said: " + LastChat);
        }

        internal static List<PointF> Stations(int count, double left, double right, double y, double herX)
        {
            List<double> slots = new List<double>();
            double gap = (right - left) / (count + 2);
            for (int i = 1; i <= count + 1; i++) slots.Add(left + gap * i);
            double nearest = slots[0];
            foreach (double slot in slots) if (Math.Abs(slot - herX) < Math.Abs(nearest - herX)) nearest = slot;
            slots.Remove(nearest);
            List<PointF> spots = new List<PointF>();
            for (int i = 0; i < count; i++) spots.Add(new PointF((float)slots[i], (float)y));
            return spots;
        }

        internal static string Check()
        {
            int count;
            string[,] heard =
            {
                { "squad up and find me the best budget gaming mouse", "find me the best budget gaming mouse", "0" },
                { "Capri, squad up: plan a weekend in Tokyo", "plan a weekend in Tokyo", "0" },
                { "can you get your squad to compare iPhone and Pixel cameras?", "compare iPhone and Pixel cameras", "0" },
                { "research the best pizza places near Bondi as a squad", "research the best pizza places near Bondi", "0" },
                { "find cheap flights to Bali with your squad please", "find cheap flights to Bali", "0" },
                { "squad up with three of you and look up the new Zelda reviews", "look up the new Zelda reviews", "3" },
                { "split into a squad and find me a cheap flight to Bali", "find me a cheap flight to Bali", "0" },
                { "make a squad to find the best ramen in Sydney", "find the best ramen in Sydney", "0" },
                { "send a squad to look for a hotel with 4 stars in Rome", "look for a hotel with 4 stars in Rome", "0" },
                { "squad up and find a hotel with 4 stars", "find a hotel with 4 stars", "0" },
                { "squad up and look for 3 tickets to the footy", "look for 3 tickets to the footy", "0" },
                { "split up into 3 and find a hotel with 4 stars", "find a hotel with 4 stars", "3" },
                { "split into 2, compare the new iPads", "compare the new iPads", "2" },
                { "hey capri, could you split into a squad of 2 and research electric cars", "research electric cars", "2" },
                { "make a squad of three to plan a road trip from Sydney to Melbourne", "plan a road trip from Sydney to Melbourne", "3" },
                { "send out a squad of 6 to compare phone plans", "compare phone plans", "3" },
                { "have your squad find me a birthday present for my mum", "find me a birthday present for my mum", "0" },
                { "tell your squad to check the reviews for the Sony headphones", "check the reviews for the Sony headphones", "0" },
                { "find a hotel with 4 stars in Paris as a squad of 2", "find a hotel with 4 stars in Paris", "2" },
                { "Squad up! Help me pick a laptop for uni.", "Help me pick a laptop for uni", "0" },
                { "split into a squad", "", "0" },
                { "send a squad", "", "0" },
                { "split into 3", "", "3" },
                { "squad up", "", "0" }
            };
            for (int i = 0; i < heard.GetLength(0); i++)
                if (JobIn(heard[i, 0], out count) != heard[i, 1] || count.ToString() != heard[i, 2]) return "Her squad heard \"" + heard[i, 0] + "\" as \"" + JobIn(heard[i, 0], out count) + "\" (" + count + ").";
            string[] chatter =
            {
                "I'm playing valorant with the squad", "squad, let's go", "let's split up and find the ghost room", "my squad won't stop dying", "find a hotel with 4 stars",
                "split up and find two recipes for dinner", "squad, find the ghost room", "squad up in fortnite", "squad up and win this one", "squad up, fortnite time",
                "let's squad up and find the ghost room", "send a squad to B site", "send the squad in", "get a squad together for ranked", "start a squad in fortnite",
                "I need a squad for ranked", "join my squad", "we got squad wiped again", "the squad wants to play apex", "good luck with your squad",
                "play valorant with your squad", "my team won't stop feeding", "my team is carrying me", "my team needs to find the ghost room", "let's team up in fortnite",
                "let's split the bill", "can we split the bill 3 ways", "split up the bill for me", "split the pizza into 4", "split into two teams",
                "buy 3 tickets for the concert", "my squad is so good at fortnite", "squad goals", "what's a squad", "find me the best mouse", "use your computer to find a recipe"
            };
            foreach (string not in chatter)
                if (JobIn(not, out count) != null || count != 0) return "Her squad took a line that wasn't a squad job: " + not;
            foreach (string stop in new[] { "stop the squad", "call the squad back", "call off your squad", "call back the squad", "Capri, stop the squad please", "cancel the squad now", "send your squad home" })
                if (!StopAsked(stop)) return "Her squad didn't hear being called back: " + stop;
            foreach (string not in new[] { "my squad won't stop dying", "stop it", "squad up and find a mouse", "stop the squad from dying", "squad, come back", "I can't stop playing with the squad", "the squad needs to stop", "my team won't stop feeding" })
                if (StopAsked(not)) return "Her squad was called back by a line that wasn't for it: " + not;
            List<SquadMate> plan = Plan("Sure!\n1. deal hunter | find mice under $50\n2) **review nerd** | read reviews of the top picks\n- 3: spec checker|compare weights and sensors\n4. extra | too many", 0);
            if (plan.Count != 3 || plan[0].Name != "deal hunter" || plan[1].Name != "review nerd" || plan[2].Part != "compare weights and sensors" || plan[2].Number != 3) return "Her squad misread its plan.";
            plan = Plan("1. find mice\n2. read reviews\n3. compare", 2);
            if (plan.Count != 2 || plan[0].Name != "capri 2" || plan[1].Name != "capri 3") return "Her squad didn't cap or name its copies.";
            if (Plan("I can't split that", 0).Count != 0 || PlanPrompt(2).IndexOf("into two parts", StringComparison.Ordinal) < 0) return "Her squad made a plan out of nothing, or asked for the wrong number of parts.";
            Squad squad = new Squad("find a mouse");
            squad.Mates.AddRange(Plan("1. deal hunter | cheap mice\n2. review nerd | reviews", 0));
            squad.Mates[0].State = "done";
            squad.Mates[0].Answer = "Logitech G305 for $29";
            squad.Mates[1].State = "working";
            squad.Doing(squad.Mates[1], "Searching for mouse reviews");
            string board = squad.Board(squad.Mates[1]);
            if (board.IndexOf("Logitech G305", StringComparison.Ordinal) < 0 || board.IndexOf("review nerd", StringComparison.Ordinal) >= 0) return "Her squad's board left out a find or showed a copy her own line.";
            if (squad.Board(squad.Mates[0]).IndexOf("Searching for mouse reviews", StringComparison.Ordinal) < 0) return "Her squad's board didn't say what the others are doing.";
            string system = squad.MateSystem(squad.Mates[1]);
            if (system.IndexOf("reviews", StringComparison.Ordinal) < 0 || system.IndexOf("DONE <your answer", StringComparison.Ordinal) < 0 || system.IndexOf("review nerd", StringComparison.Ordinal) < 0) return "Her squad copy's prompt lost her part or her actions.";
            squad.Mates[1].Persona = "You are Mochi, a sleepy cat girl.";
            system = squad.MateSystem(squad.Mates[1]);
            if (!system.StartsWith("You are Mochi", StringComparison.Ordinal) || system.IndexOf("You are Capri", StringComparison.Ordinal) >= 0 || system.IndexOf("Never type passwords", StringComparison.Ordinal) < 0) return "A friend in her squad lost her own personality or the browser rules.";
            if (system.IndexOf(Brain.Safety, StringComparison.Ordinal) < 0 || squad.MateSystem(squad.Mates[0]).IndexOf(Brain.Safety, StringComparison.Ordinal) < 0 || ChatSystem.IndexOf(Brain.Safety, StringComparison.Ordinal) < 0) return "Her squad's copies or their chatter worked without her safety lines.";
            if (system.IndexOf("NOTE <", StringComparison.Ordinal) >= 0 || squad.MateSystem(squad.Mates[0]).IndexOf("NOTE <", StringComparison.Ordinal) >= 0) return "Her squad's copies were told to save notes of their own.";
            string chat = squad.ChatPrompt();
            if (chat.IndexOf("Logitech G305", StringComparison.Ordinal) < 0 || chat.IndexOf("Searching for mouse reviews", StringComparison.Ordinal) < 0 || chat.IndexOf("numbers 0 to 2", StringComparison.Ordinal) < 0) return "Her squad's chatter wasn't about what they're really doing.";
            List<PointF> spots = Stations(3, 0, 1000, 700, 140);
            if (spots.Count != 3 || spots.Exists(spot => Math.Abs(spot.X - 200) < 1) || spots[0].X < 300) return "Her squad stood on her spot.";
            return null;
        }

        internal static string CheckRun(string folder)
        {
            HerComputer computer = new HerComputer(folder);
            Squad squad = new Squad("find me a mouse and a keyboard");
            int unguarded = 0;
            Func<string, string, string> lead = delegate(string system, string user)
            {
                if (system.IndexOf(Brain.Safety, StringComparison.Ordinal) < 0) Interlocked.Increment(ref unguarded);
                if (system.IndexOf("Split the user's job", StringComparison.Ordinal) >= 0) return "1. mouse scout | find a mouse\n2. keyboard scout | find a keyboard\n3. deal hunter | find a bundle deal";
                return "Got you a mouse, a keyboard and a bundle." + (user.IndexOf("G305", StringComparison.Ordinal) >= 0 && user.IndexOf("K380", StringComparison.Ordinal) >= 0 ? " G305 and K380." : "");
            };
            if (!squad.Make(lead, 0) || squad.Mates.Count != 3) return "Her squad didn't split the job three ways.";
            int started = 0;
            List<string> boards = new List<string>();
            Func<string, string, string> cheap = delegate(string system, string user)
            {
                if (system.IndexOf(Brain.Safety, StringComparison.Ordinal) < 0) Interlocked.Increment(ref unguarded);
                Interlocked.Increment(ref started);
                DateTime until = DateTime.UtcNow.AddSeconds(5);
                while (Volatile.Read(ref started) < 3 && DateTime.UtcNow < until) Thread.Sleep(10);
                lock (boards) boards.Add(user);
                if (system.IndexOf("find a mouse", StringComparison.Ordinal) >= 0) return "DONE the Logitech G305";
                if (system.IndexOf("find a keyboard", StringComparison.Ordinal) >= 0)
                {
                    if (user.IndexOf("NOTE", StringComparison.Ordinal) < 0) return "NOTE keyboards | the Logitech K380 is the one";
                    Thread.Sleep(200);
                    return "DONE the Logitech K380";
                }
                return "hmm";
            };
            int done = 0;
            QuietSquadHands quiet = new QuietSquadHands();
            string note;
            string answer = squad.Work(computer, cheap, lead, delegate { return null; }, delegate { return quiet; }, delegate { Interlocked.Increment(ref done); }, out note);
            if (Volatile.Read(ref started) < 3 || boards.Count < 3) return "Her squad's copies didn't work at the same time.";
            if (answer != "Got you a mouse, a keyboard and a bundle. G305 and K380." || done != 3) return "Her squad didn't put its finds together: " + answer;
            if (Volatile.Read(ref unguarded) != 0) return "Her squad planned, worked or answered without her safety lines.";
            if (squad.Mates[2].Asked != 3 || squad.Mates[2].State != "done") return "A copy in her squad kept going past bad replies.";
            if (note == null || !File.Exists(note) || HerComputer.Read(note, 4000).IndexOf("keyboard scout", StringComparison.Ordinal) < 0) return "Her squad didn't save its note.";
            if (computer.Notes().Count != 1) return "Her squad's copies each saved a note of their own.";
            Squad stopped = new Squad("keep going");
            stopped.Make(delegate { return "1. a | one\n2. b | two"; }, 0);
            int asks = 0;
            Thread stopper = new Thread(delegate() { Thread.Sleep(150); stopped.Stop(); }) { IsBackground = true };
            stopper.Start();
            answer = stopped.Work(computer, delegate { Interlocked.Increment(ref asks); Thread.Sleep(40); return "SCROLL down"; }, lead, delegate { return null; }, delegate { return quiet; }, null, out note);
            if (answer != null || note != null || asks > 2 * Steps) return "Her squad kept working after it was called back.";
            if (stopped.Mates.Exists(mate => mate.State != "stopped")) return "Her squad's copies didn't all stop.";
            return null;
        }

        private sealed class QuietSquadHands : HerHands
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
