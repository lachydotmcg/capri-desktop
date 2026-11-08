using CapriDesktop;
using System.Diagnostics;
using System.Text.Json;

internal static class Program
{
    private static int checks;
    private static void Check(bool valid, string name) { if (!valid) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
    private sealed class Desktop : IComputerDesktop
    {
        private ComputerSnapshot current = new("public-session", "public-layout", new("0x123", 123, "birth", "fixture", new(0,0,100,100), 1, 86, false), new("QA", new(0,0,100,100), 1, 0, 100, 100), 10, 10, true, 1, 1);
        public ComputerSnapshot Snapshot(string output = null, bool focus = false) => current;
        public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken cancel) => new() { Source = source, Area = source.Target.Area, Width = 100, Height = 100 };
        public void Move(int x, int y, CancellationToken cancel) => current = current with { PointerX = x, PointerY = y };
    }
    private const string Helper = """
import json, os, signal, sys, time
mode, path = sys.argv[1:]
stopped = False
state = dict(ready=False, authorized=False, held=False, released=False)
def save():
    temp = path + '.new'
    with open(temp, 'w') as output:
        json.dump(state, output)
    os.replace(temp, path)
def stop(*_):
    global stopped
    stopped = True
signal.signal(signal.SIGTERM, stop)
try:
    n = int(sys.stdin.buffer.readline())
    sys.stdin.buffer.read(n)
    if mode == 'broken_pipe':
        os.close(0)
    state['ready'] = True
    save()
    print('READY', flush=True)
    if mode == 'broken_pipe':
        while not stopped:
            time.sleep(.005)
    elif sys.stdin.buffer.read(1) == b'G':
        state['authorized'] = True
        if mode == 'drag':
            state['held'] = True
            save()
            print('STEP 1', flush=True)
            sys.stdin.buffer.read(1)
finally:
    state['held'] = False
    state['released'] = True
    save()
""";
    private static void Run(string condition)
    {
        string directory = Path.Combine(Path.GetTempPath(), "capri-cancel-input-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
        string script = Path.Combine(directory, "helper.py"), state = Path.Combine(directory, "state.json"); File.WriteAllText(script, Helper);
        using var cancelled = new CancellationTokenSource();
        var desktop = new Desktop(); ComputerFrame frame = desktop.Capture(desktop.Snapshot(), true, CancellationToken.None);
        bool drag = condition == "cancel_during_drag", cancel = condition.StartsWith("cancel", StringComparison.Ordinal);
        int guards = 0, completions = 0, starts = 0, pid = 0; Process child = null;
        var input = new ArchInput(script, _ => { starts++; child = Process.Start(ComputerProcess.Start("/usr/bin/python3", new[] { "-u", script, drag ? "drag" : condition == "uncancelled_broken_pipe" ? "broken_pipe" : "click", state })); pid = child.Id; return child; });
        Exception failure = null;
        try
        {
            var action = new ComputerAction { Action = drag ? "drag" : "click", X = 10, Y = 10, EndX = 20, EndY = 10, Duration = 500, Button = "left" };
            try
            {
                input.Execute(action, frame, desktop, (x,y) => {
                    guards++;
                    if (guards != (drag ? 4 : 3)) return;
                    if (cancel) cancelled.Cancel();
                    if (condition == "cancel_io") throw new IOException("fixture transport interruption");
                    if (condition == "cancel_invalid" || condition == "uncancelled_invalid") throw new InvalidOperationException("fixture authorization interruption");
                    if (condition == "uncancelled_disposed") child.StandardInput.Close();
                }, cancelled.Token, (x,y) => completions++);
            }
            catch (Exception error) { failure = error; }
            using JsonDocument observed = JsonDocument.Parse(File.ReadAllText(state)); var root = observed.RootElement;
            if (cancel)
            {
                Check(failure is OperationCanceledException ended && ended.CancellationToken == cancelled.Token && ended.InnerException != null, condition + " maps only the owning cancelled transport to OperationCanceledException");
                if (condition == "cancel_before_g" || drag) Check(failure.InnerException is ObjectDisposedException, condition + " reproduces the real closed-stdin write race as the preserved inner error");
                Check(root.GetProperty("authorized").GetBoolean() == drag, condition + " preserves the actual pre/post-authorization delivery boundary");
            }
            else Check(failure != null && failure is not OperationCanceledException && (condition == "uncancelled_broken_pipe" ? failure is IOException : condition == "uncancelled_invalid" ? failure is InvalidOperationException : failure is ObjectDisposedException), condition + " remains an actionable transport failure rather than cancellation or success");
            Check(starts == 1 && completions == 0 && root.GetProperty("ready").GetBoolean(), condition + " executes one production handshake without retry or completion callback");
            Check(root.GetProperty("released").GetBoolean() && !root.GetProperty("held").GetBoolean() && pid > 0 && !Directory.Exists("/proc/" + pid), condition + " finally stops the helper and observes its held-input cleanup");
        }
        finally { Directory.Delete(directory, true); }
    }
    public static void Main()
    {
        foreach (string condition in new[] { "cancel_before_g", "cancel_during_drag", "cancel_io", "cancel_invalid", "uncancelled_broken_pipe", "uncancelled_disposed", "uncancelled_invalid" }) Run(condition);
        Console.WriteLine("PASS production input cancellation checks=" + checks + "; no desktop connection or input");
    }
}
