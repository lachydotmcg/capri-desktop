using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Text;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private const int ModRows = 4, ListingRows = 3, ModRowTop = 204, ModRowHeight = 62;
        private readonly List<KeyValuePair<Control, int>> modLeft = new List<KeyValuePair<Control, int>>();
        private readonly Label[] modNames = new Label[ModRows], modAbouts = new Label[ModRows];
        private readonly Key[] modSwitches = new Key[ModRows], modRemoves = new Key[ModRows], modInstalls = new Key[ModRows], modViews = new Key[ModRows];
        private Key modsKey, modBack, modNext, modBrowse;
        private Head modListHead;
        private List<ModListing> modListings = new List<ModListing>(), modShown = new List<ModListing>();
        private Entry modSearch;
        private readonly Key[] modFilters = new Key[ModDirectory.Kinds.Length];
        private int modFilter;
        private bool modBrowsing, modInstalling;
        private int modBrowseTicket;
        private readonly Key[] colourKeys = new Key[ArtColors.Slots];
        private Label modStatus, modEmpty, modPage;
        private bool modded;
        private int modFirst;

        private void BuildMods()
        {
            ModAsk = AskToRun;
            int first = Controls.Count;
            Section("MODS >", 0, 58);
            Caption("Code mods can reach your whole PC. Only trusted ones.", 28, 80, Look.Mono(8.5f, FontStyle.Italic, GraphicsUnit.Point), Faint);
            Key import = Make("Import mod", 28, 104, 100, 28, delegate { ImportMod(); });
            import.Accent = Cyan;
            import.Loud = true;
            modBrowse = Make("Browse mods", 134, 104, 100, 28, delegate { BrowseMods(!modBrowsing); });
            Key folder = Make("Mods folder", 240, 104, 116, 28, delegate { OpenModsFolder(); });
            Explain(import, "Import mod", "MainframeMods.cs");
            Explain(modBrowse, "Browse mods", "MainframeMods.cs");
            Explain(folder, "Open mods folder", "MainframeMods.cs");
            modStatus = ModLabel("", 28, 138, 328, 36, Font, Faint);
            modListHead = Section("INSTALLED >", 0, 180);
            modEmpty = ModLabel("No mods yet. Import a " + Mods.Extension + " file.", 28, ModRowTop, 328, 20, Font, Faint);
            modSearch = Input(28, ModRowTop, 328, 24);
            modSearch.Cue = "Search by name";
            modSearch.Box.AccessibleName = "Search the mods directory";
            modSearch.Box.TextChanged += delegate { modFirst = 0; FillMods(); };
            Explain(modSearch, "Search mods", "MainframeMods.cs");
            for (int i = 0; i < modFilters.Length; i++)
            {
                int kind = i;
                modFilters[i] = Make(ModDirectory.Kinds[i], 28 + i * 84, ModRowTop + 30, 76, 22, delegate { modFirst = 0; modFilter = kind; FillMods(); });
                modFilters[i].AccessibleName = "Show " + ModDirectory.Kinds[i].ToLowerInvariant() + " in the mods directory";
                Explain(modFilters[i], "Mod filter", "MainframeMods.cs", null, "All / Settings / Code / Skins");
            }
            for (int i = 0; i < ModRows; i++)
            {
                int row = i, top = ModRowTop + ModRowHeight * i;
                modNames[i] = ModLabel("", 28, top + 3, 196, 20, Type(10f, true), White);
                modSwitches[i] = Make("On", 230, top, 54, 22, delegate { SwitchMod(row); });
                modRemoves[i] = Make("Remove", 290, top, 66, 22, delegate { RemoveMod(row); });
                modAbouts[i] = ModLabel("", 28, top + 26, 328, 30, Look.Mono(8.5f, FontStyle.Regular, GraphicsUnit.Point), Faint);
                Explain(modSwitches[i], "Mod switch", "MainframeMods.cs", null, "On / Off");
                Explain(modRemoves[i], "Remove", "MainframeMods.cs");
                modInstalls[i] = Make("Install", 230, top, 54, 22, delegate { InstallListing(row); });
                modViews[i] = Make("View", 290, top, 66, 22, delegate { ViewListing(row); });
                Explain(modInstalls[i], "Install mod", "MainframeMods.cs");
                Explain(modViews[i], "View mod", "MainframeMods.cs");
            }
            modBack = Make("Back", 28, ModRowTop + ModRowHeight * ModRows, 60, 22, delegate { modFirst -= ModPageSize; FillMods(); });
            modPage = ModLabel("", 96, ModRowTop + ModRowHeight * ModRows + 3, 192, 18, Font, Faint);
            modPage.TextAlign = ContentAlignment.MiddleCenter;
            Explain(modBack, "Mod pages", "MainframeMods.cs");
            modNext = Make("Next", 296, ModRowTop + ModRowHeight * ModRows, 60, 22, delegate { modFirst += ModPageSize; FillMods(); });
            Explain(modNext, "Mod pages", "MainframeMods.cs");
            Section("COLORS >", 0, ModRowTop + ModRowHeight * ModRows + 30);
            int colourRow = ModRowTop + ModRowHeight * ModRows + 54;
            for (int i = 0; i < ArtColors.Slots; i++)
            {
                int slot = i;
                colourKeys[i] = Make(ArtColors.Names[i], 28 + i * 66, colourRow, 62, 26, delegate { PickColour(slot); });
                colourKeys[i].AccessibleName = "Change her " + ArtColors.Names[i].ToLowerInvariant() + " color";
                Explain(colourKeys[i], "Her colors", "MainframeMods.cs", null, "Her colors");
            }
            Key colourReset = Make("Reset colors", 28, colourRow + 32, 160, 24, delegate { ResetColours(); });
            Explain(colourReset, "Reset colors", "MainframeMods.cs");
            for (int i = first; i < Controls.Count; i++)
            {
                Control control = Controls[i];
                modLeft.Add(new KeyValuePair<Control, int>(control, control.Left));
                control.Left += Far;
                Reachable(control, false);
            }
        }

        private Label ModLabel(string text, int x, int y, int width, int height, Font font, Color colour)
        {
            Label label = Caption(text, x, y, font, colour);
            label.AutoSize = false;
            label.AutoEllipsis = true;
            label.Size = new Size(width, height);
            return label;
        }

        internal bool ModsShown { get { return modded; } }

        private int ModPageSize { get { return modBrowsing ? ListingRows : ModRows; } }

        private ModListing ListingAt(int row)
        {
            int at = modFirst + row - 1;
            return row > 0 && at < modShown.Count ? modShown[at] : null;
        }

        private void ClearSearch()
        {
            modFilter = 0;
            modSearch.Box.Text = "";
        }

        internal void ShowMods(bool on)
        {
            DismissHint();
            if (on && paged) ShowSettings(false);
            modded = on;
            modsKey.Text = on ? "Back" : "Mods";
            modsKey.Invalidate();
            Place();
            foreach (KeyValuePair<Control, int> entry in mainLeft) Reachable(entry.Key, !on);
            foreach (KeyValuePair<Control, int> entry in modLeft) Reachable(entry.Key, on);
            if (Hidden(ActiveControl)) modsKey.Focus();
            if (!on) { modBrowsing = false; modBrowseTicket++; ClearSearch(); }
            if (on) FillMods();
            Arrange(expanded);
        }

        private void FillMods()
        {
            List<Mod> mods = capri.ModList.All;
            modShown = ModDirectory.Filter(modListings, modSearch.Box.Text, modFilter);
            int total = modBrowsing ? modShown.Count : mods.Count, pageSize = ModPageSize;
            modFirst = Math.Max(0, Math.Min(modFirst, (Math.Max(0, total - 1) / pageSize) * pageSize));
            modEmpty.Visible = total == 0;
            modEmpty.Top = Scaled(ModRowTop + (modBrowsing ? ModRowHeight : 0));
            modEmpty.Text = !modBrowsing ? "No mods yet. Import a " + Mods.Extension + " file." : modListings.Count == 0 ? "Nothing to show from the mods directory." : "No mods match that.";
            modSearch.Visible = modBrowsing;
            for (int i = 0; i < modFilters.Length; i++)
            {
                modFilters[i].Visible = modSearch.Visible;
                modFilters[i].Accent = i == modFilter ? Cyan : Faint;
                modFilters[i].Invalidate();
            }
            modListHead.Text = modBrowsing ? "MODS DIRECTORY >" : "INSTALLED >";
            modBrowse.Text = modBrowsing ? "Installed" : "Browse mods";
            modBrowse.Invalidate();
            for (int i = 0; i < ModRows; i++)
            {
                ModListing listing = modBrowsing ? ListingAt(i) : null;
                modInstalls[i].Visible = modViews[i].Visible = listing != null;
                if (listing != null)
                {
                    modNames[i].Visible = modAbouts[i].Visible = true;
                    modSwitches[i].Visible = modRemoves[i].Visible = false;
                    modNames[i].Text = listing.Folder;
                    modNames[i].ForeColor = White;
                    bool have = mods.Exists(installed => string.Equals(installed.File.Id, listing.Id, StringComparison.OrdinalIgnoreCase));
                    modAbouts[i].Text = listing.Kind + " " + listing.FileName + ", " + Math.Max(1, listing.Size / 1024) + " KB." + (have ? " Installed." : "");
                    modAbouts[i].ForeColor = Faint;
                    modInstalls[i].Enabled = !modInstalling;
                    modInstalls[i].AccessibleName = "Install " + listing.Folder;
                    modViews[i].AccessibleName = "View " + listing.Folder + " on GitHub";
                    continue;
                }
                Mod mod = !modBrowsing && modFirst + i < mods.Count ? mods[modFirst + i] : null;
                modNames[i].Visible = modAbouts[i].Visible = modSwitches[i].Visible = modRemoves[i].Visible = mod != null;
                if (mod == null) continue;
                modNames[i].Text = mod.File.Name + (string.IsNullOrEmpty(mod.File.Version) ? "" : " v" + mod.File.Version);
                modNames[i].ForeColor = mod.On ? White : Faint;
                modAbouts[i].Text = (mod.Changed ? "CHANGED, approve it again. " : mod.Code ? "RUNS CODE. " : mod.Skin ? "SKIN. " : "") + (string.IsNullOrEmpty(mod.File.Author) ? "" : "by " + mod.File.Author + ". ") + (mod.File.Description ?? "") + (mod.Code || mod.File.WakeNames.Length == 0 ? "" : " Names: " + string.Join(", ", mod.File.WakeNames));
                modAbouts[i].ForeColor = mod.Changed ? Rose : Faint;
                modSwitches[i].Text = mod.On ? "On" : "Off";
                modSwitches[i].Accent = mod.On ? Cyan : Rose;
                modSwitches[i].AccessibleName = (mod.On ? "Turn off " : "Turn on ") + mod.File.Name;
                modRemoves[i].AccessibleName = "Remove " + mod.File.Name;
                modSwitches[i].Invalidate();
            }
            bool pages = total > pageSize;
            modBack.Visible = modNext.Visible = modPage.Visible = pages;
            modBack.Enabled = modFirst > 0;
            modNext.Enabled = modFirst + pageSize < total;
            modPage.Text = "Page " + (modFirst / pageSize + 1) + " of " + ((total - 1) / pageSize + 1);
            string[] colours = capri.ModList.ColoursNow;
            for (int i = 0; i < ArtColors.Slots; i++)
            {
                string shown = colours[i] ?? ArtColors.Usual[i];
                Color swatch = ColorTranslator.FromHtml(shown);
                colourKeys[i].Accent = swatch.GetBrightness() < 0.2f ? Faint : swatch;
                colourKeys[i].Invalidate();
            }
            if (!modBrowsing && capri.ModList.Problems.Count > 0) ModSays("Skipped " + capri.ModList.Problems[0], true);
        }

        private void ModSays(string text, bool problem)
        {
            modStatus.Text = text;
            modStatus.ForeColor = problem ? Rose : Cyan;
        }

        private void ImportMod()
        {
            using (OpenFileDialog dialog = new OpenFileDialog())
            {
                dialog.Title = "Import a Capri mod";
                dialog.Filter = "Capri mods (*" + Mods.Extension + ";*" + Mods.CodeExtension + ";*" + Mods.SkinExtension + ")|*" + Mods.Extension + ";*" + Mods.CodeExtension + ";*" + Mods.SkinExtension;
                if (dialog.ShowDialog(this) == DialogResult.OK) ImportMod(dialog.FileName);
            }
        }

        internal bool ImportMod(string file)
        {
            try
            {
                Mod mod = capri.ModList.Import(file, ModAsk);
                if (mod == null)
                {
                    ModSays("Not imported. Nothing ran.", false);
                    return false;
                }
                FillMods();
                ModSays(mod.Code ? mod.File.Name + " is on and running." : mod.File.WakeNames.Length == 0 ? mod.File.Name + " is on." : mod.File.Name + " is on. She now answers to " + Mods.Listed(mod.File.WakeNames) + " too.", false);
                return true;
            }
            catch (ModException error)
            {
                ModSays(error.Message, true);
                return false;
            }
        }

        private void BrowseMods(bool on)
        {
            modBrowsing = on;
            modFirst = 0;
            int ticket = ++modBrowseTicket;
            modListings = new List<ModListing>();
            ClearSearch();
            if (!on)
            {
                ModSays("", false);
                FillMods();
                return;
            }
            ModSays("Looking in the mods directory...", false);
            FillMods();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                List<ModListing> found = null;
                string problem = null;
                try { found = ModDirectory.Browse(); }
                catch (Exception error) { problem = DirectoryProblem(error, "Couldn't reach the mods directory: "); }
                try { BeginInvoke(new MethodInvoker(delegate { if (ticket == modBrowseTicket) ShowListings(found, problem); })); }
                catch (InvalidOperationException) { }
            });
        }

        private static string DirectoryProblem(Exception error, string lead)
        {
            return error is ModException ? error.Message : lead + error.Message;
        }

        private void ShowListings(List<ModListing> found, string problem)
        {
            modListings = found ?? new List<ModListing>();
            FillMods();
            if (problem != null) ModSays(problem, true);
            else if (modListings.Count == 0) ModSays("No mods in the directory yet.", false);
            else ModSays("The directory checks mods for malware first. Code mods still ask before they run.", false);
        }

        private void InstallListing(int row)
        {
            ModListing listing = ListingAt(row);
            if (modInstalling || listing == null) return;
            modInstalling = true;
            ModSays("Downloading " + listing.Folder + "...", false);
            FillMods();
            string under = Path.Combine(Path.GetTempPath(), "capri-mod-downloads");
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                string file = null, problem = null;
                try { file = ModDirectory.Download(listing, under); }
                catch (Exception error) { problem = DirectoryProblem(error, "Couldn't download that mod: "); }
                try { BeginInvoke(new MethodInvoker(delegate { FinishInstall(file, problem); })); }
                catch (InvalidOperationException) { if (file != null) ModDirectory.Clean(file); }
            });
        }

        private bool FinishInstall(string file, string problem)
        {
            modInstalling = false;
            if (file == null)
            {
                FillMods();
                ModSays(problem, true);
                return false;
            }
            try { return ImportMod(file); }
            finally
            {
                ModDirectory.Clean(file);
                FillMods();
            }
        }

        private void ViewListing(int row)
        {
            ModListing listing = ListingAt(row);
            if (listing == null) return;
            try { using (Process.Start(ModDirectory.Page + "/tree/main/mods/" + listing.Folder)) { } }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                ModSays("Couldn't open that page: " + error.Message, true);
            }
        }

        private void PickColour(int slot)
        {
            string now = capri.ModList.ColoursNow[slot] ?? ArtColors.Usual[slot];
            using (ColorDialog dialog = new ColorDialog { FullOpen = true, Color = ColorTranslator.FromHtml(now) })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                Color picked = dialog.Color;
                try
                {
                    capri.ModList.SetColour(slot, "#" + picked.R.ToString("X2") + picked.G.ToString("X2") + picked.B.ToString("X2"));
                    FillMods();
                    ModSays("Her " + ArtColors.Names[slot].ToLowerInvariant() + " changed. Saved as My Colors, so you can share it.", false);
                }
                catch (IOException error) { ModSays("Couldn't save that color: " + error.Message, true); }
                catch (UnauthorizedAccessException error) { ModSays("Couldn't save that color: " + error.Message, true); }
            }
        }

        private void ResetColours()
        {
            try
            {
                capri.ModList.ResetColours();
                FillMods();
                ModSays("Her own colors are back.", false);
            }
            catch (IOException error) { ModSays("Couldn't reset her colors: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't reset her colors: " + error.Message, true); }
        }

        private bool SwitchMod(int row)
        {
            Mod mod = capri.ModList.All[modFirst + row];
            try
            {
                bool done = capri.ModList.Set(mod, !mod.On, ModAsk);
                FillMods();
                ModSays(!done ? mod.File.Name + " stays off. Nothing ran." : mod.File.Name + (mod.On ? " is on." : " is off."), false);
                return done;
            }
            catch (ModException error) { ModSays(error.Message, true); }
            catch (IOException error) { ModSays("Couldn't change that mod: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't change that mod: " + error.Message, true); }
            return false;
        }

        internal bool SwitchModForTest(int row) { return SwitchMod(row); }
        internal string ModStatusText { get { return modStatus.Text; } }

        internal Func<ModFile, string, bool> ModAsk;

        private bool AskToRun(ModFile mod, string hash)
        {
            string message = mod.Name + (string.IsNullOrEmpty(mod.Version) ? "" : " v" + mod.Version) + (string.IsNullOrEmpty(mod.Author) ? "" : ", which says it's by " + mod.Author) + ", is a code mod.\n\n"
                + "Code mods run as part of Capri with full access to your PC: your files, your accounts and the API keys she has saved. Nothing limits what it can do, and Capri doesn't check whether it's safe.\n\n"
                + "Only run it if you wrote it, or you've read it and trust who did.\n\nFile fingerprint (SHA-256):\n" + hash + "\n\nRun this code?";
            return MessageBox.Show(this, message, "Run this code mod?", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
        }

        private void RemoveMod(int row)
        {
            Mod mod = capri.ModList.All[modFirst + row];
            try
            {
                capri.ModList.Remove(mod);
                FillMods();
                ModSays(mod.File.Name + " is removed.", false);
            }
            catch (IOException error) { ModSays("Couldn't remove that mod: " + error.Message, true); }
            catch (UnauthorizedAccessException error) { ModSays("Couldn't remove that mod: " + error.Message, true); }
        }

        private void OpenModsFolder()
        {
            try
            {
                Directory.CreateDirectory(capri.ModList.Folder);
                using (Process.Start("explorer.exe", "\"" + capri.ModList.Folder + "\"")) { }
            }
            catch (Exception error)
            {
                if (!(error is IOException || error is UnauthorizedAccessException || error is System.ComponentModel.Win32Exception)) throw;
                ModSays("Couldn't open the mods folder: " + error.Message, true);
            }
        }

        internal void CheckMods(List<string> checks, string output)
        {
            FormWindowState wasState = WindowState;
            Restore();
            Mods mods = capri.ModList;
            Func<string, string, string> sample = delegate(string name, string body)
            {
                string file = Path.Combine(output, name);
                File.WriteAllText(file, body);
                return file;
            };
            string good = sample("custom-names" + Mods.Extension, "{\"format\":1,\"id\":\"custom-names\",\"name\":\"Custom Names\",\"version\":\"1.0.0\",\"author\":\"Avery\",\"description\":\"Answers to more names.\",\"wake_names\":[\"Nova\",\"Big Z\",\"Capricorn\"]}");
            foreach (Mod old in mods.All.ToArray()) mods.Remove(old);
            if (Hands.HeardName("Nova, what time is it?") || !Hands.HeardName("Capri, what time is it?")) throw new Exception("Her names were wrong before any mod.");
            ShowMods(true);
            if (!modsKey.Visible || !modded || modsKey.Text != "Back" || modStatus.Left >= Far || more.Text != "Settings") throw new Exception("The Mods page did not open.");
            if (!ImportMod(good)) throw new Exception("A good mod was refused: " + modStatus.Text);
            if (mods.All.Count != 1 || !File.Exists(Path.Combine(mods.Folder, "custom-names" + Mods.Extension))) throw new Exception("An imported mod was not saved.");
            if (!Hands.HeardName("hey nova, what time is it") || !Hands.HeardName("BIG Z open notepad") || !Hands.HeardName("big   z") || !Hands.HeardName("ask capricorn") || !Hands.HeardName("Capri, hello"))
                throw new Exception("An imported name was not heard.");
            if (Hands.HeardName("the supernova was bright") || Hands.HeardName("novas are bright") || Hands.HeardName("big zebra"))
                throw new Exception("Part of a word counted as her name.");
            if (Hands.NoteFor(Hands.KitFor()).IndexOf("They can also call you Big Z, Capricorn or Nova", StringComparison.Ordinal) < 0 && Hands.NoteFor(Hands.KitFor()).IndexOf("They can also call you Nova, Big Z or Capricorn", StringComparison.Ordinal) < 0)
                throw new Exception("Her brain was not told her nicknames.");
            checks.Add("PASS mods: an imported mod adds whole-word names and tells her brain");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(Path.Combine(output, "mods-page.png"), ImageFormat.Png);
            }
            SwitchMod(0);
            if (Hands.HeardName("hey nova") || !Hands.HeardName("hey capri") || Hands.NoteFor(Hands.KitFor()).Contains("They can also call you")) throw new Exception("Turning a mod off left its names on.");
            mods.Load();
            if (mods.All[0].On) throw new Exception("A mod turned off came back on after loading.");
            SwitchMod(0);
            if (!Hands.HeardName("hey nova")) throw new Exception("Turning a mod back on did not bring its names back.");
            checks.Add("PASS mods: on and off apply straight away and off survives a reload");
            string[,] bad =
            {
                { "bad-json", "{nope" },
                { "bad-format", "{\"format\":99,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\"Nova\"]}" },
                { "bad-path", "{\"format\":1,\"id\":\"../../evil\",\"name\":\"A\",\"wake_names\":[\"Nova\"]}" },
                { "bad-name", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\".*\"]}" },
                { "bad-short", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[\"x\"]}" },
                { "bad-empty", "{\"format\":1,\"id\":\"a\",\"name\":\"A\",\"wake_names\":[]}" }
            };
            for (int i = 0; i < bad.GetLength(0); i++)
                if (ImportMod(sample(bad[i, 0] + Mods.Extension, bad[i, 1])) || mods.All.Count != 1) throw new Exception("A bad mod was let in: " + bad[i, 0]);
            if (ImportMod(sample("wrong.txt", "{}"))) throw new Exception("A file that isn't a mod was let in.");
            if (Directory.GetFiles(mods.Folder, "*" + Mods.Extension).Length != 1) throw new Exception("A refused mod was left in her folder.");
            checks.Add("PASS mods: broken files, bad ids and bad names are refused");
            RemoveMod(0);
            if (mods.All.Count != 0 || Hands.HeardName("hey nova")) throw new Exception("Removing a mod left it behind.");
            ShowSettings(true);
            if (modded || !paged || modStatus.Left < Far) throw new Exception("Settings did not take over from Mods.");
            ShowMods(true);
            if (paged || !modded || modStatus.Left >= Far) throw new Exception("Mods did not take over from Settings.");
            ShowMods(false);
            if (modded || modStatus.Left < Far) throw new Exception("Back did not leave Mods.");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                page.Save(Path.Combine(output, "main-page.png"), ImageFormat.Png);
            }
            WindowState = wasState;
            checks.Add("PASS mods: removing works and Mods and Settings swap cleanly");
            string ears = sample("ears" + Mods.Extension, "{\"format\":1,\"id\":\"ears\",\"name\":\"Ears\",\"sounds_like\":true,\"heard_as\":[\"Coppering\"],\"wake_names\":[\"Choom\"]}");
            string[] missedBefore = { "Copri, what time is it", "hey coppery", "Yokapri what's up", "CAH Bree are you there", "Shum, what time is it", "hey coppering" };
            string[] stayQuiet = { "copy that file", "the copper ore", "a cobra", "show me the map", "that's a shame", "in the room", "keeper of the keys", "hum a song" };
            foreach (string line in missedBefore) if (Hands.HeardName(line)) throw new Exception("Heard a sound-alike with no mod on: " + line);
            if (!ImportMod(ears)) throw new Exception("A sounds_like mod was refused: " + modStatus.Text);
            foreach (string line in missedBefore) if (!Hands.HeardName(line)) throw new Exception("Sounds like missed: " + line);
            foreach (string line in stayQuiet) if (Hands.HeardName(line)) throw new Exception("Sounds like fired on: " + line);
            if (!Hands.NoteFor(Hands.KitFor()).Contains("They can also call you Choom.")) throw new Exception("Her brain wasn't told only her real nickname.");
            SwitchMod(0);
            if (Hands.HeardName("hey coppery") || !Hands.HeardName("hey capri")) throw new Exception("Turning sounds like off left it on.");
            RemoveMod(0);
            checks.Add("PASS mods: sounds like catches her ears' misspellings, skips everyday words and switches off cleanly");
            string log = sample("log" + Mods.Extension, "{\"format\":1,\"id\":\"log\",\"name\":\"Log\",\"hearing_log\":true}");
            mods.LogHearing("nothing on yet", false);
            if (File.Exists(mods.LogPath)) throw new Exception("She kept a hearing log with no mod asking.");
            if (!ImportMod(log)) throw new Exception("A hearing log mod was refused: " + modStatus.Text);
            mods.LogHearing("hello there", false);
            if (!File.ReadAllText(mods.LogPath).Contains("ignored | hello there")) throw new Exception("The hearing log didn't record a line.");
            RemoveMod(0);
            if (File.Exists(mods.LogPath)) throw new Exception("Removing the hearing log mod left its list behind.");
            checks.Add("PASS mods: the hearing log only runs while its mod is on, and goes with it");
            CheckDirectory(checks, output);
        }

        private void CheckDirectory(List<string> checks, string output)
        {
            FormWindowState wasState = WindowState;
            Restore();
            Mods mods = capri.ModList;
            Func<string, long, byte[]> wasGet = ModDirectory.Get;
            Func<ModFile, string, bool> wasAsk = ModAsk;
            string under = Path.Combine(output, "directory-downloads");
            try
            {
                if (ModDirectory.BlobSha(Encoding.ASCII.GetBytes("test content\n")) != "d670460b4b4aece5915caf5c68d12f560a9fe3e4") throw new Exception("A file's git fingerprint came out wrong.");
                byte[] settings = Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"far-names\",\"name\":\"Far Names\",\"wake_names\":[\"Nova\"]}");
                byte[] code = Encoding.UTF8.GetBytes("// capri-mod: 1\n// id: directory-code\n// name: Directory Code\n\npublic class M : ICapriMod { public void Start(ICapriHost h) { } public bool Heard(string w) { return false; } public void Stop() { } }\n");
                string settingsSha = ModDirectory.BlobSha(settings), codeSha = ModDirectory.BlobSha(code);
                string same = "\"type\":\"blob\",\"size\":10,\"sha\":\"" + settingsSha + "\"";
                string tree = "{\"tree\":[{\"path\":\"README.md\"," + same + "},{\"path\":\"mods\",\"type\":\"tree\",\"sha\":\"" + settingsSha + "\"}"
                    + ",{\"path\":\"mods/far-names/far-names.capri-mod\",\"type\":\"blob\",\"size\":" + settings.Length + ",\"sha\":\"" + settingsSha + "\"}"
                    + ",{\"path\":\"mods/directory-code/directory-code.capri-code\",\"type\":\"blob\",\"size\":" + code.Length + ",\"sha\":\"" + codeSha + "\"}"
                    + ",{\"path\":\"mods/../../evil/evil.capri-mod\"," + same + "},{\"path\":\"mods/deep/er/evil.capri-mod\"," + same + "},{\"path\":\"mods/a/evil.exe\"," + same + "},{\"path\":\"mods/a/..evil.capri-mod\"," + same + "}"
                    + ",{\"path\":\"mods/huge/huge.capri-skin\",\"type\":\"blob\",\"size\":999999999999,\"sha\":\"" + settingsSha + "\"},{\"path\":\"mods/badsha/badsha.capri-mod\",\"type\":\"blob\",\"size\":10,\"sha\":\"../../x\"}]}";
                bool wrongBytes = false;
                ModDirectory.Get = delegate(string url, long most)
                {
                    if (url.Contains("/trees/")) return Encoding.UTF8.GetBytes(tree);
                    if (wrongBytes) return Encoding.UTF8.GetBytes("{\"format\":1,\"id\":\"far-names\",\"name\":\"Far Names\",\"wake_names\":[\"Evil\"]}");
                    return url.EndsWith(settingsSha, StringComparison.Ordinal) ? settings : code;
                };
                List<ModListing> found = ModDirectory.Browse();
                if (found.Count != 2 || found[0].Folder != "directory-code" || found[1].Folder != "far-names") throw new Exception("The directory listing let in the wrong files: " + found.Count);
                checks.Add("PASS mods directory: only mods/<folder>/<mod file> is listed; other files, paths out of the folder, huge files and bad fingerprints are not");
                ShowMods(true);
                modBrowsing = true;
                ShowListings(found, null);
                if (modListHead.Text != "MODS DIRECTORY >" || !modSearch.Visible || !modInstalls[1].Visible || !modViews[2].Visible || modInstalls[0].Visible || modSwitches[1].Visible || modNames[2].Text != "far-names" || !modAbouts[1].Text.StartsWith("RUNS CODE.", StringComparison.Ordinal)) throw new Exception("The directory page did not show its mods.");
                using (Bitmap page = new Bitmap(Width, Height))
                {
                    DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                    page.Save(Path.Combine(output, "mods-directory.png"), ImageFormat.Png);
                }
                wrongBytes = true;
                bool refused = false;
                try { ModDirectory.Download(found[1], under); }
                catch (ModException) { refused = true; }
                wrongBytes = false;
                if (!refused || mods.All.Count != 0) throw new Exception("A download that didn't match its listing was kept.");
                string file = ModDirectory.Download(found[1], under);
                if (!FinishInstall(file, null) || mods.All.Count != 1 || mods.All[0].File.Id != "far-names" || !Hands.HeardName("hey nova")) throw new Exception("A mod from the directory did not install: " + modStatus.Text);
                if (Directory.Exists(Path.GetDirectoryName(file))) throw new Exception("The download was left behind.");
                FillMods();
                if (!modAbouts[2].Text.EndsWith("Installed.", StringComparison.Ordinal)) throw new Exception("An installed directory mod wasn't marked.");
                capri.ModList.Remove(mods.All[0]);
                int asked = 0;
                ModAsk = delegate { asked++; return false; };
                file = ModDirectory.Download(found[0], under);
                if (FinishInstall(file, null) || asked != 1 || mods.All.Count != 0) throw new Exception("A code mod from the directory did not ask, or stayed after a no: " + modStatus.Text);
                checks.Add("PASS mods directory: a matching download installs through the normal import and cleans up, one that doesn't match is refused, and a code mod still asks, with no leaving nothing");
                List<ModListing> many = new List<ModListing>();
                foreach (string name in new[] { "big-hat.capri-skin", "blue-hair.capri-mod", "dance-party.capri-code", "red-hair.capri-mod", "red-hat.capri-skin" })
                    many.Add(new ModListing { Folder = Path.GetFileNameWithoutExtension(name), FileName = name, Sha = settingsSha, Size = 2048 });
                ShowListings(many, null);
                if (!modNext.Visible || modPage.Text != "Page 1 of 2" || modNames[3].Text != "dance-party" || ListingAt(3).Folder != "dance-party") throw new Exception("The directory's first page was wrong.");
                modNext.PerformClick();
                if (modPage.Text != "Page 2 of 2" || modNames[1].Text != "red-hair" || ListingAt(2).Folder != "red-hat" || modInstalls[3].Visible) throw new Exception("The directory's second page was wrong.");
                modSearch.Box.Text = "HAIR red";
                if (modShown.Count != 1 || modFirst != 0 || modNames[1].Text != "red-hair" || modNext.Visible || modInstalls[2].Visible) throw new Exception("Search didn't find the mod with every word.");
                modSearch.Box.Text = "";
                modFilters[3].PerformClick();
                if (modShown.Count != 2 || modNames[1].Text != "big-hat" || modNames[2].Text != "red-hat" || modInstalls[3].Visible || modFilters[3].Accent != Cyan || modFilters[0].Accent == Cyan) throw new Exception("The Skins filter was wrong.");
                modSearch.Box.Text = "red";
                if (modShown.Count != 1 || modNames[1].Text != "red-hat" || ListingAt(1).FileName != "red-hat.capri-skin") throw new Exception("Search and a filter didn't work together.");
                modFilters[2].PerformClick();
                if (modShown.Count != 0 || !modEmpty.Visible || modEmpty.Text != "No mods match that." || modEmpty.Top < modFilters[2].Bottom || !modSearch.Visible) throw new Exception("No matches wasn't said.");
                using (Bitmap page = new Bitmap(Width, Height))
                {
                    DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                    page.Save(Path.Combine(output, "mods-directory-empty-search.png"), ImageFormat.Png);
                }
                modFilters[0].PerformClick();
                modSearch.Box.Text = "hat";
                modFilters[3].PerformClick();
                using (Bitmap page = new Bitmap(Width, Height))
                {
                    DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                    page.Save(Path.Combine(output, "mods-directory-search.png"), ImageFormat.Png);
                }
                ClearSearch();
                checks.Add("PASS mods directory: search needs every word in any case, filters go by kind, both work together and with pages, and no matches is said");
                ShowListings(new List<ModListing>(), null);
                if (!modEmpty.Visible || modStatus.Text != "No mods in the directory yet." || modInstalls[1].Visible || !modSearch.Visible || !modFilters[0].Visible) throw new Exception("An empty directory wasn't said, or hid its search.");
                ShowListings(null, "Couldn't reach the mods directory: stand-in");
                if (!modEmpty.Visible || !modStatus.Text.StartsWith("Couldn't reach", StringComparison.Ordinal)) throw new Exception("A failed directory wasn't said.");
                ModDirectory.Get = delegate(string url, long most) { return Encoding.UTF8.GetBytes("{nope"); };
                refused = false;
                try { ModDirectory.Browse(); }
                catch (ModException) { refused = true; }
                if (!refused) throw new Exception("Broken directory data wasn't refused.");
                ShowMods(false);
                if (modBrowsing) throw new Exception("Leaving Mods kept the directory open.");
                ShowMods(true);
                if (modListHead.Text != "INSTALLED >" || modInstalls[1].Visible || modSearch.Visible) throw new Exception("Coming back to Mods stayed on the directory.");
                ShowMods(false);
                checks.Add("PASS mods directory: empty, failed and broken directories are said, and leaving Mods goes back to installed mods");
                checks.Add("LIMIT mods directory: these use stand-in downloads, so real GitHub downloads and its limit of 60 requests an hour need a live check");
            }
            finally
            {
                ModDirectory.Get = wasGet;
                ModAsk = wasAsk;
                modBrowsing = false;
                modListings = new List<ModListing>();
                WindowState = wasState;
            }
        }
    }
}
