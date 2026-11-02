using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using System.Windows.Forms;
using Windows.Media.Control;

namespace CapriDesktop
{
    // What's playing, as Windows' media session has it: the title and artist the volume flyout shows.
    internal sealed class Tune
    {
        internal string Title = "";
        internal string Artist = "";
        // The app playing it, as Windows names it: Spotify.exe, Chrome, MSEdge, SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify.
        internal string App = "";
        internal bool Playing;
        // Between songs, which Playing counts too.
        internal bool Changing;
        // What the app says it is, when it says.
        internal bool Music;
        internal bool Video;
        // Where it was and how long it is, in seconds, when it was read. Length is 0 when the app doesn't say.
        internal double Start;
        internal double Position;
        internal double Length;
        internal double ReadAt;
        internal double Rate = 1;
        internal bool CanSeek;
        internal bool CanSkip = true;
        internal bool CanBack = true;
        internal bool CanPlay = true;
        internal bool CanPause = true;
        internal bool CanToggle = true;
        // Windows' own session for it, or null for one made up in a test.
        internal object Session;

        internal bool Spotify { get { return App.IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0; } }

        // The same song in the same app.
        internal string Key { get { return App + "\n" + Title + "\n" + Artist; } }

        // Where it's got to by now: it plays on from where it was read.
        internal double At(double now)
        {
            double at = Position + (Playing ? Math.Max(0, now - ReadAt) * Rate : 0);
            return Length > 0 ? Math.Min(at, Length) : at;
        }
    }

    // Her mini player's side of Windows' media session: what's playing, and the presses that go back to the app playing it.
    internal static class Tunes
    {
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static GlobalSystemMediaTransportControlsSessionManager manager;
        private static readonly object sharing = new object(), pressing = new object();
        private static List<Tune> shared;
        private static double sharedAt = -10, pressedAt = -10;
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window, int command);

        internal static double Now { get { return clock.Elapsed.TotalSeconds; } }

        // What's playing, or null. Tests swap it out.
        internal static Func<Tune> Read = ReadSession;
        // Every session Windows has, the one it puts first leading, or null. Tests swap it out.
        internal static Func<List<Tune>> Sessions = ReadSessions;
        // The session Windows puts first, playing or paused, or null. Tests swap it out.
        internal static Func<Tune> Current = ReadCurrent;
        internal static Func<Tune> Spotify = ReadSpotify;
        // Sends previous, next, play, pause, seek:<seconds>, like or unlike to the app playing the tune. True when it went through.
        internal static Func<Tune, string, bool> Send = SendToSession;
        // Brings a window to the front. Tests swap it out.
        internal static Func<IntPtr, bool> Bring = Hands.Front;
        // Whether Spotify's own heart is filled for the song playing in it, read without pressing anything; null when it can't
        // tell. Tests swap it out.
        internal static Func<bool?> Hearted = SpotifyHearted;

        private static readonly string[,] Apps =
        {
            // What Windows calls the app, the name she shows, and its program.
            { "spotify", "Spotify", "Spotify" },
            { "^chrome", "Chrome", "chrome" },
            { "msedge", "Edge", "msedge" },
            { "firefox|^308046B0AF4A39CB$", "Firefox", "firefox" },
            { "brave", "Brave", "brave" },
            { "opera", "Opera", "opera" },
            { "vivaldi", "Vivaldi", "vivaldi" },
            { "zunemusic", "Media Player", "Microsoft.Media.Player" },
            { "applemusic|itunes", "Apple Music", "AppleMusic" },
            { "amazonmusic", "Amazon Music", "Amazon Music" },
            { "tidal", "TIDAL", "TIDAL" },
            { "deezer", "Deezer", "Deezer" },
            { "soundcloud", "SoundCloud", "SoundCloud" },
            { "foobar", "foobar2000", "foobar2000" },
            { "musicbee", "MusicBee", "MusicBee" },
            { "aimp", "AIMP", "AIMP" },
            { "winamp", "Winamp", "winamp" },
            { "vlc", "VLC", "vlc" },
            { "discord", "Discord", "Discord" }
        };

        // The app's name to show: Spotify, Chrome, Media Player, or the program's own name.
        internal static string AppName(string app)
        {
            string id = app ?? "";
            for (int i = 0; i < Apps.GetLength(0); i++) if (Regex.IsMatch(id, Apps[i, 0], RegexOptions.IgnoreCase)) return Apps[i, 1];
            Match store = Regex.Match(id, @"^[^!_]*?\.([^.!_]+)_[^!]*!");
            if (store.Success) return store.Groups[1].Value;
            Match file = Regex.Match(id, @"([^\\/]+?)(?:\.exe)?$", RegexOptions.IgnoreCase);
            return file.Success && file.Groups[1].Value.Length > 0 ? file.Groups[1].Value : "your player";
        }

        private static string Program(string app)
        {
            string id = app ?? "";
            for (int i = 0; i < Apps.GetLength(0); i++) if (Regex.IsMatch(id, Apps[i, 0], RegexOptions.IgnoreCase)) return Apps[i, 2];
            return Regex.Replace(id, @"\.exe$", "", RegexOptions.IgnoreCase);
        }

        private static GlobalSystemMediaTransportControlsSessionManager Manager()
        {
            if (manager == null)
            {
                Task<GlobalSystemMediaTransportControlsSessionManager> asking = GlobalSystemMediaTransportControlsSessionManager.RequestAsync().AsTask();
                if (asking.Wait(5000)) manager = asking.Result;
            }
            return manager;
        }

        // The session Windows puts first, the one its volume flyout shows, if it's playing; otherwise any that's playing, then
        // the first paused one. Sessions without a title, like a call or a game, don't count.
        private static Tune ReadSession()
        {
            List<Tune> tunes = Shared();
            if (tunes == null) return null;
            Tune paused = null;
            foreach (Tune tune in tunes)
            {
                if (tune.Title.Length == 0) continue;
                if (tune.Playing) return tune;
                if (paused == null) paused = tune;
            }
            return paused;
        }

