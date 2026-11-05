using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe : Form, IMessageFilter, LookWindow
    {
        // Her Mainframe's own look, after Cyberpunk 2077's settings screen (Lachy, 30 Oct): near black with a wine glow at the
        // top, sections on dark bands under a teal rule, setting names in red, values in cyan, and boxes with a dark red rim and
        // clipped corners. The colours come from Neon, which her menus, type box, installer and notices share too; her bubbles
        // keep Look's.
        private static Color Ground { get { return Neon.Ground; } }
        private static Color Glow { get { return Neon.Glow; } }
        private static Color Band { get { return Neon.Band; } }
        private static Color Rule { get { return Neon.Rule; } }
        private static Color Rose { get { return Neon.Rose; } }
        private static Color White { get { return Neon.White; } }
        private static Color Faint { get { return Neon.Faint; } }
        private static Color Field { get { return Neon.Field; } }
        private static Color Hover { get { return Neon.Hover; } }
        private static Color Pressed { get { return Neon.Pressed; } }
        private static Color Edge { get { return Neon.Edge; } }
        private static Color Hot { get { return Neon.Hot; } }
        private static Color Cyan { get { return Neon.Cyan; } }
        private static Color Deep { get { return Neon.Deep; } }
        private static Color DeepEdge { get { return Neon.DeepEdge; } }
        private static Color OnInk { get { return Neon.OnInk; } }
        private static Color Dash { get { return Neon.Dash; } }
        private static Color Lit { get { return Neon.Lit; } }
        private static Color Brim { get { return Neon.Brim; } }
        private static Color Well { get { return Neon.Well; } }
        private static Color WellEdge { get { return Neon.WellEdge; } }
        // Bahnschrift, Windows' own DIN-style face, in its semi-condensed cuts and the condensed bold of her title. Windows
        // before 10's 2017 update doesn't have it, so there she keeps her Share Tech Mono.
        private static readonly FontFamily Plain = Family("Bahnschrift SemiCondensed");
        private static readonly FontFamily Heavy = Family("Bahnschrift SemiBold SemiConden");
        private static readonly FontFamily Narrow = Family("Bahnschrift SemiBold Condensed");
        private const int Wide = 380;
        private const int Tall = 734;
        private static readonly int[] Gaps = { 10, 15, 30, 60, 90, 120, 180, 300, 600 };
        private static readonly string[] GapNames = { "10 s", "15 s", "30 s", "1 min", "1.5 min", "2 min", "3 min", "5 min", "10 min" };
        private const int SetupHeight = 118;
        // The left column is the taller one, and CLIPS' note at its foot sets this, with Free voice in games showing.
        private const int PageTall = 1868;
        // Settings scrolls at this height rather than filling a tall screen (Lachy, 31 Oct).
        private const int MostView = 820;
        // Settings opens a second column to the right, so every setting shows at once.
        private const int Column = 380;
        private const int Far = 30000;
        private static readonly string[] PerchNames = Array.ConvertAll(Companion.PerchRates, rate => rate + " fps");
        // Her voice's pitch in semitones, and her speed.
        private static readonly string[] PitchNames = { "-6", "-5", "-4", "-3", "-2", "-1", "Normal", "+1", "+2", "+3", "+4", "+5", "+6" };
        private static readonly int[] Paces = { 70, 80, 90, 100, 110, 120, 130, 140, 150 };
        private static readonly string[] PaceNames = { "0.7x", "0.8x", "0.9x", "Normal", "1.1x", "1.2x", "1.3x", "1.4x", "1.5x" };
        // Her robot and glitch, with small steps at the subtle end.
        private static readonly int[] Amounts = { 0, 5, 10, 15, 20, 30, 40, 50, 60, 70, 80, 90, 100 };
        private static readonly string[] AmountNames = { "Off", "5%", "10%", "15%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };
        private static readonly int[] Volumes = { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 125, 150 };
        private static readonly string[] VolumeNames = { "Mute", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%", "125%", "150%" };
        private static readonly string[] MicLevels = { "50%", "60%", "70%", "80%", "90%", "100%", "110%", "120%", "130%", "140%", "150%", "160%", "170%", "180%", "190%", "200%" };
        private static readonly int[] SizeSteps = { 140, 170, 200, 240, 280, 320, 380, 440, 500, 560 };
        private static readonly string[] SizeNames = { "Tiny", "Small", "Small+", "Medium", "Medium+", "Large", "Large+", "Huge", "Huge+", "Giant" };
        private static readonly int[] Sleeps = { 0, 60, 120, 300, 600, 1800 };
        private static readonly string[] SleepNames = { "Never", "1 min", "2 min", "5 min", "10 min", "30 min" };
        private static readonly string[] PopNames = { "Rarely", "Sometimes", "Often" };
        private static readonly string[] BubbleNames = { "Quick", "Normal", "Long" };
        private static readonly string[] SoundNames = { "Off", "10%", "20%", "30%", "40%", "50%", "60%", "70%", "80%", "90%", "100%" };
        private static readonly string[] WanderNames = { "Off", "Rarely", "Sometimes", "Often", "Restless" };
        private static readonly string[] Lengths = { "Short", "Medium", "Long" };
        private static readonly int[] Recalls = { 8, 16, 32 };
        private static readonly string[] MemoryNames = { "Auto", "4K", "8K", "16K", "32K", "64K" };
        private static readonly string[] ReplyNames = { "Auto", "256", "2K", "8K" };
        private const int VoiceHeight = 392;
        // While her free voice or ears download, a row with the brain setup's dotted bar opens here and pushes the rest down.
        private const int InstallAt = 535;
        private const int InstallRow = 40;
        // CLOUD VOICE's row: its service, then ElevenLabs' key or the other service's panel. Unless ElevenLabs is picked with a
        // key saved, one row under it folds away and the rest moves up; under the other rows, this one folds away instead.
        private const int KeyAt = 310;
        private const int KeyFold = 36;
        private const int HearingFixAt = 474;
        private const int HearingFixRow = 35;
        private int hearingFold;
        private float voiceFilled = -1;
        private readonly Label installNote;
        private readonly Companion capri;
        private readonly Label brainName;
        private readonly Label brainNote;
        private readonly Key models;
        private readonly ContextMenuStrip modelMenu;
        private readonly Key refresh;
        private readonly Key provider;
        private ProviderSheet providerSheet;
        private readonly Label[] steps = new Label[3];
        private readonly string[] stepText = new string[3];
        private readonly Key setup;
        private readonly Label hint;
        private readonly Label tierNote;
        private readonly Rung[] rungs = new Rung[3];
        private int tier;
        private readonly Entry nickname;
        private readonly Key showHide;
        private readonly Key perch;
        private readonly Slider sizeSlider;
        private readonly Readout sizeName;
        private readonly Toggle checkIns;
        private readonly Toggle onTop;
        private readonly Toggle headPats;
        private readonly Toggle upsetChaos;
        private readonly Toggle hologram;
        private readonly Toggle startup;
        private readonly Toggle discord;
        private readonly Toggle cursor;
        private readonly Key security;
        private readonly Key talkKeys;
        private readonly Key muteKeys;
        private readonly Key typeKeys;
        private readonly Key hideKeys;
        private readonly Key quietKeys;
        private readonly Key pushKeys;
        private readonly Key gameKeys;
        private readonly Toggle gamingMode;
        private readonly Key confirmKeys;
        private readonly Key cancelKeys;
        private readonly Key clearKeys;
        // The keys pressed so far while one of her keys is being picked: -1 until a real key comes, then its modifiers.
        private int pendingMods = -1;
        private int pendingVk;
        private int heldMods;
        private readonly Toggle watch;
        private readonly Key screens;
        private readonly ContextMenuStrip screenMenu;
        private readonly Toggle caughtUp;
        private readonly Toggle followUps;
        private readonly Key huskMode;
        private readonly ContextMenuStrip huskMenu;
        private static readonly string[] HuskNames = { "Off", "Claude Code", "Codex", "Both" };
        private readonly Slider watchMemory;
        private readonly Readout watchMemoryName;
        private static readonly int[] WatchLines = { 8, 16, 24, 48, 96 };
        private static readonly string[] WatchLineNames = { "8 lines", "16 lines", "24 lines", "48 lines", "96 lines" };
        private readonly Toggle stayOnModel;
        private readonly Toggle remembering;
        private readonly Toggle keepAwake, hideFullScreen, playerFullScreen, exploreAway;
        private Key clipsFolder;
        private Key clipChoose;
        private Label clipPath;
        private Head clipHead;
        private Slider clipLength, clipRate, clipSize, clipBits;
        private readonly Slider exploreAfter;
        private readonly Readout exploreAfterName;
        private static readonly int[] AwayTimes = { 60, 120, 300, 600, 900, 1800 };
        private static readonly string[] AwayNames = { "1 min", "2 min", "5 min", "10 min", "15 min", "30 min" };
        private readonly Toggle[] careToggles = new Toggle[7];
        private readonly Key[] careTimes = new Key[7];
        private readonly Key careEdit;
        private readonly ContextMenuStrip careMenu;
        private readonly Toggle liveReactions;
        private readonly Toggle clipGames;
        private readonly Toggle clipMoments;
        private readonly Toggle fastHands;
        private readonly Toggle gameMute;
        private readonly Label voiceHead;
        // OFF, PREBUILT LOCAL (her own voices), CUSTOM LOCAL (a voice model they set up on their PC) and CLOUD VOICE.
        private readonly Rung[] voiceRungs = new Rung[4];
        private int voiceRow = 1;
        private bool off;
        private bool eleven;
        private bool wantEleven;
        // Fish Audio or OpenAI, picked under CLOUD VOICE before it's set up: the row and its menu stay up until another row is picked.
        private string wantCloud;
        private const string Eleven = Companion.ElevenLabs;
        // Fish Audio comes first: its free tier needs no credits.
        private static readonly string[] Clouds = { "Fish Audio", Eleven, "OpenAI" };
        private string cloudName = Clouds[0];
        private readonly Key cloudPick;
        private readonly ContextMenuStrip cloudMenu;
        private readonly Key cloudVoice;
        private readonly Key customVoice;
        private readonly Key setUpModel;
        private int arrangedFold = -1;
        private readonly Key freeVoices;
        private readonly ContextMenuStrip freeMenu;
        private readonly Key freeSetup;
        private bool freeReady;
        private bool yoursShown;
        private ServerSheet sheet;
        private SecuritySheet guard;
        private ExtrasSheet extras;
        private LayersSheet layered;
        private Key layersKey;
        private Key personalityKey;
        private ContextMenuStrip personalityMenu;
        private Label styleNote;
        private readonly Slider gap;
        private readonly Readout gapName;
        private readonly Entry keyEntry;
        private readonly Key connect;
        private readonly Entry voiceEntry;
        private readonly ContextMenuStrip voiceMenu;
        private readonly Key speechModels;
        private readonly ContextMenuStrip speechModelMenu;
        private readonly Label voiceNote;
        private readonly Label hearingNote;
        private readonly Key[] hearingFixes = new Key[2];
        private HearingAction[] hearingActions = new HearingAction[0];
        private readonly Key hearingChoice;
        private readonly ContextMenuStrip hearingMenu;
        private bool speechFix;
        private readonly Label keysNote;
        private readonly Label blackwallNote;
        private readonly Label updateNote;
        private readonly Key keyHelp;
        private readonly Label shortcut;
        private bool shortcutReady;
        private bool muteReady = true;
        private int capturing;
        private string talkName = "Ctrl + Alt + V";
        private string muteName = "Ctrl + Alt + N";
        private string typeName = "Ctrl + Alt + T";
        private string hideName = "Ctrl + Alt + H";
        private string quietName = "Ctrl + Alt + P";
        private string pushName = "not set";
        private string gameName = "Ctrl + Alt + G";
        private bool typeReady = true;
        private bool hideReady = true;
        private bool quietReady = true;
        private bool gameReady = true;
        private readonly Label muteShortcut;
        private readonly Key mics;
        private readonly Key micTest;
        private readonly ContextMenuStrip micMenu;
        private readonly Key speakers;
        private readonly ContextMenuStrip speakerMenu;
        private bool keySaved;
        private readonly Entry textEntry;
        private readonly Key textConnect;
        private readonly Key freeKey;
        private readonly ContextMenuStrip freeKeyMenu;
        private readonly Label freeWarning;
        private readonly Label paidNote;
        private readonly Key movePaid;
        private string paidTextKey;
        internal const string NvidiaNote = "NVIDIA's free API is often slow, especially at busy times.";
        private bool textSaved;
        private readonly List<KeyValuePair<Control, int>> lower = new List<KeyValuePair<Control, int>>();
        private string[] lastInstalled;
        private List<Cloud> lastServers = new List<Cloud>();
        private Pick lastLocal;
        private float filled;
        private bool expanded = true;
        private bool loaded;
        private bool syncing;
        internal bool Quitting;
        private readonly Key more;
        private readonly Key minimize;
        private readonly Key close;
        private readonly Label build;
        private readonly List<KeyValuePair<Control, int>> mainLeft = new List<KeyValuePair<Control, int>>();
        private readonly List<KeyValuePair<Control, int>> pageLeft = new List<KeyValuePair<Control, int>>();
        private readonly Dictionary<Control, bool> stops = new Dictionary<Control, bool>();
        private bool paged;
        // On a screen too short for Settings, the whole page, header too, scrolls a row per turn of the wheel.
        private const int ScrollRow = 36;
        private readonly List<Control> scrolled = new List<Control>();
        private readonly Dictionary<Control, int> tops = new Dictionary<Control, int>();
        private int pageView = PageTall;
        private int pageScroll;
        // Settings opens simple: the rows most people never need are hidden and the gaps closed, and Advanced Settings brings
        // them back. These are stretches of each column as built, by Top.
        // Simple Settings keeps Her voice in your mic's rows, between Free voice in games and MOVES.
        private static readonly int[][] FoldedLeft = { new[] { 232, 400 }, new[] { 478, 506 }, new[] { 784, 1261 }, new[] { 1372, 1424 }, new[] { 1424, 1452 } };
        private static readonly int[][] FoldedRight = { new[] { 138, 362 }, new[] { 390, 418 }, new[] { 680, 1090 }, new[] { 1286, 2112 } };
        private readonly Dictionary<Control, int> built = new Dictionary<Control, int>();
        private bool simple;
        private Key advancedKey;
        private int PageNow { get { return simple ? SimpleTall : PageTall; } }
        // The same for Simple Settings, with its hidden rows taken out.
        private const int SimpleTall = 1086;
        private int wheel;
        // A wheel turn glides the page to where it's headed over a few frames, instead of jumping a row at a time (Lachy, 3 Nov).
        private const double GlideMs = 140;
        private readonly Timer glide = new Timer { Interval = 10 };
        private readonly System.Diagnostics.Stopwatch glideClock = new System.Diagnostics.Stopwatch();
        private int glideFrom, glideTo;
        // Support's heart beats twice every 7 seconds while her main page shows, and its rim lights with it (Lachy, 4 Nov).
        // It holds still with her moves off.
        private const double BeatMs = 7000;
        private readonly Timer heartbeat = new Timer { Interval = 33 };
        private readonly System.Diagnostics.Stopwatch beatClock = System.Diagnostics.Stopwatch.StartNew();
        private Key support;
        private readonly Rim topRim = new Rim();
        private readonly Rim bottomRim = new Rim();
        // Smoke tests pretend the screen's working area is this many px tall, before scaling; 0 uses the real one.
        internal int ShortScreen;
        private readonly Key languages;
        private readonly ContextMenuStrip languageMenu;
        private ToolStripMenuItem languageCurrent;
        private Font languageFont;
        private readonly Slider volume;
        private readonly Readout volumeName;
        private readonly Slider pitchSlider;
        private readonly Readout pitchName;
        private readonly Slider paceSlider;
        private readonly Readout paceName;
        private readonly Slider robotSlider;
        private readonly Readout robotName;
        private readonly Slider glitchSlider;
        private readonly Readout glitchName;
        private readonly Slider sounds;
        private readonly Slider sleepAfter;
        private readonly Slider bubble;
        private readonly Readout bubbleName;
        private readonly Slider comments;
        private readonly Readout commentsName;
        private readonly Toggle intro;
        private readonly Toggle typing;
        private readonly Toggle listenCapri;
        private readonly Toggle hdGames;
        // Her voice in your mic (F66): the switch, the line saying to pick CABLE Output, and a line under it that says when it
        // needs VB-CABLE, beside a button to VB-CABLE's page, or what's wrong while it's on. They sit under Free voice in games.
        private readonly Toggle voiceInMic;
        private readonly Label voiceInMicNote;
        private readonly Label voiceInMicState;
        private readonly Key cableKey;
        // Its Input and Output (Lachy, 3 Nov: "we need to be able to change the input and output for the mixer"), and How to,
        // which opens its setup steps: "make it clear how to set it up too".
        private readonly Key mixInput, mixOutput, mixHow;
        private readonly Slider micLoudness;
        private readonly Label micLoudnessName;
        private readonly ContextMenuStrip mixInputMenu, mixOutputMenu;
        private MixSheet mixSheet;
        private readonly Toggle callMode;
        private readonly Key callApp;
        private readonly Label callAppName, callAppNote;
        private readonly ContextMenuStrip callAppMenu;
        private const int MixAt = 590;
        private const int MixRows = 268;
        private readonly Toggle miniPlayer;
        private readonly Readout sleepName;
        private readonly Readout soundsName;
        private readonly Slider wander;
        private readonly Readout wanderName;
        private readonly Slider pops;
        private readonly Readout popsName;
        private readonly Key popTimes, gameNotes, miniGames;
        private ContextMenuStrip miniMenu;   // kept like the other menus; disposing it in its own Closed crashed WinForms
        private readonly Key notesKey, memoriesKey;
        private readonly Toggle dance;
        private readonly Slider replies;
        private readonly Readout repliesName;
        private readonly Slider recall;
        private readonly Readout recallName;
        private readonly Slider memorySize;
        private readonly Readout memorySizeName;
        private readonly Slider replyLength;
        private readonly Readout replyLengthName;
        private readonly Entry custom;
        private readonly Entry localAddress;
        private readonly Label customNote;
        private readonly Slider perchFps;
        private readonly Readout perchFpsName;
        // Her Mainframe size: Auto, or 100% to 200% of the screen's own scale.
        private static readonly int[] FrameSizes = { 0, 100, 125, 150, 175, 200 };
        private static readonly string[] FrameSizeNames = { "Auto", "100%", "125%", "150%", "175%", "200%" };
        private readonly Slider frameSize;
        private readonly Slider frameColours;
        private readonly Readout frameSizeName;
        // Settings' rows: a setting's name from x 28 and its value's box from x 184 to 356, 22 px tall on a 28 px row.
        private const int Row = 28;
        private const int ValueAt = 184;
        private const int ValueWide = 172;
        private readonly Label perchNote;

        internal Mainframe(Companion owner, Icon icon, string version)
        {
            glide.Tick += delegate { Glide(); };
            heartbeat.Tick += delegate { Heartbeat(); };
            VisibleChanged += delegate { heartbeat.Enabled = Visible; };
            capri = owner;
            Text = "Capri Mainframe";
            Icon = icon;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            Font = Type(9.5f);
            BackColor = Ground;
            ForeColor = White;
            DoubleBuffered = true;
            ClientSize = new Size(Wide, Tall + SetupHeight + KeyRow);
            Font small = Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point);
            Caption("MAINFRAME", 24, 15, Display(16f), Rose);
            minimize = Make("", 300, 20, 26, 22, delegate { WindowState = FormWindowState.Minimized; });
            minimize.Glyph = 1;
            minimize.AccessibleName = "Minimize";
            close = Make("", 330, 20, 26, 22, delegate { Close(); });
            close.Glyph = 2;
            close.AccessibleName = "Close";
            more = Make("Settings", 146, 20, 84, 22, delegate { if (modded) ShowMods(false); else ShowSettings(!paged); });
            more.Accent = Cyan;
            more.Loud = true;
            // Mods sits right beside Settings in her title bar (Lachy, 4 Nov: "I like mods better next to settings").
            modsKey = Make("Mods", 234, 20, 56, 22, delegate { ShowMods(!modded); });
            int header = Controls.Count;
            for (int i = 0; i < header; i++) Scrolls(Controls[i]);
            Section("BRAIN >", 0, 58);
            brainName = Caption("", 28, 79, Type(11f, true), White);
            brainName.AutoSize = false;
            brainName.AutoEllipsis = true;
            brainName.Size = new Size(244, 20);
            provider = Make("Provider", 280, 79, 76, 20, delegate { OpenProvider(); });
            provider.AccessibleName = "OpenAI-compatible provider address and key";
            brainNote = Caption("", 28, 101, Font, Faint);
            modelMenu = Dropdown();
            models = Make("", 28, 134, 244, 28, delegate { modelMenu.Show(models, new Point(0, models.Height)); });
            models.Chevron = true;
            models.AccessibleName = "Brain model";
            refresh = Make("Refresh", 280, 134, 76, 28, delegate { capri.FindBrain(); });
            for (int i = 0; i < 3; i++) steps[i] = Caption("", 28, 138 + i * 22, Font, Faint);
            setup = Make("Set up her brain", 28, 226, 170, 28, delegate { if (capri.SettingUp) capri.CancelSetup(); else capri.SetUpBrain(); });
            setup.Accent = Cyan;
            setup.Loud = true;
            hint = Caption("Have Ollama? Open it, then press Refresh.", 28, 264, Look.Mono(9f, FontStyle.Italic, GraphicsUnit.Point), Faint);
            tierNote = Caption("Pick one. Only one brain runs at a time.", 28, 168, Look.Mono(8.5f, FontStyle.Italic, GraphicsUnit.Point), Faint);
            string[,] tiers = { { "NORMAL", "Free · local model" }, { "MEDIUM INTELLIGENCE", "Free" }, { "MAX INTELLIGENCE", "Cloud API keys" } };
            for (int i = 0; i < rungs.Length; i++)
            {
                int picked = i;
                Rung rung = new Rung();
                rung.Text = tiers[i, 0];
                rung.Hint = tiers[i, 1];
                rung.Font = Type(9.5f, true);
                rung.HintFont = Type(9f);
                rung.AccessibleName = tiers[i, 0] + ", " + tiers[i, 1];
                rung.Bounds = new Rectangle(28, 184 + 24 * i, i == 1 ? 232 : 328, 24);
                rung.CheckedChanged += delegate { if (!syncing && rungs[picked].Checked) capri.ChooseTier(picked); };
                Controls.Add(rung);
                rungs[i] = rung;
            }
            rungs[0].Flag = "Start with this!";
            rungs[0].FlagFont = Type(9f);
            freeKeyMenu = Dropdown();
            freeKeyMenu.AccessibleName = "Free key providers";
            ToolStripMenuItem nvidiaKey = Choice("NVIDIA · free · often slow at busy times", false, delegate { capri.OpenFreeKeyPage(); });
            nvidiaKey.AccessibleName = "NVIDIA";
            freeKeyMenu.Items.Add(nvidiaKey);
            ToolStripMenuItem geminiKey = Choice("Google AI Studio (Gemini) · free tier · rate limited", false, delegate { capri.OpenGeminiKeyPage(); });
            geminiKey.AccessibleName = "Google AI Studio (Gemini)";
            freeKeyMenu.Items.Add(geminiKey);
            ToolStripMenuItem routerKey = Choice("OpenRouter · free models only", false, delegate { capri.OpenRouterKeyPage(); });
            routerKey.AccessibleName = "OpenRouter";
            freeKeyMenu.Items.Add(routerKey);
            freeKey = Make("Free key", 266, 209, 90, 22, delegate { ShowFreeKeys(); });
            freeKey.Chevron = true;
            freeKey.AccessibleName = "Free key: NVIDIA, Google AI Studio (Gemini) or OpenRouter";
            textEntry = Input(28, 262, 244);
            textEntry.Box.UseSystemPasswordChar = true;
            textEntry.Box.AccessibleName = "NVIDIA, Google AI Studio (Gemini) or OpenRouter API key";
            textEntry.Cue = "NVIDIA / Google AI Studio / OpenRouter";
            textConnect = Make("Connect", 280, 262, 76, 28, delegate { ConnectText(); });
            freeWarning = Caption("Free keys can be rate limited and slow.", 28, 294, Type(8.5f), Neon.Amber);
            paidNote = Caption("That's a paid key: it goes under MAX INTELLIGENCE.", 28, 315, Font, Neon.Amber);
            paidNote.MaximumSize = new Size(328, 0);
            movePaid = Make("Move to MAX", 28, 350, 128, 24, delegate { MovePaidText(); });
            movePaid.AccessibleName = "Move key to MAX INTELLIGENCE";
            freeWarning.Visible = paidNote.Visible = movePaid.Visible = false;
            textEntry.Box.TextChanged += delegate { RelabelText(); };
            textEntry.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ConnectText(); } };
            voiceHead = Lower(Section("VOICE >", 0, 184));
            shortcut = Caption("Ctrl + Alt + V to talk", 156, 181, small, Faint);
            shortcut.AutoSize = false;
            shortcut.TextAlign = ContentAlignment.MiddleRight;
            shortcut.Bounds = new Rectangle(156, 181, 200, 20);
            shortcut.Cursor = Cursors.Hand;
            // Keys are picked in Settings, where Confirm and Cancel sit beside them.
            shortcut.Click += delegate { ShowSettings(true); PickKeys(0); };
            Lower(shortcut);
            string[,] voiceTiers = { { "OFF", "Silent · text only" }, { "PREBUILT LOCAL", "Her voices · free" }, { "CUSTOM LOCAL", "Your own model" }, { "CLOUD VOICE", "Needs a key" } };
            for (int i = 0; i < voiceRungs.Length; i++)
            {
                int row = i;
                Rung rung = new Rung();
                // A set of its own: a self-checking radio button would untick the BRAIN tiers too.
                rung.AutoCheck = false;
                rung.TabStop = true;
                rung.Text = voiceTiers[i, 0];
                rung.Hint = voiceTiers[i, 1];
                rung.Font = Type(9.5f, true);
                rung.HintFont = Type(9f);
                rung.AccessibleName = voiceTiers[i, 0] + ", " + voiceTiers[i, 1];
                rung.Bounds = new Rectangle(28, 208 + 24 * i, i >= 2 ? 246 : 328, 23);
                rung.Click += delegate { if (syncing) return; PickRow(row); };
                Controls.Add(rung);
                voiceRungs[i] = Lower(rung);
            }
            setUpModel = Lower(Make("Set up", 276, 257, 80, 22, delegate { OpenServer(false, null); }));
            setUpModel.AccessibleName = "Set up your own voice model";
            keyHelp = Lower(Make("Setup guide", 276, 281, 80, 22, delegate { CloudGuide(); }));
            cloudMenu = Dropdown();
            foreach (string name in Clouds)
            {
                string each = name;
                cloudMenu.Items.Add(Choice(each, false, delegate { PickCloud(each); }));
            }
            cloudPick = Lower(Make(Clouds[0], 28, KeyAt, 108, 28, delegate { cloudMenu.Show(cloudPick, new Point(0, cloudPick.Height)); }));
            cloudPick.Chevron = true;
            cloudPick.AccessibleName = "Cloud voice service";
            keyEntry = Lower(Input(136, KeyAt, 136));
            keyEntry.Box.UseSystemPasswordChar = true;
            keyEntry.Box.AccessibleName = "ElevenLabs API key";
            connect = Lower(Make("Connect", 280, KeyAt, 76, 28, delegate { Connect(); }));
            keyEntry.Box.TextChanged += delegate { Relabel(); };
            keyEntry.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Connect(); } };
            // Fish Audio and OpenAI keep their key, voice and model in the voice model panel.
            cloudVoice = Lower(Make("", 144, KeyAt, 212, 28, delegate { OpenServer(true, CloudApi(cloudName)); }));
            voiceMenu = Dropdown();
            voiceEntry = Lower(Input(28, 346, 196));
            voiceEntry.Chevron = true;
            voiceEntry.Cue = "Voice ID";
            voiceEntry.Box.AccessibleName = "ElevenLabs voice ID";
            voiceEntry.Drop += delegate { voiceMenu.Show(voiceEntry, new Point(0, voiceEntry.Height)); };
            voiceEntry.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; capri.ChooseVoice(voiceEntry.Box.Text); } };
            voiceEntry.Box.Leave += delegate { capri.ChooseVoice(voiceEntry.Box.Text); };
            speechModelMenu = Dropdown();
            speechModels = Lower(Make("Default model", 232, 346, 124, 28, delegate { speechModelMenu.Show(speechModels, new Point(0, speechModels.Height)); }));
            speechModels.Chevron = true;
            freeMenu = Dropdown();
            freeVoices = Lower(Make("Free voice: Her own", 28, 346, 196, 28, delegate { freeMenu.Show(freeVoices, new Point(0, freeVoices.Height)); }));
            freeVoices.Chevron = true;
            freeVoices.Visible = false;
            // CUSTOM LOCAL's model in use. Its panel offers only what runs on their PC.
            customVoice = Lower(Make("Voice model", 28, 346, 328, 28, delegate { OpenServer(false, null); }));
            customVoice.Chevron = true;
            customVoice.AccessibleName = "Your voice model";
            customVoice.Visible = false;
            freeSetup = Lower(Make("Get free voice", 232, 346, 124, 28, delegate { if (capri.SettingUpVoice) capri.CancelFreeVoice(); else capri.SetUpFreeVoice(); }));
            freeSetup.Accent = Cyan;
            freeSetup.Loud = true;
            freeSetup.Visible = false;
            micMenu = Dropdown();
            mics = Lower(Make("Mic: default", 28, 382, 244, 28, delegate { ShowMics(); }));
            mics.Chevron = true;
            mics.AccessibleName = "Microphone";
            micTest = Lower(Make("Test", 280, 382, 76, 28, delegate { capri.TestMic(); }));
            micTest.AccessibleName = "Test microphone input";
            speakerMenu = Dropdown();
            speakers = Lower(Make("Her speaker: Default", 28, 418, 328, 28, delegate { ShowSpeakers(); }, "Her speaker"));
            speakers.Chevron = true;
            speakers.Ellipsis = true;
            speakers.AccessibleName = "Her speaker: Default (follows Windows)";
            hearingNote = Lower(Caption("", 28, 450, Type(9.5f), Faint));
            hearingNote.AutoSize = false;
            hearingNote.AutoEllipsis = true;
            hearingNote.Size = new Size(328, 20);
            hearingNote.AccessibleName = "Hearing status";
            for (int i = 0; i < hearingFixes.Length; i++)
            {
                int at = i;
                hearingFixes[i] = Lower(Make("", 28 + i * 168, 474, 160, 22, delegate { if (at < hearingActions.Length) capri.HearingFix(hearingActions[at]); }));
                hearingFixes[i].AccessibleName = "Hearing help";
                hearingFixes[i].Visible = false;
            }
            muteShortcut = Lower(Caption("Ctrl + Alt + N mutes mic", 28, 509, small, Faint));
            muteShortcut.Cursor = Cursors.Hand;
            muteShortcut.Click += delegate { ShowSettings(true); PickKeys(1); };
            Deactivate += delegate { CancelKeys(); };
            voiceNote = Lower(Caption("", 28, InstallAt, Font, Faint));
            voiceNote.Click += delegate { if (speechFix) capri.OpenSpeechSettings(); };
            installNote = Caption("", 28, InstallAt + 2, Font, White);
            installNote.Visible = false;
            int v = VoiceHeight;
            Lower(Section("CAPRI >", 0, 184 + v));
            // Shorter than a band's other keys and level with CAPRI, so its lit rim stays off the band's teal rule (Lachy, 4 Nov).
            support = Lower(Make("Support", 268, 183 + v, 88, 18, delegate { capri.OpenSupport(); }));
            support.Heart = true;
            support.AccessibleName = "Support Capri: opens Lachy's Ko-fi in your browser";
            // Her version sits where Mods was, beside Support, now Mods is up by Settings.
            build = Lower(Caption("v" + version, 196, 183 + v, small, Faint));
            build.AutoSize = false;
            build.TextAlign = ContentAlignment.MiddleRight;
            build.Bounds = new Rectangle(196, 183 + v, 66, 18);
            showHide = Lower(Make("Hide Capri", 28, 206 + v, 104, 28, delegate { capri.ToggleHidden(); }));
            Lower(Make("Bring here", 140, 206 + v, 104, 28, delegate { capri.BringHere(); }));
            perch = Lower(Make("Ride cursor", 252, 206 + v, 104, 28, delegate { capri.TogglePerch(); }));
            Lower(Make("Check for updates", 28, 250 + v, 150, 28, delegate { capri.CheckUpdates(); }));
            Key help = Lower(Make("Help", 186, 250 + v, 62, 28, delegate { capri.OpenHelp(); }));
            help.AccessibleName = "Help: ask Capri on the website";
            Key quit = Lower(Make("Quit Capri", 256, 250 + v, 100, 28, delegate { capri.Quit(); }));
            quit.Accent = Hot;
            updateNote = Lower(Caption("", 28, 288 + v, small, Faint));
            for (int i = header; i < Controls.Count; i++) mainLeft.Add(new KeyValuePair<Control, int>(Controls[i], Controls[i].Left));
            int first = Controls.Count;
            // Settings: a setting to a row, its name on the left in red and its value in a box on the right, as on Cyberpunk
            // 2077's settings screen (Lachy, 30 Oct). A section's own buttons, like Layers, sit at the right of its band.
            const int c = Column;
            // Up by Back and named for what it does: Lachy (31 Oct) missed a small All settings key in the CAPRI band.
            advancedKey = Make("Advanced Settings", 238, 20, 136, 22, delegate { capri.ToggleAdvancedSettings(); ShowSettings(true); });
            advancedKey.Accent = Cyan;
            advancedKey.Loud = true;
            Section("YOU >", 0, 58);
            notesKey = Make("Open her notes", 236, 55, 120, 20, delegate { EditNotes(false); });
            notesKey.AccessibleName = "Open her notes: edit me.md here, or her note in your Obsidian vault";
            // Their personality.md (back in 0.8.0): the key says whose personality she's using, and its menu edits or resets it.
            personalityMenu = Dropdown();
            personalityKey = Make("Her personality", 92, 55, 136, 20, delegate { ShowPersonalityMenu(); });
            personalityKey.AccessibleName = "Her personality: edit personality.md, or put her default back";
            Named("Language", 0, 82);
            languageMenu = Dropdown();
            languageMenu.AccessibleName = "Her language";
            languageMenu.Opened += delegate { KeepLanguageMenu(languageMenu); };
            languages = Make("English", ValueAt, 85, ValueWide, 22, delegate { ShowLanguages(); });
            languages.Chevron = true;
            languages.AccessibleName = "Her language";
            FillLanguages(Screen.FromRectangle(Bounds).WorkingArea);
            // A label of its own, so the question still shows once a name is saved in the box.
            Named("What should she call you?", 0, 110);
            Make("Get to know me", 236, 113, 120, 22, delegate { capri.GetToKnow(); }).AccessibleName = "Get to know me: she asks what to call you, your pronouns and what you're into, and keeps them in me.md";
            nickname = Input(28, 141, 244, 22);
            nickname.Cue = "Your name or a nickname";
            nickname.Box.AccessibleName = "What should she call you?";
            nickname.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseNickname(); } };
            Make("Save", 280, 141, 76, 22, delegate { UseNickname(); }, "What should she call you?");
            Section("VOICE >", 0, 180);
            styleNote = Caption("Applies to every voice", 88, 178, small, Faint);
            styleNote.AutoSize = false;
            styleNote.TextAlign = ContentAlignment.MiddleRight;
            styleNote.Bounds = new Rectangle(88, 178, 192, 20);
            // Her VOICE LAYERS window (Lachy, 29 Oct) opens from VOICE's band, and the button lights up while they're on.
            // "Layers: Off" and the note don't both fit the band at 150%.
            layersKey = Make("Layers", 286, 177, 70, 20, delegate { OpenLayers(); });
            layersKey.AccessibleName = "Voice layers: two copies of her voice under her, each with its own pitch and delay";
            volume = Setting("Volume", VolumeNames, 0, 204, "Her voice volume");
            volumeName = new Readout(volume);
            volume.Changed += delegate { if (syncing) return; volumeName.Text = VolumeNames[volume.Value]; capri.SetVolume(Volumes[volume.Value]); };
            // Her voice's sliders change every voice she has as they move, and she says a line once they're let go.
            pitchSlider = Setting("Pitch", PitchNames, 0, 232, "Her voice's pitch");
            pitchName = new Readout(pitchSlider);
            pitchSlider.Changed += delegate { if (syncing) return; pitchName.Text = PitchNames[pitchSlider.Value]; capri.SetVoicePitch(pitchSlider.Value - VoiceFx.MostPitch); ShowLayers(); };
            pitchSlider.Released += delegate { if (!syncing) capri.HearVoiceStyle(); };
            paceSlider = Setting("Speed", PaceNames, 0, 260, "Her voice's speed");
            paceName = new Readout(paceSlider);
            paceSlider.Changed += delegate { if (syncing) return; paceName.Text = PaceNames[paceSlider.Value]; capri.SetVoiceSpeed(Paces[paceSlider.Value]); };
            paceSlider.Released += delegate { if (!syncing) capri.HearVoiceStyle(); };
            robotSlider = Setting("Robot", AmountNames, 0, 288, "How robotic her voice sounds");
            robotName = new Readout(robotSlider);
            robotSlider.Changed += delegate { if (syncing) return; robotName.Text = AmountName(robotSlider.Value); capri.SetVoiceRobot(Amounts[robotSlider.Value]); };
            robotSlider.Released += delegate { if (!syncing) capri.HearVoiceStyle(); };
            glitchSlider = Setting("Glitch", AmountNames, 0, 316, "How much her voice glitches");
            glitchName = new Readout(glitchSlider);
            glitchSlider.Changed += delegate { if (syncing) return; glitchName.Text = AmountName(glitchSlider.Value); capri.SetVoiceGlitch(Amounts[glitchSlider.Value]); };
            glitchSlider.Released += delegate { if (!syncing) capri.HearVoiceStyle(); };
            sounds = Setting("Sounds", SoundNames, 0, 344, "Sound effects volume");
            soundsName = new Readout(sounds);
            sounds.Changed += delegate { if (syncing) return; soundsName.Text = SoundNames[sounds.Value]; capri.SetSoundLevel(sounds.Value * 10); };
            typing = Switch("Typing sound", 0, 372, delegate { if (!syncing) capri.ToggleTyping(); });
            typing.Hint = "When she types";
            typing.HintFont = small;
            typing.AccessibleName = "Typing sound. A soft typing sound while her text replies type out";
            listenCapri = Switch("Listen for Capri", 0, 400, delegate { if (!syncing) capri.ToggleListenCapri(); });
            Caption("Say her name anywhere to chat", 28, 428, small, Faint);
            listenCapri.AccessibleName = "Listen for Capri. Keep the mic open; say her name anywhere in your sentence. Stop and push to talk still work";
            Caption("Hearing", 28, 454, Type(9.5f), Rose).AccessibleName = "Hearing";
            hearingMenu = Dropdown();
            hearingMenu.AccessibleName = "Hearing engine choices";
            hearingChoice = Make("Auto", ValueAt, 450, ValueWide, 22, delegate { hearingMenu.Show(hearingChoice, new Point(0, hearingChoice.Height)); });
            hearingChoice.Chevron = true;
            hearingChoice.AccessibleName = "Hearing engine";
            for (int i = 0; i < HearingStatus.Choices.Length; i++)
            {
                int choice = i;
                hearingMenu.Items.Add(Choice(HearingStatus.Choices[i], false, delegate { capri.ChooseHearing(choice); }));
                hearingMenu.Items[i].AccessibleName = HearingStatus.Choices[i];
            }
            // The row under Listen for Capri, shown only while her HD voice is on (Lachy, 29 Oct). Off out of the box.
            hdGames = Switch("Free voice in games", 0, 478, delegate { if (!syncing) capri.ToggleHdGameRest(); });
            hdGames.AccessibleName = "Free voice in games: while a game or video is full screen, her free voice talks instead of her HD voice, so the game gets the graphics card";
            hdGames.Visible = false;
            callMode = Switch("Call mode (beta)", 0, 506, delegate { if (!syncing) capri.ToggleCallMode(); });
            callMode.AccessibleName = "Call mode. Only your mic or typing can confirm requests";
            callAppName = Named("Call app", 0, 534);
            callAppMenu = Dropdown();
            callAppMenu.AccessibleName = "Call app choices";
            callApp = Make("Auto", ValueAt, 534, ValueWide, 22, delegate { callAppMenu.Show(callApp, new Point(0, callApp.Height)); }, "Call app");
            callApp.Chevron = true;
            callApp.AccessibleName = "Call app. Auto prefers Discord, then a call app making sound. Pick Browser or Steam yourself";
            foreach (string name in CallApps.Choices)
            {
                string chosen = name;
                callAppMenu.Items.Add(Choice(name, false, delegate { capri.ChooseCallApp(chosen); }));
            }
            callAppNote = Caption(CallApps.Note, 28, 562, small, Faint);
            voiceInMic = Switch("Her voice in your mic", 0, MixAt, delegate { if (!syncing) capri.ToggleVoiceInMic(); });
            voiceInMic.Switch = 68;
            voiceInMic.AccessibleName = "Her voice plays into your mic only in call mode. Pick CABLE Output or Steam Streaming Microphone in your call app.";
            voiceInMicNote = Caption(MicMix.PickLine(MicMix.CableEnd), 28, MixAt + 26, small, Faint);
            // Without a cable the note credits VB-CABLE, and opens its page, where they can donate.
            voiceInMicNote.Click += delegate { if (voiceInMicNote.Text == CableSetup.Credit) capri.OpenCablePage(); };
            voiceInMicState = Caption("", 28, MixAt + 50, small, Faint);
            cableKey = Make("Get VB-CABLE", 244, MixAt + 76, 112, 22, delegate { capri.SetUpCable(); }, "Her voice in your mic");
            cableKey.AccessibleName = "Get VB-CABLE: opens VB-Audio's own setup, which Windows asks you to let run. Click Install Driver in it, then restart your PC. VB-CABLE is donationware from vb-cable.com";
            cableKey.Visible = false;
            mixInputMenu = Dropdown();
            mixInput = Make("Input: default", 28, MixAt + 76, 328, 22, delegate { ShowMixInputs(mixInput); }, "Her voice in your mic");
            mixInput.Chevron = true;
            mixInput.AccessibleName = "Her mix's input: the mic that goes in with her voice";
            mixOutputMenu = Dropdown();
            mixOutput = Make("Output: none", 28, MixAt + 104, 244, 22, delegate { ShowMixOutputs(mixOutput); }, "Her voice in your mic");
            mixOutput.Chevron = true;
            mixOutput.AccessibleName = "Her mix's output: the virtual mic it plays into, which you pick as your mic in your call app";
            mixHow = Make("How to", 280, MixAt + 104, 76, 22, delegate { OpenMix(); }, "Her voice in your mic");
            mixHow.AccessibleName = "How to set up her voice in your mic for your call app";
            micLoudnessName = Caption("Her voice in your mic: loudness", 28, MixAt + 134, Type(9.5f), Rose);
            micLoudness = new Slider(MicLevels);
            micLoudness.Font = Type(9.5f);
            micLoudness.Bounds = new Rectangle(28, MixAt + 158, 328, 22);
            micLoudness.AccessibleName = "Her voice in your mic: loudness";
            Controls.Add(micLoudness);
            micLoudness.Changed += delegate { if (!syncing) capri.SetMicLoudness(50 + micLoudness.Value * 10); };
            int afterVoice = Controls.Count;
            Section("MOVES >", 0, 442);
            popTimes = Make("Pop-in times", 236, 439, 120, 20, delegate { using (PopWindow dialog = new PopWindow(capri)) dialog.ShowDialog(this); });
            popTimes.AccessibleName = "Pop-in times: set the shortest and longest wait in minutes";
            // Her mini player shows what's playing while music plays, with her perched on it.
            miniPlayer = Switch("Mini player", 0, 466, delegate { if (!syncing) capri.TogglePlayer(); });
            miniPlayer.AccessibleName = "Mini player. It shows what's playing while music plays, and she sits on it";
            wander = Setting("Wander", WanderNames, 0, 494, "How much she wanders");
            wanderName = new Readout(wander);
            wander.Changed += delegate { if (syncing) return; wanderName.Text = WanderNames[wander.Value]; capri.SetWanderLevel(wander.Value); };
            sleepAfter = Setting("Sleep", SleepNames, 0, 522, "How long until she falls asleep");
            sleepName = new Readout(sleepAfter);
            sleepAfter.Changed += delegate { if (syncing) return; sleepName.Text = SleepNames[sleepAfter.Value]; capri.SetSleepAfter(Sleeps[sleepAfter.Value]); };
            // How often Check in on me brings her over to say hi.
            pops = Setting("Pop-ins", PopNames, 0, 550, "How often she pops in to say hi");
            popsName = new Readout(pops);
            pops.Changed += delegate { if (syncing) return; popsName.Text = PopNames[pops.Value]; capri.SetPopPace(pops.Value); };
            dance = Switch("Dance to music", 0, 578, delegate { if (!syncing) capri.ToggleDance(); });
            intro = Switch("Glitch in at startup", 0, 606, delegate { if (!syncing) capri.ToggleIntro(); });
            Section("KEYS >", 0, 648);
            talkKeys = Make("Talk: Ctrl + Alt + V", 28, 675, 328, 22, delegate { PickKeys(0); });
            talkKeys.AccessibleName = "Her talk key. Click, press new keys, then Confirm";
            muteKeys = Make("Mute mic: Ctrl + Alt + N", 28, 703, 328, 22, delegate { PickKeys(1); });
            muteKeys.AccessibleName = "Her mute key. Click, press new keys, then Confirm";
            typeKeys = Make("Type: Ctrl + Alt + T", 28, 731, 328, 22, delegate { PickKeys(2); });
            typeKeys.AccessibleName = "Her typing key. Click, press new keys, then Confirm";
            hideKeys = Make("Hide/show: Ctrl + Alt + H", 28, 759, 328, 22, delegate { PickKeys(3); });
            hideKeys.AccessibleName = "Her hide key. Click, press new keys, then Confirm";
            quietKeys = Make("Quiet: Ctrl + Alt + P", 28, 787, 328, 22, delegate { PickKeys(4); });
            quietKeys.AccessibleName = "Her quiet key: she only talks when you talk to her until you press it again. Click, press new keys, then Confirm";
            // Hold to talk, on any key or mouse button. It's read rather than taken, so it still does its own job in a game.
            pushKeys = Make("Push to talk: not set", 28, 815, 328, 22, delegate { PickKeys(Companion.PushSlot); });
            pushKeys.AccessibleName = "Her push-to-talk key: hold it to talk to her, with a game in front too. Click, press a key or a mouse button, then Confirm";
            gameKeys = Make("Gaming Mode: Ctrl + Alt + G", 28, 843, 328, 22, delegate { PickKeys(5); });
            gameKeys.AccessibleName = "Her Gaming Mode key: she reacts to your game until you close it. Click, press new keys, then Confirm";
            // Only a click ends picking a key, so Enter, Esc and Tab can be her keys too. Confirm and Cancel show in KEYS' band
            // while one is being picked.
            confirmKeys = Make("Confirm", 212, 645, 78, 20, delegate { ConfirmKeys(); });
            confirmKeys.Accent = Cyan;
            confirmKeys.Loud = true;
            confirmKeys.AccessibleName = "Confirm her new keys";
            cancelKeys = Make("Cancel", 296, 645, 60, 20, delegate { CancelKeys(); });
            cancelKeys.AccessibleName = "Cancel and keep her old keys";
            clearKeys = Make("Off", 160, 645, 46, 20, delegate { ClearPush(); });
            clearKeys.AccessibleName = "Turn her push-to-talk key off";
            confirmKeys.Visible = cancelKeys.Visible = clearKeys.Visible = false;
            keysNote = Caption("", 28, 872, small, Faint);
            keysNote.MaximumSize = new Size(328, 0);
            for (int i = afterVoice; i < Controls.Count; i++)
            {
                Controls[i].Top += Row * 2 + 22 + MixRows;
                underVoice.Add(new KeyValuePair<Control, int>(Controls[i], Controls[i].Top));
            }
            foreach (Control mixRow in MixControls) underVoice.Add(new KeyValuePair<Control, int>(mixRow, mixRow.Top));
            foreach (Control callRow in CallControls) underVoice.Add(new KeyValuePair<Control, int>(callRow, callRow.Top));
            Section("CAPRI >", c, 58);
            sizeSlider = Setting("Size", SizeNames, c, 82, "Her size");
            sizeName = new Readout(sizeSlider);
            sizeSlider.Changed += delegate { if (syncing) return; sizeName.Text = SizeNames[sizeSlider.Value]; capri.SetCharacterSize(SizeSteps[sizeSlider.Value]); };
            checkIns = Switch("Check in on me", c, 110, delegate { if (!syncing) capri.ToggleCheckIns(); });
            careMenu = Dropdown();
            for (int careRow = 0; careRow < careToggles.Length; careRow++)
            {
                CareKind careKind = (CareKind)careRow;
                Toggle careToggle = Switch(Care.Names[careRow], c, 138 + careRow * Row, delegate { if (!syncing) capri.ToggleCare(careKind); });
                careToggle.Width = 224;
                careToggle.Switch = 68;
                careToggle.AccessibleName = "Check in on me: " + Care.Names[careRow] + " check-in" + (careKind == CareKind.Eyes ? ", look away for 20 seconds" : careKind == CareKind.Meal ? ", mealtimes from noon, if you've been here since before lunch" : "");
                careToggles[careRow] = careToggle;
                Key careTime = Make("", 260 + c, 141 + careRow * Row, 96, 22, delegate { ChooseCareTime(careKind); });
                careTime.Chevron = true;
                careTimes[careRow] = careTime;
            }
            careEdit = Make("Edit check-in lines", 28 + c, 338, 328, 20, delegate { capri.OpenCare(); });
            careEdit.AccessibleName = "Check in on me: edit her check-in lines in check-ins.md; remove lines to silence a check-in";
            int afterCare = Controls.Count;
            onTop = Switch("Always on top", c, 138, delegate { if (!syncing) capri.ToggleOnTop(); });
            // Zenni's ask (30 Oct): under Virtual Super Resolution her Mainframe came out tiny. Auto grows it by how much bigger
            // the desktop is than the panel showing it; 100% to 200% sizes it by hand.
            frameSize = Setting("Mainframe size", FrameSizeNames, c, 166, "Mainframe size. Auto makes it bigger when Virtual Super Resolution or DSR shrinks a big desktop onto a smaller screen");
            frameSizeName = new Readout(frameSize);
            frameSize.Changed += delegate { if (!syncing) ShowFrameSize(); };
            frameSize.Released += delegate { if (!syncing) { capri.SetMainframeSize(FrameSizes[frameSize.Value]); Rezoom(Wanted()); ShowFrameSize(); } };
            frameColours = Setting("Mainframe colours", Neon.LookNames, c, 194, "Mainframe colours. Night City is red and cyan; Black, White and Grey change the Mainframe, her menus, the Type box and her notes");
            frameColours.Changed += delegate { if (!syncing) capri.SetMainframeColours(frameColours.Value); };
            headPats = Switch("Head pats", c, 222, delegate { if (!syncing) capri.ToggleHeadPats(); });
            headPats.AccessibleName = "Head pats. Rubbing the cursor over her gets a grin and a line";
            upsetChaos = Switch("Chaos when upset", c, 250, delegate { if (!syncing) capri.ToggleUpsetChaos(); });
            upsetChaos.AccessibleName = "Chaos when upset. Insulting her can set her clones off until you hold Esc";
            // Hologram stays reversible here even while clicks pass through her body (Lachy, 2 Nov).
            hologram = Switch("Hologram", c, 278, delegate { if (!syncing) capri.ToggleHologram(); });
            hologram.AccessibleName = "Hologram. Clicks pass through her body, talk pill and bubble. Turn this off to give her clicks back";
            int afterPats = Controls.Count;
            Section("WATCH >", c, 208);
            watch = Switch("Watch my screen", c, 232, delegate { if (!syncing) capri.ToggleWatch(); });
            watch.AccessibleName = "Watch my screen. She comments now and then about the desktop. Game comments have their own Comments setting under GAMES";
            Named("Screens", c, 260);
            screenMenu = Dropdown();
            screens = Make("All screens", ValueAt + c, 263, ValueWide, 22, delegate { ShowScreens(); }, "Screens");
            screens.Chevron = true;
            screens.Ellipsis = true;
            screens.AccessibleName = "Screens to watch: All screens";
            caughtUp = Switch("Remember what she sees", c, 288, delegate { if (!syncing) capri.ToggleCaughtUp(); });
            caughtUp.AccessibleName = "Remember what she sees. What she saw on your screen stays in her memory for three hours even when she said nothing about it, which uses a little more of your API";
            gap = Setting("Every", GapNames, c, 316, "Time between glances");
            gapName = new Readout(gap);
            gap.Changed += delegate { if (syncing) return; gapName.Text = GapNames[gap.Value]; capri.SetGlanceGap(Gaps[gap.Value]); };
            watchMemory = Setting("Watch-along memory", WatchLineNames, c, 344, "Lines of PC dialogue held in memory while watching together. The oldest half becomes a note when Remember what she sees is on");
            watchMemoryName = new Readout(watchMemory);
            watchMemory.Changed += delegate { if (!syncing) capri.SetWatchMemory(WatchLines[watchMemory.Value]); };
            int afterWatchMemory = Controls.Count;
            Section("BRAIN >", c, 330);
            Named("Code/Husk Mode", c, 354);
            huskMenu = Dropdown();
            huskMenu.AccessibleName = "Code/Husk Mode choices";
            huskMode = Make("Off", ValueAt + c, 357, ValueWide, 22, delegate { huskMenu.Show(huskMode, new Point(0, huskMode.Height)); }, "Code/Husk Mode");
            huskMode.Chevron = true;
            huskMode.AccessibleName = "Code/Husk Mode";
            for (int i = 0; i < HuskNames.Length; i++)
            {
                int choice = i;
                huskMenu.Items.Add(Choice(HuskNames[i], false, delegate { capri.SetHusk(choice); }));
            }
            int afterHusk = Controls.Count;
            stayOnModel = Switch("Stay on my model", c, 354, delegate { if (!syncing) capri.ToggleStayOnModel(); });
            stayOnModel.AccessibleName = "Stay on my model. When an NVIDIA model gets stuck she says so instead of switching to another one";
            replies = Setting("Replies", Lengths, c, 382, "Reply length");
            repliesName = new Readout(replies);
            replies.Changed += delegate { if (syncing) return; repliesName.Text = Lengths[replies.Value]; capri.SetWordiness(replies.Value); };
            recall = Setting("Memory", Lengths, c, 410, "How much of the chat she keeps");
            recallName = new Readout(recall);
            recall.Changed += delegate { if (syncing) return; recallName.Text = Lengths[recall.Value]; capri.SetRecall(Recalls[recall.Value]); };
            memorySize = Setting("Memory size", MemoryNames, c, 438, "Memory size in tokens for Ollama and her built-in brain. Auto keeps her usual context size");
            memorySizeName = new Readout(memorySize);
            memorySize.Changed += delegate { if (syncing) return; memorySizeName.Text = MemoryNames[memorySize.Value]; capri.SetMemorySize(Brain.MemorySizes[memorySize.Value]); };
            replyLength = Setting("Max reply", ReplyNames, c, 466, "The longest reply she can give, in tokens. Auto keeps to Replies above; 2K or 8K lets her run on. Jobs keep more room when they need it");
            replyLengthName = new Readout(replyLength);
            replyLength.Changed += delegate { if (syncing) return; replyLengthName.Text = ReplyNames[replyLength.Value]; capri.SetReplyLength(replyLength.Value); };
            bubble = Setting("Bubble", BubbleNames, c, 494, "How long her replies stay up");
            bubbleName = new Readout(bubble);
            bubble.Changed += delegate { if (syncing) return; bubbleName.Text = BubbleNames[bubble.Value]; capri.SetBubbleTime(bubble.Value); };
            followUps = Switch("Follow-up messages", c, 522, delegate { if (!syncing) capri.ToggleFollowUps(); });
            followUps.AccessibleName = "Follow-up messages. Sometimes she adds a second thought after her answer. Your typing or speech drops it";
            int afterFollowUps = Controls.Count;
            custom = Input(28 + c, 525, 244, 22);
            custom.Cue = "Custom model name";
            custom.Box.AccessibleName = "Custom model name";
            custom.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseCustom(); } };
            Make("Use", 280 + c, 525, 76, 22, delegate { UseCustom(); }, "Custom model name");
            customNote = Caption("A model in Ollama, on your local server or on your key.", 28 + c, 554, small, Faint);
            customNote.AutoSize = false;
            customNote.AutoEllipsis = true;
            customNote.Size = new Size(328, 28);
            Caption("Local server address", 28 + c, 586, Font, Rose);
            localAddress = Input(28 + c, 615, 244, 22);
            localAddress.Cue = "http://127.0.0.1:1234";
            localAddress.Box.AccessibleName = "Local server address";
            localAddress.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseLocalAddress(); } };
            Make("Save", 280 + c, 615, 76, 22, delegate { UseLocalAddress(); }, "Local server address");
            Section("HABITS >", c, 656);
            Make("Obsidian vault", 104 + c, 653, 112, 20, delegate { ShowMemoryVault(); }).AccessibleName = "Obsidian vault: keep her notes in your Obsidian vault";
            memoriesKey = Make("Open her memories", 220 + c, 653, 136, 20, delegate { EditNotes(true); });
            memoriesKey.AccessibleName = "Open her memories: edit memories.md here";
            startup = Switch("Start with Windows", c, 680, delegate { if (!syncing) capri.ToggleStartup(); });
            discord = Switch("Show on Discord", c, 708, delegate { if (!syncing) capri.ToggleDiscord(); });
            cursor = Switch("Use her cursor", c, 736, delegate { if (!syncing) capri.ToggleCursor(); });
            remembering = Switch("Remember our chats", c, 764, delegate { if (!syncing) capri.ToggleRemembering(); });
            remembering.AccessibleName = "Remember our chats. She keeps daily transcripts and memories.md on this PC, never uploaded";
            // SECURITY took Blackwall mode's place (Lachy, 29 Oct): its button opens her SECURITY window, and the line under it
            // says what the preset does. Blackwall mode is its Blackwall preset now.
            security = Make("Security: " + Security.PresetName, 28 + c, 795, 328, 22, delegate { OpenSecurity(); });
            security.AccessibleName = "Security: what she does on your PC without asking first";
            blackwallNote = Caption(Security.Short, 28 + c, 819, small, Faint);
            blackwallNote.AutoSize = false;
            blackwallNote.TextAlign = ContentAlignment.MiddleRight;
            blackwallNote.Bounds = new Rectangle(28 + c, 819, 328, 18);
            // Frames a second while she sits on a window. Her sway takes 2 s at every fps; past 30 she draws in-betweens.
            perchFps = Setting("Perch", PerchNames, c, 840, "Her perch frames per second");
            perchFpsName = new Readout(perchFps);
            perchFps.Changed += delegate { if (syncing) return; capri.SetPerchFps(Companion.PerchRates[perchFps.Value]); ShowPerch(); };
            perchNote = Caption("", 28 + c, 868, small, Faint);
            perchNote.AutoSize = false;
            perchNote.TextAlign = ContentAlignment.MiddleRight;
            perchNote.Bounds = new Rectangle(28 + c, 868, 328, 18);
            keepAwake = Switch("Keep PC awake for jobs", c, 888, delegate { if (!syncing) capri.ToggleKeepAwake(); });
            hideFullScreen = Switch("Hide in full screen", c, 916, delegate { if (!syncing) capri.ToggleHideFullScreen(); });
            playerFullScreen = Switch("Mini player over full screen", c, 944, delegate { if (!syncing) capri.TogglePlayerFullScreen(); });
            exploreAway = Switch("Explore while I'm away", c, 972, delegate { if (!syncing) capri.ToggleExploreAway(); });
            keepAwake.Switch = hideFullScreen.Switch = playerFullScreen.Switch = exploreAway.Switch = 68;
            keepAwake.AccessibleName = "Keep PC awake for jobs. Keeps the PC and screen awake while a job runs, waits for your answer or watches for something";
            hideFullScreen.AccessibleName = "Hide in full screen. Gaming Mode takes priority; a manual hide stays hidden";
            playerFullScreen.AccessibleName = "Mini player over full screen. Works over borderless windows; exclusive full screen cannot show overlays";
            exploreAway.AccessibleName = "Explore while I'm away. Scrolls browser or reader pages without moving your mouse; your input stops her";
            exploreAfter = Setting("Explore after", AwayNames, c, 1000, "How long you are away before she scrolls a browser or reader");
            exploreAfterName = new Readout(exploreAfter);
            exploreAfter.Changed += delegate { if (!syncing) capri.SetExploreAfter(AwayTimes[exploreAfter.Value]); };
            int gamesFrom = Controls.Count;
            Section("GAMES >", 0, 914);
            miniGames = Make("Play...", 140, 911, 96, 20, delegate { ShowMiniGames(); });
            miniGames.AccessibleName = "Play a mini-game with Capri";
            gameNotes = Make("Controls", 244, 911, 112, 20, delegate { ShowGameNotes(); });
            gameNotes.AccessibleName = "Game controls: teach her the keys before she plays";
            // What she says about a game they're playing, from what happens in it rather than on a timer.
            comments = Setting("Comments", Commentary.PaceNames, 0, 938, "Game comments run separately from Watch my screen and Gaming Mode. Off stops them; Events only and Chatty let her watch your game");
            commentsName = new Readout(comments);
            comments.Changed += delegate { if (syncing) return; commentsName.Text = Commentary.PaceNames[comments.Value]; capri.SetCommentPace(comments.Value); };
            // Her live reactions (F67), on by default, under Comments since they only run while her comments do.
            liveReactions = Switch("Live reactions", 0, 966, delegate { if (!syncing) capri.ToggleReactions(); });
            liveReactions.AccessibleName = "Live reactions. Quick lines on the right of a full-screen game when something happens, while her game comments run. Mute her keeps them as text";
            gamingMode = Switch("Gaming Mode", 0, 994, delegate { if (!syncing) capri.ToggleGaming(); });
            gamingMode.AccessibleName = "Gaming Mode. She reacts to your game until you close it. \"Hey Capri, activate gaming mode\" turns it on. Works best on MAX INTELLIGENCE.";
            Caption("She reacts to your game until you close it.", 28, 1020, small, Faint);
            Caption("\"Hey Capri, activate gaming mode\" turns it on.", 28, 1036, small, Faint);
            Caption("Works best on MAX INTELLIGENCE.", 28, 1052, small, Faint);
            fastHands = Switch("Fast hands in games", 0, 1074, delegate { if (!syncing) capri.ToggleFastHands(); });
            hdGames.Switch = intro.Switch = caughtUp.Switch = fastHands.Switch = 68;
            gameMute = Switch("Mute her", 0, 1102, delegate { if (!syncing) capri.ToggleShush(); });
            gameMute.AccessibleName = "Mute her. She stops talking on her own and only answers when you talk to her, the same as her Quiet key";
            fastHands.AccessibleName = "Fast hands in games. On a Claude key she plays games on Claude Haiku, which moves about three times faster and costs far less. When she's stuck she asks the brain you picked, or Claude Sonnet if that's Haiku, and chat stays on the brain you picked";
            clipHead = Section("CLIPS >", 0, 1158);
            clipGames = Switch("Clip my games", 0, 1182, delegate { if (!syncing) capri.ToggleClips(); });
            clipGames.AccessibleName = "Clip my games. Keeps recent game video and PC sound in memory while the game is in front";
            clipMoments = Switch("Save big moments", 0, 1210, delegate { if (!syncing) capri.ToggleClipMoments(); });
            clipMoments.AccessibleName = "Save big moments. Turns on Clip my games; her game comments can save a clip using your default length";
            Caption("Save folder", 28, 1242, Type(9.5f), Rose);
            clipChoose = Make("Choose folder...", ValueAt, 1238, ValueWide, 22, delegate { PickClipFolder(); });
            clipChoose.AccessibleName = "Choose the folder for future clips";
            clipPath = Caption("", 28, 1266, small, Faint);
            clipPath.AutoSize = false;
            clipPath.AutoEllipsis = true;
            clipPath.Bounds = new Rectangle(28, 1266, 328, 20);
            clipsFolder = Make("Open clips folder", 28, 1294, 328, 22, delegate { capri.OpenClips(); });
            clipsFolder.AccessibleName = "Open your selected clips folder";
            clipLength = Setting("Default length", new[] { "15 s", "30 s", "45 s", "1 min", "1.5 min", "2 min" }, 0, 1322, "Default duration for clip that and automatic clips");
            clipRate = Setting("Frame rate", new[] { "Up to 15 fps", "Up to 24 fps", "Up to 30 fps", "Up to 60 fps" }, 0, 1350, "Target capture frame rate; may drop under load or the memory budget");
            clipSize = Setting("Resolution", new[] { "Auto", "Up to 480p", "Up to 720p", "Up to 1080p" }, 0, 1378, "Maximum clip resolution, keeping the game's shape without upscaling");
            clipBits = Setting("Video bitrate", new[] { "Auto", "2 Mbps", "4 Mbps", "8 Mbps", "12 Mbps", "20 Mbps" }, 0, 1406, "H.264 video bitrate target; higher values make larger files");
            foreach (Slider recording in new[] { clipLength, clipRate, clipSize, clipBits }) recording.Changed += delegate { SaveClipOptions(); };
            Label clipHint = Caption("FPS can drop under load or for longer clips.\nFolder changes apply to future clips.", 28, 1438, small, Faint);
            clipHint.AutoSize = false;
            clipHint.Bounds = new Rectangle(28, 1438, 328, 36);
            int gamesTo = Controls.Count;
            for (int talkSettingAt = afterWatchMemory; talkSettingAt < Controls.Count; talkSettingAt++)
                Controls[talkSettingAt].Top += (talkSettingAt >= afterFollowUps ? 56 : 28) + (Controls[talkSettingAt].Left >= c ? Row : 0);
            for (int huskRow = afterHusk; huskRow < Controls.Count; huskRow++)
                if (Controls[huskRow].Left >= c) Controls[huskRow].Top += Row;
            // Mainframe colours, Head pats, Chaos when upset and Hologram each take a row under Mainframe size.
            for (int belowPats = afterPats; belowPats < Controls.Count; belowPats++)
                if (Controls[belowPats].Left >= c) Controls[belowPats].Top += Row * 4;
            for (int belowCare = afterCare; belowCare < Controls.Count; belowCare++)
                if (Controls[belowCare].Left >= c) Controls[belowCare].Top += Row * 8;
            // The hearing row and Her voice in your mic's rows push GAMES down under KEYS too.
            for (int gameRow = gamesFrom; gameRow < gamesTo; gameRow++)
            {
                Controls[gameRow].Top += Row + MixRows;
                underVoice.Add(new KeyValuePair<Control, int>(Controls[gameRow], Controls[gameRow].Top));
            }
            for (int i = first; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                pageLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                built[control] = control.Top;
                Scrolls(control);
                control.Left += Far;
                Reachable(control, false);
                // Clicking into anything else while a key is being picked keeps the old key, so typing in a box is never swallowed.
                control.Enter += delegate { if (capturing != 0 && control != confirmKeys && control != cancelKeys && control != clearKeys && control != PickedKey) CancelKeys(); };
            }
            BuildMods();
            Controls.Add(topRim);
            Controls.Add(bottomRim);
            topRim.BringToFront();
            bottomRim.BringToFront();
            PrepareHints();
            RememberLayout(this);
            Searching();
            Sync();
            Application.AddMessageFilter(this);
        }

        private static Button DialogButton(string text, int x, int y, int width, Color accent)
        {
            Button button = new Button { Text = text, Bounds = new Rectangle(x, y, width, 30), FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Neon.SheetInk, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = accent;
            return button;
        }

        private void EditNotes(bool memories)
        {
            try
            {
                using (NoteWindow dialog = new NoteWindow(Brain.OpenNote(memories), memories ? "Her memories" : "Her notes about you")) dialog.ShowDialog(this);
            }
            catch (System.IO.IOException) { Notice.Tell(this, "Couldn't read this note. Try opening it again.", "Her notes", true); }
            catch (UnauthorizedAccessException) { Notice.Tell(this, "Couldn't read this note. Check access to its folder.", "Her notes", true); }
        }

        private void ShowGameNotes()
        {
            using (Form dialog = new Form())
            {
                dialog.Text = "Game controls";
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowInTaskbar = false;
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = dialog.MinimizeBox = false;
                dialog.BackColor = Ground;
                dialog.ForeColor = White;
                dialog.Font = Look.Mono(10f, FontStyle.Regular, GraphicsUnit.Point);
                dialog.ClientSize = new Size(480, 172);
                Label label = new Label { Text = "Game name", Bounds = new Rectangle(16, 16, 448, 20) };
                TextBox game = new TextBox { Text = GameNotes.Name(Hands.GameOpen()), Bounds = new Rectangle(16, 42, 448, 24), BackColor = Field, ForeColor = White, BorderStyle = BorderStyle.FixedSingle, AccessibleName = "Game name for controls" };
                Label note = new Label { Text = "She reads these controls before each game job.", Bounds = new Rectangle(16, 78, 448, 30), ForeColor = Faint };
                Button edit = DialogButton("Edit controls", 16, 122, 128, Cyan);
                Button close = DialogButton("Close", 364, 122, 100, Edge);
                edit.Click += delegate
                {
                    if (GameNotes.Name(game.Text).Length == 0) { note.Text = "Enter the game's name first."; return; }
                    try
                    {
                        using (NoteWindow page = new NoteWindow(GameNotes.OpenControls(game.Text), "Game controls")) page.ShowDialog(dialog);
                    }
                    catch (System.IO.IOException) { note.Text = "Couldn't read these controls. Check the game's name and folder."; }
                    catch (UnauthorizedAccessException) { note.Text = "Couldn't read these controls. Check access to their folder."; }
                };
                close.Click += delegate { dialog.Close(); };
                dialog.AcceptButton = edit;
                dialog.CancelButton = close;
                dialog.Controls.AddRange(new Control[] { label, game, note, edit, close });
                dialog.ShowDialog(this);
            }
        }

        internal ContextMenuStrip MiniChoices()
        {
            ContextMenuStrip games = Dropdown();
            string[] names = { "chess", "connect_four", "tic_tac_toe", "rock_paper_scissors" };
            string[] titles = { "Chess", "Connect four", "Tic-tac-toe", "Rock, paper, scissors" };
            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                games.Items.Add(Choice(titles[i], false, delegate { capri.PlayMini(name); }));
            }
            return games;
        }

        internal ContextMenuStrip ShowMiniGames()
        {
            if (miniMenu == null) miniMenu = MiniChoices();
            miniMenu.Show(miniGames, new Point(0, miniGames.Height));
            return miniMenu;
        }

        private void SaveClipOptions()
        {
            if (syncing) return;
            capri.SetClipOptions(ClipSettings.Lengths[clipLength.Value], ClipSettings.Rates[clipRate.Value], ClipSettings.Sizes[clipSize.Value], ClipSettings.Bitrates[clipBits.Value]);
        }

        private void PickClipFolder()
        {
            using (FolderBrowserDialog picker = new FolderBrowserDialog())
            {
                picker.Description = "Choose where future Capri clips are saved";
                picker.SelectedPath = capri.ClipOptions.Folder;
                picker.ShowNewFolderButton = true;
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                string problem = capri.SetClipFolder(picker.SelectedPath);
                if (problem != null) MessageBox.Show(this, problem, "Clips", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }

        private static Label Heading(string text, int y) { return new Label { Text = text, Bounds = new Rectangle(16, y, 488, 18), ForeColor = Neon.Amber, Font = Look.Mono(9f, FontStyle.Bold, GraphicsUnit.Point) }; }

        private static Label Hint(string text, int y, int height) { return new Label { Text = text, Bounds = new Rectangle(16, y, 488, height), ForeColor = Neon.SheetDim }; }

        // Memory vault, under HABITS: her notes can live in their Obsidian vault.
        private void ShowMemoryVault()
        {
            using (Form dialog = new Form())
            {
                dialog.SuspendLayout();
                dialog.AutoScaleMode = AutoScaleMode.None;
                dialog.Text = "Obsidian memory vault";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.BackColor = Neon.Sheet;
                dialog.ForeColor = Neon.SheetInk;
                dialog.Font = Look.Mono(9f, FontStyle.Regular, GraphicsUnit.Point);
                dialog.ClientSize = new Size(520, 170);
                // Their own Obsidian vaults, read from Obsidian's list on this PC: picking one keeps her memories in its Capri Memories note.
                List<KeyValuePair<string, string>> vaults = Companion.ObsidianVaults();
                List<string> vaultNotes = new List<string> { "" };
                ComboBox vault = new ComboBox { Bounds = new Rectangle(16, 44, 368, 24), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Neon.SheetInk };
                vault.AccessibleName = "Which Obsidian vault her memories go in";
                vault.Items.Add("Her own notes (no vault)");
                foreach (KeyValuePair<string, string> found in vaults) { vault.Items.Add("Vault: " + found.Key); vaultNotes.Add(System.IO.Path.Combine(found.Value, Companion.VaultNote)); }
                Action showVault = delegate
                {
                    string current = Companion.NotesIn(capri.Vault);
                    int at = current == null ? 0 : vaultNotes.FindIndex(delegate(string note) { return string.Equals(note, current, StringComparison.OrdinalIgnoreCase); });
                    if (at < 0)
                    {
                        // A note they picked with Browse stays as its own choice.
                        at = vaultNotes.FindIndex(1, delegate(string note) { return note.StartsWith("|", StringComparison.Ordinal); });
                        if (at < 0) { vault.Items.Add(""); vaultNotes.Add(""); at = vaultNotes.Count - 1; }
                        vault.Items[at] = "Note: " + System.IO.Path.GetFileName(current);
                        vaultNotes[at] = "|" + current;
                    }
                    vault.SelectedIndex = at;
                };
                bool showing = true;
                showVault();
                showing = false;
                Button pickVault = DialogButton("Browse", 392, 42, 112, Neon.SheetDim);
                Label vaultNote = Hint(capri.Vault.Length > 0 ? capri.VaultHere ? "She saves her memories to " + System.IO.Path.GetFileName(Companion.NotesIn(capri.Vault)) + "." : "That vault wasn't there when she started, so she's using her own notes." : vaults.Count > 0 ? "Pick one of your Obsidian vaults and her memories go in a Capri Memories note there." : "No Obsidian vaults found on this PC. Open one in Obsidian first, or Browse to a note.", 78, 34);
                Action<string> useVault = delegate(string chosen)
                {
                    vaultNote.Text = capri.SetVault(chosen);
                    showing = true;
                    showVault();
                    showing = false;
                };
                vault.SelectedIndexChanged += delegate { if (!showing && vault.SelectedIndex >= 0) useVault(vaultNotes[vault.SelectedIndex].TrimStart('|')); };
                pickVault.Click += delegate
                {
                    // A save box, so they can pick a note already in a vault or type a new one's name.
                    using (SaveFileDialog picker = new SaveFileDialog { Title = "Pick the note her memories go in", Filter = "Notes (*.md)|*.md", DefaultExt = "md", AddExtension = true, OverwritePrompt = false, CheckPathExists = true, FileName = Companion.VaultNote })
                    {
                        string current = Companion.NotesIn(capri.Vault);
                        if (current == null && vaults.Count > 0) current = System.IO.Path.Combine(vaults[0].Value, Companion.VaultNote);
                        if (current != null && System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(current))) { picker.InitialDirectory = System.IO.Path.GetDirectoryName(current); picker.FileName = System.IO.Path.GetFileName(current); }
                        if (picker.ShowDialog(dialog) == DialogResult.OK) useVault(picker.FileName);
                    }
                };
                Button close = DialogButton("Close", 404, 124, 100, Neon.SheetDim);
                close.Click += delegate { dialog.Close(); };
                dialog.CancelButton = close;
                dialog.Controls.AddRange(new Control[] { Heading("MEMORY VAULT >", 12), vault, pickVault, vaultNote, close });
                Fit(dialog);
                dialog.ShowDialog(this);
            }
        }

        // A section's band and title. x is 0 for the left column or Column for the right; the band spans the column.
        private Head Section(string title, int x, int y) { return Heading(this, title, new Point(28 + x, y), Type(10f, true), White, 352); }

        // A heading on any of her windows: a section's band and title, or with no span a window's own big red title.
        private static Head Heading(Control window, string text, Point at, Font font, Color colour, int span)
        {
            Head head = new Head();
            head.Text = text;
            head.Font = font;
            head.ForeColor = colour;
            head.BackColor = Ground;
            head.Span = span;
            head.Location = at;
            window.Controls.Add(head);
            return head;
        }

        // A setting's name, on the left of its row.
        private Label Named(string name, int x, int y) { return Caption(name, 28 + x, y + 6, Font, Rose); }

        // A setting with a slider: its name on the left of its row and its value's box on the right.
        private Slider Setting(string name, string[] steps, int x, int y, string spoken)
        {
            Label label = Named(name, x, y);
            Slider slider = new Slider(steps);
            slider.Font = Type(9.5f);
            slider.Bounds = new Rectangle(ValueAt + x, y + 3, ValueWide, 22);
            slider.AccessibleName = spoken;
            Controls.Add(slider);
            Explain(slider, name, "Mainframe.cs", label);
            return slider;
        }

        // An on or off setting's whole row: its name, then OFF and ON.
        private Toggle Switch(string name, int x, int y, EventHandler changed)
        {
            Toggle toggle = new Toggle();
            toggle.Text = name;
            toggle.Font = Font;
            toggle.Bounds = new Rectangle(28 + x, y + 3, 328, 22);
            toggle.CheckedChanged += changed;
            Controls.Add(toggle);
            Explain(toggle, name, Array.IndexOf(Care.Names, name) >= 0 ? "Care.cs" : "Mainframe.cs");
            return toggle;
        }

        private static string CareTimeName(CareKind kind, int time)
        {
            if (kind == CareKind.Late) return (time % 12 == 0 ? 12 : time % 12) + (time < 12 ? " am" : " pm");
            return time % 60 == 0 ? time / 60 + " h" : time + " min";
        }

        private void ChooseCareTime(CareKind kind)
        {
            CareChoices(kind);
            Key careButton = careTimes[(int)kind];
            careMenu.Show(careButton, new Point(0, careButton.Height));
        }

        private void CareChoices(CareKind kind)
        {
            careMenu.Items.Clear();
            int[] choices = kind == CareKind.Late ? new[] { 21, 22, 23, 0, 1, 2, 3, 4, 5 } : kind == CareKind.Meal ? new[] { 180, 240, 300, 360, 480 } : new[] { 15, 25, 30, 45, 60, 90, 120, 180, 240 };
            foreach (int careChoice in choices)
            {
                int careValue = careChoice;
                ToolStripMenuItem careItem = new ToolStripMenuItem(CareTimeName(kind, careValue));
                careItem.Checked = capri.CareTime(kind) == careValue;
                careItem.Click += delegate { capri.SetCareTime(kind, careValue); };
                careMenu.Items.Add(careItem);
            }
        }

        private void UseNickname()
        {
            capri.SetNickname(nickname.Box.Text);
            nickname.Box.Text = capri.Nickname;
        }

        internal string NicknameShown { get { return nickname.Box.Text; } }

        internal void TypeNickname(string typed) { nickname.Box.Text = typed; UseNickname(); }

        private void UseCustom()
        {
            customNote.Text = capri.ChooseCustomModel(custom.Box.Text, lastInstalled);
        }

        internal void TypeCustomModel(string name) { custom.Box.Text = name; UseCustom(); }

        private void UseLocalAddress()
        {
            customNote.Text = capri.SetLocalAddress(localAddress.Box.Text);
            localAddress.Box.Text = capri.LocalAddress;
        }

        internal void TypeLocalAddress(string address) { localAddress.Box.Text = address; UseLocalAddress(); }

        internal string LocalAddressShown { get { return localAddress.Box.Text; } }

        // Whether her settings page shows its advanced settings, and whether her language menu has a language ticked, for her
        // settings tool's check.
        internal bool AdvancedShown { get { return !simple; } }
        internal bool LanguageIs(string language)
        {
            bool found = false;
            foreach (ToolStripMenuItem item in LanguageItems(languageMenu.Items))
                if ((string)item.Tag == language) { if (!item.Checked) return false; found = true; }
            return found;
        }

        private static string LanguageName(int row)
        {
            string name = Brain.Languages[row, 0], english = Brain.Languages[row, 1];
            return name == english ? name : name + " (" + english + ")";
        }

        private ToolStripMenuItem LanguageItem(int row)
        {
            string picked = Brain.Languages[row, 1];
            ToolStripMenuItem item = Choice(LanguageName(row), false, delegate { capri.SetLanguage(picked); Sync(); });
            item.Tag = picked;
            return item;
        }

        internal static IEnumerable<ToolStripMenuItem> LanguageItems(ToolStripItemCollection items)
        {
            foreach (ToolStripItem entry in items)
            {
                ToolStripMenuItem item = entry as ToolStripMenuItem;
                if (item == null) continue;
                yield return item;
                if (item.HasDropDownItems)
                    foreach (ToolStripMenuItem child in LanguageItems(item.DropDownItems)) yield return child;
            }
        }

        private void FillLanguages(Rectangle area)
        {
            while (languageMenu.Items.Count > 0) languageMenu.Items[0].Dispose();
            languageMenu.Font = Font;
            if (languageFont != null) { languageFont.Dispose(); languageFont = null; }
            languageMenu.Items.Add(LanguageItem(0));
            languageCurrent = Choice("", true, delegate { capri.SetLanguage((string)languageCurrent.Tag); Sync(); });
            languageMenu.Items.Add(languageCurrent);
            languageMenu.Items.Add(new ToolStripSeparator());
            List<int> rows = new List<int>();
            for (int i = 1; i < Brain.Languages.GetLength(0); i++) rows.Add(i);
            rows.Sort(delegate(int a, int b) { return StringComparer.OrdinalIgnoreCase.Compare(Brain.Languages[a, 1], Brain.Languages[b, 1]); });
            LanguageRows(languageMenu, rows, area, true);
            SyncLanguages();
            bool current = languageCurrent.Available;
            languageCurrent.Available = true;
            FitLanguages(area);
            languageCurrent.Available = current;
        }

        private void LanguageRows(ToolStripDropDown menu, List<int> rows, Rectangle area, bool root)
        {
            if (!root)
            {
                foreach (int row in rows) menu.Items.Add(LanguageItem(row));
                if (menu.GetPreferredSize(Size.Empty).Height <= area.Height - 4 || rows.Count == 1) return;
                while (menu.Items.Count > 0) menu.Items[0].Dispose();
            }
            int count = (rows.Count + 3) / 4;
            for (int start = 0; start < rows.Count; start += count)
            {
                List<int> part = rows.GetRange(start, Math.Min(count, rows.Count - start));
                if (part.Count == 1) { menu.Items.Add(LanguageItem(part[0])); continue; }
                ToolStripMenuItem group = Choice(Brain.Languages[part[0], 1] + " - " + Brain.Languages[part[part.Count - 1], 1], false, null);
                ContextMenuStrip drop = Dropdown();
                drop.Opened += delegate { KeepLanguageMenu(drop); };
                group.DropDown = drop;
                menu.Items.Add(group);
                LanguageRows(drop, part, area, false);
            }
        }

        private void FitLanguages(Rectangle area)
        {
            for (;;)
            {
                Size size = languageMenu.GetPreferredSize(Size.Empty);
                foreach (ToolStripMenuItem item in LanguageItems(languageMenu.Items))
                    if (item.HasDropDownItems)
                    {
                        Size child = item.DropDown.GetPreferredSize(Size.Empty);
                        size = new Size(Math.Max(size.Width, child.Width), Math.Max(size.Height, child.Height));
                    }
                if (size.Width <= area.Width - 4 && size.Height <= area.Height - 4 || languageMenu.Font.SizeInPoints <= 1f) return;
                float ratio = Math.Min((area.Width - 4f) / size.Width, (area.Height - 4f) / size.Height);
                Font font = new Font(Font.FontFamily, Math.Max(1f, languageMenu.Font.SizeInPoints * ratio * 0.9f), Font.Style, GraphicsUnit.Point);
                languageMenu.Font = font;
                foreach (ToolStripMenuItem item in LanguageItems(languageMenu.Items)) if (item.HasDropDownItems) item.DropDown.Font = font;
                if (languageFont != null) languageFont.Dispose();
                languageFont = font;
            }
        }

        private static Point LanguageAt(Point at, Size size, Rectangle area)
        {
            return new Point(Math.Max(area.Left, Math.Min(at.X, area.Right - size.Width)), Math.Max(area.Top, Math.Min(at.Y, area.Bottom - size.Height)));
        }

        private void KeepLanguageMenu(ToolStripDropDown menu)
        {
            menu.Location = LanguageAt(menu.Location, menu.Size, Screen.FromControl(this).WorkingArea);
        }

        internal ContextMenuStrip ShowLanguages()
        {
            DismissHint();
            languageMenu.Close();
            Rectangle area = Screen.FromControl(this).WorkingArea;
            FillLanguages(area);
            Point at = LanguageAt(languages.PointToScreen(new Point(0, languages.Height)), languageMenu.GetPreferredSize(Size.Empty), area);
            languageMenu.Show(languages, languages.PointToClient(at));
            return languageMenu;
        }

        private void SyncLanguages()
        {
            string language = capri.Language;
            int row = 0;
            for (int i = 0; i < Brain.Languages.GetLength(0); i++) if (Brain.Languages[i, 1] == language) { row = i; break; }
            languages.Text = Brain.Languages[row, 0];
            languageCurrent.Text = LanguageName(row);
            languageCurrent.Tag = Brain.Languages[row, 1];
            languageCurrent.Available = row != 0;
            foreach (ToolStripMenuItem item in LanguageItems(languageMenu.Items)) item.Checked = (string)item.Tag == language;
            languages.Invalidate();
            if (languageMenu.Visible) KeepLanguageMenu(languageMenu);
        }

        internal bool SettingsShown { get { return paged; } }

        internal void ShowFreeKeys()
        {
            DismissHint();
            freeKeyMenu.Show(freeKey, new Point(freeKey.Width, freeKey.Height), ToolStripDropDownDirection.BelowLeft);
            freeKeyMenu.Items[0].Select();
        }

        internal bool FreeKeysShown { get { return freeKeyMenu.Visible; } }

        internal int FreeKeyChoices { get { return freeKeyMenu.Items.Count; } }

        internal void PickFreeKey(int index) { ((ToolStripMenuItem)freeKeyMenu.Items[index]).PerformClick(); freeKeyMenu.Close(); }

        internal void CloseFreeKeys() { freeKeyMenu.Close(); }

        internal string SizeStatus { get { return sizeName.Text; } }

        internal bool TypingShown { get { return typing.Checked; } }

        internal bool ListenCapriShown { get { return listenCapri.Checked; } }

        internal void ShowCallApp()
        {
            bool was = syncing;
            syncing = true;
            callMode.Checked = capri.CallModeOn;
            syncing = was;
            CallApp app = CallApps.Get(capri.CallAppChoice);
            Retext(callApp, app == null ? "Auto" : app.Choice.StartsWith("Browser", StringComparison.Ordinal) ? "Browser" : app.Choice);
            foreach (ToolStripMenuItem item in callAppMenu.Items) item.Checked = item.Text == capri.CallAppChoice;
            if (mixSheet != null && !mixSheet.IsDisposed) mixSheet.Sync();
        }

        private Control[] CallControls { get { return new Control[] { callMode, callAppName, callApp, callAppNote }; } }

        internal string CallAppShown { get { return callApp.Text; } }

        internal void PickCallApp(int index) { ((ToolStripMenuItem)callAppMenu.Items[index]).PerformClick(); }

        internal string CallAppFits()
        {
            foreach (Control row in CallControls)
            {
                if (row.Left < 0 || row.Right > Scaled(356) || row.Top < hearingChoice.Bottom || row.Bottom > voiceInMic.Top) return "Call app rows are outside their space.";
                foreach (Control other in Controls)
                    if (row != other && other.Visible && other.Left < Scaled(Column) && !(other is Label && other.Text.Length == 0) && row.Bounds.IntersectsWith(other.Bounds)) return "Call app overlaps " + other.AccessibleName + ".";
            }
            if (TextRenderer.MeasureText(callMode.Text, callMode.Font, Size.Empty, TextFormatFlags.NoPadding).Width > callMode.NameRoom) return "Call mode does not fit.";
            if (callAppName.Right > callApp.Left || callAppName.Bottom > callAppNote.Top || callApp.Bottom > callAppNote.Top) return "Call app labels overlap.";
            if (TextRenderer.MeasureText(callAppNote.Text, callAppNote.Font).Width > Scaled(328)) return "Call app note does not fit.";
            foreach (string name in CallApps.Choices)
            {
                string shown = name.StartsWith("Browser", StringComparison.Ordinal) ? "Browser" : name;
                if (TextRenderer.MeasureText(shown, callApp.Font).Width + Scaled(28) > callApp.Width) return name + " does not fit.";
            }
            return null;
        }

        internal string CallAppAt(float scale)
        {
            float was = Zoom;
            try { Rezoom(scale / DeviceScale() / TextScale()); ShowSettings(true); return CallAppFits(); }
            finally { Rezoom(was); }
        }

        internal void FlipListenCapri() { listenCapri.Checked = !listenCapri.Checked; }

        internal string HdGamesStatus { get { return hdGames.Visible ? (hdGames.Checked ? "on" : "off") : "-"; } }

        // Free voice in games has the row under Listen for Capri, shown only while her HD voice is on. Without it, MOVES and KEYS
        // move up into its row.
        private void ShowHdGames()
        {
            bool was = syncing, hd = capri.HdVoiceOn;
            syncing = true;
            hdGames.Visible = hd;
            hdGames.Checked = capri.HdGameRest;
            syncing = was;
            int lift = hd ? 0 : Row;
            if (lift == lifted) return;
            lifted = lift;
            int back = pageScroll;
            ScrollPage(0);
            foreach (KeyValuePair<Control, int> entry in underVoice) entry.Key.Top = Scaled(entry.Value - lift);
            CloseGaps();
            ScrollPage(back);
        }

        // MOVES' and KEYS' controls with their tops as built, and how far up they've moved.
        private readonly List<KeyValuePair<Control, int>> underVoice = new List<KeyValuePair<Control, int>>();
        private int lifted;

        // Without a cable the switch can't go on: the note credits VB-CABLE with a link to its page, and the line under it says it's
        // needed, then where VB-Audio's setup is at. With Steam's mic only, the note says VB-CABLE usually sounds better and Get
        // VB-CABLE stays (a tester's Steam mic sounded bad to their friends, 4 Nov). With VB-CABLE, the note says to pick CABLE
        // Output, and the line says to turn it on when Windows has it off, or while it's on what's wrong, if anything. The switch
        // still goes off if the cable went away while it was on.
        internal void ShowVoiceInMic()
        {
            bool was = syncing, cable = capri.CableReady, on = capri.VoiceInMic;
            bool vb = capri.VbCableReady;
            string problem = capri.VoiceInMicProblem, setup = capri.CableSetupLine;
            syncing = true;
            voiceInMic.Checked = on;
            voiceInMic.Enabled = cable || on;
            micLoudness.Value = (capri.MicLoudness - 50) / 10;
            syncing = was;
            voiceInMicNote.Text = cable ? MicMix.PickLine(capri.CableEnd) : CableSetup.Credit;
            voiceInMicNote.Cursor = cable ? Cursors.Default : Cursors.Hand;
            voiceInMicState.Text = !vb && setup != null ? setup : !cable ? MicMix.Missing : on && problem != null ? problem : capri.CableFarOff ? MicMix.OffLine(capri.CableEnd) : "";
            voiceInMicState.ForeColor = cable ? Hot : Faint;
            // Visible reads false while Settings isn't on screen, so the width goes by what it was set to.
            cableOffered = !vb && !capri.CableSetupOpen;
            cableKey.Visible = cableOffered;
            FitMixInput();
            Retext(mixInput, "Input: " + (capri.MixInputName ?? "no mic found"));
            Retext(mixOutput, "Output: " + (cable ? MicMix.EndOf(capri.MixOutput) : "none yet"));
            voiceInMic.Invalidate();
            voiceInMicNote.Invalidate();
            voiceInMicState.Invalidate();
            if (mixSheet != null && !mixSheet.IsDisposed && mixSheet.Visible) mixSheet.Sync();
        }

        private bool cableOffered;

        // Input ends just before Get VB-CABLE (at 244) or runs the whole column. It goes by the built layout, since Settings moves
        // hidden pages' controls off to the side, and a new Mainframe size puts every control back as built, so Rezoom calls
        // this again.
        private void FitMixInput() { mixInput.Width = Scaled(cableOffered ? 208 : 328); }

        private static void Retext(Control control, string text)
        {
            if (control.Text == text) return;
            control.Text = text;
            control.Invalidate();
        }

        private Control[] MixControls { get { return new Control[] { voiceInMic, voiceInMicNote, voiceInMicState, cableKey, mixInput, mixOutput, mixHow, micLoudnessName, micLoudness }; } }

        internal string MicLoudnessShown { get { return micLoudness.Shown; } }

        internal void SlideMicLoudness(int index) { micLoudness.Slide(index); }

        // Her mix's Input: the mic her ears use, or one of its own. Never a cable's far end, which carries her voice.
        private void ShowMixInputs(Control under)
        {
            capri.RefreshHearingDevices();
            string chosen = capri.MixInputPick;
            List<Mic> found = capri.HearingMics.FindAll(delegate(Mic mic) { return !MicMix.IsCableOut(mic.Name); });
            mixInputMenu.Items.Clear();
            mixInputMenu.Items.Add(Choice("Same as her ears (VOICE > Mic)", Ears.Find(chosen, found) == null, delegate { capri.ChooseMixInput(null); }));
            if (found.Count > 0) mixInputMenu.Items.Add(new ToolStripSeparator());
            foreach (Mic mic in found)
            {
                string id = mic.Id;
                mixInputMenu.Items.Add(Choice(mic.Name, id == chosen, delegate { capri.ChooseMixInput(id); }));
            }
            mixInputMenu.Show(under, new Point(0, under.Height));
        }

        // Her mix's Output: VB-CABLE's or Steam's virtual mic, by the name Discord lists it under, or VB-CABLE's setup.
        private void ShowMixOutputs(Control under)
        {
            string chosen = capri.MixOutputPick;
            List<MixDevice> cables = capri.MixOutputs;
            bool vb = false;
            mixOutputMenu.Items.Clear();
            mixOutputMenu.Items.Add(Choice("Automatic: VB-CABLE, else Steam's", cables.Find(delegate(MixDevice each) { return each.Id == chosen; }) == null, delegate { capri.ChooseMixOutput(null); }));
            if (cables.Count > 0) mixOutputMenu.Items.Add(new ToolStripSeparator());
            foreach (MixDevice cable in cables)
            {
                string id = cable.Id;
                vb |= !MicMix.IsSteam(cable.Name);
                mixOutputMenu.Items.Add(Choice(MicMix.FarName(cable), id == chosen, delegate { capri.ChooseMixOutput(id); }));
            }
            if (!vb)
            {
                mixOutputMenu.Items.Add(new ToolStripSeparator());
                mixOutputMenu.Items.Add(Choice("Get VB-CABLE (free)...", false, delegate { capri.SetUpCable(); }));
            }
            mixOutputMenu.Show(under, new Point(0, under.Height));
        }

        // Her voice in your mic's setup steps, over the Mainframe like VOICE LAYERS.
        private void OpenMix()
        {
            if (mixSheet != null && !mixSheet.IsDisposed && mixSheet.Zoom != zoom) mixSheet.Close();
            if (mixSheet == null || mixSheet.IsDisposed) mixSheet = new MixSheet(this, zoom);
            mixSheet.Sync();
            if (!mixSheet.Visible) mixSheet.Show(this);
            mixSheet.Activate();
        }

        // Her voice in your mic's setup window as they'd see it, for the smoke test: what it says, what's crowded, and a picture.
        internal void ShowMixWindow() { OpenMix(); }

        internal string MixShown { get { return mixSheet == null || mixSheet.IsDisposed || !mixSheet.Visible ? null : mixSheet.Showing; } }
        internal string MixCallSteps { get { return mixSheet == null || mixSheet.IsDisposed ? null : mixSheet.CallSteps; } }
        internal static string MixAppName(string process) { return MixSheet.AppName(process); }

        internal void CloseMix() { if (mixSheet != null && !mixSheet.IsDisposed) mixSheet.Close(); }

        internal void FlipMix() { if (mixSheet != null && !mixSheet.IsDisposed) mixSheet.Flip(); }

        internal string MixSheetAt(float zoom, string picture)
        {
            using (MixSheet drawn = new MixSheet(this, zoom))
            {
                drawn.Show(this);
                drawn.Sync();
                string crowded = drawn.Crowded(), size = " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoom + ")";
                if (picture != null) drawn.Picture(picture);
                drawn.Close();
                return crowded == null ? null : crowded + size;
            }
        }

        internal string MixRowsShown { get { return mixInput.Text + " | " + mixOutput.Text; } }

        internal string MixOutputChoices()
        {
            ShowMixOutputs(mixOutput);
            List<string> choices = new List<string>();
            foreach (ToolStripItem item in mixOutputMenu.Items) if (item is ToolStripMenuItem) choices.Add(item.Text);
            mixOutputMenu.Close();
            return string.Join(" | ", choices.ToArray());
        }

        // The switch, whether it can be clicked, the line under the note, the button and the note, for the smoke test.
        internal string VoiceInMicStatus { get { return (voiceInMic.Checked ? "on" : "off") + " | " + (voiceInMic.Enabled ? "open" : "locked") + " | " + (voiceInMicState.Text.Length > 0 ? voiceInMicState.Text : "-") + " | " + (cableKey.Visible ? cableKey.Text : "-") + " | " + voiceInMicNote.Text; } }

        internal void FlipVoiceInMic() { voiceInMic.Checked = !voiceInMic.Checked; }

        internal void PressCable() { if (cableKey.Visible && cableKey.Enabled) InvokeOnClick(cableKey, EventArgs.Empty); }

        internal void PressCableCredit() { InvokeOnClick(voiceInMicNote, EventArgs.Empty); }

        // Her voice in your mic's rows fit the page with nothing over them: the switch's name in its room, the note and the line
        // inside the column, the button clear of the line, and MOVES or GAMES below them all.
        internal string VoiceInMicFits()
        {
            Rectangle page = PageArea;
            int column = Scaled(356);
            if (TextRenderer.MeasureText(voiceInMic.Text, voiceInMic.Font, Size.Empty, TextFormatFlags.NoPadding).Width > voiceInMic.NameRoom) return "the switch's name doesn't fit";
            foreach (Control row in MixControls)
                if (row.Visible && !(row is Label && row.Text.Length == 0) && (!page.Contains(row.Bounds) || row.Right > column)) return (row.Text.Length > 0 ? row.Text : row.AccessibleName) + " pokes out";
            if (voiceInMicNote.Top < voiceInMic.Bottom || voiceInMicState.Top < voiceInMicNote.Bottom) return "the note or the line overlaps the row above it";
            if (cableKey.Visible && (cableKey.Left < mixInput.Right + Scaled(4) || cableKey.Top < voiceInMicState.Bottom || TextRenderer.MeasureText(cableKey.Text, cableKey.Font).Width + Scaled(12) > cableKey.Width)) return "the button covers Input or doesn't fit";
            if (mixInput.Top < voiceInMicState.Bottom || mixOutput.Top < Math.Max(mixInput.Bottom, cableKey.Bottom) || mixHow.Top < mixInput.Bottom || mixHow.Left < mixOutput.Right) return "Input, Output or How to overlaps a row";
            foreach (string line in new[] { MicMix.Missing, CableSetup.Finish, CableSetup.Declined, CableSetup.Failed, CableSetup.Running, MicMix.Unopened, MicMix.NoMic, MicMix.MicGone, MicMix.MutedLine(MicMix.CableEnd), MicMix.MutedLine(MicMix.SteamEnd), MicMix.OffLine(MicMix.CableEnd), MicMix.OffLine(MicMix.SteamEnd) })
                if (voiceInMicState.Left + TextRenderer.MeasureText(line, voiceInMicState.Font).Width > column) return "\"" + line + "\" doesn't fit";
            foreach (string note in new[] { MicMix.PickLine(MicMix.CableEnd), MicMix.PickLine(MicMix.SteamEnd), CableSetup.Credit, CableSetup.Better })
                if (voiceInMicNote.Left + TextRenderer.MeasureText(note, voiceInMicNote.Font).Width > column) return "\"" + note + "\" doesn't fit";
            int below = int.MaxValue;
            foreach (KeyValuePair<Control, int> entry in underVoice)
                if (entry.Key.Visible && entry.Key.Left < Scaled(Column) && !Folded(entry.Key) && entry.Key.Top > voiceInMic.Top && Array.IndexOf(MixControls, entry.Key) < 0) below = Math.Min(below, entry.Key.Top);
            if (micLoudnessName.Top < Math.Max(mixOutput.Bottom, mixHow.Bottom) || micLoudness.Top < micLoudnessName.Bottom) return "loudness overlaps a row";
            if (TextRenderer.MeasureText(micLoudnessName.Text, micLoudnessName.Font).Width > Scaled(328)) return "the loudness name doesn't fit";
            int last = micLoudness.Bottom;
            return below - Scaled(5) < last ? "the next section starts on top of it" : null;
        }

        internal bool PlayerShown { get { return miniPlayer.Checked; } }

        internal void FlipPlayer() { miniPlayer.Checked = !miniPlayer.Checked; }

        internal string HabitsStatus { get { return startup.Checked ? "startup" : "-"; } }

        internal bool DiscordShown { get { return discord.Checked; } }

        internal bool HeadPatsShown { get { return headPats.Checked; } }
        internal bool UpsetChaosShown { get { return upsetChaos.Checked; } }

        internal string WanderShown { get { return wanderName.Text; } }

        internal string JobsStatus { get { return (cursor.Checked ? "cursor" : "-") + " | " + security.Text; } }

        internal string BlackwallStatus { get { return security.Text + " | " + blackwallNote.Text; } }

        internal string SettingsStatus { get { return languages.Text + " | " + volumeName.Text + " | " + soundsName.Text + " | " + wanderName.Text + " | " + sleepName.Text + " | " + (dance.Checked ? "dance" : "-") + " | " + (intro.Checked ? "intro" : "-") + " | " + repliesName.Text + " | " + recallName.Text + " | " + bubbleName.Text; } }

        internal void ShowSettings(bool on)
        {
            DismissHint();
            if (!on) CancelKeys();
            if (on && modded) ShowMods(false);
            ScrollPage(0);
            paged = on;
            simple = !capri.AdvancedSettings;
            advancedKey.Text = simple ? "Advanced Settings" : "Simple Settings";
            advancedKey.AccessibleName = simple ? "Advanced Settings: show every setting" : "Simple Settings: show only the main settings";
            advancedKey.Invalidate();
            more.Text = on || modded ? "Back" : "Settings";
            more.Invalidate();
            Place();
            foreach (KeyValuePair<Control, int> entry in mainLeft) Reachable(entry.Key, !on);
            foreach (KeyValuePair<Control, int> entry in pageLeft) Reachable(entry.Key, on && !Folded(entry.Key));
            Reachable(modsKey, !on);
            if (Hidden(ActiveControl) || on && ActiveControl == modsKey) more.Focus();
            if (on) Sync();
            wheel = 0;
            CloseGaps();
            Arrange(expanded);
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (on && Right > area.Right) Left = Math.Max(area.Left, area.Right - Width);
            // She opens centred on the shorter main page, so the taller Settings page can hang off the bottom.
            if (on && Bottom > area.Bottom) Top = Math.Max(area.Top, area.Bottom - Height);
        }

        // Each page's controls where they belong and the other page's far off to the right, with her title bar's buttons at
        // the right end of whichever page shows.
        private void Place()
        {
            foreach (KeyValuePair<Control, int> entry in mainLeft) entry.Key.Left = Scaled(entry.Value) + (paged || modded ? Far : 0);
            foreach (KeyValuePair<Control, int> entry in pageLeft) entry.Key.Left = Scaled(entry.Value) + (paged && !Folded(entry.Key) ? 0 : Far);
            foreach (KeyValuePair<Control, int> entry in modLeft) entry.Key.Left = Scaled(entry.Value) + (modded ? 0 : Far);
            int edge = paged ? Column : 0;
            // Settings' own Advanced Settings key takes Mods' place by Back, so Mods waits off the page there.
            modsKey.Left = Scaled(234) + (paged ? Far : 0);
            minimize.Left = Scaled(300 + edge);
            close.Left = Scaled(330 + edge);
        }

        private bool Folded(Control control)
        {
            int top;
            if (!simple || control == advancedKey || !built.TryGetValue(control, out top)) return false;
            foreach (int[] band in pageLeft.Exists(entry => entry.Key == control && entry.Value >= Column) ? FoldedRight : FoldedLeft)
                if (top >= band[0] && top < band[1]) return true;
            return false;
        }

        // Moves what's left up over the rows Settings hides while it's simple, or back where it was built.
        private void CloseGaps()
        {
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                int top;
                if (!built.TryGetValue(entry.Key, out top) || Folded(entry.Key)) continue;
                int gone = 0;
                if (simple) foreach (int[] band in entry.Value >= Column ? FoldedRight : FoldedLeft) if (top >= band[1]) gone += band[1] - band[0];
                bool lifts = underVoice.Exists(moved => moved.Key == entry.Key);
                entry.Key.Top = Scaled(top - gone - (lifts && !simple ? lifted : 0));
            }
        }

        // A header or settings control moves with the page, and Tab landing on it scrolls it into view.
        private void Scrolls(Control control)
        {
            scrolled.Add(control);
            control.Enter += delegate { Reveal(control); };
        }

        // Scrolls just far enough to show all of a control Tab reached. A click leaves the page where it is.
        private void Reveal(Control control)
        {
            if (!paged || pageView >= PageNow || Control.MouseButtons != MouseButtons.None) return;
            float s = Unit();
            int edge = Scaled(8);
            // Near the top it goes all the way up, so the header shows too.
            int up = pageScroll - (int)Math.Ceiling((edge - control.Top) / s);
            if (control.Top < edge) ScrollPage(up < 2 * ScrollRow ? 0 : up);
            else if (control.Bottom > ClientSize.Height - edge) ScrollPage(pageScroll + (int)Math.Ceiling((control.Bottom - ClientSize.Height + edge) / s));
        }

        // Moves the page and its header up by `to` px, as far as the page runs past the screen. Off Settings it's always 0.
        private void ScrollPage(int to)
        {
            glide.Stop();
            MovePage(to);
        }

        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr batch, IntPtr window, IntPtr after, int x, int y, int width, int height, int flags);
        [DllImport("user32.dll")] private static extern bool EndDeferWindowPos(IntPtr batch);

        private void MovePage(int to)
        {
            DismissHint();
            int next = paged ? Math.Max(0, Math.Min(PageNow - pageView, to)) : 0;
            if (next == pageScroll) return;
            if (pageScroll == 0) foreach (Control control in scrolled) tops[control] = control.Top;
            pageScroll = next;
            int shift = Scaled(next);
            // Every control moves at once, in one batch Windows applies together, and the page paints once after.
            // Moving them one by one sent Windows a few hundred moves for every turn of the wheel.
            Native.SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            SuspendLayout();
            try
            {
                List<Control> batched = scrolled.FindAll(control => control.IsHandleCreated && control.Parent == this);
                IntPtr batch = batched.Count > 0 ? BeginDeferWindowPos(batched.Count) : IntPtr.Zero;
                foreach (Control control in batched)
                {
                    if (batch == IntPtr.Zero) break;
                    batch = DeferWindowPos(batch, control.Handle, IntPtr.Zero, control.Left, tops[control] - shift, 0, 0, 0x0001 | 0x0004 | 0x0008 | 0x0010);
                }
                bool moved = batch != IntPtr.Zero && EndDeferWindowPos(batch);
                foreach (Control control in scrolled) if (!moved || !batched.Contains(control)) control.Top = tops[control] - shift;
            }
            finally { ResumeLayout(false); Native.SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero); }
            Invalidate(true);
            Update();
        }

        private void Glide()
        {
            if (IsDisposed || !paged) { glide.Stop(); return; }
            double t = Math.Min(1, glideClock.Elapsed.TotalMilliseconds / GlideMs);
            double eased = 1 - Math.Pow(1 - t, 3);
            MovePage((int)Math.Round(glideFrom + (glideTo - glideFrom) * eased));
            if (t >= 1) glide.Stop();
        }

        private void Heartbeat()
        {
            if (IsDisposed || support == null) { heartbeat.Stop(); return; }
            float swell = 1, beat = 0;
            if (capri.Moves && WindowState != FormWindowState.Minimized && !Hidden(support)) Pulse(beatClock.Elapsed.TotalMilliseconds, out swell, out beat);
            if (swell == support.Swell && beat == support.Beat) return;
            support.Swell = swell;
            support.Beat = beat;
            support.Invalidate();
        }

        // Where Support is in its beat: two quick swells of the heart, the rim lit across both, then rest.
        private static void Pulse(double ms, out float swell, out float beat)
        {
            double t = ms % BeatMs / BeatMs;
            swell = (float)Between(t, new[] { 0, .04, .08, .12, .16 }, new[] { 1, 1.22, 1, 1.12, 1 });
            beat = (float)Between(t, new[] { 0, .05, .13, .22 }, new[] { 0, 1, 1, 0.0 });
        }

        private static double Between(double t, double[] at, double[] value)
        {
            for (int i = 1; i < at.Length; i++) if (t < at[i]) return value[i - 1] + (value[i] - value[i - 1]) * (t - at[i - 1]) / (at[i] - at[i - 1]);
            return value[value.Length - 1];
        }

        // Ends a glide where it was headed, for checks that read where the page is.
        internal void Settle()
        {
            if (!glide.Enabled) return;
            glide.Stop();
            MovePage(glideTo);
        }

        // How long one frame of scrolling takes here, painting included, in ms. It leaves the page where it was.
        internal double FrameCost()
        {
            Settle();
            int start = pageScroll;
            int step = start + ScrollRow <= PageNow - pageView ? ScrollRow : -ScrollRow;
            System.Diagnostics.Stopwatch took = System.Diagnostics.Stopwatch.StartNew();
            for (int frame = 1; frame <= 20; frame++) MovePage(frame % 2 == 1 ? start + step : start);
            return took.Elapsed.TotalMilliseconds / 20;
        }

        // How much of the settings page the screen's working area has room for, before scaling.
        private int Fit()
        {
            int room = ShortScreen > 0 ? ShortScreen : (int)(Screen.FromControl(this).WorkingArea.Height / Unit());
            return Math.Max(4 * ScrollRow, Math.Min(Math.Min(MostView, PageNow), room));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            ScrollWheel(e.Delta);
        }

        private void ScrollWheel(int delta)
        {
            DismissHint();
            if (!paged || pageView >= PageNow) return;
            // A notch is a row, and a touchpad's small turns move the page by as much as they turn, so it follows the finger.
            wheel += delta * ScrollRow;
            int by = wheel / 120;
            wheel -= by * 120;
            if (by == 0) return;
            int from = glide.Enabled ? glideTo : pageScroll;
            int to = Math.Max(0, Math.Min(PageNow - pageView, from - by));
            if (to == from) return;
            glideFrom = pageScroll;
            glideTo = to;
            glideClock.Restart();
            glide.Start();
            Glide();
        }

        private void Reachable(Control control, bool on)
        {
            if (!stops.ContainsKey(control)) stops[control] = control.TabStop;
            control.TabStop = on && stops[control];
            foreach (Control child in control.Controls) Reachable(child, on);
        }

        private bool Hidden(Control control)
        {
            List<KeyValuePair<Control, int>> hidden = paged || modded ? mainLeft : pageLeft;
            for (Control top = control; top != null && top != this; top = top.Parent)
            {
                if (Folded(top)) return true;
                foreach (KeyValuePair<Control, int> entry in hidden) if (entry.Key == top) return true; foreach (KeyValuePair<Control, int> entry in modded ? pageLeft : modLeft) if (entry.Key == top) return true;
            }
            return false;
        }

        internal string TabWalk()
        {
            Control from = more;
            int reached = 0;
            for (int i = 0; i < 80; i++)
            {
                if (!SelectNextControl(from, true, true, true, true)) break;
                Control at = ActiveControl;
                if (at == null || at == from || at == more) break;
                if (Hidden(at)) return "Tab reached a hidden " + (at.AccessibleName ?? at.Text);
                reached++;
                from = at;
            }
            more.Focus();
            return reached == 0 ? "Tab reached nothing" : null;
        }

        private T Lower<T>(T control) where T : Control
        {
            lower.Add(new KeyValuePair<Control, int>(control, control.Top));
            return control;
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.Style = (value.Style | 0x00020000 | 0x00080000) & ~0x00010000;
                // Windows paints the page and every control together, so scrolling never shows them half moved.
                value.ExStyle |= 0x02000000;
                return value;
            }
        }

        private int wndDepth;
        private string outerMessage;

        protected override void WndProc(ref Message message)
        {
            // The message a nested restore started from, for RestoredBy in smoke runs.
            if (wndDepth == 0 && SmokeRun.Worker) outerMessage = "0x" + message.Msg.ToString("X4") + " " + message.WParam.ToInt64().ToString("X") + (Native.InSendMessage() ? " sent" : " posted");
            wndDepth++;
            try { HandleMessage(ref message); }
            finally { wndDepth--; }
        }

        private void HandleMessage(ref Message message)
        {
            if (message.Msg == 0x007E || message.Msg == 0x02E0) Panels.Forget();
            if (message.Msg == 0x02E0 && loaded)
            {
                monitorDpi = (int)(message.WParam.ToInt64() & 0xFFFF);
                Rezoom(Wanted());
                ShowFrameSize();
                message.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            if (message.Msg == 0x001A && loaded) { Rezoom(Wanted()); ShowFrameSize(); }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            DismissHint();
            base.OnLocationChanged(e);
            if (loaded && !sizing) { Rezoom(Wanted()); ShowFrameSize(); }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            monitorDpi = (int)Math.Round(DeviceScale() * 96);
            loaded = true;
            Rezoom(Wanted());
            ShowFrameSize();
            Arrange(expanded);
        }

        // Auto's size on the screen she's on, kept for Settings to show.
        private float autoZoom = 1f;

        // Her Mainframe at the size her settings tool just picked, as its own slider does.
        internal void ResizeSettings() { Rezoom(Wanted()); ShowFrameSize(); }

        // Her Mainframe size as picked, or for Auto what her screen needs: how much bigger its desktop is than the panel showing
        // it, less what Windows' own scaling already makes up, to the nearest 5% from 100% to 200%. Zenni's Virtual Super
        // Resolution, a 3840 x 2160 desktop on a 1920 x 1080 screen at 100%, gets 200%.
        private float Wanted()
        {
            // GitHub's virtual screen says its panel is narrower than the desktop her test needs, which isn't a shrunken monitor.
            float shrunk = Companion.OnGitHub ? 1f : Panels.Shrink(Screen.FromControl(this), restoringFullScreen || capri.FullScreenDisplay);
            autoZoom = Math.Max(1f, Math.Min(2f, (float)Math.Round(shrunk / DeviceScale() * 20) / 20));
            int picked = capri.MainframeSize;
            return picked > 0 ? picked / 100f : autoZoom;
        }

        private void ShowFrameSize()
        {
            int auto = (int)Math.Round(autoZoom * 100);
            frameSizeName.Text = frameSize.Value == 0 && auto != 100 ? "Auto · " + auto + "%" : FrameSizeNames[frameSize.Value];
        }

        // Bounds and fonts start from the same 96 DPI layout every time. Pixel fonts already include the monitor and text
        // scales, so Windows must not scale either them or the controls again.
        private void Rezoom(float next)
        {
            float nextUnit = DeviceScale() * TextScale() * next;
            if (!loaded || sizing || (Math.Abs(nextUnit - unit) < 0.005f && Math.Abs(next - zoom) < 0.005f && shownFallback == layoutFallback)) return;
            DismissHint();
            languageMenu.Close();
            sizing = true;
            SuspendLayout();
            try
            {
                ScrollPage(0);
                zoom = next;
                unit = nextUnit;
                foreach (LayoutItem item in layout) item.Apply(unit, layoutFallback);
                shownFallback = layoutFallback;
                foreach (ContextMenuStrip menu in new[] { modelMenu, freeKeyMenu, cloudMenu, voiceMenu, speechModelMenu, freeMenu, micMenu, languageMenu, careMenu, hearingMenu, mixInputMenu, mixOutputMenu, callAppMenu }) menu.Font = Font;
                foreach (ToolStripMenuItem item in LanguageItems(languageMenu.Items)) if (item.HasDropDownItems) item.DropDown.Font = Font;
                if (languageFont != null) { languageFont.Dispose(); languageFont = null; }
                foreach (KeyValuePair<Control, int> entry in underVoice) entry.Key.Top = Scaled(entry.Value - lifted);
                CloseGaps();
                freeVoices.Width = Scaled(freeReady && !yoursShown ? 328 : 196);
                customVoice.Width = Scaled(freeReady ? 328 : 196);
                FitMixInput();
                Place();
                Arrange(expanded);
                foreach (Control control in Controls) control.PerformLayout();
                Shape();
                DescribeHints();
            }
            finally { ResumeLayout(true); sizing = false; }
        }

        private readonly List<LayoutItem> layout = new List<LayoutItem>();
        private bool sizing;
        private float unit;
        private int layoutDpi;
        private int layoutText;
        private int monitorDpi;
        private bool layoutFallback;
        private bool shownFallback;

        private void RememberLayout(Control control)
        {
            layout.Add(new LayoutItem(control));
            foreach (Control child in control.Controls) RememberLayout(child);
        }

        private sealed class LayoutItem : IDisposable
        {
            private readonly Control control;
            private readonly Rectangle bounds;
            private readonly Size maximum;
            private readonly Font font;
            private readonly Font hint;
            private readonly Font flag;
            private Font scaledFont, scaledHint, scaledFlag;

            internal LayoutItem(Control control)
            {
                this.control = control;
                bounds = control.Bounds;
                maximum = control.MaximumSize;
                font = control.Font;
                Rung rung = control as Rung;
                Toggle toggle = control as Toggle;
                hint = rung != null ? rung.HintFont : toggle != null ? toggle.HintFont : null;
                flag = rung != null ? rung.FlagFont : null;
            }

            internal void Apply(float scale, bool fallback)
            {
                Refont(scale, fallback);
                control.MaximumSize = new Size(At(maximum.Width, scale), At(maximum.Height, scale));
                if (!(control is Form))
                {
                    Label label = control as Label;
                    if (label != null && label.AutoSize) control.Location = new Point(At(bounds.X, scale), At(bounds.Y, scale));
                    else control.Bounds = Rectangle.FromLTRB(At(bounds.Left, scale), At(bounds.Top, scale), At(bounds.Right, scale), At(bounds.Bottom, scale));
                }
            }

            internal void Refont(float scale, bool fallback)
            {
                Font oldFont = scaledFont, oldHint = scaledHint, oldFlag = scaledFlag;
                scaledFont = Pixels(font, scale, fallback);
                scaledHint = Pixels(hint, scale, fallback);
                scaledFlag = Pixels(flag, scale, fallback);
                control.Font = scaledFont;
                Rung rung = control as Rung;
                Toggle toggle = control as Toggle;
                if (rung != null) { rung.HintFont = scaledHint; rung.FlagFont = scaledFlag; }
                if (toggle != null) toggle.HintFont = scaledHint;
                if (oldFont != null) oldFont.Dispose();
                if (oldHint != null) oldHint.Dispose();
                if (oldFlag != null) oldFlag.Dispose();
            }

            private static int At(int value, float scale) { return (int)Math.Round(value * scale); }

            private static Font Pixels(Font original, float scale, bool fallback)
            {
                if (original == null) return null;
                float points = original.SizeInPoints;
                FontStyle style = original.Style;
                if (fallback)
                {
                    bool face = original.Name.StartsWith("Bahnschrift", StringComparison.OrdinalIgnoreCase);
                    if (face) { points -= points >= 15 ? 3f : 0.5f; style = FontStyle.Regular; }
                    return Look.FallbackMono(points * 96f / 72f * scale, style, GraphicsUnit.Pixel);
                }
                return new Font(original.FontFamily, points * 96f / 72f * scale, style, GraphicsUnit.Pixel);
            }

            public void Dispose()
            {
                if (scaledFont != null) scaledFont.Dispose();
                if (scaledHint != null) scaledHint.Dispose();
                if (scaledFlag != null) scaledFlag.Dispose();
            }
        }

        private float TextScale()
        {
            if (layoutText > 0) return layoutText / 100f;
            try
            {
                object value = Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Accessibility", "TextScaleFactor", 100);
                int percent = value is int ? (int)value : 100;
                return percent >= 100 && percent <= 225 ? percent / 100f : 1f;
            }
            catch (System.Security.SecurityException) { return 1f; }
            catch (UnauthorizedAccessException) { return 1f; }
            catch (System.IO.IOException) { return 1f; }
        }

        internal void LayoutScale(int dpi, int text, bool fallback = false)
        {
            layoutDpi = dpi;
            layoutText = text;
            layoutFallback = fallback;
            Rezoom(Wanted());
            ShowFrameSize();
        }

        // Her Mainframe's zoom, and picking its size as they would, for the smoke test.
        internal float Zoom { get { return zoom; } }

        internal string FrameSizeShown { get { return frameSizeName.Text; } }

        internal void PickColours(int look) { frameColours.Slide(look); }

        internal void ShowColours()
        {
            bool was = syncing;
            syncing = true;
            frameColours.Value = capri.MainframeColours;
            syncing = was;
        }

        internal string ColoursShown { get { return frameColours.Shown; } }

        internal void SizeMainframe(int size) { frameSize.Slide(Math.Max(0, Array.IndexOf(FrameSizes, size))); }

        // Every control's own type grown or shrunk by this much, children first, so each reads its parent's before that changes.
        private static void Grow(Control control, float factor)
        {
            foreach (Control child in control.Controls) Grow(child, factor);
            control.Font = Bigger(control.Font, factor);
            Rung rung = control as Rung;
            if (rung != null) { rung.HintFont = Bigger(rung.HintFont, factor); rung.FlagFont = Bigger(rung.FlagFont, factor); }
            Toggle toggle = control as Toggle;
            if (toggle != null) toggle.HintFont = Bigger(toggle.HintFont, factor);
        }

        private void Fit(Form sheet)
        {
            new Rescaler(sheet, DeviceScale(), TextScale(), layoutFallback);
            sheet.Location = new Point(Left + (Width - sheet.Width) / 2, Top + (Height - sheet.Height) / 2);
        }

        private sealed class Rescaler : NativeWindow
        {
            [StructLayout(LayoutKind.Sequential)] private struct Edges { internal int Left, Top, Right, Bottom; }

            private readonly Form form;
            private readonly float text;
            private readonly bool fallback;
            private readonly List<LayoutItem> items = new List<LayoutItem>();
            private Size client;
            internal float Unit;

            internal Rescaler(Form form, float screen, float text, bool fallback)
            {
                this.form = form;
                this.text = text;
                this.fallback = fallback;
                Unit = screen * text;
                form.Tag = this;
                Remember(form);
                foreach (LayoutItem item in items) item.Refont(Unit, fallback);
                form.Scale(new SizeF(Unit, Unit));
                client = form.ClientSize;
                form.ResumeLayout(false);
                foreach (Control control in form.Controls) control.PerformLayout();
                if (form.IsHandleCreated) AssignHandle(form.Handle);
                form.HandleCreated += delegate { AssignHandle(form.Handle); Frame(); };
                form.HandleDestroyed += delegate { ReleaseHandle(); };
                form.Disposed += delegate { foreach (LayoutItem item in items) item.Dispose(); };
            }

            private void Remember(Control control)
            {
                items.Add(new LayoutItem(control));
                foreach (Control child in control.Controls) Remember(child);
            }

            private void Frame()
            {
                if (form.FormBorderStyle == FormBorderStyle.None || !form.IsHandleCreated) return;
                form.Size = new Size(client.Width + form.Width - form.ClientSize.Width, client.Height + form.Height - form.ClientSize.Height);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg != 0x02E0) { base.WndProc(ref message); return; }
                Edges suggested = (Edges)Marshal.PtrToStructure(message.LParam, typeof(Edges));
                float next = (message.WParam.ToInt64() & 0xFFFF) / 96f * text;
                float ratio = next / Unit;
                message.Result = IntPtr.Zero;
                if (Math.Abs(ratio - 1f) < 0.01f) return;
                Unit = next;
                form.SuspendLayout();
                foreach (LayoutItem item in items) item.Refont(Unit, fallback);
                form.Scale(new SizeF(ratio, ratio));
                client = new Size((int)Math.Round(client.Width * ratio), (int)Math.Round(client.Height * ratio));
                form.ResumeLayout(false);
                foreach (Control control in form.Controls) control.PerformLayout();
                form.Location = new Point(suggested.Left, suggested.Top);
                Frame();
            }
        }

        private static Font Bigger(Font font, float factor) { return font == null ? null : new Font(font.FontFamily, font.SizeInPoints * factor, font.Style, GraphicsUnit.Point); }

        // How much bigger a screen's desktop is than the panel showing it, from Windows' display paths: 2 for a 3840 x 2160
        // desktop that AMD's Virtual Super Resolution or NVIDIA's DSR draws onto a 1920 x 1080 screen, 1 for an ordinary one.
        // The panel's width is the smaller of the picture sent to it and the mode it prefers, which is its own.
        private static class Panels
        {
            [StructLayout(LayoutKind.Sequential)] private struct Luid { internal uint Low; internal int High; }

            [StructLayout(LayoutKind.Sequential)]
            private struct PathInfo
            {
                internal Luid SourceAdapter;
                internal uint SourceId, SourceMode, SourceFlags;
                internal Luid TargetAdapter;
                internal uint TargetId, TargetMode, Technology, Rotation, Scaling, RefreshNumerator, RefreshDenominator, ScanLines;
                internal int Available;
                internal uint TargetFlags, Flags;
            }

            // A source mode (Kind 1) is a desktop: its size and where it sits. A target mode (Kind 2) is the picture sent to a screen.
            [StructLayout(LayoutKind.Explicit, Size = 64)]
            private struct ModeInfo
            {
                [FieldOffset(0)] internal uint Kind;
                [FieldOffset(16)] internal uint Width;
                [FieldOffset(20)] internal uint Height;
                [FieldOffset(28)] internal int X;
                [FieldOffset(32)] internal int Y;
                [FieldOffset(40)] internal uint ActiveWidth;
            }

            [StructLayout(LayoutKind.Explicit, Size = 80)]
            private struct PreferredMode
            {
                [FieldOffset(0)] internal uint Type;
                [FieldOffset(4)] internal uint Size;
                [FieldOffset(8)] internal Luid Adapter;
                [FieldOffset(16)] internal uint Id;
                [FieldOffset(20)] internal uint Width;
            }

            [DllImport("user32.dll")] private static extern int GetDisplayConfigBufferSizes(uint flags, out uint paths, out uint modes);
            [DllImport("user32.dll")] private static extern int QueryDisplayConfig(uint flags, ref uint pathCount, [Out] PathInfo[] paths, ref uint modeCount, [Out] ModeInfo[] modes, IntPtr topology);
            [DllImport("user32.dll")] private static extern int DisplayConfigGetDeviceInfo(ref PreferredMode request);

            // Asking the display driver about a panel while a game runs full screen on it may upset the game, and the answer
            // only changes with the displays, so each screen is asked once until they change (john_sandevistan, 2 Nov).
            private static readonly Dictionary<string, float> asked = new Dictionary<string, float>();
            internal static Func<Screen, float> Read = Measure;

            internal static void Forget() { lock (asked) asked.Clear(); }

            internal static float Shrink(Screen screen, bool busy)
            {
                string key = screen.DeviceName + " " + screen.Bounds;
                float known;
                lock (asked) if (asked.TryGetValue(key, out known)) return known;
                // Opening Mainframe over fullscreen must not make a cold display-driver query (john_sandevistan).
                if (busy) return 1f;
                known = Read(screen);
                lock (asked) asked[key] = known;
                return known;
            }

            private static float Measure(Screen screen)
            {
                try
                {
                    uint pathCount, modeCount;
                    if (GetDisplayConfigBufferSizes(2, out pathCount, out modeCount) != 0) return 1f;
                    PathInfo[] paths = new PathInfo[pathCount];
                    ModeInfo[] modes = new ModeInfo[modeCount];
                    if (QueryDisplayConfig(2, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != 0) return 1f;
                    for (int i = 0; i < pathCount; i++)
                    {
                        PathInfo path = paths[i];
                        if (path.SourceMode >= modeCount || modes[path.SourceMode].Kind != 1) continue;
                        ModeInfo source = modes[path.SourceMode];
                        if (source.X != screen.Bounds.X || source.Y != screen.Bounds.Y) continue;
                        uint panel = path.TargetMode < modeCount && modes[path.TargetMode].Kind == 2 ? modes[path.TargetMode].ActiveWidth : 0;
                        PreferredMode preferred = new PreferredMode { Type = 3, Size = 80, Adapter = path.TargetAdapter, Id = path.TargetId };
                        if (DisplayConfigGetDeviceInfo(ref preferred) == 0 && preferred.Width > 0) panel = panel == 0 ? preferred.Width : Math.Min(panel, preferred.Width);
                        // A screen turned on its side has its desktop's height along the panel's width.
                        uint across = path.Rotation == 2 || path.Rotation == 4 ? source.Height : source.Width;
                        return panel == 0 ? 1f : Math.Max(1f, across / (float)panel);
                    }
                }
                catch (Exception) { }
                return 1f;
            }
        }

        // What last brought the Mainframe up from minimized in a smoke run, so a section that finds it up can name what did it.
        internal string RestoredBy;
        private FormWindowState sizedState;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (sizedState == FormWindowState.Minimized && WindowState != FormWindowState.Minimized && SmokeRun.Worker) RestoredBy = Callers() + " by message " + outerMessage;
            sizedState = WindowState;
            Shape();
            Invalidate(true);
        }

        private static string Callers()
        {
            List<string> found = new List<string>();
            foreach (string line in Environment.StackTrace.Split('\n'))
            {
                string frame = line.Trim();
                if (frame.StartsWith("at ", StringComparison.Ordinal)) frame = frame.Substring(3);
                int place = frame.IndexOf(" in ", StringComparison.Ordinal);
                if (place > 0) frame = frame.Substring(0, place);
                if (frame.StartsWith("CapriDesktop.", StringComparison.Ordinal) && frame.IndexOf(".OnSizeChanged", StringComparison.Ordinal) < 0 && frame.IndexOf(".Callers", StringComparison.Ordinal) < 0) found.Add(frame);
                if (found.Count == 6) break;
            }
            return string.Join(" < ", found.ToArray());
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = Unit();
            Graphics graphics = e.Graphics;
            Backdrop(graphics, this, s);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Edging(graphics, this, s);
            if (paged)
            {
                if (pageView >= PageNow) return;
                // A thin thumb on her right edge shows how much of the page is on screen, and where.
                float rail = Height - 36 * s;
                float thumb = rail * pageView / PageNow;
                float at = 18 * s + (rail - thumb) * pageScroll / (PageNow - pageView);
                using (Brush track = new SolidBrush(Dash)) graphics.FillRectangle(track, Width - 8 * s, 18 * s, 2 * s, rail);
                using (Brush lit = new SolidBrush(Cyan)) graphics.FillRectangle(lit, Width - 8 * s, at, 2 * s, thumb);
                return;
            }
            int shift = (expanded ? SetupHeight : 0) + KeyRow;
            int below = voiceFilled >= 0 ? InstallRow : 0;
            if (expanded) Dots(graphics, s, 210, filled);
            if (below > 0) Dots(graphics, s, InstallAt + 24 + shift - arrangedFold - hearingFold, voiceFilled);
        }

        // The dotted bar her brain setup and her voice downloads share.
        private static void Dots(Graphics graphics, float s, int y, float share)
        {
            float pitch = 6 * s;
            int count = (int)(328 / 6f);
            int lit = (int)Math.Round(count * share);
            using (Brush on = new SolidBrush(Cyan))
            using (Brush off = new SolidBrush(Dash))
                for (int i = 0; i < count; i++) graphics.FillRectangle(i < lit ? on : off, 28 * s + i * pitch, y * s, pitch - 2 * s, 5 * s);
        }

        // What's behind a control: her ground, the wine glow at the top of its window and any section band it crosses, all
        // measured from the window, so every control reads as cut out of one panel.
        private static void Backdrop(Graphics graphics, Control control, float s)
        {
            graphics.Clear(Ground);
            Form window = control as Form ?? control.FindForm();
            if (window == null) return;
            int x = 0, y = 0;
            for (Control at = control; at != null && at != window; at = at.Parent) { x += at.Left; y += at.Top; }
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(-x, -y);
            float glow = 210 * s;
            if (y < glow)
                using (LinearGradientBrush wine = new LinearGradientBrush(new RectangleF(0, -1, 16, glow + 2), Glow, Ground, LinearGradientMode.Vertical))
                {
                    wine.Blend = Fade;
                    graphics.FillRectangle(wine, x, y, control.Width, Math.Min(control.Height, glow - y));
                }
            foreach (Control child in window.Controls)
            {
                Head head = child as Head;
                if (head == null || head.Span <= 0 || !head.Visible || head.Left >= window.ClientSize.Width) continue;
                RectangleF band = head.Band(s);
                if (band.Bottom <= y || band.Top >= y + control.Height || band.Right <= x || band.Left >= x + control.Width) continue;
                using (Brush fill = new SolidBrush(Band)) graphics.FillRectangle(fill, band);
                using (Brush rule = new SolidBrush(Rule)) graphics.FillRectangle(rule, band.Left, band.Top, band.Width, Math.Max(1, (int)s));
                using (Brush tick = new SolidBrush(Hot)) graphics.FillRectangle(tick, band.Left, band.Top + 7 * s, 2.5f * s, band.Height - 13 * s);
            }
            graphics.Restore(state);
        }

        private static readonly Blend Fade = Neon.Fade;

        private static GraphicsPath Cut(RectangleF box, float topLeft, float topRight, float bottomRight, float bottomLeft) { return Neon.Cut(box, topLeft, topRight, bottomRight, bottomLeft); }

        // A window's edge: corners clipped big at the top left and bottom right and small at the others, a dark red line round
        // it, and hot red strokes over the two big corners. The rims draw it again over settings scrolled under her edges.
        private static void Edging(Graphics graphics, Control window, float s) { Neon.Frame(graphics, window.Size, s, 16 * s, 6 * s, 64 * s, 34 * s); }

        // Cuts a window to its clipped corners.
        private static void Clip(Form window, float s) { Neon.Clip(window, 16 * s, 6 * s); }

        private void Shape() { Clip(this, Unit()); }

        private static FontFamily Family(string name)
        {
            try { return new FontFamily(name); }
            catch (ArgumentException) { return null; }
        }

        // Her Mainframe's type: Bahnschrift's semi-condensed cut, or its semi-bold for buttons, titles and switches.
        private static Font Type(float points, bool bold = false)
        {
            FontFamily family = bold ? Heavy : Plain;
            return family != null ? new Font(family, points, FontStyle.Regular, GraphicsUnit.Point) : Look.Mono(points - 0.5f, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Point);
        }

        // A window's title, like MAINFRAME: Bahnschrift's condensed semi-bold, big.
        private static Font Display(float points)
        {
            return Narrow != null ? new Font(Narrow, points, FontStyle.Regular, GraphicsUnit.Point) : Look.Mono(points - 3f, FontStyle.Bold, GraphicsUnit.Point);
        }

        private float DeviceScale()
        {
            if (layoutDpi > 0) return layoutDpi / 96f;
            if (monitorDpi > 0) return monitorDpi / 96f;
            try
            {
                if (IsHandleCreated)
                {
                    int dpi = Native.GetDpiForWindow(Handle);
                    if (dpi > 0) return dpi / 96f;
                }
            }
            catch (EntryPointNotFoundException) { }
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) return screen.DpiX / 96f;
        }

        // Her Mainframe size on top of the screen's own scale: 1 unless a bigger size is picked, or Auto finds a desktop
        // bigger than the panel showing it.
        private float zoom = 1f;

        private float Unit() { return unit > 0 ? unit : 1f; }

        // The scale of whichever of her windows a control is on: the Mainframe, their voice server's panel or her SECURITY
        // window, which the smoke test also draws at 150% and 200%.
        private static float ScaleOf(Control control)
        {
            Form form = control.FindForm();
            if (form == null) return 1f;
            Mainframe main = form as Mainframe;
            if (main != null) return main.Unit();
            ProviderSheet provider = form as ProviderSheet;
            if (provider != null) return form.CurrentAutoScaleDimensions.Width / 96f * provider.Zoom;
            SecuritySheet guarded = form as SecuritySheet;
            LayersSheet layers = form as LayersSheet;
            MixSheet mix = form as MixSheet;
            ServerSheet server = form as ServerSheet;
            ExtrasSheet extras = form as ExtrasSheet;
            ModSheet mods = form as ModSheet;
            float zoom = guarded != null ? guarded.Zoom : layers != null ? layers.Zoom : mix != null ? mix.Zoom : server != null ? server.Zoom : extras != null ? extras.Zoom : mods != null ? mods.Zoom : 1f;
            Rescaler scaler = form.Tag as Rescaler;
            return (scaler != null ? scaler.Unit : 1f) * zoom;
        }

        private int Scaled(int value) { return loaded ? (int)Math.Round(value * Unit()) : value; }

        private void Arrange(bool setupShown)
        {
            expanded = setupShown;
            int top = setupShown ? SetupHeight : 0, shift = top + KeyRow;
            tierNote.Top = Scaled(168 + top);
            for (int i = 0; i < rungs.Length; i++) rungs[i].Top = Scaled(184 + 24 * i + top);
            freeKey.Top = Scaled(209 + top);
            textEntry.Top = Scaled(262 + top);
            textConnect.Top = textEntry.Top;
            textEntry.Visible = textConnect.Visible = tier > 0;
            freeWarning.Top = Scaled(294 + top);
            freeWarning.Visible = tier == 1;
            paidNote.Top = Scaled(315 + top);
            paidNote.MaximumSize = new Size(Scaled(328), 0);
            movePaid.Top = Scaled(350 + top);
            paidNote.Visible = movePaid.Visible = tier == 1 && paidTextKey != null;
            int below = voiceFilled >= 0 ? InstallRow : 0;
            int fold = arrangedFold = Fold;
            hearingFold = hearingActions.Length == 0 ? HearingFixRow : 0;
            foreach (KeyValuePair<Control, int> entry in lower) entry.Key.Top = Scaled(entry.Value + shift + (entry.Value >= InstallAt ? below : 0) - (entry.Value > KeyAt ? fold : 0) - (entry.Value > HearingFixAt ? hearingFold : 0));
            installNote.Top = Scaled(InstallAt + 2 + shift - fold - hearingFold);
            installNote.Visible = below > 0;
            foreach (Label step in steps) step.Visible = setupShown;
            setup.Visible = setupShown;
            refresh.Top = Scaled(setupShown ? 226 : 134);
            brainNote.MaximumSize = new Size(Scaled(328), 0);
            voiceNote.MaximumSize = new Size(Scaled(328), 0);
            updateNote.MaximumSize = new Size(Scaled(328), 0);
            if (keysNote != null) keysNote.MaximumSize = new Size(Scaled(328), 0);
            if (paged) pageView = Fit();
            ClientSize = new Size(Scaled(paged ? Wide + Column : Wide), Scaled(paged ? pageView : Tall + shift + below - fold - hearingFold));
            ScrollPage(pageScroll);
            bool scrolls = paged && pageView < PageNow;
            topRim.Visible = bottomRim.Visible = scrolls;
            if (scrolls)
            {
                topRim.Bounds = new Rectangle(Scaled(14), 0, ClientSize.Width - Scaled(28), Scaled(3));
                bottomRim.Bounds = new Rectangle(Scaled(14), ClientSize.Height - Scaled(3), ClientSize.Width - Scaled(28), Scaled(3));
            }
            Invalidate(true);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!Quitting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; WindowState = FormWindowState.Minimized; }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { glide.Stop(); glide.Dispose(); if (languageFont != null) languageFont.Dispose(); }
            if (disposing) DisposeHints();
            if (disposing) { speakerMenu.Dispose(); screenMenu.Dispose(); if (miniMenu != null) miniMenu.Dispose(); }
            if (disposing) { Application.RemoveMessageFilter(this); modelMenu.Dispose(); freeKeyMenu.Dispose(); voiceMenu.Dispose(); speechModelMenu.Dispose(); micMenu.Dispose(); languageMenu.Dispose(); freeMenu.Dispose(); careMenu.Dispose(); hearingMenu.Dispose(); mixInputMenu.Dispose(); mixOutputMenu.Dispose(); callAppMenu.Dispose(); }
            if (disposing && languageFont != null) languageFont.Dispose();
            base.Dispose(disposing);
            if (disposing) foreach (LayoutItem item in layout) item.Dispose();
        }

        private bool restoringFullScreen;

        internal void Restore()
        {
            bool busy = restoringFullScreen;
            restoringFullScreen = busy || capri.FullScreenDisplay;
            try
            {
                if (!Visible) Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                Activate();
            }
            finally { restoringFullScreen = busy; }
        }

        // Her mainframe tool: opens Mainframe at one of its pages, turning on advanced settings for a page only they show, and
        // scrolls to its heading. Nothing on it changes.
        internal bool OpenTool(string name)
        {
            string page = (name ?? "main").Trim().ToLowerInvariant();
            if (page.Length == 0) page = "main";
            if (!Regex.IsMatch(page, @"\A(main|settings|brain|voice|you|moves|keys|capri|watch|habits|games|clips|security|extras|layers|mixer|personality|memories)\z")) return false;
            bool settings = page != "main" && page != "brain" && page != "voice";
            ShowSettings(settings);
            if (settings && page != "settings" && page != "mixer" && !capri.AdvancedSettings)
            {
                capri.ToggleAdvancedSettings();
                ShowSettings(true);
            }
            Restore();
            if (page == "security") { ShowSecurity(); return true; }
            if (page == "extras") { ShowExtras(); return true; }
            if (page == "layers") { ShowLayersWindow(); return true; }
            if (page == "mixer") { OpenMix(); return true; }
            if (page == "main" || page == "settings") return true;
            string heading = page == "personality" || page == "memories" ? "BRAIN >" : page.ToUpperInvariant() + " >";
            foreach (Control control in Controls)
            {
                if (control.Text != heading || control.Left >= Far || Folded(control)) continue;
                ScrollPage(pageScroll + (int)Math.Floor((control.Top - Scaled(12)) / Unit()));
                return true;
            }
            return true;
        }

        internal void Sync()
        {
            syncing = true;
            huskMode.Text = HuskNames[capri.HuskMode];
            huskMode.Enabled = !capri.HuskChanging;
            for (int i = 0; i < huskMenu.Items.Count; i++) ((ToolStripMenuItem)huskMenu.Items[i]).Checked = i == capri.HuskMode;
            huskMode.Invalidate();
            hearingChoice.Text = HearingStatus.Choices[capri.HearingChoice];
            hearingChoice.AccessibleName = "Hearing: " + hearingChoice.Text;
            for (int i = 0; i < hearingMenu.Items.Count; i++) ((ToolStripMenuItem)hearingMenu.Items[i]).Checked = i == capri.HearingChoice;
            showHide.Text = capri.IsHidden ? "Show Capri" : "Hide Capri";
            perch.Text = capri.Riding ? "Hop off" : "Ride cursor";
            int near = 0;
            for (int i = 0; i < SizeSteps.Length; i++) if (Math.Abs(SizeSteps[i] - capri.CharacterSize) < Math.Abs(SizeSteps[near] - capri.CharacterSize)) near = i;
            sizeSlider.Value = near;
            sizeName.Text = SizeNames[near];
            checkIns.Checked = capri.CheckIns;
            onTop.Checked = capri.OnTop;
            startup.Checked = capri.StartsWithWindows;
            discord.Checked = capri.OnDiscord;
            cursor.Checked = capri.UsesCursor;
            gamingMode.Checked = capri.GamingWanted;
            security.Text = "Security: " + Security.PresetName;
            blackwallNote.Text = Security.Short;
            if (guard != null && !guard.IsDisposed) guard.Sync();
            if (extras != null && !extras.IsDisposed) extras.Sync();
            watch.Checked = capri.Watching;
            ShowWatchScreen(capri.WatchScreenName);
            caughtUp.Checked = capri.CaughtUp;
            followUps.Checked = capri.FollowUps;
            watchMemory.Value = Math.Max(0, Array.IndexOf(WatchLines, capri.WatchMemoryLines));
            watchMemoryName.Text = WatchLineNames[watchMemory.Value];
            stayOnModel.Checked = capri.StayOnModel;
            remembering.Checked = capri.Remembers;
            keepAwake.Checked = capri.KeepAwake;
            hideFullScreen.Checked = capri.HideFullScreen;
            playerFullScreen.Checked = capri.PlayerFullScreen;
            exploreAway.Checked = capri.ExploreAway;
            exploreAfter.Value = Math.Max(0, Array.IndexOf(AwayTimes, capri.ExploreAfter));
            exploreAfterName.Text = AwayNames[exploreAfter.Value];
            for (int careSync = 0; careSync < careToggles.Length; careSync++)
            {
                CareKind careSetting = (CareKind)careSync;
                careToggles[careSync].Checked = capri.CareOn(careSetting);
                careToggles[careSync].Dimmed = !capri.CheckIns;
                careTimes[careSync].Text = CareTimeName(careSetting, capri.CareTime(careSetting));
                careTimes[careSync].Dimmed = !capri.CheckIns;
                careTimes[careSync].AccessibleName = "Check in on me: " + Care.Names[careSync] + (careSetting == CareKind.Late ? " after " : " every ") + careTimes[careSync].Text;
            }
            careEdit.Dimmed = !capri.CheckIns;
            ShowPersonality();
            liveReactions.Checked = capri.ReactionsOn;
            clipGames.Checked = capri.ClipsOn;
            clipMoments.Checked = capri.ClipMomentsOn;
            ClipSettings recording = capri.ClipOptions;
            clipLength.Value = Array.IndexOf(ClipSettings.Lengths, recording.Seconds);
            clipRate.Value = Array.IndexOf(ClipSettings.Rates, recording.Fps);
            clipSize.Value = Array.IndexOf(ClipSettings.Sizes, recording.Resolution);
            clipBits.Value = Array.IndexOf(ClipSettings.Bitrates, recording.Mbps);
            clipPath.Text = recording.Folder;
            clipPath.AccessibleName = "Clips saved in " + recording.Folder;
            fastHands.Checked = capri.FastHandsOn;
            gameMute.Checked = capri.Shushed;
            frameSize.Value = Math.Max(0, Array.IndexOf(FrameSizes, capri.MainframeSize));
            ShowFrameSize();
            frameColours.Value = capri.MainframeColours;
            VoiceTier();
            int chosen = Array.IndexOf(Gaps, capri.GlanceGap);
            gap.Value = chosen < 0 ? 5 : chosen;
            gapName.Text = GapNames[gap.Value];
            if (!nickname.Box.Focused) nickname.Box.Text = capri.Nickname;
            SyncLanguages();
            int loud = 0;
            for (int i = 0; i < Volumes.Length; i++) if (Math.Abs(Volumes[i] - capri.Volume) < Math.Abs(Volumes[loud] - capri.Volume)) loud = i;
            volume.Value = loud;
            volumeName.Text = VolumeNames[loud];
            pitchSlider.Value = capri.VoicePitch + VoiceFx.MostPitch;
            pitchName.Text = PitchNames[pitchSlider.Value];
            ShowLayers();
            int pace = 0;
            for (int i = 0; i < Paces.Length; i++) if (Math.Abs(Paces[i] - capri.VoiceSpeed) < Math.Abs(Paces[pace] - capri.VoiceSpeed)) pace = i;
            paceSlider.Value = pace;
            paceName.Text = PaceNames[pace];
            sounds.Value = Math.Max(0, Math.Min(10, (capri.SoundLevel + 5) / 10));
            soundsName.Text = SoundNames[sounds.Value];
            sleepAfter.Value = Math.Max(0, Array.IndexOf(Sleeps, capri.SleepAfter));
            sleepName.Text = SleepNames[sleepAfter.Value];
            wander.Value = capri.WanderLevel;
            wanderName.Text = WanderNames[wander.Value];
            pops.Value = Math.Max(0, Math.Min(PopNames.Length - 1, capri.PopPace));
            popsName.Text = capri.PopRange ? capri.PopMin + " to " + capri.PopMax + " min" : PopNames[pops.Value];
            dance.Checked = capri.Dancing;
            headPats.Checked = capri.HeadPats;
            upsetChaos.Checked = capri.UpsetChaos;
            hologram.Checked = capri.HologramOn;
            bubble.Value = capri.BubbleTime;
            bubbleName.Text = BubbleNames[bubble.Value];
            comments.Value = capri.CommentPace;
            commentsName.Text = Commentary.PaceNames[comments.Value];
            intro.Checked = capri.Intro;
            typing.Checked = capri.TypingSound;
            listenCapri.Checked = capri.ListenCapriOn;
            ShowHdGames();
            ShowCallApp();
            ShowVoiceInMic();
            miniPlayer.Checked = capri.PlayerOn;
            replies.Value = capri.Wordiness;
            repliesName.Text = Lengths[replies.Value];
            recall.Value = Math.Max(0, Array.IndexOf(Recalls, capri.Recall));
            recallName.Text = Lengths[recall.Value];
            memorySize.Value = Math.Max(0, Array.IndexOf(Brain.MemorySizes, capri.MemorySize));
            memorySizeName.Text = MemoryNames[memorySize.Value];
            replyLength.Value = capri.ReplyLength;
            replyLengthName.Text = ReplyNames[replyLength.Value];
            if (!localAddress.Box.Focused) localAddress.Box.Text = capri.LocalAddress;
            perchFps.Value = Math.Max(0, Array.IndexOf(Companion.PerchRates, capri.PerchFps));
            ShowPerch();
            syncing = false;
        }

        // Past 30 fps she draws her perch more often, so the HABITS line says it costs more.
        private void ShowPerch()
        {
            perchFpsName.Text = PerchNames[perchFps.Value];
            perchNote.Text = Companion.PerchRates[perchFps.Value] > 30 ? "Perch over 30 fps uses more CPU" : "";
        }

        internal string PerchStatus { get { return perchFpsName.Text + " | " + (perchNote.Text.Length > 0 ? perchNote.Text : "-"); } }

        internal string BrainSettings { get { return memorySizeName.Text + " | " + replyLengthName.Text; } }

        internal void SlideMemorySize(int index) { memorySize.Slide(index); }

        internal void SlideReplyLength(int index) { replyLength.Slide(index); }

        internal void SlidePerch(int steps) { Press(perchFps, steps); }

        internal string PopsShown { get { return popsName.Text; } }

        internal void SlidePops(int steps) { Press(pops, steps); }

        // The HABITS row and KEYS fit inside the settings page.
        internal bool PerchFits { get { Rectangle page = PageArea; return page.Contains(perchFpsName.Bounds) && page.Contains(perchFps.Bounds) && page.Contains(hideKeys.Bounds) && page.Contains(quietKeys.Bounds); } }

        internal string Status { get { return brainName.Text + " | " + brainNote.Text; } }

        internal bool WatchShown { get { return watch.Checked; } }

        internal bool CaughtUpShown { get { return caughtUp.Checked; } }
        internal bool FollowUpsShown { get { return followUps.Checked; } }
        internal string HuskShown { get { return huskMode.Text; } }
        internal bool HuskPicker { get { return huskMode.Chevron && huskMenu.Items.Count == 4 && huskMenu.Items[0].Text == "Off" && huskMenu.Items[1].Text == "Claude Code" && huskMenu.Items[2].Text == "Codex" && huskMenu.Items[3].Text == "Both"; } }
        internal int WatchMemoryShown { get { return WatchLines[watchMemory.Value]; } }
        internal void FlipFollowUps() { followUps.Checked = !followUps.Checked; }

        internal void FlipCaughtUp() { caughtUp.Checked = !caughtUp.Checked; }

        internal bool StayShown { get { return stayOnModel.Checked; } }

        internal void FlipStay() { stayOnModel.Checked = !stayOnModel.Checked; }

        internal bool KeepAwakeShown { get { return keepAwake.Checked; } }
        internal void FlipKeepAwake() { keepAwake.Checked = !keepAwake.Checked; }
        internal string DesktopSettingsStatus { get { return hideFullScreen.Checked + " | " + playerFullScreen.Checked + " | " + exploreAway.Checked + " | " + exploreAfterName.Text; } }
        internal void FlipHideFullScreen() { hideFullScreen.Checked = !hideFullScreen.Checked; }
        internal void FlipPlayerFullScreen() { playerFullScreen.Checked = !playerFullScreen.Checked; }
        internal void FlipExploreAway() { exploreAway.Checked = !exploreAway.Checked; }
        internal void SlideExploreAfter(int steps) { Press(exploreAfter, steps); }
        internal bool DesktopSettingsFit
        {
            get
            {
                foreach (Toggle desktopToggle in new[] { keepAwake, hideFullScreen, playerFullScreen, exploreAway })
                    if (TextRenderer.MeasureText(desktopToggle.Text, desktopToggle.Font, Size.Empty, TextFormatFlags.NoPadding).Width > desktopToggle.NameRoom || !PageArea.Contains(desktopToggle.Bounds)) return false;
                return keepAwake.Top >= perchNote.Bottom && keepAwake.Bottom <= hideFullScreen.Top && !string.IsNullOrEmpty(keepAwake.AccessibleName)
                    && PageArea.Contains(exploreAfter.Bounds) && PageArea.Contains(exploreAfterName.Bounds);
            }
        }

        internal bool RemembersShown { get { return remembering.Checked; } }

        internal void FlipRemembering() { remembering.Checked = !remembering.Checked; }

        internal bool CareShown(CareKind kind) { return careToggles[(int)kind].Checked; }

        internal string CareTimeShown(CareKind kind) { return careTimes[(int)kind].Text; }

        internal void FlipCare(CareKind kind) { careToggles[(int)kind].Checked = !careToggles[(int)kind].Checked; }

        internal void FlipCheckIns() { checkIns.Checked = !checkIns.Checked; }

        internal string CareFits()
        {
            if (!paged) return null;
            if (Hidden(checkIns) || !checkIns.TabStop) return "Check in on me is hidden";
            Control above = checkIns;
            for (int row = 0; row < careToggles.Length; row++)
            {
                Toggle toggle = careToggles[row];
                Key time = careTimes[row];
                if (Hidden(toggle) != simple || Hidden(time) != simple || toggle.TabStop == simple || time.TabStop == simple) return "a care row has the wrong Settings view";
                if (!toggle.Enabled || !time.Enabled || toggle.Dimmed != !capri.CheckIns || time.Dimmed != !capri.CheckIns) return "a care row is locked or has the wrong dimming";
                if (!(toggle.AccessibleName ?? "").StartsWith("Check in on me: ", StringComparison.Ordinal) || !(time.AccessibleName ?? "").StartsWith("Check in on me: ", StringComparison.Ordinal)) return "a care row has lost its parent name";
                if (!simple && (Math.Abs(toggle.Top - above.Top - Scaled(Row)) > 1 || Math.Abs(toggle.Top - time.Top) > 1 || toggle.Left != checkIns.Left || toggle.Right > time.Left)) return "a care row is out of place";
                above = toggle;
            }
            if (Hidden(careEdit) != simple || careEdit.TabStop == simple || !careEdit.Enabled || careEdit.Dimmed != !capri.CheckIns) return "Edit check-in lines has the wrong view or dimming";
            if (!(careEdit.AccessibleName ?? "").StartsWith("Check in on me: ", StringComparison.Ordinal)) return "Edit check-in lines has lost its parent name";
            if (!simple && (careEdit.Top < above.Bottom || careEdit.Bottom > onTop.Top || careEdit.Left != checkIns.Left)) return "Edit check-in lines is out of place";
            if (simple && Math.Abs(onTop.Top - checkIns.Top - Scaled(Row)) > 1) return "Simple Settings left a gap under Check in on me";
            string tip = checkIns.AccessibleDescription ?? "";
            foreach (string kind in new[] { "visits", "attention", "cheers", "busy-PC", "water", "stretch", "look away", "sleep", "meals", "game breaks", "boredom" })
                if (!tip.Contains(kind)) return "Check in on me's tip missed " + kind;
            return null;
        }

        internal long CareLight()
        {
            List<Control> rows = new List<Control>();
            rows.AddRange(careToggles);
            rows.AddRange(careTimes);
            rows.Add(careEdit);
            long light = 0;
            foreach (Control row in rows)
                using (Bitmap paper = new Bitmap(row.Width, row.Height))
                {
                    row.DrawToBitmap(paper, row.ClientRectangle);
                    for (int y = 0; y < paper.Height; y++)
                        for (int x = 0; x < paper.Width; x++)
                        {
                            Color ink = paper.GetPixel(x, y);
                            light += ink.R + ink.G + ink.B;
                        }
                }
            return light;
        }

        internal void PickCareTime(CareKind kind, int time)
        {
            CareChoices(kind);
            foreach (ToolStripMenuItem carePick in careMenu.Items)
                if (carePick.Text == CareTimeName(kind, time)) { carePick.PerformClick(); break; }
            careMenu.Close();
        }
        internal bool ClipsShown { get { return clipGames.Checked; } }

        internal void FlipClips() { clipGames.Checked = !clipGames.Checked; }

        internal bool ReactionsShown { get { return liveReactions.Checked; } }

        internal void FlipReactions() { liveReactions.Checked = !liveReactions.Checked; }

        internal bool ClipMomentsShown { get { return clipMoments.Checked; } }

        internal bool ClipsFolderShown { get { return clipsFolder != null && clipsFolder.Parent != null; } }

        internal bool ClipsSectionShown { get { return paged && !Folded(clipHead) && !Folded(clipChoose) && clipHead.Left < Far; } }
        internal bool SimpleGameControls { get { return !simple || (!Folded(gameMute) && Folded(fastHands)); } }

        internal void CheckDisplayDeferral(Action<bool> fullscreen)
        {
            Func<Screen, float> readWas = Panels.Read;
            int queries = 0;
            FormWindowState state = WindowState;
            bool restored = false;
            Screen screen = Screen.FromControl(this);
            EventHandler focused = delegate
            {
                if (WindowState != FormWindowState.Normal) return;
                fullscreen(false);
                Wanted();
                if (!restoringFullScreen || Panels.Shrink(screen, restoringFullScreen || capri.FullScreenDisplay) != 1f || queries != 1)
                    throw new Exception("Restoring Mainframe forgot the fullscreen game when focus changed.");
                restored = true;
            };
            try
            {
                Panels.Forget();
                Panels.Read = delegate { queries++; return 1.5f; };
                if (Panels.Shrink(screen, true) != 1f || queries != 0) throw new Exception("Fullscreen Mainframe queried a cold display driver.");
                if (Panels.Shrink(screen, false) != 1.5f || queries != 1 || Panels.Shrink(screen, true) != 1.5f || queries != 1) throw new Exception("Mainframe did not reuse its cached panel scale.");
                Panels.Forget();
                if (Panels.Shrink(screen, true) != 1f || queries != 1) throw new Exception("A display change queried the driver while fullscreen.");
                fullscreen(true);
                WindowState = FormWindowState.Minimized;
                Resize += focused;
                Restore();
                Resize -= focused;
                if (!restored || restoringFullScreen) throw new Exception("Mainframe did not hold and release its fullscreen restore guard.");
                if (Panels.Shrink(screen, capri.FullScreenDisplay) != 1.5f || queries != 2) throw new Exception("Mainframe did not resume panel queries after restoring.");
            }
            finally { Resize -= focused; fullscreen(false); WindowState = state; Panels.Read = readWas; Panels.Forget(); }
        }

        internal void PickClipOptions(int seconds, int fps, int resolution, int mbps)
        {
            clipLength.Slide(Array.IndexOf(ClipSettings.Lengths, seconds));
            clipRate.Slide(Array.IndexOf(ClipSettings.Rates, fps));
            clipSize.Slide(Array.IndexOf(ClipSettings.Sizes, resolution));
            clipBits.Slide(Array.IndexOf(ClipSettings.Bitrates, mbps));
        }

        internal void ShowClipSection() { ScrollPage(pageScroll + (int)Math.Floor((clipHead.Top - Scaled(12)) / Unit())); }

        internal void FlipClipMoments() { clipMoments.Checked = !clipMoments.Checked; }

        internal bool ElevenShown { get { return voiceRow == 3 && cloudName == Eleven && voiceRungs[3].Checked && !voiceRungs[0].Checked && !voiceRungs[1].Checked && !voiceRungs[2].Checked; } }

        // The VOICE row picked, what its two rows show and how far the rest folds up, for the smoke test.
        internal string VoiceTierStatus
        {
            get
            {
                string[] names = { "OFF", "PREBUILT LOCAL", "CUSTOM LOCAL", "CLOUD VOICE" };
                bool elevenLabs = voiceRow == 3 && cloudName == Eleven;
                return names[voiceRow] + " | " + (voiceRow != 3 ? "-" : elevenLabs ? "key" : cloudName) + " | " + (voiceRow == 1 ? "free voice" : voiceRow == 2 ? "voice model" : elevenLabs && keySaved ? "voice id" : "-") + " | fold " + arrangedFold;
            }
        }

        internal string Overlap()
        {
            for (int i = 0; i < Controls.Count; i++)
                for (int j = i + 1; j < Controls.Count; j++)
                {
                    Control a = Controls[i], b = Controls[j];
                    if (Hidden(a) || Hidden(b)) continue;
                    if (!a.Visible || !b.Visible || (a is Label && a.Text.Length == 0) || (b is Label && b.Text.Length == 0)) continue;
                    if (a.Bounds.IntersectsWith(b.Bounds)) return a.Text + " / " + b.Text;
                }
            return null;
        }

        internal string TextFits()
        {
            using (Bitmap paper = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(paper)) return TextFits(graphics);
        }

        private string TextFits(Graphics graphics)
        {
            List<KeyValuePair<string, Rectangle>> text = new List<KeyValuePair<string, Rectangle>>();
            foreach (Control control in Controls)
            {
                if (Hidden(control)) continue;
                string problem = CheckText(graphics, control, Point.Empty, text, control.Visible);
                if (problem != null) return problem;
            }
            for (int first = 0; first < text.Count; first++)
                for (int second = first + 1; second < text.Count; second++)
                    if (text[first].Value.IntersectsWith(text[second].Value)) return text[first].Key + " overlaps " + text[second].Key;
            return null;
        }

        private static string CheckText(Graphics graphics, Control control, Point origin, List<KeyValuePair<string, Rectangle>> text, bool shown)
        {
            Point at = new Point(origin.X + control.Left, origin.Y + control.Top);
            string name = control.GetType().Name + " at " + at.X + "," + at.Y;
            List<KeyValuePair<string, Rectangle>> visible = shown ? text : null;
            TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding;
            Rectangle area = control.ClientRectangle;
            float scale = ScaleOf(control);
            string problem = null;
            Entry entry = control as Entry;
            Rung rung = control as Rung;
            Toggle toggle = control as Toggle;
            Slider slider = control as Slider;
            Key key = control as Key;
            Label label = control as Label;
            TextBox box = control as TextBox;
            if (entry != null)
            {
                problem = FitsText(graphics, entry.Cue, entry.Box.Font, entry.Box.Bounds, flags, at, name + " cue", visible);
            }
            else if (box != null)
            {
                Size line = TextRenderer.MeasureText(graphics, "Mg", box.Font, Size.Empty, flags);
                if (line.Height > box.Height || !box.Parent.ClientRectangle.Contains(box.Bounds)) problem = name + " clips the input line";
            }
            else if (rung != null)
            {
                int left = (int)(24 * scale);
                problem = FitsText(graphics, rung.Text, rung.Font, new Rectangle(left, 0, rung.Width - left, rung.Height), flags, at, name + " name", visible);
                int rungAfter = left + TextRenderer.MeasureText(graphics, rung.Text, rung.Font, Size.Empty, flags).Width + (int)(8 * scale);
                if (problem == null) problem = FitsText(graphics, rung.Hint, rung.HintFont ?? rung.Font, new Rectangle(rungAfter, 0, Math.Max(0, rung.Width - rungAfter), rung.Height), flags, at, name + " hint", visible);
                if (problem == null) problem = FitsText(graphics, rung.Flag, rung.FlagFont ?? rung.Font, new Rectangle(0, 0, rung.Width - (int)(6 * scale), rung.Height), flags | TextFormatFlags.Right, at, name + " flag", visible);
            }
            else if (toggle != null)
            {
                problem = FitsText(graphics, toggle.Text, toggle.Font, new Rectangle(0, 0, toggle.NameRoom, toggle.Height), flags, at, name + " name", visible);
                float line = Math.Max(1f, scale), wide = toggle.Switch * scale, gap = 4 * scale, half = (wide - gap) / 2, start = toggle.Width - wide;
                using (Font small = toggle.SmallFont())
                {
                    if (problem == null) problem = FitsText(graphics, "OFF", small, Rectangle.Round(new RectangleF(start + line / 2, line / 2, half - line, toggle.Height - line)), flags | TextFormatFlags.HorizontalCenter, at, name + " off", visible);
                    if (problem == null) problem = FitsText(graphics, "ON", small, Rectangle.Round(new RectangleF(start + half + gap + line / 2, line / 2, half - line, toggle.Height - line)), flags | TextFormatFlags.HorizontalCenter, at, name + " on", visible);
                }
                if (toggle.Hint != null && problem == null)
                {
                    int toggleAfter = TextRenderer.MeasureText(graphics, toggle.Text, toggle.Font, Size.Empty, flags).Width + (int)(10 * scale);
                    Font hintFont = toggle.HintFont ?? toggle.Font;
                    if (toggleAfter + TextRenderer.MeasureText(graphics, toggle.Hint, hintFont, Size.Empty, flags).Width <= toggle.NameRoom)
                        problem = FitsText(graphics, toggle.Hint, hintFont, new Rectangle(toggleAfter, 0, toggle.NameRoom - toggleAfter, toggle.Height), flags, at, name + " hint", visible);
                }
            }
            else if (slider != null)
            {
                int reach = slider.Stepper ? (int)(26 * scale) : 0;
                area = new Rectangle(reach, 0, slider.Width - 2 * reach, slider.Stepper ? (int)(slider.Height - 4 * scale) : slider.Height);
                problem = FitsText(graphics, slider.Shown, slider.Font, area, flags | TextFormatFlags.HorizontalCenter, at, name, visible);
                foreach (string choice in slider.Choices)
                {
                    if (problem != null) break;
                    problem = FitsText(graphics, choice, slider.Font, area, flags, at, name + " choice", null);
                }
            }
            else if (key != null)
            {
                if (key.Ellipsis) flags |= TextFormatFlags.EndEllipsis;
                if (key.Chevron) area = new Rectangle((int)(10 * scale), 0, key.Width - (int)(32 * scale), key.Height);
                problem = FitsText(graphics, key.Text, key.Font, area, flags | (key.Chevron ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter), at, name, visible);
            }
            else if (label != null)
            {
                Head head = label as Head;
                string words = head != null ? head.Shown : label.Text;
                TextFormatFlags labelFlags = TextFormatFlags.NoPrefix | (head != null ? TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter : TextFormatFlags.WordBreak);
                if (label.AutoEllipsis) labelFlags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
                if (label.TextAlign == ContentAlignment.MiddleRight) labelFlags |= TextFormatFlags.Right | TextFormatFlags.VerticalCenter;
                problem = FitsText(graphics, words, label.Font, area, labelFlags, at, name, visible);
            }
            if (problem != null) return problem;
            foreach (Control child in control.Controls)
            {
                problem = CheckText(graphics, child, at, text, shown && child.Visible);
                if (problem != null) return problem;
            }
            return null;
        }

        private static string FitsText(Graphics graphics, string words, Font font, Rectangle area, TextFormatFlags flags, Point origin, string name, List<KeyValuePair<string, Rectangle>> text)
        {
            if (string.IsNullOrEmpty(words)) return null;
            Size proposed = (flags & (TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis)) != 0 ? new Size(Math.Max(1, area.Width), int.MaxValue) : Size.Empty;
            Size measured = TextRenderer.MeasureText(graphics, words, font, proposed, flags);
            if ((flags & TextFormatFlags.EndEllipsis) != 0) measured.Width = Math.Min(measured.Width, area.Width);
            if (measured.Width > area.Width || measured.Height > area.Height) return name + " needs " + measured.Width + "x" + measured.Height + " in " + area.Width + "x" + area.Height;
            int x = area.X, y = area.Y;
            if ((flags & TextFormatFlags.Right) != 0) x += area.Width - measured.Width;
            else if ((flags & TextFormatFlags.HorizontalCenter) != 0) x += (area.Width - measured.Width) / 2;
            if ((flags & TextFormatFlags.VerticalCenter) != 0) y += (area.Height - measured.Height) / 2;
            if (text != null) text.Add(new KeyValuePair<string, Rectangle>(name, new Rectangle(origin.X + x, origin.Y + y, measured.Width, measured.Height)));
            return null;
        }

        // A settings control that pokes out past the page's edge, or null.
        internal string Outside()
        {
            Rectangle page = PageArea;
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                Control control = entry.Key;
                if (Folded(control) || !control.Visible || (control is Label && control.Text.Length == 0)) continue;
                if (!page.Contains(control.Bounds)) return control.Text.Length > 0 ? control.Text : control.AccessibleName;
            }
            return null;
        }

        // The whole settings page, scrolled or not, in the window's own px.
        private Rectangle PageArea { get { return paged ? new Rectangle(0, -Scaled(pageScroll), Math.Max(ClientSize.Width, Scaled(Wide + Column)), Math.Max(ClientSize.Height, Scaled(PageNow))) : ClientRectangle; } }

        // True when Windows made the window narrower than the page it lays out, like a 1024-wide desktop at 150%.
        internal bool PageClamped { get { return ClientSize.Width < Scaled(paged ? Wide + Column : Wide); } }

        // Settings on a short screen: how much shows (or whole) and whether the rims hold her frame together.
        internal string ScrollStatus { get { return (paged && pageView < PageNow ? pageView + " tall" : "whole") + " | " + (topRim.Visible && bottomRim.Visible ? "rims" : "-"); } }

        internal int Scrolled { get { return pageScroll; } }

        internal bool Gliding { get { return glide.Enabled; } }

        internal int ScrollMost { get { return Math.Max(0, PageNow - pageView); } }

        internal int BackTop { get { return more.Top; } }

        internal bool HideKeyShows { get { return ClientRectangle.Contains(hideKeys.Bounds) && ClientRectangle.Contains(quietKeys.Bounds); } }

        internal int Px(int value) { return Scaled(value); }

        // DrawToBitmap paints child controls bottom to top, so a screenshot draws the rims again over what's under them, as the screen does.
        internal void DrawRims(Bitmap panel)
        {
            foreach (Rim rim in new[] { topRim, bottomRim })
            {
                if (!rim.Visible) continue;
                using (Bitmap strip = new Bitmap(rim.Width, rim.Height))
                using (Graphics graphics = Graphics.FromImage(panel))
                {
                    rim.DrawToBitmap(strip, new Rectangle(Point.Empty, rim.Size));
                    graphics.DrawImageUnscaled(strip, rim.Location);
                }
            }
        }

        // Turns the mouse wheel over the perch slider, so it reaches the page the way a real wheel does.
        internal void Wheel(int notches, bool settle = true)
        {
            Point at = perchFps.PointToScreen(new Point(perchFps.Width / 2, perchFps.Height / 2));
            for (int i = 0; i < Math.Abs(notches); i++)
                Native.SendMessage(perchFps.Handle, 0x020A, new IntPtr((notches > 0 ? 120 : -120) << 16), new IntPtr((at.Y << 16) | (at.X & 0xFFFF)));
            if (settle) Settle();
        }

        // Tabs through Settings from Back and names the first control that gets focus without all of it showing, or null.
        internal string TabShows(out string limit)
        {
            limit = null;
            Restore();
            if (Control.MouseButtons != MouseButtons.None) { limit = "a mouse button is held"; return null; }
            if (!more.Focus() || !more.Focused) { limit = "Windows did not give Settings keyboard focus"; return null; }
            Control from = more;
            bool keys = false;
            for (int i = 0; i < 160; i++)
            {
                if (Control.MouseButtons != MouseButtons.None) { limit = "a mouse button was pressed during the Tab walk"; return null; }
                if (!ContainsFocus) { limit = "Settings lost keyboard focus during the Tab walk"; return null; }
                if (!SelectNextControl(from, true, true, true, true)) break;
                Control at = ActiveControl;
                if (!ContainsFocus) { limit = "Settings lost keyboard focus during the Tab walk"; return null; }
                if (at == null || at == from || at == more) break;
                if (!at.ContainsFocus) return "Tab selected a control without moving keyboard focus";
                Control whole = at;
                while (whole.Parent != this) whole = whole.Parent;
                if (!ClientRectangle.Contains(whole.Bounds)) return "Tab reached " + (whole.AccessibleName ?? whole.Text) + " half off the screen";
                keys = keys || whole == quietKeys;
                from = at;
            }
            if (!more.Focus() || !more.Focused) { limit = "Windows did not return keyboard focus to Back"; return null; }
            return keys ? null : "Tab never reached the quiet key";
        }

        internal string VoiceStyleStatus { get { return pitchName.Text + " | " + paceName.Text + " | " + robotName.Text + " | " + glitchName.Text; } }

        // Drags one of her voice's sliders and lets go there, as they would: 0 pitch, 1 speed, 2 glitch, 3 robot.
        internal void SlideVoice(int which, int index) { (which == 0 ? pitchSlider : which == 1 ? paceSlider : which == 2 ? glitchSlider : robotSlider).Slide(index); }

        internal string GapShown { get { return gapName.Text; } }

        internal void SlideGap(int steps) { Press(gap, steps); }

        // Arrow keys on a slider, the way they'd press them.
        private static void Press(Slider slider, int steps) { slider.Focus(); for (int i = 0; i < Math.Abs(steps); i++) { KeyEventArgs press = new KeyEventArgs(steps > 0 ? Keys.Right : Keys.Left); typeof(Control).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(slider, new object[] { press }); } }

        internal void Searching()
        {
            brainName.Text = "Looking for a brain...";
            brainNote.Text = "";
            modelMenu.Items.Clear();
            menuRemote = null;
            menuModels = null;
            models.Visible = false;
            hint.Visible = false;
            Arrange(false);
        }

        internal void ShowOllama(string[] installed, string chosen)
        {
            brainName.Text = "Ollama · " + chosen;
            brainNote.Text = "Running on this PC";
            models.Text = chosen;
            models.Visible = true;
            hint.Visible = false;
            lastInstalled = installed;
            LocalMenu(chosen, null);
            Arrange(false);
        }

        internal void ShowServers(List<Cloud> servers, string[] installed, Pick local)
        {
            lastServers = servers;
            lastInstalled = installed;
            lastLocal = local;
        }

        private void LocalMenu(string chosen, Cloud remote)
        {
            menuRemote = null;
            modelMenu.Items.Clear();
            foreach (string name in lastInstalled ?? new string[0])
            {
                string pick = name;
                ToolStripMenuItem item = new ToolStripMenuItem("Ollama · " + name, null, delegate { capri.ChooseLocalBrain(null, pick, null); ShowOllama(lastInstalled, pick); });
                item.Checked = remote == null && name == chosen;
                modelMenu.Items.Add(item);
            }
            if (lastLocal != null) modelMenu.Items.Add(Choice("Built-in · " + lastLocal.Name, remote == null && chosen == lastLocal.Name, delegate { capri.ChooseLocalBrain(null, lastLocal.Name, lastLocal); ShowLocal(lastLocal); }));
            List<Cloud> listed = new List<Cloud>(lastServers);
            if (remote != null && !listed.Exists(candidate => candidate.Source == remote.Source)) listed.Add(remote);
            foreach (Cloud server in listed)
            {
                Cloud pick = new Cloud(server.Port, server.Model, server.Host);
                modelMenu.Items.Add(Choice(pick.Name + " · " + pick.Model, remote != null && remote.Source == pick.Source, delegate { capri.ChooseLocalBrain(pick, pick.Model, null); ShowCloud(pick, null); }));
            }
        }

        internal string[] BrainChoices { get { List<string> choices = new List<string>(); foreach (ToolStripItem item in modelMenu.Items) choices.Add(item.Text); return choices.ToArray(); } }

        internal void ShowProvider(string address, bool keyed, string model, string[] found)
        {
            brainName.Text = "My provider" + (model == null ? "" : " · " + model);
            brainNote.Text = keyed ? "Using your provider with a key" : "Using your server with no key";
            models.Text = model;
            models.Visible = model != null;
            hint.Visible = false;
            menuRemote = null;
            menuModels = null;
            List<ToolStripItem> choices = new List<ToolStripItem>();
            foreach (string name in found ?? new string[0])
            {
                string pick = name;
                choices.Add(Choice(pick, pick == model, delegate { capri.ChooseProviderModel(address, pick, found); }));
            }
            modelMenu.SuspendLayout();
            modelMenu.Items.Clear();
            modelMenu.Items.AddRange(choices.ToArray());
            modelMenu.ResumeLayout();
            Arrange(false);
        }

        internal void OpenProvider()
        {
            if (providerSheet != null && !providerSheet.IsDisposed && providerSheet.Zoom != zoom) providerSheet.Close();
            if (providerSheet == null || providerSheet.IsDisposed) providerSheet = new ProviderSheet(this, zoom);
            providerSheet.Fill();
            if (!providerSheet.Visible) providerSheet.Show(this);
            else providerSheet.Activate();
        }

        internal void ProviderProblem(string text)
        {
            if (providerSheet != null && !providerSheet.IsDisposed) providerSheet.Say(text);
            ShowProblem(text);
        }

        internal void TypeProvider(string address, string key) { providerSheet.Type(address, key); }

        internal void ConnectProvider() { providerSheet.Connect(); }

        internal string ProviderNote { get { return providerSheet == null || providerSheet.IsDisposed ? "" : providerSheet.Status; } }

        internal bool ProviderMaskedAndEmpty { get { return providerSheet != null && !providerSheet.IsDisposed && providerSheet.MaskedAndEmpty; } }

        internal bool ProviderSavedEnabled { get { return providerSheet != null && !providerSheet.IsDisposed && providerSheet.SavedEnabled; } }

        internal void PressProvider(string button) { providerSheet.Press(button); }

        internal void CloseProvider() { if (providerSheet != null && !providerSheet.IsDisposed) providerSheet.Close(); }

        internal string ProviderAt(float zoom)
        {
            using (ProviderSheet drawn = new ProviderSheet(this, zoom))
            {
                drawn.Fill();
                drawn.Show(this);
                foreach (string status in new[] { "Provider forgotten.", "That address doesn't work. Try https://... or http://localhost:1234.", "Use https:// for a provider elsewhere. A key over plain http isn't safe.", "Stopped using it. Couldn't remove the saved provider." })
                {
                    drawn.Say(status);
                    string problem = drawn.Crowded();
                    if (problem != null) return problem;
                }
                return null;
            }
        }

        internal void PickBrain(int index) { ((ToolStripMenuItem)modelMenu.Items[index]).PerformClick(); }

        // The cloud brain and list her model menu was last built from.
        private Cloud menuRemote;
        private string[] menuModels;

        internal void ShowCloud(Cloud remote, List<string> found)
        {
            brainName.Text = remote.Name + (remote.Model == null ? "" : " · " + remote.Model);
            brainNote.Text = remote.Kind == "local" ? "Running on this PC · :" + remote.Port : remote.Kind == "nvidia" ? NvidiaNote : "Using your " + remote.Company + " key";
            models.Text = remote.Model;
            models.Visible = remote.Model != null;
            hint.Visible = false;
            if (remote.Kind == "local") { LocalMenu(null, remote); Arrange(false); return; }
            // OpenRouter lists hundreds of models and NVIDIA over a hundred, and adding them one at a time laid the whole menu out
            // again for each: about 20 s frozen on every OpenRouter pick (NoxEterna, 1 Nov), and seconds after a slow NVIDIA reply,
            // which shows this again (Mika). The same list only moves its tick, and a new one goes in all at once.
            string[] listed = (found ?? (remote.Model == null ? new List<string>() : new List<string> { remote.Model })).ToArray();
            if (remote == menuRemote && menuModels != null && modelMenu.Items.Count == listed.Length && System.Linq.Enumerable.SequenceEqual(menuModels, listed))
            {
                foreach (ToolStripItem shown in modelMenu.Items)
                {
                    ToolStripMenuItem ticked = shown as ToolStripMenuItem;
                    if (ticked != null) ticked.Checked = (ticked.Tag as string) == remote.Model;
                }
            }
            else
            {
                ToolStripItem[] choices = new ToolStripItem[listed.Length];
                for (int i = 0; i < listed.Length; i++)
                {
                    string pick = listed[i];
                    ToolStripMenuItem choice = Choice(Brain.Shown(remote.Kind, pick), pick == remote.Model, delegate { capri.ChooseCloudModel(remote, pick); });
                    choice.Tag = pick;
                    choices[i] = choice;
                }
                modelMenu.SuspendLayout();
                modelMenu.Items.Clear();
                modelMenu.Items.AddRange(choices);
                modelMenu.ResumeLayout();
                menuRemote = remote;
                menuModels = listed;
            }
            Arrange(false);
        }

        private int KeyRow { get { return tier > 0 ? 128 + (tier != 1 ? 0 : paidTextKey != null ? 86 : 20) : 94; } }

        internal void ShowTiers(int pick, string nvidiaTail, string paidTail, string mediumKind = null)
        {
            int old = tier;
            tier = Math.Max(0, Math.Min(rungs.Length - 1, pick));
            if (old != tier) textEntry.Box.Clear();
            ClearPaidText();
            syncing = true;
            for (int i = 0; i < rungs.Length; i++) rungs[i].Checked = i == tier;
            syncing = false;
            string tail = tier == 1 ? nvidiaTail : tier == 2 ? paidTail : null;
            textSaved = tail != null;
            textEntry.Box.AccessibleName = tier == 1 ? "NVIDIA, Google AI Studio (Gemini) or OpenRouter API key" : "Google AI, OpenRouter, NVIDIA, OpenAI, Anthropic or DeepSeek API key";
            textEntry.AccessibleName = textEntry.Box.AccessibleName;
            tierNote.Text = tier == 1 ? mediumKind == "nvidia" ? "Most NVIDIA models can't see your screen." : "NVIDIA, Gemini, OpenRouter" : tier == 2 ? "Gemini, OpenRouter, ChatGPT, Claude, DeepSeek" : "Pick one. Only one brain runs at a time.";
            textEntry.Cue = textSaved ? "Key saved, ends " + tail : tier == 1 ? "NVIDIA / Google AI Studio / OpenRouter" : "Paste a brain API key";
            RelabelText();
            Arrange(expanded);
        }

        internal bool TiersFit { get { return TiersProblem == null; } }

        // What doesn't fit among BRAIN's tiers, in words for the smoke test, or null when it all fits.
        internal string TiersProblem
        {
            get
            {
                if (brainName.Bounds.IntersectsWith(provider.Bounds) || provider.Bottom > brainNote.Top || provider.Right > Scaled(356)) return "Provider overlaps the brain name or note";
                Rectangle last = rungs[rungs.Length - 1].Bounds;
                if (textEntry.Visible && textEntry.Top < last.Bottom) return "the key box (top " + textEntry.Top + ") overlaps the last rung (bottom " + last.Bottom + ")";
                if (textEntry.Visible && !Hidden(textConnect) && textConnect.Right > ClientSize.Width) return "Connect (right " + textConnect.Right + ") runs past the Mainframe (" + ClientSize.Width + ")";
                int end = textEntry.Visible ? textEntry.Bottom : last.Bottom;
                if (freeWarning.Visible)
                {
                    if (freeWarning.Top < end || freeWarning.Right > Scaled(356)) return "the free-key warning (" + freeWarning.Bounds + ") overlaps the key box (bottom " + end + ") or runs past " + Scaled(356);
                    end = freeWarning.Bottom;
                }
                if (paidNote.Visible)
                {
                    if (paidNote.Top < end || movePaid.Top < paidNote.Bottom || movePaid.Right > Scaled(356)) return "the paid-key offer (" + paidNote.Bounds + ", " + movePaid.Bounds + ") overlaps or runs past " + Scaled(356);
                    end = movePaid.Bottom;
                }
                foreach (Rung brainRung in rungs) if (!brainRung.Fits) return brainRung.Text + " doesn't fit its rung (" + brainRung.Bounds + ")";
                if (tierNote.Right > ClientSize.Width) return "the tier note (right " + tierNote.Right + ") runs past the Mainframe (" + ClientSize.Width + ")";
                if (rungs[0].Top < tierNote.Bottom || rungs[1].Top < rungs[0].Bottom || rungs[2].Top < rungs[1].Bottom) return "the rungs overlap the tier note or each other";
                if (freeKey.Left < rungs[1].Right || freeKey.Top < rungs[1].Top || freeKey.Bottom > rungs[1].Bottom) return "Free key (" + freeKey.Bounds + ") overlaps or leaves its rung (" + rungs[1].Bounds + ")";
                if (end > voiceHead.Top) return "BRAIN (bottom " + end + ") runs into VOICE (top " + voiceHead.Top + ")";
                if (rungs[0].Right > ClientSize.Width) return "the rungs (right " + rungs[0].Right + ") run past the Mainframe (" + ClientSize.Width + ")";
                return null;
            }
        }

        internal string TextStatus { get { return "tier " + tier + " | " + brainName.Text + " | " + brainNote.Text + " | " + (textEntry.Visible ? textEntry.Cue + " | " + textConnect.Text : "no key row"); } }

        private void ConnectText()
        {
            string typed = textEntry.Box.Text.Trim();
            if (typed.Length > 0)
            {
                string kind = Cloud.KindOf(typed);
                if (tier != 1 || kind == null || Cloud.MediumKind(kind)) textEntry.Box.Clear();
                capri.ConnectText(typed);
            }
            else if (textSaved) capri.ForgetText();
            else textEntry.Box.Focus();
        }

        private void RelabelText()
        {
            string typed = textEntry.Box.Text.Trim(), kind = Cloud.KindOf(typed);
            if (tier == 1 && kind != null && !Cloud.MediumKind(kind)) OfferPaidText(typed);
            else ClearPaidText();
            bool forget = textSaved && textEntry.Box.Text.Trim().Length == 0;
            textConnect.Text = forget ? "Forget" : "Connect";
            textConnect.Accent = forget ? Hot : Rose;
            textConnect.Invalidate();
        }

        internal void OfferPaidText(string key)
        {
            bool wasShown = paidTextKey != null;
            paidTextKey = Voice.Protect(key);
            if (!wasShown) Arrange(expanded);
        }

        private void ClearPaidText()
        {
            bool wasShown = paidTextKey != null;
            paidTextKey = null;
            if (wasShown) Arrange(expanded);
        }

        private void MovePaidText()
        {
            string key = Voice.Unprotect(paidTextKey);
            textEntry.Box.Clear();
            ClearPaidText();
            if (key != null) capri.ConnectPaidText(key);
        }

        internal void ShowLocal(Pick pick)
        {
            brainName.Text = "Built-in · " + pick.Name;
            brainNote.Text = "Running on this PC";
            models.Text = pick.Name;
            models.Visible = lastServers.Count > 0 || (lastInstalled != null && lastInstalled.Length > 0);
            LocalMenu(pick.Name, null);
            hint.Visible = false;
            Arrange(false);
        }

        internal void ShowSetup(Machine machine, Pick pick, bool viaOllama, string problem)
        {
            brainName.Text = problem == null ? "No brain yet" : "Setup stopped";
            brainNote.Text = problem ?? "Best fit for this PC: " + pick.Name;
            models.Visible = false;
            Step(0, "✓", machine.Describe(), White);
            Step(1, "2", "Download " + pick.Name + " (" + pick.Download + ")" + (viaOllama ? " with Ollama" : ""), Faint);
            Step(2, "3", "Wake her up", Faint);
            filled = 0;
            setup.Text = problem == null ? "Set up her brain" : "Try again";
            setup.Enabled = true;
            hint.Visible = !viaOllama;
            Arrange(true);
        }

        internal void ShowProgress(int step, string text, long done, long total)
        {
            Step(1, step > 1 ? "✓" : "2", stepText[1], step == 1 ? Cyan : White);
            Step(2, "3", stepText[2], step == 2 ? Cyan : Faint);
            brainName.Text = step == 1 ? "Setting up her brain" : "Waking her up";
            brainNote.Text = total > 0 ? text + " · " + Engine.Amount(done) + " of " + Engine.Amount(total) : text;
            filled = total > 0 ? Math.Min(1f, done / (float)total) : step == 2 ? 1f : 0f;
            setup.Text = step == 1 ? "Cancel" : "Starting...";
            setup.Enabled = step == 1;
            hint.Visible = false;
            if (!expanded) Arrange(true);
            else Invalidate(new Rectangle(0, Scaled(204), Width, Scaled(18)));
        }

        internal void ShowProblem(string problem) { brainNote.Text = problem; }

        internal void ShowInstall(string text, long done, long total)
        {
            bool opening = voiceFilled < 0;
            installNote.Text = total > 0 ? text + " · " + Engine.Amount(done) + " of " + Engine.Amount(total) : text + "...";
            voiceFilled = total > 0 ? Math.Min(1f, done / (float)total) : 0f;
            if (opening) Arrange(expanded);
            else Invalidate(new Rectangle(0, Scaled(InstallAt + 20 + (expanded ? SetupHeight : 0) + KeyRow - arrangedFold - hearingFold), Width, Scaled(14)));
        }

        internal void HideInstall()
        {
            if (voiceFilled < 0) return;
            voiceFilled = -1;
            installNote.Text = "";
            Arrange(expanded);
        }

        internal string InstallShown { get { return voiceFilled < 0 ? null : installNote.Text + " | " + (int)Math.Round(voiceFilled * 100) + "%"; } }

        internal int CapriRowTop { get { return showHide.Top; } }

        // OFF folds both rows under the VOICE rows away. PREBUILT LOCAL shows her voice picker and CUSTOM LOCAL their model, in the
        // second row. CLOUD VOICE shows its service in the first, with ElevenLabs' key box, then its Voice ID and model once a key is saved.
        private int Fold { get { return off ? 2 * KeyFold : voiceRow == 3 && cloudName == Eleven && keySaved ? 0 : KeyFold; } }

        private void VoiceTier()
        {
            off = capri.Silent;
            eleven = keySaved ? capri.Speaking : wantEleven;
            bool served = !off && !eleven && capri.ServerPicked, inCloud = served && capri.ServerInCloud;
            if (eleven) cloudName = Eleven;
            else if (inCloud) cloudName = capri.ServerCloudName;
            else if (wantCloud != null) cloudName = wantCloud;
            else cloudName = capri.CloudChoice;
            voiceRow = off ? 0 : eleven || inCloud || wantCloud != null ? 3 : served ? 2 : 1;
            for (int i = 0; i < voiceRungs.Length; i++) voiceRungs[i].Checked = i == voiceRow;
            bool cloud = voiceRow == 3, elevenLabs = cloud && cloudName == Eleven;
            cloudPick.Visible = cloud;
            cloudPick.Text = cloudName;
            foreach (ToolStripMenuItem item in cloudMenu.Items) item.Checked = item.Text == cloudName;
            keyEntry.Visible = elevenLabs;
            connect.Visible = elevenLabs;
            cloudVoice.Visible = cloud && !elevenLabs;
            cloudVoice.Text = inCloud ? "Voice, key and model..." : "Set up " + cloudName + "...";
            voiceEntry.Visible = elevenLabs && keySaved;
            speechModels.Visible = elevenLabs && keySaved;
            freeVoices.Visible = voiceRow == 1;
            customVoice.Visible = voiceRow == 2;
            // A model file runs on her free voice's engine, and her free voice fills in for a server that's down.
            freeSetup.Visible = (voiceRow == 1 || voiceRow == 2) && !freeReady;
            customVoice.Width = Scaled(freeReady ? 328 : 196);
            cloudPick.Invalidate();
            cloudVoice.Invalidate();
            if (Fold != arrangedFold) Arrange(expanded);
            ShowEffects();
        }

        private void PickRow(int row)
        {
            if (row == 0) PickSilent();
            else if (row == 1) { wantEleven = false; wantCloud = null; capri.UsePrebuilt(); }
            else if (row == 2) PickModel(false, null);
            else PickCloud(cloudName);
        }

        // ElevenLabs keeps its key, Voice ID and model in the Mainframe; Fish Audio and OpenAI open the voice model panel.
        private void PickCloud(string name)
        {
            cloudName = name;
            if (name == Eleven) PickVoice(true);
            else PickModel(true, CloudApi(name));
        }

        // One they set up before comes back at once. Otherwise the voice model panel opens with this row's choices, and a cloud
        // service stays picked under CLOUD VOICE, so its menu can still switch to ElevenLabs if they close the panel.
        private void PickModel(bool cloud, string api)
        {
            if (capri.HasServer(cloud, api))
            {
                wantEleven = false;
                wantCloud = null;
                capri.UseSavedServer(cloud);
            }
            else
            {
                wantCloud = cloud ? cloudName : null;
                OpenServer(cloud, api);
                VoiceTier();
            }
        }

        private static string CloudApi(string name) { return name == Clouds[0] ? VoiceServer.Fish : name == Clouds[2] ? VoiceServer.OpenAi : null; }

        // ElevenLabs' setup guide, or the voice model panel, whose note says where Fish Audio's and OpenAI's keys come from.
        private void CloudGuide()
        {
            if (cloudName == Eleven) capri.OpenKeyPage();
            else OpenServer(true, CloudApi(cloudName));
        }

        // Her HD voice and their voice server keep their own robot and glitch, so those sliders show the ones for the voice she speaks with.
        private void ShowEffects()
        {
            bool was = syncing;
            syncing = true;
            robotSlider.Value = Nearest(capri.VoiceRobot);
            robotName.Text = AmountName(robotSlider.Value);
            glitchSlider.Value = Nearest(capri.VoiceGlitch);
            glitchName.Text = AmountName(glitchSlider.Value);
            syncing = was;
        }

        private static int Nearest(int amount)
        {
            int near = 0;
            for (int i = 0; i < Amounts.Length; i++) if (Math.Abs(Amounts[i] - amount) < Math.Abs(Amounts[near] - amount)) near = i;
            return near;
        }

        private string AmountName(int step) { return AmountNames[step] + (capri.SpeaksHd ? " · HD" : capri.SpeaksServer ? " · server" : ""); }

        internal void PickVoice(bool paid)
        {
            wantEleven = paid;
            wantCloud = null;
            capri.ChooseVoiceTier(paid);
            if (paid && !keySaved) keyEntry.Box.Focus();
        }

        internal void PickSilent()
        {
            wantCloud = null;
            capri.Silence();
        }

        internal void ShowVoice(string tail, string voiceId, string model, VoiceListing listing)
        {
            keySaved = tail != null;
            // Short, to fit beside CLOUD VOICE's menu, which already says ElevenLabs.
            keyEntry.Cue = keySaved ? "Saved, ends " + tail : "Paste API key";
            Relabel();
            VoiceTier();
            if (!voiceEntry.Box.Focused || voiceEntry.Box.Text.Trim().Length == 0) voiceEntry.Box.Text = voiceId;
            voiceMenu.Items.Clear();
            voiceEntry.Chevron = listing != null && listing.Voices.Count > 0;
            speechModelMenu.Items.Clear();
            speechModelMenu.Items.Add(Choice("Default model", string.IsNullOrEmpty(model), delegate { capri.ChooseVoiceModel(null); }));
            speechModels.Text = string.IsNullOrEmpty(model) ? "Default model" : model;
            if (listing != null)
            {
                foreach (VoiceEntry entry in listing.Voices)
                {
                    string id = entry.Id;
                    voiceMenu.Items.Add(Choice(entry.Name ?? id, id == voiceId, delegate { voiceEntry.Box.Text = id; capri.ChooseVoice(id); }));
                }
                foreach (VoiceModel option in listing.Models)
                {
                    string id = option.Id;
                    string name = option.Name ?? id;
                    if (id == model) speechModels.Text = name.StartsWith("Eleven ", StringComparison.OrdinalIgnoreCase) ? name.Substring(7) : name;
                    speechModelMenu.Items.Add(Choice(name, id == model, delegate { capri.ChooseVoiceModel(id); }));
                }
            }
            speechModels.Invalidate();
        }

        // `yours`: their clip is what her HD voice clones. `kept`: one is saved but she's on her own. `served`: the name of the voice
        // server she speaks through, like Fish Audio, when it's picked.
        internal void ShowFree(bool ready, string chosen, bool busy, Machine machine, bool yours, bool kept, string served = null)
        {
            bool hd = chosen == QwenVoice.Style;
            ShowHdGames();
            freeReady = ready;
            yoursShown = hd && yours;
            freeVoices.Text = "Free voice: " + (yoursShown ? "Your voice" : served ?? chosen);
            customVoice.Text = served != null ? "Voice model: " + served : "Pick a voice model...";
            freeSetup.Text = busy ? "Cancel" : "Get free voice";
            freeSetup.Visible = (voiceRow == 1 || voiceRow == 2) && !ready;
            freeVoices.Width = Scaled(ready && !yoursShown ? 328 : 196);
            customVoice.Width = Scaled(ready ? 328 : 196);
            freeMenu.Items.Clear();
            foreach (FreeStyle style in LocalVoice.Styles)
            {
                string name = style.Name;
                freeMenu.Items.Add(Choice(name, name == chosen, delegate { capri.ChooseVoiceStyle(name); }));
            }
            freeMenu.Items.Add(new ToolStripSeparator());
            // Capri ships no voice clip, so HD copies only a clip they pick: before there is one, HD asks for it.
            if (!yours && !kept)
            {
                ToolStripMenuItem own = Choice(QwenVoice.Advice(machine), hd, delegate { PickYourVoice(); });
                freeMenu.Items.Add(own);
            }
            // Only her HD voice clones from a clip; her other styles are set voices.
            if (yours || kept)
            {
                ToolStripMenuItem theirs = Choice("Your voice (HD)", yoursShown, delegate { if (yours) capri.ChooseVoiceStyle(QwenVoice.Style); else capri.UseKeptVoice(); });
                freeMenu.Items.Add(theirs);
            }
            ToolStripMenuItem pick = Choice(yours || kept ? "Pick a new clip of you..." : "Your voice (HD) · pick a clip of you...", false, delegate { PickYourVoice(); });
            freeMenu.Items.Add(pick);
            freeMenu.Items.Add(Choice(QwenVoice.CpuChoice, capri.HdCpu, delegate { capri.ToggleHdCpu(); }));
            freeVoices.Invalidate();
            customVoice.Invalidate();
            freeSetup.Invalidate();
            ShowEffects();
        }

        // 5 to 30 seconds of them talking, as a WAV, MP3 or M4A.
        private void PickYourVoice()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Pick 5 to 30 seconds of you talking, or of a voice you have the rights and consent to use";
                dialog.Filter = "Voice clips (*.wav;*.mp3;*.m4a)|*.wav;*.mp3;*.m4a|All files (*.*)|*.*";
                if (dialog.ShowDialog(this) == DialogResult.OK) capri.UseYourVoice(dialog.FileName);
            }
        }

        internal string YourVoiceStatus
        {
            get
            {
                List<string> items = new List<string>();
                foreach (ToolStripItem item in freeMenu.Items)
                {
                    ToolStripMenuItem choice = item as ToolStripMenuItem;
                    if (choice != null && (choice.Text.StartsWith("HD ", StringComparison.Ordinal) || choice.Text.IndexOf("you", StringComparison.OrdinalIgnoreCase) >= 0)) items.Add(choice.Text + (choice.Checked ? " (picked)" : ""));
                }
                return freeVoices.Text + " | " + string.Join(", ", items.ToArray());
            }
        }

        internal bool HdChoicesEnabled
        {
            get
            {
                foreach (ToolStripItem item in freeMenu.Items) if (item is ToolStripMenuItem && !item.Enabled) return false;
                return true;
            }
        }


        // Clicks the free voice menu's item that starts with this text, as they would. False if it isn't there or can't be clicked.
        internal bool PickFree(string text)
        {
            foreach (ToolStripItem item in freeMenu.Items)
                if (item is ToolStripMenuItem && item.Enabled && item.Text.StartsWith(text, StringComparison.Ordinal)) { item.PerformClick(); return true; }
            return false;
        }

        internal string FreeStatus { get { return (freeVoices.Visible ? freeVoices.Text : "-") + " | " + (freeSetup.Visible ? freeSetup.Text : "-"); } }

        internal string HdChoice
        {
            get
            {
                foreach (ToolStripItem item in freeMenu.Items)
                {
                    ToolStripMenuItem choice = item as ToolStripMenuItem;
                    if (choice != null && choice.Text.StartsWith(QwenVoice.Style + " ", StringComparison.Ordinal)) return choice.Text + (choice.Enabled ? "" : " (off)") + (choice.Checked ? " (picked)" : "");
                }
                return "-";
            }
        }

        // CUSTOM LOCAL's voice model, or CLOUD VOICE's Fish Audio or OpenAI: its address, model, voice and key, in a panel of its own
        // over the Mainframe with only that row's choices.
        private void OpenServer(bool cloud, string api)
        {
            if (sheet != null && !sheet.IsDisposed && !sheet.Visible && sheet.Zoom != zoom) sheet.Close();
            if (sheet == null || sheet.IsDisposed) sheet = new ServerSheet(this, zoom);
            sheet.Fill(cloud, api);
            if (!sheet.Visible) sheet.Show(this);
            sheet.Activate();
        }

        // Her SECURITY window, over the Mainframe like their voice server's panel.
        private void OpenSecurity()
        {
            if (guard != null && !guard.IsDisposed && guard.Zoom != zoom) guard.Close();
            if (guard == null || guard.IsDisposed) guard = new SecuritySheet(this, zoom);
            guard.Sync();
            if (!guard.Visible) guard.Show(this);
            guard.Activate();
        }

        // Her EXTRAS window, over the Mainframe like SECURITY. Only a new install's first launch opens it.
        internal void ShowExtras()
        {
            if (extras != null && !extras.IsDisposed && extras.Zoom != zoom) extras.Close();
            if (extras == null || extras.IsDisposed) extras = new ExtrasSheet(this, zoom);
            extras.Sync();
            if (!extras.Visible) extras.Show(this);
            extras.Activate();
        }

        // Her VOICE LAYERS window, over the Mainframe like SECURITY.
        private void OpenLayers()
        {
            if (layered != null && !layered.IsDisposed && layered.Zoom != zoom) layered.Close();
            if (layered == null || layered.IsDisposed) layered = new LayersSheet(this, zoom);
            layered.Sync();
            if (!layered.Visible) layered.Show(this);
            layered.Activate();
        }

        private void ShowLayers()
        {
            bool on = capri.VoiceLayers.On;
            if (layersKey.Picked != on) layersKey.Pick(on);
            if (layered != null && !layered.IsDisposed) layered.Sync();
        }

        // Her VOICE LAYERS window as they'd use it, for the smoke test: opened from VOICE, the switch flipped, a slider dragged,
        // what it shows and how it fits at 100%, 150% and 200%.
        internal void ShowLayersWindow() { OpenLayers(); }

        internal string LayersShown { get { return layered == null || layered.IsDisposed || !layered.Visible ? null : layered.Showing; } }

        internal string LayersKeyShown { get { return layersKey.Text + (layersKey.Picked ? " (lit)" : ""); } }

        internal void FlipLayers() { layered.Flip(); }

        // Slider 0 is MAIN's pitch, 1 and 2 Layer 1's pitch and delay, 3 and 4 Layer 2's.
        internal void SlideLayers(int slider, int index) { layered.Slide(slider, index); }

        internal void LayersDefaults() { layered.Defaults(); }

        internal void CloseLayers() { if (layered != null && !layered.IsDisposed) layered.Close(); }

        internal string LayersCrowded { get { return layered == null || layered.IsDisposed ? "not open" : layered.Crowded(); } }

        // VOICE's heading from 100% to 200%: "Applies to every voice" and the Layers button each fit their own text, and
        // they don't meet. Null when they do.
        internal string VoiceHeadingFits()
        {
            if (styleNote.Bounds.IntersectsWith(layersKey.Bounds) || styleNote.Right > layersKey.Left) return "the note runs into the Layers button";
            // Text is measured at this screen's own scale, so each font is sized for the scale being checked.
            float here = Unit();
            foreach (float zoom in new[] { 1f, 1.25f, 1.5f, 1.75f, 2f })
                using (Font note = new Font(styleNote.Font.FontFamily, styleNote.Font.SizeInPoints * zoom / here, styleNote.Font.Style, GraphicsUnit.Point))
                using (Font key = new Font(layersKey.Font.FontFamily, layersKey.Font.SizeInPoints * zoom / here, layersKey.Font.Style, GraphicsUnit.Point))
                {
                    int wide = TextRenderer.MeasureText(styleNote.Text, note).Width, room = (int)(styleNote.Width / here * zoom);
                    if (wide > room) return "\"" + styleNote.Text + "\" is " + wide + " px in " + room + " at " + zoom;
                    foreach (string text in new[] { layersKey.Text })
                    {
                        int size = TextRenderer.MeasureText(text, key).Width + (int)Math.Ceiling(12 * zoom), keyRoom = (int)(layersKey.Width / here * zoom);
                        if (size > keyRoom) return "\"" + text + "\" is " + size + " px in " + keyRoom + " at " + zoom;
                    }
                }
            return null;
        }

        internal string LayersAt(float zoom, string picture)
        {
            using (LayersSheet drawn = new LayersSheet(this, zoom))
            {
                drawn.Show(this);
                string crowded = drawn.Crowded(), size = " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoom + ")";
                if (picture != null) drawn.Picture(picture);
                drawn.Close();
                return crowded == null ? null : crowded + size;
            }
        }

        // Her SECURITY window as they'd use it, for the smoke test: opened from HABITS, a toggle clicked, the slider dragged
        // or clicked, and what it shows.
        internal void ShowSecurity() { OpenSecurity(); }

        internal string SecurityShown { get { return guard == null || guard.IsDisposed || !guard.Visible ? null : guard.Showing; } }

        internal void FlipSecurity(int toggle) { guard.Flip(toggle); }

        internal void SlideSecurity(int preset) { guard.Slide(preset); }

        internal void TapSecurity(int preset) { guard.Tap(preset); }

        internal void CloseSecurity() { if (guard != null && !guard.IsDisposed) guard.Close(); }

        internal string ExtrasShown { get { return extras == null || extras.IsDisposed || !extras.Visible ? null : extras.Showing; } }

        internal void FlipExtra(int row) { extras.Flip(row); }

        internal void CloseExtras() { if (extras != null && !extras.IsDisposed) extras.Close(); }

        internal string ExtrasCrowded { get { return extras == null || extras.IsDisposed ? "not open" : extras.Crowded(); } }

        internal string SecurityCrowded { get { return guard == null || guard.IsDisposed ? "not open" : guard.Crowded(); } }

        // Her SECURITY window drawn at a scale of the screen's own (1.5 for 150% on a 100% screen), checked for overlaps and
        // text that doesn't fit, with its picture saved. Null when it all fits.
        internal string SecurityAt(float zoom, string picture)
        {
            using (SecuritySheet drawn = new SecuritySheet(this, zoom))
            {
                drawn.Show(this);
                string crowded = drawn.Crowded(), size = " (" + drawn.ClientSize.Width + " x " + drawn.ClientSize.Height + " at " + zoom + ")";
                if (picture != null) drawn.Picture(picture);
                drawn.Close();
                return crowded == null ? null : crowded + size;
            }
        }

        // The VOICE rows and CLOUD VOICE's service menu as they'd click them, and CUSTOM LOCAL's Set up, for the smoke test.
        internal void PickVoiceRow(int row) { PickRow(row); }

        internal void PickCloudVoice(string name) { PickCloud(name); }

        internal void SetUpCustom() { OpenServer(false, null); }

        internal void SetUpCloud() { OpenServer(true, CloudApi(cloudName)); }

        internal string ServerChoices { get { return sheet.Choices; } }

        internal string CloudPicked { get { return cloudName; } }

        internal string ModelChoices
        {
            get
            {
                List<string> names = new List<string>();
                foreach (ToolStripMenuItem item in modelMenu.Items) names.Add(item.Text + (item.Checked ? " (picked)" : ""));
                return string.Join(", ", names);
            }
        }

        internal string CloudMenu
        {
            get
            {
                List<string> names = new List<string>();
                foreach (ToolStripItem item in cloudMenu.Items) names.Add(item.Text);
                return string.Join(", ", names);
            }
        }

        internal string CloudShown { get { return (cloudPick.Visible ? cloudPick.Text : "-") + " | " + (cloudVoice.Visible ? cloudVoice.Text : "-") + " | " + (customVoice.Visible ? customVoice.Text : "-"); } }

        // Their voice server's panel for the smoke test: what it shows, typing into it, its choices and buttons, and a picture of it.
        internal string ServerShown { get { return sheet == null || sheet.IsDisposed || !sheet.Visible ? "-" : sheet.Showing; } }

        internal void TypeServer(string address, string name, string voice, string secret) { sheet.Type(address, name, voice, secret); }

        internal void PickService(string name) { sheet.Choose(name); }

        internal void TakeModel(string path) { sheet.Take(path); }

        internal string ServerFields { get { return sheet.Fields; } }

        internal string ServerHintPlace { get { return sheet.HintPlace; } }

        internal bool ServerScaledToScreen { get { return sheet.ScaledToScreen; } }

        internal void DropFishVoices() { sheet.DropVoices(); }

        internal string FishVoicesShown { get { return sheet.Voices; } }

        internal void PickFishVoice(string name) { sheet.PickVoice(name); }

        internal void MakeFishVoice(string clip) { sheet.MakeVoice(clip); }

        internal void TakeVoiceClip(string clip) { sheet.TakeClip(clip); }

        internal void PressServer(string button) { sheet.Press(button); }

        internal Form ServerPanel { get { return sheet; } }

        // The free voice menu as it drops down under its button, and where, for the smoke test's picture of it.
        internal Bitmap FreeMenuPicture()
        {
            freeMenu.Show(freeVoices, new Point(0, freeVoices.Height));
            try
            {
                Bitmap picture = new Bitmap(freeMenu.Width, freeMenu.Height);
                freeMenu.DrawToBitmap(picture, new Rectangle(Point.Empty, freeMenu.Size));
                return picture;
            }
            finally { freeMenu.Close(); }
        }

        internal Point FreeMenuAt { get { return new Point(freeVoices.Left, freeVoices.Bottom); } }

        internal string FreeMenuShown
        {
            get
            {
                List<string> items = new List<string>();
                foreach (ToolStripItem item in freeMenu.Items)
                {
                    ToolStripMenuItem choice = item as ToolStripMenuItem;
                    items.Add(choice == null ? "-" : choice.Text + (choice.Enabled ? "" : " (off)") + (choice.Checked ? " (picked)" : ""));
                }
                return string.Join(", ", items.ToArray());
            }
        }

        internal void ShowShortcut(bool talk, bool mute)
        {
            shortcutReady = talk;
            muteReady = mute;
            KeyLabels();
        }

        internal void ShowKeys(bool type, bool hide, bool quiet, bool game = true)
        {
            typeReady = type;
            hideReady = hide;
            quietReady = quiet;
            gameReady = game;
            KeyLabels();
        }

        internal void Hotkeys(string talk, string mute, string type, string hide, string quiet, string game)
        {
            gameName = game;
            talkName = talk;
            muteName = mute;
            typeName = type;
            hideName = hide;
            quietName = quiet;
            KeyLabels();
        }

        internal void PushKey(string name)
        {
            pushName = name;
            KeyLabels();
        }

        // Which of her keys to change: 0 talk, 1 mute mic, 2 type to her, 3 hide or show her, 4 quiet, 5 Gaming Mode, 6 push to talk.
        // Opening another key while one is being picked drops what was pressed, so the old key stays.
        internal void PickKeys(int slot)
        {
            capturing = slot + 1;
            pendingMods = -1;
            heldMods = 0;
            if (keysNote != null) keysNote.Text = "";
            KeyLabels();
        }

        internal void TakeKeys(Keys keys) { TakeKeys(keys, false); }

        // Every press shows in her key's button until Confirm or Cancel is clicked. The latest key with what was held wins.
        internal void TakeKeys(Keys keys, bool win)
        {
            if (capturing == 0) return;
            Keys key = keys & Keys.KeyCode;
            int mods = Companion.HotMods(keys, win || key == Keys.LWin || key == Keys.RWin);
            if (key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey || key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu || key == Keys.ShiftKey || key == Keys.LShiftKey || key == Keys.RShiftKey || key == Keys.LWin || key == Keys.RWin)
            {
                // Push to talk can be Ctrl, Alt or Shift alone, like a game's own push to talk. Any other key can join it.
                if (capturing == Companion.PushSlot + 1 && key != Keys.LWin && key != Keys.RWin)
                {
                    pendingMods = 0;
                    pendingVk = key == Keys.ControlKey || key == Keys.LControlKey || key == Keys.RControlKey ? 0x11 : key == Keys.Menu || key == Keys.LMenu || key == Keys.RMenu ? 0x12 : 0x10;
                    KeyLabels();
                    KeysNote("Confirm to keep " + Companion.KeyName(pendingMods, pendingVk) + ". It still does its own job in games and apps.", false);
                    return;
                }
                // A modifier on its own only shows until a key joins it, and never counts as her key.
                if (pendingMods < 0) { heldMods = mods; KeyLabels(); }
                return;
            }
            if (!Companion.Bindable(mods, (int)key)) return;
            pendingMods = mods;
            pendingVk = (int)key;
            KeyLabels();
            string name = Companion.KeyName(pendingMods, pendingVk);
            if (capturing == Companion.PushSlot + 1) { KeysNote("Confirm to keep " + name + ". It still does its own job in games and apps.", false); return; }
            KeysNote(Companion.Swallows(pendingMods, pendingVk) ? name + " won't type in other apps while she runs. Confirm to keep it."
                : Companion.ByMouse(pendingVk) ? name + " still does its own job in other apps too. Confirm to keep it." : "Confirm to keep " + name + ", or Cancel.", false);
        }

        internal void ConfirmKeys()
        {
            if (capturing == 0 || pendingMods < 0) return;
            int slot = capturing - 1, mods = pendingMods, vk = pendingVk;
            string problem = capri.SetHotkey(slot, mods, vk);
            // A key that's taken or already hers keeps the picking open, so another can be pressed straight away.
            if (problem != null) { KeysNote(problem, true); return; }
            capturing = 0;
            pendingMods = -1;
            KeyLabels();
            if (slot == Companion.PushSlot) { KeysNote("Saved. Hold " + pushName + " to talk to her, with a game in front too.", false); return; }
            KeysNote("Saved. " + (slot == 0 ? talkName + " starts voice chat." : slot == 1 ? muteName + " mutes your mic." : slot == 2 ? typeName + " opens her typing box." : slot == 3 ? hideName + " hides or shows her." : slot == 5 ? gameName + " turns Gaming Mode on and off." : quietName + " keeps her quiet, and lets her talk again.") + (Companion.Swallows(mods, vk) ? " It won't type in other apps while she runs." : Companion.ByMouse(vk) ? " It still does its own job in other apps too." : ""), false);
        }

        // Push to talk is the one key she can do without.
        internal void ClearPush()
        {
            if (capturing != Companion.PushSlot + 1) return;
            capri.SetHotkey(Companion.PushSlot, 0, 0);
            capturing = 0;
            pendingMods = -1;
            KeyLabels();
            KeysNote("Push to talk is off.", false);
        }

        internal void CancelKeys()
        {
            if (capturing == 0) return;
            capturing = 0;
            pendingMods = -1;
            if (keysNote != null) keysNote.Text = "";
            KeyLabels();
        }

        internal bool ConfirmReady { get { return confirmKeys.Visible && confirmKeys.Enabled; } }

        // Off shows only while the push-to-talk key is being picked.
        internal bool OffShown { get { return capturing == Companion.PushSlot + 1; } }

        private Control PickedKey { get { return capturing == 1 ? talkKeys : capturing == 2 ? muteKeys : capturing == 3 ? typeKeys : capturing == 4 ? hideKeys : capturing == 5 ? quietKeys : capturing == 6 ? gameKeys : capturing == 7 ? pushKeys : null; } }

        private static bool WinHeld() { return (Native.GetKeyState(0x5B) & 0x8000) != 0 || (Native.GetKeyState(0x5C) & 0x8000) != 0; }

        // A key change answers where it was made: under the KEYS row in Settings, or under VOICE on the main page.
        private void KeysNote(string text, bool problem)
        {
            if (!paged) { VoiceNote(text, problem); return; }
            keysNote.Text = text;
            keysNote.ForeColor = problem ? Hot : Faint;
        }

        internal string KeysNoteText { get { return keysNote.Text; } }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape) DismissHint();
            if (capturing == 0) return base.ProcessCmdKey(ref msg, keyData);
            TakeKeys(keyData, WinHeld());
            return true;
        }

        internal bool Picking { get { return capturing != 0; } }

        // While a key is being picked, middle click or a side mouse button anywhere on the Mainframe is picked like a key press.
        public bool PreFilterMessage(ref Message m)
        {
            FilterHints(ref m);
            // A wheel turn over Mainframe belongs to it, including at either end of the page (Lachy, 2 Nov).
            if (m.Msg == 0x020A)
            {
                Control wheelOver = Control.FromChildHandle(m.HWnd);
                if (wheelOver != null && wheelOver.FindForm() == this)
                {
                    ComboBox choice = wheelOver as ComboBox;
                    if (choice != null && choice.DroppedDown) return false;
                    ScrollWheel(unchecked((short)((m.WParam.ToInt64() >> 16) & 0xFFFF)));
                    return true;
                }
            }
            if (capturing == 0 || (m.Msg != 0x0207 && m.Msg != 0x0209 && m.Msg != 0x020B && m.Msg != 0x020D)) return false;
            Control over = Control.FromChildHandle(m.HWnd);
            if (over == null || over.FindForm() != this) return false;
            Keys button = m.Msg == 0x0207 || m.Msg == 0x0209 ? Keys.MButton : ((m.WParam.ToInt64() >> 16) & 0xFFFF) == 2 ? Keys.XButton2 : Keys.XButton1;
            TakeKeys(button | ModifierKeys, WinHeld());
            return true;
        }

        private const string Waiting = "Press new keys, then Confirm";

        private void KeyLabels()
        {
            shortcut.Text = capturing == 1 ? Waiting : talkName + (shortcutReady ? " to talk" : " is taken");
            muteShortcut.Text = capturing == 2 ? Waiting : muteName + (muteReady ? " mutes mic" : " is taken");
            if (cancelKeys == null) return;
            talkKeys.Text = capturing == 1 ? PickingText("Talk: ") : "Talk: " + talkName + (shortcutReady ? "" : " (taken)");
            muteKeys.Text = capturing == 2 ? PickingText("Mute mic: ") : "Mute mic: " + muteName + (muteReady ? "" : " (taken)");
            typeKeys.Text = capturing == 3 ? PickingText("Type: ") : "Type: " + typeName + (typeReady ? "" : " (taken)");
            hideKeys.Text = capturing == 4 ? PickingText("Hide/show: ") : "Hide/show: " + hideName + (hideReady ? "" : " (taken)");
            quietKeys.Text = capturing == 5 ? PickingText("Quiet: ") : "Quiet: " + quietName + (quietReady ? "" : " (taken)");
            gameKeys.Text = capturing == 6 ? PickingText("Gaming Mode: ") : "Gaming Mode: " + gameName + (gameReady ? "" : " (taken)");
            pushKeys.Text = capturing == 7 ? PickingText("Push to talk: ") : "Push to talk: " + pushName;
            confirmKeys.Visible = cancelKeys.Visible = capturing != 0;
            clearKeys.Visible = OffShown;
            confirmKeys.Enabled = pendingMods >= 0;
            confirmKeys.Invalidate();
        }

        // What the key being picked shows: the keys pressed so far, the modifiers held so far, or what to do.
        private string PickingText(string job)
        {
            if (pendingMods >= 0) return job + Companion.KeyName(pendingMods, pendingVk);
            if (heldMods != 0) return job + Companion.ModNames(heldMods) + "...";
            return Waiting;
        }

        internal string TierNote { get { return tierNote.Text; } }
        internal string KeyStatus { get { return shortcut.Text + " | " + muteShortcut.Text; } }
        internal string KeysShown { get { return talkKeys.Text + " | " + muteKeys.Text + " | " + typeKeys.Text + " | " + hideKeys.Text; } }
        internal string QuietKeyShown { get { return quietKeys.Text; } }
        internal string PushKeyShown { get { return pushKeys.Text; } }
        internal string CommentsShown { get { return commentsName.Text; } }
        internal string GameKeyShown { get { return gameKeys.Text; } }
        internal bool GamingShown { get { return gamingMode.Checked; } }

        internal void ShowMic(string name)
        {
            mics.Text = "Mic: " + (name ?? "default");
            mics.Invalidate();
        }

        internal void ShowSpeaker(string name)
        {
            speakers.Text = "Her speaker: " + (name ?? "Default");
            speakers.AccessibleName = "Her speaker: " + (name ?? "Default (follows Windows)");
            speakers.Invalidate();
        }

        private void SpeakerChoices()
        {
            capri.RefreshSpeakers();
            speakerMenu.Items.Clear();
            speakerMenu.Items.Add(Choice("Default (follows Windows)", string.IsNullOrEmpty(capri.SpeakerId), delegate { capri.ChooseSpeaker(null); }));
            List<Speaker.Output> found = capri.SpeakerDevices;
            bool missing = !string.IsNullOrEmpty(capri.SpeakerId) && Speaker.Find(capri.SpeakerId, found) == null;
            if (found.Count > 0 || missing) speakerMenu.Items.Add(new ToolStripSeparator());
            foreach (Speaker.Output output in found)
            {
                string id = output.Id;
                speakerMenu.Items.Add(Choice(output.Name, string.Equals(id, capri.SpeakerId, StringComparison.OrdinalIgnoreCase), delegate { capri.ChooseSpeaker(id); }));
            }
            if (missing) speakerMenu.Items.Add(new ToolStripMenuItem(capri.SpeakerName) { Checked = true, Enabled = false });
        }

        private void ShowSpeakers() { SpeakerChoices(); speakerMenu.Show(speakers, new Point(0, speakers.Height)); }

        internal void ShowWatchScreen(string name)
        {
            screens.Text = name ?? "All screens";
            screens.AccessibleName = "Screens to watch: " + screens.Text;
            screens.Invalidate();
        }

        private void ScreenChoices()
        {
            capri.RefreshWatchScreens();
            screenMenu.Items.Clear();
            screenMenu.Items.Add(Choice("All screens", string.IsNullOrEmpty(capri.WatchScreenId), delegate { capri.ChooseWatchScreen(null); }));
            List<Companion.WatchScreen> found = capri.WatchScreens;
            bool missing = !string.IsNullOrEmpty(capri.WatchScreenId) && !found.Exists(delegate(Companion.WatchScreen screen) { return string.Equals(screen.Id, capri.WatchScreenId, StringComparison.OrdinalIgnoreCase); });
            if (found.Count > 0 || missing) screenMenu.Items.Add(new ToolStripSeparator());
            foreach (Companion.WatchScreen screen in found)
            {
                string id = screen.Id;
                screenMenu.Items.Add(Choice(screen.Name, string.Equals(id, capri.WatchScreenId, StringComparison.OrdinalIgnoreCase), delegate { capri.ChooseWatchScreen(id); }));
            }
            if (missing) screenMenu.Items.Add(new ToolStripMenuItem(capri.WatchScreenName) { Checked = true, Enabled = false });
        }

        private void ShowScreens() { ScreenChoices(); screenMenu.Show(screens, new Point(0, screens.Height)); }

        internal string SpeakerShown { get { return speakers.Text; } }
        internal string WatchScreenShown { get { return screens.Text; } }

        internal string SpeakerChoicesShown { get { SpeakerChoices(); return PickChoices(speakerMenu); } }
        internal string ScreenChoicesShown { get { ScreenChoices(); return PickChoices(screenMenu); } }

        private static string PickChoices(ContextMenuStrip menu)
        {
            List<string> choices = new List<string>();
            foreach (ToolStripItem item in menu.Items)
            {
                ToolStripMenuItem choice = item as ToolStripMenuItem;
                if (choice != null) choices.Add(choice.Text + (choice.Checked ? " (picked)" : ""));
            }
            return string.Join(" | ", choices.ToArray());
        }

        internal void PickSpeakerChoice(int at) { SpeakerChoices(); speakerMenu.Items[at == 0 ? 0 : at + 1].PerformClick(); }
        internal void PickScreenChoice(int at) { ScreenChoices(); screenMenu.Items[at == 0 ? 0 : at + 1].PerformClick(); }

        // On a small desktop Windows clamps the window narrower than its page, so a clamped window skips the inside-the-window checks, like Outside's callers.
        internal string DeviceRowsFit()
        {
            if (!paged)
            {
                if (Hidden(speakers) || !speakers.Visible || !PageClamped && !ClientRectangle.Contains(speakers.Bounds)) return "Her speaker is hidden or outside VOICE";
                if (speakers.Top < Math.Max(mics.Bottom, micTest.Bottom) || hearingNote.Top < speakers.Bottom) return "Her speaker overlaps the mic or hearing row";
                foreach (Control control in Controls)
                    if (control != speakers && control.Visible && !Hidden(control) && control.Bounds.IntersectsWith(speakers.Bounds)) return "Her speaker overlaps " + control.Text;
            }
            else
            {
                if (Hidden(screens) || !screens.Visible || !PageClamped && !PageArea.Contains(screens.Bounds)) return "Screens is hidden or outside WATCH";
                if (screens.Top < watch.Bottom || caughtUp.Top < screens.Bottom) return "Screens overlaps a WATCH row";
            }
            return TextFits() ?? Overlap() ?? (paged && !PageClamped ? Outside() : null);
        }

        internal void ShowMicTest(bool listening, double level, bool muted)
        {
            micTest.Text = listening ? "Stop" : "Test";
            double db = level <= 0 ? -96 : Math.Max(-96, 20 * Math.Log10(level));
            micTest.Meter = listening ? (db + 96) / 96 : -1;
            micTest.AccessibleName = listening ? "Stop microphone test. " + (muted ? "Mic muted" : "Input level " + db.ToString("0") + " dB") : "Test microphone input";
            micTest.Invalidate();
            if (listening) VoiceNote(muted ? "Your mic is muted. Unmute it to test." : "Mic level: " + (level <= 0 ? "silence" : db.ToString("0") + " dB") + ". Speak now; click Stop when done.", false);
        }

        internal bool MicTesting { get { return micTest.Text == "Stop" && micTest.Meter >= 0; } }

        internal void PickHearingMic() { ShowMics(); }
        internal void PickHearingChoice(int choice) { hearingMenu.Items[choice].PerformClick(); }
        internal bool HearingMicMenuShown { get { return micMenu.Visible; } }
        internal void CloseHearingMicMenu() { micMenu.Close(); }

        private void ShowMics()
        {
            capri.RefreshHearingDevices();
            string chosen = capri.MicId;
            List<Mic> found = capri.HearingMics;
            micMenu.Items.Clear();
            micMenu.Items.Add(Choice("Windows default", Ears.Find(chosen, found) == null, delegate { capri.ChooseMic(null); }));
            if (found.Count > 0) micMenu.Items.Add(new ToolStripSeparator());
            foreach (Mic mic in found)
            {
                string id = mic.Id;
                micMenu.Items.Add(Choice(mic.Name, id == chosen, delegate { capri.ChooseMic(id); }));
            }
            micMenu.Show(mics, new Point(0, mics.Height));
        }

        internal void ShowHearing(HearingStatus status)
        {
            bool same = hearingNote.Text == status.Text && hearingNote.ForeColor == (status.Problem ? Hot : Faint) && hearingActions.Length == status.Actions.Length;
            if (same) for (int i = 0; i < hearingActions.Length; i++) if (hearingActions[i] != status.Actions[i]) { same = false; break; }
            if (same) return;
            hearingNote.Text = status.Text;
            hearingNote.AccessibleName = status.Text;
            hearingNote.ForeColor = status.Problem ? Hot : Faint;
            bool wasFolded = hearingActions.Length == 0;
            hearingActions = status.Actions;
            for (int i = 0; i < hearingFixes.Length; i++)
            {
                bool shown = i < status.Actions.Length;
                hearingFixes[i].Visible = shown;
                if (shown) hearingFixes[i].AccessibleName = hearingFixes[i].Text = HearingStatus.Name(status.Actions[i]);
            }
            if (loaded && wasFolded != (hearingActions.Length == 0)) Arrange(expanded);
        }

        internal string HearingText { get { return hearingNote.Text; } }
        internal bool HearingTextFits { get { return TextRenderer.MeasureText(hearingNote.Text, hearingNote.Font).Width <= hearingNote.ClientSize.Width; } }
        internal bool HearingWarning { get { return hearingNote.ForeColor == Hot; } }
        internal string HearingButtons { get { return string.Join(" | ", Array.ConvertAll(hearingActions, HearingStatus.Name)); } }
        internal string HearingShown { get { return hearingChoice.Text; } }

        internal void PressHearing(int button) { hearingFixes[button].PerformClick(); }

        internal bool HearingActionsFit
        {
            get
            {
                if (string.IsNullOrEmpty(hearingNote.AccessibleName) || hearingNote.Bottom > muteShortcut.Top) return false;
                if (hearingActions.Length > 0 && (hearingNote.Bottom > hearingFixes[0].Top || hearingFixes[0].Bottom > muteShortcut.Top)) return false;
                for (int i = 0; i < hearingFixes.Length; i++)
                    if (hearingFixes[i].Visible != (i < hearingActions.Length) || i < hearingActions.Length && (string.IsNullOrEmpty(hearingFixes[i].AccessibleName) || hearingFixes[i].Text != HearingStatus.Name(hearingActions[i]))) return false;
                return !hearingFixes[0].Bounds.IntersectsWith(hearingFixes[1].Bounds);
            }
        }

        internal bool HearingChoiceFits { get { return !Folded(hearingChoice) && !Hidden(hearingChoice) && !string.IsNullOrEmpty(hearingChoice.AccessibleName); } }

        internal string MicStatus { get { return "mic | " + mics.Text + " | " + (keyHelp.Visible ? "help" : "-") + " | " + (shortcut.Visible ? (shortcutReady ? "keys" : "taken") : "-") + " | " + (muteShortcut.Visible ? (muteReady ? "mute" : "taken") : "-"); } }

        // Windows' Online speech recognition switch is hard to find (Baguettboy couldn't), so a note that asks for it opens its page.
        internal void VoiceNote(string text, bool problem)
        {
            speechFix = text != null && text != hearingNote.Text && text.IndexOf("Online speech recognition", StringComparison.Ordinal) >= 0;
            voiceNote.Text = speechFix ? text + OpenSpeech : text;
            voiceNote.ForeColor = problem ? Hot : Faint;
            voiceNote.Cursor = speechFix ? Cursors.Hand : Cursors.Default;
        }

        internal const string OpenSpeech = " Click to open it.";
        internal bool SpeechFixShown { get { return speechFix && voiceNote.Cursor == Cursors.Hand && voiceNote.Text.EndsWith(OpenSpeech, StringComparison.Ordinal); } }

        internal string VoiceNoteText { get { return voiceNote.Text; } }
        internal bool VoiceNoteWarning { get { return voiceNote.ForeColor == Hot; } }

        internal void UpdateNote(string text, bool problem)
        {
            updateNote.Text = text;
            updateNote.ForeColor = problem ? Hot : Faint;
        }

        internal string UpdateStatus { get { return updateNote.Text; } }

        internal string VoiceStatus { get { return voiceNote.Text + " | " + voiceEntry.Box.Text + " | " + speechModels.Text + " | " + connect.Text; } }
        private void Connect()
        {
            string typed = keyEntry.Box.Text.Trim();
            if (typed.Length > 0) { keyEntry.Box.Text = ""; capri.ConnectVoice(typed); }
            else if (keySaved) capri.ForgetVoice();
            else keyEntry.Box.Focus();
        }

        private void Relabel()
        {
            bool forget = keySaved && keyEntry.Box.Text.Trim().Length == 0;
            connect.Text = forget ? "Forget" : "Connect";
            connect.Accent = forget ? Hot : Rose;
            connect.Invalidate();
        }

        // Says whose personality she's using: theirs once personality.md has something of theirs in it. Skipped lines or a file
        // she can't read turn the key hot.
        internal void ShowPersonality()
        {
            string note = Brain.PersonalityNote;
            personalityKey.Text = Brain.PersonalityYours ? "Your personality" : "Her personality";
            personalityKey.Accent = note.Length > 0 && note != Brain.LongNote ? Hot : Brain.PersonalityYours ? Cyan : Rose;
            personalityKey.Invalidate();
        }

        private void ShowPersonalityMenu()
        {
            personalityMenu.Items.Clear();
            personalityMenu.Items.Add(Choice("Edit personality.md", false, delegate { capri.OpenPersonality(); }));
            ToolStripMenuItem reset = Choice("Reset to her default", false, delegate { capri.ResetPersonality(); });
            reset.Enabled = Brain.PersonalityPath != null && System.IO.File.Exists(Brain.PersonalityPath);
            personalityMenu.Items.Add(reset);
            string note = Brain.PersonalityNote;
            if (note.Length > 0)
            {
                ToolStripMenuItem told = Choice(note, false, null);
                told.Enabled = false;
                personalityMenu.Items.Add(told);
            }
            personalityMenu.Show(personalityKey, new Point(0, personalityKey.Height));
        }

        internal string PersonalityShown { get { return personalityKey.Text + (Brain.PersonalityNote.Length > 0 ? " | " + Brain.PersonalityNote : ""); } }

        // Clicks its menu item that starts with this text, as they would. False if it isn't there or can't be clicked.
        internal bool PickPersonality(string text)
        {
            ShowPersonalityMenu();
            foreach (ToolStripItem item in personalityMenu.Items)
                if (item.Text.StartsWith(text, StringComparison.Ordinal) && item.Enabled) { personalityMenu.Close(); item.PerformClick(); return true; }
            personalityMenu.Close();
            return false;
        }

        // Back from editing personality.md in Notepad, her personality key catches up.
        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            ShowPersonality();
            if (loaded) capri.RefreshHearingDevices();
        }

        private static ToolStripMenuItem Choice(string text, bool chosen, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, click);
            item.Checked = chosen;
            return item;
        }

        // Her dropdowns aren't the Mainframe's controls, so a new look reaches them through this list. A sheet's go when the
        // sheet does, and a language group's when the item it hung from is rebuilt.
        private readonly List<ContextMenuStrip> dropdowns = new List<ContextMenuStrip>();

        internal ContextMenuStrip[] Dropdowns
        {
            get
            {
                dropdowns.RemoveAll(delegate(ContextMenuStrip strip) { return strip.IsDisposed || (strip.OwnerItem != null && strip.OwnerItem.IsDisposed); });
                return dropdowns.ToArray();
            }
        }

        // Blackwall's red. Black, White and Grey turn it into a grey too dim to read on its box, so they use their Hot.
        internal static Color BlackwallInk { get { return Neon.Mono ? Neon.Hot : Neon.Tone(255, 0, 70); } }

        private ContextMenuStrip Dropdown()
        {
            ContextMenuStrip strip = new ContextMenuStrip();
            strip.Renderer = new MenuLook();
            strip.BackColor = Ground;
            strip.ForeColor = White;
            // Its own copy: rescaling disposes the Mainframe's font, and a menu holding that one throws the next time it draws (nathanZW, #78).
            Font own = new Font(Font, Font.Style);
            strip.Font = own;
            strip.Disposed += delegate { own.Dispose(); };
            strip.ShowImageMargin = false;
            strip.ShowCheckMargin = true;
            dropdowns.RemoveAll(delegate(ContextMenuStrip old) { return old.IsDisposed || (old.OwnerItem != null && old.OwnerItem.IsDisposed); });
            dropdowns.Add(strip);
            return strip;
        }

        private Entry Input(int x, int y, int width, int height = 28)
        {
            Entry entry = new Entry();
            entry.Bounds = new Rectangle(x, y, width, height);
            Controls.Add(entry);
            return entry;
        }

        private void Step(int index, string mark, string text, Color colour)
        {
            stepText[index] = text;
            steps[index].Text = mark + "  " + text;
            steps[index].ForeColor = colour;
        }

        // Her buttons' semi-bold type, shared by every one on the Mainframe.
        private readonly Font strong = Type(9f, true);

        private Label Caption(string text, int x, int y, Font font, Color colour)
        {
            Label label = new Note();
            label.Text = text;
            label.Font = font;
            label.ForeColor = colour;
            label.BackColor = Ground;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            Controls.Add(label);
            return label;
        }

        private Key Make(string text, int x, int y, int width, int height, EventHandler click, string topic = null)
        {
            Key key = new Key();
            key.Text = text;
            key.Font = strong;
            key.Bounds = new Rectangle(x, y, width, height);
            key.Click += click;
            Controls.Add(key);
            Explain(key, topic ?? text);
            return key;
        }

        private sealed class Note : Label
        {
            // A note that takes the mouse, like the Mods status line, shows the rest of a line cut short with "..." on hover.
            internal bool Hovers;

            internal Note() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ScaleOf(this)); }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084 && !Hovers) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        // A section's title on its band: a dark strip under a teal rule, with a red tick and the title in white. Every control in
        // its row draws the band behind itself too. Span is the band's width in design px, from 14 px left of the title; a
        // window's own title, like MAINFRAME's on a sheet, has none. A heading's " >" stays in its text but isn't drawn.
        private sealed class Head : Label
        {
            internal int Span;

            internal Head()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                AutoSize = true;
            }

            internal RectangleF Band(float s) { return new RectangleF(Left - 14 * s, Top - 5 * s, Span * s, 24 * s); }

            internal string Shown { get { return Text.EndsWith(" >", StringComparison.Ordinal) ? Text.Substring(0, Text.Length - 2) : Text; } }

            public override Size GetPreferredSize(Size proposed)
            {
                Size text = TextRenderer.MeasureText(Shown, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                return new Size(text.Width + Padding.Horizontal, text.Height + Padding.Vertical);
            }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ScaleOf(this)); }

            protected override void OnPaint(PaintEventArgs e)
            {
                TextRenderer.DrawText(e.Graphics, Shown, Font, ClientRectangle, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        // A strip along her top or bottom edge that keeps her frame whole over settings scrolled under it.
        private sealed class Rim : Label
        {
            internal Rim()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                AutoSize = false;
                BackColor = Ground;
                Visible = false;
            }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ((Mainframe)Parent).Unit()); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Mainframe owner = (Mainframe)Parent;
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.TranslateTransform(-Left, -Top);
                Edging(e.Graphics, owner, owner.Unit());
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        // A button: a dark box with a red rim and its bottom right corner clipped. Cyan ones are the step to take, like
        // Settings or Use this server; a picked one, like Layers while they're on, is solid cyan; red ones end something.
        // A chevron makes it a dropdown, its choice in cyan beside a ▽.
        private sealed class Key : Button, Tinted
        {
            public void Recolour(Func<Color, Color> swap) { Accent = swap(Accent); }

            internal bool Ellipsis;
            internal Color Accent = Rose;
            private bool dimmed;
            internal bool Dimmed { get { return dimmed; } set { dimmed = value; Invalidate(); } }
            internal bool Loud;
            internal bool Picked;
            internal bool StateValue;
            internal bool Chevron;
            internal int Glyph;
            internal double Meter = -1;
            // Heart puts a beating heart before its text, like Support's; Swell is the heart's size and Beat how lit its rim is.
            internal bool Heart;
            internal float Swell = 1, Beat;
            private bool hot;
            private bool down;

            internal Key()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
            }

            internal void Pick(bool on) { Picked = on; AccessibilityNotifyClients(AccessibleEvents.StateChange, -1); Invalidate(); }

            protected override AccessibleObject CreateAccessibilityInstance() { return new KeyAccess(this); }

            private sealed class KeyAccess : ControlAccessibleObject
            {
                private readonly Key key;

                internal KeyAccess(Key owner) : base(owner) { key = owner; }

                public override string Value { get { return key.StateValue ? key.Picked ? "On" : "Off" : base.Value; } set { base.Value = value; } }

                public override AccessibleStates State { get { return base.State | (key.StateValue && key.Picked ? AccessibleStates.Checked : AccessibleStates.None); } }
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }

            protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float line = Math.Max(1f, s);
                RectangleF box = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                float x = Width / 2f;
                float y = Height / 2f;
                if (Glyph != 0)
                {
                    if (hot) using (GraphicsPath plate = Cut(box, 0, 0, 4 * s, 0)) using (Brush fill = new SolidBrush(down ? Pressed : Hover)) graphics.FillPath(fill, plate);
                    using (Pen pen = new Pen(hot ? (Glyph == 2 ? Hot : Cyan) : Faint, 1.5f * s))
                    {
                        if (Glyph == 1) graphics.DrawLine(pen, x - 5 * s, y, x + 5 * s, y);
                        else
                        {
                            graphics.DrawLine(pen, x - 4.5f * s, y - 4.5f * s, x + 4.5f * s, y + 4.5f * s);
                            graphics.DrawLine(pen, x - 4.5f * s, y + 4.5f * s, x + 4.5f * s, y - 4.5f * s);
                        }
                    }
                    return;
                }
                bool lit = hot || (Focused && ShowFocusCues);
                Color face, rim, ink;
                if (!Enabled || Dimmed) { face = Field; rim = Neon.Tone(52, 22, 32); ink = Neon.Tone(112, 94, 104); }
                else if (Picked) { face = hot ? Neon.Tone(160, 248, 251) : Cyan; rim = face; ink = OnInk; }
                else if (Accent == Cyan) { face = down ? Neon.Tone(18, 62, 67) : hot ? Neon.Tone(13, 45, 49) : Deep; rim = lit ? Cyan : Loud ? DeepEdge : Neon.Tone(28, 84, 92); ink = Cyan; }
                else if (Accent == Hot) { face = down ? Neon.Tone(72, 18, 30) : hot ? Neon.Tone(54, 16, 28) : Neon.Tone(34, 12, 22); rim = lit ? Neon.Tone(255, 124, 122) : Hot; ink = Neon.Tone(255, 166, 162); }
                else if (Heart) { face = down ? Pressed : hot ? Neon.Tone(46, 18, 34) : Toward(Neon.Tone(32, 14, 24), Neon.Tone(54, 18, 32), Beat); rim = lit ? Rose : Toward(Neon.Tone(156, 40, 56), Rose, Beat); ink = hot ? Neon.Tone(255, 194, 197) : Neon.Tone(255, 138, 144); }
                else { face = down ? Pressed : hot ? Hover : Field; rim = lit ? Hot : Edge; ink = Chevron ? Cyan : Accent; }
                using (GraphicsPath plate = Cut(box, 0, 0, 5 * s, 0))
                {
                    using (Brush brush = new SolidBrush(face)) graphics.FillPath(brush, plate);
                    using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, plate);
                }
                if (Chevron) Drop(graphics, Width - 19 * s, y, s, Enabled && !Dimmed ? Cyan : ink, hot);
                Rectangle area = Chevron ? new Rectangle((int)(10 * s), 0, Width - (int)(32 * s), Height) : ClientRectangle;
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | (Chevron ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
                if (Heart && Enabled)
                {
                    // The heart and the text are centred together, 5 px apart.
                    int wide = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding).Width;
                    float left = (Width - (17 * s + wide)) / 2;
                    DrawHeart(graphics, left + 6 * s, y, s * Swell);
                    area = new Rectangle((int)(left + 17 * s), 0, Width - (int)(left + 17 * s), Height);
                    flags = (flags & ~TextFormatFlags.HorizontalCenter) | TextFormatFlags.Left;
                }
                TextRenderer.DrawText(graphics, Text, Font, area, ink, flags);
                if (Meter >= 0) using (Brush meter = new SolidBrush(Cyan)) graphics.FillRectangle(meter, 3 * s, Height - 4 * s, (float)((Width - 6 * s) * Math.Max(0, Math.Min(1, Meter))), 2 * s);
            }

            private static Color Toward(Color from, Color to, float amount)
            {
                return Color.FromArgb(from.R + (int)((to.R - from.R) * amount), from.G + (int)((to.G - from.G) * amount), from.B + (int)((to.B - from.B) * amount));
            }

            // A 12 by 11 px heart in rose, centred on middle and drawn at size.
            private static void DrawHeart(Graphics graphics, float middleX, float middleY, float size)
            {
                PointF[] points =
                {
                    new PointF(6, 10.4f), new PointF(6, 10.4f), new PointF(0.8f, 7), new PointF(0.8f, 3.8f),
                    new PointF(0.8f, 2), new PointF(2.2f, 0.8f), new PointF(3.7f, 0.8f),
                    new PointF(4.8f, 0.8f), new PointF(5.6f, 1.5f), new PointF(6, 2.3f),
                    new PointF(6.4f, 1.5f), new PointF(7.2f, 0.8f), new PointF(8.3f, 0.8f),
                    new PointF(9.8f, 0.8f), new PointF(11.2f, 2), new PointF(11.2f, 3.8f),
                    new PointF(11.2f, 7), new PointF(6, 10.4f), new PointF(6, 10.4f)
                };
                for (int i = 0; i < points.Length; i++) points[i] = new PointF(middleX + (points[i].X - 6) * size, middleY + (points[i].Y - 5.6f) * size);
                using (GraphicsPath heart = new GraphicsPath())
                using (Brush fill = new SolidBrush(Rose))
                {
                    heart.AddBeziers(points);
                    heart.CloseFigure();
                    graphics.FillPath(fill, heart);
                }
            }
        }

        // A dropdown's ▽, drawn as an outline and filled while the mouse is on it.
        private static void Drop(Graphics graphics, float left, float middle, float s, Color colour, bool filled)
        {
            PointF[] mark = { new PointF(left, middle - 3 * s), new PointF(left + 9 * s, middle - 3 * s), new PointF(left + 4.5f * s, middle + 3.5f * s) };
            if (filled) using (Brush brush = new SolidBrush(colour)) graphics.FillPolygon(brush, mark);
            using (Pen pen = new Pen(colour, 1.3f * s)) { pen.LineJoin = LineJoin.Miter; graphics.DrawPolygon(pen, mark); }
        }

        private sealed class Entry : Control
        {
            internal readonly TextBox Box = new TextBox();
            internal event EventHandler Drop;
            private bool chevron;
            private string cue = "";
            private bool hot;

            internal Entry()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Ground;
                Cursor = Cursors.IBeam;
                Box.BorderStyle = BorderStyle.None;
                Box.BackColor = Well;
                Box.ForeColor = White;
                Box.GotFocus += delegate { Invalidate(); };
                Box.LostFocus += delegate { Invalidate(); };
                Box.HandleCreated += delegate { ShowCue(); };
                Controls.Add(Box);
            }

            internal string Cue { get { return cue; } set { cue = value ?? ""; ShowCue(); } }

            internal bool Chevron { get { return chevron; } set { chevron = value; PerformLayout(); Invalidate(); } }

            private float Unit() { return ScaleOf(this); }

            private bool OnChevron(int x) { return Chevron && x >= Width - 28 * Unit(); }

            private void ShowCue() { if (Box.IsHandleCreated) Native.SendMessage(Box.Handle, 0x1501, new IntPtr(1), cue); }

            protected override void OnLayout(LayoutEventArgs e)
            {
                base.OnLayout(e);
                float s = Unit();
                int left = (int)(10 * s);
                int right = (int)((Chevron ? 28 : 10) * s);
                int height = Box.PreferredHeight;
                Box.SetBounds(left, (Height - height) / 2, Math.Max(1, Width - left - right), height);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                bool over = OnChevron(e.X);
                Cursor = over ? Cursors.Hand : Cursors.IBeam;
                if (over != hot) { hot = over; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    if (!OnChevron(e.X)) Box.Focus();
                    else if (Drop != null) Drop(this, EventArgs.Empty);
                }
                base.OnMouseDown(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = Unit();
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float line = Math.Max(1f, s);
                using (GraphicsPath plate = Cut(new RectangleF(line / 2, line / 2, Width - line, Height - line), 0, 0, 5 * s, 0))
                {
                    using (Brush fill = new SolidBrush(Well)) graphics.FillPath(fill, plate);
                    using (Pen pen = new Pen(Box.Focused ? Cyan : WellEdge, line)) graphics.DrawPath(pen, plate);
                }
                if (Chevron) Mainframe.Drop(graphics, Width - 19 * s, Height / 2f, s, Cyan, hot);
            }
        }

        internal sealed class NoteWindow : Form, LookWindow
        {
            private readonly WrittenNote note;
            private readonly TextBox draft;
            private readonly Label status;
            private readonly Button close, reload;
            private bool discard;

            internal NoteWindow(WrittenNote page, string title)
            {
                note = page;
                Text = title;
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                MinimizeBox = false;
                BackColor = Ground;
                ForeColor = White;
                Font = Look.Mono(10f, FontStyle.Regular, GraphicsUnit.Point);
                ClientSize = new Size(580, 460);
                MinimumSize = new Size(380, 300);
                Label path = new Label { Text = note.Path, Bounds = new Rectangle(16, 14, 548, 24), AutoEllipsis = true, ForeColor = Faint, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Note file" };
                draft = new TextBox { Text = note.Text, Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = true, ScrollBars = ScrollBars.Vertical, MaxLength = int.MaxValue, Bounds = new Rectangle(16, 44, 548, 324), BorderStyle = BorderStyle.FixedSingle, BackColor = Field, ForeColor = White, Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right, AccessibleName = title };
                status = new Label { Bounds = new Rectangle(16, 376, 548, 34), ForeColor = Cyan, Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right };
                Button save = DialogButton("Save", 16, 414, 100, Cyan);
                reload = DialogButton("Reload", 124, 414, 164, Edge);
                close = DialogButton("Close", 464, 414, 100, Edge);
                save.Anchor = reload.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
                close.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
                save.Click += delegate { SaveDraft(); };
                reload.Click += delegate { ReloadNote(true); };
                close.Click += delegate { discard = true; Close(); };
                draft.TextChanged += delegate { close.Text = Dirty ? "Discard" : "Close"; reload.Text = Dirty ? "Discard + reload" : "Reload"; status.Text = Dirty ? "Unsaved changes." : ""; };
                FormClosing += delegate(object sender, FormClosingEventArgs e)
                {
                    if (!discard && Dirty && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; status.Text = "Save your changes, or press Discard."; }
                };
                Controls.AddRange(new Control[] { path, draft, status, save, reload, close });
            }

            private bool Dirty { get { return draft.Text.Replace("\r\n", "\n") != note.Text.Replace("\r\n", "\n"); } }
            internal string Draft { get { return draft.Text; } set { draft.Text = value; } }
            internal string Status { get { return status.Text; } }

            internal bool SaveDraft()
            {
                string problem = note.Save(draft.Text);
                status.Text = problem ?? "Saved.";
                status.ForeColor = problem == null ? Cyan : Hot;
                close.Text = Dirty ? "Discard" : "Close";
                reload.Text = Dirty ? "Discard + reload" : "Reload";
                return problem == null;
            }

            internal void ReloadNote(bool discard = false)
            {
                if (Dirty && !discard) { status.Text = "Save your changes, or press Discard + reload."; return; }
                try { note.Reload(); draft.Text = note.Text; status.Text = "Reloaded."; }
                catch (System.IO.IOException) { status.Text = "Couldn't reload this note. Your changes are still here."; }
                catch (UnauthorizedAccessException) { status.Text = "Couldn't reload this note. Your changes are still here."; }
            }
        }

        internal sealed class PopWindow : Form, LookWindow
        {
            private readonly Companion capri;
            private readonly NumericUpDown low, high;
            private readonly Label status;

            internal PopWindow(Companion owner)
            {
                capri = owner;
                Text = "Pop-in times";
                AutoScaleMode = AutoScaleMode.Dpi;
                StartPosition = FormStartPosition.CenterParent;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.FixedDialog;
                MaximizeBox = MinimizeBox = false;
                BackColor = Ground;
                ForeColor = White;
                Font = Look.Mono(10f, FontStyle.Regular, GraphicsUnit.Point);
                ClientSize = new Size(440, 214);
                Label shortest = new Label { Text = "Shortest wait (minutes)", Bounds = new Rectangle(16, 18, 272, 24) };
                Label longest = new Label { Text = "Longest wait (minutes)", Bounds = new Rectangle(16, 56, 272, 24) };
                low = new NumericUpDown { Minimum = 1, Maximum = int.MaxValue, Value = capri.PopMin, Bounds = new Rectangle(296, 16, 128, 26), BackColor = Field, ForeColor = White, AccessibleName = "Shortest pop-in wait in minutes" };
                high = new NumericUpDown { Minimum = 1, Maximum = int.MaxValue, Value = capri.PopMax, Bounds = new Rectangle(296, 54, 128, 26), BackColor = Field, ForeColor = White, AccessibleName = "Longest pop-in wait in minutes" };
                status = new Label { Text = "Check in on me still decides whether she visits.", Bounds = new Rectangle(16, 94, 408, 64), ForeColor = Faint };
                Button save = DialogButton("Save", 16, 168, 96, Cyan);
                Button preset = DialogButton("Use preset", 120, 168, 128, Edge);
                Button close = DialogButton("Close", 328, 168, 96, Edge);
                save.Click += delegate { if (Apply()) Close(); };
                preset.Click += delegate { capri.SetPopPace(capri.PopPace); Close(); };
                close.Click += delegate { Close(); };
                AcceptButton = save;
                CancelButton = close;
                Controls.AddRange(new Control[] { shortest, longest, low, high, status, save, preset, close });
            }

            internal int Low { get { return (int)low.Value; } set { low.Value = value; } }
            internal int High { get { return (int)high.Value; } set { high.Value = value; } }
            internal bool Apply()
            {
                if (capri.SetPopRange(Low, High)) return true;
                status.Text = "The longest wait must be at least the shortest wait.";
                status.ForeColor = Hot;
                return false;
            }
        }

        private sealed class Readout
        {
            private readonly Slider slider;

            internal Readout(Slider slider) { this.slider = slider; }

            internal string Text { get { return slider.Shown; } set { slider.Shown = value; } }

            internal Color ForeColor { get { return slider.Ink; } set { slider.Ink = value; } }

            internal Rectangle Bounds { get { return slider.Bounds; } }
        }

        // Her dropdowns: a dark wine menu with a red rim round the choice under the mouse.
        private sealed class MenuColors : ProfessionalColorTable
        {
            internal static Color Back { get { return Neon.MenuBack; } }
            public override Color ToolStripDropDownBackground { get { return Back; } }
            public override Color ImageMarginGradientBegin { get { return Back; } }
            public override Color ImageMarginGradientMiddle { get { return Back; } }
            public override Color ImageMarginGradientEnd { get { return Back; } }
            public override Color MenuBorder { get { return Edge; } }
            public override Color MenuItemSelected { get { return Neon.Tone(48, 18, 32); } }
            public override Color MenuItemBorder { get { return Hot; } }
            public override Color CheckBackground { get { return DeepEdge; } }
            public override Color CheckSelectedBackground { get { return Neon.Tone(60, 160, 172); } }
            public override Color CheckPressedBackground { get { return Neon.Tone(60, 160, 172); } }
            public override Color SeparatorDark { get { return Edge; } }
            public override Color SeparatorLight { get { return Back; } }
        }

        // Her menus, the right-click one and the Mainframe's dropdowns: a dark wine panel cut at the corners with a red rim and
        // hot red strokes over the two big corners, like her windows. The choice under the mouse sits on a band with a red tick
        // and its words go cyan, ticks are small cyan boxes, a submenu has a ▷, and a menu's title, like CAPRI / 0.7.7, is rose.
        internal sealed class MenuLook : ToolStripProfessionalRenderer
        {
            internal static readonly object Title = new object();
            internal static Color Panel { get { return MenuColors.Back; } }
            internal static Color Ink { get { return White; } }
            private static Color Chosen { get { return Neon.Tone(48, 18, 32); } }

            internal MenuLook() : base(new MenuColors()) { RoundedEdges = false; }

            private static float Unit(ToolStrip strip) { return strip.DeviceDpi / 96f; }

            protected override void Initialize(ToolStrip strip)
            {
                base.Initialize(strip);
                ToolStripDropDown drop = strip as ToolStripDropDown;
                if (drop == null) return;
                drop.DropShadowEnabled = false;
                drop.SizeChanged += delegate { Shape(drop); };
                Shape(drop);
            }

            // Cuts a menu to its clipped corners: big at the top left and bottom right, small at the others.
            private static void Shape(ToolStripDropDown drop)
            {
                if (drop.Width <= 0 || drop.Height <= 0) return;
                float s = Unit(drop);
                Neon.Clip(drop, 10 * s, 4 * s);
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                if (!(e.ToolStrip is ToolStripDropDown)) { base.OnRenderToolStripBackground(e); return; }
                float s = Unit(e.ToolStrip);
                Size size = e.ToolStrip.Size;
                using (Brush ground = new SolidBrush(Panel)) e.Graphics.FillRectangle(ground, 0, 0, size.Width, size.Height);
                float glow = Math.Min(size.Height, 90 * s);
                using (LinearGradientBrush wine = new LinearGradientBrush(new RectangleF(0, -1, 16, glow + 2), Glow, Panel, LinearGradientMode.Vertical))
                {
                    wine.Blend = Fade;
                    e.Graphics.FillRectangle(wine, 0, 0, size.Width, glow);
                }
            }

            protected override void OnRenderImageMargin(ToolStripRenderEventArgs e)
            {
                if (!(e.ToolStrip is ToolStripDropDown)) base.OnRenderImageMargin(e);
            }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                if (!(e.ToolStrip is ToolStripDropDown)) { base.OnRenderToolStripBorder(e); return; }
                float s = Unit(e.ToolStrip);
                Neon.Frame(e.Graphics, e.ToolStrip.Size, s, 10 * s, 4 * s, 30 * s, 18 * s);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!(e.ToolStrip is ToolStripDropDown)) { base.OnRenderMenuItemBackground(e); return; }
                ToolStripItem item = e.Item;
                if (!item.Enabled || !(item.Selected || item.Pressed)) return;
                Graphics graphics = e.Graphics;
                float s = Unit(e.ToolStrip);
                RectangleF band = new RectangleF(4 * s, 1, item.Width - 8 * s, item.Height - 2);
                SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath plate = Cut(band, 0, 0, 5 * s, 0))
                using (Brush fill = new SolidBrush(Chosen))
                    graphics.FillPath(fill, plate);
                graphics.SmoothingMode = was;
                using (Brush tick = new SolidBrush(Hot)) graphics.FillRectangle(tick, band.Left, band.Top + 4 * s, 2.5f * s, Math.Max(1, band.Height - 8 * s));
            }

            // Drawn here rather than by the base, which greys out every disabled item, the menu's rose title too.
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                ToolStripItem item = e.Item;
                Color colour = item.Tag == Title ? Rose : !item.Enabled ? Faint : (item.Selected || item.Pressed) && e.ToolStrip is ToolStripDropDown ? Cyan : e.TextColor;
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, e.TextRectangle, colour, e.TextFormat);
            }

            protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
            {
                if (!(e.ToolStrip is ToolStripDropDown)) { base.OnRenderItemCheck(e); return; }
                Graphics graphics = e.Graphics;
                float s = Unit(e.ToolStrip);
                Rectangle area = e.ImageRectangle;
                float side = Math.Max(6 * s, Math.Min(area.Width, area.Height) - 4 * s);
                RectangleF box = new RectangleF(area.Left + (area.Width - side) / 2f, area.Top + (area.Height - side) / 2f, side, side);
                SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath plate = Cut(box, 0, 0, 3 * s, 0))
                using (Brush fill = new SolidBrush(e.Item.Enabled ? Cyan : Faint))
                    graphics.FillPath(fill, plate);
                using (Pen tick = new Pen(OnInk, 1.6f * s))
                {
                    tick.LineJoin = LineJoin.Miter;
                    graphics.DrawLines(tick, new[] { new PointF(box.Left + side * 0.22f, box.Top + side * 0.52f), new PointF(box.Left + side * 0.42f, box.Top + side * 0.72f), new PointF(box.Left + side * 0.78f, box.Top + side * 0.3f) });
                }
                graphics.SmoothingMode = was;
            }

            protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
            {
                ToolStrip strip = e.Item != null ? e.Item.Owner : null;
                if (!(strip is ToolStripDropDown) || (e.Direction != ArrowDirection.Right && e.Direction != ArrowDirection.Left)) { base.OnRenderArrow(e); return; }
                Graphics graphics = e.Graphics;
                float s = Unit(strip);
                Rectangle area = e.ArrowRectangle;
                float x = area.Left + area.Width / 2f - 3 * s, y = area.Top + area.Height / 2f;
                bool right = e.Direction == ArrowDirection.Right;
                PointF[] mark = right
                    ? new[] { new PointF(x, y - 4 * s), new PointF(x + 6 * s, y), new PointF(x, y + 4 * s) }
                    : new[] { new PointF(x + 6 * s, y - 4 * s), new PointF(x, y), new PointF(x + 6 * s, y + 4 * s) };
                bool lit = e.Item != null && e.Item.Enabled && (e.Item.Selected || e.Item.Pressed);
                SmoothingMode was = graphics.SmoothingMode;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (lit) using (Brush fill = new SolidBrush(Cyan)) graphics.FillPolygon(fill, mark);
                using (Pen pen = new Pen(lit ? Cyan : Faint, 1.3f * s)) { pen.LineJoin = LineJoin.Miter; graphics.DrawPolygon(pen, mark); }
                graphics.SmoothingMode = was;
            }

            protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
            {
                if (!(e.ToolStrip is ToolStripDropDown) || e.Vertical) { base.OnRenderSeparator(e); return; }
                float s = Unit(e.ToolStrip);
                using (Brush line = new SolidBrush(Edge)) e.Graphics.FillRectangle(line, 10 * s, e.Item.Height / 2, e.Item.Width - 20 * s, Math.Max(1, (int)s));
            }
        }

        private sealed class ProviderSheet : Form, LookWindow
        {
            private readonly Mainframe owner;
            private readonly Entry address;
            private readonly Entry key;
            private readonly Key saved;
            private readonly Label note;
            internal readonly float Zoom;

            internal ProviderSheet(Mainframe owner, float zoom)
            {
                this.owner = owner;
                Zoom = zoom;
                Text = "Brain provider";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoScaleDimensions = new SizeF(96, 96);
                Font = Mainframe.Type(9.5f);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                ClientSize = new Size(Wide, 356);
                Heading(this, "BRAIN PROVIDER >", new Point(24, 15), Display(14f), Rose, 0);
                Key close = Make("", 330, 20, 26, delegate { Close(); });
                close.Height = 22;
                close.Glyph = 2;
                close.AccessibleName = "Close";
                CancelButton = close;
                Caption("OpenAI-compatible server or provider.", 52, 20, Faint);
                Caption("Address", 80, 18, Rose);
                address = Input(100, "http://localhost:1234/v1", "Provider base URL");
                Caption("Key", 136, 18, Rose);
                key = Input(156, "Optional. Blank means no key.", "Provider API key, optional");
                key.Box.UseSystemPasswordChar = true;
                address.Box.TextChanged += delegate { key.Box.Clear(); Kept(); };
                key.Box.TextChanged += delegate { Kept(); };
                Caption("Keys go only to this address.\nThey stay encrypted on this computer.", 196, 36, Faint);
                Key connect = Make("Connect", 28, 240, 104, delegate { Connect(); });
                connect.Accent = Cyan;
                connect.Loud = true;
                saved = Make("Use saved", 140, 240, 104, delegate { key.Box.Clear(); owner.capri.UseSavedProvider(); });
                Key forget = Make("Forget", 252, 240, 104, delegate { key.Box.Clear(); address.Box.Clear(); owner.capri.ForgetProvider(); });
                forget.Accent = Hot;
                note = Caption("", 280, 60, Faint);
                AcceptButton = connect;
                key.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Connect(); } };
                FormClosed += delegate { key.Box.Clear(); };
                if (zoom != 1f) { Grow(this, zoom); Scale(new SizeF(zoom, zoom)); }
            }

            internal void Fill()
            {
                address.Box.Text = owner.capri.ProviderAddress;
                key.Box.Clear();
                Say(owner.capri.ProviderKeySaved ? "Address and key saved. Use saved to reconnect." : "Blank key means no key.");
            }

            internal void Say(string text)
            {
                note.Text = text;
                Kept();
            }

            private void Kept()
            {
                if (saved == null) return;
                string kept = owner.capri.ProviderAddress;
                saved.Enabled = kept.Length > 0 && kept == Brain.ServerAddress(address.Box.Text) && key.Box.Text.Length == 0;
            }

            internal string Status { get { return note.Text; } }

            internal bool MaskedAndEmpty { get { return key.Box.UseSystemPasswordChar && key.Box.Text.Length == 0; } }

            internal bool SavedEnabled { get { return saved.Enabled; } }

            internal void Press(string button)
            {
                foreach (Control control in Controls)
                {
                    Key key = control as Key;
                    if (key != null && key.Text == button) { key.PerformClick(); return; }
                }
                throw new Exception("That provider button was not found.");
            }

            internal void Type(string url, string secret) { address.Box.Text = url; key.Box.Text = secret; }

            internal void Connect()
            {
                string url = address.Box.Text, secret = key.Box.Text;
                key.Box.Clear();
                owner.capri.UseProvider(url, secret);
            }

            internal string Crowded()
            {
                for (int first = 0; first < Controls.Count; first++)
                {
                    Control a = Controls[first];
                    if (!ClientRectangle.Contains(a.Bounds)) return "a provider row leaves the panel";
                    for (int second = first + 1; second < Controls.Count; second++)
                        if (a.Bounds.IntersectsWith(Controls[second].Bounds)) return "provider rows overlap";
                    using (Bitmap paper = new Bitmap(1, 1))
                    using (Graphics graphics = Graphics.FromImage(paper))
                    {
                        string problem = CheckText(graphics, a, Point.Empty, null, a.Visible);
                        if (problem != null) return problem;
                    }
                }
                if (!key.Box.UseSystemPasswordChar) return "the provider key is visible";
                return null;
            }

            private Label Caption(string text, int y, int height, Color colour)
            {
                Label made = new Note { Text = text, ForeColor = colour, BackColor = Ground, Bounds = new Rectangle(28, y, 328, height) };
                Controls.Add(made);
                return made;
            }

            private Entry Input(int y, string cue, string name)
            {
                Entry made = new Entry { Bounds = new Rectangle(28, y, 328, 28), Cue = cue };
                made.Box.AccessibleName = made.AccessibleName = name;
                Controls.Add(made);
                return made;
            }

            private Key Make(string text, int x, int y, int width, EventHandler click)
            {
                Key made = new Key { Text = text, Font = Mainframe.Type(9f, true), Bounds = new Rectangle(x, y, width, 28) };
                made.Click += click;
                Controls.Add(made);
                return made;
            }

            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                float s = ScaleOf(this);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * s);
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e) { base.OnSizeChanged(e); Clip(this, ScaleOf(this)); Invalidate(true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(e.Graphics, this, ScaleOf(this));
                Edging(e.Graphics, this, ScaleOf(this));
            }

            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        // A voice model for CUSTOM LOCAL or CLOUD VOICE: a ready-made choice like Fish Audio, or their own server. Its address, an
        // optional model, voice and key, Test, and Use this server.
        private sealed class ServerSheet : Form, LookWindow
        {
            private readonly Mainframe owner;
            private readonly Label title;
            private readonly Label hint;
            private readonly Label from;
            private readonly Key service;
            private readonly ContextMenuStrip services;
            private readonly ContextMenuStrip voices;
            private readonly Label place;
            private readonly Entry url;
            private readonly Label modelName;
            private readonly Entry model;
            private readonly Label voiceName;
            private readonly Entry speaker;
            private readonly Label keyName;
            private readonly Entry key;
            private readonly Key forget;
            private readonly Key browse;
            private readonly Key test;
            private readonly Label note;
            private VoiceServer.Service picked = VoiceServer.Services[0];
            // The server their saved key belongs to, and its last characters. The key only shows as kept for that server.
            private string savedUrl;
            private string savedApi;
            private string savedTail;
            private bool addressShown = true;
            internal readonly float Zoom;

            internal ServerSheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = VoiceServer.Menu;
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Mainframe.Type(9.5f);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                ClientSize = new Size(Wide, 420);
                title = Heading(this, "", new Point(24, 15), Display(15f), Rose, 0);
                Key shut = Make("", 330, 20, 26, 22, delegate { Close(); });
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                hint = Caption("", 28, 52, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
                from = Caption("Voice from", 28, 112, Font, Rose);
                services = owner.Dropdown();
                service = Make(picked.Name, 28, 132, 328, 28, delegate { services.Show(service, new Point(0, service.Height)); });
                service.Chevron = true;
                service.AccessibleName = "Voice from";
                place = Caption("Address", 28, 168, Font, Rose);
                url = Input(28, 188, 328, VoiceServer.Address, "Voice server address, or a voice model's folder");
                url.Box.TextChanged += delegate { Kept(); };
                modelName = Caption("Model", 28, 224, Font, Rose);
                model = Input(28, 244, 160, "Optional", "Model name, optional");
                voiceName = Caption("Voice", 196, 224, Font, Rose);
                speaker = Input(196, 244, 160, "Optional", "Voice name or ID, optional");
                // Fish Audio's cloud lists their own voices under the Voice box's arrow. A model file's arrow picks a clip for Pocket TTS.
                voices = owner.Dropdown();
                speaker.Drop += delegate { if (picked.Api == VoiceServer.Files) PickClip(); else ListVoices(); };
                keyName = Caption("Key", 28, 280, Font, Rose);
                key = Input(28, 300, 244, "Optional", "Key. It goes only to this server, as a Bearer key");
                key.Box.UseSystemPasswordChar = true;
                forget = Make("Forget", 280, 300, 76, 28, delegate { owner.capri.ForgetServerKey(savedUrl, savedApi); savedTail = null; Kept(); Say("Key forgotten.", false); });
                forget.Accent = Hot;
                // A model file has no key; Browse takes its place.
                browse = Make("Browse...", 28, 300, 150, 28, delegate { Browse(); });
                browse.Visible = false;
                test = Make("Test", 28, 344, 84, 28, delegate { Try(); });
                Key use = Make("Use this server", 120, 344, 150, 28, delegate { Use(); });
                use.Accent = Cyan;
                use.Loud = true;
                AcceptButton = use;
                CancelButton = Make("Cancel", 278, 344, 78, 28, delegate { Close(); });
                note = Caption("", 28, 384, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
                // Her Mainframe size: every font, then every place and size, before Windows scales it to the screen.
                if (zoom != 1f)
                {
                    Grow(this, zoom);
                    Scale(new SizeF(zoom, zoom));
                }
                owner.Fit(this);
            }

            // CUSTOM LOCAL's choices or CLOUD VOICE's, with what they set up for that row, or else the service asked for ready to go.
            // A saved key shows only its last four characters.
            internal void Fill(bool cloud, string wanted)
            {
                string api, address, name, voice, tail;
                bool saved = owner.capri.SavedServer(cloud, out api, out address, out name, out voice, out tail);
                Text = cloud ? "Cloud voice" : "Custom local voice";
                title.Text = (cloud ? "CLOUD VOICE" : "CUSTOM LOCAL") + " >";
                ShowAddress(!cloud);
                for (int i = services.Items.Count - 1; i >= 0; i--) services.Items[i].Dispose();
                foreach (VoiceServer.Service choice in VoiceServer.Services)
                {
                    VoiceServer.Service each = choice;
                    if (Cloudy(each) == cloud) services.Items.Add(Choice(each.Name, false, delegate { Pick(each, true); }));
                }
                savedUrl = saved ? address : null;
                savedApi = api;
                savedTail = tail;
                key.Box.Text = "";
                if (saved && (wanted == null || api == wanted))
                {
                    url.Box.Text = address ?? "";
                    model.Box.Text = name ?? "";
                    speaker.Box.Text = voice ?? "";
                    Pick(VoiceServer.Find(api, address), false);
                }
                else Pick(First(cloud, wanted), true);
                Say("", false);
            }

            private static bool Cloudy(VoiceServer.Service choice) { return choice.Url != null && VoiceServer.InCloud(choice.Url, choice.Api); }

            // Fish Audio's and OpenAI's own servers never change address, so CLOUD VOICE hides the Address row and moves the rest
            // up into its place (Lachy, 29 Oct). The address is still filled in underneath. The row is 56 px at 96 DPI, scaled
            // like the boxes in it.
            private void ShowAddress(bool shown)
            {
                if (shown == addressShown) return;
                addressShown = shown;
                int step = (int)Math.Round(56f * url.Height / 28) * (shown ? 1 : -1);
                place.Visible = url.Visible = shown;
                foreach (Control control in Controls) if (control != place && control != url && control.Top >= place.Top) control.Top += step;
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + step);
            }

            // A hint longer than its four lines, like Fish Audio's with its consent line, moves Voice from and the rows under it
            // down, keeping the gap a four-line hint leaves, instead of running under them, and a shorter one brings them back up.
            // Hints only wrap once OnLoad has set their width, so before that it changes nothing.
            private void FitHint()
            {
                int top = Math.Max(hint.Top + (int)Math.Round(64f * url.Height / 28), hint.Bottom + (int)Math.Round(8f * url.Height / 28));
                int line = from.Top, step = top - line;
                if (step == 0) return;
                foreach (Control control in Controls) if (control.Top >= line) control.Top += step;
                ClientSize = new Size(ClientSize.Width, ClientSize.Height + step);
            }

            internal bool ScaledToScreen
            {
                get
                {
                    if (Math.Abs(service.Width - 328 * ScaleOf(this)) > 1 || Math.Abs(service.Height - 28 * ScaleOf(this)) > 1) return false;
                    foreach (Entry entry in new[] { url, model, speaker })
                        if (Math.Abs(entry.Box.Top - (entry.Height - entry.Box.Height) / 2) > 1 || entry.Box.Right > entry.Width) return false;
                    return true;
                }
            }

            // For the smoke test: "fits", or the first two shown controls that cross.
            internal string HintPlace
            {
                get
                {
                    for (int i = 0; i < Controls.Count; i++)
                    {
                        Control a = Controls[i];
                        if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                        for (int j = i + 1; j < Controls.Count; j++)
                        {
                            Control b = Controls[j];
                            if (!b.Visible || (b is Label && b.Text.Length == 0)) continue;
                            if (a.Bounds.IntersectsWith(b.Bounds)) return "overlap: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " / " + (b.Text.Length > 0 ? b.Text : b.AccessibleName);
                        }
                    }
                    return "fits: hint " + hint.Top + " to " + hint.Bottom + ", Voice from at " + from.Top + ", its box at " + service.Top;
                }
            }

            // The row's first choice, or in the cloud the one with that API.
            private static VoiceServer.Service First(bool cloud, string wanted)
            {
                foreach (VoiceServer.Service choice in VoiceServer.Services)
                    if (Cloudy(choice) == cloud && (wanted == null || choice.Api == wanted)) return choice;
                return VoiceServer.Services[0];
            }

            // A ready-made choice fills in its address, model and voice. The last one, any server, keeps what's typed. A model file
            // asks for its folder, a Kokoro model's language and a speaker number instead, and has no key.
            private void Pick(VoiceServer.Service choice, bool fill)
            {
                bool file = choice.Api == VoiceServer.Files;
                // A key typed for one service never goes to the next one they pick.
                bool dropped = choice != picked && key.Box.Text.Trim().Length > 0;
                if (choice != picked) key.Box.Text = "";
                picked = choice;
                service.Text = choice.Name;
                hint.Text = choice.Hint;
                FitHint();
                place.Text = file ? "Model folder" : "Address";
                url.Cue = file ? "Browse to a voice model" : VoiceServer.Address;
                modelName.Text = file ? "Language" : "Model";
                model.Cue = file ? "Kokoro only, like es" : "Optional";
                voiceName.Text = file ? "Speaker or clip" : "Voice";
                keyName.Visible = key.Visible = !file;
                browse.Visible = file;
                speaker.Cue = choice.VoiceCue;
                speaker.Chevron = file || (choice.Api == VoiceServer.Fish && choice.Url == VoiceServer.FishCloud);
                if (fill && choice.Url != null)
                {
                    url.Box.Text = choice.Url;
                    model.Box.Text = choice.Model;
                    speaker.Box.Text = choice.Speaker;
                }
                foreach (ToolStripItem item in services.Items) ((ToolStripMenuItem)item).Checked = item.Text == choice.Name;
                Kept();
                Say(!dropped ? "" : file ? "Key cleared: a model file needs none." : "Key cleared: paste the key for " + choice.Name + ".", false);
            }

            internal void Choose(string name)
            {
                foreach (ToolStripItem item in services.Items) if (item.Text == name) { item.PerformClick(); return; }
                throw new InvalidOperationException("The voice model panel has no " + name + " to pick: disposed " + IsDisposed + ", visible " + Visible + ", choices " + Choices + ".");
            }

            // Their own Fish Audio voices, listed with the key they typed or the one saved, to pick from.
            private void ListVoices()
            {
                Say("Getting your Fish Audio voices...", false);
                owner.capri.FishVoices(url.Box.Text, key.Box.Text, delegate(List<KeyValuePair<string, string>> found, string problem)
                {
                    if (IsDisposed) return;
                    if (problem != null) { Say(problem, true); return; }
                    voices.Items.Clear();
                    foreach (KeyValuePair<string, string> each in found)
                    {
                        string id = each.Key;
                        voices.Items.Add(Choice(each.Value, VoiceServer.FishVoice(speaker.Box.Text) == id, delegate { speaker.Box.Text = id; }));
                    }
                    if (voices.Items.Count > 0) voices.Items.Add(new ToolStripSeparator());
                    voices.Items.Add(Choice(NewFishVoice, false, delegate { MakeVoice(); }));
                    Say("", false);
                    voices.Show(speaker, new Point(0, speaker.Height));
                });
            }

            internal const string NewFishVoice = "New voice from a clip...";

            private void MakeVoice()
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Pick 10 to 30 seconds of a voice you have the rights and consent to use";
                    dialog.Filter = "Voice clips (*.wav;*.mp3;*.m4a;*.flac)|*.wav;*.mp3;*.m4a;*.flac|All files (*.*)|*.*";
                    if (dialog.ShowDialog(this) == DialogResult.OK) MakeVoice(dialog.FileName);
                }
            }

            // A new private Fish Audio voice from the clip; its ID goes in the Voice box.
            internal void MakeVoice(string clip)
            {
                Say("Making your Fish Audio voice...", false);
                owner.capri.MakeFishVoice(url.Box.Text, key.Box.Text, clip, delegate(string id, string problem)
                {
                    if (IsDisposed) return;
                    if (problem != null) { Say(problem, true); return; }
                    speaker.Box.Text = id;
                    Say("Made your Fish Audio voice. Press Test to hear it.", false);
                });
            }

            // Their Fish Audio voices' names in its Voice menu, for the smoke test.
            internal string Voices
            {
                get
                {
                    List<string> names = new List<string>();
                    foreach (ToolStripItem item in voices.Items) names.Add(item is ToolStripMenuItem ? item.Text + (((ToolStripMenuItem)item).Checked ? " (picked)" : "") : "-");
                    return string.Join(", ", names.ToArray());
                }
            }

            internal void PickVoice(string name)
            {
                foreach (ToolStripItem item in voices.Items) if (item.Text == name) { item.PerformClick(); return; }
                throw new InvalidOperationException("Fish Audio's voice menu has no " + name + ".");
            }

            internal void DropVoices() { ListVoices(); }

            // The clip whose voice a Pocket TTS model copies.
            private void PickClip()
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Pick a clip of a voice you have the rights and consent to use";
                    dialog.Filter = "Voice clips (*.wav;*.mp3;*.m4a;*.flac)|*.wav;*.mp3;*.m4a;*.flac|All files (*.*)|*.*";
                    if (dialog.ShowDialog(this) == DialogResult.OK) TakeClip(dialog.FileName);
                }
            }

            internal void TakeClip(string clip)
            {
                speaker.Box.Text = clip;
                Say("Pocket TTS models copy this clip's voice. Press Test to hear it.", false);
            }

            private void Browse()
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = "Pick a voice model: its .tar.bz2, or the .onnx in its folder";
                    dialog.Filter = "Voice models (*.tar.bz2;*.zip;*.onnx)|*.tar.bz2;*.zip;*.onnx|All files (*.*)|*.*";
                    if (dialog.ShowDialog(this) == DialogResult.OK) Take(dialog.FileName);
                }
            }

            // An .onnx is used where it is. An archive is unpacked into her models folder first, off the UI thread.
            internal void Take(string path)
            {
                if (path.EndsWith(".onnx", StringComparison.OrdinalIgnoreCase))
                {
                    url.Box.Text = System.IO.Path.GetDirectoryName(path);
                    Say(ModelFile.Kind(url.Box.Text) == null ? ModelFile.Missing : "", ModelFile.Kind(url.Box.Text) == null);
                    return;
                }
                browse.Enabled = false;
                Say("Unpacking " + System.IO.Path.GetFileName(path) + "...", false);
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    string folder = null, problem = null;
                    try { folder = ModelFile.Unpack(path); }
                    catch (VoiceException error) { problem = error.Message; }
                    catch (Exception error) { problem = Voice.Short("Couldn't unpack that: " + error.Message); }
                    try
                    {
                        BeginInvoke(new MethodInvoker(delegate
                        {
                            browse.Enabled = true;
                            if (folder != null) url.Box.Text = folder;
                            Say(folder != null ? "Unpacked. Press Test to hear it." : problem, folder == null);
                        }));
                    }
                    catch (InvalidOperationException) { }
                });
            }

            private void Kept()
            {
                bool kept = savedTail != null && savedUrl != null && VoiceServer.Same(savedUrl, savedApi, url.Box.Text, picked.Api);
                key.Cue = kept ? "Key saved, ends " + savedTail : picked.KeyCue;
                forget.Visible = kept;
            }

            private void Say(string text, bool problem)
            {
                note.Text = text;
                note.ForeColor = problem ? Hot : Faint;
            }

            private void Try()
            {
                test.Enabled = false;
                Say("Asking your server...", false);
                owner.capri.TestVoiceServer(url.Box.Text, model.Box.Text, speaker.Box.Text, key.Box.Text, picked.Api, delegate(string said, bool problem)
                {
                    if (IsDisposed) return;
                    test.Enabled = true;
                    Say(said, problem);
                });
            }

            private void Use()
            {
                owner.wantEleven = false;
                owner.wantCloud = null;
                owner.capri.UseVoiceServer(url.Box.Text, model.Box.Text, speaker.Box.Text, key.Box.Text, picked.Api);
                Close();
            }

            internal string Showing { get { return picked.Name + " | " + url.Box.Text + " | " + model.Box.Text + " | " + speaker.Box.Text + " | " + key.Cue + " | " + (forget.Visible ? "Forget" : "-") + " | " + note.Text; } }

            internal string Fields { get { return (addressShown ? place.Text : "no address") + " | " + modelName.Text + " | " + voiceName.Text + " | " + (key.Visible ? "Key" : "no key") + " | " + (browse.Visible ? browse.Text : "-"); } }

            // Its title and the choices in its Voice from menu, for the smoke test.
            internal string Choices
            {
                get
                {
                    List<string> names = new List<string>();
                    foreach (ToolStripItem item in services.Items) names.Add(item.Text);
                    return title.Text + " | " + string.Join(", ", names.ToArray());
                }
            }

            internal void Type(string address, string name, string voice, string secret)
            {
                url.Box.Text = address;
                model.Box.Text = name;
                speaker.Box.Text = voice;
                key.Box.Text = secret;
            }

            // Clicks a button as they would, even while the Mainframe is minimized.
            internal void Press(string button)
            {
                foreach (Control control in Controls)
                    if (control is Key && control.Text == button && control.Visible && control.Enabled) { typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(control, new object[] { EventArgs.Empty }); return; }
                throw new InvalidOperationException("The voice server panel has no " + button + " button to press: disposed " + IsDisposed + ", visible " + Visible + ", enabled " + Enabled + ", saved key " + (savedTail != null) + ", choices " + Choices + ".");
            }

            protected override void OnShown(EventArgs e)
            {
                base.OnShown(e);
                url.Box.Focus();
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    services.Dispose();
                    voices.Dispose();
                }
                base.Dispose(disposing);
            }

            // Wrapped notes and a spot over the Mainframe, once Windows has scaled the panel to the screen.
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                float s = ScaleOf(this);
                foreach (Control control in Controls) if (control is Label) control.MaximumSize = new Size((int)Math.Round(328 * s), 0);
                FitHint();
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * s);
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                float s = ScaleOf(this);
                Clip(this, s);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, s);
                Edging(graphics, this, s);
            }

            // Dragged by any spot that isn't a control, like the Mainframe.
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }

            private Label Caption(string text, int x, int y, Font font, Color colour)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = true;
                label.Location = new Point(x, y);
                Controls.Add(label);
                return label;
            }

            private Entry Input(int x, int y, int width, string cue, string name)
            {
                Entry entry = new Entry();
                entry.Bounds = new Rectangle(x, y, width, 28);
                entry.Cue = cue;
                entry.Box.AccessibleName = name;
                Controls.Add(entry);
                return entry;
            }

            private Key Make(string text, int x, int y, int width, int height, EventHandler click)
            {
                Key made = new Key();
                made.Text = text;
                made.Font = Mainframe.Type(9f, true);
                made.Bounds = new Rectangle(x, y, width, height);
                made.Click += click;
                Controls.Add(made);
                return made;
            }
        }

        // Her SECURITY window, in Settings' look (Lachy, 29 Oct): a slider of four presets, Lockdown, Guarded, Trusted and
        // Blackwall, that sets the toggles under it, and a toggle with a plain line for each kind of thing she does on their PC.
        // Flipping one makes it Custom, and a click on the slider puts the preset under its knob back. Her hands' toggles are on
        // the left and her jobs' on the right. It opens from Settings > HABITS, where Blackwall mode was. Zoom is 1 except in
        // the smoke test, which draws it at 150% and 200% too.
        private sealed class SecuritySheet : Form, LookWindow, Tinted
        {
            internal const int Wide = 900, Tall = 516, Column = 410;
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly Slider presets;
            public void Recolour(Func<Color, Color> swap) { if (Security.Preset == Security.Blackwall) presets.Ink = BlackwallInk; }
            private readonly Readout presetName;
            private readonly Label presetLine;
            private readonly Toggle[] toggles = new Toggle[Security.Count];
            private bool syncing;

            internal SecuritySheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = "Security";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Type(9f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(Wide), Z(Tall));
                Font small = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point);
                Heading(this, "SECURITY >", new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
                Caption("What she does on your PC without asking. With a toggle off she asks first, and does it when you say yes.", 28, 52, small, Faint);
                Caption("Preset", 28, 82, Type(9.5f * zoom), Rose);
                presets = new Slider(new[] { Security.Presets[0], Security.Presets[1], Security.Presets[2], Security.Presets[3] });
                presets.Font = Type(12f * zoom, true);
                presets.Bounds = Box(120, 70, 340, 36);
                presets.AccessibleName = "Security preset: Lockdown, Guarded, Trusted or Blackwall";
                presets.Changed += delegate { if (!syncing) Security.Pick(presets.Value); };
                presets.MouseUp += delegate { if (!syncing && Security.Preset == Security.Custom) Security.Pick(presets.Value); };
                Controls.Add(presets);
                presetName = new Readout(presets);
                presetLine = Caption("", 28, 110, small, Faint);
                Font head = Type(10f * zoom, true);
                Heading(this, "HER HANDS >", new Point(Z(28), Z(144)), head, White, Column + 14);
                Heading(this, "HER JOBS >", new Point(Z(52 + Column), Z(144)), head, White, Column + 14);
                Heading(this, "LISTENING >", new Point(Z(52 + Column), Z(362)), head, White, Column + 14);
                for (int i = 0; i < Security.Count; i++)
                {
                    int x = i <= Security.Mouse ? 28 : 52 + Column;
                    int y = i <= Security.Mouse ? 168 + 46 * i : i < Security.Wake ? 168 + 46 * (i - Security.Sends) : 386 + 46 * (i - Security.Wake);
                    int toggle = i;
                    Toggle box = new Toggle();
                    box.Text = Security.Names[i];
                    box.Font = Font;
                    box.Switch = 96;
                    box.Bounds = Box(x, y, Column, 22);
                    box.AccessibleName = Security.Names[i] + ". " + Security.Lines[i];
                    box.CheckedChanged += delegate { if (!syncing) Security.Set(toggle, toggles[toggle].Checked); };
                    Controls.Add(box);
                    toggles[i] = box;
                    Caption(Security.Lines[i], x, y + 24, small, Faint);
                }
                owner.Fit(this);
                Sync();
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }

            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            private Label Caption(string text, int x, int y, Font font, Color colour)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = true;
                label.Location = new Point(Z(x), Z(y));
                Controls.Add(label);
                return label;
            }

            // The toggles and preset as they are now. Custom leaves the knob on the nearest preset.
            internal void Sync()
            {
                syncing = true;
                int preset = Security.Preset;
                presets.Value = preset == Security.Custom ? Nearest() : preset;
                presetName.Text = Security.Presets[preset];
                presetName.ForeColor = preset == Security.Custom ? Neon.Amber : Cyan;
                presetLine.Text = Security.PresetLine(preset);
                presets.Torn = preset == Security.Blackwall;
                presets.Storm = preset == Security.Blackwall && owner.capri.Moves;
                presets.Ink = preset == Security.Blackwall ? BlackwallInk : Color.Empty;
                for (int i = 0; i < Security.Count; i++) toggles[i].Checked = Security.On(i);
                syncing = false;
            }

            // The preset fewest toggles away from theirs.
            private static int Nearest()
            {
                int bits = Security.Mask, best = Security.Trusted, fewest = int.MaxValue;
                for (int preset = 0; preset < Security.Custom; preset++)
                {
                    int apart = 0, differ = bits ^ Security.MaskOf(preset);
                    for (int i = 0; i < Security.Count; i++) if ((differ & (1 << i)) != 0) apart++;
                    if (apart < fewest) { fewest = apart; best = preset; }
                }
                return best;
            }

            // The preset, where the knob is, each toggle as 1 or 0 and the preset's line, for the smoke test.
            internal string Showing
            {
                get
                {
                    System.Text.StringBuilder bits = new System.Text.StringBuilder();
                    foreach (Toggle box in toggles) bits.Append(box.Checked ? '1' : '0');
                    return presetName.Text + " | " + Security.Presets[presets.Value] + " | " + bits + " | " + presetLine.Text;
                }
            }

            // A click on a toggle, the slider dragged to a preset and a click on the slider, as they'd do them.
            internal void Flip(int toggle) { toggles[toggle].Checked = !toggles[toggle].Checked; }

            internal void Slide(int preset) { presets.Slide(preset); }

            internal void Tap(int preset)
            {
                presets.Slide(preset);
                typeof(Control).GetMethod("OnMouseUp", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).Invoke(presets, new object[] { new MouseEventArgs(MouseButtons.Left, 1, 0, 0, 0) });
            }

            // Anything that overlaps, pokes out of the window or doesn't fit its own text, or null.
            internal string Crowded()
            {
                float s = ScaleOf(this);
                // A small desktop clamps the window, so check the size it was drawn for.
                Rectangle area = new Rectangle(0, 0, Math.Max(ClientSize.Width, (int)Math.Round(Wide * s)), Math.Max(ClientSize.Height, (int)Math.Round(Tall * s)));
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                    if (!area.Contains(a.Bounds) || a.Right > area.Right - (int)(16 * s)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds + " in " + ClientSize;
                    Toggle box = a as Toggle;
                    if (box != null && TextRenderer.MeasureText(box.Text, box.Font).Width > box.NameRoom) return "doesn't fit: " + box.Text + " " + TextRenderer.MeasureText(box.Text, box.Font).Width + " in " + box.NameRoom;
                    for (int j = i + 1; j < Controls.Count; j++)
                    {
                        Control b = Controls[j];
                        if (!b.Visible || (b is Label && b.Text.Length == 0)) continue;
                        if (a.Bounds.IntersectsWith(b.Bounds)) return "overlap: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " / " + (b.Text.Length > 0 ? b.Text : b.AccessibleName);
                    }
                }
                return null;
            }

            internal void Picture(string path)
            {
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            // Over the Mainframe, like their voice server's panel, and on the screen.
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                float s = ScaleOf(this);
                Clip(this, s);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, s);
                Edging(graphics, this, s);
            }

            // Esc closes it.
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            // Dragged by any spot that isn't a control, like the Mainframe.
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        // Her EXTRAS window, in SECURITY's look, over the Mainframe on a new install's first launch (Lachy, 31 Oct: so many cool
        // ones were off with nothing pointing at them). Each costs something or uses the mic, so they start off and it's their
        // pick, and all four are in Settings too.
        private sealed class ExtrasSheet : Form, LookWindow
        {
            internal const int Wide = 600, Tall = 366;
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly Toggle[] toggles = new Toggle[4];
            private bool syncing;

            internal ExtrasSheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = "Extras";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                // She's always on top, and on a new install she arrives in the middle of the screen, right where this opens.
                TopMost = true;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Type(9f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(Wide), Z(Tall));
                Font small = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point);
                Heading(this, "EXTRAS >", new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
                Caption("These start off. Turn on what you want. They're all in Settings too.", 28, 52, small, Faint);
                Extra(0, "Watch my screen", "She glances at your screen and comments. Costs a little on a paid brain.", small, delegate { owner.capri.ToggleWatch(); });
                Extra(1, "Comment on my games", "She talks about your game as you play. Costs a little on a paid brain.", small, delegate { owner.capri.SetCommentPace(toggles[1].Checked ? 2 : 0); owner.Sync(); });
                Extra(2, "Clip my games", "Keeps the last two minutes of your game in memory. Say clip that to save it.", small, delegate { owner.capri.ToggleClips(); });
                Extra(3, "Listen for Capri", "She keeps listening and answers when you say her name.", small, delegate { owner.capri.ToggleListenCapri(); });
                Key done = new Key();
                done.Text = "Done";
                done.Font = Type(9f * zoom, true);
                done.Accent = Cyan;
                done.Loud = true;
                done.Bounds = Box(Wide - 128, Tall - 44, 100, 26);
                done.Click += delegate { Close(); };
                Controls.Add(done);
                owner.Fit(this);
                Sync();
            }

            private void Extra(int row, string name, string line, Font small, Action changed)
            {
                int y = 84 + 58 * row;
                Toggle box = new Toggle();
                box.Text = name;
                box.Font = Font;
                box.Switch = 96;
                box.Bounds = Box(28, y, Wide - 56, 22);
                box.AccessibleName = name + ". " + line;
                box.CheckedChanged += delegate { if (!syncing) changed(); };
                Controls.Add(box);
                toggles[row] = box;
                Caption(line, 28, y + 24, small, Faint);
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }

            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            private void Caption(string text, int x, int y, Font font, Color colour)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = true;
                label.Location = new Point(Z(x), Z(y));
                Controls.Add(label);
            }

            internal void Sync()
            {
                syncing = true;
                toggles[0].Checked = owner.capri.Watching;
                toggles[1].Checked = owner.capri.CommentPace > 0;
                toggles[2].Checked = owner.capri.ClipsOn;
                toggles[3].Checked = owner.capri.ListenCapriOn;
                syncing = false;
            }

            // Each toggle as 1 or 0, and a click on one as they'd do it, for the smoke test.
            internal string Showing
            {
                get
                {
                    System.Text.StringBuilder bits = new System.Text.StringBuilder();
                    foreach (Toggle box in toggles) bits.Append(box.Checked ? '1' : '0');
                    return bits.ToString();
                }
            }

            internal void Flip(int row) { toggles[row].Checked = !toggles[row].Checked; }

            // Anything that overlaps, pokes out of the window or doesn't fit its own text, or null.
            internal string Crowded()
            {
                float s = ScaleOf(this);
                // A small desktop clamps the window, so check the size it was drawn for.
                Rectangle area = new Rectangle(0, 0, Math.Max(ClientSize.Width, (int)Math.Round(Wide * s)), Math.Max(ClientSize.Height, (int)Math.Round(Tall * s)));
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible) continue;
                    if (!area.Contains(a.Bounds) || a.Right > area.Right - (int)(16 * s)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds + " in " + ClientSize;
                    Toggle box = a as Toggle;
                    if (box != null && TextRenderer.MeasureText(box.Text, box.Font).Width > box.NameRoom) return "doesn't fit: " + box.Text;
                    for (int j = i + 1; j < Controls.Count; j++)
                        if (Controls[j].Visible && a.Bounds.IntersectsWith(Controls[j].Bounds)) return "overlap: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " / " + (Controls[j].Text.Length > 0 ? Controls[j].Text : Controls[j].AccessibleName);
                }
                return null;
            }

            // Over the Mainframe, like SECURITY, and on the screen.
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                float s = ScaleOf(this);
                Clip(this, s);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, s);
                Edging(graphics, this, s);
            }

            // Esc closes it.
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            // Dragged by any spot that isn't a control, like the Mainframe.
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        // Her VOICE LAYERS window, in SECURITY's look (Lachy, 29 Oct): a switch, then MAIN, her voice itself, whose pitch is
        // Settings > VOICE > Pitch and moves with it, and LAYER 1 and LAYER 2, copies of her mixed in under her, each with its
        // own pitch (half steps from -12 to +12 semitones off hers) and delay (0 to 250 ms). Letting go of a slider has her say
        // a line in it, and so does the switch. It opens from the Layers button in VOICE's heading. Zoom is 1 except in the smoke
        // test, which draws it at 150% and 200% too.
        private sealed class LayersSheet : Form, LookWindow
        {
            internal const int Wide = 760, Tall = 356, Column = 364, DelayStep = 5;
            internal readonly float Zoom;
            private static readonly string[] PitchSteps = PitchLabels();
            private static readonly string[] DelaySteps = DelayLabels();
            private readonly Mainframe owner;
            private readonly Toggle onOff;
            private readonly Slider mainPitch;
            private readonly Readout mainName;
            private readonly Slider[] pitches = new Slider[2];
            private readonly Slider[] delays = new Slider[2];
            private readonly Readout[] pitchNames = new Readout[2];
            private readonly Readout[] delayNames = new Readout[2];
            private bool syncing;

            internal LayersSheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = "Voice layers";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Type(9.5f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(Wide), Z(Tall));
                Font small = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point), head = Type(10f * zoom, true);
                Heading(this, "VOICE LAYERS >", new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
                Caption("Her voice, and two copies of it mixed in under her, each with its own pitch and delay.", 28, 52, small, Faint);
                onOff = new Toggle();
                onOff.Text = "Voice layers";
                onOff.Font = Font;
                onOff.Switch = 120;
                onOff.Bounds = Box(28, 76, 260, 22);
                onOff.AccessibleName = "Voice layers. Off, she sounds as she always has";
                onOff.CheckedChanged += delegate
                {
                    if (syncing) return;
                    owner.capri.SetVoiceLayers(owner.capri.VoiceLayers.Turned(onOff.Checked));
                    owner.ShowLayers();
                    owner.capri.HearVoiceStyle();
                };
                Controls.Add(onOff);
                Caption("Off: she sounds as she always has. On: every voice she has, ElevenLabs too.", 28, 102, small, Faint);
                Make("Hear her", Wide - 236, 76, 100, 22, delegate { owner.capri.HearVoiceStyle(); }).AccessibleName = "Hear her: she says a line in her voice as it is now";
                Make("Defaults", Wide - 128, 76, 100, 22, delegate { Defaults(); }).AccessibleName = "Defaults: Layer 1 " + VoiceFx.Layering.PitchName(VoiceFx.Layering.HigherPitch) + " and Layer 2 " + VoiceFx.Layering.PitchName(VoiceFx.Layering.LowerPitch) + " semitones, both " + VoiceFx.Layering.DelayName(VoiceFx.Layering.Behind) + " behind her";
                Heading(this, "MAIN >", new Point(Z(28), Z(136)), head, White, Wide - 28);
                Caption("Pitch", 28, 166, Font, Rose);
                mainPitch = Bar(PitchNames, 120, 163, 300, "Her voice's pitch, the same as Settings > VOICE > Pitch");
                mainName = new Readout(mainPitch);
                mainPitch.Changed += delegate
                {
                    if (syncing) return;
                    owner.capri.SetVoicePitch(mainPitch.Value - VoiceFx.MostPitch);
                    mainName.Text = PitchNames[mainPitch.Value];
                    owner.Sync();
                };
                mainPitch.Released += delegate { if (!syncing) owner.capri.HearVoiceStyle(); };
                Caption("Her voice itself. Its pitch is Settings > VOICE > Pitch, and they move together.", 28, 192, small, Faint);
                for (int layer = 1; layer <= 2; layer++)
                {
                    int x = layer == 1 ? 28 : 28 + Column, at = layer - 1, which = layer;
                    Heading(this, "LAYER " + layer + " >", new Point(Z(x), Z(228)), head, White, Column - 12);
                    Caption("Pitch", x, 258, Font, Rose);
                    pitches[at] = Bar(PitchSteps, x + 84, 255, 244, "Layer " + layer + "'s pitch, in semitones from hers");
                    pitches[at].TickEvery = 4;
                    pitchNames[at] = new Readout(pitches[at]);
                    Caption("Delay", x, 286, Font, Rose);
                    delays[at] = Bar(DelaySteps, x + 84, 283, 244, "Layer " + layer + "'s delay behind her");
                    delays[at].TickEvery = 10;
                    delayNames[at] = new Readout(delays[at]);
                    Caption(layer == 1 ? "Out of the box a little higher, just behind her." : "Out of the box lower, just behind her.", x, 314, small, Faint);
                    pitches[at].Changed += delegate
                    {
                        if (syncing) return;
                        VoiceFx.Layering now = owner.capri.VoiceLayers;
                        owner.capri.SetVoiceLayers(now.With(which, pitches[which - 1].Value / 2.0 - VoiceFx.Layering.MostPitch, now.DelayOf(which)));
                        pitchNames[which - 1].Text = VoiceFx.Layering.PitchName(owner.capri.VoiceLayers.PitchOf(which));
                    };
                    delays[at].Changed += delegate
                    {
                        if (syncing) return;
                        VoiceFx.Layering now = owner.capri.VoiceLayers;
                        owner.capri.SetVoiceLayers(now.With(which, now.PitchOf(which), delays[which - 1].Value * DelayStep));
                        delayNames[which - 1].Text = VoiceFx.Layering.DelayName(owner.capri.VoiceLayers.DelayOf(which));
                    };
                    pitches[at].Released += delegate { if (!syncing) owner.capri.HearVoiceStyle(); };
                    delays[at].Released += delegate { if (!syncing) owner.capri.HearVoiceStyle(); };
                }
                owner.Fit(this);
                Sync();
            }

            // Half steps from -12 to +12 semitones: "-12", "-11.5", ... "Same", ... "+12".
            private static string[] PitchLabels()
            {
                string[] names = new string[(int)(VoiceFx.Layering.MostPitch * 4) + 1];
                for (int i = 0; i < names.Length; i++) names[i] = VoiceFx.Layering.PitchName(i / 2.0 - VoiceFx.Layering.MostPitch);
                return names;
            }

            private static string[] DelayLabels()
            {
                string[] names = new string[VoiceFx.Layering.MostDelay / DelayStep + 1];
                for (int i = 0; i < names.Length; i++) names[i] = VoiceFx.Layering.DelayName(i * DelayStep);
                return names;
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }

            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            private Label Caption(string text, int x, int y, Font font, Color colour)
            {
                Label label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = true;
                label.Location = new Point(Z(x), Z(y));
                Controls.Add(label);
                return label;
            }

            private Slider Bar(string[] names, int x, int y, int width, string name)
            {
                Slider slider = new Slider(names);
                slider.Font = Font;
                slider.Bounds = Box(x, y, width, 22);
                slider.AccessibleName = name;
                Controls.Add(slider);
                return slider;
            }

            private Key Make(string text, int x, int y, int width, int height, EventHandler click)
            {
                Key made = new Key();
                made.Text = text;
                made.Font = Type(9f * Zoom, true);
                made.Bounds = Box(x, y, width, height);
                made.Click += click;
                Controls.Add(made);
                return made;
            }

            // The switch, MAIN's pitch and each layer as they are now. A slider already there is left alone, so one being
            // dragged still says her line when it's let go.
            internal void Sync()
            {
                syncing = true;
                VoiceFx.Layering layers = owner.capri.VoiceLayers;
                onOff.Checked = layers.On;
                Put(mainPitch, owner.capri.VoicePitch + VoiceFx.MostPitch);
                mainName.Text = PitchNames[mainPitch.Value];
                for (int at = 0; at < 2; at++)
                {
                    Put(pitches[at], (int)Math.Round((layers.PitchOf(at + 1) + VoiceFx.Layering.MostPitch) * 2));
                    pitchNames[at].Text = VoiceFx.Layering.PitchName(layers.PitchOf(at + 1));
                    Put(delays[at], (int)Math.Round(layers.DelayOf(at + 1) / (double)DelayStep));
                    delayNames[at].Text = VoiceFx.Layering.DelayName(layers.DelayOf(at + 1));
                }
                syncing = false;
            }

            private static void Put(Slider slider, int value) { if (slider.Value != value) slider.Value = value; }

            // Both layers back to how they come, on or off as they are, and a line in them.
            internal void Defaults()
            {
                owner.capri.SetVoiceLayers(new VoiceFx.Layering(owner.capri.VoiceLayers.On, VoiceFx.Layering.HigherPitch, VoiceFx.Layering.Behind, VoiceFx.Layering.LowerPitch, VoiceFx.Layering.Behind));
                Sync();
                owner.capri.HearVoiceStyle();
            }

            // "On | Normal | +2, 25 ms | -2.5, 25 ms", as the window shows it, for the smoke test.
            internal string Showing
            {
                get { return (onOff.Checked ? "On" : "Off") + " | " + mainName.Text + " | " + pitchNames[0].Text + ", " + delayNames[0].Text + " | " + pitchNames[1].Text + ", " + delayNames[1].Text; }
            }

            internal void Flip() { onOff.Checked = !onOff.Checked; }

            internal void Slide(int slider, int index) { (slider == 0 ? mainPitch : slider == 1 ? pitches[0] : slider == 2 ? delays[0] : slider == 3 ? pitches[1] : delays[1]).Slide(index); }

            // Anything that overlaps, pokes out of the window or doesn't fit its own text, or null.
            internal string Crowded()
            {
                float s = ScaleOf(this);
                // A small desktop clamps the window, so check the size it was drawn for.
                Rectangle area = new Rectangle(0, 0, Math.Max(ClientSize.Width, (int)Math.Round(Wide * s)), Math.Max(ClientSize.Height, (int)Math.Round(Tall * s)));
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                    if (!area.Contains(a.Bounds) || a.Right > area.Right - (int)(16 * s)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds + " in " + ClientSize;
                    Toggle box = a as Toggle;
                    if (box != null && TextRenderer.MeasureText(box.Text, box.Font).Width > box.NameRoom) return "doesn't fit: " + box.Text + " " + TextRenderer.MeasureText(box.Text, box.Font).Width + " in " + box.NameRoom;
                    Key key = a as Key;
                    if (key != null && key.Text.Length > 0 && TextRenderer.MeasureText(key.Text, key.Font).Width + (int)Math.Ceiling(12 * s) > key.Width) return "doesn't fit: " + key.Text + " " + TextRenderer.MeasureText(key.Text, key.Font).Width + " in " + key.Width;
                    for (int j = i + 1; j < Controls.Count; j++)
                    {
                        Control b = Controls[j];
                        if (!b.Visible || (b is Label && b.Text.Length == 0)) continue;
                        if (a.Bounds.IntersectsWith(b.Bounds)) return "overlap: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " / " + (b.Text.Length > 0 ? b.Text : b.AccessibleName);
                    }
                }
                // The widest a value can get, inside its slider's box: "-11.5" and "250 ms".
                foreach (Slider bar in new[] { pitches[0], pitches[1], delays[0], delays[1], mainPitch })
                {
                    string widest = bar == mainPitch ? "Normal" : Array.IndexOf(pitches, bar) >= 0 ? "-11.5" : "250 ms";
                    if (TextRenderer.MeasureText(widest, bar.Font).Width + (int)Math.Ceiling(24 * s) > bar.Width) return "doesn't fit: " + widest + " in " + bar.AccessibleName;
                }
                return null;
            }

            internal void Picture(string path)
            {
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            // Over the Mainframe, like SECURITY, and on the screen.
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                float s = ScaleOf(this);
                Clip(this, s);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, s);
                Edging(graphics, this, s);
            }

            // Esc closes it.
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            // Dragged by any spot that isn't a control, like the Mainframe.
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        // Her voice in your mic's setup in a window of its own (Lachy, 3 Nov: "make it clear how to set it up too"): the switch,
        // the mix's Input and Output, then Discord's steps with the exact name to pick there, and whether Discord hears it yet,
        // read from Windows each second.
        private sealed class MixSheet : Form, LookWindow
        {
            internal const int Wide = 620, Tall = 478;
            internal const string NoCable = "No virtual mic yet. Pick Output, then Get VB-CABLE (free).", NoOutput = "None yet: pick to get VB-CABLE (free)";
            internal const string Off = "Off. Turn on Her voice in your mic above.";
            internal static string NotYet { get { return CallApps.Current.Name + " isn't hearing it yet: do 1 and 2, then join a call."; } }
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly Toggle onOff;
            private readonly Key input, output;
            private readonly Label note, state, pickStep, pick, live, elsewhere;
            private readonly Head callHead;
            private readonly Label[] steps;
            private readonly System.Windows.Forms.Timer ticks = new System.Windows.Forms.Timer();
            private bool syncing;

            internal MixSheet(Mainframe owner, float zoom)
            {
                SuspendLayout();
                this.owner = owner;
                Zoom = zoom;
                Text = "Her voice in your mic";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                Font = Type(9.5f * zoom);
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                KeyPreview = true;
                ClientSize = new Size(Z(Wide), Z(Tall));
                Font small = Look.Mono(8.5f * zoom, FontStyle.Regular, GraphicsUnit.Point), head = Type(10f * zoom, true);
                Heading(this, "VOICE IN YOUR MIC >", new Point(Z(24), Z(15)), Display(15f * zoom), Rose, 0);
                Key shut = new Key();
                shut.Bounds = Box(Wide - 54, 20, 26, 22);
                shut.Glyph = 2;
                shut.AccessibleName = "Close";
                shut.Click += delegate { Close(); };
                Controls.Add(shut);
                note = Caption("Her voice joins your mic only in call mode.", 28, 52, small, Faint);
                onOff = new Toggle();
                onOff.Text = "Her voice in your mic";
                onOff.Font = Font;
                onOff.Switch = 120;
                onOff.Bounds = Box(28, 76, 300, 22);
                onOff.AccessibleName = "Her voice in your mic";
                onOff.CheckedChanged += delegate { if (!syncing) owner.capri.ToggleVoiceInMic(); };
                Controls.Add(onOff);
                Caption("Input", 28, 115, Font, Rose);
                input = Make("", 120, 112, Wide - 148, 22, delegate { owner.ShowMixInputs(input); });
                input.Chevron = true;
                input.AccessibleName = "Input: the mic that goes in with her voice";
                Caption("Output", 28, 143, Font, Rose);
                output = Make("", 120, 140, Wide - 148, 22, delegate { owner.ShowMixOutputs(output); });
                output.Chevron = true;
                output.AccessibleName = "Output: the virtual mic she plays into, which your call app hears";
                state = Caption("", 28, 174, Font, Faint);
                callHead = Heading(this, "IN DISCORD >", new Point(Z(28), Z(208)), head, White, Wide - 28);
                steps = new Label[5];
                steps[0] = Caption("", 28, 240, Font, White);
                pickStep = Caption("2   Input Device:", 28, 266, Font, White);
                pick = Caption("", 28, 266, Font, Cyan);
                steps[1] = Caption("", 28, 292, Font, White);
                steps[2] = Caption("", 28, 318, Font, White);
                steps[3] = Caption("", 28, 344, Font, White);
                steps[4] = Caption("", 28, 370, Font, White);
                live = Caption("", 28, 404, Font, Faint);
                elsewhere = Caption("", 28, 438, Font, Faint);
                ticks.Interval = 1000;
                ticks.Tick += delegate { Sync(); };
                owner.Fit(this);
                Sync();
            }

            private int Z(int value) { return (int)Math.Round(value * Zoom); }

            private Rectangle Box(int x, int y, int width, int height) { return new Rectangle(Z(x), Z(y), Z(width), Z(height)); }

            // UseMnemonic off, so Discord's "Voice & Video" keeps its "&".
            private Label Caption(string text, int x, int y, Font font, Color colour)
            {
                Label label = new Note();
                label.UseMnemonic = false;
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = true;
                label.Location = new Point(Z(x), Z(y));
                Controls.Add(label);
                return label;
            }

            private Key Make(string text, int x, int y, int width, int height, EventHandler click)
            {
                Key made = new Key();
                made.Text = text;
                made.Font = Type(9f * Zoom, true);
                made.Bounds = Box(x, y, width, height);
                made.Click += click;
                Controls.Add(made);
                return made;
            }

            // The switch, the mix's devices, what's wrong if anything, the name to pick in Discord, and who hears it now.
            internal void Sync()
            {
                Companion capri = owner.capri;
                MixDevice there = capri.MixOutput;
                bool cable = there != null, on = capri.VoiceInMic;
                bool vb = capri.VbCableReady;
                string problem = capri.VoiceInMicProblem, setup = capri.CableSetupLine, far = MicMix.FarName(there);
                CallApp app = CallApps.Current;
                bool isDiscord = app.Name == "Discord";
                Retext(callHead, "IN " + app.Caption + " >");
                string[] lines = isDiscord ? new[] {
                    "1   User Settings (the cog by your name) > Voice & Video",
                    "3   Output Device: your headphones, like before", "4   Echo Cancellation: Off",
                    "5   Noise Suppression: None. Both can cut her voice after the first line.",
                    "6   Mic Test > Let's Check: you'll hear you, and her as she talks"
                } : new[] {
                    "1   Open the call or meeting's audio settings",
                    "3   Speaker: your headphones, like before", "4   If her voice cuts off, turn off noise filtering",
                    "5   Try a mic test in your call app",
                    "6   Join the call and check they hear both of you"
                };
                for (int i = 0; i < steps.Length; i++) Retext(steps[i], lines[i]);
                Retext(pickStep, isDiscord ? "2   Input Device:" : "2   Microphone:");
                syncing = true;
                onOff.Checked = on;
                onOff.Enabled = cable || on;
                syncing = false;
                Retext(note, cable && !vb ? CableSetup.Better + " Pick Output to get it." : "Your mic and her voice go into one virtual mic, and " + app.Name + " hears that.");
                Retext(input, capri.MixInputName ?? "No mic found");
                Retext(output, cable ? far : NoOutput);
                Retext(pick, far);
                pick.Left = pickStep.Right + (int)Math.Round(4 * ScaleOf(this));
                Retext(elsewhere, "Roblox or any other app: pick " + far + " as its mic.");
                string said;
                Color colour = Hot;
                if (!vb && setup != null) said = setup;
                else if (!cable) said = NoCable;
                else if (there.Far == null) said = MicMix.OffLine(MicMix.EndOf(there));
                else if (!on) said = Off;
                else if (problem != null) said = problem;
                else { said = "On: your mic and her voice are going in."; colour = Cyan; }
                Retext(state, said);
                state.ForeColor = colour;
                List<string> hearing = cable && on && there.Far != null ? capri.MixListeners : new List<string>();
                bool discord = hearing.Exists(delegate(string process) { return app.Has(process); });
                string apps = Apps(hearing), verb = AppNames(hearing).Count > 1 ? " hear" : " hears";
                Retext(live, !cable || !on || there.Far == null ? "" : discord ? apps + verb + " it now." : hearing.Count > 0 ? apps + verb + " it now. " + app.Name + " doesn't yet." : NotYet);
                live.ForeColor = discord ? Cyan : Faint;
            }

            // A program's everyday name: "Discord" for its test builds too, "Roblox" for its player, "OBS" for OBS Studio.
            internal static string AppName(string process)
            {
                string name = (process ?? "").ToLowerInvariant();
                if (name.StartsWith("discord", StringComparison.Ordinal)) return "Discord";
                foreach (CallApp app in CallApps.Apps) if (app.Has(process)) return app.Name;
                if (name.StartsWith("roblox", StringComparison.Ordinal)) return "Roblox";
                if (name == "obs64" || name == "obs32" || name == "obs") return "OBS";
                return process;
            }

            private static List<string> AppNames(List<string> processes)
            {
                List<string> names = new List<string>();
                foreach (string process in processes) if (!names.Contains(AppName(process))) names.Add(AppName(process));
                return names;
            }

            // "Discord", "Discord and OBS", "Discord, OBS and Roblox".
            internal static string Apps(List<string> processes)
            {
                List<string> names = AppNames(processes);
                if (names.Count < 2) return names.Count == 0 ? "" : names[0];
                return string.Join(", ", names.GetRange(0, names.Count - 1).ToArray()) + " and " + names[names.Count - 1];
            }

            internal void Flip() { onOff.Checked = !onOff.Checked; }

            // "on | Input | Output | the state line | the name to pick | who hears it", as the window shows it, for the smoke test.
            internal string Showing { get { return (onOff.Checked ? "on" : "off") + " | " + input.Text + " | " + output.Text + " | " + state.Text + " | " + pick.Text + " | " + live.Text; } }
            internal string CallSteps { get { return callHead.Text + " | " + note.Text + " | " + steps[0].Text + " | " + pickStep.Text + " | " + steps[4].Text; } }

            // Anything that overlaps, pokes out of the window or doesn't fit its own text, or null.
            internal string Crowded()
            {
                float s = ScaleOf(this);
                // A small desktop clamps the window, so check the size it was drawn for.
                Rectangle area = new Rectangle(0, 0, Math.Max(ClientSize.Width, (int)Math.Round(Wide * s)), Math.Max(ClientSize.Height, (int)Math.Round(Tall * s)));
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                    if (!area.Contains(a.Bounds) || a.Right > area.Right - (int)(16 * s)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds + " in " + ClientSize;
                    Toggle box = a as Toggle;
                    if (box != null && TextRenderer.MeasureText(box.Text, box.Font).Width > box.NameRoom) return "doesn't fit: " + box.Text + " " + TextRenderer.MeasureText(box.Text, box.Font).Width + " in " + box.NameRoom;
                    Key key = a as Key;
                    if (key != null && key.Text.Length > 0 && TextRenderer.MeasureText(key.Text, key.Font).Width + (int)Math.Ceiling(12 * s) > key.Width) return "doesn't fit: " + key.Text + " " + TextRenderer.MeasureText(key.Text, key.Font).Width + " in " + key.Width;
                    for (int j = i + 1; j < Controls.Count; j++)
                    {
                        Control b = Controls[j];
                        if (!b.Visible || (b is Label && b.Text.Length == 0)) continue;
                        if (a.Bounds.IntersectsWith(b.Bounds)) return "overlap: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " / " + (b.Text.Length > 0 ? b.Text : b.AccessibleName);
                    }
                }
                // Every line it can show fits too: VB-CABLE's far end by its full name, every state line and every live one.
                string vb = "CABLE Output (VB-Audio Virtual Cable)";
                foreach (string text in new[] { NoOutput, vb, "Microphone (" + MicMix.SteamEnd + ")" })
                    if (TextRenderer.MeasureText(text, output.Font).Width + (int)Math.Ceiling(28 * s) > output.Width) return "doesn't fit: " + text + " in Output";
                foreach (string line in new[] { "2   Input Device: " + vb, "Roblox or any other app: pick " + vb + " as its mic.", NoCable,CableSetup.Finish, CableSetup.Declined, CableSetup.Failed, CableSetup.Running, MicMix.Unopened, MicMix.NoMic, MicMix.MicGone, MicMix.MutedLine(MicMix.CableEnd), MicMix.MutedLine(MicMix.SteamEnd), MicMix.OffLine(MicMix.CableEnd), MicMix.OffLine(MicMix.SteamEnd), Off, "Discord, OBS and Roblox hear it now.", "OBS and Roblox hear it now. Discord doesn't yet.", NotYet })
                    if (state.Left + TextRenderer.MeasureText(line, Font).Width + (int)(24 * s) > ClientSize.Width - (int)(16 * s)) return "doesn't fit: " + line;
                return null;
            }

            internal void Picture(string path)
            {
                using (Bitmap shot = new Bitmap(Width, Height))
                {
                    DrawToBitmap(shot, new Rectangle(Point.Empty, Size));
                    shot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                }
            }

            protected override void OnVisibleChanged(EventArgs e)
            {
                ticks.Enabled = Visible;
                base.OnVisibleChanged(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) ticks.Dispose();
                base.Dispose(disposing);
            }

            // Over the Mainframe, like VOICE LAYERS, and on the screen.
            protected override void OnLoad(EventArgs e)
            {
                base.OnLoad(e);
                Rectangle area = Screen.FromControl(owner).WorkingArea;
                int x = owner.Left + (owner.Width - Width) / 2, y = owner.Top + (int)(48 * ScaleOf(this));
                Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - Height, y)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                float s = ScaleOf(this);
                Clip(this, s);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, s);
                Edging(graphics, this, s);
            }

            // Esc closes it.
            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
                base.OnKeyDown(e);
            }

            // Dragged by any spot that isn't a control, like the Mainframe.
            protected override void WndProc(ref Message message)
            {
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        // A row's value, in a box of its own. Up to seven steps it's a selector, ◁ value ▷ with a red dash under the value
        // marking where it is among them; past that, a bar whose red handle slides along under the value. Both step with the
        // arrow keys, and say what they're on to a screen reader.
        private sealed class Slider : Control, Tinted
        {
            public void Recolour(Func<Color, Color> swap) { ink = swap(ink); }

            private readonly string[] labels;
            private int value;
            private bool hot;
            private bool sliding;
            private int settled;
            // Which of a selector's arrows the mouse is on: -1 left, 1 right, 0 between them, 2 off it.
            private int zone = 2;
            private string shown;
            private Color ink = Color.Empty;
            // A tick under every step, or every few on a slider with many, like her voice layers' pitch and delay.
            internal int TickEvery = 1;
            // Blackwall's preset: red, torn and never quite still while it's picked (Lachy, 1 Nov).
            internal bool Torn;
            private System.Windows.Forms.Timer storm;
            private readonly Random tears = new Random();
            internal bool Storm
            {
                get { return storm != null; }
                set
                {
                    if (value == (storm != null)) return;
                    if (value)
                    {
                        storm = new System.Windows.Forms.Timer();
                        storm.Interval = 70;
                        storm.Tick += delegate { Invalidate(); };
                        storm.Start();
                    }
                    else { storm.Dispose(); storm = null; }
                    Invalidate();
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) Storm = false;
                base.Dispose(disposing);
            }

            // With Motion off it holds one torn frame, always the same.
            private void Tear(Graphics graphics, float s, TextFormatFlags flags, int reach)
            {
                Random rolls = storm != null ? tears : new Random(7);
                Color red = Neon.Tone(255, 0, 70);
                using (Pen rim = new Pen(red, Math.Max(1f, s))) graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
                if (rolls.Next(3) == 0) return;
                int shift = rolls.Next(-3, 4);
                Rectangle band = new Rectangle(0, rolls.Next(0, Math.Max(1, Height - 4)), Width, 2 + rolls.Next((int)(6 * s) + 1));
                graphics.SetClip(band);
                using (Brush dark = new SolidBrush(Neon.Tone(8, 6, 12))) graphics.FillRectangle(dark, band);
                TextRenderer.DrawText(graphics, Shown, Font, new Rectangle(reach + shift, 0, Width - 2 * reach, (int)(Height - 4 * s)), red, flags | TextFormatFlags.PreserveGraphicsClipping);
                graphics.ResetClip();
                using (Brush shard = new SolidBrush(red))
                using (Brush black = new SolidBrush(Neon.Tone(8, 6, 12)))
                    for (int i = 0; i < 3; i++)
                    {
                        int x = rolls.Next(Width), y = rolls.Next(Height), wide = 4 + rolls.Next((int)(22 * s) + 1);
                        graphics.FillRectangle(i == 1 ? black : shard, x, y, wide, Math.Max(1, (int)(1.5f * s)));
                    }
            }

            internal event EventHandler Changed;
            // Once the knob or an arrow key is let go somewhere new.
            internal event EventHandler Released;

            internal Slider(string[] labels)
            {
                this.labels = labels;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.Slider;
            }

            internal int Value
            {
                get { return value; }
                set { Place(value); settled = this.value; }
            }

            // The text in its box: its step's own name unless the Mainframe set another, like Robot's "40% · HD".
            internal string Shown { get { return shown ?? labels[value]; } set { shown = value; AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1); Invalidate(); } }

            internal Color Ink { get { return ink.IsEmpty ? Cyan : ink; } set { ink = value; Invalidate(); } }

            internal bool Stepper { get { return labels.Length <= 7; } }

            internal string[] Choices { get { return labels; } }

            private void Place(int wanted)
            {
                int next = Math.Max(0, Math.Min(labels.Length - 1, wanted));
                if (next == value) return;
                value = next;
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                Invalidate();
            }

            protected override AccessibleObject CreateAccessibilityInstance() { return new SliderAccess(this); }

            private sealed class SliderAccess : ControlAccessibleObject
            {
                private readonly Slider slider;

                internal SliderAccess(Slider owner) : base(owner) { slider = owner; }

                public override string Value { get { return slider.Shown; } set { } }
            }

            private void Pick(int next)
            {
                int before = value;
                Place(next);
                if (value != before && Changed != null) Changed(this, EventArgs.Empty);
            }

            private void Settle()
            {
                if (value == settled) return;
                settled = value;
                if (Released != null) Released(this, EventArgs.Empty);
            }

            internal void Slide(int next) { Pick(next); Settle(); }

            private float Unit() { return ScaleOf(this); }

            // The bar's handle, its grip: this wide, and it slides from one end of the box to the other.
            private float Grip { get { return 10 * Unit(); } }

            private int At(int x)
            {
                float s = Unit();
                float travel = Width - 2 * s - Grip;
                return (int)Math.Round((x - s - Grip / 2) / travel * (labels.Length - 1));
            }

            private int Zone(int x)
            {
                float reach = Math.Min(Width / 3f, 36 * Unit());
                return x < reach ? -1 : x > Width - reach ? 1 : 0;
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; zone = 2; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                Focus();
                sliding = true;
                if (!Stepper) Pick(At(e.X));
                else if (e.Button == MouseButtons.Left) Pick(value + Zone(e.X));
                base.OnMouseDown(e);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                if (sliding && !Stepper) Pick(At(e.X));
                int now = Zone(e.X);
                if (Stepper && now != zone) { zone = now; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseUp(MouseEventArgs e) { bool was = sliding; sliding = false; if (was) Settle(); Invalidate(); base.OnMouseUp(e); }

            protected override bool IsInputKey(Keys key) { return key == Keys.Left || key == Keys.Right || base.IsInputKey(key); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Left) Pick(value - 1);
                else if (e.KeyCode == Keys.Right) Pick(value + 1);
                base.OnKeyDown(e);
            }

            protected override void OnKeyUp(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Right) Settle();
                base.OnKeyUp(e);
            }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = Unit();
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float line = Math.Max(1f, s);
                RectangleF box = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                Color rim = Focused && ShowFocusCues ? Cyan : hot || sliding ? Neon.Tone(156, 40, 56) : Edge;
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                if (Stepper)
                {
                    using (GraphicsPath plate = Cut(box, 0, 5 * s, 5 * s, 0))
                    {
                        using (Brush fill = new SolidBrush(Field)) graphics.FillPath(fill, plate);
                        using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, plate);
                    }
                    float middle = Height / 2f - 1.5f * s;
                    Arrow(graphics, 11 * s, middle, s, -1, value > 0, zone == -1);
                    Arrow(graphics, Width - 11 * s, middle, s, 1, value < labels.Length - 1, zone == 1);
                    int reach = (int)(26 * s);
                    TextRenderer.DrawText(graphics, Shown, Font, new Rectangle(reach, 0, Width - 2 * reach, (int)(Height - 4 * s)), Ink, flags);
                    float dash = (labels.Length <= 4 ? 11 : 8) * s, gap = 3 * s;
                    float left = Width / 2f - (labels.Length * dash + (labels.Length - 1) * gap) / 2;
                    using (Brush off = new SolidBrush(Dash))
                    using (Brush on = new SolidBrush(Lit))
                        for (int i = 0; i < labels.Length; i++) graphics.FillRectangle(i == value ? on : off, left + i * (dash + gap), Height - 5.5f * s, dash, Math.Max(1, 1.5f * s));
                    if (Torn) Tear(graphics, s, flags, reach);
                    return;
                }
                float travel = Width - 2 * s - Grip;
                float knob = s + travel * value / (labels.Length - 1);
                using (GraphicsPath plate = Cut(box, 0, 0, 5 * s, 0))
                {
                    using (Brush fill = new SolidBrush(Field)) graphics.FillPath(fill, plate);
                    using (Brush done = new SolidBrush(Neon.Tone(42, 14, 25))) graphics.FillRectangle(done, line, line, knob - line, Height - 2 * line);
                    using (Brush tick = new SolidBrush(Dash))
                        for (int i = 0; i < labels.Length; i += Math.Max(1, TickEvery)) graphics.FillRectangle(tick, s + Grip / 2 + travel * i / (labels.Length - 1) - 0.5f * s, Height - 5 * s, Math.Max(1, s), 3 * s);
                    using (GraphicsPath grip = Cut(new RectangleF(knob, line, Grip, Height - 2 * line), 0, 0, 4 * s, 0))
                    using (Brush red = new SolidBrush(hot || sliding || Focused ? Lit : Hot))
                        graphics.FillPath(red, grip);
                    using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, plate);
                }
                Rectangle area = new Rectangle(0, 0, Width, Height);
                Rectangle handle = Rectangle.Round(new RectangleF(knob, 0, Grip, Height));
                flags |= TextFormatFlags.PreserveGraphicsClipping;
                using (Region bar = new Region(area))
                {
                    bar.Exclude(handle);
                    graphics.SetClip(bar, CombineMode.Replace);
                    TextRenderer.DrawText(graphics, Shown, Font, area, Ink, flags);
                }
                graphics.SetClip(handle);
                TextRenderer.DrawText(graphics, Shown, Font, area, Neon.Tone(40, 6, 12), flags);
                graphics.ResetClip();
            }

            // A selector's ◁ or ▷: cyan, solid under the mouse, and dark at the end it can't go past.
            private static void Arrow(Graphics graphics, float tip, float middle, float s, int way, bool open, bool over)
            {
                float back = tip - way * 7 * s;
                PointF[] mark = way < 0 ? new[] { new PointF(tip, middle), new PointF(back, middle - 5.5f * s), new PointF(back, middle + 5.5f * s) }
                    : new[] { new PointF(tip, middle), new PointF(back, middle - 5.5f * s), new PointF(back, middle + 5.5f * s) };
                Color colour = open ? Cyan : Dash;
                if (open && over) using (Brush fill = new SolidBrush(colour)) graphics.FillPolygon(fill, mark);
                using (Pen pen = new Pen(colour, 1.3f * s)) { pen.LineJoin = LineJoin.Miter; graphics.DrawPolygon(pen, mark); }
            }
        }

        // A row to pick, like BRAIN's tiers: a diamond, its name and a dim hint. The picked one's diamond and name are cyan,
        // and the one under the mouse gets a cyan box.
        private sealed class Rung : RadioButton
        {
            private bool hot;
            internal string Hint;
            internal Font HintFont;
            internal string Flag;
            internal Font FlagFont;

            internal bool Fits
            {
                get
                {
                    TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                    return (int)(32 * ScaleOf(this)) + TextRenderer.MeasureText(Text, Font, Size.Empty, flags).Width + TextRenderer.MeasureText(Hint ?? "", HintFont ?? Font, Size.Empty, flags).Width <= Width;
                }
            }

            internal Rung()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            // A radio button clicks itself when focus arrives by arrow key or from code. The VOICE pair picks on a real click or Space only.
            protected override void OnEnter(EventArgs e) { if (AutoCheck) base.OnEnter(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool lit = hot || (Focused && ShowFocusCues);
                if (lit)
                {
                    float line = Math.Max(1f, s);
                    using (GraphicsPath plate = Cut(new RectangleF(line / 2, line / 2, Width - line, Height - line), 0, 0, 5 * s, 0))
                    {
                        using (Brush wash = new SolidBrush(Color.FromArgb(24, Cyan))) graphics.FillPath(wash, plate);
                        using (Pen pen = new Pen(Color.FromArgb(190, Cyan), line)) graphics.DrawPath(pen, plate);
                    }
                }
                float x = 12 * s, y = Height / 2f, size = 4.5f * s;
                PointF[] diamond = { new PointF(x, y - size), new PointF(x + size, y), new PointF(x, y + size), new PointF(x - size, y) };
                if (Checked) using (Brush mark = new SolidBrush(Cyan)) graphics.FillPolygon(mark, diamond);
                else using (Pen pen = new Pen(lit ? Cyan : Faint, 1.2f * s)) graphics.DrawPolygon(pen, diamond);
                int left = (int)(24 * s);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(left, 0, Width - left, Height), Checked ? Cyan : White, flags);
                int after = left + TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, flags).Width + (int)(8 * s);
                if (Hint != null) TextRenderer.DrawText(graphics, Hint, HintFont ?? Font, new Rectangle(after, 0, Math.Max(0, Width - after), Height), Faint, flags);
                if (Flag != null) TextRenderer.DrawText(graphics, Flag, FlagFont ?? Font, new Rectangle(0, 0, Width - (int)(6 * s), Height), Neon.Amber, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
            }
        }

        // A setting that's on or off: its name on the left of its row and a pair of buttons on the right, OFF and ON, with the
        // one it's set to lit, solid cyan for ON and red for OFF. A click on either picks it; a click on the name, or Space,
        // flips it.
        private sealed class Toggle : CheckBox
        {
            private bool dimmed;
            internal bool Dimmed { get { return dimmed; } set { dimmed = value; Invalidate(); } }
            // Which part the mouse is on: -1 the name, 0 OFF, 1 ON, 2 off it.
            private int over = 2;
            private Font small;
            internal string Hint;
            internal Font HintFont;
            // The OFF and ON pair's width, in design px.
            internal int Switch = 172;

            internal Toggle()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
                AutoCheck = false;
            }

            // The width its name has before the pair, in px.
            internal int NameRoom { get { return Width - (int)Math.Ceiling((Switch + 10) * ScaleOf(this)); } }

            internal Font SmallFont() { return Font.Unit == GraphicsUnit.Pixel ? new Font(Font.FontFamily, Font.Size * 8.75f / 9.5f, Font.Style, GraphicsUnit.Pixel) : Type(Font.SizeInPoints - 0.75f, true); }

            private int Part(int x)
            {
                float s = ScaleOf(this), start = Width - Switch * s;
                return x < start ? -1 : x < start + Switch * s / 2 ? 0 : 1;
            }

            protected override void OnFontChanged(EventArgs e)
            {
                if (small != null) small.Dispose();
                small = null;
                base.OnFontChanged(e);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing && small != null) small.Dispose();
                base.Dispose(disposing);
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int part = Part(e.X);
                if (part != over) { over = part; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseLeave(EventArgs e) { over = 2; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnClick(EventArgs e)
            {
                MouseEventArgs mouse = e as MouseEventArgs;
                int part = mouse == null ? -1 : Part(mouse.X);
                Checked = part < 0 ? !Checked : part == 1;
                base.OnClick(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (small == null) small = SmallFont();
                float line = Math.Max(1f, s), wide = Switch * s, gap = 4 * s, half = (wide - gap) / 2, start = Width - wide;
                RectangleF off = new RectangleF(start + line / 2, line / 2, half - line, Height - line);
                RectangleF on = new RectangleF(start + half + gap + line / 2, line / 2, half - line, Height - line);
                Color offFill, offRim, offInk, onFill, onRim, onInk;
                if (Checked)
                {
                    offFill = over == 0 ? Neon.Tone(50, 16, 28) : Neon.Tone(32, 12, 19);
                    offRim = over == 0 ? Neon.Tone(120, 36, 50) : Neon.Tone(50, 18, 28);
                    offInk = over == 0 ? Neon.Tone(170, 60, 72) : Neon.Tone(84, 26, 38);
                    onFill = onRim = Cyan;
                    onInk = OnInk;
                }
                else
                {
                    offFill = Neon.Tone(155, 50, 46);
                    offRim = Neon.Tone(222, 69, 63);
                    offInk = Neon.Tone(255, 150, 144);
                    onFill = over == 1 ? Neon.Tone(12, 46, 50) : Neon.Tone(9, 30, 33);
                    onRim = over == 1 ? DeepEdge : Neon.Tone(16, 44, 48);
                    onInk = over == 1 ? Neon.Tone(110, 210, 216) : Neon.Tone(28, 68, 72);
                }
                if (Neon.Mono)
                {
                    offFill = onFill = Field;
                    offRim = Checked ? (over == 0 ? Faint : Brim) : White;
                    offInk = Checked ? (over == 0 ? White : Faint) : White;
                    onRim = Checked ? Cyan : (over == 1 ? Faint : Brim);
                    onInk = Checked ? OnInk : (over == 1 ? White : Faint);
                    if (Checked) onFill = Cyan;
                }
                if (Dimmed)
                {
                    offFill = Checked ? Field : Neon.Tone(52, 22, 32);
                    offRim = Checked ? Neon.Tone(52, 22, 32) : Brim;
                    onFill = Checked ? Deep : Field;
                    onRim = Checked ? DeepEdge : Neon.Tone(52, 22, 32);
                    offInk = onInk = Neon.Tone(112, 94, 104);
                }
                if (!Enabled) { offFill = onFill = Field; offRim = onRim = Neon.Tone(52, 22, 32); offInk = onInk = Neon.Tone(112, 94, 104); }
                TextFormatFlags centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                using (GraphicsPath plate = Cut(off, 0, 0, 0, 5 * s))
                {
                    using (Brush fill = new SolidBrush(offFill)) graphics.FillPath(fill, plate);
                    using (Pen pen = new Pen(offRim, line)) graphics.DrawPath(pen, plate);
                }
                using (GraphicsPath plate = Cut(on, 0, 0, 5 * s, 0))
                {
                    using (Brush fill = new SolidBrush(onFill)) graphics.FillPath(fill, plate);
                    using (Pen pen = new Pen(onRim, line)) graphics.DrawPath(pen, plate);
                }
                TextRenderer.DrawText(graphics, "OFF", small, Rectangle.Round(off), offInk, centre);
                TextRenderer.DrawText(graphics, "ON", small, Rectangle.Round(on), onInk, centre);
                if (Focused && ShowFocusCues)
                    using (GraphicsPath around = Cut(new RectangleF(start - 2 * s, line / 2, wide + 2 * s - line / 2, Height - line), 0, 0, 5 * s, 0))
                    using (Pen pen = new Pen(Cyan, line))
                        graphics.DrawPath(pen, around);
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                int room = NameRoom;
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(0, 0, room, Height), Enabled && !Dimmed ? Rose : Faint, flags);
                if (Hint == null) return;
                TextFormatFlags measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int after = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, measure).Width + (int)(10 * s);
                Font hint = HintFont ?? Font;
                if (after + TextRenderer.MeasureText(graphics, Hint, hint, Size.Empty, measure).Width <= room) TextRenderer.DrawText(graphics, Hint, hint, new Rectangle(after, 0, room - after, Height), Faint, flags);
            }
        }
    }
}
