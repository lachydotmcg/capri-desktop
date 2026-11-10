using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class PlanBrain : IBrainProvider
    {
        private readonly string python, accounts, model, visionModel, effort, visionEffort;
        private readonly int? tokens, visionTokens;
        private readonly bool ownsAccount;
        private readonly object gate = new object();
        private readonly SemaphoreSlim serial = new SemaphoreSlim(1, 1);
        private Process running;
        private int generation;
        public int Generation { get { return Volatile.Read(ref generation); } }
        public string Identity { get { return "chatgpt-plan:" + Account(); } }

        internal PlanBrain(string python, string accounts, string model, string visionModel, string effort, string visionEffort, int? tokens, int? visionTokens, bool ownsAccount = false)
        {
            this.python = python; this.accounts = accounts; this.model = model; this.visionModel = visionModel; this.effort = effort; this.visionEffort = visionEffort; this.ownsAccount = ownsAccount; this.tokens = tokens; this.visionTokens = visionTokens;
        }

        private string Account()
        {
            string path = Path.Combine(accounts, "active.json");
            if (new FileInfo(path).LinkTarget != null || (File.GetUnixFileMode(path) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
                throw new BrainException("The account reference must be private and cannot be a link.");
            using JsonDocument record = JsonDocument.Parse(File.ReadAllText(path));
            string id = record.RootElement.GetProperty("account").GetString();
            if (id == null || id.Length != 64 || id.Any(c => !"0123456789abcdef".Contains(c))) throw new BrainException("Invalid account reference.");
            return id;
        }

        public string Complete(string unusedModel, List<ChatMessage> messages, string tools, List<KeyValuePair<string, ToolArguments>> calls, int limit, Action<string> streamed, string purpose, string parts = null)
        {
            int ticket = Generation;
            serial.Wait();
            try
            {
                if (Generation != ticket) throw new OperationCanceledException();
                if (ownsAccount) NativeAccount.Run(python, accounts, false, model, null,
                    child => { lock (gate) { running = child; if (ticket != generation) Bridge.Kill(child); } }, () => ticket == Generation);
                lock (gate) running = null;
                string account = Account();
                List<object> input = new List<object>();
                StringBuilder instructions = new StringBuilder();
                foreach (ChatMessage message in messages)
                {
                    if (message.Role == "system") { instructions.AppendLine(message.Content); continue; }
                    List<object> content = new List<object> { new { type = message.Role == "assistant" ? "output_text" : "input_text", text = message.Content ?? "" } };
                    if (message.Content == "@capri-image@")
                    {
                        if (parts == null) throw new BrainException("Image content is missing.");
                        content.Clear();
                        using JsonDocument images = JsonDocument.Parse(parts);
                        foreach (JsonElement part in images.RootElement.EnumerateArray())
                        {
                            string type = part.GetProperty("type").GetString();
                            if (type == "text") content.Add(new { type = "input_text", text = part.GetProperty("text").GetString() });
                            else
                            {
                                string url = type == "image_url" ? part.GetProperty("image_url").GetProperty("url").GetString()
                                    : type == "image" ? "data:" + part.GetProperty("source").GetProperty("media_type").GetString() + ";base64," + part.GetProperty("source").GetProperty("data").GetString() : null;
                                if (url == null || !(url.StartsWith("data:image/jpeg;base64,", StringComparison.Ordinal) || url.StartsWith("data:image/png;base64,", StringComparison.Ordinal))) throw new BrainException("Only captured image bytes are supported.");
                                content.Add(new { type = "input_image", image_url = url, detail = "original" });
                            }
                        }
                    }
                    if (message.Images != null) foreach (string image in message.Images)
                        content.Add(new { type = "input_image", image_url = "data:image/jpeg;base64," + image, detail = "original" });
                    input.Add(new { role = message.Role == "assistant" ? "assistant" : "user", content });
                }
                if (tools != null)
                    instructions.Append("\nReturn only a JSON object with text (string) and calls (array, at most 4). Each call has name and arguments conforming to exactly one available function. Use calls=[] for no action. Never claim execution; these are proposals checked by the native app. The function catalog is: ").Append(tools);
                using Process child = Bridge.Start(python, "plan_bridge.py", "plan", accounts);
                lock (gate)
                {
                    if (ticket != generation) { Bridge.Kill(child); throw new OperationCanceledException(); }
                    running = child;
                }
                string selected = (purpose == "vision" || purpose == "computer") && !string.IsNullOrEmpty(visionModel) ? visionModel : model;
                using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
                using CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(child));
                child.StandardInput.WriteLine(JsonSerializer.Serialize(new { account_directory = accounts, account, model = selected, effort = (purpose == "vision" || purpose == "computer") ? visionEffort : effort, limit = (purpose == "vision" || purpose == "computer") ? visionTokens ?? Math.Max(1024, limit) : tokens ?? Math.Max(512, limit), messages = input, instructions = instructions.ToString(), format = purpose == "computer" ? "computer_action" : null }));
                child.StandardInput.Close();
                string answer = null;
                StringBuilder partial = new StringBuilder();

                while (true)
                {
                    string line = Bridge.ReadLine(child.StandardOutput, 256000);
                    if (line == null) break;
                    if (line.Length > 256000 || Generation != ticket || Account() != account) { Bridge.Kill(child); throw new OperationCanceledException(); }
                    using JsonDocument item = JsonDocument.Parse(line);
                    string kind = item.RootElement.GetProperty("event").GetString();
                    if (kind == "error") throw new BrainException(item.RootElement.GetProperty("message").GetString());
                    if (kind == "delta")
                    {
                        partial.Append(item.RootElement.GetProperty("text").GetString());
                        if (partial.Length > 64000) throw new BrainException("The model response exceeded its text limit.");
                        if (tools == null && streamed != null) streamed(partial.ToString());
                    }
                    else if (kind == "done") answer = item.RootElement.GetProperty("text").GetString();
                    else throw new BrainException("Unexpected account bridge response.");
                }
                child.WaitForExit();
                if (timeout.IsCancellationRequested || Generation != ticket || Account() != account) throw new OperationCanceledException();
                if (child.ExitCode != 0 || answer == null || answer != partial.ToString()) throw new BrainException("The model response did not finish; no action was executed.");
                return tools == null ? answer : Proposals(answer, tools, calls);
            }
            finally
            {
                lock (gate) { if (running != null) { Bridge.Kill(running); running = null; } }
                serial.Release();
            }
        }

        internal static string Proposals(string answer, string catalog, List<KeyValuePair<string, ToolArguments>> calls)
        {
            using JsonDocument schema = JsonDocument.Parse(catalog);
            using JsonDocument reply = JsonDocument.Parse(answer);
            JsonElement root = reply.RootElement;
            if (root.ValueKind != JsonValueKind.Object || root.EnumerateObject().Count() != 2 || !root.TryGetProperty("text", out JsonElement text) || text.ValueKind != JsonValueKind.String
                || !root.TryGetProperty("calls", out JsonElement proposed) || proposed.ValueKind != JsonValueKind.Array || proposed.GetArrayLength() > 4)
                throw new BrainException("The model returned an invalid action proposal.");
            List<KeyValuePair<string, ToolArguments>> checkedCalls = new List<KeyValuePair<string, ToolArguments>>();
            foreach (JsonElement call in proposed.EnumerateArray())
            {
                if (call.ValueKind != JsonValueKind.Object || call.EnumerateObject().Count() != 2) throw new BrainException("Invalid action fields.");
                string name = call.GetProperty("name").GetString();
                JsonElement function = schema.RootElement.EnumerateArray().Select(item => item.GetProperty("function")).FirstOrDefault(item => item.GetProperty("name").GetString() == name);
                if (function.ValueKind == JsonValueKind.Undefined) throw new BrainException("The proposed action is not enabled.");
                JsonElement args = call.GetProperty("arguments");
                Validate(args, function.GetProperty("parameters"), 0);
                using MemoryStream encoded = new MemoryStream(Encoding.UTF8.GetBytes(args.GetRawText()));
                checkedCalls.Add(new KeyValuePair<string, ToolArguments>(name, (ToolArguments)new DataContractJsonSerializer(typeof(ToolArguments)).ReadObject(encoded)));
            }
            calls.AddRange(checkedCalls);
            return text.GetString();
        }

        private static void Validate(JsonElement value, JsonElement schema, int depth)
        {
            if (depth > 8) throw new BrainException("Action nesting exceeded its limit.");
            string type = schema.TryGetProperty("type", out JsonElement typed) ? typed.GetString() : null;
            bool valid = type == "object" ? value.ValueKind == JsonValueKind.Object : type == "array" ? value.ValueKind == JsonValueKind.Array : type == "string" ? value.ValueKind == JsonValueKind.String
                : type == "boolean" ? value.ValueKind == JsonValueKind.True || value.ValueKind == JsonValueKind.False : type == "number" ? value.ValueKind == JsonValueKind.Number : type == "integer" && value.TryGetInt64(out _);
            if (!valid) throw new BrainException("An action argument has the wrong type.");
            if (schema.TryGetProperty("enum", out JsonElement options) && !options.EnumerateArray().Any(item => item.GetRawText() == value.GetRawText())) throw new BrainException("An action option is not allowed.");
            if (type == "string" && value.GetString().Length > 16000) throw new BrainException("An action string is too long.");
            if (type == "object")
            {
                JsonElement properties = schema.GetProperty("properties");
                HashSet<string> seen = new HashSet<string>();
                foreach (JsonProperty field in value.EnumerateObject())
                {
                    if (!seen.Add(field.Name) || !properties.TryGetProperty(field.Name, out JsonElement rule)) throw new BrainException("An action contains unknown or duplicate arguments.");
                    Validate(field.Value, rule, depth + 1);
                }
                if (schema.TryGetProperty("required", out JsonElement required)) foreach (JsonElement field in required.EnumerateArray())
                    if (!seen.Contains(field.GetString())) throw new BrainException("An action omitted a required argument.");
            }
            if (type == "array")
            {
                if (value.GetArrayLength() > 128) throw new BrainException("An action array is too long.");
                foreach (JsonElement item in value.EnumerateArray()) Validate(item, schema.GetProperty("items"), depth + 1);
            }
            if (value.ValueKind == JsonValueKind.Number)
            {
                double number = value.GetDouble();
                if (!double.IsFinite(number) || schema.TryGetProperty("minimum", out JsonElement min) && number < min.GetDouble() || schema.TryGetProperty("maximum", out JsonElement max) && number > max.GetDouble()) throw new BrainException("An action number is outside its bounds.");
            }
        }

        public void Cancel()
        {
            lock (gate) { Interlocked.Increment(ref generation); if (running != null) Bridge.Kill(running); }
        }
    }
}
