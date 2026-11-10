using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CapriDesktop
{
    // One measured representation owns wrapping, source offsets, reveal and vertical bounds.
    // SourceEnd stays in original UTF-16 coordinates even for CRLF, tabs and grapheme clusters.
    internal sealed class ReplyLayout
    {
        internal sealed class Glyph
        {
            internal string Text;
            internal int SourceEnd;
        }
        internal sealed class Row
        {
            internal readonly List<Glyph> Glyphs;
            internal readonly string Text;
            internal readonly double Width;
            internal Row(IEnumerable<Glyph> glyphs)
            {
                Glyphs = glyphs.ToList(); Text = string.Concat(Glyphs.Select(g => g.Text));
                Width = Measure(Text);
            }
            internal string Visible(int sourceEnd) => string.Concat(Glyphs.TakeWhile(g => g.SourceEnd <= sourceEnd).Select(g => g.Text));
        }
        internal readonly List<Row> Rows = new List<Row>();
        internal double LineHeight { get; private set; }
        internal double Height => LineHeight * Rows.Count;
        internal static double Measure(string text) => Look.Text(text, Look.Regular, Sheet.Type, Look.Neon).WidthIncludingTrailingWhitespace;
        private static readonly Dictionary<string, ReplyLayout> cache = new Dictionary<string, ReplyLayout>();
        internal static string Revealed(string text, int count)
        {
            if (text == null) return "";
            if (count >= text.Length) return text;
            int boundary = StringInfo.ParseCombiningCharacters(text).Where(index => index <= Math.Max(0, count)).DefaultIfEmpty(0).Last();
            return text.Substring(0, boundary);
        }
        internal static ReplyLayout For(string text)
        {
            text ??= "";
            if (cache.TryGetValue(text, out var found)) return found;
            var layout = new ReplyLayout(text, Sheet.Columns * Look.CharWidth(Sheet.Type));
            if (cache.Count >= 8) cache.Clear();
            cache[text] = layout; return layout;
        }
        internal ReplyLayout(string text, double width)
        {
            if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            LineHeight = Math.Max(Sheet.Line, Math.Ceiling(Look.Text("Mg", Look.Regular, Sheet.Type, Look.Neon).Height) + 3);
            var line = new List<Glyph>();
            var elements = StringInfo.GetTextElementEnumerator(text ?? "");
            while (elements.MoveNext())
            {
                string glyph = elements.GetTextElement();
                int end = elements.ElementIndex + glyph.Length;
                if (glyph == "\r\n" || glyph == "\n" || glyph == "\r") { Add(line); line.Clear(); continue; }
                if (glyph == "\t") glyph = new string(' ', 4 - line.Count % 4);
                else if (glyph.Length == 1 && char.IsControl(glyph[0])) continue;
                line.Add(new Glyph { Text = glyph, SourceEnd = end });
                while (line.Count > 1 && Measure(string.Concat(line.Select(g => g.Text))) > width)
                {
                    int cut = line.FindLastIndex(line.Count - 2, g => g.Text == " ");
                    if (cut > 0)
                    {
                        Add(line.Take(cut)); line.RemoveRange(0, cut + 1);
                    }
                    else
                    {
                        int count = line.Count - 1;
                        Add(line.Take(count)); line.RemoveRange(0, count);
                    }
                }
            }
            Add(line);
        }
        private void Add(IEnumerable<Glyph> glyphs)
        {
            var row = new Row(glyphs); Rows.Add(row);
            LineHeight = Math.Max(LineHeight, Math.Ceiling(Look.Text(row.Text.Length == 0 ? "Mg" : row.Text, Look.Regular, Sheet.Type, Look.Neon).Height) + 3);
        }
    }
}
