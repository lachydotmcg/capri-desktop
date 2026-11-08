using System;

namespace CapriDesktop
{
    // The released strip's clock; delayed UI ticks skip frames instead of extending a click.
    internal sealed class ClickPlayback
    {
        internal const int Frames = 20, Fps = 60, Cell = 144, Width = 72;
        internal const double Duration = (double)Frames / Fps;
        private double started = double.NaN;
        internal void Begin(double now) { started = now; }
        internal void Clear() { started = double.NaN; }
        internal int Frame(double now)
        {
            double age = now - started;
            return double.IsFinite(age) && age >= 0 && age < Duration ? (int)(age * Fps) : -1;
        }
    }
}
