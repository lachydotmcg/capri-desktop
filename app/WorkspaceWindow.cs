using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
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
                    workspace.Waiting = delegate(WorkspaceChat chat, WorkspaceApproval ask) { Post(delegate { WorkspaceNeeds(chat, ask); }); };
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

        private void WorkspaceNeeds(WorkspaceChat chat, WorkspaceApproval ask)
        {
            if (quitting || chat.Deleted || ask.Choice != null || ask.Announced || (workspaceFront != null ? workspaceFront() : workspaceWindow != null && workspaceWindow.InFront)) return;
            if (LineBusy && workspaceSaid == null)
            {
                if (!ask.Queued)
                {
                    ask.Queued = true;
                    ownLines.Enqueue(delegate { ask.Queued = false; WorkspaceNeeds(chat, ask); });
                }
                return;
            }
            ask.Announced = true;
            string line = "Workspace needs your OK in " + chat.Title + ".";
            if (workspaceSaid != null) workspaceSaid(line, false);
            else Remark("", line, true);
        }

        private void CloseWorkspace()
        {
            if (workspaceWindow != null && !workspaceWindow.IsDisposed) workspaceWindow.KeepPlace();
            if (workspaceWindow != null) workspaceWindow.Dispose();
            if (workspace != null) workspace.Dispose();
        }
    }

    // Her Workspace window, built to work like Metis: chats down the side, the conversation in the middle, and a composer at
    // the bottom whose + adds folders and files through the regular Explorer windows. The chosen folders and files sit as
    // chips over the message box. Everything is drawn in her look, and the frame is shaped like the Mainframe's, so the
    // window's outline and its painted corners always agree.
    internal sealed class WorkspaceWindow : Form, LookWindow, WorkGround
    {
        internal const int Wide = 1080, Tall = 740, Narrowest = 900, Shortest = 600;
        private readonly Workspace workspace;
        private readonly Func<bool> blackwall;
        private readonly bool live;
        private readonly WorkKey shut, smaller, bigger, make, choose, undo, trust;
        private readonly ChatList list;
        private readonly Transcript transcript;
        private readonly Composer composer;
        private readonly WorkField rename;
        private readonly AskCard ask;
        private readonly Dictionary<WorkspaceChat, string> drafts = new Dictionary<WorkspaceChat, string>();
        private readonly Dictionary<WorkspaceChat, int> scrolls = new Dictionary<WorkspaceChat, int>();
        private readonly Action<WorkspaceChat, WorkspaceItem> changed;
        private WorkspaceItem pageEnd;
        private WorkspaceChat chat;
        private bool filling, saving, arranging;
        private float zoom;
        private Font small, strong, keys, caption, mono, monoPlain, note, title, heading;
        private Rectangle titleBox, folderBox;
        private ToolStripDropDown openMenu;
        internal Func<string, bool> Confirm;
        internal Func<string[]> PickFolders, PickFiles;
        internal float Zoom { get { return zoom; } }
        internal int ItemCount { get { return transcript.Blocks.Count; } }
        internal WorkspaceChat Chat { get { return chat; } }
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
            AllowDrop = true;
            SetFonts();
            shut = Key("", delegate { Close(); });
            shut.Glyph = 2;
            shut.AccessibleName = "Close Workspace";
            bigger = Key("", delegate { WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; });
            bigger.Glyph = 3;
            bigger.AccessibleName = "Maximize or restore Workspace";
            smaller = Key("", delegate { WindowState = FormWindowState.Minimized; });
            smaller.Glyph = 1;
            smaller.AccessibleName = "Minimize Workspace";
            make = Key("+ New chat", delegate { NewChat(); });
            make.Accent = Neon.Cyan;
            make.AccessibleName = "New chat";
            choose = Key("Choose folder", delegate { ChooseFolders(); });
            choose.Accent = Neon.Cyan;
            choose.Solid = true;
            choose.AccessibleName = "Choose a folder to start a chat";
            undo = Key("Undo last turn", delegate { if (chat != null) Try(delegate { workspace.Undo(chat); SyncItems(); UpdateKeys(); }); });
            undo.Accent = Neon.Faint;
            undo.AccessibleName = "Undo Claude Code's file edits from the last turn";
            trust = Key("Trust folder", delegate { if (chat != null) Try(delegate { workspace.AskTrust(chat); SyncItems(); }); });
            trust.Accent = Neon.Cyan;
            list = new ChatList(this);
            Controls.Add(list);
            transcript = new Transcript(this);
            Controls.Add(transcript);
            composer = new Composer(this);
            Controls.Add(composer);
            rename = new WorkField { Visible = false, Cue = "Chat name" };
            rename.Box.AccessibleName = "Chat title";
            rename.Box.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; SaveTitle(rename.Box.Text); EndRename(); composer.Input.Focus(); }
                if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; EndRename(); }
            };
            rename.Box.Leave += delegate { if (rename.Visible) { SaveTitle(rename.Box.Text); EndRename(); } };
            Controls.Add(rename);
            ask = new AskCard(this);
            Controls.Add(ask);
            foreach (Control control in new Control[] { list, transcript, composer, composer.Input }) Dropping(control);
            Dropping(this);
            ApplyFonts();
            ClientSize = new Size(Z(Wide), Z(Tall));
            MinimumSize = new Size(Z(Narrowest), Z(Shortest));
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
            Shape();
            Place();
        }

        internal int Z(int value) { return (int)Math.Round(value * zoom); }
        internal int Z(float value) { return (int)Math.Round(value * zoom); }

        private WorkKey Key(string text, EventHandler click)
        {
            WorkKey key = new WorkKey { Text = text };
            key.Click += click;
            Controls.Add(key);
            return key;
        }

        private void SetFonts()
        {
            Font old = Font;
            Font[] olds = { small, strong, keys, caption, mono, monoPlain, note, title, heading };
            Font = Neon.Type(13.5f * zoom, false, GraphicsUnit.Pixel);
            small = Neon.Type(12f * zoom, false, GraphicsUnit.Pixel);
            strong = Neon.Type(13.5f * zoom, true, GraphicsUnit.Pixel);
            keys = Neon.Type(12f * zoom, true, GraphicsUnit.Pixel);
            caption = Neon.Type(10.5f * zoom, true, GraphicsUnit.Pixel);
            mono = Look.Mono(12f * zoom, FontStyle.Regular, GraphicsUnit.Pixel);
            monoPlain = Look.FallbackMono(12f * zoom, FontStyle.Regular, GraphicsUnit.Pixel);
            note = Look.Mono(11.5f * zoom, FontStyle.Regular, GraphicsUnit.Pixel);
            title = Neon.Display(21.5f * zoom, GraphicsUnit.Pixel);
            heading = Neon.Type(19f * zoom, true, GraphicsUnit.Pixel);
            if (olds[0] != null) { foreach (Font font in olds) font.Dispose(); old.Dispose(); }
        }

        private void ApplyFonts()
        {
            foreach (Control control in Controls) { WorkKey key = control as WorkKey; if (key != null) key.Font = keys; }
            rename.Font = strong;
            composer.ApplyFonts();
            transcript.ApplyFonts();
            list.Invalidate();
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

        // A new chat starts with its folder: until one is chosen the window asks for exactly that.
        private void NewChat()
        {
            SelectChat(null);
            choose.Focus();
        }

        private string StartFolder()
        {
            if (chat != null) return chat.Folder;
            List<WorkspaceChat> all = workspace.Chats();
            return all.Count > 0 ? Path.GetDirectoryName(all[0].Folder) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        internal void ChooseFolders()
        {
            string[] picked = PickFolders != null ? PickFolders() : WorkPickers.Folders(this, chat == null ? "Choose a folder for this chat" : "Add folders to this chat", StartFolder());
            if (picked == null || picked.Length == 0) return;
            AddPaths(picked, new string[0]);
        }

        internal void ChooseFiles()
        {
            if (chat == null) { ChooseFolders(); return; }
            string[] picked = PickFiles != null ? PickFiles() : WorkPickers.Files(this, "Add files to this chat", chat.Folder);
            if (picked == null || picked.Length == 0) return;
            AddPaths(new string[0], picked);
        }

        // Folders and files from the pickers or dropped on the window. With no chat yet, the first folder starts one there.
        internal void AddPaths(string[] folders, string[] files)
        {
            Try(delegate
            {
                List<string> rest = new List<string>(folders);
                if (chat == null)
                {
                    if (rest.Count == 0) return;
                    WorkspaceChat made = workspace.NewChat(rest[0], blackwall != null && blackwall());
                    rest.RemoveAt(0);
                    FillChats();
                    SelectChat(made);
                }
                if (rest.Count > 0) workspace.AddFolders(chat, rest);
                if (files.Length > 0) workspace.AddFiles(chat, files);
                FillHeader();
                Place();
                composer.Input.Focus();
            });
        }

        internal void RemovePath(string path)
        {
            if (chat == null) return;
            Try(delegate { workspace.Remove(chat, path); FillHeader(); Place(); });
        }

        private void Dropping(Control control)
        {
            control.AllowDrop = true;
            control.DragEnter += delegate(object sender, DragEventArgs e) { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; };
            control.DragDrop += delegate(object sender, DragEventArgs e)
            {
                string[] dropped = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (dropped == null) return;
                List<string> folders = new List<string>(), files = new List<string>();
                foreach (string path in dropped) { if (Directory.Exists(path)) folders.Add(path); else if (File.Exists(path)) files.Add(path); }
                AddPaths(folders.ToArray(), files.ToArray());
            };
        }

        private void FillChats() { list.Fill(workspace.Chats(), chat); }

        private void SelectChat(WorkspaceChat selected)
        {
            if (chat == selected && selected != null && transcript.Blocks.Count > 0) return;
            if (chat != null) { drafts[chat] = composer.Input.Text; scrolls[chat] = transcript.Offset; }
            EndRename();
            chat = selected;
            pageEnd = null;
            transcript.Clear();
            filling = true;
            string draft;
            composer.Input.Text = chat != null && drafts.TryGetValue(chat, out draft) ? draft : "";
            filling = false;
            list.Picked = chat;
            transcript.Following = true;
            FillHeader();
            SyncItems();
            int scroll;
            if (chat != null && scrolls.TryGetValue(chat, out scroll)) transcript.ScrollTo(scroll);
            UpdateKeys();
            Place();
        }

        private void SaveTitle(string text)
        {
            if (chat != null && !filling && text.Trim().Length > 0 && text.Trim() != chat.Title) Try(delegate { workspace.Change(chat, text, null, null); });
        }

        internal void BeginRename()
        {
            if (chat == null) return;
            rename.Box.Text = chat.Title;
            rename.Visible = true;
            Place();
            rename.Box.Focus();
            rename.Box.SelectAll();
        }

        private void EndRename()
        {
            if (!rename.Visible) return;
            rename.Visible = false;
            Invalidate(titleBox);
        }

        // Renames the chat the way a typed title does.
        internal void Rename(string text) { BeginRename(); rename.Box.Text = text; SaveTitle(text); EndRename(); }

        private void FillHeader()
        {
            composer.Fill(chat);
            bool untrusted = chat != null && !workspace.Trusted(chat);
            trust.Text = chat != null && Workspace.AllFolders(chat).Count > 1 ? "Trust folders" : "Trust folder";
            trust.Visible = untrusted;
            Invalidate();
        }

        private void Send()
        {
            if (chat == null || chat.Running || chat.PendingText != null || chat.Undoing != null || string.IsNullOrWhiteSpace(composer.Input.Text)) return;
            string text = composer.Input.Text;
            Try(delegate
            {
                workspace.Send(chat, text, false);
                composer.Input.Clear();
                transcript.Following = true;
                FillHeader();
                SyncItems();
                UpdateKeys();
                composer.Input.Focus();
            });
        }

        private void Stop() { if (chat != null) Try(delegate { workspace.Stop(chat); }); }

        private void UpdateKeys()
        {
            bool picked = chat != null, running = picked && chat.Running, waiting = picked && (chat.PendingText != null || chat.Undoing != null);
            composer.Running(running, picked && !running && !waiting);
            bool can = picked && !running;
            if (can) try { can = workspace.CanUndo(chat); } catch (Exception) { can = true; }
            undo.Enabled = can;
            list.Invalidate();
            Place();
        }

        internal void PickMode(string picked)
        {
            if (chat == null || picked == chat.Mode) return;
            WorkspaceChat target = chat;
            Action apply = delegate { Try(delegate { workspace.Change(target, null, picked, null); }); FillHeader(); composer.Input.Focus(); };
            if (picked != "Everything") { apply(); return; }
            string question = "Everything lets Claude Code run anything without asking. Switch?";
            if (Confirm != null) { if (Confirm(question)) apply(); return; }
            ask.Ask(question, "Switch to Everything", Neon.Hot, "Keep " + chat.Mode, delegate(bool yes) { if (yes && chat == target) apply(); });
        }

        internal void PickModel(string picked)
        {
            if (chat == null) return;
            Try(delegate { workspace.Change(chat, null, null, picked); });
            FillHeader();
        }

        private static readonly string[] ModeNotes = { "Asks before anything past reading", "Edits files here without asking", "Plans, changes nothing", "Runs anything without asking" };

        // Her menus: the + menu, MODE and MODEL. Returned open, so the smoke test can picture them.
        internal ToolStripDropDown OpenMenu(string which)
        {
            if (openMenu != null && !openMenu.IsDisposed) openMenu.Close();
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new Mainframe.MenuLook(), BackColor = Neon.Ground, ForeColor = Neon.White, ShowImageMargin = false };
            Font own = new Font(Font, FontStyle.Regular);
            menu.Font = own;
            Control anchor;
            if (which == "add")
            {
                anchor = composer.Plus;
                menu.ShowCheckMargin = false;
                menu.Items.Add(new ToolStripMenuItem(chat == null ? "Choose folders..." : "Add folders...", null, delegate { Post(ChooseFolders); }) { AccessibleName = "Add folders" });
                ToolStripMenuItem files = new ToolStripMenuItem("Add files...", null, delegate { Post(ChooseFiles); }) { AccessibleName = "Add files" };
                files.Enabled = chat != null;
                menu.Items.Add(files);
            }
            else
            {
                bool modes = which == "mode";
                anchor = modes ? composer.Mode : composer.Model;
                menu.ShowCheckMargin = true;
                string[] choices = modes ? Workspace.Modes : Workspace.Models;
                for (int i = 0; i < choices.Length; i++)
                {
                    string choice = choices[i];
                    ToolStripMenuItem item = new ToolStripMenuItem(choice, null, delegate { if (modes) PickMode(choice); else PickModel(choice); });
                    if (modes) item.ShortcutKeyDisplayString = ModeNotes[i];
                    item.Checked = chat != null && (modes ? chat.Mode : chat.Model) == choice;
                    item.AccessibleName = choice;
                    menu.Items.Add(item);
                }
            }
            menu.Closed += delegate { Post(delegate { menu.Dispose(); own.Dispose(); }); };
            openMenu = menu;
            menu.Show(anchor, new Point(0, -menu.PreferredSize.Height - Z(4)));
            return menu;
        }

        private void ChatMenu(WorkspaceChat picked, Point at)
        {
            if (picked != chat) SelectChat(picked);
            ContextMenuStrip menu = new ContextMenuStrip { Renderer = new Mainframe.MenuLook(), BackColor = Neon.Ground, ForeColor = Neon.White, ShowImageMargin = false };
            Font own = new Font(Font, FontStyle.Regular);
            menu.Font = own;
            menu.Items.Add("Rename", null, delegate { Post(BeginRename); });
            menu.Items.Add("Open folder in Explorer", null, delegate { OpenFolder(picked.Folder, null); });
            menu.Items.Add("Delete chat", null, delegate { Post(delegate { DeleteChat(picked); }); });
            menu.Closed += delegate { Post(delegate { menu.Dispose(); own.Dispose(); }); };
            openMenu = menu;
            menu.Show(list, at);
        }

        internal void DeleteChat(WorkspaceChat picked)
        {
            string question = "Delete this chat? Your files stay in the folder.";
            Action remove = delegate
            {
                Try(delegate
                {
                    workspace.Delete(picked);
                    if (chat == picked) { chat = null; transcript.Clear(); }
                    drafts.Remove(picked); scrolls.Remove(picked);
                    FillChats();
                    List<WorkspaceChat> rest = workspace.Chats();
                    SelectChat(rest.Count > 0 ? rest[0] : null);
                });
            };
            if (Confirm != null) { if (Confirm(question)) remove(); return; }
            ask.Ask(question, "Delete chat", Neon.Hot, "Keep it", delegate(bool yes) { if (yes) remove(); });
        }

        private void OpenFolder(string folder, string file)
        {
            Try(delegate
            {
                string args = file != null ? "/select," + PlanBrain.Quoted(file) : PlanBrain.Quoted(folder);
                Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            });
        }

        private void SyncItems()
        {
            List<WorkspaceItem> items = new List<WorkspaceItem>();
            if (chat != null) lock (chat.Gate) items.AddRange(chat.Items);
            Dictionary<string, string> results = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> tools = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkspaceItem item in items)
            {
                if (item.Kind == "tool") tools.Add(item.Turn + "/" + item.Id);
                if (item.Kind == "result") results[item.Turn + "/" + item.Id] = item.Text;
            }
            items.RemoveAll(delegate(WorkspaceItem item) { return item.Kind == "result" && tools.Contains(item.Turn + "/" + item.Id); });
            int end = pageEnd == null ? items.Count : items.IndexOf(pageEnd) + 1;
            if (end <= 0) { pageEnd = null; end = items.Count; }
            int start = Math.Max(0, end - 150);
            transcript.Empty = chat == null ? "" : "Write your first message below.";
            transcript.Fill(items.GetRange(start, end - start), results, start > 0, end < items.Count);
        }

        private void Page(bool back)
        {
            if (chat == null) return;
            List<WorkspaceItem> items;
            lock (chat.Gate) items = new List<WorkspaceItem>(chat.Items);
            HashSet<string> tools = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkspaceItem item in items) if (item.Kind == "tool") tools.Add(item.Turn + "/" + item.Id);
            items.RemoveAll(delegate(WorkspaceItem item) { return item.Kind == "result" && tools.Contains(item.Turn + "/" + item.Id); });
            int end = pageEnd == null ? items.Count : items.IndexOf(pageEnd) + 1;
            end = back ? Math.Max(1, end - 150) : Math.Min(items.Count, end + 150);
            pageEnd = end >= items.Count ? null : items[end - 1];
            transcript.Following = !back && pageEnd == null;
            SyncItems();
            if (back) transcript.ScrollTo(0);
        }

        // Where everything sits, in design px times her zoom: the title bar, the chats down the left, and the chat on the right
        // with its header, its conversation and the composer at the bottom.
        private void Place()
        {
            if (composer == null || arranging) return;
            arranging = true;
            try
            {
                int w = ClientSize.Width, h = ClientSize.Height, left = Z(262), right = w - Z(20), width = right - left;
                shut.Bounds = new Rectangle(w - Z(48), Z(13), Z(28), Z(24));
                bigger.Bounds = new Rectangle(w - Z(80), Z(13), Z(28), Z(24));
                smaller.Bounds = new Rectangle(w - Z(112), Z(13), Z(28), Z(24));
                make.Bounds = new Rectangle(Z(16), Z(64), Z(214), Z(32));
                list.Bounds = new Rectangle(Z(16), Z(106), Z(214), Math.Max(Z(60), h - Z(126)));
                bool open = chat != null;
                transcript.Visible = composer.Visible = undo.Visible = open;
                choose.Visible = !open;
                if (!open)
                {
                    trust.Visible = rename.Visible = false;
                    int middle = left + width / 2, wide = Math.Max(Z(170), choose.Needs + Z(24));
                    choose.Bounds = new Rectangle(middle - wide / 2, EmptyTop() + Z(150), wide, Z(38));
                    titleBox = folderBox = Rectangle.Empty;
                }
                else
                {
                    int keyRight = right;
                    undo.Bounds = new Rectangle(keyRight - Math.Max(Z(124), undo.Needs), Z(66), Math.Max(Z(124), undo.Needs), Z(28));
                    keyRight = undo.Left - Z(8);
                    if (trust.Visible) { trust.Bounds = new Rectangle(keyRight - Math.Max(Z(108), trust.Needs), Z(66), Math.Max(Z(108), trust.Needs), Z(28)); keyRight = trust.Left - Z(8); }
                    titleBox = new Rectangle(left, Z(62), Math.Max(Z(80), keyRight - left - Z(8)), Z(24));
                    folderBox = new Rectangle(left, Z(88), titleBox.Width, Z(18));
                    rename.Bounds = new Rectangle(left - Z(4), Z(60), titleBox.Width, Z(30));
                    composer.Width = width;
                    composer.Arrange();
                    int composerTall = composer.Wanted;
                    composer.Bounds = new Rectangle(left, h - Z(20) - composerTall, width, composerTall);
                    int hint = chat.Running && !composer.HintShown ? Z(22) : 0;
                    transcript.Bounds = new Rectangle(left - Z(4), Z(116), width + Z(8), Math.Max(Z(80), composer.Top - Z(10) - hint - Z(116)));
                }
                ask.Place();
            }
            finally { arranging = false; }
            Invalidate();
        }

        private int EmptyTop() { return Math.Max(Z(90), (ClientSize.Height - Z(260)) / 2); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Shape(); Place(); }

        // Cuts the window to her frame's corners, like the Mainframe, so its outline is the frame it paints.
        private void Shape() { if (Width > 0 && Height > 0) Neon.Clip(this, Z(16), Z(6)); }

        public void PaintGround(Graphics graphics, Rectangle area)
        {
            Neon.Backdrop(graphics, ClientSize, Z(210));
            using (Brush band = new SolidBrush(Neon.Band)) graphics.FillRectangle(band, Z(10), Z(56), Z(226), ClientSize.Height - Z(70));
        }

        protected override void OnPaintBackground(PaintEventArgs e) { PaintGround(e.Graphics, ClientRectangle); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            int w = ClientSize.Width, h = ClientSize.Height;
            Neon.Frame(graphics, ClientSize, zoom, Z(16), Z(6), Z(64), Z(34));
            Size named = TextRenderer.MeasureText(graphics, "WORKSPACE", title, Size.Empty, WorkPaint.Line);
            Rectangle at = new Rectangle(Z(26), Z(12), named.Width + Z(4), Z(28));
            TextRenderer.DrawText(graphics, "WORKSPACE", title, at, Neon.Rose, WorkPaint.Line);
            int beta = WorkPaint.Wide("BETA", caption) + Z(12);
            Rectangle tag = new Rectangle(at.Right + Z(8), Z(17), beta, Z(18));
            WorkPaint.Plate(graphics, tag, Z(4), Neon.Deep, Neon.Tone(28, 84, 92), Math.Max(1, zoom));
            TextRenderer.DrawText(graphics, "BETA", caption, tag, Neon.Cyan, WorkPaint.Line | TextFormatFlags.HorizontalCenter);
            using (Pen rule = new Pen(Neon.Rule, Math.Max(1, zoom)))
            {
                graphics.DrawLine(rule, Z(16), Z(48), w - Z(16), Z(48));
                if (chat != null) graphics.DrawLine(rule, Z(262), Z(110), w - Z(20), Z(110));
            }
            int left = Z(262), right = w - Z(20);
            if (chat == null)
            {
                int top = EmptyTop(), middle = left + (right - left) / 2;
                WorkPaint.FolderMark(graphics, new RectangleF(middle - Z(22), top, Z(44), Z(40)), Neon.Cyan, zoom * 1.4f);
                Rectangle line = new Rectangle(left + Z(20), top + Z(54), right - left - Z(40), Z(28));
                TextRenderer.DrawText(graphics, workspace.Chats().Count == 0 ? "Start your first chat" : "Start a new chat", heading, line, Neon.White, WorkPaint.Line | TextFormatFlags.HorizontalCenter);
                Rectangle words = new Rectangle(left + Z(40), top + Z(88), right - left - Z(80), Z(50));
                TextRenderer.DrawText(graphics, "Choose the folder Claude Code works in. You can pick several, and add more folders and files once you're in.", Font, words, Neon.Faint, WorkPaint.Wrap | TextFormatFlags.HorizontalCenter);
                Rectangle hint = new Rectangle(left + Z(20), choose.Bottom + Z(14), right - left - Z(40), Z(20));
                TextRenderer.DrawText(graphics, "or drop folders here", note, hint, Neon.Faint, WorkPaint.Line | TextFormatFlags.HorizontalCenter);
                if (workspace.Problems.Count > 0)
                {
                    Rectangle problems = new Rectangle(left + Z(20), hint.Bottom + Z(18), right - left - Z(40), Math.Max(0, h - hint.Bottom - Z(40)));
                    TextRenderer.DrawText(graphics, string.Join("\n", workspace.Problems.ToArray()), small, problems, Neon.Hot, WorkPaint.Wrap | TextFormatFlags.HorizontalCenter);
                }
                return;
            }
            if (!rename.Visible) TextRenderer.DrawText(graphics, chat.Title, heading, titleBox, Neon.White, WorkPaint.Line | TextFormatFlags.EndEllipsis);
            List<string> folders = Workspace.AllFolders(chat);
            string where = folders.Count > 1 ? folders[0] + "  +" + (folders.Count - 1) + (folders.Count == 2 ? " folder" : " folders") : folders[0];
            TextRenderer.DrawText(graphics, where, note, folderBox, Neon.Faint, WorkPaint.Line | TextFormatFlags.PathEllipsis);
            if (trust.Visible)
            {
                Rectangle words = new Rectangle(trust.Left - Z(96), trust.Top, Z(88), trust.Height);
                if (words.Left > titleBox.Left + Z(120)) TextRenderer.DrawText(graphics, "Not trusted", small, words, Neon.Faint, WorkPaint.Line | TextFormatFlags.Right);
            }
            if (chat.Running && !composer.HintShown)
                TextRenderer.DrawText(graphics, Composer.Hint, note, new Rectangle(left, composer.Top - Z(24), right - left, Z(16)), Neon.Faint, WorkPaint.Line | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (chat == null || e.Button != MouseButtons.Left) return;
            if (titleBox.Contains(e.Location)) BeginRename();
            else if (folderBox.Contains(e.Location)) OpenFolder(chat.Folder, null);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor want = chat != null && titleBox.Contains(e.Location) ? Cursors.IBeam : chat != null && folderBox.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
            if (Cursor != want) Cursor = want;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape && ask.Visible) { ask.Answer(false); e.Handled = true; return; }
            if (e.Control && e.KeyCode == Keys.N) { NewChat(); e.Handled = true; return; }
            base.OnKeyDown(e);
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
                if (hit == 1 && point.Y < Z(48)) hit = 2;
                message.Result = new IntPtr(hit);
            }
            if (message.Msg == 0x02E0 && live)
            {
                float factor = (message.WParam.ToInt64() & 65535) / 96f;
                Rectangle area = Screen.FromControl(this).WorkingArea;
                factor = Math.Max(0.75f, Math.Min(factor, Math.Min(area.Width / (float)Narrowest, area.Height / (float)Shortest)));
                if (factor > 0 && Math.Abs(factor - zoom) > 0.01f) Rescale(factor);
            }
        }

        // Her zoom changed, like on a screen with another scale: every font and size again, and the conversation laid out anew.
        internal void Rescale(float factor)
        {
            Rectangle area = Screen.FromControl(this).WorkingArea;
            zoom = factor;
            SetFonts();
            ApplyFonts();
            MinimumSize = new Size(Math.Min(Z(Narrowest), area.Width), Math.Min(Z(Shortest), area.Height));
            Size = new Size(Math.Min(Math.Max(Width, MinimumSize.Width), area.Width), Math.Min(Math.Max(Height, MinimumSize.Height), area.Height));
            MaximizedBounds = area;
            transcript.Clear();
            SyncItems();
            Shape();
            Place();
        }

        private void ClosingWindow(object sender, FormClosingEventArgs e)
        {
            if (!live || e.CloseReason != CloseReason.UserClosing) return;
            if (rename.Visible) SaveTitle(rename.Box.Text);
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
            MinimumSize = new Size(Math.Min(Z(Narrowest), area.Width), Math.Min(Z(Shortest), area.Height));
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

        internal void KeepPlace() { if (rename.Visible) SaveTitle(rename.Box.Text); SavePlace(); }

        // What the smoke test checks: every shown control inside the window, none overlapping another, no key's words cut off,
        // and the same inside the composer and every card in the conversation.
        internal string Crowded()
        {
            Place();
            List<Control> shown = new List<Control>();
            foreach (Control control in Controls)
            {
                if (!control.Visible || control == ask) continue;
                if (!ClientRectangle.Contains(control.Bounds)) return "Outside Workspace: " + (control.AccessibleName ?? control.Text) + " " + control.Bounds;
                string cut = Clipped(control);
                if (cut != null) return cut;
                foreach (Control other in shown) if (control.Bounds.IntersectsWith(other.Bounds)) return "Workspace overlap: " + (control.AccessibleName ?? control.Text) + " / " + (other.AccessibleName ?? other.Text);
                shown.Add(control);
            }
            if (chat != null && (titleBox.IntersectsWith(undo.Bounds) || trust.Visible && titleBox.IntersectsWith(trust.Bounds))) return "Workspace title runs under its keys.";
            string inside = composer.Crowded();
            if (inside != null) return inside;
            return transcript.Crowded();
        }

        internal static string Clipped(Control control)
        {
            WorkKey key = control as WorkKey;
            if (key != null && key.Glyph == 0 && key.Text.Length > 0 && key.Needs > key.Width + 1) return "Workspace key doesn't fit: " + key.Text + " (" + key.Needs + " > " + key.Width + ")";
            return null;
        }

        internal void Picture(string path) { LookShots.Save(this, path, openMenu != null && !openMenu.IsDisposed && openMenu.Visible ? openMenu : null); }

        internal void ShowChat(WorkspaceChat selected) { SelectChat(selected); }
        internal void RefreshChat() { FillChats(); FillHeader(); SyncItems(); UpdateKeys(); }
        internal TextBox Input { get { return composer.Input; } }
        internal WorkKey Find(string text)
        {
            foreach (Control control in Every(this)) { WorkKey key = control as WorkKey; if (key != null && key.Visible && key.Text == text) return key; }
            return null;
        }
        internal Control Named(string name)
        {
            foreach (Control control in Every(this)) if (control.AccessibleName == name) return control;
            return null;
        }
        internal int Scrolled { get { return transcript.Offset; } }
        internal bool AtEnd { get { return transcript.AtBottom; } }
        internal void ScrollConversation(int offset) { transcript.ScrollTo(offset); }
        internal void ScrollToEnd() { transcript.ScrollTo(int.MaxValue); }
        internal int ConversationTall { get { return transcript.Content; } }
        internal string ToggleTool(string id) { return transcript.Toggle(id); }
        internal string CopyText(WorkspaceItem item) { return transcript.CopyOf(item); }
        internal string CodeFont { get { return mono.Name; } }
        internal string ProseFont { get { return Font.Name; } }
        internal bool ChooseShown { get { return choose.Visible && chat == null; } }
        internal List<string> Chips { get { return composer.ChipNames(); } }
        internal void ClickChipCross(string path) { composer.Cross(path); }
        internal void Pending(string question, bool yes) { if (ask.Visible && ask.Question == question) ask.Answer(yes); }
        internal string Asking { get { return ask.Visible ? ask.Question : null; } }
        internal void TypeInto(TextBox box, Keys key)
        {
            System.Reflection.MethodInfo down = typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            down.Invoke(box, new object[] { new KeyEventArgs(key) });
        }

        private static IEnumerable<Control> Every(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control inner in Every(child)) yield return inner;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                workspace.Changed -= changed;
                transcript.Clear();
                if (openMenu != null && !openMenu.IsDisposed) openMenu.Dispose();
            }
            base.Dispose(disposing);
            if (disposing) foreach (Font font in new[] { small, strong, keys, caption, mono, monoPlain, note, title, heading }) if (font != null) font.Dispose();
        }

        // The chats down the left: each one's title, its folders under it, and a dot while Claude Code works (cyan) or waits
        // for an OK (red). The picked one sits on a teal plate with a cyan tick, like a picked key.
        private sealed class ChatList : Control, WorkGround
        {
            private readonly WorkspaceWindow window;
            private readonly List<WorkspaceChat> chats = new List<WorkspaceChat>();
            private WorkspaceChat picked;
            private int offset, hover = -1;
            private bool dragging;
            private int dragFrom, dragOffset;

            internal ChatList(WorkspaceWindow window)
            {
                this.window = window;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleName = "Workspace chats";
                AccessibleRole = AccessibleRole.List;
            }

            internal WorkspaceChat Picked { get { return picked; } set { picked = value; Invalidate(); } }
            private int Row { get { return window.Z(56); } }
            private int Content { get { return chats.Count * (Row + window.Z(4)); } }

            internal void Fill(List<WorkspaceChat> all, WorkspaceChat chat)
            {
                chats.Clear();
                chats.AddRange(all);
                picked = chat;
                offset = Math.Max(0, Math.Min(offset, Content - Height));
                Invalidate();
            }

            public void PaintGround(Graphics graphics, Rectangle area) { WorkPaint.Behind(graphics, this); }

            protected override void OnPaintBackground(PaintEventArgs e) { WorkPaint.Behind(e.Graphics, this); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                float s = window.zoom;
                int rail = window.Z(10);
                if (chats.Count == 0)
                {
                    TextRenderer.DrawText(graphics, "Your chats show up here.", window.note, new Rectangle(window.Z(8), window.Z(6), Width - window.Z(16), window.Z(40)), Neon.Faint, WorkPaint.Wrap);
                    return;
                }
                for (int i = 0; i < chats.Count; i++)
                {
                    Rectangle row = new Rectangle(0, i * (Row + window.Z(4)) - offset, Width - rail, Row);
                    if (row.Bottom < 0 || row.Top > Height) continue;
                    WorkspaceChat item = chats[i];
                    bool on = item == picked;
                    if (on) WorkPaint.Plate(graphics, row, 6 * s, Neon.Deep, Neon.Tone(28, 84, 92), Math.Max(1, s));
                    else if (i == hover) WorkPaint.Plate(graphics, row, 6 * s, Neon.Hover, Color.Empty, 0);
                    if (on) using (Brush tick = new SolidBrush(Neon.Cyan)) graphics.FillRectangle(tick, row.Left, row.Top + window.Z(8), Math.Max(2, window.Z(2.5f)), row.Height - window.Z(16));
                    bool pending = Workspace.Pending(item);
                    int dot = pending || item.Running ? window.Z(18) : 0;
                    TextRenderer.DrawText(graphics, item.Title, window.strong, new Rectangle(row.Left + window.Z(12), row.Top + window.Z(8), row.Width - window.Z(20) - dot, window.Z(20)), on ? Neon.Cyan : Neon.White, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    List<string> folders = Workspace.AllFolders(item);
                    string name = Path.GetFileName(folders[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (name.Length == 0) name = folders[0];
                    if (folders.Count > 1) name += "  +" + (folders.Count - 1);
                    TextRenderer.DrawText(graphics, name, window.note, new Rectangle(row.Left + window.Z(12), row.Top + window.Z(31), row.Width - window.Z(20), window.Z(16)), Neon.Faint, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    if (dot > 0)
                    {
                        SmoothingMode was = graphics.SmoothingMode;
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (Brush fill = new SolidBrush(pending ? Neon.Hot : Neon.Cyan)) graphics.FillEllipse(fill, row.Right - window.Z(18), row.Top + window.Z(14), window.Z(7), window.Z(7));
                        graphics.SmoothingMode = was;
                    }
                }
                WorkPaint.Rail(graphics, new Rectangle(Width - window.Z(4), window.Z(2), Math.Max(2, window.Z(3)), Height - window.Z(4)), Height, Content, offset, dragging);
            }

            private int At(Point point)
            {
                if (point.X >= Width - window.Z(10)) return -1;
                int index = (point.Y + offset) / (Row + window.Z(4));
                return index >= 0 && index < chats.Count && (point.Y + offset) % (Row + window.Z(4)) < Row ? index : -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging)
                {
                    RectangleF thumb = WorkPaint.Thumb(new Rectangle(0, window.Z(2), 1, Height - window.Z(4)), Height, Content, offset);
                    float room = Height - window.Z(4) - thumb.Height;
                    if (room > 0) Scroll(dragOffset + (int)((e.Y - dragFrom) * (Content - Height) / room));
                    return;
                }
                int over = At(e.Location);
                Cursor = over >= 0 ? Cursors.Hand : Cursors.Default;
                if (over != hover) { hover = over; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = -1; Invalidate(); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                if (e.Button == MouseButtons.Left && e.X >= Width - window.Z(10) && Content > Height) { dragging = true; dragFrom = e.Y; dragOffset = offset; Capture = true; return; }
                int index = At(e.Location);
                if (index < 0) return;
                if (e.Button == MouseButtons.Left) window.SelectChat(chats[index]);
                else if (e.Button == MouseButtons.Right) window.ChatMenu(chats[index], e.Location);
            }

            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragging) { dragging = false; Capture = false; Invalidate(); } }

            protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Scroll(offset - e.Delta * Row / 120); }

            protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || base.IsInputKey(key); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                int at = chats.IndexOf(picked);
                if (e.KeyCode == Keys.Down && at + 1 < chats.Count) window.SelectChat(chats[at + 1]);
                if (e.KeyCode == Keys.Up && at > 0) window.SelectChat(chats[at - 1]);
                if (e.KeyCode == Keys.Delete && picked != null) window.DeleteChat(picked);
                if (e.KeyCode == Keys.F2) window.BeginRename();
            }

            private void Scroll(int to) { offset = Math.Max(0, Math.Min(to, Content - Height)); Invalidate(); }
        }

        // The message box and everything around it, like Metis's composer: the chat's folders and files as chips along the
        // top, the box itself, and a row under it with +, MODE, MODEL and Send. The first folder chip is where Claude Code runs,
        // so it has no cross; a file chip turns faint once it has gone out with a message.
        private sealed class Composer : Control, WorkGround
        {
            private sealed class Chip { internal string Path, Name; internal bool Folder, Main, Sent; internal Rectangle Box, Cross; }

            private readonly WorkspaceWindow window;
            internal readonly MessageField Input;
            internal readonly WorkKey Plus, Mode, Model, Send, Stop;
            private readonly List<Chip> chips = new List<Chip>();
            private int chipsTall, inputTall, hover = -1;
            private bool hintShown;
            internal const string Hint = "Mode and model changes apply to your next message.";
            internal int Wanted;
            internal bool HintShown { get { return hintShown; } }

            internal Composer(WorkspaceWindow window)
            {
                this.window = window;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                AccessibleName = "Composer";
                Input = new MessageField { Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None, BackColor = Neon.Field, ForeColor = Neon.White,
                    AccessibleName = "Message. Enter sends. Shift+Enter adds a line.", Cue = "Message Claude Code. Enter sends, Shift+Enter adds a line." };
                Input.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; window.Send(); } };
                Input.TextChanged += delegate { if (!window.filling && Grow()) window.Place(); Invalidate(); };
                Input.GotFocus += delegate { Invalidate(); };
                Input.LostFocus += delegate { Invalidate(); };
                Input.Scrolled = delegate { Invalidate(); };
                Controls.Add(Input);
                Plus = Make("", delegate { window.OpenMenu("add"); });
                Plus.Glyph = 4;
                Plus.AccessibleName = "Add folders or files";
                Mode = Make("Ask me", delegate { window.OpenMenu("mode"); });
                Mode.Chevron = true;
                Mode.Caption = "MODE";
                Mode.AccessibleName = "Permission mode";
                Model = Make("Default", delegate { window.OpenMenu("model"); });
                Model.Chevron = true;
                Model.Caption = "MODEL";
                Model.AccessibleName = "Claude model";
                Send = Make("Send", delegate { window.Send(); });
                Send.Accent = Neon.Cyan;
                Send.Solid = true;
                Stop = Make("Stop", delegate { window.Stop(); });
                Stop.Accent = Neon.Hot;
                Stop.Visible = false;
            }

            private WorkKey Make(string text, EventHandler click)
            {
                WorkKey key = new WorkKey { Text = text };
                key.Click += click;
                Controls.Add(key);
                return key;
            }

            internal void ApplyFonts()
            {
                Input.Font = window.Font;
                foreach (WorkKey key in new[] { Plus, Mode, Model, Send, Stop }) { key.Font = window.keys; key.CaptionFont = window.caption; }
                Input.CueFont = window.Font;
                inputTall = 0;
            }

            internal void Fill(WorkspaceChat chat)
            {
                chips.Clear();
                if (chat != null)
                {
                    List<string> folders = Workspace.AllFolders(chat);
                    for (int i = 0; i < folders.Count; i++) chips.Add(new Chip { Path = folders[i], Name = Named(folders[i]), Folder = true, Main = i == 0 });
                    lock (chat.Gate) foreach (string file in chat.Files) chips.Add(new Chip { Path = file, Name = Named(file), Sent = chat.Sent.Contains(file) });
                    Mode.Text = chat.Mode;
                    Model.Text = chat.Model;
                }
                Arrange();
                Invalidate();
            }

            private static string Named(string path)
            {
                string name = System.IO.Path.GetFileName(path.TrimEnd('\\', '/'));
                return name.Length == 0 ? path : name;
            }

            internal List<string> ChipNames()
            {
                List<string> names = new List<string>();
                foreach (Chip chip in chips) names.Add((chip.Folder ? "folder " : "file ") + chip.Name + (chip.Main ? " (runs here)" : "") + (chip.Sent ? " (sent)" : ""));
                return names;
            }

            internal void Running(bool running, bool canSend)
            {
                Stop.Visible = running;
                Send.Visible = !running;
                Send.Enabled = canSend;
                Arrange();
                Invalidate();
            }

            private int Pad { get { return window.Z(12); } }

            private bool Grow()
            {
                int lines = Input.IsHandleCreated ? Input.GetLineFromCharIndex(Math.Max(0, Input.TextLength)) + 1 : 1;
                if (Input.TextLength > 0 && Input.Text.EndsWith("\n", StringComparison.Ordinal)) lines = Math.Max(lines, Input.Lines.Length);
                int tall = Input.Font.Height * Math.Max(2, Math.Min(8, lines)) + window.Z(4);
                if (tall == inputTall) return false;
                inputTall = tall;
                return true;
            }

            // Chips wrap onto up to three rows; past that the last one says how many more there are.
            internal void Arrange()
            {
                if (window.small == null) return;
                if (inputTall == 0) Grow();
                int pad = Pad, x = pad, y = pad, rowTall = window.Z(24), gap = window.Z(6), rows = chips.Count > 0 ? 1 : 0, right = Width - pad;
                foreach (Chip chip in chips)
                {
                    int wide = Math.Min(Math.Max(window.Z(60), right - pad), window.Z(30) + WorkPaint.Wide(chip.Name, window.small) + (chip.Main ? WorkPaint.Wide("runs here", window.caption) + window.Z(10) : window.Z(20)));
                    wide = Math.Min(wide, window.Z(260));
                    if (x + wide > right && x > pad) { x = pad; y += rowTall + gap; rows++; }
                    chip.Box = rows > 3 ? Rectangle.Empty : new Rectangle(x, y, wide, rowTall);
                    chip.Cross = chip.Main || chip.Box.IsEmpty ? Rectangle.Empty : new Rectangle(chip.Box.Right - window.Z(20), chip.Box.Top, window.Z(18), rowTall);
                    x += wide + gap;
                }
                chipsTall = chips.Count == 0 ? 0 : Math.Min(rows, 3) * (rowTall + gap);
                Input.Bounds = new Rectangle(pad, pad + chipsTall, Math.Max(1, Width - pad * 2 - window.Z(6)), inputTall);
                int keyTop = Input.Bottom + window.Z(10), keyTall = window.Z(30);
                Plus.Bounds = new Rectangle(pad, keyTop, keyTall, keyTall);
                Mode.Bounds = new Rectangle(Plus.Right + window.Z(8), keyTop, Math.Max(window.Z(148), Mode.Needs), keyTall);
                Model.Bounds = new Rectangle(Mode.Right + window.Z(8), keyTop, Math.Max(window.Z(132), Model.Needs), keyTall);
                Send.Bounds = Stop.Bounds = new Rectangle(Width - pad - window.Z(86), keyTop, window.Z(86), keyTall);
                hintShown = window.chat != null && window.chat.Running && WorkPaint.Wide(Hint, window.note) <= Stop.Left - Model.Right - window.Z(32);
                Wanted = keyTop + keyTall + pad;
            }

            internal void Cross(string path)
            {
                Chip chip = chips.Find(delegate(Chip found) { return found.Path == path; });
                if (chip != null && !chip.Main) window.RemovePath(path);
            }

            public void PaintGround(Graphics graphics, Rectangle area)
            {
                WorkPaint.Behind(graphics, this);
                float s = window.zoom, line = Math.Max(1f, s);
                WorkPaint.Plate(graphics, new RectangleF(line / 2, line / 2, Width - line, Height - line), 9 * s, Neon.Field, Input.Focused ? Neon.Cyan : Neon.Edge, line);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { PaintGround(e.Graphics, ClientRectangle); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                float s = window.zoom, line = Math.Max(1f, s);
                int extra = 0;
                foreach (Chip chip in chips)
                {
                    if (chip.Box.IsEmpty) { extra++; continue; }
                    bool over = hover >= 0 && chips[hover] == chip;
                    Color rim = chip.Main ? Neon.Tone(28, 84, 92) : chip.Folder ? Neon.Edge : Neon.Tone(70, 40, 58);
                    WorkPaint.Plate(graphics, chip.Box, 5 * s, over ? Neon.Hover : chip.Main ? Neon.Deep : Neon.Ground, over ? Neon.Hot : rim, line);
                    RectangleF mark = new RectangleF(chip.Box.Left + window.Z(8), chip.Box.Top + window.Z(6), window.Z(13), chip.Box.Height - window.Z(12));
                    Color ink = chip.Sent ? Neon.Faint : chip.Main ? Neon.Cyan : Neon.White;
                    if (chip.Folder) WorkPaint.FolderMark(graphics, mark, chip.Main ? Neon.Cyan : Neon.Faint, s); else WorkPaint.FileMark(graphics, mark, chip.Sent ? Neon.Faint : Neon.Cyan, s);
                    int textLeft = chip.Box.Left + window.Z(26);
                    int textRight = chip.Main ? chip.Box.Right - WorkPaint.Wide("runs here", window.caption) - window.Z(12) : chip.Cross.Left;
                    TextRenderer.DrawText(graphics, chip.Name, window.small, new Rectangle(textLeft, chip.Box.Top, Math.Max(1, textRight - textLeft), chip.Box.Height), ink, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    if (chip.Main) TextRenderer.DrawText(graphics, "runs here", window.caption, new Rectangle(textRight + window.Z(4), chip.Box.Top, chip.Box.Right - textRight - window.Z(4), chip.Box.Height), Neon.Faint, WorkPaint.Line);
                    else WorkPaint.Cross(graphics, new PointF(chip.Cross.Left + chip.Cross.Width / 2f - window.Z(1), chip.Cross.Top + chip.Cross.Height / 2f), window.Z(3.5f), over ? Neon.Hot : Neon.Faint, s);
                }
                if (extra > 0)
                {
                    Chip last = null;
                    foreach (Chip chip in chips) if (!chip.Box.IsEmpty) last = chip;
                    if (last != null) TextRenderer.DrawText(graphics, "+" + extra + " more", window.note, new Rectangle(last.Box.Right + window.Z(6), last.Box.Top, window.Z(70), last.Box.Height), Neon.Faint, WorkPaint.Line);
                }
                if (Input.Overflows)
                {
                    Rectangle rail = new Rectangle(Width - Pad - window.Z(2), Input.Top, Math.Max(2, window.Z(3)), Input.Height);
                    WorkPaint.Rail(graphics, rail, Input.Visible_Lines, Input.Line_Count, Input.First_Line, false);
                }
                if (hintShown) TextRenderer.DrawText(graphics, Hint, window.note, new Rectangle(Model.Right + window.Z(12), Model.Top, Stop.Left - Model.Right - window.Z(20), Model.Height), Neon.Faint, WorkPaint.Line | TextFormatFlags.Right);
            }

            private int ChipAt(Point point)
            {
                for (int i = 0; i < chips.Count; i++) if (chips[i].Box.Contains(point)) return i;
                return -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int over = ChipAt(e.Location);
                Cursor = over >= 0 ? Cursors.Hand : e.Y < Input.Bottom ? Cursors.IBeam : Cursors.Default;
                if (over != hover) { hover = over; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hover >= 0) { hover = -1; Invalidate(); } }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                int at = ChipAt(e.Location);
                if (at < 0) { Input.Focus(); return; }
                Chip chip = chips[at];
                if (chip.Cross.Contains(e.Location)) window.RemovePath(chip.Path);
                else if (chip.Folder) window.OpenFolder(chip.Path, null);
                else window.OpenFolder(null, chip.Path);
            }

            internal string Crowded()
            {
                if (!Visible) return null;
                List<Control> seen = new List<Control>();
                foreach (Control control in Controls)
                {
                    if (!control.Visible) continue;
                    if (!ClientRectangle.Contains(control.Bounds)) return "Composer control outside: " + (control.AccessibleName ?? control.Text);
                    string cut = Clipped(control);
                    if (cut != null) return cut;
                    foreach (Control other in seen) if (control.Bounds.IntersectsWith(other.Bounds)) return "Composer overlap: " + (control.AccessibleName ?? control.Text) + " / " + (other.AccessibleName ?? other.Text);
                    seen.Add(control);
                }
                foreach (Chip chip in chips)
                {
                    if (chip.Box.IsEmpty) continue;
                    if (!ClientRectangle.Contains(chip.Box)) return "Composer chip outside: " + chip.Name;
                    if (chip.Box.IntersectsWith(Input.Bounds)) return "Composer chip over the message box: " + chip.Name;
                }
                return null;
            }
        }

        // The message box: no border and no scroll bar of its own (her thin thumb shows beside it), and a faint cue while empty,
        // which a multi-line Windows box won't draw by itself.
        private sealed class MessageField : TextBox
        {
            internal string Cue = "";
            internal Font CueFont;
            internal Action Scrolled;

            internal bool Overflows { get { return IsHandleCreated && Line_Count > Visible_Lines; } }
            internal int Line_Count { get { return IsHandleCreated ? Native.SendMessage(Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero).ToInt32() : 1; } }
            internal int First_Line { get { return IsHandleCreated ? Native.SendMessage(Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32() : 0; } }
            internal int Visible_Lines { get { return Math.Max(1, ClientSize.Height / Math.Max(1, Font.Height)); } }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x000F && TextLength == 0 && Cue.Length > 0 && CueFont != null)
                    using (Graphics graphics = CreateGraphics())
                        TextRenderer.DrawText(graphics, Cue, CueFont, new Rectangle(1, 0, ClientSize.Width, Font.Height + 2), Neon.Faint, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                if ((message.Msg == 0x020A || message.Msg == 0x0115 || message.Msg == 0x0100) && Scrolled != null) Scrolled();
            }
        }

        // A question asked inside the window, in her look: the words, the step in red or cyan, and the way back out.
        private sealed class AskCard : Control
        {
            private readonly WorkspaceWindow window;
            private readonly WorkKey yes, no;
            private Action<bool> answered;
            internal string Question = "";

            internal AskCard(WorkspaceWindow window)
            {
                this.window = window;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Visible = false;
                AccessibleRole = AccessibleRole.Dialog;
                yes = new WorkKey();
                yes.Click += delegate { Answer(true); };
                no = new WorkKey { Accent = Neon.Faint };
                no.Click += delegate { Answer(false); };
                Controls.Add(yes);
                Controls.Add(no);
            }

            internal void Ask(string question, string step, Color accent, string back, Action<bool> then)
            {
                Question = question;
                AccessibleName = question;
                answered = then;
                yes.Text = step;
                yes.Accent = accent;
                no.Text = back;
                Visible = true;
                Place();
                BringToFront();
                no.Focus();
            }

            internal void Answer(bool choice)
            {
                Action<bool> then = answered;
                answered = null;
                Visible = false;
                window.Invalidate(true);
                if (then != null) then(choice);
            }

            internal void Place()
            {
                if (!Visible) return;
                yes.Font = no.Font = window.keys;
                int wide = Math.Min(window.ClientSize.Width - window.Z(80), window.Z(460));
                int words = WorkPaint.Tall(Question, window.Font, wide - window.Z(48));
                int tall = window.Z(64) + words + window.Z(52);
                Rectangle area = window.chat != null ? new Rectangle(window.Z(262), window.Z(116), window.ClientSize.Width - window.Z(282), window.ClientSize.Height - window.Z(136)) : window.ClientRectangle;
                Bounds = new Rectangle(area.Left + (area.Width - wide) / 2, area.Top + Math.Max(0, (area.Height - tall) / 3), wide, tall);
                int keyTop = Height - window.Z(48);
                no.Bounds = new Rectangle(Width - window.Z(24) - Math.Max(window.Z(110), no.Needs), keyTop, Math.Max(window.Z(110), no.Needs), window.Z(30));
                yes.Bounds = new Rectangle(no.Left - window.Z(10) - Math.Max(window.Z(110), yes.Needs), keyTop, Math.Max(window.Z(110), yes.Needs), window.Z(30));
                using (GraphicsPath hull = Neon.Cut(new RectangleF(0, 0, Width, Height), window.Z(12), window.Z(4), window.Z(12), window.Z(4)))
                {
                    Region old = Region;
                    Region = new Region(hull);
                    if (old != null) old.Dispose();
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                float s = window.zoom;
                Neon.Backdrop(graphics, Size, window.Z(120), Neon.MenuBack, Neon.Glow);
                Neon.Frame(graphics, Size, s, window.Z(12), window.Z(4), window.Z(40), window.Z(22), Neon.Brim, yes.Accent == Neon.Hot ? Neon.Hot : Neon.Cyan);
                TextRenderer.DrawText(graphics, "ARE YOU SURE?", window.caption, new Rectangle(window.Z(24), window.Z(18), Width - window.Z(48), window.Z(16)), yes.Accent == Neon.Hot ? Neon.Hot : Neon.Cyan, WorkPaint.Line);
                TextRenderer.DrawText(graphics, Question, window.Font, new Rectangle(window.Z(24), window.Z(42), Width - window.Z(48), Height - window.Z(96)), Neon.White, WorkPaint.Wrap);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }
        }
        // The conversation, drawn by hand in her fonts: Bahnschrift for words and her own mono for code. Their messages sit
        // in teal plates on the right; Claude's words run down the left beside a rose line, with code in dark wells. Tools are
        // one quiet row each that opens to show the result, and approvals, trust and undo questions are plates with real keys.
        // A message is picked with a click and copied with Ctrl+C or the right-click menu. Only the 150 items in view are laid
        // out, and only the cards waiting on an answer hold controls.
        private sealed class Transcript : Control, WorkGround
        {
            internal sealed class Block
            {
                internal WorkspaceItem Item;
                internal string Result = "Waiting for a result.";
                internal bool Open, All;
                internal string Shape = "";
                internal Rectangle Box, Toggle;
                internal readonly List<Piece> Pieces = new List<Piece>();
                internal readonly List<Control> Keys = new List<Control>();
                internal WorkField Reason;
            }

            internal sealed class Piece
            {
                internal string Text;
                internal Font Font;
                internal Color Ink;
                internal Rectangle Box;
                internal bool Code, Line, Right;
                internal string Tag;
            }

            private readonly WorkspaceWindow window;
            internal readonly List<Block> Blocks = new List<Block>();
            private readonly Dictionary<WorkspaceItem, Block> known = new Dictionary<WorkspaceItem, Block>();
            internal readonly WorkKey Earlier, Newer;
            internal int Offset, Content;
            internal bool Following = true;
            internal string Empty = "";
            private Block picked, hovered;
            private bool dragging, earlier, newer;
            private int dragFrom, dragOffset, laid = -1;

            internal Transcript(WorkspaceWindow window)
            {
                this.window = window;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
                TabStop = true;
                AccessibleName = "Conversation";
                AccessibleRole = AccessibleRole.List;
                Earlier = new WorkKey { Text = "Show 150 earlier", Accent = Neon.Cyan, Visible = false };
                Earlier.Click += delegate { window.Page(true); };
                Newer = new WorkKey { Text = "Show newer", Accent = Neon.Cyan, Visible = false };
                Newer.Click += delegate { window.Page(false); };
                Controls.Add(Earlier);
                Controls.Add(Newer);
            }

            private int Z(int value) { return window.Z(value); }
            private int Z(float value) { return window.Z(value); }
            internal bool AtBottom { get { return Offset + Height >= Content - Z(28); } }

            internal void ApplyFonts()
            {
                Earlier.Font = Newer.Font = window.keys;
                foreach (Block block in Blocks)
                {
                    foreach (Control key in block.Keys) key.Font = window.keys;
                    if (block.Reason != null) block.Reason.Font = window.Font;
                }
                laid = -1;
            }

            internal void Clear()
            {
                foreach (Block block in Blocks) Drop(block);
                Blocks.Clear();
                known.Clear();
                picked = hovered = null;
                Offset = 0;
                laid = -1;
            }

            private void Drop(Block block)
            {
                foreach (Control key in block.Keys) { Controls.Remove(key); key.Dispose(); }
                block.Keys.Clear();
                if (block.Reason != null) { Controls.Remove(block.Reason); block.Reason.Dispose(); block.Reason = null; }
                block.Shape = "";
            }

            internal void Fill(List<WorkspaceItem> shown, Dictionary<string, string> results, bool hasEarlier, bool hasNewer)
            {
                bool follow = Following;
                int was = Offset;
                HashSet<WorkspaceItem> keep = new HashSet<WorkspaceItem>(shown);
                foreach (Block old in Blocks)
                {
                    if (keep.Contains(old.Item)) continue;
                    known.Remove(old.Item);
                    Drop(old);
                    if (picked == old) picked = null;
                    if (hovered == old) hovered = null;
                }
                Blocks.Clear();
                foreach (WorkspaceItem item in shown)
                {
                    Block block;
                    if (!known.TryGetValue(item, out block)) { block = new Block { Item = item }; known.Add(item, block); }
                    string result;
                    if (item.Kind == "tool" && results.TryGetValue(item.Turn + "/" + item.Id, out result)) block.Result = result;
                    Blocks.Add(block);
                    Equip(block);
                }
                earlier = hasEarlier;
                newer = hasNewer;
                Earlier.Visible = earlier;
                Newer.Visible = newer;
                Arrange(true);
                Following = follow;
                ScrollTo(follow ? int.MaxValue : was);
                Following = follow;
            }

            private static bool Waiting(WorkspaceItem item)
            {
                return item.Kind == "approval" && item.Decision.Length == 0 && item.Ask != null
                    || item.Kind == "trust" && item.Decision.Length == 0
                    || item.Kind == "undo-ask" && item.Undo != null;
            }

            // The keys a card needs right now: Allow, Always allow and Deny with a reason box while an approval waits, Trust and
            // Not now on the trust card, the two undo choices, and Retry on an error that can rerun.
            private void Equip(Block block)
            {
                WorkspaceItem item = block.Item;
                string shape = Waiting(item) ? item.Kind + "/wait" : item.Retry ? "retry" : "";
                if (shape == block.Shape) return;
                Drop(block);
                block.Shape = shape;
                if (shape.Length == 0) return;
                WorkspaceChat chat = window.chat;
                if (shape == "retry") { Key(block, "Retry", Neon.Cyan, delegate { window.Try(delegate { window.workspace.Retry(chat); window.SyncItems(); window.UpdateKeys(); }); }); return; }
                if (item.Kind == "approval")
                {
                    Key(block, "Allow", Neon.Cyan, delegate { Answer(block, true, false); });
                    Key(block, "Always allow " + item.Name + " in this chat", Neon.Rose, delegate { Answer(block, true, true); });
                    Key(block, "Deny", Neon.Hot, delegate { Answer(block, false, false); });
                    block.Reason = new WorkField { Font = window.Font, Cue = "Why not? (optional, Claude Code sees it)" };
                    block.Reason.Box.AccessibleName = "Deny reason (optional)";
                    block.Reason.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == System.Windows.Forms.Keys.Enter) { e.SuppressKeyPress = true; Answer(block, false, false); } };
                    Controls.Add(block.Reason);
                }
                else if (item.Kind == "trust")
                {
                    Key(block, item.Text.StartsWith("Claude Code will run these", StringComparison.Ordinal) ? "Trust them" : "Trust", Neon.Cyan, delegate { window.Try(delegate { window.workspace.Trust(chat, true); window.RefreshChat(); }); });
                    Key(block, "Not now", Neon.Faint, delegate { window.Try(delegate { window.workspace.Trust(chat, false); window.RefreshChat(); }); });
                }
                else
                {
                    Key(block, "Undo all", Neon.Cyan, delegate { window.Try(delegate { window.workspace.AnswerUndo(chat, item.Undo, true); window.RefreshChat(); }); });
                    Key(block, "Only the unchanged ones", Neon.Faint, delegate { window.Try(delegate { window.workspace.AnswerUndo(chat, item.Undo, false); window.RefreshChat(); }); });
                }
            }

            private void Key(Block block, string text, Color accent, EventHandler click)
            {
                WorkKey key = new WorkKey { Text = text, Accent = accent, Font = window.keys };
                key.Click += click;
                block.Keys.Add(key);
                Controls.Add(key);
            }

            private void Answer(Block block, bool yes, bool always)
            {
                WorkspaceApproval ask = block.Item.Ask;
                if (ask == null) return;
                string reason = block.Reason == null ? "" : block.Reason.Box.Text;
                window.Try(delegate
                {
                    if (window.workspace.Answer(window.chat, ask, yes, always, reason))
                    {
                        foreach (Control key in block.Keys) key.Enabled = false;
                        if (block.Reason != null) block.Reason.Enabled = false;
                    }
                });
            }

            private int Inset { get { return Z(8); } }
            private int Edge { get { return Width - Z(16); } }

            internal void Arrange(bool force)
            {
                if (!force && laid == Width) return;
                laid = Width;
                int y = Z(10);
                if (earlier || newer)
                {
                    int x = Inset;
                    if (earlier) { Earlier.Bounds = new Rectangle(x, y, Math.Max(Z(150), Earlier.Needs), Z(28)); x = Earlier.Right + Z(8); }
                    if (newer) Newer.Bounds = new Rectangle(x, y, Math.Max(Z(110), Newer.Needs), Z(28));
                    y += Z(40);
                }
                foreach (Block block in Blocks)
                {
                    Measure(block, y);
                    y = block.Box.Bottom + Z(block.Item.Kind == "tool" || block.Item.Kind == "end" ? 6 : 14);
                }
                Content = y + Z(6);
                Place();
            }

            private Piece Add(Block block, string text, Font font, Color ink, Rectangle box)
            {
                Piece piece = new Piece { Text = text, Font = font, Ink = ink, Box = box };
                block.Pieces.Add(piece);
                return piece;
            }

            // Words wrapped to a width, top at y; returns the bottom.
            private int Words(Block block, string text, Font font, Color ink, int x, int y, int width)
            {
                int tall = WorkPaint.Tall(text, font, width);
                Add(block, text, font, ink, new Rectangle(x, y, width, tall));
                return y + tall;
            }

            // A code well: her mono font (or Windows' own when the code has letters hers lacks) on a dark well with a cut corner.
            private int Code(Block block, string text, string tag, int x, int y, int width)
            {
                Font font = Look.Drawable(text) ? window.mono : window.monoPlain;
                int pad = Z(10), top = tag.Length > 0 ? Z(16) : 0;
                int tall = WorkPaint.Tall(text.Length == 0 ? " " : text, font, width - pad * 2);
                Piece piece = Add(block, text, font, Neon.White, new Rectangle(x, y, width, tall + pad * 2 + top));
                piece.Code = true;
                piece.Tag = tag;
                return piece.Box.Bottom;
            }

            // Claude's words: paragraphs, headings and lists in Bahnschrift, fenced code in wells.
            private int Markdown(Block block, string text, int x, int y, int width)
            {
                List<string> lines = new List<string>(text.Replace("\r", "").Split('\n'));
                StringBuilder prose = new StringBuilder(), code = new StringBuilder();
                bool fenced = false, any = false;
                string tag = "";
                Action flush = delegate
                {
                    string paragraph = prose.ToString().Trim('\n');
                    prose.Length = 0;
                    if (paragraph.Length == 0) return;
                    if (any) y += Z(6);
                    y = Words(block, paragraph, window.Font, Neon.White, x, y, width);
                    any = true;
                };
                foreach (string raw in lines)
                {
                    string line = raw;
                    if (line.TrimStart().StartsWith("```", StringComparison.Ordinal))
                    {
                        if (!fenced) { flush(); fenced = true; tag = line.Trim().Substring(3).Trim(); code.Length = 0; }
                        else
                        {
                            fenced = false;
                            if (any) y += Z(8);
                            y = Code(block, code.ToString().TrimEnd('\n'), tag, x, y, width);
                            any = true;
                        }
                        continue;
                    }
                    if (fenced) { code.Append(line).Append('\n'); continue; }
                    if (line.Trim().Length == 0) { flush(); continue; }
                    Match head = Regex.Match(line, @"^\s*#{1,6}\s+(.*)$");
                    if (head.Success)
                    {
                        flush();
                        if (any) y += Z(8);
                        y = Words(block, Plain(head.Groups[1].Value), window.strong, Neon.White, x, y, width);
                        any = true;
                        continue;
                    }
                    Match bullet = Regex.Match(line, @"^(\s*)[-*+]\s+(.*)$");
                    if (bullet.Success) line = bullet.Groups[1].Value + "• " + bullet.Groups[2].Value;
                    if (prose.Length > 0) prose.Append('\n');
                    prose.Append(Plain(line));
                }
                if (fenced) { if (any) y += Z(8); y = Code(block, code.ToString().TrimEnd('\n'), tag, x, y, width); any = true; }
                flush();
                if (!any) y = Words(block, " ", window.Font, Neon.White, x, y, width);
                return y;
            }

            private static string Plain(string line) { return line.Replace("**", ""); }

            private static string Lines(string input)
            {
                Dictionary<string, object> map = null;
                try { map = HuskJson.Read(input) as Dictionary<string, object>; } catch (FormatException) { }
                if (map == null) return input;
                StringBuilder text = new StringBuilder();
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (text.Length > 0) text.Append('\n');
                    text.Append(pair.Key).Append(": ").Append(pair.Value as string ?? HuskJson.Write(pair.Value));
                }
                return text.ToString();
            }

            private int KeysRow(Block block, int x, int y, int width)
            {
                int left = x;
                foreach (Control control in block.Keys)
                {
                    WorkKey key = (WorkKey)control;
                    int wide = Math.Min(width, Math.Max(Z(86), key.Needs));
                    if (left > x && left + wide > x + width) { left = x; y += Z(38); }
                    key.Tag = new Rectangle(left, y, wide, Z(30));
                    left += wide + Z(8);
                }
                return y + Z(30);
            }

            private void Measure(Block block, int y)
            {
                block.Pieces.Clear();
                block.Toggle = Rectangle.Empty;
                WorkspaceItem item = block.Item;
                int left = Inset, right = Edge, width = right - left, pad = Z(14);
                string kind = item.Kind;
                if (kind == "user")
                {
                    int most = Math.Max(Z(220), (int)(width * 0.8f));
                    int textWide = 0;
                    foreach (string line in item.Text.Replace("\r", "").Split('\n')) textWide = Math.Max(textWide, WorkPaint.Wide(line, window.Font));
                    List<string> files = new List<string>();
                    foreach (string file in item.Files.Split('\n')) if (file.Length > 0) files.Add(file);
                    foreach (string file in files) textWide = Math.Max(textWide, Z(20) + WorkPaint.Wide(Path.GetFileName(file), window.note));
                    int wide = Math.Min(most, Math.Max(Z(120), textWide + pad * 2 + Z(4)));
                    int x = right - wide, top = y + Z(10);
                    Add(block, "YOU", window.caption, Neon.Cyan, new Rectangle(x + pad, top, wide - pad * 2, Z(14))).Line = true;
                    int bottom = Words(block, item.Text, window.Font, Neon.White, x + pad, top + Z(20), wide - pad * 2);
                    foreach (string file in files)
                    {
                        Piece chip = Add(block, Path.GetFileName(file), window.note, Neon.Faint, new Rectangle(x + pad, bottom + Z(6), wide - pad * 2, Z(16)));
                        chip.Line = true;
                        chip.Tag = "file";
                        bottom = chip.Box.Bottom;
                    }
                    block.Box = new Rectangle(x, y, wide, bottom + Z(12) - y);
                    return;
                }
                if (kind == "text")
                {
                    Add(block, "CLAUDE", window.caption, Neon.Rose, new Rectangle(left + pad, y, width - pad, Z(14))).Line = true;
                    int bottom = Markdown(block, item.Text, left + pad, y + Z(20), width - pad);
                    block.Box = new Rectangle(left, y, width, bottom - y);
                    return;
                }
                if (kind == "tool")
                {
                    block.Toggle = new Rectangle(left, y, width, Z(28));
                    int bottom = block.Toggle.Bottom;
                    if (block.Open) bottom = Code(block, block.Result, "", left + pad, bottom + Z(4), width - pad);
                    block.Box = new Rectangle(left, y, width, bottom - y);
                    return;
                }
                if (kind == "end")
                {
                    Piece piece = Add(block, item.Text.Replace(" | ", "  ·  "), window.note, Neon.Faint, new Rectangle(left, y, width, Z(16)));
                    piece.Line = true;
                    piece.Right = true;
                    block.Box = new Rectangle(left, y, width, Z(16));
                    return;
                }
                if (kind == "note" || kind == "stopped" || (kind == "approval" || kind == "trust") && !Waiting(item))
                {
                    int bottom = Words(block, item.Text, window.note, kind == "approval" && item.Decision == "deny" ? Neon.Tone(255, 166, 162) : Neon.Faint, left + Z(22), y, width - Z(22));
                    block.Box = new Rectangle(left, y, width, Math.Max(Z(16), bottom - y));
                    return;
                }
                int inner = width - pad * 2, at = y + pad;
                string label = kind == "approval" ? "NEEDS YOUR OK" : kind == "trust" ? "TRUST" : kind == "error" ? "ERROR" : kind == "undo" || kind == "undo-ask" ? "UNDO" : kind.ToUpperInvariant();
                Color tone = kind == "approval" || kind == "error" ? Neon.Hot : kind == "trust" ? Neon.Cyan : Neon.Amber;
                Add(block, label, window.caption, tone, new Rectangle(left + pad, at, inner, Z(14))).Line = true;
                at += Z(20);
                if (kind == "approval")
                {
                    at = Words(block, "Claude Code wants to use " + item.Name, window.strong, Neon.White, left + pad, at, inner) + Z(4);
                    if (item.Text.Length > 0 && item.Text != item.Name) at = Words(block, item.Text, window.small, Neon.Faint, left + pad, at, inner) + Z(8);
                    string shown = Lines(item.Input);
                    string[] rows = shown.Split('\n');
                    bool longer = rows.Length > 12 || shown.Length > 900;
                    if (longer && !block.All)
                    {
                        shown = string.Join("\n", rows, 0, Math.Min(rows.Length, 12));
                        if (shown.Length > 900) shown = shown.Substring(0, 900);
                        shown += "\n...";
                    }
                    at = Code(block, shown, "", left + pad, at, inner);
                    if (longer)
                    {
                        string toggle = block.All ? "Show less" : "Show all of it";
                        block.Toggle = new Rectangle(left + pad, at + Z(4), WorkPaint.Wide(toggle, window.small) + Z(4), Z(18));
                        Piece link = Add(block, toggle, window.small, Neon.Cyan, block.Toggle);
                        link.Line = true;
                        at = block.Toggle.Bottom;
                    }
                    at += Z(10);
                    if (block.Reason != null) { block.Reason.Tag = new Rectangle(left + pad, at, inner, Z(30)); at += Z(38); }
                }
                else at = Words(block, item.Text, window.Font, Neon.White, left + pad, at, inner) + Z(10);
                if (block.Keys.Count > 0) at = KeysRow(block, left + pad, at, inner) + Z(2);
                block.Box = new Rectangle(left, y, width, at + pad - Z(4) - y);
            }

            // Moves the cards' keys and boxes to where their card is on screen now.
            private void Place()
            {
                Offset = Math.Max(0, Math.Min(Offset, Content - Height));
                if (earlier) Earlier.Top = Z(10) - Offset;
                if (newer) Newer.Top = Z(10) - Offset;
                foreach (Block block in Blocks)
                {
                    foreach (Control key in block.Keys) if (key.Tag is Rectangle) { Rectangle at = (Rectangle)key.Tag; at.Offset(0, -Offset); key.Bounds = at; }
                    if (block.Reason != null && block.Reason.Tag is Rectangle) { Rectangle at = (Rectangle)block.Reason.Tag; at.Offset(0, -Offset); block.Reason.Bounds = at; }
                }
                Invalidate();
            }

            internal void ScrollTo(int to)
            {
                int target = Math.Max(0, Math.Min(to, Content - Height));
                if (target != Offset) { Offset = target; Place(); }
                Following = AtBottom;
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                if (window.small == null) return;
                bool follow = Following;
                Arrange(false);
                ScrollTo(follow ? int.MaxValue : Offset);
            }

            public void PaintGround(Graphics graphics, Rectangle area) { Draw(graphics, area); }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                Draw(e.Graphics, e.ClipRectangle);
                if (Blocks.Count == 0 && Empty.Length > 0)
                    TextRenderer.DrawText(e.Graphics, Empty, window.note, new Rectangle(Inset + Z(14), Z(14), Edge - Inset - Z(14), Z(60)), Neon.Faint, WorkPaint.Wrap | TextFormatFlags.PathEllipsis);
                WorkPaint.Rail(e.Graphics, new Rectangle(Width - Z(5), Z(4), Math.Max(2, Z(3)), Height - Z(8)), Height, Content, Offset, dragging);
            }

            private void Draw(Graphics graphics, Rectangle area)
            {
                WorkPaint.Behind(graphics, this);
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(0, -Offset);
                Rectangle view = new Rectangle(area.Left, area.Top + Offset, area.Width, area.Height);
                foreach (Block block in Blocks) if (block.Box.IntersectsWith(Rectangle.Inflate(view, 0, Z(8)))) PaintBlock(graphics, block);
                graphics.Restore(state);
            }

            private void PaintBlock(Graphics graphics, Block block)
            {
                float s = window.zoom, line = Math.Max(1f, s);
                WorkspaceItem item = block.Item;
                string kind = item.Kind;
                if (kind == "user") WorkPaint.Plate(graphics, block.Box, 8 * s, Neon.Deep, Neon.Tone(28, 84, 92), line);
                else if (kind == "text") using (Brush bar = new SolidBrush(Neon.Rose)) graphics.FillRectangle(bar, block.Box.Left, block.Box.Top + Z(2), Math.Max(2, Z(2)), block.Box.Height - Z(2));
                else if (kind == "tool")
                {
                    if (block == hovered || block.Open) WorkPaint.Plate(graphics, block.Toggle, 5 * s, block == hovered ? Neon.Hover : Neon.Band, Color.Empty, 0);
                    float mid = block.Toggle.Top + block.Toggle.Height / 2f, x = block.Toggle.Left + Z(10);
                    PointF[] mark = block.Open
                        ? new[] { new PointF(x, mid - 2.5f * s), new PointF(x + 8 * s, mid - 2.5f * s), new PointF(x + 4 * s, mid + 2.5f * s) }
                        : new[] { new PointF(x + 1.5f * s, mid - 4 * s), new PointF(x + 6.5f * s, mid), new PointF(x + 1.5f * s, mid + 4 * s) };
                    SmoothingMode was = graphics.SmoothingMode;
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush fill = new SolidBrush(Neon.Cyan)) graphics.FillPolygon(fill, mark);
                    graphics.SmoothingMode = was;
                    int nameWide = WorkPaint.Wide(item.Name, window.keys);
                    TextRenderer.DrawText(graphics, item.Name, window.keys, new Rectangle(block.Toggle.Left + Z(26), block.Toggle.Top, nameWide + Z(2), block.Toggle.Height), Neon.Cyan, WorkPaint.Line);
                    string detail = item.Text.StartsWith(item.Name, StringComparison.Ordinal) ? item.Text.Substring(item.Name.Length).TrimStart(':', ' ') : item.Text;
                    int detailLeft = block.Toggle.Left + Z(34) + nameWide;
                    TextRenderer.DrawText(graphics, detail, window.note, new Rectangle(detailLeft, block.Toggle.Top, Math.Max(1, block.Toggle.Right - detailLeft - Z(8)), block.Toggle.Height), Neon.Faint, WorkPaint.Line | (item.Name == "Bash" || item.Name == "PowerShell" ? TextFormatFlags.EndEllipsis : TextFormatFlags.PathEllipsis));
                }
                else if (kind == "note" || kind == "stopped" || (kind == "approval" || kind == "trust") && !Waiting(item))
                {
                    bool yes = item.Decision == "allow", no = item.Decision == "deny";
                    PointF middle = new PointF(block.Box.Left + Z(10), block.Box.Top + Z(8));
                    if (no) WorkPaint.Cross(graphics, middle, Z(3.5f), Neon.Hot, s);
                    else
                        using (Brush dot = new SolidBrush(yes ? Neon.Cyan : Neon.Faint)) graphics.FillRectangle(dot, middle.X - Z(2.5f), middle.Y - Z(2.5f), Math.Max(3, Z(5)), Math.Max(3, Z(5)));
                }
                else if (kind != "end")
                {
                    Color rim = kind == "approval" || kind == "error" ? Neon.Hot : kind == "trust" ? Neon.Cyan : Neon.Tone(120, 96, 40);
                    Color fill = kind == "trust" ? Neon.Deep : kind == "error" ? Neon.Tone(30, 12, 20) : Neon.Field;
                    WorkPaint.Plate(graphics, block.Box, 9 * s, fill, rim, line);
                }
                if (block == picked) WorkPaint.Plate(graphics, Rectangle.Inflate(block.Box, Z(4), Z(4)), 10 * s, Color.Empty, Color.FromArgb(150, Neon.Cyan), line);
                foreach (Piece piece in block.Pieces)
                {
                    if (piece.Code)
                    {
                        WorkPaint.Plate(graphics, piece.Box, 6 * s, Neon.Well, Neon.WellEdge, line);
                        int pad = Z(10), top = 0;
                        if (!string.IsNullOrEmpty(piece.Tag)) { TextRenderer.DrawText(graphics, piece.Tag.ToUpperInvariant(), window.caption, new Rectangle(piece.Box.Left + pad, piece.Box.Top + Z(6), piece.Box.Width - pad * 2, Z(14)), Neon.Faint, WorkPaint.Line); top = Z(16); }
                        TextRenderer.DrawText(graphics, piece.Text, piece.Font, new Rectangle(piece.Box.Left + pad, piece.Box.Top + pad + top, piece.Box.Width - pad * 2, piece.Box.Height - pad * 2 - top), piece.Ink, WorkPaint.Wrap);
                        continue;
                    }
                    if (piece.Tag == "file") WorkPaint.FileMark(graphics, new RectangleF(piece.Box.Left, piece.Box.Top + Z(2), Z(12), piece.Box.Height - Z(4)), Neon.Cyan, s);
                    Rectangle box = piece.Tag == "file" ? new Rectangle(piece.Box.Left + Z(18), piece.Box.Top, piece.Box.Width - Z(18), piece.Box.Height) : piece.Box;
                    TextFormatFlags flags = piece.Line ? WorkPaint.Line | TextFormatFlags.EndEllipsis | (piece.Right ? TextFormatFlags.Right : TextFormatFlags.Left) : WorkPaint.Wrap;
                    TextRenderer.DrawText(graphics, piece.Text, piece.Font, box, piece.Ink, flags);
                }
            }

            private Block At(Point point, out Piece code)
            {
                code = null;
                Point inside = new Point(point.X, point.Y + Offset);
                foreach (Block block in Blocks)
                {
                    if (!Rectangle.Inflate(block.Box, 0, Z(4)).Contains(inside)) continue;
                    foreach (Piece piece in block.Pieces) if (piece.Code && piece.Box.Contains(inside)) code = piece;
                    return block;
                }
                return null;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging)
                {
                    RectangleF thumb = WorkPaint.Thumb(new Rectangle(0, Z(4), 1, Height - Z(8)), Height, Content, Offset);
                    float room = Height - Z(8) - thumb.Height;
                    if (room > 0) ScrollTo(dragOffset + (int)((e.Y - dragFrom) * (Content - Height) / room));
                    return;
                }
                Piece code;
                Block over = At(e.Location, out code);
                bool link = over != null && !over.Toggle.IsEmpty && over.Toggle.Contains(new Point(e.X, e.Y + Offset));
                Cursor = link ? Cursors.Hand : Cursors.Default;
                Block hot = link && over.Item.Kind == "tool" ? over : null;
                if (hot != hovered) { hovered = hot; Invalidate(); }
            }

            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (hovered != null) { hovered = null; Invalidate(); } }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                if (e.Button == MouseButtons.Left && e.X >= Width - Z(12) && Content > Height) { dragging = true; dragFrom = e.Y; dragOffset = Offset; Capture = true; Invalidate(); return; }
                Piece code;
                Block block = At(e.Location, out code);
                if (block != null && e.Button == MouseButtons.Left && !block.Toggle.IsEmpty && block.Toggle.Contains(new Point(e.X, e.Y + Offset)))
                {
                    if (block.Item.Kind == "tool") block.Open = !block.Open; else block.All = !block.All;
                    bool follow = Following;
                    int was = Offset;
                    Arrange(true);
                    ScrollTo(follow && block == Blocks[Blocks.Count - 1] ? int.MaxValue : was);
                    return;
                }
                if (block != picked) { picked = block; Invalidate(); }
                if (block != null && e.Button == MouseButtons.Right) CopyMenu(block, code, e.Location);
            }

            protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); if (dragging) { dragging = false; Capture = false; Invalidate(); } }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int lines = SystemInformation.MouseWheelScrollLines;
                int step = lines < 0 ? Height : Z(22) * Math.Max(1, lines);
                ScrollTo(Offset - e.Delta * step / 120);
            }

            protected override bool IsInputKey(Keys key) { return key == System.Windows.Forms.Keys.Up || key == System.Windows.Forms.Keys.Down || key == System.Windows.Forms.Keys.PageUp || key == System.Windows.Forms.Keys.PageDown || base.IsInputKey(key); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                base.OnKeyDown(e);
                if (e.Control && e.KeyCode == System.Windows.Forms.Keys.C && picked != null) { Copy(CopyOf(picked.Item)); e.Handled = true; return; }
                int page = Math.Max(Z(40), Height - Z(40));
                if (e.KeyCode == System.Windows.Forms.Keys.Down) ScrollTo(Offset + Z(40));
                else if (e.KeyCode == System.Windows.Forms.Keys.Up) ScrollTo(Offset - Z(40));
                else if (e.KeyCode == System.Windows.Forms.Keys.PageDown) ScrollTo(Offset + page);
                else if (e.KeyCode == System.Windows.Forms.Keys.PageUp) ScrollTo(Offset - page);
                else if (e.KeyCode == System.Windows.Forms.Keys.Home) ScrollTo(0);
                else if (e.KeyCode == System.Windows.Forms.Keys.End) ScrollTo(int.MaxValue);
            }

            private void CopyMenu(Block block, Piece code, Point at)
            {
                ContextMenuStrip menu = new ContextMenuStrip { Renderer = new Mainframe.MenuLook(), BackColor = Neon.Ground, ForeColor = Neon.White, ShowImageMargin = false };
                Font own = new Font(window.Font, FontStyle.Regular);
                menu.Font = own;
                menu.Items.Add("Copy message", null, delegate { Copy(CopyOf(block.Item)); });
                if (code != null) { string text = code.Text; menu.Items.Add("Copy code", null, delegate { Copy(text); }); }
                menu.Closed += delegate { window.Post(delegate { menu.Dispose(); own.Dispose(); }); };
                window.openMenu = menu;
                menu.Show(this, at);
            }

            private void Copy(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                try { Clipboard.SetText(text); }
                catch (ExternalException) { }
            }

            internal string CopyOf(WorkspaceItem item)
            {
                Block block;
                if (!known.TryGetValue(item, out block)) return null;
                if (item.Kind == "tool") return item.Text + "\n" + block.Result;
                if (item.Kind == "approval" && item.Input.Length > 0) return item.Text + "\n" + Lines(item.Input);
                return item.Text;
            }

            internal string Toggle(string id)
            {
                foreach (Block block in Blocks)
                {
                    if (block.Item.Kind != "tool" || block.Item.Id != id) continue;
                    block.Open = !block.Open;
                    Arrange(true);
                    foreach (Piece piece in block.Pieces) if (piece.Code) return piece.Text;
                    return "";
                }
                return null;
            }

            internal string Crowded()
            {
                if (!Visible) return null;
                foreach (Block block in Blocks)
                {
                    if (block.Box.Left < 0 || block.Box.Right > Width - Z(8)) return "Workspace item outside the conversation: " + block.Item.Kind + " " + block.Box + " in " + Width;
                    foreach (Piece piece in block.Pieces) if (!Rectangle.Inflate(block.Box, 1, 1).Contains(piece.Box)) return "Workspace item clipped: " + block.Item.Kind + " / " + piece.Text;
                    List<Rectangle> seen = new List<Rectangle>();
                    foreach (Control key in block.Keys)
                    {
                        Rectangle at = (Rectangle)key.Tag;
                        if (!block.Box.Contains(at)) return "Workspace key outside its card: " + key.Text;
                        string cut = Clipped(key);
                        if (cut != null) return cut;
                        foreach (Rectangle other in seen) if (other.IntersectsWith(at)) return "Workspace keys overlap on a card: " + key.Text;
                        seen.Add(at);
                    }
                    if (block.Reason != null && !block.Box.Contains((Rectangle)block.Reason.Tag)) return "Workspace reason box outside its card.";
                }
                return null;
            }
        }
    }
}
