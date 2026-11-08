using System;
using System.Collections.Generic;
using System.Threading;

namespace CapriDesktop
{
    // Capri Remote's call (remote/PROTOCOL.md section 8.1): a paired phone talks to her push-to-talk. Each line it records
    // comes in as call.audio parts (16 kHz, 16-bit mono), her own ears turn it into words, and the words go to her as if
    // they'd been said to her at the PC, so her answer comes back to that phone like any chat reply. Her voice, as her
    // speakers play it, goes to every phone on a call in quarter-second parts. There's no mic, speaker or brain in here:
    // Capri joins it to them in app/RemoteLive.cs, and app/RemoteTests gives it stand-in ears.
    internal sealed class RemoteCall : IDisposable
    {
        internal const int Rate = 16000;
        internal const int MostSeconds = 30;
        internal const int PartMs = 250;
        private const int LinesAtOnce = 2, FlushMs = 120;

        // The phones on a call changed: the badge reads Callers(). Raised on whatever thread made the change.
        internal event Action Changed;

        // Whether a phone may call. Every paired phone may unless this says otherwise.
        internal Func<string, bool> Allowed = delegate { return true; };
        // Her voice is on at the PC, and her ears can hear: both shown on the phone when a call starts.
        internal Func<bool> VoiceOn = delegate { return true; };
        internal Func<bool> EarsOn = delegate { return true; };

        private readonly RemoteLink link;
        private readonly Func<short[], string> ears;
        private readonly Action<string, string, string> said;
        private readonly object gate = new object();
        private readonly Dictionary<string, Caller> calls = new Dictionary<string, Caller>(StringComparer.Ordinal);
        private readonly Timer flusher;
        private bool disposed;
        private long heardLines, voiceParts, voiceBytes, audioBytes;

        // ears: 16 kHz samples to her words (empty when she made out none); runs on a pool thread. said: a phone's words to
        // her, with the line's id for her answer's re.
        internal RemoteCall(RemoteLink link, Func<short[], string> ears, Action<string, string, string> said)
        {
            if (link == null) throw new ArgumentNullException("link");
            if (ears == null) throw new ArgumentNullException("ears");
            this.link = link;
            this.ears = ears;
            this.said = said;
            flusher = new Timer(Flush);
            link.Offer("call");
            link.LiveHeard += Heard;
            link.Happened += Happened;
        }

        private sealed class Caller
        {
            internal string Device;
            internal readonly Dictionary<string, Line> Lines = new Dictionary<string, Line>(StringComparer.Ordinal);
            internal readonly List<short> Voice = new List<short>();
            internal int VoiceRate, VoiceN;
            internal int VoiceSince;
        }

        private sealed class Line
        {
            internal int Next;
            internal readonly List<byte[]> Parts = new List<byte[]>();
            internal int Bytes;
        }

        internal List<RemoteWatcher> Callers()
        {
            List<RemoteWatcher> list = new List<RemoteWatcher>();
            lock (gate) foreach (Caller caller in calls.Values) list.Add(new RemoteWatcher { Device = caller.Device, Name = link.PhoneName(caller.Device) });
            return list;
        }

        internal bool OnCall(string device) { lock (gate) return device != null && calls.ContainsKey(device); }

        internal bool Any { get { lock (gate) return calls.Count > 0; } }

        // Lines heard, her voice's parts and bytes sent, and the phones' audio bytes taken in.
        internal long[] Counts() { lock (gate) return new[] { heardLines, voiceParts, voiceBytes, audioBytes }; }

        public void Dispose()
        {
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                link.LiveHeard -= Heard;
                link.Happened -= Happened;
                calls.Clear();
            }
            flusher.Dispose();
            RaiseChanged();
        }

        // ---------- A phone's messages ----------

        private void Heard(string device, RemoteJson.Map message)
        {
            switch (message.Text("type"))
            {
                case "call.start": Start(device); break;
                case "call.stop": End(device, null); break;
                case "call.audio": Audio(device, message); break;
                case "call.cancel":
                {
                    string id = message.Text("id");
                    lock (gate)
                    {
                        Caller caller;
                        if (id != null && calls.TryGetValue(device, out caller)) caller.Lines.Remove(id);
                    }
                    break;
                }
            }
        }

