using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace CapriDesktop
{
    internal sealed class Sheet : Control
    {
        internal const int Columns = 34;
        internal const string Speaker = "CAPRI: ";
        internal const double Type = 14;
        internal const double Line = 19;
        internal string Heard;
        internal string Prompt = "> you: ";
        internal string Reply;
        internal int Revealed;
        internal bool Thinking;
        internal bool CursorOn;
        internal double Glitch;
        internal Rect Panel;
        internal double TipX;
        internal double TipY;
        private readonly Random random = new Random();

        internal static double PanelWidth() { return 24 + Columns * Look.CharWidth(Type) + 14; }

        internal static double PanelHeight(string reply) { return 13 + Line * (1 + Look.Wrap(Speaker + (reply ?? ""), Columns).Count) + 10; }

        public override void Render(DrawingContext context)
        {
            if (Glitch <= 0) { Clean(context); return; }
            if (random.NextDouble() < 0.25 * Glitch) return;
            double y = 0;
            while (y < Bounds.Height)
            {
                double band = Math.Min(Bounds.Height - y, 3 + random.Next(10));
                double shift = (random.NextDouble() * 2 - 1) * 10 * Glitch;
                using (context.PushClip(new Rect(0, y, Bounds.Width, band)))
                using (context.PushTransform(Matrix.CreateTranslation(shift, 0))) Clean(context);
                y += band;
            }
            for (int i = 0; i < 2; i++)
            {
                double band = 2 + random.Next(6);
                double row = random.Next(Math.Max(1, (int)(Bounds.Height - band)));
                double shift = (random.NextDouble() * 2 - 1) * 14 * Glitch;
                Color tint = i == 0 ? Look.Cyan : Look.Red;
                using (context.PushClip(new Rect(shift, row, Bounds.Width, band)))
                    context.FillRectangle(new SolidColorBrush(Color.FromArgb((byte)(150 * Glitch), tint.R, tint.G, tint.B)), new Rect(0, row, Bounds.Width, band));
            }
        }

        private void Clean(DrawingContext context)
        {
            Rect panel = Panel;
            Geometry path = Look.Plate(panel, 10, TipX, TipY, 18);
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(46, Look.Cyan.R, Look.Cyan.G, Look.Cyan.B)), 7), path);
            context.DrawGeometry(new SolidColorBrush(Color.FromArgb(238, Look.Panel.R, Look.Panel.G, Look.Panel.B)), null, path);
            using (context.PushGeometryClip(path))
            {
                Pen scan = new Pen(new SolidColorBrush(Color.FromArgb(9, 255, 255, 255)), 1);
                for (double row = panel.Top; row < panel.Bottom; row += 3) context.DrawLine(scan, new Point(panel.Left, row), new Point(panel.Right, row));
            }
            context.DrawGeometry(null, new Pen(new SolidColorBrush(Look.Cyan), 2), path);
            context.FillRectangle(new SolidColorBrush(Look.Red), new Rect(panel.Left + 10, panel.Top + 13, 3, panel.Height - 26));
            double cw = Look.CharWidth(Type);
            double x = panel.Left + 24;
            double row2 = panel.Top + 12;
            string you = Prompt + (Heard ?? "");
            if (Look.Cells(you) > Columns) you = Look.Cut(you, Columns - 3) + "...";
            context.DrawText(Look.Text(you, Look.Italic, Type, Look.Dim), new Point(x, row2));
            row2 += Line;
            List<string> lines = Look.Wrap(Speaker + (Reply ?? ""), Columns);
            int[] takes = Look.Takes(lines, Speaker + (Reply ?? ""), Speaker.Length + (Thinking ? 0 : Math.Max(0, Revealed)));
            Point caret = new Point(x, row2);
            double right = x + Columns * cw;
            for (int i = 0; i < lines.Count && takes[i] > 0; i++)
            {
                string shown = lines[i].Substring(0, takes[i]);
                int name = i == 0 ? Math.Min(takes[i], Speaker.Length - 1) : 0;
                if (name > 0) context.DrawText(Look.Text(shown.Substring(0, name), Look.Bold, Type, Look.Red), new Point(x, row2));
                caret = new Point(x + Look.Cells(shown) * cw, row2 + i * Line);
                if (shown.Length <= name) continue;
                // An Arabic, Urdu, Persian or Hebrew line grows leftwards from the right edge as she types it. The whole line
                // decides, so it doesn't flip sides partway through. Right-to-left marks at both ends keep a full stop or
                // question mark at the line's own end.
                if (Look.RightToLeft(lines[i].Substring(name)))
                {
                    FormattedText words = Look.Text("‏" + shown.Substring(name) + "‏", Look.Regular, Type, Look.Neon);
                    double wide = words.WidthIncludingTrailingWhitespace;
                    context.DrawText(words, new Point(right - wide, row2 + i * Line));
                    caret = new Point(right - wide - cw - 2, row2 + i * Line);
                }
                else
                {
                    string ink = shown.Substring(name);
                    FormattedText words = Look.Text(ink, Look.Regular, Type, Look.Neon);
                    context.DrawText(words, new Point(x + name * cw, row2 + i * Line));
                    // Thai and Indian letters don't keep to her columns, so the caret follows what was drawn.
                    if (Look.Thai(ink) || Look.Indic(ink)) caret = new Point(x + name * cw + words.WidthIncludingTrailingWhitespace, row2 + i * Line);
                }
            }
            if (CursorOn) context.FillRectangle(new SolidColorBrush(Look.Neon), new Rect(caret.X + 1, caret.Y + 1, cw * 0.85, Line - 5));
        }
    }

    internal sealed class Bubble : Window
    {
        internal readonly Sheet Sheet = new Sheet();
        internal event Action Dismissed;
        private PixelRect panelOnScreen;

        internal Bubble()
        {
            Look.Overlay(this, "Capri says");
            Content = Sheet;
            Sheet.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton == MouseButton.Left && Dismissed != null) Dismissed(); };
        }

        internal PixelRect PanelOnScreen { get { return panelOnScreen; } }

        internal void Place(PixelPoint head, PixelPoint feet, PixelRect area, double scale)
        {
            double margin = 12;
            double top = 10;
            double tail = 16;
            double panelWidth = Sheet.PanelWidth();
            double panelHeight = Sheet.PanelHeight(Sheet.Reply);
            double width = Math.Ceiling(panelWidth + margin * 2);
            double height = Math.Ceiling(top + panelHeight + tail + 4);
            int widePx = (int)Math.Ceiling(width * scale);
            int tallPx = (int)Math.Ceiling(height * scale);
            bool below = head.Y - tallPx < area.Y;
            PixelPoint tip = below ? feet : head;
            int left = Math.Max(area.X, Math.Min(area.Right - widePx, (int)(tip.X - (margin + panelWidth * 0.7) * scale)));
            int y = below ? tip.Y : tip.Y - tallPx;
            Rect panel = new Rect(margin, below ? tail + 4 : top, panelWidth, panelHeight);
            Sheet.Panel = panel;
            Sheet.TipX = Math.Max(panel.Left + 32, Math.Min(panel.Right - 10, (tip.X - left) / scale));
            Sheet.TipY = below ? 0 : height - 1;
            if (Width != width) Width = width;
            if (Height != height) Height = height;
            panelOnScreen = new PixelRect(left + (int)(panel.X * scale), y + (int)(panel.Y * scale), (int)(panel.Width * scale), (int)(panel.Height * scale));
            PixelPoint at = new PixelPoint(left, y);
            if (Position != at) Position = at;
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            Sheet.InvalidateVisual();
        }
    }

    internal sealed class Plate : Control
    {
        public override void Render(DrawingContext context)
        {
            Geometry path = Look.Plate(new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2), 10, -1, Bounds.Height / 2, 0, true);
            context.DrawGeometry(new SolidColorBrush(Look.Panel), new Pen(new SolidColorBrush(Look.Cyan), 2), path);
            context.FillRectangle(new SolidColorBrush(Look.Red), new Rect(10, 10, 3, Bounds.Height - 20));
        }
    }

    internal sealed class Typer : Window
    {
        internal const double Tall = 44;
        internal event Action<string> Submitted;
        private readonly TextBox input = new TextBox();
        private readonly TextBlock prompt;

        internal Typer()
        {
            Look.Overlay(this, "Talk to Capri");
            ShowActivated = true;
            Width = Math.Ceiling(Sheet.PanelWidth());
            Height = Tall;
            prompt = new TextBlock { Text = "> you:", FontFamily = Look.Mono, FontStyle = FontStyle.Italic, FontSize = Sheet.Type, Foreground = new SolidColorBrush(Look.Dim), VerticalAlignment = VerticalAlignment.Center };
            input.FontFamily = Look.Mono;
            input.FontSize = Sheet.Type;
            input.MaxLength = 240;
            input.BorderThickness = new Thickness(0);
            input.Padding = new Thickness(0);
            input.MinHeight = 0;
            input.VerticalAlignment = VerticalAlignment.Center;
            input.VerticalContentAlignment = VerticalAlignment.Center;
            IBrush clear = Brushes.Transparent;
            IBrush ink = new SolidColorBrush(Look.Ink);
            foreach (string key in new[] { "TextControlBackground", "TextControlBackgroundPointerOver", "TextControlBackgroundFocused", "TextControlBorderBrush", "TextControlBorderBrushPointerOver", "TextControlBorderBrushFocused" }) input.Resources[key] = clear;
            foreach (string key in new[] { "TextControlForeground", "TextControlForegroundPointerOver", "TextControlForegroundFocused" }) input.Resources[key] = ink;
            input.Resources["TextControlSelectionHighlightColor"] = new SolidColorBrush(Look.Neon);
            input.CaretBrush = new SolidColorBrush(Look.Neon);
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Enter) { e.Handled = true; Submit(); }
                else if (e.Key == Key.Escape) { e.Handled = true; Hide(); }
            };
            DockPanel row = new DockPanel { Margin = new Thickness(24, 0, 14, 0), LastChildFill = true };
            DockPanel.SetDock(prompt, Dock.Left);
            prompt.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(prompt);
            row.Children.Add(input);
            Grid layers = new Grid();
            layers.Children.Add(new Plate());
            layers.Children.Add(row);
            Content = layers;
            Deactivated += delegate { if (Program.Smoke == null) Hide(); };
        }

        internal string Draft { get { return input.Text; } set { input.Text = value; } }

        internal bool Secret
        {
            get { return input.PasswordChar != '\0'; }
            set
            {
                input.PasswordChar = value ? '\u2022' : '\0';
                input.MaxLength = value ? 200 : 240;
                prompt.Text = value ? "> key:" : "> you:";
                if (value) input.Text = "";
            }
        }

        internal void Open(PixelPoint head, PixelPoint feet, PixelRect area, double scale, PixelRect above)
        {
            int widePx = (int)Math.Ceiling(Width * scale);
            int tallPx = (int)Math.Ceiling(Tall * scale);
            bool below = head.Y - tallPx - 20 * scale < area.Y;
            int left = Math.Max(area.X, Math.Min(area.Right - widePx, (int)(head.X - widePx * 0.7)));
            int top = below ? feet.Y + (int)(8 * scale) : head.Y - tallPx - (int)(18 * scale);
            Position = new PixelPoint(left, top);
            if (above.Width > 0) Follow(above, area, scale);
            Show();
            Look.OnTopAgain(this);
            Activate();
            input.Focus();
            input.CaretIndex = (input.Text ?? "").Length;
        }

        internal void Follow(PixelRect above, PixelRect area, double scale)
        {
            int tallPx = (int)Math.Ceiling(Tall * scale);
            int top = above.Y - tallPx - (int)(16 * scale);
            if (top < area.Y) top = above.Bottom + (int)(8 * scale);
            PixelPoint target = new PixelPoint(above.X, top);
            if (Position != target) Position = target;
        }

        internal void Send(string text)
        {
            input.Text = text;
            Submit();
        }

        private void Submit()
        {
            string text = (input.Text ?? "").Trim();
            if (text.Length == 0) return;
            input.Text = "";
            if (Submitted != null) Submitted(text);
        }
    }

    // The pencil and wave under her when you hover her, the same as on Windows: black plates with cut corners and a
    // pink-to-cyan neon edge, TYPE and TALK in her mono font, the wave as bars of square segments, and a few frames of her
    // glitch when it shows. It draws in whole screen pixels at her scale, so it stays crisp at 100%, 150% and 200%. Voice
    // chat is Windows-only, so the mic button, the mic level and MUTED only show in the smoke test's checks here.
    internal sealed class PillFace : Control
    {
        // Frames of the glitch it shows with, 45 ms each.
        internal const int TearFrames = 4;
        // Segments in each of the wave's five bars when it's full: the wave's shape, and the top of the mic level.
        internal static readonly int[] Bars = { 2, 4, 5, 3, 2 };
        internal static readonly Color Fill = Color.FromArgb(246, 9, 11, 15);
        internal static readonly Color Unlit = Color.FromRgb(46, 52, 64);
        internal static readonly Color Caption = Color.FromRgb(168, 174, 184);
        private const byte WashAlpha = 40;
        internal int Hover;
        internal bool Chatting;
        internal bool Muted;
        internal int Level;
        internal int Tear;
        internal int Seed;
        // A scale to draw at, in pixels, for the smoke test's pictures; 0 follows her window's scale.
        internal double Scale;

        // Its letters in plain greyscale like Windows, without coloured subpixel edges.
        internal PillFace() { RenderOptions.SetTextRenderingMode(this, TextRenderingMode.Antialias); }

        public override void Render(DrawingContext context)
        {
            TopLevel top = TopLevel.GetTopLevel(this);
            double s = Scale > 0 ? Scale : top != null ? top.RenderScaling : 1;
            double shrink = Scale > 0 ? 1 : 1 / s;
            using (context.PushTransform(Matrix.CreateScale(shrink, shrink))) Draw(context, s, Hover, Chatting, Muted, Level, Tear, Seed);
        }

        // One state as a control of its own at scale s, sized in pixels, for the smoke test.
        private static PillFace State(double s, int hover, bool chatting, bool muted, int level, int tear)
        {
            PillFace face = new PillFace { Hover = hover, Chatting = chatting, Muted = muted, Level = level, Tear = tear, Seed = 424242, Scale = s };
            Size size = new Size(Across(s, chatting), Measure(s).Height);
            face.Measure(size);
            face.Arrange(new Rect(size));
            return face;
        }

        // Its size in screen pixels at that scale, like TalkPill.Measure on Windows.
        internal static Size Measure(double s) { return new Size(Math.Round(Pill.Wide * s), Math.Round(Pill.Tall * s)); }

        internal static double Across(double s, bool chatting) { Size size = Measure(s); return chatting ? size.Width + Math.Round(4 * s) + size.Height : size.Width; }

        internal static Rect Body(double s)
        {
            Size size = Measure(s);
            double margin = Math.Round(3 * s);
            return new Rect(margin, margin, size.Width - margin * 2, size.Height - margin * 2);
        }

        internal static Rect MicBody(double s)
        {
            Size size = Measure(s);
            double margin = Math.Round(3 * s);
            return new Rect(size.Width + Math.Round(4 * s) + margin, margin, size.Height - margin * 2, size.Height - margin * 2);
        }

        internal static double Cut(double s) { return Math.Max(3, Math.Round(6 * s)); }

        internal static double EdgeWidth(double s) { return Math.Max(1, Math.Round(1.5 * s)); }

        internal static string Label(int part, bool chatting, bool muted) { return part == 1 ? "TYPE" : chatting && muted ? "MUTED" : "TALK"; }

        internal static int Lit(int bar, int level)
        {
            if (level <= 0) return 0;
            return Math.Max(1, (int)Math.Round(Bars[bar] * Math.Min(6, level) / 6.0, MidpointRounding.AwayFromZero));
        }

        private static double Square(double s) { return Math.Max(2, Math.Round(2 * s)); }

        private static double Pitch(double s) { return Math.Max(3, Math.Round(3 * s, MidpointRounding.AwayFromZero)); }

        internal static Rect Segment(Rect meter, double s, int bar, int row)
        {
            double square = Square(s);
            return new Rect(meter.Left + bar * Pitch(s), meter.Bottom - square - row * Pitch(s), square, square);
        }

        private static FormattedText Words(string label, double s, Color colour) { return Look.Text(label, Look.Regular, 8.5 * s, colour); }

        // Where a label's letters land, from where it's drawn, in whole pixels.
        private static Rect Ink(FormattedText text)
        {
            Geometry shape = text.BuildGeometry(new Point(0, 0));
            Rect ink = shape != null && shape.Bounds.Width > 0 ? shape.Bounds : new Rect(0, text.Baseline - text.Height * 0.55, text.Width, text.Height * 0.55);
            return new Rect(new Point(Math.Floor(ink.Left), Math.Floor(ink.Top)), new Point(Math.Ceiling(ink.Right), Math.Ceiling(ink.Bottom)));
        }

        // The icon's box and the label's letters in one half, 1 the pencil's and 2 the wave's, the pair in the middle of it.
        internal static Rect[] Layout(double s, int part, string label)
        {
            Rect ink = Ink(Words(label, s, Look.Ink));
            Rect body = Body(s);
            double mid = Math.Floor(Measure(s).Width / 2);
            double edge = EdgeWidth(s);
            double left = part == 1 ? body.Left + edge : mid;
            double right = part == 1 ? mid : body.Right - edge;
            double wide = part == 1 ? Math.Round(12.5 * s) : Pitch(s) * 4 + Square(s);
            double gap = Math.Round(4 * s);
            double start = left + Math.Floor((right - left - wide - gap - ink.Width + 1) / 2);
            double middle = body.Top + Math.Floor(body.Height / 2);
            return new[] { new Rect(start, middle - Math.Floor(wide / 2), wide, wide), new Rect(start + wide + gap, middle - Math.Floor(ink.Height / 2), ink.Width, ink.Height) };
        }

        // Draws it in screen pixels at scale s. A glitch frame draws it in rows, a few of them slipped sideways and tinted.
        internal static void Draw(DrawingContext context, double s, int hover, bool chatting, bool muted, int level, int tear, int seed)
        {
            if (tear <= 0) { Clean(context, s, hover, chatting, muted, level); return; }
            double width = Across(s, chatting);
            int height = (int)Measure(s).Height;
            double amount = Math.Min(1, (double)tear / TearFrames);
            Random roll = new Random(seed + tear);
            int bands = 2 + (int)Math.Round(2 * amount);
            int[] tops = new int[bands], tall = new int[bands], shifts = new int[bands];
            bool[] torn = new bool[height];
            for (int i = 0; i < bands; i++)
            {
                tall[i] = Math.Max(2, (int)Math.Round((2 + roll.Next(6)) * s));
                tops[i] = roll.Next(Math.Max(1, height - tall[i]));
                shifts[i] = (roll.Next(2) == 0 ? -1 : 1) * Math.Max(1, (int)Math.Round((2 + roll.Next(6)) * s * amount));
                for (int y = tops[i]; y < Math.Min(height, tops[i] + tall[i]); y++) torn[y] = true;
            }
            for (int y = 0; y < height;)
            {
                if (torn[y]) { y++; continue; }
                int end = y;
                while (end < height && !torn[end]) end++;
                using (context.PushClip(new Rect(0, y, width, end - y))) Clean(context, s, hover, chatting, muted, level);
                y = end;
            }
            for (int i = 0; i < bands; i++)
            {
                Rect strip = new Rect(0, tops[i], width, tall[i]);
                using (context.PushClip(strip))
                using (context.PushTransform(Matrix.CreateTranslation(shifts[i], 0))) Clean(context, s, hover, chatting, muted, level);
                Color tint = i % 2 == 0 ? Look.Pink : Look.Cyan;
                IBrush wash = new SolidColorBrush(Color.FromArgb((byte)(96 * amount), tint.R, tint.G, tint.B));
                foreach (Rect plate in chatting ? new[] { Body(s), MicBody(s) } : new[] { Body(s) })
                {
                    Rect both = plate.Intersect(strip);
                    if (both.Width > 0 && both.Height > 0) context.FillRectangle(wash, both);
                }
            }
        }

        private static void Clean(DrawingContext context, double s, int hover, bool chatting, bool muted, int level)
        {
            Rect body = Body(s);
            double mid = Math.Floor(Measure(s).Width / 2);
            Rect lit = hover == 1 ? new Rect(body.Left, body.Top, mid - body.Left, body.Height) : hover == 2 ? new Rect(mid, body.Top, body.Right - mid, body.Height) : new Rect();
            Plate(context, body, s, lit, Look.Pink, Look.Cyan);
            // A slanted seam where the two halves' hit areas meet.
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(66, 72, 86)), Math.Max(1, Math.Round(s))), new Point(mid + 1.5 * s, body.Top + 10 * s), new Point(mid - 1.5 * s, body.Bottom - 10 * s));
            // Her glitch, like her mini player on Windows: slivers of the edge torn loose, cyan at the top right and pink at the bottom left.
            double sliver = Math.Max(1, Math.Round(s));
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(220, Look.Cyan.R, Look.Cyan.G, Look.Cyan.B)), new Rect(body.Right - Math.Round(32 * s), body.Top - Math.Round(2 * s), Math.Round(15 * s), sliver));
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(220, Look.Pink.R, Look.Pink.G, Look.Pink.B)), new Rect(body.Left + Math.Round(12 * s), body.Bottom + Math.Round(2 * s) - sliver, Math.Round(11 * s), sliver));
            Color wash = Mix(Fill, Look.Cyan, WashAlpha);
            string talk = Label(2, chatting, muted);
            Rect[] pencil = Layout(s, 1, "TYPE"), wave = Layout(s, 2, talk);
            Pencil(context, pencil[0].Center, 0.9 * s, hover == 1 ? Look.Cyan : Look.Ink, hover == 1 ? wash : Fill);
            Words(context, "TYPE", s, pencil[1], hover == 1 ? Look.Cyan : Caption);
            Meter(context, wave[0], s, chatting, muted, level, hover == 2 || chatting ? Look.Cyan : Look.Ink);
            Words(context, talk, s, wave[1], chatting && muted ? Look.Red : hover == 2 || chatting ? Look.Cyan : Caption);
            if (!chatting) return;
            Rect mic = MicBody(s);
            Color edge = muted ? Look.Red : Look.Cyan;
            Plate(context, mic, s, hover == 3 ? mic : new Rect(), edge, edge);
            Mic(context, mic.Center, s, hover == 3 ? Look.Cyan : muted ? Look.Dim : Look.Ink, muted, hover == 3 ? wash : Fill);
        }

        // A plate with cut corners: a soft glow, the black fill with faint scan lines, a cyan wash where it's hovered, and a neon
        // edge from one colour to the other, inside the box so its straight edges land on whole pixels.
        private static void Plate(DrawingContext context, Rect box, double s, Rect lit, Color from, Color to)
        {
            double cut = Cut(s), edge = EdgeWidth(s), margin = Math.Round(3 * s), middle = box.Top + box.Height / 2;
            double x0 = box.Left - margin - 1, x1 = box.Right + margin + 1;
            Geometry outline = Look.Plate(box, cut, -1, middle, 0, true);
            context.DrawGeometry(null, new Pen(Gradient(x0, x1, from, to, 34), margin * 2), outline);
            context.DrawGeometry(null, new Pen(Gradient(x0, x1, from, to, 70), margin), outline);
            context.DrawGeometry(new SolidColorBrush(Fill), null, outline);
            using (context.PushGeometryClip(outline))
            {
                double line = Math.Max(1, Math.Round(s));
                Pen scan = new Pen(new SolidColorBrush(Color.FromArgb(9, 255, 255, 255)), line);
                for (double row = box.Top; row < box.Bottom; row += Math.Max(3, Math.Round(3 * s))) context.DrawLine(scan, new Point(box.Left, row + line / 2), new Point(box.Right, row + line / 2));
                if (lit.Width > 0) context.FillRectangle(new SolidColorBrush(Color.FromArgb(WashAlpha, Look.Cyan.R, Look.Cyan.G, Look.Cyan.B)), lit);
            }
            double inset = edge / 2;
            Geometry rim = Look.Plate(new Rect(box.Left + inset, box.Top + inset, box.Width - edge, box.Height - edge), cut - inset * 0.586, -1, middle, 0, true);
            context.DrawGeometry(null, new Pen(Gradient(x0, x1, from, to, 255), edge), rim);
        }

        private static IBrush Gradient(double x0, double x1, Color from, Color to, byte alpha)
        {
            LinearGradientBrush brush = new LinearGradientBrush { StartPoint = new RelativePoint(x0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(x1, 0, RelativeUnit.Absolute) };
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, from.R, from.G, from.B), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromArgb(alpha, to.R, to.G, to.B), 1));
            return brush;
        }

        private static Color Mix(Color under, Color over, int alpha)
        {
            return Color.FromRgb((byte)(under.R + (over.R - under.R) * alpha / 255), (byte)(under.G + (over.G - under.G) * alpha / 255), (byte)(under.B + (over.B - under.B) * alpha / 255));
        }

        private static void Words(DrawingContext context, string label, double s, Rect box, Color colour)
        {
            FormattedText text = Words(label, s, colour);
            Rect ink = Ink(text);
            context.DrawText(text, new Point(box.Left - ink.Left, box.Top - ink.Top));
        }

        // The wave as five bars of square segments. Out of voice chat it's the whole wave; in voice chat the segments light up to
        // your mic level and the rest stay dark, all of them while the mic is muted.
        // Lachy liked the old wave better (29 Oct): five rounded bars that stretch with your mic level in voice chat, and stay low
        // and grey while the mic is muted, like Windows'.
        private static void Meter(DrawingContext context, Rect meter, double s, bool chatting, bool muted, int level, Color colour)
        {
            double[] heights = { 5, 10, 14, 9, 4 };
            double stretch = !chatting ? 1 : 0.45 + (muted ? 0 : level) * 0.14;
            Point centre = meter.Center;
            Pen bar = new Pen(new SolidColorBrush(chatting && muted ? Look.Dim : colour), 2.3 * s) { LineCap = PenLineCap.Round };
            for (int i = 0; i < heights.Length; i++)
            {
                double x = centre.X + (i - 2) * 3.6 * s;
                double half = Math.Max(1.5, heights[i] * stretch) * s / 2;
                context.DrawLine(bar, new Point(x, centre.Y - half), new Point(x, centre.Y + half));
            }
        }

        private static void Mic(DrawingContext context, Point centre, double s, Color colour, bool muted, Color back)
        {
            double x = centre.X, y = centre.Y + 0.5 * s;
            IBrush ink = new SolidColorBrush(colour);
            context.DrawRectangle(ink, null, new Rect(x - 3.2 * s, y - 9 * s, 6.4 * s, 11 * s), 3.2 * s, 3.2 * s);
            Pen line = new Pen(ink, 1.8 * s, null, PenLineCap.Round);
            StreamGeometry cup = new StreamGeometry();
            using (StreamGeometryContext shape = cup.Open())
            {
                shape.BeginFigure(new Point(x + 6 * s, y - 2 * s), false);
                shape.ArcTo(new Point(x - 6 * s, y - 2 * s), new Size(6 * s, 6 * s), 0, false, SweepDirection.Clockwise);
                shape.EndFigure(false);
            }
            context.DrawGeometry(null, line, cup);
            context.DrawLine(line, new Point(x, y + 4 * s), new Point(x, y + 7 * s));
            context.DrawLine(line, new Point(x - 3.5 * s, y + 7 * s), new Point(x + 3.5 * s, y + 7 * s));
            if (!muted) return;
            context.DrawLine(new Pen(new SolidColorBrush(back), 4.2 * s), new Point(x - 7.5 * s, y - 9.5 * s), new Point(x + 7.5 * s, y + 7.5 * s));
            context.DrawLine(new Pen(new SolidColorBrush(Look.Red), 2 * s, null, PenLineCap.Round), new Point(x - 7.5 * s, y - 9.5 * s), new Point(x + 7.5 * s, y + 7.5 * s));
        }

        // A square-cut pencil at 45 degrees, centred on where its outline's middle falls.
        private static void Pencil(DrawingContext context, Point centre, double k, Color colour, Color back)
        {
            IBrush ink = new SolidColorBrush(colour);
            using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 4) * Matrix.CreateTranslation(centre.X + 0.5 * k, centre.Y - 0.5 * k)))
            {
                context.FillRectangle(ink, new Rect(-8 * k, -2.5 * k, 12.6 * k, 5 * k));
                StreamGeometry tip = new StreamGeometry();
                using (StreamGeometryContext shape = tip.Open())
                {
                    shape.BeginFigure(new Point(4.6 * k, -2.5 * k), true);
                    shape.LineTo(new Point(9 * k, 0));
                    shape.LineTo(new Point(4.6 * k, 2.5 * k));
                    shape.EndFigure(true);
                }
                context.DrawGeometry(ink, null, tip);
                Pen gap = new Pen(new SolidColorBrush(back), 1.1 * k);
                context.DrawLine(gap, new Point(-4.8 * k, -3 * k), new Point(-4.8 * k, 3 * k));
                context.DrawLine(gap, new Point(4 * k, -3 * k), new Point(4 * k, 3 * k));
            }
        }

        // One state drawn into a bitmap at scale s and read back, for the checks.
        private sealed class Shot
        {
            internal readonly int Width;
            internal readonly int Height;
            private readonly byte[] pixels;
            private readonly bool rgba;

            internal Shot(double s, int hover, bool chatting, bool muted, int level, bool rgba)
            {
                Width = (int)Across(s, chatting);
                Height = (int)Measure(s).Height;
                this.rgba = rgba;
                pixels = Read(Width, Height, State(s, hover, chatting, muted, level, 0));
            }

            internal Color At(int x, int y)
            {
                int i = (y * Width + x) * 4;
                int a = pixels[i + 3];
                if (a == 0) return Color.FromArgb(0, 0, 0, 0);
                int r = rgba ? pixels[i] : pixels[i + 2], g = pixels[i + 1], b = rgba ? pixels[i + 2] : pixels[i];
                return Color.FromArgb((byte)a, (byte)Math.Min(255, r * 255 / a), (byte)Math.Min(255, g * 255 / a), (byte)Math.Min(255, b * 255 / a));
            }

            internal int Count(Rect box, Func<Color, bool> test)
            {
                int count = 0;
                for (int y = Math.Max(0, (int)box.Top); y < Math.Min(Height, (int)box.Bottom); y++)
                    for (int x = Math.Max(0, (int)box.Left); x < Math.Min(Width, (int)box.Right); x++)
                        if (test(At(x, y))) count++;
                return count;
            }
        }

        private static byte[] Read(int width, int height, Action<DrawingContext> paint)
        {
            byte[] pixels = new byte[width * height * 4];
            using (RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                using (DrawingContext context = target.CreateDrawingContext()) paint(context);
                GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { target.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4); }
                finally { pin.Free(); }
            }
            return pixels;
        }

        private static byte[] Read(int width, int height, Control face)
        {
            byte[] pixels = new byte[width * height * 4];
            using (RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize(width, height), new Vector(96, 96)))
            {
                target.Render(face);
                GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
                try { target.CopyPixels(new PixelRect(0, 0, width, height), pin.AddrOfPinnedObject(), pixels.Length, width * 4); }
                finally { pin.Free(); }
            }
            return pixels;
        }

        // The smoke test's checks of its look at 100%, 150% and 200%, the same as on Windows: null when they pass, or what's
        // wrong. It saves the states it checked, at 100% and 200%, as one picture.
        internal static string Check(string picture)
        {
            byte[] probe = Read(1, 1, delegate(DrawingContext context) { context.FillRectangle(new SolidColorBrush(Color.FromRgb(255, 0, 0)), new Rect(0, 0, 1, 1)); });
            bool rgba = probe[0] > 200;
            if (picture != null) Sheet(picture);
            if (Pill.Wide != 100 || Pill.Tall != 40) return "Its size changed: " + Pill.Wide + " x " + Pill.Tall;
            if (Pill.PartAt(0, false) != 1 || Pill.PartAt(49.5, false) != 1 || Pill.PartAt(50, false) != 2 || Pill.PartAt(99.5, true) != 2 || Pill.PartAt(100, true) != 3 || Pill.PartAt(143.5, true) != 3 || Pill.PartAt(102, false) != 2) return "Its hit areas moved";
            foreach (double s in new[] { 1.0, 1.5, 2.0 })
            {
                string at = " at " + (int)Math.Round(s * 100) + "%";
                Size size = Measure(s);
                Rect body = Body(s), mic = MicBody(s);
                double cut = Cut(s), edge = EdgeWidth(s);
                Shot idle = new Shot(s, 0, false, false, 0, rgba), typing = new Shot(s, 1, false, false, 0, rgba), talking = new Shot(s, 2, false, false, 0, rgba);
                Shot live0 = new Shot(s, 0, true, false, 0, rgba), live3 = new Shot(s, 0, true, false, 3, rgba), live6 = new Shot(s, 0, true, false, 6, rgba);
                Shot hushed = new Shot(s, 0, true, true, 0, rgba), micLit = new Shot(s, 3, true, false, 0, rgba);
                if (idle.Width != (int)Math.Round(100 * s) || idle.Height != (int)Math.Round(40 * s) || live0.Width != (int)(size.Width + Math.Round(4 * s) + size.Height)) return "Its size changed" + at;
                foreach (Rect plate in new[] { body, mic })
                {
                    Shot shot = plate == body ? idle : live0;
                    int l = (int)plate.Left, t = (int)plate.Top, r = (int)plate.Right - 1, b = (int)plate.Bottom - 1, c = (int)cut;
                    if (shot.At(l, t).A > 140 || shot.At(r, b).A > 140 || shot.At(r, t).A > 140 || shot.At(l, t + c + 1).A < 250 || shot.At(r, b - c - 1).A < 250 || shot.At(l + c + 1, t).A < 250 || shot.At(r - c - 1, b).A < 250)
                        return "Its corners aren't cut" + at;
                }
                int x0 = (int)(body.Left + cut + 2), top = (int)(body.Top + edge), half = (int)(body.Top + Math.Floor(body.Height / 2)), side = (int)(body.Left + edge);
                if (Brightest(idle.At(x0, top - 1)) < 150 || Brightest(idle.At(x0, top)) > 60 || Brightest(idle.At(side - 1, half)) < 150 || Brightest(idle.At(side, half)) > 60) return "Its neon edge isn't crisp" + at;
                if (!Red(hushed.At((int)mic.Left, (int)(mic.Top + cut + 1))) || Red(live0.At((int)mic.Left, (int)(mic.Top + cut + 1)))) return "The mic button's edge didn't go red only while muted" + at;
                Rect[] pencil = Layout(s, 1, "TYPE"), wave = Layout(s, 2, "TALK"), hush = Layout(s, 2, "MUTED");
                int letters = (int)(6 * s * s);
                if (idle.Count(pencil[1], Grey) < letters || idle.Count(wave[1], Grey) < letters) return "Its TYPE and TALK labels are missing" + at;
                if (typing.Count(pencil[1], Cyan) < letters || typing.Count(wave[1], Cyan) > 0 || talking.Count(wave[1], Cyan) < letters || talking.Count(pencil[1], Cyan) > 0) return "Hovering didn't light just its part cyan" + at + ": " + typing.Count(pencil[1], Cyan) + " " + typing.Count(wave[1], Cyan) + " " + talking.Count(wave[1], Cyan) + " " + talking.Count(pencil[1], Cyan) + " of " + letters + " in " + pencil[1] + " and " + wave[1];
                Point tip = pencil[0].Center;
                if (!Grey(idle.At((int)tip.X, (int)tip.Y)) || !Cyan(typing.At((int)tip.X, (int)tip.Y))) return "The pencil didn't light cyan on hover" + at;
                if (hushed.Count(hush[1], Red) < letters || live0.Count(hush[1], Red) > 0 || idle.Count(hush[1], Red) > 0) return "MUTED didn't show in red, or showed unmuted" + at;
                Rect core = mic.Inflate(-cut - edge);
                if (micLit.Count(core, Cyan) < letters || live0.Count(core, Cyan) > 0) return "Hovering the mic didn't light it cyan" + at;
                Rect waveBox = wave[0].Inflate(4 * s), hushBox = hush[0].Inflate(4 * s);
                Point middle = wave[0].Center;
                if (!Grey(idle.At((int)middle.X, (int)middle.Y)) || !Cyan(talking.At((int)middle.X, (int)middle.Y))) return "The wave didn't show" + at;
                int quietest = live0.Count(waveBox, Cyan), middling = live3.Count(waveBox, Cyan), loudest = live6.Count(waveBox, Cyan);
                if (!(quietest < middling && middling < loudest)) return "The wave didn't stretch with the mic level" + at + ": " + quietest + ", " + middling + ", " + loudest;
                if (hushed.Count(hushBox, Cyan) > 0) return "The wave lit while muted" + at;
            }
            return null;
        }

        // The states side by side at 100% and 200%, on a dark and a light desktop, like Windows' pill.png.
        private static void Sheet(string path)
        {
            object[][] states =
            {
                new object[] { 0, false, false, 0, 0 }, new object[] { 1, false, false, 0, 0 }, new object[] { 2, false, false, 0, 0 },
                new object[] { 0, true, false, 0, 0 }, new object[] { 0, true, false, 3, 0 }, new object[] { 0, true, false, 6, 0 },
                new object[] { 3, true, false, 2, 0 }, new object[] { 0, true, true, 0, 0 }, new object[] { 3, true, true, 0, 0 },
                new object[] { 0, false, false, 0, 4 }, new object[] { 0, false, false, 0, 2 }
            };
            double[] scales = { 1, 2 };
            int gap = 18, row = 98, width = gap;
            foreach (double s in scales) width += 2 * ((int)Across(s, true) + gap);
            List<RenderTargetBitmap> shots = new List<RenderTargetBitmap>();
            using (RenderTargetBitmap sheet = new RenderTargetBitmap(new PixelSize(width, 20 + states.Length * row), new Vector(96, 96)))
            {
                using (DrawingContext context = sheet.CreateDrawingContext())
                {
                    context.FillRectangle(new SolidColorBrush(Color.FromRgb(24, 26, 32)), new Rect(0, 0, width, 20 + states.Length * row));
                    double x = gap;
                    foreach (double s in scales)
                        foreach (bool light in new[] { false, true })
                        {
                            double across = Across(s, true);
                            context.FillRectangle(new SolidColorBrush(light ? Color.FromRgb(214, 220, 228) : Color.FromRgb(44, 48, 58)), new Rect(x - 6, 10, across + 12, states.Length * row));
                            for (int i = 0; i < states.Length; i++)
                            {
                                PillFace face = State(s, (int)states[i][0], (bool)states[i][1], (bool)states[i][2], (int)states[i][3], (int)states[i][4]);
                                RenderTargetBitmap shot = new RenderTargetBitmap(new PixelSize((int)face.Bounds.Width, (int)face.Bounds.Height), new Vector(96, 96));
                                shot.Render(face);
                                shots.Add(shot);
                                context.DrawImage(shot, new Rect(x, 20 + i * row, face.Bounds.Width, face.Bounds.Height));
                            }
                            x += across + gap;
                        }
                }
                sheet.Save(path);
            }
            foreach (RenderTargetBitmap shot in shots) shot.Dispose();
        }

        private static int Brightest(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)); }

        private static bool Grey(Color c) { return c.A > 200 && Math.Min(c.R, Math.Min(c.G, c.B)) > 80 && Brightest(c) - Math.Min(c.R, Math.Min(c.G, c.B)) < 40; }

        private static bool Cyan(Color c) { return c.A > 200 && c.G > 140 && c.B > 150 && c.R < 130; }

        private static bool Red(Color c) { return c.A > 200 && c.R > 150 && c.G < 90 && c.B < 120; }
    }

    internal sealed class Pill : Window
    {
        internal const double Wide = 100;
        internal const double Tall = 40;
        internal event Action Typing;
        internal event Action Voice;
        private static readonly Random random = new Random();
        private readonly PillFace face = new PillFace();
        private double shown = -1;

        internal Pill()
        {
            Look.Overlay(this, "Talk to Capri");
            Width = Wide;
            Height = Tall;
            Content = face;
            face.PointerMoved += delegate(object sender, PointerEventArgs e) { Hover(Part(e.GetPosition(face).X)); };
            face.PointerExited += delegate { Hover(0); };
            face.PointerReleased += delegate(object sender, PointerReleasedEventArgs e) { if (e.InitialPressMouseButton == MouseButton.Left) Press(Part(e.GetPosition(face).X)); };
        }

        internal bool Over { get { return face.IsPointerOver; } }

        // True for the few frames it glitches in.
        internal bool Tearing { get { return face.Tear > 0; } }

        // Which part is under x, in her window's units: 1 the pencil, 2 the wave, 3 the mic button right of it in voice chat
        // (Windows-only for now), like Windows.
        internal static int PartAt(double x, bool chatting)
        {
            if (chatting && x >= Wide) return 3;
            return x < Wide / 2 ? 1 : 2;
        }

        private int Part(double x) { return PartAt(x, false); }

        private void Hover(int part)
        {
            if (face.Hover == part) return;
            face.Hover = part;
            face.InvalidateVisual();
        }

        // Only showing it glitches: a hover or her walking draws it clean.
        internal void Place(PixelPoint topCentre, double scale, double now)
        {
            PixelPoint at = new PixelPoint(topCentre.X - (int)Math.Round(Wide * scale / 2), topCentre.Y);
            if (Position != at) Position = at;
            if (!IsVisible)
            {
                shown = now;
                face.Seed = random.Next(1, int.MaxValue);
                face.Tear = PillFace.TearFrames;
                face.InvalidateVisual();
                Show();
                Look.OnTopAgain(this);
            }
            int tear = shown < 0 ? 0 : Math.Max(0, PillFace.TearFrames - (int)((now - shown) / 0.045));
            if (tear == 0) shown = -1;
            if (tear != face.Tear) { face.Tear = tear; face.InvalidateVisual(); }
        }

        // For the smoke test: it glitches in when it shows, settles after its frames, and a hover doesn't glitch it again.
        internal bool GlitchesIn(double now)
        {
            double scale = DesktopScaling;
            PixelPoint topCentre = new PixelPoint(Position.X + (int)Math.Round(Wide * scale / 2), Position.Y);
            Hide();
            Place(topCentre, scale, now);
            bool torn = IsVisible && face.Tear == PillFace.TearFrames;
            Place(topCentre, scale, now + 0.1);
            bool fading = face.Tear > 0 && face.Tear < PillFace.TearFrames;
            Place(topCentre, scale, now + 0.5);
            bool calm = face.Tear == 0;
            Hover(1);
            Hover(0);
            Place(topCentre, scale, now + 0.6);
            return torn && fading && calm && face.Tear == 0;
        }

        internal void Press(int part)
        {
            if (part == 1) { if (Typing != null) Typing(); }
            else if (Voice != null) Voice();
        }
    }

    internal sealed class Talk
    {
        internal const double LettersPerSecond = 28;
        private const double MouthRate = 12;
        internal readonly Bubble Bubble = new Bubble();
        internal readonly Typer Typer = new Typer();
        internal readonly Pill Pill = new Pill();
        private readonly Pet pet;
        private string reply;
        private double replyStart = -1;
        private double replyDone = -1;
        private double opened = -1;
        private double leaving = -1;
        private double pillUntil;
        private bool thinking;
        private double pace = LettersPerSecond;
        private Action<string> asked;
        // Where a key typed in the hidden box goes: her voice key unless something else asked for it.
        private Action<string> secretTo;

        internal Talk(Pet pet)
        {
            this.pet = pet;
            Typer.Submitted += delegate(string text)
            {
                if (asked != null) { Action<string> to = asked; asked = null; Typer.Hide(); to(text); return; }
                if (!Typer.Secret) { pet.Hear(text); return; }
                Typer.Secret = false;
                Typer.Hide();
                Action<string> keyTo = secretTo ?? pet.SaveVoiceKey;
                secretTo = null;
                keyTo(text);
            };
            Pill.Typing += delegate { if (Typer.IsVisible) Typer.Hide(); else Open(); };
            Pill.Voice += delegate { pet.NoVoice(); };
            Bubble.Dismissed += delegate { if (leaving < 0) leaving = pet.Now; };
        }

        internal string Reply { get { return reply; } }
        internal double Pace { get { return pace; } }
        internal string Shown { get { return reply == null ? "" : reply.Substring(0, Math.Min(reply.Length, Math.Max(0, Bubble.Sheet.Revealed))); } }
        internal bool Typing { get { return Typer.IsVisible; } }
        internal bool Done { get { return reply != null && replyDone >= 0; } }
        internal bool Quiet { get { return reply == null && !thinking && !Typer.IsVisible; } }
        internal Control Face { get { return Bubble.Sheet; } }

        internal void Open()
        {
            asked = null;
            secretTo = null;
            if (Typer.Secret) Typer.Secret = false;
            pillUntil = pet.Now + 1.5;
            Typer.Open(pet.Head, pet.FeetPoint, pet.Area, pet.DesktopScaling, Bubble.IsVisible ? Bubble.PanelOnScreen : new PixelRect());
        }

        internal void Submit(string text) { Typer.Send(text); }

        internal void AskFor(string heard, string ask, Action<string> to)
        {
            Answer(heard, ask);
            if (Typer.Secret) Typer.Secret = false;
            secretTo = null;
            asked = to;
            pillUntil = pet.Now + 1.5;
            Typer.Open(pet.Head, pet.FeetPoint, pet.Area, pet.DesktopScaling, Bubble.IsVisible ? Bubble.PanelOnScreen : new PixelRect());
        }

        internal void AskKey(string ask) { AskKey("(voice key)", ask, null); }

        internal void AskKey(string heard, string ask, Action<string> to)
        {
            asked = null;
            secretTo = to;
            Answer(heard, ask);
            Typer.Secret = true;
            pillUntil = pet.Now + 1.5;
            Typer.Open(pet.Head, pet.FeetPoint, pet.Area, pet.DesktopScaling, Bubble.IsVisible ? Bubble.PanelOnScreen : new PixelRect());
        }

        internal void Thinking(string heard)
        {
            if (pet.Away) return;
            reply = null;
            thinking = true;
            pace = LettersPerSecond;
            replyDone = -1;
            Start(heard);
        }

        // Like on Windows, where her bubble is drawn in her own window, nothing new shows while she's hidden or glitching out of
        // sight: it would float there with nobody under it, and stop her pop-ins until she's shown.
        internal void Answer(string heard, string answer, double seconds = 0)
        {
            if (pet.Away) return;
            reply = answer;
            thinking = false;
            pace = seconds < 0.2 ? LettersPerSecond : Math.Max(8, answer.Length / seconds);
            replyStart = pet.Now;
            replyDone = -1;
            Start(heard);
        }

        private void Start(string heard)
        {
            Sheet sheet = Bubble.Sheet;
            if (!Bubble.IsVisible || leaving >= 0) opened = pet.Now;
            leaving = -1;
            sheet.Heard = heard;
            sheet.Reply = reply;
            sheet.Thinking = thinking;
            sheet.Revealed = 0;
            Update(pet.Now);
        }

        internal void Dismiss()
        {
            reply = null;
            thinking = false;
            leaving = -1;
            Bubble.Hide();
            Typer.Hide();
        }

        // While she's hidden her bubble and pill stay down too, even one that was still up when her window went.
        internal void Gone()
        {
            reply = null;
            thinking = false;
            leaving = -1;
            if (Bubble.IsVisible) Bubble.Hide();
            if (Pill.IsVisible) Pill.Hide();
        }

        // How long a finished reply stays up, like Windows: 4 s plus a bit per letter, up to 14 s, halved for Quick and
        // doubled for Long.
        internal static double Hold(int length, int level) { return Math.Min(14, 4 + length * 0.05) * new[] { 0.5, 1, 2 }[Math.Max(0, Math.Min(2, level))]; }

        internal void Update(double now)
        {
            UpdatePill(now);
            if (reply == null && !thinking) { if (Bubble.IsVisible) Bubble.Hide(); return; }
            Sheet sheet = Bubble.Sheet;
            if (reply != null)
            {
                sheet.Revealed = Math.Min(reply.Length, (int)((now - replyStart) * pace));
                if (sheet.Revealed >= reply.Length && replyDone < 0) replyDone = now;
                if (replyDone >= 0 && leaving < 0 && !Typer.IsVisible && now > replyDone + Hold(reply.Length, pet.BubbleTime)) leaving = now;
            }
            double glitch = 0;
            if (opened >= 0 && now - opened < 0.2) glitch = 1 - (now - opened) / 0.2;
            if (leaving >= 0)
            {
                if (now - leaving > 0.25) { reply = null; thinking = false; leaving = -1; Bubble.Hide(); return; }
                glitch = Math.Min(1, (now - leaving) / 0.25 + 0.3);
            }
            sheet.Glitch = glitch;
            sheet.CursorOn = thinking ? (int)(now * 2.5) % 2 == 0 : replyDone < 0 || (int)(now * 2) % 2 == 0;
            Bubble.Place(pet.Head, pet.FeetPoint, pet.Area, pet.DesktopScaling);
            if (Typer.IsVisible) Typer.Follow(Bubble.PanelOnScreen, pet.Area, pet.DesktopScaling);
        }

        private void UpdatePill(double now)
        {
            bool allowed = !pet.Busy;
            if (allowed && (pet.Hovered || (Pill.IsVisible && Pill.Over) || Typer.IsVisible || reply != null || thinking)) pillUntil = Math.Max(pillUntil, now + 1.5);
            if (!allowed || now > pillUntil) { if (Pill.IsVisible) Pill.Hide(); return; }
            double scale = pet.DesktopScaling;
            PixelPoint feet = pet.FeetPoint;
            int top = Math.Min(feet.Y + (int)(2 * scale), pet.Area.Bottom - (int)Math.Ceiling(Pill.Tall * scale) - 2);
            Pill.Place(new PixelPoint(feet.X - (int)(4 * scale), top), scale, now);
        }

        internal int MouthCell(double now)
        {
            if (reply == null || replyDone >= 0 || replyStart < 0) return -1;
            int step = (int)((now - replyStart) * MouthRate);
            int letter = (int)((now - replyStart) * pace);
            return letter < reply.Length ? MouthFor(reply[letter], step) : -1;
        }

        internal static int MouthFor(char letter, int step)
        {
            char lower = char.ToLowerInvariant(letter);
            if ("aeo".IndexOf(lower) >= 0) return char.IsUpper(letter) ? 4 : 3;
            if ("iuy".IndexOf(lower) >= 0) return 2;
            if ("mbp".IndexOf(lower) >= 0 || !char.IsLetter(letter)) return 0;
            return step % 2 == 1 ? 1 : 2;
        }
    }
}
