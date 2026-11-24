using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace CapriDesktop
{
    // Listen for Capri must keep working on Linux desktops that can't report a session lock. Reading an unknown Hyprland
    // snapshot as locked turned the switch on and then dropped every line she heard. These pin that decision and logind's
    // read without opening a microphone.
    internal static class NameListeningChecks
    {
        internal static void Checks(Pet pet, List<string> checks)
        {
            Func<bool?> lockedWas = Pet.LockedForNameListening;
            Func<string, string[], string> runWas = Music.Run;
            Func<long> nowWas = Limbs.LockClock;
            long now = 0;
            try
            {
                Limbs.LockClock = () => now;
                Pet.LockedForNameListening = () => null;
                Require(!pet.NameBarredNow, "a desktop that can't report a lock barred name listening, so her switch would be dead");
                Pet.LockedForNameListening = () => true;
                Require(pet.NameBarredNow, "a locked session did not bar name listening");
                Pet.LockedForNameListening = () => false;
                Require(!pet.NameBarredNow, "an unlocked desktop barred name listening");
                if (OperatingSystem.IsLinux())
                {
                    string asked = null, hint = null;
                    Music.Run = (file, args) => { asked = file + " " + string.Join(" ", args); return hint; };
                    now += 10000;
                    hint = null;
                    Require(Limbs.SessionLocked() == null, "a missing logind reply was read as lock state");
                    now += 10000;
                    hint = "b true\n";
                    Require(Limbs.SessionLocked() == true, "logind's locked session was not honored");
                    now += 10000;
                    hint = "b false\n";
                    Require(Limbs.SessionLocked() == false, "logind's unlocked session was read as locked");
                    now += 10000;
                    hint = "Failed to get property: Unknown property LockedHint\n";
                    Require(Limbs.SessionLocked() == null, "a desktop without LockedHint was read as locked");
                    Require(asked == "busctl get-property org.freedesktop.login1 /org/freedesktop/login1/session/auto org.freedesktop.login1.Session LockedHint",
                        "name listening asked logind something other than its lock hint: " + asked);
                    // The desktop that broke: no Hyprland to answer, so the switch depends on what logind says.
                    if (HyprlandDesktop.Current == null)
                    {
                        Pet.LockedForNameListening = lockedWas;
                        now += 10000;
                        hint = "b true\n";
                        Require(pet.NameBarredNow, "logind's locked session did not stop name listening without Hyprland");
                        now += 10000;
                        hint = null;
                        Require(!pet.NameBarredNow, "a desktop without Hyprland and without a lock answer barred name listening, so her switch would be dead");
                    }
                    int spawned = 0;
                    string reply = "b false\n";
                    Music.Run = (file, args) => { spawned++; return reply; };
                    now += 100000;
                    Require(Limbs.SessionLocked() == false && spawned == 1, "the first lock read did not ask logind once: " + spawned);
                    reply = "b true\n";
                    now += 2999;
                    Require(Limbs.SessionLocked() == false && spawned == 1, "a kept answer expired early, so a tick asked logind again");
                    now++;
                    Require(Limbs.SessionLocked() == true && spawned == 2, "an expired answer was not read again, so a lock could never apply");
                }
            }
            finally
            {
                Pet.LockedForNameListening = lockedWas;
                Music.Run = runWas;
                Limbs.LockClock = nowWas;
            }
            checks.Add("PASS name listening: an unknown session lock never bars her, Hyprland and logind's LockedHint still stop her, idle is never read as locked, and her tick keeps the logind answer instead of a busctl each frame");
            if (OperatingSystem.IsLinux()) HoldChecks(pet, checks);
            if (OperatingSystem.IsLinux()) CorrectionChecks(pet, checks);
            if (OperatingSystem.IsLinux()) ReviewChecks(pet, checks);
            if (OperatingSystem.IsLinux()) LensChecks(pet, checks);
            if (OperatingSystem.IsLinux()) VerifierChecks(pet, checks);
            if (OperatingSystem.IsLinux()) HygieneCheck(pet, checks);
        }

        // The verifier reproduced these against the fix branches. They drive the real tick, the real Talk recovery prompt and
        // the real settings files (a scratch profile through the production Save/ReadSettings), with a gated nameLifetime so a
        // start attempt is observable without ever opening a microphone.
        private static void VerifierChecks(Pet pet, List<string> checks)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Func<bool?> lockedWas = Pet.LockedForNameListening;
            Func<string, string[], string> runWas = Music.Run;
            Func<long> nowWas = Limbs.LockClock;
            Settings saved = pet.Saved;
            bool capriWas = saved.ListenCapri, holdWas = saved.ListenCapriHold;
            string holdContextWas = saved.ListenCapriHoldContext;
            string nodeWas = saved.MicrophoneNode, fingerprintWas = saved.MicrophoneFingerprint;
            string pythonWas = saved.OwnedAsrPython, manifestWas = saved.OwnedAsrManifest;
            string serverWas = saved.BrainServer, modelWas = saved.ServerModel;
            int xWas = saved.X, yWas = saved.Y;
            bool placedWas = saved.Placed;
            string brainModelWas = pet.Brain.Model, brainServerWas = pet.Brain.Server;
            var stoppedField = typeof(Pet).GetField("nameStopped", fields);
            var startingField = typeof(Pet).GetField("nameStarting", fields);
            var statusField = typeof(Pet).GetField("nameStatus", fields);
            var epochField = typeof(Pet).GetField("nameEpoch", fields);
            var contextField = typeof(Pet).GetField("nameContext", fields);
            var lifetimeField = typeof(Pet).GetField("nameLifetime", fields);
            var lockPausedField = typeof(Pet).GetField("nameLockPaused", fields);
            var answersField = typeof(Pet).GetField("answerTurn", fields);
            var shushedField = typeof(Pet).GetField("shushed", fields);
            var openedField = typeof(Pet).GetField("opened", fields);
            var writeBlockedField = typeof(Pet).GetField("settingsWriteBlocked", fields);
            var folderField = typeof(Pet).GetField("folder", fields);
            var tick = typeof(Pet).GetMethod("TickNameListening", fields, null, Type.EmptyTypes, null);
            var stop = typeof(Pet).GetMethod("StopNameListening", fields, null, new[] { typeof(string) }, null);
            var captureContext = typeof(Pet).GetMethod("CaptureNameContext", fields, null, Type.EmptyTypes, null);
            var initialize = typeof(Pet).GetMethod("InitializeNameListening", fields, null, Type.EmptyTypes, null);
            var earsField = typeof(Pet).GetField("nameEars", fields);
            var identityField = typeof(Pet).GetField("nameIdentity", fields);
            var nextIdentityField = typeof(Pet).GetField("nextNameIdentityCheck", fields);
            var earsStateField = typeof(OwnedNameEars).GetField("state", fields);
            bool stoppedWas = (bool)stoppedField.GetValue(pet), startingWas = (bool)startingField.GetValue(pet);
            bool lockPausedWas = (bool)lockPausedField.GetValue(pet), shushedWas = (bool)shushedField.GetValue(pet);
            bool openedWas = (bool)openedField.GetValue(pet), writeBlockedWas = (bool)writeBlockedField.GetValue(pet);
            string statusWas = (string)statusField.GetValue(pet), smokeWas = Program.Smoke;
            string folderWas = (string)folderField.GetValue(pet);
            object contextWas = contextField.GetValue(pet);
            Task lifetimeWas = (Task)lifetimeField.GetValue(pet);
            OwnedNameEars earsWas = (OwnedNameEars)earsField.GetValue(pet);
            string identityWas = (string)identityField.GetValue(pet);
            double nextIdentityWas = (double)nextIdentityField.GetValue(pet);
            Func<bool> started = () => !(bool)stoppedField.GetValue(pet);
            Func<bool> starting = () => (bool)startingField.GetValue(pet);
            Func<bool> lockPaused = () => (bool)lockPausedField.GetValue(pet);
            Func<long> epoch = () => (long)epochField.GetValue(pet);
            Func<int> answers = () => (int)answersField.GetValue(pet);
            string profile = Path.Combine(Path.GetTempPath(), "capri-name-verifier-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(profile);
            bool filesHold() => JsonFlag(Path.Combine(profile, "cross.json"), "ListenCapriHold");
            bool mirrorHold() => JsonFlag(Path.Combine(profile, "cross-new.json"), "ListenCapriHold");
            string hashes() => Hash(Path.Combine(profile, "cross.json")) + "/" + Hash(Path.Combine(profile, "cross-new.json"));
            try
            {
                pet.Talk.Dismiss();
                // A scratch profile: the real Save and ReadSettings run against it, never the user's own files.
                folderField.SetValue(pet, profile);
                // A start attempt parks on this forever, so no decoder process opens and no async error can race the check.
                lifetimeField.SetValue(pet, new TaskCompletionSource().Task);
                shushedField.SetValue(pet, false);
                openedField.SetValue(pet, true);
                saved.MicrophoneNode = "test-mic";
                saved.MicrophoneFingerprint = "test-fingerprint";
                string manifest = Path.Combine(profile, "manifest.json");
                File.WriteAllText(manifest, "{}");
                saved.OwnedAsrPython = "/usr/bin/python3";
                saved.OwnedAsrManifest = manifest;

                // Blocker 1: a stale YES after an explicit Stop must not revive the switch, the hold or a later resume.
                ResetName(pet, fields, saved, opted: false, hold: false, active: false);
                saved.ListenCapriHold = true;
                DiskWrite(pet);
                Pet.LockedForNameListening = () => null;
                pet.Talk.Dismiss();
                typeof(Pet).GetMethod("EnableNameListening", fields).Invoke(pet, new object[] { true });
                Require(pet.Talk.Typing, "the recovery prompt never opened");
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                // The Stop is what must win, so the state just after it is the baseline the stale answer may not change.
                long stoppedEpoch = epoch();
                string stoppedFiles = hashes();
                bool stoppedOpted = saved.ListenCapri, stoppedHeld = saved.ListenCapriHold, stoppedPaused = lockPaused();
                bool leftTyping = pet.Talk.Typing;
                Pet.LockedForNameListening = () => true;
                pet.Talk.Submit("YES");
                string afterStale = "typing after Stop=" + leftTyping + ", opted=" + saved.ListenCapri + ", hold=" + saved.ListenCapriHold + ", paused=" + lockPaused() + ", status=" + pet.NameListeningStatus;
                Require(saved.ListenCapri == stoppedOpted, "a stale YES changed the opt-in after an explicit Stop: " + afterStale);
                Require(saved.ListenCapriHold == stoppedHeld, "a stale YES changed the lock hold after an explicit Stop: " + afterStale);
                Require(lockPaused() == stoppedPaused, "a stale YES changed her resumability after an explicit Stop: " + afterStale);
                Require(epoch() == stoppedEpoch && hashes() == stoppedFiles, "a stale YES changed her epoch or her files after an explicit Stop: " + afterStale);
                Pet.LockedForNameListening = () => null;
                TickNow(pet, fields, tick);
                Pet.LockedForNameListening = () => false;
                TickNow(pet, fields, tick);
                Require(!started(), "an unlock started her after a stale YES behind an explicit Stop");
                pet.Talk.Dismiss();

                // Blocker 2: a context change under a lost signal must drop the old hold, and it must not come back after a restart.
                ResetName(pet, fields, saved, opted: true, hold: false, active: true);
                Pet.LockedForNameListening = () => true;
                TickNow(pet, fields, tick);
                Require(saved.ListenCapriHold && filesHold() && mirrorHold(), "a real lock seen by the tick did not persist its hold");
                Pet.LockedForNameListening = () => null;
                saved.MicrophoneNode = "changed-mic";
                TickNow(pet, fields, tick);
                Require(!saved.ListenCapriHold && !filesHold() && !mirrorHold(),
                    "a context change under a lost lock signal kept the old hold: files hold=" + filesHold() + "/" + mirrorHold());
                Pet.LockedForNameListening = () => false;
                TickNow(pet, fields, tick);
                Require(!started() && !lockPaused(), "an unlock resumed her after the context changed under a lost signal");
                long contextEpoch = epoch();
                TickNow(pet, fields, tick);
                Require(epoch() == contextEpoch, "a further tick restarted her after the context changed");

                // Blocker 3a: a real lock seen by the generic tick must persist even though the tick's *own* answer is the only
                // read now, so a later tick (or NameBarred's internal read) turning unknown can't erase it.
                ResetName(pet, fields, saved, opted: true, hold: false, active: true);
                SequenceLock(pet, true, null, null);
                TickNow(pet, fields, tick);
                Require(!started() && lockPaused() && saved.ListenCapriHold && filesHold() && mirrorHold(),
                    "a lock seen in the tick with an unknown later read was dropped: hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold());
                // The next tick reads unknown, and R2 says that changes nothing.
                TickNow(pet, fields, tick);
                Require(saved.ListenCapriHold && filesHold() && mirrorHold() && lockPaused(),
                    "a lost signal after the tick's observed lock dropped the hold");

                // Blocker 3b: a relock after the tick's read but before the start's own probe is re-marked, so
                // the hold survives even when the start refuses and its leading stop cleared the pause.
                ResetName(pet, fields, saved, opted: true, hold: true, active: false);
                SequenceLock(pet, true, null);
                ObserveNow(pet, false);
                Require(saved.ListenCapriHold && filesHold() && lockPaused(),
                    "a relock during the resume was dropped: hold=" + saved.ListenCapriHold + "/" + filesHold());

                // Blocker 3c: a relock observed at the final async startup gate must persist its hold before the stop, so the
                // next unlock can resume her. A real gate parks startup exactly where production waits for it.
                ResetName(pet, fields, saved, opted: true, hold: true, active: false);
                var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lifetimeField.SetValue(pet, gate.Task);
                Pet.LockedForNameListening = () => false;
                ObserveNow(pet, false);
                Require(started() && starting(), "the checked start did not reach its async gate");
                Pet.LockedForNameListening = () => true;
                string gateSmoke = Program.Smoke;
                Program.Smoke = null;
                try
                {
                    gate.SetResult();
                    Require(System.Threading.SpinWait.SpinUntil(() =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        return !starting();
                    }, TimeSpan.FromSeconds(5)), "startup never reached its final lock guard");
                }
                finally { Program.Smoke = gateSmoke; }
                Require(!started() && lockPaused() && saved.ListenCapriHold && filesHold() && mirrorHold(),
                    "a relock at the final startup gate lost its hold: hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold());

                // Round 3, B2: the hold carries the context it was taken under, so a microphone change made while Capri was
                // CLOSED is detected at startup instead of resuming the old pause. A fresh init captures her context, which can
                // only differ from the persisted one when the hold remembers whose it was.
                ResetName(pet, fields, saved, opted: true, hold: false, active: true);
                Pet.LockedForNameListening = () => true;
                TickNow(pet, fields, tick);
                Require(saved.ListenCapriHold && filesHold(), "the tick's lock did not persist a hold to carry a context for");
                string holdContext = JsonString(Path.Combine(profile, "cross.json"), "ListenCapriHoldContext");
                Require(!string.IsNullOrEmpty(holdContext), "a persisted hold did not remember the context it was taken under");
                Require(holdContext == JsonString(Path.Combine(profile, "cross-new.json"), "ListenCapriHoldContext"),
                    "the newer settings copy lost the hold's context, so a rewrite would make the hold stale");
                // The mic changes while she is closed: only the saved files change, never a live context.
                Pet.LockedForNameListening = () => null;
                saved.MicrophoneNode = "changed-while-closed";
                DiskWrite(pet);
                contextField.SetValue(pet, null);
                string initSmoke = Program.Smoke;
                Program.Smoke = null;
                try { typeof(Pet).GetMethod("InitializeNameListening", fields).Invoke(pet, null); }
                finally { Program.Smoke = initSmoke; }
                Require(!saved.ListenCapriHold && !filesHold() && !mirrorHold(),
                    "a hold from a different context survived startup: hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold());
                Require(!pet.NameListeningStatus.StartsWith("Paused after a locked screen", StringComparison.Ordinal),
                    "a hold from a different context still seeded the paused-after-lock status: " + pet.NameListeningStatus);
                Pet.LockedForNameListening = () => false;
                TickNow(pet, fields, tick);
                Require(!started(), "an unlock after startup resumed the microphone under a hold from another context");

                // And a hold written by an older build has no context at all, so it cannot be trusted either.
                ResetName(pet, fields, saved, opted: true, hold: false, active: false);
                saved.ListenCapriHold = true;
                saved.ListenCapriHoldContext = null;
                DiskWrite(pet);
                typeof(Pet).GetField("nameContext", fields).SetValue(pet, null);
                string noContextSmoke = Program.Smoke;
                Program.Smoke = null;
                try { typeof(Pet).GetMethod("InitializeNameListening", fields).Invoke(pet, null); }
                finally { Program.Smoke = noContextSmoke; }
                Require(!saved.ListenCapriHold && !filesHold(), "a hold with no remembered context survived startup");
                Require(JsonString(Path.Combine(profile, "cross.json"), "ListenCapriHoldContext") == null,
                    "a dropped legacy hold left its context behind");

                // Round 3, B3 YesSplit: the YES answer reads null, the hold clears, then the start's guard reads locked, so the
                // start refuses and its leading stop would leave nothing to resume. The attempt must re-mark the hold.
                ResetName(pet, fields, saved, opted: false, hold: true, active: false);
                SequenceLock(pet, null, true, null);
                typeof(Pet).GetMethod("EnableNameListening", fields).Invoke(pet, new object[] { true });
                pet.Talk.Submit("YES");
                Require(!started() && lockPaused() && saved.ListenCapriHold && filesHold() && mirrorHold(),
                    "a relock at the YES start's guard lost its hold: hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold());
                Require(pet.NameListeningStatus == "Paused while the screen is locked.",
                    "a relock at the YES start's guard reported the wrong status: " + pet.NameListeningStatus);

                // Round 3, B3 TransientResume: the tick's read let the resume clear, then the relock lands at the start's
                // own probe and its re-probe sees nothing at all. The hold must come back anyway.
                ResetName(pet, fields, saved, opted: true, hold: true, active: false);
                SequenceLock(pet, true, null);
                ObserveNow(pet, false);
                Require(!started() && lockPaused() && saved.ListenCapriHold && filesHold() && mirrorHold(),
                    "a relock that turned unknown during the resume lost its hold: hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold());
                Require(pet.NameListeningStatus == "Paused while the screen is locked.",
                    "a relock that turned unknown during the resume reported the wrong status: " + pet.NameListeningStatus);

                // Round 4, B3 TransientTick: the tick's ONE read must also drive the live decoder's barred flag, not just
                // the observation and the stop branch. Only a real OwnedNameEars with an inert child (never a microphone)
                // reaches the steady Update path; the old check parked on nameStarting=true and never ran it, so the second
                // read inside NameState hid there. The child is /usr/bin/sleep and is stopped in the finally.
                ResetName(pet, fields, saved, opted: true, hold: false, active: true);
                var liveEars = new OwnedNameEars((_, _) => System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo("/usr/bin/sleep", "20")
                    { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true }));
                earsField.SetValue(pet, liveEars);
                startingField.SetValue(pet, false);
                identityField.SetValue(pet, "probe-identity");
                nextIdentityField.SetValue(pet, double.PositiveInfinity);
                Task liveTask = null;
                try
                {
                    liveTask = liveEars.Start("unused", "unused", new NameListeningState("probe-identity", saved.MicrophoneNode,
                        saved.MicrophoneFingerprint, false, false, false), null, null, null);
                    Require(liveEars.Active && !starting(), "the live-tick fixture never reached the steady active-ear path");
                    int tickReads = 0;
                    bool?[] tickAnswers = { false, true, null };
                    Pet.LockedForNameListening = () => tickAnswers[Math.Min(tickReads++, tickAnswers.Length - 1)];
                    string tickFiles = hashes();
                    TickNow(pet, fields, tick);
                    int first = tickReads;
                    bool barredAtFirst = ((NameListeningState)earsStateField.GetValue(liveEars)).Barred;
                    bool firstOk = first == 1 && started() && !barredAtFirst && !saved.ListenCapriHold && hashes() == tickFiles;
                    TickNow(pet, fields, tick);
                    int second = tickReads;
                    bool secondOk = second == 2 && !started() && lockPaused() && saved.ListenCapriHold && filesHold() && mirrorHold() &&
                        pet.NameListeningStatus == "Paused while the screen is locked.";
                    string atSecond = "active=" + started() + ", hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold() +
                        ", paused=" + lockPaused() + ", status=" + pet.NameListeningStatus;
                    string pauseFiles = hashes();
                    TickNow(pet, fields, tick);
                    Require(firstOk && secondOk && tickReads == 3 && !started() && saved.ListenCapriHold && hashes() == pauseFiles,
                        "expected one read per live tick and the hold persisted on the read that saw the lock: cumulative reads " +
                        first + "/" + second + "/" + tickReads + ", decoder barred after the false tick=" + barredAtFirst +
                        ", hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold() + ", paused=" + lockPaused() +
                        ", tick #2: " + atSecond + ", files unchanged=" + (hashes() == tickFiles));
                }
                finally
                {
                    liveEars.Stop();
                    Require(liveTask != null && liveTask.Wait(TimeSpan.FromSeconds(3)), "the inert live-tick worker did not stop");
                }

                // Round 3, B3 RelockAtAsyncGate (true then null): one gate read is the whole decision, so a lock there is
                // persisted even if the next answer would have been lost. The steady relock already passes; this pins the split.
                ResetName(pet, fields, saved, opted: true, hold: true, active: false);
                var splitGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                lifetimeField.SetValue(pet, splitGate.Task);
                Pet.LockedForNameListening = () => false;
                ObserveNow(pet, false);
                Require(started() && starting(), "the checked start never reached its async gate");
                SequenceLock(pet, true, null);
                string splitSmoke = Program.Smoke;
                Program.Smoke = null;
                try
                {
                    splitGate.SetResult();
                    Require(System.Threading.SpinWait.SpinUntil(() =>
                    {
                        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                        return !starting();
                    }, TimeSpan.FromSeconds(5)), "startup never reached its final guard");
                }
                finally { Program.Smoke = splitSmoke; }
                Require(!started() && lockPaused() && saved.ListenCapriHold && filesHold() && mirrorHold(),
                    "a relock that turned unknown at the async gate lost its hold: hold=" + saved.ListenCapriHold + "/" + filesHold() + "/" + mirrorHold());

                // S2: the settings tip must still show the lock limitation after truncation.
                Mainframe frame = new Mainframe(pet);
                frame.ShowSettings(true);
                var note = (Avalonia.Controls.Control)typeof(Mainframe).GetField("nameListeningNote", fields).GetValue(frame);
                var tip = ToolTip.GetTip(note) as Avalonia.Controls.TextBlock;
                string tipText = tip?.Text ?? "(none)";
                frame.Close();
                Require(tipText.IndexOf("locked", StringComparison.OrdinalIgnoreCase) >= 0 &&
                    (tipText.IndexOf("can't", StringComparison.OrdinalIgnoreCase) >= 0 || tipText.IndexOf("may keep", StringComparison.OrdinalIgnoreCase) >= 0),
                    "the settings tip lost the lock limitation after truncation: " + tipText);
            }
            finally
            {
                pet.Talk.Dismiss();
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                folderField.SetValue(pet, folderWas);
                Program.Smoke = smokeWas;
                writeBlockedField.SetValue(pet, writeBlockedWas);
                Pet.LockedForNameListening = lockedWas;
                Music.Run = runWas;
                Limbs.LockClock = nowWas;
                saved.X = xWas; saved.Y = yWas; saved.Placed = placedWas;
                saved.ListenCapri = capriWas;
                saved.ListenCapriHold = holdWas;
                saved.ListenCapriHoldContext = holdContextWas;
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                saved.OwnedAsrPython = pythonWas;
                saved.OwnedAsrManifest = manifestWas;
                saved.BrainServer = serverWas;
                saved.ServerModel = modelWas;
                pet.Brain.Model = brainModelWas;
                pet.Brain.Server = brainServerWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                statusField.SetValue(pet, statusWas);
                contextField.SetValue(pet, contextWas);
                lifetimeField.SetValue(pet, lifetimeWas);
                earsField.SetValue(pet, earsWas);
                identityField.SetValue(pet, identityWas);
                nextIdentityField.SetValue(pet, nextIdentityWas);
                lockPausedField.SetValue(pet, lockPausedWas);
                shushedField.SetValue(pet, shushedWas);
                openedField.SetValue(pet, openedWas);
                try { Directory.Delete(profile, true); } catch (Exception) { }
            }
            checks.Add("PASS name listening verifier: a stale answer after an explicit Stop changes nothing, a context change under a lost signal drops the old hold for good and a hold remembers the context it was taken under so a change made while Capri was closed is caught at startup, every observed lock persists even when a relock lands at the start gate or turns unknown there, and the live tick reads the lock exactly once, decoder update included; the settings tip still shows the lock limitation after truncation");
        }

        // The verifier's leak probe: run each group and compare every field, delegate and cache it may touch.
        private static void HygieneCheck(Pet pet, List<string> checks)
        {
            var flags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
            var instance = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var lockedAtField = typeof(Limbs).GetField("lockedAt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var lockedHintField = typeof(Limbs).GetField("lockedHint", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
            var contextField = typeof(Pet).GetField("nameContext", instance);
            var lifetimeField = typeof(Pet).GetField("nameLifetime", instance);
            var pausedField = typeof(Pet).GetField("nameLockPaused", instance);
            var changedField = typeof(Pet).GetField("SettingsChanged", instance);
            string[] groups = { "HoldChecks", "CorrectionChecks", "ReviewChecks", "LensChecks" };
            List<string> leaks = new List<string>();
            foreach (string group in groups)
            {
                var writer = typeof(NameListeningChecks).GetMethod(group, flags);
                if (writer == null) { leaks.Add(group + ": missing"); continue; }
                // The verifier's own fixture prelude: a fresh baseline before each group, so a leak can't hide behind a value
                // that was already what Save writes. Everything the prelude changes is put back after the comparison.
                object lifetimeBefore = lifetimeField.GetValue(pet);
                object contextBefore = contextField.GetValue(pet);
                bool placedBefore = pet.Saved.Placed;
                bool holdBefore = pet.Saved.ListenCapriHold;
                string holdContextBefore = pet.Saved.ListenCapriHoldContext;
                lifetimeField.SetValue(pet, new TaskCompletionSource().Task);
                contextField.SetValue(pet, null);
                pet.Saved.Placed = false;
                pet.Saved.ListenCapriHold = false;
                pet.Saved.ListenCapriHoldContext = null;
                object lifetimeBase = lifetimeField.GetValue(pet), contextBase = contextField.GetValue(pet);
                bool pausedBase = (bool)pausedField.GetValue(pet);
                object changedBase = changedField.GetValue(pet);
                Func<bool?> lockedBase = Pet.LockedForNameListening;
                Func<string, string[], string> runBase = Music.Run;
                Func<long> clockBase = Limbs.LockClock;
                long atBase = (long)lockedAtField.GetValue(null);
                object hintBase = lockedHintField.GetValue(null);
                object playbackBase = BeforePlaybackField()?.GetValue(null);
                try
                {
                    writer.Invoke(null, new object[] { pet, new List<string>() });
                    if (pet.Saved.Placed) leaks.Add(group + ": Placed");
                    if (pet.Saved.ListenCapriHold) leaks.Add(group + ": ListenCapriHold");
                    if (pet.Saved.ListenCapriHoldContext != null) leaks.Add(group + ": ListenCapriHoldContext");
                    if (!ReferenceEquals(contextBase, contextField.GetValue(pet))) leaks.Add(group + ": nameContext");
                    if (!ReferenceEquals(lifetimeBase, lifetimeField.GetValue(pet))) leaks.Add(group + ": nameLifetime");
                    if (pausedBase != (bool)pausedField.GetValue(pet)) leaks.Add(group + ": nameLockPaused");
                    if (!ReferenceEquals(changedBase, changedField.GetValue(pet))) leaks.Add(group + ": SettingsChanged");
                    if (!ReferenceEquals(lockedBase, Pet.LockedForNameListening)) leaks.Add(group + ": LockedForNameListening");
                    if (!ReferenceEquals(runBase, Music.Run)) leaks.Add(group + ": Music.Run");
                    if (!ReferenceEquals(clockBase, Limbs.LockClock)) leaks.Add(group + ": LockClock");
                    if (atBase != (long)lockedAtField.GetValue(null)) leaks.Add(group + ": logind lockedAt");
                    if (!Equals(hintBase, lockedHintField.GetValue(null))) leaks.Add(group + ": logind lockedHint");
                    if (!ReferenceEquals(playbackBase, BeforePlaybackField()?.GetValue(null))) leaks.Add(group + ": BeforePlayback");
                }
                catch (System.Reflection.TargetInvocationException error)
                { leaks.Add(group + ": threw " + (error.InnerException ?? error).Message); }
                finally
                {
                    lifetimeField.SetValue(pet, lifetimeBefore);
                    contextField.SetValue(pet, contextBefore);
                    pet.Saved.Placed = placedBefore;
                    pet.Saved.ListenCapriHold = holdBefore;
                    pet.Saved.ListenCapriHoldContext = holdContextBefore;
                }
            }
            Require(leaks.Count == 0, "a name-listening check group leaked " + string.Join(", ", leaks.ToArray()));
            checks.Add("PASS name listening hygiene: every check group restores the fields, delegates, the speech handler and logind's cache it touches, and none leaves Saved.Placed, the lock hold or its context behind");
        }

        private static void ResetName(Pet pet, System.Reflection.BindingFlags fields, Settings saved, bool opted, bool hold, bool active)
        {
            var stop = typeof(Pet).GetMethod("StopNameListening", fields, null, new[] { typeof(string) }, null);
            typeof(Pet).GetField("nameContext", fields).SetValue(pet, null);
            pet.Talk.Dismiss();
            stop.Invoke(pet, new object[] { "Local name listening is off." });
            saved.ListenCapri = opted;
            saved.ListenCapriHold = hold;
            saved.MicrophoneNode = "test-mic";
            saved.MicrophoneFingerprint = "test-fingerprint";
            typeof(Pet).GetField("nameStopped", fields).SetValue(pet, !active);
            typeof(Pet).GetField("nameStarting", fields).SetValue(pet, active);
            typeof(Pet).GetField("nameLockPaused", fields).SetValue(pet, hold && opted);
            if (active || hold) typeof(Pet).GetField("nameContext", fields).SetValue(pet, typeof(Pet).GetMethod("CaptureNameContext", fields, null, Type.EmptyTypes, null).Invoke(pet, null));
            // A fixture hold must remember its context like a real one, or the next tick would rightly drop it as stale.
            saved.ListenCapriHoldContext = hold ? HoldContext(pet, fields) : null;
            DiskWrite(pet);
        }

        // The context a real hold would persist right now, for fixtures that build a held state by hand.
        private static string HoldContext(Pet pet, System.Reflection.BindingFlags fields)
        {
            return (string)typeof(Pet).GetMethod("NameHoldContext", fields, null, Type.EmptyTypes, null).Invoke(pet, null);
        }

        private static void DiskWrite(Pet pet)
        {
            string smoke = Program.Smoke;
            Program.Smoke = null;
            try { pet.Save(); }
            finally { Program.Smoke = smoke; }
        }

        private static void TickNow(Pet pet, System.Reflection.BindingFlags fields, System.Reflection.MethodInfo tick)
        {
            string smoke = Program.Smoke;
            Program.Smoke = null;
            try { tick.Invoke(pet, null); }
            finally { Program.Smoke = smoke; }
        }

        private static void ObserveNow(Pet pet, bool? answer)
        {
            string smoke = Program.Smoke;
            Program.Smoke = null;
            try { pet.ObserveNameLock(answer); }
            finally { Program.Smoke = smoke; }
        }

        private static void SequenceLock(Pet pet, params bool?[] values)
        {
            int next = 0;
            Pet.LockedForNameListening = () => values[Math.Min(next++, values.Length - 1)];
        }

        private static string JsonString(string path, string key)
        {
            if (!File.Exists(path)) return null;
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path)))
                return document.RootElement.TryGetProperty(key, out System.Text.Json.JsonElement value) ? value.GetString() : null;
        }

        private static bool JsonFlag(string path, string key)
        {
            if (!File.Exists(path)) return false;
            using (System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path)))
                return document.RootElement.TryGetProperty(key, out System.Text.Json.JsonElement flag) && flag.GetBoolean();
        }

        private static System.Reflection.FieldInfo LockedAtField()
        {
            return typeof(Limbs).GetField("lockedAt", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        }

        private static System.Reflection.FieldInfo LockedHintField()
        {
            return typeof(Limbs).GetField("lockedHint", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        }

        private static System.Reflection.FieldInfo BeforePlaybackField()
        {
            return typeof(SpeechAudio).GetField("BeforePlayback", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        }

        private static string Hash(string path)
        {
            return File.Exists(path) ? Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path))) : "missing";
        }

        // The last lens captures found that a present Hyprland desktop with no snapshot skipped logind entirely, that a relock
        // between the tick's read and the resume's own start probe left no hold behind, that the tick could read the lock twice
        // and lose it in between, and that recovery messages still said "turn on" under an already-on switch.
        private static void LensChecks(Pet pet, List<string> checks)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Func<bool?> lockedWas = Pet.LockedForNameListening;
            Func<string, string[], string> runWas = Music.Run;
            Func<long> nowWas = Limbs.LockClock;
            long lockedAtWas = (long)LockedAtField().GetValue(null);
            object lockedHintWas = LockedHintField().GetValue(null);
            Settings saved = pet.Saved;
            bool capriWas = saved.ListenCapri, holdWas = saved.ListenCapriHold;
            string holdContextWas = saved.ListenCapriHoldContext;
            string nodeWas = saved.MicrophoneNode, fingerprintWas = saved.MicrophoneFingerprint;
            int xWas = saved.X, yWas = saved.Y;
            bool placedWas = saved.Placed;
            var stoppedField = typeof(Pet).GetField("nameStopped", fields);
            var startingField = typeof(Pet).GetField("nameStarting", fields);
            var statusField = typeof(Pet).GetField("nameStatus", fields);
            var epochField = typeof(Pet).GetField("nameEpoch", fields);
            var contextField = typeof(Pet).GetField("nameContext", fields);
            var lockPausedField = typeof(Pet).GetField("nameLockPaused", fields);
            var captureContext = typeof(Pet).GetMethod("CaptureNameContext", fields, null, Type.EmptyTypes, null);
            var startListening = typeof(Pet).GetMethod("StartNameListening", fields, null, Type.EmptyTypes, null);
            var writeBlockedField = typeof(Pet).GetField("settingsWriteBlocked", fields);
            var tick = typeof(Pet).GetMethod("TickNameListening", fields, null, Type.EmptyTypes, null);
            var enable = typeof(Pet).GetMethod("EnableNameListening", fields, null, new[] { typeof(bool) }, null);
            var stop = typeof(Pet).GetMethod("StopNameListening", fields, null, new[] { typeof(string) }, null);
            bool stoppedWas = (bool)stoppedField.GetValue(pet), startingWas = (bool)startingField.GetValue(pet);
            bool lockPausedWas = (bool)lockPausedField.GetValue(pet), writeBlockedWas = (bool)writeBlockedField.GetValue(pet);
            string statusWas = (string)statusField.GetValue(pet), smokeWas = Program.Smoke;
            object contextWas = contextField.GetValue(pet);
            Func<bool> started = () => !(bool)stoppedField.GetValue(pet);
            Func<bool> starting = () => (bool)startingField.GetValue(pet);
            Func<bool> lockPaused = () => (bool)lockPausedField.GetValue(pet);
            Func<long> epoch = () => (long)epochField.GetValue(pet);
            Func<bool> holdSaved = () =>
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, saved);
                    stream.Position = 0;
                    return ((Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream)).ListenCapriHold;
                }
            };
            long now = 0;
            try
            {
                pet.Talk.Dismiss();
                Limbs.LockClock = () => now;
                saved.MicrophoneNode = "check-node";
                saved.MicrophoneFingerprint = "check-fp";

                // Finding 1: no snapshot must defer to logind, never stand in for a missing answer. Smoke has no Hyprland
                // session, so the branch runs through LockedFromDesktop with real HyprlandSnapshot values and a logind seam.
                Func<bool?> logind = () => true;
                Require(Pet.LockedFromDesktop(null, logind) == true, "a missing Hyprland snapshot stopped before asking logind for a lock");
                logind = () => false;
                Require(Pet.LockedFromDesktop(null, logind) == false, "a missing snapshot ignored logind's unlocked answer");
                logind = () => null;
                Require(Pet.LockedFromDesktop(null, logind) == null, "a missing snapshot with no logind answer invented a lock answer");
                logind = () => true;
                Require(Pet.LockedFromDesktop(Hyprland(false), logind) == false, "a live snapshot deferred to logind instead of answering");
                Require(Pet.LockedFromDesktop(Hyprland(true), () => false) == true, "a locked live snapshot was overridden by logind");

                // And the production seam itself defers. Smoke has no Hyprland session, so its snapshot is absent and this
                // exercises the fallback: whatever logind says must come through instead of the old silent null.
                if (HyprlandDesktop.Current == null)
                {
                    Music.Run = (file, args) => "b true\n";
                    now += 100000;
                    Require(Pet.LockedForNameListening() == true, "the production lock seam dropped logind's locked answer when Hyprland had no snapshot");
                    Music.Run = (file, args) => "b false\n";
                    now += 100000;
                    Require(Pet.LockedForNameListening() == false, "the production lock seam dropped logind's unlocked answer when Hyprland had no snapshot");
                    Music.Run = (file, args) => null;
                    now += 100000;
                    Require(Pet.LockedForNameListening() == null, "the production lock seam invented a lock answer when neither Hyprland nor logind answered");
                }

                // Finding 2: a relock landing between the resume's eligibility check and the start's own bar check leaves no
                // hold behind, so a later unlock can never resume her. The seam answers unlocked to that eligibility read and
                // locked from then on, which is exactly a screen locking inside StartNameListening.
                saved.ListenCapri = true;
                saved.ListenCapriHold = true;
                saved.ListenCapriHoldContext = HoldContext(pet, fields);
                saved.MicrophoneNode = "check-node";
                saved.MicrophoneFingerprint = "check-fp";
                lockPausedField.SetValue(pet, true);
                stoppedField.SetValue(pet, true);
                contextField.SetValue(pet, null);
                pet.Talk.Dismiss();
                // The tick's own read is the resume's eligibility answer, so only the start's probe and its specified
                // re-probe read here; a relock landing in that gap must still come back as a hold.
                int reads = 0;
                Pet.LockedForNameListening = delegate { reads++; return true; };
                long relockEpoch = epoch();
                pet.ObserveNameLock(false);
                Require(reads == 2, "the resume read the lock " + reads + " times instead of the start's probe and its re-probe");
                Require(saved.ListenCapriHold && holdSaved(), "a relock during the resume left no hold, so a later unlock could never resume her");
                Require(lockPaused(), "a relock during the resume left her not resumable");
                Require(!started(), "a relock during the resume started her");
                Require(pet.NameListeningStatus == "Paused while the screen is locked.",
                    "a relock during the resume reported the wrong state: " + pet.NameListeningStatus);
                Require(epoch() == relockEpoch + 1, "a relock during the resume stopped her more than once");
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(started() && starting(), "the hold re-established after a relock never resumed her");
                contextField.SetValue(pet, null);
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // Finding 3: the tick reads the lock twice, so a change between the reads must still pause her as a lock.
                writeBlockedField.SetValue(pet, true);
                Program.Smoke = null;
                try
                {
                    saved.ListenCapri = true;
                    saved.ListenCapriHold = false;
                    saved.MicrophoneNode = "check-node";
                    saved.MicrophoneFingerprint = "check-fp";
                    lockPausedField.SetValue(pet, false);
                    stoppedField.SetValue(pet, false);
                    // A single read per tick now feeds both the observation and the stop branch, so one locked answer is all
                    // it takes; there is no second read that could disagree.
                    contextField.SetValue(pet, null);
                    Pet.LockedForNameListening = () => true;
                    tick.Invoke(pet, null);
                    Require(saved.ListenCapriHold && holdSaved(), "a tick that read a lock recorded no hold");
                    Require(lockPaused(), "a tick that read a lock left her not resumable");
                    Require(!started(), "a tick that read a lock left her running");
                    Require(pet.NameListeningStatus == "Paused while the screen is locked.",
                        "a tick that read a lock reported the wrong stop: " + pet.NameListeningStatus);
                    stop.Invoke(pet, new object[] { "Local name listening is off." });
                }
                finally
                {
                    Program.Smoke = smokeWas;
                    writeBlockedField.SetValue(pet, writeBlockedWas);
                }
                contextField.SetValue(pet, null);

                // Finding 4: every message sent while the switch stays on must say off and on, never "turn on".
                saved.ListenCapri = true;
                saved.ListenCapriHold = false;
                lockPausedField.SetValue(pet, false);
                stoppedField.SetValue(pet, true);
                contextField.SetValue(pet, null);
                // Quiet is the bar here, and the lock must read unlocked or its own pause would win the status.
                Pet.LockedForNameListening = () => false;
                typeof(Pet).GetField("shushed", fields).SetValue(pet, true);
                startListening.Invoke(pet, null);
                Require(pet.NameListeningStatus == "Name listening paused. Unmute and wake Capri, then turn Listen for Capri off and on.",
                    "the barred refusal still told the user to turn on an already-on switch: " + pet.NameListeningStatus);
                typeof(Pet).GetField("shushed", fields).SetValue(pet, false);
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                Require(pet.NameListeningStatus == "Local name listening is off.",
                    "the user's own off switch stopped saying to turn it on: " + pet.NameListeningStatus);
                saved.ListenCapri = true;
                lockPausedField.SetValue(pet, false);
                contextField.SetValue(pet, captureContext.Invoke(pet, null));
                Require(contextField.GetValue(pet) != null, "the check could not record a listening context to change");
                saved.MicrophoneNode = "other-node";
                Pet.LockedForNameListening = () => false;
                stoppedField.SetValue(pet, false);
                statusField.SetValue(pet, "Starting local name listening…");
                // The real tick returns at once in smoke, so this one drive swaps smoke out and restores it in finally.
                writeBlockedField.SetValue(pet, true);
                Program.Smoke = null;
                try { tick.Invoke(pet, null); }
                finally { Program.Smoke = smokeWas; writeBlockedField.SetValue(pet, writeBlockedWas); }
                Require(pet.NameListeningStatus == "Name listening stopped after a state change. Turn Listen for Capri off and on to resume.",
                    "the tick's state-change message still said turn on: " + pet.NameListeningStatus);
                contextField.SetValue(pet, null);
                stop.Invoke(pet, new object[] { "Local name listening is off." });
            }
            finally
            {
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                Program.Smoke = smokeWas;
                writeBlockedField.SetValue(pet, writeBlockedWas);
                typeof(Pet).GetField("shushed", fields).SetValue(pet, false);
                Pet.LockedForNameListening = lockedWas;
                Music.Run = runWas;
                Limbs.LockClock = nowWas;
                saved.X = xWas; saved.Y = yWas; saved.Placed = placedWas;
                saved.ListenCapri = capriWas;
                saved.ListenCapriHold = holdWas;
                saved.ListenCapriHoldContext = holdContextWas;
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                statusField.SetValue(pet, statusWas);
                contextField.SetValue(pet, contextWas);
                lockPausedField.SetValue(pet, lockPausedWas);
                LockedAtField().SetValue(null, lockedAtWas);
                LockedHintField().SetValue(null, lockedHintWas);
                pet.Talk.Dismiss();
            }
            checks.Add("PASS name listening lenses: a present Hyprland desktop with no snapshot defers to logind instead of dropping the signal, a relock during the resume keeps a hold that a later unlock can use, a tick reads the lock once and still pauses her as a lock, and every message sent while the switch stays on says off and on");
        }

        // A late review found three gaps in the lock work: a hold recorded after the listening context changed, a recovery
        // answer that spent the hold when the lock turned true mid-prompt, and an activation note emitted before a refused
        // start. It also found that driving ObserveNameLock alone would pass even if the tick stopped polling while paused.
        // These drive the real seams each finding names, with everything restored.
        private static void ReviewChecks(Pet pet, List<string> checks)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Func<bool?> lockedWas = Pet.LockedForNameListening;
            Settings saved = pet.Saved;
            bool capriWas = saved.ListenCapri, holdWas = saved.ListenCapriHold;
            string holdContextWas = saved.ListenCapriHoldContext;
            string nodeWas = saved.MicrophoneNode, fingerprintWas = saved.MicrophoneFingerprint;
            int xWas = saved.X, yWas = saved.Y;
            bool placedWas = saved.Placed;
            var stoppedField = typeof(Pet).GetField("nameStopped", fields);
            var startingField = typeof(Pet).GetField("nameStarting", fields);
            var startupField = typeof(Pet).GetField("nameStartup", fields);
            var statusField = typeof(Pet).GetField("nameStatus", fields);
            var epochField = typeof(Pet).GetField("nameEpoch", fields);
            var contextField = typeof(Pet).GetField("nameContext", fields);
            var lockPausedField = typeof(Pet).GetField("nameLockPaused", fields);
            var lifetimeField = typeof(Pet).GetField("nameLifetime", fields);
            var captureContext = typeof(Pet).GetMethod("CaptureNameContext", fields, null, Type.EmptyTypes, null);
            var answersField = typeof(Pet).GetField("answerTurn", fields);
            var shushedField = typeof(Pet).GetField("shushed", fields);
            var writeBlockedField = typeof(Pet).GetField("settingsWriteBlocked", fields);
            var tick = typeof(Pet).GetMethod("TickNameListening", fields, null, Type.EmptyTypes, null);
            var enable = typeof(Pet).GetMethod("EnableNameListening", fields, null, new[] { typeof(bool) }, null);
            var stop = typeof(Pet).GetMethod("StopNameListening", fields, null, new[] { typeof(string) }, null);
            bool stoppedWas = (bool)stoppedField.GetValue(pet), startingWas = (bool)startingField.GetValue(pet);
            bool startupWas = (bool)startupField.GetValue(pet), lockPausedWas = (bool)lockPausedField.GetValue(pet);
            bool shushedWas = (bool)shushedField.GetValue(pet), writeBlockedWas = (bool)writeBlockedField.GetValue(pet);
            string statusWas = (string)statusField.GetValue(pet), smokeWas = Program.Smoke;
            object contextWas = contextField.GetValue(pet);
            Task lifetimeWas = (Task)lifetimeField.GetValue(pet);
            object changedWas = typeof(Pet).GetField("SettingsChanged", fields).GetValue(pet);
            Func<bool> started = () => !(bool)stoppedField.GetValue(pet);
            Func<bool> starting = () => (bool)startingField.GetValue(pet);
            Func<bool> lockPaused = () => (bool)lockPausedField.GetValue(pet);
            Func<long> epoch = () => (long)epochField.GetValue(pet);
            Func<int> answers = () => (int)answersField.GetValue(pet);
            Func<bool> holdSaved = () =>
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, saved);
                    stream.Position = 0;
                    return ((Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream)).ListenCapriHold;
                }
            };
            try
            {
                pet.Talk.Dismiss();
                Pet.LockedForNameListening = () => false;
                // Check that an unlock starts listening while the decoder remains parked, as in the other lock checks.
                lifetimeField.SetValue(pet, new TaskCompletionSource().Task);
                saved.MicrophoneNode = "check-node";
                saved.MicrophoneFingerprint = "check-fp";

                // Finding 1: a context change while the screen is locked must not become a resumable pause. Her recorded context
                // is a real one captured here, then the selected microphone changes so the recorded context no longer matches.
                saved.ListenCapri = true;
                saved.ListenCapriHold = false;
                lockPausedField.SetValue(pet, false);
                contextField.SetValue(pet, captureContext.Invoke(pet, null));
                Require(contextField.GetValue(pet) != null, "the check could not record a listening context to change");
                stoppedField.SetValue(pet, false);
                statusField.SetValue(pet, "Starting local name listening…");
                saved.MicrophoneNode = "other-node";
                Pet.LockedForNameListening = () => true;
                long changedEpoch = epoch();
                pet.ObserveNameLock(true);
                Require(!saved.ListenCapriHold && !holdSaved(), "a lock recorded a resumable pause after the listening context changed");
                Require(!lockPaused(), "a lock kept a resumable pause after the listening context changed");
                Require(!started(), "a lock after a context change left her running");
                Require(pet.NameListeningStatus == "Name listening stopped after a state change. Turn Listen for Capri off and on to resume.",
                    "a lock after a context change reported the wrong stop: " + pet.NameListeningStatus);
                Require(epoch() == changedEpoch + 1, "a lock after a context change did not stop her exactly once");
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(!started(), "an unlock restarted her after the listening context changed");
                Require(epoch() == changedEpoch + 1, "an unlock moved her epoch after the listening context changed");

                // The same must hold while she is already paused by a lock: a changed context clears that hold too, so its
                // unlock can't re-arm the microphone either.
                saved.ListenCapri = true;
                saved.ListenCapriHold = true;
                lockPausedField.SetValue(pet, true);
                stoppedField.SetValue(pet, true);
                long pausedEpoch = epoch();
                Pet.LockedForNameListening = () => true;
                pet.ObserveNameLock(true);
                Require(!saved.ListenCapriHold && !holdSaved(), "a held pause survived a changed listening context");
                Require(!lockPaused(), "a held pause stayed resumable after a changed listening context");
                // She was already stopped, so dropping the hold is the whole job: no extra stop, no extra save churn.
                Require(epoch() == pausedEpoch, "a changed context moved her epoch while she was already stopped");
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(!started(), "an unlock resumed the microphone under a changed context");
                Require(epoch() == pausedEpoch, "an unlock moved her epoch under a changed context");
                int churn = 0;
                pet.SettingsChanged += () => churn++;
                pet.ObserveNameLock(true);
                Require(churn == 0, "a tick that kept seeing the lock saved settings again after the hold was cleared");
                contextField.SetValue(pet, null);
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // Finding 2: the lock can turn true while the warning is up, so the answer must re-check it, not spend the hold.
                saved.ListenCapri = false;
                saved.ListenCapriHold = true;
                saved.MicrophoneNode = "check-node";
                lockPausedField.SetValue(pet, false);
                stoppedField.SetValue(pet, true);
                Pet.LockedForNameListening = () => null;
                pet.Talk.Dismiss();
                enable.Invoke(pet, new object[] { true });
                Require(pet.Talk.Typing, "the unknown-lock warning did not open before the answer");
                Pet.LockedForNameListening = () => true;
                pet.Talk.Submit("YES");
                Require(saved.ListenCapriHold && holdSaved(), "answering YES spent the hold after the screen locked during the warning");
                Require(saved.ListenCapri, "answering YES with the screen locked did not keep the switch on");
                Require(lockPaused(), "answering YES with the screen locked did not leave her resumable");
                Require(!started(), "answering YES with the screen locked started her");
                Require(pet.NameListeningStatus == "Paused while the screen is locked.",
                    "answering YES with the screen locked reported the wrong state: " + pet.NameListeningStatus);
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(started() && starting(), "the hold kept by answering YES never resumed her on a detected unlock");
                // The saved projection too, so a lost write can't leave her paused after a restart.
                Require(!Pet.Newest(pet.Saved).ListenCapriHold, "the resumed hold was not cleared in the saved copy, so a restart would pause her");
                Require(!holdSaved(), "the resumed hold was still written to cross.json");
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // Finding 3: the limitation note belongs only to an activation that really begins capture, never a refused one.
                saved.ListenCapri = false;
                saved.ListenCapriHold = false;
                lockPausedField.SetValue(pet, false);
                shushedField.SetValue(pet, true);
                pet.Talk.Dismiss();
                int quietAnswers = answers();
                enable.Invoke(pet, new object[] { true });
                Require(!started(), "a muted activation started listening");
                Require(answers() == quietAnswers, "the limitation note showed for an activation that never started: " + (answers() - quietAnswers));
                shushedField.SetValue(pet, false);
                pet.Talk.Dismiss();
                saved.ListenCapri = false;
                quietAnswers = answers();
                enable.Invoke(pet, new object[] { true });
                Require(answers() == quietAnswers + 1, "a real activation emitted " + (answers() - quietAnswers) + " lines instead of one");
                Require(pet.Talk.Reply == "She can't always tell when the screen is locked, so on some desktops she may keep listening while it is.",
                    "a real activation lost its limitation note: " + pet.Talk.Reply);
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // Finding 5: the app's own tick is what makes a lock pause resumable, so drive the real TickNameListening while
                // held. Smoke's ticks are off and a real write must not reach the user's profile, so both are swapped out and
                // restored around this block only.
                writeBlockedField.SetValue(pet, true);
                Program.Smoke = null;
                try
                {
                    saved.ListenCapri = true;
                    saved.ListenCapriHold = true;
                    saved.ListenCapriHoldContext = HoldContext(pet, fields);
                    saved.MicrophoneNode = "check-node";
                    saved.MicrophoneFingerprint = "check-fp";
                    lockPausedField.SetValue(pet, true);
                    stoppedField.SetValue(pet, true);
                    startupField.SetValue(pet, false);
                    statusField.SetValue(pet, "Paused while the screen is locked.");
                    long heldEpoch = epoch();
                    Pet.LockedForNameListening = () => null;
                    tick.Invoke(pet, null);
                    Require(saved.ListenCapriHold, "a tick with a lost lock answer dropped the hold");
                    Require(!started(), "a tick with a lost lock answer started her");
                    Require(epoch() == heldEpoch, "a tick with a lost lock answer moved her epoch");
                    Pet.LockedForNameListening = () => false;
                    tick.Invoke(pet, null);
                    Require(!saved.ListenCapriHold, "the tick's detected unlock kept the hold");
                    Require(started() && starting(), "the tick's detected unlock did not resume her");
                    Require(epoch() == heldEpoch + 1, "the tick's detected unlock did not start her exactly once");
                    tick.Invoke(pet, null);
                    Require(epoch() == heldEpoch + 1, "a later tick started her a second time");
                }
                finally
                {
                    Program.Smoke = smokeWas;
                    writeBlockedField.SetValue(pet, writeBlockedWas);
                    saved.X = xWas; saved.Y = yWas; saved.Placed = placedWas;
                }
                stop.Invoke(pet, new object[] { "Local name listening is off." });
            }
            finally
            {
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                Program.Smoke = smokeWas;
                writeBlockedField.SetValue(pet, writeBlockedWas);
                Pet.LockedForNameListening = lockedWas;
                saved.X = xWas; saved.Y = yWas; saved.Placed = placedWas;
                saved.ListenCapri = capriWas;
                saved.ListenCapriHold = holdWas;
                saved.ListenCapriHoldContext = holdContextWas;
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                startupField.SetValue(pet, startupWas);
                statusField.SetValue(pet, statusWas);
                shushedField.SetValue(pet, shushedWas);
                contextField.SetValue(pet, contextWas);
                lifetimeField.SetValue(pet, lifetimeWas);
                lockPausedField.SetValue(pet, lockPausedWas);
                typeof(Pet).GetField("SettingsChanged", fields).SetValue(pet, changedWas);
                pet.Talk.Dismiss();
            }
            checks.Add("PASS name listening review: a context change while the screen is locked drops the hold and never resumes on unlock, the recovery answer re-checks the lock and keeps the hold for a later unlock, the limitation note shows only for an activation that really starts and never for a refused one, and the real tick keeps polling while held so a detected unlock resumes her exactly once");
        }

        // A missing Hyprland snapshot was read as locked, and with the hold persisted that wrote a false lock to disk that
        // survived a restart. These pin the real mapping from a Hyprland snapshot to a lock answer, and pin that a lock never
        // outranks an explicit Stop. Smoke has no Hyprland session, so the live VisualSnapshot read isn't reachable here; the
        // mapper it feeds is the production decision, and is driven with real HyprlandSnapshot values.
        private static void CorrectionChecks(Pet pet, List<string> checks)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Func<bool?> lockedWas = Pet.LockedForNameListening;
            Func<string, string[], string> runWas = Music.Run;
            Func<long> nowWas = Limbs.LockClock;
            Settings saved = pet.Saved;
            bool capriWas = saved.ListenCapri, holdWas = saved.ListenCapriHold;
            string holdContextWas = saved.ListenCapriHoldContext;
            string nodeWas = saved.MicrophoneNode, fingerprintWas = saved.MicrophoneFingerprint;
            var stoppedField = typeof(Pet).GetField("nameStopped", fields);
            var startingField = typeof(Pet).GetField("nameStarting", fields);
            var startupField = typeof(Pet).GetField("nameStartup", fields);
            var statusField = typeof(Pet).GetField("nameStatus", fields);
            var epochField = typeof(Pet).GetField("nameEpoch", fields);
            var stop = typeof(Pet).GetMethod("StopNameListening", fields, null, new[] { typeof(string) }, null);
            bool stoppedWas = (bool)stoppedField.GetValue(pet), startingWas = (bool)startingField.GetValue(pet);
            bool startupWas = (bool)startupField.GetValue(pet);
            bool lockPausedWas = (bool)typeof(Pet).GetField("nameLockPaused", fields).GetValue(pet);
            string statusWas = (string)statusField.GetValue(pet);
            int saves = 0;
            Action counted = () => saves++;
            object contextWas = typeof(Pet).GetField("nameContext", fields).GetValue(pet);
            object playbackWas = BeforePlaybackField()?.GetValue(null);
            bool placedWas2 = saved.Placed;
            Func<bool> started = () => !(bool)stoppedField.GetValue(pet);
            Func<long> epoch = () => (long)epochField.GetValue(pet);
            Func<bool> holdSaved = () =>
            {
                using (MemoryStream stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, saved);
                    stream.Position = 0;
                    return ((Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream)).ListenCapriHold;
                }
            };
            try
            {
                pet.Talk.Dismiss();
                saved.MicrophoneNode = "check-node";
                saved.MicrophoneFingerprint = "check-fp";
                Pet.LockedForNameListening = lockedWas;
                pet.SettingsChanged += counted;

                // Blocker 1: a desktop that answers no snapshot is one that can't report a lock, so it never holds her.
                Require(Pet.LockedFromSnapshot(null) == null, "an absent Hyprland snapshot was read as a lock");
                Require(Pet.LockedFromSnapshot(Hyprland(true)) == true, "a Hyprland snapshot that reported a lock was not honored");
                Require(Pet.LockedFromSnapshot(Hyprland(false)) == false, "an unlocked Hyprland snapshot was read as locked");

                // And that answer must never reach disk as a hold, which a restart would then trust.
                saved.ListenCapri = true;
                saved.ListenCapriHold = false;
                stoppedField.SetValue(pet, false);
                int savesBefore = saves;
                Pet.LockedForNameListening = () => Pet.LockedFromSnapshot(null);
                pet.ObserveNameLock(Pet.LockedForNameListening());
                Require(!saved.ListenCapriHold && !holdSaved(), "an absent Hyprland snapshot wrote a false lock hold");
                Require(saves == savesBefore, "an absent Hyprland snapshot asked her to save settings");
                Require(started(), "an absent Hyprland snapshot stopped name listening");

                // Blocker 2: an explicit Stop outranks any lock, so a later unlock can't bring her back.
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                long stoppedEpoch = epoch();
                saved.ListenCapri = true;
                saved.ListenCapriHold = false;
                savesBefore = saves;
                Pet.LockedForNameListening = () => true;
                pet.ObserveNameLock(true);
                Require(!saved.ListenCapriHold && !holdSaved(), "a lock after an explicit Stop wrote a hold");
                Require(saves == savesBefore, "a lock after an explicit Stop saved settings");
                Require(epoch() == stoppedEpoch, "a lock after an explicit Stop moved her epoch");
                Pet.LockedForNameListening = () => null;
                pet.ObserveNameLock(null);
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(!started(), "an unlock restarted her after an explicit Stop");
                Require(epoch() == stoppedEpoch, "an unlock moved her epoch after an explicit Stop");
                Require(pet.NameListeningStatus == "Local name listening is off.", "an unlock churned her status after an explicit Stop: " + pet.NameListeningStatus);

                // A real lock while she listens still pauses her, and the unlock brings her back once.
                saved.ListenCapri = true;
                saved.ListenCapriHold = false;
                stoppedField.SetValue(pet, false);
                long runningEpoch = epoch();
                Pet.LockedForNameListening = () => true;
                pet.ObserveNameLock(true);
                Require(saved.ListenCapriHold && holdSaved(), "a real lock while listening did not hold her");
                Require(!started(), "a real lock while listening did not stop her");
                Require(epoch() == runningEpoch + 1, "a real lock did not advance her epoch exactly once: " + runningEpoch + " -> " + epoch());
                Pet.LockedForNameListening = () => true;
                pet.ObserveNameLock(true);
                Require(epoch() == runningEpoch + 1, "a tick that still saw the lock stopped her again");
                long lockedEpoch = epoch();
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(started() && (bool)startingField.GetValue(pet), "a real lock's unlock did not bring her back");
                Require(epoch() == lockedEpoch + 1, "the unlock's start did not advance her epoch exactly once");
                Require(pet.Talk.Reply == null, "an automatic resume showed the activation warning");
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // A hold saved across a restart is a lock pause, so its detected unlock still resumes her exactly once.
                saved.ListenCapri = true;
                saved.ListenCapriHold = true;
                // A real hold remembers its context, or startup rightly treats it as stale from another profile.
                saved.ListenCapriHoldContext = typeof(Pet).GetMethod("NameHoldContext", fields, null, Type.EmptyTypes, null).Invoke(pet, null) as string;
                typeof(Pet).GetMethod("InitializeNameListening", fields).Invoke(pet, null);
                long restartEpoch = epoch();
                Require(!(bool)startupField.GetValue(pet), "a saved hold still auto-started her at startup");
                Pet.LockedForNameListening = () => null;
                pet.ObserveNameLock(null);
                Require(!started(), "a lost lock answer started her after a saved hold");
                Require(epoch() == restartEpoch, "a lost lock answer moved her epoch after a saved hold");
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(started() && (bool)startingField.GetValue(pet), "a saved hold's detected unlock did not resume her after a restart");
                Require(epoch() == restartEpoch + 1, "a saved hold's unlock did not move her epoch exactly once");
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // Blocker 2 again, through the switch: a lock seen with the switch on leaves her resumable, never running.
                saved.ListenCapri = false;
                saved.ListenCapriHold = true;
                saved.ListenCapriHoldContext = HoldContext(pet, fields);
                Pet.LockedForNameListening = () => true;
                typeof(Pet).GetMethod("EnableNameListening", fields).Invoke(pet, new object[] { true });
                Require(saved.ListenCapri && saved.ListenCapriHold, "the switch over a lock lost the opt-in or the hold");
                Require(!started(), "the switch over a lock started her");
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(started() && (bool)startingField.GetValue(pet), "the switch's lock pause never resumed on a detected unlock");
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // S2 sequencing: the limitation note only belongs on the path that really starts listening. With no microphone
                // picked she routes to the chooser instead, so the note must never be emitted and replaced by the chooser's own
                // line before it can render. Answer turns count emissions, because the chooser overwrites Talk.Reply either way.
                var answersField = typeof(Pet).GetField("answerTurn", fields);
                Func<int> answers = () => (int)answersField.GetValue(pet);
                saved.ListenCapri = false;
                saved.ListenCapriHold = false;
                saved.MicrophoneNode = null;
                saved.MicrophoneFingerprint = null;
                Pet.LockedForNameListening = () => false;
                pet.Talk.Dismiss();
                int answeredBefore = answers();
                typeof(Pet).GetMethod("EnableNameListening", fields).Invoke(pet, new object[] { true });
                Require(saved.ListenCapri, "the switch with no microphone did not save the opt-in");
                Require(answers() == answeredBefore + 1,
                    "the limitation note was emitted before the microphone chooser took over: " + (answers() - answeredBefore));
                // The chooser's own first line, so a generic error can't stand in for it. Its numbered choices arrive from the
                // detached pw-dump inventory, which smoke can't run here, so they are not asserted; the first line is the part
                // that proves the chooser took over the reply.
                Require(pet.Talk.Reply == "Checking available physical microphones…",
                    "the microphone chooser's own line was replaced or lost: " + pet.Talk.Reply);
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // And it still shows, as one line, when a microphone is picked so she really starts listening.
                saved.ListenCapri = false;
                saved.ListenCapriHold = false;
                saved.MicrophoneNode = "check-node";
                saved.MicrophoneFingerprint = "check-fp";
                pet.Talk.Dismiss();
                answeredBefore = answers();
                typeof(Pet).GetMethod("EnableNameListening", fields).Invoke(pet, new object[] { true });
                Require(answers() == answeredBefore + 1, "a real activation emitted " + (answers() - answeredBefore) + " lines instead of one");
                Require(pet.Talk.Reply == "She can't always tell when the screen is locked, so on some desktops she may keep listening while it is.",
                    "a real activation lost its limitation note: " + pet.Talk.Reply);
                stop.Invoke(pet, new object[] { "Local name listening is off." });
            }
            finally
            {
                pet.SettingsChanged -= counted;
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                Pet.LockedForNameListening = lockedWas;
                Music.Run = runWas;
                Limbs.LockClock = nowWas;
                saved.ListenCapri = capriWas;
                saved.ListenCapriHold = holdWas;
                saved.ListenCapriHoldContext = holdContextWas;
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                startupField.SetValue(pet, startupWas);
                statusField.SetValue(pet, statusWas);
                // After the Stop above, which clears it: a paused state left over from before this check is put back too.
                typeof(Pet).GetField("nameLockPaused", fields).SetValue(pet, lockPausedWas);
                typeof(Pet).GetField("nameContext", fields).SetValue(pet, contextWas);
                if (BeforePlaybackField() != null) BeforePlaybackField().SetValue(null, playbackWas);
                saved.Placed = placedWas2;
                pet.Talk.Dismiss();
            }
            checks.Add("PASS name listening corrections: a desktop with no Hyprland snapshot is unknown rather than locked and never writes a false hold nor saves, an explicit Stop outranks a later lock and its unlock with her epoch untouched, a real lock while listening pauses her exactly once and its unlock resumes her once without the activation warning, a hold saved across a restart still resumes exactly once on a detected unlock, and the limitation note shows only when she really starts listening and never over the microphone chooser's line");
        }

        private static HyprlandSnapshot Hyprland(bool locked)
        {
            return new HyprlandSnapshot("check-session", "check-layout",
                new[] { new HyprlandOutput(1, "check-out", new Avalonia.Rect(0, 0, 1920, 1080), 1920, 1080, 1, 0, 1, 0, true, new Avalonia.Rect(0, 0, 1920, 1080)) },
                Array.Empty<HyprlandWindow>(), new Avalonia.Point(20, 20), 0, locked, 0);
        }

        // A held lock keeps her paused, and only a detected unlock (never a lost answer) brings her back. These drive the state
        // machine directly: smoke's ticks are off, and the selected mic and fingerprint only have to be present for the start to
        // reach its decoder check without opening a microphone.
        private static void HoldChecks(Pet pet, List<string> checks)
        {
            var fields = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            Func<bool?> lockedWas = Pet.LockedForNameListening;
            Settings saved = pet.Saved;
            bool capriWas = saved.ListenCapri, holdWas = saved.ListenCapriHold;
            string holdContextWas = saved.ListenCapriHoldContext;
            string nodeWas = saved.MicrophoneNode, fingerprintWas = saved.MicrophoneFingerprint;
            var stoppedField = typeof(Pet).GetField("nameStopped", fields);
            var startingField = typeof(Pet).GetField("nameStarting", fields);
            var startupField = typeof(Pet).GetField("nameStartup", fields);
            var statusField = typeof(Pet).GetField("nameStatus", fields);
            var shushedField = typeof(Pet).GetField("shushed", fields);
            var stop = typeof(Pet).GetMethod("StopNameListening", fields, null, new[] { typeof(string) }, null);
            // The switch's own body, behind SetNameListening's platform and smoke guards: the same code, driven here.
            var enable = typeof(Pet).GetMethod("EnableNameListening", fields, null, new[] { typeof(bool) }, null);
            Action<bool> switchOn = on => enable.Invoke(pet, new object[] { on });
            bool stoppedWas = (bool)stoppedField.GetValue(pet), startingWas = (bool)startingField.GetValue(pet);
            bool startupWas = (bool)startupField.GetValue(pet), shushedWas = (bool)shushedField.GetValue(pet);
            bool lockPausedWas = (bool)typeof(Pet).GetField("nameLockPaused", fields).GetValue(pet);
            string statusWas = (string)statusField.GetValue(pet);
            int saves = 0;
            Action counted = () => saves++;
            object contextWas = typeof(Pet).GetField("nameContext", fields).GetValue(pet);
            object playbackWas = BeforePlaybackField()?.GetValue(null);
            Func<bool> started = () => !(bool)stoppedField.GetValue(pet);
            bool placedWas = saved.Placed;
            Func<bool> starting = () => (bool)startingField.GetValue(pet);
            try
            {
                pet.Talk.Dismiss();
                saved.MicrophoneNode = "check-node";
                saved.MicrophoneFingerprint = "check-fp";
                saved.ListenCapri = true;
                saved.ListenCapriHold = false;
                stoppedField.SetValue(pet, false);
                pet.SettingsChanged += counted;

                // A real lock holds her and stops the worker, and a lost answer after it never counts as an unlock.
                pet.ObserveNameLock(true);
                Require(pet.Saved.ListenCapriHold, "a locked screen did not hold name listening");
                Require(!started(), "a locked screen did not stop name listening");
                Require(pet.NameListeningStatus == "Paused while the screen is locked.", "a locked screen reported the wrong pause: " + pet.NameListeningStatus);
                int savedOnce = saves;
                Require(savedOnce > 0, "a locked screen did not save her hold");
                pet.ObserveNameLock(true);
                Require(saves == savedOnce, "a tick that still saw the lock saved her settings again");                pet.ObserveNameLock(null);
                Require(pet.Saved.ListenCapriHold, "a lost lock answer dropped the hold");
                Require(!started(), "a lost lock answer resumed name listening");
                Require(pet.NameListeningStatus == "Paused while the screen is locked.", "a lost lock answer changed her pause: " + pet.NameListeningStatus);

                // A detected unlock asks her back, and an automatic resume never shows the activation warning.
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(!pet.Saved.ListenCapriHold, "a detected unlock kept the hold");
                Require(started(), "a detected unlock did not ask her to listen again");
                Require(starting(), "a detected unlock did not reach her decoder check");
                Require(pet.NameListeningStatus == "Starting local name listening…", "a detected unlock reported the wrong start: " + pet.NameListeningStatus);
                Require(pet.Talk.Reply == null, "an automatic resume showed the activation warning");
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // Any other bar keeps her held instead of forcing a start.
                stoppedField.SetValue(pet, false);
                pet.ObserveNameLock(true);
                shushedField.SetValue(pet, true);
                pet.ObserveNameLock(false);
                Require(pet.Saved.ListenCapriHold, "an unlock dropped the hold while Quiet kept her barred");
                Require(!started(), "an unlock started her while Quiet kept her barred");
                shushedField.SetValue(pet, false);
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // A saved hold skips the startup start and says why, instead of looking like a fresh off switch.
                saved.ListenCapri = true;
                saved.ListenCapriHold = true;
                saved.ListenCapriHoldContext = HoldContext(pet, fields);
                typeof(Pet).GetMethod("InitializeNameListening", fields).Invoke(pet, null);
                Require(!(bool)startupField.GetValue(pet), "a saved lock hold still asked her to start at startup");
                Require(pet.NameListeningStatus == "Paused after a locked screen. Turn Listen for Capri off and on, or unlock, to bring her back.",
                    "a saved lock hold reported the wrong startup: " + pet.NameListeningStatus);

                // Turning the switch on after a hold probes the lock again: an unlocked desktop resumes and says what can't be known.
                saved.ListenCapri = false;
                saved.ListenCapriHold = true;
                Pet.LockedForNameListening = () => false;
                pet.Talk.Dismiss();
                switchOn(true);
                Require(!pet.Saved.ListenCapriHold, "turning the switch on with an unlocked desktop kept the hold");
                Require(pet.Saved.ListenCapri, "turning the switch on with an unlocked desktop did not turn the opt-in on");
                Require(started() && starting(), "turning the switch on with an unlocked desktop did not start her");
                Require(pet.NameListeningStatus == "Starting local name listening…", "the manual resume reported the wrong start: " + pet.NameListeningStatus);
                Require(pet.Talk.Reply == "She can't always tell when the screen is locked, so on some desktops she may keep listening while it is.",
                    "the manual resume did not say what she can't always know: " + pet.Talk.Reply);
                stop.Invoke(pet, new object[] { "Local name listening is off." });

                // A lock still observed turns the switch on but keeps her paused.
                saved.ListenCapri = false;
                saved.ListenCapriHold = true;
                Pet.LockedForNameListening = () => true;
                switchOn(true);
                Require(pet.Saved.ListenCapri, "turning the switch on with a lock observed did not turn the opt-in on");
                Require(pet.Saved.ListenCapriHold, "turning the switch on with a lock observed dropped the hold");
                Require(!started(), "turning the switch on with a lock observed started her");
                Require(pet.NameListeningStatus == "Paused while the screen is locked.", "the paused switch reported the wrong state: " + pet.NameListeningStatus);

                // An unknown lock asks first: NO keeps her paused and off, YES resumes, and the warning is the agreed one.
                saved.ListenCapri = false;
                saved.ListenCapriHold = true;
                Pet.LockedForNameListening = () => null;
                pet.Talk.Dismiss();
                switchOn(true);
                Require(pet.Saved.ListenCapriHold, "the unknown-lock warning dropped the hold before an answer");
                Require(!pet.Saved.ListenCapri, "the unknown-lock warning turned the opt-in on before an answer");
                Require(pet.Talk.Typing && pet.Talk.Reply == "Capri can't reliably detect whether your screen is locked. Her microphone may keep listening while your screen is locked. Type YES to resume listening, or NO to keep her paused.",
                    "the unknown-lock warning was missing or wrong: " + pet.Talk.Reply);
                pet.Talk.Submit("no");
                Require(pet.Saved.ListenCapriHold, "answering NO to the unknown-lock warning dropped the hold");
                Require(!pet.Saved.ListenCapri, "answering NO to the unknown-lock warning turned the opt-in on");
                Require(pet.NameListeningStatus == "Local name listening is off.", "answering NO reported the wrong state: " + pet.NameListeningStatus);
                switchOn(true);
                pet.Talk.Submit("YES");
                Require(!pet.Saved.ListenCapriHold, "answering YES to the unknown-lock warning kept the hold");
                Require(pet.Saved.ListenCapri, "answering YES to the unknown-lock warning did not turn the opt-in on");
                Require(started() && starting(), "answering YES to the unknown-lock warning did not start her");
                Require(pet.NameListeningStatus == "Starting local name listening…", "answering YES reported the wrong start: " + pet.NameListeningStatus);
                // Talk is always built with her, so the null-Talk guard on this warning has no reachable state to check.

                // The hold is saved in cross.json, so a restart remembers it, and an older copy rewriting that file can't drop it.
                Settings held = new Settings { ListenCapri = true, ListenCapriHold = true, ListenCapriHoldContext = "hold-context" };
                Settings reread;
                using (MemoryStream stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, held);
                    stream.Position = 0;
                    reread = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                }
                Require(reread.ListenCapriHold, "the lock hold was not saved in cross.json");
                Require(reread.ListenCapriHoldContext == "hold-context", "the lock hold's context was not saved in cross.json");
                Require(Pet.Newest(held).ListenCapriHold, "the newer settings copy dropped the lock hold");
                Require(Pet.Newest(held).ListenCapriHoldContext == "hold-context", "the newer settings copy dropped the lock hold's context");
                Settings oldCopy = new Settings();
                Pet.Kept(oldCopy, Pet.Newest(held));
                Require(oldCopy.ListenCapriHold, "an older copy's rewrite dropped the lock hold");
                Require(oldCopy.ListenCapriHoldContext == "hold-context", "an older copy's rewrite dropped the lock hold's context");
            }
            finally
            {
                pet.SettingsChanged -= counted;
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                Pet.LockedForNameListening = lockedWas;
                saved.ListenCapri = capriWas;
                saved.ListenCapriHold = holdWas;
                saved.ListenCapriHoldContext = holdContextWas;
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                startupField.SetValue(pet, startupWas);
                statusField.SetValue(pet, statusWas);
                shushedField.SetValue(pet, shushedWas);
                // After the Stop above, which clears it: a paused state left over from before this check is put back too.
                typeof(Pet).GetField("nameLockPaused", fields).SetValue(pet, lockPausedWas);
                typeof(Pet).GetField("nameContext", fields).SetValue(pet, contextWas);
                if (BeforePlaybackField() != null) BeforePlaybackField().SetValue(null, playbackWas);
                saved.Placed = placedWas;
                pet.Talk.Dismiss();
            }
            checks.Add("PASS name listening holds: a real lock pauses her without re-saving each tick and a lost answer after it never resumes her, a detected unlock asks her back without the activation warning unless another control bars her, a saved hold skips the startup start and says why, and turning the switch on re-probes the lock with the agreed warning before YES or NO");
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
