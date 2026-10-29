using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

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

    internal sealed class PillFace : Control
    {
        internal int Hover;

        public override void Render(DrawingContext context)
        {
            double margin = 3;
            Rect pill = new Rect(margin, margin, Bounds.Width - margin * 2, Bounds.Height - margin * 2);
            context.DrawRectangle(new SolidColorBrush(Look.Pill), new Pen(new SolidColorBrush(Look.Edge), 1.2), pill, pill.Height / 2, pill.Height / 2);
            double middle = pill.X + pill.Width / 2;
            context.DrawLine(new Pen(new SolidColorBrush(Look.Edge), 1.2), new Point(middle, pill.Y + pill.Height * 0.27), new Point(middle, pill.Bottom - pill.Height * 0.27));
            Point pencil = new Point(pill.X + pill.Width * 0.26, pill.Y + pill.Height / 2);
            Point wave = new Point(pill.X + pill.Width * 0.74, pill.Y + pill.Height / 2);
            if (Hover == 1 || Hover == 2)
            {
                Point lit = Hover == 1 ? pencil : wave;
                double radius = pill.Height * 0.4;
                context.DrawEllipse(new SolidColorBrush(Color.FromArgb(30, 255, 255, 255)), null, lit, radius, radius);
            }
            Pencil(context, pencil, Hover == 1 ? Look.Cyan : Look.Ink);
            Wave(context, wave, Hover == 2 ? Look.Cyan : Look.Ink);
        }

        private static void Pencil(DrawingContext context, Point centre, Color colour)
        {
            IBrush ink = new SolidColorBrush(colour);
            using (context.PushTransform(Matrix.CreateRotation(-Math.PI / 4) * Matrix.CreateTranslation(centre.X, centre.Y)))
            {
                context.DrawRectangle(ink, null, new Rect(-8, -2.5, 12, 5), 1.2, 1.2);
                StreamGeometry tip = new StreamGeometry();
                using (StreamGeometryContext shape = tip.Open())
                {
                    shape.BeginFigure(new Point(4.6, -2.5), true);
                    shape.LineTo(new Point(9, 0));
                    shape.LineTo(new Point(4.6, 2.5));
                    shape.EndFigure(true);
                }
                context.DrawGeometry(ink, null, tip);
                Pen gap = new Pen(new SolidColorBrush(Color.FromRgb(20, 21, 25)), 1.1);
                context.DrawLine(gap, new Point(-4.8, -3), new Point(-4.8, 3));
                context.DrawLine(gap, new Point(4, -3), new Point(4, 3));
            }
        }

        private static void Wave(DrawingContext context, Point centre, Color colour)
        {
            double[] heights = { 5, 10, 14, 9, 4 };
            Pen bar = new Pen(new SolidColorBrush(colour), 2.3, null, PenLineCap.Round);
            for (int i = 0; i < heights.Length; i++)
            {
                double x = centre.X + (i - 2) * 3.6;
                double half = Math.Max(1.5, heights[i]) / 2;
                context.DrawLine(bar, new Point(x, centre.Y - half), new Point(x, centre.Y + half));
            }
        }
    }

    internal sealed class Pill : Window
    {
        internal const double Wide = 100;
        internal const double Tall = 40;
        internal event Action Typing;
        internal event Action Voice;
        private readonly PillFace face = new PillFace();

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

        private int Part(double x) { return x < Wide / 2 ? 1 : 2; }

        private void Hover(int part)
        {
            if (face.Hover == part) return;
            face.Hover = part;
            face.InvalidateVisual();
        }

        internal void Place(PixelPoint topCentre, double scale)
        {
            PixelPoint at = new PixelPoint(topCentre.X - (int)Math.Round(Wide * scale / 2), topCentre.Y);
            if (Position != at) Position = at;
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
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
            Pill.Place(new PixelPoint(feet.X - (int)(4 * scale), top), scale);
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
