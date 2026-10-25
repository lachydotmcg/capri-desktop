using System;
namespace CapriDesktop
{
    internal enum Locomotion { Idle, Walking, Jumping, Falling, Sitting }
    internal static class MotionPhysics
    {
        // Exact constant-acceleration integration, bounded to discard long UI stalls.
        internal static double Fall(ref double velocity, double gravity, double elapsed)
        {
            double dt = Math.Max(0, Math.Min(.1, elapsed));
            double distance = velocity * dt + .5 * gravity * dt * dt;
            velocity += gravity * dt;
            return distance;
        }
        internal static double Walk(ref double velocity, double target, double elapsed)
        {
            double dt = Math.Max(0, Math.Min(.1, elapsed));
            double decay = Math.Exp(-10 * dt);
            double distance = target * dt + (velocity - target) * (1 - decay) / 10;
            velocity = target + (velocity - target) * decay;
            return distance;
        }
        internal static bool PlanJump(double dx, double dy, double upwardSpeed, double gravity, double maxHorizontalSpeed, out double horizontalSpeed)
        {
            horizontalSpeed = 0;
            if (gravity <= 0 || upwardSpeed <= 0 || maxHorizontalSpeed <= 0) return false;
            double discriminant = upwardSpeed * upwardSpeed + 2 * gravity * dy;
            if (discriminant < 0) return false;
            double time = (upwardSpeed + Math.Sqrt(discriminant)) / gravity;
            if (time <= 0 || time > 2) return false;
            horizontalSpeed = dx / time;
            return Math.Abs(horizontalSpeed) <= maxHorizontalSpeed;
        }
        internal static bool Crosses(double before, double after, double surface)
        { return after >= before && before <= surface && after >= surface; }
        internal static bool Covers(int left, int top, int right, int bottom, int screenLeft, int screenTop, int screenRight, int screenBottom)
        { return left <= screenLeft && top <= screenTop && right >= screenRight && bottom >= screenBottom; }
    }
}
