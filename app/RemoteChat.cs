using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;

namespace CapriDesktop
{
    // Capri Remote in her chat (remote/PROTOCOL.md): a phone's chat.send goes in exactly like a line typed in her TYPE box,
    // and her answer to it, the tools she uses for it and her typing go back to that phone. Her other lines, like a reminder
    // or a comment on the screen, stay on the PC. The connection is app/RemoteLink.cs and the Mainframe's REMOTE sheet is
    // app/MainframeRemote.cs. Remote stays off until it's turned on there, and never runs in her smoke test.
    internal sealed partial class Companion
    {
        private readonly object remoteGate = new object();
        private volatile RemoteLink remoteLink;
        private volatile RemoteSink remoteSink;
        // Her tickets that answer a phone's message, and a phone's messages a running job heard instead.
        private readonly Dictionary<int, RemoteAsker> remoteTickets = new Dictionary<int, RemoteAsker>();
        private readonly List<RemoteAsker> remoteAsides = new List<RemoteAsker>();
        // A phone's message on its way into Hear, which owns any ticket Hear makes before it returns.
        private RemoteAsker remoteHearing;
        private int remoteHearingFrom;
        private string remoteNote = "";

        private sealed class RemoteAsker
        {
            internal string Device, Id, Text;
            internal bool Answered;
        }

        private sealed class RemoteToolRun
        {
            internal string Device, Re, Id, Name, Summary;
        }

        private RemoteSettings RemotePrefs
        {
            get
            {
                if (preferences.Remote == null) preferences.Remote = new RemoteSettings();
                return preferences.Remote;
            }
        }

        // ---------- On and off ----------

        // At start: only when it was left on, and never in her smoke test.
        private void StartRemote()
        {
            if (!testing && preferences.Remote != null && preferences.Remote.On) RemoteConnect();
            mainframe.RemoteChanged();
        }

        private void StopRemote() { RemoteDisconnect(); }

        internal void SetRemote(bool on)
        {
            if (testing) return;
            RemotePrefs.On = on;
            SavePreferences();
            remoteNote = "";
            if (on) RemoteConnect();
            else RemoteDisconnect();
            mainframe.RemoteChanged();
        }

        // A relay host as typed, or "" for the default; false when it isn't a host. The room goes with her to the new relay,
        // which hasn't seen it, so phones paired on the old one pair again.
        internal bool SetRemoteHost(string host)
        {
            if (RemoteLink.RelayBase(host) == null) return false;
            string typed = (host ?? "").Trim();
            string was = RemotePrefs.Host;
            RemotePrefs.Host = typed.Length == 0 || RemoteLink.RelayBase(typed) == RemoteLink.RelayBase(null) ? null : typed;
            if (RemotePrefs.Host == was) return true;
            SavePreferences();
            remoteNote = "Relay changed";
            if (RemotePrefs.On && !testing) RemoteConnect();
            mainframe.RemoteChanged();
            return true;
        }

        private void RemoteConnect()
        {
            RemoteDisconnect();
            RemoteSettings settings = RemotePrefs;
            string relay = RemoteLink.RelayBase(settings.Host);
            if (relay == null)
            {
                remoteNote = "Bad relay host";
                return;
            }
            RemoteState state = RemoteState.Unseal(settings.Sealed);
            if (state == null)
            {
                // First time on, or settings from another Windows user that DPAPI can't open: a new room.
                if (!string.IsNullOrEmpty(settings.Sealed)) remoteNote = "New room: pair your phones again";
                state = RemoteState.Fresh();
                settings.Sealed = state.Seal();
                SavePreferences();
            }
            RemoteLink link = new RemoteLink(state, relay, Environment.MachineName, "capri/" + Assembly.GetExecutingAssembly().GetName().Version.ToString(3), RemoteSaved);
            RemoteBrainsOn(link);
            link.Happened += RemoteHappened;
            link.Said += RemoteHeard;
            remoteLink = link;
            remoteSink = new RemoteLinkSink(link);
            RemoteLiveStart(link);
            link.Start();
        }

