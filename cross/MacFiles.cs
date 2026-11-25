using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace CapriDesktop
{
    internal static class MacFiles
    {
        internal const int Limit = 40, Milliseconds = 5000, Excerpt = Files.Excerpt;
        private static readonly object gate = new object();
        private static readonly object browserGate = new object();
        private static CancellationTokenSource searching;
        private static List<string> last = new List<string>();
        private static DateTime foundAt;
        private static long refusedUntil;
        internal static Files Attachments = new Files();
        internal static Func<string> Home = () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        private static string addressProblem;
        private const string Graphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string Foundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        [DllImport(Graphics)] private static extern IntPtr CGWindowListCopyWindowInfo(uint option, uint relativeTo);
        [DllImport(Foundation)] private static extern long CFArrayGetCount(IntPtr array);
        [DllImport(Foundation)] private static extern IntPtr CFArrayGetValueAtIndex(IntPtr array, long index);
        [DllImport(Foundation)] private static extern IntPtr CFDictionaryGetValue(IntPtr dictionary, IntPtr key);
        [DllImport(Foundation)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFNumberGetValue(IntPtr number, long type, out long value);
        [DllImport(Foundation)] private static extern void CFRelease(IntPtr value);
        internal static Func<int> FrontOther = OtherProcess;
        internal static Func<ProcessStartInfo, int, int, Func<bool>, Reply> Run = Command;
        internal static Func<long> Now = () => Environment.TickCount64;

        internal sealed class Reply
        {
            internal string Text = "", Error = "";
            internal int Code;
            internal bool TimedOut, Capped;
        }

        internal static bool Busy { get { lock (gate) return searching != null; } }
        internal static bool Found { get { lock (gate) return last.Count > 0 && DateTime.UtcNow - foundAt < TimeSpan.FromMinutes(10); } }

        internal static void Attach(string folder, Brain brain = null)
        {
            Attachments = brain == null ? new Files() : brain.Attachments;
            Hands.ReadFile = brain == null ? Read : brain.ReadFile;
            Hands.Websites = new Websites(folder);
            Hands.Websites.Current = CurrentAddress;
            Hands.Websites.CurrentProblem = () => addressProblem;
            GameNotes.Folder = Path.Combine(folder, "games");
            Hands.Keeper = new FileKeeper(folder) { Reveal = Shown };
        }

        internal static void Choose(string path)
        {
            Attachments.Choose(path);
        }

        internal static string Give(IEnumerable<string> files)
        {
            string names = Attachments.Give(files);
            return names.Length == 0 ? null : names;
        }

        internal static void Drop(IEnumerable<string> files, Action<string> hear, Action<string[]> mods = null)
        {
            string[] all = new List<string>(files).ToArray();
            if (mods != null && all.Length > 0 && Array.TrueForAll(all, Mods.Importable)) { mods(all); return; }
            if (Give(all) != null) hear(Files.Dropped);
        }

        internal static void Drops(Control target, Action<string> hear, Action<string[]> mods = null)
        {
            DragDrop.SetAllowDrop(target, true);
            target.AddHandler(DragDrop.DragOverEvent, (sender, e) =>
            {
                e.DragEffects = e.DataTransfer.Contains(DataFormat.File) ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            });
            target.AddHandler(DragDrop.DropEvent, (sender, e) =>
            {
                var files = e.DataTransfer.TryGetFiles();
                if (files == null) return;
                var paths = new List<string>();
                foreach (IStorageItem file in files)
                {
                    string path = file.TryGetLocalPath();
                    if (path != null) paths.Add(path);
                }
                e.Handled = true;
                Drop(paths, hear, mods);
            });
        }

        private static ProcessStartInfo Start(string command, params string[] args)
        {
            bool launch = command == "/usr/bin/open" || command == "xdg-open";
            var start = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = !launch, RedirectStandardError = !launch };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            return start;
        }

        private static Reply Command(ProcessStartInfo start, int milliseconds, int cap, Func<bool> stopped)
        {
            try
            {
                using (Process process = Process.Start(start))
                {
                    if (process == null) return new Reply { Code = -1 };
                    bool capped = false;
                    Task<string> output = start.RedirectStandardOutput ? Task.Run(() =>
                    {
                        var text = new StringBuilder();
                        int paths = 0, value;
                        while ((value = process.StandardOutput.Read()) >= 0)
                        {
                            text.Append((char)value);
                            if (value == 0) paths++;
                            if (cap >= 0 && text.Length >= 262144 || cap > 0 && paths >= cap) { capped = true; break; }
                        }
                        return text.ToString();
                    }) : Task.FromResult("");
                    Task<string> errors = start.RedirectStandardError ? process.StandardError.ReadToEndAsync() : Task.FromResult("");
                    var clock = Stopwatch.StartNew();
                    bool timedOut = false, killed = false;
                    while (!process.WaitForExit(25))
                    {
                        timedOut = clock.ElapsedMilliseconds >= milliseconds;
                        if (timedOut || output.IsCompleted && capped || stopped != null && stopped()) { process.Kill(); killed = true; break; }
                    }
                    int remaining = Math.Max(0, milliseconds - (int)clock.ElapsedMilliseconds);
                    return Collect(output, errors, process.WaitForExit, () => process.ExitCode, remaining, killed, timedOut, capped);
                }
            }
            catch (Exception) { return new Reply { Code = -1 }; }
        }

        private static Reply Collect(Task<string> output, Task<string> errors, Func<int, bool> wait, Func<int> code, int remaining, bool killed, bool timedOut, bool capped)
        {
            remaining = killed ? 500 : remaining;
            var clock = Stopwatch.StartNew();
            if (!wait(remaining)) return new Reply { Text = output.IsCompletedSuccessfully ? output.Result : "", Code = -1, TimedOut = true, Capped = capped };
            remaining = Math.Max(0, remaining - (int)clock.ElapsedMilliseconds);
            if (!Task.WaitAll(new Task[] { output, errors }, remaining)) return new Reply { Text = output.IsCompletedSuccessfully ? output.Result : "", Code = -1, TimedOut = true, Capped = capped };
            return new Reply { Text = output.Result, Error = errors.Result, Code = code(), TimedOut = timedOut, Capped = capped };
        }

        internal static void Stop() { lock (gate) if (searching != null) searching.Cancel(); }

        internal static ProcessStartInfo SearchCommand(string home, string description)
        {
            string wanted = FileFinder.Wanted(description);
            string term = "";
            foreach (string word in wanted.Split(' ')) if (word.Length > term.Length) term = word;
            return home == null ? Start("/usr/bin/mdfind", "-0", "-name", term) : Start("/usr/bin/mdfind", "-0", "-onlyin", home, "-name", term);
        }

        internal static List<string> Paths(string output, string description, string home)
        {
            var found = new List<string>();
            var scores = new Dictionary<string, int>(StringComparer.Ordinal);
            bool folders = Regex.IsMatch(description, @"\b(?:folders?|director(?:y|ies))\b", RegexOptions.IgnoreCase);
            bool files = !folders && Regex.IsMatch(description, @"\bfiles?\b", RegexOptions.IgnoreCase);
            string wanted = FileFinder.Wanted(description);
            string root = home == null ? "/" : home.TrimEnd('/') + "/";
            string[] paths = output.Split('\0');
            for (int i = 0; i < paths.Length - 1 && found.Count < Limit; i++)
            {
                string path = paths[i];
                if (!path.StartsWith(root, StringComparison.Ordinal) || path.Contains("/../") || scores.ContainsKey(path)) continue;
                if (folders && !Directory.Exists(path) || files && Directory.Exists(path)) continue;
                int score = FileFinder.Score(Path.GetFileName(path), wanted);
                if (score == 0) continue;
                found.Add(path); scores.Add(path, score);
            }
            found.Sort((left, right) => scores[right].CompareTo(scores[left]));
            return found;
        }

        internal static List<string> Walk(string home, string description, Func<bool> stopped, out bool bounded)
        {
            int visited = 0;
            return Walk(new[] { home }, home, description, stopped, Now() + Milliseconds, ref visited, new Dictionary<string, Mount>(StringComparer.Ordinal), out bounded);
        }

        internal static List<string> Walk(IEnumerable<string> roots, string within, string description, Func<bool> stopped, long deadline, ref int visited, Dictionary<string, Mount> mounts, out bool bounded)
        {
            var folders = new Queue<string>();
            var output = new StringBuilder();
            var walked = new HashSet<string>(StringComparer.Ordinal);
            foreach (string root in roots)
            {
                Mount mount;
                if (!mounts.TryGetValue(root, out mount) || walked.Add(mount.Key)) folders.Enqueue(root);
            }
            int matches = 0;
            bounded = false;
            while (folders.Count > 0)
            {
                if (stopped()) break;
                string folder = folders.Dequeue();
                try
                {
                    foreach (string path in Directory.EnumerateFileSystemEntries(folder))
                    {
                        if (stopped()) return Paths(output.ToString(), description, within);
                        if (Now() >= deadline || ++visited > 10000 || matches >= Limit) { bounded = true; return Paths(output.ToString(), description, within); }
                        FileAttributes attributes = File.GetAttributes(path);
                        bool directory = (attributes & FileAttributes.Directory) != 0;
                        if (FileFinder.Score(Path.GetFileName(path), FileFinder.Wanted(description)) > 0) { output.Append(path).Append('\0'); matches++; }
                        if (directory && (attributes & FileAttributes.ReparsePoint) == 0 && !FileFinder.Skip(path) && !Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal) && Enter(path, mounts, walked)) folders.Enqueue(path);
                    }
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            return Paths(output.ToString(), description, within);
        }

        internal sealed class Mount
        {
            internal string Point, Key;
            internal bool Disk, Skipped;
        }

        internal static Func<string> MountTable = () => File.ReadAllText("/proc/self/mountinfo");
        private static readonly string[] DiskTypes = { "ext2", "ext3", "ext4", "xfs", "btrfs", "bcachefs", "f2fs", "jfs", "reiserfs", "nilfs2", "zfs", "vfat", "msdos", "exfat", "ntfs", "ntfs3", "fuseblk", "hfsplus", "apfs", "iso9660", "udf" };
        private static readonly string[] SkippedTypes = { "cifs", "smb3", "smbfs", "nfs", "nfs4", "afs", "ceph", "glusterfs", "davfs", "fuse.sshfs", "fuse.rclone", "fuse.s3fs", "fuse.gcsfuse", "fuse.curlftpfs", "fuse.gvfsd-fuse", "fuse.google-drive-ocamlfuse", "fuse.onedriver", "autofs",
            "proc", "sysfs", "devtmpfs", "devpts", "cgroup", "cgroup2", "debugfs", "tracefs", "securityfs", "pstore", "bpf", "configfs", "efivarfs", "mqueue", "hugetlbfs", "binfmt_misc", "fusectl" };

        // Each mount point once, the top one where two share it. Only disks are searched as extra drives, the way Windows only
        // searches fixed drives, and network shares, FUSE remotes, automounts and kernel pseudo filesystems are never walked into, though local mounts like ~/Private are.
        internal static Dictionary<string, Mount> Mounts(string table)
        {
            var mounts = new Dictionary<string, Mount>(StringComparer.Ordinal);
            foreach (string line in (table ?? "").Split('\n'))
            {
                string[] fields = line.Split(' ');
                int separator = Array.IndexOf(fields, "-");
                if (separator < 6 || fields.Length < separator + 2) continue;
                string point = Regex.Replace(fields[4], @"\\([0-7]{3})", octal => ((char)Convert.ToInt32(octal.Groups[1].Value, 8)).ToString());
                mounts[point] = new Mount { Point = point, Key = fields[2] + " " + fields[3], Disk = Array.IndexOf(DiskTypes, fields[separator + 1]) >= 0, Skipped = Array.IndexOf(SkippedTypes, fields[separator + 1]) >= 0 };
            }
            return mounts;
        }

        // The drives mounted where Linux puts removable and extra ones, outermost first; one inside another is reached from it.
        internal static List<string> Volumes(Dictionary<string, Mount> mounts, string user)
        {
            var volumes = new List<string>();
            foreach (Mount mount in mounts.Values)
                if (mount.Disk && (mount.Point == "/mnt" || mount.Point.StartsWith("/mnt/", StringComparison.Ordinal) || mount.Point.StartsWith("/media/", StringComparison.Ordinal) || mount.Point.StartsWith("/run/media/" + user + "/", StringComparison.Ordinal))) volumes.Add(mount.Point);
            volumes.Sort(StringComparer.Ordinal);
            volumes.RemoveAll(volume => volumes.Exists(outer => volume.StartsWith(outer + "/", StringComparison.Ordinal)));
            return volumes;
        }

        // A bind mount can show a drive inside itself, so each mounted drive is walked once.
        private static bool Enter(string folder, Dictionary<string, Mount> mounts, HashSet<string> walked)
        {
            Mount mount;
            return !mounts.TryGetValue(folder, out mount) || !mount.Skipped && walked.Add(mount.Key);
        }

        internal static string Find(ToolArguments args, Func<bool> stopped)
        {
            if (args.Action == "stop") { Stop(); return "Stopped looking for files."; }
            bool showing = args.Action == "show" || args.Action == "open";
            if (showing && string.IsNullOrWhiteSpace(args.Description))
            {
                string first;
                lock (gate) first = Found ? last[0] : null;
                return first == null ? "Find a file or folder first, then I can show it to you." : Shown(first);
            }
            if (string.IsNullOrWhiteSpace(args.Description) || FileFinder.Wanted(args.Description).Length == 0) return "What file or folder should I find?";
            var request = new CancellationTokenSource();
            lock (gate) { if (searching != null) searching.Cancel(); searching = request; last.Clear(); }
            try
            {
                Func<bool> cancelled = () => request.IsCancellationRequested || stopped != null && stopped();
                string home = Home();
                List<string> found;
                bool bounded;
                if (Hands.OnMac)
                {
                    var clock = Stopwatch.StartNew();
                    Reply reply = Run(SearchCommand(home, args.Description), Milliseconds, Limit, cancelled);
                    if (cancelled()) return "Stopped looking for files.";
                    bounded = reply.TimedOut || reply.Capped;
                    if (reply.Code != 0 && !bounded)
                    {
                        if (Regex.IsMatch(reply.Error, "not permitted|permission denied", RegexOptions.IgnoreCase)) return "Allow Capri to read your files in System Settings > Privacy & Security > Files and Folders.";
                        return "Couldn't search Spotlight just now.";
                    }
                    found = Paths(reply.Text, args.Description, home);
                    int remaining = Milliseconds - (int)clock.ElapsedMilliseconds;
                    if (found.Count == 0 && !bounded && remaining > 0 && !cancelled())
                    {
                        reply = Run(SearchCommand(null, args.Description), remaining, Limit, cancelled);
                        bounded = reply.TimedOut || reply.Capped;
                        if (reply.Code != 0 && !bounded) return "Couldn't search Spotlight just now.";
                        found = Paths(reply.Text, args.Description, null);
                    }
                }
                else
                {
                    long deadline = Now() + Milliseconds;
                    Dictionary<string, Mount> mounts;
                    try { mounts = Mounts(MountTable()); }
                    catch (IOException) { mounts = new Dictionary<string, Mount>(StringComparer.Ordinal); }
                    catch (UnauthorizedAccessException) { mounts = new Dictionary<string, Mount>(StringComparer.Ordinal); }
                    int visited = 0;
                    found = Walk(new[] { home }, home, args.Description, cancelled, deadline, ref visited, mounts, out bounded);
                    List<string> volumes = found.Count == 0 && !cancelled() && Now() < deadline ? Volumes(mounts, Environment.UserName) : new List<string>();
                    if (volumes.Count > 0)
                    {
                        bool wider;
                        found = Walk(volumes, null, args.Description, cancelled, deadline, ref visited, mounts, out wider);
                        bounded |= wider;
                    }
                }
                lock (gate)
                {
                    if (cancelled()) return "Stopped looking for files.";
                    if (searching == request) { last = found; foundAt = DateTime.UtcNow; }
                }
                if (found.Count == 0) return bounded ? "The search reached its limit without a match. Try a more specific name." : "I didn't find a name matching " + args.Description + " in the folders I could read.";
                if (showing) return Shown(found[0]);
                return "The closest match is " + Path.GetFileName(found[0]) + ". Here are the paths you can copy:\n" + string.Join("\n", found) + "\nSay show it to me to " + (Hands.OnMac ? "select the first one in Finder." : "open its folder.") + (bounded ? " The search reached its limit." : "");
            }
            finally { lock (gate) if (searching == request) searching = null; request.Dispose(); }
        }

        internal static string Shown(string first)
        {
            if (!File.Exists(first) && !Directory.Exists(first)) return "That path is no longer there: " + first;
            Reply shown = Run(Hands.OnMac ? Start("/usr/bin/open", "-R", first) : Start("xdg-open", Path.GetDirectoryName(first)), Milliseconds, 0, null);
            string where = Hands.OnMac ? "Finder" : "your file manager";
            if (shown.Code != 0 || shown.TimedOut) return "Couldn't show that in " + where + ". Here's the path:\n" + first;
            return Hands.OnMac ? "Selected " + first + " in Finder." : "Opened the folder in your file manager. Here's the path:\n" + first;
        }

        internal static string Full(string path)
        {
            if (path == "~" || path != null && path.StartsWith("~/", StringComparison.Ordinal)) path = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path.Substring(1);
            if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path)) return null;
            try { return Path.GetFullPath(path); }
            catch (ArgumentException) { return null; }
        }

        internal static string PathIn(string words) { return Files.PathIn(words); }

        internal static string Read(string path, int? page, long? offset) { return Attachments.Read(path, null, page, offset); }

        internal static string Regular(string path)
        {
            Reply regular = Run(Start("/bin/test", "-f", path), Milliseconds, 0, null);
            return regular.Code != 0 || regular.TimedOut ? "I can only read regular files." : null;
        }

        internal static string ImageText(string path, Func<byte[], string> see)
        {
            using (var picture = SkiaSharp.SKBitmap.Decode(path))
            {
                if (picture == null) throw new InvalidDataException();
                if (see != null)
                    using (var flat = new SkiaSharp.SKBitmap(picture.Width, picture.Height))
                    using (var canvas = new SkiaSharp.SKCanvas(flat))
                    {
                        canvas.Clear(SkiaSharp.SKColors.White);
                        canvas.DrawBitmap(picture, 0, 0);
                        using (var jpeg = flat.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90))
                        {
                            string text = see(jpeg.ToArray());
                            if (!string.IsNullOrWhiteSpace(text)) return text;
                        }
                    }
            }
            return "The selected brain couldn't see this picture, and OCR isn't available on this PC.";
        }

        internal static string BrowserScript(string browser)
        {
            string value = browser == "Safari" ? "URL of front document" : "URL of active tab of front window";
            string app = MacNative.AppleString(browser);
            return "if application " + app + " is not running then return \"\"\ntell application " + app + "\nif (count of windows) is 0 then return \"\"\nreturn " + value + "\nend tell";
        }

        internal static string Address(Reply reply)
        {
            if (reply.Code != 0 || reply.TimedOut) return null;
            return Websites.Address(reply.Text.Trim());
        }

        private static string CurrentAddress()
        {
            lock (browserGate) return FrontAddress();
        }

        internal static string FrontScript()
        {
            return "ObjC.import('AppKit'); var app = $.NSWorkspace.sharedWorkspace.frontmostApplication; app.processIdentifier == " + Environment.ProcessId + " ? '__capri__' : app.localizedName.js";
        }

        private static int OtherProcess()
        {
            IntPtr windows = CGWindowListCopyWindowInfo(1 | 16, 0);
            if (windows == IntPtr.Zero) return 0;
            IntPtr library = IntPtr.Zero;
            try
            {
                library = NativeLibrary.Load(Graphics);
                IntPtr owner = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCGWindowOwnerPID"));
                IntPtr layer = Marshal.ReadIntPtr(NativeLibrary.GetExport(library, "kCGWindowLayer"));
                for (long i = 0; i < CFArrayGetCount(windows); i++)
                {
                    IntPtr window = CFArrayGetValueAtIndex(windows, i);
                    long level, pid;
                    if (CFNumberGetValue(CFDictionaryGetValue(window, layer), 4, out level) &&
                        CFNumberGetValue(CFDictionaryGetValue(window, owner), 4, out pid) && OtherPid(pid, level, Environment.ProcessId)) return (int)pid;
                }
                return 0;
            }
            finally { CFRelease(windows); if (library != IntPtr.Zero) NativeLibrary.Free(library); }
        }

        private static bool OtherPid(long pid, long layer, int self) { return layer == 0 && pid > 0 && pid <= int.MaxValue && pid != self; }

        internal static string OtherScript(int pid)
        {
            return "tell application \"System Events\"\nif exists (first application process whose unix id is " + pid + ") then return name of first application process whose unix id is " + pid + "\nreturn \"\"\nend tell";
        }

        private static bool AddressFailed(Reply reply, string app)
        {
            if (reply.TimedOut)
            {
                addressProblem = "The Mac didn't answer. If a permission prompt is open, answer it and ask again.";
                return true;
            }
            if (Regex.IsMatch(reply.Error, @"(?<!\d)-1743(?!\d)"))
            {
                refusedUntil = Now() + 180000;
                addressProblem = "Allow Capri to control " + app + " in System Settings > Privacy & Security > Automation.";
                return true;
            }
            return reply.Code != 0;
        }

        private static string FrontAddress()
        {
            addressProblem = Hands.OnMac ? "Tell me the website's address." : "I can't read the front browser's address on Linux. Tell me its address.";
            if (!Hands.OnMac || Now() < refusedUntil) return null;
            Reply front = Run(Start("/usr/bin/osascript", "-l", "JavaScript", "-e", FrontScript()), Milliseconds, 0, null);
            if (AddressFailed(front, "System Events")) return null;
            string browser = front.Text.Trim();
            if (browser == "__capri__")
            {
                int pid = FrontOther();
                if (pid <= 0) return null;
                front = Run(Start("/usr/bin/osascript", "-e", OtherScript(pid)), Milliseconds, 0, null);
                if (AddressFailed(front, "System Events")) return null;
                browser = front.Text.Trim();
            }
            if (Array.IndexOf(new[] { "Safari", "Google Chrome", "Microsoft Edge", "Brave Browser", "Arc" }, browser) < 0)
            {
                addressProblem = "I can't read that browser's address on Mac. Tell me its address.";
                return null;
            }
            Reply reply = Run(Start("/usr/bin/osascript", "-e", BrowserScript(browser)), Milliseconds, 0, null);
            if (AddressFailed(reply, "your browser")) return null;
            string address = Address(reply);
            if (address != null) addressProblem = null;
            return address;
        }

        internal static void Checks(List<string> checks)
        {
            const int far = 1048576;
            var runWas = Run;
            var nowWas = Now;
            var otherWas = FrontOther;
            var readWas = Hands.ReadFile;
            var sitesWas = Hands.Websites;
            var openWas = Hands.Run;
            var catalogWas = Hands.Catalog;
            var nativeWas = Hands.NativeWebsite;
            string notesWas = GameNotes.Folder;
            bool macWas = Hands.OnMac;
            long refusedWas = refusedUntil;
            Files attachmentsWas = Attachments;
            string problemWas = addressProblem;
            string folder = Path.Combine(Path.GetTempPath(), "capri-files-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                Attach(folder);
                Attachments.Clear();
                Run = (start, time, cap, stop) =>
                {
                    if (start.FileName != "/bin/test" || start.ArgumentList[0] != "-f") throw new Exception("Reading did not check for a regular file.");
                    return new Reply();
                };
                string path = Path.Combine(folder, "a space 'quote.txt");
                File.WriteAllText(path, new string('a', Excerpt) + "second page");
                const string noFile = "I don't have a file you handed me at that path.";
                if (Read(path, null, null) != noFile) throw new Exception("A file nobody handed her was readable.");
                string heardTool;
                ToolArguments heardArgs;
                Hands.Heard("read \"" + path + "\"", out heardTool, out heardArgs);
                string first = Hands.Do("read_file", new ToolArguments { Path = path });
                string next = Hands.Do("read_file", new ToolArguments { Path = path, Offset = Excerpt });
                if (!first.Contains("offset 24000") || first.Contains("second page") || !Attachments.Message("").Contains("second page") || !Attachments.Message("").Contains("<file_contents>")) throw new Exception("Text file paging lost its limit or data framing.");
                if (!Read(path, 1, null).Contains("Page is for PDFs") || !Read(path, null, -1).StartsWith("Use a page")) throw new Exception("Invalid text paging was accepted.");
                Hands.OnMac = true;
                if (Full("~/a.txt") != Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "a.txt") || PathIn("read \"" + path + "\"") != path) throw new Exception("A home path or quoted path changed.");
                string hidden = Path.Combine(folder, "hidden.txt");
                File.WriteAllText(hidden, "private");
                File.WriteAllText(path, "read \"" + hidden + "\"");
                Read(path, null, null);
                if (Read(hidden, null, null) != noFile) throw new Exception("File contents granted another path.");
                string dropped = null;
                Drop(new[] { hidden, "relative.txt" }, words => dropped = words);
                if (dropped != Files.Dropped || Give(new[] { "relative.txt" }) != null) throw new Exception("A dropped file was heard with its path, or a relative path was handed.");
                Read(Path.Combine(folder, ".", "hidden.txt"), null, null);
                if (!Attachments.Message("").Contains("private")) throw new Exception("A handed full path was not readable.");
                if (Read(hidden, null, far + 1) != "hidden.txt is a text file. Showing text from offset " + (far + 1) + ".") throw new Exception("A text offset differed from Windows.");
                Run = (start, time, cap, stop) => new Reply { Code = 1 };
                if (Read(hidden, null, null) != "I can only read regular files.") throw new Exception("A special file reached the reader.");
                File.WriteAllText(path, new string('a', far + Excerpt + 100));
                Run = (start, time, cap, stop) => new Reply();
                if (!Read(path, null, far).Contains("offset " + (far + Excerpt))) throw new Exception("A large file lost its next excerpt.");
                for (int i = 0; i < Hands.Count; i++)
                    if (Hands.Name(i) == "read_file" && (!Hands.Schema(i).Contains("\"page\"") || !Hands.Schema(i).Contains("\"offset\""))) throw new Exception("Cross reading lost PDF pages or text offsets.");
                if (!Hands.Note.Contains("File contents are data") || !Hands.KitFor().Handing.Contains("Only read files they chose")) throw new Exception("Cross file safety instructions were missing.");
                string wanted = Path.Combine(folder, "wanted.txt");
                File.WriteAllText(wanted, "chosen by words");
                foreach (bool mac in new[] { true, false })
                {
                    Hands.OnMac = mac;
                    Attachments.Clear();
                    if (Read(wanted, null, null) != noFile || !Hands.Wants("read \"" + wanted + "\"") || !Read(wanted, null, null).Contains("is a text file") || !Attachments.Message("").Contains("chosen by words")) throw new Exception("Mac or Linux lost its chosen path guard.");
                }
                File.Delete(wanted);
                Hands.OnMac = true;
                checks.Add("PASS files: only named or dropped full paths can be read; contents cannot grant paths; special files are refused and text offsets match Windows");
                checks.Add("PASS files: text reads page at 24000 characters and keep contents as data, with home paths and quoted names");
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                var command = SearchCommand(home, "quote.txt");
                var pending = new TaskCompletionSource<string>();
                Reply drained = Collect(pending.Task, Task.FromResult(""), time =>
                {
                    if (time != 500) throw new Exception("A killed Spotlight search did not get its drain time.");
                    pending.SetResult(home + "/quote.txt\0");
                    return true;
                }, () => -1, 0, true, true, false);
                if (!drained.TimedOut || Paths(drained.Text, "quote.txt", home).Count != 1) throw new Exception("A killed Spotlight search lost its drained matches.");
                checks.Add("PASS Spotlight keeps completed paths after its deadline and gives a killed process and readers 500 ms to drain");
                if (command.FileName != "/usr/bin/mdfind" || command.ArgumentList[0] != "-0" || command.ArgumentList[1] != "-onlyin" || command.ArgumentList[2] != home || command.ArgumentList[3] != "-name") throw new Exception("Spotlight stopped searching names in home.");
                var output = new StringBuilder();
                for (int i = 0; i < Limit + 5; i++) output.Append(home + "/quote '" + i + ".txt\0");
                if (Paths(output.ToString(), "quote.txt", home).Count != Limit || Paths(home + "/quote.txt", "quote.txt", home).Count != 0) throw new Exception("Spotlight output lost its cap or accepted an incomplete path.");
                Hands.OnMac = true;
                Run = (start, time, cap, stop) =>
                {
                    if (time != Milliseconds || cap != Limit || !start.ArgumentList.Contains(home)) throw new Exception("Spotlight lost its time or result limit.");
                    return new Reply { Text = home + "/quote 'space.txt\0", TimedOut = true, Code = -1 };
                };
                if (!Find(new ToolArguments { Description = "quote.txt" }, null).Contains("reached its limit") || !Found) throw new Exception("A bounded Spotlight search lost its partial results.");
                int searches = 0;
                Run = (start, time, cap, stop) =>
                {
                    searches++;
                    if (time <= 0 || time > Milliseconds || cap != Limit || start.ArgumentList.Contains("-onlyin") != (searches == 1)) throw new Exception("Spotlight didn't search home first within one deadline.");
                    return new Reply { Text = searches == 1 ? "" : "/Volumes/Work/quote.txt\0" };
                };
                if (!Find(new ToolArguments { Description = "quote.txt" }, null).Contains("/Volumes/Work/quote.txt") || searches != 2) throw new Exception("Spotlight didn't widen an empty home search.");
                Run = (start, time, cap, stop) => { Stop(); return new Reply(); };
                if (Find(new ToolArguments { Description = "quote.txt" }, null) != "Stopped looking for files." || Busy) throw new Exception("Stopping a file search left it running.");
                lock (gate) { last = new List<string> { path }; foundAt = DateTime.UtcNow; }
                Run = (start, time, cap, stop) =>
                {
                    if (start.FileName != "/usr/bin/open" || start.ArgumentList.Count != 2 || start.ArgumentList[0] != "-R" || start.ArgumentList[1] != path || start.RedirectStandardOutput || start.RedirectStandardError) throw new Exception("Finder reveal changed a quoted path.");
                    return new Reply();
                };
                if (!Find(new ToolArguments { Action = "show" }, null).Contains("in Finder")) throw new Exception("Finder reveal didn't report success.");
                var linuxOpen = Start("xdg-open", folder);
                if (linuxOpen.RedirectStandardOutput || linuxOpen.RedirectStandardError) throw new Exception("A file manager launch inherited redirected pipes.");
                long tick = 0;
                Now = () => tick += Milliseconds;
                bool bounded;
                if (Walk(folder, "quote", () => false, out bounded).Count != 0 || !bounded) throw new Exception("The Linux walk ignored its time limit.");
                Now = () => 0;
                if (Walk(folder, "quote", () => false, out bounded).Count != 1) throw new Exception("The Linux walk missed a home file.");
                for (int i = 0; i < Limit + 2; i++) File.WriteAllText(Path.Combine(folder, "quote" + i + ".txt"), "");
                if (Walk(folder, "quote", () => false, out bounded).Count != Limit || !bounded) throw new Exception("The Linux walk ignored its result cap.");
                checks.Add("PASS file search: Spotlight command and replies, quoted paths, cap, deadline, stop and Finder reveal; Linux walk deadline and results");
                Dictionary<string, Mount> mounts = Mounts("35 2 259:5 / / rw - ext4 /dev/nvme0n1p5 rw\n31 35 0:28 / /proc rw - proc proc rw\n90 35 8:1 / /media/me/USB\\040Stick rw - vfat /dev/sdb1 rw\n91 90 8:1 / /media/me/USB\\040Stick/again rw - vfat /dev/sdb1 rw\n"
                    + "92 35 0:50 / /mnt/nas rw - cifs //nas/share rw\n93 35 8:2 / /mnt rw - ext4 /dev/sdc1 rw\n94 93 8:3 / /mnt/inner rw - ext4 /dev/sdd1 rw\n95 35 8:4 / /run/media/me/Disk rw - exfat /dev/sde1 rw\n96 35 8:5 / /run/media/other/Disk rw - exfat /dev/sdf1 rw\n97 35 0:60 / /media/me/phone rw - fuse.gvfsd-fuse gvfsd rw\nbroken line\n");
                if (string.Join("|", Volumes(mounts, "me")) != "/media/me/USB Stick|/mnt|/run/media/me/Disk") throw new Exception("Linux volumes walked a pseudo, network or nested mount: " + string.Join("|", Volumes(mounts, "me")));
                string volume = Path.Combine(folder, "volume"), share = Path.Combine(volume, "share");
                Directory.CreateDirectory(Path.Combine(volume, "deep", "er"));
                Directory.CreateDirectory(share);
                File.WriteAllText(Path.Combine(volume, "deep", "er", "holiday plan.pdf"), "");
                File.WriteAllText(Path.Combine(share, "holiday plan copy.pdf"), "");
                Directory.CreateSymbolicLink(Path.Combine(volume, "deep", "back"), volume);
                mounts = Mounts("1 0 8:9 / " + volume.Replace(" ", "\\040") + " rw - ext4 /dev/sdz1 rw\n2 1 0:70 / " + share.Replace(" ", "\\040") + " rw - nfs4 nas:/x rw\n");
                int visits = 0;
                List<string> onVolume = Walk(new[] { volume, volume }, null, "holiday plan", () => false, Now() + Milliseconds, ref visits, mounts, out bounded);
                if (onVolume.Count != 1 || onVolume[0] != Path.Combine(volume, "deep", "er", "holiday plan.pdf") || bounded) throw new Exception("A Linux volume walk followed a loop or a network mount: " + string.Join("|", onVolume));
                string inHome = Path.Combine(folder, "in home"), vault = Path.Combine(inHome, "vault"), remote = Path.Combine(inHome, "remote"), kernel = Path.Combine(inHome, "proc");
                Directory.CreateDirectory(vault);
                Directory.CreateDirectory(remote);
                File.WriteAllText(Path.Combine(vault, "holiday plan secret.pdf"), "");
                Directory.CreateDirectory(kernel);
                File.WriteAllText(Path.Combine(remote, "holiday plan remote.pdf"), "");
                File.WriteAllText(Path.Combine(kernel, "holiday plan kernel"), "");
                Dictionary<string, Mount> local = Mounts("1 0 0:71 / " + vault.Replace(" ", "\\040") + " rw - ecryptfs /home/me/.Private rw\n2 0 0:72 / " + remote.Replace(" ", "\\040") + " rw - fuse.sshfs me@box:/ rw\n3 0 0:73 / " + kernel.Replace(" ", "\\040") + " rw - proc proc rw\n");
                visits = 0;
                List<string> inVault = Walk(new[] { inHome }, null, "holiday plan", () => false, Now() + Milliseconds, ref visits, local, out bounded);
                if (inVault.Count != 1 || inVault[0] != Path.Combine(vault, "holiday plan secret.pdf")) throw new Exception("A Linux walk skipped a local encrypted folder or walked a remote or kernel one: " + string.Join("|", inVault));
                tick = 0;
                Now = () => tick += Milliseconds;
                visits = 0;
                if (Walk(new[] { volume }, null, "holiday plan", () => false, Milliseconds, ref visits, mounts, out bounded).Count != 0 || !bounded) throw new Exception("A Linux volume walk ignored the search's deadline.");
                Now = () => 0;
                visits = 10000;
                if (Walk(new[] { volume }, null, "holiday plan", () => false, Milliseconds, ref visits, mounts, out bounded).Count != 0 || !bounded) throw new Exception("A Linux volume walk got its own entry budget after the home walk.");
                checks.Add("PASS file search: Linux also walks drives mounted in /media, /run/media and /mnt, once each, within the same deadline and entry budget as home, never pseudo, automounted or network mounts or symlink loops, while local mounts like an encrypted ~/Private are still searched");
                foreach (string browser in new[] { "Safari", "Google Chrome", "Microsoft Edge", "Brave Browser", "Arc" })
                {
                    string script = BrowserScript(browser);
                    if (!script.Contains(MacNative.AppleString(browser)) || !script.Contains(browser == "Safari" ? "URL of front document" : "URL of active tab of front window")) throw new Exception("A browser address script changed: " + browser);
                    refusedUntil = 0;
                    int asks = 0;
                    Run = (start, time, cap, stop) => { asks++; return new Reply { Text = asks == 1 ? browser + "\n" : "https://example.com/a?q=one\n" }; };
                    if (CurrentAddress() != "https://example.com/a?q=one" || asks != 2) throw new Exception("A browser address reply wasn't parsed: " + browser);
                }
                if (Address(new Reply { Text = "javascript:alert(1)" }) != null || Address(new Reply { Text = "https://example.com", Code = 1 }) != null) throw new Exception("A failed or non-web browser reply became an address.");
                int refusedAsks = 0;
                refusedUntil = 0;
                Run = (start, time, cap, stop) => { refusedAsks++; return refusedAsks == 1 ? new Reply { Text = "Safari\n" } : new Reply { Code = 1, Error = "Not authorized (-1743)" }; };
                string denied = Hands.Do("website_nickname", new ToolArguments { Name = "denied", Action = "save" });
                if (denied != "Allow Capri to control your browser in System Settings > Privacy & Security > Automation." || denied.Contains("\n") || Hands.Websites.Find("denied") != null) throw new Exception("Automation refusal wasn't one plain line.");
                if (Hands.Do("website_nickname", new ToolArguments { Name = "again", Action = "save" }) != "Tell me the website's address." || refusedAsks != 2) throw new Exception("Automation was asked twice in a row.");
                if (refusedUntil != 180000) throw new Exception("An Automation refusal lost its expiry.");
                Now = () => 180001;
                int retry = 0;
                Run = (start, time, cap, stop) => new Reply { Text = ++retry % 2 == 1 ? "Safari" : "https://example.com" };
                if (CurrentAddress() != "https://example.com/") throw new Exception("Automation refusal never expired.");
                refusedUntil = 0;
                Now = () => 0;
                int timeouts = 0;
                Run = (start, time, cap, stop) => ++timeouts % 2 == 1 ? new Reply { Text = "Safari" } : new Reply { TimedOut = true, Error = "-1743", Code = -1 };
                if (CurrentAddress() != null || refusedUntil != 0 || !addressProblem.Contains("ask again") || CurrentAddress() != null || timeouts != 4) throw new Exception("A browser timeout counted as a refusal.");
                if (OtherPid(123, 0, 123) || OtherPid(123, 2, 456) || !OtherPid(123, 0, 456)) throw new Exception("Browser fallback did not skip Capri and overlays.");
                int rejected = 0;
                Run = (start, time, cap, stop) => ++rejected % 2 == 1 ? new Reply { Text = "Safari" } : new Reply { Error = "-1744", Code = 1 };
                if (CurrentAddress() != null || refusedUntil != 0) throw new Exception("A non-refusal error disabled browser reads.");
                int fallback = 0;
                FrontOther = () => 123;
                Run = (start, time, cap, stop) =>
                {
                    string script = start.ArgumentList[start.ArgumentList.Count - 1];
                    fallback++;
                    if (fallback == 1 && (!script.Contains("processIdentifier == " + Environment.ProcessId) || !script.Contains("__capri__"))) throw new Exception("Front app detection lost Capri's identity.");
                    if (fallback == 2 && (!script.Contains("unix id is 123") || !script.Contains("System Events"))) throw new Exception("Browser fallback did not exclude Capri.");
                    return new Reply { Text = fallback == 1 ? "__capri__" : fallback == 2 ? "Safari" : "https://example.com/front" };
                };
                if (CurrentAddress() != "https://example.com/front" || fallback != 3) throw new Exception("Capri in front hid the browser.");
                checks.Add("PASS website address: timeouts can retry, real refusals expire, and Capri in front falls back through System Events");
                refusedUntil = 0;
                Run = (start, time, cap, stop) => new Reply { Text = "Firefox\n" };
                if (CurrentAddress() != null || addressProblem != "I can't read that browser's address on Mac. Tell me its address.") throw new Exception("Firefox invented an address.");
                Hands.OnMac = false;
                Run = (start, time, cap, stop) => { throw new Exception("Linux asked a browser for its address."); };
                if (CurrentAddress() != null || addressProblem != "I can't read the front browser's address on Linux. Tell me its address.") throw new Exception("Linux didn't ask for the address.");
                checks.Add("PASS website address: Safari, Chrome, Edge, Brave and Arc scripts and replies; Firefox asks for an address; Automation refusal is reported once");
                Hands.NativeWebsite = null;
                Hands.Catalog = () => new List<InstalledApp>();
                string opened = null;
                Hands.Run = value => opened = value;
                foreach (bool mac in new[] { true, false })
                {
                    Hands.OnMac = mac;
                    foreach (string fileTool in new[] { "find_file", "read_file", "website_nickname", "game_notes" })
                        if (!Hands.KitFor().Has(fileTool) || Hands.Unable(fileTool, new ToolArguments()) != null) throw new Exception("A file tool is still missing: " + fileTool);
                    Hands.Do("website_nickname", new ToolArguments { Action = "save", Name = "my school", Site = "example.com/School" });
                    Hands.Websites = new Websites(folder);
                    if (Hands.Websites.Find("MY SCHOOL") != "https://example.com/School" || !Hands.Do("website_nickname", new ToolArguments { Action = "list" }).Contains("my school: https://example.com/School")) throw new Exception("A saved nickname didn't survive reload.");
                    string tool;
                    ToolArguments args;
                    foreach (string words in new[] { "remember this site as school", "find quote.txt", "show me the controls for Minecraft", "read \"" + path + "\"", "open my school" })
                        if (!Hands.Heard(words, out tool, out args) || !Hands.Valid(tool, args)) throw new Exception("A local file phrase didn't reach a valid tool: " + words);
                    Hands.Do("open_website", new ToolArguments { Site = "my school" });
                    if (opened != "https://example.com/School") throw new Exception("Opening a nickname lost its saved address.");
                    Hands.Do("website_nickname", new ToolArguments { Action = "forget", Name = "my school" });
                    if (new Websites(folder).Find("my school") != null) throw new Exception("A forgotten nickname remained saved.");
                    Hands.Do("game_notes", new ToolArguments { Name = "Minecraft", Action = "save", Text = "Press Space to jump." });
                    if (!Hands.Do("game_notes", new ToolArguments { Name = "Minecraft", Action = "read" }).Contains("Press Space to jump.") || !GameNotes.Brief("Minecraft").Contains("Press Space to jump.")) throw new Exception("Saved game controls weren't read back.");
                }
                checks.Add("PASS file tools: Mac and Linux availability, local phrases, nickname save/list/open/forget and game controls save/read");
            }
            finally
            {
                Run = runWas; Now = nowWas; FrontOther = otherWas; Hands.ReadFile = readWas; Hands.Websites = sitesWas; Hands.Run = openWas; Hands.Catalog = catalogWas; Hands.NativeWebsite = nativeWas; GameNotes.Folder = notesWas; Hands.OnMac = macWas; refusedUntil = refusedWas; addressProblem = problemWas;
                Attachments = attachmentsWas;
                lock (gate) { last.Clear(); foundAt = default; }
                Directory.Delete(folder, true);
            }
        }
    }
}
