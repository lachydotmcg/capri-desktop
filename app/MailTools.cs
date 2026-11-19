using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal static class MailTools
    {
        internal static string Folder;
        private static MailAccount account;
        private static bool loaded;
        internal const string NotSetUp = "Email isn't set up yet. Set it up under Mods > Starter plug-ins > Email.";
        internal const string NeedsPassword = "Email needs its app password again this session. Enter it under Mods > Starter plug-ins > Email.";
        private const int MaxText = 8000;

        internal static MailAccount Account
        {
            get
            {
                if (!loaded && Folder != null) { account = MailAccount.Load(Folder); loaded = true; }
                return account;
            }
            set { account = value; loaded = true; }
        }

        internal static void Forget()
        {
            account = null;
            loaded = true;
            if (Folder != null) MailAccount.Delete(Folder);
        }

        private static Dictionary<string, object> Prop(string type, string description)
        {
            Dictionary<string, object> d = new Dictionary<string, object>();
            d["type"] = type;
            d["description"] = description;
            return d;
        }

        private static Dictionary<string, object> Tool(string name, string title, string description, bool readOnly, Dictionary<string, object> props, params string[] required)
        {
            Dictionary<string, object> schema = new Dictionary<string, object>();
            schema["type"] = "object";
            schema["properties"] = props;
            schema["required"] = new List<object>(required);
            Dictionary<string, object> ann = new Dictionary<string, object>();
            ann["title"] = title;
            ann["readOnlyHint"] = readOnly;
            if (!readOnly) ann["destructiveHint"] = false;
            Dictionary<string, object> t = new Dictionary<string, object>();
            t["name"] = name;
            t["description"] = description;
            t["annotations"] = ann;
            t["inputSchema"] = schema;
            return t;
        }

        internal static List<Dictionary<string, object>> List()
        {
            List<Dictionary<string, object>> tools = new List<Dictionary<string, object>>();
            Dictionary<string, object> p = new Dictionary<string, object>();
            p["folder"] = Prop("string", "Mail folder, INBOX by default.");
            p["count"] = Prop("integer", "How many messages to list, 10 by default, at most 25.");
            p["unread_only"] = Prop("boolean", "Only list unread messages.");
            tools.Add(Tool("recent_mail", "Check mail", "List the newest emails in a folder, newest first, with their uid, date, sender and subject. Does not mark anything as read.", true, p));
            p = new Dictionary<string, object>();
            p["query"] = Prop("string", "Words to look for in the message text or subject.");
            p["from"] = Prop("string", "Only messages from this sender (name or address).");
            p["since"] = Prop("string", "Only messages on or after this date, YYYY-MM-DD.");
            p["folder"] = Prop("string", "Mail folder, INBOX by default.");
            p["count"] = Prop("integer", "How many messages to list, 10 by default, at most 25.");
            tools.Add(Tool("search_mail", "Search mail", "Search emails by text, sender or date. Gives the newest matches first with their uid.", true, p));
            p = new Dictionary<string, object>();
            p["uid"] = Prop("integer", "The uid of the message, from a recent_mail or search_mail list.");
            p["folder"] = Prop("string", "Mail folder, INBOX by default.");
            tools.Add(Tool("read_mail", "Read mail", "Read one email's headers and text. Does not mark it as read.", true, p, "uid"));
            p = new Dictionary<string, object>();
            p["to"] = Prop("string", "Recipient addresses, separated by commas (10 at most). Optional when replying: it defaults to whoever wrote the message.");
            p["subject"] = Prop("string", "Subject line. Optional when replying.");
            p["body"] = Prop("string", "Plain text body of the email.");
            p["reply_to_uid"] = Prop("integer", "Reply to the message with this uid, keeping the thread.");
            p["folder"] = Prop("string", "Folder holding the message being replied to, INBOX by default.");
            tools.Add(Tool("send_mail", "Send mail", "Send an email from the user's account. This really sends it.", false, p, "body"));
            tools.Add(Tool("mail_folders", "Mail folders", "List the mail folders in the account.", true, new Dictionary<string, object>()));
            return tools;
        }

        private static Dictionary<string, object> Result(string text, bool error)
        {
            Dictionary<string, object> item = new Dictionary<string, object>();
            item["type"] = "text";
            item["text"] = text;
            List<object> content = new List<object>();
            content.Add(item);
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["content"] = content;
            if (error) r["isError"] = true;
            return r;
        }

        private static string Str(Dictionary<string, object> args, string key)
        {
            object v;
            if (args == null || !args.TryGetValue(key, out v) || v == null) return "";
            return Convert.ToString(v, CultureInfo.InvariantCulture).Trim();
        }

        private static int Int(Dictionary<string, object> args, string key, int fallback)
        {
            string s = Str(args, key);
            double d;
            if (s.Length == 0 || !double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return fallback;
            return (int)d;
        }

        private static bool Flag(Dictionary<string, object> args, string key)
        {
            string s = Str(args, key).ToLowerInvariant();
            return s == "true" || s == "1" || s == "yes";
        }

        private static string Clip(string s)
        {
            return s.Length <= MaxText ? s : s.Substring(0, MaxText) + "\n[Cut off here. The message is longer.]";
        }

        private static string Line(string s)
        {
            return Regex.Replace(s ?? "", @"\s+", " ").Trim();
        }

        internal static object Call(string name, Dictionary<string, object> args)
        {
            MailAccount a = Account;
            if (a == null) return Result(NotSetUp, true);
            if (a.Password.Length == 0) return Result(NeedsPassword, true);
            string bad = MailAccount.Unsupported(a.Address);
            if (bad != null) return Result(bad, true);
            if (!a.Known) return Result("Email needs its server names. Fill them in under Mods > Starter plug-ins > Email.", true);
            try
            {
                switch (name)
                {
                    case "recent_mail": return Result(Recent(a, args), false);
                    case "search_mail": return Result(Search(a, args), false);
                    case "read_mail": return Result(Read(a, args), false);
                    case "send_mail": return Result(Send(a, args), false);
                    case "mail_folders": return Result(Folders(a), false);
                }
                return Result("Capri's email has no tool called " + name + ".", true);
            }
            catch (MailException ex) { return Result(Hide(a, ex.Message), true); }
            catch (Exception ex) { return Result(Hide(a, "Email didn't work: " + ex.Message), true); }
        }

        private static string Hide(MailAccount a, string text)
        {
            if (a.Password.Length > 0) text = text.Replace(a.Password, "***");
            return text;
        }

        private static string Listing(MailBox box, string folder, List<int> uids, int count)
        {
            if (uids.Count == 0) return "No messages found in " + folder + ".";
            List<int> pick = uids.Count > count ? uids.GetRange(uids.Count - count, count) : uids;
            List<MailHead> heads = box.Headers(pick);
            heads.Sort(delegate(MailHead x, MailHead y) { return y.Uid.CompareTo(x.Uid); });
            StringBuilder sb = new StringBuilder();
            sb.Append(folder + ": " + heads.Count + " of " + uids.Count + " messages, newest first. Format: uid | date | from | subject\n");
            foreach (MailHead h in heads)
            {
                sb.Append(h.Uid + " | " + MailMime.ShortDate(h.Date) + " | " + Line(h.From) + " | " + (h.Subject.Length == 0 ? "(no subject)" : Line(h.Subject)));
                if (!h.Seen) sb.Append(" (unread)");
                sb.Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        private static int Count(Dictionary<string, object> args)
        {
            int n = Int(args, "count", 10);
            return n < 1 ? 10 : Math.Min(n, 25);
        }

        private static string FolderOf(Dictionary<string, object> args)
        {
            string f = Str(args, "folder");
            return f.Length == 0 ? "INBOX" : f;
        }

        private static string Recent(MailAccount a, Dictionary<string, object> args)
        {
            string folder = FolderOf(args);
            using (MailBox box = MailBox.Open(a))
            {
                box.Select(folder, true);
                List<int> uids = Flag(args, "unread_only") ? box.Search("UNSEEN") : box.Search("ALL");
                return Listing(box, folder, uids, Count(args));
            }
        }

        private static string Search(MailAccount a, Dictionary<string, object> args)
        {
            string folder = FolderOf(args);
            string query = Str(args, "query"), from = Str(args, "from"), since = Str(args, "since");
            List<object> crit = new List<object>();
            if (since.Length > 0)
            {
                DateTime d;
                if (!DateTime.TryParseExact(since, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) throw new MailException("The date should look like 2026-03-31.");
                crit.Add("SINCE");
                crit.Add(d.ToString("d-MMM-yyyy", CultureInfo.InvariantCulture));
            }
            if (from.Length > 0) { crit.Add("FROM"); crit.Add(MailBox.Arg(from)); }
            if (query.Length > 0) { crit.Add("TEXT"); crit.Add(MailBox.Arg(query)); }
            if (crit.Count == 0) throw new MailException("Give a search word, a sender or a date to look for.");
            using (MailBox box = MailBox.Open(a))
            {
                box.Select(folder, true);
                return Listing(box, folder, box.Search(crit.ToArray()), Count(args));
            }
        }

        private static string Read(MailAccount a, Dictionary<string, object> args)
        {
            int uid = Int(args, "uid", 0);
            if (uid < 1) throw new MailException("Say which message to read by its uid.");
            string folder = FolderOf(args);
            string raw;
            bool seen;
            using (MailBox box = MailBox.Open(a))
            {
                box.Select(folder, true);
                raw = box.Message(uid, out seen);
            }
            if (raw == null) throw new MailException("There's no message with uid " + uid + " in " + folder + ".");
            MailPart part = MailMime.Parse(raw, 0);
            MailHead h = MailMime.Head(raw.Substring(0, Math.Min(raw.Length, 65536)));
            List<string> attachments = new List<string>();
            string body = MailMime.Body(part, attachments);
            StringBuilder sb = new StringBuilder();
            sb.Append("From: " + Line(h.From) + "\n");
            sb.Append("To: " + Line(h.To) + "\n");
            sb.Append("Date: " + MailMime.ShortDate(h.Date) + "\n");
            sb.Append("Subject: " + Line(h.Subject) + "\n");
            if (attachments.Count > 0) sb.Append("Attachments (not opened): " + string.Join(", ", attachments.ToArray()) + "\n");
            sb.Append("\n").Append(body.Length == 0 ? "(no text in this message)" : body);
            return Clip(sb.ToString());
        }

        private static string Send(MailAccount a, Dictionary<string, object> args)
        {
            string to = Str(args, "to"), subject = Str(args, "subject"), body = Str(args, "body");
            int replyUid = Int(args, "reply_to_uid", 0);
            if (body.Length == 0) throw new MailException("The email needs a body.");
            string inReplyTo = null, references = null;
            if (replyUid > 0)
            {
                MailHead orig = null;
                using (MailBox box = MailBox.Open(a))
                {
                    box.Select(FolderOf(args), true);
                    List<int> one = new List<int>();
                    one.Add(replyUid);
                    List<MailHead> heads = box.Headers(one);
                    if (heads.Count > 0) orig = heads[0];
                }
                if (orig == null) throw new MailException("There's no message with uid " + replyUid + " to reply to.");
                inReplyTo = orig.MessageId;
                references = (orig.References + " " + orig.MessageId).Trim();
                if (subject.Length == 0) subject = Regex.IsMatch(orig.Subject, @"^\s*re:", RegexOptions.IgnoreCase) ? orig.Subject : "Re: " + orig.Subject;
                if (to.Length == 0) to = orig.ReplyTo.Length > 0 ? orig.ReplyTo : orig.From;
            }
            if (subject.Length == 0) throw new MailException("The email needs a subject.");
            List<string> recipients = MailSmtp.Recipients(to);
            if (!MailSmtp.ValidAddress(a.Address)) throw new MailException("The account's own address doesn't look right.");
            string message = MailSmtp.Build(a, recipients, subject, body, inReplyTo, references);
            using (MailSmtp smtp = MailSmtp.Open(a)) smtp.Send(a.Address, recipients, message);
            return "Sent to " + string.Join(", ", recipients.ToArray()) + ".";
        }

        private static string Folders(MailAccount a)
        {
            using (MailBox box = MailBox.Open(a))
            {
                List<string> names = box.Folders();
                return names.Count == 0 ? "No folders found." : "Mail folders:\n" + string.Join("\n", names.ToArray());
            }
        }

        internal static string Test(MailAccount a)
        {
            try
            {
                if (a == null || a.Address.Length == 0) return "Type the email address first.";
                string bad = MailAccount.Unsupported(a.Address);
                if (bad != null) return bad;
                if (!MailSmtp.ValidAddress(a.Address)) return "That doesn't look like an email address.";
                if (!a.Known) return "Fill in the incoming and outgoing server names for this address.";
                if (a.Password.Length == 0) return "Type the app password first.";
                using (MailBox box = MailBox.Open(a)) { }
                using (MailSmtp smtp = MailSmtp.Open(a)) { }
                return null;
            }
            catch (MailException ex) { return Hide(a, ex.Message); }
            catch (Exception ex) { return Hide(a, "Email didn't work: " + ex.Message); }
        }
    }
}
