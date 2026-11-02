using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        internal void SmokeTips(List<string> checks, string output)
        {
            bool wasSettings = paged, wasAdvanced = capri.AdvancedSettings;
            int wasSize = capri.MainframeSize;
            Point wasAt = Location;
            Control wasFocus = ActiveControl;
            // Restore() brings the Mainframe up, so it goes back down after: a minimized start stays minimized.
            bool wasShown = Visible;
            FormWindowState wasState = WindowState;
            try
            {
                tipsDriven = true;
                Restore();
                LayoutScale(96, 100);
                SizeMainframe(100);
                int covered = 0;
                foreach (bool advanced in new[] { false, true })
                {
                    if (capri.AdvancedSettings != advanced) capri.ToggleAdvancedSettings();
                    foreach (bool settings in new[] { false, true })
                    {
                        ShowSettings(settings);
                        foreach (Control control in Controls)
                        {
                            bool setting = control is Key || control is Toggle || control is Slider || control is Rung || control is Entry;
                            if (!setting) continue;
                            SettingHint row;
                            if (!hints.TryGetValue(control, out row)) throw new Exception("Mainframe tip missing: " + (control.AccessibleName ?? control.Text));
                            if (row.Topic.Facts.Trim().Length < 25 || SelfKnow.Setting(row.Topic.File, row.Topic.Label) != row.Topic)
                                throw new Exception("Mainframe tip has no real SelfKnow topic: " + row.Title);
                            CheckHintText(row);
                            if (control.Visible && !Hidden(control)) covered++;
                            foreach (Control child in control.Controls)
                                if (child.AccessibleDescription != row.Text) throw new Exception("A setting's child has different accessible help: " + row.Title);
                        }
                    }
                }
                checks.Add("PASS Mainframe tips: every switch, slider, picker, entry and button on the main page and in Simple and Advanced Settings has real SelfKnow text, including hidden variants and input children (" + covered + " shown controls checked)");

                int oldVolume = volume.Value;
                string volumeTip = volume.AccessibleDescription;
                try
                {
                    volume.Slide((oldVolume + 1) % volume.Choices.Length);
                    if (volume.AccessibleDescription != volumeTip || volume.AccessibilityObject.Value != volume.Shown)
                        throw new Exception("A slider value replaced its help or was not exposed as an accessible value.");
                }
                finally { volume.Slide(oldVolume); }
                if (layersKey.AccessibilityObject.Value != (layersKey.Picked ? "On" : "Off") || layersKey.AccessibleDescription != hints[layersKey].Text)
                    throw new Exception("The Layers button lost its current accessible state or its help.");
                foreach (Control binding in new Control[] { talkKeys, muteKeys, typeKeys, hideKeys, pushKeys, gameKeys, quietKeys })
                    if (hints[binding].Title.IndexOf(':') >= 0) throw new Exception("A key tip cached a changeable binding as its title.");
                checks.Add("PASS Mainframe tip state: changing a slider preserves its help and exposes its current accessible value; key-tip headings use stable setting names");

                ShowSettings(true);
                ScrollPage(0);
                more.Focus();
                Control focus = ActiveControl;
                IntPtr foreground = Native.GetForegroundWindow();
                DismissHint();
                dismissedHint = null;
                overHint = null;
                Stopwatch delay = Stopwatch.StartNew();
                MoveOverHint(checkIns);
                PumpTips(250);
                if (HintVisible) throw new Exception("A Mainframe tip appeared before the hover delay.");
                AwaitHint();
                if (delay.ElapsedMilliseconds < 350 || delay.ElapsedMilliseconds > 1200) throw new Exception("Mainframe tip hover delay was " + delay.ElapsedMilliseconds + " ms.");
                if (ActiveControl != focus || Native.GetForegroundWindow() != foreground || hintCard.ContainsFocus)
                    throw new Exception("A Mainframe tip took keyboard focus.");
                CheckHintPlace(checkIns);
                TipMessage(checkIns, 0x02A3, 0);
                if (HintVisible) throw new Exception("A Mainframe tip stayed after mouse leave.");
                PumpTips(450);
                if (HintVisible) throw new Exception("A Mainframe tip reopened after mouse leave.");
                checks.Add("PASS Mainframe tip hover: waits about 400 ms, sits beside its control inside the monitor's working area, leaves foreground and keyboard focus alone, and hides on leave");

                SettingHint named = hints[volume];
                Point labelAt = named.Label.PointToScreen(new Point(named.Label.Width / 2, named.Label.Height / 2));
                Message labelMove = Message.Create(Handle, 0x00A0, new IntPtr(2), new IntPtr((labelAt.Y << 16) | (labelAt.X & 0xFFFF)));
                tipSent = true;
                try { PreFilterMessage(ref labelMove); }
                finally { tipSent = false; }
                if (waitingHint != named || !hintDelay.Enabled) throw new Exception("A transparent setting name did not queue its slider's delayed tip.");
                ShowHint();
                CheckHintPlace(volume);
                TipMessage(this, 0x02A2, 0);
                if (HintVisible) throw new Exception("A setting-name tip stayed after non-client mouse leave.");
                MoveOverHint(checkIns);
                TipMessage(this, 0x02A2, 0);
                TipMessage(volume, 0x02A3, 0);
                if (waitingHint != hints[checkIns]) throw new Exception("An old mouse-leave message cancelled the next control's tip.");
                DismissHint();
                checks.Add("PASS Mainframe setting-name tips: transparent row labels resolve non-client screen coordinates to the slider's delayed tip, share its text beside the whole row, and hide on non-client leave");

                foreach (int message in new[] { 0x0100, 0x0201, 0x00A7, 0x020A, 0x020E })
                {
                    dismissedHint = null;
                    overHint = null;
                    MoveOverHint(checkIns);
                    AwaitHint();
                    TipMessage(checkIns, message, message == 0x0100 ? (int)Keys.Escape : 0);
                    if (HintVisible) throw new Exception("A Mainframe tip stayed after Esc, click or scroll.");
                    MoveOverHint(checkIns);
                    PumpTips(450);
                    if (HintVisible) throw new Exception("A dismissed Mainframe tip reopened without leaving its control.");
                }
                dismissedHint = null;
                overHint = null;
                MoveOverHint(checkIns);
                TipMessage(checkIns, 0x0100, (int)Keys.Escape);
                PumpTips(450);
                if (HintVisible) throw new Exception("Esc left a pending Mainframe tip open.");
                checks.Add("PASS Mainframe tip dismissal: Esc, clicking and both scroll directions cancel a shown or pending tip and keep it dismissed until the pointer leaves");

                more.Focus();
                dismissedHint = null;
                if (!languages.Focus() || !languages.Focused) checks.Add("LIMIT Mainframe tip keyboard: Windows did not give the picker keyboard focus");
                else
                {
                    PumpTips(250);
                    if (HintVisible) throw new Exception("A keyboard tip skipped its delay.");
                    AwaitHint();
                    if (!languages.Focused || hintCard.ContainsFocus || hintCard.Body != languages.AccessibleDescription) throw new Exception("A keyboard tip changed focus or screen-reader text.");
                    CheckHintPlace(languages);
                    more.Focus();
                    if (HintVisible) throw new Exception("A keyboard tip stayed after focus left.");
                    if (!clipsFolder.Focus() || !clipsFolder.Focused) throw new Exception("Keyboard focus could not reach the lower Settings controls.");
                    AwaitHint();
                    CheckHintPlace(clipsFolder);
                    if (!ClientRectangle.Contains(clipsFolder.Bounds)) throw new Exception("A keyboard tip did not follow the scrolled setting into view.");
                    nickname.Box.Focus();
                    AwaitHint();
                    CheckHintPlace(nickname);
                    if (!nickname.Box.Focused || nickname.Box.AccessibleDescription != hintCard.Body) throw new Exception("A text entry's focused child lost its help or focus.");
                    more.Focus();
                    DismissHint();
                    checks.Add("PASS Mainframe tip keyboard: focusing a picker shows the same delayed text as hover and AccessibleDescription, focus stays on the picker, and leaving hides it");
                    checks.Add("PASS Mainframe tip keyboard scrolling: a lower button scrolls into view before its tip opens, and a focused text-box child shows its entry's same help without taking focus");
                }

                Directory.CreateDirectory(output);
                foreach (int percent in new[] { 100, 150, 200 })
                {
                    LayoutScale(96, 100);
                    SizeMainframe(percent);
                    ShowSettings(true);
                    ScrollPage(0);
                    foreach (Control control in new Control[] { checkIns, volume, languages })
                    {
                        DismissHint();
                        dismissedHint = null;
                        overHint = null;
                        MoveOverHint(control);
                        AwaitHint();
                        CheckHintPlace(control);
                        if (hintCard.Width > Px(360)) throw new Exception("A tip is wider than 360 scaled pixels.");
                        if (percent != 200) SaveTipPreview(output, (control == checkIns ? "switch" : control == volume ? "slider" : "picker") + "-" + percent + ".png");
                    }
                }
                foreach (int dpi in new[] { 96, 144, 192 })
                    foreach (int percent in new[] { 100, 150, 200 })
                    {
                        LayoutScale(dpi, 100);
                        SizeMainframe(percent);
                        foreach (SettingHint row in hintRows) CheckHintText(row);
                        foreach (Rectangle area in new[] { new Rectangle(0, 0, 1920, 1080), new Rectangle(-1280, 80, 1280, 944), new Rectangle(0, 0, 800, 600) })
                            foreach (Point corner in new[] { new Point(0, 0), new Point(1, 0), new Point(0, 1), new Point(1, 1) })
                            {
                                int width = Math.Min(Px(328), area.Width - 20), height = Math.Min(Px(22), area.Height / 4);
                                Rectangle anchor = new Rectangle(corner.X == 0 ? area.Left + 10 : area.Right - width - 10, corner.Y == 0 ? area.Top + 10 : area.Bottom - height - 10, width, height);
                                foreach (Control control in new Control[] { checkIns, volume, languages, gamingMode })
                                {
                                    SettingHint row = hints[control];
                                    if (!hintCard.Place(row.Title, row.Topic.Facts, Unit(), anchor, area) || !area.Contains(hintCard.Bounds) || anchor.IntersectsWith(hintCard.Bounds))
                                        throw new Exception("A tip left or covered a control at " + dpi + " DPI, " + percent + "% Mainframe size, " + area + ".");
                                    if (hintCard.Width > Px(360)) throw new Exception("A DPI-scaled tip exceeded its width limit.");
                                }
                            }
                    }
                checks.Add("PASS Mainframe tip scaling: switch, slider and picker fit at 100%, 150% and 200%; all DPI/size combinations and four screen corners fit, including a negative-coordinate monitor and a small working area; six switch/slider/picker previews saved at 100% and 150%");
                checks.Add("LIMIT Mainframe tips: DPI and monitor geometry checks are simulated; physical mixed-DPI monitor moves and a spoken screen-reader session need manual review");
            }
            finally
            {
                tipsDriven = false;
                DismissHint();
                overHint = dismissedHint = null;
                if (capri.AdvancedSettings != wasAdvanced) capri.ToggleAdvancedSettings();
                LayoutScale(0, 0);
                SizeMainframe(wasSize);
                ShowSettings(wasSettings);
                Location = wasAt;
                if (wasFocus != null && wasFocus.CanFocus) wasFocus.Focus();
                DismissHint();
                if (!wasShown) Hide();
                else if (WindowState != wasState) WindowState = wasState;
            }
        }

        private bool HintVisible { get { return hintCard != null && hintCard.Visible; } }

        private void CheckHintText(SettingHint hint)
        {
            string prefix = hint.Text;
            if (prefix.EndsWith(" Ask Capri for more", StringComparison.Ordinal)) prefix = prefix.Substring(0, prefix.Length - " Ask Capri for more".Length).TrimEnd('.');
            if (prefix.Length == 0 || !HintCard.Unpathed(hint.Topic.Facts).StartsWith(prefix, StringComparison.Ordinal) || hint.Control.AccessibleDescription != hint.Text)
                throw new Exception("A tip differs from SelfKnow or AccessibleDescription: " + hint.Title);
            using (Font ink = HintCard.Ink(Unit()))
                if (TextRenderer.MeasureText(hint.Text, ink, new Size(Math.Max(1, Px(332)), int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl).Height > ink.Height * 6)
                    throw new Exception("A tip exceeds six lines: " + hint.Title);
        }

        private void CheckHintPlace(Control control)
        {
            SettingHint row = hints[control];
            if (!HintVisible || !Screen.FromRectangle(row.Bounds).WorkingArea.Contains(hintCard.Bounds) || hintCard.Bounds.IntersectsWith(row.Bounds))
                throw new Exception("A Mainframe tip covers its control or leaves the screen: " + row.Title);
            if (row.Text != hintCard.Body || control.AccessibleDescription != hintCard.Body) throw new Exception("A shown tip differs from its accessible text.");
        }

        private void MoveOverHint(Control control)
        {
            Point point = new Point(control.Width / 2, control.Height / 2);
            Message move = Message.Create(control.Handle, 0x0200, IntPtr.Zero, new IntPtr((point.Y << 16) | (point.X & 0xFFFF)));
            tipSent = true;
            try { PreFilterMessage(ref move); }
            finally { tipSent = false; }
        }

        private void TipMessage(Control control, int message, int key)
        {
            Message press = Message.Create(control.Handle, message, new IntPtr(key), IntPtr.Zero);
            tipSent = true;
            try { PreFilterMessage(ref press); }
            finally { tipSent = false; }
        }

        private void AwaitHint()
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (!HintVisible && clock.ElapsedMilliseconds < 1200) { Application.DoEvents(); Thread.Sleep(10); }
            if (!HintVisible) throw new Exception("A Mainframe tip never appeared after its delay.");
        }

        private static void PumpTips(int milliseconds)
        {
            Stopwatch clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < milliseconds) { Application.DoEvents(); Thread.Sleep(10); }
        }

        private void SaveTipPreview(string output, string name)
        {
            Rectangle whole = Rectangle.Union(Bounds, hintCard.Bounds);
            whole.Inflate(Px(12), Px(12));
            using (Bitmap image = new Bitmap(whole.Width, whole.Height))
            using (Bitmap panel = new Bitmap(Width, Height))
            using (Bitmap card = new Bitmap(hintCard.Width, hintCard.Height))
            using (Graphics drawing = Graphics.FromImage(image))
            {
                drawing.Clear(Color.FromArgb(24, 24, 30));
                DrawToBitmap(panel, new Rectangle(Point.Empty, Size));
                DrawRims(panel);
                hintCard.DrawToBitmap(card, new Rectangle(Point.Empty, hintCard.Size));
                drawing.DrawImageUnscaled(panel, Left - whole.Left, Top - whole.Top);
                drawing.DrawImageUnscaled(card, hintCard.Left - whole.Left, hintCard.Top - whole.Top);
                image.Save(Path.Combine(output, name), ImageFormat.Png);
            }
        }
    }
}
