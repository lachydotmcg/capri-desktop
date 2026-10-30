using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using CapriDesktop;

internal static class ComputerChecks
{
    private static int passed;
    private static void Check(bool condition, string name)
    {
        if (!condition) throw new Exception("FAIL " + name);
        Console.WriteLine("PASS " + name); passed++;
    }
    private static bool Reject(Action operation)
    {
        try { operation(); return false; }
        catch (InvalidOperationException) { return true; }
        catch (FormatException) { return true; }
        catch (JsonException) { return true; }
    }

    private sealed class HelperReached : Exception { }

    private static void CheckTextTargets(Desktop desktop)
    {
        JsonObject window = JsonNode.Parse("{\"address\":\"0x123\",\"pid\":123,\"mapped\":true,\"at\":[0,0],\"size\":[800,600],\"workspace\":{\"id\":1}}").AsObject();
        JsonArray clients = new JsonArray(window);
        window["xwayland"] = true;
        Check(HyprlandJson.Windows(clients.ToJsonString()).Single().XWayland == true, "Hyprland preserves explicit XWayland target identity");
        window["xwayland"] = false;
        Check(HyprlandJson.Windows(clients.ToJsonString()).Single().XWayland == false, "Hyprland preserves explicit native Wayland target identity");
        window.Remove("xwayland");
        Check(HyprlandJson.Windows(clients.ToJsonString()).Single().XWayland == null, "missing compositor identity stays unknown rather than native");
        window["xwayland"] = "false";
        Check(HyprlandJson.Windows(clients.ToJsonString()).Single().XWayland == null, "malformed compositor identity stays unknown rather than native");

        string helper = Path.GetTempFileName();
        try
        {
            int starts = 0, guards = 0;
            string mode = null;
            ArchInput input = new ArchInput(helper, start => { starts++; mode = start.ArgumentList[0]; throw new HelperReached(); });
            ComputerAction type = new ComputerAction { Action = "type", Text = "synthetic text" };
            foreach (bool? identity in new bool?[] { true, null })
            {
                ComputerFrame blocked = desktop.Capture(desktop.Current with { Target = desktop.Current.Target with { XWayland = identity } }, true, CancellationToken.None);
                blocked.Source = desktop.Current with { Target = desktop.Current.Target with { XWayland = identity } };
                string error = null;
                try { input.Execute(type, blocked, desktop, (x, y) => guards++, CancellationToken.None); }
                catch (InvalidOperationException rejected) { error = rejected.Message; }
                Check(starts == 0 && guards == 0 && error != null && error.Contains(identity == true ? "XWayland windows is disabled" : "identity is unknown"), "automated text rejects " + (identity == true ? "XWayland" : "unknown") + " before helper invocation or input guards");
            }
            ComputerFrame native = desktop.Capture(desktop.Current, true, CancellationToken.None);
            native.Source = native.Source with { Target = native.Source.Target with { XWayland = false } };
            bool eligible = false;
            try { input.Execute(type, native, desktop, (x, y) => guards++, CancellationToken.None); }
            catch (HelperReached) { eligible = true; }
            Check(eligible && starts == 1 && guards == 1 && mode == "text", "known native Wayland text remains eligible at the intercepted helper boundary");
            Check(Reject(() => ComputerJobs.Same(native.Source, native.Source with { Target = native.Source.Target with { XWayland = true } }, true)), "window backend identity changes invalidate the bound target");
            native.Source = native.Source with { Target = native.Source.Target with { XWayland = true } };
            eligible = false;
            try { input.Execute(new ComputerAction { Action = "press", Keys = "TAB" }, native, desktop, (x, y) => guards++, CancellationToken.None); }
            catch (HelperReached) { eligible = true; }
            Check(eligible && starts == 2 && mode == "key", "text-only guard leaves existing key actions unchanged without sending real input");
        }
        finally { File.Delete(helper); }
    }

    private sealed class Desktop : IComputerDesktop
    {
        internal ComputerSnapshot Current = new ComputerSnapshot("session", "layout", new ComputerTarget("0x123", 123, "1000:456", "fixture", new ComputerRect(-1000, -200, 800, 600), 1, 2), new ComputerOutput("DP-1", new ComputerRect(-1280, -200, 1280, 960), 1.5, 1, 1440, 1920), -500, 0, true);
        internal int Captures;
        public ComputerSnapshot Snapshot(string output = null, bool focus = false) { return Current; }
        public ComputerFrame Capture(ComputerSnapshot source, bool crop, CancellationToken token) { Captures++; return new ComputerFrame { Source = Current, Area = Current.Target.Area, Width = 1200, Height = 900, Jpeg = new byte[] { 1, 2, 3 } }; }
        public void Move(int x, int y, CancellationToken token) { Current = Current with { PointerX = x, PointerY = y }; }
    }
    private sealed class Input : IComputerInput
    {
        internal int Actions;
        public void Execute(ComputerAction action, ComputerFrame frame, IComputerDesktop desktop, Action<double, double> guard, CancellationToken token)
        {
            guard(frame.Source.PointerX, frame.Source.PointerY);
            Actions++;
        }
    }
    private static string Proposal(string frame, string action = "click", string previous = "none", string risk = "none", string label = "focus the search box")
    {
        return JsonSerializer.Serialize(new { action, frame, previous_effect = previous, risk, label, secret = false, x = 100, y = 100, button = "left" });
    }

