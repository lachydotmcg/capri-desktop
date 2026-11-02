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
