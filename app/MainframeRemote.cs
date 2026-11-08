using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What the REMOTE sheet shows, read from her link (Companion.RemoteView), or made up by the smoke test.
    internal sealed class RemoteView
    {
        internal bool On, Ready, Expired;
        internal string Status = "off", Host = RemoteLink.DefaultHost, Qr, Note = "";
        internal int QrSeconds;
        internal RemoteAsk Ask;
        internal List<RemoteDevice> Phones = new List<RemoteDevice>();
        internal HashSet<string> Online = new HashSet<string>(StringComparer.Ordinal);
    }

    // Capri Remote in the Mainframe: a Remote key on CAPRI's band, cyan while Remote is on and solid cyan while a phone is
    // connected, and the REMOTE sheet it opens: Remote on or off, a QR to pair a phone and the code to check when one asks,
    // the paired phones with Revoke, and the relay's host.
    internal sealed partial class Mainframe
    {
        private Key remoteKey;
        private RemoteSheet remoteSheet;

        // Called once from the main page's layout, beside her version on CAPRI's band.
        private void MakeRemoteKey(int y)
        {
            remoteKey = Lower(Make("Remote", 100, y, 88, 18, delegate { OpenRemote(); }));
            remoteKey.AccessibleName = "Capri Remote: chat with her from your phone";
        }

        internal void RemoteChanged()
        {
            RemoteView view = capri.RemoteView();
            ShowRemote(view);
            if (remoteSheet != null && !remoteSheet.IsDisposed) remoteSheet.Fill(view);
        }

        private void ShowRemote(RemoteView view)
        {
            if (remoteKey == null) return;
            remoteKey.Accent = view.On ? Cyan : Rose;
            remoteKey.Pick(view.On && view.Online.Count > 0);
            remoteKey.AccessibleDescription = RemoteKeyWords(view);
        }

        private static string RemoteKeyWords(RemoteView view)
        {
            if (!view.On) return "Remote off";
            return view.Online.Count == 0 ? "Remote on" : "Remote on, " + view.Online.Count + (view.Online.Count == 1 ? " phone" : " phones");
        }

        private void OpenRemote()
        {
            float fit = SheetZoom(RemoteSheet.Wide, RemoteSheet.Tall);
            if (remoteSheet != null && !remoteSheet.IsDisposed && remoteSheet.Zoom != fit) remoteSheet.Close();
            if (remoteSheet == null || remoteSheet.IsDisposed) remoteSheet = new RemoteSheet(this, fit, true);
            remoteSheet.Fill(capri.RemoteView());
            if (!remoteSheet.Visible) remoteSheet.Show(this);
            remoteSheet.Activate();
        }

        // The smoke test's look at the sheet: drawn at a zoom with a made-up view, checked for crowding, saved as a picture.
        internal string RemoteSheetAt(float zoom, RemoteView view, string picture)
        {
            using (RemoteSheet sheet = new RemoteSheet(this, zoom, false))
            {
                sheet.Fill(view);
                sheet.Show(this);
                string crowded = sheet.Crowded();
                if (crowded == null) crowded = sheet.Matches(view);
                if (picture != null) sheet.Picture(picture);
                sheet.Close();
                return crowded;
            }
        }

        // The smoke test's look at CAPRI's band: nothing else on the main page touches the Remote key, and each control on its
        // row is drawn where it sits, over the band, into a picture. The page needn't be laid out for it.
        internal string RemoteBand(string path)
        {
            Rectangle strip = new Rectangle(0, remoteKey.Top - 30, Wide, 66);
            List<Control> row = new List<Control>();
            foreach (Control control in Controls)
            {
                if (!control.Visible || !control.Bounds.IntersectsWith(strip)) continue;
                if (control != remoteKey && control.Bounds.IntersectsWith(remoteKey.Bounds)) return "the Remote key overlaps " + (control.Text.Length > 0 ? control.Text : control.AccessibleName);
                row.Add(control);
            }
            if (!row.Contains(support)) return "the Remote key isn't on Support's row";
            using (Bitmap picture = new Bitmap(strip.Width, strip.Height))
            {
                using (Graphics graphics = Graphics.FromImage(picture))
                {
                    graphics.Clear(Ground);
                    foreach (Control control in row)
                    {
                        Head head = control as Head;
                        if (head == null || head.Span <= 0) continue;
                        RectangleF band = head.Band(1f);
                        band.Offset(-strip.Left, -strip.Top);
                        DrawBand(graphics, band, 1f);
                    }
                }
                foreach (Control control in row)
                    using (Bitmap one = new Bitmap(control.Width, control.Height))
                    {
                        control.DrawToBitmap(one, new Rectangle(Point.Empty, control.Size));
                        using (Graphics graphics = Graphics.FromImage(picture)) graphics.DrawImageUnscaled(one, control.Left - strip.Left, control.Top - strip.Top);
                    }
                picture.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            return null;
        }

        internal string RemoteKeyFor(RemoteView view)
        {
            ShowRemote(view);
            bool picked = remoteKey.Picked;
            string words = remoteKey.AccessibleDescription;
            return picked == (view.On && view.Online.Count > 0) ? words : "a key " + (picked ? "picked" : "not picked") + " for " + words;
        }

        private sealed class RemoteSheet : ModSheet
        {
            internal const int Wide = 600, Tall = 548, Rows = 5;
            private readonly bool live;
            private readonly Toggle power;
            private readonly Label status, idle, qrNote, askTitle, askName, codeLabel, askCode, askCheck, empty, more, note;
            private readonly Label[] names = new Label[Rows], states = new Label[Rows];
            private readonly Key[] revokes = new Key[Rows];
            private readonly RemoteQrBox qr;
            private readonly Key pair, allow, deny, saveHost;
            private readonly Entry host;
            private readonly Timer clock = new Timer { Interval = 1000 };
            private RemoteView view = new RemoteView();
            private DateTime expires;
            private int armed = -1, armedAt;
            private bool syncing;

            // live: the real sheet, which talks to her link. The smoke test's copies only draw.
            internal RemoteSheet(Mainframe owner, float zoom, bool live) : base(owner, zoom, "Capri Remote", "REMOTE >", Wide, Tall)
            {
                this.live = live;
                Words("Chat with Capri from your phone", 28, 52, 544, 16, Small(), Faint);
                power = new Toggle();
                power.Text = "Remote";
                power.Hint = "Sealed end to end";
                power.HintFont = Small();
                power.Font = Type(9.5f * zoom);
                power.Switch = 120;
                power.Bounds = Box(28, 78, 544, 24);
                power.AccessibleName = "Capri Remote";
                power.CheckedChanged += delegate { if (!syncing && this.live) Frame.capri.SetRemote(power.Checked); };
                Controls.Add(power);
                status = Words("", 28, 110, 544, 18, Type(9.5f * zoom, true), Faint);
                Font band = Type(10f * zoom, true);
                Heading(this, "QR CODE >", new Point(Z(28), Z(146)), band, White, 286);
                Heading(this, "PHONES >", new Point(Z(316), Z(146)), band, White, 270);
                Heading(this, "RELAY >", new Point(Z(28), Z(440)), band, White, 558);
                pair = Make("Pair a phone", 186, 145, 106, 18, delegate { if (this.live) Frame.capri.RemoteNewCode(); });
                pair.Accent = Cyan;
                pair.Loud = true;
                qr = new RemoteQrBox();
                qr.Bounds = Box(42, 176, 236, 236);
                qr.AccessibleName = "Pairing QR code";
                Controls.Add(qr);
                qrNote = Words("", 28, 416, 264, 16, Small(), Faint);
                qrNote.TextAlign = ContentAlignment.MiddleCenter;
                idle = Words("", 28, 180, 264, 18, Font, Faint);
                askTitle = Words("ALLOW THIS PHONE?", 28, 180, 264, 20, Type(10.5f * zoom, true), Rose);
                askName = Words("", 28, 204, 264, 22, Type(11f * zoom, true), White);
                codeLabel = Words("CODE", 28, 240, 264, 14, Small(), Faint);
                askCode = Words("", 28, 256, 264, 46, Display(26f * zoom), Cyan);
                askCheck = Words("Same code on the phone?", 28, 306, 264, 18, Font, Faint);
                allow = Make("Allow", 28, 338, 128, 28, delegate { if (this.live) Frame.capri.RemoteAllow(); });
                allow.Accent = Cyan;
                allow.Loud = true;
                deny = Make("Deny", 164, 338, 128, 28, delegate { if (this.live) Frame.capri.RemoteDeny(); });
                deny.Accent = Hot;
                for (int i = 0; i < Rows; i++)
                {
                    int row = i, y = 178 + 44 * i;
                    names[i] = Words("", 316, y, 176, 20, Type(10f * zoom, true), White);
                    states[i] = Words("", 316, y + 20, 176, 16, Small(), Faint);
                    revokes[i] = Make("Revoke", 500, y + 5, 72, 26, delegate { Revoke(row); });
                    revokes[i].Accent = Hot;
                }
                empty = Words("No phones yet", 316, 180, 256, 18, Font, Faint);
                more = Words("", 316, 400, 256, 16, Small(), Faint);
                host = new Entry();
                host.Bounds = Box(28, 470, 460, 26);
                host.Cue = RemoteLink.DefaultHost;
                host.Box.AccessibleName = "Relay host";
                host.Box.KeyDown += delegate (object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveHost(); } };
                Controls.Add(host);
                saveHost = Make("Save", 496, 470, 76, 26, delegate { SaveHost(); });
                saveHost.AccessibleName = "Save relay host";
                note = Words("", 28, 504, 544, 18, Font, Cyan);
                clock.Tick += delegate { Tick(); };
                if (live) clock.Start();
                Frame.Fit(this);
            }

            internal void Fill(RemoteView shown)
            {
                view = shown ?? new RemoteView();
                syncing = true;
                power.Checked = view.On;
                syncing = false;
                Color tone;
                status.Text = StatusLine(view, out tone);
                status.ForeColor = tone;
                bool asking = view.Ask != null, showing = !asking && view.Qr != null;
                qr.Url = showing ? view.Qr : null;
                qr.Visible = showing;
                expires = showing ? DateTime.Now.AddSeconds(view.QrSeconds) : DateTime.MaxValue;
                qrNote.Visible = showing;
                qrNote.Text = showing ? "SCAN WITH YOUR PHONE · EXPIRES " + expires.ToString("HH:mm", CultureInfo.CurrentCulture) : "";
                idle.Visible = !asking && !showing;
                idle.Text = !view.On ? "Turn Remote on first" : !view.Ready ? "Waiting for the relay" : view.Expired ? "That code expired" : "No code open";
                askTitle.Visible = askName.Visible = codeLabel.Visible = askCode.Visible = askCheck.Visible = allow.Visible = deny.Visible = asking;
                askName.Text = asking ? view.Ask.Name : "";
                askCode.Text = asking ? view.Ask.Code : "";
                pair.Text = showing || view.Expired ? "New code" : "Pair a phone";
                pair.Enabled = view.On && view.Ready && !asking;
                for (int i = 0; i < Rows; i++)
                {
                    bool on = i < view.Phones.Count;
                    names[i].Visible = states[i].Visible = revokes[i].Visible = on;
                    if (!on) continue;
                    RemoteDevice phone = view.Phones[i];
                    bool online = view.Online.Contains(phone.Id);
                    names[i].Text = phone.Name;
                    states[i].Text = online ? "CONNECTED" : "ADDED " + phone.Added.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture).ToUpperInvariant();
                    states[i].ForeColor = online ? Cyan : Faint;
                    revokes[i].Text = armed == i ? "Sure?" : "Revoke";
                    revokes[i].Loud = armed == i;
                    revokes[i].AccessibleName = "Revoke " + phone.Name;
                    revokes[i].Invalidate();
                }
                empty.Visible = view.Phones.Count == 0;
                more.Visible = view.Phones.Count > Rows;
                more.Text = view.Phones.Count > Rows ? "+" + (view.Phones.Count - Rows) + " MORE" : "";
                if (!host.Box.Focused) host.Box.Text = view.Host == RemoteLink.DefaultHost ? "" : view.Host;
                note.Text = view.Note ?? "";
                note.ForeColor = Cyan;
                Invalidate(true);
            }

            private static string StatusLine(RemoteView view, out Color tone)
            {
                tone = Faint;
                switch (view.Status)
                {
                    case "online":
                        tone = Cyan;
                        int count = view.Online.Count;
                        return "ONLINE · " + (count == 0 ? "NO PHONES CONNECTED" : count + (count == 1 ? " PHONE CONNECTED" : " PHONES CONNECTED"));
                    case "connecting": return "CONNECTING";
                    case "retrying":
                        tone = Rose;
                        return "CAN'T REACH THE RELAY · RETRYING";
                    case "replaced":
                        tone = Rose;
                        return "ANOTHER CAPRI TOOK OVER · TURN OFF AND ON";
                    case "bad-host":
                        tone = Rose;
                        return "BAD RELAY HOST";
                    default: return "OFF";
                }
            }

            // A second press within 4 seconds revokes, so one stray click never unpairs a phone.
            private void Revoke(int row)
            {
                if (row >= view.Phones.Count) return;
                if (armed != row || unchecked(Environment.TickCount - armedAt) > 4000)
                {
                    armed = row;
                    armedAt = Environment.TickCount;
                    Fill(view);
                    return;
                }
                armed = -1;
                if (live) Frame.capri.RemoteRevoke(view.Phones[row].Id);
            }

            private void SaveHost()
            {
                if (!live) return;
                if (Frame.capri.SetRemoteHost(host.Box.Text)) return;
                note.Text = "Not a relay host";
                note.ForeColor = Rose;
            }

            private void Tick()
            {
                if (armed >= 0 && unchecked(Environment.TickCount - armedAt) > 4000)
                {
                    armed = -1;
                    Fill(view);
                }
                if (view.Qr != null && DateTime.Now >= expires) Fill(Frame.capri.RemoteView());
            }

            // The smoke test's check that each state shows what it should.
            internal string Matches(RemoteView expected)
            {
                if (expected.Ask != null && (!allow.Visible || askCode.Text != expected.Ask.Code || qr.Visible)) return "the Allow prompt didn't show its code";
                if (expected.Ask == null && expected.Qr != null && (!qr.Visible || allow.Visible)) return "the QR didn't show";
                if (expected.Phones.Count > 0 && (!revokes[0].Visible || names[0].Text != expected.Phones[0].Name)) return "the paired phones didn't show";
                if (expected.Phones.Count == 0 && !empty.Visible) return "an empty list didn't say so";
                if (power.Checked != expected.On) return "the switch didn't match";
                return null;
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                clock.Stop();
                if (live) Frame.capri.RemoteCloseCode();
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) clock.Dispose();
                base.Dispose(disposing);
            }
        }

        // The pairing QR: dark modules on a light plate with the sheet's clipped corner, a four-module quiet zone inside it,
        // and whole pixels per module so a phone's camera reads it cleanly. Its colours never follow her Mainframe colours.
        private sealed class RemoteQrBox : Control
        {
            private static readonly Color Ink = Color.FromArgb(10, 10, 16), Paper = Color.FromArgb(242, 242, 246);
            private RemoteQr code;
            private string url;

            internal RemoteQrBox()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Ground;
                AccessibleRole = AccessibleRole.Graphic;
                TabStop = false;
            }

            internal string Url
            {
                set
                {
                    if (value == url) return;
                    url = value;
                    code = value == null ? null : RemoteQr.Encode(value);
                    Invalidate();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath plate = Cut(new RectangleF(0, 0, Width, Height), 0, 0, 12 * s, 0))
                using (Brush paper = new SolidBrush(Paper))
                    graphics.FillPath(paper, plate);
                if (code == null) return;
                int modules = code.Size + 8;
                int unit = Math.Max(1, Math.Min(Width, Height) / modules);
                int side = unit * modules, left = (Width - side) / 2 + 4 * unit, top = (Height - side) / 2 + 4 * unit;
                graphics.SmoothingMode = SmoothingMode.None;
                using (Brush ink = new SolidBrush(Ink))
                    for (int y = 0; y < code.Size; y++)
                        for (int x = 0; x < code.Size; x++)
                            if (code.Dark(x, y)) graphics.FillRectangle(ink, left + x * unit, top + y * unit, unit, unit);
            }
        }
    }
}
