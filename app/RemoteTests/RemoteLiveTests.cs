using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Threading;

namespace CapriDesktop
{
    // `RemoteTests live`: the real RemoteScreen and RemoteCall (app/RemoteScreen.cs, app/RemoteCall.cs) on the real
    // RemoteLink, with a drawn desktop instead of his monitors, stand-in ears instead of hers and a tone instead of her
    // voice. Nothing here captures a screen or opens a mic or speaker. app/RemoteTests/remote-live-e2e.mjs drives it.
    internal static partial class RemoteTests
    {
        private sealed class LiveRig : IDisposable
        {
            internal readonly FakeDesktop Desktop = new FakeDesktop();
            internal readonly RemoteScreen Screen;
            internal readonly RemoteCall Call;
            private readonly Action<RemoteJson.Writer> say;
            private bool allowWatch = true, allowCall = true;

            internal LiveRig(RemoteLink link, Action<RemoteJson.Writer> say)
            {
                this.say = say;
                Screen = new RemoteScreen(link, Desktop);
                Screen.Allowed = delegate { return allowWatch; };
                Call = new RemoteCall(link, Ears, delegate (string device, string id, string text) { say(new RemoteJson.Writer().Text("e", "heard").Text("device", device).Text("id", id).Text("text", text)); });
                Call.Allowed = delegate { return allowCall; };
                Call.VoiceOn = delegate { return true; };
                Call.EarsOn = delegate { return true; };
                Screen.Changed += delegate { say(Watchers("watchers", Screen.Watchers())); };
                Call.Changed += delegate { say(Watchers("callers", Call.Callers())); };
            }

            private static RemoteJson.Writer Watchers(string what, List<RemoteWatcher> list)
            {
                List<string> shown = new List<string>();
                foreach (RemoteWatcher one in list) shown.Add(one.Device + "@" + (one.Monitor == null ? "" : one.Monitor.Id) + "@" + (one.Name ?? ""));
                shown.Sort(StringComparer.Ordinal);
                return new RemoteJson.Writer().Text("e", what).Texts("list", shown);
            }

            // Stand-in ears: silence is nothing heard, anything else the same few words. The samples' count and FNV-1a hash
            // go out first, so the test can tell every byte arrived.
            private string Ears(short[] samples)
            {
                uint hash = 2166136261;
                bool sound = false;
                foreach (short sample in samples)
                {
                    hash = (hash ^ (uint)(sample & 0xff)) * 16777619;
                    hash = (hash ^ (uint)((sample >> 8) & 0xff)) * 16777619;
                    if (sample != 0) sound = true;
                }
                say(new RemoteJson.Writer().Text("e", "ears").Number("samples", samples.Length).Text("hash", hash.ToString("x8", CultureInfo.InvariantCulture)));
                return sound ? "testing one two three" : "";
            }

