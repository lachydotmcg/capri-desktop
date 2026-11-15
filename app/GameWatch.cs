using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;

namespace CapriDesktop
{
    // Watches the game in front for moments worth reacting to, with no brain: a burst of its sound, a flash, a red screen edge, a
    // health bar dropping. Her live reactions, her comments and her HUD react to them, and a hit makes a game she's playing
    // herself look up.
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

        internal enum WatchFrame { None, Grab, Clips }

        internal static WatchFrame WatchCapture(GameCapture frames, bool on, bool clipping, IntPtr window, out Rectangle area, Func<IntPtr, Rectangle> look = null, Func<bool> full = null)
        {
            area = on && window != IntPtr.Zero ? (look ?? GameFinder.Frame)(window) : Rectangle.Empty;
            bool ready = frames.Update(on && !clipping, window, area, on && window != IntPtr.Zero && (full ?? Native.Exclusive)());
            if (!on || window == IntPtr.Zero || frames.Foreground != window) return WatchFrame.None;
            return ready ? WatchFrame.Grab : WatchFrame.Clips;
        }

        private void GameWatching()
        {
            GameSense sense = new GameSense();
            GameEars ears = null;
            GameCapture frames = new GameCapture("watch");
            Rectangle covers = Rectangle.Empty;
            int listening = 0, heard = 0;
            IntPtr watchedWindow = IntPtr.Zero;
            byte[] before = null, after = null;
            Bitmap current = null;
            Stopwatch watch = Stopwatch.StartNew();
            double kept = 0, checkedFront = -1, bannerAt = -10;
            bool live = false;
            // Each moment goes to her reactions and comments. While her reactions run, a moment also gets a look, at most every
            // 1.5 s, for the big words a game shows when they win, lose, die or get a kill, read off the watcher on a worker so
            // the frames keep coming (F67).
            Action<string> noticed = delegate(string kind)
            {
                byte[] was = before, now = after;
                Bitmap middle = null;
                if (current != null && Reactions.Running && watch.Elapsed.TotalSeconds - bannerAt >= 1.5 && Ocr.Ready)
                {
                    bannerAt = watch.Elapsed.TotalSeconds;
                    middle = Banners.Middle(current);
                }
                if (middle == null) { Commentary.Moment(kind, null, was, now); return; }
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string said = null, read = null;
                    try { using (middle) read = Banners.Read(middle, 500, out said); }
                    catch (Exception) { read = null; }
                    if (read != null && Banners.Fresh(read, Environment.TickCount)) Commentary.Moment(read, "The screen says " + said, was, now);
                    else Commentary.Moment(kind, null, was, now);
                });
            };
            sense.Noticed += delegate(string kind, double strength)
            {
                if (watchedWindow == IntPtr.Zero || Native.GetForegroundWindow() != watchedWindow) return;
                if (kind == "cut" || kind == "dark") Computer.GameReset(watchedWindow);
                string moment = WatchKind(kind);
                if (moment == "hit")
                {
                    if (docked) GameHud.Mood(HudMood.Hit);
                    Job running = job;
                    if (running != null) running.Hurt();
                }
                noticed(moment);
            };
            string failedWith = null;
            try
            {
                while (!quitting)
                {
                    try
                    {
                        bool watching = GamePace != Commentary.Off || job != null;
                        IFrameGrab previous = frames.Grab;
                        if (watch.Elapsed.TotalSeconds - checkedFront >= 1)
                        {
                            checkedFront = watch.Elapsed.TotalSeconds;
                            Commentary.Pace = GamePace;
                            Sight front = Commentary.Pace != Commentary.Off || job != null ? (GameAhead() != null ? Computer.Glimpse() : null) : null;
                            Computer.NoteGame(front);
                            int owner = 0;
                            if (front != null && front.Window != IntPtr.Zero) Native.GetWindowThreadProcessId(front.Window, out owner);
                            live = owner > 0;
                            if (live)
                            {
                                if (watchedWindow != front.Window) { before = after = null; sense.Reset(); }
                                watchedWindow = front.Window;
                                covers = front.Bounds;
                                ReactionGame = front.Bounds;
                                if (owner != listening)
                                {
                                    before = after = null;
                                    sense.Reset();
                                    if (ears != null) ears.Dispose();
                                    ears = new GameEars(owner);
                                    int soundOwner = owner;
                                    // A burst of sound is taken up with the next frame, on this thread, so it can look for a banner too.
                                    ears.Loud += delegate(double level) { if (listening == soundOwner && watchedWindow != IntPtr.Zero && Native.GetForegroundWindow() == watchedWindow) Interlocked.Exchange(ref heard, 1); };
                                    ears.Start();
                                    listening = owner;
                                }
                            }
                            else
                            {
                                watchedWindow = IntPtr.Zero;
                                covers = Rectangle.Empty;
                                ReactionGame = Rectangle.Empty;
                                before = after = null;
                                Interlocked.Exchange(ref heard, 0);
                                if (ears != null) { ears.Dispose(); ears = null; listening = 0; }
                                sense.Reset();
                            }
                        }
                        Rectangle area;
                        WatchFrame source = WatchCapture(frames, watching, clips.Enabled, watchedWindow, out area);
                        IFrameGrab grab = frames.Grab;
                        if (previous != grab) { before = after = null; sense.Reset(); }
                        if (!live)
                        {
                            Thread.Sleep(1000);
                            continue;
                        }
                        if (source != WatchFrame.None) covers = area;
                        bool keepFrame = watch.Elapsed.TotalSeconds - kept >= 1;
                        Bitmap seen = source == WatchFrame.None ? null : source == WatchFrame.Clips ? clips.Glimpse() : grab.Next(120) ? grab.Copy(keepFrame ? 1280 : 480) : null;
                        if (seen != null)
                            using (Bitmap frame = seen)
                            {
                                // Her own feed never reaches her eyes, her senses or a banner read.
                                ReactionFeed shown = feed;
                                if (shown != null) PaintOut(frame, covers, shown.Area);
                                current = frame;
                                try
                                {
                                    byte[] pixels = GameSense.Bgra(frame, WatchWidth, WatchHeight);
                                    if (keepFrame)
                                    {
                                        kept = watch.Elapsed.TotalSeconds;
                                        before = after;
                                        after = Jpeg(frame, 1280);
                                    }
                                    if (pixels != null) sense.Feed(pixels, WatchWidth, WatchHeight, watch.Elapsed.TotalSeconds);
                                    if (Interlocked.Exchange(ref heard, 0) != 0) noticed("sound");
                                }
                                finally { current = null; }
                            }
                        else if (Interlocked.Exchange(ref heard, 0) != 0) noticed("sound");
                        Thread.Sleep(100);
                    }
                    catch (Exception error)
                    {
                        try { frames.Close("error"); } catch (Exception) { }
                        if (error.Message != failedWith) Brain.Log("game watch", -1, error.GetType().Name + " 0x" + error.HResult.ToString("X8"));
                        failedWith = error.Message;
                        watchedWindow = IntPtr.Zero;
                        covers = Rectangle.Empty;
                        ReactionGame = Rectangle.Empty;
                        current = null;
                        before = after = null;
                        live = false;
                        listening = 0;
                        if (ears != null) { try { ears.Dispose(); } catch (Exception) { } ears = null; }
                        sense.Reset();
                        Thread.Sleep(1000);
                        try { frames.Release(); } catch (Exception) { }
                    }
                }
            }
            finally
            {
                frames.Close("switched off");
                if (ears != null) ears.Dispose();
            }
        }
    }
}
