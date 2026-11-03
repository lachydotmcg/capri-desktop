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
                    string problem = mainframe.Overlap() ?? mainframe.Outside() ?? mainframe.TextFits();
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
