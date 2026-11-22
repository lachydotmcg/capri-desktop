using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        internal void CheckSearch(List<string> checks, string output)
        {
            bool pagedWas = paged, moddedWas = modded, shownWas = Visible, advancedWas = capri.AdvancedSaved;
            FormWindowState stateWas = WindowState;
            int sizeWas = capri.MainframeSize;
            Point atWas = Location;
            Control heldWas = Held();
            Mods mods = capri.ModList;
            string startersFile = Path.Combine(mods.PluginFolder, "starters.txt"), startersAside = startersFile + ".aside";
            bool startersWere = File.Exists(startersFile), aside = false;
            List<string> pluginsWere = Mods.PluginsOf(mods.All).ConvertAll(one => one.File.Id);
            try
            {
                tipsDriven = true;
                Restore();
                LayoutScale(96, 100);
                SizeMainframe(100);
                ShowMods(false);
                ShowSettings(false);
                SearchCtrlF(checks);
                SearchComments(checks, output);
                SearchMic(checks, output);
                if (!pluginsWere.Contains(Mods.MailStarter))
                {
                    // Her list of starters already made keeps a removed Email from coming back, so it waits aside meanwhile.
                    if (startersWere) { File.Move(startersFile, startersAside); aside = true; }
                    mods.AddStarters();
                    mods.Load();
                }
                SearchEmail(checks);
                SearchNothing(checks);
                SearchFits(checks);
                SearchReaches(checks);
                SearchSimple(checks);
                string said = SelfKnow.Matching("how do I search my settings?") ?? "";
                SettingHint tip;
                if (!said.Contains(SearchHow) || !hints.TryGetValue(searchBox, out tip) || SelfKnow.Setting("MainframeSearch.cs", "Search settings") != tip.Topic || searchBox.AccessibleDescription != tip.Text)
                    throw new Exception("Capri doesn't know her settings search: " + said);
                checks.Add("PASS settings search: Capri knows the box is there and how to use it, and the box's own tip says the same");
            }
            finally
            {
                tipsDriven = false;
                Unspot();
                CloseSearchList();
                searchBox.Box.Text = "";
                DismissHint();
                dismissedHint = overHint = null;
                foreach (Mod one in mods.All.ToArray()) if (one.Plugin && !pluginsWere.Contains(one.File.Id)) mods.Remove(one);
                try
                {
                    if ((!startersWere || aside) && File.Exists(startersFile)) File.Delete(startersFile);
                    if (aside) File.Move(startersAside, startersFile);
                }
                catch (IOException) { }
                mods.Load();
                FillMods();
                if (capri.AdvancedSaved != advancedWas) capri.ToggleAdvancedSettings();
                LayoutScale(0, 0);
                SizeMainframe(sizeWas);
                ShowMods(false);
                ShowSettings(pagedWas);
                if (moddedWas) ShowMods(true);
                Location = atWas;
                if (heldWas != null && heldWas.CanFocus) heldWas.Focus();
                DismissHint();
                if (!shownWas) Hide();
                else if (WindowState != stateWas) WindowState = stateWas;
            }
        }

        private void TypeSearch(string words)
        {
            OpenSearch();
            searchBox.Box.Text = words;
            searchBox.Box.SelectionStart = searchBox.Box.TextLength;
        }

        private string Listed(List<Found> found)
        {
            List<string> rows = found.ConvertAll(one => one.Name + " · " + one.Section);
            return rows.Count == 0 ? "nothing" : string.Join(", ", rows.ToArray());
        }

        private static string Called(Control control)
        {
            if (control == null) return "nothing";
            return control.Text.Length > 0 ? control.Text : control.AccessibleName ?? control.GetType().Name;
        }

        private void SearchCtrlF(List<string> checks)
        {
            Hold(more);
            bool main = Strike(Keys.Control | Keys.F) && paged && !modded && Held() == searchBox && pageScroll == 0;
            ShowMods(true);
            Hold(modBrowse);
            bool fromMods = Strike(Keys.Control | Keys.F) && paged && !modded && Held() == searchBox;
            bool fromLow = true;
            if (ScrollMost > 0)
            {
                ScrollPage(ScrollMost);
                Hold(clipsFolder);
                fromLow = Strike(Keys.Control | Keys.F) && pageScroll == 0 && Held() == searchBox;
            }
            bool row = searchBox.Top == more.Top && searchBox.Height == more.Height && searchBox.Left > advancedKey.Right && searchBox.Right < minimize.Left;
            if (!main || !fromMods || !fromLow || !row)
                throw new Exception("Ctrl + F didn't put the keyboard in the search box: from her main page " + main + ", from Mods " + fromMods + ", from low on Settings " + fromLow + ", in the title row " + row + " at " + searchBox.Bounds);
            checks.Add("PASS settings search: the box sits in the Settings title row between Advanced Settings and minimize, and Ctrl + F opens Settings from her main page or Mods, scrolls back to the top and puts the keyboard in it");
        }

        private void SearchComments(List<string> checks, string output)
        {
            TypeSearch("comments");
            if (!SearchOpen || searchFound.Count == 0 || !searchFound[0].Controls.Contains(comments) || searchFound[0].Name != "Comments" || searchFound[0].Section != "GAMES")
                throw new Exception("\"comments\" didn't list GAMES > Comments first: " + Listed(searchFound));
            Stopwatch clock = Stopwatch.StartNew();
            bool jumped = Strike(Keys.Enter);
            Update();
            if (!jumped || SearchOpen || !paged || modded || Held() != comments || !Spotted(comments) || !ClientRectangle.Contains(comments.Bounds) || !spotTimer.Enabled)
                throw new Exception("Enter on Comments didn't open Settings with it scrolled into view, focused and outlined: held " + Called(Held()) + " at " + comments.Bounds + " in " + ClientRectangle + ", list open " + SearchOpen);
            string jump = Path.Combine(output, "settings-search-jump.png");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                DrawRims(page);
                page.Save(jump, ImageFormat.Png);
            }
            while (Spotted(comments) && clock.ElapsedMilliseconds < 4000) { Application.DoEvents(); System.Threading.Thread.Sleep(10); }
            long gone = clock.ElapsedMilliseconds;
            if (spotted != null || gone < 1800 || gone > 3500) throw new Exception("The outline on Comments didn't go after about 2 seconds: " + gone + " ms, still on " + Called(spotted));
            string after = Path.Combine(output, "settings-search-after.png");
            using (Bitmap page = new Bitmap(Width, Height))
            {
                DrawToBitmap(page, new Rectangle(Point.Empty, Size));
                DrawRims(page);
                page.Save(after, ImageFormat.Png);
            }
            string missed = OutlineMissed(jump, after, comments);
            File.Delete(after);
            if (missed != null) throw new Exception("The cyan outline round Comments is wrong in settings-search-jump.png: " + missed);
            DismissHint();
            checks.Add("PASS settings search: \"comments\" lists Comments · GAMES first; Enter opens Settings, scrolls Comments into view, gives it the keyboard and outlines it in her cyan, steady, until it goes after " + gone + " ms; settings-search-jump.png saved");
        }

        // Her cyan round all four sides of a setting while it's outlined, against the same setting once the outline went, the
        // way her mark pictures check white. Where the setting is cyan itself, like ON while it's on, there's nothing to tell.
        private string OutlineMissed(string outlined, string plain, Control control)
        {
            Rectangle around = control.Bounds;
            Toggle toggle = control as Toggle;
            if (toggle != null) around = new Rectangle(control.Right - (int)(toggle.Switch * Unit()), control.Top, (int)(toggle.Switch * Unit()), control.Height);
            int told = 0;
            using (Bitmap with = new Bitmap(outlined))
            using (Bitmap without = new Bitmap(plain))
            {
                if (!new Rectangle(Point.Empty, with.Size).Contains(around) || without.Size != with.Size) return "it's outside the picture";
                for (int i = 1; i <= 3; i++)
                {
                    int x = around.Left + around.Width * i / 4, y = around.Top + around.Height * i / 4;
                    int[][] spots = { new[] { x, around.Top + 1, 0, 1 }, new[] { x, around.Bottom - 2, 0, 1 }, new[] { around.Left + 1, y, 1, 0 }, new[] { around.Right - 2, y, 1, 0 } };
                    foreach (int[] spot in spots)
                    {
                        if (CyanNear(without, spot)) continue;
                        if (!CyanNear(with, spot)) return "no cyan at " + spot[0] + "," + spot[1];
                        told++;
                    }
                }
            }
            return told >= 6 ? null : "only " + told + " of its 12 spots could show it";
        }

        private static bool CyanNear(Bitmap picture, int[] spot)
        {
            for (int step = -1; step <= 1; step++)
            {
                Color colour = picture.GetPixel(spot[0] + step * spot[2], spot[1] + step * spot[3]);
                if (Math.Abs(colour.R - Cyan.R) <= 8 && Math.Abs(colour.G - Cyan.G) <= 8 && Math.Abs(colour.B - Cyan.B) <= 8) return true;
            }
            return false;
        }

        private void SearchMic(List<string> checks, string output)
        {
            TypeSearch("mic");
            if (!SearchOpen || searchFound.Count < 2 || !searchFound[0].Controls.Contains(mics) || searchFound[0].Section != "VOICE")
                throw new Exception("\"mic\" didn't list her mic first: " + Listed(searchFound));
            SaveSearchPicture(Path.Combine(output, "settings-search.png"));
            bool down = Strike(Keys.Down) && searchPicked == 1 && SearchOpen;
            bool up = Strike(Keys.Up) && searchPicked == 0 && Strike(Keys.Up) && searchPicked == searchFound.Count - 1 && Strike(Keys.Down) && searchPicked == 0;
            bool jumped = Strike(Keys.Enter);
            if (!down || !up || !jumped || paged || modded || Held() != mics || !Spotted(mics))
                throw new Exception("Up, Down and Enter on \"mic\" went wrong: down " + down + ", up and round " + up + ", landed on " + Called(Held()) + " with Settings " + paged);
            Unspot();
            checks.Add("PASS settings search: \"mic\" lists her mic first (Microphone · VOICE) by its other name, Up and Down move through the list and wrap, and Enter goes back to her main page and lands on it; settings-search.png saved with the list open");
        }

        // The Mainframe with its list where it shows on screen, on a dark ground, like her tip previews.
        private void SaveSearchPicture(string path)
        {
            Rectangle list = searchList.Bounds;
            Rectangle whole = Rectangle.Union(Bounds, list);
            whole.Inflate(Px(12), Px(12));
            using (Bitmap image = new Bitmap(whole.Width, whole.Height))
            using (Bitmap panel = new Bitmap(Width, Height))
            using (Graphics drawing = Graphics.FromImage(image))
            {
                drawing.Clear(Color.FromArgb(24, 24, 30));
                DrawToBitmap(panel, new Rectangle(Point.Empty, Size));
                DrawRims(panel);
                drawing.DrawImageUnscaled(panel, Left - whole.Left, Top - whole.Top);
                drawing.DrawImage(searchList.Image, new Rectangle(list.Left - whole.Left, list.Top - whole.Top, list.Width, list.Height));
                image.Save(path, ImageFormat.Png);
            }
        }

        private void SearchEmail(List<string> checks)
        {
            TypeSearch("email");
            Found first = searchFound.Count == 0 ? null : searchFound[0];
            if (first == null || first.Plug == null || first.Name != "Email" || first.Section != "PLUG-INS")
                throw new Exception("\"email\" didn't list the Email plug-in under PLUG-INS first: " + Listed(searchFound));
            bool jumped = Strike(Keys.Enter);
            PlugTile tile = Held() as PlugTile;
            if (!jumped || !modded || paged || tile == null || tile.Mod == null || tile.Mod.File.Name != "Email" || !Spotted(tile))
                throw new Exception("Enter on Email didn't open Mods > PLUG-INS on its tile: Mods " + modded + ", held " + Called(Held()));
            Unspot();
            TypeSearch("plug-ins");
            if (searchFound.Count == 0 || searchFound[0].Plug != null || searchFound[0].Section != "MODS" || !(searchFound[0].Controls[0] is PlugTile))
                throw new Exception("\"plug-ins\" didn't list PLUG-INS first: " + Listed(searchFound));
            checks.Add("PASS settings search: \"email\" lists Email · PLUG-INS first and Enter opens Mods on its tile, outlined, and \"plug-ins\" lists the section itself");
        }

        private void SearchNothing(List<string> checks)
        {
            TypeSearch("zqxv wobbleflump");
            DismissHint();
            if (searchFound.Count != 0 || !SearchOpen) throw new Exception("A nonsense word listed " + Listed(searchFound) + ", or no list showed.");
            Rectangle[] rows;
            int margin;
            string crowded;
            using (Bitmap empty = SearchPicture(searchFound, 0, Unit(), layoutFallback, out rows, out margin, out crowded))
                if (rows.Length != 0 || crowded != null || empty.Height < Px(60)) throw new Exception("The empty list didn't show " + NoSettingsMatch + " and the line to ask her: " + crowded);
            bool stays = Strike(Keys.Enter) && paged && Held() == searchBox && spotted == null && SearchOpen;
            bool cleared = Strike(Keys.Escape) && searchBox.Box.TextLength == 0 && !SearchOpen && paged && Held() == searchBox;
            bool leaves = Strike(Keys.Escape) && !paged;
            if (!stays || !cleared || !leaves) throw new Exception("Enter or Esc on no match went wrong: Enter stays " + stays + ", Esc clears " + cleared + ", Esc again leaves Settings " + leaves);
            checks.Add("PASS settings search: a word that matches nothing shows \"" + NoSettingsMatch + "\" and \"" + AskInstead + "\", Enter does nothing there, Esc clears the box and closes the list, and the next Esc leaves Settings as before");
        }

        private void SearchFits(List<string> checks)
        {
            int[][] scales = { new[] { 96, 0 }, new[] { 96, 1 }, new[] { 144, 0 } };
            try
            {
                foreach (int[] scale in scales)
                {
                    LayoutScale(scale[0], 100, scale[1] == 1);
                    ShowSettings(true);
                    ScrollPage(0);
                    string crowded = SearchCrowded();
                    if (crowded != null) throw new Exception("The search doesn't fit at " + scale[0] * 100 / 96 + "%" + (scale[1] == 1 ? " on the fallback font: " : ": ") + crowded);
                }
            }
            finally { LayoutScale(96, 100); }
            checks.Add("PASS settings search fit: the box and its cue fit the Settings title row without touching Advanced Settings or minimize, and the list sits under the box inside the Mainframe with every name and section fitting its row, and the no-match lines too, at 100% and 150% and on the fallback font");
        }

        private string SearchCrowded()
        {
            if (searchBox.Top != more.Top || searchBox.Height != more.Height || searchBox.Left < advancedKey.Right + Scaled(4) || searchBox.Right > minimize.Left - Scaled(4))
                return "the box left the title row: " + searchBox.Bounds;
            foreach (Control control in Controls)
            {
                if (control == searchBox || Hidden(control) || !control.Visible || (control is Label && control.Text.Length == 0)) continue;
                if (control.Bounds.IntersectsWith(searchBox.Bounds)) return "the box overlaps " + Called(control);
            }
            using (Bitmap paper = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(paper))
            {
                string problem = CheckText(graphics, searchBox, Point.Empty, new List<KeyValuePair<string, Rectangle>>(), true);
                if (problem != null) return problem;
            }
            Rectangle box = searchBox.RectangleToScreen(searchBox.ClientRectangle), frame = RectangleToScreen(ClientRectangle);
            List<Found> all = Entries();
            List<List<Found>> lists = new List<List<Found>>();
            for (int first = 0; first < all.Count; first += SearchShown) lists.Add(all.GetRange(first, Math.Min(SearchShown, all.Count - first)));
            lists.Add(new List<Found>());
            Rectangle area = Screen.FromRectangle(box).WorkingArea;
            foreach (List<Found> rows in lists)
            {
                Rectangle[] boxes;
                int margin;
                string crowded;
                using (Bitmap picture = SearchPicture(rows, 0, Unit(), layoutFallback, out boxes, out margin, out crowded))
                {
                    if (crowded != null) return crowded;
                    Point at = SearchListAt(picture.Size, margin);
                    Rectangle panel = new Rectangle(at.X + margin, at.Y + margin, picture.Width - margin * 2, picture.Height - margin * 2);
                    if (panel.Top <= box.Bottom || panel.Left != box.Left || panel.Right != box.Right) return "the list isn't under the box: " + panel + " under " + box;
                    if (!area.Contains(panel) || panel.Left < frame.Left) return "the list leaves the screen or the Mainframe: " + panel + " in " + area;
                    for (int i = 0; i < boxes.Length; i++)
                    {
                        if (!new Rectangle(margin, margin, panel.Width, panel.Height).Contains(boxes[i])) return "row " + rows[i].Name + " pokes out of the list";
                        if (i > 0 && boxes[i].IntersectsWith(boxes[i - 1])) return "rows " + rows[i - 1].Name + " and " + rows[i].Name + " overlap";
                    }
                }
            }
            return null;
        }

        private void SearchReaches(List<string> checks)
        {
            List<Found> index = SearchIndex();
            int rows = 0;
            foreach (SettingHint hint in hintRows)
            {
                Control top = TopOf(hint.Control);
                if (top == searchBox) continue;
                rows++;
                if (!index.Exists(one => one.Controls.Contains(top))) throw new Exception("A setting with a tip isn't in the search: " + hint.Title);
            }
            int landed = 0, beside = 0, named = 0;
            foreach (Found found in Entries())
            {
                List<Found> listed = SearchFor(found.Name);
                if (!listed.Exists(one => Same(one, found))) listed = SearchFor(found.Name + " " + found.Section);
                else named++;
                if (!listed.Exists(one => Same(one, found))) throw new Exception(found.Name + " · " + found.Section + " can't be found by its name and section: " + Listed(listed));
                Control target = GoTo(found, true);
                bool page = found.Page < 0 || (found.Page == 0 ? !paged && !modded : found.Page == 1 ? paged : modded);
                if (target == null || !Spotted(target) || !page || !Shows(target) || !ClientRectangle.IntersectsWith(target.Bounds))
                    throw new Exception("Jumping to " + found.Name + " · " + found.Section + " didn't land on the right page: " + Called(target) + ", Settings " + paged + ", Mods " + modded);
                if (found.Controls.Contains(target) || found.Plug != null) landed++;
                else if (found.Controls.Exists(Shows)) throw new Exception("Jumping to " + found.Name + " · " + found.Section + " landed beside it while it shows");
                else beside++;
            }
            Unspot();
            DismissHint();
            checks.Add("PASS settings search reaches everything: all " + rows + " settings with a tip are in it, each found by its name (" + named + " of " + (landed + beside) + " rows), or with its section where names are shared, and a jump lands on each, or while it's hidden on the closest option in its section (" + beside + " hidden right now)");
        }

        private void SearchSimple(List<string> checks)
        {
            string name = null;
            capri.InSimpleSettings(delegate
            {
                ShowSettings(true);
                Found folded = SearchIndex().Find(one => one.Controls.Contains(liteMode));
                if (folded == null || !folded.Controls.TrueForAll(Folded)) folded = SearchIndex().Find(one => one.Page == 1 && one.Controls.Count > 0 && one.Controls.TrueForAll(Folded) && one.Controls.Exists(Steerable));
                if (!simple || folded == null) throw new Exception("Simple Settings folded nothing away to search for.");
                name = folded.Name;
                TypeSearch(folded.Name);
                if (searchFound.Count == 0 || !Same(searchFound[0], folded)) throw new Exception("\"" + folded.Name + "\" wasn't listed first in Simple Settings: " + Listed(searchFound));
                bool jumped = Strike(Keys.Enter);
                if (!jumped || simple || !paged || spotted == null || !folded.Controls.Contains(spotted) || !Shows(spotted))
                    throw new Exception("Jumping to " + folded.Name + " from Simple Settings didn't show Advanced Settings with it: simple " + simple + ", landed on " + Called(spotted));
                Unspot();
            });
            ShowSettings(true);
            checks.Add("PASS settings search in Simple Settings: jumping to " + name + ", which only Advanced Settings shows, switches to Advanced Settings and lands on it");
        }
    }
}
