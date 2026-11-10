using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    // How she gets from one place to another. She comes apart into the ribbons she forms from: her hair's red with flecks of
    // white in it, and thin lines of her green. They wind into a ball where she was and stream to where she's going (Lachy,
    // 10 Nov: "lines of green and red, just like when she's forming"). Where she lands she forms from the transformation
    // animation itself: the drawings by Qewiw (mix_qewiw1412_86144) in assets/sprites/form.png, played as drawn, back from
    // the ball of ribbon to her. Lachy, 10 Nov: "id also like to respect the original work too". The ribbons on the way
    // follow those drawings' look and are drawn by this code, afresh on every frame: no picture is slid across the screen.
    // Hiding and quitting have nowhere to go, so they still shatter.
    internal sealed class Ribbons : Overlay
    {
        // One trip. Every place is in screen pixels and every time is in seconds since she left.
        internal sealed class Trip
        {
            internal PointF From, To;      // her middle where she left, and the middle of the ball she forms from where she lands
            internal double GoAt;
            internal double Travel = -1;   // how long her ribbons take to get there; under 0 until she knows where she's going
            internal float Body;           // her height, which every ribbon is sized by
            internal float Scale = 1;      // her size over 240, like her shatter
            internal int Seed;
            internal Color Hair, Accent;
            internal Bitmap Self;          // her picture where she left
            internal Point SelfAt;
            internal Bitmap Form;          // the drawings she forms from, side by side. Lent: her clip keeps it
            internal float FormScale;      // screen pixels for each pixel of the animation's frame
            internal bool FormTurned;      // mirrored. The animation is drawn on her facing left, so this is her facing right
        }

        // wide: how broad it is beside the others. leaves: how long after the first one it sets off, in travel times.
        // lasts: how long it is, as the time its tail follows its head. twists: how many times it turns over along its
        // length. around: where it starts in the bundle. The rest is the loop it winds into where she was: how big, how many
        // times round, how flat, how tipped, and how far off the middle. No two are alike, so the loops read as a ball of
        // ribbon and not as rings. The red ones carry her hair's white flecks.
        private sealed class Strand
        {
            internal readonly bool Hair;
            internal readonly double Wide, Leaves, Lasts, Twists, Around, Coil, Turns, Squash, Tilt, OffX, OffY;
            internal readonly int Flecks;

            internal Strand(bool hair, double wide, double leaves, double lasts, double twists, double around, double coil, double turns, double squash, double tilt, double offX, double offY, int flecks)
            {
                Hair = hair; Wide = wide; Leaves = leaves; Lasts = lasts; Twists = twists; Around = around; Coil = coil; Turns = turns; Squash = squash; Tilt = tilt; OffX = offX; OffY = offY; Flecks = flecks;
            }
        }

        private static readonly Strand[] Strands =
        {
            new Strand(true, 1.00, 0.00, 0.52, 2.4, 0.0, 1.00, 1.5, 0.85, 0.15, 0, 0, 90),
            new Strand(false, 0.28, 0.04, 0.46, 3.1, 2.2, 0.82, 1.8, 0.60, -0.5, 0.12, -0.08, 0),
            new Strand(true, 0.78, 0.09, 0.46, 1.9, 4.1, 0.70, 1.3, 0.70, 0.6, -0.16, 0.1, 70),
            new Strand(false, 0.22, 0.13, 0.55, 2.7, 1.1, 1.14, 1.6, 0.90, -0.2, 0.05, 0.12, 0),
            new Strand(true, 0.60, 0.17, 0.43, 2.2, 3.2, 0.90, 1.2, 0.62, -0.7, 0.1, 0.05, 50),
            new Strand(false, 0.20, 0.21, 0.40, 3.4, 5.4, 0.58, 2.0, 0.75, 0.9, -0.08, -0.1, 0)
        };

        internal sealed class Shape
        {
            internal PointF[] Outline;
            internal bool Hair;
            internal readonly List<float[]> Flecks = new List<float[]>();   // x, y, radius, how bright
        }

        internal const double Gather = 0.24;    // her picture goes and her ribbons wind into a ball
        internal const double Wait = 1.0;       // how long the ball waits to hear where she's going
        internal const double GiveUp = 0.18;    // and how long it takes to go if it never hears
        // The drawings she forms from. The animation holds each for a thirtieth of a second; here it's a twenty-fourth, so
        // they can be seen. The first five are the ball of ribbon, back to front. The last four are its ribbons alone as they
        // leave her, and by then her own window is showing under them.
        internal const int Drawings = 9, Ball = 5;
        internal const double Drawn = 1 / 24.0;
        internal const double Handoff = Ball * Drawn;   // from her first ribbon landing to her own window taking over
        internal const double Whole = 0.7;              // how far into her arrival she is by then: its last frames, where she's whole
        // Where the drawings sit. A cell of the strip is this box of the animation's 2048 px frame, and the ball's middle is
        // here in that frame.
        private static readonly RectangleF FormBox = new RectangleF(0, 124, 1920, 1720);
        private static readonly PointF FormMiddle = new PointF(1015, 1004);
        // The animation starts on her sitting, facing left. One pixel of it is this many of her perch cell's (384 tall), and
        // the ball's middle is this far from the cell's middle, in the cell's pixels, once she's turned to face right.
        internal const float FormInCell = 0.335f, FormSitX = 5.97f, FormSitY = 12.34f;
        private const int Points = 48;
        private readonly Timer ticks = new Timer();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private readonly Random random = new Random();
        private Trip trip;
        private int lift;      // frames left in which they go back above her glitch bars, which show just after they do
        private bool landed;   // her own window has taken over under the last drawings

        internal Ribbons() : base("Capri ribbons")
        {
            ticks.Interval = 15;
            ticks.Tick += delegate { Step(); };
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

        // Her ribbons are in a ball where she was, and haven't been told where she's going. Once the ball starts giving up
        // it's too late to send it anywhere.
        internal bool Waiting { get { return trip != null && trip.Travel < 0 && watch.Elapsed.TotalSeconds < Gather + Wait; } }

        internal bool Going { get { return trip != null && trip.Travel >= 0; } }

        // She comes apart where she is. self is her picture as she was, with its top left corner at canvas; body is the part of
        // the screen she fills.
        internal void Leave(Bitmap self, Point canvas, Rectangle body, Color hair, Color accent, int size)
        {
            Stop();
            if (body.Width < 4 || body.Height < 4 || size < 8) { self.Dispose(); return; }
            trip = new Trip { Self = self, SelfAt = canvas, From = new PointF(body.Left + body.Width / 2f, body.Top + body.Height / 2f), Body = size * 0.85f, Scale = size / 240f, Seed = random.Next(), Hair = hair, Accent = accent };
            lift = 3;
            landed = false;
            watch.Reset();
            watch.Start();
            Step();
            ticks.Start();
        }

        // Tells her waiting ribbons where she lands. middle is where the ball she forms from goes; form is the strip of
        // drawings, each pixel of the animation scale pixels on the screen, mirrored when turned. They take the seconds this
        // gives back to get there, and her own window takes over Handoff after that.
        internal double Go(PointF middle, Bitmap form, float scale, bool turned)
        {
            if (!Waiting) return 0;
            double now = watch.Elapsed.TotalSeconds;
            trip.To = middle;
            trip.Form = form;
            trip.FormScale = scale;
            trip.FormTurned = turned;
            // Told early, the ball still finishes winding up before it goes.
            trip.GoAt = Math.Max(now, Gather);
            lift = 3;
            trip.Travel = TravelTime(Math.Sqrt((trip.To.X - trip.From.X) * (double)(trip.To.X - trip.From.X) + (trip.To.Y - trip.From.Y) * (double)(trip.To.Y - trip.From.Y)));
            return Landing(trip) - now;
        }

        internal void Stop()
        {
            ticks.Stop();
            if (Visible) Hide();
            Drop();
        }

        private void Drop()
        {
            if (trip == null) return;
            if (trip.Self != null) trip.Self.Dispose();
            trip = null;
        }

        private void Step()
        {
            if (trip == null) { Stop(); return; }
            double t = watch.Elapsed.TotalSeconds;
            if (t >= Length(trip)) { Stop(); return; }
            try
            {
                List<Shape> shapes = Shapes(trip, t);
                Rectangle area = Rectangle.Intersect(Reach(trip, t, shapes), SystemInformation.VirtualScreen);
                if (area.Width < 2 || area.Height < 2) { if (Visible) Hide(); return; }
                // The last drawings are the ribbons leaving her, so they stay above her own window as it takes over.
                if (!landed && Drawing(trip, t) >= Ball) { landed = true; lift = 3; }
                // Only the part of the screen she's over this frame, so a trip across two monitors stays cheap.
                using (Native.Surface surface = new Native.Surface(area.Width, area.Height))
                {
                    using (Graphics graphics = Graphics.FromImage(surface.Image)) Paint(graphics, trip, t, shapes, new Point(-area.X, -area.Y));
                    if (Bounds != area) Bounds = area;
                    if (!Visible) Show();
                    surface.Show(Handle, area.Location);
                    if (lift > 0) { lift--; Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); }
                }
            }
            // A screen unplugged, a mod swapped mid-trip and its drawings let go, no memory for the picture: she shows up
            // where she landed without the rest of her ribbons, and nothing is left on the screen.
            catch (Exception) { Stop(); }
        }

        private static double Clamp(double x) { return Math.Max(0, Math.Min(1, x)); }

        private static double Smooth(double x)
        {
            x = Clamp(x);
            return x * x * (3 - 2 * x);
        }

        // A fixed random number from -1 to 1 for a seed and a whole number, and smooth noise made from them: a height at
        // every whole number, eased from one to the next.
        private static double Hash(int seed, int i)
        {
            uint v = unchecked(((uint)seed ^ ((uint)i * 0x9E3779B1u)) * 0x85EBCA6Bu);
            v ^= v >> 15;
            v = unchecked(v * 0xC2B2AE35u);
            v ^= v >> 13;
            return v / 2147483648.0 - 1;
        }

        private static double Wave(int seed, double x)
        {
            double whole = Math.Floor(x), part = x - whole;
            double a = Hash(seed, (int)whole), b = Hash(seed, (int)whole + 1);
            return a + (b - a) * part * part * (3 - 2 * part);
        }

        // Fairly quick (Lachy, 10 Nov), and a little longer the further she goes, so a hop along her window doesn't snap and a
        // trip across two monitors doesn't drag.
        internal static double TravelTime(double distance) { return Math.Max(0.22, Math.Min(0.42, 0.2 + distance / 5200)); }

        // When her first ribbon gets there and she starts to form; under 0 while she doesn't know where that is.
        internal static double Landing(Trip trip) { return trip.Travel < 0 ? -1 : trip.GoAt + trip.Travel; }

        // When her own window should take over.
        internal static double Shown(Trip trip) { return trip.Travel < 0 ? -1 : Landing(trip) + Handoff; }

        // Her last ribbon's tail is in 0.68 travel times after her first one's head.
        internal static double Length(Trip trip) { return trip.Travel < 0 ? Gather + Wait + GiveUp : Landing(trip) + Math.Max(Drawings * Drawn, trip.Travel * 0.7); }

        // Which of the drawings she forms from is showing, or -1.
        internal static int Drawing(Trip trip, double t)
        {
            if (trip.Travel < 0 || trip.Form == null) return -1;
            double since = t - Landing(trip);
            if (since < 0) return -1;
            int index = (int)(since / Drawn);
            return index < Drawings ? index : -1;
        }

        // Where a drawing goes on the screen: the animation's frame around the ball's middle.
        private static RectangleF FormOnScreen(Trip trip)
        {
            float scale = trip.FormScale, left = (FormBox.Left - FormMiddle.X) * scale, right = (FormBox.Right - FormMiddle.X) * scale;
            return RectangleF.FromLTRB(trip.To.X + (trip.FormTurned ? -right : left), trip.To.Y + (FormBox.Top - FormMiddle.Y) * scale, trip.To.X + (trip.FormTurned ? -left : right), trip.To.Y + (FormBox.Bottom - FormMiddle.Y) * scale);
        }

        // A ball that never heard where she went fades where it is.
        private static double GivenUp(Trip trip, double t) { return trip.Travel < 0 ? Smooth((t - Gather - Wait) / GiveUp) : 0; }

        // How much of her picture is still there where she left.
        private static double Leaving(double t) { return 1 - Smooth((t - 0.02) / 0.16); }

        // How far along its way a point of a ribbon is, for how long ago it set off in travel times. -1 to 0 is the loop
        // where she was, from its inner end to where it comes off over the top. 0 to 1 is the way across: easing off, quickest
        // into where she lands, and there it stops, so a ribbon runs into the ball she forms from until its tail is in.
        private static double Along(double since, double lasts)
        {
            if (since <= 0) return Math.Max(-1, since / lasts);
            return since < 1 ? Math.Pow(since, 1.3) : 1;
        }

        // A place on a ribbon's loop: wound 0 is where it goes over the top, 1 is its inner end.
        private static PointF Wound(PointF middle, int turn, int index, double wound, double t, double loop)
        {
            Strand strand = Strands[index];
            double size = loop * strand.Coil;
            double angle = -Math.PI / 2 + turn * wound * Math.PI * 2 * strand.Turns;
            double far = size * (1 - 0.45 * wound) * (1 + 0.2 * Wave(820 + index * 13, wound * 3 + t * 0.6));
            double x = Math.Cos(angle) * far, y = Math.Sin(angle) * far * strand.Squash, cos = Math.Cos(strand.Tilt * turn), sin = Math.Sin(strand.Tilt * turn);
            return new PointF((float)(middle.X + strand.OffX * size * turn + x * cos - y * sin), (float)(middle.Y + strand.OffY * size + x * sin + y * cos));
        }

        // Every ribbon at one moment, as flat shapes: a smooth centre line, and a width that swells and pinches as it turns over.
        internal static List<Shape> Shapes(Trip trip, double t)
        {
            List<Shape> shapes = new List<Shape>();
            if (GivenUp(trip, t) >= 0.99) return shapes;
            bool going = trip.Travel >= 0;
            double loop = trip.Body * 0.3, half = loop * 0.164;
            double dx = going ? trip.To.X - trip.From.X : 0, dy = going ? trip.To.Y - trip.From.Y : 0, distance = Math.Sqrt(dx * dx + dy * dy);
            // Which way round they wind where she was is settled as she leaves, so the ball doesn't flip over when it hears
            // where she's going. They come off over its top heading one way, and land heading the way she went.
            int turn = (trip.Seed & 1) == 0 ? 1 : -1, exit = -turn, side = Math.Abs(dx) >= 1 ? Math.Sign(dx) : exit;
            // A ribbon that comes off heading away from where she's going turns back in a tight hook, not a wide swing.
            double pull = Math.Max(loop * 0.9, distance * 0.33), hook = exit == side ? pull : loop * 0.9, rise = Math.Min(distance * 0.22, trip.Body * 1.1);
            for (int i = 0; i < Strands.Length; i++)
            {
                Strand strand = Strands[i];
                double grown = 1 - Math.Pow(1 - Clamp((t - strand.Leaves * Gather) / (Gather * (1 - strand.Leaves))), 3);
                double since = going ? (t - trip.GoAt) / trip.Travel - strand.Leaves : -1;
                double head = since > 0 ? since : -strand.Lasts * (1 - grown), tail = Math.Max(-strand.Lasts, head - strand.Lasts);
                double from = Along(tail, strand.Lasts), to = Along(head, strand.Lasts);
                if (to - from < 0.004) continue;
                // They run into the middle of the ball she forms from, each from a little off it and from its own side.
                PointF off = Wound(trip.From, turn, i, 0, t, loop), on = going ? new PointF((float)(trip.To.X + strand.OffX * loop), (float)(trip.To.Y + strand.OffY * loop)) : off;
                double leaveTilt = strand.Tilt * turn, landTilt = strand.Tilt * side;
                double pullX1 = off.X + exit * Math.Cos(leaveTilt) * hook, pullY1 = off.Y + exit * Math.Sin(leaveTilt) * hook;
                double pullX2 = on.X - side * Math.Cos(landTilt) * pull, pullY2 = on.Y - side * Math.Sin(landTilt) * pull;
                PointF[] line = new PointF[Points + 1];
                float[] wide = new float[Points + 1];
                for (int n = 0; n <= Points; n++)
                {
                    double u = (double)n / Points, p = from + (to - from) * u;
                    if (p <= 0) line[n] = Wound(trip.From, turn, i, Math.Min(1, -p), t, loop);
                    else
                    {
                        // one sweep across, rising in the middle, and on the way they wind round one another, sway apart, and
                        // gather at each end
                        double q = 1 - p;
                        double x = q * q * q * off.X + 3 * q * q * p * pullX1 + 3 * q * p * p * pullX2 + p * p * p * on.X;
                        double y = q * q * q * off.Y + 3 * q * q * p * pullY1 + 3 * q * p * p * pullY2 + p * p * p * on.Y - rise * 16 * p * p * q * q;
                        double loose = Smooth(p / 0.12) * (1 - Smooth((p - 0.72) / 0.28)), wind = strand.Around + p * 7.5 - t * 2.2, apart = loop * 0.5 * loose;
                        x += (Math.Cos(wind) + Wave(700 + i * 11, p * 3.1 + t * 0.9) * 0.6 + Wave(720 + i * 7, p * 1.4 + t * 0.5) * 0.6) * apart;
                        y += (Math.Sin(wind) * 0.7 + Wave(705 + i * 11, p * 3.1 + t * 0.9) * 0.5 + Wave(725 + i * 7, p * 1.4 + t * 0.5) * 0.45) * apart;
                        line[n] = new PointF((float)x, (float)y);
                    }
                    double flat = Math.Abs(Math.Sin(Math.PI * (u * strand.Twists + strand.Around + t * 1.1)));   // edge on to face on as it turns over
                    double ends = Math.Pow(Math.Min(1, u * 3.2), 0.8) * Math.Pow(Math.Min(1, (1 - u) * 6), 0.7);
                    wide[n] = n == 0 || n == Points ? 0 : (float)(half * strand.Wide * (0.16 + 0.84 * Math.Pow(flat, 0.9)) * ends);
                }
                Shape shape = new Shape { Outline = Outline(line, wide), Hair = strand.Hair };
                // her hair's white flecks, riding in the red
                for (int f = 0; f < strand.Flecks; f++)
                {
                    double u = 0.08 + 0.86 * (Hash(900 + i, f) * 0.5 + 0.5);
                    int n = Math.Min(Points - 1, (int)(u * Points));
                    float part = (float)(u * Points - n), girth = wide[n] + (wide[n + 1] - wide[n]) * part;
                    if (girth < 2) continue;
                    float nx = line[n].Y - line[n + 1].Y, ny = line[n + 1].X - line[n].X, length = (float)Math.Sqrt(nx * nx + ny * ny);
                    if (length < 0.001f) continue;
                    float across = (float)(Hash(950 + i, f) * 0.72 * girth);
                    float radius = (float)Math.Max(0.8, Math.Min(3, girth * 0.14 * (0.6 + Math.Abs(Hash(970 + i, f)))));
                    shape.Flecks.Add(new[] { line[n].X + (line[n + 1].X - line[n].X) * part + nx / length * across, line[n].Y + (line[n + 1].Y - line[n].Y) * part + ny / length * across, radius, (float)(0.5 + 0.45 * Math.Abs(Hash(990 + i, f))) });
                }
                shapes.Add(shape);
            }
            return shapes;
        }

        private static PointF[] Outline(PointF[] line, float[] wide)
        {
            int count = line.Length;
            PointF[] edge = new PointF[count * 2];
            for (int i = 0; i < count; i++)
            {
                PointF a = line[Math.Max(0, i - 1)], b = line[Math.Min(count - 1, i + 1)];
                float nx = a.Y - b.Y, ny = b.X - a.X, length = (float)Math.Sqrt(nx * nx + ny * ny);
                if (length < 0.0001f) { nx = ny = 0; length = 1; }
                float half = wide[i] > 0 ? Math.Max(wide[i], 0.4f) : 0;
                edge[i] = new PointF(line[i].X + nx / length * half, line[i].Y + ny / length * half);
                edge[count * 2 - 1 - i] = new PointF(line[i].X - nx / length * half, line[i].Y - ny / length * half);
            }
            return edge;
        }

        // The part of the screen one frame covers: her ribbons, her picture while it's going, and the drawing she's forming from.
        internal static Rectangle Reach(Trip trip, double t, List<Shape> shapes)
        {
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (Shape shape in shapes)
                foreach (PointF point in shape.Outline)
                {
                    if (point.X < left) left = point.X;
                    if (point.X > right) right = point.X;
                    if (point.Y < top) top = point.Y;
                    if (point.Y > bottom) bottom = point.Y;
                }
            // her picture's slices slide sideways as they go
            int slack = (int)Math.Ceiling(26 * trip.Scale);
            RectangleF[] pictures =
            {
                trip.Self != null && Leaving(t) > 0.01 ? Rectangle.Inflate(new Rectangle(trip.SelfAt, trip.Self.Size), slack, 2) : RectangleF.Empty,
                Drawing(trip, t) >= 0 ? FormOnScreen(trip) : RectangleF.Empty
            };
            foreach (RectangleF picture in pictures)
            {
                if (picture.IsEmpty) continue;
                left = Math.Min(left, picture.Left);
                top = Math.Min(top, picture.Top);
                right = Math.Max(right, picture.Right);
                bottom = Math.Max(bottom, picture.Bottom);
            }
            if (left > right || top > bottom) return Rectangle.Empty;
            return Rectangle.FromLTRB((int)Math.Floor(left) - 4, (int)Math.Floor(top) - 4, (int)Math.Ceiling(right) + 4, (int)Math.Ceiling(bottom) + 4);
        }

        // Flat fills, like the animation: her picture going, each ribbon's outline, the flecks on top, and over them the
        // drawing she's forming from, so her ribbons run into it.
        internal static void Paint(Graphics graphics, Trip trip, double t, List<Shape> shapes, Point shift)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            Random pick = new Random(trip.Seed + (int)(t * 30) * 7919);
            double leaving = Leaving(t);
            if (trip.Self != null && leaving > 0.01) Shatter.Sliced(graphics, trip.Self, new Point(trip.SelfAt.X + shift.X, trip.SelfAt.Y + shift.Y), leaving, Math.Min(1, 0.35 + t / 0.1), trip.Scale, pick, trip.Accent, trip.Hair);
            int alpha = (int)(255 * (1 - GivenUp(trip, t)));
            if (alpha > 2 && shapes.Count != 0)
            {
                graphics.TranslateTransform(shift.X, shift.Y);
                using (Brush hair = new SolidBrush(Color.FromArgb(alpha, trip.Hair)))
                using (Brush accent = new SolidBrush(Color.FromArgb(alpha, trip.Accent)))
                using (Brush bright = new SolidBrush(Color.FromArgb(alpha * 9 / 10, Color.White)))
                using (Brush soft = new SolidBrush(Color.FromArgb(alpha * 6 / 10, Color.White)))
                    foreach (Shape shape in shapes)
                    {
                        graphics.FillPolygon(shape.Hair ? hair : accent, shape.Outline, FillMode.Winding);
                        foreach (float[] fleck in shape.Flecks) graphics.FillEllipse(fleck[3] > 0.72f ? bright : soft, fleck[0] - fleck[2], fleck[1] - fleck[2], fleck[2] * 2, fleck[2] * 2);
                    }
                graphics.ResetTransform();
            }
            int drawing = Drawing(trip, t);
            if (drawing < 0) return;
            RectangleF place = FormOnScreen(trip);
            place.Offset(shift.X, shift.Y);
            int cell = trip.Form.Width / Drawings;
            // top left, top right, bottom left: swapped over to mirror it
            PointF[] corners = trip.FormTurned
                ? new[] { new PointF(place.Right, place.Top), new PointF(place.Left, place.Top), new PointF(place.Right, place.Bottom) }
                : new[] { new PointF(place.Left, place.Top), new PointF(place.Right, place.Top), new PointF(place.Left, place.Bottom) };
            PixelOffsetMode offset = graphics.PixelOffsetMode;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(trip.Form, corners, new RectangleF(drawing * cell, 0, cell, trip.Form.Height), GraphicsUnit.Pixel);
            graphics.PixelOffsetMode = offset;
        }

        // One frame as a picture of a given part of the screen, for her checks and her sheets.
        internal static Bitmap Frame(Trip trip, double t, Rectangle stage)
        {
            Bitmap image = new Bitmap(stage.Width, stage.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(image)) Paint(graphics, trip, t, Shapes(trip, t), new Point(-stage.X, -stage.Y));
            return image;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Dispose(); Drop(); }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class Companion
    {
        // The drawings she forms from where her ribbons land: Qewiw's transformation animation, from its ball of ribbon back
        // to her. Loaded when a trip starts and let go like her other sometimes art; a color mod recolours it with her.
        private readonly Clip form = new Clip("form", Ribbons.Drawings, 1 / Ribbons.Drawn, 1, 0, 0, true);

        private static Color ArtColour(string hex) { return Color.FromArgb(255, Convert.ToInt32(hex.Substring(1, 2), 16), Convert.ToInt32(hex.Substring(3, 2), 16), Convert.ToInt32(hex.Substring(5, 2), 16)); }

        // Her hair's colour and her accent colour as she's wearing them now, which a color mod can change.
        private Color RibbonColour(int slot)
        {
            string[] now = ModList != null ? ModList.ColoursNow : null;
            return ArtColour(now != null && now[slot] != null && now[slot].Length == 7 ? now[slot] : ArtColors.Usual[slot]);
        }

        // Where the middle of the ball she forms from goes. Sitting, the animation is drawn on this very pose, so it lines up
        // with her perch cell to the pixel; anywhere else it forms around her middle.
        private PointF FormMiddle(bool sat)
        {
            if (sat && !lying)
            {
                float cell = preferences.Size / 384f;
                return new PointF(anchor.X + Width / 2f + (facingLeft ? -1 : 1) * (Ribbons.FormSitX * cell + PerchShiftX * preferences.Size), anchor.Y + Height / 2f + Ribbons.FormSitY * cell + PerchShiftY * preferences.Size);
            }
            Rectangle body = BodyOnScreen(anchor, sat ? PerchedBody : Body);
            return new PointF(body.Left + body.Width / 2f, body.Top + body.Height / 2f);
        }

        // Sends her waiting ribbons to where she is now, and keeps her own window back until she has formed there.
        private void LandRibbons(double now, bool sat)
        {
            Bitmap drawings = null;
            // Without the drawings, a skin's broken copy say, her ribbons still run in and she still shows.
            try { drawings = form.Strip; }
            catch (Exception) { }
            double wait = ribbons.Go(FormMiddle(sat), drawings, Ribbons.FormInCell * preferences.Size / 384f, !facingLeft);
            veilUntil = now + wait + Ribbons.Handoff;
        }

        // Her ribbons: they wind into a ball where she was and stream to where she's going, in her hair's red with white
        // flecks and her green; where they land she forms from Qewiw's drawings, the right way round for the way she faces;
        // nothing is left once she's back; a hop takes well under a second; and her own teleport leaves as ribbons, lands
        // where she went and holds her own window back until she has formed.
        private void SmokeRibbons(List<string> checks, string output)
        {
            Color hairColour = ArtColour(ArtColors.Usual[ArtColors.Hair]), accentColour = ArtColour(ArtColors.Usual[ArtColors.Accent]);
            Rectangle stage = new Rectangle(0, 0, 1200, 520);
            Size cell = new Size(240, 300);
            Func<Bitmap> stand = delegate
            {
                Bitmap picture = new Bitmap(cell.Width, cell.Height, PixelFormat.Format32bppPArgb);
                using (Graphics drawn = Graphics.FromImage(picture))
                using (Brush body = new SolidBrush(Color.FromArgb(255, 60, 20, 40)))
                    drawn.FillRectangle(body, 70, 40, 100, 230);
                return picture;
            };
            Bitmap drawings = form.Strip;
            if (drawings == null || drawings.Width % Ribbons.Drawings != 0) throw new Exception("The drawings she forms from aren't in, or aren't " + Ribbons.Drawings + " cells.");
            Ribbons.Trip hop = new Ribbons.Trip { From = new PointF(220, 330), Body = 204, Scale = 1, Seed = 7, Hair = hairColour, Accent = accentColour, Self = stand(), SelfAt = new Point(100, 180) };
            try
            {
                Func<double, int[]> count = delegate(double t)
                {
                    // lit, near where she left, on the way, near where she lands, red, green, then the left and right edges of
                    // what's lit near where she lands
                    int[] seen = { 0, 0, 0, 0, 0, 0, int.MaxValue, int.MinValue };
                    using (Bitmap frame = Ribbons.Frame(hop, t, stage))
                        for (int y = 0; y < frame.Height; y += 2)
                            for (int x = 0; x < frame.Width; x += 2)
                            {
                                Color pixel = frame.GetPixel(x, y);
                                if (pixel.A < 40) continue;
                                seen[0]++;
                                double fromLeft = Math.Sqrt((x - 220) * (double)(x - 220) + (y - 330) * (double)(y - 330)), fromLanding = Math.Sqrt((x - 960) * (double)(x - 960) + (y - 250) * (double)(y - 250));
                                if (fromLeft < 200) seen[1]++;
                                else if (fromLanding < 240) { seen[3]++; seen[6] = Math.Min(seen[6], x); seen[7] = Math.Max(seen[7], x); }
                                else seen[2]++;
                                if (pixel.A > 200 && pixel.R > 200 && pixel.G < 90 && pixel.B < 140) seen[4]++;
                                if (pixel.A > 200 && pixel.G > 180 && pixel.R < 90) seen[5]++;
                            }
                    return seen;
                };
                int[] gathering = count(Ribbons.Gather * 0.95);
                if (gathering[1] < 300 || gathering[2] != 0 || gathering[3] != 0 || gathering[4] < 100 || gathering[5] < 20) throw new Exception("Her ribbons didn't wind into a ball where she was: " + string.Join(",", Array.ConvertAll(gathering, v => v.ToString())));
                if (Ribbons.Landing(hop) >= 0 || Ribbons.Shapes(hop, Ribbons.Gather + Ribbons.Wait + Ribbons.GiveUp).Count != 0) throw new Exception("A ball of ribbon with nowhere to go didn't give up.");
                // The ball winds the same way round before and after it hears where she's going.
                PointF before = Ribbons.Shapes(hop, Ribbons.Gather)[0].Outline[10];
                hop.To = new PointF(960, 250);
                hop.GoAt = 0.26;
                hop.Travel = Ribbons.TravelTime(Math.Sqrt(740.0 * 740 + 80 * 80));
                PointF told = Ribbons.Shapes(hop, Ribbons.Gather)[0].Outline[10];
                if (Math.Abs(before.X - told.X) > 0.5f || Math.Abs(before.Y - told.Y) > 0.5f) throw new Exception("Her ball of ribbon flipped over when it heard where she was going.");
                double lands = Ribbons.Landing(hop);
                int[] halfway = count(hop.GoAt + hop.Travel * 0.72), bare = count(lands + Ribbons.Drawn * 1.5);
                int redFlecks = 0, greenFlecks = 0;
                foreach (Ribbons.Shape shape in Ribbons.Shapes(hop, hop.GoAt + hop.Travel * 0.72)) { if (shape.Hair) redFlecks += shape.Flecks.Count; else greenFlecks += shape.Flecks.Count; }
                if (halfway[2] < 300 || halfway[4] < 100 || halfway[5] < 20 || redFlecks < 12 || greenFlecks != 0) throw new Exception("Her ribbons weren't on their way, in her red with white flecks and her green: " + string.Join(",", Array.ConvertAll(halfway, v => v.ToString())) + ", flecks " + redFlecks + " and " + greenFlecks);
                if (bare[3] < 20 || Ribbons.Drawing(hop, lands) != -1) throw new Exception("Without her drawings, her ribbons didn't run in where she landed: " + bare[3]);
                // With the drawings she forms from: the ball of ribbon first, then its ribbons leaving her, each in its turn.
                hop.Form = drawings;
                hop.FormScale = Ribbons.FormInCell * 240 / 384f;
                int[] ball = count(lands + Ribbons.Drawn * 1.5), streak = count(lands + Ribbons.Drawn * 0.5), loose = count(lands + Ribbons.Drawn * 6.5), last = count(lands + Ribbons.Drawn * (Ribbons.Drawings - 0.5)), after = count(Ribbons.Length(hop) + 0.001);
                if (Ribbons.Drawing(hop, lands - 0.001) != -1 || Ribbons.Drawing(hop, lands) != 0 || Ribbons.Drawing(hop, Ribbons.Shown(hop) - 0.001) != Ribbons.Ball - 1 || Ribbons.Drawing(hop, Ribbons.Shown(hop) + 0.001) != Ribbons.Ball || Ribbons.Drawing(hop, Ribbons.Length(hop)) != -1) throw new Exception("The drawings she forms from didn't each take their turn.");
                if (ball[3] < 4000 || ball[3] < bare[3] * 3 || ball[4] < 3000 || streak[5] < 800 || last[3] < 10 || last[3] > 400) throw new Exception("She didn't form from the animation's drawings where she landed: " + ball[3] + " lit beside " + bare[3] + " without, " + ball[4] + " red, " + streak[5] + " green, " + last[3] + " at the end");
                if (after[0] != 0) throw new Exception("Something stayed after she was back: " + after[0]);
                // As drawn she faces left; facing right the drawings are mirrored about the ball's middle. Measured on a
                // late drawing, once her own ribbons are all in.
                hop.FormTurned = true;
                int[] turned = count(lands + Ribbons.Drawn * 6.5);
                if (Math.Abs((960 - loose[6]) - (turned[7] - 960)) > 6 || Math.Abs((loose[7] - 960) - (960 - turned[6])) > 6 || Math.Abs((960 - loose[6]) - (loose[7] - 960)) < 12) throw new Exception("Facing right, the drawings weren't mirrored: " + loose[6] + " to " + loose[7] + " as drawn, " + turned[6] + " to " + turned[7] + " turned");
                // A different moment is a different drawing, never the same picture somewhere else.
                bool redrawn = false;
                System.Collections.Generic.List<Ribbons.Shape> one = Ribbons.Shapes(hop, hop.GoAt + hop.Travel * 0.5), next = Ribbons.Shapes(hop, hop.GoAt + hop.Travel * 0.5 + 0.016);
                if (one.Count == next.Count && one.Count != 0)
                {
                    float shiftX = next[0].Outline[0].X - one[0].Outline[0].X, shiftY = next[0].Outline[0].Y - one[0].Outline[0].Y;
                    for (int i = 0; i < one[0].Outline.Length && !redrawn; i++) redrawn = Math.Abs(next[0].Outline[i].X - one[0].Outline[i].X - shiftX) > 0.5f || Math.Abs(next[0].Outline[i].Y - one[0].Outline[i].Y - shiftY) > 0.5f;
                }
                if (!redrawn) throw new Exception("A ribbon was the same shape a frame later.");
                double quick = Ribbons.Gather + Ribbons.TravelTime(800) + Ribbons.Handoff, shattering = (perchLeave.Count + 1) / perchLeave.Fps + Shatter.Handoff;
                if (quick > 0.9 || quick >= shattering || Ribbons.TravelTime(0) < 0.2 || Ribbons.TravelTime(9000) > 0.45 || Ribbons.TravelTime(300) >= Ribbons.TravelTime(1500)) throw new Exception("Her ribbons weren't fairly quick: " + quick + " s for a hop, " + shattering + " s the old way.");
                double[] times = new double[3 + Ribbons.Drawings];
                times[0] = Ribbons.Gather;
                times[1] = hop.GoAt + hop.Travel * 0.4;
                times[2] = hop.GoAt + hop.Travel * 0.8;
                for (int i = 0; i < Ribbons.Drawings; i++) times[3 + i] = lands + Ribbons.Drawn * (i + 0.5);
                using (Bitmap sheet = new Bitmap(stage.Width * 3, stage.Height * 4, PixelFormat.Format32bppArgb))
                using (Graphics drawn = Graphics.FromImage(sheet))
                {
                    drawn.Clear(Color.FromArgb(24, 22, 34));
                    for (int i = 0; i < times.Length; i++)
                        using (Bitmap frame = Ribbons.Frame(hop, times[i], stage))
                            drawn.DrawImage(frame, (i % 3) * stage.Width, (i / 3) * stage.Height);
                    sheet.Save(Path.Combine(output, "ribbons.png"), ImageFormat.Png);
                }
            }
            finally { hop.Self.Dispose(); }
            // The window itself, on its own: it shows, waits, sets off when told where, and goes.
            using (Ribbons own = new Ribbons())
            {
                own.Leave(stand(), new Point(60, 60), new Rectangle(130, 100, 100, 230), hairColour, accentColour, 240);
                if (!own.Waiting || own.Going || !own.Visible) throw new Exception("Her ribbons didn't show where she left.");
                double wait = own.Go(new PointF(680, 195), drawings, 0.2f, false);
                if (own.Waiting || !own.Going || wait < 0.2 || wait > 0.7) throw new Exception("Her ribbons didn't set off for where she landed: " + wait);
                own.Stop();
                if (own.Waiting || own.Going || own.Visible) throw new Exception("Her ribbons didn't go when stopped.");
                if (own.Go(PointF.Empty, null, 0, false) != 0) throw new Exception("Ribbons that weren't waiting set off.");
            }
            ThrowCheck(checks, delegate(List<string> list, List<short[]> sounds)
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                StandForThrow(new Point(work.Left + work.Width / 3, work.Top + work.Height / 2));
                RenderFrame();
                instant = false;
                Point there = new Point(anchor.X + Math.Min(500, work.Width / 3), anchor.Y - 40);
                double left = clock.Elapsed.TotalSeconds;
                GlitchOut(delegate { anchor = there; Location = anchor; Play(arrive); }, false);
                if (!ribbons.Waiting || !ribbons.Visible || !veiled || outro == null || Math.Abs(outroAt - clipStart - Ribbons.Gather) > 1e-6) throw new Exception("Her teleport didn't turn her into her ribbons: waiting " + ribbons.Waiting + ", veiled " + veiled + ", away after " + (outroAt - clipStart) + " s");
                outroAt = clock.Elapsed.TotalSeconds - 1;
                Tick();
                RenderFrame();
                double now = clock.Elapsed.TotalSeconds;
                if (!ribbons.Going || clip != arrive || anchor != there || veilUntil <= left + Ribbons.Handoff + 0.2 || veilUntil > now + 0.85 || Math.Abs(veilUntil - clipStart - Ribbons.Whole) > 1e-6) throw new Exception("Her own window didn't wait for her to form: going " + ribbons.Going + ", arrival began " + (now - clipStart) + " s ago, shown in " + (veilUntil - now) + " s");
                // Standing, the ball she forms from is around her middle.
                Rectangle standing = BodyOnScreen(anchor, Body);
                PointF middle = FormMiddle(false);
                if (Math.Abs(middle.X - (standing.Left + standing.Width / 2f)) > 0.01f || Math.Abs(middle.Y - (standing.Top + standing.Height / 2f)) > 0.01f) throw new Exception("Standing, she didn't form around her middle.");
                // Sitting, it's where the animation has it on her perch, and on the other side of her when she faces the other way.
                bool wasLeft = facingLeft;
                facingLeft = false;
                PointF sitRight = FormMiddle(true);
                facingLeft = true;
                PointF sitLeft = FormMiddle(true);
                facingLeft = wasLeft;
                float centre = anchor.X + Width / 2f, off = (Ribbons.FormSitX / 384f + PerchShiftX) * preferences.Size;
                if (Math.Abs(sitRight.X - centre - off) > 0.01f || Math.Abs(sitLeft.X - centre + off) > 0.01f || sitRight.Y != sitLeft.Y || Math.Abs(sitRight.Y - anchor.Y - Height / 2f - (Ribbons.FormSitY / 384f + PerchShiftY) * preferences.Size) > 0.01f) throw new Exception("Sitting, the ball she forms from wasn't where the animation has it: " + sitRight + " and " + sitLeft);
                // An arrival set to start a moment from now holds its first frame, and by the time she shows she's whole.
                clipStart = now + 5;
                if (Advance(now) != 0 || (int)(Ribbons.Whole * arrive.Fps) < 10 || (int)(Ribbons.Whole * perchArrive.Fps) < 10) throw new Exception("An arrival that hadn't started yet wasn't on its first frame, or she'd show before she was whole.");
                clipStart = now - 3;
                RenderFrame();
                if (clip != idle) throw new Exception("After her ribbons she didn't settle where she landed.");
                veilUntil = -1;
                // Somewhere she just appears, with no arrival of her own: her ribbons still go to her, and she waits for them.
                StandForThrow(new Point(work.Left + work.Width / 3, work.Top + work.Height / 2));
                RenderFrame();
                GlitchOut(delegate { anchor = there; Location = anchor; Play(idle); }, false);
                outroAt = clock.Elapsed.TotalSeconds - 1;
                Tick();
                RenderFrame();
                now = clock.Elapsed.TotalSeconds;
                if (!ribbons.Going || veiled || veilUntil < now + 0.3 || veilUntil > now + 0.85) throw new Exception("Where she showed up without an arrival, she didn't wait for her ribbons: going " + ribbons.Going + ", shown in " + (veilUntil - now) + " s");
                veilUntil = -1;
                // A hop called off leaves no ribbons behind, and hiding still shatters.
                StandForThrow(new Point(work.Left + work.Width / 3, work.Top + work.Height / 2));
                RenderFrame();
                GlitchOut(delegate { }, false);
                bool queued = ribbons.Waiting;
                Halt();
                if (!queued || ribbons.Waiting || ribbons.Going || ribbons.Visible || outro != null) throw new Exception("A hop that was called off left her ribbons out.");
                RenderFrame();
                GlitchOut(delegate { }, true);
                bool shattered = !ribbons.Waiting && shatter.Visible && outroAt - clipStart > 0.8;
                Halt();
                if (!shattered) throw new Exception("Hiding and quitting should still shatter, not turn to ribbons.");
                instant = true;
            });
            checks.Add("PASS ribbons: a teleport turns her into her ribbons, in her hair's red with white flecks and her green; they wind into a ball where she was and stream to where she lands, drawn afresh every frame; there she forms from the transformation animation's own drawings (art: Qewiw), mirrored to the way she faces and lined up with her perch; a hop has her back in " + (Ribbons.Gather + Ribbons.TravelTime(800) + Ribbons.Handoff).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s; her own window waits until she has formed; a hop called off clears them; hiding and quitting still shatter");
        }
    }
}
