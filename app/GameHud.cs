using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal enum HudMood { Idle, Focus, Talk, Happy, Hit, Surprise }

    internal enum HudBox { None, Listening, Thinking, Reply, Job }

    // Gaming Mode's HUD: a click-through window at the top left of the game's monitor that never takes focus or the cursor,
    // with her eyes in a framed panel, a portrait while she talks, her reply box and a status strip. Everything here runs on
    // her UI thread, and a call from any other thread is posted there, so game code can call these from anywhere.
    internal static class GameHud
    {
        private static Control owner;
        private static HudWindow window;
        private static HudLife life;

        // The control that owns her UI thread. Nothing is drawn or made until the first Show.
        internal static void Attach(Control control) { owner = control; }

        internal static bool Showing { get { return window != null && window.Up; } }

        // The HUD on screen, for her type box to sit under it.
        internal static Rectangle Bounds { get { return Showing ? window.OnScreen : Rectangle.Empty; } }

        internal static HudScene Scene { get { return life == null ? null : life.Scene; } }

        // How far it sits from the corner, in pixels of a 1080p game, so it can clear a game's health bar.
        internal static Point Offset;

        private static void Run(Action action)
        {
            Control control = owner;
            if (control == null || !control.IsHandleCreated) { action(); return; }
            if (!control.InvokeRequired) { action(); return; }
            try { control.BeginInvoke(action); } catch (InvalidOperationException) { }
        }

        private static void Ready()
        {
            if (life == null) life = new HudLife();
            if (window == null || window.IsDisposed) window = new HudWindow(life);
        }

        internal static void Show(Rectangle gameBounds)
        {
            Run(delegate
            {
                Ready();
                window.Settle(gameBounds);
            });
        }

        internal static void Hide()
        {
            Run(delegate
            {
                if (window != null) window.Away();
            });
        }

        internal static void Mood(HudMood mood) { Run(delegate { if (Showing) { life.Mood(mood); window.Wake(); } }); }

        internal static void Say(string line) { Say(line, 0); }

        // Her line in the reply box at this many letters a second, or her usual pace for 0.
        internal static void Say(string line, double pace) { Run(delegate { if (Showing) { life.Say(line, pace); window.Wake(); } }); }

        internal static void Hear(string words, bool thinking) { Run(delegate { if (Showing) { life.Hear(words, thinking); window.Wake(); } }); }

        // A job's status, in the reply box while the task panel stays away.
        internal static void Job(string status) { Run(delegate { if (Showing) { life.Job(status); window.Wake(); } }); }

        // Keeps her line up this many seconds more, for as long as her voice goes on.
        internal static void Hold(double seconds) { Run(delegate { if (Showing) life.Hold(seconds); }); }

        internal static void Clear() { Run(delegate { if (Showing) { life.Clear(); window.Wake(); } }); }

        // Her mouth from her voice, 0 shut to 4 wide, or -1 for none.
        internal static void Mouth(int shape) { Run(delegate { if (Showing && life.Mouth(shape)) window.Wake(); }); }

        // The mic's level while LISTENING is up.
        internal static void Level(double value) { Run(delegate { if (Showing && life.Level(value)) window.Wake(); }); }

        internal static void Status(bool voice, bool quiet) { Run(delegate { if (life == null) life = new HudLife(); if (life.Status(voice, quiet) && Showing) window.Wake(); }); }

        // Hides it from her own screenshots for a moment, like her task panel. True when it's on screen.
        internal static bool Unseen(bool unseen) { return window != null && !window.IsDisposed && window.Unseen(unseen); }

        // Keeps it above a game that sits top-most too.
        internal static void Lift() { if (Showing) window.Lift(); }

        internal static void Close()
        {
            if (window != null) window.Close();
            window = null;
        }

        // Share of one CPU core the HUD has used since it docked, for her smoke test.
        internal static double Load { get { return window == null ? 0 : window.Share; } }

        internal static bool ClickThrough { get { return window != null && window.ClickThrough; } }

        internal static int Frames { get { return window == null ? 0 : window.Frames; } }
    }

    // One moment of the HUD. Her smoke test fills one in by hand to draw any state.
    internal sealed class HudScene
    {
        internal HudMood Mood = HudMood.Idle;
        internal float LookX, LookY;
        internal float Open = 1;
        internal float Narrow;
        internal float Wide;
        internal float Portrait;
        internal int Mouth = -1;
        internal bool Grin;
        internal float Hit;
        internal float Burst;
        internal int Seed = 1;
        internal float Boot = 1;
        internal HudBox Box;
        internal string Words = "";
        internal string Line = "";
        internal int Revealed;
        internal float Fade = 1;
        internal float Level;
        internal bool Caret;
        internal float Beat;
        internal bool Voice = true;
        internal bool Quiet;
    }

    // Keeps her eyes alive and her box up to date. Step moves everything to `now` and says if the picture changed, and Next
    // says how long nothing will, so the window sleeps between moves.
    internal sealed class HudLife
    {
        private const double LookTime = 0.022;
        private const double BlinkTime = 0.17;
        private const double TalkPace = 28;
        private const double SwapTime = 0.17;
        private const double FadeTime = 0.34;
        private readonly Random random = new Random();
        internal readonly HudScene Scene = new HudScene();
        private double clock, lookedAt, lookX, lookY, goalX, goalY, nextLook, nextBlink, blinkFrom = -1, blinkSecond = -1;
        private double hitFrom = -1, burstFrom = -1, bootFrom = -1, moodUntil = -1, swapFrom = -1, swapStart, swapGoal;
        private double lineFrom = -1, lineUntil = -1, fadeFrom = -1, pace = TalkPace, hearUntil = -1, letterFrom;
        private HudMood calm = HudMood.Idle;
        private int mouth = -1;
        private bool awake, changed;

        internal void Start(double now)
        {
            clock = now;
            bootFrom = now;
            Scene.Boot = 0;
            Scene.Open = 0;
            Scene.Portrait = 0;
            Scene.Box = HudBox.None;
            Scene.Mood = calm = HudMood.Idle;
            moodUntil = -1;
            swapFrom = -1;
            lineFrom = lineUntil = fadeFrom = hearUntil = -1;
            nextLook = now + 0.7;
            nextBlink = now + 2.5;
            goalX = goalY = lookX = lookY = 0;
            awake = true;
        }

        internal void Stop() { awake = false; }

        private double Now { get { return clock; } }

        internal void Mood(HudMood mood)
        {
            if (mood == HudMood.Idle || mood == HudMood.Focus)
            {
                calm = mood;
                if (moodUntil < 0 && lineFrom < 0) Scene.Mood = mood;
                if (mood == HudMood.Focus) nextLook = Now;
                return;
            }
            Scene.Mood = mood;
            Scene.Seed = random.Next(1, int.MaxValue);
            if (mood == HudMood.Hit) { hitFrom = Now; moodUntil = Now + 0.62; }
            else if (mood == HudMood.Surprise) { burstFrom = Now; moodUntil = Now + 1.25; nextLook = Now; }
            else if (mood == HudMood.Happy) { moodUntil = Now + 2.6; Swap(1); }
            else { moodUntil = Now + 3; Swap(1); }
        }

        private void Swap(float goal)
        {
            if (swapFrom < 0 && Scene.Portrait == goal) return;
            if (swapFrom >= 0 && swapGoal == goal) return;
            swapFrom = Now;
            swapStart = Scene.Portrait;
            swapGoal = goal;
            Scene.Seed = random.Next(1, int.MaxValue);
        }

        internal void Say(string line, double speed)
        {
            string text = (line ?? "").Trim();
            if (text.Length == 0) return;
            pace = speed > 0 ? speed : pace > 0 && text == Scene.Line ? pace : TalkPace;
            if (Scene.Box == HudBox.Reply && text == Scene.Line && fadeFrom < 0) return;
            if (Scene.Box != HudBox.Listening && Scene.Box != HudBox.Thinking) Scene.Words = "";
            Scene.Line = text;
            Scene.Revealed = 0;
            Scene.Box = HudBox.Reply;
            changed = true;
            Scene.Fade = 1;
            Scene.Caret = true;
            lineFrom = Now;
            letterFrom = Now;
            fadeFrom = -1;
            hearUntil = -1;
            lineUntil = Now + text.Length / pace + Math.Min(14, 4 + text.Length * 0.05);
            Scene.Mood = HudMood.Talk;
            moodUntil = -1;
            Swap(1);
        }

        internal void Hear(string words, bool thinking)
        {
            string text = (words ?? "").Trim();
            if (text.Length == 0 && !thinking && words == null)
            {
                if (Scene.Box == HudBox.Listening || Scene.Box == HudBox.Thinking) Fade();
                return;
            }
            Scene.Box = thinking ? HudBox.Thinking : HudBox.Listening;
            Scene.Words = text;
            Scene.Fade = 1;
            fadeFrom = -1;
            lineUntil = -1;
            hearUntil = Now + (thinking ? 30 : 3);
            changed = true;
            if (lineFrom >= 0) { lineFrom = -1; Calm(); }
        }

        internal void Job(string status)
        {
            string text = (status ?? "").Trim();
            if (text.Length == 0) return;
            Scene.Box = HudBox.Job;
            Scene.Line = text;
            Scene.Revealed = text.Length;
            Scene.Fade = 1;
            fadeFrom = -1;
            lineUntil = Now + 30;
            changed = true;
        }

        internal void Hold(double seconds)
        {
            if (lineUntil >= 0 && fadeFrom < 0) lineUntil = Math.Max(lineUntil, Now + seconds);
        }

        internal void Clear()
        {
            if (Scene.Box != HudBox.None && fadeFrom < 0) Fade();
        }

        private void Fade()
        {
            fadeFrom = Now;
            lineUntil = -1;
            hearUntil = -1;
        }

        internal bool Mouth(int shape)
        {
            if (shape == mouth) return false;
            mouth = shape;
            return true;
        }

        internal bool Level(double value)
        {
            float step = (float)Math.Round(Math.Min(1, Math.Sqrt(Math.Max(0, value)) * 4) * 6) / 6f;
            if (step == Scene.Level) return false;
            Scene.Level = step;
            changed = Scene.Box == HudBox.Listening;
            return changed;
        }

        internal bool Status(bool voice, bool quiet)
        {
            if (voice == Scene.Voice && quiet == Scene.Quiet) return false;
            Scene.Voice = voice;
            Scene.Quiet = quiet;
            changed = true;
            return true;
        }

        private void Calm()
        {
            Scene.Mood = calm;
            Scene.Grin = false;
            moodUntil = -1;
            Swap(0);
        }

        // Moves her to `now`. True when the picture changed.
        internal bool Step(double now)
        {
            if (!awake) return false;
            clock = now;
            bool moved = changed;
            changed = false;
            float was;
            if (bootFrom >= 0)
            {
                Scene.Boot = (float)Math.Min(1, (now - bootFrom) / 0.42);
                if (Scene.Boot >= 1) { bootFrom = -1; blinkFrom = now - BlinkTime * 0.5; }
                moved = true;
            }
            if (moodUntil >= 0 && now >= moodUntil)
            {
                bool talking = lineFrom >= 0;
                moodUntil = -1;
                if (talking) Scene.Mood = HudMood.Talk;
                else Calm();
                moved = true;
            }
            Scene.Grin = Scene.Mood == HudMood.Happy;
            if (swapFrom >= 0)
            {
                double t = Math.Min(1, (now - swapFrom) / SwapTime);
                Scene.Portrait = (float)(swapStart + (swapGoal - swapStart) * t);
                if (t >= 1) { swapFrom = -1; Scene.Portrait = (float)swapGoal; }
                moved = true;
            }
            if (hitFrom >= 0)
            {
                Scene.Hit = (float)Math.Max(0, 1 - (now - hitFrom) / 0.46);
                if (Scene.Hit <= 0) hitFrom = -1;
                moved = true;
            }
            if (burstFrom >= 0)
            {
                double t = (now - burstFrom) / 0.95;
                Scene.Burst = (float)Math.Max(0, Math.Min(1, t < 0.8 ? t / 0.1 : (1 - t) / 0.2));
                if (t >= 1) { burstFrom = -1; Scene.Burst = 0; }
                moved = true;
            }
            moved |= Lids(now);
            moved |= Gaze(now);
            if (lineFrom >= 0 && Scene.Box == HudBox.Reply)
            {
                int shown = Math.Min(Scene.Line.Length, (int)((now - letterFrom) * pace));
                if (shown != Scene.Revealed)
                {
                    Scene.Revealed = shown;
                    moved = true;
                }
                bool caret = shown < Scene.Line.Length;
                if (caret != Scene.Caret) { Scene.Caret = caret; moved = true; }
            }
            if (Scene.Box == HudBox.Listening || Scene.Box == HudBox.Thinking || Scene.Box == HudBox.Job)
            {
                bool caret = (int)(now * 2.5) % 2 == 0;
                if (caret != Scene.Caret) { Scene.Caret = caret; moved = true; }
                float beat = (float)((now * 1.4) % 1);
                if (Scene.Box != HudBox.Listening && (int)(beat * 12) != (int)(Scene.Beat * 12)) moved = true;
                Scene.Beat = beat;
            }
            int shape = -1;
            if (Scene.Mood == HudMood.Talk || lineFrom >= 0) shape = mouth >= 0 ? mouth : Lip(now);
            if (shape != Scene.Mouth) { Scene.Mouth = shape; moved = true; }
            if (hearUntil >= 0 && now >= hearUntil) Fade();
            if (lineUntil >= 0 && now >= lineUntil) Fade();
            if (fadeFrom >= 0)
            {
                was = Scene.Fade;
                Scene.Fade = (float)Math.Max(0, 1 - (now - fadeFrom) / FadeTime);
                moved |= was != Scene.Fade;
                if (Scene.Fade <= 0)
                {
                    fadeFrom = -1;
                    Scene.Box = HudBox.None;
                    Scene.Words = "";
                    Scene.Fade = 1;
                    if (lineFrom >= 0) { lineFrom = -1; Calm(); }
                }
            }
            return moved;
        }

        private int Lip(double now)
        {
            if (Scene.Revealed >= Scene.Line.Length) return -1;
            int step = (int)((now - letterFrom) * 12);
            return Companion.MouthFor(Scene.Line[Math.Min(Scene.Line.Length - 1, Math.Max(0, Scene.Revealed))], step);
        }

        private bool Lids(double now)
        {
            float was = Scene.Open;
            float narrow = Scene.Narrow, wide = Scene.Wide;
            float aimNarrow = Scene.Mood == HudMood.Focus ? 1 : Scene.Mood == HudMood.Happy ? 0.55f : 0;
            float aimWide = Scene.Mood == HudMood.Surprise ? 1 : 0;
            Scene.Narrow += (aimNarrow - Scene.Narrow) * 0.45f;
            Scene.Wide += (aimWide - Scene.Wide) * 0.55f;
            if (Math.Abs(Scene.Narrow - aimNarrow) < 0.01f) Scene.Narrow = aimNarrow;
            if (Math.Abs(Scene.Wide - aimWide) < 0.01f) Scene.Wide = aimWide;
            if (blinkFrom < 0 && now >= nextBlink && bootFrom < 0)
            {
                blinkFrom = now;
                bool twice = random.NextDouble() < 0.18;
                blinkSecond = twice ? now + BlinkTime + 0.09 : -1;
                nextBlink = now + 2.4 + random.NextDouble() * 3.8;
            }
            if (blinkFrom < 0 && blinkSecond >= 0 && now >= blinkSecond) { blinkFrom = now; blinkSecond = -1; }
            if (bootFrom >= 0) Scene.Open = 0;
            else if (blinkFrom >= 0)
            {
                double t = (now - blinkFrom) / BlinkTime;
                if (t >= 1) { blinkFrom = -1; Scene.Open = 1; }
                else Scene.Open = (float)Math.Abs(1 - 2 * Math.Min(1, t * 1.15));
            }
            return was != Scene.Open || narrow != Scene.Narrow || wide != Scene.Wide;
        }

        private bool Gaze(double now)
        {
            if (now >= nextLook && bootFrom < 0)
            {
                bool focused = Scene.Mood == HudMood.Focus;
                double far = random.NextDouble();
                if (Scene.Mood == HudMood.Surprise) { goalX = 0; goalY = -0.1; nextLook = now + 0.9; }
                else if (focused) { goalX = (random.NextDouble() * 2 - 1) * 0.3; goalY = (random.NextDouble() * 2 - 1) * 0.16; nextLook = now + 0.5 + random.NextDouble() * 1.1; }
                else if (Scene.Portrait > 0.5f) { goalX = (random.NextDouble() * 2 - 1) * 0.3; goalY = (random.NextDouble() * 2 - 1) * 0.2; nextLook = now + 0.8 + random.NextDouble() * 1.8; }
                else
                {
                    goalX = far < 0.35 ? (random.NextDouble() * 2 - 1) * 0.28 : (random.Next(2) * 2 - 1) * (0.5 + random.NextDouble() * 0.5);
                    goalY = (random.NextDouble() * 2 - 1) * 0.55;
                    nextLook = now + 1.1 + random.NextDouble() * 2.3;
                }
            }
            float oldX = Scene.LookX, oldY = Scene.LookY;
            double keep = Math.Exp(-Math.Min(0.05, Math.Max(0.016, now - lookedAt)) / LookTime);
            lookedAt = now;
            lookX = goalX + (lookX - goalX) * keep;
            lookY = goalY + (lookY - goalY) * keep;
            if (Math.Abs(lookX - goalX) < 0.01) lookX = goalX;
            if (Math.Abs(lookY - goalY) < 0.01) lookY = goalY;
            Scene.LookX = (float)lookX;
            Scene.LookY = (float)lookY;
            return oldX != Scene.LookX || oldY != Scene.LookY;
        }

        // Seconds until the picture next changes on its own.
        internal double Next(double now)
        {
            if (!awake) return 3600;
            bool moving = bootFrom >= 0 || swapFrom >= 0 || hitFrom >= 0 || burstFrom >= 0 || blinkFrom >= 0 || fadeFrom >= 0
                || Scene.LookX != goalX || Scene.LookY != goalY || Scene.Narrow != (Scene.Mood == HudMood.Focus ? 1 : Scene.Mood == HudMood.Happy ? 0.55f : 0) || Scene.Wide != (Scene.Mood == HudMood.Surprise ? 1 : 0);
            if (moving) return 0.03;
            double soon = Math.Min(nextBlink, nextLook);
            if (blinkSecond >= 0) soon = Math.Min(soon, blinkSecond);
            if (moodUntil >= 0) soon = Math.Min(soon, moodUntil);
            if (lineUntil >= 0) soon = Math.Min(soon, lineUntil);
            if (hearUntil >= 0) soon = Math.Min(soon, hearUntil);
            if (lineFrom >= 0 && Scene.Revealed < Scene.Line.Length) return 0.03;
            if (Scene.Box == HudBox.Listening) soon = Math.Min(soon, now + 0.2);
            if (Scene.Box == HudBox.Thinking || Scene.Box == HudBox.Job) soon = Math.Min(soon, now + 0.08);
            return Math.Max(0.015, soon - now);
        }
    }

    // The window itself: layered, top-most, click-through and never active. It draws straight into a DIB that Windows reads
    // from, so a frame costs no copy, and the timer only runs up to the next thing that moves.
    internal sealed class HudWindow : Overlay
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct DibHead
        {
            internal int Size, Width, Height;
            internal short Planes, Bits;
            internal int Compression, Image, PelsX, PelsY, Used, Important;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref DibHead head, int usage, out IntPtr bits, IntPtr section, int offset);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Native.Point destination, ref Native.Size size, IntPtr source, ref Native.Point origin, int colorKey, ref Native.Blend blend, int flags);
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Box rectangle, int flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out int x, out int y);

        [StructLayout(LayoutKind.Sequential)] private struct Box { internal int Left, Top, Right, Bottom; }

        private const int Inset = 14;
        private readonly HudLife life;
        private readonly Timer ticks = new Timer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Stopwatch busy = new Stopwatch();
        private HudPaint paint;
        private IntPtr dc, dib, old, bits;
        private Bitmap canvas;
        private Graphics pen;
        private Rectangle game;
        private float scale;
        private Point corner, placed;
        private bool up;
        private double dockedAt;
        private Size shown;
        private int frames;

        internal HudWindow(HudLife scene) : base("Capri HUD")
        {
            life = scene;
            ticks.Interval = 15;
            ticks.Tick += delegate { Step(); };
        }

        internal bool Up { get { return up; } }

        internal Rectangle OnScreen { get { return new Rectangle(corner, shown); } }

        internal int Frames { get { return frames; } }

        internal double Share { get { double spent = clock.Elapsed.TotalSeconds - dockedAt; return spent <= 0 ? 0 : busy.Elapsed.TotalSeconds / spent; } }

        internal bool ClickThrough
        {
            get
            {
                if (!IsHandleCreated) return false;
                int style = Native.GetWindowLong(Handle, -20);
                return (style & 0x20) != 0 && (style & 0x08000000) != 0 && (style & 0x80000) != 0 && (style & 0x8) != 0;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00000020;
                return value;
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        internal bool Unseen(bool unseen)
        {
            if (!IsHandleCreated) return false;
            try { SetWindowDisplayAffinity(Handle, unseen ? 0x11u : 0u); }
            catch (EntryPointNotFoundException) { return false; }
            return unseen && up;
        }

        internal void Lift()
        {
            if (IsHandleCreated) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        // Windows' scale on the game's monitor, shrunk a little for a small game window so she doesn't crowd it.
        private static float ScaleFor(Rectangle bounds)
        {
            int dpi = 96;
            try
            {
                Box box = new Box { Left = bounds.Left, Top = bounds.Top, Right = bounds.Right, Bottom = bounds.Bottom };
                int x, y;
                if (GetDpiForMonitor(MonitorFromRect(ref box, 2), 0, out x, out y) == 0 && x > 0) dpi = x;
            }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }
            float screen = dpi / 96f;
            float fit = Math.Max(0.75f, Math.Min(1f, Math.Min(bounds.Width / (1280f * screen), bounds.Height / (720f * screen))));
            return (float)Math.Round(screen * fit * 4) / 4f;
        }

        internal void Settle(Rectangle bounds)
        {
            float wanted = ScaleFor(bounds);
            bool fresh = !up;
            if (!up || bounds != game || wanted != scale || placed != GameHud.Offset)
            {
                game = bounds;
                placed = GameHud.Offset;
                if (wanted != scale || paint == null) Build(wanted);
                Rectangle area = Screen.FromRectangle(bounds).Bounds;
                float tall = area.Height / 1080f;
                corner = new Point(area.Left + (int)Math.Round((Inset + placed.X) * tall) - HudPaint.Edge(scale), area.Top + (int)Math.Round((Inset + placed.Y) * tall) - HudPaint.Edge(scale));
            }
            if (fresh)
            {
                up = true;
                busy.Reset();
                frames = 0;
                dockedAt = clock.Elapsed.TotalSeconds;
                life.Start(dockedAt);
            }
            Draw();
            if (!Visible) Show();
            Lift();
            Wake();
        }

        internal void Away()
        {
            if (!up) return;
            up = false;
            life.Stop();
            ticks.Stop();
            if (Visible) Hide();
        }

        private void Build(float wanted)
        {
            Drop();
            scale = wanted;
            paint = new HudPaint(scale);
            Size size = paint.Canvas;
            DibHead head = new DibHead { Size = Marshal.SizeOf(typeof(DibHead)), Width = size.Width, Height = -size.Height, Planes = 1, Bits = 32 };
            dc = CreateCompatibleDC(IntPtr.Zero);
            dib = CreateDIBSection(dc, ref head, 0, out bits, IntPtr.Zero, 0);
            old = SelectObject(dc, dib);
            canvas = new Bitmap(size.Width, size.Height, size.Width * 4, PixelFormat.Format32bppPArgb, bits);
            pen = Graphics.FromImage(canvas);
        }

        private void Drop()
        {
            if (pen != null) { pen.Dispose(); pen = null; }
            if (canvas != null) { canvas.Dispose(); canvas = null; }
            if (dc != IntPtr.Zero)
            {
                SelectObject(dc, old);
                DeleteObject(dib);
                DeleteDC(dc);
                dc = IntPtr.Zero;
            }
            if (paint != null) { paint.Dispose(); paint = null; }
        }

        internal void Wake()
        {
            if (!up) return;
            ticks.Stop();
            ticks.Interval = 1;
            ticks.Start();
        }

        private void Step()
        {
            if (!up) { ticks.Stop(); return; }
            double now = clock.Elapsed.TotalSeconds;
            busy.Start();
            if (life.Step(now)) Draw();
            busy.Stop();
            double next = life.Next(now);
            ticks.Interval = Math.Max(15, Math.Min(4000, (int)(next * 1000)));
        }

        private void Draw()
        {
            if (paint == null) return;
            Size used = paint.Draw(pen, life.Scene);
            Native.Point destination = new Native.Point(corner.X, corner.Y);
            Native.Point origin = new Native.Point(0, 0);
            Native.Size size = new Native.Size(used.Width, used.Height);
            Native.Blend blend = new Native.Blend { Operation = 0, Flags = 0, Alpha = 255, Format = 1 };
            UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size, dc, ref origin, 0, ref blend, 2);
            shown = used;
            frames++;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                up = false;
                ticks.Dispose();
                Drop();
            }
            base.Dispose(disposing);
        }
    }

    // The game Gaming Mode is on.
    internal sealed class GameSeen
    {
        internal IntPtr Window;
        internal int Process;
        internal string Name = "";
        internal Rectangle Bounds;
    }

    // Finds the game in front from its window, its title and where it's installed. Nothing here touches a game's memory.
    internal static class GameFinder
    {
        [StructLayout(LayoutKind.Sequential)] private struct Box { internal int Left, Top, Right, Bottom; }

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int process);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);

        private static readonly Regex Stores = new Regex(@"\\steamapps\\common\\|\\Epic Games\\(?!Launcher\\|Epic Online)|\\GOG Games\\|\\GOG Galaxy\\Games\\|\\XboxGames\\(?!Minecraft Launcher\\)|\\Riot Games\\(?!Riot Client\\)|\\Ubisoft Game Launcher\\games\\|\\(?:EA Games|Origin Games)\\|\\Rockstar Games\\(?!(?:Launcher|Social Club|Rockstar Games Social Club)\\)|\\Amazon Games\\Library\\|\\itch\\apps\\" +
            @"|\\World of Warcraft\\(?:_(?:retail|classic(?:_era)?|ptr|xptr|beta)_\\)?Wow(?:Classic|B|T)?\.exe$|\\Overwatch\\(?:_retail_\\)?Overwatch\.exe$|\\Diablo IV\\Diablo IV\.exe$|\\Diablo III\\(?:x64\\)?Diablo III(?:64)?\.exe$|\\Diablo II Resurrected\\D2R\.exe$|\\StarCraft II\\Versions\\Base\d+\\SC2(?:_x64)?\.exe$", RegexOptions.IgnoreCase);
        private static readonly Regex NotGames = new Regex(@"^(chrome|msedge|firefox|brave|opera|vivaldi|iexplore|arc|vlc|mpv|mpc-hc(64)?|potplayer(mini)?(64)?|wmplayer|video\.ui|microsoft\.media\.player|netflix|powerpnt|obs(32|64)|streamlabs( desktop)?|tiktok live studio|discord|slack|ms-teams|teams|telegram|whatsapp|signal|zoom|skype|claude|chatgpt|explorer|spotify|aimp|foobar2000|winamp|musicbee|steam|steamwebhelper|epicgameslauncher|galaxyclient|minecraftlauncher|riotclientservices|riotclientux|riotclientuxrender|ubisoftconnect|upc|uplay|uplaywebcore|eadesktop|ealauncher|eabackgroundservice|origin|originwebhelperservice|battle\.net|blizzardbrowser|agent|(?:world of warcraft|overwatch|diablo (?:ii|iii|iv)) launcher|launcher|launcherpatcher|socialclubhelper|rockstarservice|rockstarerrorhandler|rgsclauncher|amazongames|itch|itch-setup|butler|code|cursor|devenv|rider64|idea64|notepad(\+\+)?|sublime_text|winword|excel|aseprite|blender|godot(?:[._-].*)?|gdevelop|unity|unrealeditor|sc2editor(?:_x64)?|worldedit(?:64)?|windowsterminal|cmd|powershell|pwsh)$", RegexOptions.IgnoreCase);

        // A game's window: one of her known titles, anything installed by a game store, or a window with no title bar that fills its
        // monitor. Never a browser, player, launcher or editor, whose titles name games all the time.
        internal static bool IsGame(Sight sight, string path, bool fills)
        {
            if (sight == null || sight.Window == IntPtr.Zero || NotGame(sight.Program)) return false;
            return Computer.GameWindow(sight) || Stores.IsMatch(path ?? "") || fills;
        }

        internal static bool NotGame(string program) { return NotGames.IsMatch(program ?? ""); }

        internal static string PathOf(int process)
        {
            IntPtr handle = OpenProcess(0x1000, false, process);
            if (handle == IntPtr.Zero) return "";
            try
            {
                StringBuilder name = new StringBuilder(1024);
                int size = name.Capacity;
                return QueryFullProcessImageName(handle, 0, name, ref size) ? name.ToString() : "";
            }
            finally { CloseHandle(handle); }
        }

        private static bool Fills(IntPtr window, Rectangle bounds)
        {
            if (Native.Exclusive()) return true;
            return !Native.Titled(window) && (Native.Gaming() || bounds.Contains(Screen.FromRectangle(bounds).Bounds));
        }

        // The game in front, or null.
        internal static GameSeen Front()
        {
            Sight sight = Computer.Glimpse();
            if (sight == null || sight.Window == IntPtr.Zero) return null;
            int process;
            Native.GetWindowThreadProcessId(sight.Window, out process);
            if (!IsGame(sight, PathOf(process), Fills(sight.Window, sight.Bounds))) return null;
            return new GameSeen { Window = sight.Window, Process = process, Name = sight.Program ?? "", Bounds = sight.Bounds };
        }

        // Its process is running and its window is still there. A game that swaps its splash window for the real one is found
        // again in front, and one Windows won't let her ask about goes by its window alone.
        internal static bool Alive(GameSeen game)
        {
            if (!IsWindow(game.Window)) return false;
            try { using (Process running = Process.GetProcessById(game.Process)) return !running.HasExited; }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (System.ComponentModel.Win32Exception) { return true; }
        }

        internal static bool Exclusive(GameSeen game) { return Native.Exclusive() && Native.GetForegroundWindow() == game.Window; }

        // Where a window is now, or nothing while it's minimized or gone.
        internal static Rectangle Frame(IntPtr window)
        {
            Box box;
            if (!GetWindowRect(window, out box) || box.Left <= -30000) return Rectangle.Empty;
            return Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom);
        }
    }

    // Where her HUD sits in each game, as saved in `name=x,y;` pairs, in pixels of a 1080p game. Cyberpunk's health bar sits in her
    // corner, so she starts under it there.
    internal static class GameSpots
    {
        internal const int Step = 40;

        internal static Point Built(string game) { return string.Equals(game, "Cyberpunk2077", StringComparison.OrdinalIgnoreCase) ? new Point(0, 96) : Point.Empty; }

        internal static Point Find(string saved, string game)
        {
            foreach (string pair in (saved ?? "").Split(';'))
            {
                string[] parts = pair.Split('=');
                if (parts.Length != 2 || !string.Equals(parts[0], game, StringComparison.OrdinalIgnoreCase)) continue;
                string[] spot = parts[1].Split(',');
                int x, y;
                if (spot.Length == 2 && int.TryParse(spot[0], out x) && int.TryParse(spot[1], out y)) return new Point(x, y);
            }
            return Built(game);
        }

        internal static string Keep(string saved, string game, Point spot)
        {
            List<string> kept = new List<string>();
            foreach (string pair in (saved ?? "").Split(';'))
                if (pair.Length > 0 && !pair.StartsWith(game + "=", StringComparison.OrdinalIgnoreCase)) kept.Add(pair);
            if (spot != Built(game)) kept.Add(game + "=" + spot.X + "," + spot.Y);
            return string.Join(";", kept.ToArray());
        }

        // A step of "move the hud down", kept on the screen.
        internal static Point Nudged(Point spot, string way)
        {
            int x = spot.X + (way == "left" ? -Step : way == "right" ? Step : 0), y = spot.Y + (way == "up" ? -Step : way == "down" ? Step : 0);
            return new Point(Math.Max(0, Math.Min(1400, x)), Math.Max(0, Math.Min(760, y)));
        }
    }

    // Gaming Mode's rules, with no window in them so her smoke test can walk them: off, armed with no game up yet, or docked on one.
    internal sealed class GameDock
    {
        internal enum Stage { Off, Armed, Docked }

        // Seconds to wait for another game in front once this one's gone. A launcher hands over to its game, which can take a
        // while to open a window, and it's the short stays that are launchers.
        private const double GoneWait = 3, LauncherWait = 20, LauncherLife = 120;
        private const string ArmedLine = "Gaming Mode's armed. I'll dock as soon as a game's up.";
        private const string ArmedOut = "Gaming Mode's armed. I'll watch as soon as a game's up.";
        private readonly HashSet<string> warned = new HashSet<string>();
        private double dockedAt, goneAt = -1;
        // She came in by herself and nobody's heard about it.
        private bool quiet;
        internal Stage State { get; private set; }
        internal GameSeen Game { get; private set; }
        internal Func<GameSeen> Find = delegate { return null; };
        internal Func<GameSeen, bool> Alive = delegate { return false; };
        internal Func<GameSeen, bool> Exclusive = delegate { return false; };
        internal Action<GameSeen> Dock = delegate { };
        internal Action Undock = delegate { };
        internal Action Changed = delegate { };
        internal Action<string> Say = delegate { };
        internal Func<string> Name = delegate { return "choom"; };
        internal Func<bool> Hud = delegate { return true; };
        internal Func<bool> Stays = delegate { return false; };

        internal bool Asked { get { return State != Stage.Off && !quiet; } }

        internal string On(double now)
        {
            if (quiet) { quiet = false; return Landing(Game); }
            if (State != Stage.Off) return "Gaming Mode's already on.";
            GameSeen found = Find();
            if (found != null) return Land(found, now);
            State = Stage.Armed;
            Changed();
            return Hud() ? ArmedLine : ArmedOut;
        }

        internal string Off()
        {
            if (State == Stage.Off) return "Gaming Mode's already off.";
            bool on = State == Stage.Docked;
            State = Stage.Off;
            Game = null;
            goneAt = -1;
            quiet = false;
            if (on) Undock();
            Changed();
            return "Gaming Mode off.";
        }

        private string Land(GameSeen game, double now)
        {
            State = Stage.Docked;
            Game = game;
            dockedAt = now;
            goneAt = -1;
            Dock(game);
            Changed();
            return Landing(game);
        }

        private string Landing(GameSeen game) { return "Gaming Mode on, " + Name() + (Warn(game) ? ". " + Borderless : (Hud() ? ". Docking top-left until you close the game." : ". I'll stay out here with you until you close the game.")); }

        // On by default (Lachy, 31 Oct): a game in front takes her in with nothing said, unless she can't show over it.
        internal void Notice(double now)
        {
            if (State != Stage.Off) return;
            GameSeen found = Find();
            if (found == null) return;
            quiet = true;
            string line = Land(found, now);
            if (line.EndsWith(Borderless, StringComparison.Ordinal)) Say(line);
        }

        private string Borderless { get { return Hud() ? BorderlessHud : "That game's in exclusive full screen, so I can't show over it. Switch it to borderless and you'll see me."; } }
        private const string BorderlessHud = "That game's in exclusive full screen, so I can't show over it. Switch it to borderless and I'll dock top-left.";

        // True the first time this game is seen in exclusive full screen.
        private bool Warn(GameSeen game) { return Exclusive(game) && warned.Add(game.Name.ToLowerInvariant()); }

        // About once a second while she's on: an armed Gaming Mode docks on a game that's come up, and a game that's gone ends it,
        // unless another game has taken over from it.
        internal void Check(double now)
        {
            if (State == Stage.Off) return;
            if (State == Stage.Armed)
            {
                GameSeen found = Find();
                if (found != null) Say(Land(found, now));
                return;
            }
            if (Alive(Game))
            {
                goneAt = -1;
                if (Warn(Game)) Say(Borderless);
                return;
            }
            GameSeen next = Find();
            if (next != null)
            {
                Game = next;
                goneAt = -1;
                Dock(next);
                if (Warn(next)) Say(Borderless);
                return;
            }
            if (goneAt < 0) goneAt = now;
            if (now - goneAt < (goneAt - dockedAt < LauncherLife ? LauncherWait : GoneWait)) return;
            bool told = !quiet;
            Off();
            if (told) Say("GG, " + Name() + (Stays() ? "." : ". Gaming Mode off."));
        }
    }

    // Gaming Mode in her window. Docked she counts as hidden, so her body, bubble, pill, panel and every habit that hides with
    // her stay away, but what she says, hears and works on goes to the HUD instead, and she keeps her seat for when she's back.
    internal sealed partial class Companion
    {
        private const int GameKeyId = 0x4347;
        private const double HeardSpeech = 0.004;
        private readonly GameDock gaming = new GameDock();
        private bool docked, dockHid, gameKey;
        private double gameCheck;
        private Job jobDock;
        private string dockTalk, dockWords = "";

        internal bool GamingOn { get { return gaming.State != GameDock.Stage.Off; } }

        // On unless they've turned it off. With no game up there's nothing for it to do, so it only runs while one is.
        internal bool GamingWanted { get { return GamingOn || !preferences.NoGaming; } }

        private void WantGaming(bool on)
        {
            if (preferences.NoGaming != on) return;
            preferences.NoGaming = !on;
            SavePreferences();
            mainframe.Sync();
        }

        // Gaming Mode is them asking her to watch, so she reacts at least when something happens, HUD or no HUD.
        private int GamePace { get { return Math.Max(CommentPace, GamingOn ? Commentary.Events : Commentary.Off); } }

        internal string GameKeyName { get { return KeyName(preferences.GameMods, preferences.GameVk); } }

        private void SetUpGaming()
        {
            GameHud.Attach(this);
            Hands.GamingMode = GamingTool;
            gaming.Find = GameFinder.Front;
            gaming.Alive = GameFinder.Alive;
            gaming.Exclusive = GameFinder.Exclusive;
            gaming.Dock = DockOn;
            gaming.Undock = LeaveDock;
            gaming.Changed = delegate { CheckFullScreen(FullScreen()); mainframe.Sync(); };
            gaming.Say = GamingSay;
            // Lachy, 31 Oct: for now Gaming Mode keeps her normal self, body, panel and all, and the HUD stays out of it.
            gaming.Hud = delegate { return testing; };
            gaming.Stays = delegate { return !testing && !preferences.NoGaming; };
            gaming.Name = delegate { return string.IsNullOrWhiteSpace(Brain.Nickname) ? "choom" : Brain.Nickname; };
        }

        // Her tool, which her brain calls from its own thread.
        private string GamingTool(string action) { return InvokeRequired ? (string)Invoke(new Func<string, string>(GamingMode), action) : GamingMode(action); }

        // "on", "off", or which way to move her HUD: up, down, left, right or reset.
        internal string GamingMode(string action)
        {
            if (action == "on") { WakeRest(true); WantGaming(true); return gaming.On(clock.Elapsed.TotalSeconds); }
            if (action == "off")
            {
                bool wanted = GamingWanted;
                WantGaming(false);
                string left = gaming.Off();
                return wanted ? "Gaming Mode off." : left;
            }
            if (!docked) return "My HUD isn't up right now.";
            string game = gaming.Game.Name;
            Point spot = action == "reset" ? GameSpots.Built(game) : GameSpots.Nudged(GameHud.Offset, action);
            GameHud.Offset = spot;
            preferences.GameSpots = GameSpots.Keep(preferences.GameSpots, game, spot);
            SavePreferences();
            GameHud.Show(gaming.Game.Bounds);
            return action == "reset" ? "Put my HUD back." : "Moved my HUD " + action + ".";
        }

        // Her key and her Mainframe's switch.
        internal void ToggleGaming() { GamingSay(GamingMode(GamingWanted ? "off" : "on")); }

        // A line of her own in her usual voice. If she's busy answering them, it only shows, and hidden she keeps it to herself.
        private void GamingSay(string line)
        {
            if (quitting || IsDisposed || (hidden && !docked)) return;
            if (talkState == TalkState.Thinking || working != null) { Say(line); return; }
            heard = "gaming mode";
            box.Prompt = "";
            Answer(++asking, line, TalkState.Talking);
        }

        // A job that's playing a game brings Gaming Mode up by itself once the game's in front. It goes the same way as any other
        // Gaming Mode, when the game closes or they turn it off. Once a job, so turning it off holds until her next one.
        private void DockForJob(Job running)
        {
            if (running == null || running == jobDock) return;
            if (GamingOn) { jobDock = running; return; }
            if (gaming.Find() == null) return;
            jobDock = running;
            WakeRest(true);
            GamingSay(gaming.On(clock.Elapsed.TotalSeconds));
        }

        private void CheckGame(double now)
        {
            bool playing = PlayingGame;
            bool watching = !testing && !preferences.NoGaming && !napAsked && !Asleep;
            if ((!GamingOn && !playing && !watching) || now < gameCheck) return;
            gameCheck = now + 1;
            if (watching) gaming.Notice(now);
            if (playing) DockForJob(job);
            if (!GamingOn) return;
            gaming.Check(now);
            if (!docked) return;
            GameHud.Status(!preferences.Silent, shushed);
            GameHud.Lift();
            Rectangle at = GameFinder.Frame(gaming.Game.Window);
            if (at.IsEmpty || at == gaming.Game.Bounds) return;
            gaming.Game.Bounds = at;
            GameHud.Show(at);
        }

        // She glitches off the desktop, and the HUD comes up over the game.
        private void DockOn(GameSeen game)
        {
            if (!gaming.Hud()) return;
            GameHud.Offset = GameSpots.Find(preferences.GameSpots, game.Name);
            if (docked) { GameHud.Show(game.Bounds); return; }
            if (outro != null) { Action next = outro; outro = null; next(); }
            napAsked = false;
            napping = false;
            hideAsked = false;
            dockHid = hidden && (!fullScreenHidden || manualHidden);
            fullScreenHidden = false;
            if (!hidden)
            {
                StopRide();
                LeavePanel(true);
                EndWalk();
                if (frame != null && IsHandleCreated && preferences.Motion && !instant) shatter.Play(new Bitmap(frame), anchor, Screen.FromPoint(new Point(anchor.X + Width / 2, anchor.Y + Height / 2)).Bounds, true, preferences.Size);
                Static(true, Cue.Leave);
            }
            falling = false;
            dropAfter = false;
            pendingClick = false;
            clickDelay.Stop();
            docked = true;
            hidden = true;
            dockTalk = null;
            Hide();
            pill.Hide();
            typer.Hide();
            if (box.Visible) box.Hide();
            // A job that's already running moves to the HUD, as one started in Gaming Mode does.
            taskPanel.Stop();
            visibilityItem.Text = "Hide Capri";
            GameHud.Status(!preferences.Silent, shushed);
            GameHud.Show(game.Bounds);
            UpdateAnimation();
            mainframe.Sync();
        }

        // The HUD goes and she's back where she was, on her seat or ledge if she was on one, or hidden if she was hidden.
        private void LeaveDock()
        {
            if (!docked) return;
            docked = false;
            dockTalk = null;
            dockWords = "";
            GameHud.Hide();
            manualHidden = dockHid;
            fullScreenHidden = preferences.HideFullScreen && !GamingOn && FullScreen();
            hidden = manualHidden || fullScreenHidden;
            visibilityItem.Text = hidden ? "Show Capri" : "Hide Capri";
            if (!hidden)
            {
                if (seat == null && !hanging) anchor = KeepOnScreen(anchor);
                Location = anchor;
                Play(hanging ? dangle : lying ? laydownGlitch : seat != null ? perchGlitch : instant || !preferences.Motion ? grin : arrive);
                Show();
                RenderFrame();
                UpdateAnimation();
                Static(true, Cue.Arrive);
                if (seat == null && !hanging) DropSoon();
            }
            mainframe.Sync();
        }

        // Her frame timer while docked, which only runs while she's saying or working something out.
        private void DockTick(double now)
        {
            GameHud.Mouth(MouthCell(now, clip));
            UpdateTalk(now);
            if (speech == null && talkState == TalkState.Off && !(chatting && speaker.Busy)) frames.Stop();
        }

        // What her bubble would show, in the HUD's boxes. THINKING is up while she works out a reply, with their words if they
        // said something, and a job's status stays in the box while the task panel stays away.
        private void DockTalk()
        {
            bool theirs = box.Prompt == "> you: ";
            string now = talkState + "|" + (working ?? "") + "|" + (theirs ? heard : "") + "|" + reply;
            if (now != dockTalk)
            {
                dockTalk = now;
                if (talkState == TalkState.Thinking)
                {
                    GameHud.Mood(HudMood.Focus);
                    // A line she's saying stays up until it's been read.
                    if (working != null) { if (asideShown == null || clock.Elapsed.TotalSeconds >= asideUntil) GameHud.Job(working); }
                    else GameHud.Hear(theirs ? heard : "", true);
                }
                else if (reply != null)
                {
                    GameHud.Mood(HudMood.Idle);
                    GameHud.Say(reply, pace);
                }
            }
            if (talkState != TalkState.Thinking || working != null) GameHud.Hold(3);
        }

        // Her reply is finished and gone from the box too.
        private void DockQuiet()
        {
            dockTalk = null;
            dockWords = "";
            GameHud.Clear();
            GameHud.Mood(HudMood.Idle);
        }

        // Their mic while voice chat is on: the bars, and LISTENING once they speak, unless it's her own line coming through.
        private void DockLevel(double value)
        {
            if (local && preferences.ListenCapri && !pushing) return;
            GameHud.Level(value);
            if (value >= HeardSpeech && talkState == TalkState.Off && speech == null) GameHud.Hear(dockWords, false);
        }

        // What they've said so far, from Windows' speech recognition, which shows words as they come.
        private void DockWords(string words)
        {
            if (local && preferences.ListenCapri && !pushing) return;
            if (!docked || talkState != TalkState.Off) return;
            dockWords = words ?? "";
            GameHud.Hear(dockWords, false);
        }

        // Her animations have no body to play on while she's docked, so the ones that say something go to the HUD.
        private string DockAnimate(string name)
        {
            if (name == "grin" || name == "dance") { GameHud.Mood(HudMood.Happy); return "Grinning."; }
            if (name == "glitch") { GameHud.Mood(HudMood.Surprise); return "Glitched."; }
            return name == "blink" ? "Blinked." : name == "idle" ? "Back to my idle." : name == "stop_dancing" ? "I wasn't dancing." : "I don't have an animation called " + name + ".";
        }
    }

    // Everything drawn. What never moves is drawn once per scale into layers, and each frame lays her eyes between them, so
    // an idle frame is two blits and a few paths.
    internal sealed class HudPaint : IDisposable
    {
        internal static readonly Color Skin = Color.FromArgb(13, 247, 114);
        internal static readonly Color Hair = Color.FromArgb(253, 1, 80);
        internal static readonly Color Ink = Color.FromArgb(0, 0, 6);
        internal static readonly Color IrisDeep = Color.FromArgb(3, 29, 94);
        internal static readonly Color Iris = Color.FromArgb(0, 80, 225);
        internal static readonly Color IrisLit = Color.FromArgb(9, 105, 251);
        internal static readonly Color Spark = Color.FromArgb(248, 250, 254);
        internal static readonly Color Fringe = Color.FromArgb(150, 20, 240);
        private static readonly RectangleF Crop = new RectangleF(146, 151, 100, 129);
        private const float EyeLeftX = 170, EyeLeftY = 207.5f, EyeRightX = 212.3f, EyeRightY = 198.2f;
        private static readonly Color[] Palette = { Hair, Skin, Ink, Spark, Color.FromArgb(6, 85, 235), Fringe };
        private static readonly Color Backing = Color.FromArgb(96, 96, 104);
        private const int Bleed = 14;
        private const int PanelWide = 356;
        private const int EyesTall = 200;
        private const int TalkWide = 248;
        private const int TalkTall = 316;
        private const int StripTall = 24;
        private const int BoxTall = 172;
        private const int Rim = 7;
        private const int Columns = 42;
        private const int MostRows = 6;
        private readonly float s;
        private readonly float line;
        private readonly Bitmap eyesUnder, eyesOver, talkUnder, talkOver, torn, strip, box;
        private readonly Graphics tornPen, boxPen;
        private string stripKey, boxKey;
        private int boxUsed;
        private readonly Font labelFont, smallFont;

        internal static int Edge(float scale) { return (int)Math.Round(Bleed * scale); }

        internal HudPaint(float scale)
        {
            s = scale;
            line = Math.Max(1, (float)Math.Round(s));
            labelFont = Neon.Display(14f * s, GraphicsUnit.Pixel);
            smallFont = Neon.Display(12.5f * s, GraphicsUnit.Pixel);
            eyesUnder = Layer(PanelWide, EyesTall);
            eyesOver = Layer(PanelWide, EyesTall);
            talkUnder = Layer(TalkWide, TalkTall);
            talkOver = Layer(TalkWide, TalkTall);
            torn = Layer(PanelWide, TalkTall);
            strip = new Bitmap(Px(PanelWide + Bleed * 2), Px(StripTall + Bleed), PixelFormat.Format32bppPArgb);
            box = new Bitmap(Px(PanelWide + Bleed * 2), Px(BoxTall + Bleed), PixelFormat.Format32bppPArgb);
            tornPen = Graphics.FromImage(torn);
            boxPen = Graphics.FromImage(box);
            using (Graphics graphics = Graphics.FromImage(eyesUnder)) Plate(graphics, PanelWide, EyesTall, false);
            using (Graphics graphics = Graphics.FromImage(eyesOver)) { Fine(graphics); EyesOver(graphics); Glass(graphics, PanelWide, EyesTall); }
            using (Graphics graphics = Graphics.FromImage(talkUnder)) { Plate(graphics, TalkWide, TalkTall, true); Fine(graphics); Portrait(graphics); }
            using (Graphics graphics = Graphics.FromImage(talkOver)) { Fine(graphics); Glass(graphics, TalkWide, TalkTall); }
        }

        internal Size Canvas { get { return new Size(Px(PanelWide + Bleed * 2), Px(TalkTall + StripTall + BoxTall + 12 + Bleed * 2)); } }

        private int Px(float value) { return (int)Math.Round(value * s); }

        private static void Fine(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.CompositingQuality = CompositingQuality.HighSpeed;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        private Bitmap Layer(int wide, int tall) { return new Bitmap(Px(wide + Bleed * 2), Px(tall + Bleed * 2), PixelFormat.Format32bppPArgb); }

        private RectangleF Hull(int wide, int tall) { return new RectangleF(Px(Bleed), Px(Bleed), Px(wide), Px(tall)); }

        private RectangleF Screen(int wide, int tall)
        {
            RectangleF hull = Hull(wide, tall);
            return new RectangleF(hull.X + Px(Rim), hull.Y + Px(Rim), hull.Width - Px(Rim) * 2, hull.Height - Px(Rim) * 2);
        }

        private GraphicsPath Outer(RectangleF area) { return Neon.Cut(area, 22 * s, 8 * s, 22 * s, 8 * s); }

        private GraphicsPath Inner(RectangleF area) { return Neon.Cut(area, 17 * s, 5 * s, 17 * s, 5 * s); }

        // A panel's body: the soft glow, her ground and the screen's own hull.
        private void Plate(Graphics graphics, int wide, int tall, bool portrait)
        {
            Fine(graphics);
            RectangleF hull = Hull(wide, tall);
            using (GraphicsPath shape = Outer(hull))
            {
                float[] sizes = { 20, 14, 9, 5 };
                int[] alphas = { 8, 12, 20, 30 };
                for (int i = 0; i < sizes.Length; i++)
                    using (Pen glow = new Pen(Color.FromArgb(alphas[i], CityLook.Cyan), sizes[i] * s) { LineJoin = LineJoin.Round })
                        graphics.DrawPath(glow, shape);
                using (Brush ground = new SolidBrush(Color.FromArgb(250, CityLook.Ground))) graphics.FillPath(ground, shape);
            }
            using (GraphicsPath inner = Inner(Screen(wide, tall)))
            using (Brush fill = new SolidBrush(portrait ? CityLook.Deep : Skin))
                graphics.FillPath(fill, inner);
        }

        // What sits in front of her eyes: her jaw and teeth, the crest and her hair with its stars.
        private void EyesOver(Graphics graphics)
        {
            RectangleF art = Screen(PanelWide, EyesTall);
            GraphicsState state = graphics.Save();
            using (GraphicsPath inner = Inner(art)) graphics.SetClip(inner);
            float k = art.Width / 1000f;
            Func<float[], float, float, PointF[]> corners = delegate(float[] points, float dx, float dy)
            {
                PointF[] found = new PointF[points.Length / 2];
                for (int i = 0; i < found.Length; i++) found[i] = new PointF(art.Left + (points[i * 2] + dx) * k, art.Top + (points[i * 2 + 1] + dy) * k);
                return found;
            };
            Action<Color, float, float, float[]> shard = delegate(Color colour, float dx, float dy, float[] points)
            {
                using (Brush fill = new SolidBrush(colour)) graphics.FillPolygon(fill, corners(points, dx, dy));
            };
            float[] jaw = { -10, 490, 1010, 436, 1010, 560, -10, 560 };
            shard(Fringe, 0, -5, jaw);
            shard(Ink, 0, 0, jaw);
            float[] teeth = new float[26];
            for (int i = 0; i < 13; i++)
            {
                float x = 216 + i * 49;
                teeth[i * 2] = x;
                teeth[i * 2 + 1] = 490 - (x + 10) * 0.053f + (i % 2 == 0 ? 8 : 40);
            }
            shard(Spark, 0, 0, teeth);
            float[] crest = { 766, -10, 1010, -10, 1010, 96, 950, 42, 962, 112, 880, 48, 884, 92, 812, 28 };
            shard(Fringe, -5, 5, crest);
            shard(Ink, 0, 0, crest);
            float[] tip = { 906, -10, 1010, -10, 1010, 50 };
            float[] mass = { -10, -10, 404, -10, 332, 46, 352, 60, 214, 114, 230, 132, 116, 152, 64, 238, 40, 178, -10, 256 };
            float[] bang = { 452, -10, 644, -10, 568, 58, 532, 134, 494, 304, 464, 162, 424, 112 };
            shard(Ink, 13, 11, mass);
            shard(Ink, 12, 9, bang);
            shard(Hair, 0, 0, mass);
            shard(Hair, 0, 0, bang);
            shard(Hair, 0, 0, tip);
            using (GraphicsPath hair = new GraphicsPath())
            {
                hair.AddPolygon(corners(mass, 0, 0));
                hair.AddPolygon(corners(bang, 0, 0));
                hair.AddPolygon(corners(tip, 0, 0));
                Stars(graphics, hair, art, 120, 37);
            }
            graphics.Restore(state);
        }

        // White specks over her hair, as in her art.
        private void Stars(Graphics graphics, GraphicsPath hair, RectangleF area, int count, int seed)
        {
            GraphicsState state = graphics.Save();
            using (Region inside = new Region(hair)) graphics.IntersectClip(inside);
            Random stars = new Random(seed);
            using (Brush white = new SolidBrush(Color.FromArgb(240, Spark)))
                for (int i = 0; i < count; i++)
                {
                    float x = area.Left + (float)stars.NextDouble() * area.Width, y = area.Top + (float)stars.NextDouble() * area.Height;
                    float r = (0.55f + (float)Math.Pow(stars.NextDouble(), 3) * 1.25f) * s;
                    graphics.FillEllipse(white, x - r, y - r, r * 2, r * 2);
                }
            graphics.Restore(state);
        }

        // The glass over a screen: scan lines, a soft inner shadow, the thin inner frame and the rim round it all.
        private void Glass(Graphics graphics, int wide, int tall)
        {
            RectangleF hull = Hull(wide, tall), screen = Screen(wide, tall);
            GraphicsState state = graphics.Save();
            using (GraphicsPath inner = Inner(screen)) graphics.SetClip(inner);
            using (Brush scan = new SolidBrush(Color.FromArgb(18, 0, 0, 0)))
                for (float y = screen.Top; y < screen.Bottom; y += 3 * line) graphics.FillRectangle(scan, screen.Left, y, screen.Width, line);
            for (int i = 0; i < 4; i++)
                using (Pen shadow = new Pen(Color.FromArgb(52 - i * 12, 0, 0, 0), (8 - i * 2) * s))
                using (GraphicsPath inner = Inner(screen))
                    graphics.DrawPath(shadow, inner);
            graphics.Restore(state);
            using (GraphicsPath band = new GraphicsPath())
            using (GraphicsPath outer = Outer(hull))
            using (GraphicsPath inner = Inner(screen))
            using (Brush ground = new SolidBrush(CityLook.Ground))
            {
                band.FillMode = FillMode.Alternate;
                band.AddPath(outer, false);
                band.AddPath(inner, false);
                graphics.FillPath(ground, band);
            }
            RectangleF frame = RectangleF.Inflate(screen, -Px(4), -Px(4));
            frame = new RectangleF(frame.X + line / 2, frame.Y + line / 2, frame.Width - line, frame.Height - line);
            using (GraphicsPath thin = Neon.Cut(frame, 14 * s, 3 * s, 14 * s, 3 * s))
            using (Pen pen = new Pen(Color.FromArgb(150, CityLook.Cyan), line))
                graphics.DrawPath(pen, thin);
            RectangleF edge = new RectangleF(hull.X + line / 2, hull.Y + line / 2, hull.Width - line, hull.Height - line);
            using (GraphicsPath outer = Outer(edge))
            using (Pen pen = new Pen(CityLook.DeepEdge, line))
                graphics.DrawPath(pen, outer);
            float big = 22 * s, along = 58 * s, down = 28 * s, heavy = Math.Max(2, (float)Math.Round(2 * s));
            RectangleF hot = RectangleF.Inflate(edge, -heavy / 2 + line / 2, -heavy / 2 + line / 2);
            using (Pen stroke = new Pen(CityLook.Hot, heavy))
            {
                graphics.DrawLines(stroke, new[] { new PointF(hot.Left, hot.Top + big + down), new PointF(hot.Left, hot.Top + big), new PointF(hot.Left + big, hot.Top), new PointF(hot.Left + big + along, hot.Top) });
                graphics.DrawLines(stroke, new[] { new PointF(hot.Right, hot.Bottom - big - down), new PointF(hot.Right, hot.Bottom - big), new PointF(hot.Right - big, hot.Bottom), new PointF(hot.Right - big - along, hot.Bottom) });
            }
            float w = Px(9), h = Math.Max(1, (float)Math.Round(2 * s)), x = screen.Right - Px(13), y2 = screen.Bottom - Px(52);
            for (int i = 0; i < 6; i++)
                using (Brush fade = new SolidBrush(Color.FromArgb(230 - i * 28, CityLook.Cyan))) graphics.FillRectangle(fade, x, y2 + i * Px(5), w, h);
            using (Brush tab = new SolidBrush(CityLook.Cyan)) graphics.FillRectangle(tab, hull.Left + hull.Width / 2 - Px(34), hull.Top, Px(68), Math.Max(2, Px(3)));
        }

        // Her eyes, in the panel's own art space.
        private void Eyes(Graphics graphics, HudScene scene, RectangleF art, float k)
        {
            float wide = scene.Wide, narrow = scene.Narrow;
            float open = Math.Max(0, scene.Open) * (1 - 0.40f * narrow) * (1 + 0.14f * wide);
            GraphicsState state = graphics.Save();
            using (GraphicsPath inner = Inner(art)) graphics.SetClip(inner);
            Eye(graphics, new PointF(art.Left + 230 * k, art.Top + 280 * k), 142 * k, 108 * k, -8, true, open, narrow, wide, scene);
            Eye(graphics, new PointF(art.Left + 750 * k, art.Top + 262 * k), 161 * k, 128 * k, -9, false, open, narrow, wide, scene);
            graphics.Restore(state);
        }

        private static PointF Turn(float x, float y, float cos, float sin, PointF centre) { return new PointF(centre.X + x * cos - y * sin, centre.Y + x * sin + y * cos); }

        // One eye: a black almond with a sharp point at each end, a spike off each lid, a thin ring, her blue iris and its white
        // spark, then skin over whatever the lids hide. `left` mirrors it for the eye on the left.
        private void Eye(Graphics graphics, PointF centre, float a, float b, float tilt, bool left, float open, float narrow, float wide, HudScene scene)
        {
            float side = left ? -1 : 1;
            double angle = tilt * side * Math.PI / 180;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
            Func<float, float, PointF> at = delegate(float x, float y) { return Turn(x * a * side, y * b, cos, sin, centre); };
            float rise = Math.Min(1.3f, open);
            float low = 0.12f + 0.88f * Math.Min(1, open);
            float brow = narrow * 0.72f;
            PointF tipIn = at(-1.52f, 0.62f), tipOut = at(1.58f, -0.40f);
            PointF up1 = at(-0.82f, 0.62f - 1.9f * rise + brow), up2 = at(0.66f, -0.40f - 1.6f * rise + brow * 0.3f);
            PointF low1 = at(0.86f, -0.40f + 1.5f * low), low2 = at(-0.70f, 0.62f + 0.82f * low);
            using (GraphicsPath socket = new GraphicsPath())
            {
                socket.AddBezier(tipIn, up1, up2, tipOut);
                socket.AddBezier(tipOut, low1, low2, tipIn);
                socket.CloseFigure();
                using (Matrix slip = new Matrix())
                {
                    slip.Translate(-2.2f * s, 2.2f * s);
                    using (GraphicsPath ghost = (GraphicsPath)socket.Clone())
                    using (Brush fringe = new SolidBrush(Fringe))
                    {
                        ghost.Transform(slip);
                        graphics.FillPath(fringe, ghost);
                    }
                }
                using (Brush ink = new SolidBrush(Ink)) graphics.FillPath(ink, socket);
            }
            float radius = b * 0.56f * (1 - 0.26f * wide + 0.04f * narrow);
            float lookX = scene.LookX * a * 0.44f, lookY = scene.LookY * b * 0.30f;
            PointF ring = new PointF(centre.X + lookX * 0.55f, centre.Y + lookY * 0.55f);
            PointF iris = new PointF(centre.X + lookX, centre.Y + lookY);
            Pupil(graphics, ring, iris, radius, radius * (1.44f + 0.26f * wide), narrow);
            using (GraphicsPath lid = new GraphicsPath())
            using (Brush skin = new SolidBrush(Skin))
            {
                lid.AddBezier(tipIn, up1, up2, tipOut);
                lid.AddLine(tipOut, at(1.7f, -3.4f));
                lid.AddLine(at(1.7f, -3.4f), at(-1.52f, -3.4f));
                lid.CloseFigure();
                graphics.FillPath(skin, lid);
                lid.Reset();
                lid.AddBezier(tipOut, low1, low2, tipIn);
                lid.AddLine(tipIn, at(-1.52f, 2.8f));
                lid.AddLine(at(-1.52f, 2.8f), at(1.7f, 2.8f));
                lid.CloseFigure();
                graphics.FillPath(skin, lid);
            }
            using (Brush ink = new SolidBrush(Ink))
            {
                float reach = Math.Max(0.25f, Math.Min(1, open * 1.1f)) * (1 - 0.45f * narrow);
                PointF topOne = Curve(tipIn, up1, up2, tipOut, 0.215f), topTwo = Curve(tipIn, up1, up2, tipOut, 0.285f), topMid = Curve(tipIn, up1, up2, tipOut, 0.25f);
                graphics.FillPolygon(ink, new[] { topOne, topTwo, new PointF(topMid.X + (at(-1.42f, -0.9f).X - at(-0.96f, 0).X) * reach, topMid.Y + (at(-1.42f, -0.9f).Y - at(-0.96f, 0).Y) * reach) });
                PointF underOne = Curve(tipOut, low1, low2, tipIn, 0.73f), underTwo = Curve(tipOut, low1, low2, tipIn, 0.80f), underMid = Curve(tipOut, low1, low2, tipIn, 0.765f);
                graphics.FillPolygon(ink, new[] { underOne, underTwo, new PointF(underMid.X + (at(-1.2f, 0.95f).X - at(-0.86f, 0).X) * reach, underMid.Y + (at(-1.2f, 0.95f).Y - at(-0.86f, 0).Y) * reach) });
            }
        }

        // Her ring, iris and spark. Focused, four ticks close in round the ring.
        private void Pupil(Graphics graphics, PointF ring, PointF iris, float radius, float hoop, float narrow)
        {
            using (Pen pen = new Pen(Skin, Math.Max(1.2f, 1.6f * s)))
            {
                graphics.DrawEllipse(pen, ring.X - hoop, ring.Y - hoop, hoop * 2, hoop * 2);
                if (narrow > 0.5f)
                    for (int i = 0; i < 4; i++)
                    {
                        float x = (float)Math.Cos(Math.PI / 4 + i * Math.PI / 2), y = (float)Math.Sin(Math.PI / 4 + i * Math.PI / 2);
                        graphics.DrawLine(pen, ring.X + x * hoop, ring.Y + y * hoop, ring.X + x * (hoop - 5 * s), ring.Y + y * (hoop - 5 * s));
                    }
            }
            Blue(graphics, iris, radius, narrow > 0.5f ? 0.25f : 0.34f);
        }

        // Her iris in its three blues with the white spark on top.
        private static void Blue(Graphics graphics, PointF iris, float radius, float spark)
        {
            Disc(graphics, IrisDeep, iris, radius);
            Disc(graphics, Iris, iris, radius * 0.86f);
            Disc(graphics, IrisLit, iris, radius * 0.60f);
            Disc(graphics, Spark, new PointF(iris.X - radius * 0.06f, iris.Y - radius * 0.08f), radius * spark);
            Disc(graphics, Color.FromArgb(200, Spark), new PointF(iris.X + radius * 0.42f, iris.Y + radius * 0.40f), Math.Max(0.8f, radius * 0.075f));
        }

        private static void Disc(Graphics graphics, Color colour, PointF centre, float radius)
        {
            using (Brush fill = new SolidBrush(colour)) graphics.FillEllipse(fill, centre.X - radius, centre.Y - radius, radius * 2, radius * 2);
        }

        private static PointF Curve(PointF a, PointF b, PointF c, PointF d, float t)
        {
            float u = 1 - t, w1 = u * u * u, w2 = 3 * u * u * t, w3 = 3 * u * t * t, w4 = t * t * t;
            return new PointF(a.X * w1 + b.X * w2 + c.X * w3 + d.X * w4, a.Y * w1 + b.Y * w2 + c.Y * w3 + d.Y * w4);
        }

        // Her portrait from face.png itself. Scaling her up would blur her, so each pixel of a fine bicubic copy snaps to the
        // nearest of her flat colours and the result is averaged back down, which keeps her edges sharp at any size.
        internal static Bitmap Snapped(RectangleF crop, int wide, int tall)
        {
            const int fine = 3;
            int width = wide * fine, high = tall * fine;
            int[] pixels = new int[width * high];
            using (System.IO.Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Sprite.face.png"))
            using (Bitmap face = new Bitmap(stream))
            using (Bitmap big = new Bitmap(width, high, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(big))
                using (ImageAttributes clamp = new ImageAttributes())
                {
                    graphics.Clear(Backing);
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    clamp.SetWrapMode(WrapMode.TileFlipXY);
                    graphics.DrawImage(face, new Rectangle(0, 0, width, high), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel, clamp);
                }
                BitmapData read = big.LockBits(new Rectangle(0, 0, width, high), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try { for (int y = 0; y < high; y++) Marshal.Copy(new IntPtr(read.Scan0.ToInt64() + (long)y * read.Stride), pixels, y * width, width); }
                finally { big.UnlockBits(read); }
            }
            int count = Palette.Length;
            int[] reds = new int[count + 1], greens = new int[count + 1], blues = new int[count + 1];
            for (int i = 0; i < count; i++) { reds[i] = Palette[i].R; greens[i] = Palette[i].G; blues[i] = Palette[i].B; }
            reds[count] = Backing.R;
            greens[count] = Backing.G;
            blues[count] = Backing.B;
            int[] sumRed = new int[wide * tall], sumGreen = new int[wide * tall], sumBlue = new int[wide * tall], sumAlpha = new int[wide * tall];
            for (int y = 0; y < high; y++)
                for (int x = 0; x < width; x++)
                {
                    int pixel = pixels[y * width + x], red = (pixel >> 16) & 255, green = (pixel >> 8) & 255, blue = pixel & 255, best = 0, closest = int.MaxValue;
                    for (int i = 0; i <= count; i++)
                    {
                        int dr = red - reds[i], dg = green - greens[i], db = blue - blues[i], far = dr * dr + dg * dg + db * db;
                        if (far < closest) { closest = far; best = i; }
                    }
                    if (best == count) continue;
                    int cell = (y / fine) * wide + x / fine;
                    sumRed[cell] += reds[best];
                    sumGreen[cell] += greens[best];
                    sumBlue[cell] += blues[best];
                    sumAlpha[cell] += 255;
                }
            int[] done = new int[wide * tall];
            int samples = fine * fine;
            for (int i = 0; i < done.Length; i++) done[i] = ((sumAlpha[i] / samples) << 24) | ((sumRed[i] / samples) << 16) | ((sumGreen[i] / samples) << 8) | (sumBlue[i] / samples);
            Bitmap her = new Bitmap(wide, tall, PixelFormat.Format32bppPArgb);
            BitmapData write = her.LockBits(new Rectangle(0, 0, wide, tall), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
            try { for (int y = 0; y < tall; y++) Marshal.Copy(done, y * wide, new IntPtr(write.Scan0.ToInt64() + (long)y * write.Stride), wide); }
            finally { her.UnlockBits(write); }
            return her;
        }

        private void Portrait(Graphics graphics)
        {
            RectangleF art = Screen(TalkWide, TalkTall);
            GraphicsState state = graphics.Save();
            using (GraphicsPath inner = Inner(art)) graphics.SetClip(inner);
            using (Pen hatch = new Pen(Color.FromArgb(70, CityLook.DeepEdge), line))
                for (float x = art.Left - art.Height; x < art.Right; x += 9 * s) graphics.DrawLine(hatch, x, art.Bottom, x + art.Height, art.Top);
            using (Bitmap her = Snapped(Crop, (int)art.Width, (int)art.Height)) graphics.DrawImageUnscaled(her, (int)art.Left, (int)art.Top);
            Func<float, float, PointF> at = Places();
            float q = art.Width / Crop.Width;
            Disc(graphics, Ink, at(EyeLeftX, EyeLeftY), 7.2f * q);
            Disc(graphics, Ink, at(EyeRightX, EyeRightY), 9f * q);
            graphics.Restore(state);
        }

        private Func<float, float, PointF> Places()
        {
            RectangleF art = Screen(TalkWide, TalkTall);
            float q = art.Width / Crop.Width;
            return delegate(float x, float y) { return new PointF(art.Left + (x - Crop.X) * q, art.Top + (y - Crop.Y) * q); };
        }

        // Her eyes and mouth over the portrait, at their places in face.png.
        private void Face(Graphics graphics, HudScene scene)
        {
            Func<float, float, PointF> at = Places();
            float q = Screen(TalkWide, TalkTall).Width / Crop.Width;
            Watch(graphics, scene, at(EyeLeftX, EyeLeftY), 5.6f * q, 1.3f * q, new[] { at(161, 198), at(170, 196.5f), at(177, 197), at(178.5f, 203), at(181.5f, 208), at(181.5f, 218), at(172, 219), at(160, 216), at(159, 207) });
            Watch(graphics, scene, at(EyeRightX, EyeRightY), 7.2f * q, 1.7f * q, new[] { at(205, 184), at(213, 177.5f), at(230.6f, 178.8f), at(230, 188), at(227, 198), at(224.5f, 208), at(222, 212), at(208, 213), at(195, 215.5f), at(198, 200), at(201, 190) });
            if (scene.Grin) Grin(graphics, at, q);
            else if (scene.Mouth > 0) Lips(graphics, scene.Mouth, at, q);
        }

        // One side of a polygon cut by the line where its distance along (nx, ny) is `lid`, and the two ends of that cut.
        private static PointF[] Cut(PointF[] shape, float lid, float nx, float ny, bool under, out PointF one, out PointF two)
        {
            List<PointF> kept = new List<PointF>();
            one = two = PointF.Empty;
            bool found = false;
            for (int i = 0; i < shape.Length; i++)
            {
                PointF here = shape[i], next = shape[(i + 1) % shape.Length];
                float first = (here.X * nx + here.Y * ny - lid) * (under ? -1 : 1), second = (next.X * nx + next.Y * ny - lid) * (under ? -1 : 1);
                if (first <= 0) kept.Add(here);
                if ((first <= 0) == (second <= 0)) continue;
                float t = first / (first - second);
                PointF cross = new PointF(here.X + (next.X - here.X) * t, here.Y + (next.Y - here.Y) * t);
                kept.Add(cross);
                if (found) two = cross;
                else { one = cross; found = true; }
            }
            return kept.ToArray();
        }

        // Her iris moves inside the ring of her art. Lids come down over the whole socket along the tilt of her face, as in
        // her blink, and meet low down with a lash at the seam. A grin pushes the lower lid up.
        private void Watch(Graphics graphics, HudScene scene, PointF centre, float radius, float room, PointF[] socket)
        {
            Blue(graphics, new PointF(centre.X + scene.LookX * room, centre.Y + scene.LookY * room * 0.7f), radius, 0.34f);
            float shut = 1 - Math.Max(0, Math.Min(1, scene.Open));
            if (shut < 0.02f && !scene.Grin) return;
            const float nx = 0.2f, ny = 0.98f;
            float top = float.MaxValue, bottom = float.MinValue;
            foreach (PointF corner in socket)
            {
                float along = corner.X * nx + corner.Y * ny;
                top = Math.Min(top, along);
                bottom = Math.Max(bottom, along);
            }
            float tall = bottom - top, seam = top + tall * 0.72f;
            float upper = top + (seam - top) * shut, lower = bottom - (bottom - seam) * Math.Max(shut, scene.Grin ? 0.5f : 0);
            PointF oneEnd, twoEnd;
            using (Brush skin = new SolidBrush(Skin))
            using (Pen lash = new Pen(Ink, Math.Max(1.5f, 1.9f * s)) { StartCap = LineCap.Triangle, EndCap = LineCap.Triangle })
            {
                if (shut >= 0.02f)
                {
                    PointF[] cover = Cut(socket, upper, nx, ny, false, out oneEnd, out twoEnd);
                    if (cover.Length > 2) graphics.FillPolygon(skin, cover);
                    if (upper > top) graphics.DrawLine(lash, oneEnd, twoEnd);
                }
                if (shut >= 0.02f || scene.Grin)
                {
                    PointF[] cheek = Cut(socket, lower, nx, ny, true, out oneEnd, out twoEnd);
                    if (cheek.Length > 2) graphics.FillPolygon(skin, cheek);
                    if (lower < bottom) graphics.DrawLine(lash, oneEnd, twoEnd);
                }
            }
        }

        // The zigzag of her teeth in face.png, as a white line with mitred points. `drop` lowers it and `squeeze` draws its ends in.
        private static void Jag(Graphics graphics, Func<float, float, PointF> at, float q, float drop, float squeeze, float wide)
        {
            float[] jag = { 167.5f, 227, 173, 234.5f, 177.5f, 226, 183.5f, 237, 190.5f, 223.5f, 195.5f, 234.5f, 202.5f, 221, 206.5f, 227.5f, 215.5f, 217 };
            PointF[] points = new PointF[jag.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = at(192 + (jag[i * 2] - 192) * squeeze, jag[i * 2 + 1] + drop);
            using (Pen white = new Pen(Spark, wide * q) { LineJoin = LineJoin.MiterClipped, MiterLimit = 6, StartCap = LineCap.Triangle, EndCap = LineCap.Triangle })
                graphics.DrawLines(white, points);
        }

        // Her mouth while she talks: the top row of teeth stays and a second row drops with her jaw, over black, with her tongue
        // in the widest shapes.
        private void Lips(Graphics graphics, int shape, Func<float, float, PointF> at, float q)
        {
            float[] gaps = { 0, 2.6f, 5.2f, 8, 11 };
            float gap = gaps[Math.Max(0, Math.Min(4, shape))];
            using (Brush ink = new SolidBrush(Ink))
                graphics.FillPolygon(ink, new[] { at(166.5f, 226), at(219, 215.2f), at(215, 222), at(209, 230 + gap * 0.7f), at(200, 240 + gap), at(184, 243 + gap), at(171, 238 + gap * 0.7f), at(166, 231) });
            Jag(graphics, at, q, 0, 1, 3.1f);
            if (gap > 6)
                using (Brush tongue = new SolidBrush(Hair))
                    graphics.FillPolygon(tongue, new[] { at(178, 233 + gap * 0.62f), at(190, 230 + gap * 0.62f), at(204, 228 + gap * 0.5f), at(197, 234 + gap * 0.74f), at(184, 237 + gap * 0.74f) });
            Jag(graphics, at, q, gap + 1.2f, 0.92f, 3.1f);
        }

        // Her grin: a white wedge with black notches biting down from under the green, as in her widest mouth.
        private void Grin(Graphics graphics, Func<float, float, PointF> at, float q)
        {
            using (Brush ink = new SolidBrush(Ink))
                graphics.FillPolygon(ink, new[] { at(166.5f, 226), at(219, 215.2f), at(215, 222), at(209, 236), at(198, 248), at(184, 248), at(171, 238), at(166, 231) });
            float[] wedge = { 169, 227.5f, 174, 233, 178.5f, 225.5f, 184, 231, 190.5f, 223, 196, 228.5f, 203, 220.5f, 208, 225.5f, 217.5f, 216.5f, 210, 230, 196, 245, 182, 240 };
            PointF[] points = new PointF[wedge.Length / 2];
            for (int i = 0; i < points.Length; i++) points[i] = at(wedge[i * 2], wedge[i * 2 + 1]);
            using (Brush white = new SolidBrush(Spark)) graphics.FillPolygon(white, points);
        }

        // A small eye of hers for the burst: an almond, an iris and a spark, turned any way and open by `open`.
        private void Tiny(Graphics graphics, PointF centre, float size, float degrees, float open)
        {
            double angle = degrees * Math.PI / 180;
            float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle), high = size * 0.52f * open;
            using (GraphicsPath shape = new GraphicsPath())
            using (Brush ink = new SolidBrush(Ink))
            {
                PointF inner = Turn(-size * 1.3f, 0, cos, sin, centre), outer = Turn(size * 1.3f, 0, cos, sin, centre);
                shape.AddBezier(inner, Turn(-size * 0.5f, -high * 1.5f, cos, sin, centre), Turn(size * 0.5f, -high * 1.5f, cos, sin, centre), outer);
                shape.AddBezier(outer, Turn(size * 0.5f, high * 1.2f, cos, sin, centre), Turn(-size * 0.5f, high * 1.2f, cos, sin, centre), inner);
                shape.CloseFigure();
                graphics.FillPath(ink, shape);
            }
            if (open < 0.3f) return;
            float radius = Math.Min(size * 0.42f, high * 0.72f);
            Disc(graphics, Iris, centre, radius);
            Disc(graphics, Spark, new PointF(centre.X - radius * 0.1f, centre.Y - radius * 0.12f), radius * 0.36f);
        }

        // Many small eyes opening across the panel on a big moment.
        private void Swarm(Graphics graphics, HudScene scene, RectangleF art)
        {
            Random eyes = new Random(scene.Seed);
            GraphicsState state = graphics.Save();
            using (GraphicsPath inner = Inner(art)) graphics.SetClip(inner);
            for (int i = 0; i < 22; i++)
            {
                float x = art.Left + (float)eyes.NextDouble() * art.Width, y = art.Top + (float)eyes.NextDouble() * art.Height;
                float size = (7 + (float)eyes.NextDouble() * 15) * s, turn = (float)(eyes.NextDouble() * 70 - 35), late = (float)eyes.NextDouble() * 0.5f;
                float open = Math.Max(0, Math.Min(1, (scene.Burst - late) / 0.4f));
                if (open > 0) Tiny(graphics, new PointF(x, y), size, turn, open);
            }
            graphics.Restore(state);
        }

        // Bands of a picture slid sideways, for her glitches.
        private void Slide(Graphics graphics, Bitmap source, int width, int high, float amount, int seed, int x, int y)
        {
            Random bands = new Random(seed);
            int row = 0;
            while (row < high)
            {
                int band = Math.Min(high - row, Math.Max(1, (int)((3 + bands.Next(13)) * s)));
                int shift = (int)((bands.NextDouble() * 2 - 1) * 16 * s * amount);
                graphics.DrawImage(source, new Rectangle(x + shift, y + row, width, band), new Rectangle(0, row, width, band), GraphicsUnit.Pixel);
                row += band;
            }
        }

        // The hit: the frame turned red and white, torn sideways, with white shards through it.
        private void Struck(Graphics graphics, HudScene scene, int wide, int tall, int x, int y)
        {
            Random shards = new Random(scene.Seed);
            float amount = scene.Hit;
            int width = Px(wide + Bleed * 2), high = Px(tall + Bleed * 2);
            float bright = 2.3f;
            ColorMatrix red = new ColorMatrix(new[]
            {
                new float[] { 0, 0.3f * bright, 0.3f * bright, 0, 0 },
                new float[] { 0, 0.59f * bright, 0.59f * bright, 0, 0 },
                new float[] { 0, 0.11f * bright, 0.11f * bright, 0, 0 },
                new float[] { 0, 0, 0, 1, 0 },
                new float[] { 0.99f, -0.42f, -0.30f, 0, 1 }
            });
            using (ImageAttributes tone = new ImageAttributes())
            {
                tone.SetColorMatrix(red);
                int row = 0;
                while (row < high)
                {
                    int band = Math.Min(high - row, Math.Max(1, (int)((4 + shards.Next(18)) * s)));
                    int shift = (int)((shards.NextDouble() * 2 - 1) * 22 * s * amount);
                    bool hot = shards.NextDouble() < 0.35f + 0.55f * amount;
                    if (hot) graphics.DrawImage(torn, new Rectangle(x + shift, y + row, width, band), 0, row, width, band, GraphicsUnit.Pixel, tone);
                    else graphics.DrawImage(torn, new Rectangle(x + shift, y + row, width, band), new Rectangle(0, row, width, band), GraphicsUnit.Pixel);
                    row += band;
                }
            }
            RectangleF hull = Hull(wide, tall);
            hull.Offset(x, y);
            GraphicsState state = graphics.Save();
            graphics.SetClip(hull);
            float hitX = hull.Left + (float)shards.NextDouble() * hull.Width, hitY = hull.Top + (float)shards.NextDouble() * hull.Height;
            using (Brush white = new SolidBrush(Color.FromArgb((int)(245 * Math.Min(1, amount * 1.6f)), Spark)))
            using (Brush hair = new SolidBrush(Color.FromArgb((int)(245 * Math.Min(1, amount * 1.6f)), Hair)))
                for (int i = 0; i < 8; i++)
                {
                    double angle = shards.NextDouble() * Math.PI * 2, spread = 0.06 + shards.NextDouble() * 0.12;
                    float reach = (60 + (float)shards.NextDouble() * 150) * s, start = (float)shards.NextDouble() * 34 * s;
                    PointF[] shard =
                    {
                        new PointF(hitX + (float)Math.Cos(angle) * start, hitY + (float)Math.Sin(angle) * start),
                        new PointF(hitX + (float)Math.Cos(angle + spread) * reach * 0.6f, hitY + (float)Math.Sin(angle + spread) * reach * 0.6f),
                        new PointF(hitX + (float)Math.Cos(angle) * reach, hitY + (float)Math.Sin(angle) * reach),
                        new PointF(hitX + (float)Math.Cos(angle - spread) * reach * 0.5f, hitY + (float)Math.Sin(angle - spread) * reach * 0.5f)
                    };
                    graphics.FillPolygon(i % 3 == 2 ? hair : white, shard);
                }
            graphics.Restore(state);
            using (GraphicsPath edge = Outer(hull))
            using (Pen flash = new Pen(Color.FromArgb((int)(255 * amount), Hair), 3 * s))
                graphics.DrawPath(flash, edge);
        }

        // The thin strip under the panel: who she is, the mode and her voice.
        private void Strip(HudScene scene)
        {
            string key = scene.Voice + "|" + scene.Quiet;
            if (key == stripKey) return;
            stripKey = key;
            using (Graphics graphics = Graphics.FromImage(strip))
            {
                Fine(graphics);
                graphics.Clear(Color.Transparent);
                RectangleF bar = new RectangleF(Px(Bleed), 0, Px(PanelWide), Px(StripTall));
                using (GraphicsPath shape = Neon.Cut(bar, 0, 0, 9 * s, 9 * s))
                {
                    using (Pen glow = new Pen(Color.FromArgb(24, CityLook.Cyan), 7 * s) { LineJoin = LineJoin.Round }) graphics.DrawPath(glow, shape);
                    using (Brush fill = new SolidBrush(Color.FromArgb(246, CityLook.Ground))) graphics.FillPath(fill, shape);
                    using (Pen edge = new Pen(CityLook.DeepEdge, line)) graphics.DrawPath(edge, shape);
                }
                float high = bar.Height, y = bar.Top + (high - smallFont.GetHeight(graphics)) / 2, x = bar.Left + Px(5);
                RectangleF chip = new RectangleF(x, bar.Top + Px(4), Px(50), high - Px(8));
                using (GraphicsPath plate = Neon.Cut(chip, 0, 0, 4 * s, 0))
                using (Brush fill = new SolidBrush(CityLook.Rose))
                    graphics.FillPath(fill, plate);
                Words(graphics, "CAPRI", CityLook.OnInk, chip.Left + Px(7), y);
                x = chip.Right + Px(9);
                x = Words(graphics, "GAMING MODE", CityLook.White, x, y) + Px(9);
                Disc(graphics, CityLook.Cyan, new PointF(x, bar.Top + high / 2), 2.6f * s);
                Words(graphics, "ON", CityLook.Cyan, x + Px(7), y);
                string voice = scene.Voice ? "VOICE ON" : "VOICE OFF";
                float wide = graphics.MeasureString(voice, smallFont, PointF.Empty, StringFormat.GenericTypographic).Width;
                float right = bar.Right - Px(14) - wide;
                Words(graphics, voice, scene.Voice ? CityLook.Cyan : CityLook.Faint, right, y);
                if (scene.Quiet)
                {
                    float quiet = graphics.MeasureString("QUIET", smallFont, PointF.Empty, StringFormat.GenericTypographic).Width;
                    Words(graphics, "QUIET", CityLook.Rose, right - Px(12) - quiet, y);
                }
            }
        }

        private float Words(Graphics graphics, string text, Color colour, float x, float y)
        {
            using (Brush ink = new SolidBrush(colour)) graphics.DrawString(text, smallFont, ink, x, y, StringFormat.GenericTypographic);
            return x + graphics.MeasureString(text, smallFont, PointF.Empty, StringFormat.GenericTypographic).Width;
        }

        // The box under the strip: LISTENING with their words, THINKING, her line as it types out, or a job's status.
        private int Box(HudScene scene)
        {
            string key = scene.Box + "|" + scene.Words + "|" + scene.Line + "|" + scene.Revealed + "|" + scene.Caret + "|" + scene.Level + "|" + (int)(scene.Beat * 12);
            if (key == boxKey) return boxUsed;
            boxKey = key;
            Graphics graphics = boxPen;
            Fine(graphics);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using (Brush clear = new SolidBrush(Color.Transparent)) graphics.FillRectangle(clear, 0, 0, box.Width, box.Height);
            graphics.CompositingMode = CompositingMode.SourceOver;
            bool saying = scene.Box == HudBox.Reply || scene.Box == HudBox.Job;
            string all = scene.Line + scene.Words;
            using (Font text = Look.Mono(14 * s, FontStyle.Regular, GraphicsUnit.Pixel, all))
            using (Font dim = Look.Mono(13 * s, FontStyle.Italic, GraphicsUnit.Pixel, all))
            {
                float cw = Look.CharWidth(text), row = 19 * s, pad = Px(20);
                string body = saying ? scene.Line : "> you: " + scene.Words;
                List<string> lines = Look.Wrap(body, Columns);
                if (lines.Count == 0) lines.Add("");
                int[] takes = Look.Takes(lines, body, saying ? scene.Revealed : body.Length);
                int last = 0;
                for (int i = 0; i < lines.Count; i++) if (takes[i] > 0) last = i;
                int first = Math.Max(0, last - (MostRows - 1)), rows = Math.Min(MostRows, last - first + 1);
                string you = scene.Box == HudBox.Reply && scene.Words.Length > 0 ? "> you: " + scene.Words : null;
                if (you != null && Look.Cells(you) > Columns) you = Look.Cut(you, Columns - 3) + "...";
                float tall = Px(11) + Px(16) + (you != null ? row : 0) + Px(5) + row * rows + Px(11);
                boxUsed = (int)Math.Ceiling(tall) + Px(6);
                RectangleF panel = new RectangleF(Px(Bleed), Px(6), Px(PanelWide), (float)Math.Round(tall));
                using (GraphicsPath shape = Neon.Cut(panel, 12 * s, 4 * s, 12 * s, 4 * s))
                {
                    using (Pen glow = new Pen(Color.FromArgb(26, CityLook.Cyan), 8 * s) { LineJoin = LineJoin.Round }) graphics.DrawPath(glow, shape);
                    using (Brush fill = new SolidBrush(Color.FromArgb(240, CityLook.Ground))) graphics.FillPath(fill, shape);
                    using (Pen edge = new Pen(CityLook.DeepEdge, line)) graphics.DrawPath(edge, shape);
                }
                float big = 12 * s, heavy = Math.Max(2, (float)Math.Round(2 * s));
                using (Pen stroke = new Pen(CityLook.Hot, heavy))
                {
                    graphics.DrawLines(stroke, new[] { new PointF(panel.Left, panel.Top + big + 14 * s), new PointF(panel.Left, panel.Top + big), new PointF(panel.Left + big, panel.Top), new PointF(panel.Left + big + 34 * s, panel.Top) });
                    graphics.DrawLines(stroke, new[] { new PointF(panel.Right, panel.Bottom - big - 14 * s), new PointF(panel.Right, panel.Bottom - big), new PointF(panel.Right - big, panel.Bottom), new PointF(panel.Right - big - 34 * s, panel.Bottom) });
                }
                using (Brush bar = new SolidBrush(scene.Box == HudBox.Listening ? CityLook.Cyan : Look.Red)) graphics.FillRectangle(bar, panel.Left + Px(9), panel.Top + Px(12), Math.Max(2, Px(3)), panel.Height - Px(24));
                float x = panel.Left + pad, y = panel.Top + Px(11);
                Header(graphics, scene, x, y, panel);
                y += Px(16);
                if (you != null)
                {
                    using (Brush brush = new SolidBrush(Look.Dim)) Look.Draw(graphics, you, dim, brush, x, y);
                    y += row;
                }
                y += Px(5);
                using (Brush ink = new SolidBrush(!saying && scene.Box == HudBox.Thinking ? Look.Dim : CityLook.White))
                using (Brush caret = new SolidBrush(CityLook.Cyan))
                {
                    PointF end = new PointF(x, y);
                    for (int i = first; i < lines.Count && i < first + MostRows; i++)
                    {
                        if (takes[i] <= 0) continue;
                        string shown = lines[i].Substring(0, Math.Min(lines[i].Length, takes[i]));
                        float at = y + (i - first) * row;
                        Look.Draw(graphics, shown, text, ink, x, at);
                        end = new PointF(x + Look.Cells(shown) * cw, at);
                    }
                    if (scene.Caret) graphics.FillRectangle(caret, end.X + s, end.Y + s, cw * 0.8f, row - 5 * s);
                }
            }
            return boxUsed;
        }

        private void Header(Graphics graphics, HudScene scene, float x, float y, RectangleF panel)
        {
            string title = scene.Box == HudBox.Listening ? "LISTENING" : scene.Box == HudBox.Thinking ? "THINKING" : scene.Box == HudBox.Job ? "ON A JOB" : "CAPRI";
            Color colour = scene.Box == HudBox.Listening || scene.Box == HudBox.Job ? CityLook.Cyan : CityLook.Rose;
            if (scene.Box == HudBox.Listening) { Disc(graphics, Look.Red, new PointF(x + 4 * s, y + 8 * s), 4 * s); x += Px(14); }
            using (Brush ink = new SolidBrush(colour)) graphics.DrawString(title, labelFont, ink, x, y, StringFormat.GenericTypographic);
            float right = panel.Right - Px(16);
            if (scene.Box == HudBox.Listening)
            {
                int[] bars = { 3, 6, 9, 5, 7, 3, 5 };
                for (int i = 0; i < bars.Length; i++)
                {
                    float tall = Math.Max(2 * s, bars[i] * s * (0.25f + scene.Level));
                    using (Brush bar = new SolidBrush(scene.Level > 0.02f ? CityLook.Cyan : CityLook.DeepEdge))
                        graphics.FillRectangle(bar, right - (bars.Length - i) * Px(5), y + Px(8) - tall / 2, Math.Max(2, Px(3)), tall);
                }
            }
            else if (scene.Box == HudBox.Thinking || scene.Box == HudBox.Job)
            {
                int lit = (int)(scene.Beat * 6) % 6;
                for (int i = 0; i < 6; i++)
                    using (Brush cell = new SolidBrush(i == lit ? colour : Color.FromArgb(70, colour)))
                        graphics.FillRectangle(cell, right - (6 - i) * Px(10), y + Px(5), Px(7), Math.Max(2, Px(5)));
            }
        }

        // Draws one frame at the canvas' top left and says how much of it is in use.
        internal Size Draw(Graphics graphics, HudScene scene)
        {
            Fine(graphics);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using (Brush clear = new SolidBrush(Color.Transparent)) graphics.FillRectangle(clear, 0, 0, Canvas.Width, Canvas.Height);
            graphics.CompositingMode = CompositingMode.SourceOver;
            bool portrait = scene.Portrait >= 0.5f;
            int wide = portrait ? TalkWide : PanelWide, tall = portrait ? TalkTall : EyesTall;
            float swap = 1 - Math.Abs(scene.Portrait * 2 - 1);
            bool rough = scene.Hit > 0 || swap > 0 || scene.Boot < 1;
            Graphics pen = rough ? tornPen : graphics;
            if (rough)
            {
                Fine(pen);
                pen.CompositingMode = CompositingMode.SourceCopy;
                using (Brush wipe = new SolidBrush(Color.Transparent)) pen.FillRectangle(wipe, 0, 0, torn.Width, torn.Height);
                pen.CompositingMode = CompositingMode.SourceOver;
            }
            RectangleF art = Screen(wide, tall);
            if (portrait)
            {
                pen.DrawImageUnscaled(talkUnder, 0, 0);
                Face(pen, scene);
                pen.DrawImageUnscaled(talkOver, 0, 0);
            }
            else
            {
                pen.DrawImageUnscaled(eyesUnder, 0, 0);
                Eyes(pen, scene, art, art.Width / 1000f);
                pen.DrawImageUnscaled(eyesOver, 0, 0);
                if (scene.Burst > 0) Swarm(pen, scene, art);
            }
            int layerWide = Px(wide + Bleed * 2), layerHigh = Px(tall + Bleed * 2);
            if (scene.Hit > 0)
            {
                Random shake = new Random(scene.Seed + (int)(scene.Hit * 40));
                Struck(graphics, scene, wide, tall, (int)((shake.NextDouble() * 2 - 1) * 5 * s * scene.Hit), (int)((shake.NextDouble() * 2 - 1) * 4 * s * scene.Hit));
            }
            else if (scene.Boot < 1) Booting(graphics, scene, layerWide, layerHigh);
            else if (swap > 0) Slide(graphics, torn, layerWide, layerHigh, swap, scene.Seed + (int)(swap * 8), 0, 0);
            float ease = scene.Portrait * scene.Portrait * (3 - 2 * scene.Portrait);
            int top = Px(Bleed + EyesTall + (TalkTall - EyesTall) * ease + 3);
            if (scene.Boot >= 1)
            {
                Strip(scene);
                graphics.DrawImageUnscaled(strip, 0, top);
            }
            int bottom = top + Px(StripTall) + Px(Bleed);
            if (scene.Box != HudBox.None && scene.Boot >= 1)
            {
                int used = Box(scene);
                int y = top + Px(StripTall);
                if (scene.Fade >= 1) graphics.DrawImageUnscaled(box, 0, y);
                else
                    using (ImageAttributes fade = new ImageAttributes())
                    {
                        ColorMatrix alpha = new ColorMatrix { Matrix33 = scene.Fade };
                        fade.SetColorMatrix(alpha);
                        graphics.DrawImage(box, new Rectangle(0, y, box.Width, used + Px(Bleed)), 0, 0, box.Width, used + Px(Bleed), GraphicsUnit.Pixel, fade);
                    }
                bottom = y + used + Px(Bleed);
            }
            return new Size(Px(PanelWide + Bleed * 2), Math.Min(Canvas.Height, bottom));
        }

        // Docking: the panel draws in from the top with a bright line, torn while it settles.
        private void Booting(Graphics graphics, HudScene scene, int width, int high)
        {
            int reveal = (int)(high * Math.Min(1, scene.Boot * 1.25f));
            Slide(graphics, torn, width, reveal, 1 - scene.Boot, scene.Seed, 0, 0);
            using (Brush scan = new SolidBrush(CityLook.Cyan)) graphics.FillRectangle(scan, Px(Bleed), Math.Max(0, reveal - Math.Max(2, Px(2))), Px(PanelWide), Math.Max(2, Px(2)));
        }

        public void Dispose()
        {
            tornPen.Dispose();
            boxPen.Dispose();
            eyesUnder.Dispose();
            eyesOver.Dispose();
            talkUnder.Dispose();
            talkOver.Dispose();
            torn.Dispose();
            strip.Dispose();
            box.Dispose();
            labelFont.Dispose();
            smallFont.Dispose();
        }
    }
}