        private void RemoteDisconnect()
        {
            RemoteLiveStop();
            RemoteLink link = remoteLink;
            remoteLink = null;
            remoteSink = null;
            lock (remoteGate)
            {
                remoteTickets.Clear();
                remoteAsides.Clear();
                remoteHearing = null;
            }
            if (link == null) return;
            RemoteBrainsOff(link);
            link.Happened -= RemoteHappened;
            link.Said -= RemoteHeard;
            link.Stop();
        }

        // The room and phones changed: sealed with DPAPI for this Windows user here, stored with her settings on her thread.
        private void RemoteSaved(RemoteState state)
        {
            string sealedState = state.Seal();
            Post(delegate
            {
                RemotePrefs.Sealed = sealedState;
                SavePreferences();
            });
        }

        private void RemoteHappened(string what, string about)
        {
            Post(delegate
            {
                string note = RemoteNoteFor(what, about);
                if (note != null) remoteNote = note;
                mainframe.RemoteChanged();
            });
        }

        private string RemoteNoteFor(string what, string about)
        {
            switch (what)
            {
                case "allowed": return "Pairing...";
                case "paired":
                {
                    RemoteLink link = remoteLink;
                    if (link != null)
                        foreach (RemoteDevice device in link.Devices())
                            if (device.Id == about)
                            {
                                if (device.From == null) return "Paired " + device.Name;
                                // No Allow was asked for this one (PROTOCOL.md 5.1), so the PC says so out loud.
                                string words = "Paired " + device.Name + " with " + device.From + "'s link";
                                RemoteLinkNotice(words);
                                return words;
                            }
                    return "Paired";
                }
                case "link":
                {
                    RemoteLink link = remoteLink;
                    if (link != null) foreach (RemoteDevice device in link.Devices()) if (device.Id == about) return "Pairing link sent to " + device.Name;
                    return "Pairing link sent";
                }
                case "pair-failed": return about == "full" ? "16 phones is the most: revoke one first" : about == "version" ? "That phone needs a newer page" : "The phone left";
                case "ask-ended": return about == "timeout" ? "The phone waited too long" : about == "offline" ? "Lost the relay: show a new code" : "The phone left";
                case "kicked": return "A frame failed its check: that phone reconnects";
                case "version": return "A phone needs a newer page";
                case "removed": return "Phone removed";
                case "new-room": return "New room: pair your phones again";
                case "room-reset": return "The relay forgot this PC: pair again";
                default: return null;
            }
        }

        // A phone that got in with another phone's pairing link and no Allow: a tray balloon, so it's seen even with the REMOTE
        // sheet shut, and it can be revoked there.
        private void RemoteLinkNotice(string words)
        {
            if (testing) return;
            balloonPage = null;
            balloonUpdate = false;
            balloonMainframe = true;
            tray.ShowBalloonTip(8000, "Capri Remote", words + ". Not you? Revoke it under REMOTE in her Mainframe.", System.Windows.Forms.ToolTipIcon.Info);
        }

        // ---------- What the REMOTE sheet asks for ----------

        internal RemoteView RemoteView()
        {
            RemoteView view = new RemoteView();
            RemoteSettings settings = preferences.Remote;
            view.On = settings != null && settings.On;
            view.Host = settings == null || string.IsNullOrEmpty(settings.Host) ? RemoteLink.DefaultHost : settings.Host;
            view.Note = remoteNote;
            RemoteLink link = remoteLink;
            if (link != null)
            {
                view.Status = link.Status;
                view.Ready = link.Ready;
                view.QrSeconds = link.QrSeconds;
                view.Qr = view.QrSeconds > 0 ? link.Qr : null;
                view.Expired = view.QrSeconds == 0 && link.Qr != null;
                view.Ask = link.Asking;
                view.LinkFor = link.LinkFor;
                view.Phones = link.Devices();
                foreach (RemoteDevice phone in view.Phones) if (link.IsOnline(phone.Id)) view.Online.Add(phone.Id);
                return view;
            }
            view.Status = view.On ? "bad-host" : "off";
            RemoteState state = settings == null ? null : RemoteState.Unseal(settings.Sealed);
            if (state != null)
            {
                foreach (RemoteDevice device in state.Devices) view.Phones.Add(new RemoteDevice { Id = device.Id, Name = device.Name, Added = device.Added, From = device.From });
                view.Phones.Sort(delegate (RemoteDevice a, RemoteDevice b) { return b.Added.CompareTo(a.Added); });
            }
            return view;
        }

