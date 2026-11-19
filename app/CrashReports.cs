using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    }

    internal static class CrashReports
    {
        internal const string Address = "https://capri-reactions.lachydotmcg.workers.dev/crash";
        internal const int Most = 30;
        internal const int Longest = 400;
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

        internal static string Body(string version, string os, bool store, string[] lines)
        {
            CrashBody body = new CrashBody { Version = version, Platform = "windows", Os = os.Length > 60 ? os.Substring(0, 60) : os, Store = store, Lines = lines };
            using (MemoryStream stream = new MemoryStream())
            {
                new DataContractJsonSerializer(typeof(CrashBody)).WriteObject(stream, body);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
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

        internal static bool Settled(int status) { return status == 200 || status == 400 || status == 413; }

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
            if (!Settled(200) || !Settled(400) || Settled(0) || Settled(429) || Settled(503)) return "Crash reports would give up on a report her server never took, or retry one it refused.";
            if (!Regex.IsMatch(Os(), @"^Windows \d+\.\d+\.\d+ x(64|86)$")) return "Crash reports named Windows oddly: " + Os();
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
        private ToolStripMenuItem crashItem;
        private bool crashChecked, crashSending;

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
            if (preferences.CrashReports) { crashChecked = false; CheckCrashReports(CrashReports.After); }
        }

        private void CheckCrashReports(double now)
        {
            if (crashChecked || crashSending || now < CrashReports.After || !preferences.CrashReports || Brain.LogPath == null) return;
            crashChecked = true;
            crashSending = true;
            string path = Brain.LogPath, version = News.Version, os = CrashReports.Os();
            bool store = Packaged.Is;
            DateTime through = new DateTime(Math.Max(0, Math.Min(DateTime.MaxValue.Ticks, preferences.CrashesSentThrough)), DateTimeKind.Local);
            Func<string, int> post = CrashReports.Post;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long sent = 0;
                try
                {
                    string[] log;
                    lock (typeof(Brain)) log = File.Exists(path) ? File.ReadAllLines(path) : new string[0];
                    DateTime newest;
                    string[] lines = CrashReports.Since(log, through, out newest);
                    if (lines.Length > 0 && CrashReports.Settled(post(CrashReports.Body(version, os, store, lines)))) sent = newest.Ticks;
                }
                catch (Exception) { }
                Post(delegate
                {
                    crashSending = false;
                    if (sent > preferences.CrashesSentThrough) { preferences.CrashesSentThrough = sent; SavePreferences(); }
                });
            });
        }
    }
}
