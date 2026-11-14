using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class ComputerGameSession
    {
        internal const int HistoryLimit = 16, DeliveryLimit = 1024;
        internal readonly int RoundGoal;
        internal readonly bool UntilWin;
        internal readonly TimeSpan? Duration;
        internal int Rounds, Wins;
        internal long LastRoundAction;
        internal long Started, LastInputAt;
        internal string ContinueReason = "Enter the requested game and play it.";
        internal string AppName;
        internal long TransitionUntil;
        internal ComputerSnapshot TransitionCompletion;
        internal double ExpectedPointerX = double.NaN, ExpectedPointerY = double.NaN;

        internal ComputerGameSession(int rounds = 0, bool untilWin = false, TimeSpan? duration = null)
        { RoundGoal = rounds; UntilWin = untilWin; Duration = duration; }

        internal static ComputerGameSession FromRequest(string task, string asked)
        {
            string text = string.IsNullOrWhiteSpace(asked) ? task ?? "" : asked;
            string words = text.ToLowerInvariant();
            if (!Regex.IsMatch(words, @"\b(play|playing)\b|\b(?:continue|resume)\s+(?:the\s+)?(?:current\s+)?game\b") ||
                Regex.IsMatch(words, @"\b(song|music|track|album|playlist|video|podcast|spotify|youtube|soundcloud|netflix|movie|watch)\b") ||
                Regex.IsMatch(words, @"\b(chess|puzzle)\b") && Regex.IsMatch(words, @"\b(solve|analyse|analyze|advise|advice|tell|suggest|explain)\b") ||
                Regex.IsMatch(words, @"^\s*(open|launch|start|boot)\b") && !Regex.IsMatch(words, @"\b(and|then)\s+play\b")) return null;
            int rounds = 0;
            Match round = Regex.Match(words, @"\b(?<n>one|1|a|single|two|2|three|3|\d+)\s+(?:rounds?|matches?|runs?|games?)\b");
            if (round.Success)
            {
                string n = round.Groups["n"].Value;
                rounds = Number(n);
                if (rounds < 1 || rounds > 1000) throw new InvalidOperationException("Choose between one and 1000 game rounds.");
            }
            bool win = Regex.IsMatch(words, @"\buntil\s+(?:(?:you|i|we)\s+)?(?:get\s+(?:a\s+)?)?win\b|\b(?:one|a|1)\s+win\b");
            TimeSpan? duration = null;
            Match timed = Regex.Match(words, @"\bfor\s+(?<n>\d+|one|two|three|five|ten|a|an)\s+(?<unit>seconds?|minutes?|hours?)\b");
            if (timed.Success)
            {
                string n = timed.Groups["n"].Value;
                int number = Number(n);
                double seconds = number * (timed.Groups["unit"].Value.StartsWith("hour") ? 3600.0 : timed.Groups["unit"].Value.StartsWith("minute") ? 60 : 1);
                if (seconds < 1 || seconds > 86400) throw new InvalidOperationException("Choose a game duration between one second and 24 hours.");
                duration = TimeSpan.FromSeconds(seconds);
            }
            return new ComputerGameSession(rounds, win, duration);
        }

        private static int Number(string text)
        {
            if (text == "one" || text == "a" || text == "an" || text == "single") return 1;
            if (text == "two") return 2;
            if (text == "three") return 3;
            if (text == "five") return 5;
            if (text == "ten") return 10;
            if (!int.TryParse(text, out int value)) throw new InvalidOperationException("The requested game ending exceeds its supported range.");
            return value;
        }

        internal void Begin(long now) { if (Started == 0) { Started = now; LastInputAt = now; } }
        internal bool TimeEnded(long now) => Duration.HasValue && Started != 0 && Stopwatch.GetElapsedTime(Started, now) >= Duration.Value;
        internal object State(long now) => new { app = AppName, rounds = Rounds, wins = Wins, requested_rounds = RoundGoal,
            until_win = UntilWin, requested_seconds = Duration?.TotalSeconds, elapsed_seconds = Started == 0 ? 0 : Stopwatch.GetElapsedTime(Started, now).TotalSeconds,
            next = ContinueReason, stop = "The owner may say stop; do not infer that one finished round ends an ongoing session." };

        // A prior end screen or animation cannot be counted as a newly played round.
        internal bool ObserveEnd(string outcome, long inputs)
        {
            if (outcome != "win" && outcome != "loss" && outcome != "complete")
            { ContinueReason = "No round ending was established. Keep playing, or ask plainly if these controls cannot do it."; return false; }
            if (inputs <= LastRoundAction)
            { ContinueReason = "That end screen has not established another played round. Enter/start the next round; do not count it twice."; return false; }
            LastRoundAction = inputs; Rounds++;
            if (outcome == "win") Wins++;
            if (RoundGoal > 0 && Rounds >= RoundGoal || UntilWin && Wins > 0) return true;
            ContinueReason = "That round ended. The requested session continues: start another round and keep playing.";
            return false;
        }

        internal void Record(ComputerJob job, string step)
        {
            job.Steps.Add(step);
            if (job.Steps.Count > HistoryLimit) job.Steps.RemoveRange(0, job.Steps.Count - HistoryLimit);
        }

        internal static void Rebind(ComputerSnapshot before, ComputerSnapshot after)
        {
            if (before.Session != after.Session || before.Layout != after.Layout || before.Output != after.Output ||
                before.ObservationEpoch != after.ObservationEpoch || before.Target.Pid != after.Target.Pid || before.Target.Birth != after.Target.Birth ||
                before.Target.App != after.Target.App || before.Target.XWayland != after.Target.XWayland || before.Target.Monitor != after.Target.Monitor || before.Target.Workspace != after.Target.Workspace || !after.Active)
                throw new InvalidOperationException("The game target changed outside its declared same-process transition.");
        }

        internal static void CompleteTransition(ComputerSnapshot before, ComputerSnapshot after)
        {
            Rebind(before, after);
            long allowedEpoch = before.FocusEpoch + (Hands.OnMac || before.Target.Address == after.Target.Address ? 0 : 1);
            if (after.FocusEpoch != allowedEpoch)
                throw new InvalidOperationException("Focus changed outside the delivered game's single window transition.");
        }
    }

    internal sealed class ComputerGameLauncher
    {
        internal Func<List<InstalledApp>> Catalog = () => Hands.Catalog();
        internal Func<List<OpenWindow>> Windows = () => Hands.Windows();
        internal Action<string> Launch = id => Hands.Run(id);
        internal Func<OpenWindow, bool> Focus = window => Hands.Steer(window, "switch_to");
        internal Func<bool> MayOpen = () => Security.On(Security.Open);
        internal TimeSpan WaitLimit = TimeSpan.FromSeconds(30);

        private static string Plain(string text) => Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim();
        private static bool Names(string text, string name)
        {
            string words = Plain(text), wanted = Plain(name);
            return wanted.Length > 1 && ((" " + words + " ").Contains(" " + wanted + " ", StringComparison.Ordinal) ||
                wanted.Length >= 4 && words.Replace(" ", "").Contains(wanted.Replace(" ", ""), StringComparison.Ordinal));
        }
        internal static InstalledApp Resolve(IEnumerable<InstalledApp> apps, string task, string asked)
        {
            string owner = string.IsNullOrWhiteSpace(asked) ? task : asked;
            var candidates = apps.Where(app => Names(owner, app.Name)).Where(app =>
                !Regex.IsMatch(app.Name, @"(?i)^(?:steam|epic games launcher|lutris|heroic|terminal|kitty|foot|xterm|capri|spotify|youtube)$")).ToList();
            if (candidates.Count == 0) return null;
            int longest = candidates.Max(app => Plain(app.Name).Length);
            candidates = candidates.Where(app => Plain(app.Name).Length == longest).GroupBy(app => app.Id).Select(group => group.First()).ToList();
            if (candidates.Count != 1) throw new InvalidOperationException("More than one installed game matches; name the exact game.");
            return candidates[0];
        }
        internal static bool Matches(OpenWindow window, string name) => !Regex.IsMatch(window.Title ?? "", @"(?i)\blauncher\b") && (Names(window.Title, name) || Names(window.Program, name));
        private static bool Unspecified(string owner) => Regex.IsMatch(owner ?? "", @"(?ix)^\s*(?:(?:please|can\s+you|could\s+you|let['’]s)\s+)*(?:play|continue|resume)(?:\s+(?:(?:the|a|some|any|this|current|open|active)\s+)*games?)?(?:\s+with\s+me)?(?:\s+(?:for|until)\b.*)?\s*[.!?]*$");
        private static bool OwnerNames(OpenWindow window, string owner)
        {
            if (Regex.IsMatch(window.Title ?? "", @"(?i)\blauncher\b")) return false;
            string program = Regex.Split(window.Program ?? "", @"[./\\]").Last();
            return Names(owner, window.Title) || Names(owner, program);
        }

        internal ComputerSnapshot Prepare(ComputerJob job, IComputerDesktop desktop, Action current, CancellationToken token)
        {
            current(); token.ThrowIfCancellationRequested();
            InstalledApp app = Resolve(Catalog(), job.Task, job.Asked);
            string name = app?.Name;
            string owner = string.IsNullOrWhiteSpace(job.Asked) ? job.Task : job.Asked;
            bool currentGame = Unspecified(owner);
            OpenWindow Pick()
            {
                List<OpenWindow> matches = Windows().Where(window => name == null
                    ? Hands.Gamey != null && Hands.Gamey(window) && (currentGame || OwnerNames(window, owner))
                    : Matches(window, name)).ToList();
                if (matches.Count > 1) throw new InvalidOperationException("More than one game window matches; select one explicitly before asking me to play.");
                return matches.SingleOrDefault();
            }
            OpenWindow picked = Pick();
            if (picked == null)
            {
                if (app == null) throw new InvalidOperationException("I couldn't identify the installed game or a unique open game. Name the game first.");
                if (!MayOpen()) throw new InvalidOperationException("Your Security settings ask before opening apps. Open " + name + " through Capri first, then ask me to play.");
                current(); token.ThrowIfCancellationRequested(); Launch(app.Id);
                long began = Stopwatch.GetTimestamp();
                while (picked == null && Stopwatch.GetElapsedTime(began) < WaitLimit)
                {
                    current(); token.ThrowIfCancellationRequested(); picked = Pick();
                    if (picked == null && token.WaitHandle.WaitOne(100)) token.ThrowIfCancellationRequested();
                }
                if (picked == null) throw new InvalidOperationException("I launched " + name + ", but its actual game window did not appear before the bounded wait ended.");
            }
            current(); token.ThrowIfCancellationRequested();
            if (!Focus(picked)) throw new InvalidOperationException("The requested game window could not be focused.");
            current(); token.ThrowIfCancellationRequested();
            ComputerSnapshot bound = desktop.Snapshot();
            if (bound.Target.Address != "0x" + picked.Handle.ToInt64().ToString("x") || !bound.Active)
                throw new InvalidOperationException("The active native window is not the resolved game window.");
            job.Game.AppName = name ?? bound.Target.App;
            return bound;
        }
    }
}
