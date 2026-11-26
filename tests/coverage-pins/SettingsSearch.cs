using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

internal sealed class Row
{
    internal string Type, Label, Name;
    internal int Line;
}

internal sealed class AliasGroup
{
    internal string Target;
    internal readonly List<string> Words = new List<string>();
    internal int Line;
}

internal sealed class Contract
{
    internal string App, Setting, Cross;
    // Keys the cross search adds beyond Windows', because its row is worded differently. Everything else must match
    // both ways, so a key dropped on either side fails the pin.
    internal string[] Extra;
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
        new[] { "Lite mode", "lite mode", "slow", "lag", "laggy" },
        new[] { "Brain API key", "api key", "key" },
        new[] { "Free key", "api key", "key" },
        new[] { "ChatGPT plan", "api key", "key" },
        new[] { "Plug-ins", "email", "plug-ins", "plugins", "plug-in", "plugin" }
    };

    // The cross-file contract: each setting Windows gives keys must reach the same setting on the
    // cross side with the same words. Cross is null when the cross Mainframe has no such setting;
    // if one appears, the pin fails and asks for it here rather than letting the two drift apart.
    private static readonly Contract[] Contracts =
    {
        new Contract { App = "mics", Setting = "Mic: default", Cross = null },
        new Contract { App = "textEntry", Setting = "Brain API key", Cross = "Brain API key" },
        new Contract { App = "freeKey", Setting = "Free key", Cross = "Free key" },
        new Contract { App = "planKey", Setting = "API key, Claude plan or ChatGPT plan", Cross = "ChatGPT plan" },
        new Contract { App = "comments", Setting = "Comments", Cross = null },
        new Contract { App = "listenCapri", Setting = "Listen for Capri", Cross = "Listen for Hey Capri" },
        new Contract { App = "volume", Setting = "Volume", Cross = "Volume" },
        new Contract { App = "startup", Setting = "Start with Windows", Cross = "Start at login", Extra = new[] { "login" } },
        new Contract { App = "languages", Setting = "I speak", Cross = "Language" },
        new Contract { App = "liteMode", Setting = "Lite mode", Cross = "Lite mode" },
        new Contract { App = "PlugTile", Setting = "Plug-ins", Cross = "Plug-ins" }
    };

    internal static void Check(string root, Action<string> pass)
    {
        string appPath = Path.Combine(root, "app/MainframeSearch.cs");
        Require(File.Exists(appPath), "app/MainframeSearch.cs is not at the checkout root, so the cross-file pin has nothing to compare");
        string app = File.ReadAllText(appPath);
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

        HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
        foreach (Row row in rows)
        {
            Require(Plain(row.Name).Length > 0, "the " + row.Type + " row at Mainframe.cs:" + row.Line + " has no name she can search");
            Require(Covered(row.Type, findable, bases),
                "the " + row.Type + " row '" + row.Label + "' at Mainframe.cs:" + row.Line + " is not a kind her search index takes, so its key finds nothing");
            known.Add(row.Name);
        }

        Require(Covered("PlugTile", findable, bases), "her PlugTile row is no longer a kind her search index takes, so the plug-in key finds nothing");
        known.Add(plugName);
        foreach (string target in targets)
            Require(known.Contains(target), "the search alias '" + target + "' in MainframeSearch.cs is not the exact label of a row her search index takes, and her search attaches aliases by exact text, so its words never reach that setting");

        HashSet<string> pairs = new HashSet<string>();
        foreach (KeyValuePair<string, string> key in keys)
        {
            string word = Plain(key.Value);
            Require(key.Value.Length > 0 && word.Length > 0, "the search alias '" + key.Key + "' in MainframeSearch.cs has an empty word, so it can never be typed");
            Require(pairs.Add(key.Key + "|" + word), "the search alias word '" + key.Value + "' is listed twice for '" + key.Key + "' in MainframeSearch.cs");
        }
        int shared, sharedWords, absent, absentWords;
        CrossContract(app, search, known, keys, out shared, out sharedWords, out absent, out absentWords);
        ShippedKeys(pairs);

        pass("settings search: all " + rows.Count + " hand-labelled rows of her Mainframe are kinds her search index takes, all "
            + targets.Count + " aliases over " + keys.Count + " keys name a row by its exact label, the shipped keys are intact, and the cross-file contract holds: "
            + shared + " shared settings carry " + sharedWords + " keys word for word from Windows, and " + absent + " Windows-only settings with " + absentWords
            + " keys stay absent cross-side");
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

    private static List<AliasGroup> AppAliases(string search)
    {
        List<AliasGroup> groups = new List<AliasGroup>();
        Regex also = new Regex(@"Also\(\s*index\s*,\s*([^,]+?)\s*,\s*(-?\d+)\s*((?:,\s*""[^""]*""\s*)*)\)");
        foreach (Match match in also.Matches(search))
        {
            AliasGroup group = new AliasGroup { Target = match.Groups[1].Value, Line = LineAt(search, match.Index) };
            foreach (Match word in Regex.Matches(match.Groups[3].Value, @"""([^""]*)"""))
                group.Words.Add(word.Groups[1].Value);
            groups.Add(group);
        }
        Match plug = Regex.Match(search, @"if \(control is PlugTile[^)]*\)\s*foreach \(string word in new\[\]\s*\{([^}]*)\}\)");
        Require(plug.Success, "app/MainframeSearch.cs no longer gives her PlugTile row its words, so the cross-file pin has nothing to compare");
        AliasGroup tile = new AliasGroup { Target = "PlugTile", Line = LineAt(search, plug.Index) };
        foreach (Match word in Regex.Matches(plug.Groups[1].Value, @"""([^""]*)"""))
            tile.Words.Add(word.Groups[1].Value);
        groups.Add(tile);
        return groups;
    }

    private static void CrossContract(string app, string cross, HashSet<string> known, List<KeyValuePair<string, string>> crossKeys,
        out int shared, out int sharedWords, out int absent, out int absentWords)
    {
        List<AliasGroup> appGroups = AppAliases(app);
        Dictionary<string, AliasGroup> byTarget = new Dictionary<string, AliasGroup>(StringComparer.Ordinal);
        foreach (AliasGroup group in appGroups)
        {
            Require(group.Words.Count > 0,
                "app/MainframeSearch.cs gives '" + group.Target + "' no keys at line " + group.Line + ", so the cross-file pin has nothing to compare");
            AliasGroup first;
            Require(!byTarget.TryGetValue(group.Target, out first),
                "app/MainframeSearch.cs gives '" + group.Target + "' its keys at lines " + (first == null ? 0 : first.Line) + " and " + group.Line
                + ", so the cross-file pin cannot tell which keys it means");
            byTarget[group.Target] = group;
        }
        Dictionary<string, List<string>> theirGroups = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> key in crossKeys)
        {
            List<string> words;
            if (!theirGroups.TryGetValue(key.Key, out words)) { words = new List<string>(); theirGroups[key.Key] = words; }
            words.Add(key.Value);
        }
        shared = 0;
        sharedWords = 0;
        absent = 0;
        absentWords = 0;
        HashSet<string> classified = new HashSet<string>(StringComparer.Ordinal);
        foreach (Contract one in Contracts)
        {
            Require(classified.Add(one.App), "the cross-file pin classifies '" + one.App + "' twice, so it cannot tell which setting that is");
            AliasGroup group;
            Require(byTarget.TryGetValue(one.App, out group),
                "app/MainframeSearch.cs no longer gives '" + one.Setting + "' the keys this pin records for '" + one.App + "', so the cross-file table must name the setting's new control");
            if (one.Cross == null)
            {
                foreach (string other in known)
                    Require(Plain(other) != Plain(one.Setting),
                        "cross/Mainframe.cs now has the row '" + other + "', so the cross Mainframe has the setting app/MainframeSearch.cs gives '" + one.Setting
                        + "' its keys for; classify '" + one.App + "' SHARED and carry its keys in cross/MainframeSearch.cs");
                foreach (string other in theirGroups.Keys)
                    Require(Plain(other) != Plain(one.Setting),
                        "cross/MainframeSearch.cs now gives '" + other + "' keys, so the cross Mainframe has the setting app/MainframeSearch.cs gives '" + one.Setting
                        + "' its keys for; classify '" + one.App + "' SHARED and carry its keys");
                absent++;
                absentWords += group.Words.Count;
                continue;
            }
            Require(known.Contains(one.Cross),
                "app/MainframeSearch.cs gives '" + one.Setting + "' its keys, but cross/Mainframe.cs has no row named '" + one.Cross
                + "', so the setting cross/MainframeSearch.cs names there has gone or been renamed");
            List<string> words;
            Require(theirGroups.TryGetValue(one.Cross, out words),
                "app/MainframeSearch.cs gives '" + one.Setting + "' its keys, but cross/MainframeSearch.cs no longer gives '" + one.Cross
                + "' any keys, so those words reach nothing there");
            foreach (string word in group.Words)
            {
                bool found = false;
                foreach (string other in words) if (other == word) { found = true; break; }
                Require(found,
                    "app/MainframeSearch.cs gives '" + one.Setting + "' the key '" + word + "' at line " + group.Line
                    + ", but cross/MainframeSearch.cs has no such key on '" + one.Cross + "'; both searches must answer the same words");
                sharedWords++;
            }
            foreach (string other in words)
            {
                bool found = false;
                foreach (string word in group.Words) if (word == other) { found = true; break; }
                bool extra = false;
                if (one.Extra != null) foreach (string word in one.Extra) if (word == other) { extra = true; break; }
                Require(found || extra,
                    "cross/MainframeSearch.cs gives '" + one.Cross + "' the key '" + other + "', but app/MainframeSearch.cs has no such key for '"
                    + one.Setting + "' and it is not one of its cross-only keys; both searches must answer the same words");
            }
            shared++;
        }
        foreach (AliasGroup group in appGroups)
            Require(classified.Contains(group.Target),
                "app/MainframeSearch.cs gives '" + group.Target + "' keys at line " + group.Line
                + " that this pin does not classify; add it to the cross-file table as SHARED, naming the cross row it reaches, or ABSENT when the cross Mainframe has no such setting");
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
