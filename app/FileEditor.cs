using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her artist's second panel, drawn in code from what the art's 60 frames measure (PanelArt), so its text stays sharp at any scale and
    // it can be big enough to edit in, dressed like the Arasaka ID panel in Cyberpunk 2077 that Lachy sent (8 Nov). Three windows make
    // it up and always move together. This one is see-through and holds the spine, the sheet, the rule, the labels and the shadow, and
    // it plays the animation. The page above it holds the text box, tabs and buttons with nothing behind them, so the sheet shows
    // between the words; Windows draws no controls on a see-through window, so the page draws itself. The frost under the panel blurs
    // whatever is behind the sheet.
    internal sealed class FileEditor : Form
    {
        // Lachy's open questions, a line each.
        internal const string PanelName = "CAPRI";                  // Her artist's NAME HERE. The file's name is the title.
        internal const string ProjectName = "HER FILES";            // Her artist's PROJECT NOTES NAME.
        internal const string TitleFace = "Capri.Font.Title.ttf";   // Capri Mainframe Bold: the title, the labels and the buttons.
        internal const float BodySize = 17;                         // Share Tech Mono in px at 100%: Look.Mono in Page.Style.
        internal const float Zoom = 1.6f;                           // The panel's size against the art's frames, at 100%, on a big enough screen.
        internal static readonly Color Red = Color.FromArgb(0xEB, 0x4A, 0x42);
        internal static readonly Color Black = Color.FromArgb(0, 0, 0);
        // What the page's controls are drawn on: her red on screen, and white or black for a moment while Matted works out how strong
        // each pixel is.
        internal static Color Paper = Red;
        // The page's words, in the salmon of the reference's Arasaka panel, and the words on her red when a button is filled (8 Nov).
        internal static readonly Color Salmon = Color.FromArgb(245, 128, 120);
        internal static readonly Color Dark = Color.FromArgb(30, 12, 14);
        // How the page's top and sides sit inside the sheet, in the art's pixels.
        private const float PageInset = 7;

        // Her artist's notes: 24 fps, every line typing at its own frame at one speed. A file is far longer than those lines were,
        // so the page reveals across BodyFrames instead, and any click or key there ends the animation. Its frames are drawn as
        // often as the screen shows them (Pacer), each one where the art's 24 a second have got to by then.
        private const double Fps = 24;
        private const double TypeRate = 1.5;
        private const double BodyFrames = 14;
        private const int VersionAt = 15, NameAt = 30, ProjectAt = 35, TitleAt = 40, BodyAt = 45;
        // The page's window takes over from the panel's own drawing once the sheet has stopped moving.
        private const int PageAt = 36;
        private const int LabelsDone = 42;
        private const double LastFrame = BodyAt + BodyFrames;
        // Closing plays the opening backwards from where its words began.
        private const int RewindFrom = TitleAt;
        // How long the opening and the closing last in the art's frames, for her smoke test.
        internal const double ArtFps = Fps, OpeningFrames = LastFrame - 1, ClosingFrames = BodyFrames + RewindFrom;
        private const string Unsaved = ".unsaved";

        private enum Stage { Waiting, Opening, Open, Untyping, Closing, Gone }

        internal event Action Saved;
        internal bool Motion = true;
        // The frames it drew, in order, while her smoke test watches: where in the art each one was, when on the animation's clock,
        // and how long it took to draw. And a stop on its clock for stepping it by hand.
        internal List<double> Drawn, DrawnAt, DrawnMs;
        internal bool Held;
        private readonly float forcedScale, forcedArt;
        private readonly Page page;
        private readonly Frost frost;
        private readonly Doc[] docs;
        private readonly Pacer pacer = new Pacer();
        private readonly Stopwatch clock = new Stopwatch();
        private readonly string version = Assembly.GetExecutingAssembly().GetName().Version.ToString(3) + "V";
        private Stage stage = Stage.Waiting;
        private double frame, untyped, ticked, stepped;
        private int drawnLetters = -1, frostWaited;
        private bool frostDue;
        private float s = 1, a = 1;
        private int current;
        private Rectangle pageBox;
        private PointF[] frostShape;
        private Native.Surface surface;
        private Bitmap shade, grain, grained, veil;
        private int[] blurred, smeared, stretched;
        private Font nameFont, smallFont, spineFont;
        private bool dragging, guarded, pageUp, pending;
        private Point pressedAt, startedAt;
        private Action after;

        internal FileEditor(float scale = 0, float art = 0)
        {
            forcedScale = scale;
            forcedArt = art;
            Text = "Capri file editor";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Black;
            Cursor = Cursors.SizeAll;
            docs = new[]
            {
                new Doc("personality.md", delegate { if (!Brain.MakePersonality()) throw new IOException("No personality.md."); return Brain.OpenPersonality(); }),
                new Doc("me.md", delegate { return Brain.OpenNote(false); })
            };
            frost = new Frost();
            page = new Page(this);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        // Layered, off Alt+Tab and never active: the page takes the keyboard, so a click on the spine leaves the caret where it was.
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000;
                return value;
            }
        }

        internal bool Resting { get { return stage == Stage.Open; } }
        internal bool Leaving { get { return stage == Stage.Untyping || stage == Stage.Closing; } }
        internal bool Over { get { return stage == Stage.Gone; } }
        internal bool Guarding { get { return guarded; } }
        internal double Frame { get { return frame; } }
        // The pace its frames were last asked for at, the refresh rate of its screen, whether they were in step with the screen's
        // refreshes, and whether they're still coming.
        internal double FrameRate { get { return pacer.Rate; } }
        internal double ScreenRate { get { return pacer.Hz; } }
        internal bool InStep { get { return pacer.InStep; } }
        internal bool Pacing { get { return pacer.Running; } }
        internal bool Dirty { get { foreach (Doc doc in docs) if (doc.Dirty) return true; return false; } }
        internal string FileShown { get { return docs[current].Name; } }
        internal string Status { get { return page.Status; } }
        internal string Draft { get { return page.Words; } set { page.Words = value; } }
        internal Control PageWindow { get { return page; } }
        internal Rectangle PageBox { get { return pageBox; } }
        internal float ArtScale { get { return a; } }
        internal float UiScale { get { return s; } }
        internal int BodyPixelsHigh { get { return page.BoxHeight; } }
        internal string BodyFontName { get { return page.BoxFont; } }
        internal string[] Choices { get { return page.Labels; } }
        internal string Scrolled { get { return page.Scrolled; } }
        internal Control Box { get { return page.TextArea; } }
        internal bool Unsure { get { return page.Unsure; } }
        internal Form BlurWindow { get { return frost; } }
        internal bool Blurred { get { return frost.Blurred; } }
        internal bool BlurUnder { get { return IsHandleCreated && frost.IsHandleCreated && Native.GetWindow(Handle, 2) == frost.Handle; } }
        // Where the sheet is on screen, at rest.
        internal Rectangle SheetOnScreen { get { return Rectangle.FromLTRB(Left + (int)Sx(PanelArt.SheetLeft), Top + (int)Sy(PanelArt.Crown), Left + (int)Sx(PanelArt.SheetRight), Top + (int)Sy(PanelArt.SheetFoot)); } }
        internal int PageRenders { get { return page.Renders; } }
        internal double PageRedrawMs { get { return page.SlowestMs; } }
        // How long the page's last redraw spent painting itself on white and on black.
        internal double PagePaintMs { get { return page.PaintMs; } }

        // How long the page takes to draw itself again, on average over `count` redraws, for her smoke test.
        internal double TimeRedraws(int count)
        {
            Stopwatch timed = Stopwatch.StartNew();
            for (int i = 0; i < count; i++) page.Render();
            return timed.Elapsed.TotalMilliseconds / Math.Max(1, count);
        }
        internal Rectangle PageCaret { get { return page.Caret; } }

        // The blur's shape, in its own window's pixels.
        internal Rectangle BlurBox
        {
            get
            {
                Edges box;
                return frost.IsHandleCreated && GetWindowRgnBox(frost.Handle, out box) > 1 ? Rectangle.FromLTRB(box.Left, box.Top, box.Right, box.Bottom) : Rectangle.Empty;
            }
        }

        // Opens the panel on personality.md (0) or me.md (1), or brings it up and turns to that file if it's open already.
        internal void Open(int which)
        {
            if (stage == Stage.Gone) return;
            if (stage == Stage.Waiting) { Begin(which); return; }
            if (Leaving) { stage = Stage.Opening; after = null; Run(); }
            if (which != current) Turn(which);
            if (!pending) Raise();
        }

        // The first frame goes up straight away and the page after it: the first panel of a session takes a moment to build.
        private void Begin(int which)
        {
            Point mouse = Cursor.Position;
            Rectangle area = Screen.FromPoint(mouse).WorkingArea;
            Measure(Arrange.DpiAt(mouse), area);
            Size = PlateSize;
            // Above and left of the screen's centre, as her artist asked.
            Location = new Point(Math.Max(area.Left, Math.Min(area.Right - Width, area.Left + (area.Width - Width) / 2 - area.Width / 16)), Math.Max(area.Top, Math.Min(area.Bottom - Height, area.Top + (area.Height - Height) / 2 - area.Height / 16)));
            current = which;
            stage = Stage.Opening;
            pending = true;
            frost.Bounds = new Rectangle(Location, Size);
            frost.Show();
            Show();
            if (!Motion) { Ready(); Seek(LastFrame); return; }
            Seek(1);
            if (Held) { Ready(); return; }
            BeginInvoke(new Action(delegate { Ready(); Run(); }));
        }

        private void Ready()
        {
            if (!pending) return;
            pending = false;
            page.Strength = 0;
            page.Show(this);
            page.Fit();
            page.Take(docs[current]);
            Raise();
            TakeVeil();
            page.Veiled(true);
        }

        private void Run()
        {
            clock.Restart();
            ticked = 0;
            pacer.Start(Handle, Screen.FromHandle(Handle).DeviceName);
        }

        // A frame wherever the clock has got to, in the art's frames: a frame drawn late lands where it should be by then.
        private void Tick()
        {
            double now = clock.Elapsed.TotalSeconds * Fps, step = now - ticked;
            ticked = now;
            stepped = step;
            if (stage == Stage.Opening) Seek(frame + step);
            else if (stage == Stage.Untyping) Untype(untyped + step);
            else if (stage == Stage.Closing) Rewind(frame - step);
            else pacer.Stop();
        }

        internal void Watch()
        {
            Drawn = new List<double>();
            DrawnAt = new List<double>();
            DrawnMs = new List<double>();
        }

        private void Record(double value, long began)
        {
            if (Drawn == null) return;
            Drawn.Add(value);
            if (DrawnAt != null) DrawnAt.Add(clock.Elapsed.TotalSeconds);
            if (DrawnMs != null) DrawnMs.Add((Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
        }

        // The opening at this frame.
        internal void Seek(double to)
        {
            long began = Stopwatch.GetTimestamp();
            frame = Math.Min(LastFrame, Math.Max(0, to));
            PageUp(frame >= PageAt);
            if (frame <= LabelsDone || surface == null) DrawFrame();
            page.Reveal();
            Record(frame, began);
            if (frame >= LastFrame) Rest();
        }

        private void PageUp(bool up)
        {
            if (up == pageUp) return;
            pageUp = up;
            page.Lift(up ? 1 : 0);
        }

        // Any click or key ends the opening. Nothing else needs it to play out.
        internal void Finish()
        {
            if (stage != Stage.Opening) return;
            Ready();
            Seek(LastFrame);
        }

        private void Rest()
        {
            pacer.Stop();
            stage = Stage.Open;
            DrawFrame();
            page.Veiled(false);
            DropVeil();
            DropSurface();
            page.Settle();
        }

        // Closing: every line takes itself back at once, then the opening plays backwards. Her artist's note.
        private void Depart()
        {
            stage = Stage.Untyping;
            page.Quiet();
            untyped = 0;
            frame = Math.Min(frame, LastFrame);
            TakeVeil();
            page.Veiled(true);
            if (!Motion) { Finished(); return; }
            if (!Held) Run();
        }

        internal void Untype(double to)
        {
            long began = Stopwatch.GetTimestamp();
            untyped = to;
            // Only the labels change on the panel while they take themselves back, so it's drawn again only when they do.
            if (surface == null || Letters() != drawnLetters) DrawFrame();
            page.Reveal();
            Record(-untyped, began);
            if (untyped < BodyFrames) return;
            stage = Stage.Closing;
            PageUp(false);
            frame = Math.Min(frame, RewindFrom);
            DrawFrame();
        }

        internal void Rewind(double to)
        {
            long began = Stopwatch.GetTimestamp();
            frame = Math.Max(0, to);
            DrawFrame();
            Record(frame, began);
            if (frame <= 0) Finished();
        }

        private void Finished()
        {
            pacer.Stop();
            stage = Stage.Gone;
            Action next = after;
            after = null;
            page.Close();
            frost.Close();
            Close();
            if (next != null) next();
        }

        // Asks to close. With unsaved changes the panel stays open and asks, and `then` runs only once they've answered.
        internal void Ask(Action then = null)
        {
            if (stage == Stage.Waiting || stage == Stage.Gone) { if (then != null) then(); return; }
            if (Leaving) return;
            Finish();
            page.Keep();
            after = then;
            if (Dirty) { guarded = true; Raise(); page.Guard(UnsavedNames()); return; }
            Depart();
        }

        // True when closing would lose nothing. Otherwise it comes up and asks, and runs `then` after they answer.
        internal bool Clear(Action then)
        {
            if (stage == Stage.Waiting || stage == Stage.Gone || Leaving) return true;
            Ready();
            page.Keep();
            if (!Dirty) return true;
            Ask(then);
            return false;
        }

        private string UnsavedNames()
        {
            List<string> names = new List<string>();
            foreach (Doc doc in docs) if (doc.Dirty) names.Add(doc.Name);
            return string.Join(" and ", names.ToArray());
        }

        internal void GuardSave()
        {
            for (int i = 0; i < docs.Length; i++)
            {
                if (!docs[i].Dirty) continue;
                string problem = Write(docs[i]);
                if (problem == null) continue;
                Turn(i);
                page.Say(problem + " Your changes are still here.", true);
                return;
            }
            guarded = false;
            Depart();
        }

        internal void GuardDiscard()
        {
            foreach (Doc doc in docs) { if (!doc.Dirty) continue; doc.Revert(); ForgetDraft(doc); }
            guarded = false;
            Depart();
        }

        internal void GuardKeep()
        {
            guarded = false;
            after = null;
            page.Unguard();
            page.Mark(docs[current]);
        }

        // Keeps unsaved changes beside their file when Capri goes down with no chance to ask, like a Windows shutdown. They come
        // back as unsaved changes the next time that file opens here.
        internal void KeepDrafts()
        {
            if (stage == Stage.Waiting || stage == Stage.Gone) return;
            Ready();
            page.Keep();
            foreach (Doc doc in docs)
            {
                if (!doc.Dirty) continue;
                try { File.WriteAllText(doc.Note.Path + Unsaved, doc.ForFile()); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        private static void ForgetDraft(Doc doc)
        {
            if (doc.Note == null) return;
            try { File.Delete(doc.Note.Path + Unsaved); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        // Reads a file the first time it's shown, and again whenever it has no changes, as she may have added to me.md.
        private void Read(Doc doc)
        {
            try
            {
                if (doc.Note == null)
                {
                    doc.Note = doc.Open();
                    doc.Revert();
                    string draft = doc.Note.Path + Unsaved;
                    if (File.Exists(draft))
                    {
                        doc.Draft = Doc.Shown(File.ReadAllText(draft));
                        if (doc.Dirty) doc.Told = "Your unsaved changes came back.";
                        else File.Delete(draft);
                    }
                }
                else if (!doc.Dirty) { doc.Note.Reload(); doc.Revert(); }
                doc.Problem = null;
            }
            catch (IOException) { doc.Problem = "Couldn't read " + doc.Label + ". Try opening it again."; }
            catch (UnauthorizedAccessException) { doc.Problem = "Couldn't read " + doc.Label + ". Check access to its folder."; }
        }

        internal void Turn(int which)
        {
            if (stage == Stage.Waiting || stage == Stage.Gone) return;
            Finish();
            page.Keep();
            current = which;
            page.Take(docs[current]);
        }

        internal void Next() { Turn((current + 1) % docs.Length); }

        internal void Recheck()
        {
            if (stage != Stage.Open || guarded) return;
            Doc doc = docs[current];
            if (doc.Note == null || doc.Dirty) return;
            string was = doc.Draft;
            Read(doc);
            if (doc.Draft != was) page.Take(doc);
        }

        internal bool Save()
        {
            Finish();
            page.Keep();
            Doc doc = docs[current];
            if (doc.Note == null) return false;
            string problem = Write(doc);
            page.Mark(doc);
            if (problem != null) { page.Say(problem, true); return false; }
            page.Say(doc == docs[0] && Brain.PersonalityNote.Length > 0 ? "Saved. " + Brain.PersonalityNote + "." : "Saved.", false);
            return true;
        }

        private string Write(Doc doc)
        {
            string problem = doc.Note.Save(doc.ForFile());
            if (problem != null) return problem;
            doc.Revert();
            ForgetDraft(doc);
            if (doc == docs[0] && Saved != null) Saved();
            return null;
        }

        // Back to the file on disk: the old notes editor's Discard + reload.
        internal void Reload()
        {
            Doc doc = docs[current];
            if (doc.Note == null) { Read(doc); page.Take(doc); return; }
            try
            {
                doc.Note.Reload();
                doc.Revert();
                ForgetDraft(doc);
                page.Take(doc);
                page.Say("Reloaded.", false);
            }
            catch (IOException) { page.Say("Couldn't reload this note. Your changes are still here.", true); }
            catch (UnauthorizedAccessException) { page.Say("Couldn't reload this note. Your changes are still here.", true); }
        }

        internal void Pressed()
        {
            Finish();
            Raise();
            dragging = true;
            pressedAt = Cursor.Position;
            startedAt = Location;
        }

        internal void Dragged()
        {
            if (!dragging) return;
            Point now = Cursor.Position;
            MoveTo(new Point(startedAt.X + now.X - pressedAt.X, startedAt.Y + now.Y - pressedAt.Y));
        }

        internal void Released() { dragging = false; }

        // All three windows in one step, so they can't come apart. A corner stays on screen to grab.
        internal void MoveTo(Point at)
        {
            Rectangle all = SystemInformation.VirtualScreen;
            int keep = Px(120);
            int x = Math.Max(all.Left - Width + keep, Math.Min(all.Right - keep, at.X)), y = Math.Max(all.Top - Px(30), Math.Min(all.Bottom - keep, at.Y));
            IntPtr moves = BeginDeferWindowPos(3);
            if (moves != IntPtr.Zero) moves = DeferWindowPos(moves, Handle, IntPtr.Zero, x, y, 0, 0, 0x0015);
            if (moves != IntPtr.Zero) moves = DeferWindowPos(moves, page.Handle, IntPtr.Zero, x + pageBox.X, y + pageBox.Y, 0, 0, 0x0015);
            if (moves != IntPtr.Zero && frost.IsHandleCreated) moves = DeferWindowPos(moves, frost.Handle, IntPtr.Zero, x + frost.Offset.X, y + frost.Offset.Y, 0, 0, 0x0015);
            if (moves != IntPtr.Zero && EndDeferWindowPos(moves)) return;
            Location = new Point(x, y);
            page.Location = new Point(x + pageBox.X, y + pageBox.Y);
            frost.Location = new Point(x + frost.Offset.X, y + frost.Offset.Y);
        }

        // The page comes up with the keyboard, the panel sits straight under it, and the blur straight under that.
        private void Raise()
        {
            page.Activate();
            Under();
        }

        internal void Under()
        {
            if (IsHandleCreated && page.IsHandleCreated) SetWindowPos(Handle, page.Handle, 0, 0, 0, 0, 0x0013);
            if (IsHandleCreated && frost.IsHandleCreated) SetWindowPos(frost.Handle, Handle, 0, 0, 0, 0, 0x0013);
        }

        private int Px(float value) { return (int)Math.Round(value * s); }
        private float Ax(float x) { return (x - PanelArt.Left) * a; }
        private float Ay(float y) { return (y - PanelArt.Top) * a; }
        private float Sx(float x) { return (float)Math.Round(Ax(x)); }
        private float Sy(float y) { return (float)Math.Round(Ay(y)); }
        private Size PlateSize { get { return new Size((int)Math.Ceiling(PanelArt.Wide * a), (int)Math.Ceiling(PanelArt.High * a)); } }

        // Its scale for this screen: the art's pixels times Zoom, smaller only if the panel wouldn't fit. Text follows the screen's scale.
        private void Measure(int dpi, Rectangle area)
        {
            s = forcedScale > 0 ? forcedScale : dpi / 96f;
            a = forcedArt > 0 ? forcedArt : forcedScale > 0 ? Zoom * s : Math.Min(Zoom * s, Math.Min(area.Width / PanelArt.Wide, area.Height / PanelArt.High));
            pageBox = Rectangle.FromLTRB((int)Sx(PanelArt.SheetLeft + PageInset), (int)Sy(PanelArt.Crown + PageInset), (int)Sx(PanelArt.SheetWaist - PageInset), (int)Sy(PanelArt.SheetFoot - PageInset));
            Dress();
        }

        private void Dress()
        {
            DropFonts();
            nameFont = Title(20 * a);
            smallFont = Title(11.5f * a);
            spineFont = Title(12.5f * a);
            if (grain != null) grain.Dispose();
            grain = Grain((int)Math.Max(1, Math.Round(a / Zoom)));
            if (shade != null) { shade.Dispose(); shade = null; }
            DropSurface();
        }

        // The panel's own picture, the grain to lay over it and the shadow's workings are only kept while it moves.
        private void DropSurface()
        {
            if (surface != null) { surface.Dispose(); surface = null; }
            if (grained != null) { grained.Dispose(); grained = null; }
            blurred = smeared = stretched = null;
        }

        private void DropFonts()
        {
            foreach (Font font in new[] { nameFont, smallFont, spineFont }) if (font != null) font.Dispose();
        }

        internal static Font Title(float pixels)
        {
            FontFamily family = Look.Face(TitleFace);
            if (family == null) return Neon.Display(pixels, GraphicsUnit.Pixel);
            return new Font(family, pixels, family.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        }

        // Fine grain: specks of black and of her red, the same every time.
        private static Bitmap Grain(int size)
        {
            const int Tile = 96;
            Bitmap tile = new Bitmap(Tile * size, Tile * size, PixelFormat.Format32bppArgb);
            Random noise = new Random(865);
            using (Graphics pen = Graphics.FromImage(tile))
                for (int y = 0; y < Tile; y++)
                    for (int x = 0; x < Tile; x++)
                    {
                        double speck = noise.NextDouble() + noise.NextDouble() - 1;
                        int alpha = (int)(Math.Abs(speck) * 34);
                        using (Brush ink = new SolidBrush(Color.FromArgb(alpha, speck < 0 ? Black : Red))) pen.FillRectangle(ink, x * size, y * size, size, size);
                    }
            return tile;
        }

        // Which of the labels' letters show, as one number.
        private int Letters() { return Typed(version.Length, VersionAt) + 100 * (Typed(version.Length, NameAt) + 100 * (Typed(PanelName.Length, NameAt) + 100 * Typed(ProjectName.Length, ProjectAt))); }

        private int Typed(int length, int at)
        {
            if (stage == Stage.Closing) return 0;
            if (stage == Stage.Untyping) return Math.Max(0, Math.Min(length, (int)Math.Ceiling(length - untyped * TypeRate)));
            return Math.Max(0, Math.Min(length, (int)Math.Floor((frame - at) * TypeRate) + 1));
        }

        private void DrawFrame()
        {
            if (!IsHandleCreated) return;
            if (surface == null) surface = new Native.Surface(Width, Height);
            using (Graphics pen = Graphics.FromImage(surface.Image)) PaintFrame(pen);
            surface.Show(Handle, Location);
            if (pacer.Running) { frostDue = true; return; }
            frost.Shape(frostShape, Location);
            frostDue = false;
            frostWaited = 0;
        }

        // The blur under the sheet, once a frame from the pacer is on screen (8 Nov). Reshaping it takes Windows about as long as
        // drawing a frame, so while the sheet slides it's reshaped every third frame, after the frame rather than before it, and
        // always to a shape inside the sheet: the one it has now while it grows, and the one it will have a few frames on while it
        // shrinks. The sheet slides between the art's frames 14 and 35, and the blur takes its own shape as soon as it stops.
        private void Follow()
        {
            if (!frostDue || !IsHandleCreated) return;
            bool closing = stage == Stage.Closing;
            double at = closing ? Math.Max(0, frame - 4 * Math.Max(Math.Abs(stepped), Fps / Math.Max(1, pacer.Rate))) : frame;
            if (++frostWaited < 3 && (closing ? at > 14 : at < 35)) return;
            frostDue = false;
            frostWaited = 0;
            float sheet = PanelArt.Along(PanelArt.SheetEnds, 14, at), depth = PanelArt.Along(PanelArt.StepDepths, 16, at);
            frost.Shape(sheet > PanelArt.SheetLeft ? SheetShape(sheet, depth, 1) : null, Location);
        }

        private void PaintFrame(Graphics pen)
        {
            pen.Clear(Color.Transparent);
            pen.SmoothingMode = SmoothingMode.AntiAlias;
            pen.PixelOffsetMode = PixelOffsetMode.Half;
            frostShape = null;
            drawnLetters = Letters();
            if (frame <= 0) return;
            float spine = PanelArt.Along(PanelArt.SpineEnds, 1, frame, PanelArt.Crown);
            float sheet = PanelArt.Along(PanelArt.SheetEnds, 14, frame), depth = PanelArt.Along(PanelArt.StepDepths, 16, frame);
            float rule = frame < 21 ? 0 : PanelArt.Along(PanelArt.RuleEnds, 21, frame);
            float block = frame < 25 ? 0 : PanelArt.Along(PanelArt.BlockEnds, 25, frame);
            double sheetAlpha = PanelArt.Glassy(PanelArt.Along(PanelArt.SheetAlphas, 17, frame) / 255);
            PointF[] spineShape = SpineShape(spine);
            PointF[] sheetShape = sheet > PanelArt.SheetLeft ? SheetShape(sheet, depth, 0) : null;
            // The blur stops a little inside the sheet's edge, so its stepped outline stays under the rim.
            frostShape = sheetShape != null ? SheetShape(sheet, depth, 1) : null;
            string versionWords = version.Substring(0, Typed(version.Length, VersionAt)), topVersion = version.Substring(0, Typed(version.Length, NameAt));
            string name = PanelName.Substring(0, Typed(PanelName.Length, NameAt)), project = ProjectName.Substring(0, Typed(ProjectName.Length, ProjectAt));
            RectangleF ruleBox = RectangleF.FromLTRB(Sx(PanelArt.RuleLeft), Sy(PanelArt.RuleTop), Sx(Math.Max(rule, PanelArt.RuleLeft)), Sy(PanelArt.RuleBottom));
            RectangleF blockBox = RectangleF.FromLTRB(Sx(PanelArt.RuleLeft), Sy(PanelArt.BlockTop), Sx(Math.Max(block, PanelArt.RuleLeft)), Sy(PanelArt.BlockBottom));
            PointF versionAt = new PointF(Ax(PanelArt.RuleLeft), Ay(67)), nameAt = new PointF(Ax(PanelArt.RuleLeft), Ay(83)), projectAt = new PointF(Ax(PanelArt.BlockEnd + 8), Ay(PanelArt.BlockTop - 0.5f));
            PaintShadow(pen, spineShape, sheetShape, sheetAlpha, ruleBox, blockBox, topVersion, versionAt, name, nameAt, project, projectAt);
            if (sheetShape != null)
            {
                // The sheet's own fill replaces the shadow under it, and fades from her solid red to what the art holds at the end.
                pen.CompositingMode = CompositingMode.SourceCopy;
                using (Brush fill = new SolidBrush(PanelArt.SheetFill(sheetAlpha))) pen.FillPolygon(fill, sheetShape);
                pen.CompositingMode = CompositingMode.SourceOver;
                LayGrain(pen, sheetShape);
                // No page box under the words (Lachy, 8 Nov): the sheet stays her artist's see-through right up to the text.
                using (Pen edge = new Pen(Color.FromArgb(220, Red), 2.4f * a)) { edge.LineJoin = LineJoin.Miter; pen.DrawPolygon(edge, SheetShape(sheet, depth, 1.2f)); }
            }
            using (Brush body = new SolidBrush(PanelArt.Spine)) pen.FillPolygon(body, spineShape);
            LayGrain(pen, spineShape);
            if (spine > PanelArt.TickY + 2) using (Brush tick = new SolidBrush(Color.FromArgb((int)(170 * PanelArt.Grown(frame, 6, 14)), Black))) pen.FillRectangle(tick, Sx(141), Sy(PanelArt.TickY), Math.Max(1, Sx(148) - Sx(141)), Math.Max(1, (float)Math.Round(a)));
            using (Brush solid = new SolidBrush(PanelArt.Rule))
            {
                if (ruleBox.Width > 0) pen.FillRectangle(solid, ruleBox);
                if (blockBox.Width > 0) pen.FillRectangle(solid, blockBox);
            }
            pen.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (Brush red = new SolidBrush(Red))
            {
                Say(pen, topVersion, smallFont, red, versionAt);
                Say(pen, name, nameFont, red, nameAt);
                Say(pen, project, smallFont, red, projectAt);
            }
            if (versionWords.Length > 0 && spine > 500)
            {
                GraphicsState flat = pen.Save();
                pen.TextRenderingHint = TextRenderingHint.AntiAlias;
                pen.TranslateTransform(Ax(PanelArt.SpineLeft + 12), Ay(PanelArt.Foot - 14));
                pen.RotateTransform(-90);
                using (Brush black = new SolidBrush(Black)) Say(pen, versionWords, spineFont, black, PointF.Empty);
                pen.Restore(flat);
            }
        }

        // The grain over a shape: a panel-sized layer of it, through the shape's outline (8 Nov). GDI+'s texture brush drew the
        // same but took 50 ms to fill the sheet; only the slanted edges' half-covered pixels go without their faint specks now.
        private void LayGrain(Graphics pen, PointF[] shape)
        {
            using (GraphicsPath outline = new GraphicsPath())
            {
                outline.AddPolygon(shape);
                pen.SetClip(outline);
                pen.DrawImageUnscaled(GrainLayer(), 0, 0);
                pen.ResetClip();
            }
        }

        // The grain tile repeated over the whole panel from its top left corner, as the texture brush laid it.
        private Bitmap GrainLayer()
        {
            if (grained != null && grained.Width == Width && grained.Height == Height) return grained;
            if (grained != null) grained.Dispose();
            grained = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            BitmapData from = grain.LockBits(new Rectangle(0, 0, grain.Width, grain.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                BitmapData into = grained.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
                try
                {
                    unsafe
                    {
                        for (int y = 0; y < Height; y++)
                        {
                            uint* row = (uint*)((byte*)into.Scan0 + (long)y * into.Stride), tile = (uint*)((byte*)from.Scan0 + (long)(y % from.Height) * from.Stride);
                            for (int x = 0; x < Width; x++) row[x] = tile[x % from.Width];
                        }
                    }
                }
                finally { grained.UnlockBits(into); }
            }
            finally { grain.UnlockBits(from); }
            return grained;
        }

        private static void Say(Graphics pen, string words, Font font, Brush ink, PointF at)
        {
            if (words.Length > 0) pen.DrawString(words, font, ink, at, StringFormat.GenericTypographic);
        }

        // The spine down to `end`. The art's small details come in as it grows: the top corner's step over frames 3 to 8, the bottom corner's
        // cut, which travels with its foot, over frames 4 to 12, and the slit down its right side over frames 11 to 14.
        private PointF[] SpineShape(float end)
        {
            List<PointF> points = new List<PointF>();
            float crown = PanelArt.Crown, left = PanelArt.SpineLeft, right = PanelArt.SpineRight, step = crown + 4.5f * PanelArt.Grown(frame, 3, 8);
            float slit = 2.5f * PanelArt.Grown(frame, 11, 14), cut = PanelArt.Grown(frame, 4, 12);
            points.Add(new PointF(left, step));
            points.Add(new PointF(144.5f, step));
            points.Add(new PointF(150.5f, crown));
            points.Add(new PointF(right, crown));
            if (end > 247)
            {
                points.Add(new PointF(right, 247));
                points.Add(new PointF(right - slit, 251));
                if (end > 470)
                {
                    points.Add(new PointF(right - slit, 470));
                    points.Add(new PointF(right, 474));
                }
                else points.Add(new PointF(right - slit, end));
            }
            points.Add(new PointF(end > 470 || end <= 247 ? right : right - slit, end));
            points.Add(new PointF(left + 3 * cut, end));
            points.Add(new PointF(left + 3 * cut, end - 5 * cut));
            points.Add(new PointF(left, end - 8 * cut));
            return Device(points);
        }

        // The sheet with its right side at `right`: the art's chamfer and step travel with that edge, and deepen as it slides out. `inset`
        // draws the rim.
        private PointF[] SheetShape(float right, float depth, float inset)
        {
            float left = PanelArt.SheetLeft, top = PanelArt.Crown, foot = PanelArt.SheetFoot, chamfer = 4.5f + depth * 9 / PanelArt.StepIn;
            float near = right - depth;
            List<PointF> points = new List<PointF>
            {
                new PointF(left + inset, top + inset),
                new PointF(Math.Max(left + inset, right - chamfer - inset / 2), top + inset),
                new PointF(Math.Max(left + inset, right - inset), top + chamfer + inset / 2),
                new PointF(Math.Max(left + inset, right - inset), PanelArt.StepTop - inset / 2),
                new PointF(Math.Max(left + inset, near - inset), PanelArt.StepTop + depth - inset / 2),
                new PointF(Math.Max(left + inset, near - inset), foot - inset),
                new PointF(left + inset, foot - inset)
            };
            return Device(points);
        }

        private PointF[] Device(List<PointF> points)
        {
            PointF[] placed = new PointF[points.Count];
            for (int i = 0; i < placed.Length; i++) placed[i] = new PointF(Sx(points[i].X), Sy(points[i].Y));
            return placed;
        }

        // A soft black shadow round everything, her red text included so it holds up on any wallpaper. Drawn small and blurred, then
        // stretched, so it costs the same at 200%. The stretch goes straight into the panel's pixels and only where the shadow is:
        // GDI+ stretching it over the whole panel took most of each frame (8 Nov).
        private void PaintShadow(Graphics pen, PointF[] spine, PointF[] sheet, double sheetAlpha, RectangleF rule, RectangleF block, string topVersion, PointF versionAt, string name, PointF nameAt, string project, PointF projectAt)
        {
            int small = Math.Max(2, (int)Math.Round(a * 2.5f));
            if (shade == null) shade = new Bitmap(Width / small + 2, Height / small + 2, PixelFormat.Format32bppPArgb);
            using (Graphics ink = Graphics.FromImage(shade))
            {
                ink.Clear(Color.Transparent);
                ink.ScaleTransform(1f / small, 1f / small);
                ink.SmoothingMode = SmoothingMode.AntiAlias;
                ink.PixelOffsetMode = PixelOffsetMode.Half;
                using (Brush black = new SolidBrush(Black))
                {
                    ink.FillPolygon(black, spine);
                    if (rule.Width > 0) ink.FillRectangle(black, rule);
                    if (block.Width > 0) ink.FillRectangle(black, block);
                    Say(ink, topVersion, smallFont, black, versionAt);
                    Say(ink, name, nameFont, black, nameAt);
                    Say(ink, project, smallFont, black, projectAt);
                }
                if (sheet != null) using (Brush soft = new SolidBrush(Color.FromArgb((int)(Math.Max(sheetAlpha, 0.5) * 255), Black))) ink.FillPolygon(soft, sheet);
            }
            Rectangle covered = Blur(shade, Math.Max(1, (int)Math.Round(5 * a / small)));
            pen.Flush(FlushIntention.Sync);
            Stretch(covered, small, shade.Width);
        }

        // The shade's alpha, blurred three times over by `radius` and at the shadow's strength, into `blurred`, kept between frames.
        // Says which of its pixels have any.
        private Rectangle Blur(Bitmap image, int radius)
        {
            int width = image.Width, height = image.Height;
            if (blurred == null || blurred.Length != width * height) { blurred = new int[width * height]; smeared = new int[width * height]; }
            BitmapData data = image.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
            try
            {
                unsafe
                {
                    for (int y = 0; y < height; y++)
                    {
                        byte* row = (byte*)data.Scan0 + (long)y * data.Stride;
                        for (int x = 0; x < width; x++) blurred[y * width + x] = row[x * 4 + 3];
                    }
                }
            }
            finally { image.UnlockBits(data); }
            for (int pass = 0; pass < 3; pass++)
            {
                Smear(blurred, smeared, width, height, 1, width, radius);
                Smear(smeared, blurred, height, width, width, 1, radius);
            }
            // At 0.6 of its strength.
            int left = width, top = height, right = -1, bottom = -1;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    int at = y * width + x, alpha = Math.Min(255, blurred[at] * 3 / 5);
                    blurred[at] = alpha;
                    if (alpha == 0) continue;
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    bottom = y;
                }
            return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        // The blurred shade `small` times bigger, as black at its strength, by straight lines between its pixels' centres: what GDI+'s
        // bilinear stretch drew. A row at a time across, then down, and only over the panel's pixels the shade's `covered` ones reach.
        // The panel was just cleared, so these are written, not blended.
        private unsafe void Stretch(Rectangle covered, int small, int width)
        {
            if (covered.IsEmpty || surface == null) return;
            int wide = surface.Image.Width, high = surface.Image.Height, height = blurred.Length / width;
            int left = Math.Max(0, (covered.Left - 1) * small), right = Math.Min(wide, (covered.Right + 1) * small);
            int top = Math.Max(0, (covered.Top - 1) * small), bottom = Math.Min(high, (covered.Bottom + 1) * small);
            if (left >= right || top >= bottom) return;
            if (stretched == null || stretched.Length < height * wide) stretched = new int[height * wide];
            // Across: every shade row the panel's rows will sample, at the panel's width, in 256ths.
            int firstRow = Math.Max(0, Sampled(top, small) - 1), lastRow = Math.Min(height - 1, Sampled(bottom - 1, small) + 1);
            for (int row = firstRow; row <= lastRow; row++)
            {
                int from = row * width, into = row * wide;
                for (int x = left; x < right; x++)
                {
                    int low = Sampled(x, small), weight = Weight(x, small);
                    int one = blurred[from + Math.Max(0, Math.Min(width - 1, low))], two = blurred[from + Math.Max(0, Math.Min(width - 1, low + 1))];
                    stretched[into + x] = one * (256 - weight) + two * weight;
                }
            }
            // Down, into the panel.
            byte* pixels = (byte*)surface.Pixels;
            for (int y = top; y < bottom; y++)
            {
                int low = Sampled(y, small), weight = Weight(y, small);
                int one = Math.Max(firstRow, Math.Min(lastRow, low)) * wide, two = Math.Max(firstRow, Math.Min(lastRow, low + 1)) * wide;
                uint* line = (uint*)(pixels + (long)y * wide * 4);
                for (int x = left; x < right; x++)
                {
                    int alpha = (stretched[one + x] * (256 - weight) + stretched[two + x] * weight + 32768) >> 16;
                    if (alpha > 0) line[x] = (uint)alpha << 24;
                }
            }
        }

        // The shade pixel a panel pixel's centre falls after, and how far on towards the next, in 256ths.
        private static int Sampled(int at, int small) { int twice = 2 * at + 1 - small; return twice >= 0 ? twice / (2 * small) : -((-twice + 2 * small - 1) / (2 * small)); }
        private static int Weight(int at, int small) { return (2 * at + 1 - small - Sampled(at, small) * 2 * small) * 256 / (2 * small); }

        // One box-blur pass along `length`, over `rows` rows.
        private static void Smear(int[] from, int[] into, int length, int rows, int along, int across, int radius)
        {
            int window = radius * 2 + 1;
            for (int row = 0; row < rows; row++)
            {
                int start = row * across, sum = 0;
                for (int i = -radius; i <= radius; i++) sum += from[start + Math.Max(0, Math.Min(length - 1, i)) * along];
                for (int i = 0; i < length; i++)
                {
                    into[start + i * along] = sum / window;
                    sum += from[start + Math.Min(length - 1, i + radius + 1) * along] - from[start + Math.Max(0, i - radius) * along];
                }
            }
        }

        private void TakeVeil()
        {
            DropVeil();
            veil = page.Matted();
        }

        private void DropVeil()
        {
            if (veil != null) { veil.Dispose(); veil = null; }
        }

        // How much of the page shows: the title types at the art's speed, and everything else comes in together across BodyFrames,
        // its edge moving a pixel at a time so it sweeps evenly at any frame rate.
        private int Across(int wide)
        {
            double shown = stage == Stage.Untyping ? 1 - untyped / BodyFrames : stage == Stage.Closing ? 0 : (frame - BodyAt) / BodyFrames;
            return (int)(Math.Max(0, Math.Min(1, shown)) * wide);
        }

        private int TitleTyped() { return Typed(docs[current].Name.Length, TitleAt); }

        private void PaintVeil(Graphics pen, Size size, Rectangle title, int titleWide, bool clear)
        {
            if (clear) pen.Clear(Paper);
            if (veil == null) return;
            int across = Across(size.Width);
            if (across > 0)
            {
                pen.SetClip(new Rectangle(0, 0, across, size.Height));
                pen.ExcludeClip(title);
                pen.DrawImageUnscaled(veil, 0, 0);
                pen.ResetClip();
            }
            int length = docs[current].Name.Length, typed = TitleTyped();
            if (typed <= 0) return;
            pen.SetClip(new Rectangle(title.X, title.Y, typed >= length ? title.Width : titleWide * typed / length, title.Height));
            pen.DrawImageUnscaled(veil, 0, 0);
            pen.ResetClip();
        }

        // The panel and its page as one picture, with the page at its real strength over the sheet, for her smoke test's previews.
        internal Bitmap Picture()
        {
            bool dropped = surface == null;
            if (dropped) DrawFrame();
            Bitmap whole = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
            using (Graphics pen = Graphics.FromImage(whole))
            {
                pen.CompositingMode = CompositingMode.SourceCopy;
                pen.DrawImageUnscaled(surface.Image, 0, 0);
                pen.CompositingMode = CompositingMode.SourceOver;
                // The page as it is on screen: its words, tabs, buttons and caret, with the sheet showing through between them.
                if (pageUp) using (Bitmap top = page.Face()) pen.DrawImage(top, pageBox, 0, 0, top.Width, top.Height, GraphicsUnit.Pixel);
            }
            if (dropped) DropSurface();
            return whole;
        }

        // Where a press means the panel: its art, not the shadow round it.
        private bool OnArt(Point point)
        {
            float x = point.X / a + PanelArt.Left, y = point.Y / a + PanelArt.Top;
            return x >= PanelArt.SpineLeft && x <= PanelArt.SheetRight && y >= PanelArt.Crown && y <= PanelArt.Foot || x >= PanelArt.RuleLeft && x <= PanelArt.RuleRight && y >= 66 && y <= PanelArt.BlockBottom;
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) Pressed(); base.OnMouseDown(e); }
        protected override void OnMouseMove(MouseEventArgs e) { Dragged(); base.OnMouseMove(e); }
        protected override void OnMouseUp(MouseEventArgs e) { Released(); base.OnMouseUp(e); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (stage == Stage.Gone || stage == Stage.Waiting) return;
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Ask(); return; }
            KeepDrafts();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == Pacer.Message)
            {
                try { Tick(); }
                finally { pacer.Drawn(); }
                Follow();
                return;
            }
            if (message.Msg == 0x0084)
            {
                long value = message.LParam.ToInt64();
                if (!OnArt(PointToClient(new Point((short)(value & 65535), (short)((value >> 16) & 65535))))) { message.Result = new IntPtr(-1); return; }
            }
            if (message.Msg == 0x02E0)
            {
                Rescale((int)(message.WParam.ToInt64() & 65535));
                return;
            }
            base.WndProc(ref message);
        }

        // Dragged onto a screen of another scale: the panel redraws for it, under the mouse.
        private void Rescale(int dpi)
        {
            if (forcedScale > 0 || stage == Stage.Waiting || stage == Stage.Gone) return;
            Measure(dpi, Screen.FromHandle(Handle).WorkingArea);
            Size = PlateSize;
            page.Fit();
            page.Tell(docs[current]);
            if (dragging) { pressedAt = Cursor.Position; startedAt = Location; }
            if (stage != Stage.Open) TakeVeil();
            DrawFrame();
            if (stage == Stage.Open) DropSurface();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                pacer.Stop();
                DropVeil();
                DropFonts();
                DropSurface();
                if (shade != null) shade.Dispose();
                if (grain != null) grain.Dispose();
                if (!page.IsDisposed) page.Dispose();
                if (!frost.IsDisposed) frost.Dispose();
            }
            base.Dispose(disposing);
        }

        [DllImport("user32.dll")] private static extern IntPtr BeginDeferWindowPos(int count);
        [DllImport("user32.dll")] private static extern IntPtr DeferWindowPos(IntPtr moves, IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EndDeferWindowPos(IntPtr moves);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr window, IntPtr region, [MarshalAs(UnmanagedType.Bool)] bool redraw);
        [DllImport("user32.dll")] private static extern int GetWindowRgnBox(IntPtr window, out Edges box);
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCaretPos(out Point point);
        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr window, ref Composition data);
        [DllImport("gdi32.dll")] private static extern IntPtr CreatePolygonRgn(Point[] points, int count, int mode);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(IntPtr thing);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int colour);
        [DllImport("gdi32.dll")] private static extern int SetTextColor(IntPtr dc, int colour);
        [DllImport("gdi32.dll")] private static extern int SetBkColor(IntPtr dc, int colour);
        [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GdiFlush();
        [StructLayout(LayoutKind.Sequential)] private struct Edges { internal int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)] private struct Accent { internal int State, Flags, Tint, Animation; }
        [StructLayout(LayoutKind.Sequential)] private struct Composition { internal int Attribute; internal IntPtr Data; internal int Size; }

        // Windows 10 and 11 blur what's behind a window asked this way. Anywhere that refuses, the frost stays a plain dark window,
        // which reads as well under the dark glass.
        private static bool Frosted(IntPtr window)
        {
            Accent accent = new Accent { State = 3 };
            int size = Marshal.SizeOf(typeof(Accent));
            IntPtr memory = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, memory, false);
                Composition data = new Composition { Attribute = 19, Data = memory, Size = size };
                return SetWindowCompositionAttribute(window, ref data) != 0;
            }
            catch (EntryPointNotFoundException) { return false; }
            finally { Marshal.FreeHGlobal(memory); }
        }

        // Something on the page drew itself differently, so the page is drawn again.
        private static void Touch(Control control)
        {
            Page owner = control.Parent as Page;
            if (owner != null) owner.Freshen();
        }

        private static readonly Dictionary<int, IntPtr> paperBrushes = new Dictionary<int, IntPtr>();

        // A brush in the colour the page is being drawn on, for the text box's background. There are only ever three.
        private static IntPtr PaperBrush()
        {
            IntPtr brush;
            if (!paperBrushes.TryGetValue(Paper.ToArgb(), out brush)) paperBrushes[Paper.ToArgb()] = brush = CreateSolidBrush(ColorTranslator.ToWin32(Paper));
            return brush;
        }

        // One of her files: what's on disk, through her own note code, and what's in the box, with the box's line ends.
        private sealed class Doc
        {
            internal readonly string Label;
            internal readonly Func<WrittenNote> Open;
            internal WrittenNote Note;
            internal string Draft = "", Problem, Told;

            internal Doc(string label, Func<WrittenNote> open) { Label = label; Open = open; }

            internal string Name { get { return Note != null ? Path.GetFileName(Note.Path) : Label; } }
            internal bool Dirty { get { return Note != null && Plain(Draft) != Plain(Note.Text); } }
            internal void Revert() { Draft = Shown(Note.Text); Told = null; }

            private static string Plain(string text) { return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n'); }

            // A text box breaks lines only on \r\n, and her notes are written with \n.
            internal static string Shown(string text) { return Plain(text).Replace("\n", "\r\n"); }

            // Saved with the line ends the file already had.
            internal string ForFile()
            {
                string plain = Plain(Draft);
                return Note.Text.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? plain.Replace("\n", "\r\n") : plain;
            }
        }

        // The window with the words in it.
        private sealed class Page : Form
        {
            private readonly FileEditor panel;
            private readonly WordBox box;
            private readonly Bar bar;
            private readonly Ink[] tabs;
            private readonly Ink close, save, reload, keep;
            private readonly Note status;
            private const TextFormatFlags TitleWidth = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            private const TextFormatFlags TitleLine = TitleWidth | TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
            private readonly Veil cover;
            private Font titleFont, buttonFont, statusFont;
            private Rectangle titleBox;
            private int lineTop, lineFoot;
            private Doc shown;
            private bool loading;
            // What's on screen; the page without its caret, kept for the caret's blink; and the page painted on white and on black.
            // All four are kept between redraws, so typing allocates nothing.
            private Native.Surface face, look, light, dark;
            private readonly Timer blinker = new Timer();
            private bool queued, blink = true, blinks;
            private int seenFirst = -1, seenStart = -1, seenLength = -1, revealed = -1, titled = -1;
            private Rectangle seenCaret;
            internal double Strength, SlowestMs, PaintMs;
            internal int Renders;

            internal Page(FileEditor panel)
            {
                this.panel = panel;
                Text = "Capri file editor";
                FormBorderStyle = FormBorderStyle.None;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                ShowInTaskbar = true;
                BackColor = Red;
                ForeColor = Salmon;
                using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true);
                box = new WordBox { Multiline = true, AcceptsReturn = true, WordWrap = true, ScrollBars = ScrollBars.None, MaxLength = int.MaxValue, BorderStyle = BorderStyle.None, BackColor = Red, ForeColor = Salmon, HideSelection = false };
                bar = new Bar(box);
                box.Moved += delegate { bar.Sync(); Check(); };
                box.Stirred += Wake;
                box.Painted += Freshen;
                box.KeyDown += delegate { panel.Finish(); };
                box.MouseDown += delegate { panel.Finish(); };
                box.TextChanged += delegate { if (!loading) Typed(); Freshen(); };
                tabs = new[] { new Ink { Tag = 0 }, new Ink { Tag = 1 } };
                foreach (Ink tab in tabs)
                {
                    Ink press = tab;
                    press.Click += delegate { panel.Turn((int)press.Tag); };
                }
                close = new Ink { Mark = true, AccessibleName = "Close the file editor" };
                close.Click += delegate { panel.Ask(); };
                save = new Ink { Filled = true, Text = "Save" };
                save.Click += delegate { if (panel.Guarding) panel.GuardSave(); else panel.Save(); };
                reload = new Ink { Text = "Reload" };
                reload.Click += delegate { if (panel.Guarding) panel.GuardDiscard(); else panel.Reload(); };
                keep = new Ink { Text = "Keep editing", Visible = false };
                keep.Click += delegate { panel.GuardKeep(); };
                status = new Note { BackColor = Red, ForeColor = Salmon, AutoEllipsis = true, UseMnemonic = false, TextAlign = ContentAlignment.MiddleLeft };
                cover = new Veil(panel, this) { Visible = false };
                Controls.AddRange(new Control[] { cover, close, tabs[0], tabs[1], box, bar, status, keep, reload, save });
                cover.BringToFront();
                int order = 0;
                foreach (Control next in new Control[] { box, save, reload, keep, tabs[0], tabs[1], close }) next.TabIndex = order++;
                foreach (Control grip in new Control[] { this, status })
                {
                    grip.MouseDown += delegate(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) panel.Pressed(); };
                    grip.MouseMove += delegate { panel.Dragged(); };
                    grip.MouseUp += delegate { panel.Released(); };
                }
                Activated += delegate { panel.Under(); panel.Recheck(); Wake(); };
                Deactivate += delegate { Freshen(); };
                int rate = SystemInformation.CaretBlinkTime;
                blinks = rate > 0;
                blinker.Interval = blinks ? rate : 500;
                blinker.Tick += delegate { Blink(); };
            }

            internal string Status { get { return status.Text; } }
            internal string Words { get { return box.Text; } set { box.Text = value; } }
            internal int BoxHeight { get { return box.Height; } }
            internal string BoxFont { get { return box.Font.Name; } }
            internal string[] Labels { get { return new[] { tabs[0].Text, tabs[1].Text }; } }
            internal string Scrolled { get { return bar.Shown; } }
            internal Control TextArea { get { return box; } }
            internal bool Unsure { get { return keep.Visible; } }
            internal bool Covered { get { return cover.Visible; } }

            private void Style()
            {
                float s = panel.s;
                foreach (Font font in new[] { buttonFont, statusFont }) if (font != null) font.Dispose();
                buttonFont = Title(14 * s);
                statusFont = Look.Mono(13 * s, FontStyle.Regular, GraphicsUnit.Pixel);
                box.Font = Look.Mono(BodySize * s, FontStyle.Regular, GraphicsUnit.Pixel);
                status.Font = statusFont;
                foreach (Ink ink in new[] { tabs[0], tabs[1], close, save, reload, keep }) ink.Font = buttonFont;
            }

            internal void Fit()
            {
                Style();
                Rectangle place = panel.pageBox;
                Bounds = new Rectangle(panel.Left + place.X, panel.Top + place.Y, place.Width, place.Height);
                ClientSize = place.Size;
                int pad = Scaled(18), row = Scaled(52), foot = Scaled(48), gap = Scaled(8);
                close.Bounds = new Rectangle(Width - pad - Scaled(28), Scaled(12), Scaled(28), Scaled(28));
                int right = close.Left - Scaled(12);
                tabs[1].Text = panel.docs[1].Label;
                tabs[0].Text = panel.docs[0].Label;
                for (int i = tabs.Length - 1; i >= 0; i--)
                {
                    int wide = TextRenderer.MeasureText(tabs[i].Text + " *", buttonFont).Width + Scaled(24);
                    tabs[i].Bounds = new Rectangle(right - wide, Scaled(12), wide, Scaled(28));
                    right -= wide + gap;
                }
                titleBox = new Rectangle(pad, Scaled(4), Math.Max(Scaled(40), right - pad), row - Scaled(6));
                int top = row, bottom = Height - foot, barWide = Scaled(8);
                lineTop = row - Scaled(6);
                lineFoot = bottom + Scaled(2);
                // Whole lines only, so the last one isn't cut through above the buttons.
                int line = TextRenderer.MeasureText("Ag", box.Font, Size.Empty, TextFormatFlags.NoPadding).Height;
                box.Bounds = new Rectangle(pad, top, Width - pad * 2 - barWide - gap, (bottom - top) / line * line);
                Native.SendMessage(box.Handle, 0x00D3, new IntPtr(3), IntPtr.Zero);
                bar.Line = line;
                bar.Bounds = new Rectangle(Width - pad - barWide, top, barWide, box.Height);
                int buttonHigh = Scaled(30), buttonTop = bottom + (foot - buttonHigh) / 2;
                int at = Width - pad;
                at = PlaceButton(save, at, buttonTop, buttonHigh, gap);
                at = PlaceButton(reload, at, buttonTop, buttonHigh, gap);
                at = PlaceButton(keep, at, buttonTop, buttonHigh, gap);
                status.Bounds = new Rectangle(pad, buttonTop, Math.Max(Scaled(40), at - pad), buttonHigh);
                cover.Bounds = new Rectangle(0, 0, Width, Height);
                bar.Sync();
                Invalidate(true);
                Freshen();
            }

            private int PlaceButton(Ink ink, int right, int top, int high, int gap)
            {
                if (!ink.Visible && ink != keep) return right;
                int wide = TextRenderer.MeasureText(ink.Text, buttonFont).Width + Scaled(26);
                ink.Bounds = new Rectangle(right - wide, top, wide, high);
                return right - wide - gap;
            }

            private void PlaceFoot()
            {
                int pad = Scaled(18), gap = Scaled(8), at = Width - pad;
                at = PlaceButton(save, at, save.Top, save.Height, gap);
                at = PlaceButton(reload, at, save.Top, save.Height, gap);
                if (keep.Visible) at = PlaceButton(keep, at, save.Top, save.Height, gap);
                status.Bounds = new Rectangle(pad, save.Top, Math.Max(Scaled(40), at - pad), save.Height);
            }

            private int Scaled(float value) { return (int)Math.Round(value * panel.s); }

            // Shows this file, reading it first.
            internal void Take(Doc doc)
            {
                panel.Read(doc);
                loading = true;
                box.ReadOnly = doc.Note == null;
                box.Text = doc.Draft;
                box.ClearUndo();
                box.Select(0, 0);
                loading = false;
                shown = doc;
                Text = doc.Name + " - Capri";
                box.AccessibleName = doc.Name;
                Tell(doc);
                if (panel.Resting) box.Focus();
            }

            // The page's title, tabs, buttons and status line, with anything this file has to say.
            internal void Tell(Doc doc)
            {
                FitTitle(doc.Name);
                bar.Sync();
                Invalidate();
                Mark(doc);
                if (panel.Guarding) return;
                if (doc.Problem != null) Say(doc.Problem, true);
                else if (doc.Told != null) Say(doc.Told, false);
            }

            // Capri Mainframe has Latin letters only: a file with any other name gets Windows' own title font. On a small screen a long
            // name comes down in size before it loses its end.
            private void FitTitle(string name)
            {
                bool latin = true;
                foreach (char c in name) if (c > 0x7E) latin = false;
                Font whole = latin ? Title(30 * panel.s) : Neon.Display(26 * panel.s, GraphicsUnit.Pixel);
                int wide = TextRenderer.MeasureText(name, whole, Size.Empty, TitleWidth).Width;
                if (titleFont != null) titleFont.Dispose();
                titleFont = whole;
                if (wide <= titleBox.Width) return;
                titleFont = new Font(whole.FontFamily, Math.Max(whole.Size * 0.6f, whole.Size * 0.96f * titleBox.Width / wide), whole.Style, GraphicsUnit.Pixel);
                whole.Dispose();
            }

            // The tabs, buttons and status line for this file as it stands. A star marks a file with unsaved changes.
            internal void Mark(Doc doc)
            {
                for (int i = 0; i < tabs.Length; i++)
                {
                    Doc each = panel.docs[i];
                    tabs[i].Text = each.Label + (each.Dirty ? " *" : "");
                    tabs[i].Filled = each == doc;
                    tabs[i].AccessibleName = "Show " + each.Label + (each.Dirty ? ", with unsaved changes" : "");
                    tabs[i].Invalidate();
                }
                if (panel.Guarding) return;
                save.Enabled = doc.Note != null;
                reload.Text = doc.Dirty ? "Discard + reload" : "Reload";
                status.ForeColor = Salmon;
                status.Text = doc.Dirty ? "Unsaved changes." : "";
                PlaceFoot();
            }

            internal void Say(string words, bool wrong)
            {
                status.ForeColor = Salmon;
                status.Text = wrong ? "! " + words : words;
            }

            private void Typed()
            {
                panel.Finish();
                if (shown == null) return;
                shown.Draft = box.Text;
                shown.Told = null;
                Mark(shown);
            }

            // What's in the box goes to its file's draft.
            internal void Keep()
            {
                if (shown != null && !box.ReadOnly) shown.Draft = box.Text;
            }

            internal void Guard(string names)
            {
                save.Text = "Save + close";
                reload.Text = "Discard";
                keep.Visible = true;
                PlaceFoot();
                Say("Unsaved changes in " + names + ".", false);
                save.Focus();
            }

            internal void Unguard()
            {
                save.Text = "Save";
                keep.Visible = false;
                box.Focus();
            }

            // Leaving: nothing here takes clicks or keys any more.
            internal void Quiet()
            {
                save.Text = "Save";
                keep.Visible = false;
                if (shown != null) Mark(shown);
                PlaceFoot();
                status.Text = "";
            }

            internal void Veiled(bool on)
            {
                bool had = cover.Visible;
                cover.Visible = on;
                if (on && !had) cover.BringToFront();
                Render();
            }

            // A frame of the words typing in or taking themselves back: drawn straight away, in step with the panel, but only when it
            // shows more or less of them than the last did. Until the title starts typing there's nothing to show.
            internal void Reveal()
            {
                if (!cover.Visible) return;
                if (look != null && revealed == panel.Across(Width) && titled == panel.TitleTyped()) return;
                Render();
            }

            internal void Settle()
            {
                box.Focus();
                bar.Sync();
                Wake();
            }

            // A window Windows sees through pixel by pixel: the page draws itself onto it in Render.
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x00080000;
                    return value;
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Paper); }

            protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Freshen(); }

            // Something on the page changed: it's drawn again once the messages already waiting are done, however many asked.
            internal void Freshen()
            {
                if (queued || !IsHandleCreated || IsDisposed) return;
                queued = true;
                BeginInvoke(new Action(Render));
            }

            // A key or a click: the caret shows straight away, then blinks.
            private void Wake()
            {
                blink = true;
                if (blinks) { blinker.Stop(); blinker.Start(); }
                Check();
            }

            private void Blink()
            {
                blink = !blink;
                if (look != null) Compose();
            }

            // A scroll or a selection changes what the text box draws, so the page is drawn again. A caret that only moved is just put
            // back on top.
            private void Check()
            {
                if (!box.IsHandleCreated) return;
                int first = Native.SendMessage(box.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32(), start = box.SelectionStart, length = box.SelectionLength;
                bool redraw = first != seenFirst || length != seenLength || length > 0 && start != seenStart;
                seenFirst = first;
                seenStart = start;
                seenLength = length;
                if (redraw) Freshen();
                else if (look != null && Caret != seenCaret) Compose();
            }

            // What's on screen: the page's words, tabs and buttons with nothing behind them, then its caret.
            internal void Render()
            {
                queued = false;
                if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
                Stopwatch timed = Stopwatch.StartNew();
                Fit(ref look);
                if (cover.Visible) VeilFrame();
                else Matte(look);
                Compose();
                Renders++;
                SlowestMs = Math.Max(SlowestMs, timed.Elapsed.TotalMilliseconds);
            }

            private void Compose()
            {
                if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0) return;
                Fit(ref face);
                seenCaret = Caret;
                bool ready = Fits(look), caret = blink && !seenCaret.IsEmpty;
                if (ready) Copy(look, face);
                if (!ready || caret)
                    using (Graphics pen = Graphics.FromImage(face.Image))
                    {
                        if (!ready) pen.Clear(Color.FromArgb(1, 0, 0, 0));
                        if (caret) using (Brush ink = new SolidBrush(Salmon)) pen.FillRectangle(ink, seenCaret);
                    }
                face.Show(Handle, Location, (byte)Math.Round(Math.Max(0, Math.Min(1, Strength)) * 255));
            }

            private bool Fits(Native.Surface surface) { return surface != null && surface.Image.Width == Width && surface.Image.Height == Height; }

            // A surface the page's size, made again only when that changes.
            private void Fit(ref Native.Surface surface)
            {
                if (Fits(surface)) return;
                if (surface != null) surface.Dispose();
                surface = new Native.Surface(Width, Height);
            }

            private static unsafe void Copy(Native.Surface from, Native.Surface into)
            {
                long bytes = (long)from.Image.Width * from.Image.Height * 4;
                Buffer.MemoryCopy((void*)from.Pixels, (void*)into.Pixels, bytes, bytes);
            }

            // Up at full strength, or gone.
            internal void Lift(double strength)
            {
                Strength = strength;
                if (look == null) Render();
                else Compose();
            }

            // Where the text box has its caret, in the page's pixels: only while it has the keyboard, nothing is selected and the words
            // aren't still typing in.
            internal Rectangle Caret
            {
                get
                {
                    Point at;
                    if (cover.Visible || !box.IsHandleCreated || !box.Focused || box.SelectionLength > 0 || !GetCaretPos(out at)) return Rectangle.Empty;
                    return new Rectangle(box.Left + at.X, box.Top + at.Y, Math.Max(1, Scaled(2)), bar.Line);
                }
            }

            // The page as it is on screen, for her smoke test's pictures.
            internal Bitmap Face()
            {
                if (queued || look == null || face == null) Render();
                Bitmap copy = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
                if (face != null) using (Graphics pen = Graphics.FromImage(copy)) pen.DrawImageUnscaled(face.Image, 0, 0);
                return copy;
            }

            // A frame of the words typing in or taking themselves back, with nothing behind them.
            private void VeilFrame()
            {
                using (Graphics pen = Graphics.FromImage(look.Image))
                {
                    pen.Clear(Color.FromArgb(1, 0, 0, 0));
                    PaintCover(pen, false);
                }
                revealed = panel.Across(Width);
                titled = panel.TitleTyped();
            }

            // The page with nothing behind it, so the sheet shows through everywhere but the words, tabs and buttons. It's painted once
            // on white and once on black: how far each pixel moves between the two says how see-through it is, and the one on black is
            // already its colour times that, which is how the window takes it. Nothing is ever quite clear (an alpha of 1, too faint to
            // see), so a click between the words still lands on the page.
            private void Matte(Native.Surface into)
            {
                Stopwatch timed = Stopwatch.StartNew();
                Fit(ref light);
                Fit(ref dark);
                Print(light, Color.White);
                Print(dark, Color.Black);
                GdiFlush();
                PaintMs = timed.Elapsed.TotalMilliseconds;
                if (!Fits(into)) return;
                long count = (long)Width * Height * 4;
                unsafe
                {
                    byte* white = (byte*)light.Pixels, black = (byte*)dark.Pixels, matted = (byte*)into.Pixels;
                    for (long i = 0; i < count; i += 4)
                    {
                        int alpha = 255 - (white[i] - black[i] + white[i + 1] - black[i + 1] + white[i + 2] - black[i + 2] + 1) / 3;
                        if (alpha < 1) alpha = 1;
                        else if (alpha > 255) alpha = 255;
                        int blue = black[i], green = black[i + 1], red = black[i + 2];
                        matted[i] = (byte)(blue < alpha ? blue : alpha);
                        matted[i + 1] = (byte)(green < alpha ? green : alpha);
                        matted[i + 2] = (byte)(red < alpha ? red : alpha);
                        matted[i + 3] = (byte)alpha;
                    }
                }
            }

            // The page with nothing behind it as a picture of its own, without its cover: the words that type in under the cover.
            internal Bitmap Matted()
            {
                bool covered = cover.Visible;
                using (Native.Surface matted = new Native.Surface(Width, Height))
                {
                    if (covered) cover.Visible = false;
                    try { Matte(matted); }
                    finally { if (covered) cover.Visible = true; }
                    Bitmap picture = new Bitmap(Width, Height, PixelFormat.Format32bppPArgb);
                    using (Graphics pen = Graphics.FromImage(picture))
                    {
                        pen.CompositingMode = CompositingMode.SourceCopy;
                        pen.DrawImageUnscaled(matted.Image, 0, 0);
                    }
                    return picture;
                }
            }

            // The page painted on one colour, straight onto a surface: what DrawToBitmap does, with nothing in between. Everything on
            // the page draws its background in Paper, so that one colour is all that changes, and nothing is left needing a repaint.
            private void Print(Native.Surface onto, Color paper)
            {
                Paper = paper;
                try { Native.SendMessage(Handle, 0x0317, onto.Dc, (IntPtr)0x1E); }
                finally { Paper = Red; }
            }

            internal void PaintCover(Graphics pen, bool clear)
            {
                int wide = shown == null ? titleBox.Width : Math.Min(titleBox.Width, TextRenderer.MeasureText(shown.Name, titleFont, Size.Empty, TitleWidth).Width);
                panel.PaintVeil(pen, Size, titleBox, wide, clear);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                if (shown != null) TextRenderer.DrawText(e.Graphics, shown.Name, titleFont, titleBox, Salmon, TitleLine);
                // Thin red rules under the title and over the buttons, like the lines between the reference panel's fields.
                using (Brush rule = new SolidBrush(Color.FromArgb(200, Red)))
                {
                    int thick = Math.Max(1, Scaled(1.5f));
                    e.Graphics.FillRectangle(rule, Scaled(6), lineTop, Width - Scaled(6), thick);
                    e.Graphics.FillRectangle(rule, Scaled(6), lineFoot, Width - Scaled(6), thick);
                }
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keys)
            {
                if (keys == (Keys.Control | Keys.S)) { if (panel.Guarding) panel.GuardSave(); else panel.Save(); return true; }
                if (keys == Keys.Escape) { if (panel.Guarding) panel.GuardKeep(); else panel.Ask(); return true; }
                if (keys == (Keys.Control | Keys.Tab) || keys == (Keys.Control | Keys.Shift | Keys.Tab)) { panel.Next(); return true; }
                if (keys == (Keys.Control | Keys.A) && box.Focused) { box.SelectAll(); return true; }
                return base.ProcessCmdKey(ref message, keys);
            }

            protected override void WndProc(ref Message message)
            {
                // The text box's colours come from here: her words, on whatever the page is being drawn on.
                if ((message.Msg == 0x0133 || message.Msg == 0x0138) && box != null && box.IsHandleCreated && message.LParam == box.Handle)
                {
                    SetTextColor(message.WParam, ColorTranslator.ToWin32(box.ForeColor));
                    SetBkColor(message.WParam, ColorTranslator.ToWin32(Paper));
                    message.Result = PaperBrush();
                    return;
                }
                if (message.Msg == 0x02E0) return;
                base.WndProc(ref message);
                if (message.Msg == 0x000F) Freshen();
            }

            protected override void OnFormClosing(FormClosingEventArgs e)
            {
                base.OnFormClosing(e);
                if (e.CloseReason == CloseReason.UserClosing && !panel.Over) { e.Cancel = true; panel.Ask(); }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    foreach (Font font in new[] { titleFont, buttonFont, statusFont }) if (font != null) font.Dispose();
                    blinker.Dispose();
                    if (face != null) face.Dispose();
                    foreach (Native.Surface surface in new[] { look, light, dark }) if (surface != null) surface.Dispose();
                }
                base.Dispose(disposing);
            }
        }

        // The text box, which says when it may have scrolled, when a key or a click may have moved its caret, and when it has drawn.
        private sealed class WordBox : TextBox
        {
            internal event Action Moved, Stirred, Painted;

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x020A)
                {
                    int notches = (short)((message.WParam.ToInt64() >> 16) & 65535), lines = SystemInformation.MouseWheelScrollLines;
                    Native.SendMessage(Handle, 0x00B6, IntPtr.Zero, new IntPtr(-notches * Math.Max(1, lines) / 120));
                    if (Moved != null) Moved();
                    return;
                }
                base.WndProc(ref message);
                switch (message.Msg)
                {
                    case 0x0005: case 0x000C: case 0x0101: case 0x0102: case 0x0200: case 0x0202:
                        if (Moved != null) Moved();
                        break;
                }
                switch (message.Msg)
                {
                    case 0x0007: case 0x0008: case 0x0100: case 0x0102: case 0x0201: case 0x0202:
                        if (Stirred != null) Stirred();
                        break;
                    case 0x000F:
                        if (Painted != null) Painted();
                        break;
                }
            }
        }

        // A thin black scrollbar, since Windows' own has no place on her red.
        private sealed class Bar : Control
        {
            private readonly WordBox box;
            internal int Line = 1;
            private int first, seen, lines;
            private bool holding;
            private int heldAt, heldFirst;

            internal Bar(WordBox box)
            {
                this.box = box;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Red;
                TabStop = false;
            }

            protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Touch(this); }

            internal void Sync()
            {
                if (!box.IsHandleCreated) return;
                int top = Native.SendMessage(box.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32(), count = Native.SendMessage(box.Handle, 0x00BA, IntPtr.Zero, IntPtr.Zero).ToInt32();
                int fits = Math.Max(1, box.ClientSize.Height / Line);
                if (top == first && fits == seen && count == lines) return;
                first = top;
                seen = fits;
                lines = count;
                Invalidate();
            }

            internal string Shown { get { return first + " of " + lines + ", " + seen + " seen"; } }

            private Rectangle Thumb
            {
                get
                {
                    int high = Math.Max(Width * 3, Height * seen / Math.Max(1, lines)), room = Height - high;
                    int most = Math.Max(1, lines - seen);
                    return new Rectangle(0, room * Math.Min(first, most) / most, Width, high);
                }
            }

            private void Scroll(int to)
            {
                Native.SendMessage(box.Handle, 0x00B6, IntPtr.Zero, new IntPtr(to - first));
                Sync();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (lines <= seen) return;
                Rectangle thumb = Thumb;
                if (thumb.Contains(e.Location)) { holding = true; heldAt = e.Y; heldFirst = first; return; }
                Scroll(first + (e.Y < thumb.Top ? -seen : seen));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                if (!holding) return;
                int room = Math.Max(1, Height - Thumb.Height);
                Scroll(heldFirst + (e.Y - heldAt) * Math.Max(1, lines - seen) / room);
            }

            protected override void OnMouseUp(MouseEventArgs e) { holding = false; base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Paper);
                if (lines <= seen) return;
                using (Brush track = new SolidBrush(Color.FromArgb(90, Red))) e.Graphics.FillRectangle(track, (Width - Math.Max(1, Width / 4)) / 2, 0, Math.Max(1, Width / 4), Height);
                using (Brush ink = new SolidBrush(Red)) e.Graphics.FillRectangle(ink, Thumb);
            }
        }

        // The cover over the page while its words type in or take themselves back: a picture of the page, shown a piece at a time.
        private sealed class Veil : Control
        {
            private readonly FileEditor panel;
            private readonly Page page;

            internal Veil(FileEditor panel, Page page)
            {
                this.panel = panel;
                this.page = page;
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                TabStop = false;
            }

            protected override void OnPaint(PaintEventArgs e) { page.PaintCover(e.Graphics, true); }
            protected override void OnMouseDown(MouseEventArgs e) { panel.Finish(); base.OnMouseDown(e); }
        }

        // The status line, drawn on whatever the page is being drawn on.
        private sealed class Note : Label
        {
            protected override void OnPaintBackground(PaintEventArgs e) { e.Graphics.Clear(Paper); }
            protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Touch(this); }
        }

        // Windows' own blur under the sheet, so what's behind the panel goes soft, like the room behind the Arasaka panel in Lachy's
        // reference. It's a window of its own, the shape of the sheet, straight under the panel, and clicks go through it.
        private sealed class Frost : Form
        {
            internal bool Blurred;
            // Where its window sits in the panel's.
            internal Point Offset;
            // The outline it was last given, and where the panel was, so an outline that hasn't changed isn't given again.
            private Point[] shaped;
            private Point shapedAt;

            internal Frost()
            {
                Text = "Capri file editor blur";
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Black;
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            // Off Alt+Tab and never active.
            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x00000080 | 0x08000000;
                    return value;
                }
            }

            // No shape until the panel draws its first frame, so it never shows as a box.
            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                SetWindowRgn(Handle, CreateRectRgn(0, 0, 0, 0), false);
                Blurred = Frosted(Handle);
            }

            // The sheet's outline, given in the panel's pixels, or nothing. The window covers only the sheet's own box, so a Windows
            // that ignored the outline would still blur nothing beyond the sheet's cut corners.
            internal void Shape(PointF[] sheet, Point panelAt)
            {
                if (!IsHandleCreated) return;
                if (sheet == null || sheet.Length < 3)
                {
                    if (shaped != null && shaped.Length == 0) return;
                    shaped = new Point[0];
                    IntPtr none = CreateRectRgn(0, 0, 0, 0);
                    if (none != IntPtr.Zero && SetWindowRgn(Handle, none, true) == 0) DeleteObject(none);
                    return;
                }
                Point[] corners = new Point[sheet.Length];
                int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
                for (int i = 0; i < sheet.Length; i++)
                {
                    corners[i] = Point.Round(sheet[i]);
                    left = Math.Min(left, corners[i].X);
                    top = Math.Min(top, corners[i].Y);
                    right = Math.Max(right, corners[i].X);
                    bottom = Math.Max(bottom, corners[i].Y);
                }
                for (int i = 0; i < corners.Length; i++) corners[i] = new Point(corners[i].X - left, corners[i].Y - top);
                if (Same(corners, shaped) && shapedAt == panelAt && Offset == new Point(left, top)) return;
                shaped = corners;
                shapedAt = panelAt;
                Offset = new Point(left, top);
                SetWindowPos(Handle, IntPtr.Zero, panelAt.X + left, panelAt.Y + top, Math.Max(1, right - left), Math.Max(1, bottom - top), 0x0014);
                IntPtr region = CreatePolygonRgn(corners, corners.Length, 2);
                if (region != IntPtr.Zero && SetWindowRgn(Handle, region, true) == 0) DeleteObject(region);
            }

            private static bool Same(Point[] one, Point[] other)
            {
                if (one == null || other == null || one.Length != other.Length) return false;
                for (int i = 0; i < one.Length; i++) if (one[i] != other[i]) return false;
                return true;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
                base.WndProc(ref message);
            }
        }

        // The animation's beat (8 Nov). A thread of its own wakes in step with the screen and asks the panel for a frame: at every
        // refresh on a screen up to 99 Hz and at every few on a faster one, so 50 to 99 frames a second on any screen of 50 Hz or
        // more, and never more often than the screen shows them. While the last frame is still being drawn it asks for nothing, so
        // a slow PC skips frames instead of falling behind. WinForms' own timer only ticks on Windows' 15.6 ms steps and wanders by
        // as much again.
        private sealed class Pacer
        {
            internal const int Message = 0x8000 + 0x5C;
            private object token;
            private int asked;
            internal double Rate, Hz;
            internal bool InStep;

            internal bool Running { get { return System.Threading.Volatile.Read(ref token) != null; } }

            internal void Start(IntPtr window, string device)
            {
                Hz = Refresh(device);
                int every = Math.Max(1, (int)(Hz / 50));
                Rate = Hz / every;
                long frequency = Stopwatch.Frequency, period = (long)(frequency / Rate), next = Stopwatch.GetTimestamp() + period;
                InStep = false;
                // Windows' compositor says when the screen last refreshed and how often: frames then start just after a refresh,
                // with the whole of it to be drawn in, so none lands right on the moment a refresh is put together.
                Timing timing = new Timing { Size = Marshal.SizeOf(typeof(Timing)) };
                try
                {
                    if (Stopwatch.IsHighResolution && DwmGetCompositionTimingInfo(IntPtr.Zero, ref timing) == 0 && timing.RefreshPeriod > 0 && timing.RefreshDen > 0)
                    {
                        double composed = (double)timing.RefreshNum / timing.RefreshDen;
                        long refresh = (long)timing.RefreshPeriod, now = Stopwatch.GetTimestamp();
                        if (Math.Abs(composed - Hz) <= Hz * 0.02 && refresh > frequency / 1000)
                        {
                            period = refresh * every;
                            Rate = (double)frequency / period;
                            long since = (now - (long)timing.VBlank) % refresh;
                            if (since < 0) since += refresh;
                            next = now - since + refresh + Math.Min(frequency / 1000, refresh / 4);
                            InStep = true;
                        }
                    }
                }
                catch (DllNotFoundException) { }
                catch (EntryPointNotFoundException) { }
                object mine = new object();
                System.Threading.Volatile.Write(ref token, mine);
                System.Threading.Interlocked.Exchange(ref asked, 0);
                System.Threading.Thread beat = new System.Threading.Thread(delegate() { Beat(mine, window, period, next); });
                beat.IsBackground = true;
                beat.Name = "File editor frames";
                beat.Start();
            }

            // The thread ends at its next wake.
            internal void Stop() { System.Threading.Volatile.Write(ref token, null); }

            // The frame it asked for is on screen: it may ask for the next.
            internal void Drawn() { System.Threading.Interlocked.Exchange(ref asked, 0); }

            private void Beat(object mine, IntPtr window, long period, long next)
            {
                // A high-resolution waitable timer wakes on time without changing Windows' timer for everything else. Older
                // Windows 10 has none, so this falls back to Windows' 1 ms timer while the animation plays.
                IntPtr timer = IntPtr.Zero;
                bool coarse = false;
                try { timer = CreateWaitableTimerExW(IntPtr.Zero, null, 2, 0x1F0003); }
                catch (EntryPointNotFoundException) { }
                if (timer == IntPtr.Zero) coarse = timeBeginPeriod(1) == 0;
                try
                {
                    while (System.Threading.Volatile.Read(ref token) == mine)
                    {
                        long wait = next - Stopwatch.GetTimestamp();
                        if (wait > 0)
                        {
                            long due = -Math.Max(1, wait * 10000000 / Stopwatch.Frequency);
                            if (timer != IntPtr.Zero && SetWaitableTimer(timer, ref due, 0, IntPtr.Zero, IntPtr.Zero, false)) WaitForSingleObject(timer, 1000);
                            else System.Threading.Thread.Sleep((int)Math.Max(1, wait * 1000 / Stopwatch.Frequency));
                        }
                        if (System.Threading.Volatile.Read(ref token) != mine) break;
                        // A window that's gone ends it.
                        if (System.Threading.Interlocked.CompareExchange(ref asked, 1, 0) == 0 && !PostMessage(window, Message, IntPtr.Zero, IntPtr.Zero)) break;
                        // A slot slept through is skipped, never made up, so frames stay in step with the screen.
                        next += period;
                        long late = Stopwatch.GetTimestamp() - next;
                        if (late >= 0) next += (late / period + 1) * period;
                    }
                }
                finally
                {
                    if (timer != IntPtr.Zero) CloseHandle(timer);
                    if (coarse) timeEndPeriod(1);
                }
            }

            // How often the screen refreshes, from Windows' display settings: 60 if it won't say.
            internal static double Refresh(string device)
            {
                Mode mode = new Mode { Size = (short)Marshal.SizeOf(typeof(Mode)) };
                return EnumDisplaySettings(device, -1, ref mode) && mode.Frequency > 1 ? mode.Frequency : 60;
            }

            [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumDisplaySettings(string device, int which, ref Mode mode);
            [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr window, int message, IntPtr wide, IntPtr low);
            [DllImport("dwmapi.dll")] private static extern int DwmGetCompositionTimingInfo(IntPtr window, ref Timing timing);
            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string name, uint flags, uint access);
            [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetWaitableTimer(IntPtr timer, ref long due, int period, IntPtr routine, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
            [DllImport("kernel32.dll")] private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
            [DllImport("kernel32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool CloseHandle(IntPtr handle);
            [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint milliseconds);
            [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint milliseconds);

#pragma warning disable 649
            // DEVMODEW, as far as its refresh rate.
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct Mode
            {
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Device;
                internal short SpecVersion, DriverVersion, Size, DriverExtra;
                internal int Fields, X, Y, Orientation, FixedOutput;
                internal short Color, Duplex, YResolution, TrueType, Collate;
                [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] internal string Form;
                internal short LogPixels;
                internal int Bits, Wide, High, Flags, Frequency, IcmMethod, IcmIntent, Media, Dither, Reserved1, Reserved2, PanningWide, PanningHigh;
            }

            // DWM_TIMING_INFO: packed, and read only as far as the refresh, but the whole of it is asked for.
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct Timing
            {
                internal int Size;
                internal uint RefreshNum, RefreshDen;
                internal ulong RefreshPeriod;
                internal uint ComposeNum, ComposeDen;
                internal ulong VBlank, Refreshes;
                internal uint DxRefresh;
                internal ulong Composed, Frame;
                internal uint DxPresent;
                internal ulong RefreshFrame, FrameSubmitted;
                internal uint DxPresentSubmitted;
                internal ulong FrameConfirmed;
                internal uint DxPresentConfirmed;
                internal ulong RefreshConfirmed;
                internal uint DxRefreshConfirmed;
                internal ulong FramesLate;
                internal uint FramesOutstanding;
                internal ulong FrameDisplayed, QpcFrameDisplayed, RefreshFrameDisplayed, FrameComplete, QpcFrameComplete, FramePending, QpcFramePending;
                internal ulong FramesDisplayed, FramesComplete, FramesPending, FramesAvailable, FramesDropped, FramesMissed;
                internal ulong RefreshNextDisplayed, RefreshNextPresented, RefreshesDisplayed, RefreshesPresented, RefreshStarted;
                internal ulong PixelsReceived, PixelsDrawn, BuffersEmpty;
            }
#pragma warning restore 649
        }

        // A button in the panel's colours. Filled is her red with dark words; the others are a red line with red words.
        private sealed class Ink : Button
        {
            internal bool Filled, Mark;
            private bool hot, down;

            internal Ink()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
                BackColor = Red;
                UseMnemonic = false;
                Cursor = Cursors.Hand;
            }

            protected override void OnInvalidated(InvalidateEventArgs e) { base.OnInvalidated(e); Touch(this); }
            protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }
            protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics pen = e.Graphics;
                pen.Clear(Paper);
                pen.SmoothingMode = SmoothingMode.AntiAlias;
                pen.PixelOffsetMode = PixelOffsetMode.Half;
                float line = Math.Max(1f, (float)Math.Round(Height / 26f));
                RectangleF area = new RectangleF(line / 2, line / 2, Width - line, Height - line);
                float cut = Height / 4f;
                bool lit = Filled ? !(hot || down) : hot || down;
                if (Mark)
                {
                    if (hot || down) using (Brush fill = new SolidBrush(Red)) pen.FillRectangle(fill, 0, 0, Width, Height);
                    using (Pen cross = new Pen(hot || down ? Dark : Red, 2 * line))
                    {
                        float x = Width / 2f, y = Height / 2f, arm = Height / 5f;
                        pen.DrawLine(cross, x - arm, y - arm, x + arm, y + arm);
                        pen.DrawLine(cross, x - arm, y + arm, x + arm, y - arm);
                    }
                }
                else
                {
                    using (GraphicsPath plate = Neon.Cut(area, 0, 0, cut, 0))
                    {
                        if (lit) using (Brush fill = new SolidBrush(Red)) pen.FillPath(fill, plate);
                        using (Pen edge = new Pen(Enabled ? Red : Color.FromArgb(110, Red), line)) pen.DrawPath(edge, plate);
                    }
                    TextRenderer.DrawText(pen, Text, Font, ClientRectangle, !Enabled ? Color.FromArgb(130, 50, 46) : lit ? Dark : Red, BackColorFor(lit), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
                }
                if (Focused && ShowFocusCues)
                    using (Pen dots = new Pen(lit ? Dark : Red, line) { DashStyle = DashStyle.Dot })
                        pen.DrawRectangle(dots, 3 * line, 3 * line, Width - 6 * line - 1, Height - 6 * line - 1);
            }

            private static Color BackColorFor(bool lit) { return lit ? Red : Paper; }
        }
    }

    // Her artist's panel, measured off the art's 768 x 768 frames: where everything sits at rest and, frame by frame at 24 fps, how far each
    // part has got. The rest is scaled from these, so the panel moves as her artist animated it at any size.
    internal static class PanelArt
    {
        // The window's edges in the art: a margin for the shadow round the art.
        internal const float Left = 109, Top = 42, Wide = 565, High = 535;
        internal const float SpineLeft = 133, SpineRight = 170, Crown = 164, Foot = 553, TickY = 353;
        internal const float SheetLeft = 172, SheetRight = 650, SheetFoot = 552.5f, StepIn = 13.5f, StepTop = 378.5f;
        internal const float SheetWaist = SheetRight - StepIn;
        internal const float RuleLeft = 137, RuleRight = 434, RuleTop = 110, RuleBottom = 114, BlockTop = 123, BlockBottom = 135, BlockEnd = 172;
        internal const double RestAlpha = 81.7 / 255;
        // Where the sheet settles instead: dark glass like the Arasaka panel in Lachy's reference (8 Nov), still on her artist's
        // curve. Its colour is what that panel's sheet measures over a dark room.
        internal const double GlassAlpha = 0.78;
        internal static readonly Color Glass = Color.FromArgb(52, 30, 36);
        // The spine's foot, from frame 1.
        internal static readonly float[] SpineEnds = { 168, 182, 204, 231, 264, 300, 338, 377, 416, 452, 484, 512, 534, 548, 553 };
        // The sheet's right edge, from frame 14.
        internal static readonly float[] SheetEnds = { 172, 176, 188, 202, 221, 243, 270, 298, 329, 362, 395, 429, 462, 495, 526, 555, 582, 605, 623, 638, 647, 650 };
        // How far the sheet's step cuts in, from frame 16. Its chamfer deepens with it.
        internal static readonly float[] StepDepths = { 1, 1, 2, 2, 3, 3, 4, 5, 6, 7, 8, 9, 9, 10, 12, 12, 12, 13, 13, 13.5f };
        // The sheet's alpha out of 255, from frame 17.
        internal static readonly float[] SheetAlphas = { 253.2f, 252.4f, 250.7f, 247.4f, 242.7f, 236.2f, 227.7f, 217.3f, 205, 191.1f, 175.7f, 159.7f, 143.5f, 127.7f, 113, 100.3f, 90.4f, 84, 81.7f };
        // The rule's right end, from frame 21.
        internal static readonly float[] RuleEnds = { 139, 145, 154, 167, 183, 200, 220, 241, 263, 285, 307, 329, 349, 369, 387, 402, 415, 425, 431, 434 };
        // The block's right end, from frame 25.
        internal static readonly float[] BlockEnds = { 140, 146, 154, 162, 169, 172 };
        internal static readonly Color Spine = Color.FromArgb(243, 222, 70, 62);
        internal static readonly Color Rule = Color.FromArgb(222, FileEditor.Red);

        // A value at any moment between the art's frames: a smooth curve through every frame's measurement (8 Nov), so a screen
        // drawing far more often than 24 times a second sees the art's easing, not a straight line from one frame to the next with
        // a jolt at each. It never overshoots a measurement (a monotone cubic, after Fritsch and Carlson), and it eases out of
        // `lead`, where it was the frame before `first`, and into its last measurement, which it holds.
        internal static float Along(float[] curve, int first, double frame, float lead)
        {
            double at = frame - first;
            int last = curve.Length - 1;
            if (at <= -1) return lead;
            if (at >= last) return curve[last];
            int low = (int)Math.Floor(at);
            double t = at - low;
            double from = (2 * t - 3) * t * t + 1, leaving = ((t - 2) * t + 1) * t, to = (3 - 2 * t) * t * t, arriving = (t - 1) * t * t;
            return (float)(from * Sample(curve, lead, low) + leaving * Slope(curve, lead, low) + to * Sample(curve, lead, low + 1) + arriving * Slope(curve, lead, low + 1));
        }

        // For a curve that holds its first value before it starts.
        internal static float Along(float[] curve, int first, double frame) { return Along(curve, first, frame, curve[0]); }

        private static double Sample(float[] curve, float lead, int at) { return at < 0 ? lead : curve[Math.Min(at, curve.Length - 1)]; }

        // How fast the curve moves through a frame: not at all where it holds or turns back, and otherwise the harmonic mean of
        // the steps either side, which keeps it from overshooting.
        private static double Slope(float[] curve, float lead, int at)
        {
            if (at < 0) return 0;
            double before = Sample(curve, lead, at) - Sample(curve, lead, at - 1), after = Sample(curve, lead, at + 1) - Sample(curve, lead, at);
            return before * after <= 0 ? 0 : 2 / (1 / before + 1 / after);
        }

        // 0 up to one frame, 1 from another, for a detail that comes in between them.
        internal static float Grown(double frame, int from, int to) { return (float)Math.Max(0, Math.Min(1, (frame - from) / (to - from))); }

        // The sheet's alpha partway down her artist's curve, landing on the glass's alpha instead of the art's.
        internal static double Glassy(double art)
        {
            double top = SheetAlphas[0] / 255;
            return GlassAlpha + (art - RestAlpha) * (top - GlassAlpha) / (top - RestAlpha);
        }

        // The sheet's colour at its alpha: her red when solid, turning to the dark glass as it settles.
        internal static Color SheetFill(double alpha)
        {
            double t = Math.Max(0, Math.Min(1, (1 - alpha) / (1 - GlassAlpha)));
            return Color.FromArgb((int)(alpha * 255), (int)(235 + t * (Glass.R - 235)), (int)(74 + t * (Glass.G - 74)), (int)(66 + t * (Glass.B - 66)));
        }
    }
}
