using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CapriDesktop.Services;
namespace CapriDesktop
{
    internal sealed class ChatWindow : Form
    {
        private readonly ChatStorage storage;
        private readonly IChatProvider provider = new BuiltInDialogue();
        private readonly Func<CharacterCommand, string> command;
        private readonly Func<string> characterState;
        private ChatConfiguration config;
        private List<ConversationSession> archive = new List<ConversationSession>();
        private ConversationSession session;
        private readonly FlowLayoutPanel messages = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        private readonly ComboBox sessions = new ComboBox { Width = 150, DropDownStyle = ComboBoxStyle.DropDownList };
        private readonly CheckBox privateMode = new CheckBox { Text = "Private session", AutoSize = true };
        private readonly TextBox input = new TextBox { Dock = DockStyle.Fill, Multiline = true, MaxLength = 16000, AcceptsReturn = true };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 42, Text = "Built-in dialogue · no AI model or network connection · type help / помощь" };
        private readonly FlowLayoutPanel tools = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 100, AutoSize = false };
        private readonly Button send = new Button { Text = "Send", Dock = DockStyle.Right, Width = 65 };
        private CancellationTokenSource operation;
        private bool shuttingDown, loading, fadingOut;
        private Font chatFont;
        private readonly System.Windows.Forms.Timer transition = new System.Windows.Forms.Timer { Interval = 16 };
        private double targetOpacity;
        private int scrollTarget;
        private readonly Action<ChatActivity, string> activity;
        internal ChatWindow(string directory, Action<ChatActivity, string> activity, Func<CharacterCommand, string> command, Func<string> characterState)
        {
            this.activity = activity; this.command = command; this.characterState = characterState; storage = new ChatStorage(directory);
            string warning = null;
            try { config = storage.LoadConfiguration(); if (config.PersistHistory) archive = storage.LoadSessions(); }
            catch (Exception error) { config = new ChatConfiguration(); warning = "Could not load chat data: " + error.Message; }
            Text = "CAPRI / DESKTOP CHAT"; Size = new Size(540, 640); MinimumSize = new Size(440, 480);
            BackColor = Color.FromArgb(18, 21, 30); ForeColor = Color.Gainsboro; StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.Dpi; AutoScaleDimensions = new SizeF(96, 96);
            AddButton("Settings", delegate { Settings(); });
            AddButton("Stop", delegate { if (operation != null) operation.Cancel(); });
            tools.Controls.Add(sessions);
            AddButton("New", delegate { if (operation == null && !fadingOut) NewSession(); });
            AddButton("Clear", delegate
            {
                if (operation != null || fadingOut || shuttingDown) return;
                try { if (!privateMode.Checked) storage.DeleteSession(session.Id); session.Messages.Clear(); DrawHistory(); Save(); }
                catch (Exception error) { status.Text = "Could not clear saved history: " + error.Message; }
            });
            AddButton("Delete all", delegate { DeleteHistory(); });
            tools.Controls.Add(privateMode);
            var entry = new Panel { Dock = DockStyle.Bottom, Height = 72 };
            entry.Controls.Add(input); entry.Controls.Add(send);
            Controls.Add(messages); Controls.Add(entry); Controls.Add(status); Controls.Add(tools);
            send.Click += async delegate { await Send(); };
            input.KeyDown += async delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter && !e.Shift) { e.SuppressKeyPress = true; await Send(); } };
            sessions.SelectedIndexChanged += delegate { if (!loading && operation == null && sessions.SelectedItem != null) { session = (ConversationSession)sessions.SelectedItem; DrawHistory(); } };
            privateMode.CheckedChanged += delegate
            {
                if (loading) return;
                if (privateMode.Checked) { session = new ConversationSession(); RefreshSessions(); DrawHistory(); }
                else { session = archive.Count == 0 ? null : archive[archive.Count - 1]; if (session == null) NewSession(); else { RefreshSessions(); DrawHistory(); } }
                status.Text = privateMode.Checked ? "Private: this session is never saved." : "Persistence: " + (config.PersistHistory ? "enabled locally" : "off");
            };
            if (archive.Count > 0) { session = archive[archive.Count - 1]; RefreshSessions(); DrawHistory(); } else NewSession();
            messages.ClientSizeChanged += delegate { ResizeBubbles(); };
            ApplyAppearance();
            if (warning != null) status.Text = warning;
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (operation != null) operation.Cancel();
                if (!shuttingDown && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; fadingOut = true; Busy(true); targetOpacity = 0; transition.Start(); }
                else { shuttingDown = true; activity(ChatActivity.Idle, null); }
            };
            transition.Tick += delegate
            {
                double difference = targetOpacity - Opacity;
                if (Math.Abs(difference) < .05) Opacity = targetOpacity; else Opacity += Math.Sign(difference) * Math.Min(.08, Math.Abs(difference));
                int current = -messages.AutoScrollPosition.Y;
                int desired = Math.Min(scrollTarget, Math.Max(0, messages.VerticalScroll.Maximum - messages.ClientSize.Height));
                int delta = desired - current;
                if (Math.Abs(delta) > 3) messages.AutoScrollPosition = new Point(0, current + Math.Sign(delta) * Math.Max(1, Math.Abs(delta) / 3));
                else messages.AutoScrollPosition = new Point(0, desired);
                if (Math.Abs(targetOpacity - Opacity) < .001 && (fadingOut || Math.Abs(delta) <= 3))
                {
                    transition.Stop();
                    if (fadingOut) { shuttingDown = true; activity(ChatActivity.Idle, null); Close(); }
                }
            };
            Shown += delegate { targetOpacity = config.Opacity; Opacity = 0; transition.Start(); activity(ChatActivity.Open, null); input.Focus(); };
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { shuttingDown = true; if (operation != null) operation.Cancel(); transition.Dispose(); if (chatFont != null) { chatFont.Dispose(); chatFont = null; } }
            base.Dispose(disposing);
        }
        private void AddButton(string name, EventHandler handler) { var button = new Button { Text = name, AutoSize = true, FlatStyle = FlatStyle.Flat }; button.Click += handler; tools.Controls.Add(button); }
        private void ApplyAppearance()
        {
            targetOpacity = config.Opacity; Opacity = config.Opacity;
            Font old = chatFont; chatFont = new Font("Segoe UI", config.FontSize); Font = chatFont;
            if (old != null) old.Dispose();
        }
        internal void RestoreChat()
        {
            if (shuttingDown || IsDisposed) return;
            fadingOut = false; targetOpacity = config.Opacity;
            Busy(operation != null); transition.Start(); Show(); Activate();
        }
        internal void Shutdown()
        {
            shuttingDown = true; transition.Stop();
            if (operation != null) operation.Cancel();
            Close();
        }
        internal void KeepVisibleOnDesktop()
        {
            if (IsDisposed) return;
            Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
            Location = RuntimePolicy.Clamp(Location, Size, area);
        }
        private void ResizeBubbles()
        {
            foreach (Control bubble in messages.Controls) bubble.Width = Math.Max(180, messages.ClientSize.Width - 30);
        }
        private void Busy(bool busy)
        {
            send.Enabled = !busy; input.ReadOnly = busy; sessions.Enabled = !busy && !privateMode.Checked; privateMode.Enabled = !busy;
        }
        private void RefreshSessions()
        {
            loading = true; sessions.Items.Clear(); foreach (var item in archive) sessions.Items.Add(item); sessions.SelectedItem = session; sessions.Enabled = !privateMode.Checked; loading = false;
        }
        private void NewSession()
        {
            session = new ConversationSession();
            if (!privateMode.Checked) { archive.Add(session); while (archive.Count > 20) archive.RemoveAt(0); }
            RefreshSessions(); DrawHistory(); Save();
        }
        private void DrawHistory()
        {
            messages.SuspendLayout();
            while (messages.Controls.Count > 0) { var item = messages.Controls[0]; messages.Controls.RemoveAt(0); item.Dispose(); }
            foreach (var message in session.Messages) Bubble(message.Role, message.Content);
            messages.ResumeLayout();
            ScrollToEnd();
        }
        private void ScrollToEnd()
        {
            scrollTarget = Math.Max(0, messages.DisplayRectangle.Height - messages.ClientSize.Height);
            if (!shuttingDown) transition.Start();
        }
        private TextBox Bubble(string role, string text)
        {
            var bubble = new TextBox { ReadOnly = true, Multiline = true, WordWrap = true, ScrollBars = ScrollBars.Vertical,
                Width = Math.Max(300, messages.ClientSize.Width - 30), Height = 100, BorderStyle = BorderStyle.None,
                BackColor = role == "user" ? Color.FromArgb(40, 53, 65) : Color.FromArgb(33, 29, 52), ForeColor = Color.Gainsboro,
                Text = (role == "user" ? "YOU" : "CAPRI") + Environment.NewLine + text, Margin = new Padding(8) };
            messages.Controls.Add(bubble); ScrollToEnd(); return bubble;
        }
        private void Save() { try { storage.SaveSessions(archive, config, privateMode.Checked); } catch (Exception error) { status.Text = "History not saved: " + error.Message; } }
        private async Task Send()
        {
            if (operation != null || fadingOut || shuttingDown || string.IsNullOrWhiteSpace(input.Text)) return;
            var current = new CancellationTokenSource(); operation = current; Busy(true);
            string question = input.Text.Trim(); input.Clear();
            session.Messages.Add(new ConversationMessage("user", question)); Bubble("user", question);
            var answer = Bubble("assistant", "Thinking…"); activity(ChatActivity.Thinking, null); status.Text = "Typing…";
            try
            {
                DialogueReply result = provider.Reply(question, characterState(), DateTime.Now, config);
                string reply = result.Command == CharacterCommand.None ? result.Text : command(result.Command);
                if (result.Mood == DialogueMood.Happy || result.Mood == DialogueMood.Farewell) activity(ChatActivity.Open, null);
                if (result.Mood == DialogueMood.Curious) activity(ChatActivity.Error, null);
                // A short, cancellable typewriter effect, not simulated inference.
                for (int count = 0; count < reply.Length; count += 6)
                {
                    await Task.Delay(25, current.Token);
                    if (shuttingDown) return;
                    int shown = Math.Min(reply.Length, count + 6);
                    if (shown < reply.Length && char.IsHighSurrogate(reply[shown - 1])) shown--;
                    answer.Text = "CAPRI\r\n" + reply.Substring(0, shown);
                    if (result.Command == CharacterCommand.None) activity(ChatActivity.Talking, reply.Substring(0, shown));
                    ScrollToEnd();
                }
                if (shuttingDown || current.IsCancellationRequested) return;
                answer.Text = "CAPRI\r\n" + reply; session.Messages.Add(new ConversationMessage("assistant", reply));
                while (session.Messages.Count > config.HistoryMessages) session.Messages.RemoveAt(0);
                status.Text = "Ready · built-in dialogue."; Save(); DrawHistory();
            }
            catch (Exception error)
            {
                if (!shuttingDown) { status.Text = current.IsCancellationRequested ? "Typing stopped. Partial output was not saved." : "Request failed: " + FriendlyError(error); activity(ChatActivity.Error, null); }
                if (session.Messages.Count > 0 && session.Messages[session.Messages.Count - 1].Role == "user") session.Messages.RemoveAt(session.Messages.Count - 1);
                if (!shuttingDown && !fadingOut) DrawHistory();
            }
            finally { operation = null; current.Dispose(); if (!shuttingDown && !fadingOut) { Busy(false); activity(ChatActivity.Idle, null); input.Focus(); } }
        }
        private static string FriendlyError(Exception error) { return error is ArgumentException ? error.Message : "The dialogue could not finish. Try again."; }
        private void DeleteHistory()
        {
            if (operation != null || fadingOut || shuttingDown || MessageBox.Show(this, "Delete all locally saved conversations?", "Capri", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
            try { storage.DeleteAll(); archive.Clear(); NewSession(); status.Text = "History deleted."; } catch (Exception error) { status.Text = "Could not delete history: " + error.Message; }
        }
        private void Settings()
        {
            if (operation != null || fadingOut || shuttingDown) return;
            var edited = ChatJson.Read<ChatConfiguration>(ChatJson.Write(config));
            using (var dialog = new Form { Text = "Capri / Dialogue and privacy settings", Size = new Size(500, 580), StartPosition = FormStartPosition.CenterParent, BackColor = BackColor, ForeColor = ForeColor })
            {
                var grid = new PropertyGrid { Dock = DockStyle.Fill, SelectedObject = edited, HelpVisible = true, ToolbarVisible = false };
                var apply = new Button { Text = "Save settings", Dock = DockStyle.Bottom, Height = 38 };
                apply.Click += delegate { try { edited.Validate(); dialog.DialogResult = DialogResult.OK; } catch (Exception error) { MessageBox.Show(dialog, error.Message, "Settings not saved"); } };
                dialog.Controls.Add(grid); dialog.Controls.Add(apply);
                if (dialog.ShowDialog(this) == DialogResult.OK) {
                    if (!config.PersistHistory && edited.PersistHistory)
                    {
                        try
                        {
                            var previous = storage.LoadSessions();
                            foreach (var old in previous) if (!archive.Exists(delegate(ConversationSession item) { return item.Id == old.Id; })) archive.Insert(0, old);
                            while (archive.Count > 20) archive.RemoveAt(0);
                            RefreshSessions();
                        }
                        catch (Exception error) { status.Text = "Cannot restore history: " + error.Message; return; }
                    }
                    try { storage.SaveConfiguration(edited); } catch (Exception error) { status.Text = "Settings not saved: " + error.Message; return; }
                    config = edited; ApplyAppearance(); status.Text = "Settings saved. Disabling persistence keeps existing history; use Delete all to erase it."; }
            }
        }
    }
}
