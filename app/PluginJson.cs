using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CapriDesktop
{
    internal static class PluginJson
    {
        private const int Deepest = 64;

        internal static object Parse(string text, bool strict = false)
        {
            string from = (text ?? "").TrimStart('\uFEFF');
            int at = 0;
            object value = Value(from, ref at, 0, strict);
            Space(from, ref at);
            if (at != from.Length) throw new FormatException("Extra text after the JSON at " + at + ".");
            return value;
        }

        internal static bool TryParse(string text, out object value)
        {
            try { value = Parse(text); return true; }
            catch (FormatException) { value = null; return false; }
        }

        private static void Space(string s, ref int at)
        {
            while (at < s.Length && (s[at] == ' ' || s[at] == '\t' || s[at] == '\r' || s[at] == '\n')) at++;
        }

        private static object Value(string s, ref int at, int depth, bool strict)
        {
            if (depth > Deepest) throw new FormatException("The JSON is nested too deep.");
            Space(s, ref at);
            if (at >= s.Length) throw new FormatException("The JSON ended early.");
            char c = s[at];
            if (c == '{')
            {
                Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
                at++;
                Space(s, ref at);
                if (at < s.Length && s[at] == '}') { at++; return map; }
                while (true)
                {
                    Space(s, ref at);
                    if (at >= s.Length || s[at] != '"') throw new FormatException("Expected a name in quotes at " + at + ".");
                    string key = Text(s, ref at, strict);
                    if (strict && map.ContainsKey(key)) throw new FormatException("Duplicate JSON property " + key + ".");
                    Space(s, ref at);
                    if (at >= s.Length || s[at] != ':') throw new FormatException("Expected : at " + at + ".");
                    at++;
                    map[key] = Value(s, ref at, depth + 1, strict);
                    Space(s, ref at);
                    if (at < s.Length && s[at] == ',') { at++; continue; }
                    if (at < s.Length && s[at] == '}') { at++; return map; }
                    throw new FormatException("Expected , or } at " + at + ".");
                }
            }
            if (c == '[')
            {
                List<object> list = new List<object>();
                at++;
                Space(s, ref at);
                if (at < s.Length && s[at] == ']') { at++; return list; }
                while (true)
                {
                    list.Add(Value(s, ref at, depth + 1, strict));
                    Space(s, ref at);
                    if (at < s.Length && s[at] == ',') { at++; continue; }
                    if (at < s.Length && s[at] == ']') { at++; return list; }
                    throw new FormatException("Expected , or ] at " + at + ".");
                }
            }
            if (c == '"') return Text(s, ref at, strict);
            if (Word(s, ref at, "true")) return true;
            if (Word(s, ref at, "false")) return false;
            if (Word(s, ref at, "null")) return null;
            int start = at;
            if (at < s.Length && s[at] == '-') at++;
            while (at < s.Length && (char.IsDigit(s[at]) || s[at] == '.' || s[at] == 'e' || s[at] == 'E' || s[at] == '+' || s[at] == '-')) at++;
            double number;
            if (at == start || !double.TryParse(s.Substring(start, at - start), NumberStyles.Float, CultureInfo.InvariantCulture, out number)) throw new FormatException("Unexpected character at " + start + ".");
            if (strict && !System.Text.RegularExpressions.Regex.IsMatch(s.Substring(start, at - start), @"\A-?(?:0|[1-9][0-9]*)(?:\.[0-9]+)?(?:[eE][+-]?[0-9]+)?\z"))
                throw new FormatException("Invalid JSON number.");
            return number;
        }

        private static bool Word(string s, ref int at, string word)
        {
            if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) return false;
            at += word.Length;
            return true;
        }

        private static string Text(string s, ref int at, bool strict)
        {
            StringBuilder made = new StringBuilder();
            at++;
            while (at < s.Length)
            {
                char c = s[at++];
                if (strict && c < 32) throw new FormatException("Invalid JSON string character.");
                if (c == '"') return made.ToString();
                if (c != '\\') { made.Append(c); continue; }
                if (at >= s.Length) break;
                char e = s[at++];
                switch (e)
                {
                    case '"': made.Append('"'); break;
                    case '\\': made.Append('\\'); break;
                    case '/': made.Append('/'); break;
                    case 'b': made.Append('\b'); break;
                    case 'f': made.Append('\f'); break;
                    case 'n': made.Append('\n'); break;
                    case 'r': made.Append('\r'); break;
                    case 't': made.Append('\t'); break;
                    case 'u':
                        int code;
                        if (at + 4 > s.Length || !int.TryParse(s.Substring(at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) throw new FormatException("A bad \\u escape at " + at + ".");
                        made.Append((char)code);
                        at += 4;
                        break;
                    default: throw new FormatException("A bad escape at " + at + ".");
                }
            }
            throw new FormatException("A string in the JSON never ends.");
        }

        internal static string Write(object value)
        {
            StringBuilder made = new StringBuilder();
            Write(made, value, 0);
            return made.ToString();
        }

        private static void Write(StringBuilder made, object value, int depth)
        {
            if (depth > Deepest) throw new FormatException("The JSON is nested too deep.");
            if (value == null) { made.Append("null"); return; }
            string text = value as string;
            if (text != null) { made.Append(Quote(text)); return; }
            if (value is bool) { made.Append((bool)value ? "true" : "false"); return; }
            if (value is double || value is float || value is decimal)
            {
                double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (double.IsNaN(number) || double.IsInfinity(number)) { made.Append("null"); return; }
                if (Math.Floor(number) == number && Math.Abs(number) < 9007199254740992d) made.Append(((long)number).ToString(CultureInfo.InvariantCulture));
                else made.Append(number.ToString("R", CultureInfo.InvariantCulture));
                return;
            }
            if (value is int || value is long || value is short || value is byte || value is uint || value is ulong) { made.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); return; }
            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null)
            {
                made.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> pair in map)
                {
                    if (!first) made.Append(',');
                    first = false;
                    made.Append(Quote(pair.Key)).Append(':');
                    Write(made, pair.Value, depth + 1);
                }
                made.Append('}');
                return;
            }
            System.Collections.IEnumerable list = value as System.Collections.IEnumerable;
            if (list != null)
            {
                made.Append('[');
                bool first = true;
                foreach (object item in list)
                {
                    if (!first) made.Append(',');
                    first = false;
                    Write(made, item, depth + 1);
                }
                made.Append(']');
                return;
            }
            made.Append(Quote(Convert.ToString(value, CultureInfo.InvariantCulture)));
        }

        internal static string Quote(string text)
        {
            StringBuilder made = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                switch (c)
                {
                    case '"': made.Append("\\\""); break;
                    case '\\': made.Append("\\\\"); break;
                    case '\n': made.Append("\\n"); break;
                    case '\r': made.Append("\\r"); break;
                    case '\t': made.Append("\\t"); break;
                    default:
                        if (c < ' ' || c == '\u2028' || c == '\u2029') made.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else made.Append(c);
                        break;
                }
            }
            return made.Append('"').ToString();
        }

        internal static Dictionary<string, object> Map(object value) { return value as Dictionary<string, object>; }

        internal static List<object> List(object value) { return value as List<object>; }

        internal static object Get(object value, string key)
        {
            Dictionary<string, object> map = Map(value);
            object found;
            return map != null && map.TryGetValue(key, out found) ? found : null;
        }

        internal static string Str(object value, string key) { return Get(value, key) as string; }

        internal static bool? Flag(object value, string key)
        {
            object found = Get(value, key);
            return found is bool ? (bool?)(bool)found : null;
        }

        internal static Dictionary<string, object> Make(params object[] pairs)
        {
            Dictionary<string, object> map = new Dictionary<string, object>(StringComparer.Ordinal);
            for (int i = 0; i + 1 < pairs.Length; i += 2) map[(string)pairs[i]] = pairs[i + 1];
            return map;
        }
    }
}