        // Her mini player and her dancing both look every second or two, so they share a read for most of a second. One that
        // started before a press could show in it isn't shared.
        internal static List<Tune> Shared()
        {
            lock (sharing)
            {
                double now = Now, pressed;
                lock (pressing) pressed = pressedAt;
                if (shared != null && now - sharedAt < 0.9 && sharedAt >= pressed + 0.3) return shared;
                shared = Sessions();
                sharedAt = now;
                return shared;
            }
        }

        // For tests: no read to share and no press to wait out.
        internal static void Forget()
        {
            lock (sharing) shared = null;
            lock (pressing) pressedAt = -10;
        }

        internal static void Pressed() { lock (pressing) pressedAt = Now; }

        private static List<Tune> ReadSessions()
        {
            GlobalSystemMediaTransportControlsSessionManager sessions = Manager();
            if (sessions == null) return null;
            try
            {
                List<GlobalSystemMediaTransportControlsSession> order = new List<GlobalSystemMediaTransportControlsSession>();
                GlobalSystemMediaTransportControlsSession current = sessions.GetCurrentSession();
                if (current != null) order.Add(current);
                foreach (GlobalSystemMediaTransportControlsSession session in sessions.GetSessions()) if (!ReferenceEquals(session, current)) order.Add(session);
                List<Tune> tunes = new List<Tune>();
                foreach (GlobalSystemMediaTransportControlsSession session in order) tunes.Add(Describe(session));
                return tunes;
            }
            catch (Exception) { manager = null; return null; }
        }

        private static Tune ReadCurrent()
        {
            GlobalSystemMediaTransportControlsSessionManager sessions = Manager();
            if (sessions == null) return null;
            try
            {
                GlobalSystemMediaTransportControlsSession current = sessions.GetCurrentSession();
                if (current == null) return null;
                Tune tune = Describe(current);
                return tune.Title.Length == 0 ? null : tune;
            }
            catch (Exception) { manager = null; return null; }
        }

        private static Tune ReadSpotify()
        {
            try
            {
                GlobalSystemMediaTransportControlsSessionManager sessions = Manager();
                if (sessions == null) return null;
                foreach (GlobalSystemMediaTransportControlsSession session in sessions.GetSessions())
                    if ((session.SourceAppUserModelId ?? "").IndexOf("spotify", StringComparison.OrdinalIgnoreCase) >= 0) return Describe(session);
            }
            catch (Exception) { manager = null; }
            return null;
        }

        private static Tune Describe(GlobalSystemMediaTransportControlsSession session)
        {
            Tune tune = new Tune { Session = session, App = session.SourceAppUserModelId ?? "", ReadAt = Now };
            Task<GlobalSystemMediaTransportControlsSessionMediaProperties> asking = session.TryGetMediaPropertiesAsync().AsTask();
            GlobalSystemMediaTransportControlsSessionMediaProperties song = asking.Wait(2000) ? asking.Result : null;
            if (song != null)
            {
                tune.Title = Clean(song.Title);
                tune.Artist = Clean(song.Artist);
                if (tune.Artist.Length == 0) tune.Artist = Clean(song.AlbumArtist);
            }
            GlobalSystemMediaTransportControlsSessionPlaybackInfo info = session.GetPlaybackInfo();
            if (info != null)
            {
                tune.Changing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Changing;
                tune.Playing = info.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing || tune.Changing;
                Windows.Media.MediaPlaybackType? kind = info.PlaybackType;
                tune.Music = kind.HasValue && kind.Value == Windows.Media.MediaPlaybackType.Music;
                tune.Video = kind.HasValue && kind.Value == Windows.Media.MediaPlaybackType.Video;
                GlobalSystemMediaTransportControlsSessionPlaybackControls controls = info.Controls;
                if (controls != null)
                {
                    tune.CanSkip = controls.IsNextEnabled;
                    tune.CanBack = controls.IsPreviousEnabled;
                    tune.CanSeek = controls.IsPlaybackPositionEnabled;
                    tune.CanPlay = controls.IsPlayEnabled;
                    tune.CanPause = controls.IsPauseEnabled;
                    tune.CanToggle = controls.IsPlayPauseToggleEnabled;
                }
                double? rate = info.PlaybackRate;
                if (rate.HasValue && rate.Value > 0) tune.Rate = rate.Value;
            }
            GlobalSystemMediaTransportControlsSessionTimelineProperties line = session.GetTimelineProperties();
            if (line != null)
            {
                tune.Start = line.StartTime.TotalSeconds;
                tune.Length = Math.Max(0, (line.EndTime - line.StartTime).TotalSeconds);
                double at = (line.Position - line.StartTime).TotalSeconds;
                // The app said where it was when it last told Windows, and it has played on since.
                double since = (DateTimeOffset.Now - line.LastUpdatedTime).TotalSeconds;
                if (tune.Playing && since > 0 && since < 6 * 3600) at += since * tune.Rate;
                tune.Position = Math.Max(0, tune.Length > 0 ? Math.Min(at, tune.Length) : at);
            }
            if (tune.Length <= 0) tune.CanSeek = false;
            return tune;
        }

        private static string Clean(string text) { return Regex.Replace(text ?? "", @"[\s\u0000-\u001F\u007F]+", " ").Trim(); }

        internal static Tune Refresh(Tune tune)
        {
            GlobalSystemMediaTransportControlsSession session = tune == null ? null : tune.Session as GlobalSystemMediaTransportControlsSession;
            if (session == null) return null;
            try { return Describe(session); }
            catch (Exception) { return null; }
        }

        private static bool SendToSession(Tune tune, string what)
        {
            try { return SendNow(tune, what); }
            finally { Pressed(); }
        }

