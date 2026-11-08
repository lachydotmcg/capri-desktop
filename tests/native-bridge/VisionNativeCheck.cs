using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using CapriDesktop;

internal static class VisionNativeCheck
{
    private sealed class PublicProvider : IBrainProvider
    {
        public string Identity => "public-native-capture-fixture";
        public int Generation => 0;
        public void Cancel() { }
        public string Complete(string model, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose, string parts = null)
            => "Public fixture capture reached the vision route. No chess quality claim from this stand-in.";
    }

    internal static int Run(string manifestPath, string evidence, string settings = null)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var spec = manifest.RootElement;
        string output = spec.GetProperty("output").GetString(), address = spec.GetProperty("address").GetString();
        int pid = spec.GetProperty("pid").GetInt32();
        string selector = spec.GetProperty("selector").GetString(), prompt = spec.GetProperty("prompt").GetString();
        if (Directory.Exists(evidence)) throw new Exception("Use a new evidence folder.");
        Directory.CreateDirectory(evidence);
        var service = HyprlandDesktop.Current ?? throw new Exception("No native Hyprland connection.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        void PublicFixture()
        {
            var map = service.ObserveSnapshotAsync(timeout.Token).GetAwaiter().GetResult();
            var display = map.Outputs.Single(o => o.Name == output);
            var window = map.Windows.Single(w => w.Pid == pid && "0x" + w.Address.ToString("x") == address);
            if (map.Locked || !map.Visible(window) || window.Monitor != display.Id || !window.Title.StartsWith("Capri public chess fixture", StringComparison.Ordinal) ||
                window.Logical.X > display.Logical.X || window.Logical.Y > display.Logical.Y || window.Logical.Right < display.Logical.Right || window.Logical.Bottom < display.Logical.Bottom)
                throw new Exception("The exact public fixture must cover the requested output before capture.");
            if (map.Windows.Any(w => w.Pid != pid && w.AppId.StartsWith("Capri", StringComparison.OrdinalIgnoreCase) && map.Visible(w) && w.Logical.Intersects(display.Logical)))
                throw new Exception("Move companion overlays outside the public test output before capture.");
        }
        PublicFixture();
        var actions = new List<object>();var traces = new List<string>();
        VisionDiagnostics.Observer = line => { lock(traces) { traces.Add(line); File.AppendAllText(Path.Combine(evidence,"trace.jsonl"),line+"\n"); } };
        PlanBrain plan = null;
        IBrainProvider provider = new PublicProvider();string model = "public-fixture";
        if (settings != null)
        {
            using var config = JsonDocument.Parse(File.ReadAllText(settings));var c=config.RootElement;
            string Get(string key) => c.GetProperty(key).GetString();
            plan = new PlanBrain(Get("PlanPython"), Get("PlanAccounts"), Get("PlanModel"), Get("PlanVisionModel"), Get("PlanEffort"), Get("PlanVisionEffort"), 1400, 1600, false);
            provider = plan; model = Get("PlanModel");
        }
        var brain = new Brain { Model = model, Provider = provider };
        int captures = 0, inputs = 0;
        string Look(ToolArguments args)
        {
            return RequestedVision.Run(brain, args, (intent, screen) =>
            {
                PublicFixture();
                var observation = Eyes.Observe(intent, screen);
                if (observation.Frame == null || observation.Frame.Source.Output.Name != output || observation.WindowCrop) throw new Exception("Expected one full requested output, not another display or crop.");
                byte[] approvedImage = Eyes.Jpeg(observation.Png,2048);
                if (settings != null && (!spec.TryGetProperty("approved_image_sha256",out var approved) ||
                    !Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(approvedImage)).Equals(approved.GetString(),StringComparison.OrdinalIgnoreCase)))
                {
                    string rejected = Path.Combine(evidence,"rejected-private.png");
                    File.WriteAllBytes(rejected, observation.Png);
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(rejected, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    throw new Exception("The captured image differs from the inspected public payload; no model image was dispatched.");
                }
                File.WriteAllBytes(Path.Combine(evidence,"capture-"+(++captures)+".png"),observation.Png);
                File.WriteAllBytes(Path.Combine(evidence,"model-image-"+captures+".jpg"),Eyes.Jpeg(observation.Png,2048));
                return observation;
            });
        }
        brain.Act = (tool,args) =>
        {
            if (tool != "look_at_screen") { inputs++; throw new Exception("Read-only fixture rejects tool: " + tool); }
            string result = Look(args);
            actions.Add(new {tool,args.Screen,args.Focus,args.Asked,result});
            File.WriteAllText(Path.Combine(evidence,"actions.json"),JsonSerializer.Serialize(actions,new JsonSerializerOptions{WriteIndented=true}));
            return result;
        };
        try
        {
            if (settings != null)
            {
                string reply = brain.Ask(prompt);
                File.WriteAllText(Path.Combine(evidence,"reply.json"),JsonSerializer.Serialize(new{prompt,reply}));
                if (captures != 1 || inputs != 0) throw new Exception("Expected one read-only look from ordinary model routing.");
            }
            else
            {
                Hands.NativeLook = Look;
                int count = spec.TryGetProperty("count",out var n) ? n.GetInt32() : 1;
                if (count < 1 || count > 20) throw new Exception("Fixture capture count out of bounds.");
                for(int i=0;i<count;i++)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    string result = Hands.Do("look_at_screen",new ToolArguments{Asked=prompt,Focus="solve the displayed chess puzzle",Screen=selector,Current=()=>!timeout.IsCancellationRequested});
                    if (!result.StartsWith("You looked at their requested screen.",StringComparison.Ordinal)) throw new Exception(result);
                }
            }
            PublicFixture();
            File.WriteAllText(Path.Combine(evidence,"receipt.json"),JsonSerializer.Serialize(new{status="PASS_SCOPED",output,selector,pid,address,captures,input_actions=0,model_used=settings!=null,original_incident_reproduced=false},new JsonSerializerOptions{WriteIndented=true}));
            Console.WriteLine("PASS public native requested vision: "+captures+" capture(s), no input actions; model="+(settings!=null));return 0;
        }
        finally { plan?.Cancel();VisionDiagnostics.Observer=null;Hands.NativeLook=null; }
    }
}
