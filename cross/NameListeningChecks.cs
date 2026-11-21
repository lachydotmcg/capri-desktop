using System;
using System.Collections.Generic;

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
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
