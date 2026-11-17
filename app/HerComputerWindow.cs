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
        internal static readonly Rectangle AppBox = new Rectangle(160, 52, 1060, 588);
        internal const int TitleTall = 34, ToolTall = 40;
        internal static readonly Rectangle PageBox = new Rectangle(AppBox.Left, AppBox.Top + TitleTall + ToolTall, AppBox.Width, AppBox.Height - TitleTall - ToolTall);
        internal static readonly Rectangle AddressBox = new Rectangle(AppBox.Left + 112, AppBox.Top + TitleTall + 6, AppBox.Width - 130, 28);
        internal static readonly Rectangle DockBox = new Rectangle(HerComputer.Width / 2 - 170, 652, 340, 64);
        internal static readonly Rectangle BarBox = new Rectangle(24, 736, 1232, 70);
        private static readonly Color Ink = Color.FromArgb(86, 36, 92), SoftInk = Color.FromArgb(150, 96, 150), Pink = Color.FromArgb(236, 72, 190), Paper = Color.FromArgb(255, 251, 254);

        internal readonly HerComputer Computer;
        internal Func<string, string, string> Ask;
        internal Action<string> Tell;
        internal Func<bool> CanThink = delegate { return false; };
        internal static bool Quiet;

        private readonly object gate = new object();
        private readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer();
        private readonly TextBox taskBox = new TextBox();
        private readonly Button give = new Button(), control = new Button();
        private readonly Random random = new Random();
        private PointF cursor = new PointF(640, 380), target = new PointF(640, 380);
        private string openApp, typingField, typed, status = "Just hanging out", bubble, dragging, selectedNote, heldSticker;
        private DateTime bubbleUntil, rippleAt = DateTime.MinValue, lastWatched = DateTime.MinValue, heyAt = DateTime.MinValue, brainDiaryAt = DateTime.MinValue;
        private Image frame, face;
        private HerBrowser web;
        private string webProblem;
        private Thread taskThread, idleThread, frameThread;
        private volatile bool closing, stopTask, inControl;
        private volatile string runningTask;
        private float scale = 1f;
        private PointF offset;

        internal HerComputerWindow(HerComputer computer)
        {
            Computer = computer;
            Text = "Capri's computer";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(HerComputer.Width * 3 / 4, HerComputer.Height * 3 / 4);
            MinimumSize = new Size(560, 400);
            BackColor = Color.FromArgb(40, 16, 46);
            DoubleBuffered = true;
            KeyPreview = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            try { using (Stream chip = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (chip != null) Icon = new Icon(chip); } catch (Exception) { }
            try { using (Stream art = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Sprite.face.png")) if (art != null) face = new Bitmap(Image.FromStream(art)); } catch (Exception) { face = null; }
            taskBox.BorderStyle = BorderStyle.FixedSingle;
            taskBox.AccessibleName = "A job for Capri to do on her computer";
            taskBox.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; GivePressed(); } };
            foreach (Button button in new[] { give, control })
            {
                button.FlatStyle = FlatStyle.Flat;
                button.FlatAppearance.BorderSize = 0;
                button.BackColor = Pink;
                button.ForeColor = Color.White;
                button.Cursor = Cursors.Hand;
            }
            give.Text = "Give it to her";
            give.Click += delegate { GivePressed(); };
            control.Text = "Take control";
            control.Click += delegate { ToggleControl(); };
            Controls.Add(taskBox);
            Controls.Add(give);
            Controls.Add(control);
            ticker.Interval = 33;
            ticker.Tick += delegate { Tick(); };
            Layout += delegate { Place(); };
            Place();
        }

        internal bool Busy { get { return runningTask != null; } }
        internal string Doing { get { lock (gate) return status; } }
        internal string Showing { get { lock (gate) return openApp; } }
        internal bool InControl { get { return inControl; } }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            ticker.Start();
            frameThread = new Thread(FrameLoop) { IsBackground = true, Name = "her computer frames" };
            frameThread.Start();
            idleThread = new Thread(IdleLoop) { IsBackground = true, Name = "her computer idle" };
            idleThread.Start();
            if (!Computer.Greeted)
            {
                Computer.Greeted = true;
                Bubble("this is my computer. watch me work, or give me something to do.");
            }
            lastWatched = DateTime.UtcNow;
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (lastWatched != DateTime.MinValue && (DateTime.UtcNow - lastWatched).TotalSeconds > 90 && !Busy)
            {
                string line = Computer.Pick(HerComputer.PeekLines);
                ThreadPool.QueueUserWorkItem(delegate { Wave(); Bubble(line); });
            }
            lastWatched = DateTime.UtcNow;
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

        private void Place()
        {
            if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
            scale = Math.Min(ClientSize.Width / (float)HerComputer.Width, ClientSize.Height / (float)HerComputer.Height);
            offset = new PointF((ClientSize.Width - HerComputer.Width * scale) / 2, (ClientSize.Height - HerComputer.Height * scale) / 2);
            Font small = new Font("Segoe UI", Math.Max(7f, 15f * scale), FontStyle.Regular, GraphicsUnit.Pixel);
            taskBox.Font = small;
            give.Font = control.Font = new Font("Segoe UI", Math.Max(7f, 14f * scale), FontStyle.Bold, GraphicsUnit.Pixel);
            taskBox.Bounds = Screenward(new Rectangle(660, 754, 340, 32));
            taskBox.Height = Math.Max(taskBox.Height, (int)(32 * scale));
            give.Bounds = Screenward(new Rectangle(1010, 752, 116, 36));
            control.Bounds = Screenward(new Rectangle(1134, 752, 116, 36));
        }

        private Rectangle Screenward(Rectangle logical)
        {
            return new Rectangle((int)(offset.X + logical.X * scale), (int)(offset.Y + logical.Y * scale), (int)Math.Ceiling(logical.Width * scale), (int)Math.Ceiling(logical.Height * scale));
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
            if (ContainsFocus && WindowState != FormWindowState.Minimized) lastWatched = DateTime.UtcNow;
            give.Text = Busy ? "Stop" : "Give it to her";
            control.Text = inControl ? "Give it back" : "Take control";
            Invalidate();
        }

        private void FrameLoop()
        {
            while (!closing)
            {
                HerBrowser current;
                string app;
                lock (gate) { current = web; app = openApp; }
                if (current != null && app == "browser" && current.Running && WindowState != FormWindowState.Minimized)
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

        private void IdleLoop()
        {
            Thread.Sleep(6000);
            while (!closing)
            {
                int wait = 20000 + random.Next(40000);
                for (int waited = 0; waited < wait && !closing; waited += 250) Thread.Sleep(250);
                if (closing || Busy || inControl || Quiet) continue;
                bool minimized = false;
                try { Invoke(new Action(delegate { minimized = WindowState == FormWindowState.Minimized || !Visible; })); }
                catch (InvalidOperationException) { return; }
                if (minimized) continue;
                try { Computer.Potter(this, delegate { return closing || Busy || inControl; }, BrainDiary); }
                catch (Exception) { }
                if (!Busy) Status("Just hanging out");
            }
        }

        private string BrainDiary()
        {
            if (Ask == null || !CanThink() || (DateTime.UtcNow - brainDiaryAt).TotalMinutes < 60) return null;
            brainDiaryAt = DateTime.UtcNow;
            List<string> recent = Computer.Diary(5);
            string said = Ask("You are Capri writing one short line in your private diary on your own little computer. Lowercase, playful, under 18 words, no hashtags or emojis. Reply with the line only.", "Your last diary lines:\n" + string.Join("\n", recent.ToArray()) + "\nWrite today's next line.");
            return HerComputer.Short((said ?? "").Trim().Trim('"'), 140);
        }

        private HerBrowser Browser()
        {
            HerBrowser current;
            lock (gate) current = web;
            if (current != null && current.Running) return current;
            if (current != null) current.Dispose();
            HerBrowser started = new HerBrowser(Computer.BrowserFolder, PageBox.Width, PageBox.Height);
            string problem;
            Status("Starting my browser");
            if (!started.Start(out problem))
            {
                started.Dispose();
                lock (gate) { web = null; webProblem = problem; }
                Bubble(problem);
                return null;
            }
            lock (gate) { web = started; webProblem = null; }
            return started;
        }

        private void GivePressed()
        {
            if (Busy) { stopTask = true; Status("Stopping"); return; }
            string job = taskBox.Text.Trim();
            if (job.Length == 0) { taskBox.Focus(); return; }
            taskBox.Text = "";
            Start(job);
        }

        internal string Start(string job)
        {
            if (string.IsNullOrWhiteSpace(job)) return "What should I do on my computer?";
            if (Busy) return "I'm still doing \"" + HerComputer.Short(runningTask, 60) + "\" on my computer. Say stop first if you want something else.";
            if (Ask == null || !CanThink()) { Bubble("i need a brain for jobs. pick one in Mainframe."); return "I need a brain to do jobs on my computer. Pick one in Mainframe under BRAIN."; }
            stopTask = false;
            inControl = false;
            runningTask = job.Trim();
            taskThread = new Thread(delegate() { RunTask(job.Trim()); }) { IsBackground = true, Name = "her computer job" };
            taskThread.Start();
            return null;
        }

        internal void StopTask() { if (Busy) stopTask = true; }

        private void RunTask(string job)
        {
            string answer = null, note = null;
            try
            {
                answer = Computer.Run(job, this, Ask, Browser, delegate { return stopTask || closing; }, out note);
            }
            catch (Exception error)
            {
                answer = "My computer hit a snag: " + HerComputer.Short(error.Message, 160);
            }
            runningTask = null;
            if (answer == null) { Status("Stopped"); Bubble("ok, stopped."); return; }
            if (note != null) lock (gate) selectedNote = note;
            Changed();
            Status("Done: " + HerComputer.Short(job, 60));
            Bubble("done! it's in my notes.");
            Action<string> tell = Tell;
            if (tell != null) tell(HerComputer.Short(answer, 600));
        }

        private void ToggleControl()
        {
            inControl = !inControl;
            if (inControl) { stopTask = Busy; Status("You've got my mouse"); Bubble("fine, you drive. be gentle."); }
            else { Status("Just hanging out"); Bubble(Computer.Pick(new[] { "my turn again.", "thank you. i'll take it from here.", "you left fingerprints." })); }
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

        public void DragIcon(string app, Point to)
        {
            Point from = Computer.IconAt(app);
            MoveTo(new PointF(from.X + 36, from.Y + 32));
            lock (gate) { dragging = app; rippleAt = DateTime.UtcNow; }
            MoveTo(new PointF(to.X + 36, to.Y + 32));
            Computer.MoveIcon(app, to);
            lock (gate) dragging = null;
        }

        public void Bubble(string line)
        {
            lock (gate) { bubble = line; bubbleUntil = DateTime.UtcNow.AddSeconds(Math.Max(4, Math.Min(10, (line ?? "").Length / 9))); }
        }

        public void Wave()
        {
            PointF at;
            lock (gate) at = cursor;
            for (int i = 0; i < 4 && !closing; i++) MoveTo(new PointF(at.X + (i % 2 == 0 ? 26 : -26), at.Y - 6));
            MoveTo(at);
        }

        public void Changed() { }

        private PointF Spot(string spot)
        {
            spot = spot ?? "";
            if (spot == "address") return new PointF(AddressBox.Left + 40, AddressBox.Top + AddressBox.Height / 2);
            if (spot == "back") return new PointF(AppBox.Left + 22, AppBox.Top + TitleTall + 20);
            if (spot == "page") return new PointF(PageBox.Left + PageBox.Width / 2, PageBox.Top + PageBox.Height / 2);
            if (spot == "close") return new PointF(AppBox.Right - 22, AppBox.Top + TitleTall / 2);
            if (spot.StartsWith("icon:", StringComparison.Ordinal)) { Point icon = Computer.IconAt(spot.Substring(5)); return new PointF(icon.X + 36, icon.Y + 32); }
            if (spot.StartsWith("dock:", StringComparison.Ordinal)) { Rectangle slot = DockSlot(Array.IndexOf(HerComputer.Apps, spot.Substring(5))); return new PointF(slot.Left + slot.Width / 2, slot.Top + slot.Height / 2); }
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
            return new Rectangle(DockBox.Left + 14 + index * 64, DockBox.Top + 8, 48, 48);
        }

        internal static Rectangle StickerCell(int index)
        {
            index = Math.Max(0, index);
            return new Rectangle(AppBox.Left + 120 + (index % 5) * 170, AppBox.Top + TitleTall + 60 + (index / 5) * 190, 140, 150);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            PointF at = Logical(e.Location);
            string app;
            lock (gate) app = openApp;
            if (Busy && !inControl)
            {
                if ((DateTime.UtcNow - heyAt).TotalSeconds > 6 && HerComputer.Screen.Contains(System.Drawing.Point.Round(at))) { heyAt = DateTime.UtcNow; Bubble(Computer.Pick(HerComputer.HeyLines)); }
                return;
            }
            if (app != null && new RectangleF(AppBox.Right - 40, AppBox.Top, 40, TitleTall).Contains(at)) { lock (gate) openApp = null; return; }
            if (app != null && new RectangleF(AppBox.Right - 160, AppBox.Top, 110, TitleTall).Contains(at) && app != "browser" && app != "stickers") { OpenFolder(); return; }
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
                if (new RectangleF(AppBox.Left, AppBox.Top + TitleTall, 40, ToolTall).Contains(at) && current != null) { ThreadPool.QueueUserWorkItem(delegate { current.Back(); }); return; }
                if (new RectangleF(AppBox.Left + 40, AppBox.Top + TitleTall, 36, ToolTall).Contains(at) && current != null) { ThreadPool.QueueUserWorkItem(delegate { current.Forward(); }); return; }
                if (new RectangleF(AppBox.Left + 76, AppBox.Top + TitleTall, 36, ToolTall).Contains(at) && current != null) { ThreadPool.QueueUserWorkItem(delegate { current.Reload(); }); return; }
                if (AddressBox.Contains(System.Drawing.Point.Round(at))) { taskBox.Text = ""; lock (gate) { typingField = "address"; typed = ""; } Focus(); return; }
            }
            if (app == "notes")
            {
                List<string> notes = Computer.Notes();
                for (int i = 0; i < notes.Count && i < 12; i++)
                    if (new RectangleF(AppBox.Left + 8, AppBox.Top + TitleTall + 10 + i * 42, 272, 38).Contains(at)) { lock (gate) selectedNote = notes[i]; return; }
            }
            if (app == "files")
            {
                List<string> files = Computer.Files();
                for (int i = 0; i < files.Count && i < 24; i++)
                    if (FileCell(i).Contains(System.Drawing.Point.Round(at))) { Open(files[i]); return; }
            }
            if (app == "stickers")
            {
                for (int i = 0; i < HerComputer.StickerKinds.Length; i++)
                    if (StickerCell(i).Contains(System.Drawing.Point.Round(at))) { lock (gate) { heldSticker = HerComputer.StickerKinds[i]; openApp = null; } Status("Click her desktop to stick it"); return; }
            }
            if (app != null) return;
            if (heldSticker != null && HerComputer.Screen.Contains(System.Drawing.Point.Round(at)))
            {
                Computer.AddSticker(heldSticker, System.Drawing.Point.Round(at), random.Next(-20, 21));
                lock (gate) heldSticker = null;
                Bubble("ooh, nice placement.");
                Status("Just hanging out");
                return;
            }
            for (int i = 0; i < HerComputer.Apps.Length; i++)
            {
                Point icon = Computer.IconAt(HerComputer.Apps[i]);
                if (new RectangleF(icon.X, icon.Y, 72, 88).Contains(at) || DockSlot(i).Contains(System.Drawing.Point.Round(at))) { OpenApp(HerComputer.Apps[i]); return; }
            }
        }

        private void OpenApp(string app)
        {
            lock (gate) openApp = app;
            if (app == "notes") lock (gate) if (selectedNote == null) { List<string> notes = Computer.Notes(); if (notes.Count > 0) selectedNote = notes[0]; }
            if (app == "browser")
            {
                HerBrowser current;
                lock (gate) current = web;
                if (current == null || !current.Running) ThreadPool.QueueUserWorkItem(delegate { HerBrowser started = Browser(); if (started != null) started.Go("https://www.bing.com/"); Status(Busy ? Doing : "Just hanging out"); });
            }
        }

        private void OpenFolder() { Open(Computer.Folder); }

        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception) { }
        }

        internal static Rectangle FileCell(int index)
        {
            return new Rectangle(AppBox.Left + 30 + (index % 6) * 168, AppBox.Top + TitleTall + 24 + (index / 6) * 128, 150, 116);
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
            if (taskBox.Focused || !inControl) return;
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
            g.Clear(BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.TranslateTransform(offset.X, offset.Y);
            g.ScaleTransform(scale, scale);
            Draw(g);
        }

        internal void Draw(Graphics g)
        {
            string app, field, text, said, now, held, note;
            PointF at;
            Image page;
            DateTime ripple;
            string drag, problem;
            lock (gate)
            {
                app = openApp; field = typingField; text = typed; said = bubble; now = status; at = cursor; page = frame; ripple = rippleAt; drag = dragging; held = heldSticker; note = selectedNote; problem = webProblem;
                DrawAll(g, app, field, text, said, now, at, page, ripple, drag, held, note, problem);
            }
        }

        private void DrawAll(Graphics g, string app, string field, string text, string said, string now, PointF at, Image page, DateTime ripple, string drag, string held, string note, string problem)
        {
            Rectangle whole = new Rectangle(0, 0, HerComputer.Width, HerComputer.Height);
            using (LinearGradientBrush frameBrush = new LinearGradientBrush(whole, Color.FromArgb(236, 128, 226), Color.FromArgb(166, 34, 172), 55f))
            using (GraphicsPath outer = Round(new Rectangle(4, 4, HerComputer.Width - 8, HerComputer.Height - 8), 30))
                g.FillPath(frameBrush, outer);
            DrawWallpaper(g);
            foreach (HerSticker sticker in Computer.Stickers()) DrawSticker(g, sticker.Kind, new PointF(sticker.X, sticker.Y), 46, sticker.Tilt);
            if (app == null) DrawClock(g);
            foreach (HerIcon icon in Computer.Icons())
            {
                PointF place = icon.App == drag ? new PointF(at.X - 36, at.Y - 32) : new PointF(icon.X, icon.Y);
                DrawIcon(g, icon.App, place, true);
            }
            DrawDock(g, app);
            if (app != null) DrawApp(g, app, field, text, page, note, problem);
            if (said != null) DrawBubble(g, said, at);
            if (held != null) DrawSticker(g, held, new PointF(at.X + 26, at.Y + 26), 36, 0);
            DrawCursor(g, at, ripple);
            DrawBar(g, now);
        }

        private void DrawWallpaper(Graphics g)
        {
            int wallpaper = Computer.Wallpaper;
            Color top = wallpaper == 1 ? Color.FromArgb(240, 236, 255) : wallpaper == 2 ? Color.FromArgb(48, 26, 72) : Color.FromArgb(252, 238, 249);
            Color bottom = wallpaper == 1 ? Color.FromArgb(226, 220, 252) : wallpaper == 2 ? Color.FromArgb(24, 12, 40) : Color.FromArgb(246, 226, 244);
            using (GraphicsPath screen = Round(HerComputer.Screen, 22))
            using (LinearGradientBrush wall = new LinearGradientBrush(HerComputer.Screen, top, bottom, 90f))
            {
                g.FillPath(wall, screen);
                Random doodles = new Random(17);
                using (Pen faint = new Pen(Color.FromArgb(wallpaper == 2 ? 50 : 60, 190, 120, 190), 2f))
                    for (int i = 0; i < 22; i++)
                    {
                        float x = HerComputer.Screen.Left + 30 + doodles.Next(HerComputer.Screen.Width - 60), y = HerComputer.Screen.Top + 30 + doodles.Next(HerComputer.Screen.Height - 120);
                        int kind = doodles.Next(4);
                        if (kind == 0) g.DrawEllipse(faint, x, y, 22, 22);
                        else if (kind == 1) { g.DrawLine(faint, x, y + 10, x + 20, y + 10); g.DrawLine(faint, x + 10, y, x + 10, y + 20); }
                        else if (kind == 2) using (GraphicsPath heart = Heart(new PointF(x + 12, y + 12), 22)) g.DrawPath(faint, heart);
                        else g.DrawArc(faint, x, y, 24, 24, 200, 220);
                    }
            }
        }

        private void DrawClock(Graphics g)
        {
            bool night = Computer.Wallpaper == 2;
            using (Font big = new Font("Segoe UI Light", 104f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font hello = new Font("Segoe UI", 26f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(night ? Color.FromArgb(246, 220, 250) : Ink))
            using (SolidBrush soft = new SolidBrush(night ? Color.FromArgb(200, 170, 210) : SoftInk))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString(DateTime.Now.ToString("h:mm"), big, ink, new RectangleF(340, 200, 600, 140), centre);
                g.DrawString(Busy ? "busy busy busy" : "capri's computer", hello, soft, new RectangleF(340, 344, 600, 40), centre);
            }
        }

        private void DrawIcon(Graphics g, string app, PointF at, bool label)
        {
            RectangleF tile = new RectangleF(at.X + 4, at.Y, 64, 64);
            using (GraphicsPath shadow = Round(Rectangle.Round(new RectangleF(tile.X + 2, tile.Y + 4, tile.Width, tile.Height)), 16))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(40, 90, 20, 90)))
                g.FillPath(dim, shadow);
            using (GraphicsPath shape = Round(Rectangle.Round(tile), 16))
            using (SolidBrush white = new SolidBrush(Color.White))
                g.FillPath(white, shape);
            DrawGlyph(g, app, tile);
            if (!label) return;
            using (Font font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Computer.Wallpaper == 2 ? Color.White : Ink))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
                g.DrawString(HerComputer.AppNames[Math.Max(0, Array.IndexOf(HerComputer.Apps, app))], font, ink, new RectangleF(at.X - 10, at.Y + 68, 92, 20), centre);
        }

        private static void DrawGlyph(Graphics g, string app, RectangleF tile)
        {
            float cx = tile.X + tile.Width / 2, cy = tile.Y + tile.Height / 2, r = tile.Width * 0.3f;
            if (app == "browser")
            {
                using (SolidBrush blue = new SolidBrush(Color.FromArgb(92, 170, 255)))
                using (Pen white = new Pen(Color.White, 2.4f))
                {
                    g.FillEllipse(blue, cx - r, cy - r, 2 * r, 2 * r);
                    g.DrawEllipse(white, cx - r * 0.45f, cy - r, r * 0.9f, 2 * r);
                    g.DrawLine(white, cx - r, cy, cx + r, cy);
                    g.DrawLine(white, cx - r * 0.85f, cy - r * 0.5f, cx + r * 0.85f, cy - r * 0.5f);
                    g.DrawLine(white, cx - r * 0.85f, cy + r * 0.5f, cx + r * 0.85f, cy + r * 0.5f);
                }
            }
            else if (app == "notes")
            {
                using (SolidBrush yellow = new SolidBrush(Color.FromArgb(255, 214, 102)))
                using (Pen lines = new Pen(Color.FromArgb(170, 120, 40), 2f))
                {
                    g.FillRectangle(yellow, cx - r * 0.9f, cy - r, r * 1.8f, r * 2);
                    for (int i = 0; i < 4; i++) g.DrawLine(lines, cx - r * 0.6f, cy - r * 0.55f + i * r * 0.4f, cx + r * 0.6f, cy - r * 0.55f + i * r * 0.4f);
                }
            }
            else if (app == "files")
            {
                using (SolidBrush back = new SolidBrush(Color.FromArgb(70, 160, 230)))
                using (SolidBrush front = new SolidBrush(Color.FromArgb(110, 196, 255)))
                {
                    g.FillRectangle(back, cx - r, cy - r * 0.8f, r * 0.9f, r * 0.4f);
                    g.FillRectangle(back, cx - r, cy - r * 0.55f, r * 2, r * 1.5f);
                    g.FillRectangle(front, cx - r, cy - r * 0.25f, r * 2, r * 1.2f);
                }
            }
            else if (app == "diary")
            {
                using (SolidBrush cover = new SolidBrush(Pink))
                using (SolidBrush paper = new SolidBrush(Color.White))
                using (GraphicsPath heart = Heart(new PointF(cx + r * 0.1f, cy), r * 0.8f))
                {
                    g.FillRectangle(cover, cx - r * 0.85f, cy - r, r * 1.7f, r * 2);
                    g.FillRectangle(paper, cx - r * 0.85f, cy - r, r * 0.22f, r * 2);
                    g.FillPath(paper, heart);
                }
            }
            else DrawSticker(g, "star", new PointF(cx, cy), r * 2.2f, -10);
        }

        private void DrawDock(Graphics g, string app)
        {
            using (GraphicsPath dock = Round(DockBox, 20))
            using (SolidBrush glass = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
            using (Pen rim = new Pen(Color.FromArgb(120, 255, 255, 255), 2f))
            {
                g.FillPath(glass, dock);
                g.DrawPath(rim, dock);
            }
            for (int i = 0; i < HerComputer.Apps.Length; i++)
            {
                Rectangle slot = DockSlot(i);
                using (GraphicsPath shape = Round(slot, 12))
                using (SolidBrush white = new SolidBrush(Color.White))
                    g.FillPath(white, shape);
                DrawGlyph(g, HerComputer.Apps[i], slot);
                if (HerComputer.Apps[i] == app) using (SolidBrush dot = new SolidBrush(Pink)) g.FillEllipse(dot, slot.Left + slot.Width / 2 - 3, slot.Bottom + 2, 6, 6);
            }
        }

        private void DrawApp(Graphics g, string app, string field, string text, Image page, string note, string problem)
        {
            using (GraphicsPath shadow = Round(new Rectangle(AppBox.X + 4, AppBox.Y + 8, AppBox.Width, AppBox.Height), 14))
            using (SolidBrush dim = new SolidBrush(Color.FromArgb(50, 70, 10, 80)))
                g.FillPath(dim, shadow);
            using (GraphicsPath box = Round(AppBox, 14))
            using (SolidBrush paper = new SolidBrush(Paper))
            {
                g.FillPath(paper, box);
                Region kept = g.Clip;
                g.SetClip(box, CombineMode.Intersect);
                using (SolidBrush title = new SolidBrush(Color.FromArgb(248, 214, 240)))
                    g.FillRectangle(title, AppBox.Left, AppBox.Top, AppBox.Width, TitleTall);
                using (Font font = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush ink = new SolidBrush(Ink))
                {
                    int index = Math.Max(0, Array.IndexOf(HerComputer.Apps, app));
                    g.DrawString(HerComputer.AppNames[index], font, ink, AppBox.Left + 16, AppBox.Top + 7);
                    g.DrawString("✕", font, ink, AppBox.Right - 30, AppBox.Top + 7);
                    if (app != "browser" && app != "stickers") using (Font link = new Font("Segoe UI", 13f, FontStyle.Underline, GraphicsUnit.Pixel)) g.DrawString("open folder", link, ink, AppBox.Right - 150, AppBox.Top + 9);
                }
                if (app == "browser") DrawBrowser(g, field, text, page, problem);
                else if (app == "notes") DrawNotes(g, field, text, note);
                else if (app == "files") DrawFiles(g);
                else if (app == "diary") DrawDiary(g, field, text);
                else if (app == "stickers") DrawStickerBook(g);
                g.Clip = kept;
            }
        }

        private void DrawBrowser(Graphics g, string field, string text, Image page, string problem)
        {
            using (SolidBrush bar = new SolidBrush(Color.FromArgb(253, 236, 249)))
                g.FillRectangle(bar, AppBox.Left, AppBox.Top + TitleTall, AppBox.Width, ToolTall);
            using (Font font = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(SoftInk))
            {
                g.DrawString("←", font, ink, AppBox.Left + 12, AppBox.Top + TitleTall + 8);
                g.DrawString("→", font, ink, AppBox.Left + 48, AppBox.Top + TitleTall + 8);
                g.DrawString("↻", font, ink, AppBox.Left + 82, AppBox.Top + TitleTall + 8);
            }
            HerBrowser current = web;
            string shown = field == "address" && text != null ? text : current != null ? current.LastAddress : "";
            using (GraphicsPath pill = Round(AddressBox, 14))
            using (SolidBrush white = new SolidBrush(Color.White))
            using (Pen rim = new Pen(field == "address" ? Pink : Color.FromArgb(220, 180, 215), 2f))
            {
                g.FillPath(white, pill);
                g.DrawPath(rim, pill);
            }
            using (Font font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Ink))
            {
                string line = HerComputer.Short(shown ?? "", 110) + (field == "address" && DateTime.UtcNow.Millisecond < 500 ? "|" : "");
                g.DrawString(line.Length == 0 ? "search or type an address" : line, font, ink, AddressBox.Left + 14, AddressBox.Top + 5);
            }
            if (page != null) g.DrawImage(page, PageBox);
            else
                using (Font font = new Font("Segoe UI", 18f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush ink = new SolidBrush(SoftInk))
                using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                    g.DrawString(problem ?? (current == null ? "my browser's asleep" : "loading..."), font, ink, PageBox, centre);
        }

        private void DrawNotes(Graphics g, string field, string text, string note)
        {
            List<string> notes = Computer.Notes();
            using (SolidBrush side = new SolidBrush(Color.FromArgb(253, 240, 250)))
                g.FillRectangle(side, AppBox.Left, AppBox.Top + TitleTall, 290, AppBox.Height - TitleTall);
            using (Font font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font bold = new Font("Segoe UI Semibold", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush soft = new SolidBrush(SoftInk))
            using (SolidBrush pick = new SolidBrush(Color.FromArgb(250, 210, 240)))
            {
                if (notes.Count == 0 && field != "note") g.DrawString("no notes yet", font, soft, AppBox.Left + 20, AppBox.Top + TitleTall + 18);
                for (int i = 0; i < notes.Count && i < 12; i++)
                {
                    RectangleF row = new RectangleF(AppBox.Left + 8, AppBox.Top + TitleTall + 10 + i * 42, 272, 38);
                    if (notes[i] == note && field != "note") g.FillRectangle(pick, row);
                    g.DrawString(HerComputer.Short(Path.GetFileNameWithoutExtension(notes[i]), 30), bold, ink, row.X + 10, row.Y + 9);
                }
                RectangleF body = new RectangleF(AppBox.Left + 314, AppBox.Top + TitleTall + 18, AppBox.Width - 340, AppBox.Height - TitleTall - 30);
                string content = field == "note" && text != null ? text + (DateTime.UtcNow.Millisecond < 500 ? "|" : "") : HerComputer.Read(note, 6000);
                using (Font page = new Font("Segoe UI", 16f, FontStyle.Regular, GraphicsUnit.Pixel))
                    g.DrawString(content, page, ink, body);
            }
        }

        private void DrawFiles(Graphics g)
        {
            List<string> files = Computer.Files();
            using (Font font = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                if (files.Count == 0) g.DrawString("nothing here yet", font, ink, AppBox.Left + 30, AppBox.Top + TitleTall + 30);
                for (int i = 0; i < files.Count && i < 24; i++)
                {
                    Rectangle cell = FileCell(i);
                    DrawGlyph(g, files[i].EndsWith("diary.md", StringComparison.OrdinalIgnoreCase) ? "diary" : "notes", new RectangleF(cell.Left + 43, cell.Top + 4, 64, 64));
                    g.DrawString(HerComputer.Short(Path.GetFileName(files[i]), 22), font, ink, new RectangleF(cell.Left, cell.Top + 74, cell.Width, 40), centre);
                }
            }
        }

        private void DrawDiary(Graphics g, string field, string text)
        {
            RectangleF paper = new RectangleF(AppBox.Left + 60, AppBox.Top + TitleTall + 20, AppBox.Width - 120, AppBox.Height - TitleTall - 40);
            using (SolidBrush cream = new SolidBrush(Color.FromArgb(255, 250, 240)))
            using (Pen line = new Pen(Color.FromArgb(240, 200, 225), 1.5f))
            using (Pen margin = new Pen(Color.FromArgb(240, 140, 190), 2f))
            {
                g.FillRectangle(cream, paper);
                for (float y = paper.Top + 44; y < paper.Bottom; y += 34) g.DrawLine(line, paper.Left, y, paper.Right, y);
                g.DrawLine(margin, paper.Left + 170, paper.Top, paper.Left + 170, paper.Bottom);
            }
            List<string> lines = Computer.Diary(13);
            if (field == "diary" && text != null) lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + text + (DateTime.UtcNow.Millisecond < 500 ? "|" : ""));
            while (lines.Count > 14) lines.RemoveAt(0);
            using (Font font = new Font("Segoe Print", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font date = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush soft = new SolidBrush(SoftInk))
                for (int i = 0; i < lines.Count; i++)
                {
                    string entry = lines[i];
                    string when = entry.Length > 18 ? entry.Substring(0, 16) : "";
                    string words = entry.Length > 18 ? entry.Substring(18) : entry;
                    float y = paper.Top + 20 + i * 34;
                    g.DrawString(when, date, soft, paper.Left + 12, y + 8);
                    g.DrawString(HerComputer.Short(words, 90), font, ink, paper.Left + 180, y + 2);
                }
        }

        private void DrawStickerBook(Graphics g)
        {
            using (Font font = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush ink = new SolidBrush(SoftInk))
            using (SolidBrush card = new SolidBrush(Color.FromArgb(253, 238, 250)))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center })
            {
                g.DrawString("pick one and click her desktop to stick it", font, ink, AppBox.Left + 120, AppBox.Top + TitleTall + 22);
                for (int i = 0; i < HerComputer.StickerKinds.Length; i++)
                {
                    Rectangle cell = StickerCell(i);
                    using (GraphicsPath shape = Round(cell, 18)) g.FillPath(card, shape);
                    DrawSticker(g, HerComputer.StickerKinds[i], new PointF(cell.Left + cell.Width / 2, cell.Top + 62), 70, 0);
                    g.DrawString(HerComputer.StickerKinds[i], font, ink, new RectangleF(cell.Left, cell.Bottom - 34, cell.Width, 24), centre);
                }
            }
        }

        internal static void DrawSticker(Graphics g, string kind, PointF at, float size, int tilt)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(at.X, at.Y);
            g.RotateTransform(tilt);
            float r = size / 2;
            using (Pen edge = new Pen(Color.White, Math.Max(2f, size / 14f)) { LineJoin = LineJoin.Round })
            {
                GraphicsPath path = new GraphicsPath();
                Color fill = Color.FromArgb(255, 105, 180);
                switch (kind)
                {
                    case "heart": path = Heart(new PointF(0, 0), size); fill = Color.FromArgb(255, 80, 160); break;
                    case "star": path.AddPolygon(StarPoints(r, r * 0.45f, 5)); fill = Color.FromArgb(255, 205, 60); break;
                    case "moon":
                        path.AddEllipse(-r, -r, 2 * r, 2 * r);
                        using (GraphicsPath bite = new GraphicsPath()) { bite.AddEllipse(-r * 0.3f, -r * 1.05f, 2 * r, 2 * r); Region moon = new Region(path); moon.Exclude(bite); using (SolidBrush yellow = new SolidBrush(Color.FromArgb(250, 230, 140))) g.FillRegion(yellow, moon); moon.Dispose(); }
                        path.Dispose();
                        g.Restore(state);
                        return;
                    case "cat":
                        path.AddEllipse(-r * 0.8f, -r * 0.55f, r * 1.6f, r * 1.4f);
                        path.AddPolygon(new[] { new PointF(-r * 0.75f, -r * 0.2f), new PointF(-r * 0.6f, -r * 0.95f), new PointF(-r * 0.2f, -r * 0.5f) });
                        path.AddPolygon(new[] { new PointF(r * 0.75f, -r * 0.2f), new PointF(r * 0.6f, -r * 0.95f), new PointF(r * 0.2f, -r * 0.5f) });
                        fill = Color.FromArgb(70, 50, 80);
                        break;
                    case "mushroom":
                        path.AddPie(-r, -r * 0.8f, 2 * r, 1.6f * r, 180, 180);
                        path.AddRectangle(new RectangleF(-r * 0.3f, 0, r * 0.6f, r * 0.8f));
                        fill = Color.FromArgb(240, 70, 90);
                        break;
                    case "skull":
                        path.AddEllipse(-r * 0.8f, -r * 0.85f, r * 1.6f, r * 1.4f);
                        path.AddRectangle(new RectangleF(-r * 0.45f, r * 0.3f, r * 0.9f, r * 0.5f));
                        fill = Color.FromArgb(245, 240, 250);
                        break;
                    case "bolt":
                        path.AddPolygon(new[] { new PointF(r * 0.2f, -r), new PointF(-r * 0.6f, r * 0.15f), new PointF(-r * 0.05f, r * 0.15f), new PointF(-r * 0.25f, r), new PointF(r * 0.6f, -r * 0.2f), new PointF(r * 0.05f, -r * 0.2f) });
                        fill = Color.FromArgb(90, 230, 255);
                        break;
                    case "chrome":
                        path.AddEllipse(-r, -r, 2 * r, 2 * r);
                        fill = Color.FromArgb(200, 205, 225);
                        break;
                    case "flower":
                        for (int i = 0; i < 5; i++) { double a = i * Math.PI * 2 / 5; path.AddEllipse((float)Math.Cos(a) * r * 0.5f - r * 0.4f, (float)Math.Sin(a) * r * 0.5f - r * 0.4f, r * 0.8f, r * 0.8f); }
                        fill = Color.FromArgb(200, 140, 255);
                        break;
                    default:
                        path.AddEllipse(-r * 0.9f, -r * 0.1f, r * 0.9f, r * 0.9f);
                        path.AddEllipse(0, 0, r * 0.9f, r * 0.9f);
                        fill = Color.FromArgb(220, 30, 70);
                        break;
                }
                g.DrawPath(edge, path);
                if (kind == "chrome")
                    using (LinearGradientBrush shine = new LinearGradientBrush(new RectangleF(-r, -r, 2 * r, 2 * r), Color.White, Color.FromArgb(120, 120, 160), 60f)) g.FillPath(shine, path);
                else using (SolidBrush brush = new SolidBrush(fill)) g.FillPath(brush, path);
                if (kind == "cat" || kind == "skull")
                    using (SolidBrush eyes = new SolidBrush(kind == "cat" ? Color.FromArgb(255, 220, 90) : Color.FromArgb(60, 40, 70)))
                    {
                        g.FillEllipse(eyes, -r * 0.42f, -r * 0.2f, r * 0.26f, r * 0.26f);
                        g.FillEllipse(eyes, r * 0.16f, -r * 0.2f, r * 0.26f, r * 0.26f);
                    }
                if (kind == "mushroom") using (SolidBrush dots = new SolidBrush(Color.White)) { g.FillEllipse(dots, -r * 0.5f, -r * 0.55f, r * 0.3f, r * 0.3f); g.FillEllipse(dots, r * 0.15f, -r * 0.65f, r * 0.25f, r * 0.25f); }
                if (kind == "cherry") using (Pen stem = new Pen(Color.FromArgb(60, 140, 60), size / 16f)) { g.DrawLine(stem, -r * 0.45f, 0, r * 0.1f, -r * 0.8f); g.DrawLine(stem, r * 0.45f, r * 0.1f, r * 0.1f, -r * 0.8f); }
                path.Dispose();
            }
            g.Restore(state);
        }

        private static PointF[] StarPoints(float outer, float inner, int tips)
        {
            PointF[] points = new PointF[tips * 2];
            for (int i = 0; i < points.Length; i++)
            {
                double angle = -Math.PI / 2 + i * Math.PI / tips;
                float radius = i % 2 == 0 ? outer : inner;
                points[i] = new PointF((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
            }
            return points;
        }

        private void DrawBubble(Graphics g, string said, PointF at)
        {
            using (Font font = new Font("Segoe UI", 17f, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                SizeF size = g.MeasureString(said, font, 320);
                float x = Math.Max(HerComputer.Screen.Left + 10, Math.Min(HerComputer.Screen.Right - size.Width - 40, at.X - 40));
                float y = at.Y - size.Height - 50;
                if (y < HerComputer.Screen.Top + 10) y = at.Y + 40;
                Rectangle box = Rectangle.Round(new RectangleF(x, y, size.Width + 28, size.Height + 20));
                using (GraphicsPath shape = Round(box, 16))
                using (SolidBrush white = new SolidBrush(Color.White))
                using (Pen rim = new Pen(Color.FromArgb(240, 170, 225), 2f))
                using (SolidBrush ink = new SolidBrush(Ink))
                {
                    g.FillPath(white, shape);
                    g.DrawPath(rim, shape);
                    g.DrawString(said, font, ink, new RectangleF(box.Left + 14, box.Top + 10, size.Width + 2, size.Height + 2));
                }
            }
        }

        private static void DrawCursor(Graphics g, PointF at, DateTime ripple)
        {
            double since = (DateTime.UtcNow - ripple).TotalMilliseconds;
            if (since < 450)
                using (Pen ring = new Pen(Color.FromArgb((int)(200 * (1 - since / 450)), 236, 72, 190), 3f))
                {
                    float radius = 6 + (float)since / 18f;
                    g.DrawEllipse(ring, at.X - radius, at.Y - radius, radius * 2, radius * 2);
                }
            PointF[] arrow = { new PointF(at.X, at.Y), new PointF(at.X, at.Y + 26), new PointF(at.X + 7, at.Y + 20), new PointF(at.X + 12, at.Y + 31), new PointF(at.X + 17, at.Y + 29), new PointF(at.X + 12, at.Y + 18), new PointF(at.X + 21, at.Y + 18) };
            using (SolidBrush pink = new SolidBrush(Pink))
            using (Pen white = new Pen(Color.White, 2.2f) { LineJoin = LineJoin.Round })
            {
                g.FillPolygon(pink, arrow);
                g.DrawPolygon(white, arrow);
            }
        }

        private void DrawBar(Graphics g, string now)
        {
            using (GraphicsPath bar = Round(BarBox, 20))
            using (SolidBrush glass = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
                g.FillPath(glass, bar);
            Rectangle avatar = new Rectangle(BarBox.Left + 12, BarBox.Top + 9, 52, 52);
            using (SolidBrush ring = new SolidBrush(Color.White)) g.FillEllipse(ring, avatar);
            if (face != null)
            {
                GraphicsState state = g.Save();
                using (GraphicsPath circle = new GraphicsPath()) { circle.AddEllipse(avatar); g.SetClip(circle, CombineMode.Intersect); g.DrawImage(face, avatar); }
                g.Restore(state);
            }
            else DrawSticker(g, "heart", new PointF(avatar.Left + 26, avatar.Top + 27), 34, 0);
            if (Busy)
            {
                int pulse = (int)(155 + 100 * Math.Sin(DateTime.UtcNow.TimeOfDay.TotalMilliseconds / 180));
                using (SolidBrush live = new SolidBrush(Color.FromArgb(pulse, 90, 255, 160))) g.FillEllipse(live, avatar.Right - 12, avatar.Top + 2, 12, 12);
            }
            using (Font name = new Font("Segoe UI Semibold", 16f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Font font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
            using (SolidBrush white = new SolidBrush(Color.White))
            {
                g.DrawString("Capri", name, white, avatar.Right + 12, BarBox.Top + 12);
                g.DrawString(HerComputer.Short(now, 64), font, white, avatar.Right + 12, BarBox.Top + 36);
            }
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

        internal static GraphicsPath Heart(PointF centre, float size)
        {
            float r = size / 2;
            GraphicsPath path = new GraphicsPath();
            path.AddBezier(centre.X, centre.Y + r * 0.9f, centre.X - r * 1.3f, centre.Y + r * 0.1f, centre.X - r * 0.9f, centre.Y - r * 1.05f, centre.X, centre.Y - r * 0.35f);
            path.AddBezier(centre.X, centre.Y - r * 0.35f, centre.X + r * 0.9f, centre.Y - r * 1.05f, centre.X + r * 1.3f, centre.Y + r * 0.1f, centre.X, centre.Y + r * 0.9f);
            path.CloseFigure();
            return path;
        }

        internal Bitmap Snapshot()
        {
            Bitmap shot = new Bitmap(HerComputer.Width, HerComputer.Height);
            using (Graphics g = Graphics.FromImage(shot))
            {
                g.Clear(BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Draw(g);
                using (Font font = new Font("Segoe UI", 15f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (Font bold = new Font("Segoe UI", 14f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush white = new SolidBrush(Color.White))
                using (SolidBrush soft = new SolidBrush(SoftInk))
                using (SolidBrush pink = new SolidBrush(Pink))
                using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.FillRectangle(white, 660, 754, 340, 32);
                    g.DrawString(taskBox.Text.Length > 0 ? taskBox.Text : "give her something to do", font, soft, 668, 760);
                    g.FillRectangle(pink, 1010, 752, 116, 36);
                    g.FillRectangle(pink, 1134, 752, 116, 36);
                    g.DrawString(give.Text, bold, white, new RectangleF(1010, 752, 116, 36), centre);
                    g.DrawString(control.Text, bold, white, new RectangleF(1134, 752, 116, 36), centre);
                }
            }
            return shot;
        }
    }
}
