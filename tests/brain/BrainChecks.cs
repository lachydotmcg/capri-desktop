using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CapriDesktop;
using SkiaSharp;

internal static class BrainChecks
{
    private static int checks;
    internal static void Check(bool result, string description) { if (!result) throw new Exception("FAIL " + description); checks++; Console.WriteLine("PASS " + description); }
    internal sealed class Pipe : IDisposable
    {
        internal NamedPipeServerStream Host;
        internal NamedPipeClientStream Client;
        internal StreamReader Reader;
        internal StreamWriter Writer;
        internal CodexRpc Rpc;
        internal static async Task<Pipe> Open(int maximumLine = 16 * 1024 * 1024)
        {
            var p = new Pipe(); string name = "capri-brain-" + Guid.NewGuid().ToString("N");
            p.Host = new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
            p.Client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
            var wait = p.Host.WaitForConnectionAsync(); await p.Client.ConnectAsync(); await wait;
            p.Reader = new StreamReader(p.Host, new UTF8Encoding(false), false, 1024, true);
            p.Writer = new StreamWriter(p.Host, new UTF8Encoding(false), 1024, true) { AutoFlush = true };
            p.Rpc = new CodexRpc(new StreamReader(p.Client, new UTF8Encoding(false), false, 1024, true), new StreamWriter(p.Client, new UTF8Encoding(false), 1024, true), maximumLine: maximumLine);
            return p;
        }
        internal async Task<JsonElement> Read() { using var d = JsonDocument.Parse(await Reader.ReadLineAsync()); return d.RootElement.Clone(); }
        internal Task Send(object x) => Writer.WriteLineAsync(JsonSerializer.Serialize(x));
        public void Dispose() { Rpc.Dispose(); Client.Dispose(); Host.Dispose(); }
    }

    private static async Task Transport()
    {
        using var p = await Pipe.Open();
        var first = p.Rpc.Request("first", new { }, CancellationToken.None);
        var second = p.Rpc.Request("second", new { }, CancellationToken.None);
        var a = await p.Read(); var b = await p.Read();
        int notices = 0; p.Rpc.Notification += (m, v) => notices++;
        await p.Send(new { method = "notice", @params = new { ready = true } });
        await p.Send(new { id = b.GetProperty("id").GetInt64(), result = new { answer = 2 } });
        string fragmented = JsonSerializer.Serialize(new { id = a.GetProperty("id").GetInt64(), result = new { answer = 1 } }) + "\n";
        foreach (char c in fragmented) { await p.Writer.WriteAsync(c); await p.Writer.FlushAsync(); }
        Check((await first).GetProperty("answer").GetInt32() == 1 && (await second).GetProperty("answer").GetInt32() == 2, "fragmented and out-of-order responses correlate by request id");
        Check(notices == 1, "interleaved notifications are consumed continuously");
        await p.Send(new { id = a.GetProperty("id").GetInt64(), result = new { answer = 99 } });
        using var cancel = new CancellationTokenSource();
        var abandoned = p.Rpc.Request("cancel", new { }, cancel.Token); var cRequest = await p.Read(); cancel.Cancel();
        try { await abandoned; Check(false, "cancelled request throws"); } catch (OperationCanceledException) { Check(true, "cancelled request throws"); }
        await p.Send(new { id = cRequest.GetProperty("id").GetInt64(), result = new { answer = 99 } });
        await p.Send(new { id = 400, method = "item/commandExecution/requestApproval", @params = new { } });
        var denial = await p.Read(); Check(denial.GetProperty("id").GetInt32() == 400 && denial.TryGetProperty("error", out _), "unhandled server requests receive explicit denial");
        var survives = p.Rpc.Request("survive", new { }, CancellationToken.None); var d = await p.Read(); await p.Send(new { id = d.GetProperty("id").GetInt64(), result = new { answer = 3 } });
        Check((await survives).GetProperty("answer").GetInt32() == 3, "duplicate and late responses cannot contaminate the next request");
        var broken = p.Rpc.Request("broken", new { }, CancellationToken.None); await p.Read(); await p.Writer.WriteLineAsync("{broken");
        try { await broken; Check(false, "malformed protocol fails pending request"); } catch (JsonException) { Check(true, "malformed protocol fails pending request"); }
        using var bounded = await Pipe.Open(1024);
        var oversized = bounded.Rpc.Request("oversized", new { }, CancellationToken.None);
        await bounded.Read(); await bounded.Writer.WriteLineAsync(new string('x', 1025));
        try { await oversized; Check(false, "oversized protocol is bounded"); }
        catch (IOException e) { Check(e.Message.Contains("oversized"), "protocol rejects oversized fragmented lines before unbounded accumulation"); }
    }

