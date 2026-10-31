using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;

namespace CapriDesktop
{
    // Everything her cursor's electricity can be tuned by, in pixels at 100% scale and seconds.
    internal sealed class BoltLook
    {
        internal bool On = true;
        internal Color Red = Color.FromArgb(255, 0, 70);
        internal Color Black = Color.FromArgb(8, 6, 12);
        internal int Count = 4;             // arcs around her while she's moving; idle she keeps about half
        internal float Thickness = 4.2f;    // a ribbon's widest point
        internal float Radius = 26;         // how far from her cursor's middle an arc floats
        internal double ShortestLife = 0.12;
        internal double LongestLife = 0.30;
        internal float Glow = 0.22f;         // 0 to 1, red ribbons only
        internal int Trail = 3;             // fragments left behind per burst of movement
        internal int Burst = 3;             // extra bolts on a click
    }

    // Unstable energy around a cursor an AI has taken over: disconnected filled ribbons, each with its own life, never a spinner.
    internal sealed class CursorBolts
    {
        private const int Most = 14;
        private const double IdleAfter = 1.5;

        private sealed class Bolt
        {
            internal PointF At;             // from the cursor tip, or on screen once it's left behind
            internal bool Left;
            internal float Angle, Length;
            internal double Born, Life;
            internal int Kind;              // 0 red, 1 black, 2 red with a black streak
            internal PointF[] Spine;
            internal PointF[] Branch;
            internal double Nudged;
        }

        internal readonly BoltLook Look = new BoltLook();
        private readonly List<Bolt> bolts = new List<Bolt>();
        private readonly Random random = new Random();
        private PointF last;
        private double movedAt = -10, nextSpawn, nextTrail;
        private int pendingBurst;

        internal int Alive { get { return bolts.Count; } }

        internal void Tap() { pendingBurst = Look.Burst; }

        internal void Clear() { bolts.Clear(); pendingBurst = 0; }

        private float Between(float low, float high) { return low + (float)random.NextDouble() * (high - low); }

        private PointF[] Jagged(float length, int points)
        {
            PointF[] spine = new PointF[points];
            float step = length / (points - 1);
            for (int i = 0; i < points; i++) spine[i] = new PointF(i * step, i == 0 || i == points - 1 ? 0 : Between(-0.28f, 0.28f) * length);
            return spine;
        }

