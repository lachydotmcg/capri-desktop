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
            internal bool Minimized, Maximized, Admin, Pinned;
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
            Func<IntPtr, bool> pinnedWas = Arrange.Pinned;
            Func<IntPtr, bool, bool> pinWas = Arrange.Pin;
            int patienceWas = Arrange.Patience;
            Func<List<OpenWindow>> windowsWas = Hands.Windows;
            Func<IntPtr> frontWas = Hands.Foreground;
            Func<string> unlistedWas = Hands.Unlisted;
            Func<OpenWindow, bool> minimizedWas = Hands.Minimized, gameyWas = Hands.Gamey;
            Func<OpenWindow, string> screenWas = Hands.ScreenOf;
            Func<OpenWindow, string, bool> steerWas = Hands.Steer;
            Func<string, string, bool> runningWas = Hands.Running;
            Func<string, string> lookWas = Hands.Look;
            Func<string, string, string> lookOnWas = Hands.LookOn;
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
                // Nothing on this desk runs without a window.
                Hands.Running = delegate { return false; };
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
                // A monitor that isn't there, or a size that isn't one, is turned down before a minimized or maximized window comes
                // up or down.
                pretend[new IntPtr(33)].Edges = new Rectangle(-32000, -32000, 160, 28);
                pretend[new IntPtr(33)].Minimized = true;
                string nowhere = Hands.Do("app_window", new ToolArguments { Name = "notepad", Action = "move_to_screen", Screen = "screen 7" });
                string unsized = Hands.Do("app_window", new ToolArguments { Name = "chrome", Action = "resize", Width = "huge" });
                if (nowhere != "There's no screen 7. Your monitors are the main screen, screen 2 and screen 3." || !pretend[new IntPtr(33)].Minimized
                    || !unsized.StartsWith("Couldn't resize chrome: huge isn't a size.", StringComparison.Ordinal) || !pretend[new IntPtr(32)].Maximized)
                    throw new Exception("A move her window tool turned down still brought a window up or down: " + nowhere + " / " + unsized);
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
                // Her voice chat gets the same moves with every parameter described, and an agent tuned before them is tuned again.
                string voiced = null;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "app_window") voiced = Hands.ElevenLabsTool(i);
                if (voiced == null || !voiced.Contains("\"move_to_screen\"") || !voiced.Contains("\"position\"") || !voiced.Contains("Or arrange its window")
                    || System.Text.RegularExpressions.Regex.IsMatch(voiced, "\"\\w+\":\\{\"type\":\"(?:string|number|integer|boolean)\"(?![^{}]*\"description\":\"[^\"])") || !Agent.Stale(32, Brain.PersonalityStamp))
                    throw new Exception("Her voice chat's window tool is missing its moves, or her agent wouldn't be tuned again for them: " + voiced);
                // She looks at the monitor they name, from the one they're on, and says which one she saw.
                string[,] looks = { { "2", "screen 2" }, { "portrait", "screen 2" }, { "right", "screen 2" }, { "main", "the main screen" }, { "third", "screen 3" }, { "next", "screen 2" } };
                for (int i = 0; i < looks.GetLength(0); i++)
                {
                    Rectangle area;
                    string named, missing = Arrange.LookArea(looks[i, 0], IntPtr.Zero, out area, out named);
                    if (missing != null || named != looks[i, 1] || area != desk.Find(delegate(Arrange.Display one) { return one.Name == looks[i, 1].Replace("the ", ""); }).Bounds)
                        throw new Exception("Looking at the " + looks[i, 0] + " monitor found " + named + " " + area + " / " + missing);
                }
                Rectangle none;
                string noName, absentScreen = Arrange.LookArea("5", IntPtr.Zero, out none, out noName);
                if (absentScreen != "There's no screen 5. Your monitors are the main screen, screen 2 and screen 3." || noName != null || !none.IsEmpty)
                    throw new Exception("A monitor that isn't there wasn't said: " + absentScreen);
                string[,] lookAsks = { { "what's on my second monitor", "second" }, { "look at my left screen", "left" }, { "check the portrait one", "portrait" }, { "can you look at my other monitor", "next" }, { "look at my screen", "" } };
                for (int i = 0; i < lookAsks.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(lookAsks[i, 0], out tool, out args) || tool != "look_at_screen" || (args.Screen ?? "") != lookAsks[i, 1])
                        throw new Exception("\"" + lookAsks[i, 0] + "\" didn't look at the monitor they named: " + tool + " " + (args == null ? "" : args.Screen));
                }
                List<string> looked = new List<string>();
                Hands.Look = delegate(string focus) { looked.Add("here"); return "You looked at their screen."; };
                Hands.LookOn = delegate(string focus, string which) { looked.Add(which); return "You looked at screen 2."; };
                Hands.Do("look_at_screen", new ToolArguments { Screen = "left" });
                Hands.Do("look_at_screen", new ToolArguments { Focus = "the error" });
                if (string.Join(",", looked.ToArray()) != "left,here" || !kit.WorkTools.Contains("\"screen\":{\"type\":\"string\",\"description\":\"Another monitor to look at") || !kit.WorkTools.Contains("It looks at the monitor they're using, or the one they name."))
                    throw new Exception("Her look didn't go to the monitor they named: " + string.Join(",", looked.ToArray()));
                // She keeps a window on top of the others and lets it go, read back from the window, and an admin one says why not.
                Arrange.Pinned = delegate(IntPtr handle) { return pretend[handle].Pinned; };
                Arrange.Pin = delegate(IntPtr handle, bool pin) { if (pretend[handle].Admin) return false; pretend[handle].Pinned = pin; return true; };
                string pinned = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "pin_on_top" }), pinnedAgain = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "pin_on_top" });
                bool kept = pretend[new IntPtr(31)].Pinned;
                string unpinned = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "unpin" }), unpinnedAgain = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "unpin" });
                string adminPin = Hands.Do("app_window", new ToolArguments { Name = "task manager", Action = "pin_on_top" }), frozenPin = Hands.Do("app_window", new ToolArguments { Name = "frozen", Action = "pin_on_top" });
                if (pinned != "Kept discord on top of your other windows. Say unpin discord to let it go." || !kept || pinnedAgain != "discord is already kept on top of your other windows."
                    || unpinned != "discord isn't kept on top any more." || pretend[new IntPtr(31)].Pinned || unpinnedAgain != "discord isn't kept on top, so it's already in among your other windows."
                    || adminPin != "Couldn't pin task manager: it's running as admin, and Windows only lets me move admin windows when I run as admin too." || frozenPin != "Couldn't pin frozen: it isn't responding right now."
                    || !Hands.Failed(adminPin) || Hands.Failed(pinned) || Security.Asking("app_window", new ToolArguments { Name = "Discord", Action = "pin_on_top" }) != "keep Discord on top of your other windows")
                    throw new Exception("Her window tool didn't keep a window on top or let it go: " + pinned + " / " + pinnedAgain + " / " + unpinned + " / " + unpinnedAgain + " / " + adminPin + " / " + frozenPin);
                string[,] pinAsks = { { "keep discord on top", "pin_on_top" }, { "pin chrome", "pin_on_top" }, { "keep spotify on top of everything", "pin_on_top" }, { "unpin discord", "unpin" }, { "stop keeping discord on top", "unpin" } };
                for (int i = 0; i < pinAsks.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(pinAsks[i, 0], out tool, out args) || tool != "app_window" || args.Action != pinAsks[i, 1])
                        throw new Exception("\"" + pinAsks[i, 0] + "\" didn't " + pinAsks[i, 1] + ": " + tool + " " + (args == null ? "" : args.Action));
                }
                string unknownPin;
                ToolArguments unknownArgs;
                if (Hands.Heard("keep yourself on top", out unknownPin, out unknownArgs) && unknownPin == "app_window") throw new Exception("\"Keep yourself on top\" went to someone else's window.");
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
                checks.Add("PASS her window tool on three monitors, one portrait and one at 150%: snaps left, right, top and bottom, maximizes on the monitor they name, restores from maximized or minimized, sends a window to the next, numbered or portrait monitor keeping a snapped half a half and scaling the rest, moves to a corner, to pixels or by a nudge, and resizes by pixels or percent, all read back afterwards; it says when an app won't go smaller and slides it back on screen, when a window runs as admin, isn't responding, isn't open, or is already there, and names the monitors when one doesn't exist; list_windows names their monitors and where one app's window is; everyday asks like \"snap discord to the left\" or \"send spotify to my other monitor\" work for every brain; it asks first with Close and switch apps and tabs off; Mac and Linux kits leave it out and say it's Windows only; her voice chat gets the same moves, every parameter described, and her agent is tuned again for them; she keeps a window on top of the others and lets it go, like \"keep discord on top\"; she looks at the monitor they name, like \"what's on my second monitor\" or \"check the portrait one\", and says when there's no such monitor; a planner checks where the window is before moving it and hears where it ended up");
            }
            finally
            {
                Arrange.Displays = displaysWas;
                Arrange.Read = readWas;
                Arrange.Shape = shapeWas;
                Arrange.Raised = raisedWas;
                Arrange.Hung = hungWas;
                Arrange.Mouse = mouseWas;
                Arrange.Pinned = pinnedWas;
                Arrange.Pin = pinWas;
                Arrange.Patience = patienceWas;
                Hands.Windows = windowsWas;
                Hands.Foreground = frontWas;
                Hands.Unlisted = unlistedWas;
                Hands.Minimized = minimizedWas;
                Hands.Gamey = gameyWas;
                Hands.ScreenOf = screenWas;
                Hands.Look = lookWas;
                Hands.LookOn = lookOnWas;
                Hands.Steer = steerWas;
                Hands.Running = runningWas;
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

        // Everyday asks said truthfully, on a pretend player and desk: pause never starts the music and play never stops it, a skip
        // says the song it landed on, what's playing covers any player, an app in the tray isn't "not open", and a timer is a
        // reminder on every brain (Lachy, 3 Nov: "check state before acting, report truthfully").
        private void SmokeEverydayAsks(List<string> checks)
        {
            Func<List<Tune>> sessionsWas = Hands.Sessions;
            Func<Tune, string, bool> sendWas = Hands.SessionPress;
            Func<List<OpenWindow>> windowsWas = Hands.Windows;
            Func<string> unlistedWas = Hands.Unlisted, nowPlayingWas = Hands.NowPlaying;
            Func<string, string, bool> runningWas = Hands.Running;
            Action<byte> keysWas = Hands.Press;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                string app = "Spotify.exe", title = "Consume", artist = "Chase Atlantic";
                bool playing = true, stuck = false, present = true;
                List<string> sent = new List<string>();
                List<byte> keys = new List<byte>();
                // Each read is a fresh copy, the way Windows' sessions are read again after a press.
                Hands.Sessions = delegate
                {
                    List<Tune> tunes = new List<Tune>();
                    if (present) tunes.Add(new Tune { App = app, Title = title, Artist = artist, Playing = playing });
                    return tunes;
                };
                Hands.SessionPress = delegate(Tune tune, string what)
                {
                    sent.Add(what);
                    if (stuck) return true;
                    if (what == "pause" || what == "play") playing = what == "play";
                    else title = what == "next" ? "Swim" : "Consume";
                    return true;
                };
                Hands.Press = delegate(byte key) { keys.Add(key); };
                Func<string, string> media = delegate(string action) { return Hands.Do("media", new ToolArguments { Action = action }); };
                string[,] asks =
                {
                    { "now_playing", "Spotify is playing Consume by Chase Atlantic." }, { "pause", "Paused Spotify." },
                    { "pause", "Nothing's playing: Spotify is already paused on Consume by Chase Atlantic." }, { "now_playing", "Spotify is paused on Consume by Chase Atlantic." },
                    { "resume", "Spotify is playing Consume by Chase Atlantic again." }, { "resume", "Spotify is already playing Consume by Chase Atlantic." },
                    { "play_pause", "Paused Spotify." }, { "play_pause", "Spotify is playing Consume by Chase Atlantic again." },
                    { "next", "Skipped to the next track: Swim by Chase Atlantic." }, { "previous", "Went back a track: Consume by Chase Atlantic." }
                };
                List<string> wrong = new List<string>();
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string got = media(asks[i, 0]);
                    if (got != asks[i, 1]) wrong.Add(asks[i, 0] + " said \"" + got + "\"");
                }
                if (wrong.Count > 0 || string.Join(",", sent.ToArray()) != "pause,play,pause,play,next,previous" || keys.Count != 0)
                    throw new Exception("Her media tool didn't go by what's playing: " + string.Join(" / ", wrong.ToArray()) + " / pressed " + string.Join(",", sent.ToArray()));
                // A player that ignores the press is said plainly, a browser tab is named by its browser, and with no player to go by
                // she presses the key the way she always has.
                stuck = true;
                string ignored = media("pause");
                stuck = false;
                app = "Chrome";
                title = "lofi hip hop radio";
                artist = "Lofi Girl";
                string tab = media("now_playing");
                present = false;
                Hands.NowPlaying = delegate { return ""; };
                string blind = media("pause");
                if (ignored != "Couldn't pause Spotify: it still says it's playing." || !Hands.Failed(ignored) || tab != "Chrome is playing lofi hip hop radio by Lofi Girl." || blind != "Pressed pause." || keys.Count != 1 || keys[0] != 0xB3)
                    throw new Exception("Her media tool didn't say what the player did: " + ignored + " / " + tab + " / " + blind);
                // A brain's play_pause for "pause it" only pauses, and for "unpause it" only plays. An ask that goes both ways keeps
                // each call as the brain wrote it.
                string[,] aims = { { "pause the music", "pause" }, { "can you unpause it", "resume" }, { "un-pause it", "resume" }, { "resume my song", "resume" }, { "stop the music", "pause" }, { "play it again", "resume" }, { "play/pause", "play_pause" }, { "hit play pause", "play_pause" }, { "pause the music for ten seconds and then resume it", "play_pause" } };
                for (int i = 0; i < aims.GetLength(0); i++)
                {
                    ToolArguments aimed = new ToolArguments { Action = "play_pause" };
                    Hands.Aim(aimed, aims[i, 0]);
                    if (aimed.Action != aims[i, 1]) throw new Exception("\"" + aims[i, 0] + "\" aimed play_pause at " + aimed.Action);
                }
                // An app running in the tray with no window isn't "not open".
                Hands.Windows = delegate { return new List<OpenWindow> { new OpenWindow(new IntPtr(41), "notes.txt - Notepad", "notepad") }; };
                Hands.Unlisted = delegate { return null; };
                Hands.Running = delegate(string name, string id) { return name == "discord"; };
                string closed = Hands.Do("app_window", new ToolArguments { Name = "Discord", Action = "close" }), front = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "switch_to" });
                string snapped = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "snap_left" }), absent = Hands.Do("app_window", new ToolArguments { Name = "minecraft", Action = "close" });
                if (closed != "Couldn't close Discord: it has no window open, but it's running in the background, maybe in the tray. Quit it from its tray icon."
                    || front != "Couldn't switch to discord: it has no window open, but it's running in the background, maybe in the tray. Opening it should bring its window back."
                    || snapped != "Couldn't snap discord: it has no window open, but it's running in the background, maybe in the tray. Opening it should bring its window back."
                    || absent != "minecraft isn't open." || !Hands.Failed(closed) || !Hands.Failed(front) || !Hands.Failed(absent))
                    throw new Exception("An app in the tray was called not open: " + closed + " / " + front + " / " + snapped + " / " + absent);
                // Minimizing an app that's only in the tray is already done, not a failure.
                string tucked = Hands.Do("app_window", new ToolArguments { Name = "discord", Action = "minimize" });
                if (tucked != "discord has no window up: it's running in the background, maybe in the tray." || Hands.Failed(tucked))
                    throw new Exception("Minimizing an app in the tray failed: " + tucked);
                // A timer is a reminder that says it's done, set straight away on every brain, seconds too.
                string[,] timers =
                {
                    { "set a timer for 10 minutes", "your 10 minute timer is done", "in 10 minutes" }, { "timer 30 seconds", "your 30 second timer is done", "in 30 seconds" },
                    { "start a 5 minute timer", "your 5 minute timer is done", "in 5 minute" }, { "set me a timer for an hour", "your 1 hour timer is done", "in an hour" }
                };
                for (int i = 0; i < timers.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(timers[i, 0], out tool, out args) || tool != "remind" || args.Text != timers[i, 1] || args.When != timers[i, 2])
                        throw new Exception("\"" + timers[i, 0] + "\" didn't set a timer: " + tool + " " + (args == null ? "" : args.Text + " / " + args.When));
                }
                foreach (string loose in new[] { "set a timer", "set a timer for my pizza", "what's a timer" })
                {
                    string tool;
                    ToolArguments args;
                    if (Hands.Heard(loose, out tool, out args) && tool == "remind") throw new Exception("\"" + loose + "\" set a timer.");
                }
                // Cancelling one goes by the words its reminder says, "10 mins" or "10 minutes" alike.
                string[,] stops = { { "cancel my timer", "timer" }, { "stop the 10 minute timer", "10 minute timer" }, { "cancel the 10 min timer", "10 minute timer" }, { "stop the timer", "timer" } };
                for (int i = 0; i < stops.GetLength(0); i++)
                {
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard(stops[i, 0], out tool, out args) || tool != "remind" || args.Action != "cancel" || args.Text != stops[i, 1])
                        throw new Exception("\"" + stops[i, 0] + "\" didn't cancel a timer: " + tool + " " + (args == null ? "" : args.Action + " / " + args.Text));
                }
                string minced;
                ToolArguments mincedArgs;
                if (!Hands.Heard("set a timer for 10 mins", out minced, out mincedArgs) || mincedArgs.Text != "your 10 minute timer is done")
                    throw new Exception("A 10 min timer isn't called a 10 minute one: " + (mincedArgs == null ? "" : mincedArgs.Text));
                DateTime clock = new DateTime(2026, 11, 3, 1, 0, 0), due;
                if (!Reminders.Parse("in 30 seconds", clock, out due) || (due - clock).TotalSeconds != 30 || !Reminders.Parse("in 2 hours", clock, out due) || (due - clock).TotalHours != 2)
                    throw new Exception("A timer in seconds wasn't understood.");
                // A brain that works jobs out pauses only when something plays, and hears what the player said.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                app = "Spotify.exe";
                title = "Consume";
                artist = "Chase Atlantic";
                playing = false;
                present = true;
                sent.Clear();
                Brain.OpenAi = FakeServer(new[] { calls("media", "{\"action\":\"play_pause\"}"), says("It's already paused, choom.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Action); return Hands.Do(name, args); };
                string reply = planner.Ask("ugh pause whatever's playing, I'm about to jump on a call");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "It's already paused, choom." || string.Join(",", did.ToArray()) != "media:pause" || sent.Count != 0 || heard.Length != 2
                    || !heard[0].Contains("A timer is a reminder") || !heard[0].Contains("pausing never starts the music") || !heard[1].Contains("Nothing's playing: Spotify is already paused on Consume by Chase Atlantic."))
                    throw new Exception("Her planner's pause flipped the music or didn't hear the player: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                checks.Add("PASS everyday asks go by what's there: pause only pauses and play only plays by what Windows says is playing, a player that ignores the press is said plainly, next and previous name the song they landed on, what's playing covers Spotify, a browser tab or anything else, a brain's play_pause for pause or unpause can't flip the music the wrong way, with no player to read the keys work as before; an app in the tray says so instead of not open, and minimizing one is already done; set a timer for 10 minutes, 30 seconds or a 5 minute timer is a reminder on every brain, and cancel my timer or stop the 10 min timer cancels it");
                checks.Add("LIMIT pause, play and skip on their own players: Spotify, a YouTube tab and a game's music each report through Windows' media session in their own way");
            }
            finally
            {
                Hands.Sessions = sessionsWas;
                Hands.SessionPress = sendWas;
                Hands.Windows = windowsWas;
                Hands.Unlisted = unlistedWas;
                Hands.NowPlaying = nowPlayingWas;
                Hands.Running = runningWas;
                Hands.Press = keysWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // One app's own volume in a pretend volume mixer: Spotify, two Discord processes, a game, a muted Chrome and an app that
        // ignores changes. Each change is read back from the mixer, and nothing touches their real apps' sound (Lachy, 3 Nov: "turn
        // the game down" works for a brain that works jobs out).
        private static void SmokeAppVolume(List<string> checks)
        {
            Func<List<AppSound>> soundsWas = Hands.Sounds;
            Func<int, float?, bool?, bool> setWas = Hands.SetSound;
            Func<string, string, bool> runningWas = Hands.Running;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                List<AppSound> mixer = new List<AppSound>
                {
                    new AppSound { App = "Spotify.exe", Id = 10, Level = 0.5f, Playing = true }, new AppSound { App = "Discord.exe", Id = 20, Level = 1f },
                    new AppSound { App = "Discord.exe", Id = 21, Level = 1f, Playing = true }, new AppSound { App = "FortniteClient-Win64-Shipping.exe", Id = 30, Level = 0.8f, Playing = true },
                    new AppSound { App = "chrome.exe", Id = 40, Level = 0.6f, Muted = true }, new AppSound { App = "Stuck.exe", Id = 50, Level = 0.3f }
                };
                List<string> changed = new List<string>();
                bool readable = true;
                // Fresh copies each read, the way each read of the real mixer is.
                Hands.Sounds = delegate
                {
                    if (!readable) return null;
                    return mixer.ConvertAll(delegate(AppSound sound) { return new AppSound { App = sound.App, Id = sound.Id, Level = sound.Level, Muted = sound.Muted, Playing = sound.Playing }; });
                };
                Hands.SetSound = delegate(int id, float? level, bool? muted)
                {
                    changed.Add(id + (level.HasValue ? " " + Math.Round(level.Value * 100) : "") + (muted.HasValue ? (muted.Value ? " muted" : " unmuted") : ""));
                    if (id == 50) return true;
                    foreach (AppSound sound in mixer)
                    {
                        if (sound.Id != id) continue;
                        if (level.HasValue) sound.Level = level.Value;
                        if (muted.HasValue) sound.Muted = muted.Value;
                    }
                    return true;
                };
                Hands.Running = delegate(string name, string id) { return name == "steam"; };
                Func<string, string, string, string> mix = delegate(string action, string name, string level) { return Hands.Do("app_volume", new ToolArguments { Action = action, Name = name, Level = level }); };
                string[,] asks =
                {
                    { "now", null, null, "In the volume mixer: Spotify 50%, playing now; Discord 100%, playing now; FortniteClient-Win64-Shipping 80%, playing now; Chrome 60%, muted; Stuck 30%." },
                    { "now", "spotify", null, "Spotify is at 50% in the volume mixer." }, { "now", "chrome", null, "Chrome is at 60% in the volume mixer, and it's muted." },
                    { "down", "fortnite", null, "Turned FortniteClient-Win64-Shipping down to 60% in the volume mixer." }, { "down", "FortniteClient-Win64-Shipping.exe", "10", "Turned FortniteClient-Win64-Shipping down to 50% in the volume mixer." },
                    { "set", "Discord", "40", "Set Discord to 40% in the volume mixer." }, { "up", "spotify", "30", "Turned Spotify up to 80% in the volume mixer." },
                    { "up", "chrome", null, "Turned Chrome up to 80% in the volume mixer." }, { "mute", "spotify", null, "Muted Spotify in the volume mixer." },
                    { "mute", "Spotify", null, "Spotify is already muted in the volume mixer." }, { "down", "spotify", null, "Turned Spotify down to 60% in the volume mixer, but it's muted." },
                    { "unmute", "spotify", null, "Unmuted Spotify: it's at 60% in the volume mixer." }, { "unmute", "spotify", null, "Spotify isn't muted: it's at 60% in the volume mixer." },
                    { "set", "spotify", "0", "Set Spotify to 0% in the volume mixer." }, { "down", "spotify", null, "Spotify is already at 0% in the volume mixer." },
                    { "set", "discord", "100", "Set Discord to 100% in the volume mixer." }, { "up", "discord", null, "Discord is already at 100% in the volume mixer. Its own volume slider inside the app, or the PC's volume with media, can still go up." },
                    { "set", "stuck", "80", "Couldn't set Stuck's volume: it's still at 30%." }, { "set", "spotify", "150", "Couldn't set Spotify's volume: it goes from 0 to 100." },
                    { "now", "steam", null, "steam hasn't made any sound since it opened, so it has no volume of its own in the mixer yet." },
                    { "set", "steam", "50", "Couldn't change steam's volume: it hasn't made any sound since it opened, so it has no volume of its own in the mixer yet." },
                    { "now", "minecraft", null, "minecraft isn't open, or it has another name in the mixer: list_windows names its program." }, { "up", null, null, "Which app, choom?" }
                };
                List<string> wrong = new List<string>();
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string got = mix(asks[i, 0], asks[i, 1], asks[i, 2]);
                    if (got != asks[i, 3]) wrong.Add(asks[i, 0] + " " + asks[i, 1] + " " + asks[i, 2] + " said \"" + got + "\"");
                }
                if (wrong.Count > 0) throw new Exception("Her app volume didn't say where each app ended up: " + string.Join(" / ", wrong.ToArray()));
                string moves = string.Join(",", changed.ToArray());
                if (!moves.StartsWith("30 60,30 50,20 40,21 40,10 80,40 80 unmuted,10 muted,10 60,10 unmuted,10 0,20 100,21 100,50 80", StringComparison.Ordinal))
                    throw new Exception("Her app volume moved the wrong sounds: " + moves);
                if (!Hands.Failed(mix("set", "stuck", "80")) || !Hands.Failed(mix("set", "steam", "50")) || Hands.Failed(mix("now", "steam", null)) || Hands.Failed(mix("now", "spotify", null)))
                    throw new Exception("A change that didn't happen wasn't read as one.");
                // Discord playing on two outputs with only one of them muted still unmutes.
                foreach (AppSound sound in mixer) if (sound.Id == 20) sound.Muted = true;
                string halfMuted = mix("unmute", "discord", null);
                if (halfMuted != "Unmuted Discord: it's at 100% in the volume mixer." || mixer.Exists(delegate(AppSound sound) { return sound.App == "Discord.exe" && sound.Muted; }))
                    throw new Exception("An app muted on one of its two outputs stayed muted: " + halfMuted);
                readable = false;
                string unread = mix("now", null, null);
                readable = true;
                if (unread != "Couldn't read the volume mixer just now.") throw new Exception("An unreadable mixer wasn't said: " + unread);
                // Reading it never asks first and stays out of her transcript; changing it is Music, media and volume, like the PC's volume.
                ToolArguments quieter = new ToolArguments { Action = "down", Name = "fortnite" };
                if (!Hands.Valid("app_volume", new ToolArguments { Action = "now" }) || !Hands.Valid("app_volume", quieter) || !Hands.Valid("app_volume", new ToolArguments { Action = "up", Name = "spotify", Level = "30" })
                    || Hands.Valid("app_volume", new ToolArguments { Action = "set", Name = "spotify" }) || Hands.Valid("app_volume", new ToolArguments { Action = "set", Name = "spotify", Level = "loud" })
                    || Hands.Valid("app_volume", new ToolArguments { Action = "mute" }) || Hands.Valid("app_volume", new ToolArguments { Action = "blast", Name = "spotify" })
                    || !Hands.Checking("app_volume", new ToolArguments { Action = "now" }) || Hands.Checking("app_volume", quieter)
                    || Security.Needs("app_volume", new ToolArguments { Action = "now" }) != -1 || Security.Needs("app_volume", quieter) != Security.Media
                    || Security.Asking("app_volume", quieter) != "turn fortnite down" || Security.Asking("app_volume", new ToolArguments { Action = "set", Name = "Spotify", Level = "30" }) != "set Spotify to 30%")
                    throw new Exception("Her app volume's calls weren't checked.");
                // Every brain gets it: her one-shot hands and voice chat as well as a brain that works jobs out.
                Hands.Kit kit = Hands.KitFor();
                bool voiced = false;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "app_volume") voiced = true;
                if (!kit.WorkTools.Contains("\"name\":\"app_volume\"") || !kit.Tools.Contains("app_volume") || !voiced || !Hands.WorkingOut.Contains("is app_volume; the whole PC's is media"))
                    throw new Exception("Her app volume missed a brain or voice chat.");
                // A brain that works jobs out reads the mixer, turns the game down, and hears where it ended up.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                foreach (AppSound sound in mixer) if (sound.Id == 30) sound.Level = 0.8f;
                changed.Clear();
                Brain.OpenAi = FakeServer(new[] { calls("app_volume", "{\"action\":\"now\"}"), calls("app_volume", "{\"action\":\"down\",\"name\":\"fortnite\",\"level\":\"40\"}"), says("Turned Fortnite down to 40%, choom. Discord's at full.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Action + ":" + args.Name); return Hands.Do(name, args); };
                string reply = planner.Ask("the game's way too loud, I can't hear my friends on discord");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "Turned Fortnite down to 40%, choom. Discord's at full." || string.Join(",", did.ToArray()) != "app_volume:now:,app_volume:down:fortnite" || heard.Length != 3
                    || !heard[0].Contains("\"name\":\"app_volume\"") || !heard[1].Contains("FortniteClient-Win64-Shipping 80%, playing now") || !heard[2].Contains("Turned FortniteClient-Win64-Shipping down to 40% in the volume mixer.") || string.Join(",", changed.ToArray()) != "30 40")
                    throw new Exception("Her planner didn't read the mixer or hear where the game ended up: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                // Windows' own mixer answers on this PC; only reading it here, so nothing of theirs gets louder or quieter.
                List<AppSound> real = Mixer.Sounds();
                if (real == null) throw new Exception("Windows' volume mixer couldn't be read on this PC.");
                checks.Add("PASS her app volume reads and changes one app's own volume in the mixer apart from the PC's: lists every app with a sound and which are playing, sets, steps up and down, mutes and unmutes Spotify, both Discord processes, a game by its program and a muted Chrome, reads each back, says when one is already as far as it goes, when an app ignores the change, hasn't made a sound yet or isn't open, and when the mixer can't be read; reading never asks first, changing it is Music, media and volume; only a planner gets it, and it turns the game down after reading the mixer; Windows' mixer read here: " + real.Count + " sounds");
                checks.Add("LIMIT app volume on their own apps: a game, Discord and Spotify each show in Windows' mixer once they've made sound; ask \"turn the game down a bit\" with a game and Discord both playing");
            }
            finally
            {
                Hands.Sounds = soundsWas;
                Hands.SetSound = setWas;
                Hands.Running = runningWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // How their PC is doing on a pretend PC that's busy with a game, Chrome and OBS: the processor, memory, the apps using the
        // most, one app by name, drives and a laptop's battery, read only. Then a real read of this PC, which changes nothing
        // (Lachy, 3 Nov: "my game's lagging" gets an answer from what's really using it).
        private static void SmokePcStatus(List<string> checks)
        {
            Func<PcUse> useWas = Hands.Use;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                const long Gigabyte = 1073741824;
                PcUse busy = new PcUse { Cpu = 87.4, MemoryUsed = (long)(24.6 * Gigabyte), MemoryTotal = (long)(31.9 * Gigabyte) };
                string[] names = { "FortniteClient-Win64-Shipping", "chrome", "Discord", "obs64", "MsMpEng", "Spotify", "Teams", "explorer" };
                double[] cpu = { 41.2, 18.6, 3.2, 9.5, 7.9, 0.4, 0, 1.0 }, memory = { 6.3, 3.1, 0.9, 0.7, 0.3, 0.4, 1.2, 0.2 };
                for (int i = 0; i < names.Length; i++) busy.Apps.Add(new AppUse { App = names[i], Cpu = cpu[i], Memory = (long)(memory[i] * Gigabyte) });
                busy.Drives.Add("C: 45 GB free of 931 GB");
                busy.Drives.Add("D: 1.2 TB free of 1.8 TB");
                bool readable = true;
                Hands.Use = delegate { return readable ? busy : null; };
                const string Totals = "Their PC right now: the processor is 87% busy, and 25 GB of its 32 GB of memory is in use (77%).";
                string all = Hands.Do("pc_status", new ToolArguments()), chrome = Hands.Do("pc_status", new ToolArguments { Name = "chrome" }), game = Hands.Do("pc_status", new ToolArguments { Name = "Fortnite" });
                string absent = Hands.Do("pc_status", new ToolArguments { Name = "valorant" });
                busy.Battery = "54%, on battery";
                string laptop = Hands.Do("pc_status", new ToolArguments());
                readable = false;
                string unread = Hands.Do("pc_status", new ToolArguments());
                readable = true;
                string wanted = Totals + " Using the most: FortniteClient-Win64-Shipping 41% of the processor and 6.3 GB; chrome 19% of the processor and 3.1 GB; obs64 10% of the processor and 0.7 GB; MsMpEng 8% of the processor and 0.3 GB; "
                    + "Discord 3% of the processor and 0.9 GB; Teams 1.2 GB of memory; Spotify 0.4 GB of memory; explorer 0.2 GB of memory. Drives: C: 45 GB free of 931 GB; D: 1.2 TB free of 1.8 TB.";
                if (all != wanted || chrome != "chrome is using 19% of the processor and 3.1 GB of memory. " + Totals || game != "FortniteClient-Win64-Shipping is using 41% of the processor and 6.3 GB of memory. " + Totals
                    || absent != "Nothing called valorant is running. " + Totals || laptop != wanted + " Battery 54%, on battery." || unread != "Couldn't read how their PC is doing just now."
                    || Usage.Size(45 * Gigabyte) != "45 GB" || Usage.Size((long)(1.2 * 1024 * Gigabyte)) != "1.2 TB" || Usage.Size(Gigabyte / 2) != "0.5 GB")
                    throw new Exception("Her PC check didn't say how it's doing: " + all + " / " + chrome + " / " + game + " / " + absent + " / " + laptop + " / " + unread);
                // A check: it never asks first, stays out of her transcript, and every brain gets it, voice chat too.
                Hands.Kit kit = Hands.KitFor();
                bool voiced = false;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "pc_status") voiced = true;
                if (!Hands.Valid("pc_status", new ToolArguments()) || !Hands.Checking("pc_status", new ToolArguments()) || Security.Needs("pc_status", new ToolArguments()) != -1
                    || !kit.WorkTools.Contains("\"name\":\"pc_status\"") || !kit.Tools.Contains("pc_status") || !voiced || !Hands.WorkingOut.Contains("pc_status says how busy it is and what's using it"))
                    throw new Exception("Her PC check asked first, or missed a brain or voice chat.");
                // A brain that works jobs out finds out what's using the PC before it says why a game is stuttering.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                busy.Battery = null;
                Brain.OpenAi = FakeServer(new[] { calls("pc_status", "{}"), says("Your CPU's at 87%, choom. Chrome and OBS are eating a chunk next to Fortnite.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name); return Hands.Do(name, args); };
                string reply = planner.Ask("my game keeps stuttering, what's going on with my pc");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "Your CPU's at 87%, choom. Chrome and OBS are eating a chunk next to Fortnite." || string.Join(",", did.ToArray()) != "pc_status" || heard.Length != 2
                    || !heard[0].Contains("\"name\":\"pc_status\"") || !heard[1].Contains("FortniteClient-Win64-Shipping 41% of the processor and 6.3 GB"))
                    throw new Exception("Her planner didn't read the PC before answering: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                // This PC, read for real over half a second; nothing about it goes in this line.
                PcUse real = Usage.Read();
                if (real == null || real.Cpu < 0 || real.Cpu > 100 || real.MemoryTotal <= 0 || real.MemoryUsed <= 0 || real.Apps.Count == 0 || real.Drives.Count == 0)
                    throw new Exception("This PC couldn't be read: " + (real == null ? "nothing" : real.Cpu + "% / " + real.MemoryTotal + " / " + real.Apps.Count + " apps / " + real.Drives.Count + " drives"));
                checks.Add("PASS her PC check says how busy the processor and memory are, the five apps using the most processor and the three using the most memory besides, one app by name, each drive's free space and a laptop's battery, and when it can't read them; it never asks first, only a planner gets it, and a planner reads it before saying why a game stutters; this PC read for real: " + real.Apps.Count + " apps, " + real.Drives.Count + " drives");
            }
            finally
            {
                Hands.Use = useWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // Their internet on pretend connections: working, dropping out, down, a name lookup that fails and no network at all, with
        // nothing pinged from the checks; only this PC's own links are read for real (Lachy, 3 Nov: "is my internet down" or "what's
        // my ping" answered from the connection itself).
        private static void SmokeInternetStatus(List<string> checks)
        {
            Func<string, NetUse> netWas = Hands.Net;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                NetUse now = null;
                List<string> pinged = new List<string>();
                Hands.Net = delegate(string address) { pinged.Add(address ?? ""); return now; };
                Func<NetUse, string, string> check = delegate(NetUse use, string address) { now = use; return Hands.Do("internet_status", new ToolArguments { Address = address }); };
                NetUse working = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 0, Average = 14, Fastest = 12, Names = true };
                working.Links.Add("Ethernet");
                NetUse dropping = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 2, Average = 180, Fastest = 150, Names = true };
                dropping.Links.Add("Wi-Fi");
                NetUse down = new NetUse { Pinged = "8.8.8.8", Sent = 4, Lost = 4, Names = false };
                down.Links.Add("Wi-Fi");
                NetUse unnamed = new NetUse { Pinged = "1.1.1.1", Sent = 4, Lost = 0, Average = 20, Fastest = 18, Names = false };
                unnamed.Links.Add("Ethernet");
                unnamed.Links.Add("Wi-Fi");
                string fine = check(working, null), drops = check(dropping, null), dead = check(down, null), dns = check(unnamed, null), unplugged = check(new NetUse(), null), unread = check(null, null), site = check(working, "google.com");
                if (fine != "Their internet is working, over Ethernet: pings to 1.1.1.1 took 14 ms on average, 12 ms at best, and web addresses look up fine."
                    || drops != "Their internet is dropping out, over Wi-Fi: 2 of 4 pings to 1.1.1.1 were lost, and the rest took 180 ms on average, 150 ms at best, and web addresses look up fine."
                    || dead != "Their PC is connected over Wi-Fi, but nothing answered: all 4 pings to 8.8.8.8 were lost, and web addresses don't look up either, so the internet looks down."
                    || dns != "Their internet is working, over Ethernet and Wi-Fi: pings to 1.1.1.1 took 20 ms on average, 18 ms at best, but web addresses don't look up, so sites won't load: their DNS is the problem."
                    || unplugged != "Their PC isn't connected to any network: no Ethernet or Wi-Fi is up." || unread != "Couldn't check their internet just now."
                    || site != fine || string.Join(",", pinged.ToArray()) != ",,,,,,google.com")
                    throw new Exception("Her internet check didn't say how the connection is: " + fine + " / " + drops + " / " + dead + " / " + dns + " / " + unplugged + " / " + unread + " / " + string.Join(",", pinged.ToArray()));
                // A check: it never asks first, stays out of her transcript, and every brain gets it, voice chat too.
                Hands.Kit kit = Hands.KitFor();
                bool voiced = false;
                for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "internet_status") voiced = true;
                if (!Hands.Valid("internet_status", new ToolArguments()) || !Hands.Checking("internet_status", new ToolArguments()) || Security.Needs("internet_status", new ToolArguments()) != -1
                    || !kit.WorkTools.Contains("\"name\":\"internet_status\"") || !kit.Tools.Contains("internet_status") || !voiced || !Hands.WorkingOut.Contains("internet_status says whether their connection is the problem"))
                    throw new Exception("Her internet check asked first, or missed a brain or voice chat.");
                // A brain that works jobs out checks the connection before it blames the game.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                now = dropping;
                Brain.OpenAi = FakeServer(new[] { calls("internet_status", "{}"), says("It's your Wi-Fi, choom: half your pings are getting lost.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name); return Hands.Do(name, args); };
                string reply = planner.Ask("is it my internet or is valorant just lagging");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (reply != "It's your Wi-Fi, choom: half your pings are getting lost." || string.Join(",", did.ToArray()) != "internet_status" || heard.Length != 2
                    || !heard[0].Contains("\"name\":\"internet_status\"") || !heard[1].Contains("2 of 4 pings to 1.1.1.1 were lost"))
                    throw new Exception("Her planner didn't check the connection before answering: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                // This PC's links, read for real without pinging anything.
                List<string> links = Network.Links();
                if (links == null) throw new Exception("This PC's network links couldn't be read.");
                checks.Add("PASS her internet check says whether their connection works, is dropping out, is down or can't look names up, which link they're on, and pings an address they name; it never asks first, only a planner gets it, and a planner checks the connection before blaming a game; this PC's links read for real: " + links.Count);
                checks.Add("LIMIT her internet check's pings and name lookups on their own connection: the checks never ping out; ask \"is my internet down?\" and \"what's my ping to google?\"");
            }
            finally
            {
                Hands.Net = netWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // An ask opens each app, site or song once, however many times her brain repeats the call (a tester saw YouTube or Spotify
        // open about 20 times in a row on 0.7.10): her one-shot hands writing the same call over and over, and a planner calling it
        // twenty times at once and again a round later. Media still goes twice, like skipping two songs.
        private static void SmokeOpensOnce(List<string> checks)
        {
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                string looped = "";
                for (int i = 0; i < 20; i++) looped += "open_website{\"site\":\"youtube.com\"}\n";
                List<KeyValuePair<string, ToolArguments>> written = Hands.Written(looped);
                int parsed = written.Count;
                Brain.Once(written);
                List<KeyValuePair<string, ToolArguments>> mixed = Hands.Written("open_app{\"name\":\"Spotify\"}\nmedia{\"action\":\"next\"}\nopen_app{\"name\":\"spotify\"}\nmedia{\"action\":\"next\"}\nplay_music{\"song\":\"lofi beats\"}\nopen_website{\"site\":\"youtube.com\"}\nplay_music{\"song\":\"lofi beats\"}\nopen_app{\"name\":\"Discord\"}");
                Brain.Once(mixed);
                List<string> kept = mixed.ConvertAll(call => call.Key + ":" + (call.Value.Name ?? call.Value.Song ?? call.Value.Site ?? call.Value.Action));
                if (parsed != 20 || written.Count != 1 || string.Join(",", kept.ToArray()) != "open_app:Spotify,media:next,media:next,play_music:lofi beats,open_website:youtube.com,open_app:Discord")
                    throw new Exception("Repeated opens weren't cut to one: " + parsed + " written, " + written.Count + " left / " + string.Join(",", kept.ToArray()));
                // Her one-shot hands calling the same open twenty times at once open it once.
                List<string> asked = new List<string>(), did = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, int, string> repeated = delegate(string name, string json, int times)
                {
                    string made = "";
                    for (int i = 0; i < times; i++) made += (i == 0 ? "" : ",") + "{\"id\":\"c" + i + "\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}";
                    return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[" + made + "]}}]}";
                };
                Func<string, ToolArguments, string> opening = delegate(string name, ToolArguments args) { did.Add(name + ":" + (args.Name ?? args.Site)); return "Opened " + (args.Name ?? args.Site) + "."; };
                Brain.OpenAi = FakeServer(new[] { repeated("open_app", "{\"name\":\"Blorbo Studio\"}", 20), says("Blorbo Studio's up, choom."), says("Blorbo Studio's up, choom.") }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                Brain hands = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini") };
                hands.Act = opening;
                string handed = hands.Ask("boot up blorbo studio");
                int handAsks;
                lock (asked) handAsks = asked.Count;
                if (string.Join(",", did.ToArray()) != "open_app:Blorbo Studio" || handAsks == 0)
                    throw new Exception("Her hands opened it more than once, or never asked her brain: " + handed + " / " + string.Join(",", did.ToArray()) + " / " + handAsks + " calls");
                fake.Stop();
                fake = null;
                // A planner calling YouTube twenty times at once, then again a round later, opens it once and hears it's done.
                did.Clear();
                lock (asked) asked.Clear();
                Brain.OpenAi = FakeServer(new[] { repeated("open_website", "{\"site\":\"youtube.com\"}", 20), repeated("open_website", "{\"site\":\"youtube.com\"}", 1), says("YouTube's up, choom.") }, asked, out fake);
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = opening;
                string planned = planner.Ask("can you get youtube going for me while i eat");
                string[] heard;
                lock (asked) heard = asked.ToArray();
                if (planned != "YouTube's up, choom." || string.Join(",", did.ToArray()) != "open_website:youtube.com" || heard.Length != 3 || !heard[2].Contains("Already done this turn"))
                    throw new Exception("Her planner opened YouTube more than once: " + planned + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " calls");
                checks.Add("PASS an ask opens each app, site or song once: twenty calls at once from her hands open Blorbo Studio once, twenty written ones are cut to one, a planner calling YouTube twenty times at once and again a round later opens it once and hears it's done, and media still skips twice");
            }
            finally
            {
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // What she knows about herself and her help page cover her window tool, the settings she can change herself, pausing and
        // timers, in the house style: exact menu names, short lines.
        private static void SmokeHelpForTools(List<string> checks)
        {
            string[,] asks =
            {
                { "how do I move a window to my other monitor?", "Arrange windows:" }, { "can you snap my window to the left?", "Arrange windows:" },
                { "what settings can you change yourself?", "Settings she can change:" }, { "can you make yourself bigger?", "Settings she can change:" },
                { "What is the Arrange windows setting in Capri?", "Arrange windows:" }, { "how do I make you bigger?", "Size:" },
                { "can you set a timer?", "Reminders:" }, { "can you turn discord down?", "App volume:" }, { "can you use the volume mixer?", "App volume:" },
                { "can you see my other monitor?", "Look at another monitor:" }, { "why is my pc lagging?", "How your PC is doing:" }, { "is my internet down?", "How your PC is doing:" }
            };
            for (int i = 0; i < asks.GetLength(0); i++)
            {
                string found = SelfKnow.Matching(asks[i, 0]) ?? "";
                if (!found.StartsWith(asks[i, 1], StringComparison.Ordinal)) throw new Exception("Self knowledge missed " + asks[i, 1] + " for: " + asks[i, 0] + " / " + found);
            }
            string doing = SelfKnow.Matching("what can you do?") ?? "", closing = SelfKnow.Matching("Explain the Close and switch apps and tabs setting") ?? "", sizing = SelfKnow.Matching("how do I make you bigger?") ?? "";
            string changing = SelfKnow.Matching("what settings can you change yourself?") ?? "", timing = SelfKnow.Matching("how do timers work?") ?? "";
            if (!doing.Contains("maximize, snap, move, resize") || !doing.Contains("changes her own settings when you ask") || !closing.Contains("maximizing, snapping, moving and resizing windows")
                || !SelfKnow.Matching("how do I move a window to my other monitor?").Contains("keep Discord on top, until unpin Discord") || !sizing.Contains("make yourself bigger or smaller works too") || !timing.Contains("set a timer for 10 minutes, and cancel my timer stops it") || !changing.Contains("Hologram, Watch my screen and game Comments, plus Size") || !changing.Contains("Keys, brains, her voice service, SECURITY, Remember what she sees, Listen for \"Hey Capri\" and Remember our chats stay yours"))
                throw new Exception("Self knowledge didn't say what her tools do now: " + doing + " / " + closing + " / " + changing);
            string root = null;
            foreach (string start in new[] { AppDomain.CurrentDomain.BaseDirectory, Environment.CurrentDirectory })
            {
                for (System.IO.DirectoryInfo folder = new System.IO.DirectoryInfo(start); folder != null && root == null; folder = folder.Parent)
                    if (System.IO.File.Exists(System.IO.Path.Combine(folder.FullName, "site", "help", "knowledge.md"))) root = folder.FullName;
                if (root != null) break;
            }
            if (root == null)
            {
                checks.Add("PASS self knowledge covers her window tool, the settings she can change herself and Close and switch apps and tabs");
                checks.Add("LIMIT her help page's new sections need the source tree; it is not beside this executable or working directory");
                return;
            }
            string help = System.IO.File.ReadAllText(System.IO.Path.Combine(root, "site", "help", "knowledge.md"));
            foreach (string part in new[] { "### Move, snap and resize windows (0.8.0)", "### Her own settings (0.8.0)", "\"Send Discord to my other monitor\"", "Timers are reminders too (0.8.0)", "One app's own volume (0.8.0), with a cloud brain", "With more than one monitor (0.8.0), she looks at the one with the window you're using.", "### How your PC is doing (0.8.0)", "Ask \"is my internet down?\" or \"what's my ping?\" and she checks your connection too", "\"Keep Discord on top\" keeps it above your other windows until you say \"Unpin Discord\".", "\"Cancel my timer\" stops it.", "\"pause\" with nothing playing doesn't start your music", "4. Close and switch apps and tabs. It covers maximizing, snapping, moving and resizing windows too." })
                if (help.IndexOf(part, StringComparison.Ordinal) < 0) throw new Exception("Her help page is missing: " + part);
            int from = help.IndexOf("### Move, snap and resize windows (0.8.0)", StringComparison.Ordinal), to = help.IndexOf("### Discord and Windows shortcuts", from, StringComparison.Ordinal);
            if (to < from || help.Substring(from, to - from).IndexOf('\u2014') >= 0) throw new Exception("Her help page's new sections are out of place or use an em dash.");
            checks.Add("PASS self knowledge and her help page cover her window tool, the settings she can change herself, pausing only what's playing, timers, one app's own volume, looking at the monitor they name, how their PC is doing and Close and switch apps and tabs, with exact menu names");
        }

        // Her settings tool's sliders and everyday switches on pretend settings: a step by name, by number or one step up or down,
        // read back afterwards, with the ends and a slider that won't move said plainly, and nothing she mustn't touch in reach
        // (Lachy, 3 Nov: "make yourself bigger" or "stop wandering" works through the brain).
        private static void SmokeSettingsTool(List<string> checks)
        {
            Func<string, bool?> settingWas = Hands.SettingOn;
            Func<string, bool, string> setWas = Hands.SetSetting;
            Func<string, int> levelWas = Hands.LevelOf;
            Func<string, int, string> moveWas = Hands.SetLevel;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            TcpListener fake = null;
            try
            {
                Dictionary<string, bool?> switches = new Dictionary<string, bool?> { { "wander", true }, { "head_pats", true }, { "sit_on_windows", true }, { "typing_sound", false } };
                Dictionary<string, int> levels = new Dictionary<string, int> { { "size", 3 }, { "wander", 1 }, { "pop_ins", 1 }, { "sleep", 3 }, { "volume", 8 }, { "voice_speed", 5 }, { "replies", 1 }, { "bubble", 1 } };
                List<string> moved = new List<string>();
                Hands.SettingOn = delegate(string name) { return switches.ContainsKey(name) ? switches[name] : null; };
                Hands.SetSetting = delegate(string name, bool on) { switches[name] = on; return null; };
                Hands.LevelOf = delegate(string name) { return levels.ContainsKey(name) ? levels[name] : -1; };
                // Her bubble's slider is stuck, the way a setting can be when something else holds it.
                Hands.SetLevel = delegate(string name, int step) { moved.Add(name + " " + step); if (name != "bubble") levels[name] = step; return null; };
                Func<string, string, string> set = delegate(string name, string level) { return Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = name, Level = level }); };
                string[,] asks =
                {
                    { "size", "bigger", "Set my size to Medium+." }, { "size", "Giant", "Set my size to Giant." }, { "size", "a bit bigger", "My size is already Giant, as far as it goes." },
                    { "size", "giant", "My size is already Giant." }, { "size", "smallest", "Set my size to Tiny." }, { "size", "huge+", "Set my size to Huge+." },
                    { "wander", "off", "Set Wander to Off." }, { "wander", "more", "Set Wander to Rarely." }, { "wander", "Restless", "Set Wander to Restless." },
                    { "volume", "50", "Set my voice's volume to 50%." }, { "volume", "louder", "Set my voice's volume to 60%." }, { "volume", "max", "Set my voice's volume to 150%." }, { "volume", "mute", "Set my voice's volume to Mute." },
                    { "voice_speed", "1.3", "Set my voice's speed to 1.3x." }, { "voice_speed", "slower", "Set my voice's speed to 1.2x." }, { "voice_speed", "1x", "Set my voice's speed to Normal." }, { "voice_speed", "normal", "My voice's speed is already Normal." },
                    { "sleep", "10 minutes", "Set Sleep to 10 min." }, { "sleep", "never", "Set Sleep to Never." }, { "sleep", "to 30 min", "Set Sleep to 30 min." },
                    { "pop_ins", "often", "Set Pop-ins to Often." }, { "replies", "shorter", "Set Replies to Short." }, { "replies", "shorter", "Replies is already Short, as far as it goes." },
                    { "bubble", "long", "Couldn't set Bubble to Long: it's still Normal." }, { "size", "humongous", "There's no humongous for my size. It goes Tiny, Small, Small+, Medium, Medium+, Large, Large+, Huge, Huge+, Giant." },
                    { "brain", "bigger", "There's no slider of mine called brain." }
                };
                List<string> wrong = new List<string>();
                for (int i = 0; i < asks.GetLength(0); i++)
                {
                    string got = set(asks[i, 0], asks[i, 1]);
                    if (got != asks[i, 2]) wrong.Add(asks[i, 0] + " " + asks[i, 1] + " said \"" + got + "\"");
                }
                if (wrong.Count > 0) throw new Exception("Her sliders didn't say where they ended up: " + string.Join(" / ", wrong.ToArray()));
                if (!Hands.Failed(set("bubble", "quick")) || !Hands.Failed(set("size", "humongous")) || Hands.Failed(set("size", "medium")) || string.Join(",", moved.ToArray()).IndexOf("bubble 2", StringComparison.Ordinal) < 0)
                    throw new Exception("A slider that didn't move wasn't read as a failure: " + string.Join(",", moved.ToArray()));
                levels["size"] = -1;
                string unread = set("size", "bigger");
                levels["size"] = 3;
                switches["head_pats"] = true;
                string pats = Hands.Do("capri_settings", new ToolArguments { Action = "off", Name = "head_pats" }), typing = Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "typing_sound" });
                if (unread != "Couldn't read my size just now." || pats != "Turned Head pats off." || typing != "Turned Typing sound on.")
                    throw new Exception("Her settings tool didn't read her new switches back: " + unread + " / " + pats + " / " + typing);
                // Only a slider takes set, only a switch takes on or off, and set needs a level; none of it asks first or counts as a check.
                ToolArguments bigger = new ToolArguments { Action = "set", Name = "size", Level = "bigger" };
                if (!Hands.Valid("capri_settings", bigger) || Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = "size" }) || Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = "head_pats", Level = "up" })
                    || Hands.Valid("capri_settings", new ToolArguments { Action = "on", Name = "size" }) || !Hands.Valid("capri_settings", new ToolArguments { Action = "off", Name = "sit_on_windows" }) || Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = "watch_my_screen", Level = "on" })
                    || Hands.Checking("capri_settings", bigger) || Security.Needs("capri_settings", bigger) != -1)
                    throw new Exception("Her settings tool's calls weren't checked.");
                // Nothing she mustn't touch is in reach: keys, brains, her voice service, SECURITY, memories or "Hey Capri". Every one of
                // her own settings is (Lachy, 3 Nov: her brain can change any of them).
                string tools = Hands.KitFor().WorkTools;
                int at = tools.IndexOf("\"name\":\"capri_settings\"", StringComparison.Ordinal);
                string spec = at < 0 ? "" : tools.Substring(at, tools.IndexOf("}}", tools.IndexOf("\"level\"", at), StringComparison.Ordinal) - at);
                string names = spec.Length == 0 ? "" : spec.Substring(spec.IndexOf("\"enum\":[\"voice\"", StringComparison.Ordinal));
                names = names.Substring(0, names.IndexOf(']') + 1);
                foreach (string never in new[] { "api_key", "brain", "model", "voice_service", "security", "blackwall", "permissions", "remember", "memories", "hey_capri", "password", "bypass" })
                    if (names.Contains("\"" + never + "\"")) throw new Exception("Her settings tool reached " + never + ".");
                foreach (string name in Hands.SettingNames())
                    if (Array.IndexOf(Hands.SettingActions, name) < 0 && !names.Contains("\"" + name + "\"")) throw new Exception("Her settings tool omitted " + name + ".");
                // OpenAI refuses every tool when one's description runs past 1024 characters, so none of hers may.
                for (int i = 0; i < Hands.Count; i++)
                    if (Hands.Description(i).Length > 1024) throw new Exception("OpenAI would refuse " + Hands.Name(i) + "'s description: " + Hands.Description(i).Length + " characters.");
                foreach (string name in new[] { "voice", "quiet", "mic", "check_ins", "wander", "dance_to_music", "head_pats", "chaos_when_upset", "sit_on_windows", "always_on_top", "mini_player", "typing_sound", "follow_ups", "hide_in_full_screen", "glitch_in", "clip_my_games", "save_big_moments", "live_reactions", "gaming_mode", "hologram", "watch_my_screen", "game_comments" })
                    if (!Hands.Valid("capri_settings", new ToolArguments { Action = "on", Name = name })) throw new Exception("Her settings tool's switch " + name + " wasn't taken.");
                foreach (string name in new[] { "size", "wander", "pop_ins", "sleep", "volume", "voice_speed", "replies", "bubble" })
                    if (!Hands.Valid("capri_settings", new ToolArguments { Action = "set", Name = name, Level = "up" })) throw new Exception("Her settings tool's slider " + name + " wasn't taken.");
                // Her sliders' steps are the ones her Mainframe's sliders stop on, by name and by value.
                string[] mainframeNames = { "SizeNames", "WanderNames", "PopNames", "SleepNames", "VolumeNames", "PaceNames", "Lengths", "BubbleNames" };
                for (int i = 0; i < mainframeNames.Length; i++)
                {
                    string[] shown = (string[])typeof(Mainframe).GetField(mainframeNames[i], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    if (string.Join(",", shown) != string.Join(",", Hands.OwnSteps[i])) throw new Exception("Her settings tool's " + mainframeNames[i] + " aren't her Mainframe's: " + string.Join(",", Hands.OwnSteps[i]));
                }
                string[,] values = { { "SizeSteps", "OwnSizes" }, { "Sleeps", "OwnSleeps" }, { "Volumes", "OwnVolumes" }, { "Paces", "OwnPaces" } };
                for (int i = 0; i < values.GetLength(0); i++)
                {
                    int[] shown = (int[])typeof(Mainframe).GetField(values[i, 0], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    int[] own = (int[])typeof(Companion).GetField(values[i, 1], System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
                    if (string.Join(",", Array.ConvertAll(shown, step => step.ToString())) != string.Join(",", Array.ConvertAll(own, step => step.ToString()))) throw new Exception("Her settings tool's " + values[i, 1] + " aren't her Mainframe's " + values[i, 0] + ".");
                }
                // Saying she changed one with no tool behind it is a claim; questions and where a setting lives aren't.
                List<KeyValuePair<string, ToolArguments>> none = new List<KeyValuePair<string, ToolArguments>>();
                string[,] claims =
                {
                    { "There, I made myself bigger!", "capri_settings" }, { "Okay, I stopped wandering.", "capri_settings" }, { "Turned off head pats, choom.", "capri_settings" }, { "Set my volume to 50%.", "capri_settings" },
                    { "I switched Always on top off.", "capri_settings" }, { "Want me to make myself bigger?", "" }, { "You can make me bigger in Settings.", "" }, { "Head pats are on by default.", "" }, { "Should I stop wandering?", "" }
                };
                for (int i = 0; i < claims.GetLength(0); i++)
                    if ((Hands.Claimed(claims[i, 0], none) ?? "") != claims[i, 1]) throw new Exception("\"" + claims[i, 0] + "\" read as a claim for " + (Hands.Claimed(claims[i, 0], none) ?? "nothing") + " instead of " + (claims[i, 1].Length == 0 ? "nothing" : claims[i, 1]));
                // A brain that works jobs out makes itself bigger and stops wandering with the tool, and can't just say it did.
                List<string> asked = new List<string>();
                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                levels["size"] = 3;
                switches["wander"] = true;
                Brain.OpenAi = FakeServer(new[]
                {
                    calls("capri_settings", "{\"action\":\"set\",\"name\":\"size\",\"level\":\"bigger\"}"), says("There, a bit bigger, choom."),
                    calls("capri_settings", "{\"action\":\"off\",\"name\":\"wander\"}"), says("Okay, I'll stay put."),
                    says("Done, I made myself bigger."), says("Done, I made myself bigger.")
                }, asked, out fake);
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + ":" + args.Name + ":" + args.Action + (args.Level == null ? "" : ":" + args.Level)); return Hands.Do(name, args); };
                string grown = planner.Ask("make yourself bigger");
                string still = planner.Ask("stop wandering");
                string faked = planner.Ask("make yourself even bigger");
                string[] sent;
                lock (asked) sent = asked.ToArray();
                if (grown != "There, a bit bigger, choom." || still != "Okay, I'll stay put." || faked != "That setting hasn't changed yet. It's in Capri Mainframe > Settings." || sent.Length != 6
                    || string.Join(",", did.ToArray()) != "capri_settings:size:set:bigger,capri_settings:wander:off" || levels["size"] != 4 || switches["wander"] != false
                    || !sent[0].Contains("like set size bigger for make yourself bigger") || !sent[0].Contains("\"voice_speed\"") || !sent[1].Contains("Set my size to Medium+.") || !sent[3].Contains("Turned Wander off.")
                    || !sent[5].Contains("You said you did something that needs capri_settings"))
                    throw new Exception("Her planner didn't change her settings with the tool: " + grown + " / " + still + " / " + faked + " / " + string.Join(",", did.ToArray()) + " / " + sent.Length + " calls");
                checks.Add("PASS her settings tool moves her sliders: size, wander, pop-ins, sleep, her voice's volume and speed, replies and bubble, by a step's name, a number, or a step up or down, says when one is already as far as it goes, when a step doesn't exist and when a slider didn't move, and flips her everyday switches like head pats, dancing, sitting on windows and always on top; only sliders take set and nothing she mustn't touch is in reach; her steps match her Mainframe's; saying she changed one with no tool is a claim; a planner makes herself bigger and stops wandering with the tool");
            }
            finally
            {
                Hands.SettingOn = settingWas;
                Hands.SetSetting = setWas;
                Hands.LevelOf = levelWas;
                Hands.SetLevel = moveWas;
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                if (fake != null) fake.Stop();
            }
        }

        // Her settings tool on her real switches and sliders: each new switch flipped and flipped back the way its own switch does,
        // and each slider moved and read back from the setting itself, with nothing of her own said over her brain's reply.
        private void SmokeOwnSliders(List<string> checks)
        {
            bool danceWas = preferences.NoDance, patsWas = preferences.NoHeadPats, chaosWas = preferences.UpsetChaos, sitWas = preferences.NoSitting, topWas = preferences.OnTop, playerWas = preferences.NoPlayer;
            bool followWas = preferences.FollowUps, fullWas = preferences.HideFullScreen, introWas = preferences.NoIntro;
            int typingWas = preferences.Typing, sizeWas = preferences.Size, wanderWas = WanderLevel, popWas = preferences.PopPace, sleepWas = preferences.SleepAfter, volumeWas = Voice.Loudness, speedWas = VoiceFx.Speed, wordsWas = Brain.Wordiness, bubbleWas = preferences.BubbleTime;
            Action<short[]> typeOutWas = Sfx.TypeOut;
            int askingWas = asking;
            try
            {
                // Her typing sound plays a sample when it's turned on; the check keeps it off their speakers.
                Sfx.TypeOut = null;
                List<string> wrong = new List<string>();
                string[,] switches =
                {
                    { "dance_to_music", "Dance to music" }, { "head_pats", "Head pats" }, { "chaos_when_upset", "Chaos when upset" }, { "sit_on_windows", "Sit on windows" }, { "always_on_top", "Always on top" },
                    { "mini_player", "Mini player" }, { "typing_sound", "Typing sound" }, { "follow_ups", "Follow-up messages" }, { "hide_in_full_screen", "Hide in full screen" }, { "glitch_in", "Glitch in at startup" }
                };
                Func<string, bool> real = delegate(string name)
                {
                    switch (name)
                    {
                        case "dance_to_music": return !preferences.NoDance;
                        case "head_pats": return !preferences.NoHeadPats;
                        case "chaos_when_upset": return preferences.UpsetChaos;
                        case "sit_on_windows": return !preferences.NoSitting;
                        case "always_on_top": return preferences.OnTop;
                        case "mini_player": return !preferences.NoPlayer;
                        case "typing_sound": return TypingSound;
                        case "follow_ups": return preferences.FollowUps;
                        case "hide_in_full_screen": return preferences.HideFullScreen;
                        default: return !preferences.NoIntro;
                    }
                };
                for (int i = 0; i < switches.GetLength(0); i++)
                {
                    string name = switches[i, 0];
                    bool was = real(name);
                    string flipped = Hands.Do("capri_settings", new ToolArguments { Action = was ? "off" : "on", Name = name });
                    bool now = real(name), lifted = TopMost == preferences.OnTop;
                    string back = Hands.Do("capri_settings", new ToolArguments { Action = was ? "on" : "off", Name = name });
                    if (flipped != "Turned " + switches[i, 1] + (was ? " off." : " on.") || now == was || back != "Turned " + switches[i, 1] + (was ? " on." : " off.") || real(name) != was || name == "always_on_top" && !lifted)
                        wrong.Add(name + ": " + flipped + " / " + back);
                }
                if (wrong.Count > 0) throw new Exception("Her settings tool didn't flip her real switches: " + string.Join(" | ", wrong.ToArray()));
                SetCharacterSize(240);
                SetWanderLevel(1);
                SetPopPace(1);
                SetSleepAfter(300);
                SetVolume(80);
                SetVoiceSpeed(120);
                SetWordiness(1);
                SetBubbleTime(1);
                Func<string, string, string> set = delegate(string name, string level) { return Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = name, Level = level }); };
                string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list" });
                string bigger = set("size", "bigger"), giant = set("size", "giant"), wander = set("wander", "often"), still = set("wander", "off"), pops = set("pop_ins", "rarely"), sleep = set("sleep", "10 min");
                string volume = set("volume", "50%"), speed = set("voice_speed", "faster"), replies = set("replies", "long"), bubble = set("bubble", "quick");
                if (!listed.Contains("my size: Medium; Wander: Rarely; Pop-ins: Sometimes; Sleep: 5 min; my voice's volume: 80%; my voice's speed: 1.2x; Replies: Medium; Bubble: Normal;")
                    || bigger != "Set my size to Medium+." || giant != "Set my size to Giant." || preferences.Size != MaxSize || Size != CanvasSize(MaxSize)
                    || wander != "Set Wander to Often." || still != "Set Wander to Off." || preferences.Wander || WanderLevel != 0 || preferences.WanderPace != 2
                    || pops != "Set Pop-ins to Rarely." || preferences.PopPace != 0 || sleep != "Set Sleep to 10 min." || preferences.SleepAfter != 600
                    || volume != "Set my voice's volume to 50%." || Voice.Loudness != 50 || preferences.Volume != 50 || speed != "Set my voice's speed to 1.3x." || VoiceFx.Speed != 130
                    || replies != "Set Replies to Long." || Brain.Wordiness != 2 || bubble != "Set Bubble to Quick." || preferences.BubbleTime != 0 || asking != askingWas)
                    throw new Exception("Her settings tool didn't move her real sliders: " + listed + " / " + bigger + " / " + giant + " / " + wander + " / " + still + " / " + pops + " / " + sleep + " / " + volume + " / " + speed + " / " + replies + " / " + bubble + " / asking moved " + (asking - askingWas));
                checks.Add("PASS her settings tool flips Dance to music, Head pats, Chaos when upset, Sit on windows, Always on top, Mini player, Typing sound, Follow-up messages, Hide in full screen and Glitch in at startup the way their own switches do, moves her size, wander, pop-ins, sleep, voice volume and speed, replies and bubble the way her Mainframe's sliders do, reads each one back from the setting itself, and never talks over her brain's reply");
            }
            finally
            {
                Sfx.TypeOut = typeOutWas;
                if (preferences.NoDance != danceWas) ToggleDance();
                if (preferences.NoHeadPats != patsWas) ToggleHeadPats();
                if (preferences.UpsetChaos != chaosWas) ToggleUpsetChaos();
                if (preferences.NoSitting != sitWas) ToggleSitting();
                if (preferences.OnTop != topWas) ToggleOnTop();
                if (preferences.NoPlayer != playerWas) TogglePlayer();
                if (preferences.FollowUps != followWas) ToggleFollowUps();
                if (preferences.HideFullScreen != fullWas) ToggleHideFullScreen();
                if (preferences.NoIntro != introWas) ToggleIntro();
                preferences.Typing = typingWas;
                if (preferences.Size != sizeWas) SetCharacterSize(sizeWas);
                SetWanderLevel(wanderWas);
                SetPopPace(popWas);
                SetSleepAfter(sleepWas);
                SetVolume(volumeWas);
                SetVoiceSpeed(speedWas);
                SetWordiness(wordsWas);
                SetBubbleTime(bubbleWas);
                SavePreferences();
                mainframe.Sync();
            }
        }
    }
}
