using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Threading.Tasks;

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
                Require(pet.NameListeningStatus == "Name listening stopped after a state change. Turn Listen for Capri on to resume.",
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
                Require(epoch() == pausedEpoch + 1, "a changed context did not stop her paused state exactly once");
                Pet.LockedForNameListening = () => false;
                pet.ObserveNameLock(false);
                Require(!started(), "an unlock resumed the microphone under a changed context");
                Require(epoch() == pausedEpoch + 1, "an unlock moved her epoch under a changed context");
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
                Require(pet.Talk.Reply != null && pet.Talk.Reply != "She can't always tell when the screen is locked, so on some desktops she may keep listening while it is.",
                    "the microphone chooser's own line was replaced by the limitation note: " + pet.Talk.Reply);
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
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                startupField.SetValue(pet, startupWas);
                statusField.SetValue(pet, statusWas);
                // After the Stop above, which clears it: a paused state left over from before this check is put back too.
                typeof(Pet).GetField("nameLockPaused", fields).SetValue(pet, lockPausedWas);
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
            Func<bool> started = () => !(bool)stoppedField.GetValue(pet);
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
                Settings held = new Settings { ListenCapri = true, ListenCapriHold = true };
                Settings reread;
                using (MemoryStream stream = new MemoryStream())
                {
                    new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, held);
                    stream.Position = 0;
                    reread = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                }
                Require(reread.ListenCapriHold, "the lock hold was not saved in cross.json");
                Require(Pet.Newest(held).ListenCapriHold, "the newer settings copy dropped the lock hold");
                Settings oldCopy = new Settings();
                Pet.Kept(oldCopy, Pet.Newest(held));
                Require(oldCopy.ListenCapriHold, "an older copy's rewrite dropped the lock hold");            }
            finally
            {
                pet.SettingsChanged -= counted;
                stop.Invoke(pet, new object[] { "Local name listening is off." });
                Pet.LockedForNameListening = lockedWas;
                saved.ListenCapri = capriWas;
                saved.ListenCapriHold = holdWas;
                saved.MicrophoneNode = nodeWas;
                saved.MicrophoneFingerprint = fingerprintWas;
                stoppedField.SetValue(pet, stoppedWas);
                startingField.SetValue(pet, startingWas);
                startupField.SetValue(pet, startupWas);
                statusField.SetValue(pet, statusWas);
                shushedField.SetValue(pet, shushedWas);
                // After the Stop above, which clears it: a paused state left over from before this check is put back too.
                typeof(Pet).GetField("nameLockPaused", fields).SetValue(pet, lockPausedWas);
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
