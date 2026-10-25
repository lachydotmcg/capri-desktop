using System;
using System.Drawing;
namespace CapriDesktop
{
    internal static class RuntimePolicy
    {
        internal static Point Clamp(Point position, Size size, Rectangle area)
        {
            return new Point((int)Math.Max(area.Left, Math.Min((long)position.X, (long)area.Right - size.Width)),
                (int)Math.Max(area.Top, Math.Min((long)position.Y, (long)area.Bottom - size.Height)));
        }
        internal static bool SocialAnimationAllowed(bool sleeping, bool dragging, bool riding, bool falling, bool suspended)
        { return !sleeping && !dragging && !riding && !falling && !suspended; }
        internal static double CpuPercent(double cpuSeconds, double wallSeconds, int processors)
        { return wallSeconds <= 0 || processors <= 0 ? 0 : Math.Max(0, Math.Min(100, cpuSeconds / wallSeconds / processors * 100)); }
    }
}