    private static async Task Live(string directory)
    {
        Directory.CreateDirectory(directory);
        using var provider = new CodexBrain(Path.Combine(directory, "codex"));
        var events = new List<string>();
        provider.EventObserved += e => { lock (events) events.Add(e); };
        await provider.Connect(null);
        Check(provider.Connected && provider.CompletedTurns == 1, "managed ChatGPT sign-in completes actual selected-model connection turn");
        Check(provider.InstructionSources.All(s => Path.GetFileName(s) == "AGENTS.md" || Path.GetFileName(s) == "AGENTS.override.md"), "dedicated companion thread loads no project instruction file");
        var brain = new Brain { Model = provider.Model, Provider = provider };
        var chunks = new List<string>();
        string hello = await Task.Run(() => brain.Ask("Say hello in one short sentence and remember the codeword turquoise.", s => chunks.Add(s)));
        Check(chunks.Count > 0 && hello.Length > 0, "real assistant text streams through shared Brain Ask");
        string thread = provider.LastThreadId;
        string follow = await Task.Run(() => brain.Ask("What codeword did I just give you?", _ => { }));
        Check(follow.Contains("turquoise", StringComparison.OrdinalIgnoreCase) && thread == provider.LastThreadId, "follow-up resumes dedicated conversation without replaying full history");
        string stoppedTurn = null; bool stopping = false;
        Action<string> stop = e => { if (e == "item/agentMessage/delta" && !stopping) { stopping = true; stoppedTurn = provider.LastTurnId; brain.Cancel(); } };
        provider.EventObserved += stop;
        bool cancelled = false;
        try { await Task.Run(() => provider.Complete(provider.Model, new List<ChatMessage> { new("system", "For this interruption test, output a long comma-separated list."), new("user", "List every integer from 1 through 1000.") }, null, new(), 2000, _ => { })); }
        catch (OperationCanceledException) { cancelled = true; }
        provider.EventObserved -= stop;
        Check(cancelled && stopping, "Stop cancels real streaming turn and suppresses its completion");
        string after = await Task.Run(() => brain.Ask("Say ready in one short sentence.", _ => { }));
        Check(after.Length > 0, "next reply succeeds after interruption");
        using var bitmap = new SKBitmap(48, 48); using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.Red);
        using var image = SKImage.FromBitmap(bitmap); using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95);
        string seen = await Task.Run(() => brain.See(jpeg.ToArray(), "Describe only the solid color in the test image.", "What color fills this image?", 20));
        Check(seen.Contains("red", StringComparison.OrdinalIgnoreCase) && provider.ImageQualified, "synthetic image inference uses selected Codex provider");
        var proposals = new List<KeyValuePair<string, ToolArguments>>();
        string plan = await Task.Run(() => provider.Complete(provider.Model, new List<ChatMessage> { new("system", "Use exactly one supplied typed proposal tool for the request. Do not claim execution."), new("user", "Propose opening the application named Capri Fixture using capri_plan_open_app.") }, Hands.Tools, proposals, 100, null));
        Check(proposals.Count == 1 && proposals[0].Key == "open_app" && Hands.Valid(proposals[0].Key, proposals[0].Value), "negotiated dynamic tool yields one valid typed action proposal without effect");
        Check(!events.Any(e => e.Contains("commandExecution") || e.Contains("fileChange") || e.Contains("mcpToolCall")), "companion inference produces no shell, file or MCP effect events");
        string model = provider.Model, identity = provider.Identity;
        provider.Dispose();
        using var resumed = new CodexBrain(Path.Combine(directory, "codex")); await resumed.Connect(model);
        string again = await Task.Run(() => resumed.Complete(model, new List<ChatMessage> { new("system", "Answer as Capri, briefly."), new("user", "What codeword did I give earlier?") }, null, new(), 80, _ => { }));
        Check(resumed.Identity == identity && resumed.LastThreadId == thread && again.Contains("turquoise", StringComparison.OrdinalIgnoreCase), "owned process restart resumes matching dedicated thread");
        File.WriteAllText(Path.Combine(directory, "live-receipt.json"), JsonSerializer.Serialize(new { model, identity, thread, hello, follow, after, imageReply = seen, proposalCount = proposals.Count, resumedReply = again, chunks = chunks.Count, events = events.ToArray(), instructionSourceCount = resumed.InstructionSources.Length, globalInstructionsExplicitlyScoped = true }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static async Task<int> Main(string[] args)
    {
        await Transport();
        await BrainProtocolFixtures.Run();
        if (args.Length == 2 && args[0] == "--live") await Live(Path.GetFullPath(args[1]));
        Console.WriteLine(checks + " checks passed."); return 0;
    }
}
