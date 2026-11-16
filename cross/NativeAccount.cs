using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace CapriDesktop
{
    internal enum RemoteStoreResult { Missing, Read, Unreadable, Saved, WriteFailed }

    internal sealed class RemoteStore
    {
        private readonly object gate = new object();
        private readonly RemoteStoreFile file;
        internal string Problem { get; private set; }
        internal bool Damaged { get; private set; }

        internal RemoteStore(string folder) : this(new RemoteDisk(folder)) { }

        internal RemoteStore(RemoteStoreFile file) { this.file = file; }

        internal RemoteStoreResult Load(out RemoteState state, out string relay)
        {
            lock (gate)
            {
                state = null;
                relay = null;
                Problem = null;
                Damaged = false;
                try
                {
                    using Stream input = file.Read();
                    if (input == null) return RemoteStoreResult.Missing;
                    using StreamReader reader = new StreamReader(input, new UTF8Encoding(false, true), false);
                    using JsonDocument saved = JsonDocument.Parse(reader.ReadToEnd());
                    JsonElement record = saved.RootElement;
                    if (record.GetProperty("v").GetInt32() != 1) throw new FormatException("Unknown Remote file version.");
                    string host = record.GetProperty("host").GetString();
                    if (host == null || RemoteLink.RelayBase(host) != host) throw new FormatException("Invalid saved relay.");
                    RemoteState read = ReadState(record.GetProperty("state").GetRawText());
                    state = read;
                    relay = host;
                    return RemoteStoreResult.Read;
                }
                catch (Exception error)
                {
                    state = null;
                    relay = null;
                    Damaged = !(error is IOException || error is UnauthorizedAccessException);
                    Problem = !Damaged
                        ? "Cannot read the Remote file right now. Check access to the file and try again."
                        : "The Remote file is damaged.";
                    return RemoteStoreResult.Unreadable;
                }
            }
        }

        internal RemoteStoreResult Save(string host, RemoteState state)
        {
            lock (gate)
            {
                Problem = null;
                try
                {
                    string relay = RemoteLink.RelayBase(host);
                    if (relay == null || state == null) throw new FormatException("Invalid Remote state or relay.");
                    string json = state.ToJson();
                    ReadState(json);
                    StringBuilder record = new StringBuilder("{\"v\":1,\"host\":");
                    RemoteJson.Quote(record, relay);
                    record.Append(",\"state\":").Append(json).Append('}');
                    file.Save(record.ToString());
                    return RemoteStoreResult.Saved;
                }
                catch (Exception error)
                {
                    Problem = error is FormatException ? "The Remote state or relay is invalid."
                        : "Cannot save the Remote file right now. Check access to the folder and try again.";
                    return RemoteStoreResult.WriteFailed;
                }
            }
        }

        private static RemoteState ReadState(string json)
        {
            RemoteJson.Map map = RemoteJson.ParseMap(json);
            RemoteState state = RemoteState.FromJson(json);
            double version;
            if (state == null || !map.Number("v", out version) || version != 1 || !(map.Is("used", true) || map.Is("used", false))
                || map.Items("devices") == null || map.Items("devices").Count != state.Devices.Count)
                throw new FormatException("Damaged Remote state.");
            foreach (object item in map.Items("devices"))
            {
                RemoteJson.Map device = item as RemoteJson.Map;
                DateTime added;
                if (device == null || device.Text("name") == null || !DateTime.TryParse(device.Text("added"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out added)
                    || device.ContainsKey("from") && device.Text("from") == null
                    || device.ContainsKey("parent") && RemoteCrypto.FromB64u(device.Text("parent"), 16) == null)
                    throw new FormatException("Damaged saved phone.");
            }
            return state;
        }
    }

    internal abstract class RemoteStoreFile
    {
        internal abstract Stream Read();
        protected abstract Stream Begin();
        protected abstract void Commit();
        protected abstract void Cancel();

        internal void Save(string text)
        {
            try
            {
                using (Stream output = Begin())
                {
                    using (StreamWriter writer = new StreamWriter(output, new UTF8Encoding(false, true), 1024, true))
                    {
                        writer.Write(text);
                        writer.Flush();
                    }
                    if (output is FileStream disk) disk.Flush(true);
                    else output.Flush();
                }
                Commit();
            }
            finally { Cancel(); }
        }
    }

    internal sealed class RemoteDisk : RemoteStoreFile
    {
        internal const string Name = "remote-state";
        private readonly string folder;
        private string temp;
        private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;

        internal RemoteDisk(string folder) { this.folder = Path.GetFullPath(folder); }

        internal override Stream Read()
        {
            FileStream input;
            try { input = new FileStream(Path.Combine(folder, Name), FileMode.Open, FileAccess.Read, FileShare.Read); }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
            if (!OperatingSystem.IsWindows())
                try { File.SetUnixFileMode(input.SafeFileHandle, Private); }
                catch (Exception) { }
            return input;
        }

        protected override Stream Begin()
        {
            Directory.CreateDirectory(folder);
            try
            {
                foreach (string stale in Directory.GetFiles(folder, ".remote-state-*"))
                    try { File.Delete(stale); }
                    catch (Exception) { }
            }
            catch (Exception) { }
            string name = Path.Combine(folder, ".remote-state-" + Guid.NewGuid().ToString("N"));
            FileStreamOptions options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = Private;
            FileStream output = new FileStream(name, options);
            temp = name;
            if (!OperatingSystem.IsWindows())
                try { File.SetUnixFileMode(output.SafeFileHandle, Private); }
                catch (Exception) { }
            return output;
        }

        protected override void Commit()
        {
            File.Move(temp, Path.Combine(folder, Name), true);
            temp = null;
        }

        protected override void Cancel()
        {
            try { if (temp != null) File.Delete(temp); }
            catch (Exception) { }
            finally { temp = null; }
        }
    }

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
