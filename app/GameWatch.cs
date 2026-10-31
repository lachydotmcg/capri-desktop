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
            IntPtr watchedWindow = IntPtr.Zero;
            byte[] before = null, after = null;
            Stopwatch watch = Stopwatch.StartNew();
            double kept = 0, checkedFront = -1;
            bool live = false;
            sense.Noticed += delegate(string kind, double strength)
            {
                if (watchedWindow == IntPtr.Zero || Native.GetForegroundWindow() != watchedWindow) return;
                string moment = WatchKind(kind);
                if (moment == "hit")
                {
                    if (docked) GameHud.Mood(HudMood.Hit);
                    Job running = job;
                    if (running != null) running.Hurt();
                }
                Commentary.Trigger(moment, null, before, after);
            };
            string failedWith = null;
            try
            {
                while (!quitting)
                {
                    try
                    {
                        if (watch.Elapsed.TotalSeconds - checkedFront >= 1)
                        {
                            checkedFront = watch.Elapsed.TotalSeconds;
                            Commentary.Pace = GamePace;
                            Sight front = Commentary.Pace != Commentary.Off || job != null ? (GameAhead() != null ? Computer.Glimpse() : null) : null;
                            int owner = 0;
                            if (front != null && front.Window != IntPtr.Zero) Native.GetWindowThreadProcessId(front.Window, out owner);
                            live = owner > 0;
                            if (live)
                            {
                                if (watchedWindow != front.Window) { before = after = null; sense.Reset(); }
                                watchedWindow = front.Window;
                                if (owner != listening)
                                {
                                    before = after = null;
                                    sense.Reset();
                                    if (ears != null) ears.Dispose();
                                    ears = new GameEars(owner);
                                    int soundOwner = owner;
                                    ears.Loud += delegate(double level) { if (listening == soundOwner && watchedWindow != IntPtr.Zero && Native.GetForegroundWindow() == watchedWindow) Commentary.Trigger("sound", null, before, after); };
                                    ears.Start();
                                    listening = owner;
                                }
                                if (clips.Enabled)
                                {
                                    if (grab != null) { grab.Dispose(); grab = null; watched = Rectangle.Empty; }
                                }
                                else if (grab == null || front.Bounds != watched)
                                {
                                    before = after = null;
                                    if (grab != null) grab.Dispose();
                                    grab = new FrameGrab();
                                    grab.Open(front.Bounds);
                                    watched = front.Bounds;
                                    sense.Reset();
                                }
                            }
                            else
                            {
                                watchedWindow = IntPtr.Zero;
                                before = after = null;
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
                        bool keepFrame = watch.Elapsed.TotalSeconds - kept >= 1;
                        Bitmap seen = grab == null ? clips.Glimpse() : grab.Next(120) ? grab.Copy(keepFrame ? 1280 : 480) : null;
                        if (seen != null)
                            using (Bitmap frame = seen)
                            {
                                byte[] pixels = GameSense.Bgra(frame, WatchWidth, WatchHeight);
                                if (keepFrame)
                                {
                                    kept = watch.Elapsed.TotalSeconds;
                                    before = after;
                                    after = Jpeg(frame, 1280);
                                }
                                if (pixels != null) sense.Feed(pixels, WatchWidth, WatchHeight, watch.Elapsed.TotalSeconds);
                            }
                        Thread.Sleep(100);
                    }
                    catch (Exception error)
                    {
                        if (error.Message != failedWith) Brain.Log("game watch", -1, error.GetType().Name + ": " + error.Message);
                        failedWith = error.Message;
                        watchedWindow = IntPtr.Zero;
                        before = after = null;
                        live = false;
                        listening = 0;
                        watched = Rectangle.Empty;
                        if (ears != null) { try { ears.Dispose(); } catch (Exception) { } ears = null; }
                        if (grab != null) { try { grab.Dispose(); } catch (Exception) { } grab = null; }
                        sense.Reset();
                        Thread.Sleep(1000);
                    }
                }
            }
            finally
            {
                if (ears != null) ears.Dispose();
                if (grab != null) grab.Dispose();
            }
        }
    }
}
