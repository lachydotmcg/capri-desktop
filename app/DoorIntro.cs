using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her door, from YunoTheAlien's Door Intro 1.5.0 (19 Nov). On her first start after an update, a door of red alert hexagons
    // builds from the middle out where she lands while little alert screens pop up on every monitor, its middle opens, she is
    // asleep in it, the door fades and she wakes and stands where she'll stay. Once per version, and only when "Glitch in at
    // startup" and motion are on, the same as every intro. Her own sleep, wake and idle strips, so a skin's art is the art.
    //
    // Yuno's pop-ups showed three anime screenshots; these are drawn panels instead, so nothing in her download is someone
    // else's picture. A sound in her folder's sounds\intro plays with it, as in Yuno's pack, and without one the door is quiet.
    internal sealed class DoorIntro : ICapriIntro
    {
        internal const double Length = 12.6;
        // What her settings remember once the door has played, so it plays again only when her version changes.
        internal static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);

        // The door's shape: a sideways pill with a larger circle through its middle that bulges above and below it, and an
        // opening in that circle where she comes through. Sizes are shares of her height.
        private const float Opening = 0.67f, Circle = 0.90f, PillTall = 0.99f, PillWide = 3.39f;
        // A hexagon fits in a square no larger than this, in pixels.
        private const float LargestHex = 60f;
        private static readonly Color Alert = Color.FromArgb(255, 30, 38);
        private static readonly string[] Alerts =
        {
            "DANGER", "EMERGENCY", "WARNING", "ALERT", "LEVEL 1", "LEVEL 2", "LEVEL 3", "CODE RED", "LOCKED",
            "SEALED", "STANDBY", "SIGNAL", "BREACH", "KEYCODE", "REFUSED", "NO ENTRY", "HOLD",
            "SECURE", "OVERRIDE", "PRIORITY", "CONTAIN", "SECTOR A", "SECTOR B", "SECTOR C", "GATE 4"
        };
        // Seconds: the door builds from the middle out, its middle opens, she sleeps in it, it fades, she wakes.
        private const double Built = 8.0, Opened = 9.2, Asleep = 8.6, Fades = 10.4, Wakes = 11.4, Settles = 12.25;
        private const int PopupsPerScreen = 9;

        private sealed class Hex
        {
            internal PointF Centre;
            internal double Born, Gone;
            internal bool Inner, Solid;
            // A word or a triangle on some, and a flash of its own on most.
            internal string Said;
            internal bool Arrow, Hard;
            internal double Rate, Phase;
        }

        private readonly Clip sleep, wake, idle;
        private readonly string sounds;
        private readonly bool popupsShown;
        private readonly List<Hex> door = new List<Hex>();
        private float doorReach;
        private Font tag;
        private readonly StringFormat middle = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        private readonly List<Popup> popups = new List<Popup>();
        private readonly List<Bitmap> popupArt = new List<Bitmap>();
        private System.Windows.Forms.Timer popupBeat;
        private readonly Stopwatch popupClock = new Stopwatch();

        // sounds is her sounds\intro folder, or null for none. The smoke test draws it with no pop-ups, since they're windows.
        internal DoorIntro(Clip sleeping, Clip waking, Clip idling, string soundFolder, bool popupWindows)
        {
            sleep = sleeping;
            wake = waking;
            idle = idling;
            sounds = soundFolder;
            popupsShown = popupWindows;
        }

        public double Seconds { get { return Length; } }

        public void Begin(Bitmap desktop, Rectangle screen, Rectangle her)
        {
            MakeDoor(her);
            DoorSound();
            if (popupsShown) StartPopups(screen, her);
        }

        public void Draw(Graphics g, Rectangle screen, Rectangle her, double at)
        {
            g.CompositingQuality = CompositingQuality.HighSpeed;
            g.PixelOffsetMode = PixelOffsetMode.HighSpeed;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.Bilinear;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            Door(g, her, at);
        }

        internal void Stop()
        {
            EndPopups();
            foreach (Bitmap art in popupArt) art.Dispose();
            popupArt.Clear();
            if (tag != null) { tag.Dispose(); tag = null; }
        }

        // The hexagons sit on their own grid, centred on her, never moved and never overlapping. One belongs to the door when its
        // centre is inside the shape, and to the opening when it is inside the opening's circle, so both edges step the way
        // hexagons do rather than run smooth.
        private void MakeDoor(Rectangle her)
        {
            door.Clear();
            float tall = Math.Max(60f, her.Height - 6);
            float open = tall * Opening, circle = tall * Circle, pillHalf = tall * PillTall / 2, pillReach = tall * PillWide / 2;
            float cx = her.X + her.Width / 2f, cy = her.Y + her.Height / 2f;
            // Flat-topped, so a hexagon is twice its reach wide: that width fits the square, and it is the larger side.
            float reach = Math.Max(12f, Math.Min(LargestHex, pillReach * 2 / 15f)) / 2f;
            doorReach = reach;
            // As large as the longest word lets it be and still fit across a hexagon.
            if (tag != null) tag.Dispose();
            tag = new Font("Consolas", Math.Max(6f, Math.Min(reach * 0.3f, reach * 1.5f / (9 * 0.56f))), FontStyle.Bold, GraphicsUnit.Pixel);
            float stepX = reach * 1.5f, stepY = (float)(Math.Sqrt(3) * reach);
            int cols = (int)Math.Ceiling(pillReach / stepX) + 1, rows = (int)Math.Ceiling(circle / stepY) + 1;
            Random seed = new Random(7);
            for (int col = -cols; col <= cols; col++)
                for (int row = -rows; row <= rows; row++)
                {
                    float x = cx + col * stepX, y = cy + row * stepY + ((col & 1) != 0 ? stepY / 2 : 0);
                    float dx = x - cx, dy = y - cy;
                    double fromMiddle = Math.Sqrt(dx * dx + dy * dy);
                    if (fromMiddle > circle && !InPill(dx, dy, pillReach, pillHalf)) continue;
                    // How far out from the middle, so the door builds outwards and its opening clears the same way.
                    double far = Math.Min(1, Math.Sqrt(Math.Pow(dx / pillReach, 2) + Math.Pow(dy / circle, 2)) / Math.Sqrt(2));
                    Hex one = new Hex();
                    one.Centre = new PointF(x, y);
                    one.Born = 0.15 + far * (Built - 0.85) + seed.NextDouble() * 0.25;
                    one.Inner = fromMiddle <= open;
                    one.Gone = one.Inner ? Built + 0.1 + fromMiddle / open * (Opened - Built - 0.5) : double.MaxValue;
                    one.Solid = seed.NextDouble() < 0.12;
                    one.Arrow = seed.Next(7) == 0;
                    one.Said = one.Arrow ? null : seed.Next(100) < 42 ? Alerts[seed.Next(Alerts.Length)] : null;
                    // Three in four flash, out of step with each other and under one flash a second.
                    bool flashes = seed.Next(4) != 0;
                    one.Rate = flashes ? 0.7 + seed.NextDouble() * 3.4 : 0;
                    one.Phase = seed.NextDouble() * Math.PI * 2;
                    one.Hard = flashes && seed.Next(2) == 0;
                    door.Add(one);
                }
        }

        // A pill lying on its side: a band with a round cap at each end.
        private static bool InPill(float dx, float dy, float reach, float half)
        {
            float straight = reach - half;
            if (Math.Abs(dy) > half) return false;
            if (Math.Abs(dx) <= straight) return true;
            float ex = Math.Abs(dx) - straight;
            return ex * ex + dy * dy <= half * half;
        }

        private static double Clamp(double value) { return value < 0 ? 0 : value > 1 ? 1 : value; }

        // Black lands only where a hexagon does, so the rest of the screen stays the desktop. The middle ones lose their colour
        // and keep their black, which is the opening; then the door and its dark fade together.
        private void Door(Graphics g, Rectangle her, double at)
        {
            double scene = 1 - Clamp((at - Fades) / 1.0);
            if (scene > 0.003)
            {
                PointF[] shape = new PointF[6];
                using (SolidBrush dark = new SolidBrush(Color.Black))
                using (SolidBrush fill = new SolidBrush(Alert))
                using (SolidBrush knocked = new SolidBrush(Color.Black))
                using (Pen line = new Pen(Alert, Math.Max(1.5f, doorReach * 0.08f)))
                {
                    foreach (Hex hex in door)
                    {
                        double shown = Clamp((at - hex.Born) / 0.45) * scene;
                        if (shown <= 0.01) continue;
                        // The black closes up, so the opening is one dark rather than a grid; only the colour keeps its gaps.
                        Corners(shape, hex.Centre, doorReach * 1.03f);
                        dark.Color = Color.FromArgb((int)(255 * shown), 0, 0, 0);
                        g.FillPolygon(dark, shape);
                    }
                    foreach (Hex hex in door)
                    {
                        double shown = Clamp((at - hex.Born) / 0.45) * scene;
                        if (hex.Inner) shown *= 1 - Clamp((at - hex.Gone) / 0.4);
                        shown *= Beat(hex, at);
                        if (shown <= 0.01) continue;
                        Corners(shape, hex.Centre, doorReach * 0.86f);
                        int alpha = (int)(255 * shown);
                        fill.Color = Color.FromArgb(alpha, Alert);
                        if (hex.Solid) g.FillPolygon(fill, shape);
                        else { line.Color = Color.FromArgb(alpha, Alert); g.DrawPolygon(line, shape); }
                        // Knocked out of a solid one, in the colour on an outline.
                        Brush over = hex.Solid ? (Brush)knocked : fill;
                        if (hex.Solid) knocked.Color = Color.FromArgb(alpha, 0, 0, 0);
                        float cx = hex.Centre.X, cy = hex.Centre.Y;
                        if (hex.Arrow)
                        {
                            float arm = doorReach * 0.26f;
                            g.FillPolygon(over, new[] { new PointF(cx, cy + arm * 0.7f), new PointF(cx - arm, cy - arm * 0.6f), new PointF(cx + arm, cy - arm * 0.6f) });
                        }
                        else if (hex.Said != null && tag != null)
                            g.DrawString(hex.Said, tag, over, new RectangleF(cx - doorReach * 0.85f, cy - doorReach * 0.3f, doorReach * 1.7f, doorReach * 0.6f), middle);
                    }
                }
            }
            // Asleep in the opening, waking as the door goes, then standing as she will once the intro hands over: her own
            // strips, placed the way she draws them, so the last frame is where she already stands.
            float size = Math.Max(40f, (her.Height - 6) / idle.Height);
            float midX = her.X + her.Width / 2f - idle.ShiftX * size, midY = her.Y + her.Height / 2f;
            double asleep = Clamp((at - Asleep) / 1.0);
            double awake = Clamp((at - Wakes) / 0.15);
            double settled = Clamp((at - Settles) / 0.35);
            Frame(g, sleep, (int)(Math.Max(0, at - Asleep) * sleep.Fps) % sleep.Count, size, midX, midY, asleep * (1 - awake));
            Frame(g, wake, (int)(Math.Max(0, at - Wakes) * wake.Fps), size, midX, midY, awake * (1 - settled));
            Frame(g, idle, (int)(Math.Max(0, at - Settles) * idle.Fps) % idle.Count, size, midX, midY, settled);
        }

        // A hard one snaps between on and nearly off, a soft one swells, a steady one sits at full.
        private static double Beat(Hex hex, double at)
        {
            if (hex.Rate <= 0) return 1;
            double turn = Math.Sin(at * hex.Rate + hex.Phase);
            return hex.Hard ? (turn > 0 ? 1 : 0.12) : 0.3 + 0.7 * (0.5 + 0.5 * turn);
        }

        private static void Corners(PointF[] shape, PointF centre, float reach)
        {
            for (int corner = 0; corner < 6; corner++)
            {
                double turn = Math.PI / 3 * corner;
                shape[corner] = new PointF(centre.X + (float)(reach * Math.Cos(turn)), centre.Y + (float)(reach * Math.Sin(turn)));
            }
        }

        // One frame of one of her strips, its own height times her size tall, centred on her middle with its own nudge.
        private static void Frame(Graphics g, Clip clip, int index, float size, float midX, float midY, double shown)
        {
            if (shown <= 0.01 || clip.Count <= 0) return;
            Bitmap strip = clip.Strip;
            if (strip == null) return;
            int width = strip.Width / clip.Count;
            float tall = size * clip.Height, wide = tall * width / strip.Height;
            Rectangle spot = Rectangle.Round(new RectangleF(midX - wide / 2 + clip.ShiftX * size, midY - tall / 2, wide, tall));
            using (ImageAttributes faded = new ImageAttributes())
            {
                ColorMatrix matrix = new ColorMatrix();
                matrix.Matrix33 = (float)Math.Min(1, shown);
                faded.SetColorMatrix(matrix);
                g.DrawImage(strip, spot, Math.Max(0, Math.Min(clip.Count - 1, index)) * width, 0, width, strip.Height, GraphicsUnit.Pixel, faded);
            }
        }

        // One file at random from her sounds\intro folder, quietly. Drop more in and they join the pick.
        private void DoorSound()
        {
            if (sounds == null) return;
            try
            {
                if (!Directory.Exists(sounds)) return;
                List<string> found = new List<string>();
                foreach (string file in Directory.GetFiles(sounds, "*.wav")) found.Add(file);
                if (found.Count == 0) return;
                short[] samples = ModSound.ReadWave(found[new Random().Next(found.Count)], 0.35);
                Action<short[]> output = Sfx.Out;
                if (samples != null && output != null && Sfx.Level > 0) output(samples);
            }
            catch (Exception error) { Brain.Log("door intro", -1, error.Message); }
        }

        // ---- Pop-ups: little alert screens landing all over every monitor while the door builds, flashing, then all closing as
        // it opens. On top, never taking focus, and clicks pass straight through to whatever is under them.
        private sealed class Popup : Form
        {
            internal double Born, Closes, Rate, Phase;
            internal bool Hard, Open;
            private readonly Bitmap art;
            private readonly string title;

            internal Popup(Bitmap picture, Rectangle at, string word)
            {
                art = picture;
                title = word;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                Bounds = at;
                TopMost = true;
                BackColor = Color.Black;
                DoubleBuffered = true;
                Opacity = 0.99;
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x00000080 | 0x08000000 | 0x00000020 | 0x00000008;
                    return value;
                }
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            // A small window: a title bar with the word and a close mark, the picture under it, a red frame round both.
            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                int bar = Math.Max(14, Height / 9);
                g.Clear(Color.Black);
                g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                if (art != null) g.DrawImage(art, new Rectangle(2, bar, Width - 4, Height - bar - 2));
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (Font font = new Font("Consolas", Math.Max(8f, bar * 0.62f), FontStyle.Bold, GraphicsUnit.Pixel))
                using (SolidBrush red = new SolidBrush(Alert))
                {
                    g.DrawString(title, font, red, 5, (bar - font.Height) / 2f);
                    g.DrawString("x", font, red, Width - bar, (bar - font.Height) / 2f);
                }
                using (Pen frame = new Pen(Alert, 2)) g.DrawRectangle(frame, 1, 1, Width - 2, Height - 2);
                using (Pen under = new Pen(Alert, 1)) g.DrawLine(under, 2, bar - 1, Width - 3, bar - 1);
            }
        }

        // Every screen gets its share, at times that crowd in faster as the door nears done. On the door's own screen they keep
        // off the door. screen is the intro's own screen, whose top left is (0, 0), so her spot is moved onto the desktop first.
        private void StartPopups(Rectangle screen, Rectangle her)
        {
            EndPopups();
            try
            {
                if (popupArt.Count == 0) foreach (string word in new[] { "ALERT", "DANGER", "WARNING" }) popupArt.Add(Panel(word));
                Rectangle doorScreen = DoorScreen(screen);
                float tall = Math.Max(60f, her.Height - 6);
                float doorWide = tall * PillWide * 1.1f, doorTall = tall * Circle * 2.2f;
                Rectangle keepOff = Rectangle.Round(new RectangleF(doorScreen.X + her.X + her.Width / 2f - doorWide / 2, doorScreen.Y + her.Y + her.Height / 2f - doorTall / 2, doorWide, doorTall));
                Screen[] screens = Screen.AllScreens;
                Random luck = new Random();
                int many = PopupsPerScreen * screens.Length;
                List<double> times = new List<double>();
                for (int one = 0; one < many; one++) times.Add(0.4 + (Built - 0.8) * Math.Sqrt(luck.NextDouble()));
                times.Sort();
                for (int one = 0; one < many; one++)
                {
                    Rectangle area = screens[one % screens.Length].WorkingArea;
                    Bitmap art = popupArt[luck.Next(popupArt.Count)];
                    int wide = (int)(area.Width * (0.11 + luck.NextDouble() * 0.1));
                    int bar = Math.Max(14, wide / 14);
                    int high = (int)(wide * (double)art.Height / art.Width) + bar + 2;
                    Rectangle at = Rectangle.Empty;
                    for (int tries = 0; tries < 24; tries++)
                    {
                        at = new Rectangle(area.X + luck.Next(Math.Max(1, area.Width - wide)), area.Y + luck.Next(Math.Max(1, area.Height - high)), wide, high);
                        if (!at.IntersectsWith(keepOff)) break;
                    }
                    if (at.IntersectsWith(keepOff)) continue;
                    Popup popup = new Popup(art, at, Alerts[luck.Next(Alerts.Length)]);
                    popup.Born = times[one];
                    popup.Closes = Built + luck.NextDouble() * (Opened - Built - 0.3);
                    // Under two flashes a second, well clear of the three that can set off a seizure.
                    popup.Rate = 4 + luck.NextDouble() * 8;
                    popup.Phase = luck.NextDouble() * Math.PI * 2;
                    popup.Hard = luck.Next(2) == 0;
                    popups.Add(popup);
                }
                popupClock.Restart();
                popupBeat = new System.Windows.Forms.Timer { Interval = 30 };
                popupBeat.Tick += delegate { PopupFrame(); };
                popupBeat.Start();
            }
            catch (Exception error) { Brain.Log("door intro", -1, error.Message); EndPopups(); }
        }

        private void PopupFrame()
        {
            double at = popupClock.Elapsed.TotalSeconds;
            if (at >= Length) { EndPopups(); return; }
            try
            {
                foreach (Popup popup in popups)
                {
                    if (popup.IsDisposed) continue;
                    if (at >= popup.Closes) { popup.Close(); popup.Dispose(); continue; }
                    if (at < popup.Born) continue;
                    if (!popup.Open) { popup.Open = true; popup.Show(); }
                    double turn = Math.Sin(at * popup.Rate + popup.Phase);
                    popup.Opacity = popup.Hard ? (turn > 0 ? 1 : 0.3) : 0.45 + 0.55 * (0.5 + 0.5 * turn);
                }
            }
            catch (Exception error) { Brain.Log("door intro", -1, error.Message); EndPopups(); }
        }

        private void EndPopups()
        {
            if (popupBeat != null) { popupBeat.Stop(); popupBeat.Dispose(); popupBeat = null; }
            foreach (Popup popup in popups)
                try { if (!popup.IsDisposed) { popup.Close(); popup.Dispose(); } }
                catch (Exception error) { Brain.Log("door intro", -1, error.Message); }
            popups.Clear();
        }

        // The screen the door is on: the one her intro covers, which is the same size as what she was given. The main screen when
        // more than one fits.
        private static Rectangle DoorScreen(Rectangle screen)
        {
            if (Screen.PrimaryScreen.Bounds.Size == screen.Size) return Screen.PrimaryScreen.Bounds;
            foreach (Screen one in Screen.AllScreens) if (one.Bounds.Size == screen.Size) return one.Bounds;
            return Screen.PrimaryScreen.Bounds;
        }

        // A red panel with black chevrons above and below and the word across a black band.
        internal static Bitmap Panel(string word)
        {
            Bitmap made = new Bitmap(480, 270);
            using (Graphics g = Graphics.FromImage(made))
            using (SolidBrush red = new SolidBrush(Color.FromArgb(200, 10, 20)))
            using (SolidBrush black = new SolidBrush(Color.Black))
            using (Font font = new Font("Arial", 92, FontStyle.Bold, GraphicsUnit.Pixel))
            using (StringFormat centre = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(red.Color);
                for (int x = -60; x < 540; x += 60)
                {
                    g.FillPolygon(black, new[] { new PointF(x, 0), new PointF(x + 26, 0), new PointF(x + 56, 52), new PointF(x + 30, 52) });
                    g.FillPolygon(black, new[] { new PointF(x + 30, 218), new PointF(x + 56, 218), new PointF(x + 26, 270), new PointF(x, 270) });
                }
                g.FillRectangle(black, 0, 72, 480, 126);
                g.DrawString(word, font, red, new RectangleF(0, 72, 480, 126), centre);
            }
            return made;
        }
    }
}
