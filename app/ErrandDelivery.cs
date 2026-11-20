using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        private sealed class Errand
        {
            internal string Id, Task, Answer, Note, Suggest;
        }

        private const int ErrandQueueMost = 5;
        private const double ErrandGap = 20;
        private const double ErrandTravel = 40;

        internal Action<string> ErrandOpenNote;
        internal Action<string> ErrandWatch;

        private readonly List<Errand> errandQueue = new List<Errand>();
        private Errand errandNow;
        private ErrandNote errandNote;
        private double errandLast = -1000;
        private double errandGoUntil;
        private bool errandGoing, errandWalked, errandArrived;

        internal int ErrandsWaiting { get { return errandQueue.Count; } }

        internal void DeliverErrand(string id, string task, string answer, string notePath, string suggest)
        {
            Errand item = new Errand { Id = id ?? "", Task = task ?? "", Answer = answer ?? "", Note = notePath ?? "", Suggest = suggest ?? "" };
            if (item.Id.Length > 0) errandQueue.RemoveAll(delegate(Errand old) { return old.Id == item.Id; });
            errandQueue.Add(item);
            while (errandQueue.Count > ErrandQueueMost) errandQueue.RemoveAt(0);
        }

        private bool ErrandReady(double now, long quiet)
        {
            return errandQueue.Count > 0 && errandNow == null && errandNote == null && now - errandLast >= ErrandGap && PopFree(now, quiet);
        }

        private void CheckErrandDeliveries(double now, long quiet)
        {
            if (quitting || (hidden && errandNote != null)) { CloseErrandNote(); errandNow = null; errandGoing = false; }
            if (errandNote != null && errandNote.IsDisposed) errandNote = null;
            if (errandNote != null) { FollowErrandNote(); return; }
            if (errandNow != null)
            {
                if (!errandGoing) { errandNow = null; return; }
                bool there = (errandArrived || (errandWalked && !walking)) && outro == null;
                if (there || now >= errandGoUntil) ShowErrand(now);
                return;
            }
            if (!ErrandReady(now, quiet)) return;
            StartErrand(now);
        }

        private void StartErrand(double now)
        {
            errandNow = errandQueue[0];
            errandQueue.RemoveAt(0);
            errandLast = now;
            errandGoUntil = now + ErrandTravel;
            errandGoing = true;
            errandWalked = errandArrived = false;
            Point cursor = Cursor.Position;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            int size = preferences.Size;
            bool inReach = false;
            if (!hidden && preferences.Motion && !instant && IsHandleCreated && seat == null && !riding && !hanging && !onPanel && !falling && !lying && !walking && !dragging)
            {
                Point hip = Hip();
                inReach = Screen.FromPoint(hip).WorkingArea == area && Math.Abs(cursor.X - hip.X) <= size * 4;
            }
            if (inReach)
            {
                if (!WalkToCursor(cursor, area, now)) errandArrived = true;
                return;
            }
            bool wasHidden = hidden;
            Point spot = new Point(cursor.X - Width / 2 - (cursor.X > area.Left + area.Width / 2 ? 1 : -1) * (int)(size * 0.75f), area.Bottom - Height - 12);
            Action arrive = delegate
            {
                StopRide();
                seat = null;
                falling = false;
                walking = false;
                lying = false;
                facingLeft = cursor.X < spot.X + Width / 2;
                anchor = Within(spot, area, Body);
                ShowHidden(false);
                DropSoon();
                if (IsHandleCreated) Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
                errandArrived = true;
            };
            if (wasHidden) arrive();
            else GlitchOut(arrive, false);
        }

        private bool WalkToCursor(Point cursor, Rectangle area, double now)
        {
            int size = preferences.Size;
            Point hip = Hip();
            bool left = cursor.X < hip.X;
            if (left != facingLeft)
            {
                facingLeft = left;
                anchor = new Point(anchor.X + hip.X - Hip().X, anchor.Y);
            }
            int offset = (int)(size * 0.75f) * (cursor.X > area.Left + area.Width / 2 ? -1 : 1);
            int want = anchor.X + (cursor.X + offset - Hip().X);
            int from = area.Left - PaddingSize - (int)(size * Body.Left);
            int to = area.Right - PaddingSize - (int)Math.Ceiling(size * Body.Right);
            int target = Math.Max(from, Math.Min(want, to));
            if (Math.Abs(target - anchor.X) < Math.Max(4, size / 10)) return false;
            walking = true;
            walkTo = target;
            walkStep = now;
            walkCarry = 0;
            Play(walk);
            frames.Start();
            errandWalked = true;
            return true;
        }

        private void ShowErrand(double now)
        {
            Errand item = errandNow;
            errandGoing = false;
            if (walking) EndWalk();
            bool canOpen = false;
            try { canOpen = item.Note.Length > 0 && File.Exists(item.Note); }
            catch (Exception) { }
            bool canWatch = item.Suggest.Trim().Length > 0;
            string line = ErrandNote.First(item.Answer);
            if (line.Length > 0)
            {
                if (docked) Say(line);
                else Remark("", line, InChat || Hushed || FullScreen());
            }
            CloseErrandNote();
            ErrandNote note = new ErrandNote(item.Task, item.Answer, canOpen, canWatch);
            note.OpenNote = delegate { Action<string> go = ErrandOpenNote; if (go != null) go(item.Note); };
            note.Watch = delegate { Action<string> go = ErrandWatch; if (go != null) go(item.Suggest); };
            note.FormClosed += delegate { if (errandNote == note) { errandNote = null; errandNow = null; } };
            errandNote = note;
            Rectangle body = BodyOnScreen(anchor, Body);
            note.Arrive(body, Screen.FromRectangle(body).WorkingArea, !facingLeft);
        }

        private void FollowErrandNote()
        {
            if (errandNote == null || errandNote.IsDisposed) return;
            Rectangle body = BodyOnScreen(anchor, Body);
            errandNote.Follow(body, Screen.FromRectangle(body).WorkingArea, !facingLeft);
        }

        internal void CloseErrandNote()
        {
            ErrandNote note = errandNote;
            errandNote = null;
            if (note != null && !note.IsDisposed) note.Close();
        }

        private void ForgetErrands()
        {
            errandQueue.Clear();
            CloseErrandNote();
            errandGoing = false;
        }

        private void ErrandChecks(List<string> checks)
        {
            string longAnswer = "I found three good options for the trip and compared them by price, travel time and reviews, then wrote everything into the note with links so you can pick one without searching again. The cheapest is the night train but it takes much longer than the others and the seats are not great, the middle one is the best value overall.";
            foreach (float scale in new float[] { 1f, 1.5f, 2.25f })
            {
                using (ErrandNote both = new ErrandNote("compare train tickets for the long weekend away next month please", longAnswer, true, true, scale))
                {
                    Rectangle client = new Rectangle(Point.Empty, both.ClientSize);
                    if (both.Lines.Length != 4 || !both.Lines[3].EndsWith("…")) throw new Exception("A long errand answer did not wrap to four lines with an ellipsis at " + scale + ": " + both.Lines.Length);
                    if (both.Heading.Length > 40 || !both.Heading.StartsWith("Done: ")) throw new Exception("An errand note's heading was not trimmed: " + both.Heading);
                    if (!client.Contains(both.OpenBox) || !client.Contains(both.WatchBox) || !client.Contains(both.CloseBox) || both.OpenBox.IntersectsWith(both.WatchBox)) throw new Exception("An errand note's buttons did not lay out inside it at " + scale + ".");
                    bool opened = false, watched = false;
                    both.OpenNote = delegate { opened = true; };
                    both.Watch = delegate { watched = true; };
                    both.ClickWatch();
                    if (!watched || opened) throw new Exception("Keep an eye on it did not call its handler alone.");
                }
                using (ErrandNote plain = new ErrandNote("hi", "All done, nothing to add.", false, false, scale))
                {
                    if (plain.HasOpen || plain.HasWatch || !plain.OpenBox.IsEmpty || !plain.WatchBox.IsEmpty || plain.Lines.Length != 1) throw new Exception("An errand note without a note or a suggestion still showed buttons.");
                    bool opened = false;
                    plain.OpenNote = delegate { opened = true; };
                    plain.ClickOpen();
                    if (opened) throw new Exception("An errand note without a note file still opened one.");
                }
                using (ErrandNote only = new ErrandNote("", "ok", true, false, scale))
                    if (!only.HasOpen || only.HasWatch || only.OpenBox.IsEmpty || !only.WatchBox.IsEmpty || only.Heading != "All done") throw new Exception("An errand note with only a note showed the wrong buttons.");
            }
            Rectangle screen = new Rectangle(0, 0, 1000, 800);
            Size card = new Size(320, 160);
            foreach (Rectangle body in new Rectangle[] { new Rectangle(900, 600, 100, 200), new Rectangle(0, 700, 100, 100), new Rectangle(400, 0, 100, 100), new Rectangle(450, 300, 100, 400) })
                foreach (bool right in new bool[] { true, false })
                    if (!screen.Contains(new Rectangle(ErrandNote.Spot(card, body, screen, right, 10), card))) throw new Exception("An errand note left the working area beside " + body + ".");
            if (ErrandNote.First("I found it. Then more words here.") != "I found it." || ErrandNote.First("short") != "short") throw new Exception("An errand's first sentence was cut wrong.");

            List<Errand> queueWas = new List<Errand>(errandQueue);
            double lastWas = errandLast;
            Errand nowWas = errandNow;
            ErrandNote noteWas = errandNote;
            try
            {
                errandQueue.Clear();
                errandNow = null;
                errandNote = null;
                foreach (string id in new string[] { "a", "b", "c", "d", "e", "f" }) DeliverErrand(id, "task " + id, "answer " + id, "", "");
                if (errandQueue.Count != 5 || errandQueue[0].Id != "b" || errandQueue[4].Id != "f") throw new Exception("The errand queue did not keep its five newest.");
                DeliverErrand("c", "task c", "newer answer", "", "watch");
                if (errandQueue.Count != 5 || errandQueue.FindAll(delegate(Errand old) { return old.Id == "c"; }).Count != 1 || errandQueue[4].Answer != "newer answer") throw new Exception("An errand with the same id did not replace the old one.");
                errandLast = -1000;
                bool free = PopFree(5000, 0);
                if (ErrandReady(5000, 0) != free) throw new Exception("An errand did not wait for the same quiet as her pop-ins.");
                if (ErrandReady(5000, 61000) || ErrandReady(5000, 130000)) throw new Exception("An errand was delivered to someone who was away.");
                errandLast = 4990;
                if (ErrandReady(5000, 0)) throw new Exception("Two errands were delivered less than twenty seconds apart.");
                errandLast = -1000;
                errandNow = errandQueue[0];
                if (ErrandReady(5000, 0)) throw new Exception("A second errand started while one was being delivered.");
                errandNow = null;
                ForgetErrands();
                if (errandQueue.Count != 0 || errandNote != null || ErrandReady(5000, 0)) throw new Exception("Turning her own computer off left errands to deliver.");
                checks.Add("PASS Errand notes: a long answer wraps to four lines inside the card at three scales, buttons show only when there is a note or a suggestion, the card stays on the working area, the queue keeps five and replaces by id, delivery waits while they are away, and turning her own computer off drops the ones still waiting");
            }
            finally
            {
                errandQueue.Clear();
                errandQueue.AddRange(queueWas);
                errandLast = lastWas;
                errandNow = nowWas;
                errandNote = noteWas;
            }
        }
    }
}
