using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Preferences
    {
        // SOUNDS (Lachy, 12 Nov). Her chimes are on until they turn them off, so they're saved switched off. A volume of 0 was
        // never set, which is 40%.
        [DataMember] public bool NoChimes;
        [DataMember] public bool NoChimeSent;
        [DataMember] public bool NoChimeHeard;
        [DataMember] public bool NoChimeListening;
        [DataMember] public int ChimeVolume;
    }

    internal sealed partial class Companion
    {
        // Her chimes have a speaker of their own: Plain, so they never go into her voice in their mic or Capri Remote's call, and
        // apart from her voice's, so one never clears or cuts her off. Like her other speakers it plays on the speaker picked in
        // VOICE, which is Windows' default unless they chose one.
        private Speaker chimeSpeaker;
        // Free voice chat chimed as the line ended, so the words coming back don't chime it again.
        private bool lineChimed;

        private void WireChimes()
        {
            chimeSpeaker = new Speaker(testing) { Plain = true };
            ShowChimes();
            Chimes.Out = delegate(short[] sound)
            {
                if (IsDisposed) return;
                try { chimeSpeaker.Open(Chimes.Rate); chimeSpeaker.Play(sound); }
                catch (Exception) { }
            };
            Chimes.Left = delegate { return chimeSpeaker.Left; };
            ThreadPool.QueueUserWorkItem(delegate { Chimes.Warm(); });
        }

        private void CloseChimes() { if (chimeSpeaker != null) chimeSpeaker.Close(); }

        private void SentChime(string text) { if (!string.IsNullOrWhiteSpace(text)) Chimes.Play(Chime.Sent); }

        private void ShowChimes()
        {
            Chimes.On = !preferences.NoChimes;
            Chimes.Volume = ChimeVolume;
            Chimes.Want(Chime.Sent, !preferences.NoChimeSent);
            Chimes.Want(Chime.Heard, !preferences.NoChimeHeard);
            Chimes.Want(Chime.Listening, !preferences.NoChimeListening);
        }

        internal bool ChimesOn { get { return !preferences.NoChimes; } }

        internal int ChimeVolume { get { int volume = preferences.ChimeVolume; return volume <= 0 ? Chimes.DefaultVolume : Math.Max(10, Math.Min(100, volume / 10 * 10)); } }

        internal bool ChimeOn(Chime chime) { return chime == Chime.Sent ? !preferences.NoChimeSent : chime == Chime.Heard ? !preferences.NoChimeHeard : !preferences.NoChimeListening; }

        internal void ToggleChimes()
        {
            preferences.NoChimes = !preferences.NoChimes;
            ShowChimes();
            SavePreferences();
            mainframe.Sync();
        }

        internal void ToggleChime(Chime chime)
        {
            if (chime == Chime.Sent) preferences.NoChimeSent = !preferences.NoChimeSent;
            else if (chime == Chime.Heard) preferences.NoChimeHeard = !preferences.NoChimeHeard;
            else preferences.NoChimeListening = !preferences.NoChimeListening;
            ShowChimes();
            SavePreferences();
            mainframe.Sync();
        }

        internal void SetChimeVolume(int percent)
        {
            preferences.ChimeVolume = Math.Max(10, Math.Min(100, percent / 10 * 10));
            ShowChimes();
            SavePreferences();
        }

        // Letting go of Chime volume plays the heard chime at the new volume, so they hear what they picked.
        internal void PreviewChime() { Chimes.Play(Chime.Heard, true); }

        private bool? ChimeSettingOn(string name)
        {
            switch (name)
            {
                case "chimes": return ChimesOn;
                case "chime_sent": return ChimeOn(Chime.Sent);
                case "chime_heard": return ChimeOn(Chime.Heard);
                case "chime_listening": return ChimeOn(Chime.Listening);
                default: return null;
            }
        }

        private void SetChimeSetting(string name)
        {
            switch (name)
            {
                case "chimes": ToggleChimes(); break;
                case "chime_sent": ToggleChime(Chime.Sent); break;
                case "chime_heard": ToggleChime(Chime.Heard); break;
                case "chime_listening": ToggleChime(Chime.Listening); break;
            }
        }

        // Her chimes, made, played, switched and kept out of her ears, with stand-in heads, mics and speakers. Every speaker here
        // is a silent one and every chime goes into a list, so nothing plays on the PC's speakers.
        private void SmokeSounds(List<string> checks, string output)
        {
            List<string> table = new List<string>();
            for (int set = 0; set < Chimes.Sets.Length; set++)
            {
                for (int kind = 0; kind < 3; kind++)
                {
                    short[] full = Chimes.Render(set, (Chime)kind, 100), soft = Chimes.Render(set, (Chime)kind, Chimes.DefaultVolume);
                    int peak = 0, softPeak = 0;
                    foreach (short sample in full) peak = Math.Max(peak, Math.Abs((int)sample));
                    foreach (short sample in soft) softPeak = Math.Max(softPeak, Math.Abs((int)sample));
                    int ms = full.Length * 1000 / Chimes.Rate;
                    string name = Chimes.Sets[set] + " " + Chimes.Names[kind];
                    if (ms > 400 || peak >= 32000 || peak < 4000 || full[0] != 0 || full[full.Length - 1] != 0 || soft[soft.Length - 1] != 0) throw new Exception("The " + name + " chime is too long, clips, is silent or clicks at an end: " + ms + " ms, peak " + peak);
                    double ratio = (double)softPeak / peak;
                    if (Math.Abs(ratio - Chimes.Gain(Chimes.DefaultVolume)) > 0.01) throw new Exception("The " + name + " chime's volume curve is off: " + ratio);
                    table.Add(name + " " + ms + " ms, peak " + (20 * Math.Log10(peak / 32768.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " dBFS");
                }
                double alike = Alike(Chimes.Render(set, Chime.Sent, 100), Chimes.Render(set, Chime.Heard, 100));
                if (alike > 0.8) throw new Exception("The " + Chimes.Sets[set] + " set's sent and heard chimes sound too alike: " + alike);
            }
            checks.Add("PASS her chimes: all three sets made in code, none longer than 400 ms, none clips or starts or ends off zero, 40% is the volume curve's 12 dB down, and each set's sent and heard chimes differ (" + string.Join("; ", table.ToArray()) + ")");

            using (MemoryStream old = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")))
            {
                Preferences older = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(old);
                foreach (Preferences start in new[] { older, new Preferences(), Preferences.Fresh() })
                    if (start.NoChimes || start.NoChimeSent || start.NoChimeHeard || start.NoChimeListening || start.ChimeVolume != 0) throw new Exception("Her chimes didn't start on at 40% in new or older settings.");
            }

            Action<short[]> outWas = Chimes.Out;
            Func<double> leftWas = Chimes.Left;
            Func<long> clockWas = Chimes.Clock;
            bool offWas = preferences.NoChimes, sentWas = preferences.NoChimeSent, heardWas = preferences.NoChimeHeard, listeningWas = preferences.NoChimeListening;
            int volumeWas = preferences.ChimeVolume;
            bool advancedWas = preferences.AdvancedSettings, pageWas = mainframe.SettingsShown;
            long now = 1000000;
            double ringing = 0;
            List<short[]> played = new List<short[]>();
            Func<int> count = delegate { lock (played) return played.Count; };
            Action later = delegate { now += 2000; };
            try
            {
                Chimes.Clock = delegate { return now; };
                Chimes.Left = delegate { return ringing; };
                Chimes.Out = delegate(short[] sound) { lock (played) played.Add(sound); };
                Chimes.Reset();
                preferences.NoChimes = preferences.NoChimeSent = preferences.NoChimeHeard = preferences.NoChimeListening = false;
                preferences.ChimeVolume = 0;
                ShowChimes();
                mainframe.Sync();

                bool first = Chimes.Play(Chime.Sent), twice = Chimes.Play(Chime.Sent);
                now += 400;
                bool again = Chimes.Play(Chime.Sent), other = Chimes.Play(Chime.Heard);
                now += 100;
                bool crowded = Chimes.Play(Chime.Listening);
                bool masking = Chimes.Masking;
                now += played[played.Count - 1].Length * 1000L / Chimes.Rate + Chimes.Echo + 1;
                bool unmasked = !Chimes.Masking;
                later();
                ringing = 0.2;
                bool stacked = Chimes.Play(Chime.Listening);
                ringing = 0;
                bool free = Chimes.Play(Chime.Listening);
                if (!first || twice || again || !other || crowded || !masking || !unmasked || stacked || !free || count() != 3) throw new Exception("Her chimes stacked or repeated: first " + first + ", twice " + twice + ", again " + again + ", other " + other + ", crowded " + crowded + ", mask " + masking + "/" + unmasked + ", stacked " + stacked + ", free " + free);
                if (!Same(played[0], Chimes.Render(Chimes.Glass, Chime.Sent, 40)) || !Same(played[1], Chimes.Render(Chimes.Glass, Chime.Heard, 40))) throw new Exception("A chime wasn't Glass at 40% out of the box.");
                checks.Add("PASS her chimes never stack or repeat fast: the same one waits 700 ms, any two 300 ms, one still ringing drops the next, and her ears' mask covers each chime and 700 ms after; Glass at 40% plays out of the box");

                // Her real chime speaker: silent in the smoke, Plain, and apart from her voice, which plays on through a chime.
                short[] tone = new short[Chimes.Rate / 2];
                for (int i = 0; i < tone.Length; i++) tone[i] = (short)(Math.Sin(i * 0.05) * 8000);
                speaker.Open(Chimes.Rate);
                speaker.Play(tone);
                long herEnd = speaker.SoundEnd;
                outWas(Chimes.Render(Chimes.Glass, Chime.Heard, 40));
                bool own = chimeSpeaker.Plain && chimeSpeaker != speaker && chimeSpeaker != effects && chimeSpeaker.Left > 0;
                bool kept = speaker.SoundEnd == herEnd && speaker.Left > 0;
                speaker.Clear();
                chimeSpeaker.Clear();
                if (!own || !kept) throw new Exception("Her chimes didn't get their own Plain speaker, or cut her voice: own " + own + ", kept " + kept);
                checks.Add("PASS her chimes play on their own Plain speaker, which stays out of her voice in your mic and Capri Remote, and a chime over her voice leaves it playing");

                // Mainframe's SOUNDS rows.
                mainframe.ShowSettings(true);
                if (mainframe.SoundsStatus != "on | 40% | sent heard listening | enabled") throw new Exception("SOUNDS didn't show her chimes on at 40%: " + mainframe.SoundsStatus);
                mainframe.FlipChimes();
                later();
                bool offPlayed = Chimes.Play(Chime.Sent) || Chimes.Play(Chime.Heard) || Chimes.Play(Chime.Listening);
                Preferences saved = LoadPreferences();
                if (ChimesOn || !saved.NoChimes || offPlayed || mainframe.SoundsStatus != "off | 40% | sent heard listening | disabled") throw new Exception("Chimes off didn't silence them, save, or grey their rows: " + mainframe.SoundsStatus + ", played " + offPlayed);
                mainframe.FlipChimes();
                mainframe.FlipChime(Chime.Heard);
                later();
                bool heardOff = Chimes.Play(Chime.Heard);
                later();
                bool sentOn = Chimes.Play(Chime.Sent);
                saved = LoadPreferences();
                if (heardOff || !sentOn || !saved.NoChimeHeard || saved.NoChimeSent || mainframe.SoundsStatus != "on | 40% | sent listening | enabled") throw new Exception("Heard you's switch didn't work on its own: " + mainframe.SoundsStatus);
                mainframe.FlipChime(Chime.Heard);
                mainframe.FlipChime(Chime.Sent);
                mainframe.FlipChime(Chime.Listening);
                later();
                bool sentOff = Chimes.Play(Chime.Sent);
                later();
                bool listeningOff = Chimes.Play(Chime.Listening);
                if (sentOff || listeningOff || mainframe.SoundsStatus != "on | 40% | heard | enabled") throw new Exception("Message sent's or Listening's switch didn't work: " + mainframe.SoundsStatus);
                mainframe.FlipChime(Chime.Sent);
                mainframe.FlipChime(Chime.Listening);
                mainframe.SlideChimeVolume(5);
                later();
                int before = count();
                Chimes.Play(Chime.Sent);
                if (ChimeVolume != 90 || LoadPreferences().ChimeVolume != 90 || count() != before + 1 || !Same(played[before], Chimes.Render(Chimes.Glass, Chime.Sent, 90)) || mainframe.SoundsStatus != "on | 90% | sent heard listening | enabled") throw new Exception("Chime volume didn't move, save or reach the chime: " + mainframe.SoundsStatus);
                mainframe.SlideChimeVolume(-20);
                if (ChimeVolume != 10 || mainframe.SoundsStatus != "on | 10% | sent heard listening | enabled") throw new Exception("Chime volume didn't stop at 10%: " + mainframe.SoundsStatus);
                mainframe.SlideChimeVolume(3);
                checks.Add("PASS SOUNDS in Mainframe: Chimes off silences every chime, greys its rows and saves; Message sent, Heard you and Listening each switch their own chime; Chime volume runs 10% to 100%, saves and sets the chime's level");

                string turned = Hands.Do("capri_settings", new ToolArguments { Action = "off", Name = "chimes" });
                bool toolOff = !ChimesOn && !mainframe.ChimesShown && LoadPreferences().NoChimes;
                string back = Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "chimes" });
                string heardLine = Hands.Do("capri_settings", new ToolArguments { Action = "off", Name = "chime_heard" });
                bool toolHeard = !ChimeOn(Chime.Heard) && mainframe.SoundsStatus.Contains("| sent listening |");
                Hands.Do("capri_settings", new ToolArguments { Action = "on", Name = "chime_heard" });
                string louder = Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "chime_volume", Level = "70%" });
                bool toolLoud = ChimeVolume == 70 && mainframe.SoundsStatus.StartsWith("on | 70% |", StringComparison.Ordinal);
                string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list" });
                Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "chime_volume", Level = "down" });
                bool toolDown = ChimeVolume == 60;
                if (turned != "Turned Chimes off." || !toolOff || back != "Turned Chimes on." || !ChimesOn || heardLine != "Turned Heard you chime off." || !toolHeard || !toolLoud || !toolDown || !listed.Contains("Chimes on") || !listed.Contains("Chime volume: 70%") || !listed.Contains("Listening chime on"))
                    throw new Exception("Her settings tool didn't handle her chimes: " + turned + " / " + back + " / " + heardLine + " / " + louder + " / " + listed);
                foreach (string label in new[] { "Chimes", "Chime volume", "Message sent", "Heard you", "Listening" })
                    if (SelfKnow.Setting("Mainframe.cs", label) == null) throw new Exception("SOUNDS lost its help: " + label);
                string help = SelfKnow.Matching("how do I turn off the chimes?");
                if (help == null || !help.Contains("Chimes:")) throw new Exception("Her help didn't find her chimes: " + help);
                checks.Add("PASS her settings tool turns Chimes and each chime on and off and sets Chime volume by step or word, keeping SOUNDS in step, and her help answers about chimes");
                preferences.ChimeVolume = 0;
                ShowChimes();
                mainframe.Sync();

                string reach = "";
                Action<bool> fits = delegate(bool advanced)
                {
                    mainframe.ShowSettings(true);
                    if (mainframe.AdvancedShown != advanced) throw new Exception("Settings didn't open " + (advanced ? "Advanced." : "Simple."));
                    string[] parts = mainframe.PageReach.Split('|');
                    reach += (advanced ? "Advanced " : "Simple ") + mainframe.PageReach + "; ";
                    if (!mainframe.SoundsInPage) throw new Exception("SOUNDS is off the page: " + reach);
                    if (Math.Max(int.Parse(parts[0].Trim()), int.Parse(parts[1].Trim())) > int.Parse(parts[2].Trim())) throw new Exception("The Settings page runs past its height: " + reach);
                    string squeezed = mainframe.Overlap() ?? mainframe.Outside();
                    if (squeezed != null) throw new Exception("SOUNDS crowded Settings: " + squeezed);
                    mainframe.Wheel(-80);
                    Application.DoEvents();
                    SnapMainframe(Path.Combine(output, advanced ? "mainframe-sounds-advanced.png" : "mainframe-sounds-simple.png"));
                    mainframe.Wheel(80);
                };
                // Shown for real, on the smoke's own desktop, so the page is laid out as they'd see it.
                bool shownWas = mainframe.Visible;
                FormWindowState stateWas = mainframe.WindowState;
                try
                {
                    mainframe.WindowState = FormWindowState.Normal;
                    mainframe.Show();
                    Application.DoEvents();
                    InSimpleSettings(delegate { fits(false); });
                    preferences.AdvancedSettings = true;
                    fits(true);
                }
                finally
                {
                    if (!shownWas) mainframe.Hide();
                    mainframe.WindowState = stateWas;
                }
                checks.Add("PASS SOUNDS shows under HABITS in Simple and Advanced Settings with nothing overlapping or past the page (" + reach.TrimEnd(' ', ';') + "); pictures in mainframe-sounds-simple.png and mainframe-sounds-advanced.png");
                preferences.AdvancedSettings = advancedWas;
                mainframe.ShowSettings(pageWas);

                SoundsHeard(checks, played, delegate { now += 2000; });
            }
            finally
            {
                Chimes.Out = outWas;
                Chimes.Left = leftWas;
                Chimes.Clock = clockWas;
                Chimes.Reset();
                preferences.NoChimes = offWas; preferences.NoChimeSent = sentWas; preferences.NoChimeHeard = heardWas; preferences.NoChimeListening = listeningWas;
                preferences.ChimeVolume = volumeWas;
                preferences.AdvancedSettings = advancedWas;
                ShowChimes();
                SavePreferences();
                mainframe.Sync();
                mainframe.ShowSettings(pageWas);
            }
        }

        // Every way of talking to her plays its chime once, through her real ears and hands with a stand-in brain, and her ears
        // never take a chime for them.
        private void SoundsHeard(List<string> checks, List<short[]> played, Action later)
        {
            bool listenWas = preferences.ListenCapri, toldWas = preferences.ListenCapriTold, freeToldWas = preferences.FreeChatTold;
            bool quietWas = preferences.Quiet, silentWas = preferences.Silent, freeChatWas = FreeChatOn;
            int typingWas = preferences.Typing;
            bool? ownWas = ownEarsTest;
            Func<Dictation> dictationWas = Dictation.Make;
            Func<string, string> thinkWas = Think;
            Func<string, byte[]> speakWas = Speak, freeSpeakWas = FreeSpeak;
            Func<short[], string> hearingWas = Hearing.Run;
            Job runningWas = job;
            string workingWas = working, transcriptWas = Brain.TranscriptPath;
            bool keepingWas = Brain.Keeping;
            Brain keeperWas = Brain.Keeper;
            bool heyWas = preferences.HeyCapri, wakeChecksWas = wakeChecks, wakeOpenWas = wakeOpen, wakeMutedWas = wakeMicMuted, shushedWas = shushed;
            IGameStage stageWas = Commentary.Stage;
            Dictation dictationOpen = null;
            int calls = 0, decoded = 0;
            string words = "what's for dinner";
            Func<int, short[]> wave = delegate(int amplitude)
            {
                short[] samples = new short[Ears.Chunk];
                for (int i = 0; i < samples.Length; i++) samples[i] = (short)(Math.Sin(i * Math.PI / 40) * amplitude);
                return samples;
            };
            Action<Func<bool>, string> wait = delegate(Func<bool> arrived, string what)
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (!arrived() && waited.ElapsedMilliseconds < 4000) { Application.DoEvents(); Thread.Sleep(5); }
                if (!arrived()) throw new Exception("Her stand-in reply didn't arrive after " + what + ".");
            };
            Func<int> count = delegate { lock (played) return played.Count; };
            List<string> steps = new List<string>();
            Action<string, int, Chime> expect = delegate(string what, int was, Chime chime)
            {
                int got = count() - was;
                bool right = got == 1 && Same(played[played.Count - 1], Chimes.Render(Chimes.Set, chime, Chimes.Volume));
                if (!right) throw new Exception(what + " played " + got + " chimes, wanting one " + chime + " chime.");
                steps.Add(what);
            };
            try
            {
                StopChat();
                CloseTalk();
                job = null;
                working = null;
                preferences.ListenCapri = false;
                preferences.ListenCapriTold = true;
                preferences.FreeChatTold = true;
                preferences.Quiet = preferences.Silent = true;
                preferences.Typing = 2;
                FreeChatOn = true;
                ownEarsTest = false;
                Brain.TranscriptPath = Path.Combine(Path.GetTempPath(), "capri-smoke-sounds-" + Process.GetCurrentProcess().Id);
                Directory.CreateDirectory(Brain.TranscriptPath);
                Brain.Keeping = true;
                Brain.Keeper = null;
                StandInDictation standIn = new StandInDictation();
                Dictation.Make = delegate { return standIn; };
                Think = delegate { Interlocked.Increment(ref calls); return "Got it."; };
                Speak = delegate { return null; };
                FreeSpeak = null;
                Hearing.Run = delegate { Interlocked.Increment(ref decoded); return words; };

                // Typed to her.
                later();
                int was = count(), callsWas = calls;
                typer.Send("hello there");
                expect("a typed message", was, Chime.Sent);
                wait(delegate { return calls == callsWas + 1; }, "a typed message");
                CloseTalk();

                // Free voice chat, through Windows speech.
                ToggleChat();
                if (!chatting || !local || !earsReady) throw new Exception("Free voice chat didn't open her ears: chatting " + chatting + ", local " + local + ", ready " + earsReady);
                later();
                was = count(); callsWas = calls;
                if (standIn.Heard == null || dictation != standIn) throw new Exception("Free voice chat didn't open Windows speech for her ears.");
                standIn.Heard("what's for dinner");
                expect("free voice chat through Windows speech", was, Chime.Heard);
                wait(delegate { return calls == callsWas + 1; }, "Windows speech");
                CloseTalk();

                // Free voice chat through her own ears: the chime comes as the line ends, and once only when its words come back.
                dictationOpen = dictation;
                dictation = null;
                later();
                was = count(); callsWas = calls;
                int decodedWas = decoded;
                room = -1; lineAt = -100; noiseHold = false; nameOver = false;
                for (int i = 0; i < 10; i++) Gather(wave(40));
                for (int i = 0; i < 8; i++) Gather(wave(i % 2 == 0 ? 14000 : 20000));
                for (int i = 0; i < 12; i++) Gather(wave(40));
                wait(delegate { return count() > was; }, "her own ears' line");
                expect("free voice chat through her own ears, as the line ends", was, Chime.Heard);
                wait(delegate { return decoded == decodedWas + 1 && calls == callsWas + 1 && !transcribing; }, "her own ears' words");
                if (count() != was + 1) throw new Exception("Her own ears chimed again when the words came back.");
                CloseTalk();

                // Push to talk: the chime comes as they let go.
                later();
                was = count(); callsWas = calls;
                PushStart();
                for (int i = 0; i < 5; i++) Listen(wave(10000));
                PushDone();
                expect("push to talk, on letting go", was, Chime.Heard);
                wait(delegate { return calls == callsWas + 1; }, "push to talk");
                CloseTalk();

                // Listen for Capri: the room's chatter stays quiet, and her name chimes.
                preferences.ListenCapri = true;
                CloseNameChat();
                later();
                was = count();
                HeardLocally(chatTicket, asking, "i love pasta", null);
                if (count() != was) throw new Exception("Listen for Capri chimed at speech that wasn't for her.");
                callsWas = calls;
                HeardLocally(chatTicket, asking, "capri what time is it", null);
                expect("Listen for Capri, once it hears her name", was, Chime.Heard);
                wait(delegate { return calls == callsWas + 1; }, "Listen for Capri");
                CloseTalk();
                CloseNameChat();
                preferences.ListenCapri = false;

                // Her ears never take a chime for them: in the room, and over her own voice. Each has its control, the same echo
                // with no chime playing, which shows her ears were open and what the echo would have done.
                Func<string> earsState = delegate { return "shut " + earsShut + ", transcribing " + transcribing + ", muted " + ears.Muted + ", dictation " + (dictation != null); };
                Action settle = delegate
                {
                    CheckEars(clock.Elapsed.TotalSeconds);
                    room = -1; lineAt = -100; noiseHold = false; nameOver = false;
                    for (int i = 0; i < 10; i++) Gather(wave(40));
                    if (earsShut || transcribing || utterance != null) throw new Exception("Her ears weren't open for the echo check: " + earsState());
                };
                Action<Func<bool>> drain = delegate(Func<bool> done)
                {
                    for (int i = 0; i < 12; i++) Gather(wave(40));
                    Stopwatch drained = Stopwatch.StartNew();
                    while (!done() && drained.ElapsedMilliseconds < 4000) { Application.DoEvents(); Thread.Sleep(5); }
                };
                List<short[]> sentEcho = Echo(Chimes.Render(Chimes.Set, Chime.Sent, 100), 12), heardEcho = Echo(Chimes.Render(Chimes.Set, Chime.Heard, 100), 12);
                settle();
                later();
                decodedWas = decoded;
                Chimes.Play(Chime.Sent);
                foreach (short[] chunk in sentEcho) Gather(chunk);
                bool roomQuiet = utterance == null;
                drain(delegate { return !transcribing; });
                roomQuiet = roomQuiet && decoded == decodedWas;
                later(); later();
                settle();
                foreach (short[] chunk in sentEcho) Gather(chunk);
                bool unmaskedStarts = utterance != null;
                callsWas = calls;
                drain(delegate { return !transcribing && utterance == null; });
                CloseTalk();
                settle();
                Func<int, bool> overHer = delegate(int barged)
                {
                    talkState = TalkState.Talking;
                    voiceUntil = clock.Elapsed.TotalSeconds + 30;
                    spokenAt = clock.Elapsed.TotalSeconds;
                    spokenLevels = new double[400];
                    talkOver.Reset();
                    foreach (short[] chunk in heardEcho) Gather(chunk);
                    for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(5); }
                    bool kept = bargeIns == barged && voiceUntil > clock.Elapsed.TotalSeconds && talkState == TalkState.Talking;
                    voiceUntil = 0;
                    talkState = TalkState.Off;
                    spokenLevels = null;
                    talkOver.Reset();
                    lock (waking) preroll.Clear();
                    return kept;
                };
                later();
                Chimes.Play(Chime.Heard);
                bool herVoiceKept = overHer(bargeIns);
                later(); later();
                bool unmaskedKept = overHer(bargeIns);
                CloseTalk();
                if (!roomQuiet || !herVoiceKept) throw new Exception("Her ears heard a chime: room " + roomQuiet + ", her voice kept " + herVoiceKept);
                checks.Add("PASS her ears never take a chime for you: a chime's echo, at full volume and twelve times louder again, starts no line in a quiet room and doesn't talk over her voice; with no chime playing, the same sound " + (unmaskedStarts ? "starts a line" : "doesn't start a line either") + " and " + (unmaskedKept ? "doesn't stop her either" : "stops her voice") + (unmaskedStarts || !unmaskedKept ? ", so her ears were open" : ""));
                CloseTalk();
                StopChat();
                dictation = dictationOpen;
                dictationOpen = null;

                // Hey Capri opens her ears with the listening chime.
                preferences.HeyCapri = true;
                wakeChecks = true;
                wakeOpen = true;
                wakeMicMuted = shushed = false;
                later();
                was = count();
                HeardHeyCapri();
                wakeChecks = false;
                if (!chatting) throw new Exception("Hey Capri didn't open her ears.");
                expect("Hey Capri", was, Chime.Listening);
                StopChat();
                CloseTalk();

                // Off, nothing.
                preferences.NoChimes = true;
                ShowChimes();
                later();
                was = count(); callsWas = calls;
                typer.Send("are you there");
                wait(delegate { return calls == callsWas + 1; }, "a typed message with chimes off");
                CloseTalk();
                ToggleChat();
                later();
                PushStart();
                for (int i = 0; i < 5; i++) Listen(wave(10000));
                PushDone();
                wait(delegate { return calls == callsWas + 2; }, "push to talk with chimes off");
                CloseTalk();
                StopChat();
                if (count() != was) throw new Exception("A chime played with Chimes off.");
                preferences.NoChimes = false;
                ShowChimes();
                checks.Add("PASS every way of talking to her chimes once: " + string.Join(", ", steps.ToArray()) + "; with Chimes off, typing and push to talk play nothing");
                checks.Add("LIMIT ElevenLabs voice chat's heard chime and a real mic and speakers need a live check; on a call only the sent chime plays");
            }
            finally
            {
                if (dictationOpen != null) dictation = dictationOpen;
                pushing = false;
                wakeChecks = wakeChecksWas;
                StopChat();
                CloseTalk();
                preferences.ListenCapri = listenWas;
                preferences.ListenCapriTold = toldWas;
                preferences.FreeChatTold = freeToldWas;
                preferences.Quiet = quietWas;
                preferences.Silent = silentWas;
                preferences.Typing = typingWas;
                FreeChatOn = freeChatWas;
                ownEarsTest = ownWas;
                Dictation.Make = dictationWas;
                Think = thinkWas;
                Speak = speakWas;
                FreeSpeak = freeSpeakWas;
                Hearing.Run = hearingWas;
                job = runningWas;
                working = workingWas;
                Brain.TranscriptPath = transcriptWas;
                Brain.Keeping = keepingWas;
                Brain.Keeper = keeperWas;
                preferences.HeyCapri = heyWas;
                wakeOpen = wakeOpenWas;
                wakeMicMuted = wakeMutedWas;
                shushed = shushedWas;
                Commentary.Stage = stageWas;
                voiceUntil = 0;
                talkState = TalkState.Off;
            }
        }

        // A chime as her mic would hear it from the speakers: 48 kHz down to her ears' 16 kHz, louder by loud, in her ears' chunks.
        private static List<short[]> Echo(short[] sound, int loud)
        {
            List<short[]> chunks = new List<short[]>();
            int length = sound.Length / 3;
            short[] down = new short[((length + Ears.Chunk - 1) / Ears.Chunk) * Ears.Chunk];
            for (int i = 0; i < length; i++) down[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, (sound[i * 3] + sound[i * 3 + 1] + sound[i * 3 + 2]) / 3 * loud));
            for (int at = 0; at < down.Length; at += Ears.Chunk)
            {
                short[] chunk = new short[Ears.Chunk];
                Array.Copy(down, at, chunk, 0, Ears.Chunk);
                chunks.Add(chunk);
            }
            return chunks;
        }

        private static bool Same(short[] a, short[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        // How alike two chimes are: their best normalised correlation within 10 ms of each other, 1 for the same sound.
        private static double Alike(short[] a, short[] b)
        {
            double energyA = 0, energyB = 0, best = 0;
            foreach (short sample in a) energyA += (double)sample * sample;
            foreach (short sample in b) energyB += (double)sample * sample;
            int most = Chimes.Rate / 100;
            for (int lag = -most; lag <= most; lag += 4)
            {
                double sum = 0;
                for (int i = Math.Max(0, -lag); i < a.Length && i + lag < b.Length; i++) sum += (double)a[i] * b[i + lag];
                best = Math.Max(best, Math.Abs(sum) / Math.Sqrt(energyA * energyB));
            }
            return best;
        }

        private void SnapMainframe(string path)
        {
            using (Bitmap panel = new Bitmap(mainframe.Width, mainframe.Height))
            {
                mainframe.DrawToBitmap(panel, new Rectangle(Point.Empty, mainframe.Size));
                mainframe.DrawRims(panel);
                panel.Save(path, ImageFormat.Png);
            }
        }
    }

    internal sealed partial class Mainframe
    {
        private static readonly string[] ChimeVolumeNames = { "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };
        private Toggle chimes, chimeSent, chimeHeard, chimeListening;
        private Slider chimeVolume;
        private Readout chimeVolumeName;

        // SOUNDS, under HABITS: her chimes. last is where Explore after's row is, as built so far.
        private void BuildSounds(int c, int last)
        {
            int y = last + 42;
            Section("SOUNDS >", c, y);
            chimes = Switch("Chimes", c, y + 24, delegate { if (!syncing) capri.ToggleChimes(); });
            chimes.AccessibleName = "Chimes. Soft sounds when your message is sent, when she's heard you and when she's listening";
            chimeVolume = Setting("Chime volume", ChimeVolumeNames, c, y + 24 + Row, "How loud her chimes are");
            chimeVolumeName = new Readout(chimeVolume);
            chimeVolume.Changed += delegate { if (syncing) return; chimeVolumeName.Text = ChimeVolumeNames[chimeVolume.Value]; capri.SetChimeVolume((chimeVolume.Value + 1) * 10); };
            chimeVolume.Released += delegate { if (!syncing) capri.PreviewChime(); };
            chimeSent = Switch("Message sent", c, y + 24 + Row * 2, delegate { if (!syncing) capri.ToggleChime(Chime.Sent); });
            chimeSent.AccessibleName = "Message sent. A soft rising tick when your typed message goes to her";
            chimeHeard = Switch("Heard you", c, y + 24 + Row * 3, delegate { if (!syncing) capri.ToggleChime(Chime.Heard); });
            chimeHeard.AccessibleName = "Heard you. A soft falling tick when she's heard you finish talking";
            chimeListening = Switch("Listening", c, y + 24 + Row * 4, delegate { if (!syncing) capri.ToggleChime(Chime.Listening); });
            chimeListening.AccessibleName = "Listening. Three soft rising ticks when Hey Capri opens her ears";
        }

        private void SyncSounds()
        {
            bool on = capri.ChimesOn;
            chimes.Checked = on;
            chimeVolume.Value = Math.Max(0, Math.Min(ChimeVolumeNames.Length - 1, capri.ChimeVolume / 10 - 1));
            chimeVolumeName.Text = ChimeVolumeNames[chimeVolume.Value];
            chimeSent.Checked = capri.ChimeOn(Chime.Sent);
            chimeHeard.Checked = capri.ChimeOn(Chime.Heard);
            chimeListening.Checked = capri.ChimeOn(Chime.Listening);
            chimeVolume.Enabled = chimeSent.Enabled = chimeHeard.Enabled = chimeListening.Enabled = on;
        }

        internal bool ChimesShown { get { return chimes.Checked; } }

        internal string SoundsStatus
        {
            get
            {
                List<string> each = new List<string>();
                if (chimeSent.Checked) each.Add("sent");
                if (chimeHeard.Checked) each.Add("heard");
                if (chimeListening.Checked) each.Add("listening");
                bool enabled = chimeVolume.Enabled && chimeSent.Enabled && chimeHeard.Enabled && chimeListening.Enabled;
                return (chimes.Checked ? "on" : "off") + " | " + chimeVolumeName.Text + " | " + string.Join(" ", each.ToArray()) + " | " + (enabled ? "enabled" : "disabled");
            }
        }

        internal void FlipChimes() { chimes.Checked = !chimes.Checked; }

        internal void FlipChime(Chime chime) { Toggle row = chime == Chime.Sent ? chimeSent : chime == Chime.Heard ? chimeHeard : chimeListening; row.Checked = !row.Checked; }

        internal void SlideChimeVolume(int steps) { Press(chimeVolume, steps); }

        internal bool SoundsInPage
        {
            get
            {
                foreach (Control row in new Control[] { chimes, chimeVolume, chimeSent, chimeHeard, chimeListening })
                    if (Folded(row) || Hidden(row)) return false;
                return true;
            }
        }
    }
}
