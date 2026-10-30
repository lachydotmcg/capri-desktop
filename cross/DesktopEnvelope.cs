using System;
using System.Collections.Generic;
using Avalonia;

namespace CapriDesktop
{
    internal static class DesktopEnvelope
    {
        // Authored frame rectangles include glitch FX, while the standing body crop
        // does not. Keep the original anchor and draw code; enlarge only the surface.
        internal static Rect Bounds(IEnumerable<Rect> frames, Point centre, double tiltDegrees, double breathe, double margin)
        {
            double left = double.PositiveInfinity, top = double.PositiveInfinity;
            double right = double.NegativeInfinity, bottom = double.NegativeInfinity;
            double angle = Math.Abs(tiltDegrees) * Math.PI / 180;
            foreach (Rect frame in frames)
            {
                foreach (Point corner in new[] { frame.TopLeft, frame.TopRight, frame.BottomLeft, frame.BottomRight })
                {
                    double x = corner.X - centre.X, y = corner.Y - centre.Y;
                    // Bound every angle in the breathing interval, including interior
                    // extrema, rather than assuming the two endpoint rotations suffice.
                    double radius = Math.Sqrt(x * x + y * y), phase = Math.Atan2(y, x);
                    foreach (double theta in new[] { -angle, 0, angle, -phase, Math.PI - phase, -Math.PI - phase, Math.PI / 2 - phase, -Math.PI / 2 - phase })
                    {
                        if (theta < -angle || theta > angle) continue;
                        double px = centre.X + radius * Math.Cos(phase + theta), py = centre.Y + radius * Math.Sin(phase + theta);
                        left = Math.Min(left, px); top = Math.Min(top, py);
                        right = Math.Max(right, px); bottom = Math.Max(bottom, py);
                    }
                }
            }
            if (!double.IsFinite(left)) throw new ArgumentException("At least one sprite frame is required.", nameof(frames));
            return new Rect(Math.Floor(left - margin), Math.Floor(top - Math.Abs(breathe) - margin),
                Math.Ceiling(right + margin) - Math.Floor(left - margin), Math.Ceiling(bottom + Math.Abs(breathe) + margin) - Math.Floor(top - Math.Abs(breathe) - margin));
        }
    }
}
