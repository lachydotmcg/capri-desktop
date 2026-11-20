using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using CapriDesktop;

namespace CapriEverydayJobs
{
    internal static class Report
    {
        private static readonly JsonSerializerOptions Pretty = new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        internal static string Args(ToolArguments args)
        {
            if (args == null) return "{}";
            JsonObject shown = new JsonObject();
            foreach (FieldInfo field in typeof(ToolArguments).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                DataMemberAttribute member = field.GetCustomAttribute<DataMemberAttribute>();
                if (member == null) continue;
                object value = field.GetValue(args);
                if (value == null) continue;
                shown[member.Name ?? field.Name] = JsonValue.Create(value as string ?? Convert.ToString(value, CultureInfo.InvariantCulture));
            }
            return shown.ToJsonString(new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }

        internal static JsonObject Brain(string label, string kind, string model, List<KeyValuePair<Job, List<Outcome>>> results)
        {
            JsonArray jobs = new JsonArray();
            int passed = 0, total = 0;
            double cost = 0;
            foreach (KeyValuePair<Job, List<Outcome>> pair in results)
            {
                JsonArray runs = new JsonArray();
                foreach (Outcome run in pair.Value)
                {
                    total++;
                    if (run.Pass) passed++;
                    cost += run.Cost;
                    JsonArray calls = new JsonArray();
                    foreach (Call call in run.Calls) calls.Add(call.Shown);
                    runs.Add(new JsonObject
                    {
                        ["pass"] = run.Pass, ["why"] = run.Why, ["seconds"] = Math.Round(run.Seconds, 1), ["thinks"] = run.Thinks,
                        ["calls"] = calls, ["reply"] = run.Reply, ["error"] = run.Error, ["cost"] = run.Cost > 0 ? Math.Round(run.Cost, 5) : (double?)null
                    });
                }
                jobs.Add(new JsonObject { ["id"] = pair.Key.Id, ["area"] = pair.Key.Area, ["goal"] = pair.Key.Goal, ["ask"] = pair.Key.Ask, ["runs"] = runs });
            }
            return new JsonObject
            {
                ["suite"] = "everyday-jobs", ["format"] = 1,
                ["commit"] = Environment.GetEnvironmentVariable("GITHUB_SHA"),
                ["when"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                ["brain"] = new JsonObject { ["label"] = label, ["kind"] = kind, ["model"] = model },
                ["passed"] = passed, ["total"] = total, ["cost"] = cost > 0 ? Math.Round(cost, 4) : (double?)null,
                ["jobs"] = jobs
            };
        }

        internal static void Save(JsonObject result, string path)
        {
            string folder = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, result.ToJsonString(Pretty) + "\n", new UTF8Encoding(false));
        }

        private static string Cell(string text) { return (text ?? "").Replace("|", "\\|").Replace("\r", " ").Replace("\n", " "); }

        private static string Short(string text, int most) { text = Cell(text).Trim(); return text.Length > most ? text.Substring(0, most - 1) + "…" : text; }

        internal static string Table(List<JsonObject> brains)
        {
            StringBuilder md = new StringBuilder();
            md.Append("| Job | Area |");
            foreach (JsonObject brain in brains) md.Append(' ').Append(Cell((string)brain["brain"]["label"])).Append(" |");
            md.Append("\n|---|---|");
            foreach (JsonObject brain in brains) md.Append(":---:|");
            md.Append('\n');
            List<string> order = new List<string>();
            Dictionary<string, string> areas = new Dictionary<string, string>(), goals = new Dictionary<string, string>();
            foreach (JsonObject brain in brains)
                foreach (JsonNode job in (JsonArray)brain["jobs"])
                {
                    string id = (string)job["id"];
                    if (order.Contains(id)) continue;
                    order.Add(id);
                    areas[id] = (string)job["area"];
                    goals[id] = (string)job["goal"];
                }
            foreach (string id in order)
            {
                md.Append("| ").Append(Cell(id)).Append(" | ").Append(Cell(areas[id])).Append(" |");
                foreach (JsonObject brain in brains)
                {
                    JsonNode job = ((JsonArray)brain["jobs"]).FirstOrDefault(node => (string)node["id"] == id);
                    if (job == null) { md.Append(" – |"); continue; }
                    JsonArray runs = (JsonArray)job["runs"];
                    int good = runs.Count(run => (bool)run["pass"]);
                    md.Append(' ').Append(runs.Count == 1 ? good == 1 ? "✅" : "❌" : good == runs.Count ? "✅ " + good + "/" + runs.Count : good == 0 ? "❌ 0/" + runs.Count : "⚠️ " + good + "/" + runs.Count).Append(" |");
                }
                md.Append('\n');
            }
            md.Append("| **Pass rate** | |");
            foreach (JsonObject brain in brains)
            {
                int passed = (int)brain["passed"], total = (int)brain["total"];
                md.Append(" **").Append(total == 0 ? "–" : Math.Round(100.0 * passed / total) + "%").Append("** (").Append(passed).Append('/').Append(total).Append(") |");
            }
            md.Append("\n| Median time per job | |");
            foreach (JsonObject brain in brains)
            {
                List<double> seconds = new List<double>();
                foreach (JsonNode job in (JsonArray)brain["jobs"]) foreach (JsonNode run in (JsonArray)job["runs"]) seconds.Add((double)run["seconds"]);
                seconds.Sort();
                md.Append(' ').Append(seconds.Count == 0 ? "–" : seconds[seconds.Count / 2].ToString("0.0", CultureInfo.InvariantCulture) + " s").Append(" |");
            }
            md.Append('\n');
            if (brains.Exists(brain => brain["cost"] != null))
            {
                md.Append("| Cost of this run | |");
                foreach (JsonObject brain in brains) md.Append(' ').Append(brain["cost"] == null ? "–" : "$" + ((double)brain["cost"]).ToString("0.000", CultureInfo.InvariantCulture)).Append(" |");
                md.Append('\n');
            }
            md.Append("\nModels: ");
            md.Append(string.Join("; ", brains.ConvertAll(brain => Cell((string)brain["brain"]["label"]) + " = " + Cell((string)brain["brain"]["kind"]) + " " + Cell((string)brain["brain"]["model"])).ToArray()));
            md.Append("\n\n### Misses\n\n");
            bool any = false;
            foreach (JsonObject brain in brains)
                foreach (JsonNode job in (JsonArray)brain["jobs"])
                    foreach (JsonNode run in (JsonArray)job["runs"])
                    {
                        if ((bool)run["pass"]) continue;
                        any = true;
                        JsonArray calls = (JsonArray)run["calls"];
                        md.Append("- **").Append(Cell((string)brain["brain"]["label"])).Append("**, ").Append(Cell((string)job["id"])).Append(" (\"").Append(Short((string)job["ask"], 80)).Append("\"): ")
                          .Append(Short((string)run["why"], 160));
                        if (calls.Count > 0) md.Append(". Called ").Append(Short(string.Join(", ", calls.Select(call => (string)call)), 200));
                        if (!string.IsNullOrEmpty((string)run["error"])) md.Append(". Error: ").Append(Short((string)run["error"], 160));
                        else md.Append(". She said: \"").Append(Short((string)run["reply"], 160)).Append('"');
                        md.Append('\n');
                    }
            if (!any) md.Append("None.\n");
            return md.ToString();
        }

        internal static JsonObject Load(string path) { return (JsonObject)JsonNode.Parse(File.ReadAllText(path)); }
    }
}
