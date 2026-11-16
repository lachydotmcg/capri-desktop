using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class MacAppUpdater
    {
        internal const string SiliconName = "Capri-mac-apple-silicon.zip";
        internal const string IntelName = "Capri-mac-intel.zip";
        internal const string BundleName = "Capri.app";
        private const long Largest = 512L * 1024 * 1024;
        private const string Outside = "The Mac download isn't on Capri's GitHub releases, so I won't install it.";

        internal static string ZipName(Architecture arch)
        {
            return arch == Architecture.Arm64 ? SiliconName : arch == Architecture.X64 ? IntelName : null;
        }

        internal static string Image()
        {
            if (!OperatingSystem.IsMacOS()) return null;
            return Bundle(Environment.ProcessPath);
        }

        internal static string Bundle(string executable)
        {
            if (string.IsNullOrEmpty(executable) || !Path.IsPathRooted(executable)) return null;
            string full = Path.GetFullPath(executable);
            string macOS = Path.GetDirectoryName(full);
            string contents = macOS == null ? null : Path.GetDirectoryName(macOS);
            string app = contents == null ? null : Path.GetDirectoryName(contents);
            if (app == null || Path.GetFileName(macOS) != "MacOS" || Path.GetFileName(contents) != "Contents" || !app.EndsWith(".app", StringComparison.Ordinal)) return null;
            if (app.Contains("/AppTranslocation/", StringComparison.Ordinal) || app.StartsWith("/Volumes/", StringComparison.Ordinal)) return null;
            string info = Path.Combine(contents, "Info.plist");
            if (!File.Exists(full) || !File.Exists(info)) return null;
            string id;
            try { id = PlistValue(File.ReadAllText(info), "CFBundleIdentifier"); }
            catch (Exception) { return null; }
            return id == Limbs.MacBundle ? app : null;
        }

        internal static string PlistValue(string plist, string key)
        {
            Match match = Regex.Match(plist ?? "", "<key>" + Regex.Escape(key) + @"</key>\s*<string>([^<]*)</string>");
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }

        internal static LinuxUpdatePlan Pick(string json, Version mine, string zipName, Func<string, bool> trusted = null)
        {
            Func<string, bool> allowed = trusted ?? LinuxAppImageUpdater.Official;
            LinuxAppImageUpdater.ReleaseDocument release = null;
            try { using (MemoryStream input = new MemoryStream(Encoding.UTF8.GetBytes(json ?? ""))) release = (LinuxAppImageUpdater.ReleaseDocument)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(LinuxAppImageUpdater.ReleaseDocument)).ReadObject(input); }
            catch (Exception) { }
            if (release == null || Pet.ReleaseVersion(release.Tag) == null) throw new AppImageUpdateException("GitHub's answer didn't name a Capri version.");
            if (release.Draft || release.Prerelease) throw new AppImageUpdateException("That release isn't finished yet.");
            string version = Pet.Newer(release.Tag, mine);
            if (version == null) return null;
            if (zipName == null) throw new AppImageUpdateException("Capri " + version + " has no download for this kind of Mac.");
            LinuxAppImageUpdater.ReleaseFile zip = LinuxAppImageUpdater.Find(release.Assets, zipName), sums = LinuxAppImageUpdater.Find(release.Assets, LinuxAppImageUpdater.SumsName);
            if (zip == null) throw new AppImageUpdateException("Capri " + version + " has no " + zipName + ".");
            if (sums == null) throw new AppImageUpdateException("Capri " + version + " has no checksum list, so I won't install it.");
            if (zip.Size <= 0 || zip.Size > Largest) throw new AppImageUpdateException("The Mac download's size looks wrong, so I won't install it.");
            if (!allowed(zip.Url) || !allowed(sums.Url)) throw new AppImageUpdateException(Outside);
            return new LinuxUpdatePlan { Version = version, AppUrl = zip.Url, SumsUrl = sums.Url, Size = zip.Size };
        }

        internal static LinuxUpdateReady Prepare(string json, string dataFolder, Version mine)
        {
            string app = Image();
            if (app == null) return null;
            return Prepare(json, dataFolder, mine, app, ZipName(RuntimeInformation.ProcessArchitecture), null);
        }

        internal static LinuxUpdateReady Prepare(string json, string dataFolder, Version mine, string app, string zipName, Func<string, bool> trusted)
        {
            if (app == null) return null;
            string directory = Path.Combine(dataFolder, "updates");
            LinuxUpdatePlan plan = Pick(json, mine, zipName, trusted);
            if (plan == null) { Tidy(directory, null); return null; }
            string sums;
            using (MemoryStream memory = new MemoryStream())
            {
                LinuxAppImageUpdater.Fetch(plan.SumsUrl, memory, 1024 * 1024, -1, trusted, Outside);
                sums = Encoding.UTF8.GetString(memory.ToArray());
            }
            string hash = LinuxAppImageUpdater.Checksum(sums, zipName, "the Mac download");
            Directory.CreateDirectory(directory);
            string stage = Path.Combine(directory, "Capri-" + plan.Version + "-mac");
            string mark = Path.Combine(stage, ".capri-zip");
            Tidy(directory, stage);
            string staged = Path.Combine(stage, BundleName);
            if (Directory.Exists(stage))
            {
                try
                {
                    if (File.Exists(mark) && File.ReadAllText(mark).Trim() == hash && Directory.Exists(staged))
                    {
                        Check(staged, plan.Version);
                        return new LinuxUpdateReady { Version = plan.Version, Path = staged, AppImage = app, Hash = TreeHash(staged), Size = -1, Mac = true };
                    }
                }
                catch (AppImageUpdateException) { }
                LinuxAppImageUpdater.Drop(stage);
            }
            string zip = stage + ".zip", partial = zip + ".partial", unpack = stage + ".partial";
            try
            {
                using (FileStream target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    LinuxAppImageUpdater.Fetch(plan.AppUrl, target, plan.Size, plan.Size, trusted, Outside);
                    target.Flush(true);
                }
                if (!LinuxAppImageUpdater.Matches(partial, plan.Size, hash)) throw new AppImageUpdateException("The download didn't match its checksum, so I won't install it.");
                File.Move(partial, zip, true);
                LinuxAppImageUpdater.Drop(unpack);
                if (Run("/usr/bin/ditto", 300, "-x", "-k", zip, unpack) != 0) throw new AppImageUpdateException("The Mac download wouldn't unzip, so I won't install it.");
                Check(Path.Combine(unpack, BundleName), plan.Version);
                File.WriteAllText(Path.Combine(unpack, ".capri-zip"), hash);
                Directory.Move(unpack, stage);
                return new LinuxUpdateReady { Version = plan.Version, Path = staged, AppImage = app, Hash = TreeHash(staged), Size = -1, Mac = true };
            }
            finally
            {
                LinuxAppImageUpdater.Drop(partial);
                LinuxAppImageUpdater.Drop(zip);
                LinuxAppImageUpdater.Drop(unpack);
            }
        }

        internal static void Check(string app, string version)
        {
            string info = Path.Combine(app, "Contents", "Info.plist");
            if (!File.Exists(info) || !File.Exists(Path.Combine(app, "Contents", "MacOS", "Capri"))) throw new AppImageUpdateException("The Mac download has no Capri.app inside, so I won't install it.");
            string plist = File.ReadAllText(info);
            if (PlistValue(plist, "CFBundleIdentifier") != Limbs.MacBundle) throw new AppImageUpdateException("The Mac download isn't Capri, so I won't install it.");
            Version inside = Pet.ReleaseVersion(PlistValue(plist, "CFBundleShortVersionString")), wanted = Pet.ReleaseVersion(version);
            if (inside == null || wanted == null || inside != wanted) throw new AppImageUpdateException("The Mac download says it's a different version, so I won't install it.");
            if (OperatingSystem.IsMacOS() && Run("/usr/bin/codesign", 120, "--verify", "--deep", app) != 0) throw new AppImageUpdateException("The Mac download's signature didn't check out, so I won't install it.");
        }

        private static void Tidy(string directory, string keep)
        {
            if (!Directory.Exists(directory)) return;
            foreach (string path in Directory.GetFileSystemEntries(directory, "Capri-*-mac*"))
                if (!string.Equals(path, keep, StringComparison.Ordinal)) LinuxAppImageUpdater.Drop(path);
        }

        internal static int Run(string program, int seconds, params string[] args)
        {
            ProcessStartInfo start = new ProcessStartInfo(program) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (string arg in args) start.ArgumentList.Add(arg);
            using (Process process = Process.Start(start))
            {
                process.OutputDataReceived += delegate { };
                process.ErrorDataReceived += delegate { };
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                if (!process.WaitForExit(seconds * 1000)) { try { process.Kill(true); } catch (Exception) { } return -1; }
                return process.ExitCode;
            }
        }

        internal static string TreeHash(string root)
        {
            using (IncrementalHash sum = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                Walk(sum, root, root);
                return Convert.ToHexString(sum.GetHashAndReset());
            }
        }

        private static void Walk(IncrementalHash sum, string root, string folder)
        {
            List<string> entries = new List<string>(Directory.GetFileSystemEntries(folder));
            entries.Sort(StringComparer.Ordinal);
            foreach (string path in entries)
            {
                string name = Path.GetRelativePath(root, path);
                FileInfo info = new FileInfo(path);
                if (info.LinkTarget != null)
                {
                    Add(sum, "L\0" + name + "\0" + info.LinkTarget + "\n");
                    continue;
                }
                if (Directory.Exists(path))
                {
                    Add(sum, "D\0" + name + "\n");
                    Walk(sum, root, path);
                    continue;
                }
                bool runs = !OperatingSystem.IsWindows() && (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
                Add(sum, "F\0" + name + "\0" + info.Length + "\0" + (runs ? "x" : "-") + "\0");
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                    sum.AppendData(SHA256.HashData(file));
                Add(sum, "\n");
            }
        }

        private static void Add(IncrementalHash sum, string text) { sum.AppendData(Encoding.UTF8.GetBytes(text)); }

        internal static Process Install(LinuxUpdateReady update, int parent, string log)
        {
            if (update == null || parent <= 0 || !Directory.Exists(update.Path)) throw new AppImageUpdateException("The download is gone. Check for updates again.");
            if (TreeHash(update.Path) != update.Hash) throw new AppImageUpdateException("The download changed since I checked it, so I won't install it. Check for updates again.");
            string app = update.AppImage;
            if (!Directory.Exists(app)) throw new AppImageUpdateException("The Capri.app I started from isn't there any more.");
            string folder = Path.GetDirectoryName(app);
            string next = app + ".capri-new";
            try
            {
                LinuxAppImageUpdater.Drop(next);
                string probe = Path.Combine(folder, ".capri-write-" + Guid.NewGuid().ToString("N"));
                using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                if (LinuxAppImageUpdater.ReadOnlyError(error)) throw new AppImageUpdateException(folder + " is read-only for me, so I can't swap Capri.app there.", true);
                if (LinuxAppImageUpdater.FullError(error)) throw new AppImageUpdateException("The disk with Capri.app is full, so I couldn't copy the update next to it.");
                throw new AppImageUpdateException("I couldn't copy the update next to Capri.app.");
            }
            if (Run("/usr/bin/ditto", 300, update.Path, next) != 0) { LinuxAppImageUpdater.Drop(next); throw new AppImageUpdateException("I couldn't copy the update next to Capri.app. The disk may be full."); }
            if (TreeHash(next) != update.Hash) { LinuxAppImageUpdater.Drop(next); throw new AppImageUpdateException("The copy next to Capri.app didn't match the download, so I left the old one alone."); }
            ProcessStartInfo start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
            foreach (string part in new[] { "-c", Script, "capri-update", app, next, parent.ToString(System.Globalization.CultureInfo.InvariantCulture), log ?? "/dev/null", update.Version })
                start.ArgumentList.Add(part);
            Process helper = null;
            try { helper = Process.Start(start); }
            catch (Exception) { }
            if (helper == null) { LinuxAppImageUpdater.Drop(next); throw new AppImageUpdateException("The update helper didn't start, so nothing changed."); }
            return helper;
        }

        internal const string Script = @"app=$1
next=$2
parent=$3
log=$4
version=$5
backup=""$app.capri-backup.$$""
broken=""$app.capri-broken.$$""
exe=""$app/Contents/MacOS/Capri""
note() { printf '%s %s\n' ""$(date '+%Y-%m-%d %H:%M:%S')"" ""$*"" >>""$log"" 2>/dev/null || :; }
launch() { /usr/bin/open -n ""$1"" >/dev/null 2>&1 || :; }
running() { ps -axo comm= 2>/dev/null | exe=""$exe"" awk 'BEGIN { e = ENVIRON[""exe""] } $0 == e { found = 1 } END { exit !found }'; }
waited=0
while kill -0 ""$parent"" 2>/dev/null; do
    waited=$((waited + 1))
    if [ ""$waited"" -gt 600 ]; then
        rm -rf -- ""$next""
        note ""Capri was still open after 2 minutes, so nothing changed.""
        exit 1
    fi
    sleep 0.2 2>/dev/null || sleep 1
done
if ! mv -f -- ""$app"" ""$backup""; then
    rm -rf -- ""$next""
    note ""Couldn't move $app aside, so nothing changed.""
    launch ""$app""
    exit 1
fi
if ! mv -f -- ""$next"" ""$app""; then
    mv -f -- ""$backup"" ""$app""
    rm -rf -- ""$next""
    note ""Capri $version didn't take: it couldn't be put in place, so the old one is back.""
    launch ""$app""
    exit 1
fi
launch ""$app""
seen=0
tries=0
while [ ""$tries"" -lt 20 ]; do
    if running; then seen=1; break; fi
    tries=$((tries + 1))
    sleep 0.5
done
if [ ""$seen"" -eq 1 ]; then
    sleep 4
    if running; then
        rm -rf -- ""$backup""
        note ""Updated $app to Capri $version.""
        exit 0
    fi
fi
if mv -f -- ""$app"" ""$broken"" && mv -f -- ""$backup"" ""$app""; then
    rm -rf -- ""$broken""
    note ""Capri $version didn't take: it closed within 4 seconds, so the old one is back.""
else
    note ""Capri $version closed within 4 seconds. The old one is at $backup.""
fi
launch ""$app""
exit 1
";

        internal static void Checks(List<string> checks)
        {
            if (ZipName(Architecture.Arm64) != SiliconName || ZipName(Architecture.X64) != IntelName || ZipName(Architecture.Arm) != null) throw new Exception("The Mac download was picked for the wrong chip.");
            string plist = "<plist><dict>\n  <key>CFBundleIdentifier</key>\n  <string>" + Limbs.MacBundle + "</string>\n  <key>CFBundleShortVersionString</key>\n\t<string> 0.9.3 </string>\n</dict></plist>";
            if (PlistValue(plist, "CFBundleIdentifier") != Limbs.MacBundle || PlistValue(plist, "CFBundleShortVersionString") != "0.9.3" || PlistValue(plist, "CFBundleVersion") != null || PlistValue(null, "x") != null)
                throw new Exception("Her Info.plist was misread.");
            checks.Add("PASS mac update picks: Apple Silicon and Intel each get their own zip, and her Info.plist's id and version read back");

            Version mine = new Version(0, 9, 2, 0);
            Func<string, string, string> release = (tag, rest) => "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"assets\":[" + rest + "]}";
            Func<string, long, string> file = (name, size) => "{\"name\":\"" + name + "\",\"size\":" + size + ",\"browser_download_url\":\"https://github.com/lachydotmcg/capri-desktop/releases/download/v9/" + name + "\"}";
            string all = file(SiliconName, 64267881) + "," + file(IntelName, 66487919) + "," + file(LinuxAppImageUpdater.AppName, 59673080) + "," + file(LinuxAppImageUpdater.SumsName, 596);
            if (Pick(release("v0.9.2", all), mine, SiliconName) != null || Pick(release("v0.9.1", all), mine, IntelName) != null) throw new Exception("An older or the same release was offered on Mac.");
            LinuxUpdatePlan silicon = Pick(release("v0.9.3", all), mine, SiliconName), intel = Pick(release("v0.9.3", all), mine, IntelName);
            if (silicon == null || silicon.Version != "0.9.3" || !silicon.AppUrl.EndsWith("/" + SiliconName, StringComparison.Ordinal) || silicon.Size != 64267881 || intel == null || !intel.AppUrl.EndsWith("/" + IntelName, StringComparison.Ordinal) || !silicon.SumsUrl.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal))
                throw new Exception("A newer Mac release wasn't picked up whole.");
            var refusals = new Dictionary<string, string[]>
            {
                { "draft", new[] { release("v0.9.3", all).Replace("\"draft\":false", "\"draft\":true"), SiliconName } },
                { "prerelease", new[] { release("v0.9.3", all).Replace("\"prerelease\":false", "\"prerelease\":true"), SiliconName } },
                { "no zip for this chip", new[] { release("v0.9.3", file(IntelName, 5) + "," + file(LinuxAppImageUpdater.SumsName, 596)), SiliconName } },
                { "no chip", new[] { release("v0.9.3", all), null } },
                { "no checksum list", new[] { release("v0.9.3", file(SiliconName, 5)), SiliconName } },
                { "zip twice", new[] { release("v0.9.3", all + "," + file(SiliconName, 5)), SiliconName } },
                { "empty zip", new[] { release("v0.9.3", file(SiliconName, 0) + "," + file(LinuxAppImageUpdater.SumsName, 596)), SiliconName } },
                { "oversize zip", new[] { release("v0.9.3", file(SiliconName, Largest + 1) + "," + file(LinuxAppImageUpdater.SumsName, 596)), SiliconName } },
                { "foreign address", new[] { release("v0.9.3", all.Replace("https://github.com/lachydotmcg/", "https://github.com/someone/")), SiliconName } },
                { "junk tag", new[] { release("nightly", all), SiliconName } },
            };
            foreach (KeyValuePair<string, string[]> pair in refusals)
            {
                bool refused = false;
                try { Pick(pair.Value[0], mine, pair.Value[1]); } catch (AppImageUpdateException) { refused = true; }
                if (!refused) throw new Exception("A Mac release she shouldn't install was taken: " + pair.Key);
            }
            string b = new string('b', 64);
            if (LinuxAppImageUpdater.Checksum(new string('a', 64) + "  " + IntelName + "\n" + b + "  " + SiliconName + "\n", SiliconName, "the Mac download") != b) throw new Exception("The Mac zip's checksum was misread.");
            checks.Add("PASS mac update releases: older and same are skipped; a draft, a prerelease, a missing zip for this chip or checksum list, a zip listed twice, an empty, oversize or foreign zip are refused");

            string folder = Path.Combine(Path.GetTempPath(), "capri-mac-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                string fake = Path.Combine(folder, "Apps", "Capri.app");
                Directory.CreateDirectory(Path.Combine(fake, "Contents", "MacOS"));
                File.WriteAllText(Path.Combine(fake, "Contents", "Info.plist"), plist);
                string exe = Path.Combine(fake, "Contents", "MacOS", "Capri");
                File.WriteAllText(exe, "x");
                if (Bundle(exe) != fake || Bundle(Path.Combine(folder, "Capri")) != null || Bundle(null) != null || Bundle("Capri.app/Contents/MacOS/Capri") != null) throw new Exception("Her own Capri.app wasn't found from her program.");
                File.WriteAllText(Path.Combine(fake, "Contents", "Info.plist"), plist.Replace(Limbs.MacBundle, "com.example.other"));
                if (Bundle(exe) != null) throw new Exception("Another app's bundle was taken for hers.");
                if (Bundle("/private/var/folders/x/AppTranslocation/ABC/d/Capri.app/Contents/MacOS/Capri") != null || Bundle("/Volumes/Capri/Capri.app/Contents/MacOS/Capri") != null) throw new Exception("A translocated or mounted Capri.app was offered a swap.");
                File.WriteAllText(Path.Combine(fake, "Contents", "Info.plist"), plist);
                string first = TreeHash(fake);
                if (TreeHash(fake) != first) throw new Exception("The bundle's fingerprint changed by itself.");
                File.WriteAllText(exe, "y");
                string changed = TreeHash(fake);
                File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                string runs = TreeHash(fake);
                File.WriteAllText(Path.Combine(fake, "Contents", "extra"), "");
                if (changed == first || runs == changed || TreeHash(fake) == runs) throw new Exception("The bundle's fingerprint missed a changed file, mode or a new file.");
                try { Check(fake, "0.9.3"); } catch (AppImageUpdateException) { if (!OperatingSystem.IsMacOS()) throw new Exception("A good bundle was refused."); }
                foreach (string[] bad in new[] { new[] { "0.9.4", plist }, new[] { "0.9.3", plist.Replace(Limbs.MacBundle, "com.example.other") }, new[] { "0.9.3", plist.Replace("0.9.3", "nightly") } })
                {
                    File.WriteAllText(Path.Combine(fake, "Contents", "Info.plist"), bad[1]);
                    bool refused = false;
                    try { Check(fake, bad[0]); } catch (AppImageUpdateException) { refused = true; }
                    if (!refused) throw new Exception("A bundle with the wrong version or id was taken.");
                }
                checks.Add("PASS mac update bundle: she finds her own Capri.app, not another app's or a translocated one, its fingerprint follows files and modes, and a wrong id or version is refused");

                if (OperatingSystem.IsWindows()) { checks.Add("LIMIT mac update swap runs only on Linux and Mac"); return; }
                Process syntax = Process.Start(new ProcessStartInfo("/bin/sh") { ArgumentList = { "-n", "-c", Script }, UseShellExecute = false });
                if (!syntax.WaitForExit(10000) || syntax.ExitCode != 0) throw new Exception("sh -n rejected the Mac swap script.");
                checks.Add("PASS mac update swap script: sh -n passes");
                if (!OperatingSystem.IsMacOS()) { checks.Add("LIMIT mac update download, swap and relaunch run only on a Mac"); return; }
                Loopback(checks, Path.Combine(folder, "loop"));
            }
            finally { try { Directory.Delete(folder, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        private const string FakeApp = "import AppKit\nlet app = NSApplication.shared\napp.setActivationPolicy(.accessory)\nDispatchQueue.main.asyncAfter(deadline: .now() + 15) { exit(0) }\napp.run()\n";

        private static void MakeApp(string app, string version, string executable, bool script)
        {
            LinuxAppImageUpdater.Drop(app);
            Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS"));
            Directory.CreateDirectory(Path.Combine(app, "Contents", "Resources"));
            File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n  <key>CFBundleName</key>\n  <string>Capri</string>\n  <key>CFBundleIdentifier</key>\n  <string>" + Limbs.MacBundle + "</string>\n  <key>CFBundleExecutable</key>\n  <string>Capri</string>\n  <key>CFBundlePackageType</key>\n  <string>APPL</string>\n  <key>CFBundleShortVersionString</key>\n  <string>" + version + "</string>\n  <key>CFBundleVersion</key>\n  <string>" + version + "</string>\n  <key>LSUIElement</key>\n  <true/>\n</dict>\n</plist>\n");
            File.WriteAllText(Path.Combine(app, "Contents", "Resources", "note.txt"), version);
            string exe = Path.Combine(app, "Contents", "MacOS", "Capri");
            if (script) File.WriteAllText(exe, "#!/bin/sh\nexit 3\n");
            else File.Copy(executable, exe, true);
            File.SetUnixFileMode(exe, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            if (Run("/usr/bin/codesign", 120, "--force", "--deep", "--sign", "-", app) != 0) throw new Exception("The fake Capri.app couldn't be signed.");
        }

        private static void StopFakes(string app)
        {
            string exe = Path.Combine(app, "Contents", "MacOS", "Capri");
            ProcessStartInfo start = new ProcessStartInfo("/bin/ps") { UseShellExecute = false, RedirectStandardOutput = true };
            foreach (string arg in new[] { "-axo", "pid=,comm=" }) start.ArgumentList.Add(arg);
            using (Process ps = Process.Start(start))
            {
                string text = ps.StandardOutput.ReadToEnd();
                ps.WaitForExit(5000);
                foreach (string line in text.Split('\n'))
                {
                    string trimmed = line.Trim();
                    int space = trimmed.IndexOf(' ');
                    int pid;
                    if (space > 0 && trimmed.Substring(space + 1).Trim() == exe && int.TryParse(trimmed.Substring(0, space), out pid))
                        try { Process.GetProcessById(pid).Kill(); } catch (Exception) { }
                }
            }
        }

        private static void Loopback(List<string> checks, string root)
        {
            string tools = Path.Combine(root, "tools");
            Directory.CreateDirectory(tools);
            string source = Path.Combine(tools, "fake.swift"), binary = Path.Combine(tools, "fake");
            File.WriteAllText(source, FakeApp);
            if (Run("/usr/bin/xcrun", 300, "swiftc", "-O", source, "-o", binary) != 0 || !File.Exists(binary)) { checks.Add("LIMIT mac update loopback needs swiftc to build a fake Capri.app"); return; }
            string zipName = ZipName(RuntimeInformation.ProcessArchitecture);
            string installed = Path.Combine(root, "Apps dir 'odd'", BundleName), data = Path.Combine(root, "data"), log = Path.Combine(data, "updates", "update.log");
            MakeApp(installed, "0.9.0", binary, false);
            Dictionary<string, byte[]> served = new Dictionary<string, byte[]>();
            int requests = 0;
            int port;
            using (System.Net.Sockets.TcpListener probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0)) { probe.Start(); port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop(); }
            string prefix = "http://127.0.0.1:" + port + "/";
            string downloads = prefix + "lachydotmcg/capri-desktop/releases/download/";
            Func<string, bool> trusted = address => address != null && address.StartsWith(downloads, StringComparison.Ordinal) && !address.Contains("..", StringComparison.Ordinal);
            HttpListener server = new HttpListener();
            server.Prefixes.Add(prefix);
            server.Start();
            Thread serving = new Thread(delegate ()
            {
                while (server.IsListening)
                {
                    HttpListenerContext context;
                    try { context = server.GetContext(); } catch (Exception) { return; }
                    Interlocked.Increment(ref requests);
                    byte[] body;
                    lock (served) served.TryGetValue(context.Request.Url.AbsolutePath, out body);
                    context.Response.StatusCode = body == null ? 404 : 200;
                    if (body != null) { context.Response.ContentLength64 = body.Length; context.Response.OutputStream.Write(body, 0, body.Length); }
                    context.Response.Close();
                }
            }) { IsBackground = true };
            serving.Start();
            try
            {
                Func<string, string, bool, string> publish = (version, name, script) =>
                {
                    string build = Path.Combine(root, "build-" + version);
                    LinuxAppImageUpdater.Drop(build);
                    Directory.CreateDirectory(build);
                    MakeApp(Path.Combine(build, BundleName), version, binary, script);
                    File.WriteAllText(Path.Combine(build, "If Capri won't open.txt"), "x");
                    string zip = Path.Combine(root, version + ".zip");
                    LinuxAppImageUpdater.Drop(zip);
                    if (Run("/usr/bin/ditto", 120, "-c", "-k", build, zip) != 0) throw new Exception("The fake release couldn't be zipped.");
                    byte[] bytes = File.ReadAllBytes(zip);
                    string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                    string tag = "/lachydotmcg/capri-desktop/releases/download/v" + version + "/";
                    lock (served)
                    {
                        served[tag + name] = bytes;
                        served[tag + LinuxAppImageUpdater.SumsName] = Encoding.UTF8.GetBytes(new string('0', 64) + "  Capri.exe\n" + hash + "  " + name + "\n");
                    }
                    return "{\"tag_name\":\"v" + version + "\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"" + name + "\",\"size\":" + bytes.Length + ",\"browser_download_url\":\"" + prefix.TrimEnd('/') + tag + name + "\"},{\"name\":\"SHA256SUMS.txt\",\"size\":100,\"browser_download_url\":\"" + prefix.TrimEnd('/') + tag + "SHA256SUMS.txt\"}]}";
                };
                Func<LinuxUpdateReady, int> install = ready =>
                {
                    Process parent = Process.Start(new ProcessStartInfo("/bin/sleep") { ArgumentList = { "1" }, UseShellExecute = false });
                    Process helper = Install(ready, parent.Id, log);
                    if (!helper.WaitForExit(90000)) { helper.Kill(); throw new Exception("The Mac swap script didn't finish."); }
                    parent.WaitForExit(1000);
                    return helper.ExitCode;
                };
                Func<string> version = () => PlistValue(File.ReadAllText(Path.Combine(installed, "Contents", "Info.plist")), "CFBundleShortVersionString");
                Func<int> leftovers = () => Directory.GetFileSystemEntries(Path.GetDirectoryName(installed), "*.capri-*").Length;

                string good = publish("9.9.9", zipName, false);
                LinuxUpdateReady fetched = Prepare(good, data, new Version(0, 9, 0), installed, zipName, trusted);
                if (fetched == null || fetched.Version != "9.9.9" || !fetched.Mac || !Directory.Exists(fetched.Path) || fetched.AppImage != installed || Directory.GetFiles(Path.Combine(data, "updates"), "*.zip*").Length != 0)
                    throw new Exception("The loopback release wasn't downloaded, checked and unzipped.");
                int before = requests;
                LinuxUpdateReady again = Prepare(good, data, new Version(0, 9, 0), installed, zipName, trusted);
                if (again == null || again.Hash != fetched.Hash || requests != before + 1) throw new Exception("A ready download was fetched again: " + (requests - before) + " requests.");
                if (Prepare(good, data, new Version(9, 9, 9), installed, zipName, trusted) != null || Directory.Exists(Path.GetDirectoryName(fetched.Path))) throw new Exception("A current version kept or fetched the download.");
                bool foreign = false;
                try { Prepare(good, data, new Version(0, 9, 0), installed, zipName, null); } catch (AppImageUpdateException) { foreign = true; }
                if (!foreign) throw new Exception("A download outside Capri's GitHub releases was taken.");
                checks.Add("PASS mac update loopback download: a fake release server's zip is fetched, checked against SHA256SUMS.txt, unzipped and kept once; a current version clears it and a foreign address is refused");

                fetched = Prepare(good, data, new Version(0, 9, 0), installed, zipName, trusted);
                lock (served) served["/lachydotmcg/capri-desktop/releases/download/v9.9.9/" + LinuxAppImageUpdater.SumsName] = Encoding.UTF8.GetBytes(new string('1', 64) + "  " + zipName + "\n");
                LinuxAppImageUpdater.Drop(Path.Combine(data, "updates"));
                bool mismatch = false;
                try { Prepare(good, data, new Version(0, 9, 0), installed, zipName, trusted); } catch (AppImageUpdateException error) { mismatch = error.Message.Contains("checksum", StringComparison.Ordinal); }
                if (!mismatch || version() != "0.9.0" || Directory.GetFileSystemEntries(Path.Combine(data, "updates"), "Capri-*").Length != 0) throw new Exception("A zip that didn't match its checksum was kept or installed.");
                string lying = publish("9.9.8", zipName, false).Replace("v9.9.8", "v9.9.7");
                lock (served) { foreach (string key in new List<string>(served.Keys)) if (key.Contains("/v9.9.8/", StringComparison.Ordinal)) served[key.Replace("/v9.9.8/", "/v9.9.7/")] = served[key]; }
                bool wrong = false;
                try { Prepare(lying, data, new Version(0, 9, 0), installed, zipName, trusted); } catch (AppImageUpdateException error) { wrong = error.Message.Contains("different version", StringComparison.Ordinal); }
                if (!wrong) throw new Exception("A zip holding a different version was taken.");
                checks.Add("PASS mac update loopback refusals: a zip that fails SHA-256 or holds another version is refused and nothing is kept");

                good = publish("9.9.9", zipName, false);
                fetched = Prepare(good, data, new Version(0, 9, 0), installed, zipName, trusted);
                if (install(fetched) != 0 || version() != "9.9.9" || leftovers() != 0 || !File.ReadAllText(log).Contains("to Capri 9.9.9.", StringComparison.Ordinal) || LinuxAppImageUpdater.RolledBack(File.ReadAllText(log), "9.9.9"))
                    throw new Exception("The swap didn't put the new Capri.app in place and keep it running: " + File.ReadAllText(log));
                StopFakes(installed);
                checks.Add("PASS mac update loopback swap: she waits to close, swaps Capri.app in a folder with spaces and quotes, relaunches it with open, sees it running and keeps it");

                string broken = publish("9.9.10", zipName, true);
                LinuxUpdateReady dying = Prepare(broken, data, new Version(9, 9, 9), installed, zipName, trusted);
                if (install(dying) != 1 || version() != "9.9.9" || leftovers() != 0 || !LinuxAppImageUpdater.RolledBack(File.ReadAllText(log), "9.9.10"))
                    throw new Exception("A Capri.app that closed at once wasn't rolled back: " + File.ReadAllText(log));
                StopFakes(installed);
                checks.Add("PASS mac update loopback rollback: a new Capri.app that closes at once is swapped back for the old one, relaunched, and logged so she can say so");
            }
            finally
            {
                try { server.Stop(); server.Close(); } catch (Exception) { }
                try { StopFakes(installed); } catch (Exception) { }
            }
        }
    }
}