            internal bool Command(RemoteJson.Map command)
            {
                double x, y;
                switch (command.Text("c"))
                {
                    case "scene":
                        lock (Desktop.Gate) Desktop.Scene = command.Text("name") ?? "typing";
                        return true;
                    case "look":
                        lock (Desktop.Gate) Desktop.LookingAt = command.Text("monitor");
                        return true;
                    case "mouse":
                        if (command.Number("x", out x) && command.Number("y", out y)) lock (Desktop.Gate) Desktop.MouseAt = new Point((int)x, (int)y);
                        return true;
                    case "monitors":
                    {
                        List<RemoteMonitor> list = new List<RemoteMonitor>();
                        foreach (object item in command.Items("list") ?? new List<object>())
                        {
                            RemoteJson.Map one = item as RemoteJson.Map;
                            double left, top, wide, tall;
                            if (one == null || !one.Number("x", out left) || !one.Number("y", out top) || !one.Number("w", out wide) || !one.Number("h", out tall)) continue;
                            list.Add(new RemoteMonitor { Id = one.Text("id"), Name = "Monitor " + one.Text("id"), Device = @"\\.\DISPLAY" + one.Text("id"), Bounds = new Rectangle((int)left, (int)top, (int)wide, (int)tall), Primary = one.Is("primary", true) });
                        }
                        lock (Desktop.Gate) Desktop.List = list;
                        return true;
                    }
                    case "let":
                        allowWatch = !command.Is("watch", false);
                        allowCall = !command.Is("call", false);
                        return true;
                    case "stats":
                    {
                        RemoteScreenStats stats = Screen.Stats();
                        long[] call = Call.Counts();
                        say(new RemoteJson.Writer().Text("e", "stats").Number("frames", stats.Frames).Number("parts", stats.Parts).Number("jpeg", stats.Jpeg).Number("wire", stats.Wire).Number("cursors", stats.Cursors).Number("unchanged", stats.Unchanged).Number("failed", stats.Failed)
                            .Number("heard", call[0]).Number("voiceParts", call[1]).Number("voiceWire", call[2]).Number("audioIn", call[3]).Number("captures", Desktop.Captures));
                        return true;
                    }
                    case "voice":
                    {
                        // A tone as her voice: all at once, as a whole line from her voice comes, or in 20 ms pieces in time.
                        double seconds, rate, hz;
                        if (!command.Number("seconds", out seconds)) seconds = 1;
                        if (!command.Number("rate", out rate)) rate = 24000;
                        if (!command.Number("hz", out hz)) hz = 330;
                        short[] tone = Tone((int)rate, seconds, hz);
                        bool paced = command.Is("paced", true);
                        if (!paced) Call.Voice(tone, (int)rate);
                        else
                        {
                            int piece = (int)rate / 50;
                            for (int at = 0; at < tone.Length; at += piece)
                            {
                                short[] part = new short[Math.Min(piece, tone.Length - at)];
                                Array.Copy(tone, at, part, 0, part.Length);
                                Call.Voice(part, (int)rate);
                                Thread.Sleep(20);
                            }
                        }
                        say(new RemoteJson.Writer().Text("e", "voiced").Number("samples", tone.Length));
                        return true;
                    }
                    case "hush":
                        Call.Hush();
                        return true;
                    case "hang-up":
                        Call.End(command.Text("device"), "ended");
                        return true;
                    default:
                        return false;
                }
            }

            public void Dispose()
            {
                Screen.Dispose();
                Call.Dispose();
                Desktop.Dispose();
            }
        }

        internal static short[] Tone(int rate, double seconds, double hz)
        {
            short[] samples = new short[(int)(rate * seconds)];
            for (int i = 0; i < samples.Length; i++) samples[i] = (short)(Math.Sin(2 * Math.PI * hz * i / rate) * 9000);
            return samples;
        }

        // Two monitors side by side, each drawn as a desktop: a code editor and a browser on a wallpaper, the monitor's number
        // in big letters and a taskbar with a clock. The scene says what moves: nothing (still), a line being typed (typing),
        // the editor scrolling (scroll), or a video playing in the browser (video).
        internal sealed class FakeDesktop : RemoteScreenSource, IDisposable
        {
            internal readonly object Gate = new object();
            internal List<RemoteMonitor> List = new List<RemoteMonitor>
            {
                new RemoteMonitor { Id = "1", Name = "Monitor 1", Device = @"\\.\DISPLAY1", Bounds = new Rectangle(0, 0, 1920, 1080), Primary = true },
                new RemoteMonitor { Id = "2", Name = "Monitor 2", Device = @"\\.\DISPLAY2", Bounds = new Rectangle(1920, 0, 2560, 1440) },
            };
            internal string LookingAt, Scene = "typing";
            internal Point MouseAt = new Point(960, 540);
            internal int Captures;
            private readonly Dictionary<string, Bitmap> walls = new Dictionary<string, Bitmap>(StringComparer.Ordinal);

            internal override List<RemoteMonitor> Monitors() { lock (Gate) return new List<RemoteMonitor>(List); }

            internal override string Looking() { lock (Gate) return LookingAt; }

            internal override Point Mouse() { lock (Gate) return MouseAt; }

            internal override RemoteCapture Capture() { return new FakeCapture(this); }

            // The still part of a monitor's desktop, drawn once at its full size.
            internal Bitmap Wall(RemoteMonitor monitor)
            {
                string key = monitor.Id + " " + monitor.Bounds.Width + "x" + monitor.Bounds.Height;
                Bitmap wall;
                lock (Gate)
                {
                    if (walls.TryGetValue(key, out wall)) return wall;
                    wall = DrawWall(monitor);
                    walls[key] = wall;
                    return wall;
                }
            }

