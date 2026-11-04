// Downloads the published APK from GitHub and installs it on a USB-connected Quest,
// the same way a user would sideload it. Results go to the Console and Logs/sideload-test.txt.
using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace CapriRoom.EditorTools
{
    public static class CapriSideloadTest
    {
        private static string Url => $"https://github.com/fadievr/capri-room/releases/download/v{CapriRelease.Version}/CapriRoom-v{CapriRelease.Version}.apk";
        private const string Package = "com.capriroom.quest";

        [MenuItem("Capri/Release/3. Test sideload from GitHub")]
        public static void Run()
        {
            string adb = FindAdb();
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string apk = Path.Combine(root, "Temp", "github-CapriRoom.apk");
            string logPath = Path.Combine(root, "Logs", "sideload-test.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            Task.Run(() => Test(adb, apk, logPath));
            Debug.Log("[Capri] Sideload test started. Downloading from GitHub...");
        }

        // Installs the APK from "2. Build release APK" (before it's on GitHub), then opens it on the headset.
        [MenuItem("Capri/Release/Install built APK on headset")]
        public static void InstallLocal()
        {
            string adb = FindAdb();
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string apk = Path.Combine(root, "Builds", "CapriRoom-v" + CapriRelease.Version + ".apk");
            string logPath = Path.Combine(root, "Logs", "install-local.txt");
            var log = new StringBuilder();
            log.AppendLine("apk: " + apk + (File.Exists(apk) ? "" : " (MISSING)"));
            if (adb != null && File.Exists(apk))
            {
                log.AppendLine("install:\n" + Adb(adb, $"install -r \"{apk}\""));
                log.AppendLine("after: " + Version(adb));
                Adb(adb, $"shell am force-stop {Package}");
                Adb(adb, $"shell monkey -p {Package} -c android.intent.category.LAUNCHER 1");
                log.AppendLine("launched");
            }
            Directory.CreateDirectory(Path.GetDirectoryName(logPath));
            File.WriteAllText(logPath, log.ToString());
            Debug.Log("[Capri] " + log.ToString().Replace("\n", " | "));
        }

        private static async Task Test(string adb, string apk, string logPath)
        {
            var log = new StringBuilder();
            void Say(string s) { log.AppendLine(s); Debug.Log("[Capri] " + s); File.WriteAllText(logPath, log.ToString()); }
            try
            {
                Say("Started " + DateTime.Now);
                Say("adb: " + (adb ?? "NOT FOUND"));
                if (adb == null) { Say("RESULT: FAIL (adb not found)"); return; }

                using (var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) })
                {
                    http.DefaultRequestHeaders.UserAgent.ParseAdd("CapriRoom-SideloadTest");
                    var bytes = await http.GetByteArrayAsync(Url);
                    Directory.CreateDirectory(Path.GetDirectoryName(apk));
                    File.WriteAllBytes(apk, bytes);
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        Say($"Downloaded {bytes.Length} bytes, sha256 {BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant()}");
                }

                Say("devices:\n" + Adb(adb, "devices -l"));
                Say("model: " + Adb(adb, "shell getprop ro.product.model").Trim());
                Say("before: " + Version(adb));
                Say("install:\n" + Adb(adb, $"install -r \"{apk}\""));
                string after = Version(adb);
                Say("after: " + after);
                Say("launch:\n" + Adb(adb, $"shell monkey -p {Package} -c android.intent.category.LAUNCHER 1"));
                Say(after.Contains("versionName=" + CapriRelease.Version) ? "RESULT: PASS" : "RESULT: FAIL");
            }
            catch (Exception e) { Say("RESULT: FAIL " + e.GetType().Name + ": " + e.Message); }
        }

        private static string Version(string adb)
        {
            var dump = Adb(adb, $"shell dumpsys package {Package}");
            var sb = new StringBuilder();
            foreach (var line in dump.Split('\n'))
                if (line.Contains("versionName=") || line.Contains("lastUpdateTime=")) sb.Append(line.Trim()).Append("  ");
            return sb.Length > 0 ? sb.ToString() : "not installed";
        }

        private static string Adb(string adb, string args)
        {
            var start = new ProcessStartInfo(adb, args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var p = Process.Start(start))
            {
                string o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
                p.WaitForExit(180000);
                return o;
            }
        }

        private static string FindAdb()
        {
            string sdk = null;
            try { sdk = UnityEditor.Android.AndroidExternalToolsSettings.sdkRootPath; } catch { }
            var candidates = new[]
            {
                sdk == null ? null : Path.Combine(sdk, "platform-tools", "adb.exe"),
                Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer", "SDK", "platform-tools", "adb.exe"),
            };
            foreach (var c in candidates) if (c != null && File.Exists(c)) return c;
            return null;
        }
    }
}
