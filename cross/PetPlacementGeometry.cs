using System;
using Avalonia;

namespace CapriDesktop
{
    internal static class PetPlacementGeometry
    {
        internal static Point Restore(PixelRect area, double width, double height, double across, double down)
        {
            return new Point(area.X + Math.Max(0, area.Width - width) * Fraction(across), area.Y + Math.Max(0, area.Height - height) * Fraction(down));
        }

        internal static Point Clamp(PixelRect area, double width, double height, Point origin)
        {
            Point fraction = Fraction(area, width, height, origin);
            return Restore(area, width, height, fraction.X, fraction.Y);
        }

        internal static Point Fraction(PixelRect area, double width, double height, Point origin)
        {
            return new Point(Fraction((origin.X - area.X) / Math.Max(1, area.Width - width)), Fraction((origin.Y - area.Y) / Math.Max(1, area.Height - height)));
        }

        internal static bool Contains(PixelRect area, Point origin, double width, double height) =>
            origin.X >= area.X && origin.Y >= area.Y && origin.X + width <= area.Right + 1 && origin.Y + height <= area.Bottom + 1;

        private static double Fraction(double value) => double.IsFinite(value) ? Math.Clamp(value, 0, 1) : 0.5;
    }
}
