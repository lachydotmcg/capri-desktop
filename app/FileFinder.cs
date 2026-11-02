using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal sealed class FileMatch
    {
        internal string Path;
        internal int Score;
        internal int Order;
    }

    internal sealed class FileFinder
    {
        internal const string Description = "Find files or folders on this PC by name. Give the name in its original language, keeping any extension, without command words or a location. Returns whole paths, best match first. Use action show to select the first result in File Explorer, or stop to cancel a search. Searches names only, not file contents.";
        internal const string Schema = "{\"type\":\"object\",\"properties\":{\"description\":{\"type\":\"string\",\"description\":\"The file or folder name to match, in its original language; quote a literal name\"},\"action\":{\"type\":\"string\",\"description\":\"What to do; search by default\",\"enum\":[\"search\",\"show\",\"stop\"]}}}";
        private readonly object gate = new object();
        private CancellationTokenSource searching;
        private List<FileMatch> last = new List<FileMatch>();
        private DateTime foundAt;
        internal Func<IEnumerable<string>> Roots = Places;
        internal Func<IEnumerable<string>> Wider = Everywhere;
        internal Func<string, bool> Reveal = Show;
        internal bool Busy { get { lock (gate) return searching != null; } }
        internal bool Found { get { lock (gate) return last.Count > 0 && DateTime.UtcNow - foundAt < TimeSpan.FromMinutes(10); } }

        internal void Stop() { lock (gate) if (searching != null) searching.Cancel(); }

        internal string Do(ToolArguments args, Func<bool> stopped)
        {
            if (args.Action == "stop") { Stop(); return "Stopped looking for files."; }
            if (args.Action == "show")
            {
                string first;
                lock (gate) first = last.Count == 0 ? null : last[0].Path;
                if (first == null) return "Find a file or folder first, then I can show it to you.";
                if (!Directory.Exists(first) && !File.Exists(first)) return "That path is no longer there: " + first;
                try { if (Reveal(first)) return "Selected " + first + " in File Explorer."; }
                catch (Win32Exception) { }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                return "Couldn't select that in File Explorer. Here's the path:\n" + first;
            }
            if (string.IsNullOrWhiteSpace(args.Description)) return "What file or folder should I find?";
            CancellationTokenSource request = new CancellationTokenSource();
            lock (gate)
            {
                if (searching != null) searching.Cancel();
                searching = request;
                last.Clear();
            }
            try
            {
                Func<bool> cancelled = delegate { return request.IsCancellationRequested || (stopped != null && stopped()); };
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                List<FileMatch> found = Search(args.Description, Roots(), cancelled, seen);
                // Nothing where it's likely to be, so every fixed drive gets looked through, however long that takes.
                if (found.Count == 0 && !cancelled()) found = Search(args.Description, Wider(), cancelled, seen);
                lock (gate)
                {
                    if (cancelled()) return "Stopped looking for files.";
                    if (searching == request) { last = found; foundAt = DateTime.UtcNow; }
                }
                if (found.Count == 0) return "I didn't find a name matching " + args.Description + " in the folders I could read.";
                List<string> lines = new List<string>();
                foreach (FileMatch match in found) lines.Add(match.Path);
                return "The closest match is " + Path.GetFileName(found[0].Path) + ". Here are the paths you can copy:\n" + string.Join("\n", lines.ToArray()) + "\nSay show it to me to select the first one in File Explorer.";
            }
            finally
            {
                lock (gate) if (searching == request) searching = null;
                request.Dispose();
            }
        }

        internal static ToolArguments Request(string text, bool busy, bool found)
        {
            if (Regex.IsMatch(text, @"^(?:stop (?:looking|searching)(?: for (?:files|folders))?|cancel (?:the )?(?:file |folder )?search)$", RegexOptions.IgnoreCase) || busy && Regex.IsMatch(text, @"^(?:stop|cancel|never mind|nevermind)$", RegexOptions.IgnoreCase)) return new ToolArguments { Action = "stop" };
            if (found && Regex.IsMatch(text, @"^(?:show (?:it|that) to me|show me (?:it|that)|open (?:that|the) folder)$", RegexOptions.IgnoreCase)) return new ToolArguments { Action = "show" };
            Match path = Regex.Match(text, @"^(?:show|tell|give) me (?:the )?(?:path|location) (?:to|of|for)\s+(.+)$|^(?:find|locate|look for|search for)\s+(.+?)(\s+on (?:my|the) (?:pc|computer))?$|^where(?:'?s| is| are)\s+(.+)$", RegexOptions.IgnoreCase);
            if (!path.Success) return null;
            string description = path.Groups[1].Success ? path.Groups[1].Value : path.Groups[2].Success ? path.Groups[2].Value : path.Groups[4].Value;
            // "Search for cat videos" is for the web. Their own things, a file or folder, a file's extension or "on my PC" make it this PC.
            if (!path.Groups[1].Success && !path.Groups[3].Success && !Regex.IsMatch(description, @"\b(?:files?|folders?|director(?:y|ies))\b|\bmy\b.*\b(?:documents?|photos?|pictures?|videos?|downloads?)\b|\.(?:txt|md|pdf|docx?|xlsx?|csv|tsv|pptx?|odt|rtf|png|jpe?g|gif|webp|bmp|mp[34]|wav|flac|mkv|mov|avi|zip|7z|rar|exe|msi|json|xml|ini|log|cs|py|js|ts|html|css)\b", RegexOptions.IgnoreCase)) return null;
            return new ToolArguments { Description = description, Action = "search" };
        }

        private static string Plain(string text) { return Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim(); }

        private static readonly Regex Quoted = new Regex("(?:^|\\s)[\"'\u201c](?<name>.+?)[\"'\u201d](?=\\s|$)");

        // The words that name it. "My photos" keeps photos, because there's nothing else to go on.
        private static string Wanted(string description)
        {
            Match quoted = Quoted.Match(description ?? "");
            if (quoted.Success) return Plain(quoted.Groups["name"].Value);
            string words = Regex.Replace(description ?? "", @"\b(?:by\s+name|with\s+(?:the\s+)?name)\s+", "", RegexOptions.IgnoreCase);
            string plain = Regex.Replace(Plain(words), @"\b(?:the|my|a|an|some|please|called|named|for|of|to|on|pc|computer)\b", " ");
            string named = Regex.Replace(plain, @"\b(?:game|file|files|folder|folders|directory|directories|document|documents|photo|photos|picture|pictures|video|videos)\b", " ");
            return Regex.Replace(named.Trim().Length > 0 ? named : plain, @"\s+", " ").Trim();
        }

        private static int Score(string name, string wanted)
        {
            string actual = Plain(name);
            if (wanted.Length == 0 || actual.Length == 0) return 0;
            if (wanted == actual) return 1000;
            if (actual.Replace(" ", "") == wanted.Replace(" ", "")) return 950;
            string[] terms = wanted.Split(' ');
            int score = 0;
            foreach (string term in terms)
            {
                if ((" " + actual + " ").Contains(" " + term + " ")) score += 100;
                else if (term.Length >= 3 && actual.Contains(term)) score += 50;
                else return 0;
            }
            return 100 + score / terms.Length;
        }

        internal static bool Skip(string path)
        {
            string name = Path.GetFileName(path.TrimEnd('\\', '/'));
            if (Regex.IsMatch(path, @"[\\/]AppData[\\/]", RegexOptions.IgnoreCase) && Regex.IsMatch(name, @"^(?:Cache|Caches|Code Cache|GPUCache|INetCache|CacheStorage|Temp|CrashDumps|D3DSCache|GLCache|DXCache)$", RegexOptions.IgnoreCase)) return true;
            return Regex.IsMatch(name, @"^(?:Windows|WinSxS|node_modules|bower_components|\.git|\.svn|\.hg|\.cache|\.nuget|\.npm|\.pnpm-store|__pycache__|\.venv|venv|\$Recycle\.Bin|System Volume Information|Recovery|Config\.Msi)$", RegexOptions.IgnoreCase);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct FindData
        {
            internal uint Attributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME Created, Accessed, Written;
            internal uint SizeHigh, SizeLow, Tag, Reserved;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] internal string Name;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)] internal string ShortName;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindFirstFile(string path, out FindData data);
        [DllImport("kernel32.dll")] private static extern bool FindClose(IntPtr handle);

        private static uint ReparseTag(string path)
        {
            FindData data;
            IntPtr handle = FindFirstFile(path, out data);
            if (handle == new IntPtr(-1)) return 0;
            try { return data.Tag; }
            finally { FindClose(handle); }
        }

        // Cloud placeholders keep names below them; junctions and symlinks can loop back (john_sandevistan, B88).
        internal static bool Follow(FileAttributes attributes, uint tag)
        {
            return (attributes & FileAttributes.ReparsePoint) == 0 || (tag != 0 && (tag & 0x20000000) == 0);
        }

        // Check each likely place at the same depth, so a big Desktop can't hold up a name sitting in Downloads (B88).
        internal static List<FileMatch> Search(string description, IEnumerable<string> roots, Func<bool> stopped, HashSet<string> seen = null, Func<FileSystemInfo, bool> follow = null)
        {
            List<FileMatch> matches = new List<FileMatch>();
            if (seen == null) seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string kind = Quoted.Replace(description ?? "", " ");
            bool folders = Regex.IsMatch(kind, @"\b(?:folders?|directory|directories)\b", RegexOptions.IgnoreCase);
            bool files = !folders && Regex.IsMatch(kind, @"\bfiles?\b", RegexOptions.IgnoreCase);
            string wanted = Wanted(description);
            List<string> level = new List<string>();
            foreach (string root in roots)
            {
                if (stopped()) break;
                try
                {
                    if (string.IsNullOrWhiteSpace(root)) continue;
                    string start = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
                    if (start.EndsWith(":", StringComparison.Ordinal)) start += Path.DirectorySeparatorChar;
                    if (!Directory.Exists(start) || !seen.Add(start)) continue;
                    int found = files ? 0 : Score(Path.GetFileName(start), wanted);
                    if (found > 0) matches.Add(new FileMatch { Path = start, Score = found, Order = matches.Count });
                    else level.Add(start);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                catch (System.Security.SecurityException) { continue; }
                catch (ArgumentException) { continue; }
                catch (NotSupportedException) { continue; }
            }
            bool firstLevel = true;
            while (level.Count > 0 && (matches.Count == 0 || firstLevel) && !stopped())
            {
                firstLevel = false;
                List<string> deeper = new List<string>();
                foreach (string folder in level)
                {
                    if (stopped()) break;
                    try
                    {
                        foreach (FileSystemInfo entry in new DirectoryInfo(folder).EnumerateFileSystemInfos())
                        {
                            if (stopped()) break;
                            FileAttributes attributes = entry.Attributes;
                            bool directory = (attributes & FileAttributes.Directory) != 0;
                            if (directory && !seen.Add(entry.FullName)) continue;
                            int score = (folders ? directory : !files || !directory) ? Score(entry.Name, wanted) : 0;
                            if (score > 0) matches.Add(new FileMatch { Path = entry.FullName, Score = score, Order = matches.Count });
                            else if (directory && !Skip(entry.FullName) && (follow != null ? follow(entry) : Follow(attributes, (attributes & FileAttributes.ReparsePoint) == 0 ? 0 : ReparseTag(entry.FullName)))) deeper.Add(entry.FullName);
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (System.Security.SecurityException) { }
                    catch (ArgumentException) { }
                    catch (NotSupportedException) { }
                }
                level = deeper;
            }
            matches.Sort(delegate(FileMatch left, FileMatch right) { int ranked = right.Score.CompareTo(left.Score); return ranked != 0 ? ranked : left.Order.CompareTo(right.Order); });
            return matches;
        }

        [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);

        internal static string Known(string id, string fallback)
        {
            IntPtr value = IntPtr.Zero;
            try
            {
                Guid folder = new Guid(id);
                return SHGetKnownFolderPath(ref folder, 0, IntPtr.Zero, out value) == 0 ? Marshal.PtrToStringUni(value) : fallback;
            }
            catch (Exception) { return fallback; }
            finally { if (value != IntPtr.Zero) Marshal.FreeCoTaskMem(value); }
        }

        internal static IEnumerable<string> Steam(string folder)
        {
            List<string> libraries = new List<string>();
            if (string.IsNullOrEmpty(folder)) return libraries;
            libraries.Add(Path.Combine(folder, "steamapps", "common"));
            try
            {
                string manifest = Path.Combine(folder, "steamapps", "libraryfolders.vdf");
                if (File.Exists(manifest))
                    foreach (Match library in Regex.Matches(File.ReadAllText(manifest), @"""(?:path|\d+)""\s+""([^""]+)"""))
                    {
                        string location = library.Groups[1].Value.Replace(@"\\", @"\");
                        if (Path.IsPathRooted(location)) libraries.Add(Path.Combine(location, "steamapps", "common"));
                    }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return libraries;
        }

        [DataContract]
        private sealed class EpicInstall { [DataMember] internal string InstallLocation; }

        internal static IEnumerable<string> Epic(string folder)
        {
            List<string> installs = new List<string>();
            try
            {
                if (!Directory.Exists(folder)) return installs;
                foreach (string manifest in Directory.EnumerateFiles(folder, "*.item"))
                    try
                    {
                        using (FileStream stream = File.OpenRead(manifest))
                        {
                            EpicInstall item = (EpicInstall)new DataContractJsonSerializer(typeof(EpicInstall)).ReadObject(stream);
                            if (!string.IsNullOrWhiteSpace(item.InstallLocation) && Path.IsPathRooted(item.InstallLocation)) installs.Add(item.InstallLocation);
                        }
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                    catch (SerializationException) { }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return installs;
        }

        internal static IEnumerable<string> Places()
        {
            string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            yield return Known("374DE290-123F-4565-9164-39C4925E467B", Path.Combine(profile, "Downloads"));
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
            yield return Known("4C5C32FF-BB9D-43B0-BF7D-1CA5B1CCEA0D", Path.Combine(profile, "Saved Games"));
            foreach (string saved in Hands.GamePaths()) yield return saved;
            foreach (string library in Steam(Hands.SteamFolder())) yield return library;
            foreach (string install in Epic(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Epic\EpicGamesLauncher\Data\Manifests"))) yield return install;
            foreach (string driveRoot in Drives())
                foreach (string games in new[] { "Games", "SteamLibrary\\steamapps\\common", "Epic Games", "GOG Games", "Riot Games", "EA Games", "Origin Games", "XboxGames", "WildGames", "Ubisoft Game Launcher\\games" }) yield return Path.Combine(driveRoot, games);
            string programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), programs86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (string programRoot in new[] { programs, programs86 })
            {
                foreach (string library in Steam(Path.Combine(programRoot, "Steam"))) yield return library;
                foreach (string gameRoot in new[] { "Epic Games", "GOG Games", "GOG Galaxy\\Games", "EA Games", "Ubisoft\\Ubisoft Game Launcher\\games", "Rockstar Games", "Amazon Games\\Library", "WildGames" }) yield return Path.Combine(programRoot, gameRoot);
            }
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Roblox\Versions");
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        }

        private static List<string> Drives()
        {
            List<string> drives = new List<string>();
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                try { if (drive.DriveType == DriveType.Fixed && drive.IsReady) drives.Add(drive.RootDirectory.FullName); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            return drives;
        }

        internal static IEnumerable<string> Everywhere()
        {
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            foreach (string drive in Drives()) yield return drive;
        }

        internal static ProcessStartInfo Explorer(string path) { return new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true }; }

        [DllImport("ole32.dll")] private static extern int CoInitializeEx(IntPtr reserved, uint mode);
        [DllImport("ole32.dll")] private static extern void CoUninitialize();
        [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] private static extern int SHParseDisplayName(string name, IntPtr context, out IntPtr item, uint attributes, IntPtr foundAttributes);
        [DllImport("shell32.dll")] private static extern int SHOpenFolderAndSelectItems(IntPtr item, uint count, IntPtr children, uint flags);

        // The Shell says whether selecting it worked, which a launched Explorer can't (B88). If the Shell won't from this thread,
        // Explorer's own /select still opens the folder on it, the way it always did.
        private static bool Show(string path)
        {
            int initialized = CoInitializeEx(IntPtr.Zero, 0);
            IntPtr item = IntPtr.Zero;
            try
            {
                if (SHParseDisplayName(path, IntPtr.Zero, out item, 0, IntPtr.Zero) == 0 && item != IntPtr.Zero && SHOpenFolderAndSelectItems(item, 0, IntPtr.Zero, 0) == 0) return true;
            }
            finally
            {
                if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
                if (initialized >= 0) CoUninitialize();
            }
            using (Process.Start(Explorer(path))) { }
            return true;
        }

        internal static string Spoken(string text)
        {
            return Regex.Replace(text ?? "", @"(?m)^([a-zA-Z]):[\\/]([^\r\n]+)\r?$", delegate(Match path)
            {
                return path.Groups[1].Value.ToUpperInvariant() + " drive, " + path.Groups[2].Value.Replace("\\", ", ").Replace("/", ", ").Replace("_", " ").Replace(".", " dot ");
            });
        }
    }
}
