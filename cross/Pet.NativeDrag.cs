using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private bool rootDragOwned;
        private Point grabBodyDip;
        private Point rootDragStart;
        private Point? lastRootDragPointer;
        private long rootDragStartedAt;
        private IPointer rootCapturedPointer;
        private HyprlandPointer rootPointer;
        private DispatcherTimer rootDragTimer;

        private void InitializeNativeDrag()
        {
            if (!(Desk is HyprlandDesk native) || Program.Smoke != null) return;
            petSurface = new HyprlandPetSurface(native.Desktop);
            rootPointer = new HyprlandPointer(native.Desktop);
            rootDragTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(8) };
            rootDragTimer.Tick += delegate { UpdateRootDrag(); };
            Closed += delegate { DisposeNativeMotion(); };
            Opened += delegate
            {
                petSurface.Suspend(false);
                petSurface.Remapped();
                if (nativePlaced) Move(anchor);
                else PrepareNativePlacement();
            };
        }

        private bool BeginNativeDrag(PointerPressedEventArgs e)
        {
            if (rootPointer == null) return false;
            Point local = e.GetPosition(this);
            grabBodyDip = new Point(local.X + FrameX, local.Y + FrameY);
            HyprlandDesk native = (HyprlandDesk)Desk;
            HyprlandDisplayMap map = native.MapAt(downAt) ?? native.ActiveMap;
            if (map == null) return false;
            rootDragStart = map.ToLogical(downAt);
            rootDragStartedAt = Environment.TickCount64;
            rootDragOwned = true;
            lastRootDragPointer = null;
            rootCapturedPointer = e.Pointer;
            rootPointer.Enabled = true;
            rootDragTimer.Start();
            return true;
        }

        private void UpdateRootDrag(bool force = false)
        {
            if (!rootDragOwned) return;
            HyprlandPointer.Sample sample = rootPointer.Latest;
            if (sample == null || sample.At < rootDragStartedAt)
            {
                if (Environment.TickCount64 - rootDragStartedAt > 150) { TraceMotion("cancel-no-sample"); EndRootDrag(false, false); }
                return;
            }
            if (((HyprlandDesk)Desk).Desktop.Snapshot?.Locked == true) { TraceMotion("cancel-lock"); EndRootDrag(false, false); return; }
            if (Environment.TickCount64 - sample.At > 150)
            {
                TraceMotion("cancel-stale-sample");
                EndRootDrag(false, false);
                return;
            }
            if ((sample.Buttons & 256) == 0)
            {
                EndRootDrag((sample.Buttons & 1) != 0, true);
                return;
            }
            if (!force && lastRootDragPointer == sample.Position && petSurface.Error == null) return;
            lastRootDragPointer = sample.Position;
            Vector distance = sample.Position - rootDragStart;
            if (!dragging && distance.Length < 5) return;
            if (!dragging) Grab();
            HyprlandDesk native = (HyprlandDesk)Desk;
            HyprlandDisplayMap map = Array.Find(native.Maps, m => m.Output.Logical.Contains(sample.Position));
            if (map == null) { TraceMotion("cancel-output-missing"); EndRootDrag(false, false); PrepareNativePlacement(); return; }
            Point pointerRoot = map.ToAvaloniaPoint(sample.Position);
            Point to = new Point(pointerRoot.X - grabBodyDip.X * DesktopScaling, pointerRoot.Y - grabBodyDip.Y * DesktopScaling);
            MoveNative(to, map);
            TraceMotion("root-move");
        }

        private void EndRootDrag(bool here, bool released)
        {
            if (!rootDragOwned) return;
            rootDragOwned = false;
            rootDragTimer.Stop();
            rootPointer.Enabled = false;
            pressed = false;
            IPointer capture = rootCapturedPointer;
            rootCapturedPointer = null;
            capture?.Capture(null);
            if (dragging) Let(here);
            else if (released) React();
            TraceMotion("root-release");
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            TraceMotion("capture-lost");
            if (rootDragOwned) EndRootDrag(false, false);
            else if (pressed) { pressed = false; if (dragging) Drop(); }
        }

        private bool motionDisposed;
        private void DisposeNativeMotion()
        {
            if (motionDisposed) return;
            motionDisposed = true;
            EndRootDrag(false, false);
            rootPointer?.Dispose();
            petSurface?.Dispose();
            motionTrace?.Dispose();
            motionTrace = null;
        }
    }

    internal sealed class HyprlandPointer : IDisposable
    {
        [DllImport("libX11.so.6")] private static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] private static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XCloseDisplay(IntPtr display);
        [DllImport("libX11.so.6")] private static extern int XQueryPointer(IntPtr display, IntPtr window, out IntPtr root, out IntPtr child,
            out int rootX, out int rootY, out int windowX, out int windowY, out uint mask);
        internal sealed record Sample(Point Position, uint Buttons, long At);
        private readonly HyprlandDesktop desktop;
        private readonly CancellationTokenSource stop = new CancellationTokenSource();
        private readonly SemaphoreSlim wake = new SemaphoreSlim(0, 1);
        private readonly Task worker;
        private Sample latest;
        private int enabled;
        private int sampleGeneration;
        internal string Error { get; private set; }
        internal bool Enabled
        {
            set
            {
                Interlocked.Increment(ref sampleGeneration);
                Volatile.Write(ref latest, null);
                Volatile.Write(ref enabled, value ? 1 : 0);
                if (value && wake.CurrentCount == 0) wake.Release();
            }
        }
        internal Sample Latest => Volatile.Read(ref latest);
        internal HyprlandPointer(HyprlandDesktop desktop) { this.desktop = desktop; worker = Task.Run(Read); }

        private async Task Read()
        {
            IntPtr display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { Error = "XWayland pointer buttons are unavailable."; return; }
            try
            {
                IntPtr root = XDefaultRootWindow(display);
                while (!stop.IsCancellationRequested)
                {
                    if (Volatile.Read(ref enabled) == 0) { await wake.WaitAsync(stop.Token).ConfigureAwait(false); continue; }
                    if (Volatile.Read(ref enabled) != 0)
                    {
                        try
                        {
                            int generation = Volatile.Read(ref sampleGeneration);
                            long requestedAt = Environment.TickCount64;
                            Point point = HyprlandJson.Pointer(await desktop.QueryAsync("j/cursorpos", stop.Token).ConfigureAwait(false));
                            if (Environment.TickCount64 - requestedAt < 75 && generation == Volatile.Read(ref sampleGeneration) &&
                                XQueryPointer(display, root, out _, out _, out _, out _, out _, out _, out uint buttons) != 0)
                            {
                                Volatile.Write(ref latest, new Sample(point, buttons, requestedAt));
                                Error = null;
                            }
                        }
                        catch (Exception error) when (error is not OperationCanceledException) { Error = error.Message; Volatile.Write(ref latest, null); }
                    }
                    await Task.Delay(8, stop.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
            finally { XCloseDisplay(display); }
        }

        public void Dispose()
        {
            stop.Cancel();
            _ = worker.ContinueWith(_ => { wake.Dispose(); stop.Dispose(); }, TaskScheduler.Default);
        }
    }
}