        internal void RemoteNewCode()
        {
            RemoteLink link = remoteLink;
            if (link != null && link.NewCode() != null) remoteNote = "";
            mainframe.RemoteChanged();
        }

        // The sheet closed: the relay forgets an open code, and a phone still waiting gets a no.
        internal void RemoteCloseCode()
        {
            RemoteLink link = remoteLink;
            if (link != null) link.CloseCode();
        }

        internal void RemoteAllow()
        {
            RemoteLink link = remoteLink;
            if (link != null) link.Allow();
            mainframe.RemoteChanged();
        }

        internal void RemoteDeny()
        {
            RemoteLink link = remoteLink;
            if (link != null && link.Deny()) remoteNote = "Said no";
            mainframe.RemoteChanged();
        }

        // With Remote off, the phone goes from the sealed list now and from the relay at its next sign-in.
        internal void RemoteRevoke(string device)
        {
            RemoteLink link = remoteLink;
            if (link != null) link.Revoke(device);
            else
            {
                RemoteState state = preferences.Remote == null ? null : RemoteState.Unseal(preferences.Remote.Sealed);
                RemoteDevice found = state == null ? null : state.Find(device);
                if (found == null) return;
                state.Devices.Remove(found);
                preferences.Remote.Sealed = state.Seal();
                SavePreferences();
            }
            remoteNote = "Phone removed";
            mainframe.RemoteChanged();
        }

        // ---------- A phone's message in, her answer out ----------

        private void RemoteHeard(string device, string id, string text) { Post(delegate { RemoteHear(device, id, text); }); }

        private void RemoteHear(string device, string id, string text)
        {
            RemoteSink sink = remoteSink;
            if (sink == null) return;
            string said = RemoteInput(text);
            RemoteAsker asker = new RemoteAsker { Device = device, Id = id, Text = said.Trim() };
            bool beside = MidJob(job);
            int before = asking;
            lock (remoteGate)
            {
                remoteHearing = asker;
                remoteHearingFrom = before;
            }
            try { Hear(said, said, "> you: "); }
            finally { lock (remoteGate) remoteHearing = null; }
            bool asked = false;
            RemoteAsker pushedOut = null;
            lock (remoteGate)
            {
                if (asking != before)
                {
                    if (!asker.Answered && !remoteTickets.ContainsKey(asking)) remoteTickets[asking] = asker;
                    asked = true;
                }
                else if (beside)
                {
                    remoteAsides.Add(asker);
                    if (remoteAsides.Count > 16)
                    {
                        pushedOut = remoteAsides[0];
                        remoteAsides.RemoveAt(0);
                    }
                }
            }
            if (asked) RemoteDrop(asking, asker);
            else if (pushedOut != null) sink.Error(pushedOut.Device, "busy", pushedOut.Id);
            // A quick command took it with nothing to say back, like stop listening: the phone's waiting reply closes empty.
            else if (!beside) sink.Reply(device, RemoteMessages.NewId(), id, "", true);
        }

        // Begin's hook: her answer to a phone's message goes back to that phone as one finished reply. An ask she heard again,
        // like a stuck model's retry or more for a job that had just ended, still answers the phone that sent it.
        private void RemoteSaid(int ticket, string said)
        {
            RemoteSink sink = remoteSink;
            if (sink == null) return;
            RemoteAsker asker = RemoteAskerFor(ticket, true) ?? (box.Prompt == "> you: " ? RemoteAskedAgain(ticket, heard) : null);
            RemoteDrop(ticket, asker);
            if (asker == null) return;
            asker.Answered = true;
            sink.Reply(asker.Device, RemoteMessages.NewId(), asker.Id, RemoteOut(said), true);
        }

