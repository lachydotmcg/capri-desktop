using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class NeonSwatch : NeonButton
    {
        internal string Colour, Accent2;
        internal bool Chosen;

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            bool lit = IsPointerOver || IsKeyboardFocusWithin;
            Rect box = new Rect(1, 1, w - 2, h - 2);
            context.DrawGeometry(MainframeTheme.Brush(Colour), null, MainframeTheme.Cut(box));
            if (Accent2 != null) context.DrawRectangle(MainframeTheme.Brush(Accent2), null, new Rect(box.X, box.Bottom - h / 3, box.Width, h / 3 - 1));
            context.DrawGeometry(null, new Pen(Chosen ? MainframeTheme.Cyan : lit ? MainframeTheme.Hot : MainframeTheme.Edge, Chosen ? 2.5 : 1), MainframeTheme.Cut(new Rect(0.5, 0.5, w - 1, h - 1)));
            if (MainframeTheme.Marked(this)) MainframeTheme.Ring(context, new Rect(Bounds.Size));
        }

        internal override string TextFits() { return null; }
    }

    internal sealed class FirstBar : Control
    {
        private double fraction = -1;
        internal double Fraction { get { return fraction; } set { if (fraction == value) return; fraction = value; InvalidateVisual(); } }

        public override void Render(DrawingContext context)
        {
            context.DrawRectangle(MainframeTheme.Well, null, new Rect(Bounds.Size));
            if (fraction >= 0) context.DrawRectangle(MainframeTheme.Cyan, null, new Rect(0, 0, Bounds.Width * Math.Min(1, fraction), Bounds.Height));
        }
    }

    internal sealed class FirstRunCard : MainframeWindow
    {
        internal const int Wide = 380, Wake = 1, Yours = 2, Job = 3, Last = 3;
        internal const string Hello = "Hey, choom. I'm Capri. I need a brain before I can talk back. Pick one.";
        private static readonly string[] FreeNames = { "Google AI Studio (Gemini)", "NVIDIA", "OpenRouter" };
        private static readonly string[] FreePages = { "https://aistudio.google.com/apikey", "https://build.nvidia.com/settings/api-keys", "https://openrouter.ai/keys" };
        private readonly Pet pet;
        private readonly DispatcherTimer tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        internal readonly Dictionary<string, Control> Rows = new Dictionary<string, Control>();
        private NeonCaption note, line;
        private FirstBar bar;
        private NeonEntry keyBox, nameBox, jobBox;
        private NeonButton[] vibes, jobs;
        private NeonSwatch[] swatches;
        private NeonCaption vibeNote, lookName;
        private int pick, vibe, look;
        private string name;
        private bool closed;
        internal int Screen { get; private set; }
        internal int Picked { get { return pick; } }
        internal int LookPicked { get { return look; } }
        internal string Said { get { return line == null ? null : line.Text; } }
        internal string NoteText { get { return note == null ? null : note.Text; } }
        internal double BarFraction { get { return bar == null ? -1 : bar.Fraction; } }

        internal FirstRunCard(Pet owner, int screen) : base("Set up")
        {
            pet = owner;
            FitsScreen = true;
            ShowInTaskbar = false;
            Topmost = true;
            vibe = Onboarding.VibeOf(Brain.Wordiness);
            look = Onboarding.LookOf(pet.ModList.ColoursNow);
            if (look < 0) look = 0;
            Opened += delegate { tick.Start(); };
            Closed += delegate { closed = true; tick.Stop(); };
            tick.Tick += delegate { Program.Guard(Refresh); };
            Go(Math.Max(Wake, Math.Min(Last, screen)), false);
        }

        protected override void Back() { }

        private double Zoomed() { return Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0; }

        private T Add<T>(T control, double x, double y, double width, double height, string label) where T : Control
        {
            Put(Page, control, x, y, width, height, label);
            Rows[label] = control;
            return control;
        }

        private NeonCaption Words(string label, string text, double x, double y, double width, double height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false)
        {
            return Add(new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Faint, Size = size, Bold = bold, Top = height > 24 }, x, y, width, height, label);
        }

        private NeonButton Key(string label, string text, double x, double y, double width, double height, Action action, bool loud = false)
        {
            NeonButton key = Add(new NeonButton { Text = text }, x, y, width, height, label);
            if (loud) { key.Accent = MainframeTheme.Cyan; key.Loud = true; }
            key.Click += delegate { if (!closed) action(); };
            return key;
        }

        private NeonEntry Box(string label, string cue, double x, double y, double width, double height, Action enter, bool secret = false)
        {
            NeonEntry entry = Add(new NeonEntry(cue, secret), x, y, width, height, label);
            AutomationProperties.SetName(entry.Box, label);
            entry.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Program.Guard(delegate { if (!closed) enter(); }); } };
            return entry;
        }

        private void Go(int screen, bool say)
        {
            Screen = screen;
            pet.FirstRunAt(screen);
            Page.Children.Clear();
            Rows.Clear();
            note = null; bar = null; keyBox = null; vibes = null; jobs = null; swatches = null; vibeNote = null; lookName = null; line = null;
            NeonCaption title = Add(new NeonCaption { Text = "SET UP", Ink = MainframeTheme.Rose, Size = 64.0 / 3, Bold = true, Display = true }, 24, 15, 160, 29, "Title");
            NeonCaption count = Words("Count", screen + " OF " + Last, 244, 20, 112, 22, MainframeTheme.Cyan, MainframeTheme.Size, true);
            count.Align = TextAlignment.Right;
            string head = screen == Wake ? "WAKE ME UP" : screen == Yours ? "MAKE ME YOURS" : "FIRST JOB";
            Add(new NeonHead { Text = head }, 14, 53, 352, 24, head);
            double bottom = screen == Wake ? BuildWake() : screen == Yours ? BuildYours() : BuildJob();
            Key("Skip all", "Skip all", 28, bottom, 96, 28, SkipAll);
            Key("Skip", "Skip", 180, bottom, 80, 28, Skip);
            if (screen < Job) Key("Next", "Next", 268, bottom, 84, 28, Next, true);
            Fit(Wide, bottom + 56, Zoomed());
            Refresh();
            if (say) Say(screen == Yours ? Onboarding.MeLine : screen == Job ? Brained ? Onboarding.JobLine : Onboarding.JobWaitLine : null);
            int stops = 0;
            foreach (Control control in Page.Children) if (control.Focusable) control.TabIndex = stops++;
        }

        private bool Brained { get { return pet.FirstRunBrainReady; } }

        private void Say(string text)
        {
            if (text != null) pet.FirstRunSays(text);
        }

        private double BuildWake()
        {
            line = Words("Line", Brained ? "My brain's already in. Nothing to download." : Hello, 28, 84, 324, 36, MainframeTheme.White, 12.5);
            List<string> names = new List<string> { "On this computer", "Free key" };
            List<string> hints = new List<string> { "Free, private", "About a minute" };
            if (TerminalJobs.Supported) { names.Add("ChatGPT plan"); hints.Add("Sign in"); }
            double y = 126;
            for (int i = 0; i < names.Count; i++)
            {
                int at = i;
                NeonRung rung = Add(new NeonRung { Text = names[i], Hint = hints[i], Checked = i == pick }, 28, y, 324, 24, names[i]);
                rung.Click += delegate { if (!closed) Choose(at); };
                y += 24;
            }
            y += 10;
            if (pick == 1)
            {
                NeonPicker free = Add(new NeonPicker(), 28, y, 324, 28, "Get a free key");
                free.Check = false;
                free.Options(FreeNames);
                free.Text = "Get a free key";
                free.Changed += delegate(int at) { if (Program.Smoke == null) Pet.Browse(FreePages[at]); free.Text = "Get a free key"; };
                keyBox = Box("Brain API key", "Paste your key", 28, y + 36, 236, 28, Connect, true);
                Key("Connect", "Connect", 272, y + 36, 80, 28, Connect, true);
                y += 72;
            }
            else if (pick == 2)
            {
                Key("Connect ChatGPT", "Connect ChatGPT", 28, y, 324, 28, pet.UsePlan, true);
                y += 36;
            }
            note = Words("Note", "", 28, y, 324, 36, MainframeTheme.Faint, 34.0 / 3);
            bar = Add(new FirstBar(), 28, y + 40, 324, 6, "Progress");
            return y + 62;
        }

        private double BuildYours()
        {
            line = Words("Line", Onboarding.MeLine, 28, 84, 324, 36, MainframeTheme.White, 12.5);
            nameBox = Box("Name", Onboarding.NameCue, 28, 126, 324, 28, Next);
            if (!string.IsNullOrEmpty(Brain.Nickname)) nameBox.Box.Text = Brain.Nickname;
            Words("Vibe label", "HOW MUCH I SAY", 28, 168, 324, 16, MainframeTheme.Faint, 34.0 / 3, true);
            vibes = new NeonButton[Onboarding.Vibes.Length];
            for (int i = 0; i < vibes.Length; i++)
            {
                int at = i;
                vibes[i] = Key(Onboarding.Vibes[i], Onboarding.Vibes[i], 28 + i * 112, 188, 100, 28, delegate { SetVibe(at); });
            }
            vibeNote = Words("Vibe note", "", 28, 222, 324, 16, MainframeTheme.Faint, 34.0 / 3);
            Words("Look label", "HOW I LOOK", 28, 252, 324, 16, MainframeTheme.Faint, 34.0 / 3, true);
            swatches = new NeonSwatch[Onboarding.Looks.Length];
            for (int i = 0; i < swatches.Length; i++)
            {
                int at = i;
                Outfit outfit = Onboarding.Looks[i];
                swatches[i] = Add(new NeonSwatch { Colour = outfit.Swatch, Accent2 = outfit.Colours == null ? null : outfit.Colours[1] }, 28 + i * 67, 272, 56, 40, outfit.Name);
                swatches[i].Click += delegate { if (!closed) SetLook(at); };
            }
            lookName = Words("Look name", "", 28, 320, 160, 16, MainframeTheme.Faint, 34.0 / 3);
            NeonButton more = Key("More looks in Mods", "More looks in Mods", 196, 316, 156, 22, pet.OpenMods);
            more.Accent = MainframeTheme.Cyan;
            more.TextSize = 12;
            return 356;
        }

        private double BuildJob()
        {
            line = Words("Line", Brained ? Onboarding.JobLine : Onboarding.JobWaitLine, 28, 84, 324, 36, MainframeTheme.White, 12.5);
            FirstJob[] picks = Onboarding.Jobs(false, OperatingSystem.IsMacOS(), DateTime.Now.Hour);
            jobs = new NeonButton[picks.Length];
            double y = 126;
            for (int i = 0; i < picks.Length; i++)
            {
                string said = picks[i].Said;
                jobs[i] = Key(picks[i].Label, picks[i].Label, 28, y, 324, 28, delegate { Pick(said); });
                y += 36;
            }
            jobBox = Box("Own job", Onboarding.OwnCue, 28, y, 236, 28, Own);
            Key("Do it", "Do it", 272, y, 80, 28, Own, true);
            return y + 44;
        }

        private void Choose(int at)
        {
            if (pick == at) return;
            pick = at;
            Go(Screen, false);
        }

        private void Connect()
        {
            string key = ((keyBox == null ? "" : keyBox.Box.Text) ?? "").Trim();
            if (key.Length == 0) return;
            keyBox.Box.Text = "";
            string kind = Cloud.KindOf(key);
            int tier = kind != null && Cloud.MediumKind(kind) ? 1 : 2;
            pet.SetBrainTier(tier);
            pet.SaveBrainKey(key, tier);
        }

        private void Next()
        {
            if (Screen == Wake) pet.FirstRunWake(pick);
            else if (Screen == Yours)
            {
                string typed = nameBox == null ? "" : (nameBox.Box.Text ?? "").Trim();
                if (typed.Length > 0 && typed != Brain.Nickname) { pet.SetNickname(typed); name = typed; }
                else if (typed.Length > 0) name = typed;
            }
            if (Screen < Last) Go(Screen + 1, true);
        }

        private void Skip()
        {
            if (Screen < Last) Go(Screen + 1, true);
            else Finish(null);
        }

        private void SkipAll() { Finish(null); }

        private void SetVibe(int at)
        {
            vibe = at;
            pet.SetReplies(at);
            Refresh();
        }

        private void SetLook(int at)
        {
            look = at;
            pet.FirstRunLook(Onboarding.Looks[at].Colours);
            Refresh();
        }

        private void Own()
        {
            string said = Onboarding.Job(jobBox == null ? null : jobBox.Box.Text);
            if (said != null) Pick(said);
        }

        internal void Pick(string said)
        {
            if (closed) return;
            Finish(said);
        }

        private void Finish(string job)
        {
            if (closed) return;
            closed = true;
            tick.Stop();
            Close();
            pet.FinishFirstRun(name != null || job != null, job);
        }

        internal void Press(string label)
        {
            Control row;
            if (!Rows.TryGetValue(label, out row)) throw new Exception("No set up control: " + label);
            ((NeonButton)row).Press();
        }

        private string BrainNote()
        {
            string problem = pet.MainframeProblem;
            if (pet.MainframePulling) return pet.MainframeProgress;
            if (pet.MainframeConnecting) return "Checking the key...";
            if (Brained) return "My brain's in and ready: " + pet.MainframeBrainName + ".";
            if (problem != null) return problem;
            if (pick == 1) return "Press Get a free key, make one, paste it here and press Connect.";
            if (pick == 2) return "Press Connect ChatGPT and sign in. You can keep going while it connects.";
            return "Next starts my own brain downloading. You can keep going while it does.";
        }

        internal void Refresh()
        {
            if (closed) return;
            if (note != null)
            {
                note.Text = BrainNote();
                bar.Fraction = pet.MainframePulling && pet.FirstRunPulled >= 0 ? pet.FirstRunPulled / 100.0 : -1;
                bar.IsVisible = pet.MainframePulling;
            }
            if (vibes != null)
            {
                for (int i = 0; i < vibes.Length; i++) { vibes[i].Picked = i == vibe; vibes[i].InvalidateVisual(); }
                vibeNote.Text = Onboarding.VibeNotes[vibe];
                for (int i = 0; i < swatches.Length; i++) { swatches[i].Chosen = i == look; swatches[i].InvalidateVisual(); }
                lookName.Text = look >= 0 ? Onboarding.Looks[look].Name : "";
            }
            if (Screen == Job && line != null)
            {
                string now = Brained ? Onboarding.JobLine : Onboarding.JobWaitLine;
                if (line.Text != now) line.Text = now;
            }
        }

        internal string Crowded()
        {
            foreach (Control control in Page.Children)
            {
                if (!control.IsVisible) continue;
                NeonCaption caption = control as NeonCaption;
                if (caption != null && caption.Text.Length > 0)
                {
                    FormattedText text = MainframeTheme.Words(caption.Text, caption.Size, caption.Bold);
                    text.MaxTextWidth = caption.Width;
                    if (text.Height > caption.Height + 0.5 || text.WidthIncludingTrailingWhitespace > caption.Width + 0.5) return caption.Text;
                }
                NeonButton button = control as NeonButton;
                string wrong = button == null ? null : button.TextFits();
                if (wrong != null) return wrong;
                if (Canvas.GetLeft(control) + control.Width > Page.Width + 0.5 || Canvas.GetTop(control) + control.Height > Page.Height + 0.5) return AutomationProperties.GetName(control);
            }
            return null;
        }
    }

    internal sealed partial class Pet
    {
        private FirstRunCard firstRun;
        private string firstJob;
        internal Action<string> FirstJobTo;
        internal Action FirstRunPulls;
        internal readonly List<string> FirstRunLines = new List<string>();

        internal static bool WantsFirstRun(bool fresh, int step) { return fresh || step > 0; }

        internal bool FirstRunOpen { get { return firstRun != null; } }
        internal FirstRunCard FirstRunWindow { get { return firstRun; } }
        internal bool FirstRunBrainReady { get { return Brain.Model != null; } }
        internal int FirstRunPulled { get { return pulled; } }
        internal string FirstJobWaiting { get { return firstJob; } }

        internal void StartFirstRun(bool isFresh)
        {
            if (!WantsFirstRun(isFresh, Saved.FirstRunStep) || firstRun != null) return;
            if (isFresh && Saved.FirstRunStep == 0) { Saved.FirstRunStep = FirstRunCard.Wake; Save(); }
            firstRun = new FirstRunCard(this, Saved.FirstRunStep);
            firstRun.Closed += delegate { firstRun = null; };
            firstRun.Show();
            PlaceFirstRun();
        }

        private void PlaceFirstRun()
        {
            if (firstRun == null) return;
            PixelRect area = Area;
            double scale = DesktopScaling;
            int wide = (int)Math.Ceiling(firstRun.Width * scale), tall = (int)Math.Ceiling(firstRun.Height * scale), gap = Px(body.Size * 0.55);
            int x = Head.X + gap + wide <= area.Right ? Head.X + gap : Math.Max(area.X, Head.X - gap - wide);
            int y = Math.Max(area.Y, Math.Min(FeetPoint.Y - tall, area.Bottom - tall));
            Look.Position(firstRun, new PixelPoint(x, y));
        }

        internal void FirstRunAt(int screen)
        {
            if (Saved.FirstRunStep == screen) return;
            Saved.FirstRunStep = screen;
            Save();
            if (firstRun != null && firstRun.IsVisible) PlaceFirstRun();
        }

        internal void FirstRunWake(int pick)
        {
            if (pick != 0 || FirstRunBrainReady || pulling) return;
            if (MainframeTier != 0) SetBrainTier(0);
            if (FirstRunPulls != null) FirstRunPulls();
            else PullBrain();
        }

        internal void FirstRunLook(string[] colours)
        {
            ModList.UseColours(colours);
        }

        internal void FirstRunSays(string line)
        {
            if (Program.Smoke != null) { FirstRunLines.Add(line); return; }
            Talk.Notice("(set up)", line);
        }

        internal void OpenMods()
        {
            OpenMainframe();
            mainframe.OpenPage("mods");
        }

        internal void FinishFirstRun(bool met, string job)
        {
            Saved.FirstRunStep = 0;
            Save();
            if (met) meetAt = -1;
            else meetAt = Now + 4;
            if (job != null)
            {
                if (FirstRunBrainReady) SendFirstJob(job);
                else { firstJob = job; FirstRunSays(Onboarding.JobQueuedLine); }
            }
            if (!FirstRunBrainReady && !pulling && brainPending == 0 && !OperatingSystem.IsLinux() && Program.Smoke == null) OpenMainframe();
        }

        private void SendFirstJob(string job)
        {
            if (FirstJobTo != null) FirstJobTo(job);
            else Hear(job);
        }

        internal void CheckFirstJob()
        {
            if (firstJob == null || !FirstRunBrainReady) return;
            string job = firstJob;
            firstJob = null;
            SendFirstJob(job);
        }
    }
}
