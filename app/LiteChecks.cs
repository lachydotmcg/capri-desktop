using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private const double UseSeconds = 30;

        private sealed class UseWindow
        {
            internal string State;
            internal double Seconds, CoreMs, UiMs, Ticks, Paints, ChildCpu;
            internal long PrivateAvg, PrivateMax, WorkingAvg, WorkingMax, ChildRam;
            internal string Children = "none";

            internal double CorePercent { get { return Seconds <= 0 ? 0 : CoreMs / (Seconds * 10); } }

            internal double TickRate { get { return Seconds <= 0 ? 0 : Ticks / Seconds; } }

            internal string Row()
            {
                return string.Format(CultureInfo.InvariantCulture, "| {0} | {1:0.00}% | {2:0.00} | {3:0.0} / {4:0.0} | {5:0.0} / {6:0.0} | {7:0.0} / {8:0.0} | {9} |",
                    State, CorePercent, Seconds <= 0 ? 0 : UiMs / Seconds, TickRate, Seconds <= 0 ? 0 : Paints / Seconds,
                    PrivateAvg / 1048576.0, PrivateMax / 1048576.0, WorkingAvg / 1048576.0, WorkingMax / 1048576.0, Children);
            }
        }

        private static void PumpFor(double seconds)
        {
            Stopwatch wait = Stopwatch.StartNew();
            while (wait.Elapsed.TotalSeconds < seconds)
            {
                Native.MsgWaitForMultipleObjectsEx(0, null, 20, 0x04FF, 0x0004);
                Application.DoEvents();
            }
        }

        private UseWindow MeasureUse(string state, double seconds)
        {
            UseWindow window = new UseWindow { State = state };
            int ticks = 0;
            EventHandler counted = delegate { ticks++; };
            SystemUse children = new SystemUse();
            frames.Tick += counted;
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                PumpFor(2);
                using (Process self = Process.GetCurrentProcess())
                {
                    children.Sample(BrainServerId, false);
                    self.Refresh();
                    double coreStart = self.TotalProcessorTime.TotalMilliseconds, uiStart = PerfCpu();
                    int paintStart = bodyPaints;
                    ticks = 0;
                    long privateSum = 0, workingSum = 0;
                    int reads = 0;
                    double next = 0;
                    Stopwatch wall = Stopwatch.StartNew();
                    while (wall.Elapsed.TotalSeconds < seconds)
                    {
                        Native.MsgWaitForMultipleObjectsEx(0, null, 20, 0x04FF, 0x0004);
                        Application.DoEvents();
                        if (wall.Elapsed.TotalSeconds < next) continue;
                        next += 1;
                        self.Refresh();
                        privateSum += self.PrivateMemorySize64;
                        workingSum += self.WorkingSet64;
                        reads++;
                        window.PrivateMax = Math.Max(window.PrivateMax, self.PrivateMemorySize64);
                        window.WorkingMax = Math.Max(window.WorkingMax, self.WorkingSet64);
                    }
                    window.Seconds = wall.Elapsed.TotalSeconds;
                    self.Refresh();
                    window.CoreMs = self.TotalProcessorTime.TotalMilliseconds - coreStart;
                    window.UiMs = PerfCpu() - uiStart;
                    window.Ticks = ticks;
                    window.Paints = bodyPaints - paintStart;
                    window.PrivateAvg = reads == 0 ? 0 : privateSum / reads;
                    window.WorkingAvg = reads == 0 ? 0 : workingSum / reads;
                    UseView view = children.Sample(BrainServerId, false);
                    List<string> named = new List<string>();
                    foreach (UseRow row in view.Capri)
                    {
                        if (row.Name == "Capri") continue;
                        window.ChildRam += row.Ram;
                        window.ChildCpu += row.Cpu;
                        named.Add(string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} MB {2:0.00}%", row.Name, row.Ram / 1048576.0, row.Cpu));
                    }
                    if (named.Count > 0) window.Children = string.Join(", ", named.ToArray());
                }
            }
            finally { frames.Tick -= counted; }
            return window;
        }

        private string RestPace()
        {
            double at = clock.Elapsed.TotalSeconds;
            return FrameInterval(at) + " ms at " + PerchRateNow(at) + " fps";
        }

        private void SmokeLiteUse(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            if (!IsHandleCreated || hidden) { checks.Add("LIMIT Lite use: she was hidden, so her drawing couldn't be measured here"); return; }
            bool framesWere = frames.Enabled, signalsWere = signals.Enabled, busyWas = frontBusy, throughWas = fullThrough, ridingWas = riding, liteWas = preferences.Lite;
            int saverWas = preferences.PowerSaver, fpsWas = preferences.PerchFps;
            Func<long> idleWas = FakedIdle;
            Ledge seatWas = seat;
            Clip clipWas = clip;
            string speechWas = speech;
            double movedWas = seatMovedAt;
            List<UseWindow> windows = new List<UseWindow>();
            StringBuilder table = new StringBuilder();
            try
            {
                seat = new Ledge { Floor = true, Left = anchor.X - 400, Right = anchor.X + Width + 400, Y = Hip().Y };
                riding = false;
                speech = null;
                seatMovedAt = -10;
                frontBusy = fullThrough = false;
                preferences.PerchFps = 30;
                preferences.Lite = false;
                Play(perch);
                frames.Start();
                signals.Start();
                preferences.PowerSaver = 0;
                windows.Add(MeasureUse("Resting, Power saver Off", UseSeconds));
                string offPace = RestPace();
                preferences.PowerSaver = 2;
                windows.Add(MeasureUse("Resting, Power saver Always", UseSeconds));
                preferences.PowerSaver = 0;
                preferences.Lite = true;
                FakedIdle = delegate { return 0; };
                windows.Add(MeasureUse("Resting, Lite mode, PC in use", UseSeconds));
                string usedPace = RestPace();
                FakedIdle = delegate { return LiteIdleMs + 1000; };
                windows.Add(MeasureUse("Resting, Lite mode, PC idle a minute", UseSeconds));
                string idlePace = RestPace();
                preferences.Lite = liteWas;
                FakedIdle = idleWas;
                preferences.PowerSaver = saverWas;
                SaverTick();
                if (offPace != "33 ms at 30 fps" || usedPace != "61 ms at 15 fps" || idlePace != "92 ms at 10 fps")
                    throw new Exception("Lite mode's rest pace was wrong: Power saver Off " + offPace + ", Lite with the PC in use " + usedPace + ", idle " + idlePace);
                if (windows[2].TickRate >= windows[0].TickRate || windows[3].TickRate >= windows[2].TickRate)
                    throw new Exception(string.Format(CultureInfo.InvariantCulture, "Lite mode didn't tick less: Off {0:0.0}, Lite in use {1:0.0}, Lite idle {2:0.0} ticks a second", windows[0].TickRate, windows[2].TickRate, windows[3].TickRate));

                long held, rested, kept;
                double perchLoad, laydownLoad;
                using (Process self = Process.GetCurrentProcess())
                {
                    PumpFor(1);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    self.Refresh();
                    held = self.PrivateMemorySize64;
                    Clip.RestAll(unchecked(Environment.TickCount + DockedArtRest + 2000), DockedArtRest);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    self.Refresh();
                    rested = self.PrivateMemorySize64;
                    Stopwatch load = Stopwatch.StartNew();
                    drawnClip = null;
                    RenderFrame();
                    perchLoad = load.Elapsed.TotalMilliseconds;
                    self.Refresh();
                    kept = self.PrivateMemorySize64;
                    load.Restart();
                    laydown.Load(false);
                    laydownLoad = load.Elapsed.TotalMilliseconds;
                    Clip.WarmAll();
                    PumpFor(4);
                }
                table.AppendLine("| State | CPU (one core) | UI thread ms/s | Ticks / paints a second | Private MB avg / peak | Working set MB avg / peak | Her child processes |");
                table.AppendLine("|---|---|---|---|---|---|---|");
                foreach (UseWindow window in windows) table.AppendLine(window.Row());
                table.AppendLine();
                table.AppendLine(string.Format(CultureInfo.InvariantCulture, "Rest pace: Power saver Off {0}; Lite mode with the PC in use {1}; Lite mode after a minute with no input {2}.", offPace, usedPace, idlePace));
                table.AppendLine();
                table.AppendLine(string.Format(CultureInfo.InvariantCulture, "Art: letting go of every strip she isn't drawing took private memory from {0:0.0} MB to {1:0.0} MB ({2:0.0} MB freed); once the perch she sits in came back it was {3:0.0} MB below where it started. Loading her perch strip again took {4:0.0} ms and her laydown strip {5:0.0} ms on this PC.",
                    held / 1048576.0, rested / 1048576.0, (held - rested) / 1048576.0, (held - kept) / 1048576.0, perchLoad, laydownLoad));
                File.WriteAllText(Path.Combine(output, "lite-use.md"), table.ToString());
                foreach (UseWindow window in windows)
                    checks.Add(string.Format(CultureInfo.InvariantCulture, "PASS Lite use: {0} over {1:0} s: CPU {2:0.00}% of one core, UI thread {3:0.00} ms a second, {4:0.0} ticks and {5:0.0} paints a second, private {6:0.0} MB (peak {7:0.0}), working set {8:0.0} MB (peak {9:0.0}), children {10}",
                        window.State, window.Seconds, window.CorePercent, window.Seconds <= 0 ? 0 : window.UiMs / window.Seconds, window.TickRate, window.Seconds <= 0 ? 0 : window.Paints / window.Seconds,
                        window.PrivateAvg / 1048576.0, window.PrivateMax / 1048576.0, window.WorkingAvg / 1048576.0, window.WorkingMax / 1048576.0, window.Children));
                checks.Add("PASS Lite use: resting on a window she ticks at " + offPace + " with Power saver Off, " + usedPace + " in Lite mode while the PC is in use, and " + idlePace + " once nobody has touched the mouse or keys for a minute");
                checks.Add(string.Format(CultureInfo.InvariantCulture, "PASS Lite use: letting go of the art she isn't drawing freed {0:0.0} MB of private memory, {1:0.0} MB once her perch came back; her perch strip reloads in {2:0.0} ms and her laydown strip in {3:0.0} ms",
                    (held - rested) / 1048576.0, (held - kept) / 1048576.0, perchLoad, laydownLoad));
                checks.Add("LIMIT Lite use: her brain, HD voice, ears and wake word need their downloads, and Watch my screen, game comments and Clip my games don't run in a smoke test, so these numbers are Capri's own process at rest");
            }
            finally
            {
                preferences.Lite = liteWas;
                FakedIdle = idleWas;
                preferences.PowerSaver = saverWas;
                preferences.PerchFps = fpsWas;
                SaverTick();
                frontBusy = busyWas;
                fullThrough = throughWas;
                riding = ridingWas;
                seat = seatWas;
                speech = speechWas;
                seatMovedAt = movedWas;
                Play(clipWas);
                if (!framesWere) frames.Stop();
                if (!signalsWere) signals.Stop();
                RenderFrame();
            }
        }

        private void SmokeLite(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            if (preferences.NoAi) { checks.Add("LIMIT Lite mode: No AI is on in this run, so her brain's side of Lite mode wasn't checked"); return; }
            bool liteWas = preferences.Lite, silentWas = preferences.Silent, watchWas = preferences.Watch, clipsWas = preferences.ClipGames, momentsWas = preferences.ClipMoments, noCommentsWas = preferences.NoComments;
            bool advancedWas = preferences.AdvancedSettings, simpleWas = simpleTest, pageWas = mainframe.SettingsShown, shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            int paceWas = preferences.CommentPace, paceBackWas = preferences.CommentPaceWas, saverWas = preferences.PowerSaver, limitWas = Brain.GpuLimit;
            string[] keptWas = preferences.LiteKept;
            string ollamaWas = Brain.Ollama, modelWas = brain.Model, heardWas = heard, statusWas = mainframe.Status, addressWas, keyWas;
            brain.GetServer(out addressWas, out keyWas);
            Cloud remoteWas = brain.Remote;
            Engine localWas = brain.Local;
            Pick pickWas = brain.LocalPick;
            Action<string> browseWas = Browse;
            Func<ProcessStartInfo, Process> spawnWas = Engine.Spawn;
            List<System.Net.Sockets.TcpListener> fakes = new List<System.Net.Sockets.TcpListener>();
            List<string> made = new List<string>(), pages = new List<string>(), asked = new List<string>();
            try
            {
                const string done = "{\"model\":\"lite-stand-in\",\"message\":{\"role\":\"assistant\",\"content\":\"\"},\"done\":true}";
                System.Net.Sockets.TcpListener ollamaFake;
                Brain.Ollama = FakeServer(new string[40], asked, out ollamaFake, respond: delegate(string raw) { return done; });
                lock (fakes) fakes.Add(ollamaFake);
                Brain.UnloadOllama();
                Brain.GpuLimit = 0;
                preferences.Silent = true;
                Browse = pages.Add;
                brain.Remote = null;
                brain.SetServer(null, null);
                brain.Local = null;
                brain.LocalPick = null;
                brain.Model = null;
                preferences.Lite = Brain.Lite = false;
                preferences.LiteKept = null;

                preferences.Watch = true;
                SetCommentPace(Commentary.Chatty);
                preferences.ClipGames = preferences.ClipMoments = true;
                mainframe.Sync();
                string onNote = SetLite(true, false);
                string kept = string.Join(",", preferences.LiteKept ?? new string[0]);
                bool paused = !preferences.Watch && CommentPace == Commentary.Off && !preferences.ClipGames && !preferences.ClipMoments && preferences.Lite && Brain.Lite && mainframe.LiteShown;
                Preferences savedOn = LoadPreferences();
                bool savedLite = savedOn.Lite && string.Join(",", savedOn.LiteKept ?? new string[0]) == kept;
                preferences.PowerSaver = 0;
                bool lighter = Saving && HdStepsAside();
                preferences.PowerSaver = saverWas;
                string offNote = SetLite(false, false);
                bool back = preferences.Watch && CommentPace == Commentary.Chatty && preferences.ClipGames && preferences.ClipMoments && !preferences.Lite && !Brain.Lite && preferences.LiteKept == null && !mainframe.LiteShown;
                Preferences savedOff = LoadPreferences();
                if (kept != "watch_my_screen,game_comments,clip_my_games,save_big_moments" || !paused || !savedLite || !lighter || !back || savedOff.Lite || savedOff.LiteKept != null
                    || onNote != "Off until Lite mode goes off: Watch my screen, game comments and Clip my games." || offNote != "Back on as before: Watch my screen, game comments and Clip my games.")
                    throw new Exception("Lite mode didn't pause and give back her watchers: kept " + kept + ", paused " + paused + ", saved " + savedLite + ", lighter " + lighter + ", back " + back + ", notes " + onNote + " / " + offNote);
                checks.Add("PASS Lite mode: on, it saves that Watch my screen, game comments (Chatty), Clip my games and Save big moments were on and turns each off, rests her drawing like Power saver even with Power saver Off, and hands HD voice to her free voice; off, it puts each back as it was, Chatty included, and both save");

                preferences.Watch = false;
                SetCommentPace(Commentary.Off);
                preferences.ClipGames = preferences.ClipMoments = false;
                Pick standIn = Engine.Picks[2];
                ProcessStartInfo launch = engine.StartInfo(standIn, false, false, 1);
                string model = Regex.Match(launch.Arguments, "-m \"([^\"]+)\"").Groups[1].Value;
                foreach (string file in new[] { launch.FileName, model })
                {
                    if (File.Exists(file)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(file));
                    File.WriteAllText(file, "");
                    made.Add(file);
                }
                int spawned = 0;
                Engine.Spawn = delegate(ProcessStartInfo info)
                {
                    spawned++;
                    System.Net.Sockets.TcpListener health;
                    FakeServer(new[] { "{\"status\":\"ok\"}", "{\"status\":\"ok\"}", "{\"status\":\"ok\"}" }, new List<string>(), out health, port: int.Parse(Regex.Match(info.Arguments, @"--port (\d+)").Groups[1].Value));
                    lock (fakes) fakes.Add(health);
                    return Process.Start(new ProcessStartInfo("ping.exe", "-n 120 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true });
                };
                brain.Local = engine;
                brain.LocalPick = standIn;
                brain.Model = standIn.Name;
                mainframe.ShowProblem("Running on this PC");
                engine.Ensure(standIn);
                int id = engine.ServerId;
                if (!engine.Running || id == 0) throw new Exception("The stand-in brain didn't start.");
                SystemUse use = new SystemUse();
                bool counted = use.Sample(id, false).Capri.Exists(row => row.Name == "Her brain");
                Func<bool> stops = delegate
                {
                    Stopwatch waited = Stopwatch.StartNew();
                    while (engine.Running && waited.Elapsed.TotalSeconds < 10) PumpFor(0.1);
                    return !engine.Running;
                };
                string brainNote = SetLite(true, false);
                bool rested = stops();
                bool gone;
                try { using (Process left = Process.GetProcessById(id)) gone = left.HasExited; }
                catch (ArgumentException) { gone = true; }
                bool uncounted = !use.Sample(id, false).Capri.Exists(row => row.Name == "Her brain");
                bool resting = mainframe.Status.EndsWith(" | " + Mainframe.LiteResting, StringComparison.Ordinal);
                string refused = null;
                try { new Brain { Model = standIn.Name, Local = engine, LocalPick = standIn }.Aside("Chat", "Hello", 90); }
                catch (BrainException error) { refused = error.Message; }
                new Brain { Model = standIn.Name, Local = engine, LocalPick = standIn }.Warm();
                bool stayedOff = !engine.Running && spawned == 1;
                SetLite(false, false);
                bool awake = mainframe.Status.EndsWith(" | Running on this PC", StringComparison.Ordinal);
                engine.Ensure(standIn);
                bool started = engine.Running && spawned == 2;
                bool keptAsking;
                engine.Begin();
                try
                {
                    SetLite(true, false);
                    PumpFor(0.5);
                    keptAsking = engine.Running;
                }
                finally { engine.End(); }
                RestForLite();
                bool restedAfter = stops();
                SetLite(false, false);
                if (!counted || !rested || !gone || !uncounted || !resting || refused != Brain.LiteLine || !stayedOff || !awake || !started || !keptAsking || !restedAfter
                    || brainNote == null || !brainNote.StartsWith("Your brain on their PC is off now", StringComparison.Ordinal) || Brain.Brief(Brain.LiteLine) != Brain.LiteNote)
                    throw new Exception("Lite mode didn't rest her built-in brain: counted " + counted + ", stopped " + rested + ", process gone " + gone + ", uncounted " + uncounted + ", note " + resting + ", refused " + refused + ", stayed off " + stayedOff + " (" + spawned + " starts), awake note " + awake + ", started again " + started + ", kept mid-ask " + keptAsking + ", rested after " + restedAfter + ", tool note " + brainNote);
                checks.Add("PASS Lite mode and her built-in brain: turning it on stops a running llama-server (a stand-in here), its process leaves Capri's resources and BRAIN reads \"" + Mainframe.LiteResting + "\"; while it's on her brain refuses with her Lite mode line and Warm starts nothing; it never stops her brain in the middle of an ask, only once the ask is done; turned off, the note says Running on this PC again and her brain starts when asked");

                brain.Local = null;
                brain.LocalPick = null;
                brain.Model = "lite-stand-in";
                int start;
                lock (asked) start = asked.Count;
                new Brain { Model = "lite-stand-in" }.Warm();
                bool tracked = Array.IndexOf(Brain.OllamaLoaded, "lite-stand-in") >= 0;
                SetLite(true, false);
                Stopwatch unloading = Stopwatch.StartNew();
                while (unloading.Elapsed.TotalSeconds < 10)
                {
                    lock (asked) if (asked.Count >= start + 2) break;
                    PumpFor(0.1);
                }
                string unload;
                lock (asked) unload = asked.Count >= start + 2 ? asked[start + 1] : "";
                bool unloaded = unload.Contains("\"keep_alive\":\"0\"") && unload.Contains("\"lite-stand-in\"") && Brain.OllamaLoaded.Length == 0;
                string ollamaRefused = null;
                try { new Brain { Model = "lite-stand-in" }.Aside("Chat", "Hello", 90); }
                catch (BrainException error) { ollamaRefused = error.Message; }
                new Brain { Model = "lite-stand-in" }.Warm();
                PumpFor(0.3);
                int after;
                lock (asked) after = asked.Count;
                SetLite(false, false);
                if (!tracked || !unloaded || ollamaRefused != Brain.LiteLine || after != start + 2)
                    throw new Exception("Lite mode didn't let go of Ollama: tracked " + tracked + ", unload " + unload.Replace("\r\n", " ") + ", refused " + ollamaRefused + ", " + (after - start) + " requests");
                checks.Add("PASS Lite mode and Ollama: a model she warmed is unloaded with keep_alive 0 as Lite mode turns on, and while it's on she sends Ollama nothing and answers with her Lite mode line");

                string look = null, rows = "", reach = "";
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Show();
                Application.DoEvents();
                Func<string, string> fits = delegate(string name)
                {
                    mainframe.ShowSettings(true);
                    string[] parts = mainframe.PageReach.Split('|');
                    reach += name + " " + mainframe.PageReach + "; ";
                    rows += name + " " + mainframe.LiteRow + " / " + mainframe.SaverRows + "; ";
                    string problem = mainframe.Overlap() ?? mainframe.Outside() ?? mainframe.LiteFits();
                    if (problem == null && Math.Max(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim())) > int.Parse(parts[2].Trim())) problem = "the page runs past its height";
                    return problem ?? mainframe.LiteLook(Path.Combine(output, "mainframe-lite-" + name.ToLowerInvariant() + ".png"));
                };
                InSimpleSettings(delegate { look = fits("Simple"); });
                preferences.AdvancedSettings = true;
                look = look ?? fits("Advanced");
                if (look != null || rows != "Simple shown | shown / shown | folded; Advanced shown | shown / shown | shown; ")
                    throw new Exception("Lite mode's row didn't fit Settings: " + look + " / " + rows + " / " + reach);
                checks.Add("PASS Lite mode's row: Lite mode and Why slow? sit under Music app in HABITS in Simple and Advanced Settings, with In full screen still folded in Simple, nothing overlapping, cut or past the page (" + reach.TrimEnd(' ', ';') + "); pictures in mainframe-lite-simple.png and mainframe-lite-advanced.png");

                brain.Model = null;
                mainframe.FlipLite();
                bool switchedOn = preferences.Lite && mainframe.LiteShown && LoadPreferences().Lite && heard == "lite mode" && reply == LiteSaid(true, false, new List<string>());
                CloseTalk();
                brain.Model = "lite-stand-in";
                mainframe.FlipLite();
                bool switchedOff = !preferences.Lite && !mainframe.LiteShown && reply == LiteSaid(false, true, new List<string>());
                CloseTalk();
                mainframe.FlipLite();
                bool offered = preferences.Lite && reply == LiteOffer && liteOffer >= 0 && mainframe.Status.EndsWith(" | " + Mainframe.LiteResting, StringComparison.Ordinal);
                CloseTalk();
                string yes = LiteAnswer("yes"), again = LiteAnswer("yes");
                bool opened = yes == FreeKeyLine && again == null && pages.Count == 1 && pages[0] == FreeKeyPage;
                mainframe.FlipLite();
                CloseTalk();
                liteOffer = clock.Elapsed.TotalSeconds;
                string no = LiteAnswer("no thanks");
                liteOffer = clock.Elapsed.TotalSeconds;
                string other = LiteAnswer("what's the weather like?");
                bool dropped = liteOffer < 0;
                liteOffer = clock.Elapsed.TotalSeconds - LiteOfferWait - 1;
                string stale = LiteAnswer("yes");
                if (!switchedOn || !switchedOff || !offered || !opened || no != LiteLater || other != null || !dropped || stale != null || pages.Count != 1 || preferences.Lite)
                    throw new Exception("Lite mode's switch or offer went wrong: on " + switchedOn + ", off " + switchedOff + ", offered " + offered + ", opened " + opened + " (" + string.Join(" ", pages.ToArray()) + "), no " + no + ", other " + other + ", stale " + stale);
                checks.Add("PASS Lite mode's switch: flipping it saves and she says so; with her brain on this PC she says it's off and asks to open the free key page, a yes within two minutes opens NVIDIA's key page once with her usual free key steps, no gets her later line, and anything else or a late yes drops the offer");

                brain.Model = null;
                string toolOn = Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "lite_mode" });
                bool toolSet = preferences.Lite && mainframe.LiteShown;
                string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list", Name = "lite_mode" });
                string toolOff = Hands.Do("capri_settings", new ToolArguments { Action = "off", Name = "lite_mode" });
                if (toolOn != "Turned Lite mode on." || !toolSet || listed != "How my settings are right now: Lite mode on." || toolOff != "Turned Lite mode off." || preferences.Lite || !Hands.Valid("capri_settings", new ToolArguments { Action = "on", Name = "lite_mode" }))
                    throw new Exception("Her settings tool didn't turn Lite mode on and off: " + toolOn + " / " + listed + " / " + toolOff);
                checks.Add("PASS Lite mode in her settings tool: lite_mode turns on and off and lists, the same as HABITS' switch");
            }
            finally
            {
                liteOffer = -1;
                Engine.Spawn = spawnWas;
                engine.Stop();
                foreach (string file in made)
                {
                    try { File.Delete(file); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
                lock (fakes) foreach (System.Net.Sockets.TcpListener fake in fakes) fake.Stop();
                Brain.Ollama = ollamaWas;
                Brain.GpuLimit = limitWas;
                brain.Remote = remoteWas;
                brain.Local = localWas;
                brain.LocalPick = pickWas;
                brain.Model = modelWas;
                brain.SetServer(addressWas, keyWas);
                preferences.Lite = Brain.Lite = liteWas;
                preferences.LiteKept = keptWas;
                preferences.Watch = watchWas;
                preferences.ClipGames = clipsWas;
                preferences.ClipMoments = momentsWas;
                preferences.CommentPace = paceWas;
                preferences.NoComments = noCommentsWas;
                preferences.CommentPaceWas = paceBackWas;
                Commentary.Pace = GamePace;
                preferences.PowerSaver = saverWas;
                preferences.AdvancedSettings = advancedWas;
                simpleTest = simpleWas;
                Browse = browseWas;
                ++asking;
                CloseTalk();
                preferences.Silent = silentWas;
                heard = heardWas;
                mainframe.ShowSettings(pageWas);
                if (!shownWas) mainframe.Hide();
                mainframe.WindowState = stateWas;
                int split = statusWas.IndexOf(" | ", StringComparison.Ordinal);
                if (split >= 0) mainframe.ShowProblem(statusWas.Substring(split + 3));
                SavePreferences();
                mainframe.Sync();
            }
        }

        private static SlowSigns HealthySigns()
        {
            return new SlowSigns { Kind = "built-in", Name = "Gemma 4 E4B", Read = true, Running = true, BrainId = 1, Threads = 16, Processor = "AMD Ryzen 7 5800X", Model = 4590807392, InVram = 5400L << 20, CardUsed = 6L << 30, CardTotal = 12L << 30, RamFree = 9L << 30, RamTotal = 32L << 30, Busy = 12 };
        }

        private void SmokeWhySlow(List<string> checks)
        {
            Action<SlowSigns> nothing = delegate { };
            List<KeyValuePair<string, Action<SlowSigns>>> cases = new List<KeyValuePair<string, Action<SlowSigns>>>
            {
                new KeyValuePair<string, Action<SlowSigns>>("I checked your PC and nothing is slowing me down right now.", nothing),
                new KeyValuePair<string, Action<SlowSigns>>("Brain VRAM is set to RAM only, so my brain runs on your processor. Set Brain VRAM to Auto under Resources in Capri Mainframe.", delegate(SlowSigns s) { s.RamOnly = true; }),
                new KeyValuePair<string, Action<SlowSigns>>("My brain runs on your processor since your graphics card couldn't load it before. Press Free key under BRAIN in Capri Mainframe for a free cloud brain. I'll try the card again after my next update.", delegate(SlowSigns s) { s.CardFailed = true; }),
                new KeyValuePair<string, Action<SlowSigns>>("Your graphics card has only 512 MB of its own memory, so my brain runs on your processor. Press Free key under BRAIN in Capri Mainframe for a free cloud brain.", delegate(SlowSigns s) { s.CardTotal = 512L << 20; s.CardUsed = 100L << 20; }),
                new KeyValuePair<string, Action<SlowSigns>>("Only about 46% of my brain fits in your graphics card's memory, so the rest runs slowly on your processor. Raise Brain VRAM under Resources in Capri Mainframe, or pick a smaller brain.", delegate(SlowSigns s) { s.InVram = 2000L << 20; s.Limited = true; }),
                new KeyValuePair<string, Action<SlowSigns>>("Almost none of my brain fits in your graphics card's memory, so it runs slowly on your processor. Close games or apps using your graphics card, or pick a smaller brain under BRAIN.", delegate(SlowSigns s) { s.Kind = "ollama"; s.Model = 4000000000; s.InVram = 0; }),
                new KeyValuePair<string, Action<SlowSigns>>("Your graphics card's memory is nearly full, 11.4 of 12 GB used. Close a game or app you're not using, or press Free key under BRAIN for a free cloud brain.", delegate(SlowSigns s) { s.CardUsed = (long)(11.4 * (1L << 30)); }),
                new KeyValuePair<string, Action<SlowSigns>>("My brain on your PC rests after 5 minutes unused, so my next reply waits while it loads again. Only that first reply is slow. Press Free key under BRAIN in Capri Mainframe for a free cloud brain.", delegate(SlowSigns s) { s.Running = false; s.InVram = -1; }),
                new KeyValuePair<string, Action<SlowSigns>>("Ollama let go of my brain after a while unused, so my next reply waits while it loads again. Only that first reply is slow. Press Free key under BRAIN in Capri Mainframe for a free cloud brain.", delegate(SlowSigns s) { s.Kind = "ollama"; s.Running = false; s.Model = -1; s.InVram = -1; }),
                new KeyValuePair<string, Action<SlowSigns>>("I checked your PC and nothing is slowing me down right now.", delegate(SlowSigns s) { s.Kind = "ollama"; s.Read = false; s.Running = false; s.Model = -1; s.InVram = -1; }),
                new KeyValuePair<string, Action<SlowSigns>>("Your processor, Intel Core i3-2100, has only 4 threads, which is small for running a brain. Press Free key under BRAIN in Capri Mainframe for a free cloud brain. Lite mode helps too.", delegate(SlowSigns s) { s.Threads = 4; s.Processor = "Intel Core i3-2100"; }),
                new KeyValuePair<string, Action<SlowSigns>>("Only 900 MB of your 8.0 GB of RAM is free. Close apps or browser tabs you're not using, or turn on Lite mode.", delegate(SlowSigns s) { s.RamFree = 900L << 20; s.RamTotal = 8L << 30; }),
                new KeyValuePair<string, Action<SlowSigns>>("Your processor is 96% busy, mostly with chrome. Close chrome if you're done with it.", delegate(SlowSigns s) { s.Busy = 96.2; s.Busiest = "chrome"; s.BusiestCpu = 61; }),
                new KeyValuePair<string, Action<SlowSigns>>("Watch my screen and Clip my games are on, and they keep working in the background. Turn on Lite mode to pause them, or turn off the ones you don't need.", delegate(SlowSigns s) { s.Watchers.Add("Watch my screen"); s.Watchers.Add("Clip my games"); }),
                new KeyValuePair<string, Action<SlowSigns>>("HD voice is loaded too, and it shares your PC's memory and graphics card with me. Turn on Lite mode so my free voice takes over.", delegate(SlowSigns s) { s.HdAwake = true; }),
                new KeyValuePair<string, Action<SlowSigns>>("Lite mode is on, so my brain on your PC is switched off. Press Free key under BRAIN in Capri Mainframe for a free cloud brain, or turn Lite mode off.", delegate(SlowSigns s) { s.Lite = true; s.RamOnly = true; s.Running = false; s.HdAwake = true; }),
                new KeyValuePair<string, Action<SlowSigns>>("Nothing on your PC is slowing me down right now. My brain is in the cloud, so slow replies come from the service being busy. Picking another model under BRAIN can help.", delegate(SlowSigns s) { s.Kind = "cloud"; s.RamOnly = true; s.Running = false; s.InVram = 0; }),
                new KeyValuePair<string, Action<SlowSigns>>("Your processor has only 2 threads, which is small for everything I do. Turn on Lite mode so I draw lighter.", delegate(SlowSigns s) { s.Kind = "cloud"; s.Threads = 2; s.Processor = null; }),
                new KeyValuePair<string, Action<SlowSigns>>("Brain VRAM is set to RAM only, so my brain runs on your processor. Set Brain VRAM to Auto under Resources in Capri Mainframe. Only 900 MB of your 16 GB of RAM is free. Close apps or browser tabs you're not using, or turn on Lite mode.", delegate(SlowSigns s) { s.RamOnly = true; s.RamFree = 900L << 20; s.RamTotal = 16L << 30; s.Busy = 97; s.HdAwake = true; })
            };
            List<string> wrong = new List<string>();
            foreach (KeyValuePair<string, Action<SlowSigns>> one in cases)
            {
                SlowSigns signs = HealthySigns();
                one.Value(signs);
                string said = SlowLine(signs);
                if (said != one.Key) wrong.Add(said + " (wanted: " + one.Key + ")");
            }
            if (wrong.Count > 0) throw new Exception("Why slow said the wrong thing: " + string.Join(" | ", wrong.ToArray()));
            checks.Add("PASS why slow reasons: from faked signs she names her brain on the processor (RAM only, a failed card, a small card), part of her brain not fitting in graphics card memory, a nearly full card, her brain resting, few processor threads, little free RAM, a busy processor and its busiest app, watchers on and HD voice loaded, each with its fix, Lite mode first when it's on, only the first two out loud, and nothing when nothing's wrong");

            string[] asks = { "why are you so slow?", "Why are you slow", "capri why are you so laggy?", "Hey Capri, why are you lagging today?", "why do you take so long to reply?", "why is your brain so slow", "what\u2019s making you so slow?", "why so slow?", "why are you so slow on my pc?", "why are you taking so long", "Why r u so slow" };
            string[] notAsks = { null, "", "why is my pc slow?", "why are you so slow at chess?", "my game is slow", "talk slower", "can you be slower", "why is my internet slow", "you're slow", "why are you so slow to reply to my brother on discord" };
            foreach (string ask in asks) if (!AsksWhySlow(ask)) throw new Exception("Why slow didn't hear: " + ask);
            foreach (string ask in notAsks) if (AsksWhySlow(ask)) throw new Exception("Why slow heard something else: " + ask);
            checks.Add("PASS why slow asks: why are you so slow, laggy or taking so long, with or without her name, start her check; why is my PC slow, games, chess and her voice speed don't");

            string ollamaWas = Brain.Ollama, modelWas = brain.Model, addressWas, keyWas;
            brain.GetServer(out addressWas, out keyWas);
            Cloud remoteWas = brain.Remote;
            Engine localWas = brain.Local;
            Pick pickWas = brain.LocalPick;
            bool noAiWas = preferences.NoAi, offWas = Brain.Off;
            System.Net.Sockets.TcpListener fake = null;
            SlowSigns spill, down;
            try
            {
                List<string> asked = new List<string>();
                const string places = "{\"models\":[{\"name\":\"lite-stand-in:latest\",\"model\":\"lite-stand-in:latest\",\"size\":4000000000,\"digest\":\"x\",\"expires_at\":\"2026-11-21T03:00:00Z\",\"size_vram\":1000000000}]}";
                Brain.Ollama = FakeServer(new[] { places }, asked, out fake);
                preferences.NoAi = Brain.Off = false;
                brain.Remote = null;
                brain.SetServer(null, null);
                brain.Local = null;
                brain.LocalPick = null;
                brain.Model = "lite-stand-in";
                spill = ReadSlowSigns();
                fake.Stop();
                fake = null;
                Brain.Ollama = "http://127.0.0.1:1";
                down = ReadSlowSigns();
                if (asked.Count != 1 || !asked[0].StartsWith("GET /api/ps", StringComparison.Ordinal)) throw new Exception("Why slow didn't ask Ollama what's loaded: " + string.Join(" | ", asked.ToArray()));
            }
            finally
            {
                if (fake != null) fake.Stop();
                Brain.Ollama = ollamaWas;
                preferences.NoAi = noAiWas;
                Brain.Off = offWas;
                brain.Remote = remoteWas;
                brain.SetServer(addressWas, keyWas);
                brain.Local = localWas;
                brain.LocalPick = pickWas;
                brain.Model = modelWas;
            }
            List<string[]> spilled = SlowReasons(spill);
            if (spill.Kind != "ollama" || !spill.Read || !spill.Running || spill.Model != 4000000000 || spill.InVram != 1000000000 || spilled.Count == 0 || spilled[0][0] != "Only about 25% of my brain fits in your graphics card's memory, so the rest runs slowly on your processor."
                || down.Kind != "ollama" || down.Read || down.Running || SlowReasons(down).Exists(reason => reason[0].StartsWith("Ollama", StringComparison.Ordinal)))
                throw new Exception("Why slow read Ollama wrong: " + spill.Kind + " " + spill.Read + " " + spill.Running + " " + spill.Model + " " + spill.InVram + " / " + down.Read + " " + down.Running);
            checks.Add("PASS why slow reads Ollama: /api/ps says how much of her model sits in graphics card memory (25% here), and with Ollama not answering she doesn't guess it let go of her brain");

            SlowSigns here = ReadSlowSigns();
            List<string[]> found = SlowReasons(here);
            if (here.RamTotal <= 0 || here.RamFree < 0 || here.RamFree > here.RamTotal || here.Threads != Environment.ProcessorCount || here.Busy > 100)
                throw new Exception("Why slow couldn't read this PC: " + here.RamFree + " of " + here.RamTotal + ", " + here.Threads + " threads, CPU " + here.Busy);
            checks.Add(string.Format(CultureInfo.InvariantCulture, "PASS why slow on this PC: {0} threads{1}, {2} of {3} RAM free, CPU {4}, graphics card {5}{6}, her brain {7}; found {8}",
                here.Threads, here.Processor == null ? "" : " (" + here.Processor + ")", UseView.Bytes(here.RamFree), UseView.Bytes(here.RamTotal), UseView.Percent(here.Busy), here.CardTotal > 0 ? UseView.Bytes(here.CardTotal) : "unknown",
                here.CardUsed >= 0 ? " with " + UseView.Bytes(here.CardUsed) + " in use" : "", here.Kind, found.Count == 0 ? "nothing slowing her" : string.Join(" ", found.ConvertAll(reason => reason[0]).ToArray())));

            ToolArguments why = new ToolArguments { Action = "why_slow" };
            string told = Hands.Do("capri_settings", why);
            Hands.Kit kit = Hands.KitFor();
            int settingsAt = -1;
            for (int i = 0; i < Hands.Count; i++) if (Hands.Name(i) == "capri_settings") settingsAt = i;
            string described = settingsAt < 0 ? "" : Hands.Description(settingsAt);
            if (told == null || !told.StartsWith("From a check of their PC just now: ", StringComparison.Ordinal) || !told.EndsWith(" Lite mode is " + (preferences.Lite ? "on." : "off."), StringComparison.Ordinal)
                || !Hands.Valid("capri_settings", why) || !Hands.Checking("capri_settings", why) || Security.Needs("capri_settings", why) != -1
                || described.Length > 1024 || described.IndexOf("why_slow", StringComparison.Ordinal) < 0 || kit.Brief.Length > 3800 || kit.Brief.IndexOf("\n- capri_settings: ", StringComparison.Ordinal) < 0)
                throw new Exception("Why slow wasn't in her settings tool: " + told + " / description " + described.Length + " / brief " + kit.Brief.Length);
            checks.Add("PASS why slow is her settings tool's why_slow action: it reads only, needs no SECURITY toggle, and returns what it found with whether Lite mode is on; her chat brain's short tool list is " + kit.Brief.Length + " characters (cap 3800) and the settings tool's description " + described.Length + " (cap 1024)");

            string liteHelp = SelfKnow.Matching("What is the Lite mode setting in Capri?") ?? "", slowHelp = SelfKnow.Matching("what does the Why slow? button do?") ?? "", plainLite = SelfKnow.Matching("what is lite mode?") ?? "";
            if (!liteHelp.StartsWith("Lite mode: Mainframe > Settings > HABITS > Lite mode", StringComparison.Ordinal) || !liteHelp.Contains("still use memory") || !plainLite.StartsWith("Lite mode:", StringComparison.Ordinal)
                || !slowHelp.StartsWith("Why slow?: Mainframe > Settings > HABITS > Why slow?", StringComparison.Ordinal))
                throw new Exception("Her help didn't explain Lite mode or Why slow?: " + liteHelp + " / " + slowHelp + " / " + plainLite);
            checks.Add("PASS her help explains Lite mode, including what still uses memory, and Why slow?");

            bool silentWas = preferences.Silent;
            string heardWas = heard;
            try
            {
                preferences.Silent = true;
                heard = null;
                CheckWhySlow();
                Stopwatch waited = Stopwatch.StartNew();
                while (heard != "why slow?" && waited.Elapsed.TotalSeconds < 15) PumpFor(0.1);
                if (heard != "why slow?" || string.IsNullOrEmpty(reply) || reply == SlowUnread) throw new Exception("Why slow? didn't say what it found: " + heard + " / " + reply);
                checks.Add("PASS Why slow? in HABITS checks this PC on a worker and she says what she found: " + reply);
            }
            finally
            {
                ++asking;
                CloseTalk();
                preferences.Silent = silentWas;
                heard = heardWas;
            }
        }
    }
}
