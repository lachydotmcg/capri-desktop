using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private IntroSheet introSheet;

        internal void OpenIntroPicker()
        {
            if (introSheet != null && !introSheet.IsDisposed && introSheet.Zoom != zoom) introSheet.Close();
            if (introSheet == null || introSheet.IsDisposed) introSheet = new IntroSheet(this, zoom);
            introSheet.Fill();
            if (!introSheet.Visible) introSheet.Show(this);
            else introSheet.Activate();
        }

        private bool IntroSheetUp { get { return introSheet != null && !introSheet.IsDisposed && introSheet.Visible; } }
        internal bool IntroPickerOpen { get { return IntroSheetUp; } }
        internal string[] IntroCards { get { return IntroSheetUp ? introSheet.Ids : new string[0]; } }
        internal string IntroPickShown { get { return introPick.Text; } }
        internal Rectangle IntroPickBounds { get { return introPick.Bounds; } }
        internal Rectangle IntroSwitchBounds { get { return intro.Bounds; } }
        internal void PickIntroCard(string id) { if (IntroSheetUp) introSheet.Pick(id); }
        internal void PlayIntroCard() { if (IntroSheetUp) introSheet.PlayPicked(); }
        internal int IntroCardDrawn(string id) { return IntroSheetUp ? introSheet.Drawn(id) : 0; }
        internal string IntroCardPicked { get { return IntroSheetUp ? introSheet.Picked : null; } }
        internal string IntroPickerNote { get { return IntroSheetUp ? introSheet.Note : ""; } }
        internal string IntroPickerCrowded { get { return IntroSheetUp ? introSheet.Crowded() : "the picker isn't open"; } }
        internal void TickIntroPicker(double seconds) { if (IntroSheetUp) introSheet.Tick(seconds); }
        internal Bitmap IntroCardPicture(string id) { return IntroSheetUp ? introSheet.Picture(id) : null; }
        internal bool IntroPreviewsReady { get { return IntroSheetUp && introSheet.Ready; } }
        internal void IntroPickerShot(string path) { if (IntroSheetUp) introSheet.Snapshot(path); }
        internal void CloseIntroPicker() { if (introSheet != null && !introSheet.IsDisposed) introSheet.Close(); }

        private sealed class IntroCard : Control
        {
            internal string Id, Title, About;
            internal ICapriIntro Preview;
            internal bool Near;
            internal Bitmap Stage;
            internal double Start;
            internal bool Picked;
            internal int Drawn;
            internal Action Chosen, Played, Back, On;
            private bool over;

            internal IntroCard()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
                BackColor = Ground;
                Cursor = Look.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.RadioButton;
                AccessibleDefaultActionDescription = "Pick";
            }

            internal void Describe() { AccessibleName = Title + ", " + About + (Picked ? ", picked" : ""); }

            protected override void OnMouseEnter(EventArgs e) { over = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { over = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
            protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
            protected override void OnClick(EventArgs e) { if (Chosen != null) Chosen(); base.OnClick(e); }
            protected override void OnDoubleClick(EventArgs e) { if (Played != null) Played(); base.OnDoubleClick(e); }

            protected override bool IsInputKey(Keys keyData)
            {
                Keys key = keyData & Keys.KeyCode;
                return key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Enter || key == Keys.Space || base.IsInputKey(keyData);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space) { if (Chosen != null) Chosen(); e.Handled = true; }
                else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Down) { if (On != null) On(); e.Handled = true; }
                else if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Up) { if (Back != null) Back(); e.Handled = true; }
                base.OnKeyDown(e);
            }

            internal int ThumbHeight { get { return Stage == null ? Width * 9 / 16 : (int)Math.Round(Width * Stage.Height / (double)Stage.Width); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                int thumbHigh = Math.Min(Height, ThumbHeight);
                Rectangle thumb = new Rectangle(0, 0, Width, thumbHigh);
                if (Stage != null)
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(Stage, thumb);
                }
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool marked = Focused && ShowFocusCues;
                Color rim = Picked ? Cyan : over || marked ? Rose : Faint;
                float line = Picked || marked ? Math.Max(2f, 2 * s) : Math.Max(1f, s);
                using (Pen pen = new Pen(rim, line)) graphics.DrawRectangle(pen, line / 2, line / 2, Width - line, thumbHigh - line);
                int textTop = thumbHigh + (int)Math.Round(5 * s);
                Rectangle name = new Rectangle(0, textTop, Width, (int)Math.Round(18 * s));
                TextRenderer.DrawText(graphics, Picked ? Title + "  \u2713" : Title, Font, name, Picked ? Cyan : White, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                using (Font small = new Font(Font.FontFamily, Font.Size * 0.86f, FontStyle.Regular, Font.Unit))
                    TextRenderer.DrawText(graphics, About, small, new Rectangle(0, name.Bottom, Width, Math.Max(1, Height - name.Bottom)), Faint, TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Free();
                    if (Stage != null) { Stage.Dispose(); Stage = null; }
                }
                base.Dispose(disposing);
            }

            internal void Free()
            {
                DoorIntro door = Preview as DoorIntro;
                if (door != null) door.Stop();
                ArtIntro art = Preview as ArtIntro;
                if (art != null) art.Dispose();
                Preview = null;
            }
        }

        private sealed class IntroSheet : Form, LookWindow
        {
            private const int Edge = 28, CardWide = 176, Gap = 12, Across = 3, CardsTop = 80, Words = 44, StageWide = 480;
            private const double Tail = 0.8;
            private readonly Mainframe owner;
            internal readonly float Zoom;
            private readonly List<IntroCard> cards = new List<IntroCard>();
            private readonly Timer beat = new Timer { Interval = 40 };
            private readonly Stopwatch clock = Stopwatch.StartNew();
            private readonly Label note;
            private readonly Key play;
            private readonly Size stage;
            private readonly Rectangle herOnStage, herNear;
            private Bitmap backdrop, nearBackdrop;

            internal IntroSheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = "Startup animation";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Mainframe.Type(9.5f);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                Rectangle her;
                Rectangle screen = owner.capri.IntroStage(out her);
                double k = StageWide / (double)Math.Max(1, screen.Width);
                stage = new Size(StageWide, Math.Max(120, Math.Min(400, (int)Math.Round(screen.Height * k))));
                herOnStage = new Rectangle((int)Math.Round(her.X * k), (int)Math.Round(her.Y * k), Math.Max(1, (int)Math.Round(her.Width * k)), Math.Max(1, (int)Math.Round(her.Height * k)));
                backdrop = Snap(screen, stage);
                Rectangle near = Near(screen.Size, her, stage);
                double n = stage.Width / (double)near.Width;
                herNear = new Rectangle((int)Math.Round((her.X - near.X) * n), (int)Math.Round((her.Y - near.Y) * n), Math.Max(1, (int)Math.Round(her.Width * n)), Math.Max(1, (int)Math.Round(her.Height * n)));
                nearBackdrop = Snap(new Rectangle(screen.X + near.X, screen.Y + near.Y, near.Width, near.Height), stage);
                List<string> ids = new List<string>();
                foreach (string id in Intros.Built) if (!Intros.IsArt(id) || ArtIntro.Frames(id) > 0) ids.Add(id);
                string modName = owner.capri.IntroModName;
                if (modName != null) ids.Add(Intros.FromMods);
                int thumbHigh = (int)Math.Round(CardWide * stage.Height / (double)stage.Width);
                int rows = (ids.Count + Across - 1) / Across;
                int footer = CardsTop + rows * (thumbHigh + Words + Gap) + 4;
                ClientSize = new Size(Edge * 2 + Across * CardWide + (Across - 1) * Gap, footer + 28 + 24);
                Heading(this, "STARTUP ANIMATION >", new Point(24, 15), Display(14f), Rose, 0);
                Key close = Make("", ClientSize.Width - 54, 20, 26, delegate { Close(); });
                close.Height = 22;
                close.Glyph = 2;
                close.AccessibleName = "Close";
                CancelButton = close;
                note = new Note { ForeColor = Faint, BackColor = Ground, Bounds = new Rectangle(Edge, 50, ClientSize.Width - Edge * 2, 20) };
                Controls.Add(note);
                for (int i = 0; i < ids.Count; i++)
                {
                    string id = ids[i];
                    IntroCard card = new IntroCard
                    {
                        Id = id,
                        Near = id == Intros.Classic || id == Intros.Door,
                        Title = id == Intros.FromMods ? modName : Intros.NameOf(id),
                        About = id == Intros.FromMods ? "From your mods" : Intros.AboutOf(id),
                        Font = Mainframe.Type(9.5f, true),
                        Bounds = new Rectangle(Edge + i % Across * (CardWide + Gap), CardsTop + i / Across * (thumbHigh + Words + Gap), CardWide, thumbHigh + Words)
                    };
                    card.Stage = new Bitmap(stage.Width, stage.Height, PixelFormat.Format32bppPArgb);
                    card.Preview = owner.capri.IntroPreview(id);
                    if (card.Preview != null)
                    {
                        try { card.Preview.Begin(card.Near ? nearBackdrop : backdrop, new Rectangle(Point.Empty, stage), card.Near ? herNear : herOnStage); }
                        catch (ArgumentException error) { Brain.Log("intro preview", -1, error.ToString()); card.Free(); }
                    }
                    card.Chosen = delegate { Pick(id); };
                    card.Played = delegate { Pick(id); PlayPicked(); };
                    card.On = delegate { Step(card, 1); };
                    card.Back = delegate { Step(card, -1); };
                    card.Describe();
                    cards.Add(card);
                    Controls.Add(card);
                }
                play = Make("Play on screen", Edge, footer, 220, delegate { PlayPicked(); });
                play.Accent = Cyan;
                play.Loud = true;
                play.AccessibleName = "Play on screen: she hides, the picked startup animation plays across your screen, and she comes back";
                Make("Done", ClientSize.Width - Edge - 120, footer, 120, delegate { Close(); });
                beat.Tick += delegate { Tick(clock.Elapsed.TotalSeconds); };
                VisibleChanged += delegate { if (Visible) beat.Start(); else beat.Stop(); };
                FormClosed += delegate { Free(); };
                Tick(0);
                if (zoom != 1f) { Grow(this, zoom); Scale(new SizeF(zoom, zoom)); }
                owner.Fit(this);
                ResumeLayout(false);
            }

            internal string[] Ids
            {
                get
                {
                    string[] ids = new string[cards.Count];
                    for (int i = 0; i < cards.Count; i++) ids[i] = cards[i].Id;
                    return ids;
                }
            }

            internal string Picked
            {
                get
                {
                    foreach (IntroCard card in cards) if (card.Picked) return card.Id;
                    return null;
                }
            }

            internal string Note { get { return note.Text; } }

            internal void Snapshot(string path)
            {
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(path, ImageFormat.Png);
                }
            }

            internal bool Ready
            {
                get
                {
                    foreach (IntroCard card in cards)
                    {
                        ArtIntro art = card.Preview as ArtIntro;
                        if (art != null && !art.Filled) return false;
                    }
                    return true;
                }
            }

            internal int Drawn(string id)
            {
                IntroCard card = Find(id);
                return card == null ? 0 : card.Drawn;
            }

            internal Bitmap Picture(string id)
            {
                IntroCard card = Find(id);
                return card == null || card.Stage == null ? null : new Bitmap(card.Stage);
            }

            private IntroCard Find(string id)
            {
                foreach (IntroCard card in cards) if (card.Id == id) return card;
                return null;
            }

            internal void Fill()
            {
                string now = owner.capri.IntroPick;
                foreach (IntroCard card in cards)
                {
                    bool picked = card.Id == now;
                    if (picked == card.Picked) continue;
                    card.Picked = picked;
                    card.Describe();
                    card.Invalidate();
                }
                note.Text = owner.capri.Intro ? "Click one to pick it. It plays each time Capri starts." : "Glitch in at startup is off, so none plays when Capri starts.";
            }

            internal void Pick(string id)
            {
                IntroCard card = Find(id);
                if (card == null) return;
                owner.capri.SetIntroPick(id);
                card.Start = clock.Elapsed.TotalSeconds;
                Fill();
            }

            internal void PlayPicked()
            {
                string id = Picked;
                if (id != null) owner.capri.PlayIntroNow(id);
            }

            private void Step(IntroCard from, int way)
            {
                int at = cards.IndexOf(from), next = at + way;
                if (next >= 0 && next < cards.Count) cards[next].Focus();
                else if (way > 0) play.Focus();
            }

            internal void Tick(double now)
            {
                foreach (IntroCard card in cards)
                {
                    if (card.Stage == null) continue;
                    using (Graphics paper = Graphics.FromImage(card.Stage))
                    {
                        paper.CompositingMode = CompositingMode.SourceCopy;
                        paper.InterpolationMode = InterpolationMode.NearestNeighbor;
                        Bitmap behind = card.Near ? nearBackdrop : backdrop;
                        paper.DrawImage(behind, new Rectangle(Point.Empty, stage), 0, 0, behind.Width, behind.Height, GraphicsUnit.Pixel);
                        paper.CompositingMode = CompositingMode.SourceOver;
                        if (card.Preview == null) { Placard(paper, card.Id == Intros.FromMods ? "Its own intro" : "No preview"); }
                        else
                        {
                            double length = Math.Max(0.5, card.Preview.Seconds);
                            double at = Math.Max(0, now - card.Start) % (length + Tail);
                            if (at < length)
                            {
                                try
                                {
                                    card.Preview.Draw(paper, new Rectangle(Point.Empty, stage), card.Near ? herNear : herOnStage, at);
                                    card.Drawn++;
                                }
                                catch (ArgumentException error) { Brain.Log("intro preview", -1, error.ToString()); card.Free(); }
                                catch (ExternalException error) { Brain.Log("intro preview", -1, error.ToString()); card.Free(); }
                            }
                        }
                    }
                    card.Invalidate();
                }
            }

            private void Placard(Graphics paper, string words)
            {
                using (Brush dim = new SolidBrush(Color.FromArgb(150, Ground))) paper.FillRectangle(dim, new Rectangle(Point.Empty, stage));
                using (Font big = Mainframe.Type(18f, true))
                    TextRenderer.DrawText(paper, words, big, new Rectangle(Point.Empty, stage), White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            private static Rectangle Near(Size screen, Rectangle her, Size stage)
            {
                int high = Math.Min(screen.Height, Math.Max(screen.Height / 6, (int)(her.Height * 2.4)));
                int wide = Math.Min(screen.Width, (int)Math.Round(high * stage.Width / (double)stage.Height));
                high = Math.Max(1, (int)Math.Round(wide * stage.Height / (double)stage.Width));
                int x = Math.Max(0, Math.Min(screen.Width - wide, her.X + her.Width / 2 - wide / 2));
                int y = Math.Max(0, Math.Min(screen.Height - high, her.Y + her.Height / 2 - high / 2));
                return new Rectangle(x, y, Math.Max(1, wide), high);
            }

            private static Bitmap Snap(Rectangle screen, Size size)
            {
                Bitmap small = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
                try
                {
                    using (Bitmap whole = new Bitmap(screen.Width, screen.Height, PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics grab = Graphics.FromImage(whole)) grab.CopyFromScreen(screen.Location, Point.Empty, screen.Size);
                        using (Graphics paper = Graphics.FromImage(small))
                        {
                            paper.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            paper.PixelOffsetMode = PixelOffsetMode.HighQuality;
                            paper.DrawImage(whole, new Rectangle(Point.Empty, size));
                        }
                    }
                }
                catch (Win32Exception) { using (Graphics paper = Graphics.FromImage(small)) paper.Clear(Color.FromArgb(46, 58, 74)); }
                catch (ArgumentException) { using (Graphics paper = Graphics.FromImage(small)) paper.Clear(Color.FromArgb(46, 58, 74)); }
                return small;
            }

            internal string Crowded()
            {
                for (int first = 0; first < Controls.Count; first++)
                {
                    Control a = Controls[first];
                    if (!a.Visible) continue;
                    if (!ClientRectangle.Contains(a.Bounds)) return a.AccessibleName + " leaves the picker";
                    for (int second = first + 1; second < Controls.Count; second++)
                        if (Controls[second].Visible && a.Bounds.IntersectsWith(Controls[second].Bounds)) return "picker rows overlap: " + (a.AccessibleName ?? a.Text) + " and " + (Controls[second].AccessibleName ?? Controls[second].Text);
                }
                return null;
            }

            private void Free()
            {
                beat.Stop();
                beat.Dispose();
                foreach (IntroCard card in cards) card.Free();
                if (backdrop != null) { backdrop.Dispose(); backdrop = null; }
                if (nearBackdrop != null) { nearBackdrop.Dispose(); nearBackdrop = null; }
            }

            private Key Make(string text, int x, int y, int width, EventHandler click)
            {
                Key made = new Key { Text = text, Font = Mainframe.Type(9f, true), Bounds = new Rectangle(x, y, width, 28) };
                made.Click += click;
                Controls.Add(made);
                return made;
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                float s = ScaleOf(this);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * s);
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Clip(this, ScaleOf(this)); Invalidate(true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(e.Graphics, this, ScaleOf(this));
                Edging(e.Graphics, this, ScaleOf(this));
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }
    }
}
