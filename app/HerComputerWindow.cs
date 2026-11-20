using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class HerComputerWindow : Form, HerHands
    {
        internal static readonly Rectangle AppBox = new Rectangle(96, 66, 1088, 600);
        internal const int TabTall = 42, ToolTall = 46;
        internal static readonly Rectangle PageBox = HerComputer.Page;
        internal static readonly Rectangle AddressBox = new Rectangle(AppBox.Left + 112, AppBox.Top + TabTall + 8, AppBox.Width - 172, 30);
        internal const int DockWide = 44 + 46 * 5 + 22 * 4;
        internal static readonly Rectangle DockBox = new Rectangle(HerComputer.Width / 2 - DockWide / 2, 684, DockWide, 70);
        internal static readonly Rectangle CardBox = new Rectangle(280, 96, 720, 540);
        internal const int CardHead = 52;
        private static readonly Color Ground = Color.FromArgb(11, 7, 16), Plum = Color.FromArgb(22, 11, 18), Chrome = Color.FromArgb(42, 15, 29), Rose = Color.FromArgb(255, 93, 102), Blush = Color.FromArgb(255, 227, 234), Faded = Color.FromArgb(185, 150, 168), Mint = Color.FromArgb(61, 242, 138), Ink = Color.FromArgb(42, 20, 32), SoftInk = Color.FromArgb(160, 132, 143), Paper = Color.FromArgb(255, 250, 252), PaperHead = Color.FromArgb(255, 233, 240), Hot = Color.FromArgb(192, 23, 79);

        internal readonly HerComputer Computer;
        internal Func<string, string, string> Ask;
        internal Action<string> Tell;
        internal Func<bool> CanThink = delegate { return false; };
        internal Action<HerJob> HandOff;
        internal static bool Quiet;

        private readonly object gate = new object();
        private readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer();
        private readonly Random random = new Random();
        private readonly Dictionary<string, Image> art = new Dictionary<string, Image>();
        private readonly Font clockFont, bigFont, textFont, smallFont, boldFont, labelFont, monoFont;
        private PointF cursor = new PointF(700, 420), target = new PointF(700, 420);
        private string openApp, typingField, typed, status = "Idle", bubble, selectedNote, heldSticker;
        private DateTime bubbleUntil, rippleAt = DateTime.MinValue, handActiveAt = DateTime.MinValue;
        private const double HandBackSeconds = 30;
        private Image frame, perch, face, pointer;
        private Bitmap backdrop;
        private RectangleF controlButton;
        private HerBrowser web;
        private string webProblem;
        private readonly object startGate = new object();
        private volatile bool starting;
        private Thread taskThread, frameThread;
        private volatile bool closing, stopTask, inControl;
        private volatile string runningTask;
        private readonly object pumpGate = new object();
        private readonly List<KeyValuePair<RectangleF, Action>> jobHits = new List<KeyValuePair<RectangleF, Action>>();
        private string jobTyped;
        private int jobRepeat;
        private bool begun, forceClose, toldBackground;
        private DateTime pumpedAt = DateTime.MinValue;
        private float scale = 1f;
        private PointF offset;
        private bool quietShow;
        private DateTime watchedAt = DateTime.MinValue;
        private DateTime boomAt = DateTime.MinValue;
        private PointF boomSpot;
        private Bitmap boomScene, boomFrame;
        private readonly Random sparkle = new Random();
        internal const string BombSticker = "bomb";
        private const double Fuse = 1.6, GlitchEnd = 5.0, BoomEnd = 7.2;
        internal HerComputerWindow(HerComputer computer)
        {
            Computer = computer;
            Text = "Capri's computer";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(HerComputer.Width * 3 / 4, HerComputer.Height * 3 / 4);
            MinimumSize = new Size(560, 400);
            BackColor = Ground;
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { using (Stream chip = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (chip != null) Icon = new Icon(chip); } catch (Exception) { }
            perch = Load("Capri.Her.capri-perch.png", 0);
            face = Load("Capri.Her.capri-avatar.png", 0);
            pointer = Load("Capri.Cursor.png", 80);
            foreach (string app in HerComputer.Apps) Art("app-" + app);
            foreach (string site in HerComputer.Sites) Art("site-" + site);
            foreach (string kind in HerComputer.StickerKinds) Art("sticker-" + kind);
            Art("sticker-" + BombSticker);
            Art("boom");
            clockFont = FileEditor.Title(78f);
            bigFont = Neon.Type(28f, true, GraphicsUnit.Pixel);
            textFont = Neon.Type(17f, false, GraphicsUnit.Pixel);
            smallFont = Neon.Type(14f, false, GraphicsUnit.Pixel);
            boldFont = Neon.Type(17f, true, GraphicsUnit.Pixel);
            labelFont = Neon.Type(22f, false, GraphicsUnit.Pixel);
            monoFont = Look.Mono(13f, FontStyle.Regular);
            ticker.Interval = 33;
            ticker.Tick += delegate { Tick(); };
            Layout += delegate { Place(); };
            Place();
        }

        private static Image Load(string resource, int size)
        {
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if (stream == null) return null;
                    using (Image full = Image.FromStream(stream))
                    {
                        if (size <= 0 || full.Width <= size) return new Bitmap(full);
                        Bitmap small = new Bitmap(size, Math.Max(1, full.Height * size / full.Width), PixelFormat.Format32bppPArgb);
                        using (Graphics g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            g.DrawImage(full, new Rectangle(0, 0, small.Width, small.Height));
                        }
                        return small;
                    }
                }
            }
            catch (Exception) { return null; }
        }

        private Image Art(string name)
        {
            Image found;
            if (art.TryGetValue(name, out found)) return found;
            found = Load("Capri.Her." + name + ".png", 0);
            art[name] = found;
            return found;
        }

        protected override bool ShowWithoutActivation { get { return quietShow; } }

        internal void ShowQuietly()
        {
            quietShow = true;
            try { Show(); }
            finally { quietShow = false; }
        }

        internal void Watched() { watchedAt = DateTime.UtcNow; }

        private bool Seen { get { return WindowState != FormWindowState.Minimized || (DateTime.UtcNow - watchedAt).TotalSeconds < 3; } }

        internal bool Busy { get { return runningTask != null; } }
        internal string Doing { get { lock (gate) return status; } }
        internal string Showing { get { lock (gate) return openApp; } }
        internal bool InControl { get { return inControl; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Begin();
        }

        private void Begin()
        {
            if (begun) return;
            begun = true;
            ticker.Start();
            frameThread = new Thread(FrameLoop) { IsBackground = true, Name = "her computer frames" };
            frameThread.Start();
            ThreadPool.QueueUserWorkItem(delegate { Computer.RefreshMemories(); });
        }

        internal void RunHidden()
        {
            IntPtr made = Handle;
            if (made != IntPtr.Zero) Begin();
        }

        internal void CloseForGood()
        {
            forceClose = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!forceClose && e.CloseReason == CloseReason.UserClosing && (Busy || Computer.Jobs.Waiting > 0 || Computer.Jobs.Repeating().Count > 0))
            {
                e.Cancel = true;
                Hide();
                if (!toldBackground)
                {
                    toldBackground = true;
                    Action<string> tell = Tell;
                    if (tell != null) tell("I'll keep my computer running in the background for my jobs. Turn Her own computer off in Mainframe to shut it down.");
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            closing = true;
            stopTask = true;
            ticker.Stop();
            HerBrowser closingWeb;
            lock (gate) { closingWeb = web; web = null; }
            if (closingWeb != null) new Thread(delegate() { closingWeb.Dispose(); }) { IsBackground = false, Name = "her browser closing" }.Start();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticker.Dispose();
                foreach (Image image in art.Values) if (image != null) image.Dispose();
                foreach (IDisposable owned in new IDisposable[] { perch, face, pointer, backdrop, boomScene, boomFrame, clockFont, bigFont, textFont, smallFont, boldFont, labelFont, monoFont }) if (owned != null) owned.Dispose();
                lock (gate) if (frame != null) { frame.Dispose(); frame = null; }
            }
            base.Dispose(disposing);
        }

        private void Place()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            scale = Math.Min(ClientSize.Width / (float)HerComputer.Width, ClientSize.Height / (float)HerComputer.Height);
            offset = new PointF((ClientSize.Width - HerComputer.Width * scale) / 2, (ClientSize.Height - HerComputer.Height * scale) / 2);
            if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
        }

        private PointF Logical(Point screen) { return new PointF((screen.X - offset.X) / scale, (screen.Y - offset.Y) / scale); }

        private void Tick()
        {
            if (closing) return;
            lock (gate)
            {
                float dx = target.X - cursor.X, dy = target.Y - cursor.Y;
                float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                if (distance < 1.5f) cursor = target;
                else
                {
                    float step = Math.Max(6f, Math.Min(distance * 0.18f, 46f));
                    cursor = new PointF(cursor.X + dx / distance * Math.Min(step, distance), cursor.Y + dy / distance * Math.Min(step, distance));
                }
                if (bubble != null && DateTime.UtcNow > bubbleUntil) bubble = null;
            }
            if (inControl && (DateTime.UtcNow - handActiveAt).TotalSeconds > HandBackSeconds) HandBack();
            if (!Busy && (DateTime.UtcNow - pumpedAt).TotalSeconds > 15) { pumpedAt = DateTime.UtcNow; Pump(); }
            if (!Visible) return;
            Invalidate();
        }

        private void FrameLoop()
        {
            while (!closing)
            {
                HerBrowser current;
                string app;
                lock (gate) { current = web; app = openApp; }
                if (current != null && app == "browser" && current.Running && Seen)
                {
                    byte[] jpeg = current.Frame(72);
                    if (jpeg != null)
                    {
                        try
                        {
                            Image next;
                            using (MemoryStream stream = new MemoryStream(jpeg)) next = new Bitmap(Image.FromStream(stream));
                            Image old;
                            lock (gate) { old = frame; frame = next; }
                            if (old != null) old.Dispose();
                        }
                        catch (ArgumentException) { }
                    }
                    Thread.Sleep(220);
                }
                else Thread.Sleep(300);
            }
        }

        private HerBrowser Browser()
        {
            lock (startGate)
            {
                HerBrowser current;
                lock (gate) current = web;
                if (current != null && current.Running) return current;
                if (current != null) current.Dispose();
                HerBrowser started = new HerBrowser(Computer.BrowserFolder, PageBox.Width, PageBox.Height);
                string problem;
                Status("Starting my browser");
                lock (gate) { web = null; webProblem = null; }
                starting = true;
                try
                {
                    if (!started.Start(out problem))
                    {
                        started.Dispose();
                        Image stale;
                        lock (gate) { web = null; webProblem = problem; stale = frame; frame = null; }
                        if (stale != null) stale.Dispose();
                        Bubble(problem);
                        return null;
                    }
                }
                finally { starting = false; }
                lock (gate) { web = started; webProblem = null; }
                return started;
            }
        }

        private static readonly Rectangle BrowserBody = new Rectangle(AppBox.Left, AppBox.Top + TabTall, AppBox.Width, AppBox.Height - TabTall);

        private bool BrowserDown
        {
            get
            {
                HerBrowser current;
                lock (gate) current = web;
                return !starting && (current == null || !current.Running);
            }
        }

        internal string Start(string job) { return Start(job, ""); }

        internal string Start(string job, string after)
        {
            if (string.IsNullOrWhiteSpace(job)) return "What should I do on my computer?";
            string task;
            string repeat = HerJobs.RepeatOf(job, out task);
            if (task.Length == 0) return "What should I do on my computer?";
            bool wasBusy = Busy;
            HerJob added = Computer.Jobs.Add(task, repeat, after);
            if (added == null) return repeat.Length > 0 ? "I already have " + HerJobs.MostRepeats + " repeating jobs. Take one off in my Jobs app first." : "My job list is full. Let me get through some of it first.";
            if (Ask == null || !CanThink()) { Bubble("I need a brain for jobs. Pick one in Mainframe."); return "It's in my Jobs app, but I need a brain to do jobs on my computer. Pick one in Mainframe under BRAIN."; }
            Pump();
            if (inControl && repeat.Length == 0) return "It's in my Jobs app. I'll start it when you give my computer back.";
            if (repeat.Length > 0) return "Ok, I'll " + HerComputer.Short(task, 80) + " " + (repeat == "daily" ? "every day" : "every week") + " on my computer, starting " + (wasBusy ? "after this job." : "now.");
            if (wasBusy) return "I'm doing \"" + HerComputer.Short(runningTask, 60) + "\" right now. I'll do that next: it's number " + Math.Max(1, Computer.Jobs.Waiting - 1) + " in my Jobs app.";
            return null;
        }

        private void Pump()
        {
            if (closing || inControl || Ask == null || !CanThink()) return;
            HerJob next;
            lock (pumpGate)
            {
                if (Busy) return;
                next = Computer.Jobs.Next();
                if (next == null) return;
                stopTask = false;
                runningTask = next.Task;
            }
            taskThread = new Thread(delegate() { RunTask(next); }) { IsBackground = true, Name = "her computer job" };
            taskThread.Start();
        }

        internal void StopTask() { if (Busy) stopTask = true; }

        private void RunTask(HerJob job)
        {
            string answer = null, note = null, state = "done";
            HerRecorder recorder = Quiet ? null : HerRecorder.Begin(ReplayShot);
            try
            {
                answer = Computer.Run(job.Task, this, Ask, Browser, delegate { return stopTask || closing; }, out note);
            }
            catch (Exception error)
            {
                state = "failed";
                answer = "My computer hit a snag: " + HerComputer.Short(error.Message, 160);
            }
            if (answer == null) state = "stopped";
            if (recorder != null) recorder.Stop();
            Computer.Jobs.Finish(job.Id, state, answer, note);
            KeepReplay(job, recorder);
            runningTask = null;
            if (state == "stopped") { Status("Stopped"); return; }
            if (note != null) lock (gate) selectedNote = note;
            Changed();
            Status("Done: " + HerComputer.Short(job.Task, 50));
            bool handing = state == "done" && !string.IsNullOrEmpty(job.After) && HandOff != null;
            Bubble(FirstLine(answer));
            Action<string> tell = Tell;
            if (tell != null) tell(HerComputer.Short(answer, 600));
            if (handing)
            {
                HerJob done = job.Copy();
                done.State = state;
                done.Answer = answer ?? "";
                done.Note = note ?? "";
                HandOff(done);
            }
            if (!closing && !Quiet) Thread.Sleep(2500);
            Pump();
        }

        private Bitmap ReplayShot()
        {
            if (closing || IsDisposed || !IsHandleCreated) return null;
            Bitmap shot = null;
            using (ManualResetEvent done = new ManualResetEvent(false))
            {
                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        try { if (!closing && !IsDisposed) shot = Snapshot(); }
                        catch (Exception) { }
                        finally { try { done.Set(); } catch (ObjectDisposedException) { } }
                    }));
                }
                catch (InvalidOperationException) { return null; }
                if (!done.WaitOne(1500)) return null;
            }
            return shot;
        }

        private void KeepReplay(HerJob job, HerRecorder recorder)
        {
            if (recorder == null) return;
            string folder = Computer.ReplayFolder, task = job.Task, id = job.Id;
            new Thread(delegate()
            {
                try
                {
                    if (closing) return;
                    string path = recorder.Save(folder, task);
                    if (path != null) { Computer.Jobs.SetReplay(id, path); Changed(); }
                }
                catch (Exception) { }
                finally { recorder.Dispose(); }
            }) { IsBackground = true, Name = "her computer replay saving", Priority = ThreadPriority.BelowNormal }.Start();
        }

        private void ToggleControl()
        {
            if (inControl) { HandBack(); return; }
            handActiveAt = DateTime.UtcNow;
            inControl = true;
            stopTask = Busy;
            lock (gate) bubble = null;
            Status("You have the mouse");
        }

        private void HandBack()
        {
            if (!inControl) return;
            inControl = false;
            Status("Idle");
            ThreadPool.QueueUserWorkItem(delegate { Pump(); });
        }

        internal static string FirstLine(string answer)
        {
            string text = System.Text.RegularExpressions.Regex.Replace(answer ?? "", @"\s+", " ").Trim();
            System.Text.RegularExpressions.Match end = System.Text.RegularExpressions.Regex.Match(text, @"^.{20,}?[.!?](?=\s|$)");
            return HerComputer.Short(end.Success ? end.Value : text, 110);
        }

        public void Status(string text) { lock (gate) status = text ?? ""; }

        public void Show(string app)
        {
            lock (gate)
            {
                openApp = app;
                if (app == "notes" && selectedNote == null) { List<string> notes = Computer.Notes(); if (notes.Count > 0) selectedNote = notes[0]; }
            }
            Thread.Sleep(Quiet ? 0 : 260);
        }

        public void Point(string spot) { MoveTo(Spot(spot)); }

        public void PointPage(Rectangle box)
        {
            MoveTo(new PointF(PageBox.Left + Math.Max(0, Math.Min(PageBox.Width, box.Left + box.Width / 2)), PageBox.Top + Math.Max(0, Math.Min(PageBox.Height, box.Top + box.Height / 2))));
        }

        private void MoveTo(PointF to)
        {
            lock (gate) target = to;
            if (Quiet) { lock (gate) cursor = to; return; }
            for (int i = 0; i < 90 && !closing; i++)
            {
                lock (gate) if (Math.Abs(cursor.X - to.X) < 2 && Math.Abs(cursor.Y - to.Y) < 2) break;
                Thread.Sleep(20);
            }
            Thread.Sleep(120);
        }

        public void Click()
        {
            lock (gate) rippleAt = DateTime.UtcNow;
            Thread.Sleep(Quiet ? 0 : 220);
        }

        public void Typing(string field, string text)
        {
            if (text == null) { lock (gate) { typingField = null; typed = null; } return; }
            lock (gate) { typingField = field; typed = ""; }
            int chunk = Math.Max(1, text.Length / 70);
            for (int i = 0; i < text.Length && !closing; i += chunk)
            {
                string shown = text.Substring(0, Math.Min(text.Length, i + chunk));
                lock (gate) typed = shown;
                if (!Quiet) Thread.Sleep(28 + random.Next(45));
            }
            lock (gate) typed = text;
            Thread.Sleep(Quiet ? 0 : 250);
        }

        public void Bubble(string line)
        {
            lock (gate) { bubble = line; bubbleUntil = DateTime.UtcNow.AddSeconds(Math.Max(4, Math.Min(10, (line ?? "").Length / 9))); }
        }

        public void Changed() { }

        private PointF Spot(string spot)
        {
            spot = spot ?? "";
            if (spot == "address") return new PointF(AddressBox.Left + 40, AddressBox.Top + AddressBox.Height / 2);
            if (spot == "back") return new PointF(AppBox.Left + 24, AppBox.Top + TabTall + ToolTall / 2);
            if (spot == "page") return new PointF(PageBox.Left + PageBox.Width / 2, PageBox.Top + PageBox.Height / 2);
            if (spot == "close") return new PointF(CardBox.Right - 26, CardBox.Top + CardHead / 2);
            if (spot.StartsWith("dock:", StringComparison.Ordinal)) { Rectangle slot = DockSlot(Array.IndexOf(HerComputer.Apps, spot.Substring(5))); return new PointF(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2); }
            if (spot.StartsWith("site:", StringComparison.Ordinal)) { Rectangle tile = SiteTile(Array.IndexOf(HerComputer.Sites, spot.Substring(5))); return new PointF(tile.Left + tile.Width / 2, tile.Top + tile.Height / 2); }
            if (spot.StartsWith("sticker:", StringComparison.Ordinal)) { Rectangle cell = StickerCell(Array.IndexOf(HerComputer.StickerKinds, spot.Substring(8))); return new PointF(cell.Left + cell.Width / 2, cell.Top + cell.Height / 2); }
            if (spot.StartsWith("desk:", StringComparison.Ordinal))
            {
                string[] parts = spot.Substring(5).Split(',');
                int x, y;
                if (parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y)) return new PointF(x, y);
            }
            return new PointF(HerComputer.Width / 2, HerComputer.Height / 2);
        }

        internal static Rectangle DockSlot(int index)
        {
            index = Math.Max(0, index);
            return new Rectangle(DockBox.Left + 22 + index * 68, DockBox.Top + 12, 46, 46);
        }

        internal static Rectangle SiteTile(int index)
        {
            index = Math.Max(0, index);
            int columns = 6, across = columns * 92 + (columns - 1) * 14;
            int left = HerComputer.Width / 2 - across / 2 + (index % columns) * 106 + 17;
            return new Rectangle(left, PageBox.Top + 318 + (index / columns) * 104, 58, 58);
        }

        internal static Rectangle StickerCell(int index)
        {
            index = Math.Max(0, index);
            return new Rectangle(CardBox.Left + 34 + (index % 5) * 132, CardBox.Top + CardHead + 70 + (index / 5) * 180, 124, 160);
        }

        internal static Rectangle FileCell(int index)
        {
            return new Rectangle(CardBox.Left + 28 + (index % 5) * 134, CardBox.Top + CardHead + 24 + (index / 5) * 120, 124, 112);
        }

        private static Rectangle NoteRow(int index)
        {
            return new Rectangle(CardBox.Left + 10, CardBox.Top + CardHead + 12 + index * 40, 196, 36);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointF at = Logical(e.Location);
            RectangleF button;
            string app;
            lock (gate) { app = openApp; button = controlButton; }
            if (button.Contains(at)) { ToggleControl(); return; }
            if (app == "jobs")
            {
                Action hit = null;
                lock (gate) foreach (KeyValuePair<RectangleF, Action> spot in jobHits) if (spot.Key.Contains(at)) hit = spot.Value;
                if (hit != null) { hit(); return; }
                if (!CardBox.Contains(System.Drawing.Point.Round(at))) lock (gate) jobTyped = null;
            }
            if (DockSlot(Array.IndexOf(HerComputer.Apps, "jobs")).Contains(System.Drawing.Point.Round(at))) { if (app == "jobs") lock (gate) { openApp = null; jobTyped = null; } else OpenApp("jobs"); return; }
            if (app == "browser" && BrowserBody.Contains(System.Drawing.Point.Round(at)))
            {
                if (Busy && !inControl) { Bubble("I'm using my browser for a job. Press Take control to borrow it."); return; }
                if (BrowserDown) { OpenSite("https://www.bing.com/"); return; }
                if (starting) return;
                if (!inControl) ToggleControl();
            }
            if (Busy && !inControl) return;
            for (int i = 0; i < HerComputer.Apps.Length; i++)
                if (DockSlot(i).Contains(System.Drawing.Point.Round(at))) { if (app == HerComputer.Apps[i]) lock (gate) openApp = null; else OpenApp(HerComputer.Apps[i]); return; }
            bool card = app != null && app != "browser";
            if (card && new RectangleF(CardBox.Right - 48, CardBox.Top, 48, CardHead).Contains(at)) { lock (gate) { openApp = null; jobTyped = null; } return; }
            if (card && app != "stickers" && app != "jobs" && new RectangleF(CardBox.Right - 190, CardBox.Top, 140, CardHead).Contains(at)) { if (app == "notes") OpenVault(); else OpenFolder(); return; }
            if (app == "browser" && new RectangleF(AppBox.Right - 40, AppBox.Top, 40, TabTall).Contains(at)) { lock (gate) openApp = null; return; }
            if (app == "browser" && inControl)
            {
                HerBrowser current;
                lock (gate) current = web;
                if (PageBox.Contains(System.Drawing.Point.Round(at)) && current != null)
                {
                    int x = (int)(at.X - PageBox.Left), y = (int)(at.Y - PageBox.Top);
                    lock (gate) { cursor = target = at; rippleAt = DateTime.UtcNow; }
                    ThreadPool.QueueUserWorkItem(delegate { current.Click(x, y); });
                    return;
                }
                float tools = AppBox.Top + TabTall;
                if (new RectangleF(AppBox.Left, tools, 40, ToolTall).Contains(at) && current != null) { ThreadPool.QueueUserWorkItem(delegate { current.Back(); }); return; }
                if (new RectangleF(AppBox.Left + 40, tools, 34, ToolTall).Contains(at) && current != null) { ThreadPool.QueueUserWorkItem(delegate { current.Forward(); }); return; }
                if (new RectangleF(AppBox.Left + 74, tools, 36, ToolTall).Contains(at) && current != null) { ThreadPool.QueueUserWorkItem(delegate { current.Reload(); }); return; }
                if (AddressBox.Contains(System.Drawing.Point.Round(at))) { lock (gate) { typingField = "address"; typed = ""; } Focus(); return; }
            }
            if (app == "notes")
            {
                List<string> notes = Computer.Notes();
                for (int i = 0; i < notes.Count && i < 11; i++)
                    if (NoteRow(i).Contains(System.Drawing.Point.Round(at))) { lock (gate) selectedNote = notes[i]; return; }
            }
            if (app == "files")
            {
                List<string> files = Computer.Files();
                for (int i = 0; i < files.Count && i < 15; i++)
                    if (FileCell(i).Contains(System.Drawing.Point.Round(at))) { Open(files[i]); return; }
            }
            if (app == "stickers")
            {
                for (int i = 0; i < HerComputer.StickerKinds.Length; i++)
                    if (StickerCell(i).Contains(System.Drawing.Point.Round(at))) { lock (gate) { heldSticker = HerComputer.StickerKinds[i]; openApp = null; } Status("Click her page to stick it"); return; }
                if (StickerCell(HerComputer.StickerKinds.Length).Contains(System.Drawing.Point.Round(at))) { lock (gate) { heldSticker = BombSticker; openApp = null; } Status("Click her page to stick it"); return; }
            }
            if (app != null) return;
            if (heldSticker == BombSticker && PageBox.Contains(System.Drawing.Point.Round(at)))
            {
                if (!Booming) Bomb(at);
                return;
            }
            if (heldSticker != null && PageBox.Contains(System.Drawing.Point.Round(at)))
            {
                Computer.AddSticker(heldSticker, System.Drawing.Point.Round(at), random.Next(-20, 21));
                lock (gate) heldSticker = null;
                Status("Idle");
                return;
            }
            for (int i = 0; i < HerComputer.Sites.Length; i++)
                if (SiteTile(i).Contains(System.Drawing.Point.Round(at))) { OpenSite(HerComputer.SiteAddresses[i]); return; }
        }

        private void OpenApp(string app)
        {
            lock (gate) openApp = app;
            if (app == "notes")
            {
                ThreadPool.QueueUserWorkItem(delegate { Computer.RefreshMemories(); });
                lock (gate) if (selectedNote == null) { List<string> notes = Computer.Notes(); if (notes.Count > 0) selectedNote = notes[0]; }
            }
            if (app == "browser")
            {
                HerBrowser current;
                lock (gate) current = web;
                if (current == null || !current.Running) OpenSite("https://www.bing.com/");
            }
        }

        private void OpenSite(string address)
        {
            lock (gate) openApp = "browser";
            ThreadPool.QueueUserWorkItem(delegate { HerBrowser started = Browser(); if (started != null) started.Go(address); Status(Busy ? Doing : "Idle"); });
        }

        private void OpenFolder() { Open(Computer.Folder); }

        private void OpenVault()
        {
            string folder = Computer.NotesFolder;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Computer.RefreshMemories();
                try { Process.Start(new ProcessStartInfo(HerVault.ObsidianLink(folder)) { UseShellExecute = true }); }
                catch (Exception) { Open(folder); }
            });
        }

        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        protected override void WndProc(ref Message m)
        {
            if (inControl && (m.Msg == 0x0100 || m.Msg == 0x0102 || m.Msg == 0x0200 || m.Msg == 0x0201 || m.Msg == 0x0204 || m.Msg == 0x020A)) handActiveAt = DateTime.UtcNow;
            base.WndProc(ref m);
        }

        internal void TakeControlFor(double secondsIdle)
        {
            if (!inControl) ToggleControl();
            handActiveAt = DateTime.UtcNow.AddSeconds(-secondsIdle);
            Tick();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HerBrowser current;
            string app;
            lock (gate) { current = web; app = openApp; }
            if (inControl && app == "browser" && current != null) { int dy = -e.Delta; ThreadPool.QueueUserWorkItem(delegate { current.Scroll(dy); }); }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            string draft;
            lock (gate) draft = jobTyped;
            if (draft != null)
            {
                e.Handled = true;
                char typedKey = e.KeyChar;
                if (typedKey == '\r') { AddTypedJob(); return; }
                if (typedKey == (char)27) { lock (gate) jobTyped = null; return; }
                if (typedKey == '\b') { lock (gate) if (jobTyped.Length > 0) jobTyped = jobTyped.Substring(0, jobTyped.Length - 1); return; }
                string more = typedKey == (char)22 ? (Clipboard.ContainsText() ? Clipboard.GetText() : "") : char.IsControl(typedKey) ? "" : typedKey.ToString();
                more = more.Replace("\r", " ").Replace("\n", " ");
                lock (gate) jobTyped = HerComputer.Short(jobTyped + more, 300);
                return;
            }
            if (!inControl) return;
            HerBrowser current;
            string app, field;
            lock (gate) { current = web; app = openApp; field = typingField; }
            if (app != "browser" || current == null) return;
            e.Handled = true;
            char c = e.KeyChar;
            if (field == "address")
            {
                if (c == '\r') { string go; lock (gate) { go = typed; typingField = null; typed = null; } if (!string.IsNullOrWhiteSpace(go)) ThreadPool.QueueUserWorkItem(delegate { current.Go(go); }); }
                else if (c == '\b') lock (gate) { if (!string.IsNullOrEmpty(typed)) typed = typed.Substring(0, typed.Length - 1); }
                else if (c == (char)27) lock (gate) { typingField = null; typed = null; }
                else if (!char.IsControl(c)) lock (gate) typed = (typed ?? "") + c;
                return;
            }
            string key = c == '\r' ? "Enter" : c == '\b' ? "Backspace" : c == '\t' ? "Tab" : c == (char)27 ? "Escape" : null;
            if (key != null) ThreadPool.QueueUserWorkItem(delegate { current.Key(key); });
            else if (!char.IsControl(c)) { string one = c.ToString(); ThreadPool.QueueUserWorkItem(delegate { current.Type(one); }); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            if (backdrop == null || backdrop.Size != ClientSize)
            {
                if (backdrop != null) backdrop.Dispose();
                backdrop = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height), PixelFormat.Format32bppPArgb);
                using (Graphics b = Graphics.FromImage(backdrop))
                {
                    b.SmoothingMode = SmoothingMode.AntiAlias;
                    DrawBezel(b, new Rectangle(0, 0, backdrop.Width, backdrop.Height), 0);
                }
            }
            g.DrawImageUnscaled(backdrop, 0, 0);
            Prepare(g);
            g.TranslateTransform(offset.X, offset.Y);
            g.ScaleTransform(scale, scale);
            Draw(g);
        }

        private static void Prepare(Graphics g)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        }

        internal void Draw(Graphics g)
        {
            double since;
            lock (gate) since = (DateTime.UtcNow - boomAt).TotalSeconds;
            if (since >= Fuse && since < BoomEnd) { DrawBoom(g, since); return; }
            lock (gate) DrawAll(g, openApp, typingField, typed, bubble, status, cursor, frame, rippleAt, heldSticker, selectedNote, webProblem);
            if (since >= 0 && since < Fuse) DrawFuse(g, since);
        }

        internal bool Booming { get { lock (gate) return (DateTime.UtcNow - boomAt).TotalSeconds < BoomEnd; } }

        internal void ShowBoom(double since, PointF at)
        {
            lock (gate) { boomAt = DateTime.UtcNow.AddSeconds(-since); boomSpot = at; }
        }

        private void Bomb(PointF at)
        {
            PointF home;
            lock (gate) { boomAt = DateTime.UtcNow; boomSpot = at; heldSticker = null; home = cursor; }
            Status("Uh oh");
            lock (gate) bubble = null;
            ThreadPool.QueueUserWorkItem(delegate { BombGoesOff(home); });
        }

        private void BombGoesOff(PointF home)
        {
            DateTime began;
            lock (gate) began = boomAt;
            string reaction = null;
            Func<string, string, string> ask = Ask;
            Thread thinking = null;
            if (ask != null && CanThink())
            {
                thinking = new Thread(delegate()
                {
                    try { reaction = ask("You are Capri. Reply with one short line in your own voice, the way you'd say it out loud, with no quotes and no emojis.", "Someone just stuck a malware bomb sticker on the home page of your own little computer, the one that lives inside their PC. It fizzed, blew up, glitched your whole screen and made it reboot. It's back now. React to them."); }
                    catch (Exception) { reaction = null; }
                }) { IsBackground = true, Name = "her computer bomb" };
                thinking.Start();
            }
            while (!closing && (DateTime.UtcNow - began).TotalSeconds < Fuse) Thread.Sleep(50);
            Status("Something is very wrong");
            while (!closing && (DateTime.UtcNow - began).TotalSeconds < GlitchEnd)
            {
                if (!inControl) lock (gate) target = new PointF(PageBox.Left + random.Next(PageBox.Width), PageBox.Top + random.Next(PageBox.Height));
                Thread.Sleep(140);
            }
            Bubble(null);
            Status("Rebooting...");
            while (!closing && (DateTime.UtcNow - began).TotalSeconds < BoomEnd) Thread.Sleep(50);
            if (closing) return;
            lock (gate) { if (boomAt == began) boomAt = DateTime.MinValue; if (!inControl) target = home; }
            Status(inControl ? "You have the mouse" : "Idle");
            if (thinking != null) thinking.Join(15000);
            string line = HerComputer.Short((reaction ?? "").Trim().Trim('"'), 200);
            if (line.Length == 0) return;
            Bubble(line);
            Action<string> tell = Tell;
            if (tell != null) tell(line);
        }

        private void DrawFuse(Graphics g, double since)
        {
            float grow = (float)(since / Fuse);
            float size = 64 + 10 * grow + 4 * (float)Math.Sin(since * 18);
            int tilt = (int)(Math.Sin(since * 26) * (4 + 10 * grow));
            float halo = size * (1.25f + 0.15f * (float)Math.Sin(since * 12));
            using (SolidBrush pink = new SolidBrush(Color.FromArgb(70 + (int)(70 * grow), 255, 93, 140)))
                g.FillEllipse(pink, boomSpot.X - halo / 2, boomSpot.Y - halo / 2, halo, halo);
            DrawPicture(g, Art("sticker-" + BombSticker), new RectangleF(boomSpot.X - size / 2, boomSpot.Y - size / 2, size, size), tilt, 1f);
            PointF spark = new PointF(boomSpot.X + size * 0.3f, boomSpot.Y - size * 0.42f);
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(120, 255, 214, 92)))
                g.FillEllipse(glow, spark.X - 10, spark.Y - 10, 20, 20);
            for (int i = 0; i < 7; i++)
            {
                double angle = sparkle.NextDouble() * Math.PI * 2, far = 6 + sparkle.NextDouble() * 16;
                float dot = 2 + (float)sparkle.NextDouble() * 3;
                using (SolidBrush bit = new SolidBrush(i % 2 == 0 ? Color.FromArgb(255, 255, 236, 140) : Color.FromArgb(255, 255, 140, 60)))
                    g.FillEllipse(bit, spark.X + (float)(Math.Cos(angle) * far) - dot / 2, spark.Y + (float)(Math.Sin(angle) * far) - dot / 2, dot, dot);
            }
        }

        private void DrawBoom(Graphics g, double since)
        {
            int wide = HerComputer.Width, tall = HerComputer.Height;
            if (boomFrame == null)
            {
                boomFrame = new Bitmap(wide, tall, PixelFormat.Format32bppPArgb);
                using (Graphics f = Graphics.FromImage(boomFrame)) { f.Clear(Ground); f.SmoothingMode = SmoothingMode.AntiAlias; DrawFrame(f); }
            }
            if (boomScene == null) boomScene = new Bitmap(wide, tall, PixelFormat.Format32bppPArgb);
            using (Graphics s = Graphics.FromImage(boomScene))
            {
                s.DrawImageUnscaled(boomFrame, 0, 0);
                Prepare(s);
                lock (gate) DrawAll(s, openApp, typingField, typed, bubble, status, cursor, frame, rippleAt, null, selectedNote, webProblem);
            }
            Rectangle screen = HerComputer.Screen;
            if (since >= GlitchEnd)
            {
                double r = since - GlitchEnd, span = BoomEnd - GlitchEnd;
                g.DrawImage(boomScene, 0, 0, wide, tall);
                int alpha = r < span - 0.5 ? 255 : (int)(255 * Math.Max(0, (span - r) / 0.5));
                using (GraphicsPath shape = Round(screen, 40))
                using (SolidBrush dark = new SolidBrush(Color.FromArgb(alpha, 14, 8, 13)))
                    g.FillPath(dark, shape);
                if (alpha < 40) return;
                float fade = alpha / 255f;
                if (perch != null) DrawPicture(g, perch, new RectangleF(wide / 2 - 90, 190, 180, 180), 0, fade);
                using (SolidBrush white = new SolidBrush(Color.FromArgb(alpha, 255, 240, 246)))
                using (SolidBrush track = new SolidBrush(Color.FromArgb(alpha / 4, 255, 255, 255)))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(alpha, 255, 93, 140)))
                using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
                {
                    g.DrawString("Rebooting...", labelFont, white, new RectangleF(0, 400, wide, 34), centre);
                    Rectangle bar = new Rectangle(wide / 2 - 150, 450, 300, 10);
                    using (GraphicsPath back = Round(bar, 5)) g.FillPath(track, back);
                    int done = (int)(bar.Width * Math.Min(1, r / Math.Max(0.1, span - 0.5)));
                    if (done > 10) using (GraphicsPath front = Round(new Rectangle(bar.Left, bar.Top, done, bar.Height), 5)) g.FillPath(fill, front);
                }
                return;
            }
            double k = since - Fuse;
            float strength = (float)(1 - 0.6 * k / (GlitchEnd - Fuse));
            float shake = (float)(18 * Math.Max(0, 1 - k / 0.9) + 3 * strength);
            GraphicsState state = g.Save();
            g.TranslateTransform((float)(sparkle.NextDouble() * 2 - 1) * shake, (float)(sparkle.NextDouble() * 2 - 1) * shake);
            for (int y = 0; y < tall; )
            {
                int slice = Math.Min(tall - y, 6 + sparkle.Next(46));
                int dx = sparkle.NextDouble() < 0.45 * strength ? (int)(sparkle.Next(-60, 61) * strength) : 0;
                g.DrawImage(boomScene, new Rectangle(dx, y, wide, slice), 0, y, wide, slice, GraphicsUnit.Pixel);
                y += slice;
            }
            using (ImageAttributes red = new ImageAttributes())
            using (ImageAttributes cyan = new ImageAttributes())
            {
                red.SetColorMatrix(new ColorMatrix(new[] { new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0.5f, 0 }, new float[] { 0, 0, 0, 0, 1 } }));
                cyan.SetColorMatrix(new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 0, 0, 1, 0, 0 }, new float[] { 0, 0, 0, 0.5f, 0 }, new float[] { 0, 0, 0, 0, 1 } }));
                for (int i = 0; i < 4; i++)
                {
                    int y = sparkle.Next(tall - 60), band = 12 + sparkle.Next(60), split = (int)(6 + 10 * strength);
                    g.DrawImage(boomScene, new Rectangle(split, y, wide, band), 0, y, wide, band, GraphicsUnit.Pixel, red);
                    g.DrawImage(boomScene, new Rectangle(-split, y, wide, band), 0, y, wide, band, GraphicsUnit.Pixel, cyan);
                }
            }
            Color[] blocks = { Hot, Mint, Color.Black, Color.White, Color.FromArgb(255, 93, 140) };
            for (int i = 0; i < 3 + (int)(6 * strength); i++)
                using (SolidBrush block = new SolidBrush(Color.FromArgb(150 + sparkle.Next(100), blocks[sparkle.Next(blocks.Length)])))
                    g.FillRectangle(block, screen.Left + sparkle.Next(screen.Width - 40), screen.Top + sparkle.Next(screen.Height - 20), 20 + sparkle.Next(220), 3 + sparkle.Next(28));
            using (GraphicsPath shape = Round(screen, 40))
            using (HatchBrush lines = new HatchBrush(HatchStyle.DarkHorizontal, Color.FromArgb(46, 0, 0, 0), Color.Transparent))
                g.FillPath(lines, shape);
            if (k > 0.55)
            {
                Rectangle error = new Rectangle(wide / 2 - 220 + (sparkle.NextDouble() < 0.25 ? sparkle.Next(-14, 15) : 0), 300, 440, 132);
                using (SolidBrush paper = new SolidBrush(Paper))
                using (SolidBrush head = new SolidBrush(Hot))
                using (SolidBrush mint = new SolidBrush(Color.FromArgb(120, Mint)))
                using (SolidBrush white = new SolidBrush(Color.White))
                using (SolidBrush ink = new SolidBrush(Ink))
                {
                    if (sparkle.NextDouble() < 0.3) using (GraphicsPath ghost = Round(new Rectangle(error.Left + 9, error.Top - 5, error.Width, error.Height), 18)) g.FillPath(mint, ghost);
                    using (GraphicsPath card = Round(error, 18)) g.FillPath(paper, card);
                    using (GraphicsPath bar = Round(new Rectangle(error.Left, error.Top, error.Width, 44), 18)) g.FillPath(head, bar);
                    g.FillRectangle(head, error.Left, error.Top + 26, error.Width, 18);
                    DrawPicture(g, Art("sticker-" + BombSticker), new RectangleF(error.Left + 14, error.Top + 6, 32, 32), 0, 1f);
                    g.DrawString("capri.exe went kaboom", boldFont, white, error.Left + 54, error.Top + 11);
                    g.DrawString("A malware bomb got stuck on her home page.", textFont, ink, error.Left + 22, error.Top + 60);
                    g.DrawString("Please hold while she freaks out.", smallFont, ink, error.Left + 22, error.Top + 90);
                }
            }
            g.Restore(state);
            if (k < 0.35)
                using (SolidBrush flash = new SolidBrush(Color.FromArgb((int)(235 * (1 - k / 0.35)), 255, 244, 248)))
                using (GraphicsPath shape = Round(screen, 40))
                    g.FillPath(flash, shape);
            if (k < 0.7)
            {
                float size = 140 + 520 * (float)(k / 0.7);
                DrawPicture(g, Art("boom"), new RectangleF(boomSpot.X - size / 2, boomSpot.Y - size / 2, size, size), 0, (float)Math.Max(0, 1 - k / 0.7));
            }
        }

        private void DrawAll(Graphics g, string app, string field, string text, string said, string now, PointF at, Image page, DateTime ripple, string held, string note, string problem)
        {
            bool browsing = app == "browser";
            DrawWindow(g, browsing, field, text, page, problem);
            if (!browsing) DrawHome(g);
            bool card = app != null && !browsing;
            if (card)
            {
                DrawCard(g, app, field, text, note);
                if (perch != null) g.DrawImage(perch, new RectangleF(CardBox.Right + 2, CardBox.Bottom - 196, 180, 180));
            }
            DrawDock(g, app);
            DrawControl(g, now);
            if (said != null) DrawBubble(g, said, at, app == null ? 0 : card ? 1 : 2);
            if (held != null) DrawPicture(g, Art("sticker-" + held), new RectangleF(at.X + 14, at.Y + 18, 40, 40), 0, 1f);
            DrawCursor(g, at, ripple);
        }

        private static void DrawFrame(Graphics g) { DrawBezel(g, HerComputer.Screen, 40); }

        private static void DrawBezel(Graphics g, Rectangle box, int radius)
        {
            using (GraphicsPath shape = radius > 0 ? Round(box, radius) : new GraphicsPath())
            {
                if (radius <= 0) shape.AddRectangle(box);
                using (LinearGradientBrush pink = new LinearGradientBrush(box, Color.FromArgb(255, 106, 142), Color.FromArgb(90, 11, 42), 62f))
                {
                    ColorBlend blend = new ColorBlend();
                    blend.Colors = new[] { Color.FromArgb(255, 106, 142), Color.FromArgb(240, 48, 106), Color.FromArgb(179, 19, 74), Color.FromArgb(90, 11, 42) };
                    blend.Positions = new[] { 0f, 0.38f, 0.72f, 1f };
                    pink.InterpolationColors = blend;
                    g.FillPath(pink, shape);
                }
                Region kept = g.Clip;
                g.SetClip(shape, CombineMode.Intersect);
                Random specks = new Random(7);
                using (SolidBrush bright = new SolidBrush(Color.FromArgb(120, 255, 255, 255)))
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                    for (int i = 0, many = (int)Math.Min(2400, 520L * box.Width * box.Height / (1248L * 748)); i < many; i++)
                    {
                        float x = box.Left + specks.Next(box.Width), y = box.Top + specks.Next(box.Height), r = 0.8f + (float)specks.NextDouble() * 1.2f;
                        g.FillEllipse(i % 3 == 0 ? bright : dim, x, y, r, r);
                    }
                g.Clip = kept;
            }
        }

        private void DrawWindow(Graphics g, bool browsing, string field, string text, Image page, string problem)
        {
            for (int spread = 3; spread >= 1; spread--)
                using (GraphicsPath shadow = Round(new Rectangle(AppBox.X - spread * 3, AppBox.Y + spread * 4, AppBox.Width + spread * 6, AppBox.Height + spread * 3), 18 + spread * 3))
                using (SolidBrush dim = new SolidBrush(Color.FromArgb(22, 60, 0, 20)))
                    g.FillPath(dim, shadow);
            using (GraphicsPath box = Round(AppBox, 16))
            {
                Region kept = g.Clip;
                g.SetClip(box, CombineMode.Intersect);
                using (SolidBrush plum = new SolidBrush(Plum)) g.FillRectangle(plum, AppBox);
                using (SolidBrush chrome = new SolidBrush(Chrome)) g.FillRectangle(chrome, AppBox.Left, AppBox.Top, AppBox.Width, TabTall);
                HerBrowser current = web;
                string address = browsing && current != null ? current.LastAddress ?? "" : "";
                int site = SiteOf(address);
                Rectangle tab = new Rectangle(AppBox.Left + 12, AppBox.Top + 8, 230, TabTall - 8);
                using (GraphicsPath shape = Round(new Rectangle(tab.X, tab.Y, tab.Width, tab.Height + 14), 12))
                using (SolidBrush plum = new SolidBrush(Plum))
                    g.FillPath(plum, shape);
                Image favicon = !browsing ? null : site >= 0 ? Art("site-" + HerComputer.Sites[site]) : Art("app-browser");
                if (favicon != null) DrawPicture(g, favicon, new RectangleF(tab.Left + 14, tab.Top + 8, 18, 18), 0, 1f);
                else DrawRoundPicture(g, face, new RectangleF(tab.Left + 10, tab.Top + 5, 24, 24));
                using (SolidBrush blush = new SolidBrush(Blush))
                using (SolidBrush faded = new SolidBrush(Faded))
                using (StringFormat trim = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                {
                    g.DrawString(browsing ? Host(address) : "New tab", smallFont, blush, new RectangleF(tab.Left + 42, tab.Top + 8, tab.Width - 70, 20), trim);
                    g.DrawString("×", textFont, faded, AppBox.Right - 32, AppBox.Top + 10);
                    g.DrawString("+", textFont, faded, tab.Right + 12, AppBox.Top + 11);
                }
                float tools = AppBox.Top + TabTall;
                using (Pen faded = new Pen(Faded, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    float mid = tools + ToolTall / 2f;
                    g.DrawLines(faded, new[] { new PointF(AppBox.Left + 26, mid - 6), new PointF(AppBox.Left + 20, mid), new PointF(AppBox.Left + 26, mid + 6) });
                    g.DrawLines(faded, new[] { new PointF(AppBox.Left + 52, mid - 6), new PointF(AppBox.Left + 58, mid), new PointF(AppBox.Left + 52, mid + 6) });
                    g.DrawArc(faded, AppBox.Left + 82, mid - 7, 14, 14, -60, 300);
                }
                using (GraphicsPath pill = Round(AddressBox, 15))
                using (SolidBrush fill = new SolidBrush(Color.FromArgb(29, 14, 23)))
                using (Pen rim = new Pen(Rose, field == "address" ? 2.2f : 1.5f))
                {
                    g.FillPath(fill, pill);
                    g.DrawPath(rim, pill);
                }
                string shown = field == "address" && text != null ? text + (DateTime.UtcNow.Millisecond < 500 ? "|" : "") : address;
                using (SolidBrush ink = new SolidBrush(Color.FromArgb(217, 191, 204)))
                using (StringFormat trim = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
                    g.DrawString(HerComputer.Short(shown ?? "", 140), monoFont, ink, new RectangleF(AddressBox.Left + 16, AddressBox.Top + 7, AddressBox.Width - 30, 20), trim);
                DrawRoundPicture(g, face, new RectangleF(AppBox.Right - 47, tools + 6, 34, 34));
                using (Pen ring = new Pen(Rose, 2f)) g.DrawEllipse(ring, AppBox.Right - 47, tools + 6, 34, 34);
                using (SolidBrush line = new SolidBrush(Chrome)) g.FillRectangle(line, AppBox.Left, PageBox.Top - 1, AppBox.Width, 1);
                if (browsing)
                {
                    if (page != null) g.DrawImage(page, PageBox);
                    else
                        using (SolidBrush faded = new SolidBrush(Faded))
                        using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                            g.DrawString(problem == null ? "Loading..." : problem + "\nClick to try again.", textFont, faded, PageBox, centre);
                }
                g.Clip = kept;
            }
        }

        private void DrawHome(Graphics g)
        {
            Region kept = g.Clip;
            g.SetClip(PageBox, CombineMode.Intersect);
            int wallpaper = Computer.Wallpaper;
            if (wallpaper != 0)
                using (SolidBrush tint = new SolidBrush(wallpaper == 1 ? Color.FromArgb(18, 10, 28) : Color.FromArgb(28, 10, 16))) g.FillRectangle(tint, PageBox);
            foreach (HerSticker sticker in Computer.Stickers()) DrawPicture(g, Art("sticker-" + sticker.Kind), new RectangleF(sticker.X - 23, sticker.Y - 23, 46, 46), sticker.Tilt, 0.55f);
            if (perch != null) g.DrawImage(perch, new RectangleF(HerComputer.Width / 2 - 75, PageBox.Top + 10, 150, 150));
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                string clock = DateTime.Now.ToString("h:mm");
                RectangleF clockBox = new RectangleF(PageBox.Left, PageBox.Top + 158, PageBox.Width, 100);
                for (int glow = 3; glow >= 1; glow--)
                    using (SolidBrush halo = new SolidBrush(Color.FromArgb(28, 255, 46, 99)))
                    {
                        g.DrawString(clock, clockFont, halo, new RectangleF(clockBox.X - glow * 2, clockBox.Y, clockBox.Width, clockBox.Height), centre);
                        g.DrawString(clock, clockFont, halo, new RectangleF(clockBox.X + glow * 2, clockBox.Y, clockBox.Width, clockBox.Height), centre);
                    }
                using (SolidBrush blush = new SolidBrush(Blush)) g.DrawString(clock, clockFont, blush, clockBox, centre);
                using (SolidBrush soft = new SolidBrush(Color.FromArgb(242, 213, 225))) g.DrawString(Busy ? HerComputer.Short(runningTask, 60) : DateTime.Now.ToString("dddd d MMMM"), labelFont, soft, new RectangleF(PageBox.Left, PageBox.Top + 258, PageBox.Width, 30), centre);
                using (SolidBrush white = new SolidBrush(Color.White))
                using (SolidBrush shade = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
                using (SolidBrush label = new SolidBrush(Color.FromArgb(233, 211, 222)))
                    for (int i = 0; i < HerComputer.Sites.Length; i++)
                    {
                        Rectangle tile = SiteTile(i);
                        using (GraphicsPath drop = Round(new Rectangle(tile.X, tile.Y + 5, tile.Width, tile.Height), 16)) g.FillPath(shade, drop);
                        using (GraphicsPath shape = Round(tile, 16)) g.FillPath(white, shape);
                        DrawPicture(g, Art("site-" + HerComputer.Sites[i]), new RectangleF(tile.Left + 14, tile.Top + 14, 30, 30), 0, 1f);
                        g.DrawString(HerComputer.SiteNames[i], smallFont, label, new RectangleF(tile.Left - 20, tile.Bottom + 6, tile.Width + 40, 20), centre);
                    }
            }
            g.Clip = kept;
        }

        private void DrawCard(Graphics g, string app, string field, string text, string note)
        {
            using (GraphicsPath box = Round(AppBox, 16))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(140, 22, 11, 18)))
                g.FillPath(dim, box);
            using (GraphicsPath shadow = Round(new Rectangle(CardBox.X, CardBox.Y + 18, CardBox.Width, CardBox.Height), 22))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(90, 40, 0, 15)))
                g.FillPath(dim, shadow);
            using (GraphicsPath card = Round(CardBox, 22))
            {
                Region kept = g.Clip;
                g.SetClip(card, CombineMode.Intersect);
                using (SolidBrush paper = new SolidBrush(Paper)) g.FillRectangle(paper, CardBox);
                using (SolidBrush head = new SolidBrush(PaperHead)) g.FillRectangle(head, CardBox.Left, CardBox.Top, CardBox.Width, CardHead);
                int index = Math.Max(0, Array.IndexOf(HerComputer.Apps, app));
                DrawPicture(g, Art("app-" + app), new RectangleF(CardBox.Left + 20, CardBox.Top + 12, 28, 28), 0, 1f);
                using (SolidBrush ink = new SolidBrush(Ink))
                using (SolidBrush soft = new SolidBrush(SoftInk))
                {
                    g.DrawString("My " + HerComputer.AppNames[index].ToLowerInvariant(), boldFont, ink, CardBox.Left + 58, CardBox.Top + 15);
                    g.DrawString("×", textFont, soft, CardBox.Right - 32, CardBox.Top + 14);
                    if (app != "stickers" && app != "jobs") g.DrawString(app == "notes" ? "Open in Obsidian" : "Open folder", smallFont, soft, CardBox.Right - (app == "notes" ? 178 : 150), CardBox.Top + 17);
                }
                if (app == "notes") DrawNotes(g, field, text, note);
                else if (app == "files") DrawFiles(g);
                else if (app == "stickers") DrawStickerBook(g);
                else if (app == "jobs") DrawJobs(g);
                g.Clip = kept;
            }
        }

        private void DrawNotes(Graphics g, string field, string text, string note)
        {
            List<string> notes = Computer.Notes();
            using (SolidBrush side = new SolidBrush(Color.FromArgb(255, 241, 246)))
                g.FillRectangle(side, CardBox.Left, CardBox.Top + CardHead, 216, CardBox.Height - CardHead);
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush soft = new SolidBrush(SoftInk))
            using (SolidBrush pick = new SolidBrush(Color.FromArgb(255, 214, 228)))
            using (StringFormat trim = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            {
                if (notes.Count == 0 && field != "note") g.DrawString("No notes yet", smallFont, soft, CardBox.Left + 22, CardBox.Top + CardHead + 20);
                for (int i = 0; i < notes.Count && i < 11; i++)
                {
                    Rectangle row = NoteRow(i);
                    if (notes[i] == note && field != "note") using (GraphicsPath shape = Round(row, 10)) g.FillPath(pick, shape);
                    g.DrawString(Path.GetFileNameWithoutExtension(notes[i]), smallFont, ink, new RectangleF(row.X + 12, row.Y + 9, row.Width - 20, 20), trim);
                }
                RectangleF body = new RectangleF(CardBox.Left + 240, CardBox.Top + CardHead + 22, CardBox.Width - 268, CardBox.Height - CardHead - 40);
                string content = field == "note" && text != null ? text + (DateTime.UtcNow.Millisecond < 500 ? "|" : "") : HerComputer.Read(note, 6000) ?? "";
                if (content.StartsWith("# ", StringComparison.Ordinal))
                {
                    int end = content.IndexOf('\n');
                    string heading = (end < 0 ? content : content.Substring(0, end)).Substring(2).Trim();
                    content = end < 0 ? "" : content.Substring(end + 1).TrimStart('\r', '\n');
                    heading = heading.Length == 0 ? heading : char.ToUpperInvariant(heading[0]) + heading.Substring(1);
                    SizeF tall = g.MeasureString(heading, bigFont, (int)body.Width);
                    g.DrawString(heading, bigFont, ink, new RectangleF(body.X, body.Y, body.Width, tall.Height + 2));
                    body = new RectangleF(body.X, body.Y + tall.Height + 14, body.Width, Math.Max(0, body.Height - tall.Height - 14));
                }
                if (field != "note") content = System.Text.RegularExpressions.Regex.Replace(content, @"(?m)^>[^\n]*\n?", "").TrimStart('\r', '\n');
                g.DrawString(content, textFont, ink, body);
            }
        }

        private void DrawFiles(Graphics g)
        {
            List<string> files = Computer.Files();
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush soft = new SolidBrush(SoftInk))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter })
            {
                if (files.Count == 0) g.DrawString("Nothing here yet", smallFont, soft, CardBox.Left + 30, CardBox.Top + CardHead + 30);
                for (int i = 0; i < files.Count && i < 15; i++)
                {
                    Rectangle cell = FileCell(i);
                    bool diary = files[i].EndsWith("diary.md", StringComparison.OrdinalIgnoreCase);
                    DrawPicture(g, Art(diary ? "app-diary" : "app-notes"), new RectangleF(cell.Left + cell.Width / 2 - 28, cell.Top + 4, 56, 56), 0, 1f);
                    g.DrawString(Path.GetFileNameWithoutExtension(files[i]), smallFont, ink, new RectangleF(cell.Left, cell.Top + 66, cell.Width, 40), centre);
                }
            }
        }

        private void AddTypedJob()
        {
            string text;
            int repeat;
            lock (gate) { text = (jobTyped ?? "").Trim(); repeat = jobRepeat; }
            if (text.Length == 0) return;
            HerJob added = Computer.Jobs.Add(text, HerJobs.Repeats[repeat]);
            lock (gate) { jobTyped = null; jobRepeat = 0; }
            if (added == null) { Status("Jobs list is full"); return; }
            if (Ask == null || !CanThink()) { Status("Needs a brain: pick one in Mainframe"); return; }
            Pump();
        }

        private void JobHit(RectangleF place, Action act) { jobHits.Add(new KeyValuePair<RectangleF, Action>(place, act)); }

        private void DrawJobs(Graphics g)
        {
            jobHits.Clear();
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush soft = new SolidBrush(SoftInk))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush hot = new SolidBrush(Hot))
            using (SolidBrush row = new SolidBrush(Color.FromArgb(255, 241, 246)))
            using (SolidBrush mint = new SolidBrush(Color.FromArgb(38, 196, 110)))
            using (Pen rim = new Pen(Color.FromArgb(243, 198, 214), 1.5f))
            using (Pen focus = new Pen(Hot, 2f))
            using (StringFormat trim = new StringFormat(StringFormatFlags.NoWrap) { Trimming = StringTrimming.EllipsisCharacter })
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                int top = CardBox.Top + CardHead + 18, left = CardBox.Left + 24;
                Rectangle box = new Rectangle(left, top, 456, 40);
                using (GraphicsPath shape = Round(box, 12)) { g.FillPath(white, shape); g.DrawPath(jobTyped != null ? focus : rim, shape); }
                string shown = jobTyped == null ? "Give me a job..." : jobTyped + (DateTime.UtcNow.Millisecond < 500 ? "|" : "");
                g.DrawString(shown, textFont, jobTyped == null ? soft : ink, new RectangleF(box.Left + 14, box.Top + 10, box.Width - 24, 22), trim);
                JobHit(box, delegate { lock (gate) if (jobTyped == null) jobTyped = ""; Focus(); });
                Rectangle repeat = new Rectangle(box.Right + 10, top, 112, 40);
                using (GraphicsPath shape = Round(repeat, 12)) { g.FillPath(row, shape); g.DrawPath(rim, shape); }
                g.DrawString(jobRepeat == 0 ? "Just once" : jobRepeat == 1 ? "Every day" : "Every week", smallFont, ink, repeat, centre);
                JobHit(repeat, delegate { lock (gate) jobRepeat = (jobRepeat + 1) % HerJobs.Repeats.Length; });
                Rectangle add = new Rectangle(repeat.Right + 10, top, CardBox.Right - 24 - repeat.Right - 10, 40);
                using (GraphicsPath shape = Round(add, 12)) g.FillPath(hot, shape);
                g.DrawString("Add", boldFont, white, add, centre);
                JobHit(add, AddTypedJob);

                int column = CardBox.Left + 24, wide = 320, y = top + 64;
                HerJob now = Computer.Jobs.Running();
                g.DrawString("Now", smallFont, soft, column, y);
                y += 24;
                Rectangle current = new Rectangle(column, y, wide, 64);
                using (GraphicsPath shape = Round(current, 14)) g.FillPath(row, shape);
                if (now == null) g.DrawString(Busy ? HerComputer.Short(runningTask, 60) : "Nothing right now", textFont, soft, new RectangleF(current.Left + 14, current.Top + 22, wide - 28, 22), trim);
                else
                {
                    g.DrawString(now.Task, boldFont, ink, new RectangleF(current.Left + 14, current.Top + 10, wide - 100, 22), trim);
                    g.DrawString(status, smallFont, soft, new RectangleF(current.Left + 14, current.Top + 36, wide - 100, 20), trim);
                    Rectangle stop = new Rectangle(current.Right - 74, current.Top + 16, 60, 32);
                    using (GraphicsPath shape = Round(stop, 16)) g.FillPath(hot, shape);
                    g.DrawString("Stop", smallFont, white, stop, centre);
                    JobHit(stop, StopTask);
                }
                y = current.Bottom + 18;
                List<HerJob> queue = Computer.Jobs.Queue();
                queue.RemoveAll(job => job.State == "running");
                g.DrawString("Up next", smallFont, soft, column, y);
                y += 24;
                if (queue.Count == 0) g.DrawString("Nothing queued", textFont, soft, column + 14, y + 6);
                for (int i = 0; i < queue.Count && i < 5; i++)
                {
                    Rectangle line = new Rectangle(column, y + i * 40, wide, 34);
                    using (GraphicsPath shape = Round(line, 10)) g.FillPath(row, shape);
                    g.DrawString((i + 1) + ".  " + queue[i].Task, textFont, ink, new RectangleF(line.Left + 12, line.Top + 7, wide - 50, 22), trim);
                    string id = queue[i].Id;
                    RectangleF drop = new RectangleF(line.Right - 34, line.Top, 34, 34);
                    g.DrawString("×", textFont, soft, drop, centre);
                    JobHit(drop, delegate { Computer.Jobs.Remove(id); });
                }

                column = CardBox.Left + 24 + wide + 28;
                wide = CardBox.Right - 24 - column;
                y = top + 64;
                List<HerJob> repeats = Computer.Jobs.Repeating();
                g.DrawString("Repeats", smallFont, soft, column, y);
                y += 24;
                if (repeats.Count == 0) { g.DrawString("None yet. Pick Every day above.", textFont, soft, column + 14, y + 6); y += 40; }
                for (int i = 0; i < repeats.Count && i < 3; i++, y += 52)
                {
                    Rectangle line = new Rectangle(column, y, wide, 46);
                    using (GraphicsPath shape = Round(line, 10)) g.FillPath(row, shape);
                    g.DrawString(repeats[i].Task, textFont, ink, new RectangleF(line.Left + 12, line.Top + 4, wide - 50, 22), trim);
                    g.DrawString((repeats[i].Repeat == "daily" ? "Every day" : "Every week") + (repeats[i].NextRun <= HerJobs.Stamp(DateTime.Now) ? " · due now" : " · next " + HerJobs.Due(repeats[i].NextRun)), smallFont, soft, new RectangleF(line.Left + 12, line.Top + 25, wide - 50, 18), trim);
                    string id = repeats[i].Id;
                    RectangleF drop = new RectangleF(line.Right - 34, line.Top + 6, 34, 34);
                    g.DrawString("×", textFont, soft, drop, centre);
                    JobHit(drop, delegate { Computer.Jobs.Remove(id); });
                }
                y += 10;
                List<HerJob> done = Computer.Jobs.Done();
                g.DrawString("Done", smallFont, soft, column, y);
                if (done.Exists(one => !string.IsNullOrEmpty(one.Replay)))
                {
                    RectangleF folder = new RectangleF(column + wide - 130, y - 2, 130, 22);
                    g.DrawString("Open replays folder", smallFont, hot, folder, new StringFormat { Alignment = StringAlignment.Far });
                    JobHit(folder, delegate { Open(Computer.ReplayFolder); });
                }
                y += 24;
                if (done.Count == 0) g.DrawString("Nothing yet", textFont, soft, column + 14, y + 6);
                for (int i = 0; i < done.Count && y + 50 < CardBox.Bottom - 10; i++, y += 52)
                {
                    Rectangle line = new Rectangle(column, y, wide, 46);
                    using (GraphicsPath shape = Round(line, 10)) g.FillPath(row, shape);
                    g.FillEllipse(done[i].State == "done" ? mint : hot, line.Left + 12, line.Top + 19, 8, 8);
                    string replay = done[i].Replay;
                    bool watch = !string.IsNullOrEmpty(replay) && File.Exists(replay);
                    g.DrawString(done[i].Task, textFont, ink, new RectangleF(line.Left + 28, line.Top + 4, wide - (watch ? 130 : 40), 22), trim);
                    string first = (done[i].Answer ?? "").Replace("\r", " ").Replace("\n", " ");
                    g.DrawString(first.Length == 0 ? done[i].State : first, smallFont, soft, new RectangleF(line.Left + 28, line.Top + 25, wide - (watch ? 130 : 40), 18), trim);
                    string note = done[i].Note;
                    if (!string.IsNullOrEmpty(note) && File.Exists(note)) JobHit(line, delegate { lock (gate) { selectedNote = note; openApp = "notes"; } });
                    if (watch)
                    {
                        Rectangle pill = new Rectangle(line.Right - 98, line.Top + 12, 88, 22);
                        using (GraphicsPath shape = Round(pill, 11)) g.FillPath(hot, shape);
                        g.DrawString("Watch replay", smallFont, white, pill, centre);
                        JobHit(pill, delegate { Open(replay); });
                    }
                }
            }
        }

        private void DrawStickerBook(Graphics g)
        {
            using (SolidBrush soft = new SolidBrush(SoftInk))
            using (SolidBrush card = new SolidBrush(Color.FromArgb(255, 238, 244)))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("Pick one, then click my page to stick it", smallFont, soft, new RectangleF(CardBox.Left, CardBox.Top + CardHead + 26, CardBox.Width, 20), centre);
                for (int i = 0; i < HerComputer.StickerKinds.Length; i++)
                {
                    Rectangle cell = StickerCell(i);
                    using (GraphicsPath shape = Round(cell, 18)) g.FillPath(card, shape);
                    DrawPicture(g, Art("sticker-" + HerComputer.StickerKinds[i]), new RectangleF(cell.Left + cell.Width / 2 - 42, cell.Top + 22, 84, 84), 0, 1f);
                    g.DrawString(HerComputer.StickerKinds[i], smallFont, soft, new RectangleF(cell.Left, cell.Bottom - 34, cell.Width, 22), centre);
                }
                Rectangle bomb = StickerCell(HerComputer.StickerKinds.Length);
                using (SolidBrush danger = new SolidBrush(Color.FromArgb(255, 222, 230)))
                using (Pen dashed = new Pen(Hot, 2f) { DashStyle = DashStyle.Dash })
                using (SolidBrush hot = new SolidBrush(Hot))
                using (GraphicsPath shape = Round(bomb, 18))
                {
                    g.FillPath(danger, shape);
                    g.DrawPath(dashed, shape);
                    DrawPicture(g, Art("sticker-" + BombSticker), new RectangleF(bomb.Left + bomb.Width / 2 - 42, bomb.Top + 22, 84, 84), -8, 1f);
                    g.DrawString("malware bomb", smallFont, hot, new RectangleF(bomb.Left, bomb.Bottom - 34, bomb.Width, 22), centre);
                }
            }
        }

        private void DrawDock(Graphics g, string app)
        {
            using (GraphicsPath dock = Round(DockBox, 22))
            using (SolidBrush glass = new SolidBrush(Color.FromArgb(41, 255, 255, 255)))
            using (Pen rim = new Pen(Color.FromArgb(82, 255, 255, 255), 1f))
            {
                g.FillPath(glass, dock);
                g.DrawPath(rim, dock);
            }
            for (int i = 0; i < HerComputer.Apps.Length; i++)
            {
                Rectangle slot = DockSlot(i);
                DrawPicture(g, Art("app-" + HerComputer.Apps[i]), slot, 0, 1f);
                if (HerComputer.Apps[i] == app) using (SolidBrush dot = new SolidBrush(Color.White)) g.FillEllipse(dot, slot.Left + slot.Width / 2 - 2.5f, slot.Bottom + 4, 5, 5);
            }
        }

        private void DrawControl(Graphics g, string now)
        {
            string line = inControl ? "You have her mouse" : Busy ? "Capri is " + Lower(HerComputer.Short(now, 48)) : now == "Idle" ? "Capri's computer" : HerComputer.Short(now, 48);
            string press = inControl ? "Give it back" : "Take control";
            SizeF words = g.MeasureString(line, textFont), knob = g.MeasureString(press, boldFont);
            float width = words.Width + knob.Width + 70, left = HerComputer.Width / 2f - width / 2f, top = HerComputer.Screen.Bottom;
            using (GraphicsPath tab = new GraphicsPath())
            {
                const float r = 22;
                tab.AddLine(left, top, left + width, top);
                tab.AddArc(left + width - 2 * r, top + 44 - 2 * r, 2 * r, 2 * r, 0, 90);
                tab.AddArc(left, top + 44 - 2 * r, 2 * r, 2 * r, 90, 90);
                tab.CloseFigure();
                using (SolidBrush hot = new SolidBrush(Hot)) g.FillPath(hot, tab);
            }
            using (SolidBrush blush = new SolidBrush(Blush)) g.DrawString(line, textFont, blush, left + 22, top + 12);
            RectangleF button = new RectangleF(left + width - knob.Width - 40, top + 6, knob.Width + 32, 32);
            using (GraphicsPath pill = Round(Rectangle.Round(button), 16))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush hot = new SolidBrush(Hot))
            {
                g.FillPath(white, pill);
                g.DrawString(press, boldFont, hot, button.Left + 16, button.Top + 6);
            }
            controlButton = button;
        }

        private static string Lower(string text)
        {
            text = text ?? "";
            return text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text.Substring(1);
        }

        private void DrawBubble(Graphics g, string said, PointF at, int anchor)
        {
            bool beside = anchor == 0;
            SizeF size = g.MeasureString(said, textFont, anchor == 1 ? 170 : 250);
            RectangleF box;
            if (beside) box = new RectangleF(HerComputer.Width / 2 + 92, PageBox.Top + 34, size.Width + 36, size.Height + 26);
            else if (anchor == 1) box = new RectangleF(CardBox.Right + 4, CardBox.Bottom - 226 - size.Height, size.Width + 36, size.Height + 26);
            else
            {
                float x = Math.Max(HerComputer.Screen.Left + 20, Math.Min(HerComputer.Screen.Right - size.Width - 60, at.X + 10));
                float y = at.Y - size.Height - 64;
                if (y < HerComputer.Screen.Top + 10) y = at.Y + 48;
                box = new RectangleF(x, y, size.Width + 36, size.Height + 26);
            }
            using (GraphicsPath shape = Round(Rectangle.Round(box), 18))
            using (SolidBrush shade = new SolidBrush(Color.FromArgb(70, 0, 0, 0)))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (SolidBrush ink = new SolidBrush(Ink))
            {
                GraphicsState state = g.Save();
                g.TranslateTransform(0, 6);
                g.FillPath(shade, shape);
                g.Restore(state);
                g.FillPath(white, shape);
                PointF[] tail = beside
                    ? new[] { new PointF(box.Left + 1, box.Top + 20), new PointF(box.Left - 9, box.Top + 30), new PointF(box.Left + 1, box.Top + 38) }
                    : anchor == 1 || box.Bottom < at.Y ? new[] { new PointF(box.Left + 20, box.Bottom - 1), new PointF(box.Left + 30, box.Bottom + 10), new PointF(box.Left + 40, box.Bottom - 1) }
                    : new[] { new PointF(box.Left + 20, box.Top + 1), new PointF(box.Left + 30, box.Top - 10), new PointF(box.Left + 40, box.Top + 1) };
                g.FillPolygon(white, tail);
                g.DrawString(said, textFont, ink, new RectangleF(box.Left + 18, box.Top + 13, size.Width + 2, size.Height + 2));
            }
        }

        private void DrawCursor(Graphics g, PointF at, DateTime ripple)
        {
            double since = (DateTime.UtcNow - ripple).TotalMilliseconds;
            if (since < 450)
                using (Pen ring = new Pen(Color.FromArgb((int)(220 * (1 - since / 450)), Mint), 2.5f))
                {
                    float radius = 8 + (float)since / 16f;
                    g.DrawEllipse(ring, at.X - radius, at.Y - radius, radius * 2, radius * 2);
                }
            if (pointer == null)
            {
                PointF[] arrow = { at, new PointF(at.X, at.Y + 26), new PointF(at.X + 7, at.Y + 20), new PointF(at.X + 12, at.Y + 31), new PointF(at.X + 17, at.Y + 29), new PointF(at.X + 12, at.Y + 18), new PointF(at.X + 21, at.Y + 18) };
                using (SolidBrush rose = new SolidBrush(Rose)) g.FillPolygon(rose, arrow);
                return;
            }
            RectangleF place = new RectangleF(at.X - 1, at.Y - 1, 38, 38f * pointer.Height / pointer.Width);
            using (ImageAttributes shadow = new ImageAttributes())
            {
                shadow.SetColorMatrix(new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0.4f, 0 }, new float[] { 0, 0, 0, 0, 1 } }));
                g.DrawImage(pointer, Rectangle.Round(new RectangleF(place.X + 2, place.Y + 4, place.Width, place.Height)), 0, 0, pointer.Width, pointer.Height, GraphicsUnit.Pixel, shadow);
            }
            g.DrawImage(pointer, place);
        }

        private static void DrawPicture(Graphics g, Image image, RectangleF place, int tilt, float alpha)
        {
            if (image == null) return;
            GraphicsState state = g.Save();
            g.TranslateTransform(place.X + place.Width / 2, place.Y + place.Height / 2);
            if (tilt != 0) g.RotateTransform(tilt);
            Rectangle centred = Rectangle.Round(new RectangleF(-place.Width / 2, -place.Height / 2, place.Width, place.Height));
            if (alpha >= 1f) g.DrawImage(image, centred);
            else
                using (ImageAttributes faint = new ImageAttributes())
                {
                    faint.SetColorMatrix(new ColorMatrix { Matrix33 = alpha });
                    g.DrawImage(image, centred, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, faint);
                }
            g.Restore(state);
        }

        private static void DrawRoundPicture(Graphics g, Image image, RectangleF place)
        {
            if (image == null) return;
            GraphicsState state = g.Save();
            using (GraphicsPath circle = new GraphicsPath())
            using (SolidBrush back = new SolidBrush(Color.White))
            {
                circle.AddEllipse(place);
                g.SetClip(circle, CombineMode.Intersect);
                g.FillEllipse(back, place);
                g.DrawImage(image, new RectangleF(place.X + place.Width * 0.06f, place.Y + place.Height * 0.06f, place.Width * 0.88f, place.Height * 0.88f));
            }
            g.Restore(state);
        }

        internal static int SiteOf(string address)
        {
            string host = Host(address).ToLowerInvariant();
            if (host.Length == 0) return -1;
            for (int i = 0; i < HerComputer.Sites.Length; i++)
            {
                string site = Host(HerComputer.SiteAddresses[i]).ToLowerInvariant();
                if (site.StartsWith("www.", StringComparison.Ordinal)) site = site.Substring(4);
                if (site.StartsWith("open.", StringComparison.Ordinal)) site = site.Substring(5);
                if (host == site || host.EndsWith("." + site, StringComparison.Ordinal)) return i;
            }
            return -1;
        }

        internal static string Host(string address)
        {
            Uri uri;
            if (string.IsNullOrEmpty(address) || !Uri.TryCreate(address, UriKind.Absolute, out uri)) return "";
            string host = uri.Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host.Substring(4) : host;
        }

        internal static GraphicsPath Round(Rectangle box, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(box.Width, box.Height)));
            path.AddArc(box.Left, box.Top, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal Bitmap Snapshot()
        {
            Bitmap shot = new Bitmap(HerComputer.Width, HerComputer.Height);
            using (Graphics g = Graphics.FromImage(shot))
            {
                g.Clear(Ground);
                Prepare(g);
                DrawFrame(g);
                Draw(g);
            }
            return shot;
        }
    }
}
