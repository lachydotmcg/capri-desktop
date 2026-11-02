using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her crash is a five-second Windows blue screen, with a quick way out and no PC changes (Lachy, 2 Nov). It looks like Windows'
    // own (Lachy, 3 Nov: "It should be like this instead!"); her stop code is the tell, and before it goes her ":(" turns to ":)".
    internal sealed class ChaosScreen : Overlay
    {
        internal const double Seconds = 5, RevealAt = 3.7;
        internal const string StopCode = "CHOOM_OVERFLOW", Punchline = "jk. you're fine, choom";
        // Windows' wording, broken where it breaks at 1080p.
        private static readonly string[] Message = { "Your PC ran into a problem and needs to restart. We're", "just collecting some error info, and then we'll restart for", "you." };
        private static readonly Color Blue = Color.FromArgb(0, 120, 215);
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
        internal string Said { get { return "CAPRI // CHAOS\nFAKE CRASH\n" + (Revealed ? ":)" : ":(") + "\n" + string.Join(" ", Message) + "\n" + (Revealed ? Punchline : Percent + "% complete") + "\nFor more information about this issue and possible fixes, visit https://www.windows.com/stopcode\nIf you call a support person, give them this info:\nStop code: " + StopCode + "\nclick anywhere or press esc"; } }

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

        // Laid out on Windows' own 1080p screen and scaled to fit hers.
        private void Render()
        {
            using (Graphics graphics = Graphics.FromImage(surface.Image))
            {
                graphics.Clear(Blue);
                float s = Math.Min(room.Width / 1920f, room.Height / 1080f);
                graphics.TranslateTransform((room.Width - 1920 * s) / 2, (room.Height - 1080 * s) / 2);
                graphics.ScaleTransform(s, s);
                graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
                StringFormat exact = StringFormat.GenericTypographic;
                using (Font face = new Font("Segoe UI Light", 195, GraphicsUnit.Pixel))
                using (Font text = new Font("Segoe UI Light", 43, GraphicsUnit.Pixel))
                using (Font small = new Font("Segoe UI", 19, GraphicsUnit.Pixel))
                using (Brush white = new SolidBrush(Color.White))
                using (Brush ink = new SolidBrush(Blue))
                {
                    graphics.DrawString(Revealed ? ":)" : ":(", face, white, 181, 123, exact);
                    for (int line = 0; line < Message.Length; line++) graphics.DrawString(Message[line], text, white, 208, 388 + 64 * line, exact);
                    graphics.DrawString(Revealed ? Punchline : Percent + "% complete", text, white, 208, 606, exact);
                    graphics.FillRectangle(white, 208, 700, 108, 108);
                    Code(graphics, ink, 208, 700, 4);
                    graphics.DrawString("For more information about this issue and possible fixes, visit https://www.windows.com/stopcode", small, white, 343, 692, exact);
                    graphics.DrawString("If you call a support person, give them this info:", small, white, 343, 760, exact);
                    graphics.DrawString("Stop code: " + StopCode, small, white, 343, 794, exact);
                    if (Revealed) graphics.DrawString("click anywhere or press esc", small, white, 343, 848, exact);
                }
            }
            surface.Show(Handle, room.Location);
        }

        // A QR code's look, 25 cells with a one-cell white border: its three corner squares, timing lines and fixed noise. It
        // doesn't scan to anything.
        private static void Code(Graphics graphics, Brush ink, float x, float y, float cell)
        {
            const int cells = 25;
            Random noise = new Random(51);
            for (int row = 0; row < cells; row++)
                for (int col = 0; col < cells; col++)
                {
                    bool coin = noise.Next(2) == 0;
                    int r = row < 8 ? row : row >= cells - 8 ? row - (cells - 7) : -9;
                    int c = col < 8 ? col : col >= cells - 8 ? col - (cells - 7) : -9;
                    bool dark;
                    if (r != -9 && c != -9 && !(row >= cells - 8 && col >= cells - 8))
                        dark = r >= 0 && r <= 6 && c >= 0 && c <= 6 && (r == 0 || r == 6 || c == 0 || c == 6 || (r >= 2 && r <= 4 && c >= 2 && c <= 4));
                    else if (row == 6 || col == 6) dark = (row + col) % 2 == 0;
                    else dark = coin;
                    if (dark) graphics.FillRectangle(ink, x + (col + 1) * cell, y + (row + 1) * cell, cell, cell);
                }
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

        // The clones' own session owns her one prank. Full screen or a stream delays it, as with her other chaos acts, and stopping
        // them cancels it entirely.
        private void ChaosTick(double now)
        {
            if (cloneWindow == null || quitting) { ChaosOff(); return; }
            if (chaosScreen != null || chaosScreenUsed || now < chaosScreenAt || ChaosHeld()) return;
            Point centre = new Point(anchor.X + Width / 2, anchor.Y + Height / 2);
            ChaosScreen opened = new ChaosScreen(Screen.FromPoint(centre).Bounds, clock, preferences.Motion, ChaosHeld);
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

        // What holds her fake crash back, and closes it if it starts while it's up: full screen, a stream, a job of hers (its
        // clicks would land on the plate), their cursor lent to her, or her being dragged (a Codex review, 2 Nov).
        private bool ChaosHeld()
        {
            return FullScreen() || Computer.Streaming || working != null || MidJob(job) || lentOut || dragging || pressed;
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
