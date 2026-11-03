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
        internal CloneHaul Hauling;
    }

    // A window one of them is after or has hold of: its frame in their room, the spot on it she grabs, when she gives up, and how
    // much of its top stays on screen (more on a scaled screen).
    internal sealed class CloneHaul
    {
        internal IntPtr Window;
        internal double X, Y, Width, Height, GrabX, GrabY, Until, Keep;
        internal bool Holding, Raised;
        internal CloneBody By;
    }

    // Her copies on the desktop, after Desktop Goose: they wander, leave a trail, steal the cursor and drag notes in (Lachy, 1 Nov).
    // There's no limit on how many. A new one only waits while the PC is still drawing the last frame, so Esc always gets through.
    internal sealed class Swarm
    {
        internal const double Pace = 170, Dash = 560, Heavy = 120, PrintGap = 36, PrintLife = 6, GlitchIn = 0.7, HaulReach = 6;
        private const int Margin = 90;
        internal const int KeepShowing = 140;
        private static readonly string[] HaulLines =
        {
            "mine now", "you weren't using this", "redecorating", "this goes over here now", "borrowing this", "i'm taking this one", "stop clicking it"
        };
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
        internal const string Sorry = "hold esc if you're sorry";
        private readonly Random random;
        private readonly int wide, tall;
        private double nabAt, noteAt, holdUntil;
        private bool holding;
        internal readonly List<CloneBody> Bodies = new List<CloneBody>();
        internal readonly List<CloneNote> Notes = new List<CloneNote>();
        internal readonly List<ClonePrint> Prints = new List<ClonePrint>();
        internal readonly List<CloneHaul> Hauls = new List<CloneHaul>();
        internal CloneBody Nabber;
        internal int Pending;
        internal bool Multiplying;
        internal double SpawnX, SpawnY, HaulAt;
        // Where on her screen windows may land, in their room: clear of the taskbar. All of it unless her window says.
        internal double WorkLeft, WorkTop, WorkRight, WorkBottom;
        // The windows they could grab right now, in their room. None unless her window hands them some.
        internal Func<List<CloneHaul>> Windows = delegate { return new List<CloneHaul>(); };

        internal Swarm(int width, int height, int seed, double now)
        {
            wide = width;
            tall = height;
            WorkRight = width;
            WorkBottom = height;
            random = new Random(seed);
            nabAt = now + 8;
            noteAt = now + 5;
            HaulAt = now + 6;
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
        internal Point? Tick(double now, double passed, bool keepingUp, bool handsFree, Point cursor, bool hauling = false)
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
                if (porter != Nabber && !porter.Fetching && porter.Carrying == null && porter.Hauling == null)
                {
                    porter.Fetching = true;
                    porter.RestUntil = 0;
                    Edge(porter);
                }
            }
            Haul(now, hauling);
            return Nab(now, handsFree, cursor);
        }

        // They run at the cursor or a window they're after, and slow down hauling one.
        private double Speed(CloneBody body) { return body == Nabber || (body.Hauling != null && !body.Hauling.Holding) ? Dash : body.Hauling != null ? Heavy : Pace; }

        private void Move(CloneBody body, double now, double passed)
        {
            if (now < body.RestUntil) return;
            double dx = body.ToX - body.X, dy = body.ToY - body.Y, far = Math.Sqrt(dx * dx + dy * dy), step = Speed(body) * passed;
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
            if (body == Nabber || body.Hauling != null) return;
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
                if (chosen.Fetching || chosen.Carrying != null || chosen.Hauling != null) return null;
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

        // Lachy, 1 Nov: "I want her to actually take your applications". One of them runs to a window, grabs its title bar and
        // drags it somewhere else on her screen. More of them out means more windows on the move, up to three at once.
        private void Haul(double now, bool hauling)
        {
            for (int i = Hauls.Count - 1; i >= 0; i--)
            {
                CloneHaul haul = Hauls[i];
                CloneBody hauler = haul.By;
                bool there = hauler.X == hauler.ToX && hauler.Y == hauler.ToY;
                if (hauling && now < haul.Until && !(haul.Holding && there))
                {
                    if (!haul.Holding && there)
                    {
                        haul.Holding = true;
                        haul.Until = now + Between(3, 6);
                        Drag(haul);
                    }
                    else if (!haul.Holding)
                    {
                        hauler.ToX = haul.X + haul.GrabX;
                        hauler.ToY = haul.Y + haul.GrabY;
                    }
                    if (haul.Holding)
                    {
                        haul.X = hauler.X - haul.GrabX;
                        haul.Y = Math.Max(WorkTop, hauler.Y - haul.GrabY + Math.Sin((now + hauler.Phase) * 9) * 3);
                    }
                    continue;
                }
                Hauls.RemoveAt(i);
                hauler.Hauling = null;
                hauler.RestUntil = now + 1;
                Wander(hauler);
            }
            if (!hauling || now < HaulAt || Bodies.Count == 0 || Hauls.Count >= Math.Min(3, 1 + Bodies.Count / 15)) return;
            HaulAt = now + 3;
            CloneBody chosen = null;
            for (int tries = 0; tries < 4 && chosen == null; tries++)
            {
                CloneBody body = Bodies[random.Next(Bodies.Count)];
                if (body != Nabber && !body.Fetching && body.Carrying == null && body.Hauling == null && now - body.Born >= GlitchIn) chosen = body;
            }
            if (chosen == null) return;
            // Looking for windows costs a little, so with nothing to grab, like over a game, they look again less often.
            HaulAt = now + 8;
            List<CloneHaul> windows = Windows() ?? new List<CloneHaul>();
            windows.RemoveAll(delegate(CloneHaul window) { return Hauls.Exists(delegate(CloneHaul held) { return held.Window == window.Window; }); });
            if (windows.Count == 0) return;
            HaulAt = now + Between(8, 16);
            CloneHaul taken = windows[random.Next(windows.Count)];
            taken.By = chosen;
            taken.GrabX = taken.Width * Between(0.3, 0.7);
            taken.GrabY = Math.Min(16, taken.Height / 4);
            taken.Until = now + HaulReach;
            chosen.Hauling = taken;
            chosen.RestUntil = 0;
            chosen.ToX = taken.X + taken.GrabX;
            chosen.ToY = taken.Y + taken.GrabY;
            if (chosen.Line == null)
            {
                chosen.Line = HaulLines[random.Next(HaulLines.Length)];
                chosen.LineUntil = now + 2.5;
            }
            Hauls.Add(taken);
        }

        // Somewhere it can be got back from: never above the top or under the taskbar, at least its top 140 px on screen (more
        // on a scaled screen), at most a third off a side, and far enough from where it was that they see it go.
        private void Drag(CloneHaul haul)
        {
            double keep = haul.Keep > 0 ? haul.Keep : KeepShowing, x = haul.X, y = haul.Y;
            for (int tries = 0; tries < 6 && Math.Abs(x - haul.X) + Math.Abs(y - haul.Y) < 300; tries++)
            {
                bool side = random.Next(3) == 0;
                x = side ? (random.Next(2) == 0 ? WorkLeft - haul.Width / 3 : WorkRight - haul.Width * 2 / 3) : Between(WorkLeft, Math.Max(WorkLeft + 1, WorkRight - haul.Width));
                y = Between(WorkTop, Math.Max(WorkTop + 1, WorkBottom - keep));
            }
            haul.By.ToX = x + haul.GrabX;
            haul.By.ToY = y + haul.GrabY;
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
        private double last, hintUntil = -1;
        private long took;
        // Esc is read on a thread of its own every 30 ms in real time, so slow frames with hundreds out can't miss a tap or
        // stretch the hold, and a tap shows how to clear them (Ashy, 1 Nov: "pressing escape didnt do much").
        private System.Threading.Thread escWatch;
        private volatile bool closing, tapped;
        private volatile int heldMs = -1;
        private bool cleared;
        private Bitmap hint;
        internal const string Hint = "hold esc to clear us";
        internal readonly Swarm Swarm;
        internal Action Cleared, Failed;
        internal Action<double> Ticked;
        internal Func<bool> HandsFree = delegate { return false; };
        internal Func<bool> EscDown = delegate { return (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0; };
        // Windows on her screen they may grab, with their frames on screen. Null means none, like over a game.
        internal Func<List<KeyValuePair<IntPtr, Rectangle>>> Grabbable = delegate { return null; };
        // Either button, so a left-handed mouse counts, and a click that came and went between frames does too.
        internal Func<bool> Mousing = delegate { return ((Native.GetAsyncKeyState(0x01) | Native.GetAsyncKeyState(0x02)) & 0x8001) != 0; };
        // False once a game opens or she starts streaming, so they drop whatever they're holding.
        internal Func<bool> Allowed = delegate { return true; };
        // Where each window they moved started, everywhere it's been since, and where they last put it, so clearing them puts it back.
        private readonly Dictionary<IntPtr, Rectangle> homes = new Dictionary<IntPtr, Rectangle>(), reach = new Dictionary<IntPtr, Rectangle>();
        private readonly Dictionary<IntPtr, Point> placed = new Dictionary<IntPtr, Point>();
        private readonly List<IntPtr> stuck = new List<IntPtr>();
        private double allowedAt;
        // A file of the windows they've moved, kept up to date while they drag, so a start after a crash or a kill can put them
        // back. It's gone once they're put away.
        internal static string Marker;
        private double markAt;
        private bool unsaved;
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
            Swarm.Windows = Offered;
            Rectangle work = Screen.FromRectangle(area).WorkingArea;
            Swarm.WorkLeft = Math.Max(0, work.Left - area.Left);
            Swarm.WorkTop = Math.Max(0, work.Top - area.Top);
            Swarm.WorkRight = Math.Min(area.Width, work.Right - area.Left);
            Swarm.WorkBottom = Math.Min(area.Height, work.Bottom - area.Top);
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
            escWatch = new System.Threading.Thread(WatchEsc);
            escWatch.IsBackground = true;
            escWatch.Name = "Capri clone Esc";
            escWatch.Priority = System.Threading.ThreadPriority.Highest;
            escWatch.Start();
        }

        internal bool Hinting { get { return hintUntil > last; } }

        private void WatchEsc()
        {
            Stopwatch held = new Stopwatch();
            while (!closing)
            {
                bool down;
                try { down = EscDown(); }
                catch (Exception) { down = false; }
                if (down)
                {
                    if (!held.IsRunning) held.Start();
                    heldMs = (int)held.ElapsedMilliseconds;
                    if (held.ElapsedMilliseconds >= EscHold * 1000)
                    {
                        try { BeginInvoke(new Action(Clear)); }
                        catch (InvalidOperationException) { }
                        return;
                    }
                }
                else if (held.IsRunning)
                {
                    tapped = true;
                    held.Reset();
                    heldMs = -1;
                }
                System.Threading.Thread.Sleep(30);
            }
        }

        private void Clear()
        {
            if (cleared || IsDisposed) return;
            cleared = true;
            ticks.Stop();
            Action done = Cleared;
            if (done != null) done();
        }

        internal void Spawn(int count, Point at, bool multiplying)
        {
            Swarm.SpawnX = at.X - room.X;
            Swarm.SpawnY = at.Y - room.Y;
            Swarm.Pending = (int)Math.Min(int.MaxValue, (long)Swarm.Pending + count);
            Swarm.Multiplying |= multiplying;
        }

        internal void Pin(string text, Point at)
        {
            Swarm.Notes.Add(new CloneNote { X = Math.Max(160, Math.Min(room.Width - 160, at.X - room.X)), Y = Math.Max(40, at.Y - room.Y), Text = text });
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
            if (tapped) { tapped = false; hintUntil = now + 3; }
            Stopwatch busy = Stopwatch.StartNew();
            Point cursor = Cursor.Position;
            bool handsFree = HandsFree();
            if (Swarm.Hauls.Count > 0 && now >= allowedAt)
            {
                allowedAt = now + 3;
                if (!Allowed()) foreach (CloneHaul dropped in Swarm.Hauls) dropped.Until = 0;
            }
            foreach (CloneHaul haul in Swarm.Hauls) if (!haul.Holding) Track(haul);
            Point? dragged = Swarm.Tick(now, passed, took < SlowTick, handsFree, new Point(cursor.X - room.X, cursor.Y - room.Y), handsFree && !Mousing());
            if (dragged != null) Cursor.Position = new Point(room.X + dragged.Value.X, room.Y + dragged.Value.Y);
            foreach (CloneHaul haul in Swarm.Hauls) if (haul.Holding) Carry(haul);
            if (unsaved && now >= markAt)
            {
                unsaved = false;
                markAt = now + 0.25;
                Mark();
            }
            Render(now);
            took = busy.ElapsedMilliseconds;
            Frames++;
            Action<double> ticked = Ticked;
            if (ticked != null) ticked(now);
        }

        private List<CloneHaul> Offered()
        {
            List<CloneHaul> offered = new List<CloneHaul>();
            List<KeyValuePair<IntPtr, Rectangle>> windows = Grabbable();
            if (windows == null) return offered;
            foreach (KeyValuePair<IntPtr, Rectangle> window in windows)
            {
                Rectangle frame = window.Value;
                if (stuck.Contains(window.Key) || !room.Contains(frame.X + frame.Width / 2, frame.Y + frame.Height / 2)) continue;
                offered.Add(new CloneHaul { Window = window.Key, X = frame.X - room.X, Y = frame.Y - room.Y, Width = frame.Width, Height = frame.Height, Keep = Swarm.KeepShowing * Ledges.Dpi(window.Key) / 96.0 });
            }
            return offered;
        }

        // Where a window they're running to is now. They let go of one that's closed, shrunk down, full size or frozen.
        private void Track(CloneHaul haul)
        {
            Rectangle frame;
            if (!Ledges.Haulable(haul.Window, out frame)) { haul.Until = 0; return; }
            haul.X = frame.X - room.X;
            haul.Y = frame.Y - room.Y;
            haul.Width = frame.Width;
            haul.Height = frame.Height;
        }

        // The window goes where its hauler is. The move is posted to the window's own program, so a frozen app can't hold her up,
        // and a window she isn't allowed to move, like an admin one, is let go and never picked again. One that would end up on a
        // screen that's gone, like a monitor unplugged mid-drag, is let go where it is.
        private void Carry(CloneHaul haul)
        {
            Rectangle frame, outer;
            if (!Ledges.Haulable(haul.Window, out frame) || !Ledges.Outer(haul.Window, out outer)) { haul.Until = 0; return; }
            Point to = new Point(room.X + (int)Math.Round(haul.X) + outer.X - frame.X, room.Y + (int)Math.Round(haul.Y) + outer.Y - frame.Y);
            if (!Ledges.OnScreen(new Point(to.X + outer.Width / 2, to.Y + 20))) { haul.Until = 0; return; }
            if (!haul.Raised && !Theirs(haul.Window, outer.Location))
            {
                homes[haul.Window] = outer;
                reach[haul.Window] = new Rectangle(outer.Location, new Size(1, 1));
            }
            // The first move brings it up, but never over the window you're using: it goes just under that one.
            IntPtr after = IntPtr.Zero;
            int order = haul.Raised ? 0x0004 : 0;
            if (!haul.Raised)
            {
                IntPtr front = Native.GetForegroundWindow();
                Rectangle frontFrame;
                if (front == haul.Window) order = 0x0004;
                else if (Ledges.Showing(front, out frontFrame) && (Native.GetWindowLong(front, -20) & 0x08) == 0) after = front;
            }
            bool moved = Native.SetWindowPos(haul.Window, after, to.X, to.Y, 0, 0, 0x4000 | 0x0010 | 0x0001 | order);
            if (!moved && after != IntPtr.Zero) moved = Native.SetWindowPos(haul.Window, IntPtr.Zero, to.X, to.Y, 0, 0, 0x4000 | 0x0010 | 0x0001 | 0x0004);
            if (!moved)
            {
                stuck.Add(haul.Window);
                if (!placed.ContainsKey(haul.Window)) { homes.Remove(haul.Window); reach.Remove(haul.Window); }
                haul.Until = 0;
                return;
            }
            haul.Raised = true;
            placed[haul.Window] = to;
            Rectangle path;
            reach[haul.Window] = reach.TryGetValue(haul.Window, out path) ? Rectangle.Union(path, new Rectangle(to, new Size(1, 1))) : new Rectangle(to, new Size(1, 1));
            unsaved = true;
        }

        // Still somewhere they put it: anywhere along its way, give or take 24 px (scaled) for an app that's slow to catch up or
        // nudges itself. Anywhere else, it's been moved by hand.
        private bool Theirs(IntPtr window, Point at)
        {
            Rectangle path;
            return reach.TryGetValue(window, out path) && OnPath(window, path, at);
        }

        private static bool OnPath(IntPtr window, Rectangle path, Point at)
        {
            int slack = 24 * Ledges.Dpi(window) / 96;
            path.Inflate(slack, slack);
            return path.Contains(at);
        }

        // Every window they moved goes back where it was, unless it's since been moved by hand, closed or made full size. A
        // minimised one goes back there when it's restored, one on another desktop goes back all the same, and none is sent to a
        // screen that's gone. Every move is posted, so a frozen app can't hold her up.
        internal void PutBack()
        {
            foreach (KeyValuePair<IntPtr, Rectangle> home in homes)
            {
                Point last;
                Rectangle path;
                if (placed.TryGetValue(home.Key, out last) && reach.TryGetValue(home.Key, out path)) PutOneBack(home.Key, home.Value, path, last);
            }
            homes.Clear();
            reach.Clear();
            placed.Clear();
            unsaved = false;
            Unmark();
        }

        private static bool PutOneBack(IntPtr window, Rectangle home, Rectangle path, Point last)
        {
            Rectangle outer;
            if (!Ledges.Alive(window) || Ledges.FullSize(window) || !Ledges.OnScreen(new Point(home.X + home.Width / 2, home.Y + 20))) return false;
            if (Ledges.Minimised(window))
            {
                if (Ledges.Frozen(window)) return false;
                Ledges.MoveRestored(window, home.X - last.X, home.Y - last.Y);
                return true;
            }
            if (!Ledges.Outer(window, out outer) || !OnPath(window, path, outer.Location)) return false;
            return Native.SetWindowPos(window, IntPtr.Zero, home.X, home.Y, 0, 0, 0x4000 | 0x0010 | 0x0004 | 0x0001);
        }

        // One line a window: its handle, its program, where it started, everywhere it's been (with room for the last quarter of a
        // second, which may not be saved yet) and where it was last put.
        internal static string MarkLine(IntPtr window, int program, Rectangle home, Rectangle path, Point last)
        {
            path.Inflate(40, 40);
            long[] values = { window.ToInt64(), program, home.X, home.Y, home.Width, home.Height, path.X, path.Y, path.Width, path.Height, last.X, last.Y };
            string[] words = new string[values.Length];
            for (int i = 0; i < values.Length; i++) words[i] = values[i].ToString(System.Globalization.CultureInfo.InvariantCulture);
            return string.Join(" ", words);
        }

        private void Mark()
        {
            if (Marker == null) return;
            System.Text.StringBuilder lines = new System.Text.StringBuilder();
            foreach (KeyValuePair<IntPtr, Rectangle> home in homes)
            {
                Point last;
                Rectangle path;
                int program;
                if (!placed.TryGetValue(home.Key, out last) || !reach.TryGetValue(home.Key, out path)) continue;
                Native.GetWindowThreadProcessId(home.Key, out program);
                lines.AppendLine(MarkLine(home.Key, program, home.Value, path, last));
            }
            try { System.IO.File.WriteAllText(Marker, lines.ToString()); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Unmark()
        {
            if (Marker == null) return;
            try { System.IO.File.Delete(Marker); }
            catch (System.IO.IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // A crash or a kill mid-drag would leave windows where her clones dropped them, so her next start puts back each one that's
        // still in the same program and still somewhere they had it. One closed or moved by hand since is left alone.
        internal static int Recover()
        {
            if (Marker == null || !System.IO.File.Exists(Marker)) return 0;
            string[] lines;
            try { lines = System.IO.File.ReadAllLines(Marker); }
            catch (System.IO.IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
            int back = 0;
            foreach (string line in lines)
            {
                string[] words = line.Split(' ');
                long[] values = new long[12];
                bool read = words.Length == values.Length;
                for (int i = 0; read && i < values.Length; i++) read = long.TryParse(words[i], System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out values[i]);
                if (!read) continue;
                IntPtr window = new IntPtr(values[0]);
                int program;
                if (!Ledges.Alive(window)) continue;
                Native.GetWindowThreadProcessId(window, out program);
                if (program != values[1]) continue;
                if (PutOneBack(window, new Rectangle((int)values[2], (int)values[3], (int)values[4], (int)values[5]), new Rectangle((int)values[6], (int)values[7], (int)values[8], (int)values[9]), new Point((int)values[10], (int)values[11]))) back++;
            }
            Unmark();
            return back;
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
                    bool grinning = (body == Swarm.Nabber && Swarm.Holding) || (body.Hauling != null && body.Hauling.Holding);
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
                // The hold fills a bar at the top middle, and a tap shows how to clear them under it.
                int holding = heldMs;
                if (holding >= 0)
                {
                    graphics.FillRectangle(barBack, room.Width / 2 - 124, 20, 248, 14);
                    graphics.FillRectangle(barInk, room.Width / 2 - 120, 24, (int)(240 * Math.Min(1, holding / (EscHold * 1000))), 6);
                }
                if (now < hintUntil)
                {
                    if (hint == null) hint = Card(Hint);
                    graphics.DrawImage(hint, room.Width / 2 - hint.Width / 2, 40, hint.Width, hint.Height);
                }
            }
            surface.Show(Handle, room.Location);
        }

        protected override void Dispose(bool disposing)
        {
            closing = true;
            if (disposing)
            {
                ticks.Dispose();
                if (hint != null) { hint.Dispose(); hint = null; }
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

        private int upsets;

        private string CloneTool(string action, int count) { return InvokeRequired ? (string)Invoke(new Func<string, int, string>(Clone), action, count) : Clone(action, count); }

        private bool UpsetTool()
        {
            if (testing) return false;
            Func<bool> upset = delegate { return Upset(random.Next(3)); };
            try { return InvokeRequired ? (bool)Invoke(upset) : upset(); }
            catch (InvalidOperationException) { return false; }
        }

        // An insult sets her clones off 1 time in 3, then 2 in 3, and always by the third. Never over a job, a game or a stream,
        // and the note near her tells anyone who's never met them how to stop it.
        internal bool Upset(int roll)
        {
            if (!preferences.UpsetChaos || working != null || hidden || cloneWindow != null || InChat || Computer.Streaming || Computer.GameOpen() != null) return false;
            upsets++;
            if (roll >= upsets) return false;
            upsets = 0;
            Clone("spawn", 0);
            if (cloneWindow != null) cloneWindow.Pin(Swarm.Sorry, new Point(anchor.X + Width / 2, anchor.Y - 24));
            return true;
        }

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
                opened.HandsFree = delegate { return !testing && chaosScreen == null && working == null && !dragging && !pressed; };
                int own;
                using (Process self = Process.GetCurrentProcess()) own = self.Id;
                opened.Grabbable = delegate { return Computer.Streaming || Computer.GameOpen() != null ? null : Ledges.Haulable(own); };
                opened.Allowed = delegate { return chaosScreen == null && !Computer.Streaming && Computer.GameOpen() == null; };
                opened.Ticked = delegate(double tick) { if (cloneWindow != opened) return; CloneTick(tick); ChaosTick(tick); };
                cloneWindow = opened;
                chaosScreenAt = now + 40 + random.NextDouble() * 80;
                chaosScreenUsed = false;
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
            ClearIcons();
            cloneTurn++;
            cloneTalking = false;
            ChaosOff();
            CloneWindow closing = cloneWindow;
            cloneWindow = null;
            if (closing == null) return;
            try { closing.PutBack(); }
            catch (Exception problem) { Brain.Log("clones", -1, problem.Message); }
            closing.Close();
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
            if (working != null || speech != null || LineBusy || talkState != TalkState.Off) { CloneLater(400, lines, at, turn); return; }
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
                Action play = null;
                play = delegate
                {
                    if (turn != cloneTurn || cloneWindow == null) return;
                    if (LineBusy) { ownLines.Enqueue(play); return; }
                    if (preferences.Silent || Hushed) wav = null;
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
                };
                Post(play);
            });
        }
    }
}
