using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    // Search settings, like Windows' (app/MainframeSearch.cs): a box at the top of Settings, Ctrl + F (or Cmd + F on a Mac) to
    // get there, a short list as you type, and a jump that lands on the setting with her cyan outline for two seconds.
    internal sealed partial class Mainframe
    {
        internal const string SearchHow = "type what you want to change in the search box at the top of Settings, or press Ctrl + F";
        internal const string NoSettingsMatch = "No settings match";
        internal const string AskInstead = "Try asking Capri instead.";
        private const int SearchShown = 6;
        private const double SearchWide = 288;
        private static readonly Regex Filler = new Regex(@"^(a|an|the|my|her|your|to|of|for|in|on|and|or|is|it|how|do|i|can|where|what|which|please)$");
        private NeonEntry searchBox;
        private Popup searchPopup;
        private StackPanel searchPanel;
        private LayoutTransformControl searchZoom;
        private readonly List<SearchRow> searchRows = new List<SearchRow>();
        private readonly List<Found> searchFound = new List<Found>();
        private List<Found> searchIndex;
        private int searchPicked;
        private Vector searchScroll;
        private Control spotted;
        private readonly DispatcherTimer spotClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };

        private sealed class Found
        {
            internal string Name = "", Section = "", Plain = "", PlainSection = "";
            internal string[] NameWords = Array.Empty<string>(), SectionWords = Array.Empty<string>();
            internal readonly List<string> TopicWords = new List<string>(), FactWords = new List<string>();
            internal readonly List<Item> Items = new List<Item>();
            internal readonly List<KeyValuePair<string, int>> Also = new List<KeyValuePair<string, int>>();
            internal int Page, Score, Order;
            internal double Y, X;
            internal NeonHead Head;
            internal Mod Plug;
        }

        internal NeonEntry SearchBox { get { return searchBox; } }
        internal bool SearchOpen { get { return searchPopup != null && searchPopup.IsOpen; } }
        internal List<string> SearchListed { get { return searchFound.ConvertAll(one => one.Name + " · " + one.Section); } }
        internal Control Spotted { get { return spotted; } }
        internal bool Spots(Control control) { return spotted != null && spotted == control; }

        private void BuildSearch()
        {
            searchBox = Entry("Search settings", "Search settings", 384, 20, 288, 22, delegate
            {
                if (searchFound.Count > 0 && (searchBox.Box.Text ?? "").Trim().Length > 0) GoTo(searchFound[Math.Min(searchPicked, searchFound.Count - 1)]);
            });
            // TextChanged comes late in this Avalonia, after the new text renders; TextChanging lets the list keep up with each key.
            searchBox.Box.TextChanging += (_, _) => Program.Guard(FindSettings);
            searchBox.Box.GotFocus += (_, _) => Program.Guard(delegate { if (!string.IsNullOrEmpty(searchBox.Box.Text)) FindSettings(); });
            searchBox.Box.LostFocus += (_, _) => Program.Guard(CloseSearch);
            searchBox.Box.AddHandler(KeyDownEvent, (_, e) =>
            {
                if (e.Handled || e.Key != Avalonia.Input.Key.Escape || e.KeyModifiers != KeyModifiers.None || (string.IsNullOrEmpty(searchBox.Box.Text) && !SearchOpen)) return;
                e.Handled = true;
                Program.Guard(delegate { searchBox.Box.Text = ""; CloseSearch(); });
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Scroll.ScrollChanged += (_, _) => { if (Scroll.Offset != searchScroll) { searchScroll = Scroll.Offset; CloseSearch(); } };
            Deactivated += (_, _) => CloseSearch();
            PositionChanged += (_, _) => CloseSearch();
            spotClock.Tick += (_, _) => Program.Guard(Unspot);
            Closed += (_, _) => { spotClock.Stop(); CloseSearch(); };
        }

        protected override bool Finds(KeyEventArgs e)
        {
            if (searchBox == null) return false;
            if (e.Key == Avalonia.Input.Key.F && (e.KeyModifiers == KeyModifiers.Control || OperatingSystem.IsMacOS() && e.KeyModifiers == KeyModifiers.Meta)) { OpenSearch(); return true; }
            if (e.KeyModifiers != KeyModifiers.None || (e.Key != Avalonia.Input.Key.Up && e.Key != Avalonia.Input.Key.Down) || !SearchOpen || searchFound.Count == 0 || !searchBox.Box.IsKeyboardFocusWithin) return false;
            searchPicked = (searchPicked + (e.Key == Avalonia.Input.Key.Up ? searchFound.Count - 1 : 1)) % searchFound.Count;
            for (int i = 0; i < searchRows.Count; i++) { searchRows[i].Lit = i == searchPicked; searchRows[i].InvalidateVisual(); }
            return true;
        }

        internal void OpenSearch()
        {
            if (modded) ShowMods(false);
            if (!paged) ShowSettings(true);
            Top();
            UpdateLayout();
            Hold(searchBox);
            searchBox.Box.SelectAll();
            if (!string.IsNullOrEmpty(searchBox.Box.Text)) FindSettings();
        }

        private void FindSettings()
        {
            string asked = searchBox.Box.Text ?? "";
            searchFound.Clear();
            searchPicked = 0;
            if (asked.Trim().Length == 0) { CloseSearch(); return; }
            searchFound.AddRange(SearchFor(asked));
            ShowSearch();
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
            found.NameWords = found.Plain.Length == 0 ? Array.Empty<string>() : found.Plain.Split(' ');
            found.SectionWords = found.PlainSection.Length == 0 ? Array.Empty<string>() : found.PlainSection.Split(' ');
        }

        private static bool Findable(Control control) { return control is NeonButton || control is NeonSwitch || control is NeonSlider || control is NeonEntry; }

        private static string SearchName(Control control)
        {
            string named = AutomationProperties.GetName(control);
            if (control is PlugTile) return "Plug-ins";
            if (control is NeonPicker || control is NeonSlider || control is NeonEntry) return named;
            if (control is NeonSwitch toggle) return toggle.Text;
            if (control is NeonButton button && !string.IsNullOrEmpty(button.Text)) return button.Text;
            return named;
        }

        private static string PageName(int page) { return page == 1 ? "SETTINGS" : page == 2 ? "MODS" : "MAINFRAME"; }

        private static NeonHead HeadOf(Item item, List<Item> heads)
        {
            if (item.Page < 0) return null;
            bool right = item.Page == 1 && item.X >= 380;
            double middle = item.Y + (double.IsNaN(item.Control.Height) ? 0 : item.Control.Height / 2);
            Item best = null;
            foreach (Item head in heads)
                if (head.Page == item.Page && (item.Page != 1 || (head.X >= 380) == right) && head.Y <= middle && (best == null || head.Y > best.Y)) best = head;
            return best == null ? null : (NeonHead)best.Control;
        }

        private List<Found> SearchIndex()
        {
            if (searchIndex != null) return searchIndex;
            List<Item> heads = items.FindAll(item => item.Control is NeonHead);
            var index = new List<Found>();
            var groups = new Dictionary<string, Found>();
            foreach (Item item in items)
            {
                Control control = item.Control;
                if (control == searchBox || !Findable(control)) continue;
                string name = SearchName(control);
                if (string.IsNullOrWhiteSpace(name)) continue;
                NeonHead head = HeadOf(item, heads);
                string section = head != null ? head.Text : PageName(item.Page);
                if (Plainly(name) == Plainly(section)) section = PageName(item.Page);
                string key = item.Page + "|" + Plainly(name) + "|" + section;
                if (!groups.TryGetValue(key, out Found found))
                {
                    found = new Found { Page = item.Page, Head = head, Y = item.Y, X = item.X };
                    Entitle(found, name, section);
                    groups[key] = found;
                    index.Add(found);
                }
                found.Items.Add(item);
                Words(found.TopicWords, AutomationProperties.GetName(control));
                object tip = ToolTip.GetTip(control);
                Words(found.FactWords, tip is TextBlock block ? block.Text : tip as string);
                if (control is PlugTile && found.Also.Count == 0)
                    foreach (string word in new[] { "email", "plug-ins", "plugins", "plug-in", "plugin" }) found.Also.Add(new KeyValuePair<string, int>(Plainly(word), 0));
            }
            Also(index, "Volume", 0, "volume", "loud", "quiet", "louder", "quieter");
            Also(index, "Language", 0, "language");
            Also(index, "Listen for Hey Capri", 0, "listen", "wake word", "hey capri");
            Also(index, "Start at login", 0, "startup", "start up", "start with windows", "login");
            Also(index, "Brain API key", 0, "api key", "key");
            Also(index, "Free key", 1, "api key", "key");
            Also(index, "ChatGPT plan", 2, "api key", "key");
            Also(index, "Lite mode", 0, "lite mode", "slow", "lag", "laggy");
            index.Sort((one, other) => one.Page != other.Page ? one.Page.CompareTo(other.Page) : one.Y != other.Y ? one.Y.CompareTo(other.Y) : one.X.CompareTo(other.X));
            searchIndex = index;
            return index;
        }

        private static void Also(List<Found> index, string name, int rank, params string[] words)
        {
            foreach (Found found in index)
                if (found.Name == name)
                    foreach (string word in words) found.Also.Add(new KeyValuePair<string, int>(Plainly(word), rank));
        }

        private List<Found> Entries()
        {
            var all = new List<Found>(SearchIndex());
            int at = all.FindIndex(one => one.Items.Count > 0 && one.Items[0].Control is PlugTile);
            NeonHead plugs = at < 0 ? null : all[at].Head;
            if (at < 0) at = all.Count;
            foreach (Mod mod in Mods.PluginsOf(pet.ModList.All))
            {
                var plug = new Found { Page = 2, Plug = mod, Head = plugs, Y = PlugTop, X = 28 };
                Entitle(plug, string.IsNullOrEmpty(mod.File.Name) ? mod.File.Id : mod.File.Name, plugs == null ? "PLUG-INS" : plugs.Text);
                Words(plug.FactWords, mod.File.Description);
                all.Insert(at++, plug);
            }
            return all;
        }

        private List<Found> SearchFor(string asked)
        {
            var matches = new List<Found>();
            string query = Plainly(asked);
            if (query.Length == 0) return matches;
            string[] all = query.Split(' ');
            var kept = new List<string>();
            foreach (string word in all) if (!Filler.IsMatch(word)) kept.Add(word);
            string[] words = kept.Count > 0 ? kept.ToArray() : all;
            string core = string.Join(" ", words);
            List<Found> entries = Entries();
            for (int i = 0; i < entries.Count; i++)
            {
                Found found = entries[i];
                found.Order = i;
                found.Score = Score(found, query, core, words);
                if (found.Score > 0) matches.Add(found);
            }
            matches.Sort((one, other) => one.Score != other.Score ? other.Score.CompareTo(one.Score) : one.Order.CompareTo(other.Order));
            if (matches.Count > SearchShown) matches.RemoveRange(SearchShown, matches.Count - SearchShown);
            return matches;
        }

        private static int Score(Found found, string query, string core, string[] words)
        {
            if (found.Plain == query || found.Plain == core || found.Plain + " " + found.PlainSection == query) return 1000;
            int best = 0;
            foreach (KeyValuePair<string, int> also in found.Also)
            {
                if (also.Key == query || also.Key == core) best = Math.Max(best, 1000 - also.Value);
                else if (query.Length >= 2 && also.Key.StartsWith(query, StringComparison.Ordinal)) best = Math.Max(best, 880 - also.Value);
            }
            if (found.Plain.StartsWith(query, StringComparison.Ordinal)) best = Math.Max(best, 900);
            if (best > 0) return best;
            if (Starts(words, found.NameWords, null)) return 800;
            if (Starts(words, found.NameWords, found.SectionWords)) return 750;
            if (found.PlainSection == core) return 700;
            if (core.Length >= 2 && found.PlainSection.StartsWith(core, StringComparison.Ordinal)) return 650;
            if (Starts(words, found.TopicWords, null)) return 600;
            if (core.Length >= 3 && found.Plain.Contains(core)) return 500;
            return Array.TrueForAll(words, word => word.Length >= 3) && Starts(words, found.FactWords, null) ? 300 : 0;
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

        // The list under the box: her menu's plate, a row for each match with its section, the picked one on a smaller plate.
        private void ShowSearch()
        {
            if (!paged || !IsVisible || searchBox == null || !searchBox.IsVisible) { CloseSearch(); return; }
            if (searchPopup == null)
            {
                searchPanel = new StackPanel();
                var plate = new NeonPlate { Menu = true, Child = new Border { Padding = new Thickness(5), Child = searchPanel } };
                var rim = new NeonRim { Plate = plate, IsHitTestVisible = false };
                var surface = new Grid { Width = SearchWide };
                surface.Children.Add(plate);
                surface.Children.Add(rim);
                surface.SizeChanged += (_, _) => surface.Clip = MainframeTheme.Cut(new Rect(surface.Bounds.Size), 10, 4, 10, 4);
                searchZoom = new LayoutTransformControl { Child = surface };
                searchPopup = new Popup { PlacementTarget = searchBox, Placement = PlacementMode.Bottom, VerticalOffset = 4, IsLightDismissEnabled = false, Child = searchZoom };
            }
            searchZoom.LayoutTransform = new ScaleTransform(Zoom, Zoom);
            searchPanel.Children.Clear();
            searchRows.Clear();
            if (searchFound.Count == 0)
            {
                searchPanel.Children.Add(new SearchRow { Setting = NoSettingsMatch, Width = SearchWide - 10, Height = 26 });
                searchPanel.Children.Add(new SearchRow { Setting = AskInstead, Quiet = true, Width = SearchWide - 10, Height = 26 });
            }
            for (int i = 0; i < searchFound.Count; i++)
            {
                int at = i;
                var row = new SearchRow { Setting = searchFound[i].Name, Where = searchFound[i].Section, Lit = i == searchPicked, Width = SearchWide - 10, Height = 26, Cursor = new Cursor(StandardCursorType.Hand) };
                AutomationProperties.SetName(row, searchFound[i].Name + ", " + searchFound[i].Section);
                row.PointerEntered += (_, _) => { searchPicked = at; foreach (SearchRow one in searchRows) { one.Lit = one == row; one.InvalidateVisual(); } };
                row.PointerPressed += (_, e) => { if (!e.GetCurrentPoint(row).Properties.IsLeftButtonPressed) return; e.Handled = true; Program.Guard(delegate { GoTo(searchFound[at]); }); };
                searchRows.Add(row);
                searchPanel.Children.Add(row);
            }
            if (!searchPopup.IsOpen) searchPopup.IsOpen = true;
        }

        private void CloseSearch() { if (searchPopup != null && searchPopup.IsOpen) searchPopup.IsOpen = false; }

        private bool Shows(Item item)
        {
            return item.Control.IsVisible && Findable(item.Control) && item.Control.IsEnabled && (item.Page < 0 || item.Page == (paged ? 1 : modded ? 2 : 0));
        }

        private Control StandIn(Found found)
        {
            Item near = null;
            double best = double.MaxValue;
            List<Item> heads = items.FindAll(item => item.Control is NeonHead);
            foreach (Item item in items)
            {
                if (item.Control == searchBox || item.Page != found.Page || !Shows(item)) continue;
                double distance = Math.Abs(item.Y - found.Y) + (HeadOf(item, heads) == found.Head ? 0 : 100000);
                if (distance < best) { best = distance; near = item; }
            }
            return near == null ? null : near.Control;
        }

        private Control GoTo(Found found)
        {
            CloseSearch();
            if (found.Page == 2)
            {
                if (!modded) ShowMods(true);
                if (found.Plug != null)
                {
                    int at = Mods.PluginsOf(pet.ModList.All).FindIndex(one => string.Equals(one.File.Id, found.Plug.File.Id, StringComparison.OrdinalIgnoreCase));
                    if (at >= 0) plugFirst = at / PlugTiles * PlugTiles;
                    FillMods();
                }
            }
            else if (found.Page == 1)
            {
                if (!paged) ShowSettings(true);
                if (!found.Items.Exists(Shows) && !pet.Saved.AdvancedSettings) { pet.SetAdvancedSettings(true); ShowSettings(true); }
            }
            else if (found.Page == 0)
            {
                if (modded) ShowMods(false);
                if (paged) ShowSettings(false);
            }
            UpdateLayout();
            Control target = null;
            if (found.Plug != null) target = Array.Find(plugTiles, tile => tile.IsVisible && tile.Mod != null && string.Equals(tile.Mod.File.Id, found.Plug.File.Id, StringComparison.OrdinalIgnoreCase));
            if (target == null) { Item shown = found.Items.Find(Shows); target = shown != null ? shown.Control : StandIn(found); }
            if (target == null) return null;
            Hold(target);
            Outline(target);
            return target;
        }

        private void Outline(Control control)
        {
            Unspot();
            spotted = control;
            control.InvalidateVisual();
            spotClock.Start();
        }

        internal void Unspot()
        {
            spotClock.Stop();
            Control was = spotted;
            spotted = null;
            if (was != null) was.InvalidateVisual();
        }

        // Every name and section fits a row, the long ones cut short with room left for their section.
        internal string SearchFits()
        {
            double room = SearchWide - 10 - 16;
            foreach (Found found in Entries())
            {
                double after = MainframeTheme.Words(" · " + found.Section).Width;
                if (room - after < MainframeTheme.Words("MMMMMMMM").Width) return found.Name + " · " + found.Section;
            }
            foreach (string line in new[] { NoSettingsMatch, AskInstead }) if (MainframeTheme.Words(line).Width > room + 0.5) return line;
            return null;
        }
    }

    internal sealed class SearchRow : Control
    {
        internal string Setting = "", Where = "";
        internal bool Lit, Quiet;

        public override void Render(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height;
            context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
            if (Lit) context.DrawGeometry(MainframeTheme.Deep, new Pen(MainframeTheme.DeepEdge, 1), MainframeTheme.Cut(new Rect(0.5, 1.5, Math.Max(0, w - 1), Math.Max(0, h - 3)), 4, 2, 4, 2));
            double x = 8, room = Math.Max(0, w - 16);
            string tail = string.IsNullOrEmpty(Where) ? "" : " · " + Lingo.Of(Where);
            double after = tail.Length == 0 ? 0 : MainframeTheme.Words(tail).Width;
            string name = MainframeTheme.Fitted(Setting, Math.Max(0, room - after));
            double wide = Math.Min(room, MainframeTheme.Words(name).Width);
            MainframeTheme.Write(context, name, new Rect(x, 0, wide + 1, h), Quiet ? MainframeTheme.Faint : Lit ? MainframeTheme.Cyan : MainframeTheme.White);
            if (tail.Length > 0) MainframeTheme.Write(context, tail, new Rect(x + wide, 0, after + 1, h), MainframeTheme.Faint);
        }
    }
}
