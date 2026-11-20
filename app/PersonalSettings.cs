using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class PersonalSettings
    {
        internal const string FolderName = "personalSettings";
        internal const string SysName = "sys.md";
        internal const string NeverWatchKey = "never watch";
        internal const string AskKey = "ask about me";

        internal static string Folder;

        private static readonly object gate = new object();
        private static readonly Dictionary<string, string> said = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string readFrom;
        private static DateTime readAt = DateTime.MinValue;
        private static long readLength = -1;

        private const string Head =
            "# Her system settings\n" +
            "> Capri reads this while she runs. Change a line and save, and she uses it straight away.\n";

        private static readonly string[,] Sections =
        {
            {
                NeverWatchKey,
                "## Never watch these\n\n" +
                "> Programs Watch my screen skips. Comma separated, and part of a name is enough:\n" +
                "> \"discord\" covers Discord.exe. Matched on the program and on its window title.\n" +
                "> She doesn't look, and says nothing, while one of these is in front.\n\n" +
                NeverWatchKey + ": \n"
            },
            {
                AskKey,
                "## Getting to know you\n\n" +
                "> 1: now and then, after you've been chatting, she asks about you and keeps your answer in me.md.\n" +
                "> 0: she never asks.\n\n" +
                AskKey + ": 1\n"
            },
            {
                Doomscroll.ListKey,
                "## Doomscroll guard\n\n" +
                "> Sites and apps Doomscroll guard keeps time on, once it's on in Mainframe > Settings > DOOMSCROLL.\n" +
                "> Comma separated, matched on the window title: tiktok, youtube shorts, instagram, reddit and x\n" +
                "> are known, and any other word counts when it's in the title or the program's name.\n\n" +
                Doomscroll.ListKey + ": " + Doomscroll.Stock + "\n"
            }
        };

        internal static string SysPath { get { string folder = Folder; return folder == null ? null : Path.Combine(folder, SysName); } }

        internal static string Stock()
        {
            StringBuilder made = new StringBuilder(Head);
            for (int each = 0; each < Sections.GetLength(0); each++) made.Append("\n").Append(Sections[each, 1]);
            return made.ToString();
        }

        internal static string Make()
        {
            string path = SysPath;
            if (path == null) return null;
            lock (gate)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    if (!File.Exists(path)) { File.WriteAllText(path, Stock(), new UTF8Encoding(false)); Forget(); return path; }
                    string had = File.ReadAllText(path);
                    Dictionary<string, string> keys = Parse(had);
                    StringBuilder adding = new StringBuilder();
                    for (int each = 0; each < Sections.GetLength(0); each++)
                        if (!keys.ContainsKey(Sections[each, 0])) adding.Append("\n").Append(Sections[each, 1]);
                    if (adding.Length > 0)
                    {
                        string ending = had.Contains("\r\n") ? "\r\n" : "\n";
                        if (had.Length > 0 && !had.EndsWith("\n", StringComparison.Ordinal)) had += ending;
                        File.WriteAllText(path, had + adding.ToString().Replace("\n", ending), new UTF8Encoding(false));
                        Forget();
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return path;
        }

        private static void Forget() { readAt = DateTime.MinValue; readLength = -1; readFrom = null; }

        internal static Dictionary<string, string> Parse(string text)
        {
            Dictionary<string, string> found = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in (text ?? "").Split('\n'))
            {
                string row = raw.TrimEnd('\r').Trim();
                if (row.Length == 0 || row.StartsWith("#", StringComparison.Ordinal) || row.StartsWith(">", StringComparison.Ordinal)) continue;
                Match pair = Regex.Match(row, @"^-?[ \t]*([A-Za-z][A-Za-z ]*?)[ \t]*[:=][ \t]*(.*)$");
                if (!pair.Success) continue;
                string key = Regex.Replace(pair.Groups[1].Value, @"\s+", " ").Trim();
                if (!found.ContainsKey(key)) found[key] = pair.Groups[2].Value.Trim();
            }
            return found;
        }

        internal static string Of(string key)
        {
            string path = SysPath;
            if (path == null) return null;
            lock (gate)
            {
                try
                {
                    FileInfo file = new FileInfo(path);
                    if (!file.Exists) { said.Clear(); Forget(); return null; }
                    if (readFrom != path || file.LastWriteTimeUtc != readAt || file.Length != readLength)
                    {
                        Dictionary<string, string> fresh = Parse(File.ReadAllText(path));
                        said.Clear();
                        foreach (KeyValuePair<string, string> pair in fresh) said[pair.Key] = pair.Value;
                        readFrom = path;
                        readAt = file.LastWriteTimeUtc;
                        readLength = file.Length;
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                string value;
                return said.TryGetValue(key, out value) ? value : null;
            }
        }

        internal static string[] NeverWatch()
        {
            string list = Of(NeverWatchKey);
            List<string> found = new List<string>();
            if (string.IsNullOrWhiteSpace(list)) return found.ToArray();
            foreach (string one in list.Split(',', ';'))
            {
                string name = (one ?? "").Trim().Trim('"', '\'');
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name.Substring(0, name.Length - 4);
                if (name.Length >= 2) found.Add(name);
            }
            return found.ToArray();
        }

        internal static bool Skipped(string program, string title)
        {
            foreach (string one in NeverWatch())
            {
                if (!string.IsNullOrEmpty(program) && program.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (!string.IsNullOrEmpty(title) && title.IndexOf(one, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }

        internal static bool AsksAboutThem
        {
            get
            {
                string value = (Of(AskKey) ?? "").Trim();
                return !Regex.IsMatch(value, @"^(0|off|no|false|never)$", RegexOptions.IgnoreCase);
            }
        }
    }
}
