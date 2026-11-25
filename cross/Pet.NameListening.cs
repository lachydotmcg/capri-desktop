using System;
using System.IO;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        // A new opt-in: older dormant microphone flags do not grant consent.
        [DataMember] internal bool ListenCapri;
        [DataMember] internal bool ListenCapriHold;
        [DataMember] internal string ListenCapriHoldContext;
    }

    internal static class NameListeningText
    {
        internal static bool Named(string text)
        {
            return Regex.IsMatch(text ?? "", @"(?<!\w)capri(?!\w)", RegexOptions.IgnoreCase);
        }

        internal static string Request(string text)
        {
            text = (text ?? "").Trim();
            text = Regex.Replace(text, @"^(?:hey\s+)?capri(?:[\s,:.!?]+|$)", "", RegexOptions.IgnoreCase);
            return Regex.Replace(text, @"[\s,:.!?]+capri[.!?]*$", "", RegexOptions.IgnoreCase).Trim();
        }

        // The Python gate decides which lines listening accepts, so her name on its own is decided the same way
        // bridge/speech/name_chat.py's name_alone decides it: her name, then nothing but the greetings she answers to.
        // Python's word characters are letters, numbers and underscore; .NET's \w also counts combining marks, so the
        // name boundary is spelled out instead of \w to find the same name the Python gate finds.
        // Python's re.IGNORECASE puts the Turkish I pair on i (I, i, İ and ı all match i); .NET's culture-sensitive
        // IgnoreCase instead follows the current culture, so under tr-TR an ASCII I folds to ı, while invariant casing
        // leaves İ and ı alone. Lowercase with the invariant rules, fold those two onto i by hand, and match the
        // lowercase patterns with CultureInvariant so the answer can never depend on the machine's locale.
        internal static bool NameAlone(string text)
        {
            if (text == null) return false;
            // The Python gate reads the NFKC form before deciding, so her name on its own is decided on that form too.
            string normalized;
            try { normalized = text.Normalize(NormalizationForm.FormKC); }
            catch (ArgumentException) { normalized = text; }
            normalized = normalized.ToLowerInvariant().Replace('\u0130', 'i').Replace('\u0131', 'i');
            const string word = @"[\p{L}\p{N}_]";
            string name = @"(?<!" + word + @")capri(?!" + word + @")";
            if (!Regex.IsMatch(normalized, name, RegexOptions.CultureInvariant)) return false;
            string left = Regex.Replace(normalized, name, " ", RegexOptions.CultureInvariant);
            foreach (string part in Regex.Split(left, @"[^\p{L}\p{N}]+"))
                if (part.Length > 0 && !Regex.IsMatch(part, @"\A(?:hey|hi|hiya|hello|yo|ok|okay|oi|um|uh)\z", RegexOptions.CultureInvariant))
                    return false;
            return true;
        }
    }

    internal sealed partial class Pet
    {
        private sealed record NameContext(IBrainProvider Provider, string Model, Cloud Remote, string RemoteModel,
            object Local, string Session, string Source, string Fingerprint, string Python, string Manifest,
            string RemoteKey, string VoiceConfiguration);
        private readonly OwnedNameEars nameEars = new OwnedNameEars();
        private NameContext nameContext;
        private Task nameLifetime = Task.CompletedTask;
        private long nameEpoch;
        private bool nameStopped = true, nameStartup, nameClosed, nameChecking, nameStarting;
        // Her current stopped state was caused by the lock, so only a detected unlock may bring her back.
        private bool nameLockPaused;
        // A manual activation still owes its "can't always tell" note, shown only once she really begins capture.
        private bool nameActivationNote;
        private string nameIdentity, nameStatus = "Local name listening is off.";
        private double nextNameIdentityCheck, macSpeechUntil;

        internal bool NameListeningEnabled => Saved.ListenCapri && !nameStopped;
        internal string NameListeningStatus => nameStatus;
        internal bool NameBarredNow { get { return NameBarred; } }
        // Hyprland answers at once. A missing snapshot is not an answer, so it defers to logind; an answer nobody can read
        // never bars her, so the switch keeps working on desktops that can't report a lock at all.
        internal static Func<bool?> LockedForNameListening = delegate
        {
            if (!OperatingSystem.IsLinux()) return false;
            return LockedFromDesktop(HyprlandDesktop.Current?.VisualSnapshot, Limbs.SessionLocked);
        };
        // A live snapshot speaks for itself; a missing one defers to logind, the explicit signal the design accepts. Both
        // missing is unknown, which never bars her and never holds her.
        internal static bool? LockedFromDesktop(HyprlandSnapshot snapshot, Func<bool?> logind)
        {
            if (snapshot != null) return snapshot.Locked;
            return logind == null ? null : logind();
        }
        // Only a snapshot that exists can report a lock; an absent one is unknown.
        internal static bool? LockedFromSnapshot(HyprlandSnapshot snapshot) => snapshot == null ? null : (bool?)snapshot.Locked;
        internal static bool NameBarredByLock() => LockedForNameListening() == true;
        private bool NameBarred => NameBarredAt(LockedForNameListening());
        // The tick takes one lock read and passes it here, so its observation and its stop branch can never disagree.
        private bool NameBarredAt(bool? locked) => nameClosed || quitting || away || Hushed || Asleep || Listening ||
            Talk?.Typer.Secret == true || locked == true;

        private NameContext CaptureNameContext() => new NameContext(Brain.Provider, Brain.Model, Brain.Remote,
            Brain.Remote?.Model, Brain.Local, HyprlandDesktop.Current?.SessionId, Saved.MicrophoneNode,
            Saved.MicrophoneFingerprint, Saved.OwnedAsrPython, Saved.OwnedAsrManifest,
            Brain.Remote?.Key, Saved.VoiceProvider + "|" + Saved.VoiceId + "|" + Saved.OwnedVoicePython + "|" + Saved.OwnedVoiceProfile + "|" + Saved.Silent + "|" + Saved.LocalVoiceUrl + "|" + Saved.LocalVoiceModel + "|" + Saved.LocalVoiceName + "|" + Saved.LocalVoiceFormat + "|" + Saved.SpeechModel);

        private bool SameNameContext(NameContext value)
        {
            if (value == null) return false;
            return ReferenceEquals(value.Provider, Brain.Provider) && ReferenceEquals(value.Remote, Brain.Remote) &&
                ReferenceEquals(value.Local, Brain.Local) && value.Model == Brain.Model && value.RemoteModel == Brain.Remote?.Model &&
                value.RemoteKey == Brain.Remote?.Key && value.Session == HyprlandDesktop.Current?.SessionId &&
                value.Source == Saved.MicrophoneNode && value.Fingerprint == Saved.MicrophoneFingerprint &&
                value.Python == Saved.OwnedAsrPython && value.Manifest == Saved.OwnedAsrManifest &&
                value.VoiceConfiguration == Saved.VoiceProvider + "|" + Saved.VoiceId + "|" + Saved.OwnedVoicePython + "|" +
                    Saved.OwnedVoiceProfile + "|" + Saved.Silent + "|" + Saved.LocalVoiceUrl + "|" + Saved.LocalVoiceModel + "|" +
                    Saved.LocalVoiceName + "|" + Saved.LocalVoiceFormat + "|" + Saved.SpeechModel;
        }

        // Provider.Identity may read active.json. Only workers call this method.
        private static string NameIdentity(NameContext value)
        {
            string account = value.Provider?.Identity ?? "local";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(account + "|" + value.Model + "|" +
                value.RemoteModel + "|" + value.Session + "|" + value.Source + "|" + value.Fingerprint)));
        }

        // The start path takes its own lock read; the tick passes the one it already took, so a tick never reads twice.
        private NameListeningState NameState() => NameState(LockedForNameListening());
        private NameListeningState NameState(bool? observedLock) => new NameListeningState(nameIdentity, Saved.MicrophoneNode,
            Saved.MicrophoneFingerprint, NameBarredAt(observedLock), SpeechAudio.MicrophoneHeld || Chimes.Masking || (OperatingSystem.IsMacOS() && Now < macSpeechUntil),
            Talk?.ThinkingNow == true || ComputerJobs.Active, ComputerJobs.GameRunning);

        private void InitializeNameListening()
        {
            SpeechAudio.BeforePlayback += GateNamePlayback;
            Chimes.BeforePlay = delegate { if (Saved.ListenCapri) nameEars.GateForPlayback(); };
            if (OperatingSystem.IsMacOS())
            {
                Voice.Playback = wav =>
                {
                    if (Saved.ListenCapri)
                    {
                        nameEars.GateForPlayback();
                        macSpeechUntil = Now + Voice.Seconds(wav) + 0.7;
                    }
                    Limbs.Speak(wav);
                };
                Voice.Silence = () => { Limbs.Hush(); macSpeechUntil = Now + 0.7; };
            }
            // A hold remembers the context it was taken under, so a change made while Capri was closed (or a hold from an
            // older build, which remembers none) starts from a fresh decision instead of resuming the old pause. That
            // decision is the user's: a dropped hold never becomes an automatic start.
            bool staleHold = Saved.ListenCapriHold && !NameHoldContextMatches();
            if (staleHold) ClearNameHold();
            nameStartup = Saved.ListenCapri && !staleHold && !(OperatingSystem.IsLinux() && Saved.ListenCapriHold);
            if (Saved.ListenCapri && Saved.ListenCapriHold && !nameStartup)
            {
                nameStatus = "Paused after a locked screen. Turn Listen for Capri off and on, or unlock, to bring her back.";
                // A hold that survived a restart is a lock pause, so a detected unlock still brings her back.
                nameLockPaused = true;
            }
            Closed += (_, _) =>
            {
                nameClosed = true;
                SpeechAudio.BeforePlayback -= GateNamePlayback;
                StopNameListening("Local name listening stopped.");
            };
        }

        private void GateNamePlayback() => nameEars.GateForPlayback();

        // A listening context is the account, model, remote, session and microphone she agreed to listen under. A hold belongs
        // to it, so a different live context, or a persisted fingerprint that no longer matches the saved settings, drops it.
        private void DropNameHoldForChangedContext()
        {
            if (!Saved.ListenCapriHold && !nameLockPaused) return;
            if (NameHoldContextMatches() && (nameContext == null || SameNameContext(nameContext))) return;
            ClearNameHold();
        }

        // The hold and the pause always travel together with the context they were taken under.
        private void ClearNameHold()
        {
            Saved.ListenCapriHold = false;
            Saved.ListenCapriHoldContext = null;
            Save();
            nameLockPaused = false;
        }

        // The identity of the listening context over what a saved profile can answer without reading the account: microphone,
        // decoder and voice selection, provider, model, remote and session. NameIdentity also hashes the account, which reads
        // active.json, so only a worker may compute it; this hash stays safe on the UI thread and still catches every saved
        // change the lock promise names.
        private string NameHoldContext()
        {
            string context = Saved.MicrophoneNode + "|" + Saved.MicrophoneFingerprint + "|" + Saved.OwnedAsrPython + "|" +
                Saved.OwnedAsrManifest + "|" + Brain.Model + "|" + Brain.Remote?.Model + "|" + Brain.Remote?.Key + "|" +
                Saved.BrainProvider + "|" + Brain.Server + "|" + HyprlandDesktop.Current?.SessionId + "|" +
                Saved.VoiceProvider + "|" + Saved.VoiceId + "|" +
                Saved.OwnedVoicePython + "|" + Saved.OwnedVoiceProfile + "|" + Saved.Silent + "|" + Saved.LocalVoiceUrl + "|" +
                Saved.LocalVoiceModel + "|" + Saved.LocalVoiceName + "|" + Saved.LocalVoiceFormat + "|" + Saved.SpeechModel;
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(context)));
        }

        // Persist a hold together with the context it belongs to, so a change made while Capri was closed is visible at startup.
        private void HoldNameListening()
        {
            Saved.ListenCapriHold = true;
            Saved.ListenCapriHoldContext = NameHoldContext();
            Save();
        }

        // A hold whose remembered context no longer matches is stale, and a hold from an older build remembers none at all.
        private bool NameHoldContextMatches()
        {
            return Saved.ListenCapriHoldContext != null && Saved.ListenCapriHoldContext == NameHoldContext();
        }

        // R3: a false read may clear the hold only together with the start it allows. If that start was refused by a lock
        // that landed in between, the attempt re-marks the hold here, so the next unlock can still resume her.
        private void StartNameListeningFromHold()
        {
            StartNameListening();
            if (nameStopped && Saved.ListenCapri && LockedForNameListening() == true)
            {
                HoldNameListening();
                nameLockPaused = true;
                SetNameStatus("Paused while the screen is locked.");
            }
        }

        internal void ObserveNameLock(bool? locked)
        {
            DropNameHoldForChangedContext();
            if (locked == true)
            {
                // A context change outranks the lock: a pause must never be carried into a session under settings she never
                // agreed to, so it clears the hold and never records resumability, whether she listens or is already paused.
                if (nameContext != null && !SameNameContext(nameContext))
                {
                    if (!nameStopped) StopNameListening("Name listening stopped after a state change. Turn Listen for Capri off and on to resume.");
                    return;
                }
                // R1: every observed lock persists immediately, so a later unlock always has a hold to use. Only a lock that
                // actually stops her is a lock pause: a user's Stop, TALK bar or off switch already stopped her, and neither
                // the hold nor the pause may be revived against that.
                if (!nameStopped)
                {
                    HoldNameListening();
                    StopNameListening("Paused while the screen is locked.");
                    nameLockPaused = true;
                }
                return;
            }
            // R2: a lost signal never changes the hold.
            if (locked != false) return;
            // R3: a false read clears the hold only with the start it allows, and the resume's own guard re-marks it if that
            // start was refused by a lock rather than by a real bar.
            if (nameLockPaused && Saved.ListenCapriHold && Saved.ListenCapri && !NameBarredAt(locked) && !nameClosed &&
                (nameContext == null || SameNameContext(nameContext)))
            {
                ClearNameHold();
                StartNameListeningFromHold();
            }
        }

        internal void SetNameListening(bool on)
        {
            if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || Program.Smoke != null) return;
            EnableNameListening(on);
        }

        private void EnableNameListening(bool on)
        {
            if (on && OperatingSystem.IsLinux() && Saved.ListenCapriHold)
            {
                bool? locked = LockedForNameListening();
                if (locked == true)
                {
                    Saved.ListenCapri = true;
                    Save();
                    StopNameListening("Paused while the screen is locked.");
                    // She is lock-paused again, so a detected unlock may bring her back without another yes.
                    nameLockPaused = true;
                    return;
                }
                if (locked == null)
                {
                    if (Talk == null) return;
                    // An explicit Stop (or any other stop) advances the epoch, so its stale answer changes nothing.
                    long promptTicket = nameEpoch;
                    Talk.AskFor("(name listening)", "Capri can't reliably detect whether your screen is locked. Her microphone may keep listening while your screen is locked. Type YES to resume listening, or NO to keep her paused.", answer =>
                    {
                        if (promptTicket != nameEpoch) return;
                        if (!string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                        {
                            Saved.ListenCapri = false;
                            Save();
                            StopNameListening("Local name listening is off.");
                            return;
                        }
                        // R1: an observed lock is persisted here. R3: a false read clears the hold only with the start, so the
                        // start's own guard below decides whether the stop it caused was lock-caused.
                        bool? answerLock = LockedForNameListening();
                        if (answerLock == true)
                        {
                            Saved.ListenCapri = true;
                            HoldNameListening();
                            StopNameListening("Paused while the screen is locked.");
                            nameLockPaused = true;
                            return;
                        }
                        Saved.ListenCapri = true;
                        ClearNameHold();
                        StartNameListeningFromHold();
                    });
                    return;
                }
                if (Saved.ListenCapriHold || Saved.ListenCapriHoldContext != null || nameLockPaused) ClearNameHold();
            }
            Saved.ListenCapri = on;
            if (on && OperatingSystem.IsMacOS())
            {
                Saved.MicrophoneNode = null;
                Saved.MicrophoneFingerprint = null;
            }
            Save();
            if (!on) { StopNameListening("Local name listening is off."); return; }
            // The note is owed by a manual activation and lands only once she really begins capture, so a refused start or a
            // microphone chooser never shows it.
            nameActivationNote = true;
            StartNameListening();
        }

        private static bool NameMicrophoneSet(string source, string fingerprint)
        {
            return !string.IsNullOrWhiteSpace(source) && !string.IsNullOrWhiteSpace(fingerprint) &&
                (!OperatingSystem.IsMacOS() || (source == "mac-default" && fingerprint.Length == 64));
        }

        private void SetNameStatus(string status)
        {
            if (nameStatus == status) return;
            nameStatus = status;
            NotifySettings();
        }

        private void StopNameListening(string status = "Name listening stopped. Turn Listen for Capri on to resume.")
        {
            nameEpoch++;
            nameStopped = true;
            nameStarting = false;
            nameStartup = false;
            nameLockPaused = false;
            nameEars.Stop();
            SetNameStatus(status);
        }

        private async void StartNameListening()
        {
            StopNameListening();
            // ONE read for this start's own bar check and lock record, so they can never disagree.
            bool? startLock = LockedForNameListening();
            if (NameBarredAt(startLock))
            {
                // The bar may be this start's own lock observation, and R1 says an observed lock is persisted immediately, so
                // a later unlock can resume her. Another bar (TALK, mute, sleep) leaves nothing to resume.
                if (startLock == true && Saved.ListenCapri)
                {
                    HoldNameListening();
                    nameLockPaused = true;
                    nameActivationNote = false;
                    SetNameStatus("Paused while the screen is locked.");
                    return;
                }
                nameActivationNote = false;
                SetNameStatus(Listening ? "Finish TALK, then turn Listen for Capri off and on." : "Name listening paused. Unmute and wake Capri, then turn Listen for Capri off and on.");
                return;
            }
            NameContext captured = CaptureNameContext();
            if (!NameMicrophoneSet(captured.Source, captured.Fingerprint))
            { nameActivationNote = false; ChooseNameMicrophone(); return; }
            long ticket = nameEpoch;
            nameContext = captured;
            nameStopped = false;
            nameStarting = true;
            SetNameStatus("Starting local name listening…");
            if (nameActivationNote)
            {
                nameActivationNote = false;
                if (Talk != null) Talk.Answer("(name listening)", "She can't always tell when the screen is locked, so on some desktops she may keep listening while it is.");
            }
            try
            {
                await nameLifetime;
                string identity = await Task.Run(() =>
                {
                    if (string.IsNullOrWhiteSpace(captured.Python) || string.IsNullOrWhiteSpace(captured.Manifest) ||
                        !File.Exists(captured.Python) || !File.Exists(captured.Manifest))
                        throw new InvalidOperationException("Set up the local Moonshine decoder before enabling name listening.");
                    return NameIdentity(captured);
                });
                if (ticket != nameEpoch || nameStopped) return;
                // ONE read at the final gate, so the bar check and the lock record can never disagree.
                bool? gateLock = LockedForNameListening();
                bool contextChanged = !SameNameContext(captured);
                if (NameBarredAt(gateLock) || contextChanged)
                {
                    // A lock seen at this last gate is still an observed lock: persist it before the stop, so the next unlock
                    // can resume her. A real context change is not lock-caused and keeps its own message.
                    if (!contextChanged && gateLock == true)
                    {
                        HoldNameListening();
                        StopNameListening("Paused while the screen is locked.");
                        nameLockPaused = true;
                        return;
                    }
                    StopNameListening("The listening context changed during startup. Re-enable Listen for Capri.");
                    return;
                }
                nameContext = captured;
                nameIdentity = identity;
                nextNameIdentityCheck = Now + 1;
                nameLifetime = nameEars.Start(captured.Python, captured.Manifest, NameState(),
                    status => Dispatcher.UIThread.Post(() => { if (ticket == nameEpoch && !nameStopped) SetNameStatus(status); }),
                    transcript => ValidateNameFinal(ticket, captured, identity, transcript),
                    error => Dispatcher.UIThread.Post(() => { if (ticket == nameEpoch) StopNameListening(error); }));
            }
            catch (Exception error)
            {
                if (ticket == nameEpoch) StopNameListening(error is InvalidOperationException ? error.Message :
                    "Local name listening could not start. Check the retained decoder and account setup.");
            }
            finally { if (ticket == nameEpoch) nameStarting = false; }
        }

        private void ChooseNameMicrophone()
        {
            if (OperatingSystem.IsMacOS()) { ChooseMacNameMicrophone(); return; }
            long ticket = nameEpoch;
            Talk.Answer("(microphone)", "Checking available physical microphones…");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    byte[] inventory = ComputerProcess.Bytes("/usr/bin/pw-dump", Array.Empty<string>(), System.Threading.CancellationToken.None, 5000, 4 * 1024 * 1024);
                    System.Diagnostics.ProcessStartInfo info = ComputerProcess.Start("/usr/bin/python3", new[] { System.IO.Path.Combine(AppContext.BaseDirectory, "bridge/speech/sources.py") });
                    using System.Diagnostics.Process filter = System.Diagnostics.Process.Start(info);
                    using System.Threading.CancellationTokenSource timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using System.Threading.CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(filter));
                    var read = filter.StandardOutput.ReadToEndAsync();
                    var errors = filter.StandardError.ReadToEndAsync();
                    filter.StandardInput.BaseStream.Write(inventory); filter.StandardInput.Close();
                    filter.WaitForExit();
                    if (filter.ExitCode != 0) throw new InvalidOperationException("Microphone inventory could not be verified.");
                    using System.Text.Json.JsonDocument rows = System.Text.Json.JsonDocument.Parse(read.GetAwaiter().GetResult());
                    var choices = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Take(System.Linq.Enumerable.Select(rows.RootElement.EnumerateArray(), row => row.Clone()), 8));
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ticket != nameEpoch || !Saved.ListenCapri) return;
                        if (choices.Length == 0) { Talk.Answer("(microphone)", "No physical microphone is available."); return; }
                        string prompt = "Choose a microphone near you for Listen for Capri by number.\n";
                        for (int i = 0; i < choices.Length; i++) prompt += (i + 1) + ": " + choices[i].GetProperty("description").GetString() + "\n";
                        Talk.AskFor("(microphone)", prompt, selected =>
                        {
                            if (ticket != nameEpoch || !Saved.ListenCapri) return;
                            if (!int.TryParse(selected, out int index) || index < 1 || index > choices.Length) { Talk.Answer("(microphone)", "No microphone selected. Turn Listen for Capri on to choose again."); return; }
                            Saved.MicrophoneNode = choices[index - 1].GetProperty("node").GetString();
                            Saved.MicrophoneFingerprint = choices[index - 1].GetProperty("fingerprint").GetString();
                            Save(); StartNameListening();
                        });
                    });
                }
                catch (Exception error) { Dispatcher.UIThread.Post(() => { if (ticket == nameEpoch && Saved.ListenCapri) Talk.Answer("(microphone)", error.Message); }); }
            });
        }

        private void ChooseMacNameMicrophone()
        {
            long ticket = nameEpoch;
            Talk.Answer("(microphone)", "Checking the macOS default microphone…");
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    string helper = Path.Combine(AppContext.BaseDirectory, "capri-mic");
                    using var process = new System.Diagnostics.Process();
                    process.StartInfo = new System.Diagnostics.ProcessStartInfo(helper)
                    {
                        UseShellExecute = false, RedirectStandardOutput = true,
                        RedirectStandardError = true, CreateNoWindow = true
                    };
                    process.StartInfo.ArgumentList.Add("--identity");
                    process.Start();
                    string result = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(5000)) { Bridge.Kill(process); throw new InvalidOperationException("The macOS microphone check timed out."); }
                    if (process.ExitCode != 0) throw new InvalidOperationException("No macOS default microphone is available.");
                    using var json = System.Text.Json.JsonDocument.Parse(result);
                    string uid = json.RootElement.GetProperty("uid").GetString();
                    string name = json.RootElement.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(uid) || string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("No macOS default microphone is available.");
                    string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(uid))).ToLowerInvariant();
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ticket != nameEpoch || !Saved.ListenCapri) return;
                        Talk.AskFor("(microphone)", "Listen for Hey Capri will use your Mac's CURRENT default mic: " + name +
                            ". Change the default mic under System Settings > Sound > Input first if needed. Type YES to enable local listening, or NO to cancel.", answer =>
                        {
                            if (ticket != nameEpoch || !Saved.ListenCapri) return;
                            if (!string.Equals(answer?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                            {
                                Saved.ListenCapri = false; Save(); StopNameListening("Local name listening is off."); return;
                            }
                            Saved.MicrophoneNode = "mac-default";
                            Saved.MicrophoneFingerprint = fingerprint;
                            Save(); StartNameListening();
                        });
                    });
                }
                catch (Exception error)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (ticket == nameEpoch && Saved.ListenCapri)
                            StopNameListening("Mac microphone setup failed: " + error.Message);
                    });
                }
            });
        }

        private void ValidateNameFinal(long ticket, NameContext captured, string identity, NameTranscript transcript)
        {
            // Do not run account disk I/O on the UI thread. The generation and
            // context are checked again after the UI dispatcher receives it.
            int providerGeneration = captured.Provider?.Generation ?? 0;
            _ = Task.Run(() =>
            {
                bool current;
                try { current = identity == NameIdentity(captured); }
                catch (Exception) { current = false; }
                Dispatcher.UIThread.Post(() =>
                {
                    if (ticket != nameEpoch || nameStopped || !Saved.ListenCapri) return;
                    if (!current || !SameNameContext(captured))
                    { StopNameListening("The account, model or microphone changed. Re-enable Listen for Capri."); return; }
                    // An ordinary new Hear cancels the provider generation too. Drop
                    // that stale queued line without ending the valid name window.
                    if (providerGeneration != (captured.Provider?.Generation ?? 0)) return;
                    if (NameBarred || Chimes.Masking || SpeechAudio.MicrophoneHeld || (OperatingSystem.IsMacOS() && Now < macSpeechUntil) || !nameEars.IsCurrent(transcript)) return;
                    NameLineAccepted(transcript.Text);
                });
            });
        }

        internal void NameLineAccepted(string text)
        {
            // Her name on its own is the cue to go ahead, decided exactly as the Python gate decides it, so a line it
            // accepts as name-only never counts as a request. A line that carries one plays Heard you, like Windows.
            if (NameListeningText.NameAlone(text)) { Chimes.Play(Chime.Listening); return; }
            string request = NameListeningText.Request(text);
            if (request.Length == 0) return;
            Chimes.Play(Chime.Heard);
            Hear(request, true);
        }

        private void TickNameListening()
        {
            if (!(OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) || Program.Smoke != null || nameClosed) return;
            // A changed context drops any hold before the lock is even read, so a lost signal can't carry it forward.
            DropNameHoldForChangedContext();
            // ONE read per tick: it feeds the observation, the startup probe and the stop branch, so they can never disagree.
            bool? locked = OperatingSystem.IsLinux() ? Pet.LockedForNameListening() : null;
            if (nameStartup && opened && locked != true)
            { nameStartup = false; StartNameListeningFromHold(); }
            // A held state must keep reading the lock, or an unlock could never be seen.
            if (OperatingSystem.IsLinux() && Saved.ListenCapri) ObserveNameLock(locked);
            if (nameStopped) return;
            if (NameBarredAt(locked) || nameContext != null && !SameNameContext(nameContext))
            {
                // A changed context outranks the lock: it was already dropped above, so this is never a lock pause.
                bool contextChanged = nameContext != null && !SameNameContext(nameContext);
                // This tick's own read is the lock evidence here, so a lock found with a final unknown read still counts.
                if (!contextChanged && locked == true)
                {
                    if (!Saved.ListenCapriHold && Saved.ListenCapri) HoldNameListening();
                    StopNameListening("Paused while the screen is locked.");
                    nameLockPaused = true;
                    return;
                }
                StopNameListening("Name listening stopped after a state change. Turn Listen for Capri off and on to resume.");
                return;
            }
            if (nameStarting || !nameEars.Active) return;
            // The decoder's barred flag must be the same lock answer this tick already acted on, never a second read.
            nameEars.Update(NameState(locked));
            if (nameChecking || Now < nextNameIdentityCheck) return;
            nameChecking = true;
            nextNameIdentityCheck = Now + 1;
            long ticket = nameEpoch;
            NameContext captured = nameContext;
            string identity = nameIdentity;
            _ = Task.Run(() =>
            {
                bool current;
                try { current = identity == NameIdentity(captured); }
                catch (Exception) { current = false; }
                Dispatcher.UIThread.Post(() =>
                {
                    nameChecking = false;
                    if (ticket == nameEpoch && !nameStopped && (!current || !SameNameContext(captured)))
                        StopNameListening("The account changed. Turn Listen for Capri off and on to resume.");
                });
            });
        }

    }
}
