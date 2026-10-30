using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace CapriDesktop
{
    // Watches the game in front for moments worth reacting to, with no brain: a burst of its sound, a flash, a red screen edge, a
    // health bar dropping. Her comments and her HUD react to them, and a hit makes a game she's playing herself look up.
    internal sealed partial class Companion
    {
        private const int WatchWidth = 240, WatchHeight = 135;
        private Thread gameWatch;

        private void WatchGames()
        {
            if (gameWatch != null) return;
            gameWatch = new Thread(GameWatching) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "Game watch" };
            gameWatch.Start();
        }

        // What each thing GameSense notices means to her comments.
        internal static string WatchKind(string noticed)
        {
            return noticed == "hurt" || noticed == "bar_drop" ? "hit" : noticed == "cut" ? "scene" : noticed;
        }

        private void GameWatching()
        {
            GameSense sense = new GameSense();
            GameEars ears = null;
            FrameGrab grab = null;
            Rectangle watched = Rectangle.Empty;
            int listening = 0;
            byte[] before = null, after = null;
            Stopwatch watch = Stopwatch.StartNew();
            double kept = 0, checkedFront = -1;
            bool live = false;
            sense.Noticed += delegate(string kind, double strength)
            {
                string moment = WatchKind(kind);
                if (moment == "hit")
                {
                    if (docked) GameHud.Mood(HudMood.Hit);
                    Job running = job;
                    if (running != null) running.Hurt();
                }
                Commentary.Trigger(moment, null, before, after);
            };
            try
            {
                while (!quitting)
                {
                    if (watch.Elapsed.TotalSeconds - checkedFront >= 1)
                    {
                        checkedFront = watch.Elapsed.TotalSeconds;
                        // Gaming Mode is them asking her to watch, so she reacts at least when something happens.
                        Commentary.Pace = Math.Max(CommentPace, docked ? Commentary.Events : Commentary.Off);
                        Sight front = Commentary.Pace != Commentary.Off || job != null ? (GameAhead() != null ? Computer.Glimpse() : null) : null;
                        live = front != null && front.Window != IntPtr.Zero;
                        if (live)
                        {
                            int owner;
                            Native.GetWindowThreadProcessId(front.Window, out owner);
                            if (owner != listening)
                            {
                                if (ears != null) ears.Dispose();
                                ears = new GameEars(owner);
                                ears.Loud += delegate(double level) { Commentary.Trigger("sound", null, before, after); };
                                ears.Start();
                                listening = owner;
                            }
                            if (clips.Enabled)
                            {
                                if (grab != null) { grab.Dispose(); grab = null; watched = Rectangle.Empty; }
                            }
                            else if (grab == null || front.Bounds != watched)
                            {
                                if (grab != null) grab.Dispose();
                                grab = new FrameGrab();
                                grab.Open(front.Bounds);
                                watched = front.Bounds;
                                sense.Reset();
                            }
                        }
                        else
                        {
                            if (ears != null) { ears.Dispose(); ears = null; listening = 0; }
                            if (grab != null) { grab.Dispose(); grab = null; watched = Rectangle.Empty; }
                            sense.Reset();
                        }
                    }
                    if (!live)
                    {
                        Thread.Sleep(1000);
                        continue;
                    }
                    Bitmap seen = grab == null ? clips.Glimpse() : grab.Next(120) ? grab.Copy(480) : null;
                    if (seen != null)
                        using (Bitmap frame = seen)
                        {
                            byte[] pixels = GameSense.Bgra(frame, WatchWidth, WatchHeight);
                            if (watch.Elapsed.TotalSeconds - kept >= 1)
                            {
                                kept = watch.Elapsed.TotalSeconds;
                                before = after;
                                after = Jpeg(frame, 480);
                            }
                            if (pixels != null) sense.Feed(pixels, WatchWidth, WatchHeight, watch.Elapsed.TotalSeconds);
                        }
                    Thread.Sleep(100);
                }
            }
            catch (Exception error) { Brain.Log("game watch", -1, error.GetType().Name + ": " + error.Message); }
            finally
            {
                if (ears != null) ears.Dispose();
                if (grab != null) grab.Dispose();
            }
        }
    }
}
