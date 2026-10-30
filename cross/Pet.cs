using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace CapriDesktop
{
    [DataContract]
    internal sealed class Settings
    {
        [DataMember] internal int X;
        [DataMember] internal int Y;
        [DataMember] internal bool Placed;
        [DataMember] internal double Size = 240;
        [DataMember] internal string Model;
        [DataMember] internal string Language;
        [DataMember] internal string Nickname;
        [DataMember] internal int Replies;
        [DataMember] internal int Recall;
        [DataMember] internal int Wander;
        [DataMember] internal bool NoDance;
        [DataMember] internal int? Sounds;
        [DataMember] internal string VoiceId;
        [DataMember] internal bool Silent;
        [DataMember] internal int Typing;
        [DataMember] internal bool NoDiscord;
        [DataMember] internal string CloudModel;
        [DataMember] internal string UpdateSeen;
        [DataMember] internal bool StayOnModel;
        [DataMember] internal bool NoMemories;
        [DataMember] internal bool NoCheckIns;
        [DataMember] internal int? PopPace;
        [DataMember] internal bool Watch;
        [DataMember] internal int GlanceGap;
        [DataMember] internal bool NoCaughtUp;
        [DataMember] internal int? SleepAfter;
        [DataMember] internal int? Bubble;
        [DataMember] internal int? Volume;
        [DataMember] internal int Pitch;
        [DataMember] internal int? Pace;
        [DataMember] internal int Robot;
        [DataMember] internal int Glitch;
        // Her voice layers, like Windows: off out of the box and in settings from before them, which get the layers' defaults.
        [DataMember] internal bool Layers;
        [DataMember] internal double? LayerPitch1;
        [DataMember] internal int? LayerDelay1;
        [DataMember] internal double? LayerPitch2;
        [DataMember] internal int? LayerDelay2;
        [DataMember] internal bool NoIntro;
        [DataMember] internal bool NotOnTop;
        // Security's toggles 1 to 10 as 1s and 0s, like Windows. Missing in settings from before it, which get Trusted.
        [DataMember] internal string Permissions;
    }

    // The bits of GitHub's latest release she reads to tell them about an update.
    [DataContract]
    internal sealed class Release
    {
        [DataMember(Name = "tag_name")] internal string Tag;
        [DataMember(Name = "html_url")] internal string Page;
        [DataMember(Name = "draft")] internal bool Draft;
        [DataMember(Name = "prerelease")] internal bool Prerelease;
    }

    internal sealed class Clip
    {
        internal readonly Bitmap Strip;
        internal readonly int Count;
        internal readonly double Fps;
        internal readonly double Height;
        // How far a cell's middle sits from the middle of her square, as a share of her size. Narrow cells cut to the art need it.
        internal readonly double ShiftX, ShiftY;

        internal Clip(string name, int count, double fps, double height, double shiftX = 0, double shiftY = 0)
        {
            Count = count;
            Fps = fps;
            Height = height;
            ShiftX = shiftX;
            ShiftY = shiftY;
            Stream stream = typeof(Clip).Assembly.GetManifestResourceStream("Capri.Sprite." + name + ".png");
            if (stream == null) throw new FileNotFoundException("Capri's " + name + " animation is missing.");
            using (stream) Strip = new Bitmap(stream);
        }

        internal bool Even { get { return Strip.PixelSize.Width % Count == 0; } }
        internal int Wide { get { return Strip.PixelSize.Width / Count; } }

        internal bool Solid(int cell, int x, int y, int radius)
        {
            int left = cell * Wide + Math.Max(0, x - radius);
            int right = cell * Wide + Math.Min(Wide, x + radius + 1);
            int top = Math.Max(0, y - radius);
            int bottom = Math.Min(Strip.PixelSize.Height, y + radius + 1);
            if (right <= left || bottom <= top) return false;
            int wide = right - left;
            byte[] pixels = new byte[wide * (bottom - top) * 4];
            GCHandle pin = GCHandle.Alloc(pixels, GCHandleType.Pinned);
            try { Strip.CopyPixels(new PixelRect(left, top, wide, bottom - top), pin.AddrOfPinnedObject(), pixels.Length, wide * 4); }
            finally { pin.Free(); }
            for (int i = 3; i < pixels.Length; i += 4) if (pixels[i] > 24) return true;
            return false;
        }
    }

    internal sealed class Body : Control
    {
        internal Clip Shown;
        internal int Cell;
        internal Clip Mouth;
        internal int Talking = -1;
        internal double Size = 240;
        internal double Breathe;
        internal double Tilt;
        internal Point Centre;

        internal Rect Place(Clip shown)
        {
            double tall = Size * shown.Height;
            double wide = tall * shown.Wide / shown.Strip.PixelSize.Height;
            return new Rect(Centre.X - wide / 2 + shown.ShiftX * Size, Centre.Y - tall / 2 + shown.ShiftY * Size, wide, tall);
        }

        public override void Render(DrawingContext context)
        {
            Clip shown = Shown;
            if (shown == null) return;
            using (context.PushTransform(Matrix.CreateTranslation(-Centre.X, -Centre.Y) * Matrix.CreateRotation(Tilt * Math.PI / 180) * Matrix.CreateTranslation(Centre.X, Centre.Y + Breathe)))
            {
                Rect place = Place(shown);
                context.DrawImage(shown.Strip, new Rect(Cell * shown.Wide, 0, shown.Wide, shown.Strip.PixelSize.Height), place);
                if (Talking >= 0 && Mouth != null) context.DrawImage(Mouth.Strip, new Rect(Talking * Mouth.Wide, 0, Mouth.Wide, Mouth.Strip.PixelSize.Height), place);
            }
        }
    }

    internal sealed class Pet : Window
    {
        internal const double SitX = 0.30;
        internal const double SitY = 0.79;
        internal const double PerchLeft = 0.21;
        internal const double PerchTop = 0.17;
        internal const double PerchRight = 0.69;
        private const int BlinkFrom = 9;
        // Her perch blinks where her artist drew it: loop frames 29 to 36 (index 28 on).
        private const int PerchBlinkFrom = 28;
        // her artist's perch: 60 frames at 30 fps in cells cut to her outline, 26 px left of her square's middle and 36 px down it in 384 px art.
        private const double PerchShiftX = -26.0 / 384, PerchShiftY = 36.0 / 384;
        private const double StandX = 0.35;
        private const double StandY = 0.99;
        private static readonly double[] WanderPaces = { 3, 1, 0.45, 0.18 };
        internal static readonly string[] WanderNames = { "Off", "Rarely", "Sometimes", "Often", "Restless" };
        private static readonly string[] Lengths = { "Short", "Medium", "Long" };
        private static readonly int[] Recalls = { 8, 16, 32 };
        private static readonly string[] SoundNames = { "Off", "Quiet", "Normal", "Loud" };
        private static readonly int[] SoundLevels = { 0, 30, 60, 100 };
        internal const string Offline = "My brain's offline. Install Ollama from ollama.com and open it, then right-click me > Brain > Download " + Starter + ".";
        // The brain she downloads for herself through Ollama when it has none yet, so no terminal is needed.
        internal const string Starter = "gemma4:e4b";
        internal const string PullChoice = "Download " + Starter + ", her brain (a few GB)";
        internal const string NoOllama = "Ollama isn't running. Install it from ollama.com and open it.";
        private const string FreeKeyPage = "https://build.nvidia.com/settings/api-keys";
        internal const string KeyChoice = "Use an API key...";
        internal const string NotAKey = "That doesn't look like an NVIDIA, OpenAI, Claude or DeepSeek key. NVIDIA's start with nvapi-.";
        internal const string FreeKeySteps = "Make a key on NVIDIA's page and copy it. Then right-click me > Brain > " + KeyChoice + " and paste it.";
        internal const string DiscordPage = "https://discord.gg/F4fDHfGtFk";
        internal const string HelpPage = "https://lachydotmcg.github.io/capri-desktop/help/";
        // Mac and Linux don't update themselves yet, so she checks GitHub's latest release now and then and offers its page.
        private const string LatestRelease = "https://api.github.com/repos/lachydotmcg/capri-desktop/releases/latest";
        private const string ReleasesPage = "https://github.com/lachydotmcg/capri-desktop/releases/latest";
        internal static readonly double[] Sizes = { 140, 170, 240, 320, 440, 560 };
        private static readonly string[] SizeNames = { "Tiny", "Small", "Medium", "Large", "Huge", "Giant" };
        internal readonly Brain Brain = new Brain();
        internal readonly Talk Talk;
        internal Desk Desk = Desk.Make();
        internal Func<string, string> Think;
        internal Settings Saved;
        private readonly IClassicDesktopStyleApplicationLifetime desktop;
        private readonly Clip idle = new Clip("idle", 16, 8, 1.11);
        private readonly Clip blink = new Clip("blink", 4, 8, 1.11);
        private readonly Clip blinkGlitch = new Clip("blink_glitch", 4, 8, 1.11);
        private readonly Clip glitch = new Clip("glitch", 10, 15, 1.11);
        private readonly Clip dangle = new Clip("dangle", 24, 10, 1.11);
        private readonly Clip perch = new Clip("perch", 60, 30, 1, PerchShiftX, PerchShiftY);
        // her artist's own blink, frames 29 to 36 of the blink set, and the same with a glitch around her eye.
        private readonly Clip perchBlink = new Clip("perch_blink", 8, 30, 1, PerchShiftX, PerchShiftY);
        private readonly Clip perchBlinkGlitch = new Clip("perch_blink_glitch", 8, 30, 1, PerchShiftX, PerchShiftY);
        private readonly Clip perchGlitch = new Clip("perch_glitch", 10, 15, 1, PerchShiftX, PerchShiftY);
        private readonly Clip mouth = new Clip("mouth", 5, 12, 1.11);
        private readonly Clip walk = new Clip("walk", 12, 12, 1.11);
        private readonly Clip dance = new Clip("dance", 16, 16, 1.11);
        private readonly Clip perchDance = new Clip("perch_music", 60, 30, 1, PerchShiftX, PerchShiftY);
        private readonly Clip arrive = new Clip("arrive", 14, 15, 1.11);
        private readonly Clip leave = new Clip("leave", 12, 15, 1.11);
        private readonly Clip perchArrive = new Clip("perch_arrive", 14, 15, 1, PerchShiftX, PerchShiftY);
        private readonly Clip perchLeave = new Clip("perch_leave", 12, 15, 1, PerchShiftX, PerchShiftY);
        private readonly Clip grin = new Clip("grin", 16, 8, 1.11);
        private readonly Clip sleep = new Clip("sleep", 16, 6, 1.11);
        private readonly Clip wake = new Clip("wake", 13, 15, 1.11);
        internal static readonly string[] PetLines =
        {
            "Heh. Okay, I'll allow it.", "Pets logged. Morale up twelve percent.", "Careful, choom. I bite viruses.",
            "Head pats? On my chrome? Bold.", "Okay, that's nice. Don't tell anyone.", "System status: very patted.",
            "You're gonna wear a groove in my firmware, choom.", "Easy on the hair. It took three patches to get it like this.",
            "Affection detected. Sending some back. Hi.", "Keep going. This beats defragging.", "Hehe. My cooling fans just kicked in.",
            "Ten out of ten pats. Would get patted again.", "Is this a bribe? Because it's working.", "Mmm. If I start glitch-purring, that's on you."
        };
        private readonly List<double> petTurns = new List<double>();
        private int petX;
        private int petWay;
        private double petAt = -60;
        private int petLine;
        private int petTicket;
        private readonly Body body = new Body();
        private readonly List<MenuItem> wanderPicks = new List<MenuItem>();
        private readonly List<MenuItem> languagePicks = new List<MenuItem>();
        private readonly List<MenuItem> replyPicks = new List<MenuItem>();
        private readonly List<MenuItem> recallPicks = new List<MenuItem>();
        private readonly List<MenuItem> soundPicks = new List<MenuItem>();
        private MenuItem danceItem;
        private MenuItem musicItem;
        private MenuItem introItem;
        private MenuItem brainMenu;
        internal Func<bool> MusicPlaying = Music.Playing;
        private bool musicOn;
        private bool musicBusy;
        private double musicAt;
        private bool walking;
        private int walkTo;
        private double walkStep;
        private double walkCarry;
        private bool dropAfter;
        private bool quitting;
        private bool away;
        private bool opened;
        private readonly Voice voice = new Voice();
        private VoiceListing voices;
        private string voiceProblem;
        private bool listing;
        private MenuItem voiceMenu;
        private MenuItem typingItem;
        private const int VoiceWait = 8000;
        private const string KeyHeard = "(voice key)";
        private const string NameHeard = "(nickname)";
        private const string BrainHeard = "(brain download)";
        private const string BrainKeyHeard = "(brain key)";
        // The models on their cloud key, read when the Brain menu opens.
        private List<string> cloudModels;
        private bool cloudListing;
        // "Playing Capri" on their Discord profile, like on Windows. Off in the smoke test.
        private readonly Presence presence;
        private MenuItem discordItem;
        private MenuItem securityMenu;
        private readonly List<MenuItem> securityPicks = new List<MenuItem>();
        private readonly List<MenuItem> securityToggles = new List<MenuItem>();
        private MenuItem memoriesItem;
        private double nextStatus;
        private MenuItem updateItem;
        private string updatePage;
        private double nextUpdate = 60;
        private bool updateChecking;
        // What she says about a new version once she's free.
        internal string UpdateNews;
        private bool pulling;
        private volatile int pulled = -1;
        private bool dancing;
        private Action outro;
        private double outroAt;
        private double nextWander = -1;
        // Check in on me, like on Windows and on by default: now and then she glitches in at the top of her screen to say hi,
        // even when hidden, and hides again afterwards if she was. Pop-ins sets how often.
        internal static readonly string[] PopLines = { "Boo. Just checking on you.", "Hi! Don't mind me.", "Peekaboo.", "Miss me? I missed you.", "Hey, you. Having fun?", "Surprise. I got bored without you." };
        internal static readonly string[] PopNames = { "Rarely", "Sometimes", "Often" };
        private static readonly double[,] PopPaces = { { 1200, 1200 }, { 480, 720 }, { 180, 300 } };
        private const string PopHeard = "(pops in)";
        private readonly List<MenuItem> popPicks = new List<MenuItem>();
        private MenuItem checkInsItem;
        private double nextPopIn = -1;
        private double popOut = -1;
        private int popAsked, popClicks, popPets;
        // Always on top, like Windows' and on by default. Off, she's an ordinary window, and coming out to say hi, asking for
        // attention or being shown brings her to the front until it's over or they touch her.
        private MenuItem onTopItem;
        private double raisedUntil = -1;
        private int raisedAsked, raisedClicks, raisedPets;
        private bool popChecking;
        // Cyberpunk 2077 starting, or a Night City song coming on, is home for her, like on Windows: she cheers once, then waits
        // two hours before cheering for the same again. Check in on me off stops it.
        internal const double HomeGap = 7200;
        internal static readonly string[] HomeGameLines = { "Night City?! You're taking me home, choom.", "Ooh, Cyberpunk. Go wreck some corpos for me.", "[SIGNAL CORRUPTED] ...wait. I know that skyline." };
        internal static readonly string[] HomeSongLines = { "Wait, that's Night City. Turn it up.", "Ooh, my soundtrack. Preem taste.", "Now that's music. You're learning." };
        private bool homeBusy, homeGame, homeSong;
        private double homeAt = -HomeGap, gameCheered = -HomeGap, songCheered = -HomeGap;
        // A computer stuck near full CPU for about a minute gets a joke from her, at most once every half hour and never over a
        // full-screen window, like on Windows (Zenni's idea).
        internal const double LagGap = 1800;
        internal static readonly string[] LagLines = { "Your CPU's cooking, choom. Careful, I might go cyberpsycho.", "Everything's lagging. One more tab and I go full cyberpsycho.", "[SIGNAL CORRUPTED] ...this lag is making me cyberpsycho. Close something." };
        private long lagIdle, lagTotal;
        private int lagHot;
        private double lagAt = -LagGap, lagSaid = -LagGap;
        // Asking for attention, like on Windows: with Check in on me on and nobody paying her any mind for about 2 hours while
        // they're at the computer, she comes over near the mouse and asks. Noticing her gets a happy line; 20 s of nothing gets a
        // sulk and twice the wait, up to 8 hours.
        internal const double NudgeGap = 7200;
        internal static readonly string[] NudgeLines = { "Psst. Remember me? Tiny, glitchy, very important.", "I'm bored. Entertain me.", "Hey. Hey. Look at me.", "You've been ignoring me for ages. Rude.", "Pay attention to me or I start clicking things.", "Knock knock. It's your favourite gremlin.", "Choom. I'm bored out of my many minds." };
        internal static readonly string[] NoticedLines = { "Finally. Hi.", "There you are.", "Knew you couldn't resist me." };
        internal static readonly string[] SnubbedLines = { "Fine. Ignore me. See if I care.", "Wow. Ignored. Noted.", "I'll just sit here. Alone. Bored." };
        private const string NudgeHeard = "(wants attention)";
        private double noticed = -1;
        private int noticedClicks, noticedAsked, noticedPets;
        private double nudgeGap = NudgeGap;
        private double nudgeOut = -1;
        private int nudgeAsked;
        private double nudgeLook;
        private bool nudgeChecking;
        private int clicks;
        // Watch my screen, like on Windows and off until ticked: every so often she takes a quiet screenshot and comments when
        // something's worth a line. Glance every sets how often; Keep her caught up keeps the glances she stays quiet about.
        internal static readonly int[] GlanceGaps = { 10, 15, 30, 60, 90, 120, 180, 300, 600 };
        internal static readonly string[] GlanceGapNames = { "10 s", "15 s", "30 s", "1 min", "1.5 min", "2 min", "3 min", "5 min", "10 min" };
        private readonly List<MenuItem> gapPicks = new List<MenuItem>();
        private MenuItem watchItem;
        private MenuItem caughtUpItem;
        private double nextGlance = -1;
        private double lastGlance = -1;
        private int watchTicket;
        private bool glancing;
        private bool watchTold;
        // The last glance failure written to errors.txt: one that keeps happening, like a text-only Ollama model's, goes in once.
        private string glanceFailed;
        private byte[] lastPrint;
        private readonly List<Tuple<double, string>> glanced = new List<Tuple<double, string>>();
        // Her brain's look at a glance, and how long they've been away from the computer. The smoke test hands in its own.
        internal Func<byte[], string, string> Seeing;
        internal Func<double> Idle = Limbs.IdleSeconds;
        // Tells the tray menu whether to offer Hide her or Show her, since a pop-in or Talk to her brings her out too.
        internal Action AwayChanged;
        private bool shushed;
        private MenuItem quietItem;
        // A nap they asked for, like on Windows: asked until she's said her line and stands free to doze off, then napping,
        // asleep through the mouse and keyboard until a click, a drag, talking to her or "wake up". Quiet meanwhile.
        private bool napAsked, napping;
        private MenuItem napItem;
        // Hang here, like on Windows: she dangles where she is until a click, a drag or "let go". A stop to her dance while music
        // plays holds her still until the music ends, and a hide they asked for waits for her line.
        private bool hanging, danceHeld, hideAsked;
        private MenuItem hangItem;
        private MenuItem screenItem;
        // Their mouse, which "teleport here" goes to. The smoke test hands in its own.
        internal Func<PixelPoint?> MouseAt;
        // Getting to know them, like on Windows: on a new install it starts once she's said hello, and Settings > Get to know me
        // starts it again.
        private readonly Meeting meeting = new Meeting();
        private double meetAt = -1;
        private readonly bool fresh;
        // Ticks the tray's Quiet item when her own menu changes it.
        internal Action ShushChanged;
        // Settings > Start at login. Off until they tick it; the file is the setting.
        internal readonly string LoginFile;
        private MenuItem loginItem;
        // Move > Fall asleep after, like Windows' Sleep slider: how long without the mouse or keyboard before she dozes off
        // where she stands, 0 for never. Where she was told the mouse was when she fell asleep, to wake when it moves.
        internal static readonly int[] Sleeps = { 0, 60, 120, 300, 600, 1800 };
        internal static readonly string[] SleepNames = { "Never", "1 min", "2 min", "5 min", "10 min", "30 min" };
        private readonly List<MenuItem> sleepPicks = new List<MenuItem>();
        private double nextSleepCheck;
        private bool sleepChecking;
        private double sleepIdle;
        private MenuItem sitItem;
        // Settings > Bubble, like Windows: how long her replies stay up.
        internal static readonly string[] BubbleNames = { "Quick", "Normal", "Long" };
        private readonly List<MenuItem> bubblePicks = new List<MenuItem>();
        // Settings > Volume, like Windows: her spoken voice, Mute to 150%. Her sound effects stay on Sounds.
        internal static readonly int[] Volumes = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 125, 150 };
        internal static readonly string[] VolumeNames = { "Mute", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%", "125%", "150%" };
        private readonly List<MenuItem> volumePicks = new List<MenuItem>();
        // Settings > Voice style, Windows' Pitch, Speed, Robot and Glitch sliders. They change her ElevenLabs voice here.
        internal static readonly string[] PitchNames = { "-6", "-5", "-4", "-3", "-2", "-1", "Normal", "+1", "+2", "+3", "+4", "+5", "+6" };
        internal static readonly int[] Paces = { 70, 80, 90, 100, 110, 120, 130, 140, 150 };
        internal static readonly string[] PaceNames = { "0.7x", "0.8x", "0.9x", "Normal", "1.1x", "1.2x", "1.3x", "1.4x", "1.5x" };
        internal static readonly int[] Amounts = { 0, 5, 10, 15, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        internal static readonly string[] AmountNames = { "Off", "5%", "10%", "15%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };
        internal const string StyleLine = "How do I sound now, choom?";
        private readonly List<MenuItem> pitchPicks = new List<MenuItem>();
        private readonly List<MenuItem> pacePicks = new List<MenuItem>();
        private readonly List<MenuItem> robotPicks = new List<MenuItem>();
        private readonly List<MenuItem> glitchPicks = new List<MenuItem>();
        // Settings > Voice style > Layers and each layer's pitch and delay, Windows' VOICE LAYERS window. A menu can't hold every
        // half step, so it has these.
        internal static readonly double[] LayerPitches = { -12, -9, -7, -5, -4, -3, -2.5, -2, -1.5, -1, -0.5, 0, 0.5, 1, 1.5, 2, 2.5, 3, 4, 5, 7, 9, 12 };
        internal static readonly int[] LayerDelays = { 0, 10, 15, 20, 25, 30, 40, 50, 75, 100, 150, 200, 250 };
        private MenuItem layersItem;
        private readonly List<MenuItem>[] layerPitchPicks = { new List<MenuItem>(), new List<MenuItem>() };
        private readonly List<MenuItem>[] layerDelayPicks = { new List<MenuItem>(), new List<MenuItem>() };
        // The style line as ElevenLabs said it, and in which voice, so trying another setting restyles it instead of spending credits.
        private byte[] styleSaid;
        private string styleVoice;
        private PixelPoint? sleptAt;
        private readonly DispatcherTimer frames = new DispatcherTimer();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly Random random = new Random();
        private readonly string folder;
        private Clip clip;
        private double clipStart;
        private int idleCycle;
        private Clip blinkWith;
        private double nextGlitch;
        private bool pressed;
        private bool dragging;
        private bool falling;
        private PixelPoint anchor;
        private PixelPoint grab;
        private PixelPoint downAt;
        private Point pointer = new Point(-1, -1);
        private double fallSpeed;
        private double fallStep;
        private Ledge seat;
        private Ledge fallTo;
        private double coveredSince = -1;
        private double seatLost = -1;
        private double nextCheck;
        private int asking;
        private Smoke smoke;

        internal Pet(IClassicDesktopStyleApplicationLifetime desktop)
        {
            this.desktop = desktop;
            folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri");
            fresh = Program.Smoke == null && !File.Exists(Path.Combine(folder, "cross.json"));
            Saved = Program.Smoke != null ? new Settings() : Load();
            // Security, like Windows: Mac and Linux have the first five toggles' features, and settings from before it get Trusted.
            Security.Has = delegate(int toggle) { return toggle <= Security.Power; };
            Security.Load(Saved.Permissions);
            Security.Changed = delegate { Saved.Permissions = Security.Saved; Save(); if (ContextMenu != null) Sync(); };
            LoginFile = Program.Smoke != null ? Path.Combine(Program.Smoke, OperatingSystem.IsMacOS() ? "capri.plist" : "capri.desktop") : Limbs.LoginFile();
            // A copy that moved since they ticked it takes the login entry over, so it doesn't point at the old place.
            if (Program.Smoke == null && File.Exists(LoginFile)) Limbs.SetLogin(LoginFile, Environment.ProcessPath, true, OperatingSystem.IsMacOS());
            Brain.LogPath = Path.Combine(Program.Smoke ?? folder, "errors.txt");
            Brain.BrainMenu = "in my right-click menu under Brain";
            Brain.FreeKeyHow = "right-click me > Brain > Get a free NVIDIA key";
            Brain.Bin = OperatingSystem.IsMacOS() ? "Trash" : "trash";
            Brain.MemoryPath = Path.Combine(Program.Smoke ?? folder, "me.md");
            // Remember our chats, like on Windows: every chat goes in a daily transcript, and the brain they picked folds older
            // lines into memories.md, which she reads every chat.
            Brain.MemoriesPath = Path.Combine(Program.Smoke ?? folder, "memories.md");
            Brain.TranscriptPath = Path.Combine(Program.Smoke ?? folder, "transcripts");
            Brain.Keeping = !Saved.NoMemories;
            if (Program.Smoke == null) Brain.Keeper = Brain;
            Brain.Language = Brain.LanguageCode(Saved.Language) == "en" ? null : Saved.Language;
            Brain.Nickname = Brain.Nick(Saved.Nickname).Length == 0 ? null : Brain.Nick(Saved.Nickname);
            Brain.Wordiness = Math.Max(0, Math.Min(2, Saved.Replies));
            Brain.Recall = Saved.Recall == 16 || Saved.Recall == 32 ? Saved.Recall : 8;
            Saved.Wander = Math.Max(0, Math.Min(4, Saved.Wander));
            Sfx.Level = Math.Max(0, Math.Min(100, Saved.Sounds ?? 60));
            Voice.Loudness = Math.Max(0, Math.Min(150, Saved.Volume ?? 100));
            VoiceFx.Pitch = Math.Max(-VoiceFx.MostPitch, Math.Min(VoiceFx.MostPitch, Saved.Pitch));
            VoiceFx.Speed = Math.Max(VoiceFx.Slowest, Math.Min(VoiceFx.Fastest, Saved.Pace ?? 100));
            VoiceFx.Robot = Math.Max(0, Math.Min(100, Saved.Robot));
            VoiceFx.Glitch = Math.Max(0, Math.Min(100, Saved.Glitch));
            VoiceFx.Layers = LayersOf(Saved);
            if (Program.Smoke == null) Sfx.Out = Limbs.Sound;
            if (Program.Smoke == null) Brain.Load(Path.Combine(folder, "chat.json"));
            Limbs.Attach(Brain);
            Hands.Look = LookAt;
            Hands.Nap = NapTool;
            Hands.MoveSelf = MoveTool;
            Hands.Animate = AnimateTool;
            if (Program.Smoke == null) presence = new Presence(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            // A cloud key they saved thinks for her instead of Ollama, on the model they picked.
            string brainKey = ReadSecret(BrainKeyFile);
            if (brainKey != null && Cloud.KindOf(brainKey) != null && !string.IsNullOrEmpty(Saved.CloudModel))
            {
                Brain.Remote = new Cloud(brainKey, Saved.CloudModel);
                Brain.Model = Saved.CloudModel;
            }
            voice.Key = ReadVoiceKey();
            voice.VoiceId = string.IsNullOrEmpty(Saved.VoiceId) ? null : Saved.VoiceId;
            if (Program.Smoke == null) { Voice.Playback = Limbs.Speak; Voice.Silence = Limbs.Hush; }
            if (Program.Smoke == null)
            {
                Sfx.TypeOut = delegate(short[] samples) { Limbs.Speak(Voice.Wave(samples, Sfx.Rate)); };
                Sfx.Untyped = Limbs.Hush;
            }
            Title = "Capri";
            SystemDecorations = SystemDecorations.None;
            Background = Brushes.Transparent;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Topmost = !Saved.NotOnTop;
            ShowInTaskbar = false;
            CanResize = false;
            // Showing her, at the start or when she pops in, never takes the keyboard from what they're typing in, like on Windows.
            ShowActivated = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Unfocus(this);
            body.Size = Array.IndexOf(Sizes, Saved.Size) >= 0 ? Saved.Size : 240;
            body.Mouth = mouth;
            RenderOptions.SetBitmapInterpolationMode(body, BitmapInterpolationMode.HighQuality);
            Content = body;
            Fit();
            Play(idle);
            nextGlitch = 30 + random.NextDouble() * 30;
            ContextMenu = Menu();
            // A saved key's models are read now, so a stuck NVIDIA model has others to hand over to.
            if (Brain.Remote != null && Program.Smoke == null) ListCloud();
            Talk = new Talk(this);
            // Her bubble and pill come up by themselves with her, so they leave the keyboard alone too. Her typing box takes it.
            Unfocus(Talk.Bubble);
            Unfocus(Talk.Pill);
            if (!OnTop) Stack(false);
            frames.Interval = TimeSpan.FromMilliseconds(33);
            frames.Tick += delegate { Tick(); };
            Opened += delegate
            {
                if (opened) return;
                opened = true;
                try
                {
                    double points = Desk.Points;
                    if (points > 0 && Screens.Primary != null) Desk.Scale = Screens.Primary.Bounds.Width / points;
                }
                catch (Exception error) { Lose(error); }
                Place();
                frames.Start();
                FindBrain();
                if (Program.Smoke != null) smoke = new Smoke(this, Program.Smoke);
                else
                {
                    int ticket = asking;
                    DispatcherTimer.RunOnce(() => Greet(ticket, DateTime.Now.Hour), TimeSpan.FromMilliseconds(1800));
                    if (fresh) meetAt = Now + 4;
                }
            };
        }

        private void Unfocus(Window shown)
        {
            try
            {
                IPlatformHandle handle = shown.TryGetPlatformHandle();
                if (handle != null) Desk.Unfocused(handle.Handle.ToInt64());
            }
            catch (Exception) { }
        }

        internal double Now { get { return clock.Elapsed.TotalSeconds; } }
        internal bool Falling { get { return falling; } }
        internal bool Dragging { get { return dragging; } }
        internal Ledge Seat { get { return seat; } }
        internal double BodySize { get { return body.Size; } }
        internal Clip Showing { get { return clip; } }
        internal Clip PerchGlitch { get { return perchGlitch; } }
        internal IList<Clip> Clips { get { return new[] { idle, blink, blinkGlitch, glitch, dangle, perch, perchBlink, perchBlinkGlitch, perchGlitch, mouth, walk, dance, perchDance, arrive, leave, perchArrive, perchLeave, grin, sleep, wake }; } }
        internal Clip Arrival { get { return arrive; } }
        internal Clip PerchArrival { get { return perchArrive; } }
        internal Clip SeatDance { get { return perchDance; } }
        internal bool Arriving { get { return clip == arrive || clip == perchArrive; } }
        internal bool Upright { get { return clip == idle && seat == null; } }
        internal bool Asleep { get { return clip == sleep; } }
        internal bool Waking { get { return clip == wake; } }
        internal bool Walking { get { return walking; } }
        internal bool Leaving { get { return outro != null; } }
        internal bool Dancing { get { return dancing; } set { dancing = value; Sync(); } }
        internal PixelPoint Anchor { get { return anchor; } }
        internal PixelPoint Hip { get { return walking && seat != null ? new PixelPoint(anchor.X + Px(body.Size * StandX), anchor.Y + Px(body.Size * StandY)) : new PixelPoint(anchor.X + Px(body.Size * SitX), anchor.Y + Px(body.Size * SitY)); } }
        internal int Headroom { get { return (int)Math.Ceiling(body.Size * (SitY - PerchTop) * DesktopScaling); } }
        internal int Feet { get { return anchor.Y + Px(body.Size * 0.99); } }
        internal PixelPoint FeetPoint { get { return new PixelPoint(anchor.X + Px(body.Size * 0.58), Feet); } }
        internal PixelPoint Head { get { return new PixelPoint(anchor.X + Px(body.Size * 0.52), anchor.Y - Px(4)); } }
        internal PixelPoint Middle { get { return new PixelPoint(anchor.X + Px(body.Size / 2), anchor.Y + Px(body.Size / 2)); } }
        internal PixelRect Area { get { Screen screen = ScreenHere(); return screen == null ? new PixelRect(0, 0, 1920, 1080) : screen.WorkingArea; } }
        internal bool Busy { get { return dragging || pressed || falling; } }
        internal bool Hovered { get { return body.IsPointerOver && Opaque(pointer); } }
        private double FrameX { get { return body.Size * 0.08 - 8; } }
        private double FrameY { get { return -body.Size * 0.06 - 8; } }

        private int Px(double dip) { return (int)Math.Round(dip * DesktopScaling); }

        internal PixelPoint Seated(int x, int y) { return new PixelPoint(x - Px(body.Size * SitX), y - Px(body.Size * SitY)); }

        private PixelPoint Standing(int x, int y) { return new PixelPoint(x - Px(body.Size * StandX), y - Px(body.Size * StandY)); }

        internal void Move(PixelPoint to)
        {
            anchor = to;
            Position = new PixelPoint(to.X + Px(FrameX), to.Y + Px(FrameY));
        }

        private void Fit()
        {
            Width = Math.Ceiling(body.Size * 0.77 + 16);
            Height = Math.Ceiling(body.Size * 1.16 + 16);
            body.Centre = new Point(body.Size / 2 - FrameX, body.Size / 2 - FrameY);
        }

        internal Screen ScreenHere()
        {
            PixelPoint hip = Hip;
            Screen screen = Screens.ScreenFromPoint(new PixelPoint(hip.X, hip.Y - 1));
            if (screen == null) screen = Screens.ScreenFromWindow(this);
            return screen ?? Screens.Primary;
        }

        private PixelRect ScreenAt(PixelPoint point)
        {
            Screen screen = Screens.ScreenFromPoint(point) ?? Screens.Primary;
            return screen == null ? new PixelRect(0, 0, 1920, 1080) : screen.Bounds;
        }

        private PixelRect WorkAt(PixelPoint point)
        {
            Screen screen = Screens.ScreenFromPoint(point) ?? ScreenHere();
            return screen == null ? new PixelRect(0, 0, 1920, 1080) : screen.WorkingArea;
        }

        private void Place()
        {
            Screen saved = Saved.Placed ? Screens.ScreenFromPoint(new PixelPoint(Saved.X + Px(body.Size / 2), Saved.Y + Px(body.Size / 2))) : null;
            Screen screen = saved ?? Screens.Primary ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
            if (screen == null) return;
            Raise(Now + 6);
            if (Saved.NoIntro && saved != null)
            {
                Move(new PixelPoint(Saved.X, Saved.Y));
                Reappear();
                return;
            }
            PixelRect area = screen.WorkingArea;
            Move(new PixelPoint(area.X + (area.Width - Px(body.Size)) / 2, area.Y + (area.Height - Px(body.Size)) / 2));
            Arrive();
        }

        // With Glitch in at startup off she starts back where she sat, like on Windows, with no glitch or sound. If what she
        // sat on has gone, she drops onto what's under her.
        internal void Reappear()
        {
            seat = null;
            Ledge under = Perches.Below(Ledges(), Hip, Px(body.Size / 2), Px(8), 0, Headroom, ScreenAt);
            if (under != null) Land(under);
            if (seat != null) Play(perch);
            else Fall();
        }

        internal void Bring()
        {
            Screen screen = Screens.Primary ?? ScreenHere();
            if (screen == null || quitting) return;
            Halt();
            SetAway(false);
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            walking = false;
            seat = null;
            falling = false;
            hanging = false;
            PixelRect area = screen.WorkingArea;
            Move(new PixelPoint(area.X + area.Width / 2 - Px(body.Size / 2), area.Y));
            Arrive();
            Raise(Now + 6);
        }

        internal bool Away { get { return away; } }

        private void SetAway(bool on)
        {
            away = on;
            ForgetNudges();
            if (AwayChanged != null) AwayChanged();
        }

        internal void HideAway()
        {
            if (away || quitting) return;
            // A reply still on its way is dropped and her voice stops, so no bubble turns up with nobody under it to hold up her
            // pop-ins. A job she's doing carries on.
            ++asking;
            voice.Stop();
            SetAway(true);
            Talk.Dismiss();
            GlitchOut(delegate { seat = null; walking = false; Hide(); });
        }

        internal void ComeBack()
        {
            if (!away || quitting) return;
            Halt();
            SetAway(false);
            if (!IsVisible) { Show(); Look.OnTopAgain(this); }
            walking = false;
            seat = null;
            falling = false;
            hanging = false;
            Arrive();
            Raise(Now + 6);
        }

        internal int Arrivals;

        private void Arrive(bool quiet = false)
        {
            Arrivals++;
            Play(arrive);
            dropAfter = true;
            if (!quiet) Sfx.Play(Cue.Arrive);
        }

        private void Tick()
        {
            double now = Now;
            if (presence != null && now >= nextStatus) { nextStatus = now + 1; presence.Show(Saved.NoDiscord ? null : Doing); }
            if (Program.Smoke == null && now >= nextUpdate) { nextUpdate = now + 6 * 3600; CheckUpdate(); }
            if (UpdateNews != null && !away && !dragging && Talk.Quiet) { Talk.Answer("(update)", UpdateNews); UpdateNews = null; }
            if (outro != null && now >= outroAt) { Action next = outro; outro = null; next(); }
            CheckRaised(now);
            if (away && !IsVisible)
            {
                Talk.Gone();
                if (now >= nextCheck && Program.Smoke == null) { nextCheck = now + 0.25; CheckPopIn(now); }
                if (smoke != null) smoke.Tick(now);
                return;
            }
            if (!dragging)
            {
                if (falling) Tumble(now);
                else if (seat != null) HoldSeat(now);
            }
            if (walking) Walk(now);
            if (now >= nextCheck && !away)
            {
                nextCheck = now + 0.25;
                CheckSeat(now);
                CheckWander(now);
                CheckMusic(now);
                CheckNap();
                CheckHide();
                CheckMeet(now);
                if (Program.Smoke == null) CheckPopIn(now);
                if (Program.Smoke == null) CheckHome(now);
                if (Program.Smoke == null) CheckLag(now);
                if (Program.Smoke == null) CheckNudge(now);
                if (Program.Smoke == null) CheckGlance(now);
                if (Program.Smoke == null) CheckSleep(now);
            }
            Advance(now);
            Talk.Update(now);
            if (smoke != null) smoke.Tick(now);
        }

        // What their Discord profile says under "Playing Capri". Plain words only, like on Windows: song names and what
        // she's told can be private, so they never go out.
        internal string Doing
        {
            get
            {
                if (away) return "Taking a break";
                if (!Talk.Quiet) return "Chatting";
                if (clip == dance || clip == perchDance) return musicOn ? "Dancing to music" : "Dancing";
                if (clip == sleep) return "Asleep";
                return "Hanging out";
            }
        }

        // "Check my screen": her brain looks at a screenshot when they ask, like on Windows. Watch my screen's glances are below.
        private string LookAt(string focus)
        {
            Cloud remote = Brain.Remote;
            if (Brain.Model == null) return "You can't see their screen: your brain is offline.";
            if (remote != null && remote.Kind == "deepseek") return "You can't see their screen: DeepSeek can't look at pictures. Tell them a free NVIDIA key can (right-click you > Brain > Get a free NVIDIA key).";
            byte[] shot = Eyes.Jpeg(Eyes.Shoot(), 1280);
            if (shot == null) return Eyes.Missing;
            string seen;
            try
            {
                seen = Brain.See(shot, "You describe screenshots accurately and briefly.", "Describe what's on this screen in two or three plain sentences: what kind of game or app it is (name it only if the name is on screen), what's happening, and any text or numbers that matter. Don't guess who a person is from their face; name someone only if their name is written on screen." + (string.IsNullOrWhiteSpace(focus) ? "" : " Pay attention to: " + focus.Trim()), 160);
            }
            catch (BrainException error)
            {
                if (remote == null || error.Message != Brain.Sightless(remote)) throw;
                return "You can't see their screen: " + remote.Model + " can't look at pictures. Tell them to pick one that can " + Brain.BrainMenu + (remote.Kind == "nvidia" ? ", like nvidia/nemotron-3.5-super-vl-120b-a12b." : ".");
            }
            if (seen.Length == 0) return "You looked but couldn't make out their screen.";
            Brain.Saw(seen, null, true);
            return "You looked at their screen. You see: " + seen;
        }

        // Her version, the system, her brain and voice, then the last 25 lines of errors.txt, for pasting in the Discord.
        // The system line carries the distro, and on Linux whether it's Wayland or X11 and which desktop.
        internal string ErrorDetails()
        {
            string system = System.Runtime.InteropServices.RuntimeInformation.OSDescription + " " + System.Runtime.InteropServices.RuntimeInformation.OSArchitecture;
            if (OperatingSystem.IsLinux()) system += " " + (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") ?? "?") + " " + (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "?");
            Cloud remote = Brain.Remote;
            string brain = remote != null ? remote.Name + " " + remote.Model : Brain.Model != null ? "Ollama " + Brain.Model : "none";
            string head = "Capri v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " | " + system + " | brain: " + brain + " | voice: " + (voice.Key != null ? "ElevenLabs" : "none");
            string[] lines = new string[0];
            try { if (Brain.LogPath != null && File.Exists(Brain.LogPath)) lines = File.ReadAllLines(Brain.LogPath); } catch (IOException) { }
            int from = Math.Max(0, lines.Length - 25);
            return head + Environment.NewLine + (lines.Length == 0 ? "No errors logged." : string.Join(Environment.NewLine, lines, from, lines.Length - from));
        }

        private void CheckUpdate()
        {
            if (updateChecking) return;
            updateChecking = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string latest = null;
                try
                {
                    using (System.Net.Http.HttpClient client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                    {
                        client.DefaultRequestHeaders.UserAgent.ParseAdd("Capri-Desktop");
                        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
                        latest = client.GetStringAsync(LatestRelease).GetAwaiter().GetResult();
                    }
                }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate { updateChecking = false; if (latest != null) ShowUpdate(latest); });
            });
        }

        // A newer finished release puts "Get Capri X" in her menu, and she mentions it once per version.
        internal void ShowUpdate(string latest)
        {
            Release found = null;
            try { using (MemoryStream body = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(latest ?? ""))) found = (Release)new DataContractJsonSerializer(typeof(Release)).ReadObject(body); }
            catch (Exception) { }
            string version = found == null || found.Draft || found.Prerelease ? null : Newer(found.Tag, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            if (ContextMenu == null) return;
            if (version == null) { if (updateItem != null) ContextMenu.Items.Remove(updateItem); return; }
            updatePage = found.Page != null && found.Page.StartsWith("https://github.com/lachydotmcg/capri-desktop/releases/", StringComparison.Ordinal) ? found.Page : ReleasesPage;
            if (updateItem == null)
            {
                updateItem = new MenuItem();
                updateItem.Click += delegate { Browse(updatePage); };
            }
            updateItem.Header = "Get Capri " + version;
            if (!ContextMenu.Items.Contains(updateItem))
            {
                int at = ContextMenu.Items.Count;
                for (int i = ContextMenu.Items.Count - 1; i >= 0; i--) if (ContextMenu.Items[i] is Separator) { at = i; break; }
                ContextMenu.Items.Insert(at, updateItem);
            }
            if (Saved.UpdateSeen == version) return;
            Saved.UpdateSeen = version;
            Save();
            UpdateNews = "Capri " + version + " is out. Right-click me > Get Capri " + version + ".";
        }

        // The release's version when it's newer than hers, like "0.7.6". Null for older, odd or missing tags.
        internal static string Newer(string tag, Version mine)
        {
            Version found;
            if (tag == null || !System.Text.RegularExpressions.Regex.IsMatch(tag, @"^[vV]?\d+\.\d+(\.\d+)?$") || !Version.TryParse(tag.TrimStart('v', 'V'), out found)) return null;
            found = new Version(found.Major, found.Minor, Math.Max(0, found.Build));
            return found > new Version(mine.Major, mine.Minor, Math.Max(0, mine.Build)) ? found.ToString(3) : null;
        }

        internal string UpdateChoice { get { return updateItem != null && ContextMenu != null && ContextMenu.Items.Contains(updateItem) ? updateItem.Header as string : null; } }

        internal string UpdatePage { get { return updatePage; } }

        internal void CopyErrors()
        {
            Avalonia.Input.Platform.IClipboard board = Clipboard;
            if (board == null) return;
            try { board.SetTextAsync(ErrorDetails()); }
            catch (Exception) { return; }
            Talk.Answer("(error details)", "Copied my error log. Paste it in the Discord so they can fix me, choom.");
        }

        internal void SetDiscord(bool on)
        {
            Saved.NoDiscord = !on;
            Save();
            Sync();
        }

        private void Play(Clip next)
        {
            clip = next;
            clipStart = Now;
            idleCycle = 0;
            blinkWith = null;
        }

        private void Advance(double now)
        {
            bool seated = clip == perch || clip == perchGlitch || clip == perchDance || clip == perchArrive || clip == perchLeave;
            bool perched = seat != null;
            if (perched && !seated && !walking) Play(perch);
            if ((!perched && seated) || (clip == dangle && !dragging && !falling && !hanging)) Play(idle);
            int index = (int)((now - clipStart) * clip.Fps);
            if (clip == leave || clip == perchLeave) index = Math.Min(index, clip.Count - 1);
            if ((clip == glitch && index >= glitch.Count) || (clip == wake && index >= wake.Count) || (clip == grin && index >= grin.Count * 2)) { Play(idle); index = 0; }
            // Like on Windows, she's awake as soon as she has something to say or they open her typing box.
            if (clip == sleep && !Talk.Quiet) { sleptAt = null; Play(idle); index = 0; }
            if (clip == arrive && index >= arrive.Count)
            {
                Play(idle);
                index = 0;
                if (dropAfter) { dropAfter = false; Fall(); }
            }
            if ((clip == perchGlitch && index >= perchGlitch.Count) || (clip == perchArrive && index >= perchArrive.Count)) { Play(perch); index = 0; }
            if (dropAfter && clip != arrive && clip != glitch)
            {
                dropAfter = false;
                if (seat == null && !dragging && !falling) Fall();
            }
            bool still = !walking && !dragging && !falling && !hanging && outro == null && Talk.Quiet && !napAsked && !napping;
            bool grooving = dancing || (musicOn && !Saved.NoDance && !danceHeld);
            if (grooving && still && (clip == idle || clip == perch)) { Play(clip == perch ? perchDance : dance); index = 0; }
            if ((!grooving || !still) && (clip == dance || clip == perchDance)) { Play(clip == perchDance ? perch : idle); index = 0; }
            if (clip == idle || clip == perch)
            {
                int cycle = index / clip.Count;
                if (cycle != idleCycle)
                {
                    idleCycle = cycle;
                    double roll = random.NextDouble();
                    blinkWith = roll < 0.1 ? (clip == perch ? perchBlinkGlitch : blinkGlitch) : roll < 0.55 ? (clip == perch ? perchBlink : blink) : null;
                    if (now >= nextGlitch && Talk.Quiet)
                    {
                        nextGlitch = now + 30 + random.NextDouble() * 30;
                        Play(clip == perch ? perchGlitch : glitch);
                        index = 0;
                    }
                }
            }
            int cell = index % clip.Count;
            Clip shown = clip;
            int blinkFrom = clip == perch ? PerchBlinkFrom : BlinkFrom;
            if ((clip == idle || clip == perch) && blinkWith != null && cell >= blinkFrom && cell < blinkFrom + blinkWith.Count) { shown = blinkWith; cell -= blinkFrom; }
            bool settled = !dragging && clip != dangle && clip != sleep;
            body.Breathe = settled ? Math.Sin(now * 1.75) * 2.2 : 0;
            body.Tilt = settled ? Math.Sin(now * 0.95) * 0.65 : 0;
            body.Shown = shown;
            body.Cell = cell;
            body.Talking = clip == idle && shown != blinkGlitch ? Talk.MouthCell(now) : -1;
            body.InvalidateVisual();
        }

        private List<long> Ours()
        {
            List<long> skip = new List<long>();
            foreach (TopLevel window in new TopLevel[] { this, Talk.Bubble, Talk.Typer, Talk.Pill })
            {
                IPlatformHandle handle = window.TryGetPlatformHandle();
                if (handle != null) skip.Add(handle.Handle.ToInt64());
            }
            return skip;
        }

        internal string DeskTrouble;

        private void Lose(Exception error)
        {
            DeskTrouble = error.GetType().Name + ": " + error.Message;
            Desk = new BareDesk();
        }

        internal List<Spot> Windows()
        {
            try { return Desk.Windows(Ours()); }
            catch (Exception error) { Lose(error); return new List<Spot>(); }
        }

        private Spot Find(long window)
        {
            try { return Desk.Find(window); }
            catch (Exception error) { Lose(error); return null; }
        }

        private bool Alive(long window)
        {
            try { return Desk.Alive(window); }
            catch (Exception error) { Lose(error); return false; }
        }

        internal List<Ledge> Ledges()
        {
            List<PixelRect> floors = new List<PixelRect>();
            Desk.Screens.Clear();
            foreach (Screen screen in Screens.All)
            {
                floors.Add(screen.WorkingArea);
                Desk.Screens.Add(screen.Bounds);
            }
            return Perches.Find(Windows(), floors);
        }

        internal Ledge Perch(IEnumerable<Ledge> ledges, PixelPoint hip)
        {
            return Perches.Below(ledges, hip, Px(Math.Max(32, body.Size * 0.45)), Px(Math.Max(160, body.Size)), Px(24), Headroom, ScreenAt);
        }

        internal void Fall()
        {
            walking = false;
            seat = null;
            hanging = false;
            fallTo = null;
            falling = true;
            fallSpeed = 0;
            fallStep = Now;
            Play(dangle);
        }

        private void Tumble(double now)
        {
            double step = Math.Max(0, Math.Min(0.05, now - fallStep));
            fallStep = now;
            PixelPoint hip = Hip;
            if (fallTo == null) fallTo = Perches.Below(Ledges(), hip, 0, int.MaxValue, 0, Headroom, ScreenAt);
            if (fallTo == null) { falling = false; Save(); return; }
            fallSpeed += body.Size * 11 * step;
            int drop = (int)Math.Ceiling(fallSpeed * step * DesktopScaling);
            if (hip.Y + drop >= fallTo.Y) Land(fallTo);
            else Move(new PixelPoint(anchor.X, anchor.Y + drop));
        }

        private void Land(Ledge ledge)
        {
            fallTo = null;
            walking = false;
            if (ledge.Window != 0)
            {
                Spot spot = Find(ledge.Window);
                if (spot == null || !spot.Seat) return;
                if (spot.Bounds != ledge.Bounds) { ledge.Left = spot.Bounds.X; ledge.Right = spot.Bounds.Right; ledge.Y = spot.Bounds.Y; ledge.Bounds = spot.Bounds; }
            }
            falling = false;
            coveredSince = -1;
            seatLost = -1;
            int x = Math.Max(ledge.Left + Px(8), Math.Min(Hip.X, ledge.Right - Px(8)));
            PixelRect area = WorkAt(new PixelPoint(x, ledge.Y - 1));
            int rightmost = area.Right - (int)Math.Ceiling(body.Size * (PerchRight - SitX) * DesktopScaling);
            x = Math.Max(area.X + (int)Math.Ceiling(body.Size * (SitX - PerchLeft) * DesktopScaling), Math.Min(x, rightmost));
            seat = ledge;
            Move(Seated(x, ledge.Y));
            Play(perchGlitch);
            Save();
        }

        internal void HoldSeat(double now)
        {
            if (seat == null || seat.Window == 0) return;
            Spot spot = Find(seat.Window);
            if (spot == null || !spot.Seat)
            {
                if (seatLost < 0) seatLost = now;
                if (!Alive(seat.Window) || now - seatLost >= 0.3) StandUp();
                return;
            }
            seatLost = -1;
            PixelRect bounds = spot.Bounds;
            if (bounds == seat.Bounds) return;
            EndWalk();
            int dx =bounds.Width == seat.Bounds.Width ? bounds.X - seat.Bounds.X : 0;
            int x = Math.Max(bounds.X + Px(8), Math.Min(Hip.X + dx, bounds.Right - Px(8)));
            seat.Bounds = bounds;
            seat.Left = bounds.X;
            seat.Right = bounds.Right;
            seat.Y = bounds.Y;
            Move(Seated(x, bounds.Y));
        }

        internal void CheckSeat(double now)
        {
            if (seat == null || falling || dragging || seat.Floor) return;
            if (!Perches.Covered(Windows(), seat, Hip.X)) { coveredSince = -1; return; }
            if (coveredSince < 0) coveredSince = now;
            if (now - coveredSince >= 0.7) StandUp();
        }

        internal void StandUp()
        {
            Halt();
            walking = false;
            seat = null;
            hanging = false;
            coveredSince = -1;
            seatLost = -1;
            falling = false;
            fallTo = null;
            Play(glitch);
        }

        private void SitHere()
        {
            Halt();
            EndWalk();
            hanging = false;
            PixelPoint hip = Hip;
            Land(new Ledge { Left = hip.X - Px(8), Right = hip.X + Px(8), Y = hip.Y, Floor = true, Free = true });
        }

        // Hang here, like on Windows: she dangles where she is until a click, a drag, "let go" or "stand up".
        private void HangHere()
        {
            Halt();
            EndWalk();
            dropAfter = false;
            seat = null;
            falling = false;
            fallTo = null;
            hanging = true;
            Play(dangle);
            Save();
        }

        private void LetGo()
        {
            hanging = false;
            Play(glitch);
            Drop();
        }

        internal bool Hanging { get { return hanging; } }

        internal string HangChoice { get { return hanging ? "Let go" : "Hang here"; } }

        private bool Opaque(Point at)
        {
            Clip shown = body.Shown;
            if (shown == null || at.X < 0) return false;
            Rect place = body.Place(shown);
            double x = at.X - place.X;
            double y = at.Y - body.Breathe - place.Y;
            if (x < 0 || y < 0 || x >= place.Width || y >= place.Height) return false;
            return shown.Solid(body.Cell, (int)(x / place.Width * shown.Wide), (int)(y / place.Height * shown.Strip.PixelSize.Height), 6);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || outro != null) return;
            Point at = e.GetPosition(this);
            if (!Opaque(at)) return;
            pressed = true;
            clicks++;
            dragging = false;
            downAt = this.PointToScreen(at);
            grab = new PixelPoint(downAt.X - anchor.X, downAt.Y - anchor.Y);
            e.Pointer.Capture(this);
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            pointer = e.GetPosition(this);
            if (!pressed) { Stroke(this.PointToScreen(pointer)); return; }
            PixelPoint at = this.PointToScreen(pointer);
            int dx = at.X - downAt.X;
            int dy = at.Y - downAt.Y;
            if (!dragging && dx * dx + dy * dy < 25) return;
            if (!dragging) Grab();
            Move(new PixelPoint(at.X - grab.X, at.Y - grab.Y));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            pointer = new Point(-1, -1);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            if (!pressed || e.InitialPressMouseButton != MouseButton.Left) return;
            pressed = false;
            e.Pointer.Capture(null);
            if (dragging) Let((e.KeyModifiers & KeyModifiers.Shift) != 0);
            else React();
        }

        // Letting go of her: she falls onto the window below, or with Shift held sits right there, like on Windows.
        internal void Let(bool here)
        {
            if (!here) { Drop(); return; }
            dragging = false;
            SitHere();
        }

        internal int Pets { get { return petTicket; } }

        internal bool Smiling { get { return seat != null ? clip == perch && blinkWith == perchBlink : clip == grin; } }

        internal Point? BodySpot()
        {
            for (double y = Bounds.Height / 4; y < Bounds.Height * 3 / 4; y += 3)
                for (double x = 20; x < Bounds.Width - 20; x += 3)
                    if (Opaque(new Point(x - 12, y)) && Opaque(new Point(x + 12, y))) return new Point(x, y);
            return null;
        }

        internal void Stroke(PixelPoint cursor)
        {
            if (pressed || dragging || falling || outro != null || Talk.Typing || !Opaque(this.PointToClient(cursor))) { petTurns.Clear(); petWay = 0; return; }
            int dx = cursor.X - petX;
            if (Math.Abs(dx) < 6) return;
            double now = Now;
            int way = Math.Sign(dx);
            petX = cursor.X;
            if (petWay != 0 && way != petWay) petTurns.Add(now);
            petWay = way;
            petTurns.RemoveAll(at => now - at > 1.5);
            if (petTurns.Count < 4 || now - petAt < 20) return;
            petTurns.Clear();
            petAt = now;
            Petted();
        }

        private void Petted()
        {
            EndWalk();
            if (seat != null) { Play(perch); blinkWith = perchBlink; }
            else Play(grin);
            int ticket = ++petTicket;
            DispatcherTimer.RunOnce(() => PetLine(ticket), TimeSpan.FromMilliseconds(1300));
        }

        internal void Greet(int ticket, int hour)
        {
            string line = Brain.Greeting(hour);
            if (line == null || ticket != asking || outro != null || !Talk.Quiet) return;
            byte[] hello = Hello(Brain.GreetingClip(hour));
            if (hello == null) { Reply(++asking, "startup", line); return; }
            ++asking;
            Talk.Answer("startup", line, Voice.Seconds(hello));
            voice.Play(hello);
        }

        internal static byte[] Hello(string name)
        {
            using (Stream clip = typeof(Pet).Assembly.GetManifestResourceStream("Capri.Voice.hello-" + name + ".wav"))
            {
                if (clip == null) return null;
                using (MemoryStream copy = new MemoryStream()) { clip.CopyTo(copy); return copy.ToArray(); }
            }
        }

        internal void PetLine(int ticket)
        {
            if (ticket != petTicket || outro != null || away || !Talk.Quiet) return;
            if (clip == grin) Play(idle);
            string line = PetLines[petLine++ % PetLines.Length];
            Talk.Answer("pets you", line);
            TypeAlong(line, ticket);
        }

        internal void ForgetPets()
        {
            petTicket++;
            petAt = -60;
            petTurns.Clear();
            Play(seat != null ? perch : idle);
        }

        internal void React()
        {
            napAsked = false;
            if (hanging) { LetGo(); return; }
            // A click wakes her the gentle way, from a nap they asked for or her own doze.
            if (clip == sleep) { napping = false; Wake(); return; }
            Play(seat != null ? perchGlitch : glitch);
        }

        // A quick click, pressed and let go, for the smoke test.
        internal void Tap()
        {
            clicks++;
            React();
        }

        internal void NoVoice()
        {
            Talk.Answer("(voice)", "Voice chat isn't on Mac and Linux yet. Type to me for now, choom.");
        }

        internal void Grab()
        {
            Halt();
            walking = false;
            dropAfter = false;
            hanging = false;
            seat = null;
            fallTo = null;
            falling = false;
            dragging = true;
            Play(dangle);
            Talk.Dismiss();
        }

        internal bool Drop()
        {
            dragging = false;
            Ledge target = Perch(Ledges(), Hip);
            if (target == null) { Save(); return false; }
            falling = true;
            fallTo = target;
            fallSpeed = 0;
            fallStep = Now;
            return true;
        }

        internal void Resize(double size)
        {
            EndWalk();
            PixelPoint hip = Hip;
            PixelPoint middle = Middle;
            body.Size = size;
            Saved.Size = size;
            Fit();
            if (seat != null) Move(Seated(hip.X, seat.Y));
            else Move(new PixelPoint(middle.X - Px(size / 2), middle.Y - Px(size / 2)));
            body.InvalidateVisual();
            Save();
        }

        private ContextMenu Menu()
        {
            ContextMenu menu = new ContextMenu();
            MenuItem talk = new MenuItem { Header = "Talk to her" };
            talk.Click += delegate { Talk.Open(); };
            quietItem = new MenuItem { Header = "Quiet", ToggleType = MenuItemToggleType.CheckBox };
            quietItem.Click += delegate { ToggleShush(); };
            sitItem = new MenuItem { Header = "Sit here" };
            sitItem.Click += delegate { if (seat != null && seat.Free) StandUp(); else SitHere(); };
            hangItem = new MenuItem { Header = "Hang here" };
            hangItem.Click += delegate { if (hanging) LetGo(); else HangHere(); };
            napItem = new MenuItem { Header = "Go to sleep" };
            napItem.Click += delegate { Nap(!napAsked && clip != sleep); };
            MenuItem move = new MenuItem { Header = "Move" };
            for (int i = 0; i < WanderNames.Length; i++)
            {
                int level = i;
                MenuItem pick = new MenuItem { Header = i == 0 ? "Stay put" : "Wander: " + WanderNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "wander" };
                pick.Click += delegate { SetWander(level); };
                wanderPicks.Add(pick);
                move.Items.Add(pick);
            }
            move.Items.Add(new Separator());
            musicItem = new MenuItem { Header = "Dance to music", ToggleType = MenuItemToggleType.CheckBox };
            musicItem.Click += delegate { SetDanceToMusic(Saved.NoDance); };
            move.Items.Add(musicItem);
            danceItem = new MenuItem { Header = "Dance", ToggleType = MenuItemToggleType.CheckBox };
            danceItem.Click += delegate { Animate(dancing ? "stop_dancing" : "dance"); };
            move.Items.Add(danceItem);
            screenItem = new MenuItem { Header = "Go to the next screen" };
            screenItem.Click += delegate { MoveSelf("other_screen", null); };
            move.Items.Add(screenItem);
            move.Items.Add(new Separator());
            MenuItem sleeps = new MenuItem { Header = "Fall asleep after" };
            for (int i = 0; i < Sleeps.Length; i++)
            {
                int seconds = Sleeps[i];
                MenuItem pick = new MenuItem { Header = SleepNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "sleeps" };
                pick.Click += delegate { SetSleepAfter(seconds); };
                sleepPicks.Add(pick);
                sleeps.Items.Add(pick);
            }
            move.Items.Add(sleeps);
            introItem = new MenuItem { Header = "Glitch in at startup", ToggleType = MenuItemToggleType.CheckBox };
            introItem.Click += delegate { SetIntro(Saved.NoIntro); };
            move.Items.Add(introItem);
            brainMenu = new MenuItem { Header = "Brain" };
            brainMenu.Items.Add(new MenuItem { Header = "Looking for models...", IsEnabled = false });
            MenuItem settings = new MenuItem { Header = "Settings" };
            MenuItem language = new MenuItem { Header = "Language" };
            for (int i = 0; i < Brain.Languages.GetLength(0); i++)
            {
                string name = Brain.Languages[i, 1];
                MenuItem pick = new MenuItem { Header = Brain.Languages[i, 0], ToggleType = MenuItemToggleType.Radio, GroupName = "language", Tag = name };
                pick.Click += delegate { SetLanguage(name); };
                languagePicks.Add(pick);
                language.Items.Add(pick);
            }
            MenuItem replies = new MenuItem { Header = "Reply length" };
            MenuItem recall = new MenuItem { Header = "Chat memory" };
            for (int i = 0; i < Lengths.Length; i++)
            {
                int level = i;
                MenuItem reply = new MenuItem { Header = Lengths[i], ToggleType = MenuItemToggleType.Radio, GroupName = "replies" };
                reply.Click += delegate { SetReplies(level); };
                replyPicks.Add(reply);
                replies.Items.Add(reply);
                MenuItem keep = new MenuItem { Header = Lengths[i], ToggleType = MenuItemToggleType.Radio, GroupName = "recall" };
                keep.Click += delegate { SetRecall(Recalls[level]); };
                recallPicks.Add(keep);
                recall.Items.Add(keep);
            }
            MenuItem sounds = new MenuItem { Header = "Sounds" };
            for (int i = 0; i < SoundNames.Length; i++)
            {
                int level = SoundLevels[i];
                MenuItem pick = new MenuItem { Header = SoundNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "sounds" };
                pick.Click += delegate { SetSounds(level); };
                soundPicks.Add(pick);
                sounds.Items.Add(pick);
            }
            sounds.Items.Add(new Separator());
            typingItem = new MenuItem { Header = "Typing sound", ToggleType = MenuItemToggleType.CheckBox };
            typingItem.Click += delegate { SetTyping(!TypingSound); };
            sounds.Items.Add(typingItem);
            MenuItem notes = new MenuItem { Header = "Open her notes (me.md)" };
            notes.Click += delegate { OpenNotes(); };
            memoriesItem = new MenuItem { Header = "Remember our chats", ToggleType = MenuItemToggleType.CheckBox };
            memoriesItem.Click += delegate { SetRemembering(Saved.NoMemories); };
            MenuItem memories = new MenuItem { Header = "Open her memories (memories.md)" };
            memories.Click += delegate { OpenMemories(); };
            MenuItem nickname = new MenuItem { Header = "What should she call you?" };
            nickname.Click += delegate { AskNickname(); };
            settings.Items.Add(nickname);
            MenuItem meet = new MenuItem { Header = "Get to know me" };
            meet.Click += delegate { GetToKnow(); };
            settings.Items.Add(meet);
            settings.Items.Add(language);
            settings.Items.Add(replies);
            settings.Items.Add(recall);
            MenuItem bubble = new MenuItem { Header = "Bubble" };
            for (int i = 0; i < BubbleNames.Length; i++)
            {
                int level = i;
                MenuItem pick = new MenuItem { Header = BubbleNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "bubble" };
                pick.Click += delegate { SetBubbleTime(level); };
                bubblePicks.Add(pick);
                bubble.Items.Add(pick);
            }
            settings.Items.Add(bubble);
            settings.Items.Add(sounds);
            voiceMenu = new MenuItem { Header = "Voice" };
            settings.Items.Add(voiceMenu);
            MenuItem volume = new MenuItem { Header = "Volume" };
            for (int i = 0; i < Volumes.Length; i++)
            {
                int percent = Volumes[i];
                MenuItem pick = new MenuItem { Header = VolumeNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "volume" };
                pick.Click += delegate { SetVolume(percent); };
                volumePicks.Add(pick);
                volume.Items.Add(pick);
            }
            settings.Items.Add(volume);
            MenuItem style = new MenuItem { Header = "Voice style" };
            MenuItem pitch = new MenuItem { Header = "Pitch" };
            for (int i = 0; i < PitchNames.Length; i++)
            {
                int semitones = i - VoiceFx.MostPitch;
                MenuItem pick = new MenuItem { Header = PitchNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "pitch" };
                pick.Click += delegate { SetPitch(semitones); HearStyle(); };
                pitchPicks.Add(pick);
                pitch.Items.Add(pick);
            }
            MenuItem speed = new MenuItem { Header = "Speed" };
            for (int i = 0; i < Paces.Length; i++)
            {
                int percent = Paces[i];
                MenuItem pick = new MenuItem { Header = PaceNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "pace" };
                pick.Click += delegate { SetPace(percent); HearStyle(); };
                pacePicks.Add(pick);
                speed.Items.Add(pick);
            }
            MenuItem robot = new MenuItem { Header = "Robot" };
            MenuItem glitchy = new MenuItem { Header = "Glitch" };
            for (int i = 0; i < Amounts.Length; i++)
            {
                int amount = Amounts[i];
                MenuItem robotPick = new MenuItem { Header = AmountNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "robot" };
                robotPick.Click += delegate { SetRobot(amount); HearStyle(); };
                robotPicks.Add(robotPick);
                robot.Items.Add(robotPick);
                MenuItem glitchPick = new MenuItem { Header = AmountNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "glitch" };
                glitchPick.Click += delegate { SetGlitch(amount); HearStyle(); };
                glitchPicks.Add(glitchPick);
                glitchy.Items.Add(glitchPick);
            }
            style.Items.Add(pitch);
            style.Items.Add(speed);
            style.Items.Add(robot);
            style.Items.Add(glitchy);
            // Her voice layers (Lachy, 29 Oct): two copies of her mixed in under her, each with its own pitch and delay. Pitch above
            // is Main's.
            layersItem = new MenuItem { Header = "Layers", ToggleType = MenuItemToggleType.CheckBox };
            layersItem.Click += delegate { SetLayers(VoiceFx.Layers.Turned(!VoiceFx.Layers.On)); HearStyle(); };
            style.Items.Add(layersItem);
            for (int layer = 1; layer <= 2; layer++)
            {
                int which = layer;
                MenuItem layerPitch = new MenuItem { Header = "Layer " + layer + " pitch" };
                foreach (double semitones in LayerPitches)
                {
                    double picked = semitones;
                    MenuItem pick = new MenuItem { Header = VoiceFx.Layering.PitchName(semitones), ToggleType = MenuItemToggleType.Radio, GroupName = "layer" + layer + "pitch" };
                    pick.Click += delegate { SetLayers(VoiceFx.Layers.With(which, picked, VoiceFx.Layers.DelayOf(which))); HearStyle(); };
                    layerPitchPicks[layer - 1].Add(pick);
                    layerPitch.Items.Add(pick);
                }
                MenuItem layerDelay = new MenuItem { Header = "Layer " + layer + " delay" };
                foreach (int ms in LayerDelays)
                {
                    int picked = ms;
                    MenuItem pick = new MenuItem { Header = VoiceFx.Layering.DelayName(ms), ToggleType = MenuItemToggleType.Radio, GroupName = "layer" + layer + "delay" };
                    pick.Click += delegate { SetLayers(VoiceFx.Layers.With(which, VoiceFx.Layers.PitchOf(which), picked)); HearStyle(); };
                    layerDelayPicks[layer - 1].Add(pick);
                    layerDelay.Items.Add(pick);
                }
                style.Items.Add(layerPitch);
                style.Items.Add(layerDelay);
            }
            settings.Items.Add(style);
            checkInsItem = new MenuItem { Header = "Check in on me", ToggleType = MenuItemToggleType.CheckBox };
            checkInsItem.Click += delegate { SetCheckIns(Saved.NoCheckIns); };
            settings.Items.Add(checkInsItem);
            MenuItem pops = new MenuItem { Header = "Pop-ins" };
            for (int i = 0; i < PopNames.Length; i++)
            {
                int pace = i;
                MenuItem pick = new MenuItem { Header = PopNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "pops" };
                pick.Click += delegate { SetPopPace(pace); };
                popPicks.Add(pick);
                pops.Items.Add(pick);
            }
            settings.Items.Add(pops);
            onTopItem = new MenuItem { Header = "Always on top", ToggleType = MenuItemToggleType.CheckBox };
            onTopItem.Click += delegate { SetOnTop(Saved.NotOnTop); };
            settings.Items.Add(onTopItem);
            watchItem = new MenuItem { Header = "Watch my screen", ToggleType = MenuItemToggleType.CheckBox };
            watchItem.Click += delegate { SetWatch(!Saved.Watch); };
            settings.Items.Add(watchItem);
            MenuItem gaps = new MenuItem { Header = "Glance every" };
            for (int i = 0; i < GlanceGaps.Length; i++)
            {
                int seconds = GlanceGaps[i];
                MenuItem pick = new MenuItem { Header = GlanceGapNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "gaps" };
                pick.Click += delegate { SetGlanceGap(seconds); };
                gapPicks.Add(pick);
                gaps.Items.Add(pick);
            }
            settings.Items.Add(gaps);
            caughtUpItem = new MenuItem { Header = "Keep her caught up", ToggleType = MenuItemToggleType.CheckBox };
            caughtUpItem.Click += delegate { SetCaughtUp(Saved.NoCaughtUp); };
            settings.Items.Add(caughtUpItem);
            loginItem = new MenuItem { Header = "Start at login", ToggleType = MenuItemToggleType.CheckBox };
            loginItem.Click += delegate { SetLogin(!StartsAtLogin); };
            settings.Items.Add(loginItem);
            discordItem = new MenuItem { Header = "Show on Discord", ToggleType = MenuItemToggleType.CheckBox };
            discordItem.Click += delegate { SetDiscord(Saved.NoDiscord); };
            settings.Items.Add(discordItem);
            // Security, like Windows' SECURITY window (Lachy, 29 Oct): Lockdown, Guarded, Trusted and Blackwall set the toggles
            // under them, and flipping one makes it Custom. Each toggle has its plain line. Off means she asks first and does it
            // on your yes. Mac and Linux have the first five; her mouse and keyboard jobs and "Hey Capri" are Windows-only so far.
            securityMenu = new MenuItem { Header = "Security: " + Security.PresetName };
            for (int i = 0; i <= Security.Custom; i++)
            {
                int preset = i;
                MenuItem pick = new MenuItem { Header = Security.Presets[i], ToggleType = MenuItemToggleType.Radio, GroupName = "security", IsEnabled = i != Security.Custom };
                ToolTip.SetTip(pick, Security.PresetLine(i));
                pick.Click += delegate { if (preset != Security.Custom) Security.Pick(preset); };
                securityPicks.Add(pick);
                securityMenu.Items.Add(pick);
            }
            securityMenu.Items.Add(new Separator());
            for (int i = 0; i < Security.Count; i++)
            {
                if (!Security.Has(i)) continue;
                int toggle = i;
                StackPanel face = new StackPanel();
                face.Children.Add(new TextBlock { Text = Security.Names[i] });
                face.Children.Add(new TextBlock { Text = Security.Lines[i], FontSize = 11, Foreground = new SolidColorBrush(Look.Dim) });
                MenuItem item = new MenuItem { Header = face, ToggleType = MenuItemToggleType.CheckBox, Tag = i };
                item.Click += delegate { Security.Set(toggle, !Security.On(toggle)); };
                securityToggles.Add(item);
                securityMenu.Items.Add(item);
            }
            settings.Items.Add(securityMenu);
            settings.Items.Add(notes);
            settings.Items.Add(memoriesItem);
            settings.Items.Add(memories);
            MenuItem sizes = new MenuItem { Header = "Size" };
            for (int i = 0; i < Sizes.Length; i++)
            {
                double size = Sizes[i];
                MenuItem pick = new MenuItem { Header = SizeNames[i] };
                pick.Click += delegate { Resize(size); };
                sizes.Items.Add(pick);
            }
            MenuItem discord = new MenuItem { Header = "Join the Discord" };
            discord.Click += delegate { Browse(DiscordPage); };
            MenuItem help = new MenuItem { Header = "Help" };
            MenuItem helpPage = new MenuItem { Header = "Help page" };
            helpPage.Click += delegate { Browse(HelpPage); };
            MenuItem copyErrors = new MenuItem { Header = "Copy error details" };
            copyErrors.Click += delegate { CopyErrors(); };
            help.Items.Add(helpPage);
            help.Items.Add(copyErrors);
            MenuItem quit = new MenuItem { Header = "Quit Capri" };
            quit.Click += delegate { Quit(); };
            menu.Items.Add(talk);
            menu.Items.Add(quietItem);
            menu.Items.Add(sitItem);
            menu.Items.Add(hangItem);
            menu.Items.Add(napItem);
            menu.Items.Add(move);
            menu.Items.Add(sizes);
            menu.Items.Add(brainMenu);
            menu.Items.Add(settings);
            menu.Items.Add(help);
            menu.Items.Add(discord);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);
            menu.Opening += delegate { Sync(); RefreshBrains(); RefreshVoice(); };
            return menu;
        }

        private void Sync()
        {
            if (danceItem == null) return;
            for (int i = 0; i < wanderPicks.Count; i++) wanderPicks[i].IsChecked = i == Saved.Wander;
            danceItem.IsChecked = dancing;
            musicItem.IsChecked = !Saved.NoDance;
            introItem.IsChecked = !Saved.NoIntro;
            onTopItem.IsChecked = OnTop;
            string language = Brain.Language ?? "English";
            foreach (MenuItem pick in languagePicks) pick.IsChecked = (string)pick.Tag == language;
            for (int i = 0; i < replyPicks.Count; i++) replyPicks[i].IsChecked = i == Brain.Wordiness;
            for (int i = 0; i < recallPicks.Count; i++) recallPicks[i].IsChecked = Recalls[i] == Brain.Recall;
            for (int i = 0; i < soundPicks.Count; i++) soundPicks[i].IsChecked = SoundLevels[i] == Sfx.Level;
            typingItem.IsChecked = TypingSound;
            discordItem.IsChecked = !Saved.NoDiscord;
            securityMenu.Header = "Security: " + Security.PresetName;
            for (int i = 0; i < securityPicks.Count; i++) securityPicks[i].IsChecked = i == Security.Preset;
            foreach (MenuItem item in securityToggles) item.IsChecked = Security.On((int)item.Tag);
            memoriesItem.IsChecked = !Saved.NoMemories;
            checkInsItem.IsChecked = !Saved.NoCheckIns;
            for (int i = 0; i < popPicks.Count; i++) popPicks[i].IsChecked = i == PopPace;
            loginItem.IsChecked = StartsAtLogin;
            watchItem.IsChecked = Saved.Watch;
            for (int i = 0; i < gapPicks.Count; i++) gapPicks[i].IsChecked = GlanceGaps[i] == GlanceGap;
            caughtUpItem.IsChecked = !Saved.NoCaughtUp;
            quietItem.IsChecked = shushed;
            for (int i = 0; i < sleepPicks.Count; i++) sleepPicks[i].IsChecked = Sleeps[i] == SleepAfter;
            sitItem.Header = SitChoice;
            hangItem.Header = HangChoice;
            screenItem.IsVisible = Screens.All.Count > 1;
            napItem.Header = NapChoice;
            for (int i = 0; i < bubblePicks.Count; i++) bubblePicks[i].IsChecked = i == BubbleTime;
            for (int i = 0; i < volumePicks.Count; i++) volumePicks[i].IsChecked = Volumes[i] == Voice.Loudness;
            for (int i = 0; i < pitchPicks.Count; i++) pitchPicks[i].IsChecked = i - VoiceFx.MostPitch == VoiceFx.Pitch;
            for (int i = 0; i < pacePicks.Count; i++) pacePicks[i].IsChecked = Paces[i] == VoiceFx.Speed;
            for (int i = 0; i < robotPicks.Count; i++) robotPicks[i].IsChecked = Amounts[i] == VoiceFx.Robot;
            for (int i = 0; i < glitchPicks.Count; i++) glitchPicks[i].IsChecked = Amounts[i] == VoiceFx.Glitch;
            VoiceFx.Layering layers = VoiceFx.Layers;
            if (layersItem != null) layersItem.IsChecked = layers.On;
            for (int layer = 1; layer <= 2; layer++)
            {
                for (int i = 0; i < layerPitchPicks[layer - 1].Count; i++) layerPitchPicks[layer - 1][i].IsChecked = LayerPitches[i] == layers.PitchOf(layer);
                for (int i = 0; i < layerDelayPicks[layer - 1].Count; i++) layerDelayPicks[layer - 1][i].IsChecked = LayerDelays[i] == layers.DelayOf(layer);
            }
        }

        // What Voice style's Layers menus have ticked, like Windows' VOICE LAYERS window: "On | +2, 25 ms | -2.5, 25 ms".
        internal string LayersShown
        {
            get
            {
                string[] pitchNames = new string[LayerPitches.Length], delayNames = new string[LayerDelays.Length];
                for (int i = 0; i < pitchNames.Length; i++) pitchNames[i] = VoiceFx.Layering.PitchName(LayerPitches[i]);
                for (int i = 0; i < delayNames.Length; i++) delayNames[i] = VoiceFx.Layering.DelayName(LayerDelays[i]);
                return (layersItem.IsChecked == true ? "On" : "Off") + " | " + Ticked(layerPitchPicks[0], pitchNames) + ", " + Ticked(layerDelayPicks[0], delayNames) + " | " + Ticked(layerPitchPicks[1], pitchNames) + ", " + Ticked(layerDelayPicks[1], delayNames);
            }
        }

        internal static VoiceFx.Layering LayersOf(Settings saved)
        {
            return new VoiceFx.Layering(saved.Layers, saved.LayerPitch1 ?? VoiceFx.Layering.HigherPitch, saved.LayerDelay1 ?? VoiceFx.Layering.Behind, saved.LayerPitch2 ?? VoiceFx.Layering.LowerPitch, saved.LayerDelay2 ?? VoiceFx.Layering.Behind);
        }

        internal void SetLayers(VoiceFx.Layering layers)
        {
            VoiceFx.Layers = layers ?? VoiceFx.Layering.Off;
            Saved.Layers = VoiceFx.Layers.On;
            Saved.LayerPitch1 = VoiceFx.Layers.Pitch1;
            Saved.LayerDelay1 = VoiceFx.Layers.Delay1;
            Saved.LayerPitch2 = VoiceFx.Layers.Pitch2;
            Saved.LayerDelay2 = VoiceFx.Layers.Delay2;
            Save();
            Sync();
        }

        // What Voice style's menus have ticked, like Windows' VOICE section: "+3 | 1.2x | 30% | 40%".
        internal string StyleShown
        {
            get
            {
                return Ticked(pitchPicks, PitchNames) + " | " + Ticked(pacePicks, PaceNames) + " | " + Ticked(robotPicks, AmountNames) + " | " + Ticked(glitchPicks, AmountNames);
            }
        }

        private static string Ticked(List<MenuItem> picks, string[] names)
        {
            for (int i = 0; i < picks.Count; i++) if (picks[i].IsChecked) return names[i];
            return "?";
        }

        internal void SetPitch(int semitones)
        {
            VoiceFx.Pitch = Math.Max(-VoiceFx.MostPitch, Math.Min(VoiceFx.MostPitch, semitones));
            Saved.Pitch = VoiceFx.Pitch;
            Save();
            Sync();
        }

        internal void SetPace(int percent)
        {
            VoiceFx.Speed = Math.Max(VoiceFx.Slowest, Math.Min(VoiceFx.Fastest, percent));
            Saved.Pace = VoiceFx.Speed;
            Save();
            Sync();
        }

        internal void SetRobot(int amount)
        {
            VoiceFx.Robot = Math.Max(0, Math.Min(100, amount));
            Saved.Robot = VoiceFx.Robot;
            Save();
            Sync();
        }

        internal void SetGlitch(int amount)
        {
            VoiceFx.Glitch = Math.Max(0, Math.Min(100, amount));
            Saved.Glitch = VoiceFx.Glitch;
            Save();
            Sync();
        }

        // Picking a style: she says a line in it so they hear it, like on Windows. ElevenLabs says the line once per voice; after
        // that its audio is restyled here, so trying settings spends no more credits.
        internal void HearStyle()
        {
            if (!Speaks)
            {
                Talk.Answer("(voice check)", voice.Ready ? "My voice is off, choom. Settings > Voice > Use ElevenLabs turns it back on." : "Add an ElevenLabs key under Settings > Voice to hear that, choom.");
                return;
            }
            int ticket = ++asking;
            voice.Stop();
            string id = voice.VoiceId;
            byte[] said = styleVoice == id ? styleSaid : null;
            Talk.Thinking("(voice check)");
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string problem = null;
                try
                {
                    if (said == null) said = voice.Speak(StyleLine);
                    wav = VoiceFx.Styled(said, StyleLine);
                }
                catch (VoiceException error) { problem = Mac(error.Message); }
                catch (Exception error) { problem = Voice.Short("Voice glitched: " + error.Message); }
                Dispatcher.UIThread.Post(delegate
                {
                    if (said != null) { styleSaid = said; styleVoice = id; }
                    if (ticket != asking) return;
                    double seconds = wav == null ? 0 : Voice.Seconds(wav);
                    if (seconds < 0.2) { Talk.Answer("(voice check)", problem ?? StyleLine); return; }
                    Talk.Answer("(voice check)", StyleLine, seconds);
                    voice.Play(wav);
                });
            });
        }

        internal string VolumePicked
        {
            get
            {
                for (int i = 0; i < volumePicks.Count; i++) if (volumePicks[i].IsChecked) return VolumeNames[i];
                return null;
            }
        }

        internal void SetVolume(int percent)
        {
            Voice.Loudness = Math.Max(0, Math.Min(150, percent));
            Saved.Volume = Voice.Loudness;
            Save();
            Sync();
        }

        internal int BubbleTime { get { return Math.Max(0, Math.Min(2, Saved.Bubble ?? 1)); } }

        internal string BubblePicked
        {
            get
            {
                for (int i = 0; i < bubblePicks.Count; i++) if (bubblePicks[i].IsChecked) return BubbleNames[i];
                return null;
            }
        }

        internal void SetBubbleTime(int level)
        {
            Saved.Bubble = Math.Max(0, Math.Min(2, level));
            Save();
            Sync();
        }

        // Sit here, or Stand up while she sits where she was put, like Windows' Move menu.
        internal string SitChoice { get { return seat != null && seat.Free ? "Stand up" : "Sit here"; } }
        internal string NapChoice { get { return napAsked || clip == sleep ? "Wake up" : "Go to sleep"; } }

        internal int SleepAfter { get { return Saved.SleepAfter ?? 120; } }

        internal string SleepPicked
        {
            get
            {
                for (int i = 0; i < sleepPicks.Count; i++) if (sleepPicks[i].IsChecked) return SleepNames[i];
                return null;
            }
        }

        internal void SetSleepAfter(int seconds)
        {
            Saved.SleepAfter = Math.Max(0, Math.Min(3600, seconds));
            Save();
            Sync();
            if (seconds <= 0 && clip == sleep) Wake();
        }

        // She dozes off like on Windows when nobody's touched the mouse or keyboard for Fall asleep after, and only standing,
        // as there: sitting on a window she stays awake. Reading the idle time starts a process on Mac and Linux, so it's read
        // off her thread every 5 s awake and every second asleep, and a moved mouse wakes her at once on Mac and X11.
        internal void CheckSleep(double now)
        {
            if (napping) return;
            if (clip == sleep && sleptAt != null)
            {
                PixelPoint? mouse = Mouse();
                if (mouse != null && mouse.Value != sleptAt.Value) { Wake(); return; }
            }
            if (sleepChecking || now < nextSleepCheck) return;
            if (clip != sleep && (SleepAfter <= 0 || !Drowsy)) return;
            nextSleepCheck = now + (clip == sleep ? 1 : 5);
            sleepChecking = true;
            Func<double> read = Idle;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double idle = -1;
                try { idle = read(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate { sleepChecking = false; Doze(idle); });
            });
        }

        // What she does with how long the mouse and keyboard have been left alone (-1 when the desktop won't say). Asleep, a
        // shorter wait than the last reading means they're back.
        internal void Doze(double idle)
        {
            if (idle < 0) return;
            if (clip == sleep)
            {
                if (napping) return;
                if (idle < sleepIdle) Wake();
                else sleepIdle = idle;
                return;
            }
            if (SleepAfter <= 0 || idle < SleepAfter || !Drowsy) return;
            Play(sleep);
            sleepIdle = idle;
            sleptAt = Mouse();
            nextSleepCheck = Now + 1;
        }

        private void Wake()
        {
            sleptAt = null;
            Play(wake);
        }

        // "Go to sleep" from her menu, her brain or plain words, like on Windows: she gets down off her window, finishes what
        // she's saying, then sleeps standing. Unlike the doze she falls into by herself, the mouse and keyboard don't wake her.
        internal string Nap(bool sleeping)
        {
            if (!sleeping)
            {
                bool resting = napAsked || napping || clip == sleep;
                napAsked = false;
                napping = false;
                if (clip == sleep) Wake();
                Sync();
                return resting ? "Woke up." : "Already awake.";
            }
            ComeBack();
            if (seat != null || hanging) StandUp();
            EndWalk();
            dancing = false;
            napAsked = true;
            Sync();
            return "Going to sleep. A click on me wakes me up.";
        }

        // Her nap tool, which her brain calls from its own thread.
        private string NapTool(bool sleeping) { return Dispatcher.UIThread.CheckAccess() ? Nap(sleeping) : Dispatcher.UIThread.Invoke(new Func<string>(delegate { return Nap(sleeping); })); }

        // The nap they asked for starts once her line is done and she stands free, and ends when anything wakes her.
        internal void CheckNap()
        {
            if (napping && clip != sleep) napping = false;
            if (!napAsked || clip != idle || seat != null || walking || dragging || pressed || falling || outro != null || away || quitting || !Talk.Quiet) return;
            napAsked = false;
            napping = true;
            sleptAt = null;
            Play(sleep);
        }

        internal bool Napping { get { return napping; } }

        // Her own moves, like on Windows: "teleport here" glitches her over to their mouse, "go to my other monitor" to the same
        // spot on another screen, and she sits, stands, hangs, hides and comes back. Riding the cursor and the Mainframe are
        // Windows-only so far.
        internal string MoveSelf(string action, string screen)
        {
            if (action == "move_mainframe") return "My Mainframe is only on Windows so far. Here my settings are in my right-click menu.";
            if (action == "ride_cursor" || action == "hop_off") return "Riding your cursor is only on Windows so far.";
            if (action == "hide")
            {
                if (away) return "Already hidden.";
                hideAsked = true;
                return "Hiding. Pick Show her in my tray menu to bring me back.";
            }
            hideAsked = false;
            if (action == "show")
            {
                if (!away) return "I'm right here.";
                ComeBack();
                return "Back.";
            }
            napAsked = false;
            napping = false;
            if (action == "come_here")
            {
                PixelPoint? mouse = MouseAt != null ? MouseAt() : Mouse();
                if (mouse == null) return "Couldn't find your mouse on this desktop.";
                Teleport(mouse.Value);
                return "Teleporting to your mouse.";
            }
            if (action == "other_screen")
            {
                IReadOnlyList<Screen> all = Screens.All;
                Screen from = ScreenHere();
                int to = PickScreen(all, from, screen);
                if (to < 0) return all.Count < 2 ? "There's no other screen to go to." : "There's no screen like that to go to.";
                if (from != null && all[to].Bounds == from.Bounds) return "I'm already on " + ScreenName(all, to) + ".";
                Teleport(Across(Hip, from != null ? from.WorkingArea : Area, all[to].WorkingArea));
                return "Moving to " + ScreenName(all, to) + ".";
            }
            ComeBack();
            if (action == "sit")
            {
                if (seat != null && seat.Free) return "Already sitting.";
                SitHere();
                return "Sitting right here.";
            }
            if (action == "stand_up")
            {
                if (hanging) { LetGo(); return "Let go."; }
                if (seat == null) return "Already standing.";
                StandUp();
                return "Standing up.";
            }
            if (action == "hang")
            {
                if (hanging) return "Already hanging.";
                HangHere();
                return "Hanging right here.";
            }
            return "Couldn't move like that.";
        }

        private string MoveTool(string action, string screen) { return Dispatcher.UIThread.CheckAccess() ? MoveSelf(action, screen) : Dispatcher.UIThread.Invoke(new Func<string>(delegate { return MoveSelf(action, screen); })); }

        // She glitches out and back in with her hip on the point, kept on its screen, then falls onto what's under her.
        internal void Teleport(PixelPoint to)
        {
            PixelRect area = WorkAt(to);
            Action arrive = delegate
            {
                if (quitting) return;
                if (away) SetAway(false);
                if (!IsVisible) { Show(); Look.OnTopAgain(this); }
                walking = false;
                seat = null;
                falling = false;
                hanging = false;
                PixelPoint spot = Seated(to.X, to.Y);
                int wide = Px(body.Size);
                Move(new PixelPoint(Math.Max(area.X, Math.Min(spot.X, area.Right - wide)), Math.Max(area.Y, Math.Min(spot.Y, area.Bottom - wide))));
                Arrive();
                Raise(Now + 6);
            };
            if (away) arrive();
            else GlitchOut(arrive, true);
        }

        // Which of their screens "next", "left", "main", "here" or "2" means, from the given one, or -1. Screens count from the left.
        private int PickScreen(IReadOnlyList<Screen> all, Screen from, string which)
        {
            int[] x = new int[all.Count], y = new int[all.Count];
            int primary = 0, current = -1, pointed = -1;
            PixelPoint? mouse = MouseAt != null ? MouseAt() : Mouse();
            for (int i = 0; i < all.Count; i++)
            {
                PixelRect bounds = all[i].Bounds;
                x[i] = bounds.X + bounds.Width / 2;
                y[i] = bounds.Y + bounds.Height / 2;
                if (all[i].IsPrimary) primary = i;
                if (from != null && bounds == from.Bounds) current = i;
                if (mouse != null && bounds.Contains(mouse.Value)) pointed = i;
            }
            return Hands.PickScreen(x, y, null, primary, current, pointed, which);
        }

        private static string ScreenName(IReadOnlyList<Screen> all, int index)
        {
            if (all[index].IsPrimary) return "your main screen";
            if (all.Count == 2) return "your other screen";
            int place = 1;
            foreach (Screen other in all) if (other.Bounds.X < all[index].Bounds.X || (other.Bounds.X == all[index].Bounds.X && other.Bounds.Y < all[index].Bounds.Y)) place++;
            return "screen " + place;
        }

        // The same spot on another screen: as far across and down its working area as it was on the first.
        internal static PixelPoint Across(PixelPoint at, PixelRect from, PixelRect to)
        {
            double across = from.Width > 0 ? Math.Max(0, Math.Min(1, (at.X - from.X) / (double)from.Width)) : 0.5;
            double down = from.Height > 0 ? Math.Max(0, Math.Min(1, (at.Y - from.Y) / (double)from.Height)) : 0.5;
            return new PixelPoint(to.X + (int)Math.Round(across * to.Width), to.Y + (int)Math.Round(down * to.Height));
        }

        // A hide they asked for waits until she's said her line.
        internal void CheckHide()
        {
            if (!hideAsked || away || dragging || pressed || quitting || !Talk.Quiet) return;
            hideAsked = false;
            HideAway();
        }

        // Her animations by name, like on Windows. Her dance is her Move menu's Dance, on until "stop dancing".
        internal string Animate(string name)
        {
            if (name == "stop_dancing")
            {
                bool was = dancing || clip == dance || clip == perchDance;
                danceHeld = musicOn;
                Dancing = false;
                if (clip == dance || clip == perchDance) Play(clip == perchDance ? perch : idle);
                return was ? "Stopped dancing." : "I wasn't dancing.";
            }
            napAsked = false;
            napping = false;
            ComeBack();
            if (hanging) LetGo();
            if (name == "dance")
            {
                danceHeld = false;
                Dancing = true;
                return "Dancing. Tell me to stop when you've had enough.";
            }
            bool perched = seat != null;
            if (name == "idle")
            {
                danceHeld = musicOn;
                Dancing = false;
                Play(perched ? perch : idle);
            }
            else if (name == "glitch") Play(perched ? perchGlitch : glitch);
            else if (name == "grin")
            {
                // Her grin is a standing one, so she gets up for it.
                if (perched) StandUp();
                Play(grin);
            }
            else if (name == "blink")
            {
                Play(perched ? perch : idle);
                blinkWith = perched ? perchBlink : blink;
            }
            else return "I don't have an animation called " + name + ".";
            return name == "idle" ? "Back to my idle." : name == "glitch" ? "Glitched." : name == "grin" ? "Grinning." : "Blinked.";
        }

        private string AnimateTool(string name) { return Dispatcher.UIThread.CheckAccess() ? Animate(name) : Dispatcher.UIThread.Invoke(new Func<string>(delegate { return Animate(name); })); }

        // Which of her animations is showing, by name, for the smoke test.
        internal string Playing
        {
            get
            {
                Clip[] named = { idle, glitch, grin, dance, perchDance, dangle, perch, perchGlitch, arrive, leave, sleep, wake };
                string[] names = { "idle", "glitch", "grin", "dance", "perch_music", "dangle", "perch", "perch_glitch", "arrive", "leave", "sleep", "wake" };
                int at = Array.IndexOf(named, clip);
                return at < 0 ? "other" : names[at];
            }
        }

        internal bool Blinking { get { return blinkWith != null; } }

        private bool Drowsy { get { return clip == idle && seat == null && !walking && !dragging && !pressed && !falling && outro == null && !away && !quitting && Talk.Quiet && (ContextMenu == null || !ContextMenu.IsOpen); } }

        internal bool Shushed { get { return shushed; } }
        internal bool QuietShown { get { return quietItem.IsChecked; } }

        // Quiet or a nap they asked for: no glances, pop-ins, check-ins, cheers or jokes.
        private bool Hushed { get { return shushed || napAsked || napping; } }

        // Quiet, like Windows' quiet key, for when they focus or game: she stops what she's saying and doesn't glance, pop in,
        // check in, cheer or joke until it's turned off, but still answers when they talk to her. Starting her again ends it.
        internal void ToggleShush()
        {
            shushed = !shushed;
            nextPopIn = -1;
            nextGlance = -1;
            ForgetNudges();
            if (shushed) voice.Stop();
            Talk.Answer("(quiet)", shushed ? "Going quiet. Right-click me > Quiet when you want me back." : "I'm back, choom.");
            Sync();
            if (ShushChanged != null) ShushChanged();
        }

        internal bool StartsAtLogin { get { return File.Exists(LoginFile); } }
        internal bool LoginShown { get { return loginItem.IsChecked; } }

        internal void SetLogin(bool on)
        {
            if (!Limbs.SetLogin(LoginFile, Environment.ProcessPath, on, OperatingSystem.IsMacOS())) Talk.Answer("(start at login)", "Couldn't change that, choom. I need to be able to write " + LoginFile + ".");
            Sync();
        }

        internal bool DiscordShown { get { return discordItem.IsChecked == true; } }

        internal string Picked
        {
            get
            {
                List<string> on = new List<string>();
                foreach (List<MenuItem> group in new[] { wanderPicks, languagePicks, replyPicks, recallPicks, soundPicks })
                    foreach (MenuItem pick in group) if (pick.IsChecked) on.Add(pick.Header.ToString());
                if (musicItem.IsChecked) on.Add("Dance to music");
                if (danceItem.IsChecked) on.Add("Dance");
                return string.Join(" | ", on);
            }
        }

        internal void SetDanceToMusic(bool on)
        {
            Saved.NoDance = !on;
            Save();
            Sync();
        }

        internal bool IntroShown { get { return introItem.IsChecked; } }
        internal bool OnTop { get { return !Saved.NotOnTop; } }
        internal bool OnTopShown { get { return onTopItem.IsChecked; } }
        internal bool Raised { get { return raisedUntil >= 0; } }

        internal void SetOnTop(bool on)
        {
            Saved.NotOnTop = !on;
            raisedUntil = -1;
            Stack(on);
            Save();
            Sync();
        }

        // Her bubble, typing box and pill go with her, like on Windows.
        private void Stack(bool above)
        {
            Topmost = above;
            foreach (Window window in new Window[] { Talk.Bubble, Talk.Typer, Talk.Pill }) window.Topmost = above;
        }

        internal void Raise(double until)
        {
            if (OnTop) return;
            raisedUntil = until;
            raisedAsked = asking;
            raisedClicks = clicks;
            raisedPets = petTicket;
            Stack(true);
        }

        // Back to an ordinary window once her line's done and the time's up, or as soon as they touch her or talk to her: she stays
        // in front of what's there now until another window comes up, like Windows' pop-ins.
        internal void CheckRaised(double now)
        {
            if (raisedUntil < 0) return;
            bool touched = asking != raisedAsked || clicks != raisedClicks || petTicket != raisedPets || dragging || pressed || Talk.Typing;
            if (!touched && (now < raisedUntil || !Talk.Quiet)) return;
            raisedUntil = -1;
            Stack(OnTop);
        }

        internal void SetIntro(bool on)
        {
            Saved.NoIntro = !on;
            Save();
            Sync();
        }

        private void CheckMusic(double now)
        {
            if (Saved.NoDance || musicBusy || now < musicAt || Program.Smoke != null) return;
            musicAt = now + 2;
            musicBusy = true;
            Func<bool> playing = MusicPlaying;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool on = false;
                try { on = playing(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate { musicBusy = false; HearMusic(on); });
            });
        }

        internal void HearMusic(bool on)
        {
            musicOn = on;
            if (!on) danceHeld = false;
        }

        internal void SetWander(int level)
        {
            Saved.Wander = Math.Max(0, Math.Min(4, level));
            nextWander = -1;
            Save();
            Sync();
        }

        internal void AskNickname()
        {
            ComeBack();
            Talk.AskFor(NameHeard, Brain.Nickname == null ? "What should I call you? Type a name and press Enter." : "I call you " + Brain.Nickname + ". Type a new name and press Enter, or choom to go back.", SetNickname);
        }

        internal void SetNickname(string typed)
        {
            string name = Brain.Nick(typed);
            Brain.Nickname = name.Length == 0 ? null : name;
            Saved.Nickname = Brain.Nickname;
            Save();
            Reply(++asking, NameHeard, Brain.Renamed(name));
        }

        // Get to know me, like on Windows: she asks what to call them, their pronouns and what they're into in her typing box, and
        // writes the answers to me.md. Their name is what she calls them too.
        internal void GetToKnow()
        {
            meetAt = -1;
            ComeBack();
            hideAsked = false;
            ++asking;
            Talk.AskFor("(get to know me)", meeting.Start(), Met);
        }

        internal bool GettingToKnow { get { return meeting.Asking; } }

        private void Met(string said)
        {
            string called;
            string next = meeting.Take(said, out called);
            if (next == null) { Hear(said); return; }
            if (called != null)
            {
                Brain.Nickname = called.Length == 0 ? null : called;
                Saved.Nickname = Brain.Nickname;
                Save();
            }
            if (meeting.Asking) { ++asking; Talk.AskFor(said, next, Met); }
            else Reply(++asking, said, next);
        }

        // On a new install, once she's said hello and her bubble's gone, she gets to know them.
        private void CheckMeet(double now)
        {
            if (meetAt < 0 || now < meetAt || away || dragging || pressed || outro != null || quitting || napAsked || napping || !Talk.Quiet) return;
            meetAt = -1;
            GetToKnow();
        }

        internal void SetLanguage(string language)
        {
            Brain.Language = Brain.LanguageCode(language) == "en" ? null : language;
            Saved.Language = Brain.Language;
            Brain.Forget();
            Save();
            Sync();
        }

        internal void SetReplies(int level)
        {
            Brain.Wordiness = Math.Max(0, Math.Min(2, level));
            Saved.Replies = Brain.Wordiness;
            Save();
            Sync();
        }

        internal void SetRecall(int count)
        {
            Brain.Recall = count == 16 || count == 32 ? count : 8;
            Saved.Recall = Brain.Recall;
            Save();
            Sync();
        }

        internal void SetSounds(int level)
        {
            Sfx.Level = Math.Max(0, Math.Min(100, level));
            Saved.Sounds = Sfx.Level;
            Save();
            Sync();
            Sfx.Play(Cue.Hop);
        }

        private void RefreshBrains()
        {
            if (Brain.Remote != null) { ShowCloud(); ListCloud(); return; }
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] found = null;
                try { found = Brain.InstalledModels(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate { ShowBrains(found); });
            });
        }

        internal void ShowBrains(string[] found)
        {
            if (Brain.Remote != null) { ShowCloud(); return; }
            brainMenu.Items.Clear();
            if (found == null) brainMenu.Items.Add(new MenuItem { Header = NoOllama, IsEnabled = false });
            else if (found.Length == 0)
            {
                MenuItem fetch = new MenuItem { Header = pulling ? "Downloading " + Starter + (pulled >= 0 ? ": " + pulled + "%" : "...") : PullChoice, IsEnabled = !pulling };
                fetch.Click += delegate { PullBrain(); };
                brainMenu.Items.Add(fetch);
            }
            else
                foreach (string name in found)
                {
                    string model = name;
                    MenuItem pick = new MenuItem { Header = model, ToggleType = MenuItemToggleType.Radio, GroupName = "brain", IsChecked = model == Brain.Model };
                    pick.Click += delegate { PickBrain(model); };
                    brainMenu.Items.Add(pick);
                }
            // A cloud key thinks for her instead, for PCs that can't run Ollama's models well. NVIDIA's is free.
            brainMenu.Items.Add(new Separator());
            MenuItem free = new MenuItem { Header = "Get a free NVIDIA key" };
            free.Click += delegate { GetFreeKey(); };
            MenuItem paste = new MenuItem { Header = KeyChoice };
            paste.Click += delegate { AskBrainKey(); };
            brainMenu.Items.Add(free);
            brainMenu.Items.Add(paste);
        }

        // On a cloud key the Brain menu lists that key's chat models, and can go back to Ollama.
        private void ShowCloud()
        {
            Cloud remote = Brain.Remote;
            if (remote == null) return;
            brainMenu.Items.Clear();
            brainMenu.Items.Add(new MenuItem { Header = remote.Name + " key ending " + Voice.Tail(remote.Key), IsEnabled = false });
            if (cloudModels == null) brainMenu.Items.Add(new MenuItem { Header = "Loading models...", IsEnabled = false });
            else
                foreach (string name in cloudModels)
                {
                    string model = name;
                    MenuItem pick = new MenuItem { Header = Brain.Shown(remote.Kind, model), ToggleType = MenuItemToggleType.Radio, GroupName = "cloud", IsChecked = model == remote.Model };
                    pick.Click += delegate { PickCloud(model); };
                    brainMenu.Items.Add(pick);
                }
            brainMenu.Items.Add(new Separator());
            if (remote.Kind == "nvidia")
            {
                MenuItem stay = new MenuItem { Header = "Stay on my model", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Saved.StayOnModel };
                stay.Click += delegate { SetStay(!Saved.StayOnModel); };
                brainMenu.Items.Add(stay);
            }
            MenuItem back = new MenuItem { Header = "Forget the key and use Ollama" };
            back.Click += delegate { ForgetBrainKey(); };
            brainMenu.Items.Add(back);
        }

        private void ListCloud()
        {
            Cloud remote = Brain.Remote;
            if (remote == null || cloudListing || cloudModels != null) return;
            cloudListing = true;
            string key = remote.Key;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> found = null;
                try { found = Brain.CloudModels(key); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    cloudListing = false;
                    if (found == null || Brain.Remote == null || Brain.Remote.Key != key) return;
                    cloudModels = found;
                    ShowCloud();
                });
            });
        }

        // The model rows at the top of the Brain menu, above its key choices.
        internal int BrainChoices
        {
            get
            {
                int count = 0;
                foreach (object item in brainMenu.Items) { if (item is Separator) break; count++; }
                return count;
            }
        }

        internal string FirstBrainChoice { get { MenuItem first = brainMenu.Items.Count > 0 ? brainMenu.Items[0] as MenuItem : null; return first == null ? null : first.Header as string; } }

        internal string BrainMenu
        {
            get
            {
                List<string> shown = new List<string>();
                foreach (object item in brainMenu.Items)
                {
                    MenuItem entry = item as MenuItem;
                    if (entry == null) shown.Add("-");
                    else shown.Add((entry.IsChecked ? "*" : "") + entry.Header + (entry.IsEnabled ? "" : " (off)"));
                }
                return string.Join(" | ", shown);
            }
        }

        // Her Security menu as it shows, for the smoke test: its header, then each preset and toggle with a star when it's
        // ticked, like "Security: Trusted | Lockdown | Guarded | *Trusted | Blackwall | Custom (off) | - | *Open apps, games and sites (On: ...)".
        internal string SecurityShown
        {
            get
            {
                List<string> shown = new List<string> { securityMenu.Header as string };
                foreach (object item in securityMenu.Items)
                {
                    MenuItem entry = item as MenuItem;
                    if (entry == null) { shown.Add("-"); continue; }
                    StackPanel face = entry.Header as StackPanel;
                    string name = face == null ? entry.Header as string : ((TextBlock)face.Children[0]).Text + " (" + ((TextBlock)face.Children[1]).Text + ")";
                    shown.Add((entry.IsChecked ? "*" : "") + name + (entry.IsEnabled ? "" : " (off)"));
                }
                return string.Join(" | ", shown);
            }
        }

        // Clicks a preset (0 to 3) or, with preset -1, a toggle in her Security menu, the way a click does.
        internal void ClickSecurity(int preset, int toggle)
        {
            MenuItem item = preset >= 0 ? securityPicks[preset] : securityToggles.Find(delegate(MenuItem entry) { return (int)entry.Tag == toggle; });
            item.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        }

        // Ollama has no models yet: she pulls her own brain, shows how far it's got on the Brain menu, then picks it and says so.
        internal void PullBrain()
        {
            if (pulling) return;
            pulling = true;
            pulled = -1;
            Talk.Answer(BrainHeard, "Downloading my brain, " + Starter + ". It's a few GB, so give me a while. Right-click me > Brain shows how far it's got.");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = null;
                try { Brain.Pull(Starter, delegate(string status, long done, long total) { pulled = (int)(done * 100 / Math.Max(1, total)); }, delegate { return false; }); }
                catch (Exception error)
                {
                    System.Net.WebException web = error as System.Net.WebException;
                    problem = web != null && web.Response == null ? NoOllama : Brain.Plain(error.Message) ?? "Couldn't download my brain: " + Voice.Short(error.Message);
                }
                Dispatcher.UIThread.Post(delegate
                {
                    pulling = false;
                    pulled = -1;
                    if (problem != null) { Talk.Answer(BrainHeard, problem); return; }
                    Brain.Model = Starter;
                    Saved.Model = Starter;
                    Save();
                    Talk.Answer(BrainHeard, "My brain's in. Talk to me, choom.");
                });
            });
        }

        internal void PickBrain(string model)
        {
            if (string.IsNullOrEmpty(model) || model == Brain.Model) return;
            Brain.Cancel();
            // Her chat carries on with the new model, so changing brains never wipes what she was doing.
            Brain.Model = model;
            Saved.Model = model;
            Save();
        }

        internal string OpenNotes()
        {
            string path = Brain.MemoryPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                if (!File.Exists(path)) File.WriteAllText(path, Brain.NotesHead);
            }
            catch (Exception) { return null; }
            if (Program.Smoke == null) Browse(path);
            return path;
        }

        // Off, she stops writing transcripts, folding and reading memories.md. Nothing gets deleted.
        internal void SetRemembering(bool on)
        {
            Saved.NoMemories = !on;
            Brain.Keeping = on;
            Save();
            Sync();
        }

        internal bool Remembers { get { return memoriesItem.IsChecked; } }

        // memories.md opens in their editor, made first if it isn't there yet. Her transcripts are in the folder beside it.
        internal string OpenMemories()
        {
            string path = Brain.MemoriesPath;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                Directory.CreateDirectory(Brain.TranscriptPath);
                if (!File.Exists(path)) File.WriteAllText(path, Brain.MemoriesHead);
            }
            catch (Exception) { return null; }
            if (Program.Smoke == null) Browse(path);
            return path;
        }

        internal static int WalkTarget(int at, int from, int to, int size, double side, double length)
        {
            int shortest = size / 2;
            int longest = size * 3;
            bool right = side < 0.5;
            if ((right ? to - at : at - from) < shortest) right = !right;
            int room = Math.Min(longest, right ? to - at : at - from);
            if (room < shortest) return at;
            int step = shortest + (int)((room - shortest) * length);
            return right ? at + step : at - step;
        }

        internal bool Stroll()
        {
            if (walking || falling || dragging || outro != null || seat == null || seat.Free) return false;
            PixelPoint hip = Hip;
            PixelRect area = WorkAt(hip);
            if (seat.Y - Px(body.Size * StandY) < ScreenAt(hip).Y) return false;
            int from = Math.Max(seat.Left, area.X) + Px(body.Size * 0.1) + Px(8);
            int to = Math.Min(seat.Right, area.Right) - Px(body.Size * 0.41) - Px(8);
            int target = WalkTarget(hip.X, from, to, Px(body.Size), random.NextDouble(), random.NextDouble());
            if (target == hip.X) return false;
            PixelPoint standing = Standing(hip.X, seat.Y);
            walkTo = standing.X + target - hip.X;
            walking = true;
            Move(standing);
            walkStep = Now;
            walkCarry = 0;
            Play(walk);
            return true;
        }

        private void Walk(double now)
        {
            double step = Math.Max(0, Math.Min(0.05, now - walkStep));
            walkStep = now;
            int gap = walkTo - anchor.X;
            walkCarry += body.Size * 0.42 * step * DesktopScaling;
            int stride = (int)walkCarry;
            walkCarry -= stride;
            if (Math.Abs(gap) <= stride) { Move(new PixelPoint(walkTo, anchor.Y)); EndWalk(); return; }
            Move(new PixelPoint(anchor.X + Math.Sign(gap) * stride, anchor.Y));
        }

        private void EndWalk()
        {
            if (!walking) return;
            PixelPoint hip = Hip;
            walking = false;
            if (seat != null) { Move(Seated(hip.X, seat.Y)); Play(perchGlitch); }
            else Play(idle);
            Save();
        }

        private void CheckWander(double now)
        {
            if (Saved.Wander <= 0) { nextWander = -1; return; }
            if (dragging || pressed || falling || walking || outro != null || quitting || seat == null || seat.Free || !Talk.Quiet || napAsked) return;
            double pace = WanderPaces[Saved.Wander - 1];
            if (nextWander < 0) nextWander = now + (20 + random.NextDouble() * 40) * pace;
            if (now < nextWander) return;
            nextWander = now + (45 + random.NextDouble() * 75) * pace;
            if (random.NextDouble() < 0.5 && Blink()) return;
            Wander();
        }

        internal bool Blink()
        {
            if (walking || falling || dragging || outro != null || quitting || seat == null || seat.Free) return false;
            PixelPoint hip = Hip;
            PixelRect area = WorkAt(hip);
            int from = Math.Max(seat.Left, area.X) + Px(body.Size * 0.1) + Px(8);
            int to = Math.Min(seat.Right, area.Right) - Px(body.Size * 0.41) - Px(8);
            int target = WalkTarget(hip.X, from, to, Px(body.Size), random.NextDouble(), random.NextDouble());
            if (target == hip.X) return false;
            Ledge spot = seat;
            GlitchOut(delegate { Hop(spot, target); }, true);
            return true;
        }

        internal bool Wander()
        {
            if (outro != null || quitting) return false;
            List<Ledge> spots = new List<Ledge>();
            foreach (Ledge ledge in Ledges())
            {
                if (ledge.Right - ledge.Left < Px(body.Size / 2)) continue;
                if (seat != null && ledge.Window == seat.Window && ledge.Floor == seat.Floor && ledge.Y == seat.Y) continue;
                if (ledge.Y - Headroom < ScreenAt(new PixelPoint(ledge.Left, ledge.Y)).Y) continue;
                spots.Add(ledge);
            }
            if (spots.Count == 0) return false;
            Ledge spot = spots[random.Next(spots.Count)];
            int margin = Px(body.Size / 4);
            int x = spot.Left + margin + random.Next(Math.Max(1, spot.Right - spot.Left - 2 * margin));
            GlitchOut(delegate { Hop(spot, x); }, true);
            return true;
        }

        private void Hop(Ledge spot, int x)
        {
            walking = false;
            seat = null;
            Move(Seated(x, spot.Y));
            Land(spot);
            if (seat == spot) Play(perchArrive);
            else Arrive(true);
        }

        internal void GlitchOut(Action then, bool hop = false)
        {
            EndWalk();
            falling = false;
            hanging = false;
            fallTo = null;
            dropAfter = false;
            Play(seat != null ? perchLeave : leave);
            Sfx.Play(hop ? Cue.Hop : Cue.Leave);
            outro = then;
            outroAt = clipStart + (clip.Count + 1) / clip.Fps;
        }

        private void Halt()
        {
            if (outro == null || quitting) return;
            outro = null;
            if (clip == leave || clip == perchLeave) Play(seat != null ? perch : idle);
        }

        internal void CheckPopIn(double now)
        {
            // Touching her, typing to her, opening her menu or asking her something while she's out from hiding keeps her out. Clicks
            // and pats are counted, since a quick click is over before this looks.
            if (popOut >= 0 && (asking != popAsked || clicks != popClicks || petTicket != popPets || dragging || pressed || Talk.Typing || (ContextMenu != null && ContextMenu.IsOpen))) popOut = -1;
            if (popOut >= 0 && now >= popOut && Talk.Quiet) { popOut = -1; HideAway(); }
            if (Saved.NoCheckIns || Hushed) { nextPopIn = -1; return; }
            if (nextPopIn < 0) nextPopIn = now + PopGap();
            if (now < nextPopIn || popChecking) return;
            nextPopIn = now + PopGap();
            if (!CanPop) return;
            popChecking = true;
            Func<double> read = Idle;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double idle = -1;
                try { idle = read(); }
                catch (Exception) { }
                // Only while someone's at the computer, like on Windows. A desktop that doesn't say counts as someone there.
                Dispatcher.UIThread.Post(delegate { popChecking = false; if (idle <= 60 && CanPop && !Hushed && !Saved.NoCheckIns) PopIn(); });
            });
        }

        private bool CanPop { get { return !quitting && !dragging && !pressed && !falling && outro == null && popOut < 0 && nudgeOut < 0 && Talk.Quiet && (ContextMenu == null || !ContextMenu.IsOpen); } }

        // She glitches out if she's showing, then in at a random spot along the top of her screen, says hi and drops onto a window.
        internal void PopIn()
        {
            if (quitting) return;
            bool hidden = away;
            string line = PopLines[random.Next(PopLines.Length)];
            // With Stay put, or on a Sit here spot, she says hi from where she is.
            if (!hidden && (Saved.Wander <= 0 || (seat != null && seat.Free)))
            {
                Blurt(PopHeard, line);
                popAsked = asking;
                popClicks = clicks;
                popPets = petTicket;
                popOut = -1;
                Raise(Now + 6);
                return;
            }
            Action arrive = delegate
            {
                Screen screen = ScreenHere();
                if (screen == null || quitting) return;
                // Only coming out of hiding restarts her wait to ask for attention: a pop-in nobody touched isn't them noticing her.
                if (away) SetAway(false);
                if (!IsVisible) { Show(); Look.OnTopAgain(this); }
                walking = false;
                seat = null;
                falling = false;
                PixelRect area = screen.WorkingArea;
                Move(new PixelPoint(area.X + random.Next(Math.Max(1, area.Width - Px(body.Size))), area.Y));
                Arrive();
                Blurt(PopHeard, line);
                popAsked = asking;
                popClicks = clicks;
                popPets = petTicket;
                popOut = hidden ? Now + 6 : -1;
                Raise(Now + 6);
            };
            if (hidden) arrive();
            else GlitchOut(arrive);
        }

        // A line she says by herself, typed out like her replies. It leaves asking alone, so it never counts as them asking her something.
        private void Blurt(string heard, string line)
        {
            Talk.Answer(heard, line);
            TypeAlong(line, asking);
        }

        // Free for a line of her own: Check in on me on, showing, and not busy with them.
        internal bool Free { get { return !Saved.NoCheckIns && !Hushed && !away && CanPop; } }

        private void CheckHome(double now)
        {
            if (Saved.NoCheckIns || homeBusy || now - homeAt < 15) return;
            homeBusy = true;
            homeAt = now;
            ThreadPool.QueueUserWorkItem(delegate
            {
                bool game = false, song = false;
                try
                {
                    game = Limbs.CyberpunkRunning();
                    foreach (string playing in Music.Songs()) if (Hands.NightCity(playing)) song = true;
                }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    homeBusy = false;
                    string line = Cheer(game, song, Now, Free);
                    if (line != null) Blurt(Array.IndexOf(HomeGameLines, line) >= 0 ? "(Cyberpunk 2077)" : "(Night City song)", line);
                });
            });
        }

        // Her cheer when the game starts or a Night City song comes on and she's free, or null. One she misses while busy waits for the next time.
        internal string Cheer(bool game, bool song, double now, bool free)
        {
            bool started = game && !homeGame, cameOn = song && !homeSong;
            homeGame = game;
            homeSong = song;
            if (!free) return null;
            if (started && now - gameCheered >= HomeGap) { gameCheered = now; return HomeGameLines[random.Next(HomeGameLines.Length)]; }
            if (cameOn && now - songCheered >= HomeGap) { songCheered = now; return HomeSongLines[random.Next(HomeSongLines.Length)]; }
            return null;
        }

        internal void ForgetCheers()
        {
            homeGame = homeSong = false;
            gameCheered = songCheered = -HomeGap;
        }

        private void CheckLag(double now)
        {
            if (now - lagAt < 15) return;
            lagAt = now;
            long idle, total;
            if (!Limbs.CpuTimes(out idle, out total)) return;
            double busy = lagTotal == 0 || total <= lagTotal ? 0 : 1 - (double)(idle - lagIdle) / (total - lagTotal);
            lagIdle = idle;
            lagTotal = total;
            string line = Lagging(busy, now, Free && !FullScreen());
            if (line != null) Blurt("(CPU near 100%)", line);
        }

        // Her joke once the CPU has been over 90% busy four checks in a row, about a minute, while she's free, or null.
        internal string Lagging(double busy, double now, bool free)
        {
            lagHot = busy >= 0.9 ? lagHot + 1 : 0;
            if (lagHot < 4 || !free || now - lagSaid < LagGap) return null;
            lagHot = 0;
            lagSaid = now;
            return LagLines[random.Next(LagLines.Length)];
        }

        internal void ForgetLag()
        {
            lagHot = 0;
            lagSaid = -LagGap;
        }

        private bool FullScreen()
        {
            Desk.Screens.Clear();
            foreach (Screen screen in Screens.All) Desk.Screens.Add(screen.Bounds);
            return Desk.Covering(Windows());
        }

        internal void CheckNudge(double now)
        {
            bool attending = clicks != noticedClicks || asking != noticedAsked || petTicket != noticedPets || dragging || pressed || Talk.Typing || (ContextMenu != null && ContextMenu.IsOpen);
            if (noticed < 0 || attending) { noticed = now; noticedClicks = clicks; noticedAsked = asking; noticedPets = petTicket; }
            if (nudgeOut >= 0)
            {
                if (attending)
                {
                    nudgeOut = -1;
                    nudgeGap = NudgeGap;
                    // A click, a pat, a drag or her menu gets a happy line; talking to her gets her answer instead.
                    if (!Talk.Typing && asking == nudgeAsked) Blurt("(you noticed)", NoticedLines[random.Next(NoticedLines.Length)]);
                }
                else if (now >= nudgeOut && Talk.Quiet)
                {
                    nudgeOut = -1;
                    nudgeGap = Math.Min(nudgeGap * 2, NudgeGap * 4);
                    noticed = now;
                    if (!away) Snub();
                }
                return;
            }
            if (Saved.NoCheckIns || Hushed || away || nudgeChecking || now < nudgeLook || now - noticed < nudgeGap || !CanPop || (clip != idle && clip != perch) || (lastGlance >= 0 && now - lastGlance < 300)) return;
            nudgeChecking = true;
            Func<double> read = Idle;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double idle = -1;
                try { idle = read(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    nudgeChecking = false;
                    // Only while they're using the computer, like on Windows. Away, she looks again in a minute.
                    if (idle > 30) { nudgeLook = Now + 60; return; }
                    if (Free && Now - noticed >= nudgeGap) Nudge(Now, Mouse());
                });
            });
        }

        private PixelPoint? Mouse()
        {
            try { return Desk.Pointer(); }
            catch (Exception) { return null; }
        }

        // She asks where she is when she's near the mouse or the desktop won't say where it is; otherwise she glitches over beside it.
        internal void Nudge(double now, PixelPoint? mouse)
        {
            string line = NudgeLines[random.Next(NudgeLines.Length)];
            PixelPoint hip = Hip;
            PixelRect area = mouse == null ? Area : WorkAt(mouse.Value);
            int wide = Px(body.Size);
            if (mouse == null || Saved.Wander <= 0 || (seat != null && seat.Free) || (area.Contains(hip) && Math.Abs(hip.X - mouse.Value.X) <= Math.Max(Px(300), wide * 2))) { Plead(now, line); return; }
            int x = mouse.Value.X - area.X < area.Width / 2 ? mouse.Value.X + wide / 2 : mouse.Value.X - wide - wide / 2;
            x = Math.Max(area.X, Math.Min(area.Right - wide, x));
            // Her wait to be noticed starts once she's there and has asked: a glitch-out cut short, by her window closing say, never asked.
            GlitchOut(delegate
            {
                walking = false;
                seat = null;
                falling = false;
                Move(new PixelPoint(x, area.Y));
                Arrive();
                Plead(Now, line);
            });
        }

        private void Plead(double now, string line)
        {
            nudgeOut = now + 20;
            nudgeAsked = asking;
            Blurt(NudgeHeard, line);
            Raise(now + 20);
        }

        // Like on Windows, she only sulks out loud when someone's touched the computer in the last 20 s; she waits twice as long either way.
        private void Snub()
        {
            Func<double> read = Idle;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double idle = -1;
                try { idle = read(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate { if (idle < 20 && !away && outro == null && Talk.Quiet) Blurt("(ignored)", SnubbedLines[random.Next(SnubbedLines.Length)]); });
            });
        }

        internal void ForgetNudges()
        {
            noticed = -1;
            nudgeOut = -1;
            nudgeGap = NudgeGap;
            noticedClicks = clicks;
            noticedAsked = asking;
            noticedPets = petTicket;
        }

        internal double NudgeOut { get { return nudgeOut; } }
        internal double Noticed { get { return noticed; } }
        internal double NudgeWait { get { return nudgeGap; } }

        internal int GlanceGap { get { return Saved.GlanceGap > 0 ? Saved.GlanceGap : 120; } }
        internal bool Glancing { get { return glancing; } }
        internal bool WatchShown { get { return watchItem.IsChecked; } }
        internal string GapShown
        {
            get
            {
                for (int i = 0; i < gapPicks.Count; i++) if (gapPicks[i].IsChecked) return GlanceGapNames[i];
                return "";
            }
        }

        internal void SetWatch(bool on)
        {
            Saved.Watch = on;
            watchTicket++;
            nextGlance = -1;
            lastGlance = -1;
            lastPrint = null;
            watchTold = false;
            Save();
            Sync();
            string blind = on ? Unseeing() : null;
            if (blind != null) Talk.Answer("(watch my screen)", blind);
        }

        internal void SetGlanceGap(int seconds)
        {
            Saved.GlanceGap = seconds;
            if (Saved.Watch) nextGlance = Now + Math.Min(20, seconds);
            Save();
            Sync();
        }

        internal void SetCaughtUp(bool on)
        {
            Saved.NoCaughtUp = !on;
            Save();
            Sync();
        }

        // Why her brain can't watch, or null when it can.
        private string Unseeing()
        {
            Cloud remote = Brain.Remote;
            if (Brain.Model == null) return "My brain's offline, so I can't watch your screen yet.";
            if (remote != null && remote.Kind == "deepseek") return "DeepSeek can't see, so I can't watch your screen. A free NVIDIA key can: " + Brain.FreeKeyHow + ".";
            if (remote != null && remote.Model == Brain.Unsighted) return remote.Model + " can't see screens. Pick one that can " + Brain.BrainMenu + (remote.Kind == "nvidia" ? ", like nvidia/nemotron-3.5-super-vl-120b-a12b." : ".");
            return null;
        }

        private void CheckGlance(double now)
        {
            if (!Saved.Watch || glancing || Hushed) return;
            if (nextGlance < 0) nextGlance = now + Math.Min(20, GlanceGap);
            if (now < nextGlance || Brain.Model == null || away || dragging || pressed || !Talk.Quiet) return;
            nextGlance = now + GlanceGap * (0.8 + random.NextDouble() * 0.4);
            if (Unseeing() != null) return;
            Glance(now);
        }

        // One look: a quiet screenshot, skipped when the screen hasn't changed since the last one, then her brain's line about it.
        internal void Glance(double now)
        {
            glancing = true;
            int ticket = asking;
            int watch = watchTicket;
            bool force = lastGlance < 0 || now - lastGlance >= 300;
            byte[] before = lastPrint;
            string ask = Glances.Ask(RecentGlances(now));
            Func<byte[], string, string> seeing = Seeing ?? delegate(byte[] shot, string question) { return Brain.See(shot, Brain.Persona + Brain.Speech(), question, 140); };
            Func<double> idle = Idle;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said = null, saw = null, problem = null;
                byte[] print = null;
                bool looked = false, blind = false;
                try
                {
                    // Not while they've been away from the computer for 10 minutes, like on Windows.
                    if (idle() <= 600)
                    {
                        byte[] picture = Eyes.Peek();
                        print = Eyes.Print(picture);
                        if (print == null) blind = true;
                        else if (force || Glances.Moved(print, before))
                        {
                            byte[] shot = Eyes.Jpeg(picture, 1280);
                            looked = shot != null;
                            if (looked) said = Glances.Reply(seeing(shot, ask), out saw);
                        }
                    }
                }
                catch (Exception error)
                {
                    // Talking to her cancels a glance; that isn't a problem, like on Windows.
                    System.Net.WebException web = error as System.Net.WebException;
                    if (web == null || web.Status != System.Net.WebExceptionStatus.RequestCanceled)
                    {
                        looked = true;
                        problem = error.GetType().Name + ": " + error.Message;
                    }
                }
                Dispatcher.UIThread.Post(delegate { FinishGlance(ticket, watch, print, looked, blind, said, saw, problem); });
            });
        }

        private void FinishGlance(int ticket, int watch, byte[] print, bool looked, bool blind, string said, string saw, string problem)
        {
            glancing = false;
            if (!Saved.Watch || watch != watchTicket) return;
            if (blind)
            {
                // Said once each time Watch my screen goes on; she keeps trying quietly in case a tool turns up.
                if (!watchTold && Talk.Quiet && !away && !Hushed) { watchTold = true; Blurt("(watch my screen)", Eyes.Unwatchable); }
                return;
            }
            if (!looked) return;
            lastGlance = Now;
            lastPrint = print;
            if (problem != null)
            {
                lastPrint = null;
                if (problem != glanceFailed) { glanceFailed = problem; Brain.Log("glance", -1, problem); }
                // A brain that turned out not to see gets said once, like Windows' Mainframe note; other errors go in errors.txt.
                string unseen = Unseeing();
                if (unseen != null && !watchTold && Talk.Quiet && !away && !Hushed) { watchTold = true; Blurt("(watch my screen)", unseen); }
                return;
            }
            glanceFailed = null;
            if (string.IsNullOrEmpty(said)) { Glimpsed(saw); return; }
            // Quiet going on while she looked drops the line too, like on Windows.
            if (ticket != asking || away || Hushed || dragging || !Talk.Quiet) { lastPrint = null; Glimpsed(saw); return; }
            // A line about something she already brought up is dropped like a SKIP, so the same scene waits before she tries again.
            if (Glances.Repeats(said, RecentGlances(Now), Brain.Nickname)) { Glimpsed(saw); return; }
            glanced.Add(Tuple.Create(Now, said));
            Brain.Saw(saw, said);
            Brain.Remember(string.IsNullOrEmpty(saw) ? "(You glanced at their screen.)" : "(You glanced at their screen and saw: " + saw + ")", said);
            Reply(++asking, "(your screen)", said);
            // Her own glance isn't them paying her attention.
            noticedAsked = asking;
        }

        // With Keep her caught up on, a glance she kept quiet about still goes in her memory, so she can say what was on their screen.
        private void Glimpsed(string saw)
        {
            if (!Saved.NoCaughtUp) Brain.Saw(saw, null);
        }

        private List<string> RecentGlances(double now)
        {
            glanced.RemoveAll(delegate(Tuple<double, string> line) { return now - line.Item1 > Glances.Memory || now < line.Item1; });
            if (glanced.Count > 30) glanced.RemoveRange(0, glanced.Count - 30);
            return glanced.ConvertAll<string>(delegate(Tuple<double, string> line) { return line.Item2; });
        }

        internal double PopOut { get { return popOut; } }
        internal bool CheckIns { get { return !Saved.NoCheckIns; } }
        internal bool CheckInsShown { get { return checkInsItem.IsChecked; } }
        internal int PopPace { get { return Math.Max(0, Math.Min(2, Saved.PopPace ?? 1)); } }
        internal double PopGap() { return PopPaces[PopPace, 0] + random.NextDouble() * PopPaces[PopPace, 1]; }

        internal string PopsPicked
        {
            get
            {
                foreach (MenuItem pick in popPicks) if (pick.IsChecked) return pick.Header.ToString();
                return "";
            }
        }

        internal void SetCheckIns(bool on)
        {
            Saved.NoCheckIns = !on;
            nextPopIn = -1;
            ForgetNudges();
            Save();
            Sync();
        }

        internal void SetPopPace(int pace)
        {
            Saved.PopPace = Math.Max(0, Math.Min(2, pace));
            nextPopIn = -1;
            Save();
            Sync();
        }

        internal static void Browse(string url)
        {
            try
            {
                if (OperatingSystem.IsMacOS()) Process.Start("open", url);
                else if (OperatingSystem.IsLinux()) Process.Start("xdg-open", url);
                else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception) { }
        }

        internal void FindBrain()
        {
            string saved = Saved.Model;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string model = null;
                try { model = Brain.PickModel(Brain.InstalledModels(), saved, null); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    if (Brain.Model != null || model == null) return;
                    Brain.Model = model;
                    Saved.Model = model;
                    Save();
                });
            });
        }

        internal void Hear(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > 240) text = text.Substring(0, 240);
            if (meeting.Asking) { Met(text); return; }
            int ticket = ++asking;
            Brain.Cancel();
            voice.Stop();
            bool askPlease;
            string heard = text;
            text = Hands.Manners(text, out askPlease);
            if (askPlease) { Reply(ticket, heard, Hands.SayPlease); return; }
            string tool;
            ToolArguments args;
            bool noted;
            try { noted = Hands.Heard(text, out tool, out args) && tool == "remember"; }
            catch (Exception) { noted = false; tool = null; args = null; }
            if (noted)
            {
                string done = Hands.Do(tool, args);
                Brain.Remember(text, done);
                Reply(ticket, text, done);
                return;
            }
            // Her nap, her own moves and her animations work before she has a brain, like on Windows.
            if (Think == null && Brain.Model == null && (tool == "nap" || tool == "move_yourself" || tool == "animate")) { Reply(ticket, text, Hands.Do(tool, args)); return; }
            if (Think == null && Brain.Model == null) { Reply(ticket, text, Offline); FindBrain(); return; }
            Talk.Thinking(text);
            Ask(ticket, text, 0);
        }

        // Her brain answers on the thread pool. A stuck NVIDIA model hands the message to the next one, twice at most, like on Windows.
        private void Ask(int ticket, string text, int switches)
        {
            Func<string, string> think = Think ?? Brain.Ask;
            Cloud remote = Brain.Remote;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said;
                bool stuck = false;
                try { said = think(text); }
                catch (BrainException error) { said = error.Message == Brain.Offline ? Offline : error.Told ? error.Message : "My brain couldn't answer: " + error.Message; stuck = Brain.Stalled(error); }
                catch (Exception error) { said = Brain.Explain(error, remote) ?? "That reply was cancelled. Try again."; stuck = Brain.Stalled(error); }
                if (string.IsNullOrWhiteSpace(said)) said = "My brain sent an empty reply. Try again.";
                Dispatcher.UIThread.Post(delegate
                {
                    if (ticket != asking) return;
                    if (stuck && switches < 2 && Hop(remote)) { Ask(ticket, text, switches + 1); return; }
                    Reply(ticket, text, said);
                });
            });
        }

        // NVIDIA models that got stuck since she started, so she never hands back to one.
        private readonly List<string> stalled = new List<string>();

        // Stay on my model: a stuck NVIDIA model says what went wrong instead of handing over.
        internal void SetStay(bool on)
        {
            Saved.StayOnModel = on;
            Save();
        }

        internal bool Hop(Cloud remote)
        {
            if (Saved.StayOnModel || remote == null || remote.Kind != "nvidia" || Brain.Remote != remote) return false;
            string old = remote.Model;
            stalled.Add(old);
            string next = Brain.Next(old, cloudModels, stalled);
            if (next == null) return false;
            Brain.Cancel();
            remote.Model = next;
            Brain.Model = next;
            Brain.Log("NVIDIA", -1, old + " wasn't answering, so she switched to " + next + ".");
            return true;
        }

        internal bool Speaks { get { return voice.Ready && !Saved.Silent; } }

        private void Reply(int ticket, string heard, string said)
        {
            if (!Speaks || Voice.Spoken(said).Length == 0) { Talk.Answer(heard, said); TypeAlong(said, ticket); return; }
            Talk.Thinking(heard);
            bool[] shown = { false };
            DispatcherTimer.RunOnce(delegate
            {
                if (ticket != asking || shown[0]) return;
                shown[0] = true;
                Talk.Answer(heard, said);
                TypeAlong(said, ticket);
            }, TimeSpan.FromMilliseconds(VoiceWait));
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string problem = null;
                // ElevenLabs hands back a finished WAV, so her pitch, speed, robot and glitch go on here, like on Windows.
                try { wav = VoiceFx.Styled(voice.Speak(said), said); }
                catch (VoiceException error) { problem = Mac(error.Message); }
                catch (Exception error) { problem = Voice.Short("Voice glitched: " + error.Message); }
                Dispatcher.UIThread.Post(delegate
                {
                    voiceProblem = problem;
                    if (ticket != asking || shown[0]) return;
                    shown[0] = true;
                    double seconds = wav == null ? 0 : Voice.Seconds(wav);
                    Talk.Answer(heard, said, seconds);
                    if (seconds >= 0.2) voice.Play(wav);
                    else TypeAlong(said, ticket);
                });
            });
        }

        private static string Mac(string problem) { return problem == Voice.Rejected ? "ElevenLabs rejected the key. Settings > Voice > New key." : problem; }

        private string KeyFile { get { return Path.Combine(Program.Smoke ?? folder, "voice-key"); } }

        private string BrainKeyFile { get { return Path.Combine(Program.Smoke ?? folder, "brain-key"); } }

        private string ReadVoiceKey() { return ReadSecret(KeyFile); }

        private void WriteVoiceKey(string key) { WriteSecret(KeyFile, key, "voice key"); }

        private static string ReadSecret(string path)
        {
            try
            {
                string key = File.ReadAllText(path).Trim();
                return key.Length == 0 ? null : key;
            }
            catch (Exception) { return null; }
        }

        // Keys are kept in a file only they can read, or deleted with null.
        private static void WriteSecret(string path, string key, string what)
        {
            try
            {
                if (key == null) { File.Delete(path); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new FileStream(path, options))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    using (StreamWriter writer = new StreamWriter(stream)) writer.Write(key);
                }
            }
            catch (Exception error) { Brain.Log(path, -1, "Couldn't save the " + what + ": " + error.Message); }
        }

        // NVIDIA's page takes the focus, which would close her key box, so she says where to paste the key instead.
        internal void GetFreeKey()
        {
            ComeBack();
            if (Program.Smoke == null) Browse(FreeKeyPage);
            Talk.Answer(BrainKeyHeard, FreeKeySteps);
        }

        internal void AskBrainKey()
        {
            ComeBack();
            Talk.AskKey(BrainKeyHeard, "Paste your NVIDIA, OpenAI, Claude or DeepSeek API key and press Enter. It stays on this computer. A free NVIDIA key works.", SaveBrainKey);
        }

        // A key she checks by reading its models, then thinks with on its best chat model until they pick another under Brain.
        internal void SaveBrainKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;
            string kind = Cloud.KindOf(key);
            if (kind == null) { Talk.Answer(BrainKeyHeard, NotAKey); return; }
            int ticket = ++asking;
            Talk.Thinking(BrainKeyHeard);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> found = null;
                string problem = null;
                string company = new Cloud(key, null).Company;
                try { found = Brain.CloudModels(key); }
                catch (BrainException error) { problem = error.Message; }
                catch (Exception error) { Brain.Log(company + " models", -1, error.Message); problem = "Couldn't reach " + company + ". Check your internet, then paste the key again."; }
                Dispatcher.UIThread.Post(delegate
                {
                    string model = found == null ? null : Brain.DefaultCloudModel(kind, found);
                    if (problem == null && model == null) problem = "That key can't use any chat models.";
                    if (problem != null) { if (ticket == asking) Talk.Answer(BrainKeyHeard, problem); return; }
                    UseCloud(key, model, found);
                    WriteSecret(BrainKeyFile, key, "brain key");
                    if (ticket == asking) Talk.Answer(BrainKeyHeard, "Key saved. I'm thinking with " + model + " now. Change it under Brain in my right-click menu.");
                });
            });
        }

        internal void UseCloud(string key, string model, List<string> models)
        {
            Brain.Cancel();
            Brain.Remote = new Cloud(key, model);
            Brain.Model = model;
            Saved.CloudModel = model;
            cloudModels = models;
            Save();
        }

        internal void PickCloud(string model)
        {
            Cloud remote = Brain.Remote;
            if (remote == null || string.IsNullOrEmpty(model)) return;
            Brain.Cancel();
            remote.Model = model;
            Brain.Model = model;
            Saved.CloudModel = model;
            Save();
        }

        // Back to Ollama: the key file goes, and she finds her Ollama model again.
        internal void ForgetBrainKey()
        {
            Brain.Cancel();
            Brain.Remote = null;
            Brain.Model = null;
            cloudModels = null;
            Saved.CloudModel = null;
            WriteSecret(BrainKeyFile, null, "brain key");
            Save();
            FindBrain();
        }

        internal void AskVoiceKey()
        {
            ComeBack();
            Talk.AskKey("Paste your ElevenLabs API key and press Enter. It stays on this computer.");
        }

        internal void SaveVoiceKey(string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;
            int ticket = ++asking;
            voice.Stop();
            Talk.Thinking(KeyHeard);
            ThreadPool.QueueUserWorkItem(delegate
            {
                VoiceListing found = null;
                string problem = null;
                int status = 0;
                try { found = Voice.Fetch(key); }
                catch (VoiceException error) { problem = error.Message; status = error.Status; }
                catch (Exception error) { problem = Voice.Short("Couldn't read your ElevenLabs voices: " + error.Message); }
                Dispatcher.UIThread.Post(delegate
                {
                    if (status == 401) { if (ticket == asking) Talk.Answer(KeyHeard, Voice.Unsaved(problem)); return; }
                    voice.Key = key;
                    WriteVoiceKey(key);
                    voiceProblem = problem == null ? null : Mac(problem);
                    if (found != null)
                    {
                        voices = found;
                        if (found.Voices.Count > 0 && (voice.VoiceId == null || !Lists(found, voice.VoiceId))) voice.VoiceId = found.Voices[0].Id;
                    }
                    Saved.VoiceId = voice.VoiceId;
                    Saved.Silent = false;
                    Save();
                    if (ticket != asking) return;
                    if (problem != null) Talk.Answer(KeyHeard, "Key saved, but " + voiceProblem);
                    else if (voice.VoiceId == null) Talk.Answer(KeyHeard, "Key saved, but your ElevenLabs account has no voices yet. Add one at elevenlabs.io.");
                    else Reply(ticket, KeyHeard, "Key saved. I'll talk as " + VoiceName() + ". Change it under Settings > Voice.");
                });
            });
        }

        private static bool Lists(VoiceListing found, string id)
        {
            foreach (VoiceEntry entry in found.Voices) if (entry.Id == id) return true;
            return false;
        }

        private string VoiceName()
        {
            if (voice.VoiceId == null) return null;
            if (voices != null) foreach (VoiceEntry entry in voices.Voices) if (entry.Id == voice.VoiceId && !string.IsNullOrEmpty(entry.Name)) return entry.Name;
            return voice.VoiceId;
        }

        internal string VoiceStatus { get { return (voice.Key == null ? "no key" : "key " + Voice.Tail(voice.Key)) + " | " + (VoiceName() ?? "no voice") + " | " + (Speaks ? "speaking" : "quiet"); } }

        internal bool VoiceWorking { get { return Speaks && voiceProblem == null; } }

        internal bool TypingSound { get { return Saved.Typing == 1 || (Saved.Typing == 0 && !VoiceWorking); } }

        internal void SetTyping(bool on)
        {
            Saved.Typing = on ? 1 : 2;
            Sfx.Typing = on;
            Save();
            Sfx.Type("click clack", Talk.LettersPerSecond, 1);
        }

        internal void AutoTyping() { Saved.Typing = 0; }

        private void TypeAlong(string said, int ticket)
        {
            Sfx.Typing = TypingSound;
            Sfx.Type(said, Talk.Pace, ticket);
        }

        internal void SetSpeak(bool on)
        {
            Saved.Silent = !on;
            if (!on) voice.Stop();
            Save();
        }

        internal void ChooseVoice(string id)
        {
            voice.VoiceId = id;
            Saved.VoiceId = id;
            Save();
        }

        internal void ForgetVoice()
        {
            voice.Stop();
            voice.Key = null;
            voices = null;
            voiceProblem = null;
            WriteVoiceKey(null);
        }

        private void ListVoices()
        {
            string key = voice.Key;
            if (key == null || listing) return;
            listing = true;
            ThreadPool.QueueUserWorkItem(delegate
            {
                VoiceListing found = null;
                string problem = null;
                try { found = Voice.Fetch(key); }
                catch (VoiceException error) { problem = Mac(error.Message); }
                catch (Exception error) { problem = Voice.Short("Couldn't read your ElevenLabs voices: " + error.Message); }
                Dispatcher.UIThread.Post(delegate
                {
                    listing = false;
                    if (voice.Key != key) return;
                    if (found != null) voices = found;
                    voiceProblem = problem;
                    RefreshVoice();
                });
            });
        }

        internal string VoiceMenu
        {
            get
            {
                RefreshVoice();
                List<string> shown = new List<string>();
                foreach (object item in voiceMenu.Items)
                {
                    MenuItem entry = item as MenuItem;
                    if (entry == null) shown.Add("-");
                    else shown.Add((entry.IsChecked ? "*" : "") + entry.Header + (entry.IsEnabled ? "" : " (off)"));
                }
                return string.Join(" | ", shown);
            }
        }

        private void RefreshVoice()
        {
            if (voiceMenu == null) return;
            voiceMenu.Items.Clear();
            if (voice.Key == null)
            {
                MenuItem add = new MenuItem { Header = "Add ElevenLabs key..." };
                add.Click += delegate { AskVoiceKey(); };
                voiceMenu.Items.Add(add);
                return;
            }
            MenuItem speak = new MenuItem { Header = "Use ElevenLabs", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Speaks, IsEnabled = voice.VoiceId != null };
            speak.Click += delegate { SetSpeak(Saved.Silent); };
            voiceMenu.Items.Add(speak);
            voiceMenu.Items.Add(new Separator());
            if (voices == null)
            {
                voiceMenu.Items.Add(new MenuItem { Header = "Loading voices...", IsEnabled = false });
                ListVoices();
            }
            else
            {
                int shown = 0;
                foreach (VoiceEntry entry in voices.Voices)
                {
                    if (shown++ >= 30) break;
                    string id = entry.Id;
                    MenuItem pick = new MenuItem { Header = string.IsNullOrEmpty(entry.Name) ? id : entry.Name, ToggleType = MenuItemToggleType.Radio, GroupName = "voice", IsChecked = id == voice.VoiceId };
                    pick.Click += delegate { ChooseVoice(id); };
                    voiceMenu.Items.Add(pick);
                }
                if (voices.Voices.Count == 0) voiceMenu.Items.Add(new MenuItem { Header = "No voices on this key", IsEnabled = false });
            }
            voiceMenu.Items.Add(new Separator());
            MenuItem renew = new MenuItem { Header = "New key..." };
            renew.Click += delegate { AskVoiceKey(); };
            MenuItem remove = new MenuItem { Header = "Remove key ending " + Voice.Tail(voice.Key) };
            remove.Click += delegate { ForgetVoice(); };
            voiceMenu.Items.Add(renew);
            voiceMenu.Items.Add(remove);
            if (voiceProblem != null) voiceMenu.Items.Add(new MenuItem { Header = voiceProblem, IsEnabled = false });
        }

        private Settings Load()
        {
            try
            {
                using (FileStream stream = File.OpenRead(Path.Combine(folder, "cross.json"))) return (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
            }
            // A new install starts in the computer's own language when she speaks it.
            catch (Exception) { return new Settings { Language = Brain.SystemLanguage(System.Globalization.CultureInfo.CurrentUICulture) }; }
        }

        internal void Save()
        {
            Saved.X = anchor.X;
            Saved.Y = anchor.Y;
            Saved.Placed = true;
            if (Program.Smoke != null) return;
            try
            {
                Directory.CreateDirectory(folder);
                using (FileStream stream = File.Create(Path.Combine(folder, "cross.json"))) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, Saved);
            }
            catch (Exception) { }
        }

        internal void Quit()
        {
            if (quitting) return;
            quitting = true;
            Brain.Cancel();
            voice.Stop();
            if (presence != null) presence.Dispose();
            Talk.Dismiss();
            GlitchOut(delegate
            {
                Save();
                frames.Stop();
                desktop.Shutdown(0);
            });
        }

        internal void Finish(int code)
        {
            frames.Stop();
            Talk.Dismiss();
            desktop.Shutdown(code);
        }
    }
}
