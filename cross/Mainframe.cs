using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal class MainframeWindow : Window
    {
        private readonly Canvas header = new Canvas { Height = 50, Background = Brushes.Transparent };
        private readonly LayoutTransformControl headerZoom;
        private readonly NeonPlate headerPlate;
        internal readonly Canvas Page = new Canvas { Background = Brushes.Transparent };
        internal readonly LayoutTransformControl Zoomed;
        internal readonly ScrollViewer Scroll;
        private readonly NeonPlate plate;
        private readonly NeonRim rim;
        private readonly DispatcherTimer glide = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
        private readonly Stopwatch clock = new Stopwatch();
        private Vector from, to;
        private PixelRect lastArea;
        private double lastDpi;
        private bool fitting;
        private Control keyed;
        private Key pageHeld = Key.None;
        protected bool Steered;
        private sealed class Stop
        {
            internal Control Control;
            internal NeonHead Head;
            internal int Section, Row;
            internal double Left, Middle;
        }
        internal double Zoom { get; private set; } = 1;
        internal double PageHeight { get { return Page.Height; } }
        internal bool Gliding { get { return glide.IsEnabled; } }

        internal MainframeWindow(string title)
        {
            Title = "Capri " + title; NativeSurface.Register(this, NativeSurfaceRole.Mainframe); SystemDecorations = SystemDecorations.None; CanResize = false;
            Background = Brushes.Transparent; TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            FontFamily = MainframeTheme.Text; FontSize = MainframeTheme.Size; Foreground = MainframeTheme.White;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Zoomed = new LayoutTransformControl { Child = Page };
            Scroll = new ScrollViewer { Content = Zoomed, HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
            plate = new NeonPlate { Child = Scroll };
            rim = new NeonRim { Plate = plate, IsHitTestVisible = false };
            headerZoom = new LayoutTransformControl { Child = header, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
            headerPlate = new NeonPlate { Child = headerZoom, IsVisible = false };
            var surface = new Grid(); surface.Children.Add(plate); surface.Children.Add(headerPlate); surface.Children.Add(rim); Content = surface;
            header.PointerPressed += (_, e) => Program.Guard(() => { if (e.GetCurrentPoint(header).Properties.IsLeftButtonPressed && (e.Source == header || e.Source is NeonCaption)) BeginMoveDrag(e); });
            Scroll.ScrollChanged += (_, _) => { plate.Scroll = Scroll.Offset.Y / Zoom; plate.View = Scroll.Viewport.Height / Zoom; rim.InvalidateVisual(); };
            // Her buttons, switches, sliders and pickers go through Program.Guard by themselves (MainframeTheme), and the window's
            // own scrolling, keys, dragging and boxes do here, so a bug in Mainframe is written down instead of ending her.
            AddHandler(PointerWheelChangedEvent, (_, e) =>
            {
                e.Handled = true;
                Program.Guard(() =>
                {
                    Vector step = Wheel(e.Delta, e.KeyModifiers) * 36 * Zoom;
                    if (Precise(e.Delta)) { glide.Stop(); to = Clamped(Scroll.Offset - step); Scroll.Offset = to; }
                    else GlideTo((glide.IsEnabled ? to : Scroll.Offset) - step);
                });
            }, RoutingStrategies.Tunnel);
            glide.Tick += (_, _) => Program.Guard(() =>
            {
                double t = Math.Min(1, clock.Elapsed.TotalMilliseconds / 180), eased = 1 - Math.Pow(1 - t, 3);
                // A glide that fails still ends, so its timer can't spin forever.
                try { Scroll.Offset = from + (to - from) * eased; }
                finally { if (t >= 1) glide.Stop(); }
            });
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Program.Guard(Back); } };
            AddHandler(KeyDownEvent, (_, e) => { if (Steered && !e.Handled) Program.Guard(() => Steer(e)); }, RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, (_, _) => Program.Guard(Unmark), RoutingStrategies.Tunnel);
            AddHandler(KeyUpEvent, (_, _) => { pageHeld = Key.None; }, RoutingStrategies.Tunnel);
            Deactivated += (_, _) => { pageHeld = Key.None; };
            Page.PointerPressed += (_, e) => Program.Guard(() => { if (e.GetPosition(Page).Y < 50 && e.GetCurrentPoint(Page).Properties.IsLeftButtonPressed && (e.Source == Page || e.Source is NeonCaption)) BeginMoveDrag(e); });
            AddHandler(GotFocusEvent, (_, e) => { if (e.Source is Control control) control.BringIntoView(); });
            SizeChanged += (_, _) => { surface.Clip = MainframeTheme.Cut(new Rect(Bounds.Size), 16 * Zoom, 6 * Zoom, 16 * Zoom, 6 * Zoom); rim.InvalidateVisual(); };
            Closed += (_, _) => glide.Stop();
            Opened += (_, _) => Program.Guard(ScreenChanged);
            PositionChanged += (_, _) => Program.Guard(ScreenChanged);
            ScalingChanged += (_, _) => Program.Guard(ScreenChanged);
        }

        protected virtual void Back() { Close(); }
        protected virtual bool PageKey(Key key) { return false; }
        private static bool Steerable(Control control) { return control is NeonButton || control is NeonSwitch || control is NeonSlider || control is NeonEntry; }
        private List<Stop> Stops()
        {
            var heads = new List<NeonHead>();
            var stops = new List<Stop>();
            foreach (Canvas canvas in new[] { header, Page })
                foreach (Control control in canvas.Children)
                {
                    if (!control.IsVisible || double.IsNaN(Canvas.GetLeft(control)) || double.IsNaN(Canvas.GetTop(control)) || double.IsNaN(control.Height)) continue;
                    if (control is NeonHead head) { heads.Add(head); continue; }
                    if (control.IsEnabled && Steerable(control)) stops.Add(new Stop { Control = control, Section = -1, Left = Canvas.GetLeft(control), Middle = Canvas.GetTop(control) + control.Height / 2 });
                }
            heads.Sort((one, other) => Column(one) != Column(other) ? Column(one).CompareTo(Column(other)) : Canvas.GetTop(one).CompareTo(Canvas.GetTop(other)));
            foreach (Stop stop in stops)
                for (int i = 0; i < heads.Count; i++)
                    if (Column(heads[i]) == Column(stop.Control) && Canvas.GetTop(heads[i]) <= stop.Middle) { stop.Section = i; stop.Head = heads[i]; }
            stops.Sort((one, other) => one.Section != other.Section ? one.Section.CompareTo(other.Section) : one.Middle != other.Middle ? one.Middle.CompareTo(other.Middle) : one.Left.CompareTo(other.Left));
            int row = 0;
            double anchor = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                if (i == 0 || stops[i].Section != stops[i - 1].Section || stops[i].Middle - anchor > 10) { row++; anchor = stops[i].Middle; }
                stops[i].Row = row;
            }
            stops.Sort((one, other) => one.Row != other.Row ? one.Row.CompareTo(other.Row) : one.Left.CompareTo(other.Left));
            return stops;
        }
        private static int Column(Control control) { return (int)(Canvas.GetLeft(control) / 380); }
        private Control Held()
        {
            for (StyledElement at = FocusManager?.GetFocusedElement() as StyledElement; at != null; at = at.Parent)
                if (at.Parent == Page || at.Parent == header) return at as Control;
            return null;
        }
        private static int At(List<Stop> stops, Control control)
        {
            for (int i = 0; i < stops.Count; i++) if (stops[i].Control == control) return i;
            return -1;
        }
        internal bool Marks(Control control) { return keyed == control && control.IsKeyboardFocusWithin; }
        private void Unmark()
        {
            Control was = keyed;
            keyed = null;
            if (was != null) was.InvalidateVisual();
        }
        protected void Hold(Control control)
        {
            Unmark();
            keyed = control;
            Control target = control is NeonEntry entry ? entry.Box : control;
            target.Focus();
            control.InvalidateVisual();
        }
        protected void Land()
        {
            List<Stop> stops = Stops();
            foreach (Stop stop in stops) if (stop.Section >= 0) { Hold(stop.Control); return; }
            if (stops.Count > 0) Hold(stops[0].Control);
        }
        private void Step(int by)
        {
            List<Stop> stops = Stops();
            if (stops.Count == 0) return;
            int at = At(stops, Held());
            if (at < 0 && by > 0) { Land(); return; }
            Hold(stops[(at < 0 ? stops.Count - 1 : at + by + stops.Count) % stops.Count].Control);
        }
        private void Jump(int by)
        {
            List<Stop> stops = Stops();
            var firsts = new List<int>();
            for (int i = 0; i < stops.Count; i++)
                if (stops[i].Section >= 0 && (i == 0 || stops[i].Section != stops[i - 1].Section)) firsts.Add(i);
            if (firsts.Count == 0) { Step(by); return; }
            int at = At(stops, Held()), now = -1;
            if (at >= 0 && stops[at].Section >= 0)
                for (int i = 0; i < firsts.Count; i++) if (stops[firsts[i]].Section == stops[at].Section) now = i;
            int next = now < 0 ? (by > 0 ? 0 : firsts.Count - 1) : (now + by + firsts.Count) % firsts.Count;
            Hold(stops[firsts[next]].Control);
        }
        private void Along(int by)
        {
            List<Stop> stops = Stops();
            int at = At(stops, Held()), next = at + by;
            if (at < 0 || next < 0 || next >= stops.Count || stops[next].Row != stops[at].Row) return;
            Hold(stops[next].Control);
        }
        private void Steer(KeyEventArgs e)
        {
            if (e.Source is NeonChoice) return;
            Key key = e.Key;
            KeyModifiers mods = e.KeyModifiers;
            TextBox box = FocusManager?.GetFocusedElement() as TextBox;
            if (box != null && TopLevel.GetTopLevel(box) != this) box = null;
            bool lines = box != null && box.AcceptsReturn;
            if (key == Key.Tab && (mods == KeyModifiers.None || mods == KeyModifiers.Shift) && !(lines && box.AcceptsTab))
            {
                Jump(mods == KeyModifiers.Shift ? -1 : 1);
                e.Handled = true;
                return;
            }
            if (mods != KeyModifiers.None || lines) return;
            if (key == Key.Up || key == Key.Down) { Step(key == Key.Up ? -1 : 1); e.Handled = true; return; }
            if (key == Key.Left || key == Key.Right)
            {
                if (box != null || Held() is NeonSlider) return;
                Along(key == Key.Left ? -1 : 1);
                e.Handled = true;
                return;
            }
            if (box != null) return;
            if (key == pageHeld) { e.Handled = true; return; }
            if (PageKey(key)) { pageHeld = key; e.Handled = true; }
        }
        internal void Strike(Key key, KeyModifiers mods = KeyModifiers.None)
        {
            Interactive target = this;
            if (FocusManager?.GetFocusedElement() is Control focused && (focused is NeonChoice || TopLevel.GetTopLevel(focused) == this)) target = focused;
            target.RaiseEvent(new KeyEventArgs { RoutedEvent = KeyDownEvent, Key = key, KeyModifiers = mods });
            RaiseEvent(new KeyEventArgs { RoutedEvent = KeyUpEvent, Key = key, KeyModifiers = mods });
        }
        internal Control HeldNow { get { return Held(); } }
        internal bool HeldMarked { get { Control held = Held(); return held != null && Marks(held); } }
        internal string HeldSection
        {
            get
            {
                List<Stop> stops = Stops();
                int at = At(stops, Held());
                return at < 0 ? "nothing" : stops[at].Head == null ? "title" : stops[at].Head.Text;
            }
        }
        internal bool HeldFirst
        {
            get
            {
                List<Stop> stops = Stops();
                int at = At(stops, Held());
                return at >= 0 && (at == 0 || stops[at - 1].Section != stops[at].Section);
            }
        }
        internal void HoldRow(Control control) { Hold(control); }

        // Keep the same native header controls reachable while the long Linux settings page scrolls.
        protected void PinHeader(Control control)
        {
            Page.Children.Remove(control); header.Children.Add(control); headerPlate.IsVisible = true;
        }

        internal void Fit(double width, double height, double zoom)
        {
            if (fitting) return;
            fitting = true;
            try { FitPage(width, height, zoom); }
            finally { fitting = false; }
        }
        private void ScreenChanged()
        {
            if (!IsVisible || fitting || double.IsNaN(Page.Width)) return;
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            if (screen != null && (screen.WorkingArea != lastArea || screen.Scaling != lastDpi)) Fit(Page.Width, Page.Height, Zoom);
        }
        private void FitPage(double width, double height, double zoom)
        {
            Zoom = zoom;
            header.Width = width; headerZoom.LayoutTransform = new ScaleTransform(zoom, zoom);
            headerPlate.Zoom = zoom; headerPlate.Clip = new RectangleGeometry(new Rect(0, 0, width * zoom, 50 * zoom));
            Page.Width = width; Page.Height = height;
            Zoomed.LayoutTransform = new ScaleTransform(zoom, zoom);
            plate.Zoom = zoom; plate.Page = height;
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            double dpi = screen?.Scaling ?? RenderScaling;
            var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 900);
            lastArea = area; lastDpi = dpi;
            Width = Math.Min(width * zoom, Math.Max(200, area.Width / dpi - 32));
            Height = Math.Min(Math.Min(height, 820) * zoom, Math.Max(180, area.Height / dpi - 48));
            if (IsVisible)
            {
                int maxX = area.Right - (int)Math.Ceiling(Width * dpi), maxY = area.Bottom - (int)Math.Ceiling(Height * dpi);
                Look.Position(this, new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, maxX)), Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, maxY))));
            }
            UpdateLayout();
            plate.View = Scroll.Viewport.Height / Zoom; rim.InvalidateVisual();
        }

        // A trackpad swipe up carries a little sideways drift too, so both directions move the page; dropping the up and down
        // whenever there was any sideways left her stuck at the bottom on a Mac. Shift turns a plain wheel sideways.
        internal static Vector Wheel(Vector delta, KeyModifiers keys)
        {
            if ((keys & KeyModifiers.Shift) != 0 && delta.X == 0) return new Vector(delta.Y, 0);
            return delta;
        }
        // A trackpad sends many small fractional steps and already eases them itself, momentum included, so they move the page
        // as they come; gliding each one again made every swipe start and end slowly. A mouse wheel's whole notches still glide.
        internal static bool Precise(Vector delta)
        {
            return delta.X != Math.Round(delta.X) || delta.Y != Math.Round(delta.Y);
        }
        private Vector Clamped(Vector offset)
        {
            return new Vector(Math.Clamp(offset.X, 0, Math.Max(0, Scroll.Extent.Width - Scroll.Viewport.Width)), Math.Clamp(offset.Y, 0, Math.Max(0, Scroll.Extent.Height - Scroll.Viewport.Height)));
        }
        internal void GlideTo(Vector offset)
        {
            from = Scroll.Offset;
            to = Clamped(offset);
            clock.Restart(); glide.Start();
        }
        internal void Settle() { glide.Stop(); Scroll.Offset = to; }
        internal void Top() { glide.Stop(); to = Scroll.Offset = new Vector(); }
        internal void SavePicture(string path)
        {
            Top(); UpdateLayout();
            // Linux's window manager can still be catching up on a new size, so the picture lays her out at the size she asked for
            // and leaves the window to lay out again at its own.
            var surface = (Control)Content; var size = new Size(Width, Height); var clip = surface.Clip;
            surface.Clip = MainframeTheme.Cut(new Rect(size), 16 * Zoom, 6 * Zoom, 16 * Zoom, 6 * Zoom);
            surface.Measure(size); surface.Arrange(new Rect(size));
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(Width), (int)Math.Ceiling(Height)), new Vector(96, 96))) { bitmap.Render(surface); bitmap.Save(path); }
            surface.Clip = clip; surface.InvalidateMeasure();
        }
        internal static T Put<T>(Canvas canvas, T control, double x, double y, double width, double height, string name = null) where T : Control
        {
            Canvas.SetLeft(control, x); Canvas.SetTop(control, y); control.Width = width; control.Height = height;
            if (name != null) AutomationProperties.SetName(control, name);
            canvas.Children.Add(control); return control;
        }
    }

    internal sealed class Mainframe : MainframeWindow
    {
        private sealed class Item
        {
            internal Control Control;
            internal double X, Y;
            internal int Page;
            internal Func<bool> When;
        }
        private readonly Pet pet;
        private readonly List<Item> items = new List<Item>();
        private readonly List<Action> readers = new List<Action>();
        private readonly Dictionary<string, Control> rows = new Dictionary<string, Control>();
        private readonly Dictionary<string, Action<int>> changes = new Dictionary<string, Action<int>>();
        private readonly List<Window> sheets = new List<Window>();
        internal ModsWindow Mods { get; private set; }
        internal RemoteSheet Remote { get; private set; }
        private readonly DispatcherTimer status = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        // Support's heart beats twice every 7 seconds while her main page shows, and its rim lights with it (Lachy, 4 Nov).
        private const double BeatMs = 7000;
        private readonly DispatcherTimer heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        private readonly Stopwatch beatClock = Stopwatch.StartNew();
        private readonly NeonButton support, remoteKey;
        private readonly NeonCaption brainName, brainNote, tierNote, voiceNote, updateNote, nameListeningNote;
        private readonly NeonButton more, advanced, minimize, close, setup, refresh, textConnect, voiceConnect, customVoice, showHide;
        private readonly NeonCaption build;
        private readonly Dictionary<NeonEntry, string> entries = new Dictionary<NeonEntry, string>();
        private readonly NeonRung[] brains = new NeonRung[3];
        private readonly NeonRung[] voices = new NeonRung[4];
        private readonly NeonPicker models, freeKeys, voicePicker, speechModels;
        private readonly NeonPicker huskMode;
        private readonly NeonCaption huskLabel;
        private readonly NeonPicker thinkingLevel;
        private readonly NeonCaption thinkingLabel, extraLabel;
        private readonly NeonButton thinkingExtra;
        private readonly NeonSwitch huskVerbosity;
        private readonly NeonEntry textKey, voiceKey, voiceId, nickname, custom, localAddress;
        private bool paged, syncing, closed, finishing;
        private string layout;
        private string paidKey;
        private int building;
        private static readonly int[] FrameSizes = { 0, 100, 125, 150, 175, 200 };
        private static readonly string[] FrameNames = { "Auto", "100%", "125%", "150%", "175%", "200%" };
        private static readonly string[] Lengths = { "Short", "Medium", "Long" };
        private static readonly int[] Recalls = { 8, 16, 32 };
        private static readonly int[][] FoldedLeft = { new[] { 232, 428 }, new[] { 478, 506 }, new[] { 644, 1121 }, new[] { 1232, 1284 }, new[] { 1284, 1312 } };
        private static readonly int[][] FoldedRight = { new[] { 166, 194 }, new[] { 400, 782 }, new[] { 978, 2028 } };
        private static readonly int[][] MissingLeft = { new[] { 428, 648 }, new[] { 672, 700 }, new[] { 854, 1121 }, new[] { 1150, 1314 }, new[] { 1342, 1728 } };
        private static readonly int[][] MissingRight = { new[] { 400, 428 }, new[] { 978, 1028 }, new[] { 1056, 1180 }, new[] { 1408, 2028 } };

        internal Mainframe(Pet owner) : base("Mainframe")
        {
            pet = owner;
            Steered = true;
            Caption("MAINFRAME", 24, 15, 120, 29, MainframeTheme.Rose, 64.0 / 3, true, true);
            build = Caption("v" + typeof(Pet).Assembly.GetName().Version.ToString(), 230, 20, 62, 22, MainframeTheme.Faint, 34.0 / 3); build.Align = TextAlignment.Right;
            // Kelv's Hyprland changes: a minimized Mainframe has no taskbar there to come back from, so it gets only Close,
            // and Super+C closes it the way Hyprland folk expect. Super is Meta here; on a Mac Meta is Cmd and Cmd+C is copy,
            // so this stays on Hyprland.
            bool hyprland = OperatingSystem.IsLinux() && HyprlandDesktop.Current != null;
            if (!hyprland)
            {
                minimize = Key("", 300, 20, 26, 22, () => WindowState = WindowState.Minimized); minimize.Glyph = 1;
                AutomationProperties.SetName(minimize, "Minimize");
                MainframeTips.Explain(minimize, "Minimize");
            }
            close = Key("", 330, 20, 26, 22, hyprland ? (Action)Shut : Close); close.Glyph = 2;
            AutomationProperties.SetName(close, "Close");
            MainframeTips.Explain(close, "Close");
            if (hyprland) KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.C && e.KeyModifiers == KeyModifiers.Meta) { e.Handled = true; Program.Guard(Shut); } };
            more = Key("Settings", 146, 20, 84, 22, () => ShowSettings(!paged)); more.Accent = MainframeTheme.Cyan; more.Loud = true;
            foreach (Item item in items) item.Page = -1;
            Section("BRAIN", 0, 58);
            if (TerminalJobs.Supported) { Key("ChatGPT plan", 230, 56, 126, 20, pet.UsePlan); Key("Stop", 134, 56, 86, 20, pet.StopNativeWork); }
            brainName = Caption("", 28, 79, 328, 20, MainframeTheme.White, 44.0 / 3, true); brainName.Ellipsis = true;
            brainNote = Caption("", 28, 101, 328, 24, MainframeTheme.Faint); brainNote.Ellipsis = true;
            models = Picker("Brain model", 28, 134, 244, () => Array.IndexOf(pet.MainframeModels, pet.Brain.Model), i => { if (i == pet.MainframeModels.Length) pet.AskPull(); else pet.MainframePickModel(pet.MainframeModels[i]); }); models.Ellipsis = true;
            refresh = Key("Refresh", 280, 134, 76, 28, pet.MainframeRefresh);
            setup = Key("Set up her brain", 28, 226, 170, 28, pet.PullBrain); setup.Accent = MainframeTheme.Cyan; setup.Loud = true;
            Caption("1. Open Ollama.", 28, 138, 328, 18, MainframeTheme.Faint);
            Caption("2. Download her brain.", 28, 160, 328, 18, MainframeTheme.Faint);
            Caption("3. Pick a model.", 28, 182, 328, 18, MainframeTheme.Faint);
            Caption("Have Ollama? Open it, then press Refresh.", 28, 264, 328, 18, MainframeTheme.Faint, 12);
            tierNote = Caption("", 28, 168, 328, 16, MainframeTheme.Faint, 34.0 / 3);
            string[] brainNames = { "NORMAL", "MEDIUM INTELLIGENCE", "MAX INTELLIGENCE" };
            string[] brainHints = { "Free · local model", "Free", "Cloud API keys" };
            for (int i = 0; i < 3; i++)
            {
                int at = i;
                brains[i] = Add(new NeonRung { Text = brainNames[i], Hint = brainHints[i], Flag = i == 0 ? "Start with this!" : null }, 28, 184 + i * 24, i == 1 ? 232 : 328, 24, brainNames[i]);
                brains[i].Click += (_, _) => pet.SetBrainTier(at);
            }
            freeKeys = Add(new NeonPicker(), 266, 209, 90, 22, "Free key");
            freeKeys.Ellipsis = true;
            freeKeys.Check = false;
            freeKeys.Options(new[] { "NVIDIA", "Google AI Studio (Gemini)", "OpenRouter" });
            freeKeys.Text = "Free key";
            freeKeys.Changed += i => { if (Program.Smoke == null) Pet.Browse(new[] { "https://build.nvidia.com/settings/api-keys", "https://aistudio.google.com/apikey", "https://openrouter.ai/keys" }[i]); freeKeys.Text = "Free key"; };
            textKey = Entry("Brain API key", "Paste a brain API key", 28, 262, 244, 28, ConnectBrain, true);
            textConnect = Key("Connect", 280, 262, 76, 28, ConnectBrain);
            MainframeTips.Explain(textConnect, "NVIDIA, Google AI Studio (Gemini) or OpenRouter API key");
            Caption("Free keys can be rate limited and slow.", 28, 294, 328, 18, MainframeTheme.Yellow, 34.0 / 3);
            Caption("That's a paid key: it goes under MAX INTELLIGENCE.", 28, 315, 328, 24, MainframeTheme.Yellow);
            Key("Move to MAX", 28, 350, 128, 24, () => { string key = paidKey; if (key == null) return; paidKey = null; pet.SetBrainTier(2); pet.SaveBrainKey(key, 2); });
            Section("VOICE", 0, 184);
            string[] voiceNames = { "OFF", "RETAINED LOCAL", "CUSTOM LOCAL", "CLOUD VOICE" };
            string[] voiceHints = { "Silent · text only", "Existing voice pack", "Your own model", "Needs a key" };
            for (int i = 0; i < 4; i++)
            {
                if (i == 1 && !OperatingSystem.IsLinux()) continue;
                int at = i;
                voices[i] = Add(new NeonRung { Text = voiceNames[i], Hint = voiceHints[i] }, 28, 208 + i * 24, i >= 2 ? 246 : 328, 23, voiceNames[i]);
                voices[i].Click += (_, _) => pet.MainframeVoiceModePick(at);
            }
            Key("Set up", 276, 257, 80, 22, OpenServer);
            Key("Setup guide", 276, 281, 80, 22, () => { if (Program.Smoke == null) Pet.Browse("https://elevenlabs.io/app/settings/api-keys"); });
            Caption("ElevenLabs", 28, 310, 108, 28, MainframeTheme.Cyan);
            voiceKey = Entry("ElevenLabs API key", "ElevenLabs key", 136, 310, 136, 28, ConnectVoice, true);
            voiceConnect = Key("Connect", 280, 310, 76, 28, ConnectVoice);
            MainframeTips.Explain(voiceConnect, "ElevenLabs API key");
            voiceId = Entry("ElevenLabs voice ID", "Voice ID", 28, 346, 196, 28, () => pet.ChooseVoice(voiceId.Box.Text));
            voicePicker = new NeonPicker { Width = 196, Height = 28, Ellipsis = true };
            voicePicker.Changed += i => { pet.ChooseVoice(pet.MainframeVoiceIds[i]); Sync(); }; changes["Voice"] = i => voicePicker.Pick(i); rows["Voice"] = voicePicker;
            readers.Add(() => voicePicker.Selected = Array.IndexOf(pet.MainframeVoiceIds, pet.Saved.VoiceId));
            speechModels = Picker("Speech model", 232, 346, 124, () => Array.IndexOf(pet.MainframeSpeechIds, pet.Saved.SpeechModel ?? ""), i => pet.SetSpeechModel(pet.MainframeSpeechIds[i])); speechModels.Ellipsis = true;
            MainframeTips.Explain(speechModels, "Default model");
            voiceId.Chevron = true; voiceId.Drop += () => { pet.MainframeLoadVoices(); voicePicker.OpenAt(voiceId); };
            voiceId.Box.LostFocus += (_, _) => Program.Guard(() => { if (voiceId.Box.Text != pet.Saved.VoiceId && !string.IsNullOrWhiteSpace(voiceId.Box.Text)) pet.ChooseVoice(voiceId.Box.Text.Trim()); });
            customVoice = Key("Voice server", 28, 346, 328, 28, OpenServer); customVoice.Chevron = true; customVoice.Ellipsis = true;
            MainframeTips.Explain(customVoice, "CUSTOM LOCAL");
            voiceNote = Caption("", 28, 499, 244, 22, MainframeTheme.Faint); voiceNote.Ellipsis = true;
            var voiceTest = Key("Test voice", 280, 499, 76, 22, () => { if (Program.Smoke == null) pet.HearStyle(); });
            items[items.Count - 1].When = () => pet.MainframeVoiceMode != 0;
            nameListeningNote = null;
            if (OperatingSystem.IsLinux())
            {
                Toggle("Listen for Capri", 0, 524, () => pet.NameListeningEnabled, pet.SetNameListening);
                nameListeningNote = Caption("", 28, 555, 328, 22, MainframeTheme.Faint); nameListeningNote.Ellipsis = true;
                MainframeTips.Explain(nameListeningNote, "Local decoder · 20-second follow-ups · selected physical microphone");
            }
            Section("CAPRI", 0, 540);
            remoteKey = Key("Remote", 100, 539, 88, 18, OpenRemote);
            AutomationProperties.SetName(remoteKey, "Capri Remote: chat with her from your phone");
            Key("Mods", 190, 539, 70, 18, OpenMods).Accent = MainframeTheme.Cyan;
            // Shorter than a band's other keys and level with CAPRI, so its lit rim stays off the band's teal rule (Lachy, 4 Nov).
            support = Key("Support", 268, 539, 88, 18, () => { if (Program.Smoke == null) Pet.Browse("https://ko-fi.com/lachysdev"); });
            support.Heart = true;
            showHide = Key("Hide Capri", 28, 562, 104, 28, pet.MainframeToggleHidden);
            Key("Bring here", 140, 562, 104, 28, pet.Bring);
            Key("Talk to her", 252, 562, 104, 28, () => pet.Talk.Open());
            Key("Check for updates", 28, 606, 150, 28, pet.MainframeUpdateCheck);
            Key("Help", 186, 606, 62, 28, () => { if (Program.Smoke == null) Pet.Browse(Pet.HelpPage); });
            Key("Quit Capri", 256, 606, 100, 28, pet.Quit).Accent = MainframeTheme.Hot;
            updateNote = Caption("", 28, 644, 328, 18, MainframeTheme.Faint, 34.0 / 3); updateNote.Ellipsis = true;
            building = 1;
            advanced = Key("Advanced Settings", 238, 20, 136, 22, () => { pet.SetAdvancedSettings(!pet.Saved.AdvancedSettings); ShowSettings(true); }); advanced.Accent = MainframeTheme.Cyan; advanced.Loud = true;
            Section("YOU", 0, 58);
            var personality = Add(new NeonPicker(), 72, 55, 106, 20, "Her personality");
            personality.Check = false;
            // Match the released row's explicit fallback for narrower native text.
            personality.Ellipsis = true;
            personality.Options(new[] { "Edit personality.md", "Reset to default" }); personality.Text = "Her personality";
            personality.Changed += i => { if (i == 0) pet.OpenPersonality(); else pet.ResetPersonality(); personality.Text = "Her personality"; };
            Key("Open her notes", 184, 55, 96, 20, () => pet.OpenNotes()).Ellipsis = true;
            Key("File editor", 286, 55, 70, 20, () => pet.OpenFiles(0)).Ellipsis = true;
            var language = Picker("Language", 184, 85, 172, () => LanguageIndex(), i => pet.SetLanguage(Brain.Languages[i, 1]));
            language.Height = 22;
            var languages = new string[Brain.Languages.GetLength(0)];
            for (int i = 0; i < languages.Length; i++) languages[i] = Brain.Languages[i, 0];
            language.Options(languages); language.Ellipsis = true;
            Named("Language", 0, 82);
            Caption("What should she call you?", 28, 116, 200, 18);
            Key("Get to know me", 236, 113, 120, 22, pet.GetToKnow);
            nickname = Entry("What should she call you?", "Your name or a nickname", 28, 141, 244, 22, () => pet.SetNickname(nickname.Box.Text));
            Key("Save", 280, 141, 76, 22, () => pet.SetNickname(nickname.Box.Text));
            Section("VOICE", 0, 180);
            var toneNote = Caption("Applies to every voice", 88, 178, 192, 20, MainframeTheme.Faint, 34.0 / 3); toneNote.Align = TextAlignment.Right;
            readers.Add(() => toneNote.Text = pet.RetainedVoice ? "Tone: retained profile" : "Applies to every voice");
            var layersKey = Key("Layers", 286, 177, 70, 20, OpenLayers);
            readers.Add(() => { layersKey.IsEnabled = !pet.RetainedVoice; layersKey.Picked = VoiceFx.Layers.On; layersKey.InvalidateVisual(); });
            Slider("Volume", Pet.VolumeNames, 0, 204, () => Nearest(Pet.Volumes, Voice.Loudness), i => pet.SetVolume(Pet.Volumes[i]));
            Slider("Pitch", Pet.PitchNames, 0, 232, () => VoiceFx.Pitch + VoiceFx.MostPitch, i => pet.SetPitch(i - VoiceFx.MostPitch), true);
            Slider("Speed", Pet.PaceNames, 0, 260, () => Nearest(Pet.Paces, VoiceFx.Speed), i => pet.SetPace(Pet.Paces[i]), true);
            Slider("Robot", Pet.AmountNames, 0, 288, () => Nearest(Pet.Amounts, VoiceFx.Robot), i => pet.SetRobot(Pet.Amounts[i]), true);
            Slider("Glitch", Pet.AmountNames, 0, 316, () => Nearest(Pet.Amounts, VoiceFx.Glitch), i => pet.SetGlitch(Pet.Amounts[i]), true);
            Slider("Sounds", Pet.SoundNames, 0, 344, () => Nearest(Pet.SoundLevels, Sfx.Level), i => pet.SetSounds(Pet.SoundLevels[i]));
            Toggle("Typing sound", 0, 372, () => pet.TypingSound, pet.SetTyping).Hint = "When she types";
            Named("Typing mode", 0, 400);
            var typingMode = Picker("Typing mode", 184, 403, 172, () => pet.Saved.Typing, pet.SetTypingMode); typingMode.Height = 22; typingMode.Options(new[] { "Auto", "On", "Off" });
            Section("MOVES", 0, 648);
            Slider("Wander", Pet.WanderNames, 0, 700, () => pet.Saved.Wander, pet.SetWander);
            Slider("Sleep", Pet.SleepNames, 0, 728, () => Array.IndexOf(Pet.Sleeps, pet.SleepAfter), i => pet.SetSleepAfter(Pet.Sleeps[i]));
            Slider("Pop-ins", Pet.PopNames, 0, 756, () => pet.PopPace, pet.SetPopPace);
            Toggle("Dance to music", 0, 784, () => !pet.Saved.NoDance, pet.SetDanceToMusic);
            Toggle("Glitch in at startup", 0, 812, () => !pet.Saved.NoIntro, pet.SetIntro).Switch = 68;
            if (pet.MainframeInputSetup)
            {
                Section("KEYS", 0, 854);
                Key("Set up mouse and keyboard", 28, 881, 328, 22, pet.MainframeSetupInput);
            }
            Section("GAMES", 0, 1126);
            Toggle("Mute her", 0, 1314, () => pet.Shushed, on => { if (pet.Shushed != on) pet.ToggleShush(false); });
            Section("CAPRI", 380, 58);
            Slider("Size", Pet.SizeNames, 380, 82, () => Nearest(Pet.Sizes, pet.BodySize), i => pet.Resize(Pet.Sizes[i]));
            Toggle("Check in on me", 380, 110, () => !pet.Saved.NoCheckIns, pet.SetCheckIns);
            Toggle("Always on top", 380, 138, () => pet.OnTop, pet.SetOnTop);
            var frameSize = Slider("Mainframe size", FrameNames, 380, 166, () => Array.IndexOf(FrameSizes, Pet.FrameSize(pet.Saved.MainframeSize)), i => { }); frameSize.Deferred = true;
            frameSize.Released += () => { if (!syncing) pet.SetMainframeSize(FrameSizes[frameSize.Value]); };
            Slider("Mainframe colours", MainframeTheme.LookNames, 380, 194, () => MainframeTheme.ClampLook(pet.Saved.MainframeColours), pet.SetMainframeColours);
            Toggle("Head pats", 380, 222, () => pet.HeadPats, pet.SetHeadPats);
            Toggle("Legacy stand", 380, 250, () => pet.LegacyStand, pet.SetLegacyStand);
            Section("WATCH", 380, 292);
            Toggle("Watch my screen", 380, 316, () => pet.Saved.Watch, on => pet.SetWatch(on));
            Toggle("Remember what she sees", 380, 344, () => !pet.Saved.NoCaughtUp, pet.SetCaughtUp).Switch = 68;
            Slider("Every", Pet.GlanceGapNames, 380, 372, () => Nearest(Pet.GlanceGaps, pet.GlanceGap), i => pet.SetGlanceGap(Pet.GlanceGaps[i]));
            Section("BRAIN", 380, 442);
            huskLabel = Caption("Code/Husk Mode", 408, 472, 146, 16);
            readers.Add(delegate { if (huskMode != null) huskMode.Options(HuskWatch.Choices(pet.HuskMode)); });
            huskMode = Picker("Code/Husk Mode", 564, 469, 172, delegate { return HuskWatch.Choice(pet.HuskMode); }, delegate(int choice) { pet.SetHusk(HuskWatch.Modes[choice]); });
            huskMode.Height = 22;
            huskMode.Options(HuskWatch.Choices(pet.HuskMode));
            readers.Add(delegate { huskMode.IsEnabled = !pet.HuskChanging; });
            ToolTip.SetTip(huskMode, "Watch Claude Code, Codex or OpenCode through her hooks or plugin. Restart the coding app after changing this.");
            thinkingLabel = Caption("Thinking", 408, 500, 146, 16);
            readers.Add(delegate
            {
                if (thinkingLevel == null) return;
                string[] choices = new string[ThinkingLevels.Names.Length];
                bool[] greyed = new bool[choices.Length];
                for (int i = 0; i < choices.Length; i++)
                {
                    string why = pet.Brain.ThinkingWhy(i);
                    choices[i] = why == null ? ThinkingLevels.Names[i] : ThinkingLevels.Names[i] + " · " + why;
                    greyed[i] = why != null;
                }
                thinkingLevel.Options(choices);
                thinkingLevel.Greyed = greyed;
            });
            thinkingLevel = Picker("Thinking", 564, 497, 172, () => pet.Brain.ThinkingLevel, pet.SetThinking);
            thinkingLevel.Height = 22;
            thinkingLevel.Ellipsis = true;
            extraLabel = Caption("Extra request JSON", 408, 528, 146, 16);
            thinkingExtra = Add(new NeonButton { Text = "Add" }, 564, 525, 172, 22, "Extra request JSON");
            thinkingExtra.Click += (_, _) => { OpenThinkingExtra(); Sync(); };
            readers.Add(delegate
            {
                thinkingExtra.Text = pet.Brain.ThinkingExtra == null ? "Add" : "Edit";
                thinkingExtra.IsEnabled = pet.Brain.ThinkingOpen;
                thinkingExtra.InvalidateVisual();
            });
            huskVerbosity = Toggle("Verbosity", 380, 466, () => !pet.Saved.NoHuskVerbosity, pet.SetHuskVerbosity);
            Toggle("Stay on my model", 380, 466, () => pet.Saved.StayOnModel, pet.SetStay);
            Slider("Replies", Lengths, 380, 494, () => Brain.Wordiness, pet.SetReplies);
            int[] recallChoices = Array.IndexOf(Recalls, Brain.Recall) < 0 ? new[] { Recalls[0], Recalls[1], Recalls[2], Brain.Recall } : Recalls;
            string[] recallLabels = recallChoices.Length == Recalls.Length ? Lengths : new[] { Lengths[0], Lengths[1], Lengths[2], Brain.Recall + " turns" };
            Slider("Memory", recallLabels, 380, 522, () => Array.IndexOf(recallChoices, Brain.Recall), i => pet.SetRecall(recallChoices[i]));
            Slider("Memory size", new[] { "Auto", "4K", "8K", "16K", "32K", "64K" }, 380, 550, () => Array.IndexOf(Brain.MemorySizes, Brain.MemorySize), i => pet.SetMemorySize(Brain.MemorySizes[i]));
            Slider("Max reply", new[] { "Auto", "256", "2K", "8K" }, 380, 578, () => Brain.ReplyLength, pet.SetReplyLength);
            Slider("Bubble", Pet.BubbleNames, 380, 606, () => pet.BubbleTime, pet.SetBubbleTime);
            Toggle("Follow-up messages", 380, 634, () => pet.FollowUps, pet.SetFollowUps).Switch = 68;
            custom = Entry("Custom model name", "Custom model name", 408, 665, 244, 22, () => pet.MainframePickModel(custom.Box.Text));
            Key("Use", 660, 665, 76, 22, () => pet.MainframePickModel(custom.Box.Text));
            Caption("A model in Ollama, on your local server or on your key.", 408, 694, 328, 22, MainframeTheme.Faint, 34.0 / 3);
            Caption("Local server address", 408, 726, 328, 18);
            localAddress = Entry("Local server address", "http://127.0.0.1:1234", 408, 755, 244, 22, () => UseServer());
            Key("Save", 660, 755, 76, 22, UseServer);
            Section("HABITS", 380, 796);
            Key("Obsidian vault", 484, 793, 112, 20, OpenVault);
            Key("Open her memories", 600, 793, 136, 20, () => pet.OpenMemories());
            Toggle("Start at login", 380, 820, () => pet.StartsAtLogin, on => pet.SetLogin(on, false));
            Toggle("Show on Discord", 380, 848, () => !pet.Saved.NoDiscord, pet.SetDiscord);
            Named("Music app", 380, 876);
            var musicApp = Picker("Music app", 564, 879, 172, () => OperatingSystem.IsLinux() && pet.MusicApp == 2 ? 0 : pet.MusicApp, pet.SetMusicApp); musicApp.Height = 22; musicApp.Options(OperatingSystem.IsLinux() ? new[] { "Spotify", "YouTube" } : Hands.MusicSteps());
            Toggle("Remember our chats", 380, 904, () => !pet.Saved.NoMemories, pet.SetRemembering);
            var security = Key("Security: " + Security.PresetName, 408, 935, 328, 22, OpenSecurity);
            var securityNote = Caption(Security.Short, 408, 959, 328, 18, MainframeTheme.Faint, 34.0 / 3); securityNote.Align = TextAlignment.Right; securityNote.Ellipsis = true;
            readers.Add(() => { security.Text = "Security: " + Security.PresetName; securityNote.Text = Security.Short; securityNote.InvalidateVisual(); });
            Toggle("Keep PC awake for jobs", 380, 1028, () => pet.KeepAwake, pet.SetKeepAwake).Switch = 68;
            for (int i = 0; i < Care.Names.Length; i++)
            {
                CareKind kind = (CareKind)i;
                var on = Toggle(Care.Names[i], 380, 1180 + i * 28, () => pet.CareOn(kind), value => pet.SetCare(kind, value));
                on.Width = 224; on.Switch = 68;
                int[] times = Hands.ValuesFor(Hands.CareSettings[i]);
                string[] names = new string[times.Length];
                for (int t = 0; t < times.Length; t++) names[t] = CareTimeName(kind, times[t]);
                var time = Picker(Care.Names[i] + " time", 640, 1183 + i * 28, 96, () => Math.Max(0, Array.IndexOf(times, pet.CareTime(kind))), at => pet.SetCareTime(kind, times[at]));
                time.Height = 22; time.Options(names);
            }
            Key("Edit check-in lines", 408, 1380, 328, 20, pet.OpenCare);
            if (OperatingSystem.IsLinux()) foreach (Item item in items) if (item.Y < 50) PinHeader(item.Control);
            status.Tick += (_, _) => Program.Guard(Sync);
            pet.SettingsChanged += Sync;
            heartbeat.Tick += (_, _) => Program.Guard(Heartbeat);
            Opened += (_, _) => { Sync(); status.Start(); heartbeat.Start(); };
            Closed += (_, _) => { closed = true; status.Stop(); heartbeat.Stop(); pet.SettingsChanged -= Sync; foreach (Window sheet in sheets.ToArray()) sheet.Close(); CloseMenus(); };
            Closing += (_, e) => { if (MacQuit.MinimizeMainframe(OperatingSystem.IsMacOS(), OperatingSystem.IsLinux(), finishing, e.CloseReason)) { e.Cancel = true; CloseMenus(); foreach (Window sheet in sheets.ToArray()) sheet.Close(); WindowState = WindowState.Minimized; } };
            Sync();
        }

        private T Add<T>(T control, double x, double y, double width, double height, string name = null) where T : Control
        {
            Put(Page, control, x, y, width, height, name);
            items.Add(new Item { Control = control, X = x, Y = y, Page = building });
            if (name != null) { rows[name] = control; MainframeTips.Explain(control, name); }
            return control;
        }
        private NeonCaption Caption(string text, double x, double y, double width, double height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false, bool display = false)
        {
            return Add(new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Rose, Size = size, Bold = bold, Display = display }, x, y, width, height);
        }
        private NeonHead Section(string title, int x, int y) { return Add(new NeonHead { Text = title }, 14 + x, y - 5, 352, 24); }
        private void Named(string text, int x, int y)
        {
            var label = Caption(text, x + 28, y + 6, 146, 16);
            MainframeTips.Explain(label, text);
        }
        private void Heartbeat()
        {
            double swell = 1, beat = 0;
            if (WindowState != WindowState.Minimized && support.IsVisible) Pulse(beatClock.Elapsed.TotalMilliseconds, out swell, out beat);
            if (swell == support.Swell && beat == support.Beat) return;
            support.Swell = swell; support.Beat = beat; support.InvalidateVisual();
        }
        // Where Support is in its beat: two quick swells of the heart, the rim lit across both, then rest.
        internal static void Pulse(double ms, out double swell, out double beat)
        {
            double t = ms % BeatMs / BeatMs;
            swell = Between(t, new[] { 0, .04, .08, .12, .16 }, new[] { 1, 1.22, 1, 1.12, 1 });
            beat = Between(t, new[] { 0, .05, .13, .22 }, new[] { 0, 1, 1, 0.0 });
        }
        private static double Between(double t, double[] at, double[] value)
        {
            for (int i = 1; i < at.Length; i++) if (t < at[i]) return value[i - 1] + (value[i] - value[i - 1]) * (t - at[i - 1]) / (at[i] - at[i - 1]);
            return value[value.Length - 1];
        }
        private NeonButton Key(string text, double x, double y, double width, double height, Action action)
        {
            var key = Add(new NeonButton { Text = text }, x, y, width, height);
            AutomationProperties.SetName(key, text);
            MainframeTips.Explain(key, text);
            key.Click += (_, _) => { action(); Sync(); };
            return key;
        }
        private NeonEntry Entry(string name, string cue, double x, double y, double width, double height, Action enter, bool secret = false)
        {
            var entry = Add(new NeonEntry(cue, secret), x, y, width, height, name);
            entry.Box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Program.Guard(() => { enter(); Sync(); }); } };
            return entry;
        }
        private NeonPicker Picker(string name, double x, double y, double width, Func<int> read, Action<int> change)
        {
            var picker = Add(new NeonPicker(), x, y, width, 28, name);
            picker.Changed += i => { if (!syncing) { change(i); Sync(); } };
            readers.Add(() => picker.Selected = read()); changes[name] = i => picker.Pick(i);
            return picker;
        }
        private NeonSlider Slider(string name, string[] labels, int x, int y, Func<int> read, Action<int> change, bool sample = false)
        {
            Named(name, x, y);
            var slider = Add(new NeonSlider(name, labels), x + 184, y + 3, 172, 22, name);
            slider.Changed += i => { if (!syncing) change(i); };
            if (sample) readers.Add(() => slider.IsEnabled = !pet.RetainedVoice);
            if (sample) slider.Released += () => { if (!syncing && Program.Smoke == null) pet.HearStyle(); };
            readers.Add(() => { if (!slider.Deferred || !slider.Editing) slider.Value = read(); });
            changes[name] = i => slider.Slide(i);
            return slider;
        }
        private NeonSwitch Toggle(string name, int x, int y, Func<bool> read, Action<bool> change)
        {
            var toggle = Add(new NeonSwitch { Text = name }, x + 28, y + 3, 328, 22, name);
            toggle.IsCheckedChanged += (_, _) => { if (!syncing) { change(toggle.Checked); Sync(); } };
            readers.Add(() => toggle.Checked = read()); changes[name] = i => { if (toggle.Checked != (i != 0)) toggle.Flip(); };
            return toggle;
        }
        private int LanguageIndex()
        {
            string language = Brain.Language ?? "English";
            for (int i = 0; i < Brain.Languages.GetLength(0); i++) if (Brain.Languages[i, 1] == language) return i;
            return 0;
        }
        private static int Nearest(int[] values, int value) { int at = 0; for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[at] - value)) at = i; return at; }
        internal static string CareTimeName(CareKind kind, int time) { return kind == CareKind.Late ? (time % 12 == 0 ? 12 : time % 12) + (time < 12 ? " am" : " pm") : time >= 60 && time % 60 == 0 ? time / 60 + " h" : time + " min"; }
        private static int Nearest(double[] values, double value) { int at = 0; for (int i = 1; i < values.Length; i++) if (Math.Abs(values[i] - value) < Math.Abs(values[at] - value)) at = i; return at; }
        private void Read(NeonEntry entry, string text)
        {
            string last;
            text = text ?? "";
            if (!entry.Box.IsKeyboardFocusWithin && (!entries.TryGetValue(entry, out last) || last != text)) { entry.Box.Text = text; entries[entry] = text; }
        }

        internal void Sync()
        {
            if (closed || syncing) return;
            syncing = true;
            try
            {
                var choices = new List<string>(pet.MainframeModels);
                if (pet.MainframeTier == 0) choices.Add("Download a model...");
                models.Options(choices.ToArray());
                voicePicker.Options(pet.MainframeVoices);
                speechModels.Options(pet.MainframeSpeechModels);
                if (paidKey != null && pet.MainframeTier != 1) paidKey = null;
                foreach (Action read in readers) read();
                RemoteView remote = pet.ReadRemote();
                remoteKey.Accent = remote.On ? MainframeTheme.Cyan : MainframeTheme.Rose;
                remoteKey.Picked = remote.On && remote.Online.Count > 0;
                remoteKey.InvalidateVisual();
                if (Array.IndexOf(pet.MainframeModels, pet.Brain.Model) < 0 && pet.Brain.Model != null) models.Text = pet.Brain.Model;
                if (Array.IndexOf(pet.MainframeVoiceIds, pet.Saved.VoiceId) < 0) voicePicker.Text = "Voices";
                for (int i = 0; i < brains.Length; i++) { brains[i].Checked = i == pet.MainframeTier; brains[i].InvalidateVisual(); }
                for (int i = 0; i < voices.Length; i++) if (voices[i] != null) { voices[i].Checked = i == pet.MainframeVoiceMode; voices[i].InvalidateVisual(); }
                brainName.Text = pet.Brain.Remote != null ? pet.Brain.Remote.Name + " · " + pet.Brain.Model : pet.Brain.Model ?? "Her brain isn't set up yet";
                brainNote.Text = pet.MainframeProblem ?? (pet.Brain.Provider != null ? "ChatGPT plan · retained account" : pet.MainframeTier > 0 && pet.MainframeConnecting ? "Checking the key..." : pet.MainframeProblem ?? pet.MainframeProgress ?? (pet.Brain.Remote != null ? "Key saved, ends " + Voice.Tail(pet.MainframeBrainKey) : pet.MainframeTier > 0 ? "Paste a brain key and press Connect." : pet.Brain.Server != null ? pet.Brain.Server : pet.Brain.Model == null ? "Set up her brain, or open Ollama and press Refresh." : "Local model · Ollama"));
                brainNote.Text = Brain.Brief(brainNote.Text) ?? brainNote.Text;
                tierNote.Text = pet.MainframeTier == 1 ? pet.Brain.Remote?.Kind == "nvidia" ? "Most NVIDIA models can't see your screen." : "NVIDIA, Gemini, OpenRouter" : pet.MainframeTier == 2 ? "Gemini, OpenRouter, ChatGPT, Claude, DeepSeek" : "Pick one. Only one brain runs at a time.";
                textKey.Cue = pet.MainframeBrainKey != null ? "Key saved, ends " + Voice.Tail(pet.MainframeBrainKey) : pet.MainframeTier == 1 ? "NVIDIA / Google AI Studio / OpenRouter" : "Paste a brain API key";
                textConnect.Text = pet.MainframeBrainKey == null || !string.IsNullOrWhiteSpace(textKey.Box.Text) ? "Connect" : "Forget";
                voiceKey.Cue = pet.MainframeVoiceKey == null ? "ElevenLabs key" : "Key saved, ends " + Voice.Tail(pet.MainframeVoiceKey);
                voiceConnect.Text = pet.MainframeVoiceKey == null || !string.IsNullOrWhiteSpace(voiceKey.Box.Text) ? "Connect" : "Forget";
                voiceNote.Text = pet.MainframeVoiceProblem ?? (pet.MainframeVoiceMode == 0 ? "Silent · text only" : pet.MainframeVoiceMode == 1 ? "Retained local voice · existing profile" : pet.MainframeVoiceMode == 2 ? "Local voice server" : pet.MainframeVoiceName ?? "Connect your ElevenLabs key.");
                if (nameListeningNote != null) nameListeningNote.Text = pet.NameListeningStatus;
                customVoice.Text = string.IsNullOrEmpty(pet.MainframeLocalModel) ? "Voice server" : pet.MainframeLocalModel;
                showHide.Text = pet.Away ? "Show Capri" : "Hide Capri";
                updateNote.Text = pet.MainframeUpdate;
                Read(nickname, Brain.Nickname); Read(custom, pet.Brain.Model); Read(localAddress, pet.Brain.Server); Read(voiceId, pet.Saved.VoiceId);
                more.Text = paged ? "Back" : "Settings"; advanced.Text = pet.Saved.AdvancedSettings ? "Simple Settings" : "Advanced Settings";
                string next = paged + "|" + pet.Saved.AdvancedSettings + "|" + Pet.FrameSize(pet.Saved.MainframeSize) + "|" + pet.MainframeTier + "|" + pet.MainframeVoiceMode + "|" + (pet.MainframeVoiceKey != null) + "|" + Expanded + "|" + (paidKey != null);
                if (layout != next) { layout = next; ArrangePage(); }
            }
            finally { syncing = false; }
        }
        private bool Expanded { get { return pet.MainframeTier == 0 && (pet.Brain.Model == null || pet.MainframePulling); } }
        internal bool SettingsShown { get { return paged; } }
        internal void Shut() { finishing = true; Close(); }
        internal void ShowSettings(bool on) { paged = on; layout = null; CloseMenus(); Top(); Sync(); }
        protected override void Back()
        {
            if (!paged) { Close(); return; }
            ShowSettings(false);
            Hold(more);
        }
        protected override bool PageKey(Avalonia.Input.Key key)
        {
            if (key == Avalonia.Input.Key.S) { if (!paged) { ShowSettings(true); Land(); } return true; }
            if (key == Avalonia.Input.Key.M) { OpenMods(); return true; }
            if (key == Avalonia.Input.Key.H) { if (paged) ShowSettings(false); Land(); return true; }
            if (key == Avalonia.Input.Key.A && paged) { advanced.Press(); Land(); return true; }
            return false;
        }
        internal void OpenPage(string name)
        {
            name = (name ?? "main").Trim().ToLowerInvariant();
            if (name == "mods") { OpenMods(); return; }
            if (name == "remote") { OpenRemote(); return; }
            ShowSettings(!string.IsNullOrEmpty(name) && name != "main" && name != "brain" && name != "voice");
            if (paged && name != "settings" && !pet.Saved.AdvancedSettings) { pet.SetAdvancedSettings(true); ShowSettings(true); }
            if (name == "layers") { OpenLayers(); return; }
            if (name == "security") { OpenSecurity(); return; }
            if (name == "personality" || name == "memories") name = "brain";
            if (paged && name != "settings")
            {
                foreach (Item item in items) if (item.Control is NeonHead head && string.Equals(head.Text, name, StringComparison.OrdinalIgnoreCase) && item.Control.IsVisible) { GlideTo(new Vector(0, Math.Max(0, Canvas.GetTop(item.Control) - 12) * Zoom)); break; }
            }
        }
        private static bool In(double y, int[][] bands) { foreach (int[] band in bands) if (y >= band[0] && y < band[1]) return true; return false; }
        private static double Lift(double y, int[][] missing, int[][] folded, bool simple)
        {
            double gone = 0;
            for (int at = 0; at < y; at++) if (In(at, missing) || simple && In(at, folded)) gone++;
            return y - gone;
        }
        private void ArrangePage()
        {
            bool simple = !pet.Saved.AdvancedSettings;
            int tier = pet.MainframeTier, voice = pet.MainframeVoiceMode;
            double extra = Expanded ? 118 : 0, shift = extra + (tier == 0 ? 94 : 128 + (paidKey != null ? 86 : 0));
            double fold = voice == 0 ? 72 : voice == 3 && pet.MainframeVoiceKey != null ? 0 : 36;
            double bottom = 0;
            foreach (Item item in items)
            {
                Control control = item.Control;
                double x = item.X, y = item.Y;
                bool show = item.Page == -1 || item.Page == (paged ? 1 : 0);
                if (item.Page == -1 && (control == build || control == minimize || control == close)) x += paged ? 380 : 0;
                if (item.Page == 1 && control != advanced)
                {
                    bool right = x >= 380;
                    double anchor = y + (control is NeonHead ? 5 : 0);
                    if (simple && In(anchor, right ? FoldedRight : FoldedLeft)) show = false;
                    int[][] missing = right ? MissingRight : MissingLeft;
                    if (!right && pet.MainframeInputSetup) missing = new[] { new[] { 428, 648 }, new[] { 672, 700 }, new[] { 910, 1121 }, new[] { 1150, 1314 }, new[] { 1342, 1728 } };
                    y = Lift(anchor, missing, right ? FoldedRight : FoldedLeft, simple) - (control is NeonHead ? 5 : 0);
                    if (right && !simple && anchor >= 466 && control != huskMode && control != huskLabel && control != thinkingLevel && control != thinkingLabel && control != thinkingExtra && control != extraLabel) y += control == huskVerbosity ? 84 : 112;
                }
                if (item.Page == 0)
                {
                    if (control == setup || item.Y == 264) show &= Expanded;
                    if (item.Y == 138 || item.Y == 160 || item.Y == 182) show &= Expanded;
                    if (control == models) show &= !Expanded;
                    if (control == refresh) y = Expanded ? 226 : 134;
                    if (control == tierNote || Array.IndexOf(brains, control) >= 0 || control == freeKeys || control == textKey || control == textConnect || item.Y == 294) y += extra;
                    if (control == textKey || control == textConnect) show &= tier > 0;
                    if (item.Y == 294) show &= tier == 1;
                    if (item.Y == 315 || item.Y == 350) show &= tier == 1 && paidKey != null;
                    if (control == freeKeys) show &= tier == 1;
                    bool below = control is NeonHead head && head.Text != "BRAIN" || item.Y >= 499 || Array.IndexOf(voices, control) >= 0 || item.Y == 257 || item.Y == 281 || item.Y == 310 || item.Y == 346;
                    if (below)
                    {
                        y += shift;
                        if (!OperatingSystem.IsLinux() && item.Y >= 256) y -= 24;
                        if (item.Y >= 499) y -= 117 + fold;
                        // A section control starts five pixels above its authored band.
                        // Move CAPRI's heading with its rows past the name controls.
                        if (OperatingSystem.IsLinux() && control != nameListeningNote &&
                            (item.Y >= 539 || control is NeonHead band && band.Text == "CAPRI")) y += 70;
                    }
                    if (item.Y == 310) show &= voice == 3;
                    if (item.Y == 346)
                    {
                        if (control == customVoice) { show &= voice == 2; y -= 36; }
                        else show &= voice == 3 && pet.MainframeVoiceKey != null;
                    }
                }
                if (item.When != null) show &= item.When();
                control.IsVisible = show;
                Canvas.SetLeft(control, x); Canvas.SetTop(control, y);
                if (show) bottom = Math.Max(bottom, y + control.Height);
            }
            Fit(paged ? 760 : 380, bottom + 28, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0);
        }
        private void CloseMenus() { voicePicker?.CloseMenu(); foreach (Item item in items) if (item.Control is NeonPicker picker) picker.CloseMenu(); }
        private void ConnectBrain()
        {
            string key = (textKey.Box.Text ?? "").Trim(); textKey.Box.Text = "";
            if (pet.MainframeTier == 1 && Cloud.KindOf(key) != null && !Cloud.MediumKind(Cloud.KindOf(key))) { paidKey = key; layout = null; Sync(); return; }
            paidKey = null;
            if (key.Length == 0) { if (pet.MainframeBrainKey != null) pet.MainframeForgetBrain(); }
            else pet.SaveBrainKey(key, pet.MainframeTier);
        }
        private void ConnectVoice()
        {
            string key = (voiceKey.Box.Text ?? "").Trim(); voiceKey.Box.Text = "";
            if (key.Length == 0) { if (pet.MainframeVoiceKey != null) pet.MainframeForgetVoice(); }
            else pet.SaveVoiceKey(key);
        }
        private void UseServer() { if (string.IsNullOrWhiteSpace(localAddress.Box.Text)) pet.LeaveServer(); else pet.UseServer(localAddress.Box.Text); }
        internal void Change(string name, int value) { changes[name](value); Sync(); }
        internal void PickBrainTier(int tier) { brains[tier].Press(); Sync(); }
        internal void PickVoiceTier(int tier) { voices[tier].Press(); Sync(); }
        internal void Press(string text) { foreach (Item item in items) if (item.Control is NeonButton button && button.Text == text && button.IsVisible) { button.Press(); return; } throw new Exception("No Mainframe button: " + text); }
        internal NeonButton Button(string text) { foreach (Item item in items) if (item.Control is NeonButton button && button.Text == text && button.IsVisible) return button; return null; }
        internal Control Row(string name) { return rows[name]; }
        internal string Value(string name)
        {
            Control row = rows[name];
            if (row is NeonSlider slider) return slider.Shown;
            if (row is NeonSwitch toggle) return toggle.Checked ? "On" : "Off";
            if (row is NeonButton key) return key.Text;
            return (row as NeonEntry)?.Box.Text;
        }
        internal string Outside()
        {
            var page = new Rect(0, 0, Page.Width, Page.Height);
            foreach (Item item in items) if (item.Control.IsVisible && !page.Contains(new Rect(Canvas.GetLeft(item.Control), Canvas.GetTop(item.Control), item.Control.Width, item.Control.Height))) return NameOf(item.Control);
            return null;
        }
        private static string NameOf(Control control) { return AutomationProperties.GetName(control) ?? (control as NeonCaption)?.Text ?? (control as NeonHead)?.Text ?? control.GetType().Name; }
        private static Rect Box(Control control)
        {
            double x = Canvas.GetLeft(control), y = Canvas.GetTop(control);
            if (control is NeonHead head) return new Rect(x + 14, y + 5, MainframeTheme.Words(head.Text, 40.0 / 3, true).Width, 19);
            return new Rect(x, y, control.Width, control.Height);
        }
        internal string Overlap()
        {
            for (int i = 0; i < items.Count; i++)
            {
                Control a = items[i].Control; if (!a.IsVisible) continue;
                for (int j = i + 1; j < items.Count; j++)
                {
                    Control b = items[j].Control; if (!b.IsVisible) continue;
                    Rect both = Box(a).Intersect(Box(b));
                    if (both.Width > 0.5 && both.Height > 0.5) return NameOf(a) + " / " + NameOf(b);
                }
            }
            return null;
        }
        internal string TextFits()
        {
            foreach (Item item in items)
            {
                Control control = item.Control; if (!control.IsVisible) continue;
                string wrong = control is NeonCaption caption ? caption.TextFits() : control is NeonSwitch toggle ? toggle.TextFits() : control is NeonSlider slider ? slider.TextFits() : control is NeonButton button ? button.TextFits() : control is NeonEntry entry ? entry.TextFits() : null;
                if (wrong != null) return NameOf(control) + ": " + wrong;
            }
            return null;
        }
        private void ShowSheet(MainframeWindow sheet)
        {
            sheets.Add(sheet); sheet.Closed += (_, _) => sheets.Remove(sheet); sheet.Show(this);
        }
        internal void OpenServer() { ShowSheet(new MainframeSheet(pet, "CUSTOM LOCAL", "server")); }
        internal void OpenThinkingExtra() { ShowSheet(new MainframeSheet(pet, "EXTRA REQUEST JSON", "thinking")); }
        internal void OpenLayers() { ShowSheet(new MainframeSheet(pet, "VOICE LAYERS", "layers")); }
        internal void OpenSecurity() { ShowSheet(new MainframeSheet(pet, "SECURITY", "security")); }
        internal void OpenVault() { ShowSheet(new MainframeSheet(pet, "MEMORY VAULT", "vault")); }
        internal void OpenMods()
        {
            if (Mods == null)
            {
                ModsWindow sheet = new ModsWindow(pet);
                Mods = sheet;
                sheet.Closed += delegate { Mods = null; };
                ShowSheet(sheet);
            }
            else { Mods.WindowState = WindowState.Normal; Mods.Activate(); }
        }
        internal void OpenRemote()
        {
            if (Remote == null)
            {
                RemoteSheet sheet = new RemoteSheet(pet.ReadRemote, pet.SetRemote, pet.NewRemoteCode, pet.CloseRemoteCode, pet.AllowRemote, pet.DenyRemote, pet.RevokeRemote, pet.RemoveRemotePhones, pet.SetRemoteHost, Zoom);
                Remote = sheet;
                sheet.Closed += delegate { Remote = null; };
                ShowSheet(sheet);
            }
            else { Remote.WindowState = WindowState.Normal; Remote.Activate(); }
        }
    }
}
