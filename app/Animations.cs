using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class AnimationDraft : IDisposable
    {
        internal sealed class Strip
        {
            internal string Title;
            internal bool Left;
            internal int OwnCount, OwnFps, Wide, High, Fps;
            internal bool Fixed;
            internal List<Bitmap> Frames;
            internal bool Changed, Retimed;
            internal string From;

            internal string File { get { return Title + (Left ? "_flipped" : "") + ".png"; } }
            internal string Shown { get { return Title + (Left ? " (facing left)" : ""); } }
            internal int Count { get { return Frames == null ? OwnCount : Frames.Count; } }
            internal int Most { get { return Fixed ? OwnCount : Math.Min(Mods.MostFrames, Mods.WidestStrip / Math.Max(1, Wide)); } }
        }

        internal readonly List<Strip> Strips = new List<Strip>();
        internal readonly string BaseFolder, BaseName, BaseId;
        private readonly ModFrames[] baseRates;

        internal AnimationDraft(Mods mods)
        {
            Mod outfit = mods.Outfit;
            BaseFolder = mods.OutfitFolder;
            BaseName = outfit == null ? null : outfit.File.Name;
            BaseId = outfit == null ? null : outfit.File.Id;
            baseRates = outfit == null ? null : outfit.File.Frames;
            HashSet<string> seen = new HashSet<string>();
            foreach (Clip clip in Clip.Every())
            {
                string title = clip.Title;
                if (title == null || !seen.Add(title)) continue;
                int wide, high;
                using (Stream own = typeof(AnimationDraft).Assembly.GetManifestResourceStream("Capri.Sprite." + title + ".png"))
                    if (!Mods.PngSize(own, out wide, out high)) continue;
                foreach (bool left in new[] { false, true })
                {
                    if (left && !Companion.HasArt("Capri.Sprite." + title + "_flipped.png")) continue;
                    Strip strip = new Strip { Title = title, Left = left, OwnCount = clip.OwnCount, OwnFps = Math.Max(1, (int)Math.Round(clip.OwnFps)), Wide = wide / Math.Max(1, clip.OwnCount), High = high };
                    strip.Fixed = Array.IndexOf(Mods.FixedFrames, title) >= 0;
                    strip.Fps = RateIn(title, strip.OwnFps);
                    Strips.Add(strip);
                }
            }
            Strips.Sort(delegate(Strip one, Strip other)
            {
                int order = string.CompareOrdinal(one.Title, other.Title);
                return order != 0 ? order : one.Left.CompareTo(other.Left);
            });
        }

        private int RateIn(string title, int own)
        {
            if (baseRates != null) foreach (ModFrames rate in baseRates) if (rate != null && rate.Strip == title) return rate.Fps;
            return own;
        }

        internal bool Dirty { get { return Strips.Exists(strip => strip.Changed || strip.Retimed); } }

        internal void Load(Strip strip)
        {
            if (strip.Frames != null) return;
            using (Bitmap art = Raw(strip)) strip.Frames = Cut(art, strip, out strip.From);
        }

        private Bitmap Raw(Strip strip)
        {
            string file = BaseFolder == null ? null : Path.Combine(BaseFolder, strip.File);
            if (strip.Left && BaseFolder != null && !System.IO.File.Exists(file) && System.IO.File.Exists(Path.Combine(BaseFolder, strip.Title + ".png")))
                file = Path.Combine(BaseFolder, strip.Title + ".png");
            if (file != null && System.IO.File.Exists(file))
            {
                using (MemoryStream bytes = new MemoryStream(System.IO.File.ReadAllBytes(file)))
                using (Bitmap read = new Bitmap(bytes))
                    return Plain(read);
            }
            using (Stream own = typeof(AnimationDraft).Assembly.GetManifestResourceStream("Capri.Sprite." + strip.File))
            using (Bitmap read = new Bitmap(own))
                return Plain(read);
        }

        private List<Bitmap> Cut(Bitmap art, Strip strip, out string from)
        {
            from = BaseName ?? "her own art";
            if (art.Height != strip.High || art.Width % strip.Wide != 0)
            {
                using (Stream own = typeof(AnimationDraft).Assembly.GetManifestResourceStream("Capri.Sprite." + strip.File))
                using (Bitmap read = new Bitmap(own))
                using (Bitmap plain = Plain(read))
                {
                    from = "her own art";
                    return Frames(plain, strip.Wide);
                }
            }
            return Frames(art, strip.Wide);
        }

        internal static Bitmap Plain(Image image)
        {
            Bitmap copy = new Bitmap(image.Width, image.Height, PixelFormat.Format32bppArgb);
            using (Graphics paper = Graphics.FromImage(copy))
            {
                paper.CompositingMode = CompositingMode.SourceCopy;
                paper.DrawImage(image, new Rectangle(0, 0, image.Width, image.Height));
            }
            return copy;
        }

        internal static List<Bitmap> Frames(Bitmap art, int wide)
        {
            List<Bitmap> frames = new List<Bitmap>();
            for (int x = 0; x + wide <= art.Width; x += wide) frames.Add(art.Clone(new Rectangle(x, 0, wide, art.Height), PixelFormat.Format32bppArgb));
            return frames;
        }

        internal static Bitmap Joined(List<Bitmap> frames, int wide, int high)
        {
            Bitmap strip = new Bitmap(wide * frames.Count, high, PixelFormat.Format32bppArgb);
            using (Graphics paper = Graphics.FromImage(strip))
            {
                paper.CompositingMode = CompositingMode.SourceCopy;
                for (int i = 0; i < frames.Count; i++) paper.DrawImage(frames[i], new Rectangle(i * wide, 0, wide, high));
            }
            return strip;
        }

        internal List<Bitmap> Read(Strip strip, string file)
        {
            Bitmap read;
            try
            {
                using (MemoryStream bytes = new MemoryStream(System.IO.File.ReadAllBytes(file)))
                using (Bitmap loaded = new Bitmap(bytes))
                    read = Plain(loaded);
            }
            catch (ArgumentException) { throw new ModException(Path.GetFileName(file) + " isn't a picture Capri can read."); }
            using (read)
            {
                if (read.Height != strip.High || read.Width % strip.Wide != 0)
                    throw new ModException(Path.GetFileName(file) + " is " + read.Width + " x " + read.Height + ". Each frame of " + strip.Shown + " is " + strip.Wide + " x " + strip.High + ", and a strip is frames side by side.");
                return Frames(read, strip.Wide);
            }
        }

        internal string Fits(Strip strip, int count)
        {
            if (strip.Fixed && count != strip.OwnCount) return strip.Shown + " keeps her " + strip.OwnCount + " cells.";
            if (count < 1) return strip.Shown + " needs at least one frame.";
            if (count > strip.Most) return strip.Shown + " can have up to " + strip.Most + " frames.";
            return null;
        }

        internal void Reset(Strip strip, bool own)
        {
            Drop(strip);
            strip.Changed = own && BaseFolder != null && System.IO.File.Exists(Path.Combine(BaseFolder, strip.File));
            strip.Retimed = false;
            strip.Fps = own ? strip.OwnFps : RateIn(strip.Title, strip.OwnFps);
            if (!own) return;
            using (Stream stream = typeof(AnimationDraft).Assembly.GetManifestResourceStream("Capri.Sprite." + strip.File))
            using (Bitmap read = new Bitmap(stream))
            using (Bitmap plain = Plain(read))
                strip.Frames = Frames(plain, strip.Wide);
            strip.From = "her own art";
        }

        private static void Drop(Strip strip)
        {
            if (strip.Frames != null) foreach (Bitmap frame in strip.Frames) frame.Dispose();
            strip.Frames = null;
        }

        internal Dictionary<string, byte[]> Pack(out ModFrames[] rates)
        {
            Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            if (BaseFolder != null && Directory.Exists(BaseFolder))
                foreach (string file in Directory.GetFiles(BaseFolder, "*.png")) files[Path.GetFileName(file)] = System.IO.File.ReadAllBytes(file);
            Dictionary<string, int> speeds = new Dictionary<string, int>(StringComparer.Ordinal);
            if (baseRates != null) foreach (ModFrames rate in baseRates) if (rate != null && rate.Strip != null) speeds[rate.Strip] = rate.Fps;
            foreach (Strip strip in Strips)
            {
                if (strip.Changed && strip.Frames != null)
                {
                    using (Bitmap joined = Joined(strip.Frames, strip.Wide, strip.High))
                    using (MemoryStream png = new MemoryStream())
                    {
                        joined.Save(png, ImageFormat.Png);
                        files[strip.File] = png.ToArray();
                    }
                }
                if (strip.Retimed && !strip.Left)
                {
                    if (strip.Fps == strip.OwnFps) speeds.Remove(strip.Title);
                    else speeds[strip.Title] = strip.Fps;
                }
            }
            List<ModFrames> made = new List<ModFrames>();
            foreach (KeyValuePair<string, int> speed in speeds) made.Add(new ModFrames { Strip = speed.Key, Fps = speed.Value });
            rates = made.Count == 0 ? null : made.ToArray();
            return files;
        }

        public void Dispose() { foreach (Strip strip in Strips) Drop(strip); }
    }

    internal sealed partial class Mainframe
    {
        private AnimationDraft animationDraft;

        private void EditAnimations()
        {
            if (animationDraft == null || animationDraft.BaseFolder != capri.ModList.OutfitFolder) ForgetAnimations();
            if (animationDraft == null) animationDraft = new AnimationDraft(capri.ModList);
            AnimationDraft used = animationDraft;
            AnimationSheet sheet = new AnimationSheet(this, SheetZoom(AnimationSheet.Wide, AnimationSheet.Tall), used);
            sheet.ShowDialog(this);
            sheet.Dispose();
            if (animationDraft != used) used.Dispose();
        }

        private void ForgetAnimations()
        {
            if (animationDraft != null) animationDraft.Dispose();
            animationDraft = null;
        }

        private sealed class AnimationPreview : Control
        {
            internal Bitmap Frame;
            internal bool Mirror;

            internal AnimationPreview()
            {
                SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = Well;
                AccessibleRole = AccessibleRole.Animation;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics graphics = e.Graphics;
                using (Brush fill = new SolidBrush(Well)) graphics.FillRectangle(fill, ClientRectangle);
                Bitmap frame = Frame;
                if (frame != null)
                {
                    float scale = Math.Min((Width - 8) / (float)frame.Width, (Height - 8) / (float)frame.Height);
                    float wide = frame.Width * scale, tall = frame.Height * scale;
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    GraphicsState kept = graphics.Save();
                    if (Mirror) { graphics.TranslateTransform(Width, 0); graphics.ScaleTransform(-1, 1); }
                    graphics.DrawImage(frame, (Width - wide) / 2, (Height - tall) / 2, wide, tall);
                    graphics.Restore(kept);
                }
                using (Pen rim = new Pen(WellEdge)) graphics.DrawRectangle(rim, 0, 0, Width - 1, Height - 1);
            }
        }

        private sealed class AnimationSheet : ModSheet
        {
            internal const int Wide = 640, Tall = 632;
            private readonly AnimationDraft draft;
            private readonly AnimationPreview preview = new AnimationPreview();
            private readonly Label name, about, at, status;
            private readonly Key play, duplicate, remove, add, earlier, later, slower, faster;
            private readonly Entry skinName;
            private readonly Timer clock = new Timer();
            private int shown, frame;
            private readonly Timer rewear = new Timer();
            private string wornAs;
            private sealed class Editing
            {
                internal AnimationDraft.Strip Strip;
                internal Bitmap Frame;
                internal string File;
                internal FileSystemWatcher Watcher;
                internal DateTime Seen;
                internal bool Due;
                internal int Tries;
            }
            private readonly Timer taking = new Timer();
            internal Func<string, bool> Opener;

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            private struct OpenAsInfo
            {
                [MarshalAs(UnmanagedType.LPWStr)] internal string File;
                [MarshalAs(UnmanagedType.LPWStr)] internal string Class;
                internal int Flags;
            }

            [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHOpenWithDialog(IntPtr parent, ref OpenAsInfo info);
            private readonly List<Editing> editing = new List<Editing>();

            internal AnimationSheet(Mainframe owner, float zoom, AnimationDraft draft) : base(owner, zoom, "Animations", "ANIMATIONS >", Wide, Tall)
            {
                this.draft = draft;
                Words("Change her frames, add more, or set how fast a strip plays, then wear it as a skin.", 28, 50, 584, 18, Small(), Faint);
                Make("<", 28, 76, 32, 26, delegate { Pick(shown - 1); }).AccessibleName = "Previous animation";
                name = Words("", 66, 79, 508, 22, Type(11f * Zoom, true), White);
                Make(">", 580, 76, 32, 26, delegate { Pick(shown + 1); }).AccessibleName = "Next animation";
                about = Words("", 28, 106, 584, 18, Small(), Faint);
                preview.Bounds = Box(28, 132, 300, 362);
                Controls.Add(preview);
                at = Words("", 344, 132, 268, 20, Font, White);
                play = Make("Pause", 344, 158, 84, 26, delegate { Play(!clock.Enabled); });
                Make("Prev", 434, 158, 86, 26, delegate { Play(false); Step(-1); }).AccessibleName = "Previous frame";
                Make("Next", 526, 158, 86, 26, delegate { Play(false); Step(1); }).AccessibleName = "Next frame";
                slower = Make("Slower", 344, 192, 130, 26, delegate { Retime(-1); });
                faster = Make("Faster", 482, 192, 130, 26, delegate { Retime(1); });
                Words("THIS FRAME", 344, 232, 268, 18, Small(), Rose);
                duplicate = Make("Duplicate", 344, 254, 130, 26, delegate { Duplicate(); });
                remove = Make("Delete", 482, 254, 130, 26, delegate { Delete(); });
                earlier = Make("Move earlier", 344, 286, 130, 26, delegate { Shift(-1); });
                later = Make("Move later", 482, 286, 130, 26, delegate { Shift(1); });
                Make("Replace...", 344, 318, 130, 26, delegate { Replace(); }).AccessibleName = "Replace this frame with a picture";
                add = Make("Add after...", 482, 318, 130, 26, delegate { AddAfter(); });
                add.AccessibleName = "Add frames from pictures after this one";
                Make("Edit frame in...", 344, 350, 130, 26, delegate { EditIn(false); }).AccessibleName = "Open this frame in any app, and take it back each time it's saved";
                Make("Edit strip in...", 482, 350, 130, 26, delegate { EditIn(true); }).AccessibleName = "Open the whole strip in any app, and take it back each time it's saved";
                Words("WHOLE STRIP", 344, 390, 268, 18, Small(), Rose);
                Make("Save PNG...", 344, 412, 130, 26, delegate { SaveStrip(); }).AccessibleName = "Save this strip as a PNG";
                Make("Load PNG...", 482, 412, 130, 26, delegate { LoadStrip(); }).AccessibleName = "Load this strip from a PNG";
                Make("Save frames...", 344, 444, 130, 26, delegate { SaveFrames(); }).AccessibleName = "Save each frame as its own PNG";
                Make("Load frames...", 482, 444, 130, 26, delegate { LoadFrames(); }).AccessibleName = "Load frames from a folder of PNGs";
                Make("Undo changes", 344, 476, 130, 26, delegate { Revert(false); });
                Make("Her own", 482, 476, 130, 26, delegate { Revert(true); }).AccessibleName = "Go back to her own art for this strip";
                Words("SKIN NAME", 28, 512, 120, 18, Small(), Rose);
                skinName = new Entry { Bounds = Box(28, 532, 300, 28), Cue = "Name your skin" };
                skinName.Box.Text = draft.BaseName == null ? "My animations" : Clamp(draft.BaseName.EndsWith(" (edited)", StringComparison.Ordinal) ? draft.BaseName : draft.BaseName + " (edited)");
                skinName.Box.AccessibleName = "Skin name";
                Controls.Add(skinName);
                Key wear = Make("Wear it", 344, 533, 130, 28, delegate { Save(true); });
                wear.Accent = Cyan;
                wear.Loud = true;
                wear.AccessibleName = "Save these animations as a skin and wear it";
                Make("Save skin...", 482, 533, 130, 28, delegate { Save(false); }).AccessibleName = "Save these animations as a skin file to share";
                status = Words("", 28, 572, 584, 40, Small(), Faint);
                clock.Tick += delegate { Step(1); };
                FormClosed += delegate { clock.Stop(); clock.Dispose(); StopEditing(); };
                taking.Interval = 400;
                taking.Tick += delegate { TakeNow(); };
                rewear.Interval = 700;
                rewear.Tick += delegate
                {
                    rewear.Stop();
                    Mod outfit = Frame.capri.ModList.Outfit;
                    if (wornAs != null && outfit != null && outfit.File.Id == Mods.IdFor(wornAs)) Wear(wornAs, true);
                };
                Pick(Math.Max(0, draft.Strips.FindIndex(strip => strip.Title == "idle" && !strip.Left)));
                Play(true);
                Frame.Fit(this);
            }

            private static string Clamp(string text) { return text.Length <= 40 ? text : text.Substring(0, 40).TrimEnd(); }

            private AnimationDraft.Strip Strip { get { return draft.Strips[shown]; } }

            private void Say(string text, bool trouble)
            {
                status.ForeColor = trouble ? Rose : Faint;
                status.Text = text;
            }

            private void Pick(int index)
            {
                if (draft.Strips.Count == 0) return;
                shown = (index % draft.Strips.Count + draft.Strips.Count) % draft.Strips.Count;
                try { draft.Load(Strip); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException || error is OutOfMemoryException)) throw;
                    Say("Couldn't read " + Strip.Shown + ": " + error.Message, true);
                }
                frame = 0;
                ShowFrame();
            }

            private void ShowFrame()
            {
                AnimationDraft.Strip strip = Strip;
                int count = strip.Count;
                frame = count == 0 ? 0 : Math.Max(0, Math.Min(frame, count - 1));
                name.Text = strip.Shown + (strip.Changed || strip.Retimed ? "  *" : "");
                about.Text = count + " frames at " + strip.Fps + " fps, " + strip.Wide + " x " + strip.High + " each, from " + (strip.From ?? "her own art") + (strip.Fixed ? ". Her mouth shapes and drawings keep their cells." : strip.Left ? ". Leave it out and she mirrors facing right." : ".");
                at.Text = "Frame " + (frame + 1) + " of " + count;
                preview.Frame = strip.Frames != null && count > 0 ? strip.Frames[frame] : null;
                preview.Mirror = strip.Left;
                preview.AccessibleName = strip.Shown + ", frame " + (frame + 1) + " of " + count;
                preview.Invalidate();
                duplicate.Enabled = add.Enabled = !strip.Fixed && count < strip.Most;
                remove.Enabled = !strip.Fixed && count > 1;
                earlier.Enabled = frame > 0;
                later.Enabled = frame < count - 1;
                slower.Enabled = !strip.Fixed && !strip.Left && strip.Fps > 1;
                faster.Enabled = !strip.Fixed && !strip.Left && strip.Fps < Mods.FastestFps;
                clock.Interval = Math.Max(15, 1000 / Math.Max(1, strip.Fps));
            }

            private void Play(bool on)
            {
                if (on) clock.Start(); else clock.Stop();
                play.Text = on ? "Pause" : "Play";
            }

            private void Step(int by)
            {
                int count = Strip.Count;
                if (count == 0) return;
                frame = ((frame + by) % count + count) % count;
                ShowFrame();
            }

            private void Changed()
            {
                Strip.Changed = true;
                Say("", false);
                ShowFrame();
            }

            private bool Room(int count)
            {
                string trouble = draft.Fits(Strip, count);
                if (trouble != null) Say(trouble, true);
                return trouble == null;
            }

            private void Retime(int by)
            {
                AnimationDraft.Strip strip = Strip;
                int steps = by * (strip.Fps >= 30 ? 5 : strip.Fps >= 10 ? 2 : 1);
                int fps = Math.Max(1, Math.Min(Mods.FastestFps, strip.Fps + steps));
                foreach (AnimationDraft.Strip twin in draft.Strips)
                    if (twin.Title == strip.Title) { twin.Fps = fps; twin.Retimed = true; }
                ShowFrame();
            }

            private void Duplicate()
            {
                List<Bitmap> frames = Strip.Frames;
                if (frames == null || !Room(frames.Count + 1)) return;
                frames.Insert(frame + 1, new Bitmap(frames[frame]));
                frame++;
                Changed();
            }

            private void Delete()
            {
                List<Bitmap> frames = Strip.Frames;
                if (frames == null || !Room(frames.Count - 1)) return;
                frames[frame].Dispose();
                frames.RemoveAt(frame);
                Changed();
            }

            private void Shift(int by)
            {
                List<Bitmap> frames = Strip.Frames;
                int to = frame + by;
                if (frames == null || to < 0 || to >= frames.Count) return;
                Bitmap moving = frames[frame];
                frames[frame] = frames[to];
                frames[to] = moving;
                frame = to;
                Changed();
            }

            private string[] PickPictures(string title, bool many)
            {
                using (OpenFileDialog dialog = new OpenFileDialog())
                {
                    dialog.Title = title;
                    dialog.Filter = "PNG pictures (*.png)|*.png";
                    dialog.Multiselect = many;
                    return dialog.ShowDialog(this) == DialogResult.OK ? dialog.FileNames : null;
                }
            }

            private List<Bitmap> ReadAll(IEnumerable<string> files) { return ReadAll(Strip, files); }

            private List<Bitmap> ReadAll(AnimationDraft.Strip strip, IEnumerable<string> files)
            {
                List<Bitmap> found = new List<Bitmap>();
                try
                {
                    foreach (string file in files) found.AddRange(draft.Read(strip, file));
                    return found;
                }
                catch (Exception error)
                {
                    foreach (Bitmap made in found) made.Dispose();
                    if (error is ModException) { Say(error.Message, true); return null; }
                    if (error is IOException || error is UnauthorizedAccessException || error is OutOfMemoryException) { Say("Couldn't read that: " + error.Message, true); return null; }
                    throw;
                }
            }

            private void Put(int at, int replacing, List<Bitmap> frames) { Put(Strip, at, replacing, frames); }

            private bool Put(AnimationDraft.Strip strip, int at, int replacing, List<Bitmap> frames)
            {
                List<Bitmap> into = strip.Frames;
                if (into == null || frames == null) return false;
                string trouble = draft.Fits(strip, into.Count - replacing + frames.Count);
                if (trouble != null || at < 0 || at + replacing > into.Count) { foreach (Bitmap made in frames) made.Dispose(); Say(trouble ?? strip.Shown + " changed since it was opened.", true); return false; }
                for (int i = 0; i < replacing; i++) { into[at].Dispose(); into.RemoveAt(at); }
                into.InsertRange(at, frames);
                strip.Changed = true;
                if (strip == Strip) frame = at;
                ShowFrame();
                Say(frames.Count == 1 ? "1 frame in." : frames.Count + " frames in.", false);
                return true;
            }

            private void Replace()
            {
                string[] files = PickPictures("Pick the picture for frame " + (frame + 1), false);
                if (files != null) Put(frame, 1, ReadAll(files));
            }

            private void AddAfter()
            {
                string[] files = PickPictures("Pick pictures to add after frame " + (frame + 1), true);
                if (files == null) return;
                Array.Sort(files, Natural);
                Put(frame + 1, 0, ReadAll(files));
            }

            private void LoadStrip()
            {
                string[] files = PickPictures("Pick a PNG strip for " + Strip.Shown, false);
                if (files != null) Put(0, Strip.Count, ReadAll(files));
            }

            private void LoadFrames()
            {
                using (FolderBrowserDialog picker = new FolderBrowserDialog())
                {
                    picker.Description = "Pick a folder of PNG frames for " + Strip.Shown + ". They go in by name.";
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    string[] files = Directory.GetFiles(picker.SelectedPath, "*.png");
                    if (files.Length == 0) { Say("That folder has no PNG pictures.", true); return; }
                    Array.Sort(files, Natural);
                    Put(0, Strip.Count, ReadAll(files));
                }
            }

            private static int Natural(string one, string other)
            {
                string a = Path.GetFileNameWithoutExtension(one), b = Path.GetFileNameWithoutExtension(other);
                int i = 0, j = 0;
                while (i < a.Length && j < b.Length)
                {
                    if (char.IsDigit(a[i]) && char.IsDigit(b[j]))
                    {
                        int startA = i, startB = j;
                        while (i < a.Length && char.IsDigit(a[i])) i++;
                        while (j < b.Length && char.IsDigit(b[j])) j++;
                        string numberA = a.Substring(startA, i - startA).TrimStart('0'), numberB = b.Substring(startB, j - startB).TrimStart('0');
                        int order = numberA.Length != numberB.Length ? numberA.Length.CompareTo(numberB.Length) : string.CompareOrdinal(numberA, numberB);
                        if (order != 0) return order;
                        continue;
                    }
                    int letters = char.ToLowerInvariant(a[i]).CompareTo(char.ToLowerInvariant(b[j]));
                    if (letters != 0) return letters;
                    i++;
                    j++;
                }
                return (a.Length - i).CompareTo(b.Length - j);
            }

            private void SaveStrip()
            {
                AnimationDraft.Strip strip = Strip;
                if (strip.Frames == null) return;
                using (SaveFileDialog picker = new SaveFileDialog { Title = "Save " + strip.Shown + " as a PNG strip", Filter = "PNG pictures (*.png)|*.png", DefaultExt = "png", AddExtension = true, FileName = strip.File })
                {
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        using (Bitmap joined = AnimationDraft.Joined(strip.Frames, strip.Wide, strip.High)) joined.Save(picker.FileName, ImageFormat.Png);
                        Say("Saved " + strip.Count + " frames side by side, " + strip.Wide + " x " + strip.High + " each. Draw on it and load it back.", false);
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException || error is UnauthorizedAccessException || error is System.Runtime.InteropServices.ExternalException)) throw;
                        Say("Couldn't save that: " + error.Message, true);
                    }
                }
            }

            private void SaveFrames()
            {
                AnimationDraft.Strip strip = Strip;
                if (strip.Frames == null) return;
                using (FolderBrowserDialog picker = new FolderBrowserDialog())
                {
                    picker.Description = "Pick a folder for the frames of " + strip.Shown + ".";
                    if (picker.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        string lead = strip.File.Substring(0, strip.File.Length - 4);
                        for (int i = 0; i < strip.Frames.Count; i++) strip.Frames[i].Save(Path.Combine(picker.SelectedPath, lead + "_" + (i + 1).ToString("000") + ".png"), ImageFormat.Png);
                        Say("Saved " + strip.Count + " frames as " + lead + "_001.png and on.", false);
                    }
                    catch (Exception error)
                    {
                        if (!(error is IOException || error is UnauthorizedAccessException || error is System.Runtime.InteropServices.ExternalException)) throw;
                        Say("Couldn't save those: " + error.Message, true);
                    }
                }
            }

            private static string EditFolder { get { return Path.Combine(Path.GetTempPath(), "capri-animations"); } }

            private void EditIn(bool whole) { EditFile(whole, true); }

            internal string EditFile(bool whole, bool open)
            {
                AnimationDraft.Strip strip = Strip;
                if (strip.Frames == null || strip.Frames.Count == 0) return null;
                string lead = strip.File.Substring(0, strip.File.Length - 4);
                string file = Path.Combine(EditFolder, whole ? lead + ".png" : lead + "_" + (frame + 1).ToString("000") + ".png");
                Editing same = editing.Find(each => string.Equals(each.File, file, StringComparison.OrdinalIgnoreCase));
                if (same != null) Forget(same);
                try
                {
                    Directory.CreateDirectory(EditFolder);
                    if (whole) using (Bitmap joined = AnimationDraft.Joined(strip.Frames, strip.Wide, strip.High)) joined.Save(file, ImageFormat.Png);
                    else strip.Frames[frame].Save(file, ImageFormat.Png);
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ExternalException)) throw;
                    Say("Couldn't get it ready to edit: " + error.Message, true);
                    return null;
                }
                Editing made = new Editing { Strip = strip, Frame = whole ? null : strip.Frames[frame], File = file, Seen = File.GetLastWriteTimeUtc(file) };
                made.Watcher = new FileSystemWatcher(EditFolder, Path.GetFileName(file));
                made.Watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size;
                made.Watcher.SynchronizingObject = this;
                FileSystemEventHandler saw = delegate { made.Due = true; made.Tries = 0; taking.Stop(); taking.Start(); };
                made.Watcher.Changed += saw;
                made.Watcher.Created += saw;
                made.Watcher.Renamed += delegate { made.Due = true; made.Tries = 0; taking.Stop(); taking.Start(); };
                made.Watcher.EnableRaisingEvents = true;
                editing.Add(made);
                if (open && !(Opener ?? OpenWith)(file)) { Forget(made); return null; }
                Say("Opened " + Path.GetFileName(file) + ". Each time you save it in that app, it comes back here" + (wornAs != null ? " and onto her." : ". Wear it to see it on her, and saves after that go straight onto her."), false);
                return file;
            }

            private bool OpenWith(string file)
            {
                OpenAsInfo info = new OpenAsInfo { File = file, Flags = 0x1 | 0x4 };
                int result;
                try { result = SHOpenWithDialog(Handle, ref info); }
                catch (EntryPointNotFoundException) { result = -1; }
                if (result >= 0) return true;
                if (result == unchecked((int)0x800704C7)) return false;
                try
                {
                    using (System.Diagnostics.Process.Start("rundll32.exe", "shell32.dll,OpenAs_RunDLL " + file)) { }
                    return true;
                }
                catch (System.ComponentModel.Win32Exception error)
                {
                    Say("Couldn't ask which app to open it with: " + error.Message, true);
                    return false;
                }
            }

            internal void TakeNow()
            {
                taking.Stop();
                bool again = false;
                foreach (Editing made in editing.ToArray())
                {
                    if (!made.Due && File.Exists(made.File) && File.GetLastWriteTimeUtc(made.File) == made.Seen) continue;
                    if (!Take(made) && ++made.Tries < 15) again = true;
                    else made.Due = false;
                }
                if (again) taking.Start();
            }

            private bool Take(Editing made)
            {
                DateTime written;
                List<Bitmap> frames;
                try
                {
                    if (!File.Exists(made.File)) return false;
                    written = File.GetLastWriteTimeUtc(made.File);
                    if (written == made.Seen) return true;
                    frames = draft.Read(made.Strip, made.File);
                }
                catch (IOException) { return false; }
                catch (UnauthorizedAccessException) { return false; }
                catch (ModException error)
                {
                    if (made.Tries < 14) return false;
                    Say(error.Message, true);
                    return true;
                }
                made.Seen = written;
                made.Due = false;
                bool done;
                if (made.Frame == null) done = Put(made.Strip, 0, made.Strip.Frames == null ? 0 : made.Strip.Frames.Count, frames);
                else
                {
                    int at = made.Strip.Frames == null ? -1 : made.Strip.Frames.IndexOf(made.Frame);
                    if (at < 0)
                    {
                        foreach (Bitmap extra in frames) extra.Dispose();
                        Say("That frame isn't in " + made.Strip.Shown + " any more, so the save wasn't taken.", true);
                        return true;
                    }
                    done = Put(made.Strip, at, 1, frames);
                    if (done) made.Frame = frames[0];
                }
                if (done && wornAs != null) { rewear.Stop(); rewear.Start(); }
                return true;
            }

            private void Forget(Editing made)
            {
                made.Watcher.EnableRaisingEvents = false;
                made.Watcher.Dispose();
                editing.Remove(made);
                try { if (File.Exists(made.File)) File.Delete(made.File); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            private void StopEditing()
            {
                taking.Stop();
                taking.Dispose();
                rewear.Stop();
                rewear.Dispose();
                foreach (Editing made in editing.ToArray()) Forget(made);
            }

            private void Revert(bool own)
            {
                try { draft.Reset(Strip, own); draft.Load(Strip); }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)) throw;
                    Say("Couldn't read " + Strip.Shown + ": " + error.Message, true);
                }
                Say(own ? Strip.Shown + " is her own art again." : Strip.Shown + " is back to how it was.", false);
                ShowFrame();
            }

            private void Save(bool wear) { Wear(skinName.Box.Text.Trim(), wear); }

            private void Wear(string title, bool wear)
            {
                if (title.Length == 0) { Say("Name your skin first.", true); return; }
                if (!draft.Dirty) { Say("Change a frame or a speed first.", true); return; }
                string id = Mods.IdFor(title);
                Mod taken = Frame.capri.ModList.All.Find(mod => mod.File.Id == id);
                if (taken != null && !taken.Skin) { Say(taken.File.Name + " already uses that name. Pick another.", true); return; }
                string file = null;
                if (!wear)
                    using (SaveFileDialog picker = new SaveFileDialog { Title = "Save your skin", Filter = "Capri skins (*" + Mods.SkinExtension + ")|*" + Mods.SkinExtension, DefaultExt = Mods.SkinExtension.TrimStart('.'), AddExtension = true, FileName = id + Mods.SkinExtension })
                    {
                        if (picker.ShowDialog(this) != DialogResult.OK) return;
                        file = picker.FileName;
                    }
                string temporary = Path.Combine(Path.GetTempPath(), "capri-animations-" + Guid.NewGuid().ToString("N") + Mods.SkinExtension);
                try
                {
                    ModFrames[] rates;
                    Dictionary<string, byte[]> strips = draft.Pack(out rates);
                    ModFile details = new ModFile { Id = id, Name = Clamp(title), Version = "1", Description = draft.BaseName == null ? "Her animations, changed in her animation editor." : Clamp("Made from " + draft.BaseName) + " in her animation editor.", Frames = rates };
                    Mods.PackSkin(wear ? temporary : file, details, strips);
                    if (!wear) { Say("Saved " + Path.GetFileName(file) + ". Import it on any Capri, or share it in the mods directory.", false); return; }
                    Mod worn = Frame.capri.ModList.WearOutfit(temporary);
                    Frame.ForgetDraftAfterWearing();
                    Frame.FillMods();
                    wornAs = worn == null ? null : title;
                    Say(worn == null ? "Couldn't wear it." : worn.File.Name + " is on. Find it in your mods to publish it." + (editing.Count > 0 ? " Saving in your app puts it on her too." : ""), worn == null);
                }
                catch (ModException error) { Say(error.Message, true); }
                catch (IOException error) { Say("Couldn't save that skin: " + error.Message, true); }
                catch (UnauthorizedAccessException error) { Say("Couldn't save that skin: " + error.Message, true); }
                finally
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            protected override bool ProcessCmdKey(ref Message message, Keys keys)
            {
                if (!skinName.Box.Focused)
                {
                    if (keys == Keys.Left || keys == Keys.Right) { Play(false); Step(keys == Keys.Left ? -1 : 1); return true; }
                    if (keys == Keys.PageUp || keys == Keys.PageDown) { Pick(shown + (keys == Keys.PageUp ? -1 : 1)); return true; }
                    if (keys == (Keys.Control | Keys.Space)) { Play(!clock.Enabled); return true; }
                }
                return base.ProcessCmdKey(ref message, keys);
            }

            internal string Showing { get { return Strip.Shown + " " + (frame + 1) + "/" + Strip.Count + " " + Strip.Fps; } }
        }

        private void ForgetDraftAfterWearing() { animationDraft = null; }

        internal void CheckAnimations(List<string> checks, string output)
        {
            ForgetAnimations();
            Mods mods = capri.ModList;
            Func<string, CapriDesktop.Clip> clip = delegate(string title) { return Array.Find(CapriDesktop.Clip.Every(), each => each.Title == title); };
            AnimationDraft draft = new AnimationDraft(mods);
            try
            {
                AnimationDraft.Strip idle = draft.Strips.Find(strip => strip.Title == "idle" && !strip.Left), mouth = draft.Strips.Find(strip => strip.Title == "mouth");
                if (idle == null || mouth == null || !mouth.Fixed || idle.Fixed || !draft.Strips.Exists(strip => strip.Title == "perch" && strip.Left) || draft.Strips.Exists(strip => strip.Title == "walk" && strip.Left))
                    throw new Exception("Her animation editor didn't list her strips and their facing-left sets.");
                using (AnimationSheet sheet = new AnimationSheet(this, 1f, draft))
                {
                    sheet.Show(this);
                    string crowded = sheet.Crowded();
                    if (crowded != null) throw new Exception("Her animation editor doesn't fit: " + crowded);
                    if (!sheet.Showing.StartsWith("idle 1/60 24", StringComparison.Ordinal)) throw new Exception("Her animation editor didn't open on her idle: " + sheet.Showing);
                    sheet.Picture(Path.Combine(output, "animations.png"));
                    sheet.Close();
                }
                draft.Load(idle);
                if (idle.Count != 60 || idle.Frames[0].Width != idle.Wide || idle.Frames[0].Height != idle.High || idle.From != "her own art") throw new Exception("Her idle didn't load into her animation editor as her own 60 frames.");
                if (draft.Fits(idle, Mods.MostFrames + 1) == null || draft.Fits(mouth, mouth.OwnCount + 1) == null || draft.Fits(idle, 0) == null || draft.Fits(idle, 61) != null) throw new Exception("Her animation editor let a strip take a frame count it can't.");
                idle.Frames.Add(new Bitmap(idle.Frames[0]));
                idle.Changed = true;
                foreach (AnimationDraft.Strip strip in draft.Strips) if (strip.Title == "idle") { strip.Fps = 30; strip.Retimed = true; }
                ModFrames[] rates;
                Dictionary<string, byte[]> strips = draft.Pack(out rates);
                if (strips.Count != 1 || !strips.ContainsKey("idle.png") || rates == null || rates.Length != 1 || rates[0].Strip != "idle" || rates[0].Fps != 30) throw new Exception("Her animation editor packed more or less than what changed.");
                string file = Path.Combine(output, "animations-test" + Mods.SkinExtension);
                Mods.PackSkin(file, new ModFile { Id = Mods.IdFor("Animations Test!"), Name = "Animations Test", Frames = rates }, strips);
                Mod worn = mods.WearOutfit(file);
                try
                {
                    CapriDesktop.Clip standing = clip("idle");
                    if (worn == null || worn.File.Id != "animations-test" || mods.Outfit != worn || standing.Count != 61 || standing.Fps != 30) throw new Exception("Wearing a skin from her animation editor didn't give her its 61 frames at 30 fps: " + (standing == null ? "no idle" : standing.Count + " at " + standing.Fps));
                }
                finally { if (worn != null) mods.Remove(worn); }
                if (clip("idle").Count != 60 || clip("idle").Fps != 24) throw new Exception("Removing a skin from her animation editor didn't bring her own idle back.");
                checks.Add("PASS animations: her editor lists her strips, fits, loads her own frames, keeps her mouth shapes fixed, and a frame and speed changed there are worn as a skin and taken off again");
            }
            finally { draft.Dispose(); }
            AnimationDraft editing = new AnimationDraft(mods);
            try
            {
                using (AnimationSheet sheet = new AnimationSheet(this, 1f, editing))
                {
                    List<string> opened = new List<string>();
                    sheet.Opener = delegate(string file) { opened.Add(file); return true; };
                    sheet.Show(this);
                    AnimationDraft.Strip idle = editing.Strips.Find(strip => strip.Title == "idle" && !strip.Left);
                    Func<string, int, Color, bool> draw = delegate(string file, int frames, Color paint)
                    {
                        using (Bitmap made = new Bitmap(idle.Wide * frames, idle.High, PixelFormat.Format32bppArgb))
                        {
                            using (Graphics paper = Graphics.FromImage(made)) paper.Clear(paint);
                            made.Save(file, ImageFormat.Png);
                        }
                        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(5));
                        return true;
                    };
                    string one = sheet.EditFile(false, true);
                    if (one == null || opened.Count != 1 || opened[0] != one || !File.Exists(one)) throw new Exception("Edit frame in didn't write the frame and ask which app opens it.");
                    draw(one, 1, Color.Red);
                    sheet.TakeNow();
                    if (idle.Count != 60 || !idle.Changed || idle.Frames[0].GetPixel(10, 10).ToArgb() != Color.Red.ToArgb()) throw new Exception("A frame saved in another app didn't come back into her editor.");
                    string whole = sheet.EditFile(true, true);
                    draw(whole, 30, Color.Blue);
                    sheet.TakeNow();
                    if (idle.Count != 30 || idle.Frames[29].GetPixel(10, 10).ToArgb() != Color.Blue.ToArgb()) throw new Exception("A strip saved in another app didn't come back with its 30 frames.");
                    draw(whole, 1, Color.Lime);
                    File.WriteAllText(whole, "not a picture yet");
                    File.SetLastWriteTimeUtc(whole, DateTime.UtcNow.AddSeconds(10));
                    sheet.TakeNow();
                    if (idle.Count != 30) throw new Exception("A half-saved strip from another app replaced her frames.");
                    sheet.Close();
                    if (File.Exists(one) || File.Exists(whole)) throw new Exception("Closing her editor left its files for other apps behind.");
                }
                checks.Add("PASS animations: Edit frame in and Edit strip in write a PNG, ask which app opens it, take each save back, and leave a half-saved file alone");
            }
            finally { editing.Dispose(); }
        }
    }
}
