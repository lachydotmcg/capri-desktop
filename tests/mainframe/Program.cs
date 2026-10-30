using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CapriDesktop;

internal static class Entry
{
    internal static string Output;
    [STAThread] private static int Main(string[] args)
    {
        Output = Path.GetFullPath(args[0]); Directory.CreateDirectory(Output);
        return AppBuilder.Configure<Checks>().UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
    }
}
internal sealed class Checks : Application
{
    private readonly List<string> checks = new();
    public override void Initialize() { Styles.Add(new FluentTheme()); }
    public override void OnFrameworkInitializationCompleted()
    {
        base.OnFrameworkInitializationCompleted();
        Dispatcher.UIThread.Post(Run);
    }
    private static string Saved(Settings settings)
    {
        using var stream = new MemoryStream(); new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, settings); return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }
    private void Check(bool condition, string name) { if (!condition) throw new Exception(name); checks.Add("PASS " + name); }
    private static async Task Painted()
    {
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Render);
        await Task.Delay(180);
    }
    private static void Capture(Window window, string name)
    {
        double dpi = window.RenderScaling;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(window.Bounds.Width * dpi), (int)Math.Ceiling(window.Bounds.Height * dpi)), new Vector(96 * dpi, 96 * dpi));
        bitmap.Render(window); bitmap.Save(Path.Combine(Entry.Output, name + ".png"));
    }
    private async void Run()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime;
        Pet pet = null;
        try
        {
            CapriDesktop.Program.Smoke = Entry.Output;
            Environment.SetEnvironmentVariable("HYPRLAND_INSTANCE_SIGNATURE", null);
            string display = Environment.GetEnvironmentVariable("DISPLAY");
            Environment.SetEnvironmentVariable("DISPLAY", null);
            try { pet = new Pet(desktop); }
            finally { Environment.SetEnvironmentVariable("DISPLAY", display); }
            var before = Saved(pet.Saved); int permissions = Security.Mask;
            var frame = new Mainframe(pet); frame.Show(); await Painted();
            Check(Saved(pet.Saved) == before && permissions == Security.Mask, "opening Mainframe preserves settings and grants");
            Check(!pet.VoiceChatting, "opening settings does not start microphone capture");
            Capture(frame, "primary-100");
            frame.ShowSettings(true); await Painted(); Capture(frame, "settings-100");
            var toggles = frame.GetVisualDescendants().OfType<NeonSwitch>().ToList();
            NeonSwitch dance = toggles.Single(c => AutomationProperties.GetName(c) == "Dance to music");
            dance.IsChecked = false; Check(pet.Saved.NoDance, "native switch changes actual Pet setting");
            pet.SetDanceToMusic(true); Check(dance.IsChecked == true, "existing menu setter synchronizes native switch");
            var pitch = frame.GetVisualDescendants().OfType<NeonSlider>().Single(s => AutomationProperties.GetName(s.Slider) == "Pitch");
            pitch.Slider.Value = 9; Check(pet.Saved.Pitch == 3 && VoiceFx.Pitch == 3, "native discrete slider changes actual DSP pitch");
            if (Environment.GetEnvironmentVariable("CAPRI_PRIVATE_UI_TEST") == "1")
            {
                frame.Activate(); await Painted();
                var pointer = pitch.Slider.PointToScreen(new Point(pitch.Slider.Bounds.Width * 0.9, pitch.Slider.Bounds.Height / 2));
                checks.Add("INFO pointer target " + pointer + " frame " + frame.Position + " slider " + pitch.Slider.Bounds);
                foreach (var c in pitch.Slider.GetVisualDescendants().OfType<Control>()) checks.Add("INFO slider control " + c.GetType().Name + " " + c.Name + " " + c.Bounds + " visible=" + c.IsVisible + " hit=" + c.IsHitTestVisible);
                frame.AddHandler(InputElement.PointerPressedEvent, (_, e) => checks.Add("INFO native press " + e.Source?.GetType().Name + " at " + e.GetPosition(frame) + " parents " + string.Join("/", ((Visual)e.Source).GetVisualAncestors().Select(v => v.GetType().Name))), Avalonia.Interactivity.RoutingStrategies.Tunnel, true);
                await PrivatePointer.Click(pointer); await Painted();
                checks.Add("INFO after pointer pitch " + pet.Saved.Pitch);
                Capture(frame, "pointer-click");
                Check(pet.Saved.Pitch >= 4, "actual X11 pointer on the slider track changes the setting");
                pitch.Slider.Focus(); int previous = pet.Saved.Pitch;
                await PrivatePointer.Key(0xff51); await Painted();
                Check(pet.Saved.Pitch == previous - 1, "actual keyboard arrow changes the focused slider by one step");
                var nickname = frame.GetVisualDescendants().OfType<TextBox>().Single(c => AutomationProperties.GetName(c) == "What should she call you?");
                frame.Activate(); nickname.Focus(); await Painted();
                var inputBorder = nickname.GetVisualDescendants().OfType<Border>().Single(c => c.Name == "PART_BorderElement");
                Check(nickname.IsFocused && ((ISolidColorBrush)inputBorder.Background).Color == ((ISolidColorBrush)MainframeTheme.Field).Color,
                    "focused native input keeps the dark Mainframe field palette");
                Check(((ISolidColorBrush)inputBorder.BorderBrush).Color == ((ISolidColorBrush)MainframeTheme.Cyan).Color,
                    "focused native input has a visible cyan focus border");
                if (Environment.GetEnvironmentVariable("CAPRI_PRIVATE_CLIPBOARD_TEST") == "1")
                {
                    await frame.Clipboard.SetTextAsync("fixture choom");
                    await PrivatePointer.Paste(); await Painted();
                    Check(nickname.Text == "fixture choom", "native text input accepts clipboard paste");
                }
                else
                {
                    foreach (char character in "fixture choom") await PrivatePointer.Key(character);
                    await Painted();
                    Check(nickname.Text == "fixture choom", "actual keyboard text input reaches the native text box");
                }
                await PrivatePointer.Key(0xff0d); await Painted();
                Check(pet.Saved.Nickname == "fixture choom", "Enter saves the nickname through the existing settings service");
            }
            var voiceBefore = pet.Saved.Volume;
            var zoom = frame.GetVisualDescendants().OfType<NeonSlider>().Single(s => AutomationProperties.GetName(s.Slider) == "Mainframe size");
            zoom.Slider.Value = 3; Check(pet.MainframeSize == 0, "Mainframe zoom waits for release");
            zoom.Slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Right });
            Check(pet.MainframeSize == 150 && pet.Saved.Volume == voiceBefore, "zoom release persists without retuning voice");
            await Painted(); Capture(frame, "settings-150");
            pet.SetMainframeSize(200); frame.ShowSettings(true); await Painted(); Capture(frame, "settings-200");
            zoom.Slider.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = Key.Right });
            Check(pet.MainframeSize == 200, "releasing unchanged zoom preserves the currently saved size");
            var area = frame.Screens.ScreenFromWindow(frame).WorkingArea;
            Check(frame.Width * frame.RenderScaling <= area.Width && frame.Height * frame.RenderScaling <= area.Height, "200 percent window stays inside available work area");
            var scroll = frame.GetVisualDescendants().OfType<ScrollViewer>().First();
            Check(scroll.Extent.Width > scroll.Viewport.Width && scroll.Extent.Height > scroll.Viewport.Height, "large settings remain reachable through both scroll axes");
            scroll.Offset = new Vector(scroll.Extent.Width, scroll.Extent.Height); await Painted(); Capture(frame, "settings-200-scrolled");
            pet.SetMainframeSize(100); frame.ShowSettings(false); await Painted();
            frame.OpenSecurity(); await Painted(); var security = desktop.Windows.Single(w => w.Title == "Capri Security"); Capture(security, "security-100");
            Check(Security.Mask == permissions, "opening Security does not change grants"); security.Close();
            var layersBefore = Saved(pet.Saved); frame.OpenLayers(); await Painted(); var layers = desktop.Windows.Single(w => w.Title == "Capri Voice layers"); Capture(layers, "layers-100");
            Check(Saved(pet.Saved) == layersBefore, "opening Voice Layers does not rewrite tuned values"); layers.Close();
            frame.OpenVoiceSetup(false); await Painted(); var setup = desktop.Windows.Single(w => w.Title == "Capri Voice setup"); Capture(setup, "voice-setup-100"); setup.Close();
            foreach (double size in Pet.Sizes)
            {
                pet.Resize(size); var encoded = Saved(pet.Saved);
                using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(encoded));
                var restored = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                Check(restored.Size == size && Array.IndexOf(Pet.Sizes, restored.Size) >= 0, "character size " + size + " persists and passes validation");
            }
            bool invalid = false; try { pet.Resize(999); } catch (ArgumentOutOfRangeException) { invalid = true; }
            Check(invalid, "unsupported character size is rejected without resizing");
            var final = Saved(pet.Saved); frame.Close();
            string smoke = CapriDesktop.Program.Smoke; CapriDesktop.Program.Smoke = null;
            pet.Save(); CapriDesktop.Program.Smoke = smoke;
            string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri", "cross.json");
            Check(File.Exists(file), "actual settings save writes the isolated profile");
            using (var stream = File.OpenRead(file))
            {
                var stored = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                Check(stored.MainframeSize == pet.Saved.MainframeSize && stored.Pitch == pet.Saved.Pitch && stored.Nickname == pet.Saved.Nickname, "zoom and changed settings roundtrip through the actual saved file");
            }
            var reopened = new Mainframe(pet); reopened.Show(); await Painted();
            Check(Saved(pet.Saved) == final, "close/reopen preserves current settings");
            var typeface = new Typeface(MainframeTheme.Text).GlyphTypeface;
            Check(typeface.FamilyName.Contains("Barlow"), "bundled Barlow family actually resolves");
            checks.Add("INFO resolved font: " + typeface.FamilyName);
            reopened.Close();
            File.WriteAllLines(Path.Combine(Entry.Output, "checks.txt"), checks); pet.Finish(0);
        }
        catch (Exception error)
        {
            checks.Add("FAIL " + error); File.WriteAllLines(Path.Combine(Entry.Output, "checks.txt"), checks);
            if (pet != null) pet.Finish(1); else desktop.Shutdown(1);
        }
    }
}

