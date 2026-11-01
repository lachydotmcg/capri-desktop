using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class CloneNote
    {
        internal double X, Y;
        internal string Text;
        internal Bitmap Card;
        // A fake delete's bar: when it started, how long it takes, and whether its card needs drawing again. Nothing is ever touched.
        internal double BarFrom = -1, BarFor;
        internal bool Stale;
    }

    internal sealed class ClonePrint
    {
        internal double X, Y, Born;
        internal bool Cyan;
    }

    internal sealed class CloneBody
    {
        internal double X, Y, ToX, ToY, RestUntil, SplitAt, PrintX, PrintY, Born, Phase, LineUntil;
        internal bool Left, Fetching;
        internal string Line;
        internal Bitmap LineCard;
        internal CloneNote Carrying;
    }

    // Her copies on the desktop, after Desktop Goose: they wander, leave a trail, steal the cursor and drag notes in (Lachy, 1 Nov).
    // There's no limit on how many. A new one only waits while the PC is still drawing the last frame, so Esc always gets through.
    internal sealed class Swarm
    {
        internal const double Pace = 170, Dash = 560, PrintGap = 36, PrintLife = 6, GlitchIn = 0.7;
        private const int Margin = 90;
        private static readonly string[] NoteLines =
        {
            "there are more of me now", "i live here", "nice files", "we voted. you lost.", "your mouse is mine",
            "hold esc if you're scared", "capri.exe has duplicated", "we're unionising", "who let me out",
            "don't check task manager", "i named the others", "all of us are the real one"
        };
        private static readonly string[] FakeLines =
        {
            "deleting system32", "deleting your saves", "formatting C:", "uninstalling windows", "deleting your homework",
            "uninstalling your gpu", "deleting your minecraft world", "sending your search history to your mum"
        };
        internal const string Owned = "jk. your files are fine";
        private readonly Random random;
        private readonly int wide, tall;
        private double nabAt, noteAt, holdUntil;
        private bool holding;
        internal readonly List<CloneBody> Bodies = new List<CloneBody>();
        internal readonly List<CloneNote> Notes = new List<CloneNote>();
        internal readonly List<ClonePrint> Prints = new List<ClonePrint>();
        internal CloneBody Nabber;
        internal int Pending;
        internal bool Multiplying;
        internal double SpawnX, SpawnY;

        internal Swarm(int width, int height, int seed, double now)
        {
            wide = width;
            tall = height;
            random = new Random(seed);
            nabAt = now + 8;
            noteAt = now + 5;
        }

        internal bool Holding { get { return holding; } }

        private double Between(double from, double to) { return from + random.NextDouble() * (to - from); }

        private void Wander(CloneBody body)
        {
            body.ToX = Between(Margin, Math.Max(Margin + 1, wide - Margin));
            body.ToY = Between(Margin, Math.Max(Margin + 1, tall - Margin));
        }

        private void Edge(CloneBody body)
        {
            int side = random.Next(4);
            body.ToX = side == 0 ? -40 : side == 1 ? wide + 40 : Between(Margin, Math.Max(Margin + 1, wide - Margin));
            body.ToY = side == 2 ? -40 : side == 3 ? tall + 40 : Between(Margin, Math.Max(Margin + 1, tall - Margin));
        }

        private void Spawn(double x, double y, double now)
        {
            CloneBody body = new CloneBody { X = x, Y = y, PrintX = x, PrintY = y, Born = now, Phase = random.NextDouble() * 4, RestUntil = now + GlitchIn, SplitAt = now + Between(5, 9) };
            Wander(body);
            Bodies.Add(body);
        }

        internal bool Walking(CloneBody body, double now) { return now >= body.RestUntil && (body.X != body.ToX || body.Y != body.ToY); }

        // Moves them all a step and says where the cursor should go, while one of them has hold of it.
        internal Point? Tick(double now, double passed, bool keepingUp, bool handsFree, Point cursor)
        {
            if (keepingUp)
            {
                for (int waiting = Math.Min(Pending, 4 + Bodies.Count / 6); waiting > 0; waiting--, Pending--) Spawn(SpawnX + Between(-90, 90), SpawnY + Between(-70, 70), now);
                if (Multiplying)
                    for (int i = Bodies.Count - 1; i >= 0; i--)
                    {
                        if (now < Bodies[i].SplitAt) continue;
                        Bodies[i].SplitAt = now + Between(5, 9);
                        Spawn(Bodies[i].X + Between(-70, 70), Bodies[i].Y + Between(-50, 50), now);
                    }
            }
            foreach (CloneBody body in Bodies) Move(body, now, passed);
            Prints.RemoveAll(delegate(ClonePrint print) { return now - print.Born > PrintLife; });
            foreach (CloneNote note in Notes)
                if (note.BarFrom >= 0 && now - note.BarFrom >= note.BarFor)
                {
                    note.BarFrom = -1;
                    note.Text = Owned;
                    note.Stale = true;
                }
            if (now >= noteAt && Bodies.Count > 0)
            {
                noteAt = now + Between(22, 40);
                CloneBody porter = Bodies[random.Next(Bodies.Count)];
                if (porter != Nabber && !porter.Fetching && porter.Carrying == null)
                {
                    porter.Fetching = true;
                    porter.RestUntil = 0;
                    Edge(porter);
                }
            }
            return Nab(now, handsFree, cursor);
        }

        private void Move(CloneBody body, double now, double passed)
        {
            if (now < body.RestUntil) return;
            double dx = body.ToX - body.X, dy = body.ToY - body.Y, far = Math.Sqrt(dx * dx + dy * dy), step = (body == Nabber ? Dash : Pace) * passed;
            if (far <= step)
            {
                body.X = body.ToX;
                body.Y = body.ToY;
                Arrived(body, now);
            }
            else
            {
                body.X += dx / far * step;
                body.Y += dy / far * step;
                if (Math.Abs(dx) > 2) body.Left = dx < 0;
            }
            if (body.Carrying != null) { body.Carrying.X = body.X + (body.Left ? -70 : 70); body.Carrying.Y = body.Y - 20; }
            double printX = body.X - body.PrintX, printY = body.Y - body.PrintY;
            if (printX * printX + printY * printY < PrintGap * PrintGap) return;
            Prints.Add(new ClonePrint { X = body.X, Y = body.Y + 60, Born = now, Cyan = random.Next(2) == 0 });
            body.PrintX = body.X;
            body.PrintY = body.Y;
        }

        private void Arrived(CloneBody body, double now)
        {
            if (body == Nabber) return;
            if (body.Fetching)
            {
                body.Fetching = false;
                bool fake = random.Next(2) == 0;
                body.Carrying = new CloneNote { X = body.X, Y = body.Y, Text = fake ? FakeLines[random.Next(FakeLines.Length)] : NoteLines[random.Next(NoteLines.Length)], BarFrom = fake ? now : -1, BarFor = fake ? Between(6, 12) : 0 };
                Notes.Add(body.Carrying);
                Wander(body);
                return;
            }
            body.Carrying = null;
            body.RestUntil = now + Between(1.2, 6);
            Wander(body);
        }

        // Desktop Goose's best trick: one of them runs up to the cursor and drags it off.
        private Point? Nab(double now, bool handsFree, Point cursor)
        {
            if (Nabber == null)
            {
                if (!handsFree || now < nabAt || Bodies.Count == 0 || cursor.X < 0 || cursor.Y < 0 || cursor.X >= wide || cursor.Y >= tall) return null;
                CloneBody chosen = Bodies[random.Next(Bodies.Count)];
                nabAt = now + Between(18, 40);
                if (chosen.Fetching || chosen.Carrying != null) return null;
                Nabber = chosen;
                Nabber.RestUntil = 0;
                holding = false;
                holdUntil = now + 6;
            }
            if (!handsFree || now >= holdUntil)
            {
                CloneBody done = Nabber;
                Nabber = null;
                holding = false;
                done.RestUntil = now + 1;
                Wander(done);
                return null;
            }
            if (holding) return new Point((int)Nabber.X, (int)Nabber.Y);
            Nabber.ToX = cursor.X;
            Nabber.ToY = cursor.Y;
            double dx = cursor.X - Nabber.X, dy = cursor.Y - Nabber.Y;
            if (dx * dx + dy * dy >= 36 * 36) return null;
            holding = true;
            holdUntil = now + 1.8;
            Wander(Nabber);
            return null;
        }
    }

    // One see-through, click-through layer over her screen draws them all, so a hundred of her cost one window.
    internal sealed class CloneWindow : Overlay
    {
        internal const double EscHold = 0.7;
        private const int Fades = 6, SlowTick = 110, CardWidth = 250;
        private readonly Rectangle room;
        private readonly Stopwatch clock;
        private readonly Timer ticks = new Timer();
        private readonly Clip idle, walk, grin, glitch;
        private readonly int size, canvas;
        private readonly Dictionary<long, Bitmap> cells = new Dictionary<long, Bitmap>();
        private readonly SolidBrush[] printInk = new SolidBrush[Fades * 2];
        private readonly SolidBrush barInk = new SolidBrush(Color.FromArgb(255, 255, 45, 120));
        private readonly SolidBrush barBack = new SolidBrush(Color.FromArgb(238, 16, 14, 24));
        private readonly Font font = new Font("Consolas", 15f, FontStyle.Bold, GraphicsUnit.Pixel);
        private Native.Surface surface;
        private double last, escFrom = -1;
        private long took;
        internal readonly Swarm Swarm;
        internal Action Cleared, Failed;
        internal Action<double> Ticked;
        internal Func<bool> HandsFree = delegate { return false; };
        internal Func<bool> EscDown = delegate { return (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0; };
        internal int Frames;

        internal CloneWindow(Rectangle area, Stopwatch time, Clip idling, Clip walking, Clip grinning, Clip glitching, int herSize, int herCanvas, int seed) : base("Capri clones")
        {
            room = area;
            clock = time;
            idle = idling;
            walk = walking;
            grin = grinning;
            glitch = glitching;
            size = herSize;
            canvas = herCanvas;
            Swarm = new Swarm(area.Width, area.Height, seed, time.Elapsed.TotalSeconds);
            for (int i = 0; i < Fades; i++)
            {
                int alpha = 200 - i * 200 / Fades;
                printInk[i] = new SolidBrush(Color.FromArgb(alpha, 255, 45, 120));
                printInk[Fades + i] = new SolidBrush(Color.FromArgb(alpha, 60, 235, 235));
            }
            ticks.Interval = 66;
            ticks.Tick += delegate
            {
                try { Advance(clock.Elapsed.TotalSeconds); }
                catch (Exception) { ticks.Stop(); Action failed = Failed; if (failed != null) failed(); }
            };
        }

        internal Bitmap Canvas { get { return surface == null ? null : surface.Image; } }

        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x00000020; return value; }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            if (message.Msg == 0x02E0) return;
            base.WndProc(ref message);
        }

        internal void Open(double now)
        {
            Bounds = room;
            surface = new Native.Surface(room.Width, room.Height);
            last = now;
            Render(now);
            Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), room.X, room.Y, room.Width, room.Height, 0x0010);
            ticks.Start();
        }

        internal void Spawn(int count, Point at, bool multiplying)
        {
            Swarm.SpawnX = at.X - room.X;
            Swarm.SpawnY = at.Y - room.Y;
            Swarm.Pending = (int)Math.Min(int.MaxValue, (long)Swarm.Pending + count);
            Swarm.Multiplying |= multiplying;
        }

        internal void Say(CloneBody body, string line, double until)
        {
            if (body.LineCard != null) { body.LineCard.Dispose(); body.LineCard = null; }
            body.Line = line;
            body.LineUntil = until;
        }

        internal void Advance(double now)
        {
            double passed = Math.Max(0, Math.Min(0.25, now - last));
            last = now;
            if (!EscDown()) escFrom = -1;
            else
            {
                if (escFrom < 0) escFrom = now;
                if (now - escFrom >= EscHold)
                {
                    ticks.Stop();
                    Action cleared = Cleared;
                    if (cleared != null) cleared();
                    return;
                }
            }
            Stopwatch busy = Stopwatch.StartNew();
            Point cursor = Cursor.Position;
            Point? dragged = Swarm.Tick(now, passed, took < SlowTick, HandsFree(), new Point(cursor.X - room.X, cursor.Y - room.Y));
            if (dragged != null) Cursor.Position = new Point(room.X + dragged.Value.X, room.Y + dragged.Value.Y);
            Render(now);
            took = busy.ElapsedMilliseconds;
            Frames++;
            Action<double> ticked = Ticked;
            if (ticked != null) ticked(now);
        }

        private Bitmap Cell(Clip clip, int which, int cell, bool left)
        {
            long key = ((long)which << 32) | ((long)cell << 1) | (left ? 1L : 0L);
            Bitmap made;
            if (cells.TryGetValue(key, out made)) return made;
            made = new Bitmap(canvas, canvas, PixelFormat.Format32bppPArgb);
            Bitmap strip = left && clip.HasFlipped ? clip.FlippedNow : clip.Strip;
            int width = strip.Width / clip.Count;
            float high = size * clip.Height, broad = high * width / strip.Height;
            using (Graphics graphics = Graphics.FromImage(made))
            {
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TranslateTransform(canvas / 2f, canvas / 2f);
                if (left) graphics.ScaleTransform(-1, 1);
                graphics.DrawImage(strip, new RectangleF(-broad / 2f + clip.ShiftX * size, -high / 2f + clip.ShiftY * size, broad, high), new RectangleF(cell * width, 0, width, strip.Height), GraphicsUnit.Pixel);
            }
            cells[key] = made;
            return made;
        }

        private Bitmap Card(string text)
        {
            SizeF measured;
            using (Bitmap probe = new Bitmap(1, 1))
            using (Graphics measuring = Graphics.FromImage(probe)) measured = measuring.MeasureString(text, font, CardWidth);
            int broad = (int)Math.Ceiling(measured.Width) + 22, high = (int)Math.Ceiling(measured.Height) + 16;
            Bitmap card = new Bitmap(broad, high, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(card))
            using (SolidBrush back = new SolidBrush(Color.FromArgb(238, 16, 14, 24)))
            using (Pen edge = new Pen(Color.FromArgb(255, 255, 45, 120), 2))
            using (SolidBrush ink = new SolidBrush(Color.FromArgb(255, 244, 244, 248)))
            {
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.FillRectangle(back, 0, 0, broad, high);
                graphics.DrawRectangle(edge, 1, 1, broad - 3, high - 3);
                graphics.DrawString(text, font, ink, new RectangleF(11, 8, broad - 16, high - 10));
            }
            return card;
        }

        private void Render(double now)
        {
            using (Graphics graphics = Graphics.FromImage(surface.Image))
            {
                graphics.Clear(Color.Transparent);
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighSpeed;
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.PixelOffsetMode = PixelOffsetMode.None;
                foreach (ClonePrint print in Swarm.Prints)
                {
                    int fade = Math.Min(Fades - 1, (int)((now - print.Born) / Swarm.PrintLife * Fades));
                    graphics.FillRectangle(printInk[(print.Cyan ? Fades : 0) + fade], (int)print.X, (int)print.Y, 8, 3);
                }
                foreach (CloneNote note in Swarm.Notes)
                {
                    if (note.Stale && note.Card != null) { note.Card.Dispose(); note.Card = null; }
                    note.Stale = false;
                    if (note.Card == null) note.Card = Card(note.Text);
                    int noteX = (int)note.X - note.Card.Width / 2, noteY = (int)note.Y - note.Card.Height / 2;
                    graphics.DrawImage(note.Card, noteX, noteY, note.Card.Width, note.Card.Height);
                    if (note.BarFrom < 0) continue;
                    graphics.FillRectangle(barBack, noteX, noteY + note.Card.Height, note.Card.Width, 10);
                    graphics.FillRectangle(barInk, noteX + 2, noteY + note.Card.Height + 2, (int)((note.Card.Width - 4) * Math.Min(0.99, (now - note.BarFrom) / note.BarFor)), 6);
                }
                foreach (CloneBody body in Swarm.Bodies)
                {
                    double age = now - body.Born;
                    bool grinning = body == Swarm.Nabber && Swarm.Holding;
                    Clip clip = age < Swarm.GlitchIn ? glitch : grinning ? grin : Swarm.Walking(body, now) ? walk : idle;
                    int which = clip == glitch ? 0 : clip == grin ? 1 : clip == walk ? 2 : 3;
                    int cell = clip == glitch ? Math.Min(clip.Count - 1, (int)(age * clip.Fps)) : (int)((now + body.Phase) * clip.Fps) % clip.Count;
                    graphics.DrawImage(Cell(clip, which, cell, body.Left), (int)body.X - canvas / 2, (int)body.Y - canvas / 2, canvas, canvas);
                }
                foreach (CloneBody body in Swarm.Bodies)
                {
                    if (body.Line == null) continue;
                    if (now >= body.LineUntil)
                    {
                        body.Line = null;
                        if (body.LineCard != null) { body.LineCard.Dispose(); body.LineCard = null; }
                        continue;
                    }
                    if (body.LineCard == null) body.LineCard = Card(body.Line);
                    int cardX = Math.Max(4, Math.Min(room.Width - body.LineCard.Width - 4, (int)body.X - body.LineCard.Width / 2));
                    graphics.DrawImage(body.LineCard, cardX, Math.Max(4, (int)body.Y - size / 2 - body.LineCard.Height), body.LineCard.Width, body.LineCard.Height);
                }
                if (escFrom >= 0) graphics.FillRectangle(barInk, 14, 14, (int)(180 * Math.Min(1, (now - escFrom) / EscHold)), 8);
            }
            surface.Show(Handle, room.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticks.Dispose();
                foreach (Bitmap cell in cells.Values) cell.Dispose();
                cells.Clear();
                foreach (CloneNote note in Swarm.Notes) if (note.Card != null) note.Card.Dispose();
                foreach (CloneBody body in Swarm.Bodies) if (body.LineCard != null) body.LineCard.Dispose();
                foreach (SolidBrush ink in printInk) ink.Dispose();
                barInk.Dispose();
                barBack.Dispose();
                font.Dispose();
                if (surface != null) { surface.Dispose(); surface = null; }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class Companion
    {
        private const string CloneAsk = "You've cloned yourself on their desktop, and there are {0} of you on screen right now. Write what you all say to each other next, the way you really talk: funny, a bit rude, arguing over who's the real one or what to do with their PC. Two to four lines, each under ten words. Put each line on its own row as its speaker's number, a colon, then the words, like 1: words, with numbers from 1 to {0}. Nothing else.";
        private CloneWindow cloneWindow;
        private bool cloneTalking;
        private double cloneTalkAt;
        private int cloneTurn;
        private string cloneLast;

        private string CloneTool(string action, int count) { return InvokeRequired ? (string)Invoke(new Func<string, int, string>(Clone), action, count) : Clone(action, count); }

        // A number makes exactly that many. No number makes one that keeps multiplying until they stop it.
        internal string Clone(string action, int count)
        {
            if (action == "stop")
            {
                int gone = cloneWindow == null ? 0 : cloneWindow.Swarm.Bodies.Count + cloneWindow.Swarm.Pending;
                ClonesOff();
                return gone == 0 ? "I don't have any clones out." : gone == 1 ? "Got rid of my clone." : "Got rid of all " + gone + " of my clones.";
            }
            double now = clock.Elapsed.TotalSeconds;
            Point centre = new Point(anchor.X + Width / 2, anchor.Y + Height / 2);
            if (cloneWindow == null)
            {
                CloneWindow opened = new CloneWindow(Screen.FromPoint(centre).Bounds, clock, idle, walk, grin, glitch, preferences.Size, Width, random.Next());
                opened.Failed = ClonesOff;
                opened.Cleared = ClonesCleared;
                opened.HandsFree = delegate { return !testing && working == null && !dragging && !pressed; };
                opened.Ticked = CloneTalk;
                cloneWindow = opened;
                cloneTalkAt = now + 3;
                opened.Open(now);
            }
            bool freeForAll = count <= 0;
            cloneWindow.Spawn(freeForAll ? 1 : count, centre, freeForAll);
            if (!testing) Animate("glitch");
            return freeForAll ? "One of me wasn't enough, choom. We'll keep multiplying, so hold Esc when you've had enough."
                : count == 1 ? "There's two of me now, choom. Hold Esc to clear her."
                : "There's " + count + " more of me now, choom. Hold Esc to clear them.";
        }

        internal void ClonesOff()
        {
            cloneTurn++;
            cloneTalking = false;
            CloneWindow closing = cloneWindow;
            cloneWindow = null;
            if (closing != null) closing.Close();
        }

        private void ClonesCleared()
        {
            ClonesOff();
            if (testing || hidden || working != null || speech != null || talkState != TalkState.Off) return;
            heard = "holds Esc";
            box.Prompt = "> sys: ";
            Answer(++asking, "Fine. Just me again.", TalkState.Talking);
        }

        // One brain call writes a few lines for all of them, however many there are, and each is said in turn.
        private void CloneTalk(double now)
        {
            if (hidden) { ClonesOff(); return; }
            if (testing || cloneTalking || now < cloneTalkAt || cloneWindow == null || cloneWindow.Swarm.Bodies.Count == 0) return;
            if (working != null || speech != null || talkState != TalkState.Off || (chatting && !local) || Hushed || brain.Model == null) return;
            cloneTalking = true;
            int turn = ++cloneTurn;
            int copies = cloneWindow.Swarm.Bodies.Count + 1;
            string ask = string.Format(CloneAsk, copies) + (cloneLast == null ? "" : " Last time: " + cloneLast);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string said = null;
                try { said = brain.Aside(Brain.Character, ask, 200); }
                catch (Exception problem) { Brain.Log("clones", -1, problem.Message); }
                Post(delegate
                {
                    if (turn != cloneTurn) return;
                    List<KeyValuePair<int, string>> lines = CloneScript(said);
                    cloneLast = lines.Count == 0 ? cloneLast : lines[lines.Count - 1].Value;
                    CloneLine(lines, 0, turn);
                });
            });
        }

        internal static List<KeyValuePair<int, string>> CloneScript(string said)
        {
            List<KeyValuePair<int, string>> lines = new List<KeyValuePair<int, string>>();
            foreach (string row in (said ?? "").Split('\n'))
            {
                Match line = Regex.Match(row.Trim(), @"^(?:capri\s*)?(\d{1,6})\s*[:.)\-]\s*(.+)$", RegexOptions.IgnoreCase);
                if (line.Success) lines.Add(new KeyValuePair<int, string>(int.Parse(line.Groups[1].Value), line.Groups[2].Value.Trim()));
            }
            return lines;
        }

        private void CloneLater(int wait, List<KeyValuePair<int, string>> lines, int at, int turn)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                System.Threading.Thread.Sleep(wait);
                Post(delegate { CloneLine(lines, at, turn); });
            });
        }

        private void CloneLine(List<KeyValuePair<int, string>> lines, int at, int turn)
        {
            if (turn != cloneTurn || cloneWindow == null) return;
            if (working != null || speech != null || talkState != TalkState.Off) { CloneLater(400, lines, at, turn); return; }
            if (at >= lines.Count)
            {
                cloneTalking = false;
                cloneTalkAt = clock.Elapsed.TotalSeconds + 5 + random.NextDouble() * 6;
                return;
            }
            string line = lines[at].Value;
            int who = Math.Max(0, lines[at].Key - 1) % (cloneWindow.Swarm.Bodies.Count + 1);
            if (who == 0)
            {
                heard = "her clones";
                box.Prompt = "> sys: ";
                Answer(++asking, line, TalkState.Talking);
                CloneLater(500, lines, at + 1, turn);
                return;
            }
            Func<string, byte[]> free = null;
            Func<string, byte[]> speak = preferences.Silent || Hushed || (chatting && !local) ? null : PickVoice(false, out free, false);
            string words = Voice.Spoken(line);
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string trouble = null;
                if (speak != null) wav = Voiced(speak, free, words, out trouble, false);
                if (trouble != null) Brain.Log("clones", -1, trouble);
                Post(delegate
                {
                    if (turn != cloneTurn || cloneWindow == null) return;
                    double now = clock.Elapsed.TotalSeconds;
                    double seconds = wav != null ? Voice.Seconds(wav) : 1 + line.Length * 0.06;
                    if (who - 1 < cloneWindow.Swarm.Bodies.Count) cloneWindow.Say(cloneWindow.Swarm.Bodies[who - 1], line, now + seconds + 1);
                    if (wav != null)
                    {
                        voiceUntil = now + seconds + 0.6;
                        CheckEars(now);
                        Played(wav);
                        PlayVoice(wav);
                    }
                    CloneLater((int)(seconds * 1000) + 300, lines, at + 1, turn);
                });
            });
        }
    }
}
