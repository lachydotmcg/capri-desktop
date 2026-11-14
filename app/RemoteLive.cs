using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Capri Remote's screen view and call in Capri herself (app/RemoteScreen.cs, app/RemoteCall.cs): her monitors and their
    // captures (FrameGrab), the monitor she's looking at for Auto, her ears and voice for a call, and a badge on the PC the
    // whole time a phone watches or calls. RemoteConnect starts it with her link and RemoteDisconnect ends it.
    internal sealed partial class Companion
    {
        private volatile RemoteScreen remoteScreen;
        private volatile RemoteCall remoteCall;
        // The monitor she's looking at, worked out on her thread every half second while a phone watches.
        private volatile string remoteLooking;
        private RemoteBadge remoteBadge;
        private System.Windows.Forms.Timer remoteLookTimer;
        // Her answers for a phone on a call go a sentence at a time; her smoke test turns it off to time it against before.
        private bool remotePieces = true;
        // The phone message the answer she's saying is for, and its ticket, so Interrupt on that phone can stop it.
        private RemoteAsker remoteSaying;
        private int remoteSayingTicket = -1;
        // Her smoke test lets her voice reach her (silent) speaker, so it reaches a phone's call as it would for real.
        private bool voiceInTest;

        // On her thread, with the link RemoteConnect just made.
        private void RemoteLiveStart(RemoteLink link)
        {
            RemoteLiveStop();
            RemoteScreen screen = new RemoteScreen(link, new RemoteDesktop(this));
            RemoteCall call = new RemoteCall(link, RemoteEars, RemoteHeard);
            call.VoiceOn = delegate { return !preferences.Silent; };
            call.EarsOn = delegate { return OwnCallEars() != null || Hearing.Available; };
            call.Interrupted = RemoteInterrupted;
            call.Timed = RemoteTimed;
            screen.Changed += RemoteLiveChanged;
            call.Changed += RemoteLiveChanged;
            remoteScreen = screen;
            remoteCall = call;
            Speaker.Tap = RemoteVoiceTap;
            Speaker.Hushed = RemoteVoiceHushed;
        }

        private void RemoteLiveStop()
        {
            RemoteScreen screen = remoteScreen;
            RemoteCall call = remoteCall;
            remoteScreen = null;
            remoteCall = null;
            Speaker.Tap = null;
            Speaker.Hushed = null;
            if (screen != null) screen.Dispose();
            if (call != null) call.Dispose();
            RemoteBadgeShow();
        }

        // Her own ears on a phone's line: Parakeet whenever it's in and Windows speech isn't the pick, with Talk on at the PC
        // or not, and Windows' recognizer otherwise. Hearing.Text alone only has Parakeet while Talk is on, so with Talk off
        // every call line went to Windows' recognizer, which turned them into word soup (9 Nov).
        private string RemoteEars(short[] samples)
        {
            LocalEars own = OwnCallEars();
            return own != null ? Hearing.Clean(own.Hear(samples)) : Hearing.Text(samples);
        }

        private LocalEars OwnCallEars()
        {
            LocalEars own = parakeet;
            return own != null && HearingStatus.Own(HearingChoice, own.Ready) ? own : null;
        }

        // Her Parakeet ears load as a call starts, so its first line isn't held up loading them, and unload after the last
        // call ends unless Talk is on and still has them.
        private bool remoteEarsWarm;

        private void RemoteEarsFor(bool calling)
        {
            if (calling == remoteEarsWarm) return;
            remoteEarsWarm = calling;
            LocalEars own = OwnCallEars();
            if (testing) return;
            if (calling) { if (own != null) ThreadPool.QueueUserWorkItem(delegate { try { own.Warm(); } catch (Exception) { } }); }
            else if (!chatting) EarsModel(false);
        }

        private void RemoteVoiceTap(short[] samples, int rate)
        {
            RemoteCall call = remoteCall;
            // Her voice's own thread: nothing here may stop her talking.
            try { if (call != null && call.Any) call.Voice(samples, rate); }
            catch (Exception) { }
        }

        private void RemoteVoiceHushed()
        {
            RemoteCall call = remoteCall;
            try { if (call != null) call.Hush(); }
            catch (Exception) { }
        }

        private void RemoteLiveChanged() { Post(RemoteBadgeShow); }

        // How soon her first words reached a phone after its line ended, in her log for each answer on a call.
        private static void RemoteTimed(long ms, string words) { Brain.Log("call", ms, words); }

        // ---------- Her answer on a call, a sentence at a time ----------

        // The pieces an answer is voiced in when the phone that asked is on a call, or null to voice it whole as usual.
        private List<string> RemotePieces(int ticket, string words)
        {
            RemoteCall call = remoteCall;
            if (call == null || !remotePieces || !call.Any) return null;
            RemoteAsker asker = RemoteAskerFor(ticket, false);
            if (asker == null || !call.Hushes(asker.Device)) return null;
            List<string> pieces = RemoteCall.Pieces(words);
            return pieces.Count > 1 ? pieces : null;
        }

        private sealed class PieceRun
        {
            internal volatile bool Stopped;
        }

        // Her answer for a phone on a call, voiced a piece at a time on one pool thread: each piece plays as soon as it's
        // made, straight after the one before, while the next one is made, so the phone hears her first words while the rest
        // is still being voiced. Her bubble shows the whole answer with the first piece, paced by how long the whole will
        // take. A newer answer, talking over her or Interrupt on the phone stops the pieces still to come, and a piece her
        // voice can't make leaves the rest in words.
        private void VoicePieces(int ticket, int turn, int audio, string said, TalkState state, Func<string, byte[]> speak, Func<string, byte[]> free, List<string> pieces)
        {
            int letters = 0;
            foreach (string piece in pieces) letters += Math.Max(1, VoiceServer.Letters(piece));
            PieceRun run = new PieceRun();
            // As for her HD voice, her bubble stays up for the pieces still to come.
            hdSpeaking = ticket;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Func<string, byte[]> voicing = speak;
                for (int i = 0; i < pieces.Count; i++)
                {
                    if (run.Stopped || turn != answerTurn || ticket != Volatile.Read(ref asking) || audio != audioTurn) return;
                    // Out of ElevenLabs credits partway: Voiced said that piece in her free voice, and so are the rest.
                    if (voiceBroke && free != null) voicing = free;
                    string problem;
                    Stopwatch took = Stopwatch.StartNew();
                    byte[] wav = Voiced(voicing, free, pieces[i], out problem);
                    long ms = took.ElapsedMilliseconds;
                    if (!testing && problem != null) Brain.Log(voicing == free ? "free voice" : "ElevenLabs voice", ms, problem);
                    int index = i;
                    bool freeVoice = voicing == free;
                    double share = Math.Max(1, VoiceServer.Letters(pieces[index])) / (double)letters;
                    Post(delegate { PieceVoiced(run, ticket, turn, audio, said, state, freeVoice, wav, problem, ms, pieces, index, share); });
                    if (wav == null) return;
                }
            });
        }

        // On her thread: one piece of her answer is voiced. The first goes like an answer's whole WAV; the rest follow it.
        private void PieceVoiced(PieceRun run, int ticket, int turn, int audio, string said, TalkState state, bool freeVoice, byte[] wav, string problem, long ms, List<string> pieces, int index, double share)
        {
            bool current = turn == answerTurn && ticket == asking && !IsDisposed;
            bool audible = current && audio == audioTurn && !run.Stopped;
            bool last = index == pieces.Count - 1 || wav == null;
            double seconds = wav == null ? 0 : Voice.Seconds(wav);
            VoiceDelivered(turn, audio, current, audible, wav, pieces[index], ms, " piece=" + (index + 1) + "/" + pieces.Count);
            if (!current) return;
            if (audible)
            {
                if (index == 0 || problem != null) voiceBroken = problem != null;
                if (problem != null) mainframe.VoiceNote(problem, wav == null);
            }
            bool played = false;
            if (index == 0)
            {
                // The whole answer's length, from how long its first piece runs, paces her bubble.
                double whole = seconds > 0 && share > 0 ? seconds / share : 0;
                if (reply == null && talkState == TalkState.Thinking) Begin(ticket, said, state, audible ? wav : null, whole);
                else if (audible && FollowDelivery(ticket) && talkState != TalkState.Off && box.Leaving < 0 && reply == said && (freeVoice || !preferences.Quiet)) PlayReply(said, wav);
                played = audible && wav != null && voiceUntil > clock.Elapsed.TotalSeconds;
            }
            else if (audible && wav != null && talkState != TalkState.Off && box.Leaving < 0 && reply == said)
            {
                PlayMore(said, wav);
                played = true;
            }
            // Nothing more of it plays once a piece didn't: the rest isn't voiced.
            if (!played) run.Stopped = true;
            if (last || !played)
            {
                if (answerVoice == ticket) answerVoice = -1;
                if (hdSpeaking == ticket) hdSpeaking = -1;
            }
        }

        // The next piece of an answer, straight after what's still playing: her mouth, the levels her ears hold against her own
        // voice and how long she's talking run on with it, as if it had all come in one WAV.
        private void PlayMore(string said, byte[] wav)
        {
            double seconds = Voice.Seconds(wav);
            if (seconds < 0.05) return;
            double now = clock.Elapsed.TotalSeconds;
            // PlayReply's voiceUntil runs 0.6 s past her sound.
            double starts = Math.Max(now, voiceUntil - 0.6);
            if (speech == null) Say(said);
            mouths = Joined(mouths, (int)Math.Round((starts - speechStart) * MouthRate), Voice.Mouths(wav, MouthRate), -1);
            double[] levels = TalkOver.Levels(wav);
            if (spokenLevels == null)
            {
                spokenLevels = levels;
                spokenAt = starts;
            }
            else spokenLevels = Joined(spokenLevels, (int)Math.Round((starts - spokenAt) * 10), levels, 0.0);
            voiceUntil = starts + seconds + 0.6;
            CheckEars(now);
            PlayVoice(wav);
        }

        // head, then pad until at (never over what head already holds), then tail.
        private static T[] Joined<T>(T[] head, int at, T[] tail, T pad)
        {
            int kept = head == null ? 0 : head.Length, start = Math.Max(at, kept);
            T[] all = new T[start + tail.Length];
            if (head != null) Array.Copy(head, all, kept);
            for (int i = kept; i < start; i++) all[i] = pad;
            Array.Copy(tail, 0, all, start, tail.Length);
            return all;
        }

        // ---------- Interrupt on a phone ----------

        // From the link's read worker.
        private void RemoteInterrupted(string device, string re) { Post(delegate { RemoteInterrupt(device, re); }); }

        // Interrupt on a phone (call.hush): her answer to that phone's message stops at once, as if they'd talked over her at
        // the PC, with the pieces still to be voiced and what's still waiting to go to phones (her speaker's hush sends them
        // call.hush). With nothing of it still going, that phone just hears so. Anything else she's saying carries on, and
        // the phone keeps that quiet by itself.
        private void RemoteInterrupt(string device, string re)
        {
            RemoteCall call = remoteCall;
            RemoteAsker saying = remoteSaying;
            if (call == null || saying == null || remoteSayingTicket != asking || saying.Device != device || saying.Id != re) return;
            if (Voicing) BargeIn("phone interrupt");
            else call.Hush(device);
        }

        // ---------- Her smoke test: her answer on a phone's call ----------

        private sealed class CallSent
        {
            internal long At;
            internal string Device, Type;
            internal int Samples;
        }

        // A phone's lines on its call through her real ears hook, chat path and voice, with stand-in ears, brain and voice that
        // take about as long as hers (her voice 450 ms and 10 ms a letter, as her log shows ElevenLabs taking): her first words
        // reach the phone sooner a sentence at a time than voiced whole, the next sentence follows with no hush between them,
        // and the phone's Interrupt (call.hush) stops her answer there and on the PC with nothing more of it sent. An Interrupt
        // for an answer she's moved on from, one with no re and one from a phone that isn't on a call change nothing.
        private void SmokeRemoteCall(List<string> checks)
        {
            const string Phone = "phoneC";
            // Lines her bridge check already shows go to her brain rather than a tool of her own (like her weather).
            string[] lines = { "hello from my phone", "what are you up to", "second message" };
            string[] answers =
            {
                "Sure thing, here it is. It's clear all day today. Take a jacket tonight. The wind picks up late. The weekend looks sunny. So go for a walk.",
                "Okay, here you go then. Tomorrow is cloudy at first. It clears up by lunch. The evening stays warm. Rain comes on Friday. So enjoy the sun.",
                "Right, here's the weekend. Saturday is bright and warm. Sunday gets a little windy. Both nights stay dry. It's a good one to get out. So plan a trip."
            };
            foreach (string line in lines)
            {
                string tool;
                ToolArguments arguments;
                if (Hands.Plan(line).Key != null || Hands.Heard(line, out tool, out arguments)) throw new Exception("The call test's line \"" + line + "\" plans a tool of its own.");
            }
            SmokeCallPieces();

            RemoteSink sinkWas = remoteSink;
            RemoteCall callWas = remoteCall;
            Action<short[], int> tapWas = Speaker.Tap;
            Action hushedWas = Speaker.Hushed;
            Func<string, string> thinkWas = Think;
            Func<string, byte[]> speakWas = Speak, freeWas = FreeSpeak;
            bool silentWas = preferences.Silent, quietWas = preferences.Quiet, hdWas = preferences.HdVoice, servedWas = preferences.ServerVoice;
            bool chattingWas = chatting, localWas = local, hiddenWas = hidden, shushedWas = shushed, dockedWas = docked, piecesWas = remotePieces;
            Job jobWas = job;
            string workingWas = working;
            int pitchWas = VoiceFx.Pitch, speedWas = VoiceFx.Speed, robotWas = VoiceFx.Robot, glitchWas = VoiceFx.Glitch;
            VoiceFx.Layering layersWas = VoiceFx.Layers;
            int waitWas = VoiceWait, freeWaitWas = FreeWait;
            bool signalsWas = signals.Enabled;
            List<CallSent> sent = new List<CallSent>();
            List<long> timed = new List<long>();
            List<string> voiced = new List<string>();
            int voicing = 0, heard = 0, thought = 0;
            RemoteLink link = new RemoteLink(RemoteState.Fresh(), RemoteLink.RelayBase(null), "Smoke PC", "capri/smoke", null);
            RemoteCall call = new RemoteCall(link, delegate
            {
                Thread.Sleep(300);
                return lines[Math.Min(lines.Length - 1, Interlocked.Increment(ref heard) - 1)];
            }, RemoteHeard);
            call.Interrupted = RemoteInterrupted;
            call.Timed = delegate(long ms, string words) { lock (timed) timed.Add(ms); };
            call.Sending = delegate(string device, string message)
            {
                RemoteJson.Map map = RemoteJson.ParseMap(message);
                string type = map == null ? null : map.Text("type");
                byte[] pcm = type == "call.voice" ? RemoteCrypto.FromB64u(map.Text("pcm") ?? "") : null;
                lock (sent) sent.Add(new CallSent { At = Stopwatch.GetTimestamp(), Device = device, Type = type, Samples = pcm == null ? 0 : pcm.Length / 2 });
            };
            // Her voice: 450 ms and 10 ms a letter to make, and 12 letters a second to say.
            Func<string, byte[]> standIn = delegate(string words)
            {
                Interlocked.Increment(ref voicing);
                try
                {
                    lock (voiced) voiced.Add(words);
                    int letters = Math.Max(1, VoiceServer.Letters(words));
                    Thread.Sleep(450 + 10 * letters);
                    short[] samples = new short[24000 * letters / 12];
                    for (int i = 0; i < samples.Length; i++) samples[i] = (short)(6000 * Math.Sin(i * 0.06));
                    return Voice.Wave(samples, 24000);
                }
                finally { Interlocked.Decrement(ref voicing); }
            };
            try
            {
                // Nothing of her own, like a check-in, speaks up in the quiet between her answers.
                signals.Stop();
                CloseTalk();
                voiceChanges.Clear();
                ownLines.Clear();
                voicePreview = null;
                hidden = shushed = docked = chatting = local = false;
                preferences.Quiet = preferences.Silent = preferences.HdVoice = preferences.ServerVoice = false;
                job = null;
                working = null;
                VoiceWait = 4000;
                FreeWait = 8000;
                VoiceFx.Pitch = VoiceFx.Robot = VoiceFx.Glitch = 0;
                VoiceFx.Speed = 100;
                VoiceFx.Layers = VoiceFx.Layering.Off;
                Think = delegate
                {
                    Thread.Sleep(300);
                    return answers[Math.Min(answers.Length - 1, Interlocked.Increment(ref thought) - 1)];
                };
                Speak = FreeSpeak = standIn;
                remoteSink = new RemoteTap();
                remoteCall = call;
                Speaker.Tap = RemoteVoiceTap;
                Speaker.Hushed = RemoteVoiceHushed;
                voiceInTest = true;
                string[] features = link.FeaturesNow;
                if (Array.IndexOf(features, "call") < 0 || Array.IndexOf(features, RemoteCall.HushFeature) < 0) throw new Exception("Her hello-ok offers " + string.Join(", ", features) + ", not call and " + RemoteCall.HushFeature + ".");
                call.Heard(Phone, "{\"type\":\"call.start\",\"hush\":true}");
                call.Heard("phoneOld", "{\"type\":\"call.start\"}");
                if (!call.OnCall(Phone) || !call.Hushes(Phone) || !call.OnCall("phoneOld") || call.Hushes("phoneOld")) throw new Exception("A phone's call.start didn't put it on a call, or said wrongly whether it stops her with call.hush.");
                // An older phone (no hush) gets her answers whole, so its own Interrupt still drops all of what's left.
                call.End("phoneOld", null);

                // Before: her answer voiced whole, as it is off a call.
                remotePieces = false;
                int at;
                long whole = SmokeCallLine(call, sent, Phone, "c1", out at);
                lock (voiced) if (voiced.Count != 1) throw new Exception("Her answer voiced whole took " + voiced.Count + " goes of her voice.");
                CloseTalk();
                SmokeCallWait(delegate { return Volatile.Read(ref voicing) == 0; }, 6000, "Her stand-in voice never finished her first answer.");

                // After: a sentence at a time. Her first piece goes as soon as it's made, and the next follows it with no hush.
                remotePieces = true;
                List<string> pieces = RemoteCall.Pieces(answers[1]);
                long first = SmokeCallLine(call, sent, Phone, "c2", out at);
                int firstPart = at;
                lock (voiced)
                    if (voiced.Count < 2 || voiced[1].Length > answers[1].Length / 2) throw new Exception("Her answer on a call was voiced as \"" + (voiced.Count > 1 ? voiced[1] : "") + "\", not its first sentence.");
                if (first + 400 > whole) throw new Exception("Her first words on a call took " + first + " ms a sentence at a time and " + whole + " ms voiced whole.");
                int twoPieces = 0;
                foreach (string piece in pieces.GetRange(0, 2)) twoPieces += 24000 * Math.Max(1, VoiceServer.Letters(piece)) / 12;
                SmokeCallWait(delegate { return SmokeCallSamples(sent, firstPart, Phone) >= twoPieces * 9 / 10; }, 6000, "Her answer's second sentence never followed the first to the phone.");
                if (SmokeCallFind(sent, firstPart, "call.hush", Phone) >= 0) throw new Exception("Her answer's next sentence hushed the phone before it played.");
                if (voiceUntil <= clock.Elapsed.TotalSeconds) throw new Exception("Her answer's sentences didn't run on one after another.");

                // The phone's Interrupt: she stops at once, and nothing more of that answer goes, not even the piece she was making.
                int stopAt;
                lock (sent) stopAt = sent.Count;
                call.Heard(Phone, "{\"type\":\"call.hush\",\"re\":\"c2\"}");
                SmokeCallWait(delegate { return SmokeCallFind(sent, stopAt, "call.hush", Phone) >= 0; }, 3000, "The phone's Interrupt didn't stop her answer.");
                int hushAt = SmokeCallFind(sent, stopAt, "call.hush", Phone);
                SmokeCallWait(delegate { return Volatile.Read(ref voicing) == 0; }, 4000, "Her stand-in voice kept going after the phone's Interrupt.");
                SmokeCallPump(400);
                int made;
                lock (voiced) made = voiced.Count - 1;
                if (SmokeCallFind(sent, hushAt + 1, "call.voice", Phone) >= 0) throw new Exception("More of her answer went to the phone after its Interrupt.");
                if (made >= pieces.Count) throw new Exception("Her voice made all " + pieces.Count + " sentences of an answer the phone interrupted.");
                if (voice.Busy || voiceUntil > 0) throw new Exception("The phone's Interrupt left her talking on the PC.");
                CloseTalk();

                // An Interrupt for her last answer, one with no re and one from a phone that isn't on a call leave this one alone.
                long third = SmokeCallLine(call, sent, Phone, "c3", out at);
                int staleAt;
                lock (sent) staleAt = sent.Count;
                call.Heard(Phone, "{\"type\":\"call.hush\",\"re\":\"c2\"}");
                call.Heard("phoneZ", "{\"type\":\"call.hush\",\"re\":\"c3\"}");
                call.Heard(Phone, "{\"type\":\"call.hush\"}");
                SmokeCallPump(500);
                int bad = SmokeCallFind(sent, staleAt, "error", Phone);
                if (bad < 0) throw new Exception("A call.hush with no re got no bad-message.");
                if (SmokeCallFind(sent, staleAt, "call.hush", null) >= 0 || SmokeCallFind(sent, staleAt, null, "phoneZ") >= 0) throw new Exception("An Interrupt for another answer, or from a phone that isn't on a call, hushed her.");
                if (!Voicing) throw new Exception("An Interrupt for another answer stopped her.");
                lock (sent) staleAt = sent.Count;
                call.Heard(Phone, "{\"type\":\"call.hush\",\"re\":\"c3\"}");
                SmokeCallWait(delegate { return SmokeCallFind(sent, staleAt, "call.hush", Phone) >= 0; }, 3000, "The phone's Interrupt for her answer now didn't stop it.");
                long[] times;
                lock (timed) times = timed.ToArray();
                if (times.Length != 3) throw new Exception("Her log timed " + times.Length + " of her 3 answers on a call.");
                checks.Add("PASS on a phone's call her answer goes a sentence at a time: her first words reach the phone " + first + " ms after its line ends, against " + whole + " ms voiced whole (" + third
                    + " ms for the next answer; her log said " + string.Join(", ", Array.ConvertAll<long, string>(times, delegate(long ms) { return ms.ToString(CultureInfo.InvariantCulture); })) + " ms), the sentences follow one another with no hush, and the phone's Interrupt stops her there and on the PC with nothing more sent (" + made + " of " + pieces.Count
                    + " sentences made); an Interrupt for an answer she's moved on from, or from a phone not on a call, leaves her talking, and one with no re is a bad-message");
            }
            finally
            {
                CloseTalk();
                voiceInTest = false;
                remotePieces = piecesWas;
                call.Dispose();
                remoteCall = callWas;
                remoteSink = sinkWas;
                Speaker.Tap = tapWas;
                Speaker.Hushed = hushedWas;
                remoteSaying = null;
                remoteSayingTicket = -1;
                Think = thinkWas;
                Speak = speakWas;
                FreeSpeak = freeWas;
                preferences.Silent = silentWas;
                preferences.Quiet = quietWas;
                preferences.HdVoice = hdWas;
                preferences.ServerVoice = servedWas;
                chatting = chattingWas;
                local = localWas;
                hidden = hiddenWas;
                shushed = shushedWas;
                docked = dockedWas;
                job = jobWas;
                working = workingWas;
                VoiceFx.Pitch = pitchWas;
                VoiceFx.Speed = speedWas;
                VoiceFx.Robot = robotWas;
                VoiceFx.Glitch = glitchWas;
                VoiceFx.Layers = layersWas;
                VoiceWait = waitWas;
                FreeWait = freeWaitWas;
                signals.Enabled = signalsWas;
                lock (remoteGate)
                {
                    remoteTickets.Clear();
                    remoteAsides.Clear();
                }
            }
        }

        // Her answer's pieces on a call: a sentence each, a short one joined to the next, a short or wordless last bit kept
        // with the one before, decimals left whole, and at most RemoteCall.MostPieces.
        private static void SmokeCallPieces()
        {
            string[] cases =
            {
                "One sentence only.", "One sentence only.",
                "Oh. That's a lovely idea. Let's do it!", "Oh. That's a lovely idea.|Let's do it!",
                "It costs 3.50 today. Want it?", "It costs 3.50 today. Want it?",
                "Wait... what? No way! That's wild.", "Wait... what?|No way! That's wild.",
                "That's all I have. ...", "That's all I have. ...",
                "Done. :)", "Done. :)"
            };
            for (int i = 0; i < cases.Length; i += 2)
            {
                string got = string.Join("|", RemoteCall.Pieces(cases[i]).ToArray());
                if (got != cases[i + 1]) throw new Exception("\"" + cases[i] + "\" came in pieces as \"" + got + "\", not \"" + cases[i + 1] + "\".");
            }
            List<string> sentences = new List<string>();
            for (int i = 1; i <= 12; i++) sentences.Add("Sentence number " + i + " is here.");
            string all = string.Join(" ", sentences.ToArray());
            List<string> many = RemoteCall.Pieces(all);
            if (many.Count != RemoteCall.MostPieces || string.Join(" ", many.ToArray()) != all) throw new Exception("Twelve sentences came in " + many.Count + " pieces, not " + RemoteCall.MostPieces + " holding them all.");
        }

        // A phone's line on its call, as one call.audio part with end. Gives how long after it her first words went to the
        // phone, and where in sent that part is.
        private static long SmokeCallLine(RemoteCall call, List<CallSent> sent, string phone, string id, out int at)
        {
            int from;
            lock (sent) from = sent.Count;
            byte[] pcm = new byte[19200];
            for (int i = 0; i < pcm.Length / 2; i++)
            {
                short sample = (short)(6000 * Math.Sin(i * 0.05));
                pcm[i * 2] = (byte)(sample & 0xff);
                pcm[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
            string part = new RemoteJson.Writer().Text("type", "call.audio").Text("id", id).Number("n", 0).Number("rate", RemoteCall.Rate).Text("pcm", RemoteCrypto.B64u(pcm)).Flag("end", true).ToString();
            long ended = Stopwatch.GetTimestamp();
            call.Heard(phone, part);
            SmokeCallWait(delegate { return SmokeCallFind(sent, from, "call.voice", phone) >= 0; }, 8000, "Her answer to the phone's line " + id + " never reached it.");
            if (SmokeCallFind(sent, from, "call.heard", phone) < 0) throw new Exception("The phone's line " + id + " got no call.heard.");
            at = SmokeCallFind(sent, from, "call.voice", phone);
            lock (sent) return (sent[at].At - ended) * 1000 / Stopwatch.Frequency;
        }

        // The first message from from on of type (any type when null) to device (any device when null), or -1.
        private static int SmokeCallFind(List<CallSent> sent, int from, string type, string device)
        {
            lock (sent)
                for (int i = Math.Max(0, from); i < sent.Count; i++)
                    if ((type == null || sent[i].Type == type) && (device == null || sent[i].Device == device)) return i;
            return -1;
        }

        // Her voice's samples that went to device from from on.
        private static int SmokeCallSamples(List<CallSent> sent, int from, string device)
        {
            int total = 0;
            lock (sent)
                for (int i = Math.Max(0, from); i < sent.Count; i++)
                    if (sent[i].Device == device) total += sent[i].Samples;
            return total;
        }

        private static void SmokeCallWait(Func<bool> done, int ms, string problem)
        {
            Stopwatch waited = Stopwatch.StartNew();
            while (!done() && waited.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
            if (!done()) throw new Exception(problem);
        }

        private static void SmokeCallPump(int ms)
        {
            Stopwatch waited = Stopwatch.StartNew();
            while (waited.ElapsedMilliseconds < ms)
            {
                Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        // ---------- Auto: the monitor she's looking at ----------

        // Where her hands are while she works the computer; otherwise the monitor her glances watch, when Watch my screen has
        // one picked; otherwise the one with their window in front, which is where she looks when asked (LookAt).
        private void RemoteLook()
        {
            try
            {
                Rectangle area;
                WatchScreen picked = preferences.Watch ? WatchedScreen() : null;
                if (herPointer.Visible) area = new Rectangle(herPointer.Tip, new Size(1, 1));
                else if (picked != null) area = picked.Bounds;
                else area = CaptureArea(Native.GetForegroundWindow());
                Point middle = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
                string found = null;
                foreach (RemoteMonitor monitor in RemoteDesktop.List()) if (monitor.Bounds.Contains(middle)) found = monitor.Id;
                remoteLooking = found;
            }
            catch (Exception) { }
        }

        private sealed class RemoteDesktop : RemoteScreenSource
        {
            private readonly Companion her;

            internal RemoteDesktop(Companion her) { this.her = her; }

            internal static List<RemoteMonitor> List()
            {
                List<KeyValuePair<string, Rectangle>> screens = new List<KeyValuePair<string, Rectangle>>();
                foreach (Screen screen in Screen.AllScreens) screens.Add(new KeyValuePair<string, Rectangle>(screen.DeviceName, screen.Bounds));
                Screen main = Screen.PrimaryScreen;
                return RemoteScreen.Numbered(screens, main == null ? null : main.DeviceName);
            }

            internal override List<RemoteMonitor> Monitors() { return List(); }

            internal override string Looking() { return her.remoteLooking; }

            internal override Point Mouse() { return Cursor.Position; }

            internal override RemoteCapture Capture() { return new RemoteGrab(); }
        }

        // One monitor through FrameGrab: Desktop Duplication, which shows her and every other window and says when nothing
        // changed, or a plain screen copy where that isn't there. Frames that come out the same as the last are dropped.
        internal sealed class RemoteGrab : RemoteCapture
        {
            private FrameGrab grab;
            private int lastWidth;
            private ulong lastPrint;
            private bool any;

            internal override bool Open(RemoteMonitor monitor)
            {
                try
                {
                    if (grab == null) grab = new FrameGrab();
                    grab.Open(monitor.Bounds);
                }
                catch (Exception) { return false; }
                any = false;
                lastWidth = 0;
                return true;
            }

            internal string Live { get { return grab == null ? "Closed" : grab.Live; } }

            internal override Bitmap Next(int maxWidth)
            {
                if (grab == null) return null;
                bool fresh;
                try { fresh = grab.Next(0); }
                catch (Exception) { return null; }
                if (!fresh && any && maxWidth == lastWidth) return null;
                Bitmap frame;
                try { frame = grab.Copy(maxWidth); }
                catch (Exception) { return null; }
                if (frame == null) return null;
                ulong print = Print(frame);
                if (any && maxWidth == lastWidth && print == lastPrint)
                {
                    frame.Dispose();
                    return null;
                }
                any = true;
                lastWidth = maxWidth;
                lastPrint = print;
                return frame;
            }

            // FNV-1a over every pixel, to tell a frame that changed from one that didn't.
            private static unsafe ulong Print(Bitmap frame)
            {
                BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    ulong hash = 14695981039346656037UL;
                    for (int row = 0; row < data.Height; row++)
                    {
                        uint* pixel = (uint*)((byte*)data.Scan0 + (long)row * data.Stride);
                        for (int column = 0; column < data.Width; column++) hash = (hash ^ (pixel[column] & 0xFFFFFFu)) * 1099511628211UL;
                    }
                    return hash;
                }
                finally { frame.UnlockBits(data); }
            }

            public override void Dispose()
            {
                FrameGrab was = grab;
                grab = null;
                if (was != null) was.Dispose();
            }
        }

        // ---------- The badge ----------

        // On her thread: shown on the monitor a phone watches (or the main one for a call alone) while any phone watches or
        // calls, gone otherwise. Auto's look runs only while it's up.
        private void RemoteBadgeShow()
        {
            RemoteScreen screen = remoteScreen;
            RemoteCall call = remoteCall;
            List<RemoteWatcher> watching = screen == null ? new List<RemoteWatcher>() : screen.Watchers();
            List<RemoteWatcher> calling = call == null ? new List<RemoteWatcher>() : call.Callers();
            bool hearing = call != null && call.Hearing;
            if (!IsDisposed) RemoteEarsFor(hearing);
            if (watching.Count > 0)
            {
                if (remoteLookTimer == null)
                {
                    remoteLookTimer = new System.Windows.Forms.Timer { Interval = 500 };
                    remoteLookTimer.Tick += delegate { RemoteLook(); };
                }
                if (!remoteLookTimer.Enabled)
                {
                    RemoteLook();
                    remoteLookTimer.Start();
                }
            }
            else if (remoteLookTimer != null) remoteLookTimer.Stop();
            if (watching.Count == 0 && calling.Count == 0 || IsDisposed)
            {
                if (remoteBadge != null && !remoteBadge.IsDisposed) remoteBadge.Hide();
                return;
            }
            HashSet<string> phones = new HashSet<string>(StringComparer.Ordinal);
            string name = null;
            foreach (RemoteWatcher one in watching) { phones.Add(one.Device); if (name == null) name = one.Name; }
            foreach (RemoteWatcher one in calling) { phones.Add(one.Device); if (name == null) name = one.Name; }
            RemoteMonitor monitor = null;
            foreach (RemoteWatcher one in watching) if (monitor == null && one.Monitor != null) monitor = one.Monitor;
            Rectangle area = monitor != null ? Screen.FromRectangle(monitor.Bounds).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            string lead = BadgeLead(watching.Count > 0, calling.Count > 0, hearing);
            string who = phones.Count > 1 ? phones.Count + " PHONES" : (name ?? "PHONE").ToUpperInvariant();
            if (remoteBadge == null || remoteBadge.IsDisposed) remoteBadge = new RemoteBadge();
            remoteBadge.Put(lead, who, area);
        }

        private static string BadgeLead(bool watching, bool calling, bool hearing)
        {
            if (calling && !hearing) return "PHONE IS LISTENING ";
            return watching && calling ? "PHONE WATCHING AND ON A CALL " : watching ? "PHONE IS WATCHING " : "PHONE IS ON A CALL ";
        }

        // ---------- Smoke ----------

        // The live parts that run without a relay: the badge drawn at 100%, 150% and 200% to remote-watch-*.png, her monitors
        // numbered, Auto's look naming one of them, and one frame from a monitor's capture where this desktop gives one.
        private void SmokeRemoteLive(List<string> checks, string output)
        {
            foreach (float zoom in new[] { 1f, 1.5f, 2f })
                using (Bitmap badges = new Bitmap((int)(2800 * zoom), (int)(50 * zoom)))
                using (Graphics draw = Graphics.FromImage(badges))
                {
                    int at = 0;
                    foreach (string kind in new[] { "watch", "call", "both", "listen" })
                        using (Bitmap badge = RemoteBadge.Draw(BadgeLead(kind == "watch" || kind == "both", kind != "watch", kind == "call" || kind == "both"), "LACHY'S PIXEL 9 PRO XL", zoom))
                        {
                            if (badge.Width < 200 * zoom || badge.Height < 20 * zoom || badge.Width > 700 * zoom || badge.Height > badges.Height) throw new Exception("The " + kind + " badge came out " + badge.Width + " x " + badge.Height + " at " + (int)(zoom * 100) + "%.");
                            badge.Save(Path.Combine(output, "remote-" + kind + "-" + (int)(zoom * 100) + ".png"), ImageFormat.Png);
                            draw.DrawImageUnscaled(badge, at, 0);
                            at += (int)(700 * zoom);
                        }
                    badges.Save(Path.Combine(output, "remote-badges-" + (int)(zoom * 100) + ".png"), ImageFormat.Png);
                }
            List<RemoteMonitor> monitors = RemoteDesktop.List();
            if (monitors.Count == 0) throw new Exception("Capri Remote found no monitors to show.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RemoteMonitor monitor in monitors) if (!ids.Add(monitor.Id) || monitor.Name != "Monitor " + monitor.Id) throw new Exception("Monitor " + monitor.Id + " is named twice or wrongly.");
            remoteLooking = null;
            RemoteLook();
            if (remoteLooking == null || !ids.Contains(remoteLooking)) throw new Exception("Auto looked at no monitor (" + (remoteLooking ?? "none") + ").");
            string frame;
            using (RemoteGrab grab = new RemoteGrab())
            {
                if (!grab.Open(monitors[0])) frame = "LIMIT a phone's capture couldn't open " + monitors[0].Name + " on this desktop";
                else
                {
                    Bitmap shot = null;
                    for (int tries = 0; tries < 10 && shot == null; tries++)
                    {
                        shot = grab.Next(640);
                        if (shot == null) System.Threading.Thread.Sleep(50);
                    }
                    if (shot == null) frame = "LIMIT a phone's capture of " + monitors[0].Name + " gave no frame on this desktop (" + grab.Live + ")";
                    else
                    {
                        using (shot)
                        {
                            if (shot.Width > 640 || shot.Width < Math.Min(160, monitors[0].Bounds.Width)) throw new Exception("A phone's frame came out " + shot.Width + " wide.");
                            byte[] jpeg = RemoteScreen.Jpeg(shot, RemoteQuality.Balanced.Jpeg);
                            if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new Exception("A phone's frame isn't a JPEG.");
                            // A still screen gives nothing more to send (a desktop that's animating may, so this is only said).
                            bool still;
                            using (Bitmap again = grab.Next(640)) still = again == null;
                            frame = "PASS a phone's capture of " + monitors[0].Name + " gives a " + shot.Width + " x " + shot.Height + " JPEG of " + jpeg.Length + " bytes (" + grab.Live + ")" + (still ? ", and nothing more while it stays the same" : "");
                        }
                    }
                }
            }
            checks.Add(frame);
            checks.Add("PASS Capri Remote's watching, call and listening badges fit at 100%, 150% and 200%, " + monitors.Count + (monitors.Count == 1 ? " monitor is" : " monitors are") + " numbered as Windows counts them, and Auto looks at one of them");
        }
    }

    // The badge on the PC while a phone watches or calls: the CAPRI IS CONTROLLING badge's slab with her face, at the top
    // right of the monitor being watched. Clicks go through it, and screen captures and recordings leave it out.
    internal sealed class RemoteBadge : Overlay
    {
        private string shownLead, shownWho;
        private Rectangle shownArea;

        internal RemoteBadge() : base("Capri Remote") { ClickThrough = true; }

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WDA_EXCLUDEFROMCAPTURE (Windows 10 2004 on): not in the phone's frames, nor his stream. Older Windows shows it.
            try { SetWindowDisplayAffinity(Handle, 0x11); }
            catch (Exception) { }
        }

        internal void Put(string lead, string who, Rectangle area)
        {
            if (Visible && lead == shownLead && who == shownWho && area == shownArea) return;
            float scale;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, screen.DpiX / 96f);
            Bitmap picture = Draw(lead, who, scale);
            int margin = (int)Math.Round(12 * scale);
            Point at = new Point(area.Right - picture.Width - margin, area.Top + margin);
            shownLead = lead;
            shownWho = who;
            shownArea = area;
            Present(picture, at);
        }

        internal static Bitmap Draw(string lead, string who, float scale)
        {
            string name = RemoteMessages.Clip(who, 32);
            using (Font font = Look.Mono(11 * scale, FontStyle.Bold))
            {
                float tall = 26 * scale, icon = 20 * scale, pad = 7 * scale, wide;
                using (Bitmap measure = new Bitmap(1, 1))
                using (Graphics graphics = Graphics.FromImage(measure))
                    wide = pad + icon + pad + graphics.MeasureString(lead, font, PointF.Empty, Takeover.Spaced).Width + graphics.MeasureString(name, font, PointF.Empty, Takeover.Spaced).Width + pad * 1.4f;
                int side = (int)Math.Ceiling(4 * scale);
                Bitmap picture = new Bitmap((int)Math.Ceiling(wide) + side * 2, (int)Math.Ceiling(tall) + side * 2, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(picture))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    RectangleF slab = new RectangleF(side, side, wide, tall);
                    RectangleF iconAt = new RectangleF(slab.Left + pad, slab.Top + (tall - icon) / 2, icon, icon);
                    Takeover.Badge(graphics, slab, iconAt, lead, name, font, pad, icon, 1, 1, Takeover.Face, false, new Random(1), scale);
                }
                return picture;
            }
        }
    }
}
