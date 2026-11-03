using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace CapriDesktop
{
    // What the call panel shows, read from her ten times a second.
    internal sealed class CallView
    {
        internal bool Listening, Muted, Discord, Talking;
        // How loud your mic and Discord are, 0 to 1, as her ears measure them.
        internal double You, Call;
        // Why she can't hear Discord while it's on, or null.
        internal string DiscordNote;
        internal TimeSpan Length;
    }

    // Call mode in her look, laid out like a voice call: a tile each for you, her and Discord, whose edge lights up while that
    // one talks, and the call's buttons under them (Lachy, 3 Nov: "make it look like a discord call too when shes on call mode").
    internal sealed class CallPanel : Form
    {
        private const int Wide = 614, Tall = 326, Inset = 22, Gap = 12, TileY = 54, TileHigh = 196, RowY = 272, PressHigh = 32;
        // Talking from about -40 dB, a little under where her ears start a line, and lit for 0.3 s after the last loud moment.
        internal const double Talks = 0.01;
        private const int Hold = 300;
        internal event Action MicPressed, DiscordPressed, EndPressed;
        private readonly float s;
        private readonly Bitmap face;
        private readonly Func<CallView> read;
        private readonly Timer ticks = new Timer();
        private readonly Plate hide, mic, discord, end;
        private CallView view = new CallView();
        private int youAt = -100000, callAt = -100000;
        private bool youLit, callLit;
        private string drawn;

        // Scale 0 follows the screen it's on. The smoke test draws it at others.
        internal CallPanel(Bitmap face, Func<CallView> read, float scale = 0)
        {
            this.face = face;
            this.read = read;
            s = scale > 0 ? scale : DeviceDpi / 96f;
            Text = "Capri call";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = true;
            MaximizeBox = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            ClientSize = new Size(Px(Wide), Px(Tall));
            hide = new Plate { Glyph = 1, Size = new Size(Px(30), Px(24)) };
            hide.Location = new Point(ClientSize.Width - Px(14) - hide.Width, Px(12));
            hide.Click += delegate { WindowState = FormWindowState.Minimized; };
            end = Press("END CALL", Neon.Hot, 104);
            discord = Press("DISCORD: ON", Neon.Rose, 128);
            mic = Press("MUTE", Neon.Rose, 96);
            end.Location = new Point(ClientSize.Width - Px(Inset) - end.Width, Px(RowY));
            discord.Location = new Point(end.Left - Px(8) - discord.Width, Px(RowY));
            mic.Location = new Point(discord.Left - Px(8) - mic.Width, Px(RowY));
            mic.Click += delegate { if (MicPressed != null) MicPressed(); };
            discord.Click += delegate { if (DiscordPressed != null) DiscordPressed(); };
            end.Click += delegate { if (EndPressed != null) EndPressed(); };
            Controls.AddRange(new Control[] { hide, mic, discord, end });
            Neon.Clip(this, 16 * s, 6 * s);
            ticks.Interval = 100;
            ticks.Tick += delegate { if (this.read != null) Take(this.read()); };
        }

        private int Px(float value) { return (int)Math.Round(value * s); }

        // Her fonts in pixels at this panel's scale, so a preview at 200 percent reads like the real thing.
        private Font Type(float points, bool bold = false) { return Neon.Type(points * 96f / 72f * s, bold, GraphicsUnit.Pixel); }

        private Plate Press(string text, Color accent, int wide)
        {
            return new Plate { Text = text, Accent = accent, Font = Type(10f, true), Size = new Size(Px(wide), Px(PressHigh)) };
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override void OnVisibleChanged(EventArgs e)
        {
            ticks.Enabled = Visible && read != null;
            base.OnVisibleChanged(e);
        }

        // Closing it only hides it: the call goes on until END CALL, and the panel comes back with call mode.
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
            base.OnFormClosing(e);
        }

        // It drags from anywhere that isn't a button.
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }

        internal void Take(CallView next)
        {
            if (next == null) return;
            view = next;
            int now = Environment.TickCount;
            if (next.Listening && !next.Muted && next.You >= Talks) youAt = now;
            if (next.Discord && next.DiscordNote == null && next.Call >= Talks) callAt = now;
            youLit = now - youAt < Hold;
            callLit = now - callAt < Hold;
            Mark(mic, !next.Listening ? "LISTEN" : next.Muted ? "UNMUTE" : "MUTE", next.Listening ? Neon.Rose : Neon.Cyan);
            Mark(discord, next.Discord ? "DISCORD: ON" : "DISCORD: OFF", Neon.Rose);
            string state = youLit + "|" + callLit + "|" + next.Talking + "|" + next.Listening + "|" + next.Muted + "|" + next.Discord + "|" + next.DiscordNote + "|" + Clock(next.Length);
            if (state == drawn) return;
            drawn = state;
            Invalidate();
        }

        private static void Mark(Plate plate, string text, Color accent)
        {
            if (plate.Text == text && plate.Accent == accent) return;
            plate.Text = text;
            plate.Accent = accent;
            plate.Invalidate();
        }

        internal static string Status(CallView view)
        {
            if (!view.Listening) return "She isn't listening.";
            if (view.Muted) return "Muted: she hears no one.";
            if (!view.Discord) return "She hears you only.";
            return view.DiscordNote ?? "She hears you and Discord.";
        }

        internal static string Clock(TimeSpan length)
        {
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            string minutes = length.Minutes.ToString("00") + ":" + length.Seconds.ToString("00");
            return length.TotalHours >= 1 ? (int)length.TotalHours + ":" + minutes : minutes;
        }

        private RectangleF Tile(int index)
        {
            float wide = (Wide - 2 * Inset - 2 * Gap) / 3f;
            return new RectangleF((Inset + index * (wide + Gap)) * s, TileY * s, wide * s, TileHigh * s);
        }

        private RectangleF StatusBox { get { return new RectangleF(Px(Inset + 2), Px(RowY - 6), mic.Left - Px(Inset + 14), Px(PressHigh + 12)); } }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * s); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            TextFormatFlags plain = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            using (Font title = Neon.Display(15f * 96f / 72f * s, GraphicsUnit.Pixel))
            using (Font small = Type(9.5f))
            {
                Point at = new Point(Px(Inset + 2), Px(14));
                TextRenderer.DrawText(graphics, "CALL MODE", title, at, Neon.Rose, plain);
                Size titled = TextRenderer.MeasureText(graphics, "CALL MODE", title, Size.Empty, plain);
                Size beta = TextRenderer.MeasureText(graphics, "BETA", small, Size.Empty, plain);
                TextRenderer.DrawText(graphics, "BETA", small, new Point(at.X + titled.Width + Px(8), at.Y + titled.Height - beta.Height - Px(2)), Neon.Faint, plain);
                string clock = Clock(view.Length);
                Size timed = TextRenderer.MeasureText(graphics, clock, small, Size.Empty, plain);
                TextRenderer.DrawText(graphics, clock, small, new Point(hide.Left - Px(10) - timed.Width, at.Y + titled.Height - timed.Height - Px(2)), Neon.Faint, plain);
            }
            bool youOff = !view.Listening || view.Muted, callOff = !view.Discord || view.DiscordNote != null;
            DrawTile(graphics, 0, "YOU", !view.Listening ? "not listening" : view.Muted ? "muted" : null, view.Muted ? Neon.Hot : Neon.Faint, youLit && !youOff, youOff);
            DrawTile(graphics, 1, "CAPRI", null, Neon.Faint, view.Talking, false);
            DrawTile(graphics, 2, "DISCORD", !view.Discord ? "off" : view.DiscordNote != null ? Short(view.DiscordNote) : null, view.DiscordNote != null && view.Discord ? Neon.Hot : Neon.Faint, callLit && !callOff, callOff);
            using (Font words = Type(10f))
                TextRenderer.DrawText(graphics, Status(view), words, Rectangle.Round(StatusBox), Neon.Faint, TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            Neon.Frame(graphics, ClientSize, s, 16 * s, 6 * s, 52 * s, 26 * s);
        }

        // A Discord note short enough for its tile: the full one is the status line's.
        internal static string Short(string note) { return note == CallEars.NotOpen ? "not open" : "can't hear it"; }

        private void DrawTile(Graphics graphics, int index, string name, string note, Color noted, bool lit, bool dim)
        {
            RectangleF tile = Tile(index);
            using (GraphicsPath edge = Neon.Cut(tile, 10 * s, 3 * s, 10 * s, 3 * s))
            {
                using (Brush fill = new SolidBrush(Neon.Well)) graphics.FillPath(fill, edge);
                using (Pen line = new Pen(lit ? Neon.Cyan : Neon.WellEdge, lit ? 2 * s : Math.Max(1, s))) graphics.DrawPath(line, edge);
            }
            float size = 96 * s;
            RectangleF art = new RectangleF(tile.Left + (tile.Width - size) / 2, tile.Top + 26 * s, size, size);
            using (GraphicsPath frame = Neon.Cut(art, 12 * s, 4 * s, 12 * s, 4 * s))
            {
                GraphicsState state = graphics.Save();
                graphics.SetClip(frame, CombineMode.Intersect);
                if (index == 1 && face != null) graphics.DrawImage(face, art, new RectangleF(face.Width * 0.25f, face.Height * 0.25f, face.Width * 0.525f, face.Height * 0.525f), GraphicsUnit.Pixel);
                else
                {
                    using (Brush ground = new SolidBrush(index == 2 ? Neon.Deep : Neon.Field)) graphics.FillRectangle(ground, art);
                    graphics.TranslateTransform(art.Left, art.Top);
                    graphics.ScaleTransform(size / 96f, size / 96f);
                    if (index == 0) Person(graphics); else Headset(graphics);
                }
                graphics.Restore(state);
                if (dim) using (Brush shade = new SolidBrush(Color.FromArgb(150, Neon.Well))) graphics.FillPath(shade, frame);
                using (Pen line = new Pen(Neon.WellEdge, Math.Max(1, s))) graphics.DrawPath(line, frame);
            }
            TextFormatFlags plain = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
            int left = (int)Math.Round(tile.Left + 14 * s), wide = (int)Math.Round(tile.Width - 28 * s);
            using (Font named = Type(11f, true)) TextRenderer.DrawText(graphics, name, named, new Rectangle(left, (int)Math.Round(tile.Top + 140 * s), wide, Px(20)), Neon.White, plain);
            if (note != null) using (Font small = Type(9.5f)) TextRenderer.DrawText(graphics, note, small, new Rectangle(left, (int)Math.Round(tile.Top + 162 * s), wide, Px(18)), noted, plain);
        }

        // You: a head and shoulders in her cut corners, on her field, in rose.
        private static void Person(Graphics graphics)
        {
            using (Brush rose = new SolidBrush(Neon.Rose))
            {
                using (GraphicsPath head = Neon.Cut(new RectangleF(34, 16, 28, 30), 7, 2, 7, 2)) graphics.FillPath(rose, head);
                graphics.FillPolygon(rose, new[] { new PointF(20, 84), new PointF(20, 64), new PointF(30, 54), new PointF(66, 54), new PointF(76, 64), new PointF(76, 84) });
            }
        }

        // Discord: a headset in cyan, for the people on the call.
        private static void Headset(Graphics graphics)
        {
            using (Pen band = new Pen(Neon.Cyan, 6)) graphics.DrawArc(band, new RectangleF(22, 18, 52, 52), 180, 180);
            using (Brush cyan = new SolidBrush(Neon.Cyan))
            {
                using (GraphicsPath cup = Neon.Cut(new RectangleF(15, 44, 17, 28), 4, 1, 4, 1)) graphics.FillPath(cyan, cup);
                using (GraphicsPath cup = Neon.Cut(new RectangleF(64, 44, 17, 28), 4, 1, 4, 1)) graphics.FillPath(cyan, cup);
            }
        }

        // The panel in each state at 100, 150 and 200 percent, saved for review, with its words inside their places.
        internal static void Smoke(List<string> checks, string folder, Bitmap face)
        {
            CallView[] states =
            {
                new CallView { Listening = true, Discord = true, You = 0.05, Length = TimeSpan.FromSeconds(754) },
                new CallView { Listening = true, Discord = true, Call = 0.05, Talking = true, Length = TimeSpan.FromSeconds(3725) },
                new CallView { Listening = true, Muted = true, Discord = true, DiscordNote = CallEars.NotOpen, Length = TimeSpan.FromSeconds(42) },
                new CallView { Discord = false, Length = TimeSpan.Zero }
            };
            Directory.CreateDirectory(folder);
            foreach (float scale in new[] { 1f, 1.5f, 2f })
                for (int i = 0; i < states.Length; i++)
                    using (CallPanel panel = new CallPanel(face, null, scale))
                    {
                        panel.Take(states[i]);
                        foreach (Control control in panel.Controls)
                            if (!panel.ClientRectangle.Contains(control.Bounds)) throw new Exception("A call panel button was clipped at " + scale + " scale.");
                        if (panel.mic.Left - panel.Px(12) < panel.Px(Inset) + panel.Px(140)) throw new Exception("The call panel's status line has no room at " + scale + " scale.");
                        using (Font words = panel.Type(10f))
                        {
                            RectangleF box = panel.StatusBox;
                            Size need = TextRenderer.MeasureText(Status(states[i]), words, new Size((int)box.Width, int.MaxValue), TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak);
                            if (need.Height > box.Height) throw new Exception("The call panel's status line didn't fit at " + scale + " scale.");
                        }
                        using (Font named = panel.Type(11f, true))
                            foreach (string name in new[] { "YOU", "CAPRI", "DISCORD" })
                                if (TextRenderer.MeasureText(name, named).Width > panel.Tile(0).Width - 28 * scale) throw new Exception("A call tile's name didn't fit at " + scale + " scale.");
                        using (Bitmap whole = new Bitmap(panel.Width, panel.Height))
                        using (Bitmap cut = new Bitmap(panel.Width, panel.Height))
                        {
                            panel.DrawToBitmap(whole, panel.ClientRectangle);
                            // A panel that was never shown leaves its buttons out, so each draws itself onto the sheet.
                            using (Graphics graphics = Graphics.FromImage(whole))
                                foreach (Control control in panel.Controls)
                                    using (Bitmap piece = new Bitmap(control.Width, control.Height))
                                    {
                                        control.DrawToBitmap(piece, new Rectangle(Point.Empty, control.Size));
                                        graphics.DrawImage(piece, control.Location);
                                    }
                            using (Graphics graphics = Graphics.FromImage(cut))
                            using (GraphicsPath hull = Neon.Cut(new RectangleF(0, 0, panel.Width, panel.Height), 16 * scale, 6 * scale, 16 * scale, 6 * scale))
                            {
                                graphics.SetClip(hull);
                                graphics.DrawImage(whole, 0, 0);
                            }
                            cut.Save(Path.Combine(folder, "call-panel-" + (int)(scale * 100) + "-" + (i + 1) + ".png"));
                        }
                    }
            checks.Add("PASS call panel: talking, Discord off or missing, muted and not listening all fit at 100, 150 and 200 percent, saved for review");
        }
    }
}
