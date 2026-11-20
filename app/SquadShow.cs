using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private Squad squad;
        private CloneWindow squadWindow;
        private readonly Dictionary<int, CloneBody> squadBodies = new Dictionary<int, CloneBody>();
        private readonly Dictionary<int, HerBrowser> squadWebs = new Dictionary<int, HerBrowser>();
        private int squadTurn;
        private double squadChatAt;
        private bool squadChatting, squadDone;
        internal Func<string, string, string> SquadBrain;
        internal Func<SquadMate, HerBrowser> SquadBrowserMaker;

        private string SquadCheap(string system, string user) { return brain.Aside(system, user, 500, true); }

        private string SquadLead(string system, string user) { return brain.Aside(system, user, 900, true); }

        internal bool SquadOut { get { return squad != null; } }

        private string SquadAsk(string input)
        {
            if (input != null && herHandOffAsk != null && input.Trim() == herHandOffAsk) return null;
            if (Squad.StopAsked(input))
            {
                if (squad == null && squadWindow == null) return null;
                SquadOff();
                return "Called my squad back.";
            }
            int count;
            string job = Squad.JobIn(input, out count);
            if (job == null) return null;
            if (job.Length == 0) return squad != null ? "My squad's on \"" + HerComputer.Short(squad.Job, 60) + "\" right now." : "Squad's ready. What's the job?";
            if (squad != null) return "My squad's still on \"" + HerComputer.Short(squad.Job, 60) + "\". Say stop the squad if you want them on something else.";
            if (SquadBrain == null && (brain.Model == null || Brain.Off)) return "I need a brain to run a squad. Pick one in Mainframe under BRAIN.";
            StartSquad(job, count);
            return "Splitting up for that. Hold Esc to call them back.";
        }

        private void StartSquad(string job, int count)
        {
            Squad started = new Squad(job);
            squad = started;
            squadDone = false;
            int turn = ++squadTurn;
            Func<string, string, string> cheap = SquadBrain ?? SquadCheap, lead = SquadBrain ?? SquadLead;
            string folder = HerComputerFolder;
            if (!testing) Animate("glitch");
            Brain.Log("squad", -1, "started: " + HerComputer.Short(job, 120));
            ThreadPool.QueueUserWorkItem(delegate
            {
                if (!started.Make(lead, count)) return;
                Post(delegate { if (turn == squadTurn) SquadSpread(started); });
                string answer = null, note;
                try { answer = started.Work(new HerComputer(folder), cheap, lead, SquadBrowser, delegate(SquadMate mate) { return new SquadHands(this, started, mate); }, delegate(SquadMate mate) { Post(delegate { SquadMateDone(started, mate); }); }, out note); }
                catch (Exception error) { Brain.Log("squad", -1, "failed: " + error.Message); }
                Post(delegate { SquadFinished(started, answer, turn); });
            });
        }

        private HerBrowser SquadBrowser(SquadMate mate)
        {
            Func<SquadMate, HerBrowser> maker = SquadBrowserMaker;
            if (maker != null) return maker(mate);
            HerBrowser web = new HerBrowser(Path.Combine(HerComputerFolder, "squad", "browser-" + mate.Number), HerComputer.Page.Width, HerComputer.Page.Height);
            string problem;
            if (!web.Start(out problem))
            {
                web.Dispose();
                Brain.Log("squad", -1, mate.Name + ": " + problem);
                return null;
            }
            HerBrowser old;
            lock (squadWebs)
            {
                if (squadWebs.TryGetValue(mate.Number, out old) && old != web) old.Dispose();
                squadWebs[mate.Number] = web;
            }
            return web;
        }

        private HerBrowser SquadWeb(int number)
        {
            HerBrowser web;
            lock (squadWebs) return squadWebs.TryGetValue(number, out web) ? web : null;
        }

        private void SquadBrowsersOff()
        {
            List<HerBrowser> closing;
            lock (squadWebs) { closing = new List<HerBrowser>(squadWebs.Values); squadWebs.Clear(); }
            if (closing.Count == 0) return;
            ThreadPool.QueueUserWorkItem(delegate { foreach (HerBrowser web in closing) try { web.Dispose(); } catch (Exception) { } });
        }

        private Point SquadHome { get { return new Point(anchor.X + Width / 2, anchor.Y + Height / 2); } }

        private void SquadSpread(Squad started)
        {
            if (started != squad || IsDisposed || quitting) return;
            double now = clock.Elapsed.TotalSeconds;
            Point home = SquadHome;
            if (squadWindow == null)
            {
                Screen screen = Screen.FromPoint(home);
                CloneWindow opened = new CloneWindow(screen.Bounds, clock, idle, walk, grin, glitch, preferences.Size, Width, random.Next());
                opened.Swarm.Watching = true;
                opened.Failed = SquadOff;
                opened.Cleared = SquadCleared;
                opened.Ticked = delegate(double tick) { if (squadWindow == opened) SquadTick(tick); };
                squadWindow = opened;
                squadBodies.Clear();
                opened.Open(now);
            }
            Rectangle room = squadWindow.Bounds, work = Screen.FromPoint(home).WorkingArea;
            double y = Math.Max(work.Top - room.Top + CloneWindow.LaptopHeight, Math.Min(work.Bottom - room.Top - preferences.Size * 0.55 - 30, home.Y - room.Y));
            List<PointF> spots = Squad.Stations(started.Mates.Count, work.Left - room.Left, work.Right - room.Left, y, home.X - room.X);
            for (int i = 0; i < started.Mates.Count; i++)
            {
                SquadMate mate = started.Mates[i];
                string key = "squad-" + squadTurn + "-" + mate.Number;
                squadWindow.Agent(key, true, home);
                CloneBody body = squadWindow.Swarm.Bodies.Find(delegate(CloneBody item) { return item.Agent == key; });
                if (body == null) continue;
                body.Posted = true;
                body.ToX = spots[i].X;
                body.ToY = spots[i].Y;
                body.FaceX = home.X - room.X;
                body.Tag = mate.Name;
                body.Ink = i;
                body.Screen = SquadBlank();
                body.Caption = HerComputer.Short(mate.Part, 34);
                squadBodies[mate.Number] = body;
            }
            squadChatAt = now + 6;
        }

        private static Bitmap SquadBlank()
        {
            Bitmap blank = new Bitmap(CloneWindow.LaptopWidth, CloneWindow.LaptopHeight, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(blank)) graphics.Clear(Color.FromArgb(255, 30, 26, 40));
            return blank;
        }

        private static Bitmap SquadPicture(byte[] jpeg)
        {
            if (jpeg == null || jpeg.Length == 0) return null;
            try
            {
                using (MemoryStream stream = new MemoryStream(jpeg))
                using (Image shot = Image.FromStream(stream))
                {
                    Bitmap small = new Bitmap(CloneWindow.LaptopWidth, CloneWindow.LaptopHeight, PixelFormat.Format32bppPArgb);
                    using (Graphics graphics = Graphics.FromImage(small))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.DrawImage(shot, 0, 0, CloneWindow.LaptopWidth, CloneWindow.LaptopHeight);
                    }
                    return small;
                }
            }
            catch (Exception) { return null; }
        }

        private void SquadShows(Squad from, SquadMate mate, string caption, Bitmap picture)
        {
            CloneBody body;
            if (from != squad || squadWindow == null || !squadBodies.TryGetValue(mate.Number, out body) || !squadWindow.Swarm.Bodies.Contains(body))
            {
                if (picture != null) picture.Dispose();
                return;
            }
            if (caption != null) body.Caption = HerComputer.Short(caption, 34);
            if (picture == null) return;
            if (body.Screen != null) body.Screen.Dispose();
            body.Screen = picture;
        }

        private void SquadMateDone(Squad from, SquadMate mate)
        {
            CloneBody body;
            if (from != squad || squadWindow == null || !squadBodies.TryGetValue(mate.Number, out body) || !squadWindow.Swarm.Bodies.Contains(body)) return;
            double now = clock.Elapsed.TotalSeconds;
            Rectangle room = squadWindow.Bounds;
            Point home = SquadHome;
            body.Caption = mate.State == "done" ? "done" : mate.State;
            string found = HerComputerWindow.FirstLine(mate.Answer);
            if (mate.State == "done" && found.Length > 0)
            {
                body.Carrying = new CloneNote { X = body.X, Y = body.Y, Text = found };
                squadWindow.Swarm.Notes.Add(body.Carrying);
            }
            body.Posted = false;
            body.Returning = true;
            body.RestUntil = now + 1.6;
            body.ToX = home.X - room.X;
            body.ToY = home.Y - room.Y;
        }

        private void SquadFinished(Squad from, string answer, int turn)
        {
            if (from != squad || turn != squadTurn) return;
            squad = null;
            squadDone = true;
            SquadBrowsersOff();
            if (squadWindow != null && squadWindow.Swarm.Bodies.Count == 0) SquadWindowOff();
            if (answer == null || IsDisposed || quitting) return;
            Brain.Log("squad", -1, "done: " + HerComputer.Short(from.Job, 120));
            if (!testing) Animate("grin");
            SayOwn("squad", HerComputer.Short(answer, 900));
            RemoteLink link = remoteLink;
            if (link == null || testing || !RemotePcAway()) return;
            string title = "Squad done: " + HerComputer.Short(from.Job, 60), body = HerComputerWindow.FirstLine(answer);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { link.Notify("done", title, body, null, null, null); }
                catch (Exception error) { Brain.Log("squad", -1, "phone: " + error.Message); }
            });
        }

        private void SquadCleared()
        {
            bool had = squad != null;
            SquadOff();
            if (had && !testing && !hidden) SayOwn("squad", "Called my squad back.");
        }

        internal void SquadOff()
        {
            squadTurn++;
            squadChatting = false;
            Squad stopping = squad;
            squad = null;
            squadDone = true;
            if (stopping != null) stopping.Stop();
            SquadBrowsersOff();
            SquadWindowOff();
        }

        private void SquadWindowOff()
        {
            CloneWindow closing = squadWindow;
            squadWindow = null;
            squadBodies.Clear();
            if (closing != null) closing.Close();
        }

        private void SquadTick(double now)
        {
            if (squadWindow == null) return;
            if (squadDone && squadWindow.Swarm.Bodies.Count == 0) { SquadWindowOff(); return; }
            Squad current = squad;
            if (current == null || squadChatting || now < squadChatAt || current.Chats >= Squad.MostChats || !current.Working) return;
            if (testing || hidden || Hushed || working != null || speech != null || talkState != TalkState.Off || (chatting && !local)) return;
            squadChatting = true;
            current.Chats++;
            int turn = squadTurn;
            string ask = current.ChatPrompt();
            Func<string, string, string> cheap = SquadBrain ?? SquadCheap;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said = null;
                try { said = cheap(Brain.Character, ask); }
                catch (Exception problem) { Brain.Log("squad", -1, problem.Message); }
                Post(delegate
                {
                    if (turn != squadTurn) return;
                    List<KeyValuePair<int, string>> lines = CloneScript(said);
                    if (lines.Count > 0) current.LastChat = lines[lines.Count - 1].Value;
                    SquadLine(lines, 0, turn);
                });
            });
        }

        private void SquadLater(int wait, List<KeyValuePair<int, string>> lines, int at, int turn)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                Thread.Sleep(wait);
                Post(delegate { SquadLine(lines, at, turn); });
            });
        }

        private void SquadLine(List<KeyValuePair<int, string>> lines, int at, int turn)
        {
            if (turn != squadTurn || squadWindow == null) return;
            if (working != null || speech != null || LineBusy || talkState != TalkState.Off) { SquadLater(400, lines, at, turn); return; }
            if (at >= lines.Count)
            {
                squadChatting = false;
                squadChatAt = clock.Elapsed.TotalSeconds + 10 + random.NextDouble() * 6;
                return;
            }
            string line = lines[at].Value;
            int who = lines[at].Key;
            CloneBody body = null;
            if (who > 0 && (!squadBodies.TryGetValue(who, out body) || !squadWindow.Swarm.Bodies.Contains(body))) { SquadLine(lines, at + 1, turn); return; }
            if (body == null)
            {
                SayOwn("squad", line);
                SquadLater(1200 + line.Length * 50, lines, at + 1, turn);
                return;
            }
            Func<string, byte[]> free = null;
            Func<string, byte[]> speak = preferences.Silent || Hushed || (chatting && !local) ? null : PickVoice(false, out free, false);
            string words = Voice.Spoken(line);
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string trouble = null;
                if (speak != null) wav = Voiced(speak, free, words, out trouble, false);
                if (trouble != null) Brain.Log("squad", -1, trouble);
                Action play = null;
                play = delegate
                {
                    if (turn != squadTurn || squadWindow == null) return;
                    if (LineBusy) { ownLines.Enqueue(play); return; }
                    if (preferences.Silent || Hushed) wav = null;
                    double now = clock.Elapsed.TotalSeconds;
                    double seconds = wav != null ? Voice.Seconds(wav) : 1 + line.Length * 0.06;
                    if (squadWindow.Swarm.Bodies.Contains(body)) squadWindow.Say(body, line, now + seconds + 1);
                    if (wav != null)
                    {
                        voiceUntil = now + seconds + 0.6;
                        CheckEars(now);
                        Played(wav);
                        PlayVoice(wav);
                    }
                    SquadLater((int)(seconds * 1000) + 300, lines, at + 1, turn);
                };
                Post(play);
            });
        }

        private sealed class SquadHands : HerHands
        {
            private readonly Companion her;
            private readonly Squad squad;
            private readonly SquadMate mate;
            private DateTime shotAt = DateTime.MinValue;

            internal SquadHands(Companion owner, Squad from, SquadMate who) { her = owner; squad = from; mate = who; }

            public void Status(string text)
            {
                squad.Doing(mate, text);
                Bitmap picture = null;
                HerBrowser web = her.SquadWeb(mate.Number);
                if (web != null && web.Running && !squad.Stopped && (DateTime.UtcNow - shotAt).TotalSeconds >= 1)
                {
                    shotAt = DateTime.UtcNow;
                    try { picture = SquadPicture(web.Frame(55)); }
                    catch (Exception) { picture = null; }
                }
                her.Post(delegate { her.SquadShows(squad, mate, text, picture); });
            }

            public void Show(string app) { }
            public void Point(string spot) { }
            public void PointPage(Rectangle box) { }
            public void Click() { }
            public void Typing(string field, string text) { }
            public void Bubble(string line) { }
            public void Changed() { }
        }

        internal string SquadCheck()
        {
            Func<string, string, string> brainWas = SquadBrain;
            try
            {
                SquadBrain = null;
                if (SquadAsk("what's your favourite colour") != null || SquadAsk("stop the squad") != null) return "Her squad answered a line that wasn't for it.";
                if (SquadAsk("squad up") != "Squad's ready. What's the job?") return "Her squad didn't ask what the job was.";
                herHandOffAsk = "load up fortnite and play it. You just researched the pirate token on your own computer and found: it's in the shop, so go grab it with your squad.";
                string handedOff = SquadAsk(herHandOffAsk);
                herHandOffAsk = null;
                if (handedOff != null) return "Her squad took what her own computer found as a job from them.";
                Squad shown = new Squad("find a mouse and a keyboard");
                shown.Mates.AddRange(Squad.Plan("1. mouse scout | find a mouse\n2. keyboard scout | find a keyboard\n3. deal hunter | find a deal", 0));
                squad = shown;
                squadDone = false;
                SquadSpread(shown);
                if (squadWindow == null || !squadWindow.Swarm.Watching || squadWindow.Swarm.Bodies.Count != 3 || squadBodies.Count != 3) return "Her squad didn't step out of her.";
                if (squadBodies[2].Tag != "keyboard scout" || !squadBodies[2].Posted || squadBodies[2].Screen == null) return "A copy in her squad came out without her name or laptop.";
                using (Bitmap page = new Bitmap(640, 300))
                using (MemoryStream jpeg = new MemoryStream())
                {
                    using (Graphics paint = Graphics.FromImage(page)) paint.Clear(Color.White);
                    page.Save(jpeg, ImageFormat.Jpeg);
                    Bitmap small = SquadPicture(jpeg.ToArray());
                    if (small == null || small.Width != CloneWindow.LaptopWidth) return "Her squad's laptops couldn't show a page.";
                    SquadShows(shown, shown.Mates[0], "Searching for gaming mice", small);
                }
                if (squadBodies[1].Caption != "Searching for gaming mice") return "A copy's laptop didn't say what she's doing.";
                if (SquadAsk("squad up and find a monitor").IndexOf("still on", StringComparison.Ordinal) < 0) return "Her squad took a second job while the first was out.";
                double now = clock.Elapsed.TotalSeconds;
                for (int i = 0; i < 160; i++) squadWindow.Advance(now += 0.1);
                CloneBody first = squadBodies[1];
                if (Math.Abs(first.X - first.ToX) > 1 || Math.Abs(first.Y - first.ToY) > 1) return "A copy in her squad didn't walk to her spot.";
                for (int i = 0; i < 40; i++) squadWindow.Advance(now += 0.1);
                if (Math.Abs(first.X - first.ToX) > 1 || !first.Posted) return "A copy in her squad wandered off her spot.";
                shown.Mates[0].State = "done";
                shown.Mates[0].Answer = "The Logitech G305 is $29 and great.";
                SquadMateDone(shown, shown.Mates[0]);
                if (!first.Returning || first.Carrying == null || squadWindow.Swarm.Notes.Count != 1) return "A copy in her squad didn't bring her find back.";
                for (int i = 0; i < 80; i++) squadWindow.Advance(now += 0.1);
                if (squadWindow.Swarm.Bodies.Contains(first) || squadWindow.Swarm.Notes.Count != 0 || first.Screen != null) return "A copy in her squad didn't merge back into her, or left her note behind.";
                if (SquadAsk("stop the squad") != "Called my squad back." || squadWindow != null || squad != null || !shown.Stopped) return "Her squad didn't stop when called back.";
                return null;
            }
            finally { SquadBrain = brainWas; SquadOff(); }
        }
    }
}
