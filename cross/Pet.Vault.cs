using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

namespace CapriDesktop
{
    internal sealed partial class Pet
    {
        internal const string VaultNote = "Capri Memories.md";
        private string ownNotes;
        internal Func<List<KeyValuePair<string, string>>> VaultList = ObsidianVaults;
        internal string Vault { get { return Saved.Vault ?? ""; } }
        internal bool VaultHere { get { return Brain.VaultFolder != null; } }

        private void StartVault()
        {
            ownNotes = Brain.MemoryPath;
            Brain.VaultFolder = null;
            try
            {
                string chosen = NotesIn(Vault);
                if (chosen != null && Directory.Exists(Path.GetDirectoryName(chosen))) { Brain.MemoryPath = chosen; Brain.VaultFolder = Path.GetDirectoryName(chosen); }
            }
            catch (ArgumentException) { }
            catch (IOException) { }
        }

        internal static string NotesIn(string chosen)
        {
            if (string.IsNullOrEmpty(chosen)) return null;
            return chosen.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? chosen : Path.Combine(chosen, "me.md");
        }

        internal string SetVault(string folder)
        {
            folder = (folder ?? "").Trim().Trim('"');
            string notes;
            try
            {
                notes = folder.Length == 0 ? ownNotes : NotesIn(Path.GetFullPath(folder));
                Directory.CreateDirectory(Path.GetDirectoryName(notes));
                CarryNotes(Brain.MemoryPath, notes);
            }
            catch (IOException error) { return "Couldn't use that: " + error.Message; }
            catch (UnauthorizedAccessException error) { return "Couldn't use that: " + error.Message; }
            catch (ArgumentException error) { return "Couldn't use that: " + error.Message; }
            catch (NotSupportedException error) { return "Couldn't use that: " + error.Message; }
            bool own = folder.Length == 0;
            Saved.Vault = own ? null : notes;
            Brain.MemoryPath = notes;
            Brain.VaultFolder = own ? null : Path.GetDirectoryName(notes);
            Save();
            return own ? "Her notes are back in her own folder, and your vault keeps its copy." : "Connected. She saves her memories to " + Path.GetFileName(notes) + " in " + Path.GetDirectoryName(notes) + ".";
        }

        private static void CarryNotes(string from, string to)
        {
            StringComparison paths = OperatingSystem.IsMacOS() || OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (from == null || !File.Exists(from) || string.Equals(Path.GetFullPath(from), to, paths))
            {
                if (!File.Exists(to)) File.WriteAllText(to, Brain.NotesHead);
                return;
            }
            if (!File.Exists(to)) { File.Copy(from, to); return; }
            string current = File.ReadAllText(to);
            var missing = new StringBuilder();
            foreach (string line in File.ReadAllLines(from))
                if (line.StartsWith("- ", StringComparison.Ordinal) && current.IndexOf(line.Trim(), StringComparison.OrdinalIgnoreCase) < 0) missing.Append(line.Trim()).Append('\n');
            if (missing.Length > 0) File.WriteAllText(to, current + (current.Length == 0 || current.EndsWith("\n", StringComparison.Ordinal) ? "" : "\n") + missing);
        }

        internal static List<KeyValuePair<string, string>> ObsidianVaults()
        {
            return ObsidianVaults(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OperatingSystem.IsMacOS(), Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));
        }

        internal static List<KeyValuePair<string, string>> ObsidianVaults(string home, bool mac, string config = null)
        {
            var vaults = new List<KeyValuePair<string, string>>();
            string[] lists = mac ? new[] { Path.Combine(home, "Library", "Application Support", "obsidian", "obsidian.json") } : new[] {
                Path.Combine(string.IsNullOrEmpty(config) ? Path.Combine(home, ".config") : config, "obsidian", "obsidian.json"),
                Path.Combine(home, ".var", "app", "md.obsidian.Obsidian", "config", "obsidian", "obsidian.json") };
            var seen = new HashSet<string>(mac ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            foreach (string list in lists)
            {
                try { if (File.Exists(list)) foreach (var vault in VaultsIn(File.ReadAllText(list))) if (seen.Add(vault.Value)) vaults.Add(vault); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            vaults.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return vaults;
        }

        internal static List<KeyValuePair<string, string>> VaultsIn(string json)
        {
            var vaults = new List<KeyValuePair<string, string>>();
            try
            {
                using (var read = JsonDocument.Parse(json))
                {
                    if (read.RootElement.ValueKind != JsonValueKind.Object || !read.RootElement.TryGetProperty("vaults", out var listed) || listed.ValueKind != JsonValueKind.Object) return vaults;
                    foreach (var entry in listed.EnumerateObject())
                    {
                        if (entry.Value.ValueKind != JsonValueKind.Object || !entry.Value.TryGetProperty("path", out var where) || where.ValueKind != JsonValueKind.String) continue;
                        string path = where.GetString();
                        if (string.IsNullOrEmpty(path) || !Path.IsPathFullyQualified(path) || !Directory.Exists(path)) continue;
                        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
                        vaults.Add(new KeyValuePair<string, string>(Path.GetFileName(path), path));
                    }
                }
            }
            catch (JsonException) { }
            catch (ArgumentException) { }
            catch (NotSupportedException) { }
            vaults.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return vaults;
        }
    }
}
