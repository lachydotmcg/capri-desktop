using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace CapriDesktop
{
    internal sealed class UninstallPlan
    {
        internal string Kind = "other";
        internal string App;
        internal bool AppIsFolder;
        internal string Login;
        internal string Keeps;
        internal string Data;
        internal string Hand;
    }

    internal static class Uninstall
    {
        internal const string ArchPackage = "capri-desktop";
        private static readonly string[] Packaged = { "/usr", "/opt", "/nix", "/snap", "/app", "/var/lib/flatpak" };
        private static readonly string[] Shared = { "/bin", "/sbin", "/lib", "/lib64", "/etc", "/var", "/boot", "/proc", "/sys", "/dev", "/run", "/tmp", "/root", "/home", "/mnt", "/media", "/srv" };

        internal const string Script = """
pid=$1
kind=$2
app=$3
note=$4
data=$5
login=$6
here=$(dirname -- "$0")
say() {
    [ -n "$1" ] || return 0
    if [ "$(uname)" = Darwin ]; then
        osascript -e 'on run argv' -e 'display notification (item 1 of argv) with title "Capri"' -e 'end run' "$1" >/dev/null 2>&1 || :
    elif command -v notify-send >/dev/null 2>&1; then
        notify-send Capri "$1" >/dev/null 2>&1 || :
    fi
}
tidy() { case "$here" in */capri-uninstall-*) rm -rf -- "$here" ;; esac; }
case "$pid" in ''|*[!0-9]*) tidy; exit 1 ;; esac
if [ "$pid" -le 1 ]; then tidy; exit 1; fi
waited=0
while kill -0 "$pid" 2>/dev/null; do
    waited=$((waited + 1))
    if [ "$waited" -gt 30 ]; then
        say "Capri was still open after 30 seconds, so I removed nothing. Close her and pick Uninstall again."
        tidy
        exit 1
    fi
    sleep 1
done
left=""
if [ -n "$login" ]; then
    case "${login##*/}" in
        capri.desktop|com.lachydotmcg.capri.plist) rm -f -- "$login"; [ -e "$login" ] && left="$left \"$login\"" ;;
    esac
fi
if [ -n "$data" ]; then
    case "$data" in
        /?*/Capri) if [ -d "$data" ] && [ ! -L "$data" ]; then rm -rf -- "$data"; [ -e "$data" ] && left="$left \"$data\""; fi ;;
    esac
fi
if [ -n "$app" ]; then
    case "$kind:${app##*/}" in
        mac:*.app|dir:Capri*) if [ -d "$app" ] && [ ! -L "$app" ]; then rm -rf -- "$app"; [ -e "$app" ] && left="$left \"$app\""; fi ;;
        file:Capri*) if [ -f "$app" ] && [ ! -L "$app" ]; then rm -f -- "$app"; [ -e "$app" ] && left="$left \"$app\""; fi ;;
    esac
fi
msg=$note
if [ -n "$left" ]; then msg="I couldn't remove$left, so please delete it yourself. $note"; fi
if [ -z "$msg" ]; then msg="Capri is uninstalled. Bye for now."; fi
say "$msg"
tidy
exit 0
""";

        internal const string Launcher = "if command -v setsid >/dev/null 2>&1; then setsid /bin/sh \"$@\" >/dev/null 2>&1 </dev/null & else nohup /bin/sh \"$@\" >/dev/null 2>&1 </dev/null & fi";

        private static bool Under(string path, string folder) { return path.StartsWith(folder.TrimEnd('/') + "/", StringComparison.Ordinal); }

        private static bool Plain(string path, bool folder)
        {
            try
            {
                FileSystemInfo info = folder ? new DirectoryInfo(path) : new FileInfo(path);
                return info.Exists && info.LinkTarget == null;
            }
            catch (Exception) { return false; }
        }

        internal static UninstallPlan Plan(string exe, Func<string, string> env, string home, bool mac, bool wipe, string appData = null)
        {
            UninstallPlan plan = new UninstallPlan();
            if (string.IsNullOrEmpty(home) || !Path.IsPathRooted(home) || Path.GetPathRoot(home) == home) { plan.Hand = "I couldn't find your home folder, so I won't remove anything. Delete Capri's app and her Capri data folder yourself."; return plan; }
            home = Path.GetFullPath(home).TrimEnd('/');
            string config = env("XDG_CONFIG_HOME");
            config = string.IsNullOrEmpty(config) || !Path.IsPathRooted(config) ? Path.Combine(home, ".config") : Path.GetFullPath(config);
            string parent = string.IsNullOrEmpty(appData) || !Path.IsPathRooted(appData) ? config : Path.GetFullPath(appData);
            string data = Path.Combine(parent, "Capri");
            if (Path.GetFileName(data) == "Capri" && Path.GetDirectoryName(data) != null && Path.GetDirectoryName(data) != "/" && data != home)
            {
                plan.Keeps = data;
                if (wipe) plan.Data = data;
            }
            plan.Login = mac ? Path.Combine(home, "Library", "LaunchAgents", "com.lachydotmcg.capri.plist") : Path.Combine(config, "autostart", "capri.desktop");
            if (string.IsNullOrEmpty(exe) || !Path.IsPathRooted(exe)) { plan.Hand = "I couldn't tell where Capri's app is, so I'm leaving it. After she closes, delete it yourself."; return plan; }
            exe = Path.GetFullPath(exe);
            if (mac)
            {
                string bundle = MacAppUpdater.Bundle(exe);
                if (bundle != null) { plan.Kind = "mac"; plan.App = bundle; plan.AppIsFolder = true; }
                else plan.Hand = "I only remove her own Capri.app, and I can't tell that this is it. After she closes, drag Capri.app to the Bin yourself.";
                return plan;
            }
            string image = env("APPIMAGE"), dir = env("APPDIR");
            if (!string.IsNullOrEmpty(image) && !string.IsNullOrEmpty(dir) && Path.IsPathRooted(image) && Under(exe, dir))
            {
                image = Path.GetFullPath(image);
                if (Plain(image, false) && Path.GetFileName(image).StartsWith("Capri", StringComparison.Ordinal)) { plan.Kind = "appimage"; plan.App = image; }
                else plan.Hand = "I only remove an AppImage named Capri, and this one isn't. After she closes, delete " + image + " yourself.";
                return plan;
            }
            foreach (string place in Packaged)
                if (Under(exe, place))
                {
                    plan.Kind = "package";
                    plan.Hand = "Your package manager installed Capri, so I leave her program alone. After she closes, run: sudo pacman -Rns " + ArchPackage + " (on Arch), or remove her with your package manager.";
                    return plan;
                }
            string folder = Path.GetDirectoryName(exe);
            string up = folder == null ? null : Path.GetDirectoryName(folder);
            bool hers = folder != null && up != null && up != "/" && Path.GetFileName(exe) == "Capri" && Path.GetFileName(folder).StartsWith("Capri", StringComparison.Ordinal)
                && folder != home && !(home + "/").StartsWith(folder + "/", StringComparison.Ordinal) && folder != plan.Keeps && plan.Keeps != null && !Under(plan.Keeps, folder)
                && !Shared.Any(place => folder == place || Under(folder, place) && Path.GetDirectoryName(folder) == place)
                && Plain(folder, true) && Plain(exe, false) && !Directory.Exists(Path.Combine(folder, ".git"));
            if (hers) { plan.Kind = "tar"; plan.App = folder; plan.AppIsFolder = true; }
            else plan.Hand = "I only remove a folder that is clearly hers (named Capri, with her Capri program inside), and " + (folder ?? "this") + " isn't one. After she closes, delete it yourself if you want her gone.";
            return plan;
        }

        internal static UninstallPlan Live(bool wipe)
        {
            return Plan(Environment.ProcessPath, Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OperatingSystem.IsMacOS(), wipe, Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        }

        internal static string[] Arguments(UninstallPlan plan, int pid)
        {
            string kind = plan.App == null ? "none" : plan.Kind == "mac" ? "mac" : plan.AppIsFolder ? "dir" : "file";
            return new[] { pid.ToString(System.Globalization.CultureInfo.InvariantCulture), kind, plan.App ?? "", plan.Hand ?? "", plan.Data ?? "", plan.Login ?? "" };
        }

        internal static string Says(UninstallPlan plan, bool wipe)
        {
            StringBuilder text = new StringBuilder("Capri closes, then a small helper takes her off this computer. It can't be undone.\n");
            text.Append("\nIt removes:\n");
            text.Append("  - Her start-at-login entry\n");
            if (plan.App != null) text.Append("  - ").Append(plan.App).Append('\n');
            if (wipe && plan.Data != null) text.Append("  - ").Append(plan.Data).Append(" (everything she knows)\n");
            if (!wipe && plan.Keeps != null) text.Append("\nHer memories and settings stay in ").Append(plan.Keeps).Append(", so a reinstall picks up where she left off.");
            if (plan.Hand != null) text.Append("\n\nBy hand: ").Append(plan.Hand);
            return text.ToString();
        }

        internal static bool Start(UninstallPlan plan, int pid)
        {
            try
            {
                string folder = Directory.CreateTempSubdirectory("capri-uninstall-").FullName;
                string script = Path.Combine(folder, "uninstall.sh");
                File.WriteAllText(script, Script);
                ProcessStartInfo start = new ProcessStartInfo("/bin/sh") { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder };
                foreach (string part in new[] { "-c", Launcher, "capri-uninstall", script }.Concat(Arguments(plan, pid))) start.ArgumentList.Add(part);
                string appDir = Environment.GetEnvironmentVariable("APPDIR");
                if (OperatingSystem.IsLinux() && appDir != null) LinuxAppImageUpdater.Clean(start.Environment, appDir, appDir.Contains("/appimage_extracted_", StringComparison.Ordinal));
                using (Process helper = Process.Start(start)) { if (helper == null) return false; helper.WaitForExit(5000); }
                return true;
            }
            catch (Exception error) { Brain.Log("uninstall", -1, error.Message); return false; }
        }

        internal static void Checks(List<string> checks)
        {
            string root = Path.Combine(Path.GetTempPath(), "capri-uninstall-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string home = Path.Combine(root, "home 'q' \"x\"");
                Directory.CreateDirectory(home);
                Func<string, string> none = name => null;
                string plist = "<plist><dict><key>CFBundleIdentifier</key><string>" + Limbs.MacBundle + "</string></dict></plist>";

                string app = Path.Combine(home, "Applications", "Capri.app");
                string macExe = Path.Combine(app, "Contents", "MacOS", "Capri");
                Directory.CreateDirectory(Path.GetDirectoryName(macExe));
                File.WriteAllText(macExe, "x");
                File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), plist);
                UninstallPlan mac = Plan(macExe, none, home, true, false);
                if (mac.Kind != "mac" || mac.App != app || !mac.AppIsFolder || mac.Hand != null || mac.Login != Path.Combine(home, "Library", "LaunchAgents", "com.lachydotmcg.capri.plist"))
                    throw new Exception("Her own Capri.app wasn't the one chosen on a Mac.");
                File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), plist.Replace(Limbs.MacBundle, "com.example.other"));
                UninstallPlan other = Plan(macExe, none, home, true, true);
                if (other.App != null || other.Hand == null) throw new Exception("Another app's bundle was taken for hers.");
                string downloads = Path.Combine(home, "Downloads");
                Directory.CreateDirectory(downloads);
                File.WriteAllText(Path.Combine(downloads, "Capri"), "x");
                UninstallPlan loose = Plan(Path.Combine(downloads, "Capri"), none, home, true, true);
                if (loose.App != null || loose.Hand == null) throw new Exception("A loose Capri program in Downloads was taken for her app on a Mac.");
                checks.Add("PASS uninstall plan on a Mac: only her own Capri.app (her bundle id) is chosen, another app's bundle and a loose program in Downloads are left with a by-hand note, and her LaunchAgent is listed");

                string images = Path.Combine(home, "Apps");
                Directory.CreateDirectory(images);
                string image = Path.Combine(images, "Capri-linux-x64.AppImage");
                File.WriteAllText(image, "x");
                string mount = Path.Combine(root, "mount");
                string inside = Path.Combine(mount, "usr", "bin", "Capri");
                Func<string, string> env = name => name == "APPIMAGE" ? image : name == "APPDIR" ? mount : null;
                UninstallPlan appImage = Plan(inside, env, home, false, false);
                if (appImage.Kind != "appimage" || appImage.App != image || appImage.AppIsFolder || appImage.Hand != null || appImage.Login != Path.Combine(home, ".config", "autostart", "capri.desktop"))
                    throw new Exception("Her AppImage wasn't the one chosen.");
                string odd = Path.Combine(images, "notes.AppImage");
                File.WriteAllText(odd, "x");
                UninstallPlan stranger = Plan(inside, name => name == "APPIMAGE" ? odd : name == "APPDIR" ? mount : null, home, false, true);
                if (stranger.App != null || stranger.Hand == null || !stranger.Hand.Contains(odd)) throw new Exception("An AppImage not named Capri was taken for hers.");
                string link = Path.Combine(images, "Capri-link.AppImage");
                File.CreateSymbolicLink(link, image);
                if (Plan(inside, name => name == "APPIMAGE" ? link : name == "APPDIR" ? mount : null, home, false, true).App != null) throw new Exception("A link was taken for her AppImage.");
                if (Plan(inside, name => name == "APPIMAGE" ? Path.Combine(images, "Capri-gone.AppImage") : name == "APPDIR" ? mount : null, home, false, true).App != null) throw new Exception("A missing AppImage was chosen.");
                checks.Add("PASS uninstall plan for an AppImage: only a regular file whose name starts with Capri, that she was started from, is chosen; another name, a link and a missing file are refused with a by-hand note");

                string tar = Path.Combine(home, "Capri");
                Directory.CreateDirectory(tar);
                string tarExe = Path.Combine(tar, "Capri");
                File.WriteAllText(tarExe, "x");
                UninstallPlan unpacked = Plan(tarExe, none, home, false, false);
                if (unpacked.Kind != "tar" || unpacked.App != tar || !unpacked.AppIsFolder || unpacked.Hand != null) throw new Exception("Her unpacked tarball folder wasn't the one chosen.");
                string stray = Path.Combine(home, "Tools");
                Directory.CreateDirectory(stray);
                File.WriteAllText(Path.Combine(stray, "Capri"), "x");
                File.WriteAllText(Path.Combine(home, "Capri-notes"), "x");
                string checkout = Path.Combine(home, "Capri-src");
                Directory.CreateDirectory(Path.Combine(checkout, ".git"));
                File.WriteAllText(Path.Combine(checkout, "Capri"), "x");
                string[] refused = { Path.Combine(stray, "Capri"), Path.Combine(downloads, "Capri"), Path.Combine(home, "Capri-notes"), Path.Combine(checkout, "Capri"), "/usr/bin/Capri", "/usr/lib/capri/Capri", "/tmp/Capri", "/Capri", Path.Combine(root, "Capri") };
                foreach (string path in refused)
                {
                    UninstallPlan plan = Plan(path, none, home, false, true);
                    if (plan.App != null || plan.Hand == null) throw new Exception("An unrelated folder was taken for hers: " + path);
                }
                UninstallPlan packaged = Plan("/usr/lib/capri/Capri", none, home, false, true);
                if (packaged.Kind != "package" || !packaged.Hand.Contains("pacman -Rns " + ArchPackage)) throw new Exception("A package install wasn't told its package-manager command.");
                checks.Add("PASS uninstall plan for a tarball and packages: her unpacked Capri folder is chosen; Downloads, /usr/bin, a source checkout, a lone file and a folder beside her home are refused; a package install is left alone with the pacman command");

                string config = Path.Combine(home, ".config", "Capri");
                UninstallPlan keep = Plan(macExe, none, home, false, false);
                UninstallPlan wiped = Plan(tarExe, none, home, false, true);
                UninstallPlan moved = Plan(tarExe, name => name == "XDG_CONFIG_HOME" ? Path.Combine(home, "cfg") : null, home, false, true);
                UninstallPlan shifted = Plan(tarExe, none, home, false, true, Path.Combine(home, "elsewhere"));
                UninstallPlan nowhere = Plan(tarExe, none, "", false, true);
                if (keep.Data != null || keep.Keeps != config || wiped.Data != config || moved.Data != Path.Combine(home, "cfg", "Capri") || shifted.Data != Path.Combine(home, "elsewhere", "Capri") || nowhere.Data != null || nowhere.App != null || nowhere.Login != null)
                    throw new Exception("Her data folder was listed without the wipe, or missed with it.");
                if (Says(keep, false).Contains(config + " (everything") || !Says(wiped, true).Contains(config + " (everything")) throw new Exception("The confirm text didn't follow the wipe choice.");
                checks.Add("PASS uninstall data folders: her Capri folder is listed only when they choose to delete everything she knows, follows XDG_CONFIG_HOME and her real app data folder, and an unknown home removes nothing");

                string[] sent = Arguments(new UninstallPlan { Kind = "tar", App = "/a b/Capri 'x'\"; rm -rf ~", AppIsFolder = true, Data = "/h/.config/Capri", Login = "/h/.config/autostart/capri.desktop", Hand = "note" }, 4321);
                if (sent.Length != 6 || sent[0] != "4321" || sent[1] != "dir" || sent[2] != "/a b/Capri 'x'\"; rm -rf ~" || sent[4] != "/h/.config/Capri" || sent[5] != "/h/.config/autostart/capri.desktop") throw new Exception("The uninstall helper's arguments were wrong.");
                string[] bare = Arguments(new UninstallPlan { Login = "/l" }, 7);
                if (bare[1] != "none" || bare[2] != "" || bare[4] != "") throw new Exception("An empty plan still named an app or data.");
                if (Script.Contains("/h/.config") || Script.Contains("autostart") || !Script.Contains("pid=$1") || !Script.Contains("data=$5") || !Script.Contains("kill -0") || !Script.Contains("rm -rf -- \"$data\"") || Script.Contains("eval ") || !Launcher.Contains("\"$@\""))
                    throw new Exception("The uninstall helper takes its paths from somewhere other than its arguments.");
                checks.Add("PASS uninstall helper: paths reach the sh script as six arguments (odd characters kept whole), the script holds no path of her own, waits on her pid with kill -0, and is started detached by a launcher that only forwards its arguments");
            }
            finally { try { Directory.Delete(root, true); } catch (Exception) { } }
        }
    }
}
