using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class MailPart
    {
        internal Dictionary<string, string> Headers = new Dictionary<string, string>();
        internal string Body = "";
        internal List<MailPart> Parts = new List<MailPart>();

        internal string Header(string name)
        {
            string v;
            return Headers.TryGetValue(name.ToLowerInvariant(), out v) ? v : "";
        }
    }

    internal static class MailMime
    {
        static MailMime()
        {
#if CROSS
            try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); }
            catch (Exception) { }
#endif
        }

        internal static Encoding Charset(string name)
        {
            if (string.IsNullOrEmpty(name)) return Encoding.UTF8;
            int star = name.IndexOf('*');
            if (star > 0) name = name.Substring(0, star);
            try { return Encoding.GetEncoding(name.Trim().Trim('"')); }
            catch (Exception) { return Encoding.UTF8; }
        }

        internal static Dictionary<string, string> ParseHeaders(string block)
        {
            Dictionary<string, string> h = new Dictionary<string, string>();
            string unfolded = Regex.Replace(block, @"\r?\n[ \t]+", " ");
            foreach (string line in unfolded.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line.Substring(0, colon).Trim().ToLowerInvariant();
                if (!h.ContainsKey(name)) h[name] = line.Substring(colon + 1).Trim();
            }
            return h;
        }

        internal static MailPart Parse(string raw, int depth)
        {
            MailPart p = new MailPart();
            int split = FindBlank(raw);
            string head = split < 0 ? raw : raw.Substring(0, split);
            int bodyStart = raw.Length;
            if (split >= 0) bodyStart = split + (string.CompareOrdinal(raw, split, "\r\n\r\n", 0, 4) == 0 ? 4 : 2);
            p.Headers = ParseHeaders(head);
            p.Body = raw.Substring(Math.Min(bodyStart, raw.Length));
            string type = ContentType(p.Header("content-type"));
            if (type.StartsWith("multipart/") && depth < 6)
            {
                string boundary = Param(p.Header("content-type"), "boundary");
                if (boundary.Length > 0)
                {
                    foreach (string piece in SplitParts(p.Body, boundary)) p.Parts.Add(Parse(piece, depth + 1));
                }
            }
            return p;
        }

        private static int FindBlank(string s)
        {
            int a = s.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            int b = s.IndexOf("\n\n", StringComparison.Ordinal);
            if (a < 0) return b;
            if (b < 0) return a;
            return Math.Min(a, b);
        }

        private static List<string> SplitParts(string body, string boundary)
        {
            List<string> parts = new List<string>();
            string delim = "--" + boundary;
            int i = 0;
            int start = -1;
            while (i <= body.Length)
            {
                int at = body.IndexOf(delim, i, StringComparison.Ordinal);
                if (at < 0) break;
                bool lineStart = at == 0 || body[at - 1] == '\n';
                if (!lineStart) { i = at + delim.Length; continue; }
                if (start >= 0) parts.Add(Trim(body.Substring(start, at - start)));
                int after = at + delim.Length;
                if (after + 1 < body.Length && body[after] == '-' && body[after + 1] == '-') { start = -1; break; }
                int nl = body.IndexOf('\n', after);
                if (nl < 0) { start = -1; break; }
                start = nl + 1;
                i = start;
            }
            if (start >= 0 && start < body.Length) parts.Add(body.Substring(start));
            return parts;
        }

        private static string Trim(string s)
        {
            if (s.EndsWith("\r\n")) return s.Substring(0, s.Length - 2);
            if (s.EndsWith("\n")) return s.Substring(0, s.Length - 1);
            return s;
        }

        internal static string ContentType(string value)
        {
            if (string.IsNullOrEmpty(value)) return "text/plain";
            int semi = value.IndexOf(';');
            return (semi < 0 ? value : value.Substring(0, semi)).Trim().ToLowerInvariant();
        }

        internal static string Param(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) return "";
            Match m = Regex.Match(value, @";\s*" + Regex.Escape(name) + @"\s*=\s*(?:""([^""]*)""|([^;\s]*))", RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            return m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
        }

        internal static byte[] TransferDecode(string body, string encoding)
        {
            encoding = (encoding ?? "").Trim().ToLowerInvariant();
            if (encoding == "base64")
            {
                string clean = Regex.Replace(body, @"[^A-Za-z0-9+/=]", "");
                clean = clean.TrimEnd('=');
                while (clean.Length % 4 != 0) clean += "=";
                try { return Convert.FromBase64String(clean); }
                catch (FormatException) { return new byte[0]; }
            }
            if (encoding == "quoted-printable") return QpDecode(body, false);
            return MailWire.Bytes(body);
        }

        internal static byte[] QpDecode(string s, bool header)
        {
            List<byte> o = new List<byte>(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c == '=')
                {
                    if (i + 1 < s.Length && (s[i + 1] == '\n' || s[i + 1] == '\r'))
                    {
                        i++;
                        if (s[i] == '\r' && i + 1 < s.Length && s[i + 1] == '\n') i++;
                        continue;
                    }
                    if (i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
                    {
                        o.Add(Convert.ToByte(s.Substring(i + 1, 2), 16));
                        i += 2;
                        continue;
                    }
                    o.Add((byte)'=');
                }
                else if (header && c == '_') o.Add((byte)' ');
                else o.Add((byte)c);
            }
            return o.ToArray();
        }

        internal static string Raw8(string s)
        {
            bool high = false;
            foreach (char c in s) if (c > 127) { high = true; break; }
            if (!high) return s;
            try { return new UTF8Encoding(false, true).GetString(MailWire.Bytes(s)); }
            catch (Exception) { return s; }
        }

        private static readonly Regex Word = new Regex(@"=\?([^?\s]+)\?([bBqQ])\?([^?\s]*)\?=");

        internal static string DecodeHeader(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            value = Raw8(value);
            value = Regex.Replace(value, @"(=\?[^?\s]+\?[bBqQ]\?[^?\s]*\?=)\s+(?==\?)", "$1");
            string decoded = Word.Replace(value, delegate(Match m)
            {
                try
                {
                    Encoding enc = Charset(m.Groups[1].Value);
                    byte[] data;
                    if (m.Groups[2].Value.ToUpperInvariant() == "B") data = Convert.FromBase64String(m.Groups[3].Value.PadRight((m.Groups[3].Value.Length + 3) / 4 * 4, '='));
                    else data = QpDecode(m.Groups[3].Value, true);
                    return enc.GetString(data);
                }
                catch (Exception) { return m.Value; }
            });
            return Regex.Replace(decoded, @"[\r\n\t]+", " ").Trim();
        }

        internal static MailHead Head(string raw)
        {
            Dictionary<string, string> h = ParseHeaders(raw);
            MailHead m = new MailHead();
            string v;
            m.From = h.TryGetValue("from", out v) ? DecodeHeader(v) : "";
            m.To = h.TryGetValue("to", out v) ? DecodeHeader(v) : "";
            m.Subject = h.TryGetValue("subject", out v) ? DecodeHeader(v) : "";
            m.Date = h.TryGetValue("date", out v) ? v : "";
            m.MessageId = h.TryGetValue("message-id", out v) ? v.Trim() : "";
            m.References = h.TryGetValue("references", out v) ? v.Trim() : "";
            m.ReplyTo = h.TryGetValue("reply-to", out v) ? DecodeHeader(v) : "";
            return m;
        }

        internal static string ShortDate(string date)
        {
            if (string.IsNullOrEmpty(date)) return "unknown date";
            string clean = Regex.Replace(date, @"\([^)]*\)", "").Trim();
            DateTimeOffset d;
            if (DateTimeOffset.TryParse(clean, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out d)) return d.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            return clean.Length > 40 ? clean.Substring(0, 40) : clean;
        }

        private const int MostHtml = 100000;
        private static readonly TimeSpan HtmlWait = TimeSpan.FromMilliseconds(500);

        internal static string HtmlToText(string html)
        {
            string t = html ?? "";
            if (t.Length > MostHtml) t = t.Substring(0, char.IsHighSurrogate(t[MostHtml - 1]) ? MostHtml - 1 : MostHtml);
            try
            {
                t = Regex.Replace(t, @"<(script|style|head)[^>]*>.*?</\1\s*>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase, HtmlWait);
                t = Regex.Replace(t, @"<!--.*?-->", " ", RegexOptions.Singleline, HtmlWait);
                t = Regex.Replace(t, @"<br\s*/?>|</(p|div|li|tr|h[1-6]|table|blockquote)\s*>", "\n", RegexOptions.IgnoreCase, HtmlWait);
                t = Regex.Replace(t, @"<[^>]*>", "", RegexOptions.None, HtmlWait);
            }
            catch (RegexMatchTimeoutException) { t = Untagged(t); }
            t = WebUtility.HtmlDecode(t);
            t = t.Replace(' ', ' ');
            t = Regex.Replace(t, @"[ \t]+", " ");
            t = Regex.Replace(t, @" ?\n ?", "\n");
            t = Regex.Replace(t, @"\n{3,}", "\n\n");
            return t.Trim();
        }

        private static string Untagged(string html)
        {
            StringBuilder sb = new StringBuilder(html.Length);
            int at = 0;
            while (at < html.Length)
            {
                int open = html.IndexOf('<', at);
                if (open < 0) { sb.Append(html, at, html.Length - at); break; }
                sb.Append(html, at, open - at);
                int close = html.IndexOf('>', open);
                if (close < 0) break;
                sb.Append(' ');
                at = close + 1;
            }
            return sb.ToString();
        }

        private static string Text(MailPart p)
        {
            byte[] data = TransferDecode(p.Body, p.Header("content-transfer-encoding"));
            return Charset(Param(p.Header("content-type"), "charset")).GetString(data);
        }

        private static bool Attachment(MailPart p)
        {
            return p.Header("content-disposition").ToLowerInvariant().StartsWith("attachment");
        }

        internal static string Body(MailPart p, List<string> attachments)
        {
            string type = ContentType(p.Header("content-type"));
            if (p.Parts.Count > 0)
            {
                if (type == "multipart/alternative")
                {
                    string plain = null, html = null;
                    foreach (MailPart c in p.Parts)
                    {
                        string ct = ContentType(c.Header("content-type"));
                        if (ct == "text/plain" && plain == null) plain = Body(c, attachments);
                        else if (ct == "text/html" && html == null) html = Body(c, attachments);
                        else if (ct.StartsWith("multipart/") && plain == null) plain = Body(c, attachments);
                    }
                    return plain != null && plain.Trim().Length > 0 ? plain : (html ?? plain ?? "");
                }
                StringBuilder sb = new StringBuilder();
                foreach (MailPart c in p.Parts)
                {
                    string s = Body(c, attachments);
                    if (s.Length > 0) { if (sb.Length > 0) sb.Append("\n\n"); sb.Append(s); }
                }
                return sb.ToString();
            }
            if (Attachment(p) || (!type.StartsWith("text/") && type != "message/rfc822"))
            {
                string name = Param(p.Header("content-disposition"), "filename");
                if (name.Length == 0) name = Param(p.Header("content-type"), "name");
                if (name.Length == 0) name = type;
                attachments.Add(DecodeHeader(name));
                return "";
            }
            if (type == "message/rfc822") { attachments.Add("forwarded message"); return ""; }
            string text = Text(p);
            if (type == "text/html") return HtmlToText(text);
            return text.Replace("\r\n", "\n").Trim();
        }
    }
}
