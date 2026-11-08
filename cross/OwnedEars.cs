using System;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed class OwnedEars
    {
        private readonly object gate = new object();
        private Process worker;
        private int generation;
        private bool starting;
        internal bool Active { get { lock (gate) return starting || worker != null; } }

        internal Task Listen(string python, string manifest, string source, string fingerprint, Action listening, Action<string> final, Action<string> failed)
        {
            int ticket;
            lock (gate) { if (starting || worker != null) throw new InvalidOperationException("Listening is already active."); starting = true; ticket = ++generation; }
            return Task.Run(() =>
            {
                Process child = null;
                try
                {
                    child = Bridge.Start(python, "speech/listen.py", "ears", manifest);
                    lock (gate) { if (generation != ticket) { Bridge.Kill(child); return; } worker = child; starting = false; }
                    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                    using CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(child));
                    child.StandardInput.WriteLine(JsonSerializer.Serialize(new { source, fingerprint, manifest }));
                    child.StandardInput.Close();
                    string session = null, accepted = null;
                    while (true)
                    {
                        string line = Bridge.ReadLine(child.StandardOutput, 65536);
                        if (line == null) break;
                        if (line.Length > 65536) throw new InvalidOperationException("Local recognition exceeded its message limit.");
                        using JsonDocument reply = JsonDocument.Parse(line);
                        JsonElement value = reply.RootElement;
                        if (value.GetProperty("event").GetString() == "error") throw new InvalidOperationException(value.GetProperty("message").GetString());
                        lock (gate)
                        {
                            if (ticket != generation) return;
                            if (value.GetProperty("event").GetString() == "listening") { session = value.GetProperty("session").GetString(); listening(); }
                            else if (value.GetProperty("event").GetString() == "final" && session != null && value.GetProperty("session").GetString() == session && accepted == null)
                                accepted = value.GetProperty("text").GetString();
                            else throw new InvalidOperationException("Unexpected recognition response.");
                        }
                    }
                    child.WaitForExit();
                    lock (gate)
                    {
                        if (ticket != generation) return;
                        if (child.ExitCode != 0 || string.IsNullOrWhiteSpace(accepted) || timeout.IsCancellationRequested) throw new InvalidOperationException("No accepted final transcript was received.");
                        final(accepted);
                    }
                }
                catch (Exception error) { lock (gate) if (ticket == generation) failed(error.Message); }
                finally
                {
                    lock (gate) { if (worker == child) worker = null; if (ticket == generation) starting = false; }
                    if (child != null) { Bridge.Kill(child); child.Dispose(); }
                }
            });
        }

        internal void Stop()
        {
            lock (gate) { generation++; starting = false; if (worker != null) { Bridge.Kill(worker); worker = null; } }
        }
    }
}
