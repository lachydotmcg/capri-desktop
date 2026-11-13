using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace CapriDesktop
{
    internal enum RemoteStoreResult { Missing, Read, Unreadable, Saved, WriteFailed }

    internal sealed class RemoteStore
    {
        private readonly object gate = new object();
        private readonly RemoteStoreFile file;
        internal string Problem { get; private set; }

        internal RemoteStore(string folder) : this(new RemoteDisk(folder)) { }

        internal RemoteStore(RemoteStoreFile file) { this.file = file; }

        internal RemoteStoreResult Load(out RemoteState state, out string relay)
        {
            lock (gate)
            {
                state = null;
                relay = null;
                Problem = null;
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
                    Problem = error.Message;
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
                    file.Save(JsonSerializer.Serialize(new { v = 1, host = relay, state = JsonSerializer.Deserialize<JsonElement>(json) }));
                    return RemoteStoreResult.Saved;
                }
                catch (Exception error)
                {
                    Problem = error.Message;
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
        private SafeFileHandle directory;
        private string temp;
        private static bool Mac { get { return OperatingSystem.IsMacOS(); } }
        private static int NoFollow { get { return Mac ? 0x100 : 0x20000; } }
        private static int CloseExec { get { return Mac ? 0x1000000 : 0x80000; } }
        private static int DirectoryFlag { get { return Mac ? 0x100000 : 0x10000; } }
        private static int NonBlock { get { return Mac ? 4 : 0x800; } }
        private const UnixFileMode Private = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
        [DllImport("libc", SetLastError = true)] private static extern int openat(int directory, string path, int flags, uint mode);
        [DllImport("libc", EntryPoint = "openat", SetLastError = true)] private static extern int createat(int directory, string path, int flags, IntPtr a, IntPtr b, IntPtr c, IntPtr d, IntPtr e, uint mode);
        [DllImport("libc", SetLastError = true)] private static extern int renameat(int from, string source, int to, string target);
        [DllImport("libc", SetLastError = true)] private static extern int unlinkat(int directory, string path, int flags);
        [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
        [DllImport("libc", SetLastError = true)] private static extern IntPtr acl_init(int count);
        [DllImport("libc", SetLastError = true)] private static extern IntPtr acl_get_fd(int fd);
        [DllImport("libc", SetLastError = true)] private static extern int acl_get_entry(IntPtr acl, int id, out IntPtr entry);
        [DllImport("libc", SetLastError = true)] private static extern int acl_set_fd(int fd, IntPtr acl);
        [DllImport("libc")] private static extern int acl_free(IntPtr acl);

        internal RemoteDisk(string folder) { this.folder = Path.GetFullPath(folder); }

        private static void PrivateAcl(SafeFileHandle handle, bool clear)
        {
            if (!Mac) return;
            IntPtr acl = clear ? acl_init(0) : acl_get_fd(handle.DangerousGetHandle().ToInt32());
            if (acl == IntPtr.Zero)
            {
                if (!clear && Marshal.GetLastPInvokeError() == 2) return;
                throw new IOException("Cannot check Remote file access.");
            }
            try
            {
                IntPtr entry;
                if (clear ? acl_set_fd(handle.DangerousGetHandle().ToInt32(), acl) != 0 : acl_get_entry(acl, 0, out entry) != -1 || Marshal.GetLastPInvokeError() != 22)
                    throw new IOException("The Remote file and folder must have no extra access rules.");
            }
            finally { acl_free(acl); }
        }

        private static SafeFileHandle Handle(int fd)
        {
            if (fd < 0)
            {
                int error = Marshal.GetLastPInvokeError();
                if (error == 2) throw new FileNotFoundException("Remote file was not found.");
                throw new IOException("Cannot open the Remote file (" + error + ").");
            }
            return new SafeFileHandle(new IntPtr(fd), true);
        }

        private SafeFileHandle Folder()
        {
            if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) throw new PlatformNotSupportedException("Remote files need macOS or Linux.");
            int flags = NoFollow | CloseExec | DirectoryFlag;
            SafeFileHandle current = Handle(open("/", flags));
            try
            {
                foreach (string part in folder.Split('/', StringSplitOptions.RemoveEmptyEntries))
                {
                    SafeFileHandle next = Handle(openat(current.DangerousGetHandle().ToInt32(), part, flags, 0));
                    current.Dispose();
                    current = next;
                }
                if ((File.GetUnixFileMode(current) & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0)
                    throw new IOException("The Remote folder must be writable only by you.");
                PrivateAcl(current, false);
                return current;
            }
            catch { current.Dispose(); throw; }
        }

        private static FileStream ReadAt(SafeFileHandle directory)
        {
            int fd = openat(directory.DangerousGetHandle().ToInt32(), Name, NoFollow | CloseExec | NonBlock, 0);
            if (fd < 0 && Marshal.GetLastPInvokeError() == 2) return null;
            SafeFileHandle handle = Handle(fd);
            try
            {
                if (File.GetUnixFileMode(handle) != Private) throw new IOException("The Remote file must be readable and writable only by you.");
                PrivateAcl(handle, false);
                FileStream input = new FileStream(handle, FileAccess.Read);
                if (!input.CanSeek) { input.Dispose(); throw new IOException("The Remote file must be a regular file."); }
                return input;
            }
            catch { handle.Dispose(); throw; }
        }

        internal override Stream Read()
        {
            SafeFileHandle held;
            try { held = Folder(); }
            catch (FileNotFoundException) { return null; }
            using (held) return ReadAt(held);
        }

        protected override Stream Begin()
        {
            directory = Folder();
            string name = ".remote-state-" + Guid.NewGuid().ToString("N");
            int flags = 1 | NoFollow | CloseExec | (Mac ? 0x200 | 0x800 : 0x40 | 0x80);
            int fd = directory.DangerousGetHandle().ToInt32();
            SafeFileHandle handle = Handle(Mac && RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? createat(fd, name, flags, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0x180)
                : openat(fd, name, flags, 0x180));
            temp = name;
            try
            {
                File.SetUnixFileMode(handle, Private);
                PrivateAcl(handle, true);
                return new FileStream(handle, FileAccess.Write);
            }
            catch { handle.Dispose(); throw; }
        }

        protected override void Commit()
        {
            using (FileStream previous = ReadAt(directory)) { }
            int fd = directory.DangerousGetHandle().ToInt32();
            if (renameat(fd, temp, fd, Name) != 0) throw new IOException("Cannot replace the Remote file.");
            temp = null;
            if (fsync(fd) != 0) throw new IOException("Cannot flush the Remote folder.");
        }

        protected override void Cancel()
        {
            try { if (temp != null && directory != null) unlinkat(directory.DangerousGetHandle().ToInt32(), temp, 0); }
            finally
            {
                temp = null;
                if (directory != null) directory.Dispose();
                directory = null;
            }
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
