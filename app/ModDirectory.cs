using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
#if !CROSS
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
#endif
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class ModListing
    {
        internal string Folder, FileName, Sha, PreviewSha;
        internal string Name = "", Author = "", About = "";
        // A checked download of this listing, kept from looking for updates so getting it doesn't download it again.
        internal string Fetched;
        internal long Size;
        internal int Downloads, Votes;
        internal double Rating;
        // A colour set, like Colors: Bubblegum: a settings mod the index files with skins, so it shows under Skins (Lachy, 6 Nov).
        internal bool Colours;
        internal string Title
        {
            get
            {
                if (Name.Length > 0) return Name;
                string[] words = Folder.Split(new[] { '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < words.Length; i++) words[i] = char.ToUpperInvariant(words[i][0]) + words[i].Substring(1);
                return string.Join(" ", words);
            }
        }
        internal string Kind
        {
            get
            {
                string extension = Path.GetExtension(FileName);
                return string.Equals(extension, Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) ? "RUNS CODE." : string.Equals(extension, Mods.SkinExtension, StringComparison.OrdinalIgnoreCase) ? "SKIN." : string.Equals(extension, Mods.SkillExtension, StringComparison.OrdinalIgnoreCase) ? "SKILL." : Colours ? "COLOURS." : "SETTINGS.";
            }
        }
        internal string Id { get { return Path.GetFileNameWithoutExtension(FileName); } }
        internal int KindIndex
        {
            get
            {
                string extension = Path.GetExtension(FileName);
                return string.Equals(extension, Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) ? 2 : string.Equals(extension, Mods.SkinExtension, StringComparison.OrdinalIgnoreCase) ? 3 : string.Equals(extension, Mods.SkillExtension, StringComparison.OrdinalIgnoreCase) ? 4 : Colours ? 3 : 1;
            }
        }
    }

    internal sealed class ModStats
    {
        internal int Downloads, Votes;
        internal double Rating;
    }

    internal static class ModDirectory
    {
        internal const string Page = "https://github.com/lachydotmcg/capri-mods";
        internal static readonly string[] Kinds = { "All", "Settings", "Code", "Skins", "Skills" };
        internal static readonly string[] Sorts = { "A to Z", "Downloads", "Top rated" };
        private const string Api = "https://api.github.com/repos/lachydotmcg/capri-mods/git/";
        private const long Largest = 96L * 1024 * 1024;
        private const long LargestList = 16L * 1024 * 1024;
        private const long LargestPreview = 2L * 1024 * 1024;
        private const long LargestIndex = 2L * 1024 * 1024;
        private const int WidestPreview = 4096, PreviewWide = 444;
        private const string Raw = "https://raw.githubusercontent.com/lachydotmcg/capri-mods/HEAD/";
        internal const string IndexPage = Raw + "index.json";
        internal const string StatsPage = "https://capri-reactions.lachydotmcg.workers.dev/mods";
        private static readonly byte[] PngStart = { 137, 80, 78, 71, 13, 10, 26, 10 };
        private static readonly Regex PreviewFile = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/preview\.png$");
        internal static string PreviewFolder = Path.Combine(Path.GetTempPath(), "capri-mod-previews");
        internal static Func<string, string, byte[]> Post = Push;
        private static readonly Regex Listed = new Regex(@"^mods/([A-Za-z0-9][A-Za-z0-9._-]{0,63})/([A-Za-z0-9][A-Za-z0-9._-]{0,63}\.capri-(?:mod|code|skin|skill))$");
        private static readonly Regex Blob = new Regex(@"^[0-9a-f]{40}$");
        internal static Func<string, long, byte[]> Get = Fetch;

        [DataContract]
        private sealed class Tree
        {
            [DataMember(Name = "tree")] public TreeEntry[] Entries;
        }

        [DataContract]
        private sealed class TreeEntry
        {
            [DataMember(Name = "path")] public string Path;
            [DataMember(Name = "type")] public string Type;
            [DataMember(Name = "sha")] public string Sha;
            [DataMember(Name = "size")] public long Size;
        }

        [DataContract]
        private sealed class IndexBody
        {
            [DataMember(Name = "format")] public int Format;
            [DataMember(Name = "mods")] public IndexEntry[] Mods;
        }

        [DataContract]
        private sealed class IndexEntry
        {
            [DataMember(Name = "folder")] public string Folder;
            [DataMember(Name = "file")] public string File;
            [DataMember(Name = "name")] public string Name;
            [DataMember(Name = "author")] public string Author;
            [DataMember(Name = "about")] public string About;
            [DataMember(Name = "kind")] public string Kind;
        }

        [DataContract]
        private sealed class StatsBody
        {
            [DataMember(Name = "mods")] public StatsEntry[] Mods;
        }

        [DataContract]
        private sealed class StatsEntry
        {
            [DataMember(Name = "folder")] public string Folder;
            [DataMember(Name = "downloads")] public int Downloads;
            [DataMember(Name = "rating")] public double Rating;
            [DataMember(Name = "votes")] public int Votes;
        }

        internal static List<ModListing> Browse()
        {
            List<ModListing> found = Parse(Get(Api + "trees/HEAD?recursive=1", LargestList));
            try { ReadIndex(found, Get(IndexPage, LargestIndex)); }
            catch (Exception) { }
            return found;
        }

        private static void ReadIndex(List<ModListing> listings, byte[] json)
        {
            if (json == null || json.Length > LargestIndex) return;
            IndexBody index;
            using (MemoryStream body = new MemoryStream(json)) index = (IndexBody)new DataContractJsonSerializer(typeof(IndexBody)).ReadObject(body);
            if (index == null || index.Format != 1) return;
            Dictionary<string, ModListing> listed = new Dictionary<string, ModListing>(StringComparer.Ordinal);
            foreach (ModListing listing in listings) listed[listing.Folder + "/" + listing.FileName] = listing;
            foreach (IndexEntry entry in index.Mods ?? new IndexEntry[0])
            {
                if (entry == null || entry.Folder == null || entry.File == null) continue;
                ModListing listing;
                if (!listed.TryGetValue(entry.Folder + "/" + entry.File, out listing)) continue;
                listing.Name = Plain(entry.Name, 120);
                listing.Author = Plain(entry.Author, 80);
                listing.About = Plain(entry.About, 600);
                // Only a settings mod can move, and only to Skins: the index never makes code or a skill look like something else.
                listing.Colours = entry.Kind == "skin" && string.Equals(Path.GetExtension(listing.FileName), Mods.Extension, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static string Plain(string value, int most)
        {
            string line = Regex.Replace(value ?? "", @"[\p{Cc}\p{Cf}\s]+", " ").Trim();
            if (line.Length > most) line = line.Substring(0, char.IsHighSurrogate(line[most - 1]) ? most - 1 : most).TrimEnd();
            return line;
        }

        internal static List<ModListing> Parse(byte[] json)
        {
            Tree tree;
            try
            {
                using (MemoryStream body = new MemoryStream(json)) tree = (Tree)new DataContractJsonSerializer(typeof(Tree)).ReadObject(body);
            }
            catch (SerializationException) { throw new ModException("The mods directory sent something I can't read."); }
            List<ModListing> found = new List<ModListing>();
            Dictionary<string, string> previews = new Dictionary<string, string>();
            foreach (TreeEntry entry in tree.Entries ?? new TreeEntry[0])
            {
                if (entry == null || entry.Type != "blob" || entry.Path == null || entry.Sha == null || entry.Size <= 0 || entry.Size > LargestPreview || !Blob.IsMatch(entry.Sha)) continue;
                Match picture = PreviewFile.Match(entry.Path);
                if (picture.Success && !picture.Groups[1].Value.Contains("..")) previews[picture.Groups[1].Value] = entry.Sha;
            }
            foreach (TreeEntry entry in tree.Entries ?? new TreeEntry[0])
            {
                if (entry == null || entry.Type != "blob" || entry.Path == null || entry.Sha == null || entry.Size <= 0 || entry.Size > Largest || !Blob.IsMatch(entry.Sha)) continue;
                Match match = Listed.Match(entry.Path);
                if (!match.Success || match.Groups[1].Value.Contains("..") || match.Groups[2].Value.Contains("..")) continue;
                string previewSha;
                previews.TryGetValue(match.Groups[1].Value, out previewSha);
                found.Add(new ModListing { Folder = match.Groups[1].Value, FileName = match.Groups[2].Value, Sha = entry.Sha, Size = entry.Size, PreviewSha = previewSha });
            }
            found.Sort(delegate(ModListing a, ModListing b) { return Named(a, b); });
            return found;
        }

        private static int Named(ModListing a, ModListing b)
        {
            return StringComparer.OrdinalIgnoreCase.Compare(a.Folder + "/" + a.FileName, b.Folder + "/" + b.FileName);
        }

        internal static List<ModListing> Filter(List<ModListing> listings, string search, int kind)
        {
            string[] words = (search ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return listings.FindAll(delegate(ModListing listing)
            {
                if (kind != 0 && listing.KindIndex != kind) return false;
                string named = listing.Folder + " " + listing.FileName + " " + listing.Title + " " + listing.Author + " " + listing.About;
                foreach (string word in words) if (named.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            });
        }

        internal static List<ModListing> Sort(List<ModListing> listings, int order)
        {
            if (order <= 0 || order >= Sorts.Length) return listings;
            List<ModListing> sorted = new List<ModListing>(listings);
            if (order == 1) sorted.Sort(delegate(ModListing a, ModListing b)
            {
                int by = b.Downloads.CompareTo(a.Downloads);
                return by != 0 ? by : Named(a, b);
            });
            else sorted.Sort(delegate(ModListing a, ModListing b)
            {
                int by = (a.Votes > 0 ? 0 : 1).CompareTo(b.Votes > 0 ? 0 : 1);
                if (by == 0) by = b.Rating.CompareTo(a.Rating);
                if (by == 0) by = b.Votes.CompareTo(a.Votes);
                return by != 0 ? by : Named(a, b);
            });
            return sorted;
        }

        // The directory's listings for installed mods it now has a different file for: same id and kind, and the installed copy's
        // git fingerprint isn't the listing's. Settings and skill mods are saved as text without a byte order mark, so a copy
        // that only lost one still counts as the same. Mods that aren't in the directory, or can't be read, are left out.
        internal static List<ModListing> Updates(List<ModListing> listings, List<Mod> installed)
        {
            List<ModListing> found = new List<ModListing>();
            byte[] mark = { 0xEF, 0xBB, 0xBF };
            foreach (Mod mod in installed)
            {
                if (mod == null || mod.File == null || mod.Path == null) continue;
                ModListing listing = listings.Find(delegate(ModListing each)
                {
                    return string.Equals(each.Id, mod.File.Id, StringComparison.OrdinalIgnoreCase) && string.Equals(Path.GetExtension(each.FileName), Path.GetExtension(mod.Path), StringComparison.OrdinalIgnoreCase);
                });
                if (listing == null) continue;
                byte[] bytes;
                try
                {
                    if (!File.Exists(mod.Path) || new FileInfo(mod.Path).Length > Largest) continue;
                    bytes = File.ReadAllBytes(mod.Path);
                }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                if (BlobSha(bytes) == listing.Sha) continue;
                bool text = !string.Equals(Path.GetExtension(mod.Path), Mods.CodeExtension, StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetExtension(mod.Path), Mods.SkinExtension, StringComparison.OrdinalIgnoreCase);
                if (text && listing.Size == bytes.Length + mark.Length)
                {
                    byte[] marked = new byte[bytes.Length + mark.Length];
                    Array.Copy(mark, marked, mark.Length);
                    Array.Copy(bytes, 0, marked, mark.Length, bytes.Length);
                    if (BlobSha(marked) == listing.Sha) continue;
                }
                found.Add(listing);
            }
            found.Sort(Named);
            return found;
        }

        // Of the listings Updates found, the ones that are really newer: each is downloaded and checked, and one whose version
        // isn't above the installed copy's, like someone's own mod that shares a directory mod's id, is dropped. When either
        // side has no version, a different file counts as newer. Each kept listing holds its download in Fetched.
        internal static List<ModListing> Newer(List<ModListing> candidates, List<Mod> installed, string under)
        {
            List<ModListing> newer = new List<ModListing>();
            foreach (ModListing listing in candidates)
            {
                Mod mine = installed.Find(mod => mod != null && mod.File != null && string.Equals(mod.File.Id, listing.Id, StringComparison.OrdinalIgnoreCase));
                string file;
                try { file = Download(listing, under); }
                catch (Exception error)
                {
                    if (!(error is ModException || error is IOException || error is UnauthorizedAccessException || error is WebException)) throw;
                    // Getting it will try again and say what went wrong.
                    newer.Add(listing);
                    continue;
                }
                int? order = Compare(Mods.VersionIn(file), mine != null ? mine.File.Version : null);
                if (order.HasValue && order.Value <= 0)
                {
                    Clean(file);
                    continue;
                }
                listing.Fetched = file;
                newer.Add(listing);
            }
            return newer;
        }

        // Which of two versions like 1.10.2 is higher, by their numbers in order, or null when either has no numbers.
        internal static int? Compare(string theirs, string mine)
        {
            MatchCollection a = Regex.Matches(theirs ?? "", @"\d+"), b = Regex.Matches(mine ?? "", @"\d+");
            if (a.Count == 0 || b.Count == 0) return null;
            for (int i = 0; i < Math.Max(a.Count, b.Count); i++)
            {
                decimal x = i < a.Count ? decimal.Parse(a[i].Value.Length > 20 ? a[i].Value.Substring(0, 20) : a[i].Value, CultureInfo.InvariantCulture) : 0;
                decimal y = i < b.Count ? decimal.Parse(b[i].Value.Length > 20 ? b[i].Value.Substring(0, 20) : b[i].Value, CultureInfo.InvariantCulture) : 0;
                if (x != y) return x > y ? 1 : -1;
            }
            return 0;
        }

        // Deletes the downloads kept for listings that won't be used now.
        internal static void Forget(List<ModListing> listings)
        {
            if (listings == null) return;
            foreach (ModListing listing in listings)
                if (listing.Fetched != null)
                {
                    Clean(listing.Fetched);
                    listing.Fetched = null;
                }
        }

        // "Galaxy", "Galaxy and Ember", "Galaxy, Ember and 3 more".
        internal static string Names(List<ModListing> listings)
        {
            List<string> titles = listings.ConvertAll(listing => listing.Title);
            if (titles.Count <= 2) return string.Join(" and ", titles.ToArray());
            if (titles.Count == 3) return titles[0] + ", " + titles[1] + " and " + titles[2];
            return titles[0] + ", " + titles[1] + " and " + (titles.Count - 2) + " more";
        }

        internal static string Download(ModListing listing, string under)
        {
            byte[] bytes = Get(Api + "blobs/" + listing.Sha, Largest);
            if (bytes.Length != listing.Size || BlobSha(bytes) != listing.Sha) throw new ModException(listing.FileName + " didn't match what the directory listed, so it wasn't installed.");
            string folder = Path.Combine(under, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, Path.GetFileName(listing.FileName));
            File.WriteAllBytes(file, bytes);
            return file;
        }

        internal static byte[] Preview(ModListing listing, string cache)
        {
            if (listing.PreviewSha == null) return null;
            string kept = Path.Combine(cache, listing.PreviewSha + ".png");
            try
            {
                if (File.Exists(kept))
                {
                    byte[] saved = File.ReadAllBytes(kept);
                    if (saved.Length <= LargestPreview && BlobSha(saved) == listing.PreviewSha) return saved;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            byte[] bytes = Get(Raw + "mods/" + listing.Folder + "/preview.png", LargestPreview);
            if (BlobSha(bytes) != listing.PreviewSha || !IsPng(bytes)) throw new ModException(listing.Folder + "'s preview didn't match what the directory listed.");
            try
            {
                Directory.CreateDirectory(cache);
                File.WriteAllBytes(kept, bytes);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            return bytes;
        }

        internal static bool IsPng(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 24) return false;
            for (int i = 0; i < PngStart.Length; i++) if (bytes[i] != PngStart[i]) return false;
            return true;
        }

        internal static long BigEndian(byte[] bytes, int at)
        {
            return ((long)bytes[at] << 24) | ((long)bytes[at + 1] << 16) | ((long)bytes[at + 2] << 8) | bytes[at + 3];
        }

#if !CROSS
        internal static Image Picture(byte[] bytes)
        {
            if (!IsPng(bytes)) return null;
            long wide = BigEndian(bytes, 16), tall = BigEndian(bytes, 20);
            if (wide < 1 || tall < 1 || wide > WidestPreview || tall > WidestPreview) return null;
            try
            {
                using (MemoryStream body = new MemoryStream(bytes))
                using (Image read = Image.FromStream(body))
                {
                    int width = Math.Min(PreviewWide, read.Width), height = Math.Max(1, (int)Math.Round(read.Height * (double)width / read.Width));
                    Bitmap small = new Bitmap(width, height);
                    using (Graphics paint = Graphics.FromImage(small))
                    {
                        paint.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        paint.DrawImage(read, 0, 0, width, height);
                    }
                    return small;
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            catch (OutOfMemoryException) { return null; }
        }
#else
        internal static Avalonia.Media.Imaging.Bitmap Picture(byte[] bytes)
        {
            if (!IsPng(bytes)) return null;
            long wide = BigEndian(bytes, 16), tall = BigEndian(bytes, 20);
            if (wide < 1 || tall < 1 || wide > WidestPreview || tall > WidestPreview) return null;
            try
            {
                using (MemoryStream body = new MemoryStream(bytes))
                    return Avalonia.Media.Imaging.Bitmap.DecodeToWidth(body, (int)Math.Min(PreviewWide, wide));
            }
            catch (ArgumentException) { return null; }
            catch (InvalidOperationException) { return null; }
            catch (NotSupportedException) { return null; }
        }
#endif

        internal static Dictionary<string, ModStats> Stats()
        {
            StatsBody body;
            try
            {
                using (MemoryStream read = new MemoryStream(Get(StatsPage, LargestPreview))) body = (StatsBody)new DataContractJsonSerializer(typeof(StatsBody)).ReadObject(read);
            }
            catch (SerializationException) { throw new ModException("The mods directory's numbers couldn't be read."); }
            Dictionary<string, ModStats> found = new Dictionary<string, ModStats>(StringComparer.OrdinalIgnoreCase);
            foreach (StatsEntry entry in body.Mods ?? new StatsEntry[0])
                if (entry != null && entry.Folder != null) found[entry.Folder] = new ModStats { Downloads = Math.Max(0, entry.Downloads), Rating = Math.Max(0, Math.Min(5, entry.Rating)), Votes = Math.Max(0, entry.Votes) };
            return found;
        }

        internal static void Apply(List<ModListing> listings, Dictionary<string, ModStats> stats)
        {
            foreach (ModListing listing in listings)
            {
                ModStats found;
                if (!stats.TryGetValue(listing.Folder, out found)) continue;
                listing.Downloads = found.Downloads;
                listing.Rating = found.Rating;
                listing.Votes = found.Votes;
            }
        }

        internal static string RatingLine(ModListing listing)
        {
            return listing.Votes == 0 ? "No ratings" : listing.Rating.ToString("0.0", CultureInfo.InvariantCulture) + " (" + listing.Votes + ")";
        }

        internal static string DownloadLine(ModListing listing)
        {
            string count = listing.Downloads >= 1000000 ? (listing.Downloads / 1000000.0).ToString("0.#", CultureInfo.InvariantCulture) + "M" : listing.Downloads >= 1000 ? (listing.Downloads / 1000.0).ToString("0.#", CultureInfo.InvariantCulture) + "K" : listing.Downloads.ToString(CultureInfo.InvariantCulture);
            return count + (listing.Downloads == 1 ? " download" : " downloads");
        }

        internal static void CountDownload(string folder)
        {
            Post(StatsPage + "/download", "{\"folder\":\"" + folder + "\"}");
        }

        internal static ModStats Rate(string folder, int stars)
        {
            byte[] answer = Post(StatsPage + "/rate", "{\"folder\":\"" + folder + "\",\"stars\":" + stars + "}");
            StatsEntry entry;
            try
            {
                using (MemoryStream read = new MemoryStream(answer)) entry = (StatsEntry)new DataContractJsonSerializer(typeof(StatsEntry)).ReadObject(read);
            }
            catch (SerializationException) { throw new ModException("Your rating's answer couldn't be read."); }
            return new ModStats { Downloads = Math.Max(0, entry.Downloads), Rating = Math.Max(0, Math.Min(5, entry.Rating)), Votes = Math.Max(0, entry.Votes) };
        }

        internal static string PublishSteps(Mod mod)
        {
            string id = mod.File.Id, file = Path.GetFileName(mod.Path);
            return "1. Press Fork capri-mods and make your own copy on GitHub. You'll need a free account.\n\n"
                + "2. In your copy, add a folder called mods/" + id + "/.\n\n"
                + "3. Put in " + file + " and a short README.md that says what it does and who made it.\n\n"
                + "4. Add a picture called preview.png, around 16:9. It's what people see in the mods window.\n\n"
                + "5. Open a pull request to capri-mods. Every mod is checked for malware before it goes up."
                + (mod.Code ? "\n\nThis is a code mod, so Capri will ask people before running it." : "");
        }

        internal static string BlobSha(byte[] bytes)
        {
            byte[] head = Encoding.ASCII.GetBytes("blob " + bytes.Length + "\0");
            using (SHA1 sha = SHA1.Create())
            {
                sha.TransformBlock(head, 0, head.Length, null, 0);
                sha.TransformFinalBlock(bytes, 0, bytes.Length);
                return BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant();
            }
        }

        private static byte[] Fetch(string url, long most)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.UserAgent = "Capri-Desktop";
            request.Accept = url.Contains("/blobs/") ? "application/vnd.github.raw+json" : "application/vnd.github+json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (MemoryStream kept = new MemoryStream())
            {
                byte[] chunk = new byte[81920];
                int read;
                while ((read = body.Read(chunk, 0, chunk.Length)) > 0)
                {
                    if (kept.Length + read > most) throw new ModException("That's too big to come from the mods directory.");
                    kept.Write(chunk, 0, read);
                }
                return kept.ToArray();
            }
        }

        private static byte[] Push(string url, string json)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.UserAgent = "Capri-Desktop";
            request.ContentType = "application/json";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 15000;
            byte[] sent = Encoding.UTF8.GetBytes(json);
            using (Stream body = request.GetRequestStream()) body.Write(sent, 0, sent.Length);
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream())
            using (MemoryStream kept = new MemoryStream())
            {
                body.CopyTo(kept);
                return kept.ToArray();
            }
        }

        internal static void Clean(string file)
        {
            try { Directory.Delete(Path.GetDirectoryName(file), true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
