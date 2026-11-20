using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class MailSmtp : IDisposable
    {
        private MailWire wire;
        private readonly MailAccount account;
        private string extensions = "";

        private MailSmtp(MailAccount a) { account = a; }

        private string Reply(out int code)
        {
            StringBuilder all = new StringBuilder();
            code = 0;
            while (true)
            {
                string line = wire.ReadLine();
                if (line == null) throw new MailException("The mail server closed the connection.");
                if (line.Length < 3 || !int.TryParse(line.Substring(0, 3), out code)) throw new MailException("The mail server sent something unexpected.");
                all.Append(line.Length > 4 ? line.Substring(4) : "").Append('\n');
                if (line.Length == 3 || line[3] == ' ') return all.ToString();
            }
        }

        private string Say(string line, out int code)
        {
            wire.Write(line + "\r\n");
            return Reply(out code);
        }

        private void Expect(string line, int want, string what)
        {
            int code;
            string text = Say(line, out code);
            if (code != want) throw new MailException(Fail(what, code, text));
        }

        private string Fail(string what, int code, string text)
        {
            string t = text ?? "";
            if (account.Password.Length > 0) t = t.Replace(account.Password, "***");
            t = Regex.Replace(t, @"\s+", " ").Trim();
            if (t.Length > 160) t = t.Substring(0, 160);
            return "The mail server wouldn't " + what + " (" + code + (t.Length > 0 ? ": " + t : "") + ").";
        }

        internal static MailSmtp Open(MailAccount a)
        {
            MailSmtp s = new MailSmtp(a);
            try
            {
                s.wire = MailWire.Connect(a.SmtpHost, a.SmtpPort, a.SmtpImplicitTls, a.Plain);
                int code;
                string hello = s.Reply(out code);
                if (code != 220) throw new MailException(s.Fail("accept the connection", code, hello));
                s.Ehlo();
                if (!a.SmtpImplicitTls && !a.Plain)
                {
                    s.Expect("STARTTLS", 220, "start a secure connection");
                    s.wire = s.wire.Upgrade();
                    s.Ehlo();
                }
                s.Login();
                return s;
            }
            catch (Exception)
            {
                s.Dispose();
                throw;
            }
        }

        private void Ehlo()
        {
            int code;
            string text = Say("EHLO capri.local", out code);
            if (code != 250) throw new MailException(Fail("greet back", code, text));
            extensions = text.ToUpperInvariant();
        }

        private string AuthRefused()
        {
            return account.Provider + " refused the sign-in. Check the " + (account.Provider == "The mail server" ? "password." : "app password.");
        }

        private void Login()
        {
            if (extensions.IndexOf("AUTH", StringComparison.Ordinal) < 0) throw new MailException("The mail server doesn't offer sign-in on this connection.");
            string user = account.LoginName;
            int code;
            string text = null;
            bool tryPlain = extensions.IndexOf("PLAIN", StringComparison.Ordinal) >= 0;
            if (tryPlain)
            {
                string token = Convert.ToBase64String(Encoding.UTF8.GetBytes("\0" + user + "\0" + account.Password));
                text = Say("AUTH PLAIN " + token, out code);
                if (code == 235) return;
                if (code == 535 || code == 534) throw new MailException(AuthRefused());
            }
            text = Say("AUTH LOGIN", out code);
            if (code != 334) throw new MailException(Fail("sign in", code, text));
            text = Say(Convert.ToBase64String(Encoding.UTF8.GetBytes(user)), out code);
            if (code != 334) throw new MailException(Fail("sign in", code, text));
            text = Say(Convert.ToBase64String(Encoding.UTF8.GetBytes(account.Password)), out code);
            if (code == 235) return;
            if (code == 535 || code == 534) throw new MailException(AuthRefused());
            throw new MailException(Fail("sign in", code, text));
        }

        internal void Send(string from, List<string> to, string message)
        {
            Expect("MAIL FROM:<" + from + ">", 250, "accept the sender");
            foreach (string r in to) Expect("RCPT TO:<" + r + ">", 250, "deliver to " + r);
            Expect("DATA", 354, "take the message");
            string stuffed = Regex.Replace(message, @"(?m)^\.", "..");
            if (!stuffed.EndsWith("\r\n")) stuffed += "\r\n";
            wire.Write(stuffed + ".\r\n");
            int code;
            string text = Reply(out code);
            if (code != 250) throw new MailException(Fail("send the message", code, text));
        }

        public void Dispose()
        {
            if (wire == null) return;
            try { wire.Write("QUIT\r\n"); }
            catch (Exception) { }
            wire.Dispose();
            wire = null;
        }

        private static readonly Regex AddressRule = new Regex(@"^[^\s@<>,;""()\[\]\\]+@[A-Za-z0-9\-]+(\.[A-Za-z0-9\-]+)+$");

        internal static bool ValidAddress(string a)
        {
            return a != null && a.Length <= 254 && AddressRule.IsMatch(a);
        }

        internal static List<string> Recipients(string text)
        {
            List<string> list = new List<string>();
            foreach (string piece in (text ?? "").Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string p = piece.Trim();
                Match m = Regex.Match(p, @"<([^<>]+)>\s*$");
                if (m.Success) p = m.Groups[1].Value.Trim();
                if (p.Length == 0) continue;
                if (!ValidAddress(p)) throw new MailException("\"" + (p.Length > 60 ? p.Substring(0, 60) : p) + "\" doesn't look like an email address.");
                if (!list.Contains(p)) list.Add(p);
            }
            if (list.Count == 0) throw new MailException("Say who the email is going to.");
            if (list.Count > 10) throw new MailException("That's more than 10 recipients. Capri sends to 10 at most.");
            return list;
        }

        private static string OneLine(string s)
        {
            return Regex.Replace(s ?? "", @"[\r\n]+", " ").Trim();
        }

        private static bool Ascii(string s)
        {
            foreach (char c in s) if (c > 126 || (c < 32 && c != '\t')) return false;
            return true;
        }

        internal static string EncodeWords(string text)
        {
            text = OneLine(text);
            if (Ascii(text)) return text;
            List<string> words = new List<string>();
            StringBuilder chunk = new StringBuilder();
            int bytes = 0;
            for (int i = 0; i < text.Length; i++)
            {
                string ch = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? text.Substring(i++, 2) : text[i].ToString();
                int n = Encoding.UTF8.GetByteCount(ch);
                if (bytes + n > 45)
                {
                    words.Add(Word(chunk.ToString()));
                    chunk.Length = 0;
                    bytes = 0;
                }
                chunk.Append(ch);
                bytes += n;
            }
            if (chunk.Length > 0) words.Add(Word(chunk.ToString()));
            return string.Join("\r\n ", words.ToArray());
        }

        private static string Word(string s)
        {
            return "=?utf-8?B?" + Convert.ToBase64String(Encoding.UTF8.GetBytes(s)) + "?=";
        }

        private static string Display(string name, string address)
        {
            name = OneLine(name);
            if (name.Length == 0) return address;
            if (!Ascii(name)) return EncodeWords(name) + " <" + address + ">";
            return "\"" + name.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\" <" + address + ">";
        }

        internal static string QuotedPrintable(string text)
        {
            string body = text.Replace("\r\n", "\n").Replace('\r', '\n');
            StringBuilder sb = new StringBuilder();
            foreach (string line in body.Split('\n'))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(line);
                StringBuilder cur = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    byte b = bytes[i];
                    bool last = i == bytes.Length - 1;
                    string piece;
                    if (b == '=' || b > 126 || b < 32 && b != '\t' || (b == ' ' || b == '\t') && last) piece = "=" + b.ToString("X2");
                    else piece = ((char)b).ToString();
                    if (cur.Length + piece.Length > 75)
                    {
                        sb.Append(cur).Append("=\r\n");
                        cur.Length = 0;
                    }
                    cur.Append(piece);
                }
                sb.Append(cur).Append("\r\n");
            }
            return sb.ToString();
        }

        internal static string Build(MailAccount a, List<string> to, string subject, string body, string inReplyTo, string references)
        {
            StringBuilder m = new StringBuilder();
            string domain = MailAccount.Domain(a.Address);
            if (domain.Length == 0) domain = "capri.local";
            DateTimeOffset now = DateTimeOffset.Now;
            string zone = now.ToString("zzz", CultureInfo.InvariantCulture).Replace(":", "");
            m.Append("Date: ").Append(now.ToString("ddd, dd MMM yyyy HH:mm:ss", CultureInfo.InvariantCulture)).Append(' ').Append(zone).Append("\r\n");
            m.Append("Message-ID: <").Append(Guid.NewGuid().ToString("N")).Append('@').Append(domain).Append(">\r\n");
            m.Append("From: ").Append(Display(a.Name, a.Address)).Append("\r\n");
            m.Append("To: ").Append(string.Join(", ", to.ToArray())).Append("\r\n");
            m.Append("Subject: ").Append(EncodeWords(subject)).Append("\r\n");
            if (!string.IsNullOrEmpty(inReplyTo)) m.Append("In-Reply-To: ").Append(OneLine(inReplyTo)).Append("\r\n");
            if (!string.IsNullOrEmpty(references)) m.Append("References: ").Append(OneLine(references)).Append("\r\n");
            m.Append("MIME-Version: 1.0\r\n");
            m.Append("Content-Type: text/plain; charset=utf-8\r\n");
            m.Append("Content-Transfer-Encoding: quoted-printable\r\n\r\n");
            m.Append(QuotedPrintable(body ?? ""));
            return m.ToString();
        }
    }
}
