using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class HerVault
    {
        internal const string MemoriesName = "Memories";
        internal static int LastWrites;

        private static readonly Regex Private = new Regex(@"\b(passwords?|passcodes?|pin (code|number)|log ?in details|credit card|card number|bank (account|details|number)|account number|sort code|routing number|social security|passport|(home|street|postal|email|e-mail) address|(my|their|his|her) address|postcode|post code|zip code|(phone|mobile|cell) number|surname|last name|full name|real name|date of birth|(i|they) live (in|at|on)|(my|their|his|her) school(?: is|'s| is called| called)|school (is )?called)\b|@\w+\.\w+|\d[\d\s-]{6,}\d|\b\d{1,2}[/.-]\d{1,2}[/.-]\d{2,4}\b|(?i:\baddress(es)?\b)|\b\d{1,5}[A-Za-z]?\s+(?:[A-Z][\p{L}']*\s+){1,3}(?:Street|St|Road|Rd|Avenue|Ave|Lane|Drive|Court|Place|Crescent|Boulevard|Blvd|Way|Terrace|Close|Parade|Highway|Grove|Square)\b|\b(api[ _-]?key|secret|token|bearer|sk-[A-Za-z0-9]{8,})\b", RegexOptions.IgnoreCase);

        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        internal static string ObsidianLink(string notesFolder)
        {
            return "obsidian://open?path=" + Uri.EscapeDataString(Path.GetFullPath(notesFolder));
        }

        internal static int Mirror(string dataFolder, string notesFolder)
        {
            LastWrites = 0;
            if (string.IsNullOrEmpty(dataFolder) || string.IsNullOrEmpty(notesFolder)) return 0;
            try
            {
                string root = Path.Combine(notesFolder, MemoriesName);
                Directory.CreateDirectory(root);
                string obsidian = Path.Combine(notesFolder, ".obsidian");
                Directory.CreateDirectory(obsidian);
                string app = Path.Combine(obsidian, "app.json");
                if (!File.Exists(app)) File.WriteAllText(app, "{}", Utf8);

                Dictionary<string, string> want = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                List<string> index = new List<string>();

                Add(want, index, root, "About you", "me.md", Path.Combine(dataFolder, "me.md"), "What she knows about you, from me.md");
                Add(want, index, root, "From your chats", "memories.md", Path.Combine(dataFolder, "memories.md"), "What she wrote down from your chats, from memories.md");
                AddFolder(want, index, root, Path.Combine(dataFolder, "transcripts"), "Chats", "Chat ", "Chats");
                AddFolder(want, index, root, Path.Combine(dataFolder, "games"), "Games", "Game ", "Games");

                int count = want.Count;
                if (count > 0)
                {
                    StringBuilder home = new StringBuilder("# Her memories\n> Copied from what Capri remembers on this PC. Edit the originals in her app, not these copies.\n\n");
                    foreach (string line in index) home.Append(line).Append('\n');
                    want[Path.Combine(root, "Her memories.md")] = home.ToString();
                }

                foreach (KeyValuePair<string, string> pair in want) Save(pair.Key, pair.Value);
                Sweep(root, want);
                return count;
            }
            catch (IOException) { return 0; }
            catch (UnauthorizedAccessException) { return 0; }
        }

        private static void Add(Dictionary<string, string> want, List<string> index, string root, string title, string heading, string source, string about)
        {
            string text = Clean(source);
            if (text == null) return;
            want[Path.Combine(root, title + ".md")] = "# " + title + "\n> " + about + "\n\n" + text;
            index.Add("- [[" + MemoriesName + "/" + title + "|" + title + "]]");
        }

        private static void AddFolder(Dictionary<string, string> want, List<string> index, string root, string folder, string sub, string prefix, string label)
        {
            string[] files;
            try { if (!Directory.Exists(folder)) return; files = Directory.GetFiles(folder, "*.md"); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            bool first = true;
            foreach (string file in files)
            {
                string name = Path.GetFileNameWithoutExtension(file);
                string text = Clean(file);
                if (text == null) continue;
                string title = prefix + name;
                if (first) { index.Add(""); index.Add("## " + label); first = false; }
                want[Path.Combine(Path.Combine(root, sub), title + ".md")] = "# " + title + "\n\n" + text;
                index.Add("- [[" + MemoriesName + "/" + sub + "/" + title + "|" + title + "]]");
            }
        }

        private static string Clean(string path)
        {
            string[] lines;
            try
            {
                FileInfo info = new FileInfo(path);
                if (!info.Exists || info.Length > 2000000) return null;
                lines = File.ReadAllLines(path);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
            StringBuilder body = new StringBuilder();
            int started = 0;
            foreach (string line in lines)
            {
                if (started == 0 && (line.Trim().Length == 0 || line.StartsWith("# ", StringComparison.Ordinal) || line.StartsWith(">", StringComparison.Ordinal))) continue;
                started = 1;
                if (Private.IsMatch(line)) continue;
                body.Append(line).Append('\n');
            }
            return body.ToString();
        }

        private static void Save(string path, string text)
        {
            try
            {
                if (File.Exists(path) && string.Equals(File.ReadAllText(path), text, StringComparison.Ordinal)) return;
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, text, Utf8);
                LastWrites++;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static void Sweep(string root, Dictionary<string, string> want)
        {
            string[] files;
            try { files = Directory.GetFiles(root, "*.md", SearchOption.AllDirectories); }
            catch (IOException) { return; }
            catch (UnauthorizedAccessException) { return; }
            foreach (string file in files)
            {
                if (want.ContainsKey(file)) continue;
                try { File.Delete(file); LastWrites++; }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            try
            {
                foreach (string dir in Directory.GetDirectories(root))
                    if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        internal static string Check(string tempFolder)
        {
            try
            {
                string data = Path.Combine(tempFolder, "vault-data");
                string notes = Path.Combine(data, Path.Combine("her-computer", "notes"));
                if (Directory.Exists(data)) Directory.Delete(data, true);
                Directory.CreateDirectory(Path.Combine(data, "transcripts"));
                Directory.CreateDirectory(Path.Combine(data, "games"));
                File.WriteAllText(Path.Combine(data, "me.md"), "# What Capri remembers about you\n> She reads this every time you chat. Edit or delete anything here.\n\n- They like cats\n- Pronouns: she/her\n- Their password is hunter2\n");
                File.WriteAllText(Path.Combine(data, "memories.md"), "# What Capri remembers from your chats\n> She writes this herself from your chats and reads it every time you talk. Edit or delete anything here.\n\n## Wednesday 28 October 2026\n- They finally beat Malenia in Elden Ring after 40 tries.\n");
                File.WriteAllText(Path.Combine(data, "transcripts", "2026-10-28.md"), "# Capri and you, Wednesday 28 October 2026\n> Kept on this PC only.\n\n**10:00 You:** hi\n**10:00 Capri:** hey\n");
                File.WriteAllText(Path.Combine(data, "transcripts", "unfolded.txt"), "2026-10-28 10:00 You: hi\n");
                string gameFile = Path.Combine(data, "games", "fortnite.md");
                File.WriteAllText(gameFile, "# Fortnite\n\n- Sprint is shift\n");

                int first = Mirror(data, notes);
                string memories = Path.Combine(notes, MemoriesName);
                if (first != 4) return "Mirror returned " + first + " instead of 4.";
                if (!File.Exists(Path.Combine(notes, Path.Combine(".obsidian", "app.json")))) return "No .obsidian\\app.json.";
                string about = Path.Combine(memories, "About you.md");
                if (!File.Exists(about) || !File.ReadAllText(about).Contains("They like cats")) return "About you.md is missing or empty.";
                if (File.ReadAllText(about).Contains("hunter2")) return "A password was mirrored.";
                if (!File.Exists(Path.Combine(memories, "Her memories.md"))) return "No index note.";
                if (!File.Exists(Path.Combine(memories, Path.Combine("Games", "Game fortnite.md")))) return "No game note.";
                if (File.Exists(Path.Combine(memories, Path.Combine("Chats", "Chat unfolded.md")))) return "unfolded.txt was mirrored.";

                int second = Mirror(data, notes);
                if (second != 4 || LastWrites != 0) return "The second Mirror wrote " + LastWrites + " files.";

                File.Delete(gameFile);
                int third = Mirror(data, notes);
                if (third != 3) return "Mirror returned " + third + " after a source went.";
                if (File.Exists(Path.Combine(memories, Path.Combine("Games", "Game fortnite.md")))) return "A mirrored file stayed after its source went.";
                if (!File.Exists(Path.Combine(notes, Path.Combine(".obsidian", "app.json")))) return ".obsidian went missing.";

                string mine = Path.Combine(notes, "mine.md");
                File.WriteAllText(mine, "keep");
                Mirror(data, notes);
                if (!File.Exists(mine)) return "A note outside Memories was deleted.";

                if (Mirror(Path.Combine(tempFolder, "nowhere"), Path.Combine(tempFolder, "nowhere-notes")) != 0) return "A missing data folder didn't give 0.";
                if (!ObsidianLink(notes).StartsWith("obsidian://open?path=", StringComparison.Ordinal)) return "ObsidianLink is wrong.";
                return null;
            }
            catch (Exception problem) { return "HerVault check threw " + problem.GetType().Name + ": " + problem.Message; }
        }
    }
}
