using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed record NameListeningState(string Identity, string Source, string Fingerprint,
        bool Barred, bool Speaking, bool Busy, bool NameOnly = false);
    internal sealed record NameTranscript(string Text, int Ticket, long Authority, string Session, string Utterance);

    // Continuous opt-in local name listening. All process/pipe I/O stays off the UI thread.
    internal sealed class OwnedNameEars
    {
        private readonly object gate = new object();
        private readonly Func<string, string, Process> launch;
        private Process worker;
        private Task running;
        private CancellationTokenSource cancellation;
        private Channel<StateUpdate> updates;
        private NameListeningState state;
        private string identity, source, fingerprint, session;
        private int generation;
        private long authority;
        private bool stopping;
        private sealed record StateUpdate(NameListeningState State, long Authority);

        internal OwnedNameEars(Func<string, string, Process> launch = null)
        { this.launch = launch ?? ((python, manifest) => Bridge.Start(python, "speech/name_listen.py", "ears", manifest)); }

        internal bool Active { get { lock (gate) return running != null && !running.IsCompleted; } }
        internal bool IsCurrent(NameTranscript transcript)
        {
            lock (gate) return transcript != null && !stopping && running != null && !running.IsCompleted &&
                transcript.Ticket == generation && transcript.Authority == authority && transcript.Session == session &&
                state != null && !state.Barred && !state.Speaking;
        }

        internal Task Start(string python, string manifest, NameListeningState initial,
            Action<string> changed, Action<NameTranscript> final, Action<string> failed)
        {
            if (initial == null || string.IsNullOrWhiteSpace(initial.Identity) || string.IsNullOrWhiteSpace(initial.Source) || string.IsNullOrWhiteSpace(initial.Fingerprint))
                throw new InvalidOperationException("Select the retained local decoder and an explicit physical microphone first.");
            lock (gate)
            {
                if (running != null && !running.IsCompleted) throw new InvalidOperationException("Name listening is already active or stopping.");
                int ticket = ++generation;
                authority = 1; state = initial; identity = initial.Identity; source = initial.Source; fingerprint = initial.Fingerprint;
                session = null; stopping = false;
                cancellation?.Dispose(); cancellation = new CancellationTokenSource();
                updates = Channel.CreateBounded<StateUpdate>(new BoundedChannelOptions(1) { SingleReader = true, FullMode = BoundedChannelFullMode.DropOldest });
                Channel<StateUpdate> channel = updates;
                CancellationToken token = cancellation.Token;
                running = Task.Run(() => Run(python, manifest, ticket, channel, token, changed, final, failed));
                return running;
            }
        }

        internal void Update(NameListeningState next)
        {
            if (next == null) throw new ArgumentNullException(nameof(next));
            bool revoke;
            lock (gate)
            {
                if (running == null || running.IsCompleted || stopping) return;
                revoke = next.Identity != identity || next.Source != source || next.Fingerprint != fingerprint;
                if (!revoke)
                {
                    if (next == state) return;
                    if (next.Barred != state.Barred || next.Speaking != state.Speaking || next.NameOnly != state.NameOnly) authority++;
                    state = next;
                    updates.Writer.TryWrite(new StateUpdate(state, authority));
                }
            }
            // A new account/device/session needs a fresh, explicitly configured
            // listening generation, never the old decoder's pending transcript.
            if (revoke) Stop();
        }

        internal void GateForPlayback()
        {
            lock (gate)
            {
                if (running == null || running.IsCompleted || stopping || state == null) return;
                if (!state.Speaking) { authority++; state = state with { Speaking = true }; }
                // Local authority is revoked before playback starts; the child
                // acknowledges asynchronously, and old results cannot pass IsCurrent.
                updates.Writer.TryWrite(new StateUpdate(state, authority));
            }
        }

        internal void Stop()
        {
            lock (gate)
            {
                if (stopping || running == null || running.IsCompleted) return;
                generation++; authority++; stopping = true;
                updates.Writer.TryComplete();
                cancellation.Cancel();
            }
        }

        private static object Context(NameListeningState value) => new { barred = value.Barred, speaking = value.Speaking, busy = value.Busy, name_only = value.NameOnly };

        private async Task SendUpdates(Process child, Channel<StateUpdate> channel, CancellationToken token)
        {
            try
            {
                await foreach (StateUpdate update in channel.Reader.ReadAllAsync(token).ConfigureAwait(false))
                {
                    string line = JsonSerializer.Serialize(new { command = "state", generation = update.Authority, context = Context(update.State) });
                    await child.StandardInput.WriteLineAsync(line.AsMemory(), token).ConfigureAwait(false);
                    await child.StandardInput.FlushAsync(token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception) { Bridge.Kill(child); throw; }
        }

        private async Task Run(string python, string manifest, int ticket, Channel<StateUpdate> channel, CancellationToken token,
            Action<string> changed, Action<NameTranscript> final, Action<string> failed)
        {
            Process child = null;
            Task writer = null;
            CancellationTokenRegistration stop = default;
            var accepted = new HashSet<string>(StringComparer.Ordinal);
            var order = new Queue<string>();
            string lastStatus = null;
            try
            {
                child = launch(python, manifest);
                stop = token.Register(() => _ = Task.Run(() => Bridge.Kill(child)));
                NameListeningState initial; long admitted;
                lock (gate)
                {
                    if (ticket != generation || stopping) { Bridge.Kill(child); return; }
                    worker = child; initial = state; admitted = authority;
                }
                child.StandardInput.WriteLine(JsonSerializer.Serialize(new { source = initial.Source, fingerprint = initial.Fingerprint, manifest, generation = admitted, context = Context(initial) }));
                child.StandardInput.Flush();
                writer = SendUpdates(child, channel, token);
                while (true)
                {
                    token.ThrowIfCancellationRequested();
                    string line = Bridge.ReadLine(child.StandardOutput, 65536);
                    if (line == null) break;
                    using JsonDocument reply = JsonDocument.Parse(line);
                    JsonElement value = reply.RootElement;
                    string kind = value.GetProperty("event").GetString();
                    if (kind == "error") throw new InvalidOperationException("Local name listening stopped. Check the selected microphone and pinned decoder setup.");
                    lock (gate)
                    {
                        if (ticket != generation || stopping) return;
                        string receivedSession = value.GetProperty("session").GetString();
                        if (kind == "listening")
                        {
                            if (session != null || string.IsNullOrEmpty(receivedSession) || receivedSession.Length > 64) throw new InvalidOperationException("Unexpected name-listener session.");
                            session = receivedSession;
                            continue;
                        }
                        if (session == null || receivedSession != session) throw new InvalidOperationException("The name-listener session changed.");
                        long eventAuthority = value.GetProperty("generation").GetInt64();
                        if (eventAuthority != authority) continue;
                        if (kind == "state")
                        {
                            string mode = value.GetProperty("mode").GetString();
                            string status = mode == "name_only" ? "Listening for Capri." : mode == "chatting" ? "Chatting. No need to say Capri." : mode == "paused" ? "Name listening paused." : throw new InvalidOperationException("Unexpected name-listener state.");
                            if (status != lastStatus) { lastStatus = status; changed?.Invoke(status); }
                        }
                        else if (kind == "final")
                        {
                            if (state.Barred || state.Speaking) continue;
                            string utterance = value.GetProperty("utterance").GetString(), text = value.GetProperty("text").GetString();
                            if (string.IsNullOrEmpty(utterance) || utterance.Length > 256 || string.IsNullOrWhiteSpace(text) || text.Length > 4096) throw new InvalidOperationException("Invalid accepted name-listener transcript.");
                            if (!accepted.Add(utterance)) continue;
                            order.Enqueue(utterance); while (order.Count > 128) accepted.Remove(order.Dequeue());
                            final?.Invoke(new NameTranscript(text, ticket, authority, session, utterance));
                        }
                        else throw new InvalidOperationException("Unexpected name-listener response.");
                    }
                }
                child.WaitForExit();
                token.ThrowIfCancellationRequested();
                throw new InvalidOperationException("Local name listening ended. Turn Listen for Capri off and on to retry.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception error)
            {
                lock (gate) if (ticket == generation && !stopping) failed?.Invoke(error.Message);
            }
            finally
            {
                stop.Dispose();
                if (child != null)
                {
                    Bridge.Kill(child);
                    await child.WaitForExitAsync().ConfigureAwait(false);
                }
                channel.Writer.TryComplete();
                if (writer != null) { try { await writer.ConfigureAwait(false); } catch (Exception) { } }
                lock (gate)
                {
                    if (worker == child) worker = null;
                    if (ticket == generation) { stopping = true; session = null; }
                }
                child?.Dispose();
            }
        }
    }
}
