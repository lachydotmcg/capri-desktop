using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CapriDesktop
{
    // Her look, shared by Capri and her installer: her Mainframe's ground with a wine glow at the top, corners clipped big at
    // the top left and bottom right with a red rim and hot red strokes over the two big corners, rose titles and Windows' own
    // Bahnschrift. The Mainframe, her menus, her type box, the installer and her notices all draw from here.
    internal static class Neon
    {
        internal static readonly string[] LookNames = { "Night City", "Black", "White", "Grey" };
        private static readonly Color[][] Looks =
        {
            new[] { Color.FromArgb(10, 10, 16), Color.FromArgb(54, 14, 26), Color.FromArgb(16, 12, 20), Color.FromArgb(44, 122, 134), Color.FromArgb(255, 93, 102), Color.FromArgb(236, 236, 241), Color.FromArgb(146, 130, 140), Color.FromArgb(17, 16, 28), Color.FromArgb(30, 20, 36), Color.FromArgb(46, 22, 38), Color.FromArgb(92, 24, 36), Color.FromArgb(232, 66, 70), Color.FromArgb(94, 240, 246), Color.FromArgb(9, 30, 33), Color.FromArgb(40, 120, 132), Color.FromArgb(6, 14, 18), Color.FromArgb(78, 28, 40), Color.FromArgb(245, 90, 86), Color.FromArgb(120, 30, 44), Color.FromArgb(12, 11, 20), Color.FromArgb(72, 28, 40), Color.FromArgb(244, 214, 70), Color.FromArgb(9, 11, 15), Color.FromArgb(232, 236, 240), Color.FromArgb(122, 128, 138), Color.FromArgb(16, 13, 22) },
            new[] { Gray(0), Gray(24), Gray(8), Gray(110), Gray(255), Gray(238), Gray(150), Gray(16), Gray(34), Gray(52), Gray(84), Gray(245), Gray(205), Gray(22), Gray(120), Gray(1), Gray(70), Gray(250), Gray(96), Gray(6), Gray(76), Gray(230), Gray(4), Gray(232), Gray(140), Gray(14) },
            new[] { Gray(255), Gray(236), Gray(248), Gray(120), Gray(0), Gray(20), Gray(96), Gray(244), Gray(230), Gray(214), Gray(170), Gray(30), Gray(64), Gray(238), Gray(110), Gray(254), Gray(190), Gray(10), Gray(150), Gray(250), Gray(180), Gray(60), Gray(252), Gray(26), Gray(104), Gray(243) },
            new[] { Gray(92), Gray(108), Gray(84), Gray(170), Gray(255), Gray(250), Gray(222), Gray(78), Gray(104), Gray(118), Gray(140), Gray(246), Gray(236), Gray(72), Gray(190), Gray(30), Gray(130), Gray(252), Gray(160), Gray(40), Gray(150), Gray(244), Gray(88), Gray(248), Gray(216), Gray(80) }
        };
        private static Color[] now = Looks[0];
        private static int shade;

        internal static Color Ground { get { return now[0]; } }
        internal static Color Glow { get { return now[1]; } }
        internal static Color Band { get { return now[2]; } }
        internal static Color Rule { get { return now[3]; } }
        internal static Color Rose { get { return now[4]; } }
        internal static Color White { get { return now[5]; } }
        internal static Color Faint { get { return now[6]; } }
        internal static Color Field { get { return now[7]; } }
        internal static Color Hover { get { return now[8]; } }
        internal static Color Pressed { get { return now[9]; } }
        internal static Color Edge { get { return now[10]; } }
        internal static Color Hot { get { return now[11]; } }
        internal static Color Cyan { get { return now[12]; } }
        internal static Color Deep { get { return now[13]; } }
        internal static Color DeepEdge { get { return now[14]; } }
        internal static Color OnInk { get { return now[15]; } }
        internal static Color Dash { get { return now[16]; } }
        internal static Color Lit { get { return now[17]; } }
        internal static Color Brim { get { return now[18]; } }
        internal static Color Well { get { return now[19]; } }
        internal static Color WellEdge { get { return now[20]; } }
        internal static Color Amber { get { return now[21]; } }
        internal static Color Sheet { get { return now[22]; } }
        internal static Color SheetInk { get { return now[23]; } }
        internal static Color SheetDim { get { return now[24]; } }
        internal static Color MenuBack { get { return now[25]; } }

        private static Color Gray(int v) { return Color.FromArgb(v, v, v); }

        internal static int ClampLook(int look) { return look < 0 || look >= Looks.Length ? 0 : look; }

        internal static bool Mono { get { return shade != 0; } }

        internal static Color[] LookColours(int look) { return Looks[ClampLook(look)]; }

        internal static void UseLook(int look) { shade = ClampLook(look); now = Looks[shade]; }

        internal static Color Tone(int r, int g, int b)
        {
            if (shade == 0) return Color.FromArgb(r, g, b);
            int light = (299 * r + 587 * g + 114 * b) / 1000;
            return Gray(shade == 1 ? light : shade == 2 ? 255 - light * 85 / 100 : 70 + light * 72 / 100);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wide, IntPtr tall);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RedrawWindow(IntPtr window, IntPtr box, IntPtr region, uint how);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        internal static bool ShownToWindows(Control window) { return window.IsHandleCreated && IsWindowVisible(window.Handle); }

        internal static void Repaint(int look, params Control[] also)
        {
            Color[] was = now;
            UseLook(look);
            if (was == now) return;
            System.Collections.Generic.List<Control> windows = new System.Collections.Generic.List<Control>();
            foreach (Form form in Application.OpenForms) if (form is LookWindow && !form.InvokeRequired) windows.Add(form);
            foreach (Control control in also) if (control != null && !control.IsDisposed && !control.InvokeRequired && !windows.Contains(control)) windows.Add(control);
            // A menu can be reached twice, on its own and under the item it drops from, and a second swap would read the new
            // look's greys as the old look's, so each control and item is recoloured once.
            System.Collections.Generic.HashSet<object> done = new System.Collections.Generic.HashSet<object>();
            foreach (Control window in windows)
            {
                bool batched = window.IsHandleCreated && window.Visible && !(window is ToolStrip);
                if (batched) SendMessage(window.Handle, 0x000B, IntPtr.Zero, IntPtr.Zero);
                try { Recolour(window, was, done); }
                finally
                {
                    if (batched)
                    {
                        SendMessage(window.Handle, 0x000B, new IntPtr(1), IntPtr.Zero);
                        RedrawWindow(window.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0004 | 0x0080);
                    }
                }
            }
        }

        private static Color Swapped(Color stored, Color[] was)
        {
            if (stored.IsEmpty || stored.A != 255) return stored;
            for (int i = 0; i < was.Length; i++) if (was[i].ToArgb() == stored.ToArgb()) return now[i];
            return stored;
        }

        private static void Recolour(Control control, Color[] was, System.Collections.Generic.HashSet<object> done)
        {
            if (!done.Add(control)) return;
            foreach (Control child in control.Controls) Recolour(child, was, done);
            // A strip's items read its colours until they have their own, so like child controls they go before it.
            ToolStrip strip = control as ToolStrip;
            if (strip != null) foreach (ToolStripItem item in strip.Items) Recolour(item, was, done);
            Color fore = Swapped(control.ForeColor, was), back = Swapped(control.BackColor, was);
            if (fore.ToArgb() != control.ForeColor.ToArgb()) control.ForeColor = fore;
            if (back.ToArgb() != control.BackColor.ToArgb()) control.BackColor = back;
            Button button = control as Button;
            if (button != null && button.FlatStyle == FlatStyle.Flat && !button.FlatAppearance.BorderColor.IsEmpty) button.FlatAppearance.BorderColor = Swapped(button.FlatAppearance.BorderColor, was);
            Tinted tinted = control as Tinted;
            if (tinted != null) tinted.Recolour(delegate(Color stored) { return Swapped(stored, was); });
            control.Invalidate();
        }

        private static void Recolour(ToolStripItem item, Color[] was, System.Collections.Generic.HashSet<object> done)
        {
            if (!done.Add(item)) return;
            ToolStripDropDownItem drop = item as ToolStripDropDownItem;
            if (drop != null && drop.HasDropDownItems) Recolour(drop.DropDown, was, done);
            item.ForeColor = Swapped(item.ForeColor, was);
            item.BackColor = Swapped(item.BackColor, was);
        }

        // The wine glow's falloff: strong at the top, gone well before the middle.
        internal static readonly Blend Fade = Falloff();

        private static Blend Falloff()
        {
            Blend blend = new Blend(9);
            for (int i = 0; i < 9; i++)
            {
                float at = i / 8f;
                blend.Positions[i] = at;
                blend.Factors[i] = 1 - (1 - at) * (1 - at);
            }
            return blend;
        }

        private static readonly FontFamily Plain = Family("Bahnschrift SemiCondensed");
        private static readonly FontFamily Heavy = Family("Bahnschrift SemiBold SemiConden");
        private static readonly FontFamily Narrow = Family("Bahnschrift SemiBold Condensed");

        private static FontFamily Family(string name)
        {
            try { return new FontFamily(name); }
            catch (ArgumentException) { return null; }
        }

        // Bahnschrift's semi-condensed cut, or its semi-bold for buttons and labels. Windows without it get Segoe UI.
        internal static Font Type(float size, bool bold = false, GraphicsUnit unit = GraphicsUnit.Point)
        {
            FontFamily family = bold ? Heavy : Plain;
            return family != null ? new Font(family, size, FontStyle.Regular, unit) : new Font("Segoe UI", size - 0.5f, bold ? FontStyle.Bold : FontStyle.Regular, unit);
        }

        // A window's title, like MAINFRAME: Bahnschrift's condensed semi-bold.
        internal static Font Display(float size, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return Narrow != null ? new Font(Narrow, size, FontStyle.Regular, unit) : new Font("Segoe UI Semibold", size - 2f, FontStyle.Regular, unit);
        }

        // A box with each corner clipped by its own amount, top left first and on round the clock.
        internal static GraphicsPath Cut(RectangleF box, float topLeft, float topRight, float bottomRight, float bottomLeft)
        {
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(new[]
            {
                new PointF(box.Left + topLeft, box.Top), new PointF(box.Right - topRight, box.Top), new PointF(box.Right, box.Top + topRight),
                new PointF(box.Right, box.Bottom - bottomRight), new PointF(box.Right - bottomRight, box.Bottom), new PointF(box.Left + bottomLeft, box.Bottom),
                new PointF(box.Left, box.Bottom - bottomLeft), new PointF(box.Left, box.Top + topLeft)
            });
            return path;
        }

        // A panel's edge: corners clipped big at the top left and bottom right and small at the others, a dark red line round
        // it, and hot red strokes over the two big corners, reaching `along` the top and bottom and `down` the sides.
        internal static void Frame(Graphics graphics, Size size, float s, float big, float small, float along, float down) { Frame(graphics, size, s, big, small, along, down, Brim, Hot); }

        internal static void Frame(Graphics graphics, Size size, float s, float big, float small, float along, float down, Color brim, Color hotLine)
        {
            SmoothingMode was = graphics.SmoothingMode;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            RectangleF box = new RectangleF(0.5f * s, 0.5f * s, size.Width - s - 1, size.Height - s - 1);
            using (GraphicsPath edge = Cut(box, big, small, big, small))
            using (Pen line = new Pen(brim, Math.Max(1, 1.2f * s)))
                graphics.DrawPath(line, edge);
            RectangleF hot = RectangleF.Inflate(box, -0.6f * s, -0.6f * s);
            using (Pen stroke = new Pen(hotLine, 2 * s))
            {
                graphics.DrawLines(stroke, new[] { new PointF(hot.Left, hot.Top + big + down), new PointF(hot.Left, hot.Top + big), new PointF(hot.Left + big, hot.Top), new PointF(hot.Left + big + along, hot.Top) });
                graphics.DrawLines(stroke, new[] { new PointF(hot.Right, hot.Bottom - big - down), new PointF(hot.Right, hot.Bottom - big), new PointF(hot.Right - big, hot.Bottom), new PointF(hot.Right - big - along, hot.Bottom) });
            }
            graphics.SmoothingMode = was;
        }

        // Cuts a window to its clipped corners.
        internal static void Clip(Control window, float big, float small)
        {
            Region old = window.Region;
            using (GraphicsPath hull = Cut(new RectangleF(0, 0, window.Width, window.Height), big, small, big, small)) window.Region = new Region(hull);
            if (old != null) old.Dispose();
        }

        // Her ground with the wine glow fading out over `reach` from the top.
        internal static void Backdrop(Graphics graphics, Size size, float reach) { Backdrop(graphics, size, reach, Ground, Glow); }

        internal static void Backdrop(Graphics graphics, Size size, float reach, Color bottom, Color top)
        {
            using (Brush ground = new SolidBrush(bottom)) graphics.FillRectangle(ground, 0, 0, size.Width, size.Height);
            float glow = Math.Min(size.Height, reach);
            if (glow < 1) return;
            using (LinearGradientBrush wine = new LinearGradientBrush(new RectangleF(0, -1, 16, glow + 2), top, bottom, LinearGradientMode.Vertical))
            {
                wine.Blend = Fade;
                graphics.FillRectangle(wine, 0, 0, size.Width, glow);
            }
        }

        // What's behind a control on one of her windows, measured from the window so it reads as cut out of one panel.
        internal static void Behind(Graphics graphics, Control control, float reach)
        {
            Form window = control.FindForm();
            if (window == null) { Backdrop(graphics, control.Size, reach); return; }
            int x = 0, y = 0;
            for (Control at = control; at != null && at != window; at = at.Parent) { x += at.Left; y += at.Top; }
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(-x, -y);
            Backdrop(graphics, window.ClientSize, reach);
            graphics.Restore(state);
        }

        // A label that lets the mouse through to its window, so the window drags from anywhere that isn't a control.
        internal sealed class Caption : Label
        {
            internal Caption()
            {
                BackColor = Color.Transparent;
                UseMnemonic = false;
            }

            protected override void WndProc(ref Message message)
            {
                if (message.Msg == 0x0084) { message.Result = new IntPtr(-1); return; }
                base.WndProc(ref message);
            }
        }
    }

    // A button in her look: a dark box with a red rim and its bottom right corner clipped. A cyan one is the step to take, and
    // a solid one is the window's main step; a red one ends something. Glyph 1 or 2 makes it a bare minimize or close mark.
    internal interface LookWindow { }

    internal interface Tinted { void Recolour(Func<Color, Color> swap); }

    internal static class CityLook
    {
        internal static readonly Color Ground = Neon.LookColours(0)[0];
        internal static readonly Color Glow = Neon.LookColours(0)[1];
        internal static readonly Color Band = Neon.LookColours(0)[2];
        internal static readonly Color Rule = Neon.LookColours(0)[3];
        internal static readonly Color Rose = Neon.LookColours(0)[4];
        internal static readonly Color White = Neon.LookColours(0)[5];
        internal static readonly Color Faint = Neon.LookColours(0)[6];
        internal static readonly Color Field = Neon.LookColours(0)[7];
        internal static readonly Color Hover = Neon.LookColours(0)[8];
        internal static readonly Color Pressed = Neon.LookColours(0)[9];
        internal static readonly Color Edge = Neon.LookColours(0)[10];
        internal static readonly Color Hot = Neon.LookColours(0)[11];
        internal static readonly Color Cyan = Neon.LookColours(0)[12];
        internal static readonly Color Deep = Neon.LookColours(0)[13];
        internal static readonly Color DeepEdge = Neon.LookColours(0)[14];
        internal static readonly Color OnInk = Neon.LookColours(0)[15];
        internal static readonly Color Dash = Neon.LookColours(0)[16];
        internal static readonly Color Lit = Neon.LookColours(0)[17];
        internal static readonly Color Brim = Neon.LookColours(0)[18];
        internal static readonly Color Well = Neon.LookColours(0)[19];
        internal static readonly Color WellEdge = Neon.LookColours(0)[20];
        internal static void Frame(Graphics graphics, Size size, float s, float big, float small, float along, float down) { Neon.Frame(graphics, size, s, big, small, along, down, Brim, Hot); }
        internal static void Backdrop(Graphics graphics, Size size, float reach) { Neon.Backdrop(graphics, size, reach, Ground, Glow); }
    }

    internal sealed class Plate : Button, Tinted
    {
        internal Color Accent = Neon.Rose;
        internal bool Solid;
        internal int Glyph;
        private bool hot;
        private bool down;

        public void Recolour(Func<Color, Color> swap) { Accent = swap(Accent); }

        internal Plate()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
            BackColor = Neon.Ground;
            Cursor = Cursors.Hand;
            UseMnemonic = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { hot = false; down = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { down = true; Invalidate(); } base.OnMouseDown(e); }

        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            float s = DeviceDpi / 96f;
            Graphics graphics = e.Graphics;
            Neon.Behind(graphics, this, 210 * s);
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float line = Math.Max(1f, s);
            RectangleF box = new RectangleF(line / 2, line / 2, Width - line, Height - line);
            float x = Width / 2f;
            float y = Height / 2f;
            if (Glyph != 0)
            {
                if (hot) using (GraphicsPath plate = Neon.Cut(box, 0, 0, 4 * s, 0)) using (Brush fill = new SolidBrush(down ? Neon.Pressed : Neon.Hover)) graphics.FillPath(fill, plate);
                using (Pen pen = new Pen(hot || (Focused && ShowFocusCues) ? (Glyph == 2 ? Neon.Hot : Neon.Cyan) : Neon.Faint, 1.5f * s))
                {
                    if (Glyph == 1) graphics.DrawLine(pen, x - 5 * s, y, x + 5 * s, y);
                    else
                    {
                        graphics.DrawLine(pen, x - 4.5f * s, y - 4.5f * s, x + 4.5f * s, y + 4.5f * s);
                        graphics.DrawLine(pen, x - 4.5f * s, y + 4.5f * s, x + 4.5f * s, y - 4.5f * s);
                    }
                }
                return;
            }
            bool lit = hot || (Focused && ShowFocusCues);
            Color face, rim, ink;
            if (!Enabled) { face = Neon.Field; rim = Neon.Tone(52, 22, 32); ink = Neon.Tone(112, 94, 104); }
            else if (Solid) { face = down ? ControlPaint.Dark(Accent, 0.1f) : hot ? ControlPaint.Light(Accent, 0.35f) : Accent; rim = face; ink = Neon.OnInk; }
            else if (Accent == Neon.Cyan) { face = down ? Neon.Tone(18, 62, 67) : hot ? Neon.Tone(13, 45, 49) : Neon.Deep; rim = lit ? Neon.Cyan : Neon.DeepEdge; ink = Neon.Cyan; }
            else if (Accent == Neon.Hot) { face = down ? Neon.Tone(72, 18, 30) : hot ? Neon.Tone(54, 16, 28) : Neon.Tone(34, 12, 22); rim = lit ? Neon.Tone(255, 124, 122) : Neon.Hot; ink = Neon.Tone(255, 166, 162); }
            else { face = down ? Neon.Pressed : hot ? Neon.Hover : Neon.Field; rim = lit ? Neon.Hot : Neon.Edge; ink = Accent; }
            using (GraphicsPath plate = Neon.Cut(box, 0, 0, 6 * s, 0))
            {
                using (Brush brush = new SolidBrush(face)) graphics.FillPath(brush, plate);
                using (Pen pen = new Pen(rim, line)) graphics.DrawPath(pen, plate);
            }
            // A solid button shows keyboard focus as a dark line just inside its edge.
            if (Solid && Enabled && Focused && ShowFocusCues)
                using (GraphicsPath inner = Neon.Cut(RectangleF.Inflate(box, -3 * s, -3 * s), 0, 0, 4 * s, 0))
                using (Pen pen = new Pen(Neon.OnInk, line))
                    graphics.DrawPath(pen, inner);
            TextRenderer.DrawText(graphics, Text, Font, ClientRectangle, ink, TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }
    }
}
