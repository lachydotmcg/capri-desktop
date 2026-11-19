using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;

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
            internal List<SKBitmap> Frames;
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

        internal AnimationDraft(Mods mods, IEnumerable<Clip> clips)
        {
            Mod outfit = mods.Outfit;
            BaseFolder = mods.OutfitFolder;
            BaseName = outfit == null ? null : outfit.File.Name;
            BaseId = outfit == null ? null : outfit.File.Id;
            baseRates = outfit == null ? null : outfit.File.Frames;
            HashSet<string> seen = new HashSet<string>();
            foreach (Clip clip in clips)
            {
                string title = clip.Title;
                if (title == null || !seen.Add(title)) continue;
                int wide, high;
                using (Stream own = typeof(AnimationDraft).Assembly.GetManifestResourceStream("Capri.Sprite." + title + ".png"))
                    if (!Mods.PngSize(own, out wide, out high)) continue;
                foreach (bool left in new[] { false, true })
                {
                    if (left && !Pet.HasArt("Capri.Sprite." + title + "_flipped.png")) continue;
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
            using (SKBitmap art = Raw(strip)) strip.Frames = Cut(art, strip, out strip.From);
        }

        private SKBitmap Raw(Strip strip)
        {
            string file = BaseFolder == null ? null : Path.Combine(BaseFolder, strip.File);
            if (strip.Left && BaseFolder != null && !System.IO.File.Exists(file) && System.IO.File.Exists(Path.Combine(BaseFolder, strip.Title + ".png")))
                file = Path.Combine(BaseFolder, strip.Title + ".png");
            if (file != null && System.IO.File.Exists(file)) return Plain(System.IO.File.ReadAllBytes(file));
            return Own(strip);
        }

        private static SKBitmap Own(Strip strip)
        {
            using (Stream own = typeof(AnimationDraft).Assembly.GetManifestResourceStream("Capri.Sprite." + strip.File))
            using (MemoryStream bytes = new MemoryStream())
            {
                own.CopyTo(bytes);
                return Plain(bytes.ToArray());
            }
        }

        private List<SKBitmap> Cut(SKBitmap art, Strip strip, out string from)
        {
            from = BaseName ?? "her own art";
            if (art.Height != strip.High || art.Width % strip.Wide != 0)
            {
                using (SKBitmap plain = Own(strip))
                {
                    from = "her own art";
                    return Frames(plain, strip.Wide);
                }
            }
            return Frames(art, strip.Wide);
        }

        internal static SKBitmap Plain(byte[] picture)
        {
            using (SKCodec codec = SKCodec.Create(new MemoryStream(picture)))
            {
                if (codec == null) throw new ArgumentException("That picture couldn't be read.");
                SKImageInfo info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                SKBitmap art = new SKBitmap(info);
                if (codec.GetPixels(info, art.GetPixels()) != SKCodecResult.Success)
                {
                    art.Dispose();
                    throw new ArgumentException("That picture couldn't be read.");
                }
                return art;
            }
        }

        private static SKBitmap Blank(int wide, int high)
        {
            return new SKBitmap(new SKImageInfo(wide, high, SKColorType.Bgra8888, SKAlphaType.Unpremul));
        }

        internal static SKBitmap Copy(SKBitmap frame)
        {
            SKBitmap made = Blank(frame.Width, frame.Height);
            byte[] pixels = frame.Bytes;
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, made.GetPixels(), Math.Min(pixels.Length, made.ByteCount));
            return made;
        }

        internal static List<SKBitmap> Frames(SKBitmap art, int wide)
        {
            List<SKBitmap> frames = new List<SKBitmap>();
            byte[] pixels = art.Bytes;
            int across = art.RowBytes, rowBytes = wide * 4;
            for (int x = 0; x + wide <= art.Width; x += wide)
            {
                byte[] cell = new byte[rowBytes * art.Height];
                for (int y = 0; y < art.Height; y++) Buffer.BlockCopy(pixels, y * across + x * 4, cell, y * rowBytes, rowBytes);
                SKBitmap frame = Blank(wide, art.Height);
                System.Runtime.InteropServices.Marshal.Copy(cell, 0, frame.GetPixels(), cell.Length);
                frames.Add(frame);
            }
            return frames;
        }

        internal static SKBitmap Joined(List<SKBitmap> frames, int wide, int high)
        {
            int rowBytes = wide * frames.Count * 4;
            byte[] pixels = new byte[rowBytes * high];
            for (int i = 0; i < frames.Count; i++)
            {
                byte[] cell = frames[i].Bytes;
                int cellRow = frames[i].RowBytes;
                for (int y = 0; y < high; y++) Buffer.BlockCopy(cell, y * cellRow, pixels, y * rowBytes + i * wide * 4, Math.Min(wide * 4, cellRow));
            }
            SKBitmap strip = Blank(wide * frames.Count, high);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, strip.GetPixels(), pixels.Length);
            return strip;
        }

        internal static byte[] Png(SKBitmap art)
        {
            using (SKImage image = SKImage.FromBitmap(art))
            using (SKData png = image.Encode(SKEncodedImageFormat.Png, 100)) return png.ToArray();
        }

        internal List<SKBitmap> Read(Strip strip, string file)
        {
            SKBitmap read;
            try { read = Plain(System.IO.File.ReadAllBytes(file)); }
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
            using (SKBitmap plain = Own(strip)) strip.Frames = Frames(plain, strip.Wide);
            strip.From = "her own art";
        }

        private static void Drop(Strip strip)
        {
            if (strip.Frames != null) foreach (SKBitmap frame in strip.Frames) frame.Dispose();
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
                    using (SKBitmap joined = Joined(strip.Frames, strip.Wide, strip.High)) files[strip.File] = Png(joined);
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

    internal sealed class AnimationPreview : Control
    {
        private readonly Dictionary<SKBitmap, Bitmap> pictures = new Dictionary<SKBitmap, Bitmap>();
        private SKBitmap frame;
        internal bool Mirror;

        internal SKBitmap Frame { get { return frame; } set { frame = value; InvalidateVisual(); } }

        internal void Keep(List<SKBitmap> frames)
        {
            foreach (SKBitmap gone in new List<SKBitmap>(pictures.Keys))
                if (frames == null || !frames.Contains(gone)) { pictures[gone].Dispose(); pictures.Remove(gone); }
        }

        public override void Render(DrawingContext context)
        {
            Rect box = new Rect(Bounds.Size);
            context.DrawRectangle(MainframeTheme.Well, new Pen(MainframeTheme.WellEdge, 1), box);
            SKBitmap shown = frame;
            if (shown == null) return;
            Bitmap picture;
            if (!pictures.TryGetValue(shown, out picture))
            {
                using (MemoryStream png = new MemoryStream(AnimationDraft.Png(shown))) picture = new Bitmap(png);
                pictures[shown] = picture;
            }
            double scale = Math.Min((box.Width - 8) / picture.Size.Width, (box.Height - 8) / picture.Size.Height);
            double wide = picture.Size.Width * scale, tall = picture.Size.Height * scale;
            Rect place = new Rect((box.Width - wide) / 2, (box.Height - tall) / 2, wide, tall);
            using (context.PushClip(box))
            using (context.PushTransform(Mirror ? Matrix.CreateTranslation(-box.Width / 2, 0) * Matrix.CreateScale(-1, 1) * Matrix.CreateTranslation(box.Width / 2, 0) : Matrix.Identity))
                context.DrawImage(picture, new Rect(picture.Size), place);
        }

        internal void Clear()
        {
            foreach (Bitmap picture in pictures.Values) picture.Dispose();
            pictures.Clear();
        }
    }

    internal sealed class AnimationSheet : MainframeWindow
    {
        internal const int Wide = 640, Tall = 600;
        private readonly Pet pet;
        private readonly Mainframe frame;
        private readonly AnimationDraft draft;
        private readonly AnimationPreview preview = new AnimationPreview();
        private readonly NeonCaption name, about, at, status;
        private readonly NeonButton play, duplicate, remove, add, earlier, later, slower, faster;
        private readonly NeonEntry skinName;
        private readonly DispatcherTimer clock = new DispatcherTimer();
        private int shown, number;
        private bool closed;

        internal AnimationSheet(Pet owner, Mainframe from, AnimationDraft draft) : base("Animations")
        {
            pet = owner;
            frame = from;
            this.draft = draft;
            Steered = true; FitsScreen = true;
            ShowInTaskbar = false;
            Label("ANIMATIONS", 24, 15, 260, 29, MainframeTheme.Rose, 20, true).Display = true;
            Button("", Wide - 54, 20, 26, 22, Close).Glyph = 2;
            Label("Change her frames, add more, or set how fast a strip plays, then wear it as a skin.", 28, 50, 584, 18, MainframeTheme.Faint, 34.0 / 3);
            Button("<", 28, 76, 32, 26, delegate { Pick(shown - 1); }, "Previous animation");
            name = Label("", 66, 79, 508, 22, MainframeTheme.White, 44.0 / 3, true);
            Button(">", 580, 76, 32, 26, delegate { Pick(shown + 1); }, "Next animation");
            about = Label("", 28, 106, 584, 18, MainframeTheme.Faint, 34.0 / 3);
            Put(Page, preview, 28, 132, 300, 330, "Animation preview");
            at = Label("", 344, 132, 268, 20, MainframeTheme.White);
            play = Button("Pause", 344, 158, 84, 26, delegate { Play(!clock.IsEnabled); });
            Button("Prev", 434, 158, 86, 26, delegate { Play(false); Step(-1); }, "Previous frame");
            Button("Next", 526, 158, 86, 26, delegate { Play(false); Step(1); }, "Next frame");
            slower = Button("Slower", 344, 192, 130, 26, delegate { Retime(-1); });
            faster = Button("Faster", 482, 192, 130, 26, delegate { Retime(1); });
            Label("THIS FRAME", 344, 232, 268, 18, MainframeTheme.Rose, 34.0 / 3);
            duplicate = Button("Duplicate", 344, 254, 130, 26, Duplicate);
            remove = Button("Delete", 482, 254, 130, 26, Delete);
            earlier = Button("Move earlier", 344, 286, 130, 26, delegate { Shift(-1); });
            later = Button("Move later", 482, 286, 130, 26, delegate { Shift(1); });
            Button("Replace...", 344, 318, 130, 26, Replace, "Replace this frame with a picture");
            add = Button("Add after...", 482, 318, 130, 26, AddAfter, "Add frames from pictures after this one");
            Label("WHOLE STRIP", 344, 358, 268, 18, MainframeTheme.Rose, 34.0 / 3);
            Button("Save PNG...", 344, 380, 130, 26, SaveStrip, "Save this strip as a PNG");
            Button("Load PNG...", 482, 380, 130, 26, LoadStrip, "Load this strip from a PNG");
            Button("Save frames...", 344, 412, 130, 26, SaveFrames, "Save each frame as its own PNG");
            Button("Load frames...", 482, 412, 130, 26, LoadFrames, "Load frames from a folder of PNGs");
            Button("Undo changes", 344, 444, 130, 26, delegate { Revert(false); });
            Button("Her own", 482, 444, 130, 26, delegate { Revert(true); }, "Go back to her own art for this strip");
            Label("SKIN NAME", 28, 480, 120, 18, MainframeTheme.Rose, 34.0 / 3);
            skinName = Put(Page, new NeonEntry("Name your skin"), 28, 500, 300, 28, "Skin name");
            skinName.Box.Text = draft.BaseName == null ? "My animations" : Clamp(draft.BaseName.EndsWith(" (edited)", StringComparison.Ordinal) ? draft.BaseName : draft.BaseName + " (edited)");
            NeonButton wear = Button("Wear it", 344, 501, 130, 28, delegate { Save(true); }, "Save these animations as a skin and wear it");
            wear.Accent = MainframeTheme.Cyan; wear.Loud = true;
            Button("Save skin...", 482, 501, 130, 28, delegate { Save(false); }, "Save these animations as a skin file to share");
            status = Label("", 28, 540, 584, 40, MainframeTheme.Faint, 34.0 / 3);
            status.Top = true;
            status.Ellipsis = false;
            clock.Tick += delegate { Program.Guard(delegate { Step(1); }); };
            Opened += delegate { Fit(Wide, Tall, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0); };
            Closed += delegate { closed = true; clock.Stop(); preview.Frame = null; preview.Clear(); };
            Pick(Math.Max(0, draft.Strips.FindIndex(strip => strip.Title == "idle" && !strip.Left)));
            Play(true);
            Fit(Wide, Tall, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0);
        }

        private NeonCaption Label(string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false)
        {
            return Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Faint, Size = size, Bold = bold, Ellipsis = true }, x, y, width, height);
        }

        private NeonButton Button(string text, int x, int y, int width, int height, Action action, string described = null)
        {
            NeonButton button = Put(Page, new NeonButton { Text = text }, x, y, width, height, described ?? (text.Length == 0 ? "Close" : text));
            button.Click += delegate { action(); };
            return button;
        }

        private static string Clamp(string text) { return text.Length <= 40 ? text : text.Substring(0, 40).TrimEnd(); }

        private AnimationDraft.Strip Strip { get { return draft.Strips[shown]; } }

        internal string Showing { get { return Strip.Shown + " " + (number + 1) + "/" + Strip.Count + " " + Strip.Fps; } }
        internal string StatusText { get { return status.Text; } }

        private void Say(string text, bool trouble)
        {
            status.Ink = trouble ? MainframeTheme.Rose : MainframeTheme.Faint;
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
            number = 0;
            ShowFrame();
        }

        private void ShowFrame()
        {
            AnimationDraft.Strip strip = Strip;
            int count = strip.Count;
            number = count == 0 ? 0 : Math.Max(0, Math.Min(number, count - 1));
            name.Text = strip.Shown + (strip.Changed || strip.Retimed ? "  *" : "");
            about.Text = count + " frames at " + strip.Fps + " fps, " + strip.Wide + " x " + strip.High + " each, from " + (strip.From ?? "her own art") + (strip.Fixed ? ". Her mouth shapes and drawings keep their cells." : strip.Left ? ". Leave it out and she mirrors facing right." : ".");
            at.Text = "Frame " + (number + 1) + " of " + count;
            preview.Keep(strip.Frames);
            preview.Mirror = strip.Left;
            preview.Frame = strip.Frames != null && count > 0 ? strip.Frames[number] : null;
            AutomationProperties.SetName(preview, strip.Shown + ", frame " + (number + 1) + " of " + count);
            duplicate.IsEnabled = add.IsEnabled = !strip.Fixed && count < strip.Most;
            remove.IsEnabled = !strip.Fixed && count > 1;
            earlier.IsEnabled = number > 0;
            later.IsEnabled = number < count - 1;
            slower.IsEnabled = !strip.Fixed && !strip.Left && strip.Fps > 1;
            faster.IsEnabled = !strip.Fixed && !strip.Left && strip.Fps < Mods.FastestFps;
            clock.Interval = TimeSpan.FromMilliseconds(Math.Max(15, 1000 / Math.Max(1, strip.Fps)));
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
            number = ((number + by) % count + count) % count;
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
            List<SKBitmap> frames = Strip.Frames;
            if (frames == null || !Room(frames.Count + 1)) return;
            frames.Insert(number + 1, AnimationDraft.Copy(frames[number]));
            number++;
            Changed();
        }

        private void Delete()
        {
            List<SKBitmap> frames = Strip.Frames;
            if (frames == null || !Room(frames.Count - 1)) return;
            SKBitmap gone = frames[number];
            frames.RemoveAt(number);
            preview.Frame = null;
            preview.Keep(frames);
            gone.Dispose();
            Changed();
        }

        private void Shift(int by)
        {
            List<SKBitmap> frames = Strip.Frames;
            int to = number + by;
            if (frames == null || to < 0 || to >= frames.Count) return;
            SKBitmap moving = frames[number];
            frames[number] = frames[to];
            frames[to] = moving;
            number = to;
            Changed();
        }

        private async void PickPictures(string title, bool many, Action<string[]> then)
        {
            try
            {
                if (!StorageProvider.CanOpen) { Say("Couldn't open the picture picker.", true); return; }
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = title, AllowMultiple = many,
                    FileTypeFilter = new[] { new FilePickerFileType("PNG pictures") { Patterns = new[] { "*.png" } } }
                });
                List<string> paths = new List<string>();
                foreach (IStorageFile file in files)
                {
                    string path = file.TryGetLocalPath();
                    if (path != null) paths.Add(path);
                    file.Dispose();
                }
                if (closed || files.Count == 0) return;
                if (paths.Count == 0) Say("Those need to be local files.", true);
                else Program.Guard(delegate { then(paths.ToArray()); });
            }
            catch (Exception) { if (!closed) Say("Couldn't open the picture picker.", true); }
        }

        private async void PickFolder(string title, Action<string> then)
        {
            try
            {
                if (!StorageProvider.CanPickFolder) { Say("Couldn't open the folder picker.", true); return; }
                IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
                string path = folders.Count == 0 ? null : folders[0].TryGetLocalPath();
                bool local = folders.Count == 0 || path != null;
                foreach (IStorageFolder folder in folders) folder.Dispose();
                if (closed || folders.Count == 0) return;
                if (!local) Say("That needs to be a local folder.", true);
                else Program.Guard(delegate { then(path); });
            }
            catch (Exception) { if (!closed) Say("Couldn't open the folder picker.", true); }
        }

        private async void PickSave(string title, string suggested, string kind, string pattern, Action<string> then)
        {
            try
            {
                if (!StorageProvider.CanSave) { Say("Couldn't open the save picker.", true); return; }
                IStorageFile picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = title, SuggestedFileName = suggested, DefaultExtension = pattern.TrimStart('*', '.'),
                    FileTypeChoices = new[] { new FilePickerFileType(kind) { Patterns = new[] { pattern } } }
                });
                string path = picked == null ? null : picked.TryGetLocalPath();
                if (picked != null) picked.Dispose();
                if (closed || picked == null) return;
                if (path == null) Say("That needs to be a local file.", true);
                else Program.Guard(delegate { then(path); });
            }
            catch (Exception) { if (!closed) Say("Couldn't open the save picker.", true); }
        }

        private List<SKBitmap> ReadAll(IEnumerable<string> files)
        {
            List<SKBitmap> found = new List<SKBitmap>();
            try
            {
                foreach (string file in files) found.AddRange(draft.Read(Strip, file));
                return found;
            }
            catch (Exception error)
            {
                foreach (SKBitmap made in found) made.Dispose();
                if (error is ModException) { Say(error.Message, true); return null; }
                if (error is IOException || error is UnauthorizedAccessException || error is OutOfMemoryException) { Say("Couldn't read that: " + error.Message, true); return null; }
                throw;
            }
        }

        private void Put(int from, int replacing, List<SKBitmap> frames)
        {
            List<SKBitmap> into = Strip.Frames;
            if (into == null || frames == null) return;
            if (!Room(into.Count - replacing + frames.Count)) { foreach (SKBitmap made in frames) made.Dispose(); return; }
            preview.Frame = null;
            List<SKBitmap> gone = into.GetRange(from, replacing);
            into.RemoveRange(from, replacing);
            into.InsertRange(from, frames);
            preview.Keep(into);
            foreach (SKBitmap old in gone) old.Dispose();
            number = from;
            Changed();
            Say(frames.Count == 1 ? "1 frame in." : frames.Count + " frames in.", false);
        }

        private void Replace()
        {
            int where = number;
            AnimationDraft.Strip strip = Strip;
            PickPictures("Pick the picture for frame " + (where + 1), false, delegate(string[] files) { if (Strip == strip && where < strip.Count) Put(where, 1, ReadAll(files)); });
        }

        private void AddAfter()
        {
            int where = number;
            AnimationDraft.Strip strip = Strip;
            PickPictures("Pick pictures to add after frame " + (where + 1), true, delegate(string[] files)
            {
                if (Strip != strip) return;
                Array.Sort(files, Natural);
                Put(Math.Min(where + 1, strip.Count), 0, ReadAll(files));
            });
        }

        private void LoadStrip()
        {
            AnimationDraft.Strip strip = Strip;
            PickPictures("Pick a PNG strip for " + strip.Shown, false, delegate(string[] files) { if (Strip == strip) Put(0, strip.Count, ReadAll(files)); });
        }

        private void LoadFrames()
        {
            AnimationDraft.Strip strip = Strip;
            PickFolder("Pick a folder of PNG frames for " + strip.Shown + ". They go in by name.", delegate(string folder)
            {
                if (Strip != strip) return;
                string[] files = Directory.GetFiles(folder, "*.png");
                if (files.Length == 0) { Say("That folder has no PNG pictures.", true); return; }
                Array.Sort(files, Natural);
                Put(0, strip.Count, ReadAll(files));
            });
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
            PickSave("Save " + strip.Shown + " as a PNG strip", strip.File, "PNG pictures", "*.png", delegate(string path)
            {
                try
                {
                    using (SKBitmap joined = AnimationDraft.Joined(strip.Frames, strip.Wide, strip.High)) File.WriteAllBytes(path, AnimationDraft.Png(joined));
                    Say("Saved " + strip.Count + " frames side by side, " + strip.Wide + " x " + strip.High + " each. Draw on it and load it back.", false);
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                    Say("Couldn't save that: " + error.Message, true);
                }
            });
        }

        private void SaveFrames()
        {
            AnimationDraft.Strip strip = Strip;
            if (strip.Frames == null) return;
            PickFolder("Pick a folder for the frames of " + strip.Shown + ".", delegate(string folder)
            {
                try
                {
                    string lead = strip.File.Substring(0, strip.File.Length - 4);
                    for (int i = 0; i < strip.Frames.Count; i++) File.WriteAllBytes(Path.Combine(folder, lead + "_" + (i + 1).ToString("000") + ".png"), AnimationDraft.Png(strip.Frames[i]));
                    Say("Saved " + strip.Count + " frames as " + lead + "_001.png and on.", false);
                }
                catch (Exception error)
                {
                    if (!(error is IOException || error is UnauthorizedAccessException)) throw;
                    Say("Couldn't save those: " + error.Message, true);
                }
            });
        }

        private void Revert(bool own)
        {
            try
            {
                preview.Frame = null;
                draft.Reset(Strip, own);
                draft.Load(Strip);
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is ArgumentException)) throw;
                Say("Couldn't read " + Strip.Shown + ": " + error.Message, true);
            }
            Say(own ? Strip.Shown + " is her own art again." : Strip.Shown + " is back to how it was.", false);
            ShowFrame();
        }

        private void Save(bool wear)
        {
            string title = (skinName.Box.Text ?? "").Trim();
            if (title.Length == 0) { Say("Name your skin first.", true); return; }
            if (!draft.Dirty) { Say("Change a frame or a speed first.", true); return; }
            string id = Mods.IdFor(title);
            Mod taken = pet.ModList.All.Find(mod => mod.File.Id == id);
            if (taken != null && !taken.Skin) { Say(taken.File.Name + " already uses that name. Pick another.", true); return; }
            if (wear) Make(title, id, null);
            else PickSave("Save your skin", id + Mods.SkinExtension, "Capri skins", "*" + Mods.SkinExtension, delegate(string file) { Make(title, id, file); });
        }

        private void Make(string title, string id, string file)
        {
            bool wear = file == null;
            string temporary = Path.Combine(Path.GetTempPath(), "capri-animations-" + Guid.NewGuid().ToString("N") + Mods.SkinExtension);
            try
            {
                ModFrames[] rates;
                Dictionary<string, byte[]> strips = draft.Pack(out rates);
                ModFile details = new ModFile { Id = id, Name = Clamp(title), Version = "1", Description = draft.BaseName == null ? "Her animations, changed in her animation editor." : Clamp("Made from " + draft.BaseName) + " in her animation editor.", Frames = rates };
                Mods.PackSkin(wear ? temporary : file, details, strips);
                if (!wear) { Say("Saved " + Path.GetFileName(file) + ". Import it on any Capri, or share it in the mods directory.", false); return; }
                Mod worn = pet.ModList.WearOutfit(temporary);
                frame.AnimationsWorn();
                Say(worn == null ? "Couldn't wear it." : worn.File.Name + " is on. Find it in your mods to publish it.", worn == null);
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
    }
}
