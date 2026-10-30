using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Her hands in games, where the mouse turns the view and nothing waits for her brain. All of it works from the screen and
    // real input, so it works the same in any game.
    internal static partial class Computer
    {
        internal const int GrayWidth = 240, GrayHeight = 135, GrayReach = 40;
        // The newest screenshot her brain saw, so a spot it names can be found on screen.
        internal static Picture Shown;
        // Pixels a game's view slides per mouse count, measured whenever she looks around in that game.
        private static readonly Dictionary<string, double> viewSpeeds = new Dictionary<string, double>();
        // Games that look down when the mouse goes up.
        private static readonly HashSet<string> upsideDown = new HashSet<string>();
        private static FrameGrab grab;
        private static Rectangle grabbing;
        private static readonly object grabGate = new object();

        // Moves the mouse the way a real one does. A game's camera reads this, and takes no notice of the cursor being placed.
        internal static Action<int, int> Nudge = delegate(int dx, int dy)
        {
            Input[] inputs = { new Input { Type = 0, Data = new InputUnion { Mouse = new MouseInput { X = dx, Y = dy, Flags = 0x0001 } } } };
            SendInput(1, inputs, Marshal.SizeOf(typeof(Input)));
        };

        // A small gray copy of that part of the screen, for telling how the view moved. Games show up in the desktop's own frames
        // where a plain screen copy can get a black window, and that's the fallback when they aren't to be had.
        internal static Func<Rectangle, byte[]> GrayFrame = delegate(Rectangle area)
        {
            lock (grabGate)
            {
                try
                {
                    if (grab == null || grabbing != area)
                    {
                        if (grab != null) grab.Dispose();
                        grab = new FrameGrab();
                        grab.Open(area);
                        grabbing = area;
                    }
                    grab.Next(34);
                    byte[] gray = grab.Gray(GrayWidth, GrayHeight);
                    if (gray != null) return gray;
                }
                catch (Exception) { Unframe(); }
            }
            using (Bitmap shot = Copy(area)) return Gray(shot);
        };

        // Lets go of the desktop's frames, once a job's done with them.
        internal static void Unframe()
        {
            lock (grabGate)
            {
                if (grab != null) grab.Dispose();
                grab = null;
            }
        }

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

        // The middle fifth of a small frame, where the crosshair is.
        internal static byte[] Middle(byte[] gray)
        {
            int wide = GrayWidth / 5, high = GrayHeight / 5;
            byte[] middle = new byte[wide * high];
            for (int row = 0; row < high; row++) Array.Copy(gray, (GrayHeight * 2 / 5 + row) * GrayWidth + GrayWidth * 2 / 5, middle, row * wide, wide);
            return middle;
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

        private static readonly Regex GameFolders = new Regex(@"\\(steamapps\\common|epic games\\(?!launcher\\|epic online)|riot games\\(?!riot client\\)|gog games|ea games|ubisoft game launcher\\games|xboxgames\\(?!minecraft launcher)|roblox\\versions|wildgames)\\", RegexOptions.IgnoreCase);
        private static readonly Regex NotGames = new Regex(@"wallpaper.?engine|steamvr|steamworks|soundpad|aseprite|blender|\\launcher\\|crashreport|anticheat", RegexOptions.IgnoreCase);
        private static readonly Regex Browsers = new Regex(@"^(chrome|msedge|firefox|brave|opera|vivaldi|arc)$", RegexOptions.IgnoreCase);

        [DllImport("kernel32.dll", SetLastError = true)] private static extern IntPtr OpenProcess(int access, bool inherit, int id);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder name, ref int size);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        // Whether a window's program lives where games get installed, so any game counts, not just the ones she knows by name.
        internal static bool GameExe(IntPtr window)
        {
            int process;
            if (window == IntPtr.Zero || GetWindowThreadProcessId(window, out process) == 0 || process == 0) return false;
            IntPtr handle = OpenProcess(0x1000, false, process);
            if (handle == IntPtr.Zero) return false;
            try
            {
                StringBuilder path = new StringBuilder(1024);
                int size = path.Capacity;
                return QueryFullProcessImageName(handle, 0, path, ref size) && GameFolders.IsMatch(path.ToString()) && !NotGames.IsMatch(path.ToString());
            }
            finally { CloseHandle(handle); }
        }

        // The game open on their PC by its window title, nearest the front first, or null.
        internal static Func<string> GameOpen = delegate
        {
            foreach (OpenWindow window in Hands.Windows())
            {
                if (Browsers.IsMatch(window.Program ?? "") || Watching.IsMatch(window.Title ?? "")) continue;
                if (GameTitles.IsMatch((window.Title ?? "") + " " + (window.Program ?? "")) || GameExe(window.Handle)) return window.Title;
            }
            return null;
        };

        private static Rectangle GameArea(Sight sight)
        {
            return sight != null && sight.Bounds.Width > 200 && sight.Bounds.Height > 200 ? sight.Bounds : Display();
        }

        // Turns toward what's wanted in steps and watches the view slide after each one, so she learns any game's mouse as she goes
        // and lands where she meant to. She always turns: looking at open sky, there's nothing to measure yet and she mustn't get stuck.
        internal static string TurnView(ToolArguments args, Sight sight)
        {
            Rectangle area = GameArea(sight);
            string game = sight == null ? "" : sight.Program ?? "";
            double focal = area.Width / 2.0, wantX, wantY;
            bool spot = args.X.HasValue && args.Y.HasValue && Shown != null;
            if (spot)
            {
                Point at = Shown.ToScreen(args.X.Value, args.Y.Value);
                wantX = focal * Math.Atan((at.X - (area.X + area.Width / 2.0)) / focal);
                wantY = focal * Math.Atan((at.Y - (area.Y + area.Height / 2.0)) / focal);
            }
            else
            {
                wantX = focal * (args.Right ?? 0) * Math.PI / 180;
                wantY = -focal * (args.Up ?? 0) * Math.PI / 180;
            }
            double speed;
            bool flipped;
            lock (viewSpeeds)
            {
                if (!viewSpeeds.TryGetValue(game, out speed)) speed = 0;
                flipped = upsideDown.Contains(game);
            }
            double scaleX = (double)area.Width / GrayWidth, scaleY = (double)area.Height / GrayHeight;
            double doneX = 0, doneY = 0, close = Math.Max(8, area.Width / 80.0);
            int probe = 10;
            double sentAll = 0, slidAll = 0;
            bool plain = false, frozen = false, seen = false;
            for (int pass = 0; pass < 9; pass++)
            {
                double leftX = wantX - doneX, leftY = wantY - doneY, left = Math.Sqrt(leftX * leftX + leftY * leftY);
                if (left < close) break;
                // Until she's seen this game's view slide she creeps up on it, assuming a usual mouse.
                double guess = speed > 0 ? speed : focal * 0.0026;
                double counts = speed > 0 ? left / speed : Math.Min(probe, left / guess);
                if (speed <= 0) probe *= 3;
                int countX = (int)Math.Round(counts * leftX / left), countY = (int)Math.Round(counts * leftY / left);
                if (countX == 0 && countY == 0) break;
                byte[] before = GrayFrame(area);
                Turn(countX, flipped ? -countY : countY);
                if (interrupted) return Why;
                Thread.Sleep(130);
                double dx, dy, match;
                bool read = Motion.Shift(before, GrayFrame(area), GrayWidth, GrayHeight, GrayReach, out dx, out dy, out match) && match >= 0.6;
                // Past the edge of what she can measure, a slide can't be trusted.
                if (read && (Math.Abs(dx) >= GrayReach - 1 || Math.Abs(dy) >= GrayReach - 1)) read = false;
                double sent = Math.Sqrt((double)countX * countX + (double)countY * countY);
                double hoped = sent * guess;
                sentAll += sent;
                if (!read)
                {
                    doneX += countX * guess;
                    doneY += countY * guess;
                    if (speed > 0 && hoped > (GrayReach - 4) * scaleX) seen = true;
                    else plain = true;
                    continue;
                }
                double slidX = -dx * scaleX, slidY = -dy * scaleY, slid = Math.Sqrt(slidX * slidX + slidY * slidY);
                slidAll += slid;
                // A clear view that didn't move at all: it's at the end of its travel, or there's no camera to turn.
                if (slid < 6 && hoped >= 40 && (speed > 0 || sentAll >= 300)) { frozen = true; break; }
                if (Math.Abs(countY) > Math.Abs(countX) && Math.Abs(slidY) >= 12 && slidY * countY < 0)
                {
                    flipped = !flipped;
                    lock (viewSpeeds) { if (flipped) upsideDown.Add(game); else upsideDown.Remove(game); }
                }
                if (slid >= 6) seen = true;
                doneX += slidX;
                doneY += slidY;
                if (slid >= 16)
                {
                    double found = slid / sent;
                    speed = speed > 0 ? (speed + found) / 2 : found;
                    lock (viewSpeeds) viewSpeeds[game] = speed;
                }
            }
            if ((frozen && !seen) || (!plain && !seen && slidAll < 6)) return "The view didn't move: a menu may be open, the game may need a click to take the mouse, or you're already looking as far as it goes.";
            if (frozen) return "Turned as far as the view would go.";
            if (plain && !seen) return "Turned, but the view is too plain to tell how far. Check the next screenshot.";
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
            Stopwatch clock = Stopwatch.StartNew();
            bool changed = false;
            byte[] first = watching ? Middle(GrayFrame(area)) : null;
            try
            {
                Hold(codes, true);
                Mouse(right ? 0x0008 : 0x0002, 0);
                while (clock.Elapsed.TotalSeconds < wanted && !interrupted)
                {
                    Thread.Sleep(watching ? 70 : 30);
                    if (!watching || Changed(first, Middle(GrayFrame(area))) < 0.45) continue;
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
