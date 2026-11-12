using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace CapriDesktop
{
    // What one block of Claude's words is.
    internal enum WordKind { Para, Heading, Bullet, Number, Task, Quote, Rule, Code, Table }

    // How a table column lines up, from the colons in its dashed row. None is a column with no colons.
    internal enum WordAlign { None, Left, Center, Right }

    // A stretch of text in one style. Link is the address when the stretch is a link, and null when it isn't.
    internal sealed class WordRun
    {
        internal string Text;
        internal bool Bold, Italic, Strike, Code;
        internal string Link;
    }

    // One block of a reply. Line breaks inside a paragraph, a list item or a quote stay as "\n" in the run text, the way
    // Claude typed them. Runs is never null: a code block is one run of its text as written, a rule and a table have none.
    internal sealed class WordBlock
    {
        internal WordKind Kind;
        internal int Level;          // Heading: 1 to 3, with #### and deeper read as 3. Quote: how deep, from 1.
        internal int Depth;          // How many list items hold it. An item's text lines up with the blocks inside it, at Depth + 1.
        internal string Marker;      // Items: a dot, the number as it reads ("3." or "3)"), or an empty or ticked box for a task.
        internal bool Checked;       // Task: ticked.
        internal string Lang;        // Code: the first word after the opening fence, or "".
        internal List<WordRun> Runs = new List<WordRun>();
        internal List<List<List<WordRun>>> Rows;   // Table: the header row first, and every row as wide as Align.
        internal List<WordAlign> Align;
        internal int From, Length;   // Where it came from in the text, in chars, without the last line's ending.
    }

    // Claude's Markdown, read into blocks of styled runs for the Workspace transcript, and the plain text for copying them.
    // It is CommonMark with GitHub's tables, task boxes, ~~strike~~ and bare addresses, bent where Claude's replies need it:
    // Windows paths keep their backslashes, globs and file names keep their stars and underscores, a numbered list counts
    // on the way a browser shows it, and any mark with no partner stays as typed.
    internal static class WorkWords
    {
        internal const string Dot = "\u2022", Box = "\u2610", Ticked = "\u2611";

        // Read runs again on every streamed chunk on the UI thread, so it looks at each character a fixed number of times
        // however the marks fall. It never throws; if a bug ever got that far the text still shows, unstyled.
        internal static List<WordBlock> Read(string text)
        {
            if (string.IsNullOrEmpty(text)) return new List<WordBlock>();
            try { return new BlockReader(text).Read(); }
            catch (Exception)
            {
                WordBlock plain = new WordBlock { Kind = WordKind.Para, Length = text.Length };
                plain.Runs.Add(new WordRun { Text = text.Replace("\r\n", "\n").Replace('\r', '\n') });
                return new List<WordBlock> { plain };
            }
        }

        // An open list item: the column of its marker, the column a line has to reach to belong to it, and its number.
        private sealed class Item
        {
            internal int Inside, Number;
            internal char Sign;
        }

        private enum Leaf { None, Para, Item, Quote, Table, Fence }

        // Reads lines into blocks. One leaf block at a time is open and takes lines; list items stay open around it.
        private sealed class BlockReader
        {
            private readonly string s;
            private readonly List<WordBlock> blocks = new List<WordBlock>();
            private readonly List<Item> items = new List<Item>();
            private readonly StringBuilder words = new StringBuilder();
            private Leaf leaf;
            private WordBlock open;
            private int lastAt, lastLine, lastFrom, lastEnd, beforeLast, codeLines;
            private char fence;
            private int fenceSize, fenceCol, fenceRoom;
            private List<List<string>> rows;
            private readonly InlineReader inline = new InlineReader();

            internal BlockReader(string text) { s = text; }

            private List<WordRun> Inline(string text) { return inline.Read(text); }

            internal List<WordBlock> Read()
            {
                int at = 0, n = s.Length;
                while (at < n)
                {
                    int from = at;
                    while (at < n && s[at] != '\n' && s[at] != '\r') at++;
                    int end = at;
                    if (at < n)
                    {
                        if (s[at] == '\r' && at + 1 < n && s[at + 1] == '\n') at++;
                        at++;
                    }
                    Line(from, end);
                }
                Close();
                return blocks;
            }

            private void Line(int from, int end)
            {
                int col;
                int pos = Indent(from, end, out col);
                if (leaf == Leaf.Fence)
                {
                    if (pos < end && col <= fenceRoom && Closes(pos, end)) { open.Length = end - open.From; Close(); return; }
                    if (codeLines++ > 0) words.Append('\n');
                    Strip(from, end, fenceCol);
                    open.Length = end - open.From;
                    return;
                }
                if (pos == end) { Close(); return; }

                int k = 0;
                while (k < items.Count && col >= items[k].Inside) k++;
                bool shallow = col - (k > 0 ? items[k - 1].Inside : 0) <= 3;

                // A dashed row under a line with pipes makes that line a table's header, even the last line of a paragraph.
                List<WordAlign> align;
                if ((leaf == Leaf.Para || leaf == Leaf.Item) && Dashes(pos, end, out align) && Pipe(lastFrom, lastEnd) && Cells(lastFrom, lastEnd).Count == align.Count)
                {
                    List<string> header = Cells(lastFrom, lastEnd);
                    int depth = leaf == Leaf.Item ? open.Depth + 1 : open.Depth, start = lastLine;
                    if (lastAt == 0 && leaf == Leaf.Para) { leaf = Leaf.None; open = null; words.Length = 0; }
                    else
                    {
                        words.Length = Math.Max(0, lastAt - 1);
                        if (beforeLast >= 0) open.Length = beforeLast - open.From;
                        Close();
                    }
                    open = new WordBlock { Kind = WordKind.Table, Depth = depth, Align = align, From = start, Length = end - start };
                    rows = new List<List<string>> { header };
                    leaf = Leaf.Table;
                    return;
                }
                if (leaf == Leaf.Table && Pipe(pos, end) && !(shallow && (Opens(pos, end) || Heading(pos, end) > 0 || Rule(pos, end))))
                {
                    rows.Add(Cells(pos, end));
                    open.Length = end - open.From;
                    return;
                }
                if (shallow && Opens(pos, end))
                {
                    Begin(k);
                    int info = pos;
                    while (info < end && s[info] == s[pos]) info++;
                    fence = s[pos];
                    fenceSize = info - pos;
                    fenceCol = col;
                    fenceRoom = (k > 0 ? items[k - 1].Inside : 0) + 3;
                    codeLines = 0;
                    while (info < end && (s[info] == ' ' || s[info] == '\t')) info++;
                    int word = info;
                    while (word < end && s[word] != ' ' && s[word] != '\t') word++;
                    open = new WordBlock { Kind = WordKind.Code, Depth = k, Lang = s.Substring(info, word - info), From = from, Length = end - from };
                    leaf = Leaf.Fence;
                    words.Length = 0;
                    return;
                }
                int hashes = shallow ? Heading(pos, end) : -1;
                if (hashes > 0)
                {
                    Begin(k);
                    int a = pos + hashes, b = Trimmed(pos + hashes, end, ref a);
                    int tail = b;
                    while (tail > a && s[tail - 1] == '#') tail--;
                    if (tail == a || s[tail - 1] == ' ' || s[tail - 1] == '\t') b = Trimmed(a, tail, ref a);
                    WordBlock head = new WordBlock { Kind = WordKind.Heading, Level = Math.Min(hashes, 3), Depth = k, From = from, Length = end - from };
                    head.Runs = Inline(s.Substring(a, b - a));
                    blocks.Add(head);
                    return;
                }
                if (shallow && Rule(pos, end))
                {
                    Begin(k);
                    blocks.Add(new WordBlock { Kind = WordKind.Rule, Depth = k, From = from, Length = end - from });
                    return;
                }
                if ((shallow || items.Count > 0) && StartItem(from, end, pos, col, k)) return;
                if (shallow && s[pos] == '>')
                {
                    int level = 0, a = pos;
                    while (a < end && s[a] == '>')
                    {
                        level++;
                        a++;
                        int space = a;
                        while (space < end && (s[space] == ' ' || s[space] == '\t')) space++;
                        if (space < end && s[space] == '>') a = space;
                    }
                    int b = Trimmed(a, end, ref a);
                    if (a == b) { Close(); return; }
                    if (leaf == Leaf.Quote && open.Level == level) { Add(from, end, a, b); return; }
                    Begin(k);
                    Open(Leaf.Quote, new WordBlock { Kind = WordKind.Quote, Level = level, Depth = k, From = from }, from, end, a, b);
                    return;
                }
                int p = pos, q = Trimmed(pos, end, ref p);
                if (leaf == Leaf.Para || leaf == Leaf.Item || leaf == Leaf.Quote) { Add(from, end, p, q); return; }
                Begin(k);
                Open(Leaf.Para, new WordBlock { Kind = WordKind.Para, Depth = k, From = from }, from, end, p, q);
            }

            // A list item: a dash, plus or star, or up to nine digits and a full stop or bracket, then a space or the line's end.
            // Items deeper than the one above them by any amount are one level deeper, never more.
            private bool StartItem(int from, int end, int pos, int col, int k)
            {
                char c = s[pos];
                int after = pos, number = 0;
                char sign;
                if (c == '-' || c == '+' || c == '*') { after = pos + 1; sign = 'b'; }
                else
                {
                    while (after < end && after - pos < 9 && s[after] >= '0' && s[after] <= '9') number = number * 10 + (s[after++] - '0');
                    if (after == pos || after >= end || (s[after] != '.' && s[after] != ')')) return false;
                    sign = s[after++];
                }
                if (after < end && s[after] != ' ' && s[after] != '\t') return false;
                int textCol = col + (after - pos), spaces = 0, a = after;
                while (a < end && (s[a] == ' ' || s[a] == '\t')) { spaces += s[a] == '\t' ? 4 - (textCol + spaces) % 4 : 1; a++; }
                int b = Trimmed(a, end, ref a);
                bool interrupts = (leaf == Leaf.Para && open.Depth == k) || (leaf == Leaf.Item && open.Depth + 1 == k);
                if (interrupts && (a == b || (sign != 'b' && number != 1))) return false;

                WordBlock block = new WordBlock { Depth = k, From = from };
                if (b - a >= 3 && s[a] == '[' && s[a + 2] == ']' && (s[a + 1] == ' ' || s[a + 1] == 'x' || s[a + 1] == 'X') && (a + 3 == b || s[a + 3] == ' ' || s[a + 3] == '\t'))
                {
                    block.Kind = WordKind.Task;
                    block.Checked = s[a + 1] != ' ';
                    block.Marker = block.Checked ? Ticked : Box;
                    b = Trimmed(a + 3, b, ref a);
                }
                Begin(k + 1);
                Item sibling = items.Count > k ? items[k] : null;
                if (sign != 'b' && sibling != null && sibling.Sign == sign) number = sibling.Number + 1;
                if (items.Count > k) items.RemoveRange(k, items.Count - k);
                if (block.Marker == null)
                {
                    block.Kind = sign == 'b' ? WordKind.Bullet : WordKind.Number;
                    block.Marker = sign == 'b' ? Dot : number.ToString(CultureInfo.InvariantCulture) + sign;
                }
                int content = spaces == 0 || spaces > 4 ? textCol + 1 : textCol + spaces;
                items.Add(new Item { Inside = Math.Min(content, col + 2), Number = number, Sign = sign });
                Open(Leaf.Item, block, from, end, a, b);
                return true;
            }

            // Ends the open leaf and every list item at or past the given depth.
            private void Begin(int depth)
            {
                Close();
                if (items.Count > depth) items.RemoveRange(depth, items.Count - depth);
            }

            private void Open(Leaf kind, WordBlock block, int from, int end, int a, int b)
            {
                Close();
                leaf = kind;
                open = block;
                words.Length = 0;
                lastAt = 0;
                beforeLast = -1;
                lastLine = from;
                lastFrom = a;
                lastEnd = b;
                words.Append(s, a, b - a);
                open.Length = end - from;
            }

            private void Add(int from, int end, int a, int b)
            {
                beforeLast = open.From + open.Length;
                words.Append('\n');
                lastAt = words.Length;
                lastLine = from;
                lastFrom = a;
                lastEnd = b;
                words.Append(s, a, b - a);
                open.Length = end - open.From;
            }

            private void Close()
            {
                if (leaf == Leaf.None) return;
                if (leaf == Leaf.Fence)
                {
                    if (words.Length > 0) open.Runs.Add(new WordRun { Text = words.ToString(), Code = true });
                }
                else if (leaf == Leaf.Table)
                {
                    int width = open.Align.Count;
                    open.Rows = new List<List<List<WordRun>>>(rows.Count);
                    foreach (List<string> row in rows)
                    {
                        // A row with too many cells keeps the extra ones in its last cell, so nothing typed goes missing.
                        if (row.Count > width) row[width - 1] = string.Join(" | ", row.GetRange(width - 1, row.Count - width + 1).ToArray());
                        List<List<WordRun>> cells = new List<List<WordRun>>(width);
                        for (int i = 0; i < width; i++) cells.Add(i < row.Count ? Inline(row[i]) : new List<WordRun>());
                        open.Rows.Add(cells);
                    }
                    rows = null;
                }
                else open.Runs = Inline(words.ToString());
                blocks.Add(open);
                words.Length = 0;
                leaf = Leaf.None;
                open = null;
            }

            // The first character after the line's spaces and tabs, and its column with tab stops every four.
            private int Indent(int from, int end, out int col)
            {
                col = 0;
                int at = from;
                while (at < end && (s[at] == ' ' || s[at] == '\t')) { col = s[at] == '\t' ? col + 4 - col % 4 : col + 1; at++; }
                return at;
            }

            // Trims spaces and tabs from both ends of a stretch: returns its end and moves its start.
            private int Trimmed(int a, int b, ref int start)
            {
                while (a < b && (s[a] == ' ' || s[a] == '\t')) a++;
                while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t')) b--;
                start = a;
                return b;
            }

            // A code line with up to the fence's own indent taken off, as CommonMark does; a tab cut part way leaves spaces.
            private void Strip(int from, int end, int cols)
            {
                int col = 0, at = from;
                while (at < end && col < cols && (s[at] == ' ' || s[at] == '\t'))
                {
                    int next = s[at] == '\t' ? col + 4 - col % 4 : col + 1;
                    at++;
                    if (next > cols) { words.Append(' ', next - cols); break; }
                    col = next;
                }
                words.Append(s, at, end - at);
            }

            // Three or more backticks or tildes open a fence. A backtick fence can't have a backtick after it, so ```x``` on
            // one line stays a code span.
            private bool Opens(int pos, int end)
            {
                char c = s[pos];
                if (c != '`' && c != '~') return false;
                int at = pos;
                while (at < end && s[at] == c) at++;
                if (at - pos < 3) return false;
                if (c == '`') for (; at < end; at++) if (s[at] == '`') return false;
                return true;
            }

            private bool Closes(int pos, int end)
            {
                int at = pos;
                while (at < end && s[at] == fence) at++;
                if (at - pos < fenceSize) return false;
                while (at < end && (s[at] == ' ' || s[at] == '\t')) at++;
                return at == end;
            }

            // How many hash signs start a heading here, or -1.
            private int Heading(int pos, int end)
            {
                int at = pos;
                while (at < end && s[at] == '#' && at - pos < 7) at++;
                int count = at - pos;
                if (count == 0 || count > 6) return -1;
                return at == end || s[at] == ' ' || s[at] == '\t' ? count : -1;
            }

            private bool Rule(int pos, int end)
            {
                char c = s[pos];
                if (c != '-' && c != '*' && c != '_') return false;
                int count = 0;
                for (int at = pos; at < end; at++)
                {
                    if (s[at] == c) count++;
                    else if (s[at] != ' ' && s[at] != '\t') return false;
                }
                return count >= 3;
            }

            private bool Pipe(int a, int b)
            {
                for (int at = a; at < b; at++) if (s[at] == '|') return true;
                return false;
            }

            // A table's dashed row: a pipe somewhere, and cells of hyphens with a colon at either end for alignment.
            private bool Dashes(int pos, int end, out List<WordAlign> align)
            {
                align = null;
                char c = s[pos];
                if (c != '|' && c != '-' && c != ':') return false;
                for (int at = pos; at < end; at++)
                    if ("|-: \t".IndexOf(s[at]) < 0) return false;
                if (!Pipe(pos, end)) return false;
                List<WordAlign> found = new List<WordAlign>();
                foreach (string cell in Cells(pos, end))
                {
                    int a = 0, b = cell.Length;
                    bool left = b > 0 && cell[0] == ':', right = b > 1 && cell[b - 1] == ':';
                    if (left) a++;
                    if (right) b--;
                    if (a >= b) return false;
                    for (int at = a; at < b; at++) if (cell[at] != '-') return false;
                    found.Add(left && right ? WordAlign.Center : right ? WordAlign.Right : left ? WordAlign.Left : WordAlign.None);
                }
                align = found;
                return true;
            }

            // A table row's cells, trimmed, with the outer pipes dropped and an escaped pipe kept as a pipe.
            private List<string> Cells(int a, int b)
            {
                while (a < b && (s[a] == ' ' || s[a] == '\t')) a++;
                while (b > a && (s[b - 1] == ' ' || s[b - 1] == '\t')) b--;
                if (a < b && s[a] == '|') a++;
                List<string> cells = new List<string>();
                StringBuilder cell = new StringBuilder();
                bool closed = false;
                for (int at = a; at < b; at++)
                {
                    closed = false;
                    char c = s[at];
                    if (c == '\\' && at + 1 < b)
                    {
                        if (s[at + 1] != '|') cell.Append(c);
                        cell.Append(s[++at]);
                        continue;
                    }
                    if (c != '|') { cell.Append(c); continue; }
                    cells.Add(cell.ToString().Trim(' ', '\t'));
                    cell.Length = 0;
                    closed = true;
                }
                if (!closed || cells.Count == 0) cells.Add(cell.ToString().Trim(' ', '\t'));
                return cells;
            }
        }

        private static bool Special(char c)
        {
            switch (c)
            {
                case '\\': case '`': case '[': case ']': case '!': case '<': case '*': case '_': case '~': case 'h': case 'H': case 'w': case 'W': return true;
                default: return false;
            }
        }

        private static bool Punct(char c)
        {
            return (c >= '!' && c <= '/') || (c >= ':' && c <= '@') || (c >= '[' && c <= '`') || (c >= '{' && c <= '~');
        }

        // A piece of a line: plain text, a code span, a run of marks that may pair, or where a link starts or ends.
        private sealed class Bit
        {
            internal string Text, Link, LinkStart;
            internal bool Code, LinkEnd, Opens, Closes;
            internal char Mark;
            internal int Count, Size, Before = -1, After = -1;
            internal int OpenBold, OpenItalic, OpenStrike, CloseBold, CloseItalic, CloseStrike;
        }

        // An opening bracket waiting for its partner, and the newest mark before it.
        private sealed class Bracket
        {
            internal int Bit, Floor;
            internal bool Image;
        }

        // Inline marks, read the way CommonMark does: code spans first, then links as their closing bracket arrives, then
        // stars, underscores and tildes paired from the inside out. One reader serves a whole reply, block after block, so
        // a table of thousands of cells doesn't build thousands of readers.
        private sealed class InlineReader
        {
            private string s;
            private int n;
            private readonly List<Bit> bits = new List<Bit>();
            private readonly List<Bracket> brackets = new List<Bracket>();
            private readonly StringBuilder text = new StringBuilder(), joined = new StringBuilder();
            private readonly int[] bottom = new int[18];
            private int first, last, live, wordEnd, tick;
            private bool path;
            private int[] tickStart, tickSize, tickSame, tickShort, pair, white, solid;

            internal List<WordRun> Read(string value)
            {
                int at = 0;
                while (at < value.Length && !Special(value[at])) at++;
                if (at == value.Length) return value.Length == 0 ? new List<WordRun>() : new List<WordRun> { new WordRun { Text = value } };
                s = value;
                n = value.Length;
                bits.Clear();
                brackets.Clear();
                text.Length = 0;
                text.Append(s, 0, at);
                first = last = wordEnd = -1;
                live = tick = 0;
                path = false;
                tickStart = tickSize = tickSame = tickShort = pair = white = solid = null;
                while (at < n)
                {
                    char c = s[at];
                    if (!Special(c))
                    {
                        int from = at;
                        while (at < n && !Special(s[at])) at++;
                        text.Append(s, from, at - from);
                        continue;
                    }
                    if (c == '\\') at = Escape(at);
                    else if (c == '`') at = Ticks(at);
                    else if (c == '[' || (c == '!' && at + 1 < n && s[at + 1] == '['))
                    {
                        Flush();
                        bool image = c == '!';
                        bits.Add(new Bit { Text = image ? "![" : "[" });
                        brackets.Add(new Bracket { Bit = bits.Count - 1, Floor = last, Image = image });
                        at += image ? 2 : 1;
                    }
                    else if (c == ']') at = CloseBracket(at);
                    else if (c == '<') at = Angle(at);
                    else if (c == '*' || c == '_' || c == '~') at = Marks(at);
                    else if (c == 'h' || c == 'H' || c == 'w' || c == 'W') at = Bare(at);
                    else { text.Append(c); at++; }
                }
                Flush();
                Emphasis(-1);
                return Write();
            }

            private void Flush()
            {
                if (text.Length == 0) return;
                bits.Add(new Bit { Text = text.ToString() });
                text.Length = 0;
            }

            // A backslash makes the punctuation after it literal, except inside a Windows path, where it is a separator.
            // Before a line break it is a hard break: the backslash goes and the break stays.
            private int Escape(int at)
            {
                if (at + 1 < n)
                {
                    char next = s[at + 1];
                    if (next == '|' || ((Punct(next) || next == '\n') && !InPath(at)))
                    {
                        if (next == '\n') return at + 1;
                        text.Append(next);
                        return at + 2;
                    }
                }
                text.Append('\\');
                return at + 1;
            }

            // Whether the word around this position, out to whitespace on both sides, has a backslash before a letter or
            // digit, like C:\Users or app\Words.cs. Positions only move forward, so each word is looked at once.
            private bool InPath(int at)
            {
                if (at < wordEnd) return path;
                int a = at, b = at;
                while (a > 0 && !char.IsWhiteSpace(s[a - 1])) a--;
                while (b < n && !char.IsWhiteSpace(s[b])) b++;
                path = false;
                for (int i = a; i + 1 < b; i++)
                    if (s[i] == '\\' && s[i + 1] < 128 && char.IsLetterOrDigit(s[i + 1])) { path = true; break; }
                wordEnd = b;
                return path;
            }

            // A code span: a run of backticks up to the next run of exactly as many. With no such run the ticks stay.
            private int Ticks(int at)
            {
                int size = 0;
                while (at + size < n && s[at + size] == '`') size++;
                int a = at + size, b = -1;
                if (n <= 160)
                {
                    // A short line, like a table cell, just looks ahead; the lookup is for lines where that could add up.
                    for (int i = a; i < n && b < 0; i++)
                    {
                        if (s[i] != '`') continue;
                        int from = i;
                        while (i + 1 < n && s[i + 1] == '`') i++;
                        if (i - from + 1 == size) b = from;
                    }
                }
                else
                {
                    if (tickStart == null) Measure();
                    while (tickStart[tick] + tickSize[tick] <= at) tick++;
                    int close = tickStart[tick] == at ? tickSame[tick] : tickShort[tick];
                    if (close >= 0) { b = tickStart[close]; tick = close; }
                }
                if (b < 0) { text.Append('`', size); return a; }
                Flush();
                string code = s.Substring(a, b - a).Replace('\n', ' ');
                if (code.Length >= 2 && code[0] == ' ' && code[code.Length - 1] == ' ' && code.Trim(' ').Length > 0) code = code.Substring(1, code.Length - 2);
                if (code.Length > 0) bits.Add(new Bit { Text = code, Code = true });
                return b + size;
            }

            // Every run of backticks, and for each the next run as long as it, or one shorter for a run whose first tick
            // was escaped.
            private void Measure()
            {
                List<int> starts = new List<int>(), sizes = new List<int>();
                for (int i = 0; i < n; i++)
                {
                    if (s[i] != '`') continue;
                    int from = i;
                    while (i + 1 < n && s[i + 1] == '`') i++;
                    starts.Add(from);
                    sizes.Add(i - from + 1);
                }
                tickStart = starts.ToArray();
                tickSize = sizes.ToArray();
                tickSame = new int[tickStart.Length];
                tickShort = new int[tickStart.Length];
                Dictionary<int, int> next = new Dictionary<int, int>();
                for (int i = tickStart.Length - 1; i >= 0; i--)
                {
                    int found;
                    tickSame[i] = next.TryGetValue(tickSize[i], out found) ? found : -1;
                    tickShort[i] = tickSize[i] > 1 && next.TryGetValue(tickSize[i] - 1, out found) ? found : -1;
                    next[tickSize[i]] = i;
                }
            }

            // A closing bracket with a round-bracket address after it makes a link of everything back to its opener. Once a
            // link is made, the openers before it can't make links any more: links don't hold links.
            private int CloseBracket(int at)
            {
                if (brackets.Count == 0) { text.Append(']'); return at + 1; }
                Bracket top = brackets[brackets.Count - 1];
                brackets.RemoveAt(brackets.Count - 1);
                bool active = brackets.Count >= live;
                if (live > brackets.Count) live = brackets.Count;
                int end;
                string url;
                if (!active || !Address(at + 1, out end, out url)) { text.Append(']'); return at + 1; }
                Flush();
                Emphasis(top.Floor);
                bits[top.Bit].Text = "";
                bits[top.Bit].LinkStart = url;
                // A link with no words shows its address, so it can still be seen and copied.
                if (top.Bit == bits.Count - 1) bits.Add(new Bit { Text = url });
                bits.Add(new Bit { Text = "", LinkEnd = true });
                if (!top.Image) live = brackets.Count;
                return end;
            }

            // The round-bracket address after a link's text: a destination, then perhaps a quoted title, which is dropped.
            // The lookups make a failed try cost the same however long the rest of the line is.
            private bool Address(int at, out int end, out string url)
            {
                end = at;
                url = null;
                if (at >= n || s[at] != '(') return false;
                if (pair == null) Pairs();
                int close = pair[at] - 1;
                if (close < 0) return false;
                int a = solid[at + 1], b;
                string dest;
                if (a >= close) dest = "";
                else if (s[a] == '<')
                {
                    b = a + 1;
                    while (b < close && s[b] != '>' && s[b] != '<' && s[b] != '\n') b++;
                    if (b >= close || s[b] != '>' || !Title(b + 1, close)) return false;
                    dest = s.Substring(a + 1, b - a - 1);
                }
                else
                {
                    b = Math.Min(white[a], close);
                    if (!Title(b, close)) return false;
                    dest = s.Substring(a, b - a);
                }
                StringBuilder clean = new StringBuilder(dest.Length);
                for (int i = 0; i < dest.Length; i++)
                {
                    if (dest[i] == '\\' && i + 1 < dest.Length && Punct(dest[i + 1])) i++;
                    clean.Append(dest[i]);
                }
                url = clean.ToString();
                end = close + 1;
                return true;
            }

            private bool Title(int b, int close)
            {
                int t = Math.Min(solid[b], close);
                if (t >= close) return true;
                char q = s[t];
                if (t == b || (q != '"' && q != '\'' && q != '(')) return false;
                int z = close - 1;
                while (z > t && char.IsWhiteSpace(s[z])) z--;
                return z > t && s[z] == (q == '(' ? ')' : q);
            }

            // For each opening round bracket its partner, and for each position the next space and the next non-space.
            private void Pairs()
            {
                pair = new int[n];
                white = new int[n + 1];
                solid = new int[n + 1];
                int[] stack = new int[n];
                int depth = 0;
                for (int i = 0; i < n; i++)
                {
                    char c = s[i];
                    if (c == '\\' && i + 1 < n && Punct(s[i + 1])) { i++; continue; }
                    if (c == '(') stack[depth++] = i;
                    else if (c == ')' && depth > 0) pair[stack[--depth]] = i + 1;
                }
                white[n] = n;
                solid[n] = n;
                for (int i = n - 1; i >= 0; i--)
                {
                    bool space = char.IsWhiteSpace(s[i]);
                    white[i] = space ? i : white[i + 1];
                    solid[i] = space ? solid[i + 1] : i;
                }
            }

            // <https://...> is a link; anything else in angle brackets, raw HTML included, stays as typed.
            private int Angle(int at)
            {
                int a = at + 1;
                if (Starts(a, "http://") || Starts(a, "https://") || Starts(a, "mailto:"))
                {
                    int b = a;
                    while (b < n && !char.IsWhiteSpace(s[b]) && s[b] != '<' && s[b] != '>') b++;
                    if (b < n && s[b] == '>')
                    {
                        Flush();
                        string url = s.Substring(a, b - a);
                        bits.Add(new Bit { Text = url, Link = url });
                        return b + 1;
                    }
                }
                text.Append('<');
                return at + 1;
            }

            // A bare http, https or www address at the start of a word. Sentence punctuation after it, closing marks and a
            // closing bracket with no partner inside it stay outside it.
            private int Bare(int at)
            {
                char before = at > 0 ? s[at - 1] : ' ';
                int head = !(char.IsWhiteSpace(before) || "*_~(\"'".IndexOf(before) >= 0) ? 0 : Starts(at, "https://") ? 8 : Starts(at, "http://") ? 7 : Starts(at, "www.") ? 4 : 0;
                if (head == 0) { text.Append(s[at]); return at + 1; }
                int b = at, opened = 0, shut = 0;
                while (b < n && !char.IsWhiteSpace(s[b]) && "<>`\"[]".IndexOf(s[b]) < 0)
                {
                    if (s[b] == '(') opened++;
                    else if (s[b] == ')') shut++;
                    b++;
                }
                while (b > at + head)
                {
                    char c = s[b - 1];
                    if (".,:;!?*_~'".IndexOf(c) >= 0) b--;
                    else if (c == ')' && shut > opened) { b--; shut--; }
                    else break;
                }
                if (b <= at + head) { text.Append(s[at]); return at + 1; }
                Flush();
                string url = s.Substring(at, b - at);
                bits.Add(new Bit { Text = url, Link = head == 4 ? "http://" + url : url });
                return b;
            }

            private bool Starts(int at, string word)
            {
                return at + word.Length <= n && string.Compare(s, at, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0;
            }

            // A run of stars, underscores or exactly two tildes, and whether it can open or close, from what sits either side.
            private int Marks(int at)
            {
                char c = s[at];
                int size = 1;
                while (at + size < n && s[at + size] == c) size++;
                if (c == '~' && size != 2) { text.Append('~', size); return at + size; }
                int before = Class(at - 1), after = Class(at + size);
                bool left = after != 0 && (after == 2 || before != 2);
                bool right = before != 0 && (before == 2 || after != 2);
                char p = at > 0 ? s[at - 1] : ' ', q = at + size < n ? s[at + size] : ' ';
                bool opens = left, closes = right;
                if (c == '_')
                {
                    // snake_case, __init__.py and C:\a_b stay as typed: an underscore beside a letter, a slash or a dotted
                    // name ending is part of the word.
                    bool wordBefore = before == 2 || p == '/' || p == '\\';
                    bool wordAfter = after == 2 || q == '/' || q == '\\' || (q == '.' && Class(at + size + 1) == 2);
                    opens = left && (!right || before == 1) && !wordBefore;
                    closes = right && (!left || after == 1) && !wordAfter;
                }
                else if (c == '*')
                {
                    // A star beside a slash is a glob, like **/*.cs or src/*/x.
                    bool slashBefore = p == '/' || p == '\\', slashAfter = q == '/' || q == '\\';
                    if (slashBefore || slashAfter) opens = false;
                    if (slashBefore && after != 0) closes = false;
                }
                if (!opens && !closes) { text.Append(c, size); return at + size; }
                Flush();
                bits.Add(new Bit { Mark = c, Count = size, Size = size, Opens = opens, Closes = closes, Before = last });
                int index = bits.Count - 1;
                if (last >= 0) bits[last].After = index;
                else first = index;
                last = index;
                return at + size;
            }

            // What sits at a position: 0 for whitespace or the edge, 1 for punctuation or a symbol (emoji included), 2 for
            // anything else.
            private int Class(int at)
            {
                if (at < 0 || at >= n) return 0;
                char c = s[at];
                if (c < 128)
                {
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '\f' || c == '\v') return 0;
                    return Punct(c) ? 1 : 2;
                }
                if (char.IsLowSurrogate(c) && at > 0 && char.IsHighSurrogate(s[at - 1])) at--;
                if (char.IsWhiteSpace(s[at])) return 0;
                switch (CharUnicodeInfo.GetUnicodeCategory(s, at))
                {
                    case UnicodeCategory.ConnectorPunctuation: case UnicodeCategory.DashPunctuation: case UnicodeCategory.OpenPunctuation:
                    case UnicodeCategory.ClosePunctuation: case UnicodeCategory.InitialQuotePunctuation: case UnicodeCategory.FinalQuotePunctuation:
                    case UnicodeCategory.OtherPunctuation: case UnicodeCategory.MathSymbol: case UnicodeCategory.CurrencySymbol:
                    case UnicodeCategory.ModifierSymbol: case UnicodeCategory.OtherSymbol:
                        return 1;
                    default:
                        return 2;
                }
            }

            // CommonMark's pairing of the marks newer than the floor, from the inside out. A closer that finds no opener
            // raises the floor for its kind of mark, so nothing is searched twice. The marks are then done with.
            private void Emphasis(int floor)
            {
                for (int i = 0; i < bottom.Length; i++) bottom[i] = floor;
                int closer = floor < 0 ? first : bits[floor].After;
                while (closer >= 0)
                {
                    Bit c = bits[closer];
                    if (!c.Closes) { closer = c.After; continue; }
                    int slot = (c.Mark == '*' ? 0 : c.Mark == '_' ? 6 : 12) + (c.Opens ? 3 : 0) + c.Size % 3;
                    int opener = c.Before;
                    bool found = false;
                    while (opener >= 0 && opener != floor && opener != bottom[slot])
                    {
                        Bit o = bits[opener];
                        if (o.Mark == c.Mark && o.Opens)
                        {
                            bool odd = c.Mark != '~' && (c.Opens || o.Closes) && c.Size % 3 != 0 && (o.Size + c.Size) % 3 == 0;
                            if (!odd) { found = true; break; }
                        }
                        opener = o.Before;
                    }
                    if (found)
                    {
                        Bit o = bits[opener];
                        int use = c.Mark == '~' || (c.Count >= 2 && o.Count >= 2) ? 2 : 1;
                        o.Count -= use;
                        c.Count -= use;
                        if (c.Mark == '~') { o.OpenStrike++; c.CloseStrike++; }
                        else if (use == 2) { o.OpenBold++; c.CloseBold++; }
                        else { o.OpenItalic++; c.CloseItalic++; }
                        o.After = closer;
                        c.Before = opener;
                        if (o.Count == 0) Unlink(opener);
                        if (c.Count == 0)
                        {
                            int next = c.After;
                            Unlink(closer);
                            closer = next;
                        }
                    }
                    else
                    {
                        int next = c.After;
                        bottom[slot] = c.Before;
                        if (!c.Opens) Unlink(closer);
                        closer = next;
                    }
                }
                if (floor < 0) { first = -1; last = -1; }
                else { bits[floor].After = -1; last = floor; }
            }

            private void Unlink(int index)
            {
                Bit bit = bits[index];
                if (bit.Before >= 0) bits[bit.Before].After = bit.After;
                else first = bit.After;
                if (bit.After >= 0) bits[bit.After].Before = bit.Before;
                else last = bit.Before;
            }

            // Walks the pieces once, carrying the styles opened so far, and joins neighbours that look the same.
            private List<WordRun> Write()
            {
                List<WordRun> runs = new List<WordRun>();
                joined.Length = 0;
                WordRun run = null;
                int bold = 0, italic = 0, strike = 0;
                // An image can sit in a link's words and a link in an image's: the innermost address wins.
                List<string> links = new List<string>();
                foreach (Bit bit in bits)
                {
                    if (bit.LinkStart != null) links.Add(bit.LinkStart.Length > 0 ? bit.LinkStart : null);
                    string value = bit.Text;
                    bool code = bit.Code;
                    string address = (links.Count > 0 ? links[links.Count - 1] : null) ?? bit.Link;
                    if (bit.Mark != '\0')
                    {
                        bold -= bit.CloseBold;
                        italic -= bit.CloseItalic;
                        strike -= bit.CloseStrike;
                        value = bit.Count > 0 ? new string(bit.Mark, bit.Count) : null;
                    }
                    if (!string.IsNullOrEmpty(value))
                    {
                        if (run == null || run.Bold != bold > 0 || run.Italic != italic > 0 || run.Strike != strike > 0 || run.Code != code || !string.Equals(run.Link, address, StringComparison.Ordinal))
                        {
                            if (run != null) { run.Text = joined.ToString(); runs.Add(run); joined.Length = 0; }
                            run = new WordRun { Bold = bold > 0, Italic = italic > 0, Strike = strike > 0, Code = code, Link = address };
                        }
                        joined.Append(value);
                    }
                    if (bit.Mark != '\0')
                    {
                        bold += bit.OpenBold;
                        italic += bit.OpenItalic;
                        strike += bit.OpenStrike;
                    }
                    if (bit.LinkEnd && links.Count > 0) links.RemoveAt(links.Count - 1);
                }
                if (run != null) { run.Text = joined.ToString(); runs.Add(run); }
                return runs;
            }
        }

        // The characters a block shows, without its marker: its runs end to end, a code block as written, a table's cells
        // split by tabs and its rows by line breaks, and nothing for a rule. A selection counts characters along this.
        internal static string Shown(WordBlock block)
        {
            if (block == null) return "";
            StringBuilder text = new StringBuilder();
            if (block.Kind == WordKind.Table && block.Rows != null)
            {
                for (int row = 0; row < block.Rows.Count; row++)
                {
                    if (row > 0) text.Append('\n');
                    for (int cell = 0; cell < block.Rows[row].Count; cell++)
                    {
                        if (cell > 0) text.Append('\t');
                        foreach (WordRun run in block.Rows[row][cell]) text.Append(run.Text);
                    }
                }
            }
            else if (block.Runs != null) foreach (WordRun run in block.Runs) text.Append(run.Text);
            return text.ToString();
        }

        // What a person expects to paste: no marks, list markers and their indent kept, quote lines starting with "> ", code
        // as written, table cells split by tabs so they paste into a spreadsheet, and a link as the words it shows.
        internal static string Plain(List<WordBlock> blocks)
        {
            if (blocks == null || blocks.Count == 0) return "";
            return Plain(blocks, 0, 0, blocks.Count - 1, int.MaxValue);
        }

        // A selection's paste, from character fromAt of block from to character toAt of block to, both counted along Shown.
        // A block taken whole keeps its marker and indent; a block cut part way gives just the characters selected.
        internal static string Plain(List<WordBlock> blocks, int from, int fromAt, int to, int toAt)
        {
            if (blocks == null || blocks.Count == 0) return "";
            from = Math.Max(0, Math.Min(from, blocks.Count - 1));
            to = Math.Max(from, Math.Min(to, blocks.Count - 1));
            StringBuilder text = new StringBuilder();
            for (int i = from; i <= to; i++)
            {
                WordBlock block = blocks[i];
                if (i > from) text.Append(Listed(blocks[i - 1]) && Listed(block) ? "\n" : "\n\n");
                string shown = Shown(block);
                int a = i == from ? Math.Max(0, Math.Min(fromAt, shown.Length)) : 0;
                int b = i == to ? Math.Max(a, Math.Min(toAt, shown.Length)) : shown.Length;
                if (a == 0 && b == shown.Length && !(from == to && shown.Length > 0 && fromAt == toAt)) text.Append(Whole(block, shown));
                else text.Append(shown, a, b - a);
            }
            return text.ToString();
        }

        private static bool Listed(WordBlock block)
        {
            return block.Kind == WordKind.Bullet || block.Kind == WordKind.Number || block.Kind == WordKind.Task || block.Depth > 0;
        }

        private static string Whole(WordBlock block, string shown)
        {
            string pad = new string(' ', Math.Max(0, block.Depth) * 2);
            switch (block.Kind)
            {
                case WordKind.Code:
                case WordKind.Table:
                    return shown;
                case WordKind.Rule:
                    return pad + "---";
                case WordKind.Bullet:
                case WordKind.Number:
                case WordKind.Task:
                    string marker = block.Marker ?? Dot;
                    return pad + marker + " " + shown.Replace("\n", "\n" + pad + new string(' ', marker.Length + 1));
                case WordKind.Quote:
                    string quote = pad + new string('>', Math.Max(1, block.Level)) + " ";
                    return quote + shown.Replace("\n", "\n" + quote);
                default:
                    return pad + shown.Replace("\n", "\n" + pad);
            }
        }

        // One line per block, used by the checks: kind, depth, marker and runs with their styles and links.
        internal static string Describe(List<WordBlock> blocks)
        {
            StringBuilder text = new StringBuilder();
            if (blocks == null) return "";
            foreach (WordBlock block in blocks)
            {
                if (text.Length > 0) text.Append(" | ");
                Describe(text, block);
            }
            return text.ToString();
        }

        private static void Describe(StringBuilder text, WordBlock block)
        {
            switch (block.Kind)
            {
                case WordKind.Para: text.Append("para"); break;
                case WordKind.Heading: text.Append("head").Append(block.Level); break;
                case WordKind.Bullet: text.Append("bullet").Append(block.Depth); break;
                case WordKind.Number: text.Append("number").Append(block.Depth).Append('[').Append(block.Marker).Append(']'); break;
                case WordKind.Task: text.Append("task").Append(block.Depth).Append(block.Checked ? "[x]" : "[ ]"); break;
                case WordKind.Quote: text.Append("quote").Append(block.Level); break;
                case WordKind.Rule: text.Append("rule"); break;
                case WordKind.Code: text.Append("code[").Append(block.Lang).Append(']'); break;
                case WordKind.Table: text.Append("table"); break;
            }
            bool item = block.Kind == WordKind.Bullet || block.Kind == WordKind.Number || block.Kind == WordKind.Task;
            if (!item && block.Depth > 0) text.Append('@').Append(block.Depth);
            if (block.Kind == WordKind.Rule) return;
            if (block.Kind == WordKind.Table)
            {
                text.Append('[');
                for (int i = 0; i < block.Align.Count; i++) text.Append(i > 0 ? "," : "").Append("-lcr"[(int)block.Align[i]]);
                text.Append("]{");
                for (int row = 0; row < block.Rows.Count; row++)
                {
                    if (row > 0) text.Append(';');
                    for (int cell = 0; cell < block.Rows[row].Count; cell++)
                    {
                        if (cell > 0) text.Append(',');
                        Runs(text, block.Rows[row][cell]);
                    }
                }
                text.Append('}');
                return;
            }
            Runs(text, block.Runs);
        }

        private static void Runs(StringBuilder text, List<WordRun> runs)
        {
            text.Append('(');
            for (int at = 0; at < runs.Count; at++)
            {
                WordRun run = runs[at];
                if (at > 0) text.Append(',');
                if (run.Bold) text.Append('b');
                if (run.Italic) text.Append('i');
                if (run.Strike) text.Append('s');
                if (run.Code) text.Append('c');
                if (run.Bold || run.Italic || run.Strike || run.Code) text.Append(':');
                text.Append(Quoted(run.Text));
                if (run.Link != null) text.Append('@').Append(Quoted(run.Link));
            }
            text.Append(')');
        }

        private static string Quoted(string text)
        {
            StringBuilder value = new StringBuilder("\"");
            foreach (char c in text ?? "")
            {
                if (c == '\\') value.Append("\\\\");
                else if (c == '"') value.Append("\\\"");
                else if (c == '\n') value.Append("\\n");
                else if (c == '\t') value.Append("\\t");
                else value.Append(c);
            }
            return value.Append('"').ToString();
        }

        // Every check the reader has: exact cases, pastes, three real replies cut at every length, random strings of marks,
        // and big inputs that would show any slow path. An empty list means all of them passed.
        internal static List<string> Check()
        {
            List<string> failed = new List<string>();
            Cases(failed);
            Pastes(failed);
            foreach (string reply in Replies) Streams(failed, reply);
            Shapes(failed);
            Random random = new Random(20261112);
            for (int i = 0; i < 1500; i++)
            {
                string text = Scramble(random, 1 + random.Next(40));
                Sound(failed, text);
                if (i < 300) Streams(failed, text);
                if (failed.Count > 40) break;
            }
            foreach (KeyValuePair<string, string> big in Big())
            {
                Read(big.Value);
                Stopwatch took = Stopwatch.StartNew();
                List<WordBlock> blocks = new BlockReader(big.Value).Read();
                took.Stop();
                if (took.ElapsedMilliseconds > 250) failed.Add("200,000 characters of " + big.Key + " took " + took.ElapsedMilliseconds + " ms");
                Sound(failed, big.Value, blocks);
            }
            return failed;
        }

        private static void Cases(List<string> failed)
        {
            string[] cases =
            {
                null, "",
                "", "",
                " \t\r\n\t \r ", "",
                "hello", "para(\"hello\")",
                "one\ntwo", "para(\"one\\ntwo\")",
                "one\n\ntwo", "para(\"one\") | para(\"two\")",
                "one\r\ntwo\rthree\nfour", "para(\"one\\ntwo\\nthree\\nfour\")",
                "  indented  \n   next", "para(\"indented\\nnext\")",
                "<b>raw</b> <br> &amp;", "para(\"<b>raw</b> <br> &amp;\")",
                "# Title ###", "head1(\"Title\")",
                "  ## **Bold**", "head2(b:\"Bold\")",
                "#### Four", "head3(\"Four\")",
                "####### Seven", "para(\"####### Seven\")",
                "#tag", "para(\"#tag\")",
                "    # Deep", "para(\"# Deep\")",
                "# Using C#", "head1(\"Using C#\")",
                "#", "head1()",
                "---", "rule",
                " * * * ", "rule",
                "name\n---", "para(\"name\") | rule",
                "--", "para(\"--\")",
                "**bold**", "para(b:\"bold\")",
                "__bold__", "para(b:\"bold\")",
                "*it*", "para(i:\"it\")",
                "_it_", "para(i:\"it\")",
                "***both***", "para(bi:\"both\")",
                "~~gone~~", "para(s:\"gone\")",
                "**a *b* c**", "para(b:\"a \",bi:\"b\",b:\" c\")",
                "*a **b** c*", "para(i:\"a \",bi:\"b\",i:\" c\")",
                "~~**cut**~~", "para(bs:\"cut\")",
                "~one~ ~~~three~~~", "para(\"~one~ ~~~three~~~\")",
                "~/.claude takes ~5 min", "para(\"~/.claude takes ~5 min\")",
                "**Note:** do it", "para(b:\"Note:\",\" do it\")",
                "**bold\nacross**", "para(b:\"bold\\nacross\")",
                "snake_case_name", "para(\"snake_case_name\")",
                "__init__.py", "para(\"__init__.py\")",
                "see foo/__init__.py and _this_", "para(\"see foo/__init__.py and \",i:\"this\")",
                "**/*.cs", "para(\"**/*.cs\")",
                "src/*.cs and lib/*.cs", "para(\"src/*.cs and lib/*.cs\")",
                "2 * 3 * 4", "para(\"2 * 3 * 4\")",
                "C:\\Users\\a_b\\*.txt", "para(\"C:\\\\Users\\\\a_b\\\\*.txt\")",
                "Open C:\\dev\\_build\\out.log.", "para(\"Open C:\\\\dev\\\\_build\\\\out.log.\")",
                "\\\\server\\share\\*.txt", "para(\"\\\\\\\\server\\\\share\\\\*.txt\")",
                "\\*literal\\* \\_\\_init\\_\\_", "para(\"*literal* __init__\")",
                "a \\ b \\q", "para(\"a \\\\ b \\\\q\")",
                "hard\\\nbreak", "para(\"hard\\nbreak\")",
                "*", "bullet0()",
                "[](https://x.io) ![](pic.png)", "para(\"https://x.io\"@\"https://x.io\",\" \",\"pic.png\"@\"pic.png\")",
                "**unfinished", "para(\"**unfinished\")",
                "a **bold*", "para(\"a *\",i:\"bold\")",
                "** spaced **", "para(\"** spaced **\")",
                "(_word_).", "para(\"(\",i:\"word\",\").\")",
                "`code`", "para(c:\"code\")",
                "` **raw** `", "para(c:\"**raw**\")",
                "``a ` b``", "para(c:\"a ` b\")",
                "`unfinished", "para(\"`unfinished\")",
                "**`x`**", "para(bc:\"x\")",
                "`a*b*c` and `x_y_z`", "para(c:\"a*b*c\",\" and \",c:\"x_y_z\")",
                "\\``x`", "para(\"`\",c:\"x\")",
                "use `C:\\a\\b` here", "para(\"use \",c:\"C:\\\\a\\\\b\",\" here\")",
                "```x```", "para(c:\"x\")",
                "[name](https://example.com)", "para(\"name\"@\"https://example.com\")",
                "[*a **b** c*](https://x)", "para(i:\"a \"@\"https://x\",bi:\"b\"@\"https://x\",i:\" c\"@\"https://x\")",
                "**[name](https://x)**", "para(b:\"name\"@\"https://x\")",
                "[name](https://x \"title\")", "para(\"name\"@\"https://x\")",
                "[name](https://x/a(b))", "para(\"name\"@\"https://x/a(b)\")",
                "[x](u wrong)", "para(\"[x](u wrong)\")",
                "[one [two] three](u)", "para(\"one [two] three\"@\"u\")",
                "[a](u) [b](v)", "para(\"a\"@\"u\",\" \",\"b\"@\"v\")",
                "[[a](u)](v)", "para(\"[\",\"a\"@\"u\",\"](v)\")",
                "[![a](i.png) b](u)", "para(\"a\"@\"i.png\",\" b\"@\"u\")",
                "[unfinished", "para(\"[unfinished\")",
                "[name](unfinished", "para(\"[name](unfinished\")",
                "![alt](pic.png)", "para(\"alt\"@\"pic.png\")",
                "[`WorkspaceWords.cs`](app/WorkspaceWords.cs)", "para(c:\"WorkspaceWords.cs\"@\"app/WorkspaceWords.cs\")",
                "[https://x](https://x)", "para(\"https://x\"@\"https://x\")",
                "<https://example.com>", "para(\"https://example.com\"@\"https://example.com\")",
                "See https://example.com.", "para(\"See \",\"https://example.com\"@\"https://example.com\",\".\")",
                "(see https://x.io/a_(b)).", "para(\"(see \",\"https://x.io/a_(b)\"@\"https://x.io/a_(b)\",\").\")",
                "**https://x.io**", "para(b:\"https://x.io\"@\"https://x.io\")",
                "www.example.com/path", "para(\"www.example.com/path\"@\"http://www.example.com/path\")",
                "```cs extra\nint x;\n```", "code[cs](c:\"int x;\")",
                "```\n**raw**\n", "code[](c:\"**raw**\")",
                "~~~text\na\n~~~~", "code[text](c:\"a\")",
                "````\na\n```\nb\n````", "code[](c:\"a\\n```\\nb\")",
                "   ```\n a\n  b\n   ```", "code[](c:\"a\\nb\")",
                "```\n```", "code[]()",
                "```\nx\n\n```", "code[](c:\"x\\n\")",
                "```\nopen\n\nstill code", "code[](c:\"open\\n\\nstill code\")",
                "- one\n+ two\n* three", "bullet0(\"one\") | bullet0(\"two\") | bullet0(\"three\")",
                "3. three\n4) four", "number0[3.](\"three\") | number0[4)](\"four\")",
                "1. a\n1. b\n1. c", "number0[1.](\"a\") | number0[2.](\"b\") | number0[3.](\"c\")",
                "- a\n  - b\n    - c\n  - d\n- e", "bullet0(\"a\") | bullet1(\"b\") | bullet2(\"c\") | bullet1(\"d\") | bullet0(\"e\")",
                "1. a\n   - b\n2. c", "number0[1.](\"a\") | bullet1(\"b\") | number0[2.](\"c\")",
                "1. a\n  - b", "number0[1.](\"a\") | bullet1(\"b\")",
                "- a\n        - b", "bullet0(\"a\") | bullet1(\"b\")",
                "- a\n - b", "bullet0(\"a\") | bullet0(\"b\")",
                "- a\n\t- b\n\t\t- c", "bullet0(\"a\") | bullet1(\"b\") | bullet2(\"c\")",
                "- one\n  still one", "bullet0(\"one\\nstill one\")",
                "- one\nlazy", "bullet0(\"one\\nlazy\")",
                "- one\n\n  more of one\n- two", "bullet0(\"one\") | para@1(\"more of one\") | bullet0(\"two\")",
                "- one\n\npara", "bullet0(\"one\") | para(\"para\")",
                "- [ ] todo\n- [x] done\n- [X] also", "task0[ ](\"todo\") | task0[x](\"done\") | task0[x](\"also\")",
                "- [ ]", "task0[ ]()",
                "- [link](u)", "bullet0(\"link\"@\"u\")",
                "- item\n  ```cs\n  x\n    y\n  ```\n- next", "bullet0(\"item\") | code[cs]@1(c:\"x\\n  y\") | bullet0(\"next\")",
                "1. Run:\n   ```sh\n   npm i\n   ```\n2. Then", "number0[1.](\"Run:\") | code[sh]@1(c:\"npm i\") | number0[2.](\"Then\")",
                "Steps:\n2. not a list", "para(\"Steps:\\n2. not a list\")",
                "Steps:\n1. a", "para(\"Steps:\") | number0[1.](\"a\")",
                "text\n- item", "para(\"text\") | bullet0(\"item\")",
                "10. ten\n11. eleven", "number0[10.](\"ten\") | number0[11.](\"eleven\")",
                "1. a\n\npara\n\n2. b", "number0[1.](\"a\") | para(\"para\") | number0[2.](\"b\")",
                "> **bold**\n> next", "quote1(b:\"bold\",\"\\nnext\")",
                "> one\n>> two\n> three", "quote1(\"one\") | quote2(\"two\") | quote1(\"three\")",
                "> one\n>\n> two", "quote1(\"one\") | quote1(\"two\")",
                "> > two", "quote2(\"two\")",
                ">", "",
                "> a\nlazy", "quote1(\"a\\nlazy\")",
                "| A | B | C |\n| :--- | :---: | ---: |\n| x | a\\|b | z |\n| short |", "table[l,c,r]{(\"A\"),(\"B\"),(\"C\");(\"x\"),(\"a|b\"),(\"z\");(\"short\"),(),()}",
                "A | B\n--- | ---\nx | **y**", "table[-,-]{(\"A\"),(\"B\");(\"x\"),(b:\"y\")}",
                "A | B", "para(\"A | B\")",
                "| A | B |\n|---|", "para(\"| A | B |\\n|---|\")",
                "A | B\n--- | ---\nstop", "table[-,-]{(\"A\"),(\"B\")} | para(\"stop\")",
                "|A|\n|---|\n|x|y|", "table[-]{(\"A\");(\"x | y\")}",
                "Intro\n| A | B |\n|---|---|\n| 1 | 2 |", "para(\"Intro\") | table[-,-]{(\"A\"),(\"B\");(\"1\"),(\"2\")}",
                "- compare:\n  | A | B |\n  |---|---|\n  | 1 | 2 |", "bullet0(\"compare:\") | table@1[-,-]{(\"A\"),(\"B\");(\"1\"),(\"2\")}",
                "\ud83d\ude00**bold**\ud834\udd1e", "para(\"\ud83d\ude00\",b:\"bold\",\"\ud834\udd1e\")",
                "**\ud83d\ude00** _\ud834\udd1e_", "para(b:\"\ud83d\ude00\",\" \",i:\"\ud834\udd1e\")",
                "\ud800**x**\udfff", "para(\"\ud800\",b:\"x\",\"\udfff\")",
                "- a\r\n- b\r\n\r\n```\r\nx\r\n```", "bullet0(\"a\") | bullet0(\"b\") | code[](c:\"x\")"
            };
            for (int i = 0; i < cases.Length; i += 2)
            {
                string got = Describe(Read(cases[i]));
                if (got != cases[i + 1]) failed.Add("reading " + Quoted(cases[i]) + " gave " + got + ", wanted " + cases[i + 1]);
            }
        }

        private static void Pastes(List<string> failed)
        {
            string[] pastes =
            {
                "# **Title**\n\npara *words*\n\n---", "Title\n\npara words\n\n---",
                "3. one\n   - two\n4. three\n\n- [x] done", "3. one\n  \u2022 two\n4. three\n\u2611 done",
                "> a\n> b\n\n>> c", "> a\n> b\n\n>> c",
                "[a **b**](https://x) and <https://y>", "a b and https://y",
                "```cs\na\n b\n```", "a\n b",
                "A|B\n---|---\n1|2", "A\tB\n1\t2",
                "- a\n  cont\n\npara", "\u2022 a\n  cont\n\npara",
                "- a\n  ```\n  x\n  ```\n- b", "\u2022 a\nx\n\u2022 b"
            };
            for (int i = 0; i < pastes.Length; i += 2)
            {
                string got = Plain(Read(pastes[i]));
                if (got != pastes[i + 1]) failed.Add("pasting " + Quoted(pastes[i]) + " gave " + Quoted(got) + ", wanted " + Quoted(pastes[i + 1]));
            }
            List<WordBlock> blocks = Read("First **bold** line\n\n- item one\n- item two");
            string[] cuts = { Plain(blocks, 0, 6, 0, 10), Plain(blocks, 0, 6, 2, 4), Plain(blocks, 1, 0, 2, 8), Plain(blocks, 2, 5, 2, 5) };
            string[] wanted = { "bold", "bold line\n\n\u2022 item one\nitem", "\u2022 item one\n\u2022 item two", "" };
            for (int i = 0; i < cuts.Length; i++)
                if (cuts[i] != wanted[i]) failed.Add("selection " + i + " pasted " + Quoted(cuts[i]) + ", wanted " + Quoted(wanted[i]));
        }

        private static readonly string[] Replies =
        {
            "## Plan\n\nI'll fix the reader in three steps:\n\n1. **Find the slow path** in `WorkspaceWords.cs`:\n   - the backtick lookup re-reads the line\n   - `Escaped()` walks back over every backslash\n2. Rewrite the pairing:\n   - keep the *delimiter stack* linear\n     - with a floor per mark kind\n3. Re-run the checks.\n\nThat's it \u2014 see [the brief](C:/briefs/workspace-markdown.md) for details.\n",
            "Here's the change:\n\n```csharp\ninternal static List<WordBlock> Read(string text)\n{\n    return new BlockReader(text ?? \"\").Read();\n}\n```\n\nAnd the build line:\n\n```powershell\n$sources = @('Capri', 'WorkspaceWords')\n```\n\n| File | Lines | Change |\n|:-----|------:|:------:|\n| `app\\WorkspaceWords.cs` | 900 | new |\n| `ci\\build-windows.ps1` | 1 | edited \\| list |\n\nDone.",
            "Yes \u2014 it lives in `%APPDATA%\\Capri\\prefs.ini` under `voice.quiet`. See https://capriai.app/help for the full list, or [the docs](https://capriai.app/docs#voice)."
        };

        // The replies read whole come out in the shape a browser would show them.
        private static void Shapes(List<string> failed)
        {
            string[] shapes =
            {
                "head2(\"Plan\") | para(\"I'll fix the reader in three steps:\") | number0[1.](b:\"Find the slow path\",\" in \",c:\"WorkspaceWords.cs\",\":\") | bullet1(\"the backtick lookup re-reads the line\") | bullet1(c:\"Escaped()\",\" walks back over every backslash\") | number0[2.](\"Rewrite the pairing:\") | bullet1(\"keep the \",i:\"delimiter stack\",\" linear\") | bullet2(\"with a floor per mark kind\") | number0[3.](\"Re-run the checks.\") | para(\"That's it \u2014 see \",\"the brief\"@\"C:/briefs/workspace-markdown.md\",\" for details.\")",
                "para(\"Here's the change:\") | code[csharp](c:\"internal static List<WordBlock> Read(string text)\\n{\\n    return new BlockReader(text ?? \\\"\\\").Read();\\n}\") | para(\"And the build line:\") | code[powershell](c:\"$sources = @('Capri', 'WorkspaceWords')\") | table[l,r,c]{(\"File\"),(\"Lines\"),(\"Change\");(c:\"app\\\\WorkspaceWords.cs\"),(\"900\"),(\"new\");(c:\"ci\\\\build-windows.ps1\"),(\"1\"),(\"edited | list\")} | para(\"Done.\")",
                "para(\"Yes \u2014 it lives in \",c:\"%APPDATA%\\\\Capri\\\\prefs.ini\",\" under \",c:\"voice.quiet\",\". See \",\"https://capriai.app/help\"@\"https://capriai.app/help\",\" for the full list, or \",\"the docs\"@\"https://capriai.app/docs#voice\",\".\")"
            };
            for (int i = 0; i < shapes.Length; i++)
            {
                string got = Describe(Read(Replies[i]));
                if (got != shapes[i]) failed.Add("reply " + i + " read as " + got);
            }
        }

        // A reply cut at every length: nothing throws, and every block but the last stays as the whole reply has it. The
        // block before the last may change too in two cases: the text stops mid-line, and that unfinished line stops being a
        // heading, rule, item or fence and joins it ("#" becoming "#hashtag"); or it is a table, which takes the next line
        // as a row once a pipe arrives in it.
        private static void Streams(List<string> failed, string reply)
        {
            List<WordBlock> whole = new BlockReader(reply).Read();
            for (int cut = 0; cut <= reply.Length; cut++)
            {
                List<WordBlock> part;
                try { part = new BlockReader(reply.Substring(0, cut)).Read(); }
                catch (Exception error) { failed.Add("cutting " + Quoted(reply) + " at " + cut + " threw " + error.GetType().Name); return; }
                bool midLine = cut > 0 && reply[cut - 1] != '\n' && reply[cut - 1] != '\r';
                for (int i = 0; i + 1 < part.Count; i++)
                {
                    if (i + 2 == part.Count && (midLine || part[i].Kind == WordKind.Table)) continue;
                    WordBlock a = part[i], b = i < whole.Count ? whole[i] : null;
                    if (b == null || a.From != b.From || a.Length != b.Length || Describe(new List<WordBlock> { a }) != Describe(new List<WordBlock> { b }))
                    {
                        failed.Add("cutting " + Quoted(reply) + " at " + cut + " changed block " + i + ": " + Describe(new List<WordBlock> { a }) + " became " + (b == null ? "nothing" : Describe(new List<WordBlock> { b })));
                        return;
                    }
                }
            }
        }

        // Builds a string out of Markdown's marks, words, spaces, line breaks, tabs, emoji and other surrogate pairs.
        private static string Scramble(Random random, int pieces)
        {
            string[] parts =
            {
                "*", "**", "***", "_", "__", "~~", "~", "`", "``", "```", "~~~", "[", "]", "(", ")", "![", "<", ">", "#", "# ", "### ", "- ", "* ", "+ ",
                "1. ", "3) ", "> ", ">> ", "|", " | ", "|---|", "---", "\\", "\\*", "\\_", "\n", "\n\n", "\r\n", "\r", "\t", " ", "  ", "    ",
                "[ ] ", "[x] ", "a", "bo", "word", "snake_case", "C:\\a_b\\c", "\ud83d\ude00", "\ud834\udd1e", "\u00e9", "https://q.io/a_b", "www.y.io",
                "<b>", ":", "-", ".", ",", "(u)", "](u)", "\ud800", "\udfff", "\u00a0", "&amp;", "!"
            };
            StringBuilder text = new StringBuilder();
            for (int i = 0; i < pieces; i++) text.Append(parts[random.Next(parts.Length)]);
            return text.ToString();
        }

        private static void Sound(List<string> failed, string text)
        {
            List<WordBlock> blocks;
            try { blocks = new BlockReader(text).Read(); }
            catch (Exception error) { failed.Add("reading " + Quoted(text) + " threw " + error.GetType().Name + ": " + error.Message); return; }
            Sound(failed, text, blocks);
        }

        // Nothing typed goes missing: the letters, emoji and other word characters of the input come out in the same order
        // across the blocks' code languages, run text and link addresses. Marks, digits and spaces are left out of the
        // count, because those are what the reader is allowed to take away or renumber, and so are the words after a code
        // fence's language, which CommonMark drops too.
        private static void Sound(List<string> failed, string text, List<WordBlock> blocks)
        {
            StringBuilder shown = new StringBuilder(), typed = new StringBuilder();
            int done = 0;
            foreach (WordBlock block in blocks)
            {
                if (block.Runs == null || (block.Kind == WordKind.Table && block.Rows == null)) { failed.Add("reading " + Quoted(text) + " left a block with no runs"); return; }
                if (block.Kind == WordKind.Code)
                {
                    int fenceEnd = block.From;
                    while (fenceEnd < text.Length && text[fenceEnd] != '\n' && text[fenceEnd] != '\r') fenceEnd++;
                    typed.Append(text, done, block.From - done).Append(block.Lang);
                    done = fenceEnd;
                }
                shown.Append(block.Lang);
                if (block.Kind == WordKind.Table) foreach (List<List<WordRun>> row in block.Rows) foreach (List<WordRun> cell in row) Shown(shown, cell);
                else Shown(shown, block.Runs);
            }
            typed.Append(text, done, text.Length - done);
            string want = Letters(typed.ToString()), got = Letters(shown.ToString());
            if (want != got) failed.Add("reading " + Quoted(text) + " kept " + Quoted(got) + ", wanted " + Quoted(want));
        }

        private static void Shown(StringBuilder shown, List<WordRun> runs)
        {
            for (int i = 0; i < runs.Count; i++)
            {
                WordRun run = runs[i];
                if (run.Text == null || run.Text.Length == 0) { shown.Append("\u0001"); continue; }
                shown.Append(run.Text);
                bool lastOfLink = i + 1 == runs.Count || !ReferenceEquals(runs[i + 1].Link, run.Link);
                if (run.Link != null && lastOfLink && run.Link != run.Text && run.Link != "http://" + run.Text) shown.Append(run.Link);
            }
        }

        // The letters the sweep follows. x is left out because a task box eats it, and u because it is the sweep's only link
        // address: a link and an image inside it with the same address become one run, so it would be counted once.
        private static string Letters(string text)
        {
            StringBuilder kept = new StringBuilder();
            foreach (char c in text)
                if (c != 'x' && c != 'X' && c != 'u' && (c == '\u0001' || char.IsLetter(c) || char.IsSurrogate(c))) kept.Append(c);
            return kept.ToString();
        }

        // Inputs of 200,000 characters built to find anything slower than a straight read.
        private static List<KeyValuePair<string, string>> Big()
        {
            List<KeyValuePair<string, string>> big = new List<KeyValuePair<string, string>>();
            Func<string, string> fill = delegate(string unit)
            {
                StringBuilder text = new StringBuilder(200000 + unit.Length);
                while (text.Length < 200000) text.Append(unit);
                return text.ToString(0, 200000);
            };
            big.Add(new KeyValuePair<string, string>("plain prose", fill("The reader looks at each character a fixed number of times, however long the reply runs on. Then it stops.\n\n")));
            big.Add(new KeyValuePair<string, string>("stars and underscores", fill("*a_b**c__d*_e ***f _g_ h** i__j_*k* ")));
            big.Add(new KeyValuePair<string, string>("stars alone", fill("*")));
            big.Add(new KeyValuePair<string, string>("unclosed stars", fill("**a ")));
            big.Add(new KeyValuePair<string, string>("unclosed brackets", fill("[a](b [x")));
            big.Add(new KeyValuePair<string, string>("nested link tries", fill("[a]([a](")));
            big.Add(new KeyValuePair<string, string>("open round brackets", fill("[a](<x") + ""));
            big.Add(new KeyValuePair<string, string>("links", fill("[link](https://x.io/a) and https://y.io/b, ")));
            big.Add(new KeyValuePair<string, string>("backslashes", fill("\\")));
            big.Add(new KeyValuePair<string, string>("nested lists", Nested()));
            big.Add(new KeyValuePair<string, string>("a huge table", "| A | B | C |\n|:--|:-:|--:|\n" + fill("| a | **b** | `c` |\n")));
            big.Add(new KeyValuePair<string, string>("a huge code block", "```cs\n" + fill("    int x = a * b; // __init__ [x](y)\n")));
            big.Add(new KeyValuePair<string, string>("a line of backticks", fill("`")));
            big.Add(new KeyValuePair<string, string>("backtick runs", fill("`a``b```c````d`````e")));
            return big;
        }

        private static string Nested()
        {
            StringBuilder text = new StringBuilder();
            for (int depth = 0; text.Length < 200000; depth = (depth + 1) % 400)
                text.Append(' ', (depth < 200 ? depth : 400 - depth) * 2).Append("- item *with* `code`\n");
            return text.ToString(0, 200000);
        }
    }

    internal sealed partial class Companion
    {
        // The Workspace transcript's Markdown reader: its exact cases, pastes, streaming cuts, random strings and big inputs.
        private static void SmokeWorkspaceWords(List<string> checks)
        {
            Stopwatch took = Stopwatch.StartNew();
            List<string> failed = WorkWords.Check();
            if (failed.Count > 0) throw new Exception(failed.Count + " Workspace Markdown checks failed. First: " + failed[0]);
            checks.Add("PASS workspace Markdown reads every case, paste, streamed cut, random string and 200,000-character input right (" + took.ElapsedMilliseconds + " ms)");
        }
    }
}
