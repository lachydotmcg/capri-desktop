using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class FakeCrash
    {
        internal const int Milliseconds = 5000;
        internal const string Punchline = "jk. you're fine, choom";
        internal const string Held = "I can't show that during full screen, a stream or a mouse job.";
        internal const string Game = "I can't show that while a game is open.";
        internal const string Failed = "Couldn't show my fake crash screen.";
        internal readonly bool Mac;
        internal Func<List<Display>> Screens;
        internal Func<string> Refuse;
        internal Func<Display, bool, Action, Action, Action, Plate> Make;
        internal Action<string> Say;
        internal Action<int, Action> After = (ms, done) => { Task.Delay(ms).ContinueWith(_ => Dispatcher.UIThread.Post(done, DispatcherPriority.Send)); };
        internal Func<double> Clock = () => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        private Run current;

        internal sealed class Display
        {
            internal PixelRect Bounds;
            internal double Scale;
            internal Display(PixelRect bounds, double scale) { Bounds = bounds; Scale = scale; }
        }

        internal interface Plate
        {
            void Show();
            void Close();
        }

        private sealed class Run
        {
            internal readonly List<Plate> Plates = new List<Plate>();
            internal double Until;
            internal bool Shown;
        }

        internal FakeCrash(bool mac) { Mac = mac; Make = (screen, apple, key, click, closed) => new CrashWindow(screen, apple, key, click, closed); }
        internal bool Up { get { return current != null; } }

        internal string Request(string action)
        {
            if (action == "stop") { End(current, true); return "Stopped my fake crash screen."; }
            if (action != "show") return "Couldn't find that fake crash action.";
            if (Up) return "My fake crash screen is already showing.";
            string refusal = Refuse();
            if (refusal != null) return refusal;
            Run run = new Run { Until = Clock() + Milliseconds / 1000.0 };
            current = run;
            try
            {
                After(Milliseconds, () => End(run, true));
                foreach (Display screen in Screens())
                {
                    if (current != run || Clock() >= run.Until) { End(run, true); return Failed; }
                    Plate plate = Make(screen, Mac, () => End(run, true), () => End(run, true), () => End(run, true));
                    run.Plates.Add(plate);
                    run.Shown = true;
                    plate.Show();
                }
                if (run.Plates.Count == 0 || current != run || Clock() >= run.Until) { End(run, false); return Failed; }
                return "Showing my fake crash screen.";
            }
            catch (Exception error)
            {
                End(run, false);
                Brain.Log("fake crash", -1, error.Message);
                return Failed;
            }
        }

        internal void Close() { End(current, false); }

        private void End(Run run, bool say)
        {
            if (run == null || current != run) return;
            current = null;
            foreach (Plate plate in run.Plates)
            {
                try { plate.Close(); }
                catch (Exception error) { Brain.Log("fake crash", -1, error.Message); }
            }
            if (say && run.Shown) Say(Punchline);
        }

        internal static string Refusal(bool quitting, bool full, bool stream, bool job, bool held, bool game)
        {
            if (quitting || full || stream || job || held) return Held;
            return game ? Game : null;
        }

        internal static void Draw(bool mac, Action<string, double, double, double> text, Action power)
        {
            if (mac)
            {
                power();
                text("You need to restart your computer. Hold down the Power\nbutton for several seconds or press the Restart button.", 258, 310, 16);
                text("Veuillez redémarrer votre ordinateur. Maintenez la touche\nde démarrage enfoncée pendant plusieurs secondes ou bien\nappuyez sur le bouton de réinitialisation.", 258, 375, 16);
                text("Sie müssen Ihren Computer neu starten. Halten Sie dazu\ndie Ein-/Aus-Taste einige Sekunden gedrückt oder drücken\nSie die Neustart-Taste.", 258, 460, 16);
                text("コンピュータを再起動する必要があります。パワーボタンを\n数秒間押し続けるか、リセットボタンを押してください。", 258, 545, 16);
            }
            else
            {
                text("[    0.000000] Booting Linux\n[    0.152030] Run init as init process\n[    0.184120] CPU: 0 PID: 1 Comm: init\n[    0.184121] Call Trace:\n[    0.184122]  panic+0x10/0x20\n[    0.184123]  do_exit+0x20/0x40\n[    0.184124] Kernel panic - not syncing: Attempted to kill init!\n[    0.184125] ---[ end Kernel panic ]---", 28, 40, 18);
            }
        }

        private sealed class CrashWindow : Window, Plate
        {
            internal CrashWindow(Display screen, bool mac, Action key, Action click, Action closed)
            {
                Title = "Capri fake crash";
                SystemDecorations = SystemDecorations.None;
                CanResize = false;
                ShowInTaskbar = false;
                ShowActivated = true;
                Topmost = true;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = screen.Bounds.Position;
                Width = screen.Bounds.Width / screen.Scale;
                Height = screen.Bounds.Height / screen.Scale;
                Background = mac ? new SolidColorBrush(Color.FromRgb(110, 110, 110)) : Brushes.Black;
                Cursor = new Cursor(StandardCursorType.None);
                Content = new CrashCanvas(mac);
                AddHandler(KeyDownEvent, (_, e) => { e.Handled = true; key(); }, RoutingStrategies.Tunnel, true);
                AddHandler(PointerPressedEvent, (_, e) => { e.Handled = true; click(); }, RoutingStrategies.Tunnel, true);
                Closed += (_, _) => { Cursor?.Dispose(); closed(); };
                Opened += (_, _) =>
                {
                    Position = screen.Bounds.Position;
                    if (OperatingSystem.IsMacOS()) MacCover(this);
                    else WindowState = WindowState.FullScreen;
                    Activate();
                    ((Control)Content).Focus();
                };
            }

            void Plate.Close() { Hide(); Close(); }
        }

        [DllImport("/usr/lib/libobjc.A.dylib")] private static extern IntPtr sel_registerName(string name);
        [DllImport("/usr/lib/libobjc.A.dylib", EntryPoint = "objc_msgSend")] private static extern void SendNumber(IntPtr target, IntPtr selector, long number);
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern int CGWindowLevelForKey(int key);

        private static void MacCover(Window window)
        {
            var handle = window.TryGetPlatformHandle();
            if (handle == null || handle.Handle == IntPtr.Zero || handle.HandleDescriptor != "NSWindow") throw new BrainException(Failed);
            Cover((selector, value) => SendNumber(handle.Handle, sel_registerName(selector), value), CGWindowLevelForKey);
        }

        private static void Cover(Action<string, long> send, Func<int, int> level)
        {
            send("setCollectionBehavior:", 1 | 256);
            send("setLevel:", level(13));
        }

        private sealed class CrashCanvas : Control
        {
            private readonly bool mac;
            internal CrashCanvas(bool mac) { this.mac = mac; Focusable = true; }

            public override void Render(DrawingContext context)
            {
                base.Render(context);
                context.DrawRectangle(mac ? new SolidColorBrush(Color.FromRgb(110, 110, 110)) : Brushes.Black, null, new Rect(Bounds.Size));
                double scale = Math.Min(Bounds.Width / 1024, Bounds.Height / 768);
                if (scale <= 0) return;
                using (context.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation((Bounds.Width - 1024 * scale) / 2, (Bounds.Height - 768 * scale) / 2)))
                {
                    IBrush panel = new SolidColorBrush(Color.FromRgb(55, 55, 55));
                    if (mac) context.DrawRectangle(panel, null, new Rect(220, 130, 584, 500), 12, 12);
                    Typeface face = new Typeface(mac ? FontFamily.Default : new FontFamily("monospace"));
                    Draw(mac, (line, x, y, size) => context.DrawText(Look.Text(line, face, size, Colors.White), new Point(x, y)), () =>
                    {
                        Pen pen = new Pen(new SolidColorBrush(Color.FromRgb(155, 155, 155)), 14);
                        context.DrawEllipse(null, pen, new Point(512, 220), 54, 54);
                        context.DrawRectangle(panel, null, new Rect(490, 152, 44, 60));
                        context.DrawLine(pen, new Point(512, 157), new Point(512, 226));
                    });
                }
            }
        }

        private sealed class FakePlate : Plate
        {
            internal Display Screen;
            internal bool Mac, Shown, Closed;
            internal Action Key, Click, Shut;
            internal bool Fail;
            public void Show() { Shown = true; if (Fail) throw new Exception("Fake window failure."); }
            public void Close() { Closed = true; Shut(); }
        }

        internal static void Checks(List<string> checks)
        {
            bool macWas = Hands.OnMac;
            Func<string, string> crashWas = Hands.Crash;
            try
            {
                foreach (bool mac in new[] { true, false })
                {
                    double now = 100;
                    var timers = new List<KeyValuePair<double, Action>>();
                    var plates = new List<FakePlate>();
                    var lines = new List<string>();
                    var screens = new List<Display> { new Display(new PixelRect(-1920, 0, 1920, 1080), 1), new Display(new PixelRect(0, -100, 2560, 1440), 2) };
                    string refusal = null;
                    bool fail = false;
                    var prank = new FakeCrash(mac) { Clock = () => now, Screens = () => screens, Refuse = () => refusal, Say = lines.Add };
                    prank.After = (ms, done) => timers.Add(new KeyValuePair<double, Action>(now + ms / 1000.0, done));
                    prank.Make = (screen, apple, key, click, closed) =>
                    {
                        var plate = new FakePlate { Screen = screen, Mac = apple, Key = key, Click = click, Shut = closed, Fail = fail && plates.Count % 2 == 1 };
                        plates.Add(plate);
                        return plate;
                    };
                    Hands.OnMac = mac;
                    Hands.Crash = prank.Request;
                    Hands.Kit kit = Hands.KitFor();
                    if (!kit.Has("fake_crash") || kit.Missing.ContainsKey("fake_crash") || kit.Tools.IndexOf("fake crash screen", StringComparison.Ordinal) < 0 || kit.Tools.IndexOf("fake blue screen", StringComparison.Ordinal) >= 0 || kit.NotHere.IndexOf("fake crash", StringComparison.Ordinal) >= 0) throw new Exception("Fake crash stayed missing or blue on Mac or Linux.");
                    if (Security.Asking("fake_crash", new ToolArguments { Action = "show" }) != "show the fake crash screen" || Security.Asking("fake_crash", new ToolArguments { Action = "stop" }) != "stop the fake crash screen") throw new Exception("Fake crash kept its Windows summary.");
                    foreach (int flag in new[] { 0, 1, 2, 3, 4, 5 })
                    {
                        refusal = Refusal(flag == 0, flag == 1, flag == 2, flag == 3, flag == 4, flag == 5);
                        if (Hands.Do("fake_crash", new ToolArguments { Action = "show" }) != (flag == 5 ? Game : Held) || prank.Up || plates.Count != 0 || timers.Count != 0) throw new Exception("Fake crash ignored a refusal.");
                    }
                    refusal = null;
                    foreach (string exit in new[] { "key", "click", "stop", "limit", "closed" })
                    {
                        int first = plates.Count, said = lines.Count;
                        if (Hands.Do("fake_crash", new ToolArguments { Action = "show" }) != "Showing my fake crash screen." || !prank.Up || plates.Count != first + screens.Count) throw new Exception("Fake crash missed a screen.");
                        for (int i = 0; i < screens.Count; i++) if (!plates[first + i].Shown || plates[first + i].Screen != screens[i] || plates[first + i].Mac != mac) throw new Exception("Fake crash lost a screen's bounds, scale or platform.");
                        if (prank.Request("show") != "My fake crash screen is already showing." || plates.Count != first + screens.Count) throw new Exception("Fake crash stacked windows.");
                        if (exit == "key") plates[first].Key();
                        if (exit == "click") plates[first + 1].Click();
                        if (exit == "stop" && Hands.Do("fake_crash", new ToolArguments { Action = "stop" }) != "Stopped my fake crash screen.") throw new Exception("Fake crash stop did not dispatch.");
                        if (exit == "closed") plates[first].Shut();
                        if (exit == "limit")
                        {
                            now += 4.999;
                            if (now >= timers[timers.Count - 1].Key || !prank.Up) throw new Exception("Fake crash ended before its limit.");
                            now += 0.001;
                            if (Math.Abs(now - timers[timers.Count - 1].Key) > 0.00001) throw new Exception("Fake crash did not schedule five seconds.");
                            foreach (var timer in timers) if (timer.Key <= now + 0.00001) timer.Value();
                        }
                        if (prank.Up || lines.Count != said + 1 || lines[said] != Punchline) throw new Exception("Fake crash did not end with her line.");
                        for (int i = first; i < plates.Count; i++) if (!plates[i].Closed) throw new Exception("Fake crash left a screen open.");
                    }
                    prank.Request("show");
                    foreach (var timer in timers.GetRange(0, timers.Count - 1)) timer.Value();
                    if (!prank.Up) throw new Exception("An old fake crash timer closed a new prank.");
                    prank.Close();
                    int quiet = lines.Count;
                    timers[timers.Count - 1].Value();
                    if (prank.Up || lines.Count != quiet) throw new Exception("Fake crash cleanup spoke twice.");
                    fail = true;
                    int failedAt = plates.Count;
                    if (prank.Request("show") != Failed || prank.Up) throw new Exception("A failed fake crash stayed open.");
                    for (int i = failedAt; i < plates.Count; i++) if (!plates[i].Closed) throw new Exception("Fake crash did not close after a show failure.");
                    fail = false;
                    screens.Clear();
                    if (prank.Request("show") != Failed || prank.Up) throw new Exception("Fake crash succeeded without screens.");
                    List<string> drawn = new List<string>();
                    int powers = 0;
                    Draw(mac, (text, x, y, size) => drawn.Add(text), () => powers++);
                    string words = string.Join("\n", drawn);
                    if (mac ? powers != 1 || drawn.Count != 4 || !words.Contains("You need to restart your computer. Hold down the Power\nbutton for several seconds or press the Restart button.") || !words.Contains("Veuillez redémarrer") || !words.Contains("Sie müssen") || !words.Contains("再起動") || words.Contains("Kernel panic") : powers != 0 || !words.Contains("Kernel panic - not syncing: Attempted to kill init!") || words.Contains("restart your computer")) throw new Exception("Fake crash drew the other platform's screen.");
                }
                var sent = new List<string>();
                int askedLevel = -1;
                Cover((selector, value) => sent.Add(selector + value), key => { askedLevel = key; return 1000; });
                if (askedLevel != 13 || sent.Count != 2 || sent[0] != "setCollectionBehavior:257" || sent[1] != "setLevel:1000") throw new Exception("Fake crash lost its Mac window level or Spaces flags.");
                checks.Add("PASS fake crash: Mac and Linux lists and dispatch, refusals, every screen, no stacking, key, click, stop, close, five-second limit, stale timers and failed-window cleanup");
                checks.Add("PASS fake crash drawing: Mac power symbol, four restart messages and window selectors; Linux console panic text");
            }
            finally { Hands.OnMac = macWas; Hands.Crash = crashWas; }
        }
    }

    internal sealed partial class Pet
    {
        private FakeCrash fakeCrash;

        private void AttachCrash()
        {
            fakeCrash = new FakeCrash(OperatingSystem.IsMacOS());
            fakeCrash.Screens = () =>
            {
                var found = new List<FakeCrash.Display>();
                foreach (var screen in Screens.All) found.Add(new FakeCrash.Display(screen.Bounds, screen.Scaling));
                return found;
            };
            fakeCrash.Refuse = () => FakeCrash.Refusal(quitting, FullScreen(), Hands.Streaming, ComputerJobs.Active || InControl.On, dragging || pressed, !OperatingSystem.IsMacOS() && Hands.GameOpen() != null);
            fakeCrash.Say = line => Blurt("(fake crash)", line);
            Hands.Crash = action => Dispatcher.UIThread.CheckAccess() ? fakeCrash.Request(action) : Dispatcher.UIThread.Invoke(() => fakeCrash.Request(action));
            Closed += (_, _) => fakeCrash.Close();
            Screens.Changed += (_, _) => fakeCrash.Close();
        }

        private void StopCrash() { if (fakeCrash != null && fakeCrash.Up) fakeCrash.Request("stop"); }
    }
}
