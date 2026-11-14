using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // Capri Remote's call (remote/PROTOCOL.md section 8.2): a paired phone talks to her hands-free. Each line it sends
    // comes in as call.audio parts (16 kHz, 16-bit mono), her own ears turn it into words, and the words go to her as if
    // they'd been said to her at the PC, so her answer comes back to that phone like any chat reply. Her voice, as her
    // speakers play it, goes to every phone on a call in quarter-second parts, and Interrupt on a phone (call.hush) asks
    // for her answer to its line to stop. There's no mic, speaker or brain in here: Capri joins it to them in
    // app/RemoteLive.cs, and app/RemoteTests gives it stand-in ears.
    internal sealed class RemoteCall : IDisposable
    {
        internal const int Rate = 16000;
        internal const int MostSeconds = 30;
        internal const int PartMs = 250;
        // hello-ok's feature for a PC that stops her answer when a phone on a call sends call.hush.
        internal const string HushFeature = "hush";
        private const int LinesAtOnce = 2, FlushMs = 120, TimedForMs = 60000;

        // The phones on a call changed: the badge reads Callers(). Raised on whatever thread made the change.
        internal event Action Changed;

        // Whether a phone may call. Every paired phone may unless this says otherwise.
        internal Func<string, bool> Allowed = delegate { return true; };
        // Her voice is on at the PC, and her ears can hear: both shown on the phone when a call starts.
        internal Func<bool> VoiceOn = delegate { return true; };
        internal Func<bool> EarsOn = delegate { return true; };
        // A phone on a call pressed Interrupt: its device and the message (re) whose answer should stop. Raised on the
        // link's read worker, so it must return quickly. With none set, only that phone's waiting voice is dropped.
        internal Action<string, string> Interrupted;
        // How soon her first words went to a phone after its line ended, in ms, with how long her ears took: for her log.
        // Raised on a pool thread.
        internal Action<long, string> Timed;
        // Every message to a phone as it goes, for her smoke test. Raised under the call's lock: it must only look.
        internal Action<string, string> Sending;

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
            link.Offer(HushFeature);
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
            // Timing her first words: when the last line ended (Stopwatch ticks) and how long her ears took on it. Asked
            // means its words went to her; Timing starts once her voice stops for them, so the next part sent is her answer.
            internal long EndedAt;
            internal long EarsMs;
            internal bool Asked, Timing;
            // Its call.start said hush: it stops her with call.hush, so her answers to it can go a sentence at a time.
            internal bool Hushes;
            internal bool Listen;
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

        // On a call from a phone that stops her with call.hush (older phones only drop what's still to play by themselves,
        // so they get her answers whole).
        internal bool Hushes(string device)
        {
            lock (gate)
            {
                Caller caller;
                return device != null && calls.TryGetValue(device, out caller) && caller.Hushes;
            }
        }

        internal bool Any { get { lock (gate) return calls.Count > 0; } }

        internal bool Hearing
        {
            get { lock (gate) { foreach (Caller caller in calls.Values) if (!caller.Listen) return true; return false; } }
        }

        internal bool Listening(string device)
        {
            lock (gate) { Caller caller; return device != null && calls.TryGetValue(device, out caller) && caller.Listen; }
        }

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

        // A phone's message as the link would hand it over, for her smoke test.
        internal void Heard(string device, string json)
        {
            RemoteJson.Map message = RemoteJson.ParseMap(json);
            if (message != null) Heard(device, message);
        }

        private void Heard(string device, RemoteJson.Map message)
        {
            switch (message.Text("type"))
            {
                case "call.start": Start(device, message.Is("hush", true), message.Is("listen", true)); break;
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
                case "call.hush": Interrupt(device, message.Text("re")); break;
            }
        }

        // Interrupt on a phone: re is the line (or chat.send) whose answer it heard. A phone that isn't on a call is ignored.
        private void Interrupt(string device, string re)
        {
            if (!RemoteMessages.IdOk(re))
            {
                Send(device, RemoteMessages.Error("bad-message", null));
                return;
            }
            if (!OnCall(device)) return;
            bool listen = Listening(device);
            Action<string, string> interrupted = Interrupted;
            if (interrupted == null)
            {
                Hush(device);
                if (listen) End(device, "ended");
                return;
            }
            try { interrupted(device, re); }
            catch (Exception) { }
            if (listen) End(device, "ended");
        }

        private void Start(string device, bool hushes, bool listen)
        {
            if (!Allowed(device))
            {
                End(device, null);
                Send(device, StateMessage(false, false, false, "not-allowed"));
                return;
            }
            bool fresh = false, busy = false, removed = false;
            lock (gate)
            {
                if (disposed) return;
                if (listen)
                    foreach (Caller caller in calls.Values)
                        if (caller.Device != device && !caller.Listen) busy = true;
                if (busy)
                {
                    removed = calls.Remove(device);
                    Send(device, StateMessage(false, false, false, "busy"));
                }
                else
                {
                    if (!listen)
                        foreach (string other in new List<string>(calls.Keys))
                            if (other != device && calls[other].Listen)
                            {
                                calls.Remove(other);
                                removed = true;
                                Send(other, StateMessage(false, false, false, "busy"));
                            }
                    fresh = !calls.ContainsKey(device);
                    bool changed = !fresh && calls[device].Listen != listen;
                    if (fresh || changed) calls[device] = new Caller { Device = device, Listen = listen };
                    fresh = fresh || changed;
                    calls[device].Hushes = hushes;
                }
            }
            if (busy) { if (removed) RaiseChanged(); return; }
            bool voice = true, hearing = true;
            try { voice = VoiceOn(); }
            catch (Exception) { }
            try { hearing = !listen && EarsOn(); }
            catch (Exception) { }
            Send(device, StateMessage(true, voice, hearing, null));
            if (fresh || removed) RaiseChanged();
        }

        // Ends a phone's call. reason goes to the phone when it still has a session, as when the PC hangs up.
        internal void End(string device, string reason)
        {
            lock (gate) if (device == null || !calls.Remove(device)) return;
            if (reason != null) Send(device, StateMessage(false, false, false, reason));
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
            if (Listening(device))
            {
                Send(device, RemoteMessages.Error("not-allowed", RemoteMessages.IdOk(id) ? id : null));
                return;
            }
            double n, rate;
            byte[] pcm = RemoteCrypto.FromB64u(message.Text("pcm") ?? "");
            bool end = message.Is("end", true);
            if (!RemoteMessages.IdOk(id) || !message.Number("n", out n) || n != Math.Floor(n) || n < 0 || !message.Number("rate", out rate) || rate != Rate || pcm == null || pcm.Length % 2 != 0)
            {
                Send(device, RemoteMessages.Error("bad-message", RemoteMessages.IdOk(id) ? id : null));
                return;
            }
            string problem = null;
            short[] whole = null;
            Caller accepted = null;
            long ended = 0;
            lock (gate)
            {
                Caller caller;
                if (disposed || !calls.TryGetValue(device, out caller) || caller.Listen) problem = "not-allowed";
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
                            accepted = caller;
                            caller.Lines.Remove(id);
                            ended = Stopwatch.GetTimestamp();
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
                Send(device, RemoteMessages.Error(problem, id));
                return;
            }
            if (whole != null) ThreadPool.QueueUserWorkItem(delegate { Hear(device, id, whole, ended, accepted); });
        }

        // Her ears on a finished line, then the words to her as if they'd been said at the PC. On a pool thread, where
        // anything thrown would end her.
        private void Hear(string device, string id, short[] samples, long ended, Caller accepted)
        {
            try { HearLine(device, id, samples, ended, accepted); }
            catch (Exception) { }
        }

        private void HearLine(string device, string id, short[] samples, long ended, Caller accepted)
        {
            lock (gate) { Caller caller; if (disposed || !calls.TryGetValue(device, out caller) || caller != accepted || caller.Listen) return; }
            string words;
            try { words = (ears(samples) ?? "").Trim(); }
            catch (Exception) { words = null; }
            long earsMs = Since(ended);
            lock (gate)
            {
                Caller caller;
                if (disposed || !calls.TryGetValue(device, out caller) || caller != accepted || caller.Listen) return;
                heardLines++;
            }
            if (words == null)
            {
                Send(device, RemoteMessages.Error("busy", id));
                return;
            }
            Send(device, HeardMessage(id, words));
            if (words.Length > 0 && said != null && OnCall(device))
            {
                lock (gate)
                {
                    Caller caller;
                    if (calls.TryGetValue(device, out caller))
                    {
                        caller.EndedAt = ended;
                        caller.EarsMs = earsMs;
                        caller.Asked = true;
                        caller.Timing = false;
                    }
                }
                try { said(device, id, words); }
                catch (Exception) { }
            }
        }

        private static long Since(long ticks) { return (Stopwatch.GetTimestamp() - ticks) * 1000 / Stopwatch.Frequency; }

        // ---------- Her voice out ----------

        // Her voice as her speakers play it, from any thread: to every phone on a call, at its own rate up to 24 kHz. It must
        // return quickly, so it only gathers and queues; whole quarter seconds go at once and the rest within FlushMs.
        internal void Voice(short[] samples, int rate)
        {
            if (samples == null || samples.Length == 0 || rate < 8000) return;
            List<KeyValuePair<long, long>> timed = null;
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
                    if (caller.VoiceRate != kept && caller.Voice.Count > 0) Take(caller, caller.Voice.Count);
                    caller.VoiceRate = kept;
                    if (caller.Voice.Count == 0) caller.VoiceSince = Environment.TickCount;
                    caller.Voice.AddRange(shown);
                    int part = kept * PartMs / 1000;
                    while (caller.Voice.Count >= part) Take(caller, part);
                    if (caller.Timing)
                    {
                        caller.Timing = false;
                        long first = Since(caller.EndedAt);
                        if (first < TimedForMs)
                        {
                            if (timed == null) timed = new List<KeyValuePair<long, long>>();
                            timed.Add(new KeyValuePair<long, long>(first, caller.EarsMs));
                        }
                    }
                }
                if (calls.Count > 0) flusher.Change(FlushMs, Timeout.Infinite);
            }
            Action<long, string> told = Timed;
            if (timed != null && told != null)
                foreach (KeyValuePair<long, long> one in timed)
                {
                    KeyValuePair<long, long> each = one;
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try { told(each.Key, "her first words went to the phone " + each.Key + " ms after its line ended: her ears took " + each.Value + " ms, her brain and voice the rest"); }
                        catch (Exception) { }
                    });
                }
        }

        // A timer runs this on a pool thread, where anything thrown would end her.
        private void Flush(object unused)
        {
            try { Flushed(); }
            catch (Exception) { }
        }

        private void Flushed()
        {
            lock (gate)
            {
                if (disposed) return;
                bool waiting = false;
                foreach (Caller caller in calls.Values)
                {
                    if (caller.Voice.Count == 0) continue;
                    if (unchecked(Environment.TickCount - caller.VoiceSince) >= FlushMs) Take(caller, caller.Voice.Count);
                    else waiting = true;
                }
                if (waiting) flusher.Change(FlushMs, Timeout.Infinite);
            }
        }

        // Under gate: the first count samples of a phone's waiting voice, sent as one call.voice. Parts and hushes leave
        // under the same lock, so a hush can never overtake a part made before it.
        private void Take(Caller caller, int count)
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
            voiceParts++;
            voiceBytes += RemoteScreen.Envelope(message.Length);
            Send(caller.Device, message);
        }

        // Every message to a phone goes out here, in order. SendLive only queues, so this is quick enough to hold gate.
        private void Send(string device, string message)
        {
            lock (gate)
            {
                Action<string, string> sending = Sending;
                if (sending != null)
                {
                    try { sending(device, message); }
                    catch (Exception) { }
                }
                link.SendLive(device, new[] { message }, null);
            }
        }

        private static readonly string HushMessage = new RemoteJson.Writer().Text("type", "call.hush").ToString();

        // She stopped talking mid-line (talked over, Interrupt, or stopped): phones drop what they haven't played yet. A phone
        // whose line she just heard starts timing her answer from here.
        internal void Hush()
        {
            lock (gate)
            {
                if (disposed || calls.Count == 0) return;
                foreach (Caller caller in calls.Values) Hushed(caller);
            }
        }

        // The same for one phone: after its Interrupt when nothing of her answer was still going.
        internal void Hush(string device)
        {
            lock (gate)
            {
                Caller caller;
                if (!disposed && device != null && calls.TryGetValue(device, out caller)) Hushed(caller);
            }
        }

        // Under gate.
        private void Hushed(Caller caller)
        {
            caller.Voice.Clear();
            if (caller.Asked && Since(caller.EndedAt) < TimedForMs) caller.Timing = true;
            caller.Asked = false;
            Send(caller.Device, HushMessage);
        }

        // Her answer cut into the pieces her voice makes one at a time on a call: a sentence each, any too short to be
        // worth a voice of its own (like "Oh.") joined to the next, and at most MostPieces, the last taking what's left.
        internal const int LeastPiece = 12, MostPieces = 8;
        private static readonly Regex Ends = new Regex(@"(?<=[.!?\u2026][""'\u201D\u2019)\]]*)\s+|(?<=[\u3002\uFF01\uFF1F])\s*");
        private static readonly Regex Worded = new Regex(@"[\p{L}\p{Nd}]");

        internal static List<string> Pieces(string words)
        {
            List<string> pieces = new List<string>();
            string piece = "";
            foreach (string sentence in Ends.Split((words ?? "").Trim()))
            {
                string one = sentence.Trim();
                if (one.Length == 0) continue;
                piece = piece.Length == 0 ? one : piece + " " + one;
                if (piece.Length >= LeastPiece && Worded.IsMatch(piece) && pieces.Count < MostPieces - 1)
                {
                    pieces.Add(piece);
                    piece = "";
                }
            }
            if (piece.Length == 0) return pieces;
            // A short or wordless last bit goes with the piece before it.
            if (pieces.Count > 0 && (piece.Length < LeastPiece || !Worded.IsMatch(piece))) pieces[pieces.Count - 1] += " " + piece;
            else pieces.Add(piece);
            return pieces;
        }

        // Half the rate: each pair of samples averaged, which also keeps what's above the new rate from folding back in.
        internal static short[] Halve(short[] samples)
        {
            short[] half = new short[samples.Length / 2];
            for (int i = 0; i < half.Length; i++) half[i] = (short)((samples[i * 2] + samples[i * 2 + 1]) / 2);
            return half;
        }

        internal void VoiceState(bool on)
        {
            lock (gate)
            {
                if (disposed) return;
                foreach (Caller caller in calls.Values)
                    if (caller.Listen) Send(caller.Device, StateMessage(true, on, false, null));
            }
        }

        internal static string StateMessage(bool on, bool voice, bool hearing, string reason)
        {
            RemoteJson.Writer message = new RemoteJson.Writer().Text("type", "call.state").Flag("on", on);
            if (on) message.Flag("voice", voice).Flag("ears", hearing);
            if (reason != null) message.Text("reason", reason);
            return message.ToString();
        }

        internal static string HeardMessage(string id, string words) { return new RemoteJson.Writer().Text("type", "call.heard").Text("id", id).Text("text", RemoteMessages.Clip(words, 2000)).ToString(); }

        internal static void SmokeListening(List<string> checks)
        {
            RemoteLink link = new RemoteLink(RemoteState.Fresh(), "ws://127.0.0.1:1", "Test PC", "test", null);
            int heard = 0, earsChecked = 0, changed = 0;
            List<RemoteJson.Map> sent = new List<RemoteJson.Map>();
            using (ManualResetEvent answered = new ManualResetEvent(false))
            using (RemoteCall call = new RemoteCall(link, delegate { Interlocked.Increment(ref heard); return "hello"; }, delegate { answered.Set(); }))
            {
                call.EarsOn = delegate { earsChecked++; return true; };
                call.Changed += delegate { changed++; };
                call.Sending = delegate(string device, string json)
                {
                    RemoteJson.Map row = RemoteJson.ParseMap(json);
                    row["device"] = device;
                    lock (sent) sent.Add(row);
                };
                Func<RemoteJson.Map> last = delegate { lock (sent) return sent[sent.Count - 1]; };
                call.Allowed = delegate { return false; };
                foreach (string start in new[] { "{\"type\":\"call.start\"}", "{\"type\":\"call.start\",\"listen\":true}" })
                {
                    call.Heard("phone", start);
                    if (last().Text("type") != "call.state" || !last().Is("on", false) || last().Text("reason") != "not-allowed" || call.Any || earsChecked != 0) throw new Exception("Calls off lost its refusal or checked ears.");
                }
                call.Allowed = delegate { return true; };
                call.VoiceOn = delegate { return false; };
                call.Heard("phone", "{\"type\":\"call.start\",\"listen\":true,\"hush\":true}");
                if (!call.Listening("phone") || call.Hearing || !call.Hushes("phone") || !last().Is("on", true) || !last().Is("voice", false) || !last().Is("ears", false) || earsChecked != 0) throw new Exception("Listening with voice off lost its state or checked ears.");
                call.Heard("phone", "{\"type\":\"call.audio\",\"id\":\"room-audio\",\"pcm\":\"bad\"}");
                if (last().Text("code") != "not-allowed" || last().Text("re") != "room-audio" || heard != 0 || call.Counts()[3] != 0) throw new Exception("A listening phone's audio reached her ears.");
                call.VoiceOn = delegate { return true; };
                call.Voice(new short[6000], 24000);
                if (last().Text("type") != "call.voice" || last().Text("device") != "phone") throw new Exception("A listening phone missed her voice.");
                call.Heard("other", "{\"type\":\"call.start\",\"listen\":true}");
                int states = sent.Count;
                call.VoiceState(false);
                call.VoiceState(true);
                lock (sent)
                {
                    if (sent.Count != states + 4) throw new Exception("A listener missed a voice state change.");
                    foreach (string device in new[] { "phone", "other" })
                    foreach (bool voice in new[] { false, true })
                    {
                        bool found = false;
                        for (int n = states; n < sent.Count; n++)
                            if (sent[n].Text("device") == device && sent[n].Is("on", true) && sent[n].Is("voice", voice) && sent[n].Is("ears", false) && !sent[n].ContainsKey("reason")) found = true;
                        if (!found || earsChecked != 0) throw new Exception("A listener got a changed wire shape or loaded ears on a voice switch.");
                    }
                }
                call.Heard("other", "{\"type\":\"call.stop\"}");
                call.Heard("phone", "{\"type\":\"call.stop\"}");
                if (call.Any || call.Hearing) throw new Exception("Stopping listening left a call behind.");
                call.Heard("phone", "{\"type\":\"call.start\"}");
                if (!call.Hearing || call.Listening("phone") || !last().Is("ears", true) || earsChecked != 1) throw new Exception("An ordinary call after listening did not use her ears.");
                Caller real = call.calls["phone"];
                call.Heard("phone", "{\"type\":\"call.audio\",\"id\":\"real-line\",\"n\":0,\"rate\":16000,\"pcm\":\"AAA\"}");
                int before = changed;
                call.Heard("other", "{\"type\":\"call.start\",\"listen\":true,\"hush\":true}");
                if (last().Text("device") != "other" || !last().Is("on", false) || last().Text("reason") != "busy" || changed != before || call.calls["phone"] != real || !real.Lines.ContainsKey("real-line") || real.Listen || real.Hushes || call.OnCall("other") || earsChecked != 1) throw new Exception("A listening ask changed another phone's real call.");
                call.Heard("phone", "{\"type\":\"call.start\",\"listen\":true}");
                if (call.Hearing || !call.Listening("phone") || call.calls["phone"].Lines.Count != 0 || earsChecked != 1) throw new Exception("Switching to listening kept a microphone line or checked ears.");
                call.Heard("phone", "{\"type\":\"call.hush\",\"re\":\"room-turn\"}");
                if (call.Any || last().Text("reason") != "ended") throw new Exception("Interrupt did not end listening.");
                foreach (string what in new[] { "left", "kicked", "removed", "session", "all" })
                {
                    call.Heard("phone", "{\"type\":\"call.start\",\"listen\":true}");
                    call.Happened(what == "all" ? "left" : what, what == "all" ? null : "phone");
                    if (call.Any || call.Hearing) throw new Exception("Listening survived " + what + ".");
                }
                call.Interrupted = delegate { };
                call.Heard("phone", "{\"type\":\"call.start\",\"listen\":true}");
                call.Heard("phone", "{\"type\":\"call.hush\",\"re\":\"room-turn\"}");
                if (call.Any || last().Text("reason") != "ended" || earsChecked != 1 || heard != 0) throw new Exception("Interrupt with the PC handler left listening or loaded ears.");
                call.Heard("phone", "{\"type\":\"call.start\"}");
                call.Heard("phone", "{\"type\":\"call.audio\",\"id\":\"ordinary-line\",\"n\":0,\"rate\":16000,\"pcm\":\"AAA\",\"end\":true}");
                if (!answered.WaitOne(3000) || heard != 1 || earsChecked != 2 || call.Listening("phone") || !call.Hearing) throw new Exception("An ordinary call after listening did not hear and dispatch its line.");
            }
            using (RemoteCall call = new RemoteCall(link, delegate { throw new Exception("Busy listening used ears."); }, delegate { }))
            {
                List<string> rows = new List<string>();
                int changes = 0;
                call.Sending = delegate(string device, string json) { rows.Add(device + " " + json); };
                call.Changed += delegate { changes++; };
                call.Heard("first", "{\"type\":\"call.start\",\"listen\":true}");
                call.Heard("second", "{\"type\":\"call.start\",\"listen\":true}");
                call.Heard("real", "{\"type\":\"call.start\"}");
                if (call.Callers().Count != 1 || !call.Hearing || call.OnCall("first") || call.OnCall("second") || changes != 3 || !rows.Contains("first " + StateMessage(false, false, false, "busy")) || !rows.Contains("second " + StateMessage(false, false, false, "busy"))) throw new Exception("A real call left another phone listening or missed the badge change.");
                rows.Clear();
                call.Voice(new short[6000], 24000);
                if (rows.Count != 1 || !rows[0].StartsWith("real ", StringComparison.Ordinal)) throw new Exception("An evicted listener still received her voice.");
                call.calls["first"] = new Caller { Device = "first", Listen = true };
                call.Heard("first", "{\"type\":\"call.start\",\"listen\":true}");
                if (call.OnCall("first") || call.Callers().Count != 1 || changes != 4 || rows[rows.Count - 1] != "first " + StateMessage(false, false, false, "busy")) throw new Exception("A busy retry left its old listener on the PC.");
                call.End("real", null);
                call.Heard("first", "{\"type\":\"call.start\",\"listen\":true}");
                call.Allowed = delegate { return false; };
                call.Heard("first", "{\"type\":\"call.start\",\"listen\":true}");
                if (call.Any || rows[rows.Count - 1] != "first " + StateMessage(false, false, false, "not-allowed")) throw new Exception("A refused retry left its old listener on the PC.");
            }
            link.Stop();
            checks.Add("PASS Room listening skips ears, refuses audio, gets voice, stops on stop, Interrupt and lost sessions, restores ordinary calls and reports calls off, voice off and busy separately; real calls remove listeners and busy or refused retries leave none behind");
        }

        private void RaiseChanged()
        {
            Action changed = Changed;
            if (changed == null) return;
            try { changed(); }
            catch (Exception) { }
        }
    }
}
