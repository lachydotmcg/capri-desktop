using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class HoloWindow : Form
    {
        internal bool FullSize { get; private set; }
        internal event Action ClosePressed, TalkPressed, TypePressed, MicPressed, EndPressed, FullSizePressed, CameraPressed, MenuPressed;

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        private const double Fade = 0.3, ChipHold = 3.0;
        private readonly Func<HoloView> read;
        private readonly HoloArt art;
        private readonly Timer tick;
        private readonly ToolTip tip = new ToolTip { ShowAlways = true, InitialDelay = 0 };
        private readonly Stopwatch time = new Stopwatch();
        private readonly Dictionary<HoloPose, Bitmap> faces = new Dictionary<HoloPose, Bitmap>();
        private readonly HoloScene scene = new HoloScene();
        private Native.Surface surface;
        private float scale = 1;
        private bool placed;
        private double openingOffset, chipShownAt = -10, chipShare;
        private HoloAction hover, pressed;
        private HoloPose state;
        private int glitchLeft, glitchSeed;
        private string drawn;

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
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            tick = new Timer { Interval = 33 };
            tick.Tick += delegate { Render(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000080 | (FullSize ? 0 : 0x08000000);
                return value;
            }
        }

        protected override bool ShowWithoutActivation { get { return !FullSize; } }

        internal double OpeningElapsed { get { return openingOffset + time.Elapsed.TotalSeconds; } }

        internal void ContinueOpening(double elapsed) { openingOffset = elapsed; }

        internal void OpenOn(Screen screen, bool above)
        {
            TopMost = FullSize || above;
            if (FullSize)
            {
                scale = Math.Max(1f, screen.Bounds.Height / 1080f);
                Bounds = screen.Bounds;
            }
            else if (!placed)
            {
                Rectangle work = screen.WorkingArea;
                scale = ClipToast.ScaleOf(work);
                Size wanted = HoloInk.CompactSize(scale);
                if (wanted.Width > work.Width - 24 || wanted.Height > work.Height - 24)
                {
                    scale *= Math.Min((work.Width - 24) / (float)wanted.Width, (work.Height - 24) / (float)wanted.Height);
                    wanted = HoloInk.CompactSize(scale);
                }
                Bounds = new Rectangle(work.Right - wanted.Width - (int)(16 * scale), work.Top + (int)(16 * scale), wanted.Width, wanted.Height);
                placed = true;
            }
            if (surface == null || surface.Image.Width != Width || surface.Image.Height != Height)
            {
                if (surface != null) surface.Dispose();
                surface = new Native.Surface(Width, Height);
            }
            if (!Visible)
            {
                time.Restart();
                chipShownAt = 0;
                Show();
            }
            drawn = null;
            Render();
            tick.Start();
            if (FullSize) Activate();
        }

        private Bitmap Face(HoloPose pose)
        {
            Size size = FullSize ? ClientSize : HoloInk.FaceSize(scale);
            Bitmap face;
            if (faces.TryGetValue(pose, out face) && face.Size == size) return face;
            if (face != null) face.Dispose();
            face = HoloInk.Scaled(art.Pose(pose), size, true);
            faces[pose] = face;
            return face;
        }

        private void Render()
        {
            if (IsDisposed || !IsHandleCreated || surface == null) return;
            HoloView view = read() ?? new HoloView();
            double now = time.Elapsed.TotalSeconds, opening = OpeningElapsed;
            bool introOn = view.Motion && opening < art.IntroSeconds;

            HoloPose next = view.Speaking ? HoloPose.Speaking : view.Thinking ? HoloPose.Thinking : HoloPose.Idle;
            if (next != state && !introOn && view.Motion) { glitchLeft = 4; glitchSeed = Environment.TickCount; }
            state = next;

            if (FullSize)
            {
                Point cursor = PointToClient(Cursor.Position);
                RectangleF chip = HoloInk.ActionBox(HoloAction.Exit, scale, true, ClientSize);
                if (RectangleF.Inflate(chip, 120 * scale, 90 * scale).Contains(cursor)) chipShownAt = now;
                double want = now - chipShownAt < ChipHold ? 1 : 0;
                chipShare = want > chipShare ? Math.Min(1, chipShare + 0.2) : Math.Max(0, chipShare - 0.1);
            }
            else if (!Bounds.Contains(Cursor.Position) && hover != HoloAction.None) SetHover(HoloAction.None);

            HoloPose pose = HoloInk.PoseOf(view, now);
            scene.View = view;
            scene.Time = now;
            scene.Face = Face(pose);
            scene.Opening = introOn ? art.Opening(opening) : null;
            scene.OpeningShare = introOn ? (float)Math.Min(1, (art.IntroSeconds - opening) / Fade) : 0;
            scene.Reveal = FullSize ? 1 : (float)Math.Min(1, opening / 0.6);
            scene.Glitch = glitchLeft > 0 ? glitchSeed + glitchLeft : 0;
            scene.Hover = hover;
            scene.ChipShare = (float)chipShare;
            if (glitchLeft > 0) glitchLeft--;

            bool sweep = !FullSize && view.Motion;
            byte alpha = FullSize && view.Motion && opening < 0.25 ? (byte)(255 * Math.Max(0, opening) / 0.25) : (byte)255;
            string key = pose + "|" + (introOn ? ((int)(opening * HoloArt.Fps)).ToString() : "-") + "|" + scene.Glitch + "|" + hover + "|" + (int)(chipShare * 20)
                + "|" + view.Caption + "|" + view.Captions + "|" + view.Muted + "|" + view.CameraOn + "|" + view.CanMute + "|" + view.CanEnd + "|" + view.CanCamera
                + "|" + view.Listening + "|" + view.CanTalk + "|" + view.TalkOn
                + "|" + view.InCall + "|" + view.CallName + "|" + (int)view.Elapsed.TotalSeconds + "|" + view.Thinking + "|" + view.Speaking
                + "|" + (int)(scene.Reveal * 30) + "|" + alpha + "|" + (sweep ? ((int)(now * 15)).ToString() : "");
            if (key != drawn)
            {
                using (Graphics canvas = Graphics.FromImage(surface.Image))
                {
                    canvas.Clear(Color.Transparent);
                    if (FullSize) HoloInk.DrawFull(canvas, ClientSize, scene);
                    else HoloInk.DrawCompact(canvas, scale, scene);
                }
                surface.Show(Handle, Location, alpha);
                drawn = key;
            }

            bool fast = introOn || glitchLeft > 0 || alpha < 255 || chipShare > 0 && chipShare < 1;
            tick.Interval = fast ? 33 : view.Speaking || sweep ? 66 : 150;
        }

        private void SetHover(HoloAction action)
        {
            if (hover == action) return;
            hover = action;
            drawn = null;
            if (action == HoloAction.None) { tip.Hide(this); return; }
            HoloView view = scene.View ?? new HoloView();
            RectangleF box = HoloInk.ActionBox(action, scale, FullSize, ClientSize);
            tip.Show(HoloInk.ActionText(action, view, FullSize), this, (int)box.Left, (int)(box.Bottom + 6 * scale), 2000);
        }

        private HoloAction ActionAt(Point point)
        {
            return HoloInk.ActionAt(point, scene.View ?? new HoloView(), scale, FullSize, ClientSize, (float)chipShare);
        }

        private void Press(HoloAction action)
        {
            HoloView view = read() ?? new HoloView();
            if (action == HoloAction.None || !HoloInk.ActionAvailable(action, view)) return;
            Action handler;
            switch (action)
            {
                case HoloAction.Exit: handler = ClosePressed; break;
                case HoloAction.Talk: handler = TalkPressed; break;
                case HoloAction.Type: handler = TypePressed; break;
                case HoloAction.Mic: handler = MicPressed; break;
                case HoloAction.End: handler = EndPressed; break;
                case HoloAction.Camera: handler = CameraPressed; break;
                case HoloAction.FullSize: handler = FullSizePressed; break;
                default: return;
            }
            if (handler != null) handler();
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keys)
        {
            if (keys == Keys.Escape) { Press(HoloAction.Exit); return true; }
            if (keys == Keys.F11) { Press(FullSize ? HoloAction.Exit : HoloAction.FullSize); return true; }
            return base.ProcessCmdKey(ref message, keys);
        }

        private bool Solid(Point point)
        {
            if (surface == null || point.X < 0 || point.Y < 0 || point.X >= surface.Image.Width || point.Y >= surface.Image.Height) return false;
            return Marshal.ReadByte(surface.Pixels, (point.Y * surface.Image.Width + point.X) * 4 + 3) >= 12;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084)
            {
                long value = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(value & 65535), (short)((value >> 16) & 65535)));
                if (ActionAt(point) != HoloAction.None) { message.Result = (IntPtr)1; return; }
                if (!FullSize)
                {
                    RectangleF face = RectangleF.Inflate(HoloInk.Unit(HoloInk.Portrait, scale), 8 * scale, 8 * scale);
                    bool row = point.Y >= 0 && point.Y < HoloInk.Portrait.Top * scale && point.X >= 0 && point.X < ClientSize.Width;
                    message.Result = (IntPtr)(face.Contains(point) || row ? 2 : -1);
                    return;
                }
                message.Result = (IntPtr)(Solid(point) ? 1 : -1);
                return;
            }
            if (message.Msg == 0x00A3 && !FullSize) { Press(HoloAction.FullSize); return; }
            if (message.Msg == 0x00A4 || message.Msg == 0x00A6) return;
            if (message.Msg == 0x00A5)
            {
                if (MenuPressed != null) MenuPressed();
                return;
            }
            if (message.Msg == 0x007B) return;
            base.WndProc(ref message);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            SetHover(ActionAt(e.Location));
            if (FullSize) chipShownAt = time.Elapsed.TotalSeconds;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            SetHover(HoloAction.None);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) pressed = ActionAt(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { if (MenuPressed != null) MenuPressed(); return; }
            if (e.Button != MouseButtons.Left) return;
            HoloAction was = pressed;
            pressed = HoloAction.None;
            if (was != HoloAction.None && was == ActionAt(e.Location)) Press(was);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (tick == null) return;
            if (Visible) { drawn = null; tick.Start(); }
            else tick.Stop();
        }

        internal bool Unseen(bool unseen)
        {
            if (!IsHandleCreated) return false;
            try { SetWindowDisplayAffinity(Handle, unseen ? 0x11u : 0u); }
            catch (EntryPointNotFoundException) { return false; }
            return unseen && Visible;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (tick != null) tick.Dispose();
                tip.Dispose();
                if (art != null) art.Dispose();
                foreach (Bitmap face in faces.Values) face.Dispose();
                faces.Clear();
                if (surface != null) { surface.Dispose(); surface = null; }
            }
            base.Dispose(disposing);
        }
    }
}
