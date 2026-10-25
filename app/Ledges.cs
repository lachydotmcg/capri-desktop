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

        private static readonly string[] Shell = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "tooltips_class32" };
        private const int IconInset = 2;

        private sealed class Shown
        {
            internal IntPtr Window;
            internal Rectangle Bounds;
            internal bool Seat;
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

        internal static bool Fullscreen(ICollection<IntPtr> skip)
        {
            IntPtr active = Native.GetForegroundWindow();
            Rectangle bounds;
            if (active == IntPtr.Zero || skip.Contains(active) || !Showing(active, out bounds)) return false;
            Rectangle monitor = Screen.FromHandle(active).Bounds;
            return MotionPhysics.Covers(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom,
                monitor.Left, monitor.Top, monitor.Right, monitor.Bottom);
        }

        internal static bool Alive(IntPtr window) { return IsWindow(window); }

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
            int extended = Native.GetWindowLong(window, -20);
            if ((extended & 0x80) != 0 && (extended & 0x40000) == 0) return false;
            if (IsZoomed(window) || bounds.Width < 120 || bounds.Height < 60) return false;
            return !bounds.Contains(Screen.FromRectangle(bounds).Bounds);
        }

        private static List<Shown> Windows(ICollection<IntPtr> skip)
        {
            List<Shown> found = new List<Shown>();
            Visit visit = delegate(IntPtr window, IntPtr data)
            {
                Rectangle bounds;
                if (!skip.Contains(window) && Showing(window, out bounds)) found.Add(new Shown { Window = window, Bounds = bounds, Seat = Seatable(window, bounds) });
                return true;
            };
            EnumWindows(visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return found;
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

        internal static List<Ledge> Find(ICollection<IntPtr> skip, IEnumerable<Rectangle> icons)
        {
            List<Ledge> found = new List<Ledge>();
            List<Rectangle> above = new List<Rectangle>();
            foreach (Shown window in Windows(skip))
            {
                if (window.Seat)
                    foreach (int[] span in Open(window.Bounds.Left, window.Bounds.Right, window.Bounds.Top, above))
                        found.Add(new Ledge { Window = window.Window, Bounds = window.Bounds, Left = span[0], Right = span[1], Y = window.Bounds.Top });
                above.Add(window.Bounds);
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
            bool covered = false;
            Visit visit = delegate(IntPtr window, IntPtr data)
            {
                if (window == seat.Window) return false;
                Rectangle bounds;
                if (!skip.Contains(window) && Showing(window, out bounds) && Solid(window, bounds) && x >= bounds.Left && x < bounds.Right && seat.Y >= bounds.Top && seat.Y < bounds.Bottom) { covered = true; return false; }
                return true;
            };
            EnumWindows(visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return covered;
        }

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
