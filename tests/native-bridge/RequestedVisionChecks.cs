using System;
using System.Collections.Generic;
using System.Net;
using CapriDesktop;
using SkiaSharp;

internal static class RequestedVisionChecks
{
    private sealed class Provider : IBrainProvider
    {
        internal Action During;
        internal string Key = "public-vision-fixture";
        internal int Revision, Calls;
        internal string Instruction, System, Purpose;
        public string Identity => Key;
        public int Generation => Revision;
        public void Cancel() { Revision++; }
        public string Complete(string model, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls,
            int limit, Action<string> streamed, string purpose, string parts = null)
        {
            Calls++; Purpose = purpose;
            System = messages[0].Content;
            Instruction = messages[messages.Count - 1].Content;
            During?.Invoke();
            return "PUBLIC-VISION-FIXTURE-ANSWER";
        }
    }

    internal static void Run()
    {
        int checks = 0;
        void Check(bool value, string name) { if (!value) throw new Exception(name); checks++; Console.WriteLine("PASS " + name); }
        byte[] picture;
        using (var bitmap = new SKBitmap(16, 16))
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            using var image = SKImage.FromBitmap(bitmap);
            using var data = image.Encode(SKEncodedImageFormat.Png, 100);
            picture = data.ToArray();
        }
        ComputerOutput output = new("PUBLIC-OUTPUT", new(0, 0, 1920, 1080), 1, 0, 1920, 1080);
        ComputerTarget target = new("0x123", 123, "fixture-birth", "public-fixture", new(10, 10, 800, 800), 1, 1);
        ComputerSnapshot bound = new("public-session", "public-layout", target, output, 30, 30, true, 7, 10);

        void Case(string name, Action<Provider, Brain, ToolArguments, Action<ComputerSnapshot>> change, bool reject, bool crop = false)
        {
            Brain.Unsee();
            var provider = new Provider();
            var brain = new Brain { Model = "public-fixture", Provider = provider };
            var args = new ToolArguments { Asked = "Solve the puzzle on my second monitor", Focus = "identify the board and legal solution", Current = () => true };
            ComputerSnapshot current = bound;
            int captured = 0, validated = 0;
            string captureIntent = null, captureScreen = null;
            provider.During = () => change(provider, brain, args, next => current = next);
            bool stopped = false;
            string answer = null;
            try
            {
                answer = RequestedVision.Run(brain, args,
                    (intent, screen) => { captured++; captureIntent = intent; captureScreen = screen; return new EyesObservation(picture, new ComputerFrame { Png = picture, Width = 16, Height = 16, Source = bound, Area = bound.Output.Area }, Eyes.Ticket(), crop); },
                    observation => { validated++; ArchComputer.SameObservation(observation.Frame.Source, current, crop); });
            }
            catch (OperationCanceledException) { stopped = true; }
            catch (WebException error) when (error.Status == WebExceptionStatus.RequestCanceled) { stopped = true; }
            catch (ComputerObservationChangedException) { stopped = true; }
            Check(stopped == reject, name + " has the expected observation outcome");
            Check(provider.Calls == 1 && captured == 1, name + " does not recapture or replay inference");
            Check(captureIntent == args.Asked && captureScreen == null, name + " carries owner monitor intent despite narrower model focus");
            Check(reject ? !Brain.Seen().Contains("PUBLIC-VISION-FIXTURE-ANSWER", StringComparison.Ordinal) : Brain.Seen().Contains("PUBLIC-VISION-FIXTURE-ANSWER", StringComparison.Ordinal), name + " only retains a current accepted sighting");
            if (!reject)
            {
                Check(validated == 2, name + " validates before and after inference");
                Check(answer.Contains("Read-only visual evidence", StringComparison.Ordinal), name + " remains advice without input");
                Check(provider.Purpose == "vision" && provider.Instruction.Contains(args.Asked, StringComparison.Ordinal) && provider.Instruction.Contains(args.Focus, StringComparison.Ordinal), name + " carries the original question and focus to vision");
                Check(provider.System.Contains("side to move", StringComparison.Ordinal) && provider.System.Contains("unreadable", StringComparison.Ordinal), name + " requests chess evidence and uncertainty");
            }
        }

        try
        {
            Case("unchanged public board", (_, _, _, _) => { }, false);
            Case("unrelated focus and app change for full-output advice", (_, _, _, set) => set(bound with { FocusEpoch = 99, Active = false, Target = target with { Address = "0x456", Pid = 456, Birth = "other-birth", App = "unrelated-fixture" } }), false);
            Case("cropped target lost focus without geometry change", (_, _, _, set) => set(bound with { FocusEpoch = 99, Active = false }), false, true);
            Case("session replacement during inference", (_, _, _, set) => set(bound with { Session = "replacement" }), true);
            Case("layout replacement during inference", (_, _, _, set) => set(bound with { Layout = "replacement" }), true);
            Case("output removal/replacement during inference", (_, _, _, set) => set(bound with { Output = output with { Name = "replacement" } }), true);
            Case("lock/disconnect revocation epoch during inference", (_, _, _, set) => set(bound with { ObservationEpoch = bound.ObservationEpoch + 1 }), true);
            Case("cropped target PID/birth changed during inference", (_, _, _, set) => set(bound with { Target = target with { Birth = "replacement" } }), true, true);
            Case("provider replacement during inference", (_, brain, _, _) => brain.Provider = new Provider(), true);
            Case("account identity change during inference", (provider, _, _, _) => provider.Key = "replacement", true);
            Case("provider cancellation generation during inference", (provider, _, _, _) => provider.Cancel(), true);
            Case("model replacement during inference", (_, brain, _, _) => brain.Model = "replacement", true);
            Case("owner request cancellation during inference", (_, _, args, _) => args.Current = () => false, true);
            Case("Stop revocation during inference", (_, _, _, _) => Eyes.Stop(), true);

            Brain.Unsee();
            var provider = new Provider();
            var brain = new Brain { Model = "public-fixture", Provider = provider };
            int captures = 0;
            try
            {
                RequestedVision.Run(brain, new ToolArguments { Asked = "read public fixture", Current = () => false },
                    (_, _) => { captures++; return new EyesObservation(picture, null, Eyes.Ticket()); }, _ => { });
                Check(false, "cancelled request rejected before capture");
            }
            catch (OperationCanceledException) { Check(captures == 0 && provider.Calls == 0 && !Brain.Seen().Contains("PUBLIC-VISION-FIXTURE-ANSWER", StringComparison.Ordinal), "cancelled request performs no capture, inference or sighting"); }
        }
        finally { Brain.Unsee(); }
        Console.WriteLine("PASS requested vision service total=" + checks);
    }
}
