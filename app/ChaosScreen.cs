using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her crash is just a five-second plate in her own look, with a quick way out and no PC changes (Lachy, 2 Nov).
    internal sealed class ChaosScreen : Overlay
    {
        internal const double Seconds = 5, RevealAt = 3.7;
        internal const string Headline = "CAPRI.EXE\nHAS TAKEN OVER", StopCode = "CHOOM_OVERFLOW", Punchline = "jk. you're fine, choom";
        private readonly Rectangle room;
        private readonly Stopwatch clock;
        private readonly Timer ticks = new Timer();
        private readonly Func<bool> blocked;
        private Native.Surface surface;
        private double born, drawAt;
        internal readonly bool Motion;
        internal int Percent;
        internal bool Revealed;
        internal Func<bool> EscDown = delegate { return (Native.GetAsyncKeyState(0x1B) & 0x8000) != 0; };

        internal ChaosScreen(Rectangle area, Stopwatch time, bool motion, Func<bool> fullScreen) : base("Capri fake crash")
        {
            room = area;
            clock = time;
            Motion = motion;
            blocked = fullScreen;
            ticks.Interval = 40;
            ticks.Tick += delegate
            {
                try { Advance(clock.Elapsed.TotalSeconds); }
                catch (Exception) { Close(); }
            };
        }

        internal Bitmap Canvas { get { return surface == null ? null : surface.Image; } }
        internal bool Running { get { return ticks.Enabled; } }
        internal string Said { get { return "CAPRI // CHAOS\nFAKE CRASH\n" + Headline + "\ntoo much of me. the desktop couldn't cope.\nstop code: " + StopCode + "\n" + Percent + "% Capri\n" + (Revealed ? Punchline : "no reboot. just me being a menace.") + "\nclick anywhere or press esc"; } }

        internal bool Open()
        {
            if (blocked()) return false;
            Bounds = room;
            surface = new Native.Surface(room.Width, room.Height);
            born = clock.Elapsed.TotalSeconds;
            Percent = Motion ? 0 : 100;
            Revealed = !Motion;
            Render();
            if (blocked()) return false;
            Show();
            Native.SetWindowPos(Handle, new IntPtr(-1), room.X, room.Y, room.Width, room.Height, 0x0010);
            ticks.Start();
            return true;
        }

        internal void Advance(double now)
        {
            if (IsDisposed || surface == null) return;
            double passed = Math.Max(0, now - born);
            if (EscDown() || blocked() || passed >= Seconds) { Close(); return; }
            if (!Motion) return;
            int percent = Math.Min(100, (int)(100 * passed / RevealAt));
            bool revealed = passed >= RevealAt;
            if (Revealed == revealed && (now < drawAt || percent == Percent)) return;
            Percent = percent;
            Revealed = revealed;
            drawAt = now + 0.12;
            Render();
        }

        // She stays out of keyboard focus, so Esc is polled as well as handled here. Every mouse button dismisses her plate.
        protected override void WndProc(ref Message message)
        {
            int kind = message.Msg;
            if ((kind == 0x0100 || kind == 0x0104) && message.WParam.ToInt32() == (int)Keys.Escape
                || kind == 0x0201 || kind == 0x0203 || kind == 0x0204 || kind == 0x0206 || kind == 0x0207 || kind == 0x0209 || kind == 0x020B || kind == 0x020D
                || kind == 0x00A1 || kind == 0x00A3 || kind == 0x00A4 || kind == 0x00A6 || kind == 0x00A7 || kind == 0x00A9 || kind == 0x00AB || kind == 0x00AD)
            {
                message.Result = new IntPtr(1);
                Close();
                return;
            }
            if (kind == 0x0084) { message.Result = new IntPtr(1); return; }
            if (kind == 0x0021) { message.Result = new IntPtr(3); return; }
            if (kind == 0x02E0) return;
            base.WndProc(ref message);
        }

        private void Render()
        {
            using (Graphics graphics = Graphics.FromImage(surface.Image))
            {
                graphics.Clear(Neon.Ground);
                float s = Math.Min(room.Width / 1120f, room.Height / 720f);
                Neon.Backdrop(graphics, room.Size, 360 * s);
                graphics.TranslateTransform((room.Width - 1000 * s) / 2, (room.Height - 600 * s) / 2);
                graphics.ScaleTransform(s, s);
                using (GraphicsPath plate = Neon.Cut(new RectangleF(0, 0, 1000, 600), 32, 8, 32, 8))
                using (Brush face = new SolidBrush(Neon.Ground)) graphics.FillPath(face, plate);
                Neon.Frame(graphics, new Size(1000, 600), 1, 32, 8, 110, 65);
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (Font small = Look.Mono(17, FontStyle.Regular))
                using (Font text = Look.Mono(22, FontStyle.Regular))
                using (Font title = Neon.Display(72, GraphicsUnit.Pixel))
                using (Font count = Neon.Display(60, GraphicsUnit.Pixel))
                using (Brush cyan = new SolidBrush(Neon.Cyan))
                using (Brush rose = new SolidBrush(Neon.Rose))
                using (Brush white = new SolidBrush(Neon.White))
                using (Brush faint = new SolidBrush(Neon.Faint))
                using (Brush well = new SolidBrush(Neon.Deep))
                {
                    graphics.DrawString("CAPRI // CHAOS", small, cyan, 48, 34);
                    graphics.DrawString("FAKE CRASH", small, rose, 820, 34);
                    graphics.DrawString(Headline, title, rose, new RectangleF(44, 91, 920, 185));
                    graphics.DrawString("too much of me. the desktop couldn't cope.", text, white, 48, 290);
                    graphics.DrawString("stop code: " + StopCode, small, cyan, 48, 338);
                    graphics.DrawString(Percent + "%", count, cyan, 44, 380);
                    graphics.DrawString("Capri", small, faint, 190, 412);
                    for (int block = 0; block < 25; block++) graphics.FillRectangle(block * 4 < Percent ? cyan : well, 48 + block * 36, 459, 30, 8);
                    graphics.DrawString(Revealed ? Punchline : "no reboot. just me being a menace.", text, Revealed ? cyan : white, 48, 489);
                    graphics.DrawString("click anywhere or press esc", small, faint, 48, 552);
                }
            }
            surface.Show(Handle, room.Location);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticks.Dispose();
                if (surface != null) { surface.Dispose(); surface = null; }
            }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class Companion
    {
        private ChaosScreen chaosScreen;
        private double chaosScreenAt = double.PositiveInfinity;
        private bool chaosScreenUsed;

        // The clones' own session owns her one prank. Full screen delays it, and stopping them cancels it entirely.
        private void ChaosTick(double now)
        {
            if (cloneWindow == null || quitting) { ChaosOff(); return; }
            if (chaosScreen != null || chaosScreenUsed || now < chaosScreenAt || FullScreen()) return;
            Point centre = new Point(anchor.X + Width / 2, anchor.Y + Height / 2);
            ChaosScreen opened = new ChaosScreen(Screen.FromPoint(centre).Bounds, clock, preferences.Motion, delegate { return FullScreen(); });
            try
            {
                if (!opened.Open()) { opened.Dispose(); return; }
                chaosScreenUsed = true;
                chaosScreen = opened;
                opened.FormClosed += delegate { if (chaosScreen == opened) chaosScreen = null; };
            }
            catch (Exception)
            {
                chaosScreenUsed = true;
                opened.Dispose();
            }
        }

        private void ChaosOff()
        {
            chaosScreenAt = double.PositiveInfinity;
            chaosScreenUsed = false;
            ChaosScreen closing = chaosScreen;
            chaosScreen = null;
            if (closing != null) closing.Close();
        }
    }
}
