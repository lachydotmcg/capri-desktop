using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class FileStep
    {
        [DataMember] public string From;
        [DataMember] public string To;
    }

    [DataContract]
    internal sealed class FileChange
    {
        [DataMember] public string Id;
        [DataMember] public string When;
        [DataMember] public string What;
        [DataMember] public List<FileStep> Steps;
        [DataMember] public bool Undone;
    }

    internal sealed class FileHit
    {
        internal string Path;
        internal string Kind;
        internal DateTime Changed;
        internal long Size;
        internal int Score;
        internal bool Folder, Inside;
    }

    internal sealed class FileKeeper
    {
        internal const string Description = "Work with their files and folders on this computer. find (the default) looks by words in the name, words written inside documents and text files, kind or date, like videos from last week, what they downloaded today or the PDF that mentions their lease, and numbers what it finds. Use those numbers as path to show one, move them into a folder or rename one. make_folder makes a folder; organise sorts a folder's loose files into folders by kind, month or year. You can't see inside videos, photos or music, so match them by name, kind and date and say so. Moving, renaming and sorting ask them first, never delete anything, and undo puts the last change back. Use find_file for a quick lookup of one name.";
        internal const string Schema = "{\"type\":\"object\",\"properties\":{\"action\":{\"type\":\"string\",\"description\":\"What to do; find by default\",\"enum\":[\"find\",\"show\",\"make_folder\",\"move\",\"rename\",\"organise\",\"undo\"]},\"name\":{\"type\":\"string\",\"description\":\"find: words from the file or folder name. rename: the new name. make_folder: the new folder's name\"},\"search\":{\"type\":\"string\",\"description\":\"find: words written inside the file, for documents and text files\"},\"kind\":{\"type\":\"string\",\"description\":\"find: the kind of thing to look for\",\"enum\":[\"any\",\"video\",\"photo\",\"music\",\"document\",\"archive\",\"app\",\"code\",\"folder\"]},\"when\":{\"type\":\"string\",\"description\":\"find: when it was made or changed, like today, yesterday, last week, this month, 2026-11-03, 2026-11 or 2026-11-01 to 2026-11-07\"},\"folder\":{\"type\":\"string\",\"description\":\"find: where to look. make_folder: where to make it. organise: the folder to sort. A whole path or a place like Downloads, Desktop, Documents, Pictures, Videos or Music\"},\"path\":{\"type\":\"string\",\"description\":\"show, move or rename: numbers from the last find like 1, 3 or all, or whole paths, one per line\"},\"target\":{\"type\":\"string\",\"description\":\"move: the folder to move them into, as a whole path, a place like Pictures, or a new folder's name to make beside them\"},\"by\":{\"type\":\"string\",\"description\":\"organise: how to group its loose files\",\"enum\":[\"kind\",\"month\",\"year\"]}}}";
        internal const int Shown = 15, MostMoved = 300, Kept = 30;
        internal const string UndoLine = " Say undo that to put them back.";
        private const int Visits = 200000, Reads = 400;
        private const long Readable = 20L << 20, TextBytes = 1L << 20;

        internal static Action<FileChange> Changed;
        internal Func<IEnumerable<string>> Roots = Personal;
        internal Func<string, string> Reveal;
        internal Func<DateTime> Now = delegate { return DateTime.Now; };
        internal Func<string, string> Inside = Words;
        internal int Budget = 8000;
        private readonly SavedList<FileChange> saved;
        private readonly object gate = new object();
        private readonly List<FileHit> last = new List<FileHit>();
        private readonly List<FileChange> changes = new List<FileChange>();

        internal FileKeeper(string folder)
        {
            if (string.IsNullOrEmpty(folder)) return;
            saved = new SavedList<FileChange>(Path.Combine(folder, "file-changes.json"), "file changes");
            changes.AddRange(saved.Read(change => change.Steps != null && !string.IsNullOrEmpty(change.Id)));
        }

        internal static bool Changes(ToolArguments args)
        {
            string action = args == null ? null : args.Action;
            return action == "move" || action == "rename" || action == "organise" || action == "organize";
        }

        internal static bool Valid(ToolArguments args)
        {
            if (args == null) return false;
            switch (args.Action ?? "")
            {
                case "": case "find": case "search": return Named(args.Name) || Named(args.Search) || Named(args.Kind) && args.Kind != "any" || Named(args.When);
                case "show": case "undo": return true;
                case "make_folder": return Named(args.Name);
                case "move": return Named(args.Path) && (Named(args.Target) || Named(args.Folder));
                case "rename": return Named(args.Path) && Named(args.Name);
                case "organise": case "organize": return Named(args.Folder) && (string.IsNullOrEmpty(args.By) || args.By == "kind" || args.By == "month" || args.By == "year");
                default: return false;
            }
        }

        private static bool Named(string text) { return !string.IsNullOrWhiteSpace(text); }

        internal string Do(ToolArguments args, Func<bool> stopped)
        {
            if (args == null) args = new ToolArguments();
            if (!Valid(args)) return "Tell me what to find by its name, words inside it, its kind or when it was made, or what to move, rename, make or sort.";
            if (stopped == null) stopped = delegate { return false; };
            try
            {
                switch (args.Action ?? "")
                {
                    case "show": return Show(args.Path);
                    case "make_folder": return MakeFolder(args.Name, args.Folder);
                    case "move": return Move(args.Path, Named(args.Target) ? args.Target : args.Folder);
                    case "rename": return Rename(args.Path, args.Name);
                    case "organise": case "organize": return Organise(args.Folder, args.By);
                    case "undo": return Undo();
                    default: return Find(args, stopped);
                }
            }
            catch (UnauthorizedAccessException) { return "I'm not allowed to touch that, so I left it alone."; }
            catch (IOException error) { return "That didn't work: " + error.Message; }
        }

        internal string Asking(ToolArguments args)
        {
            switch (args.Action)
            {
                case "move":
                    string target = Named(args.Target) ? args.Target : args.Folder;
                    return "move " + Things(args.Path) + " into " + Short(target);
                case "rename": return "rename " + Things(args.Path) + " to " + Short(args.Name);
                default: return "sort the loose files in " + Short(args.Folder) + " into folders by " + (args.By == "month" || args.By == "year" ? args.By : "kind");
            }
        }

        private string Things(string path)
        {
            string error;
            List<string> picked = Picked(path, out error);
            if (picked == null || picked.Count == 0) return "those";
            if (picked.Count == 1) return System.IO.Path.GetFileName(picked[0].TrimEnd('\\', '/'));
            if (picked.Count == 2) return System.IO.Path.GetFileName(picked[0]) + " and " + System.IO.Path.GetFileName(picked[1]);
            return picked.Count + " things (" + System.IO.Path.GetFileName(picked[0]) + ", " + System.IO.Path.GetFileName(picked[1]) + " and more)";
        }

        private static string Short(string text)
        {
            string value = Regex.Replace((text ?? "").Trim(), @"\s+", " ");
            return value.Length > 80 ? value.Substring(0, 77) + "..." : value.Length == 0 ? "there" : value;
        }

        internal static string Kind(string path, bool folder)
        {
            if (folder) return "folder";
            string extension = System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (Regex.IsMatch(extension, @"\A(?:mp4|mkv|mov|avi|wmv|webm|m4v|flv|mpg|mpeg|3gp|mts|m2ts)\z")) return "video";
            if (Regex.IsMatch(extension, @"\A(?:jpe?g|png|gif|webp|bmp|heic|heif|tiff?|raw|cr2|cr3|nef|arw|dng|svg|ico|avif|psd)\z")) return "photo";
            if (Regex.IsMatch(extension, @"\A(?:mp3|wav|flac|m4a|aac|ogg|opus|wma|aiff?|mid|midi)\z")) return "music";
            if (Regex.IsMatch(extension, @"\A(?:pdf|docx?|odt|rtf|txt|md|xlsx?|xlsm|csv|tsv|ods|pptx?|odp|pages|key|numbers|epub)\z")) return "document";
            if (Regex.IsMatch(extension, @"\A(?:zip|7z|rar|tar|gz|tgz|bz2|xz|iso)\z")) return "archive";
            if (Regex.IsMatch(extension, @"\A(?:exe|msi|msix|appx|bat|cmd|dmg|pkg|deb|rpm|appimage|apk)\z")) return "app";
            if (Regex.IsMatch(extension, @"\A(?:cs|py|js|ts|tsx|jsx|html?|css|json|xml|ya?ml|c|cpp|h|hpp|java|go|rs|lua|rb|php|sh|ps1|sql|toml|ini)\z")) return "code";
            return "other";
        }

        private static bool Wordy(string path)
        {
            return Regex.IsMatch(System.IO.Path.GetExtension(path).TrimStart('.').ToLowerInvariant(), @"\A(?:txt|md|csv|tsv|json|xml|html?|log|ini|cfg|ya?ml|toml|cs|py|js|ts|tsx|jsx|css|c|cpp|h|java|go|rs|lua|rb|php|sh|ps1|sql|srt|vtt|rtf|docx|xlsx|pdf)\z");
        }

        private static string Plain(string text) { return Regex.Replace((text ?? "").ToLowerInvariant(), @"[^\p{L}\p{N}]+", " ").Trim(); }

        private static string[] Terms(string text)
        {
            string plain = Regex.Replace(Plain(text), @"\b(?:the|my|a|an|some|of|for|to|on|in|with|and|called|named|file|files|folder|folders)\b", " ");
            List<string> terms = new List<string>();
            foreach (string term in Regex.Split(plain.Trim(), @"\s+")) if (term.Length > 0 && !terms.Contains(term)) terms.Add(term);
            return terms.ToArray();
        }

        internal static int NameScore(string name, string[] terms)
        {
            if (terms.Length == 0) return 1;
            string plain = " " + Plain(System.IO.Path.GetFileNameWithoutExtension(name) + " " + System.IO.Path.GetExtension(name).TrimStart('.')) + " ";
            int score = 0, matched = 0;
            foreach (string term in terms)
            {
                if (plain.Contains(" " + term + " ")) { score += 100; matched++; }
                else if (term.Length >= 3 && plain.Contains(term)) { score += 60; matched++; }
            }
            return matched == 0 ? 0 : score / terms.Length + (matched == terms.Length ? 50 : 0);
        }

        internal static string Words(string path)
        {
            string extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (extension == ".docx" || extension == ".xlsx" || extension == ".pdf")
            {
                ReadFile read = Files.Open(path, null);
                return read.Readable ? read.Text : null;
            }
            using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                byte[] buffer = new byte[(int)Math.Min(stream.Length, TextBytes)];
                int got = 0, read;
                while (got < buffer.Length && (read = stream.Read(buffer, got, buffer.Length - got)) > 0) got += read;
                return Encoding.UTF8.GetString(buffer, 0, got);
            }
        }

        internal static bool Dates(string when, DateTime now, out DateTime from, out DateTime to)
        {
            from = DateTime.MinValue; to = DateTime.MaxValue;
            string text = Regex.Replace((when ?? "").Trim().ToLowerInvariant(), @"\s+", " ").Trim('.', ' ');
            if (text.Length == 0) return true;
            DateTime today = now.Date;
            Match range = Regex.Match(text, @"^(?:from |between )?(.+?)\s*(?:\.\.|–|—| - | to | until | and )\s*(.+)$");
            if (range.Success)
            {
                DateTime a, b, c, d;
                if (!Dates(range.Groups[1].Value, now, out a, out b) || !Dates(range.Groups[2].Value, now, out c, out d) || a == DateTime.MinValue || d == DateTime.MaxValue) return false;
                from = a; to = d;
                return to > from;
            }
            Match side = Regex.Match(text, @"^(before|after|since|from|until|older than|newer than) (.+)$");
            if (side.Success)
            {
                DateTime a, b;
                if (!Dates(side.Groups[2].Value, now, out a, out b) || a == DateTime.MinValue) return false;
                string way = side.Groups[1].Value;
                if (way == "before" || way == "until" || way == "older than") { from = DateTime.MinValue; to = way == "until" ? b : a; }
                else { from = way == "after" ? b : a; to = DateTime.MaxValue; }
                return true;
            }
            int mondayOffset = ((int)today.DayOfWeek + 6) % 7;
            switch (text)
            {
                case "today": from = today; to = today.AddDays(1); return true;
                case "yesterday": from = today.AddDays(-1); to = today; return true;
                case "this week": from = today.AddDays(-mondayOffset); to = today.AddDays(1); return true;
                case "last week": from = today.AddDays(-mondayOffset - 7); to = today.AddDays(-mondayOffset); return true;
                case "this month": from = new DateTime(today.Year, today.Month, 1); to = today.AddDays(1); return true;
                case "last month": to = new DateTime(today.Year, today.Month, 1); from = to.AddMonths(-1); return true;
                case "this year": from = new DateTime(today.Year, 1, 1); to = today.AddDays(1); return true;
                case "last year": from = new DateTime(today.Year - 1, 1, 1); to = new DateTime(today.Year, 1, 1); return true;
                case "recently": case "recent": case "lately": from = today.AddDays(-7); to = DateTime.MaxValue; return true;
            }
            Match back = Regex.Match(text, @"^(?:in the |the )?(?:last|past) (\d{1,3}|a|one|two|three|four|five|six|seven|ten) (day|week|month|year)s?$");
            if (back.Success)
            {
                string count = back.Groups[1].Value;
                int n = Regex.IsMatch(count, @"^\d+$") ? int.Parse(count, CultureInfo.InvariantCulture) : Array.IndexOf(new[] { "a", "one", "two", "three", "four", "five", "six", "seven" }, count);
                if (count == "ten") n = 10;
                if (count == "a") n = 1;
                string unit = back.Groups[2].Value;
                from = unit == "day" ? today.AddDays(1 - n) : unit == "week" ? today.AddDays(-7 * n) : unit == "month" ? today.AddMonths(-n) : today.AddYears(-n);
                to = DateTime.MaxValue;
                return n > 0;
            }
            Match ago = Regex.Match(text, @"^(\d{1,3}|a|one|two|three) (day|week|month|year)s? ago$");
            if (ago.Success)
            {
                string count = ago.Groups[1].Value;
                int n = Regex.IsMatch(count, @"^\d+$") ? int.Parse(count, CultureInfo.InvariantCulture) : count == "a" ? 1 : Array.IndexOf(new[] { "", "one", "two", "three" }, count);
                string unit = ago.Groups[2].Value;
                DateTime at = unit == "day" ? today.AddDays(-n) : unit == "week" ? today.AddDays(-7 * n) : unit == "month" ? today.AddMonths(-n) : today.AddYears(-n);
                int span = unit == "day" ? 1 : unit == "week" ? 7 : unit == "month" ? 31 : 366;
                from = at.AddDays(-span / 2); to = at.AddDays(span / 2 + 1);
                return true;
            }
            string[] weekdays = { "monday", "tuesday", "wednesday", "thursday", "friday", "saturday", "sunday" };
            Match weekday = Regex.Match(text, @"^(?:on |last )?(monday|tuesday|wednesday|thursday|friday|saturday|sunday)$");
            if (weekday.Success)
            {
                int wanted = Array.IndexOf(weekdays, weekday.Groups[1].Value), back7 = (mondayOffset - wanted + 7) % 7;
                if (back7 == 0) back7 = 7;
                from = today.AddDays(-back7); to = from.AddDays(1);
                return true;
            }
            DateTime exact;
            string[] days = { "yyyy-MM-dd", "yyyy/MM/dd", "d MMMM yyyy", "d MMM yyyy", "MMMM d yyyy", "MMM d yyyy", "MMMM d, yyyy", "MMM d, yyyy", "d MMMM", "d MMM", "MMMM d", "MMM d" };
            string cleaned = Regex.Replace(text, @"(\d)(st|nd|rd|th)\b", "$1").Replace(" of ", " ");
            if (DateTime.TryParseExact(cleaned, days, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out exact))
            {
                if (!Regex.IsMatch(cleaned, @"\d{4}") && exact.Date > today) exact = exact.AddYears(-1);
                from = exact.Date; to = from.AddDays(1);
                return true;
            }
            string[] months = { "yyyy-MM", "yyyy/MM", "MMMM yyyy", "MMM yyyy", "MMMM", "MMM" };
            if (DateTime.TryParseExact(cleaned, months, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out exact))
            {
                DateTime month = new DateTime(Regex.IsMatch(cleaned, @"\d{4}") ? exact.Year : today.Year, exact.Month, 1);
                if (!Regex.IsMatch(cleaned, @"\d{4}") && month > today) month = month.AddYears(-1);
                from = month; to = month.AddMonths(1);
                return true;
            }
            Match year = Regex.Match(text, @"^(?:in )?((?:19|20)\d\d)$");
            if (year.Success)
            {
                from = new DateTime(int.Parse(year.Groups[1].Value, CultureInfo.InvariantCulture), 1, 1); to = from.AddYears(1);
                return true;
            }
            return false;
        }

        internal static IEnumerable<string> Personal()
        {
            string profile = Home();
            List<string> places = new List<string>();
            foreach (string place in new[] { "desktop", "downloads", "documents", "pictures", "videos", "music" }) places.Add(Known(place));
            if (!string.IsNullOrEmpty(profile)) places.Add(profile);
#if !CROSS
            foreach (DriveInfo drive in DriveInfo.GetDrives())
                try { if (drive.DriveType == DriveType.Fixed && drive.IsReady && !Same(drive.RootDirectory.FullName, System.IO.Path.GetPathRoot(Environment.SystemDirectory))) places.Add(drive.RootDirectory.FullName); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
#endif
            return places;
        }

        private static string Home()
        {
            try { return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile); }
            catch (Exception) { return ""; }
        }

        internal static string Known(string name)
        {
            string profile = Home();
            switch (Regex.Replace((name ?? "").Trim().ToLowerInvariant(), @"^(?:my|the)\s+", ""))
            {
                case "desktop": return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                case "documents": case "docs": return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
#if CROSS
                case "downloads": case "download": return System.IO.Path.Combine(profile, "Downloads");
                case "pictures": case "photos": case "images": return Regular(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), System.IO.Path.Combine(profile, "Pictures"));
                case "videos": case "movies": return Regular(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), System.IO.Path.Combine(profile, Hands.OnMac ? "Movies" : "Videos"));
                case "music": return Regular(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), System.IO.Path.Combine(profile, "Music"));
