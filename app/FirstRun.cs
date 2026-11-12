using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her first launch on a new install (Lachy, 12 Nov): she glitches in, says hi and a small card in the Mainframe's look
    // opens beside her. It picks a brain (her own on this PC is the default, a free key or their plan is one click away), sets
    // how she sounds and her mic, then the EXTRAS. Every screen can be skipped, and so can the whole card: she just stands there
    // ready, or the Mainframe opens if she still has no brain. Her own brain is a 3 GB download, so it starts the moment they
    // press Next and the voice and EXTRAS screens run while it comes down, with its progress on the card. Her first real reply
    // comes when it's in, wherever they are by then.
    internal static class FirstRun
    {
        internal const int None = 0, Pick = 1, Key = 2, Online = 3, Voice = 4, Extras = 5, Plan = 6, Wait = 7, Alone = 8;
        internal const int Free = 0, Local = 1, Own = 2, NoAi = 3;
        internal const int NoBrain = 0, Coming = 1, Connecting = 2, Ready = 3, Stopped = 4;

        internal const string Hello = "Hey, choom. I'm Capri. I need a brain before I can talk back. Pick one.";
        internal const string GoogleLine = "Sign in, press Create API key and copy it. Then press Paste and I'll connect.";
        internal const string KeyLine = "Sign in, make a key and copy it. Then press Paste and I'll connect.";
        internal const string PlanLine = "Got a Claude or ChatGPT plan, or a key of your own? Pick it and I'll plug in.";
        internal const string WhileLine = "My brain's on its way. While it comes down, let's sort out how I sound.";
        internal const string VoiceLine = "This is my free voice. Press Test, say something, and I'll show you I can hear you.";
        internal const string NoVoiceLine = "Press Test and say something, so I know I can hear you.";
        internal const string ExtrasLine = "Last thing. These cost a little or use your mic, so they're your pick.";
        internal const string ExtrasMeetLine = "Last thing. These cost a little or use your mic, so they're your pick. Then I want to know what to call you.";
        internal const string WaitLine = "Almost there. I'll say hi the second my brain's in.";
        internal const string BackLine = "Welcome back, choom. Let's finish setting me up.";
        internal const string AloneLine = "Got it, just me. Right-click me any time for Capri Mainframe.";
        internal static readonly string[] Lines = { Hello, GoogleLine, KeyLine, PlanLine, WhileLine, VoiceLine, NoVoiceLine, ExtrasLine, ExtrasMeetLine, WaitLine, BackLine, AloneLine };

        // The free key pages, Google AI Studio first: any Google account works and it doesn't ask for a phone.
        internal static readonly string[] Providers = { "Google AI Studio", "NVIDIA", "OpenRouter" };
        internal static readonly string[] ProviderNotes =
        {
            "Any Google account works. I opened the page in your browser.",
            "It may ask for a phone number. I opened the page in your browser.",
            "Pick a free model. I opened the page in your browser."
        };
        internal static readonly string[] MakeSteps = { "Press Create API key", "Press Generate API Key", "Press Create Key" };
        internal const string Faster = "Don't want to wait? A free key gets me talking in about a minute.";
        internal const string Heard = "Mic test finished. Sound is reaching Capri.";
    }

    internal sealed partial class Mainframe
    {
        internal FirstRunCard NewFirstRun(float scale)
        {
            return new FirstRunCard(this, scale);
        }

        // The Mainframe's own mic menu, dropped under the card's mic box.
        private void FirstRunMics(Control under)
        {
            MicChoices(null);
            micMenu.Show(under, new Point(0, under.Height));
            capri.LookMics();
        }

        internal sealed class FirstRunCard : Form, LookWindow
        {
            internal const int Wide = 600;
            private readonly Mainframe owner;
            private readonly Companion capri;
            internal readonly float Unit;
            private readonly List<Font> fonts = new List<Font>();
            private readonly System.Windows.Forms.Timer pulse = new System.Windows.Forms.Timer();
            internal int Screen = FirstRun.None;
            internal int Picked = FirstRun.Local;
            internal int Brain = FirstRun.NoBrain;
            internal int Provider;
            internal bool ExtrasSeen, BrainSkipped, Finished;
            private int returnTo = FirstRun.Voice;
            private string checking, problem;
            private bool waitingOllama, pendingLocal, wasTesting, quiet, moved;
            private Rectangle placedBy;
            private int shownBrain;

            private Pickable[] picks;
            private Pickable voiceFree, voiceText;
            private Entry keyBox;
            private Note status, heard;
            private Gauge gauge;
            private Link faster;
            private Bars bars;
            private Key micPick, micTest;
            private Toggle[] extras;
            private Key[] tabs;
            private Key primary;
            internal string Line;

            internal FirstRunCard(Mainframe owner, float scale)
            {
                SuspendLayout();
                this.owner = owner;
                capri = owner.capri;
                Text = "Capri";
                Icon = owner.Icon;
                ShowInTaskbar = false;
                // She's always on top and this sits beside her, so it is too.
                TopMost = true;
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Ground;
                ForeColor = White;
                DoubleBuffered = true;
                Font = Type(9.5f);
                ClientSize = new Size(Wide, 400);
                // Its controls are made at its scale as each screen is built, so the rescaler only carries the scale and the
                // window's own font, and follows a move to a screen with other scaling.
                if (scale > 0) new Rescaler(this, scale, 1f, owner.layoutFallback);
                else
                {
                    // Its tallest screen has to fit above the taskbar, so a short screen with big scaling gets it a little smaller.
                    float device = owner.DeviceScale(), text = owner.TextScale();
                    float fits = FitScale(device * text, System.Windows.Forms.Screen.FromRectangle(capri.Bounds).WorkingArea.Height);
                    if (fits < device * text) new Rescaler(this, fits, 1f, owner.layoutFallback);
                    else new Rescaler(this, device, text, owner.layoutFallback);
                }
                Unit = ScaleOf(this);
                pulse.Interval = 80;
                pulse.Tick += delegate { Pulse(); };
            }

            // The tallest a screen gets, in the card's own px: FREE KEY with her stopped download along the bottom.
            internal const int Tallest = 700;

            internal static float FitScale(float wanted, int roomHigh) { return Math.Min(wanted, Math.Max(0.75f, (roomHigh - 8) / (float)Tallest)); }

            private int Z(float value) { return (int)Math.Round(value * Unit); }

            private Rectangle Box(int x, int y, int width, int height) { return Rectangle.FromLTRB(Z(x), Z(y), Z(x + width), Z(y + height)); }

            private Font Keep(Font font) { fonts.Add(font); return font; }

            // Bahnschrift's semi-condensed cut at a size in px, as the mocks give it, or its semi-bold for names and buttons.
            private Font Body(float px, bool bold = false)
            {
                FontFamily family = bold ? Heavy : Plain;
                return Keep(family != null ? new Font(family, px * Unit, FontStyle.Regular, GraphicsUnit.Pixel) : Look.Mono((px - 1) * Unit, bold ? FontStyle.Bold : FontStyle.Regular));
            }

            private Font Title(float px) { return Keep(Narrow != null ? new Font(Narrow, px * Unit, FontStyle.Regular, GraphicsUnit.Pixel) : Look.Mono((px - 4) * Unit, FontStyle.Bold)); }

            private Font Mono(float px) { return Keep(Look.Mono(px * Unit, FontStyle.Regular)); }

            // The screens in the order this path takes them, for "2 OF 5".
            private int[] Route()
            {
                if (Picked == FirstRun.NoAi && !BrainSkipped) return new[] { FirstRun.Pick, FirstRun.Alone };
                if (BrainSkipped) return new[] { FirstRun.Pick, FirstRun.Voice, FirstRun.Extras };
                if (Picked == FirstRun.Local) return new[] { FirstRun.Pick, FirstRun.Voice, FirstRun.Extras, FirstRun.Online };
                return new[] { FirstRun.Pick, Picked == FirstRun.Own ? FirstRun.Plan : FirstRun.Key, FirstRun.Online, FirstRun.Voice, FirstRun.Extras };
            }

            internal string Count
            {
                get
                {
                    int[] route = Route();
                    int at = Array.IndexOf(route, Screen == FirstRun.Wait ? FirstRun.Online : Screen);
                    return at < 0 ? "" : (at + 1) + " OF " + route.Length;
                }
            }

            internal void Begin(int screen, int pick, bool resumed)
            {
                Picked = pick;
                quiet = false;
                Go(screen);
                if (resumed) capri.FirstRunSay(FirstRun.BackLine);
                pulse.Start();
            }

            // Builds a screen from scratch: its title and count, what's on it, the brain's progress while it comes down, and the
            // bottom row with Skip, Skip all and the step to take.
            internal void Go(int screen)
            {
                if (Finished) return;
                shownBrain = Brain;
                bool leavingVoice = Screen == FirstRun.Voice && screen != FirstRun.Voice;
                if (leavingVoice && owner.MicTesting) capri.TestMic();
                Screen = screen;
                if (screen == FirstRun.Extras) ExtrasSeen = true;
                SuspendLayout();
                List<Control> old = new List<Control>();
                foreach (Control control in Controls) old.Add(control);
                Controls.Clear();
                // The button that was pressed is still finishing its click, so the old screen goes once that's done.
                List<Font> gone = new List<Font>(fonts);
                fonts.Clear();
                Action drop = delegate { foreach (Control control in old) control.Dispose(); foreach (Font font in gone) font.Dispose(); };
                if (IsHandleCreated) BeginInvoke(drop); else drop();
                picks = null; voiceFree = voiceText = null; keyBox = null; status = heard = null; gauge = null; faster = null; bars = null; micPick = micTest = null; extras = null; tabs = null; primary = null;
                string[] titles = { "", "WAKE ME UP >", "FREE KEY >", "BRAIN ONLINE >", "HOW I SOUND >", "LAST THING >", "MY PLAN OR KEY >", "ALMOST THERE >", "JUST ME >" };
                Heading(this, titles[screen], new Point(Z(28), Z(18)), Title(30), Rose, 0);
                Note count = Caption(Count, Wide - 28 - 120, 28, 120, 20, Mono(16), Faint);
                count.TextAlign = ContentAlignment.TopRight;
                int y;
                if (screen == FirstRun.Pick) y = BuildPick();
                else if (screen == FirstRun.Key) y = BuildKey();
                else if (screen == FirstRun.Plan) y = BuildPlan();
                else if (screen == FirstRun.Online) y = BuildOnline();
                else if (screen == FirstRun.Voice) y = BuildVoice();
                else if (screen == FirstRun.Extras) y = BuildExtras();
                else if (screen == FirstRun.Alone) y = BuildAlone();
                else y = BuildWait();
                y = Strip(y);
                y = BottomRow(y);
                ClientSize = new Size(Z(Wide), Z(y));
                ResumeLayout(false);
                Clip(this, Unit);
                Place();
                Invalidate(true);
                Refresh();
                capri.FirstRunAt(screen, Picked);
                if (!quiet) SayFor(screen);
            }

            private void SayFor(int screen)
            {
                string line = screen == FirstRun.Pick ? FirstRun.Hello
                    : screen == FirstRun.Key ? (Provider == 0 ? FirstRun.GoogleLine : FirstRun.KeyLine)
                    : screen == FirstRun.Plan ? FirstRun.PlanLine
                    : screen == FirstRun.Voice ? (Brain == FirstRun.Coming ? FirstRun.WhileLine : capri.FreeVoiceIn && !capri.Silent ? FirstRun.VoiceLine : FirstRun.NoVoiceLine)
                    : screen == FirstRun.Extras ? (Brain == FirstRun.Ready && !capri.Met ? FirstRun.ExtrasMeetLine : FirstRun.ExtrasLine)
                    : screen == FirstRun.Wait ? FirstRun.WaitLine
                    : screen == FirstRun.Alone ? FirstRun.AloneLine : null;
                // Her brain-online hello is her own words, from the brain itself.
                if (line == null) return;
                Line = line;
                capri.FirstRunSay(line);
            }

            // Beside her, and it follows her when she's dragged or lands, until they drag the card somewhere themselves. Then it
            // only stays on the screen as it changes size.
            private void Place()
            {
                Rectangle her = capri.Bounds;
                placedBy = her;
                Rectangle area = System.Windows.Forms.Screen.FromRectangle(moved ? Bounds : her).WorkingArea;
                if (moved) { Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, Left)), Math.Max(area.Top, Math.Min(area.Bottom - Height, Top))); return; }
                int bubbleLeft, bubbleRight;
                capri.BubbleSpan(area, out bubbleLeft, out bubbleRight);
                Location = Beside(her, bubbleLeft, bubbleRight, area, Size, Unit);
            }

            // On her left when there's room, clear of her and of her bubble above her, with its middle level with hers. On a
            // screen too narrow for both sides, it goes where it pokes over her bubble least, and always stays on the screen.
            internal static Point Beside(Rectangle her, int bubbleLeft, int bubbleRight, Rectangle area, Size card, float unit)
            {
                int left, right;
                Sides(her, bubbleLeft, bubbleRight, card.Width, unit, out left, out right);
                int x = left >= area.Left ? left : right + card.Width <= area.Right ? right : area.Left - left < right + card.Width - area.Right ? area.Left : area.Right - card.Width;
                int y = her.Top + her.Height / 2 - card.Height / 2;
                return new Point(Math.Max(area.Left, Math.Min(area.Right - card.Width, x)), Math.Max(area.Top, Math.Min(area.Bottom - card.Height, y)));
            }

            // Where the card's left edge goes to sit clear of her and her bubble on her left, or on her right.
            private static void Sides(Rectangle her, int bubbleLeft, int bubbleRight, int wide, float unit, out int left, out int right)
            {
                int middle = her.Left + her.Width / 2, reach = (int)(her.Width * 0.22f) + (int)Math.Round(28 * unit), gap = (int)Math.Round(10 * unit);
                left = Math.Min(middle - reach, bubbleLeft - gap) - wide;
                right = Math.Max(middle + reach, bubbleRight + gap);
            }

            // How far she steps aside when the card fits neither side of her, like the middle of a 1366 px screen: the least
            // that clears one side, or 0.
            internal static int Nudge(Rectangle her, int bubbleLeft, int bubbleRight, Rectangle area, int wide, float unit)
            {
                int left, right;
                Sides(her, bubbleLeft, bubbleRight, wide, unit, out left, out right);
                if (left >= area.Left || right + wide <= area.Right) return 0;
                int toRight = area.Left - left, toLeft = right + wide - area.Right;
                return toRight < toLeft ? toRight : -toLeft;
            }

            // The same screen drawn again without her saying its line again.
            internal void Redraw() { quiet = true; Go(Screen); quiet = false; }

            private Note Caption(string text, int x, int y, int width, int height, Font font, Color colour)
            {
                Note label = new Note();
                label.Text = text;
                label.Font = font;
                label.ForeColor = colour;
                label.BackColor = Ground;
                label.AutoSize = false;
                label.UseMnemonic = false;
                label.Bounds = Box(x, y, width, height);
                Controls.Add(label);
                return label;
            }

            private Key MakeKey(string text, int x, int y, int width, int height, Font font, Color accent, EventHandler click)
            {
                Key key = new Key();
                key.Text = text;
                key.Font = font;
                key.Accent = accent;
                key.Bounds = Box(x, y, width, height);
                key.Click += click;
                Controls.Add(key);
                return key;
            }

            private Link Linked(string text, int x, int y, Font font, Color colour, EventHandler click)
            {
                Link link = new Link();
                link.Text = text;
                link.Font = font;
                link.Ink = colour;
                link.AccessibleName = text;
                Size size = TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                link.Bounds = new Rectangle(Z(x), Z(y), size.Width + Z(4), Math.Max(size.Height, Z(24)));
                link.Click += click;
                Controls.Add(link);
                return link;
            }

            private Pickable Option(int y, int height, string title, string body, string aside, string flag, bool picked, EventHandler chosen)
            {
                Pickable pick = new Pickable();
                pick.Title = title;
                pick.Body = body;
                pick.Aside = aside;
                pick.Flag = flag;
                pick.Picked = picked;
                pick.TitleFont = Body(22, true);
                pick.FlagFont = Body(17);
                pick.AsideFont = Mono(16);
                pick.Font = Body(18);
                pick.AccessibleName = title + ". " + body;
                pick.Bounds = Box(28, y, Wide - 56, height);
                pick.Click += chosen;
                Controls.Add(pick);
                return pick;
            }

            private int BuildPick()
            {
                Caption("Pick a brain so I can talk back. You can change it any time.", 28, 74, Wide - 56, 26, Body(19), Faint);
                string fit = capri.FitName, size = capri.FitSize;
                string local = fit == null ? "Free and private, works offline. Downloads the best model for this PC." : "Free and private, works offline. Downloads " + fit + " (" + size + "), the best fit for this PC.";
                picks = new Pickable[4];
                int[] order = { FirstRun.Local, FirstRun.Free, FirstRun.Own, FirstRun.NoAi };
                string[] names = { "ON THIS PC", "FREE KEY", "MY PLAN OR KEY", "NO AI" };
                string[] bodies = { local, "A free key from Google AI Studio, NVIDIA or OpenRouter. Nothing to download. Needs internet.", "Claude or ChatGPT plan, a paid key (OpenAI, Claude, DeepSeek), or Ollama.", "Nothing to download, nothing sent anywhere. I won't talk back. Add a brain any time in the Mainframe." };
                string[] asides = { capri.FitMinutes, "about 1 min", "about 1 min", null };
                for (int i = 0; i < 4; i++)
                {
                    int choice = order[i];
                    picks[i] = Option(116 + 92 * i, 86, names[i], bodies[i], asides[i], i == 0 ? "Start with this!" : null, Picked == choice, delegate { PickBrain(choice); });
                }
                return 116 + 92 * 4;
            }

            internal void PickBrain(int choice)
            {
                Picked = choice;
                int[] order = { FirstRun.Local, FirstRun.Free, FirstRun.Own, FirstRun.NoAi };
                for (int i = 0; i < picks.Length; i++) { picks[i].Picked = order[i] == choice; picks[i].Invalidate(); }
            }

            private int BuildKey()
            {
                tabs = new Key[3];
                int x = 28;
                for (int i = 0; i < 3; i++)
                {
                    int which = i, wide = i == 0 ? 190 : 162;
                    tabs[i] = MakeKey(FirstRun.Providers[i], x, 80, wide, 38, Body(18, true), i == Provider ? Cyan : Faint, delegate { Tab(which, true); });
                    tabs[i].AccessibleName = FirstRun.Providers[i] + " free key page";
                    x += wide + 10;
                }
                status = Caption(FirstRun.ProviderNotes[Provider], 28, 130, Wide - 56, 46, Body(18), Faint);
                string[] steps = { "Sign in", FirstRun.MakeSteps[Provider], "Copy the key" };
                for (int i = 0; i < 3; i++)
                {
                    Numbered row = new Numbered();
                    row.Number = i + 1;
                    row.Text = steps[i];
                    row.Font = Body(21, true);
                    row.Small = Body(18, true);
                    row.Bounds = Box(28, 180 + 46 * i, Wide - 56, 32);
                    Controls.Add(row);
                }
                return KeyRow(326, "YOUR KEY", "Paste reads your clipboard once, when you press it.", true);
            }

            // A key box with Paste beside it, and two small lines under it. Returns where the screen's own content ends.
            private int KeyRow(int y, string label, string note, bool pageLink)
            {
                Caption(label, 28, y, 200, 20, Mono(15), Faint);
                keyBox = new Entry();
                keyBox.Bounds = Box(28, y + 24, Wide - 56 - 112, 42);
                keyBox.Box.Font = Body(19);
                keyBox.Box.UseSystemPasswordChar = true;
                keyBox.Box.AccessibleName = "Your key";
                keyBox.Cue = "Paste your key here";
                Controls.Add(keyBox);
                MakeKey("Paste", Wide - 28 - 102, y + 24, 102, 42, Body(18, true), Rose, delegate { Paste(); });
                Caption(note, 28, y + 76, Wide - 56, 20, Mono(14), Faint);
                // A free key screen doesn't talk about paid keys; MY PLAN OR KEY does.
                if (pageLink) Linked("Open the page again", 28, y + 106, Body(18, true), Rose, delegate { capri.FirstRunPage(Provider); });
                else Caption("A paid key goes under MAX INTELLIGENCE by itself.", 28, y + 96, Wide - 56, 20, Mono(14), Faint);
                heard = Caption("", 28, y + (pageLink ? 138 : 126), Wide - 56, 46, Body(18), Hot);
                return y + (pageLink ? 184 : 172);
            }

            // A key they've already pasted stays in the box when they switch tabs.
            internal void Tab(int which, bool open)
            {
                string typed = Typed;
                Provider = which;
                if (open) capri.FirstRunPage(which);
                quiet = true;
                Go(FirstRun.Key);
                quiet = false;
                if (!string.IsNullOrEmpty(typed)) Typed = typed;
            }

            internal void Paste()
            {
                string copied = capri.FirstRunClipboard();
                if (keyBox == null) return;
                string key = Squeezed(copied);
                if (key.Length == 0) { if (Brain != FirstRun.Connecting) Problem("There's no key on your clipboard yet. Copy it from the page, then press Paste."); return; }
                keyBox.Box.Text = key;
                if (Brain != FirstRun.Connecting) Problem(null);
                keyBox.Box.Focus();
                keyBox.Box.SelectionStart = keyBox.Box.Text.Length;
            }

            // A key never has spaces in it, so a line break or space picked up while copying it goes.
            internal static string Squeezed(string text)
            {
                if (text == null) return "";
                System.Text.StringBuilder kept = new System.Text.StringBuilder(Math.Min(text.Length, 4096));
                foreach (char c in text) if (!char.IsWhiteSpace(c) && c != '\u200B' && c != '\uFEFF') kept.Append(c);
                return kept.ToString();
            }

            internal string Typed { get { return keyBox == null ? null : keyBox.Box.Text; } set { if (keyBox != null) keyBox.Box.Text = value; } }

            internal void Connect()
            {
                if (keyBox == null) return;
                string key = Squeezed(keyBox.Box.Text);
                if (key.Length == 0) { Problem("Copy the key from the page, then press Paste."); return; }
                string wrong = capri.FirstRunConnect(key);
                if (wrong != null) { Problem(wrong); return; }
                Brain = FirstRun.Connecting;
                checking = owner.brainNote.Text;
                Problem(null);
                heard.ForeColor = Cyan;
                heard.Text = checking;
            }

            private void Problem(string text)
            {
                problem = text;
                if (heard == null) return;
                heard.ForeColor = Hot;
                heard.Text = text ?? "";
            }

            // A key or plan that didn't connect: what went wrong stays on the screen, and they can try again.
            internal void KeyProblem(string text)
            {
                if (Brain != FirstRun.Connecting) return;
                Brain = FirstRun.NoBrain;
                checking = null;
                Problem(text);
            }

            internal string Told { get { return heard == null ? null : heard.Text; } }

            private int BuildPlan()
            {
                Caption("A plan you already pay for, a key of your own, or Ollama.", 28, 74, Wide - 56, 26, Body(19), Faint);
                Caption("YOUR PLAN", 28, 112, 200, 20, Mono(15), Faint);
                MakeKey("Claude plan", 28, 136, 262, 40, Body(18, true), Cyan, delegate { Plan("claudecode"); }).AccessibleName = "Claude plan, runs Claude Code on this PC";
                MakeKey("ChatGPT plan", 310, 136, 262, 40, Body(18, true), Cyan, delegate { Plan("codex"); }).AccessibleName = "ChatGPT plan, runs Codex on this PC";
                Caption("Runs Claude Code or Codex on this PC, signed in.", 28, 184, Wide - 56, 20, Mono(14), Faint);
                Caption("OLLAMA", 28, 222, 200, 20, Mono(15), Faint);
                MakeKey("Open Ollama, I'll find it", 28, 246, 262, 40, Body(18, true), Rose, delegate { Ollama(); });
                return KeyRow(306, "OR A KEY", "OpenAI, Claude, DeepSeek, Google, NVIDIA or OpenRouter.", false);
            }

            internal void Plan(string kind)
            {
                capri.FirstRunPlan(kind);
                Brain = FirstRun.Connecting;
                checking = owner.brainNote.Text;
                Problem(null);
                if (heard != null) { heard.ForeColor = Cyan; heard.Text = checking; }
            }

            internal void Ollama()
            {
                waitingOllama = true;
                capri.FindBrain();
                if (heard != null) { heard.ForeColor = Cyan; heard.Text = "Looking for Ollama. Open it if it isn't running."; }
            }

            private int BuildOnline()
            {
                Ticked tick = new Ticked();
                tick.Bounds = Box(28, 84, 32, 32);
                Controls.Add(tick);
                Caption(capri.FirstRunBrainName, 72, 82, Wide - 100, 30, Body(23, true), White);
                Caption("Saved under " + capri.FirstRunTierName + " in the Mainframe.", 72, 114, Wide - 100, 24, Body(18), Faint);
                Caption("TALK TO ME", 28, 172, 200, 20, Mono(15), Faint);
                Caption("Type", 28, 198, 80, 28, Body(21, true), Cyan);
                Caption(capri.TypeKeyName + ", or the pencil under me", 110, 200, Wide - 138, 26, Body(20), White);
                Caption("Talk", 28, 234, 80, 28, Body(21, true), Cyan);
                Caption(capri.TalkKeyName + ", or the wave under me", 110, 236, Wide - 138, 26, Body(20), White);
                MakeKey("Type to me", 28, 282, 150, 36, Body(18, true), Cyan, delegate { capri.FirstRunType(); });
                return 318;
            }

            private int BuildVoice()
            {
                bool silent = capri.Silent;
                voiceFree = Option(80, 62, "MY FREE VOICE", "Runs on your PC. No key, no credits.", capri.FreeVoiceAside, null, !silent, delegate { Voice(true); });
                voiceText = Option(146, 62, "TEXT ONLY", "I type my replies and stay quiet.", null, null, silent, delegate { Voice(false); });
                Caption("Fish Audio, ElevenLabs and more are under VOICE in the Mainframe.", 58, 212, Wide - 58 - 18, 24, Body(17), Faint);
                Caption("YOUR MIC", 28, 254, 200, 20, Mono(15), Faint);
                micPick = MakeKey("Mic: " + capri.FirstRunMicName, 28, 278, Wide - 56 - 112, 40, Body(18, true), Rose, delegate { owner.FirstRunMics(micPick); });
                micPick.Chevron = true;
                micPick.AccessibleName = "Your mic";
                micTest = MakeKey("Test", Wide - 28 - 102, 278, 102, 40, Body(18, true), Rose, delegate { TestMic(); });
                micTest.AccessibleName = "Test your mic";
                bars = new Bars();
                bars.Bounds = Box(28, 330, 340, 30);
                Controls.Add(bars);
                heard = Caption("", 380, 332, Wide - 28 - 380, 28, Body(19, true), Cyan);
                heard.AutoEllipsis = true;
                Caption("Nothing? Windows Settings > Privacy & security > Microphone.", 28, 372, Wide - 56, 20, Mono(14), Faint);
                return 392;
            }

            internal void Voice(bool free)
            {
                capri.FirstRunVoice(free);
                if (voiceFree != null) { voiceFree.Picked = free; voiceFree.Invalidate(); }
                if (voiceText != null) { voiceText.Picked = !free; voiceText.Invalidate(); }
            }

            internal void TestMic()
            {
                capri.TestMic();
                if (heard != null) heard.Text = "";
            }

            private int BuildExtras()
            {
                Caption("These start off. Each costs a little or uses your mic.", 28, 74, Wide - 56, 26, Body(19), Faint);
                string[] names = { "Watch my screen", "Comment on my games", "Clip my games", "Listen for Capri" };
                string[] notes = { "I glance at your screen and comment. Costs a little on a paid brain.", "I talk about your game as you play. Costs a little on a paid brain.", "I keep your last two minutes of game. Say \"clip that\" to save it.", "I keep listening. Say my name, then just talk." };
                extras = new Toggle[4];
                for (int i = 0; i < 4; i++)
                {
                    int row = i, y = 116 + 78 * i;
                    Toggle box = new Toggle();
                    box.Text = names[i];
                    box.Font = Body(21, true);
                    box.Switch = 108;
                    box.Bounds = Box(28, y, Wide - 56, 28);
                    box.Checked = capri.FirstRunExtraOn(i);
                    box.AccessibleName = names[i] + ". " + notes[i];
                    box.CheckedChanged += delegate { if (box.Checked != capri.FirstRunExtraOn(row)) capri.FirstRunExtra(row); };
                    Controls.Add(box);
                    extras[i] = box;
                    Caption(notes[i], 28, y + 32, Wide - 56, 24, Body(17), Faint);
                }
                Caption("Right-click me any time for the Mainframe, Help and the Discord.", 28, 438, Wide - 56, 26, Body(19), White);
                return 466;
            }

            internal string ExtrasShown
            {
                get
                {
                    if (extras == null) return null;
                    System.Text.StringBuilder bits = new System.Text.StringBuilder();
                    foreach (Toggle box in extras) bits.Append(box.Checked ? '1' : '0');
                    return bits.ToString();
                }
            }

            internal void FlipExtra(int row) { extras[row].Checked = !extras[row].Checked; }

            // NO AI's last screen: what she still does, and where the Mainframe is.
            private int BuildAlone()
            {
                Caption("I won't talk back, and nothing is sent anywhere. I'll still hang out, dance to your music and do the quick things you ask, like the volume.", 28, 74, Wide - 56, 76, Body(19), Faint);
                Caption("Right-click me any time for Capri Mainframe. A brain switches on under BRAIN.", 28, 160, Wide - 56, 50, Body(19), White);
                return 210;
            }

            private int BuildWait()
            {
                Caption("My brain is coming down. I'll say hi the second it's in, and you can talk to me then.", 28, 74, Wide - 56, 50, Body(19), Faint);
                Caption("You can hide this. It keeps going, and the Mainframe shows it too.", 28, 130, Wide - 56, 24, Body(18), Faint);
                return 154;
            }

            // Her brain's download, or her free voice's on the first screen, along the bottom whenever one is coming down, with
            // the free key line under her brain's.
            private int Strip(int y)
            {
                bool brain = Brain == FirstRun.Coming || Brain == FirstRun.Stopped || pendingLocal;
                bool voice = Screen == FirstRun.Pick && capri.SettingUpVoice;
                if (!brain && !voice) return y;
                gauge = new Gauge();
                gauge.Font = Mono(15);
                gauge.Bounds = Box(28, y + 14, Wide - 56, 48);
                gauge.Label = brain ? "MY BRAIN" : "MY FREE VOICE";
                Controls.Add(gauge);
                Gauged();
                y += 62;
                if (Brain == FirstRun.Stopped)
                {
                    Linked("Try again", 28, y + 4, Body(17, true), Rose, delegate { StartLocal(); Go(Screen); });
                    y += 30;
                }
                if (brain && Screen != FirstRun.Key && Screen != FirstRun.Plan)
                {
                    faster = Linked(FirstRun.Faster, 28, y + 4, Body(17, true), Cyan, delegate { Faster(); });
                    y += 30;
                }
                return y;
            }

            private void Gauged()
            {
                if (gauge == null) return;
                if (gauge.Label == "MY FREE VOICE")
                {
                    gauge.Amount = capri.FreeVoiceAmount + ", downloading while you pick";
                    gauge.Share = capri.FreeVoiceShare;
                    gauge.Problem = false;
                }
                else if (Brain == FirstRun.Stopped)
                {
                    gauge.Amount = owner.brainNote.Text;
                    gauge.Share = owner.filled;
                    gauge.Problem = true;
                }
                else if (pendingLocal)
                {
                    gauge.Amount = "Looking at this PC...";
                    gauge.Share = 0;
                    gauge.Problem = false;
                }
                else
                {
                    string note = owner.brainNote.Text;
                    int dot = note.IndexOf(" · ", StringComparison.Ordinal);
                    gauge.Amount = dot >= 0 ? note.Substring(dot + 3) : note;
                    gauge.Share = owner.filled;
                    gauge.Problem = false;
                }
                gauge.Invalidate();
            }

            internal string GaugeShown { get { return gauge == null ? null : gauge.Label + " | " + gauge.Amount; } }

            internal bool FasterShown { get { return faster != null; } }

            // From the free key line: her own brain's download pauses (Set up her brain picks it up later) and the free key
            // screen opens, coming back to where they were once it's connected.
            internal void Faster()
            {
                int was = Screen;
                if (Brain == FirstRun.Coming || Brain == FirstRun.Stopped) capri.CancelSetup();
                pendingLocal = false;
                Brain = FirstRun.NoBrain;
                Picked = FirstRun.Free;
                returnTo = was == FirstRun.Wait ? FirstRun.None : was;
                Provider = 0;
                capri.FirstRunPage(0);
                Go(FirstRun.Key);
            }

            private int BottomRow(int y)
            {
                int top = y + 24;
                string next = Screen == FirstRun.Key || Screen == FirstRun.Plan ? "Connect" : Screen == FirstRun.Wait ? "Hide this" : Screen == FirstRun.Extras || Screen == FirstRun.Alone || Screen == FirstRun.Online && returnTo == FirstRun.None ? "Done" : "Next";
                primary = MakeKey(next, Wide - 28 - 160, top, 160, 40, Body(20, true), Cyan, delegate { Next(); });
                primary.Picked = true;
                AcceptButton = primary;
                ActiveControl = primary;
                int x = 28;
                Font small = Body(18, true);
                if (Screen == FirstRun.Key || Screen == FirstRun.Plan || Screen == FirstRun.Alone)
                {
                    Link back = Linked("Back", x, top + 8, small, Faint, delegate { Back(); });
                    x = (int)Math.Round(back.Right / Unit) + 22;
                }
                if (Screen != FirstRun.Wait)
                {
                    Link skip = Linked(Screen == FirstRun.Pick ? "Skip for now" : "Skip", x, top + 8, small, Faint, delegate { Skip(); });
                    skip.AccessibleName = "Skip this step";
                    x = (int)Math.Round(skip.Right / Unit) + 22;
                }
                Link all = Linked("Skip all", x, top + 8, small, Faint, delegate { SkipAll(); });
                all.AccessibleName = "Skip all of setup";
                return top + 40 + 22;
            }

            internal void Next()
            {
                if (Screen == FirstRun.Pick)
                {
                    BrainSkipped = false;
                    if (Picked == FirstRun.Local) { StartLocal(); Go(FirstRun.Voice); }
                    else if (Picked == FirstRun.Free) { Provider = 0; capri.FirstRunPage(0); Go(FirstRun.Key); }
                    else if (Picked == FirstRun.NoAi) { capri.FirstRunNoAi(); Go(FirstRun.Alone); }
                    else Go(FirstRun.Plan);
                }
                else if (Screen == FirstRun.Key || Screen == FirstRun.Plan) Connect();
                else if (Screen == FirstRun.Online) { if (returnTo == FirstRun.None) Finish(false); else Go(returnTo); }
                else if (Screen == FirstRun.Voice) Go(FirstRun.Extras);
                else if (Screen == FirstRun.Extras) { if (Brain == FirstRun.Coming || pendingLocal) Go(FirstRun.Wait); else Finish(false); }
                else Finish(false);
            }

            internal void Skip()
            {
                if (Screen == FirstRun.Pick || Screen == FirstRun.Key || Screen == FirstRun.Plan)
                {
                    if (Brain != FirstRun.Coming && Brain != FirstRun.Ready) { BrainSkipped = true; Brain = FirstRun.NoBrain; waitingOllama = false; }
                    Go(FirstRun.Voice);
                }
                else Next();
            }

            internal void Back()
            {
                if (Screen == FirstRun.Alone) capri.SetNoAi(false);
                Brain = Brain == FirstRun.Connecting ? FirstRun.NoBrain : Brain;
                Go(FirstRun.Pick);
            }

            internal void SkipAll() { Finish(true); }

            private void StartLocal()
            {
                Brain = FirstRun.Coming;
                pendingLocal = !capri.FirstRunLocal();
                // A brain already on this PC, like Ollama's, counts as ready straight away.
                if (!pendingLocal && capri.BrainModel != null && !capri.SettingUp) capri.FirstRunGreet();
            }

            internal void Resume(int screen)
            {
                if (Picked == FirstRun.Local && capri.BrainModel == null && (screen == FirstRun.Voice || screen == FirstRun.Extras || screen == FirstRun.Wait)) StartLocal();
                else if (capri.BrainModel != null) Brain = FirstRun.Ready;
                ExtrasSeen = screen == FirstRun.Wait;
            }

            // Her new brain has said hi: the BRAIN ONLINE screen, then back to where they were.
            internal void BrainOnline()
            {
                if (Finished || Brain == FirstRun.Ready && Screen == FirstRun.Online) return;
                if (Screen == FirstRun.Wait) returnTo = FirstRun.None;
                else if (Screen == FirstRun.Voice || Screen == FirstRun.Extras) returnTo = Screen;
                Brain = FirstRun.Ready;
                pendingLocal = waitingOllama = false;
                checking = null;
                Go(FirstRun.Online);
            }

            private void Finish(bool all)
            {
                if (Finished) return;
                Finished = true;
                pulse.Stop();
                if (owner.MicTesting) capri.TestMic();
                capri.FirstRunDone(Brain == FirstRun.Coming || pendingLocal);
                Close();
            }

            private void Pulse()
            {
                if (Finished) return;
                if (!moved && capri.Bounds != placedBy) Place();
                if (pendingLocal)
                {
                    pendingLocal = !capri.FirstRunLocal();
                    if (!pendingLocal && capri.BrainModel != null && !capri.SettingUp) capri.FirstRunGreet();
                }
                else if (Brain == FirstRun.Coming && !capri.SettingUp && capri.BrainModel == null)
                {
                    Brain = FirstRun.Stopped;
                    if (gauge != null) { gauge.Label = "MY BRAIN"; }
                }
                else if (Brain == FirstRun.Stopped && capri.SettingUp) Brain = FirstRun.Coming;
                // Her download stopping, or starting again, changes what's along the bottom.
                if (Brain != shownBrain && (Brain == FirstRun.Stopped || shownBrain == FirstRun.Stopped) && Screen != FirstRun.Online) { quiet = true; Go(Screen); quiet = false; }
                if (Screen == FirstRun.Pick && Brain == FirstRun.NoBrain && capri.BrainModel != null && !capri.SettingUp) { Brain = FirstRun.Coming; capri.FirstRunGreet(); }
                if (waitingOllama && capri.BrainModel != null) { waitingOllama = false; capri.FirstRunGreet(); }
                Gauged();
                if (voiceFree != null && voiceFree.Aside != capri.FreeVoiceAside) { voiceFree.Aside = capri.FreeVoiceAside; voiceFree.Invalidate(); }
                if (micPick != null)
                {
                    string mic = "Mic: " + capri.FirstRunMicName;
                    if (micPick.Text != mic) micPick.Text = mic;
                }
                if (bars != null)
                {
                    bool testing = owner.MicTesting;
                    if (testing) { bars.Push((float)owner.micTest.Meter); micTest.Text = "Stop"; }
                    else if (micTest.Text != "Test") micTest.Text = "Test";
                    if (wasTesting && !testing)
                    {
                        string said = owner.VoiceNoteText;
                        heard.ForeColor = said == FirstRun.Heard ? Cyan : Hot;
                        heard.Text = said == FirstRun.Heard ? "I can hear you." : said;
                    }
                    wasTesting = testing;
                }
            }

            // Anything that overlaps, pokes out of the card or doesn't fit its own text, or null.
            internal string Crowded()
            {
                Rectangle area = new Rectangle(0, 0, ClientSize.Width, ClientSize.Height);
                for (int i = 0; i < Controls.Count; i++)
                {
                    Control a = Controls[i];
                    if (!a.Visible) continue;
                    string name = a.Text.Length > 0 ? a.Text : a.AccessibleName;
                    if (!area.Contains(a.Bounds) || a.Right > area.Right - Z(16)) return "pokes out: " + name + " " + a.Bounds + " in " + ClientSize;
                    Label label = a as Label;
                    if (label != null && !(a is Head))
                    {
                        Size need = TextRenderer.MeasureText(label.Text, label.Font, new Size(label.Width, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                        if (need.Height > label.Height + 1 || label.Text.IndexOf(' ') < 0 && need.Width > label.Width) return "doesn't fit: " + label.Text + " needs " + need + " in " + label.Size;
                    }
                    Key key = a as Key;
                    if (key != null && TextRenderer.MeasureText(key.Text, key.Font).Width + (key.Chevron ? Z(40) : Z(8)) > key.Width) return "doesn't fit: " + key.Text;
                    Pickable pick = a as Pickable;
                    if (pick != null && pick.Overflows) return "doesn't fit: " + pick.Title;
                    Toggle box = a as Toggle;
                    if (box != null && TextRenderer.MeasureText(box.Text, box.Font).Width > box.NameRoom) return "doesn't fit: " + box.Text;
                    for (int j = i + 1; j < Controls.Count; j++)
                        if (Controls[j].Visible && a.Bounds.IntersectsWith(Controls[j].Bounds)) return "overlap: " + name + " / " + (Controls[j].Text.Length > 0 ? Controls[j].Text : Controls[j].AccessibleName);
                }
                return null;
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Clip(this, Unit);
                Invalidate(true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics graphics = e.Graphics;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Backdrop(graphics, this, Unit);
                using (Pen rule = new Pen(Edge, Math.Max(1, Unit))) graphics.DrawLine(rule, Z(28), Z(62), ClientSize.Width - Z(28), Z(62));
                if (Screen == FirstRun.Online)
                    using (Pen rule = new Pen(Edge, Math.Max(1, Unit))) graphics.DrawLine(rule, Z(28), Z(156), ClientSize.Width - Z(28), Z(156));
                if (Screen == FirstRun.Voice)
                    using (Pen rule = new Pen(Edge, Math.Max(1, Unit))) graphics.DrawLine(rule, Z(28), Z(242), ClientSize.Width - Z(28), Z(242));
                if (Screen == FirstRun.Extras)
                    using (Pen rule = new Pen(Edge, Math.Max(1, Unit))) graphics.DrawLine(rule, Z(28), Z(428), ClientSize.Width - Z(28), Z(428));
                Neon.Frame(graphics, ClientSize, Unit, 16 * Unit, 6 * Unit, 74 * Unit, 54 * Unit);
            }

            // Closing it with Alt + F4 skips the rest. Her quitting closes it too, and that keeps the step for next time.
            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                if (!Finished && e.CloseReason == CloseReason.UserClosing) Finish(true);
                base.OnFormClosing(e);
            }

            protected override void OnFormClosed(FormClosedEventArgs e)
            {
                pulse.Stop();
                capri.FirstRunGone(this);
                base.OnFormClosed(e);
            }

            protected override void Dispose(bool disposing)
            {
                base.Dispose(disposing);
                if (disposing) { pulse.Dispose(); foreach (Font font in fonts) font.Dispose(); fonts.Clear(); }
            }

            // Dragged by any spot that isn't a control, like the Mainframe.
            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0231) moved = true;
                base.WndProc(ref message);
                if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
            }
        }

        // One of the card's big choices: a diamond, its name in white (cyan once picked), an amber flag after it, a mono note on
        // the right and a line or two under it.
        private sealed class Pickable : Control
        {
            internal string Title, Flag, Aside, Body;
            internal Font TitleFont, FlagFont, AsideFont;
            internal bool Picked;
            private bool hot;

            internal Pickable()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.Selectable, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
                TabStop = true;
                AccessibleRole = AccessibleRole.RadioButton;
            }

            internal bool Overflows
            {
                get
                {
                    float s = ScaleOf(this);
                    int room = Width - (int)(30 * s);
                    Size need = TextRenderer.MeasureText(Body ?? "", Font, new Size(room, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                    int title = TextRenderer.MeasureText(Title, TitleFont).Width + (Flag == null ? 0 : TextRenderer.MeasureText(Flag, FlagFont).Width + (int)(14 * s)) + (Aside == null ? 0 : TextRenderer.MeasureText(Aside, AsideFont).Width + (int)(30 * s));
                    return need.Height > Height - (int)(32 * s) || title > room;
                }
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }

            protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

            protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Space) { e.Handled = true; OnClick(EventArgs.Empty); return; }
                base.OnKeyDown(e);
            }

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
                        using (Brush wash = new SolidBrush(Color.FromArgb(18, Cyan))) graphics.FillPath(wash, plate);
                        using (Pen pen = new Pen(Color.FromArgb(150, Cyan), line)) graphics.DrawPath(pen, plate);
                    }
                }
                float x = 11 * s, y = 15 * s, size = 7 * s;
                PointF[] diamond = { new PointF(x, y - size), new PointF(x + size, y), new PointF(x, y + size), new PointF(x - size, y) };
                if (Picked) using (Brush mark = new SolidBrush(Cyan)) graphics.FillPolygon(mark, diamond);
                else using (Pen pen = new Pen(lit ? Cyan : Faint, 1.2f * s)) graphics.DrawPolygon(pen, diamond);
                TextFormatFlags one = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int left = (int)(30 * s), top = (int)(2 * s);
                Size name = TextRenderer.MeasureText(graphics, Title, TitleFont, Size.Empty, one);
                TextRenderer.DrawText(graphics, Title, TitleFont, new Point(left, top), Picked ? Cyan : White, one);
                // The flag and the note sit on the name's baseline.
                int baseline = top + (int)(TitleFont.FontFamily.GetCellAscent(TitleFont.Style) * TitleFont.Size / TitleFont.FontFamily.GetEmHeight(TitleFont.Style));
                if (Flag != null)
                {
                    int flagTop = baseline - (int)(FlagFont.FontFamily.GetCellAscent(FlagFont.Style) * FlagFont.Size / FlagFont.FontFamily.GetEmHeight(FlagFont.Style));
                    TextRenderer.DrawText(graphics, Flag, FlagFont, new Point(left + name.Width + (int)(14 * s), flagTop), Neon.Amber, one);
                }
                if (Aside != null)
                {
                    int asideTop = baseline - (int)(AsideFont.FontFamily.GetCellAscent(AsideFont.Style) * AsideFont.Size / AsideFont.FontFamily.GetEmHeight(AsideFont.Style));
                    TextRenderer.DrawText(graphics, Aside, AsideFont, new Rectangle(0, asideTop, Width - (int)(2 * s), Height), Picked ? Cyan : Faint, TextFormatFlags.Right | TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
                }
                TextRenderer.DrawText(graphics, Body ?? "", Font, new Rectangle(left, (int)(32 * s), Width - left, Height - (int)(32 * s)), Faint, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }
        }

        // A numbered step on the free key screen: a cyan ring with its number, and the step beside it.
        private sealed class Numbered : Control
        {
            internal int Number;
            internal Font Small;

            internal Numbered() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF ring = new RectangleF(2 * s, Height / 2f - 14 * s, 28 * s, 28 * s);
                using (Pen pen = new Pen(Cyan, 2 * s)) graphics.DrawEllipse(pen, ring);
                TextRenderer.DrawText(graphics, Number.ToString(), Small ?? Font, Rectangle.Round(ring), Cyan, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle((int)(46 * s), 0, Width - (int)(46 * s), Height), White, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        // BRAIN ONLINE's tick: a dark teal ring with a cyan tick in it.
        private sealed class Ticked : Control
        {
            internal Ticked() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                RectangleF ring = new RectangleF(2 * s, 2 * s, Width - 4 * s, Height - 4 * s);
                using (Brush fill = new SolidBrush(Deep)) graphics.FillEllipse(fill, ring);
                using (Pen pen = new Pen(DeepEdge, 1.2f * s)) graphics.DrawEllipse(pen, ring);
                float x = Width / 2f, y = Height / 2f;
                using (Pen tick = new Pen(Cyan, 3 * s))
                {
                    tick.LineJoin = LineJoin.Miter;
                    graphics.DrawLines(tick, new[] { new PointF(x - 7 * s, y), new PointF(x - 2 * s, y + 6 * s), new PointF(x + 8 * s, y - 6 * s) });
                }
            }
        }

        // A progress row: a rule above it, its name and how far on in her mono, and the brain setup's dotted bar under them.
        private sealed class Gauge : Control
        {
            internal string Label = "", Amount = "";
            internal float Share = -1;
            internal bool Problem;

            internal Gauge() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                using (Pen rule = new Pen(Edge, Math.Max(1, s))) graphics.DrawLine(rule, 0, 0, Width, 0);
                TextFormatFlags one = TextFormatFlags.Top | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                int top = (int)(14 * s);
                int named = TextRenderer.MeasureText(graphics, Label, Font, Size.Empty, one).Width;
                TextRenderer.DrawText(graphics, Label, Font, new Point(0, top), Faint, one);
                int from = named + (int)(16 * s);
                TextRenderer.DrawText(graphics, Amount ?? "", Font, new Rectangle(from, top, Width - from, Height), Problem ? Hot : Faint, one | TextFormatFlags.Right | TextFormatFlags.EndEllipsis);
                if (Share < 0) return;
                float pitch = 6 * s, y = 36 * s;
                int count = (int)(Width / pitch);
                int lit = (int)Math.Round(count * Math.Max(0, Math.Min(1, Share)));
                using (Brush on = new SolidBrush(Problem ? Hot : Cyan))
                using (Brush off = new SolidBrush(Dash))
                    for (int i = 0; i < count; i++) graphics.FillRectangle(i < lit ? on : off, i * pitch, y, pitch - 2 * s, 6 * s);
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        // The mic test's level as a row of bars, newest on the right, while the test runs.
        private sealed class Bars : Control
        {
            private readonly float[] levels = new float[24];

            internal Bars() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true); }

            // The Mainframe's meter runs 0 to 1 over -96 to 0 dB; speech sits in its top half.
            internal void Push(float meter)
            {
                Array.Copy(levels, 1, levels, 0, levels.Length - 1);
                levels[levels.Length - 1] = Math.Max(0, Math.Min(1, (meter - 0.45f) / 0.45f));
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                float bottom = Height - 2 * s, tallest = Height - 4 * s;
                using (Brush on = new SolidBrush(Cyan))
                using (Brush off = new SolidBrush(Neon.Tone(40, 60, 66)))
                    for (int i = 0; i < levels.Length; i++)
                    {
                        float tall = Math.Max(2 * s, levels[i] * tallest);
                        graphics.FillRectangle(levels[i] > 0.02f ? on : off, i * 14 * s, bottom - tall, 8 * s, tall);
                    }
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }

        // Words that act like a button, like Skip and Back: faint semi-bold, white under the mouse, underlined with keyboard focus.
        private sealed class Link : Button, Tinted
        {
            internal Color Ink = Faint;
            private bool hot;

            public void Recolour(Func<Color, Color> swap) { Ink = swap(Ink); }

            internal Link()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Ground;
                Cursor = Cursors.Hand;
                UseMnemonic = false;
            }

            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                float s = ScaleOf(this);
                Graphics graphics = e.Graphics;
                Backdrop(graphics, this, s);
                Color ink = hot ? (Ink == Faint ? White : ControlPaint.Light(Ink, 0.3f)) : Ink;
                TextFormatFlags one = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
                TextRenderer.DrawText(graphics, Text, Font, new Rectangle((int)(2 * s), 0, Width, Height), ink, one);
                if (Focused && ShowFocusCues)
                {
                    int wide = TextRenderer.MeasureText(graphics, Text, Font, Size.Empty, one).Width;
                    using (Pen line = new Pen(Cyan, Math.Max(1, s))) graphics.DrawLine(line, 2 * s, Height - 2 * s, 2 * s + wide, Height - 2 * s);
                }
            }
        }
    }

    internal partial class Companion
    {
        private Mainframe.FirstRunCard firstRun;
        // No settings file when she started: a brand new install, the only kind that gets the card. An update always has one.
        private bool freshInstall;
        // Stand-ins for the smoke test: her brain's download, the clipboard and a plan.
        internal Action FirstRunSetUp;
        internal Func<string> FirstRunPaste = delegate
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText() : null; }
            catch (System.Runtime.InteropServices.ExternalException) { return null; }
        };
        internal Action<string> FirstRunUsePlan;
        internal Func<bool> FirstRunLanding;

        internal bool FirstRunShowing { get { return firstRun != null && !firstRun.IsDisposed && !firstRun.Finished; } }

        // A new install, or one that quit with the card still open. An update never gets it: it has a settings file, and an
        // install that's finished with the card or skipped it has its step at 0.
        private bool WantsFirstRun()
        {
            return WantsFirstRun(preferences, freshInstall, Program.StartedFresh);
        }

        internal static bool WantsFirstRun(Preferences saved, bool fresh, bool startedFresh)
        {
            return saved.Welcomed ? saved.FirstRunStep > 0 : fresh && !startedFresh;
        }

        // At her first launch: she wanders now and then (the new install default since 31 Oct), the card's first step is kept
        // at once so quitting before it shows still brings it back, and it opens once she's arrived, where her hello would be.
        private void StartFirstRun()
        {
            bool resumed = preferences.Welcomed;
            if (!resumed)
            {
                preferences.Welcomed = true;
                preferences.FirstRunStep = FirstRun.Pick;
                preferences.FirstRunPick = FirstRun.Local;
                SetWanderLevel(1);
                nextWander = -1;
                SavePreferences();
            }
            int step = preferences.FirstRunStep, pick = preferences.FirstRunPick;
            ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(GreetPause); Post(delegate { OpenWhenLanded(step, pick, resumed, 0); }); });
        }

        // The card goes beside where she ends up, so it waits for her arrival and her drop onto the taskbar, a few seconds at most.
        private void OpenWhenLanded(int step, int pick, bool resumed, int waited)
        {
            if (quitting || IsDisposed) return;
            bool landing = FirstRunLanding != null ? FirstRunLanding() : falling || spawnFall || dropAfter || clip == arrive;
            if (waited < 6000 && landing)
            {
                ThreadPool.QueueUserWorkItem(delegate { Thread.Sleep(150); Post(delegate { OpenWhenLanded(step, pick, resumed, waited + 150); }); });
                return;
            }
            OpenFirstRun(step, pick, resumed);
        }

        internal Mainframe.FirstRunCard OpenFirstRun(int step, int pick, bool resumed, float scale = 0)
        {
            Said.Know(FirstRun.Lines);
            if (FirstRunShowing) firstRun.Dispose();
            // Picking up after a quit: her brain-online screen has had its turn, and the free key page isn't opened again.
            int screen = step == FirstRun.Online ? FirstRun.Voice : step < FirstRun.Pick || step > FirstRun.Alone ? FirstRun.Pick : step;
            if (pick < FirstRun.Free || pick > FirstRun.NoAi) pick = FirstRun.Local;
            if (screen == FirstRun.Wait && pick == FirstRun.Local && (engine.Installed() != null || brain.Model != null)) { FirstRunDone(false); return null; }
            firstRun = mainframe.NewFirstRun(scale);
            firstRun.Picked = pick;
            // On a screen too narrow for the card beside her and her bubble, she steps aside just enough first.
            if (!testing)
            {
                Rectangle area = Screen.FromRectangle(Bounds).WorkingArea;
                int bubbleLeft, bubbleRight;
                BubbleSpan(area, out bubbleLeft, out bubbleRight);
                int aside = Mainframe.FirstRunCard.Nudge(Bounds, bubbleLeft, bubbleRight, area, firstRun.Width, firstRun.Unit);
                if (aside != 0) { anchor = new Point(anchor.X + aside, anchor.Y); Location = anchor; }
            }
            if (resumed) firstRun.Resume(screen);
            firstRun.Begin(screen, pick, resumed);
            firstRun.Show();
            return firstRun;
        }

        internal void FirstRunAt(int screen, int pick)
        {
            if (firstRun == null) return;
            preferences.FirstRunStep = screen;
            preferences.FirstRunPick = pick;
            SavePreferences();
        }

        // Her line for the card's screen, in their language once it's translated. It waits for nothing: a new screen's line
        // replaces the last one. While they're typing to her or in a voice chat she keeps quiet.
        internal void FirstRunSay(string line)
        {
            if (typer.Visible || chatting || quitting) return;
            if (hidden) SetHidden(false);
            heard = "first run";
            box.Prompt = "";
            remarkTicket = ++asking;
            Answer(remarkTicket, line, TalkState.Talking);
        }

        // Her new brain said hi (Greet): the card shows BRAIN ONLINE.
        private void FirstRunBrainOnline()
        {
            if (FirstRunShowing) firstRun.BrainOnline();
        }

        internal void FirstRunGreet() { Greet(); }

        // Starts her own brain's download, or false while she hasn't finished looking at this PC.
        internal bool FirstRunLocal()
        {
            if (SettingUp) return true;
            if (FirstRunSetUp == null && (machine == null || fit == null)) return false;
            if (brain.Model != null) return true;
            if (FirstRunSetUp != null) FirstRunSetUp();
            else SetUpBrain();
            return true;
        }

        // The key goes where its kind belongs: a free one under MEDIUM, a paid one under MAX, with no Move to MAX step.
        internal string FirstRunConnect(string key)
        {
            string kind = Cloud.KindOf(key);
            if (kind == null) return key.StartsWith("sk-nous-", StringComparison.Ordinal) ? "Nous Portal keys aren't supported yet." : Brain.NotAKey;
            ConnectText(key, Cloud.MediumKind(kind) ? 1 : 2);
            return null;
        }

        internal void FirstRunPlan(string kind)
        {
            if (FirstRunUsePlan != null) FirstRunUsePlan(kind);
            else UsePlan(kind);
        }

        internal void FirstRunPage(int provider) { OpenPage(provider == 1 ? FreeKeyPage : provider == 2 ? RouterKeyPage : GeminiKeyPage); }

        internal string FirstRunClipboard() { return FirstRunPaste(); }

        internal void FirstRunVoice(bool free)
        {
            if (free) Unsilence();
            else Silence(false);
        }

        internal bool FirstRunExtraOn(int row) { return row == 0 ? Watching : row == 1 ? CommentPace > 0 : row == 2 ? ClipsOn : ListenCapriOn; }

        internal void FirstRunExtra(int row)
        {
            if (row == 0) ToggleWatch();
            else if (row == 1) { SetCommentPace(CommentPace > 0 ? 0 : 2); mainframe.Sync(); }
            else if (row == 2) ToggleClips();
            else ToggleListenCapri();
        }

        internal void FirstRunType() { if (!typer.Visible) OpenTyper(); }

        // The card's done, finished or skipped (Lachy, 12 Nov: Skip all ends the whole thing). With a brain, or hers on its way,
        // or with No AI picked, she just stands there ready; with none, the Mainframe opens on Set up her brain so they can see
        // where to get one.
        // EXTRAS never pops up after it. Her news and holiday lines wait for her hello, which the card stood in for.
        internal void FirstRunDone(bool brainComing)
        {
            firstRun = null;
            preferences.FirstRunStep = FirstRun.None;
            SavePreferences();
            greetTried = true;
            if (!brainComing && brain.Model == null && !preferences.NoAi) mainframe.Restore();
        }

        // NO AI on the card: No AI goes on, and her voice and ears, which only a brain's replies and their talking need, stop coming
        // down. Picking a voice under VOICE still gets hers.
        internal void FirstRunNoAi()
        {
            SetNoAi(true);
            if (SettingUpVoice) CancelFreeVoice();
            if (settingUpEars) parakeet.Cancel = true;
        }

        private void FirstRunKeyProblem(string problem) { if (FirstRunShowing) firstRun.KeyProblem(problem); }

        internal void FirstRunGone(Mainframe.FirstRunCard card) { if (firstRun == card) firstRun = null; }

        // Where her bubble reaches across, left to right, for a line as short as the card's: it hangs off her head the way
        // SpeechBox.Render puts it.
        internal void BubbleSpan(Rectangle area, out int left, out int right) { BubbleSpan(Head(), TalkScale(), area, out left, out right); }

        internal static void BubbleSpan(Point head, float s, Rectangle area, out int left, out int right)
        {
            s = Math.Min(s, Math.Min(area.Width / 160f, area.Height / 150f));
            float panel = SpeechBox.PanelWidth(s), margin = 12 * s;
            int width = (int)Math.Ceiling(panel + margin * 2);
            left = Math.Max(area.Left, Math.Min(area.Right - width, (int)(head.X - margin - panel * 0.7f)));
            right = left + width;
        }

        internal string FitName { get { return fit == null ? null : fit.Name; } }
        internal string FitSize { get { return fit == null ? null : fit.Download; } }

        // At 50 Mbps, which is what the site's times assume.
        internal string FitMinutes { get { return fit == null ? "about 8 min" : "about " + Math.Max(1, (int)Math.Round(fit.Size / 6.25e6 / 60)) + " min"; } }

        internal string BrainModel { get { return brain.Model; } }
        internal bool Met { get { return preferences.Met; } }
        internal bool FreeVoiceIn { get { return kokoro.Installed; } }

        internal string FreeVoiceAmount
        {
            get
            {
                long total = voiceTotal > 0 ? voiceTotal : LocalVoice.PackSize;
                return Engine.Amount(SettingUpVoice ? voiceGot : 0) + " of " + Engine.Amount(total);
            }
        }

        internal string FreeVoiceAside { get { return kokoro.Installed ? "ready" : SettingUpVoice ? FreeVoiceAmount : Engine.Amount(voiceTotal > 0 ? voiceTotal : LocalVoice.PackSize) + " download"; } }

        internal float FreeVoiceShare { get { long total = voiceTotal > 0 ? voiceTotal : LocalVoice.PackSize; return total <= 0 ? 0 : Math.Min(1f, voiceGot / (float)total); } }

        internal string FirstRunBrainName
        {
            get
            {
                Cloud remote = brain.Remote;
                if (remote != null && remote.Kind != "local") return remote.Name + " · " + (remote.Plan ? "your plan" : Cloud.MediumKind(remote.Kind) ? "free key" : "your key");
                return (brain.Model ?? "My brain") + " · on this PC";
            }
        }

        internal string FirstRunTierName { get { return preferences.Tier == 1 ? "MEDIUM INTELLIGENCE" : preferences.Tier == 2 ? "MAX INTELLIGENCE" : "NORMAL"; } }

        internal string FirstRunMicName
        {
            get
            {
                string id = preferences.Mic;
                if (string.IsNullOrEmpty(id)) return "Windows default";
                Mic found = Ears.Find(id, HearingMics);
                return found != null ? found.Name : "Windows default";
            }
        }
    }
}
