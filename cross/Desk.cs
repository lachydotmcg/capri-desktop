using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Avalonia;

namespace CapriDesktop
{
    internal sealed class Spot
    {
        internal long Window;
        internal PixelRect Bounds;
        internal bool Seat;
        internal bool Solid;
    }

    internal abstract class Desk
    {
        internal List<PixelRect> Screens = new List<PixelRect>();
        internal double Scale = 1;

        internal static Desk Make()
        {
            try
            {
                if (OperatingSystem.IsWindows()) return new WindowsDesk();
                if (OperatingSystem.IsMacOS()) return new MacDesk();
                if (OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") == null && Environment.GetEnvironmentVariable("DISPLAY") != null) return new X11Desk();
            }
            catch (Exception) { }
            return new BareDesk();
        }

        internal abstract string Name { get; }
        internal virtual double Points { get { return 0; } }
        internal abstract List<Spot> Windows(ICollection<long> skip);
        internal abstract Spot Find(long window);
        internal abstract bool Alive(long window);
        internal virtual string Debug() { return ""; }
        // Where the mouse is on the whole desktop, in pixels, or null where the desktop won't say (Wayland).
        internal virtual PixelPoint? Pointer() { return null; }

        // Asks the window manager not to give this window the keyboard when it's shown.
        internal virtual void Unfocused(long window) { }

        protected bool Fills(PixelRect bounds)
        {
            foreach (PixelRect screen in Screens) if (bounds.X <= screen.X && bounds.Y <= screen.Y && bounds.Right >= screen.Right && bounds.Bottom >= screen.Bottom) return true;
            return false;
        }

        // A window covering a whole screen, like a full-screen game or video.
        internal bool Covering(List<Spot> windows)
        {
            foreach (Spot spot in windows) if (spot.Solid && Fills(spot.Bounds)) return true;
            return false;
        }

        protected Spot Make(long window, PixelRect bounds, bool seat)
        {
            bool big = bounds.Width >= 120 && bounds.Height >= 60;
            return new Spot { Window = window, Bounds = bounds, Seat = seat && big && !Fills(bounds), Solid = big };
        }
    }

    internal sealed class BareDesk : Desk
    {
        internal override string Name { get { return "screen edges only"; } }
        internal override List<Spot> Windows(ICollection<long> skip) { return new List<Spot>(); }
        internal override Spot Find(long window) { return null; }
        internal override bool Alive(long window) { return false; }
    }

