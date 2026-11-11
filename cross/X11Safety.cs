using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal sealed class X11Safety
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Error
        {
            internal int Type;
            internal IntPtr Display;
            internal UIntPtr Resource;
            internal UIntPtr Serial;
            internal byte Code, Request, Minor;
        }

        internal IntPtr Display;
        internal IntPtr Previous;
        internal long Errors;
        internal Func<string, bool> Log = Write;
        private string first;
        private int reported;

        internal unsafe int Handle(IntPtr display, IntPtr error)
        {
            try
            {
                Interlocked.Increment(ref Errors);
                if (Volatile.Read(ref first) == null)
                {
                    Error e = error == IntPtr.Zero ? new Error() : Marshal.PtrToStructure<Error>(error);
                    string line = "X11 error code " + e.Code + ", request code " + e.Request + ", minor " + e.Minor + ", call " + Call(e.Request) + ".";
                    Interlocked.CompareExchange(ref first, line, null);
                }
            }
            catch { }
            try
            {
                if (display != Display && Previous != IntPtr.Zero)
                    ((delegate* unmanaged[Cdecl]<IntPtr, IntPtr, int>)Previous)(display, error);
            }
            catch { }
            Report();
            return 0;
        }

        internal void Report()
        {
            try
            {
                string line = Volatile.Read(ref first);
                if (line == null || Interlocked.CompareExchange(ref reported, 1, 0) != 0) return;
                bool written = false;
                try { written = Log(line + " Count " + Interlocked.Read(ref Errors) + "; further X11 errors are counted without more log lines."); }
                finally { Volatile.Write(ref reported, written ? 2 : 0); }
            }
            catch { }
        }

        private static bool Write(string line)
        {
            string path = Brain.LogPath;
            if (path == null) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            return Brain.LogAt(path, "x11", -1, line);
        }

        private static string Call(byte request)
        {
            switch (request)
            {
                case 1: return "XCreateWindow";
                case 2: return "XChangeWindowAttributes";
                case 3: return "XGetWindowAttributes";
                case 4: return "XDestroyWindow";
                case 8: return "XMapWindow";
                case 10: return "XUnmapWindow";
                case 12: return "XConfigureWindow";
                case 14: return "XGetGeometry";
                case 15: return "XQueryTree";
                case 16: return "XInternAtom";
                case 18: return "XChangeProperty";
                case 20: return "XGetWindowProperty";
                case 26: return "XGrabPointer";
                case 27: return "XUngrabPointer";
                case 38: return "XQueryPointer";
                case 40: return "XTranslateCoordinates";
                default: return request >= 128 ? "X11 extension request" : "X11 request " + request;
            }
        }

        internal static long[] Property(int status, IntPtr type, int format, IntPtr items, IntPtr data)
        {
            long count = items.ToInt64();
            if (status != 0 || type == IntPtr.Zero || format != 32 || data == IntPtr.Zero || count < 0 || count > 4096) return null;
            long[] values = new long[(int)count];
            for (int i = 0; i < values.Length; i++) values[i] = Marshal.ReadIntPtr(data, i * IntPtr.Size).ToInt64() & 0xFFFFFFFFL;
            return values;
        }

        internal static PixelRect Work(Screen screen)
        {
            if (screen == null) return new PixelRect(0, 0, 1920, 1080);
            return OperatingSystem.IsLinux() ? Work(screen.WorkingArea, screen.Bounds) : screen.WorkingArea;
        }

        internal static PixelRect Work(PixelRect work, PixelRect bounds)
        {
            if (work.Width > 0 && work.Height > 0) return work;
            return bounds.Width > 0 && bounds.Height > 0 ? bounds : new PixelRect(0, 0, 1920, 1080);
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int OtherHandler(IntPtr display, IntPtr error);

        internal static void Checks(List<string> checks)
        {
            X11Safety safe = new X11Safety { Display = new IntPtr(1) };
            List<string> lines = new List<string>();
            safe.Log = delegate(string line) { lines.Add(line); return true; };
            int called = 0;
            OtherHandler other = delegate(IntPtr display, IntPtr error) { if (display == new IntPtr(2) && error != IntPtr.Zero) called++; return 17; };
            safe.Previous = Marshal.GetFunctionPointerForDelegate(other);
            IntPtr data = Marshal.AllocHGlobal(Math.Max(Marshal.SizeOf<Error>(), IntPtr.Size * 2));
            try
            {
                for (int i = 0; i < Marshal.SizeOf<Error>(); i++) Marshal.WriteByte(data, i, 0);
                Marshal.WriteByte(data, IntPtr.Size * 4, 3);
                Marshal.WriteByte(data, IntPtr.Size * 4 + 1, 20);
                if (safe.Handle(new IntPtr(2), data) != 0 || called != 1 || safe.Errors != 1 || lines.Count != 1
                    || !lines[0].Contains("error code 3, request code 20") || !lines[0].Contains("call XGetWindowProperty")) throw new Exception("An X11 error didn't pass to a different managed delegate.");
                safe.Handle(safe.Display, data);
                safe.Previous = IntPtr.Zero;
                if (safe.Handle(new IntPtr(2), data) != 0 || safe.Handle(new IntPtr(2), IntPtr.Zero) != 0 || called != 1 || safe.Errors != 4 || lines.Count != 1) throw new Exception("X11 errors weren't counted once or a missing handler failed.");
                X11Safety early = new X11Safety();
                early.Log = delegate { return false; };
                early.Handle(IntPtr.Zero, data);
                early.Log = delegate { throw new Exception("No log yet."); };
                if (early.Handle(IntPtr.Zero, data) != 0) throw new Exception("An X11 log failure escaped.");
                early.Log = delegate(string line) { lines.Add(line); return true; };
                early.Report();
                early.Report();
                if (lines.Count != 2 || early.Errors != 2) throw new Exception("An early X11 error was lost or logged twice.");
                checks.Add("PASS X11 errors: a different managed handler is called without a cast, zero is safe, errors are counted, and one log line survives an early or failed log");
                Marshal.WriteIntPtr(data, new IntPtr(12));
                Marshal.WriteIntPtr(data, IntPtr.Size, new IntPtr(34));
                long[] values = Property(0, new IntPtr(33), 32, new IntPtr(2), data);
                if (values == null || values.Length != 2 || values[0] != 12 || values[1] != 34
                    || Property(0, IntPtr.Zero, 32, new IntPtr(2), data) != null
                    || Property(0, new IntPtr(33), 32, new IntPtr(2), IntPtr.Zero) != null
                    || Property(1, new IntPtr(33), 32, new IntPtr(2), data) != null
                    || Property(0, new IntPtr(33), 8, new IntPtr(2), data) != null
                    || Property(0, new IntPtr(33), 32, new IntPtr(-1), data) != null
                    || Property(0, new IntPtr(33), 32, new IntPtr(4097), data) != null
                    || Property(0, new IntPtr(33), 32, IntPtr.Zero, data).Length != 0) throw new Exception("A missing or invalid X11 property wasn't ignored.");
                PixelRect bounds = new PixelRect(10, 20, 1280, 720), work = new PixelRect(10, 20, 1280, 680);
                if (Work(work, bounds) != work || Work(new PixelRect(0, 0, 0, 720), bounds) != bounds
                    || Work(new PixelRect(0, 0, 1280, 0), bounds) != bounds || Work(default(PixelRect), default(PixelRect)).Width <= 0) throw new Exception("An empty X11 work area didn't fall back to the screen.");
                X11Desk desk = new X11Desk(delegate { return null; });
                if (desk.Windows(new long[0]).Count != 0 || desk.Find(12) != null) throw new Exception("A missing X11 client list wasn't ignored.");
                desk = new X11Desk(delegate { return new long[0]; });
                if (desk.Windows(new long[0]).Count != 0 || desk.Find(12) != null) throw new Exception("An empty X11 client list wasn't ignored.");
                checks.Add("PASS X11 properties: missing, null and invalid replies are ignored; an empty work area uses screen bounds");
            }
            finally { Marshal.FreeHGlobal(data); GC.KeepAlive(other); }
        }
    }
}
