using System;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace CapriDesktop
{
    internal static class MainframeTheme
    {
        internal static readonly IBrush Ground = Brush("#0A0A10"), Glow = Brush("#360E1A"), Band = Brush("#100C14"), Rule = Brush("#2C7A86");
        internal static readonly IBrush Rose = Brush("#FF5D66"), White = Brush("#ECECF1"), Faint = Brush("#92828C"), Field = Brush("#11101C");
        internal static readonly IBrush Hover = Brush("#1E1424"), Pressed = Brush("#2E1626"), Edge = Brush("#5C1824"), Hot = Brush("#E84246");
        internal static readonly IBrush Cyan = Brush("#5EF0F6"), Deep = Brush("#091E21"), DeepEdge = Brush("#287884"), OnInk = Brush("#060E12");
        internal static readonly IBrush Dash = Brush("#4E1C28"), Lit = Brush("#F55A56"), Brim = Brush("#781E2C"), Well = Brush("#0C0B14"), WellEdge = Brush("#481C28");
        internal static readonly FontFamily Text = new FontFamily("avares://Capri/Assets#Barlow Semi Condensed");
        internal static readonly FontFamily Display = new FontFamily("avares://Capri/Assets#Barlow Condensed");

        private static IBrush Brush(string value) { return new SolidColorBrush(Color.Parse(value)); }
        internal static TextBlock Label(string text, IBrush ink = null, double size = 14)
        {
            return new TextBlock { Text = text, FontFamily = Text, FontSize = size, Foreground = ink ?? Rose, VerticalAlignment = VerticalAlignment.Center };
        }

        internal static NeonButton Button(string text, Action action, string name = null)
        {
            var button = new NeonButton { Content = text, MinHeight = 26, Padding = new Thickness(9, 3), HorizontalContentAlignment = HorizontalAlignment.Center };
            AutomationProperties.SetName(button, name ?? text);
            button.Click += (_, _) => action();
            return button;
        }

        internal static TextBox Input(string hint, bool secret = false)
        {
            var input = new TextBox { Watermark = hint, FontFamily = Text, FontSize = 14, MinHeight = 28, Padding = new Thickness(7, 3),
                Background = Field, Foreground = Cyan, BorderBrush = Edge, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(0), PasswordChar = secret ? '\u2022' : '\0' };
            AutomationProperties.SetName(input, hint);
            return input;
        }

        internal static Geometry Cut(Rect b, double big, double small)
        {
            var path = new StreamGeometry();
            using (var c = path.Open())
            {
                c.BeginFigure(new Point(b.X + big, b.Y), true);
                c.LineTo(new Point(b.Right - small, b.Y)); c.LineTo(new Point(b.Right, b.Y + small));
                c.LineTo(new Point(b.Right, b.Bottom - big)); c.LineTo(new Point(b.Right - big, b.Bottom));
                c.LineTo(new Point(b.X + small, b.Bottom)); c.LineTo(new Point(b.X, b.Bottom - small));
                c.LineTo(new Point(b.X, b.Y + big)); c.EndFigure(true);
            }
            return path;
        }
    }

    internal sealed class NeonPlate : Decorator
    {
        internal IBrush Fill = MainframeTheme.Field, Stroke = MainframeTheme.Edge;
        internal bool Frame;
        public override void Render(DrawingContext context)
        {
            var box = new Rect(0.7, 0.7, Math.Max(0, Bounds.Width - 1.4), Math.Max(0, Bounds.Height - 1.4));
            var shape = MainframeTheme.Cut(box, Frame ? 16 : 5, Frame ? 6 : 3);
            context.DrawGeometry(Fill, new Pen(Stroke, 1), shape);
            if (Frame)
            {
                var hot = new Pen(MainframeTheme.Hot, 2);
                context.DrawLine(hot, new Point(1, 36), new Point(1, 17));
                context.DrawLine(hot, new Point(1, 17), new Point(17, 1));
                context.DrawLine(hot, new Point(17, 1), new Point(70, 1));
                context.DrawLine(hot, new Point(box.Right - 70, box.Bottom), new Point(box.Right - 16, box.Bottom));
                context.DrawLine(hot, new Point(box.Right - 16, box.Bottom), new Point(box.Right, box.Bottom - 16));
                context.DrawLine(hot, new Point(box.Right, box.Bottom - 16), new Point(box.Right, box.Bottom - 36));
            }
            base.Render(context);
        }
    }

    internal sealed class NeonButton : Button
    {
        private NeonPlate plate;
        internal bool Accent;
        internal NeonButton()
        {
            FontFamily = MainframeTheme.Text; FontSize = 14; FontWeight = FontWeight.SemiBold;
            Foreground = MainframeTheme.Rose;
            Template = new FuncControlTemplate<Button>((owner, scope) =>
            {
                plate = new NeonPlate { Padding = Padding, Child = new ContentPresenter
                {
                    [!ContentPresenter.ContentProperty] = owner[!ContentProperty],
                    HorizontalContentAlignment = HorizontalContentAlignment, VerticalContentAlignment = VerticalAlignment.Center
                } };
                return plate;
            });
            PropertyChanged += (_, e) =>
            {
                if (plate == null) return;
                plate.Fill = IsPressed ? MainframeTheme.Pressed : IsPointerOver ? MainframeTheme.Hover : Accent ? MainframeTheme.Deep : MainframeTheme.Field;
                plate.Stroke = IsKeyboardFocusWithin ? MainframeTheme.Cyan : Accent ? MainframeTheme.DeepEdge : MainframeTheme.Edge;
                plate.Opacity = IsEnabled ? 1 : 0.45; plate.InvalidateVisual();
            };
        }
    }

    internal sealed class NeonSwitch : ToggleButton
    {
        private NeonPlate plate;
        private readonly TextBlock state = MainframeTheme.Label("OFF", MainframeTheme.Faint, 13);
        internal NeonSwitch(string name)
        {
            MinHeight = 23; MinWidth = 78; HorizontalContentAlignment = HorizontalAlignment.Center;
            AutomationProperties.SetName(this, name);
            Template = new FuncControlTemplate<ToggleButton>((owner, scope) => plate = new NeonPlate { Padding = new Thickness(8, 2), Child = state });
            PropertyChanged += (_, e) =>
            {
                state.Text = IsChecked == true ? "ON  ▰" : "OFF  ▱"; state.Foreground = IsChecked == true ? MainframeTheme.Cyan : MainframeTheme.Faint;
                if (plate == null) return;
                plate.Fill = IsPointerOver ? MainframeTheme.Hover : MainframeTheme.Field;
                plate.Stroke = IsKeyboardFocusWithin ? MainframeTheme.Cyan : IsChecked == true ? MainframeTheme.DeepEdge : MainframeTheme.Edge;
                plate.Opacity = IsEnabled ? 1 : 0.45; plate.InvalidateVisual();
            };
        }
    }

    internal sealed class NeonPicker : UserControl
    {
        private readonly NeonButton value;
        private readonly Action<int> changed;
        private string[] choices;
        private int selected;
        internal int Selected { get { return selected; } set { selected = Math.Clamp(value, 0, Math.Max(0, choices.Length - 1)); this.value.Content = choices.Length == 0 ? "None available" : choices[selected]; } }
        internal NeonPicker(string name, string[] options, Action<int> change)
        {
            choices = options; changed = change;
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("22,*,22"), Height = 25 };
            var left = MainframeTheme.Button("‹", () => Pick(selected - 1), name + ": previous");
            var right = MainframeTheme.Button("›", () => Pick(selected + 1), name + ": next");
            value = MainframeTheme.Button("", Open, name); value.Foreground = MainframeTheme.Cyan; value.Padding = new Thickness(3, 1);
            left.Padding = right.Padding = new Thickness(0); left.MinHeight = right.MinHeight = value.MinHeight = 23;
            grid.Children.Add(left); grid.Children.Add(value); grid.Children.Add(right); Grid.SetColumn(value, 1); Grid.SetColumn(right, 2); Content = grid; Selected = 0;
            KeyDown += (_, e) => { if (e.Key == Key.Left || e.Key == Key.Right) { Pick(selected + (e.Key == Key.Right ? 1 : -1)); e.Handled = true; } };
        }
        internal void Choices(string[] options) { choices = options; Selected = selected; }
        private void Pick(int at) { if (choices.Length == 0) return; Selected = (at + choices.Length) % choices.Length; changed(Selected); }
        private void Open()
        {
            var menu = new ContextMenu { Background = MainframeTheme.Ground, Foreground = MainframeTheme.Cyan, BorderBrush = MainframeTheme.Brim, BorderThickness = new Thickness(1), FontFamily = MainframeTheme.Text };
            for (int i = 0; i < choices.Length; i++)
            {
                int at = i; var item = new MenuItem { Header = choices[i], ToggleType = MenuItemToggleType.Radio, IsChecked = i == selected, FontFamily = MainframeTheme.Text, Foreground = MainframeTheme.Cyan };
                item.Click += (_, _) => Pick(at); menu.Items.Add(item);
            }
            menu.Open(value);
        }
    }

    internal sealed class NeonSlider : UserControl
    {
        internal readonly Slider Slider;
        private readonly TextBlock readout = MainframeTheme.Label("", MainframeTheme.Cyan, 13);
        private readonly string[] names;
        private bool syncing, touched;
        internal int Value { get { return (int)Math.Round(Slider.Value); } set { syncing = true; Slider.Value = Math.Clamp(value, 0, names.Length - 1); Read(); syncing = false; } }
        internal event Action<int> Changed;
        internal event Action Released;
        internal NeonSlider(string name, string[] labels)
        {
            names = labels; var grid = new Grid { RowDefinitions = new RowDefinitions("17,9"), Height = 26 };
            readout.HorizontalAlignment = HorizontalAlignment.Center;
            Slider = new Slider { Minimum = 0, Maximum = labels.Length - 1, TickFrequency = 1, IsSnapToTickEnabled = true, SmallChange = 1, LargeChange = 1, Height = 9, MinHeight = 0 };
            AutomationProperties.SetName(Slider, name);
            Slider.Template = new FuncControlTemplate<Slider>((owner, scope) =>
            {
                var track = new Track { Name = "PART_Track", Height = 9, Orientation = Orientation.Horizontal,
                    [!Track.MinimumProperty] = owner[!RangeBase.MinimumProperty], [!Track.MaximumProperty] = owner[!RangeBase.MaximumProperty], [!Track.ValueProperty] = owner[!RangeBase.ValueProperty] };
                scope.Register("PART_Track", track);
                var decrease = new Button { Name = "PART_DecreaseButton", Focusable = false, MinHeight = 0, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(0), Template = new FuncControlTemplate<Button>((button, namescope) => new Border { Background = Brushes.Transparent }) };
                var increase = new Button { Name = "PART_IncreaseButton", Focusable = false, MinHeight = 0, MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch, Padding = new Thickness(0), Template = new FuncControlTemplate<Button>((button, namescope) => new Border { Background = Brushes.Transparent }) };
                scope.Register("PART_DecreaseButton", decrease); scope.Register("PART_IncreaseButton", increase);
                track.DecreaseButton = decrease; track.IncreaseButton = increase;
                track.Thumb = new Thumb { Width = 12, Height = 9, Template = new FuncControlTemplate<Thumb>((thumb, namescope) => new Border { Background = MainframeTheme.Hot }) };
                var back = new Grid(); back.Children.Add(new Border { Height = 2, VerticalAlignment = VerticalAlignment.Center, Background = MainframeTheme.Dash }); back.Children.Add(track); return back;
            });
            Slider.ValueChanged += (_, _) => { Read(); if (!syncing) Changed?.Invoke(Value); };
            Slider.AddHandler(PointerPressedEvent, (_, _) => touched = true, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Slider.AddHandler(PointerReleasedEvent, (_, _) => { if (touched) { touched = false; Released?.Invoke(); } }, Avalonia.Interactivity.RoutingStrategies.Bubble | Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Slider.KeyUp += (_, e) => { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) Released?.Invoke(); };
            Slider.PointerCaptureLost += (_, _) => { if (touched) { touched = false; Released?.Invoke(); } };
            Slider.GotFocus += (_, _) => readout.Foreground = MainframeTheme.White; Slider.LostFocus += (_, _) => readout.Foreground = MainframeTheme.Cyan;
            grid.Children.Add(readout); grid.Children.Add(Slider); Grid.SetRow(Slider, 1); Content = grid; Read();
        }
        private void Read() { readout.Text = names[Math.Clamp(Value, 0, names.Length - 1)]; }
    }
}
