using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class Mainframe : Form, IMessageFilter
    {
        // Her Mainframe's own look, after Cyberpunk 2077's settings screen (Lachy, 30 Oct): near black with a wine glow at the
        // top, sections on dark bands under a teal rule, setting names in red, values in cyan, and boxes with a dark red rim and
        // clipped corners. The colours come from Neon, which her menus, type box, installer and notices share too; her bubbles
        // keep Look's.
        private static readonly Color Ground = Neon.Ground;
        private static readonly Color Glow = Neon.Glow;
        private static readonly Color Band = Neon.Band;
        private static readonly Color Rule = Neon.Rule;
        private static readonly Color Rose = Neon.Rose;
        private static readonly Color White = Neon.White;
        private static readonly Color Faint = Neon.Faint;
        private static readonly Color Field = Neon.Field;
        private static readonly Color Hover = Neon.Hover;
        private static readonly Color Pressed = Neon.Pressed;
        private static readonly Color Edge = Neon.Edge;
        private static readonly Color Hot = Neon.Hot;
        private static readonly Color Cyan = Neon.Cyan;
        private static readonly Color Deep = Neon.Deep;
        private static readonly Color DeepEdge = Neon.DeepEdge;
        private static readonly Color OnInk = Neon.OnInk;
        private static readonly Color Dash = Neon.Dash;
        private static readonly Color Lit = Neon.Lit;
        private static readonly Color Brim = Neon.Brim;
        private static readonly Color Well = Neon.Well;
        private static readonly Color WellEdge = Neon.WellEdge;
        // Bahnschrift, Windows' own DIN-style face, in its semi-condensed cuts and the condensed bold of her title. Windows
        // before 10's 2017 update doesn't have it, so there she keeps her Share Tech Mono.
        private static readonly FontFamily Plain = Family("Bahnschrift SemiCondensed");
        private static readonly FontFamily Heavy = Family("Bahnschrift SemiBold SemiConden");
        private static readonly FontFamily Narrow = Family("Bahnschrift SemiBold Condensed");
        private const int Wide = 380;
        private const int Tall = 642;
        private static readonly int[] Gaps = { 10, 15, 30, 60, 90, 120, 180, 300, 600 };
        private static readonly string[] GapNames = { "10 s", "15 s", "30 s", "1 min", "1.5 min", "2 min", "3 min", "5 min", "10 min" };
        private const int SetupHeight = 118;
        private const int PageTall = 860;
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
        private const int VoiceHeight = 300;
        // While her free voice or ears download, a row with the brain setup's dotted bar opens here and pushes the rest down.
        private const int InstallAt = 443;
        private const int InstallRow = 40;
        // CLOUD VOICE's row: its service, then ElevenLabs' key or the other service's panel. Unless ElevenLabs is picked with a
        // key saved, one row under it folds away and the rest moves up; under the other rows, this one folds away instead.
        private const int KeyAt = 310;
        private const int KeyFold = 36;
        private float voiceFilled = -1;
        private readonly Label installNote;
        private readonly Companion capri;
        private readonly Label brainName;
        private readonly Label brainNote;
        private readonly Key models;
        private readonly ContextMenuStrip modelMenu;
        private readonly Key refresh;
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
        private readonly Toggle startup;
        private readonly Toggle discord;
        private readonly Toggle cursor;
        private readonly Key security;
        private readonly Key talkKeys;
        private readonly Key muteKeys;
        private readonly Key typeKeys;
        private readonly Key hideKeys;
        private readonly Key quietKeys;
        private readonly Key confirmKeys;
        private readonly Key cancelKeys;
        // The keys pressed so far while one of her keys is being picked: -1 until a real key comes, then its modifiers.
        private int pendingMods = -1;
        private int pendingVk;
        private int heldMods;
        private readonly Toggle watch;
        private readonly Toggle caughtUp;
        private readonly Toggle stayOnModel;
        private readonly Toggle remembering;
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
        private LayersSheet layered;
        private Key layersKey;
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
        private bool typeReady = true;
        private bool hideReady = true;
        private bool quietReady = true;
        private readonly Label muteShortcut;
        private readonly Key mics;
        private readonly ContextMenuStrip micMenu;
        private bool keySaved;
        private readonly Entry textEntry;
        private readonly Key textConnect;
        private readonly Key freeKey;
        private bool textSaved;
        private readonly List<KeyValuePair<Control, int>> lower = new List<KeyValuePair<Control, int>>();
        private string[] lastInstalled;
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
        private int wheel;
        private readonly Rim topRim = new Rim();
        private readonly Rim bottomRim = new Rim();
        // Smoke tests pretend the screen's working area is this many px tall, before scaling; 0 uses the real one.
        internal int ShortScreen;
        private readonly Key languages;
        private readonly ContextMenuStrip languageMenu;
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
        private readonly Toggle intro;
        private readonly Toggle typing;
        private readonly Toggle hdGames;
        private readonly Toggle miniPlayer;
        private readonly Readout sleepName;
        private readonly Readout soundsName;
        private readonly Slider wander;
        private readonly Readout wanderName;
        private readonly Slider pops;
        private readonly Readout popsName;
        private readonly Toggle dance;
        private readonly Slider replies;
        private readonly Readout repliesName;
        private readonly Slider recall;
        private readonly Readout recallName;
        private readonly Entry custom;
        private readonly Label customNote;
        private readonly Slider perchFps;
        private readonly Readout perchFpsName;
        // Her Mainframe size: Auto, or 100% to 200% of the screen's own scale.
        private static readonly int[] FrameSizes = { 0, 100, 125, 150, 175, 200 };
        private static readonly string[] FrameSizeNames = { "Auto", "100%", "125%", "150%", "175%", "200%" };
        private readonly Slider frameSize;
        private readonly Readout frameSizeName;
        // Settings' rows: a setting's name from x 28 and its value's box from x 184 to 356, 22 px tall on a 28 px row.
        private const int Row = 28;
        private const int ValueAt = 184;
        private const int ValueWide = 172;
        private readonly Label perchNote;

        internal Mainframe(Companion owner, Icon icon, string version)
        {
            capri = owner;
            Text = "Capri Mainframe";
            Icon = icon;
            ShowInTaskbar = true;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            AutoScaleDimensions = new SizeF(96, 96);
            Font = Type(9.5f);
            BackColor = Ground;
            ForeColor = White;
            DoubleBuffered = true;
            ClientSize = new Size(Wide, Tall + SetupHeight + KeyRow);
            Font small = Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point);
            Caption("MAINFRAME", 24, 15, Display(16f), Rose);
            build = Caption("v" + version, 200, 20, small, Faint);
            build.AutoSize = false;
            build.TextAlign = ContentAlignment.MiddleRight;
            build.Bounds = new Rectangle(230, 20, 62, 22);
            minimize = Make("", 300, 20, 26, 22, delegate { WindowState = FormWindowState.Minimized; });
            minimize.Glyph = 1;
            minimize.AccessibleName = "Minimize";
            close = Make("", 330, 20, 26, 22, delegate { Close(); });
            close.Glyph = 2;
            close.AccessibleName = "Close";
            more = Make("Settings", 146, 20, 84, 22, delegate { ShowSettings(!paged); });
            more.Accent = Cyan;
            more.Loud = true;
            int header = Controls.Count;
            for (int i = 0; i < header; i++) Scrolls(Controls[i]);
            Section("BRAIN >", 0, 58);
            brainName = Caption("", 28, 79, Type(11f, true), White);
            brainNote = Caption("", 28, 101, Font, Faint);
            modelMenu = Dropdown();
            models = Make("", 28, 134, 244, 28, delegate { modelMenu.Show(models, new Point(0, models.Height)); });
            models.Chevron = true;
            refresh = Make("Refresh", 280, 134, 76, 28, delegate { capri.FindBrain(); });
            for (int i = 0; i < 3; i++) steps[i] = Caption("", 28, 138 + i * 22, Font, Faint);
            setup = Make("Set up her brain", 28, 226, 170, 28, delegate { if (capri.SettingUp) capri.CancelSetup(); else capri.SetUpBrain(); });
            setup.Accent = Cyan;
            setup.Loud = true;
            hint = Caption("Have Ollama? Open it, then press Refresh.", 28, 264, Look.Mono(9f, FontStyle.Italic, GraphicsUnit.Point), Faint);
            tierNote = Caption("Pick one. Only one brain runs at a time.", 28, 168, Look.Mono(8.5f, FontStyle.Italic, GraphicsUnit.Point), Faint);
            string[,] tiers = { { "NORMAL", "Free · local model" }, { "HIGHER", "Free · NVIDIA API" }, { "MAX INTELLIGENCE", "ChatGPT or Claude API" } };
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
            freeKey = Make("Free key", 266, 209, 90, 22, delegate { capri.OpenFreeKeyPage(); });
            textEntry = Input(28, 262, 244);
            textEntry.Box.UseSystemPasswordChar = true;
            textEntry.Box.AccessibleName = "NVIDIA API key";
            textEntry.Cue = "Paste your free NVIDIA key";
            textConnect = Make("Connect", 280, 262, 76, 28, delegate { ConnectText(); });
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
                rung.Bounds = new Rectangle(28, 208 + 24 * i, i >= 2 ? 246 : 328, 24);
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
            cloudPick = Lower(Make(Clouds[0], 28, KeyAt, 100, 28, delegate { cloudMenu.Show(cloudPick, new Point(0, cloudPick.Height)); }));
            cloudPick.Chevron = true;
            cloudPick.AccessibleName = "Cloud voice service";
            keyEntry = Lower(Input(136, KeyAt, 136));
            keyEntry.Box.UseSystemPasswordChar = true;
            keyEntry.Box.AccessibleName = "ElevenLabs API key";
            connect = Lower(Make("Connect", 280, KeyAt, 76, 28, delegate { Connect(); }));
            keyEntry.Box.TextChanged += delegate { Relabel(); };
            keyEntry.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Connect(); } };
            // Fish Audio and OpenAI keep their key, voice and model in the voice model panel.
            cloudVoice = Lower(Make("", 136, KeyAt, 220, 28, delegate { OpenServer(true, CloudApi(cloudName)); }));
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
            Lower(Make("Test", 280, 382, 76, 28, delegate { capri.TestVoice(); }));
            muteShortcut = Lower(Caption("Ctrl + Alt + N mutes mic", 28, 417, small, Faint));
            muteShortcut.Cursor = Cursors.Hand;
            muteShortcut.Click += delegate { ShowSettings(true); PickKeys(1); };
            Deactivate += delegate { CancelKeys(); };
            voiceNote = Lower(Caption("", 28, InstallAt, Font, Faint));
            voiceNote.Click += delegate { if (speechFix) capri.OpenSpeechSettings(); };
            installNote = Caption("", 28, InstallAt + 2, Font, White);
            installNote.Visible = false;
            int v = VoiceHeight;
            Lower(Section("CAPRI >", 0, 184 + v));
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
            Section("YOU >", 0, 58);
            Make("Open her notes", 236, 55, 120, 20, delegate { capri.OpenNotes(); }).AccessibleName = "Open her notes: me.md, her notes about you";
            Named("Language", 0, 82);
            languageMenu = Dropdown();
            for (int i = 0; i < Brain.Languages.GetLength(0); i++)
            {
                string picked = Brain.Languages[i, 1];
                ToolStripMenuItem item = Choice(Brain.Languages[i, 0], false, delegate { capri.SetLanguage(picked); Sync(); });
                item.Tag = picked;
                languageMenu.Items.Add(item);
            }
            languages = Make("English", ValueAt, 85, ValueWide, 22, delegate { languageMenu.Show(languages, new Point(0, languages.Height)); });
            languages.Chevron = true;
            languages.AccessibleName = "Her language";
            // A label of its own, so the question still shows once a name is saved in the box.
            Named("What should she call you?", 0, 110);
            Make("Get to know me", 236, 113, 120, 22, delegate { capri.GetToKnow(); }).AccessibleName = "Get to know me: she asks what to call you, your pronouns and what you're into, and keeps them in me.md";
            nickname = Input(28, 141, 244, 22);
            nickname.Cue = "Your name or a nickname";
            nickname.Box.AccessibleName = "What should she call you?";
            nickname.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseNickname(); } };
            Make("Save", 280, 141, 76, 22, delegate { UseNickname(); });
            Section("VOICE >", 0, 180);
            styleNote = Caption("Every voice, ElevenLabs too", 88, 178, small, Faint);
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
            // The row under Typing sound, shown only while her HD voice is on (Lachy, 29 Oct). Off out of the box.
            hdGames = Switch("Free voice in games", 0, 400, delegate { if (!syncing) capri.ToggleHdGameRest(); });
            hdGames.AccessibleName = "Free voice in games: while a game or video is full screen, her free voice talks instead of her HD voice, so the game gets the graphics card";
            hdGames.Visible = false;
            int afterVoice = Controls.Count;
            Section("MOVES >", 0, 442);
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
            // Only a click ends picking a key, so Enter, Esc and Tab can be her keys too. Confirm and Cancel show in KEYS' band
            // while one is being picked.
            confirmKeys = Make("Confirm", 212, 645, 78, 20, delegate { ConfirmKeys(); });
            confirmKeys.Accent = Cyan;
            confirmKeys.Loud = true;
            confirmKeys.AccessibleName = "Confirm her new keys";
            cancelKeys = Make("Cancel", 296, 645, 60, 20, delegate { CancelKeys(); });
            cancelKeys.AccessibleName = "Cancel and keep her old keys";
            confirmKeys.Visible = cancelKeys.Visible = false;
            keysNote = Caption("", 28, 816, small, Faint);
            keysNote.MaximumSize = new Size(328, 0);
            for (int i = afterVoice; i < Controls.Count; i++) underVoice.Add(new KeyValuePair<Control, int>(Controls[i], Controls[i].Top));
            Section("CAPRI >", c, 58);
            sizeSlider = Setting("Size", SizeNames, c, 82, "Her size");
            sizeName = new Readout(sizeSlider);
            sizeSlider.Changed += delegate { if (syncing) return; sizeName.Text = SizeNames[sizeSlider.Value]; capri.SetCharacterSize(SizeSteps[sizeSlider.Value]); };
            checkIns = Switch("Check in on me", c, 110, delegate { if (!syncing) capri.ToggleCheckIns(); });
            onTop = Switch("Always on top", c, 138, delegate { if (!syncing) capri.ToggleOnTop(); });
            // Zenni's ask (30 Oct): under Virtual Super Resolution her Mainframe came out tiny. Auto grows it by how much bigger
            // the desktop is than the panel showing it; 100% to 200% sizes it by hand.
            frameSize = Setting("Mainframe size", FrameSizeNames, c, 166, "Mainframe size. Auto makes it bigger when Virtual Super Resolution or DSR shrinks a big desktop onto a smaller screen");
            frameSizeName = new Readout(frameSize);
            frameSize.Changed += delegate { if (!syncing) ShowFrameSize(); };
            frameSize.Released += delegate { if (!syncing) { capri.SetMainframeSize(FrameSizes[frameSize.Value]); Rezoom(Wanted()); ShowFrameSize(); } };
            Section("WATCH >", c, 208);
            watch = Switch("Watch my screen", c, 232, delegate { if (!syncing) capri.ToggleWatch(); });
            watch.AccessibleName = "Watch my screen. She comments now and then";
            caughtUp = Switch("Keep her caught up", c, 260, delegate { if (!syncing) capri.ToggleCaughtUp(); });
            caughtUp.AccessibleName = "Keep her caught up on my screen. Glances she stays quiet about still go in her memory, which uses a little more of your API";
            gap = Setting("Every", GapNames, c, 288, "Time between glances");
            gapName = new Readout(gap);
            gap.Changed += delegate { if (syncing) return; gapName.Text = GapNames[gap.Value]; capri.SetGlanceGap(Gaps[gap.Value]); };
            Section("BRAIN >", c, 330);
            stayOnModel = Switch("Stay on my model", c, 354, delegate { if (!syncing) capri.ToggleStayOnModel(); });
            stayOnModel.AccessibleName = "Stay on my model. When an NVIDIA model gets stuck she says so instead of switching to another one";
            replies = Setting("Replies", Lengths, c, 382, "Reply length");
            repliesName = new Readout(replies);
            replies.Changed += delegate { if (syncing) return; repliesName.Text = Lengths[replies.Value]; capri.SetWordiness(replies.Value); };
            recall = Setting("Memory", Lengths, c, 410, "How much of the chat she keeps");
            recallName = new Readout(recall);
            recall.Changed += delegate { if (syncing) return; recallName.Text = Lengths[recall.Value]; capri.SetRecall(Recalls[recall.Value]); };
            bubble = Setting("Bubble", BubbleNames, c, 438, "How long her replies stay up");
            bubbleName = new Readout(bubble);
            bubble.Changed += delegate { if (syncing) return; bubbleName.Text = BubbleNames[bubble.Value]; capri.SetBubbleTime(bubble.Value); };
            custom = Input(28 + c, 469, 244, 22);
            custom.Cue = "Custom model name";
            custom.Box.AccessibleName = "Custom model name";
            custom.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; UseCustom(); } };
            Make("Use", 280 + c, 469, 76, 22, delegate { UseCustom(); });
            customNote = Caption("Any Ollama model you've pulled, or a model id on your key.", 28 + c, 498, small, Faint);
            customNote.MaximumSize = new Size(328, 0);
            Section("HABITS >", c, 544);
            Make("Open her memories", 220 + c, 541, 136, 20, delegate { capri.OpenMemories(); }).AccessibleName = "Open her memories: memories.md and her chat transcripts, kept on this PC";
            startup = Switch("Start with Windows", c, 568, delegate { if (!syncing) capri.ToggleStartup(); });
            discord = Switch("Show on Discord", c, 596, delegate { if (!syncing) capri.ToggleDiscord(); });
            cursor = Switch("Use her cursor", c, 624, delegate { if (!syncing) capri.ToggleCursor(); });
            remembering = Switch("Remember our chats", c, 652, delegate { if (!syncing) capri.ToggleRemembering(); });
            remembering.AccessibleName = "Remember our chats. She keeps daily transcripts and memories.md on this PC, never uploaded";
            // SECURITY took Blackwall mode's place (Lachy, 29 Oct): its button opens her SECURITY window, and the line under it
            // says what the preset does. Blackwall mode is its Blackwall preset now.
            security = Make("Security: " + Security.PresetName, 28 + c, 683, 328, 22, delegate { OpenSecurity(); });
            security.AccessibleName = "Security: what she does on your PC without asking first";
            blackwallNote = Caption(Security.Short, 28 + c, 707, small, Faint);
            blackwallNote.AutoSize = false;
            blackwallNote.TextAlign = ContentAlignment.MiddleRight;
            blackwallNote.Bounds = new Rectangle(28 + c, 707, 328, 18);
            // Frames a second while she sits on a window. Her sway takes 2 s at every fps; past 30 she draws in-betweens.
            perchFps = Setting("Perch", PerchNames, c, 728, "Her perch frames per second");
            perchFpsName = new Readout(perchFps);
            perchFps.Changed += delegate { if (syncing) return; capri.SetPerchFps(Companion.PerchRates[perchFps.Value]); ShowPerch(); };
            perchNote = Caption("", 28 + c, 756, small, Faint);
            perchNote.AutoSize = false;
            perchNote.TextAlign = ContentAlignment.MiddleRight;
            perchNote.Bounds = new Rectangle(28 + c, 756, 328, 18);
            Make("More settings", 28 + c, 790, 328, 22, delegate { ShowMoreSettings(); }).AccessibleName = "More settings: keep her notes in your Obsidian vault";
            for (int i = first; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                pageLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                Scrolls(control);
                control.Left += Far;
                Reachable(control, false);
                // Clicking into anything else while a key is being picked keeps the old key, so typing in a box is never swallowed.
                control.Enter += delegate { if (capturing != 0 && control != confirmKeys && control != cancelKeys && control != PickedKey) CancelKeys(); };
            }
            Controls.Add(topRim);
            Controls.Add(bottomRim);
            topRim.BringToFront();
            bottomRim.BringToFront();
            Searching();
            Sync();
            Application.AddMessageFilter(this);
        }

        private static Button DialogButton(string text, int x, int y, int width, Color accent)
        {
            Button button = new Button { Text = text, Bounds = new Rectangle(x, y, width, 30), FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Look.Ink, UseVisualStyleBackColor = false };
            button.FlatAppearance.BorderColor = accent;
            return button;
        }

        private static Label Heading(string text, int y) { return new Label { Text = text, Bounds = new Rectangle(16, y, 488, 18), ForeColor = Look.Yellow, Font = Look.Mono(9f, FontStyle.Bold, GraphicsUnit.Point) }; }

        private static Label Hint(string text, int y, int height) { return new Label { Text = text, Bounds = new Rectangle(16, y, 488, height), ForeColor = Look.Dim }; }

        private void ShowMoreSettings()
        {
            using (Form dialog = new Form())
            {
                dialog.AutoScaleDimensions = new SizeF(96f, 96f);
                dialog.AutoScaleMode = AutoScaleMode.Dpi;
                dialog.Text = "More settings";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ShowInTaskbar = false;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.BackColor = Look.Panel;
                dialog.ForeColor = Look.Ink;
                dialog.Font = Look.Mono(9f, FontStyle.Regular, GraphicsUnit.Point);
                dialog.ClientSize = new Size(520, 170);
                List<KeyValuePair<string, string>> vaults = Companion.ObsidianVaults();
                List<string> vaultNotes = new List<string> { "" };
                ComboBox vault = new ComboBox { Bounds = new Rectangle(16, 44, 368, 24), DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat, BackColor = Field, ForeColor = Look.Ink };
                vault.AccessibleName = "Which Obsidian vault her memories go in";
                vault.Items.Add("Her own notes (no vault)");
                foreach (KeyValuePair<string, string> found in vaults) { vault.Items.Add("Vault: " + found.Key); vaultNotes.Add(System.IO.Path.Combine(found.Value, Companion.VaultNote)); }
                Action showVault = delegate
                {
                    string current = Companion.NotesIn(capri.Vault);
                    int at = current == null ? 0 : vaultNotes.FindIndex(delegate(string note) { return string.Equals(note, current, StringComparison.OrdinalIgnoreCase); });
                    if (at < 0)
                    {
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
                Button pickVault = DialogButton("Browse", 392, 42, 112, Look.Dim);
                Label vaultNote = Hint(capri.Vault.Length > 0 ? "She saves her memories to " + System.IO.Path.GetFileName(Companion.NotesIn(capri.Vault)) + "." : vaults.Count > 0 ? "Pick one of your Obsidian vaults and her memories go in a Capri Memories note there." : "No Obsidian vaults found on this PC. Open one in Obsidian first, or Browse to a note.", 78, 34);
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
                    using (SaveFileDialog picker = new SaveFileDialog { Title = "Pick the note her memories go in", Filter = "Notes (*.md)|*.md", DefaultExt = "md", AddExtension = true, OverwritePrompt = false, CheckPathExists = true, FileName = Companion.VaultNote })
                    {
                        string current = Companion.NotesIn(capri.Vault);
                        if (current == null && vaults.Count > 0) current = System.IO.Path.Combine(vaults[0].Value, Companion.VaultNote);
                        if (current != null && System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(current))) { picker.InitialDirectory = System.IO.Path.GetDirectoryName(current); picker.FileName = System.IO.Path.GetFileName(current); }
                        if (picker.ShowDialog(dialog) == DialogResult.OK) useVault(picker.FileName);
                    }
                };
                Button close = DialogButton("Close", 404, 124, 100, Look.Dim);
                close.Click += delegate { dialog.Close(); };
                dialog.CancelButton = close;
                dialog.Controls.AddRange(new Control[] { Heading("MEMORY VAULT >", 12), vault, pickVault, vaultNote, close });
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
            Named(name, x, y);
            Slider slider = new Slider(steps);
            slider.Font = Type(9.5f);
            slider.Bounds = new Rectangle(ValueAt + x, y + 3, ValueWide, 22);
            slider.AccessibleName = spoken;
            Controls.Add(slider);
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
            return toggle;
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

        internal bool SettingsShown { get { return paged; } }

        internal string SizeStatus { get { return sizeName.Text; } }

        internal bool TypingShown { get { return typing.Checked; } }

        internal string HdGamesStatus { get { return hdGames.Visible ? (hdGames.Checked ? "on" : "off") : "-"; } }

        // Free voice in games has the row under Typing sound, shown only while her HD voice is on. Without it, MOVES and KEYS
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
            ScrollPage(back);
        }

        // MOVES' and KEYS' controls with their tops as built, and how far up they've moved.
        private readonly List<KeyValuePair<Control, int>> underVoice = new List<KeyValuePair<Control, int>>();
        private int lifted;

        internal bool PlayerShown { get { return miniPlayer.Checked; } }

        internal void FlipPlayer() { miniPlayer.Checked = !miniPlayer.Checked; }

        internal string HabitsStatus { get { return startup.Checked ? "startup" : "-"; } }

        internal bool DiscordShown { get { return discord.Checked; } }

        internal string JobsStatus { get { return (cursor.Checked ? "cursor" : "-") + " | " + security.Text; } }

        internal string BlackwallStatus { get { return security.Text + " | " + blackwallNote.Text; } }

        internal string SettingsStatus { get { return languages.Text + " | " + volumeName.Text + " | " + soundsName.Text + " | " + wanderName.Text + " | " + sleepName.Text + " | " + (dance.Checked ? "dance" : "-") + " | " + (intro.Checked ? "intro" : "-") + " | " + repliesName.Text + " | " + recallName.Text + " | " + bubbleName.Text; } }

        internal void ShowSettings(bool on)
        {
            if (!on) CancelKeys();
            paged = on;
            more.Text = on ? "Back" : "Settings";
            more.Invalidate();
            Place();
            foreach (KeyValuePair<Control, int> entry in mainLeft) Reachable(entry.Key, !on);
            foreach (KeyValuePair<Control, int> entry in pageLeft) Reachable(entry.Key, on);
            if (Hidden(ActiveControl)) more.Focus();
            if (on) Sync();
            wheel = 0;
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
            foreach (KeyValuePair<Control, int> entry in mainLeft) entry.Key.Left = Scaled(entry.Value) + (paged ? Far : 0);
            foreach (KeyValuePair<Control, int> entry in pageLeft) entry.Key.Left = Scaled(entry.Value) + (paged ? 0 : Far);
            int edge = paged ? Column : 0;
            build.Left = Scaled(230 + edge);
            minimize.Left = Scaled(300 + edge);
            close.Left = Scaled(330 + edge);
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
            if (!paged || pageView >= PageTall || Control.MouseButtons != MouseButtons.None) return;
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
            int next = paged ? Math.Max(0, Math.Min(PageTall - pageView, to)) : 0;
            if (next == pageScroll) return;
            if (pageScroll == 0) foreach (Control control in scrolled) tops[control] = control.Top;
            pageScroll = next;
            int shift = Scaled(next);
            // Every control moves at once, with no half-scrolled frame in between.
            Native.SendMessage(Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
            try { foreach (Control control in scrolled) control.Top = tops[control] - shift; }
            finally { Native.SendMessage(Handle, 0x000B, new IntPtr(1), IntPtr.Zero); }
            Invalidate(true);
        }

        // How much of the settings page the screen's working area has room for, before scaling.
        private int Fit()
        {
            int room = ShortScreen > 0 ? ShortScreen : (int)(Screen.FromControl(this).WorkingArea.Height / Unit());
            return Math.Max(4 * ScrollRow, Math.Min(PageTall, room));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!paged || pageView >= PageTall) return;
            wheel += e.Delta;
            int rows = wheel / 120;
            wheel -= rows * 120;
            if (rows != 0) ScrollPage(pageScroll - rows * ScrollRow);
        }

        private void Reachable(Control control, bool on)
        {
            if (!stops.ContainsKey(control)) stops[control] = control.TabStop;
            control.TabStop = on && stops[control];
            foreach (Control child in control.Controls) Reachable(child, on);
        }

        private bool Hidden(Control control)
        {
            List<KeyValuePair<Control, int>> hidden = paged ? mainLeft : pageLeft;
            for (Control top = control; top != null && top != this; top = top.Parent)
                foreach (KeyValuePair<Control, int> entry in hidden) if (entry.Key == top) return true;
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
                return value;
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            loaded = true;
            Rezoom(Wanted());
            ShowFrameSize();
            Arrange(expanded);
        }

        // Auto's size on the screen she's on, kept for Settings to show.
        private float autoZoom = 1f;

        // Her Mainframe size as picked, or for Auto what her screen needs: how much bigger its desktop is than the panel showing
        // it, less what Windows' own scaling already makes up, to the nearest 5% from 100% to 200%. Zenni's Virtual Super
        // Resolution, a 3840 x 2160 desktop on a 1920 x 1080 screen at 100%, gets 200%.
        private float Wanted()
        {
            float shrunk = Panels.Shrink(Screen.FromControl(this));
            autoZoom = Math.Max(1f, Math.Min(2f, (float)Math.Round(shrunk / DeviceScale() * 20) / 20));
            int picked = capri.MainframeSize;
            return picked > 0 ? picked / 100f : autoZoom;
        }

        private void ShowFrameSize()
        {
            int auto = (int)Math.Round(autoZoom * 100);
            frameSizeName.Text = frameSize.Value == 0 && auto != 100 ? "Auto · " + auto + "%" : FrameSizeNames[frameSize.Value];
        }

        // Sizes everything on her Mainframe to a new zoom: every font, then every control's place and size, then each page where
        // it belongs. Her SECURITY and layers windows pick it up the next time they open.
        private void Rezoom(float next)
        {
            if (!loaded || Math.Abs(next - zoom) < 0.005f) return;
            float factor = next / zoom;
            ScrollPage(0);
            SuspendLayout();
            Grow(this, factor);
            foreach (ContextMenuStrip menu in new[] { modelMenu, cloudMenu, voiceMenu, speechModelMenu, freeMenu, micMenu, languageMenu }) menu.Font = Font;
            Scale(new SizeF(factor, factor));
            zoom = next;
            ResumeLayout(false);
            Place();
            Arrange(expanded);
            Shape();
        }

        // Her Mainframe's zoom, and picking its size as they would, for the smoke test.
        internal float Zoom { get { return zoom; } }

        internal string FrameSizeShown { get { return frameSizeName.Text; } }

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

            internal static float Shrink(Screen screen)
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

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            Shape();
            Invalidate(true);
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
                if (pageView >= PageTall) return;
                // A thin thumb on her right edge shows how much of the page is on screen, and where.
                float rail = Height - 36 * s;
                float thumb = rail * pageView / PageTall;
                float at = 18 * s + (rail - thumb) * pageScroll / (PageTall - pageView);
                using (Brush track = new SolidBrush(Dash)) graphics.FillRectangle(track, Width - 8 * s, 18 * s, 2 * s, rail);
                using (Brush lit = new SolidBrush(Cyan)) graphics.FillRectangle(lit, Width - 8 * s, at, 2 * s, thumb);
                return;
            }
            int shift = (expanded ? SetupHeight : 0) + KeyRow;
            int below = voiceFilled >= 0 ? InstallRow : 0;
            if (expanded) Dots(graphics, s, 210, filled);
            if (below > 0) Dots(graphics, s, InstallAt + 24 + shift - arrangedFold, voiceFilled);
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

        private float DeviceScale() { return CurrentAutoScaleDimensions.Width / 96f; }

        // Her Mainframe size on top of the screen's own scale: 1 unless a bigger size is picked, or Auto finds a desktop
        // bigger than the panel showing it.
        private float zoom = 1f;

        private float Unit() { return DeviceScale() * zoom; }

        // The scale of whichever of her windows a control is on: the Mainframe, their voice server's panel or her SECURITY
        // window, which the smoke test also draws at 150% and 200%.
        private static float ScaleOf(Control control)
        {
            Form form = control.FindForm();
            if (form == null) return 1f;
            Mainframe main = form as Mainframe;
            SecuritySheet guarded = form as SecuritySheet;
            LayersSheet layers = form as LayersSheet;
            float zoom = main != null ? main.zoom : guarded != null ? guarded.Zoom : layers != null ? layers.Zoom : 1f;
            return form.CurrentAutoScaleDimensions.Width / 96f * zoom;
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
            int below = voiceFilled >= 0 ? InstallRow : 0;
            int fold = arrangedFold = Fold;
            foreach (KeyValuePair<Control, int> entry in lower) entry.Key.Top = Scaled(entry.Value + shift + (entry.Value >= InstallAt ? below : 0) - (entry.Value > KeyAt ? fold : 0));
            installNote.Top = Scaled(InstallAt + 2 + shift - fold);
            installNote.Visible = below > 0;
            foreach (Label step in steps) step.Visible = setupShown;
            setup.Visible = setupShown;
            refresh.Top = Scaled(setupShown ? 226 : 134);
            brainNote.MaximumSize = new Size(Scaled(328), 0);
            voiceNote.MaximumSize = new Size(Scaled(328), 0);
            updateNote.MaximumSize = new Size(Scaled(328), 0);
            customNote.MaximumSize = new Size(Scaled(328), 0);
            if (keysNote != null) keysNote.MaximumSize = new Size(Scaled(328), 0);
            if (paged) pageView = Fit();
            ClientSize = new Size(Scaled(paged ? Wide + Column : Wide), Scaled(paged ? pageView : Tall + shift + below - fold));
            ScrollPage(pageScroll);
            bool scrolls = paged && pageView < PageTall;
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
            if (disposing) { Application.RemoveMessageFilter(this); modelMenu.Dispose(); voiceMenu.Dispose(); speechModelMenu.Dispose(); micMenu.Dispose(); languageMenu.Dispose(); freeMenu.Dispose(); }
            base.Dispose(disposing);
        }

        internal void Restore()
        {
            if (!Visible) Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        internal void Sync()
        {
            syncing = true;
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
            security.Text = "Security: " + Security.PresetName;
            blackwallNote.Text = Security.Short;
            if (guard != null && !guard.IsDisposed) guard.Sync();
            watch.Checked = capri.Watching;
            caughtUp.Checked = capri.CaughtUp;
            stayOnModel.Checked = capri.StayOnModel;
            remembering.Checked = capri.Remembers;
            frameSize.Value = Math.Max(0, Array.IndexOf(FrameSizes, capri.MainframeSize));
            ShowFrameSize();
            VoiceTier();
            int chosen = Array.IndexOf(Gaps, capri.GlanceGap);
            gap.Value = chosen < 0 ? 5 : chosen;
            gapName.Text = GapNames[gap.Value];
            if (!nickname.Box.Focused) nickname.Box.Text = capri.Nickname;
            string language = capri.Language;
            foreach (ToolStripMenuItem item in languageMenu.Items)
            {
                item.Checked = (string)item.Tag == language;
                if (item.Checked) languages.Text = item.Text;
            }
            languages.Invalidate();
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
            popsName.Text = PopNames[pops.Value];
            dance.Checked = capri.Dancing;
            bubble.Value = capri.BubbleTime;
            bubbleName.Text = BubbleNames[bubble.Value];
            intro.Checked = capri.Intro;
            typing.Checked = capri.TypingSound;
            ShowHdGames();
            miniPlayer.Checked = capri.PlayerOn;
            replies.Value = capri.Wordiness;
            repliesName.Text = Lengths[replies.Value];
            recall.Value = Math.Max(0, Array.IndexOf(Recalls, capri.Recall));
            recallName.Text = Lengths[recall.Value];
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

        internal void SlidePerch(int steps) { Press(perchFps, steps); }

        internal string PopsShown { get { return popsName.Text; } }

        internal void SlidePops(int steps) { Press(pops, steps); }

        // The HABITS row and KEYS fit inside the settings page.
        internal bool PerchFits { get { Rectangle page = PageArea; return page.Contains(perchFpsName.Bounds) && page.Contains(perchFps.Bounds) && page.Contains(hideKeys.Bounds) && page.Contains(quietKeys.Bounds); } }

        internal string Status { get { return brainName.Text + " | " + brainNote.Text; } }

        internal bool WatchShown { get { return watch.Checked; } }

        internal bool CaughtUpShown { get { return caughtUp.Checked; } }

        internal void FlipCaughtUp() { caughtUp.Checked = !caughtUp.Checked; }

        internal bool StayShown { get { return stayOnModel.Checked; } }

        internal void FlipStay() { stayOnModel.Checked = !stayOnModel.Checked; }

        internal bool RemembersShown { get { return remembering.Checked; } }

        internal void FlipRemembering() { remembering.Checked = !remembering.Checked; }

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
                    if (!a.Visible || !b.Visible || (a is Label && a.Text.Length == 0) || (b is Label && b.Text.Length == 0)) continue;
                    if (a.Bounds.IntersectsWith(b.Bounds)) return a.Text + " / " + b.Text;
                }
            return null;
        }

        // A settings control that pokes out past the page's edge, or null.
        internal string Outside()
        {
            Rectangle page = PageArea;
            foreach (KeyValuePair<Control, int> entry in pageLeft)
            {
                Control control = entry.Key;
                if (!control.Visible || (control is Label && control.Text.Length == 0)) continue;
                if (!page.Contains(control.Bounds)) return control.Text.Length > 0 ? control.Text : control.AccessibleName;
            }
            return null;
        }

        // The whole settings page, scrolled or not, in the window's own px.
        private Rectangle PageArea { get { return paged ? new Rectangle(0, -Scaled(pageScroll), ClientSize.Width, Math.Max(ClientSize.Height, Scaled(PageTall))) : ClientRectangle; } }

        // Settings on a short screen: how much shows (or whole) and whether the rims hold her frame together.
        internal string ScrollStatus { get { return (paged && pageView < PageTall ? pageView + " tall" : "whole") + " | " + (topRim.Visible && bottomRim.Visible ? "rims" : "-"); } }

        internal int Scrolled { get { return pageScroll; } }

        internal int ScrollMost { get { return Math.Max(0, PageTall - pageView); } }

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
        internal void Wheel(int notches)
        {
            Point at = perchFps.PointToScreen(new Point(perchFps.Width / 2, perchFps.Height / 2));
            for (int i = 0; i < Math.Abs(notches); i++)
                Native.SendMessage(perchFps.Handle, 0x020A, new IntPtr((notches > 0 ? 120 : -120) << 16), new IntPtr((at.Y << 16) | (at.X & 0xFFFF)));
        }

        // Tabs through Settings from Back and names the first control that gets focus without all of it showing, or null.
        internal string TabShows()
        {
            Control from = more;
            bool keys = false;
            for (int i = 0; i < 160; i++)
            {
                if (!SelectNextControl(from, true, true, true, true)) break;
                Control at = ActiveControl;
                if (at == null || at == from || at == more) break;
                Control whole = at;
                while (whole.Parent != this) whole = whole.Parent;
                if (!ClientRectangle.Contains(whole.Bounds)) return "Tab reached " + (whole.AccessibleName ?? whole.Text) + " half off the screen";
                keys = keys || whole == quietKeys;
                from = at;
            }
            more.Focus();
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
            modelMenu.Items.Clear();
            foreach (string name in installed)
            {
                string pick = name;
                ToolStripMenuItem item = new ToolStripMenuItem(name, null, delegate { capri.ChooseModel(pick); ShowOllama(lastInstalled, pick); });
                item.Checked = name == chosen;
                modelMenu.Items.Add(item);
            }
            Arrange(false);
        }

        internal void ShowCloud(Cloud remote, List<string> found)
        {
            brainName.Text = remote.Name + " · " + remote.Model;
            brainNote.Text = "Using your " + remote.Company + " key";
            models.Text = remote.Model;
            models.Visible = true;
            hint.Visible = false;
            modelMenu.Items.Clear();
            foreach (string name in found ?? new List<string> { remote.Model })
            {
                string pick = name;
                modelMenu.Items.Add(Choice(Brain.Shown(remote.Kind, name), name == remote.Model, delegate { capri.ChooseModel(pick); ShowCloud(remote, found); }));
            }
            Arrange(false);
        }

        private int KeyRow { get { return tier > 0 ? 128 : 94; } }

        internal void ShowTiers(int pick, string nvidiaTail, string paidTail)
        {
            tier = Math.Max(0, Math.Min(rungs.Length - 1, pick));
            syncing = true;
            for (int i = 0; i < rungs.Length; i++) rungs[i].Checked = i == tier;
            syncing = false;
            string tail = tier == 1 ? nvidiaTail : tier == 2 ? paidTail : null;
            textSaved = tail != null;
            textEntry.Box.AccessibleName = tier == 1 ? "NVIDIA API key" : "OpenAI, Anthropic or DeepSeek API key";
            tierNote.Text = tier == 1 ? "Most NVIDIA models can't see your screen." : "Pick one. Only one brain runs at a time.";
            textEntry.Cue = textSaved ? (tier == 1 ? "NVIDIA key saved, ends " : "Key saved, ends ") + tail : tier == 1 ? "Paste your free NVIDIA key" : "Paste a ChatGPT or Claude key";
            RelabelText();
            Arrange(expanded);
        }

        internal bool TiersFit
        {
            get
            {
                Rectangle last = rungs[rungs.Length - 1].Bounds;
                bool row = !textEntry.Visible || (textEntry.Top >= last.Bottom && textConnect.Right <= ClientSize.Width);
                int end = textEntry.Visible ? textEntry.Bottom : last.Bottom;
                return rungs[0].Top >= tierNote.Bottom && rungs[1].Top >= rungs[0].Bottom && rungs[2].Top >= rungs[1].Bottom && freeKey.Left >= rungs[1].Right && freeKey.Top >= rungs[1].Top && freeKey.Bottom <= rungs[1].Bottom && row && end <= voiceHead.Top && rungs[0].Right <= ClientSize.Width;
            }
        }

        internal string TextStatus { get { return "tier " + tier + " | " + brainName.Text + " | " + brainNote.Text + " | " + (textEntry.Visible ? textEntry.Cue + " | " + textConnect.Text : "no key row"); } }

        private void ConnectText()
        {
            string typed = textEntry.Box.Text.Trim();
            if (typed.Length > 0) { textEntry.Box.Text = ""; capri.ConnectText(typed); }
            else if (textSaved) capri.ForgetText();
            else textEntry.Box.Focus();
        }

        private void RelabelText()
        {
            bool forget = textSaved && textEntry.Box.Text.Trim().Length == 0;
            textConnect.Text = forget ? "Forget" : "Connect";
            textConnect.Accent = forget ? Hot : Rose;
            textConnect.Invalidate();
        }

        internal void ShowLocal(Pick pick)
        {
            brainName.Text = "Built-in · " + pick.Name;
            brainNote.Text = "Running on this PC";
            models.Visible = false;
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
            else Invalidate(new Rectangle(0, Scaled(InstallAt + 20 + (expanded ? SetupHeight : 0) + KeyRow - arrangedFold), Width, Scaled(14)));
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
        internal void ShowFree(bool ready, string chosen, bool busy, bool hdFits, bool yours, bool kept, string served = null)
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
                ToolStripMenuItem own = Choice(!hdFits ? "HD · needs a 6 GB graphics card" : "HD · graphics card, " + Engine.Amount(QwenVoice.PackSize), hd, delegate { PickYourVoice(); });
                own.Enabled = hdFits;
                freeMenu.Items.Add(own);
            }
            // Only her HD voice clones from a clip; her other styles are set voices.
            if (yours || kept)
            {
                ToolStripMenuItem theirs = Choice("Your voice (HD)", yoursShown, delegate { if (yours) capri.ChooseVoiceStyle(QwenVoice.Style); else capri.UseKeptVoice(); });
                theirs.Enabled = hdFits;
                freeMenu.Items.Add(theirs);
            }
            ToolStripMenuItem pick = Choice(yours || kept ? "Pick a new clip of you..." : "Your voice (HD) · pick a clip of you...", false, delegate { PickYourVoice(); });
            pick.Enabled = hdFits;
            freeMenu.Items.Add(pick);
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

        // Her VOICE LAYERS window, over the Mainframe like SECURITY.
        private void OpenLayers()
        {
            if (layered != null && !layered.IsDisposed && layered.Zoom != zoom) layered.Close();
            if (layered == null || layered.IsDisposed) layered = new LayersSheet(this, zoom);
            layered.Sync();
            if (!layered.Visible) layered.Show(this);
            layered.Activate();
        }

        // The Layers button is lit while they're on and says so to a screen reader, and the window follows Settings' Pitch.
        private void ShowLayers()
        {
            bool on = capri.VoiceLayers.On;
            layersKey.AccessibleDescription = on ? "On" : "Off";
            if (layersKey.Picked != on) { layersKey.Picked = on; layersKey.Invalidate(); }
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

        // VOICE's heading from 100% to 200%: "Every voice, ElevenLabs too" and the Layers button each fit their own text, and
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

        internal void ShowKeys(bool type, bool hide, bool quiet)
        {
            typeReady = type;
            hideReady = hide;
            quietReady = quiet;
            KeyLabels();
        }

        internal void Hotkeys(string talk, string mute, string type, string hide, string quiet)
        {
            talkName = talk;
            muteName = mute;
            typeName = type;
            hideName = hide;
            quietName = quiet;
            KeyLabels();
        }

        // Which of her keys to change: 0 talk, 1 mute mic, 2 type to her, 3 hide or show her, 4 quiet.
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
                // A modifier on its own only shows until a key joins it, and never counts as her key.
                if (pendingMods < 0) { heldMods = mods; KeyLabels(); }
                return;
            }
            if (!Companion.Bindable(mods, (int)key)) return;
            pendingMods = mods;
            pendingVk = (int)key;
            KeyLabels();
            string name = Companion.KeyName(pendingMods, pendingVk);
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
            KeysNote("Saved. " + (slot == 0 ? talkName + " starts voice chat." : slot == 1 ? muteName + " mutes your mic." : slot == 2 ? typeName + " opens her typing box." : slot == 3 ? hideName + " hides or shows her." : quietName + " keeps her quiet, and lets her talk again.") + (Companion.Swallows(mods, vk) ? " It won't type in other apps while she runs." : Companion.ByMouse(vk) ? " It still does its own job in other apps too." : ""), false);
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

        private Control PickedKey { get { return capturing == 1 ? talkKeys : capturing == 2 ? muteKeys : capturing == 3 ? typeKeys : capturing == 4 ? hideKeys : capturing == 5 ? quietKeys : null; } }

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
            if (capturing == 0) return base.ProcessCmdKey(ref msg, keyData);
            TakeKeys(keyData, WinHeld());
            return true;
        }

        internal bool Picking { get { return capturing != 0; } }

        // While a key is being picked, middle click or a side mouse button anywhere on the Mainframe is picked like a key press.
        public bool PreFilterMessage(ref Message m)
        {
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
            confirmKeys.Visible = cancelKeys.Visible = capturing != 0;
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

        internal void ShowMic(string name)
        {
            mics.Text = "Mic: " + (name ?? "default");
            mics.Invalidate();
        }

        private void ShowMics()
        {
            string chosen = capri.MicId;
            List<Mic> found = Ears.List();
            micMenu.Items.Clear();
            micMenu.Items.Add(Choice("Windows default", string.IsNullOrEmpty(chosen), delegate { capri.ChooseMic(null); }));
            if (found.Count > 0) micMenu.Items.Add(new ToolStripSeparator());
            foreach (Mic mic in found)
            {
                string id = mic.Id;
                micMenu.Items.Add(Choice(mic.Name, id == chosen, delegate { capri.ChooseMic(id); }));
            }
            micMenu.Show(mics, new Point(0, mics.Height));
        }

        internal string MicStatus { get { return "mic | " + mics.Text + " | " + (keyHelp.Visible ? "help" : "-") + " | " + (shortcut.Visible ? (shortcutReady ? "keys" : "taken") : "-") + " | " + (muteShortcut.Visible ? (muteReady ? "mute" : "taken") : "-"); } }

        // Windows' Online speech recognition switch is hard to find (Baguettboy couldn't), so a note that asks for it opens its page.
        internal void VoiceNote(string text, bool problem)
        {
            speechFix = text != null && text.IndexOf("Online speech recognition", StringComparison.Ordinal) >= 0;
            voiceNote.Text = speechFix ? text + OpenSpeech : text;
            voiceNote.ForeColor = problem ? Hot : Faint;
            voiceNote.Cursor = speechFix ? Cursors.Hand : Cursors.Default;
        }

        internal const string OpenSpeech = " Click to open it.";
        internal bool SpeechFixShown { get { return speechFix && voiceNote.Cursor == Cursors.Hand && voiceNote.Text.EndsWith(OpenSpeech, StringComparison.Ordinal); } }

        internal string VoiceNoteText { get { return voiceNote.Text; } }

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

        private static ToolStripMenuItem Choice(string text, bool chosen, EventHandler click)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text, null, click);
            item.Checked = chosen;
            return item;
        }

        private ContextMenuStrip Dropdown()
        {
            ContextMenuStrip strip = new ContextMenuStrip();
            strip.Renderer = new MenuLook();
            strip.BackColor = Ground;
            strip.ForeColor = White;
            strip.Font = Font;
            strip.ShowImageMargin = false;
            strip.ShowCheckMargin = true;
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

        private Key Make(string text, int x, int y, int width, int height, EventHandler click)
        {
            Key key = new Key();
            key.Text = text;
            key.Font = strong;
            key.Bounds = new Rectangle(x, y, width, height);
            key.Click += click;
            Controls.Add(key);
            return key;
        }

        private sealed class Note : Label
        {
            internal Note() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true); }

            protected override void OnPaintBackground(PaintEventArgs e) { Backdrop(e.Graphics, this, ScaleOf(this)); }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
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

            private string Shown { get { return Text.EndsWith(" >", StringComparison.Ordinal) ? Text.Substring(0, Text.Length - 2) : Text; } }

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
        private sealed class Key : Button
        {
            internal Color Accent = Rose;
            internal bool Loud;
            internal bool Picked;
            internal bool Chevron;
            internal int Glyph;
            private bool hot;
            private bool down;

            internal Key()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
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
                if (!Enabled) { face = Field; rim = Color.FromArgb(52, 22, 32); ink = Color.FromArgb(112, 94, 104); }
                else if (Picked) { face = hot ? Color.FromArgb(160, 248, 251) : Cyan; rim = face; ink = OnInk; }
                else if (Accent == Cyan) { face = down ? Color.FromArgb(18, 62, 67) : hot ? Color.FromArgb(13, 45, 49) : Deep; rim = lit ? Cyan : Loud ? DeepEdge : Color.FromArgb(28, 84, 92); ink = Cyan; }
                else if (Accent == Hot) { face = down ? Color.FromArgb(72, 18, 30) : hot ? Color.FromArgb(54, 16, 28) : Color.FromArgb(34, 12, 22); rim = lit ? Color.FromArgb(255, 124, 122) : Hot; ink = Color.FromArgb(255, 166, 162); }
                else { face = down ? Pressed : hot ? Hover : Field; rim = lit ? Hot : Edge; ink = Chevron ? Cyan : Accent; }
                using (GraphicsPath plate = Cut(box, 0, 0, 5 * s, 0))
                {
                    using (Brush brush = new SolidBrush(face)) graphics.FillPath(brush, plate);
                    using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, plate);
                }
                if (Chevron) Drop(graphics, Width - 19 * s, y, s, Enabled ? Cyan : ink, hot);
                Rectangle area = Chevron ? new Rectangle((int)(10 * s), 0, Width - (int)(32 * s), Height) : ClientRectangle;
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | (Chevron ? TextFormatFlags.Left : TextFormatFlags.HorizontalCenter);
                TextRenderer.DrawText(graphics, Text, Font, area, ink, flags);
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

        // A slider's value, shown inside its own box where a label beside it used to be. Code that set that label's text or
        // colour sets these instead.
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
            internal static readonly Color Back = Color.FromArgb(16, 13, 22);
            public override Color ToolStripDropDownBackground { get { return Back; } }
            public override Color ImageMarginGradientBegin { get { return Back; } }
            public override Color ImageMarginGradientMiddle { get { return Back; } }
            public override Color ImageMarginGradientEnd { get { return Back; } }
            public override Color MenuBorder { get { return Edge; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(48, 18, 32); } }
            public override Color MenuItemBorder { get { return Hot; } }
            public override Color CheckBackground { get { return DeepEdge; } }
            public override Color CheckSelectedBackground { get { return Color.FromArgb(60, 160, 172); } }
            public override Color CheckPressedBackground { get { return Color.FromArgb(60, 160, 172); } }
            public override Color SeparatorDark { get { return Edge; } }
            public override Color SeparatorLight { get { return Back; } }
        }

        // Her menus, the right-click one and the Mainframe's dropdowns: a dark wine panel cut at the corners with a red rim and
        // hot red strokes over the two big corners, like her windows. The choice under the mouse sits on a band with a red tick
        // and its words go cyan, ticks are small cyan boxes, a submenu has a ▷, and a menu's title, like CAPRI / 0.7.7, is rose.
        internal sealed class MenuLook : ToolStripProfessionalRenderer
        {
            internal static readonly object Title = new object();
            internal static readonly Color Panel = MenuColors.Back;
            internal static readonly Color Ink = White;
            private static readonly Color Chosen = Color.FromArgb(48, 18, 32);

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

        // A voice model for CUSTOM LOCAL or CLOUD VOICE: a ready-made choice like Fish Audio, or their own server. Its address, an
        // optional model, voice and key, Test, and Use this server.
        private sealed class ServerSheet : Form
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
                this.owner = owner;
                Zoom = zoom;
                Text = VoiceServer.Menu;
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
                throw new InvalidOperationException("The voice model panel has no " + name + " to pick.");
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
                throw new InvalidOperationException("The voice server panel has no " + button + " button to press.");
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
        private sealed class SecuritySheet : Form
        {
            internal const int Wide = 900, Tall = 470, Column = 410;
            internal readonly float Zoom;
            private readonly Mainframe owner;
            private readonly Slider presets;
            private readonly Readout presetName;
            private readonly Label presetLine;
            private readonly Toggle[] toggles = new Toggle[Security.Count];
            private bool syncing;

            internal SecuritySheet(Mainframe owner, float zoom)
            {
                this.owner = owner;
                Zoom = zoom;
                Text = "Security";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoScaleDimensions = new SizeF(96, 96);
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
                presets.Font = Type(9.5f * zoom);
                presets.Bounds = Box(120, 79, 260, 22);
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
                    int y = i <= Security.Mouse ? 168 + 46 * i : i < Security.Wake ? 168 + 46 * (i - Security.Sends) : 386;
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
                presetName.ForeColor = preset == Security.Custom ? Look.Yellow : Cyan;
                presetLine.Text = Security.PresetLine(preset);
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
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                    if (!ClientRectangle.Contains(a.Bounds) || a.Right > ClientSize.Width - (int)(16 * s)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds + " in " + ClientSize;
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

        // Her VOICE LAYERS window, in SECURITY's look (Lachy, 29 Oct): a switch, then MAIN, her voice itself, whose pitch is
        // Settings > VOICE > Pitch and moves with it, and LAYER 1 and LAYER 2, copies of her mixed in under her, each with its
        // own pitch (half steps from -12 to +12 semitones off hers) and delay (0 to 250 ms). Letting go of a slider has her say
        // a line in it, and so does the switch. It opens from the Layers button in VOICE's heading. Zoom is 1 except in the smoke
        // test, which draws it at 150% and 200% too.
        private sealed class LayersSheet : Form
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
                this.owner = owner;
                Zoom = zoom;
                Text = "Voice layers";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.Dpi;
                AutoScaleDimensions = new SizeF(96, 96);
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
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible || (a is Label && a.Text.Length == 0)) continue;
                    if (!ClientRectangle.Contains(a.Bounds) || a.Right > ClientSize.Width - (int)(16 * s)) return "pokes out: " + (a.Text.Length > 0 ? a.Text : a.AccessibleName) + " " + a.Bounds + " in " + ClientSize;
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

        // A row's value, in a box of its own. Up to seven steps it's a selector, ◁ value ▷ with a red dash under the value
        // marking where it is among them; past that, a bar whose red handle slides along under the value. Both step with the
        // arrow keys, and say what they're on to a screen reader.
        private sealed class Slider : Control
        {
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
            internal string Shown { get { return shown ?? labels[value]; } set { shown = value; Invalidate(); } }

            internal Color Ink { get { return ink.IsEmpty ? Cyan : ink; } set { ink = value; Invalidate(); } }

            private bool Stepper { get { return labels.Length <= 7; } }

            private void Place(int wanted)
            {
                int next = Math.Max(0, Math.Min(labels.Length - 1, wanted));
                if (next == value) return;
                value = next;
                AccessibleDescription = labels[next];
                Invalidate();
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
                Color rim = Focused && ShowFocusCues ? Cyan : hot || sliding ? Color.FromArgb(156, 40, 56) : Edge;
                TextFormatFlags flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
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
                    return;
                }
                float travel = Width - 2 * s - Grip;
                float knob = s + travel * value / (labels.Length - 1);
                using (GraphicsPath plate = Cut(box, 0, 0, 5 * s, 0))
                {
                    using (Brush fill = new SolidBrush(Field)) graphics.FillPath(fill, plate);
                    using (Brush done = new SolidBrush(Color.FromArgb(42, 14, 25))) graphics.FillRectangle(done, line, line, knob - line, Height - 2 * line);
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
                TextRenderer.DrawText(graphics, Shown, Font, area, Color.FromArgb(40, 6, 12), flags);
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
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(left, 0, Width - left, Height), Checked ? Cyan : White, flags);
                int after = left + TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, flags).Width + (int)(8 * s);
                if (Hint != null) TextRenderer.DrawText(graphics, Hint, HintFont ?? Font, new Rectangle(after, 0, Math.Max(0, Width - after), Height), Faint, flags);
                if (Flag != null) TextRenderer.DrawText(graphics, Flag, FlagFont ?? Font, new Rectangle(0, 0, Width - (int)(6 * s), Height), Look.Yellow, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        // A setting that's on or off: its name on the left of its row and a pair of buttons on the right, OFF and ON, with the
        // one it's set to lit, solid cyan for ON and red for OFF. A click on either picks it; a click on the name, or Space,
        // flips it.
        private sealed class Toggle : CheckBox
        {
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
                if (small == null) small = Type(Font.SizeInPoints - 0.75f, true);
                float line = Math.Max(1f, s), wide = Switch * s, gap = 4 * s, half = (wide - gap) / 2, start = Width - wide;
                RectangleF off = new RectangleF(start + line / 2, line / 2, half - line, Height - line);
                RectangleF on = new RectangleF(start + half + gap + line / 2, line / 2, half - line, Height - line);
                Color offFill, offRim, offInk, onFill, onRim, onInk;
                if (Checked)
                {
                    offFill = over == 0 ? Color.FromArgb(50, 16, 28) : Color.FromArgb(32, 12, 19);
                    offRim = over == 0 ? Color.FromArgb(120, 36, 50) : Color.FromArgb(50, 18, 28);
                    offInk = over == 0 ? Color.FromArgb(170, 60, 72) : Color.FromArgb(84, 26, 38);
                    onFill = onRim = Cyan;
                    onInk = OnInk;
                }
                else
                {
                    offFill = Color.FromArgb(155, 50, 46);
                    offRim = Color.FromArgb(222, 69, 63);
                    offInk = Color.FromArgb(255, 150, 144);
                    onFill = over == 1 ? Color.FromArgb(12, 46, 50) : Color.FromArgb(9, 30, 33);
                    onRim = over == 1 ? DeepEdge : Color.FromArgb(16, 44, 48);
                    onInk = over == 1 ? Color.FromArgb(110, 210, 216) : Color.FromArgb(28, 68, 72);
                }
                if (!Enabled) { offFill = onFill = Field; offRim = onRim = Color.FromArgb(52, 22, 32); offInk = onInk = Color.FromArgb(112, 94, 104); }
                TextFormatFlags centre = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
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
                TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
                int room = NameRoom;
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle(0, 0, room, Height), Enabled ? Rose : Faint, flags);
                if (Hint == null) return;
                TextFormatFlags measure = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix;
                int after = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, measure).Width + (int)(10 * s);
                Font hint = HintFont ?? Font;
                if (after + TextRenderer.MeasureText(graphics, Hint, hint, Size.Empty, measure).Width <= room) TextRenderer.DrawText(graphics, Hint, hint, new Rectangle(after, 0, room - after, Height), Faint, flags);
            }
        }
    }
}