            private static readonly string[] Code =
            {
                "internal sealed class RemoteScreen : IDisposable", "{", "    // Nothing goes while the screen doesn't change.", "    internal const int PartBytes = 44000;",
                "    private readonly Dictionary<string, Watch> watches = new Dictionary<string, Watch>();", "    internal event Action Changed;", "",
                "    private void Step(Watch watch)", "    {", "        long now = Now();", "        if (watch.N - watch.Acked >= Ahead) return;",
                "        Bitmap frame = watch.Capture.Next(width);", "        if (frame == null) { stats.Unchanged++; return; }", "        byte[] jpeg = Jpeg(frame, quality.Jpeg);",
                "        List<string> parts = FrameMessages(n, wide, tall, monitor, cx, cy, jpeg);", "        link.SendLive(watch.Device, parts, delegate { wake.Set(); });", "    }", "}",
            };

            private static readonly string[] Page =
            {
                "Capri Remote: watch your PC from your phone", "Pick a monitor, or Auto to follow what she's looking at.", "Frames are JPEG, a few a second, sealed end to end.",
                "Nothing is sent while the screen stays the same.", "Talk to her with the screen in a mini player over the chat.", "Her own ears hear you, and her real voice answers.",
            };

            private static Bitmap DrawWall(RemoteMonitor monitor)
            {
                int wide = monitor.Bounds.Width, tall = monitor.Bounds.Height;
                float s = tall / 1080f;
                Bitmap wall = new Bitmap(wide, tall, PixelFormat.Format32bppRgb);
                using (Graphics g = Graphics.FromImage(wall))
                using (Font mono = new Font("Consolas", 15 * s, GraphicsUnit.Pixel))
                using (Font ui = new Font("Segoe UI", 16 * s, GraphicsUnit.Pixel))
                using (Font title = new Font("Segoe UI", 28 * s, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font huge = new Font("Segoe UI", 140 * s, FontStyle.Bold, GraphicsUnit.Pixel))
                {
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                    using (LinearGradientBrush sky = new LinearGradientBrush(new Rectangle(0, 0, wide, tall), Color.FromArgb(54, 14, 26), Color.FromArgb(9, 30, 33), 35f)) g.FillRectangle(sky, 0, 0, wide, tall);
                    Random dots = new Random(monitor.Id.GetHashCode());
                    for (int i = 0; i < 160; i++)
                        using (Brush dot = new SolidBrush(Color.FromArgb(40 + dots.Next(80), 94, 240, 246))) g.FillEllipse(dot, dots.Next(wide), dots.Next(tall), 3 * s, 3 * s);
                    using (Brush number = new SolidBrush(Color.FromArgb(70, 255, 93, 102))) g.DrawString(monitor.Id, huge, number, wide - 190 * s, tall - 300 * s);
                    // A code editor on the left half.
                    Rectangle editor = new Rectangle((int)(40 * s), (int)(40 * s), wide / 2 - (int)(60 * s), tall - (int)(140 * s));
                    using (Brush back = new SolidBrush(Color.FromArgb(30, 30, 36))) g.FillRectangle(back, editor);
                    using (Brush bar = new SolidBrush(Color.FromArgb(50, 50, 60))) g.FillRectangle(bar, editor.X, editor.Y, editor.Width, 34 * s);
                    using (Brush light = new SolidBrush(Color.FromArgb(220, 220, 225))) g.DrawString("RemoteScreen.cs - Capri Desktop", ui, light, editor.X + 12 * s, editor.Y + 6 * s);
                    Brush[] inks = { new SolidBrush(Color.FromArgb(156, 220, 254)), new SolidBrush(Color.FromArgb(206, 145, 120)), new SolidBrush(Color.FromArgb(106, 153, 85)), new SolidBrush(Color.FromArgb(220, 220, 170)), new SolidBrush(Color.FromArgb(197, 134, 192)) };
                    float line = 22 * s;
                    for (int row = 0; editor.Y + 50 * s + row * line < editor.Bottom - line; row++)
                    {
                        string text = Code[row % Code.Length];
                        g.DrawString((row + 1).ToString(CultureInfo.InvariantCulture).PadLeft(4), mono, Brushes.Gray, editor.X + 4 * s, editor.Y + 50 * s + row * line);
                        g.DrawString(text, mono, inks[(row * 7 + text.Length) % inks.Length], editor.X + 60 * s, editor.Y + 50 * s + row * line);
                    }
                    foreach (Brush ink in inks) ink.Dispose();
                    // A browser on the right half, with a picture in it.
                    Rectangle browser = new Rectangle(wide / 2 + (int)(20 * s), (int)(40 * s), wide / 2 - (int)(60 * s), tall - (int)(140 * s));
                    g.FillRectangle(Brushes.White, browser);
                    using (Brush bar = new SolidBrush(Color.FromArgb(222, 225, 230))) g.FillRectangle(bar, browser.X, browser.Y, browser.Width, 44 * s);
                    using (Brush field = new SolidBrush(Color.FromArgb(250, 250, 252))) g.FillRectangle(field, browser.X + 90 * s, browser.Y + 8 * s, browser.Width - 180 * s, 28 * s);
                    g.DrawString("https://capriai.app/remote/", ui, Brushes.DimGray, browser.X + 100 * s, browser.Y + 11 * s);
                    g.DrawString("Capri Remote", title, Brushes.Black, browser.X + 30 * s, browser.Y + 70 * s);
                    for (int row = 0; row < 18; row++) g.DrawString(Page[row % Page.Length], ui, Brushes.Black, browser.X + 30 * s, browser.Y + 120 * s + row * 26 * s);
                    // The taskbar.
                    using (Brush task = new SolidBrush(Color.FromArgb(32, 32, 32))) g.FillRectangle(task, 0, tall - 48 * s, wide, 48 * s);
                    for (int i = 0; i < 9; i++)
                        using (Brush icon = new SolidBrush(Color.FromArgb(80 + i * 15, 120, 200 - i * 10))) g.FillRectangle(icon, wide / 2 - 220 * s + i * 50 * s, tall - 40 * s, 32 * s, 32 * s);
                }
                return wall;
            }

            // The browser's picture area, where the video plays.
            internal static Rectangle VideoArea(Rectangle bounds)
            {
                float s = bounds.Height / 1080f;
                int left = bounds.Width / 2 + (int)(50 * s), top = (int)(600 * s);
                int wide = bounds.Width / 2 - (int)(120 * s);
                return new Rectangle(left, top, wide, Math.Min(wide * 9 / 16, bounds.Height - top - (int)(120 * s)));
            }

            public void Dispose()
            {
                lock (Gate)
                {
                    foreach (Bitmap wall in walls.Values) wall.Dispose();
                    walls.Clear();
                }
            }
        }

