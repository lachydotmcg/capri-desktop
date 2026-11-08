using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using CapriDesktop;

internal static class ObservationChecks
{
    private static int passed;
    private static void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL " + name); Console.WriteLine("PASS " + name); passed++; }
    private static bool Reject(Action operation) { try { operation(); return false; } catch (InvalidOperationException) { return true; } }
    private sealed class Desktop : IHyprlandTransport
    {
        internal string Session = "fixture-session";
        public string SessionId => Session;
        internal bool Locked;
        internal long Active = 0x22;
        internal Action<string> Before;
        internal JsonArray Outputs = JsonNode.Parse("[{\"id\":9,\"name\":\"DP-RIGHT\",\"x\":1920,\"y\":0,\"width\":1920,\"height\":1080,\"scale\":1,\"transform\":0,\"activeWorkspace\":{\"id\":2},\"focused\":true},{\"id\":3,\"name\":\"DP-LEFT\",\"x\":0,\"y\":0,\"width\":1920,\"height\":1080,\"scale\":1,\"transform\":0,\"activeWorkspace\":{\"id\":1}}]").AsArray();
        internal JsonArray Windows = JsonNode.Parse("[{\"address\":\"0x11\",\"pid\":5001,\"class\":\"browser\",\"title\":\"public board\",\"mapped\":true,\"at\":[0,0],\"size\":[1200,900],\"monitor\":3,\"workspace\":{\"id\":1},\"focusHistoryID\":1},{\"address\":\"0x22\",\"pid\":5002,\"class\":\"editor\",\"title\":\"other display\",\"mapped\":true,\"at\":[1920,0],\"size\":[1200,900],\"monitor\":9,\"workspace\":{\"id\":2},\"focusHistoryID\":0}]").AsArray();
        public Task<string> RequestAsync(string command, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Before?.Invoke(command);
            return Task.FromResult(command switch { "j/monitors" => Outputs.ToJsonString(), "j/clients" => Windows.ToJsonString(), "j/activewindow" => "{\"address\":\"0x" + Active.ToString("x") + "\"}", "j/cursorpos" => "{\"x\":50,\"y\":60}", "j/locked" => "{\"locked\":" + (Locked ? "true" : "false") + "}", _ => throw new Exception("Unexpected query " + command) });
        }
        public async IAsyncEnumerable<string> Events([System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token) { await Task.Delay(Timeout.Infinite, token); yield break; }
    }
    private static HyprlandDesktop Service(Desktop fake) => new HyprlandDesktop(fake, false, pid => pid + ":birth", () => 1000);
    private static void Increment(HyprlandDesktop service, string field)
    { FieldInfo member = typeof(HyprlandDesktop).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic); member.SetValue(service, (long)member.GetValue(service) + 1); }
    internal static void Run()
    {
        Desktop fake = new Desktop();
        using HyprlandDesktop service = Service(fake);
        ArchComputer computer = new ArchComputer(service, default, true, pid => pid + ":birth");
        ComputerSnapshot left = computer.Observe("DP-LEFT");
        Check(left.Output.Name == "DP-LEFT" && left.Target.Address == "desktop" && left.Target.Monitor == 3, "explicit read-only output uses its stable desktop identity despite another display's active app");
        Check(computer.Snapshot("DP-LEFT").Target.Address == "0x11", "strict requested-output selection cannot bind the active app on a different display");
        Check(Reject(() => new ArchComputer(service, default, true, pid => null).Observe()), "a window observation requires a nonempty process birth identity");
        ComputerSnapshot app = computer.Observe();
        Check(app.Target.Address == "0x22" && app.Output.Name == "DP-RIGHT", "legacy unqualified look still chooses its recent non-own application");
        fake.Active = 0x11;
        Check(computer.Observe("DP-RIGHT", app.Target).Target == app.Target, "a crop re-observes its exact window when focus changes elsewhere");
        fake.Windows[1]["pid"] = Environment.ProcessId;
        Check(computer.Observe().Target.Address == "0x11", "own Capri surfaces are excluded by process identity");
        Check(Reject(() => computer.Observe("DP-RIGHT", app.Target)), "an own/replaced crop target is not rebound to another window");
        fake.Windows[1]["pid"] = 5002;
        fake.Windows[1]["size"] = new JsonArray(1201, 900);
        Check(Reject(() => computer.Observe("DP-RIGHT", app.Target)), "window crop geometry changes revoke the capture binding");
        Check(Reject(() => computer.Observe("missing-output")), "a missing requested display never falls back to another output");
        ComputerSnapshot unchanged = left with { FocusEpoch = left.FocusEpoch + 10, Active = !left.Active, Target = app.Target };
        ArchComputer.SameObservation(left, unchanged, false);
        Check(true, "full-output read-only validation tolerates unrelated focus and app changes");
        Check(Reject(() => ComputerJobs.Same(left, left with { FocusEpoch = left.FocusEpoch + 1 }, true)), "the same focus change continues to revoke actual input authorization");
        Check(Reject(() => ArchComputer.SameObservation(left, unchanged, true)), "window-crop read-only validation keeps exact target identity");
        foreach (var change in new[] { ("session", left with { Session = "new" }), ("layout", left with { Layout = "new" }), ("scale", left with { Output = left.Output with { Scale = 2 } }), ("transform", left with { Output = left.Output with { Transform = 1 } }), ("lock/output epoch", left with { ObservationEpoch = left.ObservationEpoch + 1 }) })
            Check(Reject(() => ArchComputer.SameObservation(left, change.Item2, false)), "read-only observation rejects changed " + change.Item1);
        ComputerRect large = new ComputerRect(-1000, -200, 5120, 2880);
        ComputerSnapshot largeSource = left with { Output = left.Output with { Area = large, Scale = 2 } };
        string[] args = ArchComputer.CaptureArguments(largeSource, large, true);
        Check(args[1] == "-1000,-200 5120x2880" && args[3] == "0.4", "large capture downscales the full area instead of removing its right and bottom edges");
        ComputerFrame frame = new ComputerFrame { Source = largeSource, Area = large, Width = 2048, Height = 1152 };
        Check(frame.ToLogical(2047, 1151).X >= 4117 && frame.ToLogical(2047, 1151).Y >= 2677, "downscaled screenshot coordinates still map to the full far edge");
        fake.Before = command => { if (command == "j/clients") Increment(service, "epoch"); };
        Check(Reject(() => service.CaptureSnapshotAsync(default).GetAwaiter().GetResult()), "unrelated event churn still rejects strict action snapshots");
        HyprlandSnapshot read = service.ObserveSnapshotAsync(default).GetAwaiter().GetResult();
        Check(read.Outputs.Length == 2 && !read.Locked && service.Snapshot == null, "fresh read-only observation succeeds under unrelated event churn without publishing action authority");
        fake.Before = command => { if (command == "j/clients") Increment(service, "visualEpoch"); };
        Check(Reject(() => service.ObserveSnapshotAsync(default).GetAwaiter().GetResult()), "read-only observation still rejects in-flight layout/lock epoch changes");
        fake.Before = null;
        int lockReads = 0;
        fake.Before = command => { if (command == "j/locked" && ++lockReads == 2) fake.Locked = true; };
        Check(Reject(() => computer.Observe("DP-LEFT")), "a lock appearing on the final read withholds the observation");
        fake.Locked = false;
        fake.Before = command => { if (command == "j/clients") fake.Session = "replaced-session"; };
        Check(Reject(() => computer.Observe("DP-LEFT")), "a session replacement during ordered IPC reads revokes the observation");
        fake.Session = "fixture-session";
        int monitorReads = 0;
        fake.Before = command => { if (command == "j/monitors" && ++monitorReads == 2) fake.Outputs[0]["scale"] = 1.25; };
        Check(Reject(() => computer.Observe("DP-LEFT")), "a scale change in the final output bracket rejects a mixed-layout observation");
        fake.Outputs[0]["scale"] = 1;
        fake.Before = null; fake.Locked = true;
        Check(Reject(() => computer.Observe("DP-LEFT")), "a currently locked desktop cannot be observed");
        fake.Locked = false;
        using CancellationTokenSource stop = new CancellationTokenSource(); stop.Cancel();
        bool cancelled = false;
        try { new ArchComputer(service, stop.Token, true, pid => "birth").Observe("DP-LEFT"); } catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled, "a cancelled observation generation cannot start a fresh desktop read");
        Console.WriteLine("PASS " + passed + " fresh read-only observation checks");
    }
}
