using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using System.Text;

namespace CapriDesktop
{
    internal static class Favourites
    {
        internal const string FileName = "apps-used.txt";
        internal static string Kept;

        private static readonly object gate = new object();
        private static readonly Dictionary<string, double> counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> picks = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> refused = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static bool read;
        private static string marked;
        private static DateTime markedAt = DateTime.MinValue, faded = DateTime.MinValue, saved = DateTime.MinValue;

        private const double Fade = 0.95;
        private const double Enough = 3.0;
        private static readonly TimeSpan Spell = TimeSpan.FromMinutes(4);

        private static readonly Dictionary<string, string[]> Kinds = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "browser", new[] { "brave", "chrome", "firefox", "msedge", "edge", "opera", "vivaldi", "librewolf", "waterfox", "zen" } },
            { "calendar", new[] { "outlook", "calendar", "thunderbird", "em client", "emclient", "mailbird", "morgen", "notion" } },
            { "music", new[] { "spotify", "foobar", "musicbee", "winamp", "aimp", "itunes", "applemusic", "deezer", "tidal", "vlc" } },
            { "images", new[] { "qview", "irfanview", "xnview", "honeyview", "nomacs", "imageglass", "photos", "paint.net", "photoshop" } },
            { "editor", new[] { "code", "codium", "notepad++", "sublime", "rider", "devenv", "idea", "pycharm", "notepad" } },
            { "chat", new[] { "discord", "slack", "teams", "telegram", "signal", "whatsapp" } }
        };

        private static readonly Dictionary<string, string> Called = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "browser", "browser" }, { "calendar", "calendar" }, { "music", "music player" },
            { "images", "image viewer" }, { "editor", "editor" }, { "chat", "chat app" }
        };

        private static readonly Dictionary<string, string> Asked = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "browser", "browser" }, { "web browser", "browser" }, { "internet", "browser" },
            { "calendar", "calendar" }, { "diary", "calendar" }, { "schedule", "calendar" },
            { "music", "music" }, { "music player", "music" }, { "music app", "music" }, { "tunes", "music" },
            { "images", "images" }, { "image viewer", "images" }, { "photo viewer", "images" }, { "gallery", "images" },
            { "editor", "editor" }, { "code editor", "editor" }, { "text editor", "editor" }, { "ide", "editor" },
            { "chat", "chat" }, { "chat app", "chat" }, { "messenger", "chat" }
        };

        internal static string KindOf(string said)
        {
            if (string.IsNullOrWhiteSpace(said)) return null;
            string want = said.Trim().ToLowerInvariant();
            want = Regex.Replace(want, @"^(?:please\s+|open\s+|launch\s+|start\s+|run\s+)+", "");
            want = Regex.Replace(want, @"^(?:my|our|the|a|some)\s+", "");
            want = Regex.Replace(want, @"\s+(?:please|now|up|for me)$", "");
            want = Regex.Replace(want, @"[^a-z ]", "").Trim();
            string kind;
            return Asked.TryGetValue(want, out kind) ? kind : null;
        }

        internal static string Name(string kind)
        {
            string said;
            return Called.TryGetValue(kind ?? "", out said) ? said : kind;
        }

        internal static List<string> Choices(string kind)
        {
            List<string> found = new List<string>();
            string[] names;
            if (string.IsNullOrEmpty(kind) || !Kinds.TryGetValue(kind, out names)) return found;
            lock (gate)
            {
                Load();
                List<KeyValuePair<string, double>> all = new List<KeyValuePair<string, double>>();
                foreach (KeyValuePair<string, double> one in counts)
                    foreach (string name in names)
                        if (one.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { all.Add(one); break; }
                all.Sort(delegate(KeyValuePair<string, double> x, KeyValuePair<string, double> y) { return y.Value.CompareTo(x.Value); });
                foreach (KeyValuePair<string, double> one in all) found.Add(one.Key);
            }
            return found;
        }

        internal static void Pick(string kind, string program)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(program)) return;
            lock (gate) { Load(); picks[kind] = Plain(program); Write(); }
        }

        internal static void NotThat(string kind, string program)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(program)) return;
            lock (gate)
            {
                Load();
                string had;
                string plain = Plain(program);
                refused[kind] = refused.TryGetValue(kind, out had) && had.Length > 0 ? had + "|" + plain : plain;
                Write();
            }
        }

        private static bool Turned(string kind, string program)
        {
            string had;
            if (!refused.TryGetValue(kind ?? "", out had) || string.IsNullOrEmpty(had)) return false;
            foreach (string one in had.Split('|'))
                if (string.Equals(one, program, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        internal static string Ask(string kind)
        {
            if (!string.IsNullOrEmpty(Picked(kind))) return null;
            List<string> all = Choices(kind);
            List<string> worth = new List<string>();
            lock (gate)
            {
                foreach (string one in all)
                {
                    double had;
                    if (counts.TryGetValue(one, out had) && had >= 2 && !Turned(kind, one)) worth.Add(one);
                }
            }
            return worth.Count >= 2 ? worth[0] : null;
        }

        internal static string Picked(string kind)
        {
            lock (gate)
            {
                Load();
                string had;
                return picks.TryGetValue(kind ?? "", out had) && had.Length > 0 ? had : null;
            }
        }

        internal static string Best(string kind)
        {
            string told = Picked(kind);
            if (told != null) return told;
            string[] names;
            if (string.IsNullOrEmpty(kind) || !Kinds.TryGetValue(kind, out names)) return null;
            lock (gate)
            {
                Load();
                string best = null;
                double most = Enough;
                foreach (KeyValuePair<string, double> one in counts)
                {
                    if (one.Value < most) continue;
                    foreach (string name in names)
                        if (one.Key.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { best = one.Key; most = one.Value; break; }
                }
                return best;
            }
        }

        internal static void Seen(string program, DateTime now)
        {
            string plain = Plain(program);
            if (plain.Length == 0) return;
            lock (gate)
            {
                Load();
                Decay(now);
                if (string.Equals(plain, marked, StringComparison.OrdinalIgnoreCase) && now - markedAt < Spell) return;
                marked = plain;
                markedAt = now;
                double had;
                counts[plain] = (counts.TryGetValue(plain, out had) ? had : 0) + 1;
                if (now - saved >= TimeSpan.FromMinutes(5)) { saved = now; Write(); }
            }
        }

        internal static void SheOpened(string program)
        {
            string plain = Plain(program);
            if (plain.Length == 0) return;
            lock (gate) { marked = plain; markedAt = DateTime.Now; }
        }

        private static string Plain(string program)
        {
            string name = (program ?? "").Trim();
            if (name.Length == 0) return "";
            try { name = Path.GetFileNameWithoutExtension(name); }
            catch (Exception) { }
            return (name ?? "").Trim();
        }

        private static void Decay(DateTime now)
        {
            if (faded == DateTime.MinValue) { faded = now.Date; return; }
            int days = (int)(now.Date - faded).TotalDays;
            if (days <= 0) return;
            faded = now.Date;
            List<string> gone = new List<string>();
            List<string> keys = new List<string>(counts.Keys);
            foreach (string key in keys)
            {
                double left = counts[key] * Math.Pow(Fade, Math.Min(days, 90));
                if (left < 0.2) gone.Add(key); else counts[key] = left;
            }
            foreach (string key in gone) counts.Remove(key);
        }

        private static void Load()
        {
            if (read) return;
            read = true;
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                foreach (string row in File.ReadAllLines(Kept))
                {
                    int tab = (row ?? "").LastIndexOf('\t');
                    double count;
                    if (row.StartsWith("pick:", StringComparison.OrdinalIgnoreCase) || row.StartsWith("notthat:", StringComparison.OrdinalIgnoreCase)) continue;
                    if (tab <= 0 || !double.TryParse(row.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out count)) continue;
                    if (row.StartsWith("faded\t", StringComparison.OrdinalIgnoreCase)) continue;
                    counts[row.Substring(0, tab)] = count;
                }
                DateTime when;
                foreach (string row in File.ReadAllLines(Kept))
                {
                    if (row.StartsWith("faded\t", StringComparison.OrdinalIgnoreCase)
                        && DateTime.TryParse(row.Substring(6), CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) faded = when.Date;
                    else if (row.StartsWith("pick:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = row.Substring(5).Split('\t');
                        if (parts.Length == 2) picks[parts[0]] = parts[1];
                    }
                    else if (row.StartsWith("notthat:", StringComparison.OrdinalIgnoreCase))
                    {
                        string[] parts = row.Substring(8).Split('\t');
                        if (parts.Length == 2) refused[parts[0]] = parts[1];
                    }
                }
            }
            catch (Exception) { }
        }

        private static void Write()
        {
            try
            {
                if (string.IsNullOrEmpty(Kept)) return;
                StringBuilder made = new StringBuilder();
                made.Append("faded\t").Append(faded.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                foreach (KeyValuePair<string, string> one in picks)
                    made.Append("pick:").Append(one.Key).Append('\t').Append(one.Value).Append(Environment.NewLine);
                foreach (KeyValuePair<string, string> one in refused)
                    made.Append("notthat:").Append(one.Key).Append('\t').Append(one.Value).Append(Environment.NewLine);
                foreach (KeyValuePair<string, double> one in counts)
                    made.Append(one.Key).Append('\t').Append(one.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                File.WriteAllText(Kept, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        internal static void Save() { lock (gate) { if (read) Write(); } }
    }
}
