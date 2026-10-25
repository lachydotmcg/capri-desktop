using System;
namespace CapriDesktop
{
    // Decision scheduling is independent of rendering and never invokes AI or reads window content.
    internal sealed class DesktopBehavior
    {
        internal Locomotion State { get; private set; }
        internal double Velocity;
        internal double Remainder;
        internal double NextDecision = 20;
        internal double WalkUntil;
        internal int Direction = 1;
        internal void Stop(double now) { State = Locomotion.Idle; Velocity = Remainder = 0; WalkUntil = 0; NextDecision = now + 20; }
        internal bool Due(double now, bool allowed) { return allowed && now >= NextDecision; }
        internal void Begin(double now, int direction) { Direction = direction; WalkUntil = now + 4; NextDecision = now + 25; State = Locomotion.Walking; }
        internal int Step(double now, double elapsed, double speed)
        {
            if (now >= WalkUntil) State = Locomotion.Sitting;
            Remainder += MotionPhysics.Walk(ref Velocity, State == Locomotion.Walking ? Direction * speed : 0, elapsed);
            int pixels = (int)Remainder; Remainder -= pixels; return pixels;
        }
    }
}
