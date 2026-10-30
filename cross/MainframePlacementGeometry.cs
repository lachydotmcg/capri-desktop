using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal sealed record MainframeViewport(Size Dip, Size Logical);

    internal static class MainframePlacementGeometry
    {
        internal static Screen Match(HyprlandOutput output, IReadOnlyList<Screen> screens)
        {
            Screen[] named = screens.Where(s => s.DisplayName == output.Name).ToArray();
            if (named.Length == 1) return named[0];
            Screen[] exact = screens.Where(s => s.Bounds.X == output.Logical.X && s.Bounds.Y == output.Logical.Y &&
                s.Bounds.Width == output.Logical.Width && s.Bounds.Height == output.Logical.Height).ToArray();
            return exact.Length == 1 ? exact[0] : null;
        }

        internal static Vector Measure(Size logicalSize, Size bounds)
        {
            if (!Valid(logicalSize) || !Valid(bounds)) throw new InvalidOperationException("Mainframe's rendered window dimensions are not ready.");
            return new Vector(logicalSize.Width / bounds.Width, logicalSize.Height / bounds.Height);
        }

        internal static MainframeViewport Fit(HyprlandOutput output, Size logicalSize, Size bounds, Size requested, Size minimum, Screen screen, double renderScaling, Vector? measured = null)
        {
            if (!Valid(logicalSize) || !Valid(bounds) || !Valid(requested) || !double.IsFinite(renderScaling) || renderScaling <= 0)
                throw new InvalidOperationException("Mainframe's rendered window dimensions are not ready.");
            Vector factor = measured ?? Measure(logicalSize, bounds);
            double x = screen == null ? factor.X : output.Logical.Width / screen.Bounds.Width * renderScaling;
            double y = screen == null ? factor.Y : output.Logical.Height / screen.Bounds.Height * renderScaling;
            if (!double.IsFinite(x) || !double.IsFinite(y) || x <= 0 || y <= 0)
                throw new InvalidOperationException("Mainframe's display scale is unavailable.");
            Size dip = new Size(Math.Min(requested.Width, Math.Max(minimum.Width, (output.WorkingArea.Width - 24) / x)),
                Math.Min(requested.Height, Math.Max(minimum.Height, (output.WorkingArea.Height - 24) / y)));
            return new MainframeViewport(dip, new Size(Math.Round(dip.Width * x), Math.Round(dip.Height * y)));
        }

        internal static Point Center(Rect area, Size actualSize) => new Point(
            Math.Ceiling(area.X) + Math.Max(0, (Math.Floor(area.Right) - Math.Ceiling(area.X) - Math.Ceiling(actualSize.Width)) / 2),
            Math.Ceiling(area.Y) + Math.Max(0, (Math.Floor(area.Bottom) - Math.Ceiling(area.Y) - Math.Ceiling(actualSize.Height)) / 2));

        private static bool Valid(Size size) => double.IsFinite(size.Width) && double.IsFinite(size.Height) && size.Width > 0 && size.Height > 0;
    }
}
