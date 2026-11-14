using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Input;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal sealed class MoveReports
    {
        internal sealed record Ask(PixelPoint At, double When, long Number);
        private List<Ask> moves = new();
        private List<Ask> watch = new();
        private List<double> taken = new();
        private PixelPoint? mismatch, offset, sample;
        private PixelPoint sampleAsked;
        private long number, mismatchNumber, sampleNumber;
        private double mismatchAt, lastAt = double.NegativeInfinity, blocked;
        private int failures;
        internal bool Stuck { get; private set; }
        internal int Takes { get; private set; }
        internal PixelPoint LastAsked { get; private set; }
        internal PixelPoint Reported { get; private set; }
        internal Action<PixelPoint, PixelPoint> Limited;
        internal Action<bool> StuckChanged;
        internal int Count { get { return moves.Count; } }

        internal static bool Near(PixelPoint a, PixelPoint b)
        {
            return Math.Abs((long)a.X - b.X) <= 2 && Math.Abs((long)a.Y - b.Y) <= 2;
        }

        internal MoveReports Copy()
        {
            var copy = (MoveReports)MemberwiseClone();
            copy.moves = new List<Ask>(moves);
            copy.watch = new List<Ask>(watch);
            copy.taken = new List<double>(taken);
            return copy;
        }

        internal void Clear()
        {
            moves.Clear(); watch.Clear(); taken.Clear();
            mismatch = offset = sample = null;
            number = mismatchNumber = sampleNumber = 0;
            lastAt = double.NegativeInfinity; blocked = 0; failures = Takes = 0; Stuck = false;
        }

        internal void Add(PixelPoint at) { moves.Add(new Ask(at, 0, ++number)); }
        internal void RemoveAt(int index) { moves.RemoveAt(index); }
        internal void RemoveRange(int index, int count) { moves.RemoveRange(index, count); }
        internal int IndexOf(PixelPoint at) { return moves.FindIndex(m => m.At == at); }

        private void Trim(double now) { moves.RemoveAll(m => now - m.When > 1.5); }

        internal void Asked(PixelPoint at, double now, bool real = false)
        {
            Trim(now);
            bool moved = !Near(at, LastAsked);
            LastAsked = at; lastAt = now;
            var move = new Ask(at, now, ++number);
            moves.Add(move);
            if (real && moved) watch.Add(move);
        }

        internal void Observe(PixelPoint at) { Reported = at; }

        internal bool Expects(PixelPoint at, double now)
        {
            Trim(now);
            return moves.Exists(m => Near(at, m.At) || offset.HasValue && Near(at, new PixelPoint(m.At.X + offset.Value.X, m.At.Y + offset.Value.Y)));
        }

        private void Confirm(int index, bool shifted)
        {
            long confirmed = moves[index].Number;
            moves.RemoveRange(0, index + 1);
            watch.RemoveAll(m => m.Number <= confirmed);
            failures = 0;
            if (Stuck) { Stuck = false; StuckChanged?.Invoke(false); }
            mismatch = sample = null;
            if (!shifted) offset = null;
        }

        internal PixelPoint? Report(PixelPoint at, PixelPoint current, double now, bool busy)
        {
            Reported = at;
            Trim(now);
            // Any answer, even one that puts her somewhere else, means the desktop is listening. Only silence counts as stuck.
            watch.Clear(); failures = 0;
            if (Stuck && Near(at, LastAsked)) { Stuck = false; StuckChanged?.Invoke(false); }
            int match = moves.FindLastIndex(m => Near(at, m.At));
            if (match >= 0) { Confirm(match, false); return null; }
            if (Near(at, current))
            {
                mismatch = sample = null; offset = null;
                return null;
            }
            if (offset.HasValue)
            {
                PixelPoint shift = offset.Value;
                match = moves.FindLastIndex(m => Near(at, new PixelPoint(m.At.X + shift.X, m.At.Y + shift.Y)));
                if (match >= 0) { Confirm(match, true); return null; }
                if (Near(at, new PixelPoint(current.X + shift.X, current.Y + shift.Y))) { mismatch = null; return null; }
            }
            if (now < blocked) return null;
            if (number > 0 && now - lastAt <= 1.5)
            {
                PixelPoint delta = new PixelPoint(at.X - LastAsked.X, at.Y - LastAsked.Y);
                if (sample.HasValue && sampleNumber != number && sampleAsked != LastAsked && Near(delta, sample.Value))
                {
                    offset = delta;
                    match = moves.FindLastIndex(m => Near(at, new PixelPoint(m.At.X + delta.X, m.At.Y + delta.Y)));
                    if (match >= 0) { Confirm(match, true); return null; }
                }
                sample = delta; sampleNumber = number; sampleAsked = LastAsked;
            }
            bool repeated = mismatch.HasValue && Near(at, mismatch.Value);
            mismatch = at; mismatchAt = now; mismatchNumber = number;
            return repeated && !busy && now - lastAt >= 0.3 ? Take(now) : null;
        }

        private PixelPoint? Take(double now)
        {
            if (now < blocked) return null;
            taken.RemoveAll(at => now - at >= 2);
            if (taken.Count >= 3)
            {
                blocked = taken[0] + 2;
                Limited?.Invoke(LastAsked, Reported);
                mismatch = null;
                return null;
            }
            PixelPoint? at = mismatch;
            mismatch = null;
            if (at.HasValue) { taken.Add(now); Takes++; }
            return at;
        }

        internal PixelPoint? Tick(double now, bool busy)
        {
            Trim(now);
            foreach (Ask move in watch)
            {
                if (now - move.When < 1) break;
                failures++;
            }
            watch.RemoveAll(m => now - m.When >= 1);
            if (failures >= 3 && !Stuck) { Stuck = true; StuckChanged?.Invoke(true); }
            if (!busy && mismatch.HasValue && mismatchNumber == number && now - mismatchAt >= 0.3 && now - lastAt >= 0.3) return Take(now);
            return null;
        }

        internal sealed class TestDesk
        {
            internal readonly string Name;
            private readonly int bottom;
            private readonly Queue<(double When, PixelPoint At)> replies = new();
            internal TestDesk(string name, int bottom = 860) { Name = name; this.bottom = bottom; }
            internal void Ask(PixelPoint at, double now)
            {
                if (Name == "offset") at = new PixelPoint(at.X + 4, at.Y + 28);
                if (Name == "rounding") at = new PixelPoint((int)Math.Floor(Math.Floor(at.X / 1.5) * 1.5), (int)Math.Floor(Math.Floor(at.Y / 1.5) * 1.5));
                if (Name == "clamping") at = new PixelPoint(at.X, Math.Min(at.Y, bottom));
                replies.Enqueue((now + (Name == "late" ? 0.6 : 0), at));
            }
            internal void Answer(double now, Action<PixelPoint> report)
            {
                while (replies.Count > 0 && replies.Peek().When <= now + 0.00001) report(replies.Dequeue().At);
            }
        }

        internal static void Checks(List<string> checks)
        {
            foreach (string kind in new[] { "late", "offset", "rounding", "clamping" })
            {
                var reports = new MoveReports();
                var desk = new TestDesk(kind);
                PixelPoint anchor = new PixelPoint(300, 0);
                double now = 0;
                bool falling = true;
                Action<PixelPoint?> take = at => { if (at.HasValue) anchor = at.Value; };
                Action<PixelPoint> ask = at => { anchor = at; reports.Asked(at, now); desk.Ask(at, now); };
                for (int i = 0; i < 1300; i++)
                {
                    now = i * 0.05;
                    int y = anchor.Y;
                    if (falling) { ask(new PixelPoint(anchor.X, Math.Min(900, anchor.Y + 15))); falling = anchor.Y < 900; }
                    if (i == 650) ask(new PixelPoint(anchor.X - 32, anchor.Y));
                    desk.Answer(now, at => take(reports.Report(at, anchor, now, falling)));
                    take(reports.Tick(now, falling));
                    if (falling && anchor.Y < y) throw new Exception(kind + " stepped a fall back up.");
                    if (i > 70 && i != 650 && anchor.Y != y) throw new Exception(kind + " drifted at idle.");
                }
                if (reports.Takes > 1 || kind == "clamping" && (reports.Takes != 1 || anchor.Y != 860)) throw new Exception(kind + " took the wrong desktop reports.");
                checks.Add("PASS " + kind + " fall and 60 seconds idle keep their anchor with one facing flip");
            }
            var drag = new MoveReports();
            for (int i = 0; i < 24; i++) drag.Asked(new PixelPoint(i * 10, 40), i * 0.05);
            PixelPoint end = new PixelPoint(230, 40);
            for (int i = 12; i < 24; i++)
                if (drag.Report(new PixelPoint(i * 10, 40), end, i * 0.05 + 0.6, false).HasValue) throw new Exception("A late drag replayed after release.");
            if (drag.Tick(3, false).HasValue) throw new Exception("A released drag left a mismatch.");
            checks.Add("PASS late drag replies confirm without replaying after release");
            var clamp = new MoveReports();
            clamp.Asked(new PixelPoint(10, 100), 0);
            if (clamp.Report(new PixelPoint(10, 60), new PixelPoint(10, 100), 0.1, true).HasValue || clamp.Tick(0.5, true).HasValue || clamp.Tick(0.6, false) != new PixelPoint(10, 60)) throw new Exception("A KWin clamp did not wait until still.");
            checks.Add("PASS KWin's final clamp waits for stillness and is taken once");
            var duplicate = new MoveReports();
            duplicate.Asked(new PixelPoint(10, 100), 0);
            duplicate.Report(new PixelPoint(10, 60), new PixelPoint(10, 100), 0.1, true);
            duplicate.Asked(new PixelPoint(10, 100), 0.2);
            duplicate.Report(new PixelPoint(10, 60), new PixelPoint(10, 100), 0.3, true);
            if (duplicate.Tick(0.7, false) != new PixelPoint(10, 60)) throw new Exception("Repeated identical targets taught a false frame offset.");
            checks.Add("PASS repeated requests for the same position do not hide a clamp as an offset");
            foreach (bool late in new[] { false, true })
            {
                var stuck = new MoveReports();
                for (int i = 0; i < 80; i++)
                {
                    double now = i * 0.05;
                    stuck.Asked(new PixelPoint(i * 10 + 10, 50), now, true);
                    if (late && i >= 12) stuck.Report(new PixelPoint((i - 12) * 10 + 10, 50), stuck.LastAsked, now, true);
                    stuck.Tick(now, true);
                }
                if (stuck.Stuck == late) throw new Exception("Stuck detection mishandled " + (late ? "late replies." : "dropped moves."));
                stuck.Report(stuck.LastAsked, stuck.LastAsked, 4, false);
                if (stuck.Stuck) throw new Exception("A later confirmation did not clear stuck movement.");
            }
            var held = new MoveReports();
            for (int i = 0; i < 80; i++)
            {
                double now = i * 0.05;
                held.Asked(new PixelPoint(100, 500 + i * 10), now, true);
                held.Report(new PixelPoint(100, 600), held.LastAsked, now + 0.01, true);
                held.Tick(now + 0.02, true);
            }
            if (held.Stuck) throw new Exception("A desktop that answers every move from inside its work area tripped stuck detection.");
            checks.Add("PASS dropped moves trip the stuck detector, 0.6 second replies and clamped answers do not, and later replies clear it");
            var expired = new MoveReports();
            for (int i = 0; i < 3; i++) expired.Asked(new PixelPoint(50 + i * 20, 100), i * 0.05, true);
            expired.Tick(1.2, true);
            expired.Report(expired.LastAsked, new PixelPoint(10, 10), 4, false);
            if (expired.Stuck) throw new Exception("A confirmation after history expiry left movement stuck.");
            var history = new MoveReports();
            for (int i = 0; i < 100; i++) history.Asked(new PixelPoint(i * 10, 20), i * 0.01);
            if (history.Count != 100) throw new Exception("Move history kept a count cap.");
            history.Tick(3, false);
            if (history.Count != 0 || history.Stuck) throw new Exception("Old history or moves outside the real window tripped detection.");
            checks.Add("PASS history is timed without a count cap, seam moves do not trip detection, and expired confirmations clear stuck movement");
            var cancel = new MoveReports();
            cancel.Asked(new PixelPoint(100, 200), 0);
            cancel.Report(new PixelPoint(40, 50), new PixelPoint(100, 200), 0.1, true);
            cancel.Report(new PixelPoint(100, 200), new PixelPoint(100, 200), 0.2, false);
            if (cancel.Tick(1, false).HasValue) throw new Exception("A confirmation left a pending mismatch.");
            cancel.Report(new PixelPoint(40, 50), new PixelPoint(100, 200), 1.1, false);
            if (cancel.Report(new PixelPoint(40, 50), new PixelPoint(100, 200), 1.2, false) != new PixelPoint(40, 50)) throw new Exception("A repeated still mismatch was not taken.");
            checks.Add("PASS confirmations cancel pending mismatches and repeated still reports can correct the anchor");
            int reads = 0;
            using (var x11 = new X11Desk((window, name) =>
            {
                reads++;
                if (name == "_NET_CLIENT_LIST_STACKING") return new long[] { 71, 72, 73 };
                if (name == "_NET_WM_PID") return new long[] { Environment.ProcessId };
                if (name == "_NET_CURRENT_DESKTOP") return new long[] { 0 };
                throw new Exception("An own window reached geometry or other X properties.");
            }))
                if (x11.Windows(Array.Empty<long>()).Count != 0 || reads != 5) throw new Exception("Own X11 windows were not skipped before geometry.");
            checks.Add("PASS X11 skips every own process window before geometry");
            var limit = new MoveReports();
            int logged = 0;
            limit.Limited = (_, _) => logged++;
            for (int i = 0; i < 5; i++) { limit.Report(new PixelPoint(20 + i * 10, 20), default, 1 + i * 0.1, false); limit.Report(new PixelPoint(20 + i * 10, 20), default, 1.01 + i * 0.1, false); }
            if (limit.Takes != 3 || logged != 1) throw new Exception("The report rate limit failed.");
            if (limit.Tick(3.5, false).HasValue) throw new Exception("An ignored mismatch was replayed after the rate limit.");
            limit.Report(new PixelPoint(300, 20), default, 3.6, false);
            if (limit.Report(new PixelPoint(300, 20), default, 3.7, false) != new PixelPoint(300, 20)) throw new Exception("Corrections did not resume after the rate limit.");
            checks.Add("PASS at most three corrections land in two seconds");
        }
    }

    internal sealed partial class Pet
    {
        private static bool moveLimitLogged, stuckLogged, unstuckLogged;
        private PointerPressedEventArgs desktopPress;
        private Action<PointerPressedEventArgs> MoveDrag;
        private bool desktopDrag, desktopReleased;
        private double desktopMovedAt;
        private PixelPoint? desktopReported;
        private bool LinuxMoves { get { return OperatingSystem.IsLinux() && Desk is not HyprlandDesk; } }
        private bool MoveStuck { get { return LinuxMoves && asked.Stuck; } }
        private bool DesktopDrags { get { return LinuxMoves && (Desk is NiriDesk || MoveStuck); } }
        private bool MoveBusy { get { return falling || walking || riding || pressed || dragging || outro != null; } }
        private PixelPoint WindowAt { get { return new PixelPoint(anchor.X + Px(FrameX), anchor.Y + Px(FrameY)); } }

        private void RecordMove(PixelPoint at)
        {
            if (!LinuxMoves) { if (asked.Count >= 8) asked.RemoveAt(0); asked.Add(at); return; }
            if (asked.Limited == null) asked.Limited = (from, to) =>
            {
                if (moveLimitLogged) return;
                moveLimitLogged = true;
                Brain.Log("move", -1, "Position corrections paused. Asked " + from + ", reported " + to + ", desk " + Desk.Name + ", scale " + DesktopScaling + ".");
            };
            if (asked.StuckChanged == null) asked.StuckChanged = stuck =>
            {
                if (stuck ? !stuckLogged : !unstuckLogged)
                {
                    if (stuck) stuckLogged = true; else unstuckLogged = true;
                    Brain.Log("move", -1, (stuck ? "Window moves did not land. " : "Window moves confirmed again. ") + "Desk " + Desk.Name + ", asked " + asked.LastAsked + ", reported " + asked.Reported + ", desktop " + Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") + ", session " + Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") + ".");
                }
                if (stuck) { TakePosition(Position); StayPut(); }
                else if (seat == null && !pressed && !dragging && !desktopDrag) Fall();
            };
            bool real = MoveTo == null && opened && IsVisible && (Desk is not NiriDesk niri || niri.Desktop.Failed(this)) && InsideWork(at);
            if (real) asked.Observe(Position);
            asked.Asked(at, Now, real);
        }

        // A desktop can quietly keep a window off its panels and past a screen's edge, and a refused move that leaves her where
        // she was sends no report. So only a move that keeps her whole window inside a work area counts towards stuck.
        private bool InsideWork(PixelPoint at)
        {
            PixelRect box = new PixelRect(at, PixelSize.FromSize(Bounds.Size, DesktopScaling));
            foreach (Screen screen in Screens.All) if (X11Safety.Work(screen).Contains(box)) return true;
            return false;
        }

        private void TakePosition(PixelPoint? at)
        {
            if (at.HasValue) anchor = new PixelPoint(at.Value.X - Px(FrameX), at.Value.Y - Px(FrameY));
        }

        private void TickMoveReports(double now)
        {
            if (!LinuxMoves) return;
            if (desktopDrag && now - desktopMovedAt >= 5) EndDesktopDrag();
            TakePosition(asked.Tick(now, MoveBusy));
            if (MoveStuck && (falling || walking || riding)) StayPut();
        }

        internal void DesktopPosition(PixelPoint at, bool own = false)
        {
            if (!LinuxMoves) return;
            bool changed = desktopReported != at;
            desktopReported = at;
            if (desktopReleased && own && asked.Expects(at, Now)) desktopReleased = false;
            if (desktopReleased && changed && !own) StartDesktopHold();
            if (desktopDrag)
            {
                if (changed) desktopMovedAt = Now;
                asked.Report(at, WindowAt, Now, true);
                TakePosition(at);
            }
            else FollowPosition(at);
        }

        private void StayPut()
        {
            ClearMoves();
            walking = falling = hanging = dropAfter = false;
            seat = fallTo = null;
            if (outro == null) Play(idle);
        }

        private void StartDesktopHold()
        {
            NativeSurface.CancelMoves(this);
            if (!dragging) Grab();
            desktopDrag = true; desktopMovedAt = Now;
            pressed = true; dragPending = false;
        }

        private void StartDesktopDrag()
        {
            StartDesktopHold();
            desktopReleased = false;
            if (MoveDrag != null) MoveDrag(desktopPress);
            else if (desktopPress != null) BeginMoveDrag(desktopPress);
        }

        private bool DesktopPointer(PointerEventArgs e)
        {
            if (!desktopDrag) return false;
            Point at = e.GetPosition(this);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && at.X >= 0 && at.Y >= 0 && at.X < Bounds.Width && at.Y < Bounds.Height) EndDesktopDrag(false);
            return true;
        }

        protected override void OnPointerEntered(PointerEventArgs e)
        {
            base.OnPointerEntered(e);
            Program.Guard(() => DesktopPointer(e));
        }

        private void EndDesktopDrag(bool watch = true)
        {
            desktopDrag = false; desktopReleased = watch;
            pressed = dragging = dragPending = false;
            IPointer held = holder; holder = null;
            if (held != null && held.Captured == this) held.Capture(null);
            desktopPress = null;
            if (MoveStuck) { StayPut(); Save(); }
            else { Fall(); Save(); }
        }
    }
}
