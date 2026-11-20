using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    // A tool mod (.capri-tool) is a recipe in JSON: one to three HTTPS GETs to the hosts it lists, the values to keep from each
    // answer, and what to tell her. Capri's own code runs it, so a broken recipe can fail but can't run code or crash her.
    internal sealed class ToolRecipe
    {
        internal string ToolName;
        internal string Takes = "";
        internal string[] Domains = new string[0];
        internal RecipeStep[] Steps = new RecipeStep[0];
        internal string Say;
    }

    internal sealed class RecipeStep
    {
        internal string Get;
        internal List<KeyValuePair<string, string>> Keep = new List<KeyValuePair<string, string>>();
    }

    // Why one run of a recipe stopped. Broken counts towards switching the tool off; a site that's down or a word it doesn't know doesn't.
    internal sealed class RecipeException : Exception
    {
        internal readonly bool Broken;
        internal RecipeException(bool broken, string message) : base(message) { Broken = broken; }
    }

    internal sealed partial class Mods
    {
        internal const string ToolExtension = ".capri-tool";
        internal const int LargestAnswer = 64 * 1024;
        private const int MostSteps = 3, MostDomains = 5, MostKept = 8, MostInput = 500, MostSaid = 2000, StrikesToOff = 3;
        private const int StepWait = 4000, AllWait = 12000;
        private const string StartingFile = "starting.txt";
        private static readonly string[] ToolFields = { "format", "id", "name", "version", "author", "description", "takes", "domains", "steps", "say" };
        private static readonly Regex GoodToolName = new Regex(@"^[a-z][a-z0-9_]{0,39}$");
        private static readonly Regex GoodHost = new Regex(@"^(?=.{1,253}$)[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?(?:\.[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?)+$");
        private static readonly Regex GoodKept = new Regex(@"^[a-z][a-z0-9_]{0,31}$");
        private static readonly Regex Slot = new Regex(@"\{([^{}]*)\}");
        private static readonly Regex PathPart = new Regex(@"\G(?:\.?([A-Za-z_][A-Za-z0-9_-]*)|\[([0-9]{1,4})\])");
        // Swapped for a fake in her checks, so they never touch the network.
        internal static Func<string, string> RecipeFetch = Fetch;
        // Started with --no-mods: every mod stays off for this run, and nothing is written, so the next start is normal.
        internal static bool NoneThisRun;
        // Her window's way to run something on its own thread, for a tool she drafts from a brain's thread.
        internal Action<Action> OnWindow;
        private readonly Dictionary<string, int> strikes = new Dictionary<string, int>();
        private readonly object offGate = new object();
        private volatile Mod[] recipes = new Mod[0];
        private readonly List<string> stoppedStarts = new List<string>();
        private readonly object markGate = new object();
        private readonly HashSet<string> offThisRun = new HashSet<string>();
        private bool noneApplied;

        private Mod LoadTool(string file, HashSet<string> off)
        {
            byte[] bytes = File.ReadAllBytes(file);
            ModFile read = ReadTool(Words(bytes));
            if (!string.Equals(read.Id, Path.GetFileNameWithoutExtension(file), StringComparison.OrdinalIgnoreCase)) throw new ModException("its file name doesn't match its id, " + read.Id + ".");
            string hash = HashOf(bytes), approved;
            bool known = Approved().TryGetValue(read.Id, out approved), same = known && approved == hash;
            return new Mod { File = read, Path = file, Tool = true, Hash = hash, Changed = known && !same, On = same && !off.Contains(read.Id) };
        }

        internal static ModFile ReadTool(string text)
        {
            object parsed;
            try { parsed = HuskJson.Read((text ?? "").TrimStart('﻿')); }
            catch (FormatException) { throw new ModException("That file isn't valid tool JSON."); }
            Dictionary<string, object> map = parsed as Dictionary<string, object>;
            if (map == null) throw new ModException("A tool is one JSON object.");
            foreach (string key in map.Keys)
                if (Array.IndexOf(ToolFields, key) < 0) throw new ModException("\"" + key + "\" isn't a tool field. A tool has " + string.Join(", ", ToolFields) + ".");
            ModFile read = new ModFile { WakeNames = new string[0], HeardAs = new string[0], Format = Whole(map, "format"), Id = Field(map, "id"), Name = Field(map, "name"), Version = Field(map, "version"), Author = Field(map, "author"), Description = Field(map, "description") };
            CheckDetails(read);
            if (string.IsNullOrWhiteSpace(read.Description)) throw new ModException("A tool needs a description that tells her when to use it.");
            ToolRecipe recipe = new ToolRecipe { ToolName = read.Id.Replace('-', '_'), Takes = (Field(map, "takes") ?? "").Trim(), Say = Field(map, "say") };
            if (!GoodToolName.IsMatch(recipe.ToolName)) throw new ModException("A tool's id must start with a letter and be up to 40 characters.");
            if (recipe.Takes.Length > 150) throw new ModException("A tool's takes can be up to 150 characters.");
            recipe.Domains = ReadDomains(map);
            List<string> known = new List<string> { "text" };
            List<RecipeStep> steps = new List<RecipeStep>();
            List<object> listed = HuskJson.Get(map, "steps") as List<object>;
            if (listed == null || listed.Count == 0 || listed.Count > MostSteps) throw new ModException("A tool needs steps: a list of 1 to " + MostSteps + " steps.");
            foreach (object item in listed) steps.Add(ReadStep(item, recipe, known));
            recipe.Steps = steps.ToArray();
            if (string.IsNullOrWhiteSpace(recipe.Say) || recipe.Say.Length > 300) throw new ModException("A tool needs a say of up to 300 characters: what to tell her, with {text} and the values it kept.");
            CheckSlots(recipe.Say, known, recipe, "say");
            read.Recipe = recipe;
            read.Reaches = recipe.Domains;
            return read;
        }

        private static string[] ReadDomains(Dictionary<string, object> map)
        {
            List<object> listed = HuskJson.Get(map, "domains") as List<object>;
            if (listed == null || listed.Count == 0 || listed.Count > MostDomains) throw new ModException("A tool needs domains: a list of 1 to " + MostDomains + " host names it reads from.");
            List<string> domains = new List<string>();
            foreach (object item in listed)
            {
                string host = (item as string ?? "").Trim().ToLowerInvariant();
                if (!GoodHost.IsMatch(host)) throw new ModException("\"" + item + "\" isn't a host name. Write just the host, like api.example.com.");
                if (!domains.Contains(host)) domains.Add(host);
            }
            return domains.ToArray();
        }

        private static RecipeStep ReadStep(object item, ToolRecipe recipe, List<string> known)
        {
            Dictionary<string, object> map = item as Dictionary<string, object>;
            if (map == null) throw new ModException("Each step is an object with a get and a keep.");
            foreach (string key in map.Keys)
                if (key != "get" && key != "keep") throw new ModException("\"" + key + "\" isn't a step field. A step has get and keep.");
            RecipeStep step = new RecipeStep { Get = Field(map, "get") };
            if (string.IsNullOrEmpty(step.Get) || step.Get.Length > 500) throw new ModException("Each step needs a get: the https address to read, up to 500 characters.");
            CheckAddress(step.Get, recipe);
            CheckSlots(step.Get, known, recipe, "a get");
            object keep = HuskJson.Get(map, "keep");
            Dictionary<string, object> kept = keep as Dictionary<string, object>;
            if (keep != null && kept == null) throw new ModException("A step's keep is an object of names and paths, like { \"meaning\": \"[0].text\" }.");
            if (kept == null) return step;
            if (kept.Count > MostKept) throw new ModException("A step can keep up to " + MostKept + " values.");
            foreach (KeyValuePair<string, object> value in kept)
            {
                string path = value.Value as string;
                if (!GoodKept.IsMatch(value.Key) || value.Key == "text" || known.Contains(value.Key)) throw new ModException("\"" + value.Key + "\" can't be a kept name. Use lowercase letters, numbers and _, a new name for each value, and not text.");
                if (path == null || path.Length == 0 || path.Length > 120 || !GoodPath(path)) throw new ModException("\"" + path + "\" isn't a path. Paths look like extract, [0].date or results[0].latitude.");
                step.Keep.Add(new KeyValuePair<string, string>(value.Key, path));
                known.Add(value.Key);
            }
            return step;
        }

        private static void CheckAddress(string template, ToolRecipe recipe)
        {
            if (!template.StartsWith("https://", StringComparison.Ordinal)) throw new ModException("Every get starts with https://.");
            int end = template.IndexOf('/', 8);
            string authority = end < 0 ? template.Substring(8) : template.Substring(8, end - 8);
            if (authority.IndexOfAny(new[] { '{', '}', '@', ':', '?', '#', '\\' }) >= 0) throw new ModException("A get's host must be written out, with no {slots}, port or login in it.");
            if (Array.IndexOf(recipe.Domains, authority.ToLowerInvariant()) < 0) throw new ModException("A get reads from " + authority + ", which isn't in domains.");
            Uri address;
            if (!Uri.TryCreate(Slot.Replace(template, "x"), UriKind.Absolute, out address) || address.Scheme != Uri.UriSchemeHttps) throw new ModException("\"" + template + "\" isn't an https address.");
        }

        private static void CheckSlots(string template, List<string> known, ToolRecipe recipe, string where)
        {
            if (template.Replace("{", "").Length != template.Replace("}", "").Length) throw new ModException("The { and } in " + where + " don't pair up.");
            foreach (Match slot in Slot.Matches(template))
            {
                string name = slot.Groups[1].Value;
                if (!known.Contains(name)) throw new ModException(where + " uses {" + name + "}, which is neither text nor a value an earlier step kept.");
                if (name == "text" && recipe.Takes.Length == 0) throw new ModException(where + " uses {text}, so the tool needs a takes that says what its text is.");
            }
        }

        private static bool GoodPath(string path)
        {
            int at = 0;
            while (at < path.Length)
            {
                Match part = PathPart.Match(path, at);
                if (!part.Success || part.Length == 0 || at == 0 && part.Value.StartsWith(".", StringComparison.Ordinal)) return false;
                at += part.Length;
            }
            return true;
        }

        private static string Field(Dictionary<string, object> map, string key)
        {
            object value = HuskJson.Get(map, key);
            if (value != null && !(value is string)) throw new ModException("A tool's " + key + " must be text in quotes.");
            return value as string;
        }

        private static int Whole(Dictionary<string, object> map, string key)
        {
            HuskNumber number = HuskJson.Get(map, key) as HuskNumber;
            int whole;
            return number != null && int.TryParse(number.Text, NumberStyles.None, CultureInfo.InvariantCulture, out whole) ? whole : 0;
        }

        private Mod ImportTool(string file, Func<ModFile, string, bool> ask, HashSet<string> keepOff)
        {
            byte[] bytes;
            try
            {
                if (new FileInfo(file).Length > Largest) throw new ModException("That tool is too big. The limit is " + Largest / 1024 + " KB.");
                bytes = File.ReadAllBytes(file);
            }
            catch (IOException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't read that file: " + error.Message); }
            ModFile read = ReadTool(Words(bytes));
            if (TakenByOther(read.Id, ToolExtension)) throw new ModException("Another mod already uses the id " + read.Id + ".");
            offThisRun.Remove(read.Id);
            string hash = HashOf(bytes);
            bool stay = keepOff != null && keepOff.Contains(read.Id);
            if (!ask(read, hash)) return null;
            string home = Path.Combine(folder, read.Id + ToolExtension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllBytes(writing, bytes);
                if (File.Exists(home)) File.Delete(home);
                File.Move(writing, home);
                Approve(read.Id, hash);
                HashSet<string> off = Off();
                if (!stay && off.Remove(read.Id)) WriteOff(off);
            }
            catch (IOException error) { throw new ModException("Couldn't save that tool: " + error.Message); }
            catch (UnauthorizedAccessException error) { throw new ModException("Couldn't save that tool: " + error.Message); }
            Forgive(read.Id);
            Load();
            return All.Find(mod => mod.File.Id == read.Id);
        }

        private bool SetTool(Mod mod, bool on, Func<ModFile, string, bool> ask)
        {
            HashSet<string> off = Off();
            if (!on)
            {
                off.Add(mod.File.Id);
                WriteOff(off);
                mod.On = false;
                Apply();
                return true;
            }
            byte[] bytes = File.ReadAllBytes(mod.Path);
            ModFile read = ReadTool(Words(bytes));
            string hash = HashOf(bytes), approved;
            bool same = Approved().TryGetValue(mod.File.Id, out approved) && approved == hash;
            if (!same && !ask(read, hash)) return false;
            if (!same) Approve(mod.File.Id, hash);
            if (off.Remove(mod.File.Id)) WriteOff(off);
            Forgive(mod.File.Id);
            mod.File = read;
            mod.Hash = hash;
            mod.Changed = false;
            mod.On = true;
            Apply();
            return true;
        }

        // What her box says before a tool mod can be used: the sites it reads from, and that it can only read.
        internal static string ToolConsent(ModFile mod, string hash)
        {
            string hosts = "";
            foreach (string host in mod.Recipe.Domains) hosts += "\n- " + host;
            return mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : " v" + mod.Version) + (string.IsNullOrEmpty(mod.Author) ? "" : ", which says it's by " + mod.Author) + ", is a tool mod.\n\n"
                + "It gives her a tool called " + mod.Recipe.ToolName.Replace('_', ' ') + ". When she uses it, Capri "
                + (mod.Recipe.Takes.Length > 0 ? "sends what she looks up to these sites and reads their answers:" : "reads from these sites:") + hosts + "\n\n"
                + "It can only read. It can't send, post, buy or delete anything, and it can't run code on your PC.\n\n"
                + "File fingerprint (SHA-256):\n" + hash + "\n\nLet her use this tool?";
        }

        // The tools her tool mods that are on add, each as its name, what it does and what its text is for.
        internal string[][] RecipeTools()
        {
            Mod[] on = recipes;
            string[][] tools = new string[on.Length][];
            for (int i = 0; i < on.Length; i++)
            {
                ToolRecipe recipe = on[i].File.Recipe;
                string description = on[i].File.Description.Trim();
                tools[i] = new[] { recipe.ToolName, description.Length > 300 ? description.Substring(0, 300) : description, recipe.Takes };
            }
            return tools;
        }

        // Runs a tool mod by its tool name, or gives back null when no tool mod that's on has that name.
        internal string RunRecipe(string name, string text)
        {
            Mod mod = Array.Find(recipes, one => one.File.Recipe.ToolName == name);
            if (mod == null) return null;
            ToolRecipe recipe = mod.File.Recipe;
            string input = (text ?? "").Trim();
            if (recipe.Takes.Length > 0 && input.Length == 0) return mod.File.Name + " needs its text: " + recipe.Takes.TrimEnd('.') + ".";
            if (input.Length > MostInput) return "That's too long for " + mod.File.Name + ". Keep it under " + MostInput + " characters.";
            if (input == "." || input == "..") return "That can't go in a web address.";
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            values["text"] = input;
            DateTime until = DateTime.UtcNow.AddMilliseconds(AllWait);
            try
            {
                foreach (RecipeStep step in recipe.Steps)
                {
                    if (DateTime.UtcNow >= until) throw new RecipeException(false, "it took too long");
                    Uri address;
                    if (!Uri.TryCreate(Filled(step.Get, values, true), UriKind.Absolute, out address) || address.Scheme != Uri.UriSchemeHttps || !address.IsDefaultPort || address.UserInfo.Length > 0 || Array.IndexOf(recipe.Domains, address.Host.ToLowerInvariant()) < 0)
                        throw new RecipeException(true, "it tried to read from an address it doesn't list");
                    object answer;
                    try { answer = HuskJson.Read(RecipeFetch(address.AbsoluteUri)); }
                    catch (FormatException) { throw new RecipeException(true, address.Host + " didn't answer in JSON"); }
                    foreach (KeyValuePair<string, string> keep in step.Keep)
                    {
                        string value = Shown(Walked(answer, keep.Value));
                        if (value == null) throw new RecipeException(true, "there was no " + keep.Value + " in " + address.Host + "'s answer");
                        if (value.Trim() == "." || value.Trim() == "..") throw new RecipeException(true, address.Host + " sent a value that can't go in an address");
                        values[keep.Key] = value.Length > MostSaid ? value.Substring(0, MostSaid) : value;
                    }
                }
            }
            catch (RecipeException error) { return error.Broken ? Struck(mod, error.Message) : mod.File.Name + " didn't work this time: " + error.Message + "."; }
            catch (WebException error) { return mod.File.Name + " couldn't reach its site: " + error.Message; }
            catch (IOException error) { return mod.File.Name + " couldn't reach its site: " + error.Message; }
            Forgive(mod.File.Id);
            string said = Filled(recipe.Say, values, false).Trim();
            return said.Length > MostSaid ? said.Substring(0, MostSaid) : said;
        }

        private static string Fetch(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Capri-Desktop";
            request.Accept = "application/json";
            request.AllowAutoRedirect = false;
            request.Timeout = StepWait;
            request.ReadWriteTimeout = StepWait;
            try
            {
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                using (Stream body = response.GetResponseStream())
                    return Answer(request.RequestUri.Host, (int)response.StatusCode, body);
            }
            catch (WebException error)
            {
                HttpWebResponse answered = error.Response as HttpWebResponse;
                if (answered == null) throw new RecipeException(false, request.RequestUri.Host + " didn't answer");
                int code = (int)answered.StatusCode;
                answered.Close();
                if (code >= 400 && code < 500) throw new RecipeException(false, request.RequestUri.Host + " found nothing for that (" + code + ")");
                throw new RecipeException(false, request.RequestUri.Host + " had a problem (" + code + ")");
            }
        }

        private static string Answer(string host, int code, Stream body)
        {
            if (code >= 300) throw new RecipeException(false, host + " moved that somewhere else (" + code + ")");
            using (MemoryStream kept = new MemoryStream())
            {
                byte[] buffer = new byte[8192];
                int read;
                while ((read = body.Read(buffer, 0, buffer.Length)) > 0)
                {
                    kept.Write(buffer, 0, read);
                    if (kept.Length > LargestAnswer) throw new RecipeException(true, host + "'s answer was over " + LargestAnswer / 1024 + " KB");
                }
                return new UTF8Encoding(false).GetString(kept.ToArray());
            }
        }

        private static string Filled(string template, Dictionary<string, string> values, bool address)
        {
            return Slot.Replace(template, delegate(Match slot)
            {
                string value;
                if (!values.TryGetValue(slot.Groups[1].Value, out value)) value = "";
                return address ? Uri.EscapeDataString(value) : value;
            });
        }

        private static object Walked(object root, string path)
        {
            object at = root;
            int from = 0;
            while (from < path.Length)
            {
                Match part = PathPart.Match(path, from);
                if (!part.Success || part.Length == 0) return null;
                from += part.Length;
                if (part.Groups[1].Success)
                {
                    Dictionary<string, object> map = at as Dictionary<string, object>;
                    if (map == null || !map.TryGetValue(part.Groups[1].Value, out at)) return null;
                    continue;
                }
                List<object> list = at as List<object>;
                int index = int.Parse(part.Groups[2].Value, CultureInfo.InvariantCulture);
                if (list == null || index >= list.Count) return null;
                at = list[index];
            }
            return at;
        }

        private static string Shown(object value)
        {
            if (value is string) return (string)value;
            HuskNumber number = value as HuskNumber;
            if (number != null) return number.Text;
            if (value is bool) return (bool)value ? "yes" : "no";
            return null;
        }

        private string Struck(Mod mod, string why)
        {
            int count;
            lock (strikes)
            {
                strikes.TryGetValue(mod.File.Id, out count);
                strikes[mod.File.Id] = ++count;
                if (count >= StrikesToOff) strikes.Remove(mod.File.Id);
            }
            if (count < StrikesToOff) return mod.File.Name + " didn't work: " + why + ".";
            mod.On = false;
            recipes = Array.FindAll(recipes, one => one.File.Id != mod.File.Id);
            try
            {
                lock (offGate)
                {
                    HashSet<string> off = Off();
                    if (off.Add(mod.File.Id)) WriteOff(off);
                }
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            Brain.Log("mods", -1, mod.File.Id + " switched off after " + StrikesToOff + " broken runs: " + why);
            Action<Action> post = OnWindow;
            if (post != null) post(Load);
            return mod.File.Name + " broke " + StrikesToOff + " times in a row (" + why + "), so Capri switched it off. Tell them it needs fixing, and that they can turn it back on in Mods.";
        }

        private void Forgive(string id)
        {
            lock (strikes) strikes.Remove(id);
        }

        // She drafts a tool mod from what they asked for. It's saved off and not approved, so nothing runs until they read it in
        // Mods and turn it on, which shows the same box as importing one.
        internal string MakeTool(string text)
        {
            ModFile read;
            try { read = ReadTool(text); }
            catch (ModException error) { return "That tool isn't right yet: " + error.Message + " Fix it and call make_tool again."; }
            if (TakenByOther(read.Id, null)) return "There's already a mod with the id " + read.Id + ". Pick another id, or ask them to remove that one first.";
            string home = Path.Combine(folder, read.Id + ToolExtension);
            try
            {
                Directory.CreateDirectory(folder);
                string writing = home + ".new";
                File.WriteAllText(writing, text.Trim(), new UTF8Encoding(false));
                File.Move(writing, home);
                lock (offGate)
                {
                    HashSet<string> off = Off();
                    if (off.Add(read.Id)) WriteOff(off);
                }
            }
            catch (IOException error) { return "Couldn't save that tool: " + error.Message; }
            catch (UnauthorizedAccessException error) { return "Couldn't save that tool: " + error.Message; }
            Action<Action> post = OnWindow;
            if (post != null) post(Load); else Load();
            return "Saved " + read.Name + " in their Mods, switched off. It reads from " + Listed(read.Recipe.Domains) + ". Tell them to open Mods, check it and turn it on; then you can use it as " + read.Recipe.ToolName + ".";
        }

        // A code mod that was starting when she last stopped stays off, in case it's why she stopped.
        private void CheckStarting()
        {
            string marker = Path.Combine(folder, StartingFile);
            try
            {
                if (!File.Exists(marker)) return;
                string[] ids = File.ReadAllLines(marker);
                File.Delete(marker);
                HashSet<string> off = Off();
                foreach (string line in ids)
                {
                    string id = line.Trim();
                    if (!GoodId.IsMatch(id) || stoppedStarts.Contains(id)) continue;
                    off.Add(id);
                    stoppedStarts.Add(id);
                    Brain.Log("mods", -1, id + " was starting when Capri last stopped, so it's off.");
                }
                WriteOff(off);
            }
            catch (IOException error) { Brain.Log("mods", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
        }

        // Her checks for tool mods, make_tool, the startup marker and --no-mods, the same on every platform. No check touches the
        // network: every answer comes from the fake fetch below.
        internal void CheckTools(List<string> checks, string output)
        {
            Func<string, string> fetchWas = RecipeFetch;
            Action<Action> windowWas = OnWindow;
            Dictionary<string, string> answers = new Dictionary<string, string>();
            int asked = 0;
            bool answer = false;
            Func<ModFile, string, bool> ask = delegate { asked++; return answer; };
            string recipe = "{\"format\":1,\"id\":\"smoke-word\",\"name\":\"Smoke word\",\"description\":\"Looks up a word for her checks.\",\"takes\":\"One word\",\"domains\":[\"api.example.com\"],\"steps\":[{\"get\":\"https://api.example.com/words/{text}\",\"keep\":{\"meaning\":\"[0].meanings[0].text\",\"count\":\"[0].count\"}}],\"say\":\"{text}: {meaning} ({count})\"}";
            RecipeFetch = delegate(string url)
            {
                string body;
                if (!answers.TryGetValue(url, out body)) throw new RecipeException(false, "api.example.com didn't answer");
                if (body == "404") throw new RecipeException(false, "api.example.com found nothing for that (404)");
                return body;
            };
            OnWindow = null;
            try
            {
                if (File.Exists(Path.Combine(folder, StartingFile))) throw new Exception("Starting a code mod left it in starting.txt.");
                foreach (Mod old in All.ToArray()) Remove(old);
                string[,] broken =
                {
                    { recipe.Replace("https://api.example.com/", "https://other.example.com/"), "isn't in domains" },
                    { recipe.Replace("https://", "http://"), "starts with https://" },
                    { recipe.Replace("https://api.example.com/words/{text}", "https://{text}/words"), "must be written out" },
                    { recipe.Replace("{text}: {meaning}", "{text}: {missing}"), "neither text nor" },
                    { recipe.Replace("\"steps\":[{", "\"steps\":[{\"get\":\"https://api.example.com/a\"},{\"get\":\"https://api.example.com/b\"},{\"get\":\"https://api.example.com/c\"},{"), "1 to 3 steps" },
                    { recipe.Replace("[0].count", "[0]..count"), "isn't a path" },
                    { recipe.Replace("\"say\"", "\"post\":\"x\",\"say\""), "isn't a tool field" },
                    { recipe.Replace("\"takes\":\"One word\",", ""), "needs a takes" }
                };
                for (int i = 0; i < broken.GetLength(0); i++)
                {
                    try { ReadTool(broken[i, 0]); }
                    catch (ModException error)
                    {
                        if (error.Message.IndexOf(broken[i, 1], StringComparison.Ordinal) < 0) throw new Exception("A broken tool mod gave the wrong reason: " + error.Message);
                        continue;
                    }
                    throw new Exception("A broken tool mod was accepted: " + broken[i, 1]);
                }
                checks.Add("PASS tool mods: a tool that reads an unlisted host or plain http, puts a slot in its host, uses an unknown value, has too many steps, a bad path, an unknown field or {text} without takes is refused with the reason");

                string file = Path.Combine(output, "smoke-word" + ToolExtension);
                File.WriteAllText(file, recipe);
                if (Import(file, ask) != null || asked != 1 || All.Count != 0) throw new Exception("A tool mod went in without being approved.");
                answer = true;
                Mod mod = Import(file, ask);
                string[][] tools = RecipeTools();
                if (mod == null || asked != 2 || !mod.Tool || !mod.On || tools.Length != 1 || tools[0][0] != "smoke_word" || tools[0][2] != "One word") throw new Exception("An approved tool mod didn't become her smoke_word tool.");
                if (Consent(mod.File, mod.Hash).IndexOf("api.example.com", StringComparison.Ordinal) < 0) throw new Exception("A tool mod's box didn't name the site it reads from.");
                checks.Add("PASS tool mods: importing asks first and names the sites it reads, no installs nothing, and yes adds smoke_word to her tools");

                answers["https://api.example.com/words/two%20words"] = "[{\"meanings\":[{\"text\":\"a test\"}],\"count\":2.5}]";
                string said = RunRecipe("smoke_word", " two words ");
                if (said != "two words: a test (2.5)") throw new Exception("A tool mod's run said the wrong thing: " + said);
                said = Hands.Do("mod", new ToolArguments { Name = "smoke_word", Text = "two words" });
                if (said != "two words: a test (2.5)") throw new Exception("Her mod tool didn't run a tool mod: " + said);
                if (RunRecipe("nobody", "x") != null || RunRecipe("smoke_word", "").IndexOf("needs its text", StringComparison.Ordinal) < 0) throw new Exception("A tool mod answered for a name it doesn't have, or ran without its text.");
                if (RunRecipe("smoke_word", "..").IndexOf("web address", StringComparison.Ordinal) < 0) throw new Exception("A tool mod put .. in an address.");
                checks.Add("PASS tool mods: her mod tool runs one, its address gets the escaped text, values come by path with numbers as written, it needs its text, and .. never goes in an address");

                answers["https://api.example.com/words/zzz"] = "404";
                for (int i = 0; i < 4; i++) RunRecipe("smoke_word", "zzz");
                if (!mod.On) throw new Exception("A tool mod was switched off for words a site didn't know.");
                answers["https://api.example.com/words/odd"] = "{\"other\":1}";
                RunRecipe("smoke_word", "odd");
                RunRecipe("smoke_word", "odd");
                said = RunRecipe("smoke_word", "odd");
                if (said.IndexOf("switched it off", StringComparison.Ordinal) < 0 || mod.On || RecipeTools().Length != 0) throw new Exception("Three broken answers didn't switch a tool mod off: " + said);
                Load();
                if (All[0].On) throw new Exception("A tool mod switched off for breaking came back on after a reload.");
                checks.Add("PASS tool mods: a site that finds nothing doesn't count against a tool, and three broken answers in a row switch it off and keep it off");

                try { Answer("api.example.com", 301, new MemoryStream()); throw new Exception("A tool mod followed a redirect."); }
                catch (RecipeException error) { if (error.Broken) throw new Exception("A redirect counted against a tool mod."); }
                try { Answer("api.example.com", 200, new MemoryStream(new byte[LargestAnswer + 1])); throw new Exception("A tool mod read an answer over 64 KB."); }
                catch (RecipeException error) { if (!error.Broken) throw new Exception("An answer over 64 KB didn't count against a tool mod."); }
                if (Answer("api.example.com", 200, new MemoryStream(Encoding.UTF8.GetBytes("{}"))) != "{}") throw new Exception("A tool mod couldn't read a small answer.");
                checks.Add("PASS tool mods: a redirect is refused without counting against it, and an answer over 64 KB is refused");

                asked = 0;
                if (!Set(All[0], true, ask) || asked != 0 || !All[0].On) throw new Exception("Turning a tool mod back on asked again, or didn't turn it on.");
                File.WriteAllText(All[0].Path, recipe.Replace("Smoke word", "Smoke words"));
                Load();
                if (All[0].On || !All[0].Changed) throw new Exception("An edited tool mod stayed on without asking.");
                answer = false;
                if (Set(All[0], true, ask) || asked != 1 || All[0].On) throw new Exception("An edited tool mod went on without a yes.");
                checks.Add("PASS tool mods: turning one back on asks nothing, and an edited one is off until it's approved again");

                string made = Hands.Do("make_tool", new ToolArguments { Text = recipe.Replace("smoke-word", "smoke-made") });
                Mod drafted = All.Find(one => one.File.Id == "smoke-made");
                if (!made.StartsWith("Saved", StringComparison.Ordinal) || drafted == null || drafted.On || drafted.Changed || RecipeTools().Length != 0) throw new Exception("A tool she made wasn't saved off and waiting: " + made);
                if (!MakeTool(recipe.Replace("smoke-word", "smoke-made")).StartsWith("There's already", StringComparison.Ordinal)) throw new Exception("make_tool replaced a mod that was already in.");
                if (!MakeTool("{\"format\":1}").StartsWith("That tool isn't right yet", StringComparison.Ordinal)) throw new Exception("make_tool saved a broken tool.");
                checks.Add("PASS make_tool: she saves a tool off and not approved, refuses an id that's taken, and says what's wrong with a broken one");

                answer = true;
                Set(All.Find(one => one.File.Id == "smoke-word"), true, ask);
                File.WriteAllText(Path.Combine(folder, StartingFile), "smoke-word");
                CheckStarting();
                Load();
                if (File.Exists(Path.Combine(folder, StartingFile)) || All.Find(one => one.File.Id == "smoke-word").On || !Problems.Exists(problem => problem.StartsWith("smoke-word was starting", StringComparison.Ordinal))) throw new Exception("A mod that was starting when she stopped wasn't kept off.");
                Set(All.Find(one => one.File.Id == "smoke-word"), true, ask);
                Load();
                if (Problems.Exists(problem => problem.StartsWith("smoke-word was starting", StringComparison.Ordinal))) throw new Exception("The Mods page still said a mod stopped her after it was turned back on.");
                checks.Add("PASS mods: a mod that was starting when she stopped is off on her next start, the Mods page says why until it's on again, and a start that finishes leaves nothing behind");

                NoneThisRun = true;
                Load();
                if (All.Exists(one => one.On) || !Problems.Exists(problem => problem.IndexOf("--no-mods", StringComparison.Ordinal) >= 0) || Off().Contains("smoke-word")) throw new Exception("--no-mods didn't start her with every mod off, or saved them off.");
                Set(All.Find(one => one.File.Id == "smoke-word"), true, ask);
                Load();
                if (!All.Find(one => one.File.Id == "smoke-word").On) throw new Exception("A mod turned on during a --no-mods run went off again on the next reload.");
                NoneThisRun = false;
                noneApplied = false;
                offThisRun.Clear();
                checks.Add("PASS mods: --no-mods starts her with every mod off without changing what's saved, and one they turn on stays on");
            }
            finally
            {
                RecipeFetch = fetchWas;
                OnWindow = windowWas;
                NoneThisRun = false;
                noneApplied = false;
                offThisRun.Clear();
                stoppedStarts.Clear();
                foreach (Mod left in All.ToArray()) Remove(left);
            }
        }

        // Every code mod being made or started is listed in starting.txt until it's done, so a crash leaves its id behind.
        private void MarkStarting(string id, bool starting)
        {
            lock (markGate)
            {
                try
                {
                    string marker = Path.Combine(folder, StartingFile);
                    List<string> ids = new List<string>(File.Exists(marker) ? File.ReadAllLines(marker) : new string[0]);
                    ids.RemoveAll(one => one == id);
                    if (starting) ids.Add(id);
                    Directory.CreateDirectory(folder);
                    if (ids.Count == 0) File.Delete(marker);
                    else File.WriteAllLines(marker, ids.ToArray());
                }
                catch (IOException error) { Brain.Log("mods", -1, error.Message); }
                catch (UnauthorizedAccessException error) { Brain.Log("mods", -1, error.Message); }
            }
        }

        private bool TakenByOther(string id, string extension)
        {
            foreach (string kind in new[] { Extension, CodeExtension, SkinExtension, SkillExtension, ToolExtension })
                if (kind != extension && File.Exists(Path.Combine(folder, id + kind))) return true;
            return false;
        }

        // Started with --no-mods, every mod that was in at the start stays off this run, unless they turn it on or import it again.
        private void OffThisRun()
        {
            if (!NoneThisRun) return;
            if (!noneApplied)
            {
                foreach (Mod mod in All) offThisRun.Add(mod.File.Id);
                noneApplied = true;
            }
            Problems.Add("Capri started with --no-mods, so the mods that were in stay off until she starts normally or you turn one on.");
            foreach (Mod mod in All) if (offThisRun.Contains(mod.File.Id)) mod.On = false;
        }
    }
}
