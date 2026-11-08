using System;
using System.Diagnostics;
namespace CapriDesktop
{
    // In-process diagnostic hook. The application installs no recorder and writes
    // no pointer history. The disposable native fixture owns bounded collection.
    internal static class NativeMotionTrace
    {
        internal sealed record Sample(string Stage, long Tick, long Sequence, long SourceAt, double X, double Y);
        internal static event Action<Sample> Observed;
        internal static void Emit(string stage, long sequence, long sourceAt, double x, double y)
        {
            var observer = Observed;
            if (observer != null) observer(new Sample(stage, Stopwatch.GetTimestamp(), sequence, sourceAt, x, y));
        }
    }
}
