using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace CapriDesktop
{
    internal sealed class MemorySheet : MainframeWindow
    {
        internal const int Wide = 520, ListTop = 158;
        private const int Left = 28, Room = 464, KeyA = 346, KeyB = 414;
        private readonly Pet pet;
        private readonly NeonEntry add, search;
        private readonly int fixedCount;
        private readonly DispatcherTimer stripClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
        private readonly DispatcherTimer armClock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        private List<MemoryBook.Item> items = new List<MemoryBook.Item>();
        private List<MemoryBook.Day> days = new List<MemoryBook.Day>();
        private List<MemoryBook.Said> lines;
        private string editing, editText = "", openDay, stripSay, stripUndo, armed;
        private bool closed, cut, loaded, busy;
        private int ticket;
        internal readonly Dictionary<string, Control> Rows = new Dictionary<string, Control>();

        internal MemorySheet(Pet owner) : base("Her memory")
        {
            pet = owner;
            Steered = true;
            ShowInTaskbar = false;
            Put(Page, new NeonCaption { Text = "HER MEMORY >", Size = 20, Bold = true, Display = true }, 24, 15, 220, 29);
            NeonButton text = Put(Page, new NeonButton { Text = "Edit as text" }, 350, 20, 106, 22, "Edit as text");
            text.Click += (_, _) => pet.OpenMemories();
            Rows["Edit as text"] = text;
            NeonButton shut = Put(Page, new NeonButton { Glyph = 2 }, Wide - 50, 20, 26, 22, "Close");
            shut.Click += (_, _) => Close();
            Put(Page, new NeonCaption { Text = "What Capri remembers about you. It stays on this computer.", Ink = MainframeTheme.Faint, Size = 34.0 / 3, Ellipsis = true }, Left, 52, Room, 18);
            add = Put(Page, new NeonEntry(Lingo.Of("Tell her something to remember")), Left, 82, 372, 28, "Tell her something to remember");
            Rows["Tell her something to remember"] = add;
            NeonButton addKey = Put(Page, new NeonButton { Text = "Add", Accent = MainframeTheme.Cyan, Loud = true }, 408, 82, 84, 28, "Add");
            addKey.Click += (_, _) => AddNow();
            Rows["Add"] = addKey;
            add.Box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Program.Guard(AddNow); } };
            search = Put(Page, new NeonEntry(Lingo.Of("Search her memory")), Left, 118, Room, 28, "Search her memory");
            Rows["Search her memory"] = search;
            search.Box.TextChanged += (_, _) => Program.Guard(() => { armed = null; Build(); });
            fixedCount = Page.Children.Count;
            stripClock.Tick += (_, _) => Program.Guard(() => { stripClock.Stop(); stripSay = stripUndo = null; Build(); });
            armClock.Tick += (_, _) => Program.Guard(() => { armClock.Stop(); armed = null; Build(); });
            MemoryBook.Changed += Moved;
            Closed += (_, _) => { closed = true; stripClock.Stop(); armClock.Stop(); MemoryBook.Changed -= Moved; };
            Opened += (_, _) => Program.Guard(Load);
            Build();
        }

        internal bool Ready { get { return loaded && !busy; } }
        internal string OpenDayShown { get { return openDay; } }
        internal string Strip { get { return stripSay; } }
        internal bool Editing { get { return editing != null; } }

        internal NeonButton Button(string text) { foreach (Control control in Page.Children) if (control is NeonButton key && key.IsVisible && (key.Text == text || AutomationProperties.GetName(key) == "Read " + text)) return key; return null; }
        internal int Count(string text) { int found = 0; foreach (Control control in Page.Children) if (control is NeonButton key && key.IsVisible && key.Text == text) found++; return found; }
        internal void Press(string text) { NeonButton key = Button(text); if (key == null) throw new Exception("No memory key: " + text); key.Press(); }
        internal void EditRow(string was, string now)
        {
            foreach (Control control in Page.Children)
                if (control is NeonButton key && AutomationProperties.GetName(key) == "Edit: " + was) { key.Press(); editText = now; SaveEdit(); return; }
            throw new Exception("No memory row: " + was);
        }
        internal string Crowded()
        {
            Rect page = new Rect(0, 0, Wide, Page.Height);
            List<Control> shown = new List<Control>();
            foreach (Control control in Page.Children) if (control.IsVisible) shown.Add(control);
            for (int i = 0; i < shown.Count; i++)
            {
                Rect box = Box(shown[i]);
                if (!page.Contains(box)) return "Outside: " + Name(shown[i]);
                if (shown[i] is NeonCaption caption)
                {
                    FormattedText words = MainframeTheme.Words(caption.Text, caption.Size, caption.Bold);
                    words.MaxTextWidth = caption.Width;
                    if (words.Height > caption.Height + 0.5) return "Cut: " + caption.Text;
                }
                for (int j = i + 1; j < shown.Count; j++)
                {
                    Rect both = box.Intersect(Box(shown[j]));
                    if (both.Width > 0.5 && both.Height > 0.5) return Name(shown[i]) + " / " + Name(shown[j]);
                }
            }
            return null;
        }

        private static Rect Box(Control control)
        {
            double x = Canvas.GetLeft(control), y = Canvas.GetTop(control);
            if (control is NeonHead head) return new Rect(x + 14, y + 5, MainframeTheme.Words(head.Text, 40.0 / 3, true).Width, 19);
            return new Rect(x, y, control.Width, control.Height);
        }

        private static string Name(Control control) { return AutomationProperties.GetName(control) ?? (control as NeonCaption)?.Text ?? control.GetType().Name; }

        private void Moved() { Dispatcher.UIThread.Post(() => { if (!closed) Load(); }); }

        internal void Load()
        {
            int mine = ++ticket;
            busy = true;
            Task.Run(() =>
            {
                List<MemoryBook.Item> found = null;
                List<MemoryBook.Day> listed = null;
                try { found = MemoryBook.Everything(); listed = MemoryBook.DayList(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(() =>
                {
                    if (closed || mine != ticket) return;
                    busy = false;
                    if (found != null) { items = found; days = listed ?? new List<MemoryBook.Day>(); loaded = true; }
                    if (editing != null && !items.Exists(item => item.Id == editing)) editing = null;
                    Program.Guard(Build);
                });
            });
        }

        private void Run(Func<MemoryBook.Result> job, Action<MemoryBook.Result> then = null)
        {
            busy = true;
            armed = null;
            Task.Run(() =>
            {
                MemoryBook.Result result = null;
                try { result = job(); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(() =>
                {
                    if (closed) return;
                    busy = false;
                    if (result != null)
                    {
                        stripSay = result.Say; stripUndo = result.Ok ? result.Undo : null;
                        stripClock.Stop();
                        if (!string.IsNullOrEmpty(stripSay)) stripClock.Start();
                        if (then != null) then(result);
                    }
                    Load();
                });
            });
        }

        private void AddNow()
        {
            string words = add.Box.Text ?? "";
            if (words.Trim().Length == 0) return;
            Run(() => MemoryBook.Add(words), result => { if (result.Ok) add.Box.Text = ""; });
        }

        internal void SaveEdit()
        {
            string id = editing, words = editText;
            if (id == null) return;
            Run(() => MemoryBook.Edit(id, words), result => { if (result.Ok || result.Code == "same" || result.Code == "changed") editing = null; });
        }

        internal void ForgetOne(string id) { Run(() => MemoryBook.Forget(id)); }

        internal void UndoLast()
        {
            string token = stripUndo;
            if (token == null) return;
            Run(() => MemoryBook.Undo(token));
        }

        internal void OpenLog(string date)
        {
            busy = true;
            armed = null;
            Task.Run(() =>
            {
                bool over = false;
                List<MemoryBook.Said> said = null;
                try { said = MemoryBook.ReadDay(date, out over); }
                catch (Exception) { }
                Dispatcher.UIThread.Post(() =>
                {
                    if (closed) return;
                    busy = false;
                    if (said == null) { stripSay = "That day isn't there any more."; stripUndo = null; stripClock.Stop(); stripClock.Start(); Load(); return; }
                    editing = null; openDay = date; lines = said; cut = over;
                    Build();
                    Top();
                });
            });
        }

        private void Disarm() { armed = null; armClock.Stop(); }

        private void Arm(string what)
        {
            armed = what;
            armClock.Stop(); armClock.Start();
            Build();
        }

        protected override void Back()
        {
            if (editing != null) { editing = null; Build(); return; }
            if (openDay != null) { openDay = null; lines = null; Disarm(); Build(); return; }
            Close();
        }

        private static string Plain(string date)
        {
            DateTime day;
            return DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day) ? day.ToString("dddd d MMMM yyyy", CultureInfo.CurrentCulture) : date;
        }

        private static double Tall(string text, double width, double size = MainframeTheme.Size, bool bold = false)
        {
            FormattedText words = MainframeTheme.Words(text, size, bold);
            words.MaxTextWidth = width;
            return Math.Ceiling(words.Height) + 2;
        }

        private NeonCaption Words(string text, double x, double y, double width, double height, IBrush ink = null, double size = MainframeTheme.Size, bool bold = false, bool top = false)
        {
            return Put(Page, new NeonCaption { Text = text, Ink = ink ?? MainframeTheme.White, Size = size, Bold = bold, Top = top }, x, y, width, height);
        }

        private NeonButton Key(string text, double x, double y, double width, double height, Action action, string name = null, IBrush accent = null)
        {
            NeonButton key = Put(Page, new NeonButton { Text = text }, x, y, width, height, name ?? text);
            if (accent != null) key.Accent = accent;
            key.Click += (_, _) => action();
            return key;
        }

        private NeonHead Head(string title, double y) { return Put(Page, new NeonHead { Text = title }, 14, y - 5, Wide - 28, 24); }

        private bool Match(string text, string group, string find)
        {
            return find.Length == 0 || text.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0 || (group != null && group.IndexOf(find, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private void Build()
        {
            while (Page.Children.Count > fixedCount) Page.Children.RemoveAt(Page.Children.Count - 1);
            foreach (string name in new List<string>(Rows.Keys)) if (name != "Edit as text" && name != "Tell her something to remember" && name != "Add" && name != "Search her memory") Rows.Remove(name);
            double y = ListTop;
            if (stripSay != null)
            {
                double room = stripUndo != null ? 330 : Room, text = Tall(stripSay, room), high = Math.Max(26, text);
                Words(stripSay, Left, y + (high - text) / 2, room, text, MainframeTheme.Cyan, top: true);
                if (stripUndo != null) { NeonButton undo = Key("Undo", 416, y, 76, 26, UndoLast, accent: MainframeTheme.Cyan); undo.Loud = true; Rows["Undo"] = undo; }
                y += high + 12;
            }
            if (!Brain.Keeping)
            {
                string off = "Remember our chats is off, so she isn't keeping new chat memories.";
                double high = Tall(off, Room, 34.0 / 3);
                Words(off, Left, y, Room, high, MainframeTheme.Rose, 34.0 / 3, top: true);
                y += high + 12;
            }
            if (openDay != null) y = DayView(y);
            else y = Lists(y);
            Fit(Wide, y + 28, Pet.FrameSize(pet.Saved.MainframeSize) == 0 ? 1 : pet.Saved.MainframeSize / 100.0);
        }

        private double Entry(MemoryBook.Item item, double y)
        {
            bool edit = editing == item.Id;
            if (edit)
            {
                NeonEntry box = Put(Page, new NeonEntry(""), Left, y, KeyA - Left - 8, 56, "Edit memory");
                box.Box.TextWrapping = TextWrapping.Wrap;
                box.Box.Text = editText;
                box.Box.TextChanged += (_, _) => editText = box.Box.Text ?? "";
                box.Box.KeyDown += (_, e) => { if (e.Key == Avalonia.Input.Key.Enter) { e.Handled = true; Program.Guard(SaveEdit); } };
                Rows["Edit memory"] = box;
                Key("Save", KeyA, y + 1, 62, 22, SaveEdit, accent: MainframeTheme.Cyan).Loud = true;
                Key("Cancel", KeyB, y + 1, 78, 22, () => { editing = null; Build(); });
                Dispatcher.UIThread.Post(() => { if (!closed && box.IsVisible) { box.Box.Focus(); box.Box.CaretIndex = (box.Box.Text ?? "").Length; } });
                return y + 56 + 10;
            }
            double high = Tall(item.Text, KeyA - Left - 8);
            Words(item.Text, Left, y, KeyA - Left - 8, high, MainframeTheme.White, top: true);
            string id = item.Id, words = item.Text;
            Key("Edit", KeyA, y, 62, 22, () => { editing = id; editText = words; Disarm(); Build(); }, "Edit: " + words);
            Key("Forget", KeyB, y, 78, 22, () => ForgetOne(id), "Forget: " + words, MainframeTheme.Hot);
            return y + Math.Max(24, high) + 8;
        }

        private double Group(string kind, string title, double y, string find, bool newestFirst, Func<string, string> label)
        {
            List<string> order = new List<string>();
            Dictionary<string, List<MemoryBook.Item>> by = new Dictionary<string, List<MemoryBook.Item>>();
            foreach (MemoryBook.Item item in items)
            {
                if (item.Kind != kind || !Match(item.Text, item.Group, find)) continue;
                string group = item.Group ?? "";
                if (!by.ContainsKey(group)) { by[group] = new List<MemoryBook.Item>(); order.Add(group); }
                by[group].Add(item);
            }
            if (newestFirst) order.Reverse();
            foreach (string group in order)
            {
                if (group.Length > 0) { Words(label(group), Left, y, Room, 18, MainframeTheme.Cyan, 34.0 / 3, true); y += 22; }
                foreach (MemoryBook.Item item in by[group]) y = Entry(item, y);
                y += 4;
            }
            return order.Count == 0 ? -y : y;
        }

        private double Lists(double y)
        {
            string find = (search.Box.Text ?? "").Trim();
            y += 5;
            Head("ABOUT YOU", y);
            y += 30;
            double next = Group(MemoryBook.About, "ABOUT YOU", y, find, false, g => g);
            if (next < 0) { Words(find.Length > 0 ? "Nothing matches." : loaded ? "Nothing yet." : "Reading her notes.", Left, y, Room, 20, MainframeTheme.Faint); y += 30; } else y = next;
            y += 4;
            Head("FROM YOUR CHATS", y);
            y += 30;
            next = Group(MemoryBook.Chats, "FROM YOUR CHATS", y, find, true, g => g);
            if (next < 0) { Words(find.Length > 0 ? "Nothing matches." : loaded ? "Nothing yet." : "Reading her notes.", Left, y, Room, 20, MainframeTheme.Faint); y += 30; } else y = next;
            y += 4;
            Head("GAMES", y);
            string gamesNeed = "Click again to forget game notes";
            bool anyGames = items.Exists(item => item.Kind == MemoryBook.Games);
            if (anyGames)
            {
                NeonButton wipe = Key(armed == "games" ? gamesNeed : "Forget game notes", 292, y - 3, 200, 20, () => { if (armed == "games") { Disarm(); Run(() => MemoryBook.Wipe("games")); } else Arm("games"); }, "Forget game notes", MainframeTheme.Hot);
                Rows["Forget game notes"] = wipe;
            }
            y += 30;
            next = Group(MemoryBook.Games, "GAMES", y, find, false, g => g);
            if (next < 0) { Words(find.Length > 0 ? "Nothing matches." : loaded ? "Nothing yet." : "Reading her notes.", Left, y, Room, 20, MainframeTheme.Faint); y += 30; } else y = next;
            y += 4;
            Head("CHAT LOGS", y);
            y += 30;
            List<MemoryBook.Day> shown = new List<MemoryBook.Day>(days);
            shown.Sort((one, two) => string.CompareOrdinal(two.Date, one.Date));
            int count = 0;
            foreach (MemoryBook.Day day in shown)
            {
                if (find.Length > 0 && day.Date.IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0 && Plain(day.Date).IndexOf(find, StringComparison.OrdinalIgnoreCase) < 0) continue;
                string date = day.Date;
                NeonButton open = Key(Plain(date), Left, y, 230, 22, () => OpenLog(date), "Read " + date);
                Words(day.Lines + (day.Lines == 1 ? " line" : " lines"), 270, y, 120, 22, MainframeTheme.Faint, 34.0 / 3);
                y += 30; count++;
            }
            if (count == 0) { Words(find.Length > 0 ? "Nothing matches." : loaded ? "No chats kept yet." : "Reading her notes.", Left, y, Room, 20, MainframeTheme.Faint); y += 30; }
            y += 12;
            NeonButton all = Key(armed == "all" ? "Click again to forget everything about me" : "Forget everything about me", Left, y, Room, 28, () => { if (armed == "all") { Disarm(); Run(() => MemoryBook.Wipe("all")); } else Arm("all"); }, "Forget everything about me", MainframeTheme.Hot);
            Rows["Forget everything about me"] = all;
            return y + 28;
        }

        private double DayView(double y)
        {
            Key("Back", Left, y, 60, 24, () => { openDay = null; lines = null; Disarm(); Build(); }, "Back");
            Words(Plain(openDay), 98, y, 200, 24, MainframeTheme.White, MainframeTheme.Size, true);
            string date = openDay;
            NeonButton forget = Key(armed == "day" ? "Click again to forget" : "Forget this day", 316, y, 176, 24, () =>
            {
                if (armed != "day") { Arm("day"); return; }
                Disarm();
                Run(() => MemoryBook.ForgetDay(date), result => { if (result.Ok) { openDay = null; lines = null; } });
            }, "Forget this day", MainframeTheme.Hot);
            Rows["Forget this day"] = forget;
            y += 40;
            if (cut) { Words("Only the start of this day is shown.", Left, y, Room, 18, MainframeTheme.Faint, 34.0 / 3); y += 24; }
            Head("CHAT LOG", y + 5);
            y += 35;
            if (lines == null || lines.Count == 0) { Words("Nothing in this day.", Left, y, Room, 20, MainframeTheme.Faint); return y + 30; }
            foreach (MemoryBook.Said said in lines)
            {
                bool note = string.IsNullOrEmpty(said.Who);
                double high = Tall(said.Text, note ? 402 : 316);
                Words(said.At, Left, y, 56, 20, MainframeTheme.Faint, 34.0 / 3);
                if (!note) Words(said.Who, 90, y, 90, 20, said.Who == "You" ? MainframeTheme.Cyan : MainframeTheme.Rose, 34.0 / 3, true);
                Words(said.Text, note ? 90 : 176, y, note ? 402 : 316, high, MainframeTheme.White, top: true);
                y += Math.Max(22, high) + 6;
            }
            return y;
        }
    }
}
