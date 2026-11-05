using System;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed partial class Settings
    {
        [DataMember] internal string CareSwitches;
        [DataMember] internal int[] CareTimes;
        [DataMember] internal long CareNight;
        [DataMember] internal bool NoHeadPats;
        [DataMember] internal bool NoFollowUps;
        [DataMember] internal bool NoKeepAwake;
        [DataMember] internal string Vault;

        internal bool CareOn(CareKind kind)
        {
            string switches = CareSwitches != null && CareSwitches.Length == 7 ? CareSwitches : "1101111";
            return switches[(int)kind] == '1';
        }

        internal int CareTime(CareKind kind)
        {
            int[] defaults = { 60, 90, 25, 1, 360, 120, 60 };
            int at = (int)kind;
            if (CareTimes == null || CareTimes.Length != 7) return defaults[at];
            int value = CareTimes[at];
            return kind == CareKind.Late ? value >= 0 && value < 24 ? value : 1 : value > 0 ? value : defaults[at];
        }

        internal void SetCare(CareKind kind, bool on, int time)
        {
            char[] switches = new char[7];
            int[] times = new int[7];
            for (int i = 0; i < 7; i++) { switches[i] = CareOn((CareKind)i) ? '1' : '0'; times[i] = CareTime((CareKind)i); }
            switches[(int)kind] = on ? '1' : '0'; times[(int)kind] = time;
            CareSwitches = new string(switches); CareTimes = times;
        }
    }

    internal sealed partial class Pet
    {
        private Care care;
        private CareLines careLines;
        private bool careChecking, careNudge;
        private double careAt, careGameAt;
        private string careGame;
        private double inputFrom, inputUntil;
        private double inputHandled = double.NegativeInfinity;
        private bool inputSampled;
        private int inputTurn, answerTurn;
        private readonly FollowUp followUp = new FollowUp();
        internal Action<Action> FollowQueue = run => ThreadPool.QueueUserWorkItem(_ => run());
        internal bool HeadPats { get { return !Saved.NoHeadPats; } }
        internal bool FollowUps { get { return !Saved.NoFollowUps; } }

        private void StartCare()
        {
            careLines = new CareLines(Path.Combine(Program.Smoke ?? folder, "check-ins.md"));
            careLines.Create();
            care = new Care(() => Now, () => DateTime.Now, careLines.Pick);
        }

        internal bool CareOn(CareKind kind) { return Saved.CareOn(kind); }
        internal int CareTime(CareKind kind) { return Saved.CareTime(kind); }
        internal void SetCare(CareKind kind, bool on) { Saved.SetCare(kind, on, CareTime(kind)); if (!on && kind == CareKind.Bored) ForgetNudges(); Save(); }
        internal void SetCareTime(CareKind kind, int time) { Saved.SetCare(kind, CareOn(kind), time); Save(); }
        internal void SetHeadPats(bool on) { Saved.NoHeadPats = !on; ForgetPets(); Save(); }
        internal void SetFollowUps(bool on) { Saved.NoFollowUps = !on; if (!on) Answered(); Save(); }
        internal void OpenCare()
        {
            if (careLines.Create()) Limbs.Launch(OperatingSystem.IsMacOS() ? "open" : "xdg-open", new[] { Path.Combine(Program.Smoke ?? folder, "check-ins.md") });
            else Talk.Answer("(check-ins)", "Couldn't open check-ins.md.");
        }

        private void CheckHabits(double now)
        {
            if (quitting || careChecking || now < careAt) return;
            careAt = now + 0.5;
            careChecking = true;
            bool games = CareOn(CareKind.Game) && now >= careGameAt;
            if (games) careGameAt = now + 15;
            Func<double> read = Idle;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double quiet = -1;
                double from = Now, until = from;
                string playing = careGame;
                try { quiet = read(); until = Now; if (games) playing = Hands.GameOpen(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    careChecking = false;
                    if (quitting) return;
                    if (games) careGame = playing;
                    if (quiet >= 0 && !double.IsInfinity(quiet))
                    {
                        ObserveInput(from, until, quiet);
                        CheckCare(Now, (long)((quiet + Now - until) * 1000));
                        CheckNews((long)((quiet + Now - until) * 1000));
                    }
                    CheckFollowUp(Now);
                });
            });
        }

        internal void CheckCare(double now, long quiet)
        {
            bool free = quiet <= 60000 && !away && !Hushed && CanPop && !JobActive && !FullScreen();
            string line = care.Check(!Saved.NoCheckIns, CareOn, CareTime, ref Saved.CareNight, quiet, free, careGame, noticed, nudgeGap / NudgeGap);
            if (line == null) return;
            if (care.Said == CareKind.Late) Save();
            if (care.Said == CareKind.Bored) Nudge(now, Mouse(), line);
            else Reply(asking, "(check-in)", line);
        }

        internal void ObserveInput(double from, double until, double quiet)
        {
            if (quiet < 0 || double.IsNaN(quiet) || double.IsInfinity(quiet)) return;
            double first = from - quiet - 0.002, last = until - quiet + 0.002;
            bool changed = inputSampled && first > inputUntil;
            if ((changed || !inputSampled) && first > inputHandled) InputChanged();
            if (!inputSampled || changed || last < inputFrom) { inputFrom = first; inputUntil = last; }
            else { inputFrom = Math.Max(inputFrom, first); inputUntil = Math.Min(inputUntil, last); }
            inputSampled = true;
        }

        private void InputChanged() { inputTurn++; followUp.Drop(); }
        internal void Typed() { inputHandled = Now; InputChanged(); }
        internal void Answered() { answerTurn++; followUp.Drop(); }
        private bool FollowAllowed { get { return FollowUps && !quitting && !away && !Hushed && !JobActive && !Talk.Typing && !meeting.Asking; } }

        internal void HoldFollowUp(string line) { followUp.Hold(line, answerTurn, inputTurn, 0); }

        internal void CheckFollowUp(double now)
        {
            string line = followUp.Take(now, answerTurn, inputTurn, 0, FollowAllowed, Talk.Finished);
            if (line != null) Reply(asking, "(follow-up)", line, null, true);
        }

        internal bool ShowReply(int ticket, string heard, string said, double seconds, string after, bool following, int input, int answer)
        {
            if (ticket != asking || following && (!FollowAllowed || input != inputTurn || answer != answerTurn)) return false;
            if (HoldNews(ticket)) return false;
            bool current = answer == answerTurn;
            Talk.Answer(heard, said, seconds);
            if (ticket == newsTicket) KeepNews();
            if (following) Brain.Followed(said);
            else if (current && input == inputTurn && FollowAllowed) HoldFollowUp(after);
            return true;
        }

        internal void PresentReply(int ticket, string heard, string said, double seconds, string after, bool following, int input, int answer, Action shown)
        {
            if (!following) { if (ShowReply(ticket, heard, said, seconds, after, false, input, answer)) shown(); return; }
            Func<double> read = Idle;
            FollowQueue(delegate
            {
                double from = Now, quiet = -1;
                try { quiet = read(); }
                catch (Exception) { }
                double until = Now;
                Action finish = delegate
                {
                    if (quiet < 0 || double.IsNaN(quiet) || double.IsInfinity(quiet)) return;
                    ObserveInput(from, until, quiet);
                    if (ShowReply(ticket, heard, said, seconds, after, true, input, answer)) shown();
                };
                if (Dispatcher.UIThread.CheckAccess()) finish(); else Dispatcher.UIThread.Post(finish);
            });
        }
    }
}
