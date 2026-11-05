using System;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal static class Program
    {
        internal static string Smoke;
        // Smoke checks turn this on to run Guard's catch path, which otherwise rethrows in smoke runs so a bug fails them.
        internal static bool Catching;
        internal static int[] Shelf;
        private static string lastCrash;
        private static int lastCrashAt;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--hook") return HuskHook.Run(args.Length > 1 ? args[1] : "");
            if (args.Length >= 5 && args[0] == "--shelf")
            {
                Shelf = new[] { int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]) };
                return AppBuilder.Configure<ShelfApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
            if (args.Length >= 2 && args[0] == "--smoke") Smoke = Path.GetFullPath(args[1]);
            // Whatever ends her leaves a line in errors.txt. A bad click or timer is caught once her app is up (CapriApp).
            if (Smoke == null) AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs crash) { Crashed(crash.ExceptionObject as Exception); };
            return AppBuilder.Configure<CapriApp>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = false }).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
        }

        internal static void Crashed(Exception error)
        {
            if (error == null) return;
            while (error.InnerException != null) error = error.InnerException;
            string what = error.GetType().Name + ": " + error.Message;
            // The same bug from the same place is written once a minute, even when its message carries changing numbers, so a
            // timer that keeps failing can't flood errors.txt.
            string place = "";
            foreach (string frame in (error.StackTrace ?? "").Split('\n')) { place = frame.Trim(); if (place.Length > 0) break; }
            string same = error.GetType().Name + " " + place;
            lock (typeof(Program))
            {
                if (same == lastCrash && unchecked(Environment.TickCount - lastCrashAt) < 60000) return;
                lastCrash = same;
                lastCrashAt = Environment.TickCount;
                try
                {
                    if (Brain.LogPath == null) Brain.LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri", "errors.txt");
                    Directory.CreateDirectory(Path.GetDirectoryName(Brain.LogPath));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            Brain.Log("crash", -1, what);
            int shown = 0;
            foreach (string frame in (error.StackTrace ?? "").Split('\n'))
            {
                string at = frame.Trim();
                if (at.Length == 0) continue;
                Brain.Log("crash", -1, at);
                if (++shown == 4) break;
            }
        }

        // Avalonia keeps a timer tick's exception to itself and never ticks that timer again, and an exception in a click
        // ends the app, so her timers and her own clicks come through here: it's written down and she carries on. Smoke
        // runs still fail on it.
        internal static void Guard(Action work)
        {
            try { work(); }
            catch (Exception error)
            {
                if (Smoke != null && !Catching) throw;
                Crashed(error);
            }
        }
    }

    internal sealed class CapriApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Dark;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // A bad click or timer used to end her on Mac and Linux. Now it's written down and she carries on, like on Windows.
            // This waits until now because Avalonia's UI dispatcher is made from the platform on first use, and in Main there's none yet.
            if (Program.Smoke == null) Dispatcher.UIThread.UnhandledException += delegate(object sender, DispatcherUnhandledExceptionEventArgs crash) { crash.Handled = true; Program.Crashed(crash.Exception); };
            IClassicDesktopStyleApplicationLifetime desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                Pet pet = new Pet(desktop);
                NativeMenu menu = pet.TrayMenu();
                TrayIcon tray = new TrayIcon();
                using (Stream chip = typeof(CapriApp).Assembly.GetManifestResourceStream("Capri.Chip.png")) tray.Icon = new WindowIcon(new Bitmap(chip));
                tray.ToolTipText = "Capri";
                tray.Menu = menu;
                tray.Clicked += delegate { pet.OpenMainframe(); };
                TrayIcons icons = new TrayIcons();
                icons.Add(tray);
                TrayIcon.SetIcons(this, icons);
                pet.Show();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }

    internal sealed class ShelfApp : Application
    {
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
            RequestedThemeVariant = ThemeVariant.Light;
        }

        public override void OnFrameworkInitializationCompleted()
        {
            IClassicDesktopStyleApplicationLifetime desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                Window shelf = new Window
                {
                    Title = "Capri test shelf",
                    Width = Program.Shelf[2],
                    Height = Program.Shelf[3],
                    CanResize = false,
                    Topmost = true,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Position = new PixelPoint(Program.Shelf[0], Program.Shelf[1]),
                    Background = new SolidColorBrush(Color.FromRgb(238, 240, 243)),
                    Content = new TextBlock { Text = "Capri test shelf", Margin = new Thickness(16), Foreground = Brushes.Black }
                };
                shelf.Show();
                Thread reader = new Thread(delegate()
                {
                    string line;
                    while ((line = Console.In.ReadLine()) != null)
                    {
                        string[] parts = line.Trim().Split(' ');
                        Dispatcher.UIThread.Post(delegate
                        {
                            if (parts[0] == "move" && parts.Length == 3) shelf.Position = new PixelPoint(int.Parse(parts[1]), int.Parse(parts[2]));
                            else if (parts[0] == "hide") shelf.Hide();
                            else if (parts[0] == "show") shelf.Show();
                            else if (parts[0] == "quit") desktop.Shutdown(0);
                        });
                    }
                    Dispatcher.UIThread.Post(delegate { desktop.Shutdown(0); });
                });
                reader.IsBackground = true;
                reader.Start();
            }
            base.OnFrameworkInitializationCompleted();
        }
    }
}
