using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using SkiaSharp;

namespace CapriDesktop
{
    internal sealed class MoveSheet : MainframeWindow
    {
        internal const int Wide = 460, Tall = 556;
        private readonly AnimationDraft draft;
        private readonly AnimationPreview preview = new AnimationPreview();
        private readonly NeonEntry name;
        private readonly NeonCaption count, speed, status;
        private readonly NeonButton slower, faster, loops, once;
        private readonly DispatcherTimer clock = new DispatcherTimer();
        private List<SKBitmap> frames;
        private int fps = Mods.MoveFps, shown;
        private bool loop, closed;
        internal AnimationDraft.Strip Made;
        internal Action<AnimationDraft.Strip> Added;
        internal Func<string, bool, string[]> PickFiles;
        internal Func<string, string> PickFolder;

        internal MoveSheet(Pet pet, AnimationDraft draft) : base("New move")
        {
            this.draft = draft;
            Steered = true; FitsScreen = true;
            ShowInTaskbar = false;
            PixelSize cell = Mods.MoveCell;
            Label("NEW MOVE", 24, 15, 260, 29, MainframeTheme.Rose, 20, true).Display = true;
            Button("", Wide - 54, 20, 26, 22, Close).Glyph = 2;
            Label("A move of her own, in frames " + cell.Width + " x " + cell.Height + " like her standing ones.", 28, 50, 404, 18, MainframeTheme.Faint, 34.0 / 3);
            Label("MOVE NAME", 28, 78, 200, 18, MainframeTheme.Rose, 34.0 / 3);
            name = Put(Page, new NeonEntry("Like happy_wave"), 28, 98, 404, 28, "Move name");
            Label("FRAMES FROM", 28, 140, 200, 18, MainframeTheme.Rose, 34.0 / 3);
            Button("Pictures...", 28, 162, 128, 26, FromPictures, "Make the move from PNG frames");
            Button("Strip...", 166, 162, 128, 26, FromStrip, "Make the move from one PNG of frames side by side");
            Button("Folder...", 304, 162, 128, 26, FromFolder, "Make the move from a folder of PNG frames, in name order");
            Put(Page, preview, 28, 200, 150, 248, "Your move");
            count = Label("No frames yet", 194, 200, 238, 20, MainframeTheme.White);
            Label("SPEED", 194, 232, 238, 18, MainframeTheme.Rose, 34.0 / 3);
            speed = Label("", 194, 252, 238, 20, MainframeTheme.White);
            slower = Button("Slower", 194, 276, 114, 26, delegate { Retime(-1); });
            faster = Button("Faster", 318, 276, 114, 26, delegate { Retime(1); });
            Label("PLAYS", 194, 316, 238, 18, MainframeTheme.Rose, 34.0 / 3);
            loops = Button("Loops", 194, 338, 114, 26, delegate { Loop(true); }, "Loops until you tell her to stop");
            once = Button("Once", 318, 338, 114, 26, delegate { Loop(false); }, "Plays once, then she's back to her idle");
            NeonCaption hint = Label("Once plays it and she's back to her idle. Loops keeps going until you tell her to stop.", 194, 372, 238, 54, MainframeTheme.Faint, 34.0 / 3);
            hint.Top = true;
            hint.Ellipsis = false;
            NeonButton add = Button("Add move", 194, 462, 114, 28, delegate { Add(); }, "Add this move to the skin you're making");
            add.Accent = MainframeTheme.Cyan; add.Loud = true;
            Button("Cancel", 318, 462, 114, 28, Close);
            status = Label("", 28, 502, 404, 38, MainframeTheme.Faint, 34.0 / 3);
            status.Top = true;
            status.Ellipsis = false;
            clock.Tick += delegate { Program.Guard(Step); };
            Opened += delegate { Fit(Wide, Tall, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0); };
            Closed += delegate
            {
                closed = true;
                clock.Stop();
                preview.Frame = null;
                preview.Clear();
                if (Made == null) Drop();
            };
            Loop(false);
            Retime(0);
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

        internal string MoveName { get { return name.Box.Text ?? ""; } set { name.Box.Text = value; } }
        internal string Said { get { return status.Text; } }
        internal int Frames { get { return frames == null ? 0 : frames.Count; } }
        internal int Fps { get { return fps; } }
        internal bool Loops { get { return loop; } }

        private void Say(string text, bool trouble)
        {
            status.Ink = trouble ? MainframeTheme.Rose : MainframeTheme.Faint;
            status.Text = text;
        }

        internal void Retime(int by)
        {
            int steps = by * (fps >= 30 ? 5 : fps >= 10 ? 2 : 1);
            fps = Math.Max(1, Math.Min(Mods.FastestFps, fps + steps));
            speed.Text = fps + " frames a second";
            slower.IsEnabled = fps > 1;
            faster.IsEnabled = fps < Mods.FastestFps;
            clock.Interval = TimeSpan.FromMilliseconds(Math.Max(15, 1000 / fps));
        }

        internal void Loop(bool on)
        {
            loop = on;
            loops.Picked = on;
            once.Picked = !on;
            loops.InvalidateVisual();
            once.InvalidateVisual();
        }

        private async void AskFiles(string title, bool many, Action<string[]> then)
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

        private async void AskFolder(string title, Action<string> then)
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

        internal void FromPictures()
        {
            Action<string[]> take = delegate(string[] files) { Array.Sort(files, AnimationSheet.Natural); Take(files); };
            if (PickFiles != null) { string[] files = PickFiles("Pick the PNG frames for your move", true); if (files != null) take(files); }
            else AskFiles("Pick the PNG frames for your move", true, take);
        }

        internal void FromStrip()
        {
            if (PickFiles != null) { string[] files = PickFiles("Pick a PNG strip of frames side by side", false); if (files != null) Take(files); }
            else AskFiles("Pick a PNG strip of frames side by side", false, Take);
        }

        internal void FromFolder()
        {
            Action<string> take = delegate(string folder)
            {
                string[] files;
                try { files = Directory.GetFiles(folder, "*.png"); }
                catch (IOException error) { Say("Couldn't read that folder: " + error.Message, true); return; }
                catch (UnauthorizedAccessException error) { Say("Couldn't read that folder: " + error.Message, true); return; }
                if (files.Length == 0) { Say("That folder has no PNG pictures.", true); return; }
                Array.Sort(files, AnimationSheet.Natural);
                Take(files);
            };
            if (PickFolder != null) { string folder = PickFolder("Pick a folder of PNG frames for your move. They go in by name."); if (folder != null) take(folder); }
            else AskFolder("Pick a folder of PNG frames for your move. They go in by name.", take);
        }

        private void Take(string[] files)
        {
            List<SKBitmap> found = new List<SKBitmap>();
            try
            {
                foreach (string file in files) found.AddRange(AnimationDraft.ReadMove(file));
            }
            catch (Exception error)
            {
                foreach (SKBitmap made in found) made.Dispose();
                if (error is ModException) { Say(error.Message, true); return; }
                if (error is IOException || error is UnauthorizedAccessException || error is OutOfMemoryException) { Say("Couldn't read that: " + error.Message, true); return; }
                throw;
            }
            if (found.Count > Mods.MostFrames)
            {
                foreach (SKBitmap made in found) made.Dispose();
                Say("A move can have up to " + Mods.MostFrames + " frames, and that's " + found.Count + ".", true);
                return;
            }
            Drop();
            frames = found;
            shown = 0;
            count.Text = frames.Count == 1 ? "1 frame" : frames.Count + " frames";
            preview.Keep(frames);
            preview.Frame = frames[0];
            if (!closed) clock.Start();
            Say(frames.Count == 1 ? "1 frame in." : frames.Count + " frames in.", false);
        }

        private void Step()
        {
            if (frames == null || frames.Count == 0) return;
            shown = (shown + 1) % frames.Count;
            preview.Frame = frames[shown];
        }

        private void Drop()
        {
            clock.Stop();
            preview.Frame = null;
            preview.Keep(null);
            if (frames != null) foreach (SKBitmap frame in frames) frame.Dispose();
            frames = null;
        }

        internal static string NameFrom(string text)
        {
            return System.Text.RegularExpressions.Regex.Replace((text ?? "").Trim().ToLowerInvariant(), @"[\s-]+", "_");
        }

        internal bool Add()
        {
            string named = NameFrom(name.Box.Text);
            string trouble = named.Length == 0 ? "Name your move first." : draft.MoveTrouble(named);
            if (trouble == null && (frames == null || frames.Count == 0)) trouble = "Pick its frames first: pictures, a strip or a folder.";
            if (trouble != null) { Say(trouble, true); return false; }
            clock.Stop();
            preview.Frame = null;
            preview.Keep(null);
            Made = draft.AddMove(named, frames, fps, loop);
            frames = null;
            if (Added != null) Added(Made);
            Close();
            return true;
        }
    }
}
