using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class HerRecorder : IDisposable
    {
        internal const int MostClips = 30, MostFrames = 1800, FrameGap = 250, ShortestFrames = 4;
        internal const long MostBytes = 500L * 1024 * 1024, LongestClip = 60L * 10000000;

        private readonly Func<Bitmap> grab;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly ClipTrack frames = new ClipTrack();
        private readonly ImageCodecInfo jpeg = FindJpeg();
        private readonly Thread worker;
        private volatile bool stopped;
        private int count;
        private long lastEnd;
        internal readonly DateTime Began = DateTime.Now;

        private HerRecorder(Func<Bitmap> grab)
        {
            this.grab = grab;
            worker = new Thread(Loop) { IsBackground = true, Name = "her computer replay", Priority = ThreadPriority.BelowNormal };
        }

        internal int Frames { get { return count; } }

        internal static HerRecorder Begin(Func<Bitmap> grab)
        {
            if (grab == null) return null;
            HerRecorder recorder;
            try { recorder = new HerRecorder(grab); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            catch (InvalidOperationException) { return null; }
            recorder.clock.Start();
            recorder.worker.Start();
            return recorder;
        }

        private void Loop()
        {
            while (!stopped && count < MostFrames)
            {
                long started = clock.ElapsedMilliseconds;
                try { Take(); }
                catch (Exception) { }
                int wait = (int)Math.Max(20, FrameGap - (clock.ElapsedMilliseconds - started));
                for (int slept = 0; slept < wait && !stopped; slept += 25) Thread.Sleep(Math.Min(25, wait - slept));
            }
        }

        private void Take()
        {
            long at = clock.ElapsedTicks * 10000000L / Stopwatch.Frequency;
            byte[] bytes;
            using (Bitmap shot = grab())
            {
                if (shot == null || stopped) return;
                using (MemoryStream stream = new MemoryStream())
                using (EncoderParameters options = new EncoderParameters(1))
                {
                    options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 72L);
                    shot.Save(stream, jpeg, options);
                    bytes = stream.ToArray();
                }
            }
            if (stopped) return;
            frames.Merge(new[] { new ClipPart { Data = bytes, At = at, End = at + FrameGap * 10000L } });
            lastEnd = at + FrameGap * 10000L;
            count++;
        }

        internal void Stop()
        {
            stopped = true;
            if (worker.IsAlive && Thread.CurrentThread != worker) worker.Join(4000);
        }

        internal string Save(string folder, string task)
        {
            Stop();
            if (count < ShortestFrames || lastEnd <= 0) return null;
            try
            {
                Directory.CreateDirectory(folder);
                ClipMedia.Tidy(folder);
                string path = Path.Combine(folder, Name(Began, task));
                double factor = Factor(lastEnd);
                using (ClipSave save = new ClipSave())
                {
                    save.Settings = new ClipSettings(folder, 30, 15, 0, 0);
                    save.Path = path;
                    save.Start = 0;
                    save.End = Math.Max(1, (long)(lastEnd / factor));
                    save.Pictures.Merge(Retime(frames.Read(), factor));
                    ClipResult result = ClipMedia.Save(save);
                    if (result.Problem != null || !File.Exists(path)) return null;
                }
                Prune(folder, MostClips, MostBytes);
                return path;
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Dispose()
        {
            Stop();
            frames.Dispose();
        }

        private static ImageCodecInfo FindJpeg()
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders()) if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
            throw new InvalidOperationException("Windows couldn't open its JPEG encoder.");
        }

        internal static double Factor(long ticks)
        {
            return ticks <= LongestClip ? 1.0 : (double)ticks / LongestClip;
        }

        internal static IEnumerable<ClipPart> Retime(IEnumerable<ClipPart> parts, double factor)
        {
            long last = -1;
            foreach (ClipPart part in parts)
            {
                long at = (long)(part.At / factor), end = (long)(part.End / factor);
                if (at <= last) continue;
                last = at;
                yield return new ClipPart { Data = part.Data, At = at, End = Math.Max(at + 1, end) };
            }
        }

        internal static string Name(DateTime when, string task)
        {
            return when.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + "-" + Slug(task) + ".mp4";
        }

        internal static string Slug(string task)
        {
            StringBuilder slug = new StringBuilder();
            bool dash = false;
            foreach (char c in (task ?? "").ToLowerInvariant())
            {
                if (c >= 'a' && c <= 'z' || c >= '0' && c <= '9')
                {
                    if (dash && slug.Length > 0) slug.Append('-');
                    dash = false;
                    slug.Append(c);
                }
                else dash = true;
                if (slug.Length >= 32) break;
            }
            return slug.Length == 0 ? "job" : slug.ToString();
        }

        internal static List<string> Clips(string folder)
        {
            List<string> found = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) return found;
                foreach (string file in Directory.GetFiles(folder, "*.mp4"))
                    if (!Path.GetFileName(file).StartsWith(".", StringComparison.Ordinal)) found.Add(file);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            found.Sort(StringComparer.OrdinalIgnoreCase);
            return found;
        }

        internal static void Prune(string folder, int most, long mostBytes)
        {
            List<string> clips = Clips(folder);
            long total = 0;
            foreach (string clip in clips) total += Size(clip);
            while (clips.Count > 0 && (clips.Count > most || total > mostBytes))
            {
                string oldest = clips[0];
                clips.RemoveAt(0);
                long size = Size(oldest);
                try { File.Delete(oldest); total -= size; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static long Size(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        internal static string Check(string folder)
        {
            if (Factor(30L * 10000000) != 1.0 || Math.Abs(Factor(120L * 10000000) - 2.0) > 0.0001 || Math.Abs(Factor(600L * 10000000) - 10.0) > 0.0001) return "Her replays sped up a clip that wasn't long, or didn't speed up a long one.";
            List<ClipPart> raw = new List<ClipPart>();
            for (int i = 0; i < 600; i++) raw.Add(new ClipPart { Data = new byte[1], At = i * 2500000L, End = (i + 1) * 2500000L });
            double factor = Factor(raw[raw.Count - 1].End);
            long end = 0;
            int kept = 0;
            foreach (ClipPart part in Retime(raw, factor))
            {
                if (part.End <= part.At) return "Her replays retimed a frame backwards.";
                end = part.End;
                kept++;
            }
            if (kept != 600 || end > LongestClip + 10000000) return "Her replays didn't squeeze a ten minute job into about a minute.";
            if (Name(new DateTime(2026, 11, 19, 21, 5, 9), "Find me a pancake recipe!?") != "20261119-210509-find-me-a-pancake-recipe.mp4") return "Her replays named a clip wrong.";
            if (Slug("???") != "job" || Slug(null) != "job" || Slug("../../etc/passwd:*").IndexOfAny(new[] { '/', '\\', ':', '*', '.' }) >= 0 || Slug(new string('a', 200)).Length != 32) return "Her replay names aren't safe.";
            string clips = Path.Combine(folder, "replays");
            Directory.CreateDirectory(clips);
            for (int i = 0; i < MostClips + 7; i++) File.WriteAllBytes(Path.Combine(clips, Name(new DateTime(2026, 11, 1).AddHours(i), "job " + i)), new byte[16]);
            File.WriteAllBytes(Path.Combine(clips, ".capri-" + Guid.NewGuid().ToString("N") + ".mp4"), new byte[16]);
            if (Clips(clips).Count != MostClips + 7) return "Her replays listed a half-written clip.";
            Prune(clips, MostClips, MostBytes);
            List<string> left = Clips(clips);
            if (left.Count != MostClips || Path.GetFileName(left[0]) != Name(new DateTime(2026, 11, 1).AddHours(7), "job 7") || !File.Exists(Path.Combine(clips, Name(new DateTime(2026, 11, 1).AddHours(MostClips + 6), "job " + (MostClips + 6))))) return "Her replays didn't drop the oldest clips first.";
            Prune(clips, MostClips, 160L);
            if (Clips(clips).Count != 10) return "Her replays didn't drop clips over the size limit.";
            if (Directory.GetFiles(clips, ".capri-*.mp4").Length != 1) return "Her replays pruned a clip that was still being written.";
            string jobs = Path.Combine(folder, "replay-jobs");
            HerJobs list = new HerJobs(jobs);
            HerJob job = list.Add("find a recipe", "");
            list.Next();
            list.Finish(job.Id, "done", "pancakes", "");
            string made = Path.Combine(clips, "x.mp4");
            list.SetReplay(job.Id, made);
            if (list.Done()[0].Replay != made || new HerJobs(jobs).Done()[0].Replay != made) return "Her job lost where its replay is.";
            HerJobList old = new HerJobList { Done = new List<HerJob> { new HerJob { Id = "a", Task = "x", Replay = null } } };
            if (HerJobs.Fixed(old).Done[0].Replay != "") return "Her old jobs didn't come back without a replay.";
            File.WriteAllText(Path.Combine(jobs, "jobs.json"), "{\"Done\":[{\"Id\":\"old\",\"Task\":\"find cats\",\"State\":\"done\"}],\"Queue\":[],\"Repeats\":[]}");
            List<HerJob> older = new HerJobs(jobs).Done();
            if (older.Count != 1 || older[0].Replay != "") return "Her jobs from before replays didn't load.";
            return null;
        }
    }
}
