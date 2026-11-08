using System;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        private HyprlandDesktop.PointerSample newestDragSample;
        private int dragDispatch;
        private long pressSequence, lastDragSequence;
        private string pressLayout, pressSession;
        private Point pressLogical;
        private Point? lastDragPoint;
        private long dragGesture;
        private bool releaseSettling;

        private void BindNativeDrag()
        {
            if (Desk is not HyprlandDesk native) return;
            native.Desktop.PointerChanged += QueueNativeDrag;
            Closed += (_, _) => native.Desktop.PointerChanged -= QueueNativeDrag;
        }

        private bool BeginNativeDrag(Point local)
        {
            if (Desk is not HyprlandDesk native) return true;
            var state = native.Desktop.VisualSnapshot;
            PixelPoint screen = this.PointToScreen(local);
            var map = native.MapAt(screen);
            if (state == null || state.Locked || map == null) return false;
            // The press event's local point and observed native origin form one
            // acquisition. Never substitute an unrelated cached root sample.
            dragGesture++;
            pressLogical = map.ToLogical(screen);
            pressLayout = state.LayoutId; pressSession = state.SessionId;
            pressSequence = lastDragSequence = native.Desktop.PointerSequence;
            lastDragPoint = null;
            Interlocked.Exchange(ref newestDragSample, null);
            NativeSurface.CancelMoves(this);
            return true;
        }

        private void QueueNativeDrag(HyprlandDesktop.PointerSample sample)
        {
            Interlocked.Exchange(ref newestDragSample, sample);
            if (Interlocked.Exchange(ref dragDispatch, 1) != 0) return;
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref dragDispatch, 0);
                var latest = Interlocked.Exchange(ref newestDragSample, null);
                Program.Guard(() => ApplyNativeDrag(latest));
            }, DispatcherPriority.Input);
        }

        private void ApplyNativeDrag(HyprlandDesktop.PointerSample sample)
        {
            if (!pressed || Desk is not HyprlandDesk native || sample == null || sample.Sequence <= pressSequence || sample.Sequence <= lastDragSequence) return;
            var valid = native.Desktop.SamplePointer();
            if (valid == null || Environment.TickCount64 - sample.At > 100 || sample.Revision != valid.Revision || sample.Layout != pressLayout || sample.Session != pressSession) { Unhold(); return; }
            lastDragSequence = sample.Sequence;
            NativeMotionTrace.Emit("ui", sample.Sequence, sample.At, sample.Point.X, sample.Point.Y);
            if (lastDragPoint == sample.Point) return;
            lastDragPoint = sample.Point;
            double dx = sample.Point.X - pressLogical.X, dy = sample.Point.Y - pressLogical.Y;
            if (!dragging && dx * dx + dy * dy < 25) return;
            var map = native.Maps.FirstOrDefault(m => m.Output.Logical.Contains(sample.Point));
            if (map == null) { Unhold(); return; }
            if (!dragging) Grab();
            Point point = map.ToAvaloniaPoint(sample.Point);
            Move(new PixelPoint((int)Math.Round(point.X - grabDip.X * DesktopScaling), (int)Math.Round(point.Y - grabDip.Y * DesktopScaling)), map, sample.Sequence, sample.At);
        }
        private void CancelNativeRelease()
        {
            if (!releaseSettling) return;
            dragGesture++; releaseSettling = false; dragging = false;
            NativeSurface.CancelMoves(this);
        }

        private void RetireNativeRelease(long gesture)
        {
            if (gesture != dragGesture || pressed) return;
            dragging = false; asked.Clear();
            anchor = new PixelPoint(Position.X - Px(FrameX), Position.Y - Px(FrameY));
        }

        private async void SettleNativeRelease(bool here)
        {
            long gesture = dragGesture;
            releaseSettling = true;
            try
            {
                bool applied = await NativeSurface.FlushMoves(this);
                if (gesture != dragGesture || pressed) return;
                var state = (Desk as HyprlandDesk)?.Desktop.VisualSnapshot;
                if (applied && IsVisible && !quitting && state != null && !state.Locked && state.LayoutId == pressLayout && state.SessionId == pressSession) Let(here);
                else RetireNativeRelease(gesture);
            }
            catch (OperationCanceledException) { RetireNativeRelease(gesture); }
            catch (Exception error) { RetireNativeRelease(gesture); Program.Crashed(error); }
            finally { if (gesture == dragGesture) releaseSettling = false; }
        }
    }
}
