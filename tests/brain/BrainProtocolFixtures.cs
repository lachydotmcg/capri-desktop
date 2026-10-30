using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CapriDesktop;

internal static class BrainProtocolFixtures
{
    internal sealed class Server : IDisposable
    {
        internal BrainChecks.Pipe Pipe;
        internal string Mode = "complete", Account = "fixture-a";
        internal TaskCompletionSource<bool> Held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource<bool> Proposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Interruptions;
        internal bool SourceViolation, UnsupportedExperimental, ConfigurationSafe, ProposalRejected;
        internal string Thread, Turn;
        private int sequence;
        private readonly SemaphoreSlim writer = new(1, 1);
        internal static async Task<Server> Open()
        {
            var s = new Server { Pipe = await BrainChecks.Pipe.Open() };
            _ = s.Run(); return s;
        }
        internal async Task Send(object value) { await writer.WaitAsync(); try { await Pipe.Send(value); } finally { writer.Release(); } }
        private async Task Run()
        {
            try
            {
                while (true)
                {
                    var request = await Pipe.Read();
                    if (!request.TryGetProperty("method", out var method)) { if (request.TryGetProperty("id", out var rid) && rid.GetInt32() == 800) { ProposalRejected = request.TryGetProperty("error", out _); Proposed.TrySetResult(true); } continue; }
                    string name = method.GetString();
                    if (!request.TryGetProperty("id", out var id)) continue;
                    var p = request.GetProperty("params");
                    object result = new { };
                    switch (name)
                    {
                        case "initialize":
                            if (UnsupportedExperimental) { await Send(new { id, error = new { code = -32600, message = "experimentalApi is unsupported" } }); continue; }
                            result = new { codexHome = "/fixture/codex" }; break;
                        case "account/read": result = new { account = new { type = "chatgpt", email = Account, planType = "fixture" }, requiresOpenaiAuth = true }; break;
                        case "config/read": result = new { config = new { mcp_servers = new { unrelated = new { enabled = true } } } }; break;
                        case "model/list": result = new { data = new[] { new { model = "fixture-model", supportedReasoningEfforts = new[] { new { reasoningEffort = "low" } }, inputModalities = new[] { "text", "image" } } } }; break;
                        case "thread/start":
                        case "thread/resume":
                            Thread = name == "thread/start" ? "thread-" + ++sequence : p.GetProperty("threadId").GetString();
                            var c = p.GetProperty("config");
                            ConfigurationSafe = p.GetProperty("sandbox").GetString() == "read-only" && p.GetProperty("approvalPolicy").GetString() == "never" &&
                                !c.GetProperty("features.shell_tool").GetBoolean() && !c.GetProperty("mcp_servers").GetProperty("unrelated").GetProperty("enabled").GetBoolean() &&
                                p.GetProperty("developerInstructions").GetString().Contains(CodexBrain.Boundary);
                            result = new { thread = new { id = Thread }, model = "fixture-model", instructionSources = SourceViolation ? new[] { "/project/AGENTS.md" } : new[] { "/fixture/codex/AGENTS.md" } }; break;
                        case "turn/start":
                            string previous = Turn;
                            Turn = "turn-" + ++sequence;
                            if (Mode == "late-before-response")
                            {
                                await Send(new { method = "turn/completed", @params = new { threadId = Thread, turn = new { id = previous, status = "completed" } } });
                                await Send(new { method = "item/agentMessage/delta", @params = new { threadId = Thread, turnId = previous, delta = "stale" } });
                            }
                            if (Mode == "early-current") await Finish("completed");
                            if (Mode == "early-proposal" || Mode == "early-stale-proposal")
                                await Send(new { id = 800, method = "item/tool/call", @params = new { threadId = Thread, turnId = Mode == "early-proposal" ? Turn : previous, callId = "call-early", tool = "capri_plan_open_app", arguments = new { name = "Fixture" } } });
                            if (Mode == "early-overflow")
                                for (int n = 0; n < 257; n++) await Send(new { method = "item/agentMessage/delta", @params = new { threadId = Thread, turnId = Turn, delta = "x" } });
                            await Send(new { id, result = new { turn = new { id = Turn } } });
                            await Send(new { method = "turn/started", @params = new { threadId = Thread, turn = new { id = Turn } } });
                            if (Mode.StartsWith("early-") || Mode == "late-before-response") Held.TrySetResult(true);
                            else if (Mode == "proposal")
                            {
                                await Send(new { id = 800, method = "item/tool/call", @params = new { threadId = Thread, turnId = Turn, callId = "call-1", tool = "capri_plan_open_app", arguments = new { name = "Fixture" } } });
                                Held.TrySetResult(true);
                            }
                            else if (Mode == "hold") Held.TrySetResult(true);
                            else await Finish(Mode == "failed" ? "failed" : Mode == "interrupted" ? "interrupted" : "completed");
                            continue;
                        case "turn/interrupt":
                            Interruptions++;
                            await Finish("interrupted");
                            result = new { };
                            break;
                    }
                    await Send(new { id, result });
                }
            }
            catch (Exception) { }
        }
        internal async Task Finish(string status)
        {
            await Send(new { method = "item/agentMessage/delta", @params = new { threadId = Thread, turnId = Turn, itemId = "message-" + Turn, delta = "fixture reply" } });
            await Send(new { method = "turn/completed", @params = new { threadId = Thread, turn = new { id = Turn, status, error = status == "failed" ? new { message = "model unavailable" } : null } } });
        }
        public void Dispose() { Pipe.Dispose(); }
    }

