using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace CapriDesktop
{
    internal static class Music
    {
        internal static Func<string, string[], string> Run = RunTool;
        private static readonly Regex Players = new Regex(@"org\.mpris\.MediaPlayer2\.(spotify|rhythmbox|elisa|clementine|strawberry|audacious|lollypop|amberol|cmus|mpd|quodlibet|deadbeef|tauon|amarok|gnome-music|tidal|deezer|cider|youtube-music|ncspot|psst|g4music|musicpod|io\.bassi\.Amberol|com\.github\.th_ch\.youtube_music)[\w.\-]*", RegexOptions.IgnoreCase);
        private static readonly string[] MacPlayers = { "Spotify", "Music" };

        internal static bool Playing()
        {
            if (OperatingSystem.IsMacOS()) return Mac();
            if (OperatingSystem.IsLinux()) return Linux();
            return false;
        }

        internal static bool Mac()
        {
            foreach (string app in MacPlayers)
            {
                if (Run("pgrep", new[] { "-x", app }) == null) continue;
                string state = Run("osascript", new[] { "-e", "tell application \"" + app + "\" to get player state as string" });
                if (state != null && state.Trim() == "playing") return true;
            }
            return false;
        }

        internal static bool Linux()
        {
            string names = Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.ListNames" });
            if (names == null) return false;
            HashSet<string> asked = new HashSet<string>();
            foreach (Match player in Players.Matches(names))
            {
                if (!asked.Add(player.Value)) continue;
                string state = Run("dbus-send", new[] { "--session", "--print-reply", "--dest=" + player.Value, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties.Get", "string:org.mpris.MediaPlayer2.Player", "string:PlaybackStatus" });
                if (state != null && state.Contains("\"Playing\"")) return true;
            }
            return false;
        }

        // What's playing right now, as "artist - title", for her Night City cheer: Spotify and Music on Mac (only when they're
        // already open), and every MPRIS player on Linux, browsers too, so a YouTube tab counts like on Windows.
        internal static List<string> Songs() { return OperatingSystem.IsMacOS() ? MacSongs() : OperatingSystem.IsLinux() ? LinuxSongs() : new List<string>(); }

        internal static List<string> MacSongs()
        {
            List<string> songs = new List<string>();
            foreach (string app in MacPlayers)
            {
                if (Run("pgrep", new[] { "-x", app }) == null) continue;
                string song = Run("osascript", new[] { "-e", "tell application \"" + app + "\" to if player state is playing then return (artist of current track) & \" - \" & (name of current track)" });
                if (!string.IsNullOrWhiteSpace(song)) songs.Add(song.Trim());
            }
            return songs;
        }

        internal static List<string> LinuxSongs()
        {
            List<string> songs = new List<string>();
            string names = Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.ListNames" });
            if (names == null) return songs;
            HashSet<string> asked = new HashSet<string>();
            foreach (Match player in Regex.Matches(names, @"org\.mpris\.MediaPlayer2\.[\w.\-]+"))
            {
                if (!asked.Add(player.Value)) continue;
                string state = Run("dbus-send", new[] { "--session", "--print-reply", "--dest=" + player.Value, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties.Get", "string:org.mpris.MediaPlayer2.Player", "string:PlaybackStatus" });
                if (state == null || !state.Contains("\"Playing\"")) continue;
                string song = Song(Run("dbus-send", new[] { "--session", "--print-reply", "--dest=" + player.Value, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties.Get", "string:org.mpris.MediaPlayer2.Player", "string:Metadata" }));
                if (song.Length > 0) songs.Add(song);
            }
            return songs;
        }

        // "artist - title" from an MPRIS Metadata reply, or "".
        internal static string Song(string data)
        {
            if (data == null) return "";
            Match artist = Regex.Match(data, "\"xesam:artist\"\\s*variant\\s*array \\[\\s*string \"([^\"]*)\"");
            Match title = Regex.Match(data, "\"xesam:title\"\\s*variant\\s*string \"([^\"]*)\"");
            if (!title.Success) return "";
            return (artist.Success ? artist.Groups[1].Value + " - " : "") + title.Groups[1].Value;
        }

        internal static string Player()
        {
            string names = Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.ListNames" });
            if (names == null) return null;
            Match music = Players.Match(names);
            if (music.Success) return music.Value;
            Match any = Regex.Match(names, @"org\.mpris\.MediaPlayer2\.[\w.\-]+");
            return any.Success ? any.Value : null;
        }

        private static string RunTool(string file, string[] args)
        {
            try
            {
                ProcessStartInfo start = new ProcessStartInfo(file) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                foreach (string arg in args) start.ArgumentList.Add(arg);
                using (Process process = Process.Start(start))
                {
                    if (process == null) return null;
                    Task<string> output = process.StandardOutput.ReadToEndAsync();
                    process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(2000))
                    {
                        try { process.Kill(); }
                        catch (Exception) { }
                        return null;
                    }
                    return process.ExitCode == 0 ? output.Result : null;
                }
            }
            catch (Exception) { return null; }
        }
    }
}
