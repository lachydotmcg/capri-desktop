using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal enum TalkState { Off, Thinking, Talking, Online, Dormant }

    internal static class Look
    {
        internal static readonly Color Panel = Color.FromArgb(9, 11, 15);
        internal static readonly Color Cyan = Color.FromArgb(58, 214, 235);
        internal static readonly Color Red = Color.FromArgb(232, 36, 64);
        internal static readonly Color Yellow = Color.FromArgb(244, 214, 70);
        internal static readonly Color Neon = Color.FromArgb(56, 176, 255);
        internal static readonly Color Ink = Color.FromArgb(232, 236, 240);
        internal static readonly Color Dim = Color.FromArgb(122, 128, 138);

        internal static readonly Color Pink = Color.FromArgb(255, 46, 136);
        private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private static IntPtr fontMemory;
        private static readonly FontFamily embedded = Embedded("Capri.Font.Mono.ttf");
        private static Cursor pointer;

        [StructLayout(LayoutKind.Sequential)] private struct IconInfo { internal bool Icon; internal int X; internal int Y; internal IntPtr Mask; internal IntPtr Colour; }
        [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr icon, ref IconInfo info);
        [DllImport("user32.dll")] private static extern IntPtr CreateIconIndirect(ref IconInfo info);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);

        internal static Cursor Pointer
        {
            get
            {
                if (pointer != null) return pointer;
                float scale;
                using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, screen.DpiX / 96f);
                int size = (int)Math.Round(32 * scale);
                using (Bitmap art = PointerArt(size))
                {
                    IntPtr icon = art.GetHicon();
                    IconInfo info = new IconInfo();
                    GetIconInfo(icon, ref info);
                    info.Icon = false;
                    info.X = (int)Math.Round(2 * scale);
                    info.Y = (int)Math.Round(2 * scale);
                    IntPtr handle = CreateIconIndirect(ref info);
                    DeleteObject(info.Mask);
                    DeleteObject(info.Colour);
                    DestroyIcon(icon);
                    pointer = handle == IntPtr.Zero ? Cursors.Hand : new Cursor(handle);
                }
                return pointer;
            }
        }

        // Her artist's cursor, with its tip where the old arrow's was, so her clicks land the same place.
        private static Bitmap ArtistPointer(int size)
        {
            using (System.IO.Stream stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("Capri.Cursor.png"))
            {
                if (stream == null) return null;
                using (Bitmap source = new Bitmap(stream))
                {
                    Bitmap art = new Bitmap(size, size, PixelFormat.Format32bppArgb);
                    float fit = size * 30f / 32 / Math.Max(source.Width, source.Height), inset = size * 2f / 32;
                    using (Graphics graphics = Graphics.FromImage(art))
                    {
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(source, inset, inset, source.Width * fit, source.Height * fit);
                    }
                    return art;
                }
            }
        }

        internal static Bitmap PointerArt(int size)
        {
            Bitmap artist = ArtistPointer(size);
            if (artist != null) return artist;
            float s = size / 32f;
            PointF[] arrow = { new PointF(2, 2), new PointF(2, 24), new PointF(8, 18.5f), new PointF(12, 27), new PointF(16, 25.2f), new PointF(12, 16.8f), new PointF(19.5f, 16.8f) };
            Bitmap art = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Bitmap whole = new Bitmap(size, size, PixelFormat.Format32bppArgb))
            {
                using (Graphics graphics = Graphics.FromImage(whole))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    using (Brush cyan = new SolidBrush(Color.FromArgb(200, Cyan))) graphics.FillPolygon(cyan, Shift(arrow, 1.6f, 0.4f, s));
                    using (Brush red = new SolidBrush(Color.FromArgb(170, Red))) graphics.FillPolygon(red, Shift(arrow, -1.1f, -0.3f, s));
                    PointF[] body = Shift(arrow, 0, 0, s);
                    using (Brush pink = new SolidBrush(Pink)) graphics.FillPolygon(pink, body);
                    using (Pen edge = new Pen(Color.FromArgb(20, 6, 14), 1.3f * s)) { edge.LineJoin = LineJoin.Round; graphics.DrawPolygon(edge, body); }
                    using (Pen shine = new Pen(Color.FromArgb(150, 255, 214, 236), 1f * s)) graphics.DrawLine(shine, 3.6f * s, 5.5f * s, 3.6f * s, 17 * s);
                }
                using (Graphics graphics = Graphics.FromImage(art))
                {
                    int cut = (int)Math.Round(11 * s);
                    int band = Math.Max(2, (int)Math.Round(2.5f * s));
                    int slip = Math.Max(1, (int)Math.Round(2.5f * s));
                    graphics.DrawImage(whole, new Rectangle(0, 0, size, cut), new Rectangle(0, 0, size, cut), GraphicsUnit.Pixel);
                    graphics.DrawImage(whole, new Rectangle(slip, cut, size - slip, band), new Rectangle(0, cut, size - slip, band), GraphicsUnit.Pixel);
                    graphics.DrawImage(whole, new Rectangle(0, cut + band, size, size - cut - band), new Rectangle(0, cut + band, size, size - cut - band), GraphicsUnit.Pixel);
                }
            }
            return art;
        }

        private static PointF[] Shift(PointF[] points, float x, float y, float s)
        {
            PointF[] moved = new PointF[points.Length];
            for (int i = 0; i < points.Length; i++) moved[i] = new PointF((points[i].X + x) * s, (points[i].Y + y) * s);
            return moved;
        }

        [DllImport("gdi32.dll")] private static extern IntPtr AddFontMemResourceEx(IntPtr font, uint length, IntPtr reserved, ref uint count);
        [DllImport("gdi32.dll")] private static extern bool RemoveFontMemResourceEx(IntPtr font);

        private static FontFamily Embedded(string name)
        {
            IntPtr memory = IntPtr.Zero, resource = IntPtr.Zero;
            try
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                {
                    if (stream == null) return null;
                    byte[] data = new byte[stream.Length];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int took = stream.Read(data, read, data.Length - read);
                        if (took == 0) return null;
                        read += took;
                    }
                    memory = Marshal.AllocCoTaskMem(data.Length);
                    Marshal.Copy(data, 0, memory, data.Length);
                    uint count = 0;
                    resource = AddFontMemResourceEx(memory, (uint)data.Length, IntPtr.Zero, ref count);
                    if (resource == IntPtr.Zero || count == 0) return null;
                    fonts.AddMemoryFont(memory, data.Length);
                    if (fonts.Families.Length == 0) return null;
                    fontMemory = memory;
                    return fonts.Families[0];
                }
            }
            catch (ArgumentException) { return null; }
            catch (ExternalException) { return null; }
            catch (IOException) { return null; }
            finally
            {
                if (memory != IntPtr.Zero && memory != fontMemory)
                {
                    if (resource != IntPtr.Zero) RemoveFontMemResourceEx(resource);
                    Marshal.FreeCoTaskMem(memory);
                }
            }
        }

        private const string Extras = "ıŁłŒœŠšŸŽžƒ–—‘’‚“”„†‡•…‰‹›€™";

        internal static bool Drawable(string text)
        {
            foreach (char c in text ?? "") if (c > 0xFF && Extras.IndexOf(c) < 0) return false;
            return true;
        }

        internal static Font Mono(float size, FontStyle style, GraphicsUnit unit = GraphicsUnit.Pixel, string text = null)
        {
            if (embedded != null && Drawable(text)) return new Font(embedded, size, embedded.IsStyleAvailable(style) ? style : FontStyle.Regular, unit);
            return FallbackMono(size, style, unit);
        }

        internal static Font FallbackMono(float size, FontStyle style, GraphicsUnit unit)
        {
            foreach (string family in new[] { "Cascadia Mono", "Consolas", "Courier New" })
            {
                Font font = new Font(family, size, style, unit);
                if (font.Name == family) return font;
                font.Dispose();
            }
            return new Font(FontFamily.GenericMonospace, size, style, unit);
        }

        internal static float CharWidth(Font font)
        {
            using (Bitmap scratch = new Bitmap(1, 1))
            using (Graphics graphics = Graphics.FromImage(scratch))
                return graphics.MeasureString(new string('M', 20), font, PointF.Empty, StringFormat.GenericTypographic).Width / 20f;
        }

        internal static GraphicsPath Plate(RectangleF box, float cut, float tipX, float tipY, float tail, bool both = false)
        {
            float right = both ? cut : 0;
            List<PointF> points = new List<PointF>();
            float near = Math.Max(box.Left + cut + 2, tipX - tail * 0.8f);
            float far = Math.Min(box.Right - right - 2, tipX + tail * 0.2f);
            points.Add(new PointF(box.Left + cut, box.Top));
            if (tipY < box.Top) { points.Add(new PointF(near, box.Top)); points.Add(new PointF(tipX, tipY)); points.Add(new PointF(far, box.Top)); }
            points.Add(new PointF(box.Right - right, box.Top));
            if (both) points.Add(new PointF(box.Right, box.Top + cut));
            if (both) points.Add(new PointF(box.Right, box.Bottom - cut));
            points.Add(new PointF(box.Right - right, box.Bottom));
            if (tipY > box.Bottom) { points.Add(new PointF(far, box.Bottom)); points.Add(new PointF(tipX, tipY)); points.Add(new PointF(near, box.Bottom)); }
            points.Add(new PointF(box.Left + cut, box.Bottom));
            points.Add(new PointF(box.Left, box.Bottom - cut));
            points.Add(new PointF(box.Left, box.Top + cut));
            GraphicsPath path = new GraphicsPath();
            path.AddPolygon(points.ToArray());
            return path;
        }

        internal static GraphicsPath Rounded(RectangleF box, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(box.Left, box.Top, d, d, 180, 90);
            path.AddArc(box.Right - d, box.Top, d, d, 270, 90);
            path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
            path.AddArc(box.Left, box.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        // How many columns a character takes in her mono bubbles: Chinese, Japanese and Korean characters and full-width forms
        // take two, Thai vowel and tone marks (they sit above or below a letter) none, everything else one.
        internal static int Cells(char c)
        {
            if (c == 0x0E31 || (c >= 0x0E34 && c <= 0x0E3A) || (c >= 0x0E47 && c <= 0x0E4E)) return 0;
            return (c >= 0x1100 && c <= 0x115F) || (c >= 0x2E80 && c <= 0x303E) || (c >= 0x3041 && c <= 0x33FF) || (c >= 0x3400 && c <= 0x4DBF) || (c >= 0x4E00 && c <= 0x9FFF)
                || (c >= 0xA000 && c <= 0xA4CF) || (c >= 0xA960 && c <= 0xA97F) || (c >= 0xAC00 && c <= 0xD7A3) || (c >= 0xF900 && c <= 0xFAFF) || (c >= 0xFE10 && c <= 0xFE19)
                || (c >= 0xFE30 && c <= 0xFE6F) || (c >= 0xFF00 && c <= 0xFF60) || (c >= 0xFFE0 && c <= 0xFFE6) ? 2 : 1;
        }

        internal static int Cells(string text)
        {
            if (Indic(text)) return (int)Math.Ceiling(Span(text) - 0.01);
            int cells = 0;
            foreach (char c in text ?? "") cells += Cells(c);
            return cells;
        }

        // True when the text has an Indian script in it, Devanagari to Malayalam, or Ethiopic (Amharic), Lao, Khmer, Burmese,
        // Georgian or Armenian.
        internal static bool Indic(string text)
        {
            foreach (char c in text ?? "")
                if ((c >= 0x0900 && c <= 0x0DFF) || (c >= 0x1200 && c <= 0x139F) || (c >= 0x0E80 && c <= 0x0EFF) || (c >= 0x1780 && c <= 0x17FF) || (c >= 0x19E0 && c <= 0x19FF) || Myanmar(c)
                    || (c >= 0x10A0 && c <= 0x10FF) || (c >= 0x1C90 && c <= 0x1CBF) || (c >= 0x0530 && c <= 0x058F)) return true;
            return false;
        }

        // True when the text has Burmese in it.
        internal static bool Burmese(string text)
        {
            foreach (char c in text ?? "") if (Myanmar(c)) return true;
            return false;
        }

        private static bool Myanmar(char c) { return (c >= 0x1000 && c <= 0x109F) || (c >= 0xA9E0 && c <= 0xA9FF) || (c >= 0xAA60 && c <= 0xAA7F); }

        // Her mono font has no stand-in for Burmese, so GDI+ would draw nothing there. Burmese draws in Myanmar Text instead,
        // on the same baseline as the rest of the line, which stays in her font.
        internal static Font Myanmar(Font font)
        {
            foreach (string family in new[] { "Myanmar Text", "Segoe UI" })
            {
                Font found = new Font(family, font.Size, font.Style, font.Unit);
                if (found.Name == family) return found;
                found.Dispose();
            }
            return new Font(FontFamily.GenericSansSerif, font.Size, font.Style, font.Unit);
        }

        // Draws a line like DrawString does, with any Burmese in Myanmar Text. Returns where a Burmese line ends, or x for
        // any other line, which draws exactly as before.
        internal static float Draw(Graphics graphics, string text, Font font, Brush brush, float x, float y)
        {
            if (!Burmese(text)) { graphics.DrawString(text, font, brush, x, y, StringFormat.GenericTypographic); return x; }
            using (Font burmese = Myanmar(font))
            {
                float lift = Ascent(burmese) - Ascent(font);
                foreach (string piece in Pieces(text))
                {
                    Font used = Myanmar(piece[0]) ? burmese : font;
                    graphics.DrawString(piece, used, brush, x, used == font ? y : y - lift, StringFormat.GenericTypographic);
                    x += graphics.MeasureString(piece, used, PointF.Empty, Spaced).Width;
                }
            }
            return x;
        }

        private static float Mixed(Graphics graphics, string text, Font font, Font burmese)
        {
            float width = 0;
            foreach (string piece in Pieces(text)) width += graphics.MeasureString(piece, Myanmar(piece[0]) ? burmese : font, PointF.Empty, Spaced).Width;
            return width;
        }

        // The text in turns of Burmese and everything else.
        private static List<string> Pieces(string text)
        {
            List<string> pieces = new List<string>();
            int start = 0;
            for (int i = 1; i <= text.Length; i++)
                if (i == text.Length || Myanmar(text[i]) != Myanmar(text[start])) { pieces.Add(text.Substring(start, i - start)); start = i; }
            return pieces;
        }

        private static float Ascent(Font font) { return font.Size * font.FontFamily.GetCellAscent(font.Style) / font.FontFamily.GetEmHeight(font.Style); }

        // Indian scripts, Lao and Khmer join letters and marks into one shape, and Tamil, Kannada, Malayalam, Ethiopic, Georgian
        // and Armenian draw wider than her columns, so their words are measured in her bubble's font instead of counted, and
        // Burmese in Myanmar Text, which draws it. Other words still count by letter.
        private static readonly Dictionary<string, double> measured = new Dictionary<string, double>();
        private static Bitmap rulerPage;
        private static Graphics ruler;
        private static Font rulerFont;
        private static Font rulerBurmese;
        private static float rulerColumn;

        private static double Span(string text)
        {
            double cells = 0;
            string[] words = text.Split(' ');
            foreach (string word in words)
            {
                if (!Indic(word)) { foreach (char c in word) cells += Cells(c); continue; }
                lock (measured)
                {
                    double width;
                    if (!measured.TryGetValue(word, out width))
                    {
                        if (ruler == null)
                        {
                            rulerPage = new Bitmap(1, 1);
                            ruler = Graphics.FromImage(rulerPage);
                            ruler.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                            rulerFont = Mono(14f, FontStyle.Regular, GraphicsUnit.Pixel, "क");
                            rulerColumn = ruler.MeasureString(new string('M', 20), rulerFont, PointF.Empty, StringFormat.GenericTypographic).Width / 20f;
                            rulerBurmese = Myanmar(rulerFont);
                        }
                        // A shade over, since hinting at other sizes can draw a little wider.
                        width = (Burmese(word) ? Mixed(ruler, word, rulerFont, rulerBurmese) : ruler.MeasureString(word, rulerFont, PointF.Empty, StringFormat.GenericTypographic).Width) / rulerColumn * 1.03;
                        if (measured.Count > 4000) measured.Clear();
                        measured[word] = width;
                    }
                    cells += width;
                }
            }
            return cells + words.Length - 1;
        }

        // True when the first letter is Arabic, Urdu, Persian or Hebrew, so the line reads right to left. Digits and
        // punctuation don't decide it.
        internal static bool RightToLeft(string text)
        {
            foreach (char c in text ?? "")
            {
                if ((c >= 0x0590 && c <= 0x08FF) || (c >= 0xFB1D && c <= 0xFDFF) || (c >= 0xFE70 && c <= 0xFEFE)) return true;
                if (char.IsLetter(c)) return false;
            }
            return false;
        }

        // Right-to-left lines sit against the bubble's right edge and never wrap inside it.
        internal static readonly StringFormat Leftward = Mirrored();

        // Measures a line with its trailing spaces, for the caret after Thai and Indian scripts, whose letters don't keep to her columns.
        internal static readonly StringFormat Spaced = Trailing();

        private static StringFormat Trailing()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        private static StringFormat Mirrored()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            // Arabic fonts can stand taller than her line, and a line that doesn't fully fit the box would otherwise not draw.
            format.FormatFlags = (format.FormatFlags | StringFormatFlags.DirectionRightToLeft | StringFormatFlags.NoWrap) & ~StringFormatFlags.LineLimit;
            return format;
        }

        // The longest start of the text that fits in that many columns.
        internal static string Cut(string text, int columns)
        {
            text = text ?? "";
            if (Indic(text))
            {
                // Whole letters with their marks, measured, and only where a line may break between letters.
                bool[] breaks = Letters(text);
                int fits = 0;
                for (int end = 1; end <= text.Length; end++)
                {
                    if (end < text.Length && !breaks[end]) continue;
                    if (Cells(text.Substring(0, end)) > columns) break;
                    fits = end;
                }
                return text.Substring(0, fits);
            }
            int cells = 0, length = 0;
            while (length < text.Length && cells + Cells(text[length]) <= columns) cells += Cells(text[length++]);
            return text.Substring(0, length);
        }

        // True when the text has Thai in it.
        internal static bool Thai(string text)
        {
            foreach (char c in text ?? "") if (c >= 0x0E01 && c <= 0x0E5B) return true;
            return false;
        }

        // True when the text has Thai, Lao, Khmer or Burmese in it, which write no spaces between words.
        internal static bool Unspaced(string text)
        {
            foreach (char c in text ?? "") if ((c >= 0x0E80 && c <= 0x0EFF) || (c >= 0x1780 && c <= 0x17FF) || (c >= 0x19E0 && c <= 0x19FF) || Myanmar(c)) return true;
            return Thai(text);
        }

        [StructLayout(LayoutKind.Sequential)] private struct ScriptAnalysis { internal ushort Bits; internal ushort State; }
        [StructLayout(LayoutKind.Sequential)] private struct ScriptItem { internal int Start; internal ScriptAnalysis Analysis; }
        [DllImport("usp10.dll", CharSet = CharSet.Unicode)] private static extern int ScriptItemize(string text, int length, int most, IntPtr control, IntPtr state, [Out] ScriptItem[] items, out int count);
        [DllImport("usp10.dll", CharSet = CharSet.Unicode)] private static extern int ScriptBreak(string text, int length, ref ScriptAnalysis analysis, [Out] byte[] marks);
        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr ubrk_open(int type, [MarshalAs(UnmanagedType.LPStr)] string locale, IntPtr text, int length, ref int status);
        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl)] private static extern int ubrk_next(IntPtr iterator);
        [DllImport("icu.dll", CallingConvention = CallingConvention.Cdecl)] private static extern void ubrk_close(IntPtr iterator);

        // Where a line may break inside a word with Thai, Lao, Khmer or Burmese in it. They write no spaces between words, so a
        // word breaker finds them: Windows' own for Thai, and for the others, which it doesn't know, the ICU that comes with
        // Windows 10 1903 and later. Without ICU their lines break between letters.
        internal static bool[] Breaks(string word)
        {
            if (!Thai(word))
            {
                bool[] words = Icu(word);
                if (words != null) return words;
            }
            bool[] breaks = new bool[word.Length];
            try
            {
                ScriptItem[] items = new ScriptItem[word.Length + 1];
                int count = 0;
                bool found = word.Length > 0 && ScriptItemize(word, word.Length, word.Length, IntPtr.Zero, IntPtr.Zero, items, out count) == 0;
                for (int k = 0; found && k < count; k++)
                {
                    string part = word.Substring(items[k].Start, items[k + 1].Start - items[k].Start);
                    byte[] marks = new byte[part.Length];
                    found = ScriptBreak(part, part.Length, ref items[k].Analysis, marks) == 0;
                    for (int i = 0; found && i < part.Length; i++) breaks[items[k].Start + i] = (marks[i] & 1) != 0;
                }
                if (found) return breaks;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return Letters(word);
        }

        private static bool[] Icu(string word)
        {
            // ICU reads the text while it walks it, so it gets a copy the garbage collector can't move.
            IntPtr text = Marshal.StringToHGlobalUni(word);
            try
            {
                int status = 0;
                IntPtr iterator = ubrk_open(2, "", text, word.Length, ref status);
                if (iterator == IntPtr.Zero || status > 0) return null;
                bool[] breaks = new bool[word.Length];
                try { for (int at = ubrk_next(iterator); at > 0 && at < word.Length; at = ubrk_next(iterator)) breaks[at] = true; }
                finally { ubrk_close(iterator); }
                return breaks;
            }
            catch (DllNotFoundException) { return null; }
            catch (EntryPointNotFoundException) { return null; }
            finally { Marshal.FreeHGlobal(text); }
        }

        // Without a word breaker a line can still break between letters. A Thai or Lao line never breaks before a vowel or
        // tone mark, or a vowel that follows its letter, and never after a vowel written before its letter. Every script keeps
        // each letter with its marks, and with the next letter after a sign that joins them (a virama, or Khmer's coeng), and
        // a Burmese line never breaks before ါ, ာ or း (newer .NET counts them as letters of their own, like Mac and Linux do)
        // or a letter that ends the syllable before it.
        internal static bool[] Letters(string word)
        {
            bool[] breaks = new bool[word.Length];
            if (!Thai(word))
            {
                foreach (int start in System.Globalization.StringInfo.ParseCombiningCharacters(word))
                    if (start > 0 && !Joins(word[start - 1]) && !Ends(word, start) && (word[start - 1] < 0x0EC0 || word[start - 1] > 0x0EC4) && "ະາຳໆຯါား".IndexOf(word[start]) < 0) breaks[start] = true;
                return breaks;
            }
            for (int i = 1; i < word.Length; i++)
                breaks[i] = Cells(word[i]) > 0 && "ฯะาำๅๆ".IndexOf(word[i]) < 0 && (word[i - 1] < 0x0E40 || word[i - 1] > 0x0E44);
            return breaks;
        }

        // Signs that join the next letter to the one before: the Indian scripts' and Burmese viramas and Khmer's coeng.
        private static bool Joins(char c)
        {
            return c == '्' || c == '্' || c == '੍' || c == '્' || c == '୍' || c == '்' || c == '్' || c == '್' || c == '്' || c == '្' || c == '္';
        }

        // True when the Burmese letter there ends the syllable before it: an asat silences it, or a virama stacks the next
        // letter under it, after a dot below or not.
        private static bool Ends(string word, int at)
        {
            int next = at + 1 < word.Length && word[at + 1] == '့' ? at + 2 : at + 1;
            return next < word.Length && (word[next] == '်' || word[next] == '္');
        }

        // The longest start of a word with Thai, Lao, Khmer or Burmese in it that ends where a line may break and fits in that many columns.
        internal static string Fill(string word, int columns)
        {
            bool[] breaks = Breaks(word);
            int end = 0;
            for (int i = 1; i <= word.Length; i++)
            {
                if (i < word.Length && !breaks[i]) continue;
                if (Cells(word.Substring(0, i)) > columns) return word.Substring(0, end);
                end = i;
            }
            return word;
        }

        // Lines by columns, so Chinese, Japanese and Korean fit the bubble too. They have no spaces to break at, so a run of
        // them fills the rest of a line and carries on below, the way they're written. Thai, Lao, Khmer and Burmese do the same
        // between their words. Other words only break when they're longer than a whole line.
        internal static List<string> Wrap(string text, int columns)
        {
            List<string> lines = new List<string>();
            string line = "";
            foreach (string piece in (text ?? "").Split(' '))
            {
                string word = piece;
                bool thai = Unspaced(word);
                if (word.Length > 0 && (Cells(word[0]) == 2 || thai) && line.Length > 0 && Cells(line) + 1 + Cells(word) > columns)
                {
                    string start = thai ? Fill(word, columns - Cells(line) - 1) : Kept(word, Cut(word, columns - Cells(line) - 1));
                    if (start.Length > 0) { line += " " + start; word = word.Substring(start.Length); }
                    lines.Add(line);
                    line = "";
                }
                while (Cells(word) > columns)
                {
                    if (line.Length > 0) { lines.Add(line); line = ""; }
                    string cut = thai ? Fill(word, columns) : "";
                    if (cut.Length == 0) cut = Kept(word, Cut(word, columns));
                    if (cut.Length == 0) cut = word.Substring(0, 1);
                    lines.Add(cut);
                    word = word.Substring(cut.Length);
                }
                if (line.Length == 0) line = word;
                else if (Cells(line) + 1 + Cells(word) <= columns) line += " " + word;
                else { lines.Add(line); line = word; }
            }
            if (line.Length > 0 || lines.Count == 0) lines.Add(line);
            return lines;
        }

        // Chinese and Japanese never start a line with closing punctuation, so the character before it moves down with it.
        private const string Closing = "，。、！？：；）」』】〕〉》｝’”…,.!?:;)";

        private static string Kept(string word, string cut)
        {
            while (cut.Length > 1 && cut.Length < word.Length && Closing.IndexOf(word[cut.Length]) >= 0) cut = cut.Substring(0, cut.Length - 1);
            return cut;
        }

        // How much of each line shows once `revealed` characters of the text are out. A break between words used up spaces,
        // which count as revealed too; a word or a run of Chinese broken across lines used none.
        internal static int[] Takes(List<string> lines, string text, int revealed)
        {
            int[] takes = new int[lines.Count];
            int at = 0;
            for (int i = 0; i < lines.Count && revealed > 0; i++)
            {
                takes[i] = Math.Min(lines[i].Length, revealed);
                revealed -= takes[i];
                at += lines[i].Length;
                while (at < text.Length && text[at] == ' ') { at++; revealed--; }
            }
            return takes;
        }
    }

    internal class Overlay : Form
    {
        internal bool ClickThrough;
        private Bitmap shown;
        internal bool Above = true;

        internal Overlay(string name)
        {
            Text = name;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00080000 | 0x00000080 | 0x08000000 | (Above ? 0x00000008 : 0) | (ClickThrough ? 0x20 : 0);
                return value;
            }
        }

        internal Bitmap Image { get { return shown; } }

        internal void Present(Bitmap image, Point at)
        {
            Rectangle target = new Rectangle(at, image.Size);
            if (Bounds != target) Bounds = target;
            if (!Visible) Show();
            Native.DrawLayered(Handle, image, at);
            Bitmap old = shown;
            shown = image;
            if (old != null) old.Dispose();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == 0x0084 && shown != null)
            {
                long value = message.LParam.ToInt64();
                Point point = PointToClient(new Point((short)(value & 65535), (short)((value >> 16) & 65535)));
                if (point.X < 0 || point.Y < 0 || point.X >= shown.Width || point.Y >= shown.Height || shown.GetPixel(point.X, point.Y).A < 12)
                {
                    message.Result = new IntPtr(-1);
                    return;
                }
            }
            base.WndProc(ref message);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && shown != null) { shown.Dispose(); shown = null; }
            base.Dispose(disposing);
        }
    }

    // The pencil, wave and mic that show under her when you hover her, cyberpunk like her mini player: black plates with cut
    // corners and a pink-to-cyan neon edge, TYPE and TALK in her mono font, a wave of segments that lights up with your mic
    // level in voice chat, a red MUTED while the mic is muted, and a few frames of her glitch when it shows. The same size and
    // hit areas as the old rounded pill, with every straight edge on whole pixels, so it stays crisp at 100%, 150% and 200%.
    internal sealed class TalkPill : Overlay
    {
        internal event Action Typing;
        internal event Action Voice;
        internal event Action Mute;
        // Frames of the glitch it shows with, 45 ms each.
        internal const int TearFrames = 4;
        // Segments in each of the wave's five bars when it's full: the wave's shape, and the top of the mic level.
        internal static readonly int[] Bars = { 2, 4, 5, 3, 2 };
        internal static readonly Color Fill = Color.FromArgb(246, 9, 11, 15);
        internal static readonly Color Unlit = Color.FromArgb(46, 52, 64);
        internal static readonly Color Caption = Color.FromArgb(168, 174, 184);
        private const int WashAlpha = 40;
        private static readonly Random random = new Random();
        private readonly Timer ticks = new Timer();
        private int hover;
        private float scale = 1;
        private Point centre;
        private bool chatting;
        private bool muted;
        private bool listenForName;
        private int level;
        private int tear;
        private int seed;
        internal bool Motion = true;

        internal TalkPill() : base("Talk to Capri")
        {
            Cursor = Look.Pointer;
            ticks.Interval = 45;
            ticks.Tick += delegate
            {
                if (tear > 0) tear--;
                if (tear == 0) ticks.Stop();
                if (Visible) Redraw();
            };
        }

        internal static Size Measure(float scale) { return new Size((int)Math.Round(100 * scale), (int)Math.Round(40 * scale)); }

        internal bool Chatting { get { return chatting; } }

        internal bool Muted { get { return muted; } }

        internal bool ListenForName
        {
            get { return listenForName; }
            set { if (listenForName == value) return; listenForName = value; if (Visible) Redraw(); }
        }

        // True for the few frames it glitches in.
        internal bool Tearing { get { return tear > 0; } }

        internal void Place(Point topCentre, float value)
        {
            if (Visible && topCentre == centre && value == scale) return;
            centre = topCentre;
            scale = value;
            Redraw();
        }

        internal void SetChat(bool on, bool micMuted)
        {
            if (on == chatting && micMuted == muted) return;
            chatting = on;
            muted = micMuted;
            level = 0;
            if (Visible) Redraw();
        }

        internal void SetLevel(double value)
        {
            int step = chatting && !muted ? Math.Min(6, (int)(Math.Sqrt(value) * 24)) : 0;
            if (step == level) return;
            level = step;
            if (Visible) Redraw();
        }

        private void Redraw()
        {
            // Only showing it glitches: a hover, the mic level or her walking redraws it clean.
            if (!Visible && Motion)
            {
                tear = TearFrames;
                seed = random.Next(1, int.MaxValue);
                ticks.Stop();
                ticks.Start();
            }
            Present(Render(scale, hover, chatting, muted, level, tear, seed, listenForName), new Point(centre.X - Measure(scale).Width / 2, centre.Y));
        }

        // For the smoke test: it glitches in when it shows, and a redraw while it's up stays clean.
        internal bool GlitchesIn()
        {
            bool motion = Motion;
            Motion = true;
            try
            {
                Hide();
                Redraw();
                bool torn = Visible && tear == TearFrames;
                using (Bitmap clean = Render(scale, hover, chatting, muted, level, 0, 0, listenForName)) torn = torn && Differs(Image, clean);
                tear = 0;
                ticks.Stop();
                Redraw();
                bool calm = tear == 0 && !ticks.Enabled;
                using (Bitmap clean = Render(scale, hover, chatting, muted, level, 0, 0, listenForName)) calm = calm && !Differs(Image, clean);
                return torn && calm;
            }
            finally { Motion = motion; }
        }

        private static bool Differs(Bitmap one, Bitmap two)
        {
            if (one == null || two == null || one.Size != two.Size) return true;
            for (int y = 0; y < one.Height; y++)
                for (int x = 0; x < one.Width; x++)
                    if (one.GetPixel(x, y) != two.GetPixel(x, y)) return true;
            return false;
        }

        // Its plate in the picture, in whole pixels.
        internal static Rectangle Body(float s)
        {
            Size size = Measure(s);
            int margin = (int)Math.Round(3 * s);
            return new Rectangle(margin, margin, size.Width - margin * 2, size.Height - margin * 2);
        }

        // The mic button's square plate, right of the pill in voice chat.
        internal static Rectangle MicBody(float s)
        {
            Size size = Measure(s);
            int margin = (int)Math.Round(3 * s);
            return new Rectangle(size.Width + (int)Math.Round(4 * s) + margin, margin, size.Height - margin * 2, size.Height - margin * 2);
        }

        internal static int Cut(float s) { return Math.Max(3, (int)Math.Round(6 * s)); }

        internal static int EdgeWidth(float s) { return Math.Max(1, (int)Math.Round(1.5 * s)); }

        internal static string Label(int part, bool chatting, bool muted, bool listenForName = false) { return part == 1 ? "TYPE" : chatting && muted ? "MUTED" : chatting && listenForName ? "CAPRI" : "TALK"; }

        // How many of a bar's segments the mic level lights, for a level from 0 to 6.
        internal static int Lit(int bar, int level)
        {
            if (level <= 0) return 0;
            return Math.Max(1, (int)Math.Round(Bars[bar] * Math.Min(6, level) / 6.0, MidpointRounding.AwayFromZero));
        }

        private static int Square(float s) { return Math.Max(2, (int)Math.Round(2 * s)); }

        private static int Pitch(float s) { return Math.Max(3, (int)Math.Round(3 * s, MidpointRounding.AwayFromZero)); }

        // One segment of the wave: bar 0 to 4 from the left, row 0 at the bottom.
        internal static Rectangle Segment(Rectangle meter, float s, int bar, int row)
        {
            int square = Square(s);
            return new Rectangle(meter.Left + bar * Pitch(s), meter.Bottom - square - row * Pitch(s), square, square);
        }

        private static Font LabelFont(float s) { return Look.Mono(8.5f * s, FontStyle.Regular); }

        // Where a label's letters land, from where it's drawn.
        private static Rectangle Ink(Font font, string label)
        {
            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddString(label, font.FontFamily, (int)font.Style, font.Size, PointF.Empty, StringFormat.GenericTypographic);
                RectangleF ink = path.GetBounds();
                return Rectangle.FromLTRB((int)Math.Floor(ink.Left), (int)Math.Floor(ink.Top), (int)Math.Ceiling(ink.Right), (int)Math.Ceiling(ink.Bottom));
            }
        }

        // The icon's box and the label's letters in one half, 1 the pencil's and 2 the wave's, the pair in the middle of it.
        internal static Rectangle[] Layout(float s, int part, string label)
        {
            using (Font font = LabelFont(s)) return Layout(s, part, Ink(font, label));
        }

        private static Rectangle[] Layout(float s, int part, Rectangle ink)
        {
            Rectangle body = Body(s);
            int mid = Measure(s).Width / 2;
            int edge = EdgeWidth(s);
            int left = part == 1 ? body.Left + edge : mid;
            int right = part == 1 ? mid : body.Right - edge;
            int wide = part == 1 ? (int)Math.Round(12.5f * s) : Pitch(s) * 4 + Square(s);
            int gap = (int)Math.Round(4 * s);
            int start = left + (right - left - wide - gap - ink.Width + 1) / 2;
            int middle = body.Top + body.Height / 2;
            return new[] { new Rectangle(start, middle - wide / 2, wide, wide), new Rectangle(start + wide + gap, middle - ink.Height / 2, ink.Width, ink.Height) };
        }

        internal static Bitmap Render(float s, int hover, bool chatting = false, bool muted = false, int level = 0, int tear = 0, int seed = 0, bool listenForName = false)
        {
            Size size = Measure(s);
            Bitmap image = new Bitmap(chatting ? size.Width + (int)Math.Round(4 * s) + size.Height : size.Width, size.Height, PixelFormat.Format32bppPArgb);
            using (Graphics graphics = Graphics.FromImage(image))
            using (Font font = LabelFont(s))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Rectangle body = Body(s);
                int mid = size.Width / 2;
                Rectangle lit = hover == 1 ? Rectangle.FromLTRB(body.Left, body.Top, mid, body.Bottom) : hover == 2 ? Rectangle.FromLTRB(mid, body.Top, body.Right, body.Bottom) : Rectangle.Empty;
                Plate(graphics, body, s, lit, Look.Pink, Look.Cyan);
                // A slanted seam where the two halves' hit areas meet.
                using (Pen seam = new Pen(Color.FromArgb(66, 72, 86), Math.Max(1, (int)Math.Round(s))))
                    graphics.DrawLine(seam, mid + 1.5f * s, body.Top + 10 * s, mid - 1.5f * s, body.Bottom - 10 * s);
                // Her glitch, like her mini player: slivers of the edge torn loose, cyan at the top right and pink at the bottom left.
                int sliver = Math.Max(1, (int)Math.Round(s));
                using (Brush cyan = new SolidBrush(Color.FromArgb(220, Look.Cyan))) graphics.FillRectangle(cyan, body.Right - (int)Math.Round(32 * s), body.Top - (int)Math.Round(2 * s), (int)Math.Round(15 * s), sliver);
                using (Brush pink = new SolidBrush(Color.FromArgb(220, Look.Pink))) graphics.FillRectangle(pink, body.Left + (int)Math.Round(12 * s), body.Bottom + (int)Math.Round(2 * s) - sliver, (int)Math.Round(11 * s), sliver);
                Color wash = Mix(Fill, Look.Cyan, WashAlpha);
                string talk = Label(2, chatting, muted, listenForName);
                Rectangle[] pencil = Layout(s, 1, Ink(font, "TYPE"));
                Rectangle[] wave = Layout(s, 2, Ink(font, talk));
                DrawPencil(graphics, new PointF(pencil[0].X + pencil[0].Width / 2f, pencil[0].Y + pencil[0].Height / 2f), 0.9f * s, hover == 1 ? Look.Cyan : Look.Ink, hover == 1 ? wash : Fill);
                DrawLabel(graphics, "TYPE", font, pencil[1], hover == 1 ? Look.Cyan : Caption);
                DrawMeter(graphics, wave[0], s, chatting, muted, level, hover == 2 || chatting ? Look.Cyan : Look.Ink);
                DrawLabel(graphics, talk, font, wave[1], chatting && muted ? Look.Red : hover == 2 || chatting ? Look.Cyan : Caption);
                if (chatting)
                {
                    Rectangle mic = MicBody(s);
                    Color edge = muted ? Look.Red : Look.Cyan;
                    Plate(graphics, mic, s, hover == 3 ? mic : Rectangle.Empty, edge, edge);
                    DrawMic(graphics, new PointF(mic.X + mic.Width / 2f, mic.Y + mic.Height / 2f), s, hover == 3 ? Look.Cyan : muted ? Look.Dim : Look.Ink, muted, hover == 3 ? wash : Fill);
                }
            }
            if (tear > 0) Tear(image, s, Math.Min(1, (float)tear / TearFrames), seed + tear);
            return image;
        }

        // A plate with cut corners: a soft glow, the black fill with faint scan lines, a cyan wash where it's hovered, and a neon
        // edge from one colour to the other, inside the box so its straight edges land on whole pixels.
        internal static void Plate(Graphics graphics, Rectangle box, float s, Rectangle lit, Color from, Color to)
        {
            int cut = Cut(s);
            int edge = EdgeWidth(s);
            int margin = (int)Math.Round(3 * s);
            PointF start = new PointF(box.Left - margin - 1, 0), end = new PointF(box.Right + margin + 1, 0);
            float middle = box.Top + box.Height / 2f;
            using (GraphicsPath outline = Look.Plate(box, cut, -1, middle, 0, true))
            {
                using (LinearGradientBrush haze = new LinearGradientBrush(start, end, Color.FromArgb(34, from), Color.FromArgb(34, to)))
                using (Pen wide = new Pen(haze, margin * 2)) graphics.DrawPath(wide, outline);
                using (LinearGradientBrush haze = new LinearGradientBrush(start, end, Color.FromArgb(70, from), Color.FromArgb(70, to)))
                using (Pen near = new Pen(haze, margin)) graphics.DrawPath(near, outline);
                using (Brush fill = new SolidBrush(Fill)) graphics.FillPath(fill, outline);
                graphics.SetClip(outline);
                int line = Math.Max(1, (int)Math.Round(s));
                using (Pen scan = new Pen(Color.FromArgb(9, 255, 255, 255), line))
                    for (int row = box.Top; row < box.Bottom; row += Math.Max(3, (int)Math.Round(3 * s))) graphics.DrawLine(scan, box.Left, row + line / 2f, box.Right, row + line / 2f);
                if (!lit.IsEmpty) using (Brush wash = new SolidBrush(Color.FromArgb(WashAlpha, Look.Cyan))) graphics.FillRectangle(wash, lit);
                graphics.ResetClip();
            }
            float inset = edge / 2f;
            using (GraphicsPath rim = Look.Plate(new RectangleF(box.Left + inset, box.Top + inset, box.Width - edge, box.Height - edge), cut - inset * 0.586f, -1, middle, 0, true))
            using (LinearGradientBrush ink = new LinearGradientBrush(start, end, from, to))
            using (Pen pen = new Pen(ink, edge))
                graphics.DrawPath(pen, rim);
        }

        private static Color Mix(Color under, Color over, int alpha)
        {
            return Color.FromArgb(under.R + (over.R - under.R) * alpha / 255, under.G + (over.G - under.G) * alpha / 255, under.B + (over.B - under.B) * alpha / 255);
        }

        private static void DrawLabel(Graphics graphics, string label, Font font, Rectangle box, Color colour)
        {
            Rectangle ink = Ink(font, label);
            using (Brush brush = new SolidBrush(colour)) graphics.DrawString(label, font, brush, box.Left - ink.Left, box.Top - ink.Top, StringFormat.GenericTypographic);
        }

        // The wave as five bars of square segments. Out of voice chat it's the whole wave; in voice chat the segments light up to
        // your mic level and the rest stay dark, all of them while the mic is muted.
        // Lachy liked the old wave better (29 Oct): five rounded bars that stretch with your mic level in voice chat, and stay low
        // and grey while the mic is muted.
        private static void DrawMeter(Graphics graphics, Rectangle meter, float s, bool chatting, bool muted, int level, Color colour)
        {
            float[] heights = { 5, 10, 14, 9, 4 };
            float stretch = !chatting ? 1 : 0.45f + (muted ? 0 : level) * 0.14f;
            PointF centre = new PointF(meter.X + meter.Width / 2f, meter.Y + meter.Height / 2f);
            using (Pen bar = new Pen(chatting && muted ? Look.Dim : colour, 2.3f * s))
            {
                bar.StartCap = LineCap.Round;
                bar.EndCap = LineCap.Round;
                for (int i = 0; i < heights.Length; i++)
                {
                    float x = centre.X + (i - 2) * 3.6f * s;
                    float half = Math.Max(1.5f, heights[i] * stretch) * s / 2;
                    graphics.DrawLine(bar, x, centre.Y - half, x, centre.Y + half);
                }
            }
        }

        private static void DrawMic(Graphics graphics, PointF centre, float s, Color colour, bool muted, Color back)
        {
            float x = centre.X;
            float y = centre.Y + 0.5f * s;
            using (Brush ink = new SolidBrush(colour))
            using (GraphicsPath capsule = Look.Rounded(new RectangleF(x - 3.2f * s, y - 9 * s, 6.4f * s, 11 * s), 3.2f * s))
                graphics.FillPath(ink, capsule);
            using (Pen line = new Pen(colour, 1.8f * s))
            {
                line.StartCap = LineCap.Round;
                line.EndCap = LineCap.Round;
                graphics.DrawArc(line, x - 6 * s, y - 8 * s, 12 * s, 12 * s, 0, 180);
                graphics.DrawLine(line, x, y + 4 * s, x, y + 7 * s);
                graphics.DrawLine(line, x - 3.5f * s, y + 7 * s, x + 3.5f * s, y + 7 * s);
            }
            if (!muted) return;
            using (Pen gap = new Pen(back, 4.2f * s))
            using (Pen slash = new Pen(Look.Red, 2 * s))
            {
                slash.StartCap = LineCap.Round;
                slash.EndCap = LineCap.Round;
                graphics.DrawLine(gap, x - 7.5f * s, y - 9.5f * s, x + 7.5f * s, y + 7.5f * s);
                graphics.DrawLine(slash, x - 7.5f * s, y - 9.5f * s, x + 7.5f * s, y + 7.5f * s);
            }
        }

        // A square-cut pencil at 45 degrees, centred on where its outline's middle falls.
        private static void DrawPencil(Graphics graphics, PointF centre, float k, Color colour, Color back)
        {
            GraphicsState state = graphics.Save();
            graphics.TranslateTransform(centre.X + 0.5f * k, centre.Y - 0.5f * k);
            graphics.RotateTransform(-45);
            using (Brush ink = new SolidBrush(colour))
            {
                graphics.FillRectangle(ink, -8 * k, -2.5f * k, 12.6f * k, 5 * k);
                graphics.FillPolygon(ink, new[] { new PointF(4.6f * k, -2.5f * k), new PointF(9 * k, 0), new PointF(4.6f * k, 2.5f * k) });
            }
            using (Pen gap = new Pen(back, 1.1f * k))
            {
                graphics.DrawLine(gap, -4.8f * k, -3 * k, -4.8f * k, 3 * k);
                graphics.DrawLine(gap, 4f * k, -3 * k, 4f * k, 3 * k);
            }
            graphics.Restore(state);
        }

        // The glitch it shows with: rows slip sideways and take a tint of her pink or cyan, less each frame. Seeded, so a redraw
        // in the same frame tears the same way.
        private static void Tear(Bitmap image, float s, float amount, int seed)
        {
            Random roll = new Random(seed);
            bool chatting = image.Width > Measure(s).Width;
            using (Bitmap clean = image.Clone(new Rectangle(0, 0, image.Width, image.Height), image.PixelFormat))
            using (Graphics graphics = Graphics.FromImage(image))
            using (Brush clear = new SolidBrush(Color.Transparent))
            {
                int bands = 2 + (int)Math.Round(2 * amount);
                for (int i = 0; i < bands; i++)
                {
                    int band = Math.Max(2, (int)Math.Round((2 + roll.Next(6)) * s));
                    int top = roll.Next(Math.Max(1, image.Height - band));
                    int shift = (roll.Next(2) == 0 ? -1 : 1) * Math.Max(1, (int)Math.Round((2 + roll.Next(6)) * s * amount));
                    Rectangle strip = new Rectangle(0, top, image.Width, band);
                    graphics.SetClip(strip);
                    graphics.CompositingMode = CompositingMode.SourceCopy;
                    graphics.FillRectangle(clear, strip);
                    graphics.DrawImage(clean, new Rectangle(shift, 0, image.Width, image.Height));
                    graphics.CompositingMode = CompositingMode.SourceOver;
                    graphics.SetClip(Body(s));
                    if (chatting) graphics.SetClip(MicBody(s), CombineMode.Union);
                    graphics.IntersectClip(strip);
                    using (Brush tint = new SolidBrush(Color.FromArgb((int)(96 * amount), i % 2 == 0 ? Look.Pink : Look.Cyan))) graphics.FillRectangle(tint, strip);
                    graphics.ResetClip();
                }
            }
        }

        internal static int PartAt(int x, float s, bool chatting)
        {
            int wide = Measure(s).Width;
            if (chatting && x >= wide) return 3;
            return x < wide / 2 ? 1 : 2;
        }

        private int PartAt(int x) { return PartAt(x, scale, chatting); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int part = PartAt(e.X);
            if (part != hover) { hover = part; Redraw(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hover != 0) { hover = 0; Redraw(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Press(PartAt(e.X));
        }

        internal void Press(int part)
        {
            if (part == 1) { if (Typing != null) Typing(); }
            else if (part == 3) { if (Mute != null) Mute(); }
            else if (Voice != null) Voice();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }

        // The smoke test's checks of its look at 100%, 150% and 200%: null when they pass, or what's wrong.
        internal static string Check()
        {
            foreach (float s in new[] { 1f, 1.5f, 2f })
            {
                string at = " at " + (int)Math.Round(s * 100) + "%";
                Size size = Measure(s);
                Rectangle body = Body(s), mic = MicBody(s);
                int cut = Cut(s), edge = EdgeWidth(s), mid = size.Width / 2, chatWide = size.Width + (int)Math.Round(4 * s) + size.Height;
                Bitmap[] shots = { Render(s, 0), Render(s, 1), Render(s, 2), Render(s, 0, true, false, 0), Render(s, 0, true, false, 3), Render(s, 0, true, false, 6), Render(s, 0, true, true, 0), Render(s, 3, true, false, 0) };
                try
                {
                    Bitmap idle = shots[0], typing = shots[1], talking = shots[2], live0 = shots[3], live3 = shots[4], live6 = shots[5], hushed = shots[6], micLit = shots[7];
                    if (idle.Size != size || typing.Size != size || live0.Width != chatWide || live0.Height != size.Height) return "Its size changed" + at + ": " + idle.Size + ", " + live0.Size;
                    if (PartAt(0, s, false) != 1 || PartAt(mid - 1, s, false) != 1 || PartAt(mid, s, false) != 2 || PartAt(size.Width - 1, s, true) != 2 || PartAt(size.Width, s, true) != 3 || PartAt(chatWide - 1, s, true) != 3 || PartAt(size.Width + 2, s, false) != 2) return "Its hit areas moved" + at;
                    foreach (Rectangle plate in new[] { body, mic })
                    {
                        Bitmap shot = plate == body ? idle : live0;
                        if (shot.GetPixel(plate.Left, plate.Top).A > 140 || shot.GetPixel(plate.Right - 1, plate.Bottom - 1).A > 140 || shot.GetPixel(plate.Right - 1, plate.Top).A > 140
                            || shot.GetPixel(plate.Left, plate.Top + cut + 1).A < 250 || shot.GetPixel(plate.Right - 1, plate.Bottom - cut - 2).A < 250 || shot.GetPixel(plate.Left + cut + 1, plate.Top).A < 250 || shot.GetPixel(plate.Right - cut - 2, plate.Bottom - 1).A < 250)
                            return "Its corners aren't cut" + at;
                    }
                    if (Brightest(idle.GetPixel(body.Left + cut + 2, body.Top + edge - 1)) < 150 || Brightest(idle.GetPixel(body.Left + cut + 2, body.Top + edge)) > 60 || Brightest(idle.GetPixel(body.Left + edge - 1, body.Top + body.Height / 2)) < 150 || Brightest(idle.GetPixel(body.Left + edge, body.Top + body.Height / 2)) > 60)
                        return "Its neon edge isn't crisp" + at;
                    if (!Red(hushed.GetPixel(mic.Left, mic.Top + cut + 1)) || Red(live0.GetPixel(mic.Left, mic.Top + cut + 1))) return "The mic button's edge didn't go red only while muted" + at;
                    Rectangle[] pencil = Layout(s, 1, "TYPE"), wave = Layout(s, 2, "TALK"), hush = Layout(s, 2, "MUTED");
                    int letters = (int)(6 * s * s);
                    if (Count(idle, pencil[1], Grey) < letters || Count(idle, wave[1], Grey) < letters) return "Its TYPE and TALK labels are missing" + at;
                    if (Count(typing, pencil[1], Cyan) < letters || Count(typing, wave[1], Cyan) > 0 || Count(talking, wave[1], Cyan) < letters || Count(talking, pencil[1], Cyan) > 0) return "Hovering didn't light just its part cyan" + at;
                    Point tip = new Point(pencil[0].X + pencil[0].Width / 2, pencil[0].Y + pencil[0].Height / 2);
                    if (!Grey(idle.GetPixel(tip.X, tip.Y)) || !Cyan(typing.GetPixel(tip.X, tip.Y))) return "The pencil didn't light cyan on hover" + at;
                    if (Count(hushed, hush[1], Red) < letters || Count(live0, hush[1], Red) > 0 || Count(idle, hush[1], Red) > 0) return "MUTED didn't show in red, or showed unmuted" + at;
                    Rectangle core = Rectangle.Inflate(mic, -cut - edge, -cut - edge);
                    if (Count(micLit, core, Cyan) < letters || Count(live0, core, Cyan) > 0) return "Hovering the mic didn't light it cyan" + at;
                    Rectangle waveBox = Rectangle.Inflate(wave[0], (int)(4 * s), (int)(4 * s)), hushBox = Rectangle.Inflate(hush[0], (int)(4 * s), (int)(4 * s));
                    Point middle = new Point(wave[0].X + wave[0].Width / 2, wave[0].Y + wave[0].Height / 2);
                    if (!Grey(idle.GetPixel(middle.X, middle.Y)) || !Cyan(talking.GetPixel(middle.X, middle.Y))) return "The wave didn't show" + at;
                    int quietest = Count(live0, waveBox, Cyan), middling = Count(live3, waveBox, Cyan), loudest = Count(live6, waveBox, Cyan);
                    if (!(quietest < middling && middling < loudest)) return "The wave didn't stretch with the mic level" + at + ": " + quietest + ", " + middling + ", " + loudest;
                    if (Count(hushed, hushBox, Cyan) > 0) return "The wave lit while muted" + at;
                }
                finally { foreach (Bitmap shot in shots) shot.Dispose(); }
            }
            return null;
        }

        private static int Brightest(Color c) { return Math.Max(c.R, Math.Max(c.G, c.B)); }

        private static bool Grey(Color c) { return c.A > 200 && Math.Min(c.R, Math.Min(c.G, c.B)) > 80 && Brightest(c) - Math.Min(c.R, Math.Min(c.G, c.B)) < 40; }

        private static bool Cyan(Color c) { return c.A > 200 && c.G > 140 && c.B > 150 && c.R < 130; }

        private static bool Red(Color c) { return c.A > 200 && c.R > 150 && c.G < 90 && c.B < 120; }

        private static int Count(Bitmap image, Rectangle box, Func<Color, bool> test)
        {
            int count = 0;
            for (int y = Math.Max(0, box.Top); y < Math.Min(image.Height, box.Bottom); y++)
                for (int x = Math.Max(0, box.Left); x < Math.Min(image.Width, box.Right); x++)
                    if (test(image.GetPixel(x, y))) count++;
            return count;
        }
    }

    internal sealed class Tear : Overlay
    {
        internal const double Length = 0.75;
        internal const double WideLength = 1.2;
        internal const double BurstLength = 1.5;
        internal const double Linger = 0.7;
        private static readonly Color Crimson = Color.FromArgb(176, 6, 32);
        private static readonly Color Ink = Color.FromArgb(8, 6, 10);
        private static readonly Color Bruise = Color.FromArgb(34, 8, 48);
        private static readonly Color Violet = Color.FromArgb(110, 30, 170);
        private static readonly Color Hot = Color.FromArgb(255, 120, 140);
        private readonly Timer ticks = new Timer();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private readonly Random random = new Random();
        private Rectangle area;
        private Rectangle heart;
        private int seed;
        private bool wide;
        private Point? core;
        private IntPtr under;

        internal Tear() : base("Capri glitch")
        {
            ticks.Interval = 33;
            ticks.Tick += delegate { Step(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00000020;
                return value;
            }
        }

        internal void Burst(Rectangle body, Rectangle screen, bool everywhere, bool spawn = false, IntPtr behind = default(IntPtr))
        {
            wide = everywhere || spawn;
            under = wide ? behind : IntPtr.Zero;
            area = Rectangle.Intersect(wide ? Rectangle.Inflate(body, body.Height, body.Height * 2 / 3) : Rectangle.Inflate(body, body.Width, body.Height / 8), screen);
            if (area.Width < 8 || area.Height < 8) return;
            heart = new Rectangle(body.Left - area.Left, body.Top - area.Top, body.Width, body.Height);
            core = spawn ? new Point(heart.Left + heart.Width / 2, heart.Top + heart.Height / 2) : (Point?)null;
            seed = random.Next();
            ticks.Interval = 33;
            watch.Reset();
            watch.Start();
            Step();
            ticks.Start();
        }

        private void Step()
        {
            double t = watch.Elapsed.TotalSeconds;
            double length = core != null ? BurstLength : wide ? WideLength : Length, linger = core != null ? Linger : 0;
            if (t >= length + linger) { ticks.Stop(); Hide(); return; }
            Present(Frame(area.Size, Held(t, length, linger), seed, wide, core, heart, (int)(t * 30)), area.Location);
            if (under != IntPtr.Zero) { Native.SetWindowPos(Handle, under, 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010); under = IntPtr.Zero; }
        }

        internal static double Held(double t, double length, double linger)
        {
            double grown = 0.26 * length, hold = 0.3 * length + linger;
            return t < grown ? t / length : t < grown + hold ? 0.26 + 0.3 * (t - grown) / hold : (t - linger) / length;
        }

        internal static Bitmap Frame(Size size, double progress, int seed, bool wide = false, Point? core = null, Rectangle? body = null, int flicker = 0)
        {
            Bitmap image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            Random pick = new Random(seed + flicker);
            double fade = Math.Max(0, Math.Min(1, (1 - progress) / 0.65));
            fade = fade * fade * (3 - 2 * fade);
            if (fade <= 0) return image;
            Rectangle heart = body ?? new Rectangle(size.Width * 2 / 5, size.Height / 4, size.Width / 5, size.Height / 2);
            if (wide) Tendrils(image, new Rectangle(Point.Empty, size), core != null ? (PointF)core.Value : new PointF(heart.Left + heart.Width / 2f, heart.Top + heart.Height / 2f), new SizeF(heart.Width * 0.1f, heart.Height * 0.06f), Math.Max(0.6f, heart.Height / 260f), progress, seed, flicker);
            using (Graphics graphics = Graphics.FromImage(image))
            {
                double rest = core != null ? Math.Max(0, Math.Min(1, (progress - 0.25) * 3)) : 1;
                Rectangle near = wide ? Rectangle.Intersect(Rectangle.Inflate(heart, heart.Width / 2, heart.Height / 12), new Rectangle(Point.Empty, size)) : new Rectangle(Point.Empty, size);
                if (near.Width < 4 || near.Height < 4) return image;
                int bars = (int)Math.Round((wide ? 2 + pick.Next(4) : 6 + pick.Next(7)) * (0.45 + 0.55 * fade) * rest);
                int unit = Math.Max(2, wide ? heart.Height / 50 : size.Height / 26);
                for (int i = 0; i < bars; i++)
                {
                    int y = near.Top + pick.Next(near.Height);
                    int tall = 1 + (int)(unit * (wide ? 0.6 : 6) * Math.Pow(pick.NextDouble(), 2.2));
                    int width = wide ? near.Width / 6 + pick.Next(Math.Max(1, near.Width / 2)) : near.Width / 4 + pick.Next(Math.Max(1, near.Width * 3 / 4));
                    int x = near.Left + pick.Next(Math.Max(1, near.Width - width));
                    int kind = pick.Next(10);
                    Color colour = kind < 4 ? Ink : kind < 8 ? Look.Red : Crimson;
                    int alpha = (int)((kind < 4 ? 180 + pick.Next(60) : 150 + pick.Next(100)) * fade);
                    if (alpha <= 0) continue;
                    using (Brush brush = new SolidBrush(Color.FromArgb(alpha, colour))) graphics.FillRectangle(brush, x, y, width, tall);
                    if (kind < 4 && tall > 3)
                        using (Brush edge = new SolidBrush(Color.FromArgb(alpha * 2 / 3, Look.Red)))
                        {
                            graphics.FillRectangle(edge, x, y, width, 1);
                            graphics.FillRectangle(edge, x, y + tall - 1, width, 1);
                        }
                    if (pick.Next(3) == 0)
                        using (Brush ghost = new SolidBrush(Color.FromArgb(alpha / 3, Look.Red))) graphics.FillRectangle(ghost, x + pick.Next(-near.Width / 16, near.Width / 16 + 1), y + tall + 1, width / 2, Math.Max(1, tall / 2));
                }
            }
            return image;
        }

        internal static void Tendrils(Bitmap image, Rectangle room, PointF middle, SizeF spread, float unit, double progress, int seed, int flicker)
        {
            room = Rectangle.Intersect(room, new Rectangle(Point.Empty, image.Size));
            if (room.Width < 8 || room.Height < 8) return;
            double alpha = Math.Max(0, Math.Min(1, (0.97 - progress) / 0.17));
            alpha = alpha * alpha * (3 - 2 * alpha);
            if (alpha <= 0.01) return;
            Random shape = new Random(seed), jitter = new Random(seed ^ (flicker * 7919 + 1));
            double open = Math.Min(1, progress / 0.26);
            open = 1 - (1 - open) * (1 - open) * (1 - open);
            double clock = flicker / 30.0;
            List<PointF[]> spines = new List<PointF[]>();
            List<float[]> widths = new List<float[]>();
            int count = 4 + shape.Next(3);
            for (int i = 0; i < count; i++)
            {
                double angle = (i + 0.15 + shape.NextDouble() * 0.7) / count * 2 * Math.PI, dx = Math.Cos(angle), dy = Math.Sin(angle);
                float x = middle.X + (float)(dx * spread.Width), y = middle.Y + (float)(dy * spread.Height);
                double reach = Math.Min(dx > 0.01 ? (room.Right - 12 - x) / dx : dx < -0.01 ? (room.Left + 12 - x) / dx : 1e9, dy > 0.01 ? (room.Bottom - 12 - y) / dy : dy < -0.01 ? (room.Top + 12 - y) / dy : 1e9);
                double length = Math.Max(0, reach) * (0.6 + 0.32 * shape.NextDouble()), thick = (10 + shape.NextDouble() * 6) * unit;
                double close = Math.Max(0, Math.Min(1, (progress - 0.56 - 0.08 * shape.NextDouble()) / 0.3));
                close = Math.Round(close * close * (3 - 2 * close) * 6) / 6;
                Ribbon(spines, widths, shape, room, x, y, angle, length, thick, open * (1 - close), clock, unit, 2);
            }
            if (spines.Count == 0) return;
            float[] shiftX = new float[spines.Count], shiftY = new float[spines.Count];
            for (int v = 0; v < spines.Count; v++)
                if (jitter.NextDouble() < 0.08)
                {
                    shiftX[v] = (float)((jitter.Next(2) * 2 - 1) * (3 + jitter.Next(7)) * unit);
                    shiftY[v] = (float)((jitter.NextDouble() * 2 - 1) * 2 * unit);
                }
            using (Graphics graphics = Graphics.FromImage(image))
            {
                const int Soft = 4;
                using (Bitmap glow = new Bitmap(Math.Max(1, room.Width / Soft), Math.Max(1, room.Height / Soft), PixelFormat.Format32bppPArgb))
                {
                    using (Graphics blur = Graphics.FromImage(glow))
                    using (Brush haze = new SolidBrush(Color.FromArgb((int)(70 * alpha), 210, 12, 96)))
                    using (Brush halo = new SolidBrush(Color.FromArgb((int)(150 * alpha), 230, 10, 80)))
                    {
                        blur.SmoothingMode = SmoothingMode.AntiAlias;
                        for (int v = 0; v < spines.Count; v++) blur.FillPolygon(haze, Outline(spines[v], widths[v], 1f / Soft, 3.4f, 18 * unit, shiftX[v] - room.X, shiftY[v] - room.Y), FillMode.Winding);
                        for (int v = 0; v < spines.Count; v++) blur.FillPolygon(halo, Outline(spines[v], widths[v], 1f / Soft, 1.8f, 6 * unit, shiftX[v] - room.X, shiftY[v] - room.Y), FillMode.Winding);
                    }
                    using (Bitmap half = new Bitmap(Math.Max(1, room.Width / 2), Math.Max(1, room.Height / 2), PixelFormat.Format32bppPArgb))
                    {
                        using (Graphics soft = Graphics.FromImage(half))
                        {
                            soft.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            soft.PixelOffsetMode = PixelOffsetMode.Half;
                            soft.DrawImage(glow, new Rectangle(0, 0, half.Width, half.Height), 0, 0, glow.Width, glow.Height, GraphicsUnit.Pixel);
                        }
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.PixelOffsetMode = PixelOffsetMode.Half;
                        graphics.DrawImage(half, room, 0, 0, half.Width, half.Height, GraphicsUnit.Pixel);
                    }
                    graphics.PixelOffsetMode = PixelOffsetMode.Default;
                }
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                bool split = progress < 0.1 || jitter.Next(4) == 0;
                float cut = (2 + jitter.Next(3)) * unit;
                using (Brush cyan = new SolidBrush(Color.FromArgb((int)(170 * alpha), Shatter.Mint)))
                using (Brush violet = new SolidBrush(Color.FromArgb((int)(235 * alpha), Shatter.Violet)))
                using (Brush red = new SolidBrush(Color.FromArgb((int)(255 * alpha), 236, 6, 68)))
                    for (int v = 0; v < spines.Count; v++)
                    {
                        if (split) graphics.FillPolygon(cyan, Outline(spines[v], widths[v], 1, 1, 0, shiftX[v] + cut, shiftY[v] - cut * 0.4f), FillMode.Winding);
                        graphics.FillPolygon(violet, Outline(spines[v], widths[v], 1, 1, 0, shiftX[v] - 3 * unit, shiftY[v] + 3 * unit), FillMode.Winding);
                        graphics.FillPolygon(red, Outline(spines[v], widths[v], 1, 1, 0, shiftX[v], shiftY[v]), FillMode.Winding);
                    }
                graphics.SmoothingMode = SmoothingMode.None;
                using (Brush speck = new SolidBrush(Color.FromArgb((int)(210 * alpha), 255, 222, 232)))
                    for (int v = 0; v < spines.Count; v++)
                        for (int k = 1; k < spines[v].Length; k++)
                        {
                            PointF a = spines[v][k - 1], b = spines[v][k];
                            float girth = widths[v][k - 1], ex = b.X - a.X, ey = b.Y - a.Y, reach = (float)Math.Sqrt(ex * ex + ey * ey);
                            if (girth < 5 * unit || reach < 1) continue;
                            Random stars = new Random(seed ^ ((int)(spines[v][0].X * 7 + spines[v][0].Y * 13) * 977 + k * 131));
                            for (int dot = 0, dots = (int)(reach * girth / (180 * unit * unit)) + 1; dot < dots; dot++)
                            {
                                double along = stars.NextDouble(), across = (stars.NextDouble() * 2 - 1) * 0.3 * girth;
                                float dotSize = (1 + stars.Next(2)) * Math.Max(1, unit * 0.8f);
                                if (jitter.Next(4) == 0) continue;
                                graphics.FillRectangle(speck, a.X + ex * (float)along - ey / reach * (float)across + shiftX[v], a.Y + ey * (float)along + ex / reach * (float)across + shiftY[v], dotSize, dotSize);
                            }
                        }
                Color[] colours = { Look.Red, Hot, Color.White, Ink, Shatter.Violet, Shatter.Mint };
                for (int v = 0; v < spines.Count; v++)
                {
                    PointF[] spine = spines[v];
                    if (spine.Length < 3) continue;
                    PointF tip = spine[spine.Length - 1];
                    int blocks = progress > 0.55 ? 2 + jitter.Next(4) : jitter.Next(3);
                    float scatter = (float)(6 + 30 * Math.Max(0, progress - 0.5)) * unit;
                    for (int k = 0; k < blocks; k++)
                    {
                        float side = (2 + jitter.Next(3)) * unit;
                        using (Brush block = new SolidBrush(Color.FromArgb((int)(255 * alpha), colours[jitter.Next(colours.Length)])))
                            graphics.FillRectangle(block, tip.X + shiftX[v] + (float)((jitter.NextDouble() * 2 - 1) * scatter), tip.Y + shiftY[v] + (float)((jitter.NextDouble() * 2 - 1) * scatter), side * (1 + jitter.Next(4)), side);
                    }
                }
                graphics.CompositingMode = CompositingMode.SourceCopy;
                using (Brush hole = new SolidBrush(Color.Transparent))
                    for (int k = 0, holes = jitter.Next(5); k < holes; k++)
                    {
                        PointF[] spine = spines[jitter.Next(spines.Count)];
                        PointF at = spine[jitter.Next(spine.Length)];
                        graphics.FillRectangle(hole, at.X - (4 + jitter.Next(10)) * unit, at.Y - unit, (8 + jitter.Next(20)) * unit, (1 + jitter.Next(3)) * unit);
                    }
            }
            int bands = progress < 0.1 ? 3 + jitter.Next(3) : progress > 0.55 ? 2 + jitter.Next(5) : jitter.Next(4);
            if (bands == 0) return;
            BitmapData data = image.LockBits(room, ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
            try
            {
                int[] row = new int[room.Width], moved = new int[room.Width];
                for (int b = 0; b < bands; b++)
                {
                    int top = jitter.Next(room.Height), tall = Math.Max(1, (int)((2 + jitter.Next(14)) * unit)), shift = (int)((jitter.Next(2) * 2 - 1) * (5 + jitter.Next(18)) * unit);
                    for (int y = top; y < Math.Min(room.Height, top + tall); y++)
                    {
                        IntPtr line = data.Scan0 + y * data.Stride;
                        Marshal.Copy(line, row, 0, room.Width);
                        for (int x = 0; x < room.Width; x++) { int from = x - shift; moved[x] = from >= 0 && from < room.Width ? row[from] : 0; }
                        Marshal.Copy(moved, 0, line, room.Width);
                    }
                }
            }
            finally { image.UnlockBits(data); }
        }

        private static void Ribbon(List<PointF[]> spines, List<float[]> widths, Random shape, Rectangle room, float x, float y, double aim, double length, double thick, double shown, double clock, float unit, int depth)
        {
            int count = depth == 2 ? 4 + shape.Next(4) : 2 + shape.Next(2);
            double[] parts = new double[count];
            double total = 0;
            bool hook = shape.NextDouble() < 0.35;
            for (int i = 0; i < count; i++) { parts[i] = (0.35 + 1.3 * shape.NextDouble()) * (hook && i == count - 1 ? 0.55 : 1); total += parts[i]; }
            PointF[] spine = new PointF[count + 1];
            float[] width = new float[count + 1];
            double[] headings = new double[count + 1];
            spine[0] = new PointF(x, y);
            width[0] = (float)(thick * 0.9);
            headings[0] = aim;
            int side = shape.Next(2) * 2 - 1;
            double phase = shape.NextDouble() * 6.28, start = aim;
            float walkX = x, walkY = y;
            for (int i = 1; i <= count; i++)
            {
                aim += (shape.NextDouble() - 0.5) * 0.32;
                if (shape.NextDouble() < 0.7) side = -side;
                double bend = hook && i == count ? 1 + shape.NextDouble() * 0.6 : 0.22 + shape.NextDouble() * 0.48;
                double heading = aim + side * bend, step = length * parts[i - 1] / total;
                walkX += (float)(Math.Cos(heading) * step);
                walkY += (float)(Math.Sin(heading) * step);
                spine[i] = new PointF(walkX, walkY);
                headings[i] = heading;
                double bulge = 0.7 + 0.6 * shape.NextDouble();
                width[i] = i == count ? 0 : (float)(thick * Math.Pow(1 - (double)i / count, 0.45) * bulge);
            }
            float fit = 1, margin = 6;
            for (int i = 1; i <= count; i++)
            {
                float ex = spine[i].X - x, ey = spine[i].Y - y;
                if (spine[i].X < room.Left + margin && ex < 0) fit = Math.Min(fit, (x - room.Left - margin) / -ex);
                if (spine[i].X > room.Right - margin && ex > 0) fit = Math.Min(fit, (room.Right - margin - x) / ex);
                if (spine[i].Y < room.Top + margin && ey < 0) fit = Math.Min(fit, (y - room.Top - margin) / -ey);
                if (spine[i].Y > room.Bottom - margin && ey > 0) fit = Math.Min(fit, (room.Bottom - margin - y) / ey);
            }
            fit = Math.Max(0.2f, fit);
            for (int i = 1; i <= count; i++) spine[i] = new PointF(x + (spine[i].X - x) * fit, y + (spine[i].Y - y) * fit);
            const int Fine = 3;
            int ends = count * Fine;
            PointF[] line = new PointF[ends + 1];
            float[] girths = new float[ends + 1];
            double[] arc = new double[ends + 1];
            line[0] = spine[0];
            girths[0] = width[0];
            for (int i = 1; i <= count; i++)
            {
                float ex = spine[i].X - spine[i - 1].X, ey = spine[i].Y - spine[i - 1].Y;
                double bow = (shape.NextDouble() - 0.5) * 0.22, swell = 1 + shape.NextDouble() * 0.3, pinch = i < count && shape.NextDouble() < 0.4 ? 0.35 + shape.NextDouble() * 0.25 : 1;
                for (int k = 1; k <= Fine; k++)
                {
                    int n = (i - 1) * Fine + k;
                    double t = (double)k / Fine, lift = bow * 4 * t * (1 - t);
                    line[n] = new PointF(spine[i - 1].X + ex * (float)t - ey * (float)lift, spine[i - 1].Y + ey * (float)t + ex * (float)lift);
                    girths[n] = (float)((width[i - 1] + (width[i] - width[i - 1]) * t) * (k == Fine ? pinch : swell));
                    arc[n] = arc[n - 1] + Math.Sqrt((line[n].X - line[n - 1].X) * (line[n].X - line[n - 1].X) + (line[n].Y - line[n - 1].Y) * (line[n].Y - line[n - 1].Y));
                }
            }
            int forks = depth == 2 ? 1 + shape.Next(2) : depth == 1 ? shape.Next(2) : 0;
            for (int f = 0; f < forks; f++)
            {
                int at = 1 + shape.Next(Math.Max(1, count - 2)), from = at * Fine;
                double turn = (shape.Next(2) * 2 - 1) * (0.55 + shape.NextDouble() * 0.5), rest = Math.Max(1, arc[ends] - arc[from]);
                double reach = rest * (0.4 + shape.NextDouble() * 0.25);
                Ribbon(spines, widths, shape, room, line[from].X, line[from].Y, headings[at] + turn, reach, width[at] * 0.6, Math.Max(0, Math.Min(1, (shown * arc[ends] - arc[from]) / rest)), clock, unit, depth - 1);
            }
            double visible = shown * arc[ends];
            if (visible < 2) return;
            double taper = Math.Max(4 * unit, arc[ends] * 0.14);
            float swayX = (float)-Math.Sin(start), swayY = (float)Math.Cos(start);
            List<PointF> points = new List<PointF>();
            List<float> sizes = new List<float>();
            for (int i = 0; i <= ends; i++)
            {
                PointF point = line[i];
                float girth = (float)(girths[i] * Math.Min(1, (visible - arc[i]) / taper));
                bool last = arc[i] >= visible;
                if (last)
                {
                    double t = (visible - arc[i - 1]) / Math.Max(0.001, arc[i] - arc[i - 1]);
                    point = new PointF(line[i - 1].X + (float)((line[i].X - line[i - 1].X) * t), line[i - 1].Y + (float)((line[i].Y - line[i - 1].Y) * t));
                    girth = 0;
                }
                float sway = (float)(Math.Sin(clock * 3.6 + i * 0.37 + phase) * 1.8 * unit * arc[i] / Math.Max(1, arc[ends]));
                points.Add(new PointF(point.X + swayX * sway, point.Y + swayY * sway));
                sizes.Add(girth);
                if (last) break;
            }
            spines.Add(points.ToArray());
            widths.Add(sizes.ToArray());
        }

        private static PointF[] Outline(PointF[] spine, float[] width, float scale, float grow, float extra, float shiftX, float shiftY)
        {
            int n = spine.Length;
            PointF[] edge = new PointF[n * 2];
            for (int i = 0; i < n; i++)
            {
                PointF a = spine[Math.Max(0, i - 1)], b = spine[i], c = spine[Math.Min(n - 1, i + 1)];
                double inX = b.X - a.X, inY = b.Y - a.Y, outX = c.X - b.X, outY = c.Y - b.Y;
                double inLength = Math.Sqrt(inX * inX + inY * inY), outLength = Math.Sqrt(outX * outX + outY * outY);
                if (inLength < 0.001) { inX = outX; inY = outY; inLength = outLength; }
                if (outLength < 0.001) { outX = inX; outY = inY; outLength = inLength; }
                if (inLength < 0.001) { inX = outX = 1; inY = outY = 0; inLength = outLength = 1; }
                double firstX = -inY / inLength, firstY = inX / inLength, nextX = -outY / outLength, nextY = outX / outLength;
                double mx = firstX + nextX, my = firstY + nextY, ml = Math.Sqrt(mx * mx + my * my);
                if (ml < 0.001) { mx = firstX; my = firstY; ml = 1; }
                mx /= ml;
                my /= ml;
                double half = (width[i] * grow + (width[i] > 0 ? extra : 0)) / 2 / Math.Max(0.4, mx * firstX + my * firstY);
                edge[i] = new PointF((float)((b.X + mx * half + shiftX) * scale), (float)((b.Y + my * half + shiftY) * scale));
                edge[n * 2 - 1 - i] = new PointF((float)((b.X - mx * half + shiftX) * scale), (float)((b.Y - my * half + shiftY) * scale));
            }
            return edge;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class Shatter : Overlay
    {
        internal sealed class Shard
        {
            internal float X;
            internal float Y;
            internal float Size;
            internal float Angle;
            internal float Spin;
            internal float Way;
            internal float Speed;
            internal float Rise;
            internal float Delay;
            internal Color Colour;
        }

        internal const double LeaveLength = 0.95;
        internal const double ArriveLength = 0.72;
        internal const double Handoff = 0.42;
        internal static readonly Color Violet = Color.FromArgb(122, 60, 255);
        internal static readonly Color Mint = Color.FromArgb(30, 240, 138);
        private readonly Timer ticks = new Timer();
        private readonly System.Diagnostics.Stopwatch watch = new System.Diagnostics.Stopwatch();
        private readonly Random random = new Random();
        private Bitmap picture;
        private List<Shard> shards;
        private Rectangle area;
        private Point at;
        private bool leaving;
        private float scale;
        private int seed;

        internal Shatter() : base("Capri shatter")
        {
            ticks.Interval = 33;
            ticks.Tick += delegate { Step(); };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00000020;
                return value;
            }
        }

        internal static Rectangle Room(Rectangle canvas) { return Rectangle.FromLTRB(canvas.Left - canvas.Width * 3 / 4, canvas.Top - canvas.Height, canvas.Right + canvas.Width * 3 / 4, canvas.Bottom + canvas.Height / 5); }

        internal void Play(Bitmap self, Point canvas, Rectangle screen, bool leave, int size)
        {
            Stop();
            area = Rectangle.Intersect(Room(new Rectangle(canvas, self.Size)), screen);
            if (area.Width < 8 || area.Height < 8) { self.Dispose(); return; }
            picture = self;
            scale = size / 240f;
            at = new Point(canvas.X - area.Left, canvas.Y - area.Top);
            leaving = leave;
            seed = random.Next();
            shards = Cut(self, scale, leave, seed);
            watch.Reset();
            watch.Start();
            Step();
            ticks.Start();
        }

        internal void Stop()
        {
            ticks.Stop();
            if (Visible) Hide();
            if (picture != null) { picture.Dispose(); picture = null; }
        }

        private void Step()
        {
            double t = watch.Elapsed.TotalSeconds;
            if (picture == null || t >= (leaving ? LeaveLength : ArriveLength)) { Stop(); return; }
            Present(Frame(area.Size, picture, at, shards, t, leaving, scale, seed, (int)(t * 30)), area.Location);
        }

        private static double Smooth(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        internal static List<Shard> Cut(Bitmap self, float scale, bool leave, int seed)
        {
            Random pick = new Random(seed);
            List<Shard> made = new List<Shard>();
            int step = Math.Max(6, (int)Math.Round(9 * scale));
            float middleX = self.Width / 2f, middleY = self.Height / 2f;
            Color[] accents = { Violet, Mint, Color.White, Look.Red, Look.Pink };
            for (int y = step / 2; y < self.Height; y += step)
                for (int x = step / 2; x < self.Width; x += step)
                {
                    Color seen = self.GetPixel(x, y);
                    if (seen.A < 140) continue;
                    Shard shard = new Shard();
                    shard.X = x + (float)(pick.NextDouble() - 0.5) * step * 0.6f;
                    shard.Y = y + (float)(pick.NextDouble() - 0.5) * step * 0.6f;
                    shard.Size = step * (0.55f + 0.6f * (float)pick.NextDouble());
                    shard.Angle = (float)(pick.NextDouble() * Math.PI);
                    shard.Spin = (float)((pick.NextDouble() * 2 - 1) * 9);
                    shard.Way = (float)(Math.Atan2(y - middleY, x - middleX) + (pick.NextDouble() * 2 - 1) * 0.9);
                    shard.Speed = (float)((170 + pick.NextDouble() * 430) * scale);
                    shard.Rise = (float)((40 + pick.NextDouble() * 200) * scale);
                    shard.Delay = (float)(leave ? 0.1 + 0.12 * (1 - y / (double)self.Height) + pick.NextDouble() * 0.14 : 0.04 + pick.NextDouble() * 0.12);
                    bool chunk = pick.Next(100) < 9;
                    if (chunk) shard.Size *= 2.2f;
                    shard.Colour = chunk || pick.Next(10) < 3 ? accents[pick.Next(accents.Length)] : Color.FromArgb(255, seen.R, seen.G, seen.B);
                    made.Add(shard);
                }
            return made;
        }

        internal static Bitmap Frame(Size size, Bitmap self, Point at, List<Shard> shards, double t, bool leave, float scale, int seed, int flicker)
        {
            Bitmap image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            Random pick = new Random(seed + flicker * 7919);
            double length = leave ? LeaveLength : ArriveLength;
            float middleX = at.X + self.Width / 2f, middleY = at.Y + self.Height * 0.45f, feet = at.Y + self.Height * 0.9f;
            using (Graphics graphics = Graphics.FromImage(image))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
                if (leave) Beam(graphics, middleX, 0, feet, t - 0.45, 0.06, 0.12, 0.33, true, scale, pick);
                else Beam(graphics, middleX, 0, feet, t, 0.08, 0.12, 0.26, false, scale, pick);
                double shown = leave ? 1 - Smooth((t - 0.14) / 0.2) : t < Handoff + 0.05 ? Smooth((t - 0.28) / 0.12) : 0;
                double glitch = leave ? Math.Min(1, 0.35 + t / 0.12) : 1 - Smooth((t - 0.34) / 0.1) * 0.6;
                if (shown > 0.01) Sliced(graphics, self, at, shown, glitch, scale, pick);
                PointF[] corners = new PointF[4];
                foreach (Shard shard in shards)
                {
                    float x, y, tall, alpha;
                    if (leave)
                    {
                        double life = t - shard.Delay;
                        if (life < 0) continue;
                        double travel = (1 - Math.Exp(-3 * life)) / 3;
                        x = shard.X + (float)(Math.Cos(shard.Way) * shard.Speed * travel);
                        y = shard.Y + (float)(Math.Sin(shard.Way) * shard.Speed * travel * 0.6 - shard.Rise * life);
                        tall = shard.Size * (float)(1 - 0.55 * Smooth(life / 0.7));
                        alpha = (float)(1 - Smooth((t - (length - 0.32)) / 0.32));
                    }
                    else
                    {
                        double land = 0.2 + shard.Delay, start = land - 0.24;
                        if (t < start) continue;
                        double left = 1 - Smooth((t - start) / 0.24);
                        x = shard.X + (float)(Math.Cos(shard.Way) * shard.Speed * 0.38 * left);
                        y = shard.Y + (float)(Math.Sin(shard.Way) * shard.Speed * 0.38 * left * 0.6 - shard.Rise * 0.5 * left);
                        tall = shard.Size * (float)(0.6 + 0.4 * (1 - left));
                        alpha = (float)(Smooth((t - start) / 0.1) * (1 - Smooth((t - Handoff) / 0.06)));
                    }
                    if (alpha <= 0.01 || tall <= 0.5f) continue;
                    double turn = shard.Angle + shard.Spin * t;
                    float c = (float)Math.Cos(turn), s = (float)Math.Sin(turn), wide = tall * 0.5f, px = at.X + x, py = at.Y + y;
                    corners[0] = new PointF(px + tall * s, py - tall * c);
                    corners[1] = new PointF(px + wide * c, py + wide * s);
                    corners[2] = new PointF(px - tall * s, py + tall * c);
                    corners[3] = new PointF(px - wide * c, py - wide * s);
                    using (Brush fill = new SolidBrush(Color.FromArgb((int)(255 * alpha), shard.Colour))) graphics.FillPolygon(fill, corners);
                }
                if (!leave) Lines(graphics, middleX, middleY, (t - 0.3) / 0.42, scale, seed);
            }
            return image;
        }

        private static void Beam(Graphics graphics, float middle, float top, float bottom, double b, double rise, double hold, double fall, bool up, float scale, Random pick)
        {
            if (b < 0 || b > rise + hold + fall) return;
            double grown = Smooth(b / rise);
            double wide = 30 * scale * (b < rise ? grown : 1 - Smooth((b - rise - hold) / fall));
            if (wide < 0.6) return;
            float upper = up ? (float)(bottom - (bottom - top) * grown) : top;
            float lower = up ? bottom : (float)(top + (bottom - top) * grown);
            if (lower - upper < 2) return;
            float w = (float)wide, rows = Math.Max(8, 14 * scale);
            List<PointF> left = new List<PointF>(), right = new List<PointF>();
            for (float y = upper; ; y += rows)
            {
                float row = Math.Min(y, lower);
                left.Add(new PointF(middle - w / 2 - (float)(pick.NextDouble() * w * 0.35), row));
                right.Add(new PointF(middle + w / 2 + (float)(pick.NextDouble() * w * 0.35), row));
                if (row >= lower) break;
            }
            right.Reverse();
            List<PointF> outline = new List<PointF>(left);
            outline.AddRange(right);
            RectangleF haze = new RectangleF(middle - w * 1.4f, upper, w * 2.8f, lower - upper);
            using (LinearGradientBrush glow = new LinearGradientBrush(haze, Color.Transparent, Color.Transparent, LinearGradientMode.Horizontal))
            {
                ColorBlend blend = new ColorBlend();
                blend.Colors = new[] { Color.FromArgb(0, Violet), Color.FromArgb(90, Violet), Color.FromArgb(0, Violet) };
                blend.Positions = new[] { 0f, 0.5f, 1f };
                glow.InterpolationColors = blend;
                graphics.FillRectangle(glow, haze);
            }
            using (Pen edge = new Pen(Violet, Math.Max(2, 3 * scale))) graphics.DrawPolygon(edge, outline.ToArray());
            using (Brush body = new SolidBrush(Look.Red)) graphics.FillPolygon(body, outline.ToArray());
            using (Brush core = new SolidBrush(Color.FromArgb(245, 255, 255, 255))) graphics.FillRectangle(core, middle - w * 0.16f, upper, w * 0.32f, lower - upper);
            using (Brush flash = new SolidBrush(Color.FromArgb((int)(150 * Math.Min(1, wide / (30 * scale))), 255, 255, 255))) graphics.FillEllipse(flash, middle - w * 1.6f, lower - w * 0.35f, w * 3.2f, w * 0.7f);
        }

        private static void Sliced(Graphics graphics, Bitmap self, Point at, double shown, double glitch, float scale, Random pick)
        {
            using (ImageAttributes plain = Tint(Color.Empty, shown))
            using (ImageAttributes green = Tint(Mint, shown * 0.6 * glitch))
            using (ImageAttributes violet = Tint(Violet, shown * 0.6 * glitch))
            {
                float shift = (float)(4 * scale * glitch);
                for (int top = 0; top < self.Height; )
                {
                    int tall = Math.Min(self.Height - top, Math.Max(3, (int)(self.Height * (0.03 + pick.NextDouble() * 0.13))));
                    float off = pick.Next(3) == 0 ? (float)((pick.NextDouble() * 2 - 1) * 18 * scale * glitch) : 0;
                    foreach (KeyValuePair<ImageAttributes, float> layer in new[] { new KeyValuePair<ImageAttributes, float>(green, off - shift), new KeyValuePair<ImageAttributes, float>(violet, off + shift), new KeyValuePair<ImageAttributes, float>(plain, off) })
                        graphics.DrawImage(self, new Rectangle(at.X + (int)Math.Round(layer.Value), at.Y + top, self.Width, tall), 0, top, self.Width, tall, GraphicsUnit.Pixel, layer.Key);
                    top += tall;
                }
            }
            Color[] bars = { Color.White, Mint, Look.Red, Violet };
            for (int i = 0, count = (int)Math.Round(5 * glitch); i < count; i++)
            {
                int y = at.Y + (int)(self.Height * (0.1 + pick.NextDouble() * 0.8));
                int width = (int)(self.Width * (0.25 + pick.NextDouble() * 0.5));
                int x = at.X + (int)(self.Width * (0.5 + (pick.NextDouble() - 0.5) * 0.5)) - width / 2;
                using (Brush bar = new SolidBrush(Color.FromArgb((int)(210 * shown), bars[pick.Next(bars.Length)]))) graphics.FillRectangle(bar, x, y, width, Math.Max(2, (int)((1 + pick.Next(3)) * scale)));
            }
        }

        private static ImageAttributes Tint(Color colour, double alpha)
        {
            ImageAttributes tint = new ImageAttributes();
            float a = (float)Math.Max(0, Math.Min(1, alpha));
            ColorMatrix matrix = colour.IsEmpty
                ? new ColorMatrix(new[] { new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 0, 0, 1, 0, 0 }, new float[] { 0, 0, 0, a, 0 }, new float[] { 0, 0, 0, 0, 1 } })
                : new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, a, 0 }, new float[] { colour.R / 255f, colour.G / 255f, colour.B / 255f, 0, 1 } });
            tint.SetColorMatrix(matrix);
            return tint;
        }

        private static void Lines(Graphics graphics, float middleX, float middleY, double u, float scale, int seed)
        {
            if (u <= 0 || u >= 1) return;
            Random pick = new Random(seed);
            Color[] colours = { Color.White, Color.White, Color.White, Look.Red, Violet };
            float near = 120 * scale * (float)(0.7 + 0.9 * Smooth(u));
            int alpha = (int)(235 * (1 - Smooth((u - 0.35) / 0.65)));
            for (int i = 0; i < 28; i++)
            {
                double angle = (i + pick.NextDouble() * 0.7) / 28 * 2 * Math.PI;
                float inner = near * (float)(0.85 + pick.NextDouble() * 0.5), outer = inner + 150 * scale * (float)(0.5 + pick.NextDouble());
                float thick = (float)((2 + pick.NextDouble() * 6) * scale);
                Color colour = colours[pick.Next(colours.Length)];
                float c = (float)Math.Cos(angle), s = (float)Math.Sin(angle);
                PointF[] wedge = { new PointF(middleX + c * inner, middleY + s * inner * 0.8f), new PointF(middleX + c * outer - s * thick, middleY + (s * outer + c * thick) * 0.8f), new PointF(middleX + c * outer + s * thick, middleY + (s * outer - c * thick) * 0.8f) };
                using (Brush fill = new SolidBrush(Color.FromArgb(alpha, colour))) graphics.FillPolygon(fill, wedge);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Dispose(); if (picture != null) { picture.Dispose(); picture = null; } }
            base.Dispose(disposing);
        }
    }

    internal sealed class Takeover : Overlay
    {
        internal const double FaceAt = 0.12;
        internal const double FaceIn = 0.3;
        internal const double FlyAt = 0.95;
        internal const double FlyLength = 0.45;
        internal const double BadgeAt = 1.3;
        internal const double Settled = 1.75;
        internal const double LeaveLength = 0.32;
        internal const double VineLength = 1.6;
        internal static readonly RectangleF FacePlate = new RectangleF(140, 145, 110, 110);
        internal static readonly RectangleF Zoom = new RectangleF(45, 45, 300, 300);
        private static readonly StringFormat Spaced = Typographic();
        // Her face tearing through the window first is off for now (too intrusive, Lachy 27 Oct 2026); the badge and brackets still show.
        internal static bool HackIn;
        internal static Bitmap Face;
        private static Bitmap painted, minted, bruised;
        private static RectangleF paintedFrom;
        private readonly Timer ticks = new Timer();
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        private readonly Random random = new Random();
        private IntPtr window;
        private string app = "";
        private Rectangle box;
        private Rectangle screen;
        private Rectangle shownBox;
        private double started = -1;
        private double leaving = -1;
        private double nextFlicker;
        private double flickerUntil;
        private bool clean;
        private int seed;
        private float scale = 1;

        internal Takeover() : base("Capri control")
        {
            ticks.Interval = 33;
            ticks.Tick += delegate { Step(); };
        }

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00000020;
                return value;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { SetWindowDisplayAffinity(Handle, 0x11); } catch (EntryPointNotFoundException) { }
        }

        internal bool Active { get { return started >= 0 && leaving < 0; } }

        internal IntPtr Window { get { return window; } }

        private double Now { get { return watch.Elapsed.TotalSeconds; } }

        internal void Follow(IntPtr handle, string name, Rectangle bounds, float size)
        {
            Rectangle seen;
            if (handle == IntPtr.Zero) return;
            scale = Math.Max(0.8f, Math.Min(1.6f, size));
            if (Ledges.Showing(handle, out seen)) bounds = seen;
            if (bounds.Width < 60 || bounds.Height < 40) return;
            app = name;
            if (Active && handle == window) return;
            bool moving = Active;
            window = handle;
            box = bounds;
            screen = Screen.FromRectangle(bounds).Bounds;
            leaving = -1;
            clean = false;
            if (!moving) { started = HackIn ? Now : Now - Settled; seed = random.Next(); }
            else if (Now - started > Settled) started = Now - (HackIn ? BadgeAt : Settled);
            nextFlicker = Now + 4 + random.NextDouble() * 3;
            ticks.Start();
            Step();
        }

        internal void Release()
        {
            if (!Active) return;
            leaving = Now;
            clean = false;
        }

        internal void Stop()
        {
            ticks.Stop();
            started = -1;
            leaving = -1;
            window = IntPtr.Zero;
            if (Visible) Hide();
        }

        private void Step()
        {
            double now = Now, t = now - started;
            if (started < 0) { ticks.Stop(); return; }
            if (leaving >= 0 && now - leaving >= LeaveLength) { Stop(); return; }
            Rectangle seen;
            if (Ledges.Showing(window, out seen)) box = seen;
            else if (leaving < 0) { Release(); now = Now; }
            bool moving = t < Settled || leaving >= 0;
            if (!moving && now >= nextFlicker) { flickerUntil = now + 0.12; nextFlicker = now + 4 + random.NextDouble() * 3; }
            bool flicking = now < flickerUntil;
            if (!moving && !flicking && clean && box == shownBox) return;
            Rectangle area = t < Settled ? screen : Rectangle.Intersect(Rectangle.Inflate(box, 8, 8), screen);
            if (area.Width < 8 || area.Height < 8) return;
            Rectangle inside = new Rectangle(box.X - area.X, box.Y - area.Y, box.Width, box.Height);
            Present(Frame(area.Size, inside, app, t, leaving >= 0 ? (now - leaving) / LeaveLength : 0, flicking ? seed + (int)(now * 30) : -1, Face, scale, seed), area.Location);
            clean = !moving && !flicking;
            shownBox = box;
        }

        private static StringFormat Typographic()
        {
            StringFormat format = (StringFormat)StringFormat.GenericTypographic.Clone();
            format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces;
            return format;
        }

        private static double Smooth(double x)
        {
            x = Math.Max(0, Math.Min(1, x));
            return x * x * (3 - 2 * x);
        }

        private static double Back(double x)
        {
            x = Math.Max(0, Math.Min(1, x)) - 1;
            return x * x * (2.7 * x + 1.7) + 1;
        }

        internal static Bitmap Frame(Size size, Rectangle box, string app, double t, double gone, int glitch, Bitmap face, float scale, int seed)
        {
            Bitmap image = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppPArgb);
            double fade = 1 - Smooth(gone);
            if (fade <= 0) return image;
            Random pick = new Random(glitch >= 0 ? glitch : (int)(t * 30) * 7919 + 13);
            float unit = Math.Max(0.6f, Math.Min(1.6f, box.Height / 560f)), hole = (float)Math.Min(box.Height * 0.24, size.Height * 0.28);
            PointF centre = new PointF(box.Left + box.Width / 2f, box.Top + box.Height / 2f);
            double torn = Back((t - FaceAt) / 0.24), vine = (t - 0.08) / (VineLength - 0.08);
            float ring = hole * 0.75f * (float)Math.Max(0.15, Math.Min(1, torn));
            if (vine > 0 && t < VineLength) Tear.Tendrils(image, Rectangle.Inflate(box, box.Width / 30, box.Height / 30), centre, new SizeF(ring, ring), unit, gone > 0 ? Math.Max(vine, 0.8 + 0.17 * gone) : vine, seed, (int)(t * 30));
            using (Graphics graphics = Graphics.FromImage(image))
            using (Font font = Look.Mono(11 * scale, FontStyle.Bold))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                string lead = "CAPRI IS CONTROLLING ", name = app.ToUpperInvariant();
                float tall = 26 * scale, icon = 20 * scale, pad = 7 * scale;
                float leadWide = graphics.MeasureString(lead, font, PointF.Empty, Spaced).Width;
                float nameWide = graphics.MeasureString(name, font, PointF.Empty, Spaced).Width;
                float wide = pad + icon + pad + leadWide + nameWide + pad * 1.4f;
                float top = Math.Max(4 * scale, box.Top + 6 * scale);
                float middle = box.Left + box.Width / 2f;
                RectangleF iconAt = new RectangleF(middle - wide / 2 + pad, top + (tall - icon) / 2, icon, icon);
                if (t < 0.2) Hits(graphics, box, t / 0.2 + gone, unit, pick);
                if (t < FlyAt + FlyLength + 0.1) Border(graphics, box, Smooth((t - 0.08) / 0.2) * (1 - Smooth((t - FlyAt - FlyLength * 0.5) / 0.3)) * fade, scale, pick);
                if (t >= FlyAt + FlyLength * 0.6) Brackets(graphics, box, Smooth((t - FlyAt - FlyLength * 0.6) / 0.25) * fade, scale);
                if (t >= BadgeAt)
                {
                    double open = Smooth((t - BadgeAt) / 0.18);
                    float shownWide = icon + pad * 2 + (float)((wide - icon - pad * 2) * open);
                    float shake = glitch >= 0 ? (float)((pick.NextDouble() * 2 - 1) * 6 * scale) : 0;
                    RectangleF slab = new RectangleF(middle - wide / 2 + shake, top, shownWide, tall);
                    Badge(graphics, slab, iconAt, lead, name, font, pad, icon, Smooth((t - BadgeAt - 0.08) / 0.3), fade, face, glitch >= 0, pick, scale);
                }
                if (face != null && t >= FaceAt && t < FlyAt + FlyLength)
                {
                    double fly = Smooth((t - FlyAt) / FlyLength);
                    float bob = (float)(Math.Sin(t * 7) * 3 * scale * (1 - fly));
                    PointF from = new PointF(centre.X, centre.Y + bob);
                    PointF to = new PointF(iconAt.X + icon / 2, iconAt.Y + icon / 2);
                    double flick = t < FaceAt + 0.2 && pick.Next(3) == 0 ? 0.35 : 1;
                    for (int ghost = 2; ghost >= 0; ghost--)
                    {
                        double p = Math.Max(0, fly - ghost * 0.09);
                        if (ghost > 0 && (fly <= 0 || fly >= 1)) continue;
                        float side = (float)(Math.Exp(Math.Log(hole * 2) + (Math.Log(icon) - Math.Log(hole * 2)) * p) * Math.Max(0.02, torn));
                        PointF at = new PointF(from.X + (to.X - from.X) * (float)p, from.Y + (to.Y - from.Y) * (float)p);
                        float q = (float)p;
                        RectangleF source = new RectangleF(Zoom.X + (FacePlate.X - Zoom.X) * q, Zoom.Y + (FacePlate.Y - Zoom.Y) * q, Zoom.Width + (FacePlate.Width - Zoom.Width) * q, Zoom.Height + (FacePlate.Height - Zoom.Height) * q);
                        double slices = ghost > 0 ? 0.6 : t < FaceAt + FaceIn ? 1 - (t - FaceAt) / FaceIn : pick.Next(9) == 0 ? 0.5 : 0;
                        Portal(graphics, face, at, side, source, (ghost == 0 ? flick : 0.32 / ghost) * fade, slices, ghost == 1 ? Shatter.Violet : ghost == 2 ? Shatter.Mint : Color.Empty, scale, unit, p, seed, pick);
                    }
                }
            }
            return image;
        }

        private static Bitmap Baked(Bitmap face, int span, RectangleF source, Color tint)
        {
            Bitmap art = new Bitmap(span, span, PixelFormat.Format32bppPArgb);
            using (Graphics paint = Graphics.FromImage(art))
            using (ImageAttributes look = Tint(tint, tint.IsEmpty ? 1 : 0.55))
            {
                paint.InterpolationMode = InterpolationMode.HighQualityBilinear;
                paint.PixelOffsetMode = PixelOffsetMode.Half;
                paint.DrawImage(face, new Rectangle(0, 0, span, span), source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel, look);
            }
            return art;
        }

        private static void Portal(Graphics graphics, Bitmap face, PointF at, float side, RectangleF source, double alpha, double slices, Color tint, float scale, float unit, double square, int seed, Random pick)
        {
            if (side < 2 || alpha <= 0.01) return;
            Random shape = new Random(seed ^ 0x5bd1);
            const int Points = 14;
            PointF[] edge = new PointF[Points], rim = new PointF[Points], lip = new PointF[Points];
            float band = Math.Max(3 * unit, side * 0.05f), drop = 3 * unit * (float)(1 - square);
            for (int i = 0; i < Points; i++)
            {
                double angle = (i + 0.3 * shape.NextDouble()) / Points * 2 * Math.PI - Math.PI / 2;
                double jag = i % 2 == 0 ? 0.94 + 0.16 * shape.NextDouble() : 0.6 + 0.2 * shape.NextDouble();
                double reach = (jag + (1 / Math.Max(Math.Abs(Math.Cos(angle)), Math.Abs(Math.Sin(angle))) - jag) * square) * side / 2;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                edge[i] = new PointF(at.X + cos * (float)reach, at.Y + sin * (float)reach);
                rim[i] = new PointF(at.X + cos * ((float)reach + band), at.Y + sin * ((float)reach + band));
                lip[i] = new PointF(rim[i].X - drop, rim[i].Y + drop);
            }
            int a = (int)(255 * Math.Max(0, Math.Min(1, alpha)));
            if (!tint.IsEmpty)
            {
                using (Brush ghost = new SolidBrush(Color.FromArgb(a, tint))) graphics.FillPolygon(ghost, rim);
                return;
            }
            using (Brush violet = new SolidBrush(Color.FromArgb(a, Shatter.Violet))) graphics.FillPolygon(violet, lip);
            using (Brush red = new SolidBrush(Color.FromArgb(a, 236, 6, 68))) graphics.FillPolygon(red, rim);
            using (Brush dark = new SolidBrush(Color.FromArgb(a, 10, 8, 14))) graphics.FillPolygon(dark, edge);
            int span = Math.Max(2, (int)Math.Ceiling(side * 1.08f));
            if (painted == null || painted.Width != span || paintedFrom != source)
            {
                foreach (Bitmap old in new[] { painted, minted, bruised }) if (old != null) old.Dispose();
                painted = Baked(face, span, source, Color.Empty);
                minted = bruised = null;
                paintedFrom = source;
            }
            Bitmap art = painted;
            if (slices > 0.01 || alpha < 0.999)
            {
                if (slices > 0.25 && minted == null)
                {
                    minted = Baked(face, span, source, Shatter.Mint);
                    bruised = Baked(face, span, source, Shatter.Violet);
                }
                art = new Bitmap(span, span, PixelFormat.Format32bppPArgb);
                using (Graphics paint = Graphics.FromImage(art))
                using (ImageAttributes look = Tint(Color.Empty, alpha))
                {
                    paint.InterpolationMode = InterpolationMode.NearestNeighbor;
                    int shift = (int)(6 * scale * slices);
                    for (float done = 0; done < 1; )
                    {
                        float part = slices <= 0.01 ? 1 : (float)Math.Min(1 - done, 0.05 + pick.NextDouble() * 0.15);
                        int top = (int)(span * done), tall = Math.Max(1, (int)Math.Ceiling(span * part));
                        int off = slices > 0.01 && pick.Next(3) == 0 ? (int)((pick.NextDouble() * 2 - 1) * 28 * scale * slices) : 0;
                        if (slices > 0.25)
                        {
                            paint.DrawImage(minted, new Rectangle(off - shift, top, span, tall), 0, top, span, tall, GraphicsUnit.Pixel);
                            paint.DrawImage(bruised, new Rectangle(off + shift, top, span, tall), 0, top, span, tall, GraphicsUnit.Pixel);
                        }
                        paint.DrawImage(painted, new Rectangle(off, top, span, tall), 0, top, span, tall, GraphicsUnit.Pixel, look);
                        done += part;
                    }
                }
            }
            InterpolationMode smooth = graphics.InterpolationMode;
            PixelOffsetMode offset = graphics.PixelOffsetMode;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.None;
            using (TextureBrush texture = new TextureBrush(art, WrapMode.Clamp))
            {
                texture.TranslateTransform((float)Math.Round(at.X - span / 2f), (float)Math.Round(at.Y - span / 2f));
                graphics.FillPolygon(texture, edge);
            }
            graphics.InterpolationMode = smooth;
            graphics.PixelOffsetMode = offset;
            if (art != painted) art.Dispose();
            using (Pen seam = new Pen(Color.FromArgb(a, 10, 8, 14), Math.Max(1.2f, 1.6f * unit))) graphics.DrawPolygon(seam, edge);
        }

        private static void Hits(Graphics graphics, Rectangle box, double t, float unit, Random pick)
        {
            double fade = 1 - Smooth(t);
            if (fade <= 0.01) return;
            Color[] colours = { Look.Red, Color.FromArgb(10, 8, 14), Look.Pink, Shatter.Mint, Shatter.Violet };
            for (int i = 0, bars = 4 + pick.Next(5); i < bars; i++)
            {
                float wide = box.Width * (float)(0.25 + pick.NextDouble() * 0.65), tall = (1 + pick.Next(5)) * unit;
                using (Brush bar = new SolidBrush(Color.FromArgb((int)(220 * fade), colours[pick.Next(colours.Length)])))
                    graphics.FillRectangle(bar, box.Left + (float)pick.NextDouble() * (box.Width - wide), box.Top + (float)pick.NextDouble() * box.Height, wide, tall);
            }
        }

        private static Rectangle Rect(RectangleF area) { return Rectangle.Round(area); }

        private static ImageAttributes Tint(Color colour, double alpha)
        {
            ImageAttributes tint = new ImageAttributes();
            float a = (float)Math.Max(0, Math.Min(1, alpha));
            tint.SetColorMatrix(colour.IsEmpty
                ? new ColorMatrix(new[] { new float[] { 1, 0, 0, 0, 0 }, new float[] { 0, 1, 0, 0, 0 }, new float[] { 0, 0, 1, 0, 0 }, new float[] { 0, 0, 0, a, 0 }, new float[] { 0, 0, 0, 0, 1 } })
                : new ColorMatrix(new[] { new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, 0, 0 }, new float[] { 0, 0, 0, a, 0 }, new float[] { colour.R / 255f, colour.G / 255f, colour.B / 255f, 0, 1 } }));
            return tint;
        }

        private static void Border(Graphics graphics, Rectangle box, double alpha, float scale, Random pick)
        {
            if (alpha <= 0.01) return;
            List<PointF> edge = new List<PointF>();
            float step = 18 * scale, kick = 5 * scale;
            for (float x = box.Left; x < box.Right; x += step) edge.Add(new PointF(x, box.Top + (float)(pick.NextDouble() * 2 - 1) * kick));
            for (float y = box.Top; y < box.Bottom; y += step) edge.Add(new PointF(box.Right + (float)(pick.NextDouble() * 2 - 1) * kick, y));
            for (float x = box.Right; x > box.Left; x -= step) edge.Add(new PointF(x, box.Bottom + (float)(pick.NextDouble() * 2 - 1) * kick));
            for (float y = box.Bottom; y > box.Top; y -= step) edge.Add(new PointF(box.Left + (float)(pick.NextDouble() * 2 - 1) * kick, y));
            PointF[] points = edge.ToArray();
            Color[] colours = { Shatter.Violet, Color.FromArgb(40, 230, 255), Look.Pink };
            for (int i = 0; i < colours.Length; i++)
                using (Pen pen = new Pen(Color.FromArgb((int)(220 * alpha), colours[i]), (i == 2 ? 2.4f : 2f) * scale))
                {
                    pen.LineJoin = LineJoin.Miter;
                    using (Matrix shift = new Matrix())
                    {
                        shift.Translate((i - 1) * 2.5f * scale, 0);
                        PointF[] moved = (PointF[])points.Clone();
                        shift.TransformPoints(moved);
                        graphics.DrawPolygon(pen, moved);
                    }
                }
        }

        private static void Brackets(Graphics graphics, Rectangle box, double alpha, float scale)
        {
            if (alpha <= 0.01) return;
            float reach = 22 * scale, thick = 3 * scale;
            foreach (int pass in new[] { 0, 1 })
                using (Pen pen = new Pen(Color.FromArgb((int)(230 * alpha), pass == 0 ? Shatter.Violet : Look.Pink), thick))
                {
                    pen.StartCap = LineCap.Square;
                    pen.EndCap = LineCap.Square;
                    float o = pass == 0 ? 2 * scale : 0, l = box.Left + 2 + o, r = box.Right - 3 + o, u = box.Top + 2 + o, d = box.Bottom - 3 + o;
                    graphics.DrawLines(pen, new[] { new PointF(l, u + reach), new PointF(l, u), new PointF(l + reach, u) });
                    graphics.DrawLines(pen, new[] { new PointF(r - reach, u), new PointF(r, u), new PointF(r, u + reach) });
                    graphics.DrawLines(pen, new[] { new PointF(l, d - reach), new PointF(l, d), new PointF(l + reach, d) });
                    graphics.DrawLines(pen, new[] { new PointF(r - reach, d), new PointF(r, d), new PointF(r, d - reach) });
                }
        }

        private static void Badge(Graphics graphics, RectangleF slab, RectangleF iconAt, string lead, string name, Font font, float pad, float icon, double typed, double fade, Bitmap face, bool glitch, Random pick, float scale)
        {
            int a = (int)(255 * fade);
            using (GraphicsPath shape = new GraphicsPath())
            {
                float cut = slab.Height * 0.35f;
                shape.AddPolygon(new[] { new PointF(slab.Left + cut, slab.Top), new PointF(slab.Right, slab.Top), new PointF(slab.Right - cut, slab.Bottom), new PointF(slab.Left, slab.Bottom) });
                using (Brush lip = new SolidBrush(Color.FromArgb(a, Shatter.Violet)))
                using (Matrix down = new Matrix())
                using (GraphicsPath under = (GraphicsPath)shape.Clone())
                {
                    down.Translate(2 * scale, 3 * scale);
                    under.Transform(down);
                    graphics.FillPath(lip, under);
                }
                using (LinearGradientBrush fill = new LinearGradientBrush(slab, Color.FromArgb(a * 94 / 100, 34, 22, 44), Color.FromArgb(a * 96 / 100, 12, 8, 18), LinearGradientMode.Vertical)) graphics.FillPath(fill, shape);
                using (Pen edge = new Pen(Color.FromArgb(a, Look.Red), 1.6f * scale)) graphics.DrawPath(edge, shape);
                using (Region was = graphics.Clip)
                {
                graphics.SetClip(shape, CombineMode.Intersect);
                float x = iconAt.Left + icon + pad;
                float y = slab.Top + (slab.Height - font.GetHeight(graphics)) / 2 + 1;
                int shown = (int)Math.Round((lead.Length + name.Length) * Math.Max(0, Math.Min(1, typed)));
                string leadShown = lead.Substring(0, Math.Min(lead.Length, shown)), nameShown = shown > lead.Length ? name.Substring(0, Math.Min(name.Length, shown - lead.Length)) : "";
                using (Brush pink = new SolidBrush(Color.FromArgb(a, Look.Pink)))
                using (Brush white = new SolidBrush(Color.FromArgb(a, 255, 255, 255)))
                {
                    graphics.DrawString(leadShown, font, pink, x, y, Spaced);
                    if (nameShown.Length > 0) graphics.DrawString(nameShown, font, white, x + graphics.MeasureString(lead, font, PointF.Empty, Spaced).Width, y, Spaced);
                }
                if (glitch)
                    using (Brush bar = new SolidBrush(Color.FromArgb(a * 3 / 4, pick.Next(2) == 0 ? Shatter.Mint : Look.Pink)))
                        graphics.FillRectangle(bar, slab.Left, slab.Top + (float)pick.NextDouble() * slab.Height, slab.Width * (float)(0.3 + pick.NextDouble() * 0.6), Math.Max(1, 2 * scale));
                graphics.Clip = was;
                }
            }
            using (Brush back = new SolidBrush(Color.FromArgb(a, 10, 8, 14))) graphics.FillRectangle(back, iconAt);
            if (face != null)
                using (ImageAttributes look = Tint(Color.Empty, fade))
                    graphics.DrawImage(face, Rect(iconAt), FacePlate.X, FacePlate.Y, FacePlate.Width, FacePlate.Height, GraphicsUnit.Pixel, look);
            using (Pen ring = new Pen(Color.FromArgb(a, Shatter.Mint), 1.4f * scale)) graphics.DrawRectangle(ring, iconAt.X, iconAt.Y, iconAt.Width, iconAt.Height);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }
    }

    // Her own pink pointer for mouse jobs: it glides to each target and ripples where she clicks, while their mouse
    // stays where they left it. Clicks go straight through it, recordings show it, and her own screenshots paint it out.
    internal sealed class HerPointer : Overlay
    {
        internal const double Ripple = 0.38;
        private readonly Timer ticks = new Timer();
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        private readonly object gate = new object();
        private IntPtr window;
        private Bitmap arrow;
        private Point tip;
        private int reach = 40;
        private float scale = 1;
        private double tapped = -1;
        internal readonly CursorBolts Bolts = new CursorBolts();

        internal HerPointer() : base("Capri pointer")
        {
            ticks.Interval = 33;
            ticks.Tick += delegate { Step(); };
        }

        [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams value = base.CreateParams;
                value.ExStyle |= 0x00000020;
                return value;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            window = Handle;
        }

        internal Point Tip { get { lock (gate) return tip; } }

        internal bool Rippling { get { return tapped >= 0; } }

        // Her job thread calls this on every step of a glide, so it only posts the move to the window's own thread.
        internal void MoveTo(Point at)
        {
            int edge;
            lock (gate) { tip = at; edge = reach; }
            IntPtr handle = window;
            if (handle != IntPtr.Zero) SetWindowPos(handle, new IntPtr(-1), at.X - edge, at.Y - edge, 0, 0, 0x0001 | 0x0010 | 0x4000);
        }

        internal void Begin(Point at)
        {
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, screen.DpiX / 96f);
            int size = (int)Math.Round(36 * scale);
            if (arrow == null || arrow.Width != size) { if (arrow != null) arrow.Dispose(); arrow = Look.PointerArt(size); }
            lock (gate) { tip = at; reach = (int)Math.Round(90 * scale); }
            tapped = -1;
            Render();
            if (Bolts.Look.On) ticks.Start();
        }

        internal void Tap()
        {
            if (!Visible) return;
            tapped = watch.Elapsed.TotalSeconds;
            Bolts.Tap();
            ticks.Start();
            Render();
        }

        internal void End()
        {
            ticks.Stop();
            tapped = -1;
            Bolts.Clear();
            if (Visible) Hide();
        }

        private void Step()
        {
            if (tapped >= 0 && watch.Elapsed.TotalSeconds - tapped > Ripple) { tapped = -1; if (!Bolts.Look.On) ticks.Stop(); }
            if (Visible) Render();
        }

        internal void Render()
        {
            if (arrow == null) return;
            Point at = Tip;
            Bitmap frame = new Bitmap(reach * 2, reach * 2, PixelFormat.Format32bppArgb);
            using (Graphics graphics = Graphics.FromImage(frame))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                if (tapped >= 0)
                {
                    double t = Math.Min(1, (watch.Elapsed.TotalSeconds - tapped) / Ripple);
                    float eased = (float)(1 - Math.Pow(1 - t, 3));
                    float radius = (5 + 17 * eased) * scale;
                    int alpha = (int)Math.Round(230 * (1 - t));
                    using (Pen cyan = new Pen(Color.FromArgb(alpha / 2, Look.Cyan), 2f * scale)) graphics.DrawEllipse(cyan, reach - radius + 1.5f * scale, reach - radius + 0.5f * scale, radius * 2, radius * 2);
                    using (Pen pink = new Pen(Color.FromArgb(alpha, Look.Pink), (2.6f - 1.2f * eased) * scale)) graphics.DrawEllipse(pink, reach - radius, reach - radius, radius * 2, radius * 2);
                }
                float s = arrow.Width / 32f;
                double now = watch.Elapsed.TotalSeconds;
                Bolts.Step(now, at, scale);
                Bolts.Draw(graphics, new PointF(reach, reach), at, now, scale);
                graphics.DrawImage(arrow, reach - 2 * s, reach - 2 * s, arrow.Width, arrow.Height);
            }
            Present(frame, new Point(at.X - reach, at.Y - reach));
            // A window she clicks may be always on top too; her pointer stays above it, like a real cursor.
            SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Dispose(); if (arrow != null) { arrow.Dispose(); arrow = null; } }
            base.Dispose(disposing);
        }
    }

    // "Capri is working on your task": each step of a mouse and keyboard job as it happens, in her speech box look.
    // It sits in the bottom-right corner until they drag it somewhere else, and she sits on top of it while she works.
    internal sealed class TaskPanel : Overlay
    {
        private const int Columns = 36;
        private const int MostRows = 7;
        private const string Speaker = "CAPRI: ";
        private readonly Timer ticks = new Timer();
        private readonly System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
        private readonly List<JobStep> steps = new List<JobStep>();
        private object owner;
        private string words = "";
        private string busy;
        private string outcome;
        private string ending = "";
        private double endedAt = -1;
        private Rectangle area;
        private float scale = 1;
        private string drawn;
        // Where they dragged it to, as the panel's bottom-left on screen, kept for later jobs. Null is the corner.
        private Point? corner;
        private bool grabbing;
        private Point grabbed;
        private Point grabbedAt;

        // It moved: it grew a row, or they dragged it.
        internal event Action Moved;

        // Space it leaves above itself on screen, so she fits sitting on it.
        internal int Room;

        internal TaskPanel() : base("Capri task")
        {
            Cursor = Cursors.SizeAll;
            ticks.Interval = 50;
            ticks.Tick += delegate { Step(); };
        }

        [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr window, uint affinity);
        [DllImport("user32.dll")] private static extern bool GetWindowDisplayAffinity(IntPtr window, out uint affinity);

        // Their recordings and screenshots show the panel. Hers don't: it leaves capture only for the moment she takes one.
        // True when it's on screen, so there's something to wait for.
        internal bool Unseen(bool unseen)
        {
            if (!IsHandleCreated) return false;
            try { SetWindowDisplayAffinity(Handle, unseen ? 0x11u : 0u); }
            catch (EntryPointNotFoundException) { return false; }
            return unseen && Visible;
        }

        internal uint Affinity { get { uint value; return IsHandleCreated && GetWindowDisplayAffinity(Handle, out value) ? value : 0; } }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left) Grab(PointToScreen(e.Location));
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); Haul(PointToScreen(e.Location)); }

        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); LetGo(); }

        protected override void OnMouseCaptureChanged(EventArgs e) { base.OnMouseCaptureChanged(e); if (!Capture) grabbing = false; }

        internal void Grab(Point at)
        {
            grabbing = true;
            grabbed = at;
            grabbedAt = Location;
            Capture = true;
        }

        internal void Haul(Point at)
        {
            if (grabbing) MoveTo(new Point(grabbedAt.X + at.X - grabbed.X, grabbedAt.Y + at.Y - grabbed.Y));
        }

        internal void LetGo()
        {
            grabbing = false;
            if (Capture) Capture = false;
        }

        // Moves the panel's top-left to about here, kept on screen, and leaves it there for later jobs.
        internal void MoveTo(Point topLeft)
        {
            Bitmap image = Image;
            if (image == null || !Visible) return;
            int glow = Glow;
            corner = new Point(topLeft.X + glow, topLeft.Y + image.Height - glow);
            Point at = Spot(image.Size);
            corner = new Point(at.X + glow, at.Y + image.Height - glow);
            if (Location == at) return;
            Location = at;
            if (Moved != null) Moved();
        }

        // Back to the bottom-right corner for the next job.
        internal void ToCorner() { corner = null; }

        // Where the next job's panel sits, by its bottom-left corner.
        internal void Corner(Point bottomLeft) { corner = bottomLeft; }

        // The panel itself on screen, without the glow around it.
        internal Rectangle Plate
        {
            get
            {
                int glow = Glow;
                return Visible ? new Rectangle(Left + glow, Top + glow, Width - glow * 2, Height - glow * 2) : Rectangle.Empty;
            }
        }

        private int Glow { get { return (int)Math.Round(12 * scale); } }

        // The bottom-right corner of their screen, or where they dragged it. It grows upward, and stays on screen with room above for her.
        private Point Spot(Size size)
        {
            int glow = Glow;
            Rectangle screen = corner.HasValue ? Screen.FromPoint(corner.Value).WorkingArea : area;
            Point at = corner.HasValue ? new Point(corner.Value.X - glow, corner.Value.Y + glow - size.Height) : new Point(area.Right - size.Width - (int)(8 * scale), area.Bottom - size.Height - (int)(8 * scale));
            return new Point(Math.Max(screen.Left - glow, Math.Min(at.X, screen.Right + glow - size.Width)), Math.Max(screen.Top - glow + Room, Math.Min(at.Y, screen.Bottom + glow - size.Height)));
        }

        internal bool Showing { get { return owner != null; } }

        internal string Outcome { get { return outcome; } }

        internal int Rows { get { return steps.Count; } }

        private double Now { get { return watch.Elapsed.TotalSeconds; } }

        internal void Begin(object job, string task, Rectangle screen)
        {
            if (job != owner) { steps.Clear(); words = task ?? ""; }
            owner = job;
            outcome = null;
            ending = "";
            endedAt = -1;
            busy = null;
            area = screen;
            using (Graphics graphics = Graphics.FromHwnd(IntPtr.Zero)) scale = Math.Max(1f, graphics.DpiX / 96f);
            ticks.Start();
            Step();
        }

        internal void Busy(object job, string what)
        {
            if (job != owner || outcome != null) return;
            busy = what;
            Step();
        }

        internal void Add(object job, JobStep step)
        {
            if (job != owner || step == null) return;
            if (steps.Count > 0 && !steps[steps.Count - 1].Finished) steps[steps.Count - 1] = step;
            else steps.Add(step);
            busy = null;
            Step();
        }

        internal void End(object job, string how, string said)
        {
            if (job != owner || owner == null) return;
            outcome = how ?? "failed";
            ending = System.Text.RegularExpressions.Regex.Replace(said ?? "", @"\s+", " ").Trim();
            busy = null;
            endedAt = Now;
            if (steps.Count > 0 && !steps[steps.Count - 1].Finished) steps.RemoveAt(steps.Count - 1);
            Step();
        }

        internal void Stop()
        {
            ticks.Stop();
            owner = null;
            steps.Clear();
            if (Visible) Hide();
        }

        private void Step()
        {
            if (owner == null) { ticks.Stop(); return; }
            double stay = outcome == "done" ? 4 : outcome == "waiting" || outcome == "asked" ? 8 : 6;
            double gone = outcome == null ? 0 : (Now - endedAt - stay) / 0.4;
            if (gone >= 1) { Stop(); return; }
            bool caret = outcome == null && (int)(Now * 2.5) % 2 == 0;
            double fade = 1 - Math.Max(0, gone);
            string shown = outcome + "|" + busy + "|" + steps.Count + "|" + (steps.Count > 0 ? steps[steps.Count - 1].Label + steps[steps.Count - 1].Finished : "") + "|" + caret + "|" + fade.ToString("0.00") + "|" + area + "|" + Room;
            if (shown == drawn && Visible) return;
            drawn = shown;
            Bitmap image = Frame(words, steps, busy, outcome, ending, caret, fade, scale);
            Point was = Location;
            Present(image, Spot(image.Size));
            if (Location != was && Moved != null) Moved();
        }

        internal static Bitmap Frame(string task, IList<JobStep> steps, string busy, string outcome, string ending, bool caret, double fade, float s)
        {
            string status = Speaker + (outcome == null ? "working on your task." : ending.Length > 0 ? ending : outcome == "done" ? "done." : "stopped.");
            System.Text.StringBuilder every = new System.Text.StringBuilder(task + status + busy);
            foreach (JobStep step in steps) every.Append(step.Label);
            using (Font text = Look.Mono(14 * s, FontStyle.Regular, GraphicsUnit.Pixel, every.ToString()))
            using (Font heard = Look.Mono(14 * s, FontStyle.Italic, GraphicsUnit.Pixel, every.ToString()))
            using (Font bold = Look.Mono(14 * s, FontStyle.Bold, GraphicsUnit.Pixel, every.ToString()))
            {
                float cw = Look.CharWidth(text), line = 19 * s, margin = 12 * s;
                List<string> you = Look.Wrap("> you: " + System.Text.RegularExpressions.Regex.Replace(task ?? "", @"\s+", " ").Trim(), Columns);
                if (you.Count > 2) { you.RemoveRange(2, you.Count - 2); you[1] = Look.Cut(you[1], Columns - 3) + "..."; }
                List<string> said = Look.Wrap(status, Columns);
                if (said.Count > 3) { said.RemoveRange(3, said.Count - 3); said[2] = Look.Cut(said[2], Columns - 3) + "..."; }
                int hidden = steps.Count > MostRows ? steps.Count - (MostRows - 1) : 0;
                List<string> rows = new List<string>();
                List<Color> inks = new List<Color>();
                if (hidden > 0) { rows.Add("  +" + hidden + " earlier steps"); inks.Add(Look.Dim); }
                for (int i = hidden > 0 ? steps.Count - (MostRows - 1) : 0; i < steps.Count; i++)
                {
                    rows.Add((steps[i].Finished ? "• " : "> ") + steps[i].Label + (steps[i].Finished ? "" : "..."));
                    inks.Add(steps[i].Failed ? Look.Red : steps[i].Finished ? Look.Dim : Look.Neon);
                }
                bool thinking = outcome == null && busy != null;
                if (thinking) { rows.Add("> " + busy); inks.Add(Look.Neon); }
                for (int i = 0; i < rows.Count; i++) if (Look.Cells(rows[i]) > Columns) rows[i] = Look.Cut(rows[i], Columns - 3) + "...";
                bool running = outcome == null;
                float panelWidth = 30 * s + Columns * cw;
                float panelHeight = 12 * s + line * (you.Count + said.Count + rows.Count) + (rows.Count > 0 ? 6 * s : 0) + (running ? line : 0) + 8 * s;
                Bitmap image = new Bitmap((int)Math.Ceiling(panelWidth + margin * 2), (int)Math.Ceiling(panelHeight + margin * 2), PixelFormat.Format32bppPArgb);
                int a = (int)(255 * Math.Max(0, Math.Min(1, fade)));
                if (a == 0) return image;
                RectangleF panel = new RectangleF(margin, margin, panelWidth, panelHeight);
                using (Graphics graphics = Graphics.FromImage(image))
                using (GraphicsPath path = Look.Plate(panel, 10 * s, -1, panel.Top + panel.Height / 2, 0, true))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Pen glow = new Pen(Color.FromArgb(46 * a / 255, Look.Cyan), 7 * s)) graphics.DrawPath(glow, path);
                    using (Brush fill = new SolidBrush(Color.FromArgb(238 * a / 255, Look.Panel))) graphics.FillPath(fill, path);
                    graphics.SetClip(path);
                    using (Pen scan = new Pen(Color.FromArgb(9 * a / 255, 255, 255, 255), Math.Max(1, s)))
                        for (float row = panel.Top; row < panel.Bottom; row += 3 * s) graphics.DrawLine(scan, panel.Left, row, panel.Right, row);
                    graphics.ResetClip();
                    using (Pen edge = new Pen(Color.FromArgb(a, Look.Cyan), 2 * s)) graphics.DrawPath(edge, path);
                    float x = panel.Left + 15 * s, y = panel.Top + 12 * s;
                    using (Brush dim = new SolidBrush(Color.FromArgb(a, Look.Dim)))
                        foreach (string row in you) { Look.Draw(graphics, row, heard, dim, x, y); y += line; }
                    using (Brush red = new SolidBrush(Color.FromArgb(a, Look.Red)))
                    using (Brush ink = new SolidBrush(Color.FromArgb(a, Look.Neon)))
                        for (int i = 0; i < said.Count; i++)
                        {
                            int name = i == 0 ? Math.Min(said[i].Length, Speaker.Length - 1) : 0;
                            if (name > 0) graphics.DrawString(said[i].Substring(0, name), bold, red, x, y, StringFormat.GenericTypographic);
                            Look.Draw(graphics, said[i].Substring(name), text, ink, x + name * cw, y);
                            y += line;
                        }
                    if (rows.Count > 0) y += 6 * s;
                    for (int i = 0; i < rows.Count; i++)
                    {
                        using (Brush ink = new SolidBrush(Color.FromArgb(a, inks[i]))) Look.Draw(graphics, rows[i], text, ink, x, y);
                        if (thinking && caret && i == rows.Count - 1)
                            using (Brush block = new SolidBrush(Color.FromArgb(a, Look.Neon))) graphics.FillRectangle(block, x + Look.Cells(rows[i]) * cw + 2 * s, y + s, cw * 0.85f, line - 5 * s);
                        y += line;
                    }
                    if (running)
                        using (Font small = Look.Mono(12 * s, FontStyle.Regular, GraphicsUnit.Pixel))
                        using (Brush dim = new SolidBrush(Color.FromArgb(a, Look.Dim)))
                            graphics.DrawString("ESC STOPS ME", small, dim, panel.Right - 15 * s - graphics.MeasureString("ESC STOPS ME", small, PointF.Empty, StringFormat.GenericTypographic).Width, y + 2 * s, StringFormat.GenericTypographic);
                }
                return image;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ticks.Dispose();
            base.Dispose(disposing);
        }
    }

    internal sealed class SpeechBox : Overlay
    {
        internal event Action Dismissed;
        internal TalkState State;
        internal string Heard;
        internal string Prompt = "> you: ";
        internal string Reply;
        internal int Revealed;
        internal double Opened = -1;
        internal double Leaving = -1;
        private readonly Random random = new Random();
        private Rectangle panelOnScreen;
        private bool clean;
        private bool lastCursor;
        private TalkState lastState;
        private string lastHeard;
        private string lastPrompt;
        private string lastReply;
        private int lastRevealed;
        private float lastScale;
        private Point lastHead;
        private Point lastFeet;
        private Rectangle lastArea;
        private int firstLine, visibleLines, totalLines;
        private string scrollReply;
        private RectangleF pages;
        private bool copied;
        internal Action<string> Copying = delegate(string reply) { Clipboard.SetText(reply); };
        private const int Columns = 34;
        private const string Speaker = "CAPRI: ";

        internal SpeechBox() : base("Capri says") { Cursor = Look.Pointer; }

        internal Rectangle PanelOnScreen { get { return panelOnScreen; } }
        internal bool Scrollable { get { return totalLines > visibleLines; } }
        internal int FirstLine { get { return firstLine; } }
        internal int VisibleLines { get { return visibleLines; } }
        internal int TotalLines { get { return totalLines; } }

        internal void ScrollRows(int rows)
        {
            firstLine = (int)Math.Max(0, Math.Min((long)Math.Max(0, totalLines - visibleLines), (long)firstLine + rows));
            clean = false;
            if (Visible) Update(Math.Max(Opened + 1, 0), lastScale, lastHead, lastFeet, lastArea);
        }

        // Right click copies all she said, even the pages that aren't showing.
        internal void CopyReply()
        {
            if (string.IsNullOrEmpty(Reply)) return;
            try { Copying(Reply); }
            catch (System.Runtime.InteropServices.ExternalException) { return; }
            copied = true;
            clean = false;
            if (Visible) Update(Math.Max(Opened + 1, 0), lastScale, lastHead, lastFeet, lastArea);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (e.Delta != 0) ScrollRows(e.Delta > 0 ? -3 : 3);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) { CopyReply(); return; }
            if (e.Button != MouseButtons.Left) return;
            if (Scrollable && pages.Contains(e.Location)) { ScrollRows(e.X < pages.Left + pages.Width / 2 ? -visibleLines : visibleLines); return; }
            if (Dismissed != null) Dismissed();
        }

        internal static int Fit(string text)
        {
            int length = Look.Cells(text);
            return length > 420 ? 56 : length > 220 ? 44 : Columns;
        }

        internal static float PanelWidth(float s)
        {
            using (Font font = Look.Mono(14 * s, FontStyle.Regular)) return 24 * s + Columns * Look.CharWidth(font) + 14 * s;
        }

        internal void Update(double now, float s, Point head, Point feet, Rectangle area)
        {
            float glitch = 0;
            if (Opened >= 0 && now - Opened < 0.2) glitch = (float)(1 - (now - Opened) / 0.2);
            if (Leaving >= 0) glitch = (float)Math.Min(1, (now - Leaving) / 0.25 + 0.3);
            bool cursor = State == TalkState.Thinking || State == TalkState.Talking ? (State == TalkState.Talking || (int)(now * 2.5) % 2 == 0) : (int)(now * 2) % 2 == 0;
            int revealed = State == TalkState.Thinking || Reply == null ? 0 : Math.Min(Reply.Length, Math.Max(0, Revealed));
            if (glitch == 0 && clean && Visible && Image != null && cursor == lastCursor && State == lastState && Heard == lastHeard && Prompt == lastPrompt && Reply == lastReply && revealed == lastRevealed && s == lastScale && head == lastHead && feet == lastFeet && area == lastArea) return;
            Point at;
            Bitmap image = Render(s, head, feet, area, cursor, out at);
            if (glitch > 0)
            {
                Bitmap broken = Glitch(image, glitch, s);
                image.Dispose();
                image = broken;
            }
            Present(image, at);
            clean = glitch == 0;
            lastCursor = cursor;
            lastState = State;
            lastHeard = Heard;
            lastPrompt = Prompt;
            lastReply = Reply;
            lastRevealed = revealed;
            lastScale = s;
            lastHead = head;
            lastFeet = feet;
            lastArea = area;
        }

        internal Bitmap Render(float s, Point head, Point feet, Rectangle area, bool cursorOn, out Point at)
        {
            if (scrollReply != Reply) { firstLine = 0; scrollReply = Reply; copied = false; }
            s = Math.Min(s, Math.Min(area.Width / 160f, area.Height / 150f));
            string all = Speaker + (Reply ?? "") + (Heard ?? "");
            using (Font text = Look.Mono(14 * s, FontStyle.Regular, GraphicsUnit.Pixel, all))
            using (Font heard = Look.Mono(14 * s, FontStyle.Italic, GraphicsUnit.Pixel, all))
            using (Font bold = Look.Mono(14 * s, FontStyle.Bold, GraphicsUnit.Pixel, all))
            {
                float cw = Look.CharWidth(text);
                float line = 19 * s;
                float margin = 12 * s;
                float top = 10 * s;
                float tail = 16 * s;
                int columns = Math.Max(1, Math.Min(Fit(Speaker + (Reply ?? "")), (int)Math.Floor((area.Width - 62 * s) / cw)));
                List<string> lines = Look.Wrap(Speaker + (Reply ?? ""), columns);
                // Her own remarks have no prompt, so nothing sits above her words.
                int above = string.IsNullOrEmpty(Prompt) && !copied ? 0 : 1;
                float panelWidth = 24 * s + columns * cw + 14 * s;
                totalLines = lines.Count;
                int room = Math.Max(1, (int)Math.Floor((area.Height - 53 * s) / line) - above);
                visibleLines = Math.Min(totalLines, totalLines > room ? Math.Max(1, room - 1) : room);
                firstLine = Math.Max(0, Math.Min(firstLine, totalLines - visibleLines));
                float panelHeight = 13 * s + line * (above + visibleLines + (Scrollable ? 1 : 0)) + 10 * s;
                int width = (int)Math.Ceiling(panelWidth + margin * 2);
                int height = (int)Math.Ceiling(top + panelHeight + tail + 4 * s);
                bool below = head.Y - height < area.Top;
                Point tip = below ? feet : head;
                int left = Math.Max(area.Left, Math.Min(area.Right - width, (int)(tip.X - margin - panelWidth * 0.7f)));
                int y = Math.Max(area.Top, Math.Min(area.Bottom - height, below ? tip.Y : tip.Y - height));
                at = new Point(left, y);
                RectangleF panel = new RectangleF(margin, below ? tail + 4 * s : top, panelWidth, panelHeight);
                panelOnScreen = new Rectangle(left + (int)panel.X, y + (int)panel.Y, (int)panel.Width, (int)panel.Height);
                pages = Scrollable ? new RectangleF(panel.Left + 24 * s, panel.Bottom - 10 * s - line, panel.Width - 38 * s, line) : RectangleF.Empty;
                float tipX = Math.Max(panel.Left + 32 * s, Math.Min(panel.Right - 10 * s, tip.X - left));
                float tipY = below ? 0 : height - 1;
                Bitmap image = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
                using (Graphics graphics = Graphics.FromImage(image))
                using (GraphicsPath path = Look.Plate(panel, 10 * s, tipX, tipY, 18 * s))
                {
                    graphics.SmoothingMode = SmoothingMode.AntiAlias;
                    graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    using (Pen glow = new Pen(Color.FromArgb(46, Look.Cyan), 7 * s)) graphics.DrawPath(glow, path);
                    using (Brush fill = new SolidBrush(Color.FromArgb(238, Look.Panel))) graphics.FillPath(fill, path);
                    graphics.SetClip(path);
                    using (Pen scan = new Pen(Color.FromArgb(9, 255, 255, 255), Math.Max(1, s)))
                        for (float row = panel.Top; row < panel.Bottom; row += 3 * s) graphics.DrawLine(scan, panel.Left, row, panel.Right, row);
                    graphics.ResetClip();
                    using (Pen edge = new Pen(Look.Cyan, 2 * s)) graphics.DrawPath(edge, path);
                    using (Brush red = new SolidBrush(Look.Red)) graphics.FillRectangle(red, panel.Left + 10 * s, panel.Top + 13 * s, 3 * s, panel.Height - 26 * s);
                    float x = panel.Left + 24 * s;
                    float row2 = panel.Top + 12 * s;
                    if (above > 0)
                    {
                        string you = copied ? "> sys: copied" : Prompt + (Heard ?? "");
                        if (Look.Cells(you) > columns) you = Look.Cut(you, columns - 3) + "...";
                        using (Brush dim = new SolidBrush(Look.Dim)) Look.Draw(graphics, you, heard, dim, x, row2);
                        row2 += line;
                    }
                    int[] takes = Look.Takes(lines, Speaker + (Reply ?? ""), Speaker.Length + (State == TalkState.Thinking ? (Reply ?? "").Length : Math.Max(0, Revealed)));
                    PointF caret = new PointF(x, row2);
                    using (Brush red = new SolidBrush(Look.Red))
                    using (Brush ink = new SolidBrush(Look.Neon))
                    {
                        float right = x + columns * cw;
                        for (int i = firstLine; i < firstLine + visibleLines && takes[i] > 0; i++)
                        {
                            string shown = lines[i].Substring(0, takes[i]);
                            int name = i == 0 ? Math.Min(takes[i], Speaker.Length - 1) : 0;
                            if (name > 0) graphics.DrawString(shown.Substring(0, name), bold, red, x, row2, StringFormat.GenericTypographic);
                            string words = shown.Substring(name);
                            float from = x + name * cw;
                            // An Arabic, Urdu, Persian or Hebrew line grows leftwards from the right edge as she types it. The whole
                            // line decides, so it doesn't flip sides partway through.
                            if (Look.RightToLeft(lines[i].Substring(name)))
                            {
                                graphics.DrawString(words, text, ink, new RectangleF(from, row2 + (i - firstLine) * line, right - from, line), Look.Leftward);
                                caret = new PointF(right - graphics.MeasureString(words, text, PointF.Empty, Look.Leftward).Width - cw - 2 * s, row2 + (i - firstLine) * line);
                            }
                            else
                            {
                                float end = Look.Draw(graphics, words, text, ink, from, row2 + (i - firstLine) * line);
                                caret = new PointF(Look.Burmese(words) ? end : Look.Thai(words) || Look.Indic(words) ? from + graphics.MeasureString(words, text, PointF.Empty, Look.Spaced).Width : x + Look.Cells(shown) * cw, row2 + (i - firstLine) * line);
                            }
                        }
                        if (cursorOn && (firstLine + visibleLines == totalLines || takes[firstLine + visibleLines] == 0)) graphics.FillRectangle(ink, caret.X + s, caret.Y + s, cw * 0.85f, line - 5 * s);
                    }
                    if (Scrollable)
                    {
                        string paging = "< PREV   " + (firstLine + 1) + "-" + (firstLine + visibleLines) + "/" + totalLines + "   NEXT >";
                        using (Brush dim = new SolidBrush(Look.Dim)) Look.Draw(graphics, paging, text, dim, pages.Left, pages.Top);
                    }
                }
                return image;
            }
        }

        private Bitmap Glitch(Bitmap clean, float amount, float s)
        {
            Bitmap broken = new Bitmap(clean.Width, clean.Height, PixelFormat.Format32bppPArgb);
            if (random.NextDouble() < 0.25 * amount) return broken;
            using (Graphics graphics = Graphics.FromImage(broken))
            {
                int y = 0;
                while (y < clean.Height)
                {
                    int band = Math.Min(clean.Height - y, (int)((3 + random.Next(10)) * s));
                    int shift = (int)((random.NextDouble() * 2 - 1) * 10 * s * amount);
                    graphics.DrawImage(clean, new Rectangle(shift, y, clean.Width, band), new Rectangle(0, y, clean.Width, band), GraphicsUnit.Pixel);
                    y += band;
                }
                for (int i = 0; i < 2; i++)
                {
                    int band = (int)((2 + random.Next(6)) * s);
                    int row = random.Next(Math.Max(1, clean.Height - band));
                    int shift = (int)((random.NextDouble() * 2 - 1) * 14 * s * amount);
                    bool cyan = i == 0;
                    using (ImageAttributes tint = new ImageAttributes())
                    {
                        tint.SetColorMatrix(new ColorMatrix(new[]
                        {
                            new float[] { cyan ? 0 : 1, 0, 0, 0, 0 },
                            new float[] { 0, cyan ? 1 : 0, 0, 0, 0 },
                            new float[] { 0, 0, cyan ? 1 : 0, 0, 0 },
                            new float[] { 0, 0, 0, 0.6f * amount, 0 },
                            new float[] { 0, 0, 0, 0, 1 }
                        }));
                        graphics.DrawImage(clean, new Rectangle(shift, row, clean.Width, band), 0, row, clean.Width, band, GraphicsUnit.Pixel, tint);
                    }
                }
            }
            return broken;
        }
    }

    // Her type box, in her Mainframe's look: a dark well cut at the corners with a red rim and hot red strokes over the two
    // big corners, a red tick, YOU in rose and what they type in white. It opens where her speech box would.
    internal sealed class TypeBox : Form
    {
        internal event Action<string> Submitted;
        internal event Action CallModeChanged;
        private readonly TextBox input;
        private readonly Label prompt;
        private readonly Button callMode;
        private readonly Label callNote;
        private bool calling;
        private float scale = 1;
        private Font typed;
        private Font label;

        internal TypeBox()
        {
            Text = "Talk to Capri";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Neon.Well;
            DoubleBuffered = true;
            prompt = new Label();
            prompt.Text = "YOU";
            prompt.ForeColor = Neon.Rose;
            prompt.BackColor = Neon.Well;
            prompt.AutoSize = true;
            prompt.UseMnemonic = false;
            input = new TextBox();
            input.BorderStyle = BorderStyle.None;
            input.BackColor = Neon.Well;
            input.ForeColor = Neon.White;
            // No limit, so her brain reads a long request whole.
            input.MaxLength = 0;
            input.AccessibleName = Text;
            input.HandleCreated += delegate { Native.SendMessage(input.Handle, 0x1501, new IntPtr(1), "Say something to Capri"); };
            input.KeyDown += delegate(object sender, KeyEventArgs key)
            {
                if (key.KeyCode == Keys.Enter) { key.SuppressKeyPress = true; Submit(); }
                else if (key.KeyCode == Keys.Escape) { key.SuppressKeyPress = true; Hide(); }
            };
            Controls.Add(prompt);
            Controls.Add(input);
            callMode = new Button { FlatStyle = FlatStyle.Flat, BackColor = Neon.Well, ForeColor = Neon.Cyan, Text = "Call mode (beta): off", AccessibleName = "Call mode beta. Voice is conversation only when enabled", TabIndex = 1 };
            callMode.FlatAppearance.BorderColor = Neon.Edge;
            callMode.Click += delegate { if (CallModeChanged != null) CallModeChanged(); };
            callNote = new Label { Text = CallSession.Reminder, ForeColor = Neon.White, BackColor = Neon.Well, UseMnemonic = false, Visible = false };
            Controls.Add(callMode);
            Controls.Add(callNote);
            Deactivate += delegate { Hide(); };
        }

        internal string Draft { get { return input.Text; } set { input.Text = value; } }

        internal bool CallMode
        {
            get { return calling; }
            set
            {
                calling = value;
                callMode.Text = value ? "Call mode (beta): on" : "Call mode (beta): off";
                callNote.Visible = value;
                if (typed != null)
                {
                    Arrange();
                    Rectangle area = Screen.FromControl(this).WorkingArea;
                    Top = Math.Max(area.Top, Math.Min(Top, area.Bottom - Height));
                }
            }
        }

        private void Arrange()
        {
            float s = scale;
            Height = (int)((calling ? 122 : 76) * s);
            prompt.Location = new Point((int)(24 * s), ((int)(44 * s) - prompt.PreferredHeight) / 2);
            input.Location = new Point(prompt.Right + (int)(10 * s), ((int)(44 * s) - input.PreferredHeight) / 2 + (int)s);
            input.Width = Width - input.Left - (int)(16 * s);
            callMode.SetBounds((int)(24 * s), (int)(44 * s), Width - (int)(48 * s), (int)(26 * s));
            callNote.SetBounds((int)(24 * s), (int)(76 * s), Width - (int)(48 * s), (int)(38 * s));
            callMode.Font = label;
            callNote.Font = label;
            Neon.Clip(this, 10 * s, 4 * s);
            Invalidate();
        }

        internal void Open(Point head, Point feet, Rectangle area, float s, Rectangle above)
        {
            if (typed == null || scale != s)
            {
                scale = s;
                Font oldTyped = typed;
                Font oldLabel = label;
                typed = Neon.Type(15 * s, false, GraphicsUnit.Pixel);
                label = Neon.Type(12.5f * s, true, GraphicsUnit.Pixel);
                input.Font = typed;
                prompt.Font = label;
                if (oldTyped != null) { oldTyped.Dispose(); oldLabel.Dispose(); }
            }
            int width = (int)SpeechBox.PanelWidth(s);
            int height = (int)((calling ? 122 : 76) * s);
            bool below = head.Y - height - 20 * s < area.Top;
            int left = Math.Max(area.Left, Math.Min(area.Right - width, (int)(head.X - width * 0.7f)));
            int top = below ? feet.Y + (int)(8 * s) : head.Y - height - (int)(18 * s);
            Bounds = new Rectangle(left, Math.Max(area.Top, Math.Min(top, area.Bottom - height)), width, height);
            Arrange();
            if (!above.IsEmpty) Follow(above, area);
            Show();
            Activate();
            input.Focus();
            input.SelectionStart = input.Text.Length;
        }

        internal void Follow(Rectangle above, Rectangle area)
        {
            int top = above.Top - Height - (int)(16 * scale);
            if (top < area.Top) top = above.Bottom + (int)(8 * scale);
            top = Math.Max(area.Top, Math.Min(top, area.Bottom - Height));
            Point target = new Point(above.Left, top);
            if (Location != target) Location = target;
        }

        private void Submit()
        {
            string text = input.Text.Trim();
            if (text.Length == 0) return;
            input.Text = "";
            if (Submitted != null) Submitted(text);
        }

        internal void Send(string text) { input.Text = text; Submit(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            float s = scale;
            Neon.Frame(e.Graphics, Size, s, 10 * s, 4 * s, 36 * s, 10 * s);
            using (Brush tick = new SolidBrush(Neon.Hot)) e.Graphics.FillRectangle(tick, 12 * s, 12 * s, 2.5f * s, Height - 24 * s);
        }
    }
}
