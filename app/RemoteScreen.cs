using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    // Capri Remote's screen view (remote/PROTOCOL.md section 8.2). A paired phone that asks watches one of the PC's monitors
    // a few frames a second, shrunk to suit the phone and sealed like every other message. Nothing goes while the screen
    // doesn't change, each frame is split into parts under the relay's frame cap, and at most two frames go ahead of the
    // phone's last ack, so a slow phone gets fewer frames instead of a backlog. The monitor is the one the phone picked or,
    // with Auto, the one she's looking at. There's no window or capture in here: Capri's own source is app/RemoteLive.cs
    // (FrameGrab), and app/RemoteTests draws its own.

    // A monitor a phone can pick: its number as Windows counts it ("2" for \\.\DISPLAY2), and where it sits.
    internal sealed class RemoteMonitor
    {
        internal string Id, Name, Device;
        internal Rectangle Bounds;
        internal bool Primary;
    }

    // One watcher's capture of one monitor at a time.
    internal abstract class RemoteCapture : IDisposable
    {
        // Starts on a monitor, or moves to another. False when it can't be captured now.
        internal abstract bool Open(RemoteMonitor monitor);

        // The newest frame, at most maxWidth wide. Null when the screen hasn't changed since the last frame this returned at
        // that width, or when it can't be read just now.
        internal abstract Bitmap Next(int maxWidth);

        public abstract void Dispose();
    }

    // What the PC can show a phone: its monitors, the one she's looking at, the mouse, and captures.
    internal abstract class RemoteScreenSource
    {
        internal abstract List<RemoteMonitor> Monitors();

        // Auto's pick: the id of the monitor she's looking at, or null for the main one.
        internal abstract string Looking();

        // The mouse on the whole desktop, or a point off every monitor when there's none.
        internal abstract Point Mouse();

        internal abstract RemoteCapture Capture();
    }

    // How a phone asked to watch: the widest frame, its JPEG quality and how many frames a second.
    internal sealed class RemoteQuality
    {
        internal readonly string Name;
        internal readonly int Width, Jpeg, Fps;

        internal static readonly RemoteQuality Saver = new RemoteQuality("saver", 640, 40, 2);
        internal static readonly RemoteQuality Balanced = new RemoteQuality("balanced", 960, 55, 3);
        internal static readonly RemoteQuality Sharp = new RemoteQuality("sharp", 1280, 65, 4);
        internal static readonly RemoteQuality[] All = { Saver, Balanced, Sharp };

        private RemoteQuality(string name, int width, int jpeg, int fps)
        {
            Name = name;
            Width = width;
            Jpeg = jpeg;
            Fps = fps;
        }

        internal static RemoteQuality Named(string name)
        {
            foreach (RemoteQuality quality in All) if (quality.Name == name) return quality;
            return null;
        }
    }

    // A phone watching or on a call, for the PC's badge.
    internal sealed class RemoteWatcher
    {
        internal string Device, Name;
        internal RemoteMonitor Monitor;
    }

    // What went out, for the tests' numbers: frames, their parts, JPEG bytes, and the bytes of sealed envelopes the relay
    // carries to the phone for them.
    internal sealed class RemoteScreenStats
    {
        internal long Frames, Parts, Jpeg, Wire, Cursors, Unchanged, Failed;
    }

    internal sealed class RemoteScreen : IDisposable
    {
        // JPEG bytes in one watch.frame part: 58,667 base64url characters, so a sealed part stays under 60,000 bytes and its
        // frame under the relay's 65,536 (PROTOCOL.md 4.1).
        internal const int PartBytes = 44000;
        internal const int Ahead = 2;
        private const int AckWaitMs = 4000, StallMs = 15000, ListMs = 2000, SwitchMs = 1000, NapMs = 250;

        // The phones watching changed, or the monitor one watches: the badge reads Watchers(). Raised on a worker thread.
        internal event Action Changed;

        // Whether a phone may watch. Every paired phone may unless this says otherwise.
        internal Func<string, bool> Allowed = delegate { return true; };

        private static ImageCodecInfo jpegCodec;
        private readonly RemoteLink link;
        private readonly RemoteScreenSource source;
        private readonly object gate = new object();
        private readonly Dictionary<string, Watch> watches = new Dictionary<string, Watch>(StringComparer.Ordinal);
        private readonly List<Watch> ended = new List<Watch>();
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly RemoteScreenStats stats = new RemoteScreenStats();
        private Thread worker;
        private bool disposed;

        internal RemoteScreen(RemoteLink link, RemoteScreenSource source)
        {
            if (link == null) throw new ArgumentNullException("link");
            if (source == null) throw new ArgumentNullException("source");
            this.link = link;
            this.source = source;
            link.Offer("watch");
            link.LiveHeard += Heard;
            link.Happened += Happened;
        }

        private sealed class Watch
        {
            internal string Device, Pick = "auto";
            internal RemoteQuality Quality = RemoteQuality.Balanced;
            internal int ViewWide = 1280, ViewTall = 1280;
            internal RemoteCapture Capture;
            internal RemoteMonitor Shown, Candidate;
            internal List<RemoteMonitor> Monitors;
            internal string ListSaid;
            internal long CandidateAt, ListAt, DueAt, SentAt, AckAt;
            internal int N, Acked, InFlight, LastX = int.MinValue, LastY = int.MinValue;
            internal bool Tell, Gone, Missing;
        }

        // ---------- What the badge and the tests read ----------

        internal List<RemoteWatcher> Watchers()
        {
            List<RemoteWatcher> list = new List<RemoteWatcher>();
            lock (gate)
                foreach (Watch watch in watches.Values)
                    list.Add(new RemoteWatcher { Device = watch.Device, Name = link.PhoneName(watch.Device), Monitor = watch.Shown });
            return list;
        }

        internal bool Watching(string device) { lock (gate) return device != null && watches.ContainsKey(device); }

        internal RemoteScreenStats Stats()
        {
            lock (gate) return new RemoteScreenStats { Frames = stats.Frames, Parts = stats.Parts, Jpeg = stats.Jpeg, Wire = stats.Wire, Cursors = stats.Cursors, Unchanged = stats.Unchanged, Failed = stats.Failed };
        }

        // Ends every watch, as when the PC turns Remote off.
        public void Dispose()
        {
            Thread running;
            List<Watch> all = new List<Watch>();
            lock (gate)
            {
                if (disposed) return;
                disposed = true;
                link.LiveHeard -= Heard;
                link.Happened -= Happened;
                all.AddRange(watches.Values);
                all.AddRange(ended);
                watches.Clear();
                ended.Clear();
                running = worker;
                worker = null;
            }
            wake.Set();
            if (running != null && running != Thread.CurrentThread) running.Join(3000);
            foreach (Watch watch in all) Close(watch);
            RaiseChanged();
        }

        // ---------- A phone's messages ----------

        private void Heard(string device, RemoteJson.Map message)
        {
            string type = message.Text("type");
            if (type == "watch.start") Start(device, message);
            else if (type == "watch.stop") Stop(device, null);
            else if (type == "watch.ack")
            {
                double n;
                if (!message.Number("n", out n) || n != Math.Floor(n) || n < 0) return;
                lock (gate)
                {
                    Watch watch;
                    if (!watches.TryGetValue(device, out watch) || n > watch.N) return;
                    if ((int)n > watch.Acked) watch.Acked = (int)n;
                    watch.AckAt = Now();
                }
                wake.Set();
            }
        }

        // watch.start begins a watch, or changes what one shows: a monitor ("auto" or an id from watch.monitors), a quality
        // and the size the phone shows it at, in its own pixels.
        private void Start(string device, RemoteJson.Map message)
        {
            string pick = message.Text("monitor") ?? "auto";
            RemoteQuality quality = message.ContainsKey("quality") ? RemoteQuality.Named(message.Text("quality")) : RemoteQuality.Balanced;
            double wide, tall;
            if (!message.Number("width", out wide)) wide = 1280;
            if (!message.Number("height", out tall)) tall = 1280;
            if (quality == null || !(pick == "auto" || RemoteMessages.IdOk(pick) && pick.Length <= 8) || wide < 64 || tall < 64 || wide > 8192 || tall > 8192)
            {
                link.SendLive(device, new[] { RemoteMessages.Error("bad-message", null) }, null);
                return;
            }
            if (!Allowed(device))
            {
                link.SendLive(device, new[] { StateMessage(false, "not-allowed") }, null);
                return;
            }
            bool fresh = false;
            lock (gate)
            {
                if (disposed) return;
                Watch watch;
                if (!watches.TryGetValue(device, out watch))
                {
                    watch = new Watch { Device = device, AckAt = Now() };
                    watches[device] = watch;
                    fresh = true;
                }
                if (watch.Pick != pick) watch.Candidate = null;
                watch.Pick = pick;
                watch.Quality = quality;
                watch.ViewWide = (int)wide;
                watch.ViewTall = (int)tall;
                watch.DueAt = 0;
                watch.ListAt = 0;
                watch.Tell = true;
                // A change of mind restarts the ack window: what was in flight is the old view.
                watch.Acked = watch.N;
                watch.AckAt = Now();
                if (worker == null)
                {
                    worker = new Thread(Run) { IsBackground = true, Name = "Capri Remote screen", Priority = ThreadPriority.BelowNormal };
                    worker.Start();
                }
            }
            if (fresh) link.SendLive(device, new[] { StateMessage(true, null) }, null);
            wake.Set();
            RaiseChanged();
        }

        // Ends a phone's watch: it asked, it left, or its session ended. reason goes to the phone when it still has a session.
        private void Stop(string device, string reason)
        {
            lock (gate)
            {
                Watch watch;
                if (device == null || !watches.TryGetValue(device, out watch)) return;
                watches.Remove(device);
                watch.Gone = true;
                ended.Add(watch);
            }
            if (reason != null) link.SendLive(device, new[] { StateMessage(false, reason) }, null);
            wake.Set();
            RaiseChanged();
        }

        private void Happened(string what, string about)
        {
            // A phone that left, was kicked or removed, or started a new session (a new hello) has stopped watching. It asks
            // again in its new session if it still wants to.
            if (what == "left" && about == null)
            {
                List<string> all;
                lock (gate) all = new List<string>(watches.Keys);
                foreach (string device in all) Stop(device, null);
            }
            else if (what == "left" || what == "kicked" || what == "removed" || what == "session") Stop(about, null);
        }

        // ---------- The worker ----------

        private void Run()
        {
            try
            {
                while (true)
                {
                    List<Watch> due = new List<Watch>(), done;
                    int nap = NapMs;
                    lock (gate)
                    {
                        if (disposed || worker != Thread.CurrentThread) return;
                        done = new List<Watch>(ended);
                        ended.Clear();
                        if (watches.Count == 0 && done.Count == 0)
                        {
                            worker = null;
                            return;
                        }
                        long now = Now();
                        foreach (Watch watch in watches.Values)
                        {
                            if (now >= watch.DueAt) due.Add(watch);
                            else nap = (int)Math.Min(nap, watch.DueAt - now);
                        }
                    }
                    foreach (Watch watch in done) Close(watch);
                    foreach (Watch watch in due)
                    {
                        try { Step(watch); }
                        catch (Exception) { lock (gate) stats.Failed++; }
                    }
                    wake.WaitOne(Math.Max(10, nap));
                }
            }
            // Anything thrown on this thread would end her: the watches stop instead, and the next watch.start starts over.
            catch (Exception) { }
            finally
            {
                lock (gate) if (worker == Thread.CurrentThread) worker = null;
            }
        }

        private static void Close(Watch watch)
        {
            RemoteCapture capture = watch.Capture;
            watch.Capture = null;
            if (capture == null) return;
            try { capture.Dispose(); }
            catch (Exception) { }
        }

        // One watch's turn: its monitor list, Auto's pick, then a frame if the screen changed and the phone is keeping up.
        private void Step(Watch watch)
        {
            long now = Now();
            string pick;
            RemoteQuality quality;
            int viewWide, viewTall;
            lock (gate)
            {
                if (watch.Gone) return;
                watch.DueAt = now + 1000 / watch.Quality.Fps;
                pick = watch.Pick;
                quality = watch.Quality;
                viewWide = watch.ViewWide;
                viewTall = watch.ViewTall;
            }
            if (watch.Monitors == null || now >= watch.ListAt)
            {
                watch.ListAt = now + ListMs;
                List<RemoteMonitor> found;
                try { found = source.Monitors() ?? new List<RemoteMonitor>(); }
                catch (Exception) { found = new List<RemoteMonitor>(); }
                watch.Monitors = found;
            }
            RemoteMonitor target = Target(watch, pick, now);
            if (target == null)
            {
                if (watch.Shown != null || watch.Tell)
                {
                    watch.Shown = null;
                    watch.Tell = false;
                    link.SendLive(watch.Device, new[] { StateMessage(false, "no-screen") }, null);
                }
                return;
            }
            bool moved = watch.Shown == null || watch.Shown.Id != target.Id || watch.Shown.Bounds != target.Bounds;
            if (moved || watch.Capture == null)
            {
                if (watch.Capture == null) watch.Capture = source.Capture();
                if (!watch.Capture.Open(target)) return;
                watch.Shown = target;
                watch.LastX = watch.LastY = int.MinValue;
                watch.Tell = true;
                RaiseChanged();
            }
            string list = MonitorsMessage(watch.Monitors, watch.Shown, pick == "auto", watch.Missing);
            if (watch.Tell || list != watch.ListSaid)
            {
                if (link.SendLive(watch.Device, new[] { list }, null)) watch.ListSaid = list;
                watch.Tell = false;
            }
            if (Interlocked.CompareExchange(ref watch.InFlight, 0, 0) > 0) return;
            int cx, cy;
            Cursor(watch.Shown.Bounds, out cx, out cy);
            lock (gate)
            {
                if (watch.N - watch.Acked >= Ahead)
                {
                    // Stalled: the phone stopped answering, so nothing more goes until it acks or asks again.
                    if (now - watch.AckAt > StallMs) return;
                    // An ack may have been lost: after a few seconds the next frame goes anyway.
                    if (now - watch.SentAt < AckWaitMs) return;
                    watch.Acked = watch.N - 1;
                }
            }
            int width = FrameWidth(watch.Shown.Bounds, viewWide, viewTall, quality);
            Bitmap frame = watch.Capture.Next(width);
            if (frame == null)
            {
                lock (gate) stats.Unchanged++;
                // The screen stayed the same, but the mouse may have moved over it.
                if (Math.Abs(cx - watch.LastX) + Math.Abs(cy - watch.LastY) >= 20 && (cx >= 0 || watch.LastX >= 0))
                {
                    string moving = CursorMessage(cx, cy);
                    if (link.SendLive(watch.Device, new[] { moving }, null))
                    {
                        watch.LastX = cx;
                        watch.LastY = cy;
                        lock (gate)
                        {
                            stats.Cursors++;
                            stats.Wire += Envelope(moving.Length);
                        }
                    }
                }
                return;
            }
            byte[] jpeg;
            int wide, tall;
            using (frame)
            {
                wide = frame.Width;
                tall = frame.Height;
                jpeg = Jpeg(frame, quality.Jpeg);
            }
            int n;
            lock (gate) n = watch.N + 1;
            List<string> parts = FrameMessages(n, wide, tall, watch.Shown.Id, cx, cy, jpeg);
            Interlocked.Increment(ref watch.InFlight);
            if (!link.SendLive(watch.Device, parts, delegate { Interlocked.Decrement(ref watch.InFlight); wake.Set(); }))
            {
                Interlocked.Decrement(ref watch.InFlight);
                return;
            }
            watch.LastX = cx;
            watch.LastY = cy;
            lock (gate)
            {
                watch.N = n;
                watch.SentAt = now;
                stats.Frames++;
                stats.Parts += parts.Count;
                stats.Jpeg += jpeg.Length;
                foreach (string part in parts) stats.Wire += Envelope(part.Length);
            }
        }

        // The monitor to show: the phone's pick while it's there, otherwise Auto's. Auto moves to a new monitor once she has
        // looked at it for a second, so a quick glance elsewhere doesn't flick the phone back and forth.
        private RemoteMonitor Target(Watch watch, string pick, long now)
        {
            List<RemoteMonitor> monitors = watch.Monitors;
            if (monitors.Count == 0) return null;
            watch.Missing = false;
            if (pick != "auto")
            {
                RemoteMonitor picked = Find(monitors, pick);
                if (picked != null) return picked;
                watch.Missing = true;
            }
            string looking;
            try { looking = source.Looking(); }
            catch (Exception) { looking = null; }
            RemoteMonitor wanted = Find(monitors, looking) ?? MainMonitor(monitors);
            if (watch.Shown == null || Find(monitors, watch.Shown.Id) == null) return wanted;
            if (wanted.Id == watch.Shown.Id)
            {
                watch.Candidate = null;
                return Find(monitors, watch.Shown.Id);
            }
            if (watch.Candidate == null || watch.Candidate.Id != wanted.Id)
            {
                watch.Candidate = wanted;
                watch.CandidateAt = now;
            }
            return now - watch.CandidateAt >= SwitchMs ? wanted : Find(monitors, watch.Shown.Id);
        }

        private static RemoteMonitor Find(List<RemoteMonitor> monitors, string id)
        {
            if (id == null) return null;
            foreach (RemoteMonitor monitor in monitors) if (monitor.Id == id) return monitor;
            return null;
        }

        private static RemoteMonitor MainMonitor(List<RemoteMonitor> monitors)
        {
            foreach (RemoteMonitor monitor in monitors) if (monitor.Primary) return monitor;
            return monitors[0];
        }

        // The mouse over the shown monitor in ten-thousandths of its width and height, or -1 when it's on another one.
        private void Cursor(Rectangle bounds, out int cx, out int cy)
        {
            Point mouse;
            try { mouse = source.Mouse(); }
            catch (Exception) { mouse = new Point(int.MinValue, int.MinValue); }
            if (bounds.Width <= 0 || bounds.Height <= 0 || !bounds.Contains(mouse))
            {
                cx = cy = -1;
                return;
            }
            cx = (int)((long)(mouse.X - bounds.Left) * 10000 / bounds.Width);
            cy = (int)((long)(mouse.Y - bounds.Top) * 10000 / bounds.Height);
        }

        // ---------- Sizes and messages ----------

        // As wide as the phone shows it, never wider than the quality allows or the monitor is, and at least 160 pixels.
        internal static int FrameWidth(Rectangle monitor, int viewWide, int viewTall, RemoteQuality quality)
        {
            double fit = Math.Min(1.0, Math.Min((double)viewWide / monitor.Width, (double)viewTall / monitor.Height));
            int wide = (int)Math.Round(monitor.Width * fit);
            return Math.Max(Math.Min(160, monitor.Width), Math.Min(Math.Min(quality.Width, monitor.Width), wide));
        }

        internal static byte[] Jpeg(Bitmap frame, int quality)
        {
            if (jpegCodec == null)
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.FormatID == ImageFormat.Jpeg.Guid) jpegCodec = codec;
            using (MemoryStream stream = new MemoryStream())
            using (EncoderParameters settings = new EncoderParameters(1))
            {
                settings.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                frame.Save(stream, jpegCodec, settings);
                return stream.ToArray();
            }
        }

        // The bytes the relay carries to the phone for a sealed message of this many ASCII characters: the envelope's 8-byte
        // header, 16-byte IV, padded ciphertext and 32-byte tag (PROTOCOL.md 6.3).
        internal static long Envelope(int jsonBytes) { return 56 + 16 * (jsonBytes / 16 + 1); }

        // One frame as watch.frame parts of at most PartBytes JPEG bytes each. The first part also says the frame's size,
        // monitor, whole length and where the mouse is.
        internal static List<string> FrameMessages(int n, int wide, int tall, string monitor, int cx, int cy, byte[] jpeg)
        {
            int count = Math.Max(1, (jpeg.Length + PartBytes - 1) / PartBytes);
            List<string> parts = new List<string>(count);
            for (int part = 0; part < count; part++)
            {
                int at = part * PartBytes, take = Math.Min(PartBytes, jpeg.Length - at);
                byte[] piece = new byte[take];
                Buffer.BlockCopy(jpeg, at, piece, 0, take);
                RemoteJson.Writer message = new RemoteJson.Writer().Text("type", "watch.frame").Number("n", n).Number("part", part).Number("parts", count);
                if (part == 0) message.Number("w", wide).Number("h", tall).Text("monitor", monitor).Number("bytes", jpeg.Length).Number("cx", cx).Number("cy", cy);
                parts.Add(message.Text("data", RemoteCrypto.B64u(piece)).ToString());
            }
            return parts;
        }

        internal static string CursorMessage(int cx, int cy) { return new RemoteJson.Writer().Text("type", "watch.cursor").Number("cx", cx).Number("cy", cy).ToString(); }

        internal static string StateMessage(bool on, string reason)
        {
            RemoteJson.Writer message = new RemoteJson.Writer().Text("type", "watch.state").Flag("on", on);
            if (reason != null) message.Text("reason", reason);
            return message.ToString();
        }

        internal static string MonitorsMessage(List<RemoteMonitor> monitors, RemoteMonitor shown, bool auto, bool missing)
        {
            List<RemoteJson.Writer> list = new List<RemoteJson.Writer>();
            foreach (RemoteMonitor monitor in monitors)
                list.Add(new RemoteJson.Writer().Text("id", monitor.Id).Text("name", monitor.Name).Number("w", monitor.Bounds.Width).Number("h", monitor.Bounds.Height).Flag("primary", monitor.Primary));
            RemoteJson.Writer message = new RemoteJson.Writer().Text("type", "watch.monitors").Objects("list", list).Text("monitor", shown == null ? "" : shown.Id).Flag("auto", auto);
            if (missing) message.Flag("missing", true);
            return message.ToString();
        }

        // Monitors from Windows' list: each numbered from its device name (\\.\DISPLAY2 is "2"), or by its place in the list.
        internal static List<RemoteMonitor> Numbered(IList<KeyValuePair<string, Rectangle>> screens, string primary)
        {
            List<RemoteMonitor> found = new List<RemoteMonitor>();
            HashSet<string> taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, Rectangle> screen in screens)
            {
                string device = screen.Key ?? "";
                int at = device.Length;
                while (at > 0 && char.IsDigit(device[at - 1])) at--;
                int number;
                if (at == device.Length || !int.TryParse(device.Substring(at), NumberStyles.None, CultureInfo.InvariantCulture, out number) || number <= 0 || number > 99 || taken.Contains(number.ToString(CultureInfo.InvariantCulture)))
                    number = found.Count + 1;
                while (taken.Contains(number.ToString(CultureInfo.InvariantCulture))) number++;
                string id = number.ToString(CultureInfo.InvariantCulture);
                taken.Add(id);
                found.Add(new RemoteMonitor { Id = id, Name = "Monitor " + id, Device = device, Bounds = screen.Value, Primary = string.Equals(device, primary, StringComparison.OrdinalIgnoreCase) });
            }
            found.Sort(delegate (RemoteMonitor a, RemoteMonitor b) { return int.Parse(a.Id, CultureInfo.InvariantCulture).CompareTo(int.Parse(b.Id, CultureInfo.InvariantCulture)); });
            return found;
        }

        private void RaiseChanged()
        {
            Action changed = Changed;
            if (changed == null) return;
            try { changed(); }
            catch (Exception) { }
        }

        private long Now() { return clock.ElapsedMilliseconds; }
    }
}
