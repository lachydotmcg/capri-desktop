using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    // A reason she can say out loud. Anything else that goes wrong is told as a plain connection problem.
    internal sealed class AppImageUpdateException : Exception
    {
        internal AppImageUpdateException(string message) : base(message) { }
    }

    internal sealed class LinuxUpdateReady
    {
        internal string Version;
        internal string Path;
        internal string AppImage;
        internal string Hash;
        internal long Size;
    }

    internal sealed class LinuxUpdatePlan
    {
        internal string Version;
        internal string AppUrl;
        internal string SumsUrl;
        internal long Size;
    }

    // Kelv's AppImage updater (12 Nov). Only a Capri started straight from its AppImage replaces itself; a tarball, a
    // package or a source build belongs to whoever put it there, so those still get the release page. Like Windows, she
    // downloads quietly when a newer release is out, and nothing changes on disk beside the AppImage until they pick
    // Restart Capri to update.
    internal static class LinuxAppImageUpdater
    {
        internal const string AppName = "Capri-linux-x64.AppImage";
        internal const string SumsName = "SHA256SUMS.txt";
        private const long Largest = 512L * 1024 * 1024;
        private static readonly string[] RuntimeNames = { "APPIMAGE", "APPDIR", "OWD", "ARGV0", "APPIMAGE_UUID" };

        [DataContract]
        private sealed class ReleaseDocument
        {
            [DataMember(Name = "tag_name")] internal string Tag;
            [DataMember(Name = "draft")] internal bool Draft;
            [DataMember(Name = "prerelease")] internal bool Prerelease;
            [DataMember(Name = "assets")] internal ReleaseFile[] Assets;
        }

        [DataContract]
        private sealed class ReleaseFile
        {
            [DataMember(Name = "name")] internal string Name;
            [DataMember(Name = "browser_download_url")] internal string Url;
            [DataMember(Name = "size")] internal long Size;
        }

        // The AppImage she was started from, or null when she wasn't started from one.
        internal static string Image()
        {
            if (!OperatingSystem.IsLinux()) return null;
            string image = Environment.GetEnvironmentVariable("APPIMAGE");
            if (string.IsNullOrEmpty(image) || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APPDIR")) || !Path.IsPathRooted(image)) return null;
            if (!string.Equals(Limbs.SelfPath(), image, StringComparison.Ordinal) || !File.Exists(image)) return null;
            return Path.GetFullPath(image);
        }

        // Null when the release isn't newer than mine. Throws for a release she shouldn't install.
        internal static LinuxUpdatePlan Pick(string json, Version mine)
        {
            ReleaseDocument release = null;
            try { using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(json ?? ""))) release = (ReleaseDocument)new DataContractJsonSerializer(typeof(ReleaseDocument)).ReadObject(input); }
            catch (Exception) { }
            if (release == null || Pet.ReleaseVersion(release.Tag) == null) throw new AppImageUpdateException("GitHub's answer didn't name a Capri version.");
            if (release.Draft || release.Prerelease) throw new AppImageUpdateException("That release isn't finished yet.");
            string version = Pet.Newer(release.Tag, mine);
            if (version == null) return null;
            ReleaseFile app = Find(release.Assets, AppName), sums = Find(release.Assets, SumsName);
            if (app == null) throw new AppImageUpdateException("Capri " + version + " has no Linux AppImage.");
            if (sums == null) throw new AppImageUpdateException("Capri " + version + " has no checksum list, so I won't install it.");
            if (app.Size <= 0 || app.Size > Largest) throw new AppImageUpdateException("The AppImage's size looks wrong, so I won't install it.");
            if (!Official(app.Url) || !Official(sums.Url)) throw new AppImageUpdateException("The AppImage isn't on Capri's GitHub releases, so I won't install it.");
            return new LinuxUpdatePlan { Version = version, AppUrl = app.Url, SumsUrl = sums.Url, Size = app.Size };
        }

        private static ReleaseFile Find(ReleaseFile[] assets, string name)
        {
            ReleaseFile found = null;
            if (assets == null) return null;
            foreach (ReleaseFile asset in assets)
                if (asset != null && string.Equals(asset.Name, name, StringComparison.Ordinal))
                {
                    if (found != null) throw new AppImageUpdateException("The release lists " + name + " twice, so I won't install it.");
                    found = asset;
                }
            return found;
        }

        // The same addresses the Windows updater takes: https on github.com, under this repo's release downloads.
        internal static bool Official(string address)
        {
            Uri uri;
            return Uri.TryCreate(address, UriKind.Absolute, out uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com" && uri.IsDefaultPort
                && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0 && uri.Query.Length == 0
                && uri.AbsolutePath.StartsWith("/lachydotmcg/capri-desktop/releases/download/", StringComparison.Ordinal)
                && !uri.AbsolutePath.Contains("/../", StringComparison.Ordinal);
        }

        // GitHub sends release files on to its download host. Like Windows, she follows a move only to another https address.
        internal static Uri Moved(Uri from, string location)
        {
            Uri next;
            if (string.IsNullOrEmpty(location) || !Uri.TryCreate(from, location, out next) || next.Scheme != Uri.UriSchemeHttps || next.UserInfo.Length != 0)
                throw new AppImageUpdateException("The download moved somewhere I won't follow.");
            return next;
        }

        internal static string Checksum(string text, string name)
        {
            string found = null;
            foreach (string line in (text ?? "").Split('\n'))
            {
                Match match = Regex.Match(line.TrimEnd('\r'), @"^([a-fA-F0-9]{64})[ \t]+\*?(.+)$");
                if (!match.Success || !string.Equals(match.Groups[2].Value, name, StringComparison.Ordinal)) continue;
                if (found != null) throw new AppImageUpdateException("The checksum list names the AppImage twice, so I won't install it.");
                found = match.Groups[1].Value.ToLowerInvariant();
            }
            if (found == null) throw new AppImageUpdateException("The checksum list doesn't include the AppImage, so I won't install it.");
            return found;
        }

        // Downloads and checks the newer AppImage into her data folder. Null when there's nothing newer.
        internal static LinuxUpdateReady Prepare(string json, string dataFolder, Version mine)
        {
            string image = Image();
            if (image == null) return null;
            string directory = Path.Combine(dataFolder, "updates");
            LinuxUpdatePlan plan = Pick(json, mine);
            if (plan == null) { Tidy(directory, null); return null; }
            string sums;
            using (MemoryStream memory = new MemoryStream())
            {
                Fetch(plan.SumsUrl, memory, 1024 * 1024, -1);
                sums = Encoding.UTF8.GetString(memory.ToArray());
            }
            string hash = Checksum(sums, AppName);
            Directory.CreateDirectory(directory);
            string destination = Path.Combine(directory, "Capri-" + plan.Version + ".AppImage");
            Tidy(directory, destination);
            LinuxUpdateReady ready = new LinuxUpdateReady { Version = plan.Version, Path = destination, AppImage = image, Hash = hash, Size = plan.Size };
            if (File.Exists(destination))
            {
                if (Matches(destination, plan.Size, hash)) return ready;
                File.Delete(destination);
            }
            string partial = destination + ".partial";
            try
            {
                using (FileStream target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    Fetch(plan.AppUrl, target, plan.Size, plan.Size);
                    target.Flush(true);
                }
                if (!Matches(partial, plan.Size, hash)) throw new AppImageUpdateException("The download didn't match its checksum, so I won't install it.");
                File.Move(partial, destination, true);
                return ready;
            }
            finally { if (File.Exists(partial)) File.Delete(partial); }
        }

        // Older downloads and leftover partial files go, so the folder holds one AppImage at most.
        private static void Tidy(string directory, string keep)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string file in Directory.GetFiles(directory, "Capri-*.AppImage*"))
                if (!string.Equals(file, keep, StringComparison.Ordinal))
                    try { File.Delete(file); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private static void Fetch(string address, Stream target, long limit, long expected)
        {
            if (!Official(address)) throw new AppImageUpdateException("The AppImage isn't on Capri's GitHub releases, so I won't install it.");
            Uri uri = new Uri(address);
            using (CancellationTokenSource stop = new CancellationTokenSource(TimeSpan.FromMinutes(10)))
            using (HttpClientHandler handler = new HttpClientHandler { AllowAutoRedirect = false })
            using (HttpClient client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) })
            {
                client.DefaultRequestHeaders.UserAgent.ParseAdd("Capri-Desktop");
                for (int hop = 0; hop < 6; hop++)
                {
                    using (HttpResponseMessage response = client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, stop.Token).GetAwaiter().GetResult())
                    {
                        int status = (int)response.StatusCode;
                        if (status == 301 || status == 302 || status == 303 || status == 307 || status == 308)
                        {
                            uri = Moved(uri, response.Headers.Location == null ? null : response.Headers.Location.OriginalString);
                            continue;
                        }
                        long? length = response.Content.Headers.ContentLength;
                        if (status != 200 || (length.HasValue && length.Value > limit) || (expected >= 0 && length.HasValue && length.Value != expected))
                            throw new AppImageUpdateException("The download didn't come through whole. I'll try again later.");
                        using (Stream source = response.Content.ReadAsStream(stop.Token))
                        {
                            byte[] buffer = new byte[65536];
                            long total = 0;
                            int count;
                            while ((count = source.ReadAsync(buffer, 0, buffer.Length, stop.Token).GetAwaiter().GetResult()) > 0)
                            {
                                total += count;
                                if (total > limit) throw new AppImageUpdateException("The download was bigger than the release said, so I stopped it.");
                                target.Write(buffer, 0, count);
                            }
                            if ((expected >= 0 && total != expected) || (length.HasValue && total != length.Value))
                                throw new AppImageUpdateException("The download didn't come through whole. I'll try again later.");
                        }
                        return;
                    }
                }
            }
            throw new AppImageUpdateException("The download moved too many times.");
        }

        internal static bool Matches(string path, long size, string hash)
        {
            using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (SHA256 sha = SHA256.Create())
                return file.Length == size && string.Equals(Convert.ToHexString(sha.ComputeHash(file)), hash, StringComparison.OrdinalIgnoreCase);
        }

        // Copies the checked AppImage beside the old one while she's still running, so a folder she can't write to or a full
        // disk is said out loud instead of after she's gone. Then a small sh script waits for her to close, swaps the two
        // files, and starts the new one.
        internal static void Install(LinuxUpdateReady update, int parent, string log)
        {
            if (update == null || parent <= 0 || !File.Exists(update.Path)) throw new AppImageUpdateException("The download is gone. Check for updates again.");
            if (!Matches(update.Path, update.Size, update.Hash)) throw new AppImageUpdateException("The download didn't match its checksum, so I won't install it. Check for updates again.");
            string app = update.AppImage;
            if (!File.Exists(app)) throw new AppImageUpdateException("The AppImage I started from isn't there any more.");
            string folder = Path.GetDirectoryName(app);
            string next = app + ".capri-new";
            try
            {
                if (File.Exists(next)) File.Delete(next);
                using (FileStream source = new FileStream(update.Path, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (FileStream target = new FileStream(next, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    source.CopyTo(target);
                    target.Flush(true);
                }
                File.SetUnixFileMode(next, File.GetUnixFileMode(app) | UnixFileMode.UserRead | UnixFileMode.UserExecute);
            }
            catch (UnauthorizedAccessException) { Drop(next); throw new AppImageUpdateException("I can't write to " + folder + ", where the AppImage is."); }
            catch (IOException) { Drop(next); throw new AppImageUpdateException("I couldn't copy the update next to the AppImage. The disk might be full."); }
            if (!Matches(next, update.Size, update.Hash)) { Drop(next); throw new AppImageUpdateException("The copy next to the AppImage didn't match its checksum, so I left the old one alone."); }
            ProcessStartInfo start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
            foreach (string part in new[] { "-c", Script, "capri-update", app, next, parent.ToString(System.Globalization.CultureInfo.InvariantCulture), log ?? "/dev/null" })
                start.ArgumentList.Add(part);
            string appDir = Environment.GetEnvironmentVariable("APPDIR");
            Clean(start.Environment, appDir, appDir != null && appDir.Contains("/appimage_extracted_", StringComparison.Ordinal));
            Process helper = null;
            try { helper = Process.Start(start); }
            catch (Exception) { }
            if (helper == null) { Drop(next); throw new AppImageUpdateException("The update helper didn't start, so nothing changed."); }
            helper.Dispose();
        }

        private static void Drop(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        // The new AppImage starts clean: none of the old one's runtime variables, and nothing that points into its mount,
        // which is gone once she closes. An extract-and-run start keeps that mode, since FUSE may be missing.
        internal static void Clean(IDictionary<string, string> environment, string appDir, bool extracted)
        {
            foreach (string name in RuntimeNames) environment.Remove(name);
            string mount = (appDir ?? "").TrimEnd('/');
            if (mount.Length > 1 && mount.StartsWith("/", StringComparison.Ordinal))
                foreach (string name in new List<string>(environment.Keys))
                {
                    string value = environment[name];
                    if (value == null) continue;
                    List<string> kept = new List<string>();
                    bool changed = false;
                    foreach (string entry in value.Split(':'))
                    {
                        if (entry == mount || entry.StartsWith(mount + "/", StringComparison.Ordinal)) changed = true;
                        else kept.Add(entry);
                    }
                    if (!changed) continue;
                    if (kept.Count == 0 || (kept.Count == 1 && kept[0].Length == 0)) environment.Remove(name);
                    else environment[name] = string.Join(":", kept);
                }
            if (extracted) environment["APPIMAGE_EXTRACT_AND_RUN"] = "1";
        }

        // $1 the AppImage, $2 the checked copy beside it, $3 her process id, $4 the log. Every path is quoted and every
        // file command takes --, so spaces and odd characters stay part of the name.
        internal const string Script = @"app=$1
next=$2
parent=$3
log=$4
backup=""$app.capri-backup.$$""
note() { printf '%s %s\n' ""$(date '+%Y-%m-%d %H:%M:%S')"" ""$*"" >>""$log"" 2>/dev/null || :; }
launch() { nohup ""$1"" >/dev/null 2>&1 & child=$!; }
waited=0
while kill -0 ""$parent"" 2>/dev/null; do
    waited=$((waited + 1))
    if [ ""$waited"" -gt 600 ]; then
        rm -f -- ""$next""
        note ""Capri was still open after 2 minutes, so nothing changed.""
        exit 1
    fi
    sleep 0.2 2>/dev/null || sleep 1
done
if ! mv -f -- ""$app"" ""$backup""; then
    rm -f -- ""$next""
    note ""Couldn't move $app aside, so nothing changed.""
    launch ""$app""
    exit 1
fi
if ! mv -f -- ""$next"" ""$app""; then
    mv -f -- ""$backup"" ""$app""
    rm -f -- ""$next""
    note ""Couldn't put the new AppImage in place, so the old one is back.""
    launch ""$app""
    exit 1
fi
launch ""$app""
sleep 4
if kill -0 ""$child"" 2>/dev/null; then
    rm -f -- ""$backup""
    note ""Updated $app.""
    exit 0
fi
if mv -f -- ""$backup"" ""$app""; then
    note ""The new version closed within 4 seconds, so the old one is back.""
else
    note ""The new version closed within 4 seconds. The old one is at $backup.""
fi
launch ""$app""
exit 1
";

        // Window-free, with no network: addresses, the checksum list, which releases she takes, the environment the new
        // AppImage starts with, and on Linux and Mac the swap script itself on fake AppImages in a temp folder.
        internal static void Checks(List<string> checks)
        {
            string[] good = {
                "https://github.com/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://github.com/lachydotmcg/capri-desktop/releases/download/v0.9.1/SHA256SUMS.txt",
            };
            string[] bad = {
                null, "", "not a url",
                "http://github.com/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://github.com:8443/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://user@github.com/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://github.com.evil.example/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://evil.example/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://github.com/someone/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
                "https://github.com/lachydotmcg/capri-desktop/releases/download/../../someone/x/releases/download/a",
                "https://github.com/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage#x",
                "https://github.com/lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage?x=1",
                "https://github.com/lachydotmcg/capri-desktop/archive/v0.9.1.zip",
                "file:///lachydotmcg/capri-desktop/releases/download/v0.9.1/Capri-linux-x64.AppImage",
            };
            foreach (string address in good) if (!Official(address)) throw new Exception("A real release address was refused: " + address);
            foreach (string address in bad) if (Official(address)) throw new Exception("An address outside Capri's releases was taken: " + address);
            Uri asset = new Uri(good[0]);
            if (Moved(asset, "https://release-assets.githubusercontent.com/x?sig=1").Host != "release-assets.githubusercontent.com" || Moved(asset, "/elsewhere").AbsoluteUri != "https://github.com/elsewhere")
                throw new Exception("A move to an https address was refused.");
            foreach (string location in new[] { null, "", "http://release-assets.githubusercontent.com/x", "https://a:b@release-assets.githubusercontent.com/x", "ftp://example.com/x" })
            {
                bool refused = false;
                try { Moved(asset, location); } catch (AppImageUpdateException) { refused = true; }
                if (!refused) throw new Exception("A move she shouldn't follow was taken: " + location);
            }
            checks.Add("PASS appimage update addresses: only https github.com under capri-desktop's release downloads, with no port, user, query or fragment; a move goes only to https");

            string a = new string('a', 64), b = new string('B', 64), c = new string('c', 64);
            var sums = new Dictionary<string, string>
            {
                { a + " Capri.exe\n" + b + " Capri-linux-x64.AppImage\n", b.ToLowerInvariant() },
                { a + "  Capri.exe\r\n" + c + "  Capri-linux-x64.AppImage\r\n", c },
                { c + " *Capri-linux-x64.AppImage", c },
                { a + "\tCapri-linux-x64.AppImage\n" + b + " Capri-linux-x64.AppImage.zsync\n", a },
            };
            foreach (KeyValuePair<string, string> pair in sums)
                if (Checksum(pair.Key, AppName) != pair.Value) throw new Exception("The checksum list was misread: " + pair.Key.Replace("\n", "|"));
            foreach (string text in new[] { "", null, a + " Capri.exe\n", a + " Capri-linux-x64.AppImage\n" + b + " Capri-linux-x64.AppImage\n", "abc Capri-linux-x64.AppImage\n", a + " capri-linux-x64.appimage\n" })
            {
                bool refused = false;
                try { Checksum(text, AppName); } catch (AppImageUpdateException) { refused = true; }
                if (!refused) throw new Exception("A checksum list without exactly one AppImage line was taken: " + (text ?? "null").Replace("\n", "|"));
            }
            checks.Add("PASS appimage checksum list: one or two spaces, a tab, CRLF and a leading star read; the name missing, listed twice or in other case is refused");

            Version mine = new Version(0, 9, 0, 0);
            Func<string, string, string> release = (tag, rest) => "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"assets\":[" + rest + "]}";
            Func<string, long, string> file = (name, size) => "{\"name\":\"" + name + "\",\"size\":" + size + ",\"browser_download_url\":\"https://github.com/lachydotmcg/capri-desktop/releases/download/v9/" + name + "\"}";
            string both = file(AppName, 59394552) + "," + file(SumsName, 596);
            foreach (string tag in new[] { "v0.8.9", "v0.9.0", "0.9.0.0", "v0.1.12" })
                if (Pick(release(tag, both), mine) != null) throw new Exception("An older or the same release was offered: " + tag);
            LinuxUpdatePlan plan = Pick(release("v0.9.1", both), mine);
            if (plan == null || plan.Version != "0.9.1" || plan.Size != 59394552 || !plan.AppUrl.EndsWith("/" + AppName, StringComparison.Ordinal) || !plan.SumsUrl.EndsWith("/" + SumsName, StringComparison.Ordinal))
                throw new Exception("A newer release wasn't picked up whole.");
            if (Pick(release("v0.9.0.1", both), mine)?.Version != "0.9.0.1" || Pick(release("V1.0", both), mine)?.Version != "1.0.0") throw new Exception("A four-part or two-part newer version was missed.");
            var refusals = new Dictionary<string, string>
            {
                { "junk tag", release("nightly", both) }, { "suffix tag", release("v0.9.1-beta", both) }, { "five parts", release("v0.9.1.0.1", both) },
                { "no tag", "{\"assets\":[" + both + "]}" }, { "not json", "not json" }, { "nothing", null },
                { "draft", release("v0.9.1", both).Replace("\"draft\":false", "\"draft\":true") },
                { "prerelease", release("v0.9.1", both).Replace("\"prerelease\":false", "\"prerelease\":true") },
                { "no appimage", release("v0.9.1", file(SumsName, 596)) },
                { "no checksum list", release("v0.9.1", file(AppName, 59394552)) },
                { "appimage twice", release("v0.9.1", both + "," + file(AppName, 59394552)) },
                { "checksum list twice", release("v0.9.1", both + "," + file(SumsName, 596)) },
                { "empty appimage", release("v0.9.1", file(AppName, 0) + "," + file(SumsName, 596)) },
                { "oversize appimage", release("v0.9.1", file(AppName, Largest + 1) + "," + file(SumsName, 596)) },
                { "foreign address", release("v0.9.1", both.Replace("https://github.com/lachydotmcg/", "https://github.com/someone/")) },
            };
            foreach (KeyValuePair<string, string> pair in refusals)
            {
                bool refused = false;
                try { Pick(pair.Value, mine); } catch (AppImageUpdateException) { refused = true; }
                if (!refused) throw new Exception("A release she shouldn't install was taken: " + pair.Key);
            }
            checks.Add("PASS appimage releases: older, same, newer, four-part and junk versions; a draft, a prerelease, a missing AppImage or checksum list, either listed twice, and an empty or oversize AppImage are refused");

            string folder = Path.Combine(Path.GetTempPath(), "capri-appimage-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string sample = Path.Combine(folder, "sample.AppImage");
                File.WriteAllText(sample, "capri");
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("capri")));
                if (!Matches(sample, 5, hash.ToLowerInvariant()) || Matches(sample, 6, hash) || Matches(sample, 5, new string('0', 64))) throw new Exception("The size and SHA-256 check disagreed with a known file.");
                checks.Add("PASS appimage download check: the right size and SHA-256 pass, a wrong size or hash fails");

                string mount = "/tmp/.mount_CapriAb12";
                var environment = new Dictionary<string, string>
                {
                    { "APPIMAGE", "/home/me/Apps/Capri-linux-x64.AppImage" }, { "APPDIR", mount }, { "OWD", "/home/me" }, { "ARGV0", "./Capri" }, { "APPIMAGE_UUID", "x" },
                    { "PATH", mount + "/usr/bin:/usr/local/bin:/usr/bin" }, { "LD_LIBRARY_PATH", mount + "/usr/lib" }, { "XDG_DATA_DIRS", "/usr/share:" + mount + "/usr/share" },
                    { "PYTHONHOME", mount }, { "NEAR", mount + "Z/usr/bin" }, { "HOME", "/home/me" }, { "WAYLAND_DISPLAY", "wayland-1" },
                };
                Clean(environment, mount + "/", false);
                string left = string.Join(",", new List<string>(environment.Keys));
                if (left != "PATH,XDG_DATA_DIRS,NEAR,HOME,WAYLAND_DISPLAY" || environment["PATH"] != "/usr/local/bin:/usr/bin" || environment["XDG_DATA_DIRS"] != "/usr/share" || environment["NEAR"] != mount + "Z/usr/bin")
                    throw new Exception("The new AppImage would start with the old one's variables: " + left);
                var plain = new Dictionary<string, string> { { "PATH", "/usr/bin:/bin" } };
                Clean(plain, "/", true);
                if (plain["PATH"] != "/usr/bin:/bin" || plain["APPIMAGE_EXTRACT_AND_RUN"] != "1") throw new Exception("A root APPDIR emptied PATH, or extract-and-run was lost.");
                checks.Add("PASS appimage restart environment: the runtime's variables and anything inside the old mount go, PATH keeps the rest, extract-and-run carries over");

                if (OperatingSystem.IsWindows()) { checks.Add("LIMIT appimage swap script runs only on Linux and Mac"); return; }
                Process syntax = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-n", "-c", Script }, UseShellExecute = false });
                if (!syntax.WaitForExit(10000) || syntax.ExitCode != 0) throw new Exception("sh -n rejected the swap script.");
                string odd = Path.Combine(folder, "dir with 'quotes' and -dash");
                Directory.CreateDirectory(odd);
                string app = Path.Combine(odd, "-Capri it's.AppImage"), log = Path.Combine(folder, "update.log");
                Func<string, string, int> swap = (fresh, version) =>
                {
                    File.WriteAllText(app, "#!/bin/sh\nsleep 6\n");
                    File.SetUnixFileMode(app, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    File.WriteAllText(app + ".capri-new", fresh);
                    File.SetUnixFileMode(app + ".capri-new", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    Process parent = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-c", "sleep 1" }, UseShellExecute = false });
                    var start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false };
                    foreach (string part in new[] { "-c", Script, "capri-update", app, app + ".capri-new", parent.Id.ToString(), log }) start.ArgumentList.Add(part);
                    Process helper = Process.Start(start);
                    if (!helper.WaitForExit(30000)) { helper.Kill(); throw new Exception("The swap script didn't finish (" + version + ")."); }
                    parent.WaitForExit(1000);
                    return helper.ExitCode;
                };
                string working = "#!/bin/sh\n# new\nsleep 6\n";
                if (swap(working, "working") != 0 || File.ReadAllText(app) != working || File.Exists(app + ".capri-new") || Directory.GetFiles(odd, "*capri-backup*").Length != 0 || !File.ReadAllText(log).Contains("Updated "))
                    throw new Exception("The swap script didn't put a working AppImage in place cleanly: " + File.ReadAllText(log));
                string broken = "#!/bin/sh\nexit 3\n";
                if (swap(broken, "broken") != 1 || File.ReadAllText(app) != "#!/bin/sh\nsleep 6\n" || File.Exists(app + ".capri-new") || Directory.GetFiles(odd, "*capri-backup*").Length != 0 || !File.ReadAllText(log).Contains("old one is back"))
                    throw new Exception("The swap script didn't roll a broken AppImage back: " + File.ReadAllText(log));
                checks.Add("PASS appimage swap script: sh -n passes; with spaces, quotes and a leading dash in the path it waits for her, swaps, starts the new one and keeps it, or puts the old one back when the new one dies");
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
