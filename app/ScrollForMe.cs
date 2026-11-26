using System;
using System.Threading;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        internal static Action<IntPtr> ScrollPress = delegate(IntPtr window)
        {
            if (window != IntPtr.Zero && Hands.Foreground() == window) Hands.Chord(new byte[] { 0x28 });
        };

        internal bool ScrollingForThem { get { return watchAlong != null && watchAlong.Scrolling; } }

        private DoomSite ScrollSite(Sight front)
        {
            if (front == null || front.Window == IntPtr.Zero || front.Window != Hands.Foreground()) return null;
            DoomSite site = doomscroll.Match(front.Program, front.Title);
            return site != null && site.Name != "X" && site.Name != "Reddit" ? site : null;
        }

        private string ScrollForThem()
        {
            if (watchAlong != null && watchAlong.Scrolling) return "Already scrolling. Say stop scrolling when you've had enough.";
            Sight front = Computer.Glimpse();
            if (ScrollSite(front) == null) return WatchAlong.ScrollNoFeed;
            if (watchAlong != null) EndWatchAlong();
            string started = WatchWith("start");
            WatchAlong watching = watchAlong;
            if (watching == null) return started;
            double now = clock.Elapsed.TotalSeconds;
            watching.Scrolling = true;
            watching.Front = front;
            watching.PictureAt = now;
            watching.AskAt = now + WatchAlong.ScrollFirst;
            watching.ScrollAt = now + WatchAlong.ScrollFewest;
            watching.ScrolledAt = now;
            doomscroll.Reset();
            EndDoomSulk();
            if (!testing) Animate("glitch");
            string missing = started.StartsWith("I'm here with you. ", StringComparison.Ordinal) ? " " + started.Substring("I'm here with you. ".Length) : "";
            return WatchAlong.ScrollStart + missing + " Say stop scrolling when you're done.";
        }

        private bool ScrollFeed(WatchAlong watching, double now)
        {
            if (watching.ScrollPressing) return true;
            bool reacted = !watching.Thinking && watching.Pending == null && watching.AskAt - WatchAlong.AskSeconds >= watching.ScrolledAt;
            if (now < watching.ScrollAt || !reacted && now < watching.ScrollAt + WatchAlong.ScrollMost - WatchAlong.ScrollFewest) return false;
            Sight front = Computer.Glimpse();
            if (ScrollSite(front) == null)
            {
                if (watching.ScrollLostAt < 0) watching.ScrollLostAt = now;
                if (!watching.ScrollLostSaid && now - watching.ScrollLostAt >= WatchAlong.ScrollLost) { watching.ScrollLostSaid = true; watching.Notice = WatchAlong.ScrollFeedGone; }
                return false;
            }
            watching.ScrollLostAt = -1;
            watching.ScrollLostSaid = false;
            watching.ScrollPressing = true;
            IntPtr window = front.Window;
            Action<IntPtr> press = ScrollPress;
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { press(window); }
                catch (Exception) { }
                Post(delegate
                {
                    watching.ScrollPressing = false;
                    if (watchAlong != watching) return;
                    double at = clock.Elapsed.TotalSeconds;
                    watching.Scrolls++;
                    watching.MovedAt = watching.ScrolledAt = at;
                    watching.Print = null;
                    watching.Picture = null;
                    watching.Front = Computer.Glimpse();
                    watching.PictureAt = at + WatchAlong.ScrollPicture;
                    watching.AskAt = at + WatchAlong.ScrollAskAfter;
                    watching.ScrollAt = at + WatchAlong.ScrollFewest + random.Next(5);
                    if (watching.Scrolls % WatchAlong.ScrollEvery == 0)
                    {
                        int milestone = Math.Min(WatchAlong.ScrollMilestones.Length, watching.Scrolls / WatchAlong.ScrollEvery) - 1;
                        watching.Notice = WatchAlong.ScrollMilestones[milestone];
                    }
                });
            });
            return true;
        }
    }
}
