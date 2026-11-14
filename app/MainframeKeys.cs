using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Companion
    {
        internal const int FrameSlot = 7;
        private static readonly int[] FrameSpares = { 0x4D, 0x4F, 0x49, 0x4C, 0x4B };

        internal string FrameKeyName { get { return KeyName(preferences.FrameMods, preferences.FrameVk); } }

        internal bool AdvancedSaved { get { return preferences.AdvancedSettings; } }

        internal int FrameKeyMods { get { return preferences.FrameMods; } }

        internal int FrameKeyVk { get { return preferences.FrameVk; } }

        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);

        private static bool FrameClashes(Preferences value, int mods, int vk)
        {
            int[] bound = { value.TalkMods, value.TalkVk, value.MuteMods, value.MuteVk, value.TypeMods, value.TypeVk, value.HideMods, value.HideVk, value.QuietMods, value.QuietVk, value.GameMods, value.GameVk, value.PushMods, value.PushVk };
            for (int i = 0; i < bound.Length; i += 2)
                if (bound[i + 1] != 0 && bound[i] == mods && bound[i + 1] == vk) return true;
            return false;
        }

        internal static void UnclashFrame(Preferences value)
        {
            if (!Bindable(value.FrameMods, value.FrameVk)) { value.FrameMods = 3; value.FrameVk = 0x4D; }
            if (!FrameClashes(value, value.FrameMods, value.FrameVk)) return;
            foreach (int vk in FrameSpares)
                if (!FrameClashes(value, 3, vk)) { value.FrameMods = 3; value.FrameVk = vk; return; }
        }

        private string SetFrameKey(int mods, int vk)
        {
            if (!Bindable(mods, vk)) return "Press a key, alone or with Ctrl, Alt, Shift or Win.";
            string name = KeyName(mods, vk);
            int[] bound = { preferences.TalkMods, preferences.TalkVk, preferences.MuteMods, preferences.MuteVk, preferences.TypeMods, preferences.TypeVk, preferences.HideMods, preferences.HideVk, preferences.QuietMods, preferences.QuietVk, preferences.GameMods, preferences.GameVk };
            for (int i = 0; i < KeyJobs.Length; i++)
                if (mods == bound[i * 2] && vk == bound[i * 2 + 1]) return name + " is already her " + KeyJobs[i] + " key.";
            if (preferences.PushVk != 0 && mods == preferences.PushMods && vk == preferences.PushVk) return name + " is already her push-to-talk key.";
            if (!testing && IsHandleCreated)
            {
                if (frameKey) Native.UnregisterHotKey(Handle, FrameKeyId);
                bool held = ByMouse(vk) || Native.RegisterHotKey(Handle, FrameKeyId, mods | 0x4000, vk);
                frameKey = held || ByMouse(preferences.FrameVk) || Native.RegisterHotKey(Handle, FrameKeyId, preferences.FrameMods | 0x4000, preferences.FrameVk);
                if (!held) return name + " is taken by another app. Try another.";
            }
            preferences.FrameMods = mods;
            preferences.FrameVk = vk;
            SavePreferences();
            WatchMouse();
            mainframe.FrameKey(FrameKeyName, testing || frameKey);
            return null;
        }

        private bool MainframeAhead()
        {
            if (!mainframe.Visible || mainframe.WindowState == FormWindowState.Minimized) return false;
            Form front = Control.FromHandle(Native.GetForegroundWindow()) as Form;
            while (front != null && front != mainframe) front = front.Owner;
            return front != null;
        }

        internal void ToggleMainframe()
        {
            if (mainframe.Picking || (mainframe.IsHandleCreated && !IsWindowEnabled(mainframe.Handle))) return;
            if (MainframeAhead()) mainframe.Close();
            else mainframe.Restore();
        }

        private void SmokeMainframeKey(List<string> checks)
        {
            FormWindowState stateWas = mainframe.WindowState;
            bool shownWas = mainframe.Visible, pagedWas = mainframe.SettingsShown, moddedWas = mainframe.ModsShown;
            int modsWas = preferences.FrameMods, vkWas = preferences.FrameVk;
            try
            {
                SetHotkey(FrameSlot, 3, 0x4D);
                string button = mainframe.FrameKeyShown;
                mainframe.Restore();
                mainframe.WindowState = FormWindowState.Minimized;
                Message press = Message.Create(Handle, 0x0312, new IntPtr(FrameKeyId), IntPtr.Zero);
                WndProc(ref press);
                bool opened = mainframe.Visible && mainframe.WindowState == FormWindowState.Normal;
                bool ahead = MainframeAhead();
                WndProc(ref press);
                bool closed = (mainframe.WindowState == FormWindowState.Minimized) == ahead;
                mainframe.Restore();
                mainframe.ShowSettings(true);
                mainframe.PickKeys(FrameSlot);
                WndProc(ref press);
                bool kept = mainframe.Picking && mainframe.WindowState == FormWindowState.Normal;
                mainframe.TakeKeys(Keys.Control | Keys.Alt | Keys.O);
                mainframe.ConfirmKeys();
                string saved = mainframe.KeysNoteText, moved = mainframe.FrameKeyShown;
                mainframe.ShowSettings(false);
                string back = SetHotkey(FrameSlot, 3, 0x4D);
                string clash = SetHotkey(FrameSlot, 3, 0x56);
                string taken = SetHotkey(5, 3, 0x4D);
                string pushTaken = SetHotkey(PushSlot, 3, 0x4D);
                Preferences onGame = new Preferences { GameMods = 3, GameVk = 0x4D };
                CheckKeys(onGame);
                Preferences older = (Preferences)new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(Preferences)).ReadObject(new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes("{\"Size\":240}")));
                Preferences fresh = new Preferences();
                if (button != "Mainframe: Ctrl + Alt + M" || !opened || !closed || !kept || saved != "Saved. Ctrl + Alt + O opens her Mainframe." || moved != "Mainframe: Ctrl + Alt + O" || back != null
                    || clash != "Ctrl + Alt + V is already her talk key." || taken != "Ctrl + Alt + M is already her Mainframe key." || pushTaken != "Ctrl + Alt + M is already her Mainframe key."
                    || onGame.GameVk != 0x4D || onGame.FrameMods != 3 || onGame.FrameVk != 0x4F || older.FrameMods != 3 || older.FrameVk != 0x4D || fresh.FrameMods != 3 || fresh.FrameVk != 0x4D || FrameKeyName != "Ctrl + Alt + M")
                    throw new Exception("Her Mainframe key went wrong: " + button + " / opened " + opened + ", closed " + closed + " with it in front " + ahead + ", kept while picking " + kept + " / " + saved + " / " + moved + " / back " + back + " / " + clash + " / " + taken + " / " + pushTaken + " / spare " + KeyName(onGame.FrameMods, onGame.FrameVk) + " / older " + KeyName(older.FrameMods, older.FrameVk));
                checks.Add("PASS Mainframe key: Ctrl + Alt + M opens her Mainframe from minimised and closes it again when it's in front, it shows in KEYS and changes there like her other keys, it stays put while a key is being picked, a key she already uses is refused both ways, and on settings where another key has Ctrl + Alt + M it moves to a free spare");
            }
            finally
            {
                mainframe.CancelKeys();
                mainframe.ForgetKeysNote();
                mainframe.ShowSettings(pagedWas);
                if (moddedWas) mainframe.ShowMods(true);
                if (preferences.FrameMods != modsWas || preferences.FrameVk != vkWas) SetHotkey(FrameSlot, modsWas, vkWas);
                if (!shownWas) mainframe.Hide();
                else mainframe.WindowState = stateWas;
            }
        }
    }

    internal sealed partial class Mainframe
    {
        private sealed class Stop
        {
            internal Control Control;
            internal Head Head;
            internal int Section, Row;
            internal float Middle;
        }

        private static void FocusMark(Graphics graphics, RectangleF around, float s, float topRight, float bottomRight, float bottomLeft)
        {
            float wide = 2 * Math.Max(1f, s);
            RectangleF box = new RectangleF(around.Left + wide / 2, around.Top + wide / 2, around.Width - wide, around.Height - wide);
            using (GraphicsPath path = Cut(box, 0, topRight, bottomRight, bottomLeft))
            using (Pen pen = new Pen(White, wide))
                graphics.DrawPath(pen, path);
        }

        private static bool Steerable(Control control) { return control is Key || control is Toggle || control is Slider || control is Rung || control is Entry; }

        private int ColumnOf(Control control) { return paged && control.Left >= Scaled(Column) ? 1 : 0; }

        private List<Stop> Stops()
        {
            float s = Unit();
            List<Head> heads = new List<Head>();
            List<Stop> stops = new List<Stop>();
            foreach (Control control in Controls)
            {
                if (!control.Visible || control.Left >= Far) continue;
                Head head = control as Head;
                if (head != null) { if (head.Span > 0) heads.Add(head); continue; }
                if (control.Enabled && Steerable(control)) stops.Add(new Stop { Control = control, Middle = control.Top + control.Height / 2f });
            }
            heads.Sort(delegate(Head one, Head other) { return ColumnOf(one) != ColumnOf(other) ? ColumnOf(one).CompareTo(ColumnOf(other)) : one.Top.CompareTo(other.Top); });
            foreach (Stop stop in stops)
            {
                stop.Section = -1;
                for (int i = 0; i < heads.Count; i++)
                    if (ColumnOf(heads[i]) == ColumnOf(stop.Control) && heads[i].Band(s).Top <= stop.Middle) { stop.Section = i; stop.Head = heads[i]; }
            }
            stops.Sort(delegate(Stop one, Stop other) { return one.Section != other.Section ? one.Section.CompareTo(other.Section) : one.Middle != other.Middle ? one.Middle.CompareTo(other.Middle) : one.Control.Left.CompareTo(other.Control.Left); });
            int row = 0;
            float anchor = 0;
            for (int i = 0; i < stops.Count; i++)
            {
                if (i == 0 || stops[i].Section != stops[i - 1].Section || stops[i].Middle - anchor > 10 * s) { row++; anchor = stops[i].Middle; }
                stops[i].Row = row;
            }
            stops.Sort(delegate(Stop one, Stop other) { return one.Row != other.Row ? one.Row.CompareTo(other.Row) : one.Control.Left.CompareTo(other.Control.Left); });
            return stops;
        }

        private Control Held()
        {
            Control held = ActiveControl;
            while (held != null && held.Parent != this) held = held.Parent;
            return held;
        }

        private static int At(List<Stop> stops, Control control)
        {
            for (int i = 0; i < stops.Count; i++) if (stops[i].Control == control) return i;
            return -1;
        }

        private void ShowCues() { if (IsHandleCreated) Native.SendMessage(Handle, 0x0127, new IntPtr(2 | (1 << 16)), IntPtr.Zero); }

        private void Hold(Control control)
        {
            ShowCues();
            Entry entry = control as Entry;
            Rung rung = control as Rung;
            if (rung != null) rung.Arriving = true;
            try { if (entry != null) entry.Box.Select(); else control.Select(); }
            finally { if (rung != null) rung.Arriving = false; }
            control.Invalidate();
        }

        private void Land()
        {
            List<Stop> stops = Stops();
            foreach (Stop stop in stops) if (stop.Section >= 0) { Hold(stop.Control); return; }
            if (stops.Count > 0) Hold(stops[0].Control);
        }

        private void Step(int by)
        {
            List<Stop> stops = Stops();
            if (stops.Count == 0) return;
            int at = At(stops, Held());
            if (at < 0 && by > 0) { Land(); return; }
            Hold(stops[(at < 0 ? stops.Count - 1 : at + by + stops.Count) % stops.Count].Control);
        }

        private void Jump(int by)
        {
            List<Stop> stops = Stops();
            List<int> firsts = new List<int>();
            for (int i = 0; i < stops.Count; i++)
                if (stops[i].Section >= 0 && (i == 0 || stops[i].Section != stops[i - 1].Section)) firsts.Add(i);
            if (firsts.Count == 0) { Step(by); return; }
            int at = At(stops, Held());
            int now = -1;
            if (at >= 0 && stops[at].Section >= 0)
                for (int i = 0; i < firsts.Count; i++) if (stops[firsts[i]].Section == stops[at].Section) now = i;
            int next = now < 0 ? (by > 0 ? 0 : firsts.Count - 1) : (now + by + firsts.Count) % firsts.Count;
            Hold(stops[firsts[next]].Control);
        }

        private void Along(int by)
        {
            List<Stop> stops = Stops();
            int at = At(stops, Held());
            int next = at + by;
            if (at < 0 || next < 0 || next >= stops.Count || stops[next].Row != stops[at].Row) return;
            Hold(stops[next].Control);
        }

        private ContextMenuStrip Opened()
        {
            foreach (ContextMenuStrip strip in dropdowns) if (!strip.IsDisposed && strip.Visible) return strip;
            return null;
        }

        private static void SelectChoice(ContextMenuStrip strip)
        {
            ToolStripItem first = null;
            foreach (ToolStripItem item in strip.Items)
            {
                ToolStripMenuItem choice = item as ToolStripMenuItem;
                if (choice == null || !choice.Enabled || !choice.Available) continue;
                if (first == null) first = choice;
                if (choice.Checked) { first = choice; break; }
            }
            if (first != null) first.Select();
        }

        private bool Act(Control held)
        {
            Toggle toggle = held as Toggle;
            Rung rung = held as Rung;
            Key key = held as Key;
            if (toggle != null || rung != null) { ShowCues(); InvokeOnClick(held, EventArgs.Empty); return true; }
            if (key == null) return false;
            ShowCues();
            key.PerformClick();
            ContextMenuStrip opened = Opened();
            if (opened != null) SelectChoice(opened);
            return true;
        }

        private void StepBack()
        {
            ContextMenuStrip opened = Opened();
            if (opened != null) { opened.Close(); return; }
            if (modded) { ShowMods(false); return; }
            if (paged) { ShowSettings(false); return; }
            Close();
        }

        private bool TurnPage(Keys key)
        {
            if (key == Keys.S) { if (!paged) { ShowSettings(true); Land(); } return true; }
            if (key == Keys.M) { if (!modded) { ShowMods(true); Land(); } return true; }
            if (key == Keys.H) { if (modded) ShowMods(false); if (paged) ShowSettings(false); Land(); return true; }
            if (key == Keys.A && paged) { advancedKey.PerformClick(); Land(); return true; }
            return false;
        }

        private bool NeedsClick()
        {
            if (capturing == Companion.PushSlot + 1 || (pendingMods & 11) != 0) return false;
            return Companion.Swallows(pendingMods, pendingVk) || (pendingVk >= 0x21 && pendingVk <= 0x28) || pendingVk == 0x1B || pendingVk == 0x2D || pendingVk == 0x2E;
        }

        private bool EndsPicking(Keys keyData, bool repeated)
        {
            if (keyData != Keys.Enter && keyData != Keys.Escape || WinHeld()) return false;
            if (repeated) return true;
            if (pendingMods < 0) return false;
            if (keyData == Keys.Escape) { CancelKeys(); return true; }
            if (NeedsClick()) KeysNote("Click Confirm to keep " + Companion.KeyName(pendingMods, pendingVk) + ", or press Esc.", false);
            else ConfirmKeys();
            return true;
        }

        private bool HeldEnter(Message m)
        {
            return m.Msg == 0x0100 && m.WParam.ToInt64() == 0x0D && (m.LParam.ToInt64() & 0x40000000) != 0 && Opened() != null;
        }

        internal void ForgetKeysNote() { keysNote.Text = ""; }

        private bool Steer(Keys keyData, bool repeated)
        {
            Keys key = keyData & Keys.KeyCode;
            Keys with = keyData & Keys.Modifiers;
            if (WinHeld()) return false;
            if (key == Keys.Escape && with == Keys.None) { StepBack(); return true; }
            if (Opened() != null) return false;
            TextBoxBase box = ActiveControl as TextBoxBase;
            bool writing = box != null;
            if (key == Keys.Tab && (with == Keys.None || with == Keys.Shift))
            {
                if (writing && box.Multiline && box.AcceptsTab) return false;
                Jump(with == Keys.Shift ? -1 : 1);
                return true;
            }
            Control held = Held();
            Entry entry = held as Entry;
            if (key == Keys.Down && with == Keys.Alt && entry != null && entry.Opens) { entry.Open(); return true; }
            if (with != Keys.None || (writing && box.Multiline)) return false;
            if (key == Keys.Up || key == Keys.Down) { Step(key == Keys.Up ? -1 : 1); return true; }
            if (key == Keys.Left || key == Keys.Right)
            {
                if (writing || held is Slider) return false;
                Along(key == Keys.Left ? -1 : 1);
                return true;
            }
            if (writing) return false;
            if (key == Keys.Enter) return repeated || Act(held);
            if (repeated && (key == Keys.S || key == Keys.M || key == Keys.H || (key == Keys.A && paged))) return true;
            return TurnPage(key);
        }

        internal bool Strike(Keys keys, bool repeated = false)
        {
            Message press = Message.Create(Handle, 0x0100, new IntPtr((int)(keys & Keys.KeyCode)), new IntPtr(repeated ? 0x40000000 : 0));
            return ProcessCmdKey(ref press, keys);
        }

        private static bool MenuKey(ContextMenuStrip strip, Keys key)
        {
            Message press = Message.Create(strip.Handle, 0x0100, new IntPtr((int)key), IntPtr.Zero);
            return strip.PreProcessMessage(ref press);
        }

        private static ToolStripItem Chosen(ContextMenuStrip strip)
        {
            foreach (ToolStripItem item in strip.Items) if (item.Selected) return item;
            return null;
        }

        private string HeldSection()
        {
            List<Stop> stops = Stops();
            int at = At(stops, Held());
            return at < 0 ? "nothing" : stops[at].Head == null ? "title" : stops[at].Head.Shown;
        }

        private bool HeldFirst()
        {
            List<Stop> stops = Stops();
            int at = At(stops, Held());
            return at >= 0 && (at == 0 || stops[at - 1].Section != stops[at].Section);
        }

        private bool HeldMarked()
        {
            Control held = Held();
            Key key = held as Key;
            Toggle toggle = held as Toggle;
            Slider slider = held as Slider;
            Rung rung = held as Rung;
            Entry entry = held as Entry;
            return key != null ? key.Marked : toggle != null ? toggle.Marked : slider != null ? slider.Marked : rung != null ? rung.Marked : entry != null && entry.Marked;
        }

        private string TabWalk(int by, int most)
        {
            List<string> names = new List<string>();
            for (int i = 0; i < most; i++)
            {
                Strike(by > 0 ? Keys.Tab : Keys.Tab | Keys.Shift);
                if (!HeldFirst()) return "Tab landed past the first option of " + HeldSection();
                if (names.Count > 0 && HeldSection() == names[0] && Held() == walkFrom) break;
                if (names.Count == 0) walkFrom = Held();
                names.Add(HeldSection());
            }
            return string.Join(" ", names.ToArray());
        }

        private Control walkFrom;

        internal void CheckKeyboard(List<string> checks)
        {
            FormWindowState stateWas = WindowState;
            bool pagedWas = paged, moddedWas = modded, shownWas = Visible, advancedWas = capri.AdvancedSaved;
            int frameModsWas = capri.FrameKeyMods, frameVkWas = capri.FrameKeyVk;
            Restore();
            ShowMods(false);
            ShowSettings(false);
            try
            {
                if (Control.MouseButtons != MouseButtons.None) { checks.Add("LIMIT Mainframe keyboard: a mouse button is held"); return; }
                if (!more.Focus() || !more.Focused) { checks.Add("LIMIT Mainframe keyboard: Windows did not give the Mainframe keyboard focus"); return; }
                CheckPageKeys(checks);
                CheckSectionKeys(checks);
                CheckArrowKeys(checks);
                CheckEnterKey(checks);
                CheckSliderKeys(checks);
                CheckEscKey(checks);
                CheckPickingKeys(checks);
            }
            finally
            {
                ContextMenuStrip left = Opened();
                if (left != null) left.Close();
                CancelKeys();
                if (capri.FrameKeyMods != frameModsWas || capri.FrameKeyVk != frameVkWas) capri.SetHotkey(Companion.FrameSlot, frameModsWas, frameVkWas);
                if (capri.AdvancedSaved != advancedWas) capri.ToggleAdvancedSettings();
                keysNote.Text = "";
                if (!Visible) Show();
                if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
                ShowMods(false);
                ShowSettings(pagedWas);
                if (moddedWas) ShowMods(true);
                more.Focus();
                dismissedHint = null;
                DismissHint();
                if (!shownWas) Hide();
                else WindowState = stateWas;
            }
        }

        private void CheckPageKeys(List<string> checks)
        {
            more.Focus();
            bool settings = Strike(Keys.S) && paged && !modded && HeldSection() == "YOU" && HeldFirst() && HeldMarked();
            bool advancedWas = capri.AdvancedSaved;
            bool advanced = Strike(Keys.A) && paged && capri.AdvancedSaved != advancedWas && Strike(Keys.A) && capri.AdvancedSaved == advancedWas && HeldSection() == "YOU";
            bool mods = Strike(Keys.M) && modded && !paged && HeldSection() == "MODS" && HeldFirst();
            bool again = Strike(Keys.M) && modded;
            bool home = Strike(Keys.H) && !modded && !paged && HeldSection() == "BRAIN" && HeldFirst();
            bool offSettings = !Strike(Keys.A) && !paged;
            Strike(Keys.S);
            Hold(nickname);
            bool typed = nickname.Box.Focused && !Strike(Keys.S) && !Strike(Keys.M) && !Strike(Keys.H) && !Strike(Keys.A) && paged && !modded && capri.AdvancedSaved == advancedWas && Held() == nickname;
            bool held = !Strike(Keys.Control | Keys.M) && !Strike(Keys.Alt | Keys.S) && paged && !modded;
            Strike(Keys.Up);
            bool heldDown = Strike(Keys.M, true) && Strike(Keys.A, true) && !modded && paged && capri.AdvancedSaved == advancedWas;
            Strike(Keys.H);
            if (!settings || !advanced || !mods || !again || !home || !offSettings || !typed || !held || !heldDown)
                throw new Exception("Her Mainframe's page keys went wrong: settings " + settings + ", advanced " + advanced + ", mods " + mods + ", again " + again + ", home " + home + ", A off Settings " + offSettings + ", in a text box " + typed + ", with Ctrl or Alt " + held + ", a held key ignored " + heldDown);
            checks.Add("PASS Mainframe page keys: S opens Settings, M opens Mods, H goes back to her main page and A switches Simple and Advanced Settings; each lands on the page's first option with its mark, and none fires while a text box has the keyboard, with Ctrl or Alt held, or again while the key stays down");
        }

        private void CheckSectionKeys(List<string> checks)
        {
            more.Focus();
            string main = TabWalk(1, 12);
            string mainBack = TabWalk(-1, 12);
            more.Focus();
            Strike(Keys.Tab | Keys.Shift);
            string fromTitle = HeldSection();
            Strike(Keys.S);
            more.Focus();
            string page = TabWalk(1, 24);
            string pageBack = TabWalk(-1, 24);
            Hold(dance);
            Strike(Keys.Tab);
            string fromMiddle = HeldSection();
            bool first = HeldFirst();
            Strike(Keys.M);
            more.Focus();
            string modPage = TabWalk(1, 12);
            Strike(Keys.H);
            if (main != "BRAIN VOICE CAPRI" || mainBack != "CAPRI VOICE BRAIN" || fromTitle != "CAPRI" || page != "YOU VOICE MOVES KEYS GAMES CLIPS CAPRI WATCH BRAIN HABITS SOUNDS"
                || pageBack != "SOUNDS HABITS BRAIN WATCH CAPRI CLIPS GAMES KEYS MOVES VOICE YOU" || fromMiddle != "KEYS" || !first || modPage != "MODS INSTALLED COLORS")
                throw new Exception("Tab between her Mainframe's sections went wrong: main " + main + " / back " + mainBack + " / from the title " + fromTitle + " / Settings " + page + " / back " + pageBack + " / from Dance to music " + fromMiddle + ", first " + first + " / Mods " + modPage);
            checks.Add("PASS Mainframe Tab and Shift+Tab: Tab jumps to the first option of the next section and Shift+Tab to the one before, BRAIN, VOICE, CAPRI on her main page, YOU, VOICE, MOVES, KEYS and on through both columns of Settings, and MODS, INSTALLED, COLORS on Mods, wrapping at either end");
        }

        private void CheckArrowKeys(List<string> checks)
        {
            more.Focus();
            Strike(Keys.Tab);
            Strike(Keys.Tab);
            Control voiceFirst = Held();
            bool[] tiers = Array.ConvertAll(rungs, rung => rung.Checked);
            int voiceWas = voiceRow;
            Strike(Keys.Up);
            string before = HeldSection();
            Control brainLast = Held();
            Strike(Keys.Down);
            bool edge = before == "BRAIN" && Held() == voiceFirst && HeldMarked();
            Hold(rungs[0]);
            for (int i = 0; i < 12; i++) Strike(Keys.Down);
            bool rows = HeldSection() != "BRAIN";
            for (int i = 0; i < 12; i++) Strike(Keys.Up);
            bool unpicked = voiceRow == voiceWas;
            for (int i = 0; i < rungs.Length; i++) unpicked = unpicked && rungs[i].Checked == tiers[i];
            List<Stop> stops = Stops();
            Hold(stops[stops.Count - 1].Control);
            Strike(Keys.Down);
            bool wrapped = Held() == stops[0].Control && HeldSection() == "title";
            Strike(Keys.Up);
            bool wrappedBack = Held() == stops[stops.Count - 1].Control && HeldSection() == "CAPRI";
            Hold(showHide);
            Strike(Keys.Right);
            Control beside = Held();
            Strike(Keys.Left);
            bool along = beside != showHide && beside.Top == showHide.Top && Held() == showHide;
            Strike(Keys.Left);
            bool stays = Held() == showHide;
            if (brainLast == voiceFirst || !edge || !rows || !unpicked || !wrapped || !wrappedBack || !along || !stays)
                throw new Exception("Her Mainframe's arrow keys went wrong: section edge " + edge + ", through the rows " + rows + ", nothing picked " + unpicked + ", wrapped " + wrapped + ", wrapped back " + wrappedBack + ", along a row " + along + ", stays at the row's end " + stays);
            checks.Add("PASS Mainframe arrow keys: Up and Down move through a section's options with the mark on each, Up from VOICE's first goes to BRAIN's last and Down comes back, the BRAIN and VOICE rows are passed without being picked, the last option wraps to her title bar and back, and Left and Right move along a row of buttons");
        }

        private void CheckEnterKey(List<string> checks)
        {
            Strike(Keys.S);
            bool introWas = intro.Checked;
            Hold(intro);
            bool flipped = Strike(Keys.Enter) && intro.Checked != introWas && Strike(Keys.Enter, true) && intro.Checked != introWas;
            bool flippedBack = Strike(Keys.Enter) && intro.Checked == introWas;
            Key time = careTimes[0];
            int timeWas = capri.CareTime((CareKind)0);
            Hold(time);
            bool opened = Strike(Keys.Enter) && careMenu.Visible && Opened() == careMenu;
            Message heldEnter = Message.Create(careMenu.Handle, 0x0100, new IntPtr(0x0D), new IntPtr(0x40000000)), freshEnter = Message.Create(careMenu.Handle, 0x0100, new IntPtr(0x0D), IntPtr.Zero);
            bool heldOff = PreFilterMessage(ref heldEnter) && !HeldEnter(freshEnter) && careMenu.Visible;
            ToolStripItem start = Chosen(careMenu);
            bool anyPicked = false;
            foreach (ToolStripItem item in careMenu.Items) anyPicked = anyPicked || ((ToolStripMenuItem)item).Checked;
            bool onPicked = start != null && ((ToolStripMenuItem)start).Checked == anyPicked;
            bool swallowed = !Strike(Keys.Down) && !Strike(Keys.M) && Held() == time && paged;
            MenuKey(careMenu, Keys.Down);
            ToolStripItem below = Chosen(careMenu);
            MenuKey(careMenu, Keys.Up);
            bool picks = below != null && below != start && Chosen(careMenu) == start;
            MenuKey(careMenu, Keys.Escape);
            bool closed = !careMenu.Visible && paged && Held() == time;
            Strike(Keys.Enter);
            bool reopened = careMenu.Visible;
            Strike(Keys.Escape);
            bool closedHere = !careMenu.Visible && paged;
            Strike(Keys.Enter);
            int chose = 0;
            ToolStripItem again = Chosen(careMenu);
            if (again != null) again.Click += delegate { chose++; };
            MenuKey(careMenu, Keys.Enter);
            bool chosen = chose == 1 && !careMenu.Visible && capri.CareTime((CareKind)0) == timeWas;
            if (careMenu.Visible) careMenu.Close();
            if (!flipped || !flippedBack || !opened || !heldOff || !onPicked || !swallowed || !picks || !closed || !reopened || !closedHere || !chosen)
                throw new Exception("Enter in her Mainframe went wrong: toggle " + flipped + ", back " + flippedBack + ", dropdown opened " + opened + ", a held Enter kept off it " + heldOff + ", on the picked choice " + onPicked + ", page keys held off " + swallowed + ", Up and Down pick " + picks + ", Esc closed it " + closed + ", reopened " + reopened + ", Esc closed only it " + closedHere + ", Enter chose " + chosen);
            checks.Add("PASS Mainframe Enter: it turns a switch on and off once a press however long it's held, and opens a dropdown on its picked choice without the held key choosing it; in the open dropdown Up and Down pick, Enter chooses and Esc closes just the dropdown, leaving Settings open");
        }

        private void CheckSliderKeys(List<string> checks)
        {
            Strike(Keys.S);
            int was = bubble.Value;
            int toward = was + 1 < bubble.Choices.Length ? 1 : -1;
            Hold(bubble);
            bool left = !Strike(Keys.Left) && !Strike(Keys.Right) && Held() == bubble && bubble.Value == was && bubble.Marked;
            Press(bubble, toward);
            bool moved = bubble.Value == was + toward && capri.BubbleTime == was + toward && bubbleName.Text == BubbleNames[was + toward];
            Press(bubble, -toward);
            bool movedBack = bubble.Value == was && capri.BubbleTime == was;
            Strike(Keys.Down);
            bool leaves = Held() != bubble && HeldSection() == "BRAIN";
            if (!left || !moved || !movedBack || !leaves)
                throw new Exception("Her Mainframe's slider keys went wrong: left to the slider " + left + ", moved " + moved + ", moved back " + movedBack + ", Down leaves it " + leaves);
            checks.Add("PASS Mainframe slider keys: Left and Right are left to the slider with the keyboard, which moves a step each way and saves, and Up and Down still move on to the next option");
        }

        private void CheckEscKey(List<string> checks)
        {
            Strike(Keys.S);
            Hold(intro);
            SettingHint tip;
            if (hints.TryGetValue(intro, out tip)) { dismissedHint = null; QueueHint(tip, false); ShowHint(); }
            bool tipped = HintUp;
            bool tipFirst = !tipped || (Strike(Keys.Escape) && !HintUp && paged);
            Hold(careTimes[0]);
            Strike(Keys.Enter);
            bool dropdown = careMenu.Visible && Strike(Keys.Escape) && !careMenu.Visible && paged;
            bool settings = Strike(Keys.Escape) && !paged && !modded && WindowState == FormWindowState.Normal;
            Strike(Keys.M);
            bool mods = modded && Strike(Keys.Escape) && !modded && !paged && WindowState == FormWindowState.Normal;
            Strike(Keys.S);
            Hold(nickname);
            bool typing = Strike(Keys.Escape) && !paged;
            bool closes = Strike(Keys.Escape) && WindowState == FormWindowState.Minimized;
            Restore();
            if (!tipFirst || !dropdown || !settings || !mods || !typing || !closes)
                throw new Exception("Esc in her Mainframe went wrong: tip first " + tipFirst + ", dropdown " + dropdown + ", Settings " + settings + ", Mods " + mods + ", from a text box " + typing + ", closes " + closes);
            checks.Add("PASS Mainframe Esc: it goes back one step at a time, closing an open dropdown first, then leaving Settings or Mods for her main page, a text box included, and from her main page it closes the Mainframe");
            checks.Add(tipped ? "PASS Mainframe Esc and tips: with a tip showing, Esc puts the tip away and stays on the page" : "LIMIT Mainframe Esc and tips: no tip came up here to press Esc on");
        }

        private void CheckPickingKeys(List<string> checks)
        {
            string was = capri.FrameKeyName;
            Strike(Keys.S);
            Hold(frameKeys);
            bool started = Strike(Keys.Enter) && capturing == Companion.FrameSlot + 1;
            bool held = Strike(Keys.Enter, true) && pendingMods < 0;
            Strike(Keys.Control | Keys.Alt | Keys.O);
            bool pending = FrameKeyShown == "Mainframe: Ctrl + Alt + O" && capri.FrameKeyName == was;
            bool confirmed = Strike(Keys.Enter) && capturing == 0 && capri.FrameKeyName == "Ctrl + Alt + O" && paged;
            Strike(Keys.Enter);
            Strike(Keys.Control | Keys.Alt | Keys.I);
            bool cancelled = Strike(Keys.Escape) && capturing == 0 && capri.FrameKeyName == "Ctrl + Alt + O" && paged;
            Strike(Keys.Enter);
            Strike(Keys.Down);
            bool arrow = capturing != 0 && FrameKeyShown == "Mainframe: Down" && Strike(Keys.Enter) && capturing != 0 && capri.FrameKeyName == "Ctrl + Alt + O" && KeysNoteText == "Click Confirm to keep Down, or press Esc." && Strike(Keys.Escape) && capturing == 0;
            Strike(Keys.Enter);
            Strike(Keys.Enter);
            bool enterKey = capturing != 0 && FrameKeyShown == "Mainframe: Enter";
            bool mashed = Strike(Keys.Enter) && Strike(Keys.Enter) && capturing != 0 && capri.FrameKeyName == "Ctrl + Alt + O";
            bool dropped = Strike(Keys.Escape) && capturing == 0 && capri.FrameKeyName == "Ctrl + Alt + O" && paged;
            string back = capri.SetHotkey(Companion.FrameSlot, 3, 0x4D);
            Strike(Keys.H);
            if (!started || !held || !pending || !confirmed || !cancelled || !arrow || !enterKey || !mashed || !dropped || back != null || capri.FrameKeyName != was)
                throw new Exception("Picking her keys from the keyboard went wrong: started " + started + ", a held Enter ignored " + held + ", pending " + pending + ", Enter confirmed " + confirmed + ", Esc cancelled " + cancelled + ", an arrow needs a click " + arrow + ", Enter can be picked " + enterKey + ", more Enters don't save it " + mashed + ", Esc drops it " + dropped + ", back " + back);
            checks.Add("PASS Mainframe keys from the keyboard: Enter on a key in KEYS starts picking, the keys pressed show, Enter confirms them and Esc cancels, a held Enter isn't taken as the new key, and a key that types or moves, like Down or Enter on its own, can still be picked but only a click on Confirm keeps it");
        }
    }
}
