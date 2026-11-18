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
        internal static readonly Rectangle DockBox = new Rectangle(HerComputer.Width / 2 - 181, 684, 362, 70);
        internal static readonly Rectangle CardBox = new Rectangle(280, 96, 720, 540);
        internal const int CardHead = 52;
        private static readonly Color Ground = Color.FromArgb(11, 7, 16), Plum = Color.FromArgb(22, 11, 18), Chrome = Color.FromArgb(42, 15, 29), Rose = Color.FromArgb(255, 93, 102), Blush = Color.FromArgb(255, 227, 234), Faded = Color.FromArgb(185, 150, 168), Mint = Color.FromArgb(61, 242, 138), Ink = Color.FromArgb(42, 20, 32), SoftInk = Color.FromArgb(160, 132, 143), Paper = Color.FromArgb(255, 250, 252), PaperHead = Color.FromArgb(255, 233, 240), Hot = Color.FromArgb(192, 23, 79);

        internal readonly HerComputer Computer;
        internal Func<string, string, string> Ask;
        internal Action<string> Tell;
        internal Func<bool> CanThink = delegate { return false; };
        internal static bool Quiet;

        private readonly object gate = new object();
        private readonly System.Windows.Forms.Timer ticker = new System.Windows.Forms.Timer();
        private readonly Random random = new Random();
        private readonly Dictionary<string, Image> art = new Dictionary<string, Image>();
        private readonly Font clockFont, bigFont, textFont, smallFont, boldFont, labelFont, monoFont, diaryFont;
        private PointF cursor = new PointF(700, 420), target = new PointF(700, 420);
        private string openApp, typingField, typed, status = "Just hanging out", bubble, selectedNote, heldSticker;
        private DateTime bubbleUntil, rippleAt = DateTime.MinValue, lastWatched = DateTime.MinValue, heyAt = DateTime.MinValue, brainDiaryAt = DateTime.MinValue;
        private Image frame, perch, face, pointer;
        private Bitmap backdrop;
        private RectangleF controlButton;
        private HerBrowser web;
        private string webProblem;
        private Thread taskThread, idleThread, frameThread;
        private volatile bool closing, stopTask, inControl;
        private volatile string runningTask;
        private float scale = 1f;
        private PointF offset;
        private bool quietShow;
        private DateTime watchedAt = DateTime.MinValue;

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
            clockFont = FileEditor.Title(78f);
            bigFont = Neon.Type(28f, true, GraphicsUnit.Pixel);
            textFont = Neon.Type(17f, false, GraphicsUnit.Pixel);
            smallFont = Neon.Type(14f, false, GraphicsUnit.Pixel);
            boldFont = Neon.Type(17f, true, GraphicsUnit.Pixel);
            labelFont = Neon.Type(22f, false, GraphicsUnit.Pixel);
            monoFont = Look.Mono(13f, FontStyle.Regular);
            diaryFont = new Font("Segoe Print", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
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
            ticker.Start();
            frameThread = new Thread(FrameLoop) { IsBackground = true, Name = "her computer frames" };
            frameThread.Start();
            idleThread = new Thread(IdleLoop) { IsBackground = true, Name = "her computer idle" };
            idleThread.Start();
            if (!Computer.Greeted)
            {
                Computer.Greeted = true;
                Bubble("This is my computer. Watch me, or take over.");
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

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticker.Dispose();
                foreach (Image image in art.Values) if (image != null) image.Dispose();
                foreach (IDisposable owned in new IDisposable[] { perch, face, pointer, backdrop, clockFont, bigFont, textFont, smallFont, boldFont, labelFont, monoFont, diaryFont }) if (owned != null) owned.Dispose();
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
            if (ContainsFocus && WindowState != FormWindowState.Minimized) lastWatched = DateTime.UtcNow;
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

        private void IdleLoop()
        {
            Thread.Sleep(6000);
            while (!closing)
            {
                int wait = 20000 + random.Next(40000);
                for (int waited = 0; waited < wait && !closing; waited += 250) Thread.Sleep(250);
                if (closing || Busy || inControl || Quiet) continue;
                bool minimized = false;
                try { Invoke(new Action(delegate { minimized = !Seen || !Visible; })); }
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

        internal string Start(string job)
        {
            if (string.IsNullOrWhiteSpace(job)) return "What should I do on my computer?";
            if (Busy) return "I'm still doing \"" + HerComputer.Short(runningTask, 60) + "\" on my computer. Say stop first if you want something else.";
            if (Ask == null || !CanThink()) { Bubble("I need a brain for jobs. Pick one in Mainframe."); return "I need a brain to do jobs on my computer. Pick one in Mainframe under BRAIN."; }
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
            if (answer == null) { Status("Stopped"); Bubble("Ok, stopped."); return; }
            if (note != null) lock (gate) selectedNote = note;
            Changed();
            Status("Done: " + HerComputer.Short(job, 50));
            Bubble("Done! It's in my notes.");
            Action<string> tell = Tell;
            if (tell != null) tell(HerComputer.Short(answer, 600));
        }

        private void ToggleControl()
        {
            inControl = !inControl;
            if (inControl) { stopTask = Busy; Status("You've got my mouse"); Bubble("Fine, you drive. Be gentle."); }
            else { Status("Just hanging out"); Bubble(Computer.Pick(new[] { "My turn again.", "Thank you. I'll take it from here.", "You left fingerprints." })); }
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
            return new Rectangle(CardBox.Left + 40 + (index % 4) * 164, CardBox.Top + CardHead + 70 + (index / 4) * 180, 148, 160);
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
            if (Busy && !inControl)
            {
                if ((DateTime.UtcNow - heyAt).TotalSeconds > 6 && HerComputer.Screen.Contains(System.Drawing.Point.Round(at))) { heyAt = DateTime.UtcNow; Bubble(Computer.Pick(HerComputer.HeyLines)); }
                return;
            }
            for (int i = 0; i < HerComputer.Apps.Length; i++)
                if (DockSlot(i).Contains(System.Drawing.Point.Round(at))) { OpenApp(HerComputer.Apps[i]); return; }
            bool card = app != null && app != "browser";
            if (card && new RectangleF(CardBox.Right - 48, CardBox.Top, 48, CardHead).Contains(at)) { lock (gate) openApp = null; return; }
            if (card && app != "stickers" && new RectangleF(CardBox.Right - 170, CardBox.Top, 120, CardHead).Contains(at)) { OpenFolder(); return; }
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
            }
            if (app != null) return;
            if (heldSticker != null && PageBox.Contains(System.Drawing.Point.Round(at)))
            {
                Computer.AddSticker(heldSticker, System.Drawing.Point.Round(at), random.Next(-20, 21));
                lock (gate) heldSticker = null;
                Bubble("Ooh, nice placement.");
                Status("Just hanging out");
                return;
            }
            for (int i = 0; i < HerComputer.Sites.Length; i++)
                if (SiteTile(i).Contains(System.Drawing.Point.Round(at))) { OpenSite(HerComputer.SiteAddresses[i]); return; }
        }

        private void OpenApp(string app)
        {
            lock (gate) openApp = app;
            if (app == "notes") lock (gate) if (selectedNote == null) { List<string> notes = Computer.Notes(); if (notes.Count > 0) selectedNote = notes[0]; }
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
            ThreadPool.QueueUserWorkItem(delegate { HerBrowser started = Browser(); if (started != null) started.Go(address); Status(Busy ? Doing : "Just hanging out"); });
        }

        private void OpenFolder() { Open(Computer.Folder); }

        private static void Open(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception) { }
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
                    b.Clear(Ground);
                    b.SmoothingMode = SmoothingMode.AntiAlias;
                    b.TranslateTransform(offset.X, offset.Y);
                    b.ScaleTransform(scale, scale);
                    DrawFrame(b);
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
            lock (gate) DrawAll(g, openApp, typingField, typed, bubble, status, cursor, frame, rippleAt, heldSticker, selectedNote, webProblem);
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

        private static void DrawFrame(Graphics g)
        {
            Rectangle box = HerComputer.Screen;
            using (GraphicsPath shape = Round(box, 40))
            {
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
                    for (int i = 0; i < 520; i++)
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
                            g.DrawString(problem ?? "Loading...", textFont, faded, PageBox, centre);
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
                using (SolidBrush soft = new SolidBrush(Color.FromArgb(242, 213, 225))) g.DrawString(Busy ? "Busy busy busy" : "Welcome back", labelFont, soft, new RectangleF(PageBox.Left, PageBox.Top + 258, PageBox.Width, 30), centre);
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
                    if (app != "stickers") g.DrawString("Open folder", smallFont, soft, CardBox.Right - 150, CardBox.Top + 17);
                }
                if (app == "notes") DrawNotes(g, field, text, note);
                else if (app == "files") DrawFiles(g);
                else if (app == "diary") DrawDiary(g, field, text);
                else if (app == "stickers") DrawStickerBook(g);
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

        private void DrawDiary(Graphics g, string field, string text)
        {
            RectangleF paper = new RectangleF(CardBox.Left + 28, CardBox.Top + CardHead + 20, CardBox.Width - 56, CardBox.Height - CardHead - 40);
            using (SolidBrush cream = new SolidBrush(Color.FromArgb(255, 251, 243)))
            using (Pen line = new Pen(Color.FromArgb(246, 214, 228), 1.2f))
            {
                g.FillRectangle(cream, paper);
                for (float y = paper.Top + 40; y < paper.Bottom; y += 34) g.DrawLine(line, paper.Left, y, paper.Right, y);
            }
            List<string> lines = Computer.Diary(12);
            if (field == "diary" && text != null) lines.Add(DateTime.Now.ToString("yyyy-MM-dd HH:mm") + "  " + text + (DateTime.UtcNow.Millisecond < 500 ? "|" : ""));
            while (lines.Count > 12) lines.RemoveAt(0);
            using (SolidBrush ink = new SolidBrush(Ink))
            using (SolidBrush rose = new SolidBrush(Hot))
                for (int i = 0; i < lines.Count; i++)
                {
                    string entry = lines[i];
                    string when = entry.Length > 18 ? entry.Substring(11, 5) : "";
                    string words = entry.Length > 18 ? entry.Substring(18) : entry;
                    float y = paper.Top + 14 + i * 34;
                    g.DrawString(when, smallFont, rose, paper.Left + 14, y + 6);
                    g.DrawString(HerComputer.Short(words, 80), diaryFont, ink, paper.Left + 70, y);
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
            string line = inControl ? "You have her mouse" : Busy ? "Capri is " + Lower(HerComputer.Short(now, 48)) : Lower(now) == "just hanging out" ? "Capri is pottering about" : HerComputer.Short(now, 48);
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