#else
                case "downloads": case "download": return FileFinder.Known("374DE290-123F-4565-9164-39C4925E467B", System.IO.Path.Combine(profile, "Downloads"));
                case "pictures": case "photos": case "images": return Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                case "videos": case "movies": return Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
                case "music": return Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
#endif
                case "home": case "user folder": case "profile": return profile;
                default: return null;
            }
        }

#if CROSS
        private static string Regular(string found, string fallback) { return string.IsNullOrEmpty(found) ? fallback : found; }
#endif

        private static bool Same(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
#if CROSS
            StringComparison compare = Hands.OnMac ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
#else
            StringComparison compare = StringComparison.OrdinalIgnoreCase;
#endif
            return string.Equals(Full(a).TrimEnd('\\', '/'), Full(b).TrimEnd('\\', '/'), compare);
        }

        private static bool Under(string path, string folder)
        {
            if (string.IsNullOrEmpty(folder)) return false;
            string inner = Full(path).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar, outer = Full(folder).TrimEnd('\\', '/') + System.IO.Path.DirectorySeparatorChar;
#if CROSS
            StringComparison compare = Hands.OnMac ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
#else
            StringComparison compare = StringComparison.OrdinalIgnoreCase;
#endif
            return inner.StartsWith(outer, compare);
        }

        private static string Full(string path)
        {
            try { return System.IO.Path.GetFullPath(path); }
            catch (Exception) { return path; }
        }

        internal string Place(string text)
        {
            string value = (text ?? "").Trim().Trim('"', '\'', '“', '”').Trim();
            if (value.Length == 0) return null;
            value = Environment.ExpandEnvironmentVariables(value);
            if (value == "~" || value.StartsWith("~/", StringComparison.Ordinal) || value.StartsWith("~\\", StringComparison.Ordinal)) value = Home() + value.Substring(1);
            if (System.IO.Path.IsPathRooted(value)) return Full(value);
            Match number = Regex.Match(value, @"^#?(\d{1,3})$");
            if (number.Success)
            {
                lock (gate)
                {
                    int at = int.Parse(number.Groups[1].Value, CultureInfo.InvariantCulture) - 1;
                    if (at >= 0 && at < last.Count) return last[at].Folder ? last[at].Path : System.IO.Path.GetDirectoryName(last[at].Path);
                }
                return null;
            }
            string[] parts = value.Split(new[] { '\\', '/' }, 2);
            string known = Known(Regex.Replace(parts[0], @"\s+folder$", "", RegexOptions.IgnoreCase));
            if (known == null) return null;
            return parts.Length > 1 && parts[1].Trim().Length > 0 ? Full(System.IO.Path.Combine(known, parts[1].Trim())) : known;
        }

        internal string Guarded(string path, bool moving)
        {
            string full = Full(path).TrimEnd('\\', '/');
            string root = System.IO.Path.GetPathRoot(Full(path)) ?? "";
            if (full.Length == 0 || Same(full, root) || full.Length <= root.TrimEnd('\\', '/').Length) return "a whole drive";
            if (Same(full, Home())) return "your user folder";
            if (moving)
                foreach (string place in new[] { "desktop", "downloads", "documents", "pictures", "videos", "music" })
                    if (Same(full, Known(place))) return "your " + place + " folder itself";
            return Off(full);
        }

        private static List<string> systemFolders;

        private static List<string> SystemFolders()
        {
            List<string> system = systemFolders;
            if (system != null) return system;
            system = new List<string>();
            string home = Home();
#if CROSS
            system.AddRange(new[] { "/bin", "/boot", "/dev", "/etc", "/lib", "/lib64", "/proc", "/sbin", "/sys", "/usr", "/var", "/opt", "/run", "/snap", "/System", "/Library", "/Applications", "/private" });
            if (!string.IsNullOrEmpty(home)) system.Add(System.IO.Path.Combine(home, "Library"));
#else
            system.Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            system.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
            system.Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
            system.Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
            if (!string.IsNullOrEmpty(home)) system.Add(System.IO.Path.Combine(home, "AppData"));
            system.Add(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            system.Add(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
#endif
            system.RemoveAll(string.IsNullOrEmpty);
            systemFolders = system;
            return system;
        }

        internal static string Off(string path)
        {
            string full = Full(path).TrimEnd('\\', '/');
            if (Under(full, System.IO.Path.GetTempPath())) return null;
#if CROSS
            string home = Home();
            if (!string.IsNullOrEmpty(home) && Under(full, home) && full.Substring(home.TrimEnd('/').Length).TrimStart('/').StartsWith(".", StringComparison.Ordinal)) return "a hidden settings folder";
#else
            if (Regex.IsMatch(full, @"^[A-Za-z]:[\\/](?:\$Recycle\.Bin|System Volume Information|Recovery|Config\.Msi)(?:[\\/]|$)", RegexOptions.IgnoreCase)) return "a system folder";
#endif
            foreach (string folder in SystemFolders())
                if (Same(full, folder) || Under(full, folder)) return "a system or app folder";
            if (Regex.IsMatch(full, @"[\\/]\.git(?:[\\/]|$)")) return "a code repository's own files";
            return null;
        }

        private string Find(ToolArguments args, Func<bool> stopped)
        {
            DateTime from, to;
            if (!Dates(args.When, Now(), out from, out to)) return "I couldn't read the date " + args.When + ". Try today, last week, 2026-11-03, 2026-11 or 2026-11-01 to 2026-11-07.";
            string kind = Named(args.Kind) && args.Kind != "any" ? args.Kind : null;
            string[] named = Terms(args.Name), inside = Terms(args.Search);
            bool sight = kind == "video" || kind == "photo" || kind == "music";
            if (sight && inside.Length > 0) { named = Join(named, inside); inside = new string[0]; }
            List<string> roots = new List<string>();
            if (Named(args.Folder))
            {
                string place = Place(args.Folder);
                if (place == null || !Directory.Exists(place)) return "I couldn't find the folder " + args.Folder + ". Give its whole path or a place like Downloads.";
                roots.Add(place);
            }
            else roots.AddRange(Roots());
            Stopwatch clock = Stopwatch.StartNew();
            bool timedOut = false;
            int visited = 0, reads = 0;
            List<FileHit> hits = new List<FileHit>();
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Queue<string> waiting = new Queue<string>();
            foreach (string root in roots)
                if (!string.IsNullOrEmpty(root) && Directory.Exists(root) && (Named(args.Folder) || Off(root) == null)) waiting.Enqueue(Full(root));
            while (waiting.Count > 0 && !stopped())
            {
                if (clock.ElapsedMilliseconds > Budget || visited > Visits) { timedOut = true; break; }
                string folder = waiting.Dequeue();
                if (!seen.Add(folder.TrimEnd('\\', '/'))) continue;
                IEnumerable<FileSystemInfo> entries;
                try { entries = new DirectoryInfo(folder).EnumerateFileSystemInfos(); }
                catch (Exception) { continue; }
                try
                {
                    foreach (FileSystemInfo entry in entries)
                    {
                        if (stopped() || clock.ElapsedMilliseconds > Budget) { timedOut = !stopped(); break; }
                        visited++;
                        FileAttributes attributes;
                        try { attributes = entry.Attributes; }
                        catch (Exception) { continue; }
                        bool directory = (attributes & FileAttributes.Directory) != 0;
                        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 && !Named(args.Folder)) continue;
                        if (entry.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                        if (directory)
                        {
                            if (!FileFinder.Skip(entry.FullName) && (attributes & FileAttributes.ReparsePoint) == 0 && Off(entry.FullName) == null) waiting.Enqueue(entry.FullName);
                            if (kind != null && kind != "folder" || inside.Length > 0 || named.Length == 0 && kind != "folder") continue;
                        }
                        else if (kind == "folder") continue;
                        string what = Kind(entry.Name, directory);
                        if (kind != null && kind != what) continue;
                        DateTime changed, made;
                        try { changed = entry.LastWriteTime; made = entry.CreationTime; }
                        catch (Exception) { continue; }
#if CROSS
                        made = changed;
#endif
                        if (!(changed >= from && changed < to) && !(made >= from && made < to)) continue;
                        int score = NameScore(entry.Name, named);
                        if (named.Length > 0 && score == 0 && inside.Length == 0) continue;
                        long size = directory ? 0 : Length(entry);
                        bool contains = false;
                        if (inside.Length > 0)
                        {
                            if (directory || !Wordy(entry.Name) || size > Readable || reads >= Reads || (attributes & FileAttributes.Offline) != 0 || ((int)attributes & 0x00440000) != 0) continue;
                            reads++;
                            string text;
                            try { text = Inside(entry.FullName); }
                            catch (Exception) { continue; }
                            string plain = " " + Plain(text) + " ";
                            foreach (string term in inside) if (!plain.Contains(term)) { plain = null; break; }
                            if (plain == null) continue;
                            contains = true;
                            score += 80;
                        }
                        hits.Add(new FileHit { Path = entry.FullName, Kind = what, Changed = changed > made ? changed : made, Size = size, Score = score, Folder = directory, Inside = contains });
                        if (hits.Count > 5000) { hits.Sort(Better); hits.RemoveRange(1000, hits.Count - 1000); }
                    }
                }
                catch (Exception) { }
            }
            if (stopped()) return "Stopped looking for files.";
            hits.Sort(Better);
            int total = hits.Count;
            if (hits.Count > Shown) hits.RemoveRange(Shown, hits.Count - Shown);
            lock (gate) { last.Clear(); last.AddRange(hits); }
            string limit = timedOut ? " I stopped after " + (Budget / 1000) + " seconds, so there may be more further in; name a folder to look deeper." : "";
            string note = sight && Named(args.Search) ? " I can't see inside videos, photos or music, so I matched names, kinds and dates." : "";
            if (hits.Count == 0) return "I didn't find anything like that in " + (Named(args.Folder) ? Place(args.Folder) : "your folders") + "." + note + limit;
            StringBuilder told = new StringBuilder(total > hits.Count ? "Found " + total + ", here are the best " + hits.Count + ":" : "Found " + total + ":");
            for (int i = 0; i < hits.Count; i++) told.Append("\n").Append(i + 1).Append(". ").Append(hits[i].Path).Append(" (").Append(Line(hits[i])).Append(")");
            told.Append("\nNumbers work as path for show, move and rename.").Append(note).Append(limit);
            return told.ToString();
        }

        private static string[] Join(string[] a, string[] b)
        {
            List<string> all = new List<string>(a);
            foreach (string term in b) if (!all.Contains(term)) all.Add(term);
            return all.ToArray();
        }

        private static long Length(FileSystemInfo entry)
        {
            FileInfo file = entry as FileInfo;
            try { return file == null ? 0 : file.Length; }
            catch (Exception) { return 0; }
        }

        private static int Better(FileHit a, FileHit b)
        {
            int ranked = b.Score.CompareTo(a.Score);
            return ranked != 0 ? ranked : b.Changed.CompareTo(a.Changed);
        }

        private static string Line(FileHit hit)
        {
            string when = hit.Changed.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
            string size = hit.Folder ? "" : ", " + Size(hit.Size);
            return hit.Kind + ", " + when + size + (hit.Inside ? ", has those words inside" : "");
        }

        internal static string Size(long bytes)
        {
            if (bytes >= 1L << 30) return (bytes / (double)(1L << 30)).ToString("0.#", CultureInfo.InvariantCulture) + " GB";
            if (bytes >= 1L << 20) return (bytes / (double)(1L << 20)).ToString("0.#", CultureInfo.InvariantCulture) + " MB";
            if (bytes >= 1L << 10) return (bytes / 1024) + " KB";
            return bytes + " bytes";
        }

        internal List<string> Picked(string path, out string error)
        {
            error = null;
            List<string> picked = new List<string>();
            string text = (path ?? "").Trim();
            if (text.Length == 0) { error = "Which ones? Give numbers from what I found or whole paths."; return null; }
            lock (gate)
            {
                if (Regex.IsMatch(text, @"^(?:all|every ?(?:one|thing)|all of (?:them|those)|them|those|these)$", RegexOptions.IgnoreCase))
                {
                    if (last.Count == 0) { error = "Find them first, then I can move them."; return null; }
                    foreach (FileHit hit in last) picked.Add(hit.Path);
                    return picked;
                }
                if (Regex.IsMatch(text, @"^#?\d{1,3}(?:\s*(?:-|to)\s*\d{1,3})?(?:\s*(?:,|and|&|\s)\s*#?\d{1,3}(?:\s*(?:-|to)\s*\d{1,3})?)*$", RegexOptions.IgnoreCase))
                {
                    foreach (Match part in Regex.Matches(text, @"(\d{1,3})(?:\s*(?:-|to)\s*(\d{1,3}))?", RegexOptions.IgnoreCase))
                    {
                        int first = int.Parse(part.Groups[1].Value, CultureInfo.InvariantCulture), end = part.Groups[2].Success ? int.Parse(part.Groups[2].Value, CultureInfo.InvariantCulture) : first;
                        for (int i = Math.Min(first, end); i <= Math.Max(first, end); i++)
                        {
                            if (i < 1 || i > last.Count) { error = last.Count == 0 ? "Find them first, then I can use their numbers." : "I only found " + last.Count + ", so there's no number " + i + "."; return null; }
                            if (!picked.Contains(last[i - 1].Path)) picked.Add(last[i - 1].Path);
                        }
                    }
                    return picked;
                }
            }
            foreach (string line in Regex.Split(text, @"\r?\n|\s*;\s*|\s*\|\s*"))
            {
                string one = line.Trim().Trim('"', '\'', '“', '”').Trim();
                if (one.Length == 0) continue;
                string place = Place(one);
                if (place == null || !File.Exists(place) && !Directory.Exists(place)) { error = "I couldn't find " + one + ". Give its whole path or a number from what I found."; return null; }
                if (!picked.Contains(place)) picked.Add(place);
            }
            if (picked.Count == 0) error = "Which ones? Give numbers from what I found or whole paths.";
            return picked.Count == 0 ? null : picked;
        }

        private string Show(string path)
        {
            List<string> picked;
            string error = null;
            if (Named(path)) picked = Picked(path, out error);
            else lock (gate) picked = last.Count == 0 ? null : new List<string> { last[0].Path };
            if (picked == null) return error ?? "Find something first, then I can show it to you.";
            string first = picked[0];
            if (!File.Exists(first) && !Directory.Exists(first)) return "That isn't there any more: " + first;
            Func<string, string> reveal = Reveal;
            try { string shown = reveal == null ? null : reveal(first); if (shown != null) return shown; }
            catch (Exception) { }
            return "Here's the path:\n" + first;
        }

        private string MakeFolder(string name, string where)
        {
            string wanted = (name ?? "").Trim().Trim('"', '\'');
            string folder;
            if (System.IO.Path.IsPathRooted(Environment.ExpandEnvironmentVariables(wanted)) || wanted.StartsWith("~", StringComparison.Ordinal)) folder = Place(wanted);
            else
            {
                if (wanted.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || wanted == "." || wanted == "..") return "A folder name can't have " + Odd(wanted) + " in it.";
                string parent = Named(where) ? Place(where) : Beside();
                if (parent == null) return Named(where) ? "I couldn't find " + where + ". Give its whole path or a place like Documents." : "Where should I make it? Name a place like Documents or Downloads, or a whole path.";
                folder = System.IO.Path.Combine(parent, wanted);
            }
            if (folder == null) return "I couldn't tell where that folder should go.";
            string guarded = Guarded(folder, false);
            if (guarded != null) return "I won't make folders in " + guarded + ".";
            if (Directory.Exists(folder)) return folder + " is already there.";
            if (File.Exists(folder)) return "There's a file called that already: " + folder;
            List<FileStep> steps = new List<FileStep>();
            Made(folder, steps);
            Record("made folder " + System.IO.Path.GetFileName(folder), steps);
            return "Made the folder " + folder + "." + UndoLine.Replace("put them back", "take it away");
        }

        private static string Odd(string name)
        {
            foreach (char letter in name) if (Array.IndexOf(System.IO.Path.GetInvalidFileNameChars(), letter) >= 0) return letter == '\\' || letter == '/' ? "slashes" : "\"" + letter + "\"";
            return "dots on their own";
        }

        private string Beside()
        {
            lock (gate)
            {
                if (last.Count == 0) return null;
                string parent = System.IO.Path.GetDirectoryName(last[0].Path);
                foreach (FileHit hit in last) if (!Same(System.IO.Path.GetDirectoryName(hit.Path), parent)) return null;
                return parent;
            }
        }

        private static void Made(string folder, List<FileStep> steps)
        {
            List<string> missing = new List<string>();
            for (string at = folder; !string.IsNullOrEmpty(at) && !Directory.Exists(at); at = System.IO.Path.GetDirectoryName(at)) missing.Insert(0, at);
            foreach (string one in missing)
            {
                Directory.CreateDirectory(one);
                steps.Add(new FileStep { From = "", To = one });
            }
        }

        private string Move(string path, string target)
        {
            string error;
            List<string> picked = Picked(path, out error);
            if (picked == null) return error;
            if (picked.Count > MostMoved) return "That's " + picked.Count + " things. I move up to " + MostMoved + " at once, so pick fewer.";
            string destination = Place(target);
            if (destination == null)
            {
                string name = (target ?? "").Trim().Trim('"', '\'');
                if (name.Length == 0 || name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0) return "I couldn't find the folder " + target + ". Give its whole path, a place like Pictures, or a new folder's name.";
                destination = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(picked[0].TrimEnd('\\', '/')), name);
            }
            string guarded = Guarded(destination, false);
            if (guarded != null) return "I won't move things into " + guarded + ".";
            if (File.Exists(destination)) return destination + " is a file, not a folder.";
            foreach (string one in picked)
            {
                string held = Guarded(one, true);
                if (held != null) return "I won't move " + held + ", so I left everything alone.";
                if (Same(one, destination) || Directory.Exists(one) && Under(destination, one)) return "I can't move " + System.IO.Path.GetFileName(one) + " into itself.";
            }
            List<FileStep> steps = new List<FileStep>();
            Made(destination, steps);
            List<string> moved = new List<string>(), failed = new List<string>();
            foreach (string one in picked)
            {
                string done = MoveOne(one, destination, steps);
                if (done == null) failed.Add(System.IO.Path.GetFileName(one.TrimEnd('\\', '/')));
                else if (done.Length > 0) moved.Add(System.IO.Path.GetFileName(done));
            }
            if (steps.Count == 0) return failed.Count > 0 ? "I couldn't move " + Listed(failed) + ". They may be open or I may not be allowed." : "They're already in " + destination + ".";
            Record("moved " + moved.Count + " into " + System.IO.Path.GetFileName(destination), steps);
            return (moved.Count == 0 ? "Made " + destination + "." : "Moved " + Listed(moved) + " into " + destination + ".") + (failed.Count > 0 ? " I couldn't move " + Listed(failed) + "; they may be open." : "") + UndoLine;
        }

        private string MoveOne(string source, string folder, List<FileStep> steps)
        {
            string name = System.IO.Path.GetFileName(source.TrimEnd('\\', '/'));
            string destination = System.IO.Path.Combine(folder, name);
            if (Same(destination, source)) return "";
            destination = Free(destination);
            try
            {
                if (Directory.Exists(source)) Directory.Move(source, destination);
                else File.Move(source, destination);
            }
            catch (Exception) { return null; }
            steps.Add(new FileStep { From = source, To = destination });
            Moved(source, destination);
            return destination;
        }

        private void Moved(string from, string to)
        {
            lock (gate)
                foreach (FileHit hit in last)
                    if (Same(hit.Path, from)) hit.Path = to;
                    else if (Under(hit.Path, from)) hit.Path = to + hit.Path.Substring(from.TrimEnd('\\', '/').Length);
        }

        internal static string Free(string path)
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return path;
            string folder = System.IO.Path.GetDirectoryName(path), stem = System.IO.Path.GetFileNameWithoutExtension(path), extension = System.IO.Path.GetExtension(path);
            if (Directory.Exists(path)) { stem = System.IO.Path.GetFileName(path); extension = ""; }
            for (int i = 2; ; i++)
            {
                string next = System.IO.Path.Combine(folder, stem + " (" + i + ")" + extension);
                if (!File.Exists(next) && !Directory.Exists(next)) return next;
            }
        }

        private static string Listed(List<string> names)
        {
            if (names.Count > 4) return names[0] + ", " + names[1] + ", " + names[2] + " and " + (names.Count - 3) + " more";
            if (names.Count == 1) return names[0];
            return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
        }

        private string Rename(string path, string name)
        {
            string error;
            List<string> picked = Picked(path, out error);
            if (picked == null) return error;
            if (picked.Count != 1) return "I rename one thing at a time. Which one?";
            string source = picked[0].TrimEnd('\\', '/');
            string guarded = Guarded(source, true);
            if (guarded != null) return "I won't rename " + guarded + ".";
            string wanted = (name ?? "").Trim().Trim('"', '\'').Trim();
            if (wanted.Length == 0 || wanted.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || wanted == "." || wanted == "..") return "A name can't have " + Odd(wanted) + " in it.";
            bool folder = Directory.Exists(source);
            string extension = System.IO.Path.GetExtension(source);
            if (!folder && extension.Length > 0 && System.IO.Path.GetExtension(wanted).Length == 0) wanted += extension;
            string destination = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(source), wanted);
            if (destination == source) return "It's already called " + wanted + ".";
            bool caseOnly = string.Equals(destination, source, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (File.Exists(destination) || Directory.Exists(destination))) return "There's already something called " + wanted + " there.";
            if (caseOnly)
            {
                string between = Free(source + ".renaming");
                if (folder) { Directory.Move(source, between); Directory.Move(between, destination); }
                else { File.Move(source, between); File.Move(between, destination); }
            }
            else if (folder) Directory.Move(source, destination);
            else File.Move(source, destination);
            Moved(source, destination);
            Record("renamed " + System.IO.Path.GetFileName(source) + " to " + wanted, new List<FileStep> { new FileStep { From = source, To = destination } });
            return "Renamed " + System.IO.Path.GetFileName(source) + " to " + wanted + "." + UndoLine.Replace("put them back", "change it back");
        }

        internal static string Group(string path, DateTime changed, string by)
        {
            if (by == "month") return changed.ToString("yyyy-MM", CultureInfo.InvariantCulture);
            if (by == "year") return changed.ToString("yyyy", CultureInfo.InvariantCulture);
            switch (Kind(path, false))
            {
                case "video": return "Videos";
                case "photo": return "Pictures";
                case "music": return "Music";
                case "document": return "Documents";
                case "archive": return "Archives";
                case "app": return "Installers";
                case "code": return "Code";
                default: return null;
            }
        }

        private string Organise(string where, string by)
        {
            string folder = Place(where);
            if (folder == null || !Directory.Exists(folder)) return "I couldn't find the folder " + where + ". Give its whole path or a place like Downloads.";
            string guarded = Guarded(folder, false);
            if (guarded != null) return "I won't sort " + guarded + ".";
            List<KeyValuePair<string, string>> plan = new List<KeyValuePair<string, string>>();
            int more = 0;
            foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles())
            {
                FileAttributes attributes;
                DateTime changed;
                try { attributes = file.Attributes; changed = file.LastWriteTime; }
                catch (Exception) { continue; }
                if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0 || file.Name.StartsWith(".", StringComparison.Ordinal)) continue;
                if (Regex.IsMatch(file.Extension, @"\A\.(?:lnk|url|ini|tmp|part|crdownload|download)\z", RegexOptions.IgnoreCase)) continue;
                string group = Group(file.Name, changed, by);
                if (group == null) continue;
                if (plan.Count >= MostMoved) { more++; continue; }
                plan.Add(new KeyValuePair<string, string>(file.FullName, System.IO.Path.Combine(folder, group)));
            }
            if (plan.Count == 0) return "There are no loose files to sort in " + folder + ".";
            List<FileStep> steps = new List<FileStep>();
            Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            int failed = 0;
            foreach (KeyValuePair<string, string> move in plan)
            {
                Made(move.Value, steps);
                if (MoveOne(move.Key, move.Value, steps) == null) { failed++; continue; }
                string group = System.IO.Path.GetFileName(move.Value);
                int count;
                counts.TryGetValue(group, out count);
                counts[group] = count + 1;
            }
            List<string> told = new List<string>();
            foreach (KeyValuePair<string, int> count in counts) told.Add(count.Value + " into " + count.Key);
            if (told.Count == 0) return "I couldn't move any of them. They may be open or I may not be allowed.";
            Record("sorted " + System.IO.Path.GetFileName(folder.TrimEnd('\\', '/')) + " by " + (by ?? "kind"), steps);
            return "Sorted " + folder + ": " + Listed(told) + "." + (failed > 0 ? " " + failed + " were open or locked, so I left them." : "") + (more > 0 ? " " + more + " more are left; ask again to keep going." : "") + " Anything I didn't recognise stayed where it was." + UndoLine;
        }

        private void Record(string what, List<FileStep> steps)
        {
            FileChange change = new FileChange { Id = Guid.NewGuid().ToString("N"), When = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture), What = what, Steps = steps };
            lock (gate)
            {
                changes.Add(change);
                if (changes.Count > Kept) changes.RemoveRange(0, changes.Count - Kept);
                Save();
            }
            Action<FileChange> changed = Changed;
            if (changed != null) try { changed(change); } catch (Exception) { }
        }

        private void Save()
        {
            if (saved == null) return;
            try { saved.Write(changes, ".saving"); }
            catch (Exception) { }
        }

        internal List<FileChange> Recent()
        {
            lock (gate) return new List<FileChange>(changes);
        }

        internal string Undo() { return Undo(null); }

        internal string Undo(string id)
        {
            FileChange change = null;
            lock (gate)
                for (int i = changes.Count - 1; i >= 0; i--)
                    if (!changes[i].Undone && (id == null || changes[i].Id == id)) { change = changes[i]; break; }
            if (change == null) return id == null ? "There's nothing of mine to undo." : "That change was already put back.";
            int back = 0, missing = 0;
            for (int i = change.Steps.Count - 1; i >= 0; i--)
            {
                FileStep step = change.Steps[i];
                try
                {
                    if (string.IsNullOrEmpty(step.From))
                    {
                        if (Directory.Exists(step.To) && !Directory.EnumerateFileSystemEntries(step.To).GetEnumerator().MoveNext()) Directory.Delete(step.To);
                        continue;
                    }
                    bool folder = Directory.Exists(step.To);
                    if (!folder && !File.Exists(step.To)) { missing++; continue; }
                    string home = step.From;
                    if (File.Exists(home) || Directory.Exists(home)) home = Free(home);
                    string parent = System.IO.Path.GetDirectoryName(home);
                    if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
                    if (folder) Directory.Move(step.To, home);
                    else File.Move(step.To, home);
                    Moved(step.To, home);
                    back++;
                }
                catch (Exception) { missing++; }
            }
            lock (gate) { change.Undone = true; Save(); }
            Action<FileChange> changed = Changed;
            if (changed != null) try { changed(change); } catch (Exception) { }
            if (back == 0 && missing == 0) return "Undid it: " + change.What + ".";
            return "Put back " + back + (missing > 0 ? ", but " + missing + " had moved or gone since, so I left those" : "") + " (" + change.What + ").";
        }
    }
}