    internal static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--native-fixture") return ComputerNativeFixture.Run(args[1], args[2]);
        try
        {
            Desktop desktop = new Desktop();
            CheckTextTargets(desktop);
            ComputerFrame frame = desktop.Capture(desktop.Current, true, CancellationToken.None);
            Check(frame.ToLogical(600, 450) == (-600, 100), "image pixels map once to negative logical origin at fractional scale");
            string[] cropArgs = ArchComputer.CaptureArguments(desktop.Current, frame.Area, true), outputArgs = ArchComputer.CaptureArguments(desktop.Current, desktop.Current.Output.Area, false);
            Check(cropArgs.Contains("-g") && !cropArgs.Contains("-o") && outputArgs.Contains("-o") && !outputArgs.Contains("-g") && cropArgs.Contains("1.5"), "grim uses mutually exclusive selected-output or bounded-crop arguments at native scale");
            Check(Reject(() => frame.ToLogical(-1, 0)) && Reject(() => frame.ToLogical(1200, 0)) && Reject(() => frame.ToLogical(double.NaN, 1)), "out-of-frame and nonfinite coordinates are rejected instead of clamped");
            Check(Reject(() => ComputerAction.Parse(Proposal("stale"), frame)), "stale frame proposal is rejected");
            Check(Reject(() => ComputerAction.Parse(Proposal(frame.Id).Replace("\"action\":\"click\"", "\"action\":\"click\",\"action\":\"type\""), frame)), "duplicate action fields are rejected");
            Check(Reject(() => ComputerJobs.Same(desktop.Current, desktop.Current with { Layout = "new" }, true)), "hotplug layout change rejects prior action binding");
            Check(Reject(() => ComputerJobs.Same(desktop.Current, desktop.Current with { Target = desktop.Current.Target with { Birth = "1000:999" } }, true)), "PID reuse rejects prior target binding");
            Check(Reject(() => ComputerJobs.Same(desktop.Current, desktop.Current with { Session = "other" }, true)) && Reject(() => ComputerJobs.Same(desktop.Current, desktop.Current with { Active = false }, true)), "changed session or focus revokes input");

            Security.Pick(Security.Trusted);
            ComputerJob task = new ComputerJob { Task = "send hi", Asked = "send hi" };
            ComputerAction send = new ComputerAction { Action = "click", Label = "send the message", Risk = "send" };
            Check(ComputerJobs.Permission(task, send) == null, "Trusted permits exactly requested sends without repeated confirmation");
            Security.Set(Security.Sends, false);
            Security.Set(Security.Unasked, true);
            Check(ComputerJobs.Permission(task, send) != null, "Sends disabled cannot be bypassed by Unasked enabled");
            Security.Pick(Security.Trusted);
            ComputerAction buy = new ComputerAction { Action = "click", Label = "pay the invoice", Risk = "none" };
            Check(ComputerJobs.Permission(task, buy).EndsWith(Security.Yes), "payment label is gated even when model calls it harmless");
            task.Approved = buy.ConsentKey;
            Check(ComputerJobs.Permission(task, buy) == null, "exact approved action proceeds once");
            ComputerAction payment = new ComputerAction { Action = "click", Label = "send payment", Risk = "buy", X = 10, Y = 10, Button = "left" };
            task.Approved = null;
            Check(ComputerJobs.Permission(task, payment).EndsWith(Security.Yes), "send label cannot downgrade declared payment risk");
            task.Approved = payment.ConsentKey;
            payment.X = 700; payment.Y = 500; payment.Button = "right";
            Check(ComputerJobs.Permission(task, payment).EndsWith(Security.Yes), "approval cannot authorize changed coordinates or button");
            ComputerAction first = new ComputerAction { Action = "type", Label = "input|a", Text = "b" };
            ComputerAction second = new ComputerAction { Action = "type", Label = "input", Text = "a|b" };
            Check(first.ConsentKey != second.ConsentKey, "consent encoding cannot collide across field boundaries");
            task.Approved = null;
            task.Asked = "draft a reply and do not send";
            Check(ComputerJobs.Permission(task, send) != null, "explicit draft remains unsent");
            Check(ComputerJobs.Permission(task, new ComputerAction { Action = "type", Text = "secret", Label = "type password", Risk = "secret", Secret = true }) != null, "private credential setting is enforced");
            Security.Pick(Security.Trusted);

            Input input = new Input();
            int thinking = 0;
            ComputerRunner runner = new ComputerRunner { Desktop = desktop, Input = input, Account = () => "account", Think = (jpeg, system, instruction) =>
            {
                using JsonDocument brief = JsonDocument.Parse(instruction);
                string id = brief.RootElement.GetProperty("frame").GetString();
                return Proposal(id, thinking++ == 0 ? "click" : "done", thinking == 1 ? "none" : "verified", "none", thinking == 1 ? "focus search" : "the search field is focused");
            }};
            string result = runner.Run(new ComputerJob { Task = "focus search", Asked = "focus search" }, CancellationToken.None);
            Check(input.Actions == 1 && thinking == 2 && result.StartsWith("Screen assessment:"), "job proposes one action then verifies through a fresh image");
            input.Actions = 0; thinking = 0;
            runner.Think = (jpeg, system, instruction) => { using JsonDocument brief = JsonDocument.Parse(instruction); return Proposal(brief.RootElement.GetProperty("frame").GetString(), "click", thinking++ == 0 ? "none" : "uncertain"); };
            result = runner.Run(new ComputerJob { Task = "focus search", Asked = "focus search" }, CancellationToken.None);
            Check(input.Actions == 1 && result.Contains("won't repeat"), "uncertain effect stops without repeated input");
            input.Actions = 0;
            runner.Think = (jpeg, system, instruction) => { desktop.Current = desktop.Current with { PointerX = desktop.Current.PointerX + 20 }; using JsonDocument brief = JsonDocument.Parse(instruction); return Proposal(brief.RootElement.GetProperty("frame").GetString()); };
            Check(Reject(() => runner.Run(new ComputerJob { Task = "focus search", Asked = "focus search" }, CancellationToken.None)) && input.Actions == 0, "owner pointer movement during inference prevents dispatch");
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            runner.Think = (jpeg, system, instruction) => { cancelled.Cancel(); using JsonDocument brief = JsonDocument.Parse(instruction); return Proposal(brief.RootElement.GetProperty("frame").GetString()); };
            bool stopped = false;
            try { runner.Run(new ComputerJob { Task = "focus search", Asked = "focus search" }, cancelled.Token); } catch (OperationCanceledException) { stopped = true; }
            Check(stopped && input.Actions == 0, "Stop during inference prevents native dispatch");
            string account = "first";
            runner.Account = () => account;
            runner.Think = (jpeg, system, instruction) => { account = "changed"; using JsonDocument brief = JsonDocument.Parse(instruction); return Proposal(brief.RootElement.GetProperty("frame").GetString()); };
            Check(Reject(() => runner.Run(new ComputerJob { Task = "focus search", Asked = "focus search" }, CancellationToken.None)) && input.Actions == 0, "account change during inference prevents dispatch");
            runner.Think = (jpeg, system, instruction) => { Security.Set(Security.Mouse, false); using JsonDocument brief = JsonDocument.Parse(instruction); return Proposal(brief.RootElement.GetProperty("frame").GetString()); };
            Check(Reject(() => runner.Run(new ComputerJob { Task = "focus search", Asked = "focus search" }, CancellationToken.None)) && input.Actions == 0, "Security changes revoke an already planned action");
            Security.Pick(Security.Trusted);

            string[] arguments = ArchApps.Arguments("\"/opt/My App/bin/app\" \"literal;$(touch sentinel)\" --percent=100%% %U", "Demo");
            Check(arguments.Length == 3 && arguments[0] == "/opt/My App/bin/app" && arguments[1] == "literal;$(touch sentinel)" && arguments[2] == "--percent=100%", "Exec quotes and shell metacharacters remain literal argv");
            string launched = null; string[] sent = null;
            ArchApps.Open("argv:" + JsonSerializer.Serialize(arguments), (executable, args) => { launched = executable; sent = args; });
            Check(launched == arguments[0] && sent.SequenceEqual(arguments.Skip(1)) && launched != "/bin/sh", "launch uses argument list without a shell");
            Check(Reject(() => ArchApps.Open("exec:echo unsafe", (a, b) => { })), "legacy shell-form entries are rejected");
            List<InstalledApp> tools = ArchApps.NativeTools(name => "/fixture/bin/" + name);
            ArchApps.Open(tools.Single(app => app.Name == "Fastfetch").Id, (executable, args) => { launched = executable; sent = args; });
            Check(launched == "/fixture/bin/kitty" && sent.SequenceEqual(new[] { "--hold", "/fixture/bin/fastfetch" }), "fastfetch opens in a persistent terminal with exact argv and no project root");
            string folder = Path.Combine(Path.GetTempPath(), "capri-app-checks-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(folder, "first")); Directory.CreateDirectory(Path.Combine(folder, "second"));
            try
            {
                File.WriteAllText(Path.Combine(folder, "first", "demo.desktop"), "[Desktop Entry]\nType=Application\nName=Hidden override\nHidden=true\n");
                File.WriteAllText(Path.Combine(folder, "second", "demo.desktop"), "[Desktop Entry]\nType=Application\nName=Unsafe shadow\nExec=demo\n");
                Check(ArchApps.Catalog(new[] { Path.Combine(folder, "first"), Path.Combine(folder, "second") }).Count == 0, "hidden user desktop entry masks system copy by canonical ID");
            }
            finally { Directory.Delete(folder, true); }
            Console.WriteLine(passed + " deterministic checks passed; no desktop capture, input, model request or owner-app launch was used.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }
}
