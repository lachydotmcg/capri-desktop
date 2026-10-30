using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class ArchApps
    {
        internal static List<InstalledApp> NativeTools(Func<string, string> locate = null)
        {
            locate ??= Executable;
            List<InstalledApp> apps = new List<InstalledApp>();
            string kitty = locate("kitty"), fastfetch = locate("fastfetch");
            if (kitty != null)
            {
                apps.Add(new InstalledApp("Terminal", "argv:" + JsonSerializer.Serialize(new[] { kitty })));
                if (fastfetch != null) apps.Add(new InstalledApp("Fastfetch", "argv:" + JsonSerializer.Serialize(new[] { kitty, "--hold", fastfetch })));
            }
            return apps;
        }

        private static string Executable(string name)
        {
            foreach (string folder in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (!Path.IsPathFullyQualified(folder)) continue;
                string path = Path.Combine(folder, name);
                try
                {
                    if (File.Exists(path) && (File.GetUnixFileMode(path) & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute)) != 0) return path;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return null;
        }

        internal static List<InstalledApp> Catalog(IEnumerable<string> folders)
        {
            List<InstalledApp> apps = new List<InstalledApp>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder, "*.desktop", SearchOption.AllDirectories); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                foreach (string file in files.OrderBy(name => name, StringComparer.Ordinal))
                {
                    string id = Path.GetRelativePath(folder, file).Replace(Path.DirectorySeparatorChar, '-');
                    if (!seen.Add(id)) continue;
                    try
                    {
                        InstalledApp parsed = Parse(File.ReadAllLines(file));
                        if (parsed != null) apps.Add(new InstalledApp(parsed.Name, "desktop:" + Path.GetFullPath(file)));
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (FormatException) { }
                }
            }
            return apps;
        }

        internal static InstalledApp Parse(string[] lines)
        {
            Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.Ordinal);
            bool entry = false;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith('[')) { entry = line == "[Desktop Entry]"; continue; }
                int equal = line.IndexOf('=');
                if (entry && equal > 0 && !line.StartsWith('#')) values.TryAdd(line.Substring(0, equal), line.Substring(equal + 1));
            }
            string Value(string name) { return values.GetValueOrDefault(name); }
            if (Value("Type") != "Application" || Value("Hidden") == "true" || Value("NoDisplay") == "true" || string.IsNullOrWhiteSpace(Value("Name"))) return null;
            if (string.IsNullOrWhiteSpace(Value("Exec"))) return Value("DBusActivatable") == "true" ? new InstalledApp(Value("Name"), "dbus") : null;
            return new InstalledApp(Value("Name"), "argv:" + JsonSerializer.Serialize(Arguments(Value("Exec"), Value("Name"))));
        }

        internal static string[] Arguments(string exec, string name)
        {
            List<string> arguments = new List<string>();
            StringBuilder word = new StringBuilder();
            bool quoted = false, escaped = false, started = false;
            foreach (char character in exec)
            {
                if (escaped) { word.Append(character); escaped = false; started = true; }
                else if (character == '\\') escaped = true;
                else if (character == '"') { quoted = !quoted; started = true; }
                else if (char.IsWhiteSpace(character) && !quoted) { if (started) { arguments.Add(word.ToString()); word.Clear(); started = false; } }
                else { word.Append(character); started = true; }
            }
            if (quoted || escaped) throw new FormatException("The desktop entry has an invalid Exec argument.");
            if (started) arguments.Add(word.ToString());
            List<string> expanded = new List<string>();
            foreach (string argument in arguments)
            {
                if (Regex.IsMatch(argument, @"^%[fFuUdDnNickvm]$")) { if (argument == "%c") expanded.Add(name); continue; }
                string value = argument.Replace("%%", "\0");
                if (value.Contains('%')) throw new FormatException("Unsupported desktop-entry field expansion.");
                expanded.Add(value.Replace('\0', '%'));
            }
            if (expanded.Count == 0 || expanded[0].Length == 0) throw new FormatException("The desktop entry has no executable.");
            return expanded.ToArray();
        }

        internal static bool Open(string id, Action<string, string[]> launch)
        {
            if (id.StartsWith("desktop:", StringComparison.Ordinal))
            {
                string path = id.Substring(8);
                if (!Path.IsPathFullyQualified(path) || !path.EndsWith(".desktop", StringComparison.Ordinal) || !File.Exists(path)) throw new IOException("The installed desktop entry is no longer available.");
                launch("gio", new[] { "launch", path });
                return true;
            }
            if (id.StartsWith("argv:", StringComparison.Ordinal))
            {
                string[] args = JsonSerializer.Deserialize<string[]>(id.Substring(5));
                if (args == null || args.Length == 0 || string.IsNullOrWhiteSpace(args[0])) throw new FormatException("No executable was specified.");
                launch(args[0], args.Skip(1).ToArray());
                return true;
            }
            if (id.StartsWith("exec:", StringComparison.Ordinal)) throw new InvalidOperationException("Legacy shell launch entries must be refreshed from installed desktop IDs.");
            return false;
        }

        internal static List<InstalledApp> Games(string home)
        {
            List<InstalledApp> games = new List<InstalledApp>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string path in new[] { ".steam/steam", ".local/share/Steam", ".var/app/com.valvesoftware.Steam/.local/share/Steam", "snap/steam/common/.local/share/Steam" })
                foreach (InstalledApp game in Hands.SteamApps(Path.Combine(home, path))) if (seen.Add(game.Id)) games.Add(game);
            return games;
        }
    }
}
