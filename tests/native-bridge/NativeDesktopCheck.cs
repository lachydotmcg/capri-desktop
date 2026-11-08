using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CapriDesktop;

internal static class NativeDesktopCheck
{
    internal static int Run(string settings, string manifest, string evidence)
    {
        using JsonDocument scope = JsonDocument.Parse(File.ReadAllText(manifest));
        int pid = scope.RootElement.GetProperty("pid").GetInt32();
        string app = scope.RootElement.GetProperty("app_id").GetString();
        string address = scope.RootElement.GetProperty("address").GetString();
        if (!app.StartsWith("io.github.capri.ArchFixture", StringComparison.Ordinal)) throw new Exception("Only the disposable public fixture is permitted.");
        HyprlandDesktop desktop = HyprlandDesktop.Current ?? throw new Exception("Hyprland unavailable");
        using CancellationTokenSource limit = new CancellationTokenSource(TimeSpan.FromSeconds(85));
        HyprlandSnapshot snapshot = desktop.CaptureSnapshotAsync(limit.Token).GetAwaiter().GetResult();
        HyprlandWindow fixture = snapshot.Windows.Single(w => w.Pid == pid && w.AppId == app && w.Address == Convert.ToInt64(address.Substring(2),16));
        if (!fixture.Title.StartsWith("Capri disposable input fixture", StringComparison.Ordinal)) throw new Exception("Unexpected fixture title");
        OpenWindow selected = desktop.ListWindows().Single(w => w.Handle.ToInt64() == fixture.Address);
        if (!desktop.Steer(selected,"focus")) throw new Exception(desktop.LastError);
        Thread.Sleep(200);
        ArchComputer computer = new ArchComputer(desktop,limit.Token);
        ComputerSnapshot bound = computer.Snapshot();
        if (bound.Target.Pid != pid || bound.Target.Address != address || !bound.Active) throw new Exception("Public fixture must be the active target");
        if (Directory.Exists(evidence)) throw new Exception("Use a new evidence directory");
        Directory.CreateDirectory(evidence);
        ComputerFrame before = computer.Capture(bound,true,limit.Token);
        if (before.Area != bound.Target.Area) throw new Exception("Fixture capture is not contained");
        File.WriteAllBytes(Path.Combine(evidence,"before.png"),before.Png);
        if (!ArchInput.Probe(limit.Token)) throw new Exception("Compositor input protocol unavailable");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(settings));
        JsonElement c = config.RootElement;
        string Get(string name) => c.GetProperty(name).GetString();
        PlanBrain plan = new PlanBrain(Get("PlanPython"),Get("PlanAccounts"),Get("PlanModel"),Get("PlanVisionModel"),Get("PlanEffort"),Get("PlanVisionEffort"),256,1024,c.GetProperty("OwnPlanAccount").GetBoolean());
        Brain brain = new Brain { Provider = plan, Model = Get("PlanModel") };
        Security.Pick(Security.Trusted);
        ComputerJob job = new ComputerJob { Task = "Click the Click fixture button exactly once. Finish only when Clicks: 1 is visible. Do not type, close or touch any other control.", Asked = "Click the disposable public fixture button once.", Binding = bound, Account = plan.Identity, Permissions = Security.Saved };
        int calls = 0;
        ComputerRunner runner = new ComputerRunner { Desktop=computer, Input=new ArchInput(), Account=() => plan.Identity, MostSteps=3,
            Think=(jpeg,system,instruction) =>
            {
                calls++;
                string proposal = brain.See(jpeg, system, instruction, 1024, true, preserveWhitespace: true, computerAction: true);
                File.WriteAllText(Path.Combine(evidence,"proposal-" + calls + ".json"),proposal);
                return proposal;
            }};
        string result;
        try { result=runner.Run(job,limit.Token); }
        finally { plan.Cancel(); }
        ComputerSnapshot final=computer.Snapshot(); ComputerJobs.Same(bound,final,true);
        ComputerFrame after=computer.Capture(final,true,limit.Token);File.WriteAllBytes(Path.Combine(evidence,"after.png"),after.Png);
        bool closed = desktop.Steer(desktop.ListWindows().Single(w => w.Handle.ToInt64() == fixture.Address), "close");
        File.WriteAllText(Path.Combine(evidence,"receipt.json"),JsonSerializer.Serialize(new { result, calls, closed, steps=job.Steps, model=Get("PlanVisionModel"), pid, app, width=before.Width,height=before.Height, nativeWayland=bound.Target.XWayland==false, privateContentSent=false, note="Compare public fixture state.json independently; a model completion statement alone is not success." }));
        Console.WriteLine(result);
        return 0;
    }
}
