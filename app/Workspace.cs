using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class WorkspaceItem
    {
        internal string Kind = "", Text = "", Name = "", Id = "", Turn = "", Input = "", Decision = "", Cost = "";
        internal double Seconds;
        internal bool Retry;
        internal WorkspaceApproval Ask;

        internal Dictionary<string, object> Json()
        {
            return HuskJson.Map("kind", Kind, "text", Text, "name", Name, "id", Id, "turn", Turn, "input", Input,
                "decision", Decision, "seconds", Seconds, "cost", Cost, "retry", Retry);
        }

        internal static WorkspaceItem Read(Dictionary<string, object> row)
        {
            return new WorkspaceItem { Kind = HuskJson.Text(row, "kind"), Text = HuskJson.Text(row, "text"), Name = HuskJson.Text(row, "name"),
                Id = HuskJson.Text(row, "id"), Turn = HuskJson.Text(row, "turn"), Input = HuskJson.Text(row, "input"), Decision = HuskJson.Text(row, "decision"),
                Seconds = Workspace.Number(row, "seconds"), Cost = HuskJson.Text(row, "cost"), Retry = true.Equals(HuskJson.Get(row, "retry")) };
        }
    }

    internal sealed class WorkspaceChat
    {
        internal readonly object Gate = new object();
        internal string Id = Guid.NewGuid().ToString("N"), Title = "", Folder = "", Session = "", Mode = "Ask me", Model = "Default";
        internal long Updated = DateTime.UtcNow.Ticks;
        internal readonly List<WorkspaceItem> Items = new List<WorkspaceItem>();
        internal readonly List<string> Turns = new List<string>();
        internal readonly HashSet<string> Allowed = new HashSet<string>(StringComparer.Ordinal);
        internal volatile WorkspaceTurn Live;
        internal bool Deleted;
        internal bool Running { get { return Live != null; } }
    }

    internal sealed class WorkspaceApproval
    {
        internal readonly ManualResetEvent Answered = new ManualResetEvent(false);
        internal WorkspaceItem Item;
        internal WorkspaceTurn Turn;
        internal string Choice, Reason = "";
    }

    internal sealed class WorkspaceTurn
    {
        internal readonly string Id = Guid.NewGuid().ToString("N");
        internal readonly object ProcessGate = new object();
        internal readonly Stopwatch Clock = Stopwatch.StartNew();
        internal readonly List<WorkspaceApproval> Asks = new List<WorkspaceApproval>();
        internal readonly HashSet<string> Tools = new HashSet<string>(StringComparer.Ordinal);
        internal readonly StringBuilder Errors = new StringBuilder(), Output = new StringBuilder();
        internal readonly ManualResetEvent OutputDone = new ManualResetEvent(false), ErrorDone = new ManualResetEvent(false), Done = new ManualResetEvent(false), Cancel = new ManualResetEvent(false);
        internal WorkspaceChat Chat;
        internal string Mode, Model, Session, Settings, ResultText = "", Fault;
        internal Process Process;
        internal WorkspaceJob Job;
        internal volatile bool Stopped, Result, Failed;
        internal bool TextSeen, SessionSeen, StopRecorded;
        internal long ResultAt;
    }

    internal sealed class Workspace : IDisposable
    {
        internal const string Missing = "Workspace runs Claude Code on this PC. Install it from claude.com/claude-code and sign in, then press Retry.";
        internal static readonly string[] Modes = { "Ask me", "Edits only", "Plan only", "Everything" };
        internal static readonly string[] Models = { "Default", "Opus", "Sonnet", "Haiku" };
        internal readonly string Root, Pipe;
        internal string Executable;
        internal int ApprovalWait = 600000;
        internal Action<WorkspaceChat, WorkspaceItem> Changed;
        internal Action<WorkspaceChat> Finished;
        internal readonly List<string> Problems = new List<string>();
        private readonly object gate = new object(), pipeGate = new object();
        private readonly List<WorkspaceChat> chats = new List<WorkspaceChat>();
        private readonly List<NamedPipeServerStream> clients = new List<NamedPipeServerStream>();
        private NamedPipeServerStream waiting;
        private Thread server;
        private volatile bool closed;

        internal Workspace(string data, string pipe)
        {
            Root = Path.Combine(data, "workspace");
            Pipe = pipe ?? "CapriWorkspace-" + Process.GetCurrentProcess().Id;
            Directory.CreateDirectory(Path.Combine(Root, "chats"));
            foreach (string file in Directory.GetFiles(Path.Combine(Root, "chats"), "*.json"))
            {
                try
                {
                    Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                    string id = HuskJson.Text(row, "id");
                    if (!Key(id) || !string.Equals(Path.GetFileNameWithoutExtension(file), id, StringComparison.Ordinal)) throw new FormatException("Invalid chat id.");
                    WorkspaceChat chat = new WorkspaceChat { Id = id, Title = HuskJson.Text(row, "title"), Folder = HuskJson.Text(row, "folder"), Session = HuskJson.Text(row, "session"),
                        Mode = Mode(HuskJson.Text(row, "mode")), Model = Model(HuskJson.Text(row, "model")) };
                    long updated;
                    if (long.TryParse(HuskJson.Text(row, "updated"), NumberStyles.None, CultureInfo.InvariantCulture, out updated)) chat.Updated = updated;
                    List<object> items = HuskJson.List(HuskJson.Get(row, "items")), turns = HuskJson.List(HuskJson.Get(row, "turns")), allowed = HuskJson.List(HuskJson.Get(row, "allowed"));
                    if (items != null) foreach (object item in items)
                    {
                        Dictionary<string, object> map = item as Dictionary<string, object>;
                        if (map == null) continue;
                        WorkspaceItem found = WorkspaceItem.Read(map);
                        if (found.Kind == "approval" && found.Decision.Length == 0) { found.Decision = "deny"; found.Text = "Denied. Workspace closed before an answer."; }
                        chat.Items.Add(found);
                    }
                    if (turns != null) foreach (object turn in turns) if (turn is string && Key((string)turn)) chat.Turns.Add((string)turn);
                    if (allowed != null) foreach (object tool in allowed) if (tool is string) chat.Allowed.Add((string)tool);
                    chats.Add(chat);
                }
                catch (Exception error) { Problems.Add("Couldn't load " + Path.GetFileName(file) + ": " + error.Message); }
            }
            if (chats.Count > 0) Listen();
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

        internal WorkspaceChat NewChat(string folder, bool blackwall)
        {
            folder = Path.GetFullPath(folder);
            if (!Directory.Exists(folder)) throw new IOException("That folder isn't here.");
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
                if (File.Exists(file)) File.Replace(temp, file, null);
                else File.Move(temp, file);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }

        internal void Save(WorkspaceChat chat)
        {
            lock (chat.Gate)
            {
                if (chat.Deleted) return;
                List<object> items = new List<object>(), allowed = new List<object>(), turns = new List<object>();
                foreach (WorkspaceItem item in chat.Items) items.Add(item.Json());
                foreach (string tool in chat.Allowed) allowed.Add(tool);
                foreach (string turn in chat.Turns) turns.Add(turn);
                Atomic(Path.Combine(Root, "chats", chat.Id + ".json"), HuskJson.Write(HuskJson.Map("id", chat.Id, "title", chat.Title, "folder", chat.Folder,
                    "session", chat.Session, "mode", chat.Mode, "model", chat.Model, "updated", chat.Updated.ToString(CultureInfo.InvariantCulture), "items", items, "allowed", allowed, "turns", turns)));
            }
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

        internal void Delete(WorkspaceChat chat)
        {
            Stop(chat);
            lock (chat.Gate)
            {
                File.Delete(Path.Combine(Root, "chats", chat.Id + ".json"));
                chat.Deleted = true;
            }
            lock (gate) chats.Remove(chat);
            string undo = Path.Combine(Root, "undo", chat.Id);
            if (Directory.Exists(undo)) Directory.Delete(undo, true);
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
            string permission = mode == "Edits only" ? "acceptEdits" : mode == "Plan only" ? "plan" : mode == "Everything" ? "bypassPermissions" : "default";
            List<string> args = new List<string> { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json", "--permission-mode", permission };
            if (!string.IsNullOrEmpty(session)) { args.Add("--resume"); args.Add(session); }
            if (!string.IsNullOrEmpty(model) && model != "Default") { args.Add("--model"); args.Add(model.ToLowerInvariant()); }
            if (!string.IsNullOrEmpty(settings)) { args.Add("--settings"); args.Add(settings); }
            return args;
        }

        internal string Settings(WorkspaceChat chat, string turn, string exe)
        {
            string command = "\"" + exe.Replace('\\', '/') + "\" --workspace-hook " + Pipe + " " + chat.Id + " " + turn;
            return HuskJson.Write(HuskJson.Map("hooks", HuskJson.Map("PreToolUse", new List<object> {
                HuskJson.Map("matcher", "*", "hooks", new List<object> { HuskJson.Map("type", "command", "command", command, "timeout", 660) }) })));
        }

        internal bool Send(WorkspaceChat chat, string text, bool retry)
        {
            if (string.IsNullOrWhiteSpace(text) || closed) return false;
            WorkspaceTurn turn;
            lock (chat.Gate)
            {
                if (chat.Live != null || chat.Deleted) return false;
                if (!retry) Add(chat, new WorkspaceItem { Kind = "user", Text = text });
                string exe = Executable == null ? PlanBrain.Find("claude") : File.Exists(Executable) ? Executable : null;
                if (exe == null) { Add(chat, new WorkspaceItem { Kind = "error", Text = Missing, Retry = true }); return false; }
                turn = new WorkspaceTurn { Chat = chat, Mode = chat.Mode, Model = chat.Model, Session = chat.Session };
                chat.Live = turn;
                try
                {
                    if (!Directory.Exists(chat.Folder)) throw new IOException("That folder isn't here.");
                    BeginUndo(chat, turn.Id);
                    turn.Settings = Path.Combine(Root, "turns", chat.Id, turn.Id + ".json");
                    Atomic(turn.Settings, Settings(chat, turn.Id, System.Windows.Forms.Application.ExecutablePath));
                    if (!retry && chat.Items.Count > 0) chat.Items[chat.Items.Count - 1].Turn = turn.Id;
                    Save(chat);
                }
                catch (Exception error)
                {
                    chat.Live = null;
                    Add(chat, new WorkspaceItem { Kind = "error", Text = error.Message, Turn = turn.Id });
                    turn.Done.Set();
                    return false;
                }
                ThreadPool.QueueUserWorkItem(delegate { Run(turn, exe, text); });
            }
            Notify(chat, null);
            return true;
        }

        internal void Retry(WorkspaceChat chat)
        {
            string text = null;
            lock (chat.Gate) for (int i = chat.Items.Count - 1; i >= 0; i--) if (chat.Items[i].Kind == "user") { text = chat.Items[i].Text; break; }
            if (text != null) Send(chat, text, true);
        }

        private static ProcessStartInfo StartInfo(string exe, List<string> args, string folder)
        {
            ProcessStartInfo start = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
            if (exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) || exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase))
            {
                start.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                start.EnvironmentVariables["CAPRI_WORKSPACE_APP"] = exe;
                StringBuilder line = new StringBuilder("/d /s /v:off /c \"\"%CAPRI_WORKSPACE_APP%\"");
                for (int i = 0; i < args.Count; i++)
                {
                    string key = "CAPRI_WORKSPACE_ARG" + i;
                    start.EnvironmentVariables[key] = args[i];
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
                using (Process process = new Process { StartInfo = StartInfo(exe, Arguments(turn.Mode, turn.Session, turn.Model, turn.Settings), chat.Folder) })
                using (WorkspaceJob job = new WorkspaceJob())
                {
                    process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs row)
                    {
                        if (row.Data == null) { turn.OutputDone.Set(); return; }
                        try { Parse(turn, row.Data); }
                        catch (Exception error) { lock (chat.Gate) turn.Fault = error.Message; End(turn); }
                    };
                    process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs row)
                    {
                        if (row.Data == null) { turn.ErrorDone.Set(); return; }
                        lock (turn.Errors) Tail(turn.Errors, row.Data);
                    };
                    lock (turn.ProcessGate)
                    {
                        if (turn.Stopped) return;
                        process.Start();
                        turn.Process = process;
                        turn.Job = job;
                        if (!job.Bind(process))
                        {
                            End(turn);
                            throw new IOException("Workspace couldn't track Claude Code's processes.");
                        }
                    }
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();
                    try
                    {
                        using (StreamWriter input = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) input.Write(PlanBrain.Streamed(text, new List<string>()));
                    }
                    catch (IOException) { }
                    while (!process.WaitForExit(50))
                    {
                        if (turn.Stopped || turn.Fault != null || (turn.Result && turn.Clock.ElapsedMilliseconds - turn.ResultAt > 1500)) End(turn);
                    }
                    code = process.ExitCode;
                    job.End();
                    turn.OutputDone.WaitOne();
                    turn.ErrorDone.WaitOne();
                    lock (turn.ProcessGate) { turn.Process = null; turn.Job = null; }
                }
            }
            catch (Exception error) { turn.Fault = error.Message; }
            finally
            {
                try
                {
                    lock (chat.Gate)
                    {
                        CancelAsks(turn, turn.Stopped ? "Stopped." : "The turn ended.");
                        if (turn.Stopped) RecordStop(turn);
                        if (!turn.Stopped && !chat.Deleted)
                        {
                            string stderr;
                            lock (turn.Errors) stderr = turn.Errors.ToString().Trim();
                            if (code != 0 || turn.Failed || !turn.Result || turn.Fault != null)
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
                }
                catch (Exception error)
                {
                    lock (chat.Gate)
                    {
                        if (chat.Live == turn) chat.Live = null;
                        chat.Items.Add(new WorkspaceItem { Kind = "error", Text = "Couldn't save this turn: " + error.Message, Turn = turn.Id });
                    }
                }
                finally
                {
                    try { if (turn.Settings != null) File.Delete(turn.Settings); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                    turn.Done.Set();
                    Notify(chat, null);
                    Action<WorkspaceChat> finished = Finished;
                    if (!closed && !turn.Stopped && !chat.Deleted && finished != null) finished(chat);
                }
            }
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
                        Add(chat, new WorkspaceItem { Kind = "tool", Name = tool, Id = id, Text = Summary(tool, input), Input = HuskJson.Write(input ?? HuskJson.Map()), Turn = turn.Id });
                    }
                    else if (kind == "tool_result")
                    {
                        string value = Content(HuskJson.Get(content, "content"));
                        Add(chat, new WorkspaceItem { Kind = "result", Id = HuskJson.Text(content, "tool_use_id"), Text = value.Length > 2000 ? value.Substring(0, 2000) : value, Turn = turn.Id });
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
                    Add(chat, new WorkspaceItem { Kind = "end", Text = seconds.ToString("0.0", CultureInfo.InvariantCulture) + "s" + (cost.Length > 0 ? " | $" + cost : ""), Seconds = seconds, Cost = cost, Turn = turn.Id });
                    turn.ResultAt = turn.Clock.ElapsedMilliseconds;
                    turn.Result = true;
                }
                else if (type == "error") Add(chat, new WorkspaceItem { Kind = "error", Text = Content(HuskJson.Get(row, "error") ?? HuskJson.Get(row, "message")), Turn = turn.Id });
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
            ThreadPool.QueueUserWorkItem(delegate { try { RecordStop(turn); } catch (Exception) { } });
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
                if (turn.Job != null && turn.Job.End()) return;
                Process process = turn.Process;
                if (process == null) return;
                try
                {
                    if (process.HasExited) return;
                    using (Process kill = Process.Start(new ProcessStartInfo("taskkill.exe", "/T /F /PID " + process.Id) { UseShellExecute = false, CreateNoWindow = true }))
                    {
                        if (!kill.WaitForExit(900)) kill.Kill();
                    }
                    if (!process.HasExited) process.Kill();
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }
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
            return Regex.IsMatch(tool ?? "", @"\A(?:Read|Glob|Grep|LS|TodoRead|TodoWrite|TaskList|TaskGet|TaskCreate|TaskUpdate|TaskOutput|WebSearch|WebFetch|ListMcpResources|ReadMcpResource|ListMcpResourcesTool|ReadMcpResourceTool|ToolSearch)\z", RegexOptions.CultureInvariant);
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
                if (turn.Mode == "Ask me" && !QuietTool(tool) && !chat.Allowed.Contains(tool))
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
                server = new Thread(Serve) { IsBackground = true, Name = "Capri Workspace" };
                server.Start();
            }
        }

        private void Serve()
        {
            try
            {
                while (!closed)
                {
                    PipeSecurity security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    NamedPipeServerStream pipe = new NamedPipeServerStream(Pipe, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
                    lock (pipeGate) { if (closed) { pipe.Dispose(); return; } waiting = pipe; }
                    pipe.WaitForConnection();
                    lock (pipeGate) { waiting = null; clients.Add(pipe); }
                    ThreadPool.QueueUserWorkItem(delegate
                    {
                        try
                        {
                            Dictionary<string, object> row = HuskJson.Read(WorkspaceHook.ReadLine(pipe, 5000)) as Dictionary<string, object>;
                            string answer = HuskJson.Write(Permission(row)) + "\n";
                            byte[] bytes = Encoding.UTF8.GetBytes(answer);
                            pipe.Write(bytes, 0, bytes.Length);
                            pipe.Flush();
                        }
                        catch (Exception) { }
                        finally { lock (pipeGate) clients.Remove(pipe); pipe.Dispose(); }
                    });
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
            if (tool != "Bash" && tool != "PowerShell" && tool != "Write" && tool != "Edit" && tool != "MultiEdit" && tool != "NotebookEdit") return;
            lock (chat.Gate)
            {
                string file = UndoFile(chat, turn);
                Dictionary<string, object> manifest = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (tool == "Bash" || tool == "PowerShell") { manifest["commands"] = true; Atomic(file, HuskJson.Write(manifest)); return; }
                if (tool != "Write" && tool != "Edit" && tool != "MultiEdit" && tool != "NotebookEdit") return;
                List<object> paths = new List<object>();
                string path = HuskJson.Text(input, tool == "NotebookEdit" ? "notebook_path" : "file_path");
                if (path.Length > 0) paths.Add(path);
                List<object> edits = HuskJson.List(HuskJson.Get(input, "edits"));
                if (edits != null) foreach (object edit in edits)
                {
                    string other = HuskJson.Text(edit as Dictionary<string, object>, "file_path");
                    if (other.Length > 0) paths.Add(other);
                }
                if (paths.Count == 0) throw new IOException("The tool didn't name a file.");
                List<object> files = HuskJson.List(HuskJson.Get(manifest, "files"));
                if (files == null) throw new FormatException("The undo list is unreadable.");
                foreach (string target in paths)
                {
                    string original = Path.GetFullPath(Path.IsPathRooted(target) ? target : Path.Combine(cwd, target));
                    bool found = false;
                    foreach (object entry in files) if (string.Equals(HuskJson.Text(entry as Dictionary<string, object>, "path"), original, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
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

        internal bool CanUndo(WorkspaceChat chat)
        {
            lock (chat.Gate) return chat.Live == null && LastUndo(chat) != null;
        }

        private string LastUndo(WorkspaceChat chat)
        {
            for (int i = chat.Turns.Count - 1; i >= 0; i--)
            {
                string file = UndoFile(chat, chat.Turns[i]);
                if (!File.Exists(file)) continue;
                Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                if (!true.Equals(HuskJson.Get(row, "restored"))) return chat.Turns[i];
            }
            return null;
        }

        internal string Undo(WorkspaceChat chat)
        {
            lock (chat.Gate)
            {
                if (chat.Live != null) return "Stop the turn before undoing it.";
                string turn = LastUndo(chat);
                if (turn == null) return "No turn to undo.";
                string file = UndoFile(chat, turn);
                Dictionary<string, object> row = HuskJson.Read(File.ReadAllText(file, Encoding.UTF8)) as Dictionary<string, object>;
                List<object> files = HuskJson.List(HuskJson.Get(row, "files"));
                List<string> restored = new List<string>(), removed = new List<string>();
                foreach (object entry in files)
                {
                    Dictionary<string, object> saved = entry as Dictionary<string, object>;
                    string path = HuskJson.Text(saved, "path");
                    if (true.Equals(HuskJson.Get(saved, "created"))) { if (File.Exists(path)) File.Delete(path); removed.Add(path); }
                    else
                    {
                        string backup = HuskJson.Text(saved, "backup");
                        if (!Regex.IsMatch(backup, @"\A[0-9]{4}\.bin\z")) throw new IOException("The undo copy is unreadable.");
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.Copy(Path.Combine(Path.GetDirectoryName(file), backup), path, true);
                        restored.Add(path);
                    }
                }
                row["restored"] = true;
                Atomic(file, HuskJson.Write(row));
                string text = (restored.Count > 0 ? "Restored: " + string.Join(", ", restored.ToArray()) + ". " : "")
                    + (removed.Count > 0 ? "Removed: " + string.Join(", ", removed.ToArray()) + ". " : "");
                if (text.Length == 0) text = "No saved file edits in that turn. ";
                text += "Commands aren't undone.";
                Add(chat, new WorkspaceItem { Kind = "undo", Text = text, Turn = turn });
                return text;
            }
        }

        public void Dispose()
        {
            if (closed) return;
            closed = true;
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

    internal static class WorkspaceHook
    {
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

        internal static Dictionary<string, object> Exchange(string pipe, string chat, string turn, Dictionary<string, object> input, int wait)
        {
            try
            {
                if (!Regex.IsMatch(pipe ?? "", @"\ACapriWorkspace-[A-Za-z0-9-]+\z") || !Workspace.Key(chat) || !Workspace.Key(turn) || input == null)
                    return Decision(false, "Workspace isn't connected.");
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", pipe, PipeDirection.InOut, PipeOptions.Asynchronous))
                {
                    client.Connect(1500);
                    byte[] bytes = Encoding.UTF8.GetBytes(HuskJson.Write(HuskJson.Map("chat", chat, "turn", turn, "tool_name", HuskJson.Text(input, "tool_name"),
                        "tool_input", HuskJson.Get(input, "tool_input"), "cwd", HuskJson.Text(input, "cwd"))) + "\n");
                    client.Write(bytes, 0, bytes.Length);
                    client.Flush();
                    Dictionary<string, object> answer = HuskJson.Read(ReadLine(client, wait)) as Dictionary<string, object>;
                    Dictionary<string, object> output = HuskJson.Get(answer, "hookSpecificOutput") as Dictionary<string, object>;
                    string choice = HuskJson.Text(output, "permissionDecision");
                    if (HuskJson.Text(output, "hookEventName") != "PreToolUse" || (choice != "allow" && choice != "deny")) return Decision(false, "Workspace sent no answer.");
                    return answer;
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
                        while ((count = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
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
