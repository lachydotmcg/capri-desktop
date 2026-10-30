using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace CapriDesktop
{
    // Her hands in games, where the mouse turns the view and nothing waits for her brain. All of it works from the screen and
    // real input, so it works the same in any game.
    internal static partial class Computer
    {
        internal const int GrayWidth = 160, GrayHeight = 90;
        // The newest screenshot her brain saw, so a spot it names can be found on screen.
        internal static Picture Shown;
        // Pixels a game's view slides per mouse count, measured the first time she looks around in that game.
        private static readonly Dictionary<string, double> viewSpeeds = new Dictionary<string, double>();

        // Moves the mouse the way a real one does. A game's camera reads this, and takes no notice of the cursor being placed.
        internal static Action<int, int> Nudge = delegate(int dx, int dy)
        {
            Input[] inputs = { new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = dx, Y = dy, Flags = 0x0001 } } } };
            SendInput(1, inputs, Marshal.SizeOf(typeof(Input)));
        };

        // A small gray copy of that part of the screen, for telling how the view moved.
        internal static Func<Rectangle, byte[]> GrayFrame = delegate(Rectangle area)
        {
            using (Bitmap shot = Copy(area)) return Gray(shot);
        };

        internal static byte[] Gray(Bitmap shot)
        {
            BitmapData data = shot.LockBits(new Rectangle(Point.Empty, shot.Size), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
            try
            {
                byte[] raw = new byte[Math.Abs(data.Stride) * shot.Height];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                byte[] gray = new byte[GrayWidth * GrayHeight];
                for (int y = 0; y < GrayHeight; y++)
                {
                    int row = (y * 2 + 1) * shot.Height / (GrayHeight * 2) * data.Stride;
                    for (int x = 0; x < GrayWidth; x++)
                    {
                        int at = row + (x * 2 + 1) * shot.Width / (GrayWidth * 2) * 3;
                        gray[y * GrayWidth + x] = (byte)((raw[at] + raw[at + 1] * 2 + raw[at + 2]) / 4);
                    }
                }
                return gray;
            }
            finally { shot.UnlockBits(data); }
        }

        // The share of two small frames that differs.
        internal static double Changed(byte[] before, byte[] after)
        {
            if (before == null || after == null || before.Length != after.Length) return 1;
            int different = 0;
            for (int i = 0; i < before.Length; i++) if (Math.Abs(before[i] - after[i]) > 14) different++;
            return (double)different / before.Length;
        }

        // How far the whole view slid between two small frames. False when no slide fits, like after a cut to another scene.
        internal static bool Slid(byte[] before, byte[] after, out int dx, out int dy)
        {
            dx = 0;
            dy = 0;
            if (before == null || after == null || before.Length != after.Length) return false;
            const int reachX = 56, reachY = 28, step = 2;
            long total = 0, bestScore = long.MaxValue;
            int tried = 0;
            for (int y = -reachY; y <= reachY; y++)
                for (int x = -reachX; x <= reachX; x++)
                {
                    long score = 0;
                    int counted = 0;
                    int fromX = Math.Max(0, -x), toX = Math.Min(GrayWidth, GrayWidth - x);
                    int fromY = Math.Max(0, -y), toY = Math.Min(GrayHeight, GrayHeight - y);
                    for (int row = fromY; row < toY; row += step)
                    {
                        int a = row * GrayWidth, b = (row + y) * GrayWidth + x;
                        for (int column = fromX; column < toX; column += step) { score += Math.Abs(before[a + column] - after[b + column]); counted++; }
                    }
                    if (counted < 200) continue;
                    score = score * 1000 / counted;
                    total += score;
                    tried++;
                    if (score < bestScore) { bestScore = score; dx = x; dy = y; }
                }
            return tried > 0 && bestScore * 3 < total / tried * 2;
        }

        private static void Turn(int dx, int dy)
        {
            int longest = Math.Max(Math.Abs(dx), Math.Abs(dy));
            int steps = Math.Max(1, (int)Math.Ceiling(longest / 48.0));
            int sentX = 0, sentY = 0;
            for (int i = 1; i <= steps && !interrupted; i++)
            {
                int wantX = (int)Math.Round((double)dx * i / steps), wantY = (int)Math.Round((double)dy * i / steps);
                Nudge(wantX - sentX, wantY - sentY);
                sentX = wantX;
                sentY = wantY;
                Thread.Sleep(4);
            }
        }

        // She turns a little, sees how far the view slid and turns back, so she knows this game's mouse without being told.
        internal static double ViewSpeed(string game, Rectangle area)
        {
            double known;
            lock (viewSpeeds) if (viewSpeeds.TryGetValue(game ?? "", out known)) return known;
            foreach (int counts in new[] { 60, 180, 540, 1600 })
            {
                if (interrupted) return 0;
                byte[] before = GrayFrame(area);
                Turn(counts, 0);
                Thread.Sleep(160);
                byte[] after = GrayFrame(area);
                Turn(-counts, 0);
                Thread.Sleep(120);
                int dx, dy;
                if (!Slid(before, after, out dx, out dy) || Math.Abs(dx) < 6) continue;
                double speed = Math.Abs(dx) * (double)area.Width / GrayWidth / counts;
                lock (viewSpeeds) viewSpeeds[game ?? ""] = speed;
                return speed;
            }
            return 0;
        }

        internal static string Traced(ToolArguments args)
        {
            if (args == null) return "";
            List<string> parts = new List<string>();
            if (args.Coordinate != null && args.Coordinate.Length == 2) parts.Add("at " + args.Coordinate[0] + "," + args.Coordinate[1]);
            if (args.X.HasValue) parts.Add("x " + args.X + " y " + args.Y);
            if (args.Right.HasValue) parts.Add("right " + args.Right);
            if (args.Up.HasValue) parts.Add("up " + args.Up);
            if (args.Keys != null) parts.Add("keys " + args.Keys);
            if (args.Hold != null) parts.Add("hold " + args.Hold);
            if (args.Press != null) parts.Add("press " + args.Press);
            if (args.Seconds != null) parts.Add("seconds " + args.Seconds);
            if (args.Text != null) parts.Add("text " + args.Text);
            if (args.Name != null) parts.Add("name " + args.Name);
            if (args.Button != null) parts.Add("button " + args.Button);
            if (args.Duration.HasValue) parts.Add("for " + args.Duration);
            return string.Join(" ", parts.ToArray());
        }

        private static Rectangle GameArea(Sight sight)
        {
            return sight != null && sight.Bounds.Width > 200 && sight.Bounds.Height > 200 ? sight.Bounds : Display();
        }

        internal static string TurnView(ToolArguments args, Sight sight)
        {
            Rectangle area = GameArea(sight);
            double speed = ViewSpeed(sight == null ? "" : sight.Program, area);
            if (interrupted) return Why;
            if (speed <= 0) return "Couldn't turn the camera: the view didn't move. A menu may be open, or the game may need a click to take the mouse.";
            // The view's width stands for about 90 degrees, near enough for a first turn; the next look corrects the rest.
            double focal = area.Width / 2.0;
            double pushX, pushY;
            bool spot = args.X.HasValue && args.Y.HasValue && Shown != null;
            if (spot)
            {
                Point at = Shown.ToScreen(args.X.Value, args.Y.Value);
                pushX = focal * Math.Atan((at.X - (area.X + area.Width / 2.0)) / focal);
                pushY = focal * Math.Atan((at.Y - (area.Y + area.Height / 2.0)) / focal);
            }
            else
            {
                pushX = focal * (args.Right ?? 0) * Math.PI / 180;
                pushY = -focal * (args.Up ?? 0) * Math.PI / 180;
            }
            byte[] before = GrayFrame(area);
            Turn((int)Math.Round(pushX / speed), (int)Math.Round(pushY / speed));
            if (interrupted) return Why;
            Thread.Sleep(120);
            if (Math.Abs(pushX) + Math.Abs(pushY) > 30 && Changed(before, GrayFrame(area)) < 0.02) return "Couldn't turn the camera: the view didn't move. A menu may be open.";
            return spot ? "Turned so that spot is at the middle of the view." : "Turned.";
        }

        // Holds the keys until the time's up, jumps when something's in the way, and lets go by herself when she's really stuck.
        internal static string Walk(string keys, string seconds, string jump, Sight sight)
        {
            string chord = Combo(string.IsNullOrWhiteSpace(keys) ? "w" : keys);
            if (chord == null) return "There's no key called " + (keys ?? "").Trim() + ".";
            if (Barred(chord)) return "Won't press " + chord + ".";
            short[] codes, space;
            Modifiers(chord, out codes);
            Modifiers("space", out space);
            double wanted = Amount(seconds, 2);
            if (wanted <= 0) wanted = 2;
            bool hops = !string.Equals(jump, "no", StringComparison.OrdinalIgnoreCase);
            Rectangle area = GameArea(sight);
            Stopwatch clock = Stopwatch.StartNew();
            int jumps = 0;
            long stillSince = -1;
            bool blocked = false, cut = false;
            byte[] last = GrayFrame(area);
            try
            {
                Hold(codes, true);
                while (clock.Elapsed.TotalSeconds < wanted && !interrupted)
                {
                    Thread.Sleep(90);
                    byte[] now = GrayFrame(area);
                    double moved = Changed(last, now);
                    last = now;
                    if (moved > 0.7 && clock.ElapsedMilliseconds > 400) { cut = true; break; }
                    if (moved >= 0.012) { stillSince = -1; continue; }
                    if (stillSince < 0) { stillSince = clock.ElapsedMilliseconds; continue; }
                    if (clock.ElapsedMilliseconds - stillSince < 420) continue;
                    if (!hops || jumps >= 2) { blocked = true; break; }
                    jumps++;
                    Hold(space, true);
                    Thread.Sleep(80);
                    Hold(space, false);
                    Pause(380);
                    stillSince = -1;
                    last = GrayFrame(area);
                }
            }
            finally { Hold(codes, false); }
            if (interrupted) return Why;
            string time = clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
            if (cut) return "Stopped after " + time + ": the whole screen changed.";
            if (blocked) return "Stopped after " + time + ": something's in the way" + (jumps > 0 ? ", even after jumping." : ".");
            return "Moved for " + time + (jumps > 0 ? ", jumping " + jumps + (jumps == 1 ? " time." : " times.") : ".");
        }

        // Holds a mouse button, with any keys, for as long as it takes. With stop on, she lets go when what's under the crosshair changes.
        internal static string MouseHold(string button, string seconds, string keys, string stop, Sight sight)
        {
            bool right = string.Equals(button, "right", StringComparison.OrdinalIgnoreCase);
            string chord = string.IsNullOrWhiteSpace(keys) ? null : Combo(keys);
            if (!string.IsNullOrWhiteSpace(keys) && chord == null) return "There's no key called " + keys.Trim() + ".";
            if (chord != null && Barred(chord)) return "Won't press " + chord + ".";
            short[] codes = new short[0];
            if (chord != null) Modifiers(chord, out codes);
            double wanted = Amount(seconds, 1);
            if (wanted <= 0) wanted = 1;
            bool watching = string.Equals(stop, "yes", StringComparison.OrdinalIgnoreCase);
            Rectangle area = GameArea(sight);
            Rectangle middle = new Rectangle(area.X + area.Width * 2 / 5, area.Y + area.Height * 2 / 5, area.Width / 5, area.Height / 5);
            Stopwatch clock = Stopwatch.StartNew();
            bool changed = false;
            byte[] first = watching ? GrayFrame(middle) : null;
            try
            {
                Hold(codes, true);
                Mouse(right ? 0x0008 : 0x0002, 0);
                while (clock.Elapsed.TotalSeconds < wanted && !interrupted)
                {
                    Thread.Sleep(watching ? 70 : 30);
                    if (!watching || Changed(first, GrayFrame(middle)) < 0.45) continue;
                    changed = true;
                    break;
                }
            }
            finally
            {
                Mouse(right ? 0x0010 : 0x0004, 0);
                Hold(codes, false);
            }
            if (interrupted) return Why;
            string time = clock.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";
            return changed ? "Let go after " + time + ": what's under the crosshair changed." : "Held for " + time + ".";
        }
    }
}
