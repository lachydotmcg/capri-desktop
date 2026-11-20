using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private GifSheet gifSheet;
        private Key gifsKey;
        internal Func<IWin32Window, string[]> PickGifs = PickGifFiles;

        private static string[] PickGifFiles(IWin32Window owner)
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Add GIFs for Capri";
                dialog.Filter = "GIFs (*.gif)|*.gif";
                dialog.Multiselect = true;
                dialog.CheckFileExists = true;
                dialog.RestoreDirectory = true;
                return dialog.ShowDialog(owner) == DialogResult.OK ? dialog.FileNames : new string[0];
            }
        }

        private void OpenGifs()
        {
            if (gifSheet != null && !gifSheet.IsDisposed && gifSheet.Zoom != zoom) gifSheet.Close();
            if (gifSheet == null || gifSheet.IsDisposed) gifSheet = new GifSheet(this, zoom);
            if (!gifSheet.Visible) gifSheet.Show(this);
            gifSheet.Activate();
        }

        internal void ShowGifs() { OpenGifs(); }

        internal void CloseGifs() { if (gifSheet != null && !gifSheet.IsDisposed) gifSheet.Close(); }

        internal string GifsShown { get { return gifSheet == null || gifSheet.IsDisposed || !gifSheet.Visible ? null : gifSheet.Snapshot; } }

        internal Button GifsKey { get { return gifsKey; } }

        internal string HabitsPicture(string path)
        {
            List<Control> keys = new List<Control>();
            foreach (Control control in Controls)
                if (control is Key && (control.Text == "Obsidian vault" || control == memoriesKey || control == gifsKey)) keys.Add(control);
            if (keys.Count != 3) return "HABITS has " + keys.Count + " of its three keys.";
            Rectangle row = keys[0].Bounds;
            foreach (Control key in keys) row = Rectangle.Union(row, key.Bounds);
            for (int i = 0; i < keys.Count; i++)
                for (int j = i + 1; j < keys.Count; j++)
                    if (keys[i].Bounds.IntersectsWith(keys[j].Bounds)) return "HABITS keys overlap: " + keys[i].Text + " / " + keys[j].Text;
            int pad = Scaled(8);
            using (Bitmap picture = new Bitmap(row.Width + pad * 2, row.Height + pad * 2))
            {
                using (Graphics graphics = Graphics.FromImage(picture)) graphics.Clear(Ground);
                foreach (Control key in keys) key.DrawToBitmap(picture, new Rectangle(key.Left - row.Left + pad, key.Top - row.Top + pad, key.Width, key.Height));
                picture.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
            return null;
        }

        internal string GifsAt(float zoom, string picture, string[] adding)
        {
            using (GifSheet drawn = new GifSheet(this, zoom))
            {
                drawn.Inline = true;
                drawn.Show(this);
                Func<IWin32Window, string[]> pickWas = PickGifs;
                try
                {
                    string size = " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoom + ")";
                    string problem = drawn.Drive(picture, adding);
                    return problem == null ? null : problem + size;
                }
                finally
                {
                    PickGifs = pickWas;
                    drawn.Close();
                }
            }
        }

        private sealed class GifSheet : ModSheet
        {
            internal const int Wide = 560, Tall = 512, ListWide = 496, ListTall = 330, NameWide = 280, RenameAt = 300, RenameWide = 84, RemoveAt = 392, RemoveWide = 96;
            private readonly Key addKey;
            private readonly Label count, notice;
            private readonly MemoryPlate viewport, content;
            private readonly MemoryRail rail;
            private readonly System.Windows.Forms.Timer noticeTimer = new System.Windows.Forms.Timer(), armTimer = new System.Windows.Forms.Timer();
            private readonly List<Font> fonts = new List<Font>();
            private List<string> names = new List<string>();
            private string editing, draft, armed;
            private TextBox editBox;
            private int ticket, offset, total;
            private float built;
            private bool loaded, redoing;

            internal GifSheet(Mainframe owner, float zoom) : base(owner, zoom, "Her GIFs", "HER GIFS >", Wide, Tall)
            {
                Words("GIFs you save for her. She sends one when you ask, or now and then.", 28, 52, 504, 16, Small(), Faint);
                addKey = Make("Add GIFs…", 28, 78, 120, 26, delegate { Adding(); });
                addKey.AccessibleName = "Add GIFs: pick .gif files for her to send";
                count = Words("", 160, 82, 372, 18, Small(), Faint);
                viewport = new MemoryPlate();
                viewport.Bounds = Box(28, 114, ListWide, ListTall);
                Controls.Add(viewport);
                content = new MemoryPlate();
                viewport.Controls.Add(content);
                rail = new MemoryRail();
                rail.Bounds = Box(530, 114, 6, ListTall);
                rail.AccessibleName = "Scroll her GIFs";
                rail.Moved += delegate(int to) { ScrollTo(to); };
                Controls.Add(rail);
                notice = Words("", 28, 452, 504, 22, Small(), Cyan);
                notice.TextAlign = ContentAlignment.MiddleLeft;
                Words("Up to " + GifShelf.Most + " GIFs, each under 8 MB and " + GifShelf.MostFrames + " frames.", 28, 480, 504, 16, Small(), Faint);
                noticeTimer.Interval = 8000;
                noticeTimer.Tick += delegate { Says("", false); };
                armTimer.Interval = 6000;
                armTimer.Tick += delegate { Disarm(); Redo(); };
                viewport.MouseWheel += Wheel;
                content.MouseWheel += Wheel;
                MouseWheel += Wheel;
                FormClosed += delegate { noticeTimer.Stop(); armTimer.Stop(); };
                Disposed += delegate
                {
                    noticeTimer.Dispose();
                    armTimer.Dispose();
                    foreach (Font font in fonts) font.Dispose();
                    fonts.Clear();
                };
                Frame.Fit(this);
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Reload();
            }

            private int P(int design) { return (int)Math.Round(design * built); }

            private Font Face(float points, bool bold)
            {
                Font font = Type(points * built, bold);
                fonts.Add(font);
                return font;
            }

            private void Wheel(object sender, MouseEventArgs e) { ScrollTo(offset - e.Delta / 120 * P(48)); }

            private void ScrollTo(int to)
            {
                int most = Math.Max(0, total - viewport.Height);
                offset = Math.Max(0, Math.Min(most, to));
                content.Top = -offset;
                rail.Set(total, viewport.Height, offset);
                viewport.Invalidate(true);
            }

            private void Reveal(Control control)
            {
                if (control.Parent != content) return;
                if (control.Top < offset) ScrollTo(control.Top - P(8));
                else if (control.Bottom > offset + viewport.Height) ScrollTo(control.Bottom - viewport.Height + P(8));
            }

            internal void Reload()
            {
                int mine = ++ticket;
                Run(delegate
                {
                    List<string> got = GifShelf.Names();
                    Back(delegate
                    {
                        if (mine != ticket) return;
                        names = got;
                        loaded = true;
                        if (editing != null && !names.Contains(editing)) editing = null;
                        if (armed != null && !names.Contains(armed)) Disarm();
                        count.Text = names.Count + " of " + GifShelf.Most + " GIFs";
                        Rebuild();
                    });
                });
            }

            private void Redo()
            {
                if (Inline) { Rebuild(); return; }
                if (redoing) return;
                redoing = true;
                Back(delegate { redoing = false; Rebuild(); });
            }

            private void Says(string text, bool problem)
            {
                noticeTimer.Stop();
                notice.Text = text ?? "";
                notice.ForeColor = problem ? Rose : Cyan;
                if (!string.IsNullOrEmpty(text)) noticeTimer.Start();
            }

            private void Disarm()
            {
                armTimer.Stop();
                armed = null;
            }

            private void Arm(string name)
            {
                armed = name;
                armTimer.Stop();
                armTimer.Start();
            }

            private void Adding()
            {
                string[] files = Frame.PickGifs(this) ?? new string[0];
                if (files.Length == 0) return;
                Disarm();
                Run(delegate
                {
                    int added = 0;
                    string last = null;
                    List<string> problems = new List<string>();
                    foreach (string file in files)
                    {
                        string kept;
                        string problem = GifShelf.Add(file, null, out kept);
                        if (problem == null) { added++; last = kept; }
                        else problems.Add(problem);
                    }
                    Back(delegate
                    {
                        string said = added == 0 ? "" : added == 1 ? "Added \"" + last + "\"." : "Added " + added + " GIFs.";
                        if (problems.Count > 0) said = (said + " " + problems[0] + (problems.Count > 1 ? " " + (problems.Count - 1) + " more couldn't be added." : "")).Trim();
                        Says(said, problems.Count > 0);
                        Reload();
                    });
                });
            }

            private void Begin(string name)
            {
                editing = name;
                draft = null;
                Disarm();
                Redo();
            }

            private void Stop()
            {
                editing = null;
                draft = null;
                Redo();
            }

            private void Save()
            {
                if (editing == null || editBox == null) return;
                string name = editing, to = editBox.Text;
                Run(delegate
                {
                    string kept;
                    string problem = GifShelf.Rename(name, to, out kept);
                    Back(delegate
                    {
                        if (problem == null) { editing = null; draft = null; Says(kept == name ? "Kept \"" + kept + "\"." : "Renamed to \"" + kept + "\".", false); }
                        else Says(problem, true);
                        Reload();
                    });
                });
            }

            private void Removing(string name)
            {
                if (armed != name) { Arm(name); Redo(); return; }
                Disarm();
                Run(delegate
                {
                    string problem = GifShelf.Remove(name);
                    Back(delegate
                    {
                        Says(problem ?? "Removed \"" + name + "\".", problem != null);
                        Reload();
                    });
                });
            }

            private void Clear()
            {
                while (content.Controls.Count > 0)
                {
                    Control gone = content.Controls[0];
                    content.Controls.RemoveAt(0);
                    gone.Dispose();
                }
                foreach (Font font in fonts) font.Dispose();
                fonts.Clear();
                editBox = null;
            }

            private void Rebuild()
            {
                if (!loaded || viewport.Width <= 0) return;
                if (editBox != null && !editBox.IsDisposed) draft = editBox.Text;
                built = viewport.Width / (float)ListWide;
                content.SuspendLayout();
                Clear();
                int y = 0;
                if (names.Count == 0)
                {
                    Label none = Line("No GIFs yet. Add some and she'll send one now and then.", 10, y, 470, Face(9f, false), Faint, 20);
                    y += none.Height;
                }
                foreach (string name in names) Row(name, ref y);
                total = y + P(6);
                content.Width = viewport.Width;
                content.Height = Math.Max(total, viewport.Height);
                content.ResumeLayout(false);
                ScrollTo(offset);
                if (editBox != null)
                {
                    editBox.Focus();
                    editBox.SelectionStart = editBox.TextLength;
                }
            }

            private void Row(string name, ref int y)
            {
                if (name == editing)
                {
                    Entry box = new Entry();
                    box.Bounds = new Rectangle(P(10), y, P(NameWide), P(28));
                    box.Font = Face(9.5f, false);
                    box.Box.MaxLength = GifShelf.Longest;
                    box.Box.Text = draft ?? name;
                    box.Box.AccessibleName = "New name for " + name;
                    box.Box.KeyDown += delegate(object sender, KeyEventArgs e)
                    {
                        if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Save(); }
                    };
                    box.Box.GotFocus += delegate { Reveal(box); };
                    box.Box.MouseWheel += Wheel;
                    content.Controls.Add(box);
                    editBox = box.Box;
                    Tool("Save", RenameAt, y + P(2), RenameWide, Save, "Save the new name", Cyan);
                    Tool("Cancel", RemoveAt, y + P(2), RemoveWide, Stop, "Cancel renaming", Rose);
                    y += P(28 + 8);
                    return;
                }
                string mine = name;
                Short(name, 10, y + P(2), NameWide, Face(9.5f, false), White);
                Tool("Rename", RenameAt, y, RenameWide, delegate { Begin(mine); }, "Rename " + name, Rose);
                Tool(armed == name ? "Click again" : "Remove", RemoveAt, y, RemoveWide, delegate { Removing(mine); }, armed == name ? "Click again to remove " + name : "Remove " + name + ". Press twice to confirm", Hot);
                y += P(24 + 8);
            }

            private Label Line(string text, int x, int y, int width, Font font, Color ink, int least)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = ink;
                label.BackColor = Ground;
                label.UseMnemonic = false;
                int wide = P(width);
                Size measured = TextRenderer.MeasureText(text, font, new Size(wide, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                label.Bounds = new Rectangle(P(x), y, wide, Math.Max(measured.Height + P(4), P(least)));
                label.MouseWheel += Wheel;
                content.Controls.Add(label);
                return label;
            }

            private Label Short(string text, int x, int y, int width, Font font, Color ink)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = ink;
                label.BackColor = Ground;
                label.UseMnemonic = false;
                label.AutoEllipsis = true;
                label.Bounds = new Rectangle(P(x), y, P(width), P(20));
                label.MouseWheel += Wheel;
                content.Controls.Add(label);
                return label;
            }

            private Key Tool(string text, int x, int y, int width, Action click, string spoken, Color accent)
            {
                Key key = new Key();
                key.Text = text;
                key.Font = Face(9f, true);
                key.Accent = accent;
                key.Bounds = new Rectangle(P(x), y, P(width), P(24));
                key.AccessibleName = spoken;
                key.Click += delegate { click(); };
                key.MouseWheel += Wheel;
                key.GotFocus += delegate { Reveal(key); };
                content.Controls.Add(key);
                return key;
            }

            internal string ContentCrowded()
            {
                Rectangle area = new Rectangle(0, 0, content.Width, content.Height);
                List<Control> shown = new List<Control>();
                foreach (Control control in content.Controls) if (control.Visible) shown.Add(control);
                for (int i = 0; i < shown.Count; i++)
                {
                    Control a = shown[i];
                    string name = a.Text.Length > 0 ? a.Text : a.AccessibleName;
                    if (!area.Contains(a.Bounds)) return "pokes out: " + name + " " + a.Bounds + " in " + area;
                    Key key = a as Key;
                    if (key != null && TextRenderer.MeasureText(key.Text, key.Font).Width + (int)Math.Ceiling(12 * built) > key.Width) return "doesn't fit: " + key.Text;
                    for (int j = i + 1; j < shown.Count; j++)
                        if (a.Bounds.IntersectsWith(shown[j].Bounds)) return "overlap: " + name + " / " + (shown[j].Text.Length > 0 ? shown[j].Text : shown[j].AccessibleName);
                }
                return null;
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (viewport != null && loaded && viewport.Width > 0 && Math.Abs(viewport.Width / (float)ListWide - built) > 0.02f) Redo();
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && (editing != null || armed != null))
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    Disarm();
                    Stop();
                    return;
                }
                if (!(ActiveControl is TextBox) && !(ActiveControl is Entry))
                {
                    int page = viewport.Height - P(40);
                    if (e.KeyCode == Keys.PageDown) { ScrollTo(offset + page); e.Handled = true; return; }
                    if (e.KeyCode == Keys.PageUp) { ScrollTo(offset - page); e.Handled = true; return; }
                }
                base.OnKeyDown(e);
            }

            internal string Snapshot
            {
                get
                {
                    StringBuilder all = new StringBuilder(count.Text).Append(" | ");
                    foreach (Control control in content.Controls)
                    {
                        Label label = control as Label;
                        if (label != null && label.Text.Length > 0) all.Append(label.Text).Append(" | ");
                        Key key = control as Key;
                        if (key != null) all.Append("[").Append(key.Text).Append("] ");
                    }
                    return all.ToString();
                }
            }

            private string Showing(string wanted)
            {
                string all = Snapshot;
                return all.IndexOf(wanted, StringComparison.Ordinal) >= 0 ? null : "\"" + wanted + "\" isn't shown in " + all;
            }

            private string Fits(string where)
            {
                string wrong = Crowded() ?? ContentCrowded();
                return wrong == null ? null : where + ": " + wrong;
            }

            private static bool Kept(string name) { return GifShelf.PathOf(name) != null; }

            internal string Drive(string picture, string[] adding)
            {
                string wrong = Fits("list");
                if (wrong != null) return wrong;
                foreach (string wanted in new[] { "2 of " + GifShelf.Most + " GIFs", "party", "wave", "[Rename]", "[Remove]" })
                {
                    wrong = Showing(wanted);
                    if (wrong != null) return wrong;
                }
                if (Snapshot.IndexOf("party", StringComparison.Ordinal) > Snapshot.IndexOf("wave", StringComparison.Ordinal)) return "Her GIFs aren't in order: " + Snapshot;
                if (picture != null) Picture(picture);

                Frame.PickGifs = delegate { return adding; };
                Adding();
                if (!Kept("cat dance") || notice.Text.IndexOf("Added", StringComparison.Ordinal) < 0 || notice.Text.IndexOf("isn't a GIF", StringComparison.Ordinal) < 0 || notice.ForeColor != Rose)
                    return "Add GIFs didn't add the GIF and refuse the rest: " + notice.Text;
                wrong = Showing("cat dance") ?? Showing("3 of " + GifShelf.Most + " GIFs") ?? Fits("added");
                if (wrong != null) return wrong;

                Begin("cat dance");
                if (editBox == null || editBox.Text != "cat dance") return "Rename didn't open a box with its name.";
                wrong = Fits("rename");
                if (wrong != null) return wrong;
                Escaping();
                if (editBox != null || editing != null) return "Esc didn't cancel renaming.";
                Begin("cat dance");
                editBox.Text = "Cat Dance!!";
                Save();
                if (!Kept("Cat Dance") || editing != null || GifShelf.Names().IndexOf("Cat Dance") < 0) return "Saving a new name didn't rename the GIF: " + notice.Text + " / " + string.Join(", ", GifShelf.Names().ToArray());
                if (notice.Text != "Renamed to \"Cat Dance\".") return "Renaming didn't say its new name: " + notice.Text;
                wrong = Showing("Cat Dance") ?? Fits("renamed");
                if (wrong != null) return wrong;

                Removing("party");
                if (!Kept("party") || armed != "party") return "The first click on Remove didn't only ask for another.";
                wrong = Showing("[Click again]") ?? Fits("remove confirm");
                if (wrong != null) return wrong;
                Escaping();
                if (armed != null || !Kept("party")) return "Esc didn't drop the remove confirm.";
                Removing("party");
                Removing("party");
                if (Kept("party") || Snapshot.IndexOf("party", StringComparison.Ordinal) >= 0 || notice.Text != "Removed \"party\".") return "The second click didn't remove the GIF: " + notice.Text + " / " + Snapshot;
                return Fits("end");
            }

            private void Escaping() { OnKeyDown(new KeyEventArgs(Keys.Escape)); }
        }
    }
}