        private void Start(string device)
        {
            if (!Allowed(device))
            {
                link.SendLive(device, new[] { StateMessage(false, false, false, "not-allowed") }, null);
                return;
            }
            bool fresh;
            lock (gate)
            {
                if (disposed) return;
                fresh = !calls.ContainsKey(device);
                if (fresh) calls[device] = new Caller { Device = device };
            }
            bool voice = true, hearing = true;
            try { voice = VoiceOn(); }
            catch (Exception) { }
            try { hearing = EarsOn(); }
            catch (Exception) { }
            link.SendLive(device, new[] { StateMessage(true, voice, hearing, null) }, null);
            if (fresh) RaiseChanged();
        }

        // Ends a phone's call. reason goes to the phone when it still has a session, as when the PC hangs up.
        internal void End(string device, string reason)
        {
            lock (gate) if (device == null || !calls.Remove(device)) return;
            if (reason != null) link.SendLive(device, new[] { StateMessage(false, false, false, reason) }, null);
            RaiseChanged();
        }

        private void Happened(string what, string about)
        {
            if (what == "left" && about == null)
            {
                List<string> all;
                lock (gate) all = new List<string>(calls.Keys);
                foreach (string device in all) End(device, null);
            }
            else if (what == "left" || what == "kicked" || what == "removed" || what == "session") End(about, null);
        }

        // One part of a line: its id (her answer's re), n counting from 0, the rate, base64url 16-bit little-endian samples,
        // and end on the last. Parts must come in order and the line can't run past MostSeconds.
        private void Audio(string device, RemoteJson.Map message)
        {
            string id = message.Text("id");
            double n, rate;
            byte[] pcm = RemoteCrypto.FromB64u(message.Text("pcm") ?? "");
            bool end = message.Is("end", true);
            if (!RemoteMessages.IdOk(id) || !message.Number("n", out n) || n != Math.Floor(n) || n < 0 || !message.Number("rate", out rate) || rate != Rate || pcm == null || pcm.Length % 2 != 0)
            {
                link.SendLive(device, new[] { RemoteMessages.Error("bad-message", RemoteMessages.IdOk(id) ? id : null) }, null);
                return;
            }
            string problem = null;
            short[] whole = null;
            lock (gate)
            {
                Caller caller;
                if (disposed || !calls.TryGetValue(device, out caller)) problem = "not-allowed";
                else
                {
                    Line line;
                    if (!caller.Lines.TryGetValue(id, out line))
                    {
                        if (n != 0 || caller.Lines.Count >= LinesAtOnce) problem = "bad-message";
                        else caller.Lines[id] = line = new Line();
                    }
                    if (problem == null && line.Next != (int)n) problem = "bad-message";
                    else if (problem == null && line.Bytes + pcm.Length > MostSeconds * Rate * 2) problem = "too-long";
                    if (problem != null) caller.Lines.Remove(id);
                    else
                    {
                        line.Next++;
                        line.Parts.Add(pcm);
                        line.Bytes += pcm.Length;
                        audioBytes += pcm.Length;
                        if (end)
                        {
                            caller.Lines.Remove(id);
                            whole = new short[line.Bytes / 2];
                            int at = 0;
                            foreach (byte[] part in line.Parts)
                            {
                                Buffer.BlockCopy(part, 0, whole, at * 2, part.Length);
                                at += part.Length / 2;
                            }
                        }
                    }
                }
            }
            if (problem != null)
            {
                link.SendLive(device, new[] { RemoteMessages.Error(problem, id) }, null);
                return;
            }
            if (whole != null) ThreadPool.QueueUserWorkItem(delegate { Hear(device, id, whole); });
        }

        // Her ears on a finished line, then the words to her as if they'd been said at the PC. On a pool thread, where
        // anything thrown would end her.
        private void Hear(string device, string id, short[] samples)
        {
            try { HearLine(device, id, samples); }
            catch (Exception) { }
        }

        private void HearLine(string device, string id, short[] samples)
        {
            string words;
            try { words = (ears(samples) ?? "").Trim(); }
            catch (Exception) { words = null; }
            lock (gate) heardLines++;
            if (words == null)
            {
                link.SendLive(device, new[] { RemoteMessages.Error("busy", id) }, null);
                return;
            }
            link.SendLive(device, new[] { HeardMessage(id, words) }, null);
            if (words.Length > 0 && said != null && OnCall(device))
            {
                try { said(device, id, words); }
                catch (Exception) { }
            }
        }

        // ---------- Her voice out ----------

