using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class SpendRow
    {
        internal string Day, Kind, Model;
        internal int Asks, Unpriced;
        internal long Fresh, Cached, Stored, Written;
        internal decimal Cost;
        internal long Tokens { get { return Fresh + Cached + Stored + Written; } }
    }

    internal sealed class Price
    {
        internal readonly decimal Fresh, Cached, Stored, Written;
        internal Price(decimal fresh, decimal cached, decimal written) : this(fresh, cached, fresh * 1.25m, written) { }
        internal Price(decimal fresh, decimal cached, decimal stored, decimal written) { Fresh = fresh; Cached = cached; Stored = stored; Written = written; }
        internal decimal Of(long fresh, long cached, long stored, long written) { return (fresh * Fresh + cached * Cached + stored * Stored + written * Written) / 1000000m; }
        internal Price Halved { get { return new Price(Fresh / 2, Cached / 2, Stored / 2, Written / 2); } }
    }

    internal static class Spend
    {
        internal static string Folder;
        internal static event Action Changed;
        internal const string File = "spend.tsv";
        private const int KeptDays = 400;
        private static readonly object gate = new object();
        private static List<SpendRow> rows = new List<SpendRow>();
        private static string loaded;

        private static readonly KeyValuePair<Regex, Price>[] Prices =
        {
            Priced(@"^claude-(fable|mythos)-5-2", new Price(8m, 0.20m, 40m)),
            Priced(@"^claude-(fable|mythos)-5", new Price(10m, 1m, 50m)),
            Priced(@"^claude-opus-5-5", new Price(4m, 0.20m, 20m)),
            Priced(@"^claude-opus-(5|4)", new Price(5m, 0.50m, 25m)),
            Priced(@"^claude-sonnet-5-5", new Price(2m, 0.10m, 10m)),
            Priced(@"^claude-sonnet-5", new Price(2m, 0.20m, 10m)),
            Priced(@"^claude-sonnet-4", new Price(3m, 0.30m, 15m)),
            Priced(@"^claude-haiku-5-5", new Price(0.10m, 0.01m, 0.50m)),
            Priced(@"^claude-haiku-4", new Price(1m, 0.10m, 5m)),
            Priced(@"^gpt-6\.2-astra", new Price(8m, 0.40m, 8m, 40m)),
            Priced(@"^gpt-[\d.]+-astra", new Price(10m, 1m, 10m, 50m)),
            Priced(@"^gpt-[\d.]+-sol", new Price(2m, 0.10m, 2m, 10m)),
            Priced(@"^gpt-[\d.]+-terra", new Price(0.50m, 0.025m, 0.50m, 2.50m)),
            Priced(@"^gpt-[\d.]+-luna", new Price(0.05m, 0.005m, 0.05m, 0.25m)),
            Priced(@"^gpt-[\d.]+-rosalind", new Price(10m, 1m, 10m, 50m)),
            Priced(@"^gpt-[\d.]+-cyber", new Price(20m, 2m, 20m, 100m)),
            Priced(@"^chat-latest", new Price(5m, 0.50m, 5m, 30m)),
            Priced(@"^gemini-3\.5-flash(?!-lite)", new Price(1.50m, 0.15m, 1.50m, 9m)),
            Priced(@"^gemini-3\.1-flash-lite", new Price(0.25m, 0.025m, 0.25m, 1.50m)),
            Priced(@"^gemini-3(\.1)?-pro", new Price(2m, 0.20m, 2m, 12m)),
            Priced(@"^gemini-3-flash(?!-lite)", new Price(0.50m, 0.05m, 0.50m, 3m)),
            Priced(@"^gemini-2\.5-pro", new Price(1.25m, 0.125m, 1.25m, 10m)),
            Priced(@"^gemini-2\.5-flash-lite", new Price(0.10m, 0.01m, 0.10m, 0.40m)),
            Priced(@"^gemini-2\.5-flash", new Price(0.30m, 0.03m, 0.30m, 2.50m)),
            Priced(@"^deepseek-v4-pro", new Price(1.32m, 0.044m, 1.32m, 3.96m)),
            Priced(@"^deepseek-(v4-)?flash", new Price(0.30m, 0.006m, 0.30m, 1.20m))
        };

        private static KeyValuePair<Regex, Price> Priced(string pattern, Price price) { return new KeyValuePair<Regex, Price>(new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant), price); }

        internal static bool Free(string kind, string model) { return kind == "nvidia" || (model ?? "").EndsWith(":free", StringComparison.Ordinal); }

        internal static Price PriceOf(string kind, string model, DateTime utc)
        {
            if (Free(kind, model)) return new Price(0, 0, 0, 0);
            if (kind == null) return null;
            Price listed = kind == "openrouter" ? Brain.RouterPrice(model) : null;
            if (listed != null) return listed;
            string bare = model ?? "";
            if (kind == "openrouter" || kind.StartsWith("other", StringComparison.Ordinal)) bare = bare.Substring(bare.LastIndexOf('/') + 1);
            foreach (KeyValuePair<Regex, Price> entry in Prices)
                if (entry.Key.IsMatch(bare)) return bare.StartsWith("deepseek-", StringComparison.OrdinalIgnoreCase) && !Peak(utc) ? entry.Value.Halved : entry.Value;
            return null;
        }

        internal static bool Peak(DateTime utc)
        {
            if (utc.DayOfWeek == DayOfWeek.Saturday || utc.DayOfWeek == DayOfWeek.Sunday) return false;
            return (utc.Hour >= 1 && utc.Hour < 4) || (utc.Hour >= 6 && utc.Hour < 10);
        }

        internal static string FileAt
        {
            get
            {
                string folder = Folder;
                string log = Brain.LogPath;
                if (folder == null && log != null) folder = Path.GetDirectoryName(log);
                return string.IsNullOrEmpty(folder) ? null : Path.Combine(folder, File);
            }
        }

        internal static void Add(string kind, string model, long fresh, long cached, long stored, long written, double told = -1)
        {
            if (string.IsNullOrEmpty(kind) || string.IsNullOrEmpty(model)) return;
            Price price = PriceOf(kind, model, DateTime.UtcNow);
            bool counted = fresh + cached + stored + written > 0;
            decimal cost = told >= 0 && told < 1000 ? (decimal)told : price != null ? price.Of(fresh, cached, stored, written) : 0;
            string day = DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            lock (gate)
            {
                Load();
                SpendRow row = rows.Find(delegate(SpendRow kept) { return kept.Day == day && kept.Kind == kind && kept.Model == model; });
                if (row == null) rows.Add(row = new SpendRow { Day = day, Kind = kind, Model = model });
                row.Asks++;
                row.Fresh += fresh;
                row.Cached += cached;
                row.Stored += stored;
                row.Written += written;
                row.Cost += cost;
                if (!counted || (price == null && told < 0)) row.Unpriced++;
                Save();
            }
            Action changed = Changed;
            if (changed != null) try { changed(); } catch (Exception) { }
        }

        private static void Load()
        {
            string path = FileAt;
            if (path == loaded) return;
            loaded = path;
            rows = new List<SpendRow>();
            if (path == null || !System.IO.File.Exists(path)) return;
            try
            {
                foreach (string line in System.IO.File.ReadAllLines(path, Encoding.UTF8))
                {
                    string[] parts = line.Split('\t');
                    if (parts.Length < 10) continue;
                    SpendRow row = new SpendRow { Day = parts[0], Kind = parts[1], Model = parts[2] };
                    if (!int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out row.Asks)) continue;
                    long.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out row.Fresh);
                    long.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out row.Cached);
                    long.TryParse(parts[6], NumberStyles.Integer, CultureInfo.InvariantCulture, out row.Stored);
                    long.TryParse(parts[7], NumberStyles.Integer, CultureInfo.InvariantCulture, out row.Written);
                    decimal.TryParse(parts[8], NumberStyles.Number, CultureInfo.InvariantCulture, out row.Cost);
                    int.TryParse(parts[9], NumberStyles.Integer, CultureInfo.InvariantCulture, out row.Unpriced);
                    rows.Add(row);
                }
            }
            catch (IOException error) { Brain.Log("spend", -1, error.Message); loaded = null; }
            catch (UnauthorizedAccessException error) { Brain.Log("spend", -1, error.Message); loaded = null; }
        }

        private static void Save()
        {
            string path = loaded;
            if (path == null) return;
            string oldest = DateTime.Now.AddDays(-KeptDays).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            rows.RemoveAll(delegate(SpendRow row) { return string.CompareOrdinal(row.Day, oldest) < 0; });
            StringBuilder text = new StringBuilder();
            foreach (SpendRow row in rows)
                text.Append(row.Day).Append('\t').Append(Clean(row.Kind)).Append('\t').Append(Clean(row.Model)).Append('\t')
                    .Append(row.Asks.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.Fresh.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.Cached.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.Stored.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.Written.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.Cost.ToString(CultureInfo.InvariantCulture)).Append('\t')
                    .Append(row.Unpriced.ToString(CultureInfo.InvariantCulture)).Append('\n');
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temporary = path + ".tmp";
                System.IO.File.WriteAllText(temporary, text.ToString(), new UTF8Encoding(false));
                if (System.IO.File.Exists(path)) System.IO.File.Replace(temporary, path, null);
                else System.IO.File.Move(temporary, path);
            }
            catch (IOException error) { Brain.Log("spend", -1, error.Message); }
            catch (UnauthorizedAccessException error) { Brain.Log("spend", -1, error.Message); }
        }

        private static string Clean(string text) { return Regex.Replace(text ?? "", @"[\t\r\n]", " "); }

        internal static void Forget()
        {
            lock (gate) { loaded = null; rows = new List<SpendRow>(); }
        }

        internal static List<SpendRow> Since(DateTime from)
        {
            string first = from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            List<SpendRow> found = new List<SpendRow>();
            lock (gate)
            {
                Load();
                foreach (SpendRow row in rows)
                {
                    if (string.CompareOrdinal(row.Day, first) < 0) continue;
                    SpendRow sum = found.Find(delegate(SpendRow kept) { return kept.Kind == row.Kind && kept.Model == row.Model; });
                    if (sum == null) found.Add(sum = new SpendRow { Day = row.Day, Kind = row.Kind, Model = row.Model });
                    sum.Asks += row.Asks;
                    sum.Unpriced += row.Unpriced;
                    sum.Fresh += row.Fresh;
                    sum.Cached += row.Cached;
                    sum.Stored += row.Stored;
                    sum.Written += row.Written;
                    sum.Cost += row.Cost;
                }
            }
            found.Sort(delegate(SpendRow a, SpendRow b)
            {
                int order = b.Cost.CompareTo(a.Cost);
                if (order == 0) order = b.Tokens.CompareTo(a.Tokens);
                return order != 0 ? order : string.CompareOrdinal(a.Model, b.Model);
            });
            return found;
        }

        internal static List<SpendRow> Today() { return Since(DateTime.Now.Date); }

        internal static List<SpendRow> Month() { DateTime now = DateTime.Now; return Since(new DateTime(now.Year, now.Month, 1)); }

        internal static decimal Total(List<SpendRow> found)
        {
            decimal sum = 0;
            foreach (SpendRow row in found) sum += row.Cost;
            return sum;
        }

        internal static string Money(decimal amount)
        {
            if (amount <= 0) return "$0";
            if (amount < 0.01m) return "<$0.01";
            return "$" + Math.Round(amount, 2).ToString(amount >= 1000 ? "#,0.00" : "0.00", CultureInfo.InvariantCulture);
        }

        internal static string Tokens(long tokens)
        {
            if (tokens >= 1000000) return (tokens / 1000000.0).ToString(tokens >= 10000000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "M";
            if (tokens >= 1000) return (tokens / 1000.0).ToString(tokens >= 10000 ? "0" : "0.#", CultureInfo.InvariantCulture) + "K";
            return tokens.ToString(CultureInfo.InvariantCulture);
        }

        internal static string Company(string kind)
        {
            if (kind == null) return "";
            if (kind.StartsWith("other:", StringComparison.Ordinal)) return kind.Substring(6);
            return Cloud.CompanyOf(kind);
        }

        internal static string Line()
        {
            return Money(Total(Today())) + " today · " + Money(Total(Month())) + " this month";
        }
    }
}
