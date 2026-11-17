using System;
using System.Collections.Generic;
using Avalonia;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        internal const double FlingSpan = 0.08, FlingFrom = 1300, FlingHard = 2500;
        private readonly List<KeyValuePair<double, PixelPoint>> dragTrail = new List<KeyValuePair<double, PixelPoint>>();
        private readonly List<double> throwsAt = new List<double>();
        private double sulkSince = -1, sulkBothered, patNoAt = -10;
        private bool? sulkLeft;
        private int petMood;
        internal int Flings, PatsRefused;
        internal string LastYelp;

        internal bool Sulking { get { return sulkSince >= 0; } }
        internal int ThrowsCounted { get { return throwsAt.Count; } }
        internal bool? SulkLeft { get { return sulkLeft; } }

        internal void NoteDrag(PixelPoint to, double now)
        {
            dragTrail.Add(new KeyValuePair<double, PixelPoint>(now, to));
            while (dragTrail.Count > 2 && dragTrail[1].Key < now - 0.5) dragTrail.RemoveAt(0);
        }

        internal double FlingSpeed(double now)
        {
            if (dragTrail.Count < 2) return 0;
            KeyValuePair<double, PixelPoint> last = dragTrail[dragTrail.Count - 1];
            if (now - last.Key > FlingSpan) return 0;
            KeyValuePair<double, PixelPoint> first = last;
            foreach (KeyValuePair<double, PixelPoint> step in dragTrail) if (step.Key >= now - FlingSpan) { first = step; break; }
            double span = Math.Max(now - first.Key, 1.0 / 120);
            double dx = last.Value.X - first.Value.X, dy = last.Value.Y - first.Value.Y;
            return Math.Sqrt(dx * dx + dy * dy) / span / DesktopScaling;
        }

        internal void LetGoFast(bool here) { if (Program.Smoke == null) LetGoFast(here, Now); else dragTrail.Clear(); }

        internal void LetGoFast(bool here, double now)
        {
            double fast = here ? 0 : FlingSpeed(now);
            dragTrail.Clear();
            if (fast >= FlingFrom) Flung(fast, now);
        }

        internal void Flung(double fast, double now)
        {
            Flings++;
            if (Sulking) { sulkBothered = now; return; }
            int count = Sulks.Thrown(throwsAt, now);
            if (count >= Sulks.Throws) { Sulk(now); Yelp(Lines.Pick(Sulks.SulkLines)); return; }
            if (count == 2) Yelp(Lines.Pick(Sulks.WarnLines));
            else if (fast >= FlingHard) Yelp(Lines.Pick(Sulks.ThrowLines));
        }

        private void Yelp(string line, string heard = "throws you")
        {
            if (away || quitting || !Talk.Quiet || Talk.Typing || JobActive) return;
            LastYelp = line;
            Reply(++asking, heard, line);
        }

        internal void Sulk(double now)
        {
            sulkSince = now;
            sulkBothered = now;
            patNoAt = -10;
            sulkLeft = null;
            throwsAt.Clear();
        }

        internal void Forgive()
        {
            throwsAt.Clear();
            sulkSince = -1;
            sulkLeft = null;
        }

        internal void CheckSulk(double now)
        {
            if (!Sulking) return;
            if (now - sulkBothered >= Sulks.Alone)
            {
                Forgive();
                if (!Hushed) Reply(++asking, "(sulk)", Lines.Pick(Sulks.AloneLines));
                return;
            }
            if (seat != null || lying) { sulkLeft = null; return; }
            PixelPoint? cursor = ReadMoveMouse();
            if (cursor == null) return;
            sulkLeft = Sulks.Away(Hip.X, cursor.Value.X, Px(body.Size / 2), sulkLeft ?? FacingLeft);
        }

        internal void Pat() { Petted(); }

        private bool PatSulking(double now)
        {
            petMood = 0;
            if (!Sulking) return false;
            if (!Sulks.PatTooSoon(sulkSince, now)) { Forgive(); petMood = 1; return false; }
            sulkBothered = now;
            PatsRefused++;
            Play(seat != null ? perchGlitch : glitch);
            if (now - patNoAt >= 3) { patNoAt = now; Yelp(Lines.Pick(Sulks.PatNoLines), "pets you"); }
            return true;
        }

        private string PatMoodLine()
        {
            int mood = petMood;
            petMood = 0;
            return mood == 1 ? Lines.Pick(Sulks.PatPeaceLines) : null;
        }

        private bool SorryHeard(string input)
        {
            if (!Sulking || !Sulks.Apology(input)) return false;
            Forgive();
            return Sulks.OnlyApology(input);
        }
    }
}
