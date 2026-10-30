using Avalonia;
using CapriDesktop;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
    checks++; Console.WriteLine("PASS " + message);
}
TaskCompletionSource<bool> Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
ChatPlacementTarget Target(int x) => new(new Point(x, 20), new Size(300, 80), 5);

var output = new HyprlandOutput(0, "HDMI-A-1", new Rect(0, 0, 1920, 1080), 1920, 1080,
    1, 0, 1, 0, false, new Rect(0, 30, 1920, 1050));
var reversed = new HyprlandDisplayMap(output, new PixelRect(3840, 0, 1920, 1080));
var target = ChatPlacementTarget.FromAvalonia(reversed, new PixelPoint(4000, 710), new Size(300, 80), 1);
Check(target.Origin == new Point(160, 710), "reversed XRandR output maps chat origin into compositor coordinates");
Check(target.Workspace == 1 && target.Size == new Size(300, 80), "chat preserves the pet output workspace and intended size");
var middle = new HyprlandDisplayMap(output with { Name = "DP-4", Logical = new Rect(1920, 0, 1920, 1080), Workspace = 5 }, new PixelRect(1920, 0, 1920, 1080));
Check(ChatPlacementTarget.FromAvalonia(middle, new PixelPoint(3325, 710), new Size(300, 80), 1).Origin == new Point(3325, 710), "reported DP-4 bubble target stays3325 rather than compositor minus515");
var scaled = new HyprlandDisplayMap(output with { Logical = new Rect(-1280, 0, 1280, 720), SpecialWorkspace = -7 }, new PixelRect(0, 0, 1920, 1080));
target = ChatPlacementTarget.FromAvalonia(scaled, new PixelPoint(150, 75), new Size(300, 80), 1.5);
Check(target.Origin == new Point(-1180, 50) && target.Size == new Size(300, 80), "OS scaling is applied once through the output-specific map");
Check(target.Workspace == -7, "chat follows the visible special workspace when present");

var entered = Signal(); var release = Signal();
var sent = new List<ChatPlacementTarget>();
int active = 0, maximum = 0;
using (var queue = new ChatPlacementQueue(async (next, cancellation) =>
{
    int count = Interlocked.Increment(ref active); maximum = Math.Max(maximum, count);
    lock (sent) sent.Add(next);
    try { if (next.Origin.X == 1) { entered.TrySetResult(true); await release.Task.WaitAsync(cancellation); } }
    finally { Interlocked.Decrement(ref active); }
}))
{
    Task first = queue.Queue(Target(1)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    _ = queue.Queue(Target(2)); Task newest = queue.Queue(Target(3)); release.SetResult(true);
    await newest.WaitAsync(TimeSpan.FromSeconds(3));
    Check(maximum == 1 && sent.Count == 2 && sent[1] == Target(3), "rapid animation updates coalesce to newest target with one native writer");
    await queue.Queue(Target(3));
    Check(sent.Count == 2, "unchanged successfully placed windows do not repeatedly issue IPC");
    queue.Suspend(true); await queue.Queue(Target(4));
    Check(sent.Count == 2, "hidden surface accepts no new placement writes");
    queue.Suspend(false); await queue.Queue(Target(3));
    Check(sent.Count == 3, "showing a remapped surface reissues even an unchanged origin");
}

entered = Signal(); var cancelled = Signal();
using (var queue = new ChatPlacementQueue(async (_, cancellation) =>
{
    entered.TrySetResult(true);
    try { await Task.Delay(Timeout.Infinite, cancellation); }
    catch (OperationCanceledException) { cancelled.TrySetResult(true); throw; }
}))
{
    Task moving = queue.Queue(Target(5)); await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
    queue.Suspend(true); await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(3)); await moving;
    Check(queue.Applied == null, "hiding cancels in-flight placement and prevents stale success");
    queue.Dispose(); await queue.Queue(Target(6));
    Check(queue.Applied == null, "closed surfaces cannot restart a writer");
}

int attempts = 0;
using (var queue = new ChatPlacementQueue((_, _) => { attempts++; throw new InvalidOperationException("fixture unmapped surface"); }))
{
    await queue.Queue(Target(7));
    Check(attempts == 4 && queue.Error == "fixture unmapped surface" && queue.Applied == null, "mapping retry is bounded and preserves the actual failure");
    await queue.Queue(Target(7));
    Check(attempts == 4, "animation ticks cannot flood a failed mapping with immediate retries");
}
Console.WriteLine(checks + " chat placement checks passed.");
