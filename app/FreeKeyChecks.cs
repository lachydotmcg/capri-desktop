using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        internal void CheckFreeKeys(List<string> checks, string output, Action<int> picked)
        {
            bool wasSettings = paged, wasAdvanced = capri.AdvancedSettings, wasShown = Visible, wasMenu = freeKeyMenu.Visible;
            bool wasFallback = layoutFallback;
            int wasSize = capri.MainframeSize, wasDpi = layoutDpi, wasText = layoutText, wasScroll = pageScroll, wasChoice = -1;
            // A minimized Mainframe's Location is -32000, so its normal place is kept instead and put back before it minimizes again.
            Point wasAt = WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location;
            Control wasFocus = ActiveControl;
            FormWindowState wasState = WindowState;
            for (int i = 0; i < freeKeyMenu.Items.Count; i++) if (freeKeyMenu.Items[i].Selected) wasChoice = i;
            try
            {
                Restore();
                freeKeyMenu.Close();
                Directory.CreateDirectory(output);
                foreach (bool advanced in new[] { false, true })
                {
                    if (capri.AdvancedSettings != advanced) capri.ToggleAdvancedSettings();
                    foreach (int percent in new[] { 100, 150, 200 })
                    {
                        LayoutScale(96, 100);
                        SizeMainframe(percent);
                        ShowSettings(true);
                        ShowSettings(false);
                        Location = new Point(Screen.FromControl(this).WorkingArea.Left + 16, Screen.FromControl(this).WorkingArea.Top + 16);
                        string crowded = TextFits() ?? Overlap();
                        if (!TiersFit || crowded != null) throw new Exception("Free key's BRAIN panel does not fit at " + percent + "%: " + crowded);
                        if (!freeKey.TabStop || !freeKey.Focus()) throw new Exception("The Free key button cannot be reached by keyboard.");
                        if (hints[freeKey].Topic != SelfKnow.Setting("Mainframe.cs", "Free key")) throw new Exception("Free key lost its real help binding.");
                        foreach (string provider in new[] { "NVIDIA", "Google AI Studio", "OpenRouter" })
                            if (!freeKey.AccessibleDescription.Contains(provider)) throw new Exception("Free key's tip missed " + provider + ".");
                        freeKey.PerformClick();
                        CheckFreeKeyRows();
                        if (!advanced && percent != 200) SaveFreeKeyPreview(output, percent);
                        FreeKeyPress(freeKeyMenu, Keys.Escape);
                        if (freeKeyMenu.Visible) throw new Exception("Esc did not close the Free key menu.");
                        foreach (Keys opening in new[] { Keys.Enter, Keys.Space })
                            for (int provider = 0; provider < freeKeyMenu.Items.Count; provider++)
                            {
                                freeKey.Focus();
                                FreeKeyPress(freeKey, opening);
                                CheckFreeKeyRows();
                                if (!freeKeyMenu.Items[0].Selected) throw new Exception("The Free key menu did not start on NVIDIA.");
                                FreeKeyPress(freeKeyMenu, Keys.Down);
                                if (!freeKeyMenu.Items[1].Selected) throw new Exception("Down did not move to Google AI Studio.");
                                FreeKeyPress(freeKeyMenu, Keys.Up);
                                if (!freeKeyMenu.Items[0].Selected) throw new Exception("Up did not move back to NVIDIA.");
                                for (int step = 0; step < provider; step++) FreeKeyPress(freeKeyMenu, Keys.Down);
                                if (!freeKeyMenu.Items[provider].Selected) throw new Exception("Arrows did not reach free key provider " + provider + ".");
                                FreeKeyPress(freeKeyMenu, Keys.Enter);
                                if (freeKeyMenu.Visible) throw new Exception("Enter did not close the Free key menu after picking.");
                                picked(provider);
                            }
                    }
                }
                checks.Add("PASS Free key menu: three accessible provider rows share the Mainframe menu style and fit with their notes at 100%, 150% and 200%, after Simple and Advanced Settings; 100% and 150% BRAIN previews saved");
                checks.Add("PASS Free key keyboard: Enter and Space open, Down and Up move between providers, Enter picks each row and Esc closes without opening a page");
            }
            finally
            {
                freeKeyMenu.Close();
                DismissHint();
                overHint = dismissedHint = null;
                if (capri.AdvancedSettings != wasAdvanced) capri.ToggleAdvancedSettings();
                LayoutScale(wasDpi, wasText, wasFallback);
                SizeMainframe(wasSize);
                ShowSettings(wasSettings);
                ScrollPage(wasScroll);
                Location = wasAt;
                if (wasFocus != null && wasFocus.CanFocus) wasFocus.Focus();
                DismissHint();
                WindowState = wasState;
                if (!wasShown) Hide();
                if (wasMenu)
                {
                    ShowFreeKeys();
                    if (wasChoice >= 0) freeKeyMenu.Items[wasChoice].Select();
                }
            }
        }

        private void CheckFreeKeyRows()
        {
            string[] providers = { "NVIDIA", "Google AI Studio (Gemini)", "OpenRouter" };
            string[] notes = { "free · often slow at busy times", "free tier · rate limited", "free models only" };
            if (!freeKeyMenu.Visible || freeKeyMenu.Items.Count != providers.Length || !(freeKeyMenu.Renderer is MenuLook) || !freeKeyMenu.Font.Equals(Font))
                throw new Exception("Free key did not open three rows in the Mainframe's menu style and scaled font.");
            if (!Screen.FromControl(this).WorkingArea.Contains(freeKeyMenu.Bounds)) throw new Exception("The Free key menu left the screen.");
            for (int i = 0; i < providers.Length; i++)
            {
                ToolStripItem row = freeKeyMenu.Items[i];
                Size needed = row.GetPreferredSize(Size.Empty);
                if (row.AccessibleName != providers[i] || row.AccessibilityObject.Name != providers[i] || row.Text != providers[i] + " · " + notes[i])
                    throw new Exception("Free key row lost its provider name or note: " + row.Text);
                if (!row.Visible || !row.Enabled || !freeKeyMenu.ClientRectangle.Contains(row.Bounds) || row.Width < needed.Width || row.Height < needed.Height)
                    throw new Exception("A Free key row is clipped: " + row.Text);
            }
        }

        private static void FreeKeyPress(Control control, Keys key)
        {
            foreach (int message in new[] { 0x0100, 0x0101 })
            {
                Message press = Message.Create(control.Handle, message, new IntPtr((int)key), IntPtr.Zero);
                if (!control.PreProcessMessage(ref press)) Native.SendMessage(control.Handle, message, press.WParam, press.LParam);
            }
            Application.DoEvents();
        }

        // BRAIN's OFF row, No AI: BRAIN with OFF picked and not at 100%, 125% and 150%, everything in it fitting, and BRAIN with
        // the row against BRAIN without it, where only what's under the row moves, by exactly one row, and the page grows by one.
        internal void CheckNoAiRow(List<string> checks, string output)
        {
            bool wasSettings = paged, wasShown = Visible, wasNoAi = noAi;
            int wasSize = capri.MainframeSize, wasScroll = pageScroll;
            Point wasAt = WindowState == FormWindowState.Normal ? Location : RestoreBounds.Location;
            FormWindowState wasState = WindowState;
            string wasName = brainName.Text, wasNote = brainNote.Text, wasTierNote = tierNote.Text;
            Color wasInk = brainNote.ForeColor;
            List<string> moves = new List<string>();
            try
            {
                Restore();
                ShowSettings(false);
                Directory.CreateDirectory(output);
                foreach (int percent in new[] { 100, 125, 150 })
                {
                    SizeMainframe(percent);
                    ShowNoAi(false);
                    NoAiRowShown = false;
                    Arrange(expanded);
                    Dictionary<Control, Rectangle> without = new Dictionary<Control, Rectangle>();
                    foreach (Control control in Controls) without[control] = control.Bounds;
                    int tallWithout = ClientSize.Height, rowAt = offRung.Top;
                    SaveBrainPicture(output, "brain-without-off-row-" + percent);
                    NoAiRowShown = true;
                    Arrange(expanded);
                    int row = Px(24);
                    foreach (Control control in Controls)
                    {
                        Rectangle was;
                        // Only what shows on BRAIN's page counts: Settings' rows wait off to the side, and the setup steps are folded away.
                        if (control == offRung || !control.Visible || control.Left >= ClientSize.Width || !without.TryGetValue(control, out was)) continue;
                        int moved = control.Top - was.Top, should = was.Top >= rowAt ? row : 0;
                        if (control.Left != was.Left || control.Size != was.Size || Math.Abs(moved - should) > 1)
                            moves.Add(percent + "%: " + (control.AccessibleName ?? control.Text) + " moved " + moved + " (" + was + " -> " + control.Bounds + ")");
                    }
                    if (Math.Abs(ClientSize.Height - tallWithout - row) > 1) moves.Add(percent + "%: the page grew " + (ClientSize.Height - tallWithout) + ", not one row (" + row + ")");
                    foreach (bool on in new[] { false, true })
                    {
                        ShowNoAi(on);
                        // Off, BRAIN shows the brain it had again, as her own ShowTiers does.
                        if (!on) { brainName.Text = wasName; brainNote.Text = wasNote; brainNote.ForeColor = wasInk; tierNote.Text = wasTierNote; }
                        string problem = TiersProblem ?? TextFits() ?? Overlap();
                        if (problem != null) throw new Exception("BRAIN with OFF " + (on ? "picked" : "not picked") + " doesn't fit at " + percent + "%: " + problem);
                        if (NoAiShown != on) throw new Exception("BRAIN's OFF row didn't show No AI " + (on ? "on" : "off") + " at " + percent + "%.");
                        SaveBrainPicture(output, "brain-off-" + (on ? "picked" : "not-picked") + "-" + percent);
                    }
                    ShowNoAi(false);
                }
                if (moves.Count > 0) throw new Exception("BRAIN's OFF row moved more than the rows under it: " + string.Join("; ", moves.ToArray()));
                if (hints[offRung].Topic != SelfKnow.Setting("Mainframe.cs", "No AI")) throw new Exception("BRAIN's OFF row has no help topic.");
                checks.Add("PASS BRAIN's OFF row (No AI) fits picked and not at 100%, 125% and 150% with its help topic, and with it only what's under it moves, by exactly one row, with the page one row longer");
            }
            finally
            {
                NoAiRowShown = true;
                ShowNoAi(wasNoAi);
                brainName.Text = wasName;
                brainNote.Text = wasNote;
                brainNote.ForeColor = wasInk;
                tierNote.Text = wasTierNote;
                SizeMainframe(wasSize);
                ShowSettings(wasSettings);
                Arrange(expanded);
                ScrollPage(wasScroll);
                Location = wasAt;
                WindowState = wasState;
                if (!wasShown) Hide();
            }
        }

        private void SaveBrainPicture(string output, string name)
        {
            Refresh();
            Rectangle brain = new Rectangle(0, 0, Width, Math.Min(Height, voiceHead.Top + Px(10)));
            using (Bitmap panel = new Bitmap(Width, Height))
            using (Bitmap picture = new Bitmap(brain.Width, brain.Height))
            using (Graphics drawing = Graphics.FromImage(picture))
            {
                DrawToBitmap(panel, new Rectangle(Point.Empty, Size));
                drawing.DrawImage(panel, new Rectangle(Point.Empty, brain.Size), brain, GraphicsUnit.Pixel);
                picture.Save(Path.Combine(output, name + ".png"), ImageFormat.Png);
            }
        }

        private void SaveFreeKeyPreview(string output, int percent)
        {
            Rectangle brain = new Rectangle(Location, new Size(Width, voiceHead.Top));
            Rectangle whole = Rectangle.Union(brain, freeKeyMenu.Bounds);
            whole.Inflate(Px(8), Px(8));
            using (Bitmap picture = new Bitmap(whole.Width, whole.Height))
            using (Bitmap panel = new Bitmap(Width, Height))
            using (Bitmap menu = new Bitmap(freeKeyMenu.Width, freeKeyMenu.Height))
            using (Graphics drawing = Graphics.FromImage(picture))
            {
                DrawToBitmap(panel, new Rectangle(Point.Empty, Size));
                DrawRims(panel);
                freeKeyMenu.DrawToBitmap(menu, new Rectangle(Point.Empty, freeKeyMenu.Size));
                drawing.Clear(Ground);
                drawing.DrawImage(panel, new Rectangle(brain.Left - whole.Left, brain.Top - whole.Top, brain.Width, brain.Height), new Rectangle(Point.Empty, brain.Size), GraphicsUnit.Pixel);
                drawing.DrawImageUnscaled(menu, freeKeyMenu.Left - whole.Left, freeKeyMenu.Top - whole.Top);
                picture.Save(Path.Combine(output, "brain-free-keys-" + percent + ".png"), ImageFormat.Png);
            }
        }
    }

    internal partial class Companion
    {
        private void SmokeFreeKeys(List<string> checks, string output)
        {
            Action<string> wasBrowse = Browse;
            bool wasSilent = preferences.Silent, wasHidden = hidden;
            string wasHeard = heard, before = SmokeState();
            List<string> browsed = new List<string>();
            string[] pages = { FreeKeyPage, GeminiKeyPage, RouterKeyPage };
            string[] lines = { FreeKeyLine, GeminiKeyLine, RouterKeyLine };
            int picked = 0;
            try
            {
                Browse = browsed.Add;
                preferences.Silent = true;
                mainframe.ShowTiers(1, null, null);
                mainframe.CheckFreeKeys(checks, output, delegate(int provider)
                {
                    if (browsed.Count != ++picked || browsed[picked - 1] != pages[provider]) throw new Exception("Free key opened the wrong page for provider " + provider + ".");
                    if (reply != lines[provider] || heard != "free key") throw new Exception("Free key did not say its line for provider " + provider + ": " + reply);
                    CloseTalk();
                });
                if (picked != 36 || browsed.Count != picked) throw new Exception("Free key did not check every provider with both opening keys at every size and Settings mode.");
                OpenPage("https://example.invalid/free-key");
                if (browsed.Count != picked) throw new Exception("OpenPage allowed an unrecognised key page.");
                checks.Add("PASS Free key pages: NVIDIA, Google AI Studio and OpenRouter each use OpenPage with their own URL and say their own line; browser opening is stubbed and unrecognised pages stay blocked");
                mainframe.CheckNoAiRow(checks, output);
            }
            finally
            {
                ++asking;
                CloseTalk();
                Browse = wasBrowse;
                preferences.Silent = wasSilent;
                heard = wasHeard;
                if (hidden != wasHidden) SetHidden(wasHidden);
                ShowTiers();
                SavePreferences();
            }
            if (SmokeState() != before) throw new Exception("Free key checks did not restore her Mainframe, menus or Always on top state.");
            checks.Add("PASS Free key cleanup: her Mainframe visibility and window state, Settings mode, scale, menus, browser hook and Always on top are restored");
        }
    }
}
