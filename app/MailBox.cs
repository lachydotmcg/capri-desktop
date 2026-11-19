using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class MailException : Exception
    {
        internal MailException(string message) : base(message) { }
    }

    [DataContract]
    internal sealed class MailAccountFile
    {
        [DataMember(Name = "address")] public string Address;
        [DataMember(Name = "name")] public string Name;
        [DataMember(Name = "login")] public string Login;
        [DataMember(Name = "imap_host")] public string ImapHost;
        [DataMember(Name = "imap_port")] public int ImapPort;
        [DataMember(Name = "smtp_host")] public string SmtpHost;
        [DataMember(Name = "smtp_port")] public int SmtpPort;
        [DataMember(Name = "smtp_implicit_tls")] public bool SmtpImplicitTls;
        [DataMember(Name = "plain")] public bool Plain;
        [DataMember(Name = "password_protected")] public string PasswordProtected;
    }

    internal sealed class MailAccount
    {
        internal const string OutlookNote = "Outlook and Hotmail don't take app passwords any more, so Capri can't sign in to them yet.";
        internal string Address = "";
        internal string Name = "";
        internal string Login = "";
        internal string ImapHost = "";
        internal int ImapPort = 993;
        internal string SmtpHost = "";
        internal int SmtpPort = 465;
        internal bool SmtpImplicitTls = true;
        internal bool Plain;
        internal string Password = "";

        internal static string Domain(string address)
        {
            if (address == null) return "";
            int at = address.LastIndexOf('@');
            return at < 0 ? "" : address.Substring(at + 1).Trim().ToLowerInvariant();
        }

        internal static string Unsupported(string address)
        {
            string d = Domain(address);
            if (d == "outlook.com" || d == "hotmail.com" || d == "live.com") return OutlookNote;
            return null;
        }

        internal static MailAccount ForAddress(string address)
        {
            address = (address ?? "").Trim();
            if (Unsupported(address) != null) return null;
            MailAccount a = new MailAccount();
            a.Address = address;
            a.Login = address;
            string d = Domain(address);
            if (d == "gmail.com" || d == "googlemail.com") a.Set("imap.gmail.com", 993, "smtp.gmail.com", 465, true);
            else if (d == "icloud.com" || d == "me.com" || d == "mac.com") a.Set("imap.mail.me.com", 993, "smtp.mail.me.com", 587, false);
            else if (d == "yahoo.com") a.Set("imap.mail.yahoo.com", 993, "smtp.mail.yahoo.com", 465, true);
            else if (d == "aol.com") a.Set("imap.aol.com", 993, "smtp.aol.com", 465, true);
            else if (d == "fastmail.com" || d == "fastmail.fm") a.Set("imap.fastmail.com", 993, "smtp.fastmail.com", 465, true);
            else if (d == "gmx.com" || d == "gmx.net") a.Set("imap.gmx.com", 993, "mail.gmx.com", 587, false);
            else if (d == "zoho.com") a.Set("imap.zoho.com", 993, "smtp.zoho.com", 465, true);
            return a;
        }

        private void Set(string ih, int ip, string sh, int sp, bool implicitTls)
        {
            ImapHost = ih; ImapPort = ip; SmtpHost = sh; SmtpPort = sp; SmtpImplicitTls = implicitTls;
        }

        internal bool Known { get { return ImapHost.Length > 0 && SmtpHost.Length > 0; } }

        internal string Provider
        {
            get
            {
                string d = Domain(Address);
                if (d == "gmail.com" || d == "googlemail.com") return "Gmail";
                if (d == "icloud.com" || d == "me.com" || d == "mac.com") return "iCloud Mail";
                if (d == "yahoo.com") return "Yahoo Mail";
                if (d == "aol.com") return "AOL Mail";
                if (d == "fastmail.com" || d == "fastmail.fm") return "Fastmail";
                if (d == "gmx.com" || d == "gmx.net") return "GMX";
                if (d == "zoho.com") return "Zoho Mail";
                return "The mail server";
            }
        }

        internal string LoginName { get { return string.IsNullOrEmpty(Login) ? Address : Login; } }

        internal static string FilePath(string folder) { return Path.Combine(folder, "mail.json"); }

        internal void Save(string folder)
        {
            Directory.CreateDirectory(folder);
            MailAccountFile f = new MailAccountFile();
            f.Address = Address; f.Name = Name; f.Login = Login;
            f.ImapHost = ImapHost; f.ImapPort = ImapPort; f.SmtpHost = SmtpHost; f.SmtpPort = SmtpPort;
            f.SmtpImplicitTls = SmtpImplicitTls; f.Plain = Plain;
#if !CROSS
            if (Password.Length > 0) f.PasswordProtected = Voice.Protect(Password);
#endif
            string path = FilePath(folder);
            string temp = path + ".tmp";
            using (FileStream s = File.Create(temp)) new DataContractJsonSerializer(typeof(MailAccountFile)).WriteObject(s, f);
            if (File.Exists(path)) File.Delete(path);
            File.Move(temp, path);
        }

        internal static MailAccount Load(string folder)
        {
            try
            {
                string path = FilePath(folder);
                if (!File.Exists(path)) return null;
                MailAccountFile f;
                using (FileStream s = File.OpenRead(path)) f = (MailAccountFile)new DataContractJsonSerializer(typeof(MailAccountFile)).ReadObject(s);
                if (f == null || string.IsNullOrEmpty(f.Address)) return null;
                MailAccount a = new MailAccount();
                a.Address = f.Address; a.Name = f.Name ?? ""; a.Login = f.Login ?? "";
                a.ImapHost = f.ImapHost ?? ""; a.ImapPort = f.ImapPort > 0 ? f.ImapPort : 993;
                a.SmtpHost = f.SmtpHost ?? ""; a.SmtpPort = f.SmtpPort > 0 ? f.SmtpPort : 465;
                a.SmtpImplicitTls = f.SmtpImplicitTls; a.Plain = f.Plain;
                a.Password = Voice.Unprotect(f.PasswordProtected) ?? "";
                return a;
            }
            catch (Exception) { return null; }
        }

        internal static void Delete(string folder)
        {
            try { if (File.Exists(FilePath(folder))) File.Delete(FilePath(folder)); }
            catch (IOException) { }
        }
    }

    internal sealed class MailWire : IDisposable
    {
        private static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
        private readonly Stream stream;
        private readonly byte[] buf = new byte[8192];
        private int pos, len;
        internal readonly string Host;

        internal MailWire(Stream s, string host) { stream = s; Host = host; }

        internal static bool Loopback(string host)
        {
            return host == "127.0.0.1" || host == "localhost" || host == "::1";
        }

        internal static MailWire Connect(string host, int port, bool tls, bool plain)
        {
            if (plain && !Loopback(host)) throw new MailException("Unencrypted mail connections are only allowed on this computer.");
            TcpClient tcp = new TcpClient();
            try
            {
                IAsyncResult r = tcp.BeginConnect(host, port, null, null);
                if (!r.AsyncWaitHandle.WaitOne(20000)) { tcp.Close(); throw new MailException("Couldn't reach " + host + " in time. Check the connection."); }
                tcp.EndConnect(r);
                tcp.ReceiveTimeout = 20000;
                tcp.SendTimeout = 20000;
                Stream s = tcp.GetStream();
                if (tls && !plain)
                {
                    SslStream ssl = new SslStream(s);
                    ssl.AuthenticateAsClient(host);
                    s = ssl;
                }
                return new MailWire(s, host);
            }
            catch (MailException) { throw; }
            catch (Exception ex)
            {
                tcp.Close();
                if (ex is System.Security.Authentication.AuthenticationException) throw new MailException("The secure connection to " + host + " failed.");
                throw new MailException("Couldn't reach " + host + ". Check the connection and the server name.");
            }
        }

        internal MailWire Upgrade()
        {
            SslStream ssl = new SslStream(stream);
            try { ssl.AuthenticateAsClient(Host); }
            catch (Exception) { throw new MailException("The secure connection to " + Host + " failed."); }
            return new MailWire(ssl, Host);
        }

        private int Next()
        {
            if (pos >= len)
            {
                try { len = stream.Read(buf, 0, buf.Length); }
                catch (IOException) { throw new MailException("The mail server at " + Host + " stopped answering."); }
                pos = 0;
                if (len <= 0) { len = 0; return -1; }
            }
            return buf[pos++];
        }

        internal string ReadLine()
        {
            StringBuilder sb = new StringBuilder();
            while (true)
            {
                int b = Next();
                if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                if (b == '\n') break;
                if (b != '\r') sb.Append((char)b);
                if (sb.Length > 1000000) throw new MailException("The mail server sent a line that was too long.");
            }
            return sb.ToString();
        }

        internal string ReadBytes(int n, int keep)
        {
            StringBuilder sb = new StringBuilder(Math.Min(keep, 65536));
            for (int i = 0; i < n; i++)
            {
                int b = Next();
                if (b < 0) throw new MailException("The mail server cut the message short.");
                if (i < keep) sb.Append((char)b);
            }
            return sb.ToString();
        }

        internal void Write(string text) { Write(Encoding.UTF8.GetBytes(text)); }

        internal void Write(byte[] data)
        {
            try { stream.Write(data, 0, data.Length); stream.Flush(); }
            catch (IOException) { throw new MailException("The mail server at " + Host + " stopped answering."); }
        }

        public void Dispose()
        {
            try { stream.Dispose(); }
            catch (Exception) { }
        }

        internal static byte[] Bytes(string latin1) { return Latin1.GetBytes(latin1); }

        internal static string Chars(byte[] bytes) { return Latin1.GetString(bytes); }
    }

    internal sealed class ImapReply
    {
        internal string Status = "";
        internal string Text = "";
        internal List<string> Untagged = new List<string>();
        internal bool Ok { get { return Status == "OK"; } }
    }

    internal sealed class MailHead
    {
        internal int Uid;
        internal bool Seen;
        internal string From = "", To = "", Subject = "", Date = "", MessageId = "", References = "", ReplyTo = "";
    }

    internal sealed class MailBox : IDisposable
    {
        internal const int MaxMessage = 2 * 1024 * 1024;
        private MailWire wire;
        private int tagCounter;
        private readonly MailAccount account;

        private MailBox(MailAccount a) { account = a; }

        internal static MailBox Open(MailAccount a)
        {
            MailBox box = new MailBox(a);
            try
            {
                box.wire = MailWire.Connect(a.ImapHost, a.ImapPort, true, a.Plain);
                ImapReply hello = box.Read(null);
                string first = hello.Untagged.Count > 0 ? hello.Untagged[0] : "";
                if (!first.StartsWith("* OK", StringComparison.OrdinalIgnoreCase) && !first.StartsWith("* PREAUTH", StringComparison.OrdinalIgnoreCase))
                    throw new MailException("The mail server at " + a.ImapHost + " didn't say hello properly.");
                ImapReply r = box.Command("LOGIN", Arg(a.LoginName), " ", Arg(a.Password));
                if (!r.Ok) throw new MailException(box.LoginFailure(r));
                return box;
            }
            catch (Exception)
            {
                box.Dispose();
                throw;
            }
        }

        private string LoginFailure(ImapReply r)
        {
            string t = (r.Text ?? "").ToUpperInvariant();
            if (t.Contains("AUTHENTICATIONFAILED") || t.Contains("AUTHENTICATION") || t.Contains("INVALID") || t.Contains("LOGIN FAILED") || t.Contains("CREDENTIALS") || t.Contains("DENIED") || t.Contains("PASSWORD"))
                return account.Provider + " refused the sign-in. Check the " + (account.Provider == "The mail server" ? "password." : "app password.");
            return "The mail server refused the sign-in: " + Scrub(r.Text);
        }

        internal string Scrub(string s)
        {
            if (s == null) return "";
            if (account.Password.Length > 0) s = s.Replace(account.Password, "***");
            s = Regex.Replace(s, @"[\r\n\t]+", " ").Trim();
            return s.Length > 200 ? s.Substring(0, 200) : s;
        }

        internal static object Arg(string value)
        {
            bool safe = true;
            foreach (char c in value) if (c < 32 || c > 126) { safe = false; break; }
            if (safe) return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
            return Encoding.UTF8.GetBytes(value);
        }

        private ImapReply Command(string verb, params object[] parts)
        {
            string tag = "A" + (++tagCounter);
            StringBuilder line = new StringBuilder(tag + " " + verb + " ");
            foreach (object p in parts)
            {
                byte[] lit = p as byte[];
                if (lit == null) { line.Append((string)p); continue; }
                line.Append("{" + lit.Length + "}\r\n");
                wire.Write(line.ToString());
                string cont = wire.ReadLine();
                if (cont == null || !cont.StartsWith("+")) throw new MailException("The mail server wouldn't take that request.");
                wire.Write(lit);
                line = new StringBuilder();
            }
            line.Append("\r\n");
            wire.Write(line.ToString());
            return Read(tag);
        }

        private static readonly Regex LiteralEnd = new Regex(@"\{(\d+)\+?\}$");

        private ImapReply Read(string tag)
        {
            ImapReply reply = new ImapReply();
            while (true)
            {
                string line = wire.ReadLine();
                if (line == null) throw new MailException("The mail server closed the connection.");
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    Match m = LiteralEnd.Match(line);
                    if (!m.Success) { sb.Append(line); break; }
                    int n = int.Parse(m.Groups[1].Value);
                    int keep = Math.Min(n, MaxMessage + 4096);
                    sb.Append(line.Substring(0, m.Index)).Append('{').Append(keep).Append("}\r\n");
                    sb.Append(wire.ReadBytes(n, keep));
                    line = wire.ReadLine();
                    if (line == null) throw new MailException("The mail server closed the connection.");
                }
                string full = sb.ToString();
                if (tag == null) { reply.Untagged.Add(full); return reply; }
                if (full.StartsWith(tag + " "))
                {
                    string rest = full.Substring(tag.Length + 1);
                    int sp = rest.IndexOf(' ');
                    reply.Status = (sp < 0 ? rest : rest.Substring(0, sp)).ToUpperInvariant();
                    reply.Text = sp < 0 ? "" : rest.Substring(sp + 1);
                    return reply;
                }
                reply.Untagged.Add(full);
            }
        }

        private ImapReply Need(ImapReply r)
        {
            if (!r.Ok) throw new MailException("The mail server said no: " + Scrub(r.Text));
            return r;
        }

        internal static string Mutf7Encode(string s)
        {
            StringBuilder sb = new StringBuilder();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                if (c >= 0x20 && c <= 0x7e) { sb.Append(c); if (c == '&') sb.Append('-'); i++; continue; }
                int j = i;
                while (j < s.Length && (s[j] < 0x20 || s[j] > 0x7e)) j++;
                byte[] be = Encoding.BigEndianUnicode.GetBytes(s.Substring(i, j - i));
                sb.Append('&').Append(Convert.ToBase64String(be).TrimEnd('=').Replace('/', ',')).Append('-');
                i = j;
            }
            return sb.ToString();
        }

        internal static string Mutf7Decode(string s)
        {
            if (s.IndexOf('&') < 0) return s;
            return Regex.Replace(s, "&([^-]*)-", delegate(Match m)
            {
                if (m.Groups[1].Length == 0) return "&";
                try
                {
                    string b = m.Groups[1].Value.Replace(',', '/');
                    while (b.Length % 4 != 0) b += "=";
                    return Encoding.BigEndianUnicode.GetString(Convert.FromBase64String(b));
                }
                catch (FormatException) { return m.Value; }
            });
        }

        internal List<string> Folders()
        {
            ImapReply r = Need(Command("LIST", "\"\" \"*\""));
            List<string> names = new List<string>();
            foreach (string u in r.Untagged)
            {
                if (!u.StartsWith("* LIST ", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    int i = 7;
                    List<object> flags = ImapParse.Value(u, ref i) as List<object>;
                    ImapParse.Value(u, ref i);
                    string name = ImapParse.Value(u, ref i) as string;
                    bool skip = false;
                    if (flags != null) foreach (object f in flags) if (string.Equals(f as string, "\\Noselect", StringComparison.OrdinalIgnoreCase) || string.Equals(f as string, "\\NonExistent", StringComparison.OrdinalIgnoreCase)) skip = true;
                    if (name != null && !skip) names.Add(Mutf7Decode(name));
                }
                catch (FormatException) { }
            }
            return names;
        }

        internal void Select(string folder, bool readOnly)
        {
            if (string.IsNullOrEmpty(folder)) folder = "INBOX";
            ImapReply r = Command(readOnly ? "EXAMINE" : "SELECT", Arg(Mutf7Encode(folder)));
            if (!r.Ok) throw new MailException("There's no mail folder called \"" + folder + "\". Ask for the folder list to see the names.");
        }

        internal List<int> Search(params object[] criteria)
        {
            bool utf8 = false;
            foreach (object c in criteria) if (c is byte[]) utf8 = true;
            List<object> parts = new List<object>();
            if (utf8) parts.Add("CHARSET UTF-8 ");
            for (int i = 0; i < criteria.Length; i++) { if (i > 0) parts.Add(" "); parts.Add(criteria[i]); }
            ImapReply r = Need(Command("UID SEARCH", parts.ToArray()));
            List<int> uids = new List<int>();
            foreach (string u in r.Untagged)
            {
                if (!u.StartsWith("* SEARCH", StringComparison.OrdinalIgnoreCase)) continue;
                foreach (string t in u.Substring(8).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int n;
                    if (int.TryParse(t, out n)) uids.Add(n);
                }
            }
            uids.Sort();
            return uids;
        }

        private const string HeadFields = "HEADER.FIELDS (FROM TO SUBJECT DATE MESSAGE-ID REFERENCES REPLY-TO)";

        private List<Dictionary<string, object>> Fetch(List<int> uids, string items)
        {
            List<string> ids = new List<string>();
            foreach (int u in uids) ids.Add(u.ToString());
            ImapReply r = Need(Command("UID FETCH", string.Join(",", ids.ToArray()) + " (" + items + ")"));
            List<Dictionary<string, object>> list = new List<Dictionary<string, object>>();
            foreach (string u in r.Untagged)
            {
                Match m = Regex.Match(u, @"^\* \d+ FETCH ", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                try
                {
                    int i = m.Length;
                    List<object> pairs = ImapParse.Value(u, ref i) as List<object>;
                    if (pairs == null) continue;
                    Dictionary<string, object> d = new Dictionary<string, object>();
                    for (int k = 0; k + 1 < pairs.Count; k += 2)
                    {
                        string key = ((pairs[k] as string) ?? "").ToUpperInvariant();
                        if (key.StartsWith("BODY[")) key = "BODY";
                        d[key] = pairs[k + 1];
                    }
                    list.Add(d);
                }
                catch (FormatException) { }
            }
            return list;
        }

        private static bool HasSeen(object flags)
        {
            List<object> l = flags as List<object>;
            if (l == null) return false;
            foreach (object f in l) if (string.Equals(f as string, "\\Seen", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static int UidOf(Dictionary<string, object> d)
        {
            int n;
            object v;
            return d.TryGetValue("UID", out v) && int.TryParse(v as string, out n) ? n : 0;
        }

        internal List<MailHead> Headers(List<int> uids)
        {
            List<MailHead> heads = new List<MailHead>();
            if (uids.Count == 0) return heads;
            foreach (Dictionary<string, object> d in Fetch(uids, "UID FLAGS BODY.PEEK[" + HeadFields + "]"))
            {
                object raw, flags;
                d.TryGetValue("BODY", out raw);
                d.TryGetValue("FLAGS", out flags);
                MailHead h = MailMime.Head((raw as string) ?? "");
                h.Uid = UidOf(d);
                h.Seen = HasSeen(flags);
                heads.Add(h);
            }
            return heads;
        }

        internal string Message(int uid, out bool seen)
        {
            seen = false;
            List<int> one = new List<int>();
            one.Add(uid);
            foreach (Dictionary<string, object> d in Fetch(one, "UID FLAGS BODY.PEEK[]<0." + MaxMessage + ">"))
            {
                if (UidOf(d) != uid) continue;
                object raw, flags;
                d.TryGetValue("BODY", out raw);
                d.TryGetValue("FLAGS", out flags);
                seen = HasSeen(flags);
                return raw as string;
            }
            return null;
        }

        public void Dispose()
        {
            if (wire == null) return;
            try { wire.Write("A" + (++tagCounter) + " LOGOUT\r\n"); }
            catch (Exception) { }
            wire.Dispose();
            wire = null;
        }
    }

    internal static class ImapParse
    {
        internal static object Value(string s, ref int i)
        {
            while (i < s.Length && s[i] == ' ') i++;
            if (i >= s.Length) throw new FormatException();
            char c = s[i];
            if (c == '(')
            {
                i++;
                List<object> list = new List<object>();
                while (true)
                {
                    while (i < s.Length && s[i] == ' ') i++;
                    if (i >= s.Length) throw new FormatException();
                    if (s[i] == ')') { i++; return list; }
                    list.Add(Value(s, ref i));
                }
            }
            if (c == '"')
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length) i++;
                    sb.Append(s[i++]);
                }
                if (i >= s.Length) throw new FormatException();
                i++;
                return sb.ToString();
            }
            if (c == '{')
            {
                int close = s.IndexOf('}', i);
                if (close < 0) throw new FormatException();
                int n = int.Parse(s.Substring(i + 1, close - i - 1));
                int start = close + 1;
                if (start + 2 <= s.Length && s.Substring(start, 2) == "\r\n") start += 2;
                if (start + n > s.Length) throw new FormatException();
                i = start + n;
                return s.Substring(start, n);
            }
            int begin = i;
            int depth = 0;
            while (i < s.Length)
            {
                char x = s[i];
                if (x == '[') depth++;
                else if (x == ']') depth--;
                else if (depth == 0 && (x == ' ' || x == '(' || x == ')' || x == '\r' || x == '\n')) break;
                i++;
            }
            string atom = s.Substring(begin, i - begin);
            return string.Equals(atom, "NIL", StringComparison.OrdinalIgnoreCase) ? null : atom;
        }
    }
}
