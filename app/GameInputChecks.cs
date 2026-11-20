using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private sealed class KeyCatcher : NativeWindow, IDisposable
        {
            internal readonly List<int[]> Got = new List<int[]>();

            internal KeyCatcher() { CreateHandle(new CreateParams { Parent = new IntPtr(-3) }); }

            protected override void WndProc(ref Message message)
            {
                int kind = message.Msg;
                if (kind == 0x100 || kind == 0x101 || kind == 0x104 || kind == 0x105)
                {
                    long bits = message.LParam.ToInt64();
                    Got.Add(new[] { (int)message.WParam.ToInt64(), (int)((bits >> 16) & 0xFF), (int)((bits >> 24) & 1), kind == 0x101 || kind == 0x105 ? 1 : 0 });
                    message.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref message);
            }

            public void Dispose() { DestroyHandle(); }
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")] private static extern IntPtr KeyMessage(IntPtr window, int message, IntPtr code, IntPtr bits);

        private static void SmokeScanKeys(List<string> checks)
        {
            Action<short, bool, bool> keyWas = Computer.SendKey;
            Func<IntPtr, bool> aboveWas = Computer.AboveHer;
            Func<IntPtr> foregroundWas = Hands.Foreground;
            Func<bool> escapeWas = Computer.Escape;
            Action haltWas = Computer.Halted;
            bool flinchedWas = Computer.Flinched;
            int soundWas = Sfx.Level;
            List<int> flagged = new List<int>();
            KeyCatcher game = new KeyCatcher();
            try
            {
                Computer.AboveHer = delegate { return false; };
                Hands.Foreground = delegate { return game.Handle; };
                Computer.Escape = delegate { return false; };
                Computer.Halted = delegate { };
                Computer.Flinched = false;
                Sfx.Level = 0;
                Computer.SendKey = delegate(short key, bool up, bool extended)
                {
                    short code, scan;
                    int flags;
                    Computer.Keyed(key, up, extended, out code, out scan, out flags);
                    flagged.Add(flags);
                    int bits = 1 | ((scan & 0xFF) << 16) | ((flags & 1) << 24) | (up ? unchecked((int)0xC0000000) : 0);
                    KeyMessage(game.Handle, up ? 0x101 : 0x100, new IntPtr(code), new IntPtr(bits));
                };
                Computer.Finish();
                Computer.Arm();
                string[] names = { "Control_R", "Alt_R", "KP_Enter", "KP_Divide", "Up", "Insert", "Delete", "Home", "End", "Prior", "Next", "numpad8", "numpad0", "Shift_R", "KP_Multiply", "KP_Subtract", "KP_Add", "KP_Decimal", "space", "F1", "Control_L", "Escape" };
                int[,] wanted =
                {
                    { 0xA3, 0x1D, 1 }, { 0xA5, 0x38, 1 }, { 0x0D, 0x1C, 1 }, { 0x6F, 0x35, 1 }, { 0x26, 0x48, 1 }, { 0x2D, 0x52, 1 }, { 0x2E, 0x53, 1 }, { 0x24, 0x47, 1 }, { 0x23, 0x4F, 1 }, { 0x21, 0x49, 1 }, { 0x22, 0x51, 1 },
                    { 0x68, 0x48, 0 }, { 0x60, 0x52, 0 }, { 0xA1, 0x36, 0 }, { 0x6A, 0x37, 0 }, { 0x6D, 0x4A, 0 }, { 0x6B, 0x4E, 0 }, { 0x6E, 0x53, 0 }, { 0x20, 0x39, 0 }, { 0x70, 0x3B, 0 }, { 0x11, 0x1D, 0 }, { 0x1B, 0x01, 0 }
                };
                string pressed = Computer.Member("key", new ToolArguments { Text = string.Join(" ", names) }, null);
                if (Computer.Failed(pressed) || game.Got.Count != names.Length * 2) throw new Exception("Her keys didn't all reach the stand-in window: " + pressed + " / " + game.Got.Count + " messages.");
                for (int i = 0; i < names.Length; i++)
                    for (int edge = 0; edge < 2; edge++)
                    {
                        int[] got = game.Got[i * 2 + edge];
                        if (got[0] != wanted[i, 0] || got[1] != wanted[i, 1] || got[2] != wanted[i, 2] || got[3] != edge)
                            throw new Exception(names[i] + " reached the game as key 0x" + got[0].ToString("X") + ", scan 0x" + got[1].ToString("X") + (got[2] == 1 ? " extended" : "") + (got[3] == 1 ? " going up" : " going down") + ".");
                    }
                foreach (int flags in flagged) if ((flags & 0x0008) == 0) throw new Exception("A key with a scan code went out without KEYEVENTF_SCANCODE.");
                checks.Add("PASS her keys reach a stand-in game window with their real scan codes: right Ctrl and Alt, the keypad's Enter and slash, the arrows and the six keys above them carry the extended bit, numpad digits, right Shift, space, F1 and Esc don't, and each goes down and comes back up");

                game.Got.Clear();
                string problem;
                string quick = Computer.Quick(Computer.QuickSteps("Control_R+w 0.06, KP_Enter, Right", out problem), null);
                string held = Computer.Member("hold_key", new ToolArguments { Text = "Alt_R", Duration = 0.1 }, null);
                int[][] seen = game.Got.ToArray();
                if (!quick.StartsWith("Did ", StringComparison.Ordinal) || Computer.Failed(held) || seen.Length != 10
                    || seen[0][1] != 0x1D || seen[0][2] != 1 || seen[3][1] != 0x1D || seen[3][3] != 1 || seen[4][1] != 0x1C || seen[4][2] != 1 || seen[6][1] != 0x4D || seen[6][2] != 1 || seen[8][1] != 0x38 || seen[8][2] != 1 || seen[9][3] != 1)
                    throw new Exception("Her combo or held keys lost their scan codes: " + quick + " / " + held + " / " + seen.Length + " messages.");
                short virtualCode, virtualScan;
                int virtualFlags;
                Computer.Keyed(Computer.Code("print"), false, false, out virtualCode, out virtualScan, out virtualFlags);
                if (virtualScan != 0 || virtualFlags != 0 || virtualCode != 0x2C) throw new Exception("Print Screen went out with a scan code.");
                Computer.Keyed(Computer.Code("xf86audioraisevolume"), true, false, out virtualCode, out virtualScan, out virtualFlags);
                if (virtualScan != 0 || virtualFlags != 0x0002) throw new Exception("A media key went out with a scan code.");
                checks.Add("PASS her combo and held keys carry the same scan codes and extended bits, and Print Screen and the media keys stay virtual-key only");

                Sight chat = new Sight { Window = new IntPtr(7501), Title = "#general - Discord", Program = "Discord" };
                if (Computer.Combo("KP_Enter") != "numpadenter" || Computer.Combo("KP_5") != "5" || Computer.Combo("Control_R+Return") != "rightctrl+enter" || Computer.Combo("Alt_R") != "rightalt" || Computer.Combo("numpad_7") != "numpad7"
                    || !Computer.Entering("key", new ToolArguments { Text = "KP_Enter" }) || !Computer.Entering("key", new ToolArguments { Text = "Control_R+Return" }) || !Computer.Entering("key", new ToolArguments { Text = "Alt_R+KP_Enter" }) || Computer.Entering("key", new ToolArguments { Text = "shift+KP_Enter" })
                    || Computer.Risk("press", new ToolArguments { Keys = "numpadenter" }, chat) != "send" || Computer.Risk("press", new ToolArguments { Keys = "Return" }, chat) != "send" || Computer.Risk("press", new ToolArguments { Keys = "enter" }, chat) != "send"
                    || !Computer.Banning("rightctrl+alt+delete") || !Computer.Banning("ctrl+rightalt+numpadperiod") || !Computer.Banning("rightctrl+rightshift+esc") || !Computer.Banning("esc+shift+ctrl") || !Computer.Banning("shift+numpadperiod") || !Computer.Banning("win+r") || Computer.Banning("ctrl+c") || Computer.Banning("rightctrl+w"))
                    throw new Exception("Her new key names got round an ask: KP_Enter is " + Computer.Combo("KP_Enter") + ", Control_R+Return is " + Computer.Combo("Control_R+Return"));
                checks.Add("PASS the keypad's Enter and right Ctrl or Alt with Enter still ask before sending in a chat app, right-hand modifiers, the keypad's dot and any key order still ask first for Ctrl + Alt + Del and the rest, and Claude's KP_ digits stay the number row");
            }
            finally
            {
                game.Dispose();
                Computer.SendKey = keyWas; Computer.AboveHer = aboveWas; Hands.Foreground = foregroundWas; Computer.Escape = escapeWas;
                Computer.Halted = haltWas; Computer.Flinched = flinchedWas; Sfx.Level = soundWas;
                Computer.Finish();
                Computer.Arm();
            }
        }

        private sealed class ComboRig : IDisposable
        {
            private readonly Func<List<OpenWindow>> windowsWas = Hands.Windows;
            private readonly Func<OpenWindow, string, bool> steerWas = Hands.Steer;
            private readonly Func<IntPtr> foregroundWas = Hands.Foreground, frontWas = Computer.Front;
            private readonly Func<IntPtr, bool> activeWas = Hands.Active, aliveWas = Computer.Alive, aboveWas = Computer.AboveHer;
            private readonly Func<IntPtr, IntPtr, bool> sameWas = Computer.SameWindow;
            private readonly Func<IntPtr, Rectangle> clientWas = Computer.ClientArea;
            private readonly Func<IntPtr, string> titleWas = Computer.TitleOf;
            private readonly Func<Point, IntPtr> atWas = Computer.WindowAt;
            private readonly Func<Rectangle, byte[]> grayWas = Computer.GrayFrame;
            private readonly Func<Rectangle?, Picture> shootWas = Computer.Shoot;
            private readonly Func<Sight> glimpseWas = Computer.Glimpse, lookWas = Computer.Look;
            private readonly Func<string> gameOpenWas = Computer.GameOpen;
            private readonly Picture shownWas = Computer.Shown;
            private readonly Action<short, bool, bool> keyWas = Computer.SendKey;
            private readonly Action<int, int> mouseWas = Computer.SendMouse, nudgeWas = Computer.Nudge;
            private readonly Action<Point> trackWas = Computer.Track, tapWas = Computer.Tapped, putWas = Computer.PutCursor;
            private readonly Func<Point> cursorWas = Computer.CursorAt;
            private readonly Action<bool> lendWas = Computer.Lending, signWas = InControl.Changed;
            private readonly Func<bool> hiddenWas = Computer.CursorHidden, escapeWas = Computer.Escape;
            private readonly Func<Point, bool> underWas = Computer.GameUnder;
            private readonly Action haltWas = Computer.Halted;
            private readonly Action<string> runWas = Hands.Run;
            private readonly Func<string[]> processesWas = AntiCheat.Processes;
            private readonly Func<string, bool> loadedWas = AntiCheat.Loaded;
            private readonly string notesWas = GameNotes.Folder;
            private readonly bool flinchedWas = Computer.Flinched, streamWas = Computer.Streaming, keysOkWas = Computer.KeysOk;
            private readonly int soundWas = Sfx.Level;

            internal readonly IntPtr Game = new IntPtr(7401), Other = new IntPtr(7402);
            internal readonly Sight Sight;
            internal readonly Stopwatch Clock = Stopwatch.StartNew();
            internal readonly List<long[]> Sent = new List<long[]>();
            internal readonly HashSet<short> Down = new HashSet<short>();
            internal int Buttons, Taps, FrontReads;
            internal volatile bool TheirEsc;
            internal long SwitchAfter = -1;
            internal short Fails;
            internal string[] Running = new string[0];
            internal readonly HashSet<string> Drivers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly ManualResetEvent Pressing = new ManualResetEvent(false);
            internal readonly string Notes = Path.Combine(Path.GetTempPath(), "capri-combo-notes-" + Guid.NewGuid().ToString("N"));
            private long firstDown = -1;

            internal ComboRig()
            {
                Sight = new Sight { Window = Game, Title = "Minecraft 26.3 - Singleplayer", Program = "javaw", Bounds = new Rectangle(0, 0, 1280, 720) };
                Rectangle client = new Rectangle(0, 0, 1280, 720);
                Point cursor = new Point(9, 9);
                Directory.CreateDirectory(Notes);
                GameNotes.Folder = Notes;
                Hands.Run = delegate { throw new Exception("A combo check tried to open something."); };
                Hands.Windows = delegate { return new List<OpenWindow> { new OpenWindow(Game, Sight.Title, Sight.Program) }; };
                Hands.Foreground = Computer.Front = delegate
                {
                    FrontReads++;
                    return SwitchAfter >= 0 && firstDown >= 0 && Clock.ElapsedMilliseconds - firstDown > SwitchAfter ? Other : Game;
                };
                Hands.Active = delegate(IntPtr window) { return window == Computer.Front(); };
                Hands.Steer = delegate { return true; };
                Computer.Alive = delegate(IntPtr window) { return window == Game || window == Other; };
                Computer.AboveHer = delegate { return false; };
                Computer.SameWindow = delegate(IntPtr one, IntPtr two) { return one == two; };
                Computer.ClientArea = delegate(IntPtr window) { return window == Game ? client : Rectangle.Empty; };
                Computer.TitleOf = delegate(IntPtr window) { return window == Game ? Sight.Title : null; };
                Computer.WindowAt = delegate(Point at) { return client.Contains(at) ? Game : Other; };
                int frames = 0;
                Computer.GrayFrame = delegate
                {
                    byte[] frame = new byte[Computer.GrayWidth * Computer.GrayHeight];
                    byte tone = (byte)(++frames % 2 == 0 ? 120 : 60);
                    for (int i = 0; i < 2000; i++) frame[i] = tone;
                    return frame;
                };
                Computer.Glimpse = Computer.Look = delegate { return new Sight { Window = Game, Title = Sight.Title, Program = Sight.Program, Bounds = Sight.Bounds }; };
                Computer.Shoot = delegate { return new Picture { Jpeg = new byte[] { 1, 2, 3 }, Size = new Size(1280, 720), Area = new Rectangle(0, 0, 1280, 720) }; };
                Computer.GameOpen = delegate { return null; };
                Computer.SendKey = delegate(short code, bool up, bool extended)
                {
                    lock (Sent)
                    {
                        if (!up && code == Fails) throw new InvalidOperationException("The stand-in keyboard broke.");
                        if (up) Down.Remove(code);
                        else
                        {
                            Down.Add(code);
                            if (firstDown < 0) firstDown = Clock.ElapsedMilliseconds;
                        }
                        Sent.Add(new long[] { up ? 1 : 0, code, Clock.ElapsedMilliseconds });
                    }
                    if (!up) Pressing.Set();
                };
                Computer.SendMouse = delegate(int flags, int data)
                {
                    lock (Sent)
                    {
                        if ((flags & 0x002A) != 0) Sent.Add(new long[] { 2, flags, Clock.ElapsedMilliseconds });
                        if ((flags & 0x0054) != 0) Sent.Add(new long[] { 3, flags, Clock.ElapsedMilliseconds });
                        Buttons |= flags & 0x002A;
                        Buttons &= ~((flags & 0x0054) >> 1);
                    }
                };
                Computer.Nudge = delegate { };
                Computer.Track = delegate { };
                Computer.Tapped = delegate { Taps++; };
                Computer.PutCursor = delegate(Point at) { cursor = at; };
                Computer.CursorAt = delegate { return cursor; };
                Computer.Lending = InControl.Changed = delegate { };
                Computer.CursorHidden = delegate { return false; };
                Computer.GameUnder = delegate { return false; };
                Computer.Escape = delegate
                {
                    if (TheirEsc) return true;
                    lock (Sent) return Down.Contains(0x1B);
                };
                Computer.Halted = delegate { };
                AntiCheat.Processes = delegate { return Running; };
                AntiCheat.Loaded = delegate(string name) { return Drivers.Contains(name); };
                Computer.Flinched = Computer.Streaming = false;
                Computer.KeysOk = false;
                Sfx.Level = 0;
                Computer.Finish();
                Computer.Arm();
            }

            internal void Clear()
            {
                lock (Sent) { Sent.Clear(); firstDown = -1; }
                Pressing.Reset();
            }

            internal string Combo(string steps) { return Computer.Do("combo", new ToolArguments { Steps = steps }, Sight); }

            internal bool Let() { lock (Sent) return Down.Count == 0 && Buttons == 0; }

            public void Dispose()
            {
                TheirEsc = false;
                Computer.Finish();
                Hands.Windows = windowsWas; Hands.Steer = steerWas; Hands.Foreground = foregroundWas; Computer.Front = frontWas; Hands.Active = activeWas; Hands.Run = runWas;
                Computer.Alive = aliveWas; Computer.AboveHer = aboveWas; Computer.SameWindow = sameWas; Computer.ClientArea = clientWas; Computer.TitleOf = titleWas; Computer.WindowAt = atWas;
                Computer.GrayFrame = grayWas; Computer.Shoot = shootWas; Computer.Glimpse = glimpseWas; Computer.Look = lookWas; Computer.GameOpen = gameOpenWas; Computer.Shown = shownWas;
                Computer.SendKey = keyWas; Computer.SendMouse = mouseWas; Computer.Nudge = nudgeWas; Computer.Track = trackWas; Computer.Tapped = tapWas; Computer.PutCursor = putWas; Computer.CursorAt = cursorWas;
                Computer.Lending = lendWas; InControl.Changed = signWas; Computer.CursorHidden = hiddenWas; Computer.GameUnder = underWas; Computer.Halted = haltWas; Computer.Escape = escapeWas;
                AntiCheat.Processes = processesWas; AntiCheat.Loaded = loadedWas; GameNotes.Folder = notesWas;
                Computer.Flinched = flinchedWas; Computer.Streaming = streamWas; Computer.KeysOk = keysOkWas; Sfx.Level = soundWas;
                Computer.Arm();
                Pressing.Close();
                try { Directory.Delete(Notes, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static string ComboEvents(List<long[]> sent)
        {
            List<string> said = new List<string>();
            foreach (long[] one in sent) said.Add((one[0] == 0 ? "down " : one[0] == 1 ? "up " : one[0] == 2 ? "press " : "release ") + one[1].ToString("X") + " @" + one[2]);
            return string.Join(", ", said.ToArray());
        }

        private static void SmokeGameCombo(List<string> checks)
        {
            using (ComboRig rig = new ComboRig())
            {
                string warm = rig.Combo("f24");
                if (!warm.StartsWith("Did ", StringComparison.Ordinal) || rig.Taps != 1 || !rig.Let()) throw new Exception("Her first combo in a game didn't click into it once first: " + warm);
                rig.Clear();
                Stopwatch whole = Stopwatch.StartNew();
                string ran = rig.Combo("shift+w 0.3, space, click, right_click 0.2, wait 0.1, d 0.15");
                long took = whole.ElapsedMilliseconds;
                List<long[]> sent;
                lock (rig.Sent) sent = new List<long[]>(rig.Sent);
                string order = ComboEvents(sent);
                long[][] at = sent.ToArray();
                if (!ran.StartsWith("Did shift+w 0.3 s, space, click, right_click 0.2 s, wait 0.1 s and d 0.15 s", StringComparison.Ordinal) || at.Length < 12)
                    throw new Exception("Her combo didn't run its steps: " + ran + " / " + order);
                long walked = at[2][2] - at[1][2], aimed = at[9][2] - at[8][2], waited = at[10][2] - at[9][2], gap = Math.Max(at[4][2] - at[3][2], at[6][2] - at[5][2]);
                if (at.Length != 12 || !(at[0][0] == 0 && at[0][1] == 0x10 && at[1][0] == 0 && at[1][1] == 0x57 && at[2][0] == 1 && at[2][1] == 0x57 && at[3][0] == 1 && at[3][1] == 0x10 && at[4][0] == 0 && at[4][1] == 0x20 && at[5][0] == 1 && at[5][1] == 0x20
                    && at[6][0] == 2 && at[6][1] == 0x02 && at[7][0] == 3 && at[7][1] == 0x04 && at[8][0] == 2 && at[8][1] == 0x08 && at[9][0] == 3 && at[9][1] == 0x10 && at[10][0] == 0 && at[10][1] == 0x44 && at[11][0] == 1 && at[11][1] == 0x44))
                    throw new Exception("Her combo ran its steps out of order: " + order);
                if (walked < 280 || walked > 700 || aimed < 180 || aimed > 600 || waited < 90 || gap > 200 || took > 2500 || !rig.Let())
                    throw new Exception("Her combo's timing was off: w held " + walked + " ms, right button " + aimed + " ms, wait " + waited + " ms, longest gap between steps " + gap + " ms, all of it " + took + " ms / " + order);
                checks.Add("PASS her combo runs its steps in order with the timing given and no settling pause between them: w held " + walked + " ms for 0.3 s, right button " + aimed + " ms for 0.2 s, the longest gap between steps " + gap + " ms, six steps in " + took + " ms, and nothing left held");

                rig.Clear();
                string own = rig.Combo("esc, w 0.1");
                if (!own.StartsWith("Did esc", StringComparison.Ordinal) || Computer.Interrupted || !rig.Let()) throw new Exception("Her own Esc in a combo stopped her: " + own);
                rig.Clear();
                Thread theirs = new Thread(delegate() { if (rig.Pressing.WaitOne(2000)) { Thread.Sleep(80); rig.TheirEsc = true; } }) { IsBackground = true };
                theirs.Start();
                Stopwatch stopping = Stopwatch.StartNew();
                string escaped = rig.Combo("w 4, space");
                long stopMs = stopping.ElapsedMilliseconds;
                theirs.Join(1000);
                bool spaced;
                lock (rig.Sent) spaced = rig.Sent.Exists(one => one[0] == 0 && one[1] == 0x20);
                if (escaped != Computer.Escaped || stopMs > 1200 || spaced || !rig.Let()) throw new Exception("Their Esc didn't stop her combo and let go at once: " + escaped + " after " + stopMs + " ms");
                rig.TheirEsc = false;
                Computer.Finish();
                Computer.Arm();
                checks.Add("PASS Esc stops her combo within " + stopMs + " ms with every key let go and no later step run, while an Esc that's one of her own steps doesn't stop her");

                rig.Clear();
                rig.SwitchAfter = 120;
                string switched = rig.Combo("w+shift 3, space");
                rig.SwitchAfter = -1;
                if (switched != "Stopped because the window I started on is no longer in front." || !rig.Let()) throw new Exception("A window switch left her combo holding keys: " + switched);
                rig.Clear();
                rig.Fails = 0x20;
                Exception broke = null;
                try { rig.Combo("shift+w 0.1, shift+space 0.2, d 0.1"); }
                catch (InvalidOperationException error) { broke = error; }
                rig.Fails = 0;
                bool dPressed;
                lock (rig.Sent) dPressed = rig.Sent.Exists(one => one[0] == 0 && one[1] == 0x44);
                if (broke == null || dPressed || !rig.Let()) throw new Exception("A failure partway through her combo left keys held or ran on.");
                rig.Clear();
                using (ManualResetEvent ended = new ManualResetEvent(false))
                {
                    Thread holding = new Thread(delegate() { try { rig.Combo("w+shift 5, right_click 2"); } finally { ended.Set(); } }) { IsBackground = true };
                    holding.Start();
                    if (!rig.Pressing.WaitOne(2000)) throw new Exception("The closing combo never started.");
                    Stopwatch closing = Stopwatch.StartNew();
                    Computer.Close();
                    long closeMs = closing.ElapsedMilliseconds;
                    if (!ended.WaitOne(1500) || closeMs > 1200 || !rig.Let()) throw new Exception("Closing didn't stop her combo and let go: " + closeMs + " ms");
                }
                typeof(Computer).GetField("closing", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).SetValue(null, false);
                Computer.Finish();
                Computer.Arm();
                checks.Add("PASS her combo lets go of every key and button when the game leaves the front, when a step fails partway and when Capri closes, and runs nothing after");

                rig.Clear();
                string[,] refused =
                {
                    { "w 5, d 4", "That didn't work: a combo runs for up to 8 seconds. Split it, and look in between." },
                    { "w, w, w, w, w, w, w, w, w, w, w, w, w", "That didn't work: a combo takes up to 12 steps. Split it, and look in between." },
                    { "w, hyper 0.2", "There's no key called hyper." },
                    { "click+right_click", "That didn't work: one step can only click one button. Put the next click in its own step." },
                    { " , ; ", "There's no step to do. Give steps like w 0.5, space, click." }
                };
                for (int i = 0; i < refused.GetLength(0); i++)
                {
                    string said = rig.Combo(refused[i, 0]);
                    if (said != refused[i, 1] || !Computer.Failed(said)) throw new Exception("A combo she can't do wasn't turned down plainly: " + refused[i, 0] + " / " + said);
                }
                string parse;
                List<Computer.QuickStep> loose = Computer.QuickSteps("Hold Shift + W for 0.8s then tap space; left click 250ms\nwait", out parse);
                if (loose == null || loose.Count != 4 || loose[0].Chord != "shift+w" || Math.Abs(loose[0].Seconds - 0.8) > 0.001 || loose[1].Chord != "space" || loose[2].Button != "left" || Math.Abs(loose[2].Seconds - 0.25) > 0.001 || !loose[3].Waiting || Math.Abs(loose[3].Seconds - 0.2) > 0.001)
                    throw new Exception("A combo written a little differently wasn't read: " + parse);
                bool sentAny;
                lock (rig.Sent) sentAny = rig.Sent.Count > 0;
                if (sentAny) throw new Exception("A combo she turned down still sent input.");
                checks.Add("PASS a combo that's too long, has too many steps, names a key that doesn't exist or clicks two buttons at once is turned down plainly before any input, and one written a little differently is still read");

                if (Security.On(Security.Unasked)) checks.Add("LIMIT combo asks: Things your request didn't mention is on here, so win+r doesn't ask first");
                else
                {
                    Sight discord = new Sight { Window = rig.Other, Title = "#general - Discord", Program = "Discord" }, roblox = new Sight { Window = rig.Game, Title = "Roblox", Program = "RobloxPlayerBeta" };
                    string barredRun = rig.Combo("w, win+r");
                    bool barredSent;
                    lock (rig.Sent) barredSent = rig.Sent.Count > 0;
                    if (barredRun != "Won't press win+r." || barredSent || Computer.Risk("combo", new ToolArguments { Steps = "w 0.2, win+r" }, rig.Sight) != "keys" || Computer.Question("combo", new ToolArguments { Steps = "w 0.2, win+r" }, rig.Sight) != "Should I press win+r?"
                        || Computer.Risk("combo", new ToolArguments { Steps = "w, Return" }, rig.Sight) != null || Computer.Risk("combo", new ToolArguments { Steps = "KP_Enter" }, discord) != "send"
                        || Computer.Risk("combo", new ToolArguments { Steps = "enter" }, roblox) != null || Computer.Risk("combo", new ToolArguments { Steps = "enter" }, roblox, true) != "send")
                        throw new Exception("Her combo got round an ask: " + barredRun);
                    Job asking = new Job("play minecraft");
                    int turns = 0;
                    asking.Drive = delegate
                    {
                        if (++turns > 1) throw new Exception("Her combo with win+r ran without asking.");
                        return Brain.Parsed("{\"content\":[{\"type\":\"tool_use\",\"id\":\"q1\",\"name\":\"combo\",\"input\":{\"steps\":\"w 0.2, win+r\"}}],\"stop_reason\":\"tool_use\"}");
                    };
                    string question = asking.Run();
                    lock (rig.Sent) barredSent = rig.Sent.Count > 0;
                    if (question != "Should I press win+r?" || asking.Outcome != "waiting" || barredSent) throw new Exception("Claude's combo with win+r didn't ask first: " + question);
                    checks.Add("PASS her combo asks first before keys like win+r on every brain path, and its Enter asks before sending in Discord and after typing in Roblox, the same as a plain Enter");
                }

                ToolArguments written;
                string writtenTool;
                List<KeyValuePair<string, ToolArguments>> moves = Computer.Moves("combo{\"steps\": \"w 0.4, space\"}\nclick{\"x\": 50, \"y\": 50}");
                bool member;
                if (Computer.Toolset.IndexOf("{\"name\":\"combo\",", StringComparison.Ordinal) < 0 || Computer.Tools.IndexOf("\"name\":\"combo\"", StringComparison.Ordinal) < 0 || Computer.ClaudeTools.IndexOf("\"name\":\"combo\"", StringComparison.Ordinal) < 0
                    || Computer.Listing.IndexOf("\n- combo: ", StringComparison.Ordinal) < 0 || Computer.Sees.IndexOf("combo{\"steps\":", StringComparison.Ordinal) < 0 || Computer.Playing.IndexOf("use combo", StringComparison.Ordinal) < 0
                    || moves.Count != 2 || moves[0].Key != "combo" || moves[0].Value.Steps != "w 0.4, space" || Computer.Planned("combo", moves[0].Value, null, null, out member) != "combo" || member
                    || !Computer.Written("combo{\"steps\": \"w 0.4, space\"}", out writtenTool, out written) || writtenTool != "combo" || written.Steps != "w 0.4, space"
                    || !Computer.KeysOnly("combo", written) || Computer.Status("combo", written, null) != "Doing quick moves." || Computer.Did("combo", written, null) != "Did quick moves" || Computer.Describe("combo", written, null) != "combo w 0.4, space"
                    || RemoteJobWords.RemoteJobLabel("combo", "", false, false) != "Pressing some keys")
                    throw new Exception("Her combo isn't a tool on every brain path.");
                checks.Add("PASS her combo is a tool for Claude, for brains with tools, for written moves and for the grid brain, it's in her game prompt, and her phone only says she's pressing some keys");
            }
        }

        private static void SmokeAntiCheat(List<string> checks)
        {
            Func<string[]> realProcesses = AntiCheat.Processes;
            Func<string, bool> realLoaded = AntiCheat.Loaded;
            using (ComboRig rig = new ComboRig())
            {
                string[][] running = { new[] { "explorer", "BEService" }, new[] { "javaw", "EasyAntiCheat_EOS" }, new[] { "FACEIT" }, new[] { "EasyAntiCheat_EOS_Helper" }, new[] { "cod" }, new[] { "SGuard64" } };
                string[] named = { "BattlEye", "Easy Anti-Cheat", "FACEIT", "Easy Anti-Cheat", "Ricochet", "ACE" };
                for (int i = 0; i < running.Length; i++)
                {
                    rig.Running = running[i];
                    string guarded = rig.Combo("w 0.2, space");
                    bool sent;
                    lock (rig.Sent) sent = rig.Sent.Count > 0;
                    if (guarded != AntiCheat.Refusal(named[i]) || !Computer.Failed(guarded) || sent || rig.Taps != 0) throw new Exception("Her combo ran beside " + named[i] + ": " + guarded);
                }
                rig.Running = new[] { "explorer", "javaw", "Discord" };
                foreach (string driver in new[] { "vgk", "BEDaisy", "FACEIT" })
                {
                    rig.Drivers.Clear();
                    rig.Drivers.Add(driver);
                    string guarded = rig.Combo("w 0.2");
                    if (!guarded.StartsWith("Won't do quick moves while ", StringComparison.Ordinal) || rig.Taps != 0) throw new Exception("Her combo ran beside the " + driver + " driver: " + guarded);
                }
                if (AntiCheat.Running() != "FACEIT") throw new Exception("A driver on its own wasn't named by its anti-cheat.");
                rig.Drivers.Clear();
                rig.Running = new[] { "explorer", "javaw", "Discord", "vgcsomething", "codex", "steam" };
                string free = rig.Combo("w 0.1");
                if (AntiCheat.Running() != null || !free.StartsWith("Did ", StringComparison.Ordinal)) throw new Exception("Her combo was turned down with no anti-cheat running: " + free);
                if (AntiCheat.Refusal("Riot Vanguard") != "Won't do quick moves while Riot Vanguard is running: anti-cheat like that can ban for them.") throw new Exception("Her anti-cheat line changed.");
            }
            Stopwatch probing = Stopwatch.StartNew();
            string here;
            AntiCheat.Processes = realProcesses;
            AntiCheat.Loaded = realLoaded;
            here = AntiCheat.Running();
            long probeMs = probing.ElapsedMilliseconds;
            checks.Add("PASS while anti-cheat runs, like BattlEye, Easy Anti-Cheat, FACEIT, ACE, Ricochet's games or Riot Vanguard's driver, her combo sends no key, click or focus click and says so plainly, and it runs once none is; on this PC the real check took " + probeMs + " ms and found " + (here ?? "none"));
        }
    }
}