        internal sealed class FakeCapture : RemoteCapture
        {
            private readonly FakeDesktop desktop;
            private RemoteMonitor monitor;
            private int step, lastWidth;
            private string lastScene;
            private bool any;

            internal FakeCapture(FakeDesktop desktop) { this.desktop = desktop; }

            internal override bool Open(RemoteMonitor monitor)
            {
                this.monitor = monitor;
                any = false;
                return true;
            }

            internal override Bitmap Next(int maxWidth)
            {
                if (monitor == null) return null;
                string scene;
                lock (desktop.Gate) scene = desktop.Scene;
                if (any && scene == "still" && lastScene == "still" && maxWidth == lastWidth) return null;
                any = true;
                lastWidth = maxWidth;
                lastScene = scene;
                step++;
                lock (desktop.Gate) desktop.Captures++;
                Rectangle bounds = new Rectangle(Point.Empty, monitor.Bounds.Size);
                float s = bounds.Height / 1080f;
                using (Bitmap full = new Bitmap(desktop.Wall(monitor)))
                {
                    using (Graphics g = Graphics.FromImage(full))
                    using (Font mono = new Font("Consolas", 15 * s, GraphicsUnit.Pixel))
                    using (Font ui = new Font("Segoe UI", 14 * s, GraphicsUnit.Pixel))
                    {
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                        // The clock always moves on, a second at a time.
                        using (Brush task = new SolidBrush(Color.FromArgb(32, 32, 32))) g.FillRectangle(task, bounds.Width - 120 * s, bounds.Height - 46 * s, 116 * s, 44 * s);
                        if (scene != "still") g.DrawString(new DateTime(2026, 11, 9, 2, 0, 0).AddSeconds(step / 3).ToString("h:mm:ss tt", CultureInfo.InvariantCulture), ui, Brushes.White, bounds.Width - 112 * s, bounds.Height - 36 * s);
                        if (scene == "typing")
                        {
                            string typed = "        // typed on the PC, " + new string('x', step % 60);
                            using (Brush back = new SolidBrush(Color.FromArgb(30, 30, 36))) g.FillRectangle(back, 100 * s, 50 * s + 40 + 18 * 22 * s, bounds.Width / 2 - 160 * s, 22 * s);
                            g.DrawString(typed, mono, Brushes.Gainsboro, 100 * s, 50 * s + 40 + 18 * 22 * s);
                        }
                        else if (scene == "scroll")
                        {
                            Rectangle editor = new Rectangle((int)(40 * s), (int)(84 * s), bounds.Width / 2 - (int)(60 * s), bounds.Height - (int)(190 * s));
                            int shift = (int)(step * 3 * 22 * s) % Math.Max(1, editor.Height);
                            using (Bitmap copy = new Bitmap(desktop.Wall(monitor)))
                                g.DrawImage(copy, new Rectangle(editor.X, editor.Y, editor.Width, editor.Height - shift), new Rectangle(editor.X, editor.Y + shift, editor.Width, editor.Height - shift), GraphicsUnit.Pixel);
                        }
                        else if (scene == "video")
                        {
                            Rectangle area = FakeDesktop.VideoArea(bounds);
                            double t = step / 3.0;
                            using (LinearGradientBrush sky = new LinearGradientBrush(area, Color.FromArgb(40 + (int)(40 * Math.Sin(t)), 80, 160), Color.FromArgb(200, 120 + (int)(60 * Math.Cos(t * 0.7)), 60), (float)(t * 20 % 360))) g.FillRectangle(sky, area);
                            for (int i = 0; i < 14; i++)
                            {
                                double phase = t * (0.6 + i * 0.13) + i;
                                int size = (int)(area.Height * (0.12 + 0.05 * (i % 4)));
                                int x = area.X + (int)((Math.Sin(phase) * 0.5 + 0.5) * (area.Width - size));
                                int y = area.Y + (int)((Math.Cos(phase * 1.3) * 0.5 + 0.5) * (area.Height - size));
                                using (Brush blob = new SolidBrush(Color.FromArgb(190, (i * 70) % 256, (i * 130 + step * 9) % 256, (255 - i * 40 + 1024) % 256))) g.FillEllipse(blob, x, y, size, size);
                            }
                        }
                    }
                    // GDI+ can't lock a bitmap's bits while a Graphics is drawing on it, so the grain goes on after.
                    if (scene == "video") Grain(full, FakeDesktop.VideoArea(bounds), new Random(step));
                    int wide = Math.Min(maxWidth, bounds.Width), tall = Math.Max(1, (int)((long)bounds.Height * wide / bounds.Width));
                    Bitmap shown = new Bitmap(wide, tall, PixelFormat.Format32bppRgb);
                    using (Graphics g = Graphics.FromImage(shown))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(full, new Rectangle(0, 0, wide, tall));
                    }
                    return shown;
                }
            }

            // A film's grain over the video, so it compresses like a picture and not like flat paint.
            private static void Grain(Bitmap bitmap, Rectangle area, Random random)
            {
                BitmapData data = bitmap.LockBits(area, ImageLockMode.ReadWrite, PixelFormat.Format32bppRgb);
                try
                {
                    byte[] row = new byte[area.Width * 4];
                    for (int y = 0; y < area.Height; y++)
                    {
                        IntPtr at = IntPtr.Add(data.Scan0, y * data.Stride);
                        System.Runtime.InteropServices.Marshal.Copy(at, row, 0, row.Length);
                        for (int i = 0; i < row.Length; i += 4)
                        {
                            int grain = random.Next(-14, 15);
                            row[i] = (byte)Math.Max(0, Math.Min(255, row[i] + grain));
                            row[i + 1] = (byte)Math.Max(0, Math.Min(255, row[i + 1] + grain));
                            row[i + 2] = (byte)Math.Max(0, Math.Min(255, row[i + 2] + grain));
                        }
                        System.Runtime.InteropServices.Marshal.Copy(row, 0, at, row.Length);
                    }
                }
                finally { bitmap.UnlockBits(data); }
            }

            public override void Dispose() { monitor = null; }
        }
    }
}