        private Bolt Make(double now, PointF at, float angle, float scale)
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
                PointF root = bolt.Spine[1 + random.Next(bolt.Spine.Length - 2)];
                bolt.Branch = new[] { root, new PointF(root.X + Between(2, 6) * scale, root.Y + Between(-7, 7) * scale), new PointF(root.X + Between(5, 10) * scale, root.Y + Between(-9, 9) * scale) };
            }
            return bolt;
        }

        // Her pointer's tip points up and left, so arcs keep to its sides and rear and the tip stays clear.
        private PointF AroundHer(float scale, out float angle)
        {
            double around = (Between(-40, 130)) * Math.PI / 180;
            float distance = Between(0.45f, 1f) * Look.Radius * scale;
            angle = Between(0, 360);
            float middle = 13 * scale;
            return new PointF(middle + (float)Math.Cos(around) * distance, middle + (float)Math.Sin(around) * distance);
        }

        internal void Step(double now, PointF tip, float scale)
        {
            if (!Look.On) { Clear(); return; }
            float dx = tip.X - last.X, dy = tip.Y - last.Y;
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
                bolt.Spine[point].Y += Between(-0.06f, 0.06f) * bolt.Length;
            }
            if (moved && now >= nextTrail)
            {
                nextTrail = now + 0.16;
                for (int i = 0; i < Look.Trail && bolts.Count < Most; i++)
                {
                    float angle;
                    PointF near = AroundHer(scale, out angle);
                    Bolt left = Make(now, new PointF(tip.X + near.X, tip.Y + near.Y), angle, scale);
                    left.Left = true;
                    left.Length *= 0.7f;
                    bolts.Add(left);
                }
            }
            for (; pendingBurst > 0 && bolts.Count < Most; pendingBurst--)
            {
                float angle = Between(-30, 120);
                double way = angle * Math.PI / 180;
                float from = 8 * scale;
                bolts.Add(Make(now, new PointF((float)Math.Cos(way) * from, (float)Math.Sin(way) * from), angle, scale * 1.4f));
            }
            int wanted = idle ? Math.Max(1, Look.Count / 2) : Look.Count;
            if (now >= nextSpawn && bolts.Count < Math.Min(wanted, Most))
            {
                float angle;
                PointF at = AroundHer(scale, out angle);
                bolts.Add(Make(now, at, angle, scale));
                nextSpawn = now + (idle ? 0.25 + random.NextDouble() * 0.5 : 0.02 + random.NextDouble() * 0.08);
            }
        }

        // A tapered ribbon along a bent path, as one filled polygon, so its corners stay sharp.
        private static PointF[] Ribbon(PointF[] path, float width)
        {
            PointF[] shape = new PointF[path.Length * 2];
            for (int i = 0; i < path.Length; i++)
            {
                PointF before = path[Math.Max(0, i - 1)], after = path[Math.Min(path.Length - 1, i + 1)];
                float x = after.X - before.X, y = after.Y - before.Y, size = (float)Math.Sqrt(x * x + y * y);
                if (size < 0.01f) size = 0.01f;
                float t = (float)i / (path.Length - 1), half = width * (float)Math.Sin(t * Math.PI) * (i % 2 == 0 ? 0.6f : 0.5f);
                shape[i] = new PointF(path[i].X - y / size * half, path[i].Y + x / size * half);
                shape[shape.Length - 1 - i] = new PointF(path[i].X + y / size * half, path[i].Y - x / size * half);
            }
            return shape;
        }

        private static PointF[] Placed(PointF[] path, PointF at, float angle)
        {
            double turn = angle * Math.PI / 180;
            float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn);
            PointF[] placed = new PointF[path.Length];
            for (int i = 0; i < path.Length; i++) placed[i] = new PointF(at.X + path[i].X * c - path[i].Y * s, at.Y + path[i].X * s + path[i].Y * c);
            return placed;
        }

        // Origin is where the cursor tip sits in the picture.
        internal void Draw(Graphics graphics, PointF origin, PointF tip, double now, float scale)
        {
            if (!Look.On || bolts.Count == 0) return;
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush red = new SolidBrush(Look.Red))
            using (SolidBrush black = new SolidBrush(Look.Black))
            using (Pen glow = new Pen(Color.FromArgb((int)(Math.Min(1, Math.Max(0, Look.Glow)) * 255), Look.Red), 3.5f * scale))
            {
                glow.LineJoin = LineJoin.Miter;
                foreach (Bolt bolt in bolts)
                {
                    PointF at = bolt.Left ? new PointF(origin.X + bolt.At.X - tip.X, origin.Y + bolt.At.Y - tip.Y) : new PointF(origin.X + bolt.At.X, origin.Y + bolt.At.Y);
                    float age = (float)((now - bolt.Born) / bolt.Life);
                    PointF[] spine = Placed(bolt.Spine, at, bolt.Angle);
                    float width = Look.Thickness * scale * (bolt.Left ? 0.75f : 1f);
                    // It breaks apart at the end of its life: the ribbon splits into two pieces that part.
                    List<PointF[]> pieces = new List<PointF[]>();
                    if (age < 0.72f || spine.Length < 4) pieces.Add(spine);
                    else
                    {
                        int split = spine.Length / 2;
                        PointF[] first = new PointF[split], second = new PointF[spine.Length - split];
                        Array.Copy(spine, first, split);
                        Array.Copy(spine, split, second, 0, second.Length);
                        pieces.Add(first);
                        pieces.Add(second);
                    }
                    foreach (PointF[] piece in pieces)
                    {
                        if (piece.Length < 2) continue;
                        PointF[] shape = Ribbon(piece, width);
                        if (bolt.Kind != 1)
                        {
                            graphics.DrawPolygon(glow, shape);
                            graphics.FillPolygon(red, shape);
                            if (bolt.Kind == 2) graphics.FillPolygon(black, Ribbon(piece, width * 0.4f));
                        }
                        else graphics.FillPolygon(black, shape);
                    }
                    if (bolt.Branch != null && age < 0.72f) graphics.FillPolygon(bolt.Kind == 1 ? black : red, Ribbon(Placed(bolt.Branch, at, bolt.Angle), width * 0.7f));
                }
            }
            graphics.SmoothingMode = was;
        }
    }
}