internal static class PrivatePointer
{
    [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
    [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] private static extern byte XKeysymToKeycode(IntPtr display, ulong symbol);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(IntPtr display, int screen, int x, int y, ulong delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(IntPtr display, uint button, bool press, ulong delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(IntPtr display, uint key, bool press, ulong delay);
    internal static async Task Click(PixelPoint point)
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        try
        {
            XTestFakeMotionEvent(display, -1, point.X, point.Y, 0); XFlush(display); await Task.Delay(40);
            XTestFakeButtonEvent(display, 1, true, 0); XFlush(display); await Task.Delay(40);
            XTestFakeButtonEvent(display, 1, false, 0); XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }
    internal static async Task Key(ulong symbol)
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        try
        {
            byte key = XKeysymToKeycode(display, symbol); XTestFakeKeyEvent(display, key, true, 0); XFlush(display); await Task.Delay(40);
            XTestFakeKeyEvent(display, key, false, 0); XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }
    internal static async Task Paste()
    {
        IntPtr display = XOpenDisplay(IntPtr.Zero);
        try
        {
            byte control = XKeysymToKeycode(display, 0xffe3), v = XKeysymToKeycode(display, 0x76);
            XTestFakeKeyEvent(display, control, true, 0); XTestFakeKeyEvent(display, v, true, 0); XFlush(display); await Task.Delay(40);
            XTestFakeKeyEvent(display, v, false, 0); XTestFakeKeyEvent(display, control, false, 0); XFlush(display);
        }
        finally { XCloseDisplay(display); }
    }
}
