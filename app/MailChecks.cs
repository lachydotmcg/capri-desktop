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
            internal bool SetSeenByFetch;
            internal string Password = "good";
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
                byte[] b = Encoding.GetEncoding(28591).GetBytes(text);
                s.Write(b, 0, b.Length);
                s.Flush();
            }

            private void Serve(TcpClient c)
            {
                Stream s = c.GetStream();
                Put(s, "* OK fake ready\r\n");
                bool login = false;
                List<int> order = new List<int>(Messages.Keys);
                order.Sort();
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
                    if (upper.StartsWith("LOGIN "))
                    {
                        List<string> args = Quoted(rest.Substring(6));
                        if (args.Count == 2 && args[0] == "me@example.com" && args[1] == Password) { login = true; Put(s, tag + " OK LOGIN completed\r\n"); }
                        else Put(s, tag + " NO [AUTHENTICATIONFAILED] Invalid credentials\r\n");
                    }
                    else if (!login) Put(s, tag + " BAD log in first\r\n");
                    else if (upper.StartsWith("LOGOUT")) { Put(s, "* BYE\r\n" + tag + " OK done\r\n"); return; }
                    else if (upper.StartsWith("LIST "))
                        Put(s, "* LIST (\\HasNoChildren) \"/\" \"INBOX\"\r\n* LIST (\\Noselect) \"/\" \"[Gmail]\"\r\n* LIST (\\HasNoChildren) \"/\" \"Sent\"\r\n* LIST (\\HasNoChildren) \"/\" \"Caf&AOk-\"\r\n" + tag + " OK done\r\n");
                    else if (upper.StartsWith("EXAMINE ") || upper.StartsWith("SELECT "))
                    {
                        if (rest.Contains("\"INBOX\"")) Put(s, "* 3 EXISTS\r\n" + tag + " OK [READ-ONLY] selected\r\n");
                        else Put(s, tag + " NO no such mailbox\r\n");
                    }
                    else if (upper.StartsWith("UID SEARCH"))
                    {
                        List<string> args = Quoted(rest.Substring(10));
                        StringBuilder found = new StringBuilder("* SEARCH");
                        foreach (int uid in order)
                        {
                            string raw = Messages[uid];
                            bool ok = true;
                            for (int i = 0; i < args.Count; i++)
                            {
                                string a = args[i].ToUpperInvariant();
                                if (a == "UNSEEN" && Seen.Contains(uid)) ok = false;
                                if ((a == "TEXT" || a == "FROM") && i + 1 < args.Count)
                                {
                                    string want = args[i + 1].ToLowerInvariant();
                                    string hay = a == "FROM" ? Regex.Match(raw, @"(?m)^From:.*$").Value : raw;
                                    if (hay.ToLowerInvariant().IndexOf(want, StringComparison.Ordinal) < 0) ok = false;
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
                            if (!Messages.TryGetValue(uid, out raw)) continue;
                            string flags = Seen.Contains(uid) ? "(\\Seen)" : "()";
                            string key, payload;
                            if (upper.Contains("HEADER.FIELDS"))
                            {
                                key = "BODY[HEADER.FIELDS (FROM TO SUBJECT DATE MESSAGE-ID REFERENCES REPLY-TO)]";
                                payload = raw.Substring(0, raw.IndexOf("\r\n\r\n", StringComparison.Ordinal) + 4);
                            }
                            else { key = upper.Contains("<0.") ? "BODY[]<0>" : "BODY[]"; payload = raw; }
                            Put(s, "* " + (order.IndexOf(uid) + 1) + " FETCH (UID " + uid + " FLAGS " + flags + " " + key + " {" + Encoding.GetEncoding(28591).GetByteCount(payload) + "}\r\n" + payload + ")\r\n");
                        }
                        Put(s, tag + " OK done\r\n");
                    }
                    else Put(s, tag + " BAD unknown\r\n");
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
                Need(MailTools.NotSetUp == "Email isn't set up yet. Turn on Email under Mods to set it up.", "setup message");
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
            }
            finally
            {
                MailTools.Folder = saved;
                MailTools.Account = savedAccount;
                imap.Listener.Stop();
                smtp.Listener.Stop();
            }
        }
    }
}
