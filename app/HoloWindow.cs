using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class HoloView
    {
        internal bool InCall, Speaking, Thinking, Muted, CanMute, CanEnd;
        internal bool Motion = true, Captions = true;
        internal bool CanCamera, CameraOn;
        internal string Caption, CallName;
        internal TimeSpan Elapsed;
    }

    // Qewiw's Open holocall mode Copy.mov, Chilling normal, Speaking and Thinking.
    // The original clip runs at 30 fps. Keep only its current decoded frame in memory.
    internal sealed class HoloArt : IDisposable
    {
        internal const int Fps = 30;
        private Bitmap idle, speaking, thinking, opening;
        private ZipArchive intro;
        private int shown = -1;

        internal HoloArt(Assembly source, bool withOpening = true)
        {
            try
            {
                idle = LoadPose(source, "idle");
                speaking = LoadPose(source, "speaking");
                thinking = LoadPose(source, "thinking");
                if (withOpening)
                {
                    Stream frames = Open(source, "intro-30.zip");
                    try { intro = new ZipArchive(frames, ZipArchiveMode.Read); }
                    catch { frames.Dispose(); throw; }
                }
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private static Stream Open(Assembly source, string name)
        {
            Stream found = source.GetManifestResourceStream("Capri.Holo." + name);
            if (found == null) throw new InvalidDataException("Missing Holocall art " + name + ".");
            return found;
        }

        private static Bitmap LoadPose(Assembly source, string pose)
        {
            using (Stream image = Open(source, pose + ".png")) return Decode(image);
        }

        private static Bitmap Decode(Stream stream)
        {
            using (Image image = Image.FromStream(stream, false, false)) return new Bitmap(image);
        }

        internal double IntroSeconds { get { return intro == null ? 0 : intro.Entries.Count / (double)Fps; } }

        internal Bitmap Pose(HoloView view)
        {
            if (view.Speaking) return speaking;
            return view.Thinking ? thinking : idle;
        }

        internal Bitmap Frame(HoloView view, double elapsed)
        {
            if (!view.Motion || elapsed < 0 || elapsed >= IntroSeconds)
            {
                ClearOpening();
                return Pose(view);
            }
            int want = (int)(elapsed * Fps);
            if (want != shown)
            {
                using (Stream frame = intro.GetEntry(want.ToString("D3", CultureInfo.InvariantCulture) + ".jpg").Open())
                {
                    Bitmap next = Decode(frame);
                    ClearOpening();
                    opening = next;
                    shown = want;
                }
            }
            return opening;
        }

        private void ClearOpening()
        {
            if (opening != null) opening.Dispose();
            opening = null;
            shown = -1;
        }

        public void Dispose()
        {
            ClearOpening();
            if (intro != null) { intro.Dispose(); intro = null; }
            if (idle != null) { idle.Dispose(); idle = null; }
            if (speaking != null) { speaking.Dispose(); speaking = null; }
            if (thinking != null) { thinking.Dispose(); thinking = null; }
        }
    }

    internal enum HoloAction { Exit, Mic, FullSize, Camera, End }

    internal static class HoloInk
    {
        internal const int Width = 496, Height = 359;
        internal static readonly Color Stage = Color.FromArgb(15, 14, 25);
        private static readonly Color Shell = Color.FromArgb(16, 19, 28);
        private static readonly Color Rose = Color.FromArgb(255, 83, 122);
        private static readonly Color Aqua = Color.FromArgb(103, 222, 226);
        private static readonly Color Chalk = Color.FromArgb(237, 239, 248);
        private static readonly Color Quiet = Color.FromArgb(162, 172, 191);

        private static readonly Font Title = new Font("Bahnschrift SemiCondensed", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        private static readonly Font Label = new Font("Segoe UI Semibold", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        private static readonly Font Mono = new Font("Consolas", 12, FontStyle.Regular, GraphicsUnit.Pixel);

        private static readonly RectangleF StageBox = new RectangleF(14, 47, 468, 263.25f);

        internal static RectangleF Fit(Size source, RectangleF bounds)
        {
            float scale = Math.Min(bounds.Width / source.Width, bounds.Height / source.Height);
            float w = source.Width * scale, h = source.Height * scale;
            return new RectangleF(bounds.X + (bounds.Width - w) / 2, bounds.Y + (bounds.Height - h) / 2, w, h);
        }

        internal static void DrawAvatar(Graphics graphics, RectangleF bounds, Image frame)
        {
            using (SolidBrush background = new SolidBrush(Stage)) graphics.FillRectangle(background, bounds);
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(frame, Fit(frame.Size, bounds));
        }

        internal static byte[] CameraFrame(Image portrait)
        {
            using (Bitmap frame = new Bitmap(1280, 720, PixelFormat.Format24bppRgb))
            using (Graphics canvas = Graphics.FromImage(frame))
            using (MemoryStream encoded = new MemoryStream())
            {
                DrawAvatar(canvas, new RectangleF(0, 0, frame.Width, frame.Height), portrait);
                frame.Save(encoded, ImageFormat.Jpeg);
                return encoded.ToArray();
            }
        }

        internal static void Draw(Graphics graphics, Size bounds, bool fullSize, HoloView view, Image frame)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            RectangleF screen = new RectangleF(0, 0, bounds.Width, bounds.Height);
            if (fullSize)
            {
                DrawAvatar(graphics, screen, frame);
                DrawCaption(graphics, screen, view, Math.Max(16, bounds.Height / 45), true);
                return;
            }

            GraphicsState saved = graphics.Save();
            try
            {
                graphics.ScaleTransform(bounds.Width / (float)Width, bounds.Height / (float)Height);
                DrawCompact(graphics, view, frame);
            }
            finally { graphics.Restore(saved); }
        }

        private static void DrawCompact(Graphics graphics, HoloView view, Image frame)
        {
            using (SolidBrush baseInk = new SolidBrush(Shell)) graphics.FillRectangle(baseInk, 0, 0, Width, Height);
            using (LinearGradientBrush gradient = new LinearGradientBrush(new Rectangle(0, 0, Width, 46),
                Color.FromArgb(40, 20, 32), Shell, LinearGradientMode.Horizontal))
            {
                graphics.FillRectangle(gradient, 0, 0, Width, 46);
            }
            using (Pen seam = new Pen(Color.FromArgb(52, 62, 81)))
            {
                graphics.DrawLine(seam, 14, 41, Width - 14, 41);
                graphics.DrawLine(seam, 14, 313, Width - 14, 313);
            }
            DrawAvatar(graphics, StageBox, frame);
            DrawCaption(graphics, StageBox, view, 13, false);

            Corner(graphics, new PointF(3, 3), 1, 1, Rose);
            Corner(graphics, new PointF(Width - 4, 3), -1, 1, Aqua);
            Corner(graphics, new PointF(3, Height - 4), 1, -1, Aqua);
            Corner(graphics, new PointF(Width - 4, Height - 4), -1, -1, Rose);

            Write(graphics, "CAPRI", Title, Chalk, new RectangleF(17, 6, 73, 31), ContentAlignment.MiddleLeft);
            string status = view.Speaking ? "SPEAKING" : view.Thinking ? "THINKING" : "IN CALL";
            if (!string.IsNullOrEmpty(view.CallName)) status = view.CallName.ToUpperInvariant() + "  /  " + status;
            using (SolidBrush signal = new SolidBrush(view.Speaking ? Rose : Aqua)) graphics.FillEllipse(signal, 97, 19, 5, 5);
            Write(graphics, status, Mono, Quiet, new RectangleF(111, 6, 253, 31), ContentAlignment.MiddleLeft);
            Write(graphics, Clock(view.Elapsed), Mono, Chalk, new RectangleF(379, 6, 60, 31), ContentAlignment.MiddleRight);
        }

        private static string Clock(TimeSpan elapsed)
        {
            return ((int)Math.Max(0, elapsed.TotalMinutes)).ToString("D2", CultureInfo.InvariantCulture)
                + ":" + Math.Max(0, elapsed.Seconds).ToString("D2", CultureInfo.InvariantCulture);
        }

        private static void DrawCaption(Graphics graphics, RectangleF stage, HoloView view, int pixels, bool fullSize)
        {
            if (!view.Captions || string.IsNullOrWhiteSpace(view.Caption)) return;
            float margin = fullSize ? pixels * 2 : 9;
            float width = Math.Min(stage.Width - margin * 2, pixels * 42);
            if (width <= 0) return;
            float height = pixels * 3;
            RectangleF well = new RectangleF(stage.Left + (stage.Width - width) / 2, stage.Bottom - margin - height, width, height);
            using (Font words = new Font("Segoe UI Semibold", pixels, FontStyle.Regular, GraphicsUnit.Pixel))
            using (StringFormat format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord })
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(219, 11, 12, 22)))
            using (SolidBrush ink = new SolidBrush(Chalk))
            {
                graphics.FillRectangle(fill, well);
                well.Inflate(-8, -1);
                graphics.DrawString(view.Caption, words, ink, well, format);
            }
        }

        private static void Corner(Graphics graphics, PointF point, int horizontal, int vertical, Color colour)
        {
            using (Pen pen = new Pen(colour, 1.35f))
            {
                graphics.DrawLines(pen, new[] {
                    new PointF(point.X, point.Y + vertical * 22),
                    new PointF(point.X, point.Y + vertical * 8),
                    new PointF(point.X + horizontal * 8, point.Y),
                    new PointF(point.X + horizontal * 34, point.Y)
                });
            }
        }

        internal static RectangleF ActionBox(HoloAction action, Size bounds, bool fullSize)
        {
            if (fullSize)
            {
                return action == HoloAction.Exit
                    ? new RectangleF(Math.Max(0, bounds.Width - 132), 18, 114, 30)
                    : RectangleF.Empty;
            }
            RectangleF unit;
            switch (action)
            {
                case HoloAction.Exit: unit = new RectangleF(451, 8, 28, 26); break;
                case HoloAction.Mic: unit = new RectangleF(15, 322, 106, 24); break;
                case HoloAction.FullSize: unit = new RectangleF(129, 322, 105, 24); break;
                case HoloAction.Camera: unit = new RectangleF(242, 322, 130, 24); break;
                default: unit = new RectangleF(388, 322, 94, 24); break;
            }
            float sx = bounds.Width / (float)Width, sy = bounds.Height / (float)Height;
            return new RectangleF(unit.X * sx, unit.Y * sy, unit.Width * sx, unit.Height * sy);
        }

        internal static string ActionText(HoloAction action, HoloView view, bool fullSize)
        {
            switch (action)
            {
                case HoloAction.Exit: return fullSize ? "Exit fullscreen" : "\u00d7";
                case HoloAction.Mic: return view.Muted ? "Unmute mic" : "Mute mic";
                case HoloAction.FullSize: return "Full size";
                case HoloAction.Camera: return view.CameraOn ? "Stop camera feed" : "Capri Camera";
                default: return "End call";
            }
        }

        internal static bool ActionAvailable(HoloAction action, HoloView view)
        {
            if (action == HoloAction.Mic) return view.CanMute;
            if (action == HoloAction.Camera) return view.CanCamera;
            return action != HoloAction.End || view.CanEnd;
        }

        internal static void DrawAction(Graphics graphics, RectangleF bounds, HoloAction action, HoloView view, bool fullSize, bool hot, bool focused)
        {
            if (bounds.Width <= 1 || bounds.Height <= 1 || !ActionAvailable(action, view)) return;
            bool end = action == HoloAction.End;
            Color edge = end ? Color.FromArgb(129, 54, 73) : Color.FromArgb(68, 87, 105);
            Color fill = end ? Color.FromArgb(58, 27, 40) : Color.FromArgb(26, 34, 46);
            if (hot || focused) edge = end ? Rose : Aqua;
            using (SolidBrush surface = new SolidBrush(fill)) graphics.FillRectangle(surface, bounds);
            using (Pen stroke = new Pen(edge))
                graphics.DrawRectangle(stroke, bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1, bounds.Height - 1);
            Write(graphics, ActionText(action, view, fullSize), Label, end ? Color.FromArgb(255, 184, 196) : Chalk, bounds, ContentAlignment.MiddleCenter);
        }

        private static void Write(Graphics graphics, string text, Font font, Color ink, RectangleF at, ContentAlignment align)
        {
            using (SolidBrush brush = new SolidBrush(ink))
            using (StringFormat type = new StringFormat
            {
                Alignment = align == ContentAlignment.MiddleCenter ? StringAlignment.Center : align == ContentAlignment.MiddleRight ? StringAlignment.Far : StringAlignment.Near,
                LineAlignment = StringAlignment.Center,
                FormatFlags = StringFormatFlags.NoWrap,
                Trimming = StringTrimming.EllipsisCharacter
            })
                graphics.DrawString(text, font, brush, at, type);
        }
    }

    internal sealed class HoloWindow : Form
    {
        internal bool FullSize { get; private set; }
        internal event Action ClosePressed, MicPressed, EndPressed, FullSizePressed, CameraPressed;
        private readonly Func<HoloView> read;
        private readonly HoloArt art;
        private readonly Timer tick;
        private readonly Stopwatch time = new Stopwatch();
        private readonly ActionButton[] actions;
        private HoloView view = new HoloView();
        private bool placed;
        private double openingOffset;
        private Point dragFrom, locationFrom;

        private sealed class ActionButton : Button
        {
            private readonly HoloWindow window;
            internal readonly HoloAction Action;
            private bool hot;

            internal ActionButton(HoloWindow window, HoloAction action)
            {
                this.window = window;
                Action = action;
                FlatStyle = FlatStyle.Flat;
                AccessibleRole = AccessibleRole.PushButton;
                SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                HoloInk.DrawAction(e.Graphics, ClientRectangle, Action, window.view, window.FullSize, hot, Focused);
            }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hot = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hot = false; Invalidate(); }
        }

        internal HoloWindow(bool fullSize, Func<HoloView> read)
        {
            if (read == null) throw new ArgumentNullException("read");
            FullSize = fullSize;
            this.read = read;
            art = new HoloArt(typeof(HoloWindow).Assembly);
            Text = fullSize ? "Capri full size" : "Capri Holocall";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            ShowInTaskbar = false;
            BackColor = HoloInk.Stage;
            ClientSize = new Size(HoloInk.Width, HoloInk.Height);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            actions = fullSize
                ? new[] { new ActionButton(this, HoloAction.Exit) }
                : new[] { new ActionButton(this, HoloAction.Exit), new ActionButton(this, HoloAction.Mic),
                    new ActionButton(this, HoloAction.FullSize), new ActionButton(this, HoloAction.Camera), new ActionButton(this, HoloAction.End) };
            foreach (ActionButton action in actions)
            {
                action.Click += delegate { Press(action.Action); };
                Controls.Add(action);
            }
            UpdateActions();
            tick = new Timer { Interval = 16 };
            tick.Tick += delegate { RefreshView(); };
        }

        protected override bool ShowWithoutActivation { get { return !FullSize; } }

        internal double OpeningElapsed { get { return openingOffset + time.Elapsed.TotalSeconds; } }

        internal void ContinueOpening(double elapsed) { openingOffset = elapsed; }

        internal void OpenOn(Screen screen, bool above)
        {
            TopMost = FullSize || above;
            if (FullSize) Bounds = screen.Bounds;
            else if (!placed)
            {
                Rectangle work = screen.WorkingArea;
                float scale = Math.Min(1f, Math.Min((work.Width - 24) / (float)HoloInk.Width, (work.Height - 24) / (float)HoloInk.Height));
                Size = new Size(Math.Max(1, (int)(HoloInk.Width * scale)), Math.Max(1, (int)(HoloInk.Height * scale)));
                Location = new Point(work.Right - Width - 20, work.Top + 20);
                placed = true;
            }
            RefreshView();
            if (!Visible)
            {
                time.Restart();
                Show();
            }
            if (FullSize) Activate();
        }

        private void RefreshView()
        {
            HoloView previous = view;
            view = read() ?? new HoloView();
            tick.Interval = view.Motion && OpeningElapsed < art.IntroSeconds ? 16 : 100;
            if (previous.Muted != view.Muted || previous.CanMute != view.CanMute || previous.CanEnd != view.CanEnd
                || previous.CanCamera != view.CanCamera || previous.CameraOn != view.CameraOn) UpdateActions();
            if (view.Motion && OpeningElapsed < art.IntroSeconds + 0.2
                || previous.Elapsed.Seconds != view.Elapsed.Seconds || previous.InCall != view.InCall || previous.Speaking != view.Speaking
                || previous.Thinking != view.Thinking || previous.Captions != view.Captions || previous.Motion != view.Motion
                || previous.Caption != view.Caption || previous.CallName != view.CallName)
                Invalidate();
        }

        private void UpdateActions()
        {
            if (actions == null) return;
            foreach (ActionButton action in actions)
            {
                action.Visible = HoloInk.ActionAvailable(action.Action, view);
                action.Bounds = Rectangle.Round(HoloInk.ActionBox(action.Action, ClientSize, FullSize));
                action.AccessibleName = !FullSize && action.Action == HoloAction.Exit
                    ? "Close Holocall display" : HoloInk.ActionText(action.Action, view, FullSize);
                action.Invalidate();
            }
        }

        private void Press(HoloAction action)
        {
            Action handler = action == HoloAction.Exit ? ClosePressed : action == HoloAction.Mic ? MicPressed
                : action == HoloAction.End ? EndPressed : action == HoloAction.Camera ? CameraPressed : FullSizePressed;
            if (handler != null && HoloInk.ActionAvailable(action, view)) handler();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == Keys.Escape) { Press(HoloAction.Exit); return true; }
            if (keys == Keys.F11) { Press(FullSize ? HoloAction.Exit : HoloAction.FullSize); return true; }
            return base.ProcessCmdKey(ref message, keys);
        }

        protected override void OnResize(EventArgs e) { base.OnResize(e); UpdateActions(); }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (tick == null) return;
            if (Visible) tick.Start();
            else tick.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (ClientSize.Width > 0 && ClientSize.Height > 0)
                HoloInk.Draw(e.Graphics, ClientSize, FullSize, view, art.Frame(view, OpeningElapsed));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (FullSize || e.Button != MouseButtons.Left || e.Y > Height * 42 / HoloInk.Height) return;
            dragFrom = Cursor.Position;
            locationFrom = Location;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Capture || e.Button != MouseButtons.Left) return;
            Point now = Cursor.Position;
            Rectangle work = Screen.FromPoint(now).WorkingArea;
            Location = new Point(Math.Max(work.Left, Math.Min(work.Right - Width, locationFrom.X + now.X - dragFrom.X)),
                Math.Max(work.Top, Math.Min(work.Bottom - Height, locationFrom.Y + now.Y - dragFrom.Y)));
        }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); Capture = false; }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tick != null) tick.Dispose();
                if (art != null) art.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