        // Begin only answers her newest ask, so a phone's message from before this ticket won't get its answer now. Its
        // waiting reply closes: quietly when the same phone's newer message took over, as on her TYPE box, and as busy when
        // something else did, like a line typed on the PC.
        private void RemoteDrop(int ticket, RemoteAsker newer)
        {
            RemoteSink sink = remoteSink;
            if (sink == null) return;
            List<RemoteAsker> dropped = new List<RemoteAsker>();
            lock (remoteGate)
            {
                List<int> old = new List<int>();
                foreach (KeyValuePair<int, RemoteAsker> pair in remoteTickets) if (pair.Key < ticket && pair.Value != newer) old.Add(pair.Key);
                foreach (int key in old)
                {
                    RemoteAsker asker = remoteTickets[key];
                    remoteTickets.Remove(key);
                    if (!asker.Answered && !dropped.Contains(asker)) dropped.Add(asker);
                }
                foreach (RemoteAsker asker in dropped) asker.Answered = true;
            }
            foreach (RemoteAsker asker in dropped)
            {
                if (newer != null && newer.Device == asker.Device) sink.Reply(asker.Device, RemoteMessages.NewId(), asker.Id, "", true);
                else sink.Error(asker.Device, "busy", asker.Id);
            }
        }

        // The phone's message an ask she heard again answers: the same words from before this ticket, still waiting for her
        // answer or heard beside a job.
        private RemoteAsker RemoteAskedAgain(int ticket, string asked)
        {
            string text = (asked ?? "").Trim();
            if (text.Length == 0) return null;
            lock (remoteGate)
            {
                int found = -1;
                foreach (KeyValuePair<int, RemoteAsker> pair in remoteTickets)
                    if (pair.Key < ticket && !pair.Value.Answered && pair.Value.Text == text) found = pair.Key;
                if (found >= 0)
                {
                    RemoteAsker asker = remoteTickets[found];
                    remoteTickets.Remove(found);
                    return asker;
                }
                for (int i = 0; i < remoteAsides.Count; i++)
                    if (remoteAsides[i].Text == text)
                    {
                        RemoteAsker asker = remoteAsides[i];
                        remoteAsides.RemoveAt(i);
                        return asker;
                    }
            }
            return null;
        }

        // AsideSay's hook: her answer beside a job, to a phone's message the job heard, goes back to that phone.
        private void RemoteAside(string asked, string line)
        {
            RemoteSink sink = remoteSink;
            if (sink == null || asked == null || string.IsNullOrEmpty(line)) return;
            RemoteAsker asker = null;
            lock (remoteGate)
                for (int i = 0; i < remoteAsides.Count && asker == null; i++)
                    if (remoteAsides[i].Text == asked.Trim())
                    {
                        asker = remoteAsides[i];
                        remoteAsides.RemoveAt(i);
                    }
            if (asker != null) sink.Reply(asker.Device, RemoteMessages.NewId(), asker.Id, RemoteOut(line), true);
        }

        // DoTool's hooks, on whatever thread the tool runs: a tool she uses for a phone's message shows on that phone as
        // running, then done or failed. The summary is plain words from the tool's name, never its arguments.
        private object RemoteToolStart(string tool, ToolArguments args, int ticket)
        {
            RemoteSink sink = remoteSink;
            if (sink == null) return null;
            RemoteAsker asker = RemoteAskerFor(ticket, false);
            if (asker == null) return null;
            RemoteToolRun run = new RemoteToolRun { Device = asker.Device, Re = asker.Id, Id = RemoteMessages.NewId(), Name = tool ?? "tool", Summary = RemoteToolWords(tool, args) };
            sink.Tool(run.Device, run.Id, run.Name, run.Summary, "running", run.Re);
            return run;
        }

        private void RemoteToolEnd(object started, string done)
        {
            RemoteToolRun run = started as RemoteToolRun;
            RemoteSink sink = remoteSink;
            if (run != null && sink != null) sink.Tool(run.Device, run.Id, run.Name, run.Summary, Hands.Failed(done) ? "failed" : "done", run.Re);
        }

        // The phone's message a ticket answers, if any. take: her answer is going, so the ticket is done with.
        private RemoteAsker RemoteAskerFor(int ticket, bool take)
        {
            lock (remoteGate)
            {
                RemoteAsker asker;
                if (!remoteTickets.TryGetValue(ticket, out asker) && remoteHearing != null && ticket > remoteHearingFrom)
                {
                    asker = remoteHearing;
                    if (!take) remoteTickets[ticket] = asker;
                }
                if (take && asker != null) remoteTickets.Remove(ticket);
                return asker;
            }
        }

