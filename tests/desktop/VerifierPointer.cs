using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Raw;
using Avalonia.Platform;
namespace CapriDesktop;
internal static partial class DesktopTests
{
    private sealed class HeldCursor : IHyprlandTransport
    {
        internal readonly Fake Inner = new();
        internal bool Hold;
        internal TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public string SessionId => Inner.SessionId;
        public async Task<string> RequestAsync(string command, CancellationToken token)
        {
            var response = await Inner.RequestAsync(command, token);
            if (command == "j/cursorpos" && Hold)
            {
                Hold = false; Entered.SetResult();
                await Release.Task.WaitAsync(token);
            }
            return response;
        }
        public IAsyncEnumerable<string> Events(CancellationToken token) => Inner.Events(token);
    }
    // Only invocation timing is exposed; assertions use actual production consumers.
    private static Task ReadPointer(HyprlandDesktop service) => (Task)typeof(HyprlandDesktop).GetMethod("RefreshPointer", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(service, null)!;
    private static async Task VerifierPointerChecks()
    {
        for (int scenario = 0; scenario < 5; scenario++)
        {
            var fake = new HeldCursor();
            using var service = new HyprlandDesktop(fake, false, p => "synthetic", () => 1000);
            await service.CaptureSnapshotAsync(default);
            var desk = new HyprlandDesk(service);
            var screens = new[] { new Screen(1, new PixelRect(0, 0, 1920, 1080), new PixelRect(0, 0, 1920, 1080), true) };
            desk.BindScreens(screens);
            fake.Hold = true;
            var older = scenario == 1 ? service.CaptureSnapshotAsync(default) : ReadPointer(service);
            await fake.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            fake.Inner.Pointer = new Point(130, 140);
            if (scenario == 2) fake.Inner.Outputs[0]["x"] = 100;
            if (scenario == 3) fake.Inner.Locked = true;
            if (scenario == 4) fake.Inner.Session = "replacement-session";
            if (scenario == 1) await ReadPointer(service);
            else await service.CaptureSnapshotAsync(default);
            fake.Release.SetResult();
            await older;
            if (scenario == 3)
                Check(service.SamplePointer() == null && desk.Pointer() == null, "Delayed unlocked pointer cannot restore locked access");
            else
                Equal(new Point(130, 140), service.SamplePointer()!.Point, "Delayed old pointer is rejected in scenario " + scenario);
            if (scenario is 2 or 4)
                Check(desk.Pointer() == null, "Old map rejects changed layout/session sample " + scenario);
            if (scenario == 2)
            {
                var newScreens = new[] { new Screen(1, new PixelRect(100, 0, 1920, 1080), new PixelRect(100, 0, 1920, 1080), true) };
                desk.BindScreens(newScreens);
                Equal(new PixelPoint(130,140), desk.Pointer()!.Value, "Fresh matching map restores pointer movement");
            }
        }
        {
            var fake = new Fake();
            using var service = Service(fake);
            await service.CaptureSnapshotAsync(default);
            var gate = typeof(HyprlandDesktop).GetField("pointerLock", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(service)!;
            HyprlandDesktop.PointerSample result = null;
            var thread = new Thread(() => result = service.SamplePointer());
            lock (gate)
            {
                thread.Start();
                if (!SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 2000))
                    throw new Exception("Controlled sample reader did not reach pointer lock");
                fake.Locked = true;
                // Fake transport completes synchronously; production publication reenters gate.
                service.CaptureSnapshotAsync(default).GetAwaiter().GetResult();
            }
            if (!thread.Join(2000)) throw new Exception("Controlled sample reader did not complete");
            Check(result == null, "Lock publication between sample state-read and pointer lock revokes old reader");
        }
        // Own-window motion can continuously invalidate action targets without
        // changing output geometry. Visual sampling must not starve with it.
        for (int scenario = 0; scenario < 3; scenario++)
        {
            long now = 1000;
            var fake = new Fake();
            using var service = new HyprlandDesktop(fake, false, p => "synthetic", () => now);
            await service.CaptureSnapshotAsync(default);
            now = 3000;
            var epoch = typeof(HyprlandDesktop).GetField("epoch", BindingFlags.Instance | BindingFlags.NonPublic)!;
            fake.Before = command =>
            {
                if (command != "j/clients") return;
                epoch.SetValue(service, (long)epoch.GetValue(service)! + 1);
                if (scenario == 1) fake.Locked = true;
                if (scenario == 2) fake.Outputs[0]["x"] = 100;
            };
            bool rejected = false;
            try { await service.CaptureSnapshotAsync(default); } catch (InvalidOperationException) { rejected = true; }
            Check(rejected && service.Snapshot == null, "Action epoch change still rejects action snapshot " + scenario);
            if (scenario == 0)
                Check(service.VisualSnapshot != null && service.SamplePointer() != null, "Window-motion storm does not starve coherent visual pointer cache");
            else if (scenario == 1)
                Check(service.VisualSnapshot?.Locked == true && service.SamplePointer() == null, "Lock publication revokes visuals despite action epoch change");
            else
                Check(service.VisualSnapshot == null && service.SamplePointer() == null, "Changed output bracket cannot revive expired visual cache");
        }
        // A visual event arriving after the bracket but before publication must
        // invalidate the queued publisher inside the same monitor used by events.
        {
            long now = 1000;
            var fake = new Fake();
            using var service = new HyprlandDesktop(fake, false, p => "synthetic", () => now);
            await service.CaptureSnapshotAsync(default);
            now = 3000;
            var flagsLocal = BindingFlags.Instance | BindingFlags.NonPublic;
            var gate = typeof(HyprlandDesktop).GetField("pointerLock", flagsLocal)!.GetValue(service)!;
            var revision = typeof(HyprlandDesktop).GetField("visualEpoch", flagsLocal)!;
            var visual = typeof(HyprlandDesktop).GetField("visual", flagsLocal)!;
            Exception failure = null;
            var thread = new Thread(() => { try { service.CaptureSnapshotAsync(default).GetAwaiter().GetResult(); } catch (Exception e) { failure=e; } });
            lock (gate)
            {
                thread.Start();
                if (!SpinWait.SpinUntil(() => (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, 2000))
                    throw new Exception("Visual publisher did not reach publication monitor");
                revision.SetValue(service,(long)revision.GetValue(service)!+1);
                visual.SetValue(service,null);
            }
            if (!thread.Join(2000)) throw new Exception("Visual publisher did not complete");
            Check(failure is InvalidOperationException && service.Snapshot == null, "Late visual invalidation rejects queued strict capture");
            Check(service.VisualSnapshot == null && service.SamplePointer() == null, "Late visual invalidation cannot be overwritten by old-layout publisher");
        }
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var name in new[] { "Type", "Position", "Root" })
            Check(typeof(RawPointerEventArgs).GetProperty(name, flags) != null, "Pinned raw pointer API " + name);
        var handled = typeof(RawInputEventArgs).GetProperty("Handled", flags)!;
        Check(handled.PropertyType == typeof(bool) && handled.SetMethod != null, "Pinned Handled property is writable bool");
        var inputManager = typeof(Application).GetProperty("InputManager", flags)!;
        Check(inputManager != null, "Pinned Application InputManager property exists");
        Check(inputManager.PropertyType.GetProperty("PreProcess", flags) != null, "Pinned input preprocessor exists");
        var raw = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(RawPointerEventArgs));
        handled.SetValue(raw, true);
        Equal(true, (bool)handled.GetValue(raw)!, "Actual pinned reflection setter handles a raw event");
        Console.WriteLine("PASS focused verifier checks=" + checks + "; no compositor/UI/input/account/audio runtime");
    }
}
