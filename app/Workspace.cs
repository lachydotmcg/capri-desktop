using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CapriDesktop
{
    internal sealed class WorkspaceItem
    {
        internal string Kind = "", Text = "", Name = "", Id = "", Turn = "", Input = "", Decision = "", Cost = "", Files = "";
        internal double Seconds;
        internal bool Retry;
        internal WorkspaceApproval Ask;
        internal WorkspaceUndo Undo;

        internal Dictionary<string, object> Json()
        {
            string text = Kind == "result" && Text.Length > 2000 ? Text.Substring(0, 2000) : Text;
            return HuskJson.Map("kind", Kind, "text", text, "name", Name, "id", Id, "turn", Turn, "input", Kind == "tool" ? Input : "",
                "decision", Decision, "seconds", Seconds, "cost", Cost, "retry", Retry, "files", Files);
        }

        internal static WorkspaceItem Read(Dictionary<string, object> row)
        {
            return new WorkspaceItem { Kind = HuskJson.Text(row, "kind"), Text = HuskJson.Text(row, "text"), Name = HuskJson.Text(row, "name"),
                Id = HuskJson.Text(row, "id"), Turn = HuskJson.Text(row, "turn"), Decision = HuskJson.Text(row, "decision"), Input = HuskJson.Text(row, "input"),
                Seconds = Workspace.Number(row, "seconds"), Cost = HuskJson.Text(row, "cost"), Retry = true.Equals(HuskJson.Get(row, "retry")), Files = HuskJson.Text(row, "files") };
        }
    }

    internal sealed class WorkspaceChat
    {
        internal readonly object Gate = new object();
        internal readonly object SaveGate = new object(), UndoGate = new object();
        internal string Id = Guid.NewGuid().ToString("N"), Title = "", Folder = "", Session = "", Mode = "Ask me", Model = "Default";
        internal long Updated = DateTime.UtcNow.Ticks;
        internal readonly List<WorkspaceItem> Items = new List<WorkspaceItem>();
        internal readonly List<string> Turns = new List<string>();
        internal readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal);
        // Folders after the first go to Claude Code with --add-dir. Files ride along with the next message, and reads and edits
        // of exactly those files count as inside the chat's folders until they're removed.
        internal readonly List<string> Folders = new List<string>(), Files = new List<string>();
        internal readonly HashSet<string> Sent = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        internal volatile WorkspaceTurn Live;
        internal bool Deleted, TrustAsked, SaveWarning;
        internal string PendingText;
        internal WorkspaceItem TrustItem;
        internal WorkspaceUndo Undoing;
        internal long Revision, SavedRevision, LastSave;
        internal bool Running { get { return Live != null; } }
    }

    internal sealed class WorkspaceApproval
    {
        internal readonly ManualResetEvent Answered = new ManualResetEvent(false);
        internal WorkspaceItem Item;
        internal WorkspaceTurn Turn;
        internal string Choice, Reason = "";
        internal bool Announced, Queued;
    }

    internal sealed class WorkspaceUndo
    {
        internal string Turn;
        internal WorkspaceItem Item;
    }

    internal sealed class WorkspaceTurn
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal readonly object ProcessGate = new object();
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly List<WorkspaceApproval> Asks = new List<WorkspaceApproval>();
        internal readonly HashSet<string> Tools = new HashSet<string>(StringComparer.Ordinal);
        internal readonly Dictionary<string, Dictionary<string, object>> Edits = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
        internal readonly StringBuilder Errors = new StringBuilder(), Output = new StringBuilder();
        internal readonly List<string> Extra = new List<string>(), Files = new List<string>();
        internal readonly ManualResetEvent OutputDone = new ManualResetEvent(false), ErrorDone = new ManualResetEvent(false), Done = new ManualResetEvent(false), Cancel = new ManualResetEvent(false);
        internal WorkspaceChat Chat;
        internal string Mode = "", Model = "", Session = "", Settings, Wrapper, ResultText = "", Fault;
        internal Process Process;
        internal WorkspaceJob Job;
        internal volatile bool Stopped, Result, Failed;
        internal bool TextSeen, SessionSeen, StopRecorded, Trusted, Uncovered;
        internal int Attempt, Active;
        internal long ResultAt;
    }

    internal sealed class Workspace : IDisposable
    {
        internal const string Missing = "Workspace runs Claude Code on this PC. Install it from claude.com/claude-code and sign in, then press Retry.";
        internal static readonly string[] Modes = { "Ask me", "Edits only", "Plan only", "Everything" };
        internal static readonly string[] Models = { "Default", "Opus", "Sonnet", "Haiku" };
        internal readonly string Root, Pipe;
        internal string Executable;
        // Her own folder for chats started without picking one: Capri\Workspace in their profile folder, the same on every PC.
        // Not Documents, which is often inside OneDrive, where Claude Code's fast small writes (.git, node_modules) would be
        // uploaded and fought over. Only made when a chat first needs it, and never emptied or deleted by her.
        internal string Home = HomeFolder();
        internal int ApprovalWait = 600000;
        internal Action<WorkspaceChat, WorkspaceItem> Changed;
        internal Action<WorkspaceChat> Finished;
        internal Action<WorkspaceChat, WorkspaceApproval> Waiting;
        internal Action<string, string> ReplaceFile;
        internal string Version = "";
        internal readonly List<string> Problems = new List<string>();
        private readonly object gate = new object(), pipeGate = new object(), versionGate = new object(), trustGate = new object();
        private readonly HashSet<string> trusted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly AutoResetEvent saveReady = new AutoResetEvent(false);
        private readonly List<WorkspaceChat> chats = new List<WorkspaceChat>();
        private readonly List<NamedPipeServerStream> clients = new List<NamedPipeServerStream>();
        private NamedPipeServerStream waiting;
        private Thread server, saver;
        private bool versionRead;
        private volatile bool closed;

        internal Workspace(string data, string pipe)
        {
            Root = Path.Combine(data, "workspace");
            Pipe = pipe ?? "CapriWorkspace-" + Process.GetCurrentProcess().Id;
            Directory.CreateDirectory(Path.Combine(Root, "chats"));
            string trustFile = Path.Combine(Root, "trusted.json");
            try
            {
                if (File.Exists(trustFile))
                {
                    List<object> folders = HuskJson.List(HuskJson.Read(File.ReadAllText(trustFile, Encoding.UTF8)));
                    if (folders != null) foreach (object folder in folders) if (folder is string) trusted.Add(WorkspacePaths.Final((string)folder));
                }
            }
            catch (Exception error) { Problems.Add("Couldn't load trusted folders: " + error.Message); }
            foreach (string file in Directory.GetFiles(Path.Combine(Root, "chats"), "*.json"))
            {
                try
                {
                    Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    string id = HuskJson.Text(row, "id");
                    if (!Key(id) || !string.Equals(Path.GetFileNameWithoutExtension(file), id, StringComparison.Ordinal)) throw new FormatException("Invalid chat id.");
                    WorkspaceChat chat = new WorkspaceChat { Id = id, Title = HuskJson.Text(row, "title"), Folder = HuskJson.Text(row, "folder"), Session = HuskJson.Text(row, "session"),
                        Mode = Mode(HuskJson.Text(row, "mode")), Model = Model(HuskJson.Text(row, "model")), TrustAsked = true.Equals(HuskJson.Get(row, "trustAsked")) };
                    long updated;
                    if (long.TryParse(HuskJson.Text(row, "updated"), NumberStyles.None, CultureInfo.InvariantCulture, out updated)) chat.Updated = updated;
                    List<object> items = HuskJson.List(HuskJson.Get(row, "items")), turns = HuskJson.List(HuskJson.Get(row, "turns")), allowed = HuskJson.List(HuskJson.Get(row, "allowed"));
                    if (items != null) foreach (object item in items)
                    {
                        Dictionary<string, object> map = item as Dictionary<string, object>;
                        if (map == null) continue;
                        WorkspaceItem found = WorkspaceItem.Read(map);
                        if (found.Kind == "approval" && found.Decision.Length == 0) { found.Decision = "deny"; found.Text = "Denied. Workspace closed before an answer."; }
                        if (found.Kind == "trust" && found.Decision.Length == 0) { found.Decision = "deny"; found.Text = "Folder not trusted."; }
                        if (found.Kind == "undo-ask") { found.Kind = "undo"; found.Text = "Undo paused. Use Undo last edits to choose again."; }
                        chat.Items.Add(found);
                    }
                    if (turns != null) foreach (object turn in turns) if (turn is string && Key((string)turn)) chat.Turns.Add((string)turn);
                    if (allowed != null) foreach (object tool in allowed) if (tool is string) chat.Allowed.Add((string)tool);
                    List<object> folders = HuskJson.List(HuskJson.Get(row, "folders")), files = HuskJson.List(HuskJson.Get(row, "files")), sent = HuskJson.List(HuskJson.Get(row, "sent"));
                    if (folders != null) foreach (object extra in folders) if (extra is string && ((string)extra).Length > 0) chat.Folders.Add((string)extra);
                    if (files != null) foreach (object named in files) if (named is string && ((string)named).Length > 0) chat.Files.Add((string)named);
                    if (sent != null) foreach (object named in sent) if (named is string) chat.Sent.Add((string)named);
                    chats.Add(chat);
                }
                catch (Exception error) { Problems.Add("Couldn't load " + Path.GetFileName(file) + ": " + error.Message); }
            }
            if (chats.Count > 0) Listen();
            saver = new Thread(SaveLoop) { IsBackground = true, Name = "Capri Workspace saves" };
            saver.Start();
        }

        internal static bool Key(string value) { return Regex.IsMatch(value ?? "", "\\A[a-f0-9]{32}\\z", RegexOptions.CultureInvariant); }
        internal static string Mode(string value) { return Array.IndexOf(Modes, value) < 0 ? Modes[0] : value; }
        internal static string Model(string value) { return Array.IndexOf(Models, value) < 0 ? Models[0] : value; }
        internal static double Number(Dictionary<string, object> row, string key)
        {
            object value = HuskJson.Get(row, key);
            HuskNumber raw = value as HuskNumber;
            double number;
            return double.TryParse(raw != null ? raw.Text : value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture, out number) && !double.IsNaN(number) && !double.IsInfinity(number) ? number : 0;
        }

        internal List<WorkspaceChat> Chats()
        {
            lock (gate)
            {
                List<WorkspaceChat> copy = new List<WorkspaceChat>(chats);
                copy.Sort(delegate(WorkspaceChat a, WorkspaceChat b) { return b.Updated.CompareTo(a.Updated); });
                return copy;
            }
        }

        internal WorkspaceChat NewChat(string folder, bool blackwall) { return NewChat(folder, blackwall, true); }

        private WorkspaceChat NewChat(string folder, bool blackwall, bool there)
        {
            folder = Path.GetFullPath(folder);
            if (there && !Directory.Exists(folder)) throw new IOException("That folder isn't here.");
            WorkspaceChat chat = new WorkspaceChat { Folder = folder, Title = new DirectoryInfo(folder).Name, Mode = blackwall ? "Everything" : "Ask me" };
            if (chat.Title.Length == 0) chat.Title = folder;
            lock (gate)
            {
                if (closed) throw new ObjectDisposedException("Workspace");
                Save(chat);
                chats.Add(chat);
            }
            Listen();
            Notify(chat, null);
            return chat;
        }

        internal static void Atomic(string file, string text)
        {
            Atomic(file, text, null);
        }

        private static void Atomic(string file, string text, Action<string, string> replace)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                using (FileStream stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                if (File.Exists(file)) { if (replace == null) File.Replace(temp, file, null); else replace(temp, file); }
                else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal void Save(WorkspaceChat chat)
        {
            lock (chat.Gate) { if (chat.Deleted) return; chat.Revision++; }
            saveReady.Set();
        }

        private void SaveLoop()
        {
            while (!closed)
            {
                saveReady.WaitOne(500);
                if (closed) return;
                foreach (WorkspaceChat chat in Chats()) WriteChat(chat, false);
            }
        }

        internal void Flush(WorkspaceChat chat) { WriteChat(chat, true); }

        private void WriteChat(WorkspaceChat chat, bool force)
        {
            lock (chat.SaveGate)
            {
                Dictionary<string, object> copy;
                long revision;
                lock (chat.Gate)
                {
                    if (chat.Deleted || !force && (chat.Revision == chat.SavedRevision || chat.Running && Stopwatch.GetTimestamp() - chat.LastSave < Stopwatch.Frequency / 2)) return;
                    List<object> items = new List<object>(), allowed = new List<object>(), turns = new List<object>();
                    foreach (WorkspaceItem item in chat.Items) items.Add(item.Json());
                    foreach (string tool in chat.Allowed) allowed.Add(tool);
                    foreach (string turn in chat.Turns) turns.Add(turn);
                    List<object> folders = new List<object>(), files = new List<object>(), sent = new List<object>();
                    foreach (string extra in chat.Folders) folders.Add(extra);
                    foreach (string file in chat.Files) files.Add(file);
                    foreach (string file in chat.Sent) sent.Add(file);
                    revision = chat.Revision;
                    chat.LastSave = Stopwatch.GetTimestamp();
                    copy = HuskJson.Map("id", chat.Id, "title", chat.Title, "folder", chat.Folder,
                        "session", chat.Session, "mode", chat.Mode, "model", chat.Model, "updated", chat.Updated.ToString(CultureInfo.InvariantCulture),
                        "trustAsked", chat.TrustAsked, "items", items, "allowed", allowed, "turns", turns, "folders", folders, "files", files, "sent", sent);
                }
                string text = HuskJson.Write(copy);
                Exception failed = null;
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    try { Atomic(Path.Combine(Root, "chats", chat.Id + ".json"), text, ReplaceFile); failed = null; break; }
                    catch (Exception error) { failed = error; if (attempt < 3) Thread.Sleep(650); }
                }
                WorkspaceItem warning = null;
                lock (chat.Gate)
                {
                    chat.SavedRevision = revision;
                    if (failed != null && !chat.SaveWarning && !chat.Deleted)
                    {
                        chat.SaveWarning = true;
                        warning = new WorkspaceItem { Kind = "note", Text = "Couldn't save this chat. Workspace will keep trying." };
                        chat.Items.Add(warning);
                    }
                }
                if (warning != null) Notify(chat, warning);
            }
        }

        internal static List<string> AllFolders(WorkspaceChat chat)
        {
            lock (chat.Gate)
            {
                List<string> all = new List<string> { chat.Folder };
                all.AddRange(chat.Folders);
                return all;
            }
        }

        internal static List<string> AllFiles(WorkspaceChat chat) { lock (chat.Gate) return new List<string>(chat.Files); }

        // The files waiting to go with the next message. Once a message takes them they stay part of the chat (Claude Code
        // may keep reading and editing them) but they're no longer waiting.
        internal static List<string> Attached(WorkspaceChat chat) { lock (chat.Gate) return chat.Files.FindAll(delegate(string file) { return !chat.Sent.Contains(file); }); }

        internal static string HomeFolder()
        {
            string profile = "";
            try { profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); } catch (Exception) { }
            if (string.IsNullOrEmpty(profile) || !Path.IsPathRooted(profile)) return Spare();
            return Path.Combine(profile, "Capri", "Workspace");
        }

        private static string Spare() { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri", "Workspace"); }

        internal bool IsHome(string folder)
        {
            try { return Home != null && folder != null && Same(Tidy(folder), Tidy(Home)); }
            catch (Exception) { return false; }
        }

        // Makes her folder when the first message in it goes. If the profile folder can't take it, it goes in local app data.
        internal string MakeHome()
        {
            try { Directory.CreateDirectory(Home); }
            catch (Exception)
            {
                Home = Spare();
                Directory.CreateDirectory(Home);
            }
            return Home;
        }

        // A chat in her folder is named from its first message: the first words, up to about 40 letters, cut at a word, on
        // one line, without trailing punctuation. Files with no words take the first file's name.
        internal static string TitleFrom(string text, List<string> files)
        {
            string words = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (words.Length == 0) return files != null && files.Count > 0 ? Path.GetFileName(files[0]) : "New chat";
            bool cut = words.Length > 40;
            if (cut)
            {
                int at = words.LastIndexOf(' ', 40);
                words = at >= 12 ? words.Substring(0, at) : words.Substring(0, 40);
            }
            words = words.TrimEnd(' ', '.', ',', ';', ':', '!', '?', '-', '\u2013', '\u2014', '(', '"', '\'');
            // A title made from a longer message looks cut: the dots sit tight against the last word.
            return cut ? words + "\u2026" : words;
        }

        // A chat in her own folder, ready to type in. Nothing has been asked yet, so its folder can still change, and the
        // folder itself isn't made until a message goes: opening Workspace leaves nothing behind on the PC.
        internal WorkspaceChat HomeChat(bool blackwall)
        {
            WorkspaceChat chat = NewChat(Home, blackwall, false);
            lock (chat.Gate) chat.Title = "New chat";
            Save(chat);
            Notify(chat, null);
            return chat;
        }

        // Until the first message the folder Claude Code runs in can change: the first picked folder takes its place and the
        // rest join it. After that it's fixed, since the conversation lives in that folder.
        internal void Rehome(WorkspaceChat chat, IList<string> picked)
        {
            List<string> folders = new List<string>();
            foreach (string raw in picked)
            {
                string folder = Tidy(raw);
                if (!Directory.Exists(folder)) throw new IOException("That folder isn't here: " + folder);
                if (!folders.Exists(delegate(string had) { return Same(had, folder); })) folders.Add(folder);
            }
            if (folders.Count == 0) return;
            lock (chat.Gate)
            {
                if (chat.Deleted) return;
                if (chat.Live != null || chat.Items.Exists(delegate(WorkspaceItem item) { return item.Kind == "user"; })) throw new IOException("The folder Claude Code runs in is fixed once a message is sent.");
                string old = chat.Folder, oldName = new DirectoryInfo(old).Name;
                List<string> rest = new List<string>(chat.Folders);
                chat.Folder = folders[0];
                chat.Folders.Clear();
                List<string> joining = folders.GetRange(1, folders.Count - 1);
                joining.AddRange(rest);
                foreach (string folder in joining)
                    if (!Same(folder, chat.Folder) && !chat.Folders.Exists(delegate(string had) { return Same(had, folder); })) chat.Folders.Add(folder);
                if (chat.Title == oldName || chat.Title == "New chat") chat.Title = new DirectoryInfo(chat.Folder).Name.Length > 0 ? new DirectoryInfo(chat.Folder).Name : chat.Folder;
                chat.TrustAsked = false;
                Save(chat);
            }
            Notify(chat, null);
        }

        internal bool Trusted(WorkspaceChat chat) { return Untrusted(chat).Count == 0; }

        internal bool Trusted(string folder)
        {
            if (IsHome(folder)) return true;
            try { string final = WorkspacePaths.Final(folder); lock (trustGate) return trusted.Contains(final); }
            catch (Exception) { return false; }
        }

        internal List<string> Untrusted(WorkspaceChat chat)
        {
            List<string> untrusted = new List<string>();
            foreach (string folder in AllFolders(chat)) if (!Trusted(folder)) untrusted.Add(folder);
            return untrusted;
        }

        internal void AskTrust(WorkspaceChat chat)
        {
            lock (chat.Gate)
            {
                List<string> untrusted = Untrusted(chat);
                if (chat.Deleted || untrusted.Count == 0 || chat.TrustItem != null) return;
                chat.TrustAsked = true;
                string names = untrusted.Count == 1 ? untrusted[0] : string.Join(", ", untrusted.GetRange(0, untrusted.Count - 1).ToArray()) + " and " + untrusted[untrusted.Count - 1];
                chat.TrustItem = new WorkspaceItem { Kind = "trust", Text = (untrusted.Count == 1 ? "Claude Code will run this folder's own settings, hooks and MCP servers. Trust " : "Claude Code will run these folders' own settings, hooks and MCP servers. Trust ") + names + "?" };
                Add(chat, chat.TrustItem);
            }
        }

        internal void Trust(WorkspaceChat chat, bool yes)
        {
            if (yes)
            {
                List<string> folders = new List<string>();
                foreach (string each in AllFolders(chat)) folders.Add(WorkspacePaths.Final(each));
                lock (trustGate)
                {
                    HashSet<string> copy = new HashSet<string>(trusted, StringComparer.OrdinalIgnoreCase);
                    copy.UnionWith(folders);
                    List<object> list = new List<object>();
                    foreach (string path in copy) list.Add(path);
                    Atomic(Path.Combine(Root, "trusted.json"), HuskJson.Write(list));
                    trusted.UnionWith(folders);
                }
            }
            string pending;
            lock (chat.Gate)
            {
                chat.TrustAsked = true;
                if (chat.TrustItem != null)
                {
                    chat.TrustItem.Decision = yes ? "allow" : "deny";
                    chat.TrustItem.Text = yes ? "Folder trusted." : "Folder not trusted.";
                    chat.TrustItem = null;
                }
                pending = chat.PendingText;
                chat.PendingText = null;
                Save(chat);
            }
            Notify(chat, null);
            if (pending != null) Send(chat, pending, true);
        }

        internal void Change(WorkspaceChat chat, string title, string mode, string model)
        {
            lock (chat.Gate)
            {
                if (title != null && title.Trim().Length > 0) chat.Title = title.Trim();
                if (mode != null) chat.Mode = Mode(mode);
                if (model != null) chat.Model = Model(model);
                Save(chat);
            }
            Notify(chat, null);
        }

        // New folders join the chat; one this PC hasn't trusted makes the next message ask again.
        internal int AddFolders(WorkspaceChat chat, IEnumerable<string> paths)
        {
            int added = 0;
            List<string> picked = new List<string>();
            foreach (string raw in paths)
            {
                string folder = Tidy(raw);
                if (!Directory.Exists(folder)) throw new IOException("That folder isn't here: " + folder);
                picked.Add(folder);
            }
            lock (chat.Gate)
            {
                if (chat.Deleted) return 0;
                foreach (string folder in picked)
                {
                    if (Same(folder, chat.Folder) || chat.Folders.Exists(delegate(string had) { return Same(had, folder); })) continue;
                    chat.Folders.Add(folder);
                    added++;
                    if (!Trusted(folder)) chat.TrustAsked = false;
                }
                if (added > 0) Save(chat);
            }
            if (added > 0) Notify(chat, null);
            return added;
        }

        internal int AddFiles(WorkspaceChat chat, IEnumerable<string> paths)
        {
            int added = 0;
            List<string> picked = new List<string>();
            foreach (string raw in paths)
            {
                string file = Path.GetFullPath(raw);
                if (!File.Exists(file)) throw new IOException("That file isn't here: " + file);
                picked.Add(file);
            }
            lock (chat.Gate)
            {
                if (chat.Deleted) return 0;
                foreach (string file in picked)
                {
                    string kept = chat.Files.Find(delegate(string had) { return Same(had, file); });
                    if (kept != null)
                    {
                        if (chat.Sent.Remove(kept)) added++;
                        continue;
                    }
                    chat.Files.Add(file);
                    chat.Sent.Remove(file);
                    added++;
                }
                if (added > 0) Save(chat);
            }
            if (added > 0) Notify(chat, null);
            return added;
        }

        // Takes a folder after the first, or a file, off the chat. The first folder is where Claude Code runs, so it stays.
        internal bool Remove(WorkspaceChat chat, string path)
        {
            bool removed;
            lock (chat.Gate)
            {
                removed = chat.Folders.RemoveAll(delegate(string had) { return Same(had, path); }) > 0;
                if (chat.Files.RemoveAll(delegate(string had) { return Same(had, path); }) > 0) { chat.Sent.Remove(path); removed = true; }
                if (removed) Save(chat);
            }
            if (removed) Notify(chat, null);
            return removed;
        }

        // A full path without a trailing slash, except a drive or share root, which needs its own.
        internal static string Tidy(string path)
        {
            string full = Path.GetFullPath(path), root = Path.GetPathRoot(full) ?? "";
            string trimmed = full.TrimEnd('\\', '/');
            return trimmed.Length <= root.TrimEnd('\\', '/').Length ? full : trimmed;
        }

        internal static bool Same(string a, string b)
        {
            return string.Equals((a ?? "").TrimEnd('\\', '/'), (b ?? "").TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);
        }

        // Their words, then the files that ride along with this message, by full path.
        internal static string Compose(string text, List<string> files)
        {
            if (files == null || files.Count == 0) return text;
            StringBuilder message = new StringBuilder(text);
            if (text.Trim().Length > 0) message.Append("\n\n");
            message.Append("Attached files:");
            foreach (string file in files) message.Append("\n- ").Append(file);
            return message.ToString();
        }

        internal void Delete(WorkspaceChat chat)
        {
            Stop(chat);
            lock (chat.SaveGate)
            {
                lock (chat.Gate) { chat.Deleted = true; chat.PendingText = null; }
                File.Delete(Path.Combine(Root, "chats", chat.Id + ".json"));
            }
            lock (gate) chats.Remove(chat);
            string undo = Path.Combine(Root, "undo", chat.Id);
            lock (chat.UndoGate) if (Directory.Exists(undo)) Directory.Delete(undo, true);
            Notify(chat, null);
        }

        private void Notify(WorkspaceChat chat, WorkspaceItem item)
        {
            Action<WorkspaceChat, WorkspaceItem> changed = Changed;
            if (changed != null && !closed && !chat.Deleted) changed(chat, item);
        }

        internal void Add(WorkspaceChat chat, WorkspaceItem item)
        {
            lock (chat.Gate)
            {
                if (chat.Deleted) return;
                chat.Items.Add(item);
                chat.Updated = DateTime.UtcNow.Ticks;
                Save(chat);
            }
            Notify(chat, item);
        }

        internal static List<string> Arguments(string mode, string session, string model, string settings)
        {
            return Arguments(mode, session, model, settings, true);
        }

        internal static List<string> Arguments(string mode, string session, string model, string settings, bool trusted)
        {
            return Arguments(mode, session, model, settings, trusted, null);
        }

        internal static List<string> Arguments(string mode, string session, string model, string settings, bool trusted, List<string> extra)
        {
            string permission = mode == "Edits only" ? "acceptEdits" : mode == "Plan only" ? "plan" : mode == "Everything" ? "bypassPermissions" : "default";
            List<string> args = new List<string> { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json", "--permission-mode", permission };
            if (!string.IsNullOrEmpty(session)) { args.Add("--resume"); args.Add(session); }
            if (!string.IsNullOrEmpty(model) && model != "Default") { args.Add("--model"); args.Add(model.ToLowerInvariant()); }
            if (!string.IsNullOrEmpty(settings)) { args.Add("--settings"); args.Add(settings); }
            if (extra != null) foreach (string folder in extra) { args.Add("--add-dir"); args.Add(folder); }
            if (!trusted) args.AddRange(new[] { "--setting-sources", "user", "--strict-mcp-config" });
            return args;
        }

        internal static bool ExecHooks(string version)
        {
            Match found = Regex.Match(version ?? "", @"\A\s*(\d+\.\d+\.\d+)(?:\s|\z)");
            System.Version parsed;
            return found.Success && System.Version.TryParse(found.Groups[1].Value, out parsed) && parsed >= new System.Version(2, 1, 295);
        }

        internal string Settings(WorkspaceChat chat, string turn, string exe)
        {
            return Settings(chat, turn, exe, Path.Combine(Root, "turns", chat.Id, turn + ".cmd"));
        }

        private string Settings(WorkspaceChat chat, string turn, string exe, string wrapper)
        {
            Dictionary<string, object> hook;
            if (ExecHooks(Version))
                hook = HuskJson.Map("type", "command", "command", exe, "args", new List<object> { "--workspace-hook", Pipe, chat.Id, turn }, "timeout", 660, "onFailure", "block");
            else
            {
                Directory.CreateDirectory(Path.GetDirectoryName(wrapper));
                if (exe.IndexOfAny(new[] { '\r', '\n', '"' }) >= 0) throw new IOException("Workspace couldn't make its hook command.");
                File.WriteAllText(wrapper, "@echo off\r\nsetlocal DisableDelayedExpansion\r\n\"%CAPRI_WORKSPACE_LAUNCHER%\" --workspace-hook " + Pipe + " " + chat.Id + " " + turn + "\r\nif not \"%errorlevel%\"==\"0\" exit /b 2\r\nexit /b 0\r\n", Encoding.ASCII);
                string command = WorkspacePaths.Short(wrapper).Replace('\\', '/');
                if (!Regex.IsMatch(command, @"\A[A-Za-z]:/[\p{L}\p{Nd}_./~-]+\.cmd\z", RegexOptions.IgnoreCase)) throw new IOException("Workspace needs a short path for its hook command.");
                hook = HuskJson.Map("type", "command", "command", command, "timeout", 660);
            }
            return HuskJson.Write(HuskJson.Map("hooks", HuskJson.Map("PreToolUse", new List<object> {
                HuskJson.Map("matcher", "*", "hooks", new List<object> { hook }) })));
        }

        private void ReadVersion(string exe, string folder)
        {
            lock (versionGate)
            {
                if (versionRead) return;
                versionRead = true;
                using (Process process = new Process { StartInfo = StartInfo(exe, new List<string> { "--version" }, folder) })
                using (ManualResetEvent read = new ManualResetEvent(false))
                {
                    StringBuilder text = new StringBuilder();
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs row)
                    {
                        if (row.Data != null) lock (text) Tail(text, row.Data);
                        else try { read.Set(); } catch (ObjectDisposedException) { }
                    };
                    process.ErrorDataReceived += delegate { };
                    try
                    {
                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.StandardInput.Close();
                        bool exited = process.WaitForExit(5000);
                        if (!exited) KillTree(process);
                        read.WaitOne(1000);
                        lock (text) Version = exited && process.ExitCode == 0 ? text.ToString().Trim() : "";
                    }
                    catch (Exception) { Version = ""; }
                    finally
                    {
                        KillTree(process);
                        try { process.CancelOutputRead(); process.CancelErrorRead(); } catch (InvalidOperationException) { }
                    }
                }
            }
        }

        internal bool Send(WorkspaceChat chat, string text, bool retry)
        {
            if (text == null || closed) return false;
            WorkspaceTurn turn;
            lock (chat.Gate)
            {
                bool files = retry ? LastFiles(chat).Count > 0 : chat.Files.Exists(delegate(string file) { return !chat.Sent.Contains(file); });
                if (string.IsNullOrWhiteSpace(text) && !files) return false;
                if (chat.Live != null || chat.Deleted || chat.PendingText != null || chat.Undoing != null) return false;
                if (!retry)
                {
                    List<string> riding = chat.Files.FindAll(delegate(string file) { return !chat.Sent.Contains(file); });
                    foreach (string file in riding) chat.Sent.Add(file);
                    // A title they typed is never replaced: only a title still as it was given (her folder's "New chat", or a
                    // picked folder's name) takes the first message's words. The folder's name stays on the list's second line.
                    bool first = !chat.Items.Exists(delegate(WorkspaceItem item) { return item.Kind == "user"; });
                    string given = new DirectoryInfo(chat.Folder).Name;
                    if (given.Length == 0) given = chat.Folder;
                    if (first && (chat.Title == "New chat" && IsHome(chat.Folder) || chat.Title == given)) chat.Title = TitleFrom(text, riding);
                    Add(chat, new WorkspaceItem { Kind = "user", Text = text, Files = string.Join("\n", riding.ToArray()) });
                }
                if (!Trusted(chat) && !chat.TrustAsked)
                {
                    chat.PendingText = text;
                    AskTrust(chat);
                    return true;
                }
                string exe = Executable == null ? PlanBrain.Find("claude") : File.Exists(Executable) ? Executable : null;
                if (exe == null) { Add(chat, new WorkspaceItem { Kind = "error", Text = Missing, Retry = true }); return false; }
                turn = new WorkspaceTurn { Chat = chat, Mode = chat.Mode, Model = chat.Model, Session = chat.Session, Trusted = Trusted(chat) };
                turn.Extra.AddRange(chat.Folders);
                for (int i = chat.Items.Count - 1; i >= 0; i--)
                    if (chat.Items[i].Kind == "user") { foreach (string file in chat.Items[i].Files.Split('\n')) if (file.Length > 0) turn.Files.Add(file); break; }
                chat.Live = turn;
                try
                {
                    if (IsHome(chat.Folder) && !Directory.Exists(chat.Folder)) chat.Folder = MakeHome();
                    if (!Directory.Exists(chat.Folder)) throw new IOException("That folder isn't here.");
                    foreach (string extra in turn.Extra) if (!Directory.Exists(extra)) throw new IOException("That folder isn't here: " + extra);
                    BeginUndo(chat, turn.Id);
                    turn.Settings = Path.Combine(Root, "turns", chat.Id, turn.Id + ".json");
                    turn.Wrapper = Path.ChangeExtension(turn.Settings, ".cmd");
                    for (int i = chat.Items.Count - 1; i >= 0; i--) if (chat.Items[i].Kind == "user") { chat.Items[i].Turn = turn.Id; break; }
                    Save(chat);
                }
                catch (Exception error)
                {
                    chat.Live = null;
                    Add(chat, new WorkspaceItem { Kind = "error", Text = error.Message, Turn = turn.Id });
                    turn.Done.Set();
                    return false;
                }
                string prompt = Compose(text, turn.Files);
                new Thread(delegate() { Run(turn, exe, prompt); }) { IsBackground = true, Name = "Capri Workspace turn" }.Start();
            }
            Notify(chat, null);
            return true;
        }

        private static List<string> LastFiles(WorkspaceChat chat)
        {
            List<string> files = new List<string>();
            for (int i = chat.Items.Count - 1; i >= 0; i--)
                if (chat.Items[i].Kind == "user") { foreach (string file in chat.Items[i].Files.Split('\n')) if (file.Length > 0) files.Add(file); break; }
            return files;
        }

        internal void Retry(WorkspaceChat chat)
        {
            string text = null;
            lock (chat.Gate) for (int i = chat.Items.Count - 1; i >= 0; i--) if (chat.Items[i].Kind == "user") { text = chat.Items[i].Text; break; }
            if (text != null) Send(chat, text, true);
        }

        internal static ProcessStartInfo StartInfo(string exe, List<string> args, string folder)
        {
            ProcessStartInfo start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            start.EnvironmentVariables["CAPRI_WORKSPACE_LAUNCHER"] = Packaged.Launcher(System.Windows.Forms.Application.ExecutablePath);
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                start.EnvironmentVariables["CAPRI_WORKSPACE_APP"] = exe;
                StringBuilder line = new StringBuilder("/d /s /v:off /c \"\"%CAPRI_WORKSPACE_APP%\"");
                for (int i = 0; i < args.Count; i++)
                {
                    string key = "CAPRI_WORKSPACE_ARG" + i;
                    // Backslashes right before the closing quote would escape it and swallow the next argument, so a folder
                    // like C:\ goes out as "C:\\", which Claude Code reads back as C:\.
                    string value = args[i];
                    int slashes = value.Length - value.TrimEnd('\\').Length;
                    start.EnvironmentVariables[key] = value + new string('\\', slashes);
                    line.Append(" \"%").Append(key).Append("%\"");
                }
                start.Arguments = line.Append('"').ToString();
            }
            else { start.FileName = exe; start.Arguments = PlanBrain.Joined(args); }
            return start;
        }

        private static void Tail(StringBuilder text, string line)
        {
            text.Append(line).Append('\n');
            if (text.Length > 8000) text.Remove(0, text.Length - 8000);
        }

        private void Run(WorkspaceTurn turn, string exe, string text)
        {
            WorkspaceChat chat = turn.Chat;
            int code = -1;
            try
            {
                ReadVersion(exe, chat.Folder);
                Atomic(turn.Settings, Settings(chat, turn.Id, Packaged.Launcher(System.Windows.Forms.Application.ExecutablePath), turn.Wrapper));
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    code = RunProcess(turn, exe, text);
                    if (!Restart(turn, attempt)) break;
                    lock (chat.Gate)
                    {
                        chat.Session = turn.Session = "";
                        turn.Result = turn.Failed = turn.TextSeen = turn.SessionSeen = false;
                        turn.ResultText = "";
                        turn.Fault = null;
                        turn.Output.Clear();
                        turn.Tools.Clear();
                        turn.Edits.Clear();
                        lock (turn.Errors) turn.Errors.Clear();
                    }
                    Add(chat, new WorkspaceItem { Kind = "note", Text = "That Claude Code session expired, so this chat starts a new one.", Turn = turn.Id });
                }
            }
            catch (Exception error) { turn.Fault = error.Message; }
            finally
            {
                try
                {
                    End(turn);
                    try { FinishUndo(chat, turn.Id); }
                    catch (Exception error) { Add(chat, new WorkspaceItem { Kind = "note", Text = "Couldn't finish the undo copies: " + error.Message, Turn = turn.Id }); }
                    lock (chat.Gate)
                    {
                        CancelAsks(turn, turn.Stopped ? "Stopped." : "The turn ended.");
                        if (turn.Stopped) RecordStop(turn);
                        if (!turn.Stopped && !chat.Deleted)
                        {
                            string stderr;
                            lock (turn.Errors) stderr = turn.Errors.ToString().Trim();
                            if (turn.Failed || !turn.Result)
                            {
                                string reason = turn.Fault ?? (turn.Failed && turn.ResultText.Length > 0 ? turn.ResultText : "");
                                if (stderr.Length > 0) reason += (reason.Length > 0 ? "\n" : "") + stderr;
                                if (reason.Length == 0) reason = turn.Output.ToString().Trim();
                                if (reason.Length == 0) reason = "Claude Code ended without a reply (exit " + code + ").";
                                Add(chat, new WorkspaceItem { Kind = "error", Text = reason, Turn = turn.Id });
                            }
                        }
                        if (chat.Live == turn) chat.Live = null;
                        Save(chat);
                    }
                    Flush(chat);
                }
                catch (Exception error)
                {
                    lock (chat.Gate)
                    {
                        if (chat.Live == turn) chat.Live = null;
                        chat.Items.Add(new WorkspaceItem { Kind = "error", Text = error.Message, Turn = turn.Id });
                    }
                }
                finally
                {
                    try { if (turn.Settings != null) File.Delete(turn.Settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    try { if (turn.Wrapper != null) File.Delete(turn.Wrapper); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    turn.Done.Set();
                    Notify(chat, null);
                    Action<WorkspaceChat> finished = Finished;
                    if (!closed && !turn.Stopped && !chat.Deleted && finished != null) finished(chat);
                }
            }
        }

        private int RunProcess(WorkspaceTurn turn, string exe, string text)
        {
            // No AI is on: Workspace's Claude Code is a brain too, so the turn ends saying why.
            if (Brain.Off) { Brain.TurnAway("Workspace"); throw new BrainException(Brain.OffLine, true); }
            int attempt;
            lock (turn.Chat.Gate)
            {
                attempt = ++turn.Attempt;
                turn.Active = attempt;
                turn.OutputDone.Reset();
                turn.ErrorDone.Reset();
            }
            using (Process process = new Process { StartInfo = StartInfo(exe, Arguments(turn.Mode, turn.Session, turn.Model, turn.Settings, turn.Trusted, turn.Extra), turn.Chat.Folder) })
            using (WorkspaceJob job = new WorkspaceJob())
            {
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs row)
                {
                    lock (turn.Chat.Gate)
                    {
                        if (turn.Active != attempt) return;
                        if (row.Data == null) { turn.OutputDone.Set(); return; }
                        try { Parse(turn, row.Data); }
                        catch (Exception error) { turn.Fault = error.Message; End(turn); }
                    }
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs row)
                {
                    lock (turn.Chat.Gate)
                    {
                        if (turn.Active != attempt) return;
                        if (row.Data == null) { turn.ErrorDone.Set(); return; }
                        lock (turn.Errors) Tail(turn.Errors, row.Data);
                    }
                };
                try
                {
                    lock (turn.ProcessGate)
                    {
                        if (turn.Stopped) return -1;
                        process.Start();
                        turn.Process = process;
                        turn.Job = job;
                        if (!job.Bind(process)) throw new IOException("Workspace couldn't track Claude Code's processes.");
                    }
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    try { WorkspaceHook.Write(process.StandardInput.BaseStream, Encoding.UTF8.GetBytes(PlanBrain.Streamed(text, new List<string>())), 5000); }
                    catch (IOException) { }
                    finally { process.StandardInput.Close(); }
                    while (!process.WaitForExit(50))
                    {
                        if (turn.Stopped || turn.Fault != null || turn.Result && turn.Clock.ElapsedMilliseconds - turn.ResultAt >= 10000) { End(turn); break; }
                    }
                    if (!process.WaitForExit(2000)) throw new IOException("Claude Code didn't stop in time.");
                    turn.OutputDone.WaitOne(2000);
                    turn.ErrorDone.WaitOne(2000);
                    return process.ExitCode;
                }
                finally
                {
                    End(turn);
                    try { process.CancelOutputRead(); process.CancelErrorRead(); } catch (InvalidOperationException) { }
                    lock (turn.Chat.Gate) turn.Active = 0;
                    lock (turn.ProcessGate) { turn.Process = null; turn.Job = null; }
                }
            }
        }

        internal static bool Expired(string text) { return (text ?? "").IndexOf("No conversation found", StringComparison.OrdinalIgnoreCase) >= 0; }

        private static bool Restart(WorkspaceTurn turn, int attempt)
        {
            if (attempt != 0 || turn.Stopped || string.IsNullOrEmpty(turn.Session) || turn.Result && !turn.Failed) return false;
            lock (turn.Errors) return Expired(turn.Errors.ToString()) || Expired(turn.ResultText) || Expired(turn.Output.ToString()) || Expired(turn.Fault);
        }

        internal void Parse(WorkspaceTurn turn, string line)
        {
            Dictionary<string, object> row;
            try { row = HuskJson.Read(line) as Dictionary<string, object>; }
            catch (FormatException) { lock (turn.Chat.Gate) Tail(turn.Output, line); return; }
            if (row == null) return;
            WorkspaceChat chat = turn.Chat;
            lock (chat.Gate)
            {
                if (turn.Stopped || turn.Result || chat.Deleted) return;
                string session = HuskJson.Text(row, "session_id"), type = HuskJson.Text(row, "type");
                if (!turn.SessionSeen && session.Length > 0)
                {
                    turn.SessionSeen = true;
                    chat.Session = session;
                    Save(chat);
                }
                Dictionary<string, object> message = HuskJson.Get(row, "message") as Dictionary<string, object>;
                List<object> blocks = HuskJson.List(HuskJson.Get(message, "content"));
                if (blocks != null) foreach (object block in blocks)
                {
                    Dictionary<string, object> content = block as Dictionary<string, object>;
                    if (content == null) continue;
                    string kind = HuskJson.Text(content, "type");
                    if (kind == "text" && type == "assistant")
                    {
                        turn.TextSeen = true;
                        Add(chat, new WorkspaceItem { Kind = "text", Text = HuskJson.Text(content, "text"), Turn = turn.Id });
                    }
                    else if (kind == "tool_use")
                    {
                        string id = HuskJson.Text(content, "id"), tool = HuskJson.Text(content, "name");
                        if (id.Length > 0 && !turn.Tools.Add(id)) continue;
                        Dictionary<string, object> input = HuskJson.Get(content, "input") as Dictionary<string, object>;
                        if (EditTool(tool)) turn.Edits[id] = HuskJson.Map("tool", tool, "input", input);
                        Add(chat, new WorkspaceItem { Kind = "tool", Name = tool, Id = id, Text = Summary(tool, input), Turn = turn.Id, Input = Shown(tool, input) });
                    }
                    else if (kind == "tool_result")
                    {
                        string value = Content(HuskJson.Get(content, "content"));
                        Add(chat, new WorkspaceItem { Kind = "result", Id = HuskJson.Text(content, "tool_use_id"), Text = value.Length > 2000 ? value.Substring(0, 2000) : value, Turn = turn.Id, Decision = true.Equals(HuskJson.Get(content, "is_error")) ? "error" : "" });
                        CheckUndo(turn, HuskJson.Text(content, "tool_use_id"));
                    }
                }
                if (type == "result")
                {
                    turn.ResultText = HuskJson.Text(row, "result");
                    if (turn.ResultText.Length == 0) turn.ResultText = Content(HuskJson.Get(row, "errors"));
                    turn.Failed = true.Equals(HuskJson.Get(row, "is_error")) || HuskJson.Text(row, "subtype").StartsWith("error", StringComparison.Ordinal);
                    if (!turn.TextSeen && !turn.Failed && turn.ResultText.Length > 0) Add(chat, new WorkspaceItem { Kind = "text", Text = turn.ResultText, Turn = turn.Id });
                    string cost = "";
                    object price = HuskJson.Get(row, "total_cost_usd") ?? HuskJson.Get(row, "cost_usd");
                    if (price != null) cost = price is HuskNumber ? ((HuskNumber)price).Text : Convert.ToString(price, CultureInfo.InvariantCulture);
                    double seconds = HuskJson.Get(row, "duration_ms") == null ? turn.Clock.Elapsed.TotalSeconds : Number(row, "duration_ms") / 1000;
                    if (!(turn.Failed && turn.Session.Length > 0 && Expired(turn.ResultText)))
                        Add(chat, new WorkspaceItem { Kind = "end", Text = seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s" + (cost.Length > 0 ? " | $" + cost : ""), Seconds = seconds, Cost = cost, Turn = turn.Id });
                    turn.ResultAt = turn.Clock.ElapsedMilliseconds;
                    turn.Result = true;
                }
                else if (type == "error")
                {
                    string error = Content(HuskJson.Get(row, "error") ?? HuskJson.Get(row, "message"));
                    Tail(turn.Output, error);
                    if (turn.Session.Length == 0 || !Expired(error)) Add(chat, new WorkspaceItem { Kind = "error", Text = error, Turn = turn.Id });
                }
            }
        }

        private static string Content(object value)
        {
            string text = value as string;
            if (text != null) return text;
            List<object> blocks = HuskJson.List(value);
            if (blocks == null) return value == null ? "" : HuskJson.Write(value);
            StringBuilder result = new StringBuilder();
            foreach (object block in blocks)
            {
                Dictionary<string, object> map = block as Dictionary<string, object>;
                string part = map != null && HuskJson.Text(map, "type") == "text" ? HuskJson.Text(map, "text") : block as string ?? HuskJson.Write(block);
                if (result.Length > 0) result.Append('\n');
                result.Append(part);
            }
            return result.ToString();
        }

        internal static string Summary(string tool, Dictionary<string, object> input)
        {
            string detail = HuskJson.Text(input, "file_path");
            if (detail.Length == 0) detail = HuskJson.Text(input, "notebook_path");
            if (detail.Length == 0) detail = HuskJson.Text(input, "path");
            if (tool == "Bash" || tool == "PowerShell") detail = ": " + HuskJson.Text(input, "command");
            else if (detail.Length == 0) detail = HuskJson.Text(input, "pattern");
            string line = Regex.Replace(tool + (detail.StartsWith(":", StringComparison.Ordinal) ? "" : " ") + detail, @"\s+", " ").Trim();
            return line.Length > 180 ? line.Substring(0, 177) + "..." : line;
        }

        internal void Stop(WorkspaceChat chat)
        {
            WorkspaceTurn turn = chat.Live;
            if (turn == null) return;
            lock (turn.ProcessGate)
            {
                if (turn.Stopped) return;
                turn.Stopped = true;
                turn.Cancel.Set();
                End(turn);
            }
            new Thread(delegate() { try { RecordStop(turn); } catch (Exception) { } }) { IsBackground = true, Name = "Capri Workspace stop" }.Start();
        }

        private void RecordStop(WorkspaceTurn turn)
        {
            lock (turn.Chat.Gate)
            {
                if (turn.StopRecorded || turn.Chat.Deleted) return;
                turn.StopRecorded = true;
                Add(turn.Chat, new WorkspaceItem { Kind = "stopped", Text = "Stopped.", Turn = turn.Id });
            }
        }

        private static void End(WorkspaceTurn turn)
        {
            lock (turn.ProcessGate)
            {
                KillTree(turn.Process);
                if (turn.Job != null) turn.Job.End();
            }
        }

        internal static void KillTree(Process process)
        {
            if (process == null) return;
            try
            {
                if (process.HasExited) return;
                using (Process kill = Process.Start(new ProcessStartInfo("taskkill.exe", "/T /F /PID " + process.Id) { UseShellExecute = false, CreateNoWindow = true }))
                {
                    if (!kill.WaitForExit(900)) kill.Kill();
                }
            }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { }
            catch (System.ComponentModel.Win32Exception) { }
        }

        private static void CancelAsks(WorkspaceTurn turn, string reason)
        {
            foreach (WorkspaceApproval ask in turn.Asks) if (ask.Choice == null) { ask.Choice = "deny"; ask.Reason = reason; ask.Answered.Set(); }
        }

        internal bool Answer(WorkspaceChat chat, WorkspaceApproval ask, bool allow, bool always, string reason)
        {
            if (ask == null || ask.Turn == null) return false;
            lock (chat.Gate)
            {
                if (chat.Live != ask.Turn || ask.Turn.Stopped || ask.Turn.Result || ask.Choice != null) return false;
                if (allow && always) { chat.Allowed.Add(ask.Item.Name); Save(chat); }
                ask.Choice = allow ? "allow" : "deny";
                ask.Reason = allow ? "Allowed in Workspace." : string.IsNullOrWhiteSpace(reason) ? "Denied in Workspace." : reason.Trim();
                ask.Answered.Set();
                return true;
            }
        }

        private static bool QuietTool(string tool)
        {
            return ReadTool(tool) || Regex.IsMatch(tool ?? "", @"\A(?:TodoRead|TodoWrite|TaskList|TaskGet|TaskCreate|TaskUpdate|TaskOutput)\z", RegexOptions.CultureInvariant);
        }

        internal static bool EditTool(string tool) { return tool == "Write" || tool == "Edit" || tool == "MultiEdit" || tool == "NotebookEdit"; }

        // What an edit's card keeps of its input to show the change: the file and the changed text, each text cut to
        // ShownLines lines and ShownChars letters, and no more than ShownAll letters in all, so a 5,000-line Write doesn't
        // fill the saved chat. "cut" says something was left out. Other tools keep nothing.
        internal const int ShownLines = 200, ShownChars = 20000, ShownAll = 40000;

        internal static string Shown(string tool, Dictionary<string, object> input)
        {
            if (!EditTool(tool) || input == null) return "";
            bool cut = false;
            int left = ShownAll;
            Dictionary<string, object> kept = Kept(input, ref cut, ref left);
            List<object> edits = HuskJson.List(HuskJson.Get(input, "edits"));
            if (edits != null)
            {
                List<object> shown = new List<object>();
                foreach (object edit in edits)
                {
                    Dictionary<string, object> one = edit as Dictionary<string, object>;
                    if (one == null) continue;
                    if (left <= 0) { cut = true; break; }
                    shown.Add(Kept(one, ref cut, ref left));
                }
                kept["edits"] = shown;
            }
            if (cut) kept["cut"] = true;
            return HuskJson.Write(kept);
        }

        private static Dictionary<string, object> Kept(Dictionary<string, object> input, ref bool cut, ref int left)
        {
            Dictionary<string, object> kept = new Dictionary<string, object>();
            foreach (string key in new[] { "file_path", "notebook_path" }) { string path = HuskJson.Text(input, key); if (path.Length > 0) kept[key] = path; }
            foreach (string key in new[] { "old_string", "new_string", "content", "new_source" })
            {
                string text = HuskJson.Get(input, key) as string;
                if (text == null) continue;
                int most = Math.Max(0, Math.Min(ShownChars, left));
                if (text.Length > most) { text = text.Substring(0, most); cut = true; }
                int lines = 0, at = 0;
                while (at < text.Length && (at = text.IndexOf('\n', at)) >= 0) { if (++lines >= ShownLines) { text = text.Substring(0, at); cut = true; break; } at++; }
                left -= text.Length;
                kept[key] = text;
            }
            return kept;
        }
        private static bool ReadTool(string tool) { return tool == "Read" || tool == "Glob" || tool == "Grep" || tool == "LS"; }

        private static void Paths(object value, List<string> paths, bool path)
        {
            Dictionary<string, object> map = value as Dictionary<string, object>;
            if (map != null)
            {
                foreach (KeyValuePair<string, object> pair in map)
                    Paths(pair.Value, paths, Regex.IsMatch(pair.Key, @"(?:^|_)(?:path|paths|directory|directories|cwd)\z|(?:Path|Paths)\z", RegexOptions.CultureInvariant));
                return;
            }
            List<object> list = HuskJson.List(value);
            if (list != null) { foreach (object item in list) Paths(item, paths, path); return; }
            string text = value as string;
            if (path && !string.IsNullOrWhiteSpace(text)) paths.Add(text);
        }

        internal static bool Inside(string folder, string tool, Dictionary<string, object> input, string cwd)
        {
            return Inside(new List<string> { folder }, null, tool, input, cwd);
        }

        // Every path the tool names sits in one of the chat's folders or is exactly one of its files.
        internal static bool Inside(List<string> folders, List<string> files, string tool, Dictionary<string, object> input, string cwd)
        {
            string folder = folders[0];
            try
            {
                List<string> paths = new List<string>();
                Paths(input, paths, false);
                if (tool == "Glob")
                {
                    string pattern = HuskJson.Text(input, "pattern");
                    // A .. after the first wildcard, like **/../../*, climbs out of wherever the start of the pattern is.
                    if (Regex.IsMatch(pattern, @"(?:^|[\\/])\.\.(?:[\\/]|$)")) return false;
                    int wild = pattern.IndexOfAny(new[] { '*', '?', '[' });
                    if (wild >= 0)
                    {
                        pattern = pattern.Substring(0, wild);
                        int slash = pattern.LastIndexOfAny(new[] { '\\', '/' });
                        pattern = slash < 0 ? "." : pattern.Substring(0, slash + 1);
                    }
                    if (pattern.Length > 0) paths.Add(pattern);
                }
                List<string> roots = new List<string>(), exact = new List<string>();
                foreach (string each in folders) roots.Add(WorkspacePaths.Final(each).TrimEnd('\\') + "\\");
                if (files != null) foreach (string file in files) exact.Add(WorkspacePaths.Final(file));
                foreach (string path in paths)
                {
                    string full = WorkspacePaths.Final(WorkspacePaths.Full(path, string.IsNullOrEmpty(cwd) ? folder : cwd));
                    bool found = exact.Exists(delegate(string file) { return string.Equals(file, full, StringComparison.OrdinalIgnoreCase); });
                    foreach (string root in roots)
                        if (string.Equals(full.TrimEnd('\\') + "\\", root, StringComparison.OrdinalIgnoreCase) || full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) found = true;
                    if (!found) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        internal static bool Pending(WorkspaceChat chat)
        {
            lock (chat.Gate)
            {
                WorkspaceTurn turn = chat.Live;
                if (turn == null) return false;
                foreach (WorkspaceApproval ask in turn.Asks) if (ask.Choice == null) return true;
                return false;
            }
        }

        internal Dictionary<string, object> Permission(Dictionary<string, object> row)
        {
            string id = HuskJson.Text(row, "chat"), key = HuskJson.Text(row, "turn"), tool = HuskJson.Text(row, "tool_name"), cwd = HuskJson.Text(row, "cwd");
            Dictionary<string, object> input = HuskJson.Get(row, "tool_input") as Dictionary<string, object>;
            WorkspaceChat chat;
            lock (gate) chat = chats.Find(delegate(WorkspaceChat found) { return found.Id == id; });
            if (chat == null || input == null || tool.Length == 0) return WorkspaceHook.Decision(false, "That Workspace turn isn't running.");
            WorkspaceTurn turn;
            WorkspaceApproval ask = null;
            lock (chat.Gate)
            {
                turn = chat.Live;
                if (turn == null || turn.Id != key || turn.Stopped || turn.Result || closed || chat.Deleted) return WorkspaceHook.Decision(false, "That Workspace turn isn't running.");
                if (turn.Mode == "Plan only") return HuskJson.Map();
                bool inside = Inside(AllFolders(chat), AllFiles(chat), tool, input, cwd);
                bool quiet = turn.Mode == "Everything" || inside && (turn.Mode == "Edits only" ? ReadTool(tool) || EditTool(tool) : QuietTool(tool));
                if (!quiet && !chat.Allowed.Contains(tool))
                {
                    WorkspaceItem item = new WorkspaceItem { Kind = "approval", Name = tool, Text = Summary(tool, input), Input = HuskJson.Write(input), Turn = turn.Id };
                    ask = new WorkspaceApproval { Item = item, Turn = turn };
                    item.Ask = ask;
                    turn.Asks.Add(ask);
                    Add(chat, item);
                }
            }
            bool allow = true;
            string reason = "Allowed in Workspace.";
            if (ask != null)
            {
                Action<WorkspaceChat, WorkspaceApproval> pending = Waiting;
                if (pending != null) pending(chat, ask);
                WaitHandle.WaitAny(new WaitHandle[] { ask.Answered, turn.Cancel }, ApprovalWait);
                lock (chat.Gate)
                {
                    if (ask.Choice == null) { ask.Choice = "deny"; ask.Reason = "No answer after ten minutes."; }
                    allow = ask.Choice == "allow";
                    reason = ask.Reason;
                }
            }
            lock (chat.Gate)
            {
                if (chat.Live != turn || turn.Stopped || turn.Result || closed || chat.Deleted) { allow = false; reason = "That Workspace turn ended."; }
                if (allow)
                {
                    try { Snapshot(chat, turn.Id, tool, input, cwd.Length == 0 ? chat.Folder : cwd); }
                    catch (Exception error) { allow = false; reason = "Couldn't save an undo copy: " + error.Message; }
                }
                if (turn.Stopped || closed || chat.Deleted) { allow = false; reason = "That Workspace turn ended."; }
                if (ask != null)
                {
                    ask.Item.Decision = allow ? "allow" : "deny";
                    ask.Item.Text = allow ? "Allowed: " + Summary(tool, input) : "Denied: " + reason;
                    turn.Asks.Remove(ask);
                    ask.Item.Ask = null;
                    ask.Answered.Dispose();
                    Save(chat);
                    Notify(chat, ask.Item);
                }
            }
            return WorkspaceHook.Decision(allow, reason);
        }

        private void Listen()
        {
            lock (pipeGate)
            {
                if (closed || server != null) return;
                waiting = MakePipe(true);
                server = new Thread(Serve) { IsBackground = true, Name = "Capri Workspace" };
                server.Start();
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PipeAttributes
        {
            internal int Length;
            internal IntPtr Descriptor;
            [MarshalAs(UnmanagedType.Bool)] internal bool Inherit;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafePipeHandle CreateNamedPipe(string name, uint open, uint mode, uint count, uint output, uint input, uint timeout, ref PipeAttributes security);

        private NamedPipeServerStream MakePipe(bool first)
        {
            PipeSecurity security = new PipeSecurity();
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            byte[] bytes = security.GetSecurityDescriptorBinaryForm();
            IntPtr descriptor = Marshal.AllocHGlobal(bytes.Length);
            try
            {
                Marshal.Copy(bytes, 0, descriptor, bytes.Length);
                PipeAttributes attributes = new PipeAttributes { Length = Marshal.SizeOf(typeof(PipeAttributes)), Descriptor = descriptor };
                SafePipeHandle handle = CreateNamedPipe(@"\\.\pipe\" + Pipe, 0x40000003U | (first ? 0x00080000U : 0), 8, 255, 4096, 4096, 0, ref attributes);
                if (handle.IsInvalid) { int error = Marshal.GetLastWin32Error(); handle.Dispose(); throw new IOException("Workspace couldn't open its approval pipe (" + error + ")."); }
                try { return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle); }
                catch { handle.Dispose(); throw; }
            }
            finally { Marshal.FreeHGlobal(descriptor); }
        }

        private void Serve()
        {
            try
            {
                while (!closed)
                {
                    NamedPipeServerStream pipe;
                    lock (pipeGate) { if (closed) return; pipe = waiting; }
                    IAsyncResult connection = pipe.BeginWaitForConnection(null, null);
                    while (!connection.AsyncWaitHandle.WaitOne(250)) if (closed) return;
                    pipe.EndWaitForConnection(connection);
                    lock (pipeGate)
                    {
                        if (closed) return;
                        waiting = MakePipe(false);
                        clients.Add(pipe);
                    }
                    new Thread(delegate()
                    {
                        try
                        {
                            Dictionary<string, object> row = HuskJson.Read(WorkspaceHook.ReadLine(pipe, 5000)) as Dictionary<string, object>;
                            string answer = HuskJson.Write(Permission(row)) + "\n";
                            byte[] bytes = Encoding.UTF8.GetBytes(answer);
                            WorkspaceHook.Write(pipe, bytes, 5000);
                        }
                        catch (Exception) { }
                        finally { lock (pipeGate) clients.Remove(pipe); pipe.Dispose(); }
                    }) { IsBackground = true, Name = "Capri Workspace approval" }.Start();
                }
            }
            catch (Exception error)
            {
                if (!closed) lock (gate) Problems.Add("Workspace approvals stopped: " + error.Message);
            }
            finally { lock (pipeGate) { if (waiting != null) { waiting.Dispose(); waiting = null; } server = null; } }
        }

        private string UndoFile(WorkspaceChat chat, string turn) { return Path.Combine(Root, "undo", chat.Id, turn, "manifest.json"); }

        internal void BeginUndo(WorkspaceChat chat, string turn)
        {
            lock (chat.Gate)
            {
                Atomic(UndoFile(chat, turn), HuskJson.Write(HuskJson.Map("files", new List<object>(), "commands", false, "restored", false)));
                chat.Turns.Add(turn);
                while (chat.Turns.Count > 20)
                {
                    string old = chat.Turns[0];
                    string folder = Path.GetDirectoryName(UndoFile(chat, old));
                    if (Directory.Exists(folder)) Directory.Delete(folder, true);
                    chat.Turns.RemoveAt(0);
                }
            }
        }

        internal void Snapshot(WorkspaceChat chat, string turn, string tool, Dictionary<string, object> input, string cwd)
        {
            if (tool != "Bash" && tool != "PowerShell" && !EditTool(tool)) return;
            lock (chat.UndoGate)
            {
                string file = UndoFile(chat, turn);
                Dictionary<string, object> manifest = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (tool == "Bash" || tool == "PowerShell") { manifest["commands"] = true; Atomic(file, HuskJson.Write(manifest)); return; }
                List<string> paths = EditPaths(tool, input);
                if (paths.Count == 0) throw new IOException("The tool didn't name a file.");
                List<object> files = HuskJson.List(HuskJson.Get(manifest, "files"));
                if (files == null) throw new FormatException("The undo list is unreadable.");
                foreach (string target in paths)
                {
                    string original = WorkspacePaths.Final(WorkspacePaths.Full(target, cwd));
                    bool found = false;
                    foreach (object entry in files) if (string.Equals(WorkspacePaths.Final(HuskJson.Text(entry as Dictionary<string, object>, "path")), original, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                    if (found) continue;
                    if (Directory.Exists(original)) throw new IOException("The target is a folder.");
                    bool exists = File.Exists(original);
                    string backup = files.Count.ToString("0000", CultureInfo.InvariantCulture) + ".bin";
                    if (exists) File.Copy(original, Path.Combine(Path.GetDirectoryName(file), backup), true);
                    files.Add(HuskJson.Map("path", original, "created", !exists, "backup", backup));
                    Atomic(file, HuskJson.Write(manifest));
                }
            }
        }

        private static List<string> EditPaths(string tool, Dictionary<string, object> input)
        {
            List<string> paths = new List<string>();
            string path = HuskJson.Text(input, tool == "NotebookEdit" ? "notebook_path" : "file_path");
            if (path.Length > 0) paths.Add(path);
            List<object> edits = HuskJson.List(HuskJson.Get(input, "edits"));
            if (edits != null) foreach (object edit in edits)
            {
                string other = HuskJson.Text(edit as Dictionary<string, object>, "file_path");
                if (other.Length > 0) paths.Add(other);
            }
            return paths;
        }

        private void CheckUndo(WorkspaceTurn turn, string id)
        {
            Dictionary<string, object> edit;
            if (turn.Uncovered || !turn.Edits.TryGetValue(id, out edit)) return;
            bool covered = false;
            try
            {
                lock (turn.Chat.UndoGate)
                {
                    Dictionary<string, object> manifest = HuskJson.Read(File.ReadAllText(UndoFile(turn.Chat, turn.Id), Encoding.UTF8)) as Dictionary<string, object>;
                    List<object> files = HuskJson.List(HuskJson.Get(manifest, "files"));
                    List<string> paths = EditPaths(HuskJson.Text(edit, "tool"), HuskJson.Get(edit, "input") as Dictionary<string, object>);
                    covered = paths.Count > 0 && files != null;
                    foreach (string path in paths)
                    {
                        string full = WorkspacePaths.Final(WorkspacePaths.Full(path, turn.Chat.Folder));
                        bool found = false;
                        if (files != null) foreach (object entry in files)
                        {
                            if (string.Equals(WorkspacePaths.Final(HuskJson.Text(entry as Dictionary<string, object>, "path")), full, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
                        }
                        if (!found) covered = false;
                    }
                }
            }
            catch (Exception) { covered = false; }
            if (!covered)
            {
                turn.Uncovered = true;
                Add(turn.Chat, new WorkspaceItem { Kind = "note", Text = "Undo can't cover this edit: Capri's approval hook didn't run.", Turn = turn.Id });
            }
        }

        internal void FinishUndo(WorkspaceChat chat, string turn)
        {
            lock (chat.UndoGate)
            {
                if (chat.Deleted) return;
                string file = UndoFile(chat, turn);
                Dictionary<string, object> manifest = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                List<object> files = HuskJson.List(HuskJson.Get(manifest, "files"));
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    Dictionary<string, object> entry = files[i] as Dictionary<string, object>;
                    string path = HuskJson.Text(entry, "path");
                    if (true.Equals(HuskJson.Get(entry, "created")) && !File.Exists(path)) { files.RemoveAt(i); continue; }
                    entry["end"] = WorkspacePaths.Fingerprint(path);
                }
                Atomic(file, HuskJson.Write(manifest));
            }
        }

        internal bool CanUndo(WorkspaceChat chat)
        {
            lock (chat.Gate) return chat.Live == null && chat.Undoing == null && LastUndo(chat) != null;
        }

        private string LastUndo(WorkspaceChat chat)
        {
            for (int i = chat.Turns.Count - 1; i >= 0; i--)
            {
                string file = UndoFile(chat, chat.Turns[i]);
                if (!File.Exists(file)) continue;
                Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (true.Equals(HuskJson.Get(row, "restored"))) continue;
                // A turn that changed no files and ran no commands has nothing to undo.
                List<object> files = HuskJson.List(HuskJson.Get(row, "files"));
                if ((files == null || files.Count == 0) && !true.Equals(HuskJson.Get(row, "commands"))) continue;
                return chat.Turns[i];
            }
            return null;
        }

        internal string Undo(WorkspaceChat chat)
        {
            lock (chat.Gate)
            lock (chat.UndoGate)
            {
                lock (chat.Gate)
                {
                    if (chat.Live != null) return "Stop the turn before undoing it.";
                    if (chat.Undoing != null) return chat.Undoing.Item.Text;
                }
                string turn = LastUndo(chat);
                if (turn == null) return "No edits to undo.";
                string file = UndoFile(chat, turn);
                Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                List<object> files = HuskJson.List(HuskJson.Get(row, "files"));
                foreach (object entry in files)
                {
                    Dictionary<string, object> saved = entry as Dictionary<string, object>;
                    saved["path"] = WorkspacePaths.Final(HuskJson.Text(saved, "path"));
                }
                List<string> restored = new List<string>(), removed = new List<string>(), changed = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    Dictionary<string, object> saved = files[i] as Dictionary<string, object>;
                    if (true.Equals(HuskJson.Get(saved, "done"))) continue;
                    string path = HuskJson.Text(saved, "path");
                    if (!seen.Add(WorkspacePaths.Final(path))) { saved["done"] = true; continue; }
                    bool same = false;
                    try { same = WorkspacePaths.Matches(path, HuskJson.Get(saved, "end") as Dictionary<string, object>); }
                    catch (Exception) { }
                    if (!same) changed.Add(path);
                    else Restore(file, row, saved, restored, removed);
                }
                Atomic(file, HuskJson.Write(row));
                if (changed.Count > 0)
                {
                    if (restored.Count + removed.Count > 0) Add(chat, new WorkspaceItem { Kind = "undo", Text = UndoText(restored, removed), Turn = turn });
                    WorkspaceUndo pending = new WorkspaceUndo { Turn = turn };
                    pending.Item = new WorkspaceItem { Kind = "undo-ask", Turn = turn, Text = "These changed after that turn: " + string.Join(", ", changed.ToArray()) + ". Undo them too?", Undo = pending };
                    lock (chat.Gate) chat.Undoing = pending;
                    Add(chat, pending.Item);
                    return pending.Item.Text;
                }
                row["restored"] = true;
                Atomic(file, HuskJson.Write(row));
                string text = UndoText(restored, removed);
                Add(chat, new WorkspaceItem { Kind = "undo", Text = text, Turn = turn });
                return text;
            }
        }

        private static void Restore(string file, Dictionary<string, object> row, Dictionary<string, object> saved, List<string> restored, List<string> removed)
        {
            string path = WorkspacePaths.Final(HuskJson.Text(saved, "path"));
            if (Directory.Exists(path)) throw new IOException("The undo target is a folder.");
            if (true.Equals(HuskJson.Get(saved, "created")))
            {
                if (File.Exists(path)) WorkspacePaths.Recycle(path);
                removed.Add(path);
            }
            else
            {
                string backup = HuskJson.Text(saved, "backup");
                if (!Regex.IsMatch(backup, @"\A[0-9]{4}\.bin\z")) throw new IOException("The undo copy is unreadable.");
                string copy = Path.Combine(Path.GetDirectoryName(file), backup);
                if (!File.Exists(copy)) throw new IOException("The undo copy isn't here.");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (File.Exists(path)) WorkspacePaths.Recycle(path);
                File.Copy(copy, path, false);
                restored.Add(path);
            }
            saved["done"] = true;
            Atomic(file, HuskJson.Write(row));
        }

        private static string UndoText(List<string> restored, List<string> removed)
        {
            string text = (restored.Count > 0 ? "Restored: " + string.Join(", ", restored.ToArray()) + ". " : "")
                + (removed.Count > 0 ? "Removed: " + string.Join(", ", removed.ToArray()) + ". " : "");
            if (text.Length == 0) text = "No saved file edits in that turn. ";
            return text + "Commands aren't undone.";
        }

        internal void AnswerUndo(WorkspaceChat chat, WorkspaceUndo pending, bool all)
        {
            lock (chat.Gate)
            lock (chat.UndoGate)
            {
                lock (chat.Gate) if (chat.Live != null || chat.Undoing != pending) return;
                string file = UndoFile(chat, pending.Turn);
                Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                List<object> files = HuskJson.List(HuskJson.Get(row, "files"));
                List<string> restored = new List<string>(), removed = new List<string>();
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = files.Count - 1; i >= 0; i--)
                {
                    Dictionary<string, object> saved = files[i] as Dictionary<string, object>;
                    string path = HuskJson.Text(saved, "path");
                    if (seen.Add(WorkspacePaths.Final(path)) && !true.Equals(HuskJson.Get(saved, "done")) && all)
                    {
                        Restore(file, row, saved, restored, removed);
                    }
                    saved["done"] = true;
                }
                row["restored"] = true;
                Atomic(file, HuskJson.Write(row));
                lock (chat.Gate)
                {
                    pending.Item.Kind = "undo";
                    pending.Item.Text = all ? UndoText(restored, removed) : "Kept the files that changed after that turn. Commands aren't undone.";
                    pending.Item.Undo = null;
                    chat.Undoing = null;
                    Save(chat);
                }
                Notify(chat, pending.Item);
            }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
            saveReady.Set();
            List<WorkspaceChat> copy = Chats();
            foreach (WorkspaceChat chat in copy) Stop(chat);
            lock (pipeGate)
            {
                if (waiting != null) waiting.Dispose();
                foreach (NamedPipeServerStream pipe in clients) pipe.Dispose();
                clients.Clear();
            }
        }
    }

    internal static class WorkspacePaths
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FileOp
        {
            internal IntPtr Window;
            internal uint Function;
            internal string From, To;
            internal ushort Flags;
            [MarshalAs(UnmanagedType.Bool)] internal bool Aborted;
            internal IntPtr Mappings;
            internal string Title;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, StringBuilder path, uint size, uint flags);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetShortPathName(string path, StringBuilder result, uint size);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref FileOp operation);

        internal static string Full(string path, string cwd)
        {
            if (path == "~" || path.StartsWith("~/", StringComparison.Ordinal) || path.StartsWith(@"~\", StringComparison.Ordinal))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length < 2 ? "" : path.Substring(2));
            cwd = Path.GetFullPath(cwd);
            if (Regex.IsMatch(path, @"\A[A-Za-z]:(?![\\/])"))
            {
                if (!string.Equals(Path.GetPathRoot(cwd).Substring(0, 2), path.Substring(0, 2), StringComparison.OrdinalIgnoreCase)) throw new IOException("That path needs a full drive name.");
                return Path.GetFullPath(Path.Combine(cwd, path.Substring(2)));
            }
            if ((path.StartsWith(@"\", StringComparison.Ordinal) && !path.StartsWith(@"\\", StringComparison.Ordinal))
                || (path.StartsWith("/", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal)))
                return Path.GetFullPath(Path.Combine(Path.GetPathRoot(cwd), path.TrimStart('\\', '/')));
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(cwd, path));
        }

        internal static string Final(string path)
        {
            string full = Path.GetFullPath(path), found = full;
            List<string> tail = new List<string>();
            while (!File.Exists(found) && !Directory.Exists(found))
            {
                string parent = Path.GetDirectoryName(found);
                if (string.IsNullOrEmpty(parent) || parent == found) throw new IOException("The file's folder isn't here.");
                tail.Add(Path.GetFileName(found));
                found = parent;
            }
            using (SafeFileHandle file = CreateFile(found, 0, 7, IntPtr.Zero, 3, 0x02000000, IntPtr.Zero))
            {
                if (file.IsInvalid) throw new IOException("Couldn't resolve the file's path.");
                StringBuilder text = new StringBuilder(32768);
                uint size = GetFinalPathNameByHandle(file, text, (uint)text.Capacity, 0);
                if (size == 0 || size >= text.Capacity) throw new IOException("Couldn't resolve the file's path.");
                full = text.ToString();
            }
            if (full.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) full = @"\\" + full.Substring(8);
            else if (full.StartsWith(@"\\?\", StringComparison.Ordinal)) full = full.Substring(4);
            for (int i = tail.Count - 1; i >= 0; i--) full = Path.Combine(full, tail[i]);
            return Path.GetFullPath(full);
        }

        internal static string Short(string path)
        {
            StringBuilder text = new StringBuilder(32768);
            uint size = GetShortPathName(path, text, (uint)text.Capacity);
            return size == 0 || size >= text.Capacity ? path : text.ToString();
        }

        internal static Dictionary<string, object> Fingerprint(string path)
        {
            if (!File.Exists(path))
            {
                if (Directory.Exists(path)) throw new IOException("The undo target is a folder.");
                return HuskJson.Map("exists", false);
            }
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 hash = SHA256.Create())
            {
                long length = file.Length, written = File.GetLastWriteTimeUtc(path).Ticks;
                string digest = Convert.ToBase64String(hash.ComputeHash(file));
                return HuskJson.Map("exists", true, "size", length.ToString(CultureInfo.InvariantCulture), "written", written.ToString(CultureInfo.InvariantCulture), "sha256", digest, "path", Final(path));
            }
        }

        internal static bool Matches(string path, Dictionary<string, object> saved)
        {
            if (saved == null) return false;
            Dictionary<string, object> now = Fingerprint(path);
            if (true.Equals(HuskJson.Get(saved, "exists")) != true.Equals(HuskJson.Get(now, "exists"))) return false;
            if (!true.Equals(HuskJson.Get(now, "exists"))) return true;
            return HuskJson.Text(saved, "size") == HuskJson.Text(now, "size") && HuskJson.Text(saved, "written") == HuskJson.Text(now, "written")
                && HuskJson.Text(saved, "sha256") == HuskJson.Text(now, "sha256") && string.Equals(HuskJson.Text(saved, "path"), HuskJson.Text(now, "path"), StringComparison.OrdinalIgnoreCase);
        }

        internal static void Recycle(string path)
        {
            FileOp operation = new FileOp { Function = 3, From = Path.GetFullPath(path) + "\0\0", Flags = 0x0040 | 0x0010 | 0x0004 | 0x0400 | 0x0200 | 0x2000 };
            int code = SHFileOperation(ref operation);
            if (code != 0 || operation.Aborted || File.Exists(path)) throw new IOException("Couldn't move the file to the Recycle Bin.");
        }
    }

    internal static class WorkspaceHook
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint process);

        internal static Dictionary<string, object> Decision(bool allow, string reason)
        {
            return HuskJson.Map("hookSpecificOutput", HuskJson.Map("hookEventName", "PreToolUse", "permissionDecision", allow ? "allow" : "deny", "permissionDecisionReason", reason));
        }

        internal static string ReadLine(Stream stream, int timeout)
        {
            Stopwatch clock = Stopwatch.StartNew();
            using (MemoryStream text = new MemoryStream())
            {
                byte[] buffer = new byte[4096];
                while (true)
                {
                    IAsyncResult read = stream.BeginRead(buffer, 0, buffer.Length, null, null);
                    // EndRead waits on this same handle and closes it itself, so it mustn't be disposed here.
                    int left = Math.Max(0, timeout - (int)clock.ElapsedMilliseconds);
                    if (!read.AsyncWaitHandle.WaitOne(left)) throw new IOException("Workspace didn't answer in time.");
                    int count = stream.EndRead(read);
                    if (count == 0) throw new IOException("Workspace closed the approval pipe.");
                    int end = Array.IndexOf(buffer, (byte)'\n', 0, count);
                    int size = end < 0 ? count : end;
                    if (text.Length + size > HuskHook.InputLimit) throw new IOException("The approval is too long.");
                    text.Write(buffer, 0, size);
                    if (end >= 0) return new UTF8Encoding(false, true).GetString(text.ToArray());
                }
            }
        }

        internal static void Write(Stream stream, byte[] bytes, int timeout)
        {
            IAsyncResult write = stream.BeginWrite(bytes, 0, bytes.Length, null, null);
            if (!write.AsyncWaitHandle.WaitOne(timeout)) throw new IOException("Workspace couldn't send its answer in time.");
            stream.EndWrite(write);
        }

        internal static Dictionary<string, object> Exchange(string pipe, string chat, string turn, Dictionary<string, object> input, int wait)
        {
            try
            {
                Match route = Regex.Match(pipe ?? "", @"\ACapriWorkspace-([1-9][0-9]*)(?:-[A-Za-z0-9-]+)?\z");
                uint expected;
                if (!route.Success || !uint.TryParse(route.Groups[1].Value, out expected) || !Workspace.Key(chat) || !Workspace.Key(turn) || input == null)
                    return Decision(false, "Workspace isn't connected.");
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    client.Connect(1500);
                    uint process;
                    if (!GetNamedPipeServerProcessId(client.SafePipeHandle, out process) || process != expected) return Decision(false, "Workspace's approval pipe belongs to another process.");
                    byte[] bytes = Encoding.UTF8.GetBytes(HuskJson.Write(HuskJson.Map("chat", chat, "turn", turn, "tool_name", HuskJson.Text(input, "tool_name"),
                        "tool_input", HuskJson.Get(input, "tool_input"), "cwd", HuskJson.Text(input, "cwd"))) + "\n");
                    Write(client, bytes, 5000);
                    Dictionary<string, object> answer = HuskJson.Read(ReadLine(client, wait)) as Dictionary<string, object>;
                    if (answer != null && answer.Count == 0) return HuskJson.Map();
                    Dictionary<string, object> output = HuskJson.Get(answer, "hookSpecificOutput") as Dictionary<string, object>;
                    string choice = HuskJson.Text(output, "permissionDecision");
                    if (HuskJson.Text(output, "hookEventName") != "PreToolUse" || (choice != "allow" && choice != "deny")) return Decision(false, "Workspace sent no answer.");
                    return Decision(choice == "allow", HuskJson.Text(output, "permissionDecisionReason"));
                }
            }
            catch (Exception) { return Decision(false, "Workspace didn't answer. Open Workspace and try again."); }
        }

        internal static int Run(string[] args)
        {
            try
            {
                using (StreamWriter output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))) return Run(args, Console.OpenStandardInput(), output);
            }
            catch (Exception) { return 0; }
        }

        internal static int Run(string[] args, Stream input, TextWriter output)
        {
            Dictionary<string, object> answer = Decision(false, "Workspace isn't connected.");
            try
            {
                if (args.Length == 4)
                {
                    using (MemoryStream data = new MemoryStream())
                    {
                        byte[] buffer = new byte[8192];
                        int count;
                        Stopwatch clock = Stopwatch.StartNew();
                        while (true)
                        {
                            IAsyncResult read = input.BeginRead(buffer, 0, buffer.Length, null, null);
                            if (!read.AsyncWaitHandle.WaitOne(Math.Max(0, 5000 - (int)clock.ElapsedMilliseconds))) throw new IOException("Workspace couldn't read this approval in time.");
                            count = input.EndRead(read);
                            if (count == 0) break;
                            if (data.Length + count > HuskHook.InputLimit) throw new IOException("The approval is too long.");
                            data.Write(buffer, 0, count);
                        }
                        answer = Exchange(args[1], args[2], args[3], HuskJson.Read(new UTF8Encoding(false, true).GetString(data.ToArray())) as Dictionary<string, object>, 620000);
                    }
                }
            }
            catch (Exception) { answer = Decision(false, "Workspace couldn't read this approval."); }
            output.WriteLine(HuskJson.Write(answer));
            output.Flush();
            return 0;
        }
    }

    internal sealed class WorkspaceJob : IDisposable
    {
        private IntPtr handle;
        private bool bound;

        [StructLayout(LayoutKind.Sequential)]
        private struct Basic
        {
            internal long ProcessTime, JobTime;
            internal uint Flags;
            internal UIntPtr Minimum, Maximum;
            internal uint Active;
            internal UIntPtr Affinity;
            internal uint Priority, Scheduling;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Limits
        {
            internal Basic Basic;
            internal ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes;
            internal UIntPtr ProcessMemory, JobMemory, PeakProcessMemory, PeakJobMemory;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll")] private static extern bool SetInformationJobObject(IntPtr job, int kind, ref Limits limits, int size);
        [DllImport("kernel32.dll")] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool TerminateJobObject(IntPtr job, uint code);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);

        internal WorkspaceJob()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            Limits limits = new Limits();
            limits.Basic.Flags = 0x2000;
            if (handle != IntPtr.Zero && !SetInformationJobObject(handle, 9, ref limits, Marshal.SizeOf(typeof(Limits)))) { CloseHandle(handle); handle = IntPtr.Zero; }
        }

        internal bool Bind(Process process) { bound = handle != IntPtr.Zero && AssignProcessToJobObject(handle, process.Handle); return bound; }
        internal bool End() { return bound && handle != IntPtr.Zero && TerminateJobObject(handle, 1); }
        public void Dispose() { if (handle != IntPtr.Zero) { CloseHandle(handle); handle = IntPtr.Zero; } }
    }
}
