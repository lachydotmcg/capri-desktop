using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class MailWatchFile
    {
        [DataMember(Name = "watch")] public bool Watch;
        [DataMember(Name = "own_hands")] public bool OwnHands;
    }

    internal sealed class MailMove
    {
        internal string Target = "", Kind = "folder";
        internal bool Gmail, Already, Ours;
    }

    internal static class MailTools
    {
        private sealed class Moved
        {
            internal string Account = "", Folder = "", MessageId = "", Target = "", Kind = "";
            internal int Uid;
            internal DateTime At = DateTime.UtcNow;
        }

        internal static string Folder;
        private static MailAccount account;
        private static bool loaded;
        private static readonly TimeSpan MovedLately = TimeSpan.FromMinutes(5);
        private static readonly object moving = new object();
        private static readonly List<Moved> moves = new List<Moved>();
        internal const string NotSetUp = "Email isn't connected yet. They can connect it with Email in Mainframe's Mods page, under Plug-ins.";
        internal const string NeedsPassword = "Email needs its app password again. Turn Email off and on under Mods to give it.";
        internal const string WatchName = "mail-watch.json";
        private const int MaxText = 8000;
        private static readonly object watchGate = new object();
        private static bool watchLoaded, watchOn, handsOn;
        private static string watchFolder;
        private static readonly Dictionary<string, MailHead> remembered = new Dictionary<string, MailHead>();
        private static readonly List<string> rememberedOrder = new List<string>();

        internal static MailAccount Account
        {
            get
            {
                if (!loaded && Folder != null) { account = MailAccount.Load(Folder); loaded = true; }
                return account;
            }
            set { if (value != account) Unremember(); account = value; loaded = true; }
        }

        internal static void Reload() { account = null; loaded = false; Unremember(); }

        internal static bool Ready { get { MailAccount a = Account; return a != null && a.Password.Length > 0; } }

        internal static void Forget()
        {
            account = null;
            loaded = true;
            Unremember();
            if (Folder != null) MailAccount.Delete(Folder);
            lock (watchGate)
            {
                watchLoaded = true;
                watchFolder = Folder;
                watchOn = handsOn = false;
                try { if (Folder != null && File.Exists(Path.Combine(Folder, WatchName))) File.Delete(Path.Combine(Folder, WatchName)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void LoadWatch()
        {
            if (watchLoaded && watchFolder == Folder) return;
            watchLoaded = true;
            watchFolder = Folder;
            watchOn = handsOn = false;
            if (Folder == null) return;
            try
            {
                string path = Path.Combine(Folder, WatchName);
                if (!File.Exists(path)) return;
                MailWatchFile f;
                using (FileStream s = File.OpenRead(path)) f = (MailWatchFile)new DataContractJsonSerializer(typeof(MailWatchFile)).ReadObject(s);
                if (f == null) return;
                watchOn = f.Watch;
                handsOn = f.OwnHands;
            }
            catch (Exception) { watchOn = handsOn = false; }
        }

        internal static bool Watch { get { lock (watchGate) { LoadWatch(); return watchOn; } } }

        internal static bool HandsSet { get { lock (watchGate) { LoadWatch(); return handsOn; } } }

        internal static bool OwnHands { get { lock (watchGate) { LoadWatch(); return watchOn && handsOn; } } }

        internal static void SetWatch(bool watch, bool hands)
        {
            lock (watchGate)
            {
                LoadWatch();
                watchOn = watch;
                handsOn = hands;
                if (Folder == null) return;
                Directory.CreateDirectory(Folder);
                string path = Path.Combine(Folder, WatchName), temp = path + ".tmp";
                MailWatchFile f = new MailWatchFile { Watch = watch, OwnHands = hands };
                using (FileStream s = File.Create(temp)) new DataContractJsonSerializer(typeof(MailWatchFile)).WriteObject(s, f);
                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);
            }
        }

        internal static void Remember(string folder, int uid, string from, string subject, string messageId)
        {
            string key = (string.IsNullOrEmpty(folder) ? "INBOX" : folder).ToUpperInvariant() + " " + uid;
            lock (remembered)
            {
                if (remembered.ContainsKey(key)) return;
                remembered[key] = new MailHead { Uid = uid, From = from ?? "", Subject = subject ?? "", MessageId = messageId ?? "" };
                rememberedOrder.Add(key);
                if (rememberedOrder.Count > 500) { remembered.Remove(rememberedOrder[0]); rememberedOrder.RemoveAt(0); }
            }
        }

        internal static MailHead Known(string folder, int uid)
        {
            MailHead h;
            lock (remembered) return remembered.TryGetValue((string.IsNullOrEmpty(folder) ? "INBOX" : folder).ToUpperInvariant() + " " + uid, out h) ? h : null;
        }

        private static void Unremember()
        {
            lock (remembered) { remembered.Clear(); rememberedOrder.Clear(); }
            lock (moves) moves.Clear();
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
            tools.Add(Tool("recent_mail", "Check mail", "Check their own inbox: their newest emails, newest first, with uid, date, sender and subject. Use it when they ask, suggest or hint at checking their email, like check my email or any new emails? It doesn't mark anything read.", true, p));
            p = new Dictionary<string, object>();
            p["query"] = Prop("string", "Words to look for in the message text or subject.");
            p["from"] = Prop("string", "Only messages from this sender (name or address).");
            p["since"] = Prop("string", "Only messages on or after this date, YYYY-MM-DD.");
            p["folder"] = Prop("string", "Mail folder, INBOX by default.");
            p["count"] = Prop("integer", "How many messages to list, 10 by default, at most 25.");
            tools.Add(Tool("search_mail", "Search mail", "Search their own email by words, sender or date, like did anyone email me about pizza or find the email from Sam. Gives the newest matches first with their uid.", true, p));
            p = new Dictionary<string, object>();
            p["uid"] = Prop("integer", "The uid of the message, from a recent_mail or search_mail list.");
            p["folder"] = Prop("string", "Mail folder, INBOX by default.");
            tools.Add(Tool("read_mail", "Read mail", "Read one of their emails in full, its headers and text, by its uid from a mail list or search, like what does Sam's email say. It doesn't mark it read.", true, p, "uid"));
            p = new Dictionary<string, object>();
            p["to"] = Prop("string", "Recipient addresses, separated by commas (10 at most). Needed when replying too, so name whoever you're answering.");
            p["subject"] = Prop("string", "Subject line. Optional when replying.");
            p["body"] = Prop("string", "Plain text body of the email.");
            p["reply_to_uid"] = Prop("integer", "Reply to the message with this uid, keeping the thread.");
            p["folder"] = Prop("string", "Folder holding the message being replied to, INBOX by default.");
            tools.Add(Tool("send_mail", "Send mail", "Send an email from the user's account. This really sends it.", false, p, "to", "body"));
            tools.Add(Tool("mail_folders", "Mail folders", "List the mail folders in the account.", true, new Dictionary<string, object>()));
            p = new Dictionary<string, object>();
            p["uid"] = Prop("integer", "The uid of the message, from a mail list or the new mail you told them about.");
            p["folder"] = Prop("string", "Mail folder, INBOX by default.");
            tools.Add(Tool("check_mail", "Check for phishing", "Check one email for phishing or a scam when they ask, suggest or hint at it, like is this legit or is it safe: who it says it's from, in its name, subject or words, against where it really came from, look-alike addresses, links that hide where they go, asks for your password, card details or money, risky attachments and failed sender checks. Gives fine, suspicious or phishing with plain reasons. It never opens links or attachments and doesn't mark it read.", true, p, "uid"));
            p = new Dictionary<string, object>();
            p["uid"] = Prop("integer", "The uid of the message to move.");
            p["to"] = Prop("string", "Where it goes: spam (their spam or junk folder), trash, archive, or a folder's name.");
            p["folder"] = Prop("string", "Folder it's in now, INBOX by default.");
            tools.Add(Tool("move_mail", "Move mail", "Move one email to their spam or junk folder, the trash, the archive or a folder they name, when they ask, suggest or hint at it, like bin it or that's spam. On Gmail, moving it to spam reports it as spam. It moves only that email and deletes nothing for good.", false, p, "uid", "to"));
            p = new Dictionary<string, object>();
            p["on"] = Prop("boolean", "true to start watching, false to stop.");
            tools.Add(Tool("watch_mail", "Watch for new mail", "Start or stop watching their inbox for new mail when they ask, suggest or hint at it. While it's on you notice new mail as it lands, say so, and warn them about phishing. Dealing with phishing on your own is a switch only they can turn on, in Email's settings.", false, p, "on"));
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
            if (!a.Known) return Result("Email needs its server names. Turn Email off and on under Mods to fill them in.", true);
            try
            {
                switch (name)
                {
                    case "recent_mail": return Result(Recent(a, args), false);
                    case "search_mail": return Result(Search(a, args), false);
                    case "read_mail": return Result(Read(a, args), false);
                    case "send_mail": return Result(Send(a, args), false);
                    case "mail_folders": return Result(Folders(a), false);
                    case "check_mail": return Result(CheckOne(a, args), false);
                    case "move_mail": return Result(Move(a, args), false);
                    case "watch_mail": return Result(WatchMail(args), false);
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
                Remember(folder, h.Uid, h.From, h.Subject, h.MessageId);
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
            Remember(folder, uid, h.From, h.Subject, h.MessageId);
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
            if (to.Length == 0) throw new MailException("Say who the email is going to, even when replying.");
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

        private static string CheckOne(MailAccount a, Dictionary<string, object> args)
        {
            int uid = Int(args, "uid", 0);
            if (uid < 1) throw new MailException("Say which message to check by its uid.");
            string folder = FolderOf(args);
            MailPeek peek;
            using (MailBox box = MailBox.Open(a))
            {
                box.Select(folder, true);
                peek = box.Peek(uid, MailBox.MaxMessage);
            }
            if (peek == null) throw new MailException("There's no message with uid " + uid + " in " + folder + ".");
            MailVerdict v = MailPhish.Check(peek.Raw, peek.Names);
            Remember(folder, uid, v.From, v.Subject, v.MessageId);
            return "Email " + uid + " in " + folder + ".\n" + MailPhish.Said(v, peek.Seen);
        }

        private static string Move(MailAccount a, Dictionary<string, object> args)
        {
            int uid = Int(args, "uid", 0);
            if (uid < 1) throw new MailException("Say which message to move by its uid.");
            string to = Str(args, "to");
            if (to.Length == 0) throw new MailException("Say where it goes: spam, trash, archive or a folder's name.");
            string folder = FolderOf(args);
            MailHead known = Known(folder, uid);
            MailMove done = MoveOne(a, folder, uid, to, known == null ? "" : known.MessageId);
            string kind = done.Kind, target = done.Target;
            bool gmail = done.Gmail;
            if (done.Already) return "It's already in " + Where(done) + "." + (done.Ours ? " I moved it a moment ago." : "");
            if (kind == "spam") return gmail ? "Moved it to Gmail's Spam folder (" + target + "), which reports it to Gmail as spam. Gmail deletes spam for good after 30 days." : "Moved it to their junk folder (" + target + "). It's still there if they want it back.";
            if (kind == "trash") return gmail ? "Moved it to Gmail's Trash (" + target + "). Gmail deletes it for good after 30 days." : "Moved it to their trash (" + target + "). It's still there if they want it back.";
            if (kind == "archive") return gmail && target.IndexOf("All Mail", StringComparison.OrdinalIgnoreCase) >= 0 ? "Archived it: it left the inbox and stays in All Mail." : "Moved it to the archive (" + target + ").";
            return "Moved it to " + target + ".";
        }

        private static string Where(MailMove done)
        {
            if (done.Kind == "spam") return done.Gmail ? "Gmail's Spam folder (" + done.Target + ")" : "their junk folder (" + done.Target + ")";
            if (done.Kind == "trash") return done.Gmail ? "Gmail's Trash (" + done.Target + ")" : "their trash (" + done.Target + ")";
            if (done.Kind == "archive") return AllMail(done.Target) ? "All Mail (" + done.Target + "), so it's archived" : "the archive (" + done.Target + ")";
            return done.Target;
        }

        private static bool AllMail(string folder) { return folder.IndexOf("All Mail", StringComparison.OrdinalIgnoreCase) >= 0; }

        // Moving one email twice, from two of her tool calls or from her watching and their asking, is one move and two
        // successes: the second finds it gone and says where it went instead of saying the move failed.
        internal static MailMove MoveOne(MailAccount a, string from, int uid, string to, string messageId)
        {
            MailMove done = new MailMove();
            using (MailBox box = MailBox.Open(a))
            {
                done.Gmail = box.Gmail;
                done.Target = Destination(box, to, out done.Kind);
                if (string.Equals(done.Target, from, StringComparison.OrdinalIgnoreCase)) throw new MailException("It's already in " + done.Target + ".");
                lock (moving)
                {
                    box.Select(from, false);
                    if (!box.Has(uid))
                    {
                        if (!Gone(box, a, from, uid, messageId, done)) throw new MailException("There's no message with uid " + uid + " in " + from + ".");
                        return done;
                    }
                    List<int> one = new List<int>();
                    one.Add(uid);
                    List<MailHead> heads = box.Headers(one);
                    MailHead head = heads.Count > 0 ? heads[0] : null;
                    if (head != null && !string.IsNullOrEmpty(messageId) && head.MessageId.Length > 0 && head.MessageId != messageId) throw new MailException("Message " + uid + " in " + from + " is a different email now, so it was left alone. List the mail again first.");
                    box.Move(uid, done.Target);
                    if (head != null) Remember(from, uid, head.From, head.Subject, head.MessageId);
                    Keep(a, from, uid, head != null && head.MessageId.Length > 0 ? head.MessageId : messageId, done);
                    return done;
                }
            }
        }

        private static string Owner(MailAccount a)
        {
            return (a.LoginName + " " + a.ImapHost + ":" + a.ImapPort.ToString(CultureInfo.InvariantCulture)).ToLowerInvariant();
        }

        private static void Keep(MailAccount a, string from, int uid, string messageId, MailMove done)
        {
            lock (moves)
            {
                moves.Add(new Moved { Account = Owner(a), Folder = from, Uid = uid, MessageId = messageId ?? "", Target = done.Target, Kind = done.Kind });
                if (moves.Count > 100) moves.RemoveAt(0);
            }
        }

        private static Moved Lately(MailAccount a, string from, int uid, string messageId)
        {
            string owner = Owner(a);
            DateTime now = DateTime.UtcNow;
            lock (moves)
            {
                moves.RemoveAll(one => now - one.At > MovedLately);
                for (int i = moves.Count - 1; i >= 0; i--)
                {
                    Moved one = moves[i];
                    if (one.Account != owner || !string.Equals(one.Folder, from, StringComparison.OrdinalIgnoreCase)) continue;
                    if ((!string.IsNullOrEmpty(messageId) && one.MessageId.Length > 0) ? one.MessageId == messageId : one.Uid == uid) return one;
                }
            }
            return null;
        }

        private static bool Gone(MailBox box, MailAccount a, string from, int uid, string messageId, MailMove done)
        {
            Moved mine = Lately(a, from, uid, messageId);
            if (mine != null)
            {
                done.Target = mine.Target;
                done.Kind = mine.Kind;
                done.Already = done.Ours = true;
                return true;
            }
            if (string.IsNullOrEmpty(messageId)) return false;
            foreach (KeyValuePair<string, string> place in Places(box, from, done))
            {
                try
                {
                    box.Select(place.Key, true);
                    if (box.WithId(messageId).Count == 0) continue;
                }
                catch (MailException) { continue; }
                done.Target = place.Key;
                done.Kind = place.Value;
                done.Already = true;
                return true;
            }
            return false;
        }

        // Where it could have gone: where it was going, then the trash, spam and archive. Gmail's All Mail holds the inbox too,
        // so it only counts when it was where this move was going.
        private static List<KeyValuePair<string, string>> Places(MailBox box, string from, MailMove done)
        {
            List<KeyValuePair<string, string>> places = new List<KeyValuePair<string, string>>();
            places.Add(new KeyValuePair<string, string>(done.Target, done.Kind));
            foreach (string word in new[] { "trash", "spam", "archive" })
            {
                string found, kind;
                try { found = Destination(box, word, out kind); }
                catch (MailException) { continue; }
                if ((done.Gmail && AllMail(found)) || string.Equals(found, from, StringComparison.OrdinalIgnoreCase) || places.Exists(one => string.Equals(one.Key, found, StringComparison.OrdinalIgnoreCase))) continue;
                places.Add(new KeyValuePair<string, string>(found, kind));
            }
            return places;
        }

        private static readonly string[] SpamWords = { "spam", "junk", "junk mail", "junk email", "junk e-mail", "bulk", "bulk mail", "spam folder", "junk folder" };
        private static readonly string[] TrashWords = { "trash", "bin", "the bin", "deleted", "deleted items", "deleted messages", "rubbish", "recycle bin", "the trash" };
        private static readonly string[] ArchiveWords = { "archive", "archived", "the archive" };

        private static string Kind(string to)
        {
            string want = Regex.Replace((to ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
            if (Array.IndexOf(SpamWords, want) >= 0) return "spam";
            if (Array.IndexOf(TrashWords, want) >= 0) return "trash";
            if (Array.IndexOf(ArchiveWords, want) >= 0) return "archive";
            return "folder";
        }

        private static string Destination(MailBox box, string to, out string kind)
        {
            kind = Kind(to);
            string found;
            if (kind == "spam")
            {
                found = box.Special("\\Junk", "[Gmail]/Spam", "[Google Mail]/Spam", "Junk", "Spam", "Junk Email", "Junk E-mail", "Bulk Mail", "Bulk", "INBOX.Junk", "INBOX.Spam");
                if (found == null) throw new MailException("This account has no spam or junk folder that could be found.");
                return found;
            }
            if (kind == "trash")
            {
                found = box.Special("\\Trash", "[Gmail]/Trash", "[Gmail]/Bin", "[Google Mail]/Trash", "[Google Mail]/Bin", "Trash", "Deleted Items", "Deleted Messages", "Deleted", "Bin", "INBOX.Trash");
                if (found == null) throw new MailException("This account has no trash folder that could be found.");
                return found;
            }
            if (kind == "archive")
            {
                found = box.Special("\\Archive", "Archive", "Archives", "INBOX.Archive");
                if (found == null && box.Gmail) found = box.Special("\\All", "[Gmail]/All Mail", "[Google Mail]/All Mail");
                if (found == null) throw new MailException("This account has no archive folder that could be found.");
                return found;
            }
            found = box.Named(to.Trim());
            if (found == null) throw new MailException("There's no mail folder called \"" + to.Trim() + "\". Ask for the folder list to see the names.");
            return found;
        }

        private static string WatchMail(Dictionary<string, object> args)
        {
            string on = Str(args, "on").ToLowerInvariant();
            if (on != "true" && on != "false") throw new MailException("Say on true to start watching or false to stop.");
            bool watch = on == "true";
            SetWatch(watch, HandsSet);
            if (!watch) return "Stopped watching their inbox for new mail.";
            return "Watching their inbox for new mail now. You'll say when something lands and warn them about phishing." + (HandsSet ? " Dealing with phishing on your own is on too, so you move phishing you're sure of to spam." : " Dealing with phishing on your own is off; only they can turn it on, in Email's settings.");
        }

        private static string Who(string from)
        {
            string name, address;
            MailPhish.Address(from, out name, out address);
            string shown = name.Length > 0 ? name : address.Length > 0 ? address : from;
            return Plugins.Line(shown, 60);
        }

        internal static string MoveAsked(Dictionary<string, object> args)
        {
            int uid = Int(args, "uid", 0);
            string folder = FolderOf(args), to = Str(args, "to"), kind = Kind(to);
            MailHead known = uid > 0 ? Known(folder, uid) : null;
            string what = known == null ? "email " + (uid > 0 ? uid.ToString(CultureInfo.InvariantCulture) : "(no uid)") + (folder == "INBOX" ? "" : " in " + Plugins.Line(folder, 60))
                : "the email from " + Who(known.From) + (known.Subject.Length > 0 ? " ('" + Plugins.Line(known.Subject, 80) + "')" : "");
            string where = kind == "spam" ? "to your spam folder" : kind == "trash" ? "to the trash" : kind == "archive" ? "to your archive" : to.Length == 0 ? "somewhere without saying where" : "to " + Plugins.Line(to, 60);
            return "move " + what + " " + where;
        }

        internal static string WatchAsked(Dictionary<string, object> args)
        {
            return Str(args, "on").ToLowerInvariant() == "false" ? "stop watching your inbox for new mail" : "watch your inbox for new mail";
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
