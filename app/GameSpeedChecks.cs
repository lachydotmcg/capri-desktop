using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private sealed class TurnTimes
        {
            internal string Label;
            internal readonly List<double> Look = new List<double>(), Capture = new List<double>(), Build = new List<double>(), Act = new List<double>(), Turn = new List<double>();
            internal readonly List<long> Sent = new List<long>();
            internal readonly List<int> Pictures = new List<int>();
            internal string Ended;
        }

        private static double TurnMedian(List<double> values)
        {
            if (values.Count == 0) return 0;
            List<double> sorted = new List<double>(values);
            sorted.Sort();
            return sorted[sorted.Count / 2];
        }

        private static string TurnMs(double value) { return value.ToString("0", CultureInfo.InvariantCulture); }

        // A made-up game frame: sky, ground, trees, a HUD and some grain, panned a little each step, so it costs about what a
        // real one does to encode and send.
        private static Bitmap TurnFrame(int width, int height, int step)
        {
            Bitmap frame = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics paint = Graphics.FromImage(frame))
            {
                using (LinearGradientBrush sky = new LinearGradientBrush(new Rectangle(0, 0, width, height / 2 + 1), Color.FromArgb(70, 120, 200), Color.FromArgb(180, 210, 240), 90f))
                    paint.FillRectangle(sky, 0, 0, width, height / 2 + 1);
                using (SolidBrush ground = new SolidBrush(Color.FromArgb(70, 110, 50))) paint.FillRectangle(ground, 0, height / 2, width, height - height / 2);
                Random place = new Random(11);
                for (int tree = 0; tree < 60; tree++)
                {
                    int x = (place.Next(width) + step * 37) % width, y = height / 2 - 20 + place.Next(height / 3), size = 30 + place.Next(120);
                    using (SolidBrush trunk = new SolidBrush(Color.FromArgb(90, 60, 30))) paint.FillRectangle(trunk, x + size / 3, y, size / 4, size);
                    using (SolidBrush leaves = new SolidBrush(Color.FromArgb(30 + place.Next(60), 100 + place.Next(80), 30))) paint.FillEllipse(leaves, x, y - size / 2, size, size * 3 / 4);
                }
                using (Font hud = new Font(FontFamily.GenericSansSerif, Math.Max(12, height / 40), FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    paint.DrawString("HP " + (100 - step % 7) + "   AMMO 30/90   WAVE " + (3 + step / 4), hud, Brushes.White, 24, height - height / 12);
                    paint.DrawString("Press E to open the chest", hud, Brushes.Yellow, width / 2 - width / 8, height / 2 + height / 6);
                }
                paint.DrawLine(Pens.White, width / 2 - 12, height / 2, width / 2 + 12, height / 2);
                paint.DrawLine(Pens.White, width / 2, height / 2 - 12, width / 2, height / 2 + 12);
            }
            BitmapData data = frame.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadWrite, PixelFormat.Format24bppRgb);
            try
            {
                byte[] raw = new byte[data.Stride * height];
                Marshal.Copy(data.Scan0, raw, 0, raw.Length);
                uint seed = unchecked((uint)(step * 7919 + 17));
                for (int i = 0; i < raw.Length; i++)
                {
                    seed ^= seed << 13;
                    seed ^= seed >> 17;
                    seed ^= seed << 5;
                    int grain = raw[i] + (int)(seed % 21) - 10;
                    raw[i] = (byte)(grain < 0 ? 0 : grain > 255 ? 255 : grain);
                }
                Marshal.Copy(raw, 0, data.Scan0, raw.Length);
            }
            finally { frame.UnlockBits(data); }
            return frame;
        }

        // Her game loop timed stage by stage on made-up frames, with a brain that answers at once, so the numbers are hers alone:
        // the look (capture and encoding), building the request, and her hands. Nothing here touches the real mouse or keyboard.
        private static void SmokeGameSpeed(List<string> checks, string output)
        {
            Func<Rectangle, Bitmap> grabWas = Computer.Grab;
            Func<Rectangle?, Picture> shootWas = Computer.Shoot;
            Func<Sight> glimpseWas = Computer.Glimpse, lookWas = Computer.Look;
            Func<IntPtr> frontWas = Computer.Front, foregroundWas = Hands.Foreground;
            Func<IntPtr, bool> activeWas = Hands.Active, aliveWas = Computer.Alive, aboveWas = Computer.AboveHer;
            Func<OpenWindow, string, bool> steerWas = Hands.Steer;
            Func<IntPtr, IntPtr, bool> sameWas = Computer.SameWindow;
            Func<IntPtr, Rectangle> clientWas = Computer.ClientArea;
            Func<IntPtr, string> titleWas = Computer.TitleOf;
            Func<Point, IntPtr> atWas = Computer.WindowAt;
            Func<Rectangle, byte[]> grayWas = Computer.GrayFrame;
            Action<short, bool, bool> keyWas = Computer.SendKey;
            Action<int, int> mouseWas = Computer.SendMouse, nudgeWas = Computer.Nudge;
            Action<Point> trackWas = Computer.Track, tapWas = Computer.Tapped, putWas = Computer.PutCursor;
            Func<Point> cursorWas = Computer.CursorAt;
            Action<bool> lendWas = Computer.Lending, signWas = InControl.Changed;
            Func<bool> hiddenWas = Computer.CursorHidden, escapeWas = Computer.Escape, secretWas = Computer.SecretFocus;
            Func<Point, bool> gameUnderWas = Computer.GameUnder;
            Func<Point, Target> underWas = Computer.Under;
            Func<Target> focusWas = Computer.Focus;
            Func<string> gameOpenWas = Computer.GameOpen;
            Func<List<OpenWindow>> windowsWas = Hands.Windows;
            Func<string, ToolArguments, Picture, string> useWas = Computer.Use;
            Func<string, ToolArguments, Sight, string> actWas = Computer.Act;
            Action haltWas = Computer.Halted;
            Action<string> runWas = Hands.Run;
            Picture shownWas = Computer.Shown;
            string notesWas = GameNotes.Folder;
            bool flinchedWas = Computer.Flinched, streamWas = Computer.Streaming;
            int soundWas = Sfx.Level;
            string notes = Path.Combine(output, "game-speed-notes-" + Guid.NewGuid().ToString("N"));
            IntPtr game = new IntPtr(7301);
            Sight sight = new Sight { Window = game, Title = "Minecraft 26.3 - Singleplayer", Program = "javaw", Bounds = new Rectangle(0, 0, 1920, 1080) };
            Bitmap[] frames = new Bitmap[3];
            StringBuilder table = new StringBuilder();
            try
            {
                for (int i = 0; i < frames.Length; i++) frames[i] = TurnFrame(1920, 1080, i + 1);
                Directory.CreateDirectory(notes);
                GameNotes.Folder = notes;
                Hands.Run = delegate { throw new Exception("Her timed game turns tried to open something."); };
                Hands.Windows = delegate { return new List<OpenWindow>(); };
                Computer.GameOpen = delegate { return null; };
                Computer.Glimpse = Computer.Look = delegate { return new Sight { Window = game, Title = sight.Title, Program = sight.Program, Bounds = sight.Bounds }; };
                Computer.Front = Hands.Foreground = delegate { return game; };
                Hands.Active = Computer.Alive = delegate(IntPtr window) { return window == game; };
                Hands.Steer = delegate { return true; };
                Computer.AboveHer = delegate { return false; };
                Computer.SameWindow = delegate(IntPtr one, IntPtr two) { return one == two; };
                Computer.ClientArea = delegate { return sight.Bounds; };
                Computer.TitleOf = delegate { return sight.Title; };
                Computer.WindowAt = delegate { return game; };
                int grays = 0;
                Computer.GrayFrame = delegate
                {
                    byte[] gray = new byte[Computer.GrayWidth * Computer.GrayHeight];
                    byte tone = (byte)(++grays % 2 == 0 ? 120 : 60);
                    for (int i = 0; i < 4000; i++) gray[i] = tone;
                    return gray;
                };
                int keysDown = 0, buttonsDown = 0;
                Computer.SendKey = delegate(short code, bool up, bool extended) { keysDown += up ? -1 : 1; };
                Computer.SendMouse = delegate(int flags, int data) { if ((flags & 0x002A) != 0) buttonsDown++; if ((flags & 0x0054) != 0) buttonsDown--; };
                Computer.Nudge = delegate { };
                Point cursor = new Point(5, 5);
                Computer.Track = Computer.Tapped = delegate { };
                Computer.PutCursor = delegate(Point at) { cursor = at; };
                Computer.CursorAt = delegate { return cursor; };
                Computer.Lending = InControl.Changed = delegate { };
                Computer.CursorHidden = Computer.Escape = Computer.SecretFocus = delegate { return false; };
                Computer.GameUnder = delegate { return false; };
                Computer.Under = delegate { return null; };
                Computer.Focus = delegate { return null; };
                Computer.Halted = delegate { };
                Computer.Flinched = Computer.Streaming = false;
                Sfx.Level = 0;

                Stopwatch clock = Stopwatch.StartNew();
                int frameAt = 0;
                bool still = false;
                TurnTimes row = null;
                double lookEnd = 0, acting = 0;
                Computer.Grab = delegate(Rectangle area)
                {
                    Stopwatch copying = Stopwatch.StartNew();
                    Bitmap source = frames[still ? 0 : frameAt++ % frames.Length];
                    Bitmap copy = source.Clone(new Rectangle(Point.Empty, source.Size), PixelFormat.Format24bppRgb);
                    if (row != null) row.Capture.Add(copying.Elapsed.TotalMilliseconds);
                    return copy;
                };
                Computer.Shoot = delegate(Rectangle? part)
                {
                    Stopwatch looking = Stopwatch.StartNew();
                    Picture taken = Computer.Snap(part);
                    if (row != null) row.Look.Add(looking.Elapsed.TotalMilliseconds);
                    lookEnd = clock.Elapsed.TotalMilliseconds;
                    return taken;
                };
                Computer.Use = delegate(string name, ToolArguments args, Picture shown)
                {
                    Stopwatch doing = Stopwatch.StartNew();
                    try { return Computer.Member(name, args, shown); }
                    finally { acting += doing.Elapsed.TotalMilliseconds; }
                };
                Computer.Act = delegate(string tool, ToolArguments args, Sight at)
                {
                    Stopwatch doing = Stopwatch.StartNew();
                    try { return Computer.Do(tool, args, at); }
                    finally { acting += doing.Elapsed.TotalMilliseconds; }
                };

                const int Turns = 5;
                Func<string, bool, Func<int, string>, TurnTimes> driven = delegate(string label, bool same, Func<int, string> reply)
                {
                    TurnTimes times = new TurnTimes { Label = label };
                    row = times;
                    still = same;
                    acting = 0;
                    double lastAsk = -1;
                    int turn = 0;
                    Computer.Finish();
                    Computer.Arm();
                    Job playing = new Job("play minecraft");
                    playing.Drive = delegate(string system, string tools, string messages)
                    {
                        double now = clock.Elapsed.TotalMilliseconds;
                        if (lastAsk >= 0)
                        {
                            times.Turn.Add(now - lastAsk);
                            times.Act.Add(acting);
                        }
                        times.Build.Add(now - lookEnd);
                        times.Sent.Add(system.Length + tools.Length + messages.Length);
                        times.Pictures.Add(Regex.Matches(messages, "\"type\":\"image\"").Count);
                        acting = 0;
                        lastAsk = clock.Elapsed.TotalMilliseconds;
                        turn++;
                        if (turn > Turns) return Brain.Parsed("{\"content\":[{\"type\":\"text\",\"text\":\"Done playing for now.\"}],\"stop_reason\":\"end_turn\"}");
                        return Brain.Parsed(reply(turn));
                    };
                    times.Ended = playing.Run();
                    row = null;
                    if (keysDown != 0 || buttonsDown != 0) throw new Exception(label + " left input held: " + keysDown + " keys, " + buttonsDown + " buttons.");
                    return times;
                };
                Func<string, Func<int, string>, TurnTimes> sighted = delegate(string label, Func<int, string> reply)
                {
                    TurnTimes times = new TurnTimes { Label = label };
                    row = times;
                    still = false;
                    acting = 0;
                    double lastAsk = -1;
                    int turn = 0;
                    Computer.Finish();
                    Computer.Arm();
                    Job playing = new Job("play minecraft");
                    playing.Seer = delegate(byte[] picture, string system, string text)
                    {
                        double now = clock.Elapsed.TotalMilliseconds;
                        if (lastAsk >= 0)
                        {
                            times.Turn.Add(now - lastAsk);
                            times.Act.Add(acting);
                        }
                        times.Build.Add(now - lookEnd);
                        times.Sent.Add(system.Length + text.Length + (picture == null ? 0 : (picture.Length + 2) / 3 * 4));
                        times.Pictures.Add(picture == null ? 0 : 1);
                        acting = 0;
                        lastAsk = clock.Elapsed.TotalMilliseconds;
                        turn++;
                        return turn > Turns ? "done{\"text\": \"Done playing for now.\"}" : reply(turn);
                    };
                    times.Ended = playing.Run();
                    row = null;
                    if (keysDown != 0 || buttonsDown != 0) throw new Exception(label + " left input held: " + keysDown + " keys, " + buttonsDown + " buttons.");
                    return times;
                };

                List<TurnTimes> rows = new List<TurnTimes>();
                rows.Add(driven("claude: walk, key, hold_key, click", false, delegate(int turn)
                {
                    return "{\"content\":[{\"type\":\"tool_use\",\"id\":\"w" + turn + "\",\"name\":\"walk\",\"input\":{\"keys\":\"w\",\"seconds\":\"0.4\",\"jump\":\"no\"}},"
                        + "{\"type\":\"tool_use\",\"id\":\"k" + turn + "\",\"name\":\"key\",\"toolset_name\":\"computer\",\"input\":{\"text\":\"space\"}},"
                        + "{\"type\":\"tool_use\",\"id\":\"h" + turn + "\",\"name\":\"hold_key\",\"toolset_name\":\"computer\",\"input\":{\"text\":\"d\",\"duration\":0.3}},"
                        + "{\"type\":\"tool_use\",\"id\":\"c" + turn + "\",\"name\":\"left_click\",\"toolset_name\":\"computer\",\"input\":{\"coordinate\":[640,360]}}],\"stop_reason\":\"tool_use\"}";
                }));
                rows.Add(driven("claude: one key, screen unchanged", true, delegate(int turn)
                {
                    return "{\"content\":[{\"type\":\"tool_use\",\"id\":\"k" + turn + "\",\"name\":\"key\",\"toolset_name\":\"computer\",\"input\":{\"text\":\"f24\"}}],\"stop_reason\":\"tool_use\"}";
                }));
                rows.Add(sighted("grid brain: walk, press, click", delegate(int turn)
                {
                    return "walk{\"keys\": \"w\", \"seconds\": \"0.4\", \"jump\": \"no\"}\npress{\"keys\": \"space\"}\nclick{\"x\": 50, \"y\": 50}";
                }));

                table.AppendLine("Her game turn, stage by stage, with a brain that answers at once (median of " + Turns + " turns, ms; bytes are the whole request).");
                table.AppendLine("scenario | look (capture + encode) | capture | request build | hands | turn without brain | request bytes | pictures sent");
                foreach (TurnTimes timed in rows)
                {
                    List<double> sent = new List<double>();
                    foreach (long bytes in timed.Sent) sent.Add(bytes);
                    List<double> pictures = new List<double>();
                    foreach (int count in timed.Pictures) pictures.Add(count);
                    table.AppendLine(timed.Label + " | " + TurnMs(TurnMedian(timed.Look)) + " | " + TurnMs(TurnMedian(timed.Capture)) + " | " + TurnMs(TurnMedian(timed.Build)) + " | " + TurnMs(TurnMedian(timed.Act)) + " | " + TurnMs(TurnMedian(timed.Turn)) + " | " + TurnMs(TurnMedian(sent)) + " | " + TurnMs(TurnMedian(pictures)));
                    if (timed.Turn.Count < Turns - 1 || timed.Look.Count == 0) throw new Exception(timed.Label + " didn't run its turns: " + timed.Ended);
                }

                table.AppendLine();
                table.AppendLine("One look on its own (median of 5, ms) | jpeg bytes | sharp bytes");
                foreach (Size size in new[] { new Size(1920, 1080), new Size(2560, 1440) })
                    using (Bitmap frame = TurnFrame(size.Width, size.Height, 4))
                    {
                        Rectangle area = new Rectangle(Point.Empty, size);
                        Picture warm = Picture.Of(frame, area);
                        List<double> whole = new List<double>(), plain = new List<double>();
                        for (int round = 0; round < 5; round++)
                        {
                            Stopwatch encoding = Stopwatch.StartNew();
                            Picture.Of(frame, area);
                            whole.Add(encoding.Elapsed.TotalMilliseconds);
                            encoding = Stopwatch.StartNew();
                            Jpeg(frame, Computer.Longest);
                            plain.Add(encoding.Elapsed.TotalMilliseconds);
                        }
                        table.AppendLine(size.Width + "x" + size.Height + ": picture with its sharp copy " + TurnMs(TurnMedian(whole)) + " ms, the 1280 jpeg alone " + TurnMs(TurnMedian(plain)) + " ms | " + warm.Jpeg.Length + " | " + (warm.Sharp == null ? 0 : warm.Sharp.Length));
                    }
                using (Bitmap frame = TurnFrame(1920, 1080, 5))
                {
                    Picture read = Picture.Of(frame, new Rectangle(0, 0, 1920, 1080));
                    List<double> gridding = new List<double>();
                    string words = "";
                    List<ReadWord> spots = null;
                    for (int round = 0; round < 3; round++)
                    {
                        Stopwatch reading = Stopwatch.StartNew();
                        Computer.Gridded(read, out words, out spots);
                        gridding.Add(reading.Elapsed.TotalMilliseconds);
                    }
                    table.AppendLine("Grid and words for a grid brain, 1920x1080 (median of 3): " + TurnMs(TurnMedian(gridding)) + " ms, " + (spots == null ? 0 : spots.Count) + " words read");
                }
                try
                {
                    using (Bitmap shot = new Bitmap(1920, 1080, PixelFormat.Format24bppRgb))
                    using (Graphics into = Graphics.FromImage(shot))
                    {
                        List<double> copies = new List<double>();
                        for (int round = 0; round < 5; round++)
                        {
                            Stopwatch copying = Stopwatch.StartNew();
                            Native.CopyScreen(into, new Rectangle(0, 0, 1920, 1080));
                            copies.Add(copying.Elapsed.TotalMilliseconds);
                        }
                        table.AppendLine("A plain screen copy of 1920x1080 on this desktop (median of 5): " + TurnMs(TurnMedian(copies)) + " ms");
                    }
                }
                catch (System.ComponentModel.Win32Exception error) { table.AppendLine("A plain screen copy failed here: " + error.Message); }

                File.WriteAllText(Path.Combine(output, "game-speed.txt"), table.ToString());
                TurnTimes moves = rows[0], unchanged = rows[1], grid = rows[2];
                checks.Add("PASS her game turns are timed stage by stage with an instant brain: claude moves turn " + TurnMs(TurnMedian(moves.Turn)) + " ms (look " + TurnMs(TurnMedian(moves.Look)) + ", hands " + TurnMs(TurnMedian(moves.Act)) + "), unchanged screen sends " + TurnMs(TurnMedian(ToDoubles(unchanged.Pictures))) + " pictures a turn, grid brain turn " + TurnMs(TurnMedian(grid.Turn)) + " ms (grid and words " + TurnMs(TurnMedian(grid.Build)) + "); the table is in game-speed.txt");
            }
            finally
            {
                foreach (Bitmap frame in frames) if (frame != null) frame.Dispose();
                Computer.Grab = grabWas; Computer.Shoot = shootWas; Computer.Glimpse = glimpseWas; Computer.Look = lookWas;
                Computer.Front = frontWas; Hands.Foreground = foregroundWas; Hands.Active = activeWas; Computer.Alive = aliveWas; Computer.AboveHer = aboveWas;
                Hands.Steer = steerWas; Computer.SameWindow = sameWas; Computer.ClientArea = clientWas; Computer.TitleOf = titleWas; Computer.WindowAt = atWas;
                Computer.GrayFrame = grayWas; Computer.SendKey = keyWas; Computer.SendMouse = mouseWas; Computer.Nudge = nudgeWas;
                Computer.Track = trackWas; Computer.Tapped = tapWas; Computer.PutCursor = putWas; Computer.CursorAt = cursorWas;
                Computer.Lending = lendWas; InControl.Changed = signWas; Computer.CursorHidden = hiddenWas; Computer.Escape = escapeWas; Computer.SecretFocus = secretWas;
                Computer.GameUnder = gameUnderWas; Computer.Under = underWas; Computer.Focus = focusWas; Computer.GameOpen = gameOpenWas; Hands.Windows = windowsWas;
                Computer.Use = useWas; Computer.Act = actWas; Computer.Halted = haltWas; Hands.Run = runWas; Computer.Shown = shownWas;
                GameNotes.Folder = notesWas; Computer.Flinched = flinchedWas; Computer.Streaming = streamWas; Sfx.Level = soundWas;
                Computer.Finish();
                Computer.Arm();
                try { Directory.Delete(notes, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static List<double> ToDoubles(List<int> values)
        {
            List<double> made = new List<double>();
            foreach (int value in values) made.Add(value);
            return made;
        }
    }
}
