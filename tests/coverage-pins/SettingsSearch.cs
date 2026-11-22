using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

internal sealed class Row
{
    internal string Type, Label, Name;
    internal int Line;
}

internal static class SettingsSearch
{
    private const string MainframeStart = "internal Mainframe(Pet owner)";
    private const string MainframeEnd = "BuildMods();";

    private static readonly string[][] RowCalls =
    {
        new[] { "Key", "NeonButton" },
        new[] { "Toggle", "NeonSwitch" },
        new[] { "Slider", "NeonSlider" },
        new[] { "Picker", "NeonPicker" },
        new[] { "Entry", "NeonEntry" }
    };

    private static readonly string[][] Shipped =
    {
        new[] { "Volume", "volume", "loud", "quiet", "louder", "quieter" },
        new[] { "Language", "language" },
        new[] { "Listen for Hey Capri", "listen", "wake word", "hey capri" },
        new[] { "Start at login", "startup", "start up", "start with windows", "login" },
        new[] { "Brain API key", "api key", "key" },
        new[] { "Free key", "api key", "key" },
        new[] { "Plug-ins", "email", "plug-ins", "plugins", "plug-in", "plugin" }
    };

    internal static void Check(string root, Action<string> pass)
    {
        string mainframe = File.ReadAllText(Path.Combine(root, "cross/Mainframe.cs"));
        string search = File.ReadAllText(Path.Combine(root, "cross/MainframeSearch.cs"));
        string theme = File.ReadAllText(Path.Combine(root, "cross/MainframeTheme.cs"));
        string plugins = File.ReadAllText(Path.Combine(root, "cross/MainframePlugins.cs"));

        int from = mainframe.IndexOf(MainframeStart, StringComparison.Ordinal);
        Require(from >= 0, "Mainframe.cs no longer builds her settings from the Mainframe constructor, so the pin has nothing to read");
        int to = mainframe.IndexOf(MainframeEnd, from, StringComparison.Ordinal);
        Require(to > from, "Mainframe.cs no longer ends its settings rows with BuildMods, so the pin has nothing to read");
        string region = mainframe.Substring(from, to - from);

        List<Row> rows = Rows(region, mainframe, from);
        Dictionary<string, string> bases = Bases(theme + "\n" + plugins);
        List<string> findable = Findable(search);
        string plugName = PlugName(search);
        List<string> targets = new List<string>();
        List<KeyValuePair<string, string>> keys = new List<KeyValuePair<string, string>>();
        Aliases(search, targets, keys);

        Require(rows.Count >= 60, "the pin found only " + rows.Count + " settings rows, so it stopped seeing her settings page");
        Require(findable.Count >= 1, "the pin found no index kinds, so it stopped seeing her search index");
        Require(targets.Count >= 1, "the pin found no search aliases, so it stopped seeing her hand-written keys");
        Require(keys.Count >= 1, "the pin found no search alias keys, so it stopped seeing her hand-written keys");

        Dictionary<string, string> names = new Dictionary<string, string>();
        foreach (Row row in rows)
        {
            string plain = Plain(row.Name);
            Require(plain.Length > 0, "the " + row.Type + " row at Mainframe.cs:" + row.Line + " has no name she can search");
            if (!names.ContainsKey(plain)) names[plain] = row.Type;
            Require(Covered(row.Type, findable, bases),
                "the " + row.Type + " row '" + row.Label + "' at Mainframe.cs:" + row.Line + " is not a kind her search index takes, so its key finds nothing");
        }

        Require(Covered("PlugTile", findable, bases), "her PlugTile row is no longer a kind her search index takes, so the plug-in key finds nothing");
        HashSet<string> known = new HashSet<string>(names.Keys);
        known.Add(Plain(plugName));
        foreach (string target in targets)
            Require(known.Contains(Plain(target)), "the search alias '" + target + "' in MainframeSearch.cs names a setting that isn't in her Mainframe, so it can never be found");

        HashSet<string> pairs = new HashSet<string>();
        foreach (KeyValuePair<string, string> key in keys)
        {
            string word = Plain(key.Value);
            Require(key.Value.Length > 0 && word.Length > 0, "the search alias '" + key.Key + "' in MainframeSearch.cs has an empty word, so it can never be typed");
            Require(pairs.Add(key.Key + "|" + word), "the search alias word '" + key.Value + "' is listed twice for '" + key.Key + "' in MainframeSearch.cs");
        }
        ShippedKeys(pairs);

        pass("settings search: all " + rows.Count + " hand-labelled rows of her Mainframe are kinds her search index takes, all "
            + targets.Count + " aliases over " + keys.Count + " keys name settings that are really there, and the shipped keys are intact");
    }

