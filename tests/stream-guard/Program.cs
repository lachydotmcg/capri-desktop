using System;
using System.Collections.Generic;
using CapriDesktop;
using SkiaSharp;

internal static class Program
{
    private const string Refusal = "I can't safely stream on this desktop because I can't check permissions for each action.";
    private static int captures, actions, calls, failures;
    private static string nested, during, changed, mode;
    private static bool nestedFlag, streamPrompt;
    private static byte[] frame;

    private sealed class FakeBrain : IBrainProvider
    {
        public string Identity => "fixture";
        public int Generation => 0;
        public void Cancel() { }
        public string Complete(string model, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> next, int limit, Action<string> streamed, string purpose, string parts = null)
        {
            calls++;
            if (messages[0].Content.Contains(Chores.StreamRules)) streamPrompt = true;
            if (calls == 1) return "{\"do\":\"click\",\"x\":5,\"y\":5}";
            return "{\"do\":\"done\",\"said\":\"Finished fixture.\"}";
        }
    }

    private static Brain Prepare(bool tools)
    {
        captures = actions = calls = 0; nested = during = changed = null; nestedFlag = streamPrompt = false; mode = "";
        Hands.Streaming = false; Hands.OnMac = false; Hands.OwnHands = true; Hands.NativeWork = null;
        Watching.Quiet = () => true; Hands.TerminalAvailable = () => false; Chores.Skills = null;
        Security.Set(Security.Mouse, true); Security.Set(Security.Pays, false); Security.Set(Security.Sends, false); Security.Set(Security.Unasked, false);
        Chores.Doing = (what, order) => { actions++; };
        Chores.Parked = () => { }; Chores.SignHidden = hide => false;
        var brain = new Brain { Model = "fixture", OwnSteps = tools, Provider = new FakeBrain() };
        Hands.Work = (task, asked) => Chores.Run(brain, task, asked);
        Chores.Shoot = () => {
            captures++;
            if (mode == "change" && captures == 1) {
                during = SelfJobs.Control(new ToolArguments { Action = "status" });
                changed = SelfJobs.Control(new ToolArguments { Action = "change", Task = "stream mode" });
                return null;
            }
            if (mode == "nested" && captures == 1) {
                during = SelfJobs.Control(new ToolArguments { Action = "status" });
                nested = Stream(new ToolArguments { Action = "start", Asked = "stream mode" });
                nestedFlag = Hands.Streaming;
            }
            return frame;
        };
        return brain;
    }

    private static void Check(string name, string result, bool good)
    {
        string status = SelfJobs.Control(new ToolArguments { Action = "status" });
        bool clean = !Hands.Streaming && !SelfJobs.Active && InControl.Holds == 0 && status == "I'm not on a job right now.";
        Console.WriteLine("status-after=[{0}] holds={1}", status, InControl.Holds);
        Console.WriteLine("{0} {1} result=[{2}] captures={3} actions={4} provider={5} streaming={6} active={7} during=[{8}] changed=[{9}] nested=[{10}] nestedFlag={11} streamPrompt={12}",
            good && clean ? "PASS" : "FAIL", name, result, captures, actions, calls, Hands.Streaming, SelfJobs.Active, during, changed, nested, nestedFlag, streamPrompt);
        if (!good || !clean) failures++;
    }
    private static string Stream(ToolArguments args) => Security.Gate("stream", args) ?? Hands.Do("stream", args);
    private static void ExpectRefused(string name, string result) => Check(name, result, result == Refusal && captures == 0 && actions == 0 && calls == 0);