        // What a phone sent, as she'd read it typed: control characters other than line breaks come out, and tabs become
        // spaces.
        private static string RemoteInput(string text)
        {
            StringBuilder kept = new StringBuilder((text ?? "").Length);
            foreach (char c in text ?? "")
            {
                if (c == '\t') kept.Append(' ');
                else if (c == '\n' || !char.IsControl(c)) kept.Append(c);
            }
            return kept.ToString();
        }

        // Her words for a phone, with passwords she knows dotted out and keys, tokens and passwords taken out, as in her logs.
        private static string RemoteOut(string text) { return Brain.Scrubbed(Brain.Redacted(Computer.Mask(text ?? ""))); }

        private static string RemoteToolWords(string tool, ToolArguments args)
        {
            switch (tool)
            {
                case "use_computer": return "Using the computer";
                case "look_at_screen": return "Looking at the screen";
                case "open_app": return "Opening an app";
                case "open_website": return "Opening a website";
                case "website_nickname": return "Saving a website name";
                case "play_music": return "Playing music";
                case "media":
                {
                    // The phone lights its music bar from these words, so a known action is named and nothing else is.
                    string action = args == null || args.Action == null ? "" : args.Action.Trim().ToLowerInvariant();
                    switch (action)
                    {
                        case "resume": return "Media: resume";
                        case "pause": return "Media: pause";
                        case "stop": return "Media: stop";
                        case "next": return "Media: next track";
                        case "previous": return "Media: previous track";
                        case "play_pause": return "Media: toggle";
                        default: return "Media";
                    }
                }
                case "volume": return "Changing the volume";
                case "find_file": return "Finding a file";
                case "read_file": return "Reading a file";
                case "screenshot": return "Taking a screenshot";
                case "clipboard": return "Using the clipboard";
                case "remind": return "Setting a reminder";
                case "routine": return "Running a routine";
                case "gaming_mode": return "Gaming Mode";
                case "watch_along": return "Watching along";
                case "draw": return "Drawing";
                case "clone": return "Clones";
                case "hologram": return "Hologram";
                case "nap": return "Napping";
                case "move_yourself": return "Moving";
                case "animate": return "Animating";
                case "desktop_icons": return "Desktop icons";
                default: return "Using " + RemoteMessages.CleanText((tool ?? "a tool").Replace('_', ' '), 64);
            }
        }

        // ---------- Smoke ----------

