using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private Key workspaceKey;
        internal float WorkspaceScale { get { return DeviceScale() * TextScale(); } }

        private void MakeWorkspaceKey()
        {
            workspaceKey = Make("WORKSPACE (beta)", 190, 57, 166, 18, delegate { capri.OpenWorkspace(); });
            workspaceKey.Accent = Cyan;
            workspaceKey.AccessibleName = "Workspace: Claude Code chats in your folders";
            Explain(workspaceKey, "WORKSPACE (beta)", "WorkspaceWindow.cs", null, "Workspace");
        }

        internal string WorkspaceKeyFits()
        {
            foreach (Control control in Controls)
            {
                if (control == workspaceKey || !control.Visible || control.Text.Length == 0) continue;
                if (control.Bounds.IntersectsWith(workspaceKey.Bounds)) return "Workspace key overlaps " + control.Text;
            }
            if (TextRenderer.MeasureText(workspaceKey.Text, workspaceKey.Font).Width + Scaled(12) > workspaceKey.Width) return "Workspace key text is clipped.";
            return null;
        }
    }

    internal sealed partial class Companion
    {
        private Workspace workspace;
        private WorkspaceWindow workspaceWindow;
        private Func<bool> workspaceFront;
        private Action<string, bool> workspaceSaid;

        internal static bool WorkspaceAsked(string text)
        {
            return Regex.IsMatch((text ?? "").Trim(), @"\Aopen (?:your |the )?workspace[.!?]*\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal void OpenWorkspace()
        {
            try
            {
                if (workspace == null)
                {
                    workspace = new Workspace(Path.GetDirectoryName(settingsPath), null);
                    workspace.Finished = delegate(WorkspaceChat chat) { Post(delegate { WorkspaceDone(chat); }); };
                }
                if (workspaceWindow == null || workspaceWindow.IsDisposed)
                {
                    Rectangle area = Screen.FromControl(mainframe).WorkingArea;
                    float fit = Math.Max(0.75f, Math.Min(mainframe.WorkspaceScale, Math.Min(area.Width / 900f, area.Height / 600f)));
                    workspaceWindow = new WorkspaceWindow(workspace, delegate { return Security.Preset == Security.Blackwall; }, fit, true);
                    workspaceWindow.Icon = mainframe.Icon;
                }
                if (!workspaceWindow.Visible) workspaceWindow.Show();
                if (workspaceWindow.WindowState == FormWindowState.Minimized) workspaceWindow.WindowState = FormWindowState.Normal;
                workspaceWindow.Activate();
            }
            catch (Exception error) { Notice.Tell(mainframe, error.Message, "Workspace", true); }
        }

        private void WorkspaceDone(WorkspaceChat chat)
        {
            if (quitting || chat.Deleted || (workspaceFront != null ? workspaceFront() : workspaceWindow != null && workspaceWindow.InFront)) return;
            string line = "Done in " + chat.Title + ".";
            if (workspaceSaid != null) workspaceSaid(line, false);
            else if (LineBusy) ownLines.Enqueue(delegate { WorkspaceDone(chat); });
            else Remark("", line, true);
        }

        private void CloseWorkspace()
        {
            if (workspaceWindow != null && !workspaceWindow.IsDisposed) workspaceWindow.KeepPlace();
            if (workspaceWindow != null) workspaceWindow.Dispose();
            if (workspace != null) workspace.Dispose();
        }
    }

    internal sealed class WorkspaceWindow : Form, LookWindow
    {
        private readonly Workspace workspace;
        private readonly Func<bool> blackwall;
        private readonly bool live;
        private readonly Label heading, modeLabel, modelLabel, next, empty;
        private readonly Plate make, send, stop, undo, folder, shut, smaller, bigger;
        private readonly ListBox chats;
        private readonly TextBox title, input;
        private readonly ComboBox mode, model;
        private readonly Panel conversation;
        private readonly Dictionary<WorkspaceChat, string> drafts = new Dictionary<WorkspaceChat, string>();
        private readonly Dictionary<WorkspaceChat, int> scrolls = new Dictionary<WorkspaceChat, int>();
        private readonly Dictionary<WorkspaceItem, Card> cards = new Dictionary<WorkspaceItem, Card>();
        private readonly List<Card> order = new List<Card>();
        private readonly Action<WorkspaceChat, WorkspaceItem> changed;
        private WorkspaceChat chat;
        private bool filling, arranging, following = true, saving;
        private float zoom;
        private Font mono, small, strong;
        internal float Zoom { get { return zoom; } }
        internal bool InFront { get { return Visible && WindowState != FormWindowState.Minimized && GetForegroundWindow() == Handle; } }

        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

        internal WorkspaceWindow(Workspace workspace, Func<bool> blackwall, float zoom, bool live)
        {
            this.workspace = workspace;
            this.blackwall = blackwall;
            this.live = live;
            this.zoom = zoom;
            SuspendLayout();
            Text = "Capri Workspace (beta)";
            FormBorderStyle = FormBorderStyle.None;
            AutoScaleMode = AutoScaleMode.None;
            StartPosition = FormStartPosition.Manual;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            KeyPreview = true;
            heading = Words("WORKSPACE (beta)", Neon.Rose);
            make = Key("+ New chat", delegate { NewChat(); });
            make.Accent = Neon.Cyan;
            chats = new ListBox { DrawMode = DrawMode.OwnerDrawFixed, BorderStyle = BorderStyle.None, IntegralHeight = false, BackColor = Neon.Band, ForeColor = Neon.White, AccessibleName = "Workspace chats" };
            chats.DrawItem += DrawChat;
            chats.SelectedIndexChanged += delegate { if (!filling) SelectChat(chats.SelectedItem as WorkspaceChat); };
            chats.MouseDown += ChatMenu;
            Controls.Add(chats);
            title = Field(false);
            title.AccessibleName = "Chat title";
            title.Leave += delegate { SaveTitle(); };
            title.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveTitle(); input.Focus(); } };
            folder = Key("", delegate { OpenFolder(); });
            folder.TextAlign = ContentAlignment.MiddleLeft;
            folder.AccessibleName = "Open chat folder in Explorer";
            folder.Accent = Neon.Cyan;
            modeLabel = Words("MODE", Neon.Faint);
            modelLabel = Words("MODEL", Neon.Faint);
            next = Words("Mode and model apply to the next turn.", Neon.Faint);
            mode = Picker(Workspace.Modes, "Permission mode");
            model = Picker(Workspace.Models, "Claude model");
            mode.SelectedIndexChanged += delegate { if (!filling && chat != null) Try(delegate { workspace.Change(chat, null, mode.Text, null); }); };
            model.SelectedIndexChanged += delegate { if (!filling && chat != null) Try(delegate { workspace.Change(chat, null, null, model.Text); }); };
            conversation = new Panel { AutoScroll = true, BackColor = Neon.Ground, AccessibleName = "Conversation" };
            conversation.Scroll += delegate(object sender, ScrollEventArgs e) { if (!arranging && e.ScrollOrientation == ScrollOrientation.VerticalScroll) following = e.NewValue + conversation.ClientSize.Height >= conversation.DisplayRectangle.Height - Z(28); };
            conversation.MouseWheel += delegate { Post(delegate { following = AtBottom(); }); };
            Controls.Add(conversation);
            empty = new Label { Text = "Pick a folder to start a chat.", ForeColor = Neon.Faint, BackColor = Neon.Ground, AutoSize = false, UseMnemonic = false };
            conversation.Controls.Add(empty);
            input = Field(true);
            input.AcceptsReturn = true;
            input.ScrollBars = ScrollBars.Vertical;
            input.AccessibleName = "Message. Enter sends. Shift+Enter adds a line.";
            input.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; Send(); }
            };
            send = Key("Send", delegate { Send(); });
            send.Accent = Neon.Cyan;
            send.Solid = true;
            stop = Key("Stop", delegate { if (chat != null) Try(delegate { workspace.Stop(chat); }); });
            stop.Accent = Neon.Hot;
            undo = Key("Undo last turn", delegate { if (chat != null) Try(delegate { workspace.Undo(chat); SyncItems(); }); });
            shut = Key("", delegate { Close(); });
            shut.Glyph = 2;
            shut.AccessibleName = "Close Workspace";
            smaller = Key("", delegate { WindowState = FormWindowState.Minimized; });
            smaller.Glyph = 1;
            smaller.AccessibleName = "Minimize Workspace";
            bigger = Key("[]", delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; });
            bigger.AccessibleName = "Maximize or restore Workspace";
            SetFonts();
            ClientSize = new Size(Z(1080), Z(740));
            MinimumSize = new Size(Z(900), Z(600));
            MaximizedBounds = Screen.FromControl(this).WorkingArea;
            changed = delegate(WorkspaceChat changedChat, WorkspaceItem item)
            {
                Post(delegate
                {
                    FillChats();
                    if (chat == changedChat) { FillHeader(); SyncItems(); }
                    UpdateKeys();
                });
            };
            workspace.Changed += changed;
            FormClosing += ClosingWindow;
            ResumeLayout(false);
            FillChats();
            List<WorkspaceChat> loaded = workspace.Chats();
            SelectChat(loaded.Count > 0 ? loaded[0] : null);
            if (live) LoadPlace();
            Place();
        }

        private int Z(int value) { return (int)Math.Round(value * zoom); }
        private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

        private Label Words(string text, Color colour)
        {
            Label label = new Label { Text = text, ForeColor = colour, BackColor = Color.Transparent, AutoEllipsis = true, UseMnemonic = false };
            Controls.Add(label);
            return label;
        }

        private TextBox Field(bool multiline)
        {
            TextBox field = new TextBox { Multiline = multiline, BorderStyle = BorderStyle.FixedSingle, BackColor = Neon.Field, ForeColor = Neon.White };
            Controls.Add(field);
            return field;
        }

        private Plate Key(string text, EventHandler click)
        {
            Plate key = new Plate { Text = text };
            key.Click += click;
            Controls.Add(key);
            return key;
        }

        private ComboBox Picker(string[] values, string name)
        {
            ComboBox picker = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Neon.Field, ForeColor = Neon.White, FlatStyle = FlatStyle.Flat, AccessibleName = name };
            picker.Items.AddRange(values);
            Controls.Add(picker);
            return picker;
        }

        private void SetFonts()
        {
            Font old = Font, oldMono = mono, oldSmall = small, oldStrong = strong;
            Font = Neon.Type(14 * zoom, false, GraphicsUnit.Pixel);
            mono = Look.Mono(13 * zoom, FontStyle.Regular, GraphicsUnit.Pixel);
            small = Neon.Type(12 * zoom, false, GraphicsUnit.Pixel);
            strong = Neon.Type(14 * zoom, true, GraphicsUnit.Pixel);
            foreach (Control control in Controls) control.Font = Font;
            heading.Font = Neon.Display(23 * zoom, GraphicsUnit.Pixel);
            modeLabel.Font = modelLabel.Font = next.Font = small;
            send.Font = make.Font = strong;
            chats.ItemHeight = Z(64);
            if (oldMono != null)
            {
                oldMono.Dispose(); oldSmall.Dispose(); oldStrong.Dispose();
                old.Dispose();
            }
        }

        private void Post(Action action)
        {
            try { if (!IsDisposed && IsHandleCreated) BeginInvoke(new MethodInvoker(delegate { if (!IsDisposed) action(); })); }
            catch (InvalidOperationException) { }
        }

        private void Try(Action action)
        {
            try { action(); }
            catch (Exception error)
            {
                if (chat != null && !chat.Deleted)
                {
                    try { workspace.Add(chat, new WorkspaceItem { Kind = "error", Text = error.Message }); }
                    catch (Exception) { Notice.Tell(this, error.Message, "Workspace", true); }
                }
                else Notice.Tell(this, error.Message, "Workspace", true);
            }
        }

        private void NewChat()
        {
            using (FolderBrowserDialog picker = new FolderBrowserDialog { Description = "Choose a folder for this chat", ShowNewFolderButton = true })
            {
                if (chat != null) picker.SelectedPath = chat.Folder;
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                Try(delegate { SelectChat(workspace.NewChat(picker.SelectedPath, blackwall != null && blackwall())); FillChats(); input.Focus(); });
            }
        }

        private void FillChats()
        {
            filling = true;
            chats.BeginUpdate();
            chats.Items.Clear();
            foreach (WorkspaceChat item in workspace.Chats()) chats.Items.Add(item);
            chats.SelectedItem = chat;
            chats.EndUpdate();
            filling = false;
        }

        private void DrawChat(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= chats.Items.Count) return;
            WorkspaceChat item = (WorkspaceChat)chats.Items[e.Index];
            bool picked = (e.State & DrawItemState.Selected) != 0;
            using (Brush fill = new SolidBrush(picked ? Neon.Deep : Neon.Band)) e.Graphics.FillRectangle(fill, e.Bounds);
            Rectangle first = new Rectangle(e.Bounds.Left + Z(12), e.Bounds.Top + Z(10), e.Bounds.Width - Z(36), Z(22));
            TextRenderer.DrawText(e.Graphics, item.Title, strong, first, picked ? Neon.Cyan : Neon.White, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            string name = Path.GetFileName(item.Folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (name.Length == 0) name = item.Folder;
            TextRenderer.DrawText(e.Graphics, name, small, new Rectangle(first.Left, e.Bounds.Top + Z(35), first.Width, Z(18)), Neon.Faint, TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            if (item.Running) using (Brush dot = new SolidBrush(Neon.Cyan)) e.Graphics.FillEllipse(dot, e.Bounds.Right - Z(17), e.Bounds.Top + Z(17), Z(6), Z(6));
            if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
        }

        private void SelectChat(WorkspaceChat selected)
        {
            if (chat == selected && cards.Count > 0) return;
            if (chat != null) { drafts[chat] = input.Text; scrolls[chat] = -conversation.AutoScrollPosition.Y; SaveTitle(); }
            chat = selected;
            ClearCards();
            filling = true;
            string draft;
            input.Text = chat != null && drafts.TryGetValue(chat, out draft) ? draft : "";
            title.Text = chat == null ? "" : chat.Title;
            chats.SelectedItem = chat;
            filling = false;
            following = true;
            FillHeader();
            SyncItems();
            int scroll;
            if (chat != null && scrolls.TryGetValue(chat, out scroll)) { conversation.AutoScrollPosition = new Point(0, scroll); following = AtBottom(); }
            UpdateKeys();
        }

        private void SaveTitle()
        {
            if (chat != null && !filling && title.Text.Trim().Length > 0 && title.Text.Trim() != chat.Title) Try(delegate { workspace.Change(chat, title.Text, null, null); });
        }

        private void FillHeader()
        {
            filling = true;
            if (!title.Focused) title.Text = chat == null ? "" : chat.Title;
            folder.Text = chat == null ? "" : chat.Folder;
            folder.AccessibleDescription = folder.Text;
            mode.SelectedItem = chat == null ? Workspace.Modes[0] : chat.Mode;
            model.SelectedItem = chat == null ? Workspace.Models[0] : chat.Model;
            filling = false;
        }

        private void Send()
        {
            if (chat == null || chat.Running || string.IsNullOrWhiteSpace(input.Text)) return;
            string text = input.Text;
            Try(delegate
            {
                SaveTitle();
                workspace.Send(chat, text, false);
                input.Clear();
                following = true;
                SyncItems();
                UpdateKeys();
                input.Focus();
            });
        }

        private void UpdateKeys()
        {
            bool picked = chat != null, running = picked && chat.Running;
            title.Enabled = folder.Enabled = mode.Enabled = model.Enabled = input.Enabled = picked;
            send.Enabled = picked && !running;
            stop.Visible = running;
            stop.Enabled = running;
            undo.Enabled = picked && !running;
            if (undo.Enabled) try { undo.Enabled = workspace.CanUndo(chat); } catch (Exception) { undo.Enabled = true; }
            chats.Invalidate();
        }

        private void OpenFolder()
        {
            if (chat == null) return;
            Try(delegate { Process.Start(new ProcessStartInfo("explorer.exe", PlanBrain.Quoted(chat.Folder)) { UseShellExecute = true }); });
        }

        private void ChatMenu(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Right) return;
            int index = chats.IndexFromPoint(e.Location);
            if (index < 0) return;
            chats.SelectedIndex = index;
            WorkspaceChat picked = chats.SelectedItem as WorkspaceChat;
            if (picked == null) return;
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new Mainframe.MenuLook() };
            menu.Items.Add("Rename", null, delegate { title.Focus(); title.SelectAll(); });
            menu.Items.Add("Delete chat", null, delegate
            {
                if (MessageBox.Show(this, "Delete this chat? Your files stay in the folder.", "Workspace", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                Try(delegate
                {
                    workspace.Delete(picked);
                    if (chat == picked) { chat = null; ClearCards(); }
                    drafts.Remove(picked); scrolls.Remove(picked);
                    FillChats();
                    List<WorkspaceChat> rest = workspace.Chats();
                    SelectChat(rest.Count > 0 ? rest[0] : null);
                });
            });
            menu.Closed += delegate { menu.Dispose(); };
            menu.Show(chats, e.Location);
        }

        private void ClearCards()
        {
            foreach (Card card in order) { conversation.Controls.Remove(card); card.Dispose(); }
            order.Clear(); cards.Clear();
            conversation.AutoScrollPosition = Point.Empty;
        }

        private void SyncItems()
        {
            if (conversation == null) return;
            bool follow = following;
            int at = -conversation.AutoScrollPosition.Y;
            List<WorkspaceItem> items = new List<WorkspaceItem>();
            if (chat != null) lock (chat.Gate) items.AddRange(chat.Items);
            foreach (WorkspaceItem item in items)
            {
                if (item.Kind == "result")
                {
                    Card tool = order.Find(delegate(Card found) { return found.Item.Kind == "tool" && found.Item.Id == item.Id && found.Item.Turn == item.Turn; });
                    if (tool != null) { tool.Result(item.Text); continue; }
                }
                Card card;
                if (!cards.TryGetValue(item, out card))
                {
                    card = new Card(this, chat, item);
                    cards.Add(item, card);
                    order.Add(card);
                    conversation.Controls.Add(card);
                }
                card.RefreshItem();
            }
            empty.Text = chat == null ? (workspace.Problems.Count > 0 ? string.Join("\n", workspace.Problems.ToArray()) : "Pick a folder to start a chat.") : "Write the first message below.";
            empty.Visible = order.Count == 0;
            Reflow();
            conversation.AutoScrollPosition = new Point(0, follow ? Math.Max(0, conversation.DisplayRectangle.Height - conversation.ClientSize.Height) : at);
            following = follow;
        }

        private bool AtBottom() { return -conversation.AutoScrollPosition.Y + conversation.ClientSize.Height >= conversation.DisplayRectangle.Height - Z(28); }

        private void ScrollText(int delta)
        {
            int lines = SystemInformation.MouseWheelScrollLines;
            int step = lines < 0 ? conversation.ClientSize.Height : Z(22) * Math.Max(1, lines);
            int at = -conversation.AutoScrollPosition.Y - delta * step / 120;
            conversation.AutoScrollPosition = new Point(0, Math.Max(0, at));
            following = AtBottom();
        }

        private void Reflow()
        {
            if (arranging || conversation == null) return;
            arranging = true;
            conversation.SuspendLayout();
            int at = -conversation.AutoScrollPosition.Y;
            conversation.AutoScrollPosition = Point.Empty;
            int y = Z(12), width = Math.Max(Z(240), conversation.ClientSize.Width - Z(24) - SystemInformation.VerticalScrollBarWidth);
            empty.Bounds = new Rectangle(Z(12), Z(18), width, Z(120));
            foreach (Card card in order)
            {
                card.Reflow(width);
                card.Location = new Point(Z(12), y);
                y += card.Height + Z(12);
            }
            conversation.AutoScrollMinSize = new Size(0, order.Count > 0 ? y : 0);
            conversation.ResumeLayout(false);
            conversation.AutoScrollPosition = new Point(0, following ? Math.Max(0, y - conversation.ClientSize.Height) : at);
            arranging = false;
        }

        private void Place()
        {
            if (input == null || arranging) return;
            int w = ClientSize.Width, h = ClientSize.Height, left = Z(246), right = w - Z(20), width = right - left;
            heading.Bounds = Box(20, 15, 450, 30);
            shut.Bounds = new Rectangle(w - Z(48), Z(16), Z(28), Z(24));
            bigger.Bounds = new Rectangle(w - Z(82), Z(16), Z(28), Z(24));
            smaller.Bounds = new Rectangle(w - Z(116), Z(16), Z(28), Z(24));
            make.Bounds = Box(16, 64, 214, 32);
            chats.Bounds = new Rectangle(Z(16), Z(108), Z(214), Math.Max(Z(80), h - Z(128)));
            title.Bounds = new Rectangle(left, Z(64), width, Z(30));
            folder.Bounds = new Rectangle(left, Z(100), width, Z(26));
            modeLabel.Bounds = new Rectangle(left, Z(136), Z(150), Z(16));
            modelLabel.Bounds = new Rectangle(left + Z(190), Z(136), Z(130), Z(16));
            mode.Bounds = new Rectangle(left, Z(155), Z(174), Z(28));
            model.Bounds = new Rectangle(left + Z(190), Z(155), Z(142), Z(28));
            next.Bounds = new Rectangle(left + Z(348), Z(143), Math.Max(0, width - Z(348)), Z(42));
            conversation.Bounds = new Rectangle(left, Z(196), width, Math.Max(Z(120), h - Z(354)));
            input.Bounds = new Rectangle(left, h - Z(146), width, Z(84));
            send.Bounds = new Rectangle(right - Z(88), h - Z(50), Z(88), Z(30));
            stop.Bounds = new Rectangle(right - Z(176), h - Z(50), Z(78), Z(30));
            undo.Bounds = new Rectangle(left, h - Z(50), Z(142), Z(30));
            Reflow();
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Place(); }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, Z(210)); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Neon.Frame(e.Graphics, ClientSize, zoom, Z(14), Z(3), Z(42), Z(20));
            using (Pen rule = new Pen(Neon.Rule, Math.Max(1, zoom))) e.Graphics.DrawLine(rule, Z(16), Z(52), ClientSize.Width - Z(20), Z(52));
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1))
            {
                long value = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(value & 65535), (short)((value >> 16) & 65535)));
                int edge = Z(7), hit = 1;
                if (WindowState != FormWindowState.Maximized)
                {
                    bool l = point.X < edge, r = point.X >= Width - edge, t = point.Y < edge, b = point.Y >= Height - edge;
                    hit = t && l ? 13 : t && r ? 14 : b && l ? 16 : b && r ? 17 : l ? 10 : r ? 11 : t ? 12 : b ? 15 : 1;
                }
                if (hit == 1 && point.Y < Z(52)) hit = 2;
                message.Result = new IntPtr(hit);
            }
            if (message.Msg == 0x02E0 && live)
            {
                float factor = (message.WParam.ToInt64() & 65535) / 96f;
                Rectangle area = Screen.FromControl(this).WorkingArea;
                factor = Math.Max(0.75f, Math.Min(factor, Math.Min(area.Width / 900f, area.Height / 600f)));
                if (factor > 0 && Math.Abs(factor - zoom) > 0.01f)
                {
                    ClearCards();
                    zoom = factor;
                    SetFonts();
                    MinimumSize = new Size(Z(900), Z(600));
                    Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
                    MaximizedBounds = area;
                    SyncItems();
                    Place();
                }
            }
        }

        private void ClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (!live || e.CloseReason != CloseReason.UserClosing) return;
            SaveTitle();
            SavePlace();
            e.Cancel = true;
            Hide();
        }

        private void LoadPlace()
        {
            string file = Path.Combine(workspace.Root, "window.json");
            Rectangle area = Screen.FromControl(this).WorkingArea;
            try
            {
                if (File.Exists(file))
                {
                    Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    Rectangle saved = new Rectangle((int)Workspace.Number(row, "x"), (int)Workspace.Number(row, "y"), (int)Workspace.Number(row, "width"), (int)Workspace.Number(row, "height"));
                    if (saved.Width >= 300 && saved.Height >= 300) { area = Screen.FromRectangle(saved).WorkingArea; Bounds = saved; }
                    if (true.Equals(HuskJson.Get(row, "maximized"))) WindowState = FormWindowState.Maximized;
                }
            }
            catch (Exception) { }
            MinimumSize = new Size(Math.Min(Z(900), area.Width), Math.Min(Z(600), area.Height));
            Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
            Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - Height)));
            MaximizedBounds = area;
        }

        private void SavePlace()
        {
            if (!live || saving) return;
            saving = true;
            try
            {
                Rectangle place = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                Workspace.Atomic(Path.Combine(workspace.Root, "window.json"), HuskJson.Write(HuskJson.Map("x", place.X, "y", place.Y, "width", place.Width, "height", place.Height, "maximized", WindowState == FormWindowState.Maximized)));
            }
            catch (Exception error) { Notice.Tell(this, "Couldn't save the window's place: " + error.Message, "Workspace", true); }
            finally { saving = false; }
        }

        internal void KeepPlace() { SaveTitle(); SavePlace(); }

        internal string Crowded()
        {
            Place();
            List<Control> shown = new List<Control>();
            foreach (Control control in Controls)
            {
                if (!control.Visible) continue;
                if (!ClientRectangle.Contains(control.Bounds)) return "Outside Workspace: " + control.AccessibleName + " " + control.Bounds;
                Plate key = control as Plate;
                if (key != null && key != folder && key.Text.Length > 0 && TextRenderer.MeasureText(key.Text, key.Font).Width + Z(12) > key.Width) return "Workspace key doesn't fit: " + key.Text;
                foreach (Control other in shown) if (control.Bounds.IntersectsWith(other.Bounds)) return "Workspace overlap: " + control.Text + " / " + other.Text;
                shown.Add(control);
            }
            foreach (Card card in order)
            {
                string crowded = card.Crowded();
                if (crowded != null) return crowded;
            }
            return null;
        }

        internal void Picture(string path)
        {
            using (Bitmap shot = new Bitmap(Width, Height)) { DrawToBitmap(shot, new Rectangle(Point.Empty, Size)); shot.Save(path, System.Drawing.Imaging.ImageFormat.Png); }
        }

        internal void ShowChat(WorkspaceChat selected) { SelectChat(selected); }
        internal void RefreshChat() { FillChats(); FillHeader(); SyncItems(); UpdateKeys(); }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                workspace.Changed -= changed;
                ClearCards();
            }
            base.Dispose(disposing);
            if (disposing) { if (mono != null) mono.Dispose(); if (small != null) small.Dispose(); if (strong != null) strong.Dispose(); }
        }

        private sealed class Card : Panel
        {
            internal readonly WorkspaceItem Item;
            private readonly WorkspaceWindow window;
            private readonly WorkspaceChat chat;
            private readonly Label label, reasonLabel;
            private readonly ChatText body;
            private readonly Plate toggle, retry, allow, always, deny;
            private readonly TextBox reason;
            private bool opened;
            private string result = "Waiting for a result.", drawn = null;

            internal Card(WorkspaceWindow window, WorkspaceChat chat, WorkspaceItem item)
            {
                this.window = window; this.chat = chat; Item = item;
                BackColor = item.Kind == "user" ? Neon.Band : Neon.Ground;
                label = new Label { AutoEllipsis = true, UseMnemonic = false, ForeColor = item.Kind == "error" ? Neon.Hot : item.Kind == "user" ? Neon.Cyan : Neon.Rose, Font = window.strong, BackColor = Color.Transparent };
                Controls.Add(label);
                body = new ChatText { ReadOnly = true, DetectUrls = false, BorderStyle = BorderStyle.None, BackColor = BackColor, ForeColor = item.Kind == "error" ? Neon.Hot : item.Kind == "end" ? Neon.Faint : Neon.White,
                    WordWrap = true, ScrollBars = RichTextBoxScrollBars.None, Font = item.Kind == "end" ? window.small : window.Font, ShortcutsEnabled = true, TabStop = true, AccessibleName = "Chat text" };
                body.Wheel = window.ScrollText;
                Controls.Add(body);
                toggle = Button("", delegate { opened = !opened; window.Reflow(); });
                toggle.Accent = Neon.Faint;
                retry = Button("Retry", delegate { window.Try(delegate { window.workspace.Retry(chat); window.SyncItems(); window.UpdateKeys(); }); });
                retry.Accent = Neon.Cyan;
                allow = Button("Allow", delegate { Answer(true, false); });
                allow.Accent = Neon.Cyan;
                always = Button("Always allow (this tool, this chat)", delegate { Answer(true, true); });
                always.Accent = Neon.Cyan;
                deny = Button("Deny", delegate { Answer(false, false); });
                deny.Accent = Neon.Hot;
                reasonLabel = new Label { Text = "Deny reason (optional)", ForeColor = Neon.Faint, BackColor = Color.Transparent, Font = window.small };
                Controls.Add(reasonLabel);
                reason = new TextBox { BorderStyle = BorderStyle.FixedSingle, BackColor = Neon.Field, ForeColor = Neon.White, Font = window.Font, AccessibleName = "Deny reason (optional)" };
                Controls.Add(reason);
                RefreshItem();
            }

            private Plate Button(string text, EventHandler click)
            {
                Plate key = new Plate { Text = text, Font = window.Font };
                key.Click += click;
                Controls.Add(key);
                return key;
            }

            private void Answer(bool yes, bool every)
            {
                WorkspaceApproval ask = Item.Ask;
                if (ask == null) return;
                window.Try(delegate
                {
                    if (window.workspace.Answer(chat, ask, yes, every, reason.Text)) allow.Enabled = always.Enabled = deny.Enabled = reason.Enabled = false;
                });
            }

            internal void Result(string text) { if (result != text) { result = text; drawn = null; } }

            internal void RefreshItem()
            {
                label.Text = Item.Kind == "user" ? "YOU" : Item.Kind == "text" ? "CLAUDE" : Item.Kind == "error" ? "ERROR" : Item.Kind == "approval" ? Item.Text : Item.Kind == "undo" ? "UNDO" : "";
                string text = Item.Kind == "approval" ? Item.Input : Item.Kind == "tool" ? result : Item.Text;
                if (drawn == text) return;
                drawn = text;
                Format(text, Item.Kind == "approval" || Item.Kind == "tool");
            }

            private void Format(string text, bool code)
            {
                body.Clear();
                body.SelectionFont = code ? window.mono : Item.Kind == "end" ? window.small : window.Font;
                if (code || Item.Kind != "text") { body.AppendText(text); body.Select(0, 0); return; }
                bool fenced = false;
                foreach (string line in text.Replace("\r", "").Split('\n'))
                {
                    if (line.TrimStart().StartsWith("```", StringComparison.Ordinal)) { fenced = !fenced; continue; }
                    body.SelectionFont = fenced ? window.mono : window.Font;
                    body.SelectionBackColor = fenced ? Neon.Well : BackColor;
                    body.AppendText(line + "\n");
                }
                body.Select(0, 0);
            }

            internal void Reflow(int width)
            {
                RefreshItem();
                int pad = window.Z(10), inner = width - pad * 2, y = pad;
                Width = width;
                bool tool = Item.Kind == "tool", approval = Item.Kind == "approval", pending = approval && Item.Decision.Length == 0 && Item.Ask != null;
                label.Visible = !tool && label.Text.Length > 0;
                if (label.Visible) { label.Bounds = new Rectangle(pad, y, inner, window.Z(24)); y += window.Z(28); }
                toggle.Visible = tool;
                if (tool) { toggle.Text = (opened ? "- " : "+ ") + Item.Text; toggle.Bounds = new Rectangle(pad, y, inner, window.Z(30)); y += window.Z(34); }
                body.Visible = !tool || opened;
                if (body.Visible)
                {
                    int height;
                    body.Width = inner;
                    if (approval) { height = window.Z(180); body.ScrollBars = RichTextBoxScrollBars.Vertical; body.Inside = true; }
                    else
                    {
                        Size measured = TextRenderer.MeasureText(body.Text.Length == 0 ? " " : body.Text, tool ? window.mono : body.Font, new Size(Math.Max(20, inner - window.Z(8)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.TextBoxControl);
                        height = Math.Max(window.Z(22), measured.Height + window.Z(12));
                        if (body.IsHandleCreated)
                        {
                            Point end = body.GetPositionFromCharIndex(body.TextLength);
                            height = Math.Max(height, end.Y + body.Font.Height + window.Z(8));
                        }
                    }
                    body.Bounds = new Rectangle(pad, y, inner, height);
                    y += height + window.Z(6);
                }
                retry.Visible = Item.Retry;
                if (retry.Visible) { retry.Enabled = !chat.Running; retry.Bounds = new Rectangle(pad, y, window.Z(86), window.Z(30)); y += window.Z(36); }
                reasonLabel.Visible = reason.Visible = allow.Visible = always.Visible = deny.Visible = pending;
                if (pending)
                {
                    reasonLabel.Bounds = new Rectangle(pad, y + window.Z(4), window.Z(156), window.Z(22));
                    reason.Bounds = new Rectangle(pad + window.Z(164), y, inner - window.Z(164), window.Z(28));
                    y += window.Z(36);
                    allow.Bounds = new Rectangle(pad, y, window.Z(72), window.Z(32));
                    always.Bounds = new Rectangle(pad + window.Z(80), y, window.Z(278), window.Z(32));
                    deny.Bounds = new Rectangle(pad + window.Z(366), y, window.Z(72), window.Z(32));
                    y += window.Z(38);
                }
                Height = y + pad;
            }

            internal string Crowded()
            {
                List<Control> seen = new List<Control>();
                foreach (Control control in Controls)
                {
                    if (!control.Visible) continue;
                    if (!ClientRectangle.Contains(control.Bounds)) return "Workspace item clipped: " + Item.Kind + " / " + control.Text;
                    Plate key = control as Plate;
                    if (key != null && key != toggle && key.Text.Length > 0 && TextRenderer.MeasureText(key.Text, key.Font).Width + window.Z(12) > key.Width) return "Workspace approval key clipped: " + key.Text;
                    foreach (Control other in seen) if (control.Bounds.IntersectsWith(other.Bounds)) return "Workspace item overlap: " + Item.Kind;
                    seen.Add(control);
                }
                return null;
            }
        }

        private sealed class ChatText : RichTextBox
        {
            internal Action<int> Wheel;
            internal bool Inside;

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x020A && !Inside && Wheel != null)
                {
                    Wheel((short)((message.WParam.ToInt64() >> 16) & 65535));
                    message.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref message);
            }
        }
    }
}
