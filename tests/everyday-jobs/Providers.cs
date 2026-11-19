using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using CapriDesktop;

namespace CapriEverydayJobs
{
    internal sealed class ScriptedBrain : IBrainProvider
    {
        private readonly Job job;
        private readonly string list;
        private readonly Outcome run;
        private bool acted;

        internal ScriptedBrain(Job job, string list, Outcome run) { this.job = job; this.list = list; this.run = run; }

        public string Identity { get { return "scripted"; } }
        public int Generation { get { return 0; } }
        public void Cancel() { }

        public string Complete(string model, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose, string parts = null)
        {
            run.Thinks++;
            string[] ideal = job.Ideal;
            string reply = ideal[ideal.Length - 1];
            if (tools == null || acted || ideal.Length == 1) return reply;
            acted = true;
            for (int i = 0; i < ideal.Length - 1; i++)
            {
                int space = ideal[i].IndexOf(' ');
                string json = ideal[i].Substring(space + 1).Replace("@list", list.Replace("\\", "\\\\"));
                calls.Add(new KeyValuePair<string, ToolArguments>(ideal[i].Substring(0, space), Hands.Read(json)));
            }
            return "";
        }
    }

    internal sealed class ClaudePlan : IBrainProvider
    {
        internal static bool Trace;
        private readonly string app, model, folder;

        private static string Flat(string text, int most) { text = (text ?? "").Replace("\r", " ").Replace("\n", " ⏎ "); return text.Length > most ? text.Substring(0, most) + "…" : text; }
        private readonly Outcome run;

        internal ClaudePlan(string app, string model, string folder, Outcome run) { this.app = app; this.model = model; this.folder = folder; this.run = run; }

        public string Identity { get { return "claude-plan"; } }
        public int Generation { get { return 0; } }
        public void Cancel() { }

        public string Complete(string unused, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose, string parts = null)
        {
            run.Thinks++;
            StringBuilder rules = new StringBuilder();
            List<ChatMessage> chat = new List<ChatMessage>();
            foreach (ChatMessage message in messages)
            {
                if (message == null) continue;
                if (message.Role == "system") { if (rules.Length > 0) rules.Append("\n\n"); rules.Append(message.Content); }
                else chat.Add(message);
            }
            string asked;
            if (chat.Count == 1 && chat[0].Role == "user") asked = chat[0].Content ?? "";
            else
            {
                StringBuilder transcript = new StringBuilder("The chat so far, oldest first. Write only Capri's next message.\n");
                foreach (ChatMessage message in chat) transcript.Append('\n').Append(message.Role == "assistant" ? "Capri: " : "Them: ").Append(message.Content ?? "");
                asked = transcript.ToString();
            }
            string run_ = Path.Combine(folder, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(run_);
            try
            {
                string system = Path.Combine(run_, "system.txt");
                File.WriteAllText(system, rules.ToString(), new UTF8Encoding(false));
                ProcessStartInfo start = new ProcessStartInfo(app) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = run_, StandardOutputEncoding = Encoding.UTF8 };
                foreach (string arg in new[] { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json", "--tools", "", "--strict-mcp-config",
                    "--no-session-persistence", "--setting-sources", "project", "--system-prompt", "You are Capri.", "--append-system-prompt-file", system, "--model", model }) start.ArgumentList.Add(arg);
                start.Environment.Remove("ANTHROPIC_API_KEY");
                start.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
                start.Environment["NO_COLOR"] = "1";
                string input = JsonSerializer.Serialize(new { type = "user", message = new { role = "user", content = new object[] { new { type = "text", text = asked } } } }) + "\n";
                using (Process process = Process.Start(start))
                {
                    var errors = process.StandardError.ReadToEndAsync();
                    process.StandardInput.Write(input);
                    process.StandardInput.Close();
                    string output = process.StandardOutput.ReadToEnd();
                    if (!process.WaitForExit(180000)) { try { process.Kill(true); } catch (InvalidOperationException) { } throw new BrainException("Claude Code took too long.", true); }
                    string said = null;
                    bool failed = false;
                    foreach (string line in output.Split('\n'))
                    {
                        if (!line.TrimStart().StartsWith("{", StringComparison.Ordinal)) continue;
                        try
                        {
                            using (JsonDocument row = JsonDocument.Parse(line))
                            {
                                JsonElement root = row.RootElement, type, result, error, cost;
                                if (!root.TryGetProperty("type", out type) || type.GetString() != "result") continue;
                                said = root.TryGetProperty("result", out result) && result.ValueKind == JsonValueKind.String ? result.GetString() : "";
                                failed = root.TryGetProperty("is_error", out error) && error.ValueKind == JsonValueKind.True;
                                if (root.TryGetProperty("total_cost_usd", out cost) && cost.ValueKind == JsonValueKind.Number) run.Cost += cost.GetDouble();
                            }
                        }
                        catch (JsonException) { }
                    }
                    if (Trace) Console.Error.WriteLine("[think " + purpose + (tools == null ? "" : " +tools") + "] " + (chat.Count == 0 ? "" : Flat(chat[chat.Count - 1].Content, 300)) + "\n   => " + Flat(said, 300));
                    if (said == null || failed || said.Trim().Length == 0) throw new BrainException("Claude Code couldn't answer: " + (said ?? errors.Result).Trim(), true);
                    return said.Trim();
                }
            }
            finally
            {
                try { Directory.Delete(run_, true); }
                catch (IOException) { }
            }
        }
    }
}
