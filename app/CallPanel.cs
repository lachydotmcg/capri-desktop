using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class CallView
    {
        internal CallApp App = CallApps.Apps[0];
        internal bool Listening, Muted, Discord, Talking, InMic, Thinking, Ended;
        internal bool Captions = true, Motion = true;
        internal double You, Call, Her, Mic;
        internal string DiscordNote, Who, Said;
        internal DateTime SaidAt;
        internal TimeSpan Length;
        internal int Answers;

        internal bool Recent(DateTime now) { return !Ended && Captions && !string.IsNullOrEmpty(Said) && (now - SaidAt).TotalSeconds < 10; }
    }

    internal sealed class CallLive
    {
        internal CallView View = new CallView();
        internal bool You, Discord;
        private int youAt, callAt;
        private bool hadYou, hadCall;
        private readonly double[] ripple = new double[3];

        internal bool Her { get { return !View.Ended && View.Talking; } }
        internal bool Animates { get { return !View.Ended && View.Motion; } }

        internal void Take(CallView next)
        {
            int now = Environment.TickCount;
            bool wasYou = You, wasHer = Her, wasCall = Discord;
            View = next;
            if (next.Listening && !next.Muted && next.You >= CallPanel.Talks) { youAt = now; hadYou = true; }
            if (next.Discord && next.DiscordNote == null && next.Call >= CallPanel.Talks) { callAt = now; hadCall = true; }
            You = !next.Ended && next.Listening && !next.Muted && hadYou && unchecked(now - youAt) < 300;
            Discord = !next.Ended && next.Discord && next.DiscordNote == null && hadCall && unchecked(now - callAt) < 300;
            double time = CallInk.Time;
            if (You && !wasYou) ripple[0] = time;
            if (Her && !wasHer) ripple[1] = time;
            if (Discord && !wasCall) ripple[2] = time;
            if (!next.Listening || next.Muted || next.Ended) hadYou = false;
            if (!next.Discord || next.DiscordNote != null || next.Ended) hadCall = false;
        }

        internal bool Lit(int index) { return index == 0 ? You : index == 1 ? Her : Discord; }
        internal double Level(int index) { return !Lit(index) ? 0 : index == 1 ? CallInk.Unit(View.Her) : CallInk.Loud(index == 0 ? View.You : View.Call); }
        internal float Ripple(int index) { return (float)((CallInk.Time - ripple[index]) % 1.2 / 1.2); }

        internal string State(bool pill)
        {
            CallView v = View;
            if (v.Ended) return "ended|" + CallPanel.Clock(v.Length) + "|" + v.Answers;
            string state = string.Join("|", new object[] { You, Her, Discord, v.Listening, v.Muted, v.Discord, v.DiscordNote != null,
                v.Motion, v.App.Name, CallPanel.Clock(v.Length), Step(Level(0)), Step(Level(1)), Step(Level(2)) });
            if (!pill) state += "|" + v.InMic + "|" + v.Thinking + "|" + v.Captions + "|" + v.DiscordNote + "|" + (v.InMic ? Step(CallInk.Loud(v.Mic)) : 0);
            if (v.Captions && (!pill || v.Recent(DateTime.UtcNow)))
                state += "|" + v.Who + "|" + v.Said + "|" + v.SaidAt.Ticks + "|" + (pill ? 100 : (int)(CallInk.Fade(v, DateTime.UtcNow) * 100));
            return state;
        }

        private static int Step(double value) { return (int)Math.Round(CallInk.Unit(value) * 100); }
    }

    internal sealed class CallFonts : IDisposable
    {
        internal readonly Font Title, Name, Note, Words, Mono, Tiny, Button;

        internal CallFonts(float s)
        {
            Title = Neon.Display(20 * s, GraphicsUnit.Pixel);
            Name = Neon.Display(15 * s, GraphicsUnit.Pixel);
            Note = Neon.Type(12.5f * s, false, GraphicsUnit.Pixel);
            Words = Neon.Type(13 * s, false, GraphicsUnit.Pixel);
            Mono = Look.Mono(12.5f * s, FontStyle.Regular);
            Tiny = Look.Mono(10.5f * s, FontStyle.Regular);
            Button = Neon.Type(14 * s, true, GraphicsUnit.Pixel);
        }

        public void Dispose() { foreach (Font font in new[] { Title, Name, Note, Words, Mono, Tiny, Button }) font.Dispose(); }
    }

    internal static class CallInk
    {
        internal static readonly Color Red = Color.FromArgb(216, 40, 63);
        internal static readonly StringFormat Left = Format(StringAlignment.Near);
        internal static readonly StringFormat Centre = Format(StringAlignment.Center);

        private static StringFormat Format(StringAlignment align)
        {
            StringFormat format = new StringFormat(StringFormat.GenericTypographic);
            format.Alignment = align;
            format.LineAlignment = StringAlignment.Center;
            format.FormatFlags |= StringFormatFlags.NoWrap;
            format.Trimming = StringTrimming.EllipsisCharacter;
            return format;
        }

        internal static double Time { get { return (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency; } }
        internal static double Unit(double value) { return double.IsNaN(value) ? 0 : Math.Max(0, Math.Min(1, value)); }
        // A voice's loudness on a meter's scale, by decibels like a real one: -48 dB is empty and -12 dB is full, so ordinary
        // talking, around -20 dB, fills about three quarters (Lachy, 4 Nov: her volume looked tiny on the call panel).
        internal static double Loud(double rms) { return double.IsNaN(rms) || rms <= 0 ? 0 : Unit((20 * Math.Log10(rms) + 48) / 36); }
        internal static float RingWidth(double level) { return 2 + 5 * (float)Unit(level); }
        internal static int Dot(bool motion, double time) { return motion ? (int)Math.Round(255 * (0.675 + 0.325 * Math.Cos(time * Math.PI * 2 / 1.4))) : 255; }
        internal static double Fade(CallView view, DateTime now)
        {
            if (string.IsNullOrEmpty(view.Said)) return 0.4;
            double age = (now - view.SaidAt).TotalSeconds;
            return age <= 10 ? 1 : view.Motion ? 1 - 0.6 * Unit(age - 10) : 0.4;
        }

        internal static void Quality(Graphics graphics)
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        }

        internal static void Text(Graphics graphics, string text, Font font, RectangleF box, Color color, bool centre = false)
        {
            using (Brush ink = new SolidBrush(color)) graphics.DrawString(text ?? "", font, ink, box, centre ? Centre : Left);
        }

        internal static void Round(Graphics graphics, RectangleF box, float radius, Color fill, Color edge, float width)
        {
            using (GraphicsPath path = Look.Rounded(box, radius))
            {
                using (Brush ink = new SolidBrush(fill)) graphics.FillPath(ink, path);
                if (width > 0) using (Pen line = new Pen(edge, width)) graphics.DrawPath(line, path);
            }
        }

        internal static string Tip(int kind, CallView view)
        {
            if (kind == 1) return !view.Listening ? "Start listening" : view.Muted ? "Unmute" : "Mute";
            if (kind == 2) return "Turn " + view.App.Name + (view.Discord ? " off" : " on");
            if (kind == 3) return view.Captions ? "Turn captions off" : "Turn captions on";
            return kind == 4 ? "End call" : kind == 5 ? "Minimize" : kind == 6 ? "Call again" : "Expand call";
        }

        internal static void Button(Graphics graphics, RectangleF box, int kind, CallView view, bool hover, float s, CallFonts fonts)
        {
            bool muted = view.Listening && view.Muted;
            Color fill = kind == 4 ? Red : muted && kind == 1 ? Neon.Tone(46, 15, 26) : hover ? Neon.Hover : Neon.Well;
            Color edge = kind == 4 ? Red : kind == 6 ? Neon.Cyan : muted && kind == 1 ? Neon.Hot : hover ? Neon.Cyan : Neon.WellEdge;
            Color ink = kind == 4 ? Color.White : kind == 6 || kind == 7 || kind == 1 && !view.Listening ? Neon.Cyan : kind == 1 && muted ? Neon.Hot
                : kind == 2 && !view.Discord || kind == 3 && !view.Captions ? Neon.Faint : Neon.Cyan;
            if (kind != 5) Round(graphics, RectangleF.Inflate(box, -s, -s), box.Height / 2 - s, fill, edge, Math.Max(1, s));
            if (kind == 3 || kind == 6) { Text(graphics, kind == 3 ? "CC" : "Call again", fonts.Button, box, ink, true); return; }
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(box.Left + box.Width / 2, box.Top + box.Height / 2);
            graphics.ScaleTransform(s, s);
            using (Pen pen = new Pen(ink, 1.9f))
            {
                pen.StartCap = pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                using (GraphicsPath path = new GraphicsPath())
                {
                    if (kind == 1)
                    {
                        using (GraphicsPath head = Look.Rounded(new RectangleF(-3.5f, -11, 7, 13), 3.5f)) graphics.DrawPath(pen, head);
                        path.AddArc(-7, -8, 14, 15, 0, 180);
                        path.StartFigure(); path.AddLine(0, 7, 0, 11); path.StartFigure(); path.AddLine(-4, 11, 4, 11);
                    }
                    else if (kind == 2)
                    {
                        path.AddArc(-9, -10, 18, 20, 180, 180);
                        using (GraphicsPath cup = Look.Rounded(new RectangleF(-11, -1, 5, 11), 2)) graphics.DrawPath(pen, cup);
                        using (GraphicsPath cup = Look.Rounded(new RectangleF(6, -1, 5, 11), 2)) graphics.DrawPath(pen, cup);
                    }
                    else if (kind == 4)
                    {
                        path.AddBezier(-14, 1, -8, -7, 8, -7, 14, 1);
                        path.AddLine(14, 1, 14, 6);
                        path.AddBezier(14, 6, 14, 9, 8, 9, 8, 6);
                        path.AddLine(8, 6, 8, 2);
                        path.AddBezier(8, 2, 4, 0, -4, 0, -8, 2);
                        path.AddLine(-8, 2, -8, 6);
                        path.AddBezier(-8, 6, -8, 9, -14, 9, -14, 6);
                        path.CloseFigure();
                        using (Brush white = new SolidBrush(ink)) graphics.FillPath(white, path);
                    }
                    else if (kind == 5) path.AddLine(-6, 0, 6, 0);
                    else if (kind == 7) path.AddLines(new[] { new PointF(-7, 3), new PointF(0, -4), new PointF(7, 3) });
                    if (kind != 4) graphics.DrawPath(pen, path);
                }
                if (kind == 1 && muted || kind == 2 && !view.Discord)
                {
                    using (Pen back = new Pen(fill, 5)) graphics.DrawLine(back, -12, -12, 12, 12);
                    graphics.DrawLine(pen, -12, -12, 12, 12);
                }
            }
            graphics.Restore(state);
        }

        internal static void Avatar(Graphics graphics, Bitmap face, int index, RectangleF art, bool dim, double level, bool lit, bool motion, float ripple, float s)
        {
            using (GraphicsPath circle = new GraphicsPath())
            {
                circle.AddEllipse(art);
                GraphicsState state = graphics.Save();
                graphics.SetClip(circle, CombineMode.Intersect);
                if (index == 1 && face != null) graphics.DrawImage(face, art, new RectangleF(face.Width * 0.25f, face.Height * 0.25f, face.Width * 0.525f, face.Height * 0.525f), GraphicsUnit.Pixel);
                else
                {
                    using (Brush fill = new SolidBrush(index == 2 ? Neon.Deep : Neon.Field)) graphics.FillEllipse(fill, art);
                    graphics.TranslateTransform(art.Left, art.Top);
                    graphics.ScaleTransform(art.Width / 96f, art.Height / 96f);
                    if (index == 2) Headset(graphics); else Person(graphics);
                }
                graphics.Restore(state);
                if (dim) using (Brush shade = new SolidBrush(Color.FromArgb(150, Neon.Well))) graphics.FillEllipse(shade, art);
            }
            float width = RingWidth(level) * s, glow = (4 + 8 * (float)Unit(level)) * s;
            RectangleF ring = RectangleF.Inflate(art, 2 * s, 2 * s);
            for (int i = 3; i >= 1; i--)
                using (Pen haze = new Pen(Color.FromArgb(lit ? 9 + (int)(10 * Unit(level)) : 5, Neon.Cyan), width + glow * i / 3)) graphics.DrawEllipse(haze, ring);
            using (Pen line = new Pen(Color.FromArgb(lit ? 245 : 65, Neon.Cyan), width)) graphics.DrawEllipse(line, ring);
            if (lit && motion)
            {
                RectangleF wave = RectangleF.Inflate(art, art.Width * 0.225f * ripple + 2 * s, art.Height * 0.225f * ripple + 2 * s);
                using (Pen line = new Pen(Color.FromArgb((int)(160 * (1 - ripple)), Neon.Cyan), s)) graphics.DrawEllipse(line, wave);
            }
        }

        private static void Person(Graphics graphics)
        {
            using (Brush rose = new SolidBrush(Neon.Rose))
            {
                using (GraphicsPath head = Neon.Cut(new RectangleF(34, 16, 28, 30), 7, 2, 7, 2)) graphics.FillPath(rose, head);
                graphics.FillPolygon(rose, new[] { new PointF(20, 84), new PointF(20, 64), new PointF(30, 54), new PointF(66, 54), new PointF(76, 64), new PointF(76, 84) });
            }
        }

        private static void Headset(Graphics graphics)
        {
            using (Pen band = new Pen(Neon.Cyan, 6)) graphics.DrawArc(band, new RectangleF(22, 18, 52, 52), 180, 180);
            using (Brush cyan = new SolidBrush(Neon.Cyan))
            {
                using (GraphicsPath cup = Neon.Cut(new RectangleF(15, 44, 17, 28), 4, 1, 4, 1)) graphics.FillPath(cyan, cup);
                using (GraphicsPath cup = Neon.Cut(new RectangleF(64, 44, 17, 28), 4, 1, 4, 1)) graphics.FillPath(cyan, cup);
            }
        }
    }

    internal sealed class CallCaption
    {
        private string drawn;
        private float width;
        internal string[] Lines = new string[0];

        internal void Fit(Graphics graphics, string text, Font font, float room)
        {
            if (drawn == text && width == room) return;
            drawn = text;
            width = room;
            List<string> lines = new List<string>();
            string left = Regex.Replace(text ?? "", @"\s+", " ").Trim();
            while (left.Length > 0 && lines.Count < 2)
            {
                int fit = 0, high = left.Length;
                while (fit < high)
                {
                    int middle = (fit + high + 1) / 2;
                    if (Measure(graphics, left.Substring(0, middle), font) <= room) fit = middle;
                    else high = middle - 1;
                }
                if (fit == 0) break;
                if (fit < left.Length && char.IsHighSurrogate(left[fit - 1])) fit--;
                if (fit == 0) break;
                if (fit < left.Length && lines.Count == 0)
                {
                    int space = left.LastIndexOf(' ', fit - 1, fit);
                    if (space > 0) fit = space;
                }
                if (fit < left.Length && lines.Count == 1)
                {
                    string tail = left.Substring(0, fit).TrimEnd();
                    while (tail.Length > 0 && Measure(graphics, tail + "...", font) > room)
                    {
                        int keep = tail.Length - 1;
                        if (keep > 0 && char.IsHighSurrogate(tail[keep - 1])) keep--;
                        tail = tail.Substring(0, keep).TrimEnd();
                    }
                    lines.Add(tail + "...");
                    left = "";
                }
                else { lines.Add(left.Substring(0, fit).TrimEnd()); left = left.Substring(fit).TrimStart(); }
            }
            Lines = lines.ToArray();
        }

        internal static float Measure(Graphics graphics, string text, Font font) { return graphics.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic).Width; }

        internal void Draw(Graphics graphics, RectangleF box, CallView view, CallFonts fonts, float s, double fade)
        {
            float nameWidth = NameWidth(graphics, view.Who, fonts.Name, s), pad = 12 * s;
            RectangleF speaker = new RectangleF(box.Left + pad, box.Top + 9 * s, nameWidth, 18 * s);
            RectangleF words = new RectangleF(speaker.Right + 4 * s, speaker.Top, box.Width - nameWidth - pad * 2 - 4 * s, 18 * s);
            string text = string.IsNullOrEmpty(view.Said) ? "Captions show here." : view.Said;
            Fit(graphics, text, fonts.Mono, words.Width);
            Color name = view.Who == "YOU" ? Neon.Rose : view.Who == "CAPRI" ? Neon.White : Neon.Cyan;
            if (!string.IsNullOrEmpty(view.Said)) CallInk.Text(graphics, view.Who, fonts.Name, speaker, Color.FromArgb((int)(255 * fade), name));
            else words = new RectangleF(box.Left + pad, words.Top, box.Width - pad * 2, words.Height);
            for (int i = 0; i < Lines.Length; i++)
            {
                RectangleF line = new RectangleF(words.Left, words.Top + i * 17 * s, words.Width, words.Height);
                CallInk.Text(graphics, Lines[i], fonts.Mono, line, Color.FromArgb((int)(255 * fade), Neon.White));
            }
        }

        internal static float NameWidth(Graphics graphics, string who, Font font, float scale) { return Math.Max(64 * scale, Measure(graphics, who ?? "", font) + 2 * scale); }
    }

    internal sealed class CallTips : IDisposable
    {
        private readonly Form owner;
        private readonly float scale;
        private readonly Timer delay = new Timer { Interval = 400 };
        private Mainframe.HintCard card;
        private Func<string> words;
        private Func<Rectangle> box;
        private string shown;

        internal CallTips(Form owner, float scale) { this.owner = owner; this.scale = scale; delay.Tick += delegate { Show(); }; }
        internal void Hover(Func<string> text, Func<Rectangle> at) { Clear(); words = text; box = at; delay.Start(); }
        internal void Refresh() { if (card != null && card.Visible && words != null && shown != words()) Show(); }
        private void Show()
        {
            delay.Stop();
            if (!owner.Visible || words == null || Control.MouseButtons != MouseButtons.None) return;
            if (card == null) card = new Mainframe.HintCard();
            Rectangle at = box();
            shown = words();
            card.TopMost = owner.TopMost;
            if (card.Place(shown, scale, at, Screen.FromRectangle(at).WorkingArea)) { if (!card.Visible) card.Show(owner); else card.Invalidate(); }
            else card.Hide();
        }
        internal void Clear() { delay.Stop(); words = null; box = null; if (card != null && !card.IsDisposed) card.Hide(); }
        public void Dispose() { Clear(); delay.Dispose(); if (card != null) { card.Dispose(); card = null; } }
    }

    internal sealed class CallButton : Control
    {
        internal int Kind;
        internal CallView View;
        private readonly float scale;
        private readonly CallFonts fonts;
        private bool hover;

        internal CallButton(int kind, float scale, CallFonts fonts)
        {
            Kind = kind; this.scale = scale; this.fonts = fonts;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            BackColor = Color.Transparent;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.PushButton;
        }
        internal void Take(CallView view) { string was = View == null ? "" : CallInk.Tip(Kind, View); View = view; string now = CallInk.Tip(Kind, view); AccessibleName = now; if (was != now) Invalidate(); }
        internal void Press() { OnClick(EventArgs.Empty); }
        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
        protected override void OnPaintBackground(PaintEventArgs e) { if (Kind == 6) e.Graphics.Clear(Neon.Well); else Neon.Behind(e.Graphics, this, 210 * scale); }
        protected override void OnPaint(PaintEventArgs e) { CallInk.Quality(e.Graphics); CallInk.Button(e.Graphics, ClientRectangle, Kind, View ?? new CallView(), hover, scale, fonts); }
    }

    internal sealed partial class CallPill : Overlay, LookWindow
    {
        private const int Wide = 540, Tall = 142;
        internal event Action MicPressed, ExpandPressed, EndPressed, AgainPressed;
        private readonly Bitmap face;
        private readonly Func<CallView> read;
        private readonly float s;
        private readonly CallFonts fonts;
        private readonly CallLive live = new CallLive();
        private readonly CallCaption caption = new CallCaption();
        private readonly Timer ticks = new Timer { Interval = 100 };
        private readonly Timer animation = new Timer { Interval = 33 };
        private readonly CallTips tips;
        private Bitmap sheet;
        private int hover, pressed;
        private bool dragging;
        private Point dragAt, dragFrom;
        private string drawn;
        private static readonly float[] bars = { 10, 18, 25, 17, 22, 12 };

        internal CallPill(Bitmap face, Func<CallView> read, float scale = 0) : base("Capri call pill")
        {
            this.face = face;
            this.read = read;
            s = scale > 0 ? scale : DeviceDpi / 96f;
            fonts = new CallFonts(s);
            ClientSize = new Size(Px(Wide), Px(Tall));
            tips = new CallTips(this, s);
            ticks.Tick += delegate { if (this.read != null) Take(this.read()); };
            animation.Tick += delegate { Redraw(); };
        }

        private int Px(float value) { return (int)Math.Round(value * s); }
        private RectangleF Box(float x, float y, float w, float h) { return new RectangleF(x * s, y * s, w * s, h * s); }
        private RectangleF Body { get { return Box(8, 8, 524, 60); } }
        private RectangleF Bubble { get { return Box(8, 80, 440, 54); } }
        internal bool Ended { get { return live.View.Ended; } }
        internal bool Animating { get { return animation.Enabled; } }

        internal RectangleF ButtonBox(int kind)
        {
            if (kind == 1) return Box(352, 16, 44, 44);
            if (kind == 7) return Box(404, 16, 44, 44);
            if (kind == 4) return Box(456, 16, 60, 44);
            if (kind == 6) return Box(390, 18, 126, 40);
            return RectangleF.Empty;
        }

        internal int PartAt(Point point)
        {
            foreach (int part in live.View.Ended ? new[] { 6 } : new[] { 1, 7, 4 })
            {
                RectangleF box = ButtonBox(part);
                using (GraphicsPath path = Look.Rounded(box, box.Height / 2)) if (path.IsVisible(point)) return part;
            }
            return 0;
        }

        internal void Take(CallView next)
        {
            if (next == null) return;
            live.Take(next);
            tips.Refresh();
            animation.Enabled = Visible && live.Animates;
            string state = live.State(true);
            if (state == drawn) return;
            drawn = state;
            if (Visible) Redraw();
        }

        internal void Open()
        {
            if (read != null) Take(read());
            Redraw();
        }

        private void Redraw() { Present(Render(), Location); }

        internal Bitmap Render()
        {
            if (sheet == null) sheet = new Bitmap(Px(Wide), Px(Tall), PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(sheet))
            {
                graphics.Clear(Color.Transparent);
                CallInk.Quality(graphics);
                RectangleF body = Body;
                for (int i = 6; i >= 1; i--)
                {
                    RectangleF shade = RectangleF.Inflate(body, i * s, i * s);
                    shade.Offset(0, 2 * s);
                    CallInk.Round(graphics, shade, shade.Height / 2, Color.FromArgb(5, 0, 0, 0), Color.Transparent, 0);
                }
                CallInk.Round(graphics, body, body.Height / 2, Color.FromArgb(250, Neon.Ground), Neon.Edge, 1.2f * s);
                CallView view = live.View;
                if (view.Ended)
                {
                    CallInk.Avatar(graphics, face, 1, Box(24, 19, 38, 38), true, 0, false, false, 0, s * 38 / 84);
                    CallInk.Text(graphics, "CALL ENDED · " + CallPanel.Clock(view.Length), fonts.Name, Box(84, 17, 294, 20), Neon.Rose);
                    CallInk.Text(graphics, CallPanel.Answered(view.Answers), fonts.Tiny, Box(84, 39, 294, 17), Neon.Faint);
                    CallInk.Button(graphics, ButtonBox(6), 6, view, hover == 6, s, fonts);
                }
                else
                {
                    GraphicsState clipped = graphics.Save();
                    using (GraphicsPath hull = Look.Rounded(body, body.Height / 2)) graphics.SetClip(hull);
                    foreach (int index in new[] { 2, 0, 1 })
                    {
                        bool dim = index == 0 ? !view.Listening || view.Muted : index == 2 && (!view.Discord || view.DiscordNote != null);
                        CallInk.Avatar(graphics, face, index, Box(index == 1 ? 24 : index == 0 ? 52 : 80, 19, 38, 38), dim,
                            live.Level(index), live.Lit(index), view.Motion, live.Ripple(index), s * 38 / 84);
                    }
                    graphics.Restore(clipped);
                    CallInk.Text(graphics, Talking(), fonts.Name, Box(132, 16, 162, 22), Neon.White);
                    using (Brush red = new SolidBrush(Color.FromArgb(CallInk.Dot(view.Motion, CallInk.Time), CallInk.Red))) graphics.FillEllipse(red, Box(134, 45, 7, 7));
                    CallInk.Text(graphics, CallPanel.Clock(view.Length), fonts.Mono, Box(148, 38, 144, 20), Neon.Faint);
                    DrawBars(graphics);
                    foreach (int part in new[] { 1, 7, 4 }) CallInk.Button(graphics, ButtonBox(part), part, view, hover == part, s, fonts);
                    if (view.Recent(DateTime.UtcNow))
                    {
                        CallInk.Round(graphics, Bubble, 12 * s, Color.FromArgb(250, Neon.Well), Neon.WellEdge, s);
                        caption.Draw(graphics, Bubble, view, fonts, s, 1);
                    }
                }
            }
            return sheet;
        }

        private string Talking() { return live.Her ? "CAPRI is talking" : live.You ? "YOU are talking" : live.Discord ? Speaker(live.View.App) : "On call"; }

        internal static string Speaker(CallApp app) { return app.Caption + (app.Name == "Discord" ? " is talking" : " talking"); }

        private void DrawBars(Graphics graphics)
        {
            bool lit = live.Her || live.You || live.Discord;
            double level = live.Her ? live.Level(1) : live.You ? live.Level(0) : live.Level(2);
            using (Pen bar = new Pen(lit ? Neon.Cyan : Color.FromArgb(65, Neon.Faint), 2.2f * s))
            {
                bar.StartCap = bar.EndCap = LineCap.Round;
                for (int i = 0; i < 6; i++)
                {
                    double motion = !live.View.Motion ? 1 : 0.65 + 0.35 * Math.Sin(CallInk.Time * 9 + i * 1.7);
                    float height = lit ? Math.Max(4, bars[i] * (float)((0.25 + 0.75 * level) * motion)) : 3;
                    float x = (306 + i * 5) * s, y = 38 * s;
                    graphics.DrawLine(bar, x, y - height * s / 2, x, y + height * s / 2);
                }
            }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            ticks.Enabled = Visible && read != null;
            animation.Enabled = Visible && live.Animates;
            if (!Visible && tips != null) { tips.Clear(); hover = pressed = 0; dragging = false; Capture = false; }
            base.OnVisibleChanged(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging)
            {
                Point now = Cursor.Position;
                Location = new Point(dragFrom.X + now.X - dragAt.X, dragFrom.Y + now.Y - dragAt.Y);
                return;
            }
            int part = PartAt(e.Location);
            Cursor = part == 0 ? Cursors.SizeAll : Cursors.Hand;
            if (part == hover) return;
            hover = part;
            tips.Clear();
            if (part != 0) tips.Hover(delegate { return CallInk.Tip(hover, live.View); }, delegate { return RectangleToScreen(Rectangle.Round(ButtonBox(hover))); });
            if (Visible) Redraw();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            tips.Clear();
            if (hover == 0) return;
            hover = 0;
            if (Visible) Redraw();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            tips.Clear();
            pressed = PartAt(e.Location);
            Capture = true;
            dragging = pressed == 0;
            dragAt = Cursor.Position;
            dragFrom = Location;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            bool drag = dragging;
            int part = pressed;
            dragging = false;
            pressed = 0;
            Capture = false;
            if (!drag && part != 0 && part == PartAt(e.Location)) Press(part);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
        }

        internal void Press(int part)
        {
            if (live.View.Ended) { if (part == 6 && AgainPressed != null) AgainPressed(); return; }
            if (part == 1 && MicPressed != null) MicPressed();
            if (part == 7 && ExpandPressed != null) ExpandPressed();
            if (part == 4 && EndPressed != null) EndPressed();
        }

        internal static void Smoke(List<string> checks, string folder, Bitmap face, CallView talking)
        {
            CallView[] states =
            {
                talking,
                new CallView { Listening = true, Muted = true, Discord = true, Length = TimeSpan.FromSeconds(754) },
                new CallView { Listening = true, Discord = true, Call = 0.7, Length = TimeSpan.FromSeconds(754), Who = "DISCORD", Said = "Yes, we can hear you.", SaidAt = DateTime.UtcNow },
                new CallView { Ended = true, Answers = 6, Length = TimeSpan.FromSeconds(754) }
            };
            foreach (float scale in new[] { 1f, 1.5f, 2f })
                for (int i = 0; i < states.Length; i++)
                    using (CallPill pill = new CallPill(face, null, scale))
                    {
                        if (!string.IsNullOrEmpty(states[i].Said)) states[i].SaidAt = DateTime.UtcNow;
                        pill.Take(states[i]);
                        Bitmap shot = pill.Render();
                        using (Graphics graphics = Graphics.FromImage(shot))
                        {
                            CallPanel.Inside(pill.ClientRectangle, pill.Body, "pill body");
                            if (states[i].Ended)
                            {
                                CallPanel.Fits(graphics, "CALL ENDED · 100:00:25", pill.fonts.Name, pill.Box(84, 17, 294, 20), "pill ended title");
                                CallPanel.Fits(graphics, CallPanel.Answered(6), pill.fonts.Tiny, pill.Box(84, 39, 294, 17), "pill ended summary");
                                CallPanel.Inside(pill.Body, pill.ButtonBox(6), "pill call again");
                                CallPanel.Fits(graphics, "Call again", pill.fonts.Button, pill.ButtonBox(6), "pill call again");
                            }
                            else
                            {
                                RectangleF words = pill.Box(132, 16, 162, 22), timer = pill.Box(148, 38, 144, 20), bars = pill.Box(304, 24, 30, 28);
                                CallPanel.Inside(pill.Body, pill.Box(14, 9, 114, 58), "pill avatars and ripples");
                                CallPanel.Inside(pill.Body, words, "pill speaker"); CallPanel.Inside(pill.Body, timer, "pill timer"); CallPanel.Inside(pill.Body, bars, "pill bars");
                                CallPanel.Apart(words, bars, "pill words and bars");
                                CallPanel.Apart(words, timer, "pill speaker and timer");
                                foreach (string who in new[] { "CAPRI is talking", "YOU are talking", "DISCORD is talking", "On call" }) CallPanel.Fits(graphics, who, pill.fonts.Name, words, "pill speaker");
                                CallPanel.Fits(graphics, "100:00:25", pill.fonts.Mono, timer, "pill clock");
                                List<RectangleF> buttons = new List<RectangleF>();
                                foreach (int part in new[] { 1, 7, 4 })
                                {
                                    RectangleF box = pill.ButtonBox(part);
                                    CallPanel.Inside(pill.Body, box, "pill button");
                                    CallPanel.Apart(bars, box, "pill bars and button");
                                    foreach (RectangleF other in buttons) CallPanel.Apart(other, box, "pill buttons");
                                    buttons.Add(box);
                                    Point middle = Point.Round(new PointF(box.Left + box.Width / 2, box.Top + box.Height / 2));
                                    if (pill.PartAt(middle) != part || pill.PartAt(Point.Round(box.Location)) != 0) throw new Exception("A call pill hit area moved.");
                                }
                                CallPanel.Inside(pill.ClientRectangle, pill.Bubble, "pill caption bubble");
                                CallPanel.Apart(pill.Body, pill.Bubble, "pill body and captions");
                                CallPanel.CheckCaption(graphics, pill.caption, pill.fonts, pill.Bubble, scale);
                            }
                        }
                        shot = pill.Render();
                        shot.Save(Path.Combine(folder, "call-pill-" + (int)(scale * 100) + "-" + (i + 1) + ".png"));
                        if (!object.ReferenceEquals(shot, pill.Render())) throw new Exception("The call pill allocated another bitmap to repaint.");
                        if (pill.animation.Enabled || pill.ticks.Enabled) throw new Exception("A hidden call pill kept its timers running.");
                        int mic = 0, expand = 0, end = 0, again = 0;
                        pill.MicPressed += delegate { mic++; };
                        pill.ExpandPressed += delegate { expand++; };
                        pill.EndPressed += delegate { end++; };
                        pill.AgainPressed += delegate { again++; };
                        pill.Press(1); pill.Press(7); pill.Press(4); pill.Press(6);
                        if (states[i].Ended ? again != 1 || mic != 0 || expand != 0 || end != 0 : again != 0 || mic != 1 || expand != 1 || end != 1) throw new Exception("A call pill handler fired the wrong action.");
                        states[i].SaidAt = DateTime.UtcNow.AddSeconds(-11);
                        pill.Take(states[i]);
                        if (pill.Render().GetPixel(pill.Px(220), pill.Px(100)).A != 0) throw new Exception("An expired call caption bubble stayed visible.");
                        states[i].SaidAt = DateTime.UtcNow;
                        states[i].Captions = false;
                        pill.Take(states[i]);
                        if (pill.Render().GetPixel(pill.Px(220), pill.Px(100)).A != 0) throw new Exception("Call captions stayed visible while off.");
                        states[i].Captions = true;
                    }
            checks.Add("PASS call pill: four states at 100, 150 and 200 percent, with words and buttons inside, round hit areas, handlers, expired and disabled captions, stopped hidden timers and a reused bitmap; saved for review");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ticks.Dispose(); animation.Dispose(); tips.Dispose(); fonts.Dispose();
                if (sheet != null && sheet != Image) sheet.Dispose();
                sheet = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class CallPanel : Form, LookWindow
    {
        private const int Wide = 614, Inset = 22, Gap = 12, TileY = 54, TileHigh = 202;
        internal const double Talks = 0.01;
        internal event Action MicPressed, DiscordPressed, CaptionsPressed, EndPressed, MinimizePressed, AgainPressed;
        private readonly float s;
        private readonly Bitmap face;
        private readonly Func<CallView> read;
        private readonly CallFonts fonts;
        private readonly CallLive live = new CallLive();
        private readonly CallCaption caption = new CallCaption();
        private readonly Timer ticks = new Timer { Interval = 100 };
        private readonly Timer animation = new Timer { Interval = 33 };
        private readonly CallButton hide, mic, discord, cc, end, again;
        private readonly CallTips tips;
        private string drawn;

        internal CallPanel(Bitmap face, Func<CallView> read, float scale = 0)
        {
            this.face = face;
            this.read = read;
            s = scale > 0 ? scale : DeviceDpi / 96f;
            fonts = new CallFonts(s);
            Text = "Capri call";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Ground;
            ForeColor = Neon.White;
            DoubleBuffered = true;
            using (Stream source = Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Chip.ico")) if (source != null) Icon = new Icon(source);
            tips = new CallTips(this, s);
            hide = Press(5, 30, 24);
            mic = Press(1, 46, 46);
            discord = Press(2, 46, 46);
            cc = Press(3, 46, 46);
            end = Press(4, 92, 46);
            again = Press(6, 128, 38);
            hide.Click += delegate { Minimize(); };
            mic.Click += delegate { if (MicPressed != null) MicPressed(); };
            discord.Click += delegate { if (DiscordPressed != null) DiscordPressed(); };
            cc.Click += delegate { if (CaptionsPressed != null) CaptionsPressed(); };
            end.Click += delegate { if (EndPressed != null) EndPressed(); };
            again.Click += delegate { if (AgainPressed != null) AgainPressed(); };
            LayoutCall();
            ticks.Tick += delegate { if (this.read != null) Take(this.read()); };
            animation.Tick += delegate { Invalidate(); };
            Take(new CallView());
        }

        private int Px(float value) { return (int)Math.Round(value * s); }
        internal float UnitScale { get { return s; } }
        internal bool Ended { get { return live.View.Ended; } }
        internal bool Animating { get { return animation.Enabled; } }

        private CallButton Press(int kind, int width, int height)
        {
            CallButton button = new CallButton(kind, s, fonts) { Size = new Size(Px(width), Px(height)) };
            button.MouseEnter += delegate { tips.Hover(delegate { return CallInk.Tip(kind, live.View); }, delegate { return button.RectangleToScreen(button.ClientRectangle); }); };
            button.MouseLeave += delegate { tips.Clear(); };
            button.MouseDown += delegate { tips.Clear(); };
            Controls.Add(button);
            return button;
        }

        private void LayoutCall()
        {
            CallView view = live.View;
            int row = view.Captions ? 332 : 274;
            ClientSize = new Size(Px(Wide), Px(view.Ended ? 326 : row + 60));
            hide.Location = new Point(ClientSize.Width - Px(44), Px(12));
            end.Location = new Point(ClientSize.Width - Px(Inset) - end.Width, Px(row));
            cc.Location = new Point(end.Left - Px(8) - cc.Width, Px(row));
            discord.Location = new Point(cc.Left - Px(8) - discord.Width, Px(row));
            mic.Location = new Point(discord.Left - Px(8) - mic.Width, Px(row));
            again.Location = new Point((ClientSize.Width - again.Width) / 2, Px(244));
            foreach (Control control in new Control[] { mic, discord, cc, end }) control.Visible = !view.Ended;
            again.Visible = view.Ended;
            Neon.Clip(this, 16 * s, 6 * s);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000080; return value; }
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            ticks.Enabled = Visible && read != null;
            animation.Enabled = Visible && live.Animates;
            if (!Visible && tips != null) tips.Clear();
            base.OnVisibleChanged(e);
        }

        internal void Minimize() { if (MinimizePressed != null) MinimizePressed(); else Hide(); }
        internal void Open() { if (read != null) Take(read()); if (!Visible) Show(); }
        internal void PressButton(int kind)
        {
            foreach (Control control in Controls)
            {
                CallButton button = control as CallButton;
                if (button != null && button.Kind == kind) { button.Press(); return; }
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Minimize(); }
            base.OnFormClosing(e);
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0021) { message.Result = new IntPtr(3); return; }
            base.WndProc(ref message);
            if (message.Msg == 0x0084 && message.Result == new IntPtr(1)) message.Result = new IntPtr(2);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Dispose(); animation.Dispose(); tips.Dispose(); }
            base.Dispose(disposing);
            if (disposing) fonts.Dispose();
        }

        internal void Take(CallView next)
        {
            if (next == null) return;
            bool layout = live.View.Ended != next.Ended || live.View.Captions != next.Captions;
            live.Take(next);
            if (layout) LayoutCall();
            foreach (Control control in Controls) ((CallButton)control).Take(next);
            tips.Refresh();
            animation.Enabled = Visible && live.Animates;
            string state = live.State(false);
            if (state == drawn) return;
            drawn = state;
            Invalidate();
        }

        internal static string Status(CallView view)
        {
            if (!view.Listening) return "She isn't listening.";
            if (view.Muted) return "Muted: she hears no one.";
            if (!view.Discord) return "She hears you only.";
            return view.DiscordNote ?? "She hears you and " + view.App.Name + ".";
        }

        internal static string Clock(TimeSpan length)
        {
            if (length < TimeSpan.Zero) length = TimeSpan.Zero;
            string minutes = length.Minutes.ToString("00") + ":" + length.Seconds.ToString("00");
            return length.TotalHours >= 1 ? (int)length.TotalHours + ":" + minutes : minutes;
        }

        internal static string Short(string note) { return note != null && note.EndsWith(" isn't open.", StringComparison.Ordinal) ? "not open" : "can't hear it"; }
        internal static string Answered(int answers) { return "she answered " + answers + (answers == 1 ? " time" : " times"); }
        internal static string EndLine(CallView view) { return Clock(view.Length) + " · " + Answered(view.Answers); }

        private RectangleF Tile(int index)
        {
            float wide = (Wide - 2 * Inset - 2 * Gap) / 3f;
            return new RectangleF((Inset + index * (wide + Gap)) * s, TileY * s, wide * s, TileHigh * s);
        }

        private RectangleF Art(int index)
        {
            RectangleF tile = Tile(index);
            return new RectangleF(tile.Left + (tile.Width - 84 * s) / 2, tile.Top + 24 * s, 84 * s, 84 * s);
        }

        private RectangleF TileWords(int index, float y, float height) { RectangleF tile = Tile(index); return new RectangleF(tile.Left + 14 * s, tile.Top + y * s, tile.Width - 28 * s, height * s); }
        private RectangleF CaptionBox { get { return new RectangleF(Inset * s, 268 * s, (Wide - 2 * Inset) * s, 54 * s); } }
        private RectangleF StatusBox { get { return new RectangleF(Px(Inset + 2), mic.Top, mic.Left - Px(Inset + 14), mic.Height); } }
        private RectangleF Badge { get { return new RectangleF(hide.Left - Px(192), Px(12), Px(182), Px(27)); } }

        private string Note(int index)
        {
            CallView view = live.View;
            if (live.Lit(index)) return "speaking";
            if (index == 0) return !view.Listening ? "not listening" : view.Muted ? "muted" : "mic on";
            if (index == 1) return view.Thinking ? "thinking" : "listening";
            return !view.Discord ? "off" : view.DiscordNote != null ? Short(view.DiscordNote) : "friends";
        }

        protected override void OnPaintBackground(PaintEventArgs e) { Neon.Backdrop(e.Graphics, ClientSize, 210 * s); }
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics graphics = e.Graphics;
            CallInk.Quality(graphics);
            CallView view = live.View;
            CallInk.Text(graphics, "CALL MODE", fonts.Title, new RectangleF(24 * s, 12 * s, 118 * s, 27 * s), Neon.Rose);
            CallInk.Text(graphics, "BETA", fonts.Note, new RectangleF(148 * s, 17 * s, 44 * s, 20 * s), Neon.Faint);
            RectangleF badge = Badge;
            CallInk.Round(graphics, badge, 7 * s, Neon.Well, Neon.WellEdge, s);
            if (!view.Ended)
                using (Brush red = new SolidBrush(Color.FromArgb(CallInk.Dot(view.Motion, CallInk.Time), CallInk.Red))) graphics.FillEllipse(red, badge.Left + 10 * s, badge.Top + 10 * s, 7 * s, 7 * s);
            CallInk.Text(graphics, view.Ended ? "ENDED" : "ON CALL", fonts.Tiny, new RectangleF(badge.Left + 25 * s, badge.Top, 57 * s, badge.Height), Neon.White);
            CallInk.Text(graphics, Clock(view.Length), fonts.Mono, new RectangleF(badge.Left + 88 * s, badge.Top, badge.Width - 96 * s, badge.Height), Neon.White, true);
            if (view.Ended) DrawEnded(graphics);
            else
            {
                for (int i = 0; i < 3; i++) DrawTile(graphics, i);
                if (view.Captions)
                {
                    using (GraphicsPath strip = Neon.Cut(CaptionBox, 6 * s, 2 * s, 6 * s, 2 * s))
                    using (Brush fill = new SolidBrush(Neon.Well)) graphics.FillPath(fill, strip);
                    caption.Draw(graphics, CaptionBox, view, fonts, s, CallInk.Fade(view, DateTime.UtcNow));
                }
                using (StringFormat wrap = new StringFormat { LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisWord, FormatFlags = StringFormatFlags.LineLimit })
                using (Brush ink = new SolidBrush(Neon.Faint)) graphics.DrawString(Status(view), fonts.Words, ink, StatusBox, wrap);
            }
            Neon.Frame(graphics, ClientSize, s, 16 * s, 6 * s, 52 * s, 26 * s);
        }

        private void DrawTile(Graphics graphics, int index)
        {
            CallView view = live.View;
            bool lit = live.Lit(index), dim = index == 0 ? !view.Listening || view.Muted : index == 2 && (!view.Discord || view.DiscordNote != null);
            RectangleF tile = Tile(index);
            using (GraphicsPath edge = Neon.Cut(tile, 10 * s, 3 * s, 10 * s, 3 * s))
            {
                using (Brush fill = new SolidBrush(Neon.Well)) graphics.FillPath(fill, edge);
                using (Pen line = new Pen(lit ? Neon.Cyan : Neon.WellEdge, lit ? 2 * s : Math.Max(1, s))) graphics.DrawPath(line, edge);
            }
            CallInk.Avatar(graphics, face, index, Art(index), dim, live.Level(index), lit, view.Motion, live.Ripple(index), s);
            CallInk.Text(graphics, index == 0 ? "YOU" : index == 1 ? "CAPRI" : view.App.Caption, fonts.Name, TileWords(index, 132, 20), Neon.White, true);
            Color note = index == 0 && view.Listening && view.Muted || index == 2 && view.Discord && view.DiscordNote != null ? Neon.Hot : lit ? Neon.Cyan : Neon.Faint;
            CallInk.Text(graphics, Note(index), fonts.Note, TileWords(index, 154, 18), note, true);
            if (index != 1) return;
            CallInk.Text(graphics, view.InMic ? "IN YOUR MIC" : "NOT IN YOUR MIC", fonts.Tiny, TileWords(index, 175, 14), view.InMic ? Neon.Cyan : Neon.Faint, true);
            RectangleF meter = TileWords(index, 192, 3);
            CallInk.Round(graphics, meter, 1.5f * s, Neon.Field, Neon.WellEdge, 0);
            float width = meter.Width * (float)(view.InMic ? CallInk.Loud(view.Mic) : 0);
            if (width > 0) CallInk.Round(graphics, new RectangleF(meter.Left, meter.Top, width, meter.Height), Math.Min(width, meter.Height) / 2, Neon.Cyan, Neon.Cyan, 0);
        }

        private void DrawEnded(Graphics graphics)
        {
            RectangleF card = new RectangleF(22 * s, 54 * s, 570 * s, 248 * s);
            using (GraphicsPath edge = Neon.Cut(card, 10 * s, 3 * s, 10 * s, 3 * s))
            {
                using (Brush fill = new SolidBrush(Neon.Well)) graphics.FillPath(fill, edge);
                using (Pen line = new Pen(Neon.WellEdge, s)) graphics.DrawPath(line, edge);
            }
            CallInk.Avatar(graphics, face, 1, new RectangleF(271 * s, 75 * s, 72 * s, 72 * s), true, 0, false, false, 0, s);
            CallInk.Text(graphics, "CALL ENDED", fonts.Title, new RectangleF(80 * s, 164 * s, 454 * s, 28 * s), Neon.Rose, true);
            CallInk.Text(graphics, EndLine(live.View), fonts.Mono, new RectangleF(60 * s, 200 * s, 494 * s, 22 * s), Neon.Faint, true);
        }

        internal static void Fits(Graphics graphics, string text, Font font, RectangleF box, string name)
        {
            if (CallCaption.Measure(graphics, text, font) > box.Width + 0.5f || font.GetHeight(graphics) > box.Height + 0.5f) throw new Exception("The call " + name + " does not fit.");
        }

        internal static void Inside(RectangleF outer, RectangleF inner, string name)
        {
            if (!outer.Contains(inner)) throw new Exception("The call " + name + " is outside its window.");
        }

        internal static void Apart(RectangleF one, RectangleF two, string name)
        {
            if (one.IntersectsWith(two)) throw new Exception("The call " + name + " overlaps.");
        }

        internal string SourceName { get { return live.View.App.Caption; } }

        internal void CheckLayout(Graphics graphics)
        {
            CallView view = live.View;
            RectangleF window = ClientRectangle;
            List<RectangleF> buttons = new List<RectangleF>();
            foreach (Control control in Controls)
            {
                CallButton button = (CallButton)control;
                if (view.Ended ? button.Kind != 5 && button.Kind != 6 : button.Kind == 6) continue;
                Inside(window, button.Bounds, "panel button");
                foreach (RectangleF other in buttons) Apart(other, button.Bounds, "panel buttons");
                buttons.Add(button.Bounds);
            }
            Fits(graphics, "CALL MODE", fonts.Title, new RectangleF(24 * s, 12 * s, 118 * s, 27 * s), "title");
            Fits(graphics, "BETA", fonts.Note, new RectangleF(148 * s, 17 * s, 44 * s, 20 * s), "beta label");
            Inside(window, Badge, "badge");
            Apart(Badge, hide.Bounds, "badge and minimize");
            Fits(graphics, "ON CALL", fonts.Tiny, new RectangleF(0, 0, 57 * s, Badge.Height), "badge label");
            Fits(graphics, Clock(TimeSpan.FromSeconds(360025)), fonts.Mono, new RectangleF(0, 0, Badge.Width - 96 * s, Badge.Height), "badge clock");
            if (view.Ended)
            {
                Fits(graphics, "CALL ENDED", fonts.Title, new RectangleF(80 * s, 164 * s, 454 * s, 28 * s), "ended title");
                Fits(graphics, EndLine(view), fonts.Mono, new RectangleF(60 * s, 200 * s, 494 * s, 22 * s), "ended summary");
                Fits(graphics, "Call again", fonts.Button, again.Bounds, "call again");
                Apart(new RectangleF(60 * s, 200 * s, 494 * s, 22 * s), again.Bounds, "ended summary and button");
                return;
            }
            for (int i = 0; i < 3; i++)
            {
                RectangleF tile = Tile(i), name = TileWords(i, 132, 20), note = TileWords(i, 154, 18);
                RectangleF ring = RectangleF.Inflate(Art(i), 22 * s, 22 * s);
                Inside(window, tile, "tile");
                if (i > 0) Apart(Tile(i - 1), tile, "tiles");
                foreach (RectangleF item in new[] { ring, name, note }) Inside(tile, item, "tile content");
                Apart(ring, name, "ring and name");
                Apart(name, note, "name and note");
                Fits(graphics, i == 0 ? "YOU" : i == 1 ? "CAPRI" : view.App.Caption, fonts.Name, name, "tile name");
                foreach (string text in i == 0 ? new[] { "speaking", "mic on", "muted", "not listening" } : i == 1 ? new[] { "speaking", "listening", "thinking" } : new[] { "speaking", "friends", "off", "not open", "can't hear it" })
                    Fits(graphics, text, fonts.Note, note, "tile note");
                if (i == 1)
                {
                    RectangleF label = TileWords(i, 175, 14), meter = TileWords(i, 192, 3);
                    Inside(tile, label, "meter label"); Inside(tile, meter, "meter");
                    Apart(note, label, "note and meter label"); Apart(label, meter, "meter label and meter");
                    Fits(graphics, "NOT IN YOUR MIC", fonts.Tiny, label, "meter label");
                }
            }
            Inside(window, StatusBox, "status");
            foreach (RectangleF button in buttons) Apart(StatusBox, button, "status and buttons");
            using (StringFormat wrap = new StringFormat { FormatFlags = StringFormatFlags.LineLimit })
            {
                foreach (string text in new[] { Status(view), "She hears you and Discord.", "Windows speech can't hear Discord. Pick her own ears under VOICE." })
                {
                    SizeF need = graphics.MeasureString(text, fonts.Words, new SizeF(StatusBox.Width, 10000), wrap);
                    if (need.Height > StatusBox.Height) throw new Exception("The call panel status does not fit.");
                }
            }
            if (view.Captions)
            {
                Inside(window, CaptionBox, "captions");
                Apart(Tile(0), CaptionBox, "tiles and captions");
                Apart(CaptionBox, StatusBox, "captions and status");
                CheckCaption(graphics, caption, fonts, CaptionBox, s);
            }
            if (ClientSize.Height >= 400 * s) throw new Exception("The call panel grew past 400 pixels.");
        }

        internal static void CheckCaption(Graphics graphics, CallCaption caption, CallFonts fonts, RectangleF box, float scale, string who = "DISCORD")
        {
            RectangleF speaker = new RectangleF(box.Left + 12 * scale, box.Top + 9 * scale, CallCaption.NameWidth(graphics, who, fonts.Name, scale), 18 * scale);
            RectangleF words = new RectangleF(speaker.Right + 4 * scale, speaker.Top, box.Right - 12 * scale - speaker.Right - 4 * scale, 35 * scale);
            Inside(box, speaker, "caption speaker"); Inside(box, words, "caption words"); Apart(speaker, words, "caption speaker and words");
            foreach (string name in new[] { "YOU", who, "CAPRI" }) Fits(graphics, name, fonts.Name, speaker, "caption speaker");
            string longest = "I heard the whole call. " + new string('W', 120) + " and there are more words to show on the next line, with an ellipsis at the end.";
            caption.Fit(graphics, longest, fonts.Mono, words.Width);
            if (caption.Lines.Length != 2 || !caption.Lines[1].EndsWith("...")) throw new Exception("Call captions did not stop at two lines with an ellipsis.");
            for (int i = 0; i < caption.Lines.Length; i++)
                Fits(graphics, caption.Lines[i], fonts.Mono, new RectangleF(words.Left, words.Top + i * 17 * scale, words.Width, 18 * scale), "caption line");
            if (fonts.Mono.GetHeight(graphics) > 17 * scale) throw new Exception("Call caption lines overlap.");
        }

        internal static void Smoke(List<string> checks, string folder, Bitmap face)
        {
            DateTime now = DateTime.UtcNow;
            CallView[] states =
            {
                new CallView { Listening = true, Discord = true, You = 0.6, Length = TimeSpan.FromSeconds(754) },
                new CallView { Listening = true, Discord = true, Talking = true, Her = 0.95, InMic = true, Mic = 0.68, Who = "CAPRI",
                    Said = "I heard you. Keep talking while I listen to the call, and I'll answer when you're ready. We can stay here a little longer.", SaidAt = now, Length = TimeSpan.FromSeconds(3725) },
                new CallView { Listening = true, Muted = true, Discord = true, DiscordNote = CallEars.NotOpen, Length = TimeSpan.FromSeconds(42) },
                new CallView { Discord = false, Captions = false },
                new CallView { Listening = true, Discord = true, Thinking = true, Who = "DISCORD", Said = "Can you hear the rest of us?", SaidAt = now, Length = TimeSpan.FromSeconds(754) },
                new CallView { Ended = true, Length = TimeSpan.FromSeconds(754), Answers = 6 }
            };
            Directory.CreateDirectory(folder);
            foreach (float scale in new[] { 1f, 1.5f, 2f })
            {
                using (Mainframe.HintCard tip = new Mainframe.HintCard())
                    foreach (string text in new[] { "Mute", "Unmute", "Start listening", "Turn Discord off", "Turn Discord on", "Turn captions off", "Turn captions on", "End call" })
                        foreach (int y in new[] { 0, 940 })
                            if (!tip.Place(text, scale, new Rectangle(120, y, 92, 46), new Rectangle(0, 0, 1920, 1080)) || tip.AccessibleName != text) throw new Exception("A call hover tip did not fit by its button.");
                for (int i = 0; i < states.Length; i++)
                    using (CallPanel panel = new CallPanel(face, null, scale))
                    using (Bitmap whole = new Bitmap(panel.Px(Wide), panel.Px(392)))
                    {
                        panel.Take(states[i]);
                        using (Graphics graphics = Graphics.FromImage(whole)) panel.CheckLayout(graphics);
                        using (Bitmap shot = new Bitmap(panel.Width, panel.Height))
                        using (Bitmap cut = new Bitmap(panel.Width, panel.Height))
                        {
                            panel.DrawToBitmap(shot, panel.ClientRectangle);
                            using (Graphics graphics = Graphics.FromImage(shot))
                                foreach (Control control in panel.Controls)
                                {
                                    CallButton button = (CallButton)control;
                                    if (states[i].Ended ? button.Kind != 5 && button.Kind != 6 : button.Kind == 6) continue;
                                    using (Bitmap piece = new Bitmap(control.Width, control.Height))
                                    {
                                        control.DrawToBitmap(piece, control.ClientRectangle);
                                        graphics.DrawImage(piece, control.Location);
                                    }
                                }
                            using (Graphics graphics = Graphics.FromImage(cut))
                            using (GraphicsPath hull = Neon.Cut(new RectangleF(0, 0, panel.Width, panel.Height), 16 * scale, 6 * scale, 16 * scale, 6 * scale))
                            {
                                graphics.SetClip(hull);
                                graphics.DrawImage(shot, 0, 0);
                            }
                            cut.Save(Path.Combine(folder, "call-panel-" + (int)(scale * 100) + "-" + (i + 1) + ".png"));
                        }
                        if (i == 0)
                        {
                            string drawn = panel.drawn;
                            panel.Take(states[i]);
                            if (panel.drawn != drawn) throw new Exception("An unchanged call panel changed its drawn state.");
                            if (panel.animation.Enabled || panel.ticks.Enabled) throw new Exception("A hidden call panel kept its timers running.");
                        }
                        int calls = 0;
                        panel.AgainPressed += delegate { calls++; };
                        panel.PressButton(6);
                        if (calls != 1) throw new Exception("The panel call again handler did not fire.");
                    }
            }
            if (CallInk.RingWidth(0) >= CallInk.RingWidth(1)) throw new Exception("Call rings do not grow with loudness.");
            if (CallInk.Loud(0) != 0 || CallInk.Loud(1) != 1 || CallInk.Loud(0.1) < 0.7 || CallInk.Loud(0.1) > 0.85 || CallInk.Loud(0.01) < 0.15 || CallInk.Loud(0.01) > 0.3) throw new Exception("Call levels are not on a decibel scale: " + CallInk.Loud(0.1) + ", " + CallInk.Loud(0.01) + ".");
            if (EndLine(new CallView { Length = TimeSpan.FromSeconds(754), Answers = 1 }) != "12:34 · she answered 1 time"
                || EndLine(new CallView { Length = TimeSpan.FromSeconds(754), Answers = 6 }) != "12:34 · she answered 6 times"
                || Clock(TimeSpan.FromSeconds(3725)) != "1:02:05" || Clock(TimeSpan.FromSeconds(-1)) != "00:00") throw new Exception("The ended call wording or clock changed.");
            if (Short(CallEars.NotOpen) != "not open" || Short("failed") != "can't hear it"
                || Status(states[2]) != "Muted: she hears no one." || Status(states[3]) != "She isn't listening.") throw new Exception("Call status or Discord notes changed.");
            if (CallInk.Dot(false, 0) != 255 || CallInk.Dot(false, 0.7) != 255 || CallInk.Dot(true, 0) != 255 || CallInk.Dot(true, 0.7) > 91) throw new Exception("The call dot does not follow Motion.");
            if (CallInk.Fade(states[1], now.AddSeconds(9)) != 1 || Math.Abs(CallInk.Fade(states[1], now.AddSeconds(12)) - 0.4) > 0.001
                || !states[1].Recent(now.AddSeconds(9)) || states[1].Recent(now.AddSeconds(10))) throw new Exception("Call caption fading or expiry changed.");
            if (CallInk.Tip(1, states[0]) != "Mute" || CallInk.Tip(1, states[2]) != "Unmute" || CallInk.Tip(1, states[3]) != "Start listening"
                || CallInk.Tip(2, states[0]) != "Turn Discord off" || CallInk.Tip(2, states[3]) != "Turn Discord on"
                || CallInk.Tip(3, states[0]) != "Turn captions off" || CallInk.Tip(3, states[3]) != "Turn captions on" || CallInk.Tip(4, states[0]) != "End call") throw new Exception("Call button tips changed.");
            CallLive still = new CallLive();
            still.Take(new CallView { Motion = false, Captions = false });
            string same = still.State(true);
            still.Take(new CallView { Motion = false, Captions = false, Thinking = true, Mic = 0.7, InMic = true, Said = "Hidden caption.", Who = "YOU", SaidAt = now });
            if (still.State(true) != same) throw new Exception("Hidden pill details caused a redraw.");
            CallPill.Smoke(checks, folder, face, states[1]);
            checks.Add("PASS call panel: six states at 100, 150 and 200 percent, with tiles, ripples, words, meter, two caption lines, status and buttons inside their places; saved for review");
            checks.Add("PASS call rings grow with loudness, the dot respects Motion, captions fade and expire, button tips follow their actions, and ended summaries handle one answer, six answers and hours");
        }
    }
}
