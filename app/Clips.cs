using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed class ClipBuffer : IDisposable
    {
        internal const int History = 120;
        internal const long Memory = 240L * 1024 * 1024;
        // The newest stretch that stays at full frame rate. A clip is 30 seconds unless they ask for more.
        internal const int Sharp = 45;
        private readonly object gate = new object();
        private readonly object saving = new object();
        private readonly LinkedList<ClipPart> pictures = new LinkedList<ClipPart>();
        private readonly LinkedList<ClipPart> sounds = new LinkedList<ClipPart>();
        private ClipSave pending;
        private readonly Func<ClipSave, ClipResult> encode;
        private readonly Func<long> clock;
        private readonly int history;
        private readonly long memory;
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private Thread capture, writer;
        private bool enabled, disposed;
        private long used, through, thinnedTo = long.MinValue;
        private GameSeen game;
        private int dropped;
        private Bitmap shared;
        private long sharedAt;
        private ClipSettings settings = new ClipSettings();
        private Task<ClipResult> recent;
        private long recentAt, session, recentSession;
        private int recentSeconds;
        private ClipSettings recentSettings;
        internal Func<IntPtr, string> MonitorOf = delegate(IntPtr window)
        {
            return (window == IntPtr.Zero ? System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position) : System.Windows.Forms.Screen.FromHandle(window)).DeviceName;
        };

        internal void Configure(ClipSettings chosen) { lock (gate) settings = chosen; changed.Set(); }
        internal ClipSettings Settings { get { lock (gate) return settings; } }

        internal ClipBuffer() : this(History, Memory) { }

        internal ClipBuffer(int seconds, long bytes, Func<ClipSave, ClipResult> write = null, Func<long> time = null)
        {
            if (seconds <= 0 || bytes <= 0) throw new ArgumentOutOfRangeException("seconds");
            history = seconds;
            memory = bytes;
            encode = write ?? ClipMedia.Save;
            clock = time ?? delegate { return Now; };
        }

        internal bool Enabled { get { lock (gate) return enabled; } }
        internal long Bytes { get { lock (gate) return used; } }
        internal int Pending { get { lock (gate) return pending == null ? 0 : 1; } }
        internal int Frames { get { lock (gate) return pictures.Count; } }
        internal int Dropped { get { lock (gate) return dropped; } }
        internal long First { get { lock (gate) return pictures.Count == 0 ? 0 : pictures.First.Value.At; } }
        internal int FramesSince(long at)
        {
            lock (gate)
            {
                int count = 0;
                for (LinkedListNode<ClipPart> frame = pictures.Last; frame != null && frame.Value.At >= at; frame = frame.Previous) count++;
                return count;
            }
        }
        internal static long Now { get { return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency)); } }

        internal void Enable(bool on)
        {
            lock (gate)
            {
                if (disposed) return;
                enabled = on;
                if (!on) { Clear(); game = null; }
                if (on && capture == null)
                {
                    capture = Worker(Pump, "Capri clip buffer");
                    try { capture.Start(); }
                    catch { capture = null; enabled = false; throw; }
                }
                changed.Set();
            }
        }

        private static Thread Worker(ThreadStart work, string name)
        {
            Thread thread = new Thread(work);
            thread.Name = name;
            thread.IsBackground = true;
            thread.Priority = ThreadPriority.BelowNormal;
            thread.SetApartmentState(ApartmentState.MTA);
            return thread;
        }

        private void Pump()
        {
            FrameGrab grab = null;
            SoundTap tap = null;
            GameSeen front = null;
            Rectangle bounds = Rectangle.Empty;
            long inspectAt = 0, nextAt = 0, soundAt = 0, imageAt = 0;
            double interval = 10000000.0 / 30;
            int wide = 1920;
            bool started = false;
            ClipSettings recording = Settings;
            try
            {
                ClipMedia.Check(ClipMedia.CoInitializeEx(IntPtr.Zero, 0));
                started = true;
                while (true)
                {
                    lock (gate) if (disposed) break;
                    long tick = Now;
                    try
                    {
                        if (tick >= inspectAt)
                        {
                            inspectAt = tick + 2500000;
                            front = Enabled ? GameFinder.Front() : null;
                            ClipSettings chosen = Settings;
                            if (!recording.Same(chosen))
                            {
                                recording = chosen;
                                interval = 10000000.0 / recording.Fps;
                                wide = 0;
                                nextAt = 0;
                            }
                            lock (gate) Trim(tick);
                            if (Follow(front))
                            {
                                if (grab != null) { grab.Dispose(); grab = null; }
                                if (tap != null) { tap.Dispose(); tap = null; }
                                soundAt = 0;
                            }
                        }
                        if (!Enabled || front == null || Native.GetForegroundWindow() != front.Window)
                        {
                            if (grab != null) { grab.Dispose(); grab = null; }
                            if (tap != null) { tap.Dispose(); tap = null; }
                            soundAt = 0;
                            nextAt = 0;
                            imageAt = 0;
                            changed.WaitOne(50);
                            continue;
                        }
                        Rectangle current = GameFinder.Frame(front.Window);
                        if (current.Width < 2 || current.Height < 2) { changed.WaitOne(50); continue; }
                        if (grab == null || current != bounds)
                        {
                            if (grab != null) grab.Dispose();
                            grab = new FrameGrab();
                            grab.Open(current);
                            bounds = current;
                            interval = 10000000.0 / recording.Fps;
                            wide = 0;
                        }
                        if (wide == 0) wide = recording.Fit(current.Width, current.Height).Width;
                        if (tick >= soundAt)
                        {
                            if (tap != null && SoundTap.DefaultDevice() != tap.Device) { tap.Dispose(); tap = null; }
                            if (tap == null) tap = SoundTap.Open(true);
                            soundAt = tick + 20000000;
                        }
                        if (tap != null)
                        {
                            long heardAt;
                            bool read = false;
                            try { read = tap.Read(null, null, out heardAt, Hear); }
                            catch (Exception) { }
                            if (!read) { tap.Dispose(); tap = null; }
                        }
                        if (tick >= nextAt)
                        {
                            long began = Now;
                            if (grab.Next(0) || began - imageAt >= 10000000)
                            {
                                long taken = Now;
                                using (Bitmap picture = grab.Copy(wide))
                                {
                                    Share(picture);
                                    if (picture != null && Native.GetForegroundWindow() == front.Window) { AddFrame(picture, taken, true); imageAt = taken; }
                                }
                            }
                            long cost = Now - began;
                            interval = Math.Max(10000000.0 / recording.Fps, Math.Max(cost * 1.5, interval * 0.98));
                            lock (gate)
                            {
                                if (pictures.Count > 0)
                                {
                                    double room = (pictures.Last.Value.Data.Length + 64.0) * Math.Min(history, Math.Max(Sharp, recording.Seconds)) * 10000000 / (memory * 0.85);
                                    // A busy picture at full size would be a slideshow, and smooth at 720p makes the better clip.
                                    if (recording.Resolution == 0 && room > 10000000.0 / 24 && wide > 1280) wide = Math.Min(wide, new ClipSettings(null, 30, 30, 720).Fit(current.Width, current.Height).Width);
                                    interval = Math.Max(interval, room);
                                }
                                if (enabled) through = Now;
                            }
                            nextAt = Now + (long)Math.Max(1, interval - cost);
                        }
                        changed.WaitOne(Math.Max(1, (int)Math.Min(15, (nextAt - Now) / 10000)));
                    }
                    catch (Exception)
                    {
                        if (grab != null) { grab.Dispose(); grab = null; }
                        if (tap != null) { tap.Dispose(); tap = null; }
                        changed.WaitOne(250);
                    }
                }
            }
            catch (Exception) { }
            finally
            {
                if (grab != null) grab.Dispose();
                if (tap != null) tap.Dispose();
                if (started) ClipMedia.CoUninitialize();
            }
        }

        internal static bool SameGame(GameSeen left, GameSeen right)
        {
            return left != null && right != null && (left.Process > 0 && right.Process > 0 ? left.Process == right.Process : left.Window == right.Window);
        }

        internal bool Follow(GameSeen next)
        {
            lock (gate)
            {
                if (next == null || disposed) return false;
                bool different = game != null && !SameGame(game, next);
                if (different) Clear();
                game = next;
                return different;
            }
        }

        internal void AddFrame(Bitmap picture, long at, bool live = false)
        {
            using (MemoryStream jpeg = new MemoryStream())
            using (EncoderParameters options = new EncoderParameters(1))
            {
                options.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 60L);
                picture.Save(jpeg, Jpeg, options);
                Keep(jpeg.ToArray(), at, at, true, live);
            }
        }

        private static readonly ImageCodecInfo Jpeg = FindJpeg();

        // The game watcher reads these instead of opening a second capture of the same screen.
        private void Share(Bitmap picture)
        {
            if (picture == null || Stopwatch.GetTimestamp() - sharedAt < Stopwatch.Frequency / 8) return;
            sharedAt = Stopwatch.GetTimestamp();
            int wide = Math.Min(480, picture.Width), tall = Math.Max(1, picture.Height * wide / picture.Width);
            Bitmap small = new Bitmap(wide, tall, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(small)) graphics.DrawImage(picture, 0, 0, wide, tall);
            lock (gate)
            {
                if (shared != null) shared.Dispose();
                shared = small;
            }
        }

        internal Bitmap Glimpse()
        {
            lock (gate)
            {
                Bitmap small = shared;
                shared = null;
                return small;
            }
        }

        private static ImageCodecInfo FindJpeg()
        {
            foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders()) if (codec.FormatID == ImageFormat.Jpeg.Guid) return codec;
            throw new InvalidOperationException("Windows couldn't open its JPEG encoder.");
        }

        private void Hear(byte[] pcm, long at, int rate)
        {
            if (!Enabled) return;
            int frames = pcm.Length / 4;
            long end = at + frames * 10000000L / rate;
            if (rate != 48000)
            {
                long startSample = (long)(at * (48000.0 / 10000000));
                int count = (int)((long)(end * (48000.0 / 10000000)) - startSample);
                byte[] resampled = new byte[count * 4];
                for (int output = 0; output < count; output++)
                {
                    int input = Math.Min(frames - 1, (int)((long)output * rate / 48000));
                    Buffer.BlockCopy(pcm, input * 4, resampled, output * 4, 4);
                }
                pcm = resampled;
            }
            Keep(pcm, at, end, false, true);
        }

        internal bool Keep(byte[] data, long at, long end, bool video, bool live = false)
        {
            lock (gate)
            {
                if (disposed || (live && !enabled)) return false;
                Trim(at);
                long size = data.LongLength + 64;
                if (used + size > memory)
                {
                    while (used + size > memory && Thin(at - Math.Max(Sharp, settings.Seconds) * 10000000L)) { }
                    while (used + size > memory && Thin(long.MaxValue)) { }
                    LinkedListNode<ClipPart> last = pictures.Last;
                    while (video && last != null && used + size > memory)
                    {
                        LinkedListNode<ClipPart> before = last.Previous;
                        Release(last.Value); pictures.Remove(last); dropped++;
                        last = before;
                    }
                }
                while (used + size > memory && (pictures.Count > 0 || sounds.Count > 0))
                {
                    bool oldestVideo = sounds.Count == 0 || (pictures.Count > 0 && pictures.First.Value.At <= sounds.First.Value.At);
                    LinkedList<ClipPart> oldest = oldestVideo ? pictures : sounds;
                    Release(oldest.First.Value);
                    oldest.RemoveFirst();
                    if (oldestVideo) dropped++;
                }
                if (used + size > memory) { if (video) dropped++; return false; }
                ClipPart part = new ClipPart { Data = data, At = at, End = end };
                (video ? pictures : sounds).AddLast(part);
                used += size;
                through = Math.Max(through, Math.Max(at, end));
                return true;
            }
        }

        internal void Advance(long at) { lock (gate) { Trim(at); through = at; } }

        // Over budget, footage before that time gives up every other frame, a pass at a time, so a long clip gets choppy at its start instead of shorter.
        private bool Thin(long before)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                LinkedListNode<ClipPart> kept = pictures.First;
                while (kept != null && kept.Value.At <= thinnedTo) kept = kept.Next;
                if (kept != null && kept.Next != null && kept.Next.Next != null && kept.Next.Value.At < before)
                {
                    thinnedTo = kept.Value.At;
                    Release(kept.Next.Value);
                    pictures.Remove(kept.Next);
                    dropped++;
                    return true;
                }
                thinnedTo = long.MinValue;
            }
            return false;
        }

        private void Trim(long at)
        {
            long cutoff = at - history * 10000000L;
            while (pictures.Count > 0 && pictures.First.Value.At < cutoff) { Release(pictures.First.Value); pictures.RemoveFirst(); }
            while (sounds.Count > 0 && sounds.First.Value.End <= cutoff) { Release(sounds.First.Value); sounds.RemoveFirst(); }
        }

        private void Release(ClipPart part) { used -= part.Data.LongLength + 64; }

        private void Clear()
        {
            session++;
            foreach (ClipPart picture in pictures) Release(picture);
            foreach (ClipPart sound in sounds) Release(sound);
            pictures.Clear();
            sounds.Clear();
            if (shared != null) { shared.Dispose(); shared = null; }
            through = 0;
        }

        private static IEnumerable<ClipPart> Since(LinkedListNode<ClipPart> first)
        {
            for (LinkedListNode<ClipPart> part = first; part != null; part = part.Next) yield return part.Value;
        }

        private IEnumerable<ClipPart> Heard(long start, long end)
        {
            foreach (ClipPart sound in sounds) if (sound.End > start && sound.At < end) yield return sound;
        }

        internal Task<ClipResult> Save(string path, int seconds, bool reuse = false, ClipSettings chosen = null)
        {
            bool repeated;
            return Save(path, seconds, reuse, chosen, out repeated);
        }

        internal Task<ClipResult> Save(string path, int seconds, bool reuse, ClipSettings chosen, out bool repeated)
        {
            repeated = false;
            if (seconds <= 0) throw new ArgumentOutOfRangeException("seconds");
            lock (saving)
            {
                List<ClipPart> frames, audio;
                long start, end, capturedSession;
                string monitor;
                lock (gate)
                {
                    chosen = chosen ?? settings;
                    capturedSession = session;
                    if (pictures.Count == 0 || disposed) return Task.FromResult(new ClipResult { Problem = "I haven't buffered any game video yet." });
                    // A repeated voice or tool request gets the same save while it's written and for ten seconds after, even with fresh
                    // settings of the same values, so a slow save or a repeat a few seconds later doesn't make a second file (john
                    // sandevistan, 3 Nov).
                    if (reuse && recent != null && recentSession == session && recentSeconds == seconds && recentSettings.Same(chosen)
                        && (!recent.IsCompleted || (recent.Result.Problem == null && clock() - recentAt < 100000000)))
                    {
                        repeated = true;
                        return recent;
                    }
                    if (pending != null && !pending.Settings.Same(chosen)) return Task.FromResult(new ClipResult { Problem = "Couldn't save that clip: one with the previous recording settings is still queued. Try again once it finishes." });
                    if (pending != null && pending.Session != capturedSession) return Task.FromResult(new ClipResult { Problem = "Couldn't save that clip: one from the previous game is still queued. Try again once it finishes." });
                    end = Math.Max(through, pictures.Last.Value.At + 1);
                    long cutoff = end - seconds * 10000000L;
                    LinkedListNode<ClipPart> first = pictures.First;
                    while (first.Next != null && first.Next.Value.At <= cutoff) first = first.Next;
                    start = Math.Max(cutoff, first.Value.At);
                    frames = new List<ClipPart>(Since(first));
                    audio = new List<ClipPart>(Heard(start, end));
                    monitor = MonitorOf(game == null ? IntPtr.Zero : game.Window);
                }
                ClipSave save = pending;
                try
                {
                    if (save == null) save = new ClipSave { Path = path, Start = start, End = end, Settings = chosen, Session = capturedSession, Monitor = monitor };
                    else { save.Merged = true; save.Start = Math.Min(save.Start, start); save.End = Math.Max(save.End, end); }
                    save.Pictures.Merge(frames);
                    save.Sounds.Merge(audio);
                    lock (gate)
                    {
                        pending = save;
                        if (reuse) { recent = save.Done.Task; recentAt = clock(); recentSession = capturedSession; recentSeconds = seconds; recentSettings = chosen; }
                        if (writer == null)
                        {
                            writer = Worker(Write, "Capri clip writer");
                            try { writer.Start(); } catch { writer = null; throw; }
                        }
                    }
                    return save.Done.Task;
                }
                catch (Exception error)
                {
                    ClipResult failed = new ClipResult { Problem = "Couldn't save that clip: " + Brain.Plainly("clips", error) };
                    lock (gate) pending = null;
                    if (save != null) { save.Dispose(); save.Done.SetResult(failed); }
                    return Task.FromResult(failed);
                }
            }
        }

        private void Write()
        {
            while (true)
            {
                ClipSave save;
                lock (saving)
                lock (gate)
                {
                    if (pending == null) { writer = null; return; }
                    save = pending;
                    pending = null;
                }
                ClipResult result;
                try { result = encode(save); }
                catch (Exception error) { result = new ClipResult { Problem = "Couldn't save that clip: " + Brain.Plainly("clips", error) }; }
                finally { save.Dispose(); }
                result.Path = save.Path;
                result.Merged = save.Merged;
                result.Monitor = save.Monitor;
                lock (gate) if (object.ReferenceEquals(recent, save.Done.Task)) recentAt = clock();
                save.Done.SetResult(result);
            }
        }

        // After a clip saves, off the UI thread: its path, how many seconds, and the game.
        internal Action<string, double, string, string> Saved;

        // Where clips go: Videos\Capri Clips, wherever Windows keeps their Videos.
        internal static string Folder { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Capri Clips"); } }

        // What she says for a saved clip: how long and of what, without the file's path, which the CLIP SAVED popup shows.
        internal static string Spoken(string result)
        {
            if (result == null || !result.StartsWith("Saved ", StringComparison.Ordinal)) return result;
            return "Clipped " + Regex.Replace(result.Substring(6), @": (?:[A-Za-z]:\\|\\\\)[^\r\n]*?\.mp4", ".");
        }

        internal string Clip(int seconds)
        {
            bool saved;
            return Clip(seconds, out saved);
        }

        internal string Clip(int seconds, out bool saved)
        {
            saved = false;
            if (!Enabled) return "Turn on Clip my games in Settings > CLIPS, then I can clip that.";
            ClipSettings recording = Settings;
            if (seconds <= 0) seconds = recording.Seconds;
            string open = Computer.GameOpen();
            lock (gate)
            {
                if (game == null) return "I haven't buffered any game video yet.";
                if (open == null) open = game.Name;
            }
            string title = open;
            foreach (char bad in Path.GetInvalidFileNameChars()) title = title.Replace(bad, '_');
            title = title.Trim().TrimEnd('.');
            if (title.Length == 0) title = "Game";
            if (title.Length > 100) title = title.Substring(0, 100);
            string directory = recording.Folder;
            Directory.CreateDirectory(directory);
            ClipMedia.Tidy(directory);
            string path = Path.Combine(directory, title + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff") + " " + Guid.NewGuid().ToString("N").Substring(0, 6) + ".mp4");
            bool repeated;
            ClipResult result = Save(path, seconds, true, recording, out repeated).GetAwaiter().GetResult();
            saved = result.Problem == null;
            if (saved && !repeated)
            {
                Action<string, double, string, string> told = Saved;
                if (told != null && result.Announce()) told(result.Path, result.Seconds, open, result.Monitor);
            }
            return Report(result, open, repeated);
        }

        internal static string Report(ClipResult result, string game, bool repeated)
        {
            if (result.Problem != null) return result.Problem;
            if (repeated) return "That clip is already saved.";
            return "Saved " + result.Seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " seconds of " + game + (result.Sound ? "" : " without sound") + ": " + result.Path + (result.Merged ? " Merged your clip requests into this clip." : "");
        }

        public void Dispose()
        {
            Thread ending, finishing;
            lock (saving)
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                enabled = false;
                changed.Set();
                ending = capture;
                finishing = writer;
                Clear();
            }
            // Quitting waits a little for a clip that's saving, but never for good (Mika, 1 Nov: "QUITTING DOESNT WORK").
            bool ended = ending == null || ending.Join(2000);
            bool finished = finishing == null || finishing.Join(5000);
            if (ended && finished) changed.Dispose();
        }
    }

    internal sealed class ClipPart
    {
        internal byte[] Data;
        internal long At, End;
    }

    internal sealed class ClipTrack : IDisposable
    {
        private FileStream data = Temporary();
        private long first = long.MaxValue, last = long.MinValue;
        private long tail;
        internal long Count;

        private static FileStream Temporary() { return new FileStream(Path.Combine(Path.GetTempPath(), "Capri-clip-" + Guid.NewGuid().ToString("N")), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 65536, FileOptions.DeleteOnClose); }

        private static void Put(Stream stream, ClipPart part)
        {
            BinaryWriter output = new BinaryWriter(stream, System.Text.Encoding.UTF8, true);
            output.Write(part.At);
            output.Write(part.End);
            output.Write(part.Data.Length);
            output.Write(part.Data);
        }

        internal void Merge(IEnumerable<ClipPart> parts)
        {
            if (Count > 0)
            {
                FileStream prefix = null;
                long earliest = first;
                try
                {
                    foreach (ClipPart prior in parts)
                    {
                        if (prior.At >= earliest) break;
                        if (prefix == null) prefix = Temporary();
                        Put(prefix, prior);
                        first = Math.Min(first, prior.At);
                        Count++;
                    }
                    if (prefix != null)
                    {
                        data.Position = 0;
                        tail += prefix.Length;
                        data.CopyTo(prefix);
                        data.Dispose();
                        data = prefix;
                        prefix = null;
                    }
                }
                finally { if (prefix != null) prefix.Dispose(); }
            }
            data.Position = data.Length;
            foreach (ClipPart newer in parts)
            {
                if (newer.At <= last) continue;
                tail = data.Position;
                Put(data, newer);
                first = Math.Min(first, newer.At);
                last = newer.At;
                Count++;
            }
            data.Flush();
        }

        internal IEnumerable<ClipPart> Read(bool newest = false)
        {
            data.Position = newest ? tail : 0;
            BinaryReader input = new BinaryReader(data, System.Text.Encoding.UTF8, true);
            while (data.Position < data.Length)
            {
                long at = input.ReadInt64(), end = input.ReadInt64();
                int count = input.ReadInt32();
                byte[] bytes = input.ReadBytes(count);
                if (bytes.Length != count) throw new EndOfStreamException();
                yield return new ClipPart { At = at, End = end, Data = bytes };
            }
        }

        public void Dispose() { data.Dispose(); }
    }

    internal sealed class ClipSave : IDisposable
    {
        internal ClipSettings Settings = new ClipSettings();
        internal string Path;
        internal string Monitor;
        internal long Start, End;
        internal long Session;
        internal bool Merged;
        internal readonly ClipTrack Pictures;
        internal readonly ClipTrack Sounds;
        internal readonly TaskCompletionSource<ClipResult> Done = new TaskCompletionSource<ClipResult>();

        internal ClipSave()
        {
            Pictures = new ClipTrack();
            try { Sounds = new ClipTrack(); }
            catch { Pictures.Dispose(); throw; }
        }

        public void Dispose() { Pictures.Dispose(); Sounds.Dispose(); }
    }

    internal sealed class ClipFormat
    {
        internal Size Size;
        internal double Fps, AverageFps, Bitrate;
    }

    internal static class ClipMedia
    {
        private static readonly Guid Major = new Guid("48eba18e-f8c9-4687-bf11-0a74c9f96a8f");
        private static readonly Guid Subtype = new Guid("f7e34c9a-42e8-4714-b74b-cb29d72c35e5");
        private static readonly Guid Video = new Guid("73646976-0000-0010-8000-00aa00389b71");
        private static readonly Guid Audio = new Guid("73647561-0000-0010-8000-00aa00389b71");
        private static readonly Guid H264 = new Guid("34363248-0000-0010-8000-00aa00389b71");
        private static readonly Guid Nv12 = new Guid("3231564e-0000-0010-8000-00aa00389b71");
        private static readonly Guid Rgb32 = new Guid("00000016-0000-0010-8000-00aa00389b71");
        private static readonly Guid Aac = new Guid("00001610-0000-0010-8000-00aa00389b71");
        private static readonly Guid Pcm = new Guid("00000001-0000-0010-8000-00aa00389b71");
        private static readonly Guid FrameSize = new Guid("1652c33d-d6b2-4012-b834-72030849a37d");
        private static readonly Guid Stride = new Guid("644b4e48-1e02-4516-b0eb-c01ca9d49ac6");
        private const int FirstVideo = unchecked((int)0xfffffffc);
        private const int FirstAudio = unchecked((int)0xfffffffd);

        // A quit that stops waiting on a clip still saving leaves its half-written file. The next clip clears any that are over
        // an hour old, so one saving right now stays.
        internal static void Tidy(string directory)
        {
            try
            {
                foreach (string left in Directory.GetFiles(directory, ".capri-*.mp4"))
                {
                    string name = Path.GetFileName(left);
                    if (!Regex.IsMatch(name, @"\A\.capri-[0-9a-f]{32}\.mp4\z", RegexOptions.IgnoreCase)) continue;
                    try { if (DateTime.UtcNow - File.GetLastWriteTimeUtc(left) > TimeSpan.FromHours(1)) File.Delete(left); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static ClipResult Save(ClipSave save)
        {
            bool initialized = false, started = false, sound = save.Sounds.Count > 0;
            string temporary = null;
            try
            {
                Check(CoInitializeEx(IntPtr.Zero, 0));
                initialized = true;
                Check(MFStartup(0x20070, 0));
                started = true;
                string full = Path.GetFullPath(save.Path);
                temporary = Path.Combine(Path.GetDirectoryName(full), ".capri-" + Guid.NewGuid().ToString("N") + ".mp4");
                try { Encode(temporary, save, sound); }
                catch (ClipSoundException)
                {
                    File.Delete(temporary);
                    sound = false;
                    Encode(temporary, save, false);
                }
                File.Move(temporary, full);
                temporary = null;
                return new ClipResult { Sound = sound, Seconds = (save.End - save.Start) / 10000000.0 };
            }
            catch (DllNotFoundException) { return new ClipResult { Problem = "Windows Media Foundation isn't installed on this PC, so I couldn't save a video.", Unavailable = true }; }
            catch (COMException error)
            {
                bool missing = error.ErrorCode == unchecked((int)0xc00d5212) || error.ErrorCode == unchecked((int)0x80040154);
                if (!missing) Brain.Log("clips", -1, "Media Foundation 0x" + error.ErrorCode.ToString("X8") + ": " + error.Message);
                return new ClipResult { Problem = missing ? "Windows couldn't find an H.264 encoder on this PC, so I couldn't save a video." : "Windows couldn't encode that clip. Try again.", Unavailable = missing };
            }
            catch (Exception error) { return new ClipResult { Problem = "Couldn't save that clip: " + Brain.Plainly("clips", error) }; }
            finally
            {
                if (temporary != null) { try { File.Delete(temporary); } catch (Exception) { } }
                if (started) MFShutdown();
                if (initialized) CoUninitialize();
            }
        }

        private static void Encode(string path, ClipSave save, bool sound)
        {
            IMFSinkWriter sink = null;
            IMFMediaType target = null, input = null, audioTarget = null, audioInput = null;
            IMFAttributes options = null;
            int width, height;
            // A chosen resolution goes by the newest picture: pictures from before they raised it kept clips small (john
            // sandevistan, 3 Nov). Auto still goes by the first.
            using (IEnumerator<ClipPart> opening = save.Pictures.Read(save.Settings.Resolution != 0).GetEnumerator())
            {
                if (!opening.MoveNext()) throw new InvalidDataException("The clip has no pictures.");
                using (MemoryStream firstBytes = new MemoryStream(opening.Current.Data, false))
                using (Bitmap first = new Bitmap(firstBytes))
                {
                    Size size = save.Settings.Fit(first.Width, first.Height);
                    width = size.Width;
                    height = size.Height;
                }
            }
            try
            {
                Check(MFCreateAttributes(out options, 2));
                Number(options, "9c27891a-ed7a-40e1-88e8-b22727a024ee", 1);
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, options, out sink));
                target = VideoType(H264, width, height, save.Settings.Fps);
                Number((IMFAttributes)target, "20332624-fb0d-4d9e-bd0d-cbf6786c102e", save.Settings.Bits(width, height));
                int videoStream;
                Check(sink.AddStream(target, out videoStream));
                input = VideoType(Nv12, width, height, save.Settings.Fps);
                Check(((IMFAttributes)input).SetUINT32(Stride, width));
                Number(options, "b0c8bf60-16f7-4951-a30b-1db1609293d6", 1);
                Check(sink.SetInputMediaType(videoStream, input, options));
                int audioStream = -1;
                if (sound)
                {
                    try
                    {
                        audioTarget = AudioType(Aac);
                        Number((IMFAttributes)audioTarget, "1aab75c8-cfef-451c-ab95-ac034b8e1731", 20000);
                        Check(sink.AddStream(audioTarget, out audioStream));
                        audioInput = AudioType(Pcm);
                        Number((IMFAttributes)audioInput, "322de230-9eeb-43bd-ab7a-ff412251541d", 4);
                        Number((IMFAttributes)audioInput, "1aab75c8-cfef-451c-ab95-ac034b8e1731", 192000);
                        Check(sink.SetInputMediaType(audioStream, audioInput, null));
                    }
                    catch (COMException soundError) { throw new ClipSoundException(soundError); }
                }
                Check(sink.BeginWriting());
                // Frames go in at the chosen frame rate, each the newest picture by then. At the capture's own times a clip had the
                // capture's slower rate whatever was chosen, and the encoder, which shares the bitrate out per frame at the chosen rate,
                // came in under it: a 60 fps 20 Mbps clip captured at 30 saved at about 10 Mbps (john sandevistan, 3 Nov). Held pictures
                // add no new motion.
                long audioAt = 0, videoAt = 0, duration = save.End - save.Start;
                long videoFrames = (duration * save.Settings.Fps + 9999999) / 10000000;
                long audioFrames = (save.End - save.Start) * 48000 / 10000000;
                using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppRgb))
                using (IEnumerator<ClipPart> videoParts = save.Pictures.Read().GetEnumerator())
                using (IEnumerator<ClipPart> soundParts = save.Sounds.Read().GetEnumerator())
                {
                    ClipPart picture = videoParts.MoveNext() ? videoParts.Current : null;
                    ClipPart following = videoParts.MoveNext() ? videoParts.Current : null;
                    ClipPart rendered = null;
                    byte[] frame = null;
                    ClipPart heard = sound && soundParts.MoveNext() ? soundParts.Current : null;
                    while (videoAt < videoFrames || (sound && audioAt < audioFrames))
                    {
                        long videoTime = videoAt < videoFrames ? videoAt * 10000000 / save.Settings.Fps : long.MaxValue;
                        long audioTime = audioAt * 10000000 / 48000;
                        if (sound && audioAt < audioFrames && audioTime < videoTime)
                        {
                            int amount = (int)Math.Min(4800, audioFrames - audioAt);
                            byte[] pcm = new byte[amount * 4];
                            long packetStart = save.Start + audioTime;
                            long packetEnd = save.Start + (audioAt + amount) * 10000000 / 48000;
                            while (heard != null && heard.End <= packetStart) heard = soundParts.MoveNext() ? soundParts.Current : null;
                            while (heard != null && heard.At < packetEnd)
                            {
                                long offset = (long)Math.Round((heard.At - save.Start) * (48000.0 / 10000000)) - audioAt;
                                int source = (int)Math.Max(0, -offset);
                                int destination = (int)Math.Max(0, offset);
                                int copied = Math.Min(heard.Data.Length / 4 - source, amount - destination);
                                if (copied > 0) Buffer.BlockCopy(heard.Data, source * 4, pcm, destination * 4, copied * 4);
                                if (heard.End > packetEnd) break;
                                heard = soundParts.MoveNext() ? soundParts.Current : null;
                            }
                            try { WriteSample(sink, audioStream, pcm, audioTime, (audioAt + amount) * 10000000 / 48000 - audioTime); }
                            catch (COMException soundError) { throw new ClipSoundException(soundError); }
                            audioAt += amount;
                        }
                        else
                        {
                            while (following != null && following.At - save.Start <= videoTime)
                            {
                                picture = following;
                                following = videoParts.MoveNext() ? videoParts.Current : null;
                            }
                            if (!object.ReferenceEquals(rendered, picture))
                            {
                                using (MemoryStream bytes = new MemoryStream(picture.Data, false))
                                using (Bitmap decoded = new Bitmap(bytes))
                                {
                                    // A frame already the clip's size is read as it is; drawing it onto the canvas first cost about 2.5 ms
                                    // more a frame, a couple of seconds on every 30 s clip.
                                    if (decoded.Width == width && decoded.Height == height) frame = Pixels(decoded);
                                    else
                                    {
                                        using (Graphics drawing = Graphics.FromImage(canvas))
                                        {
                                            drawing.Clear(Color.Black);
                                            double scale = Math.Min((double)width / decoded.Width, (double)height / decoded.Height);
                                            int across = (int)(decoded.Width * scale), down = (int)(decoded.Height * scale);
                                            drawing.DrawImage(decoded, new Rectangle((width - across) / 2, (height - down) / 2, across, down));
                                        }
                                        frame = Pixels(canvas);
                                    }
                                }
                                rendered = picture;
                            }
                            long end = Math.Min(duration, (videoAt + 1) * 10000000 / save.Settings.Fps);
                            WriteSample(sink, videoStream, frame, videoTime, Math.Max(1, end - videoTime));
                            videoAt++;
                        }
                    }
                }
                try { Check(sink.Finish()); }
                catch (COMException finalError) { if (sound) throw new ClipSoundException(finalError); throw; }
            }
            finally
            {
                Drop(audioInput);
                Drop(audioTarget);
                Drop(input);
                Drop(target);
                Drop(sink);
                Drop(options);
            }
        }

        private static IMFMediaType VideoType(Guid subtype, int width, int height, int fps)
        {
            IMFMediaType type;
            Check(MFCreateMediaType(out type));
            try
            {
                IMFAttributes attributes = (IMFAttributes)type;
                Check(attributes.SetGUID(Major, Video));
                Check(attributes.SetGUID(Subtype, subtype));
                Check(attributes.SetUINT64(FrameSize, ((long)width << 32) | (uint)height));
                Check(attributes.SetUINT64(new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"), ((long)fps << 32) | 1));
                Check(attributes.SetUINT64(new Guid("c6376a1e-8d0a-4027-be45-6d9a0ad39bb6"), (1L << 32) | 1));
                Number(attributes, "e2724bb8-e676-4806-b4b2-a8d6efb44ccd", 2);
                return type;
            }
            catch { Drop(type); throw; }
        }

        private static IMFMediaType AudioType(Guid subtype)
        {
            IMFMediaType type;
            Check(MFCreateMediaType(out type));
            try
            {
                IMFAttributes attributes = (IMFAttributes)type;
                Check(attributes.SetGUID(Major, Audio));
                Check(attributes.SetGUID(Subtype, subtype));
                Number(attributes, "37e48bf5-645e-4c5b-89de-ada9e29b696a", 2);
                Number(attributes, "5faeeae7-0290-4c31-9e8a-c534f68d9dba", 48000);
                Number(attributes, "f2deb57f-40fa-4764-aa33-ed4f2d1ff669", 16);
                return type;
            }
            catch { Drop(type); throw; }
        }

        private static void Number(IMFAttributes attributes, string key, int value) { Check(attributes.SetUINT32(new Guid(key), value)); }

        private static unsafe byte[] Pixels(Bitmap picture)
        {
            int width = picture.Width, height = picture.Height;
            byte[] bytes = new byte[width * height * 3 / 2];
            BitmapData locked = picture.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            try
            {
                for (int row = 0; row < height; row += 2)
                    for (int column = 0; column < width; column += 2)
                    {
                        int red = 0, green = 0, blue = 0;
                        for (int localY = 0; localY < 2; localY++)
                            for (int localX = 0; localX < 2; localX++)
                            {
                                byte* pixel = (byte*)locked.Scan0 + (row + localY) * locked.Stride + (column + localX) * 4;
                                int r = pixel[2], g = pixel[1], b = pixel[0];
                                bytes[(row + localY) * width + column + localX] = Channel(((66 * r + 129 * g + 25 * b + 128) >> 8) + 16);
                                red += r; green += g; blue += b;
                            }
                        int chroma = width * height + row / 2 * width + column;
                        bytes[chroma] = Channel(((-38 * red - 74 * green + 112 * blue + 512) >> 10) + 128);
                        bytes[chroma + 1] = Channel(((112 * red - 94 * green - 18 * blue + 512) >> 10) + 128);
                    }
            }
            finally { picture.UnlockBits(locked); }
            return bytes;
        }

        private static byte Channel(int value) { return (byte)Math.Max(0, Math.Min(255, value)); }

        private static void WriteSample(IMFSinkWriter writer, int stream, byte[] bytes, long at, long duration)
        {
            IMFMediaBuffer buffer = null;
            IMFSample sample = null;
            try
            {
                Check(MFCreateMemoryBuffer(bytes.Length, out buffer));
                IntPtr data;
                int maximum, current;
                Check(buffer.Lock(out data, out maximum, out current));
                try { Marshal.Copy(bytes, 0, data, bytes.Length); }
                finally { Check(buffer.Unlock()); }
                Check(buffer.SetCurrentLength(bytes.Length));
                Check(MFCreateSample(out sample));
                Check(sample.AddBuffer(buffer));
                Check(sample.SetSampleTime(at));
                Check(sample.SetSampleDuration(duration));
                Check(writer.WriteSample(stream, sample));
            }
            finally { Drop(sample); Drop(buffer); }
        }

        internal static ClipFormat Inspect(string path)
        {
            IMFSourceReader reader = null;
            IMFMediaType type = null;
            bool initialized = false, started = false;
            try
            {
                Check(CoInitializeEx(IntPtr.Zero, 0));
                initialized = true;
                Check(MFStartup(0x20070, 0));
                started = true;
                Check(MFCreateSourceReaderFromURL(path, null, out reader));
                Check(reader.GetNativeMediaType(FirstVideo, 0, out type));
                long size, rate;
                Check(((IMFAttributes)type).GetUINT64(FrameSize, out size));
                Check(((IMFAttributes)type).GetUINT64(new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"), out rate));
                if ((uint)rate == 0) throw new InvalidDataException("The clip has no frame rate.");
                ClipFormat format = new ClipFormat { Size = new Size((int)(size >> 32), (int)size), Fps = (uint)(rate >> 32) / (double)(uint)rate };
                Check(reader.SetStreamSelection(unchecked((int)0xfffffffe), false));
                Check(reader.SetStreamSelection(FirstVideo, true));
                Check(reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, type));
                long bytes = 0, frames = 0, first = long.MaxValue, end = long.MinValue;
                while (true)
                {
                    IMFSample sample;
                    IMFMediaBuffer buffer = null;
                    int stream, flags;
                    long at;
                    Check(reader.ReadSample(FirstVideo, 0, out stream, out flags, out at, out sample));
                    try
                    {
                        if (sample != null)
                        {
                            long duration;
                            int length;
                            Check(sample.GetSampleDuration(out duration));
                            Check(sample.ConvertToContiguousBuffer(out buffer));
                            Check(buffer.GetCurrentLength(out length));
                            bytes += length;
                            frames++;
                            first = Math.Min(first, at);
                            end = Math.Max(end, at + duration);
                        }
                    }
                    finally { Drop(buffer); Drop(sample); }
                    if ((flags & 2) != 0) break;
                }
                if (frames == 0 || end <= first) throw new InvalidDataException("The clip has no video samples.");
                format.AverageFps = frames * 10000000.0 / (end - first);
                format.Bitrate = bytes * 80000000.0 / (end - first);
                return format;
            }
            finally
            {
                Drop(type); Drop(reader);
                if (started) MFShutdown();
                if (initialized) CoUninitialize();
            }
        }

        internal static void Inspect(string path, out double seconds, out Bitmap first, out long[] times, out bool sound, out double audioSeconds, out int peak)
        {
            IMFSourceReader reader = null;
            IMFAttributes options = null;
            IMFMediaType output = null, actual = null, audioType = null;
            bool initialized = false, started = false;
            first = null;
            seconds = 0;
            times = null;
            sound = false;
            audioSeconds = 0;
            peak = 0;
            try
            {
                Check(CoInitializeEx(IntPtr.Zero, 0));
                initialized = true;
                Check(MFStartup(0x20070, 0));
                started = true;
                Check(MFCreateAttributes(out options, 1));
                Number(options, "fb394f3d-ccf1-42ee-bbb3-f9b845d5681d", 1);
                Check(MFCreateSourceReaderFromURL(path, options, out reader));
                PropVariant duration;
                Check(reader.GetPresentationAttribute(unchecked((int)0xffffffff), new Guid("6c990d33-bb8e-477a-8598-0d5d96fcd88a"), out duration));
                try { if (duration.Kind != 21) throw new InvalidDataException("The clip has no duration."); seconds = duration.Value / 10000000.0; }
                finally { PropVariantClear(ref duration); }
                Check(reader.SetStreamSelection(unchecked((int)0xfffffffe), false));
                Check(reader.SetStreamSelection(FirstVideo, true));
                Check(MFCreateMediaType(out output));
                Check(((IMFAttributes)output).SetGUID(Major, Video));
                Check(((IMFAttributes)output).SetGUID(Subtype, Rgb32));
                Check(reader.SetCurrentMediaType(FirstVideo, IntPtr.Zero, output));
                Check(reader.GetCurrentMediaType(FirstVideo, out actual));
                long size;
                Check(((IMFAttributes)actual).GetUINT64(FrameSize, out size));
                int width = (int)(size >> 32), height = (int)size, stride;
                if (((IMFAttributes)actual).GetUINT32(Stride, out stride) < 0) stride = width * 4;
                List<long> stamps = new List<long>();
                while (true)
                {
                    IMFSample sample;
                    int stream, flags;
                    long at;
                    Check(reader.ReadSample(FirstVideo, 0, out stream, out flags, out at, out sample));
                    try
                    {
                        if (sample != null)
                        {
                            stamps.Add(at);
                            if (first == null)
                            {
                                Drop(actual);
                                actual = null;
                                Check(reader.GetCurrentMediaType(FirstVideo, out actual));
                                if (((IMFAttributes)actual).GetUINT32(Stride, out stride) < 0) stride = width * 4;
                                first = Picture(sample, width, height, stride);
                            }
                        }
                    }
                    finally { Drop(sample); }
                    if ((flags & 2) != 0) break;
                }
                times = stamps.ToArray();
                if (reader.GetNativeMediaType(FirstAudio, 0, out audioType) >= 0)
                {
                    Guid format;
                    Check(((IMFAttributes)audioType).GetGUID(Subtype, out format));
                    if (format != Aac) throw new InvalidDataException("The clip's sound isn't AAC.");
                    sound = true;
                    Drop(audioType);
                    audioType = AudioType(Pcm);
                    Number((IMFAttributes)audioType, "322de230-9eeb-43bd-ab7a-ff412251541d", 4);
                    Number((IMFAttributes)audioType, "1aab75c8-cfef-451c-ab95-ac034b8e1731", 192000);
                    Check(reader.SetStreamSelection(FirstVideo, false));
                    Check(reader.SetStreamSelection(FirstAudio, true));
                    Check(reader.SetCurrentMediaType(FirstAudio, IntPtr.Zero, audioType));
                    PropVariant beginning = new PropVariant { Kind = 20, Value = 0 };
                    Check(reader.SetCurrentPosition(Guid.Empty, ref beginning));
                    long previous = long.MinValue;
                    while (true)
                    {
                        IMFSample audioSample;
                        int audioStream, audioFlags;
                        long audioTime;
                        Check(reader.ReadSample(FirstAudio, 0, out audioStream, out audioFlags, out audioTime, out audioSample));
                        IMFMediaBuffer audioBuffer = null;
                        try
                        {
                            if (audioSample != null)
                            {
                                if (audioTime < previous) throw new InvalidDataException("The clip's sound ran backwards.");
                                previous = audioTime;
                                long audioDuration;
                                Check(audioSample.GetSampleDuration(out audioDuration));
                                audioSeconds = Math.Max(audioSeconds, (audioTime + audioDuration) / 10000000.0);
                                Check(audioSample.ConvertToContiguousBuffer(out audioBuffer));
                                IntPtr audioData;
                                int audioMaximum, audioLength;
                                Check(audioBuffer.Lock(out audioData, out audioMaximum, out audioLength));
                                try
                                {
                                    for (int sampleAt = 0; sampleAt + 1 < audioLength; sampleAt += 2) peak = Math.Max(peak, Math.Abs((int)Marshal.ReadInt16(audioData, sampleAt)));
                                }
                                finally { Check(audioBuffer.Unlock()); }
                            }
                        }
                        finally { Drop(audioBuffer); Drop(audioSample); }
                        if ((audioFlags & 2) != 0) break;
                    }
                }
            }
            catch { if (first != null) { first.Dispose(); first = null; } throw; }
            finally
            {
                Drop(audioType); Drop(actual); Drop(output); Drop(reader); Drop(options);
                if (started) MFShutdown();
                if (initialized) CoUninitialize();
            }
        }

        private static Bitmap Picture(IMFSample sample, int width, int height, int stride)
        {
            IMFMediaBuffer buffer = null;
            IMF2DBuffer surface = null;
            Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
            try
            {
                Check(sample.GetBufferByIndex(0, out buffer));
                surface = buffer as IMF2DBuffer;
                IntPtr data;
                int maximum = 0, current = 0;
                if (surface != null) Check(surface.Lock2D(out data, out stride));
                else
                {
                    Check(buffer.Lock(out data, out maximum, out current));
                    if (stride < 0) data = IntPtr.Add(data, (height - 1) * -stride);
                }
                try
                {
                    if (surface == null && current < (long)Math.Abs(stride) * (height - 1) + width * 4) throw new InvalidDataException("The decoded frame has the wrong stride.");
                    BitmapData locked = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        byte[] rowBytes = new byte[width * 4];
                        for (int row = 0; row < height; row++)
                        {
                            Marshal.Copy(IntPtr.Add(data, row * stride), rowBytes, 0, rowBytes.Length);
                            Marshal.Copy(rowBytes, 0, IntPtr.Add(locked.Scan0, row * locked.Stride), rowBytes.Length);
                        }
                    }
                    finally { bitmap.UnlockBits(locked); }
                }
                finally { if (surface != null) Check(surface.Unlock2D()); else Check(buffer.Unlock()); }
                return bitmap;
            }
            catch { bitmap.Dispose(); throw; }
            finally { Drop(buffer); }
        }

        internal static void Check(int result) { if (result < 0) Marshal.ThrowExceptionForHR(result); }
        private static void Drop(object value) { if (value != null) Marshal.ReleaseComObject(value); }

        [DllImport("ole32.dll")] internal static extern int CoInitializeEx(IntPtr reserved, int kind);
        [DllImport("ole32.dll")] internal static extern void CoUninitialize();
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFStartup(int version, int flags);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFShutdown();
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateAttributes(out IMFAttributes attributes, int size);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMediaType(out IMFMediaType type);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateSample(out IMFSample sample);
        [DllImport("mfplat.dll", ExactSpelling = true)] private static extern int MFCreateMemoryBuffer(int size, out IMFMediaBuffer buffer);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int MFCreateSinkWriterFromURL(string url, IntPtr stream, IMFAttributes attributes, out IMFSinkWriter writer);
        [DllImport("mfreadwrite.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int MFCreateSourceReaderFromURL(string url, IMFAttributes attributes, out IMFSourceReader reader);

        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct PropVariant
        {
            [FieldOffset(0)] internal short Kind;
            [FieldOffset(8)] internal long Value;
        }


        [ComImport, Guid("2cd2d921-c447-44a7-a13c-4adabfc247e3"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFAttributes
        {
            [PreserveSig] int GetItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
            [PreserveSig] int GetItemType([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int type);
            [PreserveSig] int CompareItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value, out int equal);
            [PreserveSig] int Compare(IMFAttributes other, int match, out int equal);
            [PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
            [PreserveSig] int GetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);
            [PreserveSig] int GetDouble([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out double value);
            [PreserveSig] int GetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);
            [PreserveSig] int GetStringLength([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int length);
            [PreserveSig] int GetString([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr text, int size, out int length);
            [PreserveSig] int GetAllocatedString([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr text, out int length);
            [PreserveSig] int GetBlobSize([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int size);
            [PreserveSig] int GetBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr data, int size, out int length);
            [PreserveSig] int GetAllocatedBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr data, out int size);
            [PreserveSig] int GetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IntPtr value);
            [PreserveSig] int SetItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
            [PreserveSig] int DeleteItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
            [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
            [PreserveSig] int SetDouble([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, double value);
            [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
            [PreserveSig] int SetString([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr data, int size);
            [PreserveSig] int SetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IMFAttributes other);
        }

        [ComImport, Guid("44ae0fa8-ea31-4109-8d2e-4cae4997c555"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaType { }

        [ComImport, Guid("c40a00f2-b93a-4d80-ae8c-5a1c634f58e4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSample
        {
            [PreserveSig] int GetItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
            [PreserveSig] int GetItemType([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int type);
            [PreserveSig] int CompareItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value, out int equal);
            [PreserveSig] int Compare(IMFAttributes other, int match, out int equal);
            [PreserveSig] int GetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int value);
            [PreserveSig] int GetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out long value);
            [PreserveSig] int GetDouble([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out double value);
            [PreserveSig] int GetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out Guid value);
            [PreserveSig] int GetStringLength([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int length);
            [PreserveSig] int GetString([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr text, int size, out int length);
            [PreserveSig] int GetAllocatedString([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr text, out int length);
            [PreserveSig] int GetBlobSize([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out int size);
            [PreserveSig] int GetBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr data, int size, out int length);
            [PreserveSig] int GetAllocatedBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out IntPtr data, out int size);
            [PreserveSig] int GetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IntPtr value);
            [PreserveSig] int SetItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr value);
            [PreserveSig] int DeleteItem([In, MarshalAs(UnmanagedType.LPStruct)] Guid key);
            [PreserveSig] int DeleteAllItems();
            [PreserveSig] int SetUINT32([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, int value);
            [PreserveSig] int SetUINT64([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, long value);
            [PreserveSig] int SetDouble([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, double value);
            [PreserveSig] int SetGUID([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [In, MarshalAs(UnmanagedType.LPStruct)] Guid value);
            [PreserveSig] int SetString([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.LPWStr)] string value);
            [PreserveSig] int SetBlob([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, IntPtr data, int size);
            [PreserveSig] int SetUnknown([In, MarshalAs(UnmanagedType.LPStruct)] Guid key, [MarshalAs(UnmanagedType.IUnknown)] object value);
            [PreserveSig] int LockStore();
            [PreserveSig] int UnlockStore();
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int GetItemByIndex(int index, out Guid key, IntPtr value);
            [PreserveSig] int CopyAllItems(IMFAttributes other);
            [PreserveSig] int GetSampleFlags(out int flags);
            [PreserveSig] int SetSampleFlags(int flags);
            [PreserveSig] int GetSampleTime(out long time);
            [PreserveSig] int SetSampleTime(long time);
            [PreserveSig] int GetSampleDuration(out long duration);
            [PreserveSig] int SetSampleDuration(long duration);
            [PreserveSig] int GetBufferCount(out int count);
            [PreserveSig] int GetBufferByIndex(int index, out IMFMediaBuffer buffer);
            [PreserveSig] int ConvertToContiguousBuffer(out IMFMediaBuffer buffer);
            [PreserveSig] int AddBuffer(IMFMediaBuffer buffer);
        }

        [ComImport, Guid("045fa593-8799-42b8-bc8d-8968c6453507"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFMediaBuffer
        {
            [PreserveSig] int Lock(out IntPtr data, out int maximum, out int current);
            [PreserveSig] int Unlock();
            [PreserveSig] int GetCurrentLength(out int length);
            [PreserveSig] int SetCurrentLength(int length);
            [PreserveSig] int GetMaxLength(out int length);
        }

        [ComImport, Guid("7dc9d5f9-9ed9-44ec-9bbf-0600bb589fbb"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMF2DBuffer
        {
            [PreserveSig] int Lock2D(out IntPtr scanline, out int pitch);
            [PreserveSig] int Unlock2D();
        }

        [ComImport, Guid("3137f1cd-fe5e-4805-a5d8-fb477448cb3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSinkWriter
        {
            [PreserveSig] int AddStream(IMFMediaType type, out int stream);
            [PreserveSig] int SetInputMediaType(int stream, IMFMediaType type, IMFAttributes parameters);
            [PreserveSig] int BeginWriting();
            [PreserveSig] int WriteSample(int stream, IMFSample sample);
            [PreserveSig] int SendStreamTick(int stream, long time);
            [PreserveSig] int PlaceMarker(int stream, IntPtr context);
            [PreserveSig] int NotifyEndOfSegment(int stream);
            [PreserveSig] int Flush(int stream);
            [PreserveSig] int Finish();
        }

        [ComImport, Guid("70ae66f2-c809-4e4f-8915-bdcb406b7993"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMFSourceReader
        {
            [PreserveSig] int GetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] out bool selected);
            [PreserveSig] int SetStreamSelection(int stream, [MarshalAs(UnmanagedType.Bool)] bool selected);
            [PreserveSig] int GetNativeMediaType(int stream, int index, out IMFMediaType type);
            [PreserveSig] int GetCurrentMediaType(int stream, out IMFMediaType type);
            [PreserveSig] int SetCurrentMediaType(int stream, IntPtr reserved, IMFMediaType type);
            [PreserveSig] int SetCurrentPosition([In, MarshalAs(UnmanagedType.LPStruct)] Guid format, ref PropVariant position);
            [PreserveSig] int ReadSample(int stream, int control, out int actual, out int flags, out long time, out IMFSample sample);
            [PreserveSig] int Flush(int stream);
            [PreserveSig] int GetServiceForStream(int stream, [In, MarshalAs(UnmanagedType.LPStruct)] Guid service, [In, MarshalAs(UnmanagedType.LPStruct)] Guid iid, out IntPtr value);
            [PreserveSig] int GetPresentationAttribute(int stream, [In, MarshalAs(UnmanagedType.LPStruct)] Guid key, out PropVariant value);
        }

        private sealed class ClipSoundException : Exception
        {
            internal ClipSoundException(Exception cause) : base("Windows couldn't encode the clip's sound.", cause) { }
        }
    }

    // Saving a clip pops this up at the top right of the game's screen with a short chime, the way Medal does: CLIP SAVED, how
    // long and of what, and where it went. Clicking it shows the clip in its folder, and hovering holds it up (Lachy, 2 Nov:
    // "a sound confirmation when it's clipped and like a little popup in the top right or something kinda like a medal clip").
    internal sealed class ClipToast : Overlay
    {
        internal const int Shown = 4000, Slide = 160, Fade = 320, Step = 16;
        private static SoundPlayer chime;
        private readonly System.Windows.Forms.Timer ticks = new System.Windows.Forms.Timer();
        private Bitmap plate;
        private string saved, game;
        private double seconds;
        private float scale = 1;
        private Point home;
        private int age;
        private bool hovered;
        internal bool Motion = true;
        internal event Action<string> Open;

        internal ClipToast() : base("Clip saved")
        {
            Cursor = Look.Pointer;
            ticks.Interval = Step;
            ticks.Tick += delegate { Age(Step); };
        }

        internal string Saved { get { return saved; } }

        internal static Size Measure(float s) { return new Size((int)Math.Round(286 * s), (int)Math.Round(78 * s)); }

        // Its spot once it's in: the top right of the work area, clear of the edges.
        internal static Point Corner(Rectangle area, float s)
        {
            Size size = Measure(s);
            int gap = (int)Math.Round(18 * s);
            return new Point(area.Right - size.Width - gap, area.Top + gap);
        }

        internal void Pop(string clip, double length, string title, Rectangle area)
        {
            saved = clip;
            seconds = length;
            game = string.IsNullOrEmpty(title) ? "your game" : title;
            scale = ScaleOf(area);
            home = Corner(area, scale);
            age = Motion ? 0 : Slide;
            hovered = false;
            Render();
            Redraw();
            ticks.Stop();
            ticks.Start();
        }

        // Its life in milliseconds: it slides in from the right, holds for Shown or while hovered, then fades out and hides.
        internal void Age(int ms)
        {
            if (!Visible) { ticks.Stop(); return; }
            if (hovered && age >= Slide) return;
            age += ms;
            if (age >= Slide + Shown + (Motion ? Fade : 0)) { ticks.Stop(); Hide(); return; }
            if (age <= Slide + ms || age > Slide + Shown) Redraw();
        }

        internal void Hovering(bool on)
        {
            hovered = on;
            if (on && age > Slide) age = Slide;
            Render();
            Redraw();
        }

        // A click shows the clip, and it's done.
        internal void Choose()
        {
            ticks.Stop();
            Hide();
            Action<string> open = Open;
            if (open != null && saved != null) open(saved);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); Hovering(true); }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); Hovering(false); }

        protected override void OnMouseUp(System.Windows.Forms.MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == System.Windows.Forms.MouseButtons.Left) Choose();
        }

        private void Render()
        {
            Bitmap old = plate;
            plate = Draw(scale, seconds, game, hovered, saved);
            if (old != null) old.Dispose();
        }

        private void Redraw()
        {
            if (plate == null) return;
            float lead = Motion ? 1 - Math.Min(1f, age / (float)Slide) : 0;
            float alpha = Motion && age > Slide + Shown ? Math.Max(0f, 1 - (age - Slide - Shown) / (float)Fade) : 1;
            Present(Faded(plate, alpha), new Point(home.X + (int)Math.Round(lead * lead * 48 * scale), home.Y));
        }

        private static Bitmap Faded(Bitmap source, float alpha)
        {
            Rectangle all = new Rectangle(0, 0, source.Width, source.Height);
            if (alpha >= 1) return source.Clone(all, PixelFormat.Format32bppPArgb);
            Bitmap faded = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppPArgb);
            ColorMatrix matrix = new ColorMatrix();
            matrix.Matrix33 = alpha;
            using (Graphics graphics = Graphics.FromImage(faded))
            using (ImageAttributes attributes = new ImageAttributes())
            {
                attributes.SetColorMatrix(matrix);
                graphics.DrawImage(source, all, 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
            }
            return faded;
        }

        // Her talk pill's plate, wider: a red dot and CLIP SAVED in pink with the length on the right, the game under it, and
        // where it went, which turns cyan when hovered.
        internal static Bitmap Draw(float s, double seconds, string game, bool hovered, string clip = null)
        {
            Size size = Measure(s);
            Bitmap image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            using (Font title = Look.Mono(9.5f * s, FontStyle.Bold))
            using (Font name = Look.Mono(12f * s, FontStyle.Regular, GraphicsUnit.Pixel, game))
            using (Font small = Look.Mono(8.5f * s, FontStyle.Regular))
            using (StringFormat trim = new StringFormat(StringFormat.GenericTypographic))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                trim.Trimming = StringTrimming.EllipsisCharacter;
                trim.FormatFlags |= StringFormatFlags.NoWrap;
                int margin = (int)Math.Round(3 * s);
                Rectangle body = new Rectangle(margin, margin, size.Width - margin * 2, size.Height - margin * 2);
                TalkPill.Plate(graphics, body, s, hovered ? body : Rectangle.Empty, Look.Pink, Look.Cyan);
                float left = body.Left + 14 * s, right = body.Right - 14 * s, top = body.Top + 10 * s;
                using (Brush red = new SolidBrush(Look.Red)) graphics.FillEllipse(red, left, top + 2.5f * s, 7 * s, 7 * s);
                using (Brush pink = new SolidBrush(Look.Pink)) graphics.DrawString("CLIP SAVED", title, pink, left + 13 * s, top, StringFormat.GenericTypographic);
                string length = Length(seconds);
                float wide = graphics.MeasureString(length, title, PointF.Empty, StringFormat.GenericTypographic).Width;
                using (Brush dim = new SolidBrush(Look.Dim)) graphics.DrawString(length, title, dim, right - wide, top, StringFormat.GenericTypographic);
                using (Brush ink = new SolidBrush(Look.Ink)) graphics.DrawString(game, name, ink, new RectangleF(left, top + 19 * s, right - left, 18 * s), trim);
                using (Brush caption = new SolidBrush(hovered ? Look.Cyan : TalkPill.Caption))
                    graphics.DrawString(Caption(graphics, small, clip, hovered, right - left), small, caption, new RectangleF(left, top + 42 * s, right - left, 18 * s), trim);
            }
            return image;
        }

        internal static string Caption(Graphics graphics, Font font, string clip, bool hovered, float width)
        {
            string folder = clip == null ? "clips folder" : Path.GetDirectoryName(clip);
            string name = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Length > 0) folder = name;
            string prefix = hovered ? "Show in " : "", suffix = hovered ? "" : " / click to see it";
            string caption = prefix + folder + suffix;
            for (int length = folder.Length - 1; length > 0 && graphics.MeasureString(caption, font, PointF.Empty, StringFormat.GenericTypographic).Width > width; length--)
                caption = prefix + folder.Substring(0, length).TrimEnd() + "\u2026" + suffix;
            return caption;
        }

        // 30 seconds reads 0:30, two minutes 2:00.
        internal static string Length(double seconds)
        {
            int whole = (int)Math.Round(Math.Max(0, seconds));
            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }

        // The scale of the screen it shows on, which may not be the one she's on.
        internal static float ScaleOf(Rectangle area)
        {
            try
            {
                Box box = new Box { Left = area.Left, Top = area.Top, Right = area.Right, Bottom = area.Bottom };
                int x, y;
                if (GetDpiForMonitor(MonitorFromRect(ref box, 2), 0, out x, out y) == 0 && x > 0) return Math.Max(1f, x / 96f);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 1f;
        }

        // Her clip chime: two quick rising notes, made here so no sound file ships with her.
        internal static byte[] Chime()
        {
            const int rate = 44100;
            double[] notes = { 1318.51, 1975.53 };
            double[] lengths = { 0.075, 0.16 };
            List<short> samples = new List<short>();
            for (int n = 0; n < notes.Length; n++)
            {
                int count = (int)(rate * lengths[n]);
                for (int i = 0; i < count; i++)
                {
                    double t = (double)i / rate;
                    double envelope = Math.Min(1, t / 0.004) * Math.Min(1, (count - i) / (0.003 * rate)) * Math.Exp(-t * (n == 0 ? 28 : 16));
                    double wave = Math.Sin(2 * Math.PI * notes[n] * t) + 0.25 * Math.Sin(4 * Math.PI * notes[n] * t);
                    samples.Add((short)Math.Round(wave / 1.25 * envelope * 0.32 * short.MaxValue));
                }
            }
            using (MemoryStream stream = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                writer.Write(36 + samples.Count * 2);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(rate);
                writer.Write(rate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(System.Text.Encoding.ASCII.GetBytes("data"));
                writer.Write(samples.Count * 2);
                foreach (short sample in samples) writer.Write(sample);
                writer.Flush();
                return stream.ToArray();
            }
        }

        internal static void Ring()
        {
            try
            {
                if (chime == null) chime = new SoundPlayer(new MemoryStream(Chime()));
                chime.Play();
            }
            catch (InvalidOperationException) { }
            catch (TimeoutException) { }
            catch (IOException) { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticks.Dispose();
                if (plate != null) { plate.Dispose(); plate = null; }
            }
            base.Dispose(disposing);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Box { internal int Left, Top, Right, Bottom; }

        [DllImport("user32.dll")] private static extern IntPtr MonitorFromRect(ref Box box, int flags);
        [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int kind, out int x, out int y);
    }

    internal sealed class ClipResult
    {
        private int announced;
        internal bool Announce() { return Interlocked.CompareExchange(ref announced, 1, 0) == 0; }
        internal string Problem, Path;
        internal string Monitor;
        internal bool Sound, Unavailable, Merged;
        internal double Seconds;
    }
}
