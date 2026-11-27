using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class CrashBody
    {
        [DataMember(Name = "version", Order = 1)] public string Version;
        [DataMember(Name = "platform", Order = 2)] public string Platform;
        [DataMember(Name = "os", Order = 3)] public string Os;
        [DataMember(Name = "store", Order = 4)] public bool Store;
        [DataMember(Name = "lines", Order = 5)] public string[] Lines;
        [DataMember(Name = "id", Order = 6, EmitDefaultValue = false)] public string Id;
    }

    internal enum CrashOutcome { Sent, Refused, Later, Failed }

    // What one pass over her waiting reports did: Busy when another pass (in this Capri or another) held them.
    internal sealed class CrashPass
    {
        internal bool Busy;
        internal long SentThrough;
        internal int Waiting;
        internal int Posted;
    }

    // A report written down before it's sent, so it survives the network, a crash or a restart, and goes again with the
    // same id: her server keeps the first copy of an id and answers later ones with OK.
    internal sealed class WaitingCrash
    {
        internal string Id, Body;
        internal long Made, Through;
        internal int Tries;
    }

    internal static class CrashReports
    {
        internal const string Address = "https://capri-reactions.lachydotmcg.workers.dev/crash";
        internal const int Most = 30;
        internal const int Longest = 400;
        internal const int Biggest = 7800;
        internal const double After = 90;
        internal static readonly string[] Places = { "crash", "closed", "frozen" };
        internal static Func<string, int> Post = Send;

        internal static string[] Since(string[] log, DateTime after, out DateTime newest)
        {
            newest = after;
            List<string> found = new List<string>();
            if (log == null) return found.ToArray();
            foreach (string line in log)
            {
                DateTime at;
                string[] parts = (line ?? "").Split(new[] { " | " }, 3, StringSplitOptions.None);
                if (parts.Length < 3 || parts[0].Length != 19 || Array.IndexOf(Places, parts[1]) < 0) continue;
                if (!DateTime.TryParseExact(parts[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out at) || at <= after) continue;
                if (at > newest) newest = at;
                found.Add(line);
            }
            if (found.Count > Most) found.RemoveRange(0, found.Count - Most);
            string[] cleaned = Brain.Cleaned(found.ToArray());
            for (int i = 0; i < cleaned.Length; i++)
            {
                string line = Private(cleaned[i], Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.UserName, Environment.MachineName);
                cleaned[i] = line.Length > Longest ? line.Substring(0, Longest) : line;
            }
            return cleaned;
        }

        internal static string Private(string line, string home, string user, string machine)
        {
            string text = line ?? "";
            if (!string.IsNullOrEmpty(home) && home.Length > 3) text = Regex.Replace(text, Regex.Escape(home) + "|" + Regex.Escape(home.Replace('\\', '/')), "%USERPROFILE%", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"([A-Za-z]:[\\/]+Users[\\/]+)[^\\/|:*?""<>\r\n]+", "$1[user]", RegexOptions.IgnoreCase);
            foreach (string name in new[] { user, machine })
                if (!string.IsNullOrEmpty(name) && name.Trim().Length >= 3)
                    text = Regex.Replace(text, @"(?<![\p{L}\p{N}_])" + Regex.Escape(name.Trim()) + @"(?![\p{L}\p{N}_])", "[" + (name == user ? "user" : "pc") + "]", RegexOptions.IgnoreCase);
            return text;
        }

        internal static string Body(string version, string os, bool store, string[] lines, string id = null)
        {
            CrashBody body = new CrashBody { Version = version, Platform = "windows", Os = os.Length > 60 ? os.Substring(0, 60) : os, Store = store, Lines = lines, Id = id };
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CrashBody)).WriteObject(stream, body);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }

        internal static string Fitted(string version, string os, bool store, string[] lines, string id = null)
        {
            List<string> kept = new List<string>(lines);
            string body = Body(version, os, store, kept.ToArray(), id);
            while (body.Length > Biggest && kept.Count > 1) { kept.RemoveAt(0); body = Body(version, os, store, kept.ToArray(), id); }
            return body.Length > Biggest ? null : body;
        }

        internal static string Os()
        {
            Version found = Environment.OSVersion.Version;
            return "Windows " + found.Major + "." + found.Minor + "." + found.Build + (Environment.Is64BitOperatingSystem ? " x64" : " x86");
        }

        private static int Send(string body)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(Address);
            request.Method = "POST";
            request.UserAgent = "Capri-Desktop";
            request.ContentType = "application/json";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            try
            {
                using (Stream sent = request.GetRequestStream()) sent.Write(bytes, 0, bytes.Length);
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse()) return (int)response.StatusCode;
            }
            catch (WebException error)
            {
                HttpWebResponse response = error.Response as HttpWebResponse;
                if (response == null) return 0;
                using (response) return (int)response.StatusCode;
            }
        }

        // 200 kept it, 400 and 413 never will, 429 (today's limit) and 0 (no answer) aren't the report's fault, anything else is worth another try.
        internal static CrashOutcome Outcome(int status)
        {
            if (status == 200) return CrashOutcome.Sent;
            if (status == 400 || status == 413) return CrashOutcome.Refused;
            return status == 429 || status == 0 ? CrashOutcome.Later : CrashOutcome.Failed;
        }

        internal const int MostTries = 10;
        internal const int MostWaiting = 5;
        internal static readonly TimeSpan Oldest = TimeSpan.FromDays(14);
        private const string Marker = "sent-through.txt";
        private const string Shape = "capri crash 1";
        private static readonly Regex Named = new Regex(@"^(\d{1,19})-([0-9a-f]{32})\.report$");

        internal static string Folder(string log) { return Path.Combine(Path.GetDirectoryName(log), "crash-reports"); }

        // One pass: her new crash lines become a waiting report, then each waiting report is sent. Only one pass runs at a
        // time across every Capri on this PC, by holding the folder's lock file, which Windows lets go of if she dies. The
        // log time a report covers is in its file name and counts as handled from the moment it's written down, even while
        // a scanner holds the file or after it's damaged, so the same lines never become a second report with another id.
        // A report leaves only once it's sent, refused, too old or out of tries, and sent-through.txt moves past it first,
        // so dying in between sends it again with its own id, never as a new one.
        internal static CrashPass Pass(string folder, string log, long through, string version, string os, bool store, Func<bool> allowed, Func<string, int> post, DateTime utcNow)
        {
            CrashPass result = new CrashPass { SentThrough = through };
            Directory.CreateDirectory(folder);
            using (FileStream held = Claim(folder, 0))
            {
                if (held == null) { result.Busy = true; return result; }
                if (!allowed()) return result;
                long named;
                List<WaitingCrash> waiting = Read(folder, out named);
                long handled = Math.Max(Math.Max(through, SentThrough(folder)), named);
                string[] lines;
                lock (typeof(Brain)) lines = File.Exists(log) ? File.ReadAllLines(log) : new string[0];
                DateTime newest;
                string[] picked = Since(lines, new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, handled)), DateTimeKind.Local), out newest);
                if (picked.Length > 0)
                {
                    string id = Guid.NewGuid().ToString("N");
                    string body = Fitted(version, os, store, picked, id);
                    if (body == null) MarkSent(folder, newest.Ticks);
                    else
                    {
                        WaitingCrash made = new WaitingCrash { Id = id, Body = body, Made = utcNow.Ticks, Through = newest.Ticks };
                        Save(folder, made);
                        waiting.Add(made);
                    }
                }
                waiting.Sort(delegate(WaitingCrash a, WaitingCrash b) { return a.Made.CompareTo(b.Made); });
                while (waiting.Count > MostWaiting) { Retire(folder, waiting[0]); waiting.RemoveAt(0); }
                foreach (WaitingCrash report in waiting)
                {
                    if (utcNow.Ticks - report.Made > Oldest.Ticks || report.Tries >= MostTries) { Retire(folder, report); continue; }
                    if (!allowed()) break;
                    CrashOutcome outcome = Outcome(post(report.Body));
                    result.Posted++;
                    if (outcome == CrashOutcome.Sent || outcome == CrashOutcome.Refused) { Retire(folder, report); continue; }
                    if (outcome == CrashOutcome.Later) break;
                    report.Tries++;
                    if (!allowed()) break;
                    if (report.Tries >= MostTries) Retire(folder, report);
                    else Save(folder, report);
                }
                result.SentThrough = Math.Max(through, SentThrough(folder));
                result.Waiting = Directory.GetFiles(folder, "*.report").Length;
            }
            return result;
        }

        // Turning crash reports off takes away what's waiting, past the lines it covered, so turning them on again later
        // can't send them under a new id. A pass that's sending holds the lock until its post ends, so this waits for it.
        internal static bool Clear(string folder, int waitMs)
        {
            if (!Directory.Exists(folder)) return true;
            using (FileStream held = Claim(folder, waitMs))
            {
                if (held == null) return false;
                foreach (string file in Directory.GetFiles(folder, "*.report"))
                {
                    Match name = Named.Match(Path.GetFileName(file));
                    long through;
                    if (name.Success && long.TryParse(name.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out through)) MarkSent(folder, through);
                    File.Delete(file);
                }
                return Directory.GetFiles(folder, "*.report").Length == 0;
            }
        }

        private static FileStream Claim(string folder, int waitMs)
        {
            DateTime until = DateTime.UtcNow.AddMilliseconds(waitMs);
            while (true)
            {
                try { return new FileStream(Path.Combine(folder, ".lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                if (DateTime.UtcNow >= until) return null;
                Thread.Sleep(100);
            }
        }

        internal static List<WaitingCrash> Read(string folder)
        {
            long named;
            return Read(folder, out named);
        }

        // The reports that can be read now, and in `named` the newest log time any report's file name covers, read or not.
        // One a scanner holds waits for the next pass; a damaged one can't be sent, so it moves sent-through.txt past its lines.
        private static List<WaitingCrash> Read(string folder, out long named)
        {
            named = 0;
            List<WaitingCrash> found = new List<WaitingCrash>();
            if (!Directory.Exists(folder)) return found;
            foreach (string file in Directory.GetFiles(folder, "*.report"))
            {
                Match name = Named.Match(Path.GetFileName(file));
                long covered;
                if (!name.Success || !long.TryParse(name.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out covered)) continue;
                named = Math.Max(named, covered);
                string id = name.Groups[2].Value;
                string[] lines;
                try { lines = File.ReadAllLines(file); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                long made, through;
                int tries;
                if (lines.Length == 6 && lines[0] == Shape && lines[1] == id && long.TryParse(lines[2], NumberStyles.None, CultureInfo.InvariantCulture, out made)
                    && long.TryParse(lines[3], NumberStyles.None, CultureInfo.InvariantCulture, out through) && through == covered
                    && int.TryParse(lines[4], NumberStyles.None, CultureInfo.InvariantCulture, out tries) && lines[5].Length <= Biggest && lines[5].Contains("\"id\":\"" + id + "\""))
                    found.Add(new WaitingCrash { Id = id, Made = made, Through = through, Tries = tries, Body = lines[5] });
                else
                {
                    MarkSent(folder, covered);
                    File.Delete(file);
                }
            }
            return found;
        }

        private static string FileOf(string folder, WaitingCrash report) { return Path.Combine(folder, report.Through.ToString(CultureInfo.InvariantCulture) + "-" + report.Id + ".report"); }

        private static void Save(string folder, WaitingCrash report)
        {
            Replace(FileOf(folder, report), new[] { Shape, report.Id, report.Made.ToString(CultureInfo.InvariantCulture), report.Through.ToString(CultureInfo.InvariantCulture), report.Tries.ToString(CultureInfo.InvariantCulture), report.Body });
        }

        private static void Retire(string folder, WaitingCrash report)
        {
            MarkSent(folder, report.Through);
            File.Delete(FileOf(folder, report));
        }

        // Missing means nothing was sent yet. One that's there but held throws, so the pass stops instead of guessing low.
        internal static long SentThrough(string folder)
        {
            string file = Path.Combine(folder, Marker);
            if (!File.Exists(file)) return 0;
            long ticks;
            return long.TryParse(File.ReadAllText(file).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out ticks) ? ticks : 0;
        }

        private static void MarkSent(string folder, long through)
        {
            if (through <= SentThrough(folder)) return;
            Replace(Path.Combine(folder, Marker), new[] { through.ToString(CultureInfo.InvariantCulture) });
        }

        // Written through to the disk, so a power cut can't leave an empty file in its place. A virus scanner or the search
        // indexer can hold a file she wrote a moment ago, so the swap gets a few short tries.
        private static void Replace(string file, string[] lines)
        {
            string fresh = file + ".new";
            using (FileStream stream = new FileStream(fresh, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
            {
                using (StreamWriter writer = new StreamWriter(stream, new UTF8Encoding(false), 4096, true))
                    foreach (string line in lines) writer.WriteLine(line);
                stream.Flush(true);
            }
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(file)) File.Replace(fresh, file, null);
                    else File.Move(fresh, file);
                    return;
                }
                catch (IOException) { if (attempt == 5) throw; }
                catch (UnauthorizedAccessException) { if (attempt == 5) throw; }
                Thread.Sleep(100 * attempt);
            }
        }

        internal static string Check()
        {
            DateTime from = new DateTime(2026, 11, 19, 9, 0, 0, DateTimeKind.Local), newest;
            string key = "sk-ant-api03-" + new string('Q', 40) + "7";
            string[] log =
            {
                "2026-11-19 08:59:59 | crash | - | NullReferenceException: before the last report",
                "2026-11-19 09:00:01 | api.anthropic.com/v1/messages | 900 ms | HTTP 529 overloaded",
                "2026-11-19 09:00:02 | crash | - | IOException: C:\\Users\\Someone Else\\AppData\\Local\\Capri\\notes.md is in use with " + key,
                "2026-11-19 09:00:03 | closed | - | Capri v0.9.4 (PID 1234) closed unexpectedly around 2026-11-19 09:00:03. Application Error: module ntdll.dll; exception 0xc0000374.",
                "2026-11-19 09:00:04 | frozen | - | Stuck 10 s in her talk timer",
                "not a log line",
                "2026-11-19 09:00:05 | crash | - | " + new string('x', 900)
            };
            string[] picked = Since(log, from, out newest);
            if (picked.Length != 4 || newest != new DateTime(2026, 11, 19, 9, 0, 5, DateTimeKind.Local)) return "Crash reports picked the wrong lines: " + picked.Length + ".";
            if (picked[0].Contains(key) || picked[0].Contains("Someone Else") || !picked[0].Contains("C:\\Users\\[user]\\AppData")) return "A crash report kept a key or a user folder: " + picked[0];
            if (!picked[1].Contains("ntdll.dll") || !picked[2].Contains("frozen") || picked[3].Length != Longest) return "Crash reports lost a close, a freeze or their length cap.";
            if (Since(log, newest, out newest).Length != 0) return "Crash reports would send the same crash twice.";
            List<string> many = new List<string>();
            for (int i = 0; i < Most + 12; i++) many.Add("2026-11-19 10:" + (i / 60).ToString("00") + ":" + (i % 60).ToString("00") + " | crash | - | Crash " + i);
            string[] capped = Since(many.ToArray(), from, out newest);
            if (capped.Length != Most || !capped[Most - 1].EndsWith("Crash " + (Most + 11), StringComparison.Ordinal)) return "Crash reports didn't keep the newest " + Most + " lines.";
            string named = Private("Couldn't open D:\\Games\\GoodName\\x for goodname on GOODPC; C:/Users/GoodName/Desktop", "C:\\Users\\GoodName", "GoodName", "GOODPC");
            if (named != "Couldn't open D:\\Games\\[user]\\x for [user] on [pc]; %USERPROFILE%/Desktop") return "Crash reports kept a name: " + named;
            if (Private("pat the cat", "C:\\Users\\al", "al", "pc") != "pat the cat") return "Crash reports took short names out of words.";
            string body = Body("0.9.4", "Windows 10.0.26100 x64", true, new[] { "a \"quoted\" line" });
            if (body != "{\"version\":\"0.9.4\",\"platform\":\"windows\",\"os\":\"Windows 10.0.26100 x64\",\"store\":true,\"lines\":[\"a \\\"quoted\\\" line\"]}") return "A crash report's body isn't the shape her server takes: " + body;
            string fitted = Fitted("0.9.4", "Windows 10.0.26100 x64", false, capped.Select(delegate(string line) { return line + new string('y', Longest - line.Length); }).ToArray());
            if (fitted == null || fitted.Length > Biggest || fitted.IndexOf("Crash " + (Most + 11), StringComparison.Ordinal) < 0 || fitted.IndexOf("Crash 12y", StringComparison.Ordinal) >= 0) return "A crash report didn't fit her server's size limit with its newest lines.";
            string withId = Body("0.9.4", "Windows 10.0.26100 x64", true, new[] { "line" }, "0123456789abcdef0123456789abcdef");
            if (!withId.EndsWith(",\"lines\":[\"line\"],\"id\":\"0123456789abcdef0123456789abcdef\"}", StringComparison.Ordinal)) return "A crash report's id isn't where her server reads it: " + withId;
            if (Outcome(200) != CrashOutcome.Sent || Outcome(400) != CrashOutcome.Refused || Outcome(413) != CrashOutcome.Refused || Outcome(429) != CrashOutcome.Later || Outcome(0) != CrashOutcome.Later || Outcome(503) != CrashOutcome.Failed)
                return "Crash reports would give up on a report her server never took, or retry one it refused.";
            if (!Regex.IsMatch(Os(), @"^Windows \d+\.\d+\.\d+ x(64|86)$")) return "Crash reports named Windows oddly: " + Os();
            return null;
        }

        // Her waiting reports against a pretend server that keeps the first copy of each id, as hers does. Every "start" is a
        // fresh pass that remembers nothing but the folder.
        internal static string CheckWaiting(string root)
        {
            DateTime now = new DateTime(2026, 11, 26, 12, 0, 0, DateTimeKind.Utc);
            DateTime crashed = new DateTime(2026, 11, 26, 13, 0, 0, DateTimeKind.Local);
            Func<bool> yes = delegate { return true; };
            Func<string, string> idOf = delegate(string body) { return Regex.Match(body ?? "", "\"id\":\"([0-9a-f]{32})\"").Groups[1].Value; };
            Dictionary<string, string> kept = new Dictionary<string, string>();
            List<string> posted = new List<string>();
            Func<string, int> stores = delegate(string body) { lock (posted) { posted.Add(body); if (!kept.ContainsKey(idOf(body))) kept[idOf(body)] = body; } return 200; };
            Func<string, int> down = delegate(string body) { lock (posted) posted.Add(body); return 0; };
            Func<string, int> lostReply = delegate(string body) { stores(body); return 0; };
            Func<string, string> start = delegate(string name)
            {
                string place = Path.Combine(root, name);
                if (Directory.Exists(place)) Directory.Delete(place, true);
                Directory.CreateDirectory(place);
                string log = Path.Combine(place, "errors.txt");
                File.WriteAllLines(log, new[] { crashed.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " | crash | - | IOException: " + name, crashed.AddSeconds(1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " | frozen | - | Stuck 10 s in " + name });
                kept.Clear();
                posted.Clear();
                return log;
            };
            Func<string, Func<string, int>, DateTime, CrashPass> pass = delegate(string log, Func<string, int> post, DateTime at) { return Pass(Folder(log), log, 0, "0.9.4", "Windows 10.0.26100 x64", false, yes, post, at); };
            long through = crashed.AddSeconds(1).Ticks;

            string down1 = start("down");
            CrashPass first = pass(down1, down, now);
            if (first.Posted != 1 || first.Waiting != 1 || first.SentThrough != 0 || Read(Folder(down1)).Count != 1) return "A crash report that couldn't reach her server wasn't kept for next time.";
            string firstId = idOf(posted[0]);
            CrashPass next = pass(down1, stores, now.AddMinutes(5));
            if (next.Posted != 1 || next.Waiting != 0 || next.SentThrough != through || kept.Count != 1 || !kept.ContainsKey(firstId) || idOf(posted[1]) != firstId || posted[1] != posted[0])
                return "A kept crash report wasn't sent on the next start with the same id and words.";
            if (pass(down1, stores, now.AddMinutes(10)).Posted != 0 || kept.Count != 1) return "A crash report her server took was sent again.";

            string lost = start("lost");
            CrashPass stored = pass(lost, lostReply, now);
            CrashPass retried = pass(lost, stores, now.AddMinutes(2));
            if (stored.Waiting != 1 || retried.Waiting != 0 || posted.Count != 2 || posted[0] != posted[1] || kept.Count != 1 || pass(lost, stores, now.AddMinutes(4)).Posted != 0)
                return "A crash report whose reply was lost went again as a new report, or kept going: " + kept.Count + " kept from " + posted.Count + " sends.";

            string both = start("both");
            using (ManualResetEvent inside = new ManualResetEvent(false))
            using (ManualResetEvent release = new ManualResetEvent(false))
            {
                CrashPass slow = null;
                Thread other = new Thread(delegate() { slow = pass(both, delegate(string body) { inside.Set(); release.WaitOne(20000); return stores(body); }, now); });
                other.IsBackground = true;
                other.Start();
                if (!inside.WaitOne(20000)) return "A crash report pass never reached her server.";
                CrashPass second = pass(both, stores, now);
                int reports = Directory.GetFiles(Folder(both), "*.report").Length;
                if (Clear(Folder(both), 0)) return "Turning crash reports off took a report away while it was being sent.";
                release.Set();
                other.Join(20000);
                if (!second.Busy || second.Posted != 0 || reports != 1 || slow == null || slow.Posted != 1 || posted.Count != 1 || kept.Count != 1 || pass(both, stores, now.AddMinutes(1)).Posted != 0)
                    return "Two Capris sending at once sent a crash twice or made two reports of it: " + posted.Count + " sends, " + reports + " reports.";
            }

            foreach (int refusal in new[] { 400, 413 })
            {
                string refused = start("refused" + refusal);
                CrashPass dropped = pass(refused, delegate(string body) { posted.Add(body); return refusal; }, now);
                if (dropped.Posted != 1 || dropped.Waiting != 0 || dropped.SentThrough != through || pass(refused, stores, now.AddMinutes(1)).Posted != 0) return "A crash report her server refused with " + refusal + " was kept or sent again.";
            }

            foreach (int notNow in new[] { 429, 0 })
            {
                string limited = start("later" + notNow);
                for (int i = 0; i < MostTries + 2; i++) pass(limited, delegate(string body) { return notNow; }, now.AddMinutes(i));
                List<WaitingCrash> waiting = Read(Folder(limited));
                if (waiting.Count != 1 || waiting[0].Tries != 0) return "A crash report that got " + (notNow == 0 ? "no answer" : "today's limit") + " was dropped or used up a try.";
            }

            string watermark = start("watermark");
            Pass(Folder(watermark), watermark, crashed.Ticks, "0.9.4", "Windows 10.0.26100 x64", false, yes, stores, now);
            if (posted.Count != 1 || posted[0].Contains("IOException") || !posted[0].Contains("frozen")) return "A crash report resent lines from before the last one.";

            string locked = start("locked");
            pass(locked, down, now);
            string lockedFile = Directory.GetFiles(Folder(locked), "*.report")[0];
            posted.Clear();
            using (new FileStream(lockedFile, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                CrashPass blind = pass(locked, stores, now.AddMinutes(1));
                if (blind.Posted != 0 || Directory.GetFiles(Folder(locked), "*.report").Length != 1) return "A crash report a scanner held became a second report of the same lines.";
            }
            if (pass(locked, stores, now.AddMinutes(2)).Posted != 1 || kept.Count != 1 || pass(locked, stores, now.AddMinutes(3)).Posted != 0) return "A crash report a scanner held wasn't sent once it was free.";

            string damaged = start("damaged");
            pass(damaged, down, now);
            File.WriteAllText(Directory.GetFiles(Folder(damaged), "*.report")[0], "");
            posted.Clear();
            CrashPass afterDamage = pass(damaged, stores, now.AddMinutes(1));
            if (afterDamage.Posted != 0 || afterDamage.Waiting != 0 || afterDamage.SentThrough != through) return "A damaged crash report was sent again as a new one.";

            string died = start("died");
            pass(died, down, now);
            string diedFile = Directory.GetFiles(Folder(died), "*.report")[0];
            string[] diedLines = File.ReadAllLines(diedFile);
            pass(died, stores, now.AddMinutes(1));
            File.WriteAllLines(diedFile, diedLines);
            CrashPass resent = pass(died, stores, now.AddMinutes(2));
            if (resent.Posted != 1 || posted.Count != 3 || idOf(posted[2]) != idOf(posted[1]) || kept.Count != 1 || pass(died, stores, now.AddMinutes(3)).Posted != 0)
                return "A crash report she died before deleting was sent under a new id.";

            string failing = start("failing");
            Func<string, int> broken = delegate(string body) { posted.Add(body); return 503; };
            for (int i = 0; i < MostTries + 2; i++) pass(failing, broken, now.AddMinutes(i));
            if (posted.Count != MostTries || Read(Folder(failing)).Count != 0 || SentThrough(Folder(failing)) != through) return "A crash report her server kept failing on was tried " + posted.Count + " times instead of " + MostTries + ".";

            string old = start("old");
            pass(old, down, now);
            posted.Clear();
            CrashPass late = pass(old, stores, now + Oldest + TimeSpan.FromHours(1));
            if (late.Posted != 0 || late.Waiting != 0 || late.SentThrough != through) return "A crash report older than " + Oldest.TotalDays + " days was still sent.";

            string many = start("many");
            for (int i = 0; i < MostWaiting + 3; i++)
            {
                File.AppendAllText(many, crashed.AddMinutes(i + 1).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " | crash | - | Crash " + i + Environment.NewLine);
                pass(many, down, now.AddMinutes(i));
            }
            if (Read(Folder(many)).Count != MostWaiting || SentThrough(Folder(many)) < through) return "More than " + MostWaiting + " crash reports waited at once, or the oldest went without marking its lines.";

            string off = start("off");
            pass(off, down, now);
            posted.Clear();
            if (!Clear(Folder(off), 1000) || Read(Folder(off)).Count != 0) return "Turning crash reports off left one waiting.";
            CrashPass refusedConsent = Pass(Folder(off), off, 0, "0.9.4", "Windows 10.0.26100 x64", false, delegate { return false; }, stores, now);
            if (refusedConsent.Posted != 0 || Read(Folder(off)).Count != 0 || pass(off, stores, now.AddMinutes(1)).Posted != 0 || posted.Count != 0)
                return "A crash report was sent while crash reports were off, or came back when they were turned on again.";
            return null;
        }
    }

    internal sealed partial class Preferences
    {
        [DataMember] public bool CrashReports;
        [DataMember] public long CrashesSentThrough;
    }

    internal sealed partial class Companion
    {
        // A pass 90 s after she starts, then, while reports still wait, again after 2, 10, 30 and 60 minutes.
        private static readonly double[] CrashRetries = { 120, 600, 1800, 3600 };
        private ToolStripMenuItem crashItem;
        private bool crashSending, crashCleared;
        private double crashNextAt = CrashReports.After;
        private int crashRound;

        internal bool CrashReportsOn { get { return preferences.CrashReports; } }

        private string CrashItemText { get { return preferences.CrashReports ? "Send crash reports: on" : "Send crash reports: off"; } }

        private ToolStripMenuItem CrashMenuItem()
        {
            crashItem = new ToolStripMenuItem(CrashItemText, null, delegate { ToggleCrashReports(); });
            return crashItem;
        }

        internal void ToggleCrashReports()
        {
            preferences.CrashReports = !preferences.CrashReports;
            if (preferences.CrashReports) preferences.CrashesSentThrough = Math.Max(preferences.CrashesSentThrough, DateTime.Now.AddDays(-1).Ticks);
            if (crashItem != null) crashItem.Text = CrashItemText;
            SavePreferences();
            Tell(preferences.CrashReports ? "crash reports on" : "crash reports off", preferences.CrashReports
                ? "Crash reports on. If I crash, I send what broke, my version and your Windows version. No chats, no keys, no names."
                : "Crash reports off. Nothing about my crashes leaves your PC.");
            crashNextAt = 0;
            crashRound = 0;
            crashCleared = false;
            CheckCrashReports(clock.Elapsed.TotalSeconds);
        }

        // While they're off, what's waiting is taken away: right when they turn them off, and on each of her checks until it's
        // gone (a send in flight, a quit or settings reset to off can stop the first go).
        private void ClearCrashReports(double now)
        {
            if (crashCleared || crashSending || now < crashNextAt || Brain.LogPath == null) return;
            crashSending = true;
            string folder = CrashReports.Folder(Brain.LogPath);
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool cleared = false;
                try { cleared = CrashReports.Clear(folder, 20000); }
                catch (Exception) { }
                Post(delegate
                {
                    crashSending = false;
                    crashCleared = cleared;
                    if (!cleared) crashNextAt = now + 60;
                });
            });
        }

        private void CheckCrashReports(double now)
        {
            if (!preferences.CrashReports) { ClearCrashReports(now); return; }
            if (crashSending || now < crashNextAt || Brain.LogPath == null) return;
            crashSending = true;
            string path = Brain.LogPath, folder = CrashReports.Folder(path), version = News.Version, os = CrashReports.Os();
            bool store = Packaged.Is;
            long through = preferences.CrashesSentThrough;
            Func<string, int> post = CrashReports.Post;
            Func<bool> allowed = delegate { return preferences.CrashReports; };
            ThreadPool.QueueUserWorkItem(delegate
            {
                CrashPass pass = null;
                try { pass = CrashReports.Pass(folder, path, through, version, os, store, allowed, post, DateTime.UtcNow); }
                catch (Exception) { }
                Post(delegate
                {
                    crashSending = false;
                    bool again = pass == null || pass.Busy || pass.Waiting > 0;
                    if (!preferences.CrashReports) crashNextAt = 0;
                    else crashNextAt = again && crashRound < CrashRetries.Length ? now + CrashRetries[crashRound++] : double.MaxValue;
                    if (pass != null && pass.SentThrough > preferences.CrashesSentThrough) { preferences.CrashesSentThrough = pass.SentThrough; SavePreferences(); }
                });
            });
        }
    }
}
