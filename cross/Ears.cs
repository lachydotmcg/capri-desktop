using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed class Mic
    {
        internal readonly string Id, Name;
        internal Mic(string id, string name) { Id = id; Name = name; }
    }

    internal static class Speaker
    {
        internal static double Loudness(short[] samples, int start, int count)
        {
            if (count <= 0) return 0;
            double sum = 0;
            for (int i = start; i < start + count; i++) sum += (double)samples[i] * samples[i];
            return Math.Sqrt(sum / count) / 32768.0;
        }
    }

    // PulseAudio's client protocol also works with PipeWire-Pulse. Capture is owned by one TALK session,
    // uses a stable source name and is never started by construction, startup, or device enumeration.
    internal sealed class Ears : IDisposable
    {
        internal const int Rate = 16000, Chunk = Rate / 10;
        internal event Action<short[]> Heard;
        internal event Action<double> Level;
        internal event Action<string> Failed;
        internal volatile bool Muted;
        internal string Chosen;
        private readonly object gate = new object();
        private PulseInput capture;
        private int generation;
        internal bool Listening { get { lock (gate) return capture != null && capture.Ready; } }
        internal static List<Mic> List()
        {
            string output = SpeechAudio.Run("pactl", new[] { "--format=json", "list", "sources" });
            using (var json = JsonDocument.Parse(output))
            {
                var result = new List<Mic>();
                foreach (var source in json.RootElement.EnumerateArray())
                {
                    string id = source.GetProperty("name").GetString();
                    // Sink monitors are not microphones and cause a speaker-to-mic loop.
                    if (id.EndsWith(".monitor", StringComparison.Ordinal)) continue;
                    result.Add(new Mic(id, source.GetProperty("description").GetString()));
                }
                return result;
            }
        }
        internal async Task Start()
        {
            if (!OperatingSystem.IsLinux()) throw new VoiceException("Microphone capture is not available on this platform yet.");
            int ticket;
            string source = Chosen;
            lock (gate)
            {
                if (capture != null) return;
                ticket = ++generation;
            }
            var sources = await Task.Run(List);
            if (string.IsNullOrEmpty(source)) source = (await Task.Run(() => SpeechAudio.Run("pactl", new[] { "get-default-source" }))).Trim();
            if (!sources.Any(m => m.Id == source)) throw new VoiceException("Your mic is unavailable. Pick another under Settings > Voice > Microphone.");
            PulseInput input;
            lock (gate)
            {
                if (ticket != generation) throw new OperationCanceledException();
                input = new PulseInput(source,
                    samples => { lock (gate) { if (ticket == generation) Feed(samples); } },
                    problem => { lock (gate) { if (ticket != generation) return; capture = null; Failed?.Invoke(problem); } });
                capture = input;
            }
            try { await input.Start(); }
            catch { lock (gate) { if (capture == input) capture = null; } input.Dispose(); throw; }
        }
        internal void Feed(short[] samples)
        {
            Level?.Invoke(Muted ? 0 : Speaker.Loudness(samples, 0, samples.Length));
            Heard?.Invoke(Muted ? new short[samples.Length] : samples);
        }
        internal void Stop()
        {
            lock (gate)
            {
                generation++;
                if (capture == null) return;
                capture.Dispose();
                capture = null;
            }
        }
        public void Dispose() { Stop(); }
    }

    // Upstream free-chat endpointing: 100 ms chunks, 400 ms pre-roll, two loud chunks to wake,
    // nine quiet chunks to finish, and a 15 s cap followed by quiet before another utterance.
    // Local speech is half duplex. Holding during synthesis/playback prevents self-transcription;
    // typed interruption and Stop remain available while the microphone is held.
    internal sealed class VoiceActivity
    {
        internal const double WakeLevel = 0.02;
        private readonly List<short[]> preroll = new List<short[]>();
        private List<short[]> utterance;
        private int loudRun, quietRun;
        private bool noiseHold;
        internal bool Capped;
        internal void Reset() { preroll.Clear(); utterance = null; loudRun = quietRun = 0; noiseHold = false; Capped = false; }
        internal short[] Feed(short[] chunk, bool held)
        {
            Capped = false;
            if (held) { Reset(); return null; }
            bool loud = Speaker.Loudness(chunk, 0, chunk.Length) >= WakeLevel;
            if (noiseHold)
            {
                quietRun = loud ? 0 : quietRun + 1;
                if (quietRun >= 9) { noiseHold = false; quietRun = 0; }
                return null;
            }
            if (utterance == null)
            {
                preroll.Add(chunk); if (preroll.Count > 4) preroll.RemoveAt(0);
                loudRun = loud ? loudRun + 1 : 0;
                if (loudRun < 2) return null;
                utterance = new List<short[]>(preroll); preroll.Clear(); loudRun = quietRun = 0; return null;
            }
            utterance.Add(chunk); quietRun = loud ? 0 : quietRun + 1;
            if (quietRun < 9 && utterance.Count < 150) return null;
            short[][] taken = utterance.ToArray(); utterance = null;
            bool capped = quietRun < 9; quietRun = 0;
            if (capped) { noiseHold = true; Capped = true; return null; }
            int voiced = 0; double low = 1, high = 0;
            foreach (short[] piece in taken)
            {
                double level = Speaker.Loudness(piece, 0, piece.Length);
                if (level < WakeLevel) continue;
                voiced += piece.Length; low = Math.Min(low, level); high = Math.Max(high, level);
            }
            if (voiced < Ears.Rate * 3 / 10 || (voiced >= Ears.Rate * 8 / 10 && high < low * 1.2)) return null;
            return taken.SelectMany(piece => piece).ToArray();
        }
    }
}
