using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class ModFloor
    {
        internal const string SettingsReach = "reaches into Capri's own settings folder, where her keys are saved";
        internal const string CarriesKey = "it would send one of her saved keys";
        private const RegexOptions Loose = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        internal static readonly Regex SecretName = new Regex(@"(?:^|_)(?:API_?KEYS?|KEYS?|TOKENS?|SECRETS?|PASSWORDS?|PASSWD|PASS|PAT|CREDENTIALS?|COOKIES?)(?:_|$)", Loose);
        internal static readonly Regex Settings = new Regex(@"(?:AppData|Application Support|\.config)[\\/]+(?:(?:Local|Roaming)[\\/]+)?Capri\b|(?:LOCALAPPDATA|ApplicationData|APPDATA)[^\r\n]{0,40}?[""'\\/]Capri\b|\bCapriAI_[A-Za-z0-9]+", Loose);
        private static readonly Regex Fetches = new Regex(@"\b(?:curl|wget|iwr|irm|invoke-webrequest|invoke-restmethod|bitsadmin|start-bitstransfer|certutil|downloadstring|downloadfile|downloaddata|urlopen|urlretrieve)\b|\bnet\.webclient\b|\burllib\.request\b|\brequests\.get\b|\bfetch\s*\(|\bhttps?\.get\s*\(", Loose);
        private static readonly Regex Runs = new Regex(@"\|\s*(?:sudo\s+)?(?:sh|bash|zsh|dash|ksh|fish|python[0-9.]*|py|node|perl|ruby|php|iex|pwsh|powershell)\b|\b(?:iex|invoke-expression)\b|\beval\b|\bexec\s*\(|<\(\s*(?:curl|wget)\b|\bchmod\s+[+0-7ugoa]*x\b|\bstart-process\b|&&\s*\.{0,2}[\\/]", Loose);
        private static readonly Regex Unpacks = new Regex(@"\bb64decode\b|\bfrombase64string\b|\bbase64\s+(?:-d|--decode)\b|\batob\s*\(|['""]base64['""]\s*\)", Loose);
        private static readonly Regex Shells = new Regex(@"^(?:powershell|pwsh)(?:\.exe)?$", Loose);
        private static readonly object gate = new object();
        private static readonly HashSet<string> heard = new HashSet<string>(StringComparer.Ordinal);

        internal static string Hidden(PluginServer server)
        {
            string line = (server.Command ?? "") + " " + string.Join(" ", server.Args ?? new string[0]);
            if (Shells.IsMatch(Path.GetFileName(server.Command ?? "")) || Regex.IsMatch(line, @"\b(?:powershell|pwsh)(?:\.exe)?\b", Loose))
                foreach (string arg in server.Args ?? new string[0])
                    if (Encoded(arg)) return "runs PowerShell code hidden in an encoded block";
            if (Fetches.IsMatch(line) && Runs.IsMatch(line)) return "downloads code and runs it as it starts";
            if (Unpacks.IsMatch(line) && Runs.IsMatch(line)) return "unpacks hidden code and runs it as it starts";
            return null;
        }

        private static bool Encoded(string arg)
        {
            string flag = (arg ?? "").Trim().ToLowerInvariant();
            if (flag.Length < 2 || flag[0] != '-' && flag[0] != '/') return false;
            flag = flag.TrimStart('-', '/');
            return flag == "e" || flag == "ec" || flag.Length >= 2 && "encodedcommand".StartsWith(flag, StringComparison.Ordinal);
        }

        internal static string And(string[] names)
        {
            if (names.Length < 2) return string.Join("", names);
            return string.Join(", ", names, 0, names.Length - 1) + " and " + names[names.Length - 1];
        }

        internal static string[] Pulled(PluginServer server)
        {
            List<string> found = new List<string>();
            List<string> places = new List<string> { server.Command, server.Cwd };
            places.AddRange(server.Args ?? new string[0]);
            places.AddRange(server.Env.Values);
            foreach (string place in places)
                foreach (Match one in Regex.Matches(place ?? "", @"\$\{([A-Za-z_][A-Za-z0-9_]*)"))
                {
                    string name = one.Groups[1].Value;
                    if (SecretName.IsMatch(name) && !found.Contains(name)) found.Add(name);
                }
            return found.ToArray();
        }

        internal static List<string> Scrub(StringDictionary env, ICollection<string> named)
        {
            List<string> gone = new List<string>();
            foreach (string key in new List<string>(EnvKeys(env)))
            {
                if (Named(named, key)) continue;
                string value = env[key];
                if (SecretName.IsMatch(key) || !string.IsNullOrEmpty(value) && Brain.HoldsSecret(value)) { env.Remove(key); gone.Add(key); }
            }
            return gone;
        }

        private static IEnumerable<string> EnvKeys(StringDictionary env)
        {
            List<string> keys = new List<string>();
            foreach (string key in env.Keys) keys.Add(key);
            return keys;
        }

        private static bool Named(ICollection<string> named, string key)
        {
            if (named == null) return false;
            foreach (string one in named) if (string.Equals(one, key, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string Refuses(PluginCall call)
        {
            if (call.Arguments == null) return null;
            return Brain.HoldsSecret(PluginJson.Write(call.Arguments)) || Brain.HoldsSecret(Values(call.Arguments)) ? CarriesKey : null;
        }

        private static string Values(object value)
        {
            Dictionary<string, object> map = PluginJson.Map(value);
            if (map != null) { List<string> parts = new List<string>(); foreach (object one in map.Values) parts.Add(Values(one)); return string.Join("\n", parts.ToArray()); }
            List<object> list = PluginJson.List(value);
            if (list != null) { List<string> parts = new List<string>(); foreach (object one in list) parts.Add(Values(one)); return string.Join("\n", parts.ToArray()); }
            return value as string ?? "";
        }

        internal static string[] Reaches(string root)
        {
            List<string> found = new List<string>();
            try
            {
                int looked = 0;
                foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                {
                    if (++looked > 400) break;
                    FileInfo info = new FileInfo(file);
                    if (info.Length > 2 * 1024 * 1024) continue;
                    if (Settings.IsMatch(File.ReadAllText(file))) { found.Add(SettingsReach); break; }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return found.ToArray();
        }

        internal static void Heard(string from)
        {
            if (string.IsNullOrEmpty(from)) return;
            lock (gate) heard.Add(from);
        }

        internal static void Fresh() { lock (gate) heard.Clear(); }

        internal static string[] HeardFrom { get { lock (gate) return new List<string>(heard).ToArray(); } }

        internal static bool Forces(string tool, ToolArguments args, int toggle)
        {
            lock (gate) if (heard.Count == 0) return false;
            if (tool == "mod")
            {
                bool readOnly;
                string owner = Plugins.OwnerOf(args == null ? null : args.Name, out readOnly);
                if (owner == null) return true;
                if (readOnly) return false;
                lock (gate) foreach (string from in heard) if (from != "plugin:" + owner) return true;
                return false;
            }
#if CROSS
            if (toggle == Security.Terminal) return true;
#endif
            return toggle == Security.Mouse || toggle == Security.Sends || toggle == Security.Pays;
        }
    }
}
