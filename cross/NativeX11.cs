using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace CapriDesktop
{
    // Avalonia 11.3.22 has no public override-redirect setter. Reuse the exact
    // connection boundary already required by ComposerInput. Requests join the
    // UI-owned X11 stream before Show: no second connection, round-trip, Sync,
    // or blocking compositor IPC is performed here.
    internal static class NativeX11
    {
        private static long nextGrab, currentGrab;
        private static IntPtr grabDisplay;
        internal static long OwnPointerGrab(IntPtr display) { grabDisplay = display; return currentGrab = ++nextGrab; }
        internal static void ReleasePointerGrab(IntPtr display, long lease)
        {
            if (lease == 0 || lease != currentGrab || display != grabDisplay) return;
            currentGrab = 0; grabDisplay = IntPtr.Zero;
            XUngrabPointer(display, IntPtr.Zero); XFlush(display);
        }
        [DllImport("libX11.so.6")] private static extern int XUngrabPointer(IntPtr display, IntPtr time);
        [DllImport("libX11.so.6")] private static extern int XFlush(IntPtr display);
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static (IntPtr Display, IntPtr Handle, object Info) Connection(TopLevel window)
        {
            var handle = window.TryGetPlatformHandle();
            if (handle?.HandleDescriptor != "XID") throw new NotSupportedException("Expected the pinned X11 platform.");
            object platform = typeof(TopLevel).GetProperty("PlatformImpl", Members)?.GetValue(window);
            object info = platform?.GetType().GetField("_x11", Members)?.GetValue(platform)
                ?? throw new NotSupportedException("Pinned Avalonia X11 connection contract changed.");
            IntPtr display = (IntPtr)(info.GetType().GetProperty("Display", Members)?.GetValue(info)
                ?? throw new NotSupportedException("Pinned Avalonia X11 display contract changed."));
            if (display == IntPtr.Zero || handle.Handle == IntPtr.Zero) throw new InvalidOperationException("Native window is closed.");
            return (display, handle.Handle, info);
        }
        internal static void Configure(TopLevel window, bool passive)
        {
            var x = Connection(window);
            var attributes = new Attributes { overrideRedirect = passive ? 1 : 0 };
            XChangeWindowAttributes(x.Display, x.Handle, (nuint)512, ref attributes);
            var hints = new Hints { flags = 1, input = passive ? 0 : 1 };
            XSetWMHints(x.Display, x.Handle, ref hints);
            if (!passive)
            {
                var name = new ClassHint { name = NativePassive.ClassPrefix + "Menu", type = NativePassive.ClassPrefix + "Menu" };
                XSetClassHint(x.Display, x.Handle, ref name);
                object atoms = x.Info.GetType().GetProperty("Atoms", Members)?.GetValue(x.Info)
                    ?? x.Info.GetType().GetField("Atoms", Members)?.GetValue(x.Info);
                IntPtr Atom(string key) => (IntPtr)(atoms.GetType().GetField(key, Members)?.GetValue(atoms)
                    ?? atoms.GetType().GetProperty(key, Members)?.GetValue(atoms)
                    ?? throw new NotSupportedException("Pinned X11 atom contract changed: " + key));
                XChangeProperty(x.Display, x.Handle, Atom("_NET_WM_WINDOW_TYPE"), (IntPtr)4, 32, 0, new[] { Atom("_NET_WM_WINDOW_TYPE_NORMAL") }, 1);
            }
        }
        [StructLayout(LayoutKind.Sequential)] private struct ClassHint { [MarshalAs(UnmanagedType.LPStr)] public string name; [MarshalAs(UnmanagedType.LPStr)] public string type; }
        [StructLayout(LayoutKind.Sequential)] private struct Attributes
        {
            public nuint backgroundPixmap, backgroundPixel, borderPixmap, borderPixel;
            public int bitGravity, winGravity, backingStore;
            public nuint backingPlanes, backingPixel;
            public int saveUnder;
            public nint eventMask, doNotPropagateMask;
            public int overrideRedirect;
            public nuint colormap, cursor;
        }
        [StructLayout(LayoutKind.Sequential)] private struct Hints
        {
            public nint flags; public int input, initialState; public nuint iconPixmap, iconWindow;
            public int iconX, iconY; public nuint iconMask, windowGroup;
        }
        [DllImport("libX11.so.6")] private static extern int XChangeWindowAttributes(IntPtr d, IntPtr w, nuint mask, ref Attributes attributes);
        [DllImport("libX11.so.6")] private static extern int XSetWMHints(IntPtr d, IntPtr w, ref Hints hints);
        [DllImport("libX11.so.6")] private static extern int XSetClassHint(IntPtr d, IntPtr w, ref ClassHint hints);
        [DllImport("libX11.so.6")] private static extern int XChangeProperty(IntPtr d, IntPtr w, IntPtr property, IntPtr type, int format, int mode, IntPtr[] values, int count);
    }
}
