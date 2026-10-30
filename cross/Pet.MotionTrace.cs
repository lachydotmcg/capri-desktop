using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using Avalonia;
using Avalonia.Input;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private MotionTrace motionTrace;

        private void TraceMotion(string stage, PointerEventArgs e = null)
        {
            if (motionTrace == null) return;
            Point local = e == null ? pointer : e.GetPosition(this);
            PixelPoint root = this.PointToScreen(local);
            motionTrace.Write(new
            {
                stage, time = Now, ticks = Environment.TickCount64,
                local = new[] { local.X, local.Y }, eventRoot = new[] { root.X, root.Y },
                position = new[] { Position.X, Position.Y }, anchor = new[] { anchor.X, anchor.Y },
                grab = new[] { grab.X, grab.Y }, frame = new[] { FrameX, FrameY },
                scale = DesktopScaling, pressed, dragging, falling,
                owner = rootDragOwned ? "compositor-root-pointer" : "ordinary",
                grabBodyDip = new[] { grabBodyDip.X, grabBodyDip.Y },
                compositorPointer = rootPointer?.Latest,
                placementError = petSurface?.Error,
                output = lastNativeMap == null ? null : new { lastNativeMap.Output.Name, lastNativeMap.Output.Logical, lastNativeMap.AvaloniaBounds },
                captured = e == null ? rootCapturedPointer?.Captured == this : e.Pointer.Captured == this,
                native = motionTrace.Native(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero),
                maps = Desk is HyprlandDesk ? string.Join(";", Screens.All) : null
            });
        }
    }

    internal sealed class MotionTrace : IDisposable
    {
        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
            out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
        private readonly IntPtr display;
        private readonly StreamWriter writer;
        private readonly ConcurrentQueue<string> pending = new ConcurrentQueue<string>();
        private readonly Timer flush;
        private int count;
        private bool disposed;

        internal static MotionTrace Open()
        {
            string path = Environment.GetEnvironmentVariable("CAPRI_MOTION_TRACE");
            if (string.IsNullOrEmpty(path)) return null;
            try { return new MotionTrace(path); }
            catch (Exception error) { Console.Error.WriteLine("Motion trace unavailable: " + error.Message); return null; }
        }

        private MotionTrace(string path)
        {
            writer = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            if (OperatingSystem.IsLinux()) display = XOpenDisplay(IntPtr.Zero);
            flush = new Timer(_ => Flush(), null, 100, 100);
        }

        internal object Native(IntPtr window)
        {
            if (display == IntPtr.Zero || window == IntPtr.Zero) return null;
            if (XQueryPointer(display, window, out _, out _, out int x, out int y, out int localX, out int localY, out uint buttons) == 0) return null;
            return new { pointer = new[] { x, y }, origin = new[] { x - localX, y - localY }, local = new[] { localX, localY }, buttons };
        }

        internal void Write(object value)
        {
            if (Interlocked.Increment(ref count) > 20000) return;
            pending.Enqueue(JsonSerializer.Serialize(value));
        }

        private void Flush()
        {
            lock (writer)
            {
                if (disposed) return;
                while (pending.TryDequeue(out string line)) writer.WriteLine(line);
                writer.Flush();
            }
        }

        public void Dispose()
        {
            flush.Dispose();
            lock (writer)
            {
                Flush();
                disposed = true;
                writer.Dispose();
            }
            if (display != IntPtr.Zero) XCloseDisplay(display);
        }
    }
}
