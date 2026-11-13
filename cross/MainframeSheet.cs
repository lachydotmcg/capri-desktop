using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class ModPreview : Control
    {
        internal Bitmap Picture;

        public override void Render(DrawingContext context)
        {
            Rect box = new Rect(Bounds.Size);
            context.DrawRectangle(MainframeTheme.Well, new Pen(MainframeTheme.WellEdge, 1), box);
            if (Picture == null) MainframeTheme.Write(context, "NO PREVIEW", box, MainframeTheme.Faint, 12, false, TextAlignment.Center);
            else
            {
                double scale = Math.Max(box.Width / Picture.Size.Width, box.Height / Picture.Size.Height);
                double width = Picture.Size.Width * scale, height = Picture.Size.Height * scale;
                using (context.PushClip(box)) context.DrawImage(Picture, new Rect((box.Width - width) / 2, (box.Height - height) / 2, width, height));
            }
        }
    }

    internal sealed class ModsWindow : MainframeWindow
    {
        internal const int Wide = 760, Tall = 884;
        private const int Cards = 2, Rows = 4;
        private readonly Pet pet;
        private readonly NeonEntry search;
        private readonly NeonButton refresh, back, next, installedBack, installedNext;
        private readonly NeonCaption directoryNote, status, page, installedPage;
        private readonly NeonButton[] kinds = new NeonButton[4];
        private readonly ModPreview[] previews = new ModPreview[Cards];
        private readonly NeonCaption[] titles = new NeonCaption[Cards], authors = new NeonCaption[Cards], details = new NeonCaption[Cards], metas = new NeonCaption[Cards], numbers = new NeonCaption[Cards];
        private readonly NeonButton[] downloads = new NeonButton[Cards];
        private readonly NeonPicker[] ratings = new NeonPicker[Cards];
        private readonly NeonCaption[] names = new NeonCaption[Rows], abouts = new NeonCaption[Rows];
        private readonly NeonButton[] switches = new NeonButton[Rows], removes = new NeonButton[Rows];
        private readonly NeonEntry[] colours = new NeonEntry[ArtColors.Slots];
        private readonly Dictionary<string, Bitmap> pictures = new Dictionary<string, Bitmap>();
        private readonly HashSet<string> fetching = new HashSet<string>();
        private readonly HashSet<string> failed = new HashSet<string>();
        private List<ModListing> listings = new List<ModListing>(), shown = new List<ModListing>();
        private int first, installedFirst, kind, ticket;
        private bool closed, syncing, working, loading, statsLive;
        internal string StatusText { get { return status.Text; } }
        internal string DirectoryText { get { return directoryNote.Text; } }
        internal bool Working { get { return working || loading; } }
        internal bool PreviewsPending { get { return fetching.Count != 0; } }
        internal string PageText { get { return page.Text; } }
        internal string InstalledPageText { get { return installedPage.Text; } }

        internal ModsWindow(Pet owner) : base("Mods")
        {
            pet = owner;
            ShowInTaskbar = false;
            Label("MODS", 24, 15, 320, 29, MainframeTheme.Rose, 20, true).Display = true;
            Button("", 710, 20, 26, 22, Close).Glyph = 2;
            Put(Page, new NeonHead { Text = "DIRECTORY" }, 14, 52, 352, 24);
            Put(Page, new NeonHead { Text = "INSTALLED" }, 394, 52, 352, 24);
            search = Put(Page, new NeonEntry("Search name, author or description"), 28, 84, 236, 28, "Search mods");
            // Typing and Enter go through Program.Guard like the buttons, so a bug behind a box is written down instead of ending her.
            search.Box.TextChanged += delegate { Program.Guard(delegate { first = 0; FillDirectory(); }); };
            refresh = Button("Refresh", 272, 84, 84, 28, LoadMods);
            for (int i = 0; i < kinds.Length; i++)
            {
                int choice = i;
                kinds[i] = Button(ModDirectory.Kinds[i], 28 + i * 84, 120, 76, 24, delegate { PickKind(choice); });
            }
            directoryNote = Label("Browse mods, or import a file.", 28, 152, 328, 32);
            for (int i = 0; i < Cards; i++)
            {
                int card = i, y = 194 + i * 306;
                previews[i] = Put(Page, new ModPreview(), 28, y, 328, 128, "Mod preview");
                titles[i] = Label("", 28, y + 136, 328, 20, MainframeTheme.White, 44.0 / 3, true);
                authors[i] = Label("", 28, y + 158, 328, 18, MainframeTheme.Cyan);
                details[i] = Label("", 28, y + 180, 328, 32);
                metas[i] = Label("", 28, y + 216, 328, 18);
                numbers[i] = Label("", 28, y + 238, 328, 18);
                downloads[i] = Button("Download", 28, y + 262, 100, 28, delegate { Install(card); });
                downloads[i].Accent = MainframeTheme.Cyan;
                ratings[i] = Put(Page, new NeonPicker(), 136, y + 262, 220, 28, "Rate mod");
                ratings[i].Check = false;
                ratings[i].Options(new[] { "1 star", "2 stars", "3 stars", "4 stars", "5 stars" });
                ratings[i].Changed += delegate(int stars) { Rate(card, stars + 1); };
            }
            back = Button("Back", 28, 808, 60, 24, delegate { first -= Cards; FillDirectory(); });
            page = Label("", 96, 808, 192, 24); page.Align = TextAlignment.Center;
            next = Button("Next", 296, 808, 60, 24, delegate { first += Cards; FillDirectory(); });
            Button("View on GitHub", 28, 846, 156, 28, delegate { if (Program.Smoke == null) Pet.Browse(ModDirectory.Page); });
            NeonButton import = Button("Import mod", 408, 84, 160, 28, delegate { });
            import.Accent = MainframeTheme.Cyan;
            import.Click += async delegate
            {
                try
                {
                    if (!StorageProvider.CanOpen) { Say("Couldn't open the mod picker."); return; }
                    System.Collections.Generic.IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions {
                        Title = "Import mod", AllowMultiple = false,
                        FileTypeFilter = new[] { new FilePickerFileType("Capri mods") { Patterns = new[] { "*.capri-mod", "*.capri-skin", "*.capri-skill", "*.capri-code" } } } });
                    if (!closed && files.Count > 0)
                    {
                        string path = files[0].TryGetLocalPath();
                        if (path == null) Say("That mod needs to be a local file.");
                        else ImportMod(path);
                    }
                    foreach (IStorageFile file in files) file.Dispose();
                }
                catch (Exception) { if (!closed) Say("Couldn't open the mod picker."); }
            };
            Button("Mods folder", 576, 84, 160, 28, delegate
            {
                try { Directory.CreateDirectory(pet.ModList.Folder); if (Program.Smoke == null) Pet.Browse(pet.ModList.Folder); }
                catch (Exception error) { Say("Couldn't open the mods folder. " + error.Message); }
            });
            for (int i = 0; i < Rows; i++)
            {
                int row = i, y = 130 + i * 72;
                names[i] = Label("", 408, y, 188, 22, MainframeTheme.White, 44.0 / 3, true);
                switches[i] = Button("On", 604, y, 56, 22, delegate { SwitchMod(row); });
                removes[i] = Button("Remove", 668, y, 68, 22, delegate { RemoveMod(row); });
                abouts[i] = Label("", 408, y + 28, 328, 32);
            }
            installedBack = Button("Back", 408, 434, 60, 24, delegate { installedFirst -= Rows; Sync(); });
            installedPage = Label("", 476, 434, 192, 24); installedPage.Align = TextAlignment.Center;
            installedNext = Button("Next", 676, 434, 60, 24, delegate { installedFirst += Rows; Sync(); });
            status = Label("Import a settings mod or a skin.", 408, 470, 328, 42);
            if (pet.ModList.Problems.Count > 0) status.Text = pet.ModList.Problems[0];
            Put(Page, new NeonHead { Text = "COLORS" }, 394, 528, 352, 24);
            for (int i = 0; i < colours.Length; i++)
            {
                Label(ArtColors.Names[i], 408, 566 + i * 32, 84, 28);
                colours[i] = Put(Page, new NeonEntry(ArtColors.Usual[i]), 500, 566 + i * 32, 236, 28, ArtColors.Names[i] + " color");
                colours[i].Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Program.Guard(SaveColours); } };
            }
            Button("Save colors", 408, 738, 160, 28, SaveColours).Accent = MainframeTheme.Cyan;
            Button("Reset colors", 576, 738, 160, 28, delegate
            {
                try { pet.ModList.ResetColours(); Say("Her colors are back."); Sync(); }
                catch (Exception error) { Say(error.Message); }
            });
            Opened += delegate { Sync(); if (Program.Smoke == null) LoadMods(); };
            pet.SettingsChanged += Sync;
            Closed += delegate
            {
                closed = true; pet.SettingsChanged -= Sync;
                foreach (NeonPicker rating in ratings) rating.CloseMenu();
                foreach (Bitmap picture in pictures.Values) picture.Dispose();
                pictures.Clear();
            };
            Sync();
        }

        private NeonCaption Label(string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false)
        {
            return Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Faint, Size = size, Bold = bold, Ellipsis = true }, x, y, width, height);
        }

        private NeonButton Button(string text, int x, int y, int width, int height, Action action)
        {
            NeonButton button = Put(Page, new NeonButton { Text = text }, x, y, width, height, text.Length == 0 ? "Close" : text);
            button.Click += delegate { action(); };
            return button;
        }

        private void Say(string line) { status.Text = line; status.InvalidateVisual(); }
        private void DirectorySays(string line) { directoryNote.Text = line; directoryNote.InvalidateVisual(); }

        internal void Sync()
        {
            if (closed || syncing) return;
            syncing = true;
            try
            {
                FillInstalled(); FillDirectory();
                refresh.IsEnabled = !loading && !working;
                string[] now = pet.ModList.ColoursNow;
                for (int i = 0; i < colours.Length; i++) if (!colours[i].Box.IsKeyboardFocusWithin) colours[i].Box.Text = now[i] ?? "";
                Fit(Wide, Tall, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0);
            }
            finally { syncing = false; }
        }

        internal void LoadMods()
        {
            if (closed || loading || working) return;
            loading = true; statsLive = false; refresh.IsEnabled = false;
            failed.Clear();
            int mine = ++ticket;
            DirectorySays("Loading mods...");
            FillDirectory();
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ModListing> found = null;
                string problem = null; bool stats = false;
                try
                {
                    found = ModDirectory.Browse();
                    try { ModDirectory.Apply(found, ModDirectory.Stats()); stats = true; }
                    catch (Exception) { problem = "Download counts and ratings are unavailable."; }
                }
                catch (Exception error) { problem = error is ModException ? error.Message : "Couldn't reach the mods directory."; }
                Dispatcher.UIThread.Post(delegate
                {
                    if (closed || mine != ticket) return;
                    loading = false; refresh.IsEnabled = true; statsLive = stats;
                    listings = found ?? new List<ModListing>(); first = 0;
                    DirectorySays(problem ?? (listings.Count == 0 ? "No mods in the directory yet." : "Pick a mod to download."));
                    FillDirectory();
                });
            });
        }

        internal void PickKind(int choice) { kind = choice; first = 0; FillDirectory(); }
        // Avalonia raises TextChanged after the new text renders, so a search from here fills the cards at once.
        internal void Search(string words) { search.Box.Text = words; first = 0; FillDirectory(); }
        internal void NextPage() { first += Cards; FillDirectory(); }
        internal void NextInstalledPage() { installedFirst += Rows; Sync(); }
        internal string TitleOf(int card) { return titles[card].IsVisible ? titles[card].Text : null; }
        internal string AuthorOf(int card) { return authors[card].IsVisible ? authors[card].Text : null; }
        internal string AboutOf(int card) { return details[card].IsVisible ? details[card].Text : null; }
        internal bool HasPreview(int card) { return previews[card].Picture != null; }
        internal bool CanDownload(int card) { return downloads[card].IsVisible && downloads[card].IsEnabled; }
        internal string DownloadText(int card) { return downloads[card].Text; }
        internal string MetaOf(int card) { return metas[card].Text; }

        private void FillDirectory()
        {
            shown = ModDirectory.Filter(listings, search.Box.Text, kind);
            first = Math.Max(0, Math.Min(first, Math.Max(0, (shown.Count - 1) / Cards) * Cards));
            for (int i = 0; i < kinds.Length; i++) { kinds[i].Picked = i == kind; kinds[i].InvalidateVisual(); }
            for (int i = 0; i < Cards; i++)
            {
                ModListing mod = first + i < shown.Count ? shown[first + i] : null;
                previews[i].IsVisible = titles[i].IsVisible = metas[i].IsVisible = numbers[i].IsVisible = downloads[i].IsVisible = ratings[i].IsVisible = mod != null;
                authors[i].IsVisible = mod != null && mod.Author.Length > 0;
                details[i].IsVisible = mod != null && mod.About.Length > 0;
                if (mod == null) { previews[i].Picture = null; continue; }
                bool installed = pet.ModList.All.Exists(m => m.File.Id == mod.Id);
                titles[i].Text = mod.Title;
                authors[i].Text = mod.Author.Length > 0 ? "by " + mod.Author : "";
                details[i].Text = mod.About;
                ToolTip.SetTip(previews[i], new TextBlock { Text = mod.Title + (mod.Author.Length > 0 ? "\nby " + mod.Author : "") + (mod.About.Length > 0 ? "\n" + mod.About : ""), MaxWidth = 328, TextWrapping = TextWrapping.Wrap, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size, Foreground = MainframeTheme.White });
                metas[i].Text = mod.Kind + (installed ? " Installed." : "");
                numbers[i].Text = statsLive ? ModDirectory.DownloadLine(mod) + "  |  " + ModDirectory.RatingLine(mod) : loading ? "Loading counts and ratings..." : "Counts and ratings unavailable.";
                downloads[i].Text = installed ? "Reinstall" : "Download";
                downloads[i].IsEnabled = !working;
                ratings[i].IsEnabled = !working && statsLive && installed;
                ratings[i].Text = "Rate this mod";
                Bitmap picture = null;
                if (mod.PreviewSha != null) pictures.TryGetValue(mod.PreviewSha, out picture);
                previews[i].Picture = picture;
                foreach (Control control in new Control[] { titles[i], authors[i], details[i], metas[i], numbers[i], downloads[i], ratings[i], previews[i] }) control.InvalidateVisual();
                if (picture == null && mod.PreviewSha != null) LoadPreview(mod);
            }
            page.Text = shown.Count == 0 ? "No matching mods" : "Page " + (first / Cards + 1) + " of " + ((shown.Count + Cards - 1) / Cards);
            page.InvalidateVisual();
            back.IsEnabled = first > 0; next.IsEnabled = first + Cards < shown.Count;
        }

        private void LoadPreview(ModListing mod)
        {
            // Fast paging must not queue every page's image downloads and decodes at once.
            if (fetching.Count >= Cards || failed.Contains(mod.PreviewSha) || !fetching.Add(mod.PreviewSha)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                Bitmap made = null;
                try { made = ModDirectory.Picture(ModDirectory.Preview(mod, Path.Combine(pet.ModList.Folder, "previews"))); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(delegate
                {
                    if (closed) { if (made != null) made.Dispose(); return; }
                    fetching.Remove(mod.PreviewSha);
                    if (made == null) failed.Add(mod.PreviewSha);
                    else pictures[mod.PreviewSha] = made;
                    FillDirectory();
                    // Paging through many mods used to keep every decoded preview until this window closed.
                    foreach (string key in new List<string>(pictures.Keys))
                    {
                        if (pictures.Count <= 8) break;
                        Bitmap old = pictures[key];
                        bool visible = false;
                        foreach (ModPreview preview in previews) if (preview.Picture == old) visible = true;
                        if (visible) continue;
                        pictures.Remove(key);
                        old.Dispose();
                    }
                });
            });
        }

        private void FillInstalled()
        {
            List<Mod> all = pet.ModList.All;
            installedFirst = Math.Max(0, Math.Min(installedFirst, Math.Max(0, (all.Count - 1) / Rows) * Rows));
            for (int i = 0; i < Rows; i++)
            {
                Mod mod = installedFirst + i < all.Count ? all[installedFirst + i] : null;
                names[i].IsVisible = abouts[i].IsVisible = switches[i].IsVisible = removes[i].IsVisible = mod != null;
                if (mod == null) continue;
                names[i].Text = mod.File.Name;
                abouts[i].Text = (mod.File.Author == null ? "" : "by " + mod.File.Author + "  ") + (mod.File.Description ?? (mod.Skin ? (mod.File.Accessory ? "Accessory" : "Skin") : mod.Skill ? "Skill" : "Settings"));
                switches[i].Text = mod.On ? "On" : "Off"; switches[i].Picked = mod.On;
                switches[i].IsEnabled = !working; removes[i].IsEnabled = !working;
                names[i].InvalidateVisual(); abouts[i].InvalidateVisual(); switches[i].InvalidateVisual();
            }
            installedPage.Text = all.Count == 0 ? "No mods installed" : "Page " + (installedFirst / Rows + 1) + " of " + ((all.Count + Rows - 1) / Rows);
            installedPage.InvalidateVisual();
            installedBack.IsEnabled = installedFirst > 0; installedNext.IsEnabled = installedFirst + Rows < all.Count;
        }

        // Before a code mod runs for the first time, or after it changed, they read what it is and say yes. The smoke test answers
        // through ModAsk instead.
        internal Func<ModFile, string, bool> ModAsk;

        private bool Ask(ModFile mod, string hash)
        {
            Func<ModFile, string, bool> ask = ModAsk;
            return ask != null ? ask(mod, hash) : AskToRun(mod, hash);
        }

        // Her approve box, the same words as on Windows, waited on here so the mod list stays as it was until they answer.
        private bool AskToRun(ModFile mod, string hash)
        {
            bool yes = false;
            Window box = new Window
            {
                Title = "Run this code mod?", Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Background = MainframeTheme.Ground
            };
            NeonButton run = new NeonButton { Text = "Run it", Width = 120, Height = 28, Accent = MainframeTheme.Rose };
            NeonButton no = new NeonButton { Text = "Don't run", Width = 120, Height = 28, Accent = MainframeTheme.Cyan, IsDefault = true, IsCancel = true };
            run.Click += delegate { yes = true; box.Close(); };
            no.Click += delegate { box.Close(); };
            StackPanel buttons = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right, Margin = new Thickness(16, 0, 16, 16) };
            buttons.Children.Add(no);
            buttons.Children.Add(run);
            StackPanel all = new StackPanel();
            all.Children.Add(new TextBlock { Text = Mods.Consent(mod, hash), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), Foreground = MainframeTheme.White, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size });
            all.Children.Add(buttons);
            box.Content = all;
            using (CancellationTokenSource done = new CancellationTokenSource())
            {
                box.Closed += delegate { done.Cancel(); };
                box.ShowDialog(this);
                no.Focus();
                Dispatcher.UIThread.MainLoop(done.Token);
            }
            return yes;
        }

        internal bool ImportMod(string file)
        {
            try
            {
                Mod mod = pet.ModList.Import(file, Ask);
                // A file removed during import can be missing from the reloaded mod list.
                if (mod == null) throw new ModException("Couldn't load that mod. Check the mods folder.");
                Say(mod.File.Name + " is on."); Sync(); return true;
            }
            catch (Exception error)
            {
                if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                Say(error.Message); Sync(); return false;
            }
        }

        internal void SwitchMod(int row)
        {
            try
            {
                Mod mod = pet.ModList.All[installedFirst + row];
                pet.ModList.Set(mod, !mod.On, Ask);
                Say(mod.File.Name + (mod.On ? " is on." : " is off."));
            }
            catch (Exception error) { Say(error.Message); }
            Sync();
        }

        internal void RemoveMod(int row)
        {
            try
            {
                Mod mod = pet.ModList.All[installedFirst + row];
                pet.ModList.Remove(mod); Say("Removed " + mod.File.Name + ".");
            }
            catch (Exception error) { Say(error.Message); }
            Sync();
        }

        internal void SaveColours()
        {
            try
            {
                ModColors picked = new ModColors();
                string[] values = new string[colours.Length];
                bool any = false;
                for (int i = 0; i < values.Length; i++) { string value = (colours[i].Box.Text ?? "").Trim(); values[i] = value.Length == 0 ? null : value; any |= values[i] != null; }
                picked.Hair = values[0]; picked.Accent = values[1]; picked.Eyes = values[2]; picked.Edges = values[3]; picked.Body = values[4];
                pet.ModList.SaveColours(picked, any);
                Say("Her colors are saved."); Sync();
            }
            catch (Exception error) { Say(error.Message); }
        }

        internal void Install(int card)
        {
            if (closed || working || first + card >= shown.Count) return;
            ModListing mod = shown[first + card];
            working = true; Say("Downloading " + mod.Title + "..."); Sync();
            ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null, problem = null;
                try { file = ModDirectory.Download(mod, Path.Combine(Path.GetTempPath(), "capri-mod-downloads")); }
                catch (Exception error) { problem = error is ModException ? error.Message : "Couldn't download that mod."; }
                Dispatcher.UIThread.Post(delegate
                {
                    try
                    {
                        if (closed) return;
                        if (problem != null) Say(problem);
                        else if (ImportMod(file))
                        {
                            mod.Downloads++;
                            ThreadPool.QueueUserWorkItem(delegate { try { ModDirectory.CountDownload(mod.Folder); } catch (Exception error) { Brain.Log("mods", -1, error.Message); } });
                        }
                    }
                    finally { if (file != null) ModDirectory.Clean(file); working = false; Sync(); }
                });
            });
        }

        internal void Rate(int card, int stars)
        {
            if (closed || working || !statsLive || stars < 1 || stars > 5 || first + card >= shown.Count) return;
            ModListing mod = shown[first + card];
            if (!pet.ModList.All.Exists(m => m.File.Id == mod.Id)) return;
            working = true; Say("Saving your rating..."); Sync();
            ThreadPool.QueueUserWorkItem(delegate
            {
                ModStats stats = null; string problem = null;
                try { stats = ModDirectory.Rate(mod.Folder, stars); }
                catch (Exception error) { problem = error is ModException ? error.Message : "Couldn't save your rating."; }
                Dispatcher.UIThread.Post(delegate
                {
                    if (closed) return;
                    working = false;
                    if (stats != null) { mod.Downloads = stats.Downloads; mod.Rating = stats.Rating; mod.Votes = stats.Votes; }
                    Say(problem ?? "Rating saved."); Sync();
                });
            });
        }

        internal string Crowded()
        {
            Rect pageBox = new Rect(0, 0, Wide, Tall);
            for (int i = 0; i < Page.Children.Count; i++)
            {
                Control a = Page.Children[i];
                if (!a.IsVisible) continue;
                Rect box = Box(a);
                if (!pageBox.Contains(box)) return "Outside: " + NameOf(a);
                NeonCaption caption = a as NeonCaption;
                NeonButton button = a as NeonButton;
                NeonEntry entry = a as NeonEntry;
                string wrong = caption != null ? caption.TextFits() : button != null ? button.TextFits() : entry != null ? entry.TextFits() : null;
                if (wrong != null) return "Text: " + wrong;
                for (int j = i + 1; j < Page.Children.Count; j++)
                {
                    Control b = Page.Children[j]; if (!b.IsVisible) continue;
                    Rect overlap = box.Intersect(Box(b));
                    if (overlap.Width > 0.5 && overlap.Height > 0.5) return NameOf(a) + " / " + NameOf(b);
                }
            }
            return null;
        }

        private static Rect Box(Control control)
        {
            double x = Canvas.GetLeft(control), y = Canvas.GetTop(control);
            NeonHead head = control as NeonHead;
            if (head != null) return new Rect(x + 14, y + 5, MainframeTheme.Words(head.Text, 40.0 / 3, true).Width, 19);
            return new Rect(x, y, control.Width, control.Height);
        }

        private static string NameOf(Control control)
        {
            return Avalonia.Automation.AutomationProperties.GetName(control) ?? (control as NeonCaption == null ? control.GetType().Name : ((NeonCaption)control).Text);
        }
    }

    internal sealed class RemoteSheet : MainframeWindow
    {
        internal const int Wide = 600, Tall = 704, PhonesPerPage = 5;
        private readonly Func<RemoteView> read;
        private readonly Action<bool> change;
        private readonly Action newCode, closeCode, allowPhone, denyPhone, removePhones;
        private readonly Action<string> revokePhone;
        private readonly Func<string, bool> changeHost;
        private readonly DispatcherTimer clock = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private readonly NeonSwitch power;
        private readonly NeonCaption status, idle, qrNote, askTitle, askName, codeLabel, askCode, askCheck, empty, page, note, revokeNote;
        private readonly NeonCaption[] names = new NeonCaption[PhonesPerPage], states = new NeonCaption[PhonesPerPage], sources = new NeonCaption[PhonesPerPage];
        private readonly NeonButton[] revokes = new NeonButton[PhonesPerPage];
        private readonly NeonButton pair, showQr, allow, deny, back, next, removeAll;
        private readonly NeonEntry host;
        private readonly RemoteQrBox qr;
        private RemoteView view = new RemoteView();
        private string armedId, lastQr, revealedQr, lastHost, refusedText, refusedHost, refusedNote;
        private int first, armedAt;
        private bool syncing, closed, armedAll, streaming, hostRefused;
        internal readonly Dictionary<string, Control> Rows = new Dictionary<string, Control>();

        internal RemoteSheet(Func<RemoteView> read, Action<bool> change, Action newCode, Action closeCode, Action allow, Action deny, Action<string> revoke, Action removeAll, Func<string, bool> changeHost) : base("Remote")
        {
            this.read = read; this.change = change; this.newCode = newCode; this.closeCode = closeCode;
            allowPhone = allow; denyPhone = deny; revokePhone = revoke; removePhones = removeAll; this.changeHost = changeHost;
            ShowInTaskbar = false;
            Caption("Title", "REMOTE", 24, 15, 480, 29, MainframeTheme.Rose, 20, true).Display = true;
            Button("Close", "", 550, 20, 26, 22, Close).Glyph = 2;
            Caption("About", "Chat with Capri from your phone", 28, 52, 544, 20);
            power = Put(Page, new NeonSwitch { Text = "Remote", Hint = "Sealed end to end", Switch = 120 }, 28, 80, 544, 24, "Capri Remote"); Rows["Remote"] = power;
            power.IsCheckedChanged += delegate { if (!syncing && !closed) { this.change(power.Checked); Sync(); } };
            status = Caption("Status", "", 28, 112, 544, 24, MainframeTheme.Faint, MainframeTheme.Size, true);
            Put(Page, new NeonHead { Text = "QR CODE" }, 14, 148, 284, 24, "QR code");
            Put(Page, new NeonHead { Text = "PHONES" }, 302, 148, 284, 24, "Phones");
            pair = Button("Pair", "Pair a phone", 178, 148, 114, 24, delegate { this.newCode(); }); pair.Accent = MainframeTheme.Cyan; pair.Loud = true;
            qr = Put(Page, new RemoteQrBox(this), 34, 184, 252, 252, "Pairing QR code"); Rows["QR"] = qr;
            qrNote = Caption("QR note", "", 28, 444, 264, 20, MainframeTheme.Faint, 34.0 / 3); qrNote.Align = TextAlignment.Center;
            idle = Caption("Idle", "", 28, 192, 264, 44);
            showQr = Button("Show QR", "Show QR", 106, 248, 108, 28, delegate { revealedQr = view.Qr; }); showQr.Accent = MainframeTheme.Cyan;
            askTitle = Caption("Ask title", "ALLOW THIS PHONE?", 28, 192, 264, 24, MainframeTheme.Rose, 14, true);
            askName = Caption("Ask name", "", 28, 220, 264, 36, MainframeTheme.White, 44.0 / 3, true);
            codeLabel = Caption("Code label", "CODE", 28, 264, 264, 18, MainframeTheme.Faint, 34.0 / 3);
            askCode = Caption("Code", "", 28, 286, 264, 46, MainframeTheme.Cyan, 104.0 / 3); askCode.Display = true;
            askCheck = Caption("Ask check", "Same code on the phone?", 28, 340, 264, 24);
            this.allow = Button("Allow", "Allow", 28, 376, 128, 28, delegate { allowPhone(); }); this.allow.Accent = MainframeTheme.Cyan; this.allow.Loud = true;
            this.deny = Button("Deny", "Deny", 164, 376, 128, 28, delegate { denyPhone(); }); this.deny.Accent = MainframeTheme.Hot;
            for (int i = 0; i < PhonesPerPage; i++)
            {
                int row = i, y = 184 + 58 * i;
                names[i] = Caption("Phone " + (i + 1), "", 316, y, 176, 20, MainframeTheme.White, 40.0 / 3, true);
                states[i] = Caption("Phone state " + (i + 1), "", 316, y + 20, 176, 16, MainframeTheme.Faint, 34.0 / 3);
                sources[i] = Caption("Phone source " + (i + 1), "", 316, y + 36, 256, 16, MainframeTheme.Faint, 34.0 / 3);
                revokes[i] = Button("Revoke " + (i + 1), "Revoke", 500, y + 5, 72, 26, delegate { Revoke(row); }); revokes[i].Accent = MainframeTheme.Hot;
            }
            empty = Caption("Empty", "No phones yet", 316, 192, 256, 24);
            back = Button("Back", "Back", 316, 480, 58, 24, delegate { first -= PhonesPerPage; });
            page = Caption("Page", "", 382, 480, 124, 24, MainframeTheme.Faint, 34.0 / 3); page.Align = TextAlignment.Center;
            next = Button("Next", "Next", 514, 480, 58, 24, delegate { first += PhonesPerPage; });
            this.removeAll = Button("Remove all phones", "Remove all phones", 316, 512, 256, 28, RemoveAll); this.removeAll.Accent = MainframeTheme.Hot;
            AutomationProperties.SetName(this.removeAll, "Remove all phones and pair again");
            revokeNote = Caption("Revoke note", "", 316, 546, 256, 32, MainframeTheme.Faint, 34.0 / 3);
            Put(Page, new NeonHead { Text = "RELAY" }, 14, 588, 572, 24, "Relay");
            host = Put(Page, new NeonEntry(RemoteLink.DefaultHost), 28, 620, 460, 28, "Relay host"); Rows["Relay host"] = host;
            AutomationProperties.SetName(host.Box, "Relay host");
            host.Box.KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled = true; Program.Guard(SaveHost); } };
            Button("Save relay host", "Save", 496, 620, 76, 28, SaveHost);
            note = Caption("Note", "", 28, 656, 544, 24, MainframeTheme.Cyan);
            List<Control> keys = new List<Control>();
            foreach (Control control in Page.Children) if (control.Focusable || control == host) keys.Add(control);
            keys.Sort(delegate(Control a, Control b) { int row = Canvas.GetTop(a).CompareTo(Canvas.GetTop(b)); return row != 0 ? row : Canvas.GetLeft(a).CompareTo(Canvas.GetLeft(b)); });
            for (int i = 0; i < keys.Count; i++) { keys[i].TabIndex = i; if (keys[i] == host) host.Box.TabIndex = i; }
            clock.Tick += Tick;
            Opened += delegate { Sync(); clock.Start(); };
            ScalingChanged += delegate { qr.InvalidateVisual(); };
            SizeChanged += delegate { qr.InvalidateVisual(); };
            Scroll.ScrollChanged += delegate { qr.InvalidateVisual(); };
            Closed += delegate { closed = true; clock.Stop(); clock.Tick -= Tick; qr.Url = null; Program.Guard(this.closeCode); };
            Sync();
        }

        private NeonCaption Caption(string name, string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false)
        {
            NeonCaption caption = Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Faint, Size = size, Bold = bold, Ellipsis = true }, x, y, width, height, text.Length > 0 ? text : name);
            Rows[name] = caption; return caption;
        }

        private NeonButton Button(string name, string text, int x, int y, int width, int height, Action action)
        {
            NeonButton button = Put(Page, new NeonButton { Text = text }, x, y, width, height, name);
            Rows[name] = button;
            button.Click += delegate { if (closed) return; action(); Sync(); };
            return button;
        }

        private static void Say(NeonCaption caption, string text)
        {
            caption.Text = text ?? "";
            AutomationProperties.SetName(caption, caption.Text);
        }

        private static void Tip(Control control, string text)
        {
            TextBlock tip = ToolTip.GetTip(control) as TextBlock;
            if (tip != null && tip.Text == text) return;
            ToolTip.SetTip(control, new TextBlock { Text = text, MaxWidth = 320, TextWrapping = TextWrapping.Wrap, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size, Foreground = MainframeTheme.White });
        }

        private void Tick(object sender, EventArgs e) { Program.Guard(Sync); }

        internal void Sync()
        {
            if (closed || syncing) return;
            syncing = true;
            try
            {
                view = read() ?? new RemoteView();
                MainframeWindow frame = Owner as MainframeWindow;
                double zoom = frame == null ? Zoom : frame.Zoom;
                if (double.IsNaN(Page.Width) || Zoom != zoom) Fit(Wide, Tall, zoom);
                if (streaming != Hands.Streaming || lastQr != view.Qr) revealedQr = null;
                streaming = Hands.Streaming; lastQr = view.Qr;
                first = Math.Max(0, Math.Min(first, Math.Max(0, (view.Phones.Count - 1) / PhonesPerPage) * PhonesPerPage));
                if (unchecked(Environment.TickCount - armedAt) > 4000) { armedId = null; armedAll = false; }
                if (armedId != null && !view.Phones.Exists(delegate(RemoteDevice phone) { return phone.Id == armedId; })) armedId = null;
                power.Checked = view.On;
                IBrush tone;
                Say(status, StatusLine(view, out tone)); status.Ink = tone; status.InvalidateVisual();
                bool asking = view.Ask != null, linking = !asking && view.LinkFor != null;
                bool expired = view.Expired || view.Qr != null && view.QrSeconds <= 0;
                bool showing = view.On && view.Ready && !asking && !linking && !expired && view.Qr != null;
                bool hidden = showing && streaming && revealedQr != view.Qr;
                qr.Url = showing && !hidden ? view.Qr : null; qr.IsVisible = showing && !hidden;
                showQr.IsVisible = hidden;
                qrNote.IsVisible = showing && !hidden;
                Say(qrNote, showing ? "SCAN WITH YOUR PHONE \u00b7 " + view.QrSeconds + (view.QrSeconds == 1 ? " SECOND LEFT" : " SECONDS LEFT") : "");
                idle.IsVisible = !asking && (!showing || hidden);
                Say(idle, hidden ? "QR hidden while streaming" : !view.On ? "Turn Remote on first" : !view.Ready ? "Waiting for the relay" : expired ? "That code expired" : linking ? "Pairing link sent to " + view.LinkFor : "No code open");
                askTitle.IsVisible = askName.IsVisible = codeLabel.IsVisible = askCode.IsVisible = askCheck.IsVisible = allow.IsVisible = deny.IsVisible = asking;
                Say(askName, asking ? view.Ask.Name : ""); Say(askCode, asking ? view.Ask.Code : "");
                if (asking) Tip(askName, view.Ask.Name);
                allow.IsEnabled = deny.IsEnabled = view.On && view.Ready;
                pair.Text = showing || expired ? "New code" : "Pair a phone"; AutomationProperties.SetName(pair, pair.Text);
                pair.IsEnabled = view.On && view.Ready && !asking;
                for (int i = 0; i < PhonesPerPage; i++)
                {
                    bool present = first + i < view.Phones.Count;
                    names[i].IsVisible = states[i].IsVisible = revokes[i].IsVisible = present;
                    sources[i].IsVisible = false;
                    if (!present) continue;
                    RemoteDevice phone = view.Phones[first + i];
                    bool online = view.Online.Contains(phone.Id);
                    Say(names[i], phone.Name); Tip(names[i], phone.Name);
                    Say(states[i], online ? "CONNECTED" : "OFFLINE \u00b7 ADDED " + phone.Added.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture).ToUpperInvariant());
                    states[i].Ink = online ? MainframeTheme.Cyan : MainframeTheme.Faint; states[i].InvalidateVisual();
                    string from = phone.From;
                    if (string.IsNullOrEmpty(from) && phone.Parent != null)
                    {
                        RemoteDevice parent = view.Phones.Find(delegate(RemoteDevice found) { return found.Id == phone.Parent; });
                        if (parent != null) from = parent.Name;
                    }
                    bool linked = phone.From != null || phone.Parent != null;
                    sources[i].IsVisible = linked;
                    Say(sources[i], linked ? "BY LINK" + (string.IsNullOrEmpty(from) ? "" : " \u00b7 " + from) : "");
                    if (linked) Tip(sources[i], sources[i].Text);
                    revokes[i].Text = armedId == phone.Id ? "Sure?" : "Revoke"; revokes[i].Loud = armedId == phone.Id;
                    AutomationProperties.SetName(revokes[i], (armedId == phone.Id ? "Sure? Revoke " : "Revoke ") + phone.Name); revokes[i].InvalidateVisual();
                }
                empty.IsVisible = view.Phones.Count == 0;
                back.IsVisible = next.IsVisible = page.IsVisible = view.Phones.Count > PhonesPerPage;
                back.IsEnabled = first > 0; next.IsEnabled = first + PhonesPerPage < view.Phones.Count;
                Say(page, "Page " + (first / PhonesPerPage + 1) + " of " + Math.Max(1, (view.Phones.Count + PhonesPerPage - 1) / PhonesPerPage));
                removeAll.Text = armedAll ? "Sure?" : "Remove all phones"; removeAll.Loud = armedAll; removeAll.InvalidateVisual();
                AutomationProperties.SetName(removeAll, armedAll ? "Sure? Remove all phones and pair again" : "Remove all phones and pair again");
                Say(revokeNote, view.Phones.Exists(delegate(RemoteDevice phone) { return phone.Parent != null; }) ? "Revoke also removes the phones its links added" : ""); revokeNote.IsVisible = revokeNote.Text.Length > 0;
                if (!host.Box.IsKeyboardFocusWithin && lastHost != view.Host) { host.Box.Text = view.Host == RemoteLink.DefaultHost ? "" : view.Host; lastHost = view.Host; }
                if (hostRefused && (refusedText != (host.Box.Text ?? "") || refusedHost != view.Host || refusedNote != view.Note)) hostRefused = false;
                Say(note, hostRefused ? "Not a relay host" : view.Note); note.IsVisible = note.Text.Length > 0; note.Ink = hostRefused ? MainframeTheme.Rose : MainframeTheme.Cyan; note.InvalidateVisual();
            }
            finally { syncing = false; }
        }

        private static string StatusLine(RemoteView shown, out IBrush tone)
        {
            tone = MainframeTheme.Faint;
            switch (shown.Status)
            {
                case "online":
                    tone = MainframeTheme.Cyan;
                    int count = shown.Online.Count;
                    return "ONLINE \u00b7 " + (count == 0 ? "NO PHONES CONNECTED" : count + (count == 1 ? " PHONE CONNECTED" : " PHONES CONNECTED"));
                case "connecting": return "CONNECTING";
                case "retrying": tone = MainframeTheme.Rose; return "CAN'T REACH THE RELAY \u00b7 RETRYING";
                case "replaced": tone = MainframeTheme.Rose; return "ANOTHER CAPRI TOOK OVER \u00b7 TURN OFF AND ON";
                case "bad-host": tone = MainframeTheme.Rose; return "BAD RELAY HOST";
                case "off": return "OFF";
                default: return string.IsNullOrEmpty(shown.Status) ? "OFF" : shown.Status;
            }
        }

        private void Revoke(int row)
        {
            if (row < 0 || row >= PhonesPerPage || first + row >= view.Phones.Count) return;
            string id = view.Phones[first + row].Id;
            armedAll = false;
            if (armedId != id || unchecked(Environment.TickCount - armedAt) > 4000) { armedId = id; armedAt = Environment.TickCount; return; }
            armedId = null; revokePhone(id);
        }

        private void RemoveAll()
        {
            armedId = null;
            if (!armedAll || unchecked(Environment.TickCount - armedAt) > 4000) { armedAll = true; armedAt = Environment.TickCount; return; }
            armedAll = false; removePhones();
        }

        private void SaveHost()
        {
            if (closed) return;
            hostRefused = !changeHost(host.Box.Text ?? "");
            refusedText = host.Box.Text ?? ""; refusedHost = view.Host; refusedNote = view.Note;
            Sync();
        }

        internal string Crowded()
        {
            Rect pageBox = new Rect(0, 0, Wide, Tall);
            for (int i = 0; i < Page.Children.Count; i++)
            {
                Control a = Page.Children[i]; if (!a.IsVisible) continue;
                Rect box = Box(a);
                if (!pageBox.Contains(box)) return "Outside: " + AutomationProperties.GetName(a);
                NeonCaption caption = a as NeonCaption; NeonButton button = a as NeonButton; NeonEntry entry = a as NeonEntry; NeonSwitch toggle = a as NeonSwitch;
                string wrong = caption != null ? caption.TextFits() : button != null ? button.TextFits() : entry != null ? entry.TextFits() : toggle != null ? toggle.TextFits() : null;
                if (wrong != null) return "Text: " + wrong;
                for (int j = i + 1; j < Page.Children.Count; j++)
                {
                    Control b = Page.Children[j]; if (!b.IsVisible) continue;
                    Rect overlap = box.Intersect(Box(b));
                    if (overlap.Width > 0.5 && overlap.Height > 0.5) return AutomationProperties.GetName(a) + " / " + AutomationProperties.GetName(b);
                }
            }
            return null;
        }

        private static Rect Box(Control control)
        {
            double x = Canvas.GetLeft(control), y = Canvas.GetTop(control);
            NeonHead head = control as NeonHead;
            return head == null ? new Rect(x, y, control.Width, control.Height) : new Rect(x + 14, y + 5, MainframeTheme.Words(head.Text, 40.0 / 3, true).Width, 19);
        }

        internal static Dictionary<string, RemoteView> Samples()
        {
            Dictionary<string, RemoteView> views = new Dictionary<string, RemoteView>();
            foreach (string name in new[] { "off", "connecting", "ready", "qr", "qr-streaming", "expired", "asking", "online", "seven-phones", "unreadable", "long", "link", "retrying", "replaced", "bad-host" })
                views[name] = new RemoteView { On = true, Ready = true, Status = "online" };
            views["off"] = new RemoteView();
            views["connecting"].Ready = false; views["connecting"].Status = "connecting";
            string url = RemoteCrypto.QrUrl(RemoteCrypto.B64u(new byte[16]), RemoteCrypto.B64u(new byte[16]), RemoteCrypto.B64u(new byte[32]));
            views["qr"].Qr = views["qr-streaming"].Qr = url;
            views["qr"].QrSeconds = views["qr-streaming"].QrSeconds = 90;
            views["expired"].Expired = true; views["expired"].Qr = url;
            views["asking"].Ask = new RemoteAsk { Pending = "sample", Name = "Lachy's Pixel 9 Pro XL", Code = "318 338", App = "remote-web/0.1.0" };
            views["asking"].Qr = url; views["asking"].QrSeconds = 90;
            DateTime added = new DateTime(2026, 11, 8, 9, 30, 0, DateTimeKind.Utc);
            foreach (string name in new[] { "online", "seven-phones", "long", "link" })
            {
                for (int i = 0; i < (name == "seven-phones" ? 7 : 1); i++)
                {
                    byte[] id = new byte[16]; id[15] = (byte)(i + 1);
                    views[name].Phones.Add(new RemoteDevice { Id = RemoteCrypto.B64u(id), Name = i == 0 ? "Lachy's Pixel 9 Pro XL" : "Phone " + (i + 1), Added = added.AddDays(-i) });
                }
                views[name].Online.Add(views[name].Phones[0].Id);
            }
            views["seven-phones"].Phones[6].From = views["seven-phones"].Phones[0].Name;
            views["seven-phones"].Phones[6].Parent = views["seven-phones"].Phones[0].Id;
            views["unreadable"] = new RemoteView { Status = "Couldn't read the saved phones." };
            string longName = "Lachy's Samsung Galaxy S26 Ultra with a very long phone name that must stay inside the sheet";
            views["long"].Host = "relay-with-a-very-long-name.for-the-family-phones.on-another-long-subdomain.example:8443";
            views["long"].Phones[0].Name = longName;
            views["long"].Ask = new RemoteAsk { Pending = "long-sample", Name = longName, Code = "318 338", App = "remote-web/0.1.0" };
            views["link"].LinkFor = views["link"].Phones[0].Name; views["link"].Qr = url; views["link"].QrSeconds = 90;
            foreach (string name in new[] { "retrying", "replaced", "bad-host" }) { views[name].Status = name; views[name].Ready = false; }
            return views;
        }
    }

    internal sealed class RemoteQrBox : Control
    {
        private static readonly IBrush Paper = new SolidColorBrush(Color.FromRgb(242, 242, 246)), Ink = new SolidColorBrush(Color.FromRgb(10, 10, 16));
        private readonly MainframeWindow frame;
        private RemoteQr code;
        private string url;
        private double zoom, dpi;
        internal int PixelUnit { get; private set; }
        internal Rect QuietBox { get; private set; }

        internal RemoteQrBox(MainframeWindow frame) { this.frame = frame; IsHitTestVisible = false; }

        internal string Url
        {
            get { return url; }
            set
            {
                if (url != value) { url = value; code = null; PixelUnit = 0; QuietBox = new Rect(); }
                if (code == null || zoom != frame.Zoom || dpi != frame.RenderScaling) InvalidateVisual();
            }
        }

        internal static Rect Pixels(int modules, Size size, double scale, Point origin, out int unit)
        {
            double left = Math.Ceiling(origin.X + 6 * scale), top = Math.Ceiling(origin.Y + 6 * scale);
            double wide = Math.Floor(origin.X + (size.Width - 6) * scale) - left, tall = Math.Floor(origin.Y + (size.Height - 6) * scale) - top;
            unit = (int)Math.Floor(Math.Min(wide, tall) / modules);
            if (unit < 1) { unit = 0; return new Rect(); }
            int side = unit * modules;
            return new Rect((left + Math.Floor((wide - side) / 2) - origin.X) / scale, (top + Math.Floor((tall - side) / 2) - origin.Y) / scale, side / scale, side / scale);
        }

        public override void Render(DrawingContext context)
        {
            if (url == null) return;
            if (code == null || zoom != frame.Zoom || dpi != frame.RenderScaling)
            {
                zoom = frame.Zoom; dpi = frame.RenderScaling; code = RemoteQr.Encode(url);
            }
            context.DrawGeometry(Paper, null, MainframeTheme.Cut(new Rect(Bounds.Size), br: 12));
            Point? point = this.TranslatePoint(new Point(), frame);
            if (!point.HasValue) return;
            double scale = zoom * dpi;
            int pixels;
            QuietBox = Pixels(code.Size + 8, Bounds.Size, scale, new Point(point.Value.X * dpi, point.Value.Y * dpi), out pixels); PixelUnit = pixels;
            if (pixels == 0) return;
            double unit = pixels / scale, left = QuietBox.X + 4 * unit, top = QuietBox.Y + 4 * unit;
            using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Aliased }))
                for (int y = 0; y < code.Size; y++)
                    for (int x = 0; x < code.Size; x++)
                        if (code.Dark(x, y)) context.DrawRectangle(Ink, null, new Rect(left + x * unit, top + y * unit, unit, unit));
        }
    }

    internal sealed class MainframeSheet : MainframeWindow
    {
        private readonly Pet pet;
        private readonly List<Action> readers = new List<Action>();
        private readonly DispatcherTimer status = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        private bool syncing, closed;
        private readonly double wide;
        internal readonly Dictionary<string, Control> Rows = new Dictionary<string, Control>();

        internal MainframeSheet(Pet owner, string title, string kind) : base(title)
        {
            pet = owner;
            wide = kind == "layers" ? 760 : kind == "vault" ? 520 : 380;
            ShowInTaskbar = false;
            Put(Page, new NeonCaption { Text = title, Size = 20, Bold = true, Display = true }, 24, 15, 288, 29);
            var close = Button("", (int)wide - 50, 20, 26, 22, Close); close.Glyph = 2;
            double height = kind == "server" ? Server() : kind == "layers" ? Layers() : kind == "vault" ? Vault() : kind == "thinking" ? ThinkingExtra() : SecurityRows();
            Opened += (_, _) => { Fit(wide, height, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0); Sync(); status.Start(); };
            pet.SettingsChanged += Sync;
            status.Tick += (_, _) => Program.Guard(Sync);
            Closed += (_, _) => { closed = true; status.Stop(); pet.SettingsChanged -= Sync; };
        }
        private NeonButton Button(string text, int x, int y, int width, int height, Action action)
        {
            var button = Put(Page, new NeonButton { Text = text }, x, y, width, height, text);
            Rows[text] = button;
            button.Click += (_, _) => { action(); Sync(); };
            return button;
        }
        private void Caption(string text, int x, int y, int width, int height, IBrush ink = null, double size = MainframeTheme.Size)
        {
            Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.Rose, Size = size }, x, y, width, height);
        }
        private NeonEntry Input(string name, string cue, int x, int y, int width, Func<string> read)
        {
            Caption(name, x, y, width, 18);
            var entry = Put(Page, new NeonEntry(cue), x, y + 20, width, 28, name);
            Rows[name] = entry;
            string last = null;
            readers.Add(() => { string value = read(); if (!entry.Box.IsKeyboardFocusWithin && last != value) { entry.Box.Text = value; last = value; } });
            return entry;
        }
        private double Server()
        {
            Caption("OpenAI speech API", 28, 52, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            var address = Input("Address", LocalSpeaker.Default, 28, 84, 328, () => pet.MainframeLocalAddress);
            var model = Input("Model", "Optional", 28, 140, 160, () => pet.MainframeLocalModel);
            var voice = Input("Voice", "Optional", 196, 140, 160, () => pet.MainframeLocalVoice);
            Caption("Format", 28, 196, 146, 18);
            var format = Put(Page, new NeonPicker(), 184, 193, 172, 22, "Format");
            format.Options(new[] { "WAV", "PCM 24 kHz" }); Rows["Format"] = format;
            format.Changed += i => { if (!syncing) pet.SetLocalVoice("format", i == 1 ? LocalSpeaker.Pcm : "wav"); };
            readers.Add(() => format.Selected = pet.MainframeLocalFormat == LocalSpeaker.Pcm ? 1 : 0);
            var note = Put(Page, new NeonCaption { Ink = MainframeTheme.Faint, Ellipsis = true }, 28, 264, 328, 20);
            Func<bool> save = () =>
            {
                string url = address.Box.Text ?? "", modelName = model.Box.Text ?? "", voiceName = voice.Box.Text ?? "";
                try { if (url.Trim().Length > 0 && url.Trim() != "-") LocalSpeaker.Endpoint(url.Trim()); }
                catch (VoiceException error) { note.Text = error.Message; note.InvalidateVisual(); return false; }
                pet.SetLocalVoice("url", url); pet.SetLocalVoice("model", modelName); pet.SetLocalVoice("voice", voiceName);
                note.Text = "Saved."; note.InvalidateVisual();
                return true;
            };
            Button("Test voice", 28, 224, 84, 28, () => { if (save() && Program.Smoke == null) pet.HearStyle(); });
            Button("Save", 120, 224, 150, 28, () => save()).Accent = MainframeTheme.Cyan;
            Button("Cancel", 278, 224, 78, 28, Close);
            foreach (var entry in new[] { address, model, voice }) entry.Box.KeyDown += (_, e) => { if (e.Key == Key.Enter) { e.Handled = true; Program.Guard(() => save()); } };
            return 300;
        }
        // Extra request JSON for the brain she's on, merged into each request it sends.
        private double ThinkingExtra()
        {
            Caption("One JSON object, added to every request this brain sends.", 28, 52, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            Caption("It replaces Capri's value for any name it shares.", 28, 72, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            var entry = Put(Page, new NeonEntry(""), 28, 100, 328, 160, "Extra request JSON"); Rows["Extra request JSON"] = entry;
            entry.Box.AcceptsReturn = true; entry.Box.TextWrapping = TextWrapping.Wrap;
            var note = Put(Page, new NeonCaption { Ink = MainframeTheme.Faint, Ellipsis = true }, 28, 304, 328, 20); Rows["Note"] = note;
            string source = null;
            readers.Add(() =>
            {
                if (source == pet.Brain.ThinkingSource) return;
                source = pet.Brain.ThinkingSource;
                entry.Box.Text = pet.Brain.ThinkingExtra ?? "";
                note.Text = "For " + source.Substring(source.IndexOf('|') + 1) + "."; note.InvalidateVisual();
            });
            Action<string> save = typed =>
            {
                note.Text = pet.SetThinkingExtra(typed) ?? (string.IsNullOrWhiteSpace(typed) ? "Cleared." : "Saved.");
                note.InvalidateVisual();
            };
            Button("Save", 28, 268, 160, 28, () => save(entry.Box.Text ?? "")).Accent = MainframeTheme.Cyan;
            Button("Clear", 196, 268, 160, 28, () => { entry.Box.Text = ""; save(""); });
            return 340;
        }
        private NeonSwitch Toggle(string name, int y, Func<bool> read, Action<bool> change)
        {
            var toggle = Put(Page, new NeonSwitch { Text = name, Switch = 68 }, 28, y, 328, 22, name); Rows[name] = toggle;
            toggle.IsCheckedChanged += (_, _) => { if (!syncing) change(toggle.Checked); };
            readers.Add(() => toggle.Checked = read()); return toggle;
        }
        private double Layers()
        {
            Caption("Her voice, and two copies of it mixed in under her, each with its own pitch and delay.", 28, 52, 704, 18, MainframeTheme.Faint, 34.0 / 3);
            var enabled = Toggle("Voice layers", 76, () => VoiceFx.Layers.On, on => { pet.SetLayers(VoiceFx.Layers.Turned(on)); if (Program.Smoke == null) pet.HearStyle(); }); enabled.Width = 260; enabled.Switch = 120;
            Caption("Off: she sounds as she always has. On: every voice she has, ElevenLabs too.", 28, 102, 704, 18, MainframeTheme.Faint, 34.0 / 3);
            Button("Test voice", 524, 76, 100, 22, () => { if (Program.Smoke == null) pet.HearStyle(); });
            Button("Defaults", 632, 76, 100, 22, () => pet.SetLayers(new VoiceFx.Layering(VoiceFx.Layers.On, VoiceFx.Layering.HigherPitch, VoiceFx.Layering.Behind, VoiceFx.Layering.LowerPitch, VoiceFx.Layering.Behind)));
            Put(Page, new NeonHead { Text = "MAIN" }, 14, 131, 732, 24);
            LayerBar("Main pitch", "Pitch", Pet.PitchNames, 28, 163, 120, 300, () => VoiceFx.Pitch + VoiceFx.MostPitch, i => pet.SetPitch(i - VoiceFx.MostPitch));
            Caption("Her voice itself. Its pitch is Settings > VOICE > Pitch, and they move together.", 28, 192, 704, 18, MainframeTheme.Faint, 34.0 / 3);
            var pitches = new string[(int)(VoiceFx.Layering.MostPitch * 4) + 1]; var delays = new string[VoiceFx.Layering.MostDelay / 5 + 1];
            for (int i = 0; i < pitches.Length; i++) pitches[i] = VoiceFx.Layering.PitchName(i / 2.0 - VoiceFx.Layering.MostPitch);
            for (int i = 0; i < delays.Length; i++) delays[i] = VoiceFx.Layering.DelayName(i * 5);
            for (int layer = 1; layer <= 2; layer++)
            {
                int which = layer;
                int x = layer == 1 ? 28 : 392;
                Put(Page, new NeonHead { Text = "LAYER " + layer }, x - 14, 223, 352, 24);
                LayerBar("Layer " + layer + " pitch", "Pitch", pitches, x, 255, x + 84, 244, () => (int)Math.Round((VoiceFx.Layers.PitchOf(which) + VoiceFx.Layering.MostPitch) * 2), i => pet.SetLayers(VoiceFx.Layers.With(which, i / 2.0 - VoiceFx.Layering.MostPitch, VoiceFx.Layers.DelayOf(which)))).TickEvery = 4;
                LayerBar("Layer " + layer + " delay", "Delay", delays, x, 283, x + 84, 244, () => VoiceFx.Layers.DelayOf(which) / 5, i => pet.SetLayers(VoiceFx.Layers.With(which, VoiceFx.Layers.PitchOf(which), i * 5))).TickEvery = 10;
                Caption(layer == 1 ? "Out of the box a little higher, just behind her." : "Out of the box lower, just behind her.", x, 314, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            }
            return 356;
        }
        private NeonSlider LayerBar(string name, string label, string[] labels, int x, int y, int at, int width, Func<int> read, Action<int> change)
        {
            Caption(label, x, y + 3, at - x - 10, 18);
            var slider = Put(Page, new NeonSlider(name, labels), at, y, width, 22, name); Rows[name] = slider;
            slider.Changed += i => { if (!syncing) change(i); };
            slider.Released += () => { if (!syncing && Program.Smoke == null) pet.HearStyle(); };
            readers.Add(() => slider.Value = read()); return slider;
        }
        private double SecurityRows()
        {
            Caption("Preset", 28, 68, 146, 18);
            var preset = Put(Page, new NeonPicker(), 184, 65, 172, 22, "Preset"); Rows["Preset"] = preset;
            preset.Options(new[] { Security.Presets[0], Security.Presets[1], Security.Presets[2], Security.Presets[3] });
            preset.Changed += i => { if (!syncing) Security.Pick(i); };
            var note = Put(Page, new NeonCaption { Ink = MainframeTheme.Faint, Ellipsis = true }, 28, 99, 328, 24);
            readers.Add(() => { preset.Selected = Math.Min(Security.Preset, 3); if (Security.Preset == Security.Custom) preset.Text = "Custom"; note.Text = Security.Short; note.InvalidateVisual(); });
            int y = 142;
            for (int i = 0; i < Security.Count; i++)
            {
                if (!Security.Has(i)) continue;
                int at = i;
                var toggle = Toggle(Security.Names[i], y, () => Security.On(at), on => Security.Set(at, on));
                ToolTip.SetTip(toggle, Security.Lines[i]);
                y += 34;
            }
            if (pet.MainframeInputSetup) { Button("Set up mouse and keyboard", 28, y + 8, 328, 28, pet.MainframeSetupInput); y += 44; }
            return y + 24;
        }
        private double Vault()
        {
            var vaults = pet.VaultList();
            var paths = new List<string> { "" };
            var names = new List<string> { "Her own notes (no vault)" };
            foreach (var found in vaults) { names.Add("Vault: " + found.Key); paths.Add(Path.Combine(found.Value, Pet.VaultNote)); }
            var picker = Put(Page, new NeonPicker { Ellipsis = true }, 28, 62, 352, 24, "Obsidian vault"); Rows["Obsidian vault"] = picker;
            var note = Put(Page, new TextBlock { Foreground = MainframeTheme.Faint, FontSize = 34.0 / 3, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 3 }, 28, 100, 464, 56, "Vault note"); Rows["Vault note"] = note;
            string last = null;
            bool here = pet.VaultHere;
            Action read = () =>
            {
                string current = Pet.NotesIn(pet.Vault) ?? "";
                int at = paths.FindIndex(path => string.Equals(path, current, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase));
                if (at < 0) { paths.Add(current); names.Add("Note: " + Path.GetFileName(current)); at = paths.Count - 1; }
                picker.Options(names.ToArray()); picker.Selected = at;
                if (last == pet.Vault && here == pet.VaultHere) return;
                last = pet.Vault; here = pet.VaultHere;
                note.Text = pet.Vault.Length > 0 ? pet.VaultHere ? "She saves her memories to " + Path.GetFileName(Pet.NotesIn(pet.Vault)) + "." : "That vault wasn't there when she started, so she's using her own notes." : vaults.Count > 0 ? "Pick one of your Obsidian vaults and her memories go in a Capri Memories note there." : "No Obsidian vaults found on this PC. Open one in Obsidian first, or Browse to a note.";
            };
            Action<string> use = path => { string said = pet.SetVault(path); read(); note.Text = said; };
            picker.Changed += at => { if (!syncing) use(paths[at]); };
            readers.Add(read);
            var browse = Button("Browse", 388, 62, 104, 24, () => { });
            browse.Click += async (_, _) =>
            {
                if (!StorageProvider.CanSave) { note.Text = "Couldn't open the note picker."; return; }
                try
                {
                    string current = Pet.NotesIn(pet.Vault);
                    if (current == null && vaults.Count > 0) current = Path.Combine(vaults[0].Value, Pet.VaultNote);
                    var start = current != null && Directory.Exists(Path.GetDirectoryName(current)) ? await StorageProvider.TryGetFolderFromPathAsync(Path.GetDirectoryName(current)) : null;
                    var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions {
                        Title = "Pick the note her memories go in", SuggestedFileName = current == null ? Pet.VaultNote : Path.GetFileName(current),
                        SuggestedStartLocation = start, DefaultExtension = "md", ShowOverwritePrompt = false,
                        FileTypeChoices = new[] { new FilePickerFileType("Notes") { Patterns = new[] { "*.md" } } } });
                    string path = picked?.TryGetLocalPath();
                    if (!closed && path != null) use(path);
                }
                catch (Exception) { if (!closed) note.Text = "Couldn't open the note picker."; }
            };
            Button("Close", 388, 164, 104, 26, Close);
            return 214;
        }
        private void Sync()
        {
            if (syncing || closed) return;
            syncing = true;
            try { foreach (Action read in readers) read(); }
            finally { syncing = false; }
        }
    }
}