    internal sealed class WindowsDesk : Desk
    {
        private delegate bool Visit(IntPtr window, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] private struct Box { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(Visit visit, IntPtr data);
        [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")] private static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr window, StringBuilder name, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out Box value, int size);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);
        private static readonly string[] Shell = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd", "#32768", "tooltips_class32" };

        internal override string Name { get { return "Windows"; } }

        internal override List<Spot> Windows(ICollection<long> skip)
        {
            List<Spot> found = new List<Spot>();
            Visit visit = delegate(IntPtr window, IntPtr data)
            {
                if (skip.Contains(window.ToInt64())) return true;
                Spot spot = Look(window);
                if (spot != null) found.Add(spot);
                return true;
            };
            EnumWindows(visit, IntPtr.Zero);
            GC.KeepAlive(visit);
            return found;
        }

        internal override Spot Find(long window) { return Look(new IntPtr(window)); }

        internal override bool Alive(long window) { return IsWindow(new IntPtr(window)); }

        private Spot Look(IntPtr window)
        {
            if (!IsWindow(window) || !IsWindowVisible(window) || IsIconic(window)) return null;
            int cloaked;
            if (DwmGetWindowAttribute(window, 14, out cloaked, 4) == 0 && cloaked != 0) return null;
            int extended = GetWindowLong(window, -20);
            if ((extended & 0x20) != 0 || (extended & 0x08080000) == 0x08080000) return null;
            StringBuilder kind = new StringBuilder(128);
            GetClassName(window, kind, kind.Capacity);
            if (Array.IndexOf(Shell, kind.ToString()) >= 0) return null;
            Box box;
            if (DwmGetWindowAttribute(window, 9, out box, 16) != 0 && !GetWindowRect(window, out box)) return null;
            PixelRect bounds = new PixelRect(box.Left, box.Top, box.Right - box.Left, box.Bottom - box.Top);
            if (bounds.Width <= 0 || bounds.Height <= 0) return null;
            bool tool = (extended & 0x80) != 0 && (extended & 0x40000) == 0;
            int style = GetWindowLong(window, -16);
            Spot spot = Make(window.ToInt64(), bounds, !tool && !IsZoomed(window));
            spot.Solid = spot.Solid && !tool && (style & 0x00C00000) == 0x00C00000;
            return spot;
        }
    }

    internal sealed class MacDesk : Desk
    {
        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const uint OnScreenOnly = 1;
        private const uint IncludingWindow = 8;
        private const uint ExcludeDesktop = 16;
        [StructLayout(LayoutKind.Sequential)] private struct Area { internal double X, Y, Width, Height; }
        [DllImport(Graphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
        [DllImport(Graphics)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CGRectMakeWithDictionaryRepresentation(IntPtr dictionary, out Area rect);
        [DllImport(Graphics)] private static extern uint CGMainDisplayID();
        [DllImport(Graphics)] private static extern Area CGDisplayBounds(uint display);
        [DllImport(Foundation)] private static extern long CFArrayGetCount(IntPtr array);
        [DllImport(Foundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
        [DllImport(Foundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, long type, out long value);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, long type, out double value);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFBooleanGetValue(IntPtr boolean);
        [DllImport(Foundation)] private static extern void CFRelease(IntPtr value);
        [StructLayout(LayoutKind.Sequential)] private struct Place { internal double X, Y; }
        [DllImport(Graphics)] private static extern IntPtr CGEventCreate(IntPtr source);
        [DllImport(Graphics)] private static extern Place CGEventGetLocation(IntPtr e);
        private readonly IntPtr number;
        private readonly IntPtr layer;
        private readonly IntPtr frame;
        private readonly IntPtr owner;
        private readonly IntPtr alpha;
        private readonly IntPtr onscreen;
        private readonly long self = Environment.ProcessId;

        internal MacDesk()
        {
            IntPtr library = NativeLibrary.Load(Graphics);
            number = Key(library, "kCGWindowNumber");
            layer = Key(library, "kCGWindowLayer");
            frame = Key(library, "kCGWindowBounds");
            owner = Key(library, "kCGWindowOwnerPID");
            alpha = Key(library, "kCGWindowAlpha");
            onscreen = Key(library, "kCGWindowIsOnscreen");
        }

        private static IntPtr Key(IntPtr library, string name) { return Marshal.ReadIntPtr(NativeLibrary.GetExport(library, name)); }

        internal override string Name { get { return "macOS"; } }

        internal override double Points { get { return CGDisplayBounds(CGMainDisplayID()).Width; } }

        internal override PixelPoint? Pointer()
        {
            IntPtr e = CGEventCreate(IntPtr.Zero);
            if (e == IntPtr.Zero) return null;
            try
            {
                Place at = CGEventGetLocation(e);
                return new PixelPoint((int)Math.Round(at.X * Scale), (int)Math.Round(at.Y * Scale));
            }
            finally { CFRelease(e); }
        }

        internal override List<Spot> Windows(ICollection<long> skip) { return List(OnScreenOnly | ExcludeDesktop, 0, skip, true); }

        internal override Spot Find(long window)
        {
            foreach (Spot spot in List(IncludingWindow, (uint)window, new long[0], true)) if (spot.Window == window) return spot;
            return null;
        }

        internal override bool Alive(long window)
        {
            foreach (Spot spot in List(IncludingWindow, (uint)window, new long[0], false)) if (spot.Window == window) return true;
            return false;
        }

        private List<Spot> List(uint option, uint relative, ICollection<long> skip, bool showing)
        {
            List<Spot> found = new List<Spot>();
            IntPtr array = CGWindowListCopyWindowInfo(option, relative);
            if (array == IntPtr.Zero) return found;
            try
            {
                long count = CFArrayGetCount(array);
                for (long i = 0; i < count; i++)
                {
                    IntPtr info = CFArrayGetValueAtIndex(array, i);
                    long id = Whole(info, number);
                    long level = Whole(info, layer);
                    if (level < 0 || level > 3 || Whole(info, owner) == self || skip.Contains(id)) continue;
                    IntPtr shown = CFDictionaryGetValue(info, onscreen);
                    if (showing && (shown == IntPtr.Zero || !CFBooleanGetValue(shown) || Real(info, alpha) <= 0.05)) continue;
                    IntPtr box = CFDictionaryGetValue(info, frame);
                    Area area;
                    if (box == IntPtr.Zero || !CGRectMakeWithDictionaryRepresentation(box, out area)) continue;
                    PixelRect bounds = new PixelRect((int)Math.Round(area.X * Scale), (int)Math.Round(area.Y * Scale), (int)Math.Round(area.Width * Scale), (int)Math.Round(area.Height * Scale));
                    if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                    found.Add(Make(id, bounds, true));
                }
            }
            finally { CFRelease(array); }
            return found;
        }

        internal override string Debug()
        {
            List<string> parts = new List<string>();
            IntPtr array = CGWindowListCopyWindowInfo(OnScreenOnly | ExcludeDesktop, 0);
            if (array == IntPtr.Zero) return "no window info";
            try
            {
                long count = CFArrayGetCount(array);
                for (long i = 0; i < count && parts.Count < 12; i++)
                {
                    IntPtr info = CFArrayGetValueAtIndex(array, i);
                    IntPtr box = CFDictionaryGetValue(info, frame);
                    Area area = new Area();
                    if (box != IntPtr.Zero) CGRectMakeWithDictionaryRepresentation(box, out area);
                    parts.Add("#" + Whole(info, number) + " layer " + Whole(info, layer) + " pid " + Whole(info, owner) + " alpha " + Real(info, alpha).ToString("0.##") + " " + area.X + "," + area.Y + " " + area.Width + "x" + area.Height);
                }
                return count + " raw (self " + self + "): " + string.Join("; ", parts);
            }
            finally { CFRelease(array); }
        }

        private static long Whole(IntPtr info, IntPtr key)
        {
            IntPtr value = CFDictionaryGetValue(info, key);
            long result;
            return value != IntPtr.Zero && CFNumberGetValue(value, 4, out result) ? result : -1;
        }

        private static double Real(IntPtr info, IntPtr key)
        {
            IntPtr value = CFDictionaryGetValue(info, key);
            double result;
            return value != IntPtr.Zero && CFNumberGetValue(value, 6, out result) ? result : 1;
        }
    }

    internal sealed class X11Desk : Desk
    {
        private const string Library = "libX11.so.6";
        private delegate int Handler(IntPtr display, IntPtr error);
        [DllImport(Library)] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport(Library)] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport(Library)] private static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
        [DllImport(Library)] private static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr offset, IntPtr length, bool delete, IntPtr type, out IntPtr actualType, out int actualFormat, out IntPtr items, out IntPtr after, out IntPtr data);
        [DllImport(Library)] private static extern int XFree(IntPtr data);
        [DllImport(Library)] private static extern int XGetGeometry(IntPtr display, IntPtr drawable, out IntPtr root, out int x, out int y, out uint width, out uint height, out uint border, out uint depth);
        [DllImport(Library)] private static extern bool XTranslateCoordinates(IntPtr display, IntPtr from, IntPtr to, int x, int y, out int toX, out int toY, out IntPtr child);
        [DllImport(Library)] private static extern IntPtr XSetErrorHandler(IntPtr handler);
        [DllImport(Library)] private static extern int XSync(IntPtr display, bool discard);
        [DllImport(Library)] private static extern bool XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child, out int rootX, out int rootY, out int x, out int y, out uint mask);
        [DllImport(Library)] private static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int elements);
        [DllImport(Library)] private static extern int XFlush(IntPtr display);
        private static Handler handler;
        private static IntPtr previous;
        private static IntPtr ours;
        private readonly IntPtr display;
        private readonly IntPtr root;
        private readonly Dictionary<string, IntPtr> atoms = new Dictionary<string, IntPtr>();

        internal X11Desk()
        {
            display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) throw new InvalidOperationException("No X display.");
            ours = display;
            handler = delegate(IntPtr where, IntPtr error)
            {
                if (where == ours || previous == IntPtr.Zero) return 0;
                return Marshal.GetDelegateForFunctionPointer<Handler>(previous)(where, error);
            };
            previous = XSetErrorHandler(Marshal.GetFunctionPointerForDelegate(handler));
            root = XDefaultRootWindow(display);
        }

        internal override string Name { get { return "X11"; } }

        internal override PixelPoint? Pointer()
        {
            IntPtr top;
            IntPtr child;
            int x;
            int y;
            int inX;
            int inY;
            uint mask;
            return XQueryPointer(display, root, out top, out child, out x, out y, out inX, out inY, out mask) ? new PixelPoint(x, y) : (PixelPoint?)null;
        }

        // Under EWMH a user time of 0 means "don't focus me when I'm mapped". 6 is XA_CARDINAL; 0 replaces the property.
        internal override void Unfocused(long window)
        {
            XChangeProperty(display, new IntPtr(window), Atom("_NET_WM_USER_TIME"), new IntPtr(6), 32, 0, new[] { IntPtr.Zero }, 1);
            XFlush(display);
        }

        private IntPtr Atom(string name)
        {
            IntPtr atom;
            if (!atoms.TryGetValue(name, out atom)) { atom = XInternAtom(display, name, false); atoms[name] = atom; }
            return atom;
        }

        private long[] Property(IntPtr window, string name)
        {
            IntPtr type;
            int format;
            IntPtr items;
            IntPtr after;
            IntPtr data;
            if (XGetWindowProperty(display, window, Atom(name), IntPtr.Zero, new IntPtr(4096), false, IntPtr.Zero, out type, out format, out items, out after, out data) != 0 || data == IntPtr.Zero) return null;
            try
            {
                if (format != 32) return null;
                long[] values = new long[items.ToInt64()];
                for (int i = 0; i < values.Length; i++) values[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64();
                return values;
            }
            finally { XFree(data); }
        }

        internal override List<Spot> Windows(ICollection<long> skip)
        {
            List<Spot> found = new List<Spot>();
            long[] stack = Property(root, "_NET_CLIENT_LIST_STACKING");
            if (stack == null) return found;
            long[] current = Property(root, "_NET_CURRENT_DESKTOP");
            for (int i = stack.Length - 1; i >= 0; i--)
            {
                if (skip.Contains(stack[i])) continue;
                Spot spot = Look(new IntPtr(stack[i]), current != null && current.Length > 0 ? current[0] : -1);
                if (spot != null) found.Add(spot);
            }
            return found;
        }

        internal override Spot Find(long window)
        {
            long[] clients = Property(root, "_NET_CLIENT_LIST");
            if (clients != null && Array.IndexOf(clients, window) < 0) return null;
            long[] current = Property(root, "_NET_CURRENT_DESKTOP");
            return Look(new IntPtr(window), current != null && current.Length > 0 ? current[0] : -1);
        }

        internal override bool Alive(long window)
        {
            IntPtr parent;
            int x;
            int y;
            uint width;
            uint height;
            uint border;
            uint depth;
            return XGetGeometry(display, new IntPtr(window), out parent, out x, out y, out width, out height, out border, out depth) != 0;
        }

        private Spot Look(IntPtr window, long desktop)
        {
            long[] where = Property(window, "_NET_WM_DESKTOP");
            if (desktop >= 0 && where != null && where.Length > 0 && where[0] != desktop && (where[0] & 0xFFFFFFFF) != 0xFFFFFFFF) return null;
            long[] kinds = Property(window, "_NET_WM_WINDOW_TYPE");
            if (kinds != null)
                foreach (long kind in kinds)
                    if (kind != Atom("_NET_WM_WINDOW_TYPE_NORMAL").ToInt64() && kind != Atom("_NET_WM_WINDOW_TYPE_DIALOG").ToInt64()) return null;
            bool seat = true;
            long[] states = Property(window, "_NET_WM_STATE");
            if (states != null)
            {
                bool tall = false;
                bool wide = false;
                foreach (long state in states)
                {
                    if (state == Atom("_NET_WM_STATE_HIDDEN").ToInt64()) return null;
                    if (state == Atom("_NET_WM_STATE_FULLSCREEN").ToInt64()) seat = false;
                    if (state == Atom("_NET_WM_STATE_MAXIMIZED_VERT").ToInt64()) tall = true;
                    if (state == Atom("_NET_WM_STATE_MAXIMIZED_HORZ").ToInt64()) wide = true;
                }
                if (tall && wide) seat = false;
            }
            IntPtr parent;
            int x;
            int y;
            uint width;
            uint height;
            uint border;
            uint depth;
            if (XGetGeometry(display, window, out parent, out x, out y, out width, out height, out border, out depth) == 0) return null;
            int left;
            int top;
            IntPtr child;
            if (!XTranslateCoordinates(display, window, root, 0, 0, out left, out top, out child)) return null;
            int right = left + (int)width;
            int bottom = top + (int)height;
            long[] frame = Property(window, "_NET_FRAME_EXTENTS");
            if (frame != null && frame.Length == 4) { left -= (int)frame[0]; right += (int)frame[1]; top -= (int)frame[2]; bottom += (int)frame[3]; }
            long[] shadow = Property(window, "_GTK_FRAME_EXTENTS");
            if (shadow != null && shadow.Length == 4) { left += (int)shadow[0]; right -= (int)shadow[1]; top += (int)shadow[2]; bottom -= (int)shadow[3]; }
            if (right <= left || bottom <= top) return null;
            return Make(window.ToInt64(), new PixelRect(left, top, right - left, bottom - top), seat);
        }
    }
}
