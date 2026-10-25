using System;
using CapriDesktop;
class PolicyTests
{
    static void Main(string[] args)
    {
        if (args.Length > 0) { PackageChecks.Run(args); return; }
        ChatTests.Run();
        StabilityTests.Run();
        if (AnimationPolicy.Complete(AnimationState.Grin, 31, 16) || !AnimationPolicy.Complete(AnimationState.Grin, 32, 16)) throw new Exception("Grin duration");
        if (AnimationPolicy.Complete(AnimationState.Sleep, 100, 16)) throw new Exception("Sleep must loop");
        if (!AnimationPolicy.Seated(AnimationState.PerchGlitch)) throw new Exception("Perch transition");
        double once = AnimationPolicy.Approach(0, 3, .1), twice = AnimationPolicy.Approach(AnimationPolicy.Approach(0, 3, .05), 3, .05);
        if (Math.Abs(once - twice) > 1e-9 || once <= 0 || once >= 3) throw new Exception("Frame independent smoothing");
        double v = 0, distance = 0;
        for (int i = 0; i < 100; i++) distance += MotionPhysics.Fall(ref v, 100, .01);
        if (Math.Abs(distance - 50) > 1e-8 || Math.Abs(v - 100) > 1e-8) throw new Exception("Gravity integration");
        v = 0; double coarse = 0;
        for (int i = 0; i < 10; i++) coarse += MotionPhysics.Fall(ref v, 100, .1);
        if (Math.Abs(coarse - distance) > 1e-8) throw new Exception("Gravity depends on FPS");
        double jumpVelocity = -100, jumpHeight = 0;
        for (int i = 0; i < 20; i++) jumpHeight += MotionPhysics.Fall(ref jumpVelocity, 100, .1);
        if (Math.Abs(jumpHeight) > 1e-8 || jumpVelocity <= 0) throw new Exception("Ballistic hop return");
        double horizontal;
        if (!MotionPhysics.PlanJump(60, 0, 100, 200, 100, out horizontal) || Math.Abs(horizontal - 60) > 1e-8) throw new Exception("Reachable jump plan");
        if (MotionPhysics.PlanJump(500, 0, 100, 200, 100, out horizontal) || MotionPhysics.PlanJump(20, -100, 100, 200, 100, out horizontal)) throw new Exception("Unreachable jump rejected");
        double slow = 0, fast = 0, a = 0, b = 0;
        for (int i = 0; i < 10; i++) slow += MotionPhysics.Walk(ref a, 50, .1);
        for (int i = 0; i < 100; i++) fast += MotionPhysics.Walk(ref b, 50, .01);
        if (Math.Abs(slow - fast) > 1e-8) throw new Exception("Walking depends on FPS");
        if (!MotionPhysics.Crosses(-10, 5, 0) || MotionPhysics.Crosses(5, -10, 0)) throw new Exception("Landing direction");
        if (!MotionPhysics.Covers(-1920, 0, 0, 1080, -1920, 0, 0, 1080) || MotionPhysics.Covers(-1920, 0, 0, 1040, -1920, 0, 0, 1080)) throw new Exception("Fullscreen geometry");
        DesktopBehavior behavior = new DesktopBehavior();
        behavior.Stop(0);
        if (behavior.Due(19, true) || behavior.Due(21, false) || !behavior.Due(21, true)) throw new Exception("Decision cooldown");
        behavior.Begin(21, -1);
        if (behavior.Step(22, .1, 50) >= 0) throw new Exception("Walking direction");
        behavior.Stop(22);
        if (behavior.Step(23, .1, 50) != 0) throw new Exception("User interruption");
        var gate = new CapriDesktop.Services.DesktopPermissionGate(null);
        if (gate.RequestAsync(CapriDesktop.Services.DesktopCapability.CaptureScreen, "test", System.Threading.CancellationToken.None).GetAwaiter().GetResult()) throw new Exception("Permission must fail closed");
        Console.WriteLine("PASS 16 groups: jump planning and unreachable targets, ballistic hop, animation duration, looping, perch, easing, gravity, FPS independence, walking, collision, fullscreen geometry, cooldown, direction, interruption, permissions");
    }
}
