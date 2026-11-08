using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Capri Remote's screen view and call in Capri herself (app/RemoteScreen.cs, app/RemoteCall.cs): her monitors and their
    // captures (FrameGrab), the monitor she's looking at for Auto, her ears and voice for a call, and a badge on the PC the
    // whole time a phone watches or calls. RemoteConnect starts it with her link and RemoteDisconnect ends it.
    internal sealed partial class Companion
    {
        private volatile RemoteScreen remoteScreen;
        private volatile RemoteCall remoteCall;
        // The monitor she's looking at, worked out on her thread every half second while a phone watches.
        private volatile string remoteLooking;
        private RemoteBadge remoteBadge;
        private System.Windows.Forms.Timer remoteLookTimer;

        // On her thread, with the link RemoteConnect just made.
        private void RemoteLiveStart(RemoteLink link)
        {
            RemoteLiveStop();
            RemoteScreen screen = new RemoteScreen(link, new RemoteDesktop(this));
            RemoteCall call = new RemoteCall(link, RemoteEars, RemoteHeard);
            call.VoiceOn = delegate { return !preferences.Silent; };
            call.EarsOn = delegate { LocalEars own = Hearing.Own; return own != null && own.Ready || Hearing.Available; };
            screen.Changed += RemoteLiveChanged;
            call.Changed += RemoteLiveChanged;
            remoteScreen = screen;
            remoteCall = call;
            Speaker.Tap = RemoteVoiceTap;
            Speaker.Hushed = RemoteVoiceHushed;
        }

        private void RemoteLiveStop()
        {
            RemoteScreen screen = remoteScreen;
            RemoteCall call = remoteCall;
            remoteScreen = null;
            remoteCall = null;
            Speaker.Tap = null;
            Speaker.Hushed = null;
            if (screen != null) screen.Dispose();
            if (call != null) call.Dispose();
            RemoteBadgeShow();
        }

        // Her own ears on a phone's line: Parakeet when it's in, Windows' recognizer otherwise.
        private static string RemoteEars(short[] samples) { return Hearing.Text(samples); }

        private void RemoteVoiceTap(short[] samples, int rate)
        {
            RemoteCall call = remoteCall;
            // Her voice's own thread: nothing here may stop her talking.
            try { if (call != null && call.Any) call.Voice(samples, rate); }
            catch (Exception) { }
        }

        private void RemoteVoiceHushed()
        {
            RemoteCall call = remoteCall;
            try { if (call != null) call.Hush(); }
            catch (Exception) { }
        }

        private void RemoteLiveChanged() { Post(RemoteBadgeShow); }

        // ---------- Auto: the monitor she's looking at ----------

        // Where her hands are while she works the computer; otherwise the monitor her glances watch, when Watch my screen has
        // one picked; otherwise the one with their window in front, which is where she looks when asked (LookAt).
        private void RemoteLook()
        {
            try
            {
                Rectangle area;
                WatchScreen picked = preferences.Watch ? WatchedScreen() : null;
                if (herPointer.Visible) area = new Rectangle(herPointer.Tip, new Size(1, 1));
                else if (picked != null) area = picked.Bounds;
                else area = CaptureArea(Native.GetForegroundWindow());
                Point middle = new Point(area.X + area.Width / 2, area.Y + area.Height / 2);
                string found = null;
                foreach (RemoteMonitor monitor in RemoteDesktop.List()) if (monitor.Bounds.Contains(middle)) found = monitor.Id;
                remoteLooking = found;
            }
            catch (Exception) { }
        }

        private sealed class RemoteDesktop : RemoteScreenSource
        {
            private readonly Companion her;

            internal RemoteDesktop(Companion her) { this.her = her; }

            internal static List<RemoteMonitor> List()
            {
                List<KeyValuePair<string, Rectangle>> screens = new List<KeyValuePair<string, Rectangle>>();
                foreach (Screen screen in Screen.AllScreens) screens.Add(new KeyValuePair<string, Rectangle>(screen.DeviceName, screen.Bounds));
                Screen main = Screen.PrimaryScreen;
                return RemoteScreen.Numbered(screens, main == null ? null : main.DeviceName);
            }

            internal override List<RemoteMonitor> Monitors() { return List(); }

            internal override string Looking() { return her.remoteLooking; }

            internal override Point Mouse() { return Cursor.Position; }

            internal override RemoteCapture Capture() { return new RemoteGrab(); }
        }

        // One monitor through FrameGrab: Desktop Duplication, which shows her and every other window and says when nothing
        // changed, or a plain screen copy where that isn't there. Frames that come out the same as the last are dropped.
        internal sealed class RemoteGrab : RemoteCapture
        {
            private FrameGrab grab;
            private int lastWidth;
            private ulong lastPrint;
            private bool any;

            internal override bool Open(RemoteMonitor monitor)
            {
                try
                {
                    if (grab == null) grab = new FrameGrab();
                    grab.Open(monitor.Bounds);
                }
                catch (Exception) { return false; }
                any = false;
                lastWidth = 0;
                return true;
            }

            internal string Live { get { return grab == null ? "Closed" : grab.Live; } }

            internal override Bitmap Next(int maxWidth)
            {
                if (grab == null) return null;
                bool fresh;
                try { fresh = grab.Next(0); }
                catch (Exception) { return null; }
                if (!fresh && any && maxWidth == lastWidth) return null;
                Bitmap frame;
                try { frame = grab.Copy(maxWidth); }
                catch (Exception) { return null; }
                if (frame == null) return null;
                ulong print = Print(frame);
                if (any && maxWidth == lastWidth && print == lastPrint)
                {
                    frame.Dispose();
                    return null;
                }
                any = true;
                lastWidth = maxWidth;
                lastPrint = print;
                return frame;
            }

            // FNV-1a over every pixel, to tell a frame that changed from one that didn't.
            private static unsafe ulong Print(Bitmap frame)
            {
                BitmapData data = frame.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
                try
                {
                    ulong hash = 14695981039346656037UL;
                    for (int row = 0; row < data.Height; row++)
                    {
                        uint* pixel = (uint*)((byte*)data.Scan0 + (long)row * data.Stride);
                        for (int column = 0; column < data.Width; column++) hash = (hash ^ (pixel[column] & 0xFFFFFFu)) * 1099511628211UL;
                    }
                    return hash;
                }
                finally { frame.UnlockBits(data); }
            }

            public override void Dispose()
            {
                FrameGrab was = grab;
                grab = null;
                if (was != null) was.Dispose();
            }
        }

        // ---------- The badge ----------

        // On her thread: shown on the monitor a phone watches (or the main one for a call alone) while any phone watches or
        // calls, gone otherwise. Auto's look runs only while it's up.
        private void RemoteBadgeShow()
        {
            RemoteScreen screen = remoteScreen;
            RemoteCall call = remoteCall;
            List<RemoteWatcher> watching = screen == null ? new List<RemoteWatcher>() : screen.Watchers();
            List<RemoteWatcher> calling = call == null ? new List<RemoteWatcher>() : call.Callers();
            if (watching.Count > 0)
            {
                if (remoteLookTimer == null)
                {
                    remoteLookTimer = new System.Windows.Forms.Timer { Interval = 500 };
                    remoteLookTimer.Tick += delegate { RemoteLook(); };
                }
                if (!remoteLookTimer.Enabled)
                {
                    RemoteLook();
                    remoteLookTimer.Start();
                }
            }
            else if (remoteLookTimer != null) remoteLookTimer.Stop();
            if (watching.Count == 0 && calling.Count == 0 || IsDisposed)
            {
                if (remoteBadge != null && !remoteBadge.IsDisposed) remoteBadge.Hide();
                return;
            }
            HashSet<string> phones = new HashSet<string>(StringComparer.Ordinal);
            string name = null;
            foreach (RemoteWatcher one in watching) { phones.Add(one.Device); if (name == null) name = one.Name; }
            foreach (RemoteWatcher one in calling) { phones.Add(one.Device); if (name == null) name = one.Name; }
            RemoteMonitor monitor = null;
            foreach (RemoteWatcher one in watching) if (monitor == null && one.Monitor != null) monitor = one.Monitor;
            Rectangle area = monitor != null ? Screen.FromRectangle(monitor.Bounds).WorkingArea : Screen.PrimaryScreen.WorkingArea;
            string lead = watching.Count > 0 && calling.Count > 0 ? "PHONE WATCHING AND ON A CALL " : watching.Count > 0 ? "PHONE IS WATCHING " : "PHONE IS ON A CALL ";
            string who = phones.Count > 1 ? phones.Count + " PHONES" : (name ?? "PHONE").ToUpperInvariant();
            if (remoteBadge == null || remoteBadge.IsDisposed) remoteBadge = new RemoteBadge();
            remoteBadge.Put(lead, who, area);
        }

        // ---------- Smoke ----------

        // The live parts that run without a relay: the badge drawn at 100%, 150% and 200% to remote-watch-*.png, her monitors
        // numbered, Auto's look naming one of them, and one frame from a monitor's capture where this desktop gives one.
        private void SmokeRemoteLive(List<string> checks, string output)
        {
            foreach (float zoom in new[] { 1f, 1.5f, 2f })
                using (Bitmap badge = RemoteBadge.Draw("PHONE IS WATCHING ", "LACHY'S PIXEL 9 PRO XL", zoom))
                {
                    if (badge.Width < 200 * zoom || badge.Height < 20 * zoom || badge.Width > 700 * zoom) throw new Exception("The watching badge came out " + badge.Width + " x " + badge.Height + " at " + (int)(zoom * 100) + "%.");
                    badge.Save(Path.Combine(output, "remote-watch-" + (int)(zoom * 100) + ".png"), ImageFormat.Png);
                }
            List<RemoteMonitor> monitors = RemoteDesktop.List();
            if (monitors.Count == 0) throw new Exception("Capri Remote found no monitors to show.");
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RemoteMonitor monitor in monitors) if (!ids.Add(monitor.Id) || monitor.Name != "Monitor " + monitor.Id) throw new Exception("Monitor " + monitor.Id + " is named twice or wrongly.");
            remoteLooking = null;
            RemoteLook();
            if (remoteLooking == null || !ids.Contains(remoteLooking)) throw new Exception("Auto looked at no monitor (" + (remoteLooking ?? "none") + ").");
            string frame;
            using (RemoteGrab grab = new RemoteGrab())
            {
                if (!grab.Open(monitors[0])) frame = "LIMIT a phone's capture couldn't open " + monitors[0].Name + " on this desktop";
                else
                {
                    Bitmap shot = null;
                    for (int tries = 0; tries < 10 && shot == null; tries++)
                    {
                        shot = grab.Next(640);
                        if (shot == null) System.Threading.Thread.Sleep(50);
                    }
                    if (shot == null) frame = "LIMIT a phone's capture of " + monitors[0].Name + " gave no frame on this desktop (" + grab.Live + ")";
                    else
                    {
                        using (shot)
                        {
                            if (shot.Width > 640 || shot.Width < Math.Min(160, monitors[0].Bounds.Width)) throw new Exception("A phone's frame came out " + shot.Width + " wide.");
                            byte[] jpeg = RemoteScreen.Jpeg(shot, RemoteQuality.Balanced.Jpeg);
                            if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) throw new Exception("A phone's frame isn't a JPEG.");
                            // A still screen gives nothing more to send (a desktop that's animating may, so this is only said).
                            bool still;
                            using (Bitmap again = grab.Next(640)) still = again == null;
                            frame = "PASS a phone's capture of " + monitors[0].Name + " gives a " + shot.Width + " x " + shot.Height + " JPEG of " + jpeg.Length + " bytes (" + grab.Live + ")" + (still ? ", and nothing more while it stays the same" : "");
                        }
                    }
                }
            }
            checks.Add(frame);
            checks.Add("PASS Capri Remote's screen and call: the watching badge fits at 100%, 150% and 200% (remote-watch-*.png), " + monitors.Count + (monitors.Count == 1 ? " monitor is" : " monitors are") + " numbered as Windows counts them, and Auto looks at one of them");
        }
    }

    // The badge on the PC while a phone watches or calls: the CAPRI IS CONTROLLING badge's slab with her face, at the top
    // right of the monitor being watched. Clicks go through it, and screen captures and recordings leave it out.
    internal sealed class RemoteBadge : Overlay
    {
        private string shownLead, shownWho;
        private Rectangle shownArea;

        internal RemoteBadge() : base("Capri Remote") { ClickThrough = true; }

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // WDA_EXCLUDEFROMCAPTURE (Windows 10 2004 on): not in the phone's frames, nor his stream. Older Windows shows it.
            try { SetWindowDisplayAffinity(Handle, 0x11); }
            catch (Exception) { }
        }

        internal void Put(string lead, string who, Rectangle area)
        {
            if (Visible && lead == shownLead && who == shownWho && area == shownArea) return;
            float scale;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, screen.DpiX / 96f);
            Bitmap picture = Draw(lead, who, scale);
            int margin = (int)Math.Round(12 * scale);
            Point at = new Point(area.Right - picture.Width - margin, area.Top + margin);
            shownLead = lead;
            shownWho = who;
            shownArea = area;
            Present(picture, at);
        }

        internal static Bitmap Draw(string lead, string who, float scale)
        {
            string name = RemoteMessages.Clip(who, 32);
            using (Font font = Look.Mono(11 * scale, FontStyle.Bold))
            {
                float tall = 26 * scale, icon = 20 * scale, pad = 7 * scale, wide;
                using (Bitmap measure = new Bitmap(1, 1))
                using (Graphics graphics = Graphics.FromImage(measure))
                    wide = pad + icon + pad + graphics.MeasureString(lead, font, PointF.Empty, Takeover.Spaced).Width + graphics.MeasureString(name, font, PointF.Empty, Takeover.Spaced).Width + pad * 1.4f;
                int side = (int)Math.Ceiling(4 * scale);
                Bitmap picture = new Bitmap((int)Math.Ceiling(wide) + side * 2, (int)Math.Ceiling(tall) + side * 2, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(picture))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    RectangleF slab = new RectangleF(side, side, wide, tall);
                    RectangleF iconAt = new RectangleF(slab.Left + pad, slab.Top + (tall - icon) / 2, icon, icon);
                    Takeover.Badge(graphics, slab, iconAt, lead, name, font, pad, icon, 1, 1, Takeover.Face, false, new Random(1), scale);
                }
                return picture;
            }
        }
    }
}
