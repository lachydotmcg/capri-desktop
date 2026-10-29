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
        internal static int[] Shelf;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length >= 5 && args[0] == "--shelf")
            {
                Shelf = new[] { int.Parse(args[1]), int.Parse(args[2]), int.Parse(args[3]), int.Parse(args[4]) };
                return AppBuilder.Configure<ShelfApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
            }
            if (args.Length >= 2 && args[0] == "--smoke") Smoke = Path.GetFullPath(args[1]);
            return AppBuilder.Configure<CapriApp>().UsePlatformDetect().With(new MacOSPlatformOptions { ShowInDock = false }).StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown);
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
            IClassicDesktopStyleApplicationLifetime desktop = ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
            if (desktop != null)
            {
                Pet pet = new Pet(desktop);
                NativeMenu menu = new NativeMenu();
                NativeMenuItem talk = new NativeMenuItem("Talk to her");
                talk.Click += delegate { pet.ComeBack(); pet.Talk.Open(); };
                NativeMenuItem quiet = new NativeMenuItem("Quiet") { ToggleType = NativeMenuItemToggleType.CheckBox };
                quiet.Click += delegate { pet.ToggleShush(); };
                pet.ShushChanged = delegate { quiet.IsChecked = pet.Shushed; };
                NativeMenuItem hide = new NativeMenuItem("Hide her");
                hide.Click += delegate
                {
                    if (pet.Away) pet.ComeBack();
                    else pet.HideAway();
                };
                pet.AwayChanged = delegate { hide.Header = pet.Away ? "Show her" : "Hide her"; };
                NativeMenuItem bring = new NativeMenuItem("Bring her back");
                bring.Click += delegate { pet.Bring(); };
                NativeMenuItem quit = new NativeMenuItem("Quit Capri");
                quit.Click += delegate { pet.Quit(); };
                menu.Items.Add(talk);
                menu.Items.Add(quiet);
                menu.Items.Add(hide);
                menu.Items.Add(bring);
                menu.Items.Add(new NativeMenuItemSeparator());
                menu.Items.Add(quit);
                TrayIcon tray = new TrayIcon();
                using (Stream chip = typeof(CapriApp).Assembly.GetManifestResourceStream("Capri.Chip.png")) tray.Icon = new WindowIcon(new Bitmap(chip));
                tray.ToolTipText = "Capri";
                tray.Menu = menu;
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
