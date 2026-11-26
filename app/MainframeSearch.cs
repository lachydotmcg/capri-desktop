using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        internal const string SearchHow = "type what you want to change in the search box at the top of Settings, or press Ctrl + F";
        internal const string NoSettingsMatch = "No settings match";
        internal const string AskInstead = "Try asking Capri instead.";
        private const int SearchShown = 6;
        private static readonly Regex Filler = new Regex(@"^(a|an|the|my|her|your|to|of|for|in|on|and|or|is|it|how|do|i|can|where|what|which|please)$");
        private static Control spotted;
        private readonly Timer spotTimer = new Timer { Interval = 2000 };
        private readonly List<Found> searchFound = new List<Found>();
        private Entry searchBox;
        private Overlay searchList;
        private List<Found> searchIndex;
        private Dictionary<Control, Rectangle> searchBorn;
        private Dictionary<Control, int> searchPages, searchLefts;
        private Dictionary<Control, Head> searchHeads;
        private Rectangle[] searchRows = new Rectangle[0];
        private int searchPicked;

        // A row the search can land on: one setting, or a few alike under one name in one section, like each mod's On / Off.
        private sealed class Found
        {
            internal string Name = "", Section = "", Plain = "", PlainSection = "", Local;
            internal string[] NameWords = new string[0], SectionWords = new string[0];
            internal readonly List<string> TopicWords = new List<string>();
            internal readonly List<string> FactWords = new List<string>();
            internal readonly List<Control> Controls = new List<Control>();
            internal readonly List<KeyValuePair<string, int>> Also = new List<KeyValuePair<string, int>>();
            internal int Page, Y, X, Score, Order;
            internal Head Head;
            internal Mod Plug;
        }

        private void BuildSearch()
        {
            searchBox = Input(384, 20, 288, 22);
            searchBox.Cue = "Search settings";
            searchBox.Box.AccessibleName = "Search settings";
            searchBox.Box.TextChanged += delegate { FindSettings(); };
            searchBox.Box.Enter += delegate { if (searchBox.Box.TextLength > 0) FindSettings(); };
            searchBox.Box.Leave += delegate { CloseSearchList(); };
            Explain(searchBox, "Search settings", "MainframeSearch.cs", null, "Search settings");
            spotTimer.Tick += delegate { Unspot(); };
            Move += delegate { CloseSearchList(); };
            Resize += delegate { CloseSearchList(); };
            Deactivate += delegate { CloseSearchList(); };
            VisibleChanged += delegate { if (!Visible) CloseSearchList(); };
            FormClosed += delegate
            {
                spotTimer.Stop();
                spotTimer.Dispose();
                if (searchList != null) searchList.Dispose();
            };
        }

        internal bool SearchOpen { get { return searchList != null && !searchList.IsDisposed && searchList.Visible; } }

        private static bool Spotted(Control control) { return control != null && spotted == control; }

        private bool SearchKeys(Keys keyData)
        {
            if (searchBox == null) return false;
            if (keyData == (Keys.Control | Keys.F)) { OpenSearch(); return true; }
            if (Held() != searchBox || (keyData & Keys.Modifiers) != Keys.None) return false;
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.Escape)
            {
                if (searchBox.Box.TextLength == 0 && !SearchOpen) return false;
                searchBox.Box.Clear();
                CloseSearchList();
                return true;
            }
            if (key == Keys.Enter)
            {
                if (searchFound.Count > 0 && searchBox.Box.Text.Trim().Length > 0) GoTo(searchFound[Math.Min(searchPicked, searchFound.Count - 1)], true);
                return true;
            }
            if ((key == Keys.Up || key == Keys.Down) && SearchOpen && searchFound.Count > 0)
            {
                searchPicked = (searchPicked + (key == Keys.Up ? searchFound.Count - 1 : 1)) % searchFound.Count;
                ShowSearchList();
                return true;
            }
            return false;
        }

        private void OpenSearch()
        {
            if (modded) ShowMods(false);
            if (!paged) ShowSettings(true);
            ScrollPage(0);
            Hold(searchBox);
            searchBox.Box.SelectAll();
            if (searchBox.Box.TextLength > 0) FindSettings();
        }

        private void FindSettings()
        {
            string asked = searchBox.Box.Text;
            searchFound.Clear();
            searchPicked = 0;
            if (asked.Trim().Length == 0) { CloseSearchList(); return; }
            // Her tip on the box would sit over the list, so typing puts it away.
            if (waitingHint != null && waitingHint.Control == searchBox) { dismissedHint = waitingHint; DismissHint(); }
            searchFound.AddRange(SearchFor(asked));
            ShowSearchList();
        }

        private static string Plainly(string text)
        {
            return Regex.Replace((text ?? "").ToLowerInvariant().Replace("'", "").Replace("’", ""), @"[^\p{L}\p{Nd}]+", " ").Trim();
        }

        private static void Words(List<string> into, string text)
        {
            string plain = Plainly(text);
            if (plain.Length > 0) into.AddRange(plain.Split(' '));
        }

        private static void Entitle(Found found, string name, string section)
        {
            found.Name = name;
            found.Section = section;
            found.Plain = Plainly(name);
            found.PlainSection = Plainly(section);
            found.NameWords = found.Plain.Length == 0 ? new string[0] : found.Plain.Split(' ');
            found.SectionWords = found.PlainSection.Length == 0 ? new string[0] : found.PlainSection.Split(' ');
        }

        private Control TopOf(Control control)
        {
            while (control != null && control.Parent != this) control = control.Parent;
            return control;
        }

        private int PageOf(Control control)
        {
            int page;
            return searchPages != null && searchPages.TryGetValue(control, out page) ? page : -1;
        }

        private int BuiltLeft(Control control)
        {
            int left;
            return searchLefts != null && searchLefts.TryGetValue(control, out left) ? left : control.Left;
        }

        private Head HeadOf(Control control, List<Head> heads)
        {
            int page = PageOf(control);
            Rectangle at;
            if (page < 0 || !searchBorn.TryGetValue(control, out at)) return null;
            bool right = page == 1 && BuiltLeft(control) >= Column;
            float middle = at.Y + at.Height / 2f;
            Head best = null;
            int top = int.MinValue;
            foreach (Head head in heads)
            {
                Rectangle band;
                if (PageOf(head) != page || !searchBorn.TryGetValue(head, out band)) continue;
                if (page == 1 && (BuiltLeft(head) >= Column) != right) continue;
                if (band.Y - 5 <= middle && band.Y > top) { best = head; top = band.Y; }
            }
            return best;
        }

        private static string PageName(int page) { return page == 1 ? "SETTINGS" : page == 2 ? "MODS" : "MAINFRAME"; }

        // Every setting with a tip, once: its name, its section and the words her help has for it.
        private List<Found> SearchIndex()
        {
            if (searchIndex != null) return searchIndex;
            searchBorn = new Dictionary<Control, Rectangle>();
            foreach (LayoutItem item in layout) searchBorn[item.Control] = item.Built;
            searchPages = new Dictionary<Control, int>();
            searchLefts = new Dictionary<Control, int>();
            foreach (KeyValuePair<Control, int> entry in mainLeft) { searchPages[entry.Key] = 0; searchLefts[entry.Key] = entry.Value; }
            foreach (KeyValuePair<Control, int> entry in pageLeft) { searchPages[entry.Key] = 1; searchLefts[entry.Key] = entry.Value; }
            foreach (KeyValuePair<Control, int> entry in modLeft) { searchPages[entry.Key] = 2; searchLefts[entry.Key] = entry.Value; }
            searchPages[modsKey] = 0;
            List<Head> heads = new List<Head>();
            foreach (Control control in Controls)
            {
                Head head = control as Head;
                if (head != null && head.Span > 0) heads.Add(head);
            }
            searchHeads = new Dictionary<Control, Head>();
            foreach (Control control in Controls) searchHeads[control] = HeadOf(control, heads);
            List<Found> index = new List<Found>();
            Dictionary<string, Found> groups = new Dictionary<string, Found>();
            foreach (SettingHint hint in hintRows)
            {
                Control control = TopOf(hint.Control);
                if (control == null || control == searchBox) continue;
                string name = hint.Title.Length > 0 ? hint.Title : control.Text.Length > 0 ? control.Text : hint.Topic.Name;
                int page = PageOf(control);
                Head head;
                searchHeads.TryGetValue(control, out head);
                string section = head != null ? head.Shown : PageName(page);
                if (Plainly(name) == Plainly(section)) section = PageName(page);
                string key = page + "|" + Plainly(name) + "|" + section;
                Found found;
                if (!groups.TryGetValue(key, out found))
                {
                    Rectangle at;
                    searchBorn.TryGetValue(control, out at);
                    found = new Found { Page = page, Head = head, Y = at.Y, X = BuiltLeft(control) };
                    Entitle(found, name, section);
                    groups[key] = found;
                    index.Add(found);
                }
                found.Controls.Add(control);
                Words(found.TopicWords, hint.Topic.Name);
                Words(found.TopicWords, hint.Topic.Label);
                foreach (string phrase in hint.Topic.Words) Words(found.TopicWords, phrase);
                Words(found.TopicWords, control.AccessibleName);
                Words(found.FactWords, hint.Topic.Facts);
                if (control is PlugTile && found.Also.Count == 0)
                    foreach (string word in new[] { "email", "plug-ins", "plugins", "plug-in", "plugin" }) found.Also.Add(new KeyValuePair<string, int>(Plainly(word), 0));
            }
            Also(index, mics, 0, "mic", "microphone");
            Also(index, textEntry, 0, "api key", "key");
            Also(index, freeKey, 1, "api key", "key");
            Also(index, planKey, 2, "api key", "key");
            Also(index, comments, 0, "chatty", "commentary", "game comments");
            Also(index, listenCapri, 0, "listen", "wake word", "hey capri");
            Also(index, volume, 0, "volume", "loud", "quiet", "louder", "quieter");
            Also(index, startup, 0, "start with windows", "startup", "start up");
            Also(index, languages, 0, "language");
            Also(index, liteMode, 0, "lite mode", "slow", "lag", "laggy");
            index.Sort(delegate(Found one, Found other)
            {
                if (one.Page != other.Page) return one.Page.CompareTo(other.Page);
                return one.Y != other.Y ? one.Y.CompareTo(other.Y) : one.X.CompareTo(other.X);
            });
            searchIndex = index;
            return index;
        }

        private static void Also(List<Found> index, Control control, int rank, params string[] words)
        {
            foreach (Found found in index)
                if (found.Controls.Contains(control))
                    foreach (string word in words) found.Also.Add(new KeyValuePair<string, int>(Plainly(word), rank));
        }

        // The index with each plug-in she has as its own row, just before PLUG-INS itself.
        private List<Found> Entries()
        {
            List<Found> all = new List<Found>(SearchIndex());
            int at = all.FindIndex(one => one.Controls.Count > 0 && one.Controls[0] is PlugTile);
            Head plugs = at < 0 ? null : all[at].Head;
            if (at < 0) at = all.Count;
            foreach (Mod mod in Mods.PluginsOf(capri.ModList.All))
            {
                Found plug = new Found { Page = 2, Plug = mod, Head = plugs, Y = PlugTop, X = 28 };
                Entitle(plug, string.IsNullOrEmpty(mod.File.Name) ? mod.File.Id : mod.File.Name, plugs == null ? "PLUG-INS" : plugs.Shown);
                Words(plug.FactWords, mod.File.Description);
                all.Insert(at++, plug);
            }
            return all;
        }

        private static bool Same(Found one, Found other)
        {
            return one == other || one.Plug != null && other.Plug != null && string.Equals(one.Plug.File.Id, other.Plug.File.Id, StringComparison.OrdinalIgnoreCase);
        }

        private List<Found> SearchFor(string asked)
        {
            List<Found> matches = new List<Found>();
            string query = Plainly(asked);
            if (query.Length == 0) return matches;
            string[] all = query.Split(' ');
            List<string> kept = new List<string>();
            foreach (string word in all) if (!Filler.IsMatch(word)) kept.Add(word);
            string[] words = kept.Count > 0 ? kept.ToArray() : all;
            string core = string.Join(" ", words);
            List<Found> entries = Entries();
            for (int i = 0; i < entries.Count; i++)
            {
                Found found = entries[i];
                found.Local = Lingo.Has(found.Name) ? Plainly(Lingo.Of(found.Name)) : null;
                found.Order = i;
                found.Score = Score(found, query, core, words);
                if (found.Score > 0) matches.Add(found);
            }
            matches.Sort(delegate(Found one, Found other) { return one.Score != other.Score ? other.Score.CompareTo(one.Score) : one.Order.CompareTo(other.Order); });
            if (matches.Count > SearchShown) matches.RemoveRange(SearchShown, matches.Count - SearchShown);
            return matches;
        }

        // Its own name first, whole then from the start, and the words it's known by; then every word starting a word of its
        // name, its section or her help's words for it; her help's text last.
        private static int Score(Found found, string query, string core, string[] words)
        {
            if (found.Plain == query || found.Plain == core || found.Plain + " " + found.PlainSection == query || found.Local == query) return 1000;
            int best = 0;
            foreach (KeyValuePair<string, int> also in found.Also)
            {
                if (also.Key == query || also.Key == core) best = Math.Max(best, 1000 - also.Value);
                else if (query.Length >= 2 && also.Key.StartsWith(query, StringComparison.Ordinal)) best = Math.Max(best, 880 - also.Value);
            }
            if (found.Plain.StartsWith(query, StringComparison.Ordinal) || found.Local != null && found.Local.StartsWith(query, StringComparison.Ordinal)) best = Math.Max(best, 900);
            if (best > 0) return best;
            if (Starts(words, found.NameWords, null) || found.Local != null && Starts(words, found.Local.Split(' '), null)) return 800;
            if (Starts(words, found.NameWords, found.SectionWords)) return 750;
            if (found.PlainSection == core) return 700;
            if (core.Length >= 2 && found.PlainSection.StartsWith(core, StringComparison.Ordinal)) return 650;
            if (Starts(words, found.TopicWords, null)) return 600;
            if (core.Length >= 3 && found.Plain.Contains(core)) return 500;
            bool lengthy = true;
            foreach (string word in words) lengthy &= word.Length >= 3;
            return lengthy && Starts(words, found.FactWords, null) ? 300 : 0;
        }

        private static bool Starts(string[] words, IList<string> some, IList<string> more)
        {
            foreach (string word in words)
            {
                bool hit = false;
                foreach (string one in some) if (one.StartsWith(word, StringComparison.Ordinal)) { hit = true; break; }
                if (!hit && more != null) foreach (string one in more) if (one.StartsWith(word, StringComparison.Ordinal)) { hit = true; break; }
                if (!hit) return false;
            }
            return words.Length > 0;
        }

        private static Font SearchFont(float s, bool fallback, string text)
        {
            return fallback ? Look.FallbackMono(12 * s, FontStyle.Regular, GraphicsUnit.Pixel) : Look.Mono(12 * s, FontStyle.Regular, GraphicsUnit.Pixel, text);
        }

        private static string Fit(Graphics graphics, string text, Font font, StringFormat format, float room)
        {
            if (graphics.MeasureString(text, font, PointF.Empty, format).Width <= room) return text;
            for (int keep = text.Length - 1; keep > 0; keep--)
            {
                string cut = text.Substring(0, keep).TrimEnd() + "...";
                if (graphics.MeasureString(cut, font, PointF.Empty, format).Width <= room) return cut;
            }
            return "";
        }

        // The list under the box, in the look of her job panel: a plate with cut corners, a soft cyan glow and edge, and her
        // mono font. The picked row is a smaller plate cut the same way. Crowded says what didn't fit, if anything.
        private Bitmap SearchPicture(List<Found> rows, int picked, float s, bool fallback, out Rectangle[] boxes, out int margin, out string crowded)
        {
            margin = (int)Math.Ceiling(6 * s);
            int wide = searchBox.Width, pad = (int)Math.Round(6 * s), tall = (int)Math.Round(26 * s);
            int count = rows.Count == 0 ? 2 : rows.Count;
            int high = pad * 2 + tall * count;
            Bitmap image = new Bitmap(wide + margin * 2, high + margin * 2, PixelFormat.Format32bppPArgb);
            boxes = new Rectangle[rows.Count];
            crowded = null;
            RectangleF panel = new RectangleF(margin, margin, wide, high);
            using (Graphics graphics = Graphics.FromImage(image))
            using (GraphicsPath plate = Look.Plate(panel, 8 * s, -1, panel.Top + panel.Height / 2, 0, true))
            using (StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone())
            {
                format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap;
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                using (Pen glow = new Pen(Color.FromArgb(46, Cyan), 7 * s)) graphics.DrawPath(glow, plate);
                using (Brush fill = new SolidBrush(Neon.Sheet)) graphics.FillPath(fill, plate);
                graphics.SetClip(plate);
                using (Pen scan = new Pen(Color.FromArgb(9, 255, 255, 255), Math.Max(1, s)))
                    for (float line = panel.Top; line < panel.Bottom; line += 3 * s) graphics.DrawLine(scan, panel.Left, line, panel.Right, line);
                for (int i = 0; i < count; i++)
                {
                    Rectangle row = new Rectangle(margin + pad, margin + pad + i * tall, wide - pad * 2, tall);
                    bool found = rows.Count > 0, lit = found && i == picked;
                    string name = found ? Lingo.Of(rows[i].Name) : Lingo.Of(i == 0 ? NoSettingsMatch : AskInstead);
                    string tail = found ? " · " + Lingo.Of(rows[i].Section) : "";
                    if (found) boxes[i] = row;
                    if (lit)
                        using (GraphicsPath bar = Look.Plate(new RectangleF(row.Left, row.Top + s, row.Width, row.Height - 2 * s), 4 * s, -1, row.Top + row.Height / 2f, 0, true))
                        {
                            using (Brush wash = new SolidBrush(Deep)) graphics.FillPath(wash, bar);
                            using (Pen rim = new Pen(Neon.DeepEdge, Math.Max(1, s))) graphics.DrawPath(rim, bar);
                        }
                    using (Font font = SearchFont(s, fallback, name + tail))
                    {
                        float x = row.Left + 8 * s, room = row.Width - 16 * s;
                        float y = row.Top + (row.Height - graphics.MeasureString("Ag", font, PointF.Empty, format).Height) / 2;
                        float after = tail.Length == 0 ? 0 : graphics.MeasureString(tail, font, PointF.Empty, format).Width;
                        string shown = Fit(graphics, name, font, format, room - after);
                        if (crowded == null && shown != name && (!found || room - after < graphics.MeasureString("MMMMMMMM", font, PointF.Empty, format).Width))
                            crowded = "\"" + name + tail + "\" doesn't fit its row of " + (int)room + " px";
                        using (Brush ink = new SolidBrush(!found && i == 1 ? Neon.SheetDim : lit ? Cyan : Neon.SheetInk))
                            graphics.DrawString(shown, font, ink, x, y, format);
                        if (tail.Length > 0)
                            using (Brush dim = new SolidBrush(Neon.SheetDim))
                                graphics.DrawString(tail, font, dim, x + graphics.MeasureString(shown, font, PointF.Empty, format).Width, y, format);
                    }
                }
                graphics.ResetClip();
                using (Pen edge = new Pen(Cyan, 2 * s)) graphics.DrawPath(edge, plate);
            }
            return image;
        }

        private Point SearchListAt(Size size, int margin)
        {
            Rectangle box = searchBox.RectangleToScreen(searchBox.ClientRectangle);
            int gap = (int)Math.Round(4 * Unit());
            Rectangle area = Screen.FromRectangle(box).WorkingArea;
            Point at = new Point(box.Left - margin, box.Bottom + gap - margin);
            if (at.Y + size.Height - margin > area.Bottom && box.Top - gap - size.Height + margin >= area.Top) at.Y = box.Top - gap - size.Height + margin;
            return at;
        }

        private void ShowSearchList()
        {
            if (searchBox == null || !Visible || WindowState == FormWindowState.Minimized || Hidden(searchBox) || searchBox.Box.Text.Trim().Length == 0) { CloseSearchList(); return; }
            if (searchBox.Top < 0 || searchBox.Bottom > ClientSize.Height) ScrollPage(0);
            int margin;
            string crowded;
            Bitmap picture = SearchPicture(searchFound, searchPicked, Unit(), layoutFallback, out searchRows, out margin, out crowded);
            if (searchList == null || searchList.IsDisposed)
            {
                searchList = new Overlay("Search settings") { Above = false, Cursor = Look.Hand };
                searchList.Owner = this;
                searchList.MouseMove += delegate(object sender, MouseEventArgs e)
                {
                    int row = SearchRowAt(e.Location);
                    if (row >= 0 && row != searchPicked) { searchPicked = row; ShowSearchList(); }
                };
                searchList.MouseDown += delegate(object sender, MouseEventArgs e)
                {
                    int row = SearchRowAt(e.Location);
                    if (e.Button == MouseButtons.Left && row >= 0 && row < searchFound.Count) GoTo(searchFound[row], false);
                };
            }
            searchList.Present(picture, SearchListAt(picture.Size, margin));
        }

        private int SearchRowAt(Point point)
        {
            for (int i = 0; i < searchRows.Length; i++) if (searchRows[i].Contains(point)) return i;
            return -1;
        }

        private void CloseSearchList()
        {
            if (searchList == null || searchList.IsDisposed || !searchList.Visible) return;
            searchList.Hide();
            searchList.Forget();
        }

        private bool Shows(Control control)
        {
            return control.Visible && control.Left < Far / 2 && !Hidden(control) && Steerable(control);
        }

        // Opens the page a row is on and gives back the setting to land on: itself, or while it's hidden, the closest option in
        // its section, so a hidden one still takes you where it'll show.
        private Control OpenFound(Found found)
        {
            if (found.Page == 2)
            {
                if (!modded) ShowMods(true);
                if (found.Plug != null)
                {
                    ShowPlug(found.Plug);
                    FillPlugs();
                    foreach (PlugTile tile in plugTiles)
                        if (tile.Visible && tile.Mod != null && string.Equals(tile.Mod.File.Id, found.Plug.File.Id, StringComparison.OrdinalIgnoreCase)) return tile;
                }
            }
            else if (found.Page == 1)
            {
                if (!paged) ShowSettings(true);
                if (simple && found.Controls.TrueForAll(Folded))
                {
                    capri.ToggleAdvancedSettings();
                    ShowSettings(true);
                }
            }
            else if (found.Page == 0)
            {
                if (modded) ShowMods(false);
                if (paged) ShowSettings(false);
            }
            foreach (Control control in found.Controls) if (Shows(control)) return control;
            return StandIn(found);
        }

        private Control StandIn(Found found)
        {
            Control near = null;
            long best = long.MaxValue;
            foreach (Control control in Controls)
            {
                Rectangle at;
                Head head;
                if (control == searchBox || !control.Enabled || !Shows(control) || PageOf(control) != found.Page || !searchBorn.TryGetValue(control, out at)) continue;
                searchHeads.TryGetValue(control, out head);
                long distance = Math.Abs(at.Y - found.Y) + (head == found.Head ? 0 : 100000L);
                if (distance < best) { best = distance; near = control; }
            }
            return near;
        }

        private void ScrollToFound(Control target)
        {
            if (modded || pageView >= ScrollTall) return;
            int edge = Scaled(12);
            if (target.Top >= edge && target.Bottom <= ClientSize.Height - edge) return;
            ScrollPage(Math.Max(0, pageScroll + (int)Math.Floor((target.Top - ClientSize.Height / 3f) / Unit())));
        }

        private Control GoTo(Found found, bool keys)
        {
            CloseSearchList();
            DismissHint();
            Control target = OpenFound(found);
            if (target == null) return null;
            ScrollToFound(target);
            if (target.Enabled)
            {
                Entry entry = target as Entry;
                if (keys) Hold(target);
                else if (entry != null) entry.Box.Select();
                else target.Select();
            }
            Spot(target);
            return target;
        }

        // A steady outline in her cyan on the setting a search landed on, gone after two seconds. It's drawn where her
        // keyboard mark goes, so it follows the setting's own cut corners.
        private void Spot(Control control)
        {
            Unspot();
            spotted = control;
            control.Invalidate();
            spotTimer.Start();
        }

        private void Unspot()
        {
            spotTimer.Stop();
            Control was = spotted;
            spotted = null;
            if (was != null && !was.IsDisposed) was.Invalidate();
        }
    }
}
