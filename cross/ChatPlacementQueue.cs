using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace CapriDesktop
{
    internal sealed record ChatPlacementTarget(Point Origin, Size Size, int Workspace)
    {
        internal static ChatPlacementTarget FromAvalonia(HyprlandDisplayMap map, PixelPoint origin, Size dipSize, double scale)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            Point logical = map.ToLogical(origin);
            Point end = map.ToLogical(new Point(origin.X + dipSize.Width * scale, origin.Y + dipSize.Height * scale));
            return new ChatPlacementTarget(logical, new Size(end.X - logical.X, end.Y - logical.Y),
                map.Output.SpecialWorkspace != 0 ? map.Output.SpecialWorkspace : map.Output.Workspace);
        }
    }

    // One native writer per chat surface. Animation ticks replace pending work rather
    // than starting overlapping IPC calls. Hide/remap invalidates even unchanged targets.
    internal sealed class ChatPlacementQueue : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<ChatPlacementTarget, CancellationToken, Task> place;
        private CancellationTokenSource moving;
        private ChatPlacementTarget pending, requested, applied;
        private Task worker;
        private bool suspended, disposed;
        private string error;
        private long retryAfter;
        internal string Error { get { lock (gate) return error; } }
        internal ChatPlacementTarget Applied { get { lock (gate) return applied; } }

        internal ChatPlacementQueue(Func<ChatPlacementTarget, CancellationToken, Task> place) { this.place = place; }

        internal Task Queue(ChatPlacementTarget target)
        {
            lock (gate)
            {
                if (disposed || suspended) return Task.CompletedTask;
                if (target == requested && (error == null || Environment.TickCount64 < retryAfter))
                    return worker ?? Task.CompletedTask;
                requested = pending = target;
                if (worker == null) worker = Task.Run(Drain);
                return worker;
            }
        }

        internal void Suspend(bool value)
        {
            lock (gate)
            {
                suspended = value;
                requested = pending = applied = null;
                moving?.Cancel();
            }
        }

        private async Task Drain()
        {
            while (true)
            {
                ChatPlacementTarget target;
                CancellationTokenSource operation;
                lock (gate)
                {
                    if (disposed || suspended || pending == null) { worker = null; return; }
                    target = pending; pending = null;
                    moving = operation = new CancellationTokenSource();
                }
                try
                {
                    // Mapping a newly shown XWayland window is asynchronous. Retry
                    // that bounded race off the UI thread, retaining the actual error.
                    for (int attempt = 0; ; attempt++)
                    {
                        try { await place(target, operation.Token).ConfigureAwait(false); break; }
                        catch (Exception failure) when (failure is not OperationCanceledException && attempt < 3)
                        { await Task.Delay(50, operation.Token).ConfigureAwait(false); }
                    }
                    lock (gate)
                    {
                        if (!operation.IsCancellationRequested) { applied = target; error = null; }
                    }
                }
                catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
                catch (Exception failure)
                {
                    lock (gate) { error = failure.Message; retryAfter = Environment.TickCount64 + 250; }
                }
                finally
                {
                    lock (gate) if (moving == operation) moving = null;
                    operation.Dispose();
                }
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                disposed = true;
                requested = pending = applied = null;
                moving?.Cancel();
            }
        }
    }
}
