using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class MailNews
    {
        internal int Uid;
        internal string From = "", Subject = "", Key = "";
        internal MailVerdict Verdict;
        internal string Moved;
        internal bool Spam, Gmail, Already;
        internal string Trouble;
        internal DateTime At = DateTime.UtcNow;

        internal bool Phishing { get { return Verdict != null && Verdict.Level == MailVerdict.Phishing; } }
        internal bool Odd { get { return Verdict != null && Verdict.Level == MailVerdict.Suspicious; } }
    }

    internal sealed class MailWatch
    {
        internal const int PeekBytes = 1024 * 1024;
        internal readonly MailAccount Account;
        internal Func<bool> OwnHands = delegate { return false; };
        internal int IdleMilliseconds = 4 * 60 * 1000;
        internal int PollMilliseconds = 60 * 1000;
        internal int FirstWait = 15;
        internal TimeSpan Fresh = TimeSpan.FromHours(6);
        private readonly object gate = new object();
        private readonly List<MailNews> waiting = new List<MailNews>();
        private readonly List<string> known = new List<string>();
        private readonly AutoResetEvent nudge = new AutoResetEvent(false);
        private volatile bool running;
        private volatile MailBox box;
        private Thread thread;
        private long validity, highest;
        private bool seeded;
        private string state = "starting";

        internal MailWatch(MailAccount account) { Account = account; }

        internal string State { get { lock (gate) return state; } }

        internal bool Running { get { return running; } }

        internal bool Waiting { get { lock (gate) return waiting.Count > 0; } }

        internal List<MailNews> Take()
        {
            lock (gate)
            {
                List<MailNews> all = new List<MailNews>(waiting);
                waiting.Clear();
                return all;
            }
        }

        internal void Start()
        {
            if (thread != null) return;
            running = true;
            thread = new Thread(Run) { IsBackground = true, Name = "Capri mail watch" };
            thread.Start();
        }

        internal void Stop()
        {
            if (!running) return;
            running = false;
            nudge.Set();
            Thread worker = thread;
            MailBox open = box;
            if (open == null) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                open.Wake();
                if (worker != null && !worker.Join(3000)) open.Abort();
            });
        }

        internal bool Join(int milliseconds)
        {
            Thread worker = thread;
            return worker == null || worker.Join(milliseconds);
        }

        private void Say(string now) { lock (gate) state = now; }

        private void Run()
        {
            int wait = FirstWait;
            while (running)
            {
                DateTime began = DateTime.UtcNow;
                try
                {
                    using (MailBox open = MailBox.Open(Account))
                    {
                        box = open;
                        if (!running) break;
                        Watch(open);
                    }
                }
                catch (Exception error)
                {
                    if (!running) break;
                    string said = error is MailException ? error.Message : "Watching mail stopped: " + error.Message;
                    if (Account.Password.Length > 0) said = said.Replace(Account.Password, "***");
                    Say(said);
                    if (said.Contains("refused the sign-in")) wait = Math.Max(wait, 1800);
                }
                finally { box = null; }
                if (!running) break;
                if ((DateTime.UtcNow - began).TotalMinutes >= 5) wait = FirstWait;
                nudge.WaitOne(TimeSpan.FromSeconds(wait));
                wait = Math.Min(wait * 2, 1800);
            }
            Say("stopped");
        }

        private void Watch(MailBox open)
        {
            MailMark mark = open.Select("INBOX", true);
            if (!seeded || mark.Validity != validity)
            {
                validity = mark.Validity;
                highest = mark.Next > 0 ? mark.Next - 1 : open.Top();
                seeded = true;
            }
            else Look(open);
            Say("watching");
            bool idle = open.Can("IDLE");
            while (running)
            {
                if (idle && !open.Idle(IdleMilliseconds)) idle = false;
                if (!idle)
                {
                    nudge.WaitOne(PollMilliseconds);
                    if (!running) break;
                    open.Noop();
                }
                if (!running) break;
                Look(open);
            }
        }

        private void Look(MailBox open)
        {
            List<int> fresh = open.After(highest);
            if (fresh.Count == 0) return;
            highest = Math.Max(highest, fresh[fresh.Count - 1]);
            if (fresh.Count > 10) fresh = fresh.GetRange(fresh.Count - 10, 10);
            foreach (int uid in fresh)
            {
                if (!running) return;
                MailPeek peek = open.Peek(uid, PeekBytes);
                if (peek == null) continue;
                if (peek.Arrived != DateTime.MinValue && peek.Arrived < DateTime.UtcNow - Fresh) continue;
                MailVerdict verdict = MailPhish.Check(peek.Raw, peek.Names);
                MailNews news = new MailNews { Uid = uid, From = verdict.From, Subject = verdict.Subject, Verdict = verdict };
                news.Key = verdict.MessageId.Length > 0 ? verdict.MessageId : verdict.From + "|" + verdict.Subject + "|" + peek.Arrived.Ticks;
                lock (gate)
                {
                    if (known.Contains(news.Key)) continue;
                    known.Add(news.Key);
                    if (known.Count > 500) known.RemoveAt(0);
                }
                MailTools.Remember("INBOX", uid, verdict.From, verdict.Subject, verdict.MessageId);
                if (verdict.Sure && OwnHands())
                {
                    try
                    {
                        MailMove done = MailTools.MoveOne(Account, "INBOX", uid, "spam", verdict.MessageId);
                        news.Moved = done.Target;
                        news.Spam = done.Kind == "spam";
                        news.Gmail = done.Gmail;
                        news.Already = done.Already;
                    }
                    catch (MailException error) { news.Trouble = error.Message; }
                    catch (Exception error) { news.Trouble = "Moving it didn't work: " + error.Message; }
                    if (news.Trouble != null && Account.Password.Length > 0) news.Trouble = news.Trouble.Replace(Account.Password, "***");
                }
                lock (gate) waiting.Add(news);
            }
        }

        internal const string GmailBinned = "That's phishing, choom, I reported it as spam and binned it.";
        internal const string JunkMoved = "That's phishing, choom, I moved it to your junk folder.";
        internal const string AlreadyOut = "That's phishing, choom, and it's already out of your inbox.";
        private static readonly string[] OneLines = { "Oop, something just came through.", "Ooh, new mail just landed.", "Psst. You've got a new email.", "New mail just came in, choom." };
        private static readonly string[] ManyLines = { "Oop, {0} new emails just came through.", "Ooh, {0} new emails just landed." };
        private static readonly string[] OddLines = { " Something about it looks a bit off, so go easy on the links. Want me to check it?", " It looks a bit off to me. Want me to check it before you click anything?" };
        private static readonly string[] PhishLines = { " And that one's phishing, choom. Don't click anything in it.", " Careful, that one's phishing. Don't click anything in it." };
        private static readonly string[] OddAsks = { " It wants your {0}, so don't send {1}. Want me to check it?", " It's asking for your {0}, so hold off on sending {1}. Want me to check it?" };
        private static readonly string[] PhishAsks = { " And that one's phishing, choom. It wants your {0}, so don't reply or click anything in it.", " Careful, that one's phishing. It's after your {0}, so don't reply or click anything in it." };

        internal static string Line(List<MailNews> news)
        {
            if (news == null || news.Count == 0) return null;
            MailNews moved = news.Find(one => one.Moved != null);
            MailNews failed = news.Find(one => one.Trouble != null);
            MailNews phish = news.Find(one => one.Phishing && one.Moved == null);
            MailNews odd = news.Find(one => one.Odd);
            bool late = news.TrueForAll(one => (DateTime.UtcNow - one.At).TotalMinutes >= 10);
            string start = late ? (news.Count == 1 ? "Some mail came in while you were busy." : news.Count + " emails came in while you were busy.") : news.Count == 1 ? Lines.Pick(OneLines) : string.Format(Lines.Pick(ManyLines), news.Count);
            if (moved != null)
            {
                string done = moved.Already ? AlreadyOut : moved.Spam && moved.Gmail ? GmailBinned : moved.Spam ? JunkMoved : "That's phishing, choom, I moved it to " + moved.Moved + ".";
                if (news.Count > 1) done = "One of them was phishing, choom, " + (moved.Already ? "and it's already out of your inbox." : "so I " + (moved.Spam && moved.Gmail ? "reported it as spam and binned it." : moved.Spam ? "moved it to your junk folder." : "moved it to " + moved.Moved + "."));
                return start + " " + done + (phish != null && phish != moved ? " Another looks like phishing too, so don't click anything in it." : "");
            }
            if (failed != null) return start + (news.Count > 1 ? " One of them is phishing, choom." : " That's phishing, choom.") + " I tried to move it to junk, but " + Lower(failed.Trouble) + " Don't click anything in it.";
            if (phish != null) return start + (news.Count > 1 ? " One of them is phishing, choom. Don't click anything in it." : Wanted(phish, PhishLines, PhishAsks));
            if (odd != null) return start + (news.Count > 1 ? " One of them looks a bit off. Want me to check it?" : Wanted(odd, OddLines, OddAsks));
            return start;
        }

        private static string Wanted(MailNews one, string[] usual, string[] asks)
        {
            string wants = one.Verdict == null ? "" : one.Verdict.Wants;
            return wants.Length == 0 ? Lines.Pick(usual) : string.Format(Lines.Pick(asks), wants, wants == "password" ? "it" : "them");
        }

        private static string Lower(string text)
        {
            string t = (text ?? "it didn't work.").Trim();
            if (t.Length > 1 && char.IsUpper(t[0]) && !char.IsUpper(t[1])) t = char.ToLowerInvariant(t[0]) + t.Substring(1);
            if (!t.EndsWith(".", StringComparison.Ordinal)) t += ".";
            return t;
        }

        internal static string Note(List<MailNews> news)
        {
            StringBuilder sb = new StringBuilder();
            foreach (MailNews one in news)
            {
                sb.Append("uid " + one.Uid + " in INBOX | from " + Plugins.Line(one.From, 120) + " | subject " + Plugins.Line(one.Subject.Length == 0 ? "(no subject)" : one.Subject, 140) + " | verdict: " + (one.Verdict == null ? "not checked" : one.Verdict.Level) + "\n");
                if (one.Verdict != null) for (int i = 0; i < one.Verdict.Reasons.Count && i < 5; i++) sb.Append("- " + one.Verdict.Reasons[i] + "\n");
                if (one.Moved != null && one.Already) sb.Append("It was already out of the inbox, in " + one.Moved + ", so you didn't move it again.\n");
                else if (one.Moved != null) sb.Append("You moved it to " + one.Moved + " on your own, since they let you deal with phishing." + (one.Spam && one.Gmail ? " On Gmail that reports it as spam." : "") + "\n");
                if (one.Trouble != null) sb.Append("You tried to move it to junk on your own, but: " + one.Trouble + "\n");
            }
            return "(New mail came into their inbox while you watched it, and you told them about it.)\n" + Plugins.Fence("Watch for new mail", "Email", sb.ToString().TrimEnd());
        }
    }
}
