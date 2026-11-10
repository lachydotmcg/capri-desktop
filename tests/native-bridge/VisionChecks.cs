using System;
using System.Linq;
using System.Threading;
using Avalonia;
using CapriDesktop;

internal static class VisionChecks
{
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action operation, string name)
    {
        bool rejected = false;
        try { operation(); } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, name);
    }
    private static HyprlandOutput Output(int id, string name, double x, double y = 0, int width = 1920, int height = 1080, bool focused = false)
        => new HyprlandOutput(id, name, new Rect(x, y, width, height), width, height, 1, 0, 1, 0, focused, new Rect(x, y, width, height));
    private static HyprlandSnapshot Map(params HyprlandOutput[] outputs)
        => new HyprlandSnapshot("session", "layout", outputs, Array.Empty<HyprlandWindow>(), new Point(100, 100), 0, false, 0);

    internal static void Run()
    {
        var left = Output(9, "HDMI-A-5", -1920);
        var center = Output(2, "DP-4", 0, focused: true);
        var right = Output(17, "DP-8", 1920);
        var map = Map(right, left, center);
        string Pick(string screen = null, string task = null) => ScreenSelection.Resolve(map, screen, task);
        Check(Pick("2") == "DP-4", "physical second output ignores compositor enumeration and IDs");
        Check(ScreenSelection.Resolve(Map(center, right, left), "second", null) == "DP-4", "ordinal remains stable when live enumeration changes");
        Check(Pick("screen 2") == "DP-4" && Pick("2nd") == "DP-4", "explicit wrapped ordinal aliases select the intended output");
        Check(Pick("dp-8") == "DP-8" && Pick("monitor DP-8") == "DP-8", "native connector matching is case-insensitive and bounded");
        Check(Pick("left") == "HDMI-A-5" && Pick("right") == "DP-8", "left and right select unique physical edges");
        Check(Pick("here") == "DP-4" && Pick("active") == "DP-4", "pointer and focused aliases use explicit live identities");
        Check(Pick("next") == "DP-8" && Pick("previous") == "HDMI-A-5", "relative ordinal aliases are physical rather than compositor indexed");
        Check(Pick("left of here") == "HDMI-A-5" && Pick("right of here") == "DP-8", "explicit relative directions start from the pointer output");
        Check(Pick(task: "Solve the puzzle on my second monitor") == "DP-4", "older focus task retains its explicit ordinal monitor hint");
        Check(Pick(task: "Read monitor 3") == "DP-8", "monitor-before-number hint is recognized");
        Check(Pick(task: "Solve the puzzle on DP-8") == "DP-8", "task native connector references are selected");
        Check(Pick(task: "DP-8") == "DP-8", "legacy exact focus connector remains supported");
        Check(Pick(task: "Explain the second move and the right square") == null, "ordinary task words do not silently select a display");
        Check(Pick(task: "Explain the filename DP-8-backup.txt") == null, "native name substring in unrelated task data does not select a display");
        Check(Pick(task: "Read /tmp/DP-8") == null, "native connector inside a path is not a display request");
        Check(Pick(task: "Read on DP-8-backup") == null, "native name requires a complete connector boundary");
        Reject(() => Pick("DP-8", "Solve the puzzle on my second monitor"), "explicit model screen cannot override a conflicting owner monitor request");
        Check(Pick("DP-4", "Solve the puzzle on my second monitor") == "DP-4", "matching explicit and owner screen selectors agree");
        Reject(() => Pick(task: "Solve the puzzle on DP-99"), "disconnected native connector hint cannot fall back to another output");
        Reject(() => Pick("banana"), "unknown explicit alias cannot silently become next display");
        Reject(() => Pick("4"), "disconnected display ordinal is rejected");
        Reject(() => Pick("main"), "missing primary-display identity is reported honestly");
        Reject(() => Pick("other"), "other is ambiguous with three displays");
        Reject(() => Pick(task: "Read my second monitor and third monitor"), "conflicting task hints are rejected");
        Reject(() => Pick(task: "Read left and right monitors"), "plural multiple-monitor request cannot fall back to a default output");
        Reject(() => Pick(task: "Read all screens"), "all-display capture is not silently authorized");
        var twin = Map(center, center with { Id = 7, Name = "DP-7" });
        Reject(() => ScreenSelection.Resolve(twin, "first", null), "overlapping outputs make geometric ordinal ambiguous");
        Check(ScreenSelection.Resolve(twin, "DP-7", null) == "DP-7", "exact connector disambiguates overlapping outputs");
        Reject(() => ScreenSelection.Resolve(twin, "here", null), "overlapping pointer outputs are ambiguous");
        var portrait = Output(3, "DP-9", 1920, width: 1080, height: 1920);
        Check(ScreenSelection.Resolve(Map(center, portrait), "portrait", null) == "DP-9", "unique portrait alias follows logical geometry");
        Reject(() => ScreenSelection.Resolve(Map(portrait, portrait with { Id = 8, Name = "DP-10", Logical = new Rect(3000, 0, 1080, 1920) }), "portrait", null), "multiple portrait outputs require disambiguation");
        Check(ScreenSelection.Resolve(Map(center, right), "other", null) == "DP-8", "other is valid when exactly one alternate exists");
        var upper = Output(5, "DP-5", 0, -1080);
        Check(ScreenSelection.Resolve(Map(center, upper), "up", null) == "DP-5", "up resolves relative to current pointer display");
        Reject(() => ScreenSelection.Resolve(Map(center, upper), "left of here", null), "missing relative direction is rejected");

        int attempts = 0, fences = 0;
        string result = Eyes.Fresh(number => { attempts++; if (number < 3) throw new ComputerObservationChangedException("revoked"); return "fresh"; }, () => fences++);
        Check(result == "fresh" && attempts == 3 && fences >= 6, "read-only observation retries at most three fresh attempts with cancellation fences");
        attempts = 0;
        Reject(() => Eyes.Fresh<string>(number => { attempts++; throw new ComputerObservationChangedException("revoked"); }, () => { }), "third revoked observation is reported without unbounded retry");
        Check(attempts == 3, "retry exhaustion has exactly three capture attempts");
        attempts = 0;
        Reject(() => Eyes.Fresh<string>(number => { attempts++; throw new InvalidOperationException("locked"); }, () => { }), "ordinary selection and lock failures do not retry");
        Check(attempts == 1, "nonrevocation failures run once");
        attempts = 0; bool cancelled = false;
        try { Eyes.Fresh<string>(number => { attempts++; throw new ComputerObservationChangedException("revoked"); }, () => { if (attempts == 1) throw new OperationCanceledException(); }); }
        catch (OperationCanceledException) { cancelled = true; }
        Check(cancelled && attempts == 1, "Stop between revoked attempts prevents another capture");
        bool postCancelled = false;
        try { Eyes.Fresh(number => { attempts++; return "captured"; }, () => { if (attempts > 1) throw new OperationCanceledException(); }); }
        catch (OperationCanceledException) { postCancelled = true; }
        Check(postCancelled, "post-capture cancellation fence prevents a late result");
        long old = Eyes.Ticket(); Eyes.Stop();
        bool stale = false; try { Eyes.CheckTicket(old); } catch (OperationCanceledException) { stale = true; }
        Check(stale, "Stop revokes an earlier observation ticket");
        Eyes.CheckTicket(Eyes.Ticket());
        var shoot = Eyes.Shoot;
        try { Eyes.Shoot = () => new byte[] { 1, 2, 3 }; Check(Eyes.Request("fixture").SequenceEqual(new byte[] { 1, 2, 3 }), "existing screenshot fixture override remains compatible"); }
        finally { Eyes.Shoot = shoot; }
        Console.WriteLine("PASS vision checks=" + passed);
    }
}
