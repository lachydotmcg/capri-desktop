using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using CapriDesktop;

internal static class ComputerNativeFixture
{
    internal static int Run(string manifestPath, string evidence)
    {
        try
        {
            using JsonDocument manifest = JsonDocument.Parse(File.ReadAllBytes(manifestPath));
            JsonElement root = manifest.RootElement;
            string address = root.GetProperty("address").GetString();
            int pid = root.GetProperty("pid").GetInt32();
            string app = root.GetProperty("app_id").GetString();
            if (pid <= 0 || !app.StartsWith("io.github.capri.ArchFixture", StringComparison.Ordinal)) throw new InvalidOperationException("Only the explicitly named disposable Capri test fixture is permitted.");
            HyprlandDesktop service = HyprlandDesktop.Current ?? throw new InvalidOperationException("No Hyprland session.");
            using CancellationTokenSource cancel = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            ArchComputer desktop = new ArchComputer(service, cancel.Token);
            void Fixture()
            {
                HyprlandSnapshot native = service.CaptureSnapshotAsync(cancel.Token).GetAwaiter().GetResult();
                HyprlandWindow window = native.Windows.Single(item => item.Address == Convert.ToInt64(address.Substring(2), 16));
                if (window.Pid != pid || window.AppId != app || !window.Title.StartsWith("Capri disposable input fixture", StringComparison.Ordinal) || native.ActiveAddress != window.Address || native.Locked || !native.Visible(window))
                    throw new InvalidOperationException("The exact disposable fixture is not active and visible.");
            }
            Fixture();
            ComputerSnapshot bound = desktop.Snapshot();
            if (bound.Target.Address != address || bound.Target.Pid != pid) throw new InvalidOperationException("Fixture target mismatch.");
            if (bound.Target.Area.Intersect(bound.Output.Area) != bound.Target.Area) throw new InvalidOperationException("The entire fixture must be within one output.");
            if (Directory.Exists(evidence)) throw new IOException("Evidence output must be a new directory.");
            Directory.CreateDirectory(evidence);
            List<object> receipts = new List<object>();
            ComputerFrame Save(string name)
            {
                Fixture();
                ComputerSnapshot fresh = desktop.Snapshot();
                ComputerJobs.Same(bound, fresh, true);
                ComputerFrame frame = desktop.Capture(fresh, true, cancel.Token);
                if (frame.Area != bound.Target.Area) throw new InvalidOperationException("Capture escaped the fixture crop.");
                File.WriteAllBytes(Path.Combine(evidence, name + ".png"), frame.Png);
                File.WriteAllBytes(Path.Combine(evidence, name + ".jpg"), frame.Jpeg);
                receipts.Add(new { name, frame = frame.Id, image_width = frame.Width, image_height = frame.Height, logical_crop = new { frame.Area.X, frame.Area.Y, frame.Area.Width, frame.Area.Height }, png_sha256 = Convert.ToHexString(SHA256.HashData(frame.Png)), target = bound.Target, output = bound.Output });
                return frame;
            }
            ComputerFrame shown = Save("before");
            int step = 0;
            foreach (JsonElement proposed in root.GetProperty("actions").EnumerateArray())
            {
                if (++step > 12) throw new InvalidOperationException("Fixture step limit reached.");
                JsonObject actionJson = JsonNode.Parse(proposed.GetRawText()).AsObject();
                actionJson["frame"] = shown.Id;
                actionJson["previous_effect"] = "none";
                actionJson["risk"] = "none";
                actionJson["secret"] = false;
                actionJson["label"] = "exercise the disposable input fixture";
                ComputerAction action = ComputerAction.Parse(actionJson.ToJsonString(), shown);
                if (action.Action == "done" || action.Action == "ask") throw new InvalidOperationException("A fixture action must exercise input.");
                new ArchInput().Execute(action, shown, desktop, (x, y) =>
                {
                    cancel.Token.ThrowIfCancellationRequested();
                    Fixture();
                    ComputerSnapshot current = desktop.Snapshot();
                    ComputerJobs.Same(bound, current, true);
                    if (Math.Abs(current.PointerX - x) > 3 || Math.Abs(current.PointerY - y) > 3) throw new InvalidOperationException("Pointer interference interrupted the fixture test.");
                }, cancel.Token);
                if (cancel.Token.WaitHandle.WaitOne(100)) cancel.Token.ThrowIfCancellationRequested();
                shown = Save("after-" + step);
            }
            File.WriteAllText(Path.Combine(evidence, "receipt.json"), JsonSerializer.Serialize(new { pid, address, app_id = app, actions_delivered = step, model_used = false, application_effect = "Verify independently against the fixture state.json; input receipt alone is not application success.", receipts }, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine("PASS native fixture dispatch/capture: " + step + " actions; verify app state independently. No model was used.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine("FAIL native fixture: " + error.Message); return 1; }
    }
}
