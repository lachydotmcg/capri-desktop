using System.Reflection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Avalonia.Input.Raw;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CapriDesktop;

// Disposable native fixture. It uses the real Pet factory and normal production
// focus handlers (never Program.Smoke). Only this test executable has a command
// file; no test control endpoint is shipped in Capri. Always use an empty profile.
internal static class NativeUiTests
{
    private static string directory;
    private static readonly ConcurrentQueue<NativeMotionTrace.Sample> trace = new();
    private static bool tracing;
    private static int submissions;
    private static int menuActions;
    private static string lastSubmission;
    private static IClassicDesktopStyleApplicationLifetime lifetime;
    private static Pet pet;
    private static int sequence = -1;
    private static bool wasPressed;
    private static readonly BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;

    [STAThread]
    private static int Main(string[] args)
    {
        NativeFontBootstrap.Initialize();
        if (args.Length != 1 || !Path.IsPathFullyQualified(args[0])) throw new ArgumentException("Pass an absolute task-private control directory.");
        directory = args[0];
        Directory.CreateDirectory(directory);
        string profile = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(profile) || !Path.IsPathFullyQualified(profile) || !Directory.Exists(profile)) throw new ArgumentException("Create a disposable XDG_CONFIG_HOME first.");
        if (Directory.Exists(Path.Combine(profile, "Capri", "accounts-native"))) throw new ArgumentException("The UI fixture must not use an account profile.");
        try
        {
            NativePassive.Initialize().GetAwaiter().GetResult();
            return AppBuilder.Configure<CapriApp>().UsePlatformDetect()
                .AfterSetup(_ => Dispatcher.UIThread.Post(Start))
                .StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }
        finally { NativePassive.Shutdown().GetAwaiter().GetResult(); }
    }

    private sealed class PublicInputTrace : IObserver<RawInputEventArgs>
    {
        public void OnNext(RawInputEventArgs e)
        {
            if (e is not RawPointerEventArgs) return;
            object Get(string name) => e.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(e);
            string kind = Get("Type")?.ToString();
            var root = Get("Root") as Window;
            var hit = Get("Position") is Point pos ? root?.InputHitTest(pos) : null;
            if (kind?.Contains("Button") == true) Console.WriteLine(JsonSerializer.Serialize(new { kind, point = Get("Position")?.ToString(), root = (Get("Root") as Window)?.Title, handled = Get("Handled"), hit = hit?.GetType().Name, rootId=root?.GetHashCode(), xid=root?.TryGetPlatformHandle()?.Handle.ToInt64() }));
        }
        public void OnError(Exception e) { Console.WriteLine(e); }
        public void OnCompleted() { }
    }

    private static void Start()
    {
        lifetime = (IClassicDesktopStyleApplicationLifetime)Application.Current.ApplicationLifetime;
        pet = lifetime.Windows.OfType<Pet>().SingleOrDefault();
        if (pet == null) { DispatcherTimer.RunOnce(Start, TimeSpan.FromMilliseconds(20)); return; }
        pet.ContextMenu.AddHandler(MenuItem.ClickEvent, (_, _) => menuActions++);
        MenuDiagnostics.Bind(directory, pet.ContextMenu);
        NativeMotionTrace.Observed += sample => { if (tracing && trace.Count < 100000) trace.Enqueue(sample); };
        object manager = typeof(Application).GetProperty("InputManager", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(Application.Current);
        ((IObservable<RawInputEventArgs>)manager.GetType().GetProperty("PreProcess").GetValue(manager)).Subscribe(new PublicInputTrace());
        DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        timer.Tick += (_, _) =>
        {
            bool nowPressed = (bool)typeof(Pet).GetField("pressed", fields).GetValue(pet);
            if (nowPressed != wasPressed)
            {
                var desk = pet.Desk as HyprlandDesk;
                Console.WriteLine(JsonSerializer.Serialize(new { transition = "pressed", value = nowPressed, at = Environment.TickCount64, pointer = desk?.Pointer()?.ToString(), visual = desk?.Desktop.VisualSnapshot != null, error = desk?.Desktop.LastError }));
                wasPressed = nowPressed;
            }
            string path = Path.Combine(directory, "request.json");
            if (!File.Exists(path)) return;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var request = doc.RootElement;
            int id = request.GetProperty("id").GetInt32();
            if (id == sequence) return;
            sequence = id;
            string error = null;
            try { Execute(request); } catch (Exception ex) { error = ex.ToString(); }
            var response = new { id, error, pid = Environment.ProcessId, backend = pet.Desk.GetType().Name,
                menuOpen = pet.ContextMenu.IsOpen, menuActions,
                menuSelected = (pet.ContextMenu.SelectedItem as MenuItem)?.Header?.ToString(),
                menuItems = pet.ContextMenu.Items.OfType<MenuItem>().Where(i => i.IsVisible).Select(i => new { header = i.Header?.ToString(), open = i.IsSubMenuOpen, bounds = i.Bounds.ToString(), screen = i.IsAttachedToVisualTree() ? i.PointToScreen(new Point(i.Bounds.Width / 2, i.Bounds.Height / 2)).ToString() : null }), pressed = (bool)typeof(Pet).GetField("pressed", fields).GetValue(pet), dragging = pet.Dragging,
                anchor = new { x = pet.Anchor.X, y = pet.Anchor.Y }, position = new { x = pet.Position.X, y = pet.Position.Y },
                spot = pet.BodySpot() is Point spot ? new { x = spot.X, y = spot.Y } : null,
                inputReady = pet.Talk.Typer.InputReady, composer = pet.Talk.Typer.IsVisible, secret = pet.Talk.Typer.Secret,
                draft = pet.Talk.Typer.Secret ? "[masked test secret]" : pet.Talk.Typer.Draft,
                submissions, lastSubmission, reply = pet.Talk.Bubble.Sheet.Reply, thinking = pet.Talk.Bubble.Sheet.Thinking,
                frameScroll = pet.Frame?.Scroll.Offset.ToString(),
                windows = lifetime.Windows.Select(w => new { xid = w.TryGetPlatformHandle()?.Handle.ToInt64(), title = w.Title, visible = w.IsVisible, active = w.IsActive, x = w.Position.X, y = w.Position.Y, width = w.Width, height = w.Height }),
                maps = (pet.Desk as HyprlandDesk)?.Maps.Select(m => new { name = m.Output.Name, bounds = m.AvaloniaBounds.ToString(), logical = m.Output.Logical.ToString() }) };
            string temporary = Path.Combine(directory, "response.tmp");
            File.WriteAllText(temporary, JsonSerializer.Serialize(response));
            File.Move(temporary, Path.Combine(directory, "response.json"), true);
        };
        timer.Start();
    }

    private static void Execute(JsonElement request)
    {
        string command = request.GetProperty("command").GetString();
        switch (command)
        {
            case "state": break;
            case "menu-open": MenuDiagnostics.Capture("open-request", pet.ContextMenu); pet.ContextMenu.Open(pet); break;
            case "menu-close": pet.ContextMenu.Close(); break;
            case "menu-diagnostic": MenuDiagnostics.Capture("requested-snapshot", pet.ContextMenu); break;
            case "menu-render": MenuDiagnostics.RenderControls(directory, pet.ContextMenu); break;
            case "menu-palette-diagnostic": MenuDiagnostics.ApplyPalette(pet.ContextMenu); break;
            case "menu-palette-checks": File.WriteAllText(Path.Combine(directory, "menu-palette-checks.json"), JsonSerializer.Serialize(MenuPaletteChecks.Run(pet.ContextMenu, request.TryGetProperty("requireSubmenu", out var requireSubmenu) && requireSubmenu.GetBoolean()))); break;
            case "route-trace":
                var original = pet.Brain.Act;
                pet.Brain.Act = (tool, args) => {
                    string result = original(tool, args);
                    File.AppendAllText(Path.Combine(directory, "model-actions.jsonl"), JsonSerializer.Serialize(new { tool, command=args?.Command, cwd=args?.Cwd, name=args?.Name, action=args?.Action, job_id=args?.JobId, asked=args?.Asked, result }) + "\n");
                    return result;
                }; break;
            case "layout-render": RenderLayout(); break;
            case "layout-checks": File.WriteAllText(Path.Combine(directory, "layout-checks.json"), JsonSerializer.Serialize(new { checks = ReplyLayoutChecks.Run(), status="PASS" })); break;
            case "advanced": pet.Saved.AdvancedSettings = request.GetProperty("on").GetBoolean(); pet.OpenMainframe(); pet.Frame.ShowSettings(true); TraceFrame(); break;
            case "scroll": pet.Frame.Scroll.Offset = new Vector(0, request.GetProperty("y").GetDouble()); break;
            case "control-on": InControl.Begin(); break;
            case "control-off": InControl.End(); break;
            case "control-capture-start": InControl.Begin(); Chores.SignHidden(true); break;
            case "control-capture-end": Chores.SignHidden(false); break;
            case "pet-remap": pet.Hide(); pet.Show(); break;
            case "pet-show-hide-show": pet.Hide(); pet.Show(); pet.Hide(); pet.Show(); break;
            case "pet-hide": pet.Hide(); break;
            case "pet-show": pet.Show(); break;
            case "topmost": pet.Topmost = request.GetProperty("on").GetBoolean(); break;
            case "trace-start": trace.Clear(); tracing = true; break;
            case "trace-stop": tracing = false; File.WriteAllText(Path.Combine(directory, "motion-trace.json"), JsonSerializer.Serialize(new { frequency = Stopwatch.Frequency, samples = trace.ToArray() })); break;
            case "composer": pet.Talk.Open(); break;
            case "hide": pet.Talk.Typer.Hide(); break;
            case "draft": pet.Talk.Typer.Draft = request.GetProperty("text").GetString(); break;
            case "secret": pet.Talk.AskKey("Public dummy key fixture"); break;
            case "mainframe": pet.OpenMainframe(); TraceFrame(); break;
            case "settings": pet.OpenMainframe(); pet.Frame.ShowSettings(request.GetProperty("on").GetBoolean()); TraceFrame(); break;
            case "frame-scale": pet.SetMainframeSize(request.GetProperty("size").GetInt32()); break;
            case "close-hit": CloseHit(); break;
            case "reply": pet.Talk.Answer("Public text fixture", request.GetProperty("text").GetString(), request.TryGetProperty("seconds",out var seconds) ? seconds.GetDouble() : 0); break;
            case "terminal-run":
                string script=request.GetProperty("script").GetString();
                _ = System.Threading.Tasks.Task.Run(() => File.WriteAllText(Path.Combine(directory,"terminal-start.json"),TerminalJobs.Run(new ToolArguments{Asked="run "+script,Command=script,Visible=false})));
                break;
            case "terminal-state":
                using(var started=JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"terminal-start.json"))))
                    File.WriteAllText(Path.Combine(directory,"terminal-state.json"),TerminalJobs.Control(new ToolArguments {Asked="read job",Action="read",JobId=started.RootElement.GetProperty("job_id").GetString()}));
                break;
            case "reply-state":
                var bubble = lifetime.Windows.OfType<Bubble>().Single();
                bubble.Sheet.Glitch = request.GetProperty("glitch").GetDouble();
                bubble.Sheet.Revealed = request.GetProperty("revealed").GetInt32();
                bubble.Sheet.InvalidateVisual(); break;
            case "duplicate-sheets": lifetime.Windows.OfType<Mainframe>().Single().OpenSecurity(); lifetime.Windows.OfType<Mainframe>().Single().OpenSecurity(); break;
            case "close-mainframe": lifetime.Windows.FirstOrDefault(w => w.Title == "Capri Mainframe")?.Close(); break;
            case "move": pet.StandUp(); pet.Move(new PixelPoint(request.GetProperty("x").GetInt32(), request.GetProperty("y").GetInt32())); typeof(Pet).GetMethod("SitHere", fields).Invoke(pet, null); break;
            case "echo-mode":
                // Substitute only the request sink, not the input/session code. No model traffic.
                typeof(Typer).GetField("Submitted", fields).SetValue(pet.Talk.Typer, (Action<string>)(text => { submissions++; lastSubmission = text; }));
                break;
            case "chain-mode":
                typeof(Typer).GetField("Submitted", fields).SetValue(pet.Talk.Typer, (Action<string>)(text => { submissions++; pet.Talk.AskKey("Next public prompt"); }));
                break;
            case "shutdown": lifetime.Shutdown(); break;
            default: throw new ArgumentException("Unknown native fixture command.");
        }
    }
    private static readonly HashSet<Mainframe> tracedFrames = new();
    private static void TraceFrame()
    {
        var frame = pet.Frame;
        if (frame == null || !tracedFrames.Add(frame)) return;
        var close = (NeonButton)typeof(Mainframe).GetField("close", fields).GetValue(frame);
        void Log(object data) => File.AppendAllText(Path.Combine(directory, "close-events.jsonl"), JsonSerializer.Serialize(new { pid=Environment.ProcessId, at=Environment.TickCount64, rootId=frame.GetHashCode(), xid=frame.TryGetPlatformHandle()?.Handle.ToInt64(), data }) + "\n");
        frame.AddHandler(InputElement.PointerPressedEvent, (_, e) => Log(new { stage = "press", source = e.Source?.GetType().Name, handled = e.Handled, capture = e.Pointer.Captured?.GetType().Name }), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        frame.AddHandler(InputElement.PointerReleasedEvent, (_, e) => Log(new { stage = "release", source = e.Source?.GetType().Name, handled = e.Handled, capture = e.Pointer.Captured?.GetType().Name }), RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
        close.Click += (_, _) => Log(new { stage = "Click" });
        frame.Closing += (_, e) => Log(new { stage = "Closing", reason = e.CloseReason.ToString(), cancel = e.Cancel });
        frame.Closed += (_, _) => Log(new { stage = "Closed" });
    }
    private static void CloseHit()
    {
        var frame = pet.Frame;
        var close = (NeonButton)typeof(Mainframe).GetField("close", fields).GetValue(frame);
        var points = new[] { new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), new Point(2,2), new Point(close.Bounds.Width - 2,2), new Point(2,close.Bounds.Height-2), new Point(close.Bounds.Width-2,close.Bounds.Height-2) };
        var result = points.Select(p => { var at = close.TranslatePoint(p, frame).Value; var hit = frame.InputHitTest(at); return new { local = p.ToString(), frame = new { x=at.X, y=at.Y }, hit = hit?.GetType().Name, inButton = hit is Visual v && (v == close || v.GetVisualAncestors().Contains(close)), button = close.Bounds.ToString() }; });
        File.WriteAllText(Path.Combine(directory, "close-hit.json"), JsonSerializer.Serialize(result));
    }

    private static void RenderLayout()
    {
        string sample = "The settings panel is getting a very thorough inspection.\n\n/home/test/Public QA\nhello\tworld\nCafé 👩🏽‍💻 — 日本語\nمرحبا بالعالم\nनमस्ते दुनिया";
        foreach (double scale in new[] {1.0, 1.5, 2.0})
        foreach (string mode in new[]{"Unspecified", "Antialias"})
        {
            var sheet = new Sheet { Reply=sample, Heard="Public text fixture", Revealed=sample.Length, CursorOn=false, Glitch=0 };
            double width=Sheet.PanelWidth()+30, height=Sheet.PanelHeight(sample)+40;
            sheet.Panel=new Rect(12,12,Sheet.PanelWidth(),Sheet.PanelHeight(sample));sheet.TipX=width-60;sheet.TipY=height-2;
            RenderOptions.SetTextRenderingMode(sheet, Enum.Parse<TextRenderingMode>(mode));
            sheet.Measure(new Size(width,height));sheet.Arrange(new Rect(0,0,width,height));
            using var bitmap=new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(width*scale),(int)Math.Ceiling(height*scale)),new Vector(96*scale,96*scale));bitmap.Render(sheet);
            bitmap.Save(Path.Combine(directory,"layout-"+mode+"-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"));
        }
    }

}
