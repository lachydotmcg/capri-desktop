using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class ClaudePlanBrain : IBrainProvider
    {
        internal const string Kind = "claudecode";
        internal const int Wait = 120000;
        private const int Most = 4 * 1024 * 1024;
        internal static string ClaudeOverride, FolderOverride;
        private static readonly string[] Newer = { "--safe-mode", "--disable-slash-commands" };
        private static readonly HashSet<string> dropped = new HashSet<string>(StringComparer.Ordinal);
        private readonly string model;
        private readonly object gate = new object();
        private readonly SemaphoreSlim serial = new SemaphoreSlim(1, 1);
        private Process running;
        private int generation;
        public int Generation { get { return Volatile.Read(ref generation); } }
        public string Identity { get { return "claude-plan:" + model; } }

        internal ClaudePlanBrain(string model) { this.model = model; }

        internal static void Retry() { lock (dropped) dropped.Clear(); }

        internal static string Find(string home = null)
        {
            if (!string.IsNullOrEmpty(ClaudeOverride)) return File.Exists(ClaudeOverride) ? ClaudeOverride : null;
            home ??= Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] spots = { Path.Combine(home, ".local", "bin", "claude"), Path.Combine(home, ".claude", "local", "claude"), ArchApps.Executable("claude"), "/opt/homebrew/bin/claude", "/usr/local/bin/claude", Path.Combine(home, ".npm-global", "bin", "claude"), "/usr/bin/claude" };
            foreach (string spot in spots) if (spot != null && Runs(spot)) return spot;
            return null;
        }

        private static bool Runs(string path)
        {
            try { return File.Exists(path) && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        internal static void Check()
        {
            new ClaudePlanBrain("haiku").Complete(null, new List<ChatMessage> { new ChatMessage("system", "Reply with OK and nothing else."), new ChatMessage("user", "OK?") }, null, null, 0, null, "chat");
        }

        public string Complete(string unusedModel, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose, string parts = null)
        {
            int ticket = Generation;
            serial.Wait();
            try
            {
                if (Generation != ticket) throw new OperationCanceledException();
                string app = Find();
                if (app == null) throw new BrainException(Plans.Missing(Kind), true);
                List<KeyValuePair<string, string>> pictures = new List<KeyValuePair<string, string>>();
                string prompt = Prompt(messages, parts, out string system, pictures);
                if (tools != null) system += PlanBrain.Proposal + tools;
                string said = Think(app, system, Streamed(prompt, pictures), ticket, string.Join(" ", pictures.Select(picture => picture.Value)));
                if (tools == null) return said;
                string answer = Unfenced(said);
                if (!answer.StartsWith("{", StringComparison.Ordinal)) return said;
                try { return PlanBrain.Proposals(answer, tools, calls); }
                catch (JsonException) { throw new BrainException("The model returned an invalid action proposal."); }
            }
            finally
            {
                lock (gate) { if (running != null) { Bridge.Kill(running); running = null; } }
                serial.Release();
            }
        }

        private string Think(string app, string system, string input, int ticket, string images)
        {
            string run = Path.Combine(FolderOverride ?? Path.Combine(HuskHook.Data, "plan"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            try
            {
                string rules = Path.Combine(run, "system.txt");
                File.WriteAllText(rules, system, new UTF8Encoding(false));
                List<string> args = new List<string> { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json", "--tools", "", "--strict-mcp-config",
                    "--no-session-persistence", "--setting-sources", "project", "--system-prompt", "You are Capri.", "--append-system-prompt-file", rules };
                if (!string.IsNullOrEmpty(model)) { args.Add("--model"); args.Add(model); }
                Ran claude;
                while (true)
                {
                    List<string> asked = new List<string>(args);
                    lock (dropped) foreach (string flag in Newer) if (!dropped.Contains(flag)) asked.Add(flag);
                    claude = Start(app, asked, input, run, ticket);
                    Match unknown = Regex.Match(claude.Errors + claude.Output, @"unknown option '(--[a-z-]+)'");
                    if (!unknown.Success || Array.IndexOf(Newer, unknown.Groups[1].Value) < 0) break;
                    lock (dropped) if (!dropped.Add(unknown.Groups[1].Value)) break;
                }
                string said = null;
                bool failed = false;
                foreach (string line in claude.Output.Split('\n'))
                {
                    if (line.Trim().Length == 0) continue;
                    try
                    {
                        using JsonDocument row = JsonDocument.Parse(line);
                        JsonElement root = row.RootElement;
                        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String || type.GetString() != "result") continue;
                        said = root.TryGetProperty("result", out JsonElement result) && result.ValueKind == JsonValueKind.String ? result.GetString() : "";
                        failed = root.TryGetProperty("is_error", out JsonElement error) && error.ValueKind == JsonValueKind.True;
                    }
                    catch (JsonException) { }
                }
                if (said == null || failed || said.Trim().Length == 0) throw Problem(Brain.RedactedImages((said ?? "") + "\n" + claude.Errors, images));
                return said.Trim();
            }
            finally
            {
                try { Directory.Delete(run, true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        internal static string Prompt(List<ChatMessage> messages, string parts, out string system, List<KeyValuePair<string, string>> pictures)
        {
            StringBuilder rules = new StringBuilder();
            List<KeyValuePair<string, string>> chat = new List<KeyValuePair<string, string>>();
            foreach (ChatMessage message in messages)
            {
                if (message == null) continue;
                if (message.Role == "system") { if (rules.Length > 0) rules.Append("\n\n"); rules.Append(message.Content); }
                else chat.Add(new KeyValuePair<string, string>(message.Role, Words(message, parts, pictures)));
            }
            system = rules.ToString();
            if (chat.Count == 1 && chat[0].Key == "user") return chat[0].Value.TrimStart().StartsWith("/", StringComparison.Ordinal) ? "Them: " + chat[0].Value : chat[0].Value;
            StringBuilder transcript = new StringBuilder("The chat so far, oldest first. Write only Capri's next message.\n");
            foreach (KeyValuePair<string, string> said in chat) transcript.Append('\n').Append(said.Key == "assistant" ? "Capri: " : "Them: ").Append(said.Value);
            return transcript.ToString();
        }

        private static string Words(ChatMessage message, string parts, List<KeyValuePair<string, string>> pictures)
        {
            if (message.Images != null) foreach (string image in message.Images) pictures.Add(new KeyValuePair<string, string>(message.ImageMime ?? "image/jpeg", image));
            if (message.Content != "@capri-image@") return message.Content ?? "";
            if (parts == null) throw new BrainException("Image content is missing.");
            StringBuilder text = new StringBuilder();
            using JsonDocument images = JsonDocument.Parse(parts);
            foreach (JsonElement part in images.RootElement.EnumerateArray())
            {
                string type = part.GetProperty("type").GetString();
                if (type == "text") { if (text.Length > 0) text.Append('\n'); text.Append(part.GetProperty("text").GetString()); continue; }
                string url = type == "image_url" ? part.GetProperty("image_url").GetProperty("url").GetString()
                    : type == "image" ? "data:" + part.GetProperty("source").GetProperty("media_type").GetString() + ";base64," + part.GetProperty("source").GetProperty("data").GetString() : null;
                string mime = url == null ? null : url.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) ? "image/jpeg" : url.StartsWith("data:image/png;base64,", StringComparison.Ordinal) ? "image/png" : null;
                if (mime == null) throw new BrainException("Only captured image bytes are supported.");
                pictures.Add(new KeyValuePair<string, string>(mime, url.Substring(url.IndexOf(',') + 1)));
            }
            return text.ToString();
        }

        internal static string Streamed(string prompt, List<KeyValuePair<string, string>> pictures)
        {
            List<object> blocks = new List<object>();
            foreach (KeyValuePair<string, string> picture in pictures) blocks.Add(new { type = "image", source = new { type = "base64", media_type = picture.Key, data = picture.Value } });
            blocks.Add(new { type = "text", text = prompt });
            return JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = blocks } }) + "\n";
        }

        internal static string Unfenced(string said)
        {
            Match fenced = Regex.Match(said, @"\A\s*```(?:json)?[ \t]*\r?\n(.*?)\r?\n?[ \t]*```\s*\z", RegexOptions.Singleline);
            return (fenced.Success ? fenced.Groups[1].Value : said).Trim();
        }

        internal enum Failure { Other, SignIn, Limit }

        internal static Failure ProblemKind(string output)
        {
            string text = Regex.Replace(output ?? "", @"\s+", " ").Trim();
            if (Regex.IsMatch(text, @"not (?:logged|signed) in|log ?in|sign ?in|authenticat|oauth|unauthori[sz]ed|invalid api key|\b401\b|credential", RegexOptions.IgnoreCase))
                return Failure.SignIn;
            if (Regex.IsMatch(text, @"usage limit|limit reached|hit your limit|rate.?limit|quota|too many requests|\b429\b|limit will reset|resets? at|\breset(?:s|ting)?\s+\d", RegexOptions.IgnoreCase))
                return Failure.Limit;
            return Failure.Other;
        }

        internal static BrainException Problem(string output)
        {
            string text = Regex.Replace(Brain.Redacted(Brain.Scrubbed(output)), @"\s+", " ").Trim();
            string app = Plans.App(Kind);
            Failure failure = ProblemKind(text);
            if (failure == Failure.SignIn) return new BrainException("Claude Code isn't signed in. Run claude and sign in.", true);
            if (failure == Failure.Limit) return new BrainException("Your " + Plans.Plan(Kind) + " is out of messages for now.", true) { Detail = app + " says: " + Short(text) };
            return new BrainException(app + " couldn't answer" + (text.Length == 0 ? "." : ": " + Short(text)), true);
        }

        private static string Short(string text) { return text.Length > 160 ? text.Substring(0, 157) + "..." : text; }

        private sealed class Ran
        {
            internal int Code;
            internal string Output = "", Errors = "";
        }

        private Ran Start(string app, List<string> args, string input, string folder, int ticket)
        {
            if (Brain.Off) { Brain.TurnAway(Plans.App(Kind)); throw new BrainException(Brain.OffLine, true); }
            ProcessStartInfo start = ComputerProcess.Start(app, args.ToArray());
            start.WorkingDirectory = folder;
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
            start.Environment.Remove("ANTHROPIC_API_KEY");
            start.Environment.Remove("ANTHROPIC_AUTH_TOKEN");
            start.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
            start.Environment["NO_COLOR"] = "1";
            StringBuilder output = new StringBuilder(), errors = new StringBuilder();
            using Process process = new Process { StartInfo = start };
            process.OutputDataReceived += (_, e) => { if (e.Data != null) lock (output) if (output.Length < Most) output.Append(e.Data).Append('\n'); };
            process.ErrorDataReceived += (_, e) => { if (e.Data != null) lock (errors) if (errors.Length < Most) errors.Append(e.Data).Append('\n'); };
            try { process.Start(); }
            catch (System.ComponentModel.Win32Exception error) { throw new BrainException(Plans.App(Kind) + " wouldn't start: " + error.Message, true); }
            try
            {
                lock (gate)
                {
                    if (ticket != generation) { Bridge.Kill(process); throw new OperationCanceledException(); }
                    running = process;
                }
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                try
                {
                    using (StreamWriter writer = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))) writer.Write(input);
                }
                catch (IOException) { }
                Stopwatch waited = Stopwatch.StartNew();
                while (!process.WaitForExit(100))
                {
                    bool stop = Generation != ticket;
                    if (!stop && waited.ElapsedMilliseconds < Wait) continue;
                    Bridge.Kill(process);
                    if (stop) throw new OperationCanceledException();
                    throw new BrainException(Plans.App(Kind) + " took more than " + Wait / 1000 + " seconds to answer. Try again, or pick a faster model under BRAIN.", true);
                }
                process.WaitForExit();
                if (Generation != ticket) throw new OperationCanceledException();
                Ran ran = new Ran { Code = process.ExitCode };
                lock (output) ran.Output = output.ToString();
                lock (errors) ran.Errors = errors.ToString();
                return ran;
            }
            finally { lock (gate) running = null; }
        }

        public void Cancel()
        {
            lock (gate) { Interlocked.Increment(ref generation); if (running != null) Bridge.Kill(running); }
        }
    }
}
