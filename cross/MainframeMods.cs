using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Her Mods page, like Windows' (app/MainframeMods.cs): her plug-ins, installed mods, Import mod, Browse mods, the mods folder,
    // Update mods and her colors, opened from Mods beside Settings in her title bar. Browse mods opens the mods directory in its own window.
    internal sealed partial class Mainframe
    {
        private const int ModRows = 3, ModRowTop = 370, ModRowHeight = 62, ModListHead = 312;
        private static readonly string ModDownloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Capri", "mod-downloads");
        private NeonButton modsKey, modUpdate, modBack, modNext;
        private NeonCaption modStatus, modEmpty, modNoMatch, modPage;
        private NeonEntry modSearch;
        private readonly NeonCaption[] modNames = new NeonCaption[ModRows], modAbouts = new NeonCaption[ModRows];
        private readonly NeonButton[] modSwitches = new NeonButton[ModRows], modRemoves = new NeonButton[ModRows];
        private readonly NeonCaption[] colourNames = new NeonCaption[ArtColors.Slots];
        private readonly NeonEntry[] colourEntries = new NeonEntry[ArtColors.Slots];
        private List<Mod> modShown = new List<Mod>();
        // Newer copies of installed mods found by the last look, waiting for the second press of Update mods.
        private List<ModListing> modUpdates;
        private bool modded, modLooking, modUpdating, modFilling, modAsking, modInstalling;
        private int modFirst, modUpdateTicket;
        private string shownColours;
        internal ModsWindow ModsBrowser { get; private set; }
        internal bool ModsShown { get { return modded; } }
        internal bool ModsWorking { get { return modLooking || modUpdating; } }
        // One busy state for her Mods page and the directory window, which used to share a window: while an update, a card's
        // install or an approve box runs, neither starts another change to her mods.
        internal bool ModsBusy { get { return modUpdating || modInstalling || modAsking; } }
        internal bool ModsAsking { get { return modAsking; } }
        internal bool ModsInstalling { get { return modInstalling; } set { if (modInstalling == value) return; modInstalling = value; ModsChanged(); } }
        internal string ModStatusText { get { return modStatus.Text; } set { ModSays(value); } }
        internal string UpdateText { get { return modUpdate.Text; } }
        internal string InstalledPageText { get { return modPage.IsVisible ? modPage.Text : null; } }

        private void BuildMods()
        {
            int was = building;
            building = 2;
            Section("MODS", 0, 58);
            Caption("Code mods can reach your whole PC. Only trusted ones.", 28, 80, 328, 18, MainframeTheme.Faint, 34.0 / 3);
            NeonButton import = Key("Import mod", 28, 104, 100, 28, PickModFile);
            import.Accent = MainframeTheme.Cyan; import.Loud = true;
            Key("Browse mods", 134, 104, 100, 28, BrowseMods);
            Key("Mods folder", 240, 104, 116, 28, OpenModsFolder);
            modStatus = Caption("", 28, 138, 328, 36, MainframeTheme.Faint, 34.0 / 3); modStatus.Ellipsis = true;
            BuildPlugs();
            Section("INSTALLED", 0, ModListHead);
            modSearch = Entry("Search your installed mods", "Search by name or author", 28, ModListHead + 24, 222, 26, delegate { });
            // Typing goes through Program.Guard like the buttons, so a bug behind the box is written down instead of ending her.
            modSearch.Box.TextChanged += delegate { Program.Guard(delegate { modFirst = 0; FillMods(); }); };
            modUpdate = Key("Update mods", 256, ModListHead + 24, 100, 26, UpdateMods);
            modEmpty = Caption("No mods yet. Import a " + Mods.Extension + " file.", 28, ModRowTop, 328, 20, MainframeTheme.Faint);
            modNoMatch = Caption("No mods match.", 28, ModRowTop, 328, 20, MainframeTheme.Faint);
            for (int i = 0; i < ModRows; i++)
            {
                int row = i, top = ModRowTop + ModRowHeight * i;
                modNames[i] = Caption("", 28, top + 1, 196, 22, MainframeTheme.White, 40.0 / 3, true); modNames[i].Ellipsis = true;
                modSwitches[i] = Key("On", 230, top, 54, 22, delegate { SwitchMod(row); });
                modRemoves[i] = Key("Remove", 290, top, 66, 22, delegate { RemoveMod(row); });
                modAbouts[i] = Caption("", 28, top + 26, 328, 30, MainframeTheme.Faint, 34.0 / 3); modAbouts[i].Ellipsis = true; modAbouts[i].Top = true;
            }
            modBack = Key("Back", 28, ModRowTop + ModRowHeight * ModRows, 60, 22, delegate { modFirst -= ModRows; FillMods(); });
            modPage = Caption("", 96, ModRowTop + ModRowHeight * ModRows, 192, 22, MainframeTheme.Faint); modPage.Align = TextAlignment.Center;
            modNext = Key("Next", 296, ModRowTop + ModRowHeight * ModRows, 60, 22, delegate { modFirst += ModRows; FillMods(); });
            int colours = ModRowTop + ModRowHeight * ModRows + 30;
            Section("COLORS", 0, colours);
            // Windows picks each color in its color box; Avalonia has none, so each slot takes a #RRGGBB in a row like Settings'.
            for (int i = 0; i < ArtColors.Slots; i++)
            {
                colourNames[i] = Caption(ArtColors.Names[i], 28, colours + 30 + i * 28, 146, 16);
                colourEntries[i] = Entry(ArtColors.Names[i] + " color", ArtColors.Usual[i], 184, colours + 27 + i * 28, 172, 22, SaveColours);
            }
            int keys = colours + 26 + ArtColors.Slots * 28 + 6;
            Key("Save colors", 28, keys, 104, 24, SaveColours).Accent = MainframeTheme.Cyan;
            Key("Reset colors", 140, keys, 104, 24, ResetColours);
            Key("Animations", 252, keys, 104, 24, EditAnimations);
            if (pet.ModList.Problems.Count > 0) modStatus.Text = "Skipped " + pet.ModList.Problems[0];
            building = was;
        }

        internal void ShowMods(bool on)
        {
            if (on && paged) paged = false;
            modded = on;
            layout = null; CloseMenus(); Top();
            if (on) Restore();
            Sync();
        }

        private void Restore() { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; }

        private void FillMods()
        {
            if (modFilling || modSearch == null) return;
            FillPlugs();
            List<Mod> mods = ShownMods(pet.ModList.All.FindAll(one => !one.Plugin), modSearch.Box.Text);
            modShown = mods;
            int total = mods.Count;
            modFirst = Math.Max(0, Math.Min(modFirst, (Math.Max(0, total - 1) / ModRows) * ModRows));
            ShowUpdateKey();
            // Off the Mods page its rows stay hidden, but the list still follows her mods so a row always means what it shows.
            if (!modded) return;
            modFilling = true;
            try
            {
                modEmpty.IsVisible = !pet.ModList.All.Exists(one => !one.Plugin);
                modNoMatch.IsVisible = total == 0 && !modEmpty.IsVisible;
                for (int i = 0; i < ModRows; i++)
                {
                    Mod mod = modFirst + i < mods.Count ? mods[modFirst + i] : null;
                    modNames[i].IsVisible = modAbouts[i].IsVisible = modSwitches[i].IsVisible = modRemoves[i].IsVisible = mod != null;
                    if (mod == null) continue;
                    List<Mod> stack = pet.ModList.SkinStack;
                    modNames[i].Text = mod.File.Name + (string.IsNullOrEmpty(mod.File.Version) ? "" : " v" + mod.File.Version);
                    modNames[i].Ink = mod.On ? MainframeTheme.White : MainframeTheme.Faint;
                    modAbouts[i].Text = ((mod.Changed ? "CHANGED, approve it again. " : mod.Code ? "RUNS CODE. " : mod.Tool ? "TOOL, reads from " + string.Join(", ", mod.File.Recipe.Domains) + ". " : mod.Skin ? (mod.On && stack.Count > 1 ? "SKIN, LAYER " + (stack.IndexOf(mod) + 1) + " OF " + stack.Count + ". " : "SKIN. ") : mod.Skill ? "SKILL. " : Mods.CompanionLabel(mod))
                        + (string.IsNullOrEmpty(mod.File.Author) ? "" : "by " + mod.File.Author + ". ") + (mod.File.Description ?? "")
                        + (mod.Code || mod.File.WakeNames == null || mod.File.WakeNames.Length == 0 ? "" : " Names: " + string.Join(", ", mod.File.WakeNames))
                        + (mod.Skill && mod.File.Games != null && mod.File.Games.Length > 0 ? " For " + mod.File.Games[0] + "." : "")).Trim();
                    modAbouts[i].Ink = mod.Changed ? MainframeTheme.Rose : MainframeTheme.Faint;
                    modSwitches[i].Text = mod.On ? "On" : "Off";
                    modSwitches[i].Accent = mod.On ? MainframeTheme.Cyan : MainframeTheme.Rose;
                    AutomationProperties.SetName(modSwitches[i], (mod.On ? "Turn off " : "Turn on ") + mod.File.Name);
                    AutomationProperties.SetName(modRemoves[i], "Remove " + mod.File.Name);
                    modSwitches[i].IsEnabled = modRemoves[i].IsEnabled = !ModsBusy;
                    foreach (Control control in new Control[] { modNames[i], modAbouts[i], modSwitches[i], modRemoves[i] }) control.InvalidateVisual();
                }
                bool pages = total > ModRows;
                modBack.IsVisible = modNext.IsVisible = modPage.IsVisible = pages;
                modBack.IsEnabled = modFirst > 0;
                modNext.IsEnabled = modFirst + ModRows < total;
                modPage.Text = "Page " + (modFirst / ModRows + 1) + " of " + ((total - 1) / ModRows + 1);
                string[] now = pet.ModList.ColoursNow;
                for (int i = 0; i < ArtColors.Slots; i++)
                {
                    if (shownColours != string.Join("|", now) && !colourEntries[i].Box.IsKeyboardFocusWithin) colourEntries[i].Box.Text = now[i] ?? "";
                    Color swatch;
                    if (!Color.TryParse(now[i] ?? ArtColors.Usual[i], out swatch)) swatch = Colors.Black;
                    // A dark swatch, like her nearly black body or a pure blue, can't be read on the page, so it's named in quiet ink.
                    colourNames[i].Ink = (0.2126 * swatch.R + 0.7152 * swatch.G + 0.0722 * swatch.B) / 255 < 0.25 ? MainframeTheme.Faint : new SolidColorBrush(swatch);
                    colourNames[i].InvalidateVisual();
                }
                // Only a change to her colors refills the boxes, so a color that wasn't saved stays typed in to fix.
                shownColours = string.Join("|", now);
            }
            finally { modFilling = false; }
        }

        private static List<Mod> ShownMods(List<Mod> mods, string search)
        {
            string[] words = (search ?? "").Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            return mods.FindAll(delegate(Mod mod)
            {
                string named = (mod.File.Name ?? "") + " " + (mod.File.Author ?? "");
                foreach (string word in words) if (named.IndexOf(word, StringComparison.OrdinalIgnoreCase) < 0) return false;
                return true;
            });
        }

        private void ModSays(string text)
        {
            modStatus.Text = text;
            ToolTip.SetTip(modStatus, string.IsNullOrEmpty(text) ? null : new TextBlock { Text = text, MaxWidth = 332, TextWrapping = TextWrapping.Wrap, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size, Foreground = MainframeTheme.White });
            modStatus.InvalidateVisual();
        }

        private AnimationDraft animationDraft;
        private AnimationSheet animationSheet;

        private void EditAnimations()
        {
            if (animationSheet != null) { animationSheet.WindowState = WindowState.Normal; animationSheet.Activate(); return; }
            if (animationDraft != null && animationDraft.BaseFolder != pet.ModList.OutfitFolder) ForgetAnimations();
            if (animationDraft == null) animationDraft = new AnimationDraft(pet.ModList, pet.Clips);
            AnimationDraft used = animationDraft;
            AnimationSheet sheet = new AnimationSheet(pet, this, used);
            animationSheet = sheet;
            sheet.Closed += delegate
            {
                if (animationSheet == sheet) animationSheet = null;
                if (animationDraft != used) used.Dispose();
            };
            ShowSheet(sheet);
        }

        private void ForgetAnimations()
        {
            if (animationDraft != null && animationSheet == null) animationDraft.Dispose();
            animationDraft = null;
        }

        internal void AnimationsWorn()
        {
            animationDraft = null;
            ModsChanged();
        }

        internal void BrowseMods()
        {
            if (ModsBrowser == null)
            {
                ModsWindow sheet = new ModsWindow(pet, this);
                ModsBrowser = sheet;
                sheet.Closed += delegate { if (ModsBrowser == sheet) ModsBrowser = null; };
                ShowSheet(sheet);
            }
            else { ModsBrowser.WindowState = WindowState.Normal; ModsBrowser.Activate(); }
        }

        private void RefillBrowser() { if (ModsBrowser != null) ModsBrowser.Sync(); }

        private async void PickModFile()
        {
            try
            {
                if (!StorageProvider.CanOpen) { ModSays("Couldn't open the mod picker."); return; }
                IReadOnlyList<IStorageFile> files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = "Import mod", AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Capri mods") { Patterns = new[] { "*" + Mods.Extension, "*" + Mods.SkinExtension, "*" + Mods.SkillExtension, "*" + Mods.CodeExtension, "*" + Mods.ToolExtension, "*" + Mods.PluginExtension, "*" + Mods.CompanionExtension, "plugin.json", "capri-plugin.json", ".mcp.json", "mcp.json" } } }
                });
                if (!closed && files.Count > 0)
                {
                    string path = files[0].TryGetLocalPath();
                    if (path == null) ModSays("That mod needs to be a local file.");
                    else Program.Guard(delegate { ImportMod(path); });
                }
                foreach (IStorageFile file in files) file.Dispose();
            }
            catch (Exception) { if (!closed) ModSays("Couldn't open the mod picker."); }
        }

        private void OpenModsFolder()
        {
            try { Directory.CreateDirectory(pet.ModList.Folder); if (Program.Smoke == null) Pet.Browse(pet.ModList.Folder); }
            catch (Exception error) { ModSays("Couldn't open the mods folder. " + error.Message); }
        }

        // Before a code mod runs for the first time, or after it changed, they read what it is and say yes. The smoke test answers
        // through ModAsk instead.
        internal Func<ModFile, string, bool> ModAsk;

        // An Avalonia dialog only blocks its owner, so while the box is up both her Mainframe and the directory window are off,
        // and every change to her mods refuses, so nothing can change her mods under the one being asked about.
        private bool Ask(ModFile mod, string hash)
        {
            Func<ModFile, string, bool> ask = ModAsk;
            ModsWindow browser = ModsBrowser;
            PluginWindow plug = PluginSheet;
            bool frameWas = IsEnabled, browserWas = browser != null && browser.IsEnabled, plugWas = plug != null && plug.IsEnabled;
            modAsking = true;
            IsEnabled = false;
            if (browser != null) browser.IsEnabled = false;
            if (plug != null) plug.IsEnabled = false;
            ModsChanged();
            try { return ask != null ? ask(mod, hash) : AskToRun(mod, hash); }
            finally
            {
                modAsking = false;
                IsEnabled = frameWas;
                if (browser != null) browser.IsEnabled = browserWas;
                if (plug != null) plug.IsEnabled = plugWas;
                ModsChanged();
            }
        }

        private bool Refused(bool busy)
        {
            if (!(busy ? ModsBusy : modAsking)) return false;
            ModSays(modAsking ? "Answer the code mod's box first." : "Wait for her mods to finish updating.");
            return true;
        }

        // Her approve box, the same words as on Windows, waited on here so the mod list stays as it was until they answer.
        private bool AskToRun(ModFile mod, string hash)
        {
            if (mod.Plugin) return RunBox("Turn on this plug-in?", Mods.Consent(mod, hash));
            if (!RunBox(mod.Recipe != null ? "Use this tool mod?" : "Run this code mod?", Mods.Consent(mod, hash))) return false;
            return !mod.AskTwice || RunBox("Run code you can't read?", Mods.Unclear(mod));
        }

        private bool RunBox(string title, string text)
        {
            bool yes = false;
            Window box = new Window
            {
                Title = title, Width = 520, SizeToContent = SizeToContent.Height, CanResize = false, ShowInTaskbar = false,
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
            all.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(16), Foreground = MainframeTheme.White, FontFamily = MainframeTheme.Text, FontSize = MainframeTheme.Size });
            all.Children.Add(buttons);
            box.Content = all;
            using (CancellationTokenSource done = new CancellationTokenSource())
            {
                box.Closed += delegate { done.Cancel(); };
                box.ShowDialog(PluginSheet != null && PluginSheet.IsActive ? PluginSheet : ModsBrowser != null && ModsBrowser.IsActive ? ModsBrowser : (Window)this);
                no.Focus();
                Dispatcher.UIThread.MainLoop(done.Token);
            }
            return yes;
        }

        internal bool ImportMod(string file)
        {
            if (Refused(false)) return false;
            try
            {
                Mod mod = pet.ModList.Import(file, Ask);
                // A file removed during import can be missing from the reloaded mod list.
                if (mod == null) throw new ModException("Not imported. Nothing ran.");
                if (mod.Plugin) ShowPlug(mod);
                ModSays(mod.Pack != null ? Mods.CompanionSays(mod) : mod.File.Name + " is on."); ModsChanged(); return true;
            }
            catch (Exception error)
            {
                if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                ModSays(error.Message); ModsChanged(); return false;
            }
        }

        private Mod ShownMod(int row)
        {
            int at = modFirst + row;
            if (at < modShown.Count)
            {
                // A reload makes new copies of her mods, so the row's mod is found again by its file and id.
                string path = modShown[at].Path, id = modShown[at].File.Id;
                Mod current = pet.ModList.All.Find(mod => string.Equals(mod.Path, path, StringComparison.Ordinal) && string.Equals(mod.File.Id, id, StringComparison.OrdinalIgnoreCase));
                if (current != null) return current;
            }
            ModsChanged();
            ModSays("That mod is no longer installed. The list is updated.");
            return null;
        }

        internal void SwitchMod(int row)
        {
            if (Refused(true)) return;
            Mod mod = ShownMod(row);
            if (mod != null) Switch(mod);
        }

        // On or off, the same way from her list or a plug-in's window: a plug-in turning on asks with its box first.
        private bool Switch(Mod mod)
        {
            if (Refused(true) || mod == null) return false;
            bool done = false;
            try
            {
                done = pet.ModList.Set(mod, !mod.On, Ask);
                ModSays(!done ? mod.File.Name + " stays off. Nothing ran." : mod.Pack != null ? Mods.CompanionSays(mod) : mod.File.Name + (mod.On ? " is on." : " is off."));
                if (done && mod.On) Started(mod);
            }
            catch (Exception error) { ModSays(error.Message); }
            ModsChanged();
            return done;
        }

        internal void RemoveMod(int row)
        {
            if (Refused(true)) return;
            Mod mod = ShownMod(row);
            if (mod == null) return;
            try { pet.ModList.Remove(mod); ModSays("Removed " + mod.File.Name + "."); }
            catch (Exception error) { ModSays(error.Message); }
            ModsChanged();
        }

        internal void NextInstalledPage() { modFirst += ModRows; FillMods(); }

        internal void SearchInstalled(string words) { modSearch.Box.Text = words; modFirst = 0; FillMods(); }

        internal void SaveColours()
        {
            if (Refused(false)) return;
            try
            {
                ModColors picked = new ModColors();
                string[] values = new string[colourEntries.Length];
                bool any = false;
                for (int i = 0; i < values.Length; i++) { string value = (colourEntries[i].Box.Text ?? "").Trim(); values[i] = value.Length == 0 ? null : value; any |= values[i] != null; }
                picked.Hair = values[0]; picked.Accent = values[1]; picked.Eyes = values[2]; picked.Edges = values[3]; picked.Body = values[4];
                pet.ModList.SaveColours(picked, any);
                ModSays("Her colors are saved.");
            }
            catch (Exception error) { ModSays(error.Message); }
            ModsChanged();
        }

        private void ResetColours()
        {
            if (Refused(false)) return;
            try { pet.ModList.ResetColours(); ModSays("Her colors are back."); }
            catch (Exception error) { ModSays(error.Message); }
            ModsChanged();
        }

        // Her list, her plug-ins, a plug-in's window and the directory's cards follow what changed.
        private void ModsChanged() { FillMods(); RefillBrowser(); RefreshPluginSheet(); }

        // An update from a directory card: like an import, but every mod stays on or off as it was.
        internal bool UpdateMod(string file, string id)
        {
            if (Refused(false)) return false;
            try
            {
                Mod mod = pet.ModList.Update(file, Ask);
                if (mod == null) { ModSays("Not updated. The old one is still in."); ModsChanged(); return false; }
                UpdateDone(id, file);
                ModSays(mod.File.Name + " is updated" + (mod.File.Version != null ? " to " + mod.File.Version : "") + (mod.On ? "." : ". It's still off.")); ModsChanged();
                return true;
            }
            catch (Exception error)
            {
                if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                Brain.Log("mods", -1, Path.GetFileName(file) + ": " + error.Message);
                ModSays(error.Message); ModsChanged(); return false;
            }
        }

        internal ModListing UpdateFor(string id)
        {
            return modUpdates == null ? null : modUpdates.Find(listing => string.Equals(listing.Id, id, StringComparison.OrdinalIgnoreCase));
        }

        // A mod got its update from its card, so it's no longer waiting. Its kept download goes unless it was the one used.
        private void UpdateDone(string id, string used)
        {
            if (modUpdates == null) return;
            List<ModListing> gone = modUpdates.FindAll(listing => string.Equals(listing.Id, id, StringComparison.OrdinalIgnoreCase));
            foreach (ModListing listing in gone) if (listing.Fetched == used) listing.Fetched = null;
            ModDirectory.Forget(gone);
            modUpdates.RemoveAll(listing => gone.Contains(listing));
            ShowUpdateKey();
        }

        private void ShowUpdateKey()
        {
            int waiting = modUpdates == null ? 0 : modUpdates.Count;
            modUpdate.Text = modUpdating ? "Updating..." : modLooking ? "Looking..." : waiting > 0 ? "Update " + waiting : "Update mods";
            modUpdate.Accent = waiting > 0 && !modUpdating && !modLooking ? MainframeTheme.Cyan : MainframeTheme.Rose;
            modUpdate.IsEnabled = !ModsBusy && !modLooking;
            AutomationProperties.SetName(modUpdate, waiting > 0 ? "Get updates for " + ModDirectory.Names(modUpdates) : "Update your installed mods from the mods directory");
            modUpdate.InvalidateVisual();
        }

        // Update mods: the first press looks in the mods directory for newer copies of installed mods and says which, and the
        // second gets them. Each goes in like an import, so a code mod still asks, and every mod stays on or off as it was.
        internal void UpdateMods()
        {
            if (closed || modLooking || Refused(true)) return;
            if (modUpdates != null && modUpdates.Count > 0) { GetUpdates(new List<ModListing>(modUpdates)); return; }
            LookForUpdates(null);
        }

        // Looks for newer copies of installed mods. Given the listings the directory window has just loaded, it's quiet: it only
        // marks their cards.
        internal void LookForUpdates(List<ModListing> loaded)
        {
            if (closed || modLooking || modUpdating) return;
            bool quiet = loaded != null;
            modLooking = true;
            if (!quiet) ModSays("Looking in the mods directory for updates...");
            ShowUpdateKey();
            List<Mod> installed = new List<Mod>(pet.ModList.All);
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<ModListing> found = null;
                string problem = null;
                try { found = ModDirectory.Newer(ModDirectory.Updates(loaded ?? ModDirectory.BrowseNow(), installed), installed, ModDownloads); }
                catch (Exception error) { problem = ModDirectory.Trouble(error); }
                Dispatcher.UIThread.Post(delegate
                {
                    if (closed) { ModDirectory.Forget(found); return; }
                    modLooking = false;
                    if (problem == null)
                    {
                        if (modUpdates != null) ModDirectory.Forget(modUpdates.FindAll(listing => !found.Contains(listing)));
                        modUpdates = found;
                    }
                    ShowUpdateKey(); ModsChanged();
                    if (quiet) return;
                    if (problem != null) ModSays(problem);
                    else if (found.Count == 0) ModSays("Your mods are up to date.");
                    else ModSays((found.Count == 1 ? "There's an update for " : "There are updates for ") + ModDirectory.Names(found) + ". Press " + modUpdate.Text + " to get " + (found.Count == 1 ? "it." : "them."));
                });
            });
        }

        // Closing her Mainframe on Mac or Linux only tucks it away, but it ends Update mods the way closing it does on Windows:
        // what was got stays, the rest waits for another look.
        internal void StopModUpdates() { if (modUpdating) modUpdateTicket++; }

        private void StoppedUpdates(List<ModListing> done)
        {
            modUpdating = false;
            ModDirectory.Forget(modUpdates);
            modUpdates = null;
            ModSays((done.Count == 0 ? "" : "Updated " + ModDirectory.Names(done) + ". ") + "Update mods stopped when the Mainframe closed.");
            ShowUpdateKey(); ModsChanged();
        }

        internal void ForgetModUpdates() { ModDirectory.Forget(modUpdates); modUpdates = null; }

        private void GetUpdates(List<ModListing> pending)
        {
            modUpdating = true;
            int ticket = ++modUpdateTicket;
            ShowUpdateKey(); ModsChanged();
            List<ModListing> done = new List<ModListing>(), kept = new List<ModListing>();
            string reason = null;
            Action next = null;
            next = delegate
            {
                // A mod removed since the look isn't put back.
                while (pending.Count > 0 && !pet.ModList.All.Exists(mod => string.Equals(mod.File.Id, pending[0].Id, StringComparison.OrdinalIgnoreCase)))
                {
                    ModDirectory.Forget(new List<ModListing> { pending[0] });
                    pending.RemoveAt(0);
                }
                if (pending.Count == 0)
                {
                    modUpdating = false;
                    modUpdates = null;
                    string said = done.Count == 0 ? "" : "Updated " + ModDirectory.Names(done) + ".";
                    if (kept.Count > 0) said += (said.Length > 0 ? " " : "") + "Kept the old " + ModDirectory.Names(kept) + (reason != null ? ": " + reason : ".");
                    ModSays(said.Length > 0 ? said : "Nothing needed updating.");
                    ShowUpdateKey(); ModsChanged();
                    return;
                }
                ModListing listing = pending[0];
                pending.RemoveAt(0);
                ModSays("Updating " + listing.Title + "...");
                string ready = listing.Fetched != null && File.Exists(listing.Fetched) ? listing.Fetched : null;
                listing.Fetched = null;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string file = ready, problem = null;
                    if (file == null)
                    {
                        try { file = ModDirectory.Download(listing, ModDownloads); }
                        catch (Exception error) { problem = error is ModException ? error.Message : "couldn't download it: " + error.Message; }
                    }
                    Dispatcher.UIThread.Post(delegate
                    {
                        if (closed || ticket != modUpdateTicket)
                        {
                            if (file != null) ModDirectory.Clean(file);
                            ModDirectory.Forget(pending);
                            if (!closed) StoppedUpdates(done);
                            return;
                        }
                        if (file == null) { kept.Add(listing); reason = reason ?? problem; }
                        else
                        {
                            try
                            {
                                if (pet.ModList.Update(file, Ask) != null) done.Add(listing);
                                else kept.Add(listing);
                            }
                            catch (Exception error)
                            {
                                if (!(error is ModException || error is IOException || error is UnauthorizedAccessException)) throw;
                                Brain.Log("mods", -1, listing.FileName + ": " + error.Message);
                                kept.Add(listing);
                                reason = reason ?? error.Message;
                            }
                            finally { ModDirectory.Clean(file); }
                        }
                        next();
                    });
                });
            };
            next();
        }
    }
}
