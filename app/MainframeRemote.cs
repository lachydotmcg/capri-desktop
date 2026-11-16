using System;
using System.Collections.Generic;
#if !CROSS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Windows.Forms;
#endif

namespace CapriDesktop
{
    // What the REMOTE sheet shows, read from her link (Companion.RemoteView), or made up by the smoke test.
    internal sealed class RemoteView
    {
        internal bool On, Ready, Expired;
        // LinkFor: the paired phone whose pairing link is open (PROTOCOL.md 5.1), which the sheet names but never shows as a QR.
        internal string Status = "off", Host = RemoteLink.DefaultHost, Qr, Note = "", LinkFor;
        internal int QrSeconds;
        internal RemoteAsk Ask;
        internal List<RemoteDevice> Phones = new List<RemoteDevice>();
        internal HashSet<string> Online = new HashSet<string>(StringComparer.Ordinal);
    }

    // Capri Remote in the Mainframe: a Remote key on CAPRI's band, cyan while Remote is on and solid cyan while a phone is
    // connected, and the REMOTE sheet it opens: Remote on or off, a QR to pair a phone and the code to check when one asks,
    // the paired phones with Revoke, and the relay's host.
#if !CROSS
    internal sealed partial class Mainframe
    {
        private Key remoteKey;
        private RemoteSheet remoteSheet;

        // Called once from the main page's layout, beside her version on CAPRI's band.
        private void MakeRemoteKey(int y)
        {
            remoteKey = Lower(Make("Remote", 100, y, 88, 18, delegate { OpenRemote(); }));
            remoteKey.AccessibleName = "Capri Remote: chat with her from your phone";
            remoteKey.Phone = true;
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
            // Its state is its accessible value: its description is its tip, from SelfKnow.
            remoteKey.Status = RemoteKeyWords(view);
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
                if (crowded == null && view.Phones.Count > RemoteSheet.Rows) crowded = sheet.CheckPhones(picture);
                if (crowded == null && Computer.Streaming && view.Qr != null) crowded = sheet.CheckQr();
                sheet.Close();
                return crowded;
            }
        }

