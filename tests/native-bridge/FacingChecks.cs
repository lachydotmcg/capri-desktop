using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Media.Imaging;
using CapriDesktop;

internal static class FacingChecks
{
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static byte[] Pixels(Bitmap bitmap)
    {
        byte[] pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        GCHandle pinned = GCHandle.Alloc(pixels, GCHandleType.Pinned);
        try { bitmap.CopyPixels(new PixelRect(bitmap.PixelSize), pinned.AddrOfPinnedObject(), pixels.Length, bitmap.PixelSize.Width * 4); }
        finally { pinned.Free(); }
        return pixels;
    }
    private static bool Same(Bitmap first, Bitmap second)
    {
        if (first.PixelSize != second.PixelSize) return false;
        return Pixels(first).AsSpan().SequenceEqual(Pixels(second));
    }
    private static void Left(Clip clip)
    {
        clip.WarmLeft();
        if (!SpinWait.SpinUntil(() => { clip.WarmLeft(); return clip.LeftReady; }, 5000)) throw new Exception("authored left art didn't finish loading");
    }
    private static void FinishLeft(Clip clip)
    {
        if (!SpinWait.SpinUntil(() => !clip.LeftLoading, 5000)) throw new Exception("opposite art read didn't settle");
    }
    private static Clip Idle() { return new Clip("idle", 60, 24, 1.11, 11.0 / 384 * 1.11).Older(Mods.OldIdleWide, 16, 8); }
    internal static void Run()
    {
        AppBuilder.Configure<Application>().UseStandardRuntimePlatformSubsystem().UseWindowingSubsystem(() => { }, "Offscreen checks").UseSkia().SetupWithoutStarting();
        string scratch = Path.Combine(Path.GetTempPath(), "capri-facing-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        string previous = Pet.SkinFolder; bool legacy = Pet.LegacyArt;
        try
        {
            Pet.SkinFolder = null; Pet.LegacyArt = false;
            Clip idle = Idle(), blink = new Clip("idle_blink", 8, 24, 1.11, 11.0 / 384 * 1.11), mouth = new Clip("idle_mouth", 5, 12, 1.11);
            Check(idle.Count == 60 && idle.Fps == 24 && idle.Wide == 116 && !idle.Old, "released native idle has sixty116px cells at24fps");
            Left(idle); Left(blink);
            using (Bitmap source = Pet.ReadArt("Capri.Sprite.idle_flipped.png")) Check(Same(idle.Art(true), source), "left renderer source is the authored matching-cell idle sheet");
            Check(!Same(idle.Art(true), idle.Art(false)), "left idle preserves authored anatomy rather than reusing right art");
            Check(blink.Art(true).PixelSize == blink.Art(false).PixelSize && !mouth.HasFlipped, "blink shares idle cell geometry and the mouth mirrors with her body");
            var body = new Body { Shown = idle, Size = 240, Centre = new Point(150, 150), Tilt = 17, Breathe = 3 };
            int alphaChecks = 0;
            foreach (bool left in new[] { false, true })
            {
                body.FacingLeft = left;
                foreach (int cell in new[] { 0, 29, 59 })
                {
                    body.Cell = cell;
                    Rect rect = body.Place(idle);
                    for (int y = 12; y < 384; y += 27)
                        for (int x = 3; x < 116; x += 11)
                        {
                            Point local = new Point(rect.X + (x + .5) / 116 * rect.Width, rect.Y + (y + .5) / 384 * rect.Height);
                            bool expected = idle.Solid(cell, x, y, 0, left);
                            if (body.Opaque(body.Pose.Transform(local), 0) != expected) throw new Exception("source alpha did not match transformed visible cell");
                            alphaChecks++;
                        }
                }
            }
            Check(alphaChecks > 500, "alpha hit sampling matches both authored facings after tilt and breathing");
            Pet.LegacyArt = true; idle.Reload();
            Check(idle.Old && idle.Count == 16 && idle.Fps == 8 && idle.ShiftX == 0 && !idle.HasFlipped && ReferenceEquals(idle.Art(true), idle.Strip), "Legacy stand uses its own sixteen-cell strip mirrored without new left art");
            Pet.LegacyArt = false;
            using (Stream source = typeof(Pet).Assembly.GetManifestResourceStream("Capri.Sprite.idle_legacy.png"))
            using (FileStream output = File.Create(Path.Combine(scratch, "idle.png"))) source.CopyTo(output);
            Pet.SkinFolder = scratch; idle.Reload();
            Check(idle.Old && idle.Count == 16 && !idle.HasFlipped && ReferenceEquals(idle.Art(true), idle.Strip), "old sixteen-cell skin owns both native facings");
            using (Stream source = typeof(Pet).Assembly.GetManifestResourceStream("Capri.Sprite.idle.png"))
            using (FileStream output = File.Create(Path.Combine(scratch, "idle.png"))) source.CopyTo(output);
            idle.Reload(); idle.WarmLeft();
            FinishLeft(idle);
            Check(!idle.LeftReady && ReferenceEquals(idle.Art(true), idle.Strip), "new skin with no matching opposite mirrors itself rather than wearing bundled left art");
            File.WriteAllText(Path.Combine(scratch, "idle_flipped.png"), "damaged public fixture");
            idle.Reload(); idle.WarmLeft(); FinishLeft(idle);
            Check(!idle.LeftReady && ReferenceEquals(idle.Art(true), idle.Strip), "damaged skin opposite cannot fall through to bundled art");
            Pet.SkinFolder = null; idle.Reload(); idle.WarmLeft();
            Pet.LegacyArt = true; idle.Reload();
            FinishLeft(idle);
            Check(idle.Old && !idle.LeftReady && ReferenceEquals(idle.Art(true), idle.Strip), "reload leaves no opposite from the previous art generation");
            Pet.LegacyArt = false; idle.Reload(); Left(idle);
            Check(idle.Count == 60 && idle.Art(true).PixelSize == idle.Strip.PixelSize, "reload can warm the current opposite after a stale read finishes");
            foreach (Clip clip in new[] { idle, blink, mouth }) clip.Reload();
        }
        finally { Pet.SkinFolder = previous; Pet.LegacyArt = legacy; Directory.Delete(scratch, true); }
        Console.WriteLine("PASS facing checks=" + passed);
    }
}
