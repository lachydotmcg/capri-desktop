using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.Sockets;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal partial class Companion
    {
        // A pretend window on her pretend desk: where it is, its normal place for when it's restored, and what it won't do.
        private sealed class PretendWindow
        {
            internal Rectangle Edges, Normal;
            internal bool Minimized, Maximized, Admin;
            internal Size Least = new Size(120, 80);
        }

        // Her window tool on Lachy's three monitors, one of them portrait and one at 150%, with pretend windows that each do what a
        // real one does: Discord won't go narrower than 940 pixels, Task Manager runs as admin, one app has stopped responding.
        // What she says is where each window really ended up (Lachy, 2 Nov: "move, resize, maximize, minimize, restore, snap
        // left/right, send to another monitor, and bring to front ... report truthfully what happened").
        private static void SmokeWindowTool(List<string> checks)
        {
            Func<List<Arrange.Display>> displaysWas = Arrange.Displays;
            Func<IntPtr, Arrange.Spot> readWas = Arrange.Read;
            Func<IntPtr, string, Rectangle, bool> shapeWas = Arrange.Shape;
            Func<IntPtr, bool> raisedWas = Arrange.Raised, hungWas = Arrange.Hung;
            Func<Point> mouseWas = Arrange.Mouse;
            int patienceWas = Arrange.Patience;
            Func<List<OpenWindow>> windowsWas = Hands.Windows;
            Func<IntPtr> frontWas = Hands.Foreground;
            Func<string> unlistedWas = Hands.Unlisted;
            Func<OpenWindow, bool> minimizedWas = Hands.Minimized, gameyWas = Hands.Gamey;
            Func<OpenWindow, string> screenWas = Hands.ScreenOf;
            Func<OpenWindow, string, bool> steerWas = Hands.Steer;
            bool windowsOn = Security.On(Security.Windows);
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                List<Arrange.Display> desk = new List<Arrange.Display>
                {
                    new Arrange.Display { Bounds = new Rectangle(0, 0, 1920, 1080), Work = new Rectangle(0, 0, 1920, 1032), Primary = true, Number = 1, Dpi = 96 },
                    new Arrange.Display { Bounds = new Rectangle(1920, -420, 1080, 1920), Work = new Rectangle(1920, -420, 1080, 1872), Number = 2, Dpi = 96 },
                    new Arrange.Display { Bounds = new Rectangle(3000, 0, 2560, 1440), Work = new Rectangle(3000, 0, 2560, 1392), Number = 3, Dpi = 144 }
                };
                Arrange.Named(desk);
                Dictionary<IntPtr, PretendWindow> pretend = new Dictionary<IntPtr, PretendWindow>();
                pretend[new IntPtr(31)] = new PretendWindow { Edges = new Rectangle(1990, -220, 940, 800), Least = new Size(940, 500) };
                pretend[new IntPtr(32)] = new PretendWindow { Edges = new Rectangle(0, 0, 1920, 1032), Normal = new Rectangle(320, 180, 1280, 720), Maximized = true };
                pretend[new IntPtr(33)] = new PretendWindow { Edges = new Rectangle(-32000, -32000, 160, 28), Normal = new Rectangle(100, 100, 800, 600), Minimized = true };
                pretend[new IntPtr(34)] = new PretendWindow { Edges = new Rectangle(400, 300, 900, 700), Admin = true };
                pretend[new IntPtr(35)] = new PretendWindow { Edges = new Rectangle(200, 200, 700, 500) };
                pretend[new IntPtr(36)] = new PretendWindow { Edges = new Rectangle(3200, 100, 1200, 800) };
                List<OpenWindow> open = new List<OpenWindow>
                {
                    new OpenWindow(new IntPtr(31), "#general | Capri Fans - Discord", "Discord"), new OpenWindow(new IntPtr(32), "YouTube - Google Chrome", "chrome"),
                    new OpenWindow(new IntPtr(33), "notes.txt - Notepad", "notepad"), new OpenWindow(new IntPtr(34), "Task Manager", "Taskmgr"),
                    new OpenWindow(new IntPtr(35), "Frozen App", "frozen"), new OpenWindow(new IntPtr(36), "Spotify Premium", "Spotify")
                };
                Arrange.Displays = delegate { return desk; };
                Arrange.Read = delegate(IntPtr handle)
                {
                    PretendWindow window;
                    if (!pretend.TryGetValue(handle, out window)) return new Arrange.Spot { Gone = true };
                    return new Arrange.Spot { Edges = window.Edges, Minimized = window.Minimized, Maximized = window.Maximized };
                };
                Arrange.Shape = delegate(IntPtr handle, string how, Rectangle wanted)
                {
                    PretendWindow window = pretend[handle];
                    if (window.Admin) return false;
                    if (how == "maximize")
                    {
                        if (!window.Maximized && !window.Minimized) window.Normal = window.Edges;
                        window.Maximized = true;
                        window.Minimized = false;
                        window.Edges = desk[Arrange.Under(window.Normal, desk)].Work;
                    }
                    else if (how == "restore")
                    {
                        if (window.Minimized) { window.Minimized = false; window.Edges = window.Maximized ? desk[Arrange.Under(window.Normal, desk)].Work : window.Normal; }
                        else if (window.Maximized) { window.Maximized = false; window.Edges = window.Normal; }
                    }
                    else
                    {
                        window.Edges = new Rectangle(wanted.Left, wanted.Top, Math.Max(wanted.Width, window.Least.Width), Math.Max(wanted.Height, window.Least.Height));
                        window.Normal = window.Edges;
                    }
                    return true;
                };
                Arrange.Raised = delegate(IntPtr handle) { return pretend[handle].Admin; };
                Arrange.Hung = delegate(IntPtr handle) { return handle == new IntPtr(35); };
                Arrange.Mouse = delegate { return new Point(100, 100); };
                Arrange.Patience = 100;
                Hands.Windows = delegate { return open; };
                Hands.Foreground = delegate { return new IntPtr(31); };
                Hands.Unlisted = delegate { return null; };
                Hands.Minimized = delegate(OpenWindow window) { return pretend[window.Handle].Minimized; };
                Hands.Gamey = delegate { return false; };
                Hands.ScreenOf = delegate(OpenWindow window) { PretendWindow seen = pretend[window.Handle]; return seen.Minimized ? null : desk[Arrange.Under(seen.Edges, desk)].Name; };
                Func<string, string, ToolArguments> asks = delegate(string name, string action) { return new ToolArguments { Name = name, Action = action }; };
                string[] said =
                {
                    Hands.Do("app_window", asks("chrome", "snap_left")),
                    Hands.Do("app_window", asks("discord", "snap_right")),
                    Hands.Do("app_window", asks("discord", "snap_top")),
                    Hands.Do("app_window", new ToolArguments { Name = "chrome", Action = "move_to_screen", Screen = "3" }),
                    Hands.Do("app_window", new ToolArguments { Name = "notepad", Action = "maximize", Screen = "3" }),
                    Hands.Do("app_window", asks("notepad", "restore")),
                    Hands.Do("app_window", asks("notepad", "restore")),
                    Hands.Do("app_window", asks("task manager", "maximize")),
                    Hands.Do("app_window", asks("frozen", "snap_left")),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "move", Position = "top_right" }),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "move", Left = "-200", Top = "10%" }),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "resize", Width = "50%", Height = "+10%" }),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "move_to_screen", Screen = "screen 7" }),
                    Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "move_to_screen", Screen = "portrait" }),
                    Hands.Do("app_window", asks("spotify", "move_to_screen")),
                    Hands.Do("app_window", asks("minecraft", "snap_left")),
                    Hands.Do("app_window", new ToolArguments { Name = "spotify", Action = "resize", Width = "huge" }),
                    Hands.Do("app_window", asks("chrome", "maximize")),
                    Hands.Do("app_window", asks("chrome", "maximize"))
                };
                string[] expected =
                {
                    "Snapped chrome to the left half of the main screen.",
                    "Snapped discord to the right half of screen 2, but it stayed 940 x 1872: that's as small as it goes.",
                    "Snapped discord to the top half of screen 2.",
                    "Moved chrome to screen 3.",
                    "Maximized notepad on screen 3.",
                    "Restored notepad from maximized to its normal size.",
                    "notepad isn't minimized or maximized, so it's already at its normal size.",
                    "Couldn't maximize task manager: it's running as admin, and Windows only lets me move admin windows when I run as admin too.",
                    "Couldn't snap frozen: it isn't responding right now.",
                    "Moved spotify to the top right corner of screen 3.",
                    "Moved spotify to 1160, 139 on screen 3.",
                    "Resized spotify to 1280 x 880.",
                    "There's no screen 7. Your monitors are the main screen, screen 2 and screen 3.",
                    "discord is already on screen 2.",
                    "Moved spotify to the main screen.",
                    "minecraft isn't open.",
                    "Couldn't resize spotify: huge isn't a size. Use pixels like 1280, a percent like 50%, or +20% to grow it.",
                    "Maximized chrome on screen 3.",
                    "chrome is already maximized on screen 3."
                };
                for (int i = 0; i < expected.Length; i++) if (said[i] != expected[i]) throw new Exception("Her window tool said \"" + said[i] + "\" instead of \"" + expected[i] + "\".");
                Rectangle discordNow = pretend[new IntPtr(31)].Edges, chromeNormal = pretend[new IntPtr(32)].Normal, spotifyNow = pretend[new IntPtr(36)].Edges;
                if (discordNow != new Rectangle(1920, -420, 1080, 936) || chromeNormal != new Rectangle(3000, 0, 1280, 1392) || !pretend[new IntPtr(32)].Maximized || spotifyNow != new Rectangle(934, 86, 853, 587) || pretend[new IntPtr(34)].Maximized)
                    throw new Exception("Her window tool's windows didn't end up where she said: discord " + discordNow + ", chrome " + chromeNormal + ", spotify " + spotifyNow);
                if (!Hands.Failed(said[7]) || !Hands.Failed(said[8]) || !Hands.Failed(said[12]) || !Hands.Failed(said[15]) || Hands.Failed(said[1]) || Hands.Failed(said[0]))
                    throw new Exception("Her window tool's failures didn't read as failures to her brain, or a move that worked did.");
                // A snapped half stays a half on the new monitor; anything else keeps its size, scaled for the monitor's scale.
                Rectangle carriedHalf = Arrange.Carried(new Rectangle(1920, -420, 540, 1872), desk[1], desk[2]), carriedBox = Arrange.Carried(new Rectangle(100, 100, 800, 600), desk[0], desk[1]);
                if (carriedHalf != new Rectangle(3000, 0, 1280, 1392) || carriedBox.Width != 800 || carriedBox.Height != 600 || !desk[1].Work.Contains(carriedBox)
                    || Arrange.Measure("1280px", 900, 1920, false) != 1280 || Arrange.Measure("-10%", 1000, 1920, false) != 900 || Arrange.Measure("25%", 40, 1920, true) != 480 || Arrange.Measure("+10%", 40, 1920, true) != 232 || Arrange.Measure("wide", 1, 2, false) != null || Arrange.Measure("", 7, 10, true) != 7
                    || Arrange.Pick("portrait", 0, desk) != 1 || Arrange.Pick("right", 0, desk) != 1 || Arrange.Pick("left", 2, desk) != 1 || Arrange.Pick("main", 2, desk) != 0 || Arrange.Pick("here", 2, desk) != 0 || Arrange.Pick("9", 0, desk) != -1)
                    throw new Exception("Her window tool's monitors or sizes were worked out wrong: " + carriedHalf + " / " + carriedBox);
                // What's open says where their monitors are, and where one app's window is, so a brain can move it by pixels.
                string listed = Hands.ListWindows(null), discordListed = Hands.ListWindows("discord");
                const string monitors = "\nMonitors, left to right: main screen 1920 x 1080; screen 2 1080 x 1920, portrait; screen 3 2560 x 1440.";
                if (!listed.EndsWith(monitors, StringComparison.Ordinal) || discordListed != "Windows of discord, nearest the front first: \"#general | Capri Fans - Discord\" (Discord), on screen 2, 1080 x 936 at 0, 0, in front." + monitors)
                    throw new Exception("list_windows didn't say where their monitors and windows are: " + listed + " / " + discordListed);
                // Her set phrases take the everyday asks straight away, for every brain, only for a window that's open now.
                string[,] heard =
                {
                    { "maximize chrome", "maximize chrome  " }, { "snap discord to the left", "snap_left discord  " }, { "put chrome on the right half of the screen", "snap_right chrome  " },
                    { "put spotify on the bottom half", "snap_bottom spotify  " }, { "send spotify to my other monitor", "move_to_screen spotify next " }, { "move discord to screen 2", "move_to_screen discord 2 " },
                    { "move my notepad window to the portrait monitor", "move_to_screen notepad portrait " }, { "put discord in the top right corner", "move discord  top_right" }, { "center notepad", "move notepad  center" },
                    { "restore discord", "restore discord  " }, { "unminimize notepad", "restore notepad  " }, { "put chrome on the left screen", "move_to_screen chrome left " }, { "snap chrome right", "snap_right chrome  " }
                };
                for (int i = 0; i < heard.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    bool direct = Hands.Direct(heard[i, 0], out tool, out args);
                    string got = direct && tool == "app_window" ? args.Action + " " + args.Name + " " + args.Screen + " " + args.Position : (direct ? tool : "nothing");
                    if (got != heard[i, 1]) throw new Exception("\"" + heard[i, 0] + "\" became " + got + " instead of " + heard[i, 1]);
                }
                foreach (string other in new[] { "maximize my potential", "move discord to the top", "maximize it", "restore my files" })
                {
                    string tool;
                    ToolArguments args;
                    if (Hands.Direct(other, out tool, out args) && tool == "app_window") throw new Exception("\"" + other + "\" moved a window: " + args.Action + " " + args.Name);
                }
                string ownTool;
                ToolArguments ownArgs;
                if (!Hands.Direct("move to my other monitor", out ownTool, out ownArgs) || ownTool != "move_yourself" || ownArgs.Action != "other_screen") throw new Exception("\"Move to my other monitor\" stopped being her own move.");
                // Her window tool asks first when Close and switch apps and tabs is off in SECURITY, and her calls are checked.
                Security.Set(Security.Windows, false);
                string held = Security.Gate("app_window", new ToolArguments { Name = "chrome", Action = "maximize" }), heldScreen = Security.Gate("app_window", new ToolArguments { Name = "discord", Action = "move_to_screen", Screen = "2" });
                Security.Drop();
                Security.Set(Security.Windows, true);
                if (held == null || held.IndexOf("\"Maximize chrome?", StringComparison.Ordinal) < 0 || heldScreen == null || heldScreen.IndexOf("Move discord to screen 2?", StringComparison.Ordinal) < 0
                    || Security.Asking("app_window", new ToolArguments { Name = "Discord", Action = "snap_left" }) != "snap Discord to the left half of its screen" || Security.Asking("app_window", new ToolArguments { Name = "Discord", Action = "move_to_screen", Screen = "next" }) != "move Discord to your other screen"
                    || Security.Gate("app_window", new ToolArguments { Name = "chrome", Action = "maximize" }) != null)
                    throw new Exception("Her window tool didn't ask first with Close and switch apps and tabs off: " + held + " / " + heldScreen);
                if (!Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "snap_bottom" }) || !Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "move", Position = "top right" })
                    || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "move" }) || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "resize" }) || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "move", Position = "upside" })
                    || Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "fling" }) || !Hands.Valid("app_window", new ToolArguments { Name = "chrome", Action = "resize", Width = "800" }))
                    throw new Exception("Her window tool's calls weren't checked.");
                // A Mac or Linux kit leaves arranging out of her tools and prompts, and says it's Windows only.
                List<string[]> macGaps = new List<string[]>();
                foreach (string arrange in Arrange.Actions) macGaps.Add(new[] { "app_window:" + arrange, "only on Windows for now" });
                Hands.Kit mac = Hands.KitWithout(macGaps), kit = Hands.KitFor();
                if (!mac.Tools.Contains("\"enum\":[\"switch_to\",\"minimize\",\"close\"]") || mac.Tools.Contains("\"maximize\"") || mac.Tools.Contains("\"position\"") || mac.Tools.Contains("Or arrange its window")
                    || mac.Unable("app_window", new ToolArguments { Name = "chrome", Action = "snap_left" }) != Hands.ArrangeLine || mac.Unable("app_window", new ToolArguments { Name = "chrome", Action = "close" }) != null
                    || !mac.Brief.Contains("\n- app_window: switch to, minimize or close their windows") || !Hands.NoteFor(mac).Contains("switch to, minimize or close apps that are open, play songs") || !Hands.PromptFor(mac).Contains("switch to, minimize or close an app, or look")
                    || !mac.NotHere.Contains("maximizing, snapping, moving and resizing their windows") || !mac.Abilities.Contains("switch to, minimize or close your apps and browser tabs")
                    || !kit.Tools.Contains("\"move_to_screen\"") || !kit.Brief.Contains("\n- app_window: switch to, minimize, maximize, snap, move, resize or close their windows") || !kit.WorkTools.Contains("their monitors named left to right") || !kit.WorkTools.Contains("\"position\"") || kit.Unable("app_window", new ToolArguments { Name = "chrome", Action = "snap_left" }) != null)
                    throw new Exception("Arranging windows reached a Mac or Linux kit, or left the Windows one: " + mac.NotHere);
                // A brain that works jobs out checks where the window is, moves it, and hears where it really ended up.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                pretend[new IntPtr(31)].Edges = new Rectangle(1990, -220, 940, 800);
                Brain.OpenAi = FakeServer(new[] { calls("list_windows", "{\"name\":\"discord\"}"), calls("app_window", "{\"name\":\"discord\",\"action\":\"snap_left\",\"screen\":\"portrait\"}"), says("Discord's on the left of your portrait screen, as narrow as it goes.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Name + ":" + args.Action); return Hands.Do(name, args); };
                string reply = planner.Ask("put discord on the left half of my vertical monitor");
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (reply != "Discord's on the left of your portrait screen, as narrow as it goes." || sent.Length != 3 || string.Join(",", did.ToArray()) != "list_windows:discord:,app_window:discord:snap_left"
                    || !sent[0].Contains("\"move_to_screen\"") || !sent[0].Contains("To arrange a window, use app_window") || !sent[1].Contains("Monitors, left to right: main screen 1920 x 1080; screen 2 1080 x 1920, portrait")
                    || !sent[1].Contains("940 x 800 at 70, 200") || !sent[2].Contains("Snapped discord to the left half of screen 2, but it stayed 940 x 1872: that's as small as it goes."))
                    throw new Exception("Her planner didn't check where the window was, or didn't hear where it really went: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + sent.Length + " calls");
                checks.Add("PASS her window tool on three monitors, one portrait and one at 150%: snaps left, right, top and bottom, maximizes on the monitor they name, restores from maximized or minimized, sends a window to the next, numbered or portrait monitor keeping a snapped half a half and scaling the rest, moves to a corner, to pixels or by a nudge, and resizes by pixels or percent, all read back afterwards; it says when an app won't go smaller and slides it back on screen, when a window runs as admin, isn't responding, isn't open, or is already there, and names the monitors when one doesn't exist; list_windows names their monitors and where one app's window is; everyday asks like \"snap discord to the left\" or \"send spotify to my other monitor\" work for every brain; it asks first with Close and switch apps and tabs off; Mac and Linux kits leave it out and say it's Windows only; a planner checks where the window is before moving it and hears where it ended up");
            }
            finally
            {
                Arrange.Displays = displaysWas;
                Arrange.Read = readWas;
                Arrange.Shape = shapeWas;
                Arrange.Raised = raisedWas;
                Arrange.Hung = hungWas;
                Arrange.Mouse = mouseWas;
                Arrange.Patience = patienceWas;
                Hands.Windows = windowsWas;
                Hands.Foreground = frontWas;
                Hands.Unlisted = unlistedWas;
                Hands.Minimized = minimizedWas;
                Hands.Gamey = gameyWas;
                Hands.ScreenOf = screenWas;
                Hands.Steer = steerWas;
                Security.Drop();
                Security.Set(Security.Windows, windowsOn);
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // Her window tool on a real window of her own, on whatever desk the smoke test runs on: snapped, maximized, restored, centred,
        // resized and sent to another monitor through Windows itself, and read back.
        private static void SmokeWindowReal(List<string> checks)
        {
            Form pane = null;
            ManualResetEvent shown = new ManualResetEvent(false);
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            Thread owner = new Thread(delegate()
            {
                pane = new Form { Text = "Capri window check", StartPosition = FormStartPosition.Manual, Bounds = new Rectangle(work.Left + 60, work.Top + 60, 640, 480), ShowInTaskbar = false };
                pane.Shown += delegate { shown.Set(); };
                Application.Run(pane);
            });
            owner.SetApartmentState(ApartmentState.STA);
            owner.IsBackground = true;
            owner.Start();
            try
            {
                if (!shown.WaitOne(10000)) throw new Exception("Her window check's own window never showed.");
                IntPtr handle = (IntPtr)pane.Invoke(new Func<IntPtr>(delegate { return pane.Handle; }));
                OpenWindow window = new OpenWindow(handle, "Capri window check", "Capri");
                List<Arrange.Display> screens = Arrange.Displays();
                int main = screens.FindIndex(screen => screen.Primary);
                Func<string, ToolArguments> asks = delegate(string action) { return new ToolArguments { Name = "the check window", Action = action }; };
                string snapped = Arrange.Do(window, "the check window", asks("snap_left"));
                Rectangle left = Arrange.Read(handle).Edges, half = Arrange.Half(screens[main].Work, "left");
                string maximized = Arrange.Do(window, "the check window", asks("maximize"));
                bool zoomed = (bool)pane.Invoke(new Func<bool>(delegate { return pane.WindowState == FormWindowState.Maximized; }));
                string restored = Arrange.Do(window, "the check window", asks("restore"));
                bool normal = (bool)pane.Invoke(new Func<bool>(delegate { return pane.WindowState == FormWindowState.Normal; }));
                string centred = Arrange.Do(window, "the check window", new ToolArguments { Name = "the check window", Action = "move", Position = "center" });
                string resized = Arrange.Do(window, "the check window", new ToolArguments { Name = "the check window", Action = "resize", Width = "500", Height = "400" });
                Rectangle sized = Arrange.Read(handle).Edges;
                if (!snapped.StartsWith("Snapped the check window to the left half of ", StringComparison.Ordinal) || Math.Abs(left.Left - half.Left) > 8 || Math.Abs(left.Width - half.Width) > 16 || Math.Abs(left.Height - half.Height) > 16
                    || maximized != "Maximized the check window" + (screens.Count > 1 ? " on the main screen." : ".") || !zoomed || restored != "Restored the check window from maximized to its normal size." || !normal
                    || !centred.StartsWith("Moved the check window to the middle of ", StringComparison.Ordinal) || resized != "Resized the check window to 500 x 400." || Math.Abs(sized.Width - 500) > 4 || Math.Abs(sized.Height - 400) > 4)
                    throw new Exception("Her window tool didn't move a real window: " + snapped + " " + left + " / " + maximized + " " + zoomed + " / " + restored + " / " + centred + " / " + resized + " " + sized);
                string sent = screens.Count > 1 ? Arrange.Do(window, "the check window", asks("move_to_screen")) : null;
                if (sent != null && (!sent.StartsWith("Moved the check window to ", StringComparison.Ordinal) || Arrange.Under(Arrange.Read(handle).Edges, screens) == main))
                    throw new Exception("Her window tool didn't send a real window to another monitor: " + sent);
                checks.Add("PASS her window tool moves a real window through Windows: snapped to the left half, maximized, restored, centred, resized to 500 x 400" + (sent != null ? " and sent to another monitor" : "") + ", each read back from the window itself");
                checks.Add("LIMIT her window tool on their own desk: snap, maximize and send Discord, Chrome and a game between three monitors, one portrait and one at another scale, and ask her to move Task Manager to hear that it runs as admin");
            }
            finally
            {
                if (pane != null && pane.IsHandleCreated) pane.BeginInvoke(new MethodInvoker(delegate { pane.Close(); }));
                owner.Join(5000);
            }
        }
    }
}