    private static List<Row> Rows(string region, string mainframe, int from)
    {
        List<Row> rows = new List<Row>();
        foreach (string[] call in RowCalls)
        {
            Regex pattern = new Regex(@"\b" + call[0] + @"\(\s*""([^""]*)""\s*[,)]");
            foreach (Match match in pattern.Matches(region))
            {
                if (match.Groups[1].Value.Length == 0) continue;
                rows.Add(new Row { Type = call[1], Label = match.Groups[1].Value, Name = match.Groups[1].Value, Line = LineAt(mainframe, from + match.Index) });
            }
        }
        Regex add = new Regex(@"\bAdd\(\s*new (Neon\w+)");
        foreach (Match match in add.Matches(region))
        {
            int open = region.IndexOf('(', match.Index);
            int close = open < 0 ? -1 : CloseParen(region, open);
            if (close < 0) continue;
            string statement = region.Substring(match.Index, close - match.Index + 1);
            Match named = Regex.Match(statement, @",\s*""([^""]*)""\s*\)$");
            if (!named.Success || named.Groups[1].Value.Length == 0) continue;
            Match text = Regex.Match(statement, @"\{\s*Text\s*=\s*""([^""]*)""");
            string name = text.Success && text.Groups[1].Value.Length > 0 ? text.Groups[1].Value : named.Groups[1].Value;
            rows.Add(new Row { Type = match.Groups[1].Value, Label = named.Groups[1].Value, Name = name, Line = LineAt(mainframe, from + match.Index) });
        }
        rows.Sort(delegate(Row one, Row other) { return one.Line.CompareTo(other.Line); });
        return rows;
    }

    private static Dictionary<string, string> Bases(string source)
    {
        Dictionary<string, string> bases = new Dictionary<string, string>();
        foreach (Match match in Regex.Matches(source, @"\bclass (\w+) : (\w+)"))
            bases[match.Groups[1].Value] = match.Groups[2].Value;
        return bases;
    }

    private static List<string> Findable(string search)
    {
        List<string> kinds = new List<string>();
        Match body = Regex.Match(search, @"bool Findable\(Control control\)\s*\{\s*return ([^;]+);");
        Require(body.Success, "MainframeSearch.cs no longer says which controls her search index takes, so the pin has nothing to read");
        foreach (Match kind in Regex.Matches(body.Groups[1].Value, @"control is (\w+)"))
            if (!kinds.Contains(kind.Groups[1].Value)) kinds.Add(kind.Groups[1].Value);
        return kinds;
    }

    private static string PlugName(string search)
    {
        Match name = Regex.Match(search, @"if \(control is PlugTile\) return ""([^""]*)"";");
        Require(name.Success && name.Groups[1].Value.Length > 0, "MainframeSearch.cs no longer names her PlugTile row, so the pin has nothing to read");
        return name.Groups[1].Value;
    }

    private static void Aliases(string search, List<string> targets, List<KeyValuePair<string, string>> keys)
    {
        Regex also = new Regex(@"Also\(\s*index\s*,\s*""([^""]+)""\s*,\s*(-?\d+)\s*((?:,\s*""[^""]*""\s*)*)\)");
        foreach (Match match in also.Matches(search))
        {
            string target = match.Groups[1].Value;
            targets.Add(target);
            foreach (Match word in Regex.Matches(match.Groups[3].Value, @"""([^""]*)"""))
                keys.Add(new KeyValuePair<string, string>(target, word.Groups[1].Value));
        }
        Match plug = Regex.Match(search, @"foreach \(string word in new\[\]\s*\{([^}]*)\}\)");
        Require(plug.Success, "MainframeSearch.cs no longer gives her PlugTile row its words, so the pin has nothing to read");
        foreach (Match word in Regex.Matches(plug.Groups[1].Value, @"""([^""]*)"""))
            keys.Add(new KeyValuePair<string, string>(PlugName(search), word.Groups[1].Value));
    }

    private static void ShippedKeys(HashSet<string> found)
    {
        HashSet<string> wanted = new HashSet<string>();
        foreach (string[] alias in Shipped)
            for (int i = 1; i < alias.Length; i++) wanted.Add(alias[0] + "|" + Plain(alias[i]));
        foreach (string[] alias in Shipped)
            for (int i = 1; i < alias.Length; i++)
            {
                string key = alias[0] + "|" + Plain(alias[i]);
                Require(found.Contains(key), "the search key '" + alias[i] + "' for '" + alias[0] + "' is gone from MainframeSearch.cs, so the setting it found no longer answers to it; if that is meant, drop it from this pin too");
            }
        foreach (string key in found)
            Require(wanted.Contains(key), "the search key '" + key.Substring(key.IndexOf('|') + 1) + "' for '" + key.Substring(0, key.IndexOf('|')) + "' is new in MainframeSearch.cs; add it to this pin's shipped keys so it cannot vanish later");
    }

    private static bool Covered(string type, List<string> kinds, Dictionary<string, string> bases)
    {
        string at = type;
        while (!string.IsNullOrEmpty(at))
        {
            if (kinds.Contains(at)) return true;
            string next;
            if (!bases.TryGetValue(at, out next)) return false;
            at = next;
        }
        return false;
    }

    private static int CloseParen(string text, int open)
    {
        int depth = 0;
        bool quoted = false;
        for (int i = open; i < text.Length; i++)
        {
            char c = text[i];
            if (quoted)
            {
                if (c == '\\') i++;
                else if (c == '"') quoted = false;
                continue;
            }
            if (c == '"') { quoted = true; continue; }
            if (c == '(') depth++;
            else if (c == ')') { depth--; if (depth == 0) return i; }
        }
        return -1;
    }

    private static int LineAt(string text, int at)
    {
        int line = 1;
        for (int i = 0; i < at && i < text.Length; i++) if (text[i] == '\n') line++;
        return line;
    }

    private static string Plain(string text)
    {
        return Regex.Replace((text ?? "").ToLowerInvariant().Replace("'", "").Replace("\u2019", ""), @"[^\p{L}\p{Nd}]+", " ").Trim();
    }

    private static void Require(bool good, string why)
    {
        if (!good) throw new Exception(why);
    }
}
