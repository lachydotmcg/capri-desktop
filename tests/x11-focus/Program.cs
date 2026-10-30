using CapriDesktop;
using System.Diagnostics;

using var cancellation = new CancellationTokenSource();
string mode = args.Single();
bool cancel = mode == "cancel";
if (cancel) _ = Task.Run(async () => { await Console.In.ReadLineAsync(); cancellation.Cancel(); });
var elapsed = Stopwatch.StartNew();
async Task Observe()
{
    try
    {
        bool focused = await HyprlandInputFocus.HasKeyboardFocusAsync(new IntPtr(123), cancellation.Token);
        if (mode == "focused" || mode == "proxy" || mode == "unfocused")
        {
            if (focused != (mode != "unfocused")) throw new Exception("Incorrect observed keyboard recipient.");
            return;
        }
        throw new Exception("A stalled server must not produce a focus result.");
    }
    catch (OperationCanceledException) when (cancel && cancellation.IsCancellationRequested) { }
    catch (InvalidOperationException error) when (!cancel && error.Message.Contains("timed out")) { }
}
await Task.WhenAll(Enumerable.Range(0, cancel ? 3 : 1).Select(_ => Observe()));
if (elapsed.ElapsedMilliseconds > 1800) throw new Exception("Focus observation exceeded its cancellation/deadline bound.");
Console.WriteLine("PASS " + mode + " reaped owned child in " + elapsed.ElapsedMilliseconds + "ms");
