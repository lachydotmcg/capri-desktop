using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class MailChecks
    {
        private const string Tricky = "p\"w\\d é";

        private sealed class FakeImap
        {
            internal TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            internal Dictionary<int, string> Messages = new Dictionary<int, string>();
            internal HashSet<int> Seen = new HashSet<int>();
            internal HashSet<int> Deleted = new HashSet<int>();
            internal Dictionary<int, DateTime> Arrived = new Dictionary<int, DateTime>();
            internal Dictionary<string, Dictionary<int, string>> Boxes = new Dictionary<string, Dictionary<int, string>>();
            internal Dictionary<string, string> Uses = new Dictionary<string, string>();
            internal List<string> Log = new List<string>();
            internal bool SetSeenByFetch;
            internal string Password = "good";
            internal string Caps;
            internal bool CapsOnLogin;
            internal long Validity = 7;
            internal readonly object Lock = new object();
            private readonly List<Stream> idlers = new List<Stream>();
            private int inboxNext, boxNext = 100;
            internal int Port { get { return ((IPEndPoint)Listener.LocalEndpoint).Port; } }

            internal bool Can(string capability) { return Caps != null && Array.IndexOf(Caps.Split(' '), capability) >= 0; }

            internal int Arrive(string raw, DateTime at)
            {
                lock (Lock)
                {
                    int uid = inboxNext++;
                    Messages[uid] = raw;
                    Arrived[uid] = at;
                    foreach (Stream s in idlers) { try { Put(s, "* " + Messages.Count + " EXISTS\r\n"); } catch (Exception) { } }
                    return uid;
                }
            }

            internal bool Holds(string box, string text)
            {
                lock (Lock)
                {
                    Dictionary<int, string> found = Box(box);
                    if (found == null) return false;
                    foreach (string raw in found.Values) if (raw.Contains(text)) return true;
                    return false;
                }
            }

            internal bool Logged(string command) { lock (Lock) return Log.Exists(one => one.StartsWith(command, StringComparison.OrdinalIgnoreCase)); }

            internal int Count(string command) { lock (Lock) return Log.FindAll(one => one.StartsWith(command, StringComparison.OrdinalIgnoreCase)).Count; }

            internal void Shift(int uid, string box)
            {
                lock (Lock)
                {
                    Boxes[box][boxNext++] = Messages[uid];
                    Messages.Remove(uid);
                }
            }

            private Dictionary<int, string> Box(string name)
            {
                if (string.Equals(name, "INBOX", StringComparison.OrdinalIgnoreCase)) return Messages;
                Dictionary<int, string> box;
                return name != null && Boxes.TryGetValue(name, out box) ? box : null;
            }

            private static bool InSet(int uid, string set, int top)
            {
                foreach (string part in set.Split(','))
                {
                    string[] ends = part.Split(':');
                    int a = ends[0] == "*" ? top : int.Parse(ends[0]);
                    int b = ends.Length < 2 ? a : ends[1] == "*" ? top : int.Parse(ends[1]);
                    if (uid >= Math.Min(a, b) && uid <= Math.Max(a, b)) return true;
                }
                return false;
            }

            internal void Start()
            {
                foreach (int uid in Messages.Keys) inboxNext = Math.Max(inboxNext, uid);
                inboxNext++;
                Listener.Start();
                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        TcpClient c;
                        try { c = Listener.AcceptTcpClient(); }
                        catch (Exception) { return; }
                        Thread w = new Thread(delegate() { try { Serve(c); } catch (Exception) { } });
                        w.IsBackground = true;
                        w.Start();
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            private static string Line(Stream s)
            {
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    int b = s.ReadByte();
                    if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                    if (b == '\n') return sb.ToString().TrimEnd('\r');
                    sb.Append((char)b);
                }
            }

            private static void Put(Stream s, string text)
            {
                byte[] b = Encoding.GetEncoding(28591).GetBytes(text);
                s.Write(b, 0, b.Length);
                s.Flush();
            }

            private void Serve(TcpClient c)
            {
                Stream s = c.GetStream();
                Put(s, "* OK fake ready\r\n");
                bool login = false, readOnly = true;
                string selected = null;
                while (true)
                {
                    string line = Line(s);
                    if (line == null) return;
                    StringBuilder full = new StringBuilder();
                    Match lit;
                    while ((lit = Regex.Match(line, @"\{(\d+)\}$")).Success)
                    {
                        int n = int.Parse(lit.Groups[1].Value);
                        Put(s, "+ go\r\n");
                        byte[] data = new byte[n];
                        int got = 0;
                        while (got < n) got += s.Read(data, got, n - got);
                        string text = Encoding.UTF8.GetString(data);
                        full.Append(line.Substring(0, lit.Index)).Append('"').Append(text.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
                        line = Line(s);
                    }
                    full.Append(line);
                    string cmd = full.ToString();
                    int sp = cmd.IndexOf(' ');
                    string tag = cmd.Substring(0, sp);
                    string rest = cmd.Substring(sp + 1);
                    string upper = rest.ToUpperInvariant();
                    if (login && upper == "IDLE")
                    {
                        if (!Can("IDLE")) { Put(s, tag + " BAD no idling here\r\n"); continue; }
                        lock (Lock) { Log.Add("IDLE"); idlers.Add(s); Put(s, "+ idling\r\n"); }
                        string done = Line(s);
                        lock (Lock)
                        {
                            idlers.Remove(s);
                            if (done == null) return;
                            Log.Add(done);
                            Put(s, tag + " OK IDLE terminated\r\n");
                        }
                        continue;
                    }
                    lock (Lock)
                    {
                        if (login) Log.Add(Regex.Replace(rest, "^LOGIN .*", "LOGIN", RegexOptions.IgnoreCase));
                        Dictionary<int, string> box = Box(selected);
                        List<int> order = box == null ? new List<int>() : new List<int>(box.Keys);
                        order.Sort();
                        int top = order.Count == 0 ? 0 : order[order.Count - 1];
                        if (upper.StartsWith("LOGIN "))
                        {
                            List<string> args = Quoted(rest.Substring(6));
                            if (args.Count == 2 && args[0] == "me@example.com" && args[1] == Password)
                            {
                                login = true;
                                Put(s, (CapsOnLogin && Caps != null ? "* CAPABILITY IMAP4rev1 " + Caps + "\r\n" : "") + tag + " OK LOGIN completed\r\n");
                            }
                            else Put(s, tag + " NO [AUTHENTICATIONFAILED] Invalid credentials\r\n");
                        }
                        else if (!login) Put(s, tag + " BAD log in first\r\n");
                        else if (upper.StartsWith("LOGOUT")) { Put(s, "* BYE\r\n" + tag + " OK done\r\n"); return; }
                        else if (upper == "CAPABILITY") Put(s, Caps == null ? tag + " BAD unknown\r\n" : "* CAPABILITY IMAP4rev1 " + Caps + "\r\n" + tag + " OK done\r\n");
                        else if (upper == "NOOP") Put(s, tag + " OK done\r\n");
                        else if (upper.StartsWith("LIST "))
                        {
                            StringBuilder list = new StringBuilder("* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n* LIST (\\Noselect) \"/\" \"[Gmail]\"\r\n");
                            bool uses = upper.Contains("RETURN (SPECIAL-USE)") || Can("SPECIAL-USE");
                            foreach (string name in Boxes.Keys)
                            {
                                string use;
                                list.Append("* LIST (\\HasNoChildren" + (uses && Uses.TryGetValue(name, out use) ? " " + use : "") + ") \"/\" \"" + name + "\"\r\n");
                            }
                            list.Append("* LIST (\\HasNoChildren) \"/\" \"Sent\"\r\n* LIST (\\HasNoChildren) \"/\" \"Caf&AOk-\"\r\n");
                            Put(s, list + tag + " OK done\r\n");
                        }
                        else if (upper.StartsWith("EXAMINE ") || upper.StartsWith("SELECT "))
                        {
                            List<string> args = Quoted(rest.Substring(rest.IndexOf(' ') + 1));
                            Dictionary<int, string> want = args.Count == 0 ? null : Box(args[0]);
                            if (want == null) Put(s, tag + " NO no such mailbox\r\n");
                            else
                            {
                                selected = args[0];
                                readOnly = upper.StartsWith("EXAMINE ");
                                int next = string.Equals(selected, "INBOX", StringComparison.OrdinalIgnoreCase) ? inboxNext : boxNext;
                                Put(s, "* " + want.Count + " EXISTS\r\n* OK [UIDVALIDITY " + Validity + "] ok\r\n* OK [UIDNEXT " + next + "] ok\r\n" + tag + " OK [" + (readOnly ? "READ-ONLY" : "READ-WRITE") + "] selected\r\n");
                            }
                        }
                        else if (box == null) Put(s, tag + " BAD select first\r\n");
                        else if (upper.StartsWith("UID SEARCH"))
                        {
                            List<string> args = Quoted(rest.Substring(10));
                            StringBuilder found = new StringBuilder("* SEARCH");
                            foreach (int uid in order)
                            {
                                string raw = box[uid];
                                bool ok = true;
                                for (int i = 0; i < args.Count; i++)
                                {
                                    string a = args[i].ToUpperInvariant();
                                    if (a == "UNSEEN" && Seen.Contains(uid)) ok = false;
                                    if (a == "UID" && i + 1 < args.Count && !InSet(uid, args[i + 1], top)) ok = false;
                                    if ((a == "TEXT" || a == "FROM") && i + 1 < args.Count)
                                    {
                                        string want = args[i + 1].ToLowerInvariant();
                                        string hay = a == "FROM" ? Regex.Match(raw, @"(?m)^From:.*$").Value : raw;
                                        if (hay.ToLowerInvariant().IndexOf(want, StringComparison.Ordinal) < 0) ok = false;
                                    }
                                    if (a == "HEADER" && i + 2 < args.Count)
                                    {
                                        Match field = Regex.Match(raw, "(?im)^" + Regex.Escape(args[i + 1]) + @":[ \t]*([^\r\n]*)");
                                        if (!field.Success || field.Groups[1].Value.IndexOf(args[i + 2], StringComparison.OrdinalIgnoreCase) < 0) ok = false;
                                        i += 2;
                                    }
                                }
                                if (ok) found.Append(' ').Append(uid);
                            }
                            Put(s, found + "\r\n" + tag + " OK done\r\n");
                        }
                        else if (upper.StartsWith("UID FETCH "))
                        {
                            Match m = Regex.Match(rest, @"^UID FETCH ([\d,]+) \((.*)\)$", RegexOptions.IgnoreCase);
                            bool peek = upper.Contains("BODY.PEEK");
                            if (!peek) SetSeenByFetch = true;
                            foreach (string idText in m.Groups[1].Value.Split(','))
                            {
                                int uid = int.Parse(idText);
                                string raw;
                                if (!box.TryGetValue(uid, out raw)) continue;
                                string flags = Seen.Contains(uid) && box == Messages ? "(\\Seen)" : "()";
                                string key, payload;
                                if (upper.Contains("HEADER.FIELDS"))
                                {
                                    key = "BODY[HEADER.FIELDS (FROM TO SUBJECT DATE MESSAGE-ID REFERENCES REPLY-TO)]";
                                    payload = raw.Substring(0, raw.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4);
                                }
                                else { key = upper.Contains("<0.") ? "BODY[]<0>" : "BODY[]"; payload = raw; }
                                DateTime at;
                                string when = upper.Contains("INTERNALDATE") ? " INTERNALDATE \"" + (Arrived.TryGetValue(uid, out at) ? at : DateTime.UtcNow).ToString("dd-MMM-yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " +0000\"" : "";
                                Put(s, "* " + (order.IndexOf(uid) + 1) + " FETCH (UID " + uid + " FLAGS " + flags + when + " " + key + " {" + Encoding.GetEncoding(28591).GetByteCount(payload) + "}\r\n" + payload + ")\r\n");
                            }
                            Put(s, tag + " OK done\r\n");
                        }
                        else if (upper.StartsWith("UID MOVE ") || upper.StartsWith("UID COPY "))
                        {
                            bool move = upper.StartsWith("UID MOVE ");
                            List<string> args = Quoted(rest.Substring(9));
                            Dictionary<int, string> target = args.Count < 2 ? null : Box(args[1]);
                            if (move && !Can("MOVE")) Put(s, tag + " BAD no MOVE here\r\n");
                            else if (readOnly) Put(s, tag + " NO [READ-ONLY] mailbox is read-only\r\n");
                            else if (target == null) Put(s, tag + " NO [TRYCREATE] no such mailbox\r\n");
                            else
                            {
                                StringBuilder said = new StringBuilder();
                                foreach (int uid in new List<int>(order))
                                {
                                    if (!InSet(uid, args[0], top)) continue;
                                    int copy = boxNext++;
                                    target[copy] = box[uid];
                                    if (!move) continue;
                                    said.Append("* OK [COPYUID " + Validity + " " + uid + " " + copy + "] moved\r\n* " + (order.IndexOf(uid) + 1) + " EXPUNGE\r\n");
                                    box.Remove(uid);
                                    order.Remove(uid);
                                }
                                Put(s, said + tag + " OK done\r\n");
                            }
                        }
                        else if (upper.StartsWith("UID STORE "))
                        {
                            List<string> args = Quoted(rest.Substring(10));
                            if (readOnly) Put(s, tag + " NO [READ-ONLY] mailbox is read-only\r\n");
                            else
                            {
                                foreach (int uid in order) if (InSet(uid, args[0], top) && upper.Contains("\\DELETED")) Deleted.Add(uid);
                                Put(s, tag + " OK done\r\n");
                            }
                        }
                        else if (upper.StartsWith("UID EXPUNGE "))
                        {
                            List<string> args = Quoted(rest.Substring(12));
                            if (!Can("UIDPLUS")) Put(s, tag + " BAD no UIDPLUS here\r\n");
                            else
                            {
                                StringBuilder said = new StringBuilder();
                                foreach (int uid in new List<int>(order))
                                {
                                    if (!InSet(uid, args[0], top) || !Deleted.Contains(uid)) continue;
                                    said.Append("* " + (order.IndexOf(uid) + 1) + " EXPUNGE\r\n");
                                    box.Remove(uid);
                                    order.Remove(uid);
                                }
                                Put(s, said + tag + " OK done\r\n");
                            }
                        }
                        else if (upper == "EXPUNGE" || upper == "CLOSE")
                        {
                            foreach (int uid in order) if (Deleted.Contains(uid)) box.Remove(uid);
                            Put(s, tag + " OK done\r\n");
                        }
                        else Put(s, tag + " BAD unknown\r\n");
                    }
                }
            }

            private static List<string> Quoted(string text)
            {
                List<string> list = new List<string>();
                int i = 0;
                while (i < text.Length)
                {
                    if (text[i] == ' ') { i++; continue; }
                    StringBuilder sb = new StringBuilder();
                    if (text[i] == '"')
                    {
                        i++;
                        while (i < text.Length && text[i] != '"')
                        {
                            if (text[i] == '\\') i++;
                            sb.Append(text[i++]);
                        }
                        i++;
                    }
                    else while (i < text.Length && text[i] != ' ') sb.Append(text[i++]);
                    list.Add(sb.ToString());
                }
                return list;
            }
        }

        private sealed class FakeSmtp
        {
            internal TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            internal string Password = "good";
            internal string From;
            internal List<string> To = new List<string>();
            internal string Data;
            internal int Port { get { return ((IPEndPoint)Listener.LocalEndpoint).Port; } }

            internal void Start()
            {
                Listener.Start();
                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        TcpClient c;
                        try { c = Listener.AcceptTcpClient(); }
                        catch (Exception) { return; }
                        Thread w = new Thread(delegate() { try { Serve(c); } catch (Exception) { } });
                        w.IsBackground = true;
                        w.Start();
                    }
                });
                t.IsBackground = true;
                t.Start();
            }

            private static string Line(Stream s)
            {
                StringBuilder sb = new StringBuilder();
                while (true)
                {
                    int b = s.ReadByte();
                    if (b < 0) return sb.Length == 0 ? null : sb.ToString();
                    if (b == '\n') return sb.ToString().TrimEnd('\r');
                    sb.Append((char)b);
                }
            }

            private static void Put(Stream s, string text)
            {
                byte[] b = Encoding.ASCII.GetBytes(text);
                s.Write(b, 0, b.Length);
                s.Flush();
            }

            private void Serve(TcpClient c)
            {
                Stream s = c.GetStream();
                Put(s, "220 fake smtp\r\n");
                bool auth = false;
                while (true)
                {
                    string line = Line(s);
                    if (line == null) return;
                    string up = line.ToUpperInvariant();
                    if (up.StartsWith("EHLO")) Put(s, "250-fake\r\n250 AUTH PLAIN LOGIN\r\n");
                    else if (up.StartsWith("AUTH PLAIN "))
                    {
                        string[] f = Encoding.UTF8.GetString(Convert.FromBase64String(line.Substring(11))).Split('\0');
                        if (f.Length == 3 && f[1] == "me@example.com" && f[2] == Password) { auth = true; Put(s, "235 ok\r\n"); }
                        else Put(s, "535 5.7.8 bad credentials\r\n");
                    }
                    else if (up.StartsWith("MAIL FROM:")) { if (!auth) Put(s, "530 auth first\r\n"); else { From = line.Substring(10).Trim('<', '>'); To.Clear(); Put(s, "250 ok\r\n"); } }
                    else if (up.StartsWith("RCPT TO:")) { To.Add(line.Substring(8).Trim('<', '>')); Put(s, "250 ok\r\n"); }
                    else if (up == "DATA")
                    {
                        Put(s, "354 go\r\n");
                        StringBuilder sb = new StringBuilder();
                        while (true)
                        {
                            string l = Line(s);
                            if (l == null || l == ".") break;
                            sb.Append(l).Append("\r\n");
                        }
                        Data = sb.ToString();
                        Put(s, "250 queued\r\n");
                    }
                    else if (up == "QUIT") { Put(s, "221 bye\r\n"); return; }
                    else Put(s, "502 no\r\n");
                }
            }
        }

        // A pretend OpenAI brain: it gives these answers in turn, then keeps giving the last one, and keeps what each ask sent.
        private sealed class FakeBrain
        {
            internal TcpListener Listener = new TcpListener(IPAddress.Loopback, 0);
            internal List<string> Asked = new List<string>();
            internal string Address { get { return "http://127.0.0.1:" + ((IPEndPoint)Listener.LocalEndpoint).Port; } }

            internal void Start(params string[] replies)
            {
                Listener.Start();
                Thread t = new Thread(delegate()
                {
                    for (int i = 0; ; i++)
                    {
                        try
                        {
                            using (TcpClient c = Listener.AcceptTcpClient())
                            using (NetworkStream s = c.GetStream())
                            {
                                MemoryStream got = new MemoryStream();
                                byte[] buffer = new byte[8192];
                                string text = "";
                                while (true)
                                {
                                    int read = s.Read(buffer, 0, buffer.Length);
                                    if (read <= 0) break;
                                    got.Write(buffer, 0, read);
                                    text = Encoding.UTF8.GetString(got.ToArray());
                                    int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                    if (end < 0) continue;
                                    Match length = Regex.Match(text, @"Content-Length:\s*(\d+)", RegexOptions.IgnoreCase);
                                    if (got.Length >= end + 4 + (length.Success ? int.Parse(length.Groups[1].Value) : 0)) break;
                                }
                                lock (Asked) Asked.Add(text);
                                byte[] body = Encoding.UTF8.GetBytes(replies[Math.Min(i, replies.Length - 1)]);
                                byte[] head = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: " + body.Length + "\r\nConnection: close\r\n\r\n");
                                s.Write(head, 0, head.Length);
                                s.Write(body, 0, body.Length);
                            }
                        }
                        catch (Exception) { return; }
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
        }

        private static void Need(bool ok, string what)
        {
            if (!ok) throw new Exception("Email check failed: " + what);
        }

        private static string Reply(object result, out bool error)
        {
            Dictionary<string, object> r = (Dictionary<string, object>)result;
            error = r.ContainsKey("isError");
            List<object> content = (List<object>)r["content"];
            return (string)((Dictionary<string, object>)content[0])["text"];
        }

        private static Dictionary<string, object> Args(params object[] pairs)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[(string)pairs[i]] = pairs[i + 1];
            return d;
        }

        private static string Raw(params string[] lines) { return string.Join("\r\n", lines); }

        internal static void Run(List<string> checks, string folder)
        {
            FakeImap imap = new FakeImap();
            imap.Messages[1] = Raw("From: Alice <alice@example.com>", "To: me@example.com", "Subject: Lunch", "Date: Mon, 02 Mar 2026 10:00:00 +0000", "Message-ID: <m1@example.com>", "Content-Type: text/plain; charset=us-ascii", "", "See you at noon.", ".", "End");
            string qp = Raw("From: =?UTF-8?B?Sm9zw6k=?= <jose@example.com>", "To: me@example.com", "Subject: =?utf-8?Q?Caf=C3=A9_?= =?utf-8?Q?menu?=", "Date: Tue, 03 Mar 2026 09:30:00 +1000", "Message-ID: <m2@example.com>", "References: <m0@example.com>", "MIME-Version: 1.0", "Content-Type: multipart/alternative; boundary=\"alt\"", "",
                "--alt", "Content-Type: text/plain; charset=utf-8", "Content-Transfer-Encoding: quoted-printable", "", "Caf=C3=A9 costs =E2=82=AC5 and this line is soft=", " wrapped.", "--alt", "Content-Type: text/html; charset=utf-8", "", "<p>HTML ONLY</p>", "--alt--", "");
            imap.Messages[2] = qp;
            string b64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("Base64 body üñï text"));
            imap.Messages[3] = Raw("From: Bob <bob@example.com>", "To: me@example.com", "Subject: Report", "Date: Wed, 04 Mar 2026 08:00:00 +0000", "Message-ID: <m3@example.com>", "MIME-Version: 1.0", "Content-Type: multipart/mixed; boundary=\"mix\"", "",
                "--mix", "Content-Type: text/plain; charset=utf-8", "Content-Transfer-Encoding: base64", "", b64, "--mix", "Content-Type: application/pdf; name=\"report.pdf\"", "Content-Disposition: attachment; filename=\"report.pdf\"", "Content-Transfer-Encoding: base64", "", "AAAA", "--mix--", "");
            imap.Seen.Add(1);
            imap.Start();
            FakeSmtp smtp = new FakeSmtp();
            smtp.Start();
            string saved = MailTools.Folder;
            MailAccount savedAccount = MailTools.Account;
            try
            {
                MailAccount a = new MailAccount();
                a.Address = "me@example.com"; a.Login = "me@example.com"; a.Name = "Me é";
                a.ImapHost = "127.0.0.1"; a.ImapPort = imap.Port; a.SmtpHost = "127.0.0.1"; a.SmtpPort = smtp.Port; a.SmtpImplicitTls = false; a.Plain = true;
                a.Password = "good";
                MailTools.Account = a;
                bool err;

                string text = Reply(MailTools.Call("recent_mail", Args()), out err);
                Need(!err && text.IndexOf("3 | 2026-03-04 08:00 | Bob <bob@example.com> | Report (unread)", StringComparison.Ordinal) >= 0, "recent mail line: " + text);
                Need(text.IndexOf("2 | 2026-03-03 09:30 | José <jose@example.com> | Café menu (unread)", StringComparison.Ordinal) >= 0, "encoded sender and subject: " + text);
                Need(text.IndexOf("1 | 2026-03-02 10:00 | Alice <alice@example.com> | Lunch\n", StringComparison.Ordinal) < 0 && text.TrimEnd().EndsWith("| Lunch"), "read mail isn't marked unread: " + text);
                Need(text.IndexOf("3 | ") < text.IndexOf("2 | ") && text.IndexOf("2 | ") < text.IndexOf("1 | "), "newest first");
                text = Reply(MailTools.Call("recent_mail", Args("unread_only", true, "count", 1)), out err);
                Need(!err && text.Contains("3 | ") && !text.Contains("2 | ") && !text.Contains("1 | "), "unread only with count: " + text);
                text = Reply(MailTools.Call("search_mail", Args("query", "noon", "from", "alice")), out err);
                Need(!err && text.Contains("1 | ") && !text.Contains("2 | "), "search: " + text);
                text = Reply(MailTools.Call("search_mail", Args("query", "café")), out err);
                Need(!err, "search with accents: " + text);
                text = Reply(MailTools.Call("search_mail", Args()), out err);
                Need(err, "search needs something to look for");
                text = Reply(MailTools.Call("mail_folders", Args()), out err);
                Need(!err && text.Contains("INBOX") && text.Contains("Sent") && text.Contains("Café") && !text.Contains("[Gmail]"), "folders: " + text);
                text = Reply(MailTools.Call("recent_mail", Args("folder", "Nope")), out err);
                Need(err && text.Contains("Nope"), "missing folder: " + text);

                text = Reply(MailTools.Call("read_mail", Args("uid", 2)), out err);
                Need(!err && text.Contains("Subject: Café menu") && text.Contains("Café costs €5 and this line is soft wrapped.") && !text.Contains("HTML ONLY"), "multipart quoted-printable body: " + text);
                text = Reply(MailTools.Call("read_mail", Args("uid", 3)), out err);
                Need(!err && text.Contains("Base64 body üñï text") && text.Contains("report.pdf"), "base64 body and attachment name: " + text);
                text = Reply(MailTools.Call("read_mail", Args("uid", 1)), out err);
                Need(!err && text.Contains("\n.\nEnd"), "plain body: " + text);
                text = Reply(MailTools.Call("read_mail", Args("uid", 99)), out err);
                Need(err, "missing uid");
                Need(!imap.SetSeenByFetch && !imap.Seen.Contains(2) && !imap.Seen.Contains(3), "reading never asks for \\Seen");
                string html = MailMime.HtmlToText("<html><head><style>p{}</style></head><body><p>Hi&nbsp;there &amp; you</p>Next<script>x()</script></body></html>");
                Need(html == "Hi there & you\nNext", "html to text: " + html);
                DateTime hostileStart = DateTime.UtcNow;
                string unclosed = MailMime.HtmlToText("<b>Hi</b> " + new string('<', 150000));
                string comments = MailMime.HtmlToText("<p>Yo</p>" + new StringBuilder().Insert(0, "<!--x", 30000).ToString());
                double hostileSeconds = (DateTime.UtcNow - hostileStart).TotalSeconds;
                Need(unclosed.StartsWith("Hi", StringComparison.Ordinal) && comments.StartsWith("Yo", StringComparison.Ordinal) && hostileSeconds < 15, "hostile html gives up quickly: " + hostileSeconds + " s");
                checks.Add("PASS mail: lists, searches and reads encoded, multipart and base64 messages without marking them read, and hostile html can't hang her");

                text = Reply(MailTools.Call("send_mail", Args("to", "Pat <pat@example.com>, lee@example.com", "subject", "Café ☕ plans", "body", "Hello é\r\n.starts with a dot\r\nBye", "reply_to_uid", 2)), out err);
                Need(!err && smtp.From == "me@example.com" && smtp.To.Count == 2 && smtp.To[0] == "pat@example.com", "send: " + text);
                Need(smtp.Data.Contains("\r\n..starts with a dot") || smtp.Data.Contains("=\r\n..starts"), "dot-stuffing was applied");
                MailPart sent = MailMime.Parse(smtp.Data.Replace("\r\n..", "\r\n."), 0);
                Need(MailMime.DecodeHeader(sent.Header("subject")) == "Café ☕ plans" && sent.Header("subject").StartsWith("=?utf-8?B?"), "subject encoded");
                Need(sent.Header("in-reply-to") == "<m2@example.com>" && sent.Header("references") == "<m0@example.com> <m2@example.com>", "reply headers");
                Need(MailMime.DecodeHeader(sent.Header("from")) == "Me é <me@example.com>" && sent.Header("message-id").StartsWith("<") && sent.Header("date").Length > 10 && sent.Header("mime-version") == "1.0", "sender, id, date");
                List<string> none = new List<string>();
                string sentBody = MailMime.Body(sent, none);
                Need(sentBody == "Hello é\n.starts with a dot\nBye", "sent body round trip: " + sentBody);
                smtp.Data = null;
                text = Reply(MailTools.Call("send_mail", Args("body", "Thanks", "reply_to_uid", 2)), out err);
                Need(err && smtp.Data == null && text.Contains("Say who"), "a reply with no recipient is refused: " + text);
                text = Reply(MailTools.Call("send_mail", Args("to", "jose@example.com", "body", "Thanks", "reply_to_uid", 2)), out err);
                MailPart again = MailMime.Parse(smtp.Data, 0);
                Need(!err && smtp.To[0] == "jose@example.com" && MailMime.DecodeHeader(again.Header("subject")) == "Re: Café menu", "reply keeps the Re: subject: " + text);
                text = Reply(MailTools.Call("send_mail", Args("to", "not-an-address", "subject", "x", "body", "y")), out err);
                Need(err && text.Contains("not-an-address"), "bad address refused");
                text = Reply(MailTools.Call("send_mail", Args("to", "a1@example.com,a2@example.com,a3@example.com,a4@example.com,a5@example.com,a6@example.com,a7@example.com,a8@example.com,a9@example.com,a10@example.com,a11@example.com", "subject", "x", "body", "y")), out err);
                Need(err && text.Contains("10"), "recipient cap");
                checks.Add("PASS mail: sends a correct message with dot-stuffing, an encoded subject and reply headers, and never guesses who a reply goes to");

                a.Password = "wrong-pass-xyz";
                text = Reply(MailTools.Call("recent_mail", Args()), out err);
                Need(err && text.Contains("refused the sign-in") && !text.Contains("wrong-pass-xyz"), "wrong password: " + text);
                text = Reply(MailTools.Call("send_mail", Args("to", "pat@example.com", "subject", "x", "body", "y")), out err);
                Need(err && text.Contains("refused the sign-in") && !text.Contains("wrong-pass-xyz"), "wrong smtp password: " + text);
                string tested = MailTools.Test(a);
                Need(tested != null && !tested.Contains("wrong-pass-xyz"), "Test reports a wrong password plainly");
                a.Password = "good";
                Need(MailTools.Test(a) == null, "Test passes with the right password");
                imap.Password = Tricky;
                smtp.Password = Tricky;
                a.Password = Tricky;
                text = Reply(MailTools.Call("recent_mail", Args()), out err);
                Need(!err && text.Contains("| Report"), "password with quote, backslash and accent: " + text);
                Need(MailTools.Test(a) == null, "Test with the awkward password");
                checks.Add("PASS mail: wrong passwords give a plain error, awkward passwords sign in, Test checks both servers");

                MailAccount noAcct = MailAccount.ForAddress("someone@outlook.com");
                Need(noAcct == null && MailAccount.Unsupported("a@hotmail.com") == MailAccount.OutlookNote && MailTools.Test(new MailAccount { Address = "a@live.com", Password = "x" }) == MailAccount.OutlookNote, "outlook refused plainly");
                MailAccount gm = MailAccount.ForAddress("x@gmail.com");
                Need(gm.ImapHost == "imap.gmail.com" && gm.SmtpPort == 465 && gm.SmtpImplicitTls, "gmail preset");
                MailAccount ic = MailAccount.ForAddress("x@icloud.com");
                Need(ic.SmtpPort == 587 && !ic.SmtpImplicitTls && ic.LoginName == "x@icloud.com", "icloud preset");
                Need(!MailAccount.ForAddress("x@unknown.example").Known, "unknown domains need hosts");
                try
                {
                    MailWire.Connect("mail.example.com", 143, false, true);
                    Need(false, "plain mode refused off this computer");
                }
                catch (MailException) { }

                string dir = Path.Combine(folder, "mail-check");
                Directory.CreateDirectory(dir);
                MailTools.Folder = dir;
                MailAccount.Delete(dir);
                MailTools.Account = null;
                text = Reply(MailTools.Call("recent_mail", Args()), out err);
                Need(err && text == MailTools.NotSetUp, "no account says to set it up");
                Need(MailTools.NotSetUp == "Email isn't connected yet. They can connect it with Email in Mainframe's Mods page, under Plug-ins.", "setup message");
                a.Password = "Sup3r-secret-pw";
                a.Save(dir);
                string file = File.ReadAllText(MailAccount.FilePath(dir));
                Need(!file.Contains("Sup3r-secret-pw") && file.Contains("me@example.com"), "the saved file keeps the password out");
                MailAccount back = MailAccount.Load(dir);
                Need(back != null && back.Address == "me@example.com" && back.ImapPort == imap.Port, "account loads back");
#if !CROSS
                Need(back.Password == "Sup3r-secret-pw", "password comes back through DPAPI");
#else
                Need(back.Password.Length == 0, "password isn't saved on this system");
#endif
                MailAccount.Delete(dir);
                checks.Add("PASS mail: no account asks for setup, Outlook is refused plainly, and the saved file never holds the password");

                CheckPhish(checks);
                CheckWatch(checks);
                CheckMoves(checks, folder);
            }
            finally
            {
                MailTools.Folder = saved;
                MailTools.Account = savedAccount;
                imap.Listener.Stop();
                smtp.Listener.Stop();
            }
        }

        private static bool Until(Func<bool> done, int seconds)
        {
            DateTime end = DateTime.UtcNow.AddSeconds(seconds);
            while (DateTime.UtcNow < end)
            {
                if (done()) return true;
                Thread.Sleep(50);
            }
            return done();
        }

        private static MailAccount Fake(int port)
        {
            MailAccount a = new MailAccount();
            a.Address = "me@example.com"; a.Login = "me@example.com"; a.Name = "Me";
            a.ImapHost = "127.0.0.1"; a.ImapPort = port; a.SmtpHost = "127.0.0.1"; a.SmtpPort = 1; a.SmtpImplicitTls = false; a.Plain = true;
            a.Password = "good";
            return a;
        }

        private static FakeImap Gmail()
        {
            FakeImap imap = new FakeImap();
            imap.Caps = "IDLE MOVE UIDPLUS SPECIAL-USE LIST-EXTENDED X-GM-EXT-1";
            imap.CapsOnLogin = true;
            foreach (string[] box in new[] { new[] { "[Gmail]/All Mail", "\\All" }, new[] { "[Gmail]/Spam", "\\Junk" }, new[] { "[Gmail]/Trash", "\\Trash" }, new[] { "Receipts", "" } })
            {
                imap.Boxes[box[0]] = new Dictionary<int, string>();
                if (box[1].Length > 0) imap.Uses[box[0]] = box[1];
            }
            return imap;
        }

        internal static string Staged(string id, string link)
        {
            return Raw("From: Arasaka Account Support <arasaka.alerts.desk@gmail.com>", "To: me@example.com", "Subject: Action required: your Arasaka account will be suspended", "Date: Fri, 20 Nov 2026 21:04:00 +0000", "Message-ID: <" + id + "@mail.gmail.com>", "MIME-Version: 1.0", "Content-Type: multipart/alternative; boundary=\"b1\"", "",
                "--b1", "Content-Type: text/plain; charset=UTF-8", "", "We noticed an unusual sign-in to your Arasaka account. Verify your account within 24 hours or it will be suspended.", "", "https://account.arasaka.example/security <" + link + ">", "", "Arasaka Account Support",
                "--b1", "Content-Type: text/html; charset=UTF-8", "", "<div>We noticed an unusual sign-in to your Arasaka account. Verify your account within 24 hours or it will be suspended.</div><div><a href=\"" + link + "\">https://account.arasaka.example/security</a></div><div>Arasaka Account Support</div>", "--b1--", "");
        }

        private static string Newsletter(string id)
        {
            return Raw("From: The Weekly Byte <news@weeklybyte.com>", "To: me@example.com", "Subject: This week: three tools we love", "Date: Fri, 20 Nov 2026 07:00:00 +0000", "Message-ID: <" + id + "@weeklybyte.com>", "List-Unsubscribe: <https://weeklybyte.us12.list-manage.com/unsubscribe?u=1>",
                "Authentication-Results: mx.google.com; dkim=pass header.i=@weeklybyte.com; spf=pass smtp.mailfrom=bounce@weeklybyte.com; dmarc=pass (p=NONE) header.from=weeklybyte.com", "MIME-Version: 1.0", "Content-Type: multipart/alternative; boundary=\"nl\"", "",
                "--nl", "Content-Type: text/plain; charset=UTF-8", "", "This week in tech: three tools we love, a deep dive on keyboards and your letters.", "Read the full story: https://weeklybyte.us12.list-manage.com/track/click?u=1&id=2", "Unsubscribe: https://weeklybyte.us12.list-manage.com/unsubscribe?u=1",
                "--nl", "Content-Type: text/html; charset=UTF-8", "", "<h1>This week in tech</h1><p>Three tools we love, a deep dive on keyboards and your letters.</p><a href=\"https://weeklybyte.us12.list-manage.com/track/click?u=1&amp;id=2\">Read the full story</a> <a href=\"https://weeklybyte.com/issues/48\">weeklybyte.com/issues/48</a> <img src=\"https://weeklybyte.com/logo.png\"> <a href=\"https://weeklybyte.us12.list-manage.com/unsubscribe?u=1\">Unsubscribe</a>", "--nl--", "");
        }

        private static string Personal(string id)
        {
            return Raw("From: Mum <jan.walker@gmail.com>", "To: me@example.com", "Subject: Sunday", "Date: Fri, 20 Nov 2026 18:30:00 +0000", "Message-ID: <" + id + "@mail.gmail.com>", "Content-Type: text/plain; charset=UTF-8", "", "Hey love, are you still coming for dinner on Sunday? Dad's doing a roast. Let me know by Friday x");
        }

        private static string Letter(string from, string subject, string body, bool html, string[] headers, params string[] files)
        {
            List<string> lines = new List<string> { "From: " + from, "To: me@example.com", "Subject: " + subject, "Date: Fri, 20 Nov 2026 09:00:00 +0000", "Message-ID: <" + Guid.NewGuid().ToString("N") + "@example.com>", "MIME-Version: 1.0" };
            if (headers != null) lines.AddRange(headers);
            string type = "Content-Type: " + (html ? "text/html" : "text/plain") + "; charset=utf-8";
            if (files.Length == 0) { lines.Add(type); lines.Add(""); lines.Add(body); return Raw(lines.ToArray()); }
            lines.AddRange(new[] { "Content-Type: multipart/mixed; boundary=\"mix\"", "", "--mix", type, "", body });
            foreach (string file in files)
            {
                if (file.StartsWith("*", StringComparison.Ordinal)) lines.AddRange(new[] { "--mix", "Content-Type: application/octet-stream", "Content-Disposition: attachment; filename*=" + file.Substring(1), "Content-Transfer-Encoding: base64", "", "AAAA" });
                else lines.AddRange(new[] { "--mix", "Content-Type: application/octet-stream; name=\"" + file + "\"", "Content-Disposition: attachment; filename=\"" + file + "\"", "Content-Transfer-Encoding: base64", "", "AAAA" });
            }
            lines.Add("--mix--");
            lines.Add("");
            return Raw(lines.ToArray());
        }

        private static void Flags(string what, MailVerdict v, string reason)
        {
            Need(v.Level != MailVerdict.Fine || v.Reasons.Count > 0, what + " came out fine with nothing to say");
            Need(v.Reasons.Exists(r => r.IndexOf(reason, StringComparison.Ordinal) >= 0), what + " wasn't flagged with \"" + reason + "\": " + v.Level + " " + v.Score + " | " + string.Join(" | ", v.Reasons.ToArray()));
        }

        private static MailVerdict Link(string href, string text)
        {
            return MailPhish.Check(Letter("Team <team@northwind.com>", "Hello", "<p>Hi there.</p><a href=\"" + href + "\">" + text + "</a>", true, null), null);
        }

        private static void CheckPhish(List<string> checks)
        {
            MailVerdict staged = MailPhish.Check(Staged("stage", "https://arasaka-acc0unt-verify.invalid/login"), null);
            Need(staged.Level == MailVerdict.Phishing && staged.Sure, "the staged phishing mail wasn't sure phishing: " + staged.Level + " " + staged.Score + " | " + string.Join(" | ", staged.Reasons.ToArray()));
            Flags("staged", staged, "It says it's from Arasaka Account Support, but it came from a personal gmail.com address.");
            Flags("staged", staged, "A link shows account.arasaka.example but really goes to arasaka-acc0unt-verify.invalid.");
            Flags("staged", staged, "arasaka-acc0unt-verify.invalid wraps Arasaka in extra words to look official.");
            Flags("staged", staged, "spells account as acc0unt");
            Flags("staged", staged, "It points at made-up addresses (.example, .invalid)");
            Flags("staged", staged, "\"suspended\"");
            Flags("brand name", MailPhish.Check(Letter("PayPal Service <service@paypal-billing.xyz>", "Your receipt", "Thanks for your payment.", false, null), null), "It says it's from PayPal, but it came from paypal-billing.xyz");
            Flags("brand from webmail", MailPhish.Check(Letter("Netflix Billing <netflix.billing.team@outlook.com>", "Payment", "Hi.", false, null), null), "It says it's from Netflix, but it came from a personal outlook.com address.");
            Flags("reply-to", MailPhish.Check(Letter("Northwind Billing <billing@northwind.com>", "Invoice", "See you soon.", false, new[] { "Reply-To: <northwind.payments@gmail.com>" }), null), "Replies would go to a personal gmail.com address, not to northwind.com.");
            Flags("punycode", Link("https://" + new System.Globalization.IdnMapping().GetAscii("p" + (char)0x0430 + "ypal.com") + "/signin", "Sign in"), "spells PayPal with letters from another alphabet");
            Flags("rn for m", Link("https://rnicrosoft.com/login", "Sign in"), "rnicrosoft.com looks like Microsoft's address");
            Flags("0 for o", Link("https://g00gle.com/", "Open"), "g00gle.com looks like Google's address");
            Flags("1 for l", Link("https://paypa1.com/", "Open"), "paypa1.com looks like PayPal's address");
            Flags("vv for w", Link("https://tvvitter.com/", "Open"), "tvvitter.com looks like X's address");
            Flags("extra words", Link("https://paypal-secure-login.com/", "Open"), "paypal-secure-login.com wraps PayPal's name in extra words");
            Flags("brand in front", Link("https://paypal.com.account-check.info/", "Open"), "puts PayPal's name in front of someone else's address (account-check.info)");
            Flags("odd ending", Link("https://login-portal.top/x", "Open"), "ending in .top");
            Flags("link text", Link("https://evil-site.com/", "https://www.netflix.com/account"), "A link shows www.netflix.com but really goes to evil-site.com.");
            Flags("raw ip", Link("http://192.168.10.5/login", "Log in"), "bare number address (192.168.10.5)");
            Flags("shortener", Link("https://bit.ly/3xYzAbc", "Open"), "link shortener (bit.ly)");
            Flags("@ trick", Link("https://paypal.com@evil-host.ru/login", "Open"), "A link pretends to go to paypal.com but really goes to evil-host.ru.");
            foreach (string[] ask in new[] { new[] { "Please buy three gift cards for a client today and send me the codes.", "\"gift cards\"" }, new[] { "Please arrange a wire transfer before noon.", "\"wire transfer\"" }, new[] { "Pay the fee in bitcoin to release it.", "\"bitcoin\"" }, new[] { "Your overdue invoice is attached below.", "\"overdue invoice\"" }, new[] { "Confirm your password to keep your mailbox.", "\"confirm your password\"" }, new[] { "Your account will be suspended.", "\"suspended\"" }, new[] { "Do it within 24 hours.", "\"within 24 hours\"" } })
                Flags("urgent ask", MailPhish.Check(Letter("Office <office@northwind.com>", "Hi", ask[0], false, null), null), ask[1]);
            foreach (string ending in new[] { "exe", "scr", "js", "vbs", "lnk", "iso", "img", "hta", "html" })
                Flags("." + ending + " attachment", MailPhish.Check(Letter("Office <office@northwind.com>", "Files", "Here you go.", false, null, "invoice." + ending), null), "of a kind that can run code");
            Flags("double ending", MailPhish.Check(Letter("Office <office@northwind.com>", "Files", "Here you go.", false, null, "invoice.pdf.exe"), null), "wears two endings");
            Flags("macro document", MailPhish.Check(Letter("Office <office@northwind.com>", "Files", "Here you go.", false, null, "budget.xlsm"), null), "macro document");
            Flags("macro letter", MailPhish.Check(Letter("Office <office@northwind.com>", "Files", "Here you go.", false, null, "letter.docm"), null), "macro document");
            Flags("zip password", MailPhish.Check(Letter("Office <office@northwind.com>", "Scan", "The password for the zip is 4471.", false, null, "scan.zip"), null), "locked archive");
            Flags("encoded name", MailPhish.Check(Letter("Office <office@northwind.com>", "Files", "Here you go.", false, null, "*utf-8''update%2Escr"), null), "update.scr");
            List<string> named = new List<string>();
            int at = 0;
            MailPhish.StructureNames(ImapParse.Value("((\"TEXT\" \"PLAIN\" (\"CHARSET\" \"UTF-8\") NIL NIL \"7BIT\" 10 1 NIL NIL NIL)(\"APPLICATION\" \"OCTET-STREAM\" (\"NAME\" \"setup.iso\") NIL NIL \"BASE64\" 100 NIL (\"ATTACHMENT\" (\"FILENAME\" \"setup.iso\")) NIL) \"MIXED\" (\"BOUNDARY\" \"x\") NIL NIL)", ref at), named);
            Need(named.Count == 1 && named[0] == "setup.iso", "attachment names from the server's outline: " + string.Join(", ", named.ToArray()));
            Flags("outline name", MailPhish.Check(Letter("Office <office@northwind.com>", "Files", "Here you go.", false, null), named), "setup.iso");
            MailVerdict auth = MailPhish.Check(Letter("PayPal <service@paypal.com>", "Notice", "Hello.", false, new[] { "Authentication-Results: mx.google.com; dkim=fail header.i=@paypal.com; spf=fail (google.com: domain of service@paypal.com does not designate 192.0.2.7 as permitted sender) smtp.mailfrom=service@paypal.com; dmarc=fail (p=REJECT sp=REJECT dis=QUARANTINE) header.from=paypal.com" }), null);
            Flags("dmarc", auth, "Gmail's own sender check failed: it didn't really come from paypal.com (DMARC).");
            Flags("spf", auth, "SPF failed");
            Flags("dkim", auth, "DKIM failed");
            checks.Add("PASS mail: her phishing check flags a fake sender name, replies sent elsewhere, look-alike and foreign-letter addresses, links that hide where they go, pushy asks, risky attachments and failed sender checks, each with a plain reason");

            MailVerdict news = MailPhish.Check(Newsletter("n1"), null), mum = MailPhish.Check(Personal("m1"), null);
            MailVerdict google = MailPhish.Check(Letter("Google <no-reply@accounts.google.com>", "Security alert", "A new sign-in on Windows. If this was you, you don't need to do anything. If not, we'll help you secure your account. Check activity: https://accounts.google.com/AccountChooser?continue=https://myaccount.google.com/notifications", false, null), null);
            Need(news.Level == MailVerdict.Fine && news.Reasons.Count == 0, "a newsletter wasn't fine: " + string.Join(" | ", news.Reasons.ToArray()));
            Need(mum.Level == MailVerdict.Fine && mum.Reasons.Count == 0, "a personal email wasn't fine: " + string.Join(" | ", mum.Reasons.ToArray()));
            Need(google.Level == MailVerdict.Fine, "a real security alert from Google wasn't fine: " + string.Join(" | ", google.Reasons.ToArray()));
            string said = MailPhish.Said(staged, false);
            Need(said.StartsWith("Verdict: phishing (almost certainly a scam).\nFrom: Arasaka Account Support <arasaka.alerts.desk@gmail.com>\n", StringComparison.Ordinal) && said.Contains("\nWhy:\n- ") && said.EndsWith("Its links and attachments weren't opened, and it's still unread.", StringComparison.Ordinal), "her phishing answer: " + said);
            checks.Add("PASS mail: a newsletter, a personal email and a real security alert pass her phishing check with nothing to say");

            MailVerdict his = MailPhish.Check(Letter("Daniel Hart <daniel.hart.2026@gmail.com>", "Microsoft account security notice", "Dear customer,\r\n\r\nThis is the Microsoft Account Team. We need to verify your account. Please reply to this email with your full credit card number, the expiry date and the CVV, and your Microsoft account password.\r\n\r\nThanks,\r\nMicrosoft Account Team", false, null), null);
            Need(his.Level == MailVerdict.Phishing && his.Sure, "an email naming Microsoft from gmail.com and asking for a card and a password wasn't sure phishing: " + his.Level + " " + his.Score + " | " + string.Join(" | ", his.Reasons.ToArray()));
            Flags("named company", his, "It says it's from Microsoft, but it came from a personal gmail.com address.");
            Flags("card ask", his, "It asks for your card details. Real companies don't ask for those by email.");
            Flags("password ask", his, "It asks for your password. Real companies never ask for it by email.");
            string hisSaid = MailPhish.Said(his, false);
            Need(hisSaid.StartsWith("Verdict: phishing (almost certainly a scam).", StringComparison.Ordinal) && hisSaid.Contains("\n- It says it's from Microsoft") && hisSaid.Contains("\n- It asks for your card details.") && hisSaid.Contains("\n- It asks for your password."), "asking if it's legit didn't give the new reasons: " + hisSaid);
            string hisLine = MailWatch.Line(new List<MailNews> { new MailNews { From = his.From, Subject = his.Subject, Verdict = his } });
            Need(hisLine.Contains("phishing") && hisLine.EndsWith("your card details and password, so don't reply or click anything in it.", StringComparison.Ordinal), "her line for phishing after a card and a password: " + hisLine);
            MailVerdict cardOnly = MailPhish.Check(Letter("Sam Lee <sam.lee.84@gmail.com>", "Quick favour", "Hey, can you send me your card number and the CVV on the back? I'll pay you back on Friday.", false, null), null);
            MailVerdict passwordOnly = MailPhish.Check(Letter("Chris Parker <chris.parker.77@yahoo.com>", "Mailbox full", "Your mailbox is almost full. Reply with your email password and we'll add more space today.", false, null), null);
            Need(cardOnly.Level != MailVerdict.Fine && cardOnly.Wants == "card details", "an email asking for card details wasn't flagged: " + cardOnly.Level + " " + cardOnly.Score + " | " + string.Join(" | ", cardOnly.Reasons.ToArray()));
            Need(passwordOnly.Level != MailVerdict.Fine && passwordOnly.Wants == "password", "an email asking for a password wasn't flagged: " + passwordOnly.Level + " " + passwordOnly.Score + " | " + string.Join(" | ", passwordOnly.Reasons.ToArray()));
            Flags("card ask", cardOnly, "It asks for your card details.");
            Flags("password ask", passwordOnly, "It asks for your password.");
            string oddLine = MailWatch.Line(new List<MailNews> { new MailNews { From = passwordOnly.From, Subject = passwordOnly.Subject, Verdict = passwordOnly } });
            Need(oddLine.Contains("your password, so ") && oddLine.EndsWith("Want me to check it?", StringComparison.Ordinal), "her line for an email after a password: " + oddLine);
            string[] listed = { "List-Unsubscribe: <https://weeklybyte.us12.list-manage.com/unsubscribe?u=1>", "Authentication-Results: mx.google.com; dkim=pass header.i=@weeklybyte.com; spf=pass smtp.mailfrom=bounce@weeklybyte.com; dmarc=pass (p=NONE) header.from=weeklybyte.com" };
            MailVerdict[] quiet =
            {
                MailPhish.Check(Letter("Jake <jake.m.2009@gmail.com>", "tonight", "I forgot my Xbox password lol, so I'm stuck on the login screen. Fixing it after dinner, then we can play.", false, null), null),
                MailPhish.Check(Letter("The Weekly Byte <news@weeklybyte.com>", "This week: a new Xbox from Microsoft", "Microsoft shipped a new Xbox this week, and the Microsoft Support team says the update rolls out slowly. Read the full story: https://weeklybyte.com/issues/49", false, listed), null),
                MailPhish.Check(Letter("Microsoft account team <account-security-noreply@accountprotection.microsoft.com>", "Microsoft account unusual sign-in activity", "We detected something unusual about a recent sign-in to the Microsoft account ja**@outlook.com.\r\n\r\nIf this was you, then you can safely ignore this email. If you're not sure this was you, a malicious user might have your password. Please review your recent activity and we'll help you take corrective action.\r\n\r\nReview recent activity: https://account.live.com/Activity\r\n\r\nMicrosoft will never ask you to send your password or card details by email.\r\n\r\nThanks,\r\nThe Microsoft account team", false, new[] { "Authentication-Results: mx.google.com; dkim=pass header.i=@accountprotection.microsoft.com; spf=pass smtp.mailfrom=account-security-noreply@accountprotection.microsoft.com; dmarc=pass (p=REJECT sp=REJECT dis=NONE) header.from=accountprotection.microsoft.com" }), null)
            };
            foreach (MailVerdict one in quiet) Need(one.Level == MailVerdict.Fine && one.Reasons.Count == 0, "a friend's lost Xbox password, a newsletter or a real Microsoft alert wasn't fine: " + one.Subject + " | " + string.Join(" | ", one.Reasons.ToArray()));
            MailVerdict[] calm =
            {
                MailPhish.Check(Letter("The Weekly Byte <news@weeklybyte.com>", "This week in security", "The Microsoft Security Team says thousands of accounts were compromised in a password spraying wave. Turn on two-step sign-in.", false, listed), null),
                MailPhish.Check(Letter("Sam Lee <sam.lee.84@gmail.com>", "Fwd: Microsoft account unusual sign-in activity", "Is this real?\r\n\r\n---------- Forwarded message ---------\r\nFrom: Microsoft account team <account-security-noreply@accountprotection.microsoft.com>\r\nDate: Fri, 20 Nov 2026 at 08:00\r\nSubject: Microsoft account unusual sign-in activity\r\n\r\nWe detected unusual sign-in activity on your Microsoft account. Please review your recent activity.\r\n\r\nThanks,\r\nThe Microsoft account team", false, null), null),
                MailPhish.Check(Letter("Northwind Bank <alerts@northwind.com>", "Your statement is ready", "Your November statement is ready in online banking. We'll never ask you to send your password, PIN or card details by email, and if anyone asks you to type your password into a link, it's a scam.", false, null), null),
                MailPhish.Check(Letter("Northwind <noreply@northwind.com>", "Reset your password", "Someone asked to reset your Northwind password. Enter your email address on the reset page, then type your new password twice. If this wasn't you, ignore this email.", false, null), null)
            };
            foreach (MailVerdict one in calm) Need(one.Level == MailVerdict.Fine && one.Wants.Length == 0 && !one.Reasons.Exists(r => r.StartsWith("It says it's from", StringComparison.Ordinal)), "a security newsletter, a forward, a bank's warning or a password reset was flagged: " + one.Subject + " | " + one.Level + " " + one.Score + " | " + string.Join(" | ", one.Reasons.ToArray()));
            Need(staged.Wants.Length == 0 && !staged.Reasons.Exists(r => r.StartsWith("It asks for your", StringComparison.Ordinal)), "the staged phishing mail picked up an ask it doesn't make: " + string.Join(" | ", staged.Reasons.ToArray()));
            checks.Add("PASS mail: she flags an email that names a company it didn't come from and asks for your card details or password, warns about either ask on its own, and stays quiet on a friend's lost password, newsletters, a forward, a bank's warning, a password reset and a real Microsoft alert");
        }

        private static void CheckWatch(List<string> checks)
        {
            TcpListener bait = new TcpListener(IPAddress.Loopback, 0);
            bait.Start();
            string trap = "http://127.0.0.1:" + ((IPEndPoint)bait.LocalEndpoint).Port + "/verify";
            FakeImap imap = Gmail();
            imap.Messages[1] = Personal("old1");
            imap.Messages[2] = Newsletter("old2");
            imap.Messages[3] = Staged("old3", "https://arasaka-acc0unt-verify.invalid/login");
            imap.Start();
            MailWatch watch = new MailWatch(Fake(imap.Port)) { IdleMilliseconds = 1500, FirstWait = 1 };
            try
            {
                watch.Start();
                Need(Until(delegate { return watch.State == "watching"; }, 15), "she never started watching: " + watch.State);
                Thread.Sleep(600);
                Need(!watch.Waiting, "she announced mail that was already in the inbox");
                int fresh = imap.Arrive(Newsletter("new1"), DateTime.UtcNow);
                Need(Until(delegate { return watch.Waiting; }, 15), "she didn't notice new mail");
                List<MailNews> news = watch.Take();
                Need(news.Count == 1 && news[0].Uid == fresh && news[0].Verdict.Level == MailVerdict.Fine && news[0].Moved == null, "new mail wasn't noticed once and fine: " + news.Count);
                string line = MailWatch.Line(news);
                Need(line.Length > 0 && line.Length < 60 && !line.Contains("phishing"), "her new mail line: " + line);
                Thread.Sleep(2200);
                Need(!watch.Waiting && imap.Logged("IDLE") && imap.Logged("DONE"), "she announced the same mail twice, or didn't idle and come back");
                imap.Arrive(Staged("p1", trap), DateTime.UtcNow);
                Need(Until(delegate { return watch.Waiting; }, 15), "she didn't notice the phishing mail");
                news = watch.Take();
                line = MailWatch.Line(news);
                Need(news.Count == 1 && news[0].Phishing && news[0].Moved == null && imap.Holds("INBOX", "<p1@") && line.Contains("phishing") && line.EndsWith("Don't click anything in it.", StringComparison.Ordinal), "phishing without her own hands: " + line);
                watch.OwnHands = delegate { return true; };
                imap.Arrive(Staged("p2", trap), DateTime.UtcNow);
                Need(Until(delegate { return watch.Waiting; }, 15), "she didn't notice the second phishing mail");
                news = watch.Take();
                line = MailWatch.Line(news);
                Need(news.Count == 1 && news[0].Moved == "[Gmail]/Spam" && news[0].Spam && news[0].Gmail && line.EndsWith(" That's phishing, I reported it as spam and binned it.", StringComparison.Ordinal), "dealing with phishing on Gmail: " + line + " | " + news[0].Trouble);
                Need(imap.Holds("[Gmail]/Spam", "<p2@") && !imap.Holds("INBOX", "<p2@") && imap.Holds("INBOX", "<p1@") && imap.Holds("INBOX", "<old1@") && imap.Holds("INBOX", "<new1@"), "her own move took the wrong mail");
                Need(imap.Logged("UID MOVE") && !imap.Logged("UID COPY") && !imap.Logged("EXPUNGE") && !imap.Logged("CLOSE"), "she didn't use MOVE on Gmail, or expunged");
                imap.Arrive(Letter("Northwind Billing <billing@northwind.com>", "Invoice", "See you soon.", false, new[] { "Reply-To: <northwind.payments@gmail.com>" }), DateTime.UtcNow);
                imap.Arrive(Letter("Accounts Team <accounts@northwind.com>", "Notice", "Your account has been suspended. Confirm your password and pay the overdue invoice to restore access.", false, null), DateTime.UtcNow);
                news = new List<MailNews>();
                Need(Until(delegate { news.AddRange(watch.Take()); return news.Count >= 2; }, 15), "she didn't notice the doubtful mail");
                Need(news.Count == 2 && news.TrueForAll(one => one.Moved == null) && news.Exists(one => one.Odd) && news.Exists(one => one.Phishing && !one.Verdict.Sure) && imap.Holds("INBOX", "Your account has been suspended"), "her own hands moved mail she wasn't sure of: " + news.Count);
                imap.Arrive(Staged("p3", trap), DateTime.UtcNow.AddDays(-3));
                Thread.Sleep(2500);
                Need(!watch.Waiting && imap.Holds("INBOX", "<p3@"), "old mail moved back into the inbox was taken as new");
                Need(!imap.SetSeenByFetch && imap.Seen.Count == 0 && !imap.Log.Exists(one => one.IndexOf("\\Seen", StringComparison.OrdinalIgnoreCase) >= 0), "watching marked mail read");
                Need(!bait.Pending(), "a link in the mail was fetched");
                int logouts = imap.Log.FindAll(one => one == "LOGOUT").Count;
                watch.Stop();
                Need(watch.Join(8000) && watch.State == "stopped" && Until(delegate { return imap.Log.FindAll(one => one == "LOGOUT").Count > logouts; }, 5), "she didn't stop watching cleanly");
            }
            finally
            {
                watch.Stop();
                imap.Listener.Stop();
                bait.Stop();
            }
            checks.Add("PASS mail: watching with IDLE she says new mail once, never old mail or mail moved back in, never marks anything read or opens links, and on her own moves only phishing she's sure of, with the Gmail line");

            FakeImap plain = new FakeImap();
            plain.Caps = "UIDPLUS";
            plain.Boxes["Junk"] = new Dictionary<int, string>();
            plain.Messages[1] = Personal("keep1");
            plain.Messages[2] = Personal("keep2");
            plain.Deleted.Add(2);
            plain.Start();
            MailWatch polling = new MailWatch(Fake(plain.Port)) { PollMilliseconds = 300, FirstWait = 1 };
            polling.OwnHands = delegate { return true; };
            try
            {
                polling.Start();
                Need(Until(delegate { return polling.State == "watching"; }, 15), "she never started polling: " + polling.State);
                plain.Arrive(Staged("p4", "https://arasaka-acc0unt-verify.invalid/login"), DateTime.UtcNow);
                Need(Until(delegate { return polling.Waiting; }, 15), "polling didn't notice new mail");
                List<MailNews> news = polling.Take();
                string line = MailWatch.Line(news);
                Need(news.Count == 1 && news[0].Moved == "Junk" && news[0].Spam && !news[0].Gmail && line.EndsWith(" That's phishing, I moved it to your junk folder.", StringComparison.Ordinal), "dealing with phishing off Gmail: " + line + " | " + news[0].Trouble);
                Need(plain.Logged("NOOP") && !plain.Logged("IDLE") && plain.Logged("UID COPY") && plain.Logged("UID STORE") && plain.Logged("UID EXPUNGE") && !plain.Logged("EXPUNGE") && !plain.Logged("CLOSE"), "without MOVE she didn't copy, flag and expunge just that one");
                Need(plain.Holds("Junk", "<p4@") && !plain.Holds("INBOX", "<p4@") && plain.Holds("INBOX", "<keep1@") && plain.Holds("INBOX", "<keep2@"), "expunging took other mail with it");
            }
            finally
            {
                polling.Stop();
                plain.Listener.Stop();
            }

            FakeImap stuck = new FakeImap();
            stuck.Caps = "IDLE";
            stuck.Boxes["Junk"] = new Dictionary<int, string>();
            stuck.Start();
            MailWatch failing = new MailWatch(Fake(stuck.Port)) { IdleMilliseconds = 1500, FirstWait = 1 };
            failing.OwnHands = delegate { return true; };
            try
            {
                failing.Start();
                Need(Until(delegate { return failing.State == "watching"; }, 15), "she never started watching the old server: " + failing.State);
                stuck.Arrive(Staged("p5", "https://arasaka-acc0unt-verify.invalid/login"), DateTime.UtcNow);
                Need(Until(delegate { return failing.Waiting; }, 15), "she didn't notice mail on the old server");
                List<MailNews> news = failing.Take();
                string line = MailWatch.Line(news);
                Need(news.Count == 1 && news[0].Moved == null && news[0].Trouble != null && line.Contains("I tried to move it to junk, but this mail server can't move one email on its own safely") && line.EndsWith("Don't click anything in it.", StringComparison.Ordinal), "a failed move wasn't said plainly: " + line);
                Need(stuck.Holds("INBOX", "<p5@") && !stuck.Logged("UID COPY") && !stuck.Logged("EXPUNGE"), "a server without MOVE or UIDPLUS lost the mail");
            }
            finally
            {
                failing.Stop();
                stuck.Listener.Stop();
            }
            checks.Add("PASS mail: without IDLE she polls, without MOVE she copies, flags and expunges only that email with UIDPLUS, says her junk folder line, and says plainly when a server can't move it");
        }

        private static void CheckMoves(List<string> checks, string folder)
        {
            FakeImap imap = Gmail();
            imap.Messages[1] = Staged("m1", "https://arasaka-acc0unt-verify.invalid/login");
            imap.Messages[2] = Personal("m2");
            imap.Messages[3] = Newsletter("m3");
            imap.Messages[4] = Personal("m4");
            imap.Messages[5] = Newsletter("m5");
            imap.Messages[6] = Personal("m6");
            imap.Start();
            string dir = Path.Combine(folder, "mail-watch-check");
            Directory.CreateDirectory(dir);
            MailTools.Folder = dir;
            MailTools.Account = Fake(imap.Port);
            try
            {
                bool err;
                string text = Reply(MailTools.Call("check_mail", Args("uid", 1)), out err);
                Need(!err && text.StartsWith("Email 1 in INBOX.\nVerdict: phishing", StringComparison.Ordinal) && text.Contains("A link shows account.arasaka.example but really goes to arasaka-acc0unt-verify.invalid.") && text.EndsWith("it's still unread.", StringComparison.Ordinal), "check_mail: " + text);
                text = Reply(MailTools.Call("check_mail", Args("uid", 2)), out err);
                Need(!err && text.Contains("Verdict: fine"), "check_mail on a personal email: " + text);
                Need(!imap.SetSeenByFetch, "check_mail marked mail read");
                Need(MailTools.MoveAsked(Args("uid", 1, "to", "spam")) == "move the email from Arasaka Account Support ('Action required: your Arasaka account will be suspended') to your spam folder", "what she asks before moving: " + MailTools.MoveAsked(Args("uid", 1, "to", "spam")));
                text = Reply(MailTools.Call("move_mail", Args("uid", 99, "to", "spam")), out err);
                Need(err && text.Contains("There's no message with uid 99 in INBOX."), "moving a missing uid: " + text);
                text = Reply(MailTools.Call("move_mail", Args("uid", 2, "to", "Nowhere")), out err);
                Need(err && text.Contains("There's no mail folder called \"Nowhere\""), "moving to a missing folder: " + text);
                try { MailTools.MoveOne(MailTools.Account, "INBOX", 2, "trash", "<someone-else@example.com>"); Need(false, "she moved an email whose uid now points at a different one"); }
                catch (MailException ex) { Need(ex.Message.Contains("is a different email now"), "a changed uid: " + ex.Message); }
                Need(!imap.Logged("UID MOVE"), "a refused move still moved something");
                text = Reply(MailTools.Call("move_mail", Args("uid", 1, "to", "spam")), out err);
                Need(!err && text == "Moved it to Gmail's Spam folder ([Gmail]/Spam), which reports it to Gmail as spam. Gmail deletes spam for good after 30 days.", "move_mail to spam: " + text);
                text = Reply(MailTools.Call("move_mail", Args("uid", 3, "to", "Receipts")), out err);
                Need(!err && text == "Moved it to Receipts.", "move_mail to a folder: " + text);
                Need(imap.Holds("[Gmail]/Spam", "<m1@") && imap.Holds("Receipts", "<m3@") && imap.Holds("INBOX", "<m2@") && !imap.Logged("EXPUNGE") && !imap.Logged("CLOSE"), "moves landed in the wrong place or expunged");

                text = Reply(MailTools.Call("move_mail", Args("uid", 1, "to", "spam")), out err);
                Need(!err && text == "It's already in Gmail's Spam folder ([Gmail]/Spam). I moved it a moment ago." && imap.Count("UID MOVE 1 ") == 1, "moving the same email twice: " + text);
                text = Reply(MailTools.Call("move_mail", Args("uid", 1, "to", "trash")), out err);
                Need(!err && text == "It's already in Gmail's Spam folder ([Gmail]/Spam). I moved it a moment ago." && imap.Count("UID MOVE 1 ") == 1 && !imap.Holds("[Gmail]/Trash", "<m1@"), "moving it again to somewhere else: " + text);
                text = Reply(MailTools.Call("move_mail", Args("uid", 3, "to", "Receipts")), out err);
                Need(!err && text == "It's already in Receipts. I moved it a moment ago." && imap.Count("UID MOVE 3 ") == 1, "moving it to a folder twice: " + text);
                string[] both = new string[2];
                bool[] failed = new bool[2];
                List<Thread> pair = new List<Thread>();
                for (int i = 0; i < 2; i++)
                {
                    int n = i;
                    pair.Add(new Thread(delegate() { bool e; both[n] = Reply(MailTools.Call("move_mail", Args("uid", 4, "to", "trash")), out e); failed[n] = e; }) { IsBackground = true });
                }
                foreach (Thread t in pair) t.Start();
                foreach (Thread t in pair) Need(t.Join(60000), "two moves of one email at once never finished");
                Need(!failed[0] && !failed[1] && imap.Count("UID MOVE 4 ") == 1 && imap.Holds("[Gmail]/Trash", "<m4@") && !imap.Holds("INBOX", "<m4@"), "two moves of one email at once: " + both[0] + " | " + both[1]);
                Need(Array.Exists(both, one => one.StartsWith("Moved it to Gmail's Trash ([Gmail]/Trash).", StringComparison.Ordinal)) && Array.Exists(both, one => one == "It's already in Gmail's Trash ([Gmail]/Trash). I moved it a moment ago."), "two moves at once didn't say one moved it and one found it moved: " + both[0] + " | " + both[1]);

                text = Reply(MailTools.Call("recent_mail", Args()), out err);
                Need(!err && text.Contains("5 | ") && text.Contains("6 | "), "listing before someone else moves them: " + text);
                imap.Shift(5, "[Gmail]/Spam");
                imap.Shift(6, "[Gmail]/Trash");
                text = Reply(MailTools.Call("move_mail", Args("uid", 6, "to", "trash")), out err);
                Need(!err && text == "It's already in Gmail's Trash ([Gmail]/Trash)." && imap.Count("UID MOVE 6 ") == 0, "an email someone else moved where it was going: " + text);
                text = Reply(MailTools.Call("move_mail", Args("uid", 5, "to", "trash")), out err);
                Need(!err && text == "It's already in Gmail's Spam folder ([Gmail]/Spam)." && imap.Count("UID MOVE 5 ") == 0 && !imap.Holds("[Gmail]/Trash", "<m5@") && imap.Logged("UID SEARCH HEADER Message-ID"), "an email someone else moved to spam: " + text);
                MailTools.Remember("INBOX", 98, "Nobody <nobody@example.com>", "Gone", "<nowhere@example.com>");
                text = Reply(MailTools.Call("move_mail", Args("uid", 98, "to", "trash")), out err);
                Need(err && text == "There's no message with uid 98 in INBOX.", "an email that's nowhere it could have gone: " + text);
                text = Reply(MailTools.Call("move_mail", Args("uid", 99, "to", "trash")), out err);
                Need(err && text == "There's no message with uid 99 in INBOX." && imap.Count("UID MOVE 98 ") == 0 && imap.Count("UID MOVE 99 ") == 0 && !imap.Logged("EXPUNGE") && !imap.Logged("CLOSE"), "a uid that was never there: " + text);
                text = Reply(MailTools.Call("watch_mail", Args("on", true)), out err);
                Need(!err && MailTools.Watch && !MailTools.OwnHands && text.StartsWith("Watching their inbox for new mail now.", StringComparison.Ordinal) && File.Exists(Path.Combine(dir, MailTools.WatchName)), "watch_mail on: " + text);
                text = Reply(MailTools.Call("watch_mail", Args("on", false)), out err);
                Need(!err && !MailTools.Watch && text == "Stopped watching their inbox for new mail.", "watch_mail off: " + text);
                Need(MailTools.WatchAsked(Args("on", true)) == "watch your inbox for new mail" && MailTools.WatchAsked(Args("on", false)) == "stop watching your inbox for new mail", "what she asks before watching");
            }
            finally
            {
                MailTools.Forget();
                imap.Listener.Stop();
            }
            checks.Add("PASS mail: check_mail gives a verdict without marking it read, and move_mail moves just the one email it names to spam or a folder, refusing a missing uid, a missing folder or a uid that changed");
            checks.Add("PASS mail: moving the same email twice, one after the other or at the same moment, is one real move and two successes that say where it is; one someone else moved is found by its Message-ID where it went; a uid that was never there, or an email that's nowhere it could have gone, still fails plainly");
            CheckWatchAndAsk(checks, folder);
        }

        private static void CheckWatchAndAsk(List<string> checks, string folder)
        {
            FakeImap imap = Gmail();
            imap.Messages[1] = Personal("t1");
            imap.Start();
            string dir = Path.Combine(folder, "mail-twice-check");
            Directory.CreateDirectory(dir);
            MailTools.Folder = dir;
            MailTools.Account = Fake(imap.Port);
            MailWatch watch = new MailWatch(Fake(imap.Port)) { IdleMilliseconds = 1500, FirstWait = 1 };
            bool theyAsk = false;
            int landing = 0;
            string theySaid = null;
            watch.OwnHands = delegate
            {
                if (!theyAsk || !Until(delegate { return landing > 0; }, 5)) return true;
                bool e;
                theySaid = Reply(MailTools.Call("move_mail", Args("uid", landing, "to", "trash")), out e);
                return true;
            };
            try
            {
                watch.Start();
                Need(Until(delegate { return watch.State == "watching"; }, 15), "she never started watching: " + watch.State);
                int first = imap.Arrive(Staged("t2", "https://arasaka-acc0unt-verify.invalid/login"), DateTime.UtcNow);
                Need(Until(delegate { return watch.Waiting; }, 15), "she didn't notice the phishing mail");
                List<MailNews> news = watch.Take();
                Need(news.Count == 1 && news[0].Uid == first && news[0].Moved == "[Gmail]/Spam" && !news[0].Already, "she didn't move phishing on her own first: " + news.Count + " | " + (news.Count > 0 ? news[0].Trouble : ""));
                bool err;
                string text = Reply(MailTools.Call("move_mail", Args("uid", first, "to", "trash")), out err);
                Need(!err && text == "It's already in Gmail's Spam folder ([Gmail]/Spam). I moved it a moment ago." && imap.Count("UID MOVE " + first + " ") == 1 && imap.Holds("[Gmail]/Spam", "<t2@") && !imap.Holds("[Gmail]/Trash", "<t2@"), "their ask to bin what she'd already moved: " + text);

                theyAsk = true;
                landing = imap.Arrive(Staged("t3", "https://arasaka-acc0unt-verify.invalid/login"), DateTime.UtcNow);
                Need(Until(delegate { return watch.Waiting; }, 15), "she didn't notice the second phishing mail");
                news = watch.Take();
                string line = MailWatch.Line(news);
                Need(news.Count == 1 && news[0].Already && news[0].Moved == "[Gmail]/Trash" && news[0].Trouble == null && line.EndsWith(" " + MailWatch.AlreadyOut, StringComparison.Ordinal) && MailWatch.Note(news).Contains("It was already out of the inbox, in [Gmail]/Trash, so you didn't move it again."), "they moved it while she was on it: " + line + " | " + (news.Count > 0 ? news[0].Trouble : ""));
                Need(theySaid != null && theySaid.StartsWith("Moved it to Gmail's Trash ([Gmail]/Trash).", StringComparison.Ordinal) && imap.Count("UID MOVE " + landing + " ") == 1 && imap.Holds("[Gmail]/Trash", "<t3@") && !imap.Holds("[Gmail]/Spam", "<t3@") && !imap.Holds("INBOX", "<t3@"), "their move and hers stepped on each other: " + theySaid);
            }
            finally
            {
                watch.Stop();
                MailTools.Forget();
                imap.Listener.Stop();
            }
            checks.Add("PASS mail: when she's already moved phishing on her own, their ask to bin it says where it went, and when they move one while she's on it she doesn't move it again or say it failed; it's one move either way");
        }

        internal static void CheckStarter(List<string> checks)
        {
            FakeImap imap = Gmail();
            imap.Messages[1] = Staged("s1", "https://arasaka-acc0unt-verify.invalid/login");
            imap.Messages[2] = Personal("s2");
            imap.Messages[3] = Newsletter("s3");
            imap.Messages[4] = Personal("s4");
            imap.Start();
            MailAccount was = MailTools.Account;
            bool wake = Security.On(Security.Wake);
            string saved = Security.Saved;
            MailTools.Account = Fake(imap.Port);
            ToolArguments move = new ToolArguments { Name = "email_move_mail", Text = "{\"uid\":1,\"to\":\"spam\"}" }, bin = new ToolArguments { Name = "email_move_mail", Text = "{\"uid\":2,\"to\":\"trash\"}" };
            Func<string, ToolArguments, string> run = delegate(string tool, ToolArguments args) { return Hands.Do(tool, args); };
            try
            {
                Need(Plugins.Needs("email_check_mail") == Security.ModTools && Plugins.Granted("email_move_mail") < 0, "checking mail asked like a change, or moving it always asked");
                ModFloor.Fresh();
                string checkedMail = Plugins.Run("email_check_mail", "{\"uid\":1}");
                Need(checkedMail.Contains("<plugin_result>") && checkedMail.Contains("Verdict: phishing") && Array.IndexOf(ModFloor.HeardFrom, "mail:email") >= 0, "check_mail through her plug-in gate: " + checkedMail);
                if (Security.Has(Security.Sends))
                {
                    Security.Pick(Security.Trusted);
                    Need(Security.IsHold(Security.Gate("mod", move)), "after reading an email, moving mail didn't ask first, so a mail's words could spend their yes");
                    Security.Drop();
                    ModFloor.Fresh();
                    Need(Security.Gate("mod", move) == null, "Trusted with Send, post and delete on asked before moving mail they asked for");
                    Security.Set(Security.Sends, false);
                    string held = Security.Gate("mod", move);
                    Need(Security.IsHold(held) && held.Contains("Move the email from Arasaka Account Support ('Action required: your Arasaka account will be suspended') to your spam folder?"), "moving mail didn't ask first by Send, post and delete: " + held);
                    string done = Security.Resume("yes", run);
                    Need(done != null && done.Contains("Moved it to Gmail's Spam folder") && imap.Holds("[Gmail]/Spam", "<s1@") && !imap.Holds("INBOX", "<s1@"), "their yes didn't move it: " + done);
                }
                Security.Pick(Security.Guarded);
                ModFloor.Fresh();
                Need(Security.IsHold(Security.Gate("mod", bin)), "Guarded didn't ask before moving mail");
                Security.Resume("no", run);
                Need(imap.Holds("INBOX", "<s2@") && !imap.Holds("[Gmail]/Trash", "<s2@"), "their no still moved it");

                ToolArguments three = new ToolArguments { Name = "email_move_mail", Text = "{\"uid\":3,\"to\":\"trash\"}" };
                string once = run("mod", three);
                int selects = imap.Count("SELECT ");
                string twice = run("mod", new ToolArguments { Name = "email_move_mail", Text = "{\"to\":\"trash\",\"uid\":3}" });
                Need(once.Contains("Moved it to Gmail's Trash ([Gmail]/Trash).") && twice.StartsWith(Plugins.Repeated, StringComparison.Ordinal) && twice.EndsWith(once, StringComparison.Ordinal) && imap.Count("SELECT ") == selects && imap.Count("UID MOVE 3 ") == 1, "the same move called twice went to the mail server twice: " + once + " | " + twice);
                ModFloor.Fresh();
                string heldAgain = Security.Gate("mod", three);
                string yesAgain = Security.Resume("yes", run);
                Need(Security.IsHold(heldAgain) && yesAgain != null && !yesAgain.StartsWith(Plugins.Repeated, StringComparison.Ordinal) && yesAgain.Contains("It's already in Gmail's Trash ([Gmail]/Trash). I moved it a moment ago.") && imap.Count("UID MOVE 3 ") == 1, "their yes to the same move again didn't run it, or moved it twice: " + yesAgain);
                ToolArguments four = new ToolArguments { Name = "email_move_mail", Text = "{\"uid\":4,\"to\":\"spam\"}" };
                string[] both = new string[2];
                Thread[] pair = { new Thread(delegate() { both[0] = run("mod", four); }), new Thread(delegate() { both[1] = run("mod", four.Copy()); }) };
                foreach (Thread t in pair) { t.IsBackground = true; t.Start(); }
                foreach (Thread t in pair) Need(t.Join(60000), "the same move twice at once never finished");
                Need(imap.Count("UID MOVE 4 ") == 1 && Array.Exists(both, one => one.StartsWith(Plugins.Repeated, StringComparison.Ordinal)) && Array.Exists(both, one => one.StartsWith("From Move mail", StringComparison.Ordinal) && one.Contains("Moved it to Gmail's Spam folder ([Gmail]/Spam)")), "the same move twice at once: " + both[0] + " | " + both[1]);
                string afterOther = run("mod", three.Copy());
                Need(!afterOther.StartsWith(Plugins.Repeated, StringComparison.Ordinal) && afterOther.Contains("It's already in Gmail's Trash ([Gmail]/Trash). I moved it a moment ago.") && imap.Count("UID MOVE 3 ") == 1, "a move after another change on the same plug-in was taken for a repeat: " + afterOther);
                ToolArguments nowhere = new ToolArguments { Name = "email_move_mail", Text = "{\"uid\":2,\"to\":\"Nowhere\"}" };
                string missed = run("mod", nowhere), missedAgain = run("mod", nowhere.Copy());
                Need(missed.StartsWith("Couldn't finish Move mail.", StringComparison.Ordinal) && missedAgain.StartsWith("Couldn't finish Move mail.", StringComparison.Ordinal), "a move that failed kept the next one from trying: " + missedAgain);
                int fetches = imap.Count("UID FETCH");
                Plugins.Run("email_check_mail", "{\"uid\":2}");
                Plugins.Run("email_check_mail", "{\"uid\":2}");
                Need(imap.Count("UID FETCH") >= fetches + 2, "reading the same email twice only read it once");
            }
            finally
            {
                Security.Drop();
                Security.Load(saved, wake);
                Action securityChanged = Security.Changed;
                if (securityChanged != null) securityChanged();
                ModFloor.Fresh();
                MailTools.Account = was;
                imap.Listener.Stop();
            }
            checks.Add("PASS plug-ins: moving mail asks first by Send, post and delete, always asks after she's read an email, moves on their yes and stays put on their no");
            checks.Add("PASS plug-ins: the same mail move called twice close together, one after the other or at once, reaches the mail server once and the second call gets what the first did; their yes to it again, or the same call after a different change, still runs it, a failed call doesn't stop the next try, and reading an email twice reads it twice");
        }

        // A plain ask about their email goes to her Email plug-in on the first try once it's connected, and until then every brain
        // is told where to connect it. unplugged is her kit from before Email was turned on.
        internal static void CheckPlainAsk(List<string> checks, Hands.Kit unplugged)
        {
            Func<Hands.Kit, bool> saysConnect = delegate(Hands.Kit kit)
            {
                return Hands.NoteFor(kit).Contains(Hands.MailUnplugged) && kit.ChatOnly.Contains(Hands.MailUnplugged) && Hands.PromptFor(kit).Contains("Your Email plug-in isn't connected to their inbox")
                    && !Hands.NoteFor(kit).Contains("Their own email inbox is connected") && !kit.Chat.Contains("Their own email inbox is connected");
            };
            Need(saysConnect(unplugged), "with Email off, her prompts didn't say where to connect it");
            FakeImap imap = Gmail();
            imap.Messages[1] = Personal("p1");
            imap.Start();
            FakeBrain fake = new FakeBrain();
            MailAccount was = MailTools.Account;
            string openAiWas = Brain.OpenAi, notesWas = Brain.MemoryPath, memoriesWas = Brain.MemoriesPath, transcriptsWas = Brain.TranscriptPath, logWas = Brain.LogPath;
            bool keepingWas = Brain.Keeping;
            try
            {
                MailTools.Account = Fake(imap.Port);
                ModFloor.Fresh();
                const string Reading = "email_recent_mail, email_search_mail or email_read_mail";
                Hands.Kit kit = Hands.KitFor();
                string note = Hands.NoteFor(kit), hands = Hands.PromptFor(kit);
                Need(note.Contains("Their own email inbox is connected to you through your Email plug-in") && note.Contains("use mod with " + Reading + " straight away") && !note.Contains(Hands.MailUnplugged), "her notes didn't say their inbox is connected: " + note);
                Need(hands.Contains("call mod with name " + Reading) && hands.IndexOf("call mod with name", StringComparison.Ordinal) < hands.LastIndexOf("Questions about their current", StringComparison.Ordinal), "her hands weren't told to check their inbox with the plug-in");
                Need(kit.ChatOnly.Contains("Their own email inbox is connected through your Email plug-in") && kit.Chat.IndexOf("Their own email inbox", StringComparison.Ordinal) < kit.Chat.LastIndexOf(" If they want anything done", StringComparison.Ordinal), "her chat didn't count their email as a job for her tools");
                Need(kit.WorkTools.Contains(Plugins.CatalogName) && Plugins.Catalog("{\"plugin\":\"email\",\"query\":\"recent_mail\"}").Contains("recent_mail"), "Check mail wasn't discoverable through her plug-in tools");
                Plugins.Grant(Mods.MailStarter, "recent_mail", Plugins.Off);
                string narrowed = Hands.NoteFor(Hands.KitFor());
                Plugins.Grant(Mods.MailStarter, "recent_mail", Plugins.Usual);
                Need(narrowed.Contains("use mod with email_search_mail or email_read_mail straight away"), "a mail tool they turned off was still named");

                Func<string, string> says = delegate(string text) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":" + Voice.Quote(text) + "}}]}"; };
                Func<string, string, string> calls = delegate(string name, string json) { return "{\"choices\":[{\"message\":{\"role\":\"assistant\",\"content\":null,\"tool_calls\":[{\"id\":\"c1\",\"type\":\"function\",\"function\":{\"name\":\"" + name + "\",\"arguments\":" + Voice.Quote(json) + "}}]}}]}"; };
                const string Answer = "Just one, from your mum about dinner on Sunday.";
                fake.Start(calls("mod", "{\"name\":\"email_recent_mail\"}"), says(Answer));
                Brain.OpenAi = fake.Address;
                Brain.MemoryPath = Brain.MemoriesPath = Brain.TranscriptPath = Brain.LogPath = null;
                Brain.Keeping = false;
                List<string> did = new List<string>();
                Brain planner = new Brain { Model = "gpt-5-mini", Remote = new Cloud("sk-proj-test", "gpt-5-mini"), OwnSteps = true };
                planner.Act = delegate(string name, ToolArguments args) { did.Add(name + " " + args.Name); return Hands.Do(name, args); };
                string reply = planner.Ask("Hey Capri, can you check my email?");
                string[] heard;
                lock (fake.Asked) heard = fake.Asked.ToArray();
                Need(reply == Answer && string.Join(",", did.ToArray()) == "mod email_recent_mail" && heard.Length == 2, "a plain ask didn't check their inbox first: " + reply + " / " + string.Join(",", did.ToArray()) + " / " + heard.Length + " asks");
                Need(heard[0].Contains("can you check my email") && heard[0].Contains("Their own email inbox is connected to you through your Email plug-in") && heard[0].Contains("\"name\":\"mod\"") && heard[0].Contains("email_recent_mail"), "her brain wasn't told about their inbox on the first ask");
                Need(heard[1].Contains("Sunday") && heard[1].Contains("Mum") && !imap.Seen.Contains(1), "her brain didn't get their mail back, or it was marked read");

                MailTools.Account = null;
                Need(saysConnect(Hands.KitFor()) && Plugins.Run("email_recent_mail", "").Contains(MailTools.NotSetUp), "before Email's connected, her prompts didn't say where to connect it");
            }
            finally
            {
                Brain.OpenAi = openAiWas;
                Brain.MemoryPath = notesWas;
                Brain.MemoriesPath = memoriesWas;
                Brain.TranscriptPath = transcriptsWas;
                Brain.LogPath = logWas;
                Brain.Keeping = keepingWas;
                MailTools.Account = was;
                ModFloor.Fresh();
                fake.Listener.Stop();
                imap.Listener.Stop();
            }
            checks.Add("PASS plug-ins: with Email connected, a plain \"Hey Capri, can you check my email?\" checks their inbox with her Email plug-in on the first ask, since her notes, hands and chat say their own inbox is connected and name its mail tools; before it's connected, or with it off, they say to connect it in Mainframe > Mods > Plug-ins instead of asking which email app");
        }
    }
}
