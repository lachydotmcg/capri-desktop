using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private ModsSheet browser;

        private float SheetZoom(int wide, int tall)
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            float dpi = DeviceDpi / 96f;
            return Math.Max(0.75f, Math.Min(zoom, Math.Min(area.Width / (wide * dpi), area.Height / (tall * dpi))));
        }

        private void BrowseMods()
        {
            float fit = SheetZoom(ModsSheet.Wide, ModsSheet.Tall);
            if (browser != null && !browser.IsDisposed && browser.Zoom != fit) browser.Close();
            bool fresh = browser == null || browser.IsDisposed;
            if (fresh) browser = new ModsSheet(this, fit);
            if (!browser.Visible) browser.Show(this);
            browser.Activate();
            if (fresh) browser.LoadMods();
        }

        private void PublishMod(int row)
        {
            Mod mod = capri.ModList.All[modFirst + row];
            PublishSheet sheet = new PublishSheet(this, SheetZoom(PublishSheet.Wide, PublishSheet.Tall), mod);
            sheet.ShowDialog(this);
            sheet.Dispose();
        }

        private abstract class ModSheet : Form
        {
            internal readonly float Zoom;
            protected readonly Mainframe Frame;
            private readonly int designWide, designTall;

            protected ModSheet(Mainframe owner, float zoom, string title, string heading, int wide, int tall)
            {
                Frame = owner;
                Zoom = zoom;
                designWide = wide;
                designTall = tall;
                Text = title;
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoScaleDimensions = new SizeF(96, 96);
                Font = Type(9.5f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(wide), Z(tall));
                Heading(this, heading, new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Key shut = new Key();
                shut.Bounds = Box(wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
            }

            protected int Z(int value) { return (int)Math.Round(value * Zoom); }

            protected Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            protected Font Small() { return Look.Mono(8.5f * Zoom, FontStyle.Regular, GraphicsUnit.Point); }

            protected Label Words(string text, int x, int y, int width, int height, Font font, Color colour)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoEllipsis = true;
                label.Bounds = Box(x, y, width, height);
                Controls.Add(label);
                return label;
            }

            protected Key Make(string text, int x, int y, int width, int height, EventHandler click)
            {
                Key made = new Key();
                made.Text = text;
                made.Font = Type(9f * Zoom, true);
                made.Bounds = Box(x, y, width, height);
                made.Click += click;
                Controls.Add(made);
                return made;
            }

            internal string Crowded()
            {
                float s = ScaleOf(this);
                // A small desktop clamps the window, so check the size it was drawn for.
                Rectangle area = new Rectangle(0, 0, Math.Max(ClientSize.Width, Z(designWide)), Math.Max(ClientSize.Height, Z(designTall)));
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                    if (!area.Contains(a.Bounds)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds;
                    Key key = a as Key;
                    if (key != null && key.Text.Length > 0 && TextRenderer.MeasureText(key.Text, key.Font).Width + (int)Math.Ceiling(12 * s) > key.Width) return "doesn't fit: " + key.Text;
                    for (int j = i + 1; j < Controls.Count; j++)
                    {
                        Control b = Controls[j];
                        if (!b.Visible || (b is Label && b.Text.Length == 0)) continue;
                        if (a.Bounds.IntersectsWith(b.Bounds)) return "overlap: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " / " + (b.Text.Length > 0 ? b.Text : b.AccessibleName);
                    }
                }
                return null;
            }

            internal void Picture(string path)
            {
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(path, ImageFormat.Png);
                }
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Screen.FromControl(Frame).WorkingArea;
                int x = Frame.Left + (Frame.Width - Width) / 2, y = Frame.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Clip(this, ScaleOf(this));
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(e.Graphics, this, s);
                Edging(e.Graphics, this, s);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        private sealed class ModPreview : Control
        {
            private Image image;

            internal ModPreview()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Well;
            }

            internal Image Image
            {
                get { return image; }
                set { if (image != value) { image = value; Invalidate(); } }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                using (Brush fill = new SolidBrush(Well)) graphics.FillRectangle(fill, ClientRectangle);
                if (image != null)
                {
                    float scale = Math.Max(Width / (float)image.Width, Height / (float)image.Height);
                    float wide = image.Width * scale, tall = image.Height * scale;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.DrawImage(image, (Width - wide) / 2, (Height - tall) / 2, wide, tall);
                }
                else TextRenderer.DrawText(graphics, "NO PREVIEW", Font, ClientRectangle, Faint, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                using (Pen rim = new Pen(WellEdge)) graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
            }
        }

        private sealed class ModStars : Control
        {
            internal event Action<int> Picked;
            private double value;
            private bool canRate;
            private int hover;

            internal ModStars()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Ground;
                TabStop = false;
                AccessibleRole = AccessibleRole.Slider;
            }

            internal double Value { get { return value; } set { this.value = value; Invalidate(); } }

            internal bool CanRate
            {
                get { return canRate; }
                set { canRate = value; TabStop = value; hover = 0; Cursor = value ? Cursors.Hand : Cursors.Default; Invalidate(); }
            }

            internal void Pick(int stars) { if (canRate && Picked != null) Picked(stars); }

            private static PointF[] Shape(RectangleF box)
            {
                PointF[] points = new PointF[10];
                float x = box.Left + box.Width / 2, y = box.Top + box.Height / 2, far = Math.Min(box.Width, box.Height) / 2;
                for (int i = 0; i < 10; i++)
                {
                    double angle = -Math.PI / 2 + i * Math.PI / 5;
                    float reach = i % 2 == 0 ? far : far * 0.45f;
                    points[i] = new PointF(x + (float)Math.Cos(angle) * reach, y + (float)Math.Sin(angle) * reach);
                }
                return points;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, ScaleOf(this));
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                double shown = hover > 0 ? hover : value;
                float each = Width / 5f;
                for (int i = 0; i < 5; i++)
                {
                    RectangleF box = new RectangleF(i * each + 1, 1, each - 2, Height - 2);
                    PointF[] star = Shape(box);
                    using (Brush empty = new SolidBrush(WellEdge)) graphics.FillPolygon(empty, star);
                    double lit = Math.Max(0, Math.Min(1, shown - i));
                    if (lit <= 0) continue;
                    GraphicsState kept = graphics.Save();
                    graphics.SetClip(new RectangleF(box.Left, box.Top, box.Width * (float)lit, box.Height));
                    using (Brush full = new SolidBrush(Cyan)) graphics.FillPolygon(full, star);
                    graphics.Restore(kept);
                }
                if (Focused) ControlPaint.DrawFocusRectangle(graphics, ClientRectangle);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!canRate) return;
                int over = Math.Max(1, Math.Min(5, e.X * 5 / Math.Max(1, Width) + 1));
                if (over != hover) { hover = over; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hover != 0) { hover = 0; Invalidate(); }
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (canRate && e.Button == MouseButtons.Left) Pick(Math.Max(1, Math.Min(5, e.X * 5 / Math.Max(1, Width) + 1)));
            }

            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }

            protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.KeyCode >= Keys.D1 && e.KeyCode <= Keys.D5) { Pick(e.KeyCode - Keys.D0); e.Handled = true; }
                else if (e.KeyCode >= Keys.NumPad1 && e.KeyCode <= Keys.NumPad5) { Pick(e.KeyCode - Keys.NumPad0); e.Handled = true; }
            }
        }

        private sealed class ModsSheet : ModSheet
        {
            internal const int Wide = 744, Tall = 654, Across = 3, Down = 2, Cards = Across * Down, CardWide = 222, CardStep = 233, CardTop = 110, CardTall = 238;
            private readonly ModPreview[] previews = new ModPreview[Cards];
            private readonly Label[] titles = new Label[Cards], metas = new Label[Cards], counts = new Label[Cards], stats = new Label[Cards];
            private readonly ModStars[] stars = new ModStars[Cards];
            private readonly Key[] installs = new Key[Cards], views = new Key[Cards];
            private readonly Key[] filters = new Key[ModDirectory.Kinds.Length];
            private readonly Entry search;
            private readonly Label status, empty, page;
            private readonly Key back, next;
            private readonly Dictionary<string, Image> pictures = new Dictionary<string, Image>();
            private readonly HashSet<string> asked = new HashSet<string>();
            private List<ModListing> listings = new List<ModListing>(), shown = new List<ModListing>();
            private int filter, first, ticket;
            private bool installing, statsLive;
            internal bool Inline;

            internal ModsSheet(Mainframe owner, float zoom) : base(owner, zoom, "Mods directory", "MODS DIRECTORY >", Wide, Tall)
            {
                Words("The directory checks mods for malware first. Code mods still ask before they run.", 28, 52, 688, 16, Small(), Faint);
                search = new Entry();
                search.Bounds = Box(28, 74, 352, 26);
                search.Cue = "Search by name";
                search.Box.AccessibleName = "Search the mods directory";
                search.Box.AccessibleDescription = "Search mods";
                search.Box.TextChanged += delegate { first = 0; Fill(); };
                Controls.Add(search);
                for (int i = 0; i < filters.Length; i++)
                {
                    int kind = i;
                    filters[i] = Make(ModDirectory.Kinds[i], 388 + i * 82, 76, 76, 22, delegate { first = 0; filter = kind; Fill(); });
                    filters[i].AccessibleName = "Show " + ModDirectory.Kinds[i].ToLowerInvariant() + " in the mods directory";
                    filters[i].AccessibleDescription = "Mod filter";
                }
                for (int i = 0; i < Cards; i++)
                {
                    int card = i, x = 28 + CardStep * (i % Across), y = CardTop + CardTall * (i / Across);
                    previews[i] = new ModPreview();
                    previews[i].Bounds = Box(x, y, CardWide, 122);
                    previews[i].Font = Small();
                    Controls.Add(previews[i]);
                    titles[i] = Words("", x, y + 126, CardWide, 20, Type(10.5f * zoom, true), White);
                    metas[i] = Words("", x, y + 146, CardWide, 16, Small(), Faint);
                    stars[i] = new ModStars();
                    counts[i] = Words("", x, y + 162, CardWide, 16, Small(), Faint);
                    stars[i].Bounds = Box(x, y + 180, 78, 16);
                    stars[i].AccessibleDescription = "Rate mod";
                    stars[i].Picked += delegate(int picked) { Rate(card, picked); };
                    Controls.Add(stars[i]);
                    stats[i] = Words("", x + 84, y + 180, CardWide - 84, 16, Small(), Faint);
                    installs[i] = Make("Install", x, y + 200, 108, 24, delegate { Install(card); });
                    installs[i].AccessibleDescription = "Install mod";
                    views[i] = Make("View", x + 114, y + 200, 108, 24, delegate { View(card); });
                    views[i].AccessibleDescription = "View mod";
                }
                back = Make("Back", 28, 580, 60, 22, delegate { first -= Cards; Fill(); });
                page = Words("", 272, 582, 200, 18, Font, Faint);
                page.TextAlign = ContentAlignment.MiddleCenter;
                next = Make("Next", 656, 580, 60, 22, delegate { first += Cards; Fill(); });
                empty = Words("", 28, 250, 688, 24, Font, Faint);
                empty.TextAlign = ContentAlignment.MiddleCenter;
                status = Words("", 28, 608, 688, 36, Font, Faint);
                Fill();
            }

            private void Run(Action work)
            {
                if (Inline) work();
                else ThreadPool.QueueUserWorkItem(delegate { work(); });
            }

            private void Back(Action work)
            {
                if (Inline) { work(); return; }
                try { if (!IsDisposed) BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) work(); })); }
                catch (InvalidOperationException) { }
            }

            private void Says(string text, bool problem)
            {
                status.Text = text;
                status.ForeColor = problem ? Rose : Cyan;
            }

            private ModListing At(int card) { int at = first + card; return at < shown.Count ? shown[at] : null; }

            private bool Have(ModListing listing)
            {
                return Frame.capri.ModList.All.Exists(installed => string.Equals(installed.File.Id, listing.Id, StringComparison.OrdinalIgnoreCase));
            }

            internal void LoadMods()
            {
                int mine = ++ticket;
                listings = new List<ModListing>();
                statsLive = false;
                first = 0;
                Says("Looking in the mods directory...", false);
                Fill();
                Run(delegate
                {
                    List<ModListing> found = null;
                    string problem = null;
                    try { found = ModDirectory.Browse(); }
                    catch (Exception error) { problem = DirectoryProblem(error, "Couldn't reach the mods directory: "); }
                    Back(delegate { if (mine == ticket) Showing(found, problem, mine); });
                });
            }

            private void Showing(List<ModListing> found, string problem, int mine)
            {
                listings = found ?? new List<ModListing>();
                Fill();
                if (problem != null) { Says(problem, true); return; }
                if (listings.Count == 0) { Says("No mods in the directory yet.", false); return; }
                status.Text = "";
                Run(delegate
                {
                    Dictionary<string, ModStats> read = null;
                    try { read = ModDirectory.Stats(); }
                    catch (Exception) { }
                    Back(delegate
                    {
                        if (mine != ticket || read == null) return;
                        statsLive = true;
                        ModDirectory.Apply(listings, read);
                        Fill();
                    });
                });
            }

            private void AskForPicture(ModListing listing)
            {
                string sha = listing.PreviewSha;
                if (sha == null || !asked.Add(sha)) return;
                Run(delegate
                {
                    Image picture = null;
                    try { picture = ModDirectory.Picture(ModDirectory.Preview(listing, ModDirectory.PreviewFolder)); }
                    catch (Exception) { }
                    if (picture == null) return;
                    Back(delegate { pictures[sha] = picture; Fill(); });
                });
            }

            private void Fill()
            {
                shown = ModDirectory.Filter(listings, search.Box.Text, filter);
                first = Math.Max(0, Math.Min(first, (Math.Max(0, shown.Count - 1) / Cards) * Cards));
                empty.Visible = shown.Count == 0;
                empty.Text = listings.Count == 0 ? "Nothing to show from the mods directory." : "No mods match that.";
                for (int i = 0; i < filters.Length; i++)
                {
                    filters[i].Accent = i == filter ? Cyan : Faint;
                    filters[i].Invalidate();
                }
                for (int i = 0; i < Cards; i++)
                {
                    ModListing listing = At(i);
                    bool on = listing != null;
                    previews[i].Visible = titles[i].Visible = metas[i].Visible = installs[i].Visible = views[i].Visible = on;
                    stars[i].Visible = stats[i].Visible = counts[i].Visible = on && statsLive;
                    if (!on) continue;
                    bool have = Have(listing);
                    AskForPicture(listing);
                    Image picture;
                    previews[i].Image = listing.PreviewSha != null && pictures.TryGetValue(listing.PreviewSha, out picture) ? picture : null;
                    previews[i].AccessibleName = listing.Title + " preview";
                    titles[i].Text = listing.Title;
                    metas[i].Text = listing.Kind + " " + Math.Max(1, listing.Size / 1024) + " KB." + (have ? " Installed." : "");
                    stars[i].Value = listing.Rating;
                    stars[i].CanRate = statsLive && have;
                    stars[i].AccessibleName = "Rate " + listing.Title + ", " + (listing.Votes == 0 ? "no ratings yet" : listing.Rating.ToString("0.0") + " stars") + (have ? ". Press 1 to 5." : "");
                    stats[i].Text = ModDirectory.RatingLine(listing);
                    counts[i].Text = ModDirectory.DownloadLine(listing);
                    installs[i].Text = have ? "Reinstall" : "Install";
                    installs[i].Enabled = !installing;
                    installs[i].AccessibleName = (have ? "Reinstall " : "Install ") + listing.Title;
                    installs[i].Invalidate();
                    views[i].AccessibleName = "View " + listing.Title + " on GitHub";
                }
                bool pages = shown.Count > Cards;
                back.Visible = next.Visible = page.Visible = pages;
                back.Enabled = first > 0;
                next.Enabled = first + Cards < shown.Count;
                page.Text = "Page " + (first / Cards + 1) + " of " + (Math.Max(0, shown.Count - 1) / Cards + 1);
            }

            private void Install(int card)
            {
                ModListing listing = At(card);
                if (installing || listing == null) return;
                installing = true;
                Says("Downloading " + listing.Title + "...", false);
                Fill();
                string under = Path.Combine(Path.GetTempPath(), "capri-mod-downloads");
                Run(delegate
                {
                    string file = null, problem = null;
                    try { file = ModDirectory.Download(listing, under); }
                    catch (Exception error) { problem = DirectoryProblem(error, "Couldn't download that mod: "); }
                    try { Back(delegate { Finish(listing, file, problem); }); }
                    finally { if (IsDisposed && file != null) ModDirectory.Clean(file); }
                });
            }

            private void Finish(ModListing listing, string file, string problem)
            {
                installing = false;
                if (file == null)
                {
                    Fill();
                    Says(problem, true);
                    return;
                }
                bool done;
                try { done = Frame.ImportMod(file); }
                finally { ModDirectory.Clean(file); }
                Fill();
                Says(Frame.modStatus.Text, !done && Frame.modStatus.ForeColor == Rose);
                if (!done) return;
                Run(delegate
                {
                    try { ModDirectory.CountDownload(listing.Folder); }
                    catch (Exception) { }
                });
            }

            private void Rate(int card, int picked)
            {
                ModListing listing = At(card);
                if (listing == null) return;
                Says("Saving your rating...", false);
                Run(delegate
                {
                    ModStats saved = null;
                    string problem = null;
                    try { saved = ModDirectory.Rate(listing.Folder, picked); }
                    catch (Exception error) { problem = DirectoryProblem(error, "Couldn't save your rating: "); }
                    Back(delegate
                    {
                        if (saved == null) { Says(problem, true); return; }
                        listing.Downloads = saved.Downloads;
                        listing.Rating = saved.Rating;
                        listing.Votes = saved.Votes;
                        Fill();
                        Says("You gave " + listing.Title + " " + picked + (picked == 1 ? " star." : " stars."), false);
                    });
                });
            }

            private void View(int card)
            {
                ModListing listing = At(card);
                if (listing == null) return;
                try { using (Process.Start(ModDirectory.Page + "/tree/HEAD/mods/" + listing.Folder)) { } }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                    Says("Couldn't open that page: " + error.Message, true);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    ticket++;
                    foreach (Image picture in pictures.Values) picture.Dispose();
                    pictures.Clear();
                }
                base.Dispose(disposing);
            }

            internal string StatusShown { get { return status.Text; } }
            internal string EmptyShown { get { return empty.Visible ? empty.Text : null; } }
            internal string PageShown { get { return page.Visible ? page.Text : null; } }
            internal int ShownCount { get { return shown.Count; } }
            internal string TitleOf(int card) { return titles[card].Visible ? titles[card].Text : null; }
            internal string MetaOf(int card) { return metas[card].Text; }
            internal string StatsOf(int card) { return stats[card].Visible ? stats[card].Text + " \u00B7 " + counts[card].Text : null; }
            internal string InstallTextOf(int card) { return installs[card].Text; }
            internal bool PictureOn(int card) { return previews[card].Image != null; }
            internal bool CanRateCard(int card) { return stars[card].Visible && stars[card].CanRate; }
            internal bool Across3 { get { return titles[0].Top == titles[2].Top && titles[0].Left < titles[1].Left && titles[1].Left < titles[2].Left && titles[3].Top > titles[0].Top; } }
            internal bool Filtered(int kind) { return filters[kind].Accent == Cyan; }
            internal void PressNext() { next.PerformClick(); }
            internal void PressFilter(int kind) { filters[kind].PerformClick(); }
            internal void Type2(string text) { search.Box.Text = text; }
            internal void PressInstall(int card) { Install(card); }
            internal void PressStars(int card, int picked) { stars[card].Pick(picked); }
        }

        private sealed class PublishSheet : ModSheet
        {
            internal const int Wide = 520, Tall = 366;
            private readonly Mod mod;
            private readonly Label status;
            internal readonly string Steps;

            internal PublishSheet(Mainframe owner, float zoom, Mod mod) : base(owner, zoom, "Publish mod", "PUBLISH MOD >", Wide, Tall)
            {
                this.mod = mod;
                Steps = ModDirectory.PublishSteps(mod);
                Words("Share " + mod.File.Name + " in the mods directory with a pull request.", 28, 52, 464, 18, Font, White);
                Words(Steps, 28, 78, 464, 216, Font, Faint);
                Key fork = Make("Fork capri-mods", 28, 298, 150, 26, delegate { Open(ModDirectory.Page + "/fork", null); });
                fork.Accent = Cyan;
                fork.Loud = true;
                Make("Show my mod file", 186, 298, 150, 26, delegate { Open("explorer.exe", "/select,\"" + mod.Path + "\""); });
                status = Words("", 28, 330, 464, 26, Small(), Rose);
            }

            private void Open(string what, string how)
            {
                try { using (how == null ? Process.Start(what) : Process.Start(what, how)) { } }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                    status.Text = "Couldn't open that: " + error.Message;
                }
            }

            internal string ModPath { get { return mod.Path; } }
        }

        private void CheckDirectory(List<string> checks, string output)
        {
            FormWindowState wasState = WindowState;
            Restore();
            Mods mods = capri.ModList;
            Func<string, long, byte[]> wasGet = ModDirectory.Get;
            Func<string, string, byte[]> wasPost = ModDirectory.Post;
            string wasFolder = ModDirectory.PreviewFolder;
            Func<ModFile, string, bool> wasAsk = ModAsk;
            string under = Path.Combine(output, "directory-downloads");
            ModDirectory.PreviewFolder = Path.Combine(output, "directory-previews");
            if (Directory.Exists(ModDirectory.PreviewFolder)) Directory.Delete(ModDirectory.PreviewFolder, true);
            ModsSheet sheet = null;
            try
            {
                if (ModDirectory.BlobSha(Encoding.ASCII.GetBytes("test content\n")) != "d670460b4b4aece5915caf5c68d12f560a9fe3e4") throw new Exception("A file's git fingerprint came out wrong.");
                byte[] settings = Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"far-names\",\"name\":\"Far Names\",\"wake_names\":[\"Nova\"]}");
                byte[] code = Encoding.UTF8.GetBytes("// capri-mod: 1\n// id: directory-code\n// name: Directory Code\n\npublic class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }");
                byte[] picture;
                using (Bitmap made = new Bitmap(320, 180))
                using (Graphics paint = Graphics.FromImage(made))
                using (MemoryStream held = new MemoryStream())
                {
                    paint.Clear(Color.FromArgb(40, 150, 170));
                    using (Brush mark = new SolidBrush(Color.FromArgb(240, 60, 90))) paint.FillEllipse(mark, 100, 30, 120, 120);
                    made.Save(held, ImageFormat.Png);
                    picture = held.ToArray();
                }
                string settingsSha = ModDirectory.BlobSha(settings), codeSha = ModDirectory.BlobSha(code), pictureSha = ModDirectory.BlobSha(picture);
                string same = "\"type\":\"blob\",\"size\":10,\"sha\":\"" + settingsSha + "\"";
                string tree = "{\"tree\":[{\"path\":\"README.md\"," + same + "},{\"path\":\"mods\",\"type\":\"tree\",\"sha\":\"" + settingsSha + "\"}"
                    + ",{\"path\":\"mods/far-names/far-names.capri-mod\",\"type\":\"blob\",\"size\":" + settings.Length + ",\"sha\":\"" + settingsSha + "\"}"
                    + ",{\"path\":\"mods/far-names/preview.png\",\"type\":\"blob\",\"size\":" + picture.Length + ",\"sha\":\"" + pictureSha + "\"}"
                    + ",{\"path\":\"mods/directory-code/directory-code.capri-code\",\"type\":\"blob\",\"size\":" + code.Length + ",\"sha\":\"" + codeSha + "\"}"
                    + ",{\"path\":\"mods/directory-code/preview.png\",\"type\":\"blob\",\"size\":999999999,\"sha\":\"" + pictureSha + "\"}"
                    + ",{\"path\":\"mods/no-mod/preview.png\"," + same + "},{\"path\":\"mods/../../evil/evil.capri-mod\"," + same + "},{\"path\":\"mods/deep/er/evil.capri-mod\"," + same + "},{\"path\":\"mods/a/evil.exe\"," + same + "},{\"path\":\"mods/a/..evil.capri-mod\"," + same + "}"
                    + ",{\"path\":\"mods/huge/huge.capri-skin\",\"type\":\"blob\",\"size\":999999999999,\"sha\":\"" + settingsSha + "\"},{\"path\":\"mods/badsha/badsha.capri-mod\",\"type\":\"blob\",\"size\":10,\"sha\":\"../../x\"}]}";
                string statsJson = "{\"mods\":[{\"folder\":\"far-names\",\"downloads\":1234,\"rating\":4.5,\"votes\":8},{\"folder\":\"gone\",\"downloads\":9,\"rating\":1,\"votes\":1}]}";
                bool wrongBytes = false, wrongPicture = false, statsDown = false;
                int pictureGets = 0;
                List<string> posts = new List<string>();
                ModDirectory.Get = delegate(string url, long most)
                {
                    if (url.Contains("/trees/")) return Encoding.UTF8.GetBytes(tree);
                    if (url.EndsWith("/preview.png", StringComparison.Ordinal)) { pictureGets++; return wrongPicture ? settings : picture; }
                    if (url == ModDirectory.StatsPage) { if (statsDown) throw new System.Net.WebException("stand-in"); return Encoding.UTF8.GetBytes(statsJson); }
                    if (wrongBytes) return Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"far-names\",\"name\":\"Far Names\",\"wake_names\":[\"Evil\"]}");
                    return url.EndsWith(settingsSha, StringComparison.Ordinal) ? settings : code;
                };
                ModDirectory.Post = delegate(string url, string body)
                {
                    posts.Add(url + " " + body);
                    return Encoding.UTF8.GetBytes("{\"folder\":\"far-names\",\"downloads\":1235,\"rating\":4.25,\"votes\":9}");
                };
                List<ModListing> found = ModDirectory.Browse();
                if (found.Count != 2 || found[0].Folder != "directory-code" || found[1].Folder != "far-names") throw new Exception("The directory listing let in the wrong files: " + found.Count);
                if (found[1].PreviewSha != pictureSha || found[0].PreviewSha != null || found[1].Title != "Far Names") throw new Exception("The directory listing read previews or titles wrong.");
                checks.Add("PASS mods directory: only mods/<folder>/<mod file> is listed with its preview.png; other files, paths out of the folder, huge files and bad fingerprints are not");
                wrongPicture = true;
                bool refused = false;
                try { ModDirectory.Preview(found[1], ModDirectory.PreviewFolder); }
                catch (ModException) { refused = true; }
                wrongPicture = false;
                if (!refused) throw new Exception("A preview that didn't match its listing was kept.");
                pictureGets = 0;
                byte[] loaded = ModDirectory.Preview(found[1], ModDirectory.PreviewFolder);
                ModDirectory.Preview(found[1], ModDirectory.PreviewFolder);
                if (pictureGets != 1 || loaded.Length != picture.Length) throw new Exception("A preview wasn't saved for next time: " + pictureGets);
                byte[] giant = (byte[])picture.Clone();
                giant[16] = 1;
                if (ModDirectory.Picture(giant) != null || ModDirectory.Picture(settings) != null) throw new Exception("A giant or broken picture was drawn.");
                checks.Add("PASS mods window previews: a preview has to match its listing, is kept so it isn't downloaded twice, and giant or broken pictures aren't drawn");
                ShowMods(true);
                sheet = new ModsSheet(this, 1f);
                sheet.Inline = true;
                sheet.Show(this);
                sheet.LoadMods();
                if (sheet.ShownCount != 2 || sheet.TitleOf(1) != "Far Names" || sheet.TitleOf(2) != null || !sheet.PictureOn(1) || sheet.PictureOn(0) || !sheet.Across3) throw new Exception("The mods window didn't show its cards 3 across.");
                if (sheet.StatsOf(1) != "4.5 (8) \u00B7 1.2K downloads" || sheet.StatsOf(0) != "No ratings \u00B7 0 downloads" || sheet.CanRateCard(1)) throw new Exception("The mods window's numbers were wrong: " + sheet.StatsOf(1) + " / " + sheet.StatsOf(0));
                string crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("The mods window doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mods-window.png"));
                checks.Add("PASS mods window: cards go 3 across with a preview, title, downloads and rating, and a mod with no preview says so");
                wrongBytes = true;
                refused = false;
                try { ModDirectory.Download(found[1], under); }
                catch (ModException) { refused = true; }
                wrongBytes = false;
                if (!refused || mods.All.Count != 0) throw new Exception("A download that didn't match its listing was kept.");
                posts.Clear();
                sheet.PressInstall(1);
                if (mods.All.Count != 1 || mods.All[0].File.Id != "far-names" || !Hands.HeardName("hey nova")) throw new Exception("A mod from the directory did not install: " + sheet.StatusShown);
                if (posts.Count != 1 || !posts[0].EndsWith("/mods/download {\"folder\":\"far-names\"}", StringComparison.Ordinal)) throw new Exception("An install wasn't counted once: " + string.Join(" | ", posts));
                if (Directory.Exists(under) && Directory.GetDirectories(under).Length != 0) throw new Exception("The download was left behind.");
                if (!sheet.MetaOf(1).EndsWith("Installed.", StringComparison.Ordinal) || sheet.InstallTextOf(1) != "Reinstall" || !sheet.CanRateCard(1) || sheet.CanRateCard(0)) throw new Exception("An installed directory mod wasn't marked, or couldn't be rated.");
                checks.Add("PASS mods directory: a matching download installs through the normal import and is counted once; one that doesn't match is refused");
                posts.Clear();
                sheet.PressStars(1, 4);
                if (posts.Count != 1 || !posts[0].EndsWith("/mods/rate {\"folder\":\"far-names\",\"stars\":4}", StringComparison.Ordinal) || sheet.StatsOf(1) != "4.3 (9) \u00B7 1.2K downloads" || sheet.StatusShown != "You gave Far Names 4 stars.") throw new Exception("A rating wasn't sent or shown: " + sheet.StatsOf(1) + " / " + sheet.StatusShown);
                posts.Clear();
                sheet.PressStars(0, 5);
                if (posts.Count != 0) throw new Exception("A mod that isn't installed was rated.");
                checks.Add("PASS mods window ratings: only an installed mod can be rated, and the new average shows");
                ShowMods(true);
                FillMods();
                if (!modPublishes[0].Visible || modPublishes[1].Visible) throw new Exception("Publish mod wasn't only on imported mods.");
                using (PublishSheet publish = new PublishSheet(this, 1f, mods.All[0]))
                {
                    publish.Show(this);
                    string publishCrowded = publish.Crowded();
                    if (publishCrowded != null) throw new Exception("Publish mod doesn't fit: " + publishCrowded);
                    if (!publish.Steps.Contains("mods/far-names/") || !publish.Steps.Contains("far-names" + Mods.Extension) || !publish.Steps.Contains("preview.png") || !publish.Steps.Contains("README.md") || !publish.Steps.Contains("pull request")) throw new Exception("Publish mod's steps were wrong: " + publish.Steps);
                    publish.Picture(Path.Combine(output, "publish-mod.png"));
                    publish.Close();
                }
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(Path.Combine(output, "mods-page-publish.png"), ImageFormat.Png);
                }
                checks.Add("PASS Publish mod: it's on imported mods and explains the fork, folder, README, preview and pull request");
                capri.ModList.Remove(mods.All[0]);
                int asked = 0;
                ModAsk = delegate { asked++; return false; };
                posts.Clear();
                sheet.PressInstall(0);
                if (asked != 1 || mods.All.Count != 0 || posts.Count != 0) throw new Exception("A code mod from the directory did not ask, stayed after a no, or was counted: " + sheet.StatusShown);
                ModAsk = wasAsk;
                checks.Add("PASS mods directory: a code mod still asks, and a no leaves nothing and isn't counted");
                statsDown = true;
                sheet.LoadMods();
                if (sheet.ShownCount != 2 || sheet.StatsOf(1) != null || sheet.StatusShown != "") throw new Exception("Losing the worker wasn't quiet: " + sheet.StatusShown);
                statsDown = false;
                checks.Add("PASS mods window: with the worker down the mods still show, without numbers or an error");
                StringBuilder many = new StringBuilder("{\"tree\":[");
                foreach (string name in new[] { "big-hat.capri-skin", "blue-hair.capri-mod", "dance-party.capri-code", "red-hair.capri-mod", "red-hat.capri-skin", "tall-hat.capri-skin", "wide-hat.capri-skin", "zoom.capri-mod" })
                    many.Append("{\"path\":\"mods/" + Path.GetFileNameWithoutExtension(name) + "/" + name + "\",\"type\":\"blob\",\"size\":2048,\"sha\":\"" + settingsSha + "\"},");
                string smaller = tree;
                tree = many.ToString().TrimEnd(',') + "]}";
                sheet.LoadMods();
                if (sheet.PageShown != "Page 1 of 2" || sheet.TitleOf(5) != "Tall Hat") throw new Exception("The mods window's first page was wrong.");
                sheet.PressNext();
                if (sheet.PageShown != "Page 2 of 2" || sheet.TitleOf(0) != "Wide Hat" || sheet.TitleOf(2) != null) throw new Exception("The mods window's second page was wrong.");
                sheet.Type2("HAIR red");
                if (sheet.ShownCount != 1 || sheet.TitleOf(0) != "Red Hair" || sheet.PageShown != null) throw new Exception("Search didn't find the mod with every word.");
                sheet.Type2("");
                sheet.PressFilter(3);
                if (sheet.ShownCount != 4 || !sheet.Filtered(3) || sheet.Filtered(0)) throw new Exception("The Skins filter was wrong.");
                sheet.Type2("red");
                if (sheet.ShownCount != 1 || sheet.TitleOf(0) != "Red Hat") throw new Exception("Search and a filter didn't work together.");
                sheet.PressFilter(2);
                if (sheet.ShownCount != 0 || sheet.EmptyShown != "No mods match that.") throw new Exception("No matches wasn't said.");
                sheet.Type2("");
                sheet.PressFilter(0);
                crowded = sheet.Crowded();
                if (crowded != null) throw new Exception("A full mods window doesn't fit: " + crowded);
                sheet.Picture(Path.Combine(output, "mods-window-full.png"));
                checks.Add("PASS mods window: search needs every word in any case, filters go by kind, both work together and with pages, and no matches is said");
                tree = "{\"tree\":[]}";
                sheet.LoadMods();
                if (sheet.EmptyShown != "Nothing to show from the mods directory." || sheet.StatusShown != "No mods in the directory yet.") throw new Exception("An empty directory wasn't said.");
                tree = "{nope";
                sheet.LoadMods();
                if (sheet.EmptyShown == null || sheet.StatusShown != "The mods directory sent something I can't read.") throw new Exception("Broken directory data wasn't said: " + sheet.StatusShown);
                tree = smaller;
                sheet.Close();
                foreach (float size in new[] { 1.5f, 2f })
                    using (ModsSheet sized = new ModsSheet(this, size))
                    {
                        sized.Inline = true;
                        sized.Show(this);
                        sized.LoadMods();
                        string problem = sized.Crowded();
                        sized.Close();
                        if (problem != null) throw new Exception("The mods window doesn't fit at " + size + ": " + problem);
                    }
                ShowMods(false);
                checks.Add("PASS mods window: empty and broken directories are said, and it fits at 100%, 150% and 200%");
                checks.Add("LIMIT mods window: these use stand-in downloads and a stand-in worker, so real GitHub previews and real downloads and ratings need a live check once the worker is deployed");
            }
            finally
            {
                if (sheet != null && !sheet.IsDisposed) sheet.Close();
                ModDirectory.Get = wasGet;
                ModDirectory.Post = wasPost;
                ModDirectory.PreviewFolder = wasFolder;
                ModAsk = wasAsk;
                WindowState = wasState;
            }
        }
    }
}
