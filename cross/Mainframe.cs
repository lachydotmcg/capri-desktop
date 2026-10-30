using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal class MainframeWindow : Window
    {
        internal readonly StackPanel Body = new StackPanel();
        protected readonly StackPanel HeaderActions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right };
        private readonly LayoutTransformControl zoom;
        private readonly ScrollViewer scroll;
        private readonly CancellationTokenSource nativeLifetime = new CancellationTokenSource();
        private CancellationTokenSource nativePlacement;
        private bool nativeClosed;
        private Size desiredViewport = new Size(384, 764);
        private double columns = 1, wantedHeight = 760;
        internal event Action<string> PlacementFailed;
        internal MainframeWindow(string title)
        {
            Title = "Capri " + title; SystemDecorations = SystemDecorations.None; CanResize = true;
            Background = Brushes.Transparent; TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            ShowInTaskbar = true; MinWidth = 280; MinHeight = 240;
            FontFamily = MainframeTheme.Text; FontSize = 14; Foreground = MainframeTheme.White;
            var titleText = MainframeTheme.Label(title.ToUpperInvariant(), MainframeTheme.Rose, 25); titleText.FontFamily = MainframeTheme.Display; titleText.FontWeight = FontWeight.SemiBold;
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(24, 16, 24, 12), MinHeight = 28 };
            header.Children.Add(titleText); header.Children.Add(HeaderActions); Grid.SetColumn(HeaderActions, 1);
            HeaderActions.Children.Add(MainframeTheme.Button("—", () => WindowState = WindowState.Minimized, "Minimize " + title));
            HeaderActions.Children.Add(MainframeTheme.Button("×", Close, "Close " + title));
            header.PointerPressed += (_, e) => { if (e.Source == header || e.Source == titleText) BeginMoveDrag(e); };
            var layout = new StackPanel(); layout.Children.Add(header); layout.Children.Add(Body);
            zoom = new LayoutTransformControl { Child = layout };
            scroll = new ScrollViewer { Content = zoom, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Margin = new Thickness(2) };
            var gradient = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 0.42, RelativeUnit.Relative) };
            gradient.GradientStops.Add(new GradientStop(Color.Parse("#360E1A"), 0)); gradient.GradientStops.Add(new GradientStop(Color.Parse("#0A0A10"), 1));
            Content = new NeonPlate { Frame = true, Fill = gradient, Stroke = MainframeTheme.Brim, Child = scroll };
            Opened += (_, _) => { ResizeFrame(CurrentZoom, columns, wantedHeight); BringToActiveOutput(); };
            Closed += (_, _) => { nativeClosed = true; nativeLifetime.Cancel(); nativeLifetime.Dispose(); };
            ScalingChanged += (_, _) => ResizeFrame(CurrentZoom, columns, wantedHeight);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
        }
        internal double CurrentZoom { get; private set; } = 1;
        internal async void BringToActiveOutput(bool activate = true)
        {
            var compositor = HyprlandDesktop.Current;
            if (compositor == null || nativeClosed) return;
            nativePlacement?.Cancel();
            using var placement = CancellationTokenSource.CreateLinkedTokenSource(nativeLifetime.Token);
            nativePlacement = placement;
            CancellationToken token = placement.Token;
            Exception problem = null;
            Vector? measured = null;
            double measuredScaling = RenderScaling;
            try
            {
                for (int attempt = 0; attempt < 6; attempt++)
                {
                    try
                    {
                        var observed = await compositor.ObserveOwnedWindowAsync(Title, token);
                        var screen = MainframePlacementGeometry.Match(observed.Output, Screens.All);
                        if (measuredScaling != RenderScaling) { measured = null; measuredScaling = RenderScaling; }
                        if (screen == null) measured ??= MainframePlacementGeometry.Measure(observed.Window.Logical.Size, Bounds.Size);
                        Size requested = desiredViewport;
                        MainframeViewport viewport = MainframePlacementGeometry.Fit(observed.Output, observed.Window.Logical.Size, Bounds.Size,
                            requested, new Size(MinWidth, MinHeight), screen, RenderScaling, measured);
                        Width = viewport.Dip.Width; Height = viewport.Dip.Height;
                        Point origin = MainframePlacementGeometry.Center(observed.Output.WorkingArea, viewport.Logical);
                        await compositor.PlaceOwnedWindowAsync(Title, origin, observed.Workspace, token, observed, viewport.Logical);
                        var placed = await compositor.ObserveOwnedWindowAsync(Title, token);
                        if (requested == desiredViewport && placed.Window.Address == observed.Window.Address && placed.Output.Id == observed.Output.Id && placed.Visible &&
                            Math.Abs(placed.Window.Logical.Width - viewport.Logical.Width) <= 1 && Math.Abs(placed.Window.Logical.Height - viewport.Logical.Height) <= 1)
                        {
                            if (activate && IsVisible) Activate();
                            return;
                        }
                        problem = new InvalidOperationException("Mainframe is waiting for its resized window to fit the active work area.");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                    catch (Exception error) { problem = error; }
                    try { await Task.Delay(80, token); } catch (OperationCanceledException) { return; }
                }
                PlacementFailed?.Invoke("Window placement: " + problem?.Message);
            }
            finally { if (nativePlacement == placement) nativePlacement = null; }
        }
        internal void ResizeFrame(double scale, double columnCount, double height)
        {
            CurrentZoom = scale; columns = columnCount; wantedHeight = height;
            zoom.LayoutTransform = new ScaleTransform(scale, scale);
            Body.Width = 380 * columnCount;
            var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
            double dpi = screen?.Scaling ?? RenderScaling;
            var area = screen?.WorkingArea ?? new PixelRect(0, 0, 1280, 900);
            desiredViewport = new Size(Math.Min(380 * columnCount * scale + 4, Math.Max(280, area.Width / dpi - 32)),
                Math.Min(height * scale + 4, Math.Max(240, area.Height / dpi - 48)));
            Width = desiredViewport.Width; Height = desiredViewport.Height;
            if (IsVisible)
            {
                if (HyprlandDesktop.Current != null) { BringToActiveOutput(false); return; }
                int maxX = area.Right - (int)Math.Ceiling(Width * dpi), maxY = area.Bottom - (int)Math.Ceiling(Height * dpi);
                Position = new PixelPoint(Math.Clamp(Position.X, area.X, Math.Max(area.X, maxX)), Math.Clamp(Position.Y, area.Y, Math.Max(area.Y, maxY)));
            }
        }
        internal static StackPanel Column() { return new StackPanel { Width = 380, Margin = new Thickness(0, 0, 0, 20) }; }
        internal static void Section(StackPanel panel, string name, Control action = null)
        {
            var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(28, 2, 24, 2) };
            var text = MainframeTheme.Label(name + " >", MainframeTheme.Rose, 14); text.FontWeight = FontWeight.SemiBold;
            grid.Children.Add(text);
            if (action != null) { grid.Children.Add(action); Grid.SetColumn(action, 1); }
            panel.Children.Add(new Border { Background = MainframeTheme.Band, BorderBrush = MainframeTheme.Rule, BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 8, 0, 8), MinHeight = 25, Child = grid });
        }
        internal static Grid Row(StackPanel panel, string name, Control control)
        {
            var row = new Grid { ColumnDefinitions = new ColumnDefinitions("150,*"), Margin = new Thickness(28, 2, 24, 2), MinHeight = 26 };
            row.Children.Add(MainframeTheme.Label(name)); row.Children.Add(control); Grid.SetColumn(control, 1); panel.Children.Add(row); return row;
        }
        internal static TextBlock Note(StackPanel panel, string text)
        {
            var note = MainframeTheme.Label(text, MainframeTheme.Faint, 12); note.TextWrapping = TextWrapping.Wrap; note.Margin = new Thickness(28, 4, 24, 6); panel.Children.Add(note); return note;
        }
        internal static void Wide(StackPanel panel, Control control) { control.Margin = new Thickness(28, 3, 24, 3); panel.Children.Add(control); }
        internal static void Buttons(StackPanel panel, params Control[] buttons)
        {
            var grid = new UniformGrid { Columns = buttons.Length, Margin = new Thickness(28, 3, 24, 3) };
            foreach (Control button in buttons) { button.Margin = new Thickness(0, 0, 5, 0); grid.Children.Add(button); }
            panel.Children.Add(grid);
        }
    }

    internal sealed class Mainframe : MainframeWindow
    {
        private readonly Pet pet;
        private readonly List<Action> readers = new List<Action>();
        private readonly List<Window> sheets = new List<Window>();
        private readonly Dictionary<Window, TextBlock> sheetMessages = new Dictionary<Window, TextBlock>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly DispatcherTimer statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        private readonly StackPanel primary = Column();
        private readonly Grid settings = new Grid { ColumnDefinitions = new ColumnDefinitions("380,380") };
        private readonly TextBlock brainStatus, voiceStatus, earsStatus, operation;
        private readonly NeonPicker modelPicker, micPicker;
        private bool syncing, paged, closed;
        private string[] models = Array.Empty<string>();
        private List<Mic> microphones = new List<Mic>();
        private Ears micTest;
        private CancellationTokenSource micTestJob;
        private static readonly int[] Amounts = { 0, 5, 10, 15, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        private static readonly string[] FrameNames = { "Auto", "100%", "125%", "150%", "175%", "200%" };

        internal Mainframe(Pet owner) : base("Mainframe")
        {
            pet = owner;
            PlacementFailed += Report;
            var settingsButton = MainframeTheme.Button("Settings", () => ShowSettings(!paged)); settingsButton.Accent = true;
            HeaderActions.Children.Insert(0, settingsButton);
            Body.Children.Add(primary); Body.Children.Add(settings); settings.IsVisible = false;
            Section(primary, "BRAIN");
            Choice(primary, "Provider", new[] { "Codex / ChatGPT plan", "Local · Ollama", "Existing API provider" },
                () => pet.BrainProvider == "codex" ? 0 : pet.BrainProvider == "cloud" ? 2 : 1,
                i => Run(() => pet.SelectBrainProviderAsync(new[] { "codex", "ollama", "cloud" }[i])));
            brainStatus = Note(primary, "");
            modelPicker = new NeonPicker("Brain model", Array.Empty<string>(), i => { if (!syncing && i < models.Length) Run(() => pet.SelectBrainModelAsync(models[i])); });
            Row(primary, "Model", modelPicker);
            var disconnect = MainframeTheme.Button("Disconnect", pet.DisconnectCodex);
            readers.Add(() => disconnect.IsEnabled = pet.BrainProvider == "codex");
            Buttons(primary, MainframeTheme.Button("Connect / Retry", () => { if (pet.BrainProvider == "codex") Run(pet.ConnectCodexAsync); else if (pet.BrainProvider == "cloud") OpenApiSetup(); else Run(pet.RefreshBrainModelsAsync); }),
                MainframeTheme.Button("Refresh", () => Run(pet.RefreshBrainModelsAsync)), disconnect);
            Buttons(primary, MainframeTheme.Button("Local setup", OpenBrainSetup), MainframeTheme.Button("API key setup", OpenApiSetup));
            Section(primary, "VOICE");
            Choice(primary, "Voice", new[] { "Off · text only", "Prebuilt local", "Custom local", "Existing cloud voice" }, () => pet.MainframeVoiceMode, pet.MainframeSelectVoiceMode);
            string[] styles = LocalVoice.Styles.Select(s => s.Name).ToArray();
            Choice(primary, "Style", styles, () => Array.IndexOf(styles, pet.Saved.FreeStyle ?? "Her own"), i => pet.MainframeSelectVoice(styles[i]));
            voiceStatus = Note(primary, "");
            Buttons(primary, MainframeTheme.Button("Set up", () => OpenVoiceSetup(false)), MainframeTheme.Button("Test", pet.HearStyle), MainframeTheme.Button("Stop", pet.StopTalking));
            Buttons(primary, MainframeTheme.Button("Voice layers", OpenLayers), MainframeTheme.Button("Cloud voice", () => OpenVoiceSetup(true)));
            Section(primary, "MICROPHONE");
            micPicker = new NeonPicker("Microphone", new[] { "System default" }, i => { if (!syncing) pet.MainframeSelectMicrophone(i == 0 ? null : microphones[i - 1].Id); });
            Wide(primary, micPicker);
            earsStatus = Note(primary, "");
            Buttons(primary, MainframeTheme.Button("Refresh devices", () => Run(RefreshMicrophones)), MainframeTheme.Button("Test mic", () => Run(TestMicrophone)), MainframeTheme.Button("TALK", pet.ToggleTalk));
            Section(primary, "CAPRI");
            Buttons(primary, MainframeTheme.Button("Bring here", pet.Bring), MainframeTheme.Button("Hide / Show", () => { if (pet.Away) pet.ComeBack(); else pet.HideAway(); }), MainframeTheme.Button("Sit here", () => pet.MoveSelf("sit", null)));
            Buttons(primary, MainframeTheme.Button("Type", pet.Talk.Open), MainframeTheme.Button("Quiet", pet.ToggleShush), MainframeTheme.Button("Stop", pet.StopTalking));
            Buttons(primary, MainframeTheme.Button("Updates", OpenUpdates), MainframeTheme.Button("Help", () => Pet.Browse(Pet.HelpPage)), MainframeTheme.Button("Quit Capri", pet.Quit));
            operation = Note(primary, ""); operation.Foreground = MainframeTheme.Rose;
            Note(primary, "v" + typeof(Pet).Assembly.GetName().Version.ToString(3));
            BuildSettings();
            pet.SettingsChanged += Refresh; pet.BrainConnectionChanged += BrainChanged;
            statusTimer.Tick += (_, _) => RefreshStatus(); statusTimer.Start();
            Closed += (_, _) =>
            {
                closed = true; lifetime.Cancel(); micTestJob?.Cancel(); micTest?.Dispose(); micTest = null; statusTimer.Stop(); pet.SettingsChanged -= Refresh; pet.BrainConnectionChanged -= BrainChanged;
                foreach (Window sheet in sheets.ToArray()) sheet.Close(); lifetime.Dispose();
            };
            Refresh(); Rezoom();
        }

        private void BuildSettings()
        {
            var left = Column(); var right = Column(); settings.Children.Add(left); settings.Children.Add(right); Grid.SetColumn(right, 1);
            Section(left, "YOU");
            string[] languages = Enumerable.Range(0, Brain.Languages.GetLength(0)).Select(i => Brain.Languages[i, 0]).ToArray();
            Choice(left, "Language", languages, () => Enumerable.Range(0, languages.Length).FirstOrDefault(i => Brain.Languages[i, 1] == (Brain.Language ?? "English")), i => pet.SetLanguage(Brain.Languages[i, 1]));
            var nickname = MainframeTheme.Input("What should she call you?");
            readers.Add(() => { if (!nickname.IsKeyboardFocusWithin) nickname.Text = pet.Saved.Nickname ?? ""; });
            Wide(left, nickname); nickname.KeyDown += (_, e) => { if (e.Key == Key.Enter) { pet.SetNickname(nickname.Text); e.Handled = true; } };
            Buttons(left, MainframeTheme.Button("Save name", () => pet.SetNickname(nickname.Text)), MainframeTheme.Button("Get to know me", pet.GetToKnow));
            Wide(left, MainframeTheme.Button("Open her notes", () => Report(pet.OpenNotes())));
            Section(left, "VOICE", MainframeTheme.Button("Layers", OpenLayers));
            Slide(left, "Volume", Pet.VolumeNames, () => Nearest(Pet.Volumes, pet.Saved.Volume ?? 100), i => pet.SetVolume(Pet.Volumes[i]));
            Slide(left, "Pitch", Pet.PitchNames, () => pet.Saved.Pitch + 6, i => pet.SetPitch(i - 6), pet.HearStyle);
            Slide(left, "Speed", Pet.PaceNames, () => Nearest(Pet.Paces, pet.Saved.Pace ?? 100), i => pet.SetPace(Pet.Paces[i]), pet.HearStyle);
            string[] amounts = Amounts.Select(a => a == 0 ? "Off" : a + "%").ToArray();
            Slide(left, "Robot", amounts, () => Nearest(Amounts, pet.Saved.Robot), i => pet.SetRobot(Amounts[i]), pet.HearStyle);
            Slide(left, "Glitch", amounts, () => Nearest(Amounts, pet.Saved.Glitch), i => pet.SetGlitch(Amounts[i]), pet.HearStyle);
            Slide(left, "Sounds", Enumerable.Range(0, 11).Select(i => i == 0 ? "Off" : i * 10 + "%").ToArray(), () => (pet.Saved.Sounds ?? 60) / 10, i => pet.SetSounds(i * 10));
            Switch(left, "Typing sound", () => pet.TypingSound, pet.SetTyping);
            Note(left, "Free voice in games: HD GPU rest is not available on this backend.");
            Section(left, "MOVES");
            Choice(left, "Wander", Pet.WanderNames, () => pet.Saved.Wander, pet.SetWander);
            Choice(left, "Sleep", Pet.SleepNames, () => Array.IndexOf(Pet.Sleeps, pet.SleepAfter), i => pet.SetSleepAfter(Pet.Sleeps[i]));
            Choice(left, "Pop-ins", Pet.PopNames, () => pet.PopPace, pet.SetPopPace);
            Switch(left, "Dance to music", () => !pet.Saved.NoDance, pet.SetDanceToMusic);
            Switch(left, "Glitch in at startup", () => !pet.Saved.NoIntro, pet.SetIntro);
            Note(left, "Mini player is not included in this port.");
            Section(left, "KEYS");
            Note(left, "Global Talk, Mute mic, Type, Hide/show and Quiet shortcuts are not registered on this Linux backend. Use the TALK pill, tray or Mainframe actions. Focused-window keys are not global shortcuts.");
            Section(right, "CAPRI");
            Slide(right, "Size", Pet.SizeNames, () => Array.IndexOf(Pet.Sizes, pet.Saved.Size), i => pet.Resize(Pet.Sizes[i]));
            Switch(right, "Check in on me", () => !pet.Saved.NoCheckIns, pet.SetCheckIns);
            Switch(right, "Always on top", () => pet.OnTop, pet.SetOnTop);
            var zoomSize = Slide(right, "Mainframe size", FrameNames, () => Array.IndexOf(Pet.MainframeSizes, pet.MainframeSize), _ => { });
            zoomSize.Released += () => { if (!syncing) Guard(() => { pet.SetMainframeSize(Pet.MainframeSizes[zoomSize.Value]); Rezoom(); }); };
            Note(right, "Auto uses 100% on this desktop; OS display scaling is applied once.");
            Section(right, "WATCH");
            Switch(right, "Watch my screen", () => pet.Saved.Watch, pet.SetWatch);
            Switch(right, "Keep her caught up", () => !pet.Saved.NoCaughtUp, pet.SetCaughtUp);
            Choice(right, "Every", new[] { "10 s", "15 s", "30 s", "1 min", "1.5 min", "2 min", "3 min", "5 min", "10 min" }, () => Array.IndexOf(Pet.GlanceGaps, pet.GlanceGap), i => pet.SetGlanceGap(Pet.GlanceGaps[i]));
            Section(right, "BRAIN");
            Switch(right, "Stay on my model", () => pet.Saved.StayOnModel, pet.SetStay);
            Choice(right, "Replies", new[] { "Short", "Medium", "Long" }, () => pet.Saved.Replies, pet.SetReplies);
            Choice(right, "Memory", new[] { "Short · 8", "Medium · 16", "Long · 32" }, () => Array.IndexOf(new[] { 8, 16, 32 }, Brain.Recall), i => pet.SetRecall(new[] { 8, 16, 32 }[i]));
            Choice(right, "Bubble", Pet.BubbleNames, () => pet.BubbleTime, pet.SetBubbleTime);
            var custom = MainframeTheme.Input("Custom model name"); Wide(right, custom);
            Action pick = () => Run(() => pet.SelectBrainModelAsync(custom.Text?.Trim() ?? ""));
            custom.KeyDown += (_, e) => { if (e.Key == Key.Enter) { pick(); e.Handled = true; } };
            Wide(right, MainframeTheme.Button("Use model", pick));
            Note(right, "Uses the selected provider. Codex choices must be available to your account.");
            Section(right, "HABITS");
            Switch(right, "Start at login", () => pet.StartsAtLogin, pet.SetLogin);
            Switch(right, "Show on Discord", () => !pet.Saved.NoDiscord, pet.SetDiscord);
            Switch(right, "Remember our chats", () => !pet.Saved.NoMemories, pet.SetRemembering);
            Wide(right, MainframeTheme.Button("Open her memories", () => Report(pet.OpenMemories())));
            var security = MainframeTheme.Button("Security", OpenSecurity); Wide(right, security); readers.Add(() => security.Content = "Security: " + Security.PresetName);
            var securityNote = Note(right, ""); readers.Add(() => securityNote.Text = Security.Short);
            Note(right, "Perch: original 30 fps sequence. Interpolated perch rates and custom cursors are not available on this backend.");
        }

        private void Choice(StackPanel panel, string label, string[] labels, Func<int> get, Action<int> set)
        {
            var choice = new NeonPicker(label, labels, i => { if (!syncing) Guard(() => set(i)); }); Row(panel, label, choice); readers.Add(() => choice.Selected = get());
        }
        private void Switch(StackPanel panel, string label, Func<bool> get, Action<bool> set)
        {
            var toggle = new NeonSwitch(label) { HorizontalAlignment = HorizontalAlignment.Right };
            toggle.IsCheckedChanged += (_, _) => { if (!syncing) Guard(() => set(toggle.IsChecked == true)); };
            Row(panel, label, toggle); readers.Add(() => toggle.IsChecked = get());
        }
        private NeonSlider Slide(StackPanel panel, string label, string[] labels, Func<int> get, Action<int> set, Action release = null)
        {
            var slider = new NeonSlider(label, labels); slider.Changed += i => { if (!syncing) Guard(() => set(i)); };
            slider.Released += () => { if (!syncing && release != null) Guard(release); };
            Row(panel, label, slider); readers.Add(() => slider.Value = get());
            return slider;
        }
        private void Guard(Action action) { try { action(); } catch (Exception error) { Report(error.Message); } }
        private async void Run(Func<Task> action)
        {
            try { await action(); if (!closed) Refresh(); }
            catch (OperationCanceledException) { if (!closed) Report("Stopped."); }
            catch (Exception error) { if (!closed) Report(error.Message); }
        }
        private void Report(string message)
        {
            operation.Text = message ?? "";
            Window child = sheets.LastOrDefault(w => w.IsActive) ?? sheets.LastOrDefault();
            if (child != null && sheetMessages.TryGetValue(child, out TextBlock label)) { label.Text = message ?? ""; label.IsVisible = !string.IsNullOrEmpty(message); }
        }
        private void BrainChanged() { Dispatcher.UIThread.Post(() => { if (!closed) Refresh(); }); }
        internal void ShowSettings(bool on) { paged = on; primary.IsVisible = !on; settings.IsVisible = on; Rezoom(); }
        private void Rezoom() { ResizeFrame(pet.MainframeSize == 0 ? 1 : pet.MainframeSize / 100.0, paged ? 2 : 1, paged ? 900 : 800); }
        internal void Refresh()
        {
            if (closed || syncing) return;
            syncing = true;
            try
            {
                foreach (Action reader in readers) reader();
                models = pet.BrainModelChoices ?? Array.Empty<string>(); modelPicker.Choices(models); modelPicker.Selected = Array.IndexOf(models, pet.Brain.Model);
                micPicker.Selected = pet.Saved.Mic == null ? 0 : microphones.FindIndex(m => m.Id == pet.Saved.Mic) + 1;
                RefreshStatus();
            }
            finally { syncing = false; }
            double scale = pet.MainframeSize == 0 ? 1 : pet.MainframeSize / 100.0;
            if (CurrentZoom != scale) Rezoom();
        }
        private void RefreshStatus()
        {
            brainStatus.Text = pet.BrainConnectionStatus;
            voiceStatus.Text = pet.MainframeVoiceState;
            earsStatus.Text = pet.MainframeEarsState;
        }
        private async Task RefreshMicrophones()
        {
            var found = await Task.Run(Ears.List, lifetime.Token); if (closed) return;
            microphones = found; micPicker.Choices(new[] { "System default" }.Concat(found.Select(m => m.Name)).ToArray()); Refresh();
        }
        private async Task TestMicrophone()
        {
            if (micTest != null) { micTestJob?.Cancel(); micTest.Dispose(); micTest = null; Report("Microphone test stopped."); return; }
            using (var test = new Ears { Chosen = pet.Saved.Mic })
            using (var job = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
            {
                micTest = test; micTestJob = job;
                try
                {
                    double peak = 0; string failure = null;
                    test.Level += level => { peak = Math.Max(peak, level); Dispatcher.UIThread.Post(() => { if (!closed && micTest == test) Report("Mic input: " + level + " · listening for 4 seconds"); }); };
                    test.Failed += problem => failure = problem;
                    await test.Start(); await Task.Delay(4000, job.Token);
                    if (failure != null) throw new VoiceException(failure);
                    if (!closed && micTest == test) Report(peak > 0 ? "Microphone received audio. Press TALK for local speech recognition." : "Microphone opened, but no audible signal was detected.");
                }
                catch (OperationCanceledException) when (job.IsCancellationRequested) { }
                finally { if (micTest == test) micTest = null; if (micTestJob == job) micTestJob = null; }
            }
        }
        private MainframeWindow Sheet(string name, double height = 600)
        {
            Window existing = sheets.FirstOrDefault(w => w.Title == "Capri " + name);
            if (existing != null) { existing.Activate(); return null; }
            var window = new MainframeWindow(name) { WindowStartupLocation = WindowStartupLocation.CenterOwner };
            window.ResizeFrame(CurrentZoom, 1, height); sheets.Add(window);
            var message = Note(window.Body, ""); message.IsVisible = false; message.Foreground = MainframeTheme.Rose; sheetMessages.Add(window, message);
            window.PlacementFailed += text => { message.Text = text; message.IsVisible = true; };
            window.Closed += (_, _) => { sheets.Remove(window); sheetMessages.Remove(window); }; window.Show(this); return window;
        }
        private void OpenBrainSetup()
        {
            var sheet = Sheet("Brain setup", 340); if (sheet == null) return; Section(sheet.Body, "LOCAL · OLLAMA");
            Note(sheet.Body, "Install and open Ollama, then download her starter model. This keeps local inference available alongside Codex.");
            Wide(sheet.Body, MainframeTheme.Button("Open ollama.com", () => Pet.Browse("https://ollama.com")));
            Wide(sheet.Body, MainframeTheme.Button("Download " + Pet.Starter, pet.PullBrain));
            Wide(sheet.Body, MainframeTheme.Button("Refresh models", () => Run(pet.RefreshBrainModelsAsync)));
        }
        private void OpenApiSetup()
        {
            var sheet = Sheet("API brain", 320); if (sheet == null) return; Section(sheet.Body, "OPTIONAL API PROVIDER");
            Note(sheet.Body, "NVIDIA, OpenAI, Claude or DeepSeek API key. Codex / ChatGPT plan uses your existing Codex sign-in instead.");
            var key = MainframeTheme.Input("API key", true); Wide(sheet.Body, key);
            Wide(sheet.Body, MainframeTheme.Button("Connect API key", () => { if (!string.IsNullOrWhiteSpace(key.Text)) { pet.SaveBrainKey(key.Text.Trim()); key.Text = ""; } }));
            Wide(sheet.Body, MainframeTheme.Button("NVIDIA key page", pet.GetFreeKey));
        }
        private void OpenUpdates()
        {
            var sheet = Sheet("Updates", 280); if (sheet == null) return; Note(sheet.Body, pet.MainframeUpdateState);
            Wide(sheet.Body, MainframeTheme.Button("Check for updates", pet.MainframeCheckUpdate));
            Wide(sheet.Body, MainframeTheme.Button("Open upstream releases", pet.MainframeOpenUpdates));
            Note(sheet.Body, "Updates are reviewed and installed manually on Linux.");
        }
        internal void OpenSecurity()
        {
            var sheet = Sheet("Security", 780); if (sheet == null) return; var content = sheet.Body;
            Section(content, "PRESET");
            var preset = new NeonPicker("Security preset", Security.Presets, i => { if (i < 4) pet.ClickSecurity(i, -1); });
            Wide(content, preset); var note = Note(content, Security.Short);
            var toggles = new List<NeonSwitch>(); bool loading = false;
            Section(content, "WHAT SHE CAN DO");
            for (int i = 0; i < Security.Count; i++)
            {
                int index = i; var toggle = new NeonSwitch(Security.Names[i]) { IsEnabled = Security.Has(i), HorizontalAlignment = HorizontalAlignment.Right };
                var label = MainframeTheme.Label(Security.Names[i]); label.TextWrapping = TextWrapping.Wrap;
                var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,80"), Margin = new Thickness(28, 4, 24, 0) };
                row.Children.Add(label); row.Children.Add(toggle); Grid.SetColumn(toggle, 1); content.Children.Add(row);
                Note(content, Security.Has(i) ? Security.Lines[i] : "Not available on this platform.");
                toggle.IsCheckedChanged += (_, _) => { if (!loading) pet.ClickSecurity(-1, index); }; toggles.Add(toggle);
            }
            Action sync = () => { loading = true; preset.Selected = Security.Preset; note.Text = Security.Short; for (int i = 0; i < toggles.Count; i++) toggles[i].IsChecked = Security.On(i); loading = false; };
            pet.SettingsChanged += sync; sheet.Closed += (_, _) => pet.SettingsChanged -= sync; sync();
        }
        internal void OpenLayers()
        {
            var sheet = Sheet("Voice layers", 510); if (sheet == null) return; Section(sheet.Body, "TWO VOICES UNDER HERS");
            Note(sheet.Body, "Each layer follows her voice with its own pitch and delay. The main voice remains unchanged.");
            bool loading = false;
            var enabled = new NeonSwitch("Voice layers enabled"); Row(sheet.Body, "Layers", enabled);
            var pitches = new[] { new NeonSlider("Layer 1 pitch", Pet.LayerPitches.Select(VoiceFx.Layering.PitchName).ToArray()), new NeonSlider("Layer 2 pitch", Pet.LayerPitches.Select(VoiceFx.Layering.PitchName).ToArray()) };
            var delays = new[] { new NeonSlider("Layer 1 delay", Pet.LayerDelays.Select(VoiceFx.Layering.DelayName).ToArray()), new NeonSlider("Layer 2 delay", Pet.LayerDelays.Select(VoiceFx.Layering.DelayName).ToArray()) };
            Action apply = () => { if (!loading) pet.SetLayers(new VoiceFx.Layering(enabled.IsChecked == true, Pet.LayerPitches[pitches[0].Value], Pet.LayerDelays[delays[0].Value], Pet.LayerPitches[pitches[1].Value], Pet.LayerDelays[delays[1].Value])); };
            enabled.IsCheckedChanged += (_, _) => apply();
            for (int i = 0; i < 2; i++)
            {
                Section(sheet.Body, "LAYER " + (i + 1)); Row(sheet.Body, "Pitch", pitches[i]); Row(sheet.Body, "Delay", delays[i]);
                pitches[i].Changed += _ => apply(); delays[i].Changed += _ => apply(); pitches[i].Released += pet.HearStyle; delays[i].Released += pet.HearStyle;
            }
            Action sync = () =>
            {
                loading = true; var value = Pet.LayersOf(pet.Saved); enabled.IsChecked = value.On;
                pitches[0].Value = Nearest(Pet.LayerPitches, value.Pitch1); pitches[1].Value = Nearest(Pet.LayerPitches, value.Pitch2);
                delays[0].Value = Nearest(Pet.LayerDelays, value.Delay1); delays[1].Value = Nearest(Pet.LayerDelays, value.Delay2); loading = false;
            };
            pet.SettingsChanged += sync; sheet.Closed += (_, _) => pet.SettingsChanged -= sync; sync();
            Buttons(sheet.Body, MainframeTheme.Button("Test voice", pet.HearStyle), MainframeTheme.Button("Stop", pet.StopTalking));
        }
        internal void OpenVoiceSetup(bool cloud)
        {
            var sheet = Sheet(cloud ? "Cloud voice" : "Voice setup", cloud ? 370 : 760);
            if (sheet == null) return;
            if (cloud)
            {
                Section(sheet.Body, "ELEVENLABS · OPTIONAL"); Note(sheet.Body, "Uses the existing optional provider. Local voice and ears remain available without a key.");
                var key = MainframeTheme.Input("ElevenLabs key", true); var id = MainframeTheme.Input("Voice ID"); id.Text = pet.Saved.VoiceId ?? "";
                Wide(sheet.Body, key); Wide(sheet.Body, id);
                Wide(sheet.Body, MainframeTheme.Button("Save key / voice", () => Guard(() => { pet.MainframeSaveCloudVoice(key.Text, id.Text); key.Text = ""; })));
                Wide(sheet.Body, MainframeTheme.Button("Use saved cloud voice", () => Guard(() => pet.MainframeSelectVoiceMode(3)))); return;
            }
            Section(sheet.Body, "PREBUILT LOCAL"); Note(sheet.Body, pet.MainframeVoiceState + " · " + pet.MainframeEarsState);
            Wide(sheet.Body, MainframeTheme.Button("Set up her free voice and ears", () => pet.MainframeInstallSpeech(true)));
            Wide(sheet.Body, MainframeTheme.Button("Cancel download", pet.MainframeCancelSpeechInstall));
            Note(sheet.Body, pet.MainframeHdInstalled ? "HD local engine is installed." : "HD setup downloads have not been published for Linux. Existing local engines and voices are preserved.");
            if (pet.MainframeHdInstalled) Wide(sheet.Body, MainframeTheme.Button("Use installed HD voice", pet.MainframePickHd));
            Section(sheet.Body, "YOUR MODEL · ON THIS PC");
            var path = MainframeTheme.Input("Model folder, .onnx or model archive"); var speaker = MainframeTheme.Input("Speaker number or reference clip path"); speaker.Text = "0";
            Wide(sheet.Body, path); Wide(sheet.Body, speaker);
            Buttons(sheet.Body, MainframeTheme.Button("Browse model", () => Run(async () => { var files = await sheet.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose your local voice model", AllowMultiple = false }); if (files.Count > 0) path.Text = files[0].TryGetLocalPath(); })),
                MainframeTheme.Button("Use model", () => Run(() => pet.MainframeSaveVoiceModel(path.Text ?? "", speaker.Text ?? "0"))));
            Wide(sheet.Body, MainframeTheme.Button("Browse reference clip", () => Run(async () => { var files = await sheet.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Choose your voice reference clip", AllowMultiple = false }); if (files.Count > 0) speaker.Text = files[0].TryGetLocalPath(); })));
            Section(sheet.Body, "LOCAL VOICE SERVER");
            var address = MainframeTheme.Input("Local server URL"); address.Text = pet.Saved.VoiceApi == VoiceServer.Files ? VoiceServer.Address : pet.Saved.VoiceUrl ?? VoiceServer.Address;
            var model = MainframeTheme.Input("Voice model"); model.Text = pet.Saved.VoiceModel ?? "";
            var serverSpeaker = MainframeTheme.Input("Speaker or reference ID"); serverSpeaker.Text = pet.Saved.VoiceSpeaker ?? "";
            var serverKey = MainframeTheme.Input("Optional server key · unchanged when blank", true);
            int api = pet.Saved.VoiceApi == VoiceServer.Fish ? 1 : 0;
            var apiChoice = new NeonPicker("Local server API", new[] { "OpenAI-style", "Fish Speech · local" }, i => api = i) { Selected = api };
            Wide(sheet.Body, apiChoice); Wide(sheet.Body, address); Wide(sheet.Body, model); Wide(sheet.Body, serverSpeaker); Wide(sheet.Body, serverKey);
            Wide(sheet.Body, MainframeTheme.Button("Save local server", () => Guard(() => { pet.MainframeSaveVoiceServer(address.Text ?? "", api == 1 ? VoiceServer.Fish : VoiceServer.OpenAi, model.Text ?? "", serverSpeaker.Text ?? "", serverKey.Text); serverKey.Text = ""; })));
        }
        private static int Nearest(int[] values, int value) { return Enumerable.Range(0, values.Length).MinBy(i => Math.Abs(values[i] - value)); }
        private static int Nearest(double[] values, double value) { return Enumerable.Range(0, values.Length).MinBy(i => Math.Abs(values[i] - value)); }
    }
}
