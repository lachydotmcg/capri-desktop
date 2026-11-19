using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private MemorySheet remembered;

        private void OpenMemory()
        {
            if (remembered != null && !remembered.IsDisposed && remembered.Zoom != zoom) remembered.Close();
            if (remembered == null || remembered.IsDisposed) remembered = new MemorySheet(this, zoom);
            if (!remembered.Visible) remembered.Show(this);
            remembered.Activate();
        }

        internal void ShowMemory() { OpenMemory(); }

        internal void CloseMemory() { if (remembered != null && !remembered.IsDisposed) remembered.Close(); }

        internal string MemoryAt(float zoom, string picture)
        {
            using (MemorySheet drawn = new MemorySheet(this, zoom))
            {
                drawn.Inline = true;
                drawn.Show(this);
                try
                {
                    string size = " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoom + ")";
                    string problem = drawn.Drive(picture);
                    return problem == null ? null : problem + size;
                }
                finally { drawn.Close(); }
            }
        }

        private sealed class MemoryRail : Control
        {
            internal event Action<int> Moved;
            private int total, page, offset;
            private bool dragging;
            private int grab;

            internal MemoryRail()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                TabStop = false;
                AccessibleRole = AccessibleRole.ScrollBar;
            }

            internal void Set(int totalHeight, int pageHeight, int at)
            {
                total = totalHeight;
                page = pageHeight;
                offset = at;
                Invalidate();
            }

            private bool Needed { get { return total > page && page > 0; } }

            private int ThumbHeight { get { return Math.Max(Width * 4, (int)((long)Height * page / Math.Max(1, total))); } }

            private int ThumbTop { get { return total <= page ? 0 : (int)((long)(Height - ThumbHeight) * offset / Math.Max(1, total - page)); } }

            protected override void OnPaint(PaintEventArgs e)
            {
                Backdrop(e.Graphics, this, ScaleOf(this));
                if (!Needed) return;
                float s = ScaleOf(this);
                int line = Math.Max(1, (int)(2 * s));
                int left = (Width - line) / 2;
                using (Brush track = new SolidBrush(Dash)) e.Graphics.FillRectangle(track, left, 0, line, Height);
                using (Brush lit = new SolidBrush(Cyan)) e.Graphics.FillRectangle(lit, left, ThumbTop, line, ThumbHeight);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left || !Needed) return;
                int top = ThumbTop;
                if (e.Y >= top && e.Y < top + ThumbHeight) { dragging = true; grab = e.Y - top; return; }
                Go(e.Y - ThumbHeight / 2);
                dragging = true;
                grab = ThumbHeight / 2;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (dragging) Go(e.Y - grab);
            }

            protected override void OnMouseUp(MouseEventArgs e) { dragging = false; }

            private void Go(int thumbTop)
            {
                int room = Math.Max(1, Height - ThumbHeight);
                int to = (int)((long)Math.Max(0, Math.Min(room, thumbTop)) * (total - page) / room);
                if (Moved != null) Moved(to);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084 && !Needed) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        private sealed class MemoryPlate : Panel
        {
            internal MemoryPlate()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
                BackColor = Ground;
            }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ScaleOf(this)); }
        }

        private sealed class MemorySheet : ModSheet
        {
            internal const int Wide = 760, Tall = 692, ListWide = 692, ListTall = 430, Cap = 150, DayCap = 250, EditAt = 542, EditWide = 64, ForgetAt = 614, ForgetWide = 78;
            private readonly Entry adding, searching;
            private readonly Key addKey, textKey, undoKey, wipeKey;
            private readonly Label offNote, notice;
            private readonly MemoryPlate viewport, content;
            private readonly MemoryRail rail;
            private readonly System.Windows.Forms.Timer noticeTimer = new System.Windows.Forms.Timer(), armTimer = new System.Windows.Forms.Timer();
            private readonly Action changed;
            private readonly List<Font> fonts = new List<Font>();
            private List<MemoryBook.Item> items = new List<MemoryBook.Item>();
            private List<MemoryBook.Day> days = new List<MemoryBook.Day>();
            private List<MemoryBook.Said> said;
            private bool keeping = true, loaded, cut, reading, redoing;
            private string editing, draft, armed, undoToken, readDate;
            private TextBox editBox;
            private int ticket, offset, total;
            private float built;

            internal MemorySheet(Mainframe owner, float zoom) : base(owner, zoom, "Her memory", "HER MEMORY >", Wide, Tall)
            {
                Words("What Capri remembers about you. It stays on this PC.", 28, 52, 704, 16, Small(), Faint);
                adding = new Entry();
                adding.Bounds = Box(28, 76, 500, 28);
                adding.Cue = "Tell her something to remember";
                adding.Box.AccessibleName = "Tell her something to remember";
                adding.Box.MaxLength = 200;
                adding.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Adding(); } };
                Controls.Add(adding);
                addKey = Make("Add", 536, 78, 64, 24, delegate { Adding(); });
                addKey.AccessibleName = "Add what you typed to her memory";
                textKey = Make("Edit as text", 608, 78, 124, 24, delegate { Frame.EditNotes(true); });
                textKey.AccessibleName = "Edit her memories as text";
                searching = new Entry();
                searching.Bounds = Box(28, 112, 704, 28);
                searching.Cue = "Search what she remembers";
                searching.Box.AccessibleName = "Search what she remembers";
                searching.Box.TextChanged += delegate { offset = 0; Redo(); };
                Controls.Add(searching);
                offNote = Words("", 28, 146, 704, 16, Small(), Rose);
                viewport = new MemoryPlate();
                viewport.Bounds = Box(28, 168, ListWide, ListTall);
                Controls.Add(viewport);
                content = new MemoryPlate();
                viewport.Controls.Add(content);
                rail = new MemoryRail();
                rail.Bounds = Box(726, 168, 6, ListTall);
                rail.AccessibleName = "Scroll her memories";
                rail.Moved += delegate(int to) { ScrollTo(to); };
                Controls.Add(rail);
                notice = Words("", 28, 606, 590, 22, Small(), Cyan);
                notice.TextAlign = ContentAlignment.MiddleLeft;
                undoKey = Make("Undo", 626, 606, 106, 22, delegate { UndoLast(); });
                undoKey.AccessibleName = "Undo the last change to her memory";
                undoKey.Visible = false;
                wipeKey = Make("Forget everything about me", 28, 640, 260, 24, delegate { ForgetAll(); });
                wipeKey.Accent = Hot;
                wipeKey.AccessibleName = "Forget everything she remembers about you. Press twice to confirm";
                noticeTimer.Interval = 8000;
                noticeTimer.Tick += delegate { Quiet(); };
                armTimer.Interval = 6000;
                armTimer.Tick += delegate { Disarm(); Redo(); };
                viewport.MouseWheel += Wheel;
                content.MouseWheel += Wheel;
                MouseWheel += Wheel;
                changed = delegate { Back(Reload); };
                MemoryBook.Changed += changed;
                FormClosed += delegate { MemoryBook.Changed -= changed; noticeTimer.Stop(); armTimer.Stop(); };
                Disposed += delegate
                {
                    MemoryBook.Changed -= changed;
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
                    List<MemoryBook.Item> got = null;
                    List<MemoryBook.Day> dated = null;
                    bool keep = Brain.Keeping;
                    try
                    {
                        got = MemoryBook.Everything();
                        dated = MemoryBook.DayList();
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    Back(delegate
                    {
                        if (mine != ticket) return;
                        if (got == null) { Says("Couldn't read her memories on this PC.", true, null); return; }
                        items = got;
                        days = dated;
                        keeping = keep;
                        loaded = true;
                        offNote.Text = keeping ? "" : "Remember our chats is off, so she isn't keeping new chat memories.";
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

            private void Says(string text, bool problem, string token)
            {
                noticeTimer.Stop();
                notice.Text = text ?? "";
                notice.ForeColor = problem ? Rose : Cyan;
                undoToken = problem ? null : token;
                undoKey.Visible = undoToken != null;
                if (!string.IsNullOrEmpty(text)) noticeTimer.Start();
            }

            private void Quiet()
            {
                noticeTimer.Stop();
                notice.Text = "";
                undoToken = null;
                undoKey.Visible = false;
            }

            private void Disarm()
            {
                armTimer.Stop();
                armed = null;
                wipeKey.Text = "Forget everything about me";
            }

            private void Arm(string what)
            {
                armed = what;
                armTimer.Stop();
                armTimer.Start();
                wipeKey.Text = what == "all" ? "Click again to forget everything" : "Forget everything about me";
            }

            private void Done(MemoryBook.Result result)
            {
                if (result.Ok) editing = null;
                else if (result.Code == "changed") editing = null;
                Says(result.Say, !result.Ok, result.Undo);
                Reload();
            }

            private void Adding()
            {
                string text = adding.Box.Text.Trim();
                if (text.Length == 0) { Says("What should she remember?", true, null); return; }
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.Add(text);
                    Back(delegate
                    {
                        if (result.Ok) adding.Box.Clear();
                        Done(result);
                    });
                });
            }

            private void Begin(string id)
            {
                editing = id;
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
                string id = editing, text = editBox.Text;
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.Edit(id, text);
                    Back(delegate { Done(result); });
                });
            }

            private void Forgetting(string id)
            {
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.Forget(id);
                    Back(delegate { Done(result); });
                });
            }

            private void UndoLast()
            {
                string token = undoToken;
                if (token == null) return;
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.Undo(token);
                    Back(delegate { Done(result); });
                });
            }

            private void ForgetAll()
            {
                if (armed != "all") { Arm("all"); return; }
                Disarm();
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.Wipe("all");
                    Back(delegate { reading = false; readDate = null; Done(result); });
                });
            }

            private void ForgetGames()
            {
                if (armed != "games") { Arm("games"); Redo(); return; }
                Disarm();
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.Wipe("games");
                    Back(delegate { Done(result); });
                });
            }

            private void OpenDay(string date)
            {
                Disarm();
                readDate = date;
                said = null;
                cut = false;
                reading = false;
                offset = 0;
                Run(delegate
                {
                    bool over;
                    List<MemoryBook.Said> lines = MemoryBook.ReadDay(date, out over);
                    Back(delegate
                    {
                        if (readDate != date) return;
                        said = lines;
                        cut = over;
                        reading = true;
                        offset = 0;
                        Redo();
                    });
                });
                Redo();
            }

            private void Leave()
            {
                Disarm();
                readDate = null;
                said = null;
                reading = false;
                offset = 0;
                Redo();
            }

            private void ForgetDay()
            {
                if (armed != "day") { Arm("day"); Redo(); return; }
                Disarm();
                string date = readDate;
                Run(delegate
                {
                    MemoryBook.Result result = MemoryBook.ForgetDay(date);
                    Back(delegate
                    {
                        if (result.Ok) Leave();
                        Done(result);
                    });
                });
            }

            private static string Pretty(string date, string format)
            {
                DateTime when;
                return DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out when) ? when.ToString(format, CultureInfo.InvariantCulture) : date;
            }

            private bool Matches(string text, string group)
            {
                string word = searching.Box.Text.Trim();
                if (word.Length == 0) return true;
                return text.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0 || (group != null && group.IndexOf(word, StringComparison.CurrentCultureIgnoreCase) >= 0);
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
                if (readDate != null) Reader(ref y); else Lists(ref y);
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

            private Label Section(string title, ref int y)
            {
                Label label = Line(title, 0, y, 400, Face(10f, true), Rose, 22);
                y += label.Height;
                Panel rule = new Panel();
                rule.BackColor = Rule;
                rule.Bounds = new Rectangle(0, y, P(ListWide), Math.Max(1, P(1)));
                content.Controls.Add(rule);
                y += P(8);
                return label;
            }

            private void Empty(ref int y)
            {
                Label label = Line(searching.Box.Text.Trim().Length > 0 ? "Nothing matches." : "Nothing yet.", 10, y, 672, Face(9f, false), Faint, 20);
                y += label.Height + P(8);
            }

            private static string Clip(string text, int most) { return text.Length <= most ? text : text.Substring(0, most) + "..."; }

            private void Row(MemoryBook.Item item, ref int y)
            {
                if (item.Id == editing)
                {
                    Entry box = new Entry();
                    box.Bounds = new Rectangle(P(10), y, P(EditAt - 18), P(28));
                    box.Font = Face(9.5f, false);
                    box.Box.MaxLength = item.Kind == MemoryBook.About ? 200 : MemoryBook.TextMost;
                    box.Box.Text = draft ?? item.Text;
                    box.Box.AccessibleName = "Edit this memory";
                    box.Box.KeyDown += delegate(object sender, KeyEventArgs e)
                    {
                        if (e.KeyCode == Keys.Enter) { e.Handled = true; e.SuppressKeyPress = true; Save(); }
                    };
                    box.Box.GotFocus += delegate { Reveal(box); };
                    box.Box.MouseWheel += Wheel;
                    content.Controls.Add(box);
                    editBox = box.Box;
                    Tool("Save", EditAt, y + P(2), EditWide, Save, "Save this memory", Cyan);
                    Tool("Cancel", ForgetAt, y + P(2), ForgetWide, Stop, "Cancel editing", Rose);
                    y += P(28 + 10);
                    return;
                }
                string id = item.Id;
                Label text = Line(item.Text, 10, y, EditAt - 18, Face(9.5f, false), White, 24);
                Tool("Edit", EditAt, y, EditWide, delegate { Begin(id); }, "Edit memory: " + Clip(item.Text, 60), Rose);
                Tool("Forget", ForgetAt, y, ForgetWide, delegate { Forgetting(id); }, "Forget memory: " + Clip(item.Text, 60), Hot);
                y += Math.Max(text.Height, P(24)) + P(6);
            }

            private static List<string> Groups(List<MemoryBook.Item> list)
            {
                List<string> names = new List<string>();
                foreach (MemoryBook.Item item in list) if (!names.Contains(item.Group)) names.Add(item.Group);
                return names;
            }

            private void Part(List<MemoryBook.Item> list, List<string> order, ref int y, ref int rows)
            {
                foreach (string group in order)
                {
                    bool named = false;
                    foreach (MemoryBook.Item item in list)
                    {
                        if (item.Group != group) continue;
                        if (rows >= Cap) return;
                        if (!named && group.Length > 0)
                        {
                            Label heading = Line(group, 10, y, 672, Face(9f, true), Cyan, 20);
                            y += heading.Height + P(2);
                        }
                        named = true;
                        Row(item, ref y);
                        rows++;
                    }
                }
            }

            private void Lists(ref int y)
            {
                List<MemoryBook.Item> about = new List<MemoryBook.Item>(), chats = new List<MemoryBook.Item>(), games = new List<MemoryBook.Item>();
                foreach (MemoryBook.Item item in items)
                {
                    if (!Matches(item.Text, item.Group)) continue;
                    if (item.Kind == MemoryBook.About) about.Add(item);
                    else if (item.Kind == MemoryBook.Chats) chats.Add(item);
                    else games.Add(item);
                }
                int rows = 0;
                Section("ABOUT YOU", ref y);
                if (about.Count == 0) Empty(ref y);
                else Part(about, Groups(about), ref y, ref rows);
                y += P(10);
                Section("FROM YOUR CHATS", ref y);
                if (chats.Count == 0) Empty(ref y);
                else
                {
                    List<string> order = Groups(chats);
                    order.Reverse();
                    Part(chats, order, ref y, ref rows);
                }
                y += P(10);
                Label gamesTitle = Section("GAMES", ref y);
                if (games.Count == 0) Empty(ref y);
                else
                {
                    int top = gamesTitle.Top;
                    Tool(armed == "games" ? "Click again to forget" : "Forget game notes", ListWide - 190, top - P(1), 190, ForgetGames, "Forget all her game notes. Press twice to confirm", Hot);
                    Part(games, Groups(games), ref y, ref rows);
                }
                if (rows >= Cap)
                {
                    Label more = Line("Showing the first " + Cap + ". Search to narrow it down.", 10, y, 672, Face(9f, false), Faint, 20);
                    y += more.Height + P(6);
                }
                y += P(10);
                Section("CHAT LOGS", ref y);
                int logs = 0;
                foreach (MemoryBook.Day day in days)
                {
                    string shown = Pretty(day.Date, "ddd d MMM yyyy");
                    if (!Matches(shown, day.Date)) continue;
                    string date = day.Date;
                    Tool(shown, 10, y, 170, delegate { OpenDay(date); }, "Read the chat from " + Pretty(day.Date, "dddd d MMMM yyyy") + ", " + day.Lines + (day.Lines == 1 ? " line" : " lines"), Cyan);
                    Short(day.Lines + (day.Lines == 1 ? " line" : " lines"), 190, y + P(2), 200, Face(9f, false), Faint);
                    y += P(30);
                    logs++;
                }
                if (logs == 0) Empty(ref y);
            }

            private void Reader(ref int y)
            {
                Tool("Back", 0, y, 70, Leave, "Back to her memory", Rose);
                Short(Pretty(readDate, "dddd d MMMM yyyy"), 84, y + P(2), 390, Face(10f, true), Rose);
                Tool(armed == "day" ? "Click again to forget" : "Forget this day", ListWide - 190, y, 190, ForgetDay, "Forget this day's chat. Press twice to confirm", Hot);
                y += P(34);
                if (!reading) { Label wait = Line("Loading.", 10, y, 672, Face(9f, false), Faint, 20); y += wait.Height; return; }
                if (said == null || said.Count == 0)
                {
                    Label none = Line(said == null ? "That day isn't there any more." : "No lines kept for that day.", 10, y, 672, Face(9f, false), Faint, 20);
                    y += none.Height;
                    return;
                }
                int first = Math.Max(0, said.Count - DayCap);
                if (cut || first > 0)
                {
                    Label older = Line("Showing the latest lines only.", 10, y, 672, Face(9f, false), Faint, 20);
                    y += older.Height + P(4);
                }
                for (int i = first; i < said.Count; i++)
                {
                    MemoryBook.Said line = said[i];
                    Short(line.At, 10, y, 52, Face(9f, false), Faint);
                    bool action = line.Who.Length == 0;
                    if (!action) Short(line.Who, 64, y, 70, Face(9f, true), string.Equals(line.Who, "You", StringComparison.OrdinalIgnoreCase) ? White : Rose);
                    Label text = Line(line.Text, 138, y, 544, Face(9f, false), action ? Faint : White, 20);
                    y += Math.Max(text.Height, P(20)) + P(4);
                }
            }

            internal string ContentCrowded()
            {
                Rectangle area = new Rectangle(0, 0, content.Width, content.Height);
                List<Control> shown = new List<Control>();
                foreach (Control control in content.Controls) if (control.Visible) shown.Add(control);
                float s = built;
                for (int i = 0; i < shown.Count; i++)
                {
                    Control a = shown[i];
                    string name = a.Text.Length > 0 ? a.Text : a.AccessibleName;
                    if (!area.Contains(a.Bounds)) return "pokes out: " + name + " " + a.Bounds + " in " + area;
                    Key key = a as Key;
                    if (key != null && TextRenderer.MeasureText(key.Text, key.Font).Width + (int)Math.Ceiling(12 * s) > key.Width) return "doesn't fit: " + key.Text;
                    Label label = a as Label;
                    if (label != null && label.Text.Length > 0 && label.AutoEllipsis && TextRenderer.MeasureText(label.Text, label.Font).Width > label.Width) return "cut off: " + label.Text;
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
                    StringBuilder all = new StringBuilder();
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

            private string IdOf(string kind, string text)
            {
                foreach (MemoryBook.Item item in items) if (item.Kind == kind && item.Text == text) return item.Id;
                return null;
            }

            private static string Slurp(string path) { return File.Exists(path) ? File.ReadAllText(path) : ""; }

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

            internal string Drive(string picture)
            {
                string wrong = Fits("list");
                if (wrong != null) return wrong;
                foreach (string wanted in new[] { "ABOUT YOU", "FROM YOUR CHATS", "GAMES", "CHAT LOGS", "Pronouns: she/her", "They like cats", "Thursday 29 October 2026", "They beat Malenia after 40 tries.", "Fortnite", "Elden Ring", "Wed 28 Oct 2026", "3 lines", "1 line"})
                {
                    wrong = Showing(wanted);
                    if (wrong != null) return wrong;
                }
                string all = Snapshot;
                if (all.IndexOf("Thursday 29 October 2026", StringComparison.Ordinal) > all.IndexOf("Wednesday 28 October 2026", StringComparison.Ordinal)) return "Chat memories aren't newest first: " + all;
                if (all.IndexOf("Thu 29 Oct 2026", StringComparison.Ordinal) > all.IndexOf("Wed 28 Oct 2026", StringComparison.Ordinal)) return "Chat logs aren't newest first: " + all;
                if (keeping != Brain.Keeping || (offNote.Text.Length > 0) == keeping) return "The chats-off line is " + offNote.Text + " with chats " + Brain.Keeping;

                string cats = IdOf(MemoryBook.About, "They like cats"), notes = Brain.MemoryPath, notesBefore = Slurp(notes);
                if (cats == null) return "Her notes didn't list 'They like cats'.";
                Begin(cats);
                if (editBox == null) return "Edit didn't open a box.";
                Escaping();
                if (editBox != null || editing != null) return "Esc didn't cancel the edit.";
                Begin(cats);
                editBox.Text = "They love cats and dogs";
                wrong = Fits("edit");
                if (wrong != null) return wrong;
                Save();
                if (Slurp(notes).IndexOf("They love cats and dogs", StringComparison.Ordinal) < 0 || !undoKey.Visible || editing != null) return "Saving an edit didn't change the note or offer Undo: " + notice.Text;
                wrong = Showing("They love cats and dogs") ?? Fits("saved");
                if (wrong != null) return wrong;
                if (picture != null) Picture(picture);
                UndoLast();
                if (Slurp(notes) != notesBefore || Showing("They like cats") != null) return "Undo didn't put the edited note back: " + notice.Text;

                string mic = IdOf(MemoryBook.Chats, "I helped them fix their mic."), memories = Brain.MemoriesPath, memoriesBefore = Slurp(memories);
                if (mic == null) return "Her chat memories didn't list the mic one.";
                Forgetting(mic);
                if (Slurp(memories).IndexOf("fix their mic", StringComparison.Ordinal) >= 0 || !undoKey.Visible || Snapshot.IndexOf("fix their mic", StringComparison.Ordinal) >= 0) return "Forgetting a memory left it or didn't offer Undo: " + notice.Text;
                wrong = Fits("forgot");
                if (wrong != null) return wrong;
                UndoLast();
                if (Slurp(memories) != memoriesBefore || Showing("I helped them fix their mic.") != null) return "Undo didn't bring a forgotten memory back: " + notice.Text;

                adding.Box.Text = "They main Sukuna";
                Adding();
                if (Slurp(notes).IndexOf("They main Sukuna", StringComparison.Ordinal) < 0 || adding.Box.Text.Length != 0 || Showing("They main Sukuna") != null) return "Adding a memory didn't save it: " + notice.Text;
                UndoLast();
                if (Slurp(notes) != notesBefore) return "Undo didn't take an added memory back: " + notice.Text;
                adding.Box.Text = "my password is hunter2";
                Adding();
                if (Slurp(notes).IndexOf("hunter2", StringComparison.Ordinal) >= 0 || undoKey.Visible || notice.Text.Length == 0) return "A password was added through the window.";
                adding.Box.Clear();

                searching.Box.Text = "malenia";
                if (Showing("They beat Malenia") != null || Snapshot.IndexOf("Pronouns", StringComparison.Ordinal) >= 0) return "Search didn't narrow the list: " + Snapshot;
                searching.Box.Text = "";
                if (Showing("Pronouns: she/her") != null) return "Clearing the search didn't bring the list back.";

                OpenDay("2026-10-28");
                wrong = Showing("hey choom") ?? Showing("Back") ?? Showing("Forget this day") ?? Fits("day");
                if (wrong != null) return wrong;
                string dayFile = Path.Combine(Brain.TranscriptPath, "2026-10-28.md");
                ForgetDay();
                if (!File.Exists(dayFile) || armed != "day" || Showing("Click again to forget") != null) return "The first click on Forget this day didn't only ask for another.";
                wrong = Fits("day confirm");
                if (wrong != null) return wrong;
                ForgetDay();
                if (File.Exists(dayFile) || readDate != null || !undoKey.Visible) return "The second click didn't forget the day and go back: " + notice.Text;
                UndoLast();
                if (!File.Exists(dayFile) || Showing("Wed 28 Oct 2026") != null) return "Undo didn't bring the day back: " + notice.Text;
                OpenDay("2026-10-29");
                Leave();
                if (readDate != null || Showing("CHAT LOGS") != null) return "Back didn't return to her memory.";

                ForgetAll();
                if (armed != "all" || Slurp(notes) != notesBefore || wipeKey.Text.IndexOf("again", StringComparison.Ordinal) < 0) return "The first click on Forget everything about me didn't only ask for another.";
                wrong = Fits("wipe confirm");
                if (wrong != null) return wrong;
                Escaping();
                if (armed != null) return "Esc didn't drop the confirm.";
                ForgetAll();
                ForgetAll();
                if (Slurp(notes) == notesBefore || !undoKey.Visible) return "The second click didn't forget everything: " + notice.Text;
                wrong = Fits("emptied");
                if (wrong != null) return wrong;
                UndoLast();
                if (Slurp(notes) != notesBefore || Slurp(memories) != memoriesBefore || Showing("Pronouns: she/her") != null) return "Undo didn't bring everything back: " + notice.Text;
                return Fits("end");
            }

            private void Escaping() { OnKeyDown(new KeyEventArgs(Keys.Escape)); }
        }
    }
}
