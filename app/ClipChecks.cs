using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.Serialization.Json;
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

            using (ManualResetEvent began = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                int written = 0;
                long requestTime = 0;
                using (ClipBuffer buffer = new ClipBuffer(120, 8192, delegate(ClipSave save)
                {
                    Interlocked.Increment(ref written);
                    began.Set();
                    if (!release.WaitOne(3000)) throw new Exception("The fake encoder timed out.");
                    return new ClipResult { Seconds = (save.End - save.Start) / 10000000.0 };
                }, delegate { return requestTime; }))
                try
                {
                    buffer.Keep(new byte[64], 0, 0, true);
                    Task<ClipResult> first = buffer.Save("first.mp4", 30, true);
                    if (!began.WaitOne(3000)) throw new Exception("The fake encoder did not begin.");
                    buffer.Keep(new byte[64], 10000000, 10000000, true);
                    Task<ClipResult> during = buffer.Save("duplicate.mp4", 30, true);
                    if (!object.ReferenceEquals(first, during) || buffer.Pending != 0) throw new Exception("A repeated request queued a second file during encoding.");
                    release.Set();
                    ClipResult result = first.GetAwaiter().GetResult();
                    Task<ClipResult> after = buffer.Save("another-duplicate.mp4", 30, true);
                    if (!object.ReferenceEquals(first, after) || written != 1 || result.Path != "first.mp4" || !result.Announce() || result.Announce()) throw new Exception("A completed save or its notification was duplicated.");
                    requestTime = 80000001;
                    if (buffer.Save("later.mp4", 30, true).GetAwaiter().GetResult().Path != "later.mp4") throw new Exception("Duplicate suppression swallowed a later clip.");
                    if (buffer.Save("longer.mp4", 60, true).GetAwaiter().GetResult().Path != "longer.mp4") throw new Exception("A different duration was swallowed.");
                    buffer.Configure(quality);
                    if (buffer.Save("quality.mp4", 60, true).GetAwaiter().GetResult().Path != "quality.mp4") throw new Exception("New recording settings were swallowed.");
                    buffer.Enable(false);
                    buffer.Keep(new byte[64], 20000000, 20000000, true);
                    if (buffer.Save("new-session.mp4", 60, true).GetAwaiter().GetResult().Path != "new-session.mp4" || written != 5) throw new Exception("A new buffer session inherited duplicate suppression.");
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
            checks.Add("PASS repeated clip requests share the active or just-finished save and notify once; later requests, different durations, settings, sessions and retries still save");
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
                foreach (ClipSettings chosen in new[] { new ClipSettings(folder, 30, 15, 480, 2), new ClipSettings(folder, 30, 60, 1080, 20) })
                using (ClipBuffer buffer = new ClipBuffer())
                using (Bitmap picture = new Bitmap(1920, 1080))
                {
                    buffer.Configure(chosen);
                    using (Graphics paint = Graphics.FromImage(picture)) { paint.Clear(Color.Navy); paint.FillRectangle(Brushes.Cyan, 160, 180, 480, 300); }
                    foreach (long at in new long[] { 0, 1000000, 4000000, 9000000 }) buffer.AddFrame(picture, at);
                    buffer.Advance(12000000);
                    string path = Path.Combine(folder, chosen.Fps + ".mp4");
                    ClipResult made = buffer.Save(path, 30).GetAwaiter().GetResult();
                    if (made.Unavailable) { checks.Add("LIMIT clip settings encoder: " + made.Problem); return; }
                    if (made.Problem != null) throw new Exception("The chosen clip format failed: " + made.Problem);
                    double length, soundLength;
                    Bitmap decoded;
                    long[] times;
                    bool sound;
                    int peak;
                    ClipMedia.Inspect(path, out length, out decoded, out times, out sound, out soundLength, out peak);
                    using (decoded)
                    {
                        if (decoded.Size != chosen.Fit(1920, 1080) || Math.Abs(length - 1.2) > 0.05 || times.Length != 4
                            || times[0] != 0 || Math.Abs(times[1] - 1000000) > 10000 || Math.Abs(times[2] - 4000000) > 10000 || Math.Abs(times[3] - 9000000) > 10000)
                            throw new Exception("The encoded clip ignored its chosen size or changed irregular timestamps: " + decoded.Size + ", seconds " + length);
                    }
                }
                checks.Add("PASS synthetic clips negotiate480p15/2Mbps and1080p60/20Mbps, reopen at the selected size and retain original uneven sample times");
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
            try
            {
                simpleTest = false;
                mainframe.Restore();
                mainframe.ShowSettings(true);
                mainframe.CheckDisplayDeferral();
                SmokeFullscreenInput(checks);
                mainframe.PickClipOptions(60, 24, 480, 4);
                ClipSettings saved = ClipSettings.Read(LoadPreferences());
                if (saved.Seconds != 60 || saved.Fps != 24 || saved.Resolution != 480 || saved.Mbps != 4 || clips.Settings.Seconds != 60) throw new Exception("The clips panel did not apply and save its recording controls.");
                string customFolder = Path.Combine(output, "clips-folder-with-a-long-name-to-check-the-path-ellipsis");
                Directory.CreateDirectory(customFolder);
                if (SetClipFolder(customFolder) != null || clips.Settings.Folder != customFolder || ClipSettings.Read(LoadPreferences()).Folder != customFolder) throw new Exception("The clips folder did not save and reach the recorder.");
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
            }
            checks.Add("PASS CLIPS has its own Mainframe section in Simple and Advanced Settings at100/150/200percent; recording controls and folder changes save and apply without overlaps");
            checks.Add("PASS fullscreen Mainframe reuses cached panel scaling or defers a cold driver query, including after display-cache invalidation");
            checks.Add("LIMIT live clips: verify the chosen FPS, resolution, bitrate, sound and fullscreen transitions in a real game; no real desktop or audio devices changed by these fixtures");
        }
    }
}