        // The smoke test's look at CAPRI's band: nothing else on the main page touches the Remote key, and each control on its
        // row is drawn where it sits, over the band, into a picture. The page needn't be laid out for it. s is the Mainframe's
        // scale when its size isn't 100%.
        internal string RemoteBand(string path, float s = 1f)
        {
            Rectangle strip = new Rectangle(0, remoteKey.Top - (int)(30 * s), (int)Math.Ceiling(Wide * s), (int)(66 * s));
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
                        RectangleF band = head.Band(s);
                        band.Offset(-strip.Left, -strip.Top);
                        DrawBand(graphics, band, s);
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

        // The smoke test's look at Remote's phone at the Mainframe's current size: the key keeps Support's size and row and its
        // own place, the phone is drawn left of "Remote" with room to spare, and CAPRI's band, the band with Remote hovered, and
        // Support beside Remote's looks (at rest, hovered, pressed, on, on and hovered, a phone connected, disabled) are saved.
        // Support's heart is held at rest so pictures from two builds can be compared.
        internal string RemoteMark(string folder, int size, List<string> seen)
        {
            float s = Unit();
            if (remoteKey.Size != support.Size || remoteKey.Top != support.Top) return "the key is at " + remoteKey.Bounds + " beside Support at " + support.Bounds;
            if (Math.Abs(remoteKey.Left - 100 * s) > 1.5f) return "the key moved to x " + remoteKey.Left;
            int wide = TextRenderer.MeasureText(remoteKey.Text, remoteKey.Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
            if (Key.PhoneRoom * s + wide > remoteKey.Width - 8 * s) return "the phone and its label need " + (Key.PhoneRoom * s + wide) + " px of the key's " + remoteKey.Width;
            support.Swell = 1;
            support.Beat = 0;
            int marked = 0, edge = (remoteKey.Width - wide) / 2;
            using (Bitmap with = KeyShot(remoteKey))
            {
                remoteKey.Phone = false;
                Bitmap without;
                try { without = KeyShot(remoteKey); }
                finally { remoteKey.Phone = true; }
                using (without)
                    for (int x = 0; x < edge; x++)
                        for (int y = 0; y < with.Height; y++)
                            if (with.GetPixel(x, y) != without.GetPixel(x, y)) marked++;
            }
            if (marked < 10 * s * s) return "no phone was drawn left of its label (" + marked + " pixels)";
            System.Reflection.FieldInfo hot = typeof(Key).GetField("hot", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.FieldInfo down = typeof(Key).GetField("down", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Color accent = remoteKey.Accent;
            bool picked = remoteKey.Picked;
            try
            {
                string crowded = RemoteBand(System.IO.Path.Combine(folder, "remote-mark-row-" + size + ".png"), s);
                if (crowded != null) return crowded;
                hot.SetValue(remoteKey, true);
                RemoteBand(System.IO.Path.Combine(folder, "remote-mark-row-hover-" + size + ".png"), s);
                hot.SetValue(remoteKey, false);
                int gap = (int)Math.Round(6 * s), looks = 7;
                using (Bitmap strip = new Bitmap(gap + support.Width + looks * (gap + remoteKey.Width) + gap, remoteKey.Height + 2 * gap))
                using (Graphics graphics = Graphics.FromImage(strip))
                {
                    graphics.Clear(Ground);
                    using (Bitmap one = KeyShot(support)) graphics.DrawImageUnscaled(one, gap, gap);
                    for (int look = 0; look < looks; look++)
                    {
                        remoteKey.Accent = look >= 3 ? Cyan : Rose;
                        remoteKey.Picked = look == 5;
                        hot.SetValue(remoteKey, look == 1 || look == 2 || look == 4);
                        down.SetValue(remoteKey, look == 2);
                        remoteKey.Enabled = look != 6;
                        using (Bitmap one = KeyShot(remoteKey)) graphics.DrawImageUnscaled(one, gap + support.Width + gap + look * (remoteKey.Width + gap), gap);
                    }
                    strip.Save(System.IO.Path.Combine(folder, "remote-mark-keys-" + size + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                }
            }
            finally
            {
                hot.SetValue(remoteKey, false);
                down.SetValue(remoteKey, false);
                remoteKey.Enabled = true;
                remoteKey.Accent = accent;
                remoteKey.Pick(picked);
            }
            seen.Add(size + "% at " + remoteKey.Bounds.X + "," + remoteKey.Bounds.Y + " " + remoteKey.Width + "x" + remoteKey.Height + ", " + marked + " phone pixels");
            return null;
        }

        private static Bitmap KeyShot(Control control)
        {
            Bitmap shot = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(shot, new Rectangle(Point.Empty, control.Size));
            return shot;
        }

        internal string RemoteKeyFor(RemoteView view)
        {
            ShowRemote(view);
            bool picked = remoteKey.Picked;
            string words = remoteKey.AccessibilityObject.Value;
            SettingHint tip;
            if (!hints.TryGetValue(remoteKey, out tip) || remoteKey.AccessibleDescription != tip.Text) return "a key that lost its tip for " + words;
            return picked == (view.On && view.Online.Count > 0) ? words : "a key " + (picked ? "picked" : "not picked") + " for " + words;
        }

        private sealed class RemoteSheet : ModSheet
        {
            internal const int Wide = 600, Tall = 596, Rows = 5;
            private readonly bool live;
            private readonly Toggle power;
            private readonly Label status, idle, qrNote, askTitle, askName, codeLabel, askCode, askCheck, empty, page, note, revokeNote;
            private readonly Label[] names = new Label[Rows], states = new Label[Rows];
            private readonly Key[] revokes = new Key[Rows];
            private readonly RemoteQrBox qr;
            private readonly Key pair, allow, deny, saveHost, back, next, removeAll, showQr;
            private readonly Entry host;
            private readonly Timer clock = new Timer { Interval = 1000 };
            private RemoteView view = new RemoteView();
            private DateTime expires;
            private int first, armedAt;
            private string armedId, revealedQr, lastQr;
            private bool syncing, armedAll, streaming;

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
                Heading(this, "RELAY >", new Point(Z(28), Z(488)), band, White, 558);
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
                showQr = Make("Show QR", 106, 218, 108, 26, delegate { revealedQr = view.Qr; Fill(view); });
                showQr.Accent = Cyan;
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
                back = Make("Back", 316, 400, 58, 22, delegate { first -= Rows; Fill(view); });
                page = Words("", 378, 400, 132, 22, Small(), Faint);
                page.TextAlign = ContentAlignment.MiddleCenter;
                next = Make("Next", 514, 400, 58, 22, delegate { first += Rows; Fill(view); });
                removeAll = Make("Remove all phones", 316, 430, 256, 26, delegate { RemoveAll(); });
                removeAll.Accent = Hot;
                removeAll.AccessibleName = "Remove all phones and pair again";
                revokeNote = Words("", 316, 462, 256, 16, Small(), Faint);
                host = new Entry();
                host.Bounds = Box(28, 518, 460, 26);
                host.Cue = RemoteLink.DefaultHost;
                host.Box.AccessibleName = "Relay host";
                host.Box.KeyDown += delegate (object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveHost(); } };
                Controls.Add(host);
                saveHost = Make("Save", 496, 518, 76, 26, delegate { SaveHost(); });
                saveHost.AccessibleName = "Save relay host";
                note = Words("", 28, 552, 544, 18, Font, Cyan);
                clock.Tick += delegate { Tick(); };
                if (live) clock.Start();
                Frame.Fit(this);
            }

            internal void Fill(RemoteView shown)
            {
                view = shown ?? new RemoteView();
                if (streaming != Computer.Streaming || lastQr != view.Qr) revealedQr = null;
                streaming = Computer.Streaming;
                lastQr = view.Qr;
                first = Math.Max(0, Math.Min(first, (Math.Max(0, view.Phones.Count - 1) / Rows) * Rows));
                if (armedId != null && !view.Phones.Exists(delegate(RemoteDevice phone) { return phone.Id == armedId; })) armedId = null;
                syncing = true;
                power.Checked = view.On;
                syncing = false;
                Color tone;
                status.Text = StatusLine(view, out tone);
                status.ForeColor = tone;
                bool asking = view.Ask != null, showing = !asking && view.Qr != null, linking = !asking && !showing && view.LinkFor != null;
                bool hidden = showing && streaming && revealedQr != view.Qr;
                qr.Url = showing && !hidden ? view.Qr : null;
                qr.Visible = showing && !hidden;
                showQr.Visible = hidden;
                expires = showing || linking ? DateTime.Now.AddSeconds(view.QrSeconds) : DateTime.MaxValue;
                qrNote.Visible = showing && !hidden;
                qrNote.Text = showing ? "SCAN WITH YOUR PHONE · EXPIRES " + expires.ToString("HH:mm", CultureInfo.CurrentCulture) : "";
                idle.Visible = !asking && (!showing || hidden);
                idle.Text = hidden ? "QR hidden while streaming" : !view.On ? "Turn Remote on first" : !view.Ready ? "Waiting for the relay" : view.Expired ? "That code expired" : linking ? "Pairing link sent to " + view.LinkFor : "No code open";
                askTitle.Visible = askName.Visible = codeLabel.Visible = askCode.Visible = askCheck.Visible = allow.Visible = deny.Visible = asking;
                askName.Text = asking ? view.Ask.Name : "";
                askCode.Text = asking ? view.Ask.Code : "";
                pair.Text = showing || view.Expired ? "New code" : "Pair a phone";
                pair.Enabled = view.On && view.Ready && !asking;
                for (int i = 0; i < Rows; i++)
                {
                    bool on = first + i < view.Phones.Count;
                    names[i].Visible = states[i].Visible = revokes[i].Visible = on;
                    if (!on) continue;
                    RemoteDevice phone = view.Phones[first + i];
                    bool online = view.Online.Contains(phone.Id);
                    names[i].Text = phone.Name;
                    // A phone a pairing link let in with no Allow says so, so it's easy to spot and revoke.
                    states[i].Text = (online ? "CONNECTED" : "ADDED " + phone.Added.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture).ToUpperInvariant()) + (phone.From != null || phone.Parent != null ? " \u00b7 BY LINK" : "");
                    states[i].ForeColor = online ? Cyan : Faint;
                    revokes[i].Text = armedId == phone.Id ? "Sure?" : "Revoke";
                    revokes[i].Loud = armedId == phone.Id;
                    revokes[i].AccessibleName = "Revoke " + phone.Name;
                    revokes[i].Invalidate();
                }
                empty.Visible = view.Phones.Count == 0;
                back.Visible = next.Visible = page.Visible = view.Phones.Count > Rows;
                back.Enabled = first > 0;
                next.Enabled = first + Rows < view.Phones.Count;
                page.Text = "Page " + (first / Rows + 1) + " of " + ((Math.Max(1, view.Phones.Count) - 1) / Rows + 1);
                removeAll.Text = armedAll ? "Sure?" : "Remove all phones";
                removeAll.Loud = armedAll;
                revokeNote.Text = view.Phones.Exists(delegate(RemoteDevice phone) { return phone.Parent != null; }) ? "Revoke also removes the phones its links added" : "";
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
            private string Revoke(int row)
            {
                if (row < 0 || row >= Rows || first + row >= view.Phones.Count) return null;
                string id = view.Phones[first + row].Id;
                armedAll = false;
                if (armedId != id || unchecked(Environment.TickCount - armedAt) > 4000)
                {
                    armedId = id;
                    armedAt = Environment.TickCount;
                    Fill(view);
                    return null;
                }
                armedId = null;
                if (live) Frame.capri.RemoteRevoke(id);
                else Fill(view);
                return id;
            }

            private bool RemoveAll()
            {
                armedId = null;
                if (!armedAll || unchecked(Environment.TickCount - armedAt) > 4000)
                {
                    armedAll = true;
                    armedAt = Environment.TickCount;
                    Fill(view);
                    return false;
                }
                armedAll = false;
                if (live) Frame.capri.RemoteRemoveAll();
                else Fill(view);
                return true;
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
                if ((armedId != null || armedAll) && unchecked(Environment.TickCount - armedAt) > 4000)
                {
                    armedId = null;
                    armedAll = false;
                    Fill(view);
                }
                if (streaming != Computer.Streaming) Fill(view);
                if ((view.Qr != null || view.LinkFor != null) && DateTime.Now >= expires) Fill(Frame.capri.RemoteView());
            }

            // The smoke test's check that each state shows what it should.
            internal string Matches(RemoteView expected)
            {
                if (expected.Ask != null && (!allow.Visible || askCode.Text != expected.Ask.Code || qr.Visible)) return "the Allow prompt didn't show its code";
                bool hidden = Computer.Streaming && revealedQr != expected.Qr;
                if (expected.Ask == null && expected.Qr != null && (qr.Visible == hidden || showQr.Visible != hidden || allow.Visible || hidden && (!idle.Visible || idle.Text != "QR hidden while streaming"))) return "the QR didn't follow the streaming setting";
                if (expected.Phones.Count > 0 && (!revokes[0].Visible || names[0].Text != expected.Phones[first].Name)) return "the paired phones didn't show";
                if (expected.Ask == null && expected.Qr == null && expected.LinkFor != null && (qr.Visible || !idle.Visible || idle.Text != "Pairing link sent to " + expected.LinkFor)) return "a phone's pairing link didn't show as words, with no QR";
                for (int i = 0; i < Rows && first + i < expected.Phones.Count; i++)
                    if ((expected.Phones[first + i].From != null || expected.Phones[first + i].Parent != null) && !states[i].Text.EndsWith(" BY LINK", StringComparison.Ordinal)) return "a phone added by link didn't say so";
                if (expected.Phones.Count == 0 && !empty.Visible) return "an empty list didn't say so";
                if (power.Checked != expected.On) return "the switch didn't match";
                return null;
            }

            internal string CheckPhones(string picture)
            {
                RemoteView original = view;
                HashSet<string> reached = new HashSet<string>(StringComparer.Ordinal);
                first = 0;
                Fill(original);
                do
                {
                    string problem = Crowded() ?? Matches(original);
                    if (problem != null) return problem;
                    for (int i = 0; i < Rows && first + i < original.Phones.Count; i++)
                    {
                        RemoteDevice phone = original.Phones[first + i];
                        if (!revokes[i].Visible || !revokes[i].Enabled || revokes[i].AccessibleName != "Revoke " + phone.Name || revokes[i].Text != "Revoke") return "a phone has no Revoke key";
                        reached.Add(phone.Id);
                        if (Revoke(i) != null || revokes[i].Text != "Sure?" || Revoke(i) != phone.Id) return "a phone's Revoke key didn't confirm its own id";
                    }
                    if (picture != null && first > 0) Picture(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(picture), System.IO.Path.GetFileNameWithoutExtension(picture) + "-page-" + (first / Rows + 1) + ".png"));
                    if (!next.Enabled) break;
                    next.PerformClick();
                } while (true);
                if (reached.Count != original.Phones.Count) return "paging missed a paired phone";
                first = 0;
                Fill(original);
                string target = original.Phones[0].Id;
                revokes[0].PerformClick();
                RemoteView reordered = new RemoteView { Phones = new List<RemoteDevice>(original.Phones) };
                reordered.Phones.Reverse();
                Fill(reordered);
                for (first = 0; first < reordered.Phones.Count; first += Rows)
                {
                    Fill(reordered);
                    for (int i = 0; i < Rows && first + i < reordered.Phones.Count; i++)
                        if ((revokes[i].Text == "Sure?") != (reordered.Phones[first + i].Id == target)) return "Sure? moved to a different phone";
                }
                first = (reordered.Phones.Count - 1) / Rows * Rows;
                Fill(reordered);
                if (Revoke((reordered.Phones.Count - 1) % Rows) != target) return "the moved phone couldn't be confirmed";
                first = 0;
                Fill(original);
                Revoke(0);
                Fill(reordered);
                if (Revoke(0) != null || armedId != reordered.Phones[0].Id) return "a different phone's second press revoked it";
                if (RemoveAll() || armedId != null || !armedAll || !RemoveAll()) return "Remove all phones didn't require its own second press";
                Fill(original);
                return null;
            }

            internal string CheckQr()
            {
                if (qr.Visible || !showQr.Visible || !idle.Visible) return "the QR wasn't hidden while streaming";
                showQr.PerformClick();
                if (!qr.Visible || showQr.Visible || !qrNote.Visible) return "Show QR didn't reveal the pairing code";
                string original = view.Qr;
                try
                {
                    view.Qr += "&new=1";
                    Fill(view);
                    if (qr.Visible || !showQr.Visible) return "a new pairing code stayed revealed on stream";
                }
                finally { view.Qr = original; Fill(view); }
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
#endif
}
