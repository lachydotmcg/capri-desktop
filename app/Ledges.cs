using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class Ledge
    {
        internal IntPtr Window;
        internal Rectangle Bounds;
        internal Rectangle Icon;
        internal bool Floor;
        internal bool Free;
        // A spot she took beside something full screen, given up once it's gone.
        internal bool Aside;
        internal int Left;
        internal int Right;
        internal int Y;
    }

    internal static class Ledges
    {
        private delegate bool Visit(IntPtr window, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Box { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(Visit visit, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string type, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string type, string title);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Box value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr window);
        [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point, int flags);
        [DllImport("user32.dll")] private static extern bool IsWindowArranged(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowPlacement(IntPtr window, ref Placement placement);
        [DllImport("user32.dll")] private static extern bool SetWindowPlacement(IntPtr window, ref Placement placement);
        [StructLayout(LayoutKind.Sequential)] private struct Placement { internal int Length, Flags, Show, MinX, MinY, MaxX, MaxY, Left, Top, Right, Bottom; }
        private static bool arrangedMissing;
        // Outside her tests the list of windows is kept for a second, because a few things a second ask for it.
        internal static bool Enabled;
        private static int scannedAt;
        private static List<Shown> windows;
        private static readonly object windowLock = new object();

        internal static void Invalidate() { lock (windowLock) windows = null; }

        private static readonly string[] Shell = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "tooltips_class32" };
        // Windows' own pop-ups, like Start, search and Alt+Tab, which her clones leave alone.
        private static readonly string[] Flyouts = { "Windows.UI.Core.CoreWindow", "XamlExplorerHostIslandWindow", "Shell_InputSwitchTopLevelWindow", "NotifyIconOverflowWindow", "TaskListThumbnailWnd" };
        private const int IconInset = 2;
        // Her mini player is a layered tool window like her bubbles, which the checks below pass over, yet she sits on it like a
        // window. Its plate, without the glow around it, is the edge.
        private static readonly object ownLock = new object();
        private static IntPtr ownSeat;
        private static Rectangle ownPlate;

        internal static void OwnSeat(IntPtr window, Rectangle plate) { lock (ownLock) { ownSeat = window; ownPlate = plate; } }

        private static bool Own(IntPtr window, out Rectangle plate)
        {
            lock (ownLock)
            {
                plate = ownPlate;
                return window != IntPtr.Zero && window == ownSeat && plate.Width > 0 && plate.Height > 0;
            }
        }

        private sealed class Shown
        {
            internal IntPtr Window;
            internal Rectangle Bounds;
            internal bool Seat;
            internal bool Solid;
        }

        private static string Kind(IntPtr window)
        {
            StringBuilder name = new StringBuilder(128);
            GetClassName(window, name, name.Capacity);
            return name.ToString();
        }

        internal static bool Showing(IntPtr window, out Rectangle bounds)
        {
            bounds = Rectangle.Empty;
            if (!IsWindow(window) || !IsWindowVisible(window) || IsIconic(window)) return false;
            Rectangle own;
            if (Own(window, out own)) { bounds = own; return true; }
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return false;
            int extended = Native.GetWindowLong(window, -20);
            if ((extended & 0x20) != 0 || (extended & 0x08080000) == 0x08080000) return false;
            if (Array.IndexOf(Shell, Kind(window)) >= 0) return false;
            Box box;
            if (DwmGetWindowAttribute(window, 9, out box, 16) != 0 && !GetWindowRect(window, out box)) return false;
            bounds = Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom);
            return bounds.Width > 0 && bounds.Height > 0;
        }

        internal static bool Alive(IntPtr window) { return IsWindow(window); }

        // A window her clones may grab and drag off: an app's own main window on screen, not full size, snapped or filling the
        // screen, not always on top, and not frozen, since a frozen one would never answer the move.
        internal static bool Haulable(IntPtr window, out Rectangle bounds)
        {
            if (!Showing(window, out bounds) || IsZoomed(window) || IsHungAppWindow(window) || Native.GetWindow(window, 4) != IntPtr.Zero || Arranged(window)) return false;
            if (Array.IndexOf(Flyouts, Kind(window)) >= 0) return false;
            int extended = Native.GetWindowLong(window, -20);
            if ((extended & 0x08) != 0 || ((extended & 0x80) != 0 && (extended & 0x40000) == 0)) return false;
            return bounds.Width >= 200 && bounds.Height >= 120 && !bounds.Contains(Screen.FromRectangle(bounds).Bounds);
        }

        // Every window her clones may grab, front first, leaving out the program with id skip (her own).
        internal static List<KeyValuePair<IntPtr, Rectangle>> Haulable(int skip)
        {
            List<KeyValuePair<IntPtr, Rectangle>> found = new List<KeyValuePair<IntPtr, Rectangle>>();
            Visit visit = delegate(IntPtr window, IntPtr data)
            {
                int process;
                Rectangle bounds;
                Native.GetWindowThreadProcessId(window, out process);
                if (process != skip && Haulable(window, out bounds) && GetWindowTextLength(window) > 0) found.Add(new KeyValuePair<IntPtr, Rectangle>(window, bounds));
                return true;
            };
            EnumWindows(visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return found;
        }

        // The whole window, invisible borders and all, which is what moving it goes by.
        internal static bool Outer(IntPtr window, out Rectangle bounds)
        {
            Box box;
            bool found = GetWindowRect(window, out box);
            bounds = found ? Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom) : Rectangle.Empty;
            return found;
        }

        // Snapped to a side or a corner. Moving one by code pulls it out of its snap group, so her clones leave it be. Before
        // Windows 11 there's no asking, but a snapped window's size isn't the size it goes back to.
        private static bool Arranged(IntPtr window)
        {
            if (!arrangedMissing)
            {
                try { return IsWindowArranged(window); }
                catch (EntryPointNotFoundException) { arrangedMissing = true; }
            }
            Placement placement = new Placement();
            placement.Length = Marshal.SizeOf(typeof(Placement));
            Box box;
            if (!GetWindowPlacement(window, ref placement) || placement.Show != 1 || !GetWindowRect(window, out box)) return false;
            return placement.Right - placement.Left != box.Right - box.Left || placement.Bottom - placement.Top != box.Bottom - box.Top;
        }

        // Whether a spot is on one of the screens there are now, so a window is never sent somewhere it can't be found.
        internal static bool OnScreen(Point point) { return MonitorFromPoint(point, 0) != IntPtr.Zero; }

        internal static bool Minimised(IntPtr window) { return IsIconic(window); }

        internal static bool FullSize(IntPtr window) { return IsZoomed(window); }

        internal static bool Frozen(IntPtr window) { return IsHungAppWindow(window); }

        // A minimised window's spot for when it's restored, moved right and down. Posted, so a frozen app can't hold her up.
        internal static void MoveRestored(IntPtr window, int right, int down)
        {
            Placement placement = new Placement();
            placement.Length = Marshal.SizeOf(typeof(Placement));
            if (!GetWindowPlacement(window, ref placement)) return;
            placement.Left += right;
            placement.Right += right;
            placement.Top += down;
            placement.Bottom += down;
            placement.Show = 7;
            placement.Flags |= 4;
            SetWindowPlacement(window, ref placement);
        }

        // Where Windows will put a minimised window back when it's restored.
        internal static bool RestoreSpot(IntPtr window, out Point spot)
        {
            Placement placement = new Placement();
            placement.Length = Marshal.SizeOf(typeof(Placement));
            bool found = GetWindowPlacement(window, ref placement);
            spot = found ? new Point(placement.Left, placement.Top) : Point.Empty;
            return found;
        }

        // Dots per inch on the window's screen, 96 at 100%.
        internal static int Dpi(IntPtr window)
        {
            try { int dpi = Native.GetDpiForWindow(window); return dpi > 0 ? dpi : 96; }
            catch (EntryPointNotFoundException) { return 96; }
        }

        private static bool Solid(IntPtr window, Rectangle bounds)
        {
            int style = Native.GetWindowLong(window, -16);
            int extended = Native.GetWindowLong(window, -20);
            if ((extended & 0x80) != 0 && (extended & 0x40000) == 0) return false;
            if ((style & 0x00C00000) != 0x00C00000) return false;
            return bounds.Width >= 120 && bounds.Height >= 60;
        }

        internal static bool Seatable(IntPtr window, Rectangle bounds)
        {
            Rectangle own;
            if (Own(window, out own)) return true;
            int extended = Native.GetWindowLong(window, -20);
            if ((extended & 0x80) != 0 && (extended & 0x40000) == 0) return false;
            if (IsZoomed(window) || bounds.Width < 120 || bounds.Height < 60) return false;
            return !bounds.Contains(Screen.FromRectangle(bounds).Bounds);
        }

        private static List<Shown> Windows(ICollection<IntPtr> skip)
        {
            lock (windowLock)
            {
                int now = Environment.TickCount;
                if (!Enabled || windows == null || unchecked((uint)(now - scannedAt)) >= 1000)
                {
                    List<Shown> found = new List<Shown>();
                    Visit visit = delegate(IntPtr window, IntPtr data)
                    {
                        Rectangle bounds;
                        if (Showing(window, out bounds)) found.Add(new Shown { Window = window, Bounds = bounds, Seat = Seatable(window, bounds), Solid = Solid(window, bounds) });
                        return true;
                    };
                    EnumWindows(visit, IntPtr.Zero);
                    GC.KeepAlive(visit);
                    windows = found;
                    scannedAt = now;
                }
                return windows.FindAll(shown => !skip.Contains(shown.Window));
            }
        }

        internal static List<int[]> Open(int left, int right, int y, IEnumerable<Rectangle> above)
        {
            List<int[]> spans = new List<int[]> { new[] { left, right } };
            foreach (Rectangle cover in above)
            {
                if (y < cover.Top || y >= cover.Bottom) continue;
                List<int[]> next = new List<int[]>();
                foreach (int[] span in spans)
                {
                    if (cover.Right <= span[0] || cover.Left >= span[1]) { next.Add(span); continue; }
                    if (cover.Left > span[0]) next.Add(new[] { span[0], cover.Left });
                    if (cover.Right < span[1]) next.Add(new[] { cover.Right, span[1] });
                }
                spans = next;
            }
            return spans;
        }

        internal static List<Ledge> Find(ICollection<IntPtr> skip, IEnumerable<Rectangle> icons, IntPtr under = default(IntPtr))
        {
            List<Ledge> found = new List<Ledge>();
            List<Rectangle> above = new List<Rectangle>();
            foreach (Shown window in Windows(skip))
            {
                if (window.Seat)
                    foreach (int[] span in Open(window.Bounds.Left, window.Bounds.Right, window.Bounds.Top, above))
                        found.Add(new Ledge { Window = window.Window, Bounds = window.Bounds, Left = span[0], Right = span[1], Y = window.Bounds.Top });
                if (window.Window != under) above.Add(window.Bounds);
            }
            foreach (Rectangle icon in icons)
                foreach (int[] span in Open(icon.Left, icon.Right, icon.Top + IconInset, above))
                    found.Add(new Ledge { Icon = icon, Left = span[0], Right = span[1], Y = icon.Top + IconInset });
            foreach (Screen screen in Screen.AllScreens)
                found.Add(new Ledge { Floor = true, Left = screen.WorkingArea.Left, Right = screen.WorkingArea.Right, Y = screen.WorkingArea.Bottom });
            return found;
        }

        internal static Ledge Below(IEnumerable<Ledge> ledges, Point hip, int over, int reach, int slack, int room)
        {
            Ledge best = null;
            foreach (Ledge ledge in ledges)
            {
                int drop = ledge.Y - hip.Y;
                if (drop < -over || drop > reach || ledge.Right - ledge.Left < 24) continue;
                if (hip.X < ledge.Left - slack || hip.X > ledge.Right + slack) continue;
                if (ledge.Y - room < Screen.FromPoint(new Point(hip.X, ledge.Y)).Bounds.Top) continue;
                if (best == null || Math.Abs(drop) < Math.Abs(best.Y - hip.Y)) best = ledge;
            }
            return best;
        }

        internal static bool Covered(Ledge seat, int x, ICollection<IntPtr> skip)
        {
            foreach (Shown window in Windows(new IntPtr[0]))
            {
                if (window.Window == seat.Window) return false;
                Rectangle bounds = window.Bounds;
                if (!skip.Contains(window.Window) && window.Solid && x >= bounds.Left && x < bounds.Right && seat.Y >= bounds.Top && seat.Y < bounds.Bottom) return true;
            }
            return false;
        }

        // The desktop's icon list view, for her icon prank. Same lookup her ledge-finding already does.
        internal static IntPtr DesktopListView() { return DesktopList(); }

        private static IntPtr DesktopList()
        {
            IntPtr view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
            if (view == IntPtr.Zero)
            {
                Visit visit = delegate(IntPtr window, IntPtr data)
                {
                    if (Kind(window) == "WorkerW") view = FindWindowEx(window, IntPtr.Zero, "SHELLDLL_DefView", null);
                    return view == IntPtr.Zero;
                };
                EnumWindows(visit, IntPtr.Zero);
                GC.KeepAlive(visit);
            }
            return view == IntPtr.Zero ? IntPtr.Zero : FindWindowEx(view, IntPtr.Zero, "SysListView32", null);
        }

        internal static List<Rectangle> Icons()
        {
            List<Rectangle> found = new List<Rectangle>();
            IntPtr list = DesktopList();
            if (list == IntPtr.Zero || !IsWindowVisible(list)) return found;
            try
            {
                CacheRequest cache = new CacheRequest();
                cache.Add(AutomationElement.BoundingRectangleProperty);
                using (cache.Activate())
                {
                    AutomationElementCollection items = AutomationElement.FromHandle(list).FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem));
                    foreach (AutomationElement item in items)
                    {
                        System.Windows.Rect bounds = item.Cached.BoundingRectangle;
                        if (!bounds.IsEmpty && bounds.Width >= 16 && bounds.Height >= 16) found.Add(new Rectangle((int)bounds.X, (int)bounds.Y, (int)bounds.Width, (int)bounds.Height));
                    }
                }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (COMException) { }
            return found;
        }
    }
}
