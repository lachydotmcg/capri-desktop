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
        private readonly WorkKey shut, smaller, bigger, make, undo, trust;
        private readonly ToolTip tips;
        // Which tool groups are open, by their first call, for as long as the window lives.
        private readonly HashSet<WorkspaceItem> openGroups = new HashSet<WorkspaceItem>();
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
        private bool filling, saving, arranging, picking;
        private float zoom;
        private Font small, strong, keys, caption, mono, monoPlain, note, title, heading;
        private Rectangle titleBox, folderBox;
        private ToolStripDropDown openMenu;
        internal Func<string, bool> Confirm;
        internal Func<string[]> PickFolders, PickFiles;
        internal Func<string, string> PathAlias;
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
            tips = WorkPaint.Tips(delegate { return small; });
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
            undo = Key("Undo last edits", delegate { if (chat != null) Try(delegate { workspace.Undo(chat); SyncItems(); UpdateKeys(); }); });
            undo.Accent = Neon.Rose;
            undo.AccessibleName = "Undo Claude Code's file edits from the last turn";
            trust = Key("Trust folder", delegate { if (chat != null) Try(delegate { workspace.AskTrust(chat); SyncItems(); }); });
            trust.Accent = Neon.Cyan;
            list = new ChatList(this);
            Controls.Add(list);
            transcript = new Transcript(this, openGroups);
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
            tips.SetToolTip(composer.Mode, Composer.Hint);
            tips.SetToolTip(composer.Model, Composer.Hint);
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

        // With no chats yet, Workspace opens straight into a new chat in her folder, the same one + New chat gives. It waits
        // until the window loads, since the folder she uses can be set after the window is made.
        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            if (chat == null && workspace.Chats().Count == 0) NewChat();
        }

        // A new chat starts straight away in her own folder, ready to type in. A new chat there that hasn't been used yet is
        // opened again rather than making another. Its folder can still be changed from the "runs here" chip.
        internal void NewChat()
        {
            Try(delegate
            {
                WorkspaceChat unused = workspace.Chats().Find(delegate(WorkspaceChat found)
                {
                    lock (found.Gate) return found.Items.Count == 0 && found.Folders.Count == 0 && found.Files.Count == 0 && found.Live == null && workspace.IsHome(found.Folder);
                });
                WorkspaceChat made = unused ?? workspace.HomeChat(blackwall != null && blackwall());
                FillChats();
                SelectChat(made);
                composer.Input.Focus();
            });
        }

        // Nothing sent yet, so the folder Claude Code runs in can still change.
        internal bool CanRehome
        {
            get
            {
                if (chat == null) return false;
                lock (chat.Gate) return chat.Live == null && !chat.Items.Exists(delegate(WorkspaceItem item) { return item.Kind == "user"; });
            }
        }

        internal void ChooseMain()
        {
            if (!CanRehome || picking) return;
            picking = true;
            WorkspaceChat target = chat;
            string[] picked;
            try { picked = PickFolders != null ? PickFolders() : WorkPickers.Folders(this, "Choose the folder for this chat", StartFolder()); }
            catch (Exception error) { picked = null; Notice.Tell(this, "Couldn't open the folder picker: " + error.Message, "Workspace", true); }
            finally { picking = false; }
            if (picked == null || picked.Length == 0 || IsDisposed || chat != target) return;
            Try(delegate { workspace.Rehome(target, picked); });
            FillChats();
            FillHeader();
            Place();
            composer.Input.Focus();
        }

        private string StartFolder()
        {
            if (chat != null) return chat.Folder;
            List<WorkspaceChat> all = workspace.Chats();
            return all.Count > 0 ? Path.GetDirectoryName(all[0].Folder) : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        // An Explorer window pumps this window's messages while it's open, so a queued click could open a second one over it.
        internal void ChooseFolders()
        {
            if (picking) return;
            picking = true;
            string[] picked;
            try { picked = PickFolders != null ? PickFolders() : WorkPickers.Folders(this, chat == null ? "Choose a folder for this chat" : "Add folders to this chat", StartFolder()); }
            catch (Exception error) { picked = null; Notice.Tell(this, "Couldn't open the folder picker: " + error.Message, "Workspace", true); }
            finally { picking = false; }
            if (picked == null || picked.Length == 0 || IsDisposed) return;
            AddPaths(picked, new string[0]);
        }

        internal void ChooseFiles()
        {
            if (chat == null) { ChooseFolders(); return; }
            if (picking) return;
            picking = true;
            WorkspaceChat target = chat;
            string[] picked;
            try { picked = PickFiles != null ? PickFiles() : WorkPickers.Files(this, "Add files to this chat", chat.Folder); }
            catch (Exception error) { picked = null; Notice.Tell(this, "Couldn't open the file picker: " + error.Message, "Workspace", true); }
            finally { picking = false; }
            if (picked == null || picked.Length == 0 || IsDisposed || chat != target) return;
            AddPaths(new string[0], picked);
        }

        internal bool Picking { get { return picking; } }

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
                else if (rest.Count > 0 && CanRehome && workspace.IsHome(chat.Folder) && chat.Folders.Count == 0)
                {
                    // Nothing's been asked in her folder yet, so folders given now are where they want to work instead.
                    workspace.Rehome(chat, rest);
                    rest.Clear();
                    FillChats();
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

        // A path as it's shown: whole, or as the smoke test's pictures show their stand-in folders.
        internal string Display(string path) { return PathAlias != null && !string.IsNullOrEmpty(path) ? PathAlias(path) : path; }

        // Paths as the chat sees them: inside the folder Claude Code runs in they're relative, inside another of the chat's
        // folders they lead with that folder's name, and anywhere else they stay whole.
        // Everything Shorten and the trust card read, so the conversation knows when its cards need laying out again.
        internal string PathsSeen()
        {
            if (chat == null) return PathAlias == null ? "" : "alias";
            int untrusted;
            try { untrusted = workspace.Untrusted(chat).Count; } catch (Exception) { untrusted = -1; }
            return string.Join("|", Workspace.AllFolders(chat).ToArray()) + "|" + untrusted + (PathAlias == null ? "" : "|alias");
        }

        internal string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text) || chat == null) return Display(text);
            List<string> folders = Workspace.AllFolders(chat);
            List<string> longest = new List<string>(folders);
            longest.Sort(delegate(string a, string b) { return b.Length.CompareTo(a.Length); });
            foreach (string folder in longest)
            {
                string root = folder.TrimEnd('\\', '/');
                string name = Path.GetFileName(root);
                string lead = folder == folders[0] ? "" : (name.Length > 0 ? name : root) + "\\";
                text = Regex.Replace(text, Regex.Escape(root + "\\"), delegate(Match found) { return lead; }, RegexOptions.IgnoreCase);
            }
            return Display(text);
        }

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
            trust.Visible = untrusted && chat.TrustItem == null;
            Invalidate();
        }

        private void Send()
        {
            if (chat == null || chat.Running || chat.PendingText != null || chat.Undoing != null) return;
            if (string.IsNullOrWhiteSpace(composer.Input.Text) && Workspace.Attached(chat).Count == 0) return;
            string text = composer.Input.Text.Trim().Length == 0 ? "" : composer.Input.Text;
            Try(delegate
            {
                // Turned away before it became a message (the chat was busy or closing): the words and the attached files stay
                // where they were. Once it's a message it keeps its files, and a failed start offers Retry with them.
                int before = Asked(chat);
                if (!workspace.Send(chat, text, false) && Asked(chat) == before) { FillHeader(); UpdateKeys(); return; }
                composer.Input.Clear();
                transcript.Following = true;
                FillHeader();
                SyncItems();
                UpdateKeys();
                composer.Input.Focus();
            });
        }

        private void Stop() { if (chat != null) Try(delegate { workspace.Stop(chat); }); }

        private static int Asked(WorkspaceChat target) { lock (target.Gate) return target.Items.FindAll(delegate(WorkspaceItem item) { return item.Kind == "user"; }).Count; }

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
            // Everything switches at once, like the Mainframe's Blackwall: its menu row already says it runs anything without asking.
            Try(delegate { workspace.Change(target, null, picked, null); });
            FillHeader();
            composer.Input.Focus();
        }

        internal void PickModel(string picked)
        {
            if (chat == null) return;
            Try(delegate { workspace.Change(chat, null, null, picked); });
            FillHeader();
        }

        private static readonly string[] ModeNotes = { "Asks before anything past reading", "Edits files here without asking", "Plans, changes nothing", "Runs anything without asking" };
        private static readonly string[] ModelNotes = { "Claude Code's own pick", "Strongest, slowest", "Quick and strong", "Fastest, lightest" };

        // Her menus: the + menu, MODE and MODEL, with rows like the Mainframe's BRAIN choices. Returned open, so the smoke test
        // can picture them.
        internal ToolStripDropDown OpenMenu(string which)
        {
            if (openMenu != null && !openMenu.IsDisposed) openMenu.Close();
            ToolStripDropDown menu = new ToolStripDropDown { Renderer = new Mainframe.MenuLook(), BackColor = Neon.Ground, ForeColor = Neon.White, LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow, Padding = new Padding(Z(7)) };
            Control anchor;
            if (which == "add")
            {
                anchor = composer.Plus;
                WorkChoice folders = Choice(chat == null ? "Choose folders..." : "Add folders...", "Claude Code can work in them too", 2, false, delegate { Post(ChooseFolders); });
                folders.AccessibleName = "Add folders";
                WorkChoice files = Choice("Add files...", "Goes with your next message", 3, false, delegate { Post(ChooseFiles); });
                files.AccessibleName = "Add files";
                files.Enabled = chat != null;
                menu.Items.Add(folders);
                menu.Items.Add(files);
            }
            else
            {
                bool modes = which == "mode";
                anchor = modes ? composer.Mode : composer.Model;
                string[] choices = modes ? Workspace.Modes : Workspace.Models;
                for (int i = 0; i < choices.Length; i++)
                {
                    string choice = choices[i];
                    WorkChoice item = Choice(choice, modes ? ModeNotes[i] : ModelNotes[i], 1, chat != null && (modes ? chat.Mode : chat.Model) == choice, delegate { if (modes) PickMode(choice); else PickModel(choice); });
                    item.Checked = item.On;
                    item.AccessibleName = choice;
                    menu.Items.Add(item);
                }
            }
            WorkChoice.Arrange(menu, zoom);
            menu.Closed += delegate { Post(delegate { menu.Dispose(); }); };
            openMenu = menu;
            menu.Show(anchor, new Point(0, -menu.PreferredSize.Height - Z(4)));
            return menu;
        }

        private WorkChoice Choice(string text, string note, int mark, bool on, EventHandler click)
        {
            return new WorkChoice(text, click) { Note = note, Mark = mark, On = on, Unit = zoom, LabelFont = keys, NoteFont = small };
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
                    if (rest.Count > 0) SelectChat(rest[0]); else NewChat();
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

        // What the conversation shows: a tool's result joins its call, and an answered permission question joins the call it
        // was about, so a call that was asked about is still one row, with a mark.
        private List<WorkspaceItem> Showable(Dictionary<string, string> results, HashSet<string> failed, Dictionary<WorkspaceItem, WorkspaceItem> asked)
        {
            List<WorkspaceItem> items = new List<WorkspaceItem>();
            if (chat != null) lock (chat.Gate) items.AddRange(chat.Items);
            HashSet<string> tools = new HashSet<string>(StringComparer.Ordinal);
            foreach (WorkspaceItem item in items)
            {
                if (item.Kind == "tool") tools.Add(item.Turn + "/" + item.Id);
                if (item.Kind == "result") { results[item.Turn + "/" + item.Id] = item.Text; if (item.Decision == "error") failed.Add(item.Turn + "/" + item.Id); }
            }
            items.RemoveAll(delegate(WorkspaceItem item) { return item.Kind == "result" && tools.Contains(item.Turn + "/" + item.Id); });
            HashSet<WorkspaceItem> joined = new HashSet<WorkspaceItem>();
            for (int i = 0; i < items.Count; i++)
            {
                WorkspaceItem question = items[i];
                if (question.Kind != "approval" || question.Decision.Length == 0 || question.Ask != null) continue;
                // The call it answered: the same tool in the same turn, by its words when it was allowed, or else the nearest
                // one before it, then after it.
                string about = question.Text.StartsWith("Allowed: ", StringComparison.Ordinal) ? question.Text.Substring(9) : null;
                Predicate<WorkspaceItem> fits = delegate(WorkspaceItem call) { return call.Kind == "tool" && call.Turn == question.Turn && call.Name == question.Name && !asked.ContainsKey(call); };
                WorkspaceItem match = about == null ? null : items.Find(delegate(WorkspaceItem call) { return fits(call) && call.Text == about; });
                for (int k = i - 1; match == null && k >= 0; k--) if (fits(items[k])) match = items[k];
                for (int k = i + 1; match == null && k < items.Count; k++) if (fits(items[k])) match = items[k];
                if (match == null) continue;
                asked[match] = question;
                joined.Add(question);
            }
            items.RemoveAll(joined.Contains);
            return items;
        }

        private void SyncItems()
        {
            Dictionary<string, string> results = new Dictionary<string, string>(StringComparer.Ordinal);
            HashSet<string> failed = new HashSet<string>(StringComparer.Ordinal);
            Dictionary<WorkspaceItem, WorkspaceItem> asked = new Dictionary<WorkspaceItem, WorkspaceItem>();
            List<WorkspaceItem> items = Showable(results, failed, asked);
            WorkspaceTurn running = chat == null ? null : chat.Live;
            int end = pageEnd == null ? items.Count : items.IndexOf(pageEnd) + 1;
            if (end <= 0) { pageEnd = null; end = items.Count; }
            int start = Math.Max(0, end - 150);
            transcript.Empty = chat == null ? "" : "Write your first message below." + (workspace.Problems.Count > 0 ? "\n\n" + string.Join("\n", workspace.Problems.ToArray()) : "");
            transcript.Fill(items.GetRange(start, end - start), results, failed, asked, running == null ? null : running.Id, start > 0, end < items.Count);
        }

        private void Page(bool back)
        {
            if (chat == null) return;
            List<WorkspaceItem> items = Showable(new Dictionary<string, string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal), new Dictionary<WorkspaceItem, WorkspaceItem>());
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
                make.Bounds = new Rectangle(Z(26), Z(64), Z(206), Z(32));
                list.Bounds = new Rectangle(Z(26), Z(106), Z(220), Math.Max(Z(60), h - Z(126)));
                bool open = chat != null;
                transcript.Visible = composer.Visible = undo.Visible = open;
                if (!open)
                {
                    trust.Visible = rename.Visible = false;
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
                    transcript.Bounds = new Rectangle(left - Z(4), Z(116), width + Z(8), Math.Max(Z(80), composer.Top - Z(10) - Z(116)));
                }
                ask.Place();
            }
            finally { arranging = false; }
            Invalidate();
        }


        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Shape(); Place(); if (transcript != null) transcript.Shimmer(); }

        protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); if (transcript != null) transcript.Shimmer(); }

        // Cuts the window to her frame's corners, like the Mainframe, so its outline is the frame it paints.
        private void Shape() { if (Width > 0 && Height > 0) Neon.Clip(this, Z(16), Z(6)); }

        public void PaintGround(Graphics graphics, Rectangle area)
        {
            Neon.Backdrop(graphics, ClientSize, Z(210));
            using (Brush band = new SolidBrush(Neon.Band)) graphics.FillRectangle(band, Z(16), Z(56), Z(232), ClientSize.Height - Z(70));
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
            // Only for a moment before the window shows, or if her folder's chat couldn't be made: then load problems show here.
            if (chat == null)
            {
                if (workspace.Problems.Count > 0)
                    TextRenderer.DrawText(graphics, string.Join("\n", workspace.Problems.ToArray()), small, new Rectangle(left + Z(20), Z(90), right - left - Z(40), Math.Max(0, h - Z(130))), Neon.Hot, WorkPaint.Wrap | TextFormatFlags.HorizontalCenter);
                return;
            }
            if (!rename.Visible) TextRenderer.DrawText(graphics, WorkPaint.Fit(chat.Title, heading, titleBox.Width), heading, titleBox, Neon.White, WorkPaint.Line);
            TextRenderer.DrawText(graphics, Header, note, folderBox, Neon.Faint, WorkPaint.Line | TextFormatFlags.PathEllipsis);
            if (trust.Visible)
            {
                Rectangle words = new Rectangle(trust.Left - Z(96), trust.Top, Z(88), trust.Height);
                if (words.Left > titleBox.Left + Z(120)) TextRenderer.DrawText(graphics, "Not trusted", small, words, Neon.Faint, WorkPaint.Line | TextFormatFlags.Right);
            }
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
                if (!control.Visible || control == ask || ask.Shading(control)) continue;
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
        internal List<string> ToolWell(string id) { return transcript.Well(id); }
        // For the smoke and its pictures: the tool groups' summary lines, opening them, the shine's clock and where it draws.
        internal List<string> ToolGroups { get { return transcript.Summaries(); } }
        internal Font TipFont { get { return small; } }
        internal bool Thinking { get { return transcript.Thinking; } }
        internal int TopOf(WorkspaceItem item) { return transcript.TopOf(item); }
        internal List<string> CardWords(out Point details) { return transcript.CardWords(out details); }
        internal void PressTranscript(Point at) { transcript.PressAt(at); }
        internal static string EndText(string text) { return Transcript.EndShown(text); }
        internal string SelectedText { get { return transcript.Selected(); } }
        internal bool SelectText(WorkspaceItem a, int partA, int atA, WorkspaceItem b, int partB, int atB) { return transcript.SelectText(a, partA, atA, b, partB, atB); }
        internal void ClearSelection() { transcript.ClearSelection(); }
        internal Point TextPoint(WorkspaceItem item, int part, int at) { return transcript.PointOf(item, part, at); }
        internal void DragText(Point from, Point to) { transcript.DragText(from, to, 8); }
        internal void ClickText(Point at, int times) { transcript.ClickText(at, times); }
        internal int DragPastEnd(Point from, int ticks) { return transcript.DragPastEnd(from, ticks); }
        internal void TranscriptKey(Keys keys) { transcript.Key(keys); }
        internal Rectangle CopyKeyOn(Point at) { return transcript.CopyKeyOn(at); }
        internal void PressCopyKey() { transcript.PressCopyKey(); }
        internal bool CopyTicked { get { return transcript.Ticked; } }
        internal List<string> MenuNames(WorkspaceItem item) { return transcript.MenuNames(item); }
        internal void MoveOver(Point at) { transcript.MoveTo(at); }
        internal Func<bool> ShiftHeld { set { transcript.ShiftDown = value; } }
        internal Cursor TranscriptPointer { get { return transcript.Cursor; } }
        // A fold line's middle (index-th line, in the window's own space) or the MODE key's, with the tooltip each shows.
        internal string TipOn(int fold, out Point at)
        {
            if (fold < 0)
            {
                at = PointToClient(composer.PointToScreen(new Point(composer.Mode.Left + Z(20), composer.Mode.Top)));
                return tips.GetToolTip(composer.Mode);
            }
            Point inside = transcript.HeadPoint(fold);
            at = PointToClient(transcript.PointToScreen(inside));
            transcript.MoveTo(inside);
            return transcript.TipShown;
        }
        internal Cursor PointerOn(int fold) { Point at; TipOn(fold, out at); return transcript.Cursor; }
        internal int ConversationTop { get { return transcript.Offset; } set { transcript.ScrollTo(value); } }
        internal int FoldTop(int fold) { return transcript.HeadPoint(fold).Y + transcript.Offset; }
        internal void ClickFold(int fold) { transcript.PressAt(transcript.HeadPoint(fold)); }
        internal void OpenToolGroups(bool open) { transcript.OpenAll(open); }
        internal bool Shining { get { return transcript.Shining; } }
        internal static double ShineAt { set { Transcript.ShineFixed = value; } }
        internal Rectangle ShineArea
        {
            get
            {
                Rectangle at = transcript.ShineBox();
                return at.IsEmpty ? at : RectangleToClient(transcript.RectangleToScreen(at));
            }
        }
        internal bool CueMissing(Bitmap shot)
        {
            if (!composer.Visible || composer.Input.TextLength > 0 || ask.Visible) return false;
            Rectangle box = RectangleToClient(composer.Input.RectangleToScreen(new Rectangle(0, 0, composer.Input.ClientSize.Width, composer.Input.Font.Height + 2)));
            box.Intersect(new Rectangle(Point.Empty, shot.Size));
            int inked = 0;
            Color want = Neon.Faint;
            for (int y = box.Top; y < box.Bottom; y++)
                for (int x = box.Left; x < box.Right; x++)
                {
                    Color seen = shot.GetPixel(x, y);
                    if (Math.Abs(seen.R - want.R) + Math.Abs(seen.G - want.G) + Math.Abs(seen.B - want.B) < 30) inked++;
                }
            return inked < 20;
        }
        internal string CopyText(WorkspaceItem item) { return transcript.CopyOf(item); }
        // Where copied text goes: Windows' clipboard, or the smoke's stand-in so a run never touches his.
        internal static Action<string> ClipboardWrite = delegate(string text) { Clipboard.SetText(text); };
        internal string CodeFont { get { return mono.Name; } }
        internal string ProseFont { get { return Font.Name; } }
        internal bool UndoOffered { get { return undo.Visible && undo.Enabled; } }
        // The line under the title, as drawn.
        internal string Header
        {
            get
            {
                if (chat == null) return null;
                List<string> folders = Workspace.AllFolders(chat);
                return folders.Count > 1 ? Display(folders[0]) + "  +" + (folders.Count - 1) + (folders.Count == 2 ? " folder" : " folders") : Display(folders[0]);
            }
        }
        // Files or folders dropped from Explorer, through the window's own drop handler.
        internal void DropOn(string[] paths) { OnDragDrop(new DragEventArgs(new DataObject(DataFormats.FileDrop, paths), 0, 0, 0, DragDropEffects.Copy, DragDropEffects.Copy)); }
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
                tips.Dispose();
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
                int rail = window.Z(14);
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
                    int titleWide = row.Width - window.Z(20) - dot;
                    TextRenderer.DrawText(graphics, WorkPaint.Fit(item.Title, window.strong, titleWide), window.strong, new Rectangle(row.Left + window.Z(12), row.Top + window.Z(8), titleWide, window.Z(20)), on ? Neon.Cyan : Neon.White, WorkPaint.Line);
                    List<string> folders = Workspace.AllFolders(item);
                    string name = Path.GetFileName(folders[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                    if (name.Length == 0) name = folders[0];
                    if (folders.Count > 1) name += "  +" + (folders.Count - 1);
                    else if (string.Equals(item.Title, name, StringComparison.OrdinalIgnoreCase)) name = "";
                    if (name.Length > 0) TextRenderer.DrawText(graphics, name, window.note, new Rectangle(row.Left + window.Z(12), row.Top + window.Z(31), row.Width - window.Z(20), window.Z(16)), Neon.Faint, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    if (dot > 0)
                    {
                        SmoothingMode was = graphics.SmoothingMode;
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        using (Brush fill = new SolidBrush(pending ? Neon.Hot : Neon.Cyan)) graphics.FillEllipse(fill, row.Right - window.Z(18), row.Top + window.Z(14), window.Z(7), window.Z(7));
                        graphics.SmoothingMode = was;
                    }
                }
                WorkPaint.Rail(graphics, WorkPaint.Track(ClientRectangle, s), Height, Content, offset);
            }

            private int At(Point point)
            {
                if (point.X >= Width - window.Z(14)) return -1;
                int index = (point.Y + offset) / (Row + window.Z(4));
                return index >= 0 && index < chats.Count && (point.Y + offset) % (Row + window.Z(4)) < Row ? index : -1;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (dragging)
                {
                    RectangleF track = WorkPaint.Track(ClientRectangle, window.zoom), thumb = WorkPaint.Thumb(track, Height, Content, offset);
                    float room = track.Height - thumb.Height;
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
                if (e.Button == MouseButtons.Left && e.X >= Width - window.Z(14) && Content > Height) { dragging = true; dragFrom = e.Y; dragOffset = offset; Capture = true; return; }
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
            internal const string Hint = "Mode and model changes apply to your next message.";
            internal int Wanted;

            internal Composer(WorkspaceWindow window)
            {
                this.window = window;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                AccessibleName = "Composer";
                Input = new MessageField { Multiline = true, AcceptsReturn = true, MaxLength = 0, WordWrap = true, ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None, BackColor = Neon.Field, ForeColor = Neon.White,
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
                    foreach (string file in Workspace.Attached(chat)) chips.Add(new Chip { Path = file, Name = Named(file) });
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
                int pad = Pad, rowTall = window.Z(24), gap = window.Z(6), rows = 0, right = Width - pad;
                // When some don't fit, the third row leaves room for its "+N more".
                for (int pass = 0; pass < 2; pass++)
                {
                    int x = pad, y = pad, last = pass == 0 ? right : right - window.Z(76);
                    bool hidden = false;
                    rows = chips.Count > 0 ? 1 : 0;
                    foreach (Chip chip in chips)
                    {
                        int wide = Math.Min(Math.Max(window.Z(60), right - pad), window.Z(30) + WorkPaint.Wide(chip.Name, window.small) + (chip.Main ? CaptionWide + window.Z(10) : window.Z(20)));
                        wide = Math.Min(wide, window.Z(260));
                        if (x + wide > (rows == 3 ? last : right) && x > pad) { x = pad; y += rowTall + gap; rows++; }
                        chip.Box = rows > 3 ? Rectangle.Empty : new Rectangle(x, y, wide, rowTall);
                        chip.Cross = chip.Main || chip.Box.IsEmpty ? Rectangle.Empty : new Rectangle(chip.Box.Right - window.Z(20), chip.Box.Top, window.Z(18), rowTall);
                        if (chip.Box.IsEmpty) hidden = true;
                        x += wide + gap;
                    }
                    if (!hidden) break;
                }
                chipsTall = chips.Count == 0 ? 0 : Math.Min(rows, 3) * (rowTall + gap);
                Input.Bounds = new Rectangle(pad, pad + chipsTall, Math.Max(1, Width - pad * 2 - window.Z(6)), inputTall);
                int keyTop = Input.Bottom + window.Z(10), keyTall = window.Z(30);
                Plus.Bounds = new Rectangle(pad, keyTop, keyTall, keyTall);
                Mode.Bounds = new Rectangle(Plus.Right + window.Z(8), keyTop, Math.Max(window.Z(148), Mode.Needs), keyTall);
                Model.Bounds = new Rectangle(Mode.Right + window.Z(8), keyTop, Math.Max(window.Z(132), Model.Needs), keyTall);
                Send.Bounds = Stop.Bounds = new Rectangle(Width - pad - window.Z(86), keyTop, window.Z(86), keyTall);
                Wanted = keyTop + keyTall + pad;
            }

            // Until the first message the folder it runs in can change, and the chip says so.
            private string MainCaption { get { return window.CanRehome ? "runs here · change" : "runs here"; } }
            // How wide the caption is drawn: with "change", the dot has the same gap on both sides.
            private int CaptionWide { get { return window.CanRehome ? WorkPaint.Wide("runs here", window.caption) + WorkPaint.Wide("\u00b7", window.caption) + WorkPaint.Wide("change", window.caption) + 4 * window.Z(4) : WorkPaint.Wide(MainCaption, window.caption); } }

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
                    int textRight = chip.Main ? chip.Box.Right - CaptionWide - window.Z(12) : chip.Cross.Left;
                    TextRenderer.DrawText(graphics, chip.Name, window.small, new Rectangle(textLeft, chip.Box.Top, Math.Max(1, textRight - textLeft), chip.Box.Height), ink, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    if (chip.Main && window.CanRehome)
                    {
                        // "runs here" stays grey; "change" is cyan like the window's keys, with a key's hover plate.
                        int gap = window.Z(4), at = textRight + gap, lead = WorkPaint.Wide("runs here", window.caption), dot = WorkPaint.Wide("\u00b7", window.caption), wide = WorkPaint.Wide("change", window.caption);
                        TextRenderer.DrawText(graphics, "runs here", window.caption, new Rectangle(at, chip.Box.Top, lead + 1, chip.Box.Height), Neon.Faint, WorkPaint.Line);
                        TextRenderer.DrawText(graphics, "\u00b7", window.caption, new Rectangle(at + lead + gap, chip.Box.Top, dot + 1, chip.Box.Height), Neon.Faint, WorkPaint.Line);
                        int word = at + lead + gap + dot + gap;
                        Rectangle change = new Rectangle(word - window.Z(3), chip.Box.Top + window.Z(4), wide + window.Z(6), chip.Box.Height - window.Z(8));
                        if (over) WorkPaint.Plate(graphics, change, 3 * s, Neon.Tone(13, 45, 49), Neon.Cyan, line);
                        TextRenderer.DrawText(graphics, "change", window.caption, new Rectangle(word, chip.Box.Top, wide + 1, chip.Box.Height), Neon.Cyan, WorkPaint.Line);
                    }
                    else if (chip.Main) TextRenderer.DrawText(graphics, MainCaption, window.caption, new Rectangle(textRight + window.Z(4), chip.Box.Top, chip.Box.Right - textRight - window.Z(4), chip.Box.Height), Neon.Faint, WorkPaint.Line);
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
                    RectangleF rail = new RectangleF(Width - Pad - window.Z(2), Input.Top, 2 * s, Input.Height);
                    WorkPaint.Rail(graphics, rail, Input.Visible_Lines, Input.Line_Count, Input.First_Line);
                }
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
                else if (chip.Main && window.CanRehome) window.Post(window.ChooseMain);
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
                Chip shownLast = null;
                bool more = false;
                foreach (Chip chip in chips) { if (chip.Box.IsEmpty) more = true; else shownLast = chip; }
                if (more && shownLast != null && shownLast.Box.Right + window.Z(76) > Width - Pad) return "Composer's +N more runs past its edge.";
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

            [StructLayout(LayoutKind.Sequential)]
            private struct PaintSheet
            {
                internal IntPtr Dc;
                internal bool Erase;
                internal int Left, Top, Right, Bottom;
                internal bool Restore, Update;
                [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)] internal byte[] Reserved;
            }

            [DllImport("user32.dll")] private static extern IntPtr BeginPaint(IntPtr window, out PaintSheet sheet);
            [DllImport("user32.dll")] private static extern bool EndPaint(IntPtr window, ref PaintSheet sheet);

            // While the box is empty its whole paint is hers, in one go: the field and the cue, focused or not. An empty edit
            // control has nothing else to draw (the caret is Windows' own), so the cue can't be lost between the control's paint
            // and a second one on top. A print (WM_PRINTCLIENT, how Windows copies the box) gets the same, and the box repaints
            // whenever it gains or loses focus or empties.
            protected override void WndProc(ref Message message)
            {
                int kind = message.Msg;
                if (kind == 0x000F && CueWanted && message.WParam == IntPtr.Zero)
                {
                    PaintSheet sheet;
                    IntPtr dc = BeginPaint(Handle, out sheet);
                    try { using (Graphics graphics = Graphics.FromHdc(dc)) PaintEmpty(graphics); }
                    finally { EndPaint(Handle, ref sheet); }
                    message.Result = IntPtr.Zero;
                    return;
                }
                if (kind == 0x0318 && CueWanted && message.WParam != IntPtr.Zero)
                {
                    using (Graphics graphics = Graphics.FromHdc(message.WParam)) PaintEmpty(graphics);
                    message.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref message);
                if ((kind == 0x0007 || kind == 0x0008) && TextLength == 0) Invalidate();
                if ((kind == 0x020A || kind == 0x0115 || kind == 0x0100) && Scrolled != null) Scrolled();
            }

            private void PaintEmpty(Graphics graphics)
            {
                using (Brush field = new SolidBrush(BackColor)) graphics.FillRectangle(field, ClientRectangle);
                DrawCue(graphics);
            }

            private bool CueWanted { get { return TextLength == 0 && Cue.Length > 0 && CueFont != null; } }

            private void DrawCue(Graphics graphics)
            {
                TextRenderer.DrawText(graphics, Cue, CueFont, new Rectangle(1, 0, ClientSize.Width, Font.Height + 2), Neon.Faint, TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); if (TextLength == 0) Invalidate(); }
        }

        // A question asked inside the window, in her look: the words, the step in red or cyan, and the way back out.
        private sealed class AskCard : Control
        {
            private readonly WorkspaceWindow window;
            private readonly WorkKey yes, no;
            private readonly Shade shade;
            private Action<bool> answered;
            internal string Question = "";

            // Everything behind a question, dimmed, and deaf to clicks until it's answered.
            private sealed class Shade : Control
            {
                internal Bitmap Picture;

                internal Shade()
                {
                    SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                    SetStyle(ControlStyles.Selectable, false);
                    Visible = false;
                    AccessibleRole = AccessibleRole.None;
                }

                protected override void OnPaintBackground(PaintEventArgs e) { }

                protected override void OnPaint(PaintEventArgs e)
                {
                    if (Picture != null) e.Graphics.DrawImageUnscaled(Picture, 0, 0);
                    else e.Graphics.Clear(Neon.Ground);
                    using (Brush dim = new SolidBrush(Color.FromArgb(170, 4, 2, 8))) e.Graphics.FillRectangle(dim, ClientRectangle);
                }

                protected override void Dispose(bool disposing)
                {
                    if (disposing && Picture != null) { Picture.Dispose(); Picture = null; }
                    base.Dispose(disposing);
                }
            }

            internal AskCard(WorkspaceWindow window)
            {
                this.window = window;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Visible = false;
                AccessibleRole = AccessibleRole.Dialog;
                yes = new WorkKey();
                yes.Click += delegate { Answer(true); };
                no = new WorkKey { Accent = Neon.Rose };
                no.Click += delegate { Answer(false); };
                Controls.Add(yes);
                Controls.Add(no);
                shade = new Shade();
                window.Controls.Add(shade);
            }

            internal bool Shading(Control control) { return control == shade; }

            // A still of the window as it is, laid under the card and dimmed.
            private void Cover()
            {
                Size size = window.ClientSize;
                if (size.Width <= 0 || size.Height <= 0) return;
                shade.Visible = false;
                Bitmap still = new Bitmap(size.Width, size.Height);
                try { window.DrawToBitmap(still, new Rectangle(Point.Empty, size)); }
                catch (Exception) { using (Graphics graphics = Graphics.FromImage(still)) graphics.Clear(Neon.Ground); }
                if (shade.Picture != null) shade.Picture.Dispose();
                shade.Picture = still;
                shade.Bounds = new Rectangle(Point.Empty, size);
                shade.Visible = true;
                shade.BringToFront();
                BringToFront();
            }

            internal void Ask(string question, string step, Color accent, string back, Action<bool> then)
            {
                Question = question;
                AccessibleName = question;
                answered = then;
                yes.Text = step;
                yes.Accent = accent;
                no.Text = back;
                Cover();
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
                shade.Visible = false;
                if (shade.Picture != null) { shade.Picture.Dispose(); shade.Picture = null; }
                window.Invalidate(true);
                if (then != null) then(choice);
            }

            internal void Place()
            {
                if (!Visible) return;
                if (shade.Visible && shade.Size != window.ClientSize) Cover();
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
                TextRenderer.DrawText(graphics, yes.Text.ToUpperInvariant(), window.caption, new Rectangle(window.Z(24), window.Z(18), Width - window.Z(48), window.Z(16)), yes.Accent == Neon.Hot ? Neon.Hot : Neon.Cyan, WorkPaint.Line);
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
                internal Rectangle Box, Toggle, More;
                // A waiting approval's tool picture, beside its plain sentence.
                internal RectangleF Mark;
                internal readonly List<Piece> Pieces = new List<Piece>();
                internal readonly List<Control> Keys = new List<Control>();
                internal WorkField Reason;
                // What it was last measured from, so a card that hasn't changed only moves when another arrives.
                internal string LaidText, LaidResult, LaidDecision, LaidShape, LaidInput, LaidPaths, LaidState;
                internal bool LaidOpen, LaidAll;
                internal int LaidWidth = -1, LaidRound;
                // A tool call: whether its result is in, came back failed or is still coming, and the question it was asked about.
                internal bool Done, Failed, Running, Hidden;
                internal WorkspaceItem Asked;
                // On the first call of a run of them: the whole run, its summary line and what that line says.
                internal List<Block> Group;
                internal Rectangle Head;
                internal string Words = "", Trouble = "", Doing, HeadTool = "", HeadMark = "";
                // Its text as parts a selection counts along (his words, her paragraphs, a well's code, a row's line), and where
                // each stretch of it is drawn.
                internal readonly List<WordBlock> Parts = new List<WordBlock>();
                internal readonly List<Seg> Segs = new List<Seg>();
            }

            // One drawn stretch of selectable text: its part, where it starts in that part's characters, and its box. A code
            // row draws tabs as four spaces, so Map gives each drawn character's place in the part when the row had a tab.
            internal sealed class Seg
            {
                internal int Part, Start;
                internal string Text;
                internal Font Font;
                internal Rectangle Box;
                internal int[] Map;
                internal int At(int drawn) { return Map != null ? Map[Math.Max(0, Math.Min(drawn, Map.Length - 1))] : Start + drawn; }
                internal int End { get { return At(Text.Length); } }
            }

            // A place in the conversation's text: a block, one of its parts, and a character along it.
            internal struct Spot
            {
                internal Block Block;
                internal int Part, At;
            }

            internal sealed class Piece
            {
                internal string Text;
                internal Font Font;
                internal Color Ink;
                internal Rectangle Box;
                internal bool Code, Line, Right;
                internal string Tag;
                internal List<string> Rows;
                internal List<Color> Inks;
                internal int RowTall;
            }

            private readonly WorkspaceWindow window;
            internal readonly List<Block> Blocks = new List<Block>();
            private readonly Dictionary<WorkspaceItem, Block> known = new Dictionary<WorkspaceItem, Block>();
            internal readonly WorkKey Earlier, Newer;
            internal int Offset, Content;
            internal bool Following = true;
            // Empty room kept under the end so the view didn't have to move when something at the bottom shrank.
            private int slack;
            // Claude thinking between calls, and where its line sits.
            private bool thinking;
            private Rectangle thinkLine;
            internal string Empty = "";
            private Block picked, hovered;
            private bool dragging, earlier, newer, hoveredHead;
            private int dragFrom, dragOffset, laid = -1, round;
            private readonly HashSet<WorkspaceItem> opened;
            private readonly ToolTip tips;
            private string tipShown;
            // The shine's clock, the one seam its frames hang on. On tryout-090 this line becomes:
            //     private readonly Beat shineClock = new Beat { Interval = 33 };
            private readonly System.Windows.Forms.Timer shineClock = new System.Windows.Forms.Timer { Interval = 33 };
            private static readonly Stopwatch shineTime = Stopwatch.StartNew();
            // Where the shine is in its pass, 0 to 1, when the smoke pictures it; below 0 it follows the clock.
            internal static double ShineFixed = -1;

            internal Transcript(WorkspaceWindow window, HashSet<WorkspaceItem> opened)
            {
                this.window = window;
                this.opened = opened;
                tips = WorkPaint.Tips(delegate { return window.small; });
                shineClock.Tick += delegate { ShineTick(); };
                edgeClock.Tick += delegate { EdgeTick(); };
                tickClock.Tick += delegate { TickDone(); };
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
                round++;
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

            internal void Fill(List<WorkspaceItem> shown, Dictionary<string, string> results, HashSet<string> failed, Dictionary<WorkspaceItem, WorkspaceItem> asked, string live, bool hasEarlier, bool hasNewer)
            {
                bool follow = Following;
                int was = Offset, tall = Content;
                // What he's reading: each thing on screen and where it sits, so the view can stay on it when something changes size.
                List<KeyValuePair<WorkspaceItem, int>> anchors = new List<KeyValuePair<WorkspaceItem, int>>();
                foreach (Block old in Blocks)
                    if (!old.Hidden && old.Box.Bottom > Offset && old.Box.Top < Offset + Height) anchors.Add(new KeyValuePair<WorkspaceItem, int>(old.Item, old.Box.Top - Offset));
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
                    string result, key = item.Turn + "/" + item.Id;
                    block.Done = item.Kind == "tool" && results.TryGetValue(key, out result);
                    if (block.Done) block.Result = results[key];
                    block.Failed = block.Done && failed.Contains(key);
                    WorkspaceItem question;
                    block.Asked = asked.TryGetValue(item, out question) ? question : null;
                    block.Running = item.Kind == "tool" && !block.Done && live != null && item.Turn == live;
                    Blocks.Add(block);
                    Equip(block);
                }
                earlier = hasEarlier;
                newer = hasNewer;
                Earlier.Visible = earlier;
                Newer.Visible = newer;
                bool busy = false;
                foreach (Block block in Blocks) if (block.Running || Waiting(block.Item)) busy = true;
                thinking = live != null && !busy && !hasNewer;
                slack = 0;
                Arrange(true);
                Hold(follow, tall, anchors, was);
            }

            // Keeps his place. At the end while the conversation grows, it follows, as Metis does. Otherwise the first thing he
            // could see stays where it was, even when what shrank was at the bottom (an answered card folding away): the room it
            // leaves stays under the end until he scrolls or more arrives.
            private void Hold(bool follow, int tall, List<KeyValuePair<WorkspaceItem, int>> anchors, int was)
            {
                if (follow && Content >= tall) { Following = true; ScrollTo(int.MaxValue); Following = true; return; }
                foreach (KeyValuePair<WorkspaceItem, int> anchor in anchors)
                {
                    Block block;
                    if (!known.TryGetValue(anchor.Key, out block) || block.Hidden || !Blocks.Contains(block)) continue;
                    int target = Math.Max(0, block.Box.Top - anchor.Value);
                    int room = target + Height - Content;
                    if (room > 0 && target > 0) { slack = room; Content += room; }
                    Offset = Math.Max(0, Math.Min(target, Content - Height));
                    Place();
                    Following = follow;
                    return;
                }
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
                    Key(block, "Not now", Neon.Rose, delegate { window.Try(delegate { window.workspace.Trust(chat, false); window.RefreshChat(); }); });
                }
                else
                {
                    Key(block, "Undo all", Neon.Cyan, delegate { window.Try(delegate { window.workspace.AnswerUndo(chat, item.Undo, true); window.RefreshChat(); }); });
                    Key(block, "Only the unchanged ones", Neon.Rose, delegate { window.Try(delegate { window.workspace.AnswerUndo(chat, item.Undo, false); window.RefreshChat(); }); });
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
                    bool taken = window.workspace.Answer(window.chat, ask, yes, always, reason);
                    window.workspace.Clicked(yes ? always ? "Always allow" : "Allow" : "Deny", taken ? "answered" : ask.Choice != null ? "already answered" : "turn not running");
                    if (taken)
                    {
                        foreach (Control key in block.Keys) key.Enabled = false;
                        if (block.Reason != null) block.Reason.Enabled = false;
                    }
                    // Already settled (its turn ended, stopped or timed out): show what it is now, never a card that won't answer.
                    else window.RefreshChat();
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
                string paths = window.PathsSeen();
                Group();
                for (int i = 0; i < Blocks.Count; i++)
                {
                    Block block = Blocks[i];
                    WorkspaceItem item = block.Item;
                    if (block.Hidden)
                    {
                        block.Pieces.Clear();
                        block.Parts.Clear();
                        block.Segs.Clear();
                        block.Toggle = block.More = block.Head = Rectangle.Empty;
                        block.Box = new Rectangle(Inset, y, Edge - Inset, 0);
                        block.LaidWidth = -1;
                        continue;
                    }
                    string state = State(block);
                    if (block.LaidWidth == Width && block.LaidRound == round && (object)block.LaidText == item.Text && (object)block.LaidResult == block.Result && block.LaidDecision == item.Decision
                        && (object)block.LaidInput == item.Input && block.LaidPaths == paths && block.LaidState == state
                        && block.LaidShape == block.Shape && block.LaidOpen == block.Open && block.LaidAll == block.All) Shift(block, y - block.Box.Top);
                    else
                    {
                        Measure(block, y);
                        block.LaidWidth = Width; block.LaidRound = round; block.LaidText = item.Text; block.LaidResult = block.Result;
                        block.LaidInput = item.Input; block.LaidPaths = paths; block.LaidState = state;
                        block.LaidDecision = item.Decision; block.LaidShape = block.Shape; block.LaidOpen = block.Open; block.LaidAll = block.All;
                    }
                    Block next = null;
                    for (int k = i + 1; k < Blocks.Count && next == null; k++) if (!Blocks[k].Hidden) next = Blocks[k];
                    y = block.Box.Bottom + Gap(block, next);
                }
                thinkLine = Rectangle.Empty;
                if (thinking)
                {
                    if (Blocks.Count > 0) y += Z(18);
                    thinkLine = new Rectangle(Inset, y, Edge - Inset, Z(26));
                    y = thinkLine.Bottom;
                }
                Content = y + Z(6) + slack;
                Place();
            }

            // Room between things: tight inside an open group, a little above the time and cost line, and clear space between
            // his message, her words and each group of tool calls.
            private int Gap(Block block, Block next)
            {
                if (next == null) return 0;
                if (Row(block) && Row(next) && next.Group == null) return Z(2);
                if (next.Item.Kind == "end") return Z(8);
                return Z(18);
            }

            // A tool call, or a permission question that was answered but couldn't be matched to its call.
            private static bool Row(Block block) { return block.Item.Kind == "tool" || block.Item.Kind == "approval" && !Waiting(block.Item); }

            private static bool Command(string name) { return name == "Bash" || name == "PowerShell"; }

            // Every run of tool calls between two other things sits behind one summary line, closed until it's opened.
            private void Group()
            {
                for (int i = 0; i < Blocks.Count; )
                {
                    Block first = Blocks[i];
                    if (!Row(first)) { first.Group = null; first.Hidden = false; first.Head = Rectangle.Empty; i++; continue; }
                    int j = i;
                    while (j < Blocks.Count && Row(Blocks[j])) j++;
                    List<Block> run = Blocks.GetRange(i, j - i);
                    bool open = opened.Contains(first.Item);
                    for (int k = 0; k < run.Count; k++) { run[k].Group = k == 0 ? run : null; run[k].Hidden = k > 0 && !open; }
                    Summarise(first, run);
                    i = j;
                }
            }

            private string State(Block block)
            {
                string state = block.Hidden + "|" + block.Running + block.Done + block.Failed + "|" + Status(block);
                if (block.Group != null) state += "|" + opened.Contains(block.Item) + "|" + block.Words + block.Trouble + "|" + block.Doing;
                return state;
            }

            // What a call came to, shown as a mark: asked about and allowed, denied, or failed.
            private static string Status(Block block)
            {
                WorkspaceItem item = block.Item;
                if (item.Kind == "approval") return item.Decision == "deny" ? "denied" : "asked";
                if (block.Asked != null && block.Asked.Decision == "deny") return "denied";
                if (block.Failed) return "failed";
                if (block.Asked != null) return "asked";
                return "";
            }

            // What a call is about, on one line: the command, or the path.
            private string Detail(Block block, bool whole)
            {
                WorkspaceItem item = block.Item;
                string text = item.Text;
                if (item.Kind == "approval")
                {
                    if (text.StartsWith("Allowed: ", StringComparison.Ordinal)) text = text.Substring(9);
                    else if (text.StartsWith("Denied: ", StringComparison.Ordinal)) return text;
                }
                string detail = text.StartsWith(item.Name, StringComparison.Ordinal) ? text.Substring(item.Name.Length).TrimStart(':', ' ') : text;
                if (!Command(item.Name)) return whole ? window.Display(detail) : window.Shorten(detail);
                detail = window.Display(detail);
                int cut = detail.IndexOf('\n');
                return whole || cut < 0 ? detail : detail.Substring(0, cut).TrimEnd() + " \u2026";
            }

            // Whether a row's one line had to cut its command or path short.
            private bool Cut(Block block)
            {
                string whole = Detail(block, true), shown = Detail(block, false);
                return whole != shown || WorkPaint.Wide(shown, window.note) > block.Toggle.Width - Z(52);
            }

            // A denied call in words: his own no, with the reason he gave, or why Workspace said no for him.
            private static string Refusal(Block block)
            {
                string text = block.Asked != null ? block.Asked.Text : block.Item.Text;
                string reason = text.StartsWith("Denied: ", StringComparison.Ordinal) ? text.Substring(8).Trim() : "";
                if (reason.Length == 0 || reason == "Denied in Workspace.") return "You said no.";
                if (reason == "No answer after ten minutes." || reason == "That Workspace turn ended." || reason.StartsWith("Couldn't save an undo copy", StringComparison.Ordinal)) return "Not allowed: " + reason;
                return "You said no: " + reason;
            }

            // The time and cost line as he reads it. Older chats saved the cost as it came ("$1.8235778"), so it's put to the
            // cent here as well as when a turn ends.
            internal static string EndShown(string text)
            {
                string shown = Regex.Replace(text ?? "", @"\A([0-9]+(?:\.[0-9]+)?)s(?= |\z)", delegate(Match m) { return Workspace.Took(double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)); });
                shown = Regex.Replace(shown, @"\$([0-9.]+(?:[eE][-+]?[0-9]+)?)", delegate(Match m) { return Workspace.Money(m.Groups[1].Value); });
                return shown.Replace(" | ", "  \u00b7  ");
            }

            private static string Leaf(string path)
            {
                string name = Path.GetFileName(path.TrimEnd('\\', '/'));
                return name.Length > 0 ? name : path;
            }

            // The summary line's words, Metis's way: what happened, counted by kind. A call that failed, was denied or was stopped
            // counts only there, named by what it was ("1 command failed", "1 read denied"), so the parts add up to the calls.
            // Only finished calls count; a call that's running says what it's doing instead.
            private void Summarise(Block lead, List<Block> run)
            {
                int commands = 0, searches = 0, web = 0, todos = 0, agents = 0;
                HashSet<string> read = new HashSet<string>(StringComparer.OrdinalIgnoreCase), edited = new HashSet<string>(StringComparer.OrdinalIgnoreCase), written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<string> otherNames = new List<string>(), troubleNames = new List<string>();
                Dictionary<string, int> others = new Dictionary<string, int>(StringComparer.Ordinal), trouble = new Dictionary<string, int>(StringComparer.Ordinal);
                Dictionary<string, int> kinds = new Dictionary<string, int>(StringComparer.Ordinal);
                Block running = null;
                foreach (Block block in run)
                {
                    WorkspaceItem item = block.Item;
                    if (block.Running) { running = block; continue; }
                    string status = Status(block), outcome = status == "failed" ? "failed" : status == "denied" ? "denied" : item.Kind == "tool" && !block.Done ? "stopped" : null;
                    if (outcome != null)
                    {
                        string key = Noun(item.Name) + "|" + outcome;
                        if (!trouble.ContainsKey(key)) { trouble[key] = 0; troubleNames.Add(key); }
                        trouble[key]++;
                        continue;
                    }
                    string kind = WorkPaint.ToolKind(item.Name), target = Detail(block, true);
                    int had;
                    kinds[item.Name] = kinds.TryGetValue(item.Name, out had) ? had + 1 : 1;
                    if (kind == "terminal") commands++;
                    else if (kind == "search") searches++;
                    else if (kind == "page") read.Add(target);
                    else if (kind == "pencil") { if (item.Name == "Write") written.Add(target); else edited.Add(target); }
                    else if (kind == "globe") web++;
                    else if (kind == "list") todos++;
                    else if (kind == "branch") agents++;
                    else { string noun = Noun(item.Name); if (!others.ContainsKey(noun)) { others[noun] = 0; otherNames.Add(noun); } others[noun]++; }
                }
                List<string> parts = new List<string>();
                if (commands > 0) parts.Add(commands + (commands == 1 ? " command ran" : " commands ran"));
                if (read.Count > 0) parts.Add(read.Count + (read.Count == 1 ? " file read" : " files read"));
                if (edited.Count > 0) parts.Add(edited.Count + (edited.Count == 1 ? " file edited" : " files edited"));
                if (written.Count > 0) parts.Add(written.Count + (written.Count == 1 ? " file written" : " files written"));
                if (searches > 0) parts.Add(searches + (searches == 1 ? " search" : " searches"));
                if (web > 0) parts.Add(web + (web == 1 ? " web lookup" : " web lookups"));
                if (todos > 0) parts.Add("to-do list updated");
                if (agents > 0) parts.Add(agents + (agents == 1 ? " agent ran" : " agents ran"));
                foreach (string noun in otherNames) parts.Add(others[noun] + " " + Plural(noun, others[noun]));
                List<string> troubles = new List<string>();
                foreach (string key in troubleNames)
                {
                    string[] bits = key.Split('|');
                    troubles.Add(trouble[key] + " " + Plural(bits[0], trouble[key]) + " " + bits[1]);
                }
                lead.Words = string.Join(", ", parts.ToArray());
                lead.Trouble = string.Join(", ", troubles.ToArray());
                lead.HeadMark = troubles.Count > 0 ? "failed" : "";
                lead.Doing = running == null ? null : Doing(running);
                string most = null;
                int best = 0;
                foreach (KeyValuePair<string, int> pair in kinds) if (pair.Value > best) { best = pair.Value; most = pair.Key; }
                lead.HeadTool = running != null ? running.Item.Name : most ?? run[0].Item.Name;
            }

            // What one call is called in a summary: a command, a read, an edit, or a call to a connector by the server's name.
            private static string Noun(string name)
            {
                switch (WorkPaint.ToolKind(name))
                {
                    case "terminal": return "command";
                    case "page": return "read";
                    case "pencil": return name == "Write" ? "write" : "edit";
                    case "search": return "search";
                    case "globe": return "web lookup";
                    case "list": return "to-do update";
                    case "branch": return "agent";
                    case "plug":
                        {
                            string[] bits = name.Split(new[] { "__" }, StringSplitOptions.None);
                            string server = bits.Length > 2 ? bits[1] : "";
                            if (server.Length == 0) return "connector call";
                            if (string.Equals(server, "github", StringComparison.OrdinalIgnoreCase)) server = "GitHub";
                            else server = char.ToUpperInvariant(server[0]) + server.Substring(1);
                            return server + " call";
                        }
                    default: return name + " call";
                }
            }

            private static string Plural(string noun, int count)
            {
                if (count == 1) return noun;
                return noun.EndsWith("ch", StringComparison.Ordinal) || noun.EndsWith("sh", StringComparison.Ordinal) ? noun + "es" : noun + "s";
            }

            // What a running call is doing, in a few words.
            private string Doing(Block block)
            {
                string name = block.Item.Name, kind = WorkPaint.ToolKind(name), target = Detail(block, false);
                if (kind == "terminal")
                {
                    string[] words = target.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    string start = string.Join(" ", words, 0, Math.Min(3, words.Length));
                    if (start.Length > 36) start = start.Substring(0, 35) + "\u2026";
                    return "Running " + start;
                }
                if (kind == "page") return "Reading " + Leaf(target);
                if (kind == "pencil") return (name == "Write" ? "Writing " : "Editing ") + Leaf(target);
                if (kind == "search") return "Searching " + Leaf(target);
                if (kind == "globe") return "Looking it up online";
                if (kind == "list") return "Updating the to-do list";
                if (kind == "branch") return "Running an agent";
                if (kind == "plug") { string[] bits = name.Split(new[] { "__" }, StringSplitOptions.None); return "Calling " + bits[bits.Length - 1]; }
                return "Running " + name;
            }

            internal string TipShown { get { return tipShown; } }

            internal Point HeadPoint(int index)
            {
                int k = 0;
                foreach (Block block in Blocks)
                    if (block.Group != null && !block.Head.IsEmpty && k++ == index) return new Point(block.Head.Left + Z(80), block.Head.Top + block.Head.Height / 2 - Offset);
                return Point.Empty;
            }

            // Where a thing sits on screen (its top, in this control's space), or int.MinValue when it isn't shown.
            internal int TopOf(WorkspaceItem item)
            {
                Block block;
                return known.TryGetValue(item, out block) && Blocks.Contains(block) && !block.Hidden ? block.Box.Top - Offset : int.MinValue;
            }

            // A waiting card's words and the middle of its "Show the details" link.
            internal List<string> CardWords(out Point details)
            {
                details = Point.Empty;
                List<string> words = new List<string>();
                foreach (Block block in Blocks)
                {
                    if (block.Item.Kind != "approval" || !Waiting(block.Item)) continue;
                    foreach (Piece piece in block.Pieces) if (!piece.Code) words.Add(piece.Text);
                    if (!block.Toggle.IsEmpty) details = new Point(block.Toggle.Left + block.Toggle.Width / 2, block.Toggle.Top + block.Toggle.Height / 2 - Offset);
                }
                return words;
            }

            internal void MoveTo(Point at) { OnMouseMove(new MouseEventArgs(MouseButtons.None, 0, at.X, at.Y, 0)); }

            internal void PressAt(Point at)
            {
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
            }

            internal List<string> Summaries()
            {
                List<string> lines = new List<string>();
                foreach (Block block in Blocks)
                    if (block.Group != null) lines.Add((block.Doing != null ? block.Doing + " | " : "") + block.Words + (block.Trouble.Length > 0 ? " | " + block.Trouble : ""));
                return lines;
            }

            internal void OpenAll(bool open)
            {
                foreach (Block block in Blocks) if (block.Group != null) { if (open) opened.Add(block.Item); else opened.Remove(block.Item); }
                Arrange(true);
            }

            private static void Shift(Block block, int by)
            {
                if (by == 0) return;
                block.Box.Offset(0, by);
                if (!block.Head.IsEmpty) block.Head.Offset(0, by);
                if (!block.Toggle.IsEmpty) block.Toggle.Offset(0, by);
                if (!block.More.IsEmpty) block.More.Offset(0, by);
                if (!block.Mark.IsEmpty) block.Mark.Offset(0, by);
                foreach (Piece piece in block.Pieces) piece.Box.Offset(0, by);
                foreach (Seg seg in block.Segs) seg.Box.Offset(0, by);
                foreach (Control key in block.Keys) if (key.Tag is Rectangle) { Rectangle at = (Rectangle)key.Tag; at.Offset(0, by); key.Tag = at; }
                if (block.Reason != null && block.Reason.Tag is Rectangle) { Rectangle at = (Rectangle)block.Reason.Tag; at.Offset(0, by); block.Reason.Tag = at; }
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

            // Words wrapped to a width with room between the lines; returns the bottom.
            private int Prose(Block block, string text, Font font, Color ink, int x, int y, int width)
            {
                List<string> rows = Wrap(text, font, width);
                int pitch = font.Height + Z(5);
                Piece piece = Add(block, text, font, ink, new Rectangle(x, y, width, rows.Count * pitch));
                piece.Rows = rows;
                piece.RowTall = pitch;
                // Each row is a stretch of the paragraph as typed: Wrap only drops the space or line break where it breaks.
                string source = text.Replace("\r", "");
                int part = Part(block, WordKind.Para, source), at = 0;
                for (int i = 0; i < rows.Count; i++)
                {
                    string row = rows[i];
                    int start = row.Length > 0 ? source.IndexOf(row, at, StringComparison.Ordinal) : at;
                    if (start < 0) start = at;
                    block.Segs.Add(new Seg { Part = part, Start = start, Text = row, Font = font, Box = new Rectangle(x, y + i * pitch, Math.Max(Z(2), WorkPaint.Wide(row, font)), pitch) });
                    at = start + row.Length;
                    if (at < source.Length && (source[at] == ' ' || source[at] == '\n')) at++;
                }
                return piece.Box.Bottom;
            }

            // A new part of a block's text, of one kind and one plain run.
            private static int Part(Block block, WordKind kind, string text)
            {
                WordBlock part = new WordBlock { Kind = kind };
                part.Runs.Add(new WordRun { Text = text, Code = kind == WordKind.Code });
                block.Parts.Add(part);
                return block.Parts.Count - 1;
            }

            // Lines cut at spaces to fit a width, and mid-word only when a word is wider than a whole line. Each candidate line
            // is measured whole, since words measured one by one add up wider than they draw.
            private static List<string> Wrap(string text, Font font, int width)
            {
                List<string> rows = new List<string>();
                foreach (string line in text.Replace("\r", "").Split('\n'))
                {
                    string row = null;
                    foreach (string word in line.Split(' '))
                    {
                        string next = row == null ? word : row + " " + word;
                        if (row != null && row.Length > 0 && word.Length > 0 && WorkPaint.Wide(next, font) > width) { rows.Add(row); next = word; }
                        while (next.Length > 1 && WorkPaint.Wide(next, font) > width)
                        {
                            int low = 1, high = next.Length - 1;
                            while (low < high) { int mid = (low + high + 1) / 2; if (WorkPaint.Wide(next.Substring(0, mid), font) <= width) low = mid; else high = mid - 1; }
                            rows.Add(next.Substring(0, low));
                            next = next.Substring(low);
                        }
                        row = next;
                    }
                    rows.Add(row ?? "");
                }
                return rows;
            }

            // A code well: her mono font (or Windows' own when the code has letters hers lacks) on a dark well with a cut corner.
            private int Code(Block block, string text, string tag, int x, int y, int width)
            {
                List<string> lines = new List<string>(text.Replace("\r", "").Split('\n'));
                return Well(block, text, lines, Inks(lines.Count, Neon.White), tag, x, y, width, 0);
            }

            private static List<Color> Inks(int count, Color ink)
            {
                List<Color> inks = new List<Color>();
                for (int i = 0; i < count; i++) inks.Add(ink);
                return inks;
            }

            // Her mono rows in a well, each source line in its own ink (red for removed lines, green for added ones), wrapped at
            // a path separator or a space so a path never breaks mid-word. With most above 0 a long well stops after that many
            // rows and offers the rest as "N more lines".
            private int Well(Block block, string text, List<string> lines, List<Color> inks, string tag, int x, int y, int width, int most)
            {
                Font font = Look.Drawable(string.Join("\n", lines.ToArray())) ? window.mono : window.monoPlain;
                int pad = Z(10), top = tag.Length > 0 ? Z(16) : 0;
                int columns = Columns(font, width - pad * 2);
                List<string> rows = new List<string>();
                List<Color> rowInks = new List<Color>();
                // Where each row starts in the well's text, and each drawn character's place for a row that had a tab.
                List<int> starts = new List<int>();
                List<int[]> maps = new List<int[]>();
                int lineAt = 0;
                for (int i = 0; i < lines.Count; i++)
                {
                    string line = lines[i];
                    int[] map = null;
                    if (line.IndexOf('\t') >= 0)
                    {
                        List<int> places = new List<int>();
                        for (int k = 0; k < line.Length; k++) for (int n = line[k] == '\t' ? 4 : 1; n > 0; n--) places.Add(lineAt + k);
                        places.Add(lineAt + line.Length);
                        map = places.ToArray();
                    }
                    int drawn = 0;
                    foreach (string row in Fold(line, columns))
                    {
                        rows.Add(row);
                        rowInks.Add(inks[i]);
                        if (map == null) { starts.Add(lineAt + drawn); maps.Add(null); }
                        else
                        {
                            int[] own = new int[row.Length + 1];
                            for (int k = 0; k <= row.Length; k++) own[k] = map[Math.Min(drawn + k, map.Length - 1)];
                            starts.Add(own[0]);
                            maps.Add(own);
                        }
                        drawn += row.Length;
                    }
                    lineAt += line.Length + 1;
                }
                if (rows.Count == 0) { rows.Add(""); rowInks.Add(Neon.White); starts.Add(0); maps.Add(null); }
                int total = rows.Count, hidden = 0;
                if (most > 0 && total > most && !block.All) { hidden = total - most; rows.RemoveRange(most, hidden); rowInks.RemoveRange(most, hidden); }
                int rowTall = Math.Max(font.Height, TextRenderer.MeasureText("Mgjp|", font).Height) + Z(1);
                Piece piece = Add(block, text, font, Neon.White, new Rectangle(x, y, width, rows.Count * rowTall + pad * 2 + top));
                piece.Code = true;
                piece.Tag = tag;
                piece.Rows = rows;
                piece.Inks = rowInks;
                piece.RowTall = rowTall;
                int part = Part(block, WordKind.Code, string.Join("\n", lines.ToArray()));
                for (int i = 0; i < rows.Count; i++)
                    block.Segs.Add(new Seg { Part = part, Start = starts[i], Map = maps[i], Text = rows[i], Font = font, Box = new Rectangle(x + pad, y + pad + top + i * rowTall, Math.Max(Z(2), WorkPaint.Wide(rows[i], font)), rowTall) });
                int bottom = piece.Box.Bottom;
                if (most > 0 && total > most)
                {
                    string words = hidden > 0 ? hidden + (hidden == 1 ? " more line" : " more lines") : "Show fewer lines";
                    block.More = new Rectangle(x, bottom + Z(4), WorkPaint.Wide(words, window.small) + Z(4), Z(18));
                    Add(block, words, window.small, Neon.Cyan, block.More).Line = true;
                    bottom = block.More.Bottom;
                }
                return bottom;
            }

            private static int Columns(Font font, int width)
            {
                float each = WorkPaint.Wide(new string('M', 40), font) / 40f;
                return Math.Max(8, (int)(width / Math.Max(1f, each)));
            }

            // One line cut into rows that fit: after the last \, / or space that fits, and only mid-word when a word is longer
            // than a whole row.
            private static List<string> Fold(string line, int columns)
            {
                List<string> rows = new List<string>();
                string rest = line.Replace("\t", "    ");
                while (rest.Length > columns)
                {
                    int cut = -1;
                    for (int i = columns - 1; i >= columns / 3; i--) { char c = rest[i]; if (c == '\\' || c == '/' || c == ' ') { cut = i + 1; break; } }
                    if (cut < 0) cut = columns;
                    rows.Add(rest.Substring(0, cut));
                    rest = rest.Substring(cut);
                }
                rows.Add(rest);
                return rows;
            }

            // What an edit changed, from the tool's own input: removed lines in her red, added lines in her green.
            private static bool Diff(string name, string input, List<string> lines, List<Color> inks)
            {
                if (string.IsNullOrEmpty(input) || (name != "Edit" && name != "MultiEdit" && name != "Write" && name != "NotebookEdit")) return false;
                Dictionary<string, object> map = null;
                try { map = HuskJson.Read(input) as Dictionary<string, object>; } catch (FormatException) { }
                if (map == null) return false;
                List<Dictionary<string, object>> edits = new List<Dictionary<string, object>>();
                List<object> many = HuskJson.List(map.ContainsKey("edits") ? map["edits"] : null);
                if (many != null) foreach (object one in many) { Dictionary<string, object> edit = one as Dictionary<string, object>; if (edit != null) edits.Add(edit); }
                else edits.Add(map);
                foreach (Dictionary<string, object> edit in edits)
                {
                    string old = Text(edit, "old_string"), added = Text(edit, "new_string");
                    if (added == null) added = Text(edit, "content") ?? Text(edit, "new_source");
                    if (old == null && added == null) continue;
                    if (lines.Count > 0) { lines.Add("..."); inks.Add(Neon.Faint); }
                    if (!string.IsNullOrEmpty(old)) foreach (string row in old.Replace("\r", "").TrimEnd('\n').Split('\n')) { lines.Add("- " + row); inks.Add(Removed); }
                    if (!string.IsNullOrEmpty(added)) foreach (string row in added.Replace("\r", "").TrimEnd('\n').Split('\n')) { lines.Add("+ " + row); inks.Add(Added); }
                    if (lines.Count > 400) { lines.RemoveRange(400, lines.Count - 400); inks.RemoveRange(400, inks.Count - 400); map["cut"] = true; break; }
                }
                if (lines.Count > 0 && true.Equals(HuskJson.Get(map, "cut"))) { lines.Add("... the rest of this change isn't kept here"); inks.Add(Neon.Faint); }
                return lines.Count > 0;
            }

            private static readonly Color Added = Color.FromArgb(30, 240, 138);
            private static Color Removed { get { return Neon.Hot; } }

            private static string Text(Dictionary<string, object> map, string key)
            {
                object value;
                return map.TryGetValue(key, out value) ? value as string : null;
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
                    if (any) y += Z(10);
                    y = Prose(block, paragraph, window.Font, Neon.White, x, y, width);
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
                            if (any) y += Z(12);
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
                        if (any) y += Z(14);
                        y = Prose(block, Plain(head.Groups[1].Value), window.strong, Neon.White, x, y, width);
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

            // What an approval card shows: for Bash and PowerShell the command, with its description above it; for an edit what
            // it changes; for anything else its input, without plumbing like timeouts. Paths read from the chat's folders.
            private bool Asked(WorkspaceItem item, out string about, List<string> lines, List<Color> inks)
            {
                about = "";
                Dictionary<string, object> map = null;
                try { map = HuskJson.Read(item.Input) as Dictionary<string, object>; } catch (FormatException) { }
                if (map == null)
                {
                    string shown = item.Input.Length > 0 ? item.Input : item.Text;
                    lines.AddRange(window.Shorten(shown).Replace("\r", "").Split('\n'));
                    inks.AddRange(Inks(lines.Count, Neon.White));
                    return false;
                }
                string command = Text(map, "command");
                if ((item.Name == "Bash" || item.Name == "PowerShell") && command != null)
                {
                    about = Text(map, "description") ?? "";
                    lines.AddRange(window.Display(command).Replace("\r", "").Split('\n'));
                    inks.AddRange(Inks(lines.Count, Neon.White));
                    return false;
                }
                string file = Text(map, "file_path") ?? Text(map, "notebook_path");
                if (file != null && Diff(item.Name, item.Input, lines, inks)) { about = window.Shorten(file); return false; }
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (pair.Key == "timeout" || pair.Key == "run_in_background" || pair.Key == "dangerouslyDisableSandbox") continue;
                    string value = pair.Value as string ?? HuskJson.Write(pair.Value);
                    foreach (string row in window.Shorten(pair.Key + ": " + value).Replace("\r", "").Split('\n')) { lines.Add(row); inks.Add(Neon.White); }
                }
                if (lines.Count == 0) { lines.Add(item.Text); inks.Add(Neon.White); }
                return true;
            }

            // What a waiting call will do, as one plain sentence: "Search MODDING.md for “menu”", "Read Mods.cs", or a
            // command's own description.
            private string Plainly(WorkspaceItem item)
            {
                string name = item.Name, kind = WorkPaint.ToolKind(name);
                Dictionary<string, object> map = null;
                try { map = HuskJson.Read(item.Input) as Dictionary<string, object>; } catch (FormatException) { }
                if (map == null) return "Use " + name;
                string path = Text(map, "file_path") ?? Text(map, "notebook_path") ?? Text(map, "path");
                string where = path == null ? "" : window.Shorten(path), pattern = Text(map, "pattern");
                if (name == "Grep") return "Search " + (where.Length > 0 ? where : "the files") + (pattern != null ? " for \u201c" + pattern + "\u201d" : "");
                if (name == "Glob") return "Find files matching \u201c" + (pattern ?? "") + "\u201d" + (where.Length > 0 ? " in " + where : "");
                if (name == "LS") return "List what's in " + (where.Length > 0 ? where : "the folder");
                if (kind == "page") return "Read " + where;
                if (name == "Write") return "Write " + where;
                if (kind == "pencil") return "Edit " + where;
                if (kind == "terminal")
                {
                    string about = (Text(map, "description") ?? "").Trim();
                    return about.Length > 0 ? about : name == "PowerShell" ? "Run a PowerShell command" : "Run a command";
                }
                if (name == "WebFetch") return "Open " + (Text(map, "url") ?? "a web page");
                if (name == "WebSearch") return "Search the web for \u201c" + (Text(map, "query") ?? "") + "\u201d";
                if (kind == "branch") { string about = (Text(map, "description") ?? "").Trim(); return "Start a helper agent" + (about.Length > 0 ? ": " + about : ""); }
                if (kind == "list") return "Update the to-do list";
                if (kind == "plug") { string[] bits = name.Split(new[] { "__" }, StringSplitOptions.None); return "Use a " + Noun(name) + ": " + bits[bits.Length - 1].Replace('_', ' '); }
                return "Use " + name;
            }

            // The trust card in plain words: which folders, what trusting them lets in, and where they are.
            private int Trusting(Block block, int x, int y, int width)
            {
                List<string> folders;
                try { folders = window.chat == null ? new List<string>() : window.workspace.Untrusted(window.chat); }
                catch (Exception) { folders = new List<string>(); }
                if (folders.Count == 0) return Words(block, block.Item.Text, window.Font, Neon.White, x, y, width);
                List<string> names = new List<string>();
                foreach (string folder in folders) { string name = Path.GetFileName(folder.TrimEnd('\\', '/')); names.Add(name.Length > 0 ? name : folder); }
                string named = names.Count == 1 ? names[0] : string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
                y = Words(block, "Trust " + named + "?", window.strong, Neon.White, x, y, width) + Z(4);
                string words = folders.Count == 1
                    ? "A folder can carry its own Claude Code set-up, including commands that run by themselves. Trust it if it's yours or you know where it came from."
                    : "Folders can carry their own Claude Code set-up, including commands that run by themselves. Trust them if they're yours or you know where they came from.";
                y = Words(block, words, window.Font, Neon.White, x, y, width) + Z(6);
                foreach (string folder in folders)
                {
                    Piece path = Add(block, window.Display(folder), window.note, Neon.Faint, new Rectangle(x, y, width, Z(16)));
                    path.Line = true;
                    path.Tag = "path";
                    y = path.Box.Bottom + Z(2);
                }
                return y;
            }

            private void Measure(Block block, int y)
            {
                block.Pieces.Clear();
                block.Parts.Clear();
                block.Segs.Clear();
                block.Toggle = block.More = Rectangle.Empty;
                block.Mark = RectangleF.Empty;
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
                    // A message of only files has no line of words above them.
                    int bottom = item.Text.Length == 0 && files.Count > 0 ? top - Z(6) : Prose(block, item.Text, window.Font, Neon.White, x + pad, top, wide - pad * 2);
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
                    int bottom = Markdown(block, item.Text, left + pad, y, width - pad);
                    block.Box = new Rectangle(left, y, width, bottom - y);
                    return;
                }
                if (Row(block))
                {
                    int top = y;
                    block.Head = Rectangle.Empty;
                    if (block.Group != null)
                    {
                        block.Head = new Rectangle(left, y, width, Z(26));
                        top = block.Head.Bottom + Z(4);
                        if (!opened.Contains(item)) { block.Toggle = Rectangle.Empty; block.Box = block.Head; return; }
                    }
                    int indent = Z(18);
                    block.Toggle = new Rectangle(left + indent, top, width - indent, Z(24));
                    int bottom = block.Toggle.Bottom;
                    {
                        string shown = block.Open && Cut(block) ? item.Name : Detail(block, false);
                        int part = Part(block, WordKind.Para, shown), most = Math.Max(1, block.Toggle.Width - Z(52));
                        block.Segs.Add(new Seg { Part = part, Start = 0, Text = shown, Font = window.note, Box = new Rectangle(block.Toggle.Left + Z(26), block.Toggle.Top, Math.Max(Z(2), Math.Min(most, WorkPaint.Wide(shown, window.note))), block.Toggle.Height) });
                    }
                    if (block.Open)
                    {
                        int x = block.Toggle.Left + Z(26), wide = block.Toggle.Right - x;
                        // The whole command or path, once, when the row had to cut it; the row itself then shows only the tool.
                        if (Cut(block)) bottom = Prose(block, Detail(block, true), window.note, Neon.Faint, x, bottom + Z(4), wide);
                        string status = Status(block), said = status == "denied" ? Refusal(block) : status == "failed" ? "It failed." : "";
                        if (said.Length > 0) bottom = Prose(block, said, window.small, Pink, x, bottom + Z(4), wide);
                        if (item.Kind == "tool")
                        {
                            List<string> lines = new List<string>();
                            List<Color> inks = new List<Color>();
                            string copy = block.Result;
                            if (Diff(item.Name, item.Input, lines, inks)) copy = string.Join("\n", lines.ToArray());
                            else { lines.AddRange(window.Shorten(block.Result).Replace("\r", "").Split('\n')); inks = Inks(lines.Count, Neon.White); }
                            bottom = Well(block, copy, lines, inks, "", x, bottom + Z(6), wide, 14);
                        }
                        bottom += Z(4);
                    }
                    block.Box = new Rectangle(left, y, width, bottom - y);
                    return;
                }
                if (kind == "end")
                {
                    Piece piece = Add(block, EndShown(item.Text), window.note, Neon.Faint, new Rectangle(left, y, width, Z(16)));
                    piece.Line = true;
                    piece.Right = true;
                    block.Box = new Rectangle(left, y, width, Z(16));
                    return;
                }
                if (kind == "note" || kind == "stopped" || (kind == "approval" || kind == "trust") && !Waiting(item))
                {
                    int bottom = Words(block, window.Shorten(item.Text), window.note, kind == "approval" && item.Decision == "deny" ? Neon.Tone(255, 166, 162) : Neon.Faint, left + Z(22), y, width - Z(22));
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
                    string sentence = Plainly(item);
                    block.Mark = new RectangleF(left + pad, at + Z(2), Z(16), Z(16));
                    at = Words(block, sentence, window.strong, Neon.White, left + pad + Z(26), at, inner - Z(26)) + Z(4);
                    string about;
                    List<string> lines = new List<string>();
                    List<Color> inks = new List<Color>();
                    bool raw = Asked(item, out about, lines, inks);
                    if (about.Length > 0 && about != sentence) at = Words(block, about, window.small, Neon.Faint, left + pad, at, inner) + Z(8);
                    else at += Z(4);
                    // A command or a change is what he's agreeing to, so it shows; anything else is its raw input, one click away.
                    if (raw)
                    {
                        string shownAs = block.All ? "Hide the details" : "Show the details";
                        Piece link = Add(block, shownAs, window.small, Neon.Cyan, new Rectangle(left + pad, at, WorkPaint.Wide(shownAs, window.small) + Z(4), Z(20)));
                        link.Line = true;
                        block.Toggle = link.Box;
                        at = link.Box.Bottom + Z(6);
                        if (block.All) at = Well(block, string.Join("\n", lines.ToArray()), lines, inks, "", left + pad, at, inner, 0);
                    }
                    else at = Well(block, string.Join("\n", lines.ToArray()), lines, inks, "", left + pad, at, inner, 12);
                    at += Z(10);
                    if (block.Reason != null) { block.Reason.Tag = new Rectangle(left + pad, at, inner, Z(30)); at += Z(38); }
                }
                else if (kind == "trust") at = Trusting(block, left + pad, at, inner) + Z(10);
                else at = Words(block, window.Shorten(item.Text), window.Font, Neon.White, left + pad, at, inner) + Z(10);
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
                Shimmer();
            }

            internal void ScrollTo(int to)
            {
                int target = Math.Max(0, Math.Min(to, Content - Height));
                if (target != Offset) { Offset = target; Place(); }
                // Once the kept room is out of sight it goes, so the scroll bar is true again.
                if (slack > 0 && Offset + Height <= Content - slack) { Content -= slack; slack = 0; Invalidate(); }
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
                    TextRenderer.DrawText(e.Graphics, Empty, window.note, new Rectangle(Inset + Z(14), Z(14), Edge - Inset - Z(14), Math.Max(Z(60), Height - Z(28))), Neon.Faint, WorkPaint.Wrap | TextFormatFlags.PathEllipsis);
                WorkPaint.Rail(e.Graphics, WorkPaint.Track(ClientRectangle, window.zoom), Height, Content, Offset);
            }

            private void Draw(Graphics graphics, Rectangle area)
            {
                WorkPaint.Behind(graphics, this);
                GraphicsState state = graphics.Save();
                graphics.TranslateTransform(0, -Offset);
                Rectangle view = new Rectangle(area.Left, area.Top + Offset, area.Width, area.Height);
                foreach (Block block in Blocks) if (block.Box.IntersectsWith(Rectangle.Inflate(view, 0, Z(8)))) PaintBlock(graphics, block);
                if (!thinkLine.IsEmpty && thinkLine.IntersectsWith(view)) PaintThinking(graphics);
                PaintCopyKey(graphics);
                graphics.Restore(state);
            }

            private void PaintBlock(Graphics graphics, Block block)
            {
                float s = window.zoom, line = Math.Max(1f, s);
                WorkspaceItem item = block.Item;
                string kind = item.Kind;
                if (kind == "user") WorkPaint.Plate(graphics, block.Box, 8 * s, Neon.Deep, Neon.Tone(28, 84, 92), line);
                else if (kind == "text") using (Brush bar = new SolidBrush(Neon.Rose)) graphics.FillRectangle(bar, block.Box.Left, block.Box.Top + Z(2), Math.Max(2, Z(2)), block.Box.Height - Z(2));
                else if (Row(block))
                {
                    if (block.Group != null) PaintHead(graphics, block);
                    if (!block.Toggle.IsEmpty) PaintRow(graphics, block);
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
                    if (!block.Mark.IsEmpty) WorkPaint.ToolMark(graphics, item.Name, block.Mark, Neon.White, s);
                }
                // Grounds first (a well's plate), then the selection over them, then the words over that.
                foreach (Piece piece in block.Pieces) if (piece.Code) WorkPaint.Plate(graphics, piece.Box, 6 * s, Neon.Well, Neon.WellEdge, line);
                PaintSelection(graphics, block);
                foreach (Piece piece in block.Pieces)
                {
                    if (piece.Code)
                    {
                        int pad = Z(10), top = 0;
                        if (!string.IsNullOrEmpty(piece.Tag)) { TextRenderer.DrawText(graphics, piece.Tag.ToUpperInvariant(), window.caption, new Rectangle(piece.Box.Left + pad, piece.Box.Top + Z(6), piece.Box.Width - pad * 2, Z(14)), Neon.Faint, WorkPaint.Line); top = Z(16); }
                        for (int i = 0; i < piece.Rows.Count; i++)
                            TextRenderer.DrawText(graphics, piece.Rows[i], piece.Font, new Rectangle(piece.Box.Left + pad, piece.Box.Top + pad + top + i * piece.RowTall, piece.Box.Width - pad * 2, piece.RowTall), piece.Inks[i], WorkPaint.Line | TextFormatFlags.EndEllipsis);
                        continue;
                    }
                    if (piece.Rows != null)
                    {
                        for (int i = 0; i < piece.Rows.Count; i++)
                            TextRenderer.DrawText(graphics, piece.Rows[i], piece.Font, new Rectangle(piece.Box.Left, piece.Box.Top + i * piece.RowTall, piece.Box.Width, piece.RowTall), piece.Ink, WorkPaint.Line | TextFormatFlags.NoClipping);
                        continue;
                    }
                    if (piece.Tag == "file") WorkPaint.FileMark(graphics, new RectangleF(piece.Box.Left, piece.Box.Top + Z(2), Z(12), piece.Box.Height - Z(4)), Neon.Cyan, s);
                    Rectangle box = piece.Tag == "file" ? new Rectangle(piece.Box.Left + Z(18), piece.Box.Top, piece.Box.Width - Z(18), piece.Box.Height) : piece.Box;
                    TextFormatFlags flags = piece.Line ? WorkPaint.Line | (piece.Tag == "path" ? TextFormatFlags.PathEllipsis : TextFormatFlags.EndEllipsis) | (piece.Right ? TextFormatFlags.Right : TextFormatFlags.Left) : WorkPaint.Wrap;
                    TextRenderer.DrawText(graphics, piece.Text, piece.Font, box, piece.Ink, flags);
                }
            }

            // Her palette's green for tool calls, quiet once they're done and bright while one runs, and its pink for trouble.
            private static Color Green { get { return Neon.Tone(1, 247, 109); } }
            private static Color Pink { get { return Neon.Tone(253, 0, 79); } }
            private static Color Quiet(Color ink) { return Mix(Neon.Field, ink, 0.62f); }
            // A running line sits a step below full green so the shine passing over it reads.
            private static Color Live { get { return Mix(Neon.Field, Green, 0.8f); } }

            private static Color Mix(Color from, Color to, float t)
            {
                return Color.FromArgb((int)(from.R + (to.R - from.R) * t), (int)(from.G + (to.G - from.G) * t), (int)(from.B + (to.B - from.B) * t));
            }

            // The summary line: a caret, the group's picture, what it did (or what's running, shining) and any trouble in pink.
            private void PaintHead(Graphics graphics, Block block)
            {
                float s = window.zoom;
                Rectangle head = block.Head;
                bool open = opened.Contains(block.Item);
                if (block == hovered && hoveredHead) WorkPaint.Plate(graphics, head, 5 * s, Neon.Hover, Color.Empty, 0);
                float mid = head.Top + head.Height / 2f, cx = head.Left + Z(8);
                PointF[] caret = open
                    ? new[] { new PointF(cx, mid - 2 * s), new PointF(cx + 7 * s, mid - 2 * s), new PointF(cx + 3.5f * s, mid + 2.5f * s) }
                    : new[] { new PointF(cx + 1.5f * s, mid - 3.5f * s), new PointF(cx + 6 * s, mid), new PointF(cx + 1.5f * s, mid + 3.5f * s) };
                SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Brush fill = new SolidBrush(Neon.Faint)) graphics.FillPolygon(fill, caret);
                graphics.SmoothingMode = was;
                bool running = block.Doing != null;
                Color ink = running ? Live : block.HeadMark.Length > 0 ? Pink : Quiet(Green);
                RectangleF icon = new RectangleF(head.Left + Z(22), mid - Z(7), Z(14), Z(14));
                WorkPaint.ToolMark(graphics, block.HeadTool, icon, ink, s);
                int x = head.Left + Z(44), right = head.Right - Z(8);
                Font font = window.small;
                if (running)
                {
                    int wide = Math.Min(right - x, WorkPaint.Wide(block.Doing, font) + Z(2));
                    Rectangle doing = new Rectangle(x, head.Top, Math.Max(1, wide), head.Height);
                    TextRenderer.DrawText(graphics, block.Doing, font, doing, Live, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    Shine(graphics, head, icon, block.HeadTool, doing, block.Doing, font, WorkPaint.Line | TextFormatFlags.EndEllipsis, Live, block == hovered && hoveredHead ? Neon.Hover : Color.Empty);
                    x = doing.Right + Z(10);
                }
                string words = block.Words + (block.Words.Length > 0 && block.Trouble.Length > 0 ? "," : "");
                if (words.Length > 0 && x < right)
                {
                    int wide = Math.Min(right - x, WorkPaint.Wide(words, font) + Z(2));
                    TextRenderer.DrawText(graphics, words, font, new Rectangle(x, head.Top, Math.Max(1, wide), head.Height), running ? Neon.Faint : Quiet(Green), WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    // The trouble follows the comma as one sentence would, a space after it.
                    x += block.Trouble.Length > 0 ? WorkPaint.Wide(words + " x", font) - WorkPaint.Wide("x", font) : wide;
                }
                if (block.Trouble.Length > 0 && x < right)
                {
                    string trouble = block.Trouble;
                    int wide = Math.Min(right - x, WorkPaint.Wide(trouble, font) + Z(2));
                    TextRenderer.DrawText(graphics, trouble, font, new Rectangle(x, head.Top, Math.Max(1, wide), head.Height), Pink, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                    x += wide;
                }
                if (block.HeadMark.Length > 0 && x + Z(14) < right) WorkPaint.StatusMark(graphics, "failed", new PointF(x + Z(9), mid), Z(4), Pink, s);
            }

            // Claude thinking between calls: a quiet line at the end with the same shine as a running call, so a long think
            // never looks like nothing is happening.
            private void PaintThinking(Graphics graphics)
            {
                Rectangle line = thinkLine;
                float mid = line.Top + line.Height / 2f;
                RectangleF icon = new RectangleF(line.Left + Z(22), mid - Z(7), Z(14), Z(14));
                WorkPaint.ToolMark(graphics, WorkPaint.Thinking, icon, Live, window.zoom);
                const string words = "Thinking";
                Rectangle box = new Rectangle(line.Left + Z(44), line.Top, Math.Max(1, Math.Min(line.Width - Z(52), WorkPaint.Wide(words, window.small) + Z(2))), line.Height);
                TextRenderer.DrawText(graphics, words, window.small, box, Live, WorkPaint.Line | TextFormatFlags.EndEllipsis);
                Shine(graphics, line, icon, WorkPaint.Thinking, box, words, window.small, WorkPaint.Line | TextFormatFlags.EndEllipsis, Live, Color.Empty);
            }

            internal bool Thinking { get { return !thinkLine.IsEmpty; } }

            // One call inside an open group: its picture, its command or path on one line, and its mark.
            private void PaintRow(Graphics graphics, Block block)
            {
                float s = window.zoom;
                Rectangle row = block.Toggle;
                WorkspaceItem item = block.Item;
                if (block == hovered && !hoveredHead || block.Open) WorkPaint.Plate(graphics, row, 5 * s, block == hovered && !hoveredHead ? Neon.Hover : Neon.Band, Color.Empty, 0);
                using (Pen rule = new Pen(Neon.Rule, Math.Max(1f, s))) graphics.DrawLine(rule, block.Box.Left + Z(9), row.Top - Z(2), block.Box.Left + Z(9), block.Box.Bottom);
                string status = Status(block);
                Color ink = block.Running ? Live : status == "failed" || status == "denied" ? Pink : Quiet(Green);
                Color words = block.Running ? Live : status == "failed" ? Pink : status == "denied" ? Neon.Faint : Quiet(Green);
                RectangleF icon = new RectangleF(row.Left + Z(6), row.Top + (row.Height - Z(13)) / 2f, Z(13), Z(13));
                WorkPaint.ToolMark(graphics, item.Name, icon, ink, s);
                string text = block.Open && Cut(block) ? item.Name : Detail(block, false);
                TextFormatFlags flags = WorkPaint.Line | (Command(item.Name) || item.Kind == "approval" ? TextFormatFlags.EndEllipsis : TextFormatFlags.PathEllipsis);
                Rectangle at = new Rectangle(row.Left + Z(26), row.Top, Math.Max(1, row.Width - Z(26) - Z(26)), row.Height);
                TextRenderer.DrawText(graphics, text, window.note, at, words, flags);
                if (status.Length > 0) WorkPaint.StatusMark(graphics, status, new PointF(row.Right - Z(12), row.Top + row.Height / 2f), Z(4), status == "asked" ? Quiet(Green) : Pink, s);
                if (block.Running) Shine(graphics, row, icon, item.Name, at, text, window.note, flags, Live, block == hovered && !hoveredHead ? Neon.Hover : block.Open ? Neon.Band : Color.Empty);
            }

            // A soft highlight sweeping left to right over a running line's picture and words, about 1.5 s a pass: the line is
            // drawn again in thin slices, each on its own ground and a step brighter towards the middle of the band, so the
            // letters never pile up over themselves.
            private void Shine(Graphics graphics, Rectangle area, RectangleF icon, string tool, Rectangle box, string text, Font font, TextFormatFlags flags, Color ink, Color plate)
            {
                double phase = ShineFixed >= 0 ? ShineFixed : (shineTime.ElapsedMilliseconds % 1500) / 1500.0;
                int from = (int)icon.Left - Z(2), to = box.Left + Math.Min(box.Width, WorkPaint.Wide(text, font)) + Z(2);
                int band = Z(84);
                float centre = (float)(from - band / 2f + phase * (to - from + band));
                const int slices = 14;
                float step = band / (float)slices;
                // Drawn in the control's own pixels, with the shift and clip kept by hand. A Graphics.Save taken under the scroll's
                // shift, then a clip, makes TextRenderer draw nothing, so a scrolled conversation lost the running words under
                // the band.
                Matrix kept = graphics.Transform;
                Region clip = graphics.Clip;
                float dx = kept.OffsetX, dy = kept.OffsetY;
                graphics.ResetTransform();
                area.Offset((int)dx, (int)dy);
                box.Offset((int)dx, (int)dy);
                icon.Offset(dx, dy);
                from += (int)dx;
                to += (int)dx;
                centre += dx;
                for (int k = 0; k < slices; k++)
                {
                    float a = (float)Math.Floor(centre - band / 2f + k * step), b = (float)Math.Floor(centre - band / 2f + (k + 1) * step);
                    float left = Math.Max(a, from), right = Math.Min(b, to);
                    if (right <= left) continue;
                    float t = 1f - Math.Abs((k + 0.5f) / slices * 2f - 1f);
                    Color lit = Mix(ink, Mix(Green, Neon.White, 0.7f), t * t * (3 - 2 * t));
                    graphics.SetClip(new RectangleF(left, area.Top, right - left, area.Height));
                    WorkPaint.Behind(graphics, this);
                    if (plate.A > 0) WorkPaint.Plate(graphics, area, 5 * window.zoom, plate, Color.Empty, 0);
                    WorkPaint.ToolMark(graphics, tool, icon, lit, window.zoom);
                    TextRenderer.DrawText(graphics, text, font, box, lit, flags);
                }
                graphics.Transform = kept;
                graphics.Clip = clip;
                kept.Dispose();
                clip.Dispose();
            }

            // Where the shine draws right now, in this control's own space; nothing when no call runs where it can be seen.
            private List<Rectangle> ShineBoxes()
            {
                List<Rectangle> boxes = new List<Rectangle>();
                foreach (Block block in Blocks)
                {
                    if (block.Hidden) continue;
                    if (block.Group != null && block.Doing != null && !block.Head.IsEmpty) boxes.Add(block.Head);
                    if (block.Running && !block.Toggle.IsEmpty) boxes.Add(block.Toggle);
                }
                if (!thinkLine.IsEmpty) boxes.Add(thinkLine);
                List<Rectangle> seen = new List<Rectangle>();
                foreach (Rectangle box in boxes) { Rectangle at = box; at.Offset(0, -Offset); if (at.IntersectsWith(ClientRectangle)) seen.Add(at); }
                return seen;
            }

            internal Rectangle ShineBox() { List<Rectangle> boxes = ShineBoxes(); return boxes.Count > 0 ? boxes[0] : Rectangle.Empty; }

            internal bool Shining { get { return shineClock.Enabled; } }

            // The clock runs only while a running call can be seen: not when nothing runs, the window is hidden or minimised.
            internal void Shimmer()
            {
                if (ShineWanted()) shineClock.Start(); else shineClock.Stop();
            }

            private bool ShineWanted()
            {
                return IsHandleCreated && Visible && window.Visible && window.WindowState != FormWindowState.Minimized && ShineBoxes().Count > 0;
            }

            // Each frame repaints only the running lines.
            private void ShineTick()
            {
                if (!ShineWanted()) { shineClock.Stop(); return; }
                foreach (Rectangle box in ShineBoxes()) Invalidate(box);
            }

            protected override void OnVisibleChanged(EventArgs e) { base.OnVisibleChanged(e); Shimmer(); }

            protected override void Dispose(bool disposing)
            {
                if (disposing) { shineClock.Stop(); shineClock.Dispose(); edgeClock.Dispose(); tickClock.Dispose(); tips.Dispose(); }
                base.Dispose(disposing);
            }

            // What hovering a line tells: the tool's name and what came of it, or what a group holds.
            private string TipFor(Block block, bool head)
            {
                if (block == null) return null;
                if (head)
                {
                    List<string> names = new List<string>();
                    foreach (Block one in block.Group) if (!names.Contains(one.Item.Name)) names.Add(one.Item.Name);
                    return block.Group.Count + (block.Group.Count == 1 ? " tool call: " : " tool calls: ") + string.Join(", ", names.ToArray());
                }
                string status = Status(block), how = block.Running ? " \u00b7 running" : status == "failed" ? " \u00b7 failed" : status == "denied" ? " \u00b7 denied" : status == "asked" ? " \u00b7 you allowed it" : "";
                string detail = Detail(block, true);
                if (detail.Length > 300) detail = detail.Substring(0, 300) + "\u2026";
                return block.Item.Name + how + "\n" + detail;
            }

            private Block At(Point point, out Piece code)
            {
                code = null;
                Point inside = new Point(point.X, point.Y + Offset);
                foreach (Block block in Blocks)
                {
                    if (block.Hidden) continue;
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
                    RectangleF track = WorkPaint.Track(ClientRectangle, window.zoom), thumb = WorkPaint.Thumb(track, Height, Content, Offset);
                    float room = track.Height - thumb.Height;
                    if (room > 0) ScrollTo(dragOffset + (int)((e.Y - dragFrom) * (Content - Height) / room));
                    return;
                }
                if (selecting) { Drag(e.Location); return; }
                Piece code;
                Block over = At(e.Location, out code);
                bool head = over != null && !over.Head.IsEmpty && over.Head.Contains(new Point(e.X, e.Y + Offset));
                bool link = over != null && !over.Toggle.IsEmpty && over.Toggle.Contains(new Point(e.X, e.Y + Offset));
                bool more = over != null && !over.More.IsEmpty && over.More.Contains(new Point(e.X, e.Y + Offset));
                CopyHover(e.Location);
                bool key = !copyKey.IsEmpty && copyKey.Contains(new Point(e.X, e.Y + Offset));
                Cursor = head || link || more || key ? Cursors.Hand : OnText(new Point(e.X, e.Y + Offset)) ? Cursors.IBeam : Cursors.Default;
                Block hot = head || link && Row(over) ? over : null;
                if (hot != hovered || head != hoveredHead)
                {
                    hovered = hot;
                    hoveredHead = head;
                    Invalidate();
                    string tip = TipFor(hot, head);
                    if (tip != tipShown) { tipShown = tip; tips.SetToolTip(this, tip); }
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hovered != null) { hovered = null; Invalidate(); }
                if (!selecting) CopyHover(new Point(-1, -1));
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                Focus();
                if (e.Button == MouseButtons.Left && e.X >= Width - Z(12) && Content > Height) { dragging = true; dragFrom = e.Y; dragOffset = Offset; Capture = true; Invalidate(); return; }
                Piece code;
                Block block = At(e.Location, out code);
                if (block != null && e.Button == MouseButtons.Left && !block.More.IsEmpty && block.More.Contains(new Point(e.X, e.Y + Offset)))
                {
                    block.All = !block.All;
                    int kept = Offset;
                    Arrange(true);
                    ScrollTo(kept);
                    return;
                }
                if (block != null && e.Button == MouseButtons.Left && !block.Head.IsEmpty && block.Head.Contains(new Point(e.X, e.Y + Offset)))
                {
                    if (!opened.Remove(block.Item)) opened.Add(block.Item);
                    int kept = Offset;
                    Arrange(true);
                    ScrollTo(kept);
                    return;
                }
                if (block != null && e.Button == MouseButtons.Left && !block.Toggle.IsEmpty && block.Toggle.Contains(new Point(e.X, e.Y + Offset)))
                {
                    if (Row(block)) block.Open = !block.Open; else block.All = !block.All;
                    bool follow = Following;
                    int was = Offset;
                    Arrange(true);
                    ScrollTo(follow && block == Blocks[Blocks.Count - 1] ? int.MaxValue : was);
                    return;
                }
                if (e.Button == MouseButtons.Left && !copyKey.IsEmpty && copyKey.Contains(new Point(e.X, e.Y + Offset))) { CopyKey(); return; }
                picked = block;
                if (e.Button == MouseButtons.Left) Press(e.Location);
                if (block != null && e.Button == MouseButtons.Right) CopyMenu(block, code, e.Location);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                base.OnMouseUp(e);
                if (dragging) { dragging = false; Capture = false; Invalidate(); }
                if (selecting) { selecting = false; Capture = false; edgeClock.Stop(); }
            }

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
                if (e.Control && (e.KeyCode == System.Windows.Forms.Keys.C || e.KeyCode == System.Windows.Forms.Keys.Insert))
                {
                    string chosen = Selected();
                    if (chosen.Length > 0) Copy(chosen);
                    else if (picked != null) Copy(CopyOf(picked.Item));
                    e.Handled = true;
                    return;
                }
                if (e.Control && e.KeyCode == System.Windows.Forms.Keys.A) { SelectAll(); e.Handled = true; return; }
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
                foreach (KeyValuePair<string, string> item in MenuFor(block, code)) { string text = item.Value; menu.Items.Add(item.Key, null, delegate { Copy(text); }); }
                menu.Closed += delegate { window.Post(delegate { menu.Dispose(); own.Dispose(); }); };
                window.openMenu = menu;
                menu.Show(this, at);
            }

            // What right-clicking offers and what each copies: the selection when there is one, then the message and its code.
            internal List<KeyValuePair<string, string>> MenuFor(Block block, Piece code)
            {
                List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
                string chosen = Selected();
                if (chosen.Length > 0) items.Add(new KeyValuePair<string, string>("Copy", chosen));
                if (block != null) items.Add(new KeyValuePair<string, string>("Copy message", CopyOf(block.Item)));
                if (code != null) items.Add(new KeyValuePair<string, string>("Copy code", CodeOf(block, code)));
                return items;
            }

            private void Copy(string text)
            {
                if (string.IsNullOrEmpty(text)) return;
                try { ClipboardWrite(text); }
                catch (ExternalException) { }
            }

            // A well's code as it shows, tabs and all.
            private static string CodeOf(Block block, Piece code)
            {
                foreach (Seg seg in block.Segs)
                    if (code.Box.Contains(seg.Box.Location) && seg.Part < block.Parts.Count && block.Parts[seg.Part].Kind == WordKind.Code) return WorkWords.Shown(block.Parts[seg.Part]);
                return code.Text;
            }

            // ---- Selecting text: press, drag, double and triple click, Shift+click, Ctrl+A, and scrolling past an edge ----

            private Spot anchor, caret;
            private bool selecting;
            // Whether Shift is held, from the keyboard, or from the smoke.
            internal Func<bool> ShiftDown = delegate { return (ModifierKeys & System.Windows.Forms.Keys.Shift) != 0; };
            private int clicks;
            private long lastPress = -10000;
            private Point pressedAt;
            private Point dragAt;
            private readonly System.Windows.Forms.Timer edgeClock = new System.Windows.Forms.Timer { Interval = 40 };
            private static readonly Stopwatch pressClock = Stopwatch.StartNew();
            private static Color Chosen { get { return Color.FromArgb(78, Neon.Cyan); } }

            private bool OnText(Point inside)
            {
                foreach (Block block in Blocks)
                {
                    if (block.Hidden || !Rectangle.Inflate(block.Box, 0, Z(4)).Contains(inside)) continue;
                    foreach (Seg seg in block.Segs) if (seg.Box.Contains(inside)) return true;
                }
                return false;
            }

            // The nearest place in the text to a point (in the conversation's own space): the closest line, then the gap
            // between characters nearest the point.
            private bool Hit(Point inside, out Spot spot)
            {
                spot = new Spot();
                Seg best = null;
                Block owner = null;
                long bestFar = long.MaxValue;
                foreach (Block block in Blocks)
                {
                    if (block.Hidden) continue;
                    foreach (Seg seg in block.Segs)
                    {
                        long dy = inside.Y < seg.Box.Top ? seg.Box.Top - inside.Y : inside.Y >= seg.Box.Bottom ? inside.Y - seg.Box.Bottom + 1 : 0;
                        long dx = inside.X < seg.Box.Left ? seg.Box.Left - inside.X : inside.X > seg.Box.Right ? inside.X - seg.Box.Right : 0;
                        long far = dy * 100000 + dx;
                        if (far < bestFar) { bestFar = far; best = seg; owner = block; }
                    }
                }
                if (best == null) return false;
                spot.Block = owner;
                spot.Part = best.Part;
                spot.At = best.At(Nearest(best, inside.X));
                return true;
            }

            // The drawn character gap nearest an x.
            private static int Nearest(Seg seg, int x)
            {
                if (x <= seg.Box.Left) return 0;
                int low = 0, high = seg.Text.Length;
                while (low < high)
                {
                    int mid = (low + high + 1) / 2;
                    if (seg.Box.Left + WorkPaint.Wide(seg.Text.Substring(0, mid), seg.Font) <= x) low = mid; else high = mid - 1;
                }
                if (low < seg.Text.Length)
                {
                    int here = seg.Box.Left + WorkPaint.Wide(seg.Text.Substring(0, low), seg.Font), next = seg.Box.Left + WorkPaint.Wide(seg.Text.Substring(0, low + 1), seg.Font);
                    if (x - here > next - x) low++;
                }
                return low;
            }

            private int Order(Spot a, Spot b)
            {
                int ia = Blocks.IndexOf(a.Block), ib = Blocks.IndexOf(b.Block);
                if (ia != ib) return ia.CompareTo(ib);
                if (a.Part != b.Part) return a.Part.CompareTo(b.Part);
                return a.At.CompareTo(b.At);
            }

            // The selection's two ends in reading order, or false when nothing is selected or its text has gone.
            private bool Ends(out Spot from, out Spot to)
            {
                from = anchor;
                to = caret;
                if (anchor.Block == null || caret.Block == null || !Blocks.Contains(anchor.Block) || !Blocks.Contains(caret.Block)) return false;
                if (Order(from, to) > 0) { Spot swap = from; from = to; to = swap; }
                return Order(from, to) != 0;
            }

            // The selection's paste text: each block's part as WorkWords gives it, blocks a blank line apart.
            internal string Selected()
            {
                Spot from, to;
                if (!Ends(out from, out to)) return "";
                int first = Blocks.IndexOf(from.Block), last = Blocks.IndexOf(to.Block);
                StringBuilder text = new StringBuilder();
                for (int i = first; i <= last; i++)
                {
                    Block block = Blocks[i];
                    if (block.Hidden || block.Parts.Count == 0) continue;
                    int fromPart = i == first ? from.Part : 0, fromAt = i == first ? from.At : 0;
                    int toPart = i == last ? to.Part : block.Parts.Count - 1, toAt = i == last ? to.At : int.MaxValue;
                    if (fromPart >= block.Parts.Count) continue;
                    string piece = WorkWords.Plain(block.Parts, fromPart, fromAt, Math.Min(toPart, block.Parts.Count - 1), toAt);
                    if (piece.Length == 0) continue;
                    if (text.Length > 0) text.Append("\n\n");
                    text.Append(piece);
                }
                return text.ToString();
            }

            // How much of one part is selected, in its own characters.
            private bool Covers(int index, Block block, int part, Spot from, Spot to, out int low, out int high)
            {
                low = 0;
                high = int.MaxValue;
                int first = Blocks.IndexOf(from.Block), last = Blocks.IndexOf(to.Block);
                if (index < first || index > last) return false;
                if (index == first) { if (part < from.Part) return false; if (part == from.Part) low = from.At; }
                if (index == last) { if (part > to.Part) return false; if (part == to.Part) high = to.At; }
                return high > low;
            }

            private void PaintSelection(Graphics graphics, Block block)
            {
                Spot from, to;
                if (block.Segs.Count == 0 || !Ends(out from, out to)) return;
                int index = Blocks.IndexOf(block);
                using (Brush fill = new SolidBrush(Chosen))
                    foreach (Seg seg in block.Segs)
                    {
                        int low, high;
                        if (!Covers(index, block, seg.Part, from, to, out low, out high)) continue;
                        int start = seg.At(0), end = seg.End;
                        if (high < start || low > end) continue;
                        int a = 0, b = seg.Text.Length;
                        while (a < seg.Text.Length && seg.At(a) < low) a++;
                        while (b > 0 && seg.At(b) > high) b--;
                        if (b < a) continue;
                        int left = seg.Box.Left + (a == 0 ? 0 : WorkPaint.Wide(seg.Text.Substring(0, a), seg.Font));
                        int right = seg.Box.Left + (b == 0 ? 0 : WorkPaint.Wide(seg.Text.Substring(0, b), seg.Font));
                        // A selection that runs on past this line's end shows a little past it, as in an editor.
                        if (high > end && low <= end) right += Z(5);
                        if (right <= left) continue;
                        graphics.FillRectangle(fill, left, seg.Box.Top + Z(1), right - left, seg.Box.Height - Z(2));
                    }
            }

            private void Press(Point at)
            {
                long now = pressClock.ElapsedMilliseconds;
                Size near = SystemInformation.DoubleClickSize;
                clicks = now - lastPress <= SystemInformation.DoubleClickTime && Math.Abs(at.X - pressedAt.X) <= near.Width && Math.Abs(at.Y - pressedAt.Y) <= near.Height ? clicks + 1 : 1;
                lastPress = now;
                pressedAt = at;
                Spot spot;
                if (!Hit(new Point(at.X, at.Y + Offset), out spot)) { anchor = caret = new Spot(); Invalidate(); return; }
                if (clicks == 1 && ShiftDown() && anchor.Block != null && Blocks.Contains(anchor.Block)) caret = spot;
                else if (clicks == 2) Word(spot);
                else if (clicks >= 3) Whole(spot);
                else anchor = caret = spot;
                selecting = clicks == 1;
                if (selecting) Capture = true;
                Invalidate();
            }

            // Double click: the word under the pointer, letters, digits and _ together, or the one other character.
            private void Word(Spot spot)
            {
                string shown = WorkWords.Shown(spot.Block.Parts[spot.Part]);
                int a = Math.Min(spot.At, shown.Length), b = a;
                Func<char, bool> wordy = delegate(char c) { return char.IsLetterOrDigit(c) || c == '_'; };
                if (b < shown.Length && wordy(shown[b]) || a > 0 && wordy(shown[a - 1]))
                {
                    while (a > 0 && wordy(shown[a - 1])) a--;
                    while (b < shown.Length && wordy(shown[b])) b++;
                }
                else if (b < shown.Length && shown[b] != '\n') b++;
                anchor = new Spot { Block = spot.Block, Part = spot.Part, At = a };
                caret = new Spot { Block = spot.Block, Part = spot.Part, At = b };
            }

            // Triple click: the line in code, the whole paragraph or item anywhere else.
            private void Whole(Spot spot)
            {
                WordBlock part = spot.Block.Parts[spot.Part];
                string shown = WorkWords.Shown(part);
                int a = 0, b = shown.Length;
                if (part.Kind == WordKind.Code)
                {
                    int at = Math.Min(spot.At, shown.Length);
                    a = at > 0 ? shown.LastIndexOf('\n', at - 1) + 1 : 0;
                    int end = shown.IndexOf('\n', at);
                    b = end < 0 ? shown.Length : end;
                }
                anchor = new Spot { Block = spot.Block, Part = spot.Part, At = a };
                caret = new Spot { Block = spot.Block, Part = spot.Part, At = b };
            }

            internal void SelectAll()
            {
                Block first = null, last = null;
                foreach (Block block in Blocks) if (!block.Hidden && block.Parts.Count > 0) { if (first == null) first = block; last = block; }
                if (first == null) return;
                anchor = new Spot { Block = first, Part = 0, At = 0 };
                caret = new Spot { Block = last, Part = last.Parts.Count - 1, At = WorkWords.Shown(last.Parts[last.Parts.Count - 1]).Length };
                Invalidate();
            }

            // Dragging: the caret follows the pointer, and past the top or bottom edge the conversation scrolls under it.
            private void Drag(Point at)
            {
                dragAt = at;
                Spot spot;
                Point inside = new Point(at.X, Math.Max(0, Math.Min(Height - 1, at.Y)) + Offset);
                if (Hit(inside, out spot) && (spot.Block != caret.Block || spot.Part != caret.Part || spot.At != caret.At)) { caret = spot; Invalidate(); }
                if (at.Y < 0 || at.Y >= Height) { if (!edgeClock.Enabled) edgeClock.Start(); } else edgeClock.Stop();
            }

            internal void EdgeTick()
            {
                if (!selecting) { edgeClock.Stop(); return; }
                int past = dragAt.Y < 0 ? dragAt.Y : dragAt.Y >= Height ? dragAt.Y - Height + 1 : 0;
                if (past == 0) { edgeClock.Stop(); return; }
                ScrollTo(Offset + Math.Sign(past) * Math.Min(Z(60), Z(6) + Math.Abs(past) / 2));
                Drag(dragAt);
            }

            // ---- The copy key: on a message or a code well under the pointer, a tick for a second after it copies ----

            private Rectangle copyKey, tickKey;
            private string copyWhat;
            private bool copyHot;
            private long tickedAt = -10000;
            private readonly System.Windows.Forms.Timer tickClock = new System.Windows.Forms.Timer { Interval = 1000 };

            private void CopyHover(Point at)
            {
                Point inside = new Point(at.X, at.Y + Offset);
                Rectangle key = Rectangle.Empty;
                string what = null;
                int size = Z(22);
                foreach (Block block in Blocks)
                {
                    if (block.Hidden || block.Parts.Count == 0) continue;
                    foreach (Piece piece in block.Pieces)
                        if (piece.Code && piece.Box.Contains(inside))
                        {
                            key = new Rectangle(piece.Box.Right - size - Z(4), piece.Box.Top + Z(4), size, size);
                            what = CodeOf(block, piece);
                        }
                    if (what != null) break;
                    string kind = block.Item.Kind;
                    if (kind != "user" && kind != "text") continue;
                    Rectangle reach = new Rectangle(block.Box.Left, block.Box.Top, block.Box.Width, block.Box.Height + Z(20));
                    if (!reach.Contains(inside)) continue;
                    // Under his bubble on its right, under her words on their left, clear of the time and cost line.
                    key = kind == "user" ? new Rectangle(block.Box.Right - size, block.Box.Bottom + Z(1), size, size) : new Rectangle(block.Box.Left + Z(8), block.Box.Bottom + Z(1), size, size);
                    what = CopyOf(block.Item);
                    break;
                }
                bool hot = !key.IsEmpty && key.Contains(inside);
                if (key != copyKey || hot != copyHot) { Invalidate(); copyKey = key; copyHot = hot; }
                copyWhat = what;
            }

            private void CopyKey()
            {
                if (copyWhat == null) return;
                Copy(copyWhat);
                tickKey = copyKey;
                tickedAt = pressClock.ElapsedMilliseconds;
                tickClock.Stop();
                tickClock.Start();
                Invalidate();
            }

            internal bool Ticked { get { return !tickKey.IsEmpty && pressClock.ElapsedMilliseconds - tickedAt < 1000; } }

            private void TickDone() { tickClock.Stop(); tickKey = Rectangle.Empty; Invalidate(); }

            private void PaintCopyKey(Graphics graphics)
            {
                Rectangle key = Ticked ? tickKey : copyKey;
                if (key.IsEmpty) return;
                float s = window.zoom;
                bool ticked = Ticked && key == tickKey;
                if (copyHot && !ticked) WorkPaint.Plate(graphics, key, 4 * s, Neon.Hover, Color.Empty, 0);
                if (ticked) { WorkPaint.StatusMark(graphics, "asked", new PointF(key.Left + key.Width / 2f, key.Top + key.Height / 2f), Z(5), Green, s); return; }
                Color ink = copyHot ? Neon.Cyan : Neon.Faint;
                float unit = key.Width / 22f, x = key.Left + 6 * unit, y = key.Top + 5 * unit, w = 8 * unit, h = 10 * unit;
                SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (Pen pen = new Pen(ink, Math.Max(1f, 1.3f * s)))
                {
                    pen.LineJoin = LineJoin.Round;
                    // The sheet behind shows only where the front one doesn't cover it.
                    Region kept = graphics.Clip;
                    graphics.SetClip(new RectangleF(x - unit, y + 2 * unit, w + 2 * unit, h + 2 * unit), CombineMode.Exclude);
                    using (GraphicsPath back = WorkPaint.Rounded(new RectangleF(x + 3 * unit, y, w, h), 1.6f * unit)) graphics.DrawPath(pen, back);
                    graphics.Clip = kept;
                    kept.Dispose();
                    using (GraphicsPath front = WorkPaint.Rounded(new RectangleF(x, y + 3 * unit, w, h), 1.6f * unit)) graphics.DrawPath(pen, front);
                }
                graphics.SmoothingMode = was;
            }

            // For the smoke: a selection set by place, the copy key's box and pressing it, the right-click menu's items and the
            // keys, all without the real clipboard.
            internal bool SelectText(WorkspaceItem fromItem, int fromPart, int fromAt, WorkspaceItem toItem, int toPart, int toAt)
            {
                Block a, b;
                if (!known.TryGetValue(fromItem, out a) || !known.TryGetValue(toItem, out b)) return false;
                anchor = new Spot { Block = a, Part = fromPart, At = fromAt };
                caret = new Spot { Block = b, Part = toPart, At = toAt };
                Invalidate();
                return true;
            }

            internal void ClearSelection() { anchor = caret = new Spot(); Invalidate(); }

            internal Rectangle CopyKeyOn(Point at) { CopyHover(at); Rectangle key = copyKey; key.Offset(0, -Offset); return key; }

            internal void PressCopyKey() { CopyKey(); }

            internal List<string> MenuNames(WorkspaceItem item)
            {
                Block block;
                List<string> names = new List<string>();
                if (!known.TryGetValue(item, out block)) return names;
                Piece code = null;
                foreach (Piece piece in block.Pieces) if (piece.Code) code = piece;
                foreach (KeyValuePair<string, string> pair in MenuFor(block, code)) names.Add(pair.Key);
                return names;
            }

            internal void Key(Keys keys) { OnKeyDown(new KeyEventArgs(keys)); }

            // Where a part's character is drawn, in this control's space, for the smoke's drags.
            internal Point PointOf(WorkspaceItem item, int part, int at)
            {
                Block block;
                if (!known.TryGetValue(item, out block)) return Point.Empty;
                foreach (Seg seg in block.Segs)
                {
                    if (seg.Part != part || at < seg.At(0) || at > seg.End) continue;
                    int drawn = 0;
                    while (drawn < seg.Text.Length && seg.At(drawn) < at) drawn++;
                    return new Point(seg.Box.Left + WorkPaint.Wide(seg.Text.Substring(0, drawn), seg.Font), seg.Box.Top + seg.Box.Height / 2 - Offset);
                }
                return Point.Empty;
            }

            internal void DragText(Point from, Point to, int steps)
            {
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
                for (int i = 1; i <= steps; i++) OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, from.X + (to.X - from.X) * i / steps, from.Y + (to.Y - from.Y) * i / steps, 0));
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0));
            }

            internal void ClickText(Point at, int times)
            {
                lastPress = -10000;
                for (int i = 0; i < times; i++)
                {
                    OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
                    OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, at.X, at.Y, 0));
                }
            }

            // Holds the button down past the bottom edge for some ticks of the edge clock, then lets go.
            internal int DragPastEnd(Point from, int ticks)
            {
                int before = Offset;
                OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0));
                OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, from.X, Height + Z(30), 0));
                for (int i = 0; i < ticks; i++) EdgeTick();
                OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, from.X, Height + Z(30), 0));
                return Offset - before;
            }

            internal string CopyOf(WorkspaceItem item)
            {
                Block block;
                if (!known.TryGetValue(item, out block)) return null;
                if (item.Kind == "tool") return item.Text + "\n" + block.Result;
                if (item.Kind == "approval" && item.Input.Length > 0) return item.Text + "\n" + Lines(item.Input);
                if (item.Kind == "text" && block.Parts.Count > 0) return WorkWords.Plain(block.Parts);
                return item.Text;
            }

            // A tool card's well as drawn: each row, led by R when it's in her red, G in her green, and a space otherwise.
            internal List<string> Well(string id)
            {
                List<string> rows = new List<string>();
                foreach (Block block in Blocks)
                {
                    if (block.Item.Kind != "tool" || block.Item.Id != id) continue;
                    foreach (Piece piece in block.Pieces)
                        if (piece.Code && piece.Rows != null)
                            for (int i = 0; i < piece.Rows.Count; i++) rows.Add((piece.Inks[i] == Added ? "G" : piece.Inks[i] == Removed ? "R" : " ") + piece.Rows[i]);
                }
                return rows;
            }

            internal string Toggle(string id)
            {
                foreach (Block block in Blocks)
                {
                    if (block.Item.Kind != "tool" || block.Item.Id != id) continue;
                    if (block.Hidden || block.Group != null)
                        for (int i = Blocks.IndexOf(block); i >= 0; i--) if (Blocks[i].Group != null) { opened.Add(Blocks[i].Item); break; }
                    block.Open = !block.Open;
                    Arrange(true);
                    return block.Open ? block.Result : "";
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