        // Her voice as her speakers play it, from any thread: to every phone on a call, at its own rate up to 24 kHz. It must
        // return quickly, so it only gathers; whole quarter seconds go at once and the rest within FlushMs.
        internal void Voice(short[] samples, int rate)
        {
            if (samples == null || samples.Length == 0 || rate < 8000) return;
            List<KeyValuePair<string, string>> out_ = null;
            lock (gate)
            {
                if (disposed || calls.Count == 0) return;
                int kept = rate;
                short[] shown = samples;
                while (kept > 24000)
                {
                    shown = Halve(shown);
                    kept /= 2;
                }
                foreach (Caller caller in calls.Values)
                {
                    if (caller.VoiceRate != kept && caller.Voice.Count > 0) Take(caller, caller.Voice.Count, ref out_);
                    caller.VoiceRate = kept;
                    if (caller.Voice.Count == 0) caller.VoiceSince = Environment.TickCount;
                    caller.Voice.AddRange(shown);
                    int part = kept * PartMs / 1000;
                    while (caller.Voice.Count >= part) Take(caller, part, ref out_);
                }
                if (calls.Count > 0) flusher.Change(FlushMs, Timeout.Infinite);
            }
            Send(out_);
        }

        // A timer runs this on a pool thread, where anything thrown would end her.
        private void Flush(object unused)
        {
            try { Flushed(); }
            catch (Exception) { }
        }

        private void Flushed()
        {
            List<KeyValuePair<string, string>> out_ = null;
            lock (gate)
            {
                if (disposed) return;
                bool waiting = false;
                foreach (Caller caller in calls.Values)
                {
                    if (caller.Voice.Count == 0) continue;
                    if (unchecked(Environment.TickCount - caller.VoiceSince) >= FlushMs) Take(caller, caller.Voice.Count, ref out_);
                    else waiting = true;
                }
                if (waiting) flusher.Change(FlushMs, Timeout.Infinite);
            }
            Send(out_);
        }

        // Under gate: the first count samples of a phone's waiting voice, as one call.voice.
        private void Take(Caller caller, int count, ref List<KeyValuePair<string, string>> out_)
        {
            byte[] bytes = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                short sample = caller.Voice[i];
                bytes[i * 2] = (byte)(sample & 0xff);
                bytes[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
            caller.Voice.RemoveRange(0, count);
            caller.VoiceSince = Environment.TickCount;
            string message = new RemoteJson.Writer().Text("type", "call.voice").Number("n", caller.VoiceN++).Number("rate", caller.VoiceRate).Text("pcm", RemoteCrypto.B64u(bytes)).ToString();
            if (out_ == null) out_ = new List<KeyValuePair<string, string>>();
            out_.Add(new KeyValuePair<string, string>(caller.Device, message));
            voiceParts++;
            voiceBytes += RemoteScreen.Envelope(message.Length);
        }

        private void Send(List<KeyValuePair<string, string>> messages)
        {
            if (messages == null) return;
            foreach (KeyValuePair<string, string> message in messages) link.SendLive(message.Key, new[] { message.Value }, null);
        }

        // She stopped talking mid-line (talked over, or stopped): phones drop what they haven't played yet.
        internal void Hush()
        {
            List<string> devices;
            lock (gate)
            {
                if (disposed || calls.Count == 0) return;
                devices = new List<string>(calls.Keys);
                foreach (Caller caller in calls.Values) caller.Voice.Clear();
            }
            foreach (string device in devices) link.SendLive(device, new[] { new RemoteJson.Writer().Text("type", "call.hush").ToString() }, null);
        }

        // Half the rate: each pair of samples averaged, which also keeps what's above the new rate from folding back in.
        internal static short[] Halve(short[] samples)
        {
            short[] half = new short[samples.Length / 2];
            for (int i = 0; i < half.Length; i++) half[i] = (short)((samples[i * 2] + samples[i * 2 + 1]) / 2);
            return half;
        }

        internal static string StateMessage(bool on, bool voice, bool hearing, string reason)
        {
            RemoteJson.Writer message = new RemoteJson.Writer().Text("type", "call.state").Flag("on", on);
            if (on) message.Flag("voice", voice).Flag("ears", hearing);
            if (reason != null) message.Text("reason", reason);
            return message.ToString();
        }

        internal static string HeardMessage(string id, string words) { return new RemoteJson.Writer().Text("type", "call.heard").Text("id", id).Text("text", RemoteMessages.Clip(words, 2000)).ToString(); }

        private void RaiseChanged()
        {
            Action changed = Changed;
            if (changed == null) return;
            try { changed(); }
            catch (Exception) { }
        }
    }
}