    private static int Main()
    {
        using (var bitmap = new SKBitmap(2, 2)) using (var image = SKImage.FromBitmap(bitmap)) using (var data = image.Encode(SKEncodedImageFormat.Png, 100)) frame = data.ToArray();
        var b = Prepare(true);
        Console.WriteLine("fixture WorksOut={0} stream-generated={1}", b.WorksOut(), Hands.StreamAsked("Stream the game that's open: play it yourself, live for their stream, and talk to their chat."));
        ExpectRefused("task-mode", Chores.Run(b, "stream mode", null));
        b = Prepare(false);
        ExpectRefused("asked-chat-no-tools", Chores.Run(b, "open a game", "talk to chat"));
        b = Prepare(true);
        ExpectRefused("stream-tool-unnamed", Stream(new ToolArguments { Action = "start", Asked = "stream mode" }));
        b = Prepare(true);
        ExpectRefused("stream-tool-named", Stream(new ToolArguments { Action = "start", Name = "chess", Asked = "wanna go live" }));
        b = Prepare(false);
        ExpectRefused("stream-tool-no-tools", Stream(new ToolArguments { Action = "start", Asked = "wanna go live" }));
        b = Prepare(false); mode = "change";
        string result = Chores.Run(b, "open a folder", null);
        Check("plain-changed-to-stream", result, result == Refusal && captures == 1 && actions == 0 && calls == 0 && during == "I'm working on open a folder." && changed == "Changed what my job should do.");
        b = Prepare(true);
        result = Chores.Run(b, "open a folder", null);
        Check("plain-job-control", result, result == "Did it. Finished fixture." && captures == 2 && actions == 1 && calls == 2);
        b = Prepare(true); mode = "nested";
        result = Chores.Run(b, "open a folder", null);
        Check("busy-stream-cannot-change-active-job", result, result == "Did it. Finished fixture." && during == "I'm working on open a folder." && nested == "I'm already on a job." && !nestedFlag && !streamPrompt && captures == 2 && actions == 1);
        b = Prepare(true);
        int native = 0, fallback = 0;
        Hands.NativeWork = args => { native++; return "native fixture"; };
        Hands.Work = (task, asked) => { fallback++; return "fallback fixture"; };
        result = Hands.Do("use_computer", new ToolArguments { Task = "open a folder" });
        Check("native-dispatch-control", result, result == "native fixture" && native == 1 && fallback == 0 && captures == 0 && actions == 0);
        foreach (string game in new[] { (string)null, "chess" })
        {
            b = Prepare(true);
            int nativeStreams = 0;
            string taskSeen = null, askedSeen = null;
            bool streamSeen = false;
            Hands.NativeWork = args => "unexpected native-work call";
            Hands.Work = (task, asked) => {
                nativeStreams++; taskSeen = task; askedSeen = asked; streamSeen = Hands.Streaming;
                Hands.Streaming = false;
                return "native stream fixture";
            };
            result = Stream(new ToolArguments { Action = "start", Name = game, Asked = "wanna go live" });
            string expected = game == null
                ? "Stream the game that's open: play it yourself, live for their stream, and talk to their chat."
                : "Stream chess: open it if it isn't open, then play it yourself, live for their stream, and talk to their chat.";
            Check(game == null ? "native-stream-unnamed" : "native-stream-named", result,
                result == "native stream fixture" && nativeStreams == 1 && streamSeen && taskSeen == expected && askedSeen == "wanna go live" && captures == 0 && actions == 0);
        }
        foreach (string job in new[] { "open the stream settings", "pause the streaming" })
        {
            b = Prepare(true);
            result = Chores.Run(b, job, null);
            Check("classifier-boundary-" + (job.IndexOf("streaming", StringComparison.Ordinal) >= 0 ? "streaming" : "stream"), result,
                result == "Did it. Finished fixture." && captures == 2 && actions == 1 && calls == 2);
        }
        b = Prepare(true);
        Hands.OnMac = true;
        int macStreams = 0;
        Hands.NativeWork = args => "unexpected native-work call";
        Hands.Work = (task, asked) => { macStreams++; Hands.Streaming = false; return "mac native stream"; };
        result = Stream(new ToolArguments { Action = "start", Asked = "wanna go live" });
        Check("mac-stream-unavailable", result, result == "Live stream chat and timing aren't connected to Mac screen jobs yet." && macStreams == 0 && captures == 0 && actions == 0);
        Hands.OnMac = false;
        b = Prepare(true);
        SelfJobs.Run(new ToolArguments { Task = "held" }, args => {
            Hands.Streaming = true;
            string busy = Chores.Run(b, "stream mode", null);
            bool ok = busy == "I'm already on a job." && Hands.Streaming;
            Console.WriteLine("{0} nested-refusal-keeps-mode result=[{1}] streaming={2}", ok ? "PASS" : "FAIL", busy, Hands.Streaming);
            if (!ok) failures++;
            Hands.Streaming = false;
            return "held";
        }, () => { });
        Console.WriteLine("failures={0}", failures);
        return failures == 0 ? 0 : 1;
    }
}
