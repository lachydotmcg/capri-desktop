using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Serialization.Json;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    // A picture of one of her windows as Windows draws it, cut to the window's own outline over a light backdrop, so a frame
    // whose corners don't match the window's shape shows up. DrawToBitmap misses the window's shape and some controls.
    internal static class WindowPicture
    {
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr window, IntPtr into, uint how);
        [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window, IntPtr region);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr tall);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr thing);

        internal static readonly Color Backdrop = Color.FromArgb(232, 234, 238);

        // Saves it and says how it was drawn: PrintWindow with PW_RENDERFULLCONTENT, or WM_PRINT when that came back blank.
        internal static string Save(Form window, string path, int margin = 24)
        {
            window.Refresh();
            Application.DoEvents();
            int wide = window.Width, tall = window.Height;
            string how = "PrintWindow";
            using (Bitmap shot = new Bitmap(wide, tall, PixelFormat.Format32bppArgb))
            {
                bool drawn = Print(window, shot, true);
                if (!drawn || Blank(shot)) { how = "WM_PRINT"; Print(window, shot, false); }
                Region outline;
                IntPtr handle = CreateRectRgn(0, 0, 0, 0);
                try { outline = GetWindowRgn(window.Handle, handle) > 1 ? Region.FromHrgn(handle) : new Region(new Rectangle(0, 0, wide, tall)); }
                finally { DeleteObject(handle); }
                using (outline)
                using (Bitmap page = new Bitmap(wide + margin * 2, tall + margin * 2, PixelFormat.Format24bppRgb))
                using (Graphics drawing = Graphics.FromImage(page))
                {
                    drawing.Clear(Backdrop);
                    outline.Translate(margin, margin);
                    drawing.SetClip(outline, CombineMode.Replace);
                    drawing.DrawImageUnscaled(shot, margin, margin);
                    page.Save(path, ImageFormat.Png);
                }
            }
            return how;
        }

        // The window alone, as Windows draws it, with everything outside its outline see-through, to lay over a mock desktop.
        internal static Bitmap Cut(Control window)
        {
            window.Refresh();
            Application.DoEvents();
            Bitmap shot = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
            if (!Print(window, shot, true) || Blank(shot)) Print(window, shot, false);
            Bitmap cut = new Bitmap(window.Width, window.Height, PixelFormat.Format32bppArgb);
            IntPtr handle = CreateRectRgn(0, 0, 0, 0);
            Region outline;
            try { outline = GetWindowRgn(window.Handle, handle) > 1 ? Region.FromHrgn(handle) : new Region(new Rectangle(0, 0, window.Width, window.Height)); }
            finally { DeleteObject(handle); }
            using (outline)
            using (Graphics drawing = Graphics.FromImage(cut))
            {
                drawing.Clear(Color.Transparent);
                drawing.SetClip(outline, CombineMode.Replace);
                drawing.DrawImageUnscaled(shot, 0, 0);
            }
            shot.Dispose();
            return cut;
        }

        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern int GetWindowThreadProcessId(IntPtr window, out int process);

        // Which window has the foreground and which of hers has the keyboard, for a key check that didn't land: one of her
        // windows by type, anything else by its program and window class only.
        internal static string Focus()
        {
            IntPtr front = GetForegroundWindow(), focus = GetFocus();
            string frontIs;
            if (front == IntPtr.Zero) frontIs = "none";
            else if (Control.FromHandle(front) != null) frontIs = "hers, " + TimerWatch.Who(front);
            else
            {
                int process;
                GetWindowThreadProcessId(front, out process);
                string program;
                try { using (System.Diagnostics.Process owner = System.Diagnostics.Process.GetProcessById(process)) program = owner.ProcessName; }
                catch (ArgumentException) { program = "a program that's gone"; }
                catch (InvalidOperationException) { program = "a program that's gone"; }
                frontIs = (process == System.Diagnostics.Process.GetCurrentProcess().Id ? "hers, " : program + ", ") + TimerWatch.Who(front);
            }
            Control focused = focus == IntPtr.Zero ? null : Control.FromHandle(focus);
            string focusIs = focus == IntPtr.Zero ? "nothing of hers" : focused == null ? TimerWatch.Who(focus) : TimerWatch.Who(focus) + " \"" + (focused.AccessibleName ?? focused.Text) + "\"";
            return "foreground: " + frontIs + "; keyboard focus: " + focusIs;
        }

        // Several windows where they sit on the screen, like the card with her language menu open off it, on the backdrop.
        internal static void Together(string path, params Control[] windows)
        {
            Rectangle all = Rectangle.Empty;
            foreach (Control window in windows) all = all.IsEmpty ? window.Bounds : Rectangle.Union(all, window.Bounds);
            const int margin = 24;
            using (Bitmap page = new Bitmap(all.Width + margin * 2, all.Height + margin * 2, PixelFormat.Format24bppRgb))
            using (Graphics drawing = Graphics.FromImage(page))
            {
                drawing.Clear(Backdrop);
                foreach (Control window in windows)
                    using (Bitmap cut = Cut(window)) drawing.DrawImageUnscaled(cut, window.Left - all.Left + margin, window.Top - all.Top + margin);
                page.Save(path, ImageFormat.Png);
            }
        }

        // Where a picture has anything drawn, by its alpha.
        internal static Rectangle Inked(Bitmap image)
        {
            int left = image.Width, top = image.Height, right = -1, bottom = -1;
            BitmapData bits = image.LockBits(new Rectangle(0, 0, image.Width, image.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                int[] row = new int[image.Width];
                for (int y = 0; y < image.Height; y++)
                {
                    Marshal.Copy(new IntPtr(bits.Scan0.ToInt64() + (long)y * bits.Stride), row, 0, image.Width);
                    for (int x = 0; x < image.Width; x++)
                        if ((row[x] >> 24 & 0xFF) > 24) { if (x < left) left = x; if (x > right) right = x; if (y < top) top = y; if (y > bottom) bottom = y; }
                }
            }
            finally { image.UnlockBits(bits); }
            return right < 0 ? Rectangle.Empty : Rectangle.FromLTRB(left, top, right + 1, bottom + 1);
        }

        private static bool Print(Control window, Bitmap into, bool full)
        {
            using (Graphics drawing = Graphics.FromImage(into))
            {
                drawing.Clear(Color.Black);
                IntPtr dc = drawing.GetHdc();
                try
                {
                    if (full) return PrintWindow(window.Handle, dc, 2);
                    SendMessage(window.Handle, 0x0317, dc, new IntPtr(0x1E));
                    return true;
                }
                finally { drawing.ReleaseHdc(dc); }
            }
        }

        // Every sampled pixel the same colour: Windows had nothing to give.
        private static bool Blank(Bitmap shot)
        {
            Color first = shot.GetPixel(shot.Width / 2, shot.Height / 2);
            for (int y = 2; y < shot.Height; y += Math.Max(1, shot.Height / 23))
                for (int x = 2; x < shot.Width; x += Math.Max(1, shot.Width / 23))
                    if (shot.GetPixel(x, y).ToArgb() != first.ToArgb()) return false;
            return true;
        }
    }

    internal partial class Companion
    {
        // Her first launch card as they'd use it, on stand-ins: no model is downloaded, no key is real, the clipboard and the
        // browser are never touched. Each path (this PC, free key, plan or key), Skip on every screen, Skip all from every
        // screen, quitting halfway and coming back, an update never seeing it, her lines held back while it's open, and pictures
        // of every screen at 100%, 125% and 150%.
        private void SmokeFirstRun(List<string> checks, string output)
        {
            Directory.CreateDirectory(output);
            Preferences wasPreferences = preferences;
            Func<string, string> wasThink = Think;
            Action<string> wasBrowse = Browse;
            Func<bool> wasSaidReady = Said.Ready;
            System.Globalization.CultureInfo wasCulture = Thread.CurrentThread.CurrentUICulture;
            string wasLanguage = Brain.Language;
            string wasGoogle = Brain.Google, wasOpenAi = Brain.OpenAi, wasHeard = heard, wasNote = mainframe.VoiceNoteText;
            Machine wasMachine = machine;
            Pick wasFit = fit;
            Cloud wasRemote = brain.Remote;
            Engine wasLocal = brain.Local;
            string wasServer, wasServerKey;
            brain.GetServer(out wasServer, out wasServerKey);
            string wasModel = brain.Model;
            List<string> wasModels = cloudModels;
            Func<string> wasPaste = FirstRunPaste;
            bool wasGreeted = greetTried, wasNews = newsPending, wasSettingUp = SettingUp, mainframeShown = mainframe.Visible;
            double wasMeet = meetAt;
            FormWindowState mainframeState = mainframe.WindowState;
            List<System.Net.Sockets.TcpListener> servers = new List<System.Net.Sockets.TcpListener>();
            List<string> browsed = new List<string>(), pictures = new List<string>(), ways = new List<string>();
            const string online = "Ooh, that's better. I can think now. Say something, choom.";
            int setups = 0;
            string planned = null;
            Action pump = delegate { for (int i = 0; i < 12; i++) { Application.DoEvents(); Thread.Sleep(15); } };
            // Each pass is written down as it happens, so a section that times out or throws still says how far it got.
            Action<string> passed = delegate(string line) { checks.Add(line); SmokeRun.Flush(checks); };
            // A brain pick made by a check, only ever on WAKE ME UP; anywhere else it says where the card went and what sent it.
            Action<Mainframe.FirstRunCard, int> pickBrain = delegate(Mainframe.FirstRunCard shown, int choice)
            {
                if (shown.IsDisposed || shown.Finished || shown.Screen != FirstRun.Pick || shown != firstRun)
                    throw new Exception("A brain pick found the card " + (shown.IsDisposed ? "disposed" : shown.Finished ? "finished" : shown != firstRun ? "replaced" : "on screen " + shown.Screen) + ": " + shown.Trail + (firstRun != null && firstRun != shown ? " / the card now open: " + firstRun.Trail : ""));
                shown.PickBrain(choice);
            };
            Action<Func<bool>, string> wait = delegate(Func<bool> done, string failed)
            {
                Stopwatch waited = Stopwatch.StartNew();
                while (!done()) { if (waited.ElapsedMilliseconds > 10000) throw new Exception(failed); Application.DoEvents(); Thread.Sleep(10); }
            };
            Action reset = delegate
            {
                ++asking;
                CloseTalk();
                SettingUp = false;
                brain.Remote = null;
                brain.Local = null;
                brain.Model = null;
                cloudModels = null;
                preferences = new Preferences { Tiered = true, Silent = true, Welcomed = true, Met = true };
                Brain.Language = null;
                PointEars();
                Brain.Off = false;
                greetTried = false;
            };
            Action putBack = delegate
            {
                mainframe.CloseExtras();
                if (!mainframeShown) mainframe.Hide();
                else if (mainframe.WindowState != mainframeState) mainframe.WindowState = mainframeState;
                pump();
            };
            Func<int, int, Mainframe.FirstRunCard> open = delegate(int screen, int pick)
            {
                Mainframe.FirstRunCard made = OpenFirstRun(screen, pick, false);
                pump();
                return made;
            };
            Action<Mainframe.FirstRunCard, string> picture = delegate(Mainframe.FirstRunCard pictured, string name)
            {
                pump();
                string crowded = pictured.Crowded();
                if (crowded != null) throw new Exception("The card's " + name + " screen is crowded at " + pictured.Unit + ": " + crowded);
                if (pictured.ClientSize.Height > (int)Math.Ceiling(Mainframe.FirstRunCard.Tallest * pictured.Unit) + 1) throw new Exception("The card's " + name + " screen is taller than the screen room it's sized for: " + pictured.ClientSize.Height + " at " + pictured.Unit);
                string path = Path.Combine(output, name + ".png");
                string how = WindowPicture.Save(pictured, path);
                if (!ways.Contains(how)) ways.Add(how);
                pictures.Add(name);
            };
            Action localDone = delegate
            {
                mainframe.ShowProgress(2, "Loading " + fit.Name, 0, 0);
                SettingUp = false;
                brain.Model = fit.Name;
                mainframe.ShowLocal(fit);
                Greet();
            };
            Func<string[], string> serve = delegate(string[] replies)
            {
                System.Net.Sockets.TcpListener server;
                string address = FakeServer(replies, new List<string>(), out server);
                servers.Add(server);
                return address;
            };
            // Each part is its own section inside this one: it gets its own time, keeps its passes when it fails, and the parts
            // after it still run.
            Action<string, Action> part = delegate(string name, Action run)
            {
                Section(checks, name, delegate
                {
                    try { run(); }
                    catch (NullReferenceException slip) { throw new Exception(slip.Message + " " + slip.StackTrace.Replace(Environment.NewLine, " | ") + (firstRun != null ? " / the card's trail: " + firstRun.Trail : "")); }
                    finally
                    {
                        // A part that stopped halfway leaves no card, No AI or Spanish Windows behind for the next.
                        Thread.CurrentThread.CurrentUICulture = wasCulture;
                        if (firstRun != null && !firstRun.IsDisposed) firstRun.Dispose();
                        reset();
                        putBack();
                    }
                });
            };
            Mainframe.FirstRunCard card = null;
            try
            {
                Browse = delegate(string page) { lock (browsed) browsed.Add(page); };
                Think = delegate { return online; };
                Said.Ready = delegate { return false; };
                FirstRunPaste = delegate { return "  AIza-firstrun-test  "; };
                FirstRunUsePlan = delegate(string kind) { planned = kind; };
                fit = Engine.Picks[1];
                FirstRunSetUp = delegate { setups++; SettingUp = true; mainframe.ShowProgress(1, "Downloading " + fit.Name, 0, fit.Size); };
                reset();

                // An update never sees the card; a brand new install and one that quit halfway do.
                Preferences oldSaved;
                using (MemoryStream saved = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Welcomed\":true}")))
                    oldSaved = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(saved);
                if (WantsFirstRun(oldSaved, false, false) || WantsFirstRun(new Preferences(), false, false) || !WantsFirstRun(new Preferences(), true, false) || WantsFirstRun(new Preferences(), true, true)
                    || !WantsFirstRun(new Preferences { Welcomed = true, FirstRunStep = FirstRun.Voice }, false, false) || WantsFirstRun(new Preferences { Welcomed = true }, true, false))
                    throw new Exception("The wrong starts get her first launch card.");
                passed("PASS an update (settings saved before this version, or finished with the card) never gets her first launch card; a brand new install does, one that quit halfway picks up, and settings reset after crashes keep the old first launch");
                Preferences brandNew;
                using (MemoryStream empty = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{}")))
                    brandNew = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(empty);
                if (new Preferences().Silent || brandNew.Silent || !FreeChatOn) throw new Exception("A brand new install starts with her voice off, so HOW I SOUND would have TEXT ONLY picked and she'd stay silent.");
                passed("PASS a brand new install has her voice on, so HOW I SOUND has MY FREE VOICE picked and her free voice downloads from her first second");

                part("SmokeFirstRunLanguage", delegate
                {
                    // Her language row on the first screen, on stand-ins: the Windows guess shows and is saved before any click, the
                    // Mainframe's own menu opens from it by mouse, Tab and Space or Enter, and closes on Esc. It opens on the card's
                    // screen and inside its work area, a 1366 x 768 one too. A pick is saved at once and her brain and ears follow it,
                    // English clears her brain's language, a Mainframe pick shows on the card, and Skip all and NO AI keep the pick.
                    Action<Control, Keys> languageKey = delegate(Control target, Keys code)
                    {
                        Message down = Message.Create(target.Handle, 0x0100, new IntPtr((int)code), IntPtr.Zero);
                        if (!target.PreProcessMessage(ref down)) Native.SendMessage(target.Handle, down.Msg, down.WParam, down.LParam);
                        Message up = Message.Create(target.Handle, 0x0101, new IntPtr((int)code), IntPtr.Zero);
                        if (!target.PreProcessMessage(ref up)) Native.SendMessage(target.Handle, up.Msg, up.WParam, up.LParam);
                    };
                    Action<Mainframe.FirstRunCard, string> pickLanguage = delegate(Mainframe.FirstRunCard shown, string pickedLanguage)
                    {
                        ContextMenuStrip choices = shown.ShowLanguages();
                        try
                        {
                            foreach (ToolStripMenuItem item in Mainframe.LanguageItems(choices.Items))
                                if ((string)item.Tag == pickedLanguage) { item.PerformClick(); return; }
                            throw new Exception("The card has no language item for " + pickedLanguage + ".");
                        }
                        finally { choices.Close(); }
                    };
                    // Whether a menu item's Click still calls into the card.
                    Func<ToolStripItem, object, bool> clickHeldBy = delegate(ToolStripItem item, object target)
                    {
                        System.Reflection.FieldInfo key = typeof(ToolStripItem).GetField("EventClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                        System.Reflection.PropertyInfo list = typeof(System.ComponentModel.Component).GetProperty("Events", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                        if (key == null || list == null) throw new Exception("Can't read a menu item's Click handlers on this .NET.");
                        Delegate click = ((System.ComponentModel.EventHandlerList)list.GetValue(item, null))[key.GetValue(null)];
                        if (click == null) return false;
                        foreach (Delegate one in click.GetInvocationList()) if (one.Target == target) return true;
                        return false;
                    };
                    Thread.CurrentThread.CurrentUICulture = new System.Globalization.CultureInfo("es-ES");
                    preferences = Preferences.Fresh();
                    if (preferences.Language != "Spanish") throw new Exception("Fresh Spanish settings lost her guessed language before setup.");
                    preferences.Silent = true;
                    preferences.Met = true;
                    Brain.Language = Brain.LanguageCode(preferences.Language) == "en" ? null : preferences.Language;
                    PointEars();
                    Mainframe.FirstRunCard languageCard = open(FirstRun.Pick, FirstRun.Local);
                    if (languageCard.LanguageShown != Mainframe.LanguageName("Spanish") || preferences.Language != "Spanish" || LoadPreferences().Language != "Spanish") throw new Exception("The setup row didn't show and keep the Spanish guess before a click: " + languageCard.LanguageShown);
                    passed("PASS fresh setup under Spanish Windows shows " + Mainframe.LanguageName("Spanish") + " and has the guess saved before any click");
                    Button languageButton = null;
                    foreach (Control control in languageCard.Controls) if (control.AccessibleName == "Her language") languageButton = control as Button;
                    if (languageButton == null || !languageButton.TabStop) throw new Exception("The setup language pick has no keyboard button.");
                    ContextMenuStrip languageMenu = languageCard.ShowLanguages();
                    bool[] languagesSeen = new bool[Brain.Languages.GetLength(0)];
                    try { SmokeLanguageBounds(languageMenu, Screen.FromControl(languageCard).WorkingArea, languagesSeen); }
                    finally { languageMenu.Close(); }
                    for (int row = 0; row < languagesSeen.Length; row++) if (!languagesSeen[row]) throw new Exception("The setup menu is missing " + Brain.Languages[row, 1] + ".");
                    // From the middle of every screen this PC has, the menu opens on that screen and inside its work area.
                    Point languageCardAt = languageCard.Location;
                    int screensTried = 0;
                    foreach (Screen screen in Screen.AllScreens)
                    {
                        Rectangle work = screen.WorkingArea;
                        if (work.Width < languageCard.Width || work.Height < languageCard.Height) continue;
                        languageCard.Location = new Point(work.Left + (work.Width - languageCard.Width) / 2, work.Top + (work.Height - languageCard.Height) / 2);
                        languageMenu = languageCard.ShowLanguages();
                        try
                        {
                            if (!work.Contains(languageMenu.Bounds)) throw new Exception("The setup language menu left the card's screen: " + languageMenu.Bounds + " outside " + work);
                            SmokeLanguageBounds(languageMenu, work, new bool[Brain.Languages.GetLength(0)]);
                        }
                        finally { languageMenu.Close(); }
                        screensTried++;
                    }
                    // A 1366 x 768 screen, with the card in its bottom right corner where the menu has least room.
                    Rectangle home = Screen.FromControl(languageCard).WorkingArea;
                    Rectangle small = Rectangle.Intersect(home, new Rectangle(home.Location, new Size(1366, 728)));
                    languageCard.Location = new Point(small.Right - languageCard.Width, small.Bottom - languageCard.Height);
                    languageMenu = mainframe.ShowLanguages(languageButton, small);
                    try
                    {
                        if (!small.Contains(languageMenu.Bounds)) throw new Exception("The setup language menu left a 1366 x 768 screen: " + languageMenu.Bounds + " outside " + small);
                        foreach (ToolStripMenuItem item in Mainframe.LanguageItems(languageMenu.Items))
                            if (item.HasDropDownItems)
                            {
                                Size room = item.DropDown.GetPreferredSize(Size.Empty);
                                if (room.Width > small.Width - 4 || room.Height > small.Height - 4) throw new Exception("A setup language group is too big for a 1366 x 768 screen: " + item.Text + " " + room);
                            }
                    }
                    finally { languageMenu.Close(); }
                    languageCard.Location = languageCardAt;
                    // On a screen so small the menu's text shrinks to fit, opened again and again: no group menu is left behind
                    // among her dropdowns, and every one left can still measure its text (a look change adds to each of them).
                    for (int again = 0; again < 3; again++)
                    {
                        languageMenu = mainframe.ShowLanguages(languageButton, new Rectangle(home.Location, new Size(640, 300)));
                        languageMenu.Close();
                    }
                    languageMenu = languageCard.ShowLanguages();
                    languageMenu.Close();
                    foreach (ContextMenuStrip kept in mainframe.Dropdowns)
                    {
                        if (kept.IsDisposed) throw new Exception("A disposed menu is still among the Mainframe's dropdowns.");
                        try { TextRenderer.MeasureText("Smoke", kept.Font); }
                        catch (ArgumentException) { throw new Exception("A Mainframe dropdown kept a disposed font after the language menu shrank to fit: " + kept.AccessibleName); }
                    }
                    passed("PASS the setup language menu has every Mainframe language and opens on the card's screen inside its work area, from the middle of " + screensTried + " screen(s) and from the corner of a 1366 x 768 one");
                    languageButton.PerformClick();
                    if (!languageMenu.Visible || languageMenu.SourceControl != languageButton) throw new Exception("The setup language button didn't open its menu by mouse.");
                    languageKey(languageMenu, Keys.Escape);
                    pump();
                    if (languageMenu.Visible) throw new Exception("Esc didn't close the setup language menu. " + WindowPicture.Focus());
                    bool tabbedToLanguage = false;
                    for (int step = 0; step < languageCard.Controls.Count; step++)
                    {
                        languageCard.SelectNextControl(languageCard.ActiveControl, true, true, true, true);
                        if (languageCard.ActiveControl == languageButton) { tabbedToLanguage = true; break; }
                    }
                    if (!tabbedToLanguage) throw new Exception("Tab couldn't reach the setup language button.");
                    languageKey(languageButton, Keys.Enter);
                    if (!languageMenu.Visible || languageCard.Screen != FirstRun.Pick) throw new Exception("Enter didn't open the setup language menu (or went on past the first screen). " + WindowPicture.Focus());
                    languageMenu.Close();
                    languageKey(languageButton, Keys.Space);
                    if (!languageMenu.Visible || languageCard.Screen != FirstRun.Pick) throw new Exception("Space didn't open the setup language menu. " + WindowPicture.Focus());
                    ToolStripMenuItem japanese = null;
                    foreach (ToolStripMenuItem item in Mainframe.LanguageItems(languageMenu.Items)) if ((string)item.Tag == "Japanese") japanese = item;
                    if (japanese == null) throw new Exception("The setup menu has no Japanese pick.");
                    List<ToolStripMenuItem> languagePath = new List<ToolStripMenuItem>();
                    // The groups down to Japanese, found from the top: a group's own menu doesn't always know its group item.
                    for (ToolStripItemCollection level = languageMenu.Items; level != null; )
                    {
                        ToolStripItemCollection next = null;
                        foreach (ToolStripItem entry in level)
                        {
                            ToolStripMenuItem group = entry as ToolStripMenuItem;
                            if (group == null || !group.HasDropDownItems) continue;
                            foreach (ToolStripMenuItem inside in Mainframe.LanguageItems(group.DropDownItems))
                                if (inside == japanese) { languagePath.Add(group); next = group.DropDownItems; break; }
                            if (next != null) break;
                        }
                        level = next;
                    }
                    foreach (ToolStripMenuItem group in languagePath)
                    {
                        group.Select();
                        languageKey(group.Owner, Keys.Right);
                        if (!group.DropDown.Visible) throw new Exception("Right didn't open a setup language group.");
                    }
                    japanese.Select();
                    languageKey(japanese.Owner, Keys.Enter);
                    if (preferences.Language != "Japanese" || Brain.Language != "Japanese" || LoadPreferences().Language != "Japanese" || parakeet.Language != Listening("Japanese", "es") || languageCard.LanguageShown != Mainframe.LanguageName("Japanese") || !mainframe.LanguageIs("Japanese"))
                        throw new Exception("The setup Japanese pick didn't save or reach her brain, ears and row.");
                    languageMenu.Close();
                    passed("PASS the setup language menu opens by mouse, Tab then Enter, or Space, Esc closes it, and Right then Enter picks Japanese, saved at once, with her brain, listening language, Mainframe tick and row following");
                    // What a Mainframe Language pick does, made while the card is open.
                    SetLanguage("Korean");
                    mainframe.Sync();
                    pump();
                    if (languageCard.LanguageShown != Mainframe.LanguageName("Korean")) throw new Exception("A Mainframe language pick didn't show on the open setup card: " + languageCard.LanguageShown);
                    passed("PASS a language picked in the Mainframe while the card is open shows on its row");
                    pickLanguage(languageCard, "English");
                    if (preferences.Language != "English" || Brain.Language != null || LoadPreferences().Language != "English" || parakeet.Language != Listening("English", "es") || languageCard.LanguageShown != "English")
                        throw new Exception("Picking English on setup didn't clear her brain's language and point her ears at English.");
                    passed("PASS picking English after another language on setup saves English, clears Brain.Language and listens the way a Mainframe English pick does");
                    pickLanguage(languageCard, "Japanese");
                    languageMenu = languageCard.ShowLanguages();
                    bool heldOpen = false;
                    foreach (ToolStripMenuItem item in Mainframe.LanguageItems(languageMenu.Items)) if (clickHeldBy(item, languageCard)) heldOpen = true;
                    if (!heldOpen) throw new Exception("The open setup language menu doesn't call into the card, so the leak check below proves nothing.");
                    languageCard.SkipAll();
                    pump();
                    if (FirstRunShowing || preferences.Language != "Japanese" || Brain.Language != "Japanese" || LoadPreferences().Language != "Japanese" || parakeet.Language != "ja") throw new Exception("Skip all lost the language just picked on setup.");
                    if (languageMenu.Visible) throw new Exception("Skip all left her language menu up with the card gone.");
                    if (!languageCard.IsDisposed) throw new Exception("Skip all didn't dispose the card.");
                    foreach (ToolStripMenuItem item in Mainframe.LanguageItems(languageMenu.Items)) if (clickHeldBy(item, languageCard)) throw new Exception("The Mainframe's language menu still calls into a disposed setup card: " + item.Text);
                    putBack();
                    reset();
                    languageCard = open(FirstRun.Pick, FirstRun.Local);
                    pickLanguage(languageCard, "Japanese");
                    pickBrain(languageCard, FirstRun.NoAi);
                    if (languageCard.LanguageShown != Mainframe.LanguageName("Japanese")) throw new Exception("NO AI hid the setup language row.");
                    picture(languageCard, "lang-pick-no-ai-100");
                    languageCard.Next();
                    if (languageCard.Screen != FirstRun.Alone || !preferences.NoAi || preferences.Language != "Japanese" || Brain.Language != "Japanese" || LoadPreferences().Language != "Japanese" || parakeet.Language != "ja") throw new Exception("NO AI lost the language just picked on setup.");
                    languageCard.Next();
                    if (FirstRunShowing || LoadPreferences().Language != "Japanese") throw new Exception("Finishing NO AI lost the setup language.");
                    putBack();
                    passed("PASS Skip all right after a language pick keeps it, closes the menu and leaves no handler on the disposed card; NO AI keeps the row and the pick through JUST ME and Done");
                    Thread.CurrentThread.CurrentUICulture = wasCulture;
                    // Pictures: English, the widest names the row gets, with and without the voice strip, and the menu open off it.
                    bool languageVoiceWas = SettingUpVoice;
                    int[] languageTall = new int[2];
                    try
                    {
                        foreach (float scale in new[] { 1f, 1.25f, 1.5f })
                            foreach (string language in new[] { "English", "Traditional Chinese", "Indonesian" })
                                foreach (bool voiceComing in new[] { false, true })
                                {
                                    if (language == "Indonesian" && (voiceComing || scale != 1f)) continue;
                                    reset();
                                    SettingUpVoice = voiceComing;
                                    languageCard = OpenFirstRun(FirstRun.Pick, FirstRun.Local, false, scale);
                                    pump();
                                    pickLanguage(languageCard, language);
                                    string percent = ((int)Math.Round(scale * 100)).ToString();
                                    picture(languageCard, "lang-pick-" + language.ToLowerInvariant().Replace(' ', '-') + "-" + percent + (voiceComing ? "-voice" : ""));
                                    if (languageCard.LanguageShown != Mainframe.LanguageName(language) || languageCard.ClientSize.Width != (int)Math.Round(600 * languageCard.Unit)) throw new Exception("The setup language row lost its text or the card's width changed at " + scale + ": " + languageCard.ClientSize);
                                    if (scale == 1f && language == "English") languageTall[voiceComing ? 1 : 0] = languageCard.ClientSize.Height;
                                    if (language == "English" && !voiceComing && scale != 1.25f)
                                    {
                                        ContextMenuStrip shownMenu = languageCard.ShowLanguages();
                                        try
                                        {
                                            pump();
                                            WindowPicture.Together(Path.Combine(output, "lang-menu-open-" + percent + ".png"), languageCard, shownMenu);
                                            ToolStripMenuItem group = null;
                                            foreach (ToolStripItem entry in shownMenu.Items) { ToolStripMenuItem item = entry as ToolStripMenuItem; if (item != null && item.HasDropDownItems && group == null) group = item; }
                                            if (group != null)
                                            {
                                                group.Select();
                                                group.ShowDropDown();
                                                pump();
                                                WindowPicture.Together(Path.Combine(output, "lang-menu-group-" + percent + ".png"), languageCard, shownMenu, group.DropDown);
                                            }
                                        }
                                        finally { shownMenu.Close(); }
                                    }
                                    languageCard.SkipAll();
                                    putBack();
                                }
                    }
                    finally { SettingUpVoice = languageVoiceWas; }
                    reset();
                    passed("PASS the setup language row fits the first screen with no overlap or clipped text at 100%, 125% and 150%, in English, Traditional Chinese and Indonesian, with and without the free voice strip, inside the room the card is sized for; at 100% the first screen is " + languageTall[0] + " px tall, " + languageTall[1] + " with the strip");
                });

                part("SmokeFirstRunLanding", delegate
                {
                    // The card waits for her to land before it opens beside her, and she doesn't wander off while it's open.
                    reset();
                    bool landing = true;
                    FirstRunLanding = delegate { return landing; };
                    OpenWhenLanded(FirstRun.Pick, FirstRun.Local, false, 0);
                    Stopwatch held = Stopwatch.StartNew();
                    while (held.ElapsedMilliseconds < 700) { Application.DoEvents(); Thread.Sleep(15); }
                    bool waitedForHer = !FirstRunShowing;
                    landing = false;
                    wait(delegate { return FirstRunShowing; }, "The card never opened once she'd landed.");
                    if (!waitedForHer) throw new Exception("The card opened before she'd dropped onto the taskbar.");
                    Mainframe.FirstRunCard landed = firstRun;
                    Point herWas = anchor, cardWas = landed.Location;
                    anchor = new Point(anchor.X - 160, anchor.Y);
                    Location = anchor;
                    pump();
                    Point followed = landed.Location;
                    anchor = herWas;
                    Location = anchor;
                    pump();
                    if (followed == cardWas || landed.Location != cardWas) throw new Exception("The card didn't follow her when she moved: " + cardWas + " -> " + followed + " -> " + landed.Location);
                    // Measured once she's idle, so a glitch or a line of hers that happens to land here can't stand in for the card.
                    bool wanderWas = preferences.Wander;
                    double wanderAt = nextWander;
                    preferences.Wander = true;
                    ++asking;
                    CloseTalk();
                    Stopwatch idleFor = Stopwatch.StartNew();
                    while (WanderHeld(clock.Elapsed.TotalSeconds, 0) != "first launch card" && idleFor.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(15); }
                    string heldOpen = WanderHeld(clock.Elapsed.TotalSeconds, 0);
                    nextWander = -1;
                    CheckWander(clock.Elapsed.TotalSeconds, 0);
                    bool stayed = nextWander < 0;
                    landed.SkipAll();
                    putBack();
                    ++asking;
                    CloseTalk();
                    idleFor.Restart();
                    while (WanderHeld(clock.Elapsed.TotalSeconds, 0) != null && idleFor.ElapsedMilliseconds < 15000) { Application.DoEvents(); Thread.Sleep(15); }
                    string heldAfter = WanderHeld(clock.Elapsed.TotalSeconds, 0);
                    nextWander = -1;
                    CheckWander(clock.Elapsed.TotalSeconds, 0);
                    bool wandersAfter = nextWander >= 0;
                    preferences.Wander = wanderWas;
                    nextWander = wanderAt;
                    if (heldOpen != "first launch card" || !stayed) throw new Exception("With the card open she wasn't held by it alone: " + (heldOpen ?? "nothing held her") + ", stayed " + stayed);
                    if (heldAfter != null || !wandersAfter) throw new Exception("15 s after the card closed she still couldn't wander: " + (heldAfter ?? "nothing held her, yet she didn't plan a wander"));
                    // On a short screen or with big scaling its tallest screen still fits above the taskbar.
                    foreach (int[] screenCase in new[] { new[] { 768, 100, 48 }, new[] { 768, 125, 60 }, new[] { 1080, 150, 72 }, new[] { 1080, 175, 84 }, new[] { 1080, 225, 108 }, new[] { 720, 100, 48 } })
                    {
                        float want = screenCase[1] / 100f;
                        int room = screenCase[0] - screenCase[2];
                        float got = Mainframe.FirstRunCard.FitScale(want, room);
                        if (got > want || Mainframe.FirstRunCard.Tallest * got > room) throw new Exception("The card doesn't fit a " + screenCase[0] + " px screen at " + screenCase[1] + "%: " + got);
                        if (Mainframe.FirstRunCard.Tallest * want <= room - 8 && got != want) throw new Exception("The card was shrunk on a screen it fits: " + screenCase[0] + " px at " + screenCase[1] + "%");
                    }
                    passed("PASS the card opens once she's landed and follows her when she's moved, she doesn't wander off while it's open, and its tallest screen fits above the taskbar on a 1366 x 768 screen at 125% and a 1080p one at 175% or 225%");
                });

                part("SmokeFirstRunPaths", delegate
                {
                    // This PC, the default: the download starts on Next, the voice and EXTRAS screens run while it comes down with its
                    // progress on the card, and her first reply comes when it's in, here on the waiting screen.
                    card = open(FirstRun.Pick, FirstRun.Local);
                    foreach (string line in FirstRun.Lines) if (!Said.Knows(line)) throw new Exception("Her card line isn't learnt for translating: " + line);
                    if (!FirstRunShowing || card.Screen != FirstRun.Pick || card.Picked != FirstRun.Local || card.Count != "1 OF 4" || card.Line != FirstRun.Hello || reply != FirstRun.Hello)
                        throw new Exception("The card didn't open on ON THIS PC with her hello: " + card.Screen + " / " + card.Picked + " / " + card.Count + " / " + reply);
                    picture(card, "1-pick");
                    // Her free voice coming down while they pick, as a Store copy's first launch has it.
                    bool voiceWas = SettingUpVoice;
                    SettingUpVoice = true;
                    voiceGot = 212L << 20;
                    voiceTotal = LocalVoice.PackSize;
                    card.Go(FirstRun.Pick);
                    pump();
                    string voiceShown = card.GaugeShown;
                    picture(card, "1-pick-voice-coming");
                    SettingUpVoice = voiceWas;
                    voiceGot = voiceTotal = 0;
                    if (voiceShown != "MY FREE VOICE | " + Engine.Amount(212L << 20) + " of " + Engine.Amount(LocalVoice.PackSize) + ", downloading while you pick") throw new Exception("WAKE ME UP didn't show her free voice coming down: " + voiceShown);
                    card.Go(FirstRun.Pick);
                    card.Next();
                    pump();
                    if (setups != 1 || card.Screen != FirstRun.Voice || card.Count != "2 OF 4" || !card.FasterShown || card.GaugeShown == null || !card.GaugeShown.StartsWith("MY BRAIN | ", StringComparison.Ordinal) || reply != FirstRun.WhileLine)
                        throw new Exception("Next on ON THIS PC didn't start the download and go on to her voice: " + setups + " / " + card.Screen + " / " + card.GaugeShown + " / " + reply);
                    mainframe.ShowProgress(1, "Downloading " + fit.Name, fit.Size / 3, fit.Size);
                    pump();
                    if (card.GaugeShown != "MY BRAIN | " + Engine.Amount(fit.Size / 3) + " of " + fit.Download) throw new Exception("The card didn't show the download's progress: " + card.GaugeShown);
                    Preferences kept = LoadPreferences();
                    if (kept.FirstRunStep != FirstRun.Voice || kept.FirstRunPick != FirstRun.Local) throw new Exception("The card's step wasn't kept: " + kept.FirstRunStep + " / " + kept.FirstRunPick);
                    picture(card, "4-voice-downloading");
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Extras || card.Count != "3 OF 4" || card.ExtrasShown != "0000" || !card.FasterShown) throw new Exception("EXTRAS didn't follow her voice during the download: " + card.Screen + " / " + card.ExtrasShown);
                    picture(card, "5-extras-downloading");
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Wait || card.Count != "4 OF 4" || reply != FirstRun.WaitLine) throw new Exception("Done during the download didn't wait for her brain: " + card.Screen);
                    picture(card, "wait");
                    localDone();
                    wait(delegate { return card.Screen == FirstRun.Online && reply == online; }, "Her brain coming in didn't bring BRAIN ONLINE and her first reply: " + card.Screen + " / " + reply);
                    if (card.Count != "4 OF 4") throw new Exception("BRAIN ONLINE was numbered " + card.Count);
                    picture(card, "3-online-local");
                    card.Next();
                    pump();
                    if (FirstRunShowing || preferences.FirstRunStep != FirstRun.None || !greetTried || mainframe.Visible && mainframe.WindowState != mainframeState) throw new Exception("Done after her brain came in didn't finish the card quietly.");
                    passed("PASS ON THIS PC is picked first with Start with this!, Next starts her brain's download (a stand-in) and goes on to her voice and EXTRAS with its progress and the free key line on the card, Done waits for it, and her first reply brings BRAIN ONLINE, then Done finishes");

                    // The free key, Google AI Studio first: the page opens, Paste reads the clipboard once, Connect goes to MEDIUM.
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    pickBrain(card, FirstRun.Free);
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Key || card.Count != "2 OF 5" || browsed.Count == 0 || browsed[browsed.Count - 1] != GeminiKeyPage || reply != FirstRun.GoogleLine) throw new Exception("FREE KEY didn't open Google AI Studio's page with her line: " + card.Screen + " / " + reply);
                    picture(card, "2-free-key");
                    card.Tab(1, true);
                    pump();
                    if (card.Screen != FirstRun.Key || browsed[browsed.Count - 1] != FreeKeyPage || card.Provider != 1) throw new Exception("The NVIDIA tab didn't open its page.");
                    picture(card, "2-free-key-nvidia");
                    card.Tab(0, true);
                    card.Typed = "hello";
                    card.Next();
                    if (card.Told != Brain.NotAKey || card.Screen != FirstRun.Key) throw new Exception("Something that isn't a key was sent: " + card.Told);
                    // Paste with nothing to paste (an empty clipboard, or a picture on it), a key that picked up spaces and a line
                    // break, 50,000 characters, and a key that stays put when they switch tabs.
                    const string noKey = "There's no key on your clipboard yet. Copy it from the page, then press Paste.";
                    foreach (string nothing in new[] { null, "", " \r\n\t " })
                    {
                        FirstRunPaste = delegate { return nothing; };
                        card.Typed = "";
                        card.Paste();
                        if (card.Typed != "" || card.Told != noKey) throw new Exception("Paste with nothing on the clipboard didn't say so: " + card.Told);
                    }
                    FirstRunPaste = delegate { return " AIza-first\r\nrun-test\u200B "; };
                    card.Paste();
                    if (card.Typed != "AIza-firstrun-test" || card.Told != "") throw new Exception("A key with spaces and a line break in it wasn't cleaned up: " + card.Typed.Length + " / " + card.Told);
                    string huge = new string('x', 50000);
                    FirstRunPaste = delegate { return huge; };
                    card.Paste();
                    if (card.Typed.Length != 50000) throw new Exception("50,000 pasted characters didn't fill the box: " + card.Typed.Length);
                    card.Next();
                    if (card.Told != Brain.NotAKey || card.Brain == FirstRun.Connecting) throw new Exception("50,000 characters were sent as a key: " + card.Told);
                    FirstRunPaste = delegate { return "  AIza-firstrun-test  "; };
                    card.Paste();
                    card.Tab(1, false);
                    pump();
                    if (card.Typed != "AIza-firstrun-test" || card.Provider != 1) throw new Exception("Switching tabs lost the pasted key: " + card.Provider);
                    card.Paste();
                    if (card.Typed != "AIza-firstrun-test") throw new Exception("Paste didn't fill the key: " + card.Typed);
                    Brain.Google = serve(new[] { "!401 {\"error\":{\"message\":\"Key rejected\"}}" });
                    card.Next();
                    wait(delegate { return card.Brain != FirstRun.Connecting; }, "A rejected key never came back.");
                    if (card.Screen != FirstRun.Key || string.IsNullOrEmpty(card.Told) || brain.Remote != null) throw new Exception("A rejected key didn't stay on FREE KEY with what went wrong: " + card.Told);
                    picture(card, "2-free-key-rejected");
                    string rejected = card.Told;
                    Brain.Google = serve(new string[0]);
                    servers[servers.Count - 1].Stop();
                    card.Next();
                    wait(delegate { return card.Brain != FirstRun.Connecting; }, "A key with no internet never came back.");
                    if (card.Screen != FirstRun.Key || card.Told == null || card.Told.IndexOf("internet", StringComparison.Ordinal) < 0 || brain.Remote != null) throw new Exception("No internet didn't say so on FREE KEY: " + card.Told);
                    string offline = card.Told;
                    Brain.Google = serve(new[] { "{\"data\":[{\"id\":\"gemini-90-flash\"},{\"id\":\"gemini-90-pro\"}]}" });
                    card.Next();
                    wait(delegate { return card.Screen == FirstRun.Online && reply == online; }, "The free key didn't connect and bring her first reply: " + card.Screen + " / " + card.Told + " / " + reply);
                    if (brain.Remote == null || brain.Remote.Kind != "google" || preferences.Tier != 1 || card.Count != "3 OF 5") throw new Exception("The free key didn't go under MEDIUM INTELLIGENCE.");
                    if (rejected == offline || string.IsNullOrEmpty(rejected)) throw new Exception("A rejected key and no internet said the same thing: " + rejected);
                    picture(card, "3-online-free");
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Voice || card.Count != "4 OF 5" || reply != FirstRun.NoVoiceLine) throw new Exception("Her voice didn't follow BRAIN ONLINE: " + card.Screen + " / " + reply);
                    // As a new install has it: her voice on, and her free voice coming down since her first second.
                    preferences.Silent = false;
                    bool voiceWasComing = SettingUpVoice;
                    SettingUpVoice = true;
                    voiceGot = 230L << 20;
                    voiceTotal = LocalVoice.PackSize;
                    card.Redraw();
                    pump();
                    picture(card, "4-voice");
                    SettingUpVoice = voiceWasComing;
                    voiceGot = voiceTotal = 0;
                    preferences.Silent = true;
                    card.Redraw();
                    card.Voice(false);
                    bool textOnly = preferences.Silent;
                    card.Voice(true);
                    if (!textOnly || preferences.Silent) throw new Exception("TEXT ONLY and her free voice didn't switch her voice.");
                    // The mic test on a stand-in level: the bars move while it runs, and a test that heard them says so.
                    TestMic();
                    if (micTestLevel == null) throw new Exception("Test didn't start the mic test.");
                    double[] voiceLevels = { 0.002, 0.01, 0.04, 0.1, 0.06, 0.02, 0.15, 0.25, 0.09, 0.03, 0.01, 0.006, 0.02, 0.07, 0.12, 0.05, 0.015, 0.008 };
                    foreach (double level in voiceLevels) { micTestLevel(level); Application.DoEvents(); Thread.Sleep(90); }
                    pump();
                    picture(card, "4-voice-mic-test");
                    mainframe.VoiceNote(FirstRun.Heard, false);
                    StopMicTest(false);
                    wait(delegate { return card.Told == "I can hear you."; }, "A mic test that heard them didn't say so: " + card.Told);
                    picture(card, "4-voice-heard");
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Extras || card.Count != "5 OF 5" || card.ExtrasShown != "0000" || reply != FirstRun.ExtrasLine || card.FasterShown) throw new Exception("EXTRAS didn't come last with all four off: " + card.ExtrasShown + " / " + reply);
                    picture(card, "5-extras");
                    card.FlipExtra(0);
                    bool watchOn = preferences.Watch && card.ExtrasShown == "1000";
                    card.FlipExtra(0);
                    if (!watchOn || preferences.Watch || card.ExtrasShown != "0000") throw new Exception("An EXTRAS switch on the card didn't work both ways.");
                    card.Next();
                    pump();
                    if (FirstRunShowing || preferences.FirstRunStep != FirstRun.None) throw new Exception("Done on EXTRAS didn't finish the card.");
                    passed("PASS FREE KEY opens Google AI Studio's page first and each tab its own, Paste reads the clipboard (a stand-in) only when pressed, something that isn't a key and a rejected key stay on the screen saying why, and a free key connects under MEDIUM INTELLIGENCE with her first reply, then her voice, the mic test and EXTRAS, all four off and each switch working");

                    // MY PLAN OR KEY: a plan goes through her own UsePlan, and a paid key goes under MAX by itself.
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    pickBrain(card, FirstRun.Own);
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Plan || card.Count != "2 OF 5" || reply != FirstRun.PlanLine) throw new Exception("MY PLAN OR KEY didn't open its screen.");
                    picture(card, "2-plan");
                    card.Plan("claudecode");
                    if (planned != "claudecode") throw new Exception("Claude plan didn't ask for her Claude plan.");
                    card.Typed = "sk-proj-firstrun-test";
                    Brain.OpenAi = serve(new[] { "{\"data\":[{\"id\":\"gpt-90-mini\"},{\"id\":\"gpt-90\"}]}" });
                    card.Next();
                    wait(delegate { return card.Screen == FirstRun.Online && reply == online; }, "A paid key didn't connect: " + card.Screen + " / " + card.Told);
                    if (brain.Remote == null || brain.Remote.Kind != "openai" || preferences.Tier != 2) throw new Exception("A paid key didn't go under MAX INTELLIGENCE by itself.");
                    picture(card, "3-online-plan");
                    card.Skip();
                    pump();
                    if (card.Screen != FirstRun.Voice) throw new Exception("Skip on BRAIN ONLINE didn't move past it.");
                    card.SkipAll();
                    pump();
                    if (FirstRunShowing || preferences.FirstRunStep != FirstRun.None || mainframe.Visible && mainframe.WindowState == FormWindowState.Normal || mainframe.ExtrasShown != null) throw new Exception("Skip all with a brain didn't just end the card: " + mainframe.ExtrasShown);
                    putBack();
                    passed("PASS MY PLAN OR KEY asks for a Claude or ChatGPT plan through her own plan hookup (a stand-in) and a paid key goes under MAX INTELLIGENCE with no Move to MAX step; Skip on BRAIN ONLINE moves on and Skip all with a brain ends the card with no Mainframe and no EXTRAS");
                });

                // A click or key still on its way to the card when it changes under it: her brain coming online, the screen built
                // again, Skip all, a second card opened over it. The old screen's tile and Next get their click as if it landed
                // that moment, and nothing happens; a click on the live screen after a Mainframe pick still works.
                part("SmokeFirstRunStale", delegate
                {
                    System.Reflection.MethodInfo onClick = typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    Action<Control> land = delegate(Control target)
                    {
                        try { onClick.Invoke(target, new object[] { EventArgs.Empty }); }
                        catch (System.Reflection.TargetInvocationException problem) { throw new Exception("A late click on the card's " + (target.AccessibleName ?? target.Text) + " threw: " + problem.InnerException.Message + " " + problem.InnerException.StackTrace.Replace(Environment.NewLine, " | ")); }
                    };
                    Func<Mainframe.FirstRunCard, string, Control> named = delegate(Mainframe.FirstRunCard shown, string start)
                    {
                        foreach (Control control in shown.Controls)
                            if ((control.AccessibleName ?? "").StartsWith(start, StringComparison.Ordinal) || control.Text == start) return control;
                        throw new Exception("The card has no " + start + " on screen " + shown.Screen + ".");
                    };
                    string[] moments = { "brain online", "built again", "Skip all", "a second card" };
                    foreach (string moment in moments)
                    {
                        reset();
                        card = open(FirstRun.Pick, FirstRun.Local);
                        Control tile = named(card, "FREE KEY."), next = named(card, "Next");
                        Mainframe.FirstRunCard old = card;
                        if (moment == "brain online") card.BrainOnline();
                        else if (moment == "built again") card.Redraw();
                        else if (moment == "Skip all") card.SkipAll();
                        else card = OpenFirstRun(FirstRun.Pick, FirstRun.Local, false);
                        int screenThen = old.IsDisposed ? -1 : old.Screen, pickedThen = old.Picked;
                        land(tile);
                        land(next);
                        if (!old.IsDisposed && (old.Screen != screenThen || old.Picked != pickedThen)) throw new Exception("A late click after " + moment + " still acted on the card: screen " + screenThen + " -> " + old.Screen + ", pick " + pickedThen + " -> " + old.Picked);
                        if (moment == "a second card" && (old == firstRun || card != firstRun || !old.IsDisposed || card.Screen != FirstRun.Pick)) throw new Exception("A second card didn't take over from the first.");
                        pump();
                        if (moment == "Skip all" && FirstRunShowing) throw new Exception("A late click after Skip all brought the card back.");
                        if (FirstRunShowing) firstRun.SkipAll();
                        putBack();
                    }
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    SetLanguage("Korean");
                    mainframe.Sync();
                    pump();
                    land(named(card, "FREE KEY."));
                    if (card.Picked != FirstRun.Free) throw new Exception("A click on the live screen after a Mainframe pick didn't pick FREE KEY.");
                    land(named(card, "Next"));
                    pump();
                    if (card.Screen != FirstRun.Key) throw new Exception("Next on the live screen after a Mainframe pick didn't go on to FREE KEY: " + card.Screen);
                    card.SkipAll();
                    putBack();
                    passed("PASS a click or key still on its way when her brain comes online, the screen is built again, Skip all ends it or a second card opens does nothing and throws nothing, and clicks on the live screen still work");
                });

                part("SmokeFirstRunNoAi", delegate
                {
                    // NO AI, the fourth pick: Next finishes on JUST ME with no voice, mic or EXTRAS and no Mainframe, and the choice is
                    // kept. From then on nothing is asked of any brain from any path (a stand-in hears about anything turned away), what
                    // needs no brain still works, and what needs one gets her line once, then not again for a while.
                    reset();
                    int setupsAt = setups;
                    List<string> refused = new List<string>();
                    Brain.TurnedAway = delegate(string what) { lock (refused) refused.Add(what); };
                    Func<string, string> thinkWas = Think;
                    Think = null;
                    bool voiceWasOn = SettingUpVoice;
                    SettingUpVoice = true;
                    voiceGot = 96L << 20;
                    voiceTotal = LocalVoice.PackSize;
                    card = open(FirstRun.Pick, FirstRun.Local);
                    if (card.Picked != FirstRun.Local) throw new Exception("NO AI was picked first.");
                    pickBrain(card, FirstRun.NoAi);
                    card.Redraw();
                    pump();
                    picture(card, "1-pick-no-ai");
                    card.Next();
                    pump();
                    SettingUpVoice = voiceWasOn;
                    voiceGot = voiceTotal = 0;
                    if (card.Screen != FirstRun.Alone || card.Count != "2 OF 2" || reply != FirstRun.AloneLine || !preferences.NoAi || !Brain.Off || setups != setupsAt || !LoadPreferences().NoAi)
                        throw new Exception("Next on NO AI didn't finish on JUST ME with No AI kept: " + card.Screen + " / " + card.Count + " / " + reply + " / " + preferences.NoAi + " / " + setups);
                    picture(card, "6-just-me");
                    // A local brain an earlier look found isn't brought back by Back; only the look below can find one.
                    brain.SetServer(null, null);
                    card.Back();
                    pump();
                    if (card.Screen != FirstRun.Pick || preferences.NoAi || Brain.Off) throw new Exception("Back on JUST ME didn't take No AI off again.");
                    // Back looked for a brain again; that look finishes before No AI goes back on, so what's counted below is only what
                    // asked with No AI already on.
                    wait(delegate { return !finding && !brainLooking; }, "Her look for a brain after Back never finished.");
                    pump();
                    // A brain that look finds on this PC, like a running Ollama, takes the card to BRAIN ONLINE as at any first
                    // launch. That isn't what's checked here, so it's let go and the card opened again on WAKE ME UP.
                    string foundOnBack = "";
                    if (brain.Model != null || card.Screen != FirstRun.Pick)
                    {
                        if (brain.Model == null) throw new Exception("After Back the card left WAKE ME UP with no brain found: " + card.Trail);
                        foundOnBack = " (her look after Back found a brain on this PC and the card went to BRAIN ONLINE, as designed; it was opened again for the rest)";
                        brain.SetServer(null, null);
                        brain.Remote = null;
                        brain.Model = null;
                        card.SkipAll();
                        putBack();
                        card = open(FirstRun.Pick, FirstRun.Local);
                    }
                    refused.Clear();
                    pickBrain(card, FirstRun.NoAi);
                    card.Next();
                    card.Next();
                    pump();
                    if (FirstRunShowing || preferences.FirstRunStep != FirstRun.None || mainframe.Visible && mainframe.WindowState == FormWindowState.Normal || mainframe.ExtrasShown != null || !mainframe.NoAiShown)
                        throw new Exception("Done on JUST ME didn't just end the card with OFF picked under BRAIN.");
                    ++asking;
                    CloseTalk();
                    FindBrain();
                    FindBrain(true);
                    Hear("what's the capital of France?");
                    pump();
                    bool toldOnce = reply == Brain.OffLine && talkState != TalkState.Dormant;
                    ++asking;
                    CloseTalk();
                    Hear("tell me a joke");
                    pump();
                    bool quietAfter = talkState == TalkState.Off;
                    List<byte> pressed = new List<byte>();
                    Action<byte> pressWas = Hands.Press;
                    Hands.Press = delegate(byte key) { lock (pressed) pressed.Add(key); };
                    try
                    {
                        Hear("can you turn the volume up?");
                        wait(delegate { return talkState != TalkState.Thinking && reply != null; }, "Volume never answered with No AI on.");
                    }
                    finally { Hands.Press = pressWas; }
                    bool volumeWorks = reply == "Turned the volume up." && pressed.Count > 0;
                    ++asking;
                    CloseTalk();
                    // The gate itself: a model list asked for straight is turned away, and the stand-in hears it.
                    bool gated = false;
                    try { Brain.CloudModels("AIza-firstrun-test", true); }
                    catch (Exception) { gated = true; }
                    string turned = string.Join(", ", refused.ToArray());
                    bool heardGate = refused.Count == 1;
                    refused.Clear();
                    if (!toldOnce || !quietAfter || !volumeWorks || !gated || !heardGate || brain.Model != null || SettingUp)
                        throw new Exception("No AI went wrong: told once " + toldOnce + ", quiet after " + quietAfter + ", volume " + volumeWorks + " (" + reply + "), gate " + gated + "/" + heardGate + " (" + turned + "), model " + (brain.Model ?? "none"));
                    // Picking a brain under BRAIN switches No AI off; picking OFF leaves their keys in place.
                    preferences.NvidiaKey = Voice.Protect("AIza-firstrun-test");
                    mainframe.ClickBrainRow(0);
                    pump();
                    bool tierOn = !preferences.NoAi && !Brain.Off && !mainframe.NoAiShown;
                    // NORMAL's own look for a brain finishes first, or OFF would turn its last questions away.
                    wait(delegate { return !finding && !brainLooking; }, "NORMAL's look for a brain never finished.");
                    refused.Clear();
                    mainframe.ClickBrainRow(-1);
                    pump();
                    bool offAgain = preferences.NoAi && Brain.Off && mainframe.NoAiShown && preferences.NvidiaKey != null && brain.Model == null;
                    Brain.TurnedAway = null;
                    Think = thinkWas;
                    if (!tierOn || !offAgain) throw new Exception("BRAIN's rows didn't switch No AI: picking NORMAL " + tierOn + ", picking OFF " + offAgain);
                    if (refused.Count > 0) throw new Exception("With No AI on, something still asked a brain: " + string.Join(", ", refused.ToArray()));
                    Preferences oldFile;
                    using (MemoryStream saved = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Welcomed\":true}")))
                        oldFile = (Preferences)new DataContractJsonSerializer(typeof(Preferences)).ReadObject(saved);
                    string written;
                    using (MemoryStream again = new MemoryStream())
                    {
                        new DataContractJsonSerializer(typeof(Preferences)).WriteObject(again, oldFile);
                        written = System.Text.Encoding.UTF8.GetString(again.ToArray());
                    }
                    if (oldFile.NoAi || written.Contains("NoAi")) throw new Exception("Settings from before No AI read it as on, or gained it when saved.");
                    SetNoAi(false);
                    // Its look for a brain ends before the next checks, which start with none.
                    wait(delegate { return !finding && !brainLooking; }, "Her look for a brain after No AI never finished.");
                    reset();
                    passed("PASS NO AI is the fourth pick on WAKE ME UP, never the default: Next ends on JUST ME (2 OF 2) with her line about the Mainframe, no voice, mic or EXTRAS and no Mainframe, Back undoes it, and the choice is kept; with it on she never looks for a brain, chat gets her short line once and then stays quiet, volume still works by phrase, a direct model request is turned away and nothing else asked a brain; BRAIN's OFF row and NORMAL switch it; older settings files read and save without it" + foundOnBack);
                });

                part("SmokeFirstRunSkips", delegate
                {
                    // Skip on every screen moves past that step.
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    card.Skip();
                    pump();
                    if (setups != 1 || card.Screen != FirstRun.Voice || card.Count != "2 OF 3") throw new Exception("Skip on WAKE ME UP didn't move to her voice without a download: " + card.Screen + " / " + card.Count);
                    card.Skip();
                    pump();
                    if (card.Screen != FirstRun.Extras || card.Count != "3 OF 3") throw new Exception("Skip on her voice didn't move to EXTRAS.");
                    card.Skip();
                    pump();
                    if (FirstRunShowing || !mainframe.Visible || mainframe.WindowState != FormWindowState.Normal || mainframe.ExtrasShown != null) throw new Exception("Skipping every step with no brain didn't leave them in the Mainframe on Set up her brain.");
                    putBack();
                    foreach (int pick in new[] { FirstRun.Free, FirstRun.Own })
                    {
                        card = open(FirstRun.Pick, pick);
                        card.Next();
                        pump();
                        int was = card.Screen;
                        card.Skip();
                        pump();
                        if (was != (pick == FirstRun.Free ? FirstRun.Key : FirstRun.Plan) || card.Screen != FirstRun.Voice) throw new Exception("Skip on " + (pick == FirstRun.Free ? "FREE KEY" : "MY PLAN OR KEY") + " didn't move to her voice.");
                        card.SkipAll();
                        putBack();
                    }
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    card.Next();
                    card.Next();
                    card.Next();
                    pump();
                    if (card.Screen != FirstRun.Wait) throw new Exception("The waiting screen didn't come.");
                    card.Skip();
                    pump();
                    bool hidAndKept = !FirstRunShowing && preferences.FirstRunStep == FirstRun.None && SettingUp && !(mainframe.Visible && mainframe.WindowState == FormWindowState.Normal);
                    localDone();
                    wait(delegate { return reply == online; }, "Her brain coming in after the card was hidden didn't bring her hello.");
                    if (!hidAndKept || FirstRunShowing) throw new Exception("Hide this on the waiting screen didn't close the card and leave the download going.");
                    passed("PASS Skip on every screen moves past that step (WAKE ME UP, FREE KEY, MY PLAN OR KEY, BRAIN ONLINE, her voice, EXTRAS, and Hide this while she waits), and skipping every step with no brain leaves them in the Mainframe on Set up her brain");

                    // Skip all, and Alt + F4, from every screen ends the whole thing: never EXTRAS, and the Mainframe only when she
                    // still has no brain and none on its way, so they can see where to set one up.
                    foreach (bool viaClose in new[] { false, true })
                        foreach (int screen in new[] { FirstRun.Pick, FirstRun.Key, FirstRun.Plan, FirstRun.Online, FirstRun.Voice, FirstRun.Extras, FirstRun.Wait })
                            foreach (bool coming in new[] { false, true })
                            {
                                if (coming && (screen == FirstRun.Key || screen == FirstRun.Plan || screen == FirstRun.Online)) continue;
                                reset();
                                card = open(screen == FirstRun.Online ? FirstRun.Pick : screen, screen == FirstRun.Key ? FirstRun.Free : screen == FirstRun.Plan ? FirstRun.Own : FirstRun.Local);
                                if (screen == FirstRun.Online) { brain.Model = fit.Name; card.BrainOnline(); pump(); }
                                if (coming) { card.Brain = FirstRun.Coming; SettingUp = true; card.Redraw(); pump(); }
                                if (card.Screen != screen) throw new Exception("The card didn't open on screen " + screen + ".");
                                bool needsBrain = brain.Model == null && !coming;
                                if (viaClose) card.Close();
                                else card.SkipAll();
                                pump();
                                bool opened = mainframe.Visible && mainframe.WindowState == FormWindowState.Normal;
                                if (FirstRunShowing || preferences.FirstRunStep != FirstRun.None || mainframe.ExtrasShown != null || opened != needsBrain)
                                    throw new Exception((viaClose ? "Alt + F4" : "Skip all") + " on screen " + screen + (coming ? " with her brain coming" : "") + " left the Mainframe " + (opened ? "open" : "shut") + ", EXTRAS " + (mainframe.ExtrasShown ?? "shut") + ".");
                                putBack();
                            }
                    passed("PASS Skip all and Alt + F4 from every screen end the whole card for good: no EXTRAS panel, and the Mainframe opens only when she has no brain and none coming");

                    // Quitting halfway: next launch picks up on the same screen, her own brain's download carries on, and the free
                    // key page isn't opened again.
                    reset();
                    setups = 0;
                    card = open(FirstRun.Pick, FirstRun.Local);
                    card.Next();
                    pump();
                    SettingUp = false;
                    card.Dispose();
                    pump();
                    Preferences kept = LoadPreferences();
                    if (FirstRunShowing || kept.FirstRunStep != FirstRun.Voice || kept.FirstRunPick != FirstRun.Local || !WantsFirstRun(kept, false, false)) throw new Exception("Quitting on her voice screen didn't keep the step: " + kept.FirstRunStep);
                    card = OpenFirstRun(kept.FirstRunStep, kept.FirstRunPick, true);
                    pump();
                    if (card == null || card.Screen != FirstRun.Voice || setups != 2 || card.GaugeShown == null || reply != FirstRun.BackLine) throw new Exception("Coming back after a quit didn't pick up her voice screen and the download: " + (card == null ? "no card" : card.Screen + " / " + setups + " / " + reply));
                    picture(card, "resume-voice");
                    card.SkipAll();
                    putBack();
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    pickBrain(card, FirstRun.Free);
                    card.Next();
                    pump();
                    int pages = browsed.Count;
                    card.Dispose();
                    kept = LoadPreferences();
                    card = OpenFirstRun(kept.FirstRunStep, kept.FirstRunPick, true);
                    pump();
                    if (card == null || card.Screen != FirstRun.Key || browsed.Count != pages) throw new Exception("Coming back to FREE KEY after a quit opened the page again or lost the screen.");
                    card.SkipAll();
                    putBack();
                    passed("PASS quitting halfway keeps the screen and brain pick; next launch opens the card there with her welcome back, her own brain's download picks up again and the free key page isn't reopened; closing the card itself (Alt + F4 or Skip all) ends it for good");

                    // While the card is open her other first-minute lines wait: news and holidays wait for her hello, and Get to
                    // know me for the card.
                    reset();
                    card = open(FirstRun.Pick, FirstRun.Local);
                    ++asking;
                    CloseTalk();
                    newsPending = true;
                    int newsWas = newsTicket;
                    CheckNews(0);
                    bool newsHeld = newsPending && newsTicket == newsWas;
                    newsPending = false;
                    preferences.Met = false;
                    meetAt = clock.Elapsed.TotalSeconds - 1;
                    CheckIdle(clock.Elapsed.TotalSeconds, 0);
                    bool meetHeld = meetAt >= 0 && !meeting.Asking;
                    meetAt = -1;
                    preferences.Met = true;
                    card.SkipAll();
                    putBack();
                    if (!newsHeld || !meetHeld || !greetTried) throw new Exception("Her news or Get to know me talked over the card: news " + newsHeld + ", meet " + meetHeld);
                    passed("PASS while the card is open her news and holiday lines wait for her hello, which the card stands in for, and Get to know me waits for the card to finish");
                });

                part("SmokeFirstRunDesktop", delegate
                {
                    // Her first launch as it looks: a plain desktop with a taskbar, her standing where she lands, her bubble with the
                    // screen's line and the card beside her. Nothing may overlap or leave the screen, on 1920 x 1080 and 1366 x 768,
                    // with her in the middle or at either edge.
                    Point headAt = Head(), feetAt = Feet(), herAt = anchor;
                    Size herSize = Size;
                    List<string> scenes = new List<string>(), clashes = new List<string>(), nudged = new List<string>();
                    Func<Mainframe.FirstRunCard, Size, int, string, string> scene = delegate(Mainframe.FirstRunCard shown, Size screen, int side, string name)
                    {
                        pump();
                        Rectangle area = new Rectangle(0, 0, screen.Width, screen.Height - 48);
                        int x = side == 0 ? (area.Width - herSize.Width) / 2 : side < 0 ? area.Left - PaddingSize : area.Right - herSize.Width + PaddingSize;
                        Point at = new Point(x, area.Bottom - (feetAt.Y - herAt.Y));
                        Rectangle her = new Rectangle(at, herSize);
                        Point head = new Point(at.X + headAt.X - herAt.X, at.Y + headAt.Y - herAt.Y), feet = new Point(at.X + feetAt.X - herAt.X, at.Y + feetAt.Y - herAt.Y);
                        int spanLeft, spanRight;
                        BubbleSpan(head, 1f, area, out spanLeft, out spanRight);
                        // She steps aside first where the card fits neither side, as OpenFirstRun has her do.
                        int aside = Mainframe.FirstRunCard.Nudge(her, spanLeft, spanRight, area, shown.Width, shown.Unit);
                        if (aside != 0)
                        {
                            at.Offset(aside, 0);
                            her.Offset(aside, 0);
                            head.Offset(aside, 0);
                            feet.Offset(aside, 0);
                            BubbleSpan(head, 1f, area, out spanLeft, out spanRight);
                            nudged.Add(screen.Width + "x" + screen.Height + " " + (side == 0 ? "middle" : side < 0 ? "left" : "right") + " " + aside + " px");
                        }
                        Point cardAt = Mainframe.FirstRunCard.Beside(her, spanLeft, spanRight, area, shown.Size, shown.Unit);
                        Rectangle cardBox = new Rectangle(cardAt, shown.Size);
                        box.State = TalkState.Talking;
                        box.Prompt = "";
                        box.Heard = "first run";
                        box.Reply = shown.Line;
                        box.Revealed = shown.Line.Length;
                        Point bubbleAt;
                        using (Bitmap bubble = box.Render(1f, head, new Point(feet.X, feet.Y + 44), area, false, out bubbleAt))
                        using (Bitmap body = new Bitmap(herSize.Width, herSize.Height, PixelFormat.Format32bppArgb))
                        using (Bitmap cardShot = WindowPicture.Cut(shown))
                        {
                            using (Graphics drawing = Graphics.FromImage(body)) { drawing.Clear(Color.Transparent); Draw(drawing, idle, 0); }
                            Rectangle bodyInk = WindowPicture.Inked(body), bubbleInk = WindowPicture.Inked(bubble);
                            bodyInk.Offset(at);
                            bubbleInk.Offset(bubbleAt);
                            string where = screen.Width + "x" + screen.Height + (side == 0 ? " middle" : side < 0 ? " left edge" : " right edge");
                            string problem = bodyInk.IsEmpty || bubbleInk.IsEmpty ? where + ": her or her bubble didn't draw"
                                : !area.Contains(cardBox) ? where + ": the card leaves the screen, " + cardBox
                                : cardBox.IntersectsWith(bodyInk) ? where + ": the card covers her, " + cardBox + " / " + bodyInk
                                : cardBox.IntersectsWith(bubbleInk) ? where + ": the card covers her bubble by " + Rectangle.Intersect(cardBox, bubbleInk).Width + " px, " + cardBox + " / " + bubbleInk : null;
                            if (name == null) return problem;
                            using (Bitmap desk = new Bitmap(screen.Width, screen.Height, PixelFormat.Format24bppRgb))
                            using (Graphics drawing = Graphics.FromImage(desk))
                            {
                                using (LinearGradientBrush wall = new LinearGradientBrush(new Rectangle(0, 0, screen.Width, screen.Height), Color.FromArgb(66, 82, 104), Color.FromArgb(30, 36, 50), 70f)) drawing.FillRectangle(wall, 0, 0, screen.Width, screen.Height);
                                using (Brush bar = new SolidBrush(Color.FromArgb(32, 33, 38))) drawing.FillRectangle(bar, 0, area.Bottom, screen.Width, 48);
                                using (Brush icon = new SolidBrush(Color.FromArgb(72, 76, 88)))
                                    for (int i = -3; i <= 3; i++) drawing.FillRectangle(icon, screen.Width / 2 + i * 44 - 14, area.Bottom + 10, 28, 28);
                                drawing.DrawImageUnscaled(body, at);
                                drawing.DrawImageUnscaled(bubble, bubbleAt);
                                drawing.DrawImageUnscaled(cardShot, cardAt);
                                desk.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
                            }
                            scenes.Add(name);
                            return problem;
                        }
                    };
                    foreach (int screen in new[] { FirstRun.Pick, FirstRun.Key, FirstRun.Plan, FirstRun.Online, FirstRun.Voice, FirstRun.Extras, FirstRun.Wait })
                    {
                        reset();
                        if (screen == FirstRun.Online) { brain.Remote = new Cloud("AIza-firstrun-test", "gemini-90-flash"); brain.Model = "gemini-90-flash"; preferences.Tier = 1; }
                        bool coming = screen == FirstRun.Voice || screen == FirstRun.Extras || screen == FirstRun.Wait;
                        if (coming) mainframe.ShowProgress(1, "Downloading " + fit.Name, fit.Size / 3, fit.Size);
                        bool voiceBefore = SettingUpVoice;
                        // As a new install has it: her voice on and her free voice coming down.
                        preferences.Silent = false;
                        SettingUpVoice = true;
                        voiceGot = 96L << 20;
                        voiceTotal = LocalVoice.PackSize;
                        card = OpenFirstRun(screen == FirstRun.Online ? FirstRun.Pick : screen, screen == FirstRun.Key ? FirstRun.Free : screen == FirstRun.Plan ? FirstRun.Own : FirstRun.Local, false, 1f);
                        if (screen == FirstRun.Online) card.BrainOnline();
                        if (coming) { card.Brain = FirstRun.Coming; SettingUp = true; card.Go(screen); }
                        pump();
                        if (card.Line == null) card.Line = online;
                        string[] names = { "", "desktop-1-pick-1080-middle", "desktop-2-free-key-1080-right", null, "desktop-4-voice-768-middle", null, null, null };
                        foreach (Size size in new[] { new Size(1920, 1080), new Size(1366, 768) })
                            foreach (int side in new[] { 0, -1, 1 })
                            {
                                string name = names[screen] != null && names[screen].IndexOf(size.Height.ToString(), StringComparison.Ordinal) >= 0 && names[screen].EndsWith(side == 0 ? "middle" : side < 0 ? "left" : "right", StringComparison.Ordinal) ? names[screen] : null;
                                string clash = scene(card, size, side, name);
                                if (clash != null) clashes.Add(card.Count + " " + screen + " " + clash);
                            }
                        SettingUpVoice = voiceBefore;
                        voiceGot = voiceTotal = 0;
                        card.SkipAll();
                        putBack();
                    }
                    if (scenes.Count != 3 || clashes.Count > 0) throw new Exception("Her first launch on a desktop: " + scenes.Count + " pictures; " + string.Join("; ", clashes.ToArray()));
                    passed("PASS on a 1920 x 1080 and a 1366 x 768 desktop, with her in the middle or at either edge, every screen of the card sits beside her on the screen without covering her or her bubble (she steps aside first where it fits neither side: " + string.Join(", ", nudged.ToArray()) + "): " + string.Join(", ", scenes.ToArray()));
                });

                part("SmokeFirstRunScales", delegate
                {
                    // Every screen at 125% and 150%.
                    foreach (float scale in new[] { 1.25f, 1.5f })
                    {
                        string size = ((int)Math.Round(scale * 100)).ToString();
                        foreach (int screen in new[] { FirstRun.Pick, FirstRun.Key, FirstRun.Plan, FirstRun.Online, FirstRun.Voice, FirstRun.Extras, FirstRun.Wait })
                        {
                            reset();
                            if (screen == FirstRun.Online) { brain.Remote = new Cloud("AIza-firstrun-test", "gemini-90-flash"); brain.Model = "gemini-90-flash"; preferences.Tier = 1; }
                            if (screen == FirstRun.Voice || screen == FirstRun.Extras || screen == FirstRun.Wait) mainframe.ShowProgress(1, "Downloading " + fit.Name, fit.Size / 3, fit.Size);
                            card = OpenFirstRun(screen == FirstRun.Online ? FirstRun.Pick : screen, screen == FirstRun.Key ? FirstRun.Free : screen == FirstRun.Plan ? FirstRun.Own : FirstRun.Local, false, scale);
                            if (screen == FirstRun.Online) card.BrainOnline();
                            if (screen == FirstRun.Voice || screen == FirstRun.Extras || screen == FirstRun.Wait) { setups = 0; card.Brain = FirstRun.Coming; SettingUp = true; card.Go(screen); }
                            pump();
                            if (Math.Abs(card.Unit - scale) > 0.01f) throw new Exception("The card wasn't drawn at " + scale + ": " + card.Unit);
                            string[] names = { "", "1-pick", "2-free-key", "3-online", "4-voice", "5-extras", "2-plan", "wait" };
                            picture(card, names[screen] + "-" + size);
                            card.SkipAll();
                            putBack();
                        }
                    }
                    passed("PASS every screen fits with nothing overlapping or cut off at 100%, 125% and 150% (drawn on this desktop at " + mainframe.Px(1000) / 10 + "%), pictures by " + string.Join(" and ", ways.ToArray()) + ": " + string.Join(", ", pictures.ToArray()));

                    // The keys typed and pasted above never reach her log, her chat or these checks. Her settings hold them sealed and
                    // aren't read here.
                    List<string> leaks = new List<string>();
                    foreach (string line in checks) if (line.IndexOf("firstrun-test", StringComparison.Ordinal) >= 0) leaks.Add("a check line");
                    List<string> folders = new List<string> { output };
                    if (Brain.LogPath != null) folders.Add(Path.GetDirectoryName(Brain.LogPath));
                    foreach (string folder in folders)
                    {
                        if (!Directory.Exists(folder)) continue;
                        foreach (string file in Directory.GetFiles(folder))
                        {
                            string named = Path.GetFileName(file).ToLowerInvariant();
                            if (named.StartsWith("settings", StringComparison.Ordinal) || !(named.EndsWith(".txt") || named.EndsWith(".json") || named.EndsWith(".jsonl") || named.EndsWith(".md") || named.EndsWith(".log"))) continue;
                            string text;
                            try { text = File.ReadAllText(file); } catch (IOException) { continue; }
                            if (text.IndexOf("firstrun-test", StringComparison.Ordinal) >= 0) leaks.Add(named);
                        }
                    }
                    if (leaks.Count > 0) throw new Exception("A key from the card was written down in: " + string.Join(", ", leaks.ToArray()));
                    passed("PASS no key typed or pasted on the card is written to her log, her chat files or the checks, and the key box shows dots in every picture");
                });
            }
            // A slip in the checks themselves says where, since the section only keeps the message.
            catch (NullReferenceException slip) { throw new Exception(slip.Message + " " + slip.StackTrace.Replace(Environment.NewLine, " | ") + (firstRun != null ? " / the card's trail: " + firstRun.Trail : "")); }
            finally
            {
                if (firstRun != null && !firstRun.IsDisposed) firstRun.Dispose();
                firstRun = null;
                ++asking;
                CloseTalk();
                foreach (System.Net.Sockets.TcpListener server in servers) server.Stop();
                preferences = wasPreferences;
                Brain.Language = wasLanguage;
                Thread.CurrentThread.CurrentUICulture = wasCulture;
                PointEars();
                Said.Ready = wasSaidReady;
                SavePreferences();
                Think = wasThink;
                Browse = wasBrowse;
                Brain.Google = wasGoogle;
                Brain.OpenAi = wasOpenAi;
                FirstRunSetUp = null;
                Brain.Off = false;
                Brain.TurnedAway = null;
                FirstRunPaste = wasPaste;
                FirstRunUsePlan = null;
                FirstRunLanding = null;
                machine = wasMachine;
                fit = wasFit;
                brain.Remote = wasRemote;
                brain.Local = wasLocal;
                brain.SetServer(wasServer, wasServerKey);
                brain.Model = wasModel;
                cloudModels = wasModels;
                SettingUp = wasSettingUp;
                greetTried = wasGreeted;
                newsPending = wasNews;
                meetAt = wasMeet;
                heard = wasHeard;
                if (micTest != null) StopMicTest(false);
                mainframe.VoiceNote(wasNote, false);
                ShowTiers();
                putBack();
            }
        }
    }
}
