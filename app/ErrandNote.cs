using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class ErrandNote : Form
    {
        private static readonly Color Paper = Color.FromArgb(255, 250, 252), PaperHead = Color.FromArgb(255, 233, 240), Ink = Color.FromArgb(42, 20, 32), SoftInk = Color.FromArgb(160, 132, 143), Hot = Color.FromArgb(192, 23, 79);
        private const int MaxLines = 4;
        private const int HeadingLetters = 34;
        private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.SingleLine;
        internal const int ShowSeconds = 75;

        internal Action OpenNote;
        internal Action Watch;

        private readonly string heading;
        private readonly string[] lines;
        private readonly bool canOpen, canWatch;
        private readonly float scale;
        private readonly Font headFont, bodyFont, buttonFont;
        private readonly Rectangle headBox, closeBox, openBox, watchBox, bodyBox;
        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        private int hover;

        internal ErrandNote(string task, string answer, bool canOpen, bool canWatch) : this(task, answer, canOpen, canWatch, 0) { }

        internal ErrandNote(string task, string answer, bool canOpen, bool canWatch, float scale)
        {
            this.canOpen = canOpen;
            this.canWatch = canWatch;
            this.scale = scale > 0 ? scale : DeviceDpi / 96f;
            string what = Trim(task, HeadingLetters);
            heading = what.Length == 0 ? "All done" : "Done: " + what;
            headFont = new Font("Segoe UI", 13f * this.scale, FontStyle.Bold, GraphicsUnit.Pixel);
            bodyFont = new Font("Segoe UI", 13f * this.scale, FontStyle.Regular, GraphicsUnit.Pixel);
            buttonFont = new Font("Segoe UI", 12f * this.scale, FontStyle.Bold, GraphicsUnit.Pixel);
            int wide = Px(320), pad = Px(14), headTall = Px(34);
            headBox = new Rectangle(0, 0, wide, headTall);
            closeBox = new Rectangle(wide - Px(30), Px(6), Px(22), Px(22));
            int textWide = wide - pad * 2;
            lines = Wrap(Excerpt(answer), textWide, MaxLines);
            int lineTall = TextRenderer.MeasureText("Ag", bodyFont, new Size(1000, 1000), Flags).Height + Px(2);
            int top = headTall + Px(10);
            bodyBox = new Rectangle(pad, top, textWide, lineTall * Math.Max(1, lines.Length));
            int y = bodyBox.Bottom + Px(10);
            int buttonTall = Px(28);
            int x = pad;
            openBox = watchBox = Rectangle.Empty;
            if (canOpen) { int w = ButtonWide("Open note"); openBox = new Rectangle(x, y, w, buttonTall); x += w + Px(8); }
            if (canWatch) { int w = ButtonWide("Keep an eye on it"); watchBox = new Rectangle(x, y, w, buttonTall); }
            int bottom = canOpen || canWatch ? y + buttonTall : bodyBox.Bottom;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            DoubleBuffered = true;
            BackColor = Paper;
            Text = "Capri's note";
            ClientSize = new Size(wide, bottom + Px(14));
            timer.Interval = ShowSeconds * 1000;
            timer.Tick += delegate { timer.Stop(); Close(); };
        }

        private int Px(int value) { return (int)Math.Round(value * scale); }

        private int ButtonWide(string text) { return TextRenderer.MeasureText(text, buttonFont, new Size(1000, 1000), Flags).Width + Px(24); }

        internal string Heading { get { return heading; } }
        internal string[] Lines { get { return lines; } }
        internal Rectangle CloseBox { get { return closeBox; } }
        internal Rectangle OpenBox { get { return openBox; } }
        internal Rectangle WatchBox { get { return watchBox; } }
        internal bool HasOpen { get { return canOpen; } }
        internal bool HasWatch { get { return canWatch; } }

        internal static string Trim(string text, int letters)
        {
            string flat = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            if (flat.Length <= letters) return flat;
            return flat.Substring(0, Math.Max(1, letters - 1)).TrimEnd() + "…";
        }

        internal static string Excerpt(string answer)
        {
            string text = Regex.Replace(answer ?? "", @"[`*_#>|]+", "");
            return Regex.Replace(text, @"\s+", " ").Trim();
        }

        internal static string First(string answer)
        {
            string text = Excerpt(answer);
            Match end = Regex.Match(text, @"^.{12,}?[.!?](?=\s|$)");
            string sentence = end.Success ? end.Value : text;
            return sentence.Length > 220 ? sentence.Substring(0, 219).TrimEnd() + "…" : sentence;
        }

        private string[] Wrap(string text, int wide, int most)
        {
            List<string> rows = new List<string>();
            if (text.Length == 0) return new string[0];
            string[] words = text.Split(' ');
            string row = "";
            bool cut = false;
            for (int i = 0; i < words.Length && !cut; i++)
            {
                string word = words[i];
                while (word.Length > 0 && !cut)
                {
                    string tryRow = row.Length == 0 ? word : row + " " + word;
                    if (Measure(tryRow) <= wide) { row = tryRow; word = ""; break; }
                    if (row.Length > 0)
                    {
                        rows.Add(row);
                        row = "";
                        if (rows.Count == most) cut = true;
                        continue;
                    }
                    int fit = word.Length;
                    while (fit > 1 && Measure(word.Substring(0, fit)) > wide) fit--;
                    rows.Add(word.Substring(0, fit));
                    word = word.Substring(fit);
                    if (rows.Count == most) cut = true;
                }
            }
            if (!cut && row.Length > 0) rows.Add(row);
            if (rows.Count > most) { rows.RemoveRange(most, rows.Count - most); cut = true; }
            if (cut && rows.Count > 0)
            {
                string last = rows[rows.Count - 1];
                while (last.Length > 1 && Measure(last + "…") > wide) last = last.Substring(0, last.Length - 1).TrimEnd();
                rows[rows.Count - 1] = last + "…";
            }
            return rows.ToArray();
        }

        private int Measure(string text) { return TextRenderer.MeasureText(text, bodyFont, new Size(10000, 1000), Flags).Width; }

        internal static Point Spot(Size size, Rectangle body, Rectangle area, bool right, int gap)
        {
            int x = right ? body.Right + gap : body.Left - size.Width - gap;
            if (x < area.Left || x + size.Width > area.Right)
            {
                int other = right ? body.Left - size.Width - gap : body.Right + gap;
                if (other >= area.Left && other + size.Width <= area.Right) x = other;
            }
            int y = body.Top + body.Height / 4;
            x = Math.Max(area.Left, Math.Min(x, area.Right - size.Width));
            y = Math.Max(area.Top, Math.Min(y, area.Bottom - size.Height));
            return new Point(x, y);
        }

        internal void Follow(Rectangle body, Rectangle area, bool right)
        {
            Point spot = Spot(Size, body, area, right, Px(10));
            if (Location != spot) Location = spot;
        }

        internal void Arrive(Rectangle body, Rectangle area, bool right)
        {
            Follow(body, area, right);
            if (!Visible) Show();
            timer.Stop();
            timer.Start();
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams p = base.CreateParams;
                p.ExStyle |= 0x08000000 | 0x80;
                return p;
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x21) { m.Result = new IntPtr(3); return; }
            base.WndProc(ref m);
        }

        private void Shape()
        {
            if (!IsHandleCreated) return;
            Region old = Region;
            using (GraphicsPath shape = Round(new Rectangle(0, 0, Width, Height), Px(14))) Region = new Region(shape);
            if (old != null) old.Dispose();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            Shape();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Shape();
        }

        private static GraphicsPath Round(Rectangle box, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = Math.Max(2, Math.Min(radius * 2, Math.Min(box.Width, box.Height)));
            path.AddArc(box.Left, box.Top, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private int At(Point point)
        {
            if (closeBox.Contains(point)) return 3;
            if (canOpen && openBox.Contains(point)) return 1;
            if (canWatch && watchBox.Contains(point)) return 2;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int now = At(e.Location);
            Cursor = now != 0 || canOpen ? Cursors.Hand : Cursors.Default;
            if (now != hover) { hover = now; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != 0) { hover = 0; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Click(At(e.Location));
        }

        internal void Click(int what)
        {
            if (what == 3) { Close(); return; }
            if (what == 2) { Action go = Watch; Close(); if (go != null) go(); return; }
            if (what == 1 || (what == 0 && canOpen)) { Action go = OpenNote; Close(); if (go != null) go(); }
        }

        internal void ClickOpen() { Click(canOpen ? 1 : -1); }

        internal void ClickWatch() { Click(canWatch ? 2 : -1); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush head = new SolidBrush(PaperHead)) g.FillRectangle(head, headBox);
            using (Pen line = new Pen(Color.FromArgb(70, Hot), Math.Max(1f, scale))) g.DrawLine(line, 0, headBox.Bottom, Width, headBox.Bottom);
            Rectangle title = new Rectangle(Px(14), 0, closeBox.Left - Px(14), headBox.Height);
            TextRenderer.DrawText(g, heading, headFont, title, Hot, Flags | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            TextRenderer.DrawText(g, "×", headFont, closeBox, hover == 3 ? Hot : SoftInk, TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            int lineTall = lines.Length > 0 ? bodyBox.Height / lines.Length : 0;
            for (int i = 0; i < lines.Length; i++)
                TextRenderer.DrawText(g, lines[i], bodyFont, new Rectangle(bodyBox.Left, bodyBox.Top + i * lineTall, bodyBox.Width, lineTall), Ink, Flags | TextFormatFlags.VerticalCenter);
            if (canOpen) Button(g, openBox, "Open note", true, hover == 1);
            if (canWatch) Button(g, watchBox, "Keep an eye on it", false, hover == 2);
            using (GraphicsPath edge = Round(new Rectangle(0, 0, Width - 1, Height - 1), Px(14)))
            using (Pen rim = new Pen(Color.FromArgb(150, Hot), Math.Max(1f, scale))) g.DrawPath(rim, edge);
        }

        private void Button(Graphics g, Rectangle box, string text, bool solid, bool over)
        {
            using (GraphicsPath pill = Round(box, box.Height / 2))
            {
                if (solid) using (SolidBrush fill = new SolidBrush(over ? Color.FromArgb(215, 40, 100) : Hot)) g.FillPath(fill, pill);
                else
                {
                    if (over) using (SolidBrush fill = new SolidBrush(PaperHead)) g.FillPath(fill, pill);
                    using (Pen rim = new Pen(Hot, Math.Max(1f, scale))) g.DrawPath(rim, pill);
                }
            }
            TextRenderer.DrawText(g, text, buttonFont, box, solid ? Color.White : Hot, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                headFont.Dispose();
                bodyFont.Dispose();
                buttonFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
