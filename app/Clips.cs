using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal sealed class ClipBuffer : IDisposable
    {
        internal const int History = 120;
        internal const long Memory = 240L * 1024 * 1024;
        private readonly object gate = new object();
        private readonly LinkedList<ClipPart> pictures = new LinkedList<ClipPart>();
        private readonly LinkedList<ClipPart> sounds = new LinkedList<ClipPart>();
        private readonly Queue<ClipSave> saves = new Queue<ClipSave>();
        private readonly int history;
        private readonly long memory;
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private Thread capture, writer;
        private bool enabled, disposed;
        private long used, through;
        private string game;
        private int dropped;
        private Bitmap shared;
        private long sharedAt;

        internal ClipBuffer() : this(History, Memory) { }

        internal ClipBuffer(int seconds, long bytes)
        {
            if (seconds <= 0 || bytes <= 0) throw new ArgumentOutOfRangeException("seconds");
            history = seconds;
            memory = bytes;
        }

        internal bool Enabled { get { lock (gate) return enabled; } }
        internal long Bytes { get { lock (gate) return used; } }
        internal int Frames { get { lock (gate) return pictures.Count; } }
        internal int Dropped { get { lock (gate) return dropped; } }
        internal long First { get { lock (gate) return pictures.Count == 0 ? 0 : pictures.First.Value.At; } }
        internal static long Now { get { return (long)(Stopwatch.GetTimestamp() * (10000000.0 / Stopwatch.Frequency)); } }

        internal void Enable(bool on)
        {
            lock (gate)
            {
                if (disposed) return;
                enabled = on;
                if (!on) Clear();
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
            bool started = false;
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
                            string open = Enabled ? Computer.GameOpen() : null;
                            front = open == null ? null : GameFinder.Front();
                            lock (gate)
                            {
                                Trim(tick);
                                if (game != open) { Clear(); game = open; }
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
                            interval = 10000000.0 / 30;
                            lock (gate) Clear();
                        }
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
                                using (Bitmap picture = grab.Copy(1920))
                                {
                                    Share(picture);
                                    if (picture != null && Native.GetForegroundWindow() == front.Window) { AddFrame(picture, taken, true); imageAt = taken; }
                                }
                            }
                            long cost = Now - began;
                            interval = Math.Max(10000000.0 / 30, Math.Max(cost * 1.5, interval * 0.98));
                            lock (gate)
                            {
                                if (pictures.Count > 0)
                                    interval = Math.Max(interval, (pictures.Last.Value.Data.Length + 64.0) * history * 10000000 / (memory * 0.85));
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
                    LinkedListNode<ClipPart> thin = pictures.First;
                    bool leave = true;
                    while (thin != null && used + size > memory)
                    {
                        LinkedListNode<ClipPart> following = thin.Next;
                        if (!leave && following != null && thin.Value.Uses == 1) { Release(thin.Value); pictures.Remove(thin); dropped++; }
                        leave = !leave;
                        thin = following;
                    }
                    LinkedListNode<ClipPart> last = pictures.Last;
                    while (video && last != null && used + size > memory)
                    {
                        LinkedListNode<ClipPart> before = last.Previous;
                        if (last.Value.Uses == 1) { Release(last.Value); pictures.Remove(last); dropped++; }
                        last = before;
                    }
                }
                if (used + size > memory) { if (video) dropped++; return false; }
                ClipPart part = new ClipPart { Data = data, At = at, End = end, Uses = 1 };
                (video ? pictures : sounds).AddLast(part);
                used += size;
                through = Math.Max(through, Math.Max(at, end));
                return true;
            }
        }

        internal void Advance(long at) { lock (gate) { Trim(at); through = at; } }

        private void Trim(long at)
        {
            long cutoff = at - history * 10000000L;
            while (pictures.Count > 0 && pictures.First.Value.At < cutoff) { Release(pictures.First.Value); pictures.RemoveFirst(); }
            while (sounds.Count > 0 && sounds.First.Value.End <= cutoff) { Release(sounds.First.Value); sounds.RemoveFirst(); }
        }

        private void Release(ClipPart part) { if (--part.Uses == 0) used -= part.Data.LongLength + 64; }

        private void Clear()
        {
            foreach (ClipPart picture in pictures) Release(picture);
            foreach (ClipPart sound in sounds) Release(sound);
            pictures.Clear();
            sounds.Clear();
            if (shared != null) { shared.Dispose(); shared = null; }
            through = 0;
        }

        internal Task<ClipResult> Save(string path, int seconds)
        {
            if (seconds <= 0) throw new ArgumentOutOfRangeException("seconds");
            ClipSave save = new ClipSave { Path = path };
            lock (gate)
            {
                if (pictures.Count == 0 || disposed)
                {
                    save.Done.SetResult(new ClipResult { Problem = "I haven't buffered any game video yet." });
                    return save.Done.Task;
                }
                save.End = Math.Max(through, pictures.Last.Value.At + 1);
                long cutoff = save.End - seconds * 10000000L;
                LinkedListNode<ClipPart> first = pictures.First;
                while (first.Next != null && first.Next.Value.At <= cutoff) first = first.Next;
                for (LinkedListNode<ClipPart> chosen = first; chosen != null; chosen = chosen.Next) { chosen.Value.Uses++; save.Pictures.Add(chosen.Value); }
                save.Start = Math.Max(cutoff, first.Value.At);
                foreach (ClipPart sound in sounds)
                    if (sound.End > save.Start && sound.At < save.End) { sound.Uses++; save.Sounds.Add(sound); }
                saves.Enqueue(save);
                if (writer == null) { writer = Worker(Write, "Capri clip writer"); writer.Start(); }
            }
            return save.Done.Task;
        }

        private void Write()
        {
            while (true)
            {
                ClipSave save;
                lock (gate)
                {
                    if (saves.Count == 0) { writer = null; return; }
                    save = saves.Dequeue();
                }
                ClipResult result;
                try { result = ClipMedia.Save(save); }
                catch (Exception error) { result = new ClipResult { Problem = "Couldn't save that clip: " + error.Message }; }
                lock (gate)
                {
                    foreach (ClipPart picture in save.Pictures) Release(picture);
                    foreach (ClipPart sound in save.Sounds) Release(sound);
                    save.Pictures.Clear();
                    save.Sounds.Clear();
                }
                save.Done.SetResult(result);
            }
        }

        internal string Clip(int seconds)
        {
            if (!Enabled) return "Turn on Clip my games in Settings > GAMES, then I can clip that.";
            string open = Computer.GameOpen();
            if (open == null) return "There's no game open to clip.";
            lock (gate) if (game != open) return "I haven't buffered any game video yet.";
            string title = open;
            foreach (char bad in Path.GetInvalidFileNameChars()) title = title.Replace(bad, '_');
            title = title.Trim().TrimEnd('.');
            if (title.Length == 0) title = "Game";
            if (title.Length > 100) title = title.Substring(0, 100);
            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Capri Clips");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, title + " " + DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss-fff") + " " + Guid.NewGuid().ToString("N").Substring(0, 6) + ".mp4");
            ClipResult result = Save(path, seconds).GetAwaiter().GetResult();
            return result.Problem ?? "Saved " + result.Seconds.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + " seconds of " + open + (result.Sound ? "" : " without sound") + ": " + path;
        }

        public void Dispose()
        {
            Thread ending, finishing;
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
            if (ending != null) ending.Join();
            if (finishing != null) finishing.Join();
            changed.Dispose();
        }
    }

    internal sealed class ClipPart
    {
        internal byte[] Data;
        internal long At, End;
        internal int Uses;
    }

    internal sealed class ClipSave
    {
        internal string Path;
        internal long Start, End;
        internal readonly List<ClipPart> Pictures = new List<ClipPart>();
        internal readonly List<ClipPart> Sounds = new List<ClipPart>();
        internal readonly TaskCompletionSource<ClipResult> Done = new TaskCompletionSource<ClipResult>();
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
                return new ClipResult { Problem = missing ? "Windows couldn't find an H.264 encoder on this PC, so I couldn't save a video." : "Windows couldn't encode that clip: " + error.Message, Unavailable = missing };
            }
            catch (Exception error) { return new ClipResult { Problem = "Couldn't save that clip: " + error.Message }; }
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
            using (MemoryStream firstBytes = new MemoryStream(save.Pictures[0].Data, false))
            using (Bitmap first = new Bitmap(firstBytes))
            {
                width = (first.Width + 1) & ~1;
                height = (first.Height + 1) & ~1;
            }
            try
            {
                Check(MFCreateAttributes(out options, 2));
                Number(options, "9c27891a-ed7a-40e1-88e8-b22727a024ee", 1);
                Check(MFCreateSinkWriterFromURL(path, IntPtr.Zero, options, out sink));
                target = VideoType(H264, width, height);
                Number((IMFAttributes)target, "20332624-fb0d-4d9e-bd0d-cbf6786c102e", Math.Max(250000, Math.Min(12000000, width * height * 4)));
                int videoStream;
                Check(sink.AddStream(target, out videoStream));
                input = VideoType(Nv12, width, height);
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
                int videoAt = 0;
                long audioAt = 0;
                long audioFrames = (save.End - save.Start) * 48000 / 10000000;
                int soundAt = 0;
                using (Bitmap canvas = new Bitmap(width, height, PixelFormat.Format32bppRgb))
                {
                    while (videoAt < save.Pictures.Count || (sound && audioAt < audioFrames))
                    {
                        long videoTime = videoAt < save.Pictures.Count ? Math.Max(0, save.Pictures[videoAt].At - save.Start) : long.MaxValue;
                        long audioTime = audioAt * 10000000 / 48000;
                        if (sound && audioAt < audioFrames && audioTime < videoTime)
                        {
                            int amount = (int)Math.Min(4800, audioFrames - audioAt);
                            byte[] pcm = new byte[amount * 4];
                            long packetStart = save.Start + audioTime;
                            long packetEnd = save.Start + (audioAt + amount) * 10000000 / 48000;
                            while (soundAt < save.Sounds.Count && save.Sounds[soundAt].End <= packetStart) soundAt++;
                            for (int partAt = soundAt; partAt < save.Sounds.Count && save.Sounds[partAt].At < packetEnd; partAt++)
                            {
                                ClipPart part = save.Sounds[partAt];
                                long offset = (long)Math.Round((part.At - save.Start) * (48000.0 / 10000000)) - audioAt;
                                int source = (int)Math.Max(0, -offset);
                                int destination = (int)Math.Max(0, offset);
                                int copied = Math.Min(part.Data.Length / 4 - source, amount - destination);
                                if (copied > 0) Buffer.BlockCopy(part.Data, source * 4, pcm, destination * 4, copied * 4);
                            }
                            try { WriteSample(sink, audioStream, pcm, audioTime, (audioAt + amount) * 10000000 / 48000 - audioTime); }
                            catch (COMException soundError) { throw new ClipSoundException(soundError); }
                            audioAt += amount;
                        }
                        else
                        {
                            ClipPart picture = save.Pictures[videoAt];
                            long end = videoAt + 1 < save.Pictures.Count ? save.Pictures[videoAt + 1].At : save.End;
                            using (MemoryStream bytes = new MemoryStream(picture.Data, false))
                            using (Bitmap decoded = new Bitmap(bytes))
                            using (Graphics drawing = Graphics.FromImage(canvas))
                            {
                                drawing.Clear(Color.Black);
                                double scale = Math.Min((double)width / decoded.Width, (double)height / decoded.Height);
                                int across = (int)(decoded.Width * scale), down = (int)(decoded.Height * scale);
                                drawing.DrawImage(decoded, new Rectangle((width - across) / 2, (height - down) / 2, across, down));
                            }
                            WriteSample(sink, videoStream, Pixels(canvas), videoTime, Math.Max(1, end - save.Start - videoTime));
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

        private static IMFMediaType VideoType(Guid subtype, int width, int height)
        {
            IMFMediaType type;
            Check(MFCreateMediaType(out type));
            try
            {
                IMFAttributes attributes = (IMFAttributes)type;
                Check(attributes.SetGUID(Major, Video));
                Check(attributes.SetGUID(Subtype, subtype));
                Check(attributes.SetUINT64(FrameSize, ((long)width << 32) | (uint)height));
                Check(attributes.SetUINT64(new Guid("c459a2e8-3d2c-4e44-b132-fee5156c7bb0"), (30L << 32) | 1));
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

    internal sealed class ClipResult
    {
        internal string Problem;
        internal bool Sound, Unavailable;
        internal double Seconds;
    }
}
