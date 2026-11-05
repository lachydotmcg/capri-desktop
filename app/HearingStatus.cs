using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CapriDesktop
{
    internal enum HearingAction { Sound, Type, Privacy, Speech, Own, Windows, Test, Pick, Output }

    internal sealed class HearingStatus
    {
        internal readonly string Text;
        // The whole reason with its fix, when the one-line Text had to leave some of it out. It's what she says and VOICE shows.
        internal readonly string Full;
        internal readonly bool Problem;
        internal readonly HearingAction[] Actions;

        internal HearingStatus(string text, bool problem, params HearingAction[] actions) : this(text, null, problem, actions) { }

        internal HearingStatus(string text, string full, bool problem, params HearingAction[] actions)
        {
            Text = text;
            Full = full;
            Problem = problem;
            Actions = actions;
        }

        internal string Said { get { return Full ?? Text; } }

        // The hearing line fits about this many characters before it's cut off.
        internal const int LineRoom = 56;

        // A line made from a reason: the lead, like "Mic failed: ", then the reason's first sentence, short enough for the line.
        // A reason that names Windows speech itself needs no lead. Whatever the line leaves out, like the fix, goes in Full:
        // the reason as it is when it stands on its own, or with the lead.
        internal static HearingStatus Explained(string lead, string reason, bool alone, bool problem, params HearingAction[] actions)
        {
            string whole = Regex.Replace(reason ?? "", @"\s+", " ").Trim();
            if (whole.StartsWith("Windows speech ", StringComparison.Ordinal) && lead.StartsWith("Windows speech", StringComparison.Ordinal)) { lead = ""; alone = true; }
            string shown = Reason(whole, LineRoom - lead.Length);
            string full = shown == whole.TrimEnd('.') ? null : alone ? whole : lead + whole;
            return new HearingStatus(lead + shown, full, problem, actions);
        }

        internal static readonly string[] Choices = { "Auto", "Her own ears", "Windows speech" };
        // VB-CABLE's input as Windows' default speakers sends every app's sound into the cable, where nobody hears it, and its
        // setup can leave it that way (Neco Arc, 3 Nov: "I stop hear everything else"). Voicemeeter's input is meant to be the
        // default, and Steam's is only while it streams, so only VB-Audio's cables count.
        internal const string CableSpeakers = "Default speakers are CABLE Input";
        internal const string CableNotice = "Your PC's sound is going into CABLE Input, so you can't hear your other apps. Pick your speakers or headphones in Windows Sound settings.";
        // A Bluetooth headset's mic puts it on a call, which quiets everything else it plays. She still hears, so it's only a note.
        internal const string BluetoothMic = "Bluetooth mic: call mode quiets other apps";
        internal static bool CableOutput(string name) { return Regex.IsMatch(name ?? "", @"^(Hi-Fi )?CABLE(-[A-D])? Input\b", RegexOptions.IgnoreCase); }
        internal static int Choice(int value) { return value >= 0 && value < Choices.Length ? value : 0; }
        internal static bool Own(int choice, bool ready) { return Choice(choice) != 2 && ready; }
        internal static string Name(HearingAction action)
        {
            switch (action)
            {
                case HearingAction.Sound: return "Open Sound settings";
                case HearingAction.Output: return "Open output settings";
                case HearingAction.Type: return "Type instead";
                case HearingAction.Privacy: return "Open mic privacy";
                case HearingAction.Speech: return "Open speech settings";
                case HearingAction.Own: return "Use her own ears";
                case HearingAction.Windows: return "Use Windows speech";
                case HearingAction.Test: return "Test mic";
                default: return "Pick another mic";
            }
        }

        internal static string Reason(string text) { return Reason(text, LineRoom); }

        // One plain sentence for the line, without a lead-in the line already says. Too long, it keeps what comes before a colon
        // or a comma, and only then ends at a word with "...", never mid-word.
        internal static string Reason(string text, int room)
        {
            string reason = Regex.Replace(string.IsNullOrWhiteSpace(text) ? "No words came back" : text, @"\s+", " ").Trim();
            foreach (string lead in new[] { "Windows speech recognition didn't start: ", "Can't open your mic: " })
                if (reason.StartsWith(lead, StringComparison.Ordinal) && reason.Length > lead.Length) reason = reason.Substring(lead.Length);
            int stop = reason.IndexOf(". ", StringComparison.Ordinal);
            if (stop > 0) reason = reason.Substring(0, stop);
            reason = reason.TrimEnd('.');
            room = Math.Max(20, room);
            foreach (string mark in new[] { ": ", ", " })
            {
                int at = reason.IndexOf(mark, StringComparison.Ordinal);
                if (reason.Length > room && at >= 12) reason = reason.Substring(0, at);
            }
            if (reason.Length <= room) return reason;
            int cut = reason.LastIndexOf(' ', room - 3);
            return reason.Substring(0, cut > room / 2 ? cut : room - 3).TrimEnd(' ', ',', ';', ':') + "...";
        }
    }

    internal sealed partial class Companion
    {
        private Func<List<Mic>> hearingDevices = Ears.List;
        private Func<string> hearingDefault = Ears.DefaultDevice;
        private Func<string> hearingOutput = HearingOutput;
        private Func<string, string, bool> hearingHandsFree = Ears.IsHandsFree;
        private Func<bool> hearingPrivacy = Ears.PrivacyBlocked;
        private Func<bool?> hearingOnline = HearingWatch.OnlineSpeech;
        private Action<string> hearingDownloadNotice;
        private Action hearingDownload;
        private HearingWatch hearingWatch;
        private List<Mic> hearingMics = new List<Mic>();
        private string hearingDefaultId;
        private bool hearingCableOut;
        private bool hearingCableSaid;
        private Action<string, bool> hearingSoundNotice;
        private bool hearingBlocked;
        private string hearingCaptureProblem, hearingSpeechProblem, hearingResultProblem;
        private string hearingInstallProblem;
        private string hearingSaid;
        private bool hearingOffline, hearingSilent, hearingQuiet, hearingLoud, hearingTestSilent;
        // The mic Test found the mic muted in Windows. It's cleared by the next Test, a mic change or a reset.
        private bool hearingWindowsMuted;
        private int hearingMisses;
        private double hearingSoundAt, hearingLoudAt, hearingWordsAt, hearingSignalSeconds;
        private HearingStatus hearingStatus;

        internal int HearingChoice { get { return HearingStatus.Choice(preferences.Hearing); } }
        internal List<Mic> HearingMics { get { return new List<Mic>(hearingMics); } }

        private void StartHearingStatus()
        {
            hearingSoundNotice = EarsNote;
            hearingDownloadNotice = delegate(string text) { Notice.Tell(mainframe, text, "Her own ears", false); };
            hearingDownload = DownloadHearing;
            if (testing) hearingOnline = delegate { return null; };
            // A test machine with no mic at all (GitHub's runners) gets a stand-in, or every hearing check there reads "No mic found".
            if (testing && Ears.List().Count == 0)
            {
                hearingDevices = delegate { return new List<Mic> { new Mic("smoke-mic", "Microphone (stand-in)") }; };
                hearingDefault = delegate { return "smoke-mic"; };
            }
            RefreshHearingDevices();
            if (!testing) hearingWatch = new HearingWatch(delegate { Post(RefreshHearingDevices); });
        }

        internal void RefreshHearingDevices()
        {
            if (mainframe == null || mainframe.IsDisposed) return;
            RefreshSpeakers();
            List<Mic> before = hearingMics;
            string defaultBefore = hearingDefaultId;
            bool blockedBefore = hearingBlocked;
            hearingMics = hearingDevices();
            hearingDefaultId = hearingDefault();
            hearingBlocked = hearingPrivacy();
            hearingCableOut = HearingStatus.CableOutput(hearingOutput());
            bool? online = hearingOnline();
            bool speechChanged = online == true && hearingSpeechProblem == Dictation.PrivacyOff;
            if (speechChanged) hearingSpeechProblem = null;
            else if (online == false && HearingWindows && !(dictation != null && earsReady)) hearingSpeechProblem = Dictation.PrivacyOff;
            bool changed = defaultBefore != hearingDefaultId || blockedBefore != hearingBlocked || before.Count != hearingMics.Count;
            if (!changed) for (int i = 0; i < before.Count; i++) if (before[i].Id != hearingMics[i].Id || before[i].Name != hearingMics[i].Name) { changed = true; break; }
            if (changed)
            {
                hearingCaptureProblem = null;
                if (!hearingBlocked && hearingSpeechProblem == Dictation.MicBlocked || hearingMics.Count > 0 && hearingSpeechProblem == Dictation.NoMic) hearingSpeechProblem = null;
                ResetHearingSignal();
            }
            Mic chosen = Ears.Find(preferences.Mic, hearingMics);
            mainframe.ShowMic(chosen == null ? null : chosen.Name);
            if (speechChanged && chatting && local && (hearingOffline || earsAwaited)) { StopChat(); StartLocal(); return; }
            UpdateHearing();
        }

        private bool HearingWindows
        {
            get { return chatting && local ? dictation != null || hearingOffline : !HearingStatus.Own(HearingChoice, OwnEars) && HearingChoice != 1; }
        }

        private bool HearingLink
        {
            get { return chatting ? !local : voice.Key != null && voice.VoiceId != null && !ChatUsesEars(preferences.ListenCapri, preferences.Quiet, preferences.Silent, true) && !CallModeOn; }
        }

        // Windows' default speakers' name, for the cable check. Games and calls can each have their own default, so either counts.
        private static string HearingOutput()
        {
            string speakers = Ears.FullName(Ears.DefaultDevice(0, 0));
            string voice = Ears.FullName(Ears.DefaultDevice(0, 1));
            return HearingStatus.CableOutput(speakers) ? speakers : voice ?? speakers;
        }

        // The mic she hears through: Windows speech's, the one her ears opened, or the one they'd open, which is never VB-CABLE's.
        private Mic HearingMic(bool windows)
        {
            Mic chosen = Ears.Find(preferences.Mic, hearingMics);
            string id = windows && !hearingOffline ? dictation != null ? dictationMic : hearingDefaultId : ears.Listening ? ears.OpenedId : chosen == null ? hearingDefaultId : chosen.Id;
            Mic device = Ears.Find(id, hearingMics);
            if (device == null && windows && !string.IsNullOrEmpty(id)) foreach (Mic mic in hearingMics) if (id.IndexOf(mic.Id, StringComparison.OrdinalIgnoreCase) >= 0) { device = mic; break; }
            if (device == null && (!windows || hearingOffline) && MicMix.IsCableOut(Ears.FullName(id)) && hearingMics.Count > 0) device = hearingMics[0];
            return device;
        }

        // The cable as their speakers is said once a run, when she isn't thinking, talking or on a job.
        private void CheckSoundNotice()
        {
            if (hearingCableSaid || !hearingCableOut || talkState == TalkState.Thinking || StillSaying || job != null || working != null) return;
            hearingCableSaid = true;
            hearingSoundNotice(HearingStatus.CableNotice, true);
        }

        private HearingStatus ReadHearing()
        {
            if (hearingBlocked || hearingCaptureProblem == Dictation.MicBlocked || hearingSpeechProblem == Dictation.MicBlocked)
                return new HearingStatus("Windows is blocking the mic", true, HearingAction.Privacy);
            if (hearingMics.Count == 0 && string.IsNullOrEmpty(hearingDefaultId) && !ears.Listening || hearingCaptureProblem == Dictation.NoMic || hearingSpeechProblem == Dictation.NoMic)
                return new HearingStatus("No mic found", true, HearingAction.Sound, HearingAction.Type);
            if (hearingCaptureProblem != null)
                return HearingStatus.Explained("Mic failed: ", hearingCaptureProblem, true, true, HearingAction.Sound, HearingAction.Type);
            if (hearingCableOut) return new HearingStatus(HearingStatus.CableSpeakers, true, HearingAction.Output);
            if (ears.Muted) return new HearingStatus("Mic muted", true);
            if (hearingWindowsMuted) return new HearingStatus("Mic muted in Windows", true, HearingAction.Sound, HearingAction.Test);
            bool ownFits = parakeet.Fits;
            bool windows = HearingWindows && !HearingLink;
            if (!HearingLink && HearingChoice == 1 && !ownFits)
                return new HearingStatus(IntPtr.Size == 8 ? "Her ears don't know this language yet" : "Her ears need 64-bit Windows", true, HearingAction.Windows);
            if (!HearingLink && HearingChoice != 2 && hearingInstallProblem != null && !OwnEars && !settingUpEars)
                return HearingStatus.Explained("Her ears didn't download: ", hearingInstallProblem, false, true, HearingAction.Own, HearingAction.Windows);
            // VB-CABLE's output as Windows' default mic isn't Windows speech failing: she hears their real mic offline, and VOICE
            // gives the whole reason. The line names the cable, so they know which default to change.
            if (!HearingLink && hearingSpeechProblem == CableDefault) return new HearingStatus("Hearing offline: your default mic is CABLE Output", false, HearingAction.Sound);
            // Call mode picks offline hearing to keep each speaker apart, so the line is a grey note and VOICE says why.
            if (!HearingLink && hearingSpeechProblem == CallHearing) return new HearingStatus(CallHearingLine, false);
            if (!HearingLink && hearingSpeechProblem != null)
            {
                if (hearingSpeechProblem == Dictation.PrivacyOff)
                    return ownFits ? new HearingStatus("Online speech recognition is off", true, HearingAction.Speech, HearingAction.Own) : new HearingStatus("Online speech recognition is off", true, HearingAction.Speech);
                return ownFits ? HearingStatus.Explained("Windows speech failed: ", hearingSpeechProblem, false, true, HearingAction.Own, HearingAction.Type) : HearingStatus.Explained("Windows speech failed: ", hearingSpeechProblem, false, true, HearingAction.Speech, HearingAction.Type);
            }
            Mic chosen = Ears.Find(preferences.Mic, hearingMics);
            if (windows && !hearingOffline && chosen != null && Dictation.OtherMic(chosen.Id, chosen.Name, dictation != null ? dictationMic : hearingDefaultId) != null)
                return ownFits ? new HearingStatus("Windows speech hears a different mic: Windows default", true, HearingAction.Own, HearingAction.Sound) : new HearingStatus("Windows speech hears a different mic: Windows default", true, HearingAction.Sound);
            if (hearingSilent) return new HearingStatus("Silent for 20 s: is the right mic picked?", true, HearingAction.Test, HearingAction.Pick);
            if (hearingTestSilent) return new HearingStatus("No sound reached the mic test", true, HearingAction.Test, HearingAction.Pick);
            if (hearingQuiet) return new HearingStatus("Mic is too quiet: raise its input volume", true, HearingAction.Test, HearingAction.Pick);
            if (!HearingLink && hearingResultProblem != null)
            {
                string engine = windows ? "Windows speech" : "Her ears";
                return windows && !ownFits ? HearingStatus.Explained(engine + " failed: ", hearingResultProblem, false, true, HearingAction.Test, HearingAction.Type) : HearingStatus.Explained(engine + " failed: ", hearingResultProblem, false, true, windows ? HearingAction.Own : HearingAction.Windows);
            }
            if (!HearingLink && (settingUpEars || earsAwaited || HearingChoice == 1 && !OwnEars))
            {
                string progress = settingUpEars ? earsTotal > 0 ? (earsGot * 100 / earsTotal) + "%" : "starting" : SettingUpVoice ? "getting the speech engine" : "not installed";
                if (settingUpEars || SettingUpVoice) return windows && earsReady ? new HearingStatus("Her ears are downloading: " + progress + " · Windows speech on", false) : new HearingStatus("Her ears are downloading: " + progress, false, HearingAction.Type);
                return new HearingStatus("Her ears aren't installed", true, HearingAction.Own, HearingAction.Windows);
            }
            // Only once nothing's wrong, so it never hides a problem.
            Mic device = HearingMic(windows);
            if (device != null && hearingHandsFree(device.Id, device.Name))
                return windows && ownFits ? new HearingStatus(HearingStatus.BluetoothMic, false, HearingAction.Pick, HearingAction.Own) : new HearingStatus(HearingStatus.BluetoothMic, false, HearingAction.Pick);
            string engineName = HearingLink ? "ElevenLabs" : windows ? hearingOffline ? "Windows offline speech" : "Windows speech" : "her own ears";
            string name = device == null ? windows && !hearingOffline ? "default mic" : "mic" : device.Name;
            if (windows && !hearingOffline && device != null) name = "default mic (" + name + ")";
            bool open = chatting && (local ? earsReady : ears.Listening);
            return new HearingStatus("Hearing: " + engineName + " · " + name + " · " + (open ? "working" : chatting ? "starting" : "ready"), false);
        }

        private void UpdateHearing()
        {
            if (mainframe == null || mainframe.IsDisposed) return;
            bool problem = hearingStatus != null && hearingStatus.Problem;
            hearingStatus = ReadHearing();
            if (problem && !hearingStatus.Problem) earsMended++;
            mainframe.ShowHearing(hearingStatus);
        }

        private bool HearingNote(bool always = false)
        {
            UpdateHearing();
            if (hearingStatus.Text == HearingStatus.CableSpeakers) { CheckSoundNotice(); return true; }
            if (!always && !hearingStatus.Problem) return false;
            if (hearingSaid != hearingStatus.Text)
            {
                hearingSaid = hearingStatus.Text;
                // The whole reason with its fix, when the line had to shorten it.
                EarsNote(hearingStatus.Said, hearingStatus.Problem);
            }
            return true;
        }

        private void ResetHearingSignal()
        {
            hearingSilent = hearingQuiet = hearingLoud = hearingTestSilent = hearingWindowsMuted = false;
            hearingMisses = 0;
            hearingResultProblem = null;
            hearingSoundAt = hearingLoudAt = hearingWordsAt = clock.Elapsed.TotalSeconds;
            hearingSignalSeconds = 0;
        }

        private void HearingLevel(double level)
        {
            double now = clock.Elapsed.TotalSeconds;
            if (level >= MicAlive) hearingSoundAt = now;
            if (level >= QuietWake)
            {
                hearingLoudAt = now;
                hearingLoud = true;
                if (chatting && local && earsReady && !earsShut) hearingSignalSeconds += 0.1;
            }
            if ((hearingSilent || hearingTestSilent) && level >= MicAlive || hearingQuiet && level >= QuietWake)
            {
                hearingSilent = hearingQuiet = hearingTestSilent = false;
                UpdateHearing();
            }
        }

        // A quiet mic is far more often them not talking than a broken one, and a noise gate or noise suppression sends nothing at
        // all between lines. So silence, a quiet mic and lines with no words only show on the hearing line under VOICE, with its
        // Test and Pick buttons, and she never says them (Lachy, 2 Nov: warned after a quiet spell, while talking, and over her).
        private void CheckHearingSignal(double now)
        {
            if (!chatting || !(local ? earsReady : ears.Listening) || ears.Muted || talkState == TalkState.Thinking) return;
            if (local && hearingSignalSeconds >= 3 && now - hearingWordsAt >= MicQuietWait && hearingResultProblem == null)
            {
                hearingResultProblem = "No words came back";
                UpdateHearing();
            }
            bool silent = now - hearingSoundAt >= MicQuietWait;
            bool quiet = !silent && now - hearingLoudAt >= MicQuietWait;
            if (silent == hearingSilent && quiet == hearingQuiet) return;
            hearingSilent = silent;
            hearingQuiet = quiet;
            UpdateHearing();
        }

        private void HearingResult(string problem, bool success)
        {
            if (success)
            {
                hearingMisses = 0;
                hearingResultProblem = null;
                hearingWordsAt = clock.Elapsed.TotalSeconds;
                hearingSignalSeconds = 0;
            }
            else if (problem != null && problem != "No words came back") hearingResultProblem = problem;
            else if (hearingLoud && ++hearingMisses >= 3) hearingResultProblem = "No words came back";
            UpdateHearing();
        }

        private void HearingCaptureFailed(string problem, bool say)
        {
            if (problem != null && problem.IndexOf(Dictation.MicBlocked, StringComparison.Ordinal) >= 0) problem = Dictation.MicBlocked;
            if (problem != null && problem.IndexOf(Dictation.NoMic, StringComparison.Ordinal) >= 0) problem = Dictation.NoMic;
            hearingCaptureProblem = problem;
            if (say) HearingNote(); else UpdateHearing();
        }

        internal void HearingFix(HearingAction action)
        {
            switch (action)
            {
                case HearingAction.Sound: Browse("ms-settings:sound"); break;
                case HearingAction.Output: Browse("ms-settings:sound"); break;
                case HearingAction.Privacy: Browse("ms-settings:privacy-microphone"); break;
                case HearingAction.Speech: OpenSpeechSettings(); break;
                case HearingAction.Type: if (!typer.Visible) OpenTyper(); break;
                case HearingAction.Own: ChooseHearing(1); break;
                case HearingAction.Windows: ChooseHearing(2); break;
                case HearingAction.Test: TestMic(); break;
                case HearingAction.Pick: mainframe.PickHearingMic(); break;
            }
        }

        internal void ChooseHearing(int choice)
        {
            choice = HearingStatus.Choice(choice);
            if (choice == 1 && !OwnEars)
            {
                if (!parakeet.Fits)
                {
                    Notice.Tell(mainframe, IntPtr.Size == 8 ? "Her own ears don't know this language yet. Choose Windows speech." : "Her ears need 64-bit Windows. Choose Windows speech.", "Her own ears", true);
                    return;
                }
                if (!settingUpEars && !SettingUpVoice)
                {
                    long size = (parakeet.Installed ? 0 : parakeet.Size) + (kokoro.Installed ? 0 : LocalVoice.PackSize);
                    hearingDownloadNotice("Get her own ears: " + Engine.Amount(size) + ", one time. This includes the free speech engine if needed. Then she hears the picked mic offline.");
                }
            }
            bool restart = chatting && local;
            if (restart) StopChat();
            StopMicTest(false);
            preferences.Hearing = choice;
            hearingSpeechProblem = hearingCaptureProblem = null;
            hearingInstallProblem = null;
            ResetHearingSignal();
            SavePreferences();
            mainframe.Sync();
            if (choice == 1 && !OwnEars) hearingDownload();
            if (restart) StartLocal();
            UpdateHearing();
        }

        private void DownloadHearing()
        {
            if (testing) return;
            if (!kokoro.Installed && !SettingUpVoice) SetUpFreeVoice(true);
            GetFreeEars();
        }

        private void SmokeHearingStatus(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            Preferences preferencesWas = preferences;
            Func<List<Mic>> devicesWas = hearingDevices;
            Func<string> defaultWas = hearingDefault, speechDefaultWas = Dictation.DefaultMic;
            Func<string> outputWas = hearingOutput;
            Func<string, string, bool> handsFreeWas = hearingHandsFree;
            Func<bool> privacyWas = hearingPrivacy;
            Func<bool?> onlineWas = hearingOnline;
            Func<Dictation> makeWas = Dictation.Make;
            Action<string> browseWas = Browse, noticeWas = hearingDownloadNotice;
            Action downloadWas = hearingDownload;
            bool? ownWas = ownEarsTest, comingWas = earsComingTest;
            bool settingWas = settingUpEars, voiceSettingWas = SettingUpVoice;
            bool freeWas = FreeChatOn;
            string languageWas = parakeet.Language;
            string voiceKeyWas = voice.Key, voiceIdWas = voice.VoiceId;
            bool pageWas = mainframe.SettingsShown;
            bool simpleWas = simpleTest;
            // Its checks bring the Mainframe up, so it goes back down after: a minimized start stays minimized.
            bool mainframeShownWas = mainframe.Visible;
            System.Windows.Forms.FormWindowState mainframeStateWas = mainframe.WindowState;
            try
            {
                StopChat();
                preferences = new Preferences();
                voice.Key = voice.VoiceId = null;
                preferences.FreeChatTold = true;
                FreeChatOn = true;
                parakeet.Language = "en";
                ownEarsTest = true;
                earsComingTest = false;
                settingUpEars = SettingUpVoice = false;
                List<Mic> devices = new List<Mic> { new Mic("usb", "USB Mic"), new Mic("default", "Realtek") };
                string currentDefault = "default";
                bool privacy = false;
                hearingDevices = delegate { return new List<Mic>(devices); };
                hearingDefault = delegate { return currentDefault; };
                hearingOutput = delegate { return "Speakers"; };
                hearingHandsFree = delegate { return false; };
                Dictation.DefaultMic = delegate { return currentDefault; };
                hearingPrivacy = delegate { return privacy; };
                bool? online = null;
                hearingOnline = delegate { return online; };
                List<string> pages = new List<string>();
                Browse = pages.Add;
                List<string> downloads = new List<string>();
                hearingDownloadNotice = delegate(string size) { downloads.Add("notice: " + size); };
                hearingDownload = delegate { downloads.Add("download"); settingUpEars = true; earsGot = 40; earsTotal = 100; };
                List<StandInDictation> opened = new List<StandInDictation>();
                Dictation.Make = delegate { StandInDictation made = new StandInDictation(); opened.Add(made); return made; };
                mainframe.Restore();
                mainframe.ShowSettings(false);
                RefreshHearingDevices();
                Action<string, bool, string> expect = delegate(string words, bool warning, string buttons)
                {
                    UpdateHearing();
                    if (mainframe.HearingText != words || mainframe.HearingWarning != warning || mainframe.HearingButtons != buttons)
                        throw new Exception("Hearing state: expected " + words + " / " + buttons + "; got " + mainframe.HearingText + " / " + mainframe.HearingButtons + ".");
                    if (!mainframe.HearingActionsFit) throw new Exception("Hearing actions are hidden, unnamed or overlapping.");
                };
                Action<string> preview = delegate(string name)
                {
                    foreach (int dpi in new[] { 96, 144 })
                    {
                        mainframe.LayoutScale(dpi, 100);
                        mainframe.ShowSettings(false);
                        UpdateHearing();
                        using (Bitmap panel = new Bitmap(mainframe.Width, mainframe.Height))
                        {
                            mainframe.DrawToBitmap(panel, new Rectangle(Point.Empty, mainframe.Size));
                            panel.Save(Path.Combine(output, name + "-" + dpi * 100 / 96 + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                };
                voice.Key = "hearing-test-key";
                voice.VoiceId = "hearing-test-voice";
                ShowVoice(false);
                expect("Hearing: ElevenLabs · Realtek · ready", false, "");
                voice.Key = voice.VoiceId = null;
                ShowVoice(false);
                expect("Hearing: her own ears · Realtek · ready", false, "");
                preferences.Mic = "usb";
                ears.Chosen = "usb";
                StartLocal();
                expect("Hearing: her own ears · USB Mic · working", false, "");
                if (opened.Count != 0 || Hearing.Own != parakeet) throw new Exception("Auto didn't prefer ready own ears.");
                preview("working");
                StopChat();
                ownEarsTest = false;
                preferences.Mic = null;
                StartLocal();
                expect("Hearing: Windows speech · default mic (Realtek) · working", false, "");
                if (dictation == null || opened.Count != 1 || Hearing.Own != null) throw new Exception("Auto didn't fall back to Windows speech.");
                currentDefault = "usb";
                RefreshHearingDevices();
                CheckHearing(dictationMicAt + 1);
                expect("Hearing: Windows speech · default mic (USB Mic) · working", false, "");
                if (opened.Count != 2 || !opened[0].Disposed) throw new Exception("Default input change didn't restart Windows speech.");
                ownEarsTest = true;
                if (!EarsArrived() || dictation != null || !opened[1].Disposed || Hearing.Own != parakeet) throw new Exception("Auto didn't hand Windows speech over to newly ready own ears.");
                StopChat();
                ownEarsTest = false;
                currentDefault = "default";
                devices.Clear();
                currentDefault = null;
                RefreshHearingDevices();
                expect("No mic found", true, "Open Sound settings | Type instead");
                mainframe.PressHearing(0);
                mainframe.PressHearing(1);
                if (pages.Count != 1 || pages[0] != "ms-settings:sound" || !typer.Visible) throw new Exception("No-mic actions didn't open fake Sound settings and the type box.");
                typer.Hide();
                preview("no-mic");
                devices.Add(new Mic("usb", "USB Mic")); devices.Add(new Mic("default", "Realtek"));
                currentDefault = "default";
                privacy = true;
                RefreshHearingDevices();
                expect("Windows is blocking the mic", true, "Open mic privacy");
                mainframe.PressHearing(0);
                if (pages[pages.Count - 1] != "ms-settings:privacy-microphone") throw new Exception("Privacy action opened the wrong settings.");
                preview("blocked");
                privacy = false;
                RefreshHearingDevices();
                StartLocal();
                double now = clock.Elapsed.TotalSeconds;
                hearingSoundAt = hearingLoudAt = now - 21;
                int said = asking;
                CheckHearingSignal(now);
                expect("Silent for 20 s: is the right mic picked?", true, "Test mic | Pick another mic");
                if (asking != said || reply == mainframe.HearingText) throw new Exception("A silent mic was said out loud; it belongs on the hearing line.");
                CheckHearingSignal(now + 1);
                if (asking != said) throw new Exception("A silent mic was said out loud on a later check.");
                preview("silent");
                mainframe.PressHearing(0);
                if (!mainframe.MicTesting || micTest == null || micTest.Listening) throw new Exception("Test action didn't use the smoke-mode input.");
                StopMicTest(false);
                mainframe.PressHearing(1);
                if (!mainframe.HearingMicMenuShown) throw new Exception("Pick action didn't open the mic picker.");
                mainframe.CloseHearingMicMenu();
                HeardLevel(0.002);
                hearingLoudAt = now - 21;
                CheckHearingSignal(now);
                expect("Mic is too quiet: raise its input volume", true, "Test mic | Pick another mic");
                HeardLevel(0.03);
                expect("Hearing: Windows speech · default mic (Realtek) · working", false, "");
                // A real problem while she's still saying a reply shows under VOICE, waits for her to finish, then is said once
                // unless her hearing recovered meanwhile. A reply has its own ticket, which tells it from her last ears note.
                talkState = TalkState.Talking;
                int midLine = ++asking;
                EarsNote("Mic failed: test", true);
                if (asking != midLine || mainframe.VoiceNoteText != "Mic failed: test") throw new Exception("A hearing problem cut into her line.");
                talkState = TalkState.Online;
                voiceUntil = 0;
                SayHeldEarsNote();
                if (asking == midLine || reply != "Mic failed: test") throw new Exception("A hearing problem held for her line was never said.");
                // Her ears failing on a line while she talks hold "Say it again?", though VOICE then shows what went wrong.
                talkState = TalkState.Talking;
                midLine = ++asking;
                earsWarned = -100;
                HeardLocally(chatTicket, asking, null, "Test ears failure.");
                bool glitchHeld = asking == midLine && mainframe.VoiceNoteText == "Test ears failure.";
                talkState = TalkState.Online;
                SayHeldEarsNote();
                bool glitchSaid = asking != midLine && reply == EarsGlitched;
                HearingResult(null, true);
                // A mic unplugged while she talks is held too, and plugging one back in before she's done drops it, though VOICE
                // still says it.
                talkState = TalkState.Talking;
                midLine = ++asking;
                devices.Clear();
                currentDefault = null;
                RefreshHearingDevices();
                hearingSaid = null;
                HearingNote();
                bool unplugHeld = asking == midLine && mainframe.VoiceNoteText == "No mic found";
                devices.Add(new Mic("usb", "USB Mic")); devices.Add(new Mic("default", "Realtek"));
                currentDefault = "default";
                RefreshHearingDevices();
                talkState = TalkState.Online;
                SayHeldEarsNote();
                if (!glitchHeld || !glitchSaid || !unplugHeld || asking != midLine)
                    throw new Exception("A held hearing problem went wrong: glitch held " + glitchHeld + ", glitch said " + glitchSaid + ", unplugged held " + unplugHeld + ", said after the mic was back " + (asking != midLine) + ", VOICE " + mainframe.VoiceNoteText);
                StopChat();
                checks.Add("PASS hearing: working own ears and Windows default mic, route changes, no mic, blocked, silent and quiet show their exact words and only fitting accessible actions; Sound, privacy, type, test and picker actions use stand-ins; silence shows on the hearing line without being said and recovers; a problem never cuts into her line and is said after it only while it's still the problem");

                Dictation.Make = delegate { return new StandInDictation { Problem = Dictation.PrivacyOff }; };
                StartLocal();
                expect("Online speech recognition is off", true, "Open speech settings | Use her own ears");
                if (!hearingOffline || !earsReady || dictation != null) throw new Exception("Auto didn't keep its existing Windows offline fallback.");
                if (reply != mainframe.HearingText || mainframe.VoiceNoteText != reply) throw new Exception("Speech-off chat note and hearing line disagree.");
                mainframe.PressHearing(0);
                if (pages[pages.Count - 1] != SpeechSettings) throw new Exception("Speech action opened the wrong settings.");
                mainframe.PressHearing(1);
                if (HearingChoice != 1 || downloads.Count != 2 || !downloads[0].StartsWith("notice: Get her own ears: " + Engine.Amount(parakeet.Size + (kokoro.Installed ? 0 : LocalVoice.PackSize)), StringComparison.Ordinal) || downloads[1] != "download") throw new Exception("Own ears didn't state the whole size before calling the existing installer.");
                expect("Her ears are downloading: 40%", false, "Type instead");
                earsGot = 60;
                ShowInstall();
                expect("Her ears are downloading: 60%", false, "Type instead");
                StopChat();
                settingUpEars = false;
                ShowInstall();
                ownEarsTest = true;
                Dictation.Make = delegate { return new StandInDictation(); };
                ChooseHearing(2);
                StartLocal();
                if (dictation == null || Hearing.Own != null || mainframe.HearingShown != "Windows speech") throw new Exception("Windows choice was ignored while own ears were ready.");
                Dictation keptWindows = dictation;
                if (EarsArrived() || dictation != keptWindows) throw new Exception("Downloaded ears replaced an explicit Windows choice.");
                ownEarsTest = false;
                preferences.Mic = "usb";
                ears.Chosen = "usb";
                RefreshHearingDevices();
                expect("Windows speech hears a different mic: Windows default", true, "Use her own ears | Open Sound settings");
                HearingNote();
                if (reply != mainframe.HearingText) throw new Exception("Different-mic chat note disagrees with the hearing line.");
                mainframe.PressHearing(1);
                if (pages[pages.Count - 1] != "ms-settings:sound") throw new Exception("Default-mic fix opened the wrong page.");
                preferences.Mic = null;
                ears.Chosen = null;
                RefreshHearingDevices();
                HeardLevel(0.03);
                Dictation rejected = dictation;
                rejected.Rejected(null); rejected.Rejected(null); rejected.Rejected(null);
                expect("Windows speech failed: No words came back", true, "Use her own ears");
                ownEarsTest = true;
                mainframe.PressHearing(0);
                if (HearingChoice != 1 || dictation != null || Hearing.Own != parakeet) throw new Exception("Switch from Windows speech to own ears failed.");
                HeardLevel(0.03);
                HearingResult("No words came back", false); HearingResult("No words came back", false); HearingResult("No words came back", false);
                expect("Her ears failed: No words came back", true, "Use Windows speech");
                mainframe.PressHearing(0);
                if (HearingChoice != 2 || dictation == null || Hearing.Own != null) throw new Exception("Switch from own ears to Windows speech failed.");
                HeardLevel(0.03);
                hearingSignalSeconds = 3;
                hearingWordsAt = clock.Elapsed.TotalSeconds - 21;
                CheckHearingSignal(clock.Elapsed.TotalSeconds);
                expect("Windows speech failed: No words came back", true, "Use her own ears");
                dictation.Partial("I can hear words");
                expect("Hearing: Windows speech · default mic (Realtek) · working", false, "");
                HearingResult("The recognizer could not load", false);
                expect("Windows speech failed: The recognizer could not load", true, "Use her own ears");
                HearingResult(null, true);
                StopChat();
                Dictation.Make = delegate { return new StandInDictation { Problem = "The connection was lost." }; };
                StartLocal();
                expect("Windows speech failed: The connection was lost", true, "Use her own ears | Type instead");
                if (reply != mainframe.HearingText) throw new Exception("Failed Windows speech chat note disagrees with the hearing line.");
                StopChat();
                HearingCaptureFailed("WASAPI start failed. " + Dictation.MicBlocked, false);
                expect("Windows is blocking the mic", true, "Open mic privacy");
                HearingCaptureFailed(Dictation.NoMic, false);
                expect("No mic found", true, "Open Sound settings | Type instead");
                HearingCaptureFailed("The selected device is busy.", false);
                expect("Mic failed: The selected device is busy", true, "Open Sound settings | Type instead");
                HearingCaptureFailed(null, false);
                hearingSpeechProblem = null;
                checks.Add("PASS hearing: speech privacy, different Windows mic, recognizer failure and three empty results with good levels offer the right engine or settings; buttons switch actual stand-in sessions, state the download size before installation, and show progress; chat notes use the status text");

                foreach (int choice in new[] { 0, 1, 2 })
                {
                    mainframe.PickHearingChoice(choice);
                    Preferences loaded = LoadPreferences();
                    if (loaded.Hearing != choice || HearingStatus.Choice(loaded.Hearing) != choice || mainframe.HearingShown != HearingStatus.Choices[choice]) throw new Exception("Hearing preference didn't round-trip.");
                }
                using (MemoryStream empty = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")))
                {
                    Preferences old = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(empty);
                    if (HearingStatus.Choice(old.Hearing) != 0 || HearingStatus.Choice(-1) != 0 || HearingStatus.Choice(99) != 0) throw new Exception("Old or invalid preferences didn't default to Auto.");
                }
                preferences.Hearing = 1;
                Dictation.Make = delegate { throw new Exception("Own ears tried Windows speech"); };
                StartLocal();
                if (!earsReady || Hearing.Own != parakeet) throw new Exception("Own choice didn't use her ready ears.");
                StopChat();
                ownEarsTest = false;
                StartLocal();
                if (earsReady || !earsAwaited || dictation != null) throw new Exception("Own choice silently used Windows while the model was missing.");
                StopChat();
                ownEarsTest = false;
                preferences.Hearing = 2;
                earsComingTest = true;
                Dictation.Make = delegate { return new StandInDictation { Problem = Dictation.PrivacyOff }; };
                StartLocal();
                if (!hearingOffline || !earsReady || earsAwaited) throw new Exception("Windows choice waited for own ears after Windows speech failed.");
                Dictation.Make = delegate { return new StandInDictation(); };
                online = true;
                RefreshHearingDevices();
                if (hearingOffline || dictation == null || !earsReady || hearingSpeechProblem != null) throw new Exception("Enabling Online speech didn't clear the old problem and restart Windows speech.");
                StopChat();
                online = false;
                RefreshHearingDevices();
                expect("Online speech recognition is off", true, "Open speech settings | Use her own ears");
                online = null;
                hearingSpeechProblem = null;
                preferences.Hearing = 1;
                parakeet.Language = "ja";
                expect("Her ears don't know this language yet", true, "Use Windows speech");
                parakeet.Language = "en";
                int changes = 0;
                HearingChanges notifications = new HearingChanges(delegate { changes++; });
                notifications.OnDefaultDeviceChanged(0, 0, "speakers");
                notifications.OnDefaultDeviceChanged(1, 0, "usb");
                notifications.OnDeviceAdded("usb");
                notifications.OnDeviceRemoved("usb");
                notifications.OnDeviceStateChanged("usb", 1);
                notifications.OnPropertyValueChanged("usb", new HearingProperty());
                if (changes != 6) throw new Exception("Mic or speaker changes weren't event driven.");
                // A burst of changes from Windows reads her mics once, and the next change waits out the second.
                int told = 0;
                long firstAt = -1, secondAt = -1;
                System.Diagnostics.Stopwatch burstClock = System.Diagnostics.Stopwatch.StartNew();
                using (HearingWatch burst = new HearingWatch(delegate
                {
                    long at = burstClock.ElapsedMilliseconds;
                    if (Interlocked.Increment(ref told) == 1) Interlocked.Exchange(ref firstAt, at); else Interlocked.Exchange(ref secondAt, at);
                }, false))
                {
                    Func<int, bool> reached = delegate(int count)
                    {
                        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                        while (Volatile.Read(ref told) < count && waited.ElapsedMilliseconds < 5000) Thread.Sleep(20);
                        return Volatile.Read(ref told) >= count;
                    };
                    for (int i = 0; i < 200; i++) burst.Poke();
                    bool once = reached(1);
                    Thread.Sleep(burst.Settle);
                    int afterBurst = Volatile.Read(ref told);
                    burst.Poke();
                    bool again = reached(2);
                    long apart = Interlocked.Read(ref secondAt) - Interlocked.Read(ref firstAt);
                    if (!once || afterBurst != 1 || !again || apart < burst.Spacing - 50)
                        throw new Exception("A burst of mic changes wasn't read once, or the next one came within a second: " + afterBurst + " then " + Volatile.Read(ref told) + ", " + apart + " ms apart.");
                }
                checks.Add("PASS hearing recovery: sustained good input with no results offers the other engine; partial or final words clear the problem; recognizer errors are immediate; Windows stays selected when ears arrive, speech privacy changes restart a fallback session, unsupported own-ear languages explain the limit, and synthetic endpoint events update without polling, a burst of them as one read at most once a second");
                checks.Add("PASS hearing choice saves and loads Auto, Her own ears and Windows speech; missing and invalid values mean Auto; Auto preserves own-then-Windows order and explicit choices stay on their engine while models arrive");

                ownEarsTest = true;
                hearingSpeechProblem = hearingCaptureProblem = null;
                ResetHearingSignal();
                mainframe.VoiceNote("", false);
                foreach (int dpi in new[] { 96, 144, 192 })
                {
                    mainframe.LayoutScale(dpi, 100);
                    foreach (bool advanced in new[] { false, true })
                    {
                        preferences.AdvancedSettings = advanced;
                        simpleTest = !advanced;
                        mainframe.ShowSettings(true);
                        string overlap = mainframe.Overlap(), outside = mainframe.PageClamped ? null : mainframe.Outside(), text = mainframe.TextFits();
                        if (overlap != null || outside != null || text != null || !mainframe.HearingChoiceFits)
                            throw new Exception("Hearing layout " + dpi + " dpi, " + (advanced ? "Advanced" : "Simple") + ": overlap " + overlap + "; outside " + outside + "; text " + text + ".");
                        mainframe.ShowSettings(false);
                        foreach (HearingStatus state in new[] {
                            new HearingStatus("Hearing: her own ears · USB Mic · working", false),
                            new HearingStatus("No mic found", true, HearingAction.Sound, HearingAction.Type),
                            new HearingStatus("Windows is blocking the mic", true, HearingAction.Privacy),
                            new HearingStatus("Silent for 20 s: is the right mic picked?", true, HearingAction.Test, HearingAction.Pick),
                            new HearingStatus("Online speech recognition is off", true, HearingAction.Speech, HearingAction.Own),
                            new HearingStatus("Windows speech hears a different mic: Windows default", true, HearingAction.Own, HearingAction.Sound) })
                        {
                            mainframe.ShowHearing(state);
                            mainframe.VoiceNote(state.Text, state.Problem);
                            overlap = mainframe.Overlap(); text = mainframe.TextFits();
                            if (overlap != null || text != null || !mainframe.HearingActionsFit) throw new Exception("Hearing main layout " + dpi + ": " + overlap + "; " + text + ".");
                        }
                    }
                }
                foreach (string ask in new[] { "why can't you hear me", "my mic isn't working", "how do I change how you hear me" })
                {
                    string facts = SelfKnow.Matching(ask);
                    if (facts == null || facts.IndexOf("Hearing", StringComparison.OrdinalIgnoreCase) < 0 && facts.IndexOf("hearing line", StringComparison.Ordinal) < 0) throw new Exception("Hearing help didn't match: " + ask);
                    if (facts.IndexOf('\u2014') >= 0) throw new Exception("Hearing help has an em dash.");
                }
                checks.Add("PASS hearing controls, labels and buttons fit at 100%, 150% and 200%, in Simple and Advanced Settings; new controls are accessible, no rows overlap, help matches the three support questions, and eight previews show working, no mic, blocked and silent at 100% and 150%");
            }
            finally
            {
                StopMicTest(false);
                StopChat();
                typer.Hide();
                preferences = preferencesWas;
                hearingDevices = devicesWas; hearingDefault = defaultWas; hearingPrivacy = privacyWas; hearingOnline = onlineWas;
                hearingOutput = outputWas; hearingHandsFree = handsFreeWas;
                Dictation.DefaultMic = speechDefaultWas; Dictation.Make = makeWas;
                Browse = browseWas; hearingDownloadNotice = noticeWas; hearingDownload = downloadWas;
                ownEarsTest = ownWas; earsComingTest = comingWas;
                settingUpEars = settingWas; SettingUpVoice = voiceSettingWas;
                FreeChatOn = freeWas; parakeet.Language = languageWas;
                voice.Key = voiceKeyWas; voice.VoiceId = voiceIdWas;
                simpleTest = simpleWas;
                hearingCaptureProblem = hearingSpeechProblem = hearingInstallProblem = null;
                ResetHearingSignal();
                ears.Chosen = preferences.Mic;
                RefreshHearingDevices();
                ShowInstall();
                mainframe.LayoutScale(0, 0);
                mainframe.ShowSettings(pageWas);
                mainframe.Sync();
                SavePreferences();
                if (!mainframeShownWas) mainframe.Hide();
                else if (mainframe.WindowState != mainframeStateWas) mainframe.WindowState = mainframeStateWas;
            }
        }
    }

    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IHearingChanges
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, int state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, HearingProperty key);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct HearingProperty { internal Guid Format; internal int Id; }

    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    internal sealed class HearingChanges : IHearingChanges
    {
        private readonly Action changed;
        internal HearingChanges(Action changed) { this.changed = changed; }
        public int OnDeviceStateChanged(string id, int state) { changed(); return 0; }
        public int OnDeviceAdded(string id) { changed(); return 0; }
        public int OnDeviceRemoved(string id) { changed(); return 0; }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { if (flow == 0 || flow == 1) changed(); return 0; }
        public int OnPropertyValueChanged(string id, HearingProperty key) { changed(); return 0; }
    }

    // Windows can tell of a change many times a second: a driver rewriting a headset's properties, any app opening a mic, a
    // Bluetooth headset going on a call. Each one read every mic again on her window's thread, so a burst of them could hold
    // her up (Neco Arc, 3 Nov: she lagged and froze with Mainframe half drawn). A burst now settles and comes as one, at most
    // once a second.
    internal sealed class HearingWatch : IDisposable
    {
        internal int Settle = 300, Spacing = 1000;
        private readonly ManualResetEvent closed = new ManualResetEvent(false);
        private readonly AutoResetEvent poked = new AutoResetEvent(false);
        private readonly List<RegistryKey> keys = new List<RegistryKey>();
        private readonly List<AutoResetEvent> changes = new List<AutoResetEvent>();
        private ISoundDevices devices;
        private HearingChanges endpoints;
        private readonly Thread worker;

        [DllImport("advapi32.dll")] private static extern int RegNotifyChangeKeyValue(IntPtr key, bool tree, int filter, IntPtr signal, bool async);

        internal static bool? OnlineSpeech()
        {
            try
            {
                object value = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", null);
                return value is int ? (bool?)((int)value != 0) : null;
            }
            catch (System.Security.SecurityException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (IOException) { return null; }
        }

        internal HearingWatch(Action notify) : this(notify, true) { }

        // The smoke test watches nothing of Windows' and pokes it itself.
        internal HearingWatch(Action notify, bool windows)
        {
            if (windows)
            {
                try
                {
                    devices = (ISoundDevices)new SoundDevices();
                    endpoints = new HearingChanges(Poke);
                    Marshal.ThrowExceptionForHR(devices.RegisterEndpointNotificationCallback(endpoints));
                }
                catch (Exception error) { Brain.Log("ears", -1, "Mic change notifications: " + Voice.Short(error.Message)); }
                foreach (RegistryKey root in new[] { Registry.CurrentUser, Registry.LocalMachine })
                    foreach (string path in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore", @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio", @"SOFTWARE\Microsoft\Multimedia\Audio", @"SOFTWARE\Microsoft\Speech_OneCore\Settings" })
                    {
                        try { RegistryKey key = root.OpenSubKey(path); if (key != null) { keys.Add(key); changes.Add(new AutoResetEvent(false)); } }
                        catch (System.Security.SecurityException) { }
                        catch (UnauthorizedAccessException) { }
                        catch (IOException) { }
                    }
            }
            worker = new Thread(delegate() { Watch(notify); }) { IsBackground = true, Name = "Capri hearing changes" };
            worker.Start();
        }

        // Windows' endpoint changes come on its own threads, so they only wake her watcher.
        internal void Poke()
        {
            try { poked.Set(); }
            catch (ObjectDisposedException) { }
        }

        private void Watch(Action notify)
        {
            List<WaitHandle> signals = new List<WaitHandle> { closed, poked };
            signals.AddRange(changes.ToArray());
            WaitHandle[] waits = signals.ToArray();
            for (int i = 0; i < keys.Count; i++) RegNotifyChangeKeyValue(keys[i].Handle.DangerousGetHandle(), true, 5, changes[i].SafeWaitHandle.DangerousGetHandle(), true);
            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            long due = -1, told = -Spacing;
            while (true)
            {
                int at = WaitHandle.WaitAny(waits, due < 0 ? Timeout.Infinite : (int)Math.Max(0, due - clock.ElapsedMilliseconds));
                if (at == 0) return;
                if (at == WaitHandle.WaitTimeout)
                {
                    due = -1;
                    told = clock.ElapsedMilliseconds;
                    notify();
                    continue;
                }
                if (at >= 2) RegNotifyChangeKeyValue(keys[at - 2].Handle.DangerousGetHandle(), true, 5, changes[at - 2].SafeWaitHandle.DangerousGetHandle(), true);
                if (due < 0) due = Math.Max(clock.ElapsedMilliseconds + Settle, told + Spacing);
            }
        }

        public void Dispose()
        {
            closed.Set();
            worker.Join();
            if (devices != null)
            {
                try { if (endpoints != null) devices.UnregisterEndpointNotificationCallback(endpoints); } catch (COMException) { }
                Marshal.ReleaseComObject(devices);
            }
            foreach (RegistryKey key in keys) key.Dispose();
            foreach (AutoResetEvent change in changes) change.Dispose();
            closed.Dispose();
            poked.Dispose();
        }
    }
}
