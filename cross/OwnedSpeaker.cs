using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class OwnedSpeaker : ISpeaker, IDisposable
    {
        private readonly object gate = new object();
        private readonly SemaphoreSlim serial = new SemaphoreSlim(1, 1);
        private Process worker;
        private int generation, sequence;
        private bool speaking;
        internal string Python, Profile, Cache;
        public string Name { get { return "Retained local voice"; } }
        public bool Ready { get { return OperatingSystem.IsLinux() && File.Exists(Python) && File.Exists(Profile); } }

        private JsonElement Exchange(object request, int expected, int ticket)
        {
            Process child;
            lock (gate) child = worker;
            if (child == null || ticket != Volatile.Read(ref generation)) throw new OperationCanceledException();
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
            using CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(child));
            child.StandardInput.WriteLine(JsonSerializer.Serialize(request));
            child.StandardInput.Flush();
            string line = Bridge.ReadLine(child.StandardOutput, 100000);
            if (ticket != Volatile.Read(ref generation)) throw new OperationCanceledException();
            if (line == null || line.Length > 100000 || timeout.IsCancellationRequested) throw new VoiceException("The retained local voice did not respond. Check its runtime and profile.");
            using JsonDocument reply = JsonDocument.Parse(line);
            JsonElement root = reply.RootElement;
            if (root.GetProperty("id").GetInt32() != expected || !root.GetProperty("ok").GetBoolean()) throw new VoiceException("The retained local voice could not synthesize this line. Check its pinned setup and whether another Capri is using it.");
            return root.Clone();
        }

        private void Open(int ticket)
        {
            if (!Ready) throw new VoiceException("Configure the retained local voice runtime and profile first.");
            bool warm = false;
            lock (gate)
            {
                if (ticket != generation) throw new OperationCanceledException();
                if (worker == null || worker.HasExited)
                {
                    worker?.Dispose();
                    worker = Bridge.Start(Python, "speech/voice_worker.py", "voice", Profile, Cache);
                    warm = true;
                }
            }
            if (warm) { int id = ++sequence; Exchange(new { id, op = "warm" }, id, ticket); }
        }

        public byte[] Speak(string text)
        {
            int ticket = Volatile.Read(ref generation);
            serial.Wait();
            string output = null;
            try
            {
                lock (gate) { if (ticket != generation) throw new OperationCanceledException(); speaking = true; }
                Open(ticket);
                Directory.CreateDirectory(Cache, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                string said = Voice.Sayable(text);
                if (said.Length > 4096) throw new VoiceException("This reply is too long for one local speech turn.");
                System.Collections.Generic.List<short> joined = new System.Collections.Generic.List<short>();
                int sampleRate = 0;
                while (said.Length > 0)
                {
                    int length = Math.Min(384, said.Length);
                    if (length < said.Length) { int boundary = said.LastIndexOf(' ', length - 1, length); if (boundary > 0) length = boundary; }
                    string chunk = said.Substring(0, length);
                    said = said.Substring(length).TrimStart();
                output = Path.Combine(Cache, Guid.NewGuid().ToString("N") + ".wav");
                int id = ++sequence;
                Exchange(new { id, op = "synthesize", text = chunk, output }, id, ticket);
                if (new FileInfo(output).Length > 32 * 1024 * 1024) throw new VoiceException("Local speech exceeded its audio limit.");
                byte[] wav = File.ReadAllBytes(output);
                if (ticket != Volatile.Read(ref generation)) throw new OperationCanceledException();
                if (wav.Length < 44 || System.Text.Encoding.ASCII.GetString(wav, 0, 4) != "RIFF" || Voice.Seconds(wav) <= 0) throw new VoiceException("The local worker returned unreadable audio.");
                int rate;
                short[] samples = Voice.Samples(wav, out rate);
                if (sampleRate != 0 && sampleRate != rate) throw new VoiceException("The local voice changed its sample rate.");
                sampleRate = rate;
                if (joined.Count + samples.Length > 120 * rate) throw new VoiceException("This speech turn exceeded two minutes.");
                joined.AddRange(samples);
                File.Delete(output); output = null;
                }
                return Voice.Wave(joined.ToArray(), sampleRate);
            }
            catch { Cancel(); throw; }
            finally { lock (gate) speaking = false; try { if (output != null) File.Delete(output); } finally { serial.Release(); } }
        }

        internal void Cancel()
        {
            lock (gate) { generation++; if (speaking && worker != null) { Bridge.Kill(worker); worker.Dispose(); worker = null; } }
        }
        public void Dispose() { lock (gate) { generation++; if (worker != null) { Bridge.Kill(worker); worker.Dispose(); worker = null; } } }
    }
}
