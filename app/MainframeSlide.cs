using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private const int ComposedStyle = 0x02000000;
        private const int ExtendedStyle = -20;
        private const int SlideHoldMs = 350;
        private const int PictureTickMs = 16;
        private const int PictureBudgetMs = 10;
        private const int PictureSettleMs = 300;
        private const int PictureRushLimit = 60;
        private const int PictureForgetMs = 120000;
        private const int CopyPixels = 0x00CC0020;
        private const int BlendWithAlpha = 0x01FF0000;
        private const int PictureFrameInset = 20;
        private const double SlideFlickMs = 100;
        private const double SlideFollowMs = 45;
        private const double SlideGapMs = 250;
        private const double SlideEaseFade = 0.85;
        private const double SlideEaseFloor = 2;
        private const int SlideLumpDelta = 90;
        private const int MouseNotch = 120;

        [StructLayout(LayoutKind.Sequential)]
        private struct PixelHeader
        {
            internal int Size, Width, Height;
            internal short Planes, Bits;
            internal int Compression, ImageSize, XPels, YPels, Used, Important;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr thing);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr thing);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(IntPtr dc, ref PixelHeader header, uint usage, out IntPtr bits, IntPtr section, uint offset);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr target, int x, int y, int wide, int tall, IntPtr source, int fromX, int fromY, int operation);
        [DllImport("gdi32.dll")] private static extern bool GdiFlush();
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
        [DllImport("user32.dll", EntryPoint = "InvalidateRect")] private static extern bool RepaintLater(IntPtr window, IntPtr area, bool erase);
        [DllImport("msimg32.dll")] private static extern bool AlphaBlend(IntPtr target, int x, int y, int wide, int tall, IntPtr source, int fromX, int fromY, int fromWide, int fromTall, int blend);
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] private static extern int ReadWindowStyle(IntPtr window, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] private static extern int WriteWindowStyle(IntPtr window, int index, int value);
        [DllImport("user32.dll", EntryPoint = "WindowFromPoint")] private static extern IntPtr WindowAtPoint(Point point);
        [DllImport("user32.dll", EntryPoint = "ScreenToClient")] private static extern bool ScreenSpotToClient(IntPtr window, ref Point point);
        [DllImport("user32.dll", EntryPoint = "PostMessage")] private static extern bool PostToWindow(IntPtr window, int message, IntPtr wide, IntPtr low);

        private sealed class PixelLayer : IDisposable
        {
            private readonly IntPtr bitmap, old;
            internal readonly IntPtr Dc, Bits;
            internal readonly int Wide, Tall;
            internal readonly Bitmap Picture;

            internal PixelLayer(int wide, int tall)
            {
                Wide = wide;
                Tall = tall;
                PixelHeader header = new PixelHeader { Size = Marshal.SizeOf(typeof(PixelHeader)), Width = wide, Height = -tall, Planes = 1, Bits = 32 };
                IntPtr bits;
                bitmap = CreateDIBSection(IntPtr.Zero, ref header, 0, out bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) throw new OutOfMemoryException("No room for the Settings page's picture.");
                Bits = bits;
                Dc = CreateCompatibleDC(IntPtr.Zero);
                old = SelectObject(Dc, bitmap);
                Picture = new Bitmap(wide, tall, wide * 4, PixelFormat.Format32bppPArgb, bits);
            }

            public void Dispose()
            {
                Picture.Dispose();
                SelectObject(Dc, old);
                DeleteDC(Dc);
                DeleteObject(bitmap);
            }
        }

        private sealed class SlideCover : Control
        {
            private readonly Mainframe owner;

            internal SlideCover(Mainframe owner)
            {
                this.owner = owner;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Visible = false;
            }

            protected override void OnPaint(PaintEventArgs e) { owner.PaintSlide(e.Graphics); }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0201 || message.Msg == 0x0204 || message.Msg == 0x0207 || message.Msg == 0x020B)
                {
                    owner.PassPress(message.Msg, message.WParam, message.LParam);
                    return;
                }
                base.WndProc(ref message);
            }
        }

        private readonly Timer slideHold = new Timer { Interval = SlideHoldMs };
        private readonly Timer pictureTimer = new Timer { Interval = PictureTickMs };
        private readonly Timer pictureForget = new Timer { Interval = PictureForgetMs };
        private readonly System.Diagnostics.Stopwatch pictureAge = new System.Diagnostics.Stopwatch();
        private readonly Queue<Control> piecesWaiting = new Queue<Control>();
        private readonly HashSet<Control> pieceQueued = new HashSet<Control>();
        private PixelLayer pagePicture, glowColumn, frameRows, frameColumns, slideCanvas;
        private SlideCover slideCover;
        private bool sliding, pictureStale, pictureWaits, pictureQuiet;
        private int slideAt, slidePixels, paintedPixels, pictureLook, pictureSignature, previousDelta;
        private double slideExact, slideMiddle, slideEase;
        private readonly System.Diagnostics.Stopwatch wheelPace = new System.Diagnostics.Stopwatch();
        private volatile object pumpToken;
        private List<long> slideFrames;
        private static bool picturing;

        private void WireSlide()
        {
            slideHold.Tick += delegate { EndSlide(); };
            pictureTimer.Tick += delegate { BuildPicture(); };
            pictureForget.Tick += delegate { pictureForget.Stop(); DropPicture(); };
            VisibleChanged += delegate
            {
                if (Disposing || IsDisposed) return;
                if (!Visible) Settle();
                else if (paged) pictureTimer.Start();
            };
        }

        private void WatchForPicture(Control control)
        {
            control.Invalidated += delegate { PieceChanged(control); };
            control.TextChanged += delegate { PieceChanged(control); };
            foreach (Control child in control.Controls) child.TextChanged += delegate { PieceChanged(control); };
            control.LocationChanged += delegate { PageChanged(); };
            control.SizeChanged += delegate { PageChanged(); };
            control.VisibleChanged += delegate { PageChanged(); };
        }

        private void PieceChanged(Control control)
        {
            if (pictureQuiet || pagePicture == null || !pieceQueued.Add(control)) return;
            piecesWaiting.Enqueue(control);
            pictureTimer.Start();
        }

        private void PageChanged()
        {
            if (pictureQuiet || !paged) return;
            pictureStale = true;
            pictureWaits = true;
            pictureAge.Restart();
            pictureTimer.Start();
        }

        private void PictureOpened()
        {
            pictureForget.Stop();
            pictureStale = pagePicture == null || PictureSignature() != pictureSignature;
            pictureWaits = false;
            pictureTimer.Start();
        }

        private void SlideSizeChanged()
        {
            Settle();
            if (paged && WindowState != FormWindowState.Minimized) pictureTimer.Start();
        }

        private int PictureSignature()
        {
            unchecked
            {
                int wide = ClientSize.Width, shift = Scaled(pageScroll), signature = wide * 31 + PageNow;
                foreach (Control control in scrolled)
                {
                    if (control.Parent != this) continue;
                    bool shown = control.Visible && control.Left < wide;
                    signature = signature * 31 + (shown ? 1 : 0);
                    if (!shown) continue;
                    signature = signature * 31 + control.Left;
                    signature = signature * 31 + control.Top + shift;
                    signature = signature * 31 + control.Width;
                    signature = signature * 31 + control.Height;
                }
                return signature;
            }
        }

        private int ShownScroll { get { return sliding ? slideAt : pageScroll; } }


        private bool PictureInPlace { get { return pagePicture != null && !pictureStale && pictureLook == Neon.PaletteStamp && slideCanvas != null && slideCanvas.Tall == ClientSize.Height && pagePicture.Wide == ClientSize.Width && pagePicture.Tall == PictureTall; } }

        private int PictureTall { get { return Math.Max(Scaled(PageNow), ClientSize.Height); } }

        private void BuildPicture()
        {
            if (!paged || !Visible || WindowState == FormWindowState.Minimized) { pictureTimer.Stop(); return; }
            if (sliding || glide.Enabled) return;
            if (pictureStale && pictureWaits && pictureAge.ElapsedMilliseconds < PictureSettleMs) return;
            bool drawn = TryPicture(delegate
            {
                if (!PictureInPlace) RestartPicture();
                System.Diagnostics.Stopwatch budget = System.Diagnostics.Stopwatch.StartNew();
                while (piecesWaiting.Count > 0 && budget.ElapsedMilliseconds < PictureBudgetMs) DrawNextPiece();
            });
            if (drawn && piecesWaiting.Count == 0) pictureTimer.Stop();
        }

        private bool TryPicture(Action draw)
        {
            try
            {
                draw();
                return true;
            }
            catch (OutOfMemoryException) { }
            catch (ArgumentException) { }
            catch (ExternalException) { }
            DropPicture();
            return false;
        }

        private static void Free(ref PixelLayer layer)
        {
            if (layer != null) layer.Dispose();
            layer = null;
        }

        private void RestartPicture()
        {
            int wide = ClientSize.Width, tall = PictureTall;
            if (pagePicture == null || pagePicture.Wide != wide || pagePicture.Tall != tall)
            {
                DropLayers();
                pagePicture = new PixelLayer(wide, tall);
            }
            pictureStale = false;
            pictureLook = Neon.PaletteStamp;
            piecesWaiting.Clear();
            pieceQueued.Clear();
            float s = Unit();
            int shift = Scaled(pageScroll);
            using (Graphics page = Graphics.FromImage(pagePicture.Picture))
            {
                page.Clear(Ground);
                foreach (Control child in Controls)
                {
                    Head head = child as Head;
                    if (head == null || head.Span <= 0 || !head.Visible || head.Left >= wide) continue;
                    RectangleF band = head.Band(s);
                    band.Offset(0, shift);
                    DrawBand(page, band, s);
                }
            }
            DrawGlowColumn(s);
            DrawFrameStrips(s);
            foreach (Control control in scrolled)
                if (control.Parent == this && pieceQueued.Add(control)) piecesWaiting.Enqueue(control);
            pictureSignature = PictureSignature();
        }

        private void DrawGlowColumn(float s)
        {
            int tall = (int)Math.Ceiling(210 * s);
            Free(ref glowColumn);
            glowColumn = new PixelLayer(1, tall);
            Color ground = Ground;
            using (Bitmap fade = new Bitmap(1, tall, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(fade))
                using (LinearGradientBrush wine = new LinearGradientBrush(new RectangleF(0, -1, 16, tall + 2), Glow, ground, LinearGradientMode.Vertical))
                {
                    wine.Blend = Fade;
                    graphics.FillRectangle(wine, 0, 0, 1, tall);
                }
                for (int y = 0; y < tall; y++) Marshal.WriteInt32(glowColumn.Bits, y * 4, GlowPixel(ground, fade.GetPixel(0, y)));
            }
        }

        private static int GlowPixel(Color under, Color wanted)
        {
            int alpha = Math.Max(GlowAlpha(under.R, wanted.R), Math.Max(GlowAlpha(under.G, wanted.G), GlowAlpha(under.B, wanted.B)));
            int red = GlowPart(under.R, wanted.R, alpha), green = GlowPart(under.G, wanted.G, alpha), blue = GlowPart(under.B, wanted.B, alpha);
            return (alpha << 24) | (red << 16) | (green << 8) | blue;
        }

        private static int GlowAlpha(int under, int wanted)
        {
            if (wanted > under) return (int)Math.Ceiling((wanted - under) * 255.0 / (255 - under));
            if (wanted < under) return (int)Math.Ceiling((under - wanted) * 255.0 / under);
            return 0;
        }

        private static int GlowPart(int under, int wanted, int alpha)
        {
            return Math.Max(0, Math.Min(alpha, (int)Math.Round(wanted - (255 - alpha) * under / 255.0)));
        }

        private void DrawFrameStrips(float s)
        {
            int inset = Scaled(PictureFrameInset), wide = ClientSize.Width, tall = ClientSize.Height;
            Free(ref frameRows);
            Free(ref frameColumns);
            Free(ref slideCanvas);
            slideCanvas = new PixelLayer(wide, tall);
            frameRows = new PixelLayer(wide, inset * 2);
            frameColumns = new PixelLayer(inset * 2, tall);
            using (Bitmap frame = new Bitmap(wide, tall, PixelFormat.Format32bppPArgb))
            {
                using (Graphics graphics = Graphics.FromImage(frame))
                {
                    graphics.Clear(Color.Transparent);
                    Edging(graphics, this, s);
                }
                using (Graphics rows = Graphics.FromImage(frameRows.Picture))
                {
                    rows.CompositingMode = CompositingMode.SourceCopy;
                    rows.DrawImage(frame, new Rectangle(0, 0, wide, inset), new Rectangle(0, 0, wide, inset), GraphicsUnit.Pixel);
                    rows.DrawImage(frame, new Rectangle(0, inset, wide, inset), new Rectangle(0, tall - inset, wide, inset), GraphicsUnit.Pixel);
                }
                using (Graphics columns = Graphics.FromImage(frameColumns.Picture))
                {
                    columns.CompositingMode = CompositingMode.SourceCopy;
                    columns.DrawImage(frame, new Rectangle(0, 0, inset, tall), new Rectangle(0, 0, inset, tall), GraphicsUnit.Pixel);
                    columns.DrawImage(frame, new Rectangle(inset, 0, inset, tall), new Rectangle(wide - inset, 0, inset, tall), GraphicsUnit.Pixel);
                }
            }
        }

        private void DrawNextPiece()
        {
            Control control = piecesWaiting.Dequeue();
            pieceQueued.Remove(control);
            if (control.IsDisposed || !control.IsHandleCreated || !control.Visible || control.Left >= ClientSize.Width || control.Width <= 0 || control.Height <= 0) return;
            using (Bitmap piece = new Bitmap(control.Width, control.Height, PixelFormat.Format32bppPArgb))
            {
                picturing = true;
                try { control.DrawToBitmap(piece, new Rectangle(Point.Empty, piece.Size)); }
                finally { picturing = false; }
                using (Graphics page = Graphics.FromImage(pagePicture.Picture)) page.DrawImageUnscaled(piece, control.Left, control.Top + Scaled(pageScroll));
            }
        }

        private void DropLayers()
        {
            Free(ref pagePicture);
            Free(ref glowColumn);
            Free(ref frameRows);
            Free(ref frameColumns);
            Free(ref slideCanvas);
            piecesWaiting.Clear();
            pieceQueued.Clear();
        }

        private void DropPicture()
        {
            pictureTimer.Stop();
            pictureStale = false;
            pumpToken = null;
            DropLayers();
        }

        private void StartFramePump()
        {
            if (pumpToken != null) return;
            object mine = new object();
            pumpToken = mine;
            IntPtr target = slideCover.Handle;
            System.Threading.Thread framePump = new System.Threading.Thread(delegate()
            {
                while (pumpToken == mine)
                {
                    if (DwmFlush() != 0) System.Threading.Thread.Sleep(8);
                    RepaintLater(target, IntPtr.Zero, false);
                }
            });
            framePump.IsBackground = true;
            framePump.Name = "Mainframe frames";
            framePump.Start();
        }

        private void PaceSlide(int delta, bool turned)
        {
            double gap = wheelPace.IsRunning ? wheelPace.Elapsed.TotalMilliseconds : double.MaxValue;
            wheelPace.Restart();
            bool flick = !turned && gap <= SlideGapMs && Math.Abs(delta) >= SlideLumpDelta && Math.Abs(previousDelta) >= SlideLumpDelta && Math.Abs(delta) % MouseNotch != 0;
            slideEase = flick ? SlideFlickMs : turned || gap > SlideGapMs ? 0 : slideEase * SlideEaseFade;
            previousDelta = delta;
        }

        private void AdvanceSlide()
        {
            if (!glide.Enabled) return;
            double elapsed = glideClock.Elapsed.TotalMilliseconds;
            glideClock.Restart();
            slideMiddle = slideEase < SlideEaseFloor ? glideTo : slideMiddle + (glideTo - slideMiddle) * (1 - Math.Exp(-elapsed / slideEase));
            double exact = slideExact + (slideMiddle - slideExact) * (1 - Math.Exp(-elapsed / SlideFollowMs));
            if (Math.Max(Math.Abs(glideTo - exact), Math.Abs(glideTo - slideMiddle)) * Unit() < 0.5)
            {
                exact = slideMiddle = glideTo;
                glide.Stop();
                pumpToken = null;
                slideHold.Start();
            }
            slideExact = exact;
            slideAt = (int)Math.Round(exact);
            slidePixels = (int)Math.Round(exact * Unit());
        }

        private bool BeginSlide()
        {
            if (sliding) return true;
            if (!PictureInPlace || piecesWaiting.Count > PictureRushLimit || !IsHandleCreated) return false;
            if (!TryPicture(delegate { while (piecesWaiting.Count > 0) DrawNextPiece(); })) return false;
            pictureQuiet = true;
            if (slideCover == null)
            {
                slideCover = new SlideCover(this);
                Controls.Add(slideCover);
                slideCover.CreateControl();
            }
            slideAt = pageScroll;
            slideExact = slideMiddle = pageScroll;
            slideEase = 0;
            slidePixels = Scaled(pageScroll);
            paintedPixels = -1;
            sliding = true;
            WriteWindowStyle(Handle, ExtendedStyle, ReadWindowStyle(Handle, ExtendedStyle) & ~ComposedStyle);
            slideCover.Bounds = ClientRectangle;
            slideCover.BringToFront();
            topRim.BringToFront();
            bottomRim.BringToFront();
            slideCover.Visible = true;
            pictureQuiet = false;
            slideCover.Invalidate();
            slideCover.Update();
            return true;
        }

        private void ShowSlide(double at)
        {
            slideExact = slideMiddle = at;
            slideAt = (int)Math.Round(at);
            int pixels = (int)Math.Round(at * Unit());
            if (pixels == slidePixels) return;
            slidePixels = pixels;
            slideCover.Invalidate();
            slideCover.Update();
        }

        private void PaintSlide(Graphics graphics)
        {
            if (pagePicture == null) return;
            if (slideFrames != null) slideFrames.Add(System.Diagnostics.Stopwatch.GetTimestamp());
            AdvanceSlide();
            if (slidePixels == paintedPixels) return;
            paintedPixels = slidePixels;
            float s = Unit();
            int wide = ClientSize.Width, tall = ClientSize.Height, inset = frameRows.Tall / 2;
            IntPtr canvas = slideCanvas.Dc;
            BitBlt(canvas, 0, 0, wide, tall, pagePicture.Dc, 0, slidePixels, CopyPixels);
            AlphaBlend(canvas, 0, 0, wide, glowColumn.Tall, glowColumn.Dc, 0, 0, 1, glowColumn.Tall, BlendWithAlpha);
            AlphaBlend(canvas, 0, 0, wide, inset, frameRows.Dc, 0, 0, wide, inset, BlendWithAlpha);
            AlphaBlend(canvas, 0, tall - inset, wide, inset, frameRows.Dc, 0, inset, wide, inset, BlendWithAlpha);
            AlphaBlend(canvas, 0, inset, inset, tall - inset * 2, frameColumns.Dc, 0, inset, inset, tall - inset * 2, BlendWithAlpha);
            AlphaBlend(canvas, wide - inset, inset, inset, tall - inset * 2, frameColumns.Dc, inset, inset, inset, tall - inset * 2, BlendWithAlpha);
            GdiFlush();
            float rail = Height - 36 * s, thumb = rail * pageView / PageNow, at = 18 * s + (rail - thumb) * (float)slideExact / (PageNow - pageView);
            using (Graphics marks = Graphics.FromImage(slideCanvas.Picture))
            using (Brush track = new SolidBrush(Dash))
            using (Brush lit = new SolidBrush(Cyan))
            {
                marks.FillRectangle(track, Width - 8 * s, 18 * s, 2 * s, rail);
                marks.FillRectangle(lit, Width - 8 * s, at, 2 * s, thumb);
            }
            IntPtr dc = graphics.GetHdc();
            BitBlt(dc, 0, 0, wide, tall, canvas, 0, 0, CopyPixels);
            graphics.ReleaseHdc(dc);
        }

        private void EndSlide()
        {
            if (!sliding) return;
            slideHold.Stop();
            glide.Stop();
            sliding = false;
            pumpToken = null;
            if (slideAt != pageScroll) MoveControls(slideAt);
            pictureQuiet = true;
            WriteWindowStyle(Handle, ExtendedStyle, ReadWindowStyle(Handle, ExtendedStyle) | ComposedStyle);
            slideCover.Visible = false;
            Invalidate(true);
            pictureQuiet = false;
            Update();
            if (piecesWaiting.Count > 0 || pictureStale) pictureTimer.Start();
        }

        private void PassPress(int message, IntPtr wide, IntPtr low)
        {
            int packed = unchecked((int)low.ToInt64());
            Point screen = PointToScreen(new Point(unchecked((short)(packed & 0xFFFF)), unchecked((short)((packed >> 16) & 0xFFFF))));
            EndSlide();
            IntPtr hit = WindowAtPoint(screen);
            Control over = Control.FromChildHandle(hit);
            if (over == null || over.FindForm() != this) return;
            IntPtr screenSpot = new IntPtr((screen.Y << 16) | (screen.X & 0xFFFF));
            int where = hit == Handle ? unchecked((int)Native.SendMessage(hit, 0x0084, IntPtr.Zero, screenSpot).ToInt64()) : 1;
            if (where != 1)
            {
                int outer = message == 0x0201 ? 0x00A1 : message == 0x0204 ? 0x00A4 : message == 0x0207 ? 0x00A7 : 0x00AB;
                PostToWindow(hit, outer, new IntPtr(where), screenSpot);
                return;
            }
            Point spot = screen;
            ScreenSpotToClient(hit, ref spot);
            PostToWindow(hit, message, wide, new IntPtr((spot.Y << 16) | (spot.X & 0xFFFF)));
        }

        private void FinishPicture()
        {
            if (!PictureInPlace) RestartPicture();
            while (piecesWaiting.Count > 0) DrawNextPiece();
            pictureTimer.Stop();
        }

        private int ComparedTop { get { return (int)Math.Ceiling(210 * Unit()) + Scaled(PictureFrameInset); } }

        private bool Compared(Control control, Size real)
        {
            int margin = Scaled(PictureFrameInset);
            return control.Parent == this && control.Visible && control.Width > 0 && control.Height > 0 && control.Left >= margin && control.Right <= real.Width - margin && control.Top >= ComparedTop && control.Bottom <= real.Height - margin;
        }

        private static bool PixelsDiffer(Color one, Color other)
        {
            return Math.Abs(one.R - other.R) > 8 || Math.Abs(one.G - other.G) > 8 || Math.Abs(one.B - other.B) > 8;
        }

        private string PictureDiffers(Bitmap real, int scrolledBy)
        {
            foreach (Control control in scrolled)
            {
                if (!Compared(control, real.Size)) continue;
                int checkedPixels = 0, wrong = 0;
                for (int y = control.Top; y < control.Bottom; y += 2)
                    for (int x = control.Left; x < control.Right; x += 2)
                    {
                        checkedPixels++;
                        if (PixelsDiffer(real.GetPixel(x, y), pagePicture.Picture.GetPixel(x, y + scrolledBy))) wrong++;
                    }
                if (wrong * 100 > checkedPixels) return (control.Text.Length > 0 ? control.Text : control.GetType().Name) + " differs in " + wrong + " of " + checkedPixels + " pixels";
            }
            int margin = Scaled(PictureFrameInset), compared = 0, different = 0;
            for (int y = ComparedTop; y < real.Height - margin; y += 3)
                for (int x = margin; x < real.Width - margin; x += 3)
                {
                    compared++;
                    if (PixelsDiffer(real.GetPixel(x, y), pagePicture.Picture.GetPixel(x, y + scrolledBy))) different++;
                }
            return different * 100 > compared ? different + " of " + compared + " pixels" : null;
        }

        private string MissingPieceProblem()
        {
            using (Bitmap real = RealPage())
            {
                Size size = real.Size;
                Control blanked = scrolled.Find(control => control is Key && control.Text.Length > 0 && Compared(control, size));
                if (blanked == null) return null;
                using (Graphics page = Graphics.FromImage(pagePicture.Picture))
                using (Brush ground = new SolidBrush(Ground))
                    page.FillRectangle(ground, blanked.Left, blanked.Top + Scaled(pageScroll), blanked.Width, blanked.Height);
                string found = PictureDiffers(real, Scaled(pageScroll));
                PieceChanged(blanked);
                FinishPicture();
                return found == null ? "Blanking " + blanked.Text + " in the picture wasn't noticed." : null;
            }
        }

        private Bitmap RealPage()
        {
            Bitmap real = new Bitmap(ClientSize.Width, ClientSize.Height, PixelFormat.Format32bppArgb);
            DrawToBitmap(real, new Rectangle(Point.Empty, real.Size));
            return real;
        }

        internal void CheckPageSlide(List<string> checks)
        {
            FormWindowState wasState = WindowState;
            bool wasPaged = paged, wasFallback = layoutFallback;
            int wasScroll = pageScroll, wasDpi = layoutDpi, wasText = layoutText;
            double frameCost, framesASecond, longestGap;
            string problem;
            Restore();
            try
            {
                ShowSettings(true);
                LayoutScale(192, 100);
                ShowSettings(true);
                Settle();
                ScrollPage(0);
                FinishPicture();
                problem = SlideProblem(out frameCost, out framesASecond, out longestGap);
            }
            finally
            {
                ScrollPage(0);
                LayoutScale(wasDpi, wasText, wasFallback);
                ShowSettings(wasPaged);
                ScrollPage(wasScroll);
                WindowState = wasState;
            }
            if (problem != null) throw new Exception(problem);
            string cost = frameCost.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
            string rate = framesASecond.ToString("0", System.Globalization.CultureInfo.InvariantCulture) + " frames a second, longest gap " + longestGap.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms";
            checks.Add((frameCost <= 16 && framesASecond >= 45 ? "PASS" : "LIMIT") + " Settings slides as one picture at " + cost + " ms a frame at 200% and " + rate + ", each frame reaching the screen whole, turning back at once and easing only a flick's lumps, without moving a control until it stops; the picture matches the real page control by control and survives reopening Settings, a control that repaints is redrawn in it, with no memory for it she scrolls the old way, and a press, a layout change or minimising brings the real page straight back where it had slid to");
        }

        private string SlideProblem(out double frameCost, out double framesASecond, out double longestGap)
        {
            frameCost = framesASecond = longestGap = 0;
            string differs;
            using (Bitmap real = RealPage()) differs = PictureDiffers(real, 0);
            if (differs != null) return "The picture of Settings doesn't match the page: " + differs;
            string missing = MissingPieceProblem();
            if (missing != null) return missing;
            Control watched = scrolled.Find(control => control.Parent == this && control.Visible && control.Left < ClientSize.Width);
            if (watched == null) return "Settings had nothing to watch.";
            int watchedTop = watched.Top;
            ScrollWheel(-120);
            if (!sliding) return "A wheel turn didn't start sliding the picture.";
            if (watched.Top != watchedTop) return "The controls moved while the picture was sliding.";
            if ((ReadWindowStyle(Handle, ExtendedStyle) & ComposedStyle) != 0) return "The Mainframe stayed composited while sliding.";
            if (!slideCover.Visible) return "The picture wasn't showing while it slid.";
            glide.Stop();
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 0; frame < 20; frame++) ShowSlide(frame % 2 == 0 ? ScrollRow : 0);
            frameCost = took.Elapsed.TotalMilliseconds / 20;
            glideTo = ScrollRow;
            glide.Start();
            Settle();
            if (sliding) return "The picture stayed up after the slide ended.";
            if (slideCover.Visible) return "The cover stayed visible after the slide ended.";
            if (pageScroll != ScrollRow) return "The page ended at " + pageScroll + " instead of " + ScrollRow + ".";
            if (watched.Top != watchedTop - Scaled(ScrollRow)) return "The controls didn't land where the picture slid to.";
            if ((ReadWindowStyle(Handle, ExtendedStyle) & ComposedStyle) == 0) return "The Mainframe stayed uncomposited after the slide.";
            using (Bitmap real = RealPage()) differs = PictureDiffers(real, Scaled(pageScroll));
            if (differs != null) return "The picture doesn't match the scrolled page: " + differs;

            FinishPicture();
            slideFrames = new List<long>();
            ScrollWheel(-120);
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            while (glide.Enabled && waited.ElapsedMilliseconds < 2000) Application.DoEvents();
            double tick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            if (slideFrames.Count > 1) framesASecond = (slideFrames.Count - 1) * 1000 / ((slideFrames[slideFrames.Count - 1] - slideFrames[0]) * tick);
            for (int frame = 1; frame < slideFrames.Count; frame++) longestGap = Math.Max(longestGap, (slideFrames[frame] - slideFrames[frame - 1]) * tick);
            slideFrames = null;
            if (sliding && pumpToken != null) return "The frame thread kept running after the page settled.";
            Settle();

            for (int round = 0; round < 5; round++)
            {
                ScrollWheel(-120);
                Settle();
                ScrollWheel(120);
                waited.Restart();
                while (glide.Enabled && waited.ElapsedMilliseconds < 2000) Application.DoEvents();
                if (glide.Enabled) return "A wheel turn right after a slide ended froze the picture.";
                Settle();
            }

            FinishPicture();
            int rested = pageScroll;
            ScrollWheel(-120);
            ScrollWheel(-120);
            ScrollWheel(-120);
            ScrollWheel(120);
            if (glideTo >= rested) return "Turning the wheel the other way kept the page heading the old way, to " + glideTo + " from " + rested + ".";
            Settle();

            wheelPace.Reset();
            ScrollWheel(-185);
            ScrollWheel(-37);
            if (slideEase != 0) return "A held trackpad scroll got a flick's long ease: " + slideEase + " ms.";
            Settle();
            wheelPace.Reset();
            ScrollWheel(-291);
            ScrollWheel(-264);
            if (slideEase != SlideFlickMs) return "A trackpad flick's big lumps didn't get the long ease: " + slideEase + " ms.";
            Settle();
            wheelPace.Reset();
            ScrollWheel(-120);
            ScrollWheel(-120);
            if (slideEase != 0) return "Mouse wheel notches got a flick's long ease: " + slideEase + " ms.";
            Settle();

            FinishPicture();
            more.Invalidate();
            if (!pieceQueued.Contains(more)) return "A control that repainted wasn't redrawn in the picture.";
            FinishPicture();

            ScrollWheel(-120);
            if (!sliding) return "A second wheel turn didn't slide.";
            Point middle = new Point(ClientSize.Width / 2, ClientSize.Height / 2);
            Native.SendMessage(slideCover.Handle, 0x0207, IntPtr.Zero, new IntPtr((middle.Y << 16) | (middle.X & 0xFFFF)));
            if (sliding) return "A press on the sliding picture didn't bring the real page back.";

            FinishPicture();
            ScrollPage(0);
            ScrollWheel(-120);
            if (!sliding) return "The wheel turn before a layout change didn't slide.";
            glide.Stop();
            ShowSlide(ScrollRow);
            Arrange(expanded);
            if (sliding) return "A layout change left the picture up.";
            if (pageScroll != ScrollRow) return "A layout change during a slide put the page at " + pageScroll + " instead of " + ScrollRow + ".";

            FinishPicture();
            ScrollWheel(-120);
            if (!sliding) return "The wheel turn before minimising didn't slide.";
            WindowState = FormWindowState.Minimized;
            bool leftSliding = sliding, leftPump = pumpToken != null;
            Restore();
            if (leftSliding || leftPump) return "Minimising the Mainframe left it sliding.";

            ScrollPage(0);
            FinishPicture();
            PixelLayer held = pagePicture;
            ShowSettings(false);
            if (pagePicture != held) return "Leaving Settings threw its picture away straight away.";
            ShowSettings(true);
            if (pagePicture != held || !PictureInPlace) return "Reopening Settings didn't keep its picture.";
            ScrollWheel(-120);
            if (!sliding) return "The first wheel turn after reopening Settings didn't slide.";
            Settle();

            int beforeFallback = pageScroll;
            bool survived = TryPicture(delegate { throw new OutOfMemoryException(); });
            if (survived || pagePicture != null) return "Running out of memory for the picture didn't drop it.";
            DropPicture();
            ScrollWheel(-120);
            if (sliding) return "With no picture the wheel still tried to slide.";
            Settle();
            if (pageScroll == beforeFallback) return "With no picture the wheel didn't scroll the old way.";
            return null;
        }
    }
}
