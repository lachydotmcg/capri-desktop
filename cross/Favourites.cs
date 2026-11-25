using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CapriDesktop
{
    // Which app they reach for by kind, learned from the app in front, like Windows (app/Favourites.cs). Windows keeps
    // this in apps-used.txt as tab-separated rows, and so does this: faded, then a count for each program. Mac has no
    // front-app signal in the shared surface, so only Linux learns; the rest of the capability works on both.
    internal static class Favourites
    {
        internal const string FileName = "apps-used.txt";
        internal static string Kept;

        internal static readonly bool Learning = !OperatingSystem.IsMacOS();

        internal static Func<DateTime> Now = delegate { return DateTime.Now; };
        internal static Func<string> Front = delegate { return FrontProgram(); };

        private static readonly object gate = new object();
        private static readonly Dictionary<string, double> counts = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> others = new List<string>();
        private static bool read;
        private static int sampling, stopping, observationVersion;
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

        internal static string Best(string kind)
        {
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
            lock (gate) { marked = plain; markedAt = Now(); observationVersion++; }
        }

        // What the app in front is called: the same program name her window list uses, so Best can find its app.
        internal static string FrontProgram()
        {
            if (!Learning) return null;
            try
            {
                IntPtr front = Hands.Foreground();
                if (front == IntPtr.Zero) return null;
                foreach (OpenWindow window in Hands.Windows())
                    if (window.Handle == front) return window.Program;
            }
            catch (Exception) { }
            return null;
        }

        internal static void Sample()
        {
            if (!Learning || Volatile.Read(ref stopping) != 0) return;
            int version;
            lock (gate) version = observationVersion;
            string program = Front();
            lock (gate)
            {
                if (stopping == 0 && observationVersion == version) Seen(program, Now());
            }
        }

        // A slow desktop command or file write can outlive several ticks. Keep only one sample in flight.
        internal static Task SampleInBackground()
        {
            if (!Learning || Volatile.Read(ref stopping) != 0 || Interlocked.CompareExchange(ref sampling, 1, 0) != 0)
                return null;
            return Task.Run(() =>
            {
                try { Sample(); }
                catch (Exception) { }
                finally { Volatile.Write(ref sampling, 0); }
            });
        }

        internal static void Save() { lock (gate) { if (read) Write(); } }

        internal static void StopAndSave()
        {
            Volatile.Write(ref stopping, 1);
            Save();
        }

        // Start over from what's on disk, which is also how a check reads its own file back.
        internal static void Reload()
        {
            lock (gate)
            {
                observationVersion++;
                counts.Clear();
                marked = null;
                markedAt = DateTime.MinValue;
                faded = DateTime.MinValue;
                saved = DateTime.MinValue;
                read = false;
            }
        }

        // Her own opens arrive as an app id, argv: for a .desktop entry or exec:, not a program name, so they're
        // reduced the same way the sampler's window program is: the executable without its folder or extension.
        private static string Plain(string program)
        {
            string name = (program ?? "").Trim();
            if (name.Length == 0) return "";
            if (name.StartsWith("argv:", StringComparison.Ordinal))
            {
                try
                {
                    string[] argv = JsonSerializer.Deserialize<string[]>(name.Substring(5));
                    if (argv != null && argv.Length > 0) name = argv[0];
                }
                catch (Exception) { }
            }
            else if (name.StartsWith("exec:", StringComparison.Ordinal)) name = name.Substring(5);
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
            others.Clear();
            try
            {
                if (string.IsNullOrEmpty(Kept) || !File.Exists(Kept)) return;
                foreach (string row in File.ReadAllLines(Kept))
                {
                    string line = row ?? "";
                    if (line.StartsWith("faded\t", StringComparison.OrdinalIgnoreCase))
                    {
                        DateTime when;
                        if (DateTime.TryParse(line.Substring(6), CultureInfo.InvariantCulture, DateTimeStyles.None, out when)) faded = when.Date;
                        continue;
                    }
                    int tab = line.LastIndexOf('\t');
                    double count;
                    if (tab > 0 && double.TryParse(line.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out count)
                        && !line.StartsWith("pick:", StringComparison.OrdinalIgnoreCase) && !line.StartsWith("notthat:", StringComparison.OrdinalIgnoreCase))
                    {
                        counts[line.Substring(0, tab)] = count;
                        continue;
                    }
                    // Only a truly empty row is blank: every other row is kept exactly as it was read.
                    if (line.Length > 0) others.Add(line);
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
                foreach (string one in others) made.Append(one).Append(Environment.NewLine);
                foreach (KeyValuePair<string, double> one in counts)
                    made.Append(one.Key).Append('\t').Append(one.Value.ToString("0.##", CultureInfo.InvariantCulture)).Append(Environment.NewLine);
                File.WriteAllText(Kept, made.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }
    }
}
