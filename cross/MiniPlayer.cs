using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class MiniPlayer : PassiveWindow
    {
        internal sealed class Song
        {
            internal string App, Title, Artist;
            internal bool Playing;
            internal double Position, Length;
        }

        private readonly Canvas page = new Canvas { Width = 336, Height = 116, Background = Brushes.Transparent };
        private readonly LayoutTransformControl zoomed;
        private readonly NeonPlate plate;
        private readonly NeonCaption title, artist, time;
        private readonly NeonButton previous, play, next, close;
        private readonly Timeline timeline = new Timeline();
        private readonly DispatcherTimer ticks = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        private readonly Queue<KeyValuePair<Song, byte>> presses = new Queue<KeyValuePair<Song, byte>>();
        internal Func<Song> Read = () => ReadSong(OperatingSystem.IsMacOS());
        internal Func<Song, byte, bool> Send = (song, key) => SendKey(song, key, OperatingSystem.IsMacOS());
        internal Action<Action> Work = action => ThreadPool.QueueUserWorkItem(_ => action());
        internal Func<double> Now = () => Environment.TickCount64 / 1000.0;
        internal Func<bool> Allowed = () => true;
        internal Func<bool> Above = () => true;
        internal Func<PixelPoint?> Place = () => null;
        internal Action<bool> Changed;
        internal Action<string> Notice;
        private bool enabled = true;
        internal bool Enabled { get { return enabled; } set { SetEnabled(value); } }
        private bool dismissed, reading, sending, stopped, blocked, started;
        private int epoch;
        private double heard = -1;
        private Song song;
        private string problem;
        private string lastNotice;
        private double zoom = 1;

        internal MiniPlayer()
        {
            Look.Overlay(this, "Capri mini player");
            Focusable = false;
            zoomed = new LayoutTransformControl { Child = page };
            plate = new NeonPlate { Child = zoomed };
            var surface = new Grid();
            surface.Children.Add(plate); surface.Children.Add(new NeonRim { Plate = plate, IsHitTestVisible = false }); Content = surface;
            title = MainframeWindow.Put(page, new NeonCaption { Bold = true, Size = 15, Ellipsis = true }, 18, 9, 276, 22, "Song title");
            artist = MainframeWindow.Put(page, new NeonCaption { Ink = MainframeTheme.Cyan, Ellipsis = true }, 18, 32, 176, 18, "Artist");
            time = MainframeWindow.Put(page, new NeonCaption { Ink = MainframeTheme.Faint, Align = TextAlignment.Right, Size = 11 }, 202, 32, 116, 18, "Song time");
            MainframeWindow.Put(page, timeline, 18, 57, 300, 3);
            previous = MainframeWindow.Put(page, new NeonButton { Text = "Previous", Focusable = false }, 38, 73, 76, 30, "Previous track");
            play = MainframeWindow.Put(page, new NeonButton { Text = "Pause", Accent = MainframeTheme.Cyan, Focusable = false }, 124, 69, 88, 38, "Play or pause");
            next = MainframeWindow.Put(page, new NeonButton { Text = "Next", Focusable = false }, 222, 73, 76, 30, "Next track");
            close = MainframeWindow.Put(page, new NeonButton { Glyph = 2, Focusable = false }, 300, 7, 26, 24, "Hide mini player");
            previous.Click += (_, _) => Press(0xB1); play.Click += (_, _) => Press(0xB3); next.Click += (_, _) => Press(0xB0);
            close.Click += (_, _) => Tool("hide");
            page.PointerPressed += (_, e) => Program.Guard(() =>
            {
                if (e.GetCurrentPoint(page).Properties.IsLeftButtonPressed && (e.Source == page || e.Source is NeonCaption)) BeginMoveDrag(e);
            });
            ticks.Tick += (_, _) => Program.Guard(Refresh);
            PropertyChanged += (_, e) => { if (e.Property == IsVisibleProperty) Schedule(); };
            SizeChanged += (_, _) => surface.Clip = MainframeTheme.Cut(new Rect(Bounds.Size), 16 * zoom, 6 * zoom, 16 * zoom, 6 * zoom);
            Closed += (_, _) => { stopped = true; epoch++; ticks.Stop(); };
            Scale(1);
        }

        internal void Start() { if (!stopped) { started = true; Schedule(); Refresh(); } }

        internal int PollSeconds { get { return !Enabled || stopped || blocked ? 0 : IsVisible && song != null && song.Playing ? 1 : 3; } }

        private void Schedule()
        {
            int seconds = PollSeconds;
            if (!started || seconds == 0) { ticks.Stop(); return; }
            TimeSpan interval = TimeSpan.FromSeconds(seconds);
            if (ticks.Interval != interval) ticks.Interval = interval;
            if (!ticks.IsEnabled) ticks.Start();
        }

        internal void Scale(double value)
        {
            zoom = value; Width = 336 * value; Height = 116 * value;
            zoomed.LayoutTransform = new ScaleTransform(value, value); plate.Zoom = value;
        }

        internal string Tool(string action)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.Invoke(() => Tool(action));
            if (action == "hide") { dismissed = true; epoch++; Hide(); return "Hid my mini player."; }
            if (action != "show") return "Couldn't find that mini player action.";
            SetEnabled(true); dismissed = false; blocked = false; problem = null; epoch++; Refresh();
            return problem ?? "My mini player will show when music is playing.";
        }

        internal void SetEnabled(bool on)
        {
            if (Enabled == on) return;
            enabled = on; epoch++;
            if (!on) Hide();
            else { dismissed = false; blocked = false; problem = null; }
            Schedule();
            if (Changed != null) Changed(on);
        }

        internal void Refresh()
        {
            Schedule();
            if (stopped) return;
            if (!Enabled || !Allowed()) { Hide(); return; }
            Topmost = Above();
            if (IsVisible) Follow();
            if (reading || blocked || sending) return;
            reading = true;
            int asked = epoch;
            Work(delegate
            {
                Song found = null; string error = null;
                try { found = Read(); }
                catch (Exception fault) { error = fault.Message; }
                Post(delegate
                {
                    reading = false;
                    if (stopped || asked != epoch) return;
                    if (error != null) { Failed(error); return; }
                    Apply(found, Now());
                });
            });
        }

        private static void Post(Action action)
        {
            if (Dispatcher.UIThread.CheckAccess()) action(); else Dispatcher.UIThread.Post(() => Program.Guard(action));
        }

        private void Failed(string error)
        {
            if (blocked) return;
            blocked = true; problem = error; Hide();
            Schedule();
            if (Notice != null && lastNotice != error) { lastNotice = error; Notice(error); }
        }

        internal void Apply(Song found, double now)
        {
            lastNotice = null;
            song = found;
            if (found != null && found.Playing) heard = now;
            else if (dismissed && now - heard >= 10) { dismissed = false; heard = -1; }
            if (!Enabled || !Allowed() || dismissed || found == null || heard < 0 || now - heard >= 60) { Hide(); return; }
            title.Text = found.Title ?? ""; artist.Text = string.IsNullOrWhiteSpace(found.Artist) ? found.App : found.Artist;
            play.Text = found.Playing ? "Pause" : "Play";
            time.Text = found.Length > 0 ? Clock(found.Position) + " / " + Clock(found.Length) : "";
            timeline.Fraction = found.Length > 0 ? Math.Clamp(found.Position / found.Length, 0, 1) : 0;
            timeline.InvalidateVisual();
            Follow();
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            Schedule();
        }

        internal void Follow()
        {
            PixelPoint? at = Place();
            if (at.HasValue && Position != at.Value) Look.Position(this, at.Value);
        }

        internal static PixelPoint Spot(PixelRect area, double dpi, Size size, PixelRect near)
        {
            int gap = (int)Math.Round(12 * dpi), width = (int)Math.Ceiling(size.Width * dpi), height = (int)Math.Ceiling(size.Height * dpi);
            int x = near.Right + gap;
            if (x + width > area.Right) x = near.X - gap - width;
            return new PixelPoint(Math.Max(area.X, Math.Min(x, area.Right - width)), Math.Max(area.Y, Math.Min(near.Bottom - height, area.Bottom - height)));
        }

        internal void Press(byte key)
        {
            if (song == null || blocked || !Enabled || !IsVisible) return;
            epoch++;
            lock (presses)
            {
                presses.Enqueue(new KeyValuePair<Song, byte>(song, key));
                if (sending) return;
                sending = true;
            }
            Work(delegate
            {
                while (true)
                {
                    KeyValuePair<Song, byte> press;
                    lock (presses)
                    {
                        if (presses.Count == 0) { sending = false; return; }
                        press = presses.Dequeue();
                    }
                    string error = null;
                    try { if (!Send(press.Key, press.Value)) error = "Couldn't control the music player."; }
                    catch (Exception fault) { error = fault.Message; }
                    if (error != null)
                    {
                        lock (presses) { presses.Clear(); sending = false; }
                        Post(() => { if (!stopped) Failed(error); }); return;
                    }
                }
            });
        }

        private static string Clock(double seconds)
        {
            int total = (int)Math.Clamp(double.IsFinite(seconds) ? seconds : 0, 0, int.MaxValue);
            return total >= 3600 ? total / 3600 + ":" + (total / 60 % 60).ToString("00") + ":" + (total % 60).ToString("00") : total / 60 + ":" + (total % 60).ToString("00");
        }

        private sealed class Timeline : Control
        {
            internal double Fraction;
            public override void Render(DrawingContext context)
            {
                context.DrawRectangle(MainframeTheme.Dash, null, new Rect(Bounds.Size));
                if (Fraction > 0) context.DrawRectangle(MainframeTheme.Cyan, null, new Rect(0, 0, Bounds.Width * Fraction, Bounds.Height));
            }
        }

        private const string Automation = "Allow Capri to control your music app in System Settings > Privacy & Security > Automation, then show my mini player again.";

        internal static string Script(string app, string command)
        {
            string body = command;
            if (command == null)
                body = "set stateText to player state as text\nif stateText is not \"playing\" and stateText is not \"paused\" then return \"\"\n" +
                    "set titleText to name of current track\nset artistText to artist of current track\nset elapsed to 0\nset total to 0\ntry\nset elapsed to player position\nset total to duration of current track\nend try\n" +
                    "set sep to ASCII character 31\nreturn stateText & sep & titleText & sep & artistText & sep & (elapsed as text) & sep & (total as text)";
            return "try\nif application \"" + app + "\" is not running then return \"\"\ntell application \"" + app + "\"\n" + body +
                "\nend tell\n" + (command == null ? "" : "return \"OK\"\n") + "on error errorText number errorNumber\nif errorNumber is -1743 or errorNumber is -1744 then return \"CAPRI_AUTOMATION\"\nif errorNumber is -600 or errorNumber is -1728 then return \"\"\nreturn \"CAPRI_ERROR\"\nend try";
        }

        private static string MacReply(string app, string command)
        {
            string reply = Music.Run("/usr/bin/osascript", new[] { "-e", Script(app, command) });
            if (reply == null) throw new IOException("Couldn't reach your music app. Check Capri under System Settings > Privacy & Security > Automation, then show my mini player again.");
            if (reply.Trim() == "CAPRI_AUTOMATION") throw new IOException(Automation);
            if (reply.Trim() == "CAPRI_ERROR") throw new IOException("Couldn't read the music player.");
            return reply.TrimEnd('\r', '\n');
        }

        internal static Song ReadSong(bool mac)
        {
            Song paused = null;
            if (mac)
            {
                foreach (string app in new[] { "Spotify", "Music" })
                {
                    if (Music.Run("/usr/bin/pgrep", new[] { "-x", app }) == null) continue;
                    Song found = MacSong(app, MacReply(app, null));
                    if (found == null) continue;
                    if (found.Playing) return found;
                    if (paused == null) paused = found;
                }
                return paused;
            }
            string names = Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.ListNames" });
            if (names == null) throw new IOException("Couldn't read music from D-Bus.");
            var asked = new HashSet<string>();
            foreach (Match player in Regex.Matches(names, @"org\.mpris\.MediaPlayer2\.[\w.\-]+"))
            {
                if (!asked.Add(player.Value)) continue;
                string data = Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=" + player.Value, "/org/mpris/MediaPlayer2", "org.freedesktop.DBus.Properties.GetAll", "string:org.mpris.MediaPlayer2.Player" });
                Song found = LinuxSong(player.Value, data);
                if (found == null) continue;
                if (found.Playing) return found;
                if (paused == null) paused = found;
            }
            return paused;
        }

        private static double Number(string value)
        {
            double result;
            if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out result) || !double.IsFinite(result)) return 0;
            return Math.Max(0, result);
        }

        internal static Song MacSong(string app, string data)
        {
            string[] parts = (data ?? "").Split('\u001f');
            if (parts.Length != 5 || string.IsNullOrWhiteSpace(parts[1]) || (parts[0] != "playing" && parts[0] != "paused")) return null;
            return new Song { App = app, Title = parts[1], Artist = parts[2], Playing = parts[0] == "playing", Position = Number(parts[3]), Length = Number(parts[4]) / (app == "Spotify" ? 1000 : 1) };
        }

        internal static Song LinuxSong(string app, string data)
        {
            if (data == null) return null;
            Match title = Regex.Match(data, "\"xesam:title\"\\s*variant\\s*string \"((?:\\\\.|[^\"\\\\])*)\"");
            Match artist = Regex.Match(data, "\"xesam:artist\"\\s*variant\\s*array \\[\\s*string \"((?:\\\\.|[^\"\\\\])*)\"");
            Match state = Regex.Match(data, "\"PlaybackStatus\"\\s*variant\\s*string \"(Playing|Paused)\"");
            if (!title.Success || !state.Success || title.Groups[1].Value.Length == 0) return null;
            return new Song { App = app, Title = Regex.Unescape(title.Groups[1].Value), Artist = Regex.Unescape(artist.Groups[1].Value), Playing = state.Groups[1].Value == "Playing",
                Position = Number(Regex.Match(data, "\"Position\"\\s*variant\\s*int64 (\\d+)").Groups[1].Value) / 1000000,
                Length = Number(Regex.Match(data, "\"mpris:length\"\\s*variant\\s*int64 (\\d+)").Groups[1].Value) / 1000000 };
        }

        internal static bool SendKey(Song shown, byte key, bool mac)
        {
            string command = key == 0xB3 ? "playpause" : key == 0xB0 ? "next track" : key == 0xB1 ? "previous track" : null;
            if (command == null || shown == null) return false;
            if (mac)
            {
                if (shown.App != "Spotify" && shown.App != "Music") return false;
                if (Music.Run("/usr/bin/pgrep", new[] { "-x", shown.App }) == null) return false;
                return MacReply(shown.App, command) == "OK";
            }
            string names = Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=org.freedesktop.DBus", "/org/freedesktop/DBus", "org.freedesktop.DBus.ListNames" });
            if (names == null || !Regex.IsMatch(shown.App ?? "", @"^org\.mpris\.MediaPlayer2\.[\w.\-]+$") || !Regex.IsMatch(names, "\"" + Regex.Escape(shown.App) + "\"")) return false;
            return Music.Run("dbus-send", new[] { "--session", "--print-reply", "--dest=" + shown.App, "/org/mpris/MediaPlayer2", "org.mpris.MediaPlayer2.Player." + (key == 0xB3 ? "PlayPause" : key == 0xB0 ? "Next" : "Previous") }) != null;
        }

        internal void SavePicture(string path, double scale)
        {
            var surface = (Control)Content; var size = new Size(336 * scale, 116 * scale);
            Scale(scale); surface.Measure(size); surface.Arrange(new Rect(size));
            surface.Clip = MainframeTheme.Cut(new Rect(size), 16 * scale, 6 * scale, 16 * scale, 6 * scale);
            using (var bitmap = new RenderTargetBitmap(new PixelSize((int)size.Width, (int)size.Height), new Vector(96, 96))) { bitmap.Render(surface); bitmap.Save(path); }
        }

        internal static void Checks(Pet pet, string folder, List<string> checks)
        {
            var run = Music.Run; var seam = Hands.MiniPlayer; bool mac = Hands.OnMac;
            var player = new MiniPlayer();
            var petRead = pet.miniPlayer.Read; var petWork = pet.miniPlayer.Work; bool petOff = pet.Saved.NoPlayer;
            try
            {
                double now = 100; int pollReads = 0; var commands = new List<byte>();
                Song fake = new Song { App = "Spotify", Title = "Never Fade Away", Artist = "SAMURAI", Playing = true, Position = 61, Length = 240 };
                player.Work = action => action(); player.Now = () => now; player.Read = () => { pollReads++; return fake; };
                if (player.PollSeconds != 3) throw new Exception("Hidden mini player did not wait three seconds.");
                player.Send = (shown, key) => { if (shown != fake) throw new Exception("Mini player sent to another song."); commands.Add(key); return true; };
                pet.miniPlayer.Read = () => fake; pet.miniPlayer.Work = action => action();
                Hands.Do("capri_settings", new ToolArguments { Name = "mini_player", Action = "off" });
                if (!pet.Saved.NoPlayer || pet.miniPlayer.Enabled || pet.SettingOn("mini_player") != false) throw new Exception("Mini player setting tool did not turn it off.");
                Hands.Do("capri_settings", new ToolArguments { Name = "mini_player", Action = "on" });
                if (pet.Saved.NoPlayer || !pet.miniPlayer.Enabled || pet.SettingOn("mini_player") != true) throw new Exception("Mini player setting tool did not turn it on.");
                pet.miniPlayer.Hide();
                Hands.MiniPlayer = player.Tool;
                foreach (bool onMac in new[] { true, false })
                {
                    Hands.OnMac = onMac;
                    var kit = Hands.KitFor();
                    if (!kit.Has("mini_player") || !kit.Has("capri_settings:mini_player") || !kit.Tools.Contains("\"name\":\"mini_player\"")) throw new Exception("Mini player is still missing on Mac or Linux.");
                }
                Hands.Do("mini_player", new ToolArguments { Action = "show" });
                if (!player.IsVisible || player.title.Text != fake.Title || player.artist.Text != fake.Artist || player.time.Text != "1:01 / 4:00" || player.ShowActivated || player.Focusable || player.ShowInTaskbar) throw new Exception("Mini player did not show the fake song without focus.");
                player.Start();
                if (player.PollSeconds != 1 || !player.ticks.IsEnabled || player.ticks.Interval != TimeSpan.FromSeconds(1)) throw new Exception("Playing mini player did not poll once a second.");
                fake = new Song { App = "Music", Title = "A long title " + new string('T', 160), Artist = "A long artist " + new string('A', 120), Playing = true };
                player.Refresh();
                if (player.title.Text != fake.Title || player.artist.Text != fake.Artist || player.time.Text != "") throw new Exception("Mini player kept the old title, artist or progress.");
                player.previous.Press(); player.play.Press(); player.next.Press();
                if (commands.Count != 3 || commands[0] != 0xB1 || commands[1] != 0xB3 || commands[2] != 0xB0) throw new Exception("Mini player buttons sent the wrong commands.");
                Hands.Do("mini_player", new ToolArguments { Action = "hide" }); player.Refresh();
                if (player.IsVisible || !player.Enabled) throw new Exception("Mini player hide changed its setting or came back.");
                if (player.PollSeconds != 3 || player.ticks.Interval != TimeSpan.FromSeconds(3)) throw new Exception("Hidden mini player kept polling every second.");
                Hands.Do("mini_player", new ToolArguments { Action = "show" });
                player.SetEnabled(false); int beforeOff = pollReads; player.Refresh();
                if (player.IsVisible || player.PollSeconds != 0 || player.ticks.IsEnabled || pollReads != beforeOff) throw new Exception("Mini player setting off stayed visible or kept polling.");
                Hands.Do("mini_player", new ToolArguments { Action = "show" });
                if (!player.Enabled || !player.IsVisible) throw new Exception("Mini player show did not turn its setting on.");
                Action delayed = null;
                player.Work = action => delayed = action;
                player.Refresh(); Hands.Do("mini_player", new ToolArguments { Action = "hide" }); delayed();
                if (player.IsVisible) throw new Exception("An old song read undid mini player hide.");
                player.Work = action => action(); Hands.Do("mini_player", new ToolArguments { Action = "show" });
                fake.Playing = false; now += 59; player.Refresh();
                if (!player.IsVisible || player.play.Text != "Play") throw new Exception("Mini player did not linger after a pause.");
                if (player.PollSeconds != 3 || player.ticks.Interval != TimeSpan.FromSeconds(3)) throw new Exception("Paused mini player kept polling every second.");
                now += 1; player.Refresh(); if (player.IsVisible) throw new Exception("Mini player did not hide a minute after a pause.");
                fake.Playing = true; player.Refresh(); player.close.Press(); fake.Playing = false; now += 10; player.Refresh(); fake.Playing = true; player.Refresh();
                if (!player.IsVisible) throw new Exception("Mini player dismissal did not reset after quiet.");
                player.Allowed = () => false; player.Refresh();
                if (player.IsVisible) throw new Exception("Mini player stayed up while Capri was hidden.");
                player.Allowed = () => true; fake.Length = 240; fake.Position = 61; player.Refresh();
                foreach (int scale in new[] { 100, 150, 200 })
                {
                    double dpi = scale / 100.0;
                    var area = new PixelRect(-1920, -200, 1920, 1080);
                    var near = new PixelRect(-1600, 100, 160, 240);
                    PixelPoint spot = Spot(area, dpi, new Size(336, 116), near);
                    if (spot.X != near.Right + (int)Math.Round(12 * dpi) || spot.Y + (int)Math.Ceiling(116 * dpi) != near.Bottom) throw new Exception("Mini player did not sit beside Capri at " + scale + "%.");
                    player.Place = () => Spot(area, dpi, new Size(336, 116), near);
                    player.Follow();
                    if (player.Position != spot) throw new Exception("Mini player ignored Capri's position.");
                    near = new PixelRect(-1300, 200, 160, 240); player.Follow();
                    if (player.Position == spot || player.Position != Spot(area, dpi, new Size(336, 116), near)) throw new Exception("Mini player did not follow Capri.");
                    near = new PixelRect(-160, 700, 160, 240);
                    spot = Spot(area, dpi, new Size(336, 116), near);
                    if (spot.X + (int)Math.Ceiling(336 * dpi) != near.X - (int)Math.Round(12 * dpi) || spot.Y + (int)Math.Ceiling(116 * dpi) != area.Bottom) throw new Exception("Mini player did not switch sides or stay in the work area.");
                    spot = Spot(area, dpi, new Size(2000, 2000), near);
                    if (spot != area.Position) throw new Exception("Oversized mini player left the work area's top-left.");
                    player.SavePicture(Path.Combine(folder, "mainframe-mini-player-" + scale + ".png"), scale / 100.0);
                    var boxes = new List<Rect>();
                    foreach (Control child in player.page.Children)
                    {
                        Rect box = child.Bounds;
                        if (!new Rect(0, 0, 336, 116).Contains(box)) throw new Exception("Mini player control left its plate at " + scale + "%.");
                        foreach (Rect other in boxes) if (box.Intersects(other)) throw new Exception("Mini player controls overlap at " + scale + "%.");
                        boxes.Add(box);
                    }
                    using (var picture = new Bitmap(Path.Combine(folder, "mainframe-mini-player-" + scale + ".png")))
                        if (picture.PixelSize != new PixelSize(336 * scale / 100, 116 * scale / 100)) throw new Exception("Mini player picture has the wrong scale.");
                }
                checks.Add("PASS mini player: one-second playing polls, three-second hidden and paused polls, no off polls, follows Capri on either side within the work area; songs, buttons, settings and pictures at 100%, 150% and 200%");
                var calls = new List<string>();
                Music.Run = (file, args) => { calls.Add(file + " " + string.Join(" ", args)); return null; };
                if (ReadSong(true) != null || SendKey(new Song { App = "Spotify" }, 0xB3, true)) throw new Exception("Mini player used a closed Mac player.");
                foreach (string call in calls) if (!call.StartsWith("/usr/bin/pgrep -x ", StringComparison.Ordinal)) throw new Exception("Mini player asked a closed app.");
                calls.Clear();
                Music.Run = (file, args) =>
                {
                    calls.Add(file + " " + string.Join(" ", args));
                    if (file == "/usr/bin/pgrep") return args[1] == "Music" ? "123" : null;
                    if (args[1] == Script("Music", null)) return "playing\u001fMusic title\u001fMusic artist\u001f1,5\u001f180,5\n";
                    return "OK\n";
                };
                Song music = ReadSong(true);
                if (music == null || music.App != "Music" || music.Title != "Music title" || music.Artist != "Music artist" || music.Position != 1.5 || music.Length != 180.5) throw new Exception("Mini player misread Music's reply.");
                foreach (byte key in new byte[] { 0xB1, 0xB3, 0xB0 }) if (!SendKey(music, key, true)) throw new Exception("Mini player Mac command failed.");
                foreach (string command in new[] { "previous track", "playpause", "next track" })
                    if (!calls.Contains("/usr/bin/osascript -e " + Script("Music", command))) throw new Exception("Mini player Mac command was wrong.");
                Song spotify = MacSong("Spotify", "paused\u001fSpotify title\u001fSpotify artist\u001f61\u001f240000");
                if (spotify == null || spotify.Playing || spotify.Length != 240 || !Script("Spotify", null).Contains("is not running then return")) throw new Exception("Mini player lost Spotify's units or running guard.");
                string data = "string \"PlaybackStatus\" variant string \"Playing\" string \"xesam:title\" variant string \"Linux title\" string \"xesam:artist\" variant array [ string \"Linux artist\" ] string \"Position\" variant int64 61000000 string \"mpris:length\" variant int64 240000000";
                calls.Clear();
                Music.Run = (file, args) => { calls.Add(file + " " + string.Join(" ", args)); return args[args.Length - 1] == "org.freedesktop.DBus.ListNames" ? "string \"org.mpris.MediaPlayer2.spotify\"" : args[args.Length - 1] == "string:org.mpris.MediaPlayer2.Player" ? data : ""; };
                Song linux = ReadSong(false);
                if (linux == null || linux.Title != "Linux title" || linux.Artist != "Linux artist" || linux.Position != 61 || linux.Length != 240) throw new Exception("Mini player misread D-Bus metadata.");
                foreach (byte key in new byte[] { 0xB1, 0xB3, 0xB0 }) if (!SendKey(linux, key, false)) throw new Exception("Mini player Linux command failed.");
                foreach (string command in new[] { "Previous", "PlayPause", "Next" })
                    if (!calls.Exists(call => call.EndsWith("org.mpris.MediaPlayer2.Player." + command, StringComparison.Ordinal))) throw new Exception("Mini player Linux command was wrong.");
                Music.Run = (file, args) => "";
                if (ReadSong(false) != null || SendKey(linux, 0xB0, false)) throw new Exception("Mini player asked an absent D-Bus player.");
                int notices = 0, reads = 0;
                player.Notice = line => { if (line != Automation) throw new Exception("Mini player permission line was wrong."); notices++; };
                Music.Run = (file, args) => file == "/usr/bin/pgrep" ? "123" : "CAPRI_AUTOMATION";
                player.Read = () => { reads++; return ReadSong(true); };
                player.Tool("show"); player.Refresh(); player.Refresh();
                if (player.IsVisible || notices != 1 || reads != 1) throw new Exception("Mini player repeated an Automation denial.");
                player.Tool("show"); player.Refresh();
                if (notices != 1 || reads != 2) throw new Exception("Mini player repeated its permission line or did not allow an explicit retry.");
                checks.Add("PASS mini player sources: closed apps are never asked; Mac running guards, Spotify milliseconds, Music decimal seconds, exact Mac and D-Bus commands, absent Linux players and one Automation notice");
                if (pet.miniPlayer == null || seam != pet.miniPlayer.Tool) throw new Exception("Mini player seam was not wired to her panel.");
            }
            finally
            {
                player.Close(); Music.Run = run; Hands.MiniPlayer = seam; Hands.OnMac = mac;
                pet.miniPlayer.Hide(); pet.miniPlayer.Read = petRead; pet.miniPlayer.Work = petWork;
                pet.Saved.NoPlayer = petOff; pet.miniPlayer.Enabled = !petOff;
            }
        }
    }

    internal sealed partial class Settings
    {
        [DataMember] internal bool NoPlayer;
    }

    internal sealed partial class Pet
    {
        internal MiniPlayer miniPlayer;
        private void StartMiniPlayer()
        {
            miniPlayer = new MiniPlayer { Enabled = !Saved.NoPlayer, Allowed = () => !away && !quitting, Above = () => OnTop };
            miniPlayer.Place = () =>
            {
                var screen = ScreenHere();
                return screen == null ? null : MiniPlayer.Spot(screen.WorkingArea, screen.Scaling, new Size(miniPlayer.Width, miniPlayer.Height), DrawnBox);
            };
            PositionChanged += (_, _) => { if (miniPlayer.IsVisible) miniPlayer.Follow(); };
            Unfocus(miniPlayer);
            miniPlayer.Changed = on => { Saved.NoPlayer = !on; Save(); };
            miniPlayer.Notice = line => { Brain.Log("mini player", -1, line); Talk.Answer("(mini player)", line); };
            Hands.MiniPlayer = miniPlayer.Tool;
            Opened += (_, _) => { if (Program.Smoke == null) miniPlayer.Start(); };
            Closed += (_, _) => miniPlayer.Close();
        }
    }
}
