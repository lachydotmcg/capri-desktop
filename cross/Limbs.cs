using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CapriDesktop
{
    internal static class Limbs
    {
        internal static Action<string, string[]> Launch = StartTool;
        internal static int Patience = 20;
        internal static int Settle = 1500;
        private static readonly bool OnMac = OperatingSystem.IsMacOS();
        private static readonly Regex Field = new Regex(@"\s*%[fFuUdDnNickvm]");
        private static readonly string[] MacPlayers = { "Spotify", "Music" };

        private static string soundPlayer;
        private static Process speaker;
        private static int speeches;

        internal static void Sound(short[] samples)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = Path.Combine(Path.GetTempPath(), "capri-sound-" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    File.WriteAllBytes(path, Voice.Wave(samples, Sfx.Rate));
                    Music.Run(Player(), new[] { path });
                }
                catch (IOException) { }
                finally
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            });
        }

        private static string Player()
        {
            if (soundPlayer == null) soundPlayer = OnMac ? "afplay" : Music.Run("sh", new[] { "-c", "command -v paplay" }) != null ? "paplay" : "aplay";
            return soundPlayer;
        }

        internal static void Speak(byte[] wav)
        {
            Hush();
            int turn = Interlocked.Increment(ref speeches);
            ThreadPool.QueueUserWorkItem(delegate
            {
                string path = Path.Combine(Path.GetTempPath(), "capri-voice-" + Guid.NewGuid().ToString("N") + ".wav");
                try
                {
                    File.WriteAllBytes(path, wav);
                    ProcessStartInfo start = new ProcessStartInfo(Player()) { UseShellExecute = false, CreateNoWindow = true };
                    start.ArgumentList.Add(path);
                    using (Process process = Process.Start(start))
                    {
                        if (process == null) return;
                        speaker = process;
                        if (turn != speeches) Hush();
                        process.WaitForExit(120000);
                        if (speaker == process) speaker = null;
                    }
                }
                catch (Exception) { }
                finally
                {
                    try { File.Delete(path); }
                    catch (IOException) { }
                }
            });
        }

        internal static void Hush()
        {
            Interlocked.Increment(ref speeches);
            Process playing = speaker;
            speaker = null;
            try { if (playing != null && !playing.HasExited) playing.Kill(); }
            catch (Exception) { }
        }

        internal static void Attach(Brain brain)
        {
            Hands.Run = delegate(string id) { Open(id, OnMac); };
            Hands.Press = delegate(byte key) { Key(key, OnMac); };
            Hands.Catalog = delegate
            {
                List<InstalledApp> apps = OnMac ? MacApps(MacFolders()) : LinuxApps(LinuxFolders());
                return Hands.WithSteam(apps, Hands.SteamApps(SteamRoot(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), OnMac)));
            };
            Hands.NowPlaying = delegate { return Song(OnMac); };
            Hands.Windows = delegate { return Windows(OnMac); };
            Hands.Steer = delegate(OpenWindow window, string action) { return Steer(window, action, OnMac); };
            Hands.Power = delegate(string action, int seconds) { return Power(action, seconds, OnMac); };
            Hands.Unlisted = delegate { return Blind; };
            Hands.Presses = false;
            Hands.Instead = delegate(string name) { return Shortcut(name, OnMac); };
            Hands.SetVolume = delegate(double level) { return SetLevel(level, OnMac); };
            Hands.VolumeNow = delegate { return Level(OnMac); };
            Hands.MuteState = delegate { return Muted(OnMac); };
            Hands.SetMute = delegate(bool on) { return Mute(on, OnMac); };
            Hands.Chord = delegate { throw new PlatformNotSupportedException("keyboard shortcuts only work on Windows for now"); };
            // A look is already in her transcript; anything else she did goes in as a job, like on Windows. A toggle switched
            // off in Security holds the call so she asks first, and their yes runs it.
            brain.Act = delegate(string tool, ToolArguments args)
            {
                string held = Security.Gate(tool, args);
                if (held != null) return held;
                string done = Hands.Do(tool, args);
                if (tool != "look_at_screen" && !string.IsNullOrEmpty(done)) Brain.Transcribe(null, "Job: " + done);
                return done;
            };
            Hands.Resume = delegate(string text) { return Security.Resume(text, brain.Act); };
        }

        internal static void Open(string id, bool mac)
        {
            if (id.StartsWith("exec:", StringComparison.Ordinal)) { Launch("/bin/sh", new[] { "-c", id.Substring(5) }); return; }
            if (Regex.IsMatch(id, @"^spotify:(track|album|playlist|artist):") && PlaySpotify(id, mac)) return;
            Launch(mac ? "open" : "xdg-open", new[] { id });
        }

        internal static bool PlaySpotify(string uri, bool mac)
        {
            if (mac)
            {
                if (!Running("Spotify"))
                {
                    Launch("open", new[] { "-a", "Spotify" });
                    if (!Wait(delegate { return Running("Spotify"); })) return false;
                    Thread.Sleep(Settle);
                }
                return Music.Run("osascript", new[] { "-e", "tell application \"Spotify\" to play track \"" + uri + "\"" }) != null;
            }
            if (OpenUri(uri)) return true;
            Launch("xdg-open", new[] { uri });
            Wait(delegate { return OpenUri(uri); });
            return true;
        }

        private static bool OpenUri(string uri)
        {
            return Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.mpris.MediaPlayer2.spotify", "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player.OpenUri", "string:" + uri }) != null;
        }

        private static bool Running(string app) { return Music.Run("pgrep", new[] { "-x", app }) != null; }

        private static bool Wait(Func<bool> done)
        {
            for (int i = 0; i < Patience; i++)
            {
                if (done()) return true;
                Thread.Sleep(500);
            }
            return false;
        }

        internal static void Key(byte key, bool mac)
        {
            string command = key == 0xB3 ? "playpause" : key == 0xB0 ? "next track" : key == 0xB1 ? "previous track" : null;
            string method = key == 0xB3 ? "PlayPause" : key == 0xB0 ? "Next" : key == 0xB1 ? "Previous" : null;
            if (mac)
            {
                if (command != null)
                {
                    foreach (string app in MacPlayers)
                        if (Running(app)) { Music.Run("osascript", new[] { "-e", "tell application \"" + app + "\" to " + command }); return; }
                    return;
                }
                string volume = key == 0xAF ? "set volume output volume ((output volume of (get volume settings)) + 2)" : key == 0xAE ? "set volume output volume ((output volume of (get volume settings)) - 2)" : key == 0xAD ? "set volume output muted (not (output muted of (get volume settings)))" : null;
                if (volume != null) Music.Run("osascript", new[] { "-e", volume });
                return;
            }
            if (method != null)
            {
                string player = Music.Player();
                if (player != null) Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=" + player, "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player." + method });
                return;
            }
            string[] pactl = key == 0xAF ? new[] { "set-sink-volume", "@DEFAULT_SINK@", "+2%" } : key == 0xAE ? new[] { "set-sink-volume", "@DEFAULT_SINK@", "-2%" } : key == 0xAD ? new[] { "set-sink-mute", "@DEFAULT_SINK@", "toggle" } : null;
            if (pactl != null && Music.Run("pactl", pactl) == null) Music.Run("amixer", new[] { "-q", "sset", "Master", key == 0xAF ? "2%+" : key == 0xAE ? "2%-" : "toggle" });
        }

        // The volume as a number, like Windows' audio API: osascript on Mac, pactl (PulseAudio or PipeWire) on Linux, then ALSA's amixer.
        internal static bool SetLevel(double level, bool mac)
        {
            int percent = (int)Math.Round(Math.Max(0, Math.Min(1, level)) * 100);
            if (mac) return Music.Run("osascript", new[] { "-e", "set volume output volume " + percent }) != null;
            return Music.Run("pactl", new[] { "set-sink-volume", "@DEFAULT_SINK@", percent + "%" }) != null || Music.Run("amixer", new[] { "-q", "sset", "Master", percent + "%" }) != null;
        }

        internal static double? Level(bool mac)
        {
            string shown = mac ? Music.Run("osascript", new[] { "-e", "output volume of (get volume settings)" }) : Music.Run("pactl", new[] { "get-sink-volume", "@DEFAULT_SINK@" }) ?? Music.Run("amixer", new[] { "sget", "Master" });
            Match number = Regex.Match(shown ?? "", mac ? @"^\s*(\d{1,3})\s*$" : @"(\d{1,3})%");
            return number.Success ? int.Parse(number.Groups[1].Value) / 100.0 : (double?)null;
        }

        internal static bool? Muted(bool mac)
        {
            string shown = mac ? Music.Run("osascript", new[] { "-e", "output muted of (get volume settings)" }) : Music.Run("pactl", new[] { "get-sink-mute", "@DEFAULT_SINK@" }) ?? Music.Run("amixer", new[] { "sget", "Master" });
            string said = (shown ?? "").Trim();
            if (mac) return said == "true" ? true : said == "false" ? false : (bool?)null;
            // pactl says "Mute: yes"; amixer shows [off] for muted and [on] for playing.
            if (Regex.IsMatch(said, @"Mute:\s*yes") || said.Contains("[off]")) return true;
            if (Regex.IsMatch(said, @"Mute:\s*no") || said.Contains("[on]")) return false;
            return null;
        }

        internal static bool Mute(bool on, bool mac)
        {
            if (mac) return Music.Run("osascript", new[] { "-e", "set volume output muted " + (on ? "true" : "false") }) != null;
            return Music.Run("pactl", new[] { "set-sink-mute", "@DEFAULT_SINK@", on ? "1" : "0" }) != null || Music.Run("amixer", new[] { "-q", "sset", "Master", on ? "mute" : "unmute" }) != null;
        }

        internal static string Song(bool mac)
        {
            if (mac)
            {
                if (!Running("Spotify")) return "";
                string playing = Music.Run("osascript", new[] { "-e", "tell application \"Spotify\" to if player state is playing then return (artist of current track) & \" - \" & (name of current track)" });
                return playing == null ? "" : playing.Trim();
            }
            string status = Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.mpris.MediaPlayer2.spotify", "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties.Get", "string:org.mpris.MediaPlayer2.Player", "string:PlaybackStatus" });
            if (status == null || !status.Contains("\"Playing\"")) return "";
            return Music.Song(Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.mpris.MediaPlayer2.spotify", "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties.Get", "string:org.mpris.MediaPlayer2.Player", "string:Metadata" }));
        }

        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")] private static extern uint mach_host_self();
        [System.Runtime.InteropServices.DllImport("/usr/lib/libSystem.dylib")] private static extern int host_statistics(uint host, int flavor, int[] info, ref uint count);
        private static uint machHost;

        // The whole computer's CPU time so far, idle and in all, like Windows' GetSystemTimes: /proc/stat on Linux, and on Mac
        // the kernel's tick counts (HOST_CPU_LOAD_INFO: user, system, idle, nice). False when it can't be read.
        internal static bool CpuTimes(out long idle, out long total)
        {
            idle = 0;
            total = 0;
            try
            {
                if (OnMac)
                {
                    if (machHost == 0) machHost = mach_host_self();
                    int[] ticks = new int[4];
                    uint count = 4;
                    if (host_statistics(machHost, 3, ticks, ref count) != 0 || count < 4) return false;
                    foreach (int tick in ticks) total += (uint)tick;
                    idle = (uint)ticks[2];
                    return total > 0;
                }
                string line;
                using (StreamReader stat = new StreamReader("/proc/stat")) line = stat.ReadLine();
                string[] parts = (line ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 6 || parts[0] != "cpu") return false;
                // user, nice, system, idle, iowait, irq, softirq and steal; guest time is already inside user.
                for (int i = 1; i < Math.Min(parts.Length, 9); i++) total += long.Parse(parts[i]);
                idle = long.Parse(parts[4]) + long.Parse(parts[5]);
                return total > 0;
            }
            catch (Exception) { return false; }
        }

        // Start at login: a LaunchAgent on Mac, an XDG autostart entry on Linux. Either one starts this copy of Capri.
        internal static string LoginFile()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OnMac) return Path.Combine(home, "Library", "LaunchAgents", "com.lachydotmcg.capri.plist");
            string config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            return Path.Combine(string.IsNullOrEmpty(config) ? Path.Combine(home, ".config") : config, "autostart", "capri.desktop");
        }

        // Her app's id on Mac, from cross/mac/Info.plist.
        internal const string MacBundle = "io.github.lachydotmcg.capri";

        internal static string LoginEntry(string program, bool mac)
        {
            if (mac)
            {
                // Inside her app, macOS opens Capri by its id, so it finds her wherever she's kept, even moved or still in Downloads,
                // and runs her as a normal app. A bare build starts from its own path.
                bool bundled = program.Contains(".app/Contents/MacOS/");
                string[] run = bundled ? new[] { "/usr/bin/open", "-g", "-b", MacBundle } : new[] { program };
                StringBuilder args = new StringBuilder();
                foreach (string arg in run) args.Append("\t\t<string>").Append(System.Security.SecurityElement.Escape(arg)).Append("</string>\n");
                // Login Items names the entry after the program it runs, which is Apple's open; this names it after her app instead.
                string owner = bundled ? "\t<key>AssociatedBundleIdentifiers</key>\n\t<array>\n\t\t<string>" + MacBundle + "</string>\n\t</array>\n" : "";
                return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n<plist version=\"1.0\">\n<dict>\n\t<key>Label</key>\n\t<string>com.lachydotmcg.capri</string>\n" + owner + "\t<key>ProgramArguments</key>\n\t<array>\n" + args + "\t</array>\n\t<key>RunAtLoad</key>\n\t<true/>\n\t<key>ProcessType</key>\n\t<string>Interactive</string>\n</dict>\n</plist>\n";
            }
            // The Exec line quotes the path and escapes what the desktop entry spec reserves; a backslash takes four.
            string quoted = "\"" + program.Replace("\\", "\\\\\\\\").Replace("\"", "\\\"").Replace("`", "\\`").Replace("$", "\\$").Replace("%", "%%") + "\"";
            return "[Desktop Entry]\nType=Application\nName=Capri\nComment=Capri, your desktop companion\nExec=" + quoted + "\nTerminal=false\nX-GNOME-Autostart-enabled=true\n";
        }

        internal static bool SetLogin(string file, string program, bool on, bool mac)
        {
            try
            {
                if (!on)
                {
                    if (File.Exists(file)) File.Delete(file);
                    return true;
                }
                // Left alone when it already says the same, so starting her doesn't rewrite it, and macOS doesn't announce it again.
                string entry = LoginEntry(program, mac);
                if (File.Exists(file) && File.ReadAllText(file) == entry) return true;
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                File.WriteAllText(file, entry);
                return true;
            }
            catch (Exception) { return false; }
        }

        // Cyberpunk 2077 running: the Mac game, or the Windows one under Proton or Wine on Linux.
        internal static bool CyberpunkRunning() { return Music.Run("pgrep", new[] { "-i", "-f", "[/\\\\]cyberpunk ?2077(\\.exe)?( |$)" }) != null; }

        // Seconds since any input, the call Chrome reads its Mac idle time with: 0 is the combined session state, all ones any event.
        [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")] private static extern double CGEventSourceSecondsSinceLastEventType(int state, uint type);

        // How long since they last touched the mouse or keyboard: macOS's own count (ioreg's HID idle time if that fails), GNOME's
        // idle monitor (X11 and Wayland), or xprintidle on other X11 desktops. XWayland can't see Wayland input, so xprintidle
        // isn't asked there. -1 when unknown. On Mac it's read every second while she sleeps, so it's no longer a process each time.
        internal static double IdleSeconds()
        {
            if (OnMac)
            {
                try
                {
                    double seconds = CGEventSourceSecondsSinceLastEventType(0, uint.MaxValue);
                    if (seconds >= 0 && !double.IsInfinity(seconds)) return seconds;
                }
                catch (Exception) { }
                string hid = Music.Run("ioreg", new[] { "-c", "IOHIDSystem" });
                Match nanoseconds = hid == null ? Match.Empty : Regex.Match(hid, "\"HIDIdleTime\"\\s*=\\s*(\\d+)");
                return nanoseconds.Success ? long.Parse(nanoseconds.Groups[1].Value) / 1e9 : -1;
            }
            string gnome = Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.gnome.Mutter.IdleMonitor", "/org/gnome/Mutter/IdleMonitor/Core", "org.gnome.Mutter.IdleMonitor.GetIdletime" });
            Match idle = gnome == null ? Match.Empty : Regex.Match(gnome, @"uint64\s+(\d+)");
            if (idle.Success) return long.Parse(idle.Groups[1].Value) / 1000.0;
            if (Environment.GetEnvironmentVariable("WAYLAND_DISPLAY") != null) return -1;
            string x11 = Music.Run("xprintidle", new string[0]);
            long milliseconds;
            return x11 != null && long.TryParse(x11.Trim(), out milliseconds) ? milliseconds / 1000.0 : -1;
        }

        // Her app_window tool. On Mac it's the apps in the Dock, through System Events; on Linux it's the windows wmctrl lists,
        // which on Wayland are only the apps running through XWayland.
        private static readonly Dictionary<string, string> Bundles = new Dictionary<string, string>();
        // Why the last listing found nothing it could read, for her window tool to say; what's playing just sees no windows.
        internal static volatile string Blind;

        internal static Func<int, string> ProcessName = delegate(int id)
        {
            try { return id > 0 ? File.ReadAllText("/proc/" + id + "/comm").Trim() : ""; }
            catch (Exception) { return ""; }
        };

        internal static List<OpenWindow> Windows(bool mac)
        {
            List<OpenWindow> found = new List<OpenWindow>();
            if (mac)
            {
                string apps = Music.Run("osascript", new[] { "-e", "set out to \"\"", "-e", "tell application \"System Events\"", "-e", "repeat with p in (every application process whose background only is false)",
                    "-e", "set out to out & (name of p as text) & tab & (bundle identifier of p as text) & linefeed", "-e", "end repeat", "-e", "end tell", "-e", "return out" });
                Blind = apps == null ? "macOS didn't let me see your apps. Allow Capri to control System Events under System Settings > Privacy & Security > Automation, then ask again." : null;
                if (apps == null) return found;
                lock (Bundles)
                {
                    Bundles.Clear();
                    foreach (string line in apps.Split('\n'))
                    {
                        string[] parts = line.TrimEnd('\r').Split('\t');
                        string name = parts[0].Trim();
                        if (name.Length == 0 || name == "Capri") continue;
                        Bundles[name] = parts.Length > 1 && parts[1].Trim() != "missing value" ? parts[1].Trim() : "";
                        found.Add(new OpenWindow(IntPtr.Zero, name, name));
                    }
                }
                return found;
            }
            string windows = Music.Run("wmctrl", new[] { "-lpx" });
            Blind = windows == null ? "Closing and switching apps on Linux needs wmctrl. Install it (sudo apt install wmctrl, or your distro's package), then ask again." : null;
            if (windows == null) return found;
            foreach (string line in windows.Split('\n'))
            {
                Match window = Regex.Match(line.TrimEnd('\r'), @"^0x([0-9a-fA-F]+)\s+(-?\d+)\s+(\d+)\s+(\S+)\s+\S+ ?(.*)$");
                if (!window.Success || window.Groups[2].Value == "-1") continue;
                int id = int.Parse(window.Groups[3].Value);
                if (id == Environment.ProcessId) continue;
                // The process name, like Windows uses; without one, the window's class (discord.discord, Navigator.firefox).
                string program = ProcessName(id);
                if (program.Length == 0) program = window.Groups[4].Value.Substring(window.Groups[4].Value.LastIndexOf('.') + 1).ToLowerInvariant();
                found.Add(new OpenWindow(new IntPtr(Convert.ToInt64(window.Groups[1].Value, 16)), window.Groups[5].Value.Trim(), program));
            }
            return found;
        }

        internal static bool Steer(OpenWindow window, string action, bool mac)
        {
            if (mac)
            {
                string name = Quoted(window.Program), bundle;
                lock (Bundles) Bundles.TryGetValue(window.Program, out bundle);
                string app = string.IsNullOrEmpty(bundle) ? "application \"" + name + "\"" : "application id \"" + Quoted(bundle) + "\"";
                if (action == "minimize") return Music.Run("osascript", new[] { "-e", "tell application \"System Events\" to set visible of application process \"" + name + "\" to false" }) != null;
                if (action == "close")
                {
                    if (Music.Run("osascript", new[] { "-e", "tell " + app + " to quit" }) == null) return false;
                    for (int i = 0; i < 10; i++)
                    {
                        string open = Music.Run("osascript", new[] { "-e", "tell application \"System Events\" to exists application process \"" + name + "\"" });
                        if (open != null && open.Trim() == "false") return true;
                        Thread.Sleep(200);
                    }
                    return false;
                }
                if (Music.Run("osascript", new[] { "-e", "tell " + app + " to activate" }) == null) return false;
                return action != "close_tab" || Music.Run("osascript", new[] { "-e", "tell application \"System Events\" to keystroke \"w\" using command down" }) != null;
            }
            string handle = "0x" + window.Handle.ToInt64().ToString("x8");
            if (action == "minimize") return Music.Run("xdotool", new[] { "windowminimize", handle }) != null || Music.Run("wmctrl", new[] { "-i", "-r", handle, "-b", "add,hidden" }) != null;
            if (action == "close")
            {
                if (Music.Run("wmctrl", new[] { "-i", "-c", handle }) == null) return false;
                for (int i = 0; i < 10; i++)
                {
                    string open = Music.Run("wmctrl", new[] { "-l" });
                    if (open != null && open.IndexOf(handle, StringComparison.OrdinalIgnoreCase) < 0) return true;
                    Thread.Sleep(200);
                }
                return false;
            }
            if (Music.Run("wmctrl", new[] { "-i", "-a", handle }) == null) return false;
            return action != "close_tab" || Music.Run("xdotool", new[] { "key", "--clearmodifiers", "ctrl+w" }) != null;
        }

        private static string Quoted(string text) { return (text ?? "").Replace("\\", "\\\\").Replace("\"", "\\\""); }

        // Shortcuts with a tool of their own on Mac and Linux. The rest need key presses, which only Windows has so far.
        internal static string Shortcut(string name, bool mac)
        {
            if (name == "screenshot")
            {
                if (mac) { Launch("screencapture", new[] { "-i", "-c" }); return "Opened the screenshot picker. Drag over what to grab, and it goes to your clipboard."; }
                string[][] pickers = { new[] { "flameshot", "gui" }, new[] { "spectacle", "-r" }, new[] { "gnome-screenshot", "-a", "-c" }, new[] { "xfce4-screenshooter", "-r" } };
                foreach (string[] picker in pickers)
                {
                    if (!Has(picker[0])) continue;
                    string[] rest = new string[picker.Length - 1];
                    Array.Copy(picker, 1, rest, 0, rest.Length);
                    Launch(picker[0], rest);
                    return "Opened the screenshot picker. Drag over what to grab.";
                }
                // The desktop's own screenshot window, through its portal: GNOME's on Wayland.
                if (Has("gdbus"))
                {
                    Launch("gdbus", new[] { "call", "--session", "--dest", "org.freedesktop.portal.Desktop", "--object-path", "/org/freedesktop/portal/desktop", "--method", "org.freedesktop.portal.Screenshot.Screenshot", "", "{'interactive': <true>}" });
                    return "Opened the screenshot window.";
                }
                return "Couldn't find a screenshot tool. Install one, like flameshot, and ask again.";
            }
            if (name == "task_manager")
            {
                if (mac) { Launch("open", new[] { "-a", "Activity Monitor" }); return "Opened Activity Monitor."; }
                foreach (string monitor in new[] { "gnome-system-monitor", "plasma-systemmonitor", "ksysguard", "xfce4-taskmanager", "mate-system-monitor", "lxtask" })
                    if (Has(monitor)) { Launch(monitor, new string[0]); return "Opened the system monitor."; }
                return "Couldn't find a system monitor. Install one, like gnome-system-monitor, and ask again.";
            }
            if (name == "show_desktop" && !mac && Music.Run("wmctrl", new[] { "-k", "on" }) != null) return "Showed the desktop.";
            return null;
        }

        private static bool Has(string tool) { return Music.Run("sh", new[] { "-c", "command -v " + tool }) != null; }

        // Her power tool on Mac and Linux, like shutdown.exe on Windows: a wait first, so her line plays and "cancel" can stop
        // it, then the first command that works.
        private static readonly object powerGate = new object();
        private static Timer powerLater;

        internal static string[][] PowerCommands(string action, bool mac)
        {
            if (mac)
            {
                if (action == "shut_down") return new[] { new[] { "osascript", "-e", "tell application \"System Events\" to shut down" } };
                if (action == "restart") return new[] { new[] { "osascript", "-e", "tell application \"System Events\" to restart" } };
                if (action == "sleep") return new[] { new[] { "pmset", "sleepnow" } };
                if (action == "lock") return new[] { new[] { "pmset", "displaysleepnow" } };
                if (action == "sign_out") return new[] { new[] { "osascript", "-e", "tell application \"loginwindow\" to «event aevtrlgo»" } };
                return null;
            }
            if (action == "shut_down") return new[] { new[] { "systemctl", "poweroff" }, new[] { "loginctl", "poweroff" } };
            if (action == "restart") return new[] { new[] { "systemctl", "reboot" }, new[] { "loginctl", "reboot" } };
            if (action == "sleep") return new[] { new[] { "systemctl", "suspend" }, new[] { "loginctl", "suspend" } };
            if (action == "lock") return new[] { new[] { "loginctl", "lock-session" }, new[] { "xdg-screensaver", "lock" } };
            if (action != "sign_out") return null;
            List<string[]> signOut = new List<string[]> { new[] { "gnome-session-quit", "--logout", "--no-prompt" }, new[] { "qdbus", "org.kde.ksmserver", "/KSMServer", "logout", "0", "0", "0" } };
            string session = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
            if (!string.IsNullOrEmpty(session)) signOut.Add(new[] { "loginctl", "terminate-session", session });
            return signOut.ToArray();
        }

        internal static bool Power(string action, int seconds, bool mac)
        {
            if (action == "cancel")
            {
                lock (powerGate)
                {
                    bool stopped = powerLater != null;
                    if (stopped) powerLater.Dispose();
                    powerLater = null;
                    return stopped;
                }
            }
            string[][] commands = PowerCommands(action, mac);
            if (commands == null) return false;
            if (seconds <= 0) return PowerNow(commands);
            Timer timer = null;
            timer = new Timer(delegate
            {
                lock (powerGate) { if (powerLater != timer) return; powerLater = null; }
                PowerNow(commands);
            }, null, Timeout.Infinite, Timeout.Infinite);
            lock (powerGate) { if (powerLater != null) powerLater.Dispose(); powerLater = timer; }
            timer.Change(seconds * 1000L, Timeout.Infinite);
            return true;
        }

        private static bool PowerNow(string[][] commands)
        {
            foreach (string[] command in commands)
            {
                string[] args = new string[command.Length - 1];
                Array.Copy(command, 1, args, 0, args.Length);
                if (Music.Run(command[0], args) != null) return true;
            }
            return false;
        }

        internal static string[] MacFolders()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[] { "/Applications", "/Applications/Utilities", "/System/Applications", "/System/Applications/Utilities", Path.Combine(home, "Applications") };
        }

        internal static List<InstalledApp> MacApps(IEnumerable<string> folders)
        {
            List<InstalledApp> apps = new List<InstalledApp>();
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                try { foreach (string app in Directory.GetDirectories(folder, "*.app")) apps.Add(new InstalledApp(Path.GetFileNameWithoutExtension(app), app)); }
                catch (Exception) { }
            }
            return apps;
        }

        // Steam's own library, for games with no .desktop file or .app of their own: the normal, Flatpak and Snap installs on Linux.
        internal static string SteamRoot(string home, bool mac)
        {
            string[] roots = mac ? new[] { Path.Combine(home, "Library", "Application Support", "Steam") }
                : new[] { Path.Combine(home, ".steam", "steam"), Path.Combine(home, ".local", "share", "Steam"), Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"), Path.Combine(home, "snap", "steam", "common", ".local", "share", "Steam") };
            foreach (string root in roots) if (Directory.Exists(Path.Combine(root, "steamapps"))) return root;
            return null;
        }

        internal static string[] LinuxFolders()
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string data = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            List<string> folders = new List<string> { Path.Combine(string.IsNullOrEmpty(data) ? Path.Combine(home, ".local", "share") : data, "applications") };
            string dirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
            foreach (string dir in (string.IsNullOrEmpty(dirs) ? "/usr/local/share:/usr/share" : dirs).Split(':'))
                if (dir.Length > 0) folders.Add(Path.Combine(dir, "applications"));
            folders.Add("/var/lib/flatpak/exports/share/applications");
            folders.Add(Path.Combine(home, ".local", "share", "flatpak", "exports", "share", "applications"));
            folders.Add("/var/lib/snapd/desktop/applications");
            return folders.ToArray();
        }

        internal static List<InstalledApp> LinuxApps(IEnumerable<string> folders)
        {
            List<InstalledApp> apps = new List<InstalledApp>();
            HashSet<string> seen = new HashSet<string>();
            foreach (string folder in folders)
            {
                if (!Directory.Exists(folder)) continue;
                string[] files;
                try { files = Directory.GetFiles(folder, "*.desktop", SearchOption.AllDirectories); }
                catch (Exception) { continue; }
                foreach (string file in files)
                {
                    if (!seen.Add(Path.GetFileName(file))) continue;
                    try
                    {
                        InstalledApp app = Desktop(File.ReadAllLines(file));
                        if (app != null) apps.Add(app);
                    }
                    catch (Exception) { }
                }
            }
            return apps;
        }

        internal static InstalledApp Desktop(string[] lines)
        {
            bool entry = false;
            bool hidden = false;
            bool application = false;
            string name = null;
            string exec = null;
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.StartsWith("[", StringComparison.Ordinal)) { entry = line == "[Desktop Entry]"; continue; }
                if (!entry) continue;
                if (name == null && line.StartsWith("Name=", StringComparison.Ordinal)) name = line.Substring(5).Trim();
                else if (exec == null && line.StartsWith("Exec=", StringComparison.Ordinal)) exec = line.Substring(5).Trim();
                else if (line == "NoDisplay=true" || line == "Hidden=true") hidden = true;
                else if (line == "Type=Application") application = true;
            }
            if (hidden || !application || string.IsNullOrEmpty(name) || string.IsNullOrEmpty(exec)) return null;
            return new InstalledApp(name, "exec:" + Field.Replace(exec, "").Replace("%%", "%").Trim());
        }

        private static void StartTool(string file, string[] args)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(file) { UseShellExecute = false, CreateNoWindow = true };
                foreach (string arg in args) start.ArgumentList.Add(arg);
                using (Process.Start(start)) { }
            }
            catch (Exception) { }
        }
    }
}
