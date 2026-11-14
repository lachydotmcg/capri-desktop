using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // A phone waiting for Allow: the name it gave, cleaned, and the code both screens should show.
    internal sealed class RemoteAsk
    {
        internal string Pending, Name, Code, App;
        internal RemoteChannel Channel;
    }

    // The PC's side of Capri Remote (remote/PROTOCOL.md sections 4, 5, 7, 8 and 9): one connection out to the relay,
    // pairing, a session per phone and the chat messages. There's no window or brain in here: app/RemoteChat.cs joins it
    // to her chat, app/MainframeRemote.cs to the Mainframe, and app/RemoteTests drives it against the real relay.
    //
    // Threads: frames are read on one worker. Every frame leaves through one pump per connection, which takes a session's
    // next sequence number and sends in the same step under one lock, so frames leave in sequence order and only one
    // SendAsync ever runs. Everything shared sits behind gate. Events are raised outside it, on the thread that made the
    // change, and must return quickly.
    internal sealed partial class RemoteLink
    {
        internal const string DefaultHost = "capri-remote.lachydotmcg.workers.dev";
        internal const int CodeSeconds = 600;
        private static readonly string[] Features = { "chat" };
        internal const string PairFeature = "pair";
        private const int PongMs = 10000, ReadyMs = 10000, ConnectMs = 15000, FirstWaitMs = 1000, LastWaitMs = 60000, BusyWaitMs = 10000;
        private const int BatchMs = 100, TypingMs = 10000, AskMs = 180000, SeenMs = 600000, KeepMs = 600000, KeepCount = 64;
        private const int PartChars = 4000, MaxFrame = 262144;

        // PROTOCOL.md 4.2: the exact heartbeat every 30 seconds. The tests shorten it to see several.
        internal int BeatMs = 30000;

        // Raised after something changed: what ("status", "qr", "ask", "ask-ended", "allowed", "paired", "pair-failed",
        // "joined", "left", "session", "version", "kicked", "removed", "new-room", "room-reset", "link"), and the device or
        // detail. "link" is a paired phone that got a pairing link (PROTOCOL.md 5.1).
        internal event Action<string, string> Happened;

        // A phone said something to Capri: its device id, the chat.send id (every answer's re) and the text.
        internal event Action<string, string, string> Said;

        private readonly object gate = new object();
        private readonly string relay, pcName, app;
        private readonly Action<RemoteState> save;
        private readonly Dictionary<string, Phone> phones = new Dictionary<string, Phone>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> admitting = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> revoking = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, long> seen = new Dictionary<string, long>(StringComparer.Ordinal);
        private readonly Dictionary<string, Ask> asks = new Dictionary<string, Ask>(StringComparer.Ordinal);
        private readonly List<Part> parts = new List<Part>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random jitter = new Random();
        private RemoteState state;
        private Pairing pairing;
        private Wire wire;
        private bool ready, started, stopped, ticking;
        private long tickDue;
        private string status = "off";
        private CancellationTokenSource run;
        private Timer ticker;

        // relay is a base address from RelayBase. state is the room and the paired phones; save gets a copy after each
        // change, to seal and store.
        internal RemoteLink(RemoteState state, string relay, string pcName, string app, Action<RemoteState> save)
        {
            if (state == null) throw new ArgumentNullException("state");
            this.state = state;
            this.relay = relay;
            this.pcName = RemoteMessages.Clip(pcName, 64);
            this.app = RemoteMessages.Clip(app, 64);
            this.save = save;
            foreach (RemoteDevice device in state.Devices) phones[device.Id] = new Phone(device);
        }

        // ---------- What the Mainframe reads ----------

        // "off", "connecting", "online", "retrying", or "replaced" (another copy of Capri took this room; stopped).
        internal string Status { get { lock (gate) return status; } }

        internal bool Ready { get { lock (gate) return ready; } }

        // Phones connected now: ready.online plus joined minus left. The badge shows while it isn't 0.
        internal int PhonesOnline
        {
            get
            {
                lock (gate)
                {
                    int count = 0;
                    foreach (Phone phone in phones.Values) if (phone.Online) count++;
                    return count;
                }
            }
        }

        // The paired phones, newest first, without their keys.
        internal List<RemoteDevice> Devices()
        {
            lock (gate)
            {
                List<RemoteDevice> list = new List<RemoteDevice>();
                foreach (RemoteDevice device in state.Devices) list.Add(new RemoteDevice { Id = device.Id, Name = device.Name, Added = device.Added, From = device.From, Parent = device.Parent });
                list.Sort(delegate (RemoteDevice a, RemoteDevice b) { return b.Added.CompareTo(a.Added); });
                return list;
            }
        }

        internal bool IsOnline(string device)
        {
            lock (gate)
            {
                Phone phone;
                return device != null && phones.TryGetValue(device, out phone) && phone.Online;
            }
        }

        internal bool HasSession(string device)
        {
            lock (gate)
            {
                Phone phone;
                return device != null && phones.TryGetValue(device, out phone) && phone.Session != null;
            }
        }

        // The QR to show, while one is open and nobody has used it yet. A phone's pairing link is never shown: it pairs
        // without Allow, so a QR of it would let anyone near the screen in.
        internal string Qr { get { lock (gate) return pairing != null && pairing.For == null && pairing.Ask == null && !pairing.Used ? pairing.Url : null; } }

        // The paired phone whose pairing link is open, by name, while nobody has used it and it hasn't run out.
        internal string LinkFor
        {
            get
            {
                lock (gate)
                {
                    if (pairing == null || pairing.For == null || pairing.Used || Now() >= pairing.Expires) return null;
                    RemoteDevice asker = state.Find(pairing.For);
                    return asker == null ? null : asker.Name;
                }
            }
        }

        // Seconds left on the open QR code, by this PC's clock (the relay keeps its own).
        internal int QrSeconds { get { lock (gate) return pairing == null ? 0 : (int)Math.Max(0, (pairing.Expires - Now() + 999) / 1000); } }

        // The phone waiting for Allow, without its keys.
        internal RemoteAsk Asking
        {
            get
            {
                lock (gate)
                {
                    if (pairing == null || pairing.Ask == null) return null;
                    RemoteAsk ask = pairing.Ask;
                    return new RemoteAsk { Pending = ask.Pending, Name = ask.Name, Code = ask.Code, App = ask.App };
                }
            }
        }

        // ---------- Starting and stopping ----------

        internal void Start()
        {
            CancellationToken token;
            lock (gate)
            {
                if (started) return;
                started = true;
                run = new CancellationTokenSource();
                ticker = new Timer(Tick);
                token = run.Token;
            }
            Task.Run(delegate { return Loop(token); });
        }

        // Turns Remote off: a QR still open is cancelled (a phone still waiting gets a no), then the connection closes.
        // Nothing is saved or raised after this.
        internal void Stop()
        {
            lock (gate)
            {
                if (!started || stopped) return;
                stopped = true;
                status = "off";
                EndPairing(null, true);
                if (wire != null) wire.Push(Outgoing.Closing());
                if (ticker != null) ticker.Dispose();
                // A connection that won't close by itself is cut off; one that isn't up stops now.
                if (wire != null) run.CancelAfter(3000);
                else run.Cancel();
            }
        }

        // The relay's base address from what was typed: a host such as capri-remote.lachydotmcg.workers.dev (wss), or a
        // ws:// or wss:// address. Plain ws:// is only allowed to this PC, for a relay under wrangler dev. Null when it
        // isn't a host.
        internal static string RelayBase(string host)
        {
            string text = (host ?? "").Trim();
            if (text.Length == 0) text = DefaultHost;
            string scheme = null;
            int at = text.IndexOf("://", StringComparison.Ordinal);
            if (at >= 0)
            {
                string given = text.Substring(0, at).ToLowerInvariant();
                if (given == "wss" || given == "https") scheme = "wss";
                else if (given == "ws" || given == "http") scheme = "ws";
                else return null;
                text = text.Substring(at + 3);
            }
            text = text.TrimEnd('/');
            if (text.Length == 0 || text.IndexOfAny(new[] { '/', '\\', '?', '#', '@', ' ' }) >= 0) return null;
            Uri probe, uri;
            if (!Uri.TryCreate("wss://" + text + "/", UriKind.Absolute, out probe) || probe.Host.Length == 0) return null;
            if (scheme == null) scheme = probe.IsLoopback ? "ws" : "wss";
            if (scheme == "ws" && !probe.IsLoopback) return null;
            if (!Uri.TryCreate(scheme + "://" + text + "/", UriKind.Absolute, out uri) || uri.PathAndQuery != "/") return null;
            // As typed, lower-cased: .NET Framework would write [::1] out in full.
            return scheme + "://" + text.ToLowerInvariant();
        }

        // ---------- Pairing (PROTOCOL.md section 5) ----------

        // Opens a fresh one-time code and pairing key on the relay and returns the QR's URL, or null while not signed in.
        // A code still open is replaced, and a phone still waiting on it gets a no.
        internal string NewCode()
        {
            List<Note> notes = new List<Note>();
            string url;
            lock (gate)
            {
                if (stopped || !ready || wire == null) return null;
                EndPairing(notes, false);
                url = OpenCode();
                notes.Add(new Note("qr", null));
            }
            Raise(notes, null);
            return url;
        }

        // The pairing dialog closed: forget the code on the relay (code-cancel), or say no to a phone still waiting. A paired
        // phone's pairing link isn't the dialog's, so it stays open.
        internal void CloseCode()
        {
            List<Note> notes = new List<Note>();
            lock (gate) if (pairing == null || pairing.For == null) EndPairing(notes, true);
            Raise(notes, null);
        }

        // Allow the waiting phone.
        internal bool Allow()
        {
            List<Note> notes = new List<Note>();
            RemoteState copy;
            lock (gate)
            {
                if (stopped || wire == null || pairing == null || pairing.Ask == null) return false;
                copy = Admit(notes);
            }
            Save(copy);
            Raise(notes, null);
            return true;
        }

        // Under gate, with a phone asking: a new device id, token and key go to it sealed in pair-allow, and the relay keeps
        // only the token's hash. The PC keeps the id, key, name and date, and which phone's link it used if any, and forgets
        // the token and the pairing key. Returns the state to save.
        private RemoteState Admit(List<Note> notes)
        {
            RemoteAsk ask = pairing.Ask;
            RemoteDevice asker = pairing.For == null ? null : state.Find(pairing.For);
            byte[] token = RemoteCrypto.Random(32);
            RemoteDevice device = new RemoteDevice { Id = RemoteCrypto.B64u(RemoteCrypto.Random(16)), Key = RemoteCrypto.Random(32), Name = ask.Name, Added = DateTime.UtcNow };
            if (pairing.For != null) device.From = asker == null ? "Unnamed phone" : asker.Name;
            device.Parent = pairing.For;
            string tokenHash = RemoteCrypto.Hash(token);
            byte[] env = ask.Channel.Seal(RemoteMessages.PairAllow(device.Id, RemoteCrypto.B64u(token), RemoteCrypto.B64u(device.Key), pcName, app));
            Array.Clear(token, 0, token.Length);
            Push(Outgoing.Control(RemoteMessages.Admit(ask.Pending, device.Id, tokenHash, RemoteCrypto.B64u(env))));
            state.Devices.Add(device);
            phones[device.Id] = new Phone(device);
            admitting[ask.Pending] = device.Id;
            pairing.Forget();
            pairing = null;
            notes.Add(new Note("qr", null));
            notes.Add(new Note("allowed", device.Id));
            return state.Copy();
        }

        internal bool Deny()
        {
            List<Note> notes = new List<Note>();
            lock (gate)
            {
                if (stopped || pairing == null || pairing.Ask == null) return false;
                EndPairing(notes, true);
            }
            Raise(notes, null);
            return true;
        }

        // Removes a paired phone. If it's connected it's told first (a sealed revoke, so it forgets the pairing), then the
        // relay deletes it and closes it with 4403. Offline, the relay is told on the next sign-in instead.
        internal void Revoke(string id)
        {
            List<Note> notes = new List<Note>();
            RemoteState copy;
            lock (gate)
            {
                RemoteDevice device = state.Find(id);
                if (stopped || device == null) return;
                foreach (string remove in RevokeIds(state.Devices, id)) RevokeOne(state.Find(remove), notes);
                copy = state.Copy();
            }
            Save(copy);
            Raise(notes, null);
        }

        internal static List<string> RevokeIds(List<RemoteDevice> devices, string id)
        {
            List<string> ids = new List<string> { id };
            for (int i = 0; i < ids.Count; i++)
                foreach (RemoteDevice device in devices)
                    if (device.Parent == ids[i] && !ids.Contains(device.Id)) ids.Add(device.Id);
            return ids;
        }

        private void RevokeOne(RemoteDevice device, List<Note> notes)
        {
            if (device == null) return;
            Phone phone;
            if (phones.TryGetValue(device.Id, out phone) && phone.Session != null) Push(Outgoing.Message(phone, RemoteMessages.Revoked()));
            if (wire != null && revoking.Add(device.Id)) Push(Outgoing.Control(RemoteMessages.Revoke(device.Id)));
            Remove(device, notes);
        }

        internal RemoteState RemoveAll()
        {
            lock (gate)
            {
                List<Note> notes = new List<Note>();
                if (!stopped)
                    foreach (RemoteDevice device in new List<RemoteDevice>(state.Devices))
                        try { RevokeOne(device, notes); }
                        catch (Exception) { }
                Stop();
                stopped = true;
                if (pairing != null) pairing.Forget();
                pairing = null;
                state = RemoteState.Fresh();
                phones.Clear();
                parts.Clear();
                asks.Clear();
                seen.Clear();
                admitting.Clear();
                ready = false;
                return state.Copy();
            }
        }

        // ---------- Chat out (PROTOCOL.md section 8) ----------

        internal void Typing(string device, bool on, string re) { Send(device, RemoteMessages.Typing(on, re), re, true, null); }

        internal void Tool(string device, string id, string name, string summary, string toolState, string re, string move = null)
        {
            Send(device, RemoteMessages.Tool(id, RemoteMessages.Clip(name, 64), RemoteMessages.Clip(summary, 200), toolState, re, move), re, false, id);
        }

        internal void Error(string device, string code, string re)
        {
            Send(device, RemoteMessages.Error(code, re), re, false, null);
            lock (gate) asks.Remove(device + " " + re);
        }

        // One part of a reply. Parts are gathered for 100 ms, so a phone gets at most about 10 reply frames a second
        // however finely her words stream; done marks the last.
        internal void Reply(string device, string id, string re, string text, bool done)
        {
            lock (gate)
            {
                Phone phone = Live(device);
                if (phone == null || !RemoteMessages.IdOk(id)) return;
                Part part = null;
                foreach (Part open in parts) if (open.Phone == phone && open.Id == id) part = open;
                if (part == null)
                {
                    part = new Part { Phone = phone, Id = id, Re = re };
                    parts.Add(part);
                }
                part.Words.Append(text ?? "");
                if (done)
                {
                    part.Done = true;
                    asks.Remove(phone.Device.Id + " " + re);
                }
                else Touch(phone, re);
                Arm(BatchMs);
            }
        }

        private void Send(string device, string json, string re, bool stale, string toolId)
        {
            lock (gate)
            {
                Phone phone = Live(device);
                if (phone == null) return;
                FlushParts(phone);
                Outgoing item = Outgoing.Message(phone, json);
                item.Stale = stale;
                item.ToolId = toolId;
                Push(item);
                Touch(phone, re);
            }
        }

        // ---------- Screen and call (app/RemoteScreen.cs, app/RemoteCall.cs) ----------

        // A phone's watch.* or call.* message: its device id and the message. Raised on the read worker, outside gate.
        internal event Action<string, RemoteJson.Map> LiveHeard;

        private readonly List<string> offered = new List<string>();
        private readonly List<KeyValuePair<string, RemoteJson.Map>> liveIn = new List<KeyValuePair<string, RemoteJson.Map>>();

        // A feature for hello-ok's list beside chat, such as "watch" or "call". Sessions that start after this offer it.
        internal void Offer(string feature) { lock (gate) if (!offered.Contains(feature)) offered.Add(feature); }

        // Under gate. hello-ok's features: the link's own, the ones offered (watch, call), her brains while something answers
        // for them, then pairing links (PROTOCOL.md 8.3), which the link answers itself.
        private string[] Offered()
        {
            List<string> all = new List<string>(Features);
            all.AddRange(offered);
            if (BrainAsked != null && !all.Contains(RemoteBrainKeys.Feature)) all.Add(RemoteBrainKeys.Feature);
            if (!all.Contains(PairFeature)) all.Add(PairFeature);
            return all.ToArray();
        }

        // What the next hello-ok will list, for her smoke test.
        internal string[] FeaturesNow { get { lock (gate) return Offered(); } }

        private static bool IsLive(string type) { return type != null && (type.StartsWith("watch.", StringComparison.Ordinal) || type.StartsWith("call.", StringComparison.Ordinal) || type == "job.list" || type == "job.stop" || type == "job.answer"); }

        // Job messages (PROTOCOL.md section 8.4) wait in each phone's own queue instead of going straight out: to one phone,
        // or to every phone that's online, one partway through hello too, so a job that ends then still reaches it.
        internal void JobSend(string device, string json)
        {
            lock (gate)
            {
                Phone phone = Live(device);
                if (phone == null || !phone.Online || phone.Kicked) return;
                QueueJob(phone, json);
            }
        }

        internal void JobBroadcast(string json)
        {
            lock (gate)
                foreach (Phone phone in phones.Values)
                    if (phone.Online && (phone.Session != null || phone.JobHello) && !phone.Kicked) QueueJob(phone, json);
        }

        // A step's newer update replaces its older one still waiting, and a job's state its same state, so a phone that's
        // behind gets where things are now. A different state never jumps an older one, so running, asking and running again
        // arrive in order. A phone that can't keep up, like during a game's quick keys, keeps her newest 30 steps, the most
        // JOBS lists, instead of falling minutes behind.
        private void QueueJob(Phone phone, string json)
        {
            RemoteJson.Map message = RemoteJson.ParseMap(json);
            if (message == null) return;
            string type = message.Text("type");
            double n;
            string key = type + " " + (message.Text("id") ?? message.Text("re")) + " " + (message.Number("n", out n) ? n.ToString(System.Globalization.CultureInfo.InvariantCulture) : message.Text("state"));
            for (int i = phone.Jobs.Count - 1; i >= 0; i--)
            {
                if (phone.Jobs[i].Key == key) { phone.Jobs[i] = new KeyValuePair<string, string>(key, json); Arm(200); return; }
                if (type == "job.state" && phone.Jobs[i].Key.StartsWith("job.state " + message.Text("id") + " ", StringComparison.Ordinal)) break;
            }
            phone.Jobs.Add(new KeyValuePair<string, string>(key, json));
            if (type == "job.step" && phone.Jobs.FindAll(frame => frame.Key.StartsWith("job.step ", StringComparison.Ordinal)).Count > JobSteps)
                phone.Jobs.RemoveAt(phone.Jobs.FindIndex(frame => frame.Key.StartsWith("job.step ", StringComparison.Ordinal)));
            Arm(200);
        }

        private const int JobSteps = 30;

        // At most one job frame a phone every 200 ms, five a second, with one in flight at a time, taking turns between states
        // and steps so neither holds up the other. Frames made before a phone's newer hello never go out (Frames).
        private bool FlushJobs(long now)
        {
            bool more = false;
            foreach (Phone phone in phones.Values)
            {
                if (!phone.Online || phone.Kicked) { phone.Jobs.Clear(); continue; }
                if (phone.Jobs.Count == 0) continue;
                more = true;
                if (phone.Session == null || now < phone.JobAt || System.Threading.Volatile.Read(ref phone.JobBusy) != 0) continue;
                int step = phone.Jobs.FindIndex(delegate(KeyValuePair<string, string> frame) { return frame.Key.StartsWith("job.step ", StringComparison.Ordinal); });
                int state = phone.Jobs.FindIndex(delegate(KeyValuePair<string, string> frame) { return !frame.Key.StartsWith("job.step ", StringComparison.Ordinal); });
                int next = state >= 0 && (step < 0 || !phone.JobStepNext) ? state : step;
                Outgoing item = Outgoing.Message(phone, phone.Jobs[next].Value);
                phone.Jobs.RemoveAt(next);
                phone.JobStepNext = next == state;
                phone.JobAt = now + 200;
                phone.JobBusy = 1;
                item.Stale = true;
                item.Job = true;
                item.Round = phone.Hellos;
                Phone target = phone;
                item.Left = delegate { System.Threading.Interlocked.Exchange(ref target.JobBusy, 0); };
                Push(item);
            }
            return more;
        }

        // True while a phone still has job frames to go, which keeps the ticker running.
        private bool JobsWaiting()
        {
            foreach (Phone phone in phones.Values) if (phone.Jobs.Count > 0) return true;
            return false;
        }

        // Job frames on a stand-in wire: both live phones get each one sealed with their own keys, a step's updates batch to
        // its newest, frames keep to five a second, a frame from before a phone's newer hello is dropped, a phone that falls
        // behind keeps only the newest 30 steps, and a job that ends during hello waits for the phone.
        internal static void SmokeJobs(List<string> checks)
        {
            RemoteState state = RemoteState.Fresh();
            Dictionary<string, RemoteKeys> keys = new Dictionary<string, RemoteKeys>();
            for (int i = 0; i < 2; i++)
            {
                RemoteDevice device = new RemoteDevice { Id = RemoteMessages.NewId(), Key = RemoteCrypto.Random(32), Name = "Phone " + i };
                state.Devices.Add(device);
                keys[device.Id] = RemoteCrypto.SessionKeys(device.Key, new byte[16], RemoteCrypto.Random(16));
            }
            RemoteLink link = new RemoteLink(state, "ws://127.0.0.1:1", "Test PC", "test", null);
            link.Offer("jobs");
            if (Array.IndexOf(link.FeaturesNow, "jobs") < 0) throw new Exception("Jobs weren't offered at hello.");
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                link.wire = new Wire(stop);
                try
                {
                    foreach (Phone phone in link.phones.Values)
                    {
                        phone.Online = true;
                        phone.Session = new RemoteChannel(keys[phone.Device.Id], RemoteCrypto.KindSession);
                    }
                    string started = RemoteMessages.JobState("j1", "Opening an app", "running", 0, 1, 0, null, "m1");
                    link.JobBroadcast(started);
                    link.JobBroadcast(RemoteMessages.JobStep("j1", 1, "type", "Typing some text", false, false));
                    link.JobBroadcast(RemoteMessages.JobStep("j1", 1, "type", "Typed some text", true, false));
                    link.FlushJobs(0);
                    HashSet<string> got = new HashSet<string>();
                    Outgoing item;
                    while ((item = link.wire.Take()) != null)
                    {
                        got.Add(item.Phone.Device.Id);
                        List<KeyValuePair<byte[], WebSocketMessageType>> frames = link.Frames(item);
                        if (frames.Count != 1) throw new Exception("A jobs tick sent more than one frame to a phone.");
                        byte[] frame = frames[0].Key;
                        string reason;
                        byte[] plain = RemoteCrypto.Open(keys[item.Phone.Device.Id].PcToPhone, RemoteCrypto.KindSession, RemoteCrypto.PcToPhone, 0, frame, 16, frame.Length - 16, out reason);
                        if (plain == null || Encoding.UTF8.GetString(plain) != started) throw new Exception("A phone couldn't open its job state.");
                        item.Finish();
                    }
                    if (got.Count != 2) throw new Exception("A live phone missed the job.");
                    link.FlushJobs(199);
                    if (link.wire.Take() != null) throw new Exception("Jobs sent faster than five frames a second.");
                    link.FlushJobs(200);
                    int steps = 0;
                    while ((item = link.wire.Take()) != null)
                    {
                        if (RemoteJson.ParseMap(item.Json).Text("label") != "Typed some text") throw new Exception("A step wasn't batched to its newest state.");
                        item.Phone.Hellos++;
                        if (link.Frames(item).Count != 0) throw new Exception("A job frame went to a replacement session.");
                        item.Finish();
                        steps++;
                    }
                    if (steps != 2) throw new Exception("A phone missed the job's step.");
                    for (int n = 2; n <= 41; n++) link.JobBroadcast(RemoteMessages.JobStep("j1", n, "click", "Clicked Play", true, false));
                    foreach (Phone phone in link.phones.Values)
                    {
                        if (phone.Jobs.Count != JobSteps || phone.Jobs[0].Key != "job.step j1 12") throw new Exception("A phone that fell behind kept " + phone.Jobs.Count + " steps instead of her newest " + JobSteps + ".");
                        phone.Jobs.Clear();
                    }
                    foreach (Phone phone in link.phones.Values) { phone.Session = null; phone.JobHello = true; }
                    link.JobBroadcast(RemoteMessages.JobState("j1", "Opening an app", "done", 1, 1, 2, null, null));
                    foreach (Phone phone in link.phones.Values)
                        if (phone.Jobs.Count != 1) throw new Exception("A job ending during hello was lost.");
                }
                finally { link.wire.Socket.Dispose(); }
            }
            checks.Add("PASS job messages reach both live phones, open with each phone's keys, batch steps, stay at five frames a second, drop frames from a replaced session and keep a phone that fell behind to the newest 30 steps");
        }

        internal static RemoteLink SmokeSession(RemoteState state)
        {
            RemoteLink link = new RemoteLink(state, "ws://127.0.0.1:1", "Test PC", "test", null);
            foreach (Phone phone in link.phones.Values)
            {
                phone.Online = true;
                phone.Session = new RemoteChannel(RemoteCrypto.SessionKeys(phone.Device.Key, new byte[16], new byte[16]), RemoteCrypto.KindSession);
            }
            return link;
        }

        internal static void SmokePairing(List<string> checks)
        {
            RemoteState state = RemoteState.Fresh();
            RemoteDevice parent = new RemoteDevice { Id = RemoteMessages.NewId(), Key = RemoteCrypto.Random(32), Name = "Phone" };
            RemoteDevice other = new RemoteDevice { Id = RemoteMessages.NewId(), Key = RemoteCrypto.Random(32), Name = "Phone" };
            state.Devices.Add(parent);
            state.Devices.Add(other);
            RemoteLink link = SmokeSession(state);
            using (CancellationTokenSource stop = new CancellationTokenSource())
            {
                link.wire = new Wire(stop);
                link.ready = true;
                try
                {
                    List<Note> notes = new List<Note>();
                    Phone phone = link.phones[parent.Id];
                    Func<List<RemoteJson.Map>> take = delegate
                    {
                        List<RemoteJson.Map> messages = new List<RemoteJson.Map>();
                        Outgoing item;
                        while ((item = link.wire.Take()) != null)
                        {
                            RemoteJson.Map message = item.Close ? null : RemoteJson.ParseMap(item.Json ?? item.Text);
                            if (message != null) messages.Add(message);
                            item.Finish();
                        }
                        return messages;
                    };
                    link.PairAsk(phone, "pair-first", notes);
                    if (link.pairing == null || link.pairing.For != parent.Id || link.Qr != null || !take().Exists(delegate(RemoteJson.Map message) { return message.Text("type") == "pair.link" && message.Text("re") == "pair-first"; })) throw new Exception("A paired phone didn't get its private link.");
                    Pairing first = link.pairing;
                    phone.Session = new RemoteChannel(RemoteCrypto.SessionKeys(parent.Key, new byte[16], new byte[16]), RemoteCrypto.KindSession);
                    link.PairAsk(phone, "pair-again", notes);
                    List<RemoteJson.Map> again = take();
                    if (link.pairing != first || again.Count != 1 || again[0].Text("code") != "busy" || again[0].Text("re") != "pair-again") throw new Exception("A second pair.ask within a minute wasn't busy after a new session.");
                    phone.PairAt -= 60000;
                    link.PairAsk(phone, "pair-later", notes);
                    if (link.pairing == first || !take().Exists(delegate(RemoteJson.Map message) { return message.Text("type") == "pair.link"; })) throw new Exception("A phone couldn't ask again after a minute.");
                    link.pairing.Ask = new RemoteAsk { Pending = RemoteMessages.NewId(), Name = "Home screen", Channel = new RemoteChannel(RemoteCrypto.PairKeys(link.pairing.Key, new byte[16]), RemoteCrypto.KindPair) };
                    link.Admit(notes);
                    RemoteDevice child = state.Devices[state.Devices.Count - 1];
                    if (child.Parent != parent.Id || child.From != parent.Name) throw new Exception("A link-added phone lost its parent's id or name.");
                    RemoteState saved = RemoteState.Unseal(state.Copy().Seal());
                    if (saved == null || saved.Find(child.Id).Parent != parent.Id || saved.Find(child.Id).From != parent.Name) throw new Exception("A saved phone lost its link parent.");
                    take();
                    link.PairAsk(link.phones[child.Id], "pair-child", notes);
                    again = take();
                    if (again.Count != 1 || again[0].Text("code") != "not-allowed" || again[0].Text("re") != "pair-child" || link.pairing != null) throw new Exception("A link-added phone could make another pairing link.");
                    RemoteDevice legacy = new RemoteDevice { Id = RemoteMessages.NewId(), Key = RemoteCrypto.Random(32), Name = "Old home screen", From = parent.Name };
                    state.Devices.Add(legacy);
                    link.phones[legacy.Id] = new Phone(legacy);
                    link.PairAsk(link.phones[legacy.Id], "pair-legacy", notes);
                    again = take();
                    if (again.Count != 1 || again[0].Text("code") != "not-allowed") throw new Exception("An older link-added phone could make a pairing link.");
                    RemoteDevice descendant = new RemoteDevice { Id = RemoteMessages.NewId(), Key = RemoteCrypto.Random(32), Name = "Descendant", From = child.Name, Parent = child.Id };
                    state.Devices.Add(descendant);
                    link.phones[descendant.Id] = new Phone(descendant);
                    link.Revoke(parent.Id);
                    again = take();
                    foreach (string id in new[] { parent.Id, child.Id, descendant.Id })
                        if (state.Find(id) != null || link.HasSession(id) || !again.Exists(delegate(RemoteJson.Map message) { return message.Text("t") == "revoke" && message.Text("device") == id; })) throw new Exception("Revoking a phone missed a link child or its relay revoke.");
                    if (state.Find(other.Id) == null || state.Find(legacy.Id) == null) throw new Exception("Revoking a phone matched a name instead of a parent id.");
                    while (state.Devices.Count < 16)
                    {
                        RemoteDevice device = new RemoteDevice { Id = RemoteMessages.NewId(), Key = RemoteCrypto.Random(32), Name = "Phone" };
                        state.Devices.Add(device);
                        link.phones[device.Id] = new Phone(device);
                    }
                    HashSet<string> all = new HashSet<string>();
                    foreach (RemoteDevice device in state.Devices) all.Add(device.Id);
                    link.started = true;
                    link.run = stop;
                    RemoteState fresh = link.RemoveAll();
                    HashSet<string> revoked = new HashSet<string>();
                    foreach (RemoteJson.Map message in take()) if (message.Text("t") == "revoke") revoked.Add(message.Text("device"));
                    if (!all.SetEquals(revoked) || fresh.Devices.Count != 0 || link.Devices().Count != 0 || fresh.Room == state.Room || fresh.Token == state.Token || link.Ready || !link.Stopped) throw new Exception("Remove all phones missed a revoke, kept a phone or reused the room.");
                }
                finally { link.wire.Socket.Dispose(); }
            }
            checks.Add("PASS pairing links refuse link-added phones, limit each phone to one ask a minute across sessions, save the parent's id and revoke its known link children; Remove all phones queues all 16 relay revokes and starts a fresh room with no phones");
        }

        // Messages that only mean something now, like a screen frame or a moment of her voice, sealed together in order. They
        // never wait for a phone's next session. left runs once they've gone to the relay, or been dropped: it must not
        // block or take a lock. False, with left not run, when that phone has no session.
        internal bool SendLive(string device, IList<string> messages, Action left)
        {
            lock (gate)
            {
                Phone phone = Live(device);
                if (phone == null || phone.Session == null || wire == null || wire.Closing || stopped) return false;
                Outgoing item = Outgoing.Message(phone, null);
                item.Many = new List<string>(messages);
                item.Stale = true;
                item.Left = left;
                Push(item);
                return true;
            }
        }

        // A paired phone's cleaned name, for the PC's watching badge.
        internal string PhoneName(string device)
        {
            lock (gate)
            {
                RemoteDevice found = device == null ? null : state.Find(device);
                return found == null ? null : found.Name;
            }
        }

        private void RaiseLive()
        {
            List<KeyValuePair<string, RemoteJson.Map>> heard;
            lock (gate)
            {
                if (liveIn.Count == 0) return;
                heard = new List<KeyValuePair<string, RemoteJson.Map>>(liveIn);
                liveIn.Clear();
            }
            Action<string, RemoteJson.Map> hook = LiveHeard;
            if (hook == null || Stopped) return;
            foreach (KeyValuePair<string, RemoteJson.Map> message in heard)
            {
                try { hook(message.Key, message.Value); }
                catch (Exception) { }
            }
        }

        // ---------- The connection ----------

        private async Task Loop(CancellationToken stop)
        {
            int wait = FirstWaitMs;
            while (!stop.IsCancellationRequested && !Stopped)
            {
                SetStatus("connecting");
                long began = Now();
                Ending end = await Connection(stop).ConfigureAwait(false);
                if (stop.IsCancellationRequested || Stopped) break;
                if (end.Code == 4409)
                {
                    // Another copy of Capri signed in to this room. Don't fight it: stop and say so.
                    SetStatus("replaced");
                    return;
                }
                if (end.Code == 4401)
                {
                    // The relay holds a different PC token for this room: start a new room, and phones pair again.
                    NewRoom();
                    wait = FirstWaitMs;
                }
                else if (end.Ready && Now() - began > 10000) wait = FirstWaitMs;
                int delay = wait + jitter.Next(wait * 3 / 10 + 1);
                if (end.Code == 4429) delay = Math.Max(delay, BusyWaitMs + jitter.Next(3000));
                SetStatus("retrying");
                try { await Task.Delay(delay, stop).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
                wait = Math.Min(wait * 2, LastWaitMs);
            }
            SetStatus("off");
        }

        private async Task<Ending> Connection(CancellationToken stop)
        {
            Wire w = new Wire(CancellationTokenSource.CreateLinkedTokenSource(stop));
            // The relay's own heartbeat keeps the connection alive, so .NET's keep-alive frames are left off. No Origin
            // header is ever set: the relay refuses a PC connection that has one, because only web pages send them.
            w.Socket.Options.KeepAliveInterval = TimeSpan.Zero;
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            Uri uri;
            string token;
            lock (gate)
            {
                uri = new Uri(relay + "/v1/rooms/" + state.Room + "/pc");
                token = state.Token;
            }
            bool connected = false;
            try
            {
                using (CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(w.Stop.Token))
                {
                    limit.CancelAfter(ConnectMs);
                    await w.Socket.ConnectAsync(uri, limit.Token).ConfigureAwait(false);
                    connected = true;
                }
            }
            catch (Exception) { }
            if (!connected)
            {
                w.Stop.Cancel();
                w.Socket.Dispose();
                return new Ending(1006, "unreachable", false);
            }
            lock (gate)
            {
                if (stopped)
                {
                    w.Closing = true;
                }
                else
                {
                    wire = w;
                    ready = false;
                }
            }
            Ending end = null;
            if (!w.Closing)
            {
                w.Push(Outgoing.Control(RemoteMessages.Auth(token)));
                Task pump = Pump(w);
                Task beat = Beat(w);
                Task signIn = AwaitReady(w);
                try { end = await Read(w).ConfigureAwait(false); }
                catch (Exception) { end = null; }
            }
            if (end == null) end = w.Failure ?? new Ending(1006, "lost", false);
            end.Ready = w.Ready;
            Ended(w);
            await Shut(w).ConfigureAwait(false);
            return end;
        }

        // Frames from the relay, whole, until it closes, a fatal error arrives, or this side gives up on it.
        private async Task<Ending> Read(Wire w)
        {
            byte[] buffer = new byte[16384];
            using (MemoryStream message = new MemoryStream())
            {
                while (true)
                {
                    WebSocketReceiveResult result = await w.Socket.ReceiveAsync(new ArraySegment<byte>(buffer), w.Stop.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                        return new Ending(result.CloseStatus.HasValue ? (int)result.CloseStatus.Value : 1005, result.CloseStatusDescription, false);
                    message.Write(buffer, 0, result.Count);
                    if (message.Length > MaxFrame) return new Ending(1009, "too-big", false);
                    if (!result.EndOfMessage) continue;
                    byte[] data = message.ToArray();
                    message.SetLength(0);
                    Ending end = result.MessageType == WebSocketMessageType.Text ? OnText(w, data) : OnBinary(data);
                    if (end != null) return end;
                }
            }
        }

        // The one sender. Each frame's sequence number is taken and the frame sent under the same lock, so a session's
        // frames leave in order and SendAsync never overlaps itself.
        private async Task Pump(Wire w)
        {
            try
            {
                while (true)
                {
                    await w.Signal.WaitAsync(w.Stop.Token).ConfigureAwait(false);
                    Outgoing item = w.Take();
                    if (item == null) continue;
                    await w.Sending.WaitAsync(w.Stop.Token).ConfigureAwait(false);
                    try
                    {
                        if (w.Closing)
                        {
                            Return(item);
                            return;
                        }
                        if (item.Close)
                        {
                            w.Closing = true;
                            using (CancellationTokenSource limit = new CancellationTokenSource(2000))
                                await w.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", limit.Token).ConfigureAwait(false);
                            return;
                        }
                        foreach (KeyValuePair<byte[], WebSocketMessageType> frame in Frames(item))
                            await w.Socket.SendAsync(new ArraySegment<byte>(frame.Key), frame.Value, true, w.Stop.Token).ConfigureAwait(false);
                    }
                    finally
                    {
                        item.Finish();
                        w.Sending.Release();
                    }
                }
            }
            catch (Exception) { w.Fail(new Ending(1006, "send", false)); }
        }

        // The exact 12-character heartbeat every BeatMs, and a reconnect when its pong doesn't come within 10 seconds.
        private async Task Beat(Wire w)
        {
            int pongWait = Math.Min(PongMs, BeatMs / 2);
            try
            {
                while (true)
                {
                    await Task.Delay(BeatMs - pongWait, w.Stop.Token).ConfigureAwait(false);
                    long asked = Now();
                    w.Push(Outgoing.Control(RemoteMessages.Ping));
                    await Task.Delay(pongWait, w.Stop.Token).ConfigureAwait(false);
                    if (Interlocked.Read(ref w.Pong) < asked)
                    {
                        w.Fail(new Ending(1006, "no-pong", false));
                        return;
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task AwaitReady(Wire w)
        {
            try
            {
                await Task.Delay(ReadyMs, w.Stop.Token).ConfigureAwait(false);
                if (!w.Ready) w.Fail(new Ending(4408, "no-ready", false));
            }
            catch (OperationCanceledException) { }
        }

        // Stop sending and close this side: wait for a frame already on its way, send the close, then let go.
        private static async Task Shut(Wire w)
        {
            w.Closing = true;
            w.Signal.Release();
            bool mine = false;
            try { mine = await w.Sending.WaitAsync(1000).ConfigureAwait(false); }
            catch (Exception) { }
            if (mine)
            {
                WebSocketState now = w.Socket.State;
                if (now == WebSocketState.Open || now == WebSocketState.CloseReceived)
                {
                    try
                    {
                        using (CancellationTokenSource limit = new CancellationTokenSource(2000))
                            await w.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", limit.Token).ConfigureAwait(false);
                    }
                    catch (Exception) { }
                }
            }
            w.Stop.Cancel();
            w.Socket.Dispose();
        }

        // A connection ended: every session with it, and anything still queued for a phone waits for that phone's next
        // session. A phone waiting for Allow was closed by the relay (4503); a QR nobody used yet still works on return.
        private void Ended(Wire w)
        {
            List<Note> notes = new List<Note>();
            lock (gate)
            {
                w.Closing = true;
                if (wire == w) wire = null;
                ready = false;
                revoking.Clear();
                admitting.Clear();
                foreach (Phone phone in phones.Values)
                {
                    phone.Session = null;
                    phone.Jobs.Clear();
                    phone.JobHello = false;
                    phone.Online = false;
                    phone.Kicked = false;
                }
                foreach (Outgoing item in w.Drain()) Keep(item);
                if (pairing != null && pairing.Ask != null)
                {
                    pairing.Forget();
                    pairing = null;
                    notes.Add(new Note("ask-ended", "offline"));
                    notes.Add(new Note("qr", null));
                }
                notes.Add(new Note("left", null));
            }
            Raise(notes, null);
        }

        private void NewRoom()
        {
            List<Note> notes = new List<Note>();
            RemoteState copy;
            lock (gate)
            {
                if (stopped) return;
                foreach (RemoteDevice device in state.Devices) Array.Clear(device.Key, 0, device.Key.Length);
                state = RemoteState.Fresh();
                phones.Clear();
                parts.Clear();
                asks.Clear();
                seen.Clear();
                if (pairing != null) pairing.Forget();
                pairing = null;
                copy = state.Copy();
                notes.Add(new Note("new-room", null));
                notes.Add(new Note("qr", null));
            }
            Save(copy);
            Raise(notes, null);
        }

        // ---------- Frames in ----------

        private Ending OnText(Wire w, byte[] data)
        {
            RemoteJson.Map frame = RemoteJson.ParseMap(Encoding.UTF8.GetString(data));
            string t = frame == null ? null : frame.Text("t");
            if (t == null) return null;
            if (t == "pong")
            {
                Interlocked.Exchange(ref w.Pong, Now());
                return null;
            }
            if (t == "error" && !frame.ContainsKey("op"))
            {
                // Fatal (PROTOCOL.md 4.5): act on the code now and close this side. The close frame may lag behind it.
                string code = frame.Text("code") ?? "";
                return new Ending(FatalClose(code), code, false);
            }
            List<Note> notes = new List<Note>();
            RemoteState copy = null;
            lock (gate)
            {
                if (stopped || wire != w) return null;
                switch (t)
                {
                    case "ready": OnReady(w, frame, notes, ref copy); break;
                    case "pending": OnPending(frame, notes, ref copy); break;
                    case "pending-left":
                        if (pairing != null && pairing.Ask != null && pairing.Ask.Pending == frame.Text("id"))
                        {
                            pairing.Forget();
                            pairing = null;
                            notes.Add(new Note("ask-ended", frame.Text("reason") == "timeout" ? "timeout" : "closed"));
                            notes.Add(new Note("qr", null));
                        }
                        break;
                    case "joined":
                    {
                        string id = frame.Text("device");
                        Phone phone = Live(id);
                        if (phone == null)
                        {
                            if (RemoteCrypto.FromB64u(id, 16) != null && revoking.Add(id)) Push(Outgoing.Control(RemoteMessages.Revoke(id)));
                            break;
                        }
                        phone.Online = true;
                        phone.Kicked = false;
                        notes.Add(new Note("joined", id));
                        break;
                    }
                    case "left":
                    {
                        Phone phone = Live(frame.Text("device"));
                        if (phone == null) break;
                        phone.Online = false;
                        phone.Session = null;
                        phone.Jobs.Clear();
                        phone.JobHello = false;
                        phone.Kicked = false;
                        notes.Add(new Note("left", phone.Device.Id));
                        break;
                    }
                    case "ok":
                    {
                        string op = frame.Text("op");
                        string device;
                        if (op == "admit" && frame.Text("pending") != null && admitting.TryGetValue(frame.Text("pending"), out device))
                        {
                            admitting.Remove(frame.Text("pending"));
                            notes.Add(new Note("paired", device));
                        }
                        else if (op == "revoke" && frame.Text("device") != null) revoking.Remove(frame.Text("device"));
                        break;
                    }
                    case "error":
                    {
                        string op = frame.Text("op"), code = frame.Text("code");
                        if (op == "send" && code == "no-device")
                        {
                            // That phone isn't connected: what it hasn't been sent yet waits for its next session.
                            Phone phone = Live(frame.Text("device"));
                            if (phone != null)
                            {
                                phone.Session = null;
                                phone.JobHello = false;
                                phone.Jobs.Clear();
                                phone.Online = false;
                                notes.Add(new Note("left", phone.Device.Id));
                            }
                        }
                        else if (op == "admit")
                        {
                            // The control did nothing (the phone left, or the room is full): drop the device made for it.
                            string pending = frame.Text("pending"), id;
                            if (pending != null && admitting.TryGetValue(pending, out id))
                            {
                                admitting.Remove(pending);
                                RemoteDevice device = state.Find(id);
                                if (device != null)
                                {
                                    Remove(device, notes);
                                    copy = state.Copy();
                                }
                                notes.Add(new Note("pair-failed", code == "too-many-devices" ? "full" : "left"));
                            }
                        }
                        break;
                    }
                }
            }
            Save(copy);
            Raise(notes, null);
            return null;
        }

        // Signed in. The relay's device list wins: ids it has that this PC doesn't know are revoked, and phones this PC
        // has that it doesn't list are removed. claimed on a room used before means it was deleted after 30 days away.
        private void OnReady(Wire w, RemoteJson.Map frame, List<Note> notes, ref RemoteState copy)
        {
            w.Ready = true;
            ready = true;
            HashSet<string> listed = Strings(frame.Items("devices"));
            HashSet<string> online = Strings(frame.Items("online"));
            bool dirty = false;
            if (frame.Is("claimed", true) && state.Used && state.Devices.Count > 0) notes.Add(new Note("room-reset", null));
            if (!state.Used)
            {
                state.Used = true;
                dirty = true;
            }
            foreach (string id in listed)
                if (state.Find(id) == null && revoking.Add(id)) Push(Outgoing.Control(RemoteMessages.Revoke(id)));
            for (int i = state.Devices.Count - 1; i >= 0; i--)
                if (!listed.Contains(state.Devices[i].Id))
                {
                    Remove(state.Devices[i], notes);
                    dirty = true;
                }
            foreach (Phone phone in phones.Values) phone.Online = online.Contains(phone.Device.Id);
            status = "online";
            notes.Add(new Note("status", status));
            if (dirty) copy = state.Copy();
        }

        // A phone presented the open code. Only one try per QR; a request that doesn't open gets a plain no and a new QR
        // goes up quietly; otherwise Capri asks, showing the phone's name and the confirmation code. A paired phone's pairing
        // link was asked for over a live session, so its phone gets in with no Allow (PROTOCOL.md 5.1).
        private void OnPending(RemoteJson.Map frame, List<Note> notes, ref RemoteState copy)
        {
            string id = frame.Text("id");
            if (RemoteCrypto.FromB64u(id, 16) == null) return;
            byte[] nonce = RemoteCrypto.FromB64u(frame.Text("nonce"), 16);
            byte[] env = RemoteCrypto.FromB64u(frame.Text("env"));
            if (pairing == null || pairing.Used || nonce == null || env == null)
            {
                Push(Outgoing.Control(RemoteMessages.Deny(id, null)));
                return;
            }
            pairing.Used = true;
            RemoteChannel channel = new RemoteChannel(RemoteCrypto.PairKeys(pairing.Key, nonce), RemoteCrypto.KindPair);
            RemoteJson.Map request = channel.Open(env, 0, env.Length);
            if (request == null || request.Text("type") != "pair-request")
            {
                Push(Outgoing.Control(RemoteMessages.Deny(id, null)));
                // A new QR goes up for the dialog; nobody here is waiting on a phone's link.
                bool shown = pairing.For == null;
                pairing.Forget();
                pairing = null;
                if (shown) OpenCode();
                notes.Add(new Note("qr", null));
                return;
            }
            double v;
            if (!request.Number("v", out v) || v != RemoteMessages.Protocol)
            {
                Push(Outgoing.Control(RemoteMessages.Deny(id, RemoteCrypto.B64u(channel.Seal(RemoteMessages.PairDeny("bad-request"))))));
                pairing.Forget();
                pairing = null;
                notes.Add(new Note("pair-failed", "version"));
                notes.Add(new Note("qr", null));
                return;
            }
            pairing.Ask = new RemoteAsk
            {
                Pending = id,
                Name = RemoteMessages.CleanName(request.Text("name")),
                App = RemoteMessages.CleanText(request.Text("app"), 64),
                Code = RemoteCrypto.Confirm(pairing.Key, nonce),
                Channel = channel,
            };
            if (pairing.For != null) copy = Admit(notes);
            else notes.Add(new Note("ask", id));
        }

        // PROTOCOL.md 5.1 and 8.3: a paired phone asks for a pairing link for another browser on it, like its home screen
        // app. It gets a fresh one-time code that pairs with no Allow and is never shown on this PC. A QR up here or a phone
        // waiting for Allow comes first; that phone's earlier link nobody used, or a QR that ran out, is replaced.
        private void PairAsk(Phone phone, string id, List<Note> notes)
        {
            if (!RemoteMessages.IdOk(id))
            {
                Push(Outgoing.Message(phone, RemoteMessages.Error("bad-message", null)));
                return;
            }
            if (phone.Device.From != null || phone.Device.Parent != null)
            {
                Push(Outgoing.Message(phone, RemoteMessages.Error("not-allowed", id)));
                return;
            }
            long now = Now();
            if (phone.PairAsked && now - phone.PairAt < 60000)
            {
                Push(Outgoing.Message(phone, RemoteMessages.Error("busy", id)));
                return;
            }
            phone.PairAsked = true;
            phone.PairAt = now;
            bool busy = pairing != null && (pairing.Ask != null || (pairing.For == null && !pairing.Used && Now() < pairing.Expires));
            if (!busy)
            {
                EndPairing(notes, false);
                busy = OpenCode() == null;
            }
            if (busy)
            {
                Push(Outgoing.Message(phone, RemoteMessages.Error("busy", id)));
                return;
            }
            pairing.For = phone.Device.Id;
            // Only now matters: a phone whose session ended before this went asks again.
            Outgoing link = Outgoing.Message(phone, RemoteMessages.PairLink(id, pairing.Url, CodeSeconds));
            link.Stale = true;
            Push(link);
            notes.Add(new Note("link", phone.Device.Id));
        }

        // A phone's envelope, behind its 16-byte device id. A frame that fails any check ends that phone's session and
        // gets it kicked, so it reconnects with a fresh one.
        private Ending OnBinary(byte[] data)
        {
            if (data.Length < RemoteCrypto.IdBytes + RemoteCrypto.MinEnvelope) return null;
            byte[] idBytes = new byte[RemoteCrypto.IdBytes];
            Buffer.BlockCopy(data, 0, idBytes, 0, idBytes.Length);
            string id = RemoteCrypto.B64u(idBytes);
            List<Note> notes = new List<Note>();
            List<string[]> said = new List<string[]>();
            RemoteState copy = null;
            lock (gate)
            {
                if (stopped) return null;
                Phone phone = Live(id);
                if (phone == null)
                {
                    if (revoking.Add(id)) Push(Outgoing.Control(RemoteMessages.Revoke(id)));
                    return null;
                }
                if (phone.Kicked) return null;
                int offset = RemoteCrypto.IdBytes, count = data.Length - RemoteCrypto.IdBytes;
                // The kind byte only picks which keys to try; the tag check then proves it.
                if (data[offset + 1] == RemoteCrypto.KindHello) Hello(phone, data, offset, count, notes);
                else
                {
                    RemoteJson.Map message = phone.Session == null ? null : phone.Session.Open(data, offset, count);
                    if (message == null) Kick(phone, notes);
                    else if (!BrainAsk(phone, message)) OnMessage(phone, message, notes, said, ref copy);
                }
            }
            Save(copy);
            RaiseLive();
            Raise(notes, said);
            RaiseBrainAsks();
            return null;
        }

        // PROTOCOL.md section 7. Any hello ends the session the phone had; a good one starts a new one with fresh nonces
        // from both sides, and hello-ok goes back in the hello context with the phone's nonce in echo.
        private void Hello(Phone phone, byte[] data, int offset, int count, List<Note> notes)
        {
            RemoteChannel hello = new RemoteChannel(RemoteCrypto.HelloKeys(phone.Device.Key, phone.IdBytes), RemoteCrypto.KindHello);
            RemoteJson.Map message = hello.Open(data, offset, count);
            phone.Session = null;
            phone.Jobs.Clear();
            phone.JobHello = false;
            phone.JobStepNext = false;
            phone.Hellos++;
            if (message == null || message.Text("type") != "hello")
            {
                Kick(phone, notes);
                return;
            }
            double v;
            if (!message.Number("v", out v) || v != RemoteMessages.Protocol)
            {
                Push(Outgoing.Hello(phone, hello, RemoteMessages.VersionError(), null, phone.Hellos));
                notes.Add(new Note("version", phone.Device.Id));
                return;
            }
            string echo = message.Text("nonce");
            byte[] phoneNonce = RemoteCrypto.FromB64u(echo, 16);
            if (phoneNonce == null)
            {
                Kick(phone, notes);
                return;
            }
            byte[] pcNonce = RemoteCrypto.Random(16);
            RemoteChannel session = new RemoteChannel(RemoteCrypto.SessionKeys(phone.Device.Key, phoneNonce, pcNonce), RemoteCrypto.KindSession);
            phone.JobHello = true;
            Push(Outgoing.Hello(phone, hello, RemoteMessages.HelloOk(RemoteCrypto.B64u(pcNonce), echo, pcName, app, Offered()), session, phone.Hellos));
            phone.Online = true;
            notes.Add(new Note("session", phone.Device.Id));
        }

        private void OnMessage(Phone phone, RemoteJson.Map message, List<Note> notes, List<string[]> said, ref RemoteState copy)
        {
            switch (message.Text("type"))
            {
                case "chat.send":
                {
                    string id = message.Text("id"), text = message.Text("text");
                    if (!RemoteMessages.IdOk(id))
                    {
                        Push(Outgoing.Message(phone, RemoteMessages.Error("bad-message", null)));
                        return;
                    }
                    if (text == null || text.Length == 0 || text.Length > 8000)
                    {
                        Push(Outgoing.Message(phone, RemoteMessages.Error("bad-message", id)));
                        return;
                    }
                    long now = Now();
                    Prune(now);
                    string key = phone.Device.Id + " " + id;
                    // A phone may send again after a reconnect: an id seen in the last 10 minutes is already answered.
                    long when;
                    if (seen.TryGetValue(key, out when) && now - when <= SeenMs) return;
                    seen[key] = now;
                    Outgoing receipt = Outgoing.Message(phone, RemoteMessages.Typing(true, id));
                    receipt.Stale = true;
                    Push(receipt);
                    asks[key] = new Ask { Phone = phone, Re = id, Opened = now, Sent = now };
                    Arm(1000);
                    said.Add(new[] { phone.Device.Id, id, text });
                    return;
                }
                case "ping":
                {
                    double n;
                    if (message.Number("n", out n) && n == Math.Floor(n) && Math.Abs(n) <= 9007199254740991.0)
                    {
                        Outgoing pong = Outgoing.Message(phone, RemoteMessages.PongFor((long)n));
                        pong.Stale = true;
                        Push(pong);
                    }
                    return;
                }
                case "revoke":
                {
                    // The phone asked to be forgotten: delete it here, then on the relay.
                    string id = phone.Device.Id;
                    Remove(phone.Device, notes);
                    if (revoking.Add(id)) Push(Outgoing.Control(RemoteMessages.Revoke(id)));
                    copy = state.Copy();
                    return;
                }
                case "pair.ask":
                    PairAsk(phone, message.Text("id"), notes);
                    return;
                default:
                    // Screen and call messages go to their own parts, after this frame (RaiseLive).
                    if (IsLive(message.Text("type"))) liveIn.Add(new KeyValuePair<string, RemoteJson.Map>(phone.Device.Id, message));
                    // chat.typing from the phone, pong, error, the names saved for later phases, and anything unknown.
                    return;
            }
        }

        private void Kick(Phone phone, List<Note> notes)
        {
            phone.Session = null;
            phone.JobHello = false;
            phone.Jobs.Clear();
            phone.Hellos++;
            if (phone.Kicked) return;
            phone.Kicked = true;
            Push(Outgoing.Control(RemoteMessages.Kick(phone.Device.Id)));
            notes.Add(new Note("kicked", phone.Device.Id));
        }

        // ---------- Frames out ----------

        // Under gate. Without a connection, a phone's message waits for its next session.
        private void Push(Outgoing item)
        {
            if (wire != null && !wire.Closing) wire.Push(item);
            else Keep(item);
        }

        // Called by the pump. A phone's message is sealed now, with the session it has now; one with no session yet
        // waits for it. hello-ok starts the new session, and whatever was waiting for one follows straight after.
        private List<KeyValuePair<byte[], WebSocketMessageType>> Frames(Outgoing item)
        {
            List<KeyValuePair<byte[], WebSocketMessageType>> frames = new List<KeyValuePair<byte[], WebSocketMessageType>>();
            if (item.Text != null)
            {
                frames.Add(new KeyValuePair<byte[], WebSocketMessageType>(Encoding.UTF8.GetBytes(item.Text), WebSocketMessageType.Text));
                return frames;
            }
            lock (gate)
            {
                Phone phone = item.Phone;
                if ((item.Channel != null || item.Job) && item.Round != phone.Hellos || item.Job && !phone.Online) return frames;
                RemoteChannel channel = item.Channel ?? phone.Session;
                if (channel == null)
                {
                    Keep(item);
                    return frames;
                }
                Seal(phone, channel, item, frames);
                if (item.Then != null)
                {
                    phone.Session = item.Then;
                    phone.JobHello = false;
                    int now = Environment.TickCount;
                    foreach (Outgoing kept in phone.Kept)
                        if (unchecked(now - kept.At) < KeepMs) Seal(phone, item.Then, kept, frames);
                    phone.Kept.Clear();
                }
            }
            return frames;
        }

        private static void Seal(Phone phone, RemoteChannel channel, Outgoing item, List<KeyValuePair<byte[], WebSocketMessageType>> frames)
        {
            foreach (string json in item.Messages())
                frames.Add(new KeyValuePair<byte[], WebSocketMessageType>(RemoteCrypto.Join(phone.IdBytes, channel.Seal(json)), WebSocketMessageType.Binary));
        }

        // Under gate. Holds a phone's message for its next session. Typing and pongs only mean something now, so they're
        // dropped; reply parts join up and a tool's later state replaces its earlier one, so a phone coming back gets a few
        // frames, not a backlog.
        private void Keep(Outgoing item)
        {
            Phone phone = item.Phone;
            if (phone == null || item.Channel != null || item.Stale || phone.Removed)
            {
                item.Finish();
                return;
            }
            List<Outgoing> kept = phone.Kept;
            for (int i = kept.Count - 1; i >= 0; i--)
            {
                if (item.ReplyId != null && kept[i].ReplyId == item.ReplyId && !kept[i].Done)
                {
                    kept[i].Words.Append(item.Words.ToString());
                    kept[i].Done = item.Done;
                    return;
                }
                if (item.ToolId != null && kept[i].ToolId == item.ToolId)
                {
                    kept[i] = item;
                    return;
                }
            }
            kept.Add(item);
            if (kept.Count > KeepCount) kept.RemoveAt(0);
        }

        // The pump had taken this when its connection closed.
        private void Return(Outgoing item)
        {
            if (item.Text != null || item.Close) return;
            lock (gate) Keep(item);
        }

        // Under gate. Turns gathered reply parts into frames, for one phone or all of them.
        private void FlushParts(Phone only)
        {
            for (int i = 0; i < parts.Count; i++)
            {
                Part part = parts[i];
                if (only != null && part.Phone != only) continue;
                parts.RemoveAt(i--);
                Push(Outgoing.Reply(part.Phone, part.Id, part.Re, part.Words.ToString(), part.Done));
            }
        }

        // Under gate. Something went to the phone about this chat.send, so its typing receipt doesn't need refreshing yet.
        private void Touch(Phone phone, string re)
        {
            Ask ask;
            if (re != null && asks.TryGetValue(phone.Device.Id + " " + re, out ask)) ask.Sent = Now();
        }

        // Every 100 ms while reply parts are gathering, and every second while a chat.send waits: parts go out, and a
        // phone still waiting gets chat.typing again every 10 seconds (its dots stop 20 seconds after the last one).
        // A timer runs this on a pool thread, where anything thrown would end her: a tick that fails is dropped, and the
        // next reply part or send arms another.
        private void Tick(object unused)
        {
            try { Ticked(); }
            catch (Exception) { }
        }

        private void Ticked()
        {
            lock (gate)
            {
                ticking = false;
                if (stopped) return;
                FlushParts(null);
                long now = Now();
                bool jobs = FlushJobs(now);
                List<string> done = new List<string>();
                foreach (KeyValuePair<string, Ask> pair in asks)
                {
                    Ask ask = pair.Value;
                    if (now - ask.Opened > AskMs || ask.Phone.Removed) done.Add(pair.Key);
                    else if (now - ask.Sent >= TypingMs && ask.Phone.Session != null)
                    {
                        Outgoing typing = Outgoing.Message(ask.Phone, RemoteMessages.Typing(true, ask.Re));
                        typing.Stale = true;
                        Push(typing);
                        ask.Sent = now;
                    }
                }
                foreach (string key in done) asks.Remove(key);
                Arm(parts.Count > 0 ? BatchMs : jobs ? 200 : 1000);
            }
        }

        // Under gate.
        private void Arm(int due)
        {
            if (stopped || ticker == null || (parts.Count == 0 && asks.Count == 0 && !JobsWaiting())) return;
            long at = Now() + due;
            if (ticking && tickDue <= at) return;
            ticking = true;
            tickDue = at;
            try { ticker.Change(due, Timeout.Infinite); }
            catch (ObjectDisposedException) { }
        }

        // ---------- Small things ----------

        // Under gate.
        private string OpenCode()
        {
            if (!ready || wire == null) return null;
            pairing = new Pairing(RemoteCrypto.Random(16), RemoteCrypto.Random(32), Now() + CodeSeconds * 1000L, state.Room);
            Push(Outgoing.Control(RemoteMessages.Code(RemoteCrypto.Hash(pairing.Code), CodeSeconds)));
            return pairing.Url;
        }

        // Under gate. cancel: the dialog is closing, so the relay forgets the code too. A phone still waiting always gets a
        // sealed no.
        private void EndPairing(List<Note> notes, bool cancel)
        {
            if (pairing == null) return;
            if (pairing.Ask != null)
                Push(Outgoing.Control(RemoteMessages.Deny(pairing.Ask.Pending, RemoteCrypto.B64u(pairing.Ask.Channel.Seal(RemoteMessages.PairDeny("denied"))))));
            else if (cancel && !pairing.Used) Push(Outgoing.Control(RemoteMessages.CodeCancel()));
            pairing.Forget();
            pairing = null;
            if (notes != null) notes.Add(new Note("qr", null));
        }

        // Under gate. A pairing link the removed phone asked for goes with it.
        private void Remove(RemoteDevice device, List<Note> notes)
        {
            if (pairing != null && pairing.For == device.Id) EndPairing(notes, true);
            state.Devices.Remove(device);
            Phone phone;
            if (phones.TryGetValue(device.Id, out phone))
            {
                phone.Removed = true;
                phone.Online = false;
                phone.Kept.Clear();
                phones.Remove(device.Id);
                parts.RemoveAll(delegate (Part part) { return part.Phone == phone; });
            }
            notes.Add(new Note("removed", device.Id));
        }

        // Under gate.
        private Phone Live(string id)
        {
            Phone phone;
            return id != null && phones.TryGetValue(id, out phone) && !phone.Removed ? phone : null;
        }

        private void Prune(long now)
        {
            if (seen.Count < 64) return;
            List<string> old = new List<string>();
            foreach (KeyValuePair<string, long> pair in seen) if (now - pair.Value > SeenMs) old.Add(pair.Key);
            foreach (string key in old) seen.Remove(key);
        }

        private static HashSet<string> Strings(List<object> items)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
            if (items != null) foreach (object item in items) if (item is string) set.Add((string)item);
            return set;
        }

        // PROTOCOL.md 4.5, for a fatal error frame that arrives before its close.
        private static int FatalClose(string code)
        {
            switch (code)
            {
                case "too-big": return 1009;
                case "internal": return 1011;
                case "bad-request": return 4400;
                case "bad-token": return 4401;
                case "no-room": return 4404;
                case "timeout": return 4408;
                case "replaced": return 4409;
                case "busy":
                case "rate-limited": return 4429;
                default: return 1011;
            }
        }

        private bool Stopped { get { lock (gate) return stopped; } }

        private long Now() { return clock.ElapsedMilliseconds; }

        private void SetStatus(string now)
        {
            lock (gate)
            {
                if (stopped && now != "off") return;
                if (status == now) return;
                status = now;
            }
            Raise(new List<Note> { new Note("status", now) }, null);
        }

        private void Save(RemoteState copy)
        {
            if (copy == null || save == null || Stopped) return;
            try { save(copy); }
            catch (Exception) { }
        }

        private void Raise(List<Note> notes, List<string[]> said)
        {
            if (Stopped && (notes == null || notes.Count == 0 || notes[0].What != "status")) return;
            Action<string, string> happened = Happened;
            if (happened != null && notes != null)
                foreach (Note note in notes)
                {
                    try { happened(note.What, note.About); }
                    catch (Exception) { }
                }
            Action<string, string, string> heard = Said;
            if (heard != null && said != null)
                foreach (string[] item in said)
                {
                    try { heard(item[0], item[1], item[2]); }
                    catch (Exception) { }
                }
        }

        private struct Note
        {
            internal readonly string What, About;

            internal Note(string what, string about)
            {
                What = what;
                About = about;
            }
        }

        // How a connection ended: a close code (1006 when there was none) and its reason.
        private sealed class Ending
        {
            internal readonly int Code;
            internal readonly string Reason;
            internal bool Ready;

            internal Ending(int code, string reason, bool wasReady)
            {
                Code = code;
                Reason = reason;
                Ready = wasReady;
            }
        }

        private sealed class Phone
        {
            internal readonly RemoteDevice Device;
            internal readonly byte[] IdBytes;
            internal readonly List<Outgoing> Kept = new List<Outgoing>();
            internal readonly List<KeyValuePair<string, string>> Jobs = new List<KeyValuePair<string, string>>();
            internal long JobAt;
            internal int JobBusy;
            internal bool JobHello;
            internal bool JobStepNext;
            internal RemoteChannel Session;
            internal bool Online, Kicked, Removed;
            internal bool PairAsked;
            internal long PairAt;
            internal int Hellos;

            internal Phone(RemoteDevice device)
            {
                Device = device;
                IdBytes = device.IdBytes;
            }
        }

        // A chat.send still waiting for its answer, so its typing receipt can be kept fresh.
        private sealed class Ask
        {
            internal Phone Phone;
            internal string Re;
            internal long Opened, Sent;
        }

        // Reply text gathering for its next frame.
        private sealed class Part
        {
            internal Phone Phone;
            internal string Id, Re;
            internal readonly StringBuilder Words = new StringBuilder();
            internal bool Done;
        }

        // One open QR: its one-time code and pairing key, which are wiped once pairing ends, and the phone it's asking for.
        // For is the paired phone that asked for it as a pairing link, or null for the dialog's QR.
        private sealed class Pairing
        {
            internal readonly byte[] Code, Key;
            internal readonly long Expires;
            internal readonly string Url;
            internal RemoteAsk Ask;
            internal bool Used;
            internal string For;

            internal Pairing(byte[] code, byte[] key, long expires, string room)
            {
                Code = code;
                Key = key;
                Expires = expires;
                Url = RemoteCrypto.QrUrl(room, RemoteCrypto.B64u(code), RemoteCrypto.B64u(key));
            }

            internal void Forget()
            {
                Array.Clear(Code, 0, Code.Length);
                Array.Clear(Key, 0, Key.Length);
                Ask = null;
            }
        }

        // One frame to send: a relay control (Text), or a message for a phone, sealed when it leaves.
        private sealed class Outgoing
        {
            internal string Text, Json, ReplyId, Re, ToolId;
            internal Phone Phone;
            internal RemoteChannel Channel, Then;
            internal StringBuilder Words;
            internal bool Done, Stale, Close, Job;
            internal int Round;
            internal readonly int At = Environment.TickCount;
            // A live item's messages, sealed back to back, and what runs once they've left or been dropped (SendLive).
            internal List<string> Many;
            internal Action Left;

            internal void Finish()
            {
                Action left = Interlocked.Exchange(ref Left, null);
                if (left == null) return;
                try { left(); }
                catch (Exception) { }
            }

            internal static Outgoing Control(string text) { return new Outgoing { Text = text }; }

            internal static Outgoing Closing() { return new Outgoing { Close = true }; }

            internal static Outgoing Message(Phone phone, string json) { return new Outgoing { Phone = phone, Json = json }; }

            // hello-ok or a version error, in the hello context; then starts the new session once it has gone.
            internal static Outgoing Hello(Phone phone, RemoteChannel hello, string json, RemoteChannel then, int count) { return new Outgoing { Phone = phone, Channel = hello, Json = json, Then = then, Round = count }; }

            internal static Outgoing Reply(Phone phone, string id, string re, string text, bool done)
            {
                Outgoing item = new Outgoing { Phone = phone, ReplyId = id, Re = re, Done = done, Words = new StringBuilder(text) };
                return item;
            }

            // A long reply part is split so every sealed message stays well under the 60,000-byte limit, never between
            // the two halves of a surrogate pair.
            internal IEnumerable<string> Messages()
            {
                if (Many != null)
                {
                    foreach (string json in Many) yield return json;
                    yield break;
                }
                if (ReplyId == null)
                {
                    yield return Json;
                    yield break;
                }
                string text = Words.ToString();
                int at = 0;
                do
                {
                    int take = Math.Min(PartChars, text.Length - at);
                    if (take < text.Length - at && take > 0 && char.IsHighSurrogate(text[at + take - 1])) take--;
                    bool last = at + take >= text.Length;
                    yield return RemoteMessages.Reply(ReplyId, Re, text.Substring(at, take), last && Done);
                    at += take;
                } while (at < text.Length);
            }
        }

        // One connection to the relay and its queue.
        private sealed class Wire
        {
            internal readonly ClientWebSocket Socket = new ClientWebSocket();
            internal readonly CancellationTokenSource Stop;
            internal readonly SemaphoreSlim Signal = new SemaphoreSlim(0), Sending = new SemaphoreSlim(1, 1);
            private readonly Queue<Outgoing> queue = new Queue<Outgoing>();
            internal long Pong;
            internal volatile bool Ready, Closing;
            internal Ending Failure;

            internal Wire(CancellationTokenSource stop)
            {
                Stop = stop;
                Pong = -1;
            }

            internal void Push(Outgoing item)
            {
                lock (queue) queue.Enqueue(item);
                Signal.Release();
            }

            internal Outgoing Take()
            {
                lock (queue) return queue.Count > 0 ? queue.Dequeue() : null;
            }

            internal List<Outgoing> Drain()
            {
                lock (queue)
                {
                    List<Outgoing> left = new List<Outgoing>(queue);
                    queue.Clear();
                    return left;
                }
            }

            internal void Fail(Ending why)
            {
                lock (queue) if (Failure == null) Failure = why;
                try { Stop.Cancel(); }
                catch (ObjectDisposedException) { }
            }
        }
    }
}
