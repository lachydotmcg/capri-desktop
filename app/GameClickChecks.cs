using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A pretend full-screen game for her smoke test, in a process of its own like a real one. It hides its cursor or sets none,
    // counts the clicks that reach it and answers the test over window messages.
    internal sealed class FakeGame : Form
    {
        internal const int SetCursorMode = 0x8001, Lefts = 0x8002, Rights = 0x8003, TakeFront = 0x8004, CursorMode = 0x8005, LetFront = 0x8006;
        internal const string Title = "Capri check game";
        [StructLayout(LayoutKind.Sequential)] private struct MouseInput { internal int X, Y, Data, Flags, Time; internal IntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] private struct Input { internal int Type; internal MouseInput Mouse; }
        [DllImport("user32.dll")] private static extern int ShowCursor(bool show);
        [DllImport("user32.dll")] private static extern IntPtr SetCursor(IntPtr cursor);
        [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] private static extern int SendInput(int count, Input[] inputs, int size);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr window, ref Native.Point point);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, System.Text.StringBuilder name, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, System.Text.StringBuilder text, int size);
        private readonly string ready;
        private readonly Process parent;
        private readonly System.Windows.Forms.Timer watch = new System.Windows.Forms.Timer { Interval = 250 };
        private int mode, hides, lefts, rights;

        private FakeGame(string ready, Process parent, Rectangle bounds)
        {
            this.ready = ready;
            this.parent = parent;
            Text = Title;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            BackColor = Color.FromArgb(18, 20, 28);
            watch.Tick += delegate { if (this.parent.HasExited) Close(); };
        }

        // --smoke-child game <ready file> <her process> <x> <y> <width> <height>
        internal static int Run(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            int[] numbers = new int[5];
            if (args.Length != 8) return 2;
            for (int i = 0; i < numbers.Length; i++)
                if (!int.TryParse(args[3 + i], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[i])) return 2;
            try
            {
                using (Process parent = Process.GetProcessById(numbers[0]))
                using (FakeGame game = new FakeGame(args[2], parent, new Rectangle(numbers[1], numbers[2], numbers[3], numbers[4])))
                    Application.Run(game);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            watch.Start();
            File.WriteAllText(ready + ".new", Handle.ToInt64().ToString(CultureInfo.InvariantCulture));
            File.Move(ready + ".new", ready);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) watch.Dispose();
            base.Dispose(disposing);
        }

        // 0 shows the cursor, 1 hides it the way most games do, and 2 sets no cursor at all.
        private void PickCursor(int wanted)
        {
            for (; hides > 0; hides--) ShowCursor(true);
            mode = wanted;
            if (mode == 1)
            {
                int level;
                do { level = ShowCursor(false); hides++; } while (level >= 0 && hides < 8);
            }
            SetCursor(mode == 2 ? IntPtr.Zero : Cursors.Default.Handle);
        }

        protected override void WndProc(ref Message message)
        {
            switch (message.Msg)
            {
                case SetCursorMode: PickCursor(message.WParam.ToInt32()); message.Result = new IntPtr(1); return;
                case Lefts: message.Result = new IntPtr(lefts); return;
                case Rights: message.Result = new IntPtr(rights); return;
                case CursorMode: message.Result = new IntPtr(mode); return;
                case TakeFront: Activate(); SetForegroundWindow(Handle); message.Result = new IntPtr(Native.GetForegroundWindow() == Handle ? 1 : 0); return;
                case LetFront: Native.AllowSetForegroundWindow(-1); message.Result = new IntPtr(1); return;
                case 0x0201: case 0x0203: lefts++; break;
                case 0x0204: case 0x0206: rights++; break;
                case 0x0020:
                    if (mode == 2) { SetCursor(IntPtr.Zero); message.Result = new IntPtr(1); return; }
                    break;
            }
            base.WndProc(ref message);
        }

        // The real mouse, which the test only moves and clicks on GitHub's runner.
        internal static void Put(Point at) { SetCursorPos(at.X, at.Y); }

        internal static void Press(int flags)
        {
            Input[] inputs = { new Input { Type = 0, Mouse = new MouseInput { Flags = flags } } };
            SendInput(1, inputs, Marshal.SizeOf(typeof(Input)));
        }

        internal static void Front(IntPtr window) { SetForegroundWindow(window); }

        // The window a click at a spot would go to, by title, class and program, for a check that finds the wrong one there.
        internal static string Describe(Point at)
        {
            IntPtr window = Native.GetAncestor(Native.WindowFromPoint(new Native.Point(at.X, at.Y)), 2);
            if (window == IntPtr.Zero) return "nothing at " + at.X + "," + at.Y;
            System.Text.StringBuilder kind = new System.Text.StringBuilder(128), title = new System.Text.StringBuilder(128);
            GetClassName(window, kind, kind.Capacity);
            GetWindowText(window, title, title.Capacity);
            int process;
            Native.GetWindowThreadProcessId(window, out process);
            return "\"" + title + "\" (" + kind + ", process " + process + ") at " + at.X + "," + at.Y;
        }

        // A click sent straight to the window Windows would hand a real one at that spot, for a desktop with no mouse of its own.
        internal static void SendClick(Point at, bool right)
        {
            IntPtr window = Native.WindowFromPoint(new Native.Point(at.X, at.Y));
            if (window == IntPtr.Zero) return;
            Native.Point inside = new Native.Point(at.X, at.Y);
            ScreenToClient(window, ref inside);
            IntPtr spot = new IntPtr((inside.Y << 16) | (inside.X & 0xFFFF));
            Native.SendMessage(window, right ? 0x0204 : 0x0201, new IntPtr(right ? 2 : 1), spot);
            Native.SendMessage(window, right ? 0x0205 : 0x0202, IntPtr.Zero, spot);
        }
    }

    internal sealed partial class Companion
    {
        // A pretend full-screen game hides its cursor with her over it. Every click aimed at her goes to the game, her menu, pill and
        // glitch stay away and the game keeps focus. Once it shows its cursor again, a right click on her opens her menu as before.
        // The hidden runner's desktop has no cursor to read or move, so there each click goes where Windows' own hit test sends it,
        // and only GitHub's runner moves and clicks the real mouse.
        private void SmokeGameClicks(List<string> checks, string output)
        {
            if (hidden || docked || riding || dragging || pressed || walking || falling || onPanel || outro != null || working != null || frame == null || !IsHandleCreated || !NativeTop || preferences.Hologram)
            {
                checks.Add("LIMIT game clicks: her smoke scene is busy");
                return;
            }
            GameClickRun(checks, output, false);
            bool readable = GameCursor.Read().Read;
            if (OnGitHub && Environment.UserInteractive && readable) GameClickRun(checks, output, true);
            else checks.Add("LIMIT game clicks with the real mouse: only GitHub's runner moves and clicks it" + (readable ? "" : ", and this desktop has no cursor to read"));
        }

        // A spot well inside her body and on top of everything, where any click lands on her.
        private Point SolidSpot()
        {
            if (frame == null) return Point.Empty;
            for (int y = frame.Height / 3; y < frame.Height * 4 / 5; y += 3)
                for (int step = 0; step < frame.Width / 3; step += 3)
                    foreach (int x in new[] { frame.Width / 2 - step, frame.Width / 2 + step })
                    {
                        bool solid = true;
                        for (int dx = -6; dx <= 6 && solid; dx += 6)
                            for (int dy = -6; dy <= 6 && solid; dy += 6)
                            {
                                int px = x + dx, py = y + dy;
                                solid = px >= 0 && py >= 0 && px < frame.Width && py < frame.Height && frame.GetPixel(px, py).A >= 250;
                            }
                        Point spot = PointToScreen(new Point(x, y));
                        if (solid && Uncovered(spot) && Uncovered(new Point(spot.X + 2, spot.Y + 2))) return spot;
                    }
            return Point.Empty;
        }

        // Why nothing of hers takes a click, for the line that says so.
        private string Untouchable(IntPtr game)
        {
            bool above = false;
            IntPtr next = Handle;
            for (int i = 0; i < 5000 && next != IntPtr.Zero && !above; i++) { next = Native.GetWindow(next, 2); above = next == game; }
            int style = Native.GetWindowLong(Handle, -16), extended = Native.GetWindowLong(Handle, -20);
            int solid = 0, open = 0;
            if (frame != null)
                for (int y = 0; y < frame.Height; y += 4)
                    for (int x = 0; x < frame.Width; x += 4)
                        if (frame.GetPixel(x, y).A >= 250) { solid++; if (Uncovered(PointToScreen(new Point(x, y)))) open++; }
            return "her window " + (Native.IsWindowVisible(Handle) ? "shown" : "not shown") + (Enabled ? "" : " and disabled") + " at " + Bounds + ", styles 0x" + style.ToString("X8", CultureInfo.InvariantCulture)
                + " and 0x" + extended.ToString("X8", CultureInfo.InvariantCulture) + (above ? ", above" : ", not above") + " the game" + ((Native.GetWindowLong(game, -20) & 8) != 0 ? ", which is topmost" : "")
                + ", " + solid + " solid samples in her picture and " + open + " of them clickable, drawn in " + drawnBounds + ", last sent " + (bodySurface == null ? "nothing" : bodySurface.LastDirty.ToString())
                + ", clip " + (clip == null ? "none" : clip.Title) + ", veiled " + veiled + ", lent " + lentOut + ", pointing " + pointing;
        }

        private void GameClickRun(List<string> checks, string output, bool real)
        {
            Func<IntPtr> foregroundWas = Hands.Foreground;
            Func<Sight> glimpseWas = Computer.Glimpse;
            Func<bool> fullWas = FullScreen;
            Func<GameCursor.Reading> readWas = GameCursor.Read;
            bool? fakedWas = Native.FakedGaming;
            bool patsWas = preferences.NoHeadPats;
            int clicksWas = clicks;
            Clip clipWas = clip;
            bool veiledWas = veiled;
            double veilWas = veilUntil;
            Point cursorWas = real ? Cursor.Position : Point.Empty;
            IntPtr frontWas = real ? Native.GetForegroundWindow() : IntPtr.Zero;
            string ready = Path.Combine(output, "game-clicks-" + (real ? "mouse" : "hit-test") + ".txt");
            Process game = null;
            IntPtr window = IntPtr.Zero;
            bool unfiltered = false;
            try
            {
                Directory.CreateDirectory(output);
                if (File.Exists(ready)) File.Delete(ready);
                preferences.NoHeadPats = true;
                if (pill.Visible) pill.Hide();
                // Stood in her idle with no ribbons still due to form her, so there's all of her to click, whatever the section
                // before left playing.
                ribbons.Stop();
                veiled = false;
                veilUntil = -1;
                Play(idle);
                bodyCells.Clear();
                drawnClip = null;
                if (bodySurface != null) bodySurface.Forget();
                RenderFrame();
                foreach (Form form in new Form[] { this, pill, box, taskPanel, player }) { IntPtr handle = form.Handle; }
                Rectangle area = Screen.FromControl(this).Bounds;
                string arguments = "--smoke-child game " + SmokeRun.Quote(ready);
                foreach (int number in new[] { aimOwn, area.X, area.Y, area.Width, area.Height }) arguments += " " + number.ToString(CultureInfo.InvariantCulture);
                game = Process.Start(new ProcessStartInfo(Application.ExecutablePath, arguments) { UseShellExecute = false });
                Stopwatch opening = Stopwatch.StartNew();
                while (window == IntPtr.Zero && !game.HasExited && opening.Elapsed.TotalSeconds < 20)
                {
                    PumpFor(0.05);
                    long handle;
                    if (File.Exists(ready) && long.TryParse(File.ReadAllText(ready), NumberStyles.Integer, CultureInfo.InvariantCulture, out handle)) window = new IntPtr(handle);
                }
                if (window == IntPtr.Zero) throw new Exception("The pretend game never opened its window" + (game.HasExited ? ", it quit with " + game.ExitCode : "") + ".");
                int gameProcess = game.Id;
                Point away = Point.Empty, spot = Point.Empty, pointer = Point.Empty;
                Func<int, int> ask = delegate(int message) { return Native.SendMessage(window, message, IntPtr.Zero, IntPtr.Zero).ToInt32(); };
                Action<int> cursorMode = delegate(int mode) { Native.SendMessage(window, FakeGame.SetCursorMode, new IntPtr(mode), IntPtr.Zero); };
                Action<Point> move;
                Action<bool> click;
                if (real)
                {
                    Native.FakedGaming = null;
                    FullScreen = Native.FillsScreen;
                    Native.AllowSetForegroundWindow(gameProcess);
                    if (ask(FakeGame.TakeFront) == 0) FakeGame.Front(window);
                    PumpFor(0.3);
                    if (Native.GetForegroundWindow() != window || !Native.FillsScreen())
                    {
                        checks.Add("LIMIT game clicks with the real mouse: the runner couldn't put the pretend game in front and full screen");
                        return;
                    }
                    if (smokeInput != null) { Application.RemoveMessageFilter(smokeInput); unfiltered = true; }
                    WatchGameCursor(true);
                    move = delegate(Point at) { FakeGame.Put(at); PumpFor(0.15); };
                    click = delegate(bool right) { FakeGame.Press(right ? 0x0008 : 0x0002); System.Threading.Thread.Sleep(40); FakeGame.Press(right ? 0x0010 : 0x0004); PumpFor(0.35); };
                }
                else
                {
                    Hands.Foreground = delegate { return window; };
                    Computer.Glimpse = delegate { return new Sight { Window = window, Title = FakeGame.Title, Program = "Capri", Bounds = area }; };
                    FullScreen = delegate { return true; };
                    // Windows draws the cursor of whichever program's window is under it, so over her it's always her own.
                    GameCursor.Read = delegate { return new GameCursor.Reading { Read = true, At = pointer, Hidden = GameCursor.ProcessAt(pointer) == gameProcess && ask(FakeGame.CursorMode) != 0 }; };
                    move = delegate(Point at) { pointer = at; PumpFor(0.05); };
                    click = delegate(bool right) { FakeGame.SendClick(pointer, right); PumpFor(0.1); };
                }
                Func<bool, bool> aims = delegate(bool wanted)
                {
                    Stopwatch waited = Stopwatch.StartNew();
                    while (true)
                    {
                        CheckGameCursor(clock.Elapsed.TotalSeconds);
                        if (gameAim == wanted) return true;
                        if (waited.Elapsed.TotalSeconds > 3) return false;
                        PumpFor(0.05);
                    }
                };
                Func<bool, string> wrong = delegate(bool through)
                {
                    foreach (Form form in new Form[] { this, pill, box, taskPanel, player, gifCard })
                    {
                        if (!form.IsHandleCreated) continue;
                        int style = Native.GetWindowLong(form.Handle, -20);
                        if (((style & 0x20) != 0) != through || (style & 0x08000000) == 0) return form.GetType().Name;
                    }
                    return null;
                };

                Point corner = new Point(area.Left + 60, area.Top + 60);
                foreach (Point place in new[] { corner, new Point(area.Right - 60, area.Top + 60), new Point(area.Left + area.Width / 2, area.Top + 60), new Point(area.Left + 60, area.Top + area.Height / 2), new Point(area.Left + area.Width / 3, area.Top + area.Height / 3) })
                    if (away.IsEmpty && GameCursor.ProcessAt(place) == gameProcess) away = place;
                Stopwatch drawn = Stopwatch.StartNew();
                while ((spot = SolidSpot()).IsEmpty && drawn.Elapsed.TotalSeconds < 2) PumpFor(0.1);
                string how = real ? "with the real mouse" : "by hit test";
                if (away.IsEmpty || spot.IsEmpty)
                {
                    checks.Add("LIMIT game clicks " + how + ": " + (away.IsEmpty ? "the pretend game was covered by " + FakeGame.Describe(corner) : "nothing of hers was on top to click, " + FakeGame.Describe(Hip()) + " (" + Untouchable(window) + ")"));
                    return;
                }

                aimFront = IntPtr.Zero;
                aimLooked = double.NegativeInfinity;
                aimGame = 0;
                aimHidden = false;
                cursorMode(0);
                move(away);
                if (real && GameCursor.Read().Hidden)
                {
                    checks.Add("LIMIT game clicks with the real mouse: this runner shows no cursor even over the pretend game with its cursor showing, " + GameCursor.Read());
                    return;
                }
                CheckGameCursor(clock.Elapsed.TotalSeconds);
                if (GameAt(window, clock.Elapsed.TotalSeconds) != gameProcess || gameAim) throw new Exception("The pretend game wasn't taken for a game in front, or its showing cursor made her let clicks through.");
                pillUntil = -1;
                if (pill.Visible) pill.Hide();
                if (GameCursor.ProcessAt(away) != gameProcess || !Uncovered(spot))
                {
                    checks.Add("LIMIT game clicks " + how + ": " + (Uncovered(spot) ? "the pretend game was covered by " + FakeGame.Describe(away) : "she was covered by " + FakeGame.Describe(spot)));
                    return;
                }

                cursorMode(1);
                bool told = false;
                if (real)
                {
                    Stopwatch heard = Stopwatch.StartNew();
                    while (!gameAim && heard.Elapsed.TotalSeconds < 1.5) PumpFor(0.05);
                    told = gameAim;
                }
                if (!aims(true)) throw new Exception("With a game in front and its cursor hidden, her windows still took clicks: " + GameCursor.Read() + " over " + FakeGame.Describe(away) + ".");
                move(spot);
                for (int i = 0; i < 6; i++) { CheckGameCursor(clock.Elapsed.TotalSeconds); PumpFor(0.05); }
                if (!gameAim) throw new Exception("The game's hidden cursor moving onto her gave her clicks back.");
                if (real && !GameCursor.Read().Hidden) throw new Exception("Over her, the cursor wasn't the game's hidden one: " + GameCursor.Read() + ".");
                PumpFor(0.3);
                if (real && pill.Visible) throw new Exception("The game's hidden cursor over her brought up her pill.");
                int lefts = ask(FakeGame.Lefts), rights = ask(FakeGame.Rights);
                click(false);
                click(true);
                int leftGot = ask(FakeGame.Lefts) - lefts, rightGot = ask(FakeGame.Rights) - rights;
                if (leftGot != 1 || rightGot != 1) throw new Exception("Clicks on her with the game's cursor hidden didn't reach the game: " + leftGot + " left and " + rightGot + " right.");
                if (menu.Visible || typer.Visible || pressed || pendingClick || clicks != clicksWas) throw new Exception("A click on her with the game's cursor hidden still reached her: menu " + menu.Visible + ", typer " + typer.Visible + ", held " + pressed + ", glitch due " + (pendingClick || clicks != clicksWas));
                if (real && Native.GetForegroundWindow() != window) throw new Exception("A click on her took the game's focus.");
                string open = wrong(true);
                if (open != null) throw new Exception("Her " + open + " didn't let clicks through without taking focus.");
                Lend(true);
                Lend(false);
                if (!gameAim || wrong(true) != null) throw new Exception("Her hands handing the cursor back made her clickable over the aiming game.");

                cursorMode(0);
                if (!aims(false)) throw new Exception("The game showing its cursor again didn't give her clicks back: " + GameCursor.Read() + ".");
                move(away);
                cursorMode(2);
                if (!aims(true)) throw new Exception("A game that set no cursor at all left her clickable: " + GameCursor.Read() + " over " + FakeGame.Describe(away) + ".");
                move(spot);
                lefts = ask(FakeGame.Lefts);
                click(false);
                if (ask(FakeGame.Lefts) != lefts + 1 || pressed || pendingClick) throw new Exception("With the game setting no cursor, a click on her didn't reach the game.");

                cursorMode(0);
                if (!aims(false)) throw new Exception("The game showing its cursor didn't give her clicks back: " + GameCursor.Read() + ".");
                string shut = wrong(false);
                if (shut != null) throw new Exception("Her " + shut + " still let clicks through with the game's cursor showing.");
                Point back = new Point(spot.X + 2, spot.Y + 2);
                if (!Uncovered(back)) back = SolidSpot();
                if (back.IsEmpty)
                {
                    checks.Add("LIMIT game clicks " + how + ": by the last click something covered her, " + FakeGame.Describe(new Point(spot.X + 2, spot.Y + 2)));
                    return;
                }
                move(back);
                string target = FakeGame.Describe(back);
                rights = ask(FakeGame.Rights);
                // Her menu counts as opened once it's up, even if it shuts by itself straight after.
                bool asked = false, opened = false;
                string shutBy = null;
                System.ComponentModel.CancelEventHandler asking = delegate { asked = true; };
                EventHandler up = delegate { opened = true; };
                ToolStripDropDownClosedEventHandler down = delegate(object sender, ToolStripDropDownClosedEventArgs e) { if (shutBy == null) shutBy = e.CloseReason.ToString(); };
                menu.Opening += asking;
                menu.Opened += up;
                menu.Closed += down;
                bool stayed;
                try
                {
                    click(true);
                    PumpFor(0.2);
                    stayed = menu.Visible;
                    if (stayed) menu.Close();
                    PumpFor(0.1);
                }
                finally
                {
                    menu.Opening -= asking;
                    menu.Opened -= up;
                    menu.Closed -= down;
                }
                rightGot = ask(FakeGame.Rights) - rights;
                if (!opened || rightGot != 0) throw new Exception("With the game's cursor showing, a right click on her didn't open her menu: " + (asked ? "it began opening" : "it never began opening") + ", shut by " + (shutBy ?? "nothing")
                    + ", game right clicks " + rightGot + ", the click went to " + target + ", her window 0x" + Handle.ToInt64().ToString("X") + ", in front 0x" + Native.GetForegroundWindow().ToInt64().ToString("X") + ".");
                if (!stayed) checks.Add("LIMIT game clicks " + how + ": her menu opened for the right click but shut by itself (" + shutBy + ") before it could be looked at");
                checks.Add("PASS a pretend full-screen game that hides its cursor, or sets none, gets every click aimed at her " + (real ? "with the real mouse" : "by Windows' hit test")
                    + ": her menu, pill and glitch stay away, " + (real ? "the game keeps focus" : "her windows can't take focus") + " and her hands' cursor loans don't undo it; once the game shows its cursor, a right click on her opens her menu again");
                if (real && !told) checks.Add("LIMIT game clicks: Windows' cursor events didn't arrive here, so only her quarter-second check switched her");
            }
            finally
            {
                if (menu.Visible) menu.Close();
                if (real) WatchGameCursor(false);
                if (unfiltered) Application.AddMessageFilter(smokeInput);
                Hands.Foreground = foregroundWas;
                Computer.Glimpse = glimpseWas;
                FullScreen = fullWas;
                GameCursor.Read = readWas;
                Native.FakedGaming = fakedWas;
                preferences.NoHeadPats = patsWas;
                aimFront = IntPtr.Zero;
                aimGame = 0;
                aimHidden = false;
                aimLooked = double.NegativeInfinity;
                Aim(false);
                pendingClick = false;
                clickDelay.Stop();
                veiled = veiledWas;
                veilUntil = veilWas;
                if (clip != clipWas) Play(clipWas);
                RenderFrame();
                if (real && window != IntPtr.Zero)
                {
                    Native.SendMessage(window, FakeGame.LetFront, IntPtr.Zero, IntPtr.Zero);
                    if (frontWas != IntPtr.Zero) FakeGame.Front(frontWas);
                }
                if (game != null)
                {
                    try
                    {
                        if (!game.HasExited && window != IntPtr.Zero) Native.PostMessage(window, 0x0010, IntPtr.Zero, IntPtr.Zero);
                        if (!game.WaitForExit(3000)) game.Kill();
                    }
                    catch (InvalidOperationException) { }
                    catch (System.ComponentModel.Win32Exception) { }
                    game.Dispose();
                }
                if (real) FakeGame.Put(cursorWas);
                try { if (File.Exists(ready)) File.Delete(ready); } catch (IOException) { }
            }
        }
    }
}
