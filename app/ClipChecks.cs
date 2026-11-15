using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal partial class Companion
    {
        private static void SmokeCaptureWait(List<string> checks)
        {
            long tick = 0;
            CaptureWait wait = new CaptureWait(delegate { return tick; });
            IntPtr window = new IntPtr(7);
            Rectangle bounds = new Rectangle(10, 20, 640, 480);
            for (int turn = 0; turn < 100; turn++)
                if (wait.Ready(window, bounds, false)) throw new Exception("A stopped capture clock became ready.");
            tick = Stopwatch.Frequency - 1;
            if (wait.Ready(window, bounds, false)) throw new Exception("Capture became ready before a full second.");
            tick++;
            if (!wait.Ready(window, bounds, false) || !wait.Ready(window, bounds, false)) throw new Exception("A steady window did not stay ready after a full second.");
            Action<IntPtr, Rectangle, bool> restart = delegate(IntPtr next, Rectangle area, bool full)
            {
                if (wait.Ready(next, area, full)) throw new Exception("A changed capture did not restart its steady wait.");
                tick += Stopwatch.Frequency - 1;
                if (wait.Ready(next, area, full)) throw new Exception("A changed capture waited less than a full second.");
                tick++;
                if (!wait.Ready(next, area, full)) throw new Exception("A changed capture never became ready.");
            };
            window = new IntPtr(8);
            restart(window, bounds, false);
            bounds.Offset(15, -5);
            restart(window, bounds, false);
            bounds.Size = new Size(800, 600);
            restart(window, bounds, false);
            restart(window, bounds, true);
            restart(window, bounds, false);
            checks.Add("PASS capture waits a full monotonic second; a stopped clock stays waiting, and a new window, move, resize or exclusive change restarts the second");
            for (int turn = 0; turn < 10; turn++)
            {
                tick += Stopwatch.Frequency * 2;
                if (wait.Ready(IntPtr.Zero, bounds, false)) throw new Exception("A zero window became ready for capture.");
            }
            restart(window, bounds, false);
            if (wait.Ready(window, Rectangle.Empty, false)) throw new Exception("Empty bounds became ready for capture.");
            tick += Stopwatch.Frequency * 2;
            if (wait.Ready(window, Rectangle.Empty, false)) throw new Exception("Empty bounds became ready after waiting.");
            restart(window, bounds, false);
            checks.Add("PASS a zero window or empty bounds never becomes ready, and returning to valid bounds waits a new full second");
        }

        private static void SmokeClipCapture(List<string> checks)
        {
            Func<GameCapture, bool, Sight, bool, bool> turn = delegate(GameCapture frames, bool on, Sight front, bool full)
            {
                Rectangle area;
                return ClipBuffer.CaptureTurn(frames, on, new GameSeen { Window = front.Window }, full, out area, delegate { return front.Bounds; }) != null;
            };
            FrameGrab.CheckGameCapture(checks, "clips", turn);
            FrameGrab.CheckCaptureQuiet(checks, "clips", turn);
            long inspectAt = 0;
            bool exclusive = false, answer = false;
            int queries = 0;
            Func<bool> query = delegate { queries++; return answer; };
            for (long at = 0; at < 20000000; at += 150000) ClipBuffer.CaptureLook(at, true, ref inspectAt, ref exclusive, query);
            if (queries != 8 || exclusive) throw new Exception("Clip capture queried exclusive state outside its look.");
            answer = true;
            if (ClipBuffer.CaptureLook(inspectAt - 1, true, ref inspectAt, ref exclusive, query) || exclusive || queries != 8) throw new Exception("Clip capture queried exclusive state before 250 ms.");
            if (!ClipBuffer.CaptureLook(inspectAt, true, ref inspectAt, ref exclusive, query) || !exclusive || queries != 9) throw new Exception("Clip capture missed its next exclusive sample.");
            ClipBuffer.CaptureLook(inspectAt, false, ref inspectAt, ref exclusive, query);
            if (queries != 9) throw new Exception("Disabled clip capture queried exclusive state.");
            checks.Add("PASS clip capture samples exclusive state only in its 250 ms look, including a changed answer and switching off");
            long tick = 0, soundAt = 1, nextAt = 2, imageAt = 3;
            IntPtr window = new IntPtr(7), foreground = window;
            Rectangle bounds = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            GameCapture capture = new GameCapture("clips", delegate { return tick; }, delegate { return new FrameGrab(delegate { return tick; }, delegate { return false; }, delegate { }, delegate { return false; }); }, delegate { return foreground; }, delegate { return false; });
            ClipWait wait = null;
            SoundTap tap = null;
            Action pause = delegate { ClipBuffer.PauseCapture(ref wait, ref tap, ref soundAt, ref nextAt, ref imageAt); };
            string logBefore = Brain.LogPath;
            string log = Path.Combine(Path.GetTempPath(), "capri-capture-sound-" + Guid.NewGuid().ToString("N") + ".log");
            using (MicCheck sound = new MicCheck())
            try
            {
                Brain.LogPath = log;
                Rectangle area;
                GameSeen front = new GameSeen { Window = window };
                Func<IntPtr, Rectangle> look = delegate { return bounds; };
                if (ClipBuffer.CaptureTurn(capture, true, front, false, out area, look, pause) != null) throw new Exception("Clip sound fixture skipped its steady wait.");
                tick += Stopwatch.Frequency;
                if (ClipBuffer.CaptureTurn(capture, true, front, false, out area, look, pause) == null) throw new Exception("Clip sound fixture never opened capture.");
                foreach (bool away in new[] { false, true })
                {
                    tap = new SoundTap("fake", sound, sound, 48000, 6, 32, true);
                    soundAt = nextAt = imageAt = 123;
                    if (away) foreground = new IntPtr(9);
                    else bounds.Offset(1, 0);
                    if (ClipBuffer.CaptureTurn(capture, true, front, false, out area, look, pause) != null || tap != null || wait != null || soundAt != 0 || nextAt != 0 || imageAt != 0) throw new Exception("A clip wait kept its sound tap or deadlines.");
                    if (sound.Stops != (away ? 2 : 1) || sound.Polls != 0) throw new Exception("A clip wait read stale sound or failed to stop its tap.");
                }
                checks.Add("PASS clip capture closes its fake sound tap and clears deadlines while waiting for steady bounds and while another window is in front, without reading stale sound");
            }
            finally
            {
                capture.Close("switched off");
                pause();
                Brain.LogPath = logBefore;
                if (File.Exists(log)) File.Delete(log);
            }
        }

        private void SmokeWatchCapture(List<string> checks)
        {
            Rectangle before = ReactionGame;
            Rectangle held = new Rectangle(13, 17, 19, 23);
            ReactionGame = held;
            Func<GameCapture, bool, Sight, bool, bool> turn = delegate(GameCapture frames, bool on, Sight front, bool full)
            {
                Rectangle area;
                int queries = 0;
                WatchFrame source = WatchCapture(frames, on, false, front.Window, out area, delegate { return front.Bounds; }, delegate { queries++; return full; });
                bool inFront = on && front.Window != IntPtr.Zero && frames.Foreground == front.Window;
                if (!inFront && source != WatchFrame.None) throw new Exception("The watcher chose a frame behind their Mainframe or shell.");
                if (inFront && source == WatchFrame.None) throw new Exception("The watcher lost its frame source in front.");
                if (queries != (on && front.Window != IntPtr.Zero ? 1 : 0)) throw new Exception("The watcher queried exclusive state more than once per turn.");
                if (ReactionGame != held) throw new Exception("A capture turn rewrote the game reaction area.");
                return source == WatchFrame.Grab;
            };
            try
            {
                FrameGrab.CheckGameCapture(checks, "watch", turn);
                FrameGrab.CheckCaptureQuiet(checks, "watch", turn);
                long tick = 0;
                IntPtr window = new IntPtr(7), foreground = window;
                GameCapture capture = new GameCapture("watch", delegate { return tick; }, delegate { throw new Exception("The watcher opened capture while clips supplied frames."); }, delegate { return foreground; }, delegate { return false; });
                Rectangle bounds = new Rectangle(0, 0, 8, 8), area;
                if (WatchCapture(capture, true, true, window, out area, delegate { return bounds; }, delegate { return false; }) != WatchFrame.Clips) throw new Exception("The watcher stopped sharing clip frames.");
                foreground = new IntPtr(9);
                if (WatchCapture(capture, true, true, window, out area, delegate { return bounds; }, delegate { return false; }) != WatchFrame.None) throw new Exception("The watcher shared clip frames behind their Mainframe.");
                capture.Close("switched off");
                checks.Add("PASS the watcher chooses no frame behind their Mainframe or shell, shares clip frames only in front, queries exclusive state once per turn and leaves the reaction area to its slower scan");
            }
            finally { ReactionGame = before; }
        }

        private static void SmokeClipOptions(List<string> checks)
        {
            Preferences old;
            using (MemoryStream json = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}"))) old = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(json);
            ClipSettings defaults = ClipSettings.Read(old);
            if (defaults.Seconds != 30 || defaults.Fps != 30 || defaults.Resolution != 0 || defaults.Mbps != 0 || defaults.Folder != ClipBuffer.Folder || old.ClipGames)
                throw new Exception("Old settings did not keep clips off with their original defaults.");
            Preferences custom = new Preferences { ClipSeconds = 90, ClipFps = 60, ClipResolution = 720, ClipMbps = 12, ClipFolder = Path.GetTempPath() };
            using (MemoryStream json = new MemoryStream())
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(Preferences));
                serializer.WriteObject(json, custom);
                json.Position = 0;
                ClipSettings kept = ClipSettings.Read((Preferences)serializer.ReadObject(json));
                if (kept.Seconds != 90 || kept.Fps != 60 || kept.Resolution != 720 || kept.Mbps != 12 || kept.Folder != Path.GetFullPath(Path.GetTempPath())) throw new Exception("Clip settings did not survive saving.");
            }
            ClipSettings invalid = new ClipSettings(null, -1, int.MaxValue, -8, -12);
            if (invalid.Seconds != 30 || invalid.Fps != 30 || invalid.Resolution != 0 || invalid.Mbps != 0) throw new Exception("Invalid clip values did not fall back to defaults.");
            ClipSettings quality = new ClipSettings(null, 60, 60, 720, 12);
            if (quality.Fit(3840, 2160) != new Size(1280, 720) || quality.Fit(2560, 1080) != new Size(1280, 540)
                || quality.Fit(1080, 1920) != new Size(404, 720) || quality.Fit(98, 66) != new Size(98, 66) || quality.Bits(1280, 720) != 12000000)
                throw new Exception("Clip sizing lost aspect ratio, upscaled, or ignored the bitrate.");
            if (!quality.Same(new ClipSettings(quality.Folder.ToUpperInvariant() + Path.DirectorySeparatorChar, 60, 60, 720, 12))
                || quality.Same(new ClipSettings(quality.Folder, 30, 60, 720, 12)) || quality.Same(new ClipSettings(quality.Folder, 60, 30, 720, 12))
                || quality.Same(new ClipSettings(quality.Folder, 60, 60, 480, 12)) || quality.Same(new ClipSettings(quality.Folder, 60, 60, 720, 8))
                || quality.Same(new ClipSettings(Path.Combine(quality.Folder, "other"), 60, 60, 720, 12)) || quality.Same(null))
                throw new Exception("Clip settings were not compared by all their values.");
            if (defaults.Fit(3840, 2160) != new Size(1920, 1080) || defaults.Fit(98, 66) != new Size(98, 66)
                || defaults.Bits(1920, 1080) != 8294400) throw new Exception("Auto clip sizing or bitrate changed.");
            checks.Add("PASS clip folder, length, FPS, resolution and bitrate survive saving; old and invalid preferences retain safe defaults, aspect ratio and no upscaling");
            string tidy = Path.Combine(Path.GetTempPath(), "Capri-custom-clips-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tidy);
            try
            {
                string abandoned = Path.Combine(tidy, ".capri-" + Guid.NewGuid().ToString("N") + ".mp4");
                string unrelated = Path.Combine(tidy, ".capri-favourite.mp4");
                File.WriteAllText(abandoned, "fake abandoned temporary clip");
                File.WriteAllText(unrelated, "their own file");
                File.SetLastWriteTimeUtc(abandoned, DateTime.UtcNow.AddHours(-2));
                File.SetLastWriteTimeUtc(unrelated, DateTime.UtcNow.AddHours(-2));
                ClipMedia.Tidy(tidy);
                if (File.Exists(abandoned) || !File.Exists(unrelated)) throw new Exception("Custom-folder cleanup touched an unrelated file or left an abandoned temporary clip.");
            }
            finally { foreach (string file in Directory.GetFiles(tidy)) File.Delete(file); Directory.Delete(tidy); }
            checks.Add("PASS custom-folder cleanup recognizes only generated temporary clip names and preserves other files");

            using (ClipTrack track = new ClipTrack())
            {
                track.Merge(new[] { new ClipPart { At = 10, Data = new byte[4] } });
                track.Merge(new[] { new ClipPart { At = 5, Data = new byte[8] }, new ClipPart { At = 10, Data = new byte[4] } });
                int count = 0;
                foreach (ClipPart part in track.Read(true)) { count++; if (part.At != 10 || part.Data.Length != 4) throw new Exception("Prepending clip frames moved the newest frame offset."); }
                if (count != 1) throw new Exception("The newest clip frame was not read once after prepending.");
                track.Merge(new[] { new ClipPart { At = 15, Data = new byte[16] } });
                count = 0;
                foreach (ClipPart part in track.Read(true)) { count++; if (part.At != 15 || part.Data.Length != 16) throw new Exception("Appending clip frames lost the newest frame offset."); }
                if (count != 1) throw new Exception("The newest clip frame was not read once after appending.");
            }
            checks.Add("PASS clip settings compare by value; Auto keeps its size and bitrate rules; merged tracks retain their newest frame");

            using (ManualResetEvent began = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                int written = 0;
                long requestTime = 0;
                Preferences repeatedOptions = new Preferences { ClipSeconds = 30, ClipFps = 24, ClipResolution = 720, ClipMbps = 4, ClipFolder = Path.GetTempPath() };
                using (ClipBuffer buffer = new ClipBuffer(120, 8192, delegate(ClipSave save)
                {
                    Interlocked.Increment(ref written);
                    began.Set();
                    if (!release.WaitOne(3000)) throw new Exception("The fake encoder timed out.");
                    return new ClipResult { Seconds = (save.End - save.Start) / 10000000.0 };
                }, delegate { return requestTime; }))
                try
                {
                    bool repeated;
                    buffer.Configure(ClipSettings.Read(repeatedOptions));
                    buffer.Follow(new GameSeen { Window = new IntPtr(42), Process = 123, Name = "Fake game" });
                    buffer.Keep(new byte[64], 0, 0, true);
                    Task<ClipResult> first = buffer.Save("first.mp4", 30, true, ClipSettings.Read(repeatedOptions), out repeated);
                    if (repeated || !began.WaitOne(3000)) throw new Exception("The fake encoder did not begin a new save.");
                    requestTime = 150000000;
                    buffer.Keep(new byte[64], 10000000, 10000000, true);
                    Task<ClipResult> during = buffer.Save("duplicate.mp4", 30, true, ClipSettings.Read(repeatedOptions), out repeated);
                    if (!repeated || !object.ReferenceEquals(first, during) || buffer.Pending != 0) throw new Exception("A repeated request queued a second file during a long encode.");
                    release.Set();
                    ClipResult result = first.GetAwaiter().GetResult();
                    if (ClipBuffer.Spoken(ClipBuffer.Report(during.GetAwaiter().GetResult(), "Fake game", repeated)) != "That clip is already saved.") throw new Exception("A repeated active clip did not say it was already saved.");
                    requestTime = 249999999;
                    buffer.Follow(new GameSeen { Window = new IntPtr(43), Process = 123, Name = "Fake game" });
                    Task<ClipResult> after = buffer.Save("another-duplicate.mp4", 30, true, ClipSettings.Read(repeatedOptions), out repeated);
                    if (!repeated || !object.ReferenceEquals(first, after) || written != 1 || result.Path != "first.mp4" || !result.Announce() || result.Announce()
                        || ClipBuffer.Report(result, "Fake game", repeated) != "That clip is already saved." || !ClipBuffer.Report(result, "Fake game", false).StartsWith("Saved ", StringComparison.Ordinal))
                        throw new Exception("A completed save, its reply or its notification was duplicated.");
                    requestTime = 250000000;
                    if (buffer.Save("later.mp4", 30, true).GetAwaiter().GetResult().Path != "later.mp4") throw new Exception("Duplicate suppression swallowed a later clip.");
                    if (buffer.Save("longer.mp4", 60, true).GetAwaiter().GetResult().Path != "longer.mp4") throw new Exception("A different duration was swallowed.");
                    buffer.Configure(quality);
                    if (buffer.Save("quality.mp4", 60, true).GetAwaiter().GetResult().Path != "quality.mp4") throw new Exception("New recording settings were swallowed.");
                    buffer.Follow(new GameSeen { Window = new IntPtr(44), Process = 124, Name = "Other game" });
                    buffer.Keep(new byte[64], 15000000, 15000000, true);
                    if (buffer.Save("new-game.mp4", 60, true).GetAwaiter().GetResult().Path != "new-game.mp4") throw new Exception("A different game inherited duplicate suppression.");
                    buffer.Enable(false);
                    buffer.Keep(new byte[64], 20000000, 20000000, true);
                    if (buffer.Save("new-session.mp4", 60, true).GetAwaiter().GetResult().Path != "new-session.mp4" || written != 6) throw new Exception("A new buffer session inherited duplicate suppression.");
                }
                finally { release.Set(); }
            }
            int attempts = 0;
            using (ClipBuffer retry = new ClipBuffer(120, 8192, delegate { return Interlocked.Increment(ref attempts) == 1 ? new ClipResult { Problem = "fake failure" } : new ClipResult(); }))
            {
                retry.Keep(new byte[64], 0, 0, true);
                retry.Save("failed.mp4", 30, true).GetAwaiter().GetResult();
                if (retry.Save("retry.mp4", 30, true).GetAwaiter().GetResult().Problem != null || attempts != 2) throw new Exception("A failed save could not be retried.");
            }
            checks.Add("PASS fresh settings objects share a long-running save and the first ten seconds after completion, say it is already saved and notify once; later requests, different durations, settings, games, sessions and retries still save");
            using (ManualResetEvent began = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            using (ClipBuffer buffer = new ClipBuffer(120, 8192, delegate(ClipSave save)
            {
                began.Set();
                if (!release.WaitOne(3000)) throw new Exception("The fake encoder timed out.");
                return new ClipResult();
            }))
            try
            {
                IntPtr gameWindow = new IntPtr(42), captured = IntPtr.Zero;
                string monitor = "game monitor at request";
                buffer.MonitorOf = delegate(IntPtr window) { captured = window; return monitor; };
                buffer.Follow(new GameSeen { Window = gameWindow, Process = 123, Name = "Fake game" });
                buffer.Keep(new byte[64], 0, 0, true);
                Task<ClipResult> saving = buffer.Save("monitor.mp4", 30);
                if (!began.WaitOne(3000)) throw new Exception("The fake monitor encoder did not begin.");
                monitor = "other monitor at completion";
                buffer.Follow(new GameSeen { Window = new IntPtr(43), Process = 123, Name = "Fake game" });
                release.Set();
                if (saving.GetAwaiter().GetResult().Monitor != "game monitor at request" || captured != gameWindow) throw new Exception("A clip used the completion-time foreground monitor instead of its game's requested monitor.");
            }
            finally { release.Set(); }
            checks.Add("PASS clip saves carry the game's monitor from the request through delayed encoding even when the game window and current monitor change before completion");
            using (ManualResetEvent began = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                List<ClipSettings> encoded = new List<ClipSettings>();
                using (ClipBuffer buffer = new ClipBuffer(120, 8192, delegate(ClipSave save)
                {
                    if (encoded.Count == 0) { began.Set(); if (!release.WaitOne(3000)) throw new Exception("The fake encoder timed out."); }
                    encoded.Add(save.Settings);
                    return new ClipResult();
                }))
                try
                {
                    ClipSettings original = buffer.Settings;
                    buffer.Keep(new byte[64], 0, 0, true);
                    Task<ClipResult> first = buffer.Save("active.mp4", 30);
                    if (!began.WaitOne(3000)) throw new Exception("The fake encoder did not begin.");
                    Task<ClipResult> queued = buffer.Save("queued.mp4", 30);
                    ClipSettings equal = new ClipSettings(original.Folder, original.Seconds, original.Fps, original.Resolution, original.Mbps);
                    if (!object.ReferenceEquals(queued, buffer.Save("same-settings.mp4", 30, false, equal))) throw new Exception("An equal settings object could not join the pending clip.");
                    buffer.Configure(quality);
                    ClipResult changed = buffer.Save("new-folder.mp4", 30, true).GetAwaiter().GetResult();
                    if (changed.Problem == null || !changed.Problem.Contains("previous recording settings") || buffer.Pending != 1) throw new Exception("A new folder or format silently merged into an old queued clip.");
                    buffer.Enable(false);
                    buffer.Keep(new byte[64], 10000000, 10000000, true);
                    ClipResult switched = buffer.Save("new-game.mp4", 30, false, original).GetAwaiter().GetResult();
                    if (switched.Problem == null || !switched.Problem.Contains("previous game")) throw new Exception("A new game merged into an old queued clip.");
                    release.Set();
                    first.GetAwaiter().GetResult(); queued.GetAwaiter().GetResult();
                    if (buffer.Save("new-folder.mp4", 30, true).GetAwaiter().GetResult().Path != "new-folder.mp4") throw new Exception("A new format could not save once the old queue drained.");
                    buffer.Save("snapshot.mp4", 15, false, original).GetAwaiter().GetResult();
                    if (encoded.Count != 4 || !object.ReferenceEquals(encoded[2], quality) || !object.ReferenceEquals(encoded[3], original)) throw new Exception("A request changed format after taking its folder/settings snapshot.");
                }
                finally { release.Set(); }
            }
            checks.Add("PASS an old queued clip cannot swallow a new folder or format; each request retains one recording-settings snapshot through encoding");

            Func<string> gameWas = Computer.GameOpen;
            Func<int, string> saveWas = Hands.SaveClip;
            try
            {
                Computer.GameOpen = delegate { return "Fake game"; };
                int requested = -1;
                Hands.SaveClip = delegate(int seconds) { requested = seconds; return "saved"; };
                string tool;
                ToolArguments args;
                if (!Hands.Direct("clip that", out tool, out args) || args.Seconds != null || Hands.Do(tool, args) != "saved" || requested != 0) throw new Exception("Clip that did not use the configured default.");
                if (!Hands.Direct("clip the last 30 seconds", out tool, out args) || args.Seconds != "30" || Hands.Do(tool, args) != "saved" || requested != 30) throw new Exception("An explicit clip length was lost.");
            }
            finally { Computer.GameOpen = gameWas; Hands.SaveClip = saveWas; }
            checks.Add("PASS clip that uses the chosen default duration while an explicit spoken length overrides it");
            Task.Run(delegate { SmokeClipEncoding(checks); }).GetAwaiter().GetResult();
        }

        private static void SmokeClipEncoding(List<string> checks)
        {
            string folder = Path.Combine(Path.GetTempPath(), "Capri-clip-options-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                double low = 0;
                foreach (ClipSettings chosen in new[] { new ClipSettings(folder, 30, 15, 480, 2), new ClipSettings(folder, 30, 60, 1080, 20) })
                using (ClipBuffer buffer = new ClipBuffer())
                {
                    ClipSettings previous = new ClipSettings(folder, 30, 30, chosen.Resolution == 480 ? 1080 : 480);
                    Size before = previous.Fit(1920, 1080), size = chosen.Fit(1920, 1080);
                    buffer.Configure(previous);
                    using (Bitmap picture = new Bitmap(before.Width, before.Height))
                    {
                        using (Graphics paint = Graphics.FromImage(picture)) paint.Clear(Color.Navy);
                        buffer.AddFrame(picture, 0);
                    }
                    buffer.Configure(chosen);
                    // Moving shapes under HUD text, like a game: pure noise came out at 5 Mbps when 2 was chosen.
                    using (Bitmap picture = new Bitmap(size.Width, size.Height))
                    using (Graphics paint = Graphics.FromImage(picture))
                    using (Font hud = new Font(FontFamily.GenericMonospace, size.Height / 20f, GraphicsUnit.Pixel))
                        for (int frame = 1; frame < 60; frame++)
                        {
                            paint.Clear(Color.FromArgb(30, 40, 60));
                            for (int shape = 0; shape < 40; shape++)
                                using (SolidBrush brush = new SolidBrush(Color.FromArgb(shape * 37 % 256, shape * 91 % 256, (shape * 53 + frame) % 256)))
                                    paint.FillEllipse(brush, (shape * 97 + frame * (3 + shape % 7) * 4) % size.Width, (shape * 53 + frame * (2 + shape % 5) * 3) % size.Height, size.Width / 8, size.Height / 6);
                            for (int line = 0; line < 12; line++) paint.DrawString("HP 100  AMMO 30/90  frame " + frame + " line " + line, hud, Brushes.White, 10, 10 + line * size.Height / 14);
                            buffer.AddFrame(picture, frame * 10000000L / 30 + frame % 3 * 20000);
                        }
                    buffer.Advance(20000000);
                    string path = Path.Combine(folder, chosen.Fps + ".mp4");
                    ClipResult made = buffer.Save(path, 30).GetAwaiter().GetResult();
                    if (made.Unavailable) { checks.Add("LIMIT clip settings encoder: " + made.Problem); return; }
                    if (made.Problem != null) throw new Exception("The chosen clip format failed: " + made.Problem);
                    ClipFormat format = ClipMedia.Inspect(path);
                    int bits = chosen.Bits(size.Width, size.Height);
                    if (format.Size != size || Math.Abs(format.Fps - chosen.Fps) > 0.01 || Math.Abs(format.AverageFps - chosen.Fps) > 0.05)
                        throw new Exception("The saved clip ignored its resolution or frame rate: " + format.Size + ", fps " + format.Fps + ", average " + format.AverageFps);
                    if (format.Bitrate < bits * 0.5 || format.Bitrate > bits * 1.75) throw new Exception("The saved clip missed its bitrate: " + format.Bitrate + " instead of about " + bits);
                    if (low == 0) low = format.Bitrate;
                    else if (format.Bitrate < low * 3) throw new Exception("The two clip bitrate choices saved at nearly the same bitrate.");
                    double length, soundLength;
                    Bitmap decoded;
                    long[] times;
                    bool sound;
                    int peak;
                    ClipMedia.Inspect(path, out length, out decoded, out times, out sound, out soundLength, out peak);
                    using (decoded)
                    {
                        if (decoded == null || decoded.Size != size || Math.Abs(length - 2) > 0.05 || times.Length != chosen.Fps * 2)
                            throw new Exception("The encoded clip ignored its size, frame count or duration.");
                        for (int frame = 0; frame < times.Length; frame++)
                            if (Math.Abs(times[frame] - frame * 10000000L / chosen.Fps) > 10000) throw new Exception("The saved frame times ignored the chosen frame rate.");
                    }
                }
                checks.Add("PASS game-like clips reopen at 480p 15 fps near 2 Mbps and 1080p 60 fps near 20 Mbps after a settings change; a picture from before a resolution change doesn't set the clip's size, and uneven capture keeps its length at the chosen frame rate");
                using (ClipBuffer buffer = new ClipBuffer())
                using (Bitmap first = new Bitmap(98, 66))
                using (Bitmap last = new Bitmap(196, 132))
                {
                    buffer.AddFrame(first, 0);
                    buffer.AddFrame(last, 1000000);
                    buffer.Advance(2000000);
                    string path = Path.Combine(folder, "auto.mp4");
                    ClipResult made = buffer.Save(path, 30).GetAwaiter().GetResult();
                    if (made.Problem != null) throw new Exception("The Auto clip failed: " + made.Problem);
                    if (ClipMedia.Inspect(path).Size != first.Size) throw new Exception("Auto stopped using the first buffered frame's size.");
                }
                checks.Add("PASS Auto clips keep their previous output sizing when buffered frame sizes differ");
            }
            finally { foreach (string file in Directory.GetFiles(folder)) File.Delete(file); Directory.Delete(folder); }
        }

        private void SmokeFullscreenInput(List<string> checks)
        {
            bool lent = lentOut, rode = riding, pointed = pointing, full = fullThrough, dock = docked, hide = preferences.HideFullScreen;
            System.Windows.Forms.Form[] windows = { this, pill, box, taskPanel, player };
            try
            {
                lentOut = riding = pointing = docked = preferences.HideFullScreen = false;
                foreach (System.Windows.Forms.Form window in windows) { IntPtr handle = window.Handle; }
                CheckFullScreen(true);
                foreach (System.Windows.Forms.Form window in windows)
                    if ((Native.GetWindowLong(window.Handle, -20) & 0x20) != 0) throw new Exception("A fullscreen companion control still passes clicks through.");
                if (!PillHeld(true)) throw new Exception("Fullscreen hover did not keep her controls reachable.");
                lentOut = true;
                PassThrough();
                foreach (System.Windows.Forms.Form window in windows)
                    if ((Native.GetWindowLong(window.Handle, -20) & 0x20) == 0) throw new Exception("Lending the cursor did not pass her action through.");
                lentOut = false;
                CheckFullScreen(false);
                foreach (System.Windows.Forms.Form window in windows)
                    if ((Native.GetWindowLong(window.Handle, -20) & 0x20) != 0) throw new Exception("A finished cursor loan or fullscreen transition left a control unclickable.");
            }
            finally { lentOut = lent; riding = rode; pointing = pointed; fullThrough = full; docked = dock; preferences.HideFullScreen = hide; PassThrough(); }
            checks.Add("PASS fullscreen keeps Capri and her controls clickable and hoverable; lending the cursor still passes her action through and restores input afterwards");
        }

        private void SmokeClipPanel(List<string> checks, string output)
        {
            int seconds = preferences.ClipSeconds, fps = preferences.ClipFps, resolution = preferences.ClipResolution, mbps = preferences.ClipMbps;
            string folder = preferences.ClipFolder;
            int scale = preferences.MainframeSize;
            bool page = mainframe.SettingsShown, simpleWas = simpleTest, visible = mainframe.Visible;
            // The step checks after this expect Mainframe back in the taskbar the way it was, minimized.
            System.Windows.Forms.FormWindowState state = mainframe.WindowState;
            try
            {
                simpleTest = false;
                mainframe.Restore();
                mainframe.ShowSettings(true);
                bool through = fullThrough;
                try { mainframe.CheckDisplayDeferral(delegate(bool full) { fullThrough = full; }); }
                finally { fullThrough = through; }
                FrameGrab.CheckRecovery(checks);
                SmokeFullscreenInput(checks);
                mainframe.PickClipOptions(60, 24, 480, 4);
                ClipSettings saved = ClipSettings.Read(LoadPreferences());
                if (saved.Seconds != 60 || saved.Fps != 24 || saved.Resolution != 480 || saved.Mbps != 4 || !clips.Settings.Same(saved)) throw new Exception("The clips panel did not apply and save its recording controls.");
                string customFolder = Path.Combine(output, "clips-folder-with-a-long-name-to-check-the-path-ellipsis");
                Directory.CreateDirectory(customFolder);
                if (SetClipFolder(customFolder) != null || clips.Settings.Folder != customFolder || ClipSettings.Read(LoadPreferences()).Folder != customFolder) throw new Exception("The clips folder did not save and reach the recorder.");
                if (Directory.GetFiles(customFolder, ".capri-write-*.tmp").Length != 0) throw new Exception("The clips folder write check left a temporary file.");
                Action<string> checkFolderWas = ClipSettings.CheckFolder;
                try
                {
                    foreach (bool create in new[] { true, false })
                    {
                        bool tested = false;
                        ClipSettings.CheckFolder = delegate(string chosen)
                        {
                            tested = chosen == Path.GetFullPath(output);
                            if (create) throw new UnauthorizedAccessException("fake create denied");
                            throw new IOException("fake delete denied");
                        };
                        string refused = SetClipFolder(output);
                        if (!tested || refused != "Couldn't save clips in that folder. Pick a folder you can write to." || preferences.ClipFolder != customFolder
                            || clips.Settings.Folder != customFolder || ClipSettings.Read(LoadPreferences()).Folder != customFolder) throw new Exception("An unwritable clips folder replaced the previous saved folder or had no plain explanation.");
                    }
                }
                finally { ClipSettings.CheckFolder = checkFolderWas; }
                checks.Add("PASS clips folders pass a real create-and-delete probe before saving; fake create and delete failures explain the problem and preserve the old preference and recorder folder");
                OpenClips();
                foreach (bool basic in new[] { false, true })
                foreach (int zoom in new[] { 100, 150, 200 })
                {
                    simpleTest = basic;
                    mainframe.ShowSettings(true);
                    mainframe.SizeMainframe(zoom);
                    string problem = mainframe.Overlap() ?? (mainframe.PageClamped ? null : mainframe.Outside()) ?? mainframe.TextFits();
                    if (!mainframe.ClipsSectionShown || !mainframe.SimpleGameControls || problem != null) throw new Exception("The clips panel does not fit at " + zoom + "% (simple " + basic + "): " + problem);
                    mainframe.ShowClipSection();
                    using (Bitmap image = new Bitmap(mainframe.Width, mainframe.Height))
                    {
                        mainframe.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                        image.Save(Path.Combine(output, "clips-" + (basic ? "simple-" : "advanced-") + zoom + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                    }
                }
            }
            finally
            {
                preferences.ClipSeconds = seconds; preferences.ClipFps = fps; preferences.ClipResolution = resolution; preferences.ClipMbps = mbps; preferences.ClipFolder = folder;
                clips.Configure(ClipOptions);
                SavePreferences();
                simpleTest = simpleWas;
                mainframe.SizeMainframe(scale);
                mainframe.Sync();
                mainframe.ShowSettings(page);
                if (!visible) mainframe.Hide();
                else mainframe.WindowState = state;
            }
            checks.Add("PASS CLIPS has its own Mainframe section in Simple and Advanced Settings at 100, 150 and 200%; recording controls and folder changes save and apply without overlaps");
            checks.Add("PASS fullscreen Mainframe reuses cached panel scaling or defers a cold driver query, including after display-cache invalidation and a focus change during restore");
            checks.Add("LIMIT live clips: verify the chosen FPS, resolution, bitrate, sound and fullscreen transitions in a real game; no real desktop or audio devices changed by these fixtures");
        }
    }

    internal sealed partial class FrameGrab
    {
        private sealed class QuietGrab : IFrameGrab
        {
            internal int Calls, Disposals;
            private Rectangle area;
            private void Call() { Calls++; if (Disposals != 0) throw new Exception("A disposed fake grab was used."); }
            public string Live { get { Call(); return "Graphics.CopyFromScreen"; } }
            public Size Size { get { Call(); return area.Size; } }
            public Action<int> Lost { set { Call(); } }
            public void Open(Rectangle bounds) { Call(); area = bounds; }
            public bool Next(int timeoutMs) { Call(); return true; }
            public Bitmap Copy(int maxWidth) { Call(); return null; }
            public byte[] Gray(int width, int height) { Call(); return null; }
            public void Dispose() { if (++Disposals != 1) throw new Exception("A fake grab was disposed twice."); }
        }

        internal static void CheckCaptureQuiet(List<string> checks, string who, Func<GameCapture, bool, Sight, bool, bool> decide)
        {
            string before = Brain.LogPath;
            string log = Path.Combine(Path.GetTempPath(), "capri-capture-quiet-" + Guid.NewGuid().ToString("N") + ".log");
            long tick = 0;
            IntPtr window = new IntPtr(7), foreground = window;
            Rectangle bounds = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            bool full = false;
            List<QuietGrab> made = new List<QuietGrab>();
            GameCapture frames = new GameCapture(who, delegate { return tick; }, delegate
            {
                foreach (QuietGrab old in made)
                    if (old.Disposals != 1) throw new Exception(who + " made a new grab before releasing the old one.");
                QuietGrab next = new QuietGrab();
                made.Add(next);
                return next;
            }, delegate { return foreground; }, delegate { return false; });
            Func<bool, bool> turn = delegate(bool on) { return decide(frames, on, new Sight { Window = window, Bounds = bounds }, full); };
            try
            {
                Brain.LogPath = log;
                if (turn(true) || made.Count != 0) throw new Exception(who + " skipped the quiet fixture's opening wait.");
                tick += Stopwatch.Frequency;
                if (!turn(true) || made.Count != 1) throw new Exception(who + " failed to open the quiet fixture.");
                QuietGrab first = made[0];
                first.Next(0); first.Copy(8); first.Gray(2, 2);
                int calls = first.Calls;
                foreground = new IntPtr(9);
                if (turn(true) || frames.Grab != null || first.Disposals != 0 || first.Calls != calls) throw new Exception(who + " touched its grab when the game left the front.");
                for (int turnAt = 0; turnAt < 10; turnAt++)
                    if (turn(true) || first.Disposals != 0 || first.Calls != calls) throw new Exception(who + " touched its waiting grab on a stopped clock.");
                frames.Close("error");
                if (first.Disposals != 0 || first.Calls != calls) throw new Exception(who + " touched a pending grab during error cleanup.");
                tick += Stopwatch.Frequency / 2;
                foreground = new IntPtr(10);
                turn(true);
                tick += Stopwatch.Frequency / 2;
                bounds.Offset(1, 0);
                turn(true);
                tick += Stopwatch.Frequency / 2;
                full = true;
                turn(true);
                tick += Stopwatch.Frequency - 1;
                if (turn(true) || first.Disposals != 0 || first.Calls != calls) throw new Exception(who + " released or used capture before the display was quiet.");
                tick++;
                if (turn(true) || first.Disposals != 1 || first.Calls != calls) throw new Exception(who + " failed to release capture after a quiet second.");
                tick += Stopwatch.Frequency * 2;
                turn(true);
                if (first.Disposals != 1 || first.Calls != calls || made.Count != 1) throw new Exception(who + " repeated a quiet release or reopened behind the shell.");
                foreground = window;
                if (turn(true)) throw new Exception(who + " skipped the return wait in its quiet fixture.");
                tick += Stopwatch.Frequency;
                if (!turn(true)) throw new Exception(who + " failed to return in its quiet fixture.");
                QuietGrab shell = made[made.Count - 1];
                calls = shell.Calls;
                foreground = new IntPtr(10);
                if (turn(true) || frames.Grab != null || shell.Calls != calls || shell.Disposals != 0) throw new Exception(who + " kept using capture when the shell took the front.");
                tick += Stopwatch.Frequency - 1;
                if (turn(true) || shell.Calls != calls || shell.Disposals != 0) throw new Exception(who + " touched capture during the shell switch.");
                tick++;
                if (turn(true) || shell.Calls != calls || shell.Disposals != 1) throw new Exception(who + " kept capture after the shell was quiet.");
                foreground = window;
                if (turn(true)) throw new Exception(who + " skipped its wait after the shell.");
                tick += Stopwatch.Frequency;
                if (!turn(true)) throw new Exception(who + " failed to resume after the shell.");
                foreach (bool resize in new[] { true, false })
                {
                    QuietGrab old = made[made.Count - 1];
                    calls = old.Calls;
                    if (resize) bounds.Offset(1, 0); else full = !full;
                    if (turn(true) || old.Disposals != 0 || old.Calls != calls) throw new Exception(who + " touched capture during a bounds or exclusive switch.");
                    tick += Stopwatch.Frequency / 2;
                    if (resize) bounds.Offset(1, 0); else full = !full;
                    turn(true);
                    tick += Stopwatch.Frequency - 1;
                    if (turn(true) || old.Disposals != 0 || old.Calls != calls) throw new Exception(who + " shortened a changed display's quiet second.");
                    tick++;
                    if (!turn(true) || old.Disposals != 1 || old.Calls != calls) throw new Exception(who + " failed to release capture before its replacement.");
                }
                QuietGrab waiting = made[made.Count - 1];
                calls = waiting.Calls;
                foreground = new IntPtr(9);
                turn(true);
                if (waiting.Disposals != 0 || waiting.Calls != calls) throw new Exception(who + " touched its last grab before switching off.");
                if (turn(false) || waiting.Disposals != 1 || waiting.Calls != calls) throw new Exception(who + " kept a pending grab after switching off.");
                turn(false);
                foreground = window;
                turn(true);
                tick += Stopwatch.Frequency;
                if (!turn(true)) throw new Exception(who + " failed to open its closing fixture.");
                waiting = made[made.Count - 1];
                calls = waiting.Calls;
                foreground = new IntPtr(9);
                turn(true);
                frames.Close("switched off");
                frames.Close("switched off");
                if (waiting.Disposals != 1 || waiting.Calls != calls) throw new Exception(who + " kept or reused a pending grab while closing.");
                foreach (QuietGrab old in made)
                    if (old.Disposals != 1) throw new Exception(who + " left a grab waiting after shutdown.");
            }
            finally { frames.Close("switched off"); Brain.LogPath = before; if (File.Exists(log)) File.Delete(log); }
            checks.Add("PASS " + who + " stops using capture at once, leaves it untouched through foreground, bounds and exclusive changes, releases it once after a quiet second or before making another, and releases pending capture at once on switch-off and closing");
        }

        internal static void CheckGameCapture(List<string> checks, string who, Func<GameCapture, bool, Sight, bool, bool> decide)
        {
            string before = Brain.LogPath;
            Func<Sight> glimpseBefore = Computer.Glimpse;
            string log = Path.Combine(Path.GetTempPath(), "capri-capture-" + Guid.NewGuid().ToString("N") + ".log");
            long tick = 0;
            int starts = 0, takes = 0, queries = 0;
            bool dxgi = true;
            bool minimized = false;
            Rectangle bounds = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            IntPtr window = new IntPtr(7), other = new IntPtr(8);
            IntPtr foreground = window;
            List<string> expected = new List<string>();
            Action read = delegate { CheckCaptureLines(log, who, expected); };
            GameCapture frames = new GameCapture(who, delegate { return tick; }, delegate
            {
                starts++;
                FrameGrab frame = null;
                frame = new FrameGrab(delegate { return tick; }, delegate { queries++; return true; }, delegate
                {
                    if (!dxgi) return;
                    frame.duplication = Marshal.GetIUnknownForObject(new object());
                    frame.describe = delegate(IntPtr output, out OutputDesc value) { value = new OutputDesc { Attached = 1, Left = frame.desktop.Left, Top = frame.desktop.Top, Right = frame.desktop.Right, Bottom = frame.desktop.Bottom }; return 0; };
                    frame.acquire = delegate(IntPtr duplicate, int timeout, out FrameInfo info, out IntPtr resource) { takes++; info = new FrameInfo(); resource = IntPtr.Zero; return WaitTimeout; };
                }, delegate { return true; });
                return frame;
            }, delegate { return foreground; }, delegate { return minimized; });
            Sight candidate = new Sight { Window = window, Bounds = bounds };
            Func<bool, IntPtr, Rectangle, bool, bool> step = delegate(bool on, IntPtr next, Rectangle area, bool full)
            {
                candidate = new Sight { Window = next, Bounds = area };
                return decide(frames, on, Computer.Glimpse(), full);
            };
            try
            {
                Brain.LogPath = log;
                Computer.Glimpse = delegate { return candidate; };
                for (int turn = 0; turn < 10; turn++)
                    if (step(true, window, bounds, true) || frames.Grab != null || starts != 0) throw new Exception(who + " made a grab while its clock was stopped.");
                tick = Stopwatch.Frequency - 1;
                if (step(true, window, bounds, true) || starts != 0) throw new Exception(who + " opened before a steady second.");
                read();
                tick++;
                if (!step(true, window, bounds, true) || starts != 1) throw new Exception(who + " did not open after a steady second.");
                expected.Add("opened DXGI 8x8 exclusive=yes");
                read();
                FrameGrab healthy = (FrameGrab)frames.Grab;
                for (int turn = 0; turn < 3; turn++)
                {
                    tick += Stopwatch.Frequency * 3;
                    if (!step(true, window, bounds, true) || frames.Grab != healthy) throw new Exception(who + " restarted a steady capture.");
                    healthy.Next(0);
                }
                if (starts != 1 || takes != 3 || queries != 0) throw new Exception(who + " paused healthy duplication or queried fullscreen.");
                read();
                foreground = new IntPtr(9);
                if (step(true, window, bounds, true) || frames.Grab != null) throw new Exception(who + " waited for another scan before leaving the front.");
                if (healthy.disposed) throw new Exception(who + " disposed its grab during the display switch.");
                expected.Add("closed left the front");
                read();
                tick += Stopwatch.Frequency - 1;
                if (step(true, window, bounds, true) || healthy.disposed) throw new Exception(who + " released capture before a quiet second.");
                tick++;
                if (step(true, window, bounds, true) || !healthy.disposed) throw new Exception(who + " kept capture after a quiet second.");
                foreach (IntPtr covering in new[] { new IntPtr(9), new IntPtr(10) })
                {
                    foreground = covering;
                    for (int turn = 0; turn < 10; turn++)
                    {
                        tick += Stopwatch.Frequency * 2;
                        Sight reported = Computer.Glimpse();
                        if (reported.Window != window || step(true, reported.Window, reported.Bounds, true) || frames.Grab != null || starts != 1) throw new Exception(who + " captured a reported game behind their Mainframe or shell.");
                    }
                }
                read();
                foreground = window;
                if (step(true, window, bounds, true)) throw new Exception(who + " reopened on its first foreground turn.");
                tick += Stopwatch.Frequency - 1;
                if (step(true, window, bounds, true)) throw new Exception(who + " shortened its return wait.");
                tick++;
                if (!step(true, window, bounds, true) || starts != 2) throw new Exception(who + " did not capture its steady return.");
                expected.Add("opened DXGI 8x8 exclusive=yes");
                read();
                bounds.Offset(1, 1);
                if (step(true, window, bounds, true) || frames.Grab != null) throw new Exception(who + " kept capture through a bounds change.");
                expected.Add("closed bounds changed");
                read();
                tick += Stopwatch.Frequency / 2;
                bounds.Size = new Size(12, 10);
                if (step(true, window, bounds, true) || frames.Grab != null || starts != 2) throw new Exception(who + " made a grab while bounds kept changing.");
                tick += Stopwatch.Frequency - 1;
                if (step(true, window, bounds, true)) throw new Exception(who + " did not restart the second on a second bounds change.");
                dxgi = false;
                tick++;
                if (!step(true, window, bounds, true) || starts != 3) throw new Exception(who + " never captured a settled resize.");
                expected.Add("opened screen copy 12x10 exclusive=yes");
                read();
                if (step(true, window, bounds, false) || frames.Grab != null) throw new Exception(who + " kept capture through an exclusive change.");
                expected.Add("closed exclusive changed");
                read();
                tick += Stopwatch.Frequency - 1;
                if (step(true, window, bounds, false) || starts != 3) throw new Exception(who + " shortened its exclusive wait.");
                tick++;
                if (!step(true, window, bounds, false) || starts != 4) throw new Exception(who + " did not resume after an exclusive change.");
                expected.Add("opened screen copy 12x10 exclusive=no");
                if (step(false, window, bounds, false) || frames.Grab != null) throw new Exception(who + " kept capture after switching off.");
                expected.Add("closed switched off");
                for (int off = 0; off < 10; off++) step(false, window, bounds, false);
                read();
                if (step(true, window, bounds, false)) throw new Exception(who + " skipped its wait after switching on.");
                tick += Stopwatch.Frequency;
                if (!step(true, window, bounds, false)) throw new Exception(who + " did not resume after switching on.");
                expected.Add("opened screen copy 12x10 exclusive=no");
                frames.Close("error");
                frames.Close("error");
                expected.Add("closed error");
                read();
                if (step(true, window, bounds, false)) throw new Exception(who + " skipped its wait after an error.");
                tick += Stopwatch.Frequency;
                if (!step(true, window, bounds, false)) throw new Exception(who + " did not resume after an error.");
                expected.Add("opened screen copy 12x10 exclusive=no");
                foreground = other;
                if (step(true, other, bounds, false) || frames.Grab != null) throw new Exception(who + " kept capture for a different game window.");
                expected.Add("closed window changed");
                tick += Stopwatch.Frequency;
                if (!step(true, other, bounds, false)) throw new Exception(who + " did not capture the next steady window.");
                expected.Add("opened screen copy 12x10 exclusive=no");
                Rectangle screens = System.Windows.Forms.SystemInformation.VirtualScreen;
                Rectangle edge = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                foreach (System.Windows.Forms.Screen screen in System.Windows.Forms.Screen.AllScreens)
                    if (screen.Bounds.Left < edge.Left) edge = screen.Bounds;
                Rectangle clipped = new Rectangle(edge.Left - 4, edge.Top, 8, 8);
                if (step(true, other, clipped, false) || frames.Grab != null) throw new Exception(who + " kept capture through a crop change.");
                expected.Add("closed bounds changed");
                tick += Stopwatch.Frequency;
                if (!step(true, other, clipped, false) || frames.Grab.Size != new Size(4, 8)) throw new Exception(who + " lost the cropped capture size.");
                expected.Add("opened screen copy 4x8 exclusive=no");
                foreach (string why in new[] { "minimized", "bounds changed", "off screen" })
                {
                    minimized = why == "minimized";
                    foreground = minimized ? new IntPtr(9) : other;
                    Rectangle current = why == "bounds changed" ? Rectangle.Empty : why == "off screen" ? new Rectangle(screens.Right + 16, screens.Bottom + 16, 8, 8) : clipped;
                    FrameGrab previous = (FrameGrab)frames.Grab;
                    int opened = starts;
                    if (minimized && step(true, other, current, false)) throw new Exception(who + " kept a minimized window on its foreground check.");
                    if (step(true, other, current, false) || frames.Grab != null) throw new Exception(who + " kept capture with " + why + ".");
                    if (previous.disposed) throw new Exception(who + " disposed capture before it settled with " + why + ".");
                    expected.Add("closed " + why);
                    for (int turn = 0; turn < 10; turn++)
                    {
                        tick += Stopwatch.Frequency * 2;
                        foreground = other;
                        if (step(true, other, current, false) || frames.Grab != null || starts != opened) throw new Exception(who + " opened capture with " + why + " after waiting.");
                    }
                    if (!previous.disposed) throw new Exception(who + " kept capture after settling with " + why + ".");
                    read();
                    minimized = false;
                    foreground = other;
                    if (step(true, other, clipped, false)) throw new Exception(who + " skipped its wait after " + why + ".");
                    tick += Stopwatch.Frequency - 1;
                    if (step(true, other, clipped, false) || starts != opened) throw new Exception(who + " shortened its wait after " + why + ".");
                    tick++;
                    if (!step(true, other, clipped, false) || starts != opened + 1) throw new Exception(who + " did not resume after " + why + ".");
                    expected.Add("opened screen copy 4x8 exclusive=no");
                    read();
                }
                frames.Close("switched off");
                expected.Add("closed switched off");
                read();
            }
            finally
            {
                frames.Close("switched off");
                Computer.Glimpse = glimpseBefore;
                Brain.LogPath = before;
                if (File.Exists(log)) File.Delete(log);
            }
            checks.Add("PASS " + who + " closes on the first foreground check, makes no grab before a steady second, restarts on bounds or exclusive changes, and logs each open and close once with only capture state");
            checks.Add("PASS " + who + " ignores a game reported behind their Mainframe or shell, waits a fresh second on return, and closes minimized, empty or off-screen capture without repeated opens or log lines");
        }

        internal static void CheckEmptyCapture(List<string> checks)
        {
            Rectangle screens = System.Windows.Forms.SystemInformation.VirtualScreen;
            foreach (Rectangle bounds in new[] { new Rectangle(screens.Right + 16, screens.Bottom + 16, 8, 8), new Rectangle(screens.Right, screens.Top, 8, 8), new Rectangle(screens.Left, screens.Bottom, 8, 8) })
            {
                long tick = 0;
                int starts = 0, copies = 0, queries = 0;
                bool full = false;
                using (FrameGrab frame = new FrameGrab(delegate { return tick; }, delegate { queries++; return full; }, delegate { starts++; }, delegate { copies++; return true; }))
                {
                    bool refused = false;
                    try { frame.Open(Rectangle.Empty); } catch (ArgumentException) { refused = true; }
                    if (!refused || starts != 0) throw new Exception("Empty frame bounds reached display setup.");
                    frame.Open(bounds);
                    if (frame.Size.Width > 0 && frame.Size.Height > 0) throw new Exception("Off-screen frame bounds kept a capture area.");
                    for (int turn = 0; turn < 20; turn++)
                    {
                        tick += Stopwatch.Frequency * 3;
                        full = turn % 2 == 0;
                        if (frame.Next(0) || frame.Copy(8) != null || frame.Gray(2, 2) != null) throw new Exception("An empty capture area returned a frame.");
                        frame.Reopen();
                        if (starts != 0 || copies != 0 || queries != 0) throw new Exception("An empty capture area reached display setup, screen copy or fullscreen retry.");
                    }
                }
            }
            checks.Add("PASS empty, off-screen and screen-edge frame areas never reach display setup, screen copy or fullscreen retry across twenty later reopens");
            Rectangle wanted = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            Rectangle changed = new Rectangle(wanted.Right + 16, wanted.Bottom + 16, 8, 8);
            long now = 0;
            int setups = 0, frames = 0, questions = 0;
            using (FrameGrab grab = new FrameGrab(delegate { return now; }, delegate { questions++; return false; }, delegate { setups++; }, delegate { frames++; return true; }, delegate { return changed; }))
            {
                grab.Open(wanted);
                if (grab.Next(0) || setups != 0 || frames != 0 || questions != 0) throw new Exception("An empty crop touched display setup or screen copy.");
                now = Stopwatch.Frequency * 2;
                if (grab.Next(0) || setups != 0 || frames != 0 || questions != 0) throw new Exception("An empty crop reached display setup on its retry.");
                changed = screens;
                now = Stopwatch.Frequency * 4 - 1;
                if (grab.Next(0) || setups != 0 || frames != 0 || questions != 0) throw new Exception("An empty crop retried before its timer.");
                now++;
                if (!grab.Next(0) || grab.Size != wanted.Size || setups != 1 || frames != 1 || questions != 1) throw new Exception("An empty crop failed to recover on the next retry after the screens changed.");
            }
            checks.Add("PASS an empty crop retries only its screen bounds, never display setup while empty, and receives frames on its next retry when fake screens include the area again");
        }

        private static void CheckCaptureLines(string path, string who, List<string> expected)
        {
            string[] lines = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
            if (lines.Length != expected.Count) throw new Exception("Capture events wrote the wrong number of log lines.");
            for (int line = 0; line < lines.Length; line++)
                if (lines[line].Length < 19 || lines[line].Substring(19) != " | game capture | - | " + who + " " + expected[line]) throw new Exception("A capture log line changed text or included private window details.");
        }

        internal static void CheckCaptureLoss(List<string> checks)
        {
            string before = Brain.LogPath;
            string log = Path.Combine(Path.GetTempPath(), "capri-capture-loss-" + Guid.NewGuid().ToString("N") + ".log");
            Rectangle bounds = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            IntPtr window = new IntPtr(7);
            string privateDetails = @"private window title private-process.exe C:\private-game\game.exe";
            try
            {
                Brain.LogPath = log;
                foreach (string failure in new[] { "next", "copy", "gray", "open", "mode", "screen", "win32" })
                {
                    File.WriteAllText(log, "");
                    long tick = 0;
                    int starts = 0, copies = 0, queries = 0;
                    bool fail = true;
                    bool screen = failure == "screen" || failure == "win32";
                    int lost = failure == "next" ? unchecked((int)0x887A0005) : failure == "win32" ? new System.ComponentModel.Win32Exception(5, privateDetails).HResult : AccessLost;
                    List<string> expected = new List<string>();
                    FrameGrab frame = null;
                    GameCapture frames = new GameCapture("watch", delegate { return tick; }, delegate
                    {
                        frame = new FrameGrab(delegate { return tick; }, delegate { queries++; return true; }, delegate
                        {
                            starts++;
                            if (failure == "open") throw new COMException(privateDetails, lost);
                            if (screen) return;
                            frame.duplication = Marshal.GetIUnknownForObject(new object());
                            frame.describe = delegate(IntPtr output, out OutputDesc value)
                            {
                                value = new OutputDesc { Attached = failure == "mode" ? 0 : 1, Left = frame.desktop.Left, Top = frame.desktop.Top, Right = frame.desktop.Right, Bottom = frame.desktop.Bottom };
                                return failure == "next" ? lost : 0;
                            };
                            frame.map = delegate(IntPtr context, IntPtr texture, int subresource, int kind, int flags, out Mapped value) { value = new Mapped(); return lost; };
                        }, delegate
                        {
                            copies++;
                            if (fail)
                            {
                                if (failure == "win32") throw new System.ComponentModel.Win32Exception(5, privateDetails);
                                throw new ExternalException(privateDetails, lost);
                            }
                            return true;
                        });
                        return frame;
                    }, delegate { return window; }, delegate { return false; });
                    try
                    {
                        if (frames.Update(true, window, bounds, true) || frame != null) throw new Exception("A loss fixture created capture before its steady second.");
                        tick += Stopwatch.Frequency;
                        if (!frames.Update(true, window, bounds, true)) throw new Exception("A loss fixture did not open capture.");
                        if (failure == "open") expected.Add("screen lost 0x" + lost.ToString("X8"));
                        expected.Add("opened " + (screen || failure == "open" ? "screen copy" : "DXGI") + " 8x8 exclusive=yes");
                        if (failure == "copy" || failure == "gray") frame.ready = true;
                        if (failure == "copy") { if (frame.Copy(8) != null) throw new Exception("Failed capture readback returned a picture."); }
                        else if (failure == "gray") { if (frame.Gray(2, 2) != null) throw new Exception("Failed capture readback returned gray pixels."); }
                        else if (frame.Next(0)) throw new Exception("A lost screen returned a frame.");
                        if (failure != "open") expected.Add("screen lost 0x" + lost.ToString("X8"));
                        CheckCaptureLines(log, "watch", expected);
                        long lostAt = tick;
                        int copied = copies;
                        for (int turn = 1; turn <= 60; turn++)
                        {
                            tick = lostAt + turn * Stopwatch.Frequency / 61;
                            if (!frames.Update(true, window, bounds, true) || frames.Grab != frame || frame.Next(0)) throw new Exception("Capture changed or returned a frame during a loss.");
                            if (frame.Copy(8) != null || frame.Gray(2, 2) != null) throw new Exception("A lost screen kept readable pixels.");
                        }
                        if (starts != 1 || queries != 0 || (!screen && copies != copied)) throw new Exception("Capture bypassed the settle wait while logging a loss.");
                        CheckCaptureLines(log, "watch", expected);
                        fail = false;
                        tick = lostAt + Stopwatch.Frequency * 4;
                        if (!frame.Next(0) || starts != 1) throw new Exception("Logged fullscreen loss changed fallback recovery.");
                        CheckCaptureLines(log, "watch", expected);
                        fail = true;
                        for (int turn = 0; turn < 10; turn++)
                            if (frame.Next(0)) throw new Exception("A second lost screen returned a frame.");
                        CheckCaptureLines(log, "watch", expected);
                        frame.Open(bounds);
                        if (failure == "copy" || failure == "gray") frame.ready = true;
                        if (failure == "copy") { if (frame.Copy(8) != null) throw new Exception("Reopened failed readback returned a picture."); }
                        else if (failure == "gray") { if (frame.Gray(2, 2) != null) throw new Exception("Reopened failed readback returned gray pixels."); }
                        else if (frame.Next(0)) throw new Exception("A reopened lost screen returned a frame.");
                        expected.Add("screen lost 0x" + lost.ToString("X8"));
                        CheckCaptureLines(log, "watch", expected);
                        frames.Close("switched off");
                        expected.Add("closed switched off");
                        CheckCaptureLines(log, "watch", expected);
                    }
                    finally { frames.Close("switched off"); }
                }
            }
            finally { Brain.LogPath = before; if (File.Exists(log)) File.Delete(log); }
            checks.Add("PASS fake open, next, copy, gray, mode and screen-copy losses log one hex HRESULT across sixty settle turns and fallback successes, re-arm when opened again, and exclude titles, processes, paths and exception text");
        }

        internal static void CheckCaptureRetry(List<string> checks)
        {
            string before = Brain.LogPath;
            string log = Path.Combine(Path.GetTempPath(), "capri-capture-retry-" + Guid.NewGuid().ToString("N") + ".log");
            Rectangle bounds = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            IntPtr window = new IntPtr(7);
            try
            {
                Brain.LogPath = log;
                foreach (bool recover in new[] { false, true })
                {
                    File.WriteAllText(log, "");
                    long tick = 0;
                    int starts = 0, copies = 0, queries = 0;
                    bool fail = true;
                    FrameGrab frame = null;
                    List<string> expected = new List<string>();
                    GameCapture frames = new GameCapture("clips", delegate { return tick; }, delegate
                    {
                        frame = new FrameGrab(delegate { return tick; }, delegate { queries++; return false; }, delegate
                        {
                            starts++;
                            if (!recover) throw new COMException("Fake display setup failure.", AccessLost);
                            frame.duplication = Marshal.GetIUnknownForObject(new object());
                            frame.describe = delegate(IntPtr output, out OutputDesc value) { value = new OutputDesc { Attached = 1, Left = frame.desktop.Left, Top = frame.desktop.Top, Right = frame.desktop.Right, Bottom = frame.desktop.Bottom }; return 0; };
                        }, delegate { copies++; return true; }, null, delegate
                        {
                            if (fail) throw new COMException("Fake frame failure.", AccessLost);
                            frame.ready = true;
                            return true;
                        });
                        return frame;
                    }, delegate { return window; }, delegate { return false; });
                    try
                    {
                        if (frames.Update(true, window, bounds, false)) throw new Exception("A retry fixture skipped its opening wait.");
                        tick += Stopwatch.Frequency;
                        if (!frames.Update(true, window, bounds, false)) throw new Exception("A retry fixture failed to open capture.");
                        if (!recover) expected.Add("screen lost 0x887A0026");
                        expected.Add("opened " + (recover ? "DXGI" : "screen copy") + " 8x8 exclusive=no");
                        if (recover)
                        {
                            if (frame.Next(0)) throw new Exception("A lost DXGI fixture returned a frame.");
                            expected.Add("screen lost 0x887A0026");
                            CheckCaptureLines(log, "clips", expected);
                            fail = false;
                            tick += Stopwatch.Frequency * 3 / 2;
                            if (!frame.Next(0) || starts != 2 || copies != 0) throw new Exception("A DXGI loss fixture did not receive a healthy DXGI frame.");
                            fail = true;
                            if (frame.Next(0)) throw new Exception("A second DXGI loss returned a frame.");
                            expected.Add("screen lost 0x887A0026");
                            for (int turn = 0; turn < 60; turn++) frame.Next(0);
                        }
                        else
                        {
                            long began = tick;
                            for (int turn = 1; turn <= 200; turn++)
                            {
                                tick = began + turn * Stopwatch.Frequency / 10;
                                if (!frames.Update(true, window, bounds, false) || frames.Grab != frame) throw new Exception("A windowed loss fixture replaced its grab.");
                                frame.Next(0);
                            }
                            if (starts != 11 || queries != 10 || copies == 0) throw new Exception("A windowed loss fixture missed failed setups or successful screen copies over twenty seconds.");
                        }
                        CheckCaptureLines(log, "clips", expected);
                        frames.Close("switched off");
                        expected.Add("closed switched off");
                        CheckCaptureLines(log, "clips", expected);
                    }
                    finally { frames.Close("switched off"); }
                }
            }
            finally { Brain.LogPath = before; if (File.Exists(log)) File.Delete(log); }
            checks.Add("PASS a windowed game with failed DXGI setup and successful screen copies logs one loss over twenty fake seconds and eleven setup attempts; only a healthy DXGI frame re-arms the loss line");
        }

        internal static void CheckRecovery(List<string> checks)
        {
            Rectangle bounds = new Rectangle(System.Windows.Forms.Screen.PrimaryScreen.Bounds.Location, new Size(8, 8));
            long tick = 1;
            int starts = 0, takes = 0, queries = 0;
            FrameGrab healthy = null;
            using (healthy = new FrameGrab(delegate { return tick; }, delegate { queries++; return true; }, delegate
            {
                starts++;
                healthy.duplication = Marshal.GetIUnknownForObject(new object());
                healthy.describe = delegate(IntPtr output, out OutputDesc value) { value = new OutputDesc { Attached = 1, Left = healthy.desktop.Left, Top = healthy.desktop.Top, Right = healthy.desktop.Right, Bottom = healthy.desktop.Bottom }; return 0; };
                healthy.acquire = delegate(IntPtr duplicate, int timeout, out FrameInfo info, out IntPtr resource) { takes++; info = new FrameInfo(); resource = IntPtr.Zero; return WaitTimeout; };
            }, delegate { throw new Exception("Healthy duplication fell back to GDI."); }))
            {
                healthy.Open(bounds);
                for (int frame = 0; frame < 3; frame++) { tick += Stopwatch.Frequency * 3; healthy.Next(0); }
                if (starts != 1 || takes != 3 || queries != 0) throw new Exception("Healthy fullscreen duplication was paused or restarted.");
            }
            bool busy = true;
            int unsupported = 0, fallbacks = 0;
            using (FrameGrab fallback = new FrameGrab(delegate { return tick; }, delegate { return busy; }, delegate { unsupported++; }, delegate { fallbacks++; return true; }))
            {
                fallback.Open(bounds);
                for (int frame = 0; frame < 3; frame++) { tick += Stopwatch.Frequency * 3; fallback.Next(0); }
                if (unsupported != 1 || fallbacks != 3) throw new Exception("An unsupported fullscreen output repeated DXGI setup or lost its fallback.");
                busy = false;
                tick += Stopwatch.Frequency;
                fallback.Next(0);
                if (unsupported != 2) throw new Exception("A windowed unsupported output stopped retrying DXGI setup.");
            }
            foreach (string failure in new[] { "next", "copy", "gray", "open" })
            {
                long now = 1;
                bool full = true, failOpen = failure == "open";
                int opens = 0, copies = 0;
                int lost = failure == "next" ? unchecked((int)0x887A0005) : AccessLost;
                using (FrameGrab grab = new FrameGrab(delegate { return now; }, delegate { return full; },
                    delegate { opens++; if (failOpen) throw new COMException("Fake device creation failure", lost); },
                    delegate { copies++; return true; }))
                {
                    grab.Open(bounds);
                    if (opens != 1) throw new Exception("Fullscreen capture lost its initial device attempt.");
                    if (!failOpen)
                    {
                        if (!grab.Next(0) || copies != 1) throw new Exception("Healthy fullscreen capture was paused.");
                        grab.duplication = Marshal.GetIUnknownForObject(new object());
                        grab.describe = delegate(IntPtr output, out OutputDesc value) { value = new OutputDesc(); return lost; };
                        grab.map = delegate(IntPtr context, IntPtr texture, int subresource, int kind, int flags, out Mapped value) { value = new Mapped(); return lost; };
                        if (failure == "next" && grab.Next(0)) throw new Exception("A failed capture returned a frame.");
                        if (failure == "copy" && grab.Copy(8) != null) throw new Exception("A failed readback returned a picture.");
                        if (failure == "gray" && grab.Gray(2, 2) != null) throw new Exception("A failed readback returned gray pixels.");
                    }
                    int before = copies;
                    now += Stopwatch.Frequency;
                    if (grab.Next(0) || opens != 1 || copies != before) throw new Exception("Capture bypassed its settle guard after " + failure + ".");
                    now += Stopwatch.Frequency * 4;
                    if (!grab.Next(0) || opens != 1 || copies != before + 1) throw new Exception("Fullscreen recovery restarted a device or lost its GDI fallback after " + failure + ".");
                    now += Stopwatch.Frequency * 10;
                    if (!grab.Next(0) || opens != 1) throw new Exception("Fullscreen recovery retried device creation after " + failure + ".");
                    full = false;
                    failOpen = false;
                    now += Stopwatch.Frequency;
                    if (!grab.Next(0) || opens != 2) throw new Exception("Capture did not reopen after leaving fullscreen after " + failure + ".");
                }
            }
            checks.Add("PASS fake capture and readback failures settle before GDI fallback, defer D3D recreation and unsupported-output setup throughout fullscreen, and retry after leaving it; initial and healthy fullscreen capture stay available");
        }
    }
}