    internal static async Task Run()
    {
        string root = Path.Combine(Path.GetTempPath(), "capri-brain-fixtures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using (var server = await Server.Open())
            using (var provider = new CodexBrain(Path.Combine(root, "success"), _ => server.Pipe.Rpc))
            {
                server.Mode = "hold";
                var connecting = provider.Connect(null); await server.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
                BrainChecks.Check(!provider.Connected, "auth and model catalog alone do not mark brain connected");
                await server.Finish("completed"); await connecting;
                BrainChecks.Check(provider.Connected && server.ConfigurationSafe, "successful probe connects with shell and unrelated MCP disabled");
                var messages = new List<ChatMessage> { new("system", "fixture"), new("user", "fixture input") };
                server.Mode = "failed"; var calls = new List<KeyValuePair<string, ToolArguments>>();
                try { await Task.Run(() => provider.Complete(provider.Model, messages, null, calls, 40, null)); BrainChecks.Check(false, "failed turn surfaces error"); }
                catch (BrainException e) { BrainChecks.Check(e.Message.Contains("unavailable") && !provider.Connected, "failed turn surfaces error and removes connected status"); }
                server.Mode = "interrupted";
                try { await Task.Run(() => provider.Complete(provider.Model, messages, null, calls, 40, null)); BrainChecks.Check(false, "interrupted turn never succeeds"); }
                catch (OperationCanceledException) { BrainChecks.Check(true, "terminal interrupted state never becomes success"); }
                server.Held = new(TaskCreationOptions.RunContinuationsAsynchronously); server.Mode = "proposal";
                var running = Task.Run(() => provider.Complete(provider.Model, messages, Hands.Tools, calls, 40, null));
                await server.Held.Task.WaitAsync(TimeSpan.FromSeconds(5)); await server.Proposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                int before = provider.Generation; provider.Cancel();
                try { await running; BrainChecks.Check(false, "Stop discards proposed effects"); } catch (OperationCanceledException) { BrainChecks.Check(calls.Count == 0 && provider.Generation > before, "Stop discards proposed effects and advances revocation generation"); }
                await server.Finish("completed");
                server.Mode = "complete";
                string next = await Task.Run(() => provider.Complete(provider.Model, messages, null, calls, 40, null));
                BrainChecks.Check(next == "fixture reply" && calls.Count == 0 && server.Interruptions > 0, "late cancelled completion cannot contaminate next turn");
                string identity = provider.Identity; int revoked = 0; provider.Revoked += () => revoked++;
                await server.Send(new { method = "account/updated", @params = new { authMode = "chatgpt" } });
                // A request barrier orders the account notification before this assertion.
                await server.Pipe.Rpc.Request("barrier", new { }, CancellationToken.None);
                BrainChecks.Check(!provider.Connected && revoked == 1, "account notification revokes pending work and connected state");
                server.Account = "fixture-b"; await provider.Connect(provider.Model);
                BrainChecks.Check(identity != provider.Identity, "account fingerprint changes without storing account credentials");
                server.SourceViolation = true;
                try { await Task.Run(() => provider.Complete(provider.Model, messages, null, calls, 40, null)); BrainChecks.Check(false, "project instructions rejected"); }
                catch (BrainException e) { BrainChecks.Check(e.Message.Contains("project instructions"), "unexpected project instructions stop companion request"); }
            }
            foreach (string mode in new[] { "late-before-response", "early-current", "early-proposal", "early-stale-proposal", "early-overflow" })
            using (var server = await Server.Open())
            using (var provider = new CodexBrain(Path.Combine(root, mode), _ => server.Pipe.Rpc))
            {
                await provider.Connect(null);
                // Establish a prior turn in the same chat lane, as in the independent regression.
                var messages = new List<ChatMessage> { new("system", "fixture"), new("user", "first") };
                await Task.Run(() => provider.Complete(provider.Model, messages, null, new(), 40, null));
                server.Mode = mode;
                var calls = new List<KeyValuePair<string, ToolArguments>>(); var chunks = new List<string>();
                var running = Task.Run(() => provider.Complete(provider.Model, messages, mode.Contains("proposal") ? Hands.Tools : null, calls, 40, chunks.Add));
                await server.Held.Task.WaitAsync(TimeSpan.FromSeconds(5));
                if (mode.Contains("proposal")) await server.Proposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                // A request barrier proves all earlier wire messages have been consumed without time-based assertions.
                if (mode != "early-overflow") await server.Pipe.Rpc.Request("barrier", new { }, CancellationToken.None);
                if (mode == "late-before-response") BrainChecks.Check(!running.IsCompleted, "late previous completion cannot finish a new turn awaiting its authoritative completion");
                if (mode != "early-current" && mode != "early-overflow") await server.Finish("completed");
                try
                {
                    string result = await running.WaitAsync(TimeSpan.FromSeconds(5));
                    BrainChecks.Check(mode != "early-overflow" && result == "fixture reply" && string.Concat(chunks) == "fixture reply", mode + " preserves only current response-authoritative streamed text");
                    if (mode.Contains("proposal")) BrainChecks.Check(calls.Count == (mode == "early-proposal" ? 1 : 0) && server.ProposalRejected == (mode == "early-stale-proposal"), mode + " validates server request only after response authority without blocking reader");
                }
                catch (IOException e) when (mode == "early-overflow") { BrainChecks.Check(e.Message.Contains("pre-response") && !provider.Connected && calls.Count == 0, "pre-response notification overflow fails closed with no proposals"); }
            }
            using (var server = await Server.Open())
            using (var provider = new CodexBrain(Path.Combine(root, "disconnect"), _ => server.Pipe.Rpc))
            {
                await provider.Connect(null);
                server.Mode = "proposal";
                var calls = new List<KeyValuePair<string, ToolArguments>>();
                var running = Task.Run(() => provider.Complete(provider.Model, new List<ChatMessage> { new("system", "fixture"), new("user", "fixture") }, Hands.Tools, calls, 40, null));
                await server.Held.Task.WaitAsync(TimeSpan.FromSeconds(5)); await server.Proposed.Task.WaitAsync(TimeSpan.FromSeconds(5));
                server.Pipe.Host.Dispose();
                try { await running; BrainChecks.Check(false, "disconnect drops proposals"); }
                catch (IOException) { BrainChecks.Check(calls.Count == 0 && !provider.Connected, "process disconnect during tool planning drops all uncommitted proposals"); }
            }
            using (var server = await Server.Open())
            using (var provider = new CodexBrain(Path.Combine(root, "unsupported"), _ => server.Pipe.Rpc))
            {
                server.UnsupportedExperimental = true;
                try { await provider.Connect(null); BrainChecks.Check(false, "unsupported experimental API rejected"); }
                catch (BrainException e) { BrainChecks.Check(e.Message.Contains("unsupported") && !provider.Connected, "unsupported experimental API remains disconnected without unsafe fallback"); }
            }
            using (var server = await Server.Open())
            using (var provider = new CodexBrain(Path.Combine(root, "cancel-connect"), _ => server.Pipe.Rpc))
            {
                server.Mode = "hold"; var connecting = provider.Connect(null); await server.Held.Task.WaitAsync(TimeSpan.FromSeconds(5)); provider.Cancel();
                try { await connecting; BrainChecks.Check(false, "Stop during connection cancels probe"); }
                catch (OperationCanceledException) { BrainChecks.Check(!provider.Connected, "Stop during connection cannot produce false connected status"); }
            }
        }
        finally { Directory.Delete(root, true); }
    }
}
