using System;
namespace CapriDesktop
{
    internal enum AnimationState { Idle, Blink, Grin, Glitch, Dangle, Sleep, Wake, Perch, PerchBlink, PerchGlitch, Mouth }
    internal static class AnimationPolicy
    {
        internal static bool Seated(AnimationState state) { return state == AnimationState.Perch || state == AnimationState.PerchGlitch; }
        internal static bool Complete(AnimationState state, int index, int count)
        {
            return index >= count * (state == AnimationState.Grin ? 2 : 1) &&
                (state == AnimationState.Grin || state == AnimationState.Glitch || state == AnimationState.Wake || state == AnimationState.PerchGlitch);
        }
        internal static double Approach(double current, double target, double elapsed)
        { return current + (target - current) * (1 - Math.Exp(-8 * Math.Max(0, Math.Min(elapsed, 0.25)))); }
    }
}
