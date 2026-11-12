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
        // Which set, by its name in Chimes.Sets. Null, as in older settings, is Glass.
        [DataMember] public string ChimeSet;
    }

    internal sealed partial class Companion
    {
        // Her chimes have a speaker of their own: Plain, so they never go into her voice in their mic or Capri Remote's call, and
        // apart from her voice's, so one never clears or cuts her off. Like her other speakers it plays on the speaker picked in
        // VOICE, which is Windows' default unless they chose one.
        private Speaker chimeSpeaker;
        // Free voice chat chimed as the line ended, so the words coming back don't chime it again.
        private bool lineChimed;
        private bool chimesClosed;
        // The smoke feeds her ears far faster than a mic does, so a chime's mask would still be up over lines it never could
        // be; chimes stay quiet in every check but SmokeSounds.
        private bool chimesTested;
        private readonly object chimeGate = new object();
        // Her ears' gates through a chime: her own ears, Hey Capri's mic while she talks, and ElevenLabs voice chat's mic.
        private readonly EchoGate earsGate = new EchoGate(), wakeGate = new EchoGate(), linkGate = new EchoGate();
        // What ElevenLabs voice chat's mic heard through a chime, waiting to be sent as it was or as silence.
        private readonly List<short[]> linkHeld = new List<short[]>();
        // The preroll holds more than its own few chunks for a chime.
        private bool rollHeld;
        // The most her ears hold through a chime: its mask, the 300 ms after and the preroll before it.
        private const int MaskHeld = 18;

        private void WireChimes()
        {
            chimeSpeaker = new Speaker(testing) { Plain = true };
            ShowChimes();
            // Her speakers can take a moment to open, the first time or on a new device, so that's never on her UI thread.
            Chimes.Out = delegate(short[] sound)
            {
                ThreadPool.QueueUserWorkItem(delegate
                {
                    // A speaker still stuck opening for the last chime drops this one rather than queueing it up late.
                    if (!Monitor.TryEnter(chimeGate)) return;
                    try
                    {
                        if (chimesClosed) return;
                        chimeSpeaker.Open(Chimes.Rate);
                        chimeSpeaker.Play(sound);
                    }
                    catch (Exception) { }
                    finally { Monitor.Exit(chimeGate); }
                });
            };
            Chimes.Left = delegate { return chimeSpeaker.Left; };
            ThreadPool.QueueUserWorkItem(delegate { Chimes.Warm(); });
        }

        // A speaker stuck opening never holds her closing for more than half a second.
        private void CloseChimes()
        {
            if (chimeSpeaker == null || !Monitor.TryEnter(chimeGate, 500)) return;
            try { chimesClosed = true; chimeSpeaker.Close(); }
            finally { Monitor.Exit(chimeGate); }
        }

        // One chunk too many in her preroll, which keeps `keep`. Through a chime and just after, it holds up to MaskHeld instead,
        // so a line begun under the chime keeps its start; after that, what it held for the chime goes.
        private void PastChimeRoll(int keep)
        {
            if (Chimes.Holding)
            {
                rollHeld = true;
                if (preroll.Count > MaskHeld) preroll.RemoveAt(0);
                return;
            }
            preroll.RemoveAt(0);
            if (!rollHeld) return;
            rollHeld = false;
            while (preroll.Count > keep) preroll.RemoveAt(0);
        }

        // ElevenLabs voice chat's mic through a chime. What could be the chime is held; once they're heard over it, or just after
        // it, everything held goes to the agent as it was, a little late. If nobody spoke it goes as silence.
        private List<short[]> PastChime(short[] chunk)
        {
            List<short[]> ready = new List<short[]>();
            double level = VoiceLevel(chunk);
            bool holding = Chimes.Holding, voice = Chimes.Masking ? linkGate.Voice(level, WakeLevel) : level >= WakeLevel;
            if (holding && !voice)
            {
                linkHeld.Add(chunk);
                if (linkHeld.Count > MaskHeld) { ready.Add(new short[linkHeld[0].Length]); linkHeld.RemoveAt(0); }
                return ready;
            }
            foreach (short[] held in linkHeld) ready.Add(voice ? held : new short[held.Length]);
            linkHeld.Clear();
            ready.Add(chunk);
            return ready;
        }

        private void SentChime(string text) { if (!string.IsNullOrWhiteSpace(text)) Chimes.Play(Chime.Sent); }

        private void ShowChimes()
        {
            Chimes.On = !preferences.NoChimes && (!testing || chimesTested);
            Chimes.Volume = ChimeVolume;
            Chimes.Set = ChimeSet;
            Chimes.Want(Chime.Sent, !preferences.NoChimeSent);
            Chimes.Want(Chime.Heard, !preferences.NoChimeHeard);
            Chimes.Want(Chime.Listening, !preferences.NoChimeListening);
        }

        internal bool ChimesOn { get { return !preferences.NoChimes; } }

        internal int ChimeVolume { get { int volume = preferences.ChimeVolume; return volume <= 0 ? Chimes.DefaultVolume : Math.Max(10, Math.Min(100, volume / 10 * 10)); } }

        internal int ChimeSet { get { int set = Array.IndexOf(Chimes.Sets, preferences.ChimeSet ?? ""); return set < 0 ? Chimes.DefaultSet : set; } }

        // A new set takes over from the next chime, and plays its heard chime once at their volume so they hear it.
        internal void SetChimeSet(int set)
        {
            set = Math.Max(0, Math.Min(Chimes.Sets.Length - 1, set));
            preferences.ChimeSet = set == Chimes.DefaultSet ? null : Chimes.Sets[set];
            ShowChimes();
            SavePreferences();
            Chimes.Play(Chime.Heard, true);
        }

        internal bool ChimeOn(Chime chime) { return chime == Chime.Sent ? !preferences.NoChimeSent : chime == Chime.Heard ? !preferences.NoChimeHeard : !preferences.NoChimeListening; }

        internal void ToggleChimes()
        {
            preferences.NoChimes = !preferences.NoChimes;
            ShowChimes();
            // Off stops one still ringing too.
            if (preferences.NoChimes && chimeSpeaker != null) ThreadPool.QueueUserWorkItem(delegate { lock (chimeGate) { try { chimeSpeaker.Clear(); } catch (Exception) { } } });
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
            checks.Add("PASS her chimes: all six sets made in code, none longer than 400 ms, none clips or starts or ends off zero, 40% is the volume curve's 12 dB down, and each set's sent and heard chimes differ (" + string.Join("; ", table.ToArray()) + ")");

            using (MemoryStream old = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")))
            {
                Preferences older = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(old);
                foreach (Preferences start in new[] { older, new Preferences(), Preferences.Fresh() })
                    if (start.NoChimes || start.NoChimeSent || start.NoChimeHeard || start.NoChimeListening || start.ChimeVolume != 0 || start.ChimeSet != null) throw new Exception("Her chimes didn't start on Glass at 40% in new or older settings.");
            }

            Action<short[]> outWas = Chimes.Out;
            Func<double> leftWas = Chimes.Left;
            Func<long> clockWas = Chimes.Clock;
            bool offWas = preferences.NoChimes, sentWas = preferences.NoChimeSent, heardWas = preferences.NoChimeHeard, listeningWas = preferences.NoChimeListening;
            int volumeWas = preferences.ChimeVolume;
            string setWas = preferences.ChimeSet;
            bool advancedWas = preferences.AdvancedSettings, pageWas = mainframe.SettingsShown;
            long now = 1000000;
            double ringing = 0;
            chimesTested = true;
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
                preferences.ChimeSet = null;
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
                Stopwatch handed = Stopwatch.StartNew();
                outWas(Chimes.Render(Chimes.Glass, Chime.Heard, 40));
                long onUi = handed.ElapsedTicks * 1000000 / Stopwatch.Frequency;
                while (chimeSpeaker.Left <= 0 && handed.ElapsedMilliseconds < 2000) Thread.Sleep(1);
                double reached = handed.ElapsedTicks * 1000.0 / Stopwatch.Frequency;
                bool own = chimeSpeaker.Plain && chimeSpeaker != speaker && chimeSpeaker != effects && chimeSpeaker.Left > 0;
                bool kept = speaker.SoundEnd == herEnd && speaker.Left > 0;
                speaker.Clear();
                // Chimes off mid-ring stops the one ringing.
                preferences.NoChimes = false;
                ToggleChimes();
                Stopwatch off = Stopwatch.StartNew();
                while (chimeSpeaker.Left > 0 && off.ElapsedMilliseconds < 2000) Thread.Sleep(1);
                bool stopped = chimeSpeaker.Left <= 0 && off.ElapsedMilliseconds < 250;
                ToggleChimes();
                chimeSpeaker.Clear();
                if (!own || !kept || !stopped) throw new Exception("Her chimes didn't get their own Plain speaker, cut her voice or rang on once off: own " + own + ", kept " + kept + ", stopped " + stopped);
                // A speaker that's gone, or stuck opening, never throws, holds her UI thread or stacks chimes up behind it.
                Speaker realChimes = chimeSpeaker;
                bool skipWas = Speaker.SkipWave;
                int opens = 0;
                long goneUi, stuckUi;
                int duringStuck;
                using (ManualResetEvent stuck = new ManualResetEvent(false))
                {
                    try
                    {
                        Speaker.SkipWave = true;
                        chimeSpeaker = new Speaker(false, delegate(int rate, string id) { Interlocked.Increment(ref opens); throw new VoiceException("No speakers."); }) { Plain = true };
                        Stopwatch gone = Stopwatch.StartNew();
                        outWas(Chimes.Render(Chimes.Glass, Chime.Sent, 40));
                        goneUi = gone.ElapsedMilliseconds;
                        while (opens == 0 && gone.ElapsedMilliseconds < 2000) Thread.Sleep(1);
                        Thread.Sleep(50);
                        lock (chimeGate) chimeSpeaker = new Speaker(false, delegate(int rate, string id) { Interlocked.Increment(ref opens); stuck.WaitOne(3000); throw new VoiceException("Busy."); }) { Plain = true };
                        int opensWas = opens;
                        Stopwatch held = Stopwatch.StartNew();
                        outWas(Chimes.Render(Chimes.Glass, Chime.Sent, 40));
                        while (opens == opensWas && held.ElapsedMilliseconds < 2000) Thread.Sleep(1);
                        outWas(Chimes.Render(Chimes.Glass, Chime.Heard, 40));
                        outWas(Chimes.Render(Chimes.Glass, Chime.Listening, 40));
                        stuckUi = held.ElapsedMilliseconds;
                        Thread.Sleep(150);
                        duringStuck = opens - opensWas;
                    }
                    finally
                    {
                        stuck.Set();
                        lock (chimeGate) { if (chimeSpeaker != realChimes) chimeSpeaker.Dispose(); chimeSpeaker = realChimes; }
                        Speaker.SkipWave = skipWas;
                    }
                }
                if (opens < 2 || goneUi > 50 || duringStuck != 1) throw new Exception("A gone or stuck speaker held her or stacked chimes: opens " + opens + ", " + goneUi + " ms on her UI thread, " + duringStuck + " opens while stuck");
                checks.Add("PASS Chimes off mid-ring stops the chime ringing; with no speaker a chime fails quietly off her UI thread, and while a speaker is stuck opening the chimes after it are dropped, not queued (" + stuckUi + " ms on her UI thread for three)");
                checks.Add("PASS her chimes play on their own Plain speaker, which stays out of her voice in your mic and Capri Remote, and a chime over her voice leaves it playing; a chime leaves her UI thread in " + onUi + " us and is playing on the silent stand-in " + reached.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms later, and the eighteen chimes took " + Chimes.Warmed + " ms to make at start-up, off her UI thread");

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
                SoundSets(checks, played, delegate { now += 2000; }, delegate(double seconds) { ringing = seconds; });
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
                SettingsLook(Path.Combine(output, "sounds-look"));
                {
                    bool visibleWas = mainframe.Visible;
                    FormWindowState windowWas = mainframe.WindowState;
                    try
                    {
                        mainframe.WindowState = FormWindowState.Normal;
                        mainframe.Show();
                        Application.DoEvents();
                        mainframe.SoundsTips(Path.Combine(output, "sounds-look"));
                    }
                    finally
                    {
                        if (!visibleWas) mainframe.Hide();
                        mainframe.WindowState = windowWas;
                        mainframe.ShowSettings(pageWas);
                    }
                }
                preferences.AdvancedSettings = advancedWas;
                mainframe.ShowSettings(pageWas);

                SoundsHeard(checks, played, delegate { now += 2000; }, delegate(long ms) { now += ms; });
            }
            finally
            {
                Chimes.Out = outWas;
                Chimes.Left = leftWas;
                Chimes.Clock = clockWas;
                Chimes.Reset();
                preferences.ChimeSet = setWas;
                chimesTested = false;
                preferences.NoChimes = offWas; preferences.NoChimeSent = sentWas; preferences.NoChimeHeard = heardWas; preferences.NoChimeListening = listeningWas;
                preferences.ChimeVolume = volumeWas;
                preferences.AdvancedSettings = advancedWas;
                ShowChimes();
                SavePreferences();
                mainframe.Sync();
                mainframe.ShowSettings(pageWas);
            }
        }

        // Sound set: each step saves, takes over from the very next chime and plays its heard chime once; never over one still
        // ringing, never with Chimes off; and her settings tool picks a set by name.
        private void SoundSets(List<string> checks, List<short[]> played, Action later, Action<double> ring)
        {
            Func<int> count = delegate { lock (played) return played.Count; };
            List<string> walked = new List<string>();
            mainframe.ShowSettings(true);
            if (mainframe.ChimeSetShown != "Glass" || Chimes.Set != Chimes.Glass) throw new Exception("Sound set didn't start on Glass: " + mainframe.ChimeSetShown);
            for (int step = 1; step <= Chimes.Picked.Length; step++)
            {
                int set = Chimes.Picked[step % Chimes.Picked.Length];
                later();
                int was = count();
                mainframe.StepChimeSet(step < Chimes.Picked.Length ? 1 : -(Chimes.Picked.Length - 1));
                if (Chimes.Set != set || ChimeSet != set || LoadPreferences().ChimeSet != (set == Chimes.Glass ? null : Chimes.Sets[set]) || mainframe.ChimeSetShown != Chimes.Titles[set])
                    throw new Exception("Sound set didn't move to " + Chimes.Titles[set] + " or save it: " + mainframe.ChimeSetShown + ", playing " + Chimes.Titles[Chimes.Set]);
                if (step < Chimes.Picked.Length && (count() != was + 1 || !Same(played[played.Count - 1], Chimes.Render(set, Chime.Heard, Chimes.Volume)))) throw new Exception("Sound set's " + Chimes.Titles[set] + " preview didn't play once: " + (count() - was));
                later();
                Chimes.Play(Chime.Sent);
                if (!Same(played[played.Count - 1], Chimes.Render(set, Chime.Sent, Chimes.Volume))) throw new Exception("The next chime after picking " + Chimes.Titles[set] + " wasn't from it.");
                walked.Add(Chimes.Titles[set]);
            }
            // Picked while one still rings: the new set waits for the next chime, and nothing lands on the one ringing.
            later();
            Chimes.Play(Chime.Sent);
            ring(0.2);
            int ringingWas = count();
            mainframe.StepChimeSet(1);
            bool overlapped = count() != ringingWas;
            ring(0);
            mainframe.StepChimeSet(-1);
            // With Chimes off, a new set saves but plays nothing.
            preferences.NoChimes = true;
            ShowChimes();
            mainframe.Sync();
            later();
            int offWas = count();
            mainframe.StepChimeSet(2);
            bool offPreview = count() != offWas;
            int offSet = Chimes.Set;
            preferences.NoChimes = false;
            ShowChimes();
            mainframe.Sync();
            mainframe.StepChimeSet(-2);
            if (overlapped || offPreview || offSet != Chimes.Picked[2]) throw new Exception("Sound set played over a ringing chime or with Chimes off: overlapped " + overlapped + ", off " + offPreview);
            // Her settings tool.
            string shard = Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "chime_set", Level = "shard" });
            bool toolShard = Chimes.Set == Chimes.Shard && mainframe.ChimeSetShown == "Shard";
            string crush = Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "chime_set", Level = "Crush" });
            bool toolCrush = Chimes.Set == Chimes.Crush;
            string listed = Hands.Do("capri_settings", new ToolArguments { Action = "list" });
            Hands.Do("capri_settings", new ToolArguments { Action = "set", Name = "chime_set", Level = "glass" });
            string help = SelfKnow.Matching("can you use the shard sounds?");
            if (!toolShard || !toolCrush || Chimes.Set != Chimes.Glass || !listed.Contains("Chime sound set: Crush") || SelfKnow.Setting("Mainframe.cs", "Sound set") == null || help == null || !help.Contains("Sound set"))
                throw new Exception("Her settings tool or help didn't handle Sound set: " + shard + " / " + crush + " / " + help);
            checks.Add("PASS Sound set steps " + string.Join(", ", walked.ToArray()) + ", each saved, taking over from the very next chime and playing its heard chime once; picked mid-ring it plays nothing over the ringing chime, and with Chimes off it saves but plays nothing; her settings tool picks a set by name and her help answers about it");
        }

        // Every way of talking to her plays its chime once, through her real ears and hands with a stand-in brain, and her ears
        // never take a chime for them.
        private void SoundsHeard(List<string> checks, List<short[]> played, Action later, Action<long> step)
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

                // Last, with her own ears open again: what a chime costs a line said straight after it.
                StopChat();
                ToggleChat();
                dictation = null;
                SoundsLost(checks, later, step);
                StopChat();
            }
            finally
            {
                // Anything still on its way from these lines lands here, on the stand-in brain, before the real one is back.
                int lateWas = calls;
                Stopwatch drain = Stopwatch.StartNew();
                while (drain.ElapsedMilliseconds < 1500 || transcribing && drain.ElapsedMilliseconds < 5000) { Application.DoEvents(); Thread.Sleep(10); }
                if (calls != lateWas) checks.Add("INFO a line from the sounds checks reached her stand-in brain " + (calls - lateWas) + " time(s) after they finished");
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

        // How much of what they say straight after a chime reaches her, every way she hears them, with the chime's echo in the
        // mic as speakers next to it would play it: each 100 ms of their line is looked for, whole, in what her ears turned into
        // words or sent on to ElevenLabs. Losing someone's first word is worse than anything a chime's echo could do.
        private void SoundsLost(List<string> checks, Action later, Action<long> step)
        {
            Func<short[], string> runWas = Hearing.Run;
            Func<string, string> thinkWas = Think;
            Action<short[]> micWas = sendMicTest;
            bool listenWas = preferences.ListenCapri;
            int setWas = Chimes.Set;
            Agent agentWas = agent;
            bool localWas = local;
            Agent link = new Agent();
            short[] heard = null;
            int decoded = 0, replies = 0;
            string words = "what's for dinner";
            Func<short[]> hush = delegate { return Tone(0, 40); };
            Action<Func<bool>> settleOn = delegate(Func<bool> done)
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (!done() && waited.ElapsedMilliseconds < 4000) { Application.DoEvents(); Thread.Sleep(5); }
            };
            Action open = delegate
            {
                CloseTalk();
                CheckEars(clock.Elapsed.TotalSeconds);
                lock (waking) { room = -1; lineAt = -100; noiseHold = false; utterance = null; preroll.Clear(); loudRun = quietRun = 0; }
                nameOver = false;
                for (int i = 0; i < 10; i++) Gather(hush());
                if (earsShut || transcribing || utterance != null) throw new Exception("Her ears weren't open to measure what a chime costs.");
            };
            // How many of `fed` are in `got`, whole.
            Func<List<short[]>, List<short[]>, int> found = delegate(List<short[]> fed, List<short[]> got)
            {
                int kept = 0;
                foreach (short[] chunk in fed)
                    foreach (short[] piece in got)
                        if (Same(chunk, piece)) { kept++; break; }
                return kept;
            };
            Func<short[], List<short[]>> chunked = delegate(short[] samples)
            {
                List<short[]> pieces = new List<short[]>();
                for (int at = 0; samples != null && at + Ears.Chunk <= samples.Length; at += Ears.Chunk)
                {
                    short[] piece = new short[Ears.Chunk];
                    Array.Copy(samples, at, piece, 0, Ears.Chunk);
                    pieces.Add(piece);
                }
                return pieces;
            };
            // The echo, with their line from `from` chunks into it for `length` chunks, then a second of quiet. Returns their line as fed.
            Func<List<short[]>, int, int, Action<short[]>, List<short[]>> speak = delegate(List<short[]> echo, int from, int length, Action<short[]> ear)
            {
                List<short[]> fed = new List<short[]>();
                int chunks = Math.Max(echo.Count, from + length) + 12;
                for (int i = 0; i < chunks; i++)
                {
                    short[] chunk = i < echo.Count ? echo[i] : hush();
                    if (i >= from && i < from + length) { chunk = Mix(chunk, Tone(i + 1, i % 2 == 0 ? 14000 : 20000)); fed.Add(chunk); }
                    ear(chunk);
                    step(100);
                }
                return fed;
            };
            List<string> table = new List<string>(), lost = new List<string>();
            try
            {
                Hearing.Run = delegate(short[] samples) { heard = samples; Interlocked.Increment(ref decoded); return words; };
                Think = delegate { Interlocked.Increment(ref replies); return "Got it."; };
                List<short[]> near = EchoIn(Chimes.Render(Chimes.Set, Chime.Heard, Chimes.Volume), 4, 0, false);
                List<short[]> late = EchoIn(Chimes.Render(Chimes.Set, Chime.Heard, 100), 1, 400, true);
                int[] starts = { 0, 2, 4, 6, 8, 10 };
                const int Long = 15, Short = 3;
                Func<int, int, string> ms = delegate(int kept, int of) { return ((of - kept) * 100).ToString(); };

                // Her own ears in free voice chat and with Listen for Capri: a line straight after a chime.
                foreach (bool name in new[] { false, true })
                {
                    preferences.ListenCapri = name;
                    words = name ? "capri what's for dinner" : "what's for dinner";
                    foreach (int length in new[] { Long, Short })
                    {
                        List<string> row = new List<string>();
                        foreach (int from in starts)
                        {
                            open();
                            later();
                            if (!Chimes.Play(Chime.Heard)) throw new Exception("The heard chime didn't play for the measure.");
                            int decodedWas = decoded;
                            List<short[]> fed = speak(late, from, length, Gather);
                            settleOn(delegate { return decoded > decodedWas && !transcribing; });
                            int kept = decoded > decodedWas ? found(fed, chunked(heard)) : 0;
                            row.Add(from * 100 + " ms: " + ms(kept, fed.Count));
                            if (length == Long && kept < fed.Count) lost.Add((name ? "Listen for Capri" : "free voice chat") + " lost " + ms(kept, fed.Count) + " ms of a line begun " + from * 100 + " ms after the chime");
                            settleOn(delegate { return !transcribing && talkState == TalkState.Off; });
                            CloseTalk();
                            if (name) CloseNameChat();
                        }
                        table.Add((name ? "Listen for Capri" : "free voice chat") + ", " + (length == Long ? "1.5 s line" : "300 ms word") + " lost " + string.Join(", ", row.ToArray()));
                    }
                }
                preferences.ListenCapri = false;
                words = "what's for dinner";

                // Hey Capri: her ears have only just opened, and they say it all in one breath.
                {
                    List<string> row = new List<string>();
                    foreach (int from in new[] { 0, 3 })
                    {
                        StopChat();
                        ToggleChat();
                        dictation = null;
                        CheckEars(clock.Elapsed.TotalSeconds);
                        later();
                        if (!Chimes.Play(Chime.Listening)) throw new Exception("The listening chime didn't play for the measure.");
                        int decodedWas = decoded;
                        List<short[]> fed = speak(EchoIn(Chimes.Render(Chimes.Set, Chime.Listening, 100), 1, 400, true), from, Long, Gather);
                        settleOn(delegate { return decoded > decodedWas && !transcribing; });
                        int kept = decoded > decodedWas ? found(fed, chunked(heard)) : 0;
                        row.Add(from * 100 + " ms: " + ms(kept, fed.Count));
                        if (kept < fed.Count) lost.Add("Hey Capri lost " + ms(kept, fed.Count) + " ms of a line begun " + from * 100 + " ms after the chime");
                        settleOn(delegate { return !transcribing && talkState == TalkState.Off; });
                        CloseTalk();
                    }
                    table.Add("Hey Capri, 1.5 s line lost " + string.Join(", ", row.ToArray()));
                }

                // Push to talk, held again the moment the heard chime plays.
                {
                    open();
                    later();
                    Chimes.Play(Chime.Heard);
                    int decodedWas = decoded;
                    PushStart();
                    List<short[]> fed = speak(near, 0, Long, Listen);
                    PushDone();
                    settleOn(delegate { return decoded > decodedWas; });
                    int kept = decoded > decodedWas ? found(fed, chunked(heard)) : 0;
                    table.Add("push to talk, held at the chime, lost " + ms(kept, fed.Count) + " ms");
                    if (kept < fed.Count) lost.Add("push to talk lost " + ms(kept, fed.Count) + " ms");
                    settleOn(delegate { return !transcribing && talkState == TalkState.Off; });
                    CloseTalk();
                }

                // ElevenLabs voice chat: what reaches the agent.
                {
                    List<short[]> sent = new List<short[]>();
                    sendMicTest = delegate(short[] chunk) { sent.Add(chunk); };
                    List<string> row = new List<string>();
                    bool echoSent = false;
                    try
                    {
                        local = false;
                        agent = link;
                        lock (waking) { resting = false; backlog = null; }
                        foreach (int from in starts)
                        {
                            later();
                            sent.Clear();
                            Chimes.Play(Chime.Heard);
                            List<short[]> fed = speak(late, from, Long, Listen);
                            int kept = found(fed, sent);
                            row.Add(from * 100 + " ms: " + ms(kept, fed.Count));
                            if (kept < fed.Count) lost.Add("ElevenLabs voice chat lost " + ms(kept, fed.Count) + " ms of a line begun " + from * 100 + " ms after the chime");
                        }
                        // The echo alone reaches the agent as silence.
                        later();
                        sent.Clear();
                        Chimes.Play(Chime.Heard);
                        speak(late, 0, 0, Listen);
                        for (int i = 0; i < sent.Count && i < 10; i++) if (VoiceLevel(sent[i]) >= WakeLevel) echoSent = true;
                    }
                    finally
                    {
                        agent = agentWas;
                        local = localWas;
                        sendMicTest = micWas;
                    }
                    table.Add("ElevenLabs voice chat, 1.5 s line lost " + string.Join(", ", row.ToArray()) + (echoSent ? "; the echo alone reached it as sound" : ""));
                    if (echoSent) lost.Add("a chime's echo reached ElevenLabs as sound");
                }

                // The echo alone, of every chime in every set, near the mic and late with a room's ring, never starts a line. In a
                // room with a steady hum her ears keep its level through a chime, or the hum starts lines after it.
                List<string> started = new List<string>();
                for (int set = 0; set < Chimes.Sets.Length; set++)
                    for (int kind = 0; kind < 3; kind++)
                        foreach (bool ring in new[] { false, true })
                        {
                            Chimes.Set = set;
                            open();
                            later();
                            Chimes.Play((Chime)kind);
                            int decodedWas = decoded;
                            speak(EchoIn(Chimes.Render(set, (Chime)kind, 100), ring ? 1 : 12, ring ? 400 : 0, ring), 0, 0, Gather);
                            bool begun = utterance != null;
                            settleOn(delegate { return !transcribing; });
                            if (begun || decoded != decodedWas) started.Add(Chimes.Sets[set] + " " + Chimes.Names[kind] + (ring ? " late" : " near"));
                            CloseTalk();
                        }
                Chimes.Set = setWas;
                open();
                short[] hum = new short[Ears.Chunk];
                for (int i = 0; i < hum.Length; i++) hum[i] = (short)(Math.Sin(i * Math.PI / 40) * 300);
                for (int i = 0; i < 20; i++) Gather(hum);
                double roomBefore = room;
                later();
                Chimes.Play(Chime.Heard);
                List<short[]> humEcho = EchoIn(Chimes.Render(Chimes.Set, Chime.Heard, Chimes.Volume), 4, 0, false);
                int humDecoded = decoded;
                bool humStarted = false;
                for (int i = 0; i < 40; i++) { Gather(i < humEcho.Count ? Mix(hum, humEcho[i]) : hum); step(100); humStarted = humStarted || utterance != null; }
                double roomAfter = room;
                settleOn(delegate { return !transcribing; });
                humStarted = humStarted || decoded != humDecoded;
                CloseTalk();
                table.Add("a steady hum's level " + roomBefore.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + " before a chime and " + roomAfter.ToString("0.0000", System.Globalization.CultureInfo.InvariantCulture) + " after" + (humStarted ? ", and the hum started a line" : ""));
                // With Chimes off nothing plays, so nothing masks or holds her ears and every gate lets all through, as before chimes.
                preferences.NoChimes = true;
                ShowChimes();
                later();
                bool offPlayed = Chimes.Play(Chime.Sent) | Chimes.Play(Chime.Heard) | Chimes.Play(Chime.Listening);
                bool offOpen = !offPlayed && !Chimes.Masking && !Chimes.Holding && earsGate.Voice(0.5, WakeLevel) && wakeGate.Voice(0.5, WakeLevel) && linkGate.Voice(0.5, WakeLevel);
                preferences.NoChimes = false;
                ShowChimes();
                if (!offOpen) lost.Add("with Chimes off a chime still masked or held her ears");
                checks.Add("INFO what a chime costs what they say: " + string.Join("; ", table.ToArray()));
                if (started.Count > 0) lost.Add("a chime's echo alone started a line: " + string.Join(", ", started.ToArray()));
                if (humStarted || roomAfter < roomBefore / 2) lost.Add("a chime made her ears forget a room's hum");
                if (lost.Count > 0) throw new Exception("A chime cost them words or fooled her ears: " + string.Join("; ", lost.ToArray()));
                checks.Add("PASS a chime costs nothing of a line said straight after it, 0 to 1000 ms on, in free voice chat, Listen for Capri, Hey Capri, push to talk and ElevenLabs voice chat; its echo alone, near the mic twelve times over full volume or 400 ms late with a room's ring at full volume, starts no line and reaches ElevenLabs as silence, for every chime in every set; and a room's hum keeps its level through it");
            }
            finally
            {
                Hearing.Run = runWas;
                Think = thinkWas;
                sendMicTest = micWas;
                agent = agentWas;
                local = localWas;
                preferences.ListenCapri = listenWas;
                Chimes.Set = setWas;
                link.Dispose();
                CloseTalk();
            }
        }

        private static short[] Tone(int index, int amplitude)
        {
            short[] samples = new short[Ears.Chunk];
            double period = 30 + index % 17;
            for (int i = 0; i < samples.Length; i++) samples[i] = (short)(Math.Sin(i * Math.PI / period) * amplitude);
            return samples;
        }

        private static short[] Mix(short[] a, short[] b)
        {
            short[] mixed = new short[a.Length];
            for (int i = 0; i < a.Length; i++) mixed[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, a[i] + b[i]));
            return mixed;
        }

        // A chime as her mic would hear it: 48 kHz down to her ears' 16 kHz, louder by loud, `late` ms after it played, and with
        // ring, a room's echoes every 23 ms, each a quarter quieter, about half a second of ring in all.
        private static List<short[]> EchoIn(short[] sound, double loud, int late, bool ring)
        {
            int length = sound.Length / 3, delay = late * Ears.Rate / 1000, tail = ring ? 25 * 368 : 0;
            double[] down = new double[delay + length + tail + Ears.Chunk];
            for (int i = 0; i < length; i++)
            {
                double sample = (sound[i * 3] + sound[i * 3 + 1] + sound[i * 3 + 2]) / 3.0 * loud;
                down[delay + i] += sample;
                for (int tap = 1; ring && tap <= 25; tap++) down[delay + i + tap * 368] += sample * Math.Pow(0.75, tap);
            }
            List<short[]> chunks = new List<short[]>();
            for (int at = 0; at + Ears.Chunk <= down.Length; at += Ears.Chunk)
            {
                short[] chunk = new short[Ears.Chunk];
                for (int i = 0; i < Ears.Chunk; i++) chunk[i] = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, down[at + i] + Math.Sin(i * Math.PI / 40) * 40));
                chunks.Add(chunk);
            }
            return chunks;
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

        // Settings drawn at 100%, 125% and 150%, Simple and Advanced, into folder, with every row's place in settings-rows.txt.
        private void SettingsLook(string folder)
        {
            bool advancedWas = preferences.AdvancedSettings, pageWas = mainframe.SettingsShown, shownWas = mainframe.Visible;
            FormWindowState stateWas = mainframe.WindowState;
            List<string> rows = new List<string>();
            Directory.CreateDirectory(folder);
            try
            {
                mainframe.WindowState = FormWindowState.Normal;
                mainframe.Show();
                Application.DoEvents();
                foreach (bool advanced in new[] { false, true })
                    foreach (int percent in new[] { 100, 125, 150 })
                    {
                        Action draw = delegate { mainframe.SnapSettings(percent * 96 / 100, (advanced ? "advanced-" : "simple-") + percent, folder, rows); };
                        if (advanced) { preferences.AdvancedSettings = true; draw(); }
                        else InSimpleSettings(draw);
                    }
                File.WriteAllLines(Path.Combine(folder, "settings-rows.txt"), rows.ToArray());
            }
            finally
            {
                preferences.AdvancedSettings = advancedWas;
                mainframe.ShowSettings(pageWas);
                if (!shownWas) mainframe.Hide();
                mainframe.WindowState = stateWas;
            }
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
        private Slider chimeVolume, chimeSet;
        private Readout chimeVolumeName, chimeSetName;

        // Sound set's steps, in the order SOUNDS shows them.
        internal static string[] SetNames()
        {
            string[] names = new string[Chimes.Picked.Length];
            for (int i = 0; i < names.Length; i++) names[i] = Chimes.Titles[Chimes.Picked[i]];
            return names;
        }
        private Control chimeVolumeLabel, chimeSetLabel;

        // SOUNDS, under HABITS: her chimes. last is where Explore after's row is, as built so far.
        private void BuildSounds(int c, int last)
        {
            int y = last + 42;
            Section("SOUNDS >", c, y);
            chimes = Switch("Chimes", c, y + 24, delegate { if (!syncing) capri.ToggleChimes(); });
            chimes.AccessibleName = "Chimes. Soft sounds when your message is sent, when she's heard you and when she's listening";
            chimeSet = Setting("Sound set", SetNames(), c, y + 24 + Row, "Which set of chimes she plays");
            chimeSetName = new Readout(chimeSet);
            chimeSetLabel = Controls[Controls.IndexOf(chimeSet) - 1];
            chimeSet.Changed += delegate { if (syncing) return; chimeSetName.Text = SetNames()[chimeSet.Value]; capri.SetChimeSet(Chimes.Picked[chimeSet.Value]); };
            chimeVolume = Setting("Chime volume", ChimeVolumeNames, c, y + 24 + Row * 2, "How loud her chimes are");
            chimeVolumeName = new Readout(chimeVolume);
            chimeVolumeLabel = Controls[Controls.IndexOf(chimeVolume) - 1];
            chimeVolume.Changed += delegate { if (syncing) return; chimeVolumeName.Text = ChimeVolumeNames[chimeVolume.Value]; capri.SetChimeVolume((chimeVolume.Value + 1) * 10); };
            chimeVolume.Released += delegate { if (!syncing) capri.PreviewChime(); };
            chimeSent = Switch("Message sent", c, y + 24 + Row * 3, delegate { if (!syncing) capri.ToggleChime(Chime.Sent); });
            chimeSent.AccessibleName = "Message sent. A soft rising tick when your typed message goes to her";
            chimeHeard = Switch("Heard you", c, y + 24 + Row * 4, delegate { if (!syncing) capri.ToggleChime(Chime.Heard); });
            chimeHeard.AccessibleName = "Heard you. A soft falling tick when she's heard you finish talking";
            chimeListening = Switch("Listening", c, y + 24 + Row * 5, delegate { if (!syncing) capri.ToggleChime(Chime.Listening); });
            chimeListening.AccessibleName = "Listening. Three soft rising ticks when Hey Capri opens her ears";
        }

        private void SyncSounds()
        {
            bool on = capri.ChimesOn;
            chimes.Checked = on;
            chimeVolume.Value = Math.Max(0, Math.Min(ChimeVolumeNames.Length - 1, capri.ChimeVolume / 10 - 1));
            chimeVolumeName.Text = ChimeVolumeNames[chimeVolume.Value];
            chimeSet.Value = Math.Max(0, Array.IndexOf(Chimes.Picked, capri.ChimeSet));
            chimeSetName.Text = SetNames()[chimeSet.Value];
            chimeSent.Checked = capri.ChimeOn(Chime.Sent);
            chimeHeard.Checked = capri.ChimeOn(Chime.Heard);
            chimeListening.Checked = capri.ChimeOn(Chime.Listening);
            chimeSet.Enabled = chimeVolume.Enabled = chimeSent.Enabled = chimeHeard.Enabled = chimeListening.Enabled = on;
            chimeVolumeLabel.ForeColor = chimeSetLabel.ForeColor = on ? Rose : Neon.Tone(112, 94, 104);
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

        internal void StepChimeSet(int steps) { Press(chimeSet, steps); }

        internal string ChimeSetShown { get { return chimeSetName.Text + (chimeSet.Enabled ? "" : " (grey)"); } }

        // Settings as they'd see it with Windows at dpi (96 for 100%, 120 for 125%, 144 for 150%) and her Mainframe size at 100%,
        // drawn the way PrintWindow draws it on the smoke's own desktop: the view with HABITS at its top, and the whole page down
        // its length. Each row's place on the page goes into rows, to hold against another build's.
        internal void SnapSettings(int dpi, string name, string folder, List<string> rows)
        {
            int dpiWas = layoutDpi, textWas = layoutText, sizeWas = capri.MainframeSize;
            bool fallbackWas = layoutFallback;
            try
            {
                ShowSettings(true);
                LayoutScale(dpi, 100);
                SizeMainframe(100);
                ShowSettings(true);
                Application.DoEvents();
                if (Math.Abs(unit - dpi / 96f) > 0.01f) name += "-drawn-at-" + Math.Round(unit * 100);
                foreach (KeyValuePair<Control, int> entry in pageLeft)
                {
                    Control row = entry.Key;
                    if (Folded(row) || Hidden(row) || !row.Visible) continue;
                    string label = row.AccessibleName ?? "";
                    if (label.Length == 0) label = row.Text ?? "";
                    if (label.Length == 0) label = row.GetType().Name;
                    int stop = label.IndexOf('.');
                    if (stop > 0) label = label.Substring(0, stop);
                    rows.Add(name + "\t" + label.Replace('\t', ' ').Replace('\n', ' ') + "\t" + entry.Value + "\t" + (row.Top + Scaled(pageScroll)) + "\t" + row.Height + "\t" + row.Width);
                }
                float s = Unit();
                ScrollPage((int)Math.Round((startup.Top + Scaled(pageScroll)) / s) - 44);
                Application.DoEvents();
                using (Bitmap view = new Bitmap(Width, Height))
                {
                    DrawToBitmap(view, new Rectangle(Point.Empty, Size));
                    DrawRims(view);
                    view.Save(Path.Combine(folder, name + "-habits.png"), ImageFormat.Png);
                }
                ScrollPage(0);
                Application.DoEvents();
                List<KeyValuePair<int, Bitmap>> parts = new List<KeyValuePair<int, Bitmap>>();
                try
                {
                    int at = 0, height = 0;
                    for (int i = 0; i < 20; i++)
                    {
                        ScrollPage(at);
                        Application.DoEvents();
                        Bitmap part = new Bitmap(Width, Height);
                        DrawToBitmap(part, new Rectangle(Point.Empty, Size));
                        parts.Add(new KeyValuePair<int, Bitmap>(Scaled(pageScroll), part));
                        height = Scaled(pageScroll) + Height;
                        if (pageScroll < at) break;
                        int reached = pageScroll;
                        at = reached + Math.Max(100, pageView - 60);
                        ScrollPage(at);
                        if (pageScroll == reached) break;
                    }
                    using (Bitmap page = new Bitmap(Width, height))
                    using (Graphics graphics = Graphics.FromImage(page))
                    {
                        foreach (KeyValuePair<int, Bitmap> part in parts) graphics.DrawImageUnscaled(part.Value, 0, part.Key);
                        page.Save(Path.Combine(folder, name + "-page.png"), ImageFormat.Png);
                    }
                }
                finally { foreach (KeyValuePair<int, Bitmap> part in parts) part.Value.Dispose(); }
            }
            finally
            {
                ScrollPage(0);
                LayoutScale(dpiWas, textWas, fallbackWas);
                SizeMainframe(sizeWas);
            }
        }

        // SOUNDS greyed with Chimes off, and the tips on its rows, with Windows at 100%, 125% and 150%.
        internal void SoundsTips(string folder)
        {
            int dpiWas = layoutDpi, textWas = layoutText, sizeWas = capri.MainframeSize;
            bool fallbackWas = layoutFallback;
            try
            {
                foreach (int dpi in new[] { 96, 120, 144 })
                {
                    int percent = dpi * 100 / 96;
                    ShowSettings(true);
                    LayoutScale(dpi, 100);
                    SizeMainframe(100);
                    ShowSettings(true);
                    Application.DoEvents();
                    ScrollPage((int)Math.Round((chimes.Top + Scaled(pageScroll)) / Unit()) - 300);
                    Application.DoEvents();
                    foreach (Control control in new Control[] { chimes, chimeSet, chimeVolume, chimeHeard })
                    {
                        DismissHint();
                        dismissedHint = null;
                        overHint = null;
                        MoveOverHint(control);
                        AwaitHint(control);
                        if (HintVisible) SaveTipPreview(folder, "tip-" + (control == chimes ? "chimes" : control == chimeSet ? "set" : control == chimeVolume ? "volume" : "heard") + "-" + percent + ".png");
                    }
                    DismissHint();
                    chimes.Checked = false;
                    Application.DoEvents();
                    using (Bitmap view = new Bitmap(Width, Height))
                    {
                        DrawToBitmap(view, new Rectangle(Point.Empty, Size));
                        DrawRims(view);
                        view.Save(Path.Combine(folder, "chimes-off-" + percent + ".png"), ImageFormat.Png);
                    }
                    chimes.Checked = true;
                }
            }
            finally
            {
                DismissHint();
                ScrollPage(0);
                LayoutScale(dpiWas, textWas, fallbackWas);
                SizeMainframe(sizeWas);
            }
        }

        internal bool SoundsInPage
        {
            get
            {
                foreach (Control row in new Control[] { chimes, chimeSet, chimeVolume, chimeSent, chimeHeard, chimeListening })
                    if (Folded(row) || Hidden(row)) return false;
                return true;
            }
        }
    }
}
