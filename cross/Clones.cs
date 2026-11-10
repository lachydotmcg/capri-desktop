using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        [DataMember] internal bool Hologram;
    }

    internal static class CloneNative
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";
        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        [DllImport(ObjC)] private static extern IntPtr sel_registerName(string name);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")] private static extern void SendBool(IntPtr window, IntPtr selector, [MarshalAs(UnmanagedType.I1)] bool value);
        [DllImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool ReadBool(IntPtr window, IntPtr selector);
        [DllImport(Graphics)]
        [return: MarshalAs(UnmanagedType.I1)] internal static extern bool CGPreflightListenEventAccess();
        [DllImport(Graphics)]
        [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGEventSourceKeyState(int state, ushort key);

        internal static bool Esc() { return CGEventSourceKeyState(0, 53); }

        internal static void Through(Window window, bool on)
        {
            IPlatformHandle handle = window.TryGetPlatformHandle();
            IntPtr native = handle is IMacOSTopLevelPlatformHandle mac ? mac.NSWindow : handle != null && handle.HandleDescriptor == "NSWindow" ? handle.Handle : IntPtr.Zero;
            if (native == IntPtr.Zero)
                throw new InvalidOperationException("Couldn't set clicks through my window.");
            SendBool(native, sel_registerName("setIgnoresMouseEvents:"), on);
            SendBool(native, sel_registerName("setHidesOnDeactivate:"), false);
            if (ReadBool(native, sel_registerName("ignoresMouseEvents")) != on)
                throw new InvalidOperationException("Couldn't set clicks through my window.");
        }
    }

    internal sealed class HologramArt : IDisposable
    {
        private sealed class Strip
        {
            internal Bitmap Source;
            internal WriteableBitmap Tinted;
        }

        private readonly Dictionary<Clip, Strip[]> strips = new Dictionary<Clip, Strip[]>();
        private readonly Func<Clip, bool, Bitmap> read;
        internal int Count { get { return strips.Values.Sum(pair => pair.Count(strip => strip != null)); } }

        internal HologramArt(Func<Clip, bool, Bitmap> read = null)
        {
            this.read = read ?? ((clip, left) => clip.Art(left));
        }

        internal static int Tint(int pixel)
        {
            int alpha = (int)((uint)pixel >> 24);
            int blue = ((pixel & 255) * 224 + alpha * 31) * 3 / 1275;
            int green = (((pixel >> 8) & 255) * 224 + alpha * 27) * 3 / 1275;
            int red = (((pixel >> 16) & 255) * 224 + alpha * 8) * 3 / 1275;
            return (alpha * 3 / 5 << 24) | (red << 16) | (green << 8) | blue;
        }

        internal Bitmap Art(Clip clip, bool left, bool on)
        {
            Bitmap image = read(clip, left);
            if (!on) return image;
            if (!strips.TryGetValue(clip, out Strip[] pair)) strips.Add(clip, pair = new Strip[2]);
            int facing = left ? 1 : 0;
            Strip strip = pair[facing];
            if (strip != null && strip.Source == image) return strip.Tinted;
            WriteableBitmap made = new WriteableBitmap(image.PixelSize, image.Dpi, PixelFormat.Bgra8888, AlphaFormat.Premul);
            try
            {
                using (ILockedFramebuffer frame = made.Lock())
                {
                    image.CopyPixels(frame, AlphaFormat.Premul);
                    int[] row = new int[frame.Size.Width];
                    for (int y = 0; y < frame.Size.Height; y++)
                    {
                        IntPtr at = IntPtr.Add(frame.Address, y * frame.RowBytes);
                        Marshal.Copy(at, row, 0, row.Length);
                        for (int x = 0; x < row.Length; x++) row[x] = Tint(row[x]);
                        Marshal.Copy(row, 0, at, row.Length);
                    }
                }
            }
            catch { made.Dispose(); throw; }
            if (strip != null) strip.Tinted.Dispose();
            pair[facing] = new Strip { Source = image, Tinted = made };
            return made;
        }

        public void Dispose()
        {
            foreach (Strip[] pair in strips.Values)
                foreach (Strip strip in pair)
                    if (strip != null) strip.Tinted.Dispose();
            strips.Clear();
        }
    }

    internal interface CloneView
    {
        void Draw(CloneRoom room, double now);
        void Close();
    }

    internal sealed class CloneFigure
    {
        internal double X, Y, ToX, ToY, Born, RestUntil, Phase;
        internal bool Left;
    }

    internal sealed class CloneRoom
    {
        internal const double GlitchIn = 0.7, Pace = 170, EscHold = 0.7;
        internal readonly List<CloneFigure> Bodies = new List<CloneFigure>();
        internal long Pending;
        internal readonly PixelRect Area;
        internal readonly int Size;
        private readonly Random random;
        private readonly Func<CloneView> make;
        private readonly Func<double> clock;
        private CloneView view;
        private double last;
        private PixelPoint spawn;

        internal CloneRoom(PixelRect area, int size, Func<double> clock, Func<CloneView> make, int seed)
        {
            Area = area;
            Size = Math.Max(1, Math.Min(size, Math.Min(area.Width, area.Height)));
            this.clock = clock;
            this.make = make;
            random = new Random(seed);
            last = clock();
        }

        internal void Spawn(int count, PixelPoint at)
        {
            spawn = at;
            Pending = checked(Pending + Math.Max(0, count));
        }

        private double Between(double low, double high) { return low + random.NextDouble() * (high - low); }
        private double X(double x) { return Math.Clamp(x, Area.X, Area.Right - Size); }
        private double Y(double y) { return Math.Clamp(y, Area.Y, Area.Bottom - Size); }
        private void Wander(CloneFigure body)
        {
            body.ToX = Between(Area.X, Area.Right - Size);
            body.ToY = Between(Area.Y, Area.Bottom - Size);
        }

        internal void Step(bool keepingUp)
        {
            double now = clock(), passed = Math.Clamp(now - last, 0, 0.25);
            last = now;
            for (long left = keepingUp ? Math.Min(Pending, 4 + Bodies.Count / 6) : 0; left > 0; left--)
            {
                CloneFigure body = new CloneFigure { X = X(spawn.X - Size / 2 + Between(-90, 90)), Y = Y(spawn.Y - Size / 2 + Between(-70, 70)), Born = now, RestUntil = now + GlitchIn, Phase = Between(0, 4) };
                Wander(body);
                Bodies.Add(body);
                Pending--;
            }
            foreach (CloneFigure body in Bodies)
            {
                if (now < body.RestUntil) continue;
                double dx = body.ToX - body.X, dy = body.ToY - body.Y, far = Math.Sqrt(dx * dx + dy * dy), step = Pace * passed;
                if (Math.Abs(dx) > 2) body.Left = dx < 0;
                if (far <= step) { body.X = body.ToX; body.Y = body.ToY; body.RestUntil = now + Between(1.2, 6); Wander(body); }
                else { body.X = X(body.X + dx / far * step); body.Y = Y(body.Y + dy / far * step); }
            }
            if (Bodies.Count == 0) return;
            if (view == null) view = make();
            view.Draw(this, now);
        }

        internal void Clear()
        {
            Pending = 0;
            Bodies.Clear();
            CloneView closing = view;
            view = null;
            if (closing != null) closing.Close();
        }
    }

    internal sealed class CloneStage
    {
        internal readonly List<CloneRoom> Rooms = new List<CloneRoom>();
        private readonly Func<PixelRect, int, CloneView> make;
        private readonly Func<double> clock;
        private readonly Random random;
        private double held = -1;
        internal long Count { get { return Rooms.Sum(room => room.Pending + room.Bodies.Count); } }

        internal CloneStage(Func<double> clock, Func<PixelRect, int, CloneView> make, int seed)
        {
            this.clock = clock;
            this.make = make;
            random = new Random(seed);
        }

        internal void Spawn(PixelRect area, int size, int count, PixelPoint at)
        {
            CloneRoom room = Rooms.Find(item => item.Area == area);
            if (room == null)
            {
                room = new CloneRoom(area, size, clock, () => make(area, Math.Max(1, Math.Min(size, Math.Min(area.Width, area.Height)))), random.Next());
                Rooms.Add(room);
            }
            room.Spawn(count, at);
        }

        internal bool Step(bool esc, bool keepingUp = true)
        {
            double now = clock();
            if (!esc) held = -1;
            else if (held < 0) held = now;
            else if (now - held >= CloneRoom.EscHold) { Clear(); return true; }
            foreach (CloneRoom room in Rooms) room.Step(keepingUp);
            return false;
        }

        internal void Clear()
        {
            held = -1;
            try { foreach (CloneRoom room in Rooms) room.Clear(); }
            finally { Rooms.Clear(); }
        }
    }

    internal sealed class CloneCanvas : Control
    {
        private readonly Body body = new Body();
        private readonly Clip idle, walk, glitch;
        private readonly double scale;
        internal CloneRoom Room;
        internal double Now;

        internal CloneCanvas(Clip idle, Clip walk, Clip glitch, double scale)
        {
            this.idle = idle;
            this.walk = walk;
            this.glitch = glitch;
            this.scale = scale;
        }

        public override void Render(DrawingContext context)
        {
            if (Room == null) return;
            body.Size = Room.Size / scale / 1.8;
            foreach (CloneFigure figure in Room.Bodies)
            {
                double age = Now - figure.Born;
                Clip shown = age < CloneRoom.GlitchIn ? glitch : Now < figure.RestUntil ? idle : walk;
                body.Shown = shown;
                body.Cell = shown == glitch ? Math.Min(shown.Count - 1, (int)(age * shown.Fps)) : (int)((Now + figure.Phase) * shown.Fps) % shown.Count;
                body.FacingLeft = figure.Left;
                body.Centre = new Point((figure.X - Room.Area.X + Room.Size / 2.0) / scale, (figure.Y - Room.Area.Y + Room.Size / 2.0) / scale);
                body.Render(context);
            }
        }
    }

    internal sealed class CloneWindow : PassiveWindow, CloneView
    {
        private readonly CloneCanvas canvas;

        internal CloneWindow(Clip idle, Clip walk, Clip glitch, PixelRect area, double scale)
        {
            canvas = new CloneCanvas(idle, walk, glitch, scale);
            Title = "Capri clones";
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Topmost = true;
            ShowInTaskbar = false;
            CanResize = false;
            ShowActivated = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Position = area.Position;
            Width = area.Width / scale;
            Height = area.Height / scale;
            Content = canvas;
        }

        public void Draw(CloneRoom room, double now)
        {
            canvas.Room = room;
            canvas.Now = now;
            if (!IsVisible)
            {
                Show();
                CloneNative.Through(this, true);
            }
            canvas.InvalidateVisual();
        }
    }

    internal sealed partial class Pet
    {
        private CloneStage clones;
        private bool cloneEsc, clonePermissionTold;
        private long cloneTook;
        private MenuItem hologramItem;
        private Action<Window, bool> cloneThrough = CloneNative.Through;

        private void StartClones()
        {
            if (!OperatingSystem.IsMacOS()) return;
            Hands.Clone = (action, count) => Dispatcher.UIThread.CheckAccess() ? Clone(action, count) : Dispatcher.UIThread.Invoke(() => Clone(action, count));
            Hands.Cloning = () => clones != null;
            Hands.Hologram = action => Dispatcher.UIThread.CheckAccess() ? Hologram(action) : Dispatcher.UIThread.Invoke(() => Hologram(action));
            if (Program.Smoke == null)
                foreach (Window window in new Window[] { this, Talk.Pill, Talk.Bubble })
                    window.Opened += (_, _) => { if (Saved.Hologram) Program.Guard(() => cloneThrough(window, true)); };
            Closed += (_, _) => { ClonesOff(); body.Hologram.Dispose(); body.HologramMouth.Dispose(); };
            hologramItem = HologramMenu();
            ContextMenu.Items.Insert(3, hologramItem);
            MenuItem clear = new MenuItem { Header = "Clear clones" };
            clear.Click += Guarded(ClonesOff);
            ContextMenu.Items.Insert(4, clear);
            body.HologramOn = Saved.Hologram;
        }

        private MenuItem HologramMenu()
        {
            MenuItem item = new MenuItem { Header = "Hologram", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Saved.Hologram };
            item.Click += Guarded(() =>
            {
                try { SetHologram(!Saved.Hologram); }
                catch (Exception error) { Brain.Log("hologram", -1, error.Message); Talk.Answer("(hologram)", "Couldn't change hologram mode just now."); }
            });
            ToolTip.SetTip(item, "Clicks go through her until you turn it off from her tray icon");
            return item;
        }

        private string Clone(string action, int count)
        {
            if (action == "stop")
            {
                long gone = clones == null ? 0 : clones.Count;
                ClonesOff();
                return gone == 0 ? "I don't have any clones out." : gone == 1 ? "Got rid of my clone." : "Got rid of all " + gone + " of my clones.";
            }
            if (action != "spawn") return "Should I make clones or clear them?";
            if (count <= 0) return Hands.NoCloneChaos;
            if (quitting) return "I'm quitting.";
            Screen screen = ScreenHere();
            if (screen == null) return "Couldn't find my screen.";
            if (clones == null)
            {
                clones = new CloneStage(() => Now, (area, size) => new CloneWindow(idle, walk, glitch, area, Screens.All.First(item => item.WorkingArea == area).Scaling), random.Next());
                cloneEsc = CloneNative.CGPreflightListenEventAccess();
                cloneTook = 0;
            }
            clones.Spawn(screen.WorkingArea, (int)Math.Ceiling(body.Size * 1.8 * screen.Scaling), count, new PixelPoint(anchor.X + Px(Width / 2), anchor.Y + Px(Height / 2)));
            string line = count == 1 ? "There's two of me now, choom. Use Clear clones in my tray menu." : "There's " + count + " more of me now, choom. Use Clear clones in my tray menu.";
            if (!cloneEsc && !clonePermissionTold)
            {
                clonePermissionTold = true;
                return "To clear clones with Esc, turn on Input Monitoring for Capri in System Settings > Privacy & Security. Use Clear clones in my tray menu for now.";
            }
            return line;
        }

        private void TickClones()
        {
            if (!OperatingSystem.IsMacOS() || clones == null) return;
            try
            {
                if (away || Asleep || napAsked || quitting) { ClonesOff(); return; }
                if (clones.Rooms.Any(room => !Screens.All.Any(screen => screen.WorkingArea == room.Area))) { ClonesOff(); return; }
                Stopwatch busy = Stopwatch.StartNew();
                bool cleared = clones.Step(cloneEsc && CloneNative.Esc(), cloneTook < 110);
                cloneTook = busy.ElapsedMilliseconds;
                if (cleared)
                {
                    ClonesOff();
                    if (Talk.Quiet) Talk.Answer("holds Esc", "Fine. Just me again.");
                }
            }
            catch (Exception error)
            {
                ClonesOff();
                Brain.Log("clones", -1, error.Message);
                Talk.Answer("(clones)", "Couldn't show my clones just now.");
            }
        }

        private void ClonesOff()
        {
            CloneStage closing = clones;
            clones = null;
            if (closing != null) closing.Clear();
        }

        private string Hologram(string action)
        {
            if (action != "on" && action != "off") return "Should hologram mode be on or off, choom?";
            try { SetHologram(action == "on"); }
            catch (Exception error) { Brain.Log("hologram", -1, error.Message); return "Couldn't change hologram mode just now."; }
            return Saved.Hologram ? "Hologram mode on. Clicks go through me; turn it off from my tray icon or say hologram off." : "Hologram mode off. You can click me again.";
        }

        private void SetHologram(bool on)
        {
            bool was = Saved.Hologram;
            try
            {
                foreach (Window window in new Window[] { this, Talk.Pill, Talk.Bubble }) cloneThrough(window, on || Hands.OnMac && window == this && riding);
            }
            catch
            {
                foreach (Window window in new Window[] { this, Talk.Pill, Talk.Bubble }) cloneThrough(window, was || Hands.OnMac && window == this && riding);
                throw;
            }
            if (on)
            {
                Unhold();
                pressed = dragging = dragPending = false;
                ForgetStroke();
                if (hanging) LetGo();
            }
            Saved.Hologram = body.HologramOn = on;
            if (hologramItem != null) hologramItem.IsChecked = on;
            if (!on) { body.Hologram.Dispose(); body.HologramMouth.Dispose(); }
            body.InvalidateVisual();
            Save();
            Sync();
        }

        private sealed class FakeClone : CloneView
        {
            internal bool Closed;
            internal int Frames, Bodies;
            public void Draw(CloneRoom room, double now) { if (Closed) throw new Exception("A cleared clone was drawn."); Frames++; Bodies = room.Bodies.Count; }
            public void Close() { Closed = true; }
        }

        internal static void CloneChecks(Pet pet, List<string> checks)
        {
            double now = 100;
            List<FakeClone> views = new List<FakeClone>();
            Func<CloneStage> stage = () => new CloneStage(() => now, (area, size) => { FakeClone view = new FakeClone(); views.Add(view); return view; }, 7);
            PixelRect area = new PixelRect(-1600, 40, 1200, 760), other = new PixelRect(0, 0, 1920, 1080);
            CloneStage crowd = stage();
            crowd.Spawn(area, 220, 300, area.Position);
            crowd.Step(false, false);
            if (views.Count != 0 || crowd.Count != 300) throw new Exception("Clones spawned while the last frame was still drawing.");
            while (crowd.Rooms[0].Pending > 0) { now += 0.033; crowd.Step(false); }
            if (crowd.Rooms[0].Bodies.Count != 300 || views.Count != 1 || views[0].Bodies != 300) throw new Exception("300 clones did not share one overlay.");
            crowd.Spawn(area, 220, 2700, area.Position);
            crowd.Spawn(other, 180, 17, other.Position);
            for (int i = 0; i < 200; i++)
            {
                now += 0.25;
                crowd.Step(false);
                foreach (CloneRoom room in crowd.Rooms)
                    foreach (CloneFigure figure in room.Bodies)
                        if (figure.X < room.Area.X || figure.Y < room.Area.Y || figure.X + room.Size > room.Area.Right || figure.Y + room.Size > room.Area.Bottom) throw new Exception("A clone left her work area.");
            }
            if (crowd.Count != 3017 || crowd.Rooms[0].Bodies.Count != 3000 || crowd.Rooms[1].Bodies.Count != 17 || views.Count != 2 || views[0].Bodies != 3000 || views[1].Bodies != 17) throw new Exception("More clones made extra windows or lost their requested count.");
            now += 86400;
            int frames = views.Sum(view => view.Frames);
            crowd.Step(false);
            if (crowd.Count != 3017 || views.Sum(view => view.Frames) != frames + 2) throw new Exception("Clones expired or redrew each body as a window.");
            crowd.Spawn(area, 220, int.MaxValue, area.Position);
            crowd.Spawn(area, 220, int.MaxValue, area.Position);
            if (crowd.Rooms[0].Pending != 2L * int.MaxValue) throw new Exception("The clone queue capped or overflowed a request.");
            crowd.Step(true, false);
            now += 0.69;
            if (crowd.Step(true, false)) throw new Exception("Esc cleared clones before its hold finished.");
            now += 0.02;
            if (!crowd.Step(true) || crowd.Count != 0 || crowd.Rooms.Count != 0 || views.Any(view => !view.Closed)) throw new Exception("Holding Esc did not clear every overlay and its queue.");
            now += 10;
            crowd.Step(false);
            if (crowd.Count != 0 || views.Count != 2) throw new Exception("Cleared clones came back.");
            CloneRoom small = new CloneRoom(new PixelRect(30, -300, 80, 60), 220, () => now, () => new FakeClone(), 4);
            small.Spawn(1, new PixelPoint(0, 0));
            small.Step(true);
            if (small.Size != 60 || small.Bodies[0].Y != -300 || small.Bodies[0].X + small.Size > 110) throw new Exception("A clone did not fit a small work area.");
            small.Clear();
            checks.Add("PASS clones: 300 then 3000 copies share one overlay per screen, exact queued counts without a cap, bounded wandering and Esc clears every overlay");

            bool mac = Hands.OnMac;
            Func<string, int, string> clone = Hands.Clone;
            Func<string, string> hologram = Hands.Hologram;
            Func<bool> cloning = Hands.Cloning;
            CloneStage previous = pet.clones;
            Func<string, string> think = pet.Think;
            try
            {
                Hands.Clone = (action, count) => action + ":" + count;
                Hands.Hologram = action => "hologram:" + action;
                Hands.Cloning = () => true;
                foreach (bool onMac in new[] { false, true })
                {
                    Hands.OnMac = onMac;
                    Hands.Kit kit = Hands.KitFor();
                    if (kit.Has("clone") != onMac || kit.Has("hologram") != onMac || Hands.CrossSetting("hologram") != onMac) throw new Exception("Clone or hologram platform gaps changed.");
                    if (onMac && (kit.Has("clone:chaos") || kit.Has("capri_settings:chaos_when_upset"))) throw new Exception("Mac offered chaos mode.");
                    string tool;
                    ToolArguments args;
                    if (!Hands.Heard("make 3 clones of yourself", out tool, out args) || tool != "clone" || args.Count != 3 || args.Action != "spawn") throw new Exception("A clone count did not reach the tool.");
                    string result = Hands.Do(tool, args);
                    if (result != (onMac ? "spawn:3" : Hands.NoClones)) throw new Exception("The clone seam or Linux gap was bypassed.");
                    if (!Hands.Heard("get rid of the clones", out tool, out args) || args.Action != "stop") throw new Exception("Clone clear words were lost.");
                    if (onMac && Hands.Do(tool, args) != "stop:1") throw new Exception("Clone clear did not reach the seam.");
                    if (!Hands.Heard("chaos mode", out tool, out args) || onMac && Hands.Do(tool, args) != Hands.NoCloneChaos) throw new Exception("Mac accepted chaos mode.");
                    if (!Hands.Heard("clone yourself", out tool, out args) || onMac && Hands.Do(tool, args) != "spawn:1") throw new Exception("A plain clone request did not make one copy.");
                    foreach (string action in new[] { "on", "off" })
                        if (Hands.Do("hologram", new ToolArguments { Action = action }) != (onMac ? "hologram:" + action : Hands.NoHologram)) throw new Exception("The hologram seam or Linux gap was bypassed.");
                }
                if (Hands.Valid("clone", null) || Hands.Valid("clone", new ToolArguments { Action = "clear" }) || Hands.Valid("hologram", new ToolArguments { Action = "toggle" })) throw new Exception("Clone or hologram accepted an invalid action.");
                Hands.OnMac = true;
                Hands.Clone = pet.Clone;
                Hands.Cloning = () => pet.clones != null;
                foreach (bool withBrain in new[] { false, true })
                    foreach (string words in new[] { "stop", "enough" })
                    {
                        bool asked = false;
                        pet.Think = withBrain ? _ => { asked = true; return "wrong reply"; } : (Func<string, string>)null;
                        pet.clones = stage();
                        CloneStage clearing = pet.clones;
                        clearing.Spawn(area, 220, 300, area.Position);
                        clearing.Spawn(other, 180, 12, other.Position);
                        clearing.Step(false);
                        pet.Hear(words);
                        if (asked || pet.clones != null || clearing.Count != 0 || views.Any(view => !view.Closed)) throw new Exception(words + " did not clear every clone, overlay and pending copy locally.");
                        pet.Talk.Dismiss();
                    }
            }
            finally { pet.ClonesOff(); pet.clones = previous; pet.Think = think; Hands.OnMac = mac; Hands.Clone = clone; Hands.Hologram = hologram; Hands.Cloning = cloning; }
            checks.Add("PASS clones and hologram: Mac dispatch and Linux gaps; stop and enough clear all copies with or without a brain");

            using (WriteableBitmap source = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            using (WriteableBitmap flipped = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            using (WriteableBitmap replaced = new WriteableBitmap(new PixelSize(1, 1), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
            {
                Clip first = pet.idle, second = pet.walk;
                bool reload = false;
                using (HologramArt cache = new HologramArt((clip, left) => left ? flipped : reload && clip == first ? replaced : source))
                {
                Bitmap right = cache.Art(first, false, true), left = cache.Art(first, true, true);
                Bitmap walking = cache.Art(second, false, true);
                for (int i = 0; i < 12; i++)
                    if (!ReferenceEquals(right, cache.Art(first, false, true)) || !ReferenceEquals(left, cache.Art(first, true, true)) || !ReferenceEquals(walking, cache.Art(second, false, true))) throw new Exception("Hologram retinted a cached clip or facing.");
                if (cache.Count != 3 || !ReferenceEquals(source, cache.Art(first, false, false))) throw new Exception("Hologram lost a cached strip or changed normal art.");
                reload = true;
                Bitmap changed = cache.Art(first, false, true);
                if (ReferenceEquals(changed, right) || !ReferenceEquals(changed, cache.Art(first, false, true)) || !ReferenceEquals(left, cache.Art(first, true, true)) || cache.Count != 3) throw new Exception("Hologram failed to replace only the reloaded strip.");
                cache.Dispose();
                if (cache.Count != 0) throw new Exception("Hologram kept strips after being turned off.");
                }
            }
            checks.Add("PASS hologram strips stay cached by clip and facing and are released when switched off");

            Action<Window, bool> through = pet.cloneThrough;
            MenuItem menu = pet.hologramItem;
            bool saved = pet.Saved.Hologram, drawn = pet.body.HologramOn;
            bool riding = pet.riding;
            var applied = new Dictionary<Window, bool>();
            try
            {
                pet.cloneThrough = (window, on) => applied[window] = on;
                pet.hologramItem = menu ?? pet.HologramMenu();
                pet.SetHologram(false);
                PixelSize size = new PixelSize((int)Math.Ceiling(pet.body.Bounds.Width), (int)Math.Ceiling(pet.body.Bounds.Height));
                Func<int[]> pixels = () =>
                {
                    using (RenderTargetBitmap image = new RenderTargetBitmap(size))
                    using (WriteableBitmap copy = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul))
                    {
                        image.Render(pet.body);
                        using (ILockedFramebuffer frame = copy.Lock())
                        {
                            image.CopyPixels(frame, AlphaFormat.Premul);
                            int[] made = new int[size.Width * size.Height];
                            for (int y = 0; y < size.Height; y++) Marshal.Copy(IntPtr.Add(frame.Address, y * frame.RowBytes), made, y * size.Width, size.Width);
                            return made;
                        }
                    }
                };
                int[] before = pixels();
                pet.SetHologram(true);
                int[] after = pixels();
                int at = Array.FindIndex(before, pixel => (uint)pixel >> 24 > 240);
                if (at < 0 || before[at] == after[at] || ((uint)after[at] >> 24) < 140 || ((uint)after[at] >> 24) > 154) throw new Exception("Hologram did not tint a drawn sprite at 60 percent opacity. at " + at + " of " + before.Length + ", size " + size + ", before " + (at < 0 ? "none" : before[at].ToString("X8")) + ", after " + (at < 0 ? "none" : after[at].ToString("X8")) + ", most opaque before " + before.Max(pixel => (uint)pixel >> 24) + ", after " + after.Max(pixel => (uint)pixel >> 24) + ", shown " + (pet.body.Shown == null ? "null" : pet.body.Shown.Title) + " cell " + pet.body.Cell + ", arriving " + pet.Arriving + ", falling " + pet.Falling);
                if (applied.Count != 3 || !applied[pet] || !applied[pet.Talk.Pill] || !applied[pet.Talk.Bubble]) throw new Exception("Hologram did not pass clicks through exactly her body, pill and bubble.");
                using (var stream = new System.IO.MemoryStream())
                {
                    var serializer = new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Settings));
                    serializer.WriteObject(stream, pet.Saved);
                    stream.Position = 0;
                    if (!((Settings)serializer.ReadObject(stream)).Hologram) throw new Exception("Hologram did not survive saved settings.");
                }
                if ((string)pet.hologramItem.Header != "Hologram" || !pet.hologramItem.IsChecked) throw new Exception("The Hologram menu switch was lost.");
                pet.hologramItem.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
                if (pet.Saved.Hologram || pet.hologramItem.IsChecked || !before.SequenceEqual(pixels()) || applied.Values.Any(on => on)) throw new Exception("The Hologram menu did not restore her pixels and clicks.");
                if (new Settings().Hologram || HologramArt.Tint(0) != 0) throw new Exception("Hologram changed defaults or transparent pixels.");
                Hands.OnMac = true;
                pet.riding = true;
                pet.SetHologram(true);
                pet.SetHologram(false);
                if (pet.Saved.Hologram || pet.hologramItem.IsChecked || !applied[pet] || applied[pet.Talk.Pill] || applied[pet.Talk.Bubble]) throw new Exception("Turning hologram off during a Mac ride stopped passing clicks through her body.");
                checks.Add("PASS turning hologram off during a Mac ride keeps her body transparent to clicks until the ride ends");
            }
            finally
            {
                pet.Saved.Hologram = saved;
                Hands.OnMac = mac; pet.riding = riding;
                pet.body.HologramOn = drawn;
                pet.body.Hologram.Dispose();
                pet.body.HologramMouth.Dispose();
                pet.hologramItem = menu;
                if (menu != null) menu.IsChecked = saved;
                pet.cloneThrough = through;
                pet.body.InvalidateVisual();
                pet.Save();
            }
            checks.Add("PASS hologram: drawn sprite pixels tint at 60 percent, off restores them, three passive surfaces pass clicks, and saved settings retain the switch without native calls");
        }
    }
}
