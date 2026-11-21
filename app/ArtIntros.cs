using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace CapriDesktop
{
    internal static class Intros
    {
        internal const string Classic = "classic", Door = "door", FromMods = "mods";
        private static readonly string[,] Art = { { "peek", "Peek" }, { "up-close", "Up close" } };
        internal static readonly string[] Built = { Classic, "peek", "up-close", Door };

        internal static bool IsArt(string id)
        {
            for (int i = 0; i < Art.GetLength(0); i++) if (Art[i, 0] == id) return true;
            return false;
        }

        internal static bool Known(string id) { return id == Classic || id == Door || id == FromMods || IsArt(id); }

        internal static string NameOf(string id)
        {
            if (id == Classic) return "Classic";
            if (id == Door) return "Door";
            if (id == FromMods) return "From mods";
            for (int i = 0; i < Art.GetLength(0); i++) if (Art[i, 0] == id) return Art[i, 1];
            return id ?? "";
        }

        internal static string AboutOf(string id)
        {
            if (id == Classic) return "Her own glitch in";
            if (id == Door) return "YunoTheAlien \u00b7 " + DoorIntro.Length.ToString("0.0") + " s";
            if (IsArt(id)) return "Hand-drawn \u00b7 " + (ArtIntro.Frames(id) / ArtIntro.Fps).ToString("0.0") + " s";
            return "";
        }
    }

    internal sealed class ArtIntro : ICapriIntro, IDisposable
    {
        internal const double Fps = 12;
        internal const int ArtWide = 1920, ArtHigh = 1080;
        private const int Lead = 3, Workers = 2;
        private static readonly Dictionary<string, int> counted = new Dictionary<string, int>();
        private readonly string id;
        private readonly int count;
        private readonly object gate = new object();
        private readonly Dictionary<int, ArtFrame> ready = new Dictionary<int, ArtFrame>();
        private readonly List<int> making = new List<int>(), failed = new List<int>();
        private Rectangle spot;
        private bool keepAll, ended;
        private ArtFrame shown;
        private int shownAt = -1, wanted, round;

        private ArtIntro(string name, int frames)
        {
            id = name;
            count = frames;
        }

        internal static ArtIntro Make(string id)
        {
            if (!Intros.IsArt(id)) return null;
            int frames = Frames(id);
            return frames == 0 ? null : new ArtIntro(id, frames);
        }

        internal static int Frames(string id)
        {
            lock (counted)
            {
                int frames;
                if (counted.TryGetValue(id, out frames)) return frames;
                frames = 0;
                while (Companion.HasArt(Resource(id, frames))) frames++;
                counted[id] = frames;
                return frames;
            }
        }

        internal static string Resource(string id, int frame) { return "Capri.Intro." + id + "." + (frame + 1).ToString("00") + ".png"; }

        internal string Id { get { return id; } }
        internal int Count { get { return count; } }
        internal int Shown { get { return shownAt; } }
        internal bool Filled { get { lock (gate) return ready.Count == count; } }
        public double Seconds { get { return count / Fps; } }

        internal static Rectangle Fit(Rectangle screen)
        {
            double scale = Math.Min(screen.Width / (double)ArtWide, screen.Height / (double)ArtHigh);
            int wide = Math.Max(1, (int)Math.Round(ArtWide * scale)), high = Math.Max(1, (int)Math.Round(ArtHigh * scale));
            return new Rectangle(screen.X + (screen.Width - wide) / 2, screen.Bottom - high, wide, high);
        }

        public void Begin(Bitmap desktop, Rectangle screen, Rectangle her)
        {
            Place(screen);
            Ahead(0);
            if (keepAll) return;
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            lock (gate)
                while (!ready.ContainsKey(0) && making.Contains(0) && waited.ElapsedMilliseconds < 3000)
                    Monitor.Wait(gate, 50);
            Show(0);
        }

        public void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at)
        {
            Place(screen);
            int frame = FrameAt(at);
            Show(frame);
            Ahead(frame);
            if (shown == null) return;
            Bitmap picture = shown.Picture;
            GraphicsState state = graphics.Save();
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.DrawImage(picture, new Rectangle(spot.X, spot.Y, picture.Width, picture.Height), 0, 0, picture.Width, picture.Height, GraphicsUnit.Pixel);
            graphics.Restore(state);
        }

        internal bool Changes(double at)
        {
            int frame = FrameAt(at);
            lock (gate)
            {
                wanted = frame;
                Queue();
                int best = Best(frame);
                return best >= 0 && best != shownAt;
            }
        }

        private int FrameAt(double at) { return Math.Max(0, Math.Min(count - 1, (int)(at * Fps))); }

        private void Place(Rectangle screen)
        {
            Rectangle fit = Fit(screen);
            lock (gate)
            {
                if (fit.Size != spot.Size) Drop();
                spot = fit;
                keepAll = (long)fit.Width * fit.Height <= 960L * 540;
            }
        }

        private int Best(int frame)
        {
            int best = -1;
            foreach (int made in ready.Keys) if (made <= frame && made > best && (keepAll || made > shownAt)) best = made;
            return best;
        }

        private void Show(int frame)
        {
            lock (gate)
            {
                int best = Best(frame);
                if (best < 0 || best == shownAt) return;
                ArtFrame next = ready[best];
                if (!keepAll)
                {
                    List<int> passed = new List<int>();
                    foreach (int made in ready.Keys) if (made <= best) passed.Add(made);
                    foreach (int made in passed)
                    {
                        if (made != best) Retire(ready[made]);
                        ready.Remove(made);
                    }
                    Retire(shown);
                }
                shown = next;
                shownAt = best;
            }
        }

        private void Ahead(int frame)
        {
            lock (gate)
            {
                wanted = frame;
                Queue();
            }
        }

        private void Queue()
        {
            if (ended || spot.Width <= 0) return;
            int from = keepAll ? 0 : Math.Max(wanted, shownAt + 1), to = keepAll ? count : Math.Min(count, wanted + 1 + Lead);
            for (int next = from; next < to && making.Count < Workers; next++)
            {
                if (ready.ContainsKey(next) || making.Contains(next) || failed.Contains(next)) continue;
                making.Add(next);
                int frame = next, made = round;
                Size size = spot.Size;
                ThreadPool.QueueUserWorkItem(delegate { Make(frame, size, made); });
            }
        }

        private void Make(int frame, Size size, int made)
        {
            ArtFrame art = null, toss = null;
            try { art = ArtFrame.Load(id, frame, size); }
            catch (Exception error) { Brain.Log("art intro", -1, error.ToString()); }
            lock (gate)
            {
                making.Remove(frame);
                bool current = !ended && made == round;
                if (art == null) { if (current) failed.Add(frame); }
                else if (!current || ready.ContainsKey(frame) || !keepAll && frame <= shownAt) toss = art;
                else ready[frame] = art;
                Queue();
                Monitor.PulseAll(gate);
            }
            if (toss != null) toss.Dispose();
        }

        private static void Retire(ArtFrame frame)
        {
            if (frame != null) ThreadPool.QueueUserWorkItem(delegate { frame.Dispose(); });
        }

        internal static Bitmap Render(string id, int frame, Size size)
        {
            using (ArtFrame made = ArtFrame.Load(id, frame, size))
            {
                if (made == null) return null;
                Bitmap copy = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                using (Graphics paper = Graphics.FromImage(copy))
                {
                    paper.CompositingMode = CompositingMode.SourceCopy;
                    paper.DrawImage(made.Picture, new Rectangle(Point.Empty, size), 0, 0, size.Width, size.Height, GraphicsUnit.Pixel);
                }
                return copy;
            }
        }

        private void Drop()
        {
            lock (gate)
            {
                round++;
                foreach (ArtFrame one in ready.Values) if (one != shown) Retire(one);
                ready.Clear();
                failed.Clear();
                Retire(shown);
                shown = null;
                shownAt = -1;
            }
        }

        public void Dispose()
        {
            lock (gate)
            {
                ended = true;
                Drop();
            }
        }
    }

    internal sealed class ArtFrame : IDisposable
    {
        private SoftwareBitmap pixels;
        private BitmapBuffer buffer;
        private Windows.Foundation.IMemoryBufferReference reference;

        internal Bitmap Picture { get; private set; }

        internal static unsafe ArtFrame Load(string id, int frame, Size size)
        {
            if (size.Width <= 0 || size.Height <= 0) return null;
            using (Stream resource = Assembly.GetExecutingAssembly().GetManifestResourceStream(ArtIntro.Resource(id, frame)))
            {
                if (resource == null) return null;
                ArtFrame made = new ArtFrame();
                try
                {
                    using (IRandomAccessStream stream = resource.AsRandomAccessStream())
                    {
                        BitmapDecoder decoder = BitmapDecoder.CreateAsync(stream).AsTask().GetAwaiter().GetResult();
                        BitmapTransform shape = new BitmapTransform { ScaledWidth = (uint)size.Width, ScaledHeight = (uint)size.Height, InterpolationMode = size.Width < decoder.PixelWidth ? BitmapInterpolationMode.Fant : BitmapInterpolationMode.Linear };
                        made.pixels = decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, shape, ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage).AsTask().GetAwaiter().GetResult();
                    }
                    made.buffer = made.pixels.LockBuffer(BitmapBufferAccessMode.Read);
                    made.reference = made.buffer.CreateReference();
                    BitmapPlaneDescription plane = made.buffer.GetPlaneDescription(0);
                    byte* data;
                    uint capacity;
                    ((IMemoryBufferByteAccess)made.reference).GetBuffer(out data, out capacity);
                    if (data == null || plane.Width != size.Width || plane.Height != size.Height || plane.Stride < size.Width * 4 || plane.StartIndex + (long)plane.Stride * size.Height > capacity)
                        throw new InvalidDataException(ArtIntro.Resource(id, frame) + " didn't decode to " + size.Width + " x " + size.Height + ".");
                    made.Picture = new Bitmap(size.Width, size.Height, plane.Stride, PixelFormat.Format32bppPArgb, new IntPtr(data + plane.StartIndex));
                    return made;
                }
                catch
                {
                    made.Dispose();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            if (Picture != null) Picture.Dispose();
            Picture = null;
            if (reference != null) reference.Dispose();
            reference = null;
            if (buffer != null) buffer.Dispose();
            buffer = null;
            if (pixels != null) pixels.Dispose();
            pixels = null;
        }
    }

    [ComImport, Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal unsafe interface IMemoryBufferByteAccess
    {
        void GetBuffer(out byte* buffer, out uint capacity);
    }

    internal sealed class ArrivalIntro : ICapriIntro
    {
        private readonly Clip arrive, idle;

        internal ArrivalIntro(Clip arriving, Clip standing)
        {
            arrive = arriving;
            idle = standing;
        }

        public double Seconds { get { return arrive.Count / arrive.Fps + 1.6; } }

        public void Begin(Bitmap desktop, Rectangle screen, Rectangle her) { }

        public void Draw(Graphics graphics, Rectangle screen, Rectangle her, double at)
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            float size = Math.Max(4f, (her.Height - 6) / idle.Height);
            float midX = her.X + her.Width / 2f - idle.ShiftX * size, midY = her.Y + her.Height / 2f;
            int cell = (int)(Math.Max(0, at) * arrive.Fps);
            int step = (int)(Math.Max(0, at) * idle.Fps) % Math.Max(1, idle.Count);
            if (cell < arrive.Count && !Companion.StandsIn(arrive, idle))
            {
                DoorIntro.Frame(graphics, arrive, cell, size, midX, midY, 1);
                return;
            }
            if (cell < arrive.Count) DoorIntro.Under(graphics, idle, step, size, midX, midY, Companion.UnderLevel(arrive, cell), 5 * 16 + cell);
            DoorIntro.Frame(graphics, idle, step, size, midX, midY, 1);
        }
    }
}
