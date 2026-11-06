using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace CapriDesktop
{
    internal static class MainframeTheme
    {
        internal static readonly string[] LookNames = { "Night City", "Black", "White", "Grey" };
        private static readonly string[][] Looks =
        {
            new[] { "#0A0A10", "#360E1A", "#100C14", "#2C7A86", "#FF5D66", "#ECECF1", "#92828C", "#11101C", "#1E1424", "#2E1626", "#5C1824", "#E84246", "#5EF0F6", "#091E21", "#287884", "#060E12", "#4E1C28", "#F55A56", "#781E2C", "#0C0B14", "#481C28", "#F7DF70" },
            new[] { "#000000", "#181818", "#080808", "#6E6E6E", "#FFFFFF", "#EEEEEE", "#969696", "#101010", "#222222", "#343434", "#545454", "#F5F5F5", "#CDCDCD", "#161616", "#787878", "#010101", "#464646", "#FAFAFA", "#606060", "#060606", "#4C4C4C", "#E6E6E6" },
            new[] { "#FFFFFF", "#ECECEC", "#F8F8F8", "#787878", "#000000", "#141414", "#606060", "#F4F4F4", "#E6E6E6", "#D6D6D6", "#AAAAAA", "#1E1E1E", "#404040", "#EEEEEE", "#6E6E6E", "#FEFEFE", "#BEBEBE", "#0A0A0A", "#969696", "#FAFAFA", "#B4B4B4", "#3C3C3C" },
            new[] { "#5C5C5C", "#6C6C6C", "#545454", "#AAAAAA", "#FFFFFF", "#FAFAFA", "#DEDEDE", "#4E4E4E", "#686868", "#767676", "#8C8C8C", "#F6F6F6", "#ECECEC", "#484848", "#BEBEBE", "#1E1E1E", "#828282", "#FCFCFC", "#A0A0A0", "#282828", "#969696", "#F4F4F4" }
        };
        private static readonly List<SolidColorBrush> Slots = new List<SolidColorBrush>();
        private static int shade;
        internal static readonly IBrush Ground = Slot();
        internal static readonly IBrush Glow = Slot();
        internal static readonly IBrush Band = Slot();
        internal static readonly IBrush Rule = Slot();
        internal static readonly IBrush Rose = Slot();
        internal static readonly IBrush White = Slot();
        internal static readonly IBrush Faint = Slot();
        internal static readonly IBrush Field = Slot();
        internal static readonly IBrush Hover = Slot();
        internal static readonly IBrush Pressed = Slot();
        internal static readonly IBrush Edge = Slot();
        internal static readonly IBrush Hot = Slot();
        internal static readonly IBrush Cyan = Slot();
        internal static readonly IBrush Deep = Slot();
        internal static readonly IBrush DeepEdge = Slot();
        internal static readonly IBrush OnInk = Slot();
        internal static readonly IBrush Dash = Slot();
        internal static readonly IBrush Lit = Slot();
        internal static readonly IBrush Brim = Slot();
        internal static readonly IBrush Well = Slot();
        internal static readonly IBrush WellEdge = Slot();
        internal static readonly IBrush Yellow = Slot();

        private static IBrush Slot() { var brush = new SolidColorBrush(Color.Parse(Looks[0][Slots.Count])); Slots.Add(brush); return brush; }
        internal static int ClampLook(int look) { return look < 0 || look >= Looks.Length ? 0 : look; }
        internal static bool Mono { get { return shade != 0; } }
        internal static string[] LookColours(int look) { return Looks[ClampLook(look)]; }
        internal static void UseLook(int look)
        {
            shade = ClampLook(look);
            for (int i = 0; i < Slots.Count; i++) Slots[i].Color = Color.Parse(Looks[shade][i]);
        }
        internal static void Repaint(Visual also)
        {
            var desktop = Application.Current == null ? null : Application.Current.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null) foreach (var window in desktop.Windows) RepaintAll(window);
            if (also != null) RepaintAll(also);
        }
        private static void RepaintAll(Visual visual)
        {
            visual.InvalidateVisual();
            foreach (var child in visual.GetVisualDescendants()) child.InvalidateVisual();
        }
        internal static Color Tint(string value)
        {
            var colour = Color.Parse(value);
            if (shade == 0) return colour;
            int light = (299 * colour.R + 587 * colour.G + 114 * colour.B) / 1000;
            byte v = (byte)(shade == 1 ? light : shade == 2 ? 255 - light * 85 / 100 : 70 + light * 72 / 100);
            return Color.FromArgb(colour.A, v, v, v);
        }

        internal static readonly FontFamily Text = new FontFamily("avares://Capri/Assets#Barlow Semi Condensed");
        internal static readonly FontFamily Display = new FontFamily("avares://Capri/Assets#Barlow Condensed");
        internal const double Size = 38.0 / 3;

        internal static IBrush Brush(string value) { return new SolidColorBrush(Tint(value)); }
        internal static FormattedText Words(string text, double size = Size, bool bold = false, IBrush ink = null, bool display = false)
        {
            return new FormattedText(text ?? "", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface(display ? Display : Text, FontStyle.Normal, bold ? FontWeight.SemiBold : FontWeight.Normal), size, ink ?? White);
        }

        internal static void Write(DrawingContext context, string text, Rect box, IBrush ink, double size = Size, bool bold = false, TextAlignment align = TextAlignment.Left, bool display = false)
        {
            if (box.Width <= 0 || box.Height <= 0) return;
            var words = Words(text, size, bold, ink, display);
            words.MaxTextWidth = box.Width;
            words.MaxTextHeight = box.Height;
            words.Trimming = TextTrimming.CharacterEllipsis;
            words.TextAlignment = align;
            using (context.PushClip(box)) context.DrawText(words, new Point(box.X, box.Y + Math.Max(0, (box.Height - words.Height) / 2)));
        }
        internal static string Fitted(string text, double width, double size = Size, bool bold = false, bool display = false)
        {
            text = text ?? "";
            if (Words(text, size, bold, display: display).Width <= width) return text;
            while (text.Length > 0 && Words(text + "…", size, bold, display: display).Width > width) text = text.Substring(0, text.Length - 1);
            return text + "…";
        }

        internal static Geometry Cut(Rect b, double tl = 0, double tr = 0, double br = 5, double bl = 0)
        {
            var path = new StreamGeometry();
            using (var c = path.Open())
            {
                c.BeginFigure(new Point(b.X + tl, b.Y), true);
                c.LineTo(new Point(b.Right - tr, b.Y)); c.LineTo(new Point(b.Right, b.Y + tr));
                c.LineTo(new Point(b.Right, b.Bottom - br)); c.LineTo(new Point(b.Right - br, b.Bottom));
                c.LineTo(new Point(b.X + bl, b.Bottom)); c.LineTo(new Point(b.X, b.Bottom - bl));
                c.LineTo(new Point(b.X, b.Y + tl)); c.EndFigure(true);
            }
            return path;
        }

        internal static Geometry Mark(params Point[] points)
        {
            var path = new StreamGeometry();
            using (var c = path.Open())
            {
                c.BeginFigure(points[0], true);
                for (int i = 1; i < points.Length; i++) c.LineTo(points[i]);
                c.EndFigure(true);
            }
            return path;
        }

        internal static void Drop(DrawingContext context, double x, double y, bool hot)
        {
            var triangle = Mark(new Point(x, y - 3), new Point(x + 9, y - 3), new Point(x + 4.5, y + 3.5));
            context.DrawGeometry(hot ? Cyan : null, new Pen(Cyan, 1.3), triangle);
        }

        internal static TextBox Input(string hint, bool secret = false)
        {
            var input = new TextBox { Watermark = hint, FontFamily = Text, FontSize = Size, MinHeight = 0, MinWidth = 0,
                Padding = new Thickness(0), Background = Brushes.Transparent, Foreground = White, BorderThickness = new Thickness(0),
                CaretBrush = Cyan, SelectionBrush = DeepEdge, SelectionForegroundBrush = White, PasswordChar = secret ? '\u2022' : '\0',
                VerticalContentAlignment = VerticalAlignment.Center };
            foreach (string state in new[] { "", "PointerOver", "Focused", "Disabled" })
            {
                input.Resources["TextControlBackground" + state] = Brushes.Transparent;
                input.Resources["TextControlForeground" + state] = state == "Disabled" ? Faint : White;
                input.Resources["TextControlBorderBrush" + state] = Brushes.Transparent;
                input.Resources["TextControlPlaceholderForeground" + state] = Faint;
            }
            AutomationProperties.SetName(input, hint);
            return input;
        }
    }

    internal sealed class NeonPlate : Decorator
    {
        internal double Zoom = 1;
        internal bool Menu;
        internal double Scroll, View, Page;
        public override void Render(DrawingContext context)
        {
            double s = Zoom, w = Bounds.Width, h = Bounds.Height;
            var box = new Rect(0.5 * s, 0.5 * s, Math.Max(0, w - s - 1), Math.Max(0, h - s - 1));
            var shape = MainframeTheme.Cut(box, (Menu ? 10 : 16) * s, (Menu ? 4 : 6) * s, (Menu ? 10 : 16) * s, (Menu ? 4 : 6) * s);
            var wine = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Absolute), EndPoint = new RelativePoint(0, (Menu ? 90 : 210) * s, RelativeUnit.Absolute), SpreadMethod = GradientSpreadMethod.Pad };
            for (int i = 0; i < 9; i++)
            {
                double at = i / 8.0, fade = (1 - at) * (1 - at);
                wine.GradientStops.Add(new GradientStop(Color.FromRgb((byte)Math.Round((Menu ? 16 : 10) + (Menu ? 38 : 44) * fade), (byte)Math.Round((Menu ? 13 : 10) + (Menu ? 1 : 4) * fade), (byte)Math.Round((Menu ? 22 : 16) + (Menu ? 4 : 10) * fade)), at));
            }
            context.DrawGeometry(wine, null, shape);
            base.Render(context);
        }

        internal void Edge(DrawingContext context)
        {
            double s = Zoom, w = Bounds.Width, h = Bounds.Height;
            var box = new Rect(0.5 * s, 0.5 * s, Math.Max(0, w - s - 1), Math.Max(0, h - s - 1));
            double big = (Menu ? 10 : 16) * s, small = (Menu ? 4 : 6) * s, along = (Menu ? 30 : 64) * s, down = (Menu ? 18 : 34) * s;
            context.DrawGeometry(null, new Pen(MainframeTheme.Brim, Math.Max(1, 1.2 * s)), MainframeTheme.Cut(box, big, small, big, small));
            var red = new Pen(MainframeTheme.Hot, 2 * s);
            double l = box.Left + 0.6 * s, t = box.Top + 0.6 * s, r = box.Right - 0.6 * s, b = box.Bottom - 0.6 * s;
            context.DrawLine(red, new Point(l, t + big + down), new Point(l, t + big));
            context.DrawLine(red, new Point(l, t + big), new Point(l + big, t));
            context.DrawLine(red, new Point(l + big, t), new Point(l + big + along, t));
            context.DrawLine(red, new Point(r, b - big - down), new Point(r, b - big));
            context.DrawLine(red, new Point(r, b - big), new Point(r - big, b));
            context.DrawLine(red, new Point(r - big, b), new Point(r - big - along, b));
            if (Page <= View || View <= 0) return;
            double rail = h - 36 * s, thumb = rail * View / Page, at = 18 * s + (rail - thumb) * Scroll / (Page - View);
            context.DrawRectangle(MainframeTheme.Dash, null, new Rect(w - 8 * s, 18 * s, 2 * s, rail));
            context.DrawRectangle(MainframeTheme.Cyan, null, new Rect(w - 8 * s, at, 2 * s, thumb));
        }
    }

    internal sealed class NeonRim : Control
    {
        internal NeonPlate Plate;
        public override void Render(DrawingContext context) { Plate.Edge(context); }
    }

    internal sealed class NeonCaption : Control
    {
        private string text = "";
        internal string Text { get { return text; } set { value = value ?? ""; if (text == value) return; text = value; InvalidateVisual(); } }
        internal IBrush Ink = MainframeTheme.Rose;
        internal double Size = MainframeTheme.Size;
        internal bool Bold, Display, Ellipsis;
        internal TextAlignment Align;
        public override void Render(DrawingContext context) { MainframeTheme.Write(context, Text, new Rect(Bounds.Size), Ink, Size, Bold, Align, Display); }
        internal string TextFits()
        {
            var text = MainframeTheme.Words(Ellipsis ? MainframeTheme.Fitted(Text, Width, Size, Bold, Display) : Text, Size, Bold, Ink, Display);
            return text.Width > Width + 0.5 || text.Height > Height + 0.5 ? Text : null;
        }
    }

    internal sealed class NeonHead : Control
    {
        internal string Text;
        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(MainframeTheme.Band, null, new Rect(Bounds.Size));
            context.DrawRectangle(MainframeTheme.Rule, null, new Rect(0, 0, Bounds.Width, 1));
            context.DrawRectangle(MainframeTheme.Hot, null, new Rect(0, 7, 2.5, 11));
            MainframeTheme.Write(context, Text, new Rect(14, 5, Bounds.Width - 14, 19), MainframeTheme.White, 40.0 / 3, true);
        }
    }

    internal class NeonButton : Button
    {
        internal IBrush Accent = MainframeTheme.Rose;
        internal bool Loud, Picked, Chevron, Ellipsis;
        internal int Glyph;
        internal double TextSize = MainframeTheme.Size;
        // Heart puts a beating heart before its text, like Support's; Swell is the heart's size and Beat how lit its rim is.
        internal bool Heart;
        internal double Swell = 1, Beat;
        private static readonly Geometry HeartShape = Geometry.Parse("M6 10.4C6 10.4 0.8 7 0.8 3.8C0.8 2 2.2 0.8 3.7 0.8C4.8 0.8 5.6 1.5 6 2.3C6.4 1.5 7.2 0.8 8.3 0.8C9.8 0.8 11.2 2 11.2 3.8C11.2 7 6 10.4 6 10.4Z");
        internal string Text { get { return Content as string ?? ""; } set { if (Text == value) return; Content = value; InvalidateVisual(); } }
        internal NeonButton()
        {
            FontFamily = MainframeTheme.Text; FontSize = MainframeTheme.Size;
            MinWidth = MinHeight = 0; Padding = new Thickness(0); Cursor = new Cursor(StandardCursorType.Hand);
            Template = new FuncControlTemplate<Button>((owner, scope) => new Panel());
            PropertyChanged += (_, _) => InvalidateVisual();
        }
        internal void Press() { OnClick(); }
        // A click on Mac and Linux goes through Program.Guard, like her menu's, so a bug behind a button is written down
        // instead of ending her. Pickers' choices are buttons too.
        protected override void OnClick() { Program.Guard(() => base.OnClick()); }
        public override void Render(DrawingContext context)
        {
            bool hot = IsPointerOver, lit = hot || IsKeyboardFocusWithin;
            double w = Bounds.Width, h = Bounds.Height, x = w / 2, y = h / 2;
            var box = new Rect(0.5, 0.5, Math.Max(0, w - 1), Math.Max(0, h - 1));
            if (Glyph != 0)
            {
                if (hot) context.DrawGeometry(IsPressed ? MainframeTheme.Pressed : MainframeTheme.Hover, null, MainframeTheme.Cut(box, br: 4));
                var pen = new Pen(hot ? Glyph == 2 ? MainframeTheme.Hot : MainframeTheme.Cyan : MainframeTheme.Faint, 1.5);
                if (Glyph == 1) context.DrawLine(pen, new Point(x - 5, y), new Point(x + 5, y));
                else { context.DrawLine(pen, new Point(x - 4.5, y - 4.5), new Point(x + 4.5, y + 4.5)); context.DrawLine(pen, new Point(x - 4.5, y + 4.5), new Point(x + 4.5, y - 4.5)); }
                return;
            }
            IBrush face, rim, ink;
            if (!IsEnabled) { face = MainframeTheme.Field; rim = MainframeTheme.Brush("#341620"); ink = MainframeTheme.Brush("#705E68"); }
            else if (Picked) { face = hot ? MainframeTheme.Brush("#A0F8FB") : MainframeTheme.Cyan; rim = face; ink = MainframeTheme.OnInk; }
            else if (Accent == MainframeTheme.Cyan) { face = IsPressed ? MainframeTheme.Brush("#123E43") : hot ? MainframeTheme.Brush("#0D2D31") : MainframeTheme.Deep; rim = lit ? MainframeTheme.Cyan : Loud ? MainframeTheme.DeepEdge : MainframeTheme.Brush("#1C545C"); ink = MainframeTheme.Cyan; }
            else if (Accent == MainframeTheme.Hot) { face = IsPressed ? MainframeTheme.Brush("#48121E") : hot ? MainframeTheme.Brush("#36101C") : MainframeTheme.Brush("#220C16"); rim = lit ? MainframeTheme.Brush("#FF7C7A") : MainframeTheme.Hot; ink = MainframeTheme.Brush("#FFA6A2"); }
            else if (Heart) { face = IsPressed ? MainframeTheme.Pressed : hot ? MainframeTheme.Brush("#2E1222") : Toward(MainframeTheme.Tint("#200E18"), MainframeTheme.Tint("#361220"), Beat); rim = lit ? MainframeTheme.Rose : Toward(MainframeTheme.Tint("#9C2838"), MainframeTheme.Tint("#FF5D66"), Beat); ink = MainframeTheme.Brush(hot ? "#FFC2C5" : "#FF8A90"); }
            else { face = IsPressed ? MainframeTheme.Pressed : hot ? MainframeTheme.Hover : MainframeTheme.Field; rim = lit ? MainframeTheme.Hot : MainframeTheme.Edge; ink = Chevron ? MainframeTheme.Cyan : Accent; }
            context.DrawGeometry(face, new Pen(rim, 1), MainframeTheme.Cut(box));
            if (Chevron) MainframeTheme.Drop(context, w - 19, y, hot);
            if (Heart && IsEnabled)
            {
                // The heart and the text are centred together, 5 px apart.
                double wide = MainframeTheme.Words(Text, TextSize).Width, left = (w - (17 + wide)) / 2;
                using (context.PushTransform(Matrix.CreateTranslation(-6, -5.6) * Matrix.CreateScale(Swell, Swell) * Matrix.CreateTranslation(left + 6, y)))
                    context.DrawGeometry(MainframeTheme.Rose, null, HeartShape);
                MainframeTheme.Write(context, Text, new Rect(left + 17, 0, wide + 2, h), ink, TextSize);
                return;
            }
            MainframeTheme.Write(context, Text, Chevron ? new Rect(10, 0, Math.Max(0, w - 32), h) : new Rect(Bounds.Size), ink, TextSize, false, Chevron ? TextAlignment.Left : TextAlignment.Center);
        }
        private static IBrush Toward(Color from, Color to, double amount)
        {
            return new SolidColorBrush(Color.FromRgb((byte)(from.R + (to.R - from.R) * amount), (byte)(from.G + (to.G - from.G) * amount), (byte)(from.B + (to.B - from.B) * amount)));
        }
        internal virtual string TextFits()
        {
            double room = Width - (Chevron ? 32 : 8);
            string text = Ellipsis ? MainframeTheme.Fitted(Text, room, TextSize) : Text;
            return Glyph == 0 && MainframeTheme.Words(text, TextSize).Width > room + 0.5 ? Text : null;
        }
    }

    internal sealed class NeonSwitch : ToggleButton
    {
        internal string Text, Hint;
        internal int Switch = 172;
        private int over = 2;
        internal bool Checked { get { return IsChecked == true; } set { if (Checked != value) IsChecked = value; } }
        internal NeonSwitch()
        {
            MinWidth = MinHeight = 0; Cursor = new Cursor(StandardCursorType.Hand);
            Template = new FuncControlTemplate<ToggleButton>((owner, scope) => new Panel());
            PropertyChanged += (_, _) => InvalidateVisual();
        }
        internal void Flip() { OnClick(); }
        // Its clicks and its OFF and ON halves go through Program.Guard, like a button's.
        protected override void OnClick() { Program.Guard(() => base.OnClick()); }
        protected override void OnPointerMoved(PointerEventArgs e)
        {
            double x = e.GetPosition(this).X, start = Bounds.Width - Switch;
            over = x < start ? -1 : x < start + Switch / 2.0 ? 0 : 1; InvalidateVisual(); base.OnPointerMoved(e);
        }
        protected override void OnPointerExited(PointerEventArgs e) { over = 2; InvalidateVisual(); base.OnPointerExited(e); }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            double x = e.GetPosition(this).X, start = Bounds.Width - Switch;
            Focus();
            if (x >= start) { Program.Guard(() => Checked = x >= start + Switch / 2.0); e.Handled = true; }
            else base.OnPointerPressed(e);
        }
        public override void Render(DrawingContext context)
        {
            double start = Bounds.Width - Switch, half = (Switch - 4) / 2.0, h = Bounds.Height;
            var off = new Rect(start + 0.5, 0.5, half - 1, h - 1);
            var on = new Rect(start + half + 4.5, 0.5, half - 1, h - 1);
            IBrush offFill, offRim, offInk, onFill, onRim, onInk;
            if (Checked)
            {
                offFill = MainframeTheme.Brush(over == 0 ? "#32101C" : "#200C13"); offRim = MainframeTheme.Brush(over == 0 ? "#782432" : "#32121C"); offInk = MainframeTheme.Brush(over == 0 ? "#AA3C48" : "#541A26");
                onFill = onRim = MainframeTheme.Cyan; onInk = MainframeTheme.OnInk;
            }
            else
            {
                offFill = MainframeTheme.Brush("#9B322E"); offRim = MainframeTheme.Brush("#DE453F"); offInk = MainframeTheme.Brush("#FF9690");
                onFill = over == 1 ? MainframeTheme.Brush("#0C2E32") : MainframeTheme.Deep; onRim = over == 1 ? MainframeTheme.DeepEdge : MainframeTheme.Brush("#102C30"); onInk = MainframeTheme.Brush(over == 1 ? "#6ED2D8" : "#1C4448");
            }
            if (MainframeTheme.Mono)
            {
                offFill = onFill = MainframeTheme.Field;
                offRim = Checked ? (over == 0 ? MainframeTheme.Faint : MainframeTheme.Brim) : MainframeTheme.White;
                offInk = Checked ? (over == 0 ? MainframeTheme.White : MainframeTheme.Faint) : MainframeTheme.White;
                onRim = Checked ? MainframeTheme.Cyan : (over == 1 ? MainframeTheme.Faint : MainframeTheme.Brim);
                onInk = Checked ? MainframeTheme.OnInk : (over == 1 ? MainframeTheme.White : MainframeTheme.Faint);
                if (Checked) onFill = MainframeTheme.Cyan;
            }
            context.DrawGeometry(offFill, new Pen(offRim, 1), MainframeTheme.Cut(off, br: 0, bl: 5));
            context.DrawGeometry(onFill, new Pen(onRim, 1), MainframeTheme.Cut(on));
            MainframeTheme.Write(context, "OFF", off, offInk, 35.0 / 3, true, TextAlignment.Center);
            MainframeTheme.Write(context, "ON", on, onInk, 35.0 / 3, true, TextAlignment.Center);
            if (IsKeyboardFocusWithin) context.DrawGeometry(null, new Pen(MainframeTheme.Cyan, 1), MainframeTheme.Cut(new Rect(start - 2, 0.5, Switch + 1.5, h - 1)));
            double room = start - 10;
            MainframeTheme.Write(context, Text, new Rect(0, 0, room, h), MainframeTheme.Rose);
            double after = MainframeTheme.Words(Text).Width + 10;
            if (Hint != null && after + MainframeTheme.Words(Hint, 34.0 / 3).Width <= room)
                MainframeTheme.Write(context, Hint, new Rect(after, 0, room - after, h), MainframeTheme.Faint, 34.0 / 3);
        }
        internal string TextFits() { return MainframeTheme.Words(Text).Width > Width - Switch - 10 + 0.5 ? Text : null; }
    }

    internal sealed class NeonRung : NeonButton
    {
        internal string Hint, Flag;
        internal bool Checked;
        public override void Render(DrawingContext context)
        {
            double h = Bounds.Height, w = Bounds.Width;
            bool lit = IsPointerOver || IsKeyboardFocusWithin;
            if (lit) context.DrawGeometry(MainframeTheme.Brush("#185EF0F6"), new Pen(MainframeTheme.Brush("#BE5EF0F6"), 1), MainframeTheme.Cut(new Rect(0.5, 0.5, w - 1, h - 1)));
            var diamond = MainframeTheme.Mark(new Point(12, h / 2 - 4.5), new Point(16.5, h / 2), new Point(12, h / 2 + 4.5), new Point(7.5, h / 2));
            context.DrawGeometry(Checked ? MainframeTheme.Cyan : null, Checked ? null : new Pen(lit ? MainframeTheme.Cyan : MainframeTheme.Faint, 1.2), diamond);
            MainframeTheme.Write(context, Text, new Rect(24, 0, w - 24, h), Checked ? MainframeTheme.Cyan : MainframeTheme.White, bold: true);
            double after = 32 + MainframeTheme.Words(Text, bold: true).Width;
            if (Hint != null) MainframeTheme.Write(context, Hint, new Rect(after, 0, Math.Max(0, w - after), h), MainframeTheme.Faint, 12);
            if (Flag != null) MainframeTheme.Write(context, Flag, new Rect(0, 0, w - 6, h), MainframeTheme.Yellow, 12, align: TextAlignment.Right);
        }
        internal override string TextFits()
        {
            double end = 32 + MainframeTheme.Words(Text, bold: true).Width + MainframeTheme.Words(Hint, 12).Width;
            if (Flag != null && end + 8 > Width - 6 - MainframeTheme.Words(Flag, 12).Width) return Text + " / " + Flag;
            return end > Width + 0.5 ? Text + " / " + Hint : null;
        }
    }

    internal sealed class NeonChoice : NeonButton
    {
        internal bool Checked;
        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            bool hot = IsPointerOver || IsKeyboardFocusWithin;
            context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            if (hot)
            {
                context.DrawGeometry(MainframeTheme.Brush("#301220"), null, MainframeTheme.Cut(new Rect(1, 1, w - 2, h - 2)));
                context.DrawRectangle(MainframeTheme.Hot, null, new Rect(1, 5, 2.5, h - 10));
            }
            if (Checked)
            {
                var box = new Rect(9, h / 2 - 4.5, 9, 9);
                context.DrawGeometry(MainframeTheme.Cyan, null, MainframeTheme.Cut(box, br: 3));
                var tick = new Pen(MainframeTheme.OnInk, 1.6);
                context.DrawLine(tick, new Point(11, h / 2), new Point(12.8, h / 2 + 2));
                context.DrawLine(tick, new Point(12.8, h / 2 + 2), new Point(16, h / 2 - 2));
            }
            MainframeTheme.Write(context, Text, new Rect(28, 0, w - 38, h), hot ? MainframeTheme.Cyan : MainframeTheme.White);
        }
    }

    internal sealed class NeonSlider : Control
    {
        internal readonly string[] Choices;
        internal event Action<int> Changed;
        internal event Action Released;
        private int value, settled;
        private bool sliding, keying;
        private double pointer = -1;
        internal string Readout;
        internal int TickEvery = 1;
        internal bool Deferred;
        internal bool Editing { get { return sliding || keying; } }
        internal int Value { get { return value; } set { int next = Math.Clamp(value, 0, Choices.Length - 1); if (this.value == next) return; this.value = next; settled = next; InvalidateVisual(); } }
        internal bool Stepper { get { return Choices.Length <= 7; } }
        internal string Shown { get { return Readout ?? Choices[value]; } }
        internal NeonSlider(string name, string[] choices)
        {
            Choices = choices; Focusable = true; Cursor = new Cursor(StandardCursorType.Hand); AutomationProperties.SetName(this, name);
        }
        private void Pick(int next)
        {
            next = Math.Clamp(next, 0, Choices.Length - 1);
            if (next == value) return;
            // What it changes runs through Program.Guard, like a button's click, whether it was a drag, a key or a click.
            value = next; InvalidateVisual();
            Action<int> changed = Changed;
            if (changed != null) Program.Guard(() => changed(next));
        }
        private void Settle()
        {
            if (settled == value) return;
            settled = value;
            Action released = Released;
            if (released != null) Program.Guard(released);
        }
        internal void Slide(int next) { Pick(next); Settle(); }
        private int At(double x) { return (int)Math.Round((x - 6) / Math.Max(1, Bounds.Width - 12) * (Choices.Length - 1)); }
        private int Zone(double x) { double reach = Math.Min(Bounds.Width / 3, 36); return x < reach ? -1 : x > Bounds.Width - reach ? 1 : 0; }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            Focus(); sliding = true; e.Pointer.Capture(this); pointer = e.GetPosition(this).X;
            Pick(Stepper ? value + Zone(pointer) : At(pointer)); e.Handled = true;
        }
        protected override void OnPointerMoved(PointerEventArgs e) { pointer = e.GetPosition(this).X; if (sliding && !Stepper) Pick(At(pointer)); InvalidateVisual(); }
        protected override void OnPointerExited(PointerEventArgs e) { pointer = -1; InvalidateVisual(); }
        protected override void OnPointerReleased(PointerReleasedEventArgs e) { sliding = false; e.Pointer.Capture(null); Settle(); e.Handled = true; }
        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e) { sliding = false; Settle(); }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            keying = e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down || e.Key == Key.Home || e.Key == Key.End;
            if (e.Key == Key.Left || e.Key == Key.Down) Pick(value - 1);
            else if (e.Key == Key.Right || e.Key == Key.Up) Pick(value + 1);
            else if (e.Key == Key.Home) Pick(0);
            else if (e.Key == Key.End) Pick(Choices.Length - 1);
            else { base.OnKeyDown(e); return; }
            e.Handled = true;
        }
        protected override void OnKeyUp(KeyEventArgs e) { keying = false; Settle(); base.OnKeyUp(e); }
        protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e) { keying = false; Settle(); InvalidateVisual(); base.OnLostFocus(e); }
        private void Arrow(DrawingContext context, double tip, int way, bool open)
        {
            double y = Bounds.Height / 2 - 1.5, back = tip - way * 7;
            var triangle = MainframeTheme.Mark(new Point(tip, y), new Point(back, y - 5.5), new Point(back, y + 5.5));
            IBrush ink = open ? MainframeTheme.Cyan : MainframeTheme.Dash;
            context.DrawGeometry(open && pointer >= 0 && Zone(pointer) == way ? ink : null, new Pen(ink, 1.3), triangle);
        }
        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            IBrush rim = IsKeyboardFocusWithin ? MainframeTheme.Cyan : IsPointerOver || sliding ? MainframeTheme.Brush("#9C2838") : MainframeTheme.Edge;
            var box = new Rect(0.5, 0.5, w - 1, h - 1);
            if (Stepper)
            {
                context.DrawGeometry(MainframeTheme.Field, new Pen(rim, 1), MainframeTheme.Cut(box, tr: 5));
                Arrow(context, 11, -1, value > 0); Arrow(context, w - 11, 1, value < Choices.Length - 1);
                MainframeTheme.Write(context, Shown, new Rect(26, 0, w - 52, h - 4), MainframeTheme.Cyan, align: TextAlignment.Center);
                double dash = Choices.Length <= 4 ? 11 : 8, left = w / 2 - (Choices.Length * dash + (Choices.Length - 1) * 3) / 2;
                for (int i = 0; i < Choices.Length; i++) context.DrawRectangle(i == value ? MainframeTheme.Lit : MainframeTheme.Dash, null, new Rect(left + i * (dash + 3), h - 5.5, dash, 1.5));
                return;
            }
            double travel = w - 12, knob = 1 + travel * value / Math.Max(1, Choices.Length - 1);
            var shape = MainframeTheme.Cut(box);
            using (context.PushGeometryClip(shape))
            {
                context.DrawRectangle(MainframeTheme.Field, null, box);
                if (knob > 1) context.DrawRectangle(MainframeTheme.Brush("#2A0E19"), null, new Rect(1, 1, knob - 1, h - 2));
                for (int i = 0; i < Choices.Length; i += Math.Max(1, TickEvery)) context.DrawRectangle(MainframeTheme.Dash, null, new Rect(5.5 + travel * i / Math.Max(1, Choices.Length - 1), h - 5, 1, 3));
                context.DrawGeometry(IsPointerOver || sliding || IsKeyboardFocusWithin ? MainframeTheme.Lit : MainframeTheme.Hot, null, MainframeTheme.Cut(new Rect(knob, 1, 10, h - 2), br: 4));
                MainframeTheme.Write(context, Shown, new Rect(Bounds.Size), MainframeTheme.Cyan, align: TextAlignment.Center);
                using (context.PushClip(new Rect(knob, 0, 10, h))) MainframeTheme.Write(context, Shown, new Rect(Bounds.Size), MainframeTheme.Brush("#28060C"), align: TextAlignment.Center);
            }
            context.DrawGeometry(null, new Pen(rim, 1), shape);
        }
        internal string TextFits()
        {
            foreach (string text in Choices) if (MainframeTheme.Words(text).Width > Width - (Stepper ? 52 : 4) + 0.5) return text;
            return null;
        }
    }

    internal sealed class NeonEntry : Decorator
    {
        internal readonly TextBox Box;
        internal bool Chevron;
        internal event Action Drop;
        internal string Cue { get { return Box.Watermark as string; } set { Box.Watermark = value; } }
        internal NeonEntry(string cue, bool secret = false)
        {
            Box = MainframeTheme.Input(cue, secret); Child = Box; Padding = new Thickness(10, 1);
            Box.PropertyChanged += (_, _) => InvalidateVisual();
        }
        protected override Size ArrangeOverride(Size finalSize)
        {
            Box.Arrange(new Rect(10, 1, Math.Max(1, finalSize.Width - (Chevron ? 38 : 20)), Math.Max(1, finalSize.Height - 2)));
            return finalSize;
        }
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (Chevron && e.GetPosition(this).X >= Bounds.Width - 28 && e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                Action drop = Drop;
                if (drop != null) Program.Guard(drop);
                e.Handled = true;
            }
            else base.OnPointerPressed(e);
        }
        public override void Render(DrawingContext context)
        {
            context.DrawGeometry(MainframeTheme.Well, new Pen(Box.IsKeyboardFocusWithin ? MainframeTheme.Cyan : MainframeTheme.WellEdge, 1), MainframeTheme.Cut(new Rect(0.5, 0.5, Bounds.Width - 1, Bounds.Height - 1)));
            if (Chevron) MainframeTheme.Drop(context, Bounds.Width - 19, Bounds.Height / 2, IsPointerOver);
        }
        internal string TextFits() { return MainframeTheme.Words(Cue).Width > Width - (Chevron ? 38 : 20) + 0.5 ? Cue : null; }
    }

    internal sealed class NeonPicker : NeonButton
    {
        private Popup popup;
        private string[] choices = Array.Empty<string>();
        private int selected;
        internal bool Check = true;
        internal event Action<int> Changed;
        internal string[] Choices { get { return choices; } }
        internal int Selected { get { return selected; } set { selected = Math.Clamp(value, 0, Math.Max(0, choices.Length - 1)); Text = choices.Length == 0 ? "None" : choices[selected]; } }
        internal NeonPicker() { Chevron = true; Click += (_, _) => Open(); }
        internal void Options(string[] options)
        {
            if (string.Join("\n", choices) == string.Join("\n", options)) return;
            CloseMenu(); choices = options; Selected = selected;
        }
        // A bug behind a choice is written down by Program.Guard, and its menu still closes.
        internal void Pick(int at)
        {
            if (at < 0 || at >= choices.Length) return;
            Selected = at;
            Action<int> changed = Changed;
            try { if (changed != null) Program.Guard(() => changed(at)); }
            finally { CloseMenu(); }
        }
        internal void CloseMenu() { if (popup != null) { popup.IsOpen = false; popup = null; } }
        private void Open() { OpenAt(this); }
        internal void OpenAt(Control target)
        {
            CloseMenu();
            if (choices.Length == 0) return;
            var rows = new StackPanel();
            var buttons = new List<NeonButton>();
            double wide = Math.Max(Width, 150);
            var top = TopLevel.GetTopLevel(target);
            double screen = top is Window window ? (window.Screens.ScreenFromWindow(window)?.WorkingArea.Width ?? 1280) / window.RenderScaling : 1280;
            foreach (string choice in choices) wide = Math.Max(wide, MainframeTheme.Words(choice).Width + 46);
            wide = Math.Min(wide, screen - 32);
            for (int i = 0; i < choices.Length; i++)
            {
                int at = i;
                var row = new NeonChoice { Text = choices[i], Checked = Check && i == selected, Width = wide, Height = 26 };
                AutomationProperties.SetName(row, choices[i]);
                row.Click += (_, _) => Pick(at);
                row.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Down || e.Key == Key.Up) { buttons[(at + (e.Key == Key.Down ? 1 : buttons.Count - 1)) % buttons.Count].Focus(); e.Handled = true; }
                    if (e.Key == Key.Escape) { CloseMenu(); Focus(); e.Handled = true; }
                };
                buttons.Add(row); rows.Children.Add(row);
            }
            var scroll = new ScrollViewer { Content = rows, MaxHeight = 400, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
            var plate = new NeonPlate { Menu = true, Child = new Border { Padding = new Thickness(4), Child = scroll } };
            var surface = new Grid(); surface.Children.Add(plate); surface.Children.Add(new NeonRim { Plate = plate, IsHitTestVisible = false });
            surface.SizeChanged += (_, _) => surface.Clip = MainframeTheme.Cut(new Rect(surface.Bounds.Size), 10, 4, 10, 4);
            popup = new Popup { PlacementTarget = target, Placement = PlacementMode.Bottom, IsLightDismissEnabled = true,
                Child = surface };
            popup.IsOpen = true;
            buttons[selected].Focus(); buttons[selected].BringIntoView();
        }
        internal override string TextFits()
        {
            if (Ellipsis) return MainframeTheme.Words(MainframeTheme.Fitted(Text, Width - 32)).Width > Width - 32 + 0.5 ? Text : null;
            foreach (string choice in choices) if (MainframeTheme.Words(choice).Width > Width - 32 + 0.5) return choice;
            return null;
        }
    }
}
