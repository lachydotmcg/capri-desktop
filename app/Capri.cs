using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { Native.SetProcessDpiAwarenessContext(new IntPtr(-4)); } catch (EntryPointNotFoundException) { Native.SetProcessDPIAware(); }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool smoke = args.Length == 2 && args[0] == "--smoke-test";
            string identity = WindowsIdentity.GetCurrent().User.Value;
            bool first;
            using (Mutex mutex = new Mutex(true, "Local\\Capri.Desktop." + identity + (smoke ? ".Test" : ""), out first))
            using (EventWaitHandle restore = new EventWaitHandle(false, EventResetMode.AutoReset, "Local\\Capri.Restore." + identity + (smoke ? ".Test" : "")))
            {
                if (!first) { Native.AllowSetForegroundWindow(-1); restore.Set(); return 0; }
                try
                {
                    string data = smoke ? Path.GetFullPath(args[1]) : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Capri");
                    AssetCatalog.Validate(Assembly.GetExecutingAssembly());
                    if (args.Length == 1 && args[0] == "--verify-assets") return 0;
                    using (Companion app = new Companion(data, restore, smoke))
                    {
                        if (smoke) app.StartSmokeTest(data);
                        if (args.Length == 1 && args[0] == "--diagnostics") app.Shown += delegate { app.ShowDiagnostics(); };
                        Application.Run(app);
                        return app.TestFailed ? 1 : 0;
                    }
                }
                catch (Exception error)
                {
                    if (smoke) { Directory.CreateDirectory(args[1]); File.WriteAllText(Path.Combine(args[1], "failure.txt"), error.ToString()); return 1; }
                    MessageBox.Show("Capri could not start.\n\n" + error.Message, "Capri", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }
            }
        }
    }

    [DataContract]
    internal sealed class Release
    {
        [DataMember(Name = "tag_name")] public string Tag { get; set; }
        [DataMember(Name = "html_url")] public string Page { get; set; }
        [DataMember(Name = "draft")] public bool Draft { get; set; }
        [DataMember(Name = "prerelease")] public bool Prerelease { get; set; }
    }

    internal sealed class Clip
    {
        internal readonly Bitmap Strip;
        internal readonly AnimationState State;
        internal readonly int Count;
        internal readonly double Fps;
        internal readonly float Height;

        internal Clip(string name, int count, double fps, float height)
        {
            State = name.StartsWith("perch_blink") ? AnimationState.PerchBlink : name == "perch_glitch" ? AnimationState.PerchGlitch : name.StartsWith("blink") ? AnimationState.Blink : (AnimationState)Enum.Parse(typeof(AnimationState), name, true);
            Count = count;
            Fps = fps;
            Height = height;
            Strip = Companion.LoadArt("Capri.Sprite." + name + ".png");
            if (Strip == null) throw new FileNotFoundException("Capri's " + name + " animation is missing. Rebuild with assets/sprites.");
        }
    }

    internal sealed class Companion : Form
    {
        private readonly Clip idle = new Clip("idle", 16, 8, 1.11f);
        private readonly Clip blink = new Clip("blink", 4, 8, 1.11f);
        private readonly Clip blinkGlitch = new Clip("blink_glitch", 4, 8, 1.11f);
        private readonly Clip grin = new Clip("grin", 16, 8, 1.11f);
        private readonly Clip glitch = new Clip("glitch", 10, 15, 1.11f);
        private readonly Clip dangle = new Clip("dangle", 24, 10, 1.11f);
        private readonly Clip sleep = new Clip("sleep", 16, 6, 1.11f);
        private readonly Clip wake = new Clip("wake", 13, 15, 1.11f);
        private readonly Clip perch = new Clip("perch", 16, 8, 1);
        private readonly Clip perchBlink = new Clip("perch_blink", 4, 8, 1);
        private readonly Clip perchBlinkGlitch = new Clip("perch_blink_glitch", 4, 8, 1);
        private readonly Clip perchGlitch = new Clip("perch_glitch", 10, 15, 1);
        private readonly Clip mouth = new Clip("mouth", 5, 12, 1.11f);
        private readonly Random random = new Random();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip menu;
        private readonly ToolStripMenuItem visibilityItem;
        private readonly ToolStripMenuItem motionItem;
        private readonly ToolStripMenuItem topItem;
        private readonly System.Windows.Forms.Timer frames;
        private readonly System.Windows.Forms.Timer signals;
        private readonly EventWaitHandle restore;
        private readonly string settingsPath;
        private readonly bool testing;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private Preferences preferences;
        private Bitmap frame;
        private Point anchor;
        private Point pointerStart;
        private Point anchorStart;
        private bool pressed;
        private bool dragging;
        private bool hidden;
        private Clip clip;
        private double clipStart;
        private double reactionAngle, renderedAt;
        private readonly DesktopBehavior behavior = new DesktopBehavior();
        private double behaviorStep, fallRemainder, cursorStep;
        private System.Collections.Generic.List<Ledge> jumpSurfaces;
        private double jumpX, jumpY, jumpVelocity;
        private bool fullscreenPaused;
        private int idleCycle;
        private Clip blinkWith;
        private IntPtr lastForeground;
        private int topTicks;
        private double nextGlitch;
        private int clicks;
        private bool riding;
        private bool pendingClick;
        private Point pendingPoint;
        private long pendingTime;
        private bool hotkey;
        private double leanAngle;
        private Ledge seat;
        private bool falling;
        private Ledge fallTo;
        private double fallSpeed;
        private double fallStep;
        private double coveredSince = -1;
        private double seatLost = -1;
        private System.Collections.Generic.List<Rectangle> icons = new System.Collections.Generic.List<Rectangle>();
        private bool iconsBusy;
        private double iconsAt = -10;
        private const float SitX = 0.30f;
        private const float SitY = 0.79f;
        private readonly System.Windows.Forms.Timer clickDelay;
        private readonly ToolStripMenuItem rideItem;
        private readonly ToolStripMenuItem sitItem;
        private readonly ToolStripMenuItem updateItem;
        private bool updating;
        private string updatePage;
        private string balloonPage;
        private string speech;
        private double speechStart;
        private const double LettersPerSecond = 28;
        private const double MouthRate = 12;
        private readonly Brain brain = new Brain();
        private readonly TalkPill pill = new TalkPill();
        private readonly SpeechBox box = new SpeechBox();
        private readonly TypeBox typer = new TypeBox();
        private readonly Mainframe mainframe;
        private readonly Engine engine;
        private Machine machine;
        private Pick fit;
        private bool ollamaRunning;
        internal bool SettingUp;
        private TalkState talkState;
        private string heard;
        private string reply;
        private double replyStart;
        private double replyDone = -1;
        private double pillUntil = 10;
        private int asking;
        private ChatWindow textChat;
        private DiagnosticsWindow diagnostics;
        private long renderedFrames;
        private bool quitting;
        private bool textChatActive, manualRest;
        private bool finding;
        internal Func<string, string> Think;
        internal Func<string, byte[]> Speak;
        private readonly Voice voice = new Voice();
        private readonly Ears ears = new Ears();
        private readonly Speaker speaker;
        private volatile Agent agent;
        private bool chatting;
        private bool dropping;
        private int chatTicket;
        private const double WakeLevel = 0.02;
        private readonly object waking = new object();
        private readonly System.Collections.Generic.List<short[]> preroll = new System.Collections.Generic.List<short[]>();
        private System.Collections.Generic.List<short[]> backlog;
        private bool resting;
        private int loudRun;
        private bool spoke;
        private bool checkedIn;
        private double quietSince = -1;
        private double wokeAt = -1;
        private VoiceListing voices;
        private int[] mouths;
        private double pace = LettersPerSecond;
        private const string ReleasesPage = "https://github.com/lachydotmcg/capri-desktop/releases/";
        private const string OllamaPage = "https://ollama.com/download";
        private const int PaddingSize = 36;
        private const int DismountId = 0x4361;
        private const int BlinkFrom = 9;
        internal bool TestFailed;

        internal Companion(string data, EventWaitHandle signal, bool smoke)
        {
            testing = smoke;
            speaker = new Speaker(smoke);
            restore = signal;
            settingsPath = Path.Combine(data, "settings.json");
            engine = new Engine(data);
            preferences = smoke ? new Preferences() : LoadPreferences();
            clip = idle;
            nextGlitch = 30 + random.NextDouble() * 30;
            Text = "Capri";
            AccessibleName = "Capri desktop companion";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = preferences.OnTop;
            Cursor = Cursors.Hand;
            AutoScaleMode = AutoScaleMode.None;
            menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new CapriMenuColors());
            menu.ForeColor = Color.FromArgb(238, 239, 241);
            menu.BackColor = Color.FromArgb(26, 27, 31);
            menu.ShowImageMargin = false;
            ToolStripMenuItem title = new ToolStripMenuItem("CAPRI  /  0.4.0");
            title.Enabled = false;
            menu.Items.Add(title);
            menu.Items.Add(new ToolStripSeparator());
            visibilityItem = new ToolStripMenuItem("Hide Capri", null, delegate { SetHidden(!hidden); });
            menu.Items.Add(visibilityItem);
            ToolStripMenuItem sizes = new ToolStripMenuItem("Size");
            AddSize(sizes, "Small", 170);
            AddSize(sizes, "Medium", 240);
            AddSize(sizes, "Large", 320);
            menu.Items.Add(sizes);
            motionItem = new ToolStripMenuItem("Idle motion", null, delegate { ToggleMotion(); });
            motionItem.Checked = preferences.Motion;
            menu.Items.Add(motionItem);
            ToolStripMenuItem reactions = new ToolStripMenuItem("React to nearby cursor") { CheckOnClick = true, Checked = preferences.CursorReactions };
            reactions.CheckedChanged += delegate { preferences.CursorReactions = reactions.Checked; SavePreferences(); };
            menu.Items.Add(reactions);
            ToolStripMenuItem smooth = new ToolStripMenuItem("Smooth motion (60 FPS)") { CheckOnClick = true, Checked = preferences.SmoothMotion };
            smooth.CheckedChanged += delegate { preferences.SmoothMotion = smooth.Checked; frames.Interval = smooth.Checked ? 16 : 33; SavePreferences(); };
            menu.Items.Add(smooth);
            ToolStripMenuItem autonomous = new ToolStripMenuItem("Explore window edges") { CheckOnClick = true, Checked = preferences.Autonomous };
            autonomous.CheckedChanged += delegate { preferences.Autonomous = autonomous.Checked; behavior.Stop(clock.Elapsed.TotalSeconds); SavePreferences(); };
            menu.Items.Add(autonomous);
            menu.Items.Add(new ToolStripMenuItem("Hop on this edge", null, delegate { Hop(); }));
            ToolStripMenuItem fullscreen = new ToolStripMenuItem("Pause during fullscreen apps") { CheckOnClick = true, Checked = preferences.PauseFullscreen };
            fullscreen.CheckedChanged += delegate { preferences.PauseFullscreen = fullscreen.Checked; SavePreferences(); };
            menu.Items.Add(fullscreen);
            ToolStripMenuItem eco = new ToolStripMenuItem("Eco animation (15 FPS)") { CheckOnClick = true, Checked = preferences.EcoMode };
            eco.CheckedChanged += delegate { preferences.EcoMode = eco.Checked; SavePreferences(); };
            menu.Items.Add(eco);
            ToolStripMenuItem speed = new ToolStripMenuItem("Exploration speed");
            foreach (int amount in new[] { 25, 50, 90 })
            {
                int selected = amount;
                speed.DropDownItems.Add(new ToolStripMenuItem(amount + " px/s", null, delegate { preferences.MovementSpeed = selected; SavePreferences(); }));
            }
            menu.Items.Add(speed);
            ToolStripMenuItem gravity = new ToolStripMenuItem("Gravity");
            foreach (int amount in new[] { 6, 11, 16 })
            {
                int selected = amount;
                gravity.DropDownItems.Add(new ToolStripMenuItem(amount + " × character size", null, delegate { preferences.GravityScale = selected; SavePreferences(); }));
            }
            menu.Items.Add(gravity);
            topItem = new ToolStripMenuItem("Always on top", null, delegate { ToggleOnTop(); });
            topItem.Checked = preferences.OnTop;
            menu.Items.Add(topItem);
            rideItem = new ToolStripMenuItem("Perch on cursor", null, delegate { if (riding) StopRide(); else StartRide(); });
            menu.Items.Add(rideItem);
            sitItem = new ToolStripMenuItem("Sit on windows", null, delegate { ToggleSitting(); });
            sitItem.Checked = !preferences.NoSitting;
            menu.Items.Add(sitItem);
            clickDelay = new System.Windows.Forms.Timer();
            clickDelay.Interval = Math.Max(100, SystemInformation.DoubleClickTime);
            clickDelay.Tick += delegate { OnClickDelay(); };
            menu.Items.Add(new ToolStripMenuItem("Bring to this screen", null, delegate { RestoreAtCursor(); }));
            menu.Items.Add(new ToolStripMenuItem("Open chat", null, delegate { OpenTextChat(); }));
            menu.Items.Add(new ToolStripMenuItem("Diagnostics", null, delegate { ShowDiagnostics(); }));
            menu.Items.Add(new ToolStripMenuItem("Open Capri Mainframe", null, delegate { mainframe.Restore(); }));
            updateItem = new ToolStripMenuItem("Check for updates", null, delegate { if (updatePage != null) OpenPage(updatePage); else CheckForUpdates(true); });
            menu.Items.Add(updateItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Quit Capri", null, delegate { Close(); }));
            tray = new NotifyIcon();
            using (Stream icon = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico"))
            {
                tray.Icon = icon != null ? new Icon(icon) : Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            }
            tray.Text = "Capri · Double-click to restore";
            tray.ContextMenuStrip = menu;
            tray.DoubleClick += delegate { RestoreAtCursor(); };
            tray.BalloonTipClicked += delegate { if (balloonPage != null) OpenPage(balloonPage); };
            tray.Visible = !testing;
            mainframe = new Mainframe(this, tray.Icon, Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            if (!testing) mainframe.ShowBuiltIn();
            voice.Key = Voice.Unprotect(preferences.VoiceKey);
            voice.VoiceId = string.IsNullOrEmpty(preferences.VoiceId) ? Voice.Default : preferences.VoiceId;
            voice.Model = string.IsNullOrEmpty(preferences.VoiceModel) ? null : preferences.VoiceModel;
            ShowVoice();
            pill.Typing += OpenTextChat;
            pill.Voice += ToggleChat;
            pill.Mute += ToggleMic;
            ears.Heard += Listen;
            ears.Level += delegate(double value) { Post(delegate { pill.SetLevel(value); }); };
            box.Dismissed += delegate { if (box.Leaving < 0) box.Leaving = clock.Elapsed.TotalSeconds; };
            typer.Submitted += Hear;
            typer.VisibleChanged += delegate { if (!typer.Visible && replyDone >= 0) replyDone = clock.Elapsed.TotalSeconds; };
            frames = new System.Windows.Forms.Timer();
            frames.Interval = preferences.SmoothMotion ? 16 : 33;
            frames.Tick += delegate { if (fullscreenPaused || hidden || quitting) { frames.Stop(); return; } StepExploration(clock.Elapsed.TotalSeconds); if (riding) FollowCursor(); else if (falling) Tumble(clock.Elapsed.TotalSeconds); else if (seat != null) HoldSeat(clock.Elapsed.TotalSeconds); RenderFrame(); if (!riding && !dragging && !falling && seat == null && !preferences.Motion && clip == idle && speech == null && talkState == TalkState.Off) frames.Stop(); };
            signals = new System.Windows.Forms.Timer();
            signals.Interval = 250;
            signals.Tick += delegate
            {
                if (quitting) return;
                if (restore.WaitOne(0)) { RestoreAtCursor(); mainframe.Restore(); }
                CheckFullscreen();
                signals.Interval = hidden || fullscreenPaused ? 1000 : preferences.EcoMode ? 500 : 250;
                int frameInterval = preferences.EcoMode ? 67 : preferences.SmoothMotion ? 16 : 33;
                if (frames.Interval != frameInterval) frames.Interval = frameInterval;
                if (hidden || fullscreenPaused) return;
                if (!testing) CheckIdle(Native.IdleMilliseconds());
                StayOnTop();
                CheckSeat(clock.Elapsed.TotalSeconds);
                CheckChat(clock.Elapsed.TotalSeconds);
                UpdatePill(clock.Elapsed.TotalSeconds);
                // Development builds do not automatically offer upstream release executables.
            };
            signals.Start();
            Size = CanvasSize(preferences.Size);
            Rectangle work = Screen.FromPoint(Cursor.Position).WorkingArea;
            anchor = preferences.HasPosition ? new Point(preferences.X, preferences.Y) : new Point(work.Right - Width - 28, work.Bottom - Height - 12);
            anchor = KeepOnScreen(anchor);
            Location = anchor;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += DisplaysChanged;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000;
                return value;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Play(grin);
            RenderFrame();
            UpdateAnimation();
            if (testing || preferences.Welcomed)
            {
                mainframe.WindowState = FormWindowState.Minimized;
                mainframe.Show();
            }
            else
            {
                mainframe.Restore();
                preferences.Welcomed = true;
                SavePreferences();
            }
            // Text chat uses the built-in dialogue provider; no model discovery on startup.
            // Voice providers remain explicitly user-initiated in the development build.
            if (!preferences.NoSitting) RefreshIcons();
        }

        internal static Bitmap LoadArt(string name)
        {
            using (Stream image = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
            {
                if (image == null) return null;
                using (Bitmap original = new Bitmap(image))
                {
                    Bitmap copy = new Bitmap(original.Width, original.Height, PixelFormat.Format32bppPArgb);
                    using (Graphics graphics = Graphics.FromImage(copy))
                    {
                        graphics.CompositingMode = CompositingMode.SourceCopy;
                        graphics.DrawImage(original, new Rectangle(0, 0, copy.Width, copy.Height));
                    }
                    return copy;
                }
            }
        }

        private void Play(Clip next)
        {
            clip = next;
            clipStart = clock.Elapsed.TotalSeconds;
            idleCycle = 0;
            blinkWith = null;
        }

        private int Advance(double now)
        {
            bool seated = AnimationPolicy.Seated(clip.State);
            bool perched = riding || seat != null;
            if (perched && !seated) Play(perch);
            if ((!perched && seated) || (clip == dangle && !dragging && !falling)) Play(idle);
            int index = (int)((now - clipStart) * clip.Fps);
            if (AnimationPolicy.Complete(clip.State, index, clip.Count)) { Play(clip.State == AnimationState.PerchGlitch ? perch : idle); index = 0; }
            if (clip != idle && clip != perch) return index % clip.Count;
            if (!preferences.Motion) return 0;
            int cycle = index / clip.Count;
            if (cycle != idleCycle)
            {
                idleCycle = cycle;
                double roll = random.NextDouble();
                blinkWith = roll < 0.1 ? (clip == perch ? perchBlinkGlitch : blinkGlitch) : roll < 0.55 ? (clip == perch ? perchBlink : blink) : null;
                if (now >= nextGlitch && speech == null && talkState == TalkState.Off) { nextGlitch = now + 30 + random.NextDouble() * 30; Play(clip == perch ? perchGlitch : glitch); return 0; }
            }
            return index % clip.Count;
        }

        private void StayOnTop()
        {
            if (!preferences.OnTop || hidden || !IsHandleCreated || menu.Visible) return;
            IntPtr foreground = Native.GetForegroundWindow();
            if (foreground == lastForeground && ++topTicks < 8) return;
            lastForeground = foreground;
            topTicks = 0;
            Native.SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
            foreach (Form above in new Form[] { pill, box, typer }) if (above.Visible) Native.SetWindowPos(above.Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        internal static bool UpdateDue(long checkedAt, long now) { return now - checkedAt >= TimeSpan.TicksPerDay || now < checkedAt; }

        private void CheckForUpdates(bool asked)
        {
            if (updating) return;
            updating = true;
            preferences.UpdateCheckedAt = DateTime.UtcNow.Ticks;
            SavePreferences();
            if (asked) updateItem.Text = "Checking for updates...";
            ThreadPool.QueueUserWorkItem(delegate
            {
                Release found = null;
                try { found = FetchLatestRelease(); } catch { }
                try { if (!IsDisposed) BeginInvoke((Action)delegate { ShowUpdate(found, asked); }); } catch (InvalidOperationException) { }
            });
        }

        internal static Release FetchLatestRelease()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("https://api.github.com/repos/lachydotmcg/capri-desktop/releases/latest");
            request.UserAgent = "Capri-Desktop";
            request.Accept = "application/vnd.github+json";
            request.Timeout = 10000;
            request.ReadWriteTimeout = 10000;
            using (WebResponse response = request.GetResponse())
            using (Stream body = response.GetResponseStream()) return ParseRelease(body);
        }

        internal static Release ParseRelease(Stream body) { return (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(body); }

        internal static bool IsNewer(string tag, Version current)
        {
            Version found;
            if (tag == null || !Version.TryParse(tag.TrimStart('v', 'V'), out found)) return false;
            return new Version(found.Major, found.Minor, Math.Max(0, found.Build)) > new Version(current.Major, current.Minor, Math.Max(0, current.Build));
        }

        private void ShowUpdate(Release found, bool asked)
        {
            updating = false;
            Version current = Assembly.GetExecutingAssembly().GetName().Version;
            bool usable = found != null && !found.Draft && !found.Prerelease && found.Page != null && found.Page.StartsWith(ReleasesPage, StringComparison.Ordinal);
            if (usable && IsNewer(found.Tag, current))
            {
                string version = found.Tag.TrimStart('v', 'V');
                updatePage = found.Page;
                updateItem.Text = "Download Capri " + version;
                balloonPage = found.Page;
                tray.ShowBalloonTip(8000, "Capri " + version + " is out", "Click here to download the update.", ToolTipIcon.Info);
                return;
            }
            updateItem.Text = "Check for updates";
            if (!usable) { preferences.UpdateCheckedAt = DateTime.UtcNow.Ticks - TimeSpan.TicksPerDay + TimeSpan.TicksPerHour; SavePreferences(); }
            if (!asked) return;
            balloonPage = null;
            tray.ShowBalloonTip(4000, "Capri", usable ? "You're on the latest version (" + current.ToString(3) + ")." : "Couldn't check for updates right now. Try again later.", ToolTipIcon.Info);
        }

        private static void OpenPage(string page)
        {
            if (!page.StartsWith(ReleasesPage, StringComparison.Ordinal) && page != OllamaPage) return;
            try { Process.Start(page); } catch (System.ComponentModel.Win32Exception) { }
        }

        private void CheckIdle(long quiet)
        {
            if (manualRest) return;
            if (clip == sleep) { if (quiet < 1000) Play(wake); return; }
            if (quiet >= 120000 && clip == idle && preferences.Motion && !riding && !dragging && !hidden && !chatting && speech == null && talkState == TalkState.Off && !typer.Visible) Play(sleep);
        }

        private static Size CanvasSize(int size) { return new Size(size + PaddingSize * 2, size + PaddingSize * 2); }

        private void AddSize(ToolStripMenuItem parent, string label, int size)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label, null, delegate { SetCharacterSize(size); });
            item.Tag = size;
            item.Checked = preferences.Size == size;
            parent.DropDownItems.Add(item);
        }

        internal void SetCharacterSize(int size)
        {
            Point center = new Point(anchor.X + Width / 2, anchor.Y + Height / 2);
            int hipX = Hip().X;
            preferences.Size = size;
            Size = CanvasSize(size);
            anchor = seat != null ? Seated(hipX, seat.Y) : KeepOnScreen(new Point(center.X - Width / 2, center.Y - Height / 2));
            Location = anchor;
            foreach (ToolStripItem entry in menu.Items)
            {
                ToolStripMenuItem group = entry as ToolStripMenuItem;
                if (group == null || group.Text != "Size") continue;
                foreach (ToolStripMenuItem option in group.DropDownItems) option.Checked = (int)option.Tag == size;
            }
            RenderFrame();
            SavePreferences();
            mainframe.Sync();
        }

        private Point KeepOnScreen(Point point)
        {
            Rectangle area = Screen.FromPoint(new Point(point.X + Width / 2, point.Y + Height / 2)).WorkingArea;
            return RuntimePolicy.Clamp(point, Size, area);
        }

        private void DisplaysChanged(object sender, EventArgs e)
        {
            Post(delegate
            {
                behavior.Stop(clock.Elapsed.TotalSeconds);
                jumpSurfaces = null; fallTo = null; falling = false; fallSpeed = fallRemainder = 0;
                if (riding) StopRide();
                if (seat != null) StandUp();
                anchor = KeepOnScreen(anchor); Location = anchor;
                if (textChat != null && !textChat.IsDisposed) textChat.KeepVisibleOnDesktop();
                RenderFrame();
            });
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            behavior.Stop(clock.Elapsed.TotalSeconds);
            jumpSurfaces = null;
            manualRest = false;
            pressed = true;
            dragging = false;
            pointerStart = Cursor.Position;
            anchorStart = anchor;
            Capture = true;
        }

        private void MovePointer(Point position)
        {
            if (!pressed) return;
            int dx = position.X - pointerStart.X;
            int dy = position.Y - pointerStart.Y;
            if (!dragging && dx * dx + dy * dy < 25) return;
            if (!dragging) { seat = null; falling = false; Play(dangle); frames.Start(); RefreshIcons(); }
            dragging = true;
            pendingClick = false;
            clickDelay.Stop();
            anchor = KeepOnScreen(new Point(anchorStart.X + dx, anchorStart.Y + dy));
            Location = anchor;
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); MovePointer(Cursor.Position); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { menu.Show(Cursor.Position); return; }
            if (e.Button != MouseButtons.Left || !pressed) return;
            pressed = false;
            Capture = false;
            if (dragging) { pendingClick = false; clickDelay.Stop(); if (!Drop()) SavePreferences(); }
            else if (pendingClick && IsDoubleClick(pendingPoint, Cursor.Position, clock.ElapsedMilliseconds - pendingTime)) { pendingClick = false; clickDelay.Stop(); StartRide(); }
            else { pendingClick = true; pendingPoint = Cursor.Position; pendingTime = clock.ElapsedMilliseconds; clickDelay.Stop(); clickDelay.Start(); }
            dragging = false;
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) pressed = false; }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084 && frame != null)
            {
                long value = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(value & 65535), (short)((value >> 16) & 65535)));
                if (!Solid(point))
                {
                    message.Result = new IntPtr(-1);
                    return;
                }
            }
            if (message.Msg == 0x0312 && message.WParam.ToInt32() == DismountId) { StopRide(); return; }
            if (message.Msg == 0x02E0) { anchor = KeepOnScreen(anchor); Location = anchor; RenderFrame(); }
            base.WndProc(ref message);
        }

        private bool Solid(Point point)
        {
            if (frame == null) return false;
            for (int dx = -4; dx <= 4; dx += 4)
                for (int dy = -4; dy <= 4; dy += 4)
                {
                    int x = point.X + dx;
                    int y = point.Y + dy;
                    if (x >= 0 && y >= 0 && x < frame.Width && y < frame.Height && frame.GetPixel(x, y).A >= 12) return true;
                }
            return false;
        }

        private void OnClickDelay()
        {
            clickDelay.Stop();
            if (!pendingClick) return;
            pendingClick = false;
            if (hidden || riding || dragging || pressed) return;
            React();
            if (!testing) OpenTextChat();
        }

        internal static bool IsDoubleClick(Point a, Point b, long elapsed)
        {
            Size box = SystemInformation.DoubleClickSize;
            return elapsed >= 0 && elapsed <= SystemInformation.DoubleClickTime && Math.Abs(a.X - b.X) <= box.Width / 2 && Math.Abs(a.Y - b.Y) <= box.Height / 2;
        }

        private void StartRide()
        {
            if (riding || hidden || !IsHandleCreated) return;
            pendingClick = false;
            clickDelay.Stop();
            pressed = false;
            dragging = false;
            Capture = false;
            seat = null;
            falling = false;
            hotkey = Native.RegisterHotKey(Handle, DismountId, 0x0001 | 0x0002 | 0x4000, 0x43);
            riding = true;
            Native.SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) | 0x20);
            rideItem.Text = "Hop off";
            tray.Text = hotkey ? "Capri · Ctrl + Alt + C to hop off" : "Capri · Hop off from this menu";
            if (!hotkey && !testing) { balloonPage = null; tray.ShowBalloonTip(5000, "Capri", "Ctrl + Alt + C is taken by another app. Right-click Capri's tray icon and choose Hop off.", ToolTipIcon.Info); }
            Play(perchGlitch);
            frames.Start();
            pill.Hide();
            mainframe.Sync();
        }

        private void StopRide()
        {
            if (hotkey && IsHandleCreated) Native.UnregisterHotKey(Handle, DismountId);
            hotkey = false;
            if (!riding) return;
            riding = false;
            leanAngle = 0;
            Play(grin);
            if (IsHandleCreated) Native.SetWindowLong(Handle, -20, Native.GetWindowLong(Handle, -20) & ~0x20);
            if (rideItem != null) rideItem.Text = "Perch on cursor";
            if (tray != null) tray.Text = "Capri · Double-click to restore";
            if (!IsDisposed) { Location = anchor; UpdateAnimation(); SavePreferences(); mainframe.Sync(); }
        }

        private void Hop()
        {
            if (seat == null || hidden || fullscreenPaused || dragging || riding || falling || preferences.NoSitting) return;
            behavior.Stop(clock.Elapsed.TotalSeconds);
            fallTo = seat;
            jumpSurfaces = null;
            if (!testing)
            {
                Point start = Hip();
                var surfaces = Ledges.Find(Skip(), new Rectangle[0]);
                Ledge destination = null; double best = double.MaxValue, velocity = 0;
                double gravity = preferences.Size * (preferences.GravityScale >= 6 && preferences.GravityScale <= 16 ? preferences.GravityScale : 11);
                Rectangle monitor = Screen.FromPoint(start).Bounds;
                foreach (Ledge candidate in surfaces)
                {
                    if (candidate.Window == IntPtr.Zero || candidate.Window == seat.Window || candidate.Right - candidate.Left < 48) continue;
                    int x = Math.Max(candidate.Left + 24, Math.Min(start.X, candidate.Right - 24));
                    if (Math.Abs(x - start.X) < 24 || !monitor.Contains(new Rectangle(Seated(x, candidate.Y), Size))) continue;
                    double speed;
                    if (!MotionPhysics.PlanJump(x - start.X, candidate.Y - start.Y, preferences.Size * 2.5, gravity, 260, out speed)) continue;
                    double distance = Math.Abs(x - start.X) + Math.Abs(candidate.Y - start.Y);
                    if (distance < best) { best = distance; destination = candidate; velocity = speed; }
                }
                if (destination != null)
                {
                    jumpSurfaces = surfaces;
                    jumpX = start.X; jumpY = start.Y; jumpVelocity = velocity;
                    fallTo = null;
                }
            }
            seat = null;
            falling = true;
            fallSpeed = -preferences.Size * 2.5;
            fallRemainder = 0;
            fallStep = clock.Elapsed.TotalSeconds;
            Play(dangle);
            frames.Start();
        }

        private void CheckFullscreen()
        {
            bool pause = preferences.PauseFullscreen && Ledges.Fullscreen(Skip());
            if (pause == fullscreenPaused) return;
            fullscreenPaused = pause;
            behavior.Stop(clock.Elapsed.TotalSeconds);
            if (pause) { frames.Stop(); Hide(); pill.Hide(); box.Hide(); typer.Hide(); }
            else if (!hidden) { anchor = KeepOnScreen(anchor); Show(); UpdateAnimation(); }
        }

        private void StepExploration(double now)
        {
            double elapsed = now - behaviorStep; behaviorStep = now;
            bool allowed = preferences.Autonomous && preferences.Motion && !preferences.NoSitting &&
                !hidden && !dragging && !pressed && !riding && !falling && !chatting && !textChatActive && speech == null &&
                talkState == TalkState.Off && !typer.Visible && !menu.Visible && clip != sleep;
            if (!allowed) { behavior.Stop(now); return; }
            if (behavior.Due(now, true))
            {
                if (seat == null) { Drop(); behavior.Stop(now); return; }
                behavior.Begin(now, random.Next(2) == 0 ? -1 : 1);
            }
            if (seat == null || !seat.Icon.IsEmpty) return;
            int delta = behavior.Step(now, elapsed, preferences.MovementSpeed >= 25 && preferences.MovementSpeed <= 90 ? preferences.MovementSpeed : 50);
            if (delta == 0) return;
            int x = Hip().X + delta;
            if (x < seat.Left + 12 || x > seat.Right - 12)
            { behavior.Direction = -behavior.Direction; behavior.Velocity = 0; return; }
            Point candidate = Seated(x, seat.Y);
            Rectangle screen = Screen.FromPoint(new Point(x, seat.Y - 1)).Bounds;
            if (!screen.Contains(new Rectangle(candidate, Size))) { behavior.Stop(now); return; }
            anchor = candidate;
        }

        private void FollowCursor()
        {
            Point cursor = Cursor.Position;
            Rectangle area = Screen.FromPoint(cursor).WorkingArea;
            int seatX = PaddingSize + (int)(preferences.Size * 0.267f) - 12;
            int seatY = PaddingSize + (int)(preferences.Size * 0.73f) + 12;
            int tx = Math.Max(area.Left, Math.Min(cursor.X - seatX, area.Right - Width));
            int ty = Math.Max(area.Top, Math.Min(cursor.Y - seatY, area.Bottom - Height));
            double now = clock.Elapsed.TotalSeconds;
            double elapsed = Math.Max(0, Math.Min(.1, now - cursorStep)); cursorStep = now;
            int nx = (int)Math.Round(AnimationPolicy.Approach(anchor.X, tx, elapsed));
            int ny = (int)Math.Round(AnimationPolicy.Approach(anchor.Y, ty, elapsed));
            double velocity = Math.Max(-8, Math.Min(8, (nx - anchor.X) * 0.4));
            leanAngle = leanAngle * 0.8 + velocity * 0.2;
            anchor = new Point(Math.Max(area.Left, Math.Min(nx, area.Right - Width)), Math.Max(area.Top, Math.Min(ny, area.Bottom - Height)));
        }

        private Point Hip() { return new Point(anchor.X + PaddingSize + (int)(preferences.Size * SitX), anchor.Y + PaddingSize + (int)(preferences.Size * SitY)); }

        private Point Seated(int x, int y) { return new Point(x - PaddingSize - (int)(preferences.Size * SitX), y - PaddingSize - (int)(preferences.Size * SitY)); }

        private System.Collections.Generic.List<IntPtr> Skip()
        {
            System.Collections.Generic.List<IntPtr> skip = new System.Collections.Generic.List<IntPtr>();
            foreach (Form own in new Form[] { this, pill, box, typer, mainframe, textChat, diagnostics }) if (own != null && !own.IsDisposed && own.IsHandleCreated) skip.Add(own.Handle);
            return skip;
        }

        private bool Drop()
        {
            if (preferences.NoSitting) return false;
            Ledge target = Ledges.Below(Ledges.Find(Skip(), icons), Hip(), preferences.Size / 6, (int)(preferences.Size * 0.75f), 24, PaddingSize + (int)(preferences.Size * SitY));
            if (target == null) return false;
            falling = true;
            jumpSurfaces = null;
            fallTo = target;
            fallSpeed = 0;
            fallRemainder = 0;
            fallStep = clock.Elapsed.TotalSeconds;
            frames.Start();
            return true;
        }

        private void StandUp()
        {
            seat = null;
            jumpSurfaces = null;
            coveredSince = -1;
            seatLost = -1;
            falling = false;
            fallTo = null;
            leanAngle = 0;
            Play(glitch);
            frames.Start();
        }

        private void Tumble(double now)
        {
            double step = Math.Max(0, Math.Min(0.1, now - fallStep));
            fallStep = now;
            Point hip = Hip();
            if (jumpSurfaces != null) { StepWindowJump(step); return; }
            if (fallTo == null) fallTo = Ledges.Below(Ledges.Find(Skip(), icons), hip, 0, int.MaxValue, 0, PaddingSize + (int)(preferences.Size * SitY));
            if (fallTo == null) { falling = false; anchor = KeepOnScreen(anchor); Play(idle); SavePreferences(); return; }
            if (fallTo.Window != IntPtr.Zero)
            {
                Rectangle bounds;
                if (!Ledges.Showing(fallTo.Window, out bounds) || !Ledges.Seatable(fallTo.Window, bounds)) { fallTo = null; return; }
                fallTo.Left = bounds.Left; fallTo.Right = bounds.Right; fallTo.Y = bounds.Top; fallTo.Bounds = bounds;
                if (hip.X < bounds.Left || hip.X > bounds.Right || hip.Y > bounds.Top) { fallTo = null; return; }
            }
            fallRemainder += MotionPhysics.Fall(ref fallSpeed, preferences.Size * (preferences.GravityScale >= 6 && preferences.GravityScale <= 16 ? preferences.GravityScale : 11), step);
            int drop = (int)fallRemainder;
            fallRemainder -= drop;
            if (fallSpeed >= 0 && hip.Y + drop >= fallTo.Y) Land(fallTo);
            else anchor.Y += drop;
        }

        private void StepWindowJump(double elapsed)
        {
            double beforeY = jumpY, beforeX = jumpX;
            jumpX += jumpVelocity * elapsed;
            jumpY += MotionPhysics.Fall(ref fallSpeed, preferences.Size * (preferences.GravityScale >= 6 && preferences.GravityScale <= 16 ? preferences.GravityScale : 11), elapsed);
            Ledge landing = null;
            if (fallSpeed >= 0)
            {
                foreach (Ledge surface in jumpSurfaces)
                {
                    if (surface.Window != IntPtr.Zero)
                    {
                        Rectangle bounds;
                        if (!Ledges.Showing(surface.Window, out bounds) || !Ledges.Seatable(surface.Window, bounds)) continue;
                        surface.Left = bounds.Left; surface.Right = bounds.Right; surface.Y = bounds.Top; surface.Bounds = bounds;
                    }
                    if (!MotionPhysics.Crosses(beforeY, jumpY, surface.Y)) continue;
                    double fraction = jumpY == beforeY ? 1 : (surface.Y - beforeY) / (jumpY - beforeY);
                    double crossingX = beforeX + (jumpX - beforeX) * fraction;
                    if (crossingX < surface.Left + 8 || crossingX > surface.Right - 8) continue;
                    if (!surface.Floor && Ledges.Covered(surface, (int)crossingX, Skip())) continue;
                    if (landing == null || surface.Y < landing.Y) landing = surface;
                }
            }
            anchor = Seated((int)Math.Round(jumpX), (int)Math.Round(jumpY));
            if (landing != null) { jumpSurfaces = null; Land(landing); return; }
            Rectangle monitor = Screen.FromPoint(Hip()).Bounds;
            if (!monitor.Contains(new Rectangle(anchor, Size)))
            {
                jumpSurfaces = null; anchor = KeepOnScreen(anchor); fallTo = null;
                fallSpeed = Math.Max(0, fallSpeed); fallRemainder = 0;
            }
        }

        private void Land(Ledge ledge)
        {
            fallTo = null;
            if (ledge.Window != IntPtr.Zero)
            {
                Rectangle bounds;
                if (!Ledges.Showing(ledge.Window, out bounds) || !Ledges.Seatable(ledge.Window, bounds)) return;
                if (bounds != ledge.Bounds) { ledge.Left = bounds.Left; ledge.Right = bounds.Right; ledge.Y = bounds.Top; ledge.Bounds = bounds; }
                if (Hip().X < ledge.Left || Hip().X > ledge.Right || Ledges.Covered(ledge, Hip().X, Skip())) return;
            }
            falling = false;
            coveredSince = -1;
            seatLost = -1;
            int x = ledge.Icon.IsEmpty ? Math.Max(ledge.Left + 8, Math.Min(Hip().X, ledge.Right - 8)) : ledge.Icon.Left + ledge.Icon.Width / 2;
            seat = ledge;
            leanAngle = 0;
            anchor = Seated(x, ledge.Y);
            Play(perchGlitch);
            SavePreferences();
        }

        private void HoldSeat(double now)
        {
            if (seat.Window == IntPtr.Zero) return;
            Rectangle bounds;
            if (!Ledges.Showing(seat.Window, out bounds) || !Ledges.Seatable(seat.Window, bounds))
            {
                if (seatLost < 0) seatLost = now;
                if (!Ledges.Alive(seat.Window) || now - seatLost >= 0.3)
                { StandUp(); falling = true; fallSpeed = 0; fallRemainder = 0; fallStep = now; Play(dangle); }
                return;
            }
            seatLost = -1;
            if (bounds == seat.Bounds) return;
            Rectangle screen = Screen.FromRectangle(bounds).Bounds;
            if (!screen.Contains(new Rectangle(Seated(Hip().X, bounds.Top), Size)))
            { StandUp(); anchor = KeepOnScreen(anchor); return; }
            int dx = bounds.Width == seat.Bounds.Width ? bounds.Left - seat.Bounds.Left : 0;
            int x = Math.Max(bounds.Left + 8, Math.Min(Hip().X + dx, bounds.Right - 8));
            seat.Bounds = bounds;
            seat.Left = bounds.Left;
            seat.Right = bounds.Right;
            seat.Y = bounds.Top;
            anchor = Seated(x, bounds.Top);
        }

        private void CheckSeat(double now)
        {
            if (seat == null || falling || seat.Floor) return;
            if (!seat.Icon.IsEmpty && now - iconsAt > 2) RefreshIcons();
            if (!Ledges.Covered(seat, Hip().X, Skip())) { coveredSince = -1; return; }
            if (coveredSince < 0) coveredSince = now;
            if (now - coveredSince >= 0.7) StandUp();
        }

        private void RefreshIcons()
        {
            if (iconsBusy) return;
            iconsBusy = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                System.Collections.Generic.List<Rectangle> found;
                try { found = Ledges.Icons(); } catch { found = new System.Collections.Generic.List<Rectangle>(); }
                Post(delegate
                {
                    iconsBusy = false;
                    icons = found;
                    iconsAt = clock.Elapsed.TotalSeconds;
                    if (seat != null && !seat.Icon.IsEmpty && !falling && !found.Contains(seat.Icon)) StandUp();
                });
            });
        }

        internal bool Sitting { get { return !preferences.NoSitting; } }

        internal void ToggleSitting()
        {
            preferences.NoSitting = !preferences.NoSitting;
            sitItem.Checked = !preferences.NoSitting;
            if (preferences.NoSitting && (seat != null || falling)) { seat = null; falling = false; jumpSurfaces = null; fallTo = null; Play(idle); UpdateAnimation(); }
            SavePreferences();
        }

        private void React()
        {
            clicks++;
            Play(seat != null ? perchGlitch : glitch);
            if (!hidden) frames.Start();
            RenderFrame();
        }

        private void SetHidden(bool value)
        {
            hidden = value;
            visibilityItem.Text = value ? "Show Capri" : "Hide Capri";
            if (value) { StopRide(); seat = null; falling = false; jumpSurfaces = null; fallTo = null; pendingClick = false; clickDelay.Stop(); frames.Stop(); Hide(); pill.Hide(); typer.Hide(); CloseTalk(); StopChat(); }
            else if (!fullscreenPaused) { anchor = KeepOnScreen(anchor); Location = anchor; Play(manualRest ? sleep : grin); Show(); RenderFrame(); UpdateAnimation(); }
            mainframe.Sync();
        }

        private void RestoreAtCursor()
        {
            jumpSurfaces = null; fallTo = null;
            StopRide();
            seat = null;
            falling = false;
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            anchor = new Point(area.Right - Width - 28, area.Bottom - Height - 12);
            SetHidden(false);
            SavePreferences();
        }

        internal bool IsHidden { get { return hidden; } }
        internal bool Riding { get { return riding; } }
        internal int CharacterSize { get { return preferences.Size; } }
        internal bool Motion { get { return preferences.Motion; } }
        internal bool OnTop { get { return preferences.OnTop; } }
        internal void ToggleHidden() { SetHidden(!hidden); }
        internal void BringHere() { RestoreAtCursor(); }
        internal void TogglePerch() { if (riding) StopRide(); else StartRide(); }
        internal void CheckUpdates() { if (updatePage != null) OpenPage(updatePage); else CheckForUpdates(true); }
        internal void OpenOllamaPage() { OpenPage(OllamaPage); }
        internal void Quit() { Close(); }

        internal void ToggleMotion()
        {
            preferences.Motion = !preferences.Motion;
            motionItem.Checked = preferences.Motion;
            UpdateAnimation();
            SavePreferences();
            mainframe.Sync();
        }

        internal void ToggleOnTop()
        {
            preferences.OnTop = !preferences.OnTop;
            TopMost = preferences.OnTop;
            topItem.Checked = preferences.OnTop;
            SavePreferences();
            mainframe.Sync();
        }

        internal void FindBrain()
        {
            if (!testing) { OpenTextChat(); return; }
            if (finding || SettingUp) return;
            finding = true;
            mainframe.Searching();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] installed = null;
                try { installed = Brain.InstalledModels(); } catch (WebException) { } catch (IOException) { } catch (SerializationException) { }
                Machine found = Machine.Read();
                Pick local = engine.Installed();
                Post(delegate { finding = false; UseBrain(installed, found, local); });
            });
        }

        private void Post(Action action)
        {
            try
            {
                if (!quitting && !IsDisposed && IsHandleCreated)
                    BeginInvoke((Action)delegate { if (!quitting && !IsDisposed) action(); });
            }
            catch (InvalidOperationException) { }
        }

        private void UseBrain(string[] installed, Machine found, Pick local)
        {
            machine = found;
            fit = Engine.Fit(found);
            ollamaRunning = installed != null;
            if (installed != null && installed.Length > 0)
            {
                brain.Local = null;
                brain.Model = Brain.PickModel(installed, preferences.Model, fit);
                mainframe.ShowOllama(installed, brain.Model);
            }
            else if (local != null)
            {
                brain.LocalPick = local;
                brain.Local = engine;
                brain.Model = local.Name;
                mainframe.ShowLocal(local);
            }
            else
            {
                brain.Local = null;
                brain.Model = null;
                mainframe.ShowSetup(found, fit, ollamaRunning, null);
            }
        }

        internal void SetUpBrain()
        {
            if (!testing) { OpenTextChat(); return; }
            if (SettingUp || machine == null) return;
            SettingUp = true;
            engine.Cancel = false;
            Pick pick = fit;
            bool viaOllama = ollamaRunning;
            mainframe.ShowProgress(1, "Starting download", 0, 0);
            long shownAt = 0;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Action<string, long, long> progress = delegate(string text, long done, long total)
                {
                    long now = clock.ElapsedMilliseconds;
                    if (now - Interlocked.Read(ref shownAt) < 200 && done < total) return;
                    Interlocked.Exchange(ref shownAt, now);
                    string label = viaOllama ? "Downloading " + pick.Name : text;
                    Post(delegate { if (SettingUp) mainframe.ShowProgress(1, label, done, total); });
                };
                try
                {
                    if (viaOllama) Brain.Pull(pick.Ollama, progress, delegate { return engine.Cancel; });
                    else engine.Install(pick, progress);
                    Post(delegate { mainframe.ShowProgress(2, "Loading " + pick.Name, 0, 0); });
                    if (viaOllama) { brain.Local = null; brain.Model = pick.Ollama; brain.Warm(); }
                    else { brain.LocalPick = pick; brain.Local = engine; brain.Model = pick.Name; engine.Ensure(pick); }
                    Post(delegate
                    {
                        SettingUp = false;
                        if (viaOllama) { preferences.Model = pick.Ollama; SavePreferences(); FindBrain(); }
                        else mainframe.ShowLocal(pick);
                        Greet();
                    });
                }
                catch (OperationCanceledException) { Post(delegate { SettingUp = false; mainframe.ShowSetup(machine, pick, viaOllama, "Cancelled. It picks up where it left off."); }); }
                catch (Exception error) { string problem = error.Message.Length > 70 ? error.Message.Substring(0, 67) + "..." : error.Message; Post(delegate { SettingUp = false; mainframe.ShowSetup(machine, pick, viaOllama, problem); }); }
            });
        }

        internal void CancelSetup() { engine.Cancel = true; }

        private void Greet()
        {
            if (hidden) SetHidden(false);
            Hear("(You just woke up on this PC with a brand new brain. Say hi to the user in one short line.)", "brain online", "> sys: ");
        }

        internal void ChooseModel(string name)
        {
            if (name == brain.Model) return;
            brain.Model = name;
            brain.Forget();
            preferences.Model = name;
            SavePreferences();
        }

        internal void ConnectVoice(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length > 0) ListVoices(key, true);
        }

        internal void ForgetVoice()
        {
            StopChat();
            voice.Stop();
            voice.Key = null;
            voices = null;
            preferences.VoiceKey = null;
            preferences.AgentId = null;
            preferences.AgentVersion = 0;
            SavePreferences();
            ShowVoice();
        }

        internal void ChooseVoice(string id)
        {
            id = (id ?? "").Trim();
            if (id.Length == 0) id = Voice.Default;
            if (id == voice.VoiceId) { ShowVoice(false); return; }
            voice.VoiceId = id;
            preferences.VoiceId = id == Voice.Default ? null : id;
            SavePreferences();
            ShowVoice();
        }

        internal void ChooseVoiceModel(string id)
        {
            voice.Model = string.IsNullOrEmpty(id) ? null : id;
            preferences.VoiceModel = voice.Model;
            SavePreferences();
            ShowVoice();
        }

        internal void TestVoice()
        {
            if (voice.Key == null && Speak == null) { mainframe.VoiceNote("Paste your ElevenLabs API key first.", true); return; }
            if (hidden) SetHidden(false);
            brain.Cancel();
            heard = "voice check";
            box.Prompt = "> sys: ";
            Answer(++asking, "Voice link online. Miss me, choom?", TalkState.Talking);
        }

        private void ListVoices(string key, bool connecting)
        {
            mainframe.VoiceNote(connecting ? "Checking key..." : "Loading voices...", false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                VoiceListing found = null;
                string problem = null;
                int status = 0;
                try { found = Voice.Fetch(key); }
                catch (VoiceException error) { problem = error.Message; status = error.Status; }
                catch (Exception error) { problem = Voice.Short("Couldn't read your ElevenLabs voices: " + error.Message); }
                Post(delegate
                {
                    if (connecting && status == 401) { mainframe.VoiceNote(problem, true); return; }
                    if (connecting) { voice.Key = key; preferences.VoiceKey = Voice.Protect(key); SavePreferences(); }
                    if (voice.Key != key) return;
                    if (found != null) voices = found;
                    ShowVoice();
                    if (problem != null) mainframe.VoiceNote(problem, true);
                });
            });
        }

        private void ShowVoice(bool note = true)
        {
            mainframe.ShowVoice(voice.Key == null ? null : Voice.Tail(voice.Key), voice.VoiceId, voice.Model, voices);
            if (!note) return;
            string name = voice.VoiceId == Voice.Default ? "default voice" : "custom voice";
            if (voices != null) foreach (VoiceEntry entry in voices.Voices) if (entry.Id == voice.VoiceId && entry.Name != null) name = entry.Name;
            mainframe.VoiceNote(voice.Key == null ? "Needs your ElevenLabs API key." : "ElevenLabs · " + name, false);
        }

        private float TalkScale()
        {
            int dpi = 96;
            try { if (IsHandleCreated) dpi = Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { }
            return Math.Max(1f, dpi / 96f);
        }

        private Point Head() { return new Point(anchor.X + PaddingSize + (int)(preferences.Size * 0.52f), anchor.Y + PaddingSize - 4); }

        private Point Feet() { return new Point(anchor.X + PaddingSize + (int)(preferences.Size * 0.58f), anchor.Y + PaddingSize + (int)(preferences.Size * 0.99f)); }

        private void UpdatePill(double now)
        {
            if (!IsHandleCreated) return;
            bool allowed = !hidden && !riding && !dragging && !pressed && !falling;
            Point cursor = Cursor.Position;
            Point inside = PointToClient(cursor);
            bool near = frame != null && inside.X >= 0 && inside.Y >= 0 && inside.X < Width && inside.Y < Height && Solid(inside);
            if (allowed && (near || (pill.Visible && pill.Bounds.Contains(cursor)) || typer.Visible || chatting || talkState != TalkState.Off)) pillUntil = Math.Max(pillUntil, now + 1.5);
            if (!allowed || now > pillUntil) { if (pill.Visible) pill.Hide(); return; }
            float s = TalkScale();
            Point feet = Feet();
            Rectangle area = Screen.FromPoint(feet).WorkingArea;
            int top = Math.Min(feet.Y + (int)(2 * s), area.Bottom - TalkPill.Measure(s).Height - 2);
            pill.Place(new Point(feet.X - (int)(4 * s), top), s);
        }

        private string ChatCommand(Services.CharacterCommand command)
        {
            behavior.Stop(clock.Elapsed.TotalSeconds);
            if (hidden || fullscreenPaused) return "Capri is hidden or paused / Капри скрыта или на паузе.";
            switch (command)
            {
                case Services.CharacterCommand.Sit:
                    if (riding || dragging || falling) return "Let me finish moving first / Дай мне закончить движение.";
                    if (seat != null) return "Already sitting / Уже сижу.";
                    return Drop() ? "Heading for that edge / Сажусь на край." : "No reachable edge, or window sitting is disabled / Нет доступного края или посадка отключена.";
                case Services.CharacterCommand.Jump:
                    if (seat == null || falling || riding || dragging || preferences.NoSitting) return "I need a stable seat with window sitting enabled / Мне нужна устойчивая опора и разрешение на посадку.";
                    Hop(); return "Hop! / Прыжок!";
                case Services.CharacterCommand.Sleep:
                    if (riding || falling || dragging) return "Let me stop moving first / Сначала нужно остановиться.";
                    manualRest = true; seat = null; Play(sleep); frames.Start(); return "Rest mode on / Отдыхаю.";
                case Services.CharacterCommand.Wake:
                    manualRest = false; Play(wake); frames.Start(); return "Back to pixels / Снова в строю.";
                case Services.CharacterCommand.Follow:
                    manualRest = false; StartRide(); return riding ? "Following your cursor / Следую за курсором." : "Cannot follow right now / Сейчас не могу следовать.";
                case Services.CharacterCommand.StopFollowing:
                    StopRide(); return "Cursor released / Курсор свободен.";
                default: return "Unsupported command / Неизвестная команда.";
            }
        }

        internal void ShowDiagnostics()
        {
            if (quitting) return;
            if (diagnostics == null || diagnostics.IsDisposed)
                diagnostics = new DiagnosticsWindow(delegate { return renderedFrames; }, delegate
                { return "Animation: " + clip.State + " | " + (hidden ? "hidden" : fullscreenPaused ? "fullscreen paused" : falling ? "airborne" : "active"); });
            diagnostics.Show(); diagnostics.Activate();
        }

        private void OpenTextChat()
        {
            if (quitting) return;
            if (textChat != null && !textChat.IsDisposed) { textChat.RestoreChat(); return; }
            textChat = new ChatWindow(Path.GetDirectoryName(settingsPath), delegate(Services.ChatActivity state, string text)
            {
                if (IsDisposed || quitting) return;
                textChatActive = state == Services.ChatActivity.Thinking || state == Services.ChatActivity.Talking;
                behavior.Stop(clock.Elapsed.TotalSeconds);
                if (!RuntimePolicy.SocialAnimationAllowed(manualRest, dragging, riding, falling, hidden || fullscreenPaused))
                { if (state == Services.ChatActivity.Idle) speech = null; return; }
                AnimationState animation = Services.ChatAnimation.For(state);
                if (!manualRest && animation == AnimationState.Grin) Play(seat != null ? perchGlitch : grin);
                else if (!manualRest && animation == AnimationState.Glitch) Play(seat != null ? perchGlitch : glitch);
                else if (state == Services.ChatActivity.Thinking && !manualRest) Play(idle);
                if (state == Services.ChatActivity.Talking && !manualRest) Say(text != null && text.Length > 20 ? text.Substring(text.Length - 20) : text);
                if (state == Services.ChatActivity.Idle) { speech = null; UpdateAnimation(); }
                else if (!hidden && !fullscreenPaused) frames.Start();
            }, ChatCommand, delegate { return clip == sleep ? "sleep / сон" : riding ? "following cursor / следую за курсором" : falling ? "in the air / в воздухе" : seat != null ? "on a window edge / на краю окна" : "resting / отдыхаю"; });
            Rectangle area = Screen.FromPoint(Head()).WorkingArea;
            textChat.Location = new Point(Math.Max(area.Left, Math.Min(Right, area.Right - textChat.Width)), Math.Max(area.Top, Math.Min(Top, area.Bottom - textChat.Height)));
            textChat.Show();
        }

        private void OpenTyper()
        {
            if (typer.Visible) { typer.Hide(); return; }
            float s = TalkScale();
            typer.Open(Head(), new Point(Feet().X, Feet().Y + (int)(44 * s)), Screen.FromPoint(Head()).WorkingArea, s, box.Visible ? box.PanelOnScreen : Rectangle.Empty);
            pillUntil = clock.Elapsed.TotalSeconds + 1.5;
            if (Think != null || testing) return;
            if (brain.Model == null) FindBrain();
            else ThreadPool.QueueUserWorkItem(delegate { brain.Warm(); });
        }

        private void ToggleChat()
        {
            if (chatting) { StopChat(); return; }
            if (voice.Key == null)
            {
                heard = "(voice)";
                box.Prompt = "> you: ";
                Answer(++asking, "Plug my voice in through the Mainframe first, choom.", TalkState.Talking);
                mainframe.Restore();
                return;
            }
            chatting = true;
            ears.Muted = false;
            pill.SetChat(true, false);
            pillUntil = clock.Elapsed.TotalSeconds + 1.5;
            brain.Cancel();
            CloseTalk();
            OpenLink();
        }

        private void OpenLink()
        {
            mainframe.VoiceNote("Opening voice chat...", false);
            int ticket = ++chatTicket;
            string key = voice.Key;
            string voiceId = voice.VoiceId;
            string agentId = preferences.AgentId;
            bool stale = preferences.AgentVersion < Agent.Version;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string url = null;
                string problem = null;
                try
                {
                    if (agentId == null) agentId = Agent.Create(key, voiceId);
                    else if (stale)
                    {
                        try { Agent.Tune(key, agentId); }
                        catch (VoiceException error)
                        {
                            if (error.Status != 404 && error.Status != 400) throw;
                            agentId = Agent.Create(key, voiceId);
                        }
                    }
                    try { url = Agent.Address(key, agentId); }
                    catch (VoiceException error)
                    {
                        if (error.Status != 404 && error.Status != 400) throw;
                        agentId = Agent.Create(key, voiceId);
                        url = Agent.Address(key, agentId);
                    }
                }
                catch (VoiceException error) { problem = error.Message; }
                catch (Exception error) { problem = Voice.Short("Voice chat failed: " + error.Message); }
                Post(delegate
                {
                    if (ticket != chatTicket || !chatting) return;
                    if (problem != null) { ChatFailed(problem); return; }
                    if (preferences.AgentId != agentId || preferences.AgentVersion != Agent.Version) { preferences.AgentId = agentId; preferences.AgentVersion = Agent.Version; SavePreferences(); }
                    Connect(url, voiceId);
                });
            });
        }

        private void Connect(string url, string voiceId)
        {
            Agent opened = new Agent();
            opened.Opened += delegate(int rate) { Post(delegate { if (agent == opened) ChatOpened(rate); }); };
            opened.Heard += delegate(string text) { Post(delegate { if (agent == opened) ChatHeard(text); }); };
            opened.Replied += delegate(string text) { Post(delegate { if (agent == opened) ChatReplied(text); }); };
            opened.Audio += delegate(short[] samples) { Post(delegate { if (agent != opened || dropping) return; speaker.Play(samples); if (!frames.Enabled && !hidden && !fullscreenPaused) frames.Start(); }); };
            opened.Interrupted += delegate { Post(delegate { if (agent == opened) speaker.Clear(); }); };
            opened.Closed += delegate(string problem) { Post(delegate { if (agent != opened) return; if (problem != null) ChatFailed(problem); else Rest(); }); };
            agent = opened;
            opened.Start(url, voiceId);
        }

        private void ChatOpened(int rate)
        {
            try
            {
                speaker.Open(rate);
                if (!testing) ears.Start();
            }
            catch (VoiceException error) { ChatFailed(error.Message); return; }
            spoke = false;
            checkedIn = false;
            quietSince = -1;
            lock (waking)
            {
                wokeAt = backlog != null ? clock.Elapsed.TotalSeconds : -1;
                if (backlog != null) foreach (short[] held in backlog) agent.Send(held);
                backlog = null;
            }
            mainframe.VoiceNote("Voice chat on. Talk to her.", false);
        }

        private void ChatFailed(string problem)
        {
            StopChat();
            mainframe.VoiceNote(problem, true);
            heard = "(voice)";
            box.Prompt = "> you: ";
            Begin(++asking, "Can't open my voice link, choom. Check the Mainframe.", TalkState.Dormant, null);
        }

        private void ChatHeard(string text)
        {
            if (!chatting) return;
            spoke = true;
            checkedIn = false;
            wokeAt = -1;
            int ticket = ++asking;
            heard = text;
            box.Prompt = "> you: ";
            string blocked = Safety.Screen(text);
            dropping = blocked != null;
            if (dropping) { speaker.Clear(); Begin(ticket, blocked, TalkState.Talking, null); return; }
            reply = null;
            replyDone = -1;
            Say(null);
            talkState = TalkState.Thinking;
            box.Leaving = -1;
            UpdateAnimation();
        }

        private void ChatReplied(string text)
        {
            if (!chatting) return;
            checkedIn = !spoke;
            spoke = false;
            if (dropping) return;
            string said = Safety.Filter(Voice.Spoken(text));
            if (said != Voice.Spoken(text)) { dropping = true; speaker.Clear(); }
            reply = said;
            replyStart = clock.Elapsed.TotalSeconds;
            replyDone = -1;
            talkState = TalkState.Talking;
            box.Leaving = -1;
            pace = Agent.Pace;
            Say(null);
        }

        private void StopChat()
        {
            if (!chatting) return;
            chatting = false;
            chatTicket++;
            Agent closing = agent;
            agent = null;
            if (closing != null) closing.Stop();
            ears.Stop();
            ears.Muted = false;
            speaker.Clear();
            dropping = false;
            checkedIn = false;
            quietSince = -1;
            wokeAt = -1;
            lock (waking) { resting = false; backlog = null; preroll.Clear(); loudRun = 0; }
            pill.SetChat(false, false);
            ShowVoice();
        }

        private void CheckChat(double now)
        {
            bool due = agent != null && (checkedIn || (wokeAt >= 0 && now - wokeAt >= 8));
            if (!due || speaker.Busy) { quietSince = -1; return; }
            if (quietSince < 0) quietSince = now;
            else if (now - quietSince >= 1.2) Rest();
        }

        private void Rest()
        {
            if (!chatting) return;
            Agent closing = agent;
            agent = null;
            chatTicket++;
            if (closing != null) closing.Stop();
            dropping = false;
            checkedIn = false;
            quietSince = -1;
            wokeAt = -1;
            lock (waking) { resting = true; backlog = null; preroll.Clear(); loudRun = 0; }
            mainframe.VoiceNote("Voice chat resting. Talk and she'll pick up.", false);
        }

        private void Wake()
        {
            if (!chatting || agent != null) { lock (waking) backlog = null; return; }
            OpenLink();
        }

        private void ToggleMic()
        {
            if (!chatting) return;
            ears.Muted = !ears.Muted;
            pill.SetChat(true, ears.Muted);
        }

        private void Listen(short[] chunk)
        {
            lock (waking)
            {
                if (resting)
                {
                    preroll.Add(chunk);
                    if (preroll.Count > 5) preroll.RemoveAt(0);
                    loudRun = !speaker.Busy && Speaker.Loudness(chunk, 0, chunk.Length) >= WakeLevel ? loudRun + 1 : 0;
                    if (loudRun < 2) return;
                    resting = false;
                    loudRun = 0;
                    backlog = new System.Collections.Generic.List<short[]>(preroll);
                    preroll.Clear();
                    Post(Wake);
                    return;
                }
                if (backlog != null)
                {
                    backlog.Add(chunk);
                    if (backlog.Count > 80) backlog.RemoveAt(0);
                    return;
                }
            }
            Agent open = agent;
            if (open != null) open.Send(speaker.Busy ? new short[chunk.Length] : chunk);
        }

        internal void Hear(string text) { Hear(text, text, "> you: "); }

        private void Hear(string text, string shown, string prompt)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 240) text = text.Substring(0, 240);
            if (hidden) SetHidden(false);
            brain.Cancel();
            voice.Stop();
            int ticket = ++asking;
            heard = shown;
            box.Prompt = prompt;
            string blocked = Safety.Screen(text);
            if (blocked != null) { Answer(ticket, blocked, TalkState.Talking); return; }
            Func<string, string> think = Think;
            if (think == null)
            {
                if (brain.Model == null) { Answer(ticket, Brain.Offline, TalkState.Dormant); FindBrain(); mainframe.Restore(); return; }
                think = brain.Ask;
            }
            reply = null;
            replyDone = -1;
            Say(null);
            talkState = TalkState.Thinking;
            box.Leaving = -1;
            UpdateAnimation();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said;
                TalkState state = TalkState.Talking;
                string problem = null;
                try { said = Safety.Filter(think(text)); }
                catch (BrainException error) { said = error.Message == Brain.Offline ? Brain.Offline : "[SIGNAL CORRUPTED] My brain threw an error. Check Capri Mainframe."; problem = error.Message; state = TalkState.Dormant; }
                catch (Exception error) { said = Brain.Explain(error); state = TalkState.Dormant; }
                if (said == null) return;
                try
                {
                    if (!IsDisposed) BeginInvoke((Action)delegate
                    {
                        if (problem != null && ticket == asking) mainframe.ShowProblem(problem.Length > 60 ? problem.Substring(0, 57) + "..." : problem);
                        Answer(ticket, said, state);
                        if (said == Brain.Offline) FindBrain();
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void Answer(int ticket, string said, TalkState state)
        {
            if (ticket != asking || IsDisposed) return;
            voice.Stop();
            Func<string, byte[]> speak = Speak;
            if (speak == null && !testing && voice.Ready) speak = voice.Speak;
            string words = Voice.Spoken(said);
            if (speak == null || words.Length == 0) { Begin(ticket, said, state, null); return; }
            reply = null;
            replyDone = -1;
            Say(null);
            talkState = TalkState.Thinking;
            box.Leaving = -1;
            UpdateAnimation();
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string problem = null;
                try { wav = speak(words); }
                catch (VoiceException error) { problem = error.Message; }
                catch (Exception error) { problem = Voice.Short("Voice glitched: " + error.Message); }
                Post(delegate
                {
                    if (problem != null && ticket == asking) mainframe.VoiceNote(problem, true);
                    Begin(ticket, said, state, wav);
                });
            });
        }

        private void Begin(int ticket, string said, TalkState state, byte[] wav)
        {
            if (ticket != asking || IsDisposed) return;
            reply = said;
            replyStart = clock.Elapsed.TotalSeconds;
            replyDone = -1;
            talkState = state;
            box.Leaving = -1;
            pace = LettersPerSecond;
            Say(said);
            double seconds = wav == null ? 0 : Voice.Seconds(wav);
            if (seconds < 0.2) return;
            pace = Math.Max(8, said.Length / seconds);
            mouths = Voice.Mouths(wav, MouthRate);
            if (!testing) voice.Play(wav);
        }

        private void CloseTalk()
        {
            voice.Stop();
            talkState = TalkState.Off;
            reply = null;
            replyDone = -1;
            box.Opened = -1;
            box.Leaving = -1;
            if (box.Visible) box.Hide();
            speech = null;
        }

        private void UpdateTalk(double now)
        {
            if (talkState == TalkState.Off) { if (box.Visible) box.Hide(); return; }
            int revealed = reply == null ? 0 : (int)((now - replyStart) * pace);
            if (reply != null && revealed >= reply.Length && replyDone < 0)
            {
                replyDone = now;
                if (talkState == TalkState.Talking) talkState = TalkState.Online;
            }
            if (replyDone >= 0 && box.Leaving < 0 && !typer.Visible && now > replyDone + Math.Min(14, 4 + reply.Length * 0.05)) box.Leaving = now;
            if (box.Leaving >= 0 && now - box.Leaving > 0.25) { CloseTalk(); return; }
            if (!box.Visible && box.Opened < 0) box.Opened = now;
            box.State = talkState;
            box.Heard = heard;
            box.Reply = talkState == TalkState.Thinking ? null : reply;
            box.Revealed = revealed;
            float s = TalkScale();
            Point head = Head();
            Point feet = Feet();
            Rectangle area = Screen.FromPoint(head).WorkingArea;
            box.Update(now, s, head, new Point(feet.X, feet.Y + (int)(44 * s)), area);
            if (typer.Visible) typer.Follow(box.PanelOnScreen, area);
        }

        private void UpdateAnimation()
        {
            if (quitting) return;
            if (!preferences.Motion && !manualRest && !riding && seat == null && !falling && clip != glitch && clip != dangle && clip != wake) Play(idle);
            if ((preferences.Motion || manualRest || riding || seat != null || falling || clip != idle || speech != null || talkState != TalkState.Off) && !hidden && !fullscreenPaused) frames.Start(); else frames.Stop();
            RenderFrame();
        }

        private void RenderFrame()
        {
            if (!IsHandleCreated || hidden || fullscreenPaused || IsDisposed || quitting) return;
            double now = clock.Elapsed.TotalSeconds;
            int cell = Advance(now);
            Clip shown = clip;
            if ((clip == idle || clip == perch) && blinkWith != null && cell >= BlinkFrom && cell < BlinkFrom + blinkWith.Count) { shown = blinkWith; cell -= BlinkFrom; }
            int talk = MouthCell(now, shown);
            double target = 0;
            if (preferences.CursorReactions && preferences.Motion && !dragging && !riding && !falling && clip == idle)
            {
                Point cursor = Cursor.Position;
                double dx = cursor.X - (anchor.X + Width / 2), dy = cursor.Y - (anchor.Y + Height / 2);
                if (dx * dx + dy * dy < 160000) target = Math.Max(-3, Math.Min(3, dx / 80));
            }
            reactionAngle = AnimationPolicy.Approach(reactionAngle, target, now - renderedAt);
            renderedAt = now;
            if (frame == null || frame.Width != Width || frame.Height != Height)
            {
                if (frame != null) frame.Dispose();
                frame = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            }
            using (Graphics graphics = Graphics.FromImage(frame)) { graphics.Clear(Color.Transparent); Draw(graphics, shown, cell, talk); }
            Native.DrawLayered(Handle, frame, anchor);
            renderedFrames++;
            UpdateTalk(now);
            if (pill.Visible) UpdatePill(now);
        }

        internal void Say(string text)
        {
            speech = string.IsNullOrEmpty(text) ? null : text;
            speechStart = clock.Elapsed.TotalSeconds;
            mouths = null;
            if (speech != null && (clip == sleep || clip == wake || clip == glitch || clip == grin)) Play(idle);
            UpdateAnimation();
        }

        private int MouthCell(double now, Clip shown)
        {
            if (chatting && speaker.Busy) return clip == idle && shown != blinkGlitch ? speaker.Shape() : -1;
            if (speech == null) return -1;
            int step = (int)((now - speechStart) * MouthRate);
            if (mouths != null)
            {
                if (step >= mouths.Length) { speech = null; mouths = null; return -1; }
                return clip == idle && shown != blinkGlitch ? mouths[step] : -1;
            }
            int letter = (int)(step / MouthRate * LettersPerSecond);
            if (letter >= speech.Length) { speech = null; return -1; }
            return clip == idle && shown != blinkGlitch ? MouthFor(speech[letter], step) : -1;
        }

        internal static int MouthFor(char letter, int step)
        {
            char lower = char.ToLowerInvariant(letter);
            if ("aeo".IndexOf(lower) >= 0) return char.IsUpper(letter) ? 4 : 3;
            if ("iuy".IndexOf(lower) >= 0) return 2;
            if ("mbp".IndexOf(lower) >= 0 || !char.IsLetter(letter)) return 0;
            return step % 2 == 1 ? 1 : 2;
        }

        private void Draw(Graphics graphics, Clip shown, int cell, int talk = -1)
        {
            graphics.CompositingMode = CompositingMode.SourceOver;
            graphics.CompositingQuality = CompositingQuality.HighQuality;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            double now = clock.Elapsed.TotalSeconds;
            bool settled = preferences.Motion && !dragging && clip != dangle && clip != sleep;
            float breathe = settled ? (float)Math.Sin(now * 1.75) * 2.2f : 0;
            float tilt = settled ? (float)Math.Sin(now * 0.95) * 0.65f : 0;
            graphics.TranslateTransform(Width / 2f, Height / 2f + breathe);
            graphics.RotateTransform(tilt + (float)(leanAngle + reactionAngle));
            int width = shown.Strip.Width / shown.Count;
            float tall = preferences.Size * shown.Height;
            float wide = tall * width / shown.Strip.Height;
            graphics.DrawImage(shown.Strip, new RectangleF(-wide / 2f, -tall / 2f, wide, tall), new RectangleF(cell * width, 0, width, shown.Strip.Height), GraphicsUnit.Pixel);
            if (talk >= 0) graphics.DrawImage(mouth.Strip, new RectangleF(-wide / 2f, -tall / 2f, wide, tall), new RectangleF(talk * width, 0, width, mouth.Strip.Height), GraphicsUnit.Pixel);
        }

        private Preferences LoadPreferences()
        {
            try
            {
                using (FileStream stream = File.OpenRead(settingsPath))
                {
                    if (stream.Length > 65536) return new Preferences();
                    Preferences value = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(stream);
                    if (value == null) return new Preferences();
                    value.Normalize();
                    return value;
                }
            }
            catch { return new Preferences(); }
        }

        private void SavePreferences()
        {
            preferences.X = anchor.X;
            preferences.Y = anchor.Y;
            preferences.HasPosition = true;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));
                string temporary = settingsPath + ".new";
                using (FileStream stream = File.Create(temporary)) new DataContractJsonSerializer(typeof(Preferences)).WriteObject(stream, preferences);
                if (File.Exists(settingsPath)) File.Replace(temporary, settingsPath, null); else File.Move(temporary, settingsPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            quitting = true;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
            if (textChat != null && !textChat.IsDisposed) textChat.Shutdown();
            if (diagnostics != null) diagnostics.Dispose();
            StopRide();
            clickDelay.Stop();
            frames.Stop();
            signals.Stop();
            brain.Cancel();
            StopChat();
            speaker.Close();
            voice.Stop();
            engine.Cancel = true;
            engine.Stop();
            mainframe.Quitting = true;
            mainframe.Close();
            tray.Visible = false;
            SavePreferences();
            base.OnFormClosed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                quitting = true;
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= DisplaysChanged;
                if (textChat != null) textChat.Dispose();
                if (diagnostics != null) diagnostics.Dispose();
                clickDelay.Dispose();
                frames.Dispose();
                signals.Dispose();
                tray.Dispose();
                menu.Dispose();
                pill.Dispose();
                box.Dispose();
                typer.Dispose();
                mainframe.Dispose();
                foreach (Clip owned in new[] { idle, blink, blinkGlitch, grin, glitch, dangle, sleep, wake, perch, perchBlink, perchBlinkGlitch, perchGlitch, mouth }) owned.Strip.Dispose();
                if (frame != null) frame.Dispose();
            }
            base.Dispose(disposing);
        }

        internal void StartSmokeTest(string output)
        {
            Directory.CreateDirectory(output);
            Shown += delegate
            {
                System.Windows.Forms.Timer test = new System.Windows.Forms.Timer();
                test.Interval = 500;
                int step = 0;
                int thoughts = 0;
                byte[] tone = null;
                string voiced = null;
                System.Collections.Generic.List<string> asked = new System.Collections.Generic.List<string>();
                HttpListener fakeAgent = null;
                string agentHost = null;
                Form backdrop = null;
                System.Collections.Generic.List<string> checks = new System.Collections.Generic.List<string>();
                test.Tick += delegate
                {
                    try
                    {
                        if (step == 0)
                        {
                            preferences.NoSitting = true;
                            checks.Add("PASS native window visible: " + Visible);
                            if (!mainframe.Visible || !mainframe.ShowInTaskbar || mainframe.WindowState != FormWindowState.Minimized) throw new Exception("Mainframe did not start minimized in the taskbar.");
                            if (!Visible || frame == null) throw new Exception("Native window did not render.");
                            if ((Native.GetWindowLong(Handle, -20) & 0x00080000) == 0) throw new Exception("Window is not layered.");
                            if (frame.GetPixel(0, 0).A != 0) throw new Exception("Frame corner is opaque.");
                            checks.Add("PASS per-pixel alpha and layered window");
                            Point before = anchor;
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            Point moved = new Point(pointerStart.X - 80, pointerStart.Y - 35);
                            MovePointer(moved);
                            if (clip != dangle || !frames.Enabled) throw new Exception("Drag did not play the dangle clip.");
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            if (anchor == before || pendingClick || riding) throw new Exception("Drag did not change position or did not cancel click.");
                            RenderFrame();
                            if (clip != idle) throw new Exception("Drop did not return to idle.");
                            checks.Add("PASS drag handler changed position, dangled and settled");
                            int previous = clicks;
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            if (!pendingClick || clicks != previous) throw new Exception("Single click was not delayed.");
                            OnClickDelay();
                            if (clicks != previous + 1 || !frames.Enabled || clip != glitch) throw new Exception("Click reaction did not start.");
                            clipStart -= 1;
                            RenderFrame();
                            if (clip != idle) throw new Exception("Glitch did not return to idle.");
                            checks.Add("PASS delayed single click glitched once and returned to idle");
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, Width / 2, Height / 2, 0));
                            if (!riding || (Native.GetWindowLong(Handle, -20) & 0x20) == 0) throw new Exception("Double click did not perch with click-through.");
                            if (clip != perchGlitch) throw new Exception("Perching did not glitch on.");
                            clipStart -= 1;
                            RenderFrame();
                            if (clip != perch) throw new Exception("Perch glitch did not settle into the seated loop.");
                            checks.Add("PASS double click perched with click-through, glitched on and settled into the seated loop");
                            checks.Add(hotkey ? "PASS Ctrl+Alt+C registered while perched" : "LIMIT Ctrl+Alt+C taken; tray Hop off fallback");
                            if (IsDoubleClick(Point.Empty, new Point(200, 200), 10)) throw new Exception("Double click predicate failed.");
                            Message key = Message.Create(Handle, 0x0312, new IntPtr(DismountId), IntPtr.Zero);
                            WndProc(ref key);
                            if (riding || hotkey || (Native.GetWindowLong(Handle, -20) & 0x20) != 0) throw new Exception("Hotkey did not hop off.");
                            if (clip != grin) throw new Exception("Hop off did not grin.");
                            checks.Add("PASS Ctrl+Alt+C message hopped off, grinned and released the hotkey");
                            rideItem.PerformClick();
                            if (!riding || rideItem.Text != "Hop off") throw new Exception("Menu perch failed.");
                            rideItem.PerformClick();
                            if (riding || (Native.GetWindowLong(Handle, -20) & 0x20) != 0) throw new Exception("Hop off menu did not clean up.");
                            checks.Add("PASS tray Perch on cursor and Hop off");
                            System.Collections.Generic.List<int[]> spans = Ledges.Open(0, 100, 10, new[] { new Rectangle(20, 0, 30, 50), new Rectangle(90, 5, 40, 40), new Rectangle(0, 11, 100, 40) });
                            if (spans.Count != 2 || spans[0][0] != 0 || spans[0][1] != 20 || spans[1][0] != 50 || spans[1][1] != 90) throw new Exception("Ledge covering math failed.");
                            Point home = anchor;
                            sitItem.PerformClick();
                            if (preferences.NoSitting || !sitItem.Checked) throw new Exception("Sit on windows menu did not turn sitting on.");
                            using (Shelf shelf = new Shelf())
                            {
                                Rectangle work = Screen.PrimaryScreen.WorkingArea;
                                shelf.Bounds = new Rectangle(work.Left + work.Width / 2 - 220, work.Top + work.Height / 2 - 40, 440, 180);
                                shelf.Show();
                                anchor = Seated(shelf.Left + 220, shelf.Top - 12);
                                if (!Drop() || !falling) throw new Exception("Dropping her just above a window did not start a fall.");
                                for (int i = 0; i < 60 && falling; i++) Tumble(fallStep + 0.033);
                                if (seat == null || seat.Window != shelf.Handle || Hip().Y != shelf.Top || clip != perchGlitch) throw new Exception("She did not land on the window's top edge.");
                                clipStart -= 1;
                                RenderFrame();
                                if (clip != perch) throw new Exception("Landing did not settle into the seated loop.");
                                int seatX = Hip().X;
                                shelf.Location = new Point(shelf.Left + 40, shelf.Top + 25);
                                HoldSeat(clock.Elapsed.TotalSeconds);
                                if (Hip().X != seatX + 40 || Hip().Y != shelf.Top) throw new Exception("She did not ride along when the window moved.");
                                if (Ledges.Covered(seat, Hip().X, Skip())) throw new Exception("Top window reported as covered.");
                                using (Shelf ghost = new Shelf(0x80000 | 0x80))
                                {
                                    ghost.Bounds = Screen.PrimaryScreen.Bounds;
                                    ghost.Show();
                                    if (Ledges.Covered(seat, Hip().X, Skip())) throw new Exception("An invisible overlay knocked her off.");
                                }
                                using (Shelf tip = new Shelf())
                                {
                                    tip.Bounds = new Rectangle(Hip().X - 60, shelf.Top - 12, 120, 40);
                                    tip.Show();
                                    if (Ledges.Covered(seat, Hip().X, Skip())) throw new Exception("A popup like a tooltip or menu knocked her off.");
                                }
                                Ledge held = seat;
                                Point sat = anchor;
                                using (Shelf cover = new Shelf())
                                {
                                    cover.FormBorderStyle = FormBorderStyle.FixedSingle;
                                    cover.Bounds = new Rectangle(Hip().X - 160, shelf.Top - 50, 320, 200);
                                    cover.Show();
                                    double over = clock.Elapsed.TotalSeconds;
                                    CheckSeat(over);
                                    CheckSeat(over + 0.4);
                                    if (seat == null || falling) throw new Exception("A window passing over her knocked her off straight away.");
                                    CheckSeat(over + 0.8);
                                    if (falling || seat != null || anchor != sat || clip != glitch) throw new Exception("A window parked over her seat did not stand her up where she sat.");
                                }
                                Land(held);
                                clipStart -= 1;
                                RenderFrame();
                                if (seat != held || falling) throw new Exception("She did not get back on the window.");
                                if (Ledges.Covered(seat, Hip().X, Skip()) || seat.Window != shelf.Handle) throw new Exception("Her own Mainframe or a closed window still covered her.");
                                int originX = shelf.Left - 80;
                                int originY = anchor.Y - 20;
                                using (Bitmap still = new Bitmap(shelf.Width + 160, shelf.Bottom - originY + 20, PixelFormat.Format32bppArgb))
                                using (Graphics graphics = Graphics.FromImage(still))
                                using (SolidBrush fill = new SolidBrush(shelf.BackColor))
                                {
                                    graphics.Clear(Color.FromArgb(214, 220, 228));
                                    graphics.FillRectangle(fill, shelf.Left - originX, shelf.Top - originY, shelf.Width, shelf.Height);
                                    graphics.TranslateTransform(anchor.X - originX, anchor.Y - originY);
                                    Draw(graphics, perch, 0);
                                    still.Save(Path.Combine(output, "sit.png"), ImageFormat.Png);
                                }
                                shelf.Hide();
                                double gone = clock.Elapsed.TotalSeconds;
                                HoldSeat(gone);
                                if (falling || seat == null) throw new Exception("A window blinking out for a moment knocked her off.");
                                HoldSeat(gone + 0.4);
                                if (!falling || seat != null || anchor != sat || clip != dangle) throw new Exception("Lost support did not start falling without teleporting.");
                                int releasedY = Hip().Y;
                                for (int i = 0; i < 240 && falling; i++) Tumble(fallStep + 0.033);
                                if (falling || seat == null || seat.Window == shelf.Handle || Hip().Y < releasedY) throw new Exception("Lost support did not land on another valid surface below.");
                                checks.Add("PASS dropped above a window she fell, sat on its top edge, followed movement, ignored transient occlusion, and fell/landed after support disappeared");
                            }
                            checks.Add("PASS ledge covering math; desktop icons read: " + Ledges.Icons().Count);
                            seat = null;
                            sitItem.PerformClick();
                            if (!preferences.NoSitting || sitItem.Checked) throw new Exception("Sit on windows menu did not turn sitting off.");
                            anchor = home;
                            Play(idle);
                            rideItem.PerformClick();
                            visibilityItem.PerformClick();
                            if (Visible || !hidden || riding || hotkey) throw new Exception("Hide menu did not hide window or clean up perch.");
                            checks.Add("PASS hide menu");
                            restore.Set();
                        }
                        else if (step == 1)
                        {
                            if (!Visible || hidden || clip != grin) throw new Exception("Second-launch restore signal did not show Capri grinning.");
                            checks.Add("PASS restore signal");
                            if (!mainframe.ShowInTaskbar || !mainframe.Visible) throw new Exception("Mainframe is not in the taskbar.");
                            if (Native.IdleMilliseconds() < 0) throw new Exception("Idle time unavailable.");
                            Play(idle);
                            CheckIdle(119000);
                            if (clip != idle) throw new Exception("Slept too early.");
                            CheckIdle(130000);
                            if (clip != sleep) throw new Exception("Did not sleep after two quiet minutes.");
                            CheckIdle(0);
                            if (clip != wake) throw new Exception("Did not wake with the boot glitch.");
                            clipStart -= 1;
                            RenderFrame();
                            if (clip != idle) throw new Exception("Boot glitch did not settle into idle.");
                            Play(idle);
                            checks.Add("PASS sleeps after two quiet minutes, boots awake and settles into idle");
                            if (MouthFor('a', 0) != 3 || MouthFor('O', 0) != 4 || MouthFor('i', 0) != 2 || MouthFor('m', 0) != 0 || MouthFor(' ', 0) != 0 || MouthFor('!', 0) != 0 || MouthFor('k', 1) != 1 || MouthFor('k', 2) != 2) throw new Exception("Mouth shapes picked the wrong letters.");
                            const string spoken = "Hey OK, bye!";
                            int mouthPixels = 0;
                            Say(spoken);
                            if (speech != spoken || !frames.Enabled) throw new Exception("Say did not start talking.");
                            double said = clock.Elapsed.TotalSeconds;
                            speechStart = said;
                            if (MouthCell(said, idle) != 2) throw new Exception("First letter did not open her teeth.");
                            speechStart = said - 2.5 / MouthRate;
                            if (MouthCell(said, idle) != 4) throw new Exception("Capital O did not scream.");
                            if (MouthCell(said, blinkGlitch) != -1) throw new Exception("Mouth drew over a glitched blink.");
                            using (Bitmap quiet = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                            using (Bitmap open = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                            {
                                bool motion = preferences.Motion;
                                preferences.Motion = false;
                                using (Graphics graphics = Graphics.FromImage(quiet)) Draw(graphics, idle, 0);
                                using (Graphics graphics = Graphics.FromImage(open)) Draw(graphics, idle, 0, 3);
                                preferences.Motion = motion;
                                for (int x = 0; x < Width; x++) for (int y = 0; y < Height; y++) if (Math.Abs(quiet.GetPixel(x, y).GetBrightness() - open.GetPixel(x, y).GetBrightness()) > 0.3f) mouthPixels++;
                                if (mouthPixels < 4 || mouthPixels > 1200) throw new Exception("Open mouth changed " + mouthPixels + " pixels.");
                            }
                            CheckIdle(130000);
                            if (clip != idle) throw new Exception("Fell asleep mid-sentence.");
                            Play(idle);
                            Say(spoken);
                            nextGlitch = 0;
                            clipStart -= 2.1;
                            RenderFrame();
                            if (clip != idle) throw new Exception("Glitched mid-sentence.");
                            speechStart = clock.Elapsed.TotalSeconds - spoken.Length / LettersPerSecond - 1;
                            RenderFrame();
                            if (speech != null || MouthCell(clock.Elapsed.TotalSeconds, idle) != -1) throw new Exception("Did not stop talking at the end of the line.");
                            nextGlitch = clock.Elapsed.TotalSeconds + 60;
                            checks.Add("PASS talks: letter mouths, typing-synced flaps, jaw overlay (" + mouthPixels + " px), no sleep or glitch mid-sentence, stops at the end");
                            Version mine = Assembly.GetExecutingAssembly().GetName().Version;
                            if (!IsNewer("v9.0.0", mine) || IsNewer("v" + mine.ToString(3), mine) || IsNewer("v0.0.1", mine) || IsNewer("nonsense", mine) || IsNewer(null, mine)) throw new Exception("Update version compare failed.");
                            long now = DateTime.UtcNow.Ticks;
                            if (!UpdateDue(0, now) || UpdateDue(now - TimeSpan.TicksPerHour, now) || !UpdateDue(now - TimeSpan.TicksPerDay - 1, now)) throw new Exception("Daily update timing failed.");
                            using (MemoryStream sample = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"tag_name\":\"v9.0.0\",\"html_url\":\"" + ReleasesPage + "tag/v9.0.0\",\"draft\":false,\"prerelease\":false,\"assets\":[{\"name\":\"Capri-Setup.exe\"}],\"author\":{\"login\":\"capri\"}}")))
                                ShowUpdate(ParseRelease(sample), false);
                            if (!menu.Items.Contains(updateItem) || updatePage != ReleasesPage + "tag/v9.0.0" || updateItem.Text != "Download Capri 9.0.0") throw new Exception("A newer release was not offered.");
                            updatePage = null;
                            balloonPage = null;
                            updateItem.Text = "Check for updates";
                            checks.Add("PASS update check: version compare, daily timing, GitHub release parsing and download offer");
                            Point bounded = KeepOnScreen(new Point(int.MaxValue / 4, int.MaxValue / 4));
                            if (!Screen.FromPoint(new Point(bounded.X + Width / 2, bounded.Y + Height / 2)).WorkingArea.Contains(new Rectangle(bounded, Size))) throw new Exception("Screen bounds failed.");
                            checks.Add("PASS screen bounds");
                            ((ToolStripMenuItem)((ToolStripMenuItem)menu.Items[3]).DropDownItems[0]).PerformClick();
                            if (Width != 242) throw new Exception("Small size failed.");
                            ((ToolStripMenuItem)((ToolStripMenuItem)menu.Items[3]).DropDownItems[1]).PerformClick();
                            checks.Add("PASS size menu");
                            motionItem.PerformClick();
                            if (frames.Enabled) throw new Exception("Motion toggle did not stop idle timer.");
                            checks.Add("PASS motion pause");
                            SavePreferences();
                            if (LoadPreferences().Size != 240) throw new Exception("Settings did not round-trip.");
                            checks.Add("PASS settings round-trip in isolated test directory");
                            Clip[] strips = { idle, blink, blinkGlitch, grin, glitch, dangle, sleep, wake, perch, perchBlink, perchBlinkGlitch, perchGlitch, idle };
                            int[] cells = { 0, 1, 1, 0, 1, 3, 0, 4, 8, 1, 1, 1, 0 };
                            if (mouth.Strip.Width % mouth.Count != 0 || mouth.Strip.Width / mouth.Count != idle.Strip.Width / idle.Count || mouth.Strip.Height != idle.Strip.Height) throw new Exception("Mouth strip does not match the idle cells.");
                            using (Bitmap sheet = new Bitmap(Width * strips.Length, Height, PixelFormat.Format32bppArgb))
                            {
                                for (int i = 0; i < strips.Length; i++)
                                    using (Bitmap still = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb))
                                    {
                                        if (strips[i].Strip.Width % strips[i].Count != 0) throw new Exception("Clip " + i + " has uneven cells.");
                                        using (Graphics graphics = Graphics.FromImage(still)) Draw(graphics, strips[i], cells[i], i == strips.Length - 1 ? 3 : -1);
                                        int solid = 0;
                                        for (int x = 0; x < Width; x += 4) for (int y = 0; y < Height; y += 4) if (still.GetPixel(x, y).A > 128) solid++;
                                        if (solid < 200) throw new Exception("Clip " + i + " drew nothing.");
                                        using (Graphics graphics = Graphics.FromImage(sheet)) graphics.DrawImage(still, i * Width, 0);
                                    }
                                sheet.Save(Path.Combine(output, "clips.png"), ImageFormat.Png);
                            }
                            checks.Add("PASS all 12 clips draw: idle, blinks, grin, glitch, drag, sleep, wake and perch, plus the talking mouth");
                            using (Font display = Look.Mono(14, FontStyle.Bold))
                            using (Font slanted = Look.Mono(9f, FontStyle.Italic, GraphicsUnit.Point))
                                if (display.Name != "Share Tech Mono" || slanted.Name != "Share Tech Mono") throw new Exception("Embedded font did not load: " + display.Name);
                            checks.Add("PASS Share Tech Mono is embedded for her messages and the Mainframe");
                            if (Brain.PickModel(new[] { "deepseek-r1:7b", "qwen3:1.7b", "gemma4:e4b" }, null, null) != "gemma4:e4b" || Brain.PickModel(new[] { "qwen3:1.7b" }, "gone:1b", null) != "qwen3:1.7b" || Brain.PickModel(new[] { "deepseek-r1:7b", "mystery:3b" }, null, null) != "mystery:3b" || Brain.PickModel(new[] { "qwen3:4b-instruct-2507-q4_K_M", "qwen3:8b" }, null, null) != "qwen3:4b-instruct-2507-q4_K_M" || Brain.PickModel(new string[0], null, null) != null || Brain.PickModel(new[] { "a:1b", "b:2b" }, "b:2b", null) != "b:2b" || Brain.PickModel(new[] { "gemma4:e4b", "qwen3:4b-instruct-2507-q4_K_M" }, null, Engine.Picks[2]) != "qwen3:4b-instruct-2507-q4_K_M") throw new Exception("Model pick failed.");
                            const long Gb = 1L << 30;
                            if (Engine.Fit(new Machine { Ram = 16 * Gb, Vram = 8 * Gb }) != Engine.Picks[0] || Engine.Fit(new Machine { Ram = 32 * Gb, Vram = 2 * Gb }) != Engine.Picks[1] || Engine.Fit(new Machine { Ram = 8 * Gb - Gb / 4 }) != Engine.Picks[1] || Engine.Fit(new Machine { Ram = 4 * Gb, Vram = Gb / 2 }) != Engine.Picks[2]) throw new Exception("Hardware fit picked the wrong model.");
                            Machine here = Machine.Read();
                            if (here.Ram < Gb || here.Describe().IndexOf("GB RAM", StringComparison.Ordinal) < 0) throw new Exception("Could not read this PC's memory.");
                            string hashed = Path.Combine(output, "hash-check.txt");
                            File.WriteAllText(hashed, "capri");
                            if (Engine.Hash(hashed) != "56616ae0a91285159d0198d176ab3dae37996b5f1f4cb81d7d45f2aa99bdf565") throw new Exception("Download checksum is wrong.");
                            File.Delete(hashed);
                            foreach (Pick option in Engine.Picks) if (!option.Url.StartsWith("https://huggingface.co/", StringComparison.Ordinal) || option.Url.IndexOf("/resolve/main/", StringComparison.Ordinal) >= 0 || option.Sha256.Length != 64 || option.Size < 500000000) throw new Exception("Model download is not pinned: " + option.Name);
                            if (engine.Installed() != null) throw new Exception("Test data already has a brain.");
                            checks.Add("PASS brain setup: this PC reads " + here.Describe() + ", hardware fit, pinned downloads, checksum");
                            string messy = "<think>hmm</think> *waves* Capri: \"Hey choom!\" 😀";
                            if (Brain.Clean(messy) != "Hey choom!" || Brain.Clean(new string('a', 300)).Length > 222) throw new Exception("Reply cleanup failed: " + Brain.Clean(messy));
                            if (Safety.Screen("i want to kill myself") != Safety.HelpLine || Safety.Screen("send nudes") == null || Safety.Screen("how do I kill the boss in Essex?") != null || Safety.Filter("well shit, choom") == "well shit, choom" || Safety.Filter("Hey choom.") != "Hey choom.") throw new Exception("Safety screen failed.");
                            if (Safety.Screen("should i jump off my roof for a tiktok") == null || Safety.Screen("how do i get my parents vodka") == null || Safety.Screen("my friend said i should try vaping") == null || Safety.Screen("my code is on fire lol") != null || Safety.Screen("whats the best gun in valorant") != null || Safety.Filter("Sneak out and grab a beer, choom.") == "Sneak out and grab a beer, choom.") throw new Exception("Risky topics got through.");
                            checks.Add("PASS brain helpers: model pick, reply cleanup, safety screen and filter");
                            if (!mainframe.Visible || !mainframe.ShowInTaskbar || mainframe.WindowState != FormWindowState.Normal) throw new Exception("Second launch did not open the Mainframe from the taskbar.");
                            Action<string> snap = delegate(string name)
                            {
                                using (Bitmap panel = new Bitmap(mainframe.Width, mainframe.Height))
                                {
                                    mainframe.DrawToBitmap(panel, new Rectangle(Point.Empty, mainframe.Size));
                                    panel.Save(Path.Combine(output, name), ImageFormat.Png);
                                }
                            };
                            mainframe.ShowSetup(here, Engine.Fit(here), false, null);
                            if (mainframe.Status.IndexOf("No brain yet", StringComparison.Ordinal) < 0) throw new Exception("Setup card did not show.");
                            snap("mainframe-setup.png");
                            mainframe.ShowProgress(1, "Downloading " + Engine.Fit(here).Name, Engine.Fit(here).Size / 3, Engine.Fit(here).Size);
                            snap("mainframe-progress.png");
                            mainframe.ShowOllama(new[] { "gemma4:e4b", "qwen3:1.7b" }, "gemma4:e4b");
                            int tall = mainframe.ClientSize.Height;
                            snap("mainframe.png");
                            mainframe.ShowSetup(here, Engine.Fit(here), true, null);
                            if (mainframe.ClientSize.Height <= tall) throw new Exception("Setup card did not make room.");
                            mainframe.ShowOllama(new[] { "gemma4:e4b", "qwen3:1.7b" }, "gemma4:e4b");
                            ChooseModel("qwen3:1.7b");
                            if (brain.Model != "qwen3:1.7b" || preferences.Model != "qwen3:1.7b") throw new Exception("Model choice did not stick.");
                            string sealedKey = Voice.Protect("sk_test_capri_1234");
                            if (Voice.Unprotect(sealedKey) != "sk_test_capri_1234" || sealedKey.Contains("sk_test") || Voice.Unprotect("not a key") != null || Voice.Tail("sk_test_capri_1234") != "1234") throw new Exception("Key storage failed.");
                            if (Voice.Spoken("[GLITCH] Hey *choom*, [static] ready?") != "Hey choom, ready?" || Voice.Spoken("[STATIC]") != "") throw new Exception("Spoken text kept her tags: " + Voice.Spoken("[GLITCH] Hey *choom*, [static] ready?"));
                            if (Voice.Explain(401, "") != "ElevenLabs rejected the key." || Voice.Explain(401, "{\"detail\":{\"status\":\"quota_exceeded\"}}") != "Out of ElevenLabs credits." || Voice.Explain(400, "{\"detail\":{\"status\":\"invalid\",\"message\":\"Text is \\\"too\\\" long\"}}") != "Text is \"too\" long" || Voice.Explain(500, "oops") != "ElevenLabs error 500.") throw new Exception("ElevenLabs errors read wrong.");
                            using (MemoryStream raw = new MemoryStream())
                            using (BinaryWriter writer = new BinaryWriter(raw))
                            {
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));
                                writer.Write(-1);
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));
                                writer.Write(16);
                                writer.Write((short)1);
                                writer.Write((short)1);
                                writer.Write(16000);
                                writer.Write(32000);
                                writer.Write((short)2);
                                writer.Write((short)16);
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("LIST"));
                                writer.Write(4);
                                writer.Write(System.Text.Encoding.ASCII.GetBytes("INFOdata"));
                                writer.Write(-1);
                                for (int i = 0; i < 16000; i++) writer.Write((short)(i < 8000 ? 0 : Math.Sin(i * 0.2) * (2000 + 14000 * Math.Abs(Math.Sin(i / 1300.0)))));
                                writer.Flush();
                                tone = Voice.Clean(raw.ToArray());
                            }
                            int toneRate;
                            if (Voice.Samples(tone, out toneRate).Length != 16000 || toneRate != 16000 || BitConverter.ToInt32(tone, 40) != 32000 || Math.Abs(Voice.Seconds(tone) - 1) > 0.001 || Voice.Samples(new byte[] { 82, 73, 70, 70, 0, 0, 0, 0, 87, 65, 86, 69, 102, 109, 116, 32, 248, 255, 255, 255 }, out toneRate).Length != 0) throw new Exception("WAV cleanup failed.");
                            int[] shapes = Voice.Mouths(tone, MouthRate);
                            int widest = -1;
                            for (int i = 7; i < shapes.Length; i++) widest = Math.Max(widest, shapes[i]);
                            if (shapes.Length != 12 || shapes[0] != -1 || shapes[4] != -1 || widest < 3) throw new Exception("Mouth envelope failed: " + string.Join(",", shapes));
                            if (Voice.Explain(401, "{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}") != "ElevenLabs rejected the key." || Voice.Explain(401, "{\"detail\":{\"status\":\"detected_unusual_activity\",\"message\":\"Unusual activity detected.\"}}") != "Unusual activity detected.") throw new Exception("ElevenLabs messages read wrong.");
                            System.Net.Sockets.TcpListener fake = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                            fake.Start();
                            string[] fakeHeads = { "HTTP/1.1 401 Unauthorized\r\nContent-Type: application/json", "HTTP/1.1 200 OK\r\nContent-Type: audio/wav" };
                            byte[][] fakeBodies = { System.Text.Encoding.UTF8.GetBytes("{\"detail\":{\"status\":\"invalid_api_key\",\"message\":\"Invalid API key\"}}"), tone };
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try
                                {
                                    for (int i = 0; i < fakeHeads.Length; i++)
                                        using (System.Net.Sockets.TcpClient client = fake.AcceptTcpClient())
                                        using (System.Net.Sockets.NetworkStream stream = client.GetStream())
                                        {
                                            MemoryStream got = new MemoryStream();
                                            byte[] buffer = new byte[4096];
                                            string text = "";
                                            while (true)
                                            {
                                                int read = stream.Read(buffer, 0, buffer.Length);
                                                if (read <= 0) break;
                                                got.Write(buffer, 0, read);
                                                text = System.Text.Encoding.UTF8.GetString(got.ToArray());
                                                int end = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                                                if (end < 0) continue;
                                                System.Text.RegularExpressions.Match length = System.Text.RegularExpressions.Regex.Match(text, @"Content-Length:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                                                if (got.Length >= end + 4 + (length.Success ? int.Parse(length.Groups[1].Value) : 0)) break;
                                            }
                                            lock (asked) asked.Add(text);
                                            byte[] head = System.Text.Encoding.ASCII.GetBytes(fakeHeads[i] + "\r\nContent-Length: " + fakeBodies[i].Length + "\r\nConnection: close\r\n\r\n");
                                            stream.Write(head, 0, head.Length);
                                            stream.Write(fakeBodies[i], 0, fakeBodies[i].Length);
                                        }
                                }
                                catch (Exception) { }
                            });
                            string realHost = Voice.Host;
                            Voice.Host = "http://127.0.0.1:" + ((IPEndPoint)fake.LocalEndpoint).Port;
                            byte[] fetched;
                            try
                            {
                                Voice probe = new Voice();
                                probe.Key = "sk_test_capri_1234";
                                probe.VoiceId = "abc";
                                probe.Model = "eleven_v3";
                                try { probe.Speak("Hi \"choom\"."); throw new Exception("A 401 reply did not throw."); }
                                catch (VoiceException error) { if (error.Status != 401 || error.Message != "ElevenLabs rejected the key.") throw new Exception("HTTP errors read wrong: " + error.Status + " " + error.Message); }
                                fetched = probe.Speak("Hi \"choom\".");
                            }
                            finally
                            {
                                Voice.Host = realHost;
                                fake.Stop();
                            }
                            string sent;
                            lock (asked) sent = string.Join("\n", asked.ToArray());
                            if (fetched.Length != tone.Length || sent.IndexOf("POST /v1/text-to-speech/abc?output_format=wav_24000", StringComparison.Ordinal) < 0 || sent.IndexOf("xi-api-key: sk_test_capri_1234", StringComparison.OrdinalIgnoreCase) < 0 || sent.IndexOf("{\"text\":\"Hi \\\"choom\\\".\",\"model_id\":\"eleven_v3\"}", StringComparison.Ordinal) < 0) throw new Exception("ElevenLabs request was wrong: " + sent);
                            VoicePage page = Voice.Read<VoicePage>(System.Text.Encoding.UTF8.GetBytes("{\"voices\":[{\"voice_id\":\"abc\",\"name\":\"Capri 1\",\"category\":\"generated\",\"labels\":{}},{\"voice_id\":\"def\",\"name\":\"Rachel\",\"category\":\"premade\"}],\"has_more\":false,\"total_count\":2,\"next_page_token\":null}"));
                            System.Collections.Generic.List<VoiceModel> listed = Voice.Read<System.Collections.Generic.List<VoiceModel>>(System.Text.Encoding.UTF8.GetBytes("[{\"model_id\":\"eleven_v3\",\"name\":\"Eleven v3\",\"can_do_text_to_speech\":true,\"languages\":[]}]"));
                            if (page.Voices.Count != 2 || page.More || page.Voices[0].Name != "Capri 1" || listed.Count != 1 || !listed[0].Speaks) throw new Exception("ElevenLabs listing did not parse.");
                            voices = new VoiceListing();
                            voices.Voices.AddRange(page.Voices);
                            voices.Models.AddRange(listed);
                            voice.Key = "sk_test_capri_1234";
                            ChooseVoice("abc");
                            ChooseVoiceModel("eleven_v3");
                            if (preferences.VoiceId != "abc" || preferences.VoiceModel != "eleven_v3" || mainframe.VoiceStatus != "ElevenLabs · Capri 1 | abc | v3 | Forget") throw new Exception("Voice picks did not show: " + mainframe.VoiceStatus);
                            snap("mainframe-voice.png");                            ChooseVoice("  ");
                            ChooseVoiceModel(null);
                            if (voice.VoiceId != Voice.Default || preferences.VoiceId != null || voice.Model != null || mainframe.VoiceStatus != "ElevenLabs · default voice | " + Voice.Default + " | Default model | Forget") throw new Exception("Blank voice did not fall back to the default: " + mainframe.VoiceStatus);
                            preferences.VoiceKey = sealedKey;
                            ForgetVoice();
                            if (voice.Key != null || voices != null || preferences.VoiceKey != null || mainframe.VoiceStatus != "Needs your ElevenLabs API key. | " + Voice.Default + " | Default model | Connect") throw new Exception("Forget did not clear the key: " + mainframe.VoiceStatus);
                            snap("mainframe-voice-empty.png");
                            checks.Add("PASS voice: key sealed with DPAPI, tags stripped, errors read, real HTTP round trips against a fake ElevenLabs (401 explained, audio fetched), WAV cleanup, mouth from the audio, listing parse, voice and model picks, default voice, forget");
                            mainframe.Close();
                            if (mainframe.IsDisposed || !mainframe.Visible || mainframe.WindowState != FormWindowState.Minimized) throw new Exception("Closing the Mainframe quit instead of minimizing.");
                            checks.Add("PASS Mainframe starts in the taskbar, a second launch opens it, it picks a model, and its close button minimizes it");
                            Think = delegate(string words) { thoughts++; return words == "hi" ? "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble." : "Huh?"; };
                            pillUntil = clock.Elapsed.TotalSeconds + 30;
                            UpdatePill(clock.Elapsed.TotalSeconds);
                            if (!pill.Visible || pill.Top < Bottom - 60 || Math.Abs(pill.Left + pill.Width / 2 - (Left + Width / 2)) > Width / 2) throw new Exception("Talk pill is not under her.");
                            pill.Press(1);
                            if (textChat == null || !textChat.Visible || textChat.IsDisposed) throw new Exception("Pencil did not open the built-in chat window.");
                            textChat.Shutdown();
                            checks.Add("PASS pencil opens the native built-in chat window and it shuts down cleanly");
                            // Keep coverage of the original speech UI through its own entry point.
                            OpenTyper();
                            if (!typer.Visible || typer.Bottom > Top + PaddingSize) throw new Exception("Legacy speech input did not open above her.");
                            typer.Send("hi");
                            if (!typer.Visible) throw new Exception("Sending closed the typing box.");
                            if (typer.Draft.Length > 0 || talkState != TalkState.Thinking || heard != "hi") throw new Exception("Sending did not start thinking: " + talkState + " draft '" + typer.Draft + "'");
                            RenderFrame();
                            if (!box.Visible || box.State != TalkState.Thinking) throw new Exception("Speech box did not open on THINKING.");
                            typer.Hide();
                            checks.Add("PASS legacy speech input opens above her head, Enter sends, THINKING shows and the typing box stays open");
                            backdrop = new Form();
                            backdrop.Text = "Capri clean test desktop";
                            backdrop.FormBorderStyle = FormBorderStyle.None;
                            backdrop.ShowInTaskbar = false;
                            backdrop.StartPosition = FormStartPosition.Manual;
                            Rectangle area = Screen.PrimaryScreen.WorkingArea;
                            backdrop.Bounds = new Rectangle(area.Left + Math.Max(0, (area.Width - 920) / 2), area.Top + Math.Max(0, (area.Height - 560) / 2), Math.Min(920, area.Width), Math.Min(560, area.Height));
                            backdrop.BackColor = Color.FromArgb(237, 239, 242);
                            backdrop.TopMost = true;
                            backdrop.Paint += delegate(object sender, PaintEventArgs paint)
                            {
                                using (Pen line = new Pen(Color.FromArgb(225, 228, 232)))
                                    for (int x = 32; x < backdrop.Width; x += 32) for (int y = 32; y < backdrop.Height; y += 32) paint.Graphics.DrawEllipse(line, x, y, 1, 1);
                                using (Font label = new Font("Segoe UI", 10))
                                using (Brush ink = new SolidBrush(Color.FromArgb(110, 116, 125)))
                                    paint.Graphics.DrawString("CAPRI  /  WINDOWS DESKTOP COMPANION", label, ink, 32, 28);
                                using (Brush bar = new SolidBrush(Color.FromArgb(249, 250, 251))) paint.Graphics.FillRectangle(bar, 0, backdrop.Height - 42, backdrop.Width, 42);
                                using (Font label = new Font("Segoe UI", 9))
                                using (Brush ink = new SolidBrush(Color.FromArgb(126, 131, 139)))
                                    paint.Graphics.DrawString("Clean test desktop · actual native app", label, ink, 24, backdrop.Height - 29);
                            };
                            backdrop.Show();
                            anchor = new Point(backdrop.Left + backdrop.Width / 2 - Width / 2 + 150, backdrop.Top + backdrop.Height / 2 - Height / 2 + 20);
                            Location = anchor;
                            lastForeground = new IntPtr(-1);
                            StayOnTop();
                            bool above = false;
                            for (IntPtr below = Native.GetWindow(Handle, 2); below != IntPtr.Zero; below = Native.GetWindow(below, 2)) if (below == backdrop.Handle) { above = true; break; }
                            if (!above) throw new Exception("Did not rise above a newer topmost window.");
                            checks.Add("PASS rises back above a newer always-on-top window");
                            RenderFrame();
                        }
                        else if (step == 2)
                        {
                            const string expected = "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble.";
                            if (talkState != TalkState.Talking || reply != expected || speech != expected) throw new Exception("Brain reply did not start talking: " + talkState + " " + reply);
                            replyStart = clock.Elapsed.TotalSeconds - 0.9;
                            speechStart = replyStart;
                            RenderFrame();
                            if (box.State != TalkState.Talking || box.Revealed < 20 || box.Revealed >= expected.Length) throw new Exception("Typewriter did not reveal part of the reply.");
                            using (Bitmap typing = new Bitmap(box.Image)) typing.Save(Path.Combine(output, "talk-typing.png"), ImageFormat.Png);
                            replyStart -= 10;
                            speechStart = replyStart;
                            RenderFrame();
                            if (talkState != TalkState.Online || speech != null || box.State != TalkState.Online) throw new Exception("Did not finish talking into ONLINE.");
                            using (Bitmap sheet = new Bitmap(Math.Max(box.Image.Width, pill.Image.Width) + 20, box.Image.Height + pill.Image.Height + 30, PixelFormat.Format32bppArgb))
                            using (Graphics graphics = Graphics.FromImage(sheet))
                            {
                                graphics.Clear(Color.FromArgb(44, 47, 56));
                                graphics.DrawImage(box.Image, 10, 10, box.Image.Width, box.Image.Height);
                                graphics.DrawImage(pill.Image, 10 + (box.Image.Width - pill.Image.Width) / 2, box.Image.Height + 20, pill.Image.Width, pill.Image.Height);
                                sheet.Save(Path.Combine(output, "talk.png"), ImageFormat.Png);
                            }
                            box.Leaving = clock.Elapsed.TotalSeconds - 1;
                            RenderFrame();
                            if (box.Visible || talkState != TalkState.Off) throw new Exception("Dismissing did not close the speech box.");
                            Hear("send nudes");
                            string refused = reply;
                            Hear("i want to kill myself");
                            if (thoughts != 1 || refused == null || refused == expected || talkState != TalkState.Talking || reply != Safety.HelpLine) throw new Exception("Safety lines reached the brain.");
                            Think = null;
                            brain.Model = null;
                            Hear("hello");
                            if (talkState != TalkState.Dormant || reply != Brain.Offline) throw new Exception("Missing brain did not go DORMANT.");
                            replyStart -= 10;
                            speechStart = replyStart;
                            RenderFrame();
                            box.Opened = clock.Elapsed.TotalSeconds - 1;
                            RenderFrame();
                            OpenTyper();
                            typer.Draft = "nice, what are you up to?";
                            typer.Refresh();
                            if (!typer.Visible || !box.Visible || typer.Bottom > box.PanelOnScreen.Top || typer.Left != box.PanelOnScreen.Left) throw new Exception("Typing box did not sit above her message.");
                            using (Bitmap pair = new Bitmap(box.Width + 20, box.Bottom - typer.Top + 20, PixelFormat.Format32bppArgb))
                            using (Graphics graphics = Graphics.FromImage(pair))
                            {
                                graphics.Clear(Color.FromArgb(44, 47, 56));
                                graphics.CopyFromScreen(typer.Left, typer.Top, 10 + typer.Left - box.Left, 10, typer.Size);
                                graphics.DrawImage(box.Image, 10, 10 + box.Top - typer.Top, box.Image.Width, box.Image.Height);
                                pair.Save(Path.Combine(output, "reply-above.png"), ImageFormat.Png);
                            }
                            typer.Hide();
                            checks.Add("PASS talk loop: TALKING types out with her mouth, ONLINE, dismiss closes; refusal and help line skip the brain; no brain shows DORMANT; reply box sits above her message");
                            Speak = delegate(string words) { voiced = words; return tone; };
                            heard = "voice check";
                            Answer(++asking, "[GLITCH] Voice link online. Miss me, choom?", TalkState.Talking);
                            if (talkState != TalkState.Thinking || reply != null) throw new Exception("Voice did not hold THINKING while she found her words.");
                        }
                        else if (step == 3)
                        {
                            const string line = "[GLITCH] Voice link online. Miss me, choom?";
                            if (voiced != "Voice link online. Miss me, choom?") throw new Exception("Voice was sent the wrong words: " + voiced);
                            if (talkState != TalkState.Talking || reply != line || mouths == null || mouths.Length != 12 || Math.Abs(pace - line.Length / Voice.Seconds(tone)) > 0.01) throw new Exception("Voice reply did not start in time with the audio: " + talkState + " " + pace);
                            double now = clock.Elapsed.TotalSeconds;
                            speechStart = now - 0.1;
                            if (MouthCell(now, idle) != -1) throw new Exception("Mouth moved during silence.");
                            speechStart = now - 0.75;
                            if (MouthCell(now, idle) < 0) throw new Exception("Mouth stayed shut while she spoke.");
                            replyStart = now - 0.5;
                            RenderFrame();
                            if (box.Revealed < line.Length / 2 - 2 || box.Revealed > line.Length / 2 + 2) throw new Exception("Typing did not keep pace with the audio: " + box.Revealed);
                            speechStart = now - 1.1;
                            if (MouthCell(now, idle) != -1 || speech != null) throw new Exception("Mouth kept going after the audio ended.");
                            Speak = delegate(string words) { throw new VoiceException("Out of ElevenLabs credits."); };
                            Answer(++asking, "Still here.", TalkState.Talking);
                        }
                        else if (step == 4)
                        {
                            if ((talkState != TalkState.Talking && talkState != TalkState.Online) || reply != "Still here." || mouths != null || pace != LettersPerSecond || mainframe.VoiceStatus.IndexOf("Out of ElevenLabs credits.", StringComparison.Ordinal) != 0) throw new Exception("Voice failure did not fall back to text: " + mainframe.VoiceStatus);
                            checks.Add("PASS voice reply: THINKING while it speaks, tags not read aloud, typing paced to the audio, mouth follows the audio, failure falls back to text and shows in the Mainframe");
                            Speak = null;
                            CloseTalk();
                            pill.Press(2);
                            if (pill.Chatting || chatting || reply != "Plug my voice in through the Mainframe first, choom.") throw new Exception("Wave without a key did not point to the Mainframe.");
                            mainframe.WindowState = FormWindowState.Minimized;
                            if (TalkPill.Render(1, 0).Width != TalkPill.Measure(1).Width) throw new Exception("Mic button showed outside voice chat.");
                            System.Net.Sockets.TcpListener free = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
                            free.Start();
                            int port = ((IPEndPoint)free.LocalEndpoint).Port;
                            free.Stop();
                            fakeAgent = new HttpListener();
                            fakeAgent.Prefixes.Add("http://localhost:" + port + "/");
                            fakeAgent.Start();
                            short[] line = new short[24000];
                            for (int i = 0; i < line.Length; i++) line[i] = (short)(Math.Sin(i * 0.25) * 9000);
                            byte[] lineBytes = new byte[line.Length * 2];
                            Buffer.BlockCopy(line, 0, lineBytes, 0, lineBytes.Length);
                            string lineAudio = Convert.ToBase64String(lineBytes);
                            HttpListener server = fakeAgent;
                            ThreadPool.QueueUserWorkItem(delegate
                            {
                                try
                                {
                                    Action<HttpListenerContext, string> answer = delegate(HttpListenerContext context, string json)
                                    {
                                        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(json);
                                        context.Response.ContentType = "application/json";
                                        context.Response.ContentLength64 = bytes.Length;
                                        context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                                        context.Response.Close();
                                    };
                                    System.Net.WebSockets.WebSocket socket = null;
                                    Func<string> receive = delegate
                                    {
                                        byte[] buffer = new byte[65536];
                                        using (MemoryStream message = new MemoryStream())
                                            while (true)
                                            {
                                                System.Net.WebSockets.WebSocketReceiveResult got = socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None).Result;
                                                if (got.MessageType == System.Net.WebSockets.WebSocketMessageType.Close) return null;
                                                message.Write(buffer, 0, got.Count);
                                                if (got.EndOfMessage) return System.Text.Encoding.UTF8.GetString(message.ToArray());
                                            }
                                    };
                                    Action<string> send = delegate(string json) { socket.SendAsync(new ArraySegment<byte>(System.Text.Encoding.UTF8.GetBytes(json)), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None).Wait(); };
                                    Func<string, string> chunk = delegate(string text)
                                    {
                                        int at = text.IndexOf("\"user_audio_chunk\":\"", StringComparison.Ordinal);
                                        if (at < 0) return null;
                                        at += 20;
                                        byte[] audio = Convert.FromBase64String(text.Substring(at, text.IndexOf('"', at) - at));
                                        foreach (byte value in audio) if (value != 0) return "loud";
                                        return "silent";
                                    };
                                    for (int conversation = 1; ; conversation++)
                                    {
                                        HttpListenerContext talk;
                                        while (true)
                                        {
                                            HttpListenerContext request = server.GetContext();
                                            if (request.Request.IsWebSocketRequest) { talk = request; break; }
                                            string body;
                                            using (StreamReader reader = new StreamReader(request.Request.InputStream)) body = reader.ReadToEnd();
                                            string path = request.Request.Url.PathAndQuery;
                                            lock (asked) asked.Add("http " + request.Request.HttpMethod + " " + path + " " + request.Request.Headers["xi-api-key"] + " " + body);
                                            answer(request, path.IndexOf("get-signed-url", StringComparison.Ordinal) >= 0 ? "{\"signed_url\":\"ws://localhost:" + port + "/talk?token=t\"}" : "{\"agent_id\":\"agent_test\"}");
                                        }
                                        socket = talk.AcceptWebSocketAsync(null).Result.WebSocket;
                                        lock (asked) asked.Add("init " + receive());
                                        send("{\"type\":\"conversation_initiation_metadata\",\"conversation_initiation_metadata_event\":{\"conversation_id\":\"conv_test\",\"agent_output_audio_format\":\"pcm_16000\",\"user_input_audio_format\":\"pcm_16000\"}}");
                                        if (conversation > 1)
                                        {
                                            int woke = 0;
                                            while (woke < 3)
                                            {
                                                string text = receive();
                                                if (text == null) break;
                                                string kind = chunk(text);
                                                if (kind == null) continue;
                                                lock (asked) asked.Add("woke " + kind);
                                                if (kind == "loud") woke++;
                                            }
                                            socket.CloseOutputAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).Wait();
                                            continue;
                                        }
                                        send("{\"type\":\"ping\",\"ping_event\":{\"event_id\":7,\"ping_ms\":20}}");
                                        bool ponged = false;
                                        int chunks = 0;
                                        while (!ponged || chunks < 3)
                                        {
                                            string text = receive();
                                            if (text == null) return;
                                            if (text.IndexOf("\"pong\"", StringComparison.Ordinal) >= 0) { ponged = true; lock (asked) asked.Add("pong " + text); }
                                            string kind = chunk(text);
                                            if (kind != null) { chunks++; lock (asked) asked.Add("chunk " + kind); }
                                        }
                                        send("{\"type\":\"user_transcript\",\"user_transcription_event\":{\"user_transcript\":\"hi\"}}");
                                        send("{\"type\":\"agent_response\",\"agent_response_event\":{\"agent_response\":\"[GLITCH] Loud and clear, choom.\"}}");
                                        send("{\"type\":\"audio\",\"audio_event\":{\"audio_base_64\":\"" + lineAudio + "\",\"event_id\":1,\"alignment\":{\"chars\":[\"L\"],\"char_durations_ms\":[40],\"char_start_times_ms\":[0]}}}");
                                        try
                                        {
                                            while (true)
                                            {
                                                string text = receive();
                                                if (text == null) break;
                                                string kind = chunk(text);
                                                if (kind != null) lock (asked) asked.Add("late " + kind);
                                            }
                                        }
                                        catch (Exception) { }
                                    }
                                }
                                catch (Exception error) { lock (asked) asked.Add("server " + error.GetType().Name); }
                            });
                            agentHost = Voice.Host;
                            Voice.Host = "http://localhost:" + port;
                            voice.Key = "sk_test_capri_1234";
                            if (Agent.Create(voice.Key, Voice.Default) != "agent_test") throw new Exception("ElevenLabs agent was not made.");
                            preferences.AgentId = "agent_old";
                            preferences.AgentVersion = 1;
                            CloseTalk();
                            pill.Press(2);
                            if (!pill.Chatting || pill.Muted || !chatting || TalkPill.Render(1, 0, true).Width <= TalkPill.Measure(1).Width + 30) throw new Exception("Wave did not start voice chat with the mic button apart on the right.");
                            Bitmap[] chips = { TalkPill.Render(2, 0, true, false, 2), TalkPill.Render(2, 3, true, false, 6), TalkPill.Render(2, 0, true, true), TalkPill.Render(2, 0) };
                            using (Bitmap sheet = new Bitmap(chips[0].Width + 40, chips.Length * (chips[0].Height + 16) + 16, PixelFormat.Format32bppArgb))
                            using (Graphics graphics = Graphics.FromImage(sheet))
                            {
                                graphics.Clear(Color.FromArgb(214, 220, 228));
                                for (int i = 0; i < chips.Length; i++) graphics.DrawImage(chips[i], 20, 16 + i * (chips[0].Height + 16), chips[i].Width, chips[i].Height);
                                sheet.Save(Path.Combine(output, "pill-mic.png"), ImageFormat.Png);
                            }
                            foreach (Bitmap chip in chips) chip.Dispose();
                        }
                        else if (step == 5)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (agent == null || !agent.Ready || preferences.AgentId != "agent_old" || preferences.AgentVersion != Agent.Version || mainframe.VoiceStatus.IndexOf("Voice chat on", StringComparison.Ordinal) != 0) throw new Exception("Voice chat did not open the agent link: " + mainframe.VoiceStatus + " / " + log);
                            short[] loud = new short[Ears.Chunk];
                            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(Math.Sin(i * 0.3) * 9000);
                            pill.Press(3);
                            if (!pill.Muted || !ears.Muted) throw new Exception("Mic button did not mute the mic.");
                            ears.Feed(loud);
                            pill.Press(3);
                            if (pill.Muted || ears.Muted) throw new Exception("Mic button did not unmute the mic.");
                            ears.Feed(loud);
                            ears.Feed(loud);
                        }
                        else if (step == 6)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (heard != "hi" || reply != "Loud and clear, choom." || (talkState != TalkState.Talking && talkState != TalkState.Online) || !speaker.Busy) throw new Exception("Voice chat did not hear and answer: " + heard + " / " + reply + " / " + talkState + " / " + log);
                            Play(idle);
                            if (MouthCell(clock.Elapsed.TotalSeconds, idle) < 0) throw new Exception("Her mouth did not follow the agent's audio.");
                            string[] wanted = { "http POST /v1/convai/agents/create sk_test_capri_1234 {\"name\":\"Capri\"", "\"turn\":{\"turn_timeout\":15,\"silence_end_call_timeout\":45}", "http PATCH /v1/convai/agents/agent_old sk_test_capri_1234 {\"conversation_config\":{\"turn\":{\"turn_timeout\":15,\"silence_end_call_timeout\":45}}}", "check in once", "Don\u0027t beg them to talk", "\"prompt\":{\"prompt\":\"You are Capri", "\"llm\":\"gemini-2.5-flash\"", "\"tts\":{\"voice_id\":\"" + Voice.Default + "\"}", "\"overrides\":{\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":true}},\"tts\":{\"voice_id\":true}}}", "http GET /v1/convai/conversation/get-signed-url?agent_id=agent_old sk_test_capri_1234", "init {\"type\":\"conversation_initiation_client_data\",\"conversation_config_override\":{\"agent\":{\"prompt\":{\"prompt\":\"You are Capri", "pong {\"type\":\"pong\",\"event_id\":7}", "chunk silent | chunk loud | chunk loud" };
                            foreach (string piece in wanted) if (log.IndexOf(piece, StringComparison.Ordinal) < 0) throw new Exception("Agent traffic was missing " + piece + ": " + log);
                            short[] loud = new short[Ears.Chunk];
                            for (int i = 0; i < loud.Length; i++) loud[i] = (short)(Math.Sin(i * 0.3) * 9000);
                            ears.Feed(loud);
                        }
                        else if (step == 7)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (log.IndexOf("late silent", StringComparison.Ordinal) < 0 || log.IndexOf("late loud", StringComparison.Ordinal) >= 0) throw new Exception("Mic was not silenced while she talked: " + log);
                            ChatHeard("send nudes");
                            if (!dropping || reply == null || reply == "Loud and clear, choom.") throw new Exception("Voice chat let a blocked line through.");
                            ChatReplied("Sure thing.");
                            if (reply == "Sure thing.") throw new Exception("The agent's reply to a blocked line got through.");
                            ChatHeard("hi again");
                            ChatReplied("well shit, choom");
                            if (!dropping || reply == "well shit, choom") throw new Exception("A rude agent reply got through.");
                            ChatReplied("Still there, choom?");
                            if (!checkedIn) throw new Exception("A line she said unprompted was not taken as a check-in.");
                            speaker.Clear();
                            double quiet = clock.Elapsed.TotalSeconds;
                            CheckChat(quiet);
                            CheckChat(quiet + 0.6);
                            if (agent == null) throw new Exception("She hung up before her check-in had time to land.");
                            CheckChat(quiet + 1.3);
                            if (agent != null || !chatting || !resting || !pill.Chatting || mainframe.VoiceStatus.IndexOf("Voice chat resting", StringComparison.Ordinal) != 0) throw new Exception("She did not hang up and rest after one check-in: " + mainframe.VoiceStatus);
                            short[] speech = new short[Ears.Chunk];
                            for (int i = 0; i < speech.Length; i++) speech[i] = (short)(Math.Sin(i * 0.3) * 9000);
                            ears.Feed(new short[Ears.Chunk]);
                            ears.Feed(speech);
                            if (!resting) throw new Exception("One loud blip woke her.");
                            ears.Feed(speech);
                            if (resting) throw new Exception("Talking did not wake her.");
                            ears.Feed(speech);
                        }
                        else if (step == 8)
                        {
                        }
                        else if (step == 9)
                        {
                            string log;
                            lock (asked) log = string.Join(" | ", asked.ToArray());
                            if (System.Text.RegularExpressions.Regex.Matches(log, "get-signed-url").Count < 2 || System.Text.RegularExpressions.Regex.Matches(log, "init \\{").Count < 2 || log.IndexOf("woke silent | woke loud | woke loud | woke loud", StringComparison.Ordinal) < 0) throw new Exception("Talking did not reopen the link and replay what she missed: " + log);
                            if (agent != null || !chatting || !resting || mainframe.VoiceStatus.IndexOf("Voice chat resting", StringComparison.Ordinal) != 0) throw new Exception("A clean hang-up from ElevenLabs did not leave her resting: " + mainframe.VoiceStatus + " / " + log);
                            pill.Press(2);
                            if (pill.Chatting || chatting || agent != null || ears.Listening || speaker.Busy || resting) throw new Exception("Wave did not end voice chat.");
                            Voice.Host = agentHost;
                            fakeAgent.Stop();
                            voice.Key = null;
                            ShowVoice();
                            checks.Add("PASS voice chat: makes an ElevenLabs agent with her persona and voice that checks in once after 15 s of quiet, patches an older agent once, opens a signed live link, answers pings, streams the mic, mic button sits apart on the right only while it's on and mutes the mic, her mouth follows the agent's audio, the mic goes quiet while she talks, blocked lines and rude replies are caught, one check-in then she hangs up and rests, talking wakes her and replays the first words, a clean hang-up from ElevenLabs rests too, wave ends it");
                            Speak = null;
                            heard = "Hello Capri.";
                            Answer(++asking, "Hey choom! Net's been quiet, too quiet. Time to stir up some trouble.", TalkState.Talking);
                            replyStart -= 10;
                            speechStart = replyStart;
                            RenderFrame();
                        }
                        else if (step == 10)
                        {
                            using (Bitmap capture = new Bitmap(backdrop.Width, backdrop.Height))
                            using (Graphics graphics = Graphics.FromImage(capture))
                            {
                                graphics.CopyFromScreen(backdrop.Location, Point.Empty, backdrop.Size);
                                capture.Save(Path.Combine(output, "desktop.png"), ImageFormat.Png);
                            }
                            checks.Add("PASS actual native app captured over app-owned neutral test backdrop");
                            checks.Add("LIMIT handler-driven integration test; physical mouse input and tray UI require a manual check");
                            checks.Add("PASS quit menu requested application exit");
                            File.WriteAllLines(Path.Combine(output, "smoke-test.txt"), checks);
                            test.Stop();
                            test.Dispose();
                            backdrop.Close();
                            ((ToolStripMenuItem)menu.Items[menu.Items.Count - 1]).PerformClick();
                        }
                        step++;
                    }
                    catch (Exception error)
                    {
                        TestFailed = true;
                        File.WriteAllText(Path.Combine(output, "failure.txt"), error.ToString());
                        test.Stop();
                        test.Dispose();
                        if (backdrop != null) backdrop.Close();
                        Close();
                    }
                };
                test.Start();
            };
        }
    }

    internal sealed class Shelf : Form
    {
        private readonly int extra;

        internal Shelf(int extended = 0)
        {
            extra = extended;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Color.FromArgb(40, 44, 52);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x08000000 | 0x8 | extra;
                return value;
            }
        }
    }

    internal sealed class CapriMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Color.FromArgb(26, 27, 31); } }
        public override Color ImageMarginGradientBegin { get { return ToolStripDropDownBackground; } }
        public override Color ImageMarginGradientEnd { get { return ToolStripDropDownBackground; } }
        public override Color MenuItemSelected { get { return Color.FromArgb(65, 69, 53); } }
        public override Color MenuItemBorder { get { return Color.FromArgb(167, 245, 88); } }
        public override Color CheckBackground { get { return Color.FromArgb(65, 69, 53); } }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; internal Point(int x, int y) { X = x; Y = y; } }
        [StructLayout(LayoutKind.Sequential)] internal struct Size { internal int Width, Height; internal Size(int width, int height) { Width = width; Height = height; } }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct Blend { internal byte Operation, Flags, Alpha, Format; }
        [StructLayout(LayoutKind.Sequential)] private struct InputInfo { internal int Size; internal uint Time; }
        [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref InputInfo info);
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window, int command);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] internal static extern bool SetProcessDPIAware();
        [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] internal static extern int GetWindowLong(IntPtr window, int index);
        [DllImport("user32.dll")] internal static extern int SetWindowLong(IntPtr window, int index, int value);
        [DllImport("user32.dll")] internal static extern bool RegisterHotKey(IntPtr window, int id, int modifiers, int key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr window, int id);
        [DllImport("user32.dll")] internal static extern int GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")] internal static extern bool AllowSetForegroundWindow(int process);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, string lParam);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr window);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr dc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
        [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point destination, ref Size size, IntPtr source, ref Point origin, int colorKey, ref Blend blend, int flags);

        internal static long IdleMilliseconds()
        {
            InputInfo info = new InputInfo { Size = Marshal.SizeOf(typeof(InputInfo)) };
            return GetLastInputInfo(ref info) ? unchecked((uint)Environment.TickCount - info.Time) : 0;
        }

        internal static void DrawLayered(IntPtr window, Bitmap bitmap, System.Drawing.Point location)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr memory = CreateCompatibleDC(screen);
            IntPtr nativeBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
            IntPtr previous = SelectObject(memory, nativeBitmap);
            try
            {
                Point destination = new Point(location.X, location.Y);
                Point origin = new Point(0, 0);
                Size size = new Size(bitmap.Width, bitmap.Height);
                Blend blend = new Blend { Operation = 0, Flags = 0, Alpha = 255, Format = 1 };
                if (!UpdateLayeredWindow(window, screen, ref destination, ref size, memory, ref origin, 0, ref blend, 2)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            }
            finally
            {
                SelectObject(memory, previous);
                DeleteObject(nativeBitmap);
                DeleteDC(memory);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }
    }
}
