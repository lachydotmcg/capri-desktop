using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    // One desktop icon: where it is in the list view and the name she matches it back by (F72, Lachy: "move around
    // shortcuts/icons temporarily and or make them dissapear"). Her prank only ever reads and moves; it never touches a file.
    internal sealed class IconSpot
    {
        internal readonly int Index;
        internal readonly string Name;
        internal readonly Point At;
        internal IconSpot(int index, string name, Point at) { Index = index; Name = name; At = at; }
    }

    // The desktop's own icons, read and moved through the shell's view of the desktop (IFolderView), the documented way in, so
    // nothing of Explorer's is opened or written to. Positions are 32-bit, so an icon on a monitor left of the main one isn't
    // truncated. The smoke test points Open at a fake view of its own, so this never runs on the real desktop.
    internal static class DesktopIcons
    {
        internal static Func<IntPtr> Target = DefaultTarget;
        internal static Func<Batch, View> Open = delegate(Batch batch) { return new ShellView(batch); };

        private const int SW_HIDE = 0, SW_SHOWNA = 8;
        private const int PositionItem = 0x0080;
        private static int busy;
        internal static bool Busy { get { return System.Threading.Interlocked.CompareExchange(ref busy, 0, 0) != 0; } }

        [DllImport("shlwapi.dll", ExactSpelling = true)] private static extern int StrRetToStrW(ref StrRet name, IntPtr item, out IntPtr text);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("kernel32.dll")] private static extern int GetCurrentThreadId();

        internal static IntPtr DefaultTarget() { return Ledges.DesktopListView(); }

        // Only the methods she calls matter, but every slot before them has to stay, in the SDK's order.
        [ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
        private interface IShellWindows
        {
            [PreserveSig] int get_Count(out int count);
            [PreserveSig] int Item([MarshalAs(UnmanagedType.Struct)] object index, [MarshalAs(UnmanagedType.IDispatch)] out object folder);
            [PreserveSig] int _NewEnum([MarshalAs(UnmanagedType.IUnknown)] out object value);
            [PreserveSig] int Register([MarshalAs(UnmanagedType.IDispatch)] object window, int handle, int kind, out int cookie);
            [PreserveSig] int RegisterPending(int thread, [MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object root, int kind, out int cookie);
            [PreserveSig] int Revoke(int cookie);
            [PreserveSig] int OnNavigate(int cookie, [MarshalAs(UnmanagedType.Struct)] ref object location);
            [PreserveSig] int OnActivated(int cookie, [MarshalAs(UnmanagedType.VariantBool)] bool active);
            [PreserveSig] int FindWindowSW([MarshalAs(UnmanagedType.Struct)] ref object location, [MarshalAs(UnmanagedType.Struct)] ref object root, int kind, out int handle, int options, [MarshalAs(UnmanagedType.IDispatch)] out object window);
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServiceProvider
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
        }

        [ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellBrowser
        {
            [PreserveSig] int GetWindow(out IntPtr window);
            [PreserveSig] int ContextSensitiveHelp([MarshalAs(UnmanagedType.Bool)] bool enter);
            [PreserveSig] int InsertMenusSB(IntPtr menu, IntPtr widths);
            [PreserveSig] int SetMenuSB(IntPtr menu, IntPtr reserved, IntPtr active);
            [PreserveSig] int RemoveMenusSB(IntPtr menu);
            [PreserveSig] int SetStatusTextSB([MarshalAs(UnmanagedType.LPWStr)] string text);
            [PreserveSig] int EnableModelessSB([MarshalAs(UnmanagedType.Bool)] bool enable);
            [PreserveSig] int TranslateAcceleratorSB(IntPtr message, ushort id);
            [PreserveSig] int BrowseObject(IntPtr item, uint flags);
            [PreserveSig] int GetViewStateStream(uint mode, [MarshalAs(UnmanagedType.Interface)] out object stream);
            [PreserveSig] int GetControlWindow(uint id, out IntPtr window);
            [PreserveSig] int SendControlMsg(uint id, uint message, UIntPtr wParam, IntPtr lParam, out IntPtr result);
            [PreserveSig] int QueryActiveShellView([MarshalAs(UnmanagedType.Interface)] out object view);
        }

        [ComImport, Guid("CDE725B0-CCC9-4519-917E-325D72FAB4CE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IFolderView
        {
            [PreserveSig] int GetCurrentViewMode(out uint mode);
            [PreserveSig] int SetCurrentViewMode(uint mode);
            [PreserveSig] int GetFolder(ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object folder);
            [PreserveSig] int Item(int index, out IntPtr item);
            [PreserveSig] int ItemCount(uint flags, out int count);
            [PreserveSig] int Items(uint flags, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object items);
            [PreserveSig] int GetSelectionMarkedItem(out int index);
            [PreserveSig] int GetFocusedItem(out int index);
            [PreserveSig] int GetItemPosition(IntPtr item, out Point at);
            [PreserveSig] int GetSpacing(ref Point spacing);
            [PreserveSig] int GetDefaultSpacing(out Point spacing);
            [PreserveSig] int GetAutoArrange();
            [PreserveSig] int SelectItem(int index, uint flags);
            [PreserveSig] int SelectAndPositionItems(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] items, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] Point[] points, uint flags);
        }

        [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellFolder
        {
            [PreserveSig] int ParseDisplayName(IntPtr window, IntPtr context, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out IntPtr item, ref uint attributes);
            [PreserveSig] int EnumObjects(IntPtr window, uint flags, [MarshalAs(UnmanagedType.Interface)] out object items);
            [PreserveSig] int BindToObject(IntPtr item, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
            [PreserveSig] int BindToStorage(IntPtr item, IntPtr context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
            [PreserveSig] int CompareIDs(IntPtr flags, IntPtr first, IntPtr second);
            [PreserveSig] int CreateViewObject(IntPtr window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out object value);
            [PreserveSig] int GetAttributesOf(uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] IntPtr[] items, ref uint attributes);
            [PreserveSig] int GetUIObjectOf(IntPtr window, uint count, [In, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] items, ref Guid iid, IntPtr reserved, [MarshalAs(UnmanagedType.Interface)] out object value);
            [PreserveSig] int GetDisplayNameOf(IntPtr item, uint flags, out StrRet name);
        }

        [StructLayout(LayoutKind.Explicit)]
        internal unsafe struct StrData
        {
            [FieldOffset(0)] internal IntPtr Text;
            [FieldOffset(0)] internal uint Offset;
            [FieldOffset(0)] internal fixed byte Bytes[260];
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct StrRet
        {
            internal uint Type;
            internal StrData Data;
        }

        internal interface View : IDisposable
        {
            bool AutoArrange();
            int Count();
            IconSpot Read(int index);
            void Move(int index, Point at, int flags);
        }

        internal sealed class Batch
        {
            internal readonly Stopwatch Clock = Stopwatch.StartNew();
            internal volatile bool Cancelled;
            internal bool Ok;
            internal void Check() { if (Cancelled || Clock.ElapsedMilliseconds >= 2000) throw new OperationCanceledException(); }
        }

        private sealed class ShellView : View
        {
            private readonly Batch batch;
            private object windows, desktop, browser, shell, folder;
            private IFolderView view;

            internal ShellView(Batch work)
            {
                batch = work;
                try
                {
                    batch.Check();
                    windows = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39"), true));
                    object location = 0, root = null;
                    int handle;
                    batch.Check();
                    Marshal.ThrowExceptionForHR(((IShellWindows)windows).FindWindowSW(ref location, ref root, 8, out handle, 1, out desktop));
                    Guid service = new Guid("4C96BE40-915C-11CF-99D3-00AA004AE837"), iid = typeof(IShellBrowser).GUID;
                    batch.Check();
                    Marshal.ThrowExceptionForHR(((IServiceProvider)desktop).QueryService(ref service, ref iid, out browser));
                    batch.Check();
                    Marshal.ThrowExceptionForHR(((IShellBrowser)browser).QueryActiveShellView(out shell));
                    batch.Check();
                    view = (IFolderView)shell;
                    iid = typeof(IShellFolder).GUID;
                    batch.Check();
                    Marshal.ThrowExceptionForHR(view.GetFolder(ref iid, out folder));
                }
                catch { Dispose(); throw; }
            }

            public bool AutoArrange()
            {
                batch.Check();
                int result = view.GetAutoArrange();
                Marshal.ThrowExceptionForHR(result);
                return result == 0;
            }

            public int Count()
            {
                int count;
                batch.Check();
                Marshal.ThrowExceptionForHR(view.ItemCount(2, out count));
                return count;
            }

            public IconSpot Read(int index)
            {
                IntPtr item = IntPtr.Zero, text = IntPtr.Zero;
                StrRet name = new StrRet();
                bool converted = false;
                try
                {
                    batch.Check();
                    Marshal.ThrowExceptionForHR(view.Item(index, out item));
                    if (item == IntPtr.Zero) throw new InvalidOperationException();
                    Point at;
                    batch.Check();
                    Marshal.ThrowExceptionForHR(view.GetItemPosition(item, out at));
                    batch.Check();
                    Marshal.ThrowExceptionForHR(((IShellFolder)folder).GetDisplayNameOf(item, 1, out name));
                    batch.Check();
                    int result = StrRetToStrW(ref name, item, out text);
                    converted = true;
                    Marshal.ThrowExceptionForHR(result);
                    if (text == IntPtr.Zero) throw new InvalidOperationException();
                    return new IconSpot(index, Marshal.PtrToStringUni(text), at);
                }
                finally
                {
                    if (!converted && name.Type == 0 && name.Data.Text != IntPtr.Zero) Marshal.FreeCoTaskMem(name.Data.Text);
                    if (text != IntPtr.Zero) Marshal.FreeCoTaskMem(text);
                    if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
                }
            }

            public void Move(int index, Point at, int flags)
            {
                IntPtr item = IntPtr.Zero;
                try
                {
                    batch.Check();
                    Marshal.ThrowExceptionForHR(view.Item(index, out item));
                    if (item == IntPtr.Zero) throw new InvalidOperationException();
                    batch.Check();
                    Marshal.ThrowExceptionForHR(view.SelectAndPositionItems(1, new[] { item }, new[] { at }, (uint)flags));
                }
                finally { if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item); }
            }

            public void Dispose()
            {
                object[] objects = { folder, shell, browser, desktop, windows };
                folder = shell = browser = desktop = windows = null;
                view = null;
                Exception failure = null;
                foreach (object value in objects)
                {
                    try { if (value != null) Marshal.ReleaseComObject(value); }
                    catch (Exception problem) { failure = problem; }
                }
                if (failure != null) throw failure;
            }
        }

        // One batch of shell calls on a thread of its own, so a frozen Explorer can't hold her up: she waits 2 seconds at most
        // and a batch that runs out counts as failed. Only one batch is ever out, even while its thread is still stuck there.
        // The thread is free-threaded because calls into Explorer come back about a third sooner than from an STA (75 icons read
        // in 60 ms against 95), and her window waits on every one.
        private static bool Run(IntPtr list, Action<View, Batch> action)
        {
            if (list == IntPtr.Zero || System.Threading.Interlocked.CompareExchange(ref busy, 1, 0) != 0) return false;
            Batch batch = new Batch();
            Func<Batch, View> open = Open;
            System.Threading.Thread worker = new System.Threading.Thread(delegate()
            {
                try
                {
                    batch.Check();
                    using (View view = open(batch))
                    {
                        batch.Check();
                        action(view, batch);
                        batch.Check();
                    }
                    batch.Check();
                    batch.Ok = true;
                }
                catch (Exception) { }
                finally { System.Threading.Interlocked.Exchange(ref busy, 0); }
            });
            try
            {
                worker.IsBackground = true;
                worker.SetApartmentState(System.Threading.ApartmentState.MTA);
                worker.Start();
            }
            catch (Exception) { System.Threading.Interlocked.Exchange(ref busy, 0); return false; }
            int left = Math.Max(0, 2000 - (int)batch.Clock.ElapsedMilliseconds);
            if (!worker.Join(left)) { batch.Cancelled = true; return false; }
            return batch.Ok && batch.Clock.ElapsedMilliseconds < 2000;
        }

        internal static List<IconSpot> Read(IntPtr list)
        {
            List<IconSpot> spots;
            TryRead(list, out spots);
            return spots;
        }

        internal static bool TryRead(IntPtr list, out List<IconSpot> spots)
        {
            List<IconSpot> read = new List<IconSpot>();
            bool ok = Run(list, delegate(View view, Batch batch)
            {
                int count = view.Count();
                if (count < 0) throw new InvalidOperationException();
                for (int i = 0; i < count; i++) { batch.Check(); read.Add(view.Read(i)); }
            });
            spots = ok ? read : new List<IconSpot>();
            return ok;
        }

        // Only the icons she's already moving, each looked up where it last was in the view, because reading a whole desktop
        // every step of a scatter holds her window up. One that isn't there under its name any more means the desktop changed,
        // so the whole desktop is read instead.
        internal static bool TryRead(IntPtr list, List<IconSpot> wanted, out List<IconSpot> spots)
        {
            List<IconSpot> asked = new List<IconSpot>(wanted), read = new List<IconSpot>();
            bool ok = Run(list, delegate(View view, Batch batch)
            {
                int count = view.Count();
                if (count < 0) throw new InvalidOperationException();
                foreach (IconSpot want in asked)
                {
                    batch.Check();
                    IconSpot now = want.Index >= 0 && want.Index < count ? view.Read(want.Index) : null;
                    if (now == null || now.Name != want.Name) { read.Clear(); break; }
                    read.Add(now);
                }
                if (read.Count == asked.Count) return;
                for (int i = 0; i < count; i++) { batch.Check(); read.Add(view.Read(i)); }
            });
            spots = ok ? read : new List<IconSpot>();
            return ok;
        }

        internal static bool Move(IntPtr list, int index, Point at)
        {
            List<KeyValuePair<int, Point>> one = new List<KeyValuePair<int, Point>>();
            one.Add(new KeyValuePair<int, Point>(index, at));
            return MoveMany(list, one) > 0;
        }

        internal static int MoveMany(IntPtr list, List<KeyValuePair<int, Point>> moves) { return MoveMany(list, moves, null); }

        // Puts a batch of icons where she wants them in one trip. Returns how many moved, or none when the trip ran out of time.
        // Given a list, the same trip also reads where each one landed once they've all moved.
        internal static int MoveMany(IntPtr list, List<KeyValuePair<int, Point>> moves, List<IconSpot> landed)
        {
            if (moves == null || moves.Count == 0) return 0;
            List<KeyValuePair<int, Point>> copy = new List<KeyValuePair<int, Point>>(moves);
            List<IconSpot> seen = new List<IconSpot>();
            int done = 0;
            bool ok = Run(list, delegate(View view, Batch batch)
            {
                foreach (KeyValuePair<int, Point> move in copy)
                {
                    batch.Check();
                    try { view.Move(move.Key, move.Value, PositionItem); }
                    catch (Exception) { break; }
                    done++;
                }
                if (landed == null || done != copy.Count) return;
                foreach (KeyValuePair<int, Point> move in copy) { batch.Check(); seen.Add(view.Read(move.Key)); }
            });
            if (ok && landed != null) landed.AddRange(seen);
            return ok ? done : 0;
        }

        // When Auto arrange icons is on, a move snaps straight back, so she only does the vanish instead.
        internal static bool TryAutoArrange(IntPtr list, out bool arrange)
        {
            bool value = false;
            bool ok = Run(list, delegate(View view, Batch batch) { value = view.AutoArrange(); });
            arrange = value;
            return ok;
        }

        internal static bool AutoArrange(IntPtr list)
        {
            bool arrange;
            return TryAutoArrange(list, out arrange) && arrange;
        }

        private static void Visibility(IntPtr list, int command)
        {
            if (list == IntPtr.Zero) return;
            int process;
            if (Native.GetWindowThreadProcessId(list, out process) == GetCurrentThreadId()) ShowWindow(list, command);
            else ShowWindowAsync(list, command);
        }

        internal static void Hide(IntPtr list) { Visibility(list, SW_HIDE); }

        internal static void Show(IntPtr list) { Visibility(list, SW_SHOWNA); }

        internal static bool Hidden(IntPtr list) { return list != IntPtr.Zero && !IsWindowVisible(list); }
    }

    // The gag itself: she scatters the desktop icons around for a while then puts every one back exactly, or she makes them all
    // vanish and brings them back after 10 to 20 seconds. Safety first: before any change she writes the originals to a file in
    // her app data, so a crash, a kill or Esc still gets them home, and her next start puts right anything that file still holds.
    // Icons someone moved by hand meanwhile are left where they put them.
    internal sealed class IconMischief
    {
        internal static string Marker;
        private const int Slack = 28;
        private const double StepGap = 0.3;

        private readonly Random random;
        private readonly Func<IntPtr> target;
        private readonly List<Moved> moved = new List<Moved>();
        private IntPtr list;
        private bool active, vanishing, sliding;
        private double nextStep, holdUntil, showAt;
        private int step, steps;

        // Her glitch while she scatters them (skipped on reduced motion). Her window sets it.
        internal Action Glitch;

        internal IconMischief(Func<IntPtr> aim, int seed) { target = aim; random = new Random(seed); }

        internal bool Active { get { return active; } }
        internal bool Vanishing { get { return vanishing; } }
        internal bool Sliding { get { return sliding; } }
        internal int Moves { get { return moved.Count; } }

        internal string[] Picked
        {
            get { string[] names = new string[moved.Count]; for (int i = 0; i < moved.Count; i++) names[i] = moved[i].Name; return names; }
        }

        private sealed class Moved
        {
            internal readonly string Name;
            internal int Index;
            internal readonly Point Orig, From, To;
            internal Point Last;
            internal Point? Pending;
            internal bool Released;
            internal Moved(string name, int index, Point orig, Point to) { Name = name; Index = index; Orig = orig; From = orig; To = to; Last = orig; }
        }

        // The right prank for the desktop as it is: a scatter when the icons can be moved, or a vanish when Auto arrange would
        // undo every move or there's nothing to move.
        internal bool Start(double now, bool motion)
        {
            if (active || Recover() < 0) return false;
            list = target();
            if (list == IntPtr.Zero) return false;
            bool arrange;
            if (!DesktopIcons.TryAutoArrange(list, out arrange)) return false;
            if (arrange) return Vanish(now);
            List<IconSpot> spots;
            if (!DesktopIcons.TryRead(list, out spots)) return false;
            if (spots.Count == 0) return Vanish(now);
            moved.Clear();
            Rectangle area = Area(spots);
            int want = Math.Min(spots.Count, 3 + random.Next(4));
            foreach (IconSpot spot in Pick(spots, want)) moved.Add(new Moved(spot.Name, spot.Index, spot.At, Scatter(area, spot.At)));
            if (moved.Count == 0) return Vanish(now);
            if (!MarkShuffle()) { moved.Clear(); return false; }
            active = true;
            vanishing = false;
            sliding = true;
            step = 0;
            steps = motion ? 4 + random.Next(4) : 1;
            nextStep = now;
            return true;
        }

        internal bool Vanish(double now)
        {
            if (active || Recover() < 0) return false;
            list = target();
            if (list == IntPtr.Zero) return false;
            if (!MarkVanish()) return false;
            active = true;
            vanishing = true;
            sliding = false;
            showAt = now + 10 + random.NextDouble() * 10;
            DesktopIcons.Hide(list);
            return true;
        }

        internal void Advance(double now)
        {
            if (!active) return;
            if (vanishing) { if (now >= showAt) PutBack(); return; }
            if (sliding)
            {
                if (now < nextStep) return;
                nextStep = now + StepGap;
                step++;
                SlideStep();
                if (active && step >= steps) { sliding = false; holdUntil = now + 4 + random.NextDouble() * 4; }
                return;
            }
            if (now >= holdUntil) PutBack();
        }

        private void SlideStep()
        {
            double frac = steps <= 0 ? 1 : Math.Min(1.0, (double)step / steps);
            List<IconSpot> wanted = new List<IconSpot>(), before;
            foreach (Moved m in moved) if (!m.Released) wanted.Add(new IconSpot(m.Index, m.Name, m.Last));
            if (!DesktopIcons.TryRead(list, wanted, out before)) { PutBack(); return; }
            List<KeyValuePair<int, Point>> batch = new List<KeyValuePair<int, Point>>();
            foreach (Moved m in moved)
            {
                if (m.Released) continue;
                IconSpot cur = Find(before, m.Name);
                // Gone, or dragged off where she left it: someone's hand is on it, so she lets that one be.
                if (cur == null || !Near(cur.At, m.Last)) { m.Released = true; continue; }
                m.Index = cur.Index;
                m.Pending = new Point(
                    (int)Math.Round(m.From.X + (m.To.X - m.From.X) * frac),
                    (int)Math.Round(m.From.Y + (m.To.Y - m.From.Y) * frac));
                batch.Add(new KeyValuePair<int, Point>(cur.Index, m.Pending.Value));
            }
            if (!MarkShuffle()) { PutBack(); return; }
            // Where they actually landed, so a desktop that snaps to a grid doesn't read as a hand moving them next step.
            List<IconSpot> after = new List<IconSpot>();
            if (DesktopIcons.MoveMany(list, batch, after) != batch.Count) { PutBack(); return; }
            foreach (Moved m in moved)
            {
                if (m.Released) continue;
                IconSpot cur = Find(after, m.Name);
                if (cur != null) { m.Last = cur.At; m.Pending = null; }
            }
            if (!MarkShuffle()) { PutBack(); return; }
            Action glitch = Glitch;
            if (glitch != null) glitch();
        }

        // Everything home. A vanished list view comes back; scattered icons go to where they started, unless a hand moved one off
        // where she left it since. Idempotent, so Esc, a quit and the gag's own end can all call it.
        internal void PutBack()
        {
            if (!active) return;
            active = false;
            sliding = false;
            try { Recover(); }
            catch (Exception problem) { Brain.Log("icons", -1, problem.Message); }
            vanishing = false;
            moved.Clear();
        }

        // A crash or a kill mid-gag would leave the icons scattered or the list view hidden, so her next start reads the file and
        // puts right whatever it holds: a hidden list view shown again, and every scattered icon still where she left it sent home.
        internal static int Recover()
        {
            if (Marker == null || !File.Exists(Marker)) return 0;
            string[] lines;
            try { lines = File.ReadAllLines(Marker); }
            catch (IOException) { return -1; }
            catch (UnauthorizedAccessException) { return -1; }
            if (lines.Length == 0) { Unmark(); return 0; }
            IntPtr list = DesktopIcons.Target();
            if (list == IntPtr.Zero) return -1;
            int done = 0;
            string kind = lines[0].Trim();
            if (kind == "vanish")
            {
                DesktopIcons.Show(list);
                if (DesktopIcons.Hidden(list)) return -1;
                done = 1;
            }
            else if (kind == "shuffle")
            {
                List<IconSpot> spots;
                if (!DesktopIcons.TryRead(list, out spots)) return -1;
                List<KeyValuePair<int, Point>> back = new List<KeyValuePair<int, Point>>();
                List<string> remaining = new List<string>();
                List<Moved> restoring = new List<Moved>();
                for (int i = 1; i < lines.Length; i++)
                {
                    string name;
                    Point orig, last;
                    Point? pending;
                    bool released;
                    if (!ParseLine(lines[i], out name, out orig, out last, out released, out pending) || released) continue;
                    IconSpot cur = Find(spots, name);
                    if (cur == null) { remaining.Add(lines[i]); continue; }
                    if (cur.At == orig) continue;
                    if (!Near(cur.At, last) && (!pending.HasValue || !Near(cur.At, pending.Value))) continue;
                    restoring.Add(new Moved(name, cur.Index, orig, orig) { Last = last, Pending = pending });
                    back.Add(new KeyValuePair<int, Point>(cur.Index, orig));
                }
                DesktopIcons.MoveMany(list, back);
                List<IconSpot> after;
                bool read = DesktopIcons.TryRead(list, out after);
                foreach (Moved m in restoring)
                {
                    IconSpot cur = read ? Find(after, m.Name) : null;
                    if (cur != null && cur.At == m.Orig) done++;
                    else remaining.Add(MarkLine(m.Name, m.Orig, m.Last, false, m.Pending));
                }
                if (remaining.Count > 0)
                {
                    Write("shuffle" + Environment.NewLine + string.Join(Environment.NewLine, remaining.ToArray()) + Environment.NewLine);
                    return -1;
                }
            }
            return Unmark() ? done : -1;
        }

        // One line a scattered icon: its name (so she matches it back by name, never by index), where it started, where she last
        // left it, and whether a hand has since taken it over.
        internal static string MarkLine(string name, Point orig, Point last, bool released, Point? pending = null)
        {
            string tag = Convert.ToBase64String(Encoding.UTF8.GetBytes(name == null ? "" : name));
            return tag + " " + orig.X + " " + orig.Y + " " + last.X + " " + last.Y + " " + (released ? "1" : "0")
                + (pending.HasValue ? " " + pending.Value.X + " " + pending.Value.Y : "");
        }

        private static bool ParseLine(string line, out string name, out Point orig, out Point last, out bool released, out Point? pending)
        {
            name = null;
            orig = Point.Empty;
            last = Point.Empty;
            released = false;
            pending = null;
            string[] words = (line == null ? "" : line).Split(' ');
            if (words.Length != 6 && words.Length != 8) return false;
            int ox, oy, lx, ly;
            if (!int.TryParse(words[1], out ox) || !int.TryParse(words[2], out oy) || !int.TryParse(words[3], out lx) || !int.TryParse(words[4], out ly)) return false;
            try { name = Encoding.UTF8.GetString(Convert.FromBase64String(words[0])); }
            catch (FormatException) { return false; }
            orig = new Point(ox, oy);
            last = new Point(lx, ly);
            released = words[5] == "1";
            if (words.Length == 8)
            {
                int px, py;
                if (!int.TryParse(words[6], out px) || !int.TryParse(words[7], out py)) return false;
                pending = new Point(px, py);
            }
            return true;
        }

        private bool MarkShuffle()
        {
            StringBuilder lines = new StringBuilder();
            lines.AppendLine("shuffle");
            foreach (Moved m in moved) lines.AppendLine(MarkLine(m.Name, m.Orig, m.Last, m.Released, m.Pending));
            return Write(lines.ToString());
        }

        private bool MarkVanish() { return Write("vanish" + Environment.NewLine); }

        private static bool Write(string body)
        {
            if (Marker == null) return false;
            string temp = Marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temp, body);
                if (File.Exists(Marker)) File.Replace(temp, Marker, null);
                else File.Move(temp, Marker);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            finally
            {
                try { File.Delete(temp); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static bool Unmark()
        {
            if (Marker == null) return true;
            try { File.Delete(Marker); return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static IconSpot Find(List<IconSpot> spots, string name)
        {
            foreach (IconSpot spot in spots) if (spot.Name == name) return spot;
            return null;
        }

        private static bool Near(Point a, Point b) { return Math.Abs(a.X - b.X) <= Slack && Math.Abs(a.Y - b.Y) <= Slack; }

        private static Rectangle Area(List<IconSpot> spots)
        {
            int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
            foreach (IconSpot spot in spots)
            {
                minX = Math.Min(minX, spot.At.X);
                minY = Math.Min(minY, spot.At.Y);
                maxX = Math.Max(maxX, spot.At.X);
                maxY = Math.Max(maxY, spot.At.Y);
            }
            minX = Math.Max(0, minX - 160);
            minY = Math.Max(0, minY - 120);
            return Rectangle.FromLTRB(minX, minY, Math.Max(minX + 1, maxX + 160), Math.Max(minY + 1, maxY + 120));
        }

        private List<IconSpot> Pick(List<IconSpot> spots, int want)
        {
            List<IconSpot> pool = new List<IconSpot>(spots);
            List<IconSpot> chosen = new List<IconSpot>();
            while (chosen.Count < want && pool.Count > 0)
            {
                int k = random.Next(pool.Count);
                chosen.Add(pool[k]);
                pool.RemoveAt(k);
            }
            return chosen;
        }

        private Point Scatter(Rectangle area, Point from)
        {
            Point to = from;
            for (int tries = 0; tries < 8; tries++)
            {
                int x = area.Left + random.Next(Math.Max(1, area.Width));
                int y = area.Top + random.Next(Math.Max(1, area.Height));
                to = new Point(x, y);
                if (Math.Abs(x - from.X) + Math.Abs(y - from.Y) >= 60) break;
            }
            return to;
        }
    }

    internal sealed partial class Companion
    {
        private IconMischief iconChaos;
        private readonly Timer iconTimer = new Timer();
        private readonly Timer iconRecoveryTimer = new Timer();
        private static readonly int[] IconRecoveryDelays = { 15000, 45000, 120000 };
        private int iconRecoveryStep;
        private double iconActAt = -1;
        private bool iconFirst;

        private void IconTick(object sender, EventArgs e)
        {
            IconMischief gag = iconChaos;
            if (gag == null) { iconTimer.Stop(); return; }
            if (cloneWindow == null && KeyHeld((int)Keys.Escape)) { ClearIcons(); return; }
            gag.Advance(clock.Elapsed.TotalSeconds);
            if (!gag.Active) { iconChaos = null; iconTimer.Stop(); BeginIconRecovery(); }
        }

        private void BeginIconRecovery()
        {
            iconRecoveryTimer.Stop();
            iconRecoveryStep = 0;
            if (quitting || IconMischief.Recover() >= 0) return;
            iconRecoveryTimer.Interval = IconRecoveryDelays[0];
            iconRecoveryTimer.Start();
        }

        private void IconRecoveryTick(object sender, EventArgs e)
        {
            if (iconChaos != null) return;
            if (IconMischief.Recover() >= 0 || ++iconRecoveryStep >= IconRecoveryDelays.Length) { iconRecoveryTimer.Stop(); return; }
            iconRecoveryTimer.Interval = IconRecoveryDelays[iconRecoveryStep];
        }

        private void SmokeIconFailures(List<string> checks, IntPtr handle, FakeIcons icons, string marker)
        {
            Action<string, int, Point> callWas = icons.Call;
            Func<IntPtr> targetWas = DesktopIcons.Target;
            Func<int, bool> keyWas = KeyHeld;
            Func<List<IconSpot>> read = delegate { return DesktopIcons.Read(handle); };
            List<IconSpot> homes = read();
            try
            {
                icons.Call = delegate(string action, int index, Point at) { if (action == "arrange") throw new Exception("The fake Auto arrange check failed."); };
                IconMischief unreadable = new IconMischief(DesktopIcons.Target, 1);
                if (unreadable.Start(0, false) || unreadable.Active || File.Exists(marker) || DesktopIcons.Hidden(handle)) throw new Exception("An icon gag started without reading Auto arrange.");
                icons.Call = callWas;
                checks.Add("PASS an unreadable Auto arrange state prevents a prank");

                foreach (bool hide in new[] { false, true })
                {
                    IconMischief.Marker = Path.Combine(marker + ".missing", "icons.txt");
                    IconMischief blocked = new IconMischief(DesktopIcons.Target, 1);
                    if ((hide ? blocked.Vanish(0) : blocked.Start(0, false)) || blocked.Active || DesktopIcons.Hidden(handle)) throw new Exception("An icon gag started without its first journal write.");
                }
                IconMischief.Marker = marker;
                IconMischief locked = new IconMischief(DesktopIcons.Target, 1);
                if (!locked.Start(0, false)) throw new Exception("The journal failure fixture did not start.");
                string original = File.ReadAllText(marker);
                using (FileStream held = new FileStream(marker, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    locked.Advance(0);
                    if (locked.Active || File.ReadAllText(marker) != original) throw new Exception("A failed atomic journal replacement changed the original journal.");
                    foreach (IconSpot spot in read()) if (spot.At != homes[spot.Index].At) throw new Exception("An icon moved after its journal write failed.");
                }
                if (Directory.GetFiles(Path.GetDirectoryName(marker), Path.GetFileName(marker) + ".*.tmp").Length != 0) throw new Exception("A failed journal replacement left its temporary file.");
                IconMischief.Recover();
                checks.Add("PASS an unwritable first icon journal prevents scatter and vanish; a failed atomic replacement preserves the old journal and prevents the next move");

                bool sawPending = false;
                string journalError = null;
                icons.Call = delegate(string action, int index, Point at)
                {
                    if (action == "move")
                    {
                        bool found = false;
                        string name = icons.Spots[index].Name;
                        string tag = Convert.ToBase64String(Encoding.UTF8.GetBytes(name)) + " ";
                        foreach (string line in File.ReadAllLines(marker))
                        {
                            if (!line.StartsWith(tag, StringComparison.Ordinal)) continue;
                            string[] words = line.Split(' ');
                            if (words.Length != 8 || int.Parse(words[6]) != at.X || int.Parse(words[7]) != at.Y) journalError = "An icon moved before its pending destination was journalled.";
                            found = sawPending = true;
                        }
                        if (!found) journalError = "An icon moved without a journal entry.";
                    }
                };
                IconMischief ordered = new IconMischief(DesktopIcons.Target, 1);
                if (!ordered.Start(0, false)) throw new Exception("The pending journal fixture did not start.");
                ordered.Advance(0);
                icons.Call = callWas;
                if (journalError != null) throw new Exception(journalError);
                if (!sawPending || !ordered.Active || ordered.Sliding) throw new Exception("The reduced motion scatter did not finish its only step.");
                ordered.PutBack();
                IconSpot first = homes[0], second = homes[1];
                Point last = new Point(first.At.X + 100, first.At.Y + 70), pending = new Point(first.At.X + 240, first.At.Y + 120);
                foreach (Point at in new[] { last, pending })
                {
                    DesktopIcons.Move(handle, first.Index, at);
                    File.WriteAllLines(marker, new[] { "shuffle", IconMischief.MarkLine(first.Name, first.At, last, false, pending) });
                    if (IconMischief.Recover() != 1 || read()[first.Index].At != first.At || File.Exists(marker)) throw new Exception("Crash recovery did not recognise both sides of a pending icon move.");
                }
                checks.Add("PASS each icon destination is journalled before the move and recovery recognises both its last and pending position");

                using (System.Threading.ManualResetEvent entered = new System.Threading.ManualResetEvent(false))
                using (System.Threading.ManualResetEvent release = new System.Threading.ManualResetEvent(false))
                {
                    IconMischief late = new IconMischief(DesktopIcons.Target, 1);
                    if (!late.Start(0, false)) throw new Exception("The late icon move fixture did not start.");
                    int moved = icons.Moves;
                    icons.Call = delegate(string action, int index, Point at)
                    {
                        if (action != "move") return;
                        entered.Set();
                        release.WaitOne(10000);
                    };
                    try
                    {
                        late.Advance(0);
                        if (!entered.WaitOne(0) || late.Active || !DesktopIcons.Busy || !File.Exists(marker)) throw new Exception("A timed-out prank discarded its pending recovery record.");
                    }
                    finally
                    {
                        release.Set();
                        if (!System.Threading.SpinWait.SpinUntil(delegate { return !DesktopIcons.Busy; }, 2000)) throw new Exception("The late icon move did not finish.");
                        icons.Call = callWas;
                    }
                    if (icons.Moves != moved + 1 || IconMischief.Recover() < 0 || File.Exists(marker)) throw new Exception("A late icon move could not be recovered.");
                    foreach (IconSpot spot in read()) if (spot.At != homes[spot.Index].At) throw new Exception("A late icon move was not put back.");
                }
                checks.Add("PASS a timed-out prank retains its pending marker and restores an in-flight move after the worker returns");

                File.WriteAllText(marker, "vanish" + Environment.NewLine);
                DesktopIcons.Hide(handle);
                DesktopIcons.Target = delegate { return IntPtr.Zero; };
                BeginIconRecovery();
                if (!File.Exists(marker) || !iconRecoveryTimer.Enabled || iconRecoveryTimer.Interval != 15000) throw new Exception("Missing Explorer lost its recovery journal or first startup retry.");
                IconRecoveryTick(null, EventArgs.Empty);
                if (!iconRecoveryTimer.Enabled || iconRecoveryTimer.Interval != 45000) throw new Exception("The 60 second icon recovery retry was lost.");
                IconRecoveryTick(null, EventArgs.Empty);
                if (!iconRecoveryTimer.Enabled || iconRecoveryTimer.Interval != 120000) throw new Exception("The 180 second icon recovery retry was lost.");
                DesktopIcons.Target = targetWas;
                IconRecoveryTick(null, EventArgs.Empty);
                if (iconRecoveryTimer.Enabled || File.Exists(marker) || DesktopIcons.Hidden(handle)) throw new Exception("A late desktop was not recovered by the startup timer.");

                File.WriteAllLines(marker, new[] { "shuffle", IconMischief.MarkLine(first.Name, first.At, last, false) });
                original = File.ReadAllText(marker);
                DesktopIcons.Target = delegate { return IntPtr.Zero; };
                if (IconMischief.Recover() >= 0 || File.ReadAllText(marker) != original) throw new Exception("A missing shuffle target lost its recovery record.");
                DesktopIcons.Target = targetWas;
                icons.Call = delegate(string action, int index, Point at) { if (action == "count") throw new Exception("The fake view cannot be read."); };
                if (IconMischief.Recover() >= 0 || File.ReadAllText(marker) != original) throw new Exception("An unreadable list view lost its recovery record.");
                icons.Call = callWas;
                IconMischief.Recover();

                IconMischief live = new IconMischief(DesktopIcons.Target, 1);
                if (!live.Start(0, false)) throw new Exception("The stale handle fixture did not start.");
                live.Advance(0);
                DesktopIcons.Target = delegate { return IntPtr.Zero; };
                live.PutBack();
                if (!File.Exists(marker)) throw new Exception("PutBack trusted its stale list view handle or discarded unresolved entries.");
                DesktopIcons.Target = targetWas;
                if (IconMischief.Recover() < 0 || File.Exists(marker)) throw new Exception("A live gag could not recover when its target returned.");

                DesktopIcons.Move(handle, first.Index, last);
                Point lastSecond = new Point(second.At.X + 130, second.At.Y + 90);
                DesktopIcons.Move(handle, second.Index, lastSecond);
                File.WriteAllLines(marker, new[] { "shuffle", IconMischief.MarkLine(first.Name, first.At, last, false), IconMischief.MarkLine(second.Name, second.At, lastSecond, false) });
                icons.Call = delegate(string action, int index, Point at)
                {
                    if (action == "move" && index == second.Index) throw new Exception("The second fake move failed.");
                };
                if (IconMischief.Recover() >= 0 || File.ReadAllLines(marker).Length != 2 || read()[first.Index].At != first.At) throw new Exception("A partial icon restoration lost or retained the wrong journal entries.");
                icons.Call = callWas;
                if (IconMischief.Recover() != 1 || read()[second.Index].At != second.At || File.Exists(marker)) throw new Exception("An unresolved icon could not be retried.");
                checks.Add("PASS missing and unreadable desktops retain recovery records; UI retries at 15, 60 and 180 seconds, PutBack reacquires its target, and partial restoration keeps only unresolved icons");

                foreach (bool hide in new[] { false, true })
                {
                    if (cloneWindow != null || !StartIconChaos(hide)) throw new Exception("The standalone Esc fixture did not start.");
                    if (!hide) iconChaos.Advance(clock.Elapsed.TotalSeconds);
                    KeyHeld = delegate(int key) { return key == (int)Keys.Escape; };
                    IconTick(null, EventArgs.Empty);
                    if (iconChaos != null || iconTimer.Enabled || DesktopIcons.Hidden(handle) || File.Exists(marker)) throw new Exception("Esc did not clear a standalone icon gag.");
                    foreach (IconSpot spot in read()) if (spot.At != homes[spot.Index].At) throw new Exception("Esc did not restore a scattered icon.");
                }
                checks.Add("PASS Esc restores standalone scatter and vanish gags without any clones");

                if (!StartIconChaos(true)) throw new Exception("The failed back-command fixture did not start.");
                DesktopIcons.Target = delegate { return IntPtr.Zero; };
                if (!IconPrank("back").StartsWith("Couldn't put all your icons back", StringComparison.Ordinal) || !File.Exists(marker)) throw new Exception("A failed live back-command claimed it restored the icons.");
                DesktopIcons.Target = targetWas;
                BeginIconRecovery();
                if (File.Exists(marker) || DesktopIcons.Hidden(handle)) throw new Exception("The failed back-command could not recover later.");
                checks.Add("PASS a live back-command reports unavailable Explorer honestly and retains its recovery record for later");

                foreach (bool hide in new[] { true, false })
                {
                    System.Threading.Tasks.Task changed = System.Threading.Tasks.Task.Run(delegate { if (hide) DesktopIcons.Hide(handle); else DesktopIcons.Show(handle); });
                    bool returned = changed.Wait(1000);
                    Application.DoEvents();
                    if (!changed.Wait(1000) || !returned || DesktopIcons.Hidden(handle) != hide) throw new Exception("Showing or hiding another thread's list view waited for that thread.");
                }
                checks.Add("PASS another thread can show and hide the fake list view without waiting for its blocked owner; same-thread visibility changes remain immediate");
            }
            finally
            {
                icons.Call = callWas;
                DesktopIcons.Target = targetWas;
                KeyHeld = keyWas;
                IconMischief.Marker = marker;
                ClearIcons();
                iconRecoveryTimer.Stop();
                DesktopIcons.Show(handle);
            }
        }

        // Her clones' per-frame beat: they chatter to each other, and once in a while one of them goes for the desktop icons.
        private void CloneTick(double now)
        {
            CloneTalk(now);
            IconChaosTick(now);
        }

        // One of chaos mode's random acts while her clones are out. Her first go comes about 5 seconds in, every time, and slides
        // some icons about (Lachy, 2 Nov: "start stealing icons after like 5 seconds"). After that she has a 1 in 3 chance every
        // 30 to 70 seconds to scatter or vanish them. Every icon still goes back.
        private void IconChaosTick(double now)
        {
            if (testing || cloneWindow == null || iconChaos != null) return;
            if (working != null || hidden || Computer.Streaming || Computer.GameOpen() != null) return;
            if (iconActAt < 0) { iconActAt = now + 4 + random.NextDouble() * 2; iconFirst = true; return; }
            if (now < iconActAt) return;
            iconActAt = now + 30 + random.NextDouble() * 40;
            bool first = iconFirst;
            iconFirst = false;
            if (!first && random.Next(3) != 0) return;
            StartIconChaos(!first && random.Next(3) == 0);
        }

        private bool StartIconChaos(bool hideOnly)
        {
            if (iconChaos != null) return false;
            IconMischief gag = new IconMischief(DesktopIcons.Target, random.Next());
            gag.Glitch = delegate { if (!testing && preferences.Motion) Animate("glitch"); };
            double now = clock.Elapsed.TotalSeconds;
            bool ok = hideOnly ? gag.Vanish(now) : gag.Start(now, preferences.Motion);
            if (!ok) return false;
            iconChaos = gag;
            iconTimer.Start();
            return true;
        }

        internal void ClearIcons()
        {
            IconMischief gag = iconChaos;
            iconChaos = null;
            iconTimer.Stop();
            // The next chaos session gets its own first go at the icons, about 5 seconds in.
            iconActAt = -1;
            if (gag != null)
            {
                try { gag.PutBack(); } catch (Exception problem) { Brain.Log("icons", -1, problem.Message); }
                BeginIconRecovery();
            }
        }

        // mess, hide or back. "back" always works: a running gag is cleared, otherwise anything left in the backup file is put right.
        internal string IconPrank(string action)
        {
            if (action == "back")
            {
                bool running = iconChaos != null;
                ClearIcons();
                int back = IconMischief.Recover();
                if (back < 0) return "Couldn't put all your icons back just now. I've kept their places to try again.";
                return back > 0 || running ? "Put your icons back, choom." : "Your icons are already where you left them.";
            }
            if (iconChaos != null) return "I'm already in there, choom. Say put my icons back when you've had enough.";
            bool hide = action == "hide";
            if (!StartIconChaos(hide)) return "Couldn't get at your desktop icons just now, choom.";
            if (hide || (iconChaos != null && iconChaos.Vanishing)) return "Poof. Your icons are gone... I'll put them back in a sec.";
            return "Messing with your icons, choom. Say put my icons back when you want them.";
        }

        private string IconTool(string action) { return InvokeRequired ? (string)Invoke(new Func<string, string>(IconPrank), action) : IconPrank(action); }
    }
}
