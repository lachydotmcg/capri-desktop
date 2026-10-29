using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace CapriDesktop
{
    internal static class Look
    {
        internal static readonly Color Panel = Color.FromRgb(9, 11, 15);
        internal static readonly Color Cyan = Color.FromRgb(58, 214, 235);
        internal static readonly Color Red = Color.FromRgb(232, 36, 64);
        internal static readonly Color Neon = Color.FromRgb(56, 176, 255);
        internal static readonly Color Ink = Color.FromRgb(232, 236, 240);
        internal static readonly Color Dim = Color.FromRgb(122, 128, 138);
        internal static readonly Color Edge = Color.FromRgb(70, 73, 82);
        internal static readonly Color Pill = Color.FromArgb(244, 20, 21, 25);
        internal static readonly FontFamily Mono = new FontFamily("avares://Capri/Assets#Share Tech Mono");
        internal static readonly Typeface Regular = new Typeface(Mono);
        internal static readonly Typeface Italic = new Typeface(Mono, FontStyle.Italic);
        internal static readonly Typeface Bold = new Typeface(Mono, FontStyle.Normal, FontWeight.Bold);

        internal static FormattedText Text(string text, Typeface face, double size, Color colour)
        {
            return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, new SolidColorBrush(colour));
        }

        internal static double CharWidth(double size)
        {
            return Text(new string('M', 20), Regular, size, Ink).WidthIncludingTrailingWhitespace / 20;
        }

        internal static Geometry Plate(Rect box, double cut, double tipX, double tipY, double tail, bool both = false)
        {
            double right = both ? cut : 0;
            List<Point> points = new List<Point>();
            double near = Math.Max(box.Left + cut + 2, tipX - tail * 0.8);
            double far = Math.Min(box.Right - right - 2, tipX + tail * 0.2);
            points.Add(new Point(box.Left + cut, box.Top));
            if (tipY < box.Top) { points.Add(new Point(near, box.Top)); points.Add(new Point(tipX, tipY)); points.Add(new Point(far, box.Top)); }
            points.Add(new Point(box.Right - right, box.Top));
            if (both) points.Add(new Point(box.Right, box.Top + cut));
            if (both) points.Add(new Point(box.Right, box.Bottom - cut));
            points.Add(new Point(box.Right - right, box.Bottom));
            if (tipY > box.Bottom) { points.Add(new Point(far, box.Bottom)); points.Add(new Point(tipX, tipY)); points.Add(new Point(near, box.Bottom)); }
            points.Add(new Point(box.Left + cut, box.Bottom));
            points.Add(new Point(box.Left, box.Bottom - cut));
            points.Add(new Point(box.Left, box.Top + cut));
            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                context.BeginFigure(points[0], true);
                for (int i = 1; i < points.Count; i++) context.LineTo(points[i]);
                context.EndFigure(true);
            }
            return geometry;
        }

        // How many columns a character takes in her mono bubbles: Chinese, Japanese and Korean characters and full-width forms
        // take two, Thai vowel and tone marks (they sit above or below a letter) none, everything else one. The same as on Windows.
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

        private static bool Myanmar(char c) { return (c >= 0x1000 && c <= 0x109F) || (c >= 0xA9E0 && c <= 0xA9FF) || (c >= 0xAA60 && c <= 0xAA7F); }

        // Indian scripts, Lao, Khmer and Burmese join letters and marks into one shape, and Tamil, Kannada, Malayalam, Ethiopic,
        // Georgian and Armenian draw wider than her columns, so their words are measured in her bubble's font instead of counted,
        // the same as on Windows. Burmese measures in whichever font the system draws it with.
        private static readonly Dictionary<string, double> measured = new Dictionary<string, double>();

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
                        // A shade over, like Windows; a font that can't measure falls back to a column a letter.
                        try { width = Text(word, Regular, 14, Ink).WidthIncludingTrailingWhitespace / CharWidth(14) * 1.03; }
                        catch (Exception) { width = word.Length; }
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

        // Thai, Lao, Khmer and Burmese write no spaces between words, and Mac and Linux have no word breaker she can call, so
        // their lines break between letters. A Thai or Lao line never breaks before a vowel or tone mark, or a vowel that
        // follows its letter, and never after a vowel written before its letter. Every script keeps each letter with its marks,
        // and with the next letter after a sign that joins them (a virama, or Khmer's coeng), and a Burmese line never breaks
        // before ါ, ာ or း (.NET counts them as letters of their own) or a letter that ends the syllable before it. Windows
        // breaks between words.
        internal static bool[] Letters(string word)
        {
            bool[] breaks = new bool[word.Length];
            if (!Thai(word))
            {
                foreach (int start in StringInfo.ParseCombiningCharacters(word))
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
            bool[] breaks = Letters(word);
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
        // them fills the rest of a line and carries on below, the way they're written. Thai, Lao, Khmer and Burmese do the
        // same. Other words only break when they're longer than a whole line.
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

        internal static void Overlay(Window window, string title)
        {
            window.Title = title;
            window.SystemDecorations = SystemDecorations.None;
            window.Background = Brushes.Transparent;
            window.TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            window.Topmost = true;
            window.ShowInTaskbar = false;
            window.CanResize = false;
            window.ShowActivated = false;
            window.WindowStartupLocation = WindowStartupLocation.Manual;
        }
    }
}