        private static bool SendNow(Tune tune, string what)
        {
            if (what == "like" || what == "unlike") return LikeOnSpotify(what == "like");
            GlobalSystemMediaTransportControlsSession session = tune == null ? null : tune.Session as GlobalSystemMediaTransportControlsSession;
            if (session != null)
            {
                try
                {
                    Windows.Foundation.IAsyncOperation<bool> asked = null;
                    if (what == "previous") asked = session.TrySkipPreviousAsync();
                    else if (what == "next") asked = session.TrySkipNextAsync();
                    else if (what == "play") asked = tune.CanPlay || !tune.CanToggle ? session.TryPlayAsync() : session.TryTogglePlayPauseAsync();
                    else if (what == "pause") asked = tune.CanPause || !tune.CanToggle ? session.TryPauseAsync() : session.TryTogglePlayPauseAsync();
                    else if (what.StartsWith("seek:", StringComparison.Ordinal))
                    {
                        double seconds;
                        if (!double.TryParse(what.Substring(5), NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return false;
                        asked = session.TryChangePlaybackPositionAsync((long)((tune.Start + seconds) * TimeSpan.TicksPerSecond));
                    }
                    if (asked != null)
                    {
                        Task<bool> sending = asked.AsTask();
                        if (sending.Wait(3000) && sending.Result) return true;
                    }
                }
                catch (Exception) { }
            }
            // An app that won't take it from its session still hears the media keys, which go to the app Windows picked.
            byte key = what == "previous" ? (byte)0xB1 : what == "next" ? (byte)0xB0 : what == "play" || what == "pause" ? (byte)0xB3 : (byte)0;
            if (key == 0) return false;
            Hands.Press(key);
            return true;
        }

        // Brings the app playing it to the front: its window with the song in its title, or any of its windows. Spotify tucked
        // away in the tray opens from its link.
        internal static bool Open(Tune tune)
        {
            if (tune == null) return false;
            string program = Program(tune.App);
            OpenWindow best = null;
            foreach (OpenWindow window in Hands.Windows())
            {
                if (!string.Equals(window.Program, program, StringComparison.OrdinalIgnoreCase)) continue;
                if (best == null || (tune.Title.Length > 0 && (window.Title ?? "").IndexOf(tune.Title, StringComparison.OrdinalIgnoreCase) >= 0)) best = window;
            }
            if (best != null) return Bring(best.Handle);
            if (!tune.Spotify) return false;
            Hands.Run("spotify:");
            return true;
        }

        // Spotify's names for its heart in the now-playing bar: before it's liked, and after (where it opens a playlist picker).
        private static readonly string[] HeartNames = { "Add to Liked Songs", "Save to Your Library", "Save to Liked Songs", "Remove from Liked Songs", "Remove from Your Library", "Add to playlist" };

        // Spotify keeps its like to itself: no media session or media key likes a song. Its own heart in the now-playing bar,
        // found through UI Automation like her Play clicks, likes it without taking focus. Otherwise its Like/Dislike key,
        // Alt + Shift + B, does it while Spotify is in front for a moment, then whatever was in front comes back.
        private static bool LikeOnSpotify(bool want)
        {
            IntPtr window = SpotifyWindow();
            if (window == IntPtr.Zero) return false;
            try
            {
                AutomationElement heart = SpotifyHeart(window);
                bool? has = heart == null ? null : HeartLiked(heart);
                if (has.HasValue && has.Value == want) return true;
                object pattern;
                // A liked song's heart opens a playlist picker instead of taking the like back, so that one goes to the key.
                if (want && has == false && heart.TryGetCurrentPattern(InvokePattern.Pattern, out pattern)) { ((InvokePattern)pattern).Invoke(); return true; }
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (COMException) { }
            IntPtr was = Native.GetForegroundWindow();
            bool tucked = IsIconic(window);
            if (!Bring(window)) return false;
            Hands.Chord(new byte[] { 0x12, 0x10, 0x42 });
            // Spotify's thread takes the keys only while it's in front, so it gets a moment before anything else comes forward.
            Thread.Sleep(200);
            if (tucked) ShowWindow(window, 7);
            if (was != IntPtr.Zero && was != window) Bring(was);
            return true;
        }

        private static IntPtr SpotifyWindow()
        {
            foreach (OpenWindow open in Hands.Windows())
                if (string.Equals(open.Program, "Spotify", StringComparison.OrdinalIgnoreCase) && open.Handle != IntPtr.Zero) return open.Handle;
            return IntPtr.Zero;
        }

        private static bool? SpotifyHearted()
        {
            IntPtr window = SpotifyWindow();
            if (window == IntPtr.Zero) return null;
            try
            {
                AutomationElement heart = SpotifyHeart(window);
                return heart == null ? null : HeartLiked(heart);
            }
            catch (ElementNotAvailableException) { }
            catch (InvalidOperationException) { }
            catch (COMException) { }
            return null;
        }

        private static AutomationElement SpotifyHeart(IntPtr window)
        {
            AutomationElement root = AutomationElement.FromHandle(window);
            System.Windows.Rect frame = root.Current.BoundingRectangle;
            if (frame.IsEmpty) return null;
            Condition[] names = new Condition[HeartNames.Length];
            for (int i = 0; i < names.Length; i++) names[i] = new PropertyCondition(AutomationElement.NameProperty, HeartNames[i]);
            Condition wanted = new AndCondition(new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button), new OrCondition(names));
            // The now-playing bar runs along the bottom, with the song's heart in its left half; songs in lists have hearts too.
            double bar = Math.Max(150, frame.Height * 0.2);
            foreach (AutomationElement button in root.FindAll(TreeScope.Descendants, wanted))
            {
                System.Windows.Rect box = button.Current.BoundingRectangle;
                if (!box.IsEmpty && box.Y + box.Height / 2 >= frame.Bottom - bar && box.X + box.Width / 2 <= frame.Left + frame.Width / 2) return button;
            }
            return null;
        }

        private static bool? HeartLiked(AutomationElement heart)
        {
            object pattern;
            if (heart.TryGetCurrentPattern(TogglePattern.Pattern, out pattern)) return ((TogglePattern)pattern).Current.ToggleState == ToggleState.On;
            string name = heart.Current.Name ?? "";
            if (name.StartsWith("Remove", StringComparison.Ordinal) || name == "Add to playlist") return true;
            if (name.StartsWith("Add to Liked", StringComparison.Ordinal) || name.StartsWith("Save", StringComparison.Ordinal)) return false;
            return null;
        }
    }

    // The mini player's parts. The plate itself drags it.
    internal static class PlayerPart
    {
        internal const int Menu = 1, Previous = 2, PlayPause = 3, Next = 4, Heart = 5, Seek = 6, Close = 7;
    }

    // Her mini player (Lachy's ask, 28 Oct): what's playing, from Windows' media session, on a small black plate with her pink
    // and cyan neon, a seek bar, a menu, previous, play/pause, next and, for Spotify, a heart. She perches on it like a window.
    internal sealed class MiniPlayer : Overlay
    {
        internal const int Wide = 336;
        internal const int Tall = 116;
        // Where she sits on it when she hops on, from its left end: near the corner, like Lachy's picture.
        internal const float Perch = 0.1f;
        private const int TitleY = 11, ArtistY = 31, SeekY = 58, ControlsY = 88;
        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        private readonly System.Windows.Forms.Timer ticks = new System.Windows.Forms.Timer();
        private readonly Random random = new Random();
        private Tune tune;
        private bool liked;
        private float scale = 1;
        // Where they dragged it, as its plate's bottom-left on screen. Null is the main screen's bottom-left corner.
        private Point? corner;
        private int hover;
        private int held;
        private bool grabbing;
        private Point grabbed;
        private Point grabbedAt;
        private double scrub = -1;
        private int legsFrom = -1, legsTo = -1;
        private double nextGlitch;
        private double glitchUntil = -1;
        private int glitchSeed;
        private double sentAt = -10;
        private double heartAt = -10;
        internal bool Motion = true;

        // A press: menu, close, previous, play, pause, next, like, unlike or seek:<seconds>.
        internal event Action<string> Command;
        // They let go of it somewhere new: its plate's bottom-left on screen, to keep.
        internal event Action<Point> Moved;

        internal MiniPlayer() : base("Capri mini player")
        {
            AccessibleName = "Capri mini player";
            Cursor = Look.Pointer;
            ticks.Interval = 500;
            ticks.Tick += delegate { Step(); };
        }

        internal Tune Tune { get { return tune; } }

        internal bool Liked { get { return liked; } }

        internal float ScaleShown { get { return scale; } }

        internal double SentAt { get { return sentAt; } }

        internal int LegsFrom { get { return legsFrom; } }

        internal int LegsTo { get { return legsTo; } }

        // Where their mouse is. Tests swap it out.
        internal static Func<Point> Mouse = delegate { return Cursor.Position; };

        // They're using it: dragging it, holding a button, or the mouse is over it.
        internal bool Busy { get { return Visible && (grabbing || held != 0 || Bounds.Contains(Mouse())); } }

        private int Glow { get { return (int)Math.Round(12 * scale); } }

        // The plate on screen, without the glow around it.
        internal Rectangle Plate
        {
            get
            {
                int glow = Glow;
                return Visible ? new Rectangle(Left + glow, Top + glow, Width - glow * 2, Height - glow * 2) : Rectangle.Empty;
            }
        }

        private float Dpi()
        {
            int dpi = 96;
            try { dpi = Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { }
            return Math.Max(1f, dpi / 96f);
        }

        // Shows what's playing, where they left it (or the main screen's bottom-left), or redraws it where it is.
        internal void ShowTune(Tune now, Point? spot)
        {
            if (now == null) return;
            if (tune == null || now.Key != tune.Key) { liked = false; heartAt = -10; }
            tune = now;
            if (!Visible)
            {
                corner = spot;
                hover = 0;
                held = 0;
                scrub = -1;
                scale = Dpi();
                nextGlitch = Tunes.Now + 3 + random.NextDouble() * 5;
            }
            Redraw();
            if (!ticks.Enabled) ticks.Start();
        }

        private void Redraw()
        {
            if (tune == null) return;
            double now = Tunes.Now;
            Bitmap image = Render(scale, tune, now, hover, liked, scrub, legsFrom, legsTo, Motion && glitchUntil >= 0 ? glitchSeed : 0);
            Present(image, Spot(image.Size));
            float dpi = Dpi();
            if (dpi == scale) return;
            // It landed on a screen with another scale.
            scale = dpi;
            image = Render(scale, tune, now, hover, liked, scrub, legsFrom, legsTo, 0);
            Present(image, Spot(image.Size));
        }

        private Point Spot(Size size)
        {
            int glow = Glow;
            Rectangle area;
            Point at;
            if (corner.HasValue)
            {
                area = Screen.FromPoint(corner.Value).WorkingArea;
                at = new Point(corner.Value.X - glow, corner.Value.Y + glow - size.Height);
            }
            else
            {
                area = Screen.PrimaryScreen.WorkingArea;
                int margin = (int)Math.Round(12 * scale);
                at = new Point(area.Left + margin - glow, area.Bottom - margin + glow - size.Height);
            }
            return new Point(Math.Max(area.Left - glow, Math.Min(at.X, area.Right + glow - size.Width)), Math.Max(area.Top - glow, Math.Min(at.Y, area.Bottom + glow - size.Height)));
        }

        private void Step()
        {
            if (!Visible || tune == null) { ticks.Stop(); return; }
            double now = Tunes.Now;
            // Now and then a glitch tears through it for a moment, in her pink and cyan.
            if (glitchUntil >= 0 && now >= glitchUntil) { glitchUntil = -1; nextGlitch = now + 8 + random.NextDouble() * 10; ticks.Interval = 500; }
            else if (glitchUntil < 0 && Motion && now >= nextGlitch) { glitchUntil = now + 0.36; ticks.Interval = 60; }
            if (glitchUntil >= 0) glitchSeed = random.Next(1, int.MaxValue);
            Redraw();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (!Visible) { ticks.Stop(); grabbing = false; held = 0; scrub = -1; hover = 0; glitchUntil = -1; ticks.Interval = 500; }
            Seat();
        }

        protected override void OnMove(EventArgs e) { base.OnMove(e); Seat(); }

        protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Seat(); }

        // Its top edge is a window edge for her.
        private void Seat() { if (IsHandleCreated) Ledges.OwnSeat(Handle, Plate); }

        // Where she sits on it, from her hip on screen, her size and which way she faces, so the song's name moves out from
        // under her legs. Null: she isn't on it.
        internal void Sitting(int? hipX, int size, bool facingLeft)
        {
            Rectangle plate = Plate;
            int from = -1, to = -1;
            if (hipX.HasValue && size > 0 && !plate.IsEmpty)
            {
                int hip = hipX.Value - plate.Left;
                from = hip - (int)(size * (facingLeft ? 0.36f : 0.1f));
                to = hip + (int)(size * (facingLeft ? 0.1f : 0.36f));
            }
            if (from == legsFrom && to == legsTo) return;
            legsFrom = from;
            legsTo = to;
            if (Visible) Redraw();
        }

        // Like her task panel: her own screenshots during a job look straight through it.
        internal bool Unseen(bool unseen)
        {
            if (!IsHandleCreated) return false;
            try { SetWindowDisplayAffinity(Handle, unseen ? 0x11u : 0u); }
            catch (EntryPointNotFoundException) { return false; }
            return unseen && Visible;
        }

        private PointF OnPlate(Point at) { int glow = Glow; return new PointF(at.X - glow, at.Y - glow); }

        private double Fraction(float x) { return Math.Max(0, Math.Min(1, (x - 18 * scale) / ((Wide - 36) * scale))); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || tune == null) return;
            int part = PartAt(OnPlate(e.Location), scale, tune.Spotify);
            if (part == 0) { Grab(PointToScreen(e.Location)); return; }
            held = part;
            Capture = true;
            if (part == PlayerPart.Seek && tune.CanSeek) { scrub = Fraction(OnPlate(e.Location).X); Redraw(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (grabbing) { Haul(PointToScreen(e.Location)); return; }
            if (tune == null) return;
            if (scrub >= 0) { scrub = Fraction(OnPlate(e.Location).X); Redraw(); return; }
            int part = PartAt(OnPlate(e.Location), scale, tune.Spotify);
            Cursor = part == 0 ? Cursors.SizeAll : Look.Pointer;
            if (part != hover) { hover = part; Redraw(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (grabbing) { LetGo(); return; }
            int was = held;
            double to = scrub;
            held = 0;
            scrub = -1;
            if (Capture) Capture = false;
            if (tune == null) return;
            if (e.Button == MouseButtons.Right) { Raise("menu"); return; }
            if (to >= 0) { SeekTo(to); return; }
            if (was != 0 && was == PartAt(OnPlate(e.Location), scale, tune.Spotify)) Press(was);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != 0 && held == 0) { hover = 0; Redraw(); }
        }

        // Losing the mouse partway, like to a menu, ends a drag where it got to and drops a press or a seek.
        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            base.OnMouseCaptureChanged(e);
            if (Capture) return;
            if (grabbing) LetGo();
            if (held == 0 && scrub < 0) return;
            held = 0;
            scrub = -1;
            if (Visible) Redraw();
        }

        internal void Grab(Point at)
        {
            grabbing = true;
            grabbed = at;
            grabbedAt = Location;
            Capture = true;
        }

        internal void Haul(Point at)
        {
            if (grabbing) MoveTo(new Point(grabbedAt.X + at.X - grabbed.X, grabbedAt.Y + at.Y - grabbed.Y));
        }

        internal void LetGo()
        {
            bool was = grabbing;
            grabbing = false;
            if (Capture) Capture = false;
            if (!was || !Visible) return;
            if (Dpi() != scale) Redraw();
            if (corner.HasValue && Moved != null) Moved(corner.Value);
        }

        // Moves its top-left to about here, kept on screen.
        internal void MoveTo(Point topLeft)
        {
            Bitmap image = Image;
            if (image == null || !Visible) return;
            int glow = Glow;
            corner = new Point(topLeft.X + glow, topLeft.Y + image.Height - glow);
            Point at = Spot(image.Size);
            corner = new Point(at.X + glow, at.Y + image.Height - glow);
            if (Location != at) Location = at;
        }

        // A press on one of its parts, from the mouse or a test.
        internal void Press(int part)
        {
            Tune shown = tune;
            if (part == PlayerPart.Menu) { Raise("menu"); return; }
            if (part == PlayerPart.Close) { Raise("close"); return; }
            if (shown == null) return;
            double now = Tunes.Now;
            if (part == PlayerPart.Previous && shown.CanBack) Raise("previous");
            else if (part == PlayerPart.Next && shown.CanSkip) Raise("next");
            else if (part == PlayerPart.PlayPause)
            {
                shown.Position = shown.At(now);
                shown.ReadAt = now;
                shown.Playing = !shown.Playing;
                Raise(shown.Playing ? "play" : "pause");
            }
            else if (part == PlayerPart.Heart && shown.Spotify) { liked = !liked; heartAt = now; Raise(liked ? "like" : "unlike"); }
            else return;
            if (Visible) Redraw();
        }

        // What Spotify's own heart said for a song, so hers matches it. A read that began before a press on her heart, or just
        // after one Spotify may not have taken yet, is out of date.
        internal void Hearted(string key, bool has, double asked)
        {
            if (tune == null || tune.Key != key || asked < heartAt + 2 || has == liked) return;
            liked = has;
            if (Visible) Redraw();
        }

        // Skips to that far through the song, from 0 to 1.
        internal void SeekTo(double fraction)
        {
            Tune shown = tune;
            if (shown == null || !shown.CanSeek) { if (Visible) Redraw(); return; }
            shown.Position = Math.Max(0, Math.Min(1, fraction)) * shown.Length;
            shown.ReadAt = Tunes.Now;
            Raise("seek:" + shown.Position.ToString("0.###", CultureInfo.InvariantCulture));
            if (Visible) Redraw();
        }

        // How a press went. A like Spotify didn't take puts the heart back.
        internal void Sent(string what, bool ok)
        {
            if (ok || (what != "like" && what != "unlike")) return;
            liked = what == "unlike";
            if (Visible) Redraw();
        }

        private void Raise(string what)
        {
            sentAt = Tunes.Now;
            if (Command != null) Command(what);
        }

        private static RectangleF Around(float x, float y, float width, float height, float s) { return new RectangleF((x - width / 2) * s, (y - height / 2) * s, width * s, height * s); }

        // Where a part takes its clicks on the plate.
        internal static RectangleF Box(int part, float s)
        {
            switch (part)
            {
                case PlayerPart.Menu: return Around(30, ControlsY, 34, 34, s);
                case PlayerPart.Previous: return Around(Wide / 2 - 58, ControlsY, 36, 34, s);
                case PlayerPart.PlayPause: return Around(Wide / 2, ControlsY, 42, 42, s);
                case PlayerPart.Next: return Around(Wide / 2 + 58, ControlsY, 36, 34, s);
                case PlayerPart.Heart: return Around(Wide - 30, ControlsY, 34, 34, s);
                case PlayerPart.Seek: return new RectangleF(10 * s, (SeekY - 9) * s, (Wide - 20) * s, 18 * s);
                case PlayerPart.Close: return Around(Wide - 18, 17, 26, 26, s);
            }
            return RectangleF.Empty;
        }

        private static PointF Middle(int part, float s)
        {
            RectangleF box = Box(part, s);
            return new PointF(box.X + box.Width / 2, box.Y + box.Height / 2);
        }

        // The part at a point on the plate, or 0 for the plate itself. The heart is only there for Spotify.
        internal static int PartAt(PointF spot, float s, bool heart)
        {
            for (int part = PlayerPart.Menu; part <= PlayerPart.Close; part++)
                if ((part != PlayerPart.Heart || heart) && Box(part, s).Contains(spot)) return part;
            return 0;
        }

        internal static string Clock(double seconds)
        {
            int total = (int)Math.Max(0, Math.Floor(seconds));
            return total >= 3600 ? total / 3600 + ":" + (total / 60 % 60).ToString("00") + ":" + (total % 60).ToString("00") : total / 60 + ":" + (total % 60).ToString("00");
        }

        // Where the song's name starts and ends on the plate: clear of her legs while she sits on it.
        internal static float[] TextSpan(float s, int legsFrom, int legsTo)
        {
            float left = 18 * s, right = (Wide - 36) * s;
            if (legsTo > legsFrom)
            {
                float from = legsFrom - 6 * s, to = legsTo + 6 * s;
                if (to > left && from < right)
                {
                    float before = from - left, after = right - to;
                    if (Math.Max(before, after) >= 64 * s) { if (after >= before) left = to; else right = from; }
                }
            }
            return new[] { left, right };
        }

        internal static Bitmap Render(float s, Tune tune, double now, int hover, bool liked, double scrub, int legsFrom, int legsTo, int glitch)
        {
            int glow = (int)Math.Round(12 * s);
            int w = (int)Math.Round(Wide * s), h = (int)Math.Round(Tall * s);
            Bitmap image = new Bitmap(w + glow * 2, h + glow * 2, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                graphics.TranslateTransform(glow, glow);
                RectangleF plate = new RectangleF(0, 0, w, h);
                PointF from = new PointF(-1, 0), to = new PointF(w + 1, 0);
                using (GraphicsPath path = Look.Rounded(plate, 16 * s))
                {
                    using (LinearGradientBrush haze = new LinearGradientBrush(from, to, Color.FromArgb(30, Look.Pink), Color.FromArgb(30, Look.Cyan)))
                    using (Pen wide = new Pen(haze, 14 * s)) graphics.DrawPath(wide, path);
                    using (LinearGradientBrush haze = new LinearGradientBrush(from, to, Color.FromArgb(70, Look.Pink), Color.FromArgb(70, Look.Cyan)))
                    using (Pen near = new Pen(haze, 6 * s)) graphics.DrawPath(near, path);
                    using (Brush fill = new SolidBrush(Color.FromArgb(250, 5, 6, 9))) graphics.FillPath(fill, path);
                    graphics.SetClip(path);
                    using (Pen scan = new Pen(Color.FromArgb(9, 255, 255, 255), Math.Max(1, s)))
                        for (float row = 0; row < h; row += 3 * s) graphics.DrawLine(scan, 0, row, w, row);
                    graphics.ResetClip();
                    using (LinearGradientBrush ink = new LinearGradientBrush(from, to, Look.Pink, Look.Cyan))
                    using (Pen edge = new Pen(ink, 1.6f * s)) graphics.DrawPath(edge, path);
                }
                // Her glitch: a sliver of the edge torn loose at the top right, and another at the bottom left.
                using (Brush cyan = new SolidBrush(Color.FromArgb(210, Look.Cyan))) graphics.FillRectangle(cyan, w * 0.64f, -3 * s, 28 * s, 1.6f * s);
                using (Brush pink = new SolidBrush(Color.FromArgb(210, Look.Pink))) graphics.FillRectangle(pink, w * 0.18f, h + 1.4f * s, 18 * s, 1.6f * s);
                string app = Tunes.AppName(tune.App);
                string title = Plain(tune.Title).Length > 0 ? Plain(tune.Title) : app;
                string artist = Plain(tune.Title).Length == 0 ? "" : Plain(tune.Artist).Length > 0 ? Plain(tune.Artist) : app;
                double at = scrub >= 0 && tune.Length > 0 ? scrub * tune.Length : tune.At(now);
                string time = tune.Length > 0 ? Clock(at) + " / " + Clock(tune.Length) : "";
                float[] span = TextSpan(s, legsFrom, legsTo);
                bool cutRight = span[1] < (Wide - 36) * s;
                using (Font big = Look.Mono(13.5f * s, FontStyle.Regular, GraphicsUnit.Pixel, title))
                using (Font small = Look.Mono(11 * s, FontStyle.Regular, GraphicsUnit.Pixel, artist + time))
                {
                    string shown = Fit(graphics, title, big, span[1] - span[0]);
                    using (Brush pink = new SolidBrush(Color.FromArgb(140, Look.Pink))) Look.Draw(graphics, shown, big, pink, span[0] - 1.1f * s, TitleY * s);
                    using (Brush cyan = new SolidBrush(Color.FromArgb(140, Look.Cyan))) Look.Draw(graphics, shown, big, cyan, span[0] + 1.1f * s, TitleY * s);
                    using (Brush ink = new SolidBrush(Look.Ink)) Look.Draw(graphics, shown, big, ink, span[0], TitleY * s);
                    float timeWidth = time.Length > 0 ? graphics.MeasureString(time, small, PointF.Empty, StringFormat.GenericTypographic).Width : 0;
                    float timeLeft = w - 18 * s - timeWidth;
                    bool timeShown = time.Length > 0 && !(legsTo > legsFrom && legsTo + 4 * s > timeLeft && legsFrom - 4 * s < w - 18 * s);
                    float artistRight = cutRight ? span[1] : w - 18 * s;
                    if (timeShown) artistRight = Math.Min(artistRight, timeLeft - 12 * s);
                    using (Brush cyan = new SolidBrush(Color.FromArgb(225, Look.Cyan))) Look.Draw(graphics, Fit(graphics, artist, small, artistRight - span[0]), small, cyan, span[0], ArtistY * s);
                    if (timeShown) using (Brush dim = new SolidBrush(Look.Dim)) graphics.DrawString(time, small, dim, timeLeft, ArtistY * s, StringFormat.GenericTypographic);
                }
                float barLeft = 18 * s, barRight = w - 18 * s, barY = SeekY * s;
                using (Pen rail = new Pen(Color.FromArgb(58, 63, 74), 2.2f * s))
                {
                    rail.StartCap = LineCap.Round;
                    rail.EndCap = LineCap.Round;
                    graphics.DrawLine(rail, barLeft, barY, barRight, barY);
                }
                if (tune.Length > 0)
                {
                    float x = barLeft + (float)(Math.Max(0, Math.Min(1, at / tune.Length)) * (barRight - barLeft));
                    if (x > barLeft + 0.5f)
                        using (LinearGradientBrush ink = new LinearGradientBrush(new PointF(barLeft - 1, 0), new PointF(barRight + 1, 0), Look.Pink, Look.Cyan))
                        using (Pen done = new Pen(ink, 2.6f * s))
                        {
                            done.StartCap = LineCap.Round;
                            done.EndCap = LineCap.Round;
                            graphics.DrawLine(done, barLeft, barY, x, barY);
                        }
                    bool lit = hover == PlayerPart.Seek || scrub >= 0;
                    float r = (lit ? 6.5f : 5.5f) * s;
                    using (Brush halo = new SolidBrush(Color.FromArgb(lit ? 90 : 60, lit ? Look.Cyan : Look.Pink))) graphics.FillEllipse(halo, x - r * 1.9f, barY - r * 1.9f, r * 3.8f, r * 3.8f);
                    using (Brush knob = new SolidBrush(Look.Ink)) graphics.FillEllipse(knob, x - r, barY - r, r * 2, r * 2);
                }
                foreach (int part in new[] { PlayerPart.Menu, PlayerPart.Previous, PlayerPart.Next, PlayerPart.Heart })
                {
                    if (hover != part || (part == PlayerPart.Heart && !tune.Spotify)) continue;
                    PointF lit = Middle(part, s);
                    using (Brush glowing = new SolidBrush(Color.FromArgb(30, 255, 255, 255))) graphics.FillEllipse(glowing, lit.X - 15 * s, lit.Y - 15 * s, 30 * s, 30 * s);
                }
                DrawMenu(graphics, Middle(PlayerPart.Menu, s), s, hover == PlayerPart.Menu ? Look.Cyan : Look.Ink);
                DrawSkip(graphics, Middle(PlayerPart.Previous, s), s, !tune.CanBack ? Look.Dim : hover == PlayerPart.Previous ? Look.Cyan : Look.Ink, false);
                DrawSkip(graphics, Middle(PlayerPart.Next, s), s, !tune.CanSkip ? Look.Dim : hover == PlayerPart.Next ? Look.Cyan : Look.Ink, true);
                DrawPlay(graphics, Middle(PlayerPart.PlayPause, s), s, hover == PlayerPart.PlayPause, tune.Playing);
                if (tune.Spotify) DrawHeart(graphics, Middle(PlayerPart.Heart, s), s, hover == PlayerPart.Heart, liked);
                DrawClose(graphics, Middle(PlayerPart.Close, s), s, hover == PlayerPart.Close);
                if (glitch != 0) Tear(image, graphics, glow, w, h, s, glitch);
            }
            return image;
        }

        // A few rows of the plate slip sideways, tinted her pink and cyan.
        private static void Tear(Bitmap image, Graphics graphics, int glow, int w, int h, float s, int seed)
        {
            Random roll = new Random(seed);
            graphics.ResetTransform();
            using (Bitmap clean = image.Clone(new Rectangle(0, 0, image.Width, image.Height), image.PixelFormat))
            using (GraphicsPath plate = Look.Rounded(new RectangleF(glow, glow, w, h), 16 * s))
            using (Brush clear = new SolidBrush(Color.Transparent))
                for (int i = 0; i < 3; i++)
                {
                    int band = Math.Max(2, (int)Math.Round((2 + roll.Next(7)) * s));
                    int top = glow + roll.Next(Math.Max(1, h - band));
                    int shift = (roll.Next(2) == 0 ? -1 : 1) * Math.Max(1, (int)Math.Round((2 + roll.Next(5)) * s));
                    Rectangle strip = new Rectangle(0, top, image.Width, band);
                    graphics.SetClip(strip);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.FillRectangle(clear, strip);
                    graphics.DrawImage(clean, new Rectangle(shift, 0, image.Width, image.Height));
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.SetClip(plate);
                    graphics.IntersectClip(strip);
                    using (Brush tint = new SolidBrush(Color.FromArgb(56, i % 2 == 0 ? Look.Pink : Look.Cyan))) graphics.FillRectangle(tint, strip);
                    graphics.ResetClip();
                }
        }

        // Her font has no emoji and GDI+ would draw boxes for them, so they come out of what it shows.
        internal static string Plain(string text)
        {
            System.Text.StringBuilder kept = new System.Text.StringBuilder();
            foreach (char c in text ?? "") if (!char.IsSurrogate(c) && c != '︎' && c != '️' && c != '‍' && c != '⃣') kept.Append(c);
            return Regex.Replace(kept.ToString(), @"\s{2,}", " ").Trim();
        }

        // The longest start of the text that fits, with "..." when it's cut.
        private static string Fit(Graphics graphics, string text, Font font, float width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0) return "";
            if (Measure(graphics, text, font) <= width) return text;
            int low = 0, high = text.Length;
            while (low < high)
            {
                int middle = (low + high + 1) / 2;
                if (Measure(graphics, Start(text, middle) + "...", font) <= width) low = middle;
                else high = middle - 1;
            }
            return low == 0 ? "" : Start(text, low).TrimEnd() + "...";
        }

        private static string Start(string text, int length)
        {
            if (length > 0 && length < text.Length && char.IsHighSurrogate(text[length - 1])) length--;
            return text.Substring(0, length);
        }

        private static float Measure(Graphics graphics, string text, Font font) { return graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width; }

        private static void DrawMenu(Graphics graphics, PointF c, float s, Color colour)
        {
            using (Pen line = new Pen(colour, 2.2f * s))
            {
                line.StartCap = LineCap.Round;
                line.EndCap = LineCap.Round;
                for (int i = -1; i <= 1; i++) graphics.DrawLine(line, c.X - 8 * s, c.Y + i * 5.5f * s, c.X + 8 * s, c.Y + i * 5.5f * s);
            }
        }

        // Two arrows and a bar: next points right, previous left.
        private static void DrawSkip(Graphics graphics, PointF c, float s, Color colour, bool next)
        {
            float d = next ? 1 : -1;
            using (Brush ink = new SolidBrush(colour))
            {
                graphics.FillPolygon(ink, new[] { new PointF(c.X - d * 9 * s, c.Y - 6.5f * s), new PointF(c.X - d * 9 * s, c.Y + 6.5f * s), new PointF(c.X - d * 1.5f * s, c.Y) });
                graphics.FillPolygon(ink, new[] { new PointF(c.X - d * 1.5f * s, c.Y - 6.5f * s), new PointF(c.X - d * 1.5f * s, c.Y + 6.5f * s), new PointF(c.X + d * 6 * s, c.Y) });
                graphics.FillRectangle(ink, next ? c.X + 6 * s : c.X - 8.4f * s, c.Y - 6.5f * s, 2.4f * s, 13 * s);
            }
        }

        // Her white disc, split pink and cyan at its edges.
        private static void DrawPlay(Graphics graphics, PointF c, float s, bool lit, bool playing)
        {
            float r = 17 * s;
            if (lit) using (Brush halo = new SolidBrush(Color.FromArgb(70, Look.Cyan))) graphics.FillEllipse(halo, c.X - r - 5 * s, c.Y - r - 5 * s, (r + 5 * s) * 2, (r + 5 * s) * 2);
            using (Brush pink = new SolidBrush(Color.FromArgb(170, Look.Pink))) graphics.FillEllipse(pink, c.X - r - 1.6f * s, c.Y - r, r * 2, r * 2);
            using (Brush cyan = new SolidBrush(Color.FromArgb(170, Look.Cyan))) graphics.FillEllipse(cyan, c.X - r + 1.6f * s, c.Y - r, r * 2, r * 2);
            using (Brush disc = new SolidBrush(Look.Ink)) graphics.FillEllipse(disc, c.X - r, c.Y - r, r * 2, r * 2);
            using (Brush dark = new SolidBrush(Color.FromArgb(10, 11, 15)))
            {
                if (!playing) { graphics.FillPolygon(dark, new[] { new PointF(c.X - 4.5f * s, c.Y - 7.5f * s), new PointF(c.X - 4.5f * s, c.Y + 7.5f * s), new PointF(c.X + 8 * s, c.Y) }); return; }
                foreach (float x in new[] { c.X - 5.6f * s, c.X + 2 * s })
                    using (GraphicsPath bar = Look.Rounded(new RectangleF(x, c.Y - 6.5f * s, 3.6f * s, 13 * s), 1 * s)) graphics.FillPath(dark, bar);
            }
        }

        private static void DrawHeart(Graphics graphics, PointF c, float s, bool lit, bool liked)
        {
            float r = 8.5f * s, x = c.X, y = c.Y + 0.5f * s;
            using (GraphicsPath heart = new GraphicsPath())
            {
                heart.AddBezier(x, y + r * 0.95f, x - r * 0.35f, y + r * 0.6f, x - r * 1.1f, y + r * 0.1f, x - r * 1.05f, y - r * 0.35f);
                heart.AddBezier(x - r * 1.05f, y - r * 0.35f, x - r * 1.0f, y - r * 1.05f, x - r * 0.1f, y - r * 1.1f, x, y - r * 0.45f);
                heart.AddBezier(x, y - r * 0.45f, x + r * 0.1f, y - r * 1.1f, x + r * 1.0f, y - r * 1.05f, x + r * 1.05f, y - r * 0.35f);
                heart.AddBezier(x + r * 1.05f, y - r * 0.35f, x + r * 1.1f, y + r * 0.1f, x + r * 0.35f, y + r * 0.6f, x, y + r * 0.95f);
                heart.CloseFigure();
                if (liked) using (Brush fill = new SolidBrush(Look.Pink)) graphics.FillPath(fill, heart);
                using (Pen edge = new Pen(liked ? Look.Pink : lit ? Look.Cyan : Look.Ink, 2 * s))
                {
                    edge.LineJoin = LineJoin.Round;
                    graphics.DrawPath(edge, heart);
                }
            }
        }

        private static void DrawClose(Graphics graphics, PointF c, float s, bool lit)
        {
            float r = 4.5f * s;
            if (lit) using (Brush halo = new SolidBrush(Color.FromArgb(60, Look.Red))) graphics.FillEllipse(halo, c.X - 10 * s, c.Y - 10 * s, 20 * s, 20 * s);
            using (Pen cross = new Pen(lit ? Look.Red : Look.Dim, 1.8f * s))
            {
                cross.StartCap = LineCap.Round;
                cross.EndCap = LineCap.Round;
                graphics.DrawLine(cross, c.X - r, c.Y - r, c.X + r, c.Y + r);
                graphics.DrawLine(cross, c.X - r, c.Y + r, c.X + r, c.Y - r);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }
    }
}
