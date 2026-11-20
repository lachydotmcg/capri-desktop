using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class BetterModels
    {
        internal const int MaxCatalog = 4 * 1024 * 1024;
        internal const string ClaudeQuery = "{\"type\":\"control_request\",\"request_id\":\"capri-models\",\"request\":{\"subtype\":\"initialize\",\"hooks\":{},\"sdkMcpServers\":[]}}\n";
        internal const string CatalogError = "xAI sent an unreadable language model catalog. Try Refresh.";
        internal const string XaiUrl = "https://api.x.ai/v1";

        internal static bool XaiKey(string key)
        {
            return key != null && Regex.IsMatch(key, @"\Axai-[A-Za-z0-9_-]+\z");
        }

        internal static string XaiAddress(string path) { return XaiUrl + (path == "/models" ? "/language-models" : path); }

        internal static string RedactXai(string text)
        {
            return text == null ? null : Regex.Replace(text, @"\bxai-[A-Za-z0-9_-]+", "[redacted]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        internal static bool PlanId(string id)
        {
            return id != null && Regex.IsMatch(id, @"\A[A-Za-z0-9][A-Za-z0-9._\[\]-]{0,127}\z");
        }

        internal static void Add(List<string> models, string id)
        {
            if (PlanId(id) && !models.Contains(id)) models.Add(id);
        }

        internal static string XaiCatalog(string json)
        {
            XaiPage page = Read<XaiPage>(json);
            if (page == null || page.Models == null) throw new FormatException(CatalogError);
            var found = new List<XaiModel>();
            foreach (XaiModel model in page.Models)
            {
                if (model == null || !PlanId(model.Id) || !model.Id.StartsWith("grok-", StringComparison.Ordinal) ||
                    model.Input == null || Array.IndexOf(model.Input, "text") < 0 ||
                    model.Output == null || Array.IndexOf(model.Output, "text") < 0) continue;
                model.Architecture = new Modalities { Input = model.Input, Output = model.Output };
                found.Add(model);
            }
            using (var stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CloudPage)).WriteObject(stream, new CloudPage { Models = found.ToArray() });
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static List<string> Codex(string path)
        {
            using (var file = File.OpenRead(path))
            {
                if (file.Length > MaxCatalog) return new List<string>();
                using (var reader = new StreamReader(file, Encoding.UTF8)) return CodexJson(ReadBounded(reader));
            }
        }

        private static string ReadBounded(TextReader reader)
        {
            var text = new StringBuilder();
            var buffer = new char[8192];
            int count;
            while ((count = reader.Read(buffer, 0, buffer.Length)) != 0)
            {
                if (text.Length + count > MaxCatalog) throw new FormatException("Model catalog is too large.");
                text.Append(buffer, 0, count);
            }
            return text.ToString();
        }

        internal static List<string> CodexJson(string json)
        {
            var found = new List<string>();
            CodexPage page = Read<CodexPage>(json);
            if (page != null && page.Models != null)
                foreach (CodexModel model in page.Models)
                    if (model != null && model.Visibility == "list") Add(found, model.Id);
            return found;
        }

        internal static List<string> Claude(string jsonLines)
        {
            var found = new List<string>();
            if (jsonLines == null || jsonLines.Length > MaxCatalog) return found;
            using (var lines = new StringReader(jsonLines))
            {
                string line;
                while ((line = lines.ReadLine()) != null)
                {
                    if (line.IndexOf("capri-models", StringComparison.Ordinal) < 0) continue;
                    ClaudeFrame frame;
                    try { frame = Read<ClaudeFrame>(line); }
                    catch (FormatException) { continue; }
                    if (frame == null || frame.Type != "control_response" || frame.Reply == null || frame.Reply.Id != "capri-models") continue;
                    if (frame.Reply.Type == "success" && frame.Reply.Payload != null && frame.Reply.Payload.Models != null)
                        foreach (ClaudeModel model in frame.Reply.Payload.Models)
                            if (model != null) Add(found, model.Id);
                    break;
                }
            }
            return found;
        }

        internal static string CodexCache(string profile)
        {
            string home = Environment.GetEnvironmentVariable("CODEX_HOME");
            return Path.Combine(string.IsNullOrWhiteSpace(home) ? Path.Combine(profile, ".codex") : home, "models_cache.json");
        }

        internal static List<string> ClaudeArgs()
        {
            return new List<string> { "-p", "--verbose", "--input-format", "stream-json", "--output-format", "stream-json",
                "--safe-mode", "--tools", "", "--strict-mcp-config", "--mcp-config", "{\"mcpServers\":{}}",
                "--no-session-persistence", "--setting-sources", "project", "--disable-slash-commands" };
        }

        private static T Read<T>(string json) where T : class
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > MaxCatalog) throw new FormatException("Invalid model catalog.");
            try
            {
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json)))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
            }
            catch (SerializationException error) { throw new FormatException("Invalid model catalog.", error); }
        }

        [DataContract] private sealed class CloudPage { [DataMember(Name = "data")] public XaiModel[] Models { get; set; } }
        [DataContract] private sealed class XaiPage { [DataMember(Name = "models")] public XaiModel[] Models { get; set; } }
        [DataContract] private sealed class XaiModel
        {
            [DataMember(Name = "id")] public string Id { get; set; }
            [DataMember(Name = "created")] public long Created { get; set; }
            [DataMember(Name = "input_modalities")] public string[] Input { get; set; }
            [DataMember(Name = "output_modalities")] public string[] Output { get; set; }
            [DataMember(Name = "architecture")] public Modalities Architecture { get; set; }
        }
        [DataContract] private sealed class Modalities
        {
            [DataMember(Name = "input_modalities")] public string[] Input;
            [DataMember(Name = "output_modalities")] public string[] Output;
        }
        [DataContract] private sealed class CodexPage { [DataMember(Name = "models")] public CodexModel[] Models { get; set; } }
        [DataContract] private sealed class CodexModel
        {
            [DataMember(Name = "slug")] public string Id { get; set; }
            [DataMember(Name = "visibility")] public string Visibility { get; set; }
        }
        [DataContract] private sealed class ClaudeFrame
        {
            [DataMember(Name = "type")] public string Type { get; set; }
            [DataMember(Name = "response")] public ClaudeReply Reply { get; set; }
        }
        [DataContract] private sealed class ClaudeReply
        {
            [DataMember(Name = "subtype")] public string Type { get; set; }
            [DataMember(Name = "request_id")] public string Id { get; set; }
            [DataMember(Name = "response")] public ClaudePage Payload { get; set; }
        }
        [DataContract] private sealed class ClaudePage { [DataMember(Name = "models")] public ClaudeModel[] Models { get; set; } }
        [DataContract] private sealed class ClaudeModel { [DataMember(Name = "value")] public string Id { get; set; } }
    }
}
