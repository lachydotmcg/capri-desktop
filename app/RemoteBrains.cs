using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Capri Remote and her brains, her side (remote/PROTOCOL.md section 8.1). A paired phone sees which brain she uses, picks
    // another rung and sends a brain's key. The key goes in exactly as if it was typed in her Mainframe's key box: the same
    // ConnectText, so the same checks, the same notes and the same DPAPI storage, and BRAIN shows it all as usual. Keys only
    // come in: the phone hears whether a brain has one, never the key. A saved key gets a short notice on the PC. The
    // protocol's share, which app/RemoteTests runs too, is app/RemoteBrainKeys.cs.
    internal sealed partial class Companion
    {
        // The phone's key being checked: at most one at a time, like the key box.
        private sealed class PhoneKey
        {
            internal string Device, Id, Key;
            internal int Tier, Listing;
            internal long Started;
        }

        private PhoneKey phoneKey;
        // Phones that asked for her brains, which hear when they change, and what they last heard.
        private readonly HashSet<string> brainWatchers = new HashSet<string>(StringComparer.Ordinal);
        private string brainSent;
        private System.Windows.Forms.Timer brainTimer;
        private readonly Stopwatch brainClock = Stopwatch.StartNew();
        // Where brain messages go: her link, or the smoke test's tap. The smoke test hears the notice too.
        private Action<string, string> brainOut;
        private Action<string> brainNoticed;

        private void RemoteBrainsOn(RemoteLink link)
        {
            link.BrainAsked += RemoteBrainAsked;
            link.Happened += RemoteBrainsHappened;
            brainOut = link.SendBrain;
        }

        private void RemoteBrainsOff(RemoteLink link)
        {
            link.BrainAsked -= RemoteBrainAsked;
            link.Happened -= RemoteBrainsHappened;
            brainOut = null;
            phoneKey = null;
            brainWatchers.Clear();
            brainSent = null;
            if (brainTimer != null) brainTimer.Stop();
        }

        // On the link's read thread: the ask moves to her thread, where her brain lives.
        private void RemoteBrainAsked(string device, RemoteJson.Map message) { Post(delegate { RemoteBrainAsk(device, message); }); }

        private void RemoteBrainsHappened(string what, string device)
        {
            // A phone that left, or started a new session, asks again when it wants her brains.
            if (what == "left" || what == "removed" || what == "kicked" || what == "session") Post(delegate { if (device != null) brainWatchers.Remove(device); });
        }

        private void BrainSend(string device, string json)
        {
            Action<string, string> send = brainOut;
            if (send != null) send(device, json);
        }

        private void RemoteBrainAsk(string device, RemoteJson.Map message)
        {
            string re;
            RemoteBrainKeys.Ask ask = RemoteBrainKeys.Read(device, message, out re);
            if (ask == null)
            {
                BrainSend(device, RemoteMessages.Error("bad-message", re));
                return;
            }
            brainWatchers.Add(device);
            WatchBrains();
            if (ask.Type == "brain.get")
            {
                BrainSend(device, BrainState());
                return;
            }
            if (ask.Type == "brain.use")
            {
                // The same as clicking that rung under BRAIN.
                if (ask.Tier != preferences.Tier) ChooseTier(ask.Tier);
                else BrainSend(device, BrainState());
                return;
            }
            if (phoneKey != null)
            {
                BrainSend(device, RemoteBrainKeys.Note(ask.Id, "failed", RemoteBrainKeys.StillChecking, ask.Key));
                return;
            }
            string key = Cloud.IsCode(ask.Key) ? ask.Key.ToUpperInvariant() : ask.Key;
            string kind = Cloud.KindOf(key);
            if (Plans.Is(kind))
            {
                BrainSend(device, RemoteBrainKeys.Note(ask.Id, "failed", RemoteBrainKeys.PlanHere, key));
                return;
            }
            // Her Mainframe offers to move a paid key typed under MEDIUM INTELLIGENCE; the phone hears its words and can send
            // it to MAX itself.
            if (kind != null && ask.Tier == 1 && !Cloud.MediumKind(kind))
            {
                BrainSend(device, RemoteBrainKeys.Note(ask.Id, "failed", RemoteBrainKeys.PaidNote, key));
                return;
            }
            phoneKey = new PhoneKey { Device = device, Id = ask.Id, Key = key, Tier = ask.Tier, Listing = cloudListing + 1, Started = brainClock.ElapsedMilliseconds };
            ConnectText(key, ask.Tier);
            PhoneKey still = phoneKey;
            if (still != null && still.Id == ask.Id && still.Device == device && still.Listing == cloudListing) BrainSend(device, RemoteBrainKeys.Note(ask.Id, "checking", "Checking key...", key));
        }

        // TextProblem's hook: a problem with the phone's key is the phone's note too, in the same words. Another change first
        // (a key typed on the PC, a rung clicked) means the phone's key was dropped.
        private void RemoteKeyProblem(string problem, bool connecting)
        {
            PhoneKey asked = phoneKey;
            if (asked == null || !connecting) return;
            phoneKey = null;
            BrainSend(asked.Device, RemoteBrainKeys.Note(asked.Id, "failed", asked.Listing == cloudListing ? problem : RemoteBrainKeys.TakenOver, asked.Key));
        }

        // ShowTiers' hook: the phone's key saved, the same as the key box saves it, or dropped for another change; and phones
        // that asked hear her brains once BRAIN has caught up.
        private void RemoteBrainsShown()
        {
            PhoneKey asked = phoneKey;
            if (asked != null)
            {
                if (asked.Listing != cloudListing)
                {
                    phoneKey = null;
                    BrainSend(asked.Device, RemoteBrainKeys.Note(asked.Id, "failed", RemoteBrainKeys.TakenOver, asked.Key));
                }
                else if (preferences.Tier == asked.Tier && Voice.Unprotect(asked.Tier == 1 ? preferences.NvidiaKey : preferences.PaidKey) == asked.Key)
                {
                    phoneKey = null;
                    BrainSend(asked.Device, RemoteBrainKeys.Note(asked.Id, "saved", "Key saved. She uses it now.", asked.Key));
                    RemoteKeyNotice(asked);
                }
            }
            if (brainWatchers.Count > 0) Post(delegate { SendBrainState(false); });
        }

        // The PC's notice: which phone set which brain's key, in the REMOTE sheet's note and a tray balloon.
        private void RemoteKeyNotice(PhoneKey asked)
        {
            string phone = "A phone";
            RemoteLink link = remoteLink;
            if (link != null) foreach (RemoteDevice device in link.Devices()) if (device.Id == asked.Device) phone = device.Name;
            string words = phone + " set her " + RemoteBrainKeys.Shown[asked.Tier] + " key";
            remoteNote = words;
            mainframe.RemoteChanged();
            Action<string> noticed = brainNoticed;
            if (noticed != null) { noticed(words); return; }
            if (testing) return;
            balloonPage = null;
            balloonUpdate = false;
            balloonMainframe = true;
            tray.ShowBalloonTip(8000, "Capri Remote", words + ". Click to see BRAIN in her Mainframe.", ToolTipIcon.Info);
        }

        // Her brains as a phone sees them: the rung in use, what BRAIN shows for it, and which rungs have a key.
        private string BrainState()
        {
            return RemoteBrainKeys.State(preferences.Tier, RemoteOut(mainframe.BrainShown), RemoteOut(mainframe.BrainNoted), !string.IsNullOrEmpty(preferences.NvidiaKey), !string.IsNullOrEmpty(preferences.PaidKey));
        }

        private void SendBrainState(bool always)
        {
            if (brainWatchers.Count == 0) return;
            string state = BrainState();
            if (!always && state == brainSent) return;
            brainSent = state;
            foreach (string device in new List<string>(brainWatchers)) BrainSend(device, state);
        }

        // While phones watch or a key is checking: BRAIN's changes go out (a model found, a note), and a key the PC dropped
        // or never finished gets its answer.
        private void WatchBrains()
        {
            if (brainTimer == null)
            {
                brainTimer = new System.Windows.Forms.Timer { Interval = 1500 };
                brainTimer.Tick += delegate { BrainTick(); };
            }
            if (!brainTimer.Enabled) brainTimer.Start();
        }

        private void BrainTick()
        {
            PhoneKey asked = phoneKey;
            if (asked != null && (asked.Listing != cloudListing || brainClock.ElapsedMilliseconds - asked.Started > 90000))
            {
                phoneKey = null;
                BrainSend(asked.Device, RemoteBrainKeys.Note(asked.Id, "failed", asked.Listing != cloudListing ? RemoteBrainKeys.TakenOver : RemoteBrainKeys.TooSlow, asked.Key));
            }
            if (brainWatchers.Count == 0 && phoneKey == null) { brainTimer.Stop(); return; }
            SendBrainState(false);
        }

        // ---------- Smoke ----------

        // A phone's brain asks through her real key path, with stand-in providers: what the phone sees, a key checked, saved
        // with DPAPI and used exactly as the key box does it, with notes and the notice; a key that isn't one, a paid key under
        // MEDIUM, a rejected key, one key at a time, a rung picked, malformed asks, and no key ever going back.
        private void SmokeRemoteBrains(List<string> checks)
        {
            Preferences wasPreferences = preferences;
            Cloud wasRemote = brain.Remote;
            Engine wasLocal = brain.Local;
            string wasModel = brain.Model, wasGoogle = Brain.Google, wasNote = remoteNote;
            List<string> wasModels = cloudModels;
            Func<string, string> wasThink = Think;
            bool wasFinding = finding;
            Action<string, string> wasOut = brainOut;
            Action<string> wasNoticed = brainNoticed;
            List<System.Net.Sockets.TcpListener> servers = new List<System.Net.Sockets.TcpListener>();
            List<ManualResetEvent> gates = new List<ManualResetEvent>();
            List<string> sent = new List<string>(), notices = new List<string>();
            string catalog = "{\"data\":[{\"id\":\"gemini-90-flash\"},{\"id\":\"gemini-90-pro\"}]}";
            Func<string[], List<string>, ManualResetEvent, string> serve = delegate(string[] replies, List<string> asked, ManualResetEvent release)
            {
                System.Net.Sockets.TcpListener server;
                string address = FakeServer(replies, asked, out server, release: release);
                servers.Add(server);
                return address;
            };
            Action settle = delegate
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (waited.ElapsedMilliseconds < 400) { Application.DoEvents(); Thread.Sleep(5); }
            };
            Func<string> take = delegate
            {
                lock (sent)
                {
                    string all = string.Join(" | ", sent.ToArray());
                    sent.Clear();
                    return all;
                }
            };
            Func<string, string, RemoteJson.Map> last = delegate(string got, string type)
            {
                RemoteJson.Map found = null;
                foreach (string line in got.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries))
                {
                    RemoteJson.Map map = RemoteJson.ParseMap(line.Substring(line.IndexOf(' ') + 1));
                    if (map != null && map.Text("type") == type) found = map;
                }
                return found;
            };
            Action<string> ask = delegate(string json) { RemoteBrainAsk("phoneA", RemoteJson.ParseMap(json)); };
            try
            {
                brainOut = delegate(string device, string json) { lock (sent) sent.Add(device + " " + json); };
                brainNoticed = delegate(string words) { lock (notices) notices.Add(words); };
                Think = delegate { return "Connected."; };
                finding = true;
                ++asking;
                CloseTalk();
                preferences = new Preferences { Tiered = true, Silent = true, Tier = 2, PaidKey = Voice.Protect("sk-proj-phone-old") };
                brain.Remote = new Cloud("sk-proj-phone-old", "gpt-90-mini");
                brain.Local = null;
                brain.Model = "gpt-90-mini";
                cloudModels = new List<string> { "gpt-90-mini" };
                Keep(brain.Remote);
                ShowTiers();
                mainframe.ShowCloud(brain.Remote, cloudModels);

                // What a phone sees: the rung in use, BRAIN's words for it, and only whether each rung has a key.
                ask("{\"type\":\"brain.get\"}");
                string got = take();
                RemoteJson.Map state = last(got, "brain.state");
                if (state == null || state.Text("use") != "max" || !got.Contains("{\"id\":\"medium\",\"key\":false}") || !got.Contains("{\"id\":\"max\",\"key\":true}") || state.Text("name") != mainframe.BrainShown || got.Contains("sk-proj-phone-old")) throw new Exception("A phone's brain.get got " + got + ".");

                // A Gemini key for MAX INTELLIGENCE: checked, saved and used exactly as the key box does it.
                List<string> heard = new List<string>();
                Brain.Google = serve(new[] { catalog }, heard, null);
                ask("{\"type\":\"brain.key\",\"id\":\"k1\",\"brain\":\"max\",\"key\":\" AIza-phone-new-0001 \"}");
                got = take();
                if (got != "phoneA {\"type\":\"brain.note\",\"re\":\"k1\",\"state\":\"checking\",\"text\":\"Checking key...\"}") throw new Exception("A phone's key started as " + got + ".");
                WaitVoice(delegate { return phoneKey == null; }, "A phone's key was never settled.");
                settle();
                got = take();
                RemoteJson.Map note = last(got, "brain.note");
                state = last(got, "brain.state");
                if (note == null || note.Text("re") != "k1" || note.Text("state") != "saved") throw new Exception("A phone's good key went as " + got + ".");
                if (Voice.Unprotect(preferences.PaidKey) != "AIza-phone-new-0001" || preferences.PaidKey.Contains("AIza") || preferences.Tier != 2 || brain.Remote == null || brain.Remote.Kind != "google" || brain.Model != "gemini-90-flash") throw new Exception("A phone's key wasn't saved and used as the key box does: " + mainframe.Status);
                if (!heard[0].Contains("Bearer AIza-phone-new-0001")) throw new Exception("A phone's key wasn't the one checked.");
                if (notices.Count != 1 || notices[0] != "A phone set her MAX INTELLIGENCE key" || remoteNote != notices[0]) throw new Exception("A phone's saved key gave the notice " + string.Join(", ", notices.ToArray()) + ".");
                if (state == null || state.Text("use") != "max" || state.Text("name") != mainframe.BrainShown || !state.Text("name").StartsWith("Gemini", StringComparison.Ordinal)) throw new Exception("Phones didn't hear her new brain: " + got + ".");

                // Not a key: her Mainframe's own words, and she keeps her brain.
                ask("{\"type\":\"brain.key\",\"id\":\"k2\",\"brain\":\"max\",\"key\":\"definitely-not-a-key\"}");
                note = last(take(), "brain.note");
                if (note == null || note.Text("state") != "failed" || note.Text("text") != mainframe.BrainNoted || !note.Text("text").StartsWith("Still using your Google AI key.", StringComparison.Ordinal) || Voice.Unprotect(preferences.PaidKey) != "AIza-phone-new-0001") throw new Exception("A phone's not-a-key got " + (note == null ? "nothing" : note.Text("text")) + ".");

                // A paid key under MEDIUM INTELLIGENCE: the Mainframe's words for it, and nothing saved.
                ask("{\"type\":\"brain.key\",\"id\":\"k3\",\"brain\":\"medium\",\"key\":\"sk-proj-phone-paid\"}");
                note = last(take(), "brain.note");
                if (note == null || note.Text("state") != "failed" || note.Text("text") != mainframe.PaidWords || preferences.NvidiaKey != null) throw new Exception("A paid key under MEDIUM from a phone got " + (note == null ? "nothing" : note.Text("text")) + ".");

                // A rejected key: the Mainframe's words, and the old key stays.
                heard = new List<string>();
                Brain.Google = serve(new[] { "!401 {\"error\":{\"message\":\"Key rejected\"}}" }, heard, null);
                ask("{\"type\":\"brain.key\",\"id\":\"k4\",\"brain\":\"max\",\"key\":\"AIza-phone-rejected-0002\"}");
                WaitVoice(delegate { return phoneKey == null; }, "A phone's rejected key was never settled.");
                note = last(take(), "brain.note");
                if (note == null || note.Text("re") != "k4" || note.Text("state") != "failed" || note.Text("text") != mainframe.BrainNoted || Voice.Unprotect(preferences.PaidKey) != "AIza-phone-new-0001") throw new Exception("A phone's rejected key got " + (note == null ? "nothing" : note.Text("text")) + ".");

                // One key at a time.
                ManualResetEvent hold = new ManualResetEvent(false);
                gates.Add(hold);
                heard = new List<string>();
                Brain.Google = serve(new[] { catalog }, heard, hold);
                ask("{\"type\":\"brain.key\",\"id\":\"k5\",\"brain\":\"max\",\"key\":\"AIza-phone-slow-0003\"}");
                ask("{\"type\":\"brain.key\",\"id\":\"k6\",\"brain\":\"max\",\"key\":\"AIza-phone-second-0004\"}");
                got = take();
                if (!got.Contains("\"re\":\"k5\",\"state\":\"checking\"") || !got.Contains("\"re\":\"k6\",\"state\":\"failed\",\"text\":\"" + RemoteBrainKeys.StillChecking + "\"")) throw new Exception("Two keys at once went as " + got + ".");
                hold.Set();
                WaitVoice(delegate { return phoneKey == null; }, "The slow key was never settled.");
                note = last(take(), "brain.note");
                if (note == null || note.Text("re") != "k5" || note.Text("state") != "saved" || Voice.Unprotect(preferences.PaidKey) != "AIza-phone-slow-0003") throw new Exception("The slow key went as " + (note == null ? "nothing" : note.Text("text")) + ".");

                // Picking a rung is the same as clicking it, and malformed asks get bad-message with their id when it was good.
                ++asking;
                CloseTalk();
                ask("{\"type\":\"brain.use\",\"brain\":\"medium\"}");
                if (preferences.Tier != 1) throw new Exception("A phone's brain.use didn't pick MEDIUM INTELLIGENCE.");
                take();
                ask("{\"type\":\"brain.key\",\"id\":\"k7\",\"brain\":\"max\",\"key\":\"AIza-bad\\u0007key\"}");
                ask("{\"type\":\"brain.use\",\"brain\":\"turbo\"}");
                ask("{\"type\":\"brain.key\",\"id\":\"k8\",\"brain\":\"normal\",\"key\":\"AIza-phone-0005\"}");
                got = take();
                if (got != "phoneA {\"type\":\"error\",\"code\":\"bad-message\",\"re\":\"k7\"} | phoneA {\"type\":\"error\",\"code\":\"bad-message\"} | phoneA {\"type\":\"error\",\"code\":\"bad-message\",\"re\":\"k8\"}") throw new Exception("Malformed brain asks went as " + got + ".");
                foreach (string key in new[] { "AIza-phone-new-0001", "AIza-phone-slow-0003", "AIza-phone-second-0004", "sk-proj-phone-paid", "AIza-phone-rejected-0002" })
                    lock (sent) foreach (string line in sent) if (line.Contains(key)) throw new Exception("A key went back to a phone.");
                checks.Add("PASS a phone's brain asks: it sees the rung in use, BRAIN's words and only whether each rung has a key; its key is checked, saved with DPAPI and used exactly as the key box does it, with checking then saved notes and a notice on the PC; not-a-key, a paid key under MEDIUM and a rejected key get the Mainframe's own words; one key at a time; brain.use picks the rung; malformed asks get bad-message; no key goes back");
            }
            catch (System.Net.Sockets.SocketException) { checks.Add("LIMIT Remote brains: loopback sockets unavailable"); }
            finally
            {
                foreach (ManualResetEvent gate in gates) gate.Set();
                foreach (System.Net.Sockets.TcpListener server in servers) server.Stop();
                settle();
                brainOut = wasOut;
                brainNoticed = wasNoticed;
                phoneKey = null;
                brainWatchers.Clear();
                brainSent = null;
                if (brainTimer != null) brainTimer.Stop();
                ++cloudListing;
                ++asking;
                CloseTalk();
                preferences = wasPreferences;
                brain.Remote = wasRemote; brain.Local = wasLocal; brain.Model = wasModel; cloudModels = wasModels;
                Think = wasThink; finding = wasFinding;
                Brain.Google = wasGoogle;
                remoteNote = wasNote;
                mainframe.Sync();
                ShowTiers();
                if (wasRemote != null) mainframe.ShowCloud(wasRemote, wasModels);
                else mainframe.Searching();
                SavePreferences();
            }
        }
    }

    // What BRAIN shows, for a phone and for the smoke test.
    internal sealed partial class Mainframe
    {
        internal string BrainShown { get { return brainName.Text; } }
        internal string BrainNoted { get { return brainNote.Text; } }
        internal string PaidWords { get { return paidNote.Text; } }
    }
}
