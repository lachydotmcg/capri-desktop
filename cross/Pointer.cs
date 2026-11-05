using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Everything her cursor's electricity can be tuned by, in pixels at 100% scale and seconds. The same as Windows' BoltLook.
    internal sealed class BoltLook
    {
        internal bool On = true;
        internal Color Red = Color.FromRgb(255, 0, 70);
        internal Color Black = Color.FromRgb(8, 6, 12);
        internal int Count = 4;
        internal double Thickness = 4.2;
        internal double Radius = 26;
        internal double ShortestLife = 0.12;
        internal double LongestLife = 0.30;
        internal double Glow = 0.22;
        internal int Trail = 3;
        internal int Burst = 3;
    }

    // Windows' CursorBolts drawn with Avalonia: disconnected filled ribbons around a cursor an AI has taken over, each
    // with its own life, never a spinner.
    internal sealed class CursorBolts
    {
        private const int Most = 14;
        private const double IdleAfter = 1.5;

        private sealed class Bolt
        {
            internal Point At;
            internal bool Left;
            internal double Angle, Length;
            internal double Born, Life;
            internal int Kind;
            internal Point[] Spine;
            internal Point[] Branch;
            internal double Nudged;
        }

        internal readonly BoltLook Look = new BoltLook();
        private readonly List<Bolt> bolts = new List<Bolt>();
        private readonly Random random = new Random();
        private Point last;
        private double movedAt = -10, nextSpawn, nextTrail;
        private int pendingBurst;

        internal int Alive { get { return bolts.Count; } }

        internal void Tap() { pendingBurst = Look.Burst; }

        internal void Clear() { bolts.Clear(); pendingBurst = 0; }

        private double Between(double low, double high) { return low + random.NextDouble() * (high - low); }

        private Point[] Jagged(double length, int points)
        {
            Point[] spine = new Point[points];
            double step = length / (points - 1);
            for (int i = 0; i < points; i++) spine[i] = new Point(i * step, i == 0 || i == points - 1 ? 0 : Between(-0.28, 0.28) * length);
            return spine;
        }

        private Bolt Make(double now, Point at, double angle, double scale)
        {
            Bolt bolt = new Bolt();
            bolt.At = at;
            bolt.Angle = angle;
            bolt.Length = Between(9, 20) * scale;
            bolt.Born = bolt.Nudged = now;
            bolt.Life = Look.ShortestLife + random.NextDouble() * (Look.LongestLife - Look.ShortestLife);
            bolt.Kind = random.Next(3);
            bolt.Spine = Jagged(bolt.Length, 4 + random.Next(3));
            if (random.NextDouble() < 0.35)
            {
                Point root = bolt.Spine[1 + random.Next(bolt.Spine.Length - 2)];
                bolt.Branch = new[] { root, new Point(root.X + Between(2, 6) * scale, root.Y + Between(-7, 7) * scale), new Point(root.X + Between(5, 10) * scale, root.Y + Between(-9, 9) * scale) };
            }
            return bolt;
        }

        // Her pointer's tip points up and left, so arcs keep to its sides and rear and the tip stays clear.
        private Point AroundHer(double scale, out double angle)
        {
            double around = Between(-40, 130) * Math.PI / 180;
            double distance = Between(0.45, 1) * Look.Radius * scale;
            angle = Between(0, 360);
            double middle = 13 * scale;
            return new Point(middle + Math.Cos(around) * distance, middle + Math.Sin(around) * distance);
        }

        internal void Step(double now, Point tip, double scale)
        {
            if (!Look.On) { Clear(); return; }
            double dx = tip.X - last.X, dy = tip.Y - last.Y;
            bool moved = dx * dx + dy * dy > 4;
            last = tip;
            if (moved) movedAt = now;
            bool idle = now - movedAt > IdleAfter;
            bolts.RemoveAll(delegate(Bolt b) { return now - b.Born > b.Life; });
            foreach (Bolt bolt in bolts)
            {
                // A small change now and then, so a bolt holds its shape rather than boiling.
                if (now - bolt.Nudged < 0.07 || bolt.Spine.Length < 3) continue;
                bolt.Nudged = now;
                int point = 1 + random.Next(bolt.Spine.Length - 2);
                bolt.Spine[point] = new Point(bolt.Spine[point].X, bolt.Spine[point].Y + Between(-0.06, 0.06) * bolt.Length);
            }
            if (moved && now >= nextTrail)
            {
                nextTrail = now + 0.16;
                for (int i = 0; i < Look.Trail && bolts.Count < Most; i++)
                {
                    double angle;
                    Point near = AroundHer(scale, out angle);
                    Bolt left = Make(now, new Point(tip.X + near.X, tip.Y + near.Y), angle, scale);
                    left.Left = true;
                    left.Length *= 0.7;
                    bolts.Add(left);
                }
            }
            for (; pendingBurst > 0 && bolts.Count < Most; pendingBurst--)
            {
                double angle = Between(-30, 120);
                double way = angle * Math.PI / 180;
                double from = 8 * scale;
                bolts.Add(Make(now, new Point(Math.Cos(way) * from, Math.Sin(way) * from), angle, scale * 1.4));
            }
            int wanted = idle ? Math.Max(1, Look.Count / 2) : Look.Count;
            if (now >= nextSpawn && bolts.Count < Math.Min(wanted, Most))
            {
                double angle;
                Point at = AroundHer(scale, out angle);
                bolts.Add(Make(now, at, angle, scale));
                nextSpawn = now + (idle ? 0.25 + random.NextDouble() * 0.5 : 0.02 + random.NextDouble() * 0.08);
            }
        }

        // A tapered ribbon along a bent path, as one filled polygon, so its corners stay sharp.
        private static Point[] Ribbon(Point[] path, double width)
        {
            Point[] shape = new Point[path.Length * 2];
            for (int i = 0; i < path.Length; i++)
            {
                Point before = path[Math.Max(0, i - 1)], after = path[Math.Min(path.Length - 1, i + 1)];
                double x = after.X - before.X, y = after.Y - before.Y, size = Math.Sqrt(x * x + y * y);
                if (size < 0.01) size = 0.01;
                double t = (double)i / (path.Length - 1), half = width * Math.Sin(t * Math.PI) * (i % 2 == 0 ? 0.6 : 0.5);
                shape[i] = new Point(path[i].X - y / size * half, path[i].Y + x / size * half);
                shape[shape.Length - 1 - i] = new Point(path[i].X + y / size * half, path[i].Y - x / size * half);
            }
            return shape;
        }

        private static Point[] Placed(Point[] path, Point at, double angle)
        {
            double turn = angle * Math.PI / 180;
            double c = Math.Cos(turn), s = Math.Sin(turn);
            Point[] placed = new Point[path.Length];
            for (int i = 0; i < path.Length; i++) placed[i] = new Point(at.X + path[i].X * c - path[i].Y * s, at.Y + path[i].X * s + path[i].Y * c);
            return placed;
        }

        private static StreamGeometry Polygon(Point[] points)
        {
            StreamGeometry shape = new StreamGeometry();
            using (StreamGeometryContext pen = shape.Open())
            {
                pen.BeginFigure(points[0], true);
                for (int i = 1; i < points.Length; i++) pen.LineTo(points[i]);
                pen.EndFigure(true);
            }
            return shape;
        }

        // Origin is where the cursor tip sits in the picture.
        internal void Draw(DrawingContext graphics, Point origin, Point tip, double now, double scale)
        {
            if (!Look.On || bolts.Count == 0) return;
            IBrush red = new SolidColorBrush(Look.Red), black = new SolidColorBrush(Look.Black);
            Pen glow = new Pen(new SolidColorBrush(Color.FromArgb((byte)(Math.Min(1, Math.Max(0, Look.Glow)) * 255), Look.Red.R, Look.Red.G, Look.Red.B)), 3.5 * scale, null, PenLineCap.Flat, PenLineJoin.Miter);
            foreach (Bolt bolt in bolts)
            {
                Point at = bolt.Left ? new Point(origin.X + bolt.At.X - tip.X, origin.Y + bolt.At.Y - tip.Y) : new Point(origin.X + bolt.At.X, origin.Y + bolt.At.Y);
                double age = (now - bolt.Born) / bolt.Life;
                Point[] spine = Placed(bolt.Spine, at, bolt.Angle);
                double width = Look.Thickness * scale * (bolt.Left ? 0.75 : 1);
                // It breaks apart at the end of its life: the ribbon splits into two pieces that part.
                List<Point[]> pieces = new List<Point[]>();
                if (age < 0.72 || spine.Length < 4) pieces.Add(spine);
                else
                {
                    int split = spine.Length / 2;
                    Point[] first = new Point[split], second = new Point[spine.Length - split];
                    Array.Copy(spine, first, split);
                    Array.Copy(spine, split, second, 0, second.Length);
                    pieces.Add(first);
                    pieces.Add(second);
                }
                foreach (Point[] piece in pieces)
                {
                    if (piece.Length < 2) continue;
                    StreamGeometry shape = Polygon(Ribbon(piece, width));
                    if (bolt.Kind != 1)
                    {
                        graphics.DrawGeometry(red, glow, shape);
                        if (bolt.Kind == 2) graphics.DrawGeometry(black, null, Polygon(Ribbon(piece, width * 0.4)));
                    }
                    else graphics.DrawGeometry(black, null, shape);
                }
                if (bolt.Branch != null && age < 0.72) graphics.DrawGeometry(bolt.Kind == 1 ? black : red, null, Polygon(Ribbon(Placed(bolt.Branch, at, bolt.Angle), width * 0.7)));
            }
        }
    }

    // Her own pointer for mouse jobs, like Windows' HerPointer: her artist's cursor with electricity around it glides to
    // each target while their mouse stays where they left it. Clicks go straight through it, and she hides it for her
    // own screenshots so her brain never mistakes it for theirs.
    internal sealed class HerCursor : Window
    {
        private const double Size = 36, Reach = 90, Ripple = 0.45;
        private static Bitmap art;
        private readonly Face face;
        private readonly DispatcherTimer ticks = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        private PixelPoint tip;
        private double tapped = -1;
        private bool passThrough;
        internal readonly CursorBolts Bolts = new CursorBolts();

        internal HerCursor()
        {
            Look.Overlay(this, "Capri pointer");
            Width = Height = Reach * 2;
            face = new Face(this);
            Content = face;
            IsHitTestVisible = false;
            ticks.Tick += delegate { Program.Guard(Step); };
            Opened += delegate { PassThrough(); };
        }

        internal static Bitmap Art
        {
            get
            {
                if (art != null) return art;
                using (Stream stream = typeof(HerCursor).Assembly.GetManifestResourceStream("Capri.Cursor.png"))
                    if (stream != null) art = new Bitmap(stream);
                return art;
            }
        }

        // Her tip to this spot on the desktop, in pixels.
        internal void Point(PixelPoint at)
        {
            tip = at;
            double scale = RenderScaling > 0 ? RenderScaling : 1;
            Position = new PixelPoint(at.X - (int)Math.Round(Reach * scale), at.Y - (int)Math.Round(Reach * scale));
            if (!IsVisible)
            {
                Show();
                Look.OnTopAgain(this);
            }
            if (!ticks.IsEnabled) ticks.Start();
            face.InvalidateVisual();
        }

        internal void Tap()
        {
            if (!IsVisible) return;
            tapped = watch.Elapsed.TotalSeconds;
            Bolts.Tap();
            face.InvalidateVisual();
        }

        internal void Park()
        {
            ticks.Stop();
            tapped = -1;
            Bolts.Clear();
            if (IsVisible) Hide();
        }

        internal bool Shown { get { return IsVisible; } }

        private void Step()
        {
            if (tapped >= 0 && watch.Elapsed.TotalSeconds - tapped > Ripple) tapped = -1;
            double now = watch.Elapsed.TotalSeconds;
            Bolts.Step(now, new Point(tip.X, tip.Y), 1);
            face.InvalidateVisual();
        }

        private sealed class Face : Control
        {
            private readonly HerCursor her;

            internal Face(HerCursor her) { this.her = her; }

            public override void Render(DrawingContext graphics)
            {
                Point middle = new Point(Reach, Reach);
                double now = her.watch.Elapsed.TotalSeconds;
                if (her.tapped >= 0)
                {
                    double eased = Math.Min(1, (now - her.tapped) / Ripple);
                    eased = 1 - (1 - eased) * (1 - eased);
                    double radius = 6 + 22 * eased;
                    byte alpha = (byte)(220 * (1 - eased));
                    graphics.DrawEllipse(null, new Pen(new SolidColorBrush(Color.FromArgb(alpha, Look.Pink.R, Look.Pink.G, Look.Pink.B)), 2.6 - 1.2 * eased), middle, radius, radius);
                }
                her.Bolts.Draw(graphics, middle, new Point(her.tip.X, her.tip.Y), now, 1);
                Bitmap picture = Art;
                double s = Size / 32;
                if (picture == null)
                {
                    StreamGeometry arrow = new StreamGeometry();
                    using (StreamGeometryContext pen = arrow.Open())
                    {
                        Point[] points = { new Point(2, 2), new Point(2, 24), new Point(8, 18.5), new Point(12, 27), new Point(16, 25.2), new Point(12, 16.8), new Point(19.5, 16.8) };
                        pen.BeginFigure(new Point(Reach - 2 * s + points[0].X * s, Reach - 2 * s + points[0].Y * s), true);
                        for (int i = 1; i < points.Length; i++) pen.LineTo(new Point(Reach - 2 * s + points[i].X * s, Reach - 2 * s + points[i].Y * s));
                        pen.EndFigure(true);
                    }
                    graphics.DrawGeometry(new SolidColorBrush(Look.Pink), new Pen(Brushes.Black, 1.2), arrow);
                    return;
                }
                // Her artist's cursor, with its tip where the old arrow's was, so her clicks land the same place.
                double fit = Size * 30 / 32 / Math.Max(picture.PixelSize.Width, picture.PixelSize.Height), inset = Size * 2 / 32;
                Rect where = new Rect(Reach - 2 * s + inset, Reach - 2 * s + inset, picture.PixelSize.Width * fit, picture.PixelSize.Height * fit);
                graphics.DrawImage(picture, new Rect(0, 0, picture.PixelSize.Width, picture.PixelSize.Height), where);
            }
        }

        // On X11 and XWayland an empty input shape lets every click fall through to the window under her pointer.
        private void PassThrough()
        {
            if (passThrough || !OperatingSystem.IsLinux()) return;
            try
            {
                IPlatformHandle handle = TryGetPlatformHandle();
                if (handle == null) return;
                IntPtr display = XOpenDisplay(IntPtr.Zero);
                if (display == IntPtr.Zero) return;
                XShapeCombineRectangles(display, handle.Handle, 2, 0, 0, IntPtr.Zero, 0, 0, 0);
                XFlush(display);
                XCloseDisplay(display);
                passThrough = true;
            }
            catch (Exception) { }
        }

        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
        [DllImport("libXext.so.6")] private static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, IntPtr rectangles, int count, int op, int ordering);
    }
}
