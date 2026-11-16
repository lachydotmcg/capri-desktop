using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CapriDesktop
{
    internal sealed class Playlist
    {
        internal const string FileName = "playlist.json";

        internal static string Kept;
        private static readonly object writing = new object();

        internal static WrittenNote OpenNote() { return new WrittenNote(Kept, "", writing); }

        private static readonly string[] Kinds = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".mid", ".midi" };

        private readonly List<string> folders = new List<string>();
        private readonly List<string> added = new List<string>();
        private readonly List<string> removed = new List<string>();
        private readonly List<string> loved = new List<string>();
        private readonly object gate = new object();
        private string path;
        internal bool Shuffle;
        internal string Repeat = "off";

        internal Playlist(string file) { path = file; Read(); }

        internal string Where { get { return path; } }

        internal List<string> Folders { get { lock (gate) return new List<string>(folders); } }
        internal List<string> Removed { get { lock (gate) return new List<string>(removed); } }
        internal List<string> Loved { get { lock (gate) return new List<string>(loved); } }
        internal int AddedCount { get { lock (gate) return added.Count; } }

        internal List<string> Songs()
        {
            List<string> songs = new List<string>();
            HashSet<string> gone = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            List<string> roots;
            List<string> ones;
            lock (gate)
            {
                foreach (string each in removed) gone.Add(Settled(each));
                roots = new List<string>(folders);
                ones = new List<string>(added);
            }
            foreach (string folder in roots)
            {
                string[] found;
                try { found = Directory.Exists(folder) ? Directory.GetFiles(folder, "*", SearchOption.AllDirectories) : new string[0]; }
                catch (Exception) { continue; }
                Array.Sort(found, StringComparer.OrdinalIgnoreCase);
                foreach (string file in found) if (Playable(file) && !gone.Contains(Settled(file)) && !songs.Contains(file)) songs.Add(file);
            }
            foreach (string one in ones)
                if (Playable(one) && !gone.Contains(Settled(one)) && !songs.Contains(one) && FileThere(one)) songs.Add(one);
            return songs;
        }

        private static bool FileThere(string file) { try { return File.Exists(file); } catch (Exception) { return false; } }

        internal static bool Playable(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string kind = Path.GetExtension(file);
            foreach (string each in Kinds) if (string.Equals(kind, each, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string Settled(string file)
        {
            string plain = (file ?? "").Trim().Trim('"').Replace('/', '\\');
            while (plain.EndsWith("\\", StringComparison.Ordinal)) plain = plain.Substring(0, plain.Length - 1);
            try { plain = Path.GetFullPath(plain); } catch (Exception) { }
            return plain.ToLowerInvariant();
        }

        internal bool Loves(string file)
        {
            if (string.IsNullOrEmpty(file)) return false;
            string want = Settled(file);
            lock (gate) foreach (string each in loved) if (Settled(each) == want) return true;
            return false;
        }

        internal bool Love(string file, bool on)
        {
            string want = (file ?? "").Trim().Trim('"');
            if (want.Length == 0) return false;
            try { want = Path.GetFullPath(want); } catch (Exception) { }
            lock (gate)
            {
                bool held = false;
                foreach (string each in loved) if (Settled(each) == Settled(want)) held = true;
                if (on == held) return false;
                if (on) loved.Add(want);
                else loved.RemoveAll(each => Settled(each) == Settled(want));
            }
            Write();
            return true;
        }

        internal string AddFolder(string folder)
        {
            string want = (folder ?? "").Trim().Trim('"');
            if (want.Length == 0) return "Which folder?";
            try { want = Path.GetFullPath(want); } catch (Exception) { return "That isn't a folder path."; }
            if (!Directory.Exists(want) && FileThere(want)) return AddSong(want);
            if (!Directory.Exists(want)) return "There's no folder at " + want + ". Nothing was created.";
            lock (gate)
            {
                foreach (string each in folders) if (Settled(each) == Settled(want)) return "That folder's already in the playlist.";
                folders.Add(want);
            }
            Write();
            int count = Songs().Count;
            return "Added " + want + ". That's " + count + " song" + (count == 1 ? "" : "s") + " now.";
        }

        internal string AddSong(string file)
        {
            string want = (file ?? "").Trim().Trim('"');
            if (want.Length == 0) return "Which song?";
            try { want = Path.GetFullPath(want); } catch (Exception) { return "That isn't a file path."; }
            if (!FileThere(want)) return "There's no file at " + want + ".";
            if (!Playable(want)) return "I can't play that kind of file.";
            lock (gate)
            {
                removed.RemoveAll(each => Settled(each) == Settled(want));
                bool held = false;
                foreach (string each in added) if (Settled(each) == Settled(want)) held = true;
                if (!held) added.Add(want);
            }
            Write();
            return "Added " + Name(want) + ".";
        }

        internal string Remove(string file)
        {
            string want = (file ?? "").Trim().Trim('"');
            if (want.Length == 0) return "Nothing's playing.";
            lock (gate)
            {
                added.RemoveAll(each => Settled(each) == Settled(want));
                foreach (string each in removed) if (Settled(each) == Settled(want)) return Name(want) + " was already off the playlist.";
                removed.Add(want);
            }
            Write();
            return "Took " + Name(want) + " off the playlist. The file's still on your drive.";
        }

        internal static string Name(string file)
        {
            try { return Path.GetFileNameWithoutExtension(file); }
            catch (Exception) { return file; }
        }

        internal void Write()
        {
            if (path == null) return;
            List<string> roots, ones, gone, dear;
            lock (gate) { roots = new List<string>(folders); ones = new List<string>(added); gone = new List<string>(removed); dear = new List<string>(loved); }
            StringBuilder text = new StringBuilder();
            text.Append("{").Append(Environment.NewLine);
            text.Append("  \"_\": \"Capri's playlist. Folders are scanned top to bottom. Songs under added are played after them.\",").Append(Environment.NewLine);
            text.Append("  \"__\": \"Anything under removed is skipped. Taking a song off the playlist never deletes it from the drive.\",").Append(Environment.NewLine);
            text.Append("  \"____\": \"Songs under loved are favourites: on shuffle they are dealt through the list so one comes up every few tracks.\",").Append(Environment.NewLine);
            text.Append("  \"___\": \"Edit this file by hand if you like. Keep it UTF-8 with no byte order mark.\",").Append(Environment.NewLine);
            text.Append("  \"shuffle\": ").Append(Shuffle ? "true" : "false").Append(",").Append(Environment.NewLine);
            text.Append("  \"repeat\": \"").Append(Repeat).Append("\",").Append(Environment.NewLine);
            Lines(text, "playlist", roots, true);
            Lines(text, "added", ones, true);
            Lines(text, "removed", gone, true);
            Lines(text, "loved", dear, false);
            text.Append("}").Append(Environment.NewLine);
            try
            {
                string folder = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder)) Directory.CreateDirectory(folder);
                File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
            }
            catch (Exception) { }
        }

        private static void Lines(StringBuilder text, string name, List<string> items, bool comma)
        {
            text.Append("  \"").Append(name).Append("\": [");
            if (items.Count == 0) { text.Append("]").Append(comma ? "," : "").Append(Environment.NewLine); return; }
            text.Append(Environment.NewLine);
            for (int i = 0; i < items.Count; i++)
            {
                text.Append("    \"").Append(Escaped(items[i])).Append("\"");
                if (i < items.Count - 1) text.Append(",");
                text.Append(Environment.NewLine);
            }
            text.Append("  ]").Append(comma ? "," : "").Append(Environment.NewLine);
        }

        private static string Escaped(string text)
        {
            StringBuilder made = new StringBuilder();
            foreach (char letter in text ?? "")
            {
                if (letter == '\\') made.Append("\\\\");
                else if (letter == '"') made.Append("\\\"");
                else if (letter == '\n') made.Append("\\n");
                else if (letter == '\r') made.Append("\\r");
                else if (letter == '\t') made.Append("\\t");
                else if (letter < ' ') made.Append("\\u").Append(((int)letter).ToString("x4", CultureInfo.InvariantCulture));
                else made.Append(letter);
            }
            return made.ToString();
        }

        internal void Read()
        {
            if (path == null || !FileThere(path)) return;
            string text;
            try { text = File.ReadAllText(path, new UTF8Encoding(false)); }
            catch (Exception) { return; }
            if (text.Length > 0 && text[0] == '﻿') text = text.Substring(1);
            lock (gate)
            {
                folders.Clear(); added.Clear(); removed.Clear(); loved.Clear();
                Fill(text, "playlist", folders);
                Fill(text, "added", added);
                Fill(text, "removed", removed);
                Fill(text, "loved", loved);
                Shuffle = Regex.IsMatch(text ?? "", @"""shuffle""\s*:\s*true", RegexOptions.IgnoreCase);
                Match how = Regex.Match(text ?? "", @"""repeat""\s*:\s*""(off|one|all)""", RegexOptions.IgnoreCase);
                Repeat = how.Success ? how.Groups[1].Value.ToLowerInvariant() : "off";
            }
        }

        private static void Fill(string text, string name, List<string> into)
        {
            Match block = Regex.Match(text ?? "", "\"" + name + "\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!block.Success) return;
            foreach (Match one in Regex.Matches(block.Groups[1].Value, "\"((?:[^\"\\\\]|\\\\.)*)\""))
            {
                string got = Unescaped(one.Groups[1].Value);
                if (got.Length > 0 && !into.Contains(got)) into.Add(got);
            }
        }

        private static string Unescaped(string text)
        {
            return Regex.Replace(text ?? "", @"\\(u[0-9a-fA-F]{4}|.)", delegate(Match found)
            {
                string what = found.Groups[1].Value;
                if (what.StartsWith("u", StringComparison.OrdinalIgnoreCase)) return ((char)int.Parse(what.Substring(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString();
                if (what == "n") return "\n";
                if (what == "r") return "\r";
                if (what == "t") return "\t";
                return what;
            });
        }
    }

    internal static class FolderTunes
    {
        private static readonly object gate = new object();
        private static System.Windows.Threading.Dispatcher loop;
        private static System.Windows.Media.MediaPlayer player;
        private static string playing;
        private static volatile bool ended;
        private static volatile bool failed;

        internal static string Playing { get { lock (gate) return playing; } }

        private static bool Ready()
        {
            lock (gate)
            {
                if (loop != null) return true;
                System.Threading.ManualResetEvent up = new System.Threading.ManualResetEvent(false);
                System.Threading.Thread spinner = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        loop = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                        player = new System.Windows.Media.MediaPlayer();
                        player.MediaEnded += delegate { ended = true; };
                        player.MediaFailed += delegate { failed = true; ended = true; };
                        up.Set();
                        System.Windows.Threading.Dispatcher.Run();
                    }
                    catch (Exception) { up.Set(); }
                });
                spinner.IsBackground = true;
                spinner.SetApartmentState(System.Threading.ApartmentState.STA);
                spinner.Name = "Capri playlist";
                spinner.Start();
                up.WaitOne(5000);
                return loop != null && player != null;
            }
        }

        private static void OnLoop(Action what)
        {
            if (!Ready()) return;
            try { loop.Invoke(what); }
            catch (Exception) { }
        }

        internal static bool Start(string file)
        {
            if (!Ready()) return false;
            ended = false;
            failed = false;
            lock (gate) paused = false;
            double want = Loudness;
            OnLoop(delegate { try { player.Volume = want; } catch (Exception) { } });
            bool opened = false;
            OnLoop(delegate
            {
                try
                {
                    player.Stop();
                    player.Open(new Uri(file));
                    player.Play();
                    opened = true;
                }
                catch (Exception) { opened = false; }
            });
            if (!opened) return false;
            for (int waited = 0; waited < 12 && !failed; waited++)
            {
                if (Length > 0) break;
                System.Threading.Thread.Sleep(50);
            }
            if (failed) { Stop(); return false; }
            lock (gate) playing = file;
            return true;
        }

        internal static double At
        {
            get
            {
                double found = 0;
                OnLoop(delegate
                {
                    try { found = player.Position.TotalSeconds; }
                    catch (Exception) { found = 0; }
                });
                return found < 0 ? 0 : found;
            }
        }

        internal static double Seconds { get { return Length; } }

        private static double Length
        {
            get
            {
                double found = 0;
                OnLoop(delegate
                {
                    try { found = player.NaturalDuration.HasTimeSpan ? player.NaturalDuration.TimeSpan.TotalSeconds : 0; }
                    catch (Exception) { found = 0; }
                });
                return found;
            }
        }

        internal static void Stop()
        {
            OnLoop(delegate
            {
                try { player.Stop(); player.Close(); }
                catch (Exception) { }
            });
            lock (gate) { playing = null; paused = false; }
            ended = false;
        }

        private static double loud = 0.7;

        internal static double Loudness
        {
            get { lock (gate) return loud; }
            set
            {
                double want = value < 0 ? 0 : value > 1 ? 1 : value;
                lock (gate) loud = want;
                OnLoop(delegate { try { player.Volume = want; } catch (Exception) { } });
            }
        }

        private static bool paused;

        internal static void Pause() { OnLoop(delegate { try { player.Pause(); lock (gate) paused = true; } catch (Exception) { } }); }
        internal static void Resume() { OnLoop(delegate { try { player.Play(); lock (gate) paused = false; } catch (Exception) { } }); }

        internal static bool Paused { get { lock (gate) return playing != null && paused && !ended; } }

        internal static string Mode
        {
            get
            {
                lock (gate) if (playing == null) return "";
                if (ended) return "stopped";
                return "playing";
            }
        }

        internal static bool Busy { get { return Mode == "playing"; } }
    }
}
