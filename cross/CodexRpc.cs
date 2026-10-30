using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed class CodexRpc : IDisposable
    {
        private readonly StreamReader input;
        private readonly StreamWriter output;
        private readonly Process process;
        private readonly int maximumLine;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly SemaphoreSlim writer = new SemaphoreSlim(1, 1);
        private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> pending = new();
        private long nextId;
        private int ended;
        internal event Action<string, JsonElement> Notification;
        internal Func<string, JsonElement, Task<object>> ServerRequest;
        internal event Action<Exception> Disconnected;
        internal Task Reading { get; }

        internal CodexRpc(StreamReader input, StreamWriter output, Process process = null, int maximumLine = 16 * 1024 * 1024)
        {
            this.maximumLine = maximumLine;
            this.input = input; this.output = output; this.process = process;
            Reading = Read();
        }

        internal static CodexRpc Start(string cwd)
        {
            ProcessStartInfo start = new ProcessStartInfo("codex")
            {
                WorkingDirectory = cwd, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
            foreach (var pair in CodexBrain.ChildConfig())
            {
                start.ArgumentList.Add("-c"); start.ArgumentList.Add(pair.Key + "=" + JsonSerializer.Serialize(pair.Value));
            }
            Process process = Process.Start(start) ?? throw new IOException("Codex app-server did not start.");
            CodexRpc rpc = new CodexRpc(process.StandardOutput, process.StandardInput, process);
            _ = Task.Run(async () =>
            {
                try
                {
                    char[] buffer = new char[4096];
                    while (await process.StandardError.ReadAsync(buffer.AsMemory(), rpc.lifetime.Token).ConfigureAwait(false) != 0) { }
                }
                catch (OperationCanceledException) { }
                catch (IOException) { }
            });
            return rpc;
        }

        internal async Task<JsonElement> Request(string method, object parameters, CancellationToken cancellation, TimeSpan? timeout = null)
        {
            if (Volatile.Read(ref ended) != 0) throw new IOException("Codex connection is closed. Press Retry.");
            if (pending.Count >= 64) throw new IOException("Codex request queue is full.");
            long id = Interlocked.Increment(ref nextId);
            var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate Codex request id.");
            using var timed = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token);
            timed.CancelAfter(timeout ?? TimeSpan.FromSeconds(90));
            try
            {
                await Write(new { id, method, @params = parameters }, timed.Token).ConfigureAwait(false);
                return await completion.Task.WaitAsync(timed.Token).ConfigureAwait(false);
            }
            finally { pending.TryRemove(id, out _); }
        }

        internal Task Notify(string method, object parameters, CancellationToken cancellation) => Write(new { method, @params = parameters }, cancellation);

        private async Task Write(object message, CancellationToken cancellation)
        {
            await writer.WaitAsync(cancellation).ConfigureAwait(false);
            try
            {
                cancellation.ThrowIfCancellationRequested();
                await output.WriteLineAsync(JsonSerializer.Serialize(message).AsMemory(), cancellation).ConfigureAwait(false);
                await output.FlushAsync(cancellation).ConfigureAwait(false);
            }
            finally { writer.Release(); }
        }

        private async Task Read()
        {
            Exception failure = null;
            try
            {
                char[] buffer = new char[8192];
                StringBuilder line = new StringBuilder();
                while (!lifetime.IsCancellationRequested)
                {
                    int count = await input.ReadAsync(buffer.AsMemory(), lifetime.Token).ConfigureAwait(false);
                    if (count == 0) throw new IOException("Codex exited. Press Retry to reconnect; the unfinished request was not replayed.");
                    for (int i = 0; i < count; i++)
                    {
                        if (buffer[i] == '\n') { Dispatch(line.ToString()); line.Clear(); }
                        else
                        {
                            if (line.Length >= maximumLine) throw new IOException("Codex returned an oversized protocol message.");
                            line.Append(buffer[i]);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (Exception error) { failure = error; }
            finally
            {
                Interlocked.Exchange(ref ended, 1);
                foreach (var pair in pending) if (pending.TryRemove(pair.Key, out var completion)) completion.TrySetException(failure ?? new IOException("Codex disconnected."));
                if (failure != null) Disconnected?.Invoke(failure);
            }
        }

        private void Dispatch(string line)
        {
            using var document = JsonDocument.Parse(line);
            JsonElement message = document.RootElement;
            if (message.TryGetProperty("method", out var named))
            {
                string method = named.GetString();
                JsonElement parameters = message.TryGetProperty("params", out var value) ? value.Clone() : default;
                if (message.TryGetProperty("id", out var requestId)) _ = Respond(requestId.Clone(), method, parameters);
                else Notification?.Invoke(method, parameters);
            }
            else if (message.TryGetProperty("id", out var responseId) && responseId.TryGetInt64(out long id) && pending.TryRemove(id, out var completion))
            {
                if (message.TryGetProperty("error", out var error))
                    completion.TrySetException(new BrainException("Codex: " + (error.TryGetProperty("message", out var detail) ? detail.GetString() : "request failed.")));
                else if (message.TryGetProperty("result", out var result)) completion.TrySetResult(result.Clone());
                else completion.TrySetException(new IOException("Codex response has no result."));
            }
        }

        private async Task Respond(JsonElement id, string method, JsonElement parameters)
        {
            try
            {
                if (ServerRequest == null) throw new InvalidOperationException("This Codex request is not supported by Capri.");
                object result = await ServerRequest(method, parameters).ConfigureAwait(false);
                await Write(new { id, result }, lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception error)
            {
                try { await Write(new { id, error = new { code = -32601, message = error is OperationCanceledException ? "Capri cancelled this request." : "Capri does not authorize this server request: " + method } }, lifetime.Token).ConfigureAwait(false); }
                catch (Exception) when (lifetime.IsCancellationRequested || Volatile.Read(ref ended) != 0) { }
            }
        }

        public void Dispose()
        {
            if (!lifetime.IsCancellationRequested) lifetime.Cancel();
            if (process != null)
            {
                try { if (!process.HasExited) process.Kill(true); }
                catch (InvalidOperationException) { }
            }
        }
    }
}
