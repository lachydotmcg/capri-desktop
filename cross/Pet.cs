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
    internal sealed partial class Settings
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
        [DataMember] internal string LastVersion;
        [DataMember] internal bool StayOnModel;
        [DataMember] internal int HuskMode;
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
        // Which provider makes her voice: elevenlabs, local or off. Missing in settings from before it, which were ElevenLabs.
        // Silent stays apart: it quiets the provider they picked without forgetting it.
        [DataMember] internal string VoiceProvider;
        [DataMember] internal string LocalVoiceUrl;
        [DataMember] internal string LocalVoiceModel;
        [DataMember] internal string LocalVoiceName;
        [DataMember] internal string LocalVoiceFormat;
        // A server of their own that thinks for her instead of Ollama (llama-server, LM Studio, vLLM, TGI), and its model.
        [DataMember] internal string BrainServer;
        [DataMember] internal string ServerModel;
        [DataMember] internal int MainframeSize;
        [DataMember] internal int MainframeColours;
        [DataMember] internal bool AdvancedSettings;
        [DataMember] internal int MemorySize;
        [DataMember] internal int ReplyLength;
        [DataMember] internal int? BrainTier;
        [DataMember] internal string FreeModel;
        [DataMember] internal string PaidModel;
        [DataMember] internal string SpeechModel;
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
        private Bitmap strip;
        internal Bitmap Strip { get { if (strip == null) strip = Pet.ReadArt(name); return strip; } }
        private readonly string name;
        internal readonly int Count;
        internal readonly double Fps;
        internal readonly double Height;
        // How far a cell's middle sits from the middle of her square, as a share of her size. Narrow cells cut to the art need it.
        internal readonly double ShiftX, ShiftY;

        internal Clip(string name, int count, double fps, double height, double shiftX = 0, double shiftY = 0)
        {
            this.name = "Capri.Sprite." + name + ".png";
            Count = count;
            Fps = fps;
            Height = height;
            ShiftX = shiftX;
            ShiftY = shiftY;
            Reload();
        }

        internal void Reload()
        {
            Bitmap old = strip;
            strip = null;
            if (old != null) old.Dispose();
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

    internal sealed partial class Pet : Window
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
        internal static readonly string[] SoundNames = { "Off", "Quiet", "Normal", "Loud" };
        internal static readonly int[] SoundLevels = { 0, 30, 60, 100 };
        internal const string Offline = "My brain's offline. Open Capri Mainframe and set up her brain: " + Starter + ".";
        // The brain she downloads for herself through Ollama when it has none yet, so no terminal is needed.
        internal const string Starter = "gemma4:e4b";
        internal const string PullChoice = "Download " + Starter + ", her brain (a few GB)";
        internal const string NoOllama = "Ollama isn't running. Install it from ollama.com and open it.";
        private const string FreeKeyPage = "https://build.nvidia.com/settings/api-keys";
        internal const string KeyChoice = "Use an API key...";
        internal const string NotAKey = "That doesn't look like a brain key. Try NVIDIA, Google AI Studio, OpenRouter, OpenAI, Claude or DeepSeek.";
        internal const string FreeKeySteps = "Make a key on NVIDIA's page and copy it. Paste it under BRAIN in Capri Mainframe.";
        internal const string DiscordPage = "https://discord.gg/capridesktop";
        internal const string HelpPage = "https://capriai.app/help/";
        // Mac and Linux don't update themselves yet, so she checks GitHub's latest release now and then and offers its page.
        private const string LatestRelease = "https://api.github.com/repos/lachydotmcg/capri-desktop/releases/latest";
        private const string ReleasesPage = "https://github.com/lachydotmcg/capri-desktop/releases/latest";
        internal static readonly double[] Sizes = { 140, 170, 240, 320, 440, 560 };
        internal static readonly string[] SizeNames = { "Tiny", "Small", "Medium", "Large", "Huge", "Giant" };
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
        // Up-and-down pats count too, on their own axis (Lachy, 5 Nov: head pats did nothing).
        private readonly List<double> petRises = new List<double>();
        private int petY;
        private int petRise;
        private double petAt = -60;
        private int petLine;
        private int petTicket;
        // A busy moment holds her pat line up to PetWaits times, half a second each, before she lets it go.
        private const int PetWaits = 12;
        private int petWaits;
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
        private readonly LocalSpeaker localVoice = new LocalSpeaker();
        private readonly QuietSpeaker noVoice = new QuietSpeaker();
        private ISpeaker elevenLabs;
        private VoiceListing voices;
        private string voiceProblem;
        private bool listing;
        private MenuItem voiceMenu;
        private MenuItem typingItem;
        private const int VoiceWait = 8000;
        private const string KeyHeard = "(voice key)";
        private const string NameHeard = "(nickname)";
        private const string PersonalityHeard = "(personality)";
        private const string BrainHeard = "(brain download)";
        private const string BrainKeyHeard = "(brain key)";
        // The models on their cloud key, read when the Brain menu opens.
        private List<string> cloudModels;
        private bool cloudListing;
        // "Playing Capri" on their Discord profile, like on Windows. Off in the smoke test.
        private readonly Presence presence;
        private MenuItem discordItem;
        private MenuItem securityMenu;
        private bool fingers;
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
        private string pullingName = Starter;
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
        internal Action<PixelPoint> MoveTo;
        // Where a point on her window is on the desktop, which the smoke test can make fail. Null is PointToScreen.
        internal Func<Point, PixelPoint> ToScreen;
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
        private PixelPoint dragAt;
        private bool dragPending;
        // The mouse pressing her, so letting go of her without a release can give its capture back.
        private IPointer holder;
        // Window positions she asked for that the desktop hasn't confirmed yet, oldest first.
        private readonly List<PixelPoint> asked = new List<PixelPoint>();
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
            StartCare();
            // Security, like Windows: Mac and Linux have the first five toggles' features, and settings from before it get Trusted.
            // Her mouse and keyboard jobs too, once /dev/uinput lets her make her own (Fingers). The smoke test keeps the five.
            fingers = Program.Smoke == null && Fingers.Problem == null;
            Security.Has = delegate(int toggle) { return toggle <= Security.Power || (toggle == Security.Mouse && fingers); };
            Hands.OwnHands = fingers;
            Security.Load(Saved.Permissions);
            Security.Changed = delegate { Saved.Permissions = Security.Saved; Save(); if (ContextMenu != null) Sync(); };
            LoginFile = Program.Smoke != null ? Path.Combine(Program.Smoke, OperatingSystem.IsMacOS() ? "capri.plist" : "capri.desktop") : Limbs.LoginFile();
            // A copy that moved since they ticked it takes the login entry over, so it doesn't point at the old place.
            if (Program.Smoke == null && File.Exists(LoginFile)) Limbs.SetLogin(LoginFile, Limbs.SelfPath(), true, OperatingSystem.IsMacOS());
            Brain.LogPath = Path.Combine(Program.Smoke ?? folder, "errors.txt");
            StartMods();
            Brain.BrainMenu = "under BRAIN in Capri Mainframe";
            Brain.FreeKeyHow = "Capri Mainframe > BRAIN > Free key";
            Brain.Bin = OperatingSystem.IsMacOS() ? "Trash" : "trash";
            Brain.MemoryPath = Path.Combine(Program.Smoke ?? folder, "me.md");
            StartVault();
            // Their personality.md, made only when they pick Edit (back in 0.8.0). Her safety lines stay in her code after it.
            Brain.PersonalityPath = Path.Combine(Program.Smoke ?? folder, Brain.PersonalityName);
            // Remember our chats, like on Windows: every chat goes in a daily transcript, and the brain they picked folds older
            // lines into memories.md, which she reads every chat.
            Brain.MemoriesPath = Path.Combine(Program.Smoke ?? folder, "memories.md");
            Brain.TranscriptPath = Path.Combine(Program.Smoke ?? folder, "transcripts");
            // Screenshots here show her and her bubble too, so her glances stay out of memories.md (a Bazzite user).
            Brain.FoldsGlances = false;
            Brain.Keeping = !Saved.NoMemories;
            if (Program.Smoke == null) Brain.Keeper = Brain;
            Brain.Language = Brain.LanguageCode(Saved.Language) == "en" ? null : Saved.Language;
            Brain.Nickname = Brain.Nick(Saved.Nickname).Length == 0 ? null : Brain.Nick(Saved.Nickname);
            Brain.Wordiness = Math.Max(0, Math.Min(2, Saved.Replies));
            Brain.Recall = Saved.Recall == 16 || Saved.Recall == 32 ? Saved.Recall : 8;
            Brain.MemorySize = Array.IndexOf(Brain.MemorySizes, Saved.MemorySize) >= 0 ? Saved.MemorySize : 0;
            Brain.ReplyLength = Math.Clamp(Saved.ReplyLength, 0, Brain.ReplySizes.Length - 1);
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
            StartAwake();
            Hands.Look = LookAt;
            Hands.Nap = NapTool;
            Hands.MoveSelf = MoveTool;
            Hands.Mainframe = MainframeTool;
            Hands.Animate = AnimateTool;
            // Her settings tool reads and changes what her menus show.
            Hands.SettingOn = SettingOn;
            Hands.SetSetting = SetSetting;
            Hands.LevelOf = LevelOf;
            Hands.SetLevel = SetLevel;
            Hands.SettingAction = delegate(ToolArguments args) { return SettingAction(args.Action); };
            if (Program.Smoke == null)
            {
                Hands.Work = delegate(string job, string asked) { return Chores.Run(Brain, job, asked); };
                Chores.Mouse = delegate { return Dispatcher.UIThread.Invoke(MouseShare) ?? KWinMouse.Ask(); };
                Chores.Says = delegate(string line) { Dispatcher.UIThread.Post(delegate { Reply(asking, "(chat)", line); }); };
                // Her own pointer, like Windows': it glides while their mouse stays put, and hides for her screenshots.
                Chores.Track = delegate(double x, double y) { Dispatcher.UIThread.Post(delegate { HerPointer.Point(DesktopPixel(x, y)); }); };
                Chores.Tapped = delegate { Dispatcher.UIThread.Post(delegate { HerPointer.Tap(); }); };
                Chores.Hidden = delegate(bool hidden) { Dispatcher.UIThread.Invoke(delegate { if (hidden) HerPointer.Hide(); else if (herPointer != null) { HerPointer.Show(); Look.OnTopAgain(herPointer); } }); };
                Chores.Parked = delegate { Dispatcher.UIThread.Post(delegate { if (herPointer != null) herPointer.Park(); }); };
            }
            if (Program.Smoke == null) presence = new Presence(System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3));
            // A cloud key they saved thinks for her instead of Ollama, on the model they picked.
            string brainKey = ReadSecret(BrainKeyFile);
            if (brainKey != null && Cloud.KindOf(brainKey) != null && !string.IsNullOrEmpty(Saved.CloudModel))
            {
                Brain.Remote = new Cloud(brainKey, Saved.CloudModel);
                Brain.Model = Saved.CloudModel;
            }
            string savedServer = Brain.ServerAddress(Saved.BrainServer);
            string savedServerKey = null;
            string pairServer, pairKey;
            if (ReadServerPair(ServerPairFile, out pairServer, out pairKey))
            {
                savedServer = Brain.ServerAddress(pairServer);
                savedServerKey = savedServer == null ? null : pairKey;
            }
            WriteSecret(ServerKeyFile, null, "provider key");
            Brain.SetServer(savedServer, savedServerKey != null && Brain.ServerKeySafe(savedServer) ? savedServerKey : null);
            if (Brain.Remote == null && savedServer != null && !string.IsNullOrEmpty(Saved.ServerModel)) Brain.Model = Saved.ServerModel;
            RestoreBrainTier();
            voice.Key = ReadVoiceKey();
            // Her keys, for Brain.Scrubbed to keep out of errors.txt. It can run on any thread.
            Brain.Secrets = delegate { Cloud remote = Brain.Remote; return new[] { remote == null ? null : remote.Key, Brain.ServerKey, voice.Key }; };
            voice.VoiceId = string.IsNullOrEmpty(Saved.VoiceId) ? null : Saved.VoiceId;
            voice.Model = Saved.SpeechModel;
            elevenLabs = new ElevenLabsSpeaker(voice);
            Saved.VoiceProvider = Speakers.Known(Saved.VoiceProvider);
            localVoice.Url = Saved.LocalVoiceUrl;
            localVoice.Model = Saved.LocalVoiceModel;
            localVoice.Speaker = Saved.LocalVoiceName;
            localVoice.Format = LocalSpeaker.Known(Saved.LocalVoiceFormat);
            if (Program.Smoke == null) { Voice.Playback = Limbs.Speak; Voice.Silence = Limbs.Hush; }
            if (Program.Smoke == null)
            {
                Sfx.TypeOut = delegate(short[] samples) { Limbs.Speak(Voice.Wave(samples, Sfx.Rate)); };
                Sfx.Untyped = Limbs.Hush;
            }
            Look.OverGames = !Saved.NotOnTop && Program.Smoke == null;
            Look.AboveGames(this);
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
            // A Linux window manager can put her window somewhere other than where she asked, like KWin keeping it off a panel,
            // so she follows where it really went. A Mac moves her window right away, as asked.
            if (OperatingSystem.IsLinux()) PositionChanged += delegate(object sender, PixelPointEventArgs e) { FollowPosition(e.Point); };
            Play(idle);
            nextGlitch = 30 + random.NextDouble() * 30;
            ContextMenu = Menu();
            // A saved key's models are read now, so a stuck NVIDIA model has others to hand over to.
            if (Brain.Remote != null && Program.Smoke == null) ListCloud();
            Talk = new Talk(this);
            PrepareHusk();
            // Her bubble and pill come up by themselves with her, so they leave the keyboard alone too. Her typing box takes it.
            Unfocus(Talk.Bubble);
            Unfocus(Talk.Pill);
            if (!OnTop) Stack(false);
            frames.Interval = TimeSpan.FromMilliseconds(33);
            frames.Tick += delegate { Program.Guard(Tick); };
            // Closing her while held must not leave the frame timer moving a closed window.
            Closed += delegate { pressed = dragging = dragPending = false; frames.Stop(); };
            Deactivated += delegate { Program.Guard(Unhold); };
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
                    StartNews(!fresh);
                    if (Saved.HuskMode != 0) SetHusk(Saved.HuskMode, true);
                    int ticket = asking;
                    DispatcherTimer.RunOnce(() => Greet(ticket, DateTime.Now.Hour), TimeSpan.FromMilliseconds(1800));
                    if (fresh) { OpenMainframe(); meetAt = Now + 4; }
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
            PixelPoint at = new PixelPoint(to.X + Px(FrameX), to.Y + Px(FrameY));
            if (asked.Count >= 8) asked.RemoveAt(0);
            asked.Add(at);
            if (MoveTo != null) MoveTo(at);
            else Position = at;
        }

        // Where the desktop says her window is. A move she asked for, arriving late, changes nothing, so walking and falling
        // never step back to an older spot; anywhere else means the desktop put her there, and she carries on from it.
        internal void FollowPosition(PixelPoint at)
        {
            int was = asked.IndexOf(at);
            if (was >= 0) { asked.RemoveRange(0, was + 1); return; }
            asked.Clear();
            if (!dragging) anchor = new PixelPoint(at.X - Px(FrameX), at.Y - Px(FrameY));
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
            DragFrame();
            TickHusk();
            CheckStyle();
            if (presence != null && now >= nextStatus) { nextStatus = now + 1; presence.Show(Saved.NoDiscord ? null : Doing); }
            if (Program.Smoke == null && now >= nextUpdate) { nextUpdate = now + 6 * 3600; CheckUpdate(); }
            if (UpdateNews != null && !away && !dragging && Talk.Quiet) { Talk.Answer("(update)", UpdateNews); UpdateNews = null; }
            if (outro != null && now >= outroAt) { Action next = outro; outro = null; next(); }
            CheckRaised(now);
            CheckKeepAwake();
            if (Program.Smoke == null) CheckHabits(now);
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
                return "Plugged in";
            }
        }

        // "Check my screen": her brain looks at a screenshot when they ask, like on Windows. Watch my screen's glances are below.
        // Screenshots here show her and her bubble too, which aren't part of their screen.
        internal const string LookAsk = "Describe what's on this screen in two or three plain sentences: what kind of game or app it is (name it only if the name is on screen), what's happening, and any text or numbers that matter. Don't guess who a person is from their face; name someone only if their name is written on screen. The small red-haired anime girl with a speech bubble is Capri, the desktop companion, not part of their screen, so leave her out.";

        private string LookAt(string focus)
        {
            Cloud remote = Brain.Remote;
            // Failures are said as they are, so they're written for them, like Windows' lines.
            if (Brain.Model == null) return "I can't see your screen while my brain is offline. Pick a brain " + Brain.BrainMenu + ".";
            byte[] shot = Eyes.Jpeg(Eyes.Shoot(), 1280);
            if (shot == null) return Eyes.Missing;
            string seen;
            try
            {
                seen = Brain.See(shot, "You describe screenshots accurately and briefly.", LookAsk + (string.IsNullOrWhiteSpace(focus) ? "" : " Pay attention to: " + focus.Trim()), 160);
            }
            catch (BrainException error)
            {
                if (remote == null || error.Message != Brain.Sightless(remote)) throw;
                return "I can't see your screen: " + remote.Model + " can't look at pictures. Pick one that can " + Brain.BrainMenu + (remote.Kind == "nvidia" ? ", like nvidia/nemotron-3.5-super-vl-120b-a12b." : ".");
            }
            if (seen.Length == 0) return "I looked, but couldn't make out your screen clearly enough to describe it.";
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
            string head = "Capri v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + " | " + system + " | brain: " + brain + " | voice: " + (Speaker.Ready ? Speaker.Name : "none");
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
                updateItem.Click += Guarded(delegate { Browse(updatePage); });
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

        internal void CopyFingersSetup()
        {
            Avalonia.Input.Platform.IClipboard board = Clipboard;
            if (board == null) return;
            try { board.SetTextAsync(Fingers.Setup); }
            catch (Exception) { return; }
            Talk.Answer("(mouse setup)", "Copied a command for you. Paste it in a terminal, type your password, then close and open me again so I can use your mouse and keyboard.");
        }

        private HerCursor herPointer;

        private HerCursor HerPointer { get { if (herPointer == null) herPointer = new HerCursor { Topmost = true }; return herPointer; } }

        // A spot 0 to 1 across the bounding box of every screen, in desktop pixels: where her pointer goes for it.
        private PixelPoint DesktopPixel(double x, double y)
        {
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (Screen screen in Screens.All)
            {
                left = Math.Min(left, screen.Bounds.X);
                top = Math.Min(top, screen.Bounds.Y);
                right = Math.Max(right, screen.Bounds.Right);
                bottom = Math.Max(bottom, screen.Bounds.Bottom);
            }
            if (right <= left || bottom <= top) return new PixelPoint(0, 0);
            return new PixelPoint(left + (int)Math.Round(x * (right - left - 1)), top + (int)Math.Round(y * (bottom - top - 1)));
        }

        // Where the mouse is, 0 to 1 across the bounding box of every screen, which is how her mouse and her screenshots map too.
        private double[] MouseShare()
        {
            PixelPoint? at = Mouse();
            if (at == null) return null;
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            foreach (Screen screen in Screens.All)
            {
                left = Math.Min(left, screen.Bounds.X);
                top = Math.Min(top, screen.Bounds.Y);
                right = Math.Max(right, screen.Bounds.Right);
                bottom = Math.Max(bottom, screen.Bounds.Bottom);
            }
            if (right <= left || bottom <= top) return null;
            return new[] { (at.Value.X - left) / (double)(right - left - 1), (at.Value.Y - top) / (double)(bottom - top - 1) };
        }

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
            if (settled)
            {
                double age = now - huskMoodAt;
                if (age >= 0 && age < 1.2)
                {
                    double dip = Math.Sin(age / 1.2 * Math.PI);
                    if (huskMood == "prompt" || huskMood == "done") body.Breathe -= 9 * dip;
                    if (huskMood == "error") { body.Breathe += 14 * dip; body.Tilt -= 8 * dip; }
                    if (huskMood == "failure" && age < 0.5) body.Tilt += 8 * Math.Sin(age * 50) * (1 - age * 2);
                }
            }
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
            if (fallTo == null)
            {
                // A bug finding somewhere to land stops her fall where she is, so it can't hold up the rest of her every frame.
                try { fallTo = Perches.Below(Ledges(), hip, 0, int.MaxValue, 0, Headroom, ScreenAt); }
                catch (Exception) { falling = false; Save(); throw; }
            }
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
            Program.Guard(() =>
            {
                if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || outro != null) return;
                Point at = e.GetPosition(this);
                if (!Opaque(at)) return;
                // Where the press is comes first and the capture next, so a bug in either leaves her not pressed, instead of
                // pressed with no capture to tell her when the mouse lets go.
                PixelPoint down = DragPoint(at);
                e.Pointer.Capture(this);
                holder = e.Pointer;
                pressed = true;
                clicks++;
                dragging = false;
                dragPending = false;
                downAt = down;
                grab = new PixelPoint(down.X - anchor.X, down.Y - anchor.Y);
            });
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            Program.Guard(() =>
            {
                pointer = e.GetPosition(this);
                if (!pressed) { Stroke(this.PointToScreen(pointer)); return; }
                PixelPoint at = DragPoint(pointer);
                int dx = at.X - downAt.X;
                int dy = at.Y - downAt.Y;
                if (!dragging && dx * dx + dy * dy < 25) return;
                if (!dragging) Grab();
                dragAt = at;
                dragPending = true;
            });
        }

        private PixelPoint DragPoint(Point at)
        {
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    PixelPoint? screen = MouseAt != null ? MouseAt() : Desk.Grabbed();
                    if (screen != null) return screen.Value;
                }
                catch (Exception) { }
            }
            return ToScreen != null ? ToScreen(at) : this.PointToScreen(at);
        }

        internal void DragFrame()
        {
            if (!pressed || !dragging || !dragPending) return;
            dragPending = false;
            Move(new PixelPoint(dragAt.X - grab.X, dragAt.Y - grab.Y));
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            pointer = new Point(-1, -1);
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            base.OnPointerReleased(e);
            Program.Guard(() =>
            {
                if (!pressed || e.InitialPressMouseButton != MouseButton.Left) return;
                if (dragging)
                {
                    dragAt = DragPoint(e.GetPosition(this));
                    dragPending = true;
                    DragFrame();
                }
                pressed = false;
                holder = null;
                e.Pointer.Capture(null);
                if (dragging) Let((e.KeyModifiers & KeyModifiers.Shift) != 0);
                else React();
            });
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            base.OnPointerCaptureLost(e);
            Program.Guard(Unhold);
        }

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsVisibleProperty && !IsVisible) Program.Guard(Unhold);
        }

        // A lost capture has no release event, so she used to stay held after the mouse left her. Avalonia rarely says a grab
        // was lost (X11 only when her window is disabled, macOS only on losing focus during an implicit capture), so losing
        // focus or being hidden lets go of her the same way.
        private void Unhold()
        {
            if (!pressed) return;
            pressed = false;
            dragPending = false;
            IPointer held = holder;
            holder = null;
            if (held != null && held.Captured == this) held.Capture(null);
            if (!dragging) return;
            if (quitting || !IsVisible) { dragging = false; return; }
            Let(false);
        }

        // Letting go of her: she falls onto the window below, or with Shift held sits right there, like on Windows.
        internal void Let(bool here)
        {
            if (here)
            {
                if (Lost()) Reclaim();
                dragging = false;
                SitHere();
                return;
            }
            // A ledge below where she's let go catches her before anything moves her, so a drop with most of her showing no
            // longer jumps her back in first. Only with nothing to land on is she brought back.
            if (Drop() || !Lost()) return;
            Reclaim();
            Drop();
        }

        // Her drawn box on the desktop, in pixels: the cell of the clip she's showing.
        internal PixelRect DrawnBox
        {
            get
            {
                Rect place = body.Place(clip);
                return new PixelRect(anchor.X + Px(FrameX + place.X), anchor.Y + Px(FrameY + place.Y), Math.Max(1, Px(place.Width)), Math.Max(1, Px(place.Height)));
            }
        }

        // Let go off every screen, or with less than half of her drawn box on one, she's out of reach. Her hip alone used to
        // decide, which missed a drop with most of her off the right or top edge.
        private bool Lost()
        {
            PixelPoint hip = Hip;
            if (Screens.ScreenFromPoint(new PixelPoint(hip.X, hip.Y - 1)) == null) return true;
            PixelRect box = DrawnBox;
            long shown = 0;
            foreach (Screen screen in Screens.All)
            {
                PixelRect on = box.Intersect(screen.Bounds);
                shown += (long)on.Width * on.Height;
            }
            return shown * 2 < (long)box.Width * box.Height;
        }

        // Letting go outside every screen used to save her where nobody could reach her.
        private void Reclaim()
        {
            Screen screen = ScreenHere() ?? (Screens.All.Count > 0 ? Screens.All[0] : null);
            if (screen == null) return;
            PixelRect area = screen.WorkingArea;
            int size = Px(body.Size);
            Move(new PixelPoint(Math.Max(area.X, Math.Min(anchor.X, area.Right - size)), Math.Max(area.Y, Math.Min(anchor.Y, area.Bottom - size))));
        }

        internal int Pets { get { return petTicket; } }

        internal bool Smiling { get { return seat != null ? clip == perch && blinkWith == perchBlink : clip == grin; } }

        // A spot for the checks to press or stroke, with her under it and 12 px either side. The spot itself has to be on her
        // too: in half her seated dance's frames, the first row crosses the gap between two spikes of her hair with her on
        // both sides, and a press in that gap rightly doesn't hold her.
        internal Point? BodySpot()
        {
            for (double y = Bounds.Height / 4; y < Bounds.Height * 3 / 4; y += 3)
                for (double x = 20; x < Bounds.Width - 20; x += 3)
                    if (Inside(new Point(x, y)) && Inside(new Point(x - 12, y)) && Inside(new Point(x + 12, y))) return new Point(x, y);
            return null;
        }

        // A trip through screen pixels can round a point a pixel off her, so a test spot keeps 3 px of her on every side.
        private bool Inside(Point at)
        {
            for (int dx = -3; dx <= 3; dx += 3)
                for (int dy = -3; dy <= 3; dy += 3)
                    if (!Opaque(new Point(at.X + dx, at.Y + dy))) return false;
            return true;
        }

        internal void Stroke(PixelPoint cursor)
        {
            if (!HeadPats || pressed || dragging || falling || outro != null || Talk.Typing) { ForgetStroke(); return; }
            // Her window takes the pointer over her see-through edges too, so a pat that overshoots her outline skips that
            // spot instead of starting over.
            if (!Opaque(this.PointToClient(cursor))) return;
            double now = Now;
            bool turned = Turned(cursor.X, ref petX, ref petWay, petTurns, now) | Turned(cursor.Y, ref petY, ref petRise, petRises, now);
            if (!turned || (petTurns.Count < 3 && petRises.Count < 3) || now - petAt < 20) return;
            ForgetStroke();
            petAt = now;
            Petted();
        }

        // Side to side or up and down: a turn after at least 6 px each way counts, and three on one axis inside 2 s make a pat.
        private static bool Turned(int at, ref int from, ref int way, List<double> turns, double now)
        {
            int moved = at - from;
            if (Math.Abs(moved) < 6) return false;
            int heading = Math.Sign(moved);
            from = at;
            bool turned = way != 0 && heading != way;
            if (turned) turns.Add(now);
            way = heading;
            turns.RemoveAll(time => now - time > 2);
            return turned;
        }

        private void ForgetStroke()
        {
            petTurns.Clear();
            petRises.Clear();
            petWay = petRise = 0;
        }

        private void Petted()
        {
            EndWalk();
            if (seat != null) { Play(perch); blinkWith = perchBlink; }
            else Play(grin);
            int ticket = ++petTicket;
            petWaits = 0;
            DispatcherTimer.RunOnce(() => PetLine(ticket), TimeSpan.FromMilliseconds(1300));
        }

        internal void Greet(int ticket, int hour)
        {
            greetTried = true;
            string line = Brain.Greeting(hour);
            if (line == null || ticket != asking || outro != null || !Talk.Quiet) return;
            byte[] hello = Hello(Brain.GreetingClip(hour));
            if (hello == null) { Reply(++asking, "startup", line); return; }
            ++asking;
            Talk.Answer("startup", line, Voice.Seconds(hello));
            voice.Play(hello);
        }

        private bool greetTried, newsPending;
        private int newsTicket = -1;
        private long newsQuiet;

        internal void StartNews(bool existed)
        {
            newsTicket = -1;
            newsPending = existed && Saved.LastVersion != News.Version;
            if (!existed) KeepNews();
        }

        private void KeepNews()
        {
            newsPending = false;
            newsTicket = -1;
            Saved.LastVersion = News.Version;
            Save();
        }

        private bool NewsBusy(long quiet)
        {
            return quiet < 0 || quiet > 60000 || Hushed || away || !IsVisible || quitting || dragging || pressed || falling || outro != null || popOut >= 0 || nudgeOut >= 0 || (ContextMenu != null && ContextMenu.IsOpen) || JobActive || Talk.Typing || FullScreen();
        }

        private bool HoldNews(int ticket)
        {
            if (ticket != newsTicket || !NewsBusy(newsQuiet)) return false;
            newsTicket = -1;
            asking++;
            Talk.Dismiss(false);
            return true;
        }

        internal void CheckNews(long quiet)
        {
            newsQuiet = quiet;
            if (!newsPending || !greetTried || newsTicket == asking) return;
            if (NewsBusy(quiet) || !Talk.Quiet || !Talk.Finished) return;
            if (Think == null && Brain.Model == null)
            {
                Talk.Answer("(updated)", News.Line);
                KeepNews();
                return;
            }
            Brain.Cancel();
            newsTicket = ++asking;
            Talk.Thinking("(updated)");
            Ask(newsTicket, News.Prompt, 0);
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
            if (ticket != petTicket || outro != null || away) return;
            // Busy talking holds the line a few seconds instead of dropping it.
            if (!Talk.Quiet)
            {
                if (++petWaits <= PetWaits) DispatcherTimer.RunOnce(() => PetLine(ticket), TimeSpan.FromMilliseconds(500));
                else petAt = -60;   // never said, so the next pat counts straight away
                return;
            }
            if (clip == grin) Play(idle);
            string line = PetLines[petLine++ % PetLines.Length];
            Talk.Answer("pets you", line);
            TypeAlong(line, ticket);
        }

        internal void ForgetPets()
        {
            petTicket++;
            petAt = -60;
            ForgetStroke();
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
            // Her ledges are found before the drag ends, and a bug there still ends it, with a fall that looks again, so it
            // can't leave her floating where she was let go.
            Ledge target;
            try { target = Perch(Ledges(), Hip); }
            catch (Exception) { dragging = false; Fall(); throw; }
            dragging = false;
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

        // Her menu's clicks, the tray's included, go through Program.Guard, so a bug in one is written down instead of ending her.
        private static EventHandler<Avalonia.Interactivity.RoutedEventArgs> Guarded(Action click) { return delegate { Program.Guard(click); }; }

        private ContextMenu Menu()
        {
            ContextMenu menu = new ContextMenu { Background = MainframeTheme.Ground, Foreground = MainframeTheme.White, BorderBrush = MainframeTheme.Brim, BorderThickness = new Thickness(1), FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size };
            MenuItem talk = new MenuItem { Header = "Talk to her" };
            talk.Click += Guarded(delegate { Talk.Open(); });
            quietItem = new MenuItem { Header = "Quiet", ToggleType = MenuItemToggleType.CheckBox };
            quietItem.Click += Guarded(delegate { ToggleShush(); });
            sitItem = new MenuItem { Header = "Sit here" };
            sitItem.Click += Guarded(delegate { if (seat != null && seat.Free) StandUp(); else SitHere(); });
            hangItem = new MenuItem { Header = "Hang here" };
            hangItem.Click += Guarded(delegate { if (hanging) LetGo(); else HangHere(); });
            napItem = new MenuItem { Header = "Go to sleep" };
            napItem.Click += Guarded(delegate { Nap(!napAsked && clip != sleep); });
            MenuItem move = new MenuItem { Header = "Move" };
            for (int i = 0; i < WanderNames.Length; i++)
            {
                int level = i;
                MenuItem pick = new MenuItem { Header = i == 0 ? "Stay put" : "Wander: " + WanderNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "wander" };
                pick.Click += Guarded(delegate { SetWander(level); });
                wanderPicks.Add(pick);
                move.Items.Add(pick);
            }
            move.Items.Add(new Separator());
            musicItem = new MenuItem { Header = "Dance to music", ToggleType = MenuItemToggleType.CheckBox };
            musicItem.Click += Guarded(delegate { SetDanceToMusic(Saved.NoDance); });
            move.Items.Add(musicItem);
            danceItem = new MenuItem { Header = "Dance", ToggleType = MenuItemToggleType.CheckBox };
            danceItem.Click += Guarded(delegate { Animate(dancing ? "stop_dancing" : "dance"); });
            move.Items.Add(danceItem);
            screenItem = new MenuItem { Header = "Go to the next screen" };
            screenItem.Click += Guarded(delegate { MoveSelf("other_screen", null); });
            move.Items.Add(screenItem);
            move.Items.Add(new Separator());
            MenuItem sleeps = new MenuItem { Header = "Fall asleep after" };
            for (int i = 0; i < Sleeps.Length; i++)
            {
                int seconds = Sleeps[i];
                MenuItem pick = new MenuItem { Header = SleepNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "sleeps" };
                pick.Click += Guarded(delegate { SetSleepAfter(seconds); });
                sleepPicks.Add(pick);
                sleeps.Items.Add(pick);
            }
            move.Items.Add(sleeps);
            introItem = new MenuItem { Header = "Glitch in at startup", ToggleType = MenuItemToggleType.CheckBox };
            introItem.Click += Guarded(delegate { SetIntro(Saved.NoIntro); });
            move.Items.Add(introItem);
            brainMenu = new MenuItem { Header = "Brain" };
            brainMenu.Items.Add(new MenuItem { Header = "Looking for models...", IsEnabled = false });
            MenuItem settings = new MenuItem { Header = "Settings" };
            MenuItem language = new MenuItem { Header = "Language" };
            for (int i = 0; i < Brain.Languages.GetLength(0); i++)
            {
                string name = Brain.Languages[i, 1];
                MenuItem pick = new MenuItem { Header = Brain.Languages[i, 0], ToggleType = MenuItemToggleType.Radio, GroupName = "language", Tag = name };
                pick.Click += Guarded(delegate { SetLanguage(name); });
                languagePicks.Add(pick);
                language.Items.Add(pick);
            }
            MenuItem replies = new MenuItem { Header = "Reply length" };
            MenuItem recall = new MenuItem { Header = "Chat memory" };
            for (int i = 0; i < Lengths.Length; i++)
            {
                int level = i;
                MenuItem reply = new MenuItem { Header = Lengths[i], ToggleType = MenuItemToggleType.Radio, GroupName = "replies" };
                reply.Click += Guarded(delegate { SetReplies(level); });
                replyPicks.Add(reply);
                replies.Items.Add(reply);
                MenuItem keep = new MenuItem { Header = Lengths[i], ToggleType = MenuItemToggleType.Radio, GroupName = "recall" };
                keep.Click += Guarded(delegate { SetRecall(Recalls[level]); });
                recallPicks.Add(keep);
                recall.Items.Add(keep);
            }
            MenuItem sounds = new MenuItem { Header = "Sounds" };
            for (int i = 0; i < SoundNames.Length; i++)
            {
                int level = SoundLevels[i];
                MenuItem pick = new MenuItem { Header = SoundNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "sounds" };
                pick.Click += Guarded(delegate { SetSounds(level); });
                soundPicks.Add(pick);
                sounds.Items.Add(pick);
            }
            sounds.Items.Add(new Separator());
            typingItem = new MenuItem { Header = "Typing sound", ToggleType = MenuItemToggleType.CheckBox };
            typingItem.Click += Guarded(delegate { SetTyping(!TypingSound); });
            sounds.Items.Add(typingItem);
            MenuItem notes = new MenuItem { Header = "Open her notes (me.md)" };
            notes.Click += Guarded(delegate { OpenNotes(); });
            MenuItem personality = new MenuItem { Header = "Edit her personality (personality.md)" };
            personality.Click += Guarded(delegate { OpenPersonality(); });
            MenuItem resetPersonality = new MenuItem { Header = "Reset her personality to default" };
            resetPersonality.Click += Guarded(delegate { ResetPersonality(); });
            memoriesItem = new MenuItem { Header = "Remember our chats", ToggleType = MenuItemToggleType.CheckBox };
            memoriesItem.Click += Guarded(delegate { SetRemembering(Saved.NoMemories); });
            MenuItem memories = new MenuItem { Header = "Open her memories (memories.md)" };
            memories.Click += Guarded(delegate { OpenMemories(); });
            MenuItem nickname = new MenuItem { Header = "What should she call you?" };
            nickname.Click += Guarded(delegate { AskNickname(); });
            settings.Items.Add(nickname);
            MenuItem meet = new MenuItem { Header = "Get to know me" };
            meet.Click += Guarded(delegate { GetToKnow(); });
            settings.Items.Add(meet);
            settings.Items.Add(language);
            settings.Items.Add(replies);
            settings.Items.Add(recall);
            MenuItem bubble = new MenuItem { Header = "Bubble" };
            for (int i = 0; i < BubbleNames.Length; i++)
            {
                int level = i;
                MenuItem pick = new MenuItem { Header = BubbleNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "bubble" };
                pick.Click += Guarded(delegate { SetBubbleTime(level); });
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
                pick.Click += Guarded(delegate { SetVolume(percent); });
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
                pick.Click += Guarded(delegate { SetPitch(semitones); HearStyle(); });
                pitchPicks.Add(pick);
                pitch.Items.Add(pick);
            }
            MenuItem speed = new MenuItem { Header = "Speed" };
            for (int i = 0; i < Paces.Length; i++)
            {
                int percent = Paces[i];
                MenuItem pick = new MenuItem { Header = PaceNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "pace" };
                pick.Click += Guarded(delegate { SetPace(percent); HearStyle(); });
                pacePicks.Add(pick);
                speed.Items.Add(pick);
            }
            MenuItem robot = new MenuItem { Header = "Robot" };
            MenuItem glitchy = new MenuItem { Header = "Glitch" };
            for (int i = 0; i < Amounts.Length; i++)
            {
                int amount = Amounts[i];
                MenuItem robotPick = new MenuItem { Header = AmountNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "robot" };
                robotPick.Click += Guarded(delegate { SetRobot(amount); HearStyle(); });
                robotPicks.Add(robotPick);
                robot.Items.Add(robotPick);
                MenuItem glitchPick = new MenuItem { Header = AmountNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "glitch" };
                glitchPick.Click += Guarded(delegate { SetGlitch(amount); HearStyle(); });
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
            layersItem.Click += Guarded(delegate { SetLayers(VoiceFx.Layers.Turned(!VoiceFx.Layers.On)); HearStyle(); });
            style.Items.Add(layersItem);
            for (int layer = 1; layer <= 2; layer++)
            {
                int which = layer;
                MenuItem layerPitch = new MenuItem { Header = "Layer " + layer + " pitch" };
                foreach (double semitones in LayerPitches)
                {
                    double picked = semitones;
                    MenuItem pick = new MenuItem { Header = VoiceFx.Layering.PitchName(semitones), ToggleType = MenuItemToggleType.Radio, GroupName = "layer" + layer + "pitch" };
                    pick.Click += Guarded(delegate { SetLayers(VoiceFx.Layers.With(which, picked, VoiceFx.Layers.DelayOf(which))); HearStyle(); });
                    layerPitchPicks[layer - 1].Add(pick);
                    layerPitch.Items.Add(pick);
                }
                MenuItem layerDelay = new MenuItem { Header = "Layer " + layer + " delay" };
                foreach (int ms in LayerDelays)
                {
                    int picked = ms;
                    MenuItem pick = new MenuItem { Header = VoiceFx.Layering.DelayName(ms), ToggleType = MenuItemToggleType.Radio, GroupName = "layer" + layer + "delay" };
                    pick.Click += Guarded(delegate { SetLayers(VoiceFx.Layers.With(which, VoiceFx.Layers.PitchOf(which), picked)); HearStyle(); });
                    layerDelayPicks[layer - 1].Add(pick);
                    layerDelay.Items.Add(pick);
                }
                style.Items.Add(layerPitch);
                style.Items.Add(layerDelay);
            }
            settings.Items.Add(style);
            checkInsItem = new MenuItem { Header = "Check in on me", ToggleType = MenuItemToggleType.CheckBox };
            checkInsItem.Click += Guarded(delegate { SetCheckIns(Saved.NoCheckIns); });
            settings.Items.Add(checkInsItem);
            MenuItem pops = new MenuItem { Header = "Pop-ins" };
            for (int i = 0; i < PopNames.Length; i++)
            {
                int pace = i;
                MenuItem pick = new MenuItem { Header = PopNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "pops" };
                pick.Click += Guarded(delegate { SetPopPace(pace); });
                popPicks.Add(pick);
                pops.Items.Add(pick);
            }
            settings.Items.Add(pops);
            onTopItem = new MenuItem { Header = "Always on top", ToggleType = MenuItemToggleType.CheckBox };
            onTopItem.Click += Guarded(delegate { SetOnTop(Saved.NotOnTop); });
            settings.Items.Add(onTopItem);
            watchItem = new MenuItem { Header = "Watch my screen", ToggleType = MenuItemToggleType.CheckBox };
            watchItem.Click += Guarded(delegate { SetWatch(!Saved.Watch); });
            settings.Items.Add(watchItem);
            MenuItem gaps = new MenuItem { Header = "Glance every" };
            for (int i = 0; i < GlanceGaps.Length; i++)
            {
                int seconds = GlanceGaps[i];
                MenuItem pick = new MenuItem { Header = GlanceGapNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "gaps" };
                pick.Click += Guarded(delegate { SetGlanceGap(seconds); });
                gapPicks.Add(pick);
                gaps.Items.Add(pick);
            }
            settings.Items.Add(gaps);
            caughtUpItem = new MenuItem { Header = "Keep her caught up", ToggleType = MenuItemToggleType.CheckBox };
            caughtUpItem.Click += Guarded(delegate { SetCaughtUp(Saved.NoCaughtUp); });
            settings.Items.Add(caughtUpItem);
            loginItem = new MenuItem { Header = "Start at login", ToggleType = MenuItemToggleType.CheckBox };
            loginItem.Click += Guarded(delegate { SetLogin(!StartsAtLogin); });
            settings.Items.Add(loginItem);
            discordItem = new MenuItem { Header = "Show on Discord", ToggleType = MenuItemToggleType.CheckBox };
            discordItem.Click += Guarded(delegate { SetDiscord(Saved.NoDiscord); });
            settings.Items.Add(discordItem);
            // Security, like Windows' SECURITY window (Lachy, 29 Oct): Lockdown, Guarded, Trusted and Blackwall set the toggles
            // under them, and flipping one makes it Custom. Each toggle has its plain line. Off means she asks first and does it
            // on your yes. Mac and Linux have the first five, and Linux her mouse and keyboard jobs once uinput is set up; "Hey Capri" is Windows-only so far.
            securityMenu = new MenuItem { Header = "Security: " + Security.PresetName };
            for (int i = 0; i <= Security.Custom; i++)
            {
                int preset = i;
                MenuItem pick = new MenuItem { Header = Security.Presets[i], ToggleType = MenuItemToggleType.Radio, GroupName = "security", IsEnabled = i != Security.Custom };
                ToolTip.SetTip(pick, Security.PresetLine(i));
                pick.Click += Guarded(delegate { if (preset != Security.Custom) Security.Pick(preset); });
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
                item.Click += Guarded(delegate { Security.Set(toggle, !Security.On(toggle)); });
                securityToggles.Add(item);
                securityMenu.Items.Add(item);
            }
            if (Program.Smoke == null && !fingers && OperatingSystem.IsLinux())
            {
                securityMenu.Items.Add(new Separator());
                MenuItem setup = new MenuItem { Header = "Set up mouse and keyboard..." };
                ToolTip.SetTip(setup, "Copies the one command that lets her make her own mouse and keyboard through /dev/uinput.");
                setup.Click += Guarded(delegate { CopyFingersSetup(); });
                securityMenu.Items.Add(setup);
            }
            settings.Items.Add(securityMenu);
            settings.Items.Add(notes);
            settings.Items.Add(personality);
            settings.Items.Add(resetPersonality);
            settings.Items.Add(memoriesItem);
            settings.Items.Add(memories);
            MenuItem sizes = new MenuItem { Header = "Size" };
            for (int i = 0; i < Sizes.Length; i++)
            {
                double size = Sizes[i];
                MenuItem pick = new MenuItem { Header = SizeNames[i] };
                pick.Click += Guarded(delegate { Resize(size); });
                sizes.Items.Add(pick);
            }
            MenuItem discord = new MenuItem { Header = "Join the Discord" };
            discord.Click += Guarded(delegate { Browse(DiscordPage); });
            MenuItem help = new MenuItem { Header = "Help" };
            MenuItem helpPage = new MenuItem { Header = "Help page" };
            helpPage.Click += Guarded(delegate { Browse(HelpPage); });
            MenuItem copyErrors = new MenuItem { Header = "Copy error details" };
            copyErrors.Click += Guarded(delegate { CopyErrors(); });
            help.Items.Add(helpPage);
            help.Items.Add(copyErrors);
            MenuItem quit = new MenuItem { Header = "Quit Capri" };
            quit.Click += Guarded(delegate { Quit(); });
            move.Items.Clear();
            move.Items.Add(sitItem);
            move.Items.Add(hangItem);
            move.Items.Add(napItem);
            move.Items.Add(screenItem);
            move.Items.Add(danceItem);
            MenuItem wander = new MenuItem { Header = "Wander" };
            wander.Click += Guarded(delegate { SetWander(Saved.Wander == 0 ? 2 : 0); });
            move.Items.Add(wander);
            MenuItem title = new MenuItem { Header = "CAPRI / v" + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString(3), IsEnabled = false, Foreground = MainframeTheme.Rose };
            MenuItem frame = new MenuItem { Header = "Open Capri Mainframe" };
            frame.Click += Guarded(delegate { OpenMainframe(); });
            hideItem = new MenuItem { Header = "Hide Capri" };
            hideItem.Click += Guarded(delegate { if (Away) ComeBack(); else HideAway(); });
            MenuItem update = new MenuItem { Header = "Check for updates" };
            update.Click += Guarded(delegate { CheckUpdate(); });
            menu.Items.Add(title);
            menu.Items.Add(new Separator());
            menu.Items.Add(frame);
            menu.Items.Add(hideItem);
            menu.Items.Add(sizes);
            menu.Items.Add(move);
            menu.Items.Add(help);
            menu.Items.Add(update);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);
            menu.Opening += delegate { Sync(); hideItem.Header = Away ? "Show Capri" : "Hide Capri"; };
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
                Talk.Answer("(voice check)", Unspoken);
                return;
            }
            int ticket = ++asking;
            voice.Stop();
            ISpeaker speaker = Speaker;
            string id = speaker == elevenLabs ? voice.VoiceId : Saved.VoiceProvider + "\n" + localVoice.Url + "\n" + localVoice.Model + "\n" + localVoice.Speaker + "\n" + localVoice.Format;
            byte[] said = styleVoice == id ? styleSaid : null;
            Talk.Thinking("(voice check)");
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string problem = null;
                try
                {
                    if (said == null) said = speaker.Speak(StyleLine);
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
        // spot on another screen, and she sits, stands, hangs, hides and comes back. Sitting on a window they name, lying down,
        // walking or wandering off when asked, riding the cursor and the Mainframe are Windows-only so far, and her hands say so
        // before they get here.
        internal string MoveSelf(string action, string screen)
        {
            if (action == "move_mainframe") return MoveMainframe(screen);
            if (action == "hide")
            {
                if (away) return "Already hidden.";
                hideAsked = true;
                return "Hiding. Pick Show Capri in my tray menu to bring me back.";
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

        // How one of her switches is set right now for her settings tool, the way her menus show it, or null for one only Windows has.
        internal bool? SettingOn(string name)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(delegate { return SettingOn(name); }).GetAwaiter().GetResult();
            int careKind = Array.IndexOf(Hands.CareSettings, name);
            if (careKind >= 0) return CareOn((CareKind)careKind);
            switch (name)
            {
                case "head_pats": return HeadPats;
                case "follow_ups": return FollowUps;
                case "keep_awake": return KeepAwake;
                case "voice": return Speaks;
                case "quiet": return Shushed;
                case "check_ins": return CheckIns;
                case "wander": return Saved.Wander > 0;
                case "dance_to_music": return !Saved.NoDance;
                case "always_on_top": return OnTop;
                case "typing_sound": return TypingSound;
                case "glitch_in": return !Saved.NoIntro;
                case "watch_my_screen": return Saved.Watch;
                case "startup": return StartsAtLogin;
                case "discord_presence": return !Saved.NoDiscord;
                case "voice_layers": return VoiceFx.Layers.On;
                case "show_advanced": return Saved.AdvancedSettings;
                default: return null;
            }
        }

        // Flips one of her switches the way its menu item does, without her line about it, since her brain tells them what happened.
        internal string SetSetting(string name, bool on)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(delegate { return SetSetting(name, on); }).GetAwaiter().GetResult();
            if (SettingOn(name) == on) return null;
            int careKind = Array.IndexOf(Hands.CareSettings, name);
            if (careKind >= 0) { SetCare((CareKind)careKind, on); return null; }
            switch (name)
            {
                case "head_pats": SetHeadPats(on); break;
                case "follow_ups": SetFollowUps(on); break;
                case "keep_awake": SetKeepAwake(on); break;
                case "voice": SetSpeak(on); break;
                case "quiet": ToggleShush(false); break;
                case "check_ins": SetCheckIns(on); break;
                case "wander": SetWander(on ? 2 : 0); break;
                case "dance_to_music": SetDanceToMusic(on); break;
                case "always_on_top": SetOnTop(on); break;
                case "typing_sound": SetTypingMode(on ? 1 : 2); break;
                case "glitch_in": SetIntro(on); break;
                case "watch_my_screen": SetWatch(on, false); break;
                case "startup": SetLogin(on, false); break;
                case "discord_presence": SetDiscord(on); break;
                case "voice_layers": SetLayers(VoiceFx.Layers.Turned(on)); break;
                case "show_advanced": SetAdvancedSettings(on); break;
            }
            Sync();
            return name == "voice" && on && !Speaks ? "Set up a voice under VOICE in Capri Mainframe first." : null;
        }

        // The step nearest a value, for sliders whose value can sit between their steps.
        private static int Nearest(int[] steps, int value)
        {
            int near = 0;
            for (int i = 0; i < steps.Length; i++) if (Math.Abs(steps[i] - value) < Math.Abs(steps[near] - value)) near = i;
            return near;
        }

        // Which step one of her sliders is on, counted from the bottom the way her menus show it, or -1 for one only Windows has.
        internal int LevelOf(string name)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(delegate { return LevelOf(name); }).GetAwaiter().GetResult();
            int careKind = Array.IndexOf(Hands.CareSettings, name);
            if (careKind >= 0) return Math.Max(0, Array.IndexOf(Hands.ValuesFor(name), CareTime((CareKind)careKind)));
            switch (name)
            {
                case "size": return Math.Max(0, Array.IndexOf(Sizes, Saved.Size));
                case "wander": return Saved.Wander;
                case "pop_ins": return PopPace;
                case "sleep": return Nearest(Sleeps, SleepAfter);
                case "volume": return Nearest(Volumes, Voice.Loudness);
                case "voice_speed": return Nearest(Paces, VoiceFx.Speed);
                case "replies": return Brain.Wordiness;
                case "reply_limit": return Brain.ReplyLength;
                case "mainframe_size": return Array.IndexOf(new[] { 0, 100, 125, 150, 175, 200 }, FrameSize(Saved.MainframeSize));
                case "mainframe_colours": return MainframeTheme.ClampLook(Saved.MainframeColours);
                case "bubble": return BubbleTime;
                case "glance_gap": return Math.Max(0, Array.IndexOf(GlanceGaps, GlanceGap));
                case "voice_pitch": return VoiceFx.Pitch + VoiceFx.MostPitch;
                case "voice_robot": return Nearest(Amounts, VoiceFx.Robot);
                case "voice_glitch": return Nearest(Amounts, VoiceFx.Glitch);
                case "voice_layers": return VoiceFx.Layers.On ? 1 : 0;
                case "layer_1_pitch": return LayerPitchStep(VoiceFx.Layers.Pitch1);
                case "layer_2_pitch": return LayerPitchStep(VoiceFx.Layers.Pitch2);
                case "layer_1_delay": return Nearest(LayerDelays, VoiceFx.Layers.Delay1);
                case "layer_2_delay": return Nearest(LayerDelays, VoiceFx.Layers.Delay2);
                case "sounds": return Nearest(SoundLevels, Sfx.Level);
                case "language": return Math.Max(0, Array.IndexOf(Hands.StepsOf(name), Brain.Language ?? "English"));
                case "typing_sound": return Math.Max(0, Math.Min(2, Saved.Typing));
                default: return -1;
            }
        }

        // The step of a voice layer's pitch menu nearest a pitch in semitones.
        private static int LayerPitchStep(double pitch)
        {
            int near = 0;
            for (int i = 0; i < LayerPitches.Length; i++) if (Math.Abs(LayerPitches[i] - pitch) < Math.Abs(LayerPitches[near] - pitch)) near = i;
            return near;
        }

        // Moves one of her sliders to a step the way its menu does, without her line about it.
        internal string SetLevel(string name, int step)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(delegate { return SetLevel(name, step); }).GetAwaiter().GetResult();
            if (!Hands.CrossSetting(name)) return "That Capri setting is only on Windows for now.";
            step = Hands.ClampLevel(name, step);
            if (step < 0) return null;
            int careKind = Array.IndexOf(Hands.CareSettings, name);
            if (careKind >= 0) { SetCareTime((CareKind)careKind, Hands.ValuesFor(name)[step]); return null; }
            VoiceFx.Layering layers = VoiceFx.Layers;
            if (name.StartsWith("layer_", StringComparison.Ordinal)) SetLayers(new VoiceFx.Layering(layers.On, name == "layer_1_pitch" ? LayerPitches[step] : layers.Pitch1, name == "layer_1_delay" ? LayerDelays[step] : layers.Delay1, name == "layer_2_pitch" ? LayerPitches[step] : layers.Pitch2, name == "layer_2_delay" ? LayerDelays[step] : layers.Delay2));
            else switch (name)
            {
                case "size": Resize(Sizes[step]); break;
                case "wander": SetWander(step); break;
                case "pop_ins": SetPopPace(step); break;
                case "sleep": SetSleepAfter(Sleeps[step]); break;
                case "volume": SetVolume(Volumes[step]); break;
                case "voice_speed": SetPace(Paces[step]); break;
                case "replies": SetReplies(step); break;
                case "reply_limit": SetReplyLength(step); break;
                case "mainframe_size": SetMainframeSize(new[] { 0, 100, 125, 150, 175, 200 }[step]); break;
                case "mainframe_colours": SetMainframeColours(step); break;
                case "bubble": SetBubbleTime(step); break;
                case "glance_gap": SetGlanceGap(GlanceGaps[step]); break;
                case "voice_pitch": SetPitch(step - VoiceFx.MostPitch); break;
                case "voice_robot": SetRobot(Amounts[step]); break;
                case "voice_glitch": SetGlitch(Amounts[step]); break;
                case "sounds": SetSounds(SoundLevels[step]); break;
                case "language": SetLanguage(Hands.StepsOf(name)[step]); break;
                case "typing_sound": SetTypingMode(step); break;
            }
            Sync();
            return null;
        }

        // A voice preview her settings tool asked for plays once the reply it came with is over, unless they've asked something
        // else since.
        private bool styleCheckDue;
        private int styleAsked;
        internal void CheckStyle()
        {
            if (styleCheckDue && styleAsked != asking) styleCheckDue = false;
            if (styleCheckDue && Talk.Finished) { styleCheckDue = false; HearStyle(); }
        }
        // Her settings tool's actions. Mac and Linux have preview_voice, which plays her voice as it's set now after this reply.
        internal string SettingAction(string action)
        {
            if (!Dispatcher.UIThread.CheckAccess()) return Dispatcher.UIThread.InvokeAsync(delegate { return SettingAction(action); }).GetAwaiter().GetResult();
            if (action != "preview_voice") return "That action is only on Windows for now.";
            if (!Speaks) return "My voice isn't ready. Set it up under VOICE in Capri Mainframe first.";
            styleCheckDue = true;
            styleAsked = asking;
            return "I'll play my current voice after this reply.";
        }

        internal bool Shushed { get { return shushed; } }
        internal bool QuietShown { get { return quietItem.IsChecked; } }

        // Quiet or a nap they asked for: no glances, pop-ins, check-ins, cheers or jokes.
        private bool Hushed { get { return shushed || napAsked || napping; } }

        // Quiet, like Windows' quiet key, for when they focus or game: she stops what she's saying and doesn't glance, pop in,
        // check in, cheer or joke until it's turned off, but still answers when they talk to her. Starting her again ends it.
        internal void ToggleShush(bool say = true)
        {
            shushed = !shushed;
            nextPopIn = -1;
            nextGlance = -1;
            ForgetNudges();
            if (shushed) voice.Stop();
            if (say) Talk.Answer("(quiet)", shushed ? "Going quiet. Turn off Mute her in Capri Mainframe when you want me back." : "I'm back, choom.");
            Sync();
            if (ShushChanged != null) ShushChanged();
        }

        internal bool StartsAtLogin { get { return File.Exists(LoginFile); } }
        internal bool LoginShown { get { return loginItem.IsChecked; } }

        internal void SetLogin(bool on, bool say = true)
        {
            if (!Limbs.SetLogin(LoginFile, Limbs.SelfPath(), on, OperatingSystem.IsMacOS()) && say) Talk.Answer("(start at login)", "Couldn't change that, choom. I need to be able to write " + LoginFile + ".");
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
            if (MainframeTier > 0 && Brain.Remote == null) { NotifySettings(); return; }
            if (Brain.Remote != null) { ShowCloud(); ListCloud(); return; }
            string server;
            string serverKey;
            Brain.GetServer(out server, out serverKey);
            int request = ++localRequests;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] found = null;
                if (server != null)
                {
                    try { found = Brain.ServerModels(server, serverKey); }
                    catch (Exception) { }
                    Dispatcher.UIThread.Post(delegate
                    {
                        string currentServer, currentKey;
                        Brain.GetServer(out currentServer, out currentKey);
                        if (request == localRequests && MainframeTier == 0 && Brain.Remote == null && currentServer == server && currentKey == serverKey) ShowServer(found);
                    });
                    return;
                }
                try { found = Brain.InstalledModels(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate { if (request == localRequests && MainframeTier == 0 && Brain.Server == server && Brain.Remote == null) ShowBrains(found); });
            });
        }

        internal void ShowBrains(string[] found)
        {
            frameModels = found ?? Array.Empty<string>();
            if (MainframeTier == 0) frameProblem = found == null ? NoOllama : null;
            NotifySettings();
            if (Brain.Remote != null) { ShowCloud(); return; }
            brainMenu.Items.Clear();
            if (found == null) brainMenu.Items.Add(new MenuItem { Header = NoOllama, IsEnabled = false });
            else if (found.Length == 0)
            {
                MenuItem fetch = new MenuItem { Header = pulling ? Pulling : PullChoice, IsEnabled = !pulling };
                fetch.Click += Guarded(delegate { PullBrain(); });
                brainMenu.Items.Add(fetch);
            }
            else
                foreach (string name in found)
                {
                    string model = name;
                    MenuItem pick = new MenuItem { Header = model, ToggleType = MenuItemToggleType.Radio, GroupName = "brain", IsChecked = model == Brain.Model };
                    pick.Click += Guarded(delegate { PickBrain(model); });
                    brainMenu.Items.Add(pick);
                }
            brainMenu.Items.Add(new Separator());
            if (found != null)
            {
                MenuItem more = new MenuItem { Header = pulling ? Pulling : "Download a model (Ollama or Hugging Face)...", IsEnabled = !pulling };
                more.Click += Guarded(delegate { AskPull(); });
                brainMenu.Items.Add(more);
            }
            MenuItem own = new MenuItem { Header = "Use my own server (OpenAI API)..." };
            own.Click += Guarded(delegate { AskServer(); });
            brainMenu.Items.Add(own);
            MenuItem provider = new MenuItem { Header = "Use my own provider with a key (OpenAI API)..." };
            provider.Click += Guarded(delegate { AskProvider(); });
            brainMenu.Items.Add(provider);
            // A cloud key thinks for her instead, for PCs that can't run Ollama's models well. NVIDIA's is free.
            brainMenu.Items.Add(new Separator());
            MenuItem free = new MenuItem { Header = "Get a free NVIDIA key" };
            free.Click += Guarded(delegate { GetFreeKey(); });
            MenuItem paste = new MenuItem { Header = KeyChoice };
            paste.Click += Guarded(delegate { AskBrainKey(); });
            brainMenu.Items.Add(free);
            brainMenu.Items.Add(paste);
        }

        private string Pulling { get { return "Downloading " + pullingName + (pulled >= 0 ? ": " + pulled + "%" : "..."); } }

        // On their own server the Brain menu lists its models, and can change its address or go back to Ollama.
        internal void ShowServer(string[] found)
        {
            frameModels = found ?? Array.Empty<string>();
            NotifySettings();
            string server;
            string serverKey;
            Brain.GetServer(out server, out serverKey);
            if (Brain.Remote != null || server == null) return;
            frameProblem = found == null ? "Can't reach your server. Is it running?" : null;
            brainMenu.Items.Clear();
            brainMenu.Items.Add(new MenuItem { Header = "My server at " + server + (serverKey == null ? "" : " (with a key)"), IsEnabled = false });
            if (found == null) brainMenu.Items.Add(new MenuItem { Header = "Can't reach it. Is it running?", IsEnabled = false });
            else if (found.Length == 0) brainMenu.Items.Add(new MenuItem { Header = "It lists no models", IsEnabled = false });
            else
                foreach (string name in found)
                {
                    string model = name;
                    MenuItem pick = new MenuItem { Header = model, ToggleType = MenuItemToggleType.Radio, GroupName = "server", IsChecked = model == Brain.Model };
                    pick.Click += Guarded(delegate { PickServerModel(model); });
                    brainMenu.Items.Add(pick);
                }
            brainMenu.Items.Add(new Separator());
            MenuItem change = new MenuItem { Header = "Change server address (no key)..." };
            change.Click += Guarded(delegate { AskServer(); });
            MenuItem changeKey = new MenuItem { Header = "Change provider address with a key..." };
            changeKey.Click += Guarded(delegate { AskProvider(); });
            MenuItem key = new MenuItem { Header = serverKey == null ? "Set provider key..." : "Change provider key..." };
            key.Click += Guarded(delegate { AskProviderKey(); });
            MenuItem forget = new MenuItem { Header = "Forget provider key" };
            forget.Click += Guarded(delegate { ForgetServerKey(); });
            MenuItem back = new MenuItem { Header = "Stop using my server and use Ollama" };
            back.Click += Guarded(delegate { LeaveServer(); });
            brainMenu.Items.Add(change);
            brainMenu.Items.Add(changeKey);
            brainMenu.Items.Add(key);
            if (serverKey != null) brainMenu.Items.Add(forget);
            brainMenu.Items.Add(back);
        }

        internal void AskPull()
        {
            ComeBack();
            Talk.AskFor(BrainHeard, "Type a model and press Enter: an Ollama one like qwen3:4b, or a Hugging Face GGUF like hf.co/bartowski/Qwen_Qwen3-4B-Instruct-2507-GGUF:Q4_K_M or its huggingface.co link.", delegate(string typed)
            {
                string name = Brain.PullName(typed);
                if (name == null) { Talk.Answer(BrainHeard, "That isn't a model name I can download. Try qwen3:4b or hf.co/user/repo."); return; }
                PullBrain(name);
            });
        }

        internal void AskServer()
        {
            ComeBack();
            Talk.AskFor("(brain server)", "Type your server's address, like http://localhost:8080 for llama-server or http://localhost:1234 for LM Studio, and press Enter.", delegate(string typed) { UseServer(typed, null); });
        }

        internal void AskProvider()
        {
            ComeBack();
            Talk.AskFor("(brain provider)", "Type your provider's address, like https://api.commandcode.ai/provider, and press Enter.", AskProviderKeyFor);
        }

        private void AskProviderKeyFor(string typed)
        {
            string server = Brain.ServerAddress(typed);
            if (server == null) { Talk.Answer("(brain provider)", "That address doesn't work. Try https://... or http://localhost:8080."); return; }
            Talk.AskKey("(brain provider)", "Paste the API key for " + server + " and press Enter. It stays on this computer.", delegate(string key) { UseServer(server, key); });
        }

        internal void AskProviderKey()
        {
            string server;
            string ignored;
            Brain.GetServer(out server, out ignored);
            if (server == null) return;
            ComeBack();
            Talk.AskKey("(brain provider)", "Paste the API key for " + server + " and press Enter. It stays on this computer.", delegate(string key) { UseServer(server, key); });
        }

        private void ForgetServerKey()
        {
            Brain.Cancel();
            ++asking;
            string server;
            string ignored;
            Brain.GetServer(out server, out ignored);
            if (!WriteServerPair(ServerPairFile, server, null))
            {
                Talk.Answer("(brain provider)", "Couldn't remove the saved provider key.");
                return;
            }
            Brain.SetServer(server, null);
            WriteSecret(ServerKeyFile, null, "provider key");
            RefreshBrains();
        }

        internal void UseServer(string typed) { UseServer(typed, null); }

        internal void UseServer(string typed, string key)
        {
            string server = Brain.ServerAddress(typed);
            if (server == null) { Talk.Answer("(brain server)", "That address doesn't work. Try http://localhost:8080."); return; }
            key = (key ?? "").Trim();
            if (key.Length == 0) key = null;
            if (key != null && !Brain.ServerKeySafe(server))
            {
                Talk.Answer("(brain provider)", "Use https:// for a provider elsewhere. A key over plain http isn't safe.");
                return;
            }
            int ticket = ++asking;
            Talk.Thinking("(brain server)");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string[] found = null;
                try { found = Brain.ServerModels(server, key); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    if (ticket != asking) return;
                    if (found == null || found.Length == 0)
                    {
                        if (ticket == asking) Talk.Answer("(brain server)", found == null ? (key == null ? "Can't reach a server at " + server + ". Is it running?" : "Can't reach " + server + " with that key. Check the address and the key.") : "Your server at " + server + " lists no models. Load one in it first.");
                        return;
                    }
                    if (!WriteServerPair(ServerPairFile, server, key))
                    {
                        Talk.Answer("(brain provider)", "Couldn't save the provider address and key.");
                        return;
                    }
                    Brain.Cancel();
                    Brain.SetServer(server, key);
                    Saved.BrainServer = server;
                    WriteSecret(ServerKeyFile, null, "provider key");
                    string model = Array.IndexOf(found, Saved.ServerModel) >= 0 ? Saved.ServerModel : found[0];
                    if (Brain.Remote == null) Brain.Model = model;
                    Saved.ServerModel = model;
                    Save();
                    if (MainframeTier == 0) ShowServer(found);
                    if (ticket == asking) Talk.Answer("(brain server)", key == null ? "I'm thinking with " + model + " on your server now." + (Brain.Remote != null ? " Your cloud key still comes first until you forget it." : "") : "I'm thinking with " + model + " on " + server + " with your key now." + (Brain.Remote != null ? " Your cloud key still comes first until you forget it." : ""));
                });
            });
        }

        internal void PickServerModel(string model)
        {
            if (string.IsNullOrEmpty(model) || model == Brain.Model) return;
            Brain.Cancel();
            Brain.Model = model;
            Saved.ServerModel = model;
            Save();
        }

        internal void LeaveServer()
        {
            ++asking;
            if (!WriteServerPair(ServerPairFile, null, null))
            {
                Talk.Answer("(brain server)", "Couldn't remove the saved provider.");
                return;
            }
            Brain.Cancel();
            Brain.SetServer(null, null);
            Saved.BrainServer = null;
            frameModels = Array.Empty<string>();
            WriteSecret(ServerKeyFile, null, "provider key");
            if (Brain.Remote == null) Brain.Model = null;
            Save();
            FindBrain();
            RefreshBrains();
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
                    pick.Click += Guarded(delegate { PickCloud(model); });
                    brainMenu.Items.Add(pick);
                }
            brainMenu.Items.Add(new Separator());
            if (remote.Kind == "nvidia")
            {
                MenuItem stay = new MenuItem { Header = "Stay on my model", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Saved.StayOnModel };
                stay.Click += Guarded(delegate { SetStay(!Saved.StayOnModel); });
                brainMenu.Items.Add(stay);
            }
            MenuItem back = new MenuItem { Header = "Forget the key and use Ollama" };
            back.Click += Guarded(delegate { ForgetBrainKey(); });
            brainMenu.Items.Add(back);
        }

        private void ListCloud()
        {
            Cloud remote = Brain.Remote;
            if (remote == null || cloudListing || cloudModels != null) return;
            cloudListing = true;
            string key = remote.Key;
            bool freeOnly = MainframeTier == 1;
            int request = ++cloudRequests;
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> found = null;
                try { found = Brain.CloudModels(key, freeOnly); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    if (request != cloudRequests) return;
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
        internal void PullBrain() { PullBrain(Starter); }

        internal void PullBrain(string tag)
        {
            if (pulling) return;
            pulling = true;
            pulled = -1;
            pullingName = tag;
            Talk.Answer(BrainHeard, "Downloading my brain, " + tag + ". It can be a few GB. BRAIN in Capri Mainframe shows its progress.");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string problem = null;
                try { Brain.Pull(tag, delegate(string status, long done, long total) { pulled = (int)(done * 100 / Math.Max(1, total)); }, delegate { return false; }); }
                catch (Exception error)
                {
                    System.Net.WebException web = error as System.Net.WebException;
                    problem = web != null && web.Response == null ? NoOllama : Brain.Plain(error.Message) ?? "Couldn't download my brain: " + Voice.Short(error.Message);
                }
                Dispatcher.UIThread.Post(delegate
                {
                    pulling = false;
                    pulled = -1;
                    if (MainframeTier == 0) frameProblem = problem;
                    NotifySettings();
                    if (problem != null) { Talk.Answer(BrainHeard, problem); return; }
                    Saved.Model = tag;
                    string currentServer, currentKey;
                    Brain.GetServer(out currentServer, out currentKey);
                    if (MainframeTier == 0 && Brain.Remote == null && currentServer == null) Brain.Model = tag;
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

        internal string OpenPersonality()
        {
            string path = Brain.PersonalityPath;
            if (!Brain.MakePersonality()) return null;
            if (Program.Smoke == null) Browse(path);
            return path;
        }

        internal void ResetPersonality()
        {
            ComeBack();
            string backup = Brain.ResetPersonality();
            Reply(++asking, PersonalityHeard, backup == null ? "Couldn't reset my personality. It lives in " + Brain.PersonalityPath + "." : backup.Length > 0 ? Brain.ResetKept : Brain.ResetDone);
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
                    if (!Talk.Typing && asking == nudgeAsked)
                    {
                        string line = careNudge ? careLines.Pick("Bored noticed") : NoticedLines[random.Next(NoticedLines.Length)];
                        if (line != null) { if (careNudge) Reply(asking, "(you noticed)", line); else Blurt("(you noticed)", line); }
                    }
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
        }

        private PixelPoint? Mouse()
        {
            try { return Desk.Pointer(); }
            catch (Exception) { return null; }
        }

        // She asks where she is when she's near the mouse or the desktop won't say where it is; otherwise she glitches over beside it.
        internal void Nudge(double now, PixelPoint? mouse, string words = null)
        {
            careNudge = words != null;
            string line = words ?? NudgeLines[random.Next(NudgeLines.Length)];
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
            if (careNudge) Reply(asking, NudgeHeard, line); else Blurt(NudgeHeard, line);
            Raise(now + 20);
        }

        // Like on Windows, she only sulks out loud when someone's touched the computer in the last 20 s; she waits twice as long either way.
        private void Snub()
        {
            Func<double> read = Idle;
            bool caring = careNudge;
            ThreadPool.QueueUserWorkItem(delegate
            {
                double idle = -1;
                try { idle = read(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    if (idle < 0 || idle >= 20 || away || outro != null || !Talk.Quiet) return;
                    string line = caring ? careLines.Pick("Bored ignored") : SnubbedLines[random.Next(SnubbedLines.Length)];
                    if (line != null) { if (caring) Reply(asking, "(ignored)", line); else Blurt("(ignored)", line); }
                });
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

        internal void SetWatch(bool on, bool say = true)
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
            if (say && blind != null) Talk.Answer("(watch my screen)", blind);
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
                if (OperatingSystem.IsMacOS()) Limbs.Launch("open", new[] { url });
                else if (OperatingSystem.IsLinux()) Limbs.Launch("xdg-open", new[] { url });
                else Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception) { }
        }

        internal void FindBrain()
        {
            if (MainframeTier != 0) return;
            string saved = Saved.Model;
            string server;
            string serverKey;
            Brain.GetServer(out server, out serverKey);
            string serverSaved = Saved.ServerModel;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string model = null;
                try
                {
                    if (server == null) model = Brain.PickModel(Brain.InstalledModels(), saved, null);
                    else
                    {
                        string[] listed = Brain.ServerModels(server, serverKey);
                        model = Array.IndexOf(listed, serverSaved) >= 0 ? serverSaved : listed.Length > 0 ? listed[0] : null;
                    }
                }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    string currentServer, currentKey;
                    Brain.GetServer(out currentServer, out currentKey);
                    if (MainframeTier != 0 || Brain.Model != null || model == null || currentServer != server || currentKey != serverKey) return;
                    Brain.Model = model;
                    if (server == null) Saved.Model = model;
                    else Saved.ServerModel = model;
                    Save();
                });
            });
        }

        internal void Hear(string text)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            ModList.LogHearing(text, true);
            Typed();
            if (meeting.Asking) { Met(text); return; }
            int ticket = ++asking;
            Brain.Cancel();
            voice.Stop();
            string tool;
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^(?:please )?(?:(?:can|could|would) you )?(?:open|show|bring up|let me see|can i see)(?: me)?(?: the| your| her| capri'?s?)? (?:capri )?main ?frame(?: window)?(?: please)?[.!?]*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
            {
                Reply(ticket, text, MainframeTool(null));
                return;
            }
            ToolArguments args;
            bool noted;
            try { noted = Hands.Heard(text, out tool, out args) && tool == "remember"; }
            catch (Exception) { noted = false; tool = null; args = null; }
            // Something that can't work here, like clones and chaos mode, which are Windows-only: she says so herself and why,
            // brain or no brain, instead of her brain joking about it. Asked for that and something she can do too, like "open
            // Spotify. Chaos mode", the rest goes ahead and she says why after it (a Codex review, 2 Nov).
            string doable;
            if (noted || (tool != null && Hands.Unable(tool, args) != null && Hands.KitFor().Rest(text, out doable) == null))
            {
                string done = Hands.Do(tool, args);
                Brain.Remember(text, done);
                Reply(ticket, text, done);
                return;
            }
            // Asked in other words for something only Windows has, like "can you draw on my screen?", she says so too (F69).
            string lacking = Hands.KitFor().Lacking(text);
            if (lacking != null)
            {
                Brain.Remember(text, lacking);
                Reply(ticket, text, lacking);
                return;
            }
            // Her nap, her own moves and her animations work before she has a brain, like on Windows.
            if (tool == "mainframe" || (Think == null && Brain.Model == null && (tool == "nap" || tool == "move_yourself" || tool == "animate"))) { Reply(ticket, text, Hands.Do(tool, args)); return; }
            if (Think == null && Brain.Model == null && News.Matching(text) != null) { Reply(ticket, text, News.Facts); return; }
            if (Think == null && Brain.Model == null) { Reply(ticket, text, Offline); FindBrain(); return; }
            Talk.Thinking(text);
            Ask(ticket, text, 0);
        }

        // Her brain answers on the thread pool. A stuck NVIDIA model hands the message to the next one, twice at most, like on Windows.
        private void Ask(int ticket, string text, int switches)
        {
            Func<string, string> think = Think;
            bool follows = FollowUps;
            int input = inputTurn;
            Cloud remote = Brain.Remote;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string said;
                string after = null;
                bool stuck = false, failed = false;
                try { said = think == null ? Brain.Ask(text, follows, out after) : think(text); }
                catch (BrainException error) { said = error.Message == Brain.Offline ? Offline : error.Told ? error.Message : "My brain couldn't answer: " + error.Message; stuck = Brain.Stalled(error); failed = true; }
                catch (Exception error) { said = Brain.Explain(error, remote) ?? "That reply was cancelled. Try again."; stuck = Brain.Stalled(error); failed = true; }
                if (string.IsNullOrWhiteSpace(said)) { said = Brain.Wordless; failed = true; }
                Dispatcher.UIThread.Post(delegate
                {
                    if (ticket != asking) return;
                    if (stuck && switches < 2 && Hop(remote)) { Ask(ticket, text, switches + 1); return; }
                    if (input != inputTurn) after = null;
                    if (HoldNews(ticket)) return;
                    if (ticket == newsTicket && failed) { Talk.Answer("(updated)", News.Line); KeepNews(); return; }
                    Reply(ticket, ticket == newsTicket ? "(updated)" : text, said, ticket == newsTicket ? null : after);
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

        internal ISpeaker Speaker
        {
            get
            {
                string picked = Speakers.Known(Saved.VoiceProvider);
                return picked == Speakers.Local ? localVoice : picked == Speakers.Off ? (ISpeaker)noVoice : elevenLabs;
            }
        }

        internal bool Speaks { get { return Speaker.Ready && !Saved.Silent; } }

        // Why a voice check stays quiet, for the provider they picked.
        private string Unspoken
        {
            get
            {
                string picked = Speakers.Known(Saved.VoiceProvider);
                if (picked == Speakers.Off) return "My voice is off. Pick a voice under VOICE in Capri Mainframe.";
                if (picked == Speakers.Local) return localVoice.Ready ? "My voice is off. Pick CUSTOM LOCAL under VOICE in Capri Mainframe." : "Set up CUSTOM LOCAL under VOICE in Capri Mainframe.";
                return voice.Ready ? "My voice is off. Pick CLOUD VOICE under VOICE in Capri Mainframe." : "Add your ElevenLabs key under VOICE in Capri Mainframe.";
            }
        }

        private void Reply(int ticket, string heard, string said, string after = null, bool following = false)
        {
            int input = inputTurn, answer = answerTurn;
            followUp.Drop();
            if (!Speaks || Voice.Spoken(said).Length == 0) { if (ShowReply(ticket, heard, said, 0, after, following, input, answer)) TypeAlong(said, ticket); return; }
            ISpeaker speaker = Speaker;
            if (!following) Talk.Thinking(heard);
            answer = answerTurn;
            bool[] shown = { false };
            DispatcherTimer.RunOnce(delegate
            {
                if (ticket != asking || shown[0]) return;
                shown[0] = true;
                PresentReply(ticket, heard, said, 0, after, following, input, answer, () => TypeAlong(said, ticket));
            }, TimeSpan.FromMilliseconds(VoiceWait));
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                string problem = null;
                // Every provider hands back a finished WAV, so her pitch, speed, robot and glitch go on here, like on Windows.
                try { wav = VoiceFx.Styled(speaker.Speak(said), said); }
                catch (VoiceException error) { problem = Mac(error.Message); }
                catch (Exception error) { problem = Voice.Short("Voice glitched: " + error.Message); }
                Dispatcher.UIThread.Post(delegate
                {
                    voiceProblem = problem;
                    if (ticket != asking || shown[0]) return;
                    shown[0] = true;
                    double seconds = wav == null ? 0 : Voice.Seconds(wav);
                    PresentReply(ticket, heard, said, seconds, after, following, input, answer, delegate
                    {
                        if (seconds >= 0.2) voice.Play(wav);
                        else TypeAlong(said, ticket);
                    });
                });
            });
        }

        private static string Mac(string problem) { return problem == Voice.Rejected ? "ElevenLabs rejected the key. Paste a new one under VOICE in Capri Mainframe." : problem; }

        private string KeyFile { get { return Path.Combine(Program.Smoke ?? folder, "voice-key"); } }

        private string BrainKeyFile { get { return Path.Combine(Program.Smoke ?? folder, "brain-key"); } }

        private string ServerKeyFile { get { return Path.Combine(Program.Smoke ?? folder, "brain-server-key"); } }

        private string ServerPairFile { get { return Path.Combine(Program.Smoke ?? folder, "brain-server"); } }

        // The address and key live in one file, replaced atomically, so a crash can never pair one provider's
        // address with another provider's key.
        private static bool ReadServerPair(string path, out string address, out string key)
        {
            address = null;
            key = null;
            try
            {
                if (!File.Exists(path)) return false;
                string[] lines = File.ReadAllLines(path);
                if (lines.Length == 0) return false;
                address = (lines[0] ?? "").Trim();
                key = lines.Length > 1 ? (lines[1] ?? "").Trim() : null;
                if (key != null && key.Length == 0) key = null;
                return address.Length > 0;
            }
            catch (Exception) { return false; }
        }

        private static bool WriteServerPair(string path, string address, string key)
        {
            string temp = path + ".new";
            try
            {
                if (address == null)
                {
                    File.Delete(path);
                    try { File.Delete(temp); } catch (Exception) { }
                    return true;
                }
                FileStreamOptions options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
                if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
                using (FileStream stream = new FileStream(temp, options))
                {
                    if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                    using (StreamWriter writer = new StreamWriter(stream)) writer.Write(address + "\n" + (key ?? "") + "\n");
                }
                File.Move(temp, path, true);
                return true;
            }
            catch (Exception error)
            {
                try { File.Delete(temp); } catch (Exception) { }
                Brain.Log(path, -1, "Couldn't save the provider address and key: " + error.Message);
                return false;
            }
        }

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
            SaveBrainKey(key, Cloud.MediumKind(Cloud.KindOf((key ?? "").Trim())) ? 1 : 2);
        }

        internal void SaveBrainKey(string key, int tier)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;
            string kind = Cloud.KindOf(key);
            if (kind == null) { frameProblem = NotAKey; NotifySettings(); Talk.Answer(BrainKeyHeard, NotAKey); return; }
            frameProblem = null;
            int pickedTier = tier == 1 && Cloud.MediumKind(kind) ? 1 : 2;
            int request = ++brainRequests;
            brainPending++;
            int ticket = ++asking;
            Talk.Thinking(BrainKeyHeard);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<string> found = null;
                string problem = null;
                string company = new Cloud(key, null).Company;
                try { found = Brain.CloudModels(key, pickedTier == 1); }
                catch (BrainException error) { problem = error.Message; }
                catch (Exception error) { Brain.Log(company + " models", -1, error.Message); problem = "Couldn't reach " + company + ". Check your internet, then paste the key again."; }
                Dispatcher.UIThread.Post(delegate
                {
                    brainPending--;
                    if (request != brainRequests) { NotifySettings(); return; }
                    string model = found == null ? null : Brain.DefaultCloudModel(kind, found);
                    if (problem == null && model == null) problem = "That key can't use any chat models.";
                    if (problem != null) { frameProblem = problem; NotifySettings(); if (ticket == asking) Talk.Answer(BrainKeyHeard, problem); return; }
                    UseCloud(key, model, found, pickedTier);
                    WriteSecret(BrainKeyFile, key, "brain key");
                    if (ticket == asking) Talk.Answer(BrainKeyHeard, "Key saved. I'm thinking with " + model + " now. Change it under BRAIN in Capri Mainframe.");
                });
            });
        }

        internal void UseCloud(string key, string model, List<string> models, int tier = -1)
        {
            brainRequests++;
            cloudRequests++;
            frameProblem = null;
            KeepBrainTier();
            Brain.Cancel();
            Brain.Remote = new Cloud(key, model);
            Brain.Model = model;
            Saved.CloudModel = model;
            cloudModels = models;
            cloudListing = false;
            Saved.BrainTier = tier >= 0 ? tier : Cloud.MediumKind(Brain.Remote.Kind) ? 1 : 2;
            KeepBrainTier();
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
            KeepBrainTier();
            Save();
        }

        // Back to Ollama: the key file goes, and she finds her Ollama model again.
        internal void ForgetBrainKey()
        {
            brainRequests++;
            cloudRequests++;
            cloudListing = false;
            Brain.Cancel();
            Brain.Remote = null;
            Brain.Model = null;
            cloudModels = null;
            Saved.CloudModel = null;
            Saved.BrainTier = 0;
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
                    else Reply(ticket, KeyHeard, "Key saved. I'll talk as " + VoiceName() + ". Change it under VOICE in Capri Mainframe.");
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

        internal string VoiceStatus
        {
            get
            {
                string picked = Speakers.Known(Saved.VoiceProvider);
                string quiet = Speaks ? "speaking" : "quiet";
                if (picked == Speakers.Off) return "off | no voice | " + quiet;
                if (picked == Speakers.Local) return "local " + LocalWhere + " | " + (string.IsNullOrEmpty(localVoice.Speaker) ? "server's voice" : localVoice.Speaker) + " | " + quiet;
                return (voice.Key == null ? "no key" : "key " + Voice.Tail(voice.Key)) + " | " + (VoiceName() ?? "no voice") + " | " + quiet;
            }
        }

        private string LocalWhere
        {
            get
            {
                try { return LocalSpeaker.Where(LocalSpeaker.Endpoint(localVoice.Address)); }
                catch (VoiceException) { return "not set"; }
            }
        }

        internal void SetVoiceProvider(string picked)
        {
            picked = Speakers.Known(picked);
            if (picked == Saved.VoiceProvider) return;
            voice.Stop();
            Saved.VoiceProvider = picked;
            voiceProblem = null;
            Save();
        }

        internal void AskLocalVoice(string part)
        {
            ComeBack();
            string heard = part == "url" ? "(local voice server)" : part == "model" ? "(local voice model)" : "(local voice)";
            string ask = part == "url" ? "Type your local voice server's address, like " + LocalSpeaker.Default + ", and press Enter. A dash goes back to " + LocalSpeaker.Default + "."
                : part == "model" ? "Type the model your server should use, like kokoro, and press Enter. A dash leaves it to the server."
                : "Type the voice your server should use, like af_heart, and press Enter. A dash leaves it to the server.";
            Talk.AskFor(heard, ask, delegate(string typed)
            {
                if (SetLocalVoice(part, typed)) Talk.Answer(heard, "Saved. Press Test voice in Capri Mainframe to hear it.");
            });
        }

        // A blank or a dash clears the model or voice, so the server picks its own.
        internal bool SetLocalVoice(string part, string typed)
        {
            string value = (typed ?? "").Trim();
            if (value == "-") value = "";
            if (part == "url")
            {
                if (value.Length > 0)
                {
                    try { LocalSpeaker.Endpoint(value); }
                    catch (VoiceException error) { Talk.Answer("(local voice server)", error.Message); return false; }
                }
                localVoice.Url = Saved.LocalVoiceUrl = value.Length == 0 ? null : value;
            }
            else if (part == "model") localVoice.Model = Saved.LocalVoiceModel = value.Length == 0 ? null : value;
            else if (part == "format") localVoice.Format = Saved.LocalVoiceFormat = LocalSpeaker.Known(value);
            else localVoice.Speaker = Saved.LocalVoiceName = value.Length == 0 ? null : value;
            voiceProblem = null;
            Save();
            return true;
        }

        internal bool VoiceWorking { get { return Speaks && voiceProblem == null; } }

        internal bool TypingSound { get { return Saved.Typing == 1 || (Saved.Typing == 0 && !VoiceWorking); } }

        internal void SetTyping(bool on)
        {
            SetTypingMode(on ? 1 : 2);
            Sfx.Type("click clack", Talk.LettersPerSecond, 1);
        }

        // Her typing sound's mode: 0 Auto, only while she can't talk, 1 on and 2 off.
        internal void SetTypingMode(int mode)
        {
            Saved.Typing = Math.Max(0, Math.Min(2, mode));
            Sfx.Typing = TypingSound;
            Save();
            Sync();
        }
        internal void AutoTyping() { SetTypingMode(0); }

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
            string picked = Speakers.Known(Saved.VoiceProvider);
            MenuItem provider = new MenuItem { Header = "Provider: " + (picked == Speakers.Local ? localVoice.Name : picked == Speakers.Off ? "Off" : "ElevenLabs") };
            string[] pickable = { Speakers.ElevenLabs, Speakers.Local, Speakers.Off };
            string[] pickNames = { "ElevenLabs", localVoice.Name + " (OpenAI speech API)", "Off" };
            for (int i = 0; i < pickable.Length; i++)
            {
                string one = pickable[i];
                MenuItem pick = new MenuItem { Header = pickNames[i], ToggleType = MenuItemToggleType.Radio, GroupName = "voice provider", IsChecked = one == picked };
                pick.Click += Guarded(delegate { SetVoiceProvider(one); });
                provider.Items.Add(pick);
            }
            voiceMenu.Items.Add(provider);
            voiceMenu.Items.Add(new Separator());
            if (picked == Speakers.Off)
            {
                voiceMenu.Items.Add(new MenuItem { Header = "No voice provider. She types her replies.", IsEnabled = false });
                return;
            }
            if (picked == Speakers.Local)
            {
                RefreshLocalVoice();
                return;
            }
            if (voice.Key == null)
            {
                MenuItem add = new MenuItem { Header = "Add ElevenLabs key..." };
                add.Click += Guarded(delegate { AskVoiceKey(); });
                voiceMenu.Items.Add(add);
                return;
            }
            MenuItem speak = new MenuItem { Header = "Use ElevenLabs", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Speaks, IsEnabled = voice.VoiceId != null };
            speak.Click += Guarded(delegate { SetSpeak(Saved.Silent); });
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
                    pick.Click += Guarded(delegate { ChooseVoice(id); });
                    voiceMenu.Items.Add(pick);
                }
                if (voices.Voices.Count == 0) voiceMenu.Items.Add(new MenuItem { Header = "No voices on this key", IsEnabled = false });
            }
            voiceMenu.Items.Add(new Separator());
            MenuItem renew = new MenuItem { Header = "New key..." };
            renew.Click += Guarded(delegate { AskVoiceKey(); });
            MenuItem remove = new MenuItem { Header = "Remove key ending " + Voice.Tail(voice.Key) };
            remove.Click += Guarded(delegate { ForgetVoice(); });
            voiceMenu.Items.Add(renew);
            voiceMenu.Items.Add(remove);
            if (voiceProblem != null) voiceMenu.Items.Add(new MenuItem { Header = voiceProblem, IsEnabled = false });
        }

        private void RefreshLocalVoice()
        {
            MenuItem speak = new MenuItem { Header = "Speak replies", ToggleType = MenuItemToggleType.CheckBox, IsChecked = Speaks, IsEnabled = localVoice.Ready };
            speak.Click += Guarded(delegate { SetSpeak(Saved.Silent); });
            voiceMenu.Items.Add(speak);
            voiceMenu.Items.Add(new Separator());
            MenuItem server = new MenuItem { Header = "Server: " + (string.IsNullOrEmpty(localVoice.Url) ? LocalSpeaker.Default + " (Kokoro's usual)" : localVoice.Url) + "..." };
            server.Click += Guarded(delegate { AskLocalVoice("url"); });
            MenuItem model = new MenuItem { Header = "Model: " + (string.IsNullOrEmpty(localVoice.Model) ? "server's default" : localVoice.Model) + "..." };
            model.Click += Guarded(delegate { AskLocalVoice("model"); });
            MenuItem name = new MenuItem { Header = "Voice: " + (string.IsNullOrEmpty(localVoice.Speaker) ? "server's default" : localVoice.Speaker) + "..." };
            name.Click += Guarded(delegate { AskLocalVoice("voice"); });
            bool pcm = localVoice.Format == LocalSpeaker.Pcm;
            MenuItem format = new MenuItem { Header = "Format: " + (pcm ? "PCM" : "WAV") };
            MenuItem wav = new MenuItem { Header = "WAV", ToggleType = MenuItemToggleType.Radio, GroupName = "local voice format", IsChecked = !pcm };
            wav.Click += Guarded(delegate { SetLocalVoice("format", LocalSpeaker.Wav); });
            MenuItem raw = new MenuItem { Header = "PCM (16-bit, 24 kHz)", ToggleType = MenuItemToggleType.Radio, GroupName = "local voice format", IsChecked = pcm };
            raw.Click += Guarded(delegate { SetLocalVoice("format", LocalSpeaker.Pcm); });
            format.Items.Add(wav);
            format.Items.Add(raw);
            voiceMenu.Items.Add(server);
            voiceMenu.Items.Add(model);
            voiceMenu.Items.Add(name);
            voiceMenu.Items.Add(format);
            voiceMenu.Items.Add(new Separator());
            MenuItem test = new MenuItem { Header = "Test voice", IsEnabled = Speaks };
            test.Click += Guarded(delegate { HearStyle(); });
            voiceMenu.Items.Add(test);
            if (voiceProblem != null) voiceMenu.Items.Add(new MenuItem { Header = voiceProblem, IsEnabled = false });
        }

        // The settings 0.7.8 added, kept again in cross-new.json, which 0.7.6 and 0.7.7 never touch: an older copy rewrites
        // cross.json without them, so coming back from one finds them here instead of starting over on ElevenLabs.
        [DataContract]
        internal sealed class NewSettings
        {
            [DataMember] internal int HuskMode;
            [DataMember] internal string VoiceProvider;
            [DataMember] internal string LocalVoiceUrl;
            [DataMember] internal string LocalVoiceModel;
            [DataMember] internal string LocalVoiceName;
            [DataMember] internal string LocalVoiceFormat;
            [DataMember] internal string BrainServer;
            [DataMember] internal string ServerModel;
            [DataMember] internal int MainframeSize;
            [DataMember] internal int MainframeColours;
            [DataMember] internal bool AdvancedSettings;
            [DataMember] internal int MemorySize;
            [DataMember] internal int ReplyLength;
            [DataMember] internal int? BrainTier;
            [DataMember] internal string FreeModel;
            [DataMember] internal string PaidModel;
            [DataMember] internal string SpeechModel;
            [DataMember] internal string CareSwitches;
            [DataMember] internal int[] CareTimes;
            [DataMember] internal long CareNight;
            [DataMember] internal bool NoHeadPats;
            [DataMember] internal bool NoFollowUps;
            [DataMember] internal bool NoKeepAwake;
            [DataMember] internal string Vault;
        }

        // Every copy from 0.7.8 on saves a provider, so a cross.json without one was last saved by an older copy.
        internal static Settings Kept(Settings saved, NewSettings newer)
        {
            if (saved == null || newer == null || saved.VoiceProvider != null) return saved;
            saved.VoiceProvider = newer.VoiceProvider;
            saved.LocalVoiceUrl = newer.LocalVoiceUrl;
            saved.LocalVoiceModel = newer.LocalVoiceModel;
            saved.LocalVoiceName = newer.LocalVoiceName;
            saved.LocalVoiceFormat = newer.LocalVoiceFormat;
            saved.BrainServer = newer.BrainServer;
            saved.ServerModel = newer.ServerModel;
            saved.MainframeSize = FrameSize(newer.MainframeSize);
            saved.MainframeColours = MainframeTheme.ClampLook(newer.MainframeColours);
            saved.AdvancedSettings = newer.AdvancedSettings;
            saved.MemorySize = newer.MemorySize;
            saved.ReplyLength = newer.ReplyLength;
            saved.BrainTier = newer.BrainTier;
            saved.FreeModel = newer.FreeModel;
            saved.PaidModel = newer.PaidModel;
            saved.SpeechModel = newer.SpeechModel;
            saved.CareSwitches = newer.CareSwitches;
            saved.CareTimes = newer.CareTimes;
            saved.CareNight = newer.CareNight;
            saved.NoHeadPats = newer.NoHeadPats;
            saved.NoFollowUps = newer.NoFollowUps;
            saved.NoKeepAwake = newer.NoKeepAwake;
            saved.Vault = newer.Vault;
            saved.HuskMode = newer.HuskMode;
            return saved;
        }

        internal static NewSettings Newest(Settings saved)
        {
            return new NewSettings { VoiceProvider = saved.VoiceProvider, LocalVoiceUrl = saved.LocalVoiceUrl, LocalVoiceModel = saved.LocalVoiceModel, LocalVoiceName = saved.LocalVoiceName, LocalVoiceFormat = saved.LocalVoiceFormat, BrainServer = saved.BrainServer, ServerModel = saved.ServerModel,
                MainframeSize = saved.MainframeSize, MainframeColours = saved.MainframeColours, AdvancedSettings = saved.AdvancedSettings, MemorySize = saved.MemorySize, ReplyLength = saved.ReplyLength, BrainTier = saved.BrainTier, FreeModel = saved.FreeModel, PaidModel = saved.PaidModel, SpeechModel = saved.SpeechModel,
                CareSwitches = saved.CareSwitches, CareTimes = saved.CareTimes, CareNight = saved.CareNight, NoHeadPats = saved.NoHeadPats, NoFollowUps = saved.NoFollowUps, NoKeepAwake = saved.NoKeepAwake, Vault = saved.Vault, HuskMode = saved.HuskMode };
        }

        private Settings Load()
        {
            try
            {
                Settings saved;
                using (FileStream stream = File.OpenRead(Path.Combine(folder, "cross.json"))) saved = (Settings)new DataContractJsonSerializer(typeof(Settings)).ReadObject(stream);
                NewSettings newer = null;
                try { using (FileStream stream = File.OpenRead(Path.Combine(folder, "cross-new.json"))) newer = (NewSettings)new DataContractJsonSerializer(typeof(NewSettings)).ReadObject(stream); }
                catch (Exception) { }
                return Kept(saved, newer);
            }
            // A new install starts in the computer's own language when she speaks it.
            catch (Exception) { return new Settings { Language = Brain.SystemLanguage(System.Globalization.CultureInfo.CurrentUICulture) }; }
        }

        internal void Save()
        {
            Saved.X = anchor.X;
            Saved.Y = anchor.Y;
            Saved.Placed = true;
            NotifySettings();
            if (Program.Smoke != null) return;
            try
            {
                Directory.CreateDirectory(folder);
                using (FileStream stream = File.Create(Path.Combine(folder, "cross.json"))) new DataContractJsonSerializer(typeof(Settings)).WriteObject(stream, Saved);
                using (FileStream stream = File.Create(Path.Combine(folder, "cross-new.json"))) new DataContractJsonSerializer(typeof(NewSettings)).WriteObject(stream, Newest(Saved));
            }
            catch (Exception) { }
        }

        internal void Quit()
        {
            if (quitting) return;
            quitting = true;
            // Her click guard writes down a bug in any of these, so she still goes after it instead of being left unable to quit.
            try
            {
                if (husk != null) husk.Dispose();
                StopAwake();
                CloseMainframe();
                Brain.Cancel();
                voice.Stop();
                if (presence != null) presence.Dispose();
                Talk.Dismiss();
            }
            finally
            {
                GlitchOut(delegate
                {
                    Save();
                    frames.Stop();
                    desktop.Shutdown(0);
                });
            }
        }

        internal void Finish(int code)
        {
            if (husk != null) husk.Dispose();
            StopAwake();
            CloseMainframe();
            frames.Stop();
            Talk.Dismiss();
            desktop.Shutdown(code);
        }

        private HuskWatch husk;
        private bool huskChanging;
        private readonly HashSet<string> huskAgents = new HashSet<string>();
        private readonly Queue<HuskAct> huskLines = new Queue<HuskAct>();
        private string huskMood;
        private double huskMoodAt = -10;
        internal static readonly string[] HuskNames = { "Off", "Claude Code", "Codex", "Both" };
        internal const string HuskHelp = "Code/Husk Mode works on Windows, Mac and Linux. Mainframe > Settings > Advanced Settings > BRAIN > Code/Husk Mode picks Off, Claude Code, Codex or Both. It installs only Capri's hooks and backs up settings first. Restart Claude Code or Codex after picking a mode, and trust her hooks in Codex when it asks. She watches prompts, tools, failures, approvals, completion, subagents and compacting. Tool captions are silent and at least four seconds apart. Approvals get her attention and voice when it is on; answer in the coding app. On Mac and Linux she shows a short completion answer and a subagent count in her bubble. With a cloud brain, observed actions go to that brain when you ask about them, with keys and tokens removed first. Off stops the listener and removes only her hooks. She never approves or controls the coding assistant. Use these facts only when asked about Code/Husk Mode.";
        internal Action<Action> HuskQueue = delegate(Action action) { ThreadPool.QueueUserWorkItem(delegate { action(); }); };
        internal Func<string> HuskExe = HuskHooks.Executable;
        internal Action<string, bool> HuskSaid;
        internal int HuskMode { get { return huskChanging ? Saved.HuskMode : husk == null ? 0 : husk.Mode; } }
        internal bool HuskChanging { get { return huskChanging; } }
        internal HuskWatch Husk { get { return husk; } }
        internal string HuskMood { get { return huskMood; } }
        internal int HuskAgents { get { return huskAgents.Count; } }

        private void PrepareHusk()
        {
            // Her listener file goes with her even when she closes without Quit: at logout, shutdown or a kill signal.
            AppDomain.CurrentDomain.ProcessExit += (_, _) => { HuskWatch closing = husk; if (closing != null) closing.Dispose(); };
            desktop.Exit += (_, _) => { HuskWatch closing = husk; if (closing != null) closing.Dispose(); };
            Saved.HuskMode = Math.Max(0, Math.Min(3, Saved.HuskMode));
            husk = new HuskWatch(Program.Smoke ?? folder);
            HuskHooks.BackupFolder = Program.Smoke ?? folder;
            husk.Clock = delegate { return Now; };
            husk.Language = delegate { return Brain.Language; };
            husk.Dispatch = delegate(Action action) { Dispatcher.UIThread.Post(action); };
            husk.Show = delegate(HuskAct act)
            {
                if (Dispatcher.UIThread.CheckAccess()) ShowHusk(act);
                else Dispatcher.UIThread.Post(delegate { ShowHusk(act); });
            };
            // What the coding assistants did stays out of her brain once Code/Husk Mode is off.
            Brain.HuskFacts = delegate { return "\n" + HuskHelp + (husk.Mode != 0 ? husk.Context() : ""); };
        }

        internal void SetHusk(int mode) { SetHusk(mode, false); }

        // Settings files Code/Husk Mode made into files of their own, because they linked into a folder she can't change, like
        // Nix's store: said when it happens, and again when Code/Husk Mode is off, since that doesn't link them back.
        private static string Unlinked(string[] files, bool off)
        {
            if (files == null || files.Length == 0) return null;
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            List<string> names = new List<string>();
            foreach (string file in files) names.Add(home.Length > 1 && file.StartsWith(home + "/", StringComparison.Ordinal) ? "~" + file.Substring(home.Length) : file);
            bool one = names.Count == 1;
            string which = string.Join(" and ", names);
            if (off) return which + (one ? " is still a file of its own. Link it again if you want the link back." : " are still files of their own. Link them again if you want the links back.");
            return which + (one ? " linked into a folder I can't change, like Nix's store, so I made it a file of its own." : " linked into folders I can't change, like Nix's store, so I made them files of their own.");
        }

        // At startup only a problem is said, so her hello isn't swapped for a restart note every time she opens.
        private void SetHusk(int mode, bool starting)
        {
            if (quitting || huskChanging || husk == null) return;
            mode = Math.Max(0, Math.Min(3, mode));
            if (mode == 0) { husk.Stop(); huskAgents.Clear(); huskLines.Clear(); }
            Saved.HuskMode = mode;
            Save();
            huskChanging = true;
            NotifySettings();
            HuskQueue(delegate
            {
                string problem = null;
                try { husk.Change(mode, mode == 0 ? null : HuskExe()); }
                catch (Exception error) { problem = error.Message; }
                string unlinked = Unlinked(husk.Unlinked, mode == 0);
                Action finish = delegate
                {
                    huskChanging = false;
                    if (quitting) return;
                    Saved.HuskMode = husk.Mode;
                    Save();
                    string note = mode == 0 ? "Code/Husk Mode off. My hooks are removed."
                        : mode == 1 ? "Restart Claude Code to use my hooks."
                        : mode == 2 ? "Restart Codex and trust my hooks when it asks."
                        : "Restart Claude Code and Codex. Trust my hooks in Codex when it asks.";
                    if (unlinked != null) note += " " + unlinked;
                    // A link she had to replace is said even at startup.
                    if (problem != null || !starting || unlinked != null) Talk.Answer("(code/husk mode)", problem ?? note);
                };
                if (Dispatcher.UIThread.CheckAccess()) finish(); else Dispatcher.UIThread.Post(finish);
            });
        }

        private void ShowHusk(HuskAct act)
        {
            if (quitting) return;
            if (act.Kind == "agentstop") huskAgents.Remove(act.Key);
            if (husk == null || husk.Mode == 0 || !husk.Current(act)) return;
            if (act.Kind == "agentstart") huskAgents.Add(act.Key);
            if (act.Kind == "agentstart" || act.Kind == "agentstop") act.Line = "Subagents: " + huskAgents.Count;
            bool attention = act.Kind == "approval" || act.Kind == "input";
            if (act.Kind == "prompt" || attention)
            {
                napAsked = napping = false;
                if (clip == sleep) Wake();
            }
            if (attention) { hideAsked = false; ComeBack(); Raise(Now + 6); }
            huskMood = act.Kind;
            huskMoodAt = Now;
            if (!away && !dragging && !falling && !walking && !hanging && outro == null)
            {
                if (attention || act.Kind == "compact" || act.Kind == "failure") Play(seat == null ? glitch : perchGlitch);
                else if (act.Kind == "done" && seat == null) Play(grin);
                else if (act.Kind == "error") { Play(seat == null ? idle : perch); blinkWith = seat == null ? blink : perchBlink; }
            }
            if (!Hushed && !away && (attention || act.Kind == "failure" || act.Kind == "error")) Sfx.Play(Cue.Bonk);
            else if (!Hushed && !away && act.Kind == "done") Sfx.Play(Cue.Click);
            if (string.IsNullOrWhiteSpace(act.Line)) return;
            if (!Talk.Quiet && HuskSaid == null)
            {
                if (act.Kind != "caption") { while (huskLines.Count >= 32) huskLines.Dequeue(); huskLines.Enqueue(act); }
                return;
            }
            SayHusk(act);
        }

        private void TickHusk()
        {
            if (husk == null) return;
            husk.Tick();
            if (!Talk.Quiet || away || huskChanging) return;
            while (huskLines.Count > 0)
            {
                HuskAct act = huskLines.Dequeue();
                if (!husk.Current(act) || !husk.Waiting(act)) continue;
                SayHusk(act);
                break;
            }
        }

        private void SayHusk(HuskAct act)
        {
            if (quitting || away || husk == null || !husk.Current(act) || !husk.Waiting(act)) return;
            // Mac and Linux can't tell whether the coding window is in front, so a finished turn shows in her bubble without
            // being read out. Approvals and errors still speak.
            bool aloud = act.Aloud && act.Kind != "done" && !Hushed && Speaks && Voice.Loudness > 0;
            if (HuskSaid != null) { HuskSaid(act.Line, aloud); return; }
            Talk.Answer("(code/husk mode)", act.Line);
            if (!aloud) return;
            int ticket = asking;
            ISpeaker speaker = Speaker;
            HuskQueue(delegate
            {
                byte[] wav = null;
                try { wav = VoiceFx.Styled(speaker.Speak(act.Line), act.Line); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    if (wav == null || quitting || away || Hushed || ticket != asking || !husk.Current(act) || !husk.Waiting(act) || Talk.Reply != act.Line) return;
                    Talk.Answer("(code/husk mode)", act.Line, Voice.Seconds(wav));
                    voice.Play(wav);
                });
            });
        }
    }
}
