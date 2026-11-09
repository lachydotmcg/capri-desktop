using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Code mods' side of her mini player: what Windows' media session says is playing, the presses that go back to the app, its
    // volume in the mixer, and how loud the PC's sound is in bands for a visualizer.
    internal sealed partial class Companion
    {
        private bool modMusicBusy;
        private double modMusicAt = -10;
        private CapriTrack[] modSessions = new CapriTrack[0];
        private readonly Dictionary<string, byte[]> modCovers = new Dictionary<string, byte[]>();
        private readonly Dictionary<string, bool> modLiked = new Dictionary<string, bool>();
        private readonly Dictionary<string, double[]> modVolumes = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

        // A running mod that shows its own player keeps hers away.
        internal bool PlayerReplaced { get { return ModsListen(host => host.musicHidesPlayer); } }

        internal void MusicSoon() { modMusicAt = -10; }

        // Every frame: for mods that use Music, reads the media session about once a second, off her window's thread, with the cover,
        // the mixer volume and Spotify's heart for what they show.
        internal void CheckModMusic(double now)
        {
            if (modMusicBusy || now - modMusicAt < 1) return;
            ModHost[] hosts = Array.FindAll(LiveHosts(), host => host.musicWanted);
            if (hosts.Length == 0) return;
            modMusicBusy = true;
            modMusicAt = now;
            List<string> follows = new List<string>();
            foreach (ModHost host in hosts) if (host.musicFollow != null && !follows.Contains(host.musicFollow)) follows.Add(host.musicFollow);
            HashSet<string> likedRead = new HashSet<string>(modLiked.Keys);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<CapriTrack> tracks = new List<CapriTrack>();
                CapriTrack pick = null;
                Dictionary<string, byte[]> covers = new Dictionary<string, byte[]>();
                Dictionary<string, double[]> volumes = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
                string likedKey = null;
                bool? liked = null;
                try
                {
                    List<Tune> all = Tunes.Shared();
                    // Windows can list the same session twice: once as its pick and again among the rest.
                    if (all != null) foreach (Tune tune in all) if (tune.Title.Length > 0 && !tracks.Exists(track => track.Key == tune.Key)) tracks.Add(Track(tune));
                    pick = Pick(tracks);
                    List<CapriTrack> shown = new List<CapriTrack>();
                    if (pick != null) shown.Add(pick);
                    foreach (string follow in follows)
                    {
                        CapriTrack followed = tracks.Find(track => track.AppId == follow);
                        if (followed != null && !shown.Contains(followed)) shown.Add(followed);
                    }
                    foreach (CapriTrack track in shown)
                    {
                        byte[] cover = null;
                        try { cover = Tunes.Cover((Tune)track.Source); } catch (Exception) { }
                        if (cover != null) covers[track.Key] = cover;
                    }
                    List<AppSound> sounds = null;
                    try { sounds = Mixer.Sounds(); } catch (Exception) { }
                    if (sounds != null)
                        foreach (CapriTrack track in shown)
                        {
                            string program = Tunes.Program(track.AppId) + ".exe";
                            double level = -1;
                            bool muted = true, any = false;
                            foreach (AppSound sound in sounds)
                            {
                                if (!string.Equals(sound.App, program, StringComparison.OrdinalIgnoreCase)) continue;
                                any = true;
                                level = Math.Max(level, sound.Level);
                                muted &= sound.Muted;
                            }
                            volumes[track.AppId] = new[] { any ? level : -1, any && muted ? 1 : 0 };
                        }
                    // Spotify's heart is read from its window, which shows the song it's playing: once per song.
                    CapriTrack spotify = shown.Find(track => ((Tune)track.Source).Spotify && track.Playing);
                    if (spotify != null && !likedRead.Contains(spotify.Key))
                    {
                        likedKey = spotify.Key;
                        try { liked = Tunes.Hearted(); } catch (Exception) { }
                    }
                }
                catch (Exception) { }
                Post(delegate
                {
                    modMusicBusy = false;
                    ModsMusic(tracks, pick, covers, volumes, likedKey, liked);
                });
            });
        }

        internal static CapriTrack Track(Tune tune)
        {
            return new CapriTrack
            {
                Title = tune.Title, Artist = tune.Artist, Album = tune.Album ?? "", App = Tunes.AppName(tune.App), AppId = tune.App,
                Playing = tune.Playing, Music = tune.Music, Video = tune.Video, Position = tune.At(Tunes.Now), Length = tune.Length,
                Rate = tune.Rate, CanSeek = tune.CanSeek, CanSkip = tune.CanSkip, CanBack = tune.CanBack, Shuffle = tune.Shuffle,
                Repeat = tune.Repeat, Key = tune.Key, ReadAt = DateTime.UtcNow, Source = tune
            };
        }

        // Like her mini player: Windows' own pick if it's playing, else any that's playing, else the first paused one.
        private static CapriTrack Pick(List<CapriTrack> tracks)
        {
            foreach (CapriTrack track in tracks) if (track.Playing) return track;
            return tracks.Count > 0 ? tracks[0] : null;
        }

        private void ModsMusic(List<CapriTrack> tracks, CapriTrack pick, Dictionary<string, byte[]> covers, Dictionary<string, double[]> volumes, string likedKey, bool? liked)
        {
            modSessions = tracks.ToArray();
            foreach (KeyValuePair<string, byte[]> cover in covers) modCovers[cover.Key] = cover.Value;
            // Only the covers still on show are kept.
            foreach (string key in new List<string>(modCovers.Keys)) if (!tracks.Exists(track => track.Key == key)) modCovers.Remove(key);
            foreach (KeyValuePair<string, double[]> volume in volumes) modVolumes[volume.Key] = volume.Value;
            if (likedKey != null && liked.HasValue && !modLiked.ContainsKey(likedKey)) modLiked[likedKey] = liked.Value;
            if (modLiked.Count > 200) modLiked.Clear();
            ModsFire(delegate(ModHost host)
            {
                if (!host.musicWanted) return;
                CapriTrack track = host.musicFollow == null ? pick : tracks.Find(each => each.AppId == host.musicFollow);
                string key = track == null ? null : track.Key;
                bool changed = key != host.musicKey;
                host.musicNow = track;
                host.musicKey = key;
                if (changed && host.musicChanged != null) host.musicChanged(track);
                if (host.musicUpdated != null) host.musicUpdated(track);
            });
        }

        // A press goes to the app off her window's thread; the next read, straight after, shows how it went.
        internal void MusicPress(Tune tune, string what, string key)
        {
            if (what == "like" || what == "unlike") modLiked[key] = what == "like";
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool took = false;
                try { took = Tunes.Send(tune, what); } catch (Exception) { }
                Post(delegate
                {
                    if (!took && (what == "like" || what == "unlike")) modLiked.Remove(key);
                    modMusicAt = Math.Min(modMusicAt, clock.Elapsed.TotalSeconds - 0.7);
                    tuneAt = -10;
                });
            });
        }

        internal bool? MusicLiked(CapriTrack track)
        {
            bool liked;
            if (track == null || !((Tune)track.Source).Spotify) return null;
            lock (modLiked) return modLiked.TryGetValue(track.Key, out liked) ? liked : (bool?)null;
        }

        internal byte[] MusicCover(CapriTrack track)
        {
            byte[] cover;
            return track != null && modCovers.TryGetValue(track.Key, out cover) ? cover : null;
        }

        internal double[] MusicVolume(CapriTrack track)
        {
            double[] volume;
            return track != null && modVolumes.TryGetValue(track.AppId, out volume) ? volume : new double[] { -1, 0 };
        }

        internal void MusicSetVolume(CapriTrack track, double? level, bool? muted)
        {
            if (track == null) return;
            double[] known = MusicVolume(track);
            if (known[0] >= 0) modVolumes[track.AppId] = new[] { level.HasValue ? Math.Max(0, Math.Min(1, level.Value)) : known[0], muted.HasValue ? (muted.Value ? 1 : 0) : known[1] };
            string program = Tunes.Program(track.AppId) + ".exe";
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    List<AppSound> sounds = Mixer.Sounds();
                    if (sounds == null) return;
                    HashSet<int> done = new HashSet<int>();
                    foreach (AppSound sound in sounds)
                        if (string.Equals(sound.App, program, StringComparison.OrdinalIgnoreCase) && done.Add(sound.Id))
                            Mixer.Set(sound.Id, level.HasValue ? (float)Math.Max(0, Math.Min(1, level.Value)) : (float?)null, muted);
                }
                catch (Exception) { }
            });
        }

        internal CapriTrack[] MusicSessions() { return (CapriTrack[])modSessions.Clone(); }

        // Music: what's playing reaches a mod with its album, cover, heart and shuffle, presses go back to the right app, Follow
        // switches apps, a mod can keep her plate away, and a mod without the permission is refused.
        private void CheckMusicHooks(List<string> checks, Func<string, string, string, string> write, Func<Func<bool>, bool> until)
        {
            Func<List<Tune>> sessionsWas = Tunes.Sessions;
            Func<Tune, string, bool> sendWas = Tunes.Send;
            Func<Tune, byte[]> coverWas = Tunes.Cover;
            Func<bool?> heartWas = Tunes.Hearted;
            Func<SoundTap> tapWas = SoundBands.Open;
            List<string> sent = new List<string>();
            byte[] cover = { 0x89, 0x50, 0x4E, 0x47, 1, 2, 3 };
            Tune spotify = new Tune { Title = "Smoke Song", Artist = "Smoke Band", Album = "Smoke Album", App = "Spotify.exe", Playing = true, Music = true, Position = 30, Length = 200, ReadAt = Tunes.Now, CanSeek = true, Shuffle = false, Repeat = "off" };
            Tune chrome = new Tune { Title = "Smoke Video", Artist = "Smoke Channel", App = "Chrome", Playing = false, Video = true, Position = 5, Length = 60, ReadAt = Tunes.Now };
            try
            {
                Tunes.Sessions = delegate { return new List<Tune> { spotify, chrome }; };
                Tunes.Send = delegate(Tune tune, string what) { lock (sent) sent.Add(tune.App + " " + what); return true; };
                Tunes.Cover = delegate(Tune tune) { return tune == spotify ? cover : null; };
                Tunes.Hearted = delegate { return true; };
                SoundBands.Open = delegate { return null; };
                Tunes.Forget();
                string body = "public class M : ICapriMod { ICapriHost c;\n"
                    + "void Mark(string name, string text) { File.WriteAllText(Path.Combine(c.Folder, name), text); }\n"
                    + "public void Start(ICapriHost h) { c = h; h.Music.BuiltInPlayer = false;\n"
                    + "h.Music.Changed += t => Mark(\"changed\", t == null ? \"null\" : t.Title + \"|\" + t.Artist + \"|\" + t.Album + \"|\" + t.App + \"|\" + t.Shuffle + \"|\" + t.Repeat);\n"
                    + "h.Music.Updated += t => Mark(\"updated\", t == null ? \"null\" : t.App + \" \" + t.Playing + \" \" + (t.At() >= 30) + \" \" + h.Music.Sessions.Length); }\n"
                    + "public bool Heard(string w) { return false; } public void Stop() { } }";
                if (!mainframe.ImportMod(write("music3", "music", body))) throw new Exception("A mod using Music was refused: " + mainframe.ModStatusText);
                string data = Path.Combine(ModList.Folder, "music3-data");
                Func<string, string> mark = delegate(string name) { string file = Path.Combine(data, name); return File.Exists(file) ? File.ReadAllText(file) : null; };
                ModHost host = Array.Find(LiveHosts(), each => each.Id == "music3");
                if (host == null || !PlayerReplaced || PlayerAllowed) throw new Exception("A mod that keeps her mini player away didn't.");
                if (!until(delegate { CheckModMusic(clock.Elapsed.TotalSeconds); return mark("changed") == "Smoke Song|Smoke Band|Smoke Album|Spotify|False|off" && mark("updated") == "Spotify True True 2"; }))
                    throw new Exception("What's playing didn't reach a mod: " + mark("changed") + " / " + mark("updated"));
                ICapriMusic music = host;
                if (!until(delegate { CheckModMusic(clock.Elapsed.TotalSeconds); return music.Art != null && music.Liked == true; }) || music.Art.Length != cover.Length)
                    throw new Exception("A mod didn't get the cover or Spotify's heart.");
                if (!music.Next() || !music.Seek(42.5) || !music.SetShuffle(true) || !music.SetRepeat("one") || !music.Like(false) || !music.Toggle())
                    throw new Exception("A press from a mod wasn't taken.");
                bool refused = false;
                try { music.SetRepeat("sideways"); } catch (ArgumentException) { refused = true; }
                string wanted = "Spotify.exe next,Spotify.exe seek:42.5,Spotify.exe shuffle:on,Spotify.exe repeat:one,Spotify.exe unlike,Spotify.exe pause";
                // Each press goes on its own, so they can land in any order.
                Func<string> got = delegate { lock (sent) { List<string> sorted = new List<string>(sent); sorted.Sort(StringComparer.Ordinal); return string.Join(",", sorted.ToArray()); } };
                List<string> expected = new List<string>(wanted.Split(','));
                expected.Sort(StringComparer.Ordinal);
                if (!refused || !until(() => got() == string.Join(",", expected.ToArray())))
                    throw new Exception("A mod's presses didn't reach the app: " + string.Join(",", sent.ToArray()));
                if (music.Liked != false) throw new Exception("Unliking from a mod didn't show straight away.");
                float[] levels = music.Levels(16);
                if (levels.Length != 16 || Array.Exists(levels, level => level != 0) || music.Loudness != 0) throw new Exception("A mod's sound levels came back wrong.");
                double[] real = new double[64], imaginary = new double[64];
                for (int i = 0; i < 64; i++) real[i] = Math.Sin(2 * Math.PI * 8 * i / 64);
                SoundBands.Transform(real, imaginary);
                if (Math.Abs(Math.Sqrt(real[8] * real[8] + imaginary[8] * imaginary[8]) - 32) > 0.01 || Math.Abs(real[5]) + Math.Abs(imaginary[5]) > 1e-6) throw new Exception("The visualizer's FFT is wrong.");
                checks.Add("PASS mod music: what's playing reaches a mod with its album, cover and Spotify heart, its presses reach the app, and its sound levels read");
                music.Follow("Chrome");
                if (!until(delegate { CheckModMusic(clock.Elapsed.TotalSeconds); return mark("changed") == "Smoke Video|Smoke Channel||Chrome||"; }) || music.Following != "Chrome" || music.Art != null)
                    throw new Exception("Following another app didn't switch a mod to it: " + mark("changed"));
                if (!mainframe.ImportMod(write("music-nosy", null, "public class M : ICapriMod { public void Start(ICapriHost h) { try { object x = h.Music.Now; File.WriteAllText(Path.Combine(h.Folder, \"nosy\"), \"read\"); } catch (CapriPermissionException) { File.WriteAllText(Path.Combine(h.Folder, \"nosy\"), \"refused\"); } } public bool Heard(string w) { return false; } public void Stop() { } }")))
                    throw new Exception("A mod without permissions was refused: " + mainframe.ModStatusText);
                string nosy = Path.Combine(ModList.Folder, "music-nosy-data", "nosy");
                if (!until(() => File.Exists(nosy)) || File.ReadAllText(nosy) != "refused") throw new Exception("A mod without the music permission read what's playing.");
                ModList.Remove(ModList.All.Find(mod => mod.File.Id == "music3"));
                ModList.Remove(ModList.All.Find(mod => mod.File.Id == "music-nosy"));
                for (int i = 0; i < 10; i++) { Application.DoEvents(); Thread.Sleep(20); }
                if (PlayerReplaced) throw new Exception("Removing a mod that hid her mini player kept it away.");
                checks.Add("PASS mod music: Follow switches a mod to another app, a mod without the permission is refused, and removing one brings her mini player back");
            }
            finally
            {
                Tunes.Sessions = sessionsWas;
                Tunes.Send = sendWas;
                Tunes.Cover = coverWas;
                Tunes.Hearted = heartWas;
                SoundBands.Open = tapWas;
                Tunes.Forget();
            }
        }

        private sealed partial class ModHost
        {
            internal Action<CapriTrack> musicChanged, musicUpdated;
            internal volatile bool musicWanted, musicHidesPlayer;
            internal volatile string musicFollow;
            internal volatile CapriTrack musicNow;
            internal string musicKey;

            private void Music()
            {
                Need("music");
                if (musicWanted || over) return;
                musicWanted = true;
                capri.OnWindow(capri.MusicSoon);
            }

            private bool Press(string what)
            {
                Music();
                CapriTrack now = musicNow;
                Tune tune = now == null ? null : now.Source as Tune;
                if (tune == null || over) return false;
                capri.OnWindow(delegate { capri.MusicPress(tune, what, now.Key); });
                return true;
            }

            ICapriMusic ICapriHost.Music { get { return this; } }
            event Action<CapriTrack> ICapriMusic.Changed { add { Music(); musicChanged += value; } remove { musicChanged -= value; } }
            event Action<CapriTrack> ICapriMusic.Updated { add { Music(); musicUpdated += value; } remove { musicUpdated -= value; } }
            CapriTrack ICapriMusic.Now { get { Music(); return musicNow; } }
            CapriTrack[] ICapriMusic.Sessions { get { Music(); return capri.FromWindow(() => capri.MusicSessions()) ?? new CapriTrack[0]; } }
            string ICapriMusic.Following { get { Need("music"); return musicFollow; } }

            void ICapriMusic.Follow(string appId)
            {
                Music();
                musicFollow = string.IsNullOrWhiteSpace(appId) ? null : appId.Trim();
                capri.OnWindow(capri.MusicSoon);
            }

            bool ICapriMusic.Play() { return Press("play"); }
            bool ICapriMusic.Pause() { return Press("pause"); }

            bool ICapriMusic.Toggle()
            {
                Music();
                CapriTrack now = musicNow;
                return now != null && Press(now.Playing ? "pause" : "play");
            }

            bool ICapriMusic.Next() { Music(); CapriTrack now = musicNow; return now != null && now.CanSkip && Press("next"); }
            bool ICapriMusic.Previous() { Music(); CapriTrack now = musicNow; return now != null && now.CanBack && Press("previous"); }

            bool ICapriMusic.Seek(double seconds)
            {
                Music();
                CapriTrack now = musicNow;
                if (now == null || !now.CanSeek || double.IsNaN(seconds) || double.IsInfinity(seconds)) return false;
                double to = Math.Max(0, now.Length > 0 ? Math.Min(seconds, now.Length) : seconds);
                return Press("seek:" + to.ToString("0.###", CultureInfo.InvariantCulture));
            }

            bool ICapriMusic.SetShuffle(bool on) { return Press(on ? "shuffle:on" : "shuffle:off"); }

            bool ICapriMusic.SetRepeat(string mode)
            {
                string chosen = (mode ?? "").Trim().ToLowerInvariant();
                if (chosen != "off" && chosen != "one" && chosen != "all") throw new ArgumentException("Repeat is \"off\", \"one\" or \"all\".");
                return Press("repeat:" + chosen);
            }

            bool? ICapriMusic.Liked { get { Music(); CapriTrack now = musicNow; return capri.FromWindow(() => capri.MusicLiked(now)); } }

            bool ICapriMusic.Like(bool on)
            {
                Music();
                CapriTrack now = musicNow;
                return now != null && ((Tune)now.Source).Spotify && Press(on ? "like" : "unlike");
            }

            byte[] ICapriMusic.Art { get { Music(); CapriTrack now = musicNow; return capri.FromWindow(() => capri.MusicCover(now)); } }

            double ICapriMusic.Volume
            {
                get { Music(); CapriTrack now = musicNow; double[] volume = capri.FromWindow(() => capri.MusicVolume(now)); return volume == null ? -1 : volume[0]; }
                set { Music(); CapriTrack now = musicNow; double level = value; if (!double.IsNaN(level)) capri.OnWindow(delegate { capri.MusicSetVolume(now, level, null); }); }
            }

            bool ICapriMusic.Muted
            {
                get { Music(); CapriTrack now = musicNow; double[] volume = capri.FromWindow(() => capri.MusicVolume(now)); return volume != null && volume[1] > 0; }
                set { Music(); CapriTrack now = musicNow; bool muted = value; capri.OnWindow(delegate { capri.MusicSetVolume(now, null, muted); }); }
            }

            float[] ICapriMusic.Levels(int bands) { Need("music"); return SoundBands.Levels(bands); }
            float ICapriMusic.Loudness { get { Need("music"); return SoundBands.Loudness; } }

            bool ICapriMusic.BuiltInPlayer
            {
                get { Need("music"); return !musicHidesPlayer; }
                set { Need("music"); musicHidesPlayer = !value && !over; capri.OnWindow(delegate { capri.tuneAt = -10; }); }
            }

            bool ICapriMusic.Open()
            {
                Music();
                CapriTrack now = musicNow;
                Tune tune = now == null ? null : now.Source as Tune;
                if (tune == null) return false;
                ThreadPool.QueueUserWorkItem(delegate { try { Tunes.Open(tune); } catch (Exception) { } });
                return true;
            }
        }
    }

    // How loud the PC's sound is in bands from 40 Hz to 16 kHz, from the speakers' loopback, for mods' visualizers. It listens only
    // while something keeps asking and lets go of the speakers a few seconds after the last ask.
    internal static class SoundBands
    {
        private const int Size = 2048, Count = 64;
        private const double Lowest = 40, Highest = 16000;
        private static readonly object gate = new object();
        private static readonly float[] bands = new float[Count];
        private static float loud;
        private static double askedAt = -10;
        private static Thread thread;
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        // Tests swap it out.
        internal static Func<SoundTap> Open = delegate { return SoundTap.Open(true); };

        internal static bool Listening { get { lock (gate) return thread != null; } }

        internal static float Loudness { get { Ask(); lock (gate) return loud; } }

        internal static float[] Levels(int count)
        {
            Ask();
            int wanted = Math.Max(1, Math.Min(512, count));
            float[] result = new float[wanted];
            lock (gate)
            {
                if (wanted <= Count)
                    for (int i = 0; i < wanted; i++)
                    {
                        int from = i * Count / wanted, to = Math.Max(from + 1, (i + 1) * Count / wanted);
                        float most = 0;
                        for (int band = from; band < to; band++) most = Math.Max(most, bands[band]);
                        result[i] = most;
                    }
                else
                    for (int i = 0; i < wanted; i++)
                    {
                        double at = i * (Count - 1.0) / (wanted - 1);
                        int low = (int)at, high = Math.Min(Count - 1, low + 1);
                        result[i] = (float)(bands[low] * (1 - (at - low)) + bands[high] * (at - low));
                    }
            }
            return result;
        }

        private static void Ask()
        {
            lock (gate)
            {
                askedAt = clock.Elapsed.TotalSeconds;
                if (thread != null) return;
                thread = new Thread(Listen) { IsBackground = true, Name = "mod sound bands" };
                thread.Start();
            }
        }

        private static void Listen()
        {
            SoundTap tap = null;
            float[] ring = new float[Size];
            int written = 0, rate = 48000;
            double heardAt = -10, analysedAt = 0, openedAt = -10, deviceAt = 0;
            double[] real = new double[Size], imaginary = new double[Size], window = new double[Size];
            for (int i = 0; i < Size; i++) window[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (Size - 1));
            try
            {
                while (true)
                {
                    double now = clock.Elapsed.TotalSeconds;
                    lock (gate)
                        if (now - askedAt > 3)
                        {
                            thread = null;
                            Array.Clear(bands, 0, Count);
                            loud = 0;
                            return;
                        }
                    // Speakers that changed, like headphones plugged in, are opened again.
                    if (tap != null && now - deviceAt > 3)
                    {
                        deviceAt = now;
                        if (SoundTap.DefaultDevice() != tap.Device) { tap.Dispose(); tap = null; }
                    }
                    if (tap == null && now - openedAt > 2)
                    {
                        openedAt = now;
                        try { tap = Open(); } catch (Exception) { tap = null; }
                        if (tap != null) rate = tap.Rate;
                    }
                    bool heard = false;
                    if (tap != null)
                    {
                        long newest;
                        bool read = tap.Read(null, null, out newest, delegate(byte[] packet, long time, int packetRate)
                        {
                            for (int i = 0; i + 3 < packet.Length; i += 4)
                            {
                                short left = (short)(packet[i] | packet[i + 1] << 8), right = (short)(packet[i + 2] | packet[i + 3] << 8);
                                ring[written] = (left + right) / 65536f;
                                written = (written + 1) % Size;
                            }
                            heard = true;
                        });
                        if (!read) { tap.Dispose(); tap = null; }
                    }
                    if (heard) heardAt = now;
                    if (now - analysedAt >= 1 / 60.0)
                    {
                        double step = Math.Min(0.1, now - analysedAt);
                        analysedAt = now;
                        if (now - heardAt < 0.2) Analyse(ring, written, rate, real, imaginary, window, step);
                        else Fade(step);
                    }
                    Thread.Sleep(6);
                }
            }
            catch (Exception) { lock (gate) { thread = null; Array.Clear(bands, 0, Count); loud = 0; } }
            finally { if (tap != null) tap.Dispose(); }
        }

        private static void Analyse(float[] ring, int written, int rate, double[] real, double[] imaginary, double[] window, double step)
        {
            double power = 0;
            for (int i = 0; i < Size; i++)
            {
                float sample = ring[(written + i) % Size];
                power += sample * sample;
                real[i] = sample * window[i];
                imaginary[i] = 0;
            }
            Transform(real, imaginary);
            float[] fresh = new float[Count];
            double ratio = Highest / Lowest;
            for (int band = 0; band < Count; band++)
            {
                double low = Lowest * Math.Pow(ratio, band / (double)Count), high = Lowest * Math.Pow(ratio, (band + 1) / (double)Count);
                int from = Math.Max(1, (int)Math.Floor(low * Size / rate)), to = Math.Min(Size / 2 - 1, Math.Max(from, (int)Math.Ceiling(high * Size / rate)));
                double most = 0;
                for (int bin = from; bin <= to; bin++) most = Math.Max(most, real[bin] * real[bin] + imaginary[bin] * imaginary[bin]);
                // A full-scale sine peaks at a quarter of the size; music falls off about 3 dB an octave, so the treble is lifted to match.
                double amplitude = Math.Sqrt(most) / (Size / 4.0);
                double decibels = 20 * Math.Log10(amplitude + 1e-9) + 3 * Math.Log(Math.Sqrt(low * high) / 250, 2);
                fresh[band] = (float)Math.Max(0, Math.Min(1, (decibels + 58) / 50));
            }
            float level = (float)Math.Max(0, Math.Min(1, Math.Sqrt(power / Size) * 3.2));
            lock (gate)
            {
                // Quick to rise and slower to fall, like a meter.
                double fall = Math.Pow(0.08, step);
                for (int band = 0; band < Count; band++) bands[band] = fresh[band] > bands[band] ? bands[band] + (fresh[band] - bands[band]) * 0.75f : (float)(bands[band] * fall + fresh[band] * (1 - fall));
                loud = level > loud ? loud + (level - loud) * 0.6f : (float)(loud * fall + level * (1 - fall));
            }
        }

        private static void Fade(double step)
        {
            float keep = (float)Math.Pow(0.02, step);
            lock (gate)
            {
                for (int band = 0; band < Count; band++) bands[band] *= keep;
                loud *= keep;
            }
        }

        // An in-place radix-2 FFT.
        internal static void Transform(double[] real, double[] imaginary)
        {
            int n = real.Length;
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double swap = real[i]; real[i] = real[j]; real[j] = swap;
                    swap = imaginary[i]; imaginary[i] = imaginary[j]; imaginary[j] = swap;
                }
            }
            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = -2 * Math.PI / length;
                double stepReal = Math.Cos(angle), stepImaginary = Math.Sin(angle);
                for (int start = 0; start < n; start += length)
                {
                    double turnReal = 1, turnImaginary = 0;
                    for (int k = 0; k < length / 2; k++)
                    {
                        int a = start + k, b = a + length / 2;
                        double bReal = real[b] * turnReal - imaginary[b] * turnImaginary, bImaginary = real[b] * turnImaginary + imaginary[b] * turnReal;
                        real[b] = real[a] - bReal; imaginary[b] = imaginary[a] - bImaginary;
                        real[a] += bReal; imaginary[a] += bImaginary;
                        double nextReal = turnReal * stepReal - turnImaginary * stepImaginary;
                        turnImaginary = turnReal * stepImaginary + turnImaginary * stepReal;
                        turnReal = nextReal;
                    }
                }
            }
        }
    }
}
