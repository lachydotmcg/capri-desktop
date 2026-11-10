using System;
using System.Globalization;
using System.Linq;

namespace CapriDesktop
{
    // Called by the existing native smoke suite; uses the real pinned font/layout implementation.
    internal static class ReplyLayoutChecks
    {
        internal static int Run()
        {
            int checks = 0;
            foreach (string text in new[] {
                "The settings panel is getting a very thorough inspection.", "Hi!", "one\r\ntwo\n\nthree\n", "cwd\t/home/test/Public QA\nexit: 0",
                new string('a', 180), "Cafe\u0301 👩🏽‍💻 🇨🇦", "مرحبا بالعالم שלום עולם", "你好，朋友！今日は元気ですか？", "नमस्ते दुनिया", "เฮ้ เพื่อน วันนี้เหนื่อยไหม",
                "first paragraph with a few words and a second sentence that wraps.\n\nSecond paragraph." })
            {
                var layout = ReplyLayout.For(Sheet.Speaker + text);
                double width = Sheet.Columns * Look.CharWidth(Sheet.Type);
                foreach (var row in layout.Rows)
                {
                    if (row.Text.Contains('\n') || row.Text.Contains('\r') || row.Text.Contains('\t') || row.Width > width + .01) throw new Exception("Measured reply row exceeds its bounds.");
                    if (Look.Text(row.Text, Look.Regular, Sheet.Type, Look.Neon).Height > layout.LineHeight) throw new Exception("Reply line height clips its font metrics.");
                    foreach (var glyph in row.Glyphs)
                        if (row.Visible(glyph.SourceEnd - 1).EndsWith(glyph.Text) && row.Glyphs.Count == 1) throw new Exception("Reply revealed a partial grapheme.");
                    for (int at = 0; at <= (Sheet.Speaker + text).Length; at++)
                    {
                        string actual = row.Visible(at);
                        string expected = string.Concat(row.Glyphs.Where(g => g.SourceEnd <= at).Select(g => g.Text));
                        if (actual != expected) throw new Exception("Reply reveal/source offsets disagree.");
                    }
                    checks++;
                }
                if (Sheet.PanelHeight(text) < 12 + layout.LineHeight + layout.Height) throw new Exception("Reply panel clips a measured baseline.");
                checks++;
            }
            var explicitLines = ReplyLayout.For("a\r\nb\n\nc\n");
            if (string.Join("|", explicitLines.Rows.Select(r => r.Text)) != "a|b||c|") throw new Exception("Explicit blank/newline boundaries were lost.");
            return checks + 1;
        }
    }
}
