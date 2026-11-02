using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed partial class Mainframe
    {
        private readonly Dictionary<Control, SettingHint> hints = new Dictionary<Control, SettingHint>();
        private readonly List<SettingHint> hintRows = new List<SettingHint>();
        private readonly Timer hintDelay = new Timer { Interval = 400 };
        private HintCard hintCard;
        private SettingHint waitingHint, overHint, dismissedHint;
        private bool mouseHint, nonClientHint;
        // While the tips smoke runs, only the pointer moves it sends count: a real pointer resting over the Mainframe would open the
        // tip under it instead.
        private bool tipsDriven, tipSent;
        private IntPtr hintMouseWindow;

        [StructLayout(LayoutKind.Sequential)]
        private struct HintMouseTrack
        {
            internal int Size, Flags;
            internal IntPtr Window;
            internal int Time;
        }

        [DllImport("user32.dll")]
        private static extern bool TrackMouseEvent(ref HintMouseTrack track);

        private sealed class SettingHint
        {
            internal Control Control, Label;
            internal SelfKnow.Topic Topic;
            internal string Title, Text;

            internal Rectangle Bounds
            {
                get
                {
                    Rectangle bounds = Control.RectangleToScreen(Control.ClientRectangle);
                    return Label != null && Label.Visible ? Rectangle.Union(bounds, Label.RectangleToScreen(Label.ClientRectangle)) : bounds;
                }
            }
        }

        private void Explain(Control control, string label, string file = "Mainframe.cs", Control name = null, string title = null)
        {
            SelfKnow.Topic topic = SelfKnow.Setting(file, label);
            if (topic == null) return;
            SettingHint hint;
            if (!hints.TryGetValue(control, out hint))
            {
                hint = new SettingHint { Control = control, Label = name };
                hintRows.Add(hint);
                WatchHint(control, hint);
            }
            if (name != null) { hint.Label = name; hints[name] = hint; }
            hint.Topic = topic;
            hint.Title = title ?? (control.Text.Length > 0 ? control.Text : topic.Name);
            using (Font ink = HintCard.Ink(Unit()))
                hint.Text = HintCard.Brief(topic.Facts, ink, Math.Max(1, Scaled(332)), 6);
            DescribeHint(hint);
        }

        private void WatchHint(Control control, SettingHint hint)
        {
            hints[control] = hint;
            control.GotFocus += delegate
            {
                if (Control.MouseButtons != MouseButtons.None) return;
                dismissedHint = null;
                QueueHint(hint, false);
            };
            control.Leave += delegate { if (waitingHint == hint && !mouseHint) DismissHint(); };
            control.Click += delegate { dismissedHint = hint; DismissHint(); };
            control.VisibleChanged += delegate { if (!control.Visible && waitingHint == hint) DismissHint(); };
            control.LocationChanged += delegate { if (waitingHint == hint) DismissHint(); };
            foreach (Control child in control.Controls) WatchHint(child, hint);
        }

        private void PrepareHints()
        {
            Explain(minimize, "Minimize");
            Explain(close, "Close");
            Explain(more, "Settings", "Mainframe.cs", null, "Settings / Back");
            Explain(advancedKey, "Advanced Settings", "Mainframe.cs", null, "Simple / Advanced Settings");
            Explain(models, "Brain model", "Mainframe.cs", null, "Brain model");
            Explain(refresh, "Refresh");
            Explain(freeKey, "Free key", "Mainframe.cs", null, "Free key providers");
            Explain(setup, "Set up her brain", "Mainframe.cs", hint, "Set up her brain");
            for (int i = 0; i < rungs.Length; i++) Explain(rungs[i], i == 2 ? "MAX INTELLIGENCE" : i == 1 ? "MEDIUM INTELLIGENCE" : "NORMAL");
            Explain(textEntry, "NVIDIA, Google AI Studio (Gemini) or OpenRouter API key", "Mainframe.cs", null, "Brain API key");
            Explain(textConnect, "NVIDIA, Google AI Studio (Gemini) or OpenRouter API key", "Mainframe.cs", null, "Connect / Forget brain key");
            Explain(movePaid, "Move key to MAX INTELLIGENCE");
            for (int i = 0; i < voiceRungs.Length; i++) Explain(voiceRungs[i], i == 0 ? "OFF" : i == 1 ? "PREBUILT LOCAL" : i == 2 ? "CUSTOM LOCAL" : "Cloud voice service");
            Explain(setUpModel, "CUSTOM LOCAL", "Mainframe.cs", null, "Set up your voice model");
            Explain(customVoice, "CUSTOM LOCAL", "Mainframe.cs", null, "Your voice model");
            Explain(keyHelp, "Setup guide");
            Explain(cloudPick, "Cloud voice service", "Mainframe.cs", null, "Cloud voice service");
            Explain(cloudVoice, "Cloud voice service", "Mainframe.cs", null, "Cloud voice setup");
            Explain(keyEntry, "ElevenLabs API key", "Mainframe.cs", null, "ElevenLabs API key");
            Explain(connect, "ElevenLabs API key", "Mainframe.cs", null, "Connect / Forget voice key");
            Explain(voiceEntry, "ElevenLabs voice ID", "Mainframe.cs", null, "Voice ID");
            Explain(speechModels, "Default model", "Mainframe.cs", null, "Speech model");
            Explain(freeVoices, "Free voice: Her own", "Mainframe.cs", null, "Free voice style");
            Explain(freeSetup, "Get free voice");
            Explain(mics, "Mic: default", "Mainframe.cs", null, "Microphone");
            Explain(shortcut, "KEYS >", "Mainframe.cs", null, "Talk key");
            Explain(muteShortcut, "Mic: default", "Mainframe.cs", null, "Mute mic key");
            Explain(voiceNote, "Mic: default", "Mainframe.cs", null, "Voice status");
            foreach (Key fix in hearingFixes) Explain(fix, "Hearing status", "Mainframe.cs", null, "Hearing fix");
            Explain(hearingChoice, "Hearing engine", "Mainframe.cs", null, "Hearing");
            Explain(showHide, "Hide Capri", "Mainframe.cs", null, "Hide / Show Capri");
            Explain(perch, "Ride cursor", "Mainframe.cs", null, "Ride cursor / Hop off");
            foreach (Control control in Controls)
            {
                if (control is Key && control.Text == "Open her memories") Explain(control, "Remember our chats");
            }
            Explain(languages, "Language", "Mainframe.cs", FindHintLabel("Language"), "Language");
            Explain(nickname, "What should she call you?", "Mainframe.cs", FindHintLabel("What should she call you?"), "What should she call you?");
            Explain(layersKey, "Voice layers", "Mainframe.cs", null, "Layers");
            layersKey.StateValue = true;
            Explain(personalityKey, "Edit personality.md", "Mainframe.cs", null, "Her personality");
            Control[] bindings = { talkKeys, muteKeys, typeKeys, hideKeys, pushKeys, gameKeys, confirmKeys, cancelKeys, clearKeys };
            string[] names = { "Talk key", "Mute mic key", "Type key", "Hide / Show key", "Push to talk key", "Gaming Mode key", "Confirm keys", "Cancel keys", "Turn push to talk off" };
            for (int i = 0; i < bindings.Length; i++) Explain(bindings[i], i == 4 ? "Push to talk: not set" : "KEYS >", "Mainframe.cs", null, names[i]);
            Explain(quietKeys, "Quiet: Ctrl + Alt + P", "Mainframe.cs", null, "Quiet key");
            Explain(custom, "Custom model name", "Mainframe.cs", null, "Custom model name");
            Explain(localAddress, "Local server address", "Mainframe.cs", FindHintLabel("Local server address"), "Local server address");
            Explain(security, "SECURITY >", "Mainframe.cs", null, "Security");
            for (int i = 0; i < careTimes.Length; i++) Explain(careTimes[i], Care.Names[i], "Care.cs", null, Care.Names[i] + " time");
            Explain(clipChoose, "Save folder", "Mainframe.cs", FindHintLabel("Save folder"), "Save folder");
            Explain(clipsFolder, "Save folder", "Mainframe.cs", null, "Open clips folder");
            hintDelay.Tick += delegate { ShowHint(); };
            Deactivate += delegate { DismissHint(); };
            VisibleChanged += delegate { if (!Visible) DismissHint(); };
            Resize += delegate { DismissHint(); };
            DescribeHints();
        }

        private Control FindHintLabel(string text)
        {
            foreach (Control control in Controls) if (control is Label && control.Text == text) return control;
            return null;
        }

        private void DescribeHint(SettingHint hint)
        {
            foreach (KeyValuePair<Control, SettingHint> entry in hints)
                if (entry.Value == hint) entry.Key.AccessibleDescription = hint.Text;
        }

        private void DescribeHints()
        {
            using (Font ink = HintCard.Ink(Unit()))
                foreach (SettingHint hint in hintRows)
                {
                    hint.Text = HintCard.Brief(hint.Topic.Facts, ink, Math.Max(1, Scaled(332)), 6);
                    DescribeHint(hint);
                }
        }

        private bool HintShows(SettingHint hint)
        {
            return hint.Control.Visible && !Hidden(hint.Control) && ClientRectangle.IntersectsWith(hint.Control.Bounds) && Visible && WindowState != FormWindowState.Minimized;
        }

        private SettingHint HintAt(Point point)
        {
            foreach (SettingHint hint in hintRows) if (HintShows(hint) && hint.Bounds.Contains(point)) return hint;
            return null;
        }

        private void HoverHint(Point point)
        {
            SettingHint over = HintAt(point);
            if (over != overHint)
            {
                overHint = over;
                dismissedHint = null;
            }
            if (over == null) { DismissHint(); return; }
            QueueHint(over, true);
        }

        private void QueueHint(SettingHint hint, bool mouse)
        {
            if (hint == dismissedHint || !HintShows(hint) || Control.MouseButtons != MouseButtons.None) return;
            if (waitingHint == hint && mouseHint == mouse) return;
            DismissHint();
            waitingHint = hint;
            mouseHint = mouse;
            hintDelay.Start();
        }

        private void ShowHint()
        {
            hintDelay.Stop();
            SettingHint hint = waitingHint;
            if (hint == null || !HintShows(hint) || Control.MouseButtons != MouseButtons.None || (mouseHint ? overHint != hint : !hint.Control.ContainsFocus)) { DismissHint(); return; }
            if (hintCard == null) hintCard = new HintCard();
            Rectangle area = Screen.FromRectangle(hint.Bounds).WorkingArea;
            if (!hintCard.Place(hint.Title, hint.Topic.Facts, Unit(), hint.Bounds, area)) { DismissHint(); return; }
            hint.Text = hintCard.Body;
            DescribeHint(hint);
            hintCard.Show(this);
        }

        private void DismissHint()
        {
            hintDelay.Stop();
            waitingHint = null;
            if (hintCard != null && !hintCard.IsDisposed) hintCard.Hide();
        }

        private void FilterHints(ref Message message)
        {
            if (tipsDriven && !tipSent && (message.Msg == 0x0200 || message.Msg == 0x00A0 || message.Msg == 0x02A3 || message.Msg == 0x02A2)) return;
            if (message.Msg == 0x0200 || message.Msg == 0x00A0)
            {
                Control control = Control.FromChildHandle(message.HWnd);
                if (control != null && control.FindForm() == this)
                {
                    hintMouseWindow = message.HWnd;
                    nonClientHint = message.Msg == 0x00A0;
                    long at = message.LParam.ToInt64();
                    Point point = new Point((short)(at & 0xFFFF), (short)((at >> 16) & 0xFFFF));
                    if (message.Msg == 0x00A0)
                    {
                        HintMouseTrack track = new HintMouseTrack { Size = Marshal.SizeOf(typeof(HintMouseTrack)), Flags = 0x12, Window = message.HWnd };
                        TrackMouseEvent(ref track);
                    }
                    HoverHint(message.Msg == 0x00A0 ? point : control.PointToScreen(point));
                }
                else if (mouseHint) { overHint = null; DismissHint(); }
            }
            else if (message.Msg == 0x02A3 || message.Msg == 0x02A2)
            {
                if (message.HWnd != hintMouseWindow || (message.Msg == 0x02A2) != nonClientHint) return;
                overHint = null;
                DismissHint();
            }
            else if (message.Msg == 0x0201 || message.Msg == 0x0204 || message.Msg == 0x0207 || message.Msg == 0x020B || message.Msg == 0x00A1 || message.Msg == 0x00A4 || message.Msg == 0x00A7 || message.Msg == 0x00AB || message.Msg == 0x020A || message.Msg == 0x020E || ((message.Msg == 0x0100 || message.Msg == 0x0104) && (Keys)message.WParam.ToInt32() == Keys.Escape))
            {
                dismissedHint = waitingHint ?? overHint;
                DismissHint();
            }
        }

        private void DisposeHints()
        {
            hintDelay.Dispose();
            if (hintCard != null) hintCard.Dispose();
        }

        private sealed class HintCard : Form
        {
            private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
            private Font titleFont, bodyFont;
            private float scale;
            private Rectangle titleBox, bodyBox;
            private string title;
            internal string Body;

            internal HintCard()
            {
                FormBorderStyle = FormBorderStyle.None;
                AutoScaleMode = AutoScaleMode.None;
                StartPosition = FormStartPosition.Manual;
                ShowInTaskbar = false;
                BackColor = Neon.Ground;
                AccessibleRole = AccessibleRole.ToolTip;
                DoubleBuffered = true;
                SetStyle(ControlStyles.Selectable, false);
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams value = base.CreateParams;
                    value.ExStyle |= 0x08000080;
                    return value;
                }
            }

            internal static Font Ink(float scale)
            {
                using (Font font = Type(9.5f)) return new Font(font.FontFamily, font.SizeInPoints * 96f / 72f * scale, font.Style, GraphicsUnit.Pixel);
            }

            private static Size Measure(string text, Font font, int width)
            {
                return TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrap);
            }

            // Her answers lead with where a setting lives, like "Mainframe > Settings > VOICE > Volume ...", which a tip beside
            // that setting doesn't need.
            private static readonly Regex Breadcrumb = new Regex(@"^Mainframe(?: > (?:Settings|Simple Settings|Advanced Settings|[A-Z][A-Z &-]+))* > ");

            internal static string Unpathed(string facts)
            {
                string text = Breadcrumb.Replace(facts ?? "", "", 1);
                return text.Length > 0 ? char.ToUpperInvariant(text[0]) + text.Substring(1) : text;
            }

            internal static string Brief(string facts, Font font, int width, int lines)
            {
                facts = Unpathed(facts);
                int height = font.Height * Math.Max(1, lines);
                if (Measure(facts, font, width).Height <= height) return facts;
                const string more = " Ask Capri for more";
                string kept = "";
                foreach (Match sentence in Regex.Matches(facts, @".+?(?:[.!?](?=\s+[A-Z]|$)|$)"))
                {
                    string next = facts.Substring(0, sentence.Index + sentence.Length).Trim();
                    if (Measure(next + more, font, width).Height > height) break;
                    kept = next;
                }
                if (kept.Length == 0)
                {
                    foreach (Match word in Regex.Matches(facts, @"\S+"))
                    {
                        string next = facts.Substring(0, word.Index + word.Length).TrimEnd();
                        if (Measure(next + "..." + more, font, width).Height > height) break;
                        kept = next;
                    }
                    if (kept.Length > 0) kept += "...";
                }
                return kept.Length > 0 ? kept + more : more.TrimStart();
            }

            private bool Fit(string name, string facts, float unit, Size room)
            {
                scale = unit;
                if (titleFont != null) titleFont.Dispose();
                if (bodyFont != null) bodyFont.Dispose();
                bodyFont = Ink(unit);
                using (Font font = Type(10f, true)) titleFont = new Font(font.FontFamily, font.SizeInPoints * 96f / 72f * unit, font.Style, GraphicsUnit.Pixel);
                int padding = (int)Math.Ceiling(14 * unit), gap = (int)Math.Ceiling(7 * unit);
                int width = Math.Min((int)Math.Round(360 * unit), room.Width), inside = width - padding * 2;
                if (inside < bodyFont.Height * 3) return false;
                title = name;
                int titleHeight = Measure(name, titleFont, inside).Height;
                int lines = Math.Min(6, (room.Height - padding * 2 - gap - titleHeight) / bodyFont.Height);
                if (lines < 1) return false;
                Body = Brief(facts, bodyFont, inside, lines);
                if (Body == "Ask Capri for more") return false;
                int bodyHeight = Measure(Body, bodyFont, inside).Height;
                if (bodyHeight + titleHeight + gap + padding * 2 > room.Height) return false;
                titleBox = new Rectangle(padding, padding, inside, titleHeight);
                bodyBox = new Rectangle(padding, titleBox.Bottom + gap, inside, bodyHeight);
                ClientSize = new Size(width, bodyBox.Bottom + padding);
                AccessibleName = title;
                AccessibleDescription = Body;
                Neon.Clip(this, 9 * unit, 3 * unit);
                return true;
            }

            internal bool Place(string name, string facts, float unit, Rectangle control, Rectangle area)
            {
                int gap = Math.Max(4, (int)Math.Round(8 * unit));
                Rectangle[] rooms =
                {
                    Rectangle.FromLTRB(Math.Min(area.Right, control.Right + gap), area.Top, area.Right, area.Bottom),
                    Rectangle.FromLTRB(area.Left, area.Top, Math.Max(area.Left, control.Left - gap), area.Bottom),
                    Rectangle.FromLTRB(area.Left, Math.Min(area.Bottom, control.Bottom + gap), area.Right, area.Bottom),
                    Rectangle.FromLTRB(area.Left, area.Top, area.Right, Math.Max(area.Top, control.Top - gap))
                };
                for (int pass = 0; pass < 2; pass++)
                    for (int side = 0; side < rooms.Length; side++)
                    {
                        Rectangle room = rooms[side];
                        if (pass == 0 && room.Width < Math.Min((int)Math.Round(360 * unit), area.Width)) continue;
                        if (!Fit(name, facts, unit, room.Size)) continue;
                        int x = side == 0 ? room.Left : side == 1 ? room.Right - Width : Math.Max(room.Left, Math.Min(control.Left, room.Right - Width));
                        int y = side == 2 ? room.Top : side == 3 ? room.Bottom - Height : Math.Max(room.Top, Math.Min(control.Top, room.Bottom - Height));
                        Location = new Point(x, y);
                        if (area.Contains(Bounds) && !Bounds.IntersectsWith(control)) return true;
                    }
                return false;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Neon.Ground);
                Neon.Frame(e.Graphics, ClientSize, scale, 9 * scale, 3 * scale, 12 * scale, 5 * scale);
                TextRenderer.DrawText(e.Graphics, title, titleFont, titleBox, Neon.Rose, Wrap);
                TextRenderer.DrawText(e.Graphics, Body, bodyFont, bodyBox, Neon.White, Wrap);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    if (titleFont != null) titleFont.Dispose();
                    if (bodyFont != null) bodyFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
