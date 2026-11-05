// Capri → Debug → Headset report: reads Capri Room's permissions and recent log from a USB-connected Quest.
// Writes Logs/headset-report.txt. Read-only: it doesn't change anything on the headset.
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using Debug = UnityEngine.Debug;

namespace CapriRoom.EditorTools
{
    public static class CapriHeadsetReport
    {
        private const string Package = "com.capriroom.quest";

        [MenuItem("Capri/Debug/Headset report")]
        public static void Run()
        {
            string adb = FindAdb();
            string root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            string path = Path.Combine(root, "Logs", "headset-report.txt");
            var sb = new StringBuilder();
            if (adb == null) sb.AppendLine("adb not found");
            else
            {
                sb.AppendLine("== devices ==\n" + Adb(adb, "devices -l"));
                sb.AppendLine("== permissions ==");
                foreach (var line in Adb(adb, $"shell dumpsys package {Package}").Split('\n'))
                    if (line.Contains("permission") || line.Contains("granted=")) sb.AppendLine(line.TrimEnd());
                sb.AppendLine("== running ==\n" + Adb(adb, $"shell pidof {Package}"));
                sb.AppendLine("== log ==");
                foreach (var line in Adb(adb, "logcat -d -t 4000").Split('\n'))
                {
                    var l = line.ToLowerInvariant();
                    if (l.Contains("[capri]") || l.Contains("mruk") || l.Contains("scene") || l.Contains("spatial")
                        || l.Contains("use_scene") || l.Contains("capriroom") || l.Contains("exception"))
                        sb.AppendLine(line.TrimEnd());
                }
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, sb.ToString());
            Debug.Log("[Capri] Headset report written to " + path);
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
                p.WaitForExit(60000);
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
