using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her two secret emotes, her chair and her mini heart: the drawings by Qewiw (mix_qewiw1412_86144), played from the
    // frames as they were drawn. Each is a zip of PNGs in assets/emotes: every frame of the clip at 30 a second, cut to its
    // own ink, with a frame that matches the one before it pointing back at that picture. The clips came with their own
    // alpha, so nothing in them was keyed. The chair comes in two layers: her, drawn at her size where she is, and the
    // close-up of her face, every pixel of those frames that isn't her seated figure, drawn the height of her screen.
    internal sealed class EmoteClip
    {
        internal const double Fps = 30;
        internal readonly string Name;
        internal readonly int Width, Height, Count;
        // The close-up layer's whole frame, in its own pixels; 0 when the clip has none.
        internal readonly int CloseWidth, CloseHeight;
        private readonly List<byte[]> pictures = new List<byte[]>();
        private readonly int[] her, close;            // for each frame, which picture, or -1
        private readonly Point[] herAt, closeAt;      // where that picture's top left sits in its layer's frame
        // Pictures are decoded ahead on workers, so her screen thread only swaps them in. Only a few frames' worth are kept.
        private readonly object gate = new object();
        private readonly Dictionary<int, Bitmap> ready = new Dictionary<int, Bitmap>();
        private readonly HashSet<int> pending = new HashSet<int>();
        private int generation;

        private EmoteClip(string name, Stream zip)
        {
            Name = name;
            Dictionary<string, int> named = new Dictionary<string, int>();
            string[] lines;
            using (ZipArchive archive = new ZipArchive(zip, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    if (!entry.Name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) continue;
                    using (Stream data = entry.Open())
                    using (MemoryStream copy = new MemoryStream())
                    {
                        data.CopyTo(copy);
                        named[entry.Name] = pictures.Count;
                        pictures.Add(copy.ToArray());
                    }
                }
                ZipArchiveEntry list = archive.GetEntry("frames.txt");
                if (list == null) throw new InvalidDataException("Her " + name + " emote has no frame list.");
                using (StreamReader reader = new StreamReader(list.Open())) lines = reader.ReadToEnd().Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            }
            List<int> herList = new List<int>(), closeList = new List<int>();
            List<Point> herPlaces = new List<Point>(), closePlaces = new List<Point>();
            foreach (string raw in lines)
            {
                string[] parts = raw.Trim().Split(' ');
                if (parts[0] == "size") { Width = int.Parse(parts[1]); Height = int.Parse(parts[2]); continue; }
                if (parts[0] == "closeup") { CloseWidth = int.Parse(parts[1]); CloseHeight = int.Parse(parts[2]); continue; }
                int frame;
                if (!int.TryParse(parts[0], out frame)) continue;
                if (frame != herList.Count) throw new InvalidDataException("Her " + name + " emote skips a frame at " + frame + ".");
                // "frame her x y [close x y]", with "-" for an empty layer
                int next = 1;
                if (parts[next] == "-") { herList.Add(-1); herPlaces.Add(Point.Empty); next++; }
                else { herList.Add(named[parts[next]]); herPlaces.Add(new Point(int.Parse(parts[next + 1]), int.Parse(parts[next + 2]))); next += 3; }
                if (parts.Length >= next + 3) { closeList.Add(named[parts[next]]); closePlaces.Add(new Point(int.Parse(parts[next + 1]), int.Parse(parts[next + 2]))); }
                else { closeList.Add(-1); closePlaces.Add(Point.Empty); }
            }
            her = herList.ToArray();
            herAt = herPlaces.ToArray();
            close = closeList.ToArray();
            closeAt = closePlaces.ToArray();
            Count = her.Length;
            if (Width <= 0 || Height <= 0 || Count == 0) throw new InvalidDataException("Her " + name + " emote is empty.");
        }

        internal static string Resource(string name) { return "Capri.Emote." + name + ".zip"; }

        internal static EmoteClip Load(string name)
        {
            using (Stream zip = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource(name)))
                return zip == null ? null : new EmoteClip(name, zip);
        }

        internal int Pictures { get { return pictures.Count; } }

        internal bool Empty(int frame) { return frame < 0 || frame >= Count || her[frame] < 0; }

        internal bool HasCloseup(int frame) { return frame >= 0 && frame < Count && close[frame] >= 0; }

        // Which stored picture her layer shows in a frame, so her checks can see two frames share one.
        internal int HerPicture(int frame) { return frame >= 0 && frame < Count ? her[frame] : -1; }

        private static Bitmap Decode(byte[] png)
        {
            using (MemoryStream data = new MemoryStream(png))
            using (Bitmap raw = new Bitmap(data))
            {
                Bitmap next = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(next))
                {
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.DrawImage(raw, new Rectangle(0, 0, raw.Width, raw.Height));
                }
                return next;
            }
        }

        // A stored picture, from what the workers decoded ahead, or decoded here when it isn't in yet. Lent: the clip keeps it
        // until it's well behind the frame being drawn.
        private Bitmap Decoded(int index, int frame)
        {
            if (index < 0) return null;
            Bitmap found;
            lock (gate)
            {
                Forget(frame);
                if (ready.TryGetValue(index, out found)) return found;
            }
            Bitmap made = Decode(pictures[index]);
            lock (gate)
            {
                if (ready.TryGetValue(index, out found)) { made.Dispose(); return found; }
                ready[index] = made;
                return made;
            }
        }

        // Lets go of the pictures no frame near this one shows. Called only from the thread that draws.
        private void Forget(int frame)
        {
            if (ready.Count <= 6) return;
            HashSet<int> near = new HashSet<int>();
            for (int i = Math.Max(0, frame - 1); i < Math.Min(Count, frame + 6); i++) { near.Add(her[i]); near.Add(close[i]); }
            List<int> gone = new List<int>();
            foreach (int index in ready.Keys) if (!near.Contains(index)) gone.Add(index);
            foreach (int index in gone) { ready[index].Dispose(); ready.Remove(index); }
        }

        // Her layer's picture in a frame, and where it sits in her layer's frame.
        internal Bitmap Picture(int frame, out Point offset)
        {
            offset = Empty(frame) ? Point.Empty : herAt[frame];
            return Empty(frame) ? null : Decoded(her[frame], frame);
        }

        // The close-up's picture in a frame, or null, and where it sits in the close-up's frame.
        internal Bitmap Closeup(int frame, out Point offset)
        {
            offset = HasCloseup(frame) ? closeAt[frame] : Point.Empty;
            return HasCloseup(frame) ? Decoded(close[frame], frame) : null;
        }

        // Starts decoding the pictures of the next few frames on workers.
        internal void Ahead(int frame, int frames)
        {
            for (int i = Math.Max(0, frame); i < Math.Min(Count, frame + frames); i++)
                foreach (int index in new[] { her[i], close[i] })
                {
                    if (index < 0) continue;
                    int version;
                    lock (gate)
                    {
                        if (ready.ContainsKey(index) || pending.Contains(index)) continue;
                        pending.Add(index);
                        version = generation;
                    }
                    int which = index;
                    System.Threading.ThreadPool.QueueUserWorkItem(delegate
                    {
                        Bitmap made = null;
                        try { made = Decode(pictures[which]); }
                        catch (Exception) { }
                        lock (gate)
                        {
                            pending.Remove(which);
                            if (made == null) return;
                            if (version != generation || ready.ContainsKey(which)) made.Dispose();
                            else ready[which] = made;
                        }
                    });
                }
        }

        internal void Release()
        {
            lock (gate)
            {
                generation++;
                foreach (Bitmap picture in ready.Values) picture.Dispose();
                ready.Clear();
            }
        }
    }

    // Where a clip's frame goes on the screen: x is Left + Across * frame x, y is Top + Scale * frame y. Across is minus the
    // scale when it's mirrored. A Scale of 0 is nowhere.
    internal struct EmotePlace
    {
        internal float Left, Top, Scale, Across;

        internal RectangleF On(Rectangle picture)
        {
            float a = Left + Across * picture.Left, b = Left + Across * picture.Right;
            return RectangleF.FromLTRB(Math.Min(a, b), Top + Scale * picture.Top, Math.Max(a, b), Top + Scale * picture.Bottom);
        }
    }

    // How far into its clip an emote is. The chair holds on a still frame while she's still thinking or talking, so her
    // answer comes while she's sat there, and goes on to its end once she's done.
    internal sealed class EmoteClock
    {
        internal readonly int HoldAt;
        internal readonly double HoldMost;
        private double played, held;

        internal EmoteClock(int holdAt, double holdMost) { HoldAt = holdAt; HoldMost = holdMost; }

        internal double Held { get { return held; } }

        internal int Frame { get { return (int)Math.Floor(played * EmoteClip.Fps + 1e-6); } }

        internal int Advance(double seconds, bool busy)
        {
            if (seconds <= 0) return Frame;
            if (HoldAt >= 0 && busy && held < HoldMost)
            {
                double until = HoldAt / EmoteClip.Fps;
                if (played >= until) { held += seconds; return Frame; }
                if (played + seconds > until) { held += played + seconds - until; played = until; return Frame; }
            }
            played += seconds;
            return Frame;
        }
    }

    internal sealed class EmoteWindow : Overlay
    {
        // The chair clip as drawn: where the feet of her chair stand, in the clip's own pixels, so the chair goes down where
        // she stands. Its still frames run from 163 to 265; she holds on 230 while she's still answering.
        internal const float ChairFootX = 609, ChairFootY = 412;
        internal const int ChairHold = 230;
        internal const double HoldMost = 30;
        // Her chair clip's own pixels are her size at her usual 240: her sitting there, hair to the chair's feet, is as tall as
        // she is perched.
        internal const float ChairSize = 240;
        // Frames decoded ahead of the one on the screen.
        internal const int Ahead = 3;
        // Her figure in the chair, in her layer's frame: where her ribbons run to and leave from. The clip's sweep into the
        // chair first shows at frame 4; her ribbons take her back from frame 280, as its fade takes her out of the chair.
        internal static readonly Rectangle ChairBody = Rectangle.FromLTRB(534, 205, 692, 416);
        internal const int SweepFrom = 4, ChairLeave = 280;

        private readonly Beat ticks = new Beat();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private EmoteClip clip;
        private EmoteClock clock;
        private EmotePlace place, closePlace;
        private Rectangle screen;
        private double last;
        private int shown = -2;
        private Func<bool> busy, here;
        private Action done;
        private Native.Surface surface;
        private double delay;
        private int leaveFrame = -1;
        private Action leave;
        // For her checks: how many frames reached the screen in the last play, and how long drawing them took on her thread.
        internal int FramesShown;
        internal double DrawTotal, DrawWorst;
        // Where her thread's time goes, for her checks: picking the decoded pictures, the surface, painting, and showing it.
        internal double PickTotal, MakeTotal, PaintTotal, ShowTotal;
        internal int Plays;
        // When the last one started and when it ran out, in Stopwatch ticks, for her checks.
        internal long LastPlayed, LastRanOut;
        // Why the last one stopped early, if something went wrong drawing it.
        internal string Ended, StoppedEarly;

        internal EmoteWindow() : base("Capri emote")
        {
            ClickThrough = true;
            // Her own clock, so a busy window thread can't hold her emotes up. Its wait wakes on Windows' 15.6 ms tick, so asking
            // for 15 ms often waits two of them and a 30-a-second clip loses frames; asking for 8 wakes on every one.
            ticks.Interval = 8;
            ticks.Tick += delegate { Step(); };
        }

        internal bool Playing { get { return clip != null; } }

        internal string Name { get { return clip == null ? null : clip.Name; } }

        internal int At { get { return clock == null ? -1 : clock.Frame; } }

        // The chair, with its feet at foot, at her size; mirrored is her on the left of her screen.
        internal static EmotePlace ChairPlace(PointF foot, int size, bool mirrored)
        {
            float scale = size / ChairSize;
            return new EmotePlace { Scale = scale, Across = mirrored ? -scale : scale, Left = foot.X + (mirrored ? ChairFootX : -ChairFootX) * scale, Top = foot.Y - ChairFootY * scale };
        }

        // Her close-up as it was drawn: the clip's whole frame the height of her screen, against the edge away from her, so
        // only the screen's own edges cut it. Mirrored when she's on the left, against the right edge.
        internal static EmotePlace CloseupPlace(EmoteClip chair, Rectangle screen, bool mirrored)
        {
            if (chair.CloseHeight <= 0) return new EmotePlace();
            float scale = screen.Height / (float)chair.CloseHeight;
            return new EmotePlace { Scale = scale, Across = mirrored ? -scale : scale, Left = mirrored ? screen.Right : screen.Left, Top = screen.Top };
        }

        // Her heart over the whole of a screen, cropped to fill it.
        internal static EmotePlace HeartPlace(EmoteClip heart, Rectangle screen)
        {
            float scale = Math.Max(screen.Width / (float)heart.Width, screen.Height / (float)heart.Height);
            return new EmotePlace { Scale = scale, Across = scale, Left = screen.Left + (screen.Width - heart.Width * scale) / 2f, Top = screen.Top + (screen.Height - heart.Height * scale) / 2f };
        }

        // Plays a clip. busy holds the chair while she's answering; here says she's still where the emote is, and when she
        // isn't (hidden, moved away) it stops; done runs once it's over, however it ended.
        internal void Play(EmoteClip what, EmotePlace where, EmotePlace closeup, Rectangle bounds, int holdAt, Func<bool> answering, Func<bool> still, Action finished, double wait = 0, int leaveAt = -1, Action leaving = null)
        {
            Stop();
            delay = wait;
            leaveFrame = leaveAt;
            leave = leaving;
            clip = what;
            place = where;
            closePlace = closeup;
            screen = bounds;
            clock = new EmoteClock(holdAt, HoldMost);
            busy = answering;
            here = still;
            done = finished;
            shown = -2;
            last = 0;
            FramesShown = 0;
            DrawTotal = DrawWorst = 0;
            PickTotal = MakeTotal = PaintTotal = ShowTotal = 0;
            Plays++;
            LastPlayed = System.Diagnostics.Stopwatch.GetTimestamp();
            LastRanOut = 0;
            Ended = StoppedEarly = null;
            clip.Ahead(0, Ahead + 1);
            watch.Reset();
            watch.Start();
            Step();
            ticks.Start();
        }

        internal void Stop()
        {
            // For her checks: what stopped one partway, when nothing went wrong drawing it.
            if (clip != null && clock != null && clock.Frame < clip.Count - 1 && Ended == null) StoppedEarly = "at frame " + clock.Frame + " by " + System.Text.RegularExpressions.Regex.Replace(Environment.StackTrace, @"\s+at System\..*", "").Replace(Environment.NewLine, " | ");
            ticks.Stop();
            watch.Stop();
            if (Visible) Hide();
            Forget();
            DropSurface();
            EmoteClip was = clip;
            Action after = done;
            clip = null;
            clock = null;
            done = null;
            leave = null;
            busy = here = null;
            if (was != null) was.Release();
            if (after != null) after();
        }

        private void Step()
        {
            if (clip == null) { Stop(); return; }
            try
            {
                if (here != null && !here()) { Stop(); return; }
                double now = watch.Elapsed.TotalSeconds - delay;
                if (now < 0) return;
                int frame = clock.Advance(now - last, busy != null && busy());
                last = now;
                if (frame >= clip.Count) { LastRanOut = System.Diagnostics.Stopwatch.GetTimestamp(); Stop(); return; }
                if (leave != null && leaveFrame >= 0 && frame >= leaveFrame) { Action going = leave; leave = null; going(); }
                if (frame == shown) return;
                shown = frame;
                System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
                Draw(frame);
                double ms = took.Elapsed.TotalMilliseconds;
                FramesShown++;
                DrawTotal += ms;
                DrawWorst = Math.Max(DrawWorst, ms);
                clip.Ahead(frame + 1, Ahead);
            }
            // A screen unplugged, no memory for the picture: the emote ends and nothing is left on the screen.
            catch (Exception problem) { Ended = problem.GetType().Name + ": " + problem.Message; Stop(); }
        }

        // The part of the screen a frame covers: the close-up, and her over it.
        private static Rectangle Covers(Bitmap picture, Point offset, EmotePlace where, Bitmap closeup, Point closeAt, EmotePlace closeWhere, Rectangle screen)
        {
            Rectangle area = picture == null ? Rectangle.Empty : Rectangle.Ceiling(where.On(new Rectangle(offset, picture.Size)));
            if (closeup != null && closeWhere.Scale > 0)
            {
                Rectangle face = Rectangle.Ceiling(closeWhere.On(new Rectangle(closeAt, closeup.Size)));
                area = area.IsEmpty ? face : Rectangle.Union(area, face);
            }
            return Rectangle.Intersect(area, screen);
        }

        // One frame on the screen: its pictures, decoded ahead, drawn over the part of the screen they cover, and the window
        // moved there.
        private void Draw(int frame)
        {
            System.Diagnostics.Stopwatch part = System.Diagnostics.Stopwatch.StartNew();
            Point offset, closeAt;
            Bitmap picture = clip.Picture(frame, out offset), closeup = clip.Closeup(frame, out closeAt);
            PickTotal += part.Elapsed.TotalMilliseconds; part.Restart();
            Rectangle area = Covers(picture, offset, place, closeup, closeAt, closePlace, screen);
            if (area.Width < 2 || area.Height < 2) { if (Visible) Hide(); return; }
            // The heart and the close-up cover most of the screen, so one surface is kept while the size stays the same.
            if (surface == null || surface.Image.Width != area.Width || surface.Image.Height != area.Height)
            {
                DropSurface();
                surface = new Native.Surface(area.Width, area.Height);
            }
            MakeTotal += part.Elapsed.TotalMilliseconds; part.Restart();
            // A frame drawn pixel for pixel that fills the window (every heart frame, and the close-up on a screen 1080 high)
            // is copied straight in, row by row, mirrored where it's turned: no clearing and no blending of a full screen of
            // pixels, which is what held the heart to about 80 of its 105 frames.
            bool both = closePlace.Scale > 0 && closeup != null;
            bool copied = both ? CopyFirst(surface, closeup, closeAt, closePlace, area) : CopyFirst(surface, picture, offset, place, area);
            if (!copied || both)
                using (Graphics graphics = Graphics.FromImage(surface.Image))
                {
                    if (!copied)
                    {
                        graphics.Clear(Color.Transparent);
                        if (both) Paint(graphics, closeup, closeAt, closePlace, new Point(-area.X, -area.Y), true);
                    }
                    Paint(graphics, picture, offset, place, new Point(-area.X, -area.Y), !both);
                }
            PaintTotal += part.Elapsed.TotalMilliseconds; part.Restart();
            if (Bounds != area) Bounds = area;
            if (!Visible) Show();
            surface.Show(Handle, area.Location);
            ShowTotal += part.Elapsed.TotalMilliseconds;
        }

        private static unsafe bool CopyFirst(Native.Surface surface, Bitmap picture, Point offset, EmotePlace where, Rectangle area)
        {
            if (picture == null || Math.Abs(where.Scale - 1) > 0.0001f || Math.Abs(Math.Abs(where.Across) - 1) > 0.0001f) return false;
            bool turned = where.Across < 0;
            float left = turned ? where.Left - offset.X - picture.Width : where.Left + offset.X, top = where.Top + offset.Y;
            if (Math.Abs(left - Math.Round(left)) > 0.01 || Math.Abs(top - Math.Round(top)) > 0.01) return false;
            if (new Rectangle((int)Math.Round(left), (int)Math.Round(top), picture.Width, picture.Height) != area || surface.Image.Width != area.Width) return false;
            BitmapData from = picture.LockBits(new Rectangle(Point.Empty, picture.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                int width = picture.Width, row = width * 4;
                byte* into = (byte*)surface.Pixels;
                for (int y = 0; y < picture.Height; y++)
                {
                    byte* source = (byte*)from.Scan0 + y * from.Stride, target = into + y * row;
                    if (!turned) Buffer.MemoryCopy(source, target, row, row);
                    else
                    {
                        int* s = (int*)source, t = (int*)target;
                        for (int x = 0; x < width; x++) t[x] = s[width - 1 - x];
                    }
                }
            }
            finally { picture.UnlockBits(from); }
            return true;
        }

        // For her checks: one frame of her layer drawn both ways into two surfaces, the quick copy and GDI+. Says whether the
        // copy took it, and the first pixel where the two differ (premultiplied, alpha included), or null when none do.
        internal static string BothWays(EmoteClip what, int frame, EmotePlace where, Rectangle screen, out bool copied)
        {
            Point offset;
            Bitmap picture = what.Picture(frame, out offset);
            copied = false;
            Rectangle area = Covers(picture, offset, where, null, Point.Empty, new EmotePlace(), screen);
            if (area.Width < 2 || area.Height < 2) return "nothing to draw";
            using (Native.Surface quick = new Native.Surface(area.Width, area.Height))
            using (Native.Surface slow = new Native.Surface(area.Width, area.Height))
            {
                copied = CopyFirst(quick, picture, offset, where, area);
                using (Graphics graphics = Graphics.FromImage(slow.Image))
                {
                    graphics.Clear(Color.Transparent);
                    Paint(graphics, picture, offset, where, new Point(-area.X, -area.Y), true);
                }
                if (!copied) return null;
                int count = area.Width * area.Height;
                int[] a = new int[count], b = new int[count], own = new int[count];
                System.Runtime.InteropServices.Marshal.Copy(quick.Pixels, a, 0, count);
                System.Runtime.InteropServices.Marshal.Copy(slow.Pixels, b, 0, count);
                // and the copy is the clip's own pixels, turned when it's turned
                BitmapData data = picture.LockBits(new Rectangle(Point.Empty, picture.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    int[] row = new int[picture.Width];
                    for (int y = 0; y < picture.Height; y++)
                    {
                        System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, picture.Width);
                        for (int x = 0; x < picture.Width; x++) own[y * picture.Width + (where.Across < 0 ? picture.Width - 1 - x : x)] = row[x];
                    }
                }
                finally { picture.UnlockBits(data); }
                for (int i = 0; i < count; i++) if (a[i] != own[i]) return "the copy isn't the clip's own pixels at " + (i % area.Width) + "," + (i / area.Width) + ": " + a[i].ToString("X8") + " for " + own[i].ToString("X8");
                int differ = 0, first = -1;
                for (int i = 0; i < count; i++) if (a[i] != b[i]) { if (first < 0) first = i; differ++; }
                return differ == 0 ? null : differ + " pixels differ, first at " + (first % area.Width) + "," + (first / area.Width) + ": copy " + a[first].ToString("X8") + ", GDI+ " + b[first].ToString("X8");
            }
        }

        private void DropSurface()
        {
            if (surface != null) surface.Dispose();
            surface = null;
        }

        internal static void Paint(Graphics graphics, Bitmap picture, Point offset, EmotePlace place, Point shift, bool alone = false)
        {
            if (picture == null) return;
            // The first picture on a clear surface is put down as it is: blended over nothing, GDI+ rounds faint alpha down a step
            // (5 became 4), so its pixels wouldn't be the clip's own.
            graphics.CompositingMode = alone ? CompositingMode.SourceCopy : CompositingMode.SourceOver;
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            float left = place.Left + place.Across * offset.X + shift.X, right = place.Left + place.Across * (offset.X + picture.Width) + shift.X;
            float top = place.Top + place.Scale * offset.Y + shift.Y, bottom = place.Top + place.Scale * (offset.Y + picture.Height) + shift.Y;
            // Drawn pixel for pixel (the heart, and the close-up on a 1080 high screen): the quick way, and the same pixels.
            if (Math.Abs(place.Scale - 1) < 0.0001f && Math.Abs(Math.Abs(place.Across) - 1) < 0.0001f && Math.Abs(left - (float)Math.Round(left)) < 0.01f && Math.Abs(top - (float)Math.Round(top)) < 0.01f)
            {
                if (place.Across > 0) { graphics.DrawImageUnscaled(picture, (int)Math.Round(left), (int)Math.Round(top)); return; }
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            }
            // top left, top right, bottom left of the picture: swapped over when it's mirrored
            graphics.DrawImage(picture, new[] { new PointF(left, top), new PointF(right, top), new PointF(left, bottom) }, new RectangleF(0, 0, picture.Width, picture.Height), GraphicsUnit.Pixel);
        }

        // One frame of a clip as a picture of a given part of the screen, for her checks and her films: the close-up where
        // closeup puts it, if there is one, and her layer over it.
        internal static Bitmap Frame(EmoteClip what, int frame, EmotePlace where, Rectangle stage, EmotePlace closeup = new EmotePlace())
        {
            Bitmap image = new Bitmap(stage.Width, stage.Height, PixelFormat.Format32bppPArgb);
            Point offset, closeAt;
            using (Graphics graphics = Graphics.FromImage(image))
            {
                Bitmap face = what.Closeup(frame, out closeAt);
                bool under = closeup.Scale > 0 && face != null;
                if (under) Paint(graphics, face, closeAt, closeup, new Point(-stage.X, -stage.Y), true);
                Paint(graphics, what.Picture(frame, out offset), offset, where, new Point(-stage.X, -stage.Y), !under);
            }
            return image;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Dispose(); Stop(); }
            base.Dispose(disposing);
        }
    }

    // Smoke only, and only when CAPRI_EMOTE_FILM names a folder: a record of her real windows as the app draws them, for a
    // film made afterwards. Every picture a window of hers is given (the same one UpdateLayeredWindow gets) is copied as it's
    // given, with the time and where the window is, and written out on another thread; 30 times a second which of her
    // windows are showing, in their stacking order, is written down too. Nothing is read off the screen, and nothing is
    // encoded while she plays, so the record costs her as little as it can.
    internal static class EmoteFilm
    {
        private sealed class Shot { public long At; public long Window; public int X, Y, Width, Height; public byte[] Pixels; }

        private static readonly object gate = new object();
        private static readonly Queue<Shot> waiting = new Queue<Shot>();
        private static readonly Stack<byte[]> spare = new Stack<byte[]>();
        private static readonly System.Diagnostics.Stopwatch clock = new System.Diagnostics.Stopwatch();
        private static volatile bool recording;
        private static System.Threading.Thread writer, sampler;
        private static int process;
        internal static int Copies, Samples;
        internal static double CopyWorst;

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr parent);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Box box);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int owner);
        private struct Box { public int Left, Top, Right, Bottom; }

        internal static unsafe void Shown(IntPtr window, Bitmap image)
        {
            if (!recording || window == IntPtr.Zero || image == null) return;
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            Box box;
            if (!GetWindowRect(window, out box)) return;
            int bytes = image.Width * image.Height * 4;
            byte[] pixels = null;
            lock (gate) { while (spare.Count > 0 && pixels == null) { byte[] one = spare.Pop(); if (one.Length >= bytes) pixels = one; } }
            if (pixels == null) pixels = new byte[bytes];
            Rectangle all = new Rectangle(Point.Empty, image.Size);
            BitmapData from = image.LockBits(all, ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                fixed (byte* to = pixels)
                    for (int y = 0; y < all.Height; y++)
                        Buffer.MemoryCopy((byte*)from.Scan0 + y * from.Stride, to + y * all.Width * 4, bytes - y * all.Width * 4, all.Width * 4);
            }
            finally { image.UnlockBits(from); }
            lock (gate)
            {
                waiting.Enqueue(new Shot { At = clock.ElapsedTicks, Window = window.ToInt64(), X = box.Left, Y = box.Top, Width = all.Width, Height = all.Height, Pixels = pixels });
                System.Threading.Monitor.Pulse(gate);
                Copies++;
                CopyWorst = Math.Max(CopyWorst, took.Elapsed.TotalMilliseconds);
            }
        }

        // pictures.bin: per picture its time in microseconds, window, left, top, width, height, then its premultiplied BGRA
        // rows. shown.txt: per sample its time in microseconds and the windows showing, top of the stack first.
        internal static void Start(string folder)
        {
            Stop();
            Directory.CreateDirectory(folder);
            Copies = Samples = 0;
            CopyWorst = 0;
            process = System.Diagnostics.Process.GetCurrentProcess().Id;
            clock.Restart();
            recording = true;
            FileStream pictures = new FileStream(Path.Combine(folder, "pictures.bin"), FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
            writer = new System.Threading.Thread(delegate()
            {
                using (BinaryWriter into = new BinaryWriter(pictures))
                    while (true)
                    {
                        Shot next = null;
                        lock (gate)
                        {
                            while (waiting.Count == 0 && recording) System.Threading.Monitor.Wait(gate, 100);
                            if (waiting.Count == 0) break;
                            next = waiting.Dequeue();
                        }
                        into.Write(next.At * 1000000 / System.Diagnostics.Stopwatch.Frequency);
                        into.Write(next.Window);
                        into.Write(next.X); into.Write(next.Y); into.Write(next.Width); into.Write(next.Height);
                        into.Write(next.Pixels, 0, next.Width * next.Height * 4);
                        lock (gate) if (spare.Count < 8) spare.Push(next.Pixels);
                    }
            });
            writer.IsBackground = true;
            writer.Start();
            StreamWriter shown = new StreamWriter(Path.Combine(folder, "shown.txt"));
            sampler = new System.Threading.Thread(delegate()
            {
                using (shown)
                    while (recording)
                    {
                        System.Text.StringBuilder line = new System.Text.StringBuilder();
                        line.Append(clock.ElapsedTicks * 1000000 / System.Diagnostics.Stopwatch.Frequency);
                        for (IntPtr w = GetTopWindow(IntPtr.Zero); w != IntPtr.Zero; w = GetWindow(w, 2))
                        {
                            int owner;
                            GetWindowThreadProcessId(w, out owner);
                            if (owner != process || !IsWindowVisible(w)) continue;
                            Box box;
                            GetWindowRect(w, out box);
                            line.Append(' ').Append(w.ToInt64()).Append(',').Append(box.Left).Append(',').Append(box.Top);
                        }
                        shown.WriteLine(line.ToString());
                        Samples++;
                        System.Threading.Thread.Sleep(8);
                    }
            });
            sampler.IsBackground = true;
            sampler.Start();
        }

        internal static void Stop()
        {
            recording = false;
            lock (gate) System.Threading.Monitor.PulseAll(gate);
            if (sampler != null) { sampler.Join(5000); sampler = null; }
            if (writer != null) { writer.Join(60000); writer = null; }
            lock (gate) { waiting.Clear(); spare.Clear(); }
        }
    }

    internal sealed partial class Companion
    {
        // Vergil, or Devil May Cry, said or typed anywhere in what they say: she sits in her chair as she starts answering.
        // Typed, "Virgil" is someone else. Said out loud, speech to text writes Vergil as Virgil, so heard it counts.
        // "May cry" alone never does.
        internal static readonly Regex VergilMention = new Regex(@"\bvergil(?:'?s)?\b|\bdevil[\s-]*may[\s-]*cry\b", RegexOptions.IgnoreCase);
        internal static readonly Regex VergilHeard = new Regex(@"\bvirgil(?:'?s)?\b", RegexOptions.IgnoreCase);

        internal static bool Mentions(string text, bool spoken) { return VergilMention.IsMatch(text ?? "") || spoken && VergilHeard.IsMatch(text ?? ""); }

        internal static bool EmoteName(string name) { return name == "chair" || name == "heart"; }

        private EmoteWindow emote;
        private double emoteVeil = -1;

        // Where her chair's feet go: on the edge she's sat on, or the floor she's standing on, under her middle.
        private PointF ChairFoot()
        {
            bool sat = seat != null || onPanel;
            Rectangle body = BodyOnScreen(anchor, sat || riding ? PerchedBody : Body);
            float ground = sat ? Hip().Y : anchor.Y + PaddingSize + preferences.Size * StandY;
            return new PointF(body.Left + body.Width / 2f, ground);
        }

        // Her chair or her heart. The chair sits her where she is, plays the clip through with its close-ups beside her,
        // holds while she's still answering, then she comes back to what she was doing. The heart is drawn over her screen.
        private string PlayEmote(string name)
        {
            if (!preferences.Motion) return name == "heart" ? "My Motion setting is off, so no heart this time." : "My Motion setting is off, so I'm staying put.";
            // Her chair, asked again mid-way or asked and mentioned at once, carries on: she's already in it. A heart has no
            // cooldown: each one starts over from the top the moment it's sent, so ten in ten seconds are ten hearts and none
            // waits to play after they've stopped asking.
            if (name == "chair" && emote != null && emote.Playing && emote.Name == name) return "Pulling up a chair.";
            EmoteClip what;
            try { what = EmoteClip.Load(name); }
            catch (Exception) { what = null; }
            if (what == null) return "I couldn't do that one just now.";
            if (emote == null) emote = new EmoteWindow();
            else emote.Stop();
            if (name == "heart")
            {
                Rectangle bounds = Screen.FromPoint(new Point(anchor.X + Width / 2, anchor.Y + Height / 2)).Bounds;
                emote.Play(what, EmoteWindow.HeartPlace(what, bounds), new EmotePlace(), bounds, -1, null, null, null);
                return "Here, a little heart for you.";
            }
            if (riding) StopRide();
            if (hanging) LetGo();
            PointF foot = ChairFoot();
            Rectangle area = Screen.FromPoint(Point.Round(foot)).Bounds;
            // As drawn her close-up is on her left, against the screen's left edge. In the left half of her screen it goes on
            // her right, against the right edge. It stays on her screen.
            bool mirrored = foot.X - area.Left < area.Width / 2f;
            EmotePlace seated = EmoteWindow.ChairPlace(foot, preferences.Size, mirrored);
            Rectangle chairBody = Rectangle.Round(seated.On(EmoteWindow.ChairBody));
            Point at = anchor;
            double veil = clock.Elapsed.TotalSeconds + 3600;
            // She comes apart into her ribbons where she is, the way she does to teleport, and they run to her chair. The
            // clip's own sweep of red and green into the chair starts as they land there, so it reads as one movement.
            double delay = 0;
            Bitmap self = frame;
            ribbons.Stop();
            if (self != null && Visible)
            {
                ribbons.Leave(new Bitmap(self), anchor, BodyOnScreen(anchor, lying || seat != null || onPanel ? PerchedBody : Body), RibbonColour(ArtColors.Hair), RibbonColour(ArtColors.Accent), preferences.Size);
                delay = Math.Max(0, ribbons.Go(new PointF(chairBody.Left + chairBody.Width / 2f, chairBody.Top + chairBody.Height / 2f), null, 1, mirrored) - EmoteWindow.SweepFrom / EmoteClip.Fps);
            }
            emoteVeil = veilUntil = veil;
            frames.Start();
            RenderFrame();
            bool back = false;
            emote.Play(what, seated, EmoteWindow.CloseupPlace(what, area, mirrored), area, EmoteWindow.ChairHold,
                delegate { return talkState == TalkState.Thinking || talkState == TalkState.Talking; },
                delegate { return back || anchor == at && !hidden && Visible; },
                delegate { ChairDone(veil); },
                delay, EmoteWindow.ChairLeave, delegate { back = ChairReturn(veil, chairBody); });
            return "Pulling up a chair.";
        }

        // As the clip fades her out of her chair, she comes apart into her ribbons there and they take her back to where she
        // was: her arrival standing, her perch on her ledge, or lying down where she lay, the way she lands from a teleport.
        private bool ChairReturn(double veil, Rectangle chairBody)
        {
            if (IsDisposed || veilUntil != veil || !preferences.Motion) return false;
            emoteVeil = -1;
            using (Bitmap empty = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                ribbons.Stop();
                ribbons.Leave(new Bitmap(empty), anchor, chairBody, RibbonColour(ArtColors.Hair), RibbonColour(ArtColors.Accent), preferences.Size);
            }
            form.Warm(false);
            bool standing = seat == null && !onPanel && !riding && !hanging && !lying && !walking;
            veilUntil = -1;
            if (standing && clip != dance) Play(arrive);
            else if ((seat != null || onPanel) && !lying) Play(perchArrive);
            frames.Start();
            RenderFrame();
            return true;
        }

        // Out of her chair: back to what she was doing, unless something else took over her window meanwhile (her ribbons
        // bringing her back already have).
        private void ChairDone(double veil)
        {
            if (IsDisposed || veilUntil != veil) return;
            veilUntil = emoteVeil = -1;
            bool standing = seat == null && !onPanel && !riding && !hanging && !lying && !walking && (clip == idle || clip == grin || clip == glitch || clip == dance);
            if (standing && clip != dance && preferences.Motion) Play(arrive);
            frames.Start();
            RenderFrame();
        }

        // Typed or said: Vergil or Devil May Cry gets her chair as she starts answering. Her answer still comes.
        private void EmoteOnMention(string text, bool spoken)
        {
            if (!Mentions(text, spoken) || hidden || !Visible) return;
            PlayEmote("chair");
        }

        // Her chair and her heart: both clips are in, whole and timed at 30 a second; the chair is drawn at her size with her
        // black body kept and nothing around it but what was drawn; it holds while she answers; the asks and the mentions set
        // them off and the look-alikes don't; a mention still gets her answer; and she comes back to what she was doing.
        private void SmokeEmotes(List<string> checks, string output)
        {
            EmoteClip chair = EmoteClip.Load("chair"), heart = EmoteClip.Load("heart");
            if (chair == null || heart == null) throw new Exception("Her emotes aren't in the build.");
            if (chair.Count != 301 || heart.Count != 105 || chair.Width != 960 || chair.Height != 540 || heart.Width != 1920 || heart.Height != 1080) throw new Exception("Her emotes aren't their clips: " + chair.Count + " and " + heart.Count + " frames, " + chair.Width + "x" + chair.Height + " and " + heart.Width + "x" + heart.Height);
            if (!chair.Empty(0) || chair.Empty(4) || chair.Empty(200) || chair.Empty(299) || !heart.Empty(0) || heart.Empty(40) || heart.Empty(100)) throw new Exception("Her emotes' frames aren't where the clips have them.");
            // Sat in her chair at her usual size, hair to the chair's feet, she's as tall as she is perched.
            Point offset;
            Bitmap sat = chair.Picture(200, out offset);
            float seen = sat.Height * EmoteWindow.ChairPlace(PointF.Empty, 240, false).Scale, perched = 341f / 384 * 240;
            if (Math.Abs(seen / perched - 1) > 0.1) throw new Exception("In her chair she's " + seen + " px tall beside " + perched + " perched.");
            // Over white, her body is still black where it's drawn black, her hair still red, the chair still grey, and the
            // corners of the clip's frame are clear.
            Rectangle stage = new Rectangle(0, 0, 900, 600);
            PointF foot = new PointF(620, 560);
            int black = 0, red = 0, grey = 0, faint = 0;
            using (Bitmap frame = EmoteWindow.Frame(chair, 200, EmoteWindow.ChairPlace(foot, 240, false), stage))
                for (int y = 0; y < frame.Height; y++)
                    for (int x = 0; x < frame.Width; x++)
                    {
                        Color pixel = frame.GetPixel(x, y);
                        if (pixel.A > 230 && pixel.R < 40 && pixel.G < 40 && pixel.B < 40) black++;
                        else if (pixel.A > 230 && pixel.R > 200 && pixel.G < 60) red++;
                        else if (pixel.A > 230 && Math.Abs(pixel.R - pixel.G) < 14 && Math.Abs(pixel.G - pixel.B) < 14 && pixel.R > 70) grey++;
                        if (pixel.A > 0 && (x < 300 || y < 300)) faint++;
                    }
            if (black < 600 || red < 3000 || grey < 1500 || faint != 0) throw new Exception("Her chair frame was keyed wrong: " + black + " black, " + red + " red, " + grey + " grey, " + faint + " lit where nothing's drawn");
            // Mirrored, the same frame is the other way round about her chair's feet.
            RectangleF drawn = EmoteWindow.ChairPlace(foot, 240, false).On(new Rectangle(offset, sat.Size)), turned = EmoteWindow.ChairPlace(foot, 240, true).On(new Rectangle(offset, sat.Size));
            if (Math.Abs((foot.X - drawn.Left) - (turned.Right - foot.X)) > 0.5f || Math.Abs(drawn.Top - turned.Top) > 0.01f) throw new Exception("Her mirrored chair wasn't turned about its feet.");
            // Her close-up is its own layer: on in the frames it was drawn in, the height of her screen against the edge away
            // from her, on her own screen whichever that is, and her seated figure under it is the same still picture.
            if (chair.CloseHeight <= 0 || !chair.HasCloseup(100) || chair.HasCloseup(20) || chair.HasCloseup(200) || chair.HerPicture(100) != chair.HerPicture(160)) throw new Exception("Her chair's close-up isn't its own layer.");
            string copyPath;
            // The heart's quick copy gives the same pixels GDI+ does, facing either way, and a frame that doesn't fill its window
            // (partly off the screen, or scaled for another screen) goes through GDI+.
            {
                EmoteClip copyHeart = EmoteClip.Load("heart");
                Rectangle full = new Rectangle(0, 0, 1920, 1080);
                List<int> drawnFrames = new List<int>();
                for (int f = 0; f < copyHeart.Count; f++) { Point at; if (copyHeart.Picture(f, out at) != null) drawnFrames.Add(f); }
                EmotePlace facing = EmoteWindow.HeartPlace(copyHeart, full);
                EmotePlace turnedHeart = new EmotePlace { Scale = facing.Scale, Across = -facing.Across, Left = full.Left + full.Right - facing.Left, Top = facing.Top };
                foreach (int f in new[] { drawnFrames[0], drawnFrames[drawnFrames.Count / 2], drawnFrames[drawnFrames.Count - 1] })
                    foreach (EmotePlace way in new[] { facing, turnedHeart })
                    {
                        bool copiedIt;
                        string differs = EmoteWindow.BothWays(copyHeart, f, way, full, out copiedIt);
                        if (!copiedIt || differs != null) throw new Exception("Heart frame " + f + (way.Across < 0 ? " turnedHeart" : "") + ": " + (copiedIt ? differs : "the quick copy didn't take it"));
                    }
                bool slowPath;
                EmoteWindow.BothWays(copyHeart, drawnFrames[drawnFrames.Count / 2], facing, new Rectangle(200, 0, 1920, 1080), out slowPath);
                bool scaledPath;
                EmoteWindow.BothWays(copyHeart, drawnFrames[drawnFrames.Count / 2], EmoteWindow.HeartPlace(copyHeart, new Rectangle(0, 0, 1366, 768)), new Rectangle(0, 0, 1366, 768), out scaledPath);
                if (slowPath || scaledPath) throw new Exception("A heart frame that doesn't fill its window took the quick copy.");
                copyPath = "heart frames " + drawnFrames[0] + ", " + drawnFrames[drawnFrames.Count / 2] + " and " + drawnFrames[drawnFrames.Count - 1] + " copied facing and turned match GDI+ pixel for pixel, alpha included; partly off screen or scaled they go through GDI+";
                copyHeart.Release();
            }
            foreach (Rectangle monitor in new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(0, 0, 1366, 768), new Rectangle(1920, -200, 2560, 1440), new Rectangle(-1366, 300, 1366, 768) })
                foreach (bool mirrored in new[] { false, true })
                {
                    EmotePlace face = EmoteWindow.CloseupPlace(chair, monitor, mirrored);
                    RectangleF all = face.On(new Rectangle(0, 0, chair.CloseWidth, chair.CloseHeight));
                    if (Math.Abs(all.Height - monitor.Height) > 0.5f || Math.Abs(all.Top - monitor.Top) > 0.5f || (mirrored ? Math.Abs(all.Right - monitor.Right) : Math.Abs(all.Left - monitor.Left)) > 0.5f)
                        throw new Exception("Her close-up wasn't the height of a " + monitor.Width + "x" + monitor.Height + " screen against its " + (mirrored ? "right" : "left") + " edge: " + all);
                }
            // Her heart fills the screen it's on, the whole of it, and stays on it.
            Rectangle wide = new Rectangle(100, 50, 2560, 1440);
            EmotePlace over = EmoteWindow.HeartPlace(heart, wide);
            RectangleF whole = over.On(new Rectangle(0, 0, heart.Width, heart.Height));
            if (whole.Left > wide.Left + 0.5f || whole.Top > wide.Top + 0.5f || whole.Right < wide.Right - 0.5f || whole.Bottom < wide.Bottom - 0.5f) throw new Exception("Her heart didn't fill its screen: " + whole);
            // Her clock: 30 frames a second as drawn, the chair holding on its still frame while she's answering and going on
            // when she's done, and never holding longer than it may.
            EmoteClock plain = new EmoteClock(-1, EmoteWindow.HoldMost);
            for (int i = 0; i < 150; i++) plain.Advance(1 / 30.0, true);
            if (plain.Frame != 150) throw new Exception("Five seconds of her heart was frame " + plain.Frame + ".");
            EmoteClock held = new EmoteClock(EmoteWindow.ChairHold, EmoteWindow.HoldMost);
            held.Advance(9, true);
            if (held.Frame != EmoteWindow.ChairHold || Math.Abs(held.Held - (9 - EmoteWindow.ChairHold / 30.0)) > 1e-6) throw new Exception("Answering, her chair didn't hold on its still frame: " + held.Frame + ", held " + held.Held);
            held.Advance(1, false);
            if (held.Frame != EmoteWindow.ChairHold + 30) throw new Exception("Done answering, her chair didn't go on: " + held.Frame);
            EmoteClock stuck = new EmoteClock(EmoteWindow.ChairHold, EmoteWindow.HoldMost);
            for (int i = 0; i < 100; i++) stuck.Advance(1, true);
            if (stuck.Frame < chair.Count) throw new Exception("A stuck answer kept her in her chair.");
            // The asks, typed or said, and what isn't one.
            string tool;
            ToolArguments args;
            string[] chairs = { "can you sit on a chair", "Capri, can u sit on a chair?", "sit on a chair", "sit in a chair please", "pull up a chair", "grab a chair", "sit down on the chair", "do the vergil chair" };
            string[] hearts = { "make a heart", "make me a heart", "give me a finger heart", "finger heart", "mini heart", "do a little heart", "show me a heart" };
            foreach (string ask in chairs) if (!Hands.Heard(ask, out tool, out args) || tool != "animate" || args.Name != "chair" || !Hands.Valid(tool, args)) throw new Exception("\"" + ask + "\" didn't get her chair: " + tool + " " + (args == null ? "" : args.Name));
            foreach (string ask in hearts) if (!Hands.Heard(ask, out tool, out args) || tool != "animate" || args.Name != "heart" || !Hands.Valid(tool, args)) throw new Exception("\"" + ask + "\" didn't get her heart: " + tool + " " + (args == null ? "" : args.Name));
            foreach (string other in new[] { "sit on my chair cushion order", "is this chair comfy", "my heart hurts", "heart of the cards", "sit", "sit on my discord window" })
                if (Hands.Heard(other, out tool, out args) && tool == "animate" && EmoteName(args.Name)) throw new Exception("\"" + other + "\" set off an emote.");
            foreach (string said in new[] { "what do you think about devil may cry", "Capri, what do you think about Devil May Cry?", "vergil", "Vergil's motivated", "I love devil-may-cry 5", "who would win, vergil or dante" })
                if (!VergilMention.IsMatch(said)) throw new Exception("\"" + said + "\" wasn't a mention of Vergil.");
            foreach (string said in new[] { "virgil", "my friend Virgil", "may cry", "devil may", "the devil wears prada", "she may cry", "vergilius" })
                if (VergilMention.IsMatch(said)) throw new Exception("\"" + said + "\" was taken for Vergil.");
            if (Hands.Heard("what do you think about devil may cry", out tool, out args) && tool == "animate") throw new Exception("A question about Devil May Cry became a plain move instead of an answer.");
            using (Bitmap sheet = new Bitmap(4 * 480, 3 * 270, PixelFormat.Format32bppArgb))
            using (Graphics drawnSheet = Graphics.FromImage(sheet))
            {
                drawnSheet.Clear(Color.FromArgb(96, 128, 160));
                int[] chairFrames = { 8, 19, 24, 40, 70, 120, 200, 285 }, heartFrames = { 16, 30, 90, 101 };
                for (int i = 0; i < 12; i++)
                {
                    bool isChair = i < 8;
                    EmoteClip what = isChair ? chair : heart;
                    Rectangle cell = new Rectangle(0, 0, what.Width, what.Height);
                    EmotePlace where = isChair ? EmoteWindow.ChairPlace(new PointF(EmoteWindow.ChairFootX, EmoteWindow.ChairFootY), 240, false) : EmoteWindow.HeartPlace(heart, cell);
                    using (Bitmap frame = EmoteWindow.Frame(what, isChair ? chairFrames[i] : heartFrames[i - 8], where, cell))
                        drawnSheet.DrawImage(frame, new Rectangle((i % 4) * 480, (i / 4) * 270, 480, 270));
                }
                sheet.Save(Path.Combine(output, "emotes.png"), ImageFormat.Png);
            }
            chair.Release();
            heart.Release();
            string timing = "", heartRun = "", brainHearts = "", localHearts = "";
            ThrowCheck(checks, delegate(List<string> list, List<short[]> sounds)
            {
                Func<string, string> thinkWas = Think;
                bool silentWas = preferences.Silent;
                try
                {
                    preferences.Silent = true;
                    Rectangle work = Screen.PrimaryScreen.WorkingArea;
                    StandForThrow(new Point(work.Left + work.Width * 2 / 3, work.Top + work.Height / 2));
                    RenderFrame();
                    Point at = anchor;
                    PointF feet = ChairFoot();
                    if (Math.Abs(feet.Y - (anchor.Y + PaddingSize + preferences.Size * StandY)) > 0.01f) throw new Exception("Standing, her chair's feet weren't on her floor.");
                    // Asked: she sits where she is, her own window waits, and the chair holds while she talks.
                    string line = Animate("chair");
                    double now = clock.Elapsed.TotalSeconds;
                    if (!ribbons.Going || !ribbons.Visible) throw new Exception("She didn't go to her chair as her ribbons.");
                    if (line != "Pulling up a chair." || emote == null || !emote.Playing || emote.Name != "chair" || veilUntil < now + 100) throw new Exception("Asked to sit on a chair she said \"" + line + "\" and the chair is " + (emote != null && emote.Playing ? "playing" : "not playing"));
                    System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
                    while (emote.At < 12 && waited.ElapsedMilliseconds < 2000) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                    if (emote.At < 12 || !emote.Visible) throw new Exception("Her chair didn't play: frame " + emote.At + ", shown " + emote.Visible);
                    if (Animate("chair") != "Pulling up a chair." || emote.At < 12) throw new Exception("Asked again, her chair started over.");
                    emote.Stop();
                    if (emote.Playing || emote.Visible || veilUntil != -1 || clip != arrive || anchor != at) throw new Exception("Out of her chair she didn't come back where she was: veiled until " + veilUntil + ", " + clip.Title);
                    RenderFrame();
                    // Mentioned: the chair as she starts answering, and her answer still comes.
                    CloseTalk();
                    Think = delegate { return "Vergil sat in a chair and won the internet."; };
                    Hear("Capri, what do you think about Devil May Cry?");
                    if (emote == null || !emote.Playing || emote.Name != "chair") throw new Exception("Devil May Cry didn't put her in her chair.");
                    WaitVoice(delegate { return reply != null; }, "A mention of Devil May Cry got no answer.");
                    if (reply != "Vergil sat in a chair and won the internet.") throw new Exception("A mention of Devil May Cry got \"" + reply + "\".");
                    if (!emote.Playing) throw new Exception("Her chair ended before her answer did.");
                    CloseTalk();
                    emote.Stop();
                    RenderFrame();
                    Think = delegate { return "My cat's called that too."; };
                    Hear("my friend Virgil may cry");
                    WaitVoice(delegate { return reply != null; }, "A look-alike got no answer.");
                    if (emote.Playing) throw new Exception("Virgil put her in her chair.");
                    CloseTalk();
                    // Asked in words, with a brain or without, the chair goes straight to her hands.
                    Think = delegate { return "A brain was asked for a chair."; };
                    Hear("can you sit on a chair");
                    WaitVoice(delegate { return reply != null; }, "Asking for a chair got no answer.");
                    if (reply != "Pulling up a chair." || !emote.Playing) throw new Exception("Asking for a chair got \"" + reply + "\".");
                    CloseTalk();
                    emote.Stop();
                    double chairEnded = clock.Elapsed.TotalSeconds;
                    RenderFrame();
                    // Her heart: over her screen, with her own window still showing.
                    Think = delegate { return "A brain was asked for a heart."; };
                    int heartPlays = emote.Plays;
                    Hear("make a heart");
                    WaitVoice(delegate { return reply != null; }, "Asking for a heart got no answer.");
                    // Her line can come after the heart's 3.5 s on a first run (her bubble's first draw in a new process), so the heart
                    // has to have started, and to be playing or to have played through; no veil may be over her.
                    bool heartWent = emote.Plays == heartPlays + 1 && (emote.Playing ? emote.Name == "heart" : emote.Ended == null && emote.StoppedEarly == null && emote.FramesShown > 0);
                    if (reply != "Here, a little heart for you." || !heartWent || veilUntil > clock.Elapsed.TotalSeconds)
                    {
                        double heartNow = clock.Elapsed.TotalSeconds;
                        throw new Exception("Asking for a heart got \"" + reply + "\": started " + (emote.Plays - heartPlays) + ", " + emote.FramesShown + " frames shown, " + (emote.LastPlayed > 0 ? "started " + (System.Diagnostics.Stopwatch.GetTimestamp() - emote.LastPlayed) * 1.0 / System.Diagnostics.Stopwatch.Frequency + " s before the check, " : "") + "playing " + emote.Playing + ", name " + emote.Name + ", at frame " + emote.At + ", plays " + emote.Plays
                            + (emote.Ended == null ? "" : ", ended by " + emote.Ended) + (emote.StoppedEarly == null ? "" : ", stopped " + emote.StoppedEarly)
                            + ", veiled until " + veilUntil.ToString("0.000") + " (emote veil " + emoteVeil.ToString("0.000") + "), now " + heartNow.ToString("0.000") + ", chair ended " + (heartNow - chairEnded).ToString("0.000") + " s before, clip " + (clip == null ? "none" : clip.Title)
                            + ", ribbons " + (ribbons.Waiting ? "waiting" : ribbons.Going ? "going" : "off") + ", instant " + instant + ", process up " + (DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime).TotalSeconds.ToString("0.0") + " s");
                    }
                    CloseTalk();
                    emote.Stop();
                    // A heart asked for on her way to her chair, or while she sits in it, plays at once: the chair gives way, she's
                    // back where she stood, and no veil stays over her.
                    foreach (double wait in new[] { 0.15, 2.5 })
                    {
                        Point chairFrom = anchor;
                        Animate("chair");
                        Pump(wait);
                        Animate("heart");
                        if (!emote.Playing || emote.Name != "heart") throw new Exception("A heart asked for " + wait + " s into her chair didn't play.");
                        System.Diagnostics.Stopwatch settle = System.Diagnostics.Stopwatch.StartNew();
                        while ((veiled || clock.Elapsed.TotalSeconds < veilUntil || ribbons.Waiting) && settle.Elapsed.TotalSeconds < 2) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                        if (veiled || clock.Elapsed.TotalSeconds < veilUntil || emoteVeil != -1 || anchor != chairFrom || !emote.Playing || emote.Name != "heart")
                            throw new Exception("After a heart " + wait + " s into her chair: veiled " + veiled + ", veiled until " + veilUntil.ToString("0.000") + " at " + clock.Elapsed.TotalSeconds.ToString("0.000") + ", emote veil " + emoteVeil + ", at " + anchor + " from " + chairFrom + ", heart playing " + emote.Playing);
                        emote.Stop();
                        RenderFrame();
                        Pump(0.6);
                    }
                    // Said out loud, speech to text's "Virgil" is him; typed, it isn't.
                    if (!Mentions("what do you think of Virgil", true) || Mentions("what do you think of Virgil", false) || Mentions("she may cry", true)) throw new Exception("Heard and typed Virgil weren't told apart.");
                    Think = delegate { return "Motivated."; };
                    Hear("what do you think of virgil", "what do you think of virgil", "> you: ", 0, false, null, true);
                    if (!emote.Playing || emote.Name != "chair") throw new Exception("A spoken Virgil didn't put her in her chair.");
                    WaitVoice(delegate { return reply != null; }, "A spoken Virgil got no answer.");
                    CloseTalk();
                    emote.Stop();
                    RenderFrame();
                    Hear("what do you think of virgil");
                    WaitVoice(delegate { return reply != null; }, "A typed Virgil got no answer.");
                    if (emote.Playing) throw new Exception("A typed Virgil put her in her chair.");
                    CloseTalk();
                    // Perched on a ledge, her chair stands on it, and afterwards she's still perched there.
                    seat = new Ledge { Floor = true, Left = anchor.X - 400, Right = anchor.X + Width + 400, Y = Hip().Y };
                    Play(perch);
                    RenderFrame();
                    Ledge ledge = seat;
                    Point perchedAt = anchor;
                    if (Math.Abs(ChairFoot().Y - Hip().Y) > 0.01f) throw new Exception("Perched, her chair's feet weren't on her ledge.");
                    Animate("chair");
                    if (!emote.Playing) throw new Exception("Perched, she didn't sit on her chair.");
                    emote.Stop();
                    if (veilUntil != -1 || seat != ledge || clip != perch || anchor != perchedAt) throw new Exception("After her chair she wasn't back on her ledge: " + (clip == null ? "no clip" : clip.Title));
                    RenderFrame();
                    // Lying down, and afterwards still lying there.
                    lying = true;
                    Play(laydown);
                    RenderFrame();
                    Animate("chair");
                    if (!emote.Playing) throw new Exception("Lying down, she didn't sit on her chair.");
                    emote.Stop();
                    if (veilUntil != -1 || !lying || seat != ledge || clip != laydown || anchor != perchedAt) throw new Exception("After her chair she wasn't lying down again: " + (clip == null ? "no clip" : clip.Title));
                    lying = false;
                    seat = null;
                    StandForThrow(new Point(work.Left + work.Width * 2 / 3, work.Top + work.Height / 2));
                    RenderFrame();
                    // Each played through in real time: how many of its frames reach the screen, and how long her thread spends
                    // on each one now that the next few are decoded ahead.
                    foreach (string which in new[] { "chair", "heart" })
                    {
                        Point standingAt = anchor;
                        Animate(which);
                        System.Diagnostics.Stopwatch playing = System.Diagnostics.Stopwatch.StartNew();
                        bool ribbonsBack = false;
                        while (emote.Playing && playing.Elapsed.TotalSeconds < 20)
                        {
                            Application.DoEvents();
                            System.Threading.Thread.Sleep(1);
                            if (emote.At >= EmoteWindow.ChairLeave && ribbons.Going) ribbonsBack = true;
                        }
                        if (emote.Playing) throw new Exception("Her " + which + " never finished.");
                        if (which == "chair")
                        {
                            // As the clip fades her out of her chair her ribbons take her back, and she forms where she stood.
                            // In a check she has no arrival of her own (instant), so her waiting ribbons are sent home on her next frame,
                            // which can come just after the clip's last one: keep watching until they've landed and her veil is over.
                            while ((ribbons.Waiting || clock.Elapsed.TotalSeconds < veilUntil + 0.05) && playing.Elapsed.TotalSeconds < 22)
                            {
                                Application.DoEvents();
                                System.Threading.Thread.Sleep(1);
                                if (ribbons.Going) ribbonsBack = true;
                            }
                            if (!ribbonsBack || veilUntil > clock.Elapsed.TotalSeconds || anchor != standingAt || emoteVeil != -1) throw new Exception("Her ribbons didn't bring her back from her chair: " + ribbonsBack + ", veiled until " + veilUntil + ", at " + anchor + " from " + standingAt);
                        }
                        RenderFrame();
                        int count = which == "chair" ? 301 : 105;
                        timing += (timing.Length > 0 ? "; " : "") + which + " " + emote.FramesShown + " of " + count + " frames shown in " + playing.Elapsed.TotalSeconds.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s, "
                            + (emote.DrawTotal / Math.Max(1, emote.FramesShown)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms a frame on her thread, worst " + emote.DrawWorst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms ("
                            + string.Join("/", new[] { emote.PickTotal, emote.MakeTotal, emote.PaintTotal, emote.ShowTotal }.Select(delegate(double v) { return (v / Math.Max(1, emote.FramesShown)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture); }).ToArray()) + " ms picking/surface/painting/showing)";
                    }
                    // No cooldown: ten asks in ten seconds are ten hearts, each from the top the moment it's asked, and nothing
                    // plays on after the last one.
                    CloseTalk();
                    Think = delegate { return "A brain was asked for a heart."; };
                    int playsBefore = emote.Plays;
                    long lastAsk = 0;
                    for (int ask = 0; ask < 10; ask++)
                    {
                        lastAsk = System.Diagnostics.Stopwatch.GetTimestamp();
                        Hear("make me a heart");
                        System.Diagnostics.Stopwatch asked = System.Diagnostics.Stopwatch.StartNew();
                        while (emote.Plays < playsBefore + ask + 1 && asked.Elapsed.TotalSeconds < 0.9) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                        if (emote.Plays != playsBefore + ask + 1 || !emote.Playing || emote.Name != "heart") throw new Exception("Heart ask " + (ask + 1) + " of 10 didn't start a heart: " + (emote.Plays - playsBefore) + " started.");
                        // Started over: this play began after this ask (every play starts at its first frame). Reading its frame here
                        // instead raced her window thread, which a loaded PC can hold up for a second or two.
                        bool fresh = emote.LastPlayed >= lastAsk;
                        while (asked.Elapsed.TotalSeconds < 1) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                        if (!fresh) throw new Exception("Heart ask " + (ask + 1) + " carried on from an earlier heart instead of starting over.");
                    }
                    System.Diagnostics.Stopwatch tail = System.Diagnostics.Stopwatch.StartNew();
                    while (emote.Playing && tail.Elapsed.TotalSeconds < 6) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                    double lastEnded = tail.Elapsed.TotalSeconds;
                    while (tail.Elapsed.TotalSeconds < lastEnded + 2) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                    // The tenth heart's own run, from its first frame to running out, is its 3.5 s; and it started after the tenth ask.
                    double ownRun = emote.LastRanOut == 0 ? -1 : (emote.LastRanOut - emote.LastPlayed) * 1.0 / System.Diagnostics.Stopwatch.Frequency, afterAsk = (emote.LastPlayed - lastAsk) * 1.0 / System.Diagnostics.Stopwatch.Frequency;
                    if (emote.Playing || emote.Plays != playsBefore + 10 || ownRun < 3.3 || ownRun > 4.2 || afterAsk < 0 || afterAsk > 1 || emote.Ended != null) throw new Exception("After ten hearts something played on or ran short: ran " + ownRun.ToString("0.00") + " s from " + afterAsk.ToString("0.00") + " s after the tenth ask; " + (emote.Plays - playsBefore) + " started, the last ended " + lastEnded.ToString("0.00") + " s after the tenth ask's first second, " + emote.FramesShown + " frames shown" + (emote.Ended == null ? "" : ", stopped by " + emote.Ended) + (emote.StoppedEarly == null ? "" : ", stopped " + emote.StoppedEarly) + ".");
                    heartRun = "ten asks one second apart started ten hearts, each from its first frames, and the last played through in " + ownRun.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s, ending " + (afterAsk + ownRun).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s after the tenth ask, " + emote.FramesShown + " of 105 frames shown, with nothing after it";
                    CloseTalk();
                    // Her own brain sends one when she feels like it, with her words: in chat, mid-conversation after another tool,
                    // and with call mode on.
                    Cloud remoteWas = brain.Remote;
                    string modelWas = brain.Model, addressWas = Brain.OpenAi, savedSecurity = Security.Saved;
                    bool ownStepsWas = brain.OwnSteps, keepingWas = Brain.Keeping, wakeWas = Security.On(Security.Wake), followWas = preferences.FollowUps, callWas = CallModeOn;
                    // Her call-mode turn makes its ask the call user's. Left that way after call mode goes off, every tool on that
                    // same ask says the call request has ended, so whoever comes next (SmokeInk's draw) finds her hands refused.
                    int callUserWas = callUser, callUserVersionWas = callUserVersion, callAnswerWas = callAnswer;
                    System.Net.Sockets.TcpListener fake = null;
                    try
                    {
                        Security.Load("1111111111");
                        Brain.Keeping = false;
                        preferences.FollowUps = false;
                        Think = null;
                        brain.Remote = new Cloud("sk-proj-test", "gpt-5-mini");
                        brain.Model = "gpt-5-mini";
                        brain.OwnSteps = true;
                        Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                        Func<string, string, string, string> calls = delegate(string text, string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + (text == null ? "null" : Voice.Quote(text)) + ",\"tool_calls\":[{\"id\":\"h1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                        string heartCall = "{\"name\":\"heart\"}";
                        List<string> brainAsked = new List<string>();
                        Brain.OpenAi = FakeServer(new[]
                        {
                            calls("Aww, you're sweet.", "animate", heartCall), says("Aww, you're sweet. Thank you."),
                            calls(null, "animate", "{\"name\":\"grin\"}"), calls(null, "animate", heartCall), says("Congrats, that's huge!"),
                            calls(null, "animate", heartCall), says("You cheer me up too."),
                            says("spare"), says("spare")
                        }, brainAsked, out fake);
                        string[,] turns = { { "thank you capri, you're the best", "Aww, you're sweet. Thank you.", "chat" }, { "i passed my driving test!!", "Congrats, that's huge!", "after her grin" }, { "you always cheer me up capri", "You cheer me up too.", "call mode" } };
                        for (int turn = 0; turn < 3; turn++)
                        {
                            if (turn == 2) callSession.Change(true);
                            emote.Stop();
                            int heartsBefore = emote.Plays;
                            Hear(turns[turn, 0]);
                            System.Diagnostics.Stopwatch answered = System.Diagnostics.Stopwatch.StartNew();
                            while ((reply == null || emote.Plays == heartsBefore) && answered.Elapsed.TotalSeconds < 15) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                            if (emote.Plays != heartsBefore + 1 || emote.Name != "heart") { string last; lock (brainAsked) last = brainAsked.Count == 0 ? "" : brainAsked[brainAsked.Count - 1]; int body = last.IndexOf("\r\n\r\n", StringComparison.Ordinal); throw new Exception("Her brain's heart didn't play (" + turns[turn, 2] + "): " + (emote.Plays - heartsBefore) + " started, reply " + reply + ", " + brainAsked.Count + " brain requests, call mode " + CallModeOn + ", her turn " + (callUser == asking) + ", " + talkState + ", last request " + (body < 0 ? "" : last.Substring(body + 4, Math.Min(300, last.Length - body - 4)))); }
                            if (reply != turns[turn, 1]) throw new Exception("With her brain's heart (" + turns[turn, 2] + ") she said \"" + reply + "\" instead of her words.");
                            CloseTalk();
                            brain.Forget();
                        }
                        string sent;
                        lock (brainAsked) sent = brainAsked.Count > 0 ? brainAsked[0] : "";
                        if (!sent.Contains("yours to send whenever you feel it")) throw new Exception("Her brain wasn't told the heart is hers to send.");
                        string nightTool; ToolArguments nightArgs;
                        brainHearts = "her brain's animate heart played with her words kept, in chat, mid-turn right after another tool call, and with call mode on; \"goodnight capri\" goes to " + (Hands.Heard("goodnight capri", out nightTool, out nightArgs) ? nightTool + " " + (nightArgs.Action ?? nightArgs.Name) : "her brain");
                    }
                    finally
                    {
                        if (fake != null) fake.Stop();
                        callSession.Change(callWas);
                        callUser = callUserWas; callUserVersion = callUserVersionWas; callAnswer = callAnswerWas;
                        brain.Remote = remoteWas; brain.Model = modelWas; brain.OwnSteps = ownStepsWas; Brain.OpenAi = addressWas;
                        Brain.Keeping = keepingWas; preferences.FollowUps = followWas;
                        Security.Drop(); Security.Load(savedSecurity, wakeWas);
                        brain.Forget();
                        Think = null;
                    }
                    emote.Stop();
                    RenderFrame();
                    // A small brain on their PC writes her heart into its reply, tidily or not: each one meant plays, and no call
                    // text is ever shown or said.
                    {
                        Cloud localWas = brain.Remote;
                        string localModelWas = brain.Model, localSaved = Security.Saved;
                        Func<ToolArguments, string> localDrawWas = Hands.Draw;
                        int localUserWas = callUser, localUserVersionWas = callUserVersion, localAnswerWas = callAnswer;
                        bool localKeepingWas = Brain.Keeping, localWakeWas = Security.On(Security.Wake), localFollowWas = preferences.FollowUps, localCallWas = CallModeOn;
                        System.Net.Sockets.TcpListener local = null;
                        try
                        {
                            Security.Load("1111111111");
                            Brain.Keeping = false;
                            preferences.FollowUps = false;
                            Think = null;
                            int draws = 0;
                            Hands.Draw = delegate { draws++; return "Drew on your screen."; };
                            Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                            string[,] cases =
                            {
                                { "Aww, thank you! animate{\"name\":\"heart\"}", "Aww, thank you!", "1", "at the end" },
                                { "Aww animate{\"name\":\"heart\"} you're sweet.", "Aww you're sweet.", "1", "in the middle" },
                                { "Love you too! animate{\"name\":\"heart\"} animate{\"name\":\"heart\"}", "Love you too!", "2", "twice" },
                                { "Hehe animate{name:heart}", "Hehe", "1", "unquoted" },
                                { "Sure animate {\"name\": \"heart\"}", "Sure", "1", "spaced" },
                                { "Here you go animate{\"name\":\"hearts\"}", "Here you go", "1", "hearts" },
                                { "Thanks! animate{\"name\":\"hea", "Thanks!", "0", "cut off" },
                                { "animate{\"name\":\"heart\"}", "Here, a little heart for you.", "1", "only the call" },
                                { "Sure! animate{\"name\":\"heart\"} draw{\"shapes\":\"line 10 20 30 40\"}", "*", "1", "beside a drawing" },
                                { "Night! animate{\"name\":\"heart\"}", "Night!", "1", "call mode" }
                            };
                            string[] replies = new string[cases.GetLength(0) + 2];
                            for (int i = 0; i < cases.GetLength(0); i++) replies[i] = says(cases[i, 0]);
                            replies[replies.Length - 2] = replies[replies.Length - 1] = says("spare");
                            List<string> localAsked = new List<string>();
                            brain.Remote = new Cloud(0, "stand-in-local", FakeServer(replies, localAsked, out local));
                            brain.Model = "stand-in-local";
                            List<string> heartsSeen = new List<string>();
                            for (int i = 0; i < cases.GetLength(0); i++)
                            {
                                if (cases[i, 3] == "call mode") callSession.Change(true);
                                emote.Stop();
                                int before = emote.Plays, want = int.Parse(cases[i, 2]), drawsBefore = draws, wantDraws = cases[i, 3] == "beside a drawing" ? 1 : 0;
                                Hear("you're great");
                                System.Diagnostics.Stopwatch answered = System.Diagnostics.Stopwatch.StartNew();
                                while ((reply == null || emote.Plays < before + want) && answered.Elapsed.TotalSeconds < 15) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                                Pump(0.3);
                                string shown = reply ?? "";
                                if (emote.Plays - before != want || draws - drawsBefore != wantDraws || cases[i, 1] != "*" && shown != cases[i, 1] || shown.Length == 0 || shown.IndexOf("animate", StringComparison.OrdinalIgnoreCase) >= 0 || shown.IndexOf('{') >= 0 || shown.IndexOf('}') >= 0)
                                    throw new Exception("A small brain's heart " + cases[i, 3] + ": " + (emote.Plays - before) + " of " + want + " played, " + (draws - drawsBefore) + " drawings ran, and she said \"" + shown + "\".");
                                heartsSeen.Add(cases[i, 3] + " " + (emote.Plays - before));
                                CloseTalk();
                                brain.Forget();
                            }
                            string first;
                            lock (localAsked) first = localAsked.Count > 0 ? localAsked[0] : "";
                            if (first.IndexOf("end your reply with animate", StringComparison.Ordinal) < 0) throw new Exception("A brain on this PC wasn't told it can write her heart.");
                            // A reply that's only her heart, when it's her chat-only answer after "act" found no job: the heart is sent and
                            // she says its line, never asked again for words.
                            local.Stop();
                            local = null;
                            List<string> onlyAsked = new List<string>();
                            brain.Remote = new Cloud(0, "stand-in-local", FakeServer(new[] { says("act"), says("none"), says("none"), says("animate{\"name\":\"heart\"}"), says("spare"), says("spare") }, onlyAsked, out local));
                            callSession.Change(false);
                            emote.Stop();
                            int onlyBefore = emote.Plays;
                            Hear("you're great");
                            System.Diagnostics.Stopwatch onlyAnswered = System.Diagnostics.Stopwatch.StartNew();
                            while ((reply == null || emote.Plays == onlyBefore) && onlyAnswered.Elapsed.TotalSeconds < 15) { Application.DoEvents(); System.Threading.Thread.Sleep(5); }
                            Pump(0.3);
                            int onlyRequests;
                            lock (onlyAsked) onlyRequests = onlyAsked.Count;
                            if (emote.Plays - onlyBefore != 1 || reply != "Here, a little heart for you.") throw new Exception("A chat-only answer that was only her heart: " + (emote.Plays - onlyBefore) + " played and she said \"" + reply + "\" after " + onlyRequests + " requests.");
                            heartsSeen.Add("only the call after act " + (emote.Plays - onlyBefore) + " in " + onlyRequests + " requests");
                            CloseTalk();
                            localHearts = "a brain on this PC writing her heart played it (" + string.Join(", ", heartsSeen.ToArray()) + ") with no call text shown or said; a drawing beside it ran once; only the call says her heart line";
                        }
                        finally
                        {
                            if (local != null) local.Stop();
                            Hands.Draw = localDrawWas;
                            callSession.Change(localCallWas);
                            callUser = localUserWas; callUserVersion = localUserVersionWas; callAnswer = localAnswerWas;
                            brain.Remote = localWas; brain.Model = localModelWas;
                            Brain.Keeping = localKeepingWas; preferences.FollowUps = localFollowWas;
                            Security.Drop(); Security.Load(localSaved, localWakeWas);
                            brain.Forget();
                            Think = null;
                            emote.Stop();
                        }
                    }
                    // And after it all, her hands still work: a plain draw goes through.
                    {
                        Func<ToolArguments, string> drawWas = Hands.Draw;
                        ToolArguments drew = null;
                        try
                        {
                            Hands.Draw = delegate(ToolArguments given) { drew = given; return "Drew on your screen."; };
                            string drawResult = DoTool("draw", new ToolArguments { Shapes = "line 10 20 30 40" });
                            if (drawResult != "Drew on your screen." || drew == null) throw new Exception("After her hearts and chair a plain draw didn't go through: \"" + drawResult + "\"");
                        }
                        finally { Hands.Draw = drawWas; }
                    }
                    // With Motion off she says so and stays put.
                    preferences.Motion = false;
                    string still = Animate("chair");
                    if (!still.Contains("Motion") || emote.Playing || veilUntil > clock.Elapsed.TotalSeconds) throw new Exception("With Motion off she still sat on her chair: \"" + still + "\", " + (emote.Playing ? "playing" : "not playing") + ", veiled until " + veilUntil + " at " + clock.Elapsed.TotalSeconds);
                    preferences.Motion = true;
                }
                finally
                {
                    Think = thinkWas;
                    preferences.Silent = silentWas;
                    CloseTalk();
                    if (emote != null) emote.Stop();
                    ribbons.Stop();
                    veilUntil = emoteVeil = -1;
                }
            });
            checks.Add("PASS emotes: her chair (301 frames) and her heart (105 frames, art: Qewiw) play their own frames at 30 a second, the chair at her size with her black body kept; asks, typed or said, and Vergil or Devil May Cry set them off, look-alikes don't; a mention still gets her answer, and a heard Virgil counts where a typed one doesn't; she comes back where she was, standing, perched or lying; Motion off keeps her put");
            checks.Add("PASS her heart's quick copy: " + copyPath);
            checks.Add("PASS hearts have no cooldown: " + heartRun + "; " + brainHearts);
            checks.Add("PASS small brains' hearts: " + localHearts);
            checks.Add("PASS emotes played in real time on this desktop, next frames decoded ahead: " + timing);
        }
        // Smoke only, asked for by name with CAPRI_EMOTE_FILM set to a folder: records of her real windows on this (hidden)
        // desktop, from a second before she's asked until she's back standing, for chair-real-run.mp4 and heart-real-run.mp4,
        // made from them afterwards. Her stand-in brain answers; nothing is said out loud.
        private void SmokeEmoteFilms(List<string> checks, string output)
        {
            string folder = Environment.GetEnvironmentVariable("CAPRI_EMOTE_FILM");
            if (string.IsNullOrEmpty(folder)) { checks.Add("SKIP emote films: CAPRI_EMOTE_FILM isn't set"); return; }
            Directory.CreateDirectory(folder);
            Func<string, string> thinkWas = Think;
            bool silentWas = preferences.Silent, motionWas = preferences.Motion;
            List<string> notes = new List<string>();
            try
            {
                preferences.Silent = true;
                preferences.Motion = true;
                Rectangle bounds = Screen.PrimaryScreen.Bounds, work = Screen.PrimaryScreen.WorkingArea;
                notes.Add("screen " + bounds + ", work area " + work);
                foreach (string which in new[] { "chair", "heart" })
                {
                    CloseTalk();
                    if (emote != null) emote.Stop();
                    ribbons.Stop();
                    veilUntil = emoteVeil = -1;
                    StandForThrow(new Point(work.Left + work.Width * 3 / 4, work.Top + work.Height / 2));
                    Play(idle);
                    RenderFrame();
                    Point standingAt = anchor;
                    string path = Path.Combine(folder, which + "-record");
                    EmoteFilm.Start(path);
                    Pump(1.0);
                    System.Diagnostics.Stopwatch run = System.Diagnostics.Stopwatch.StartNew();
                    if (which == "chair")
                    {
                        Think = delegate { return "Vergil sat in a chair and won the internet."; };
                        Hear("Capri, what do you think about Devil May Cry?");
                    }
                    else
                    {
                        Think = delegate { return "A brain was asked for a heart."; };
                        Hear("make me a heart");
                    }
                    while ((emote == null || !emote.Playing) && run.Elapsed.TotalSeconds < 4.5) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
                    if (emote == null || !emote.Playing || emote.Name != which) throw new Exception("The " + which + " film's ask didn't start her " + which + ".");
                    double asked = clock.Elapsed.TotalSeconds, formed = -1;
                    while ((emote.Playing || clock.Elapsed.TotalSeconds < veilUntil || ribbons.Visible) && run.Elapsed.TotalSeconds < 25)
                    {
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(1);
                    }
                    formed = clock.Elapsed.TotalSeconds;
                    Pump(1.0);
                    EmoteFilm.Stop();
                    if (anchor != standingAt) throw new Exception("After her " + which + " she wasn't where she stood.");
                    notes.Add(which + ": back standing " + (formed - asked).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s after the ask; window pictures recorded " + EmoteFilm.Copies + " (worst copy " + EmoteFilm.CopyWorst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms), window samples " + EmoteFilm.Samples + "; "
                        + (emote.FramesShown) + " clip frames shown, " + (emote.DrawTotal / Math.Max(1, emote.FramesShown)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms a frame, worst " + emote.DrawWorst.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " ms");
                    if (!File.Exists(Path.Combine(path, "pictures.bin")) || new FileInfo(Path.Combine(path, "pictures.bin")).Length < 10000) throw new Exception("No " + which + " film was written.");
                }
                checks.Add("PASS emote films on this desktop: " + string.Join("; ", notes.ToArray()));
            }
            finally
            {
                EmoteFilm.Stop();
                Think = thinkWas;
                preferences.Silent = silentWas;
                preferences.Motion = motionWas;
                CloseTalk();
                if (emote != null) emote.Stop();
                ribbons.Stop();
                veilUntil = emoteVeil = -1;
                File.WriteAllLines(Path.Combine(folder, "film-notes.txt"), notes.ToArray());
            }
        }

        private static void Pump(double seconds)
        {
            System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
            while (waited.Elapsed.TotalSeconds < seconds) { Application.DoEvents(); System.Threading.Thread.Sleep(1); }
        }
        // A heart started while a game job's busy messages starve her window's timers still plays through: her emote window
        // steps on her own clock, not on a window timer.
        private void SmokeEmoteStarved(List<string> checks)
        {
            ThrowCheck(checks, delegate(List<string> list, List<short[]> sounds)
            {
                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                StandForThrow(new Point(work.Left + work.Width * 2 / 3, work.Top + work.Height / 2));
                RenderFrame();
                bool motionWas = preferences.Motion;
                preferences.Motion = true;
                Starver keeper = new Starver(true);
                System.Windows.Forms.Timer probe = new System.Windows.Forms.Timer { Interval = 33 };
                int probed = 0;
                probe.Tick += delegate { if (!keeper.Done) probed++; };
                System.Diagnostics.Stopwatch busy = System.Diagnostics.Stopwatch.StartNew();
                double started = -1, ended = -1;
                keeper.Work = delegate
                {
                    System.Threading.Thread.Sleep(2);
                    double now = busy.Elapsed.TotalSeconds;
                    if (started < 0 && now >= 0.5) { Animate("heart"); started = now; }
                    if (started >= 0 && ended < 0 && emote != null && !emote.Playing) ended = now;
                    if (now >= 7 || ended >= 0 && now - ended >= 0.3) keeper.Done = true;
                };
                try
                {
                    probe.Start();
                    keeper.Kick();
                    while (!keeper.Done) Starver.Pump(50);
                    string said = keeper.Count + " busy messages, " + probed + " window-timer ticks while it ran; the heart started "
                        + started.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s in and " + (ended < 0 ? "was still going at 7 s" : "ended " + (ended - started).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s later")
                        + ", " + (emote == null ? 0 : emote.FramesShown) + " of 105 frames shown" + (emote == null || emote.Ended == null ? "" : ", stopped by " + emote.Ended);
                    if (keeper.Count < 50 || probed > 3) list.Add("FAIL the stand-in didn't starve her window timers, so it proves nothing: " + said);
                    else if (emote == null || ended < 0 || ended - started > 4.2 || emote.FramesShown < 90 || emote.Ended != null) list.Add("FAIL her heart didn't play through while her window timers were starved: " + said);
                    else list.Add("PASS her heart plays through while her window timers are starved: " + said);
                }
                finally
                {
                    keeper.Done = true;
                    probe.Stop();
                    probe.Dispose();
                    Application.DoEvents();
                    keeper.DestroyHandle();
                    if (emote != null) emote.Stop();
                    preferences.Motion = motionWas;
                }
            });
        }
    }
}
