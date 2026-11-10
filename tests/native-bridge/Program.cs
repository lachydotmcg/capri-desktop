using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using CapriDesktop;

internal static class Program
{
    private static int passed;
    private static void Check(bool value, string name) { if (!value) throw new Exception(name); passed++; Console.WriteLine("PASS " + name); }
    private static void Reject(Action action, string name)
    {
        bool rejected = false;
        try { action(); } catch (BrainException) { rejected = true; } catch (IOException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Check(rejected, name);
    }
    private static int Main(string[] args)
    {
        NativeFontBootstrap.Initialize();
        if (args.Length == 1 && args[0] == "--tool-lifecycle") { ToolLifecycleChecks.Run(); return 0; }
        if (args.Length == 4 && args[0] == "--terminal-model") return TerminalModelCheck.Run(args[1], args[2], args[3]);
        if (args.Length == 4 && args[0] == "--native-model") return NativeDesktopCheck.Run(args[1], args[2], args[3]);
        if (args.Length == 4 && args[0] == "--voice") return VoiceCheck(args[1], args[2], args[3]);
        if (args.Length == 3 && args[0] == "--audio") return AudioCheck(args[1], args[2]);
        if (args.Length == 3 && args[0] == "--plan") return PlanCheck(args[1], args[2]);
        if (args.Length == 1 && args[0] == "--requested-vision") { RequestedVisionChecks.Run(); return 0; }
        if (args.Length == 3 && args[0] == "--vision-native") return VisionNativeCheck.Run(args[1], args[2]);
        if (args.Length == 4 && args[0] == "--vision-model") return VisionNativeCheck.Run(args[2], args[3], args[1]);
        if (args.Length == 1 && args[0] == "--vision-routing") { VisionRoutingChecks.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--vision") { VisionChecks.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--click") { ClickPlaybackChecks.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--native-editor") { NativeEditorChecks.Run(); return 0; }
        if (args.Length == 2 && args[0] == "--native-editor-fixture") return NativeEditorFixture.Run(args[1]);
        if (args.Length == 1 && args[0] == "--name-listening") { NameListeningChecks.Run(); return 0; }
        if (args.Length == 1 && args[0] == "--facing") { FacingChecks.Run(); return 0; }
        FacingChecks.Run();
        if (args.Length == 1 && args[0] == "--name-hooks") { NameHookChecks.Run(); return 0; }
        NameHookChecks.Run();
        NativeEditorChecks.Run();
        NameListeningChecks.Run();
        ToolLifecycleChecks.Run();
        TerminalChecks.Run();
        ClickPlaybackChecks.Run();
        VisionChecks.Run();
        RequestedVisionChecks.Run();
        VisionRoutingChecks.Run();
        string catalog = "[{\"type\":\"function\",\"function\":{\"name\":\"open_app\",\"parameters\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}}}]";
        List<KeyValuePair<string, ToolArguments>> calls = new List<KeyValuePair<string, ToolArguments>>();
        Check(PlanBrain.Proposals("{\"text\":\"\",\"calls\":[{\"name\":\"open_app\",\"arguments\":{\"name\":\"kitty\"}}]}", catalog, calls) == "" && calls.Count == 1 && calls[0].Value.Name == "kitty", "typed proposal preserves registered argument");
        foreach (string bad in new[] {
            "{\"text\":\"\",\"calls\":[{\"name\":\"shell\",\"arguments\":{\"name\":\"kitty\"}}]}",
            "{\"text\":\"\",\"calls\":[{\"name\":\"open_app\",\"arguments\":{}}]}",
            "{\"text\":\"\",\"calls\":[{\"name\":\"open_app\",\"arguments\":{\"name\":4}}]}",
            "{\"text\":\"\",\"calls\":[{\"name\":\"open_app\",\"arguments\":{\"name\":\"a\",\"name\":\"b\"}}]}",
            "{\"text\":\"\",\"calls\":[{\"name\":\"open_app\",\"arguments\":{\"name\":\"a\",\"command\":\"echo bad\"}}]}" })
        { int before = calls.Count; Reject(() => PlanBrain.Proposals(bad, catalog, calls), "invalid proposal denied"); Check(calls.Count == before, "rejected proposal has no partial action admission"); }
        foreach (string request in new[] { "restart example.service", "Please restart the user service example.service.", "restart example.service please" })
            Check(LinuxRoutes.ServiceRequested(request, "restart", "example.service"), "affirmative exact service request admitted");
        foreach (string request in new[] { "do not restart example.service", "can you explain restart example.service", "restart another.service", "stop example.service", "restart example.service and stop other.service", "if it fails restart example.service", "restart exampleXservice", "restart Example.service" })
            Check(!LinuxRoutes.ServiceRequested(request, "restart", "example.service"), "negative conditional mismatched or compound service request denied");
        Action<string> originalRun = Hands.Run;
        string opened = null;
        try
        {
            Hands.Run = address => opened = address;
            Check(LinuxRoutes.Website(new ToolArguments { Site = "http://localhost:8123/test", Asked = "open http://localhost:8123/test" }).StartsWith("Opened") && opened == "http://localhost:8123/test", "explicit exact loopback URL uses native browser route");
            opened = null;
            foreach (string request in new[] { "open something", "do not open http://localhost:8123/test", "open http://localhost:8124/test" })
                Check(!LinuxRoutes.Website(new ToolArguments { Site = "http://localhost:8123/test", Asked = request }).StartsWith("Opened") && opened == null, "unrequested or mismatched local URL cannot launch");
            Check(LinuxRoutes.Website(new ToolArguments { Site = "https://example.com", Asked = "open https://example.com" }) == null, "public URL validation remains upstream-owned");
        }
        finally { Hands.Run = originalRun; }
        using (MemoryStream bytes = new MemoryStream(Encoding.UTF8.GetBytes("bounded\nnext\n")))
        using (StreamReader reader = new StreamReader(bytes)) Check(Bridge.ReadLine(reader, 7) == "bounded" && Bridge.ReadLine(reader, 7) == "next", "bounded framing preserves message boundaries");
        using (MemoryStream bytes = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', 10000))))
        using (StreamReader reader = new StreamReader(bytes)) Reject(() => Bridge.ReadLine(reader, 32), "oversized bridge line is rejected while reading");
        if (OperatingSystem.IsLinux())
        {
            string identity = LinuxProcess.Identity(Environment.ProcessId);
            Check(identity.Split(':').Length >= 7, "exact process identity includes executable");
            Reject(() => LinuxProcess.ForceClose(Environment.ProcessId, identity), "self force close denied");
            using Process child = Process.Start(new ProcessStartInfo("/usr/bin/sleep", "30") { UseShellExecute = false });
            try
            {
                string bound = LinuxProcess.Identity(child.Id);
                Reject(() => LinuxProcess.ForceClose(child.Id, bound + "changed"), "mismatched executable ticket sends no signal");
                Check(!child.HasExited, "mismatch leaves disposable target alive");
                LinuxProcess.ForceClose(child.Id, bound);
                Check(child.WaitForExit(3000), "exact pidfd closes disposable target");
            }
            finally { if (!child.HasExited) child.Kill(); }
        }
        Console.WriteLine("PASS total=" + passed);
        return 0;
    }

    private static int AudioCheck(string wave, string output)
    {
        byte[] wav = File.ReadAllBytes(wave);
        string failed = null; SpeechAudio.Failed += message => failed = message;
        Stopwatch clock = Stopwatch.StartNew();
        SpeechAudio.Play(wav);
        Check(SpeechAudio.Busy && SpeechAudio.MicrophoneHeld, "actual playback child holds microphone gate");
        while (SpeechAudio.Busy && clock.ElapsedMilliseconds < 15000) Thread.Sleep(20);
        Check(!SpeechAudio.Busy && failed == null, "actual PulseAudio playback drains without error");
        long drained = clock.ElapsedMilliseconds;
        Check(drained >= Voice.Seconds(wav) * 700, "audio completion follows device drain rather than stdin write");
        SpeechAudio.Play(wav); Thread.Sleep(100); SpeechAudio.Stop();
        Check(!SpeechAudio.Busy, "Stop retires the playback child immediately");
        File.WriteAllText(output, JsonSerializer.Serialize(new { seconds = Voice.Seconds(wav), drainMilliseconds = drained, stopPassed = true, humanListening = "unverified", microphoneOpened = false }));
        return 0;
    }

    private static int PlanCheck(string settings, string directory)
    {
        Directory.CreateDirectory(directory);
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(settings));
        JsonElement c = config.RootElement;
        string Get(string name) { return c.GetProperty(name).GetString(); }
        PlanBrain plan = new PlanBrain(Get("PlanPython"), Get("PlanAccounts"), Get("PlanModel"), Get("PlanVisionModel"), Get("PlanEffort"), Get("PlanVisionEffort"), 256, 1024, c.TryGetProperty("OwnPlanAccount", out JsonElement owned) && owned.GetBoolean());
        string prompt = "For this public integration test, reply exactly CAPRI-42. Remember this public marker for the next message.";
        var messages = new List<ChatMessage> { new ChatMessage("system", "You are testing a desktop chat adapter with public fixtures. Never execute tools."), new ChatMessage("user", prompt) };
        var calls = new List<KeyValuePair<string, ToolArguments>>();
        int deltas = 0;
        Stopwatch clock = Stopwatch.StartNew();
        string answer = plan.Complete(Get("PlanModel"), messages, null, calls, 256, text => deltas++, "chat");
        Check(answer.Contains("CAPRI-42") && deltas > 0, "real selected model streams and completes public marker");
        messages.Add(new ChatMessage("assistant", answer)); messages.Add(new ChatMessage("user", "What was the public marker? Reply with only the marker."));
        string followup = plan.Complete(Get("PlanModel"), messages, null, calls, 128, null, "chat");
        Check(followup.Contains("CAPRI-42"), "real follow-up retains only fresh public context");
        string catalog = "[{\"type\":\"function\",\"function\":{\"name\":\"open_app\",\"parameters\":{\"type\":\"object\",\"properties\":{\"name\":{\"type\":\"string\"}},\"required\":[\"name\"]}}}]";
        string proposed = plan.Complete(Get("PlanModel"), new List<ChatMessage> { new ChatMessage("user", "Propose opening Fastfetch using the provided open_app function. This is a dry-run proposal only.") }, catalog, calls, 256, null, "actions");
        Check(calls.Count == 1 && calls[0].Key == "open_app" && calls[0].Value.Name.Equals("Fastfetch", StringComparison.OrdinalIgnoreCase), "real model action proposal passes typed schema without execution");
        bool cancelled = false; int cancelledDeltas = 0;
        try { plan.Complete(Get("PlanModel"), new List<ChatMessage> { new ChatMessage("user", "Count from 1 to 200, one number per line, with no introduction.") }, null, new List<KeyValuePair<string, ToolArguments>>(), 1024, text => { if (++cancelledDeltas == 2) plan.Cancel(); }, "chat"); }
        catch (Exception) { cancelled = cancelledDeltas >= 2; }
        Check(cancelled, "real streamed response cancelled before terminal completion");
        File.WriteAllText(Path.Combine(directory, "receipt.json"), JsonSerializer.Serialize(new { model = Get("PlanModel"), answer, followup, deltas, cancelledDeltas, cancelled, action = calls[0].Key, actionExecuted = false, milliseconds = clock.ElapsedMilliseconds, privateHistorySent = false }));
        return 0;
    }

    private static int VoiceCheck(string python, string profile, string directory)
    {
        Directory.CreateDirectory(directory);
        using OwnedSpeaker speaker = new OwnedSpeaker { Python = python, Profile = profile, Cache = Path.Combine(directory, "cache") };
        var records = new List<object>();
        foreach (string text in new[] { "Capri microphone test is ready.", string.Join(" ", Enumerable.Repeat("The local voice test is ready.", 16)) })
        {
            Stopwatch clock = Stopwatch.StartNew();
            byte[] wav = speaker.Speak(text);
            string path = Path.Combine(directory, "public-" + records.Count + ".wav"); File.WriteAllBytes(path, wav);
            records.Add(new { characters = text.Length, milliseconds = clock.ElapsedMilliseconds, seconds = Voice.Seconds(wav), bytes = wav.Length, file = Path.GetFileName(path) });
        }
        File.WriteAllText(Path.Combine(directory, "receipt.json"), JsonSerializer.Serialize(records));
        Console.WriteLine(JsonSerializer.Serialize(records));
        return 0;
    }
}