        // Remote's own checks: off by default and in the test, the crypto against remote/test/vectors.json when it's there,
        // the QR a pairing URL makes, tool words that never carry arguments, and the REMOTE sheet at 100%, 150% and 200% in
        // each of its states, drawn to remote-*.png in the output folder.
        private void SmokeRemote(List<string> checks, string output)
        {
            if (new Preferences().Remote != null) throw new Exception("Remote isn't off for a new install.");
            if (remoteLink != null) throw new Exception("Remote connected in her smoke test.");
            string vectors = RemoteVectors.Find();
            if (vectors != null)
            {
                int count;
                List<string> failed = RemoteVectors.Check(File.ReadAllText(vectors), out count);
                if (failed.Count > 0) throw new Exception("Remote crypto: " + failed[0]);
                checks.Add("PASS Remote crypto matches all " + count + " checks in remote/test/vectors.json");
            }
            else checks.Add("LIMIT Remote crypto vectors not checked: remote/test/vectors.json isn't next to this build (app/RemoteTests runs them)");
            string url = RemoteCrypto.QrUrl(RemoteCrypto.B64u(RemoteCrypto.Random(16)), RemoteCrypto.B64u(RemoteCrypto.Random(16)), RemoteCrypto.B64u(RemoteCrypto.Random(32)));
            RemoteQr qr = RemoteQr.Encode(url);
            if (qr.Version != 8 || qr.Correction != RemoteQr.Level.Medium || !qr.Dark(0, 0) || !qr.Dark(6, 6) || qr.Dark(7, 7)) throw new Exception("A pairing QR came out as version " + qr.Version + " " + qr.Correction + ".");
            ToolArguments secret = new ToolArguments { Site = "example.com/?token=hunter2", Task = "type hunter2", Action = "hunter2" };
            foreach (string tool in new[] { "open_website", "use_computer", "media", "some_new_tool" })
                if (RemoteToolWords(tool, secret).Contains("hunter2")) throw new Exception("A tool summary carried its arguments.");
            if (RemoteToolWords("media", new ToolArguments { Action = "resume" }) != "Media: resume") throw new Exception("Media's summary lost its action.");
            if (RemoteOut("my key is sk-abcdefghijklmnopqrstuvwxyz123456").Contains("abcdefghijklmnop")) throw new Exception("A key in her reply went to the phone.");
            // Phone keyboards' text reaches her brain whole: curly quotes and emoji stay, and half an emoji, cut off by a trim
            // like her 57-character screen names, never goes out as a lone \ud83d, which NVIDIA's server turned away (8 Nov).
            string phoneText = "Open Discord\u2019s \u201Ccall\u201D \uD83D\uDE00\uD83C\uDFAE and play \uD834\uDD1E " + new string('x', 400) + " \u2014 thanks!";
            string cut = (new string('a', 56) + "\uD83C\uDFAE more").Substring(0, 57);
            byte[] wire = Brain.Json(new ChatRequest { Model = "phone-test", Messages = new[] { new ChatMessage("user", phoneText), new ChatMessage("tool", cut), new ChatMessage("user", "\uDE00 and \uD83D") } });
            string sent = Encoding.UTF8.GetString(wire);
            ChatRequest read;
            using (MemoryStream back = new MemoryStream(wire)) read = (ChatRequest)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ChatRequest)).ReadObject(back);
            if (read.Messages[0].Content != phoneText || sent.IndexOf("\\ud83d\\ude00", StringComparison.Ordinal) < 0) throw new Exception("Phone text lost its emoji or curly quotes on the way to her brain.");
            if (read.Messages[1].Content != cut.Substring(0, 56) + "\uFFFD" || read.Messages[2].Content != "\uFFFD and \uFFFD" || Encoding.UTF8.GetString(Brain.Whole(wire)) != sent)
                throw new Exception("Half an emoji went to her brain on its own: " + sent.Substring(Math.Max(0, sent.Length - 160)));
            checks.Add("PASS phone text reaches her brain whole: curly quotes, emoji and other non-BMP characters stay, and half an emoji cut off by a trim goes as U+FFFD instead of a lone escape strict servers refuse");
            string sample =RemoteCrypto.QrUrl(RemoteCrypto.B64u(new byte[16]), RemoteCrypto.B64u(new byte[16]), RemoteCrypto.B64u(new byte[32]));
            RemoteView[] views = new RemoteView[4];
            for (int i = 0; i < views.Length; i++)
            {
                RemoteView view = views[i] = new RemoteView { On = true, Ready = true, Status = "online" };
                view.Phones.Add(new RemoteDevice { Id = RemoteCrypto.B64u(new byte[16]), Name = "Lachy's Pixel 9 Pro XL", Added = new DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc) });
                view.Phones.Add(new RemoteDevice { Id = RemoteCrypto.B64u(RemoteCrypto.Random(16)), Name = "iPad", Added = new DateTime(2026, 11, 7, 9, 30, 0, DateTimeKind.Utc) });
                view.Online.Add(view.Phones[0].Id);
            }
            views[0].Qr = sample;
            views[0].QrSeconds = 600;
            views[1].Ask = new RemoteAsk { Pending = "p", Name = "Samsung Galaxy S26 Ultra with a long name", Code = "318 338", App = "remote-web/0.1.0" };
            views[2].Note = "Paired Lachy's Pixel 9 Pro XL";
            // A phone's pairing link open (named, never a QR) and a phone it let in.
            views[2].LinkFor = "Lachy's Pixel 9 Pro XL";
            views[2].Phones[1].From = "Lachy's Pixel 9 Pro XL";
            views[3] = new RemoteView { Status = "off", Host = "relay.example:8443" };
            string[] names = { "qr", "ask", "phones", "off" };
            foreach (float zoom in new[] { 1f, 1.5f, 2f })
                for (int i = 0; i < views.Length; i++)
                {
                    string picture = Path.Combine(output, "remote-" + names[i] + "-" + (int)(zoom * 100) + ".png");
                    string problem = mainframe.RemoteSheetAt(zoom, views[i], picture);
                    if (problem != null) throw new Exception("The REMOTE sheet (" + names[i] + ", " + (int)(zoom * 100) + "%): " + problem);
                }
            SmokeRemoteBridge(checks);
            SmokeRemoteLive(checks, output);
            string key = mainframe.RemoteKeyFor(views[0]);
            if (key != "Remote on, 1 phone") throw new Exception("Mainframe's Remote key showed " + key + ".");
            string band = mainframe.RemoteBand(Path.Combine(output, "remote-key-picked.png"));
            if (band != null) throw new Exception("CAPRI's band: " + band + ".");
            mainframe.RemoteKeyFor(views[3]);
            mainframe.RemoteBand(Path.Combine(output, "remote-key-off.png"));
            mainframe.RemoteChanged();
            checks.Add("PASS Remote is off by default and in tests; a pairing URL makes a version 8 QR; tool summaries carry no arguments and keys stay out of replies; the REMOTE sheet fits at 100%, 150% and 200% with a QR, an Allow prompt, paired phones (one added by a phone's pairing link, whose open link shows as words, never a QR) and Remote off (remote-*.png)");
        }

        // What the bridge would send a phone, for the smoke test.
        private sealed class RemoteTap : RemoteSink
        {
            private readonly List<string> sent = new List<string>();

            public void Reply(string device, string id, string re, string text, bool done) { lock (sent) sent.Add("reply " + device + " " + re + " " + (done ? "done" : "part") + ": " + (text.Length == 0 ? "(empty)" : text)); }

            public void Error(string device, string code, string re) { lock (sent) sent.Add("error " + device + " " + re + " " + code); }

            public void Tool(string device, string id, string name, string summary, string state, string re) { lock (sent) sent.Add("tool " + device + " " + re + " " + name + " " + state + ": " + summary); }

            internal int Count { get { lock (sent) return sent.Count; } }

            internal string Take()
            {
                lock (sent)
                {
                    string all = string.Join(" | ", sent.ToArray());
                    sent.Clear();
                    return all;
                }
            }
        }

        // A phone's message through her real chat path with a stand-in brain: her answer goes back with its re, tools she uses
        // for it show running, then done or failed, a line typed on the PC stays on the PC, and an answer beside a job finds
        // the phone the job heard.
        private void SmokeRemoteBridge(List<string> checks)
        {
            RemoteTap tap = new RemoteTap();
            System.Threading.ManualResetEvent hold = new System.Threading.ManualResetEvent(false);
            Func<string, string> thinkWas = Think, animateWas = Hands.Animate;
            bool silentWas = preferences.Silent;
            foreach (string asked in new[] { "hello from my phone", "what are you up to", "typed on the PC", "first message", "second message", "third message", "typed over it", "say it again" })
                if (Hands.Plan(asked).Key != null) throw new Exception("The bridge test's line \"" + asked + "\" plans a tool of its own.");
            try
            {
                remoteSink = tap;
                preferences.Silent = true;
                Think = delegate { return "Hi from Capri."; };
                RemoteHear("phoneA", "m1", "hello from my phone");
                WaitVoice(delegate { return reply != null && tap.Count > 0; }, "A phone's message got no answer.");
                string got = tap.Take();
                if (got != "reply phoneA m1 done: Hi from Capri.") throw new Exception("A phone's message got " + got + ".");
                CloseTalk();
                Hands.Animate = delegate(string move) { return move == "grin" ? "Grinned." : "Couldn't do that just now."; };
                Think = delegate
                {
                    DoTool("animate", new ToolArguments { Name = "dance" });
                    return DoTool("animate", new ToolArguments { Name = "grin" });
                };
                RemoteHear("phoneA", "m2", "what are you up to");
                WaitVoice(delegate { return reply != null && tap.Count >= 5; }, "A phone's message that used tools got no answer.");
                got = tap.Take();
                if (got != "tool phoneA m2 animate running: Animating | tool phoneA m2 animate failed: Animating | tool phoneA m2 animate running: Animating | tool phoneA m2 animate done: Animating | reply phoneA m2 done: Grinned.") throw new Exception("Tools for a phone's message went as " + got + ".");
                CloseTalk();
                Think = delegate { return "Only for the PC."; };
                Hear("typed on the PC");
                WaitVoice(delegate { return reply != null; }, "A line typed on the PC got no answer.");
                got = tap.Take();
                if (got.Length > 0) throw new Exception("A line typed on the PC went to a phone: " + got + ".");
                CloseTalk();
                lock (remoteGate) remoteAsides.Add(new RemoteAsker { Device = "phoneB", Id = "m3", Text = "how's it going?" });
                RemoteAside("how's it going? ", "Nearly done.");
                got = tap.Take();
                if (got != "reply phoneB m3 done: Nearly done.") throw new Exception("An answer beside a job went as " + got + ".");
                // Two messages in a row from one phone: the first one's reply closes empty, as her TYPE box drops it.
                Think = delegate(string words)
                {
                    if (words != "second message") hold.WaitOne(3000);
                    return words == "second message" ? "Second." : "Late.";
                };
                RemoteHear("phoneA", "m4", "first message");
                RemoteHear("phoneA", "m5", "second message");
                WaitVoice(delegate { return reply != null && tap.Count >= 2; }, "A phone's second message in a row got no answer.");
                got = tap.Take();
                if (got != "reply phoneA m4 done: (empty) | reply phoneA m5 done: Second.") throw new Exception("Two messages in a row went as " + got + ".");
                hold.Set();
                CloseTalk();
                hold.Reset();
                // A line typed on the PC over a phone's message: the phone hears she's busy.
                RemoteHear("phoneA", "m6", "third message");
                Think = delegate { return "Only for the PC."; };
                Hear("typed over it");
                WaitVoice(delegate { return reply != null && tap.Count >= 1; }, "A phone's message typed over on the PC stayed open.");
                got = tap.Take();
                if (got != "error phoneA m6 busy") throw new Exception("A phone's message typed over on the PC went as " + got + ".");
                CloseTalk();
                // The same words heard again, like a stuck model's retry, still answer the phone.
                Think = delegate(string words) { if (words == "say it again") hold.WaitOne(3000); return "Said again."; };
                RemoteHear("phoneB", "m7", "say it again");
                Think = delegate { return "Said again."; };
                Hear("say it again", "say it again", "> you: ");
                WaitVoice(delegate { return reply != null && tap.Count >= 1; }, "A message heard again got no answer.");
                got = tap.Take();
                if (got != "reply phoneB m7 done: Said again.") throw new Exception("A message heard again went as " + got + ".");
            }
            finally
            {
                hold.Set();
                remoteSink = null;
                Think = thinkWas;
                Hands.Animate = animateWas;
                preferences.Silent = silentWas;
                lock (remoteGate)
                {
                    remoteTickets.Clear();
                    remoteAsides.Clear();
                }
                CloseTalk();
            }
            checks.Add("PASS a phone's message goes in like her TYPE box: her answer goes back to its message, tools she uses for it show running then done or failed, a line typed on the PC stays on the PC, an answer beside a job finds its phone, a message she moves on from closes (empty for the same phone's next message, busy for a line typed on the PC), and the same words heard again still answer the phone");
        }
    }

    // Where the chat bridge sends a phone's answers: her link, or the smoke test's tap.
    internal interface RemoteSink
    {
        void Reply(string device, string id, string re, string text, bool done);
        void Tool(string device, string id, string name, string summary, string state, string re);
        void Error(string device, string code, string re);
    }

    internal sealed class RemoteLinkSink : RemoteSink
    {
        private readonly RemoteLink link;

        internal RemoteLinkSink(RemoteLink link) { this.link = link; }

        // These run on her chat's thread or a tool's, where something thrown would end her mid-job: a send to the phone that
        // fails goes in her log and her work carries on.
        public void Reply(string device, string id, string re, string text, bool done) { Guard(delegate { link.Reply(device, id, re, text, done); }); }

        public void Tool(string device, string id, string name, string summary, string state, string re) { Guard(delegate { link.Tool(device, id, name, summary, state, re); }); }

        public void Error(string device, string code, string re) { Guard(delegate { link.Error(device, code, re); }); }

        private static void Guard(Action send)
        {
            try { send(); }
            catch (Exception error) { Brain.Log("remote", -1, "A message to the phone failed: " + error.GetType().Name + ": " + error.Message); }
        }
    }
}
