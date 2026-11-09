using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;

namespace CapriDesktop
{
    internal static class NativeAccount
    {
        internal static string Initialize(string folder)
        {
            string directory = Path.Combine(folder, "accounts-native");
            if (new DirectoryInfo(directory).LinkTarget != null) throw new IOException("The native account directory cannot be a link.");
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if ((File.GetUnixFileMode(directory) & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.OtherRead | UnixFileMode.OtherWrite)) != 0)
                throw new IOException("The native account directory must be private.");
            string marker = Path.Combine(directory, "native-owner-v1");
            if (new FileInfo(marker).LinkTarget != null) throw new IOException("The native owner marker cannot be a link.");
            if (!File.Exists(marker))
            {
                using FileStream output = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                output.Write(System.Text.Encoding.UTF8.GetBytes("Native Capri credential owner v1\n"));
                File.SetUnixFileMode(marker, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            else if (File.ReadAllText(marker) != "Native Capri credential owner v1\n") throw new IOException("Unknown native credential owner.");
            return directory;
        }

        internal static string[] Run(string python, string directory, bool connect, string model, Action<string> status, Action<Process> started, Func<bool> current)
        {
            using Process worker = Bridge.Start(python, "account_bridge.py", "account", directory);
            started?.Invoke(worker);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(connect ? 650 : 60));
            using CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(worker));
            try
            {
                worker.StandardInput.WriteLine(JsonSerializer.Serialize(new { directory, operation = connect ? "connect" : "refresh", model }));
                worker.StandardInput.Flush();
                string[] models = null;
                while (true)
                {
                    string line = Bridge.ReadLine(worker.StandardOutput, 32000);
                    if (line == null) break;
                    if (!current()) throw new OperationCanceledException();
                    using JsonDocument item = JsonDocument.Parse(line);
                    JsonElement value = item.RootElement;
                    switch (value.GetProperty("event").GetString())
                    {
                        case "browser":
                            if (!connect) throw new InvalidOperationException("A refresh cannot start sign-in.");
                            string url = value.GetProperty("url").GetString();
                            if (url.Length > 16000 || !Uri.TryCreate(url, UriKind.Absolute, out Uri uri) || uri.Scheme != "https" || uri.Host != "auth.openai.com" || uri.Port != 443 || uri.AbsolutePath != "/api/accounts/authorize" || uri.UserInfo.Length != 0)
                                throw new InvalidOperationException("Invalid sign-in destination.");
                            status?.Invoke("Opening official ChatGPT sign-in in your browser…");
                            OpenBrowser(url, current);
                            worker.StandardInput.WriteLine("{\"opened\":true}"); worker.StandardInput.Flush();
                            break;
                        case "status": status?.Invoke(value.GetProperty("message").GetString()); break;
                        case "done": models = value.GetProperty("models").EnumerateArray().Select(row => row.GetString()).ToArray(); break;
                        case "error": throw new BrainException(value.GetProperty("message").GetString());
                        default: throw new InvalidOperationException("Unexpected account event.");
                    }
                }
                worker.WaitForExit();
                if (!current() || timeout.IsCancellationRequested) throw new OperationCanceledException();
                if (worker.ExitCode != 0 || models == null || models.Length == 0) throw new BrainException("ChatGPT did not finish connecting.");
                return models;
            }
            finally { Bridge.Kill(worker); }
        }

        private static void OpenBrowser(string url, Func<bool> current)
        {
            bool mac = OperatingSystem.IsMacOS();
            ProcessStartInfo start = mac ? MacNative.Browser() : ComputerProcess.Start("/usr/bin/python3", new[] { Path.Combine(AppContext.BaseDirectory, "bridge/portal_open.py") });
            using Process browser = Process.Start(start);
            using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
            using CancellationTokenRegistration abort = timeout.Token.Register(() => Bridge.Kill(browser));
            var output = browser.StandardOutput.ReadToEndAsync();
            var error = browser.StandardError.ReadToEndAsync();
            browser.StandardInput.Write(mac ? "open location " + MacNative.AppleString(url) : url); browser.StandardInput.Close();
            while (!browser.WaitForExit(100))
                if (!current()) { Bridge.Kill(browser); throw new OperationCanceledException(); }
            if (!current()) throw new OperationCanceledException();
            if (browser.ExitCode != 0) throw new BrainException("The system browser did not accept the sign-in request.");
        }
    }
}
