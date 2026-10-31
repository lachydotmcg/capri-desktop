using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class MiniScore
    {
        internal int You, Her, Draws;
        internal string Text { get { return "YOU " + You + "   CAPRI " + Her + "   DRAWS " + Draws; } }
    }

    internal sealed class MiniGame
    {
        internal readonly string Kind;
        internal readonly MiniScore Score;
        internal readonly int[] Cells;
        private readonly Random random;
        private bool slip;
        internal int Result, You, Her, YourHand = -1, HerHand = -1;
        internal string Last = "Your move, choom.";
        private static readonly string[] Hands = { "rock", "paper", "scissors" };
        private static readonly int[] Columns = { 3, 2, 4, 1, 5, 0, 6 };

        internal MiniGame(string kind, MiniScore score, Random dice)
        {
            Kind = kind;
            Score = score;
            random = dice;
            Cells = new int[kind == "connect_four" ? 42 : 9];
            Again();
        }

        internal string Title { get { return Kind == "tic_tac_toe" ? "TIC-TAC-TOE" : Kind == "connect_four" ? "CONNECT FOUR" : "ROCK PAPER SCISSORS"; } }

        internal void Again()
        {
            Array.Clear(Cells, 0, Cells.Length);
            Result = You = Her = 0;
            YourHand = HerHand = -1;
            slip = random.Next(4) == 0;
            Last = "Your move, choom.";
        }

        internal bool Move(int move)
        {
            if (Result != 0) { Last = "Round over. Press N for another."; return false; }
            if (Kind == "rock_paper_scissors")
            {
                if (move < 0 || move > 2) return false;
                YourHand = move;
                HerHand = random.Next(3);
                int won = HandResult(YourHand, HerHand);
                if (won == 1) You++;
                if (won == 2) Her++;
                Last = "You: " + Hands[YourHand] + ". Capri: " + Hands[HerHand] + ". " + (won == 0 ? "Tie." : won == 1 ? "Your point." : "My point.");
                if (You == 3 || Her == 3) End(You == 3 ? 1 : 2);
                return true;
            }
            int square = Kind == "connect_four" ? Landing(Cells, move) : move;
            if (square < 0 || square >= Cells.Length || Cells[square] != 0)
            {
                Last = Kind == "connect_four" ? "That column's full. Pick another." : "That square's taken. Pick another.";
                return false;
            }
            Cells[square] = 1;
            Last = "You played " + Place(square) + ".";
            int state = Outcome(Cells, Kind == "connect_four");
            if (state != 0) { End(state); return true; }
            int reply;
            if (Kind == "tic_tac_toe")
            {
                int best = TicMove(Cells, false, null);
                reply = TicMove(Cells, slip, random);
                Cells[best] = 2;
                int bestValue = TicValue(Cells, 1);
                Cells[best] = 0;
                Cells[reply] = 2;
                if (TicValue(Cells, 1) < bestValue) slip = false;
            }
            else
            {
                reply = Landing(Cells, FourMove(Cells, random));
                Cells[reply] = 2;
            }
            Last += " Capri played " + Place(reply) + ".";
            state = Outcome(Cells, Kind == "connect_four");
            if (state != 0) End(state);
            return true;
        }

        private string Place(int square)
        {
            if (Kind == "connect_four") return "column " + (square % 7 + 1);
            return new[] { "top left", "top middle", "top right", "middle left", "middle", "middle right", "bottom left", "bottom middle", "bottom right" }[square];
        }

        private void End(int result)
        {
            Result = result;
            if (result == 1) Score.You++;
            else if (result == 2) Score.Her++;
            else Score.Draws++;
        }

        internal string Status { get { return Result == 1 ? "You win. Another round?" : Result == 2 ? "Capri wins. Another round?" : Result == 3 ? "Draw. Rematch?" : Last.StartsWith("That ", StringComparison.Ordinal) ? Last : Kind == "rock_paper_scissors" ? "First to 3: you " + You + " / Capri " + Her : "Your turn. You're X, Capri is O."; } }

        internal string BoardText()
        {
            StringBuilder board = new StringBuilder("\nCapri's desktop board: " + Title + ". You are Capri, playing against the user.\n");
            if (Kind == "rock_paper_scissors")
                board.Append("First to 3. User points: ").Append(You).Append(". Capri points: ").Append(Her).Append(".\nLast hands: user ").Append(YourHand < 0 ? "none" : Hands[YourHand]).Append(", Capri ").Append(HerHand < 0 ? "none" : Hands[HerHand]).Append(".\n");
            else
            {
                int width = Kind == "connect_four" ? 7 : 3;
                board.Append("X = user, O = Capri, . = empty. Rows top to bottom.\n");
                if (width == 7) board.Append("1 2 3 4 5 6 7\n");
                for (int cell = 0; cell < Cells.Length; cell++) board.Append(Cells[cell] == 1 ? 'X' : Cells[cell] == 2 ? 'O' : '.').Append(cell % width == width - 1 ? '\n' : ' ');
            }
            board.Append(Result == 1 ? "User wins." : Result == 2 ? "Capri wins." : Result == 3 ? "Draw." : "User to move.").Append('\n').Append(Last).Append("\nSession: ").Append(Score.Text).Append(".\nAnswer their chat from this position. Moves go to mini_game; ordinary chat needs no tool.\n");
            return board.ToString();
        }

        internal static int Input(string kind, string words)
        {
            string text = Regex.Replace((words ?? "").ToLowerInvariant().Trim(), @"^(?:i (?:pick|choose|play)|pick|choose|put (?:it|mine) (?:in|at)|go)\s+", "");
            text = text.Replace('-', ' ');
            if (kind == "rock_paper_scissors") return Array.IndexOf(Hands, text);
            if (kind == "connect_four") text = Regex.Replace(text, @"^(?:column|col)\s+", "");
            else
            {
                string[] places = { "top left", "top middle", "top right", "middle left", "middle", "middle right", "bottom left", "bottom middle", "bottom right" };
                text = text.Replace("center", "middle").Replace("centre", "middle");
                if (text == "middle middle") text = "middle";
                int place = Array.IndexOf(places, text);
                if (place >= 0) return place;
                text = Regex.Replace(text, @"^(?:square|cell)\s+", "");
            }
            int number;
            string[] numbers = { "one", "two", "three", "four", "five", "six", "seven", "eight", "nine" };
            if (!int.TryParse(text, out number)) number = Array.IndexOf(numbers, text) + 1;
            return number >= 1 && number <= (kind == "connect_four" ? 7 : 9) ? number - 1 : -1;
        }

        internal static int HandResult(int you, int her) { return you == her ? 0 : (you + 2) % 3 == her ? 1 : 2; }

        internal static int Landing(int[] board, int column)
        {
            if (column < 0 || column > 6) return -1;
            for (int row = 5; row >= 0; row--) if (board[row * 7 + column] == 0) return row * 7 + column;
            return -1;
        }

        internal static int Outcome(int[] board, bool four)
        {
            int width = four ? 7 : 3, height = four ? 6 : 3, run = four ? 4 : 3;
            int[] dx = { 1, 0, 1, -1 }, dy = { 0, 1, 1, 1 };
            bool full = true;
            for (int at = 0; at < board.Length; at++)
            {
                if (board[at] == 0) { full = false; continue; }
                for (int way = 0; way < 4; way++)
                {
                    int x = at % width + dx[way] * (run - 1), y = at / width + dy[way] * (run - 1);
                    if (x < 0 || x >= width || y >= height) continue;
                    int along = 1;
                    while (along < run && board[at + along * (dx[way] + width * dy[way])] == board[at]) along++;
                    if (along == run) return board[at];
                }
            }
            return full ? 3 : 0;
        }

        internal static int TicMove(int[] board, bool wrong, Random dice)
        {
            int best = -100;
            List<int> good = new List<int>(), bad = new List<int>();
            for (int pick = 0; pick < 9; pick++)
            {
                if (board[pick] != 0) continue;
                board[pick] = 2;
                int value = TicValue(board, 1);
                board[pick] = 0;
                if (value > best) { bad.AddRange(good); good.Clear(); best = value; }
                if (value == best) good.Add(pick); else bad.Add(pick);
            }
            List<int> choices = wrong && bad.Count > 0 ? bad : good;
            return choices.Count == 0 ? -1 : choices[dice == null ? 0 : dice.Next(choices.Count)];
        }

        internal static int TicValue(int[] board, int turn)
        {
            int state = Outcome(board, false);
            if (state != 0) return state == 2 ? 1 : state == 1 ? -1 : 0;
            int best = turn == 2 ? -2 : 2;
            for (int square = 0; square < 9; square++)
            {
                if (board[square] != 0) continue;
                board[square] = turn;
                int value = TicValue(board, 3 - turn);
                board[square] = 0;
                best = turn == 2 ? Math.Max(best, value) : Math.Min(best, value);
            }
            return best;
        }

        internal static int FourMove(int[] board, Random dice)
        {
            int best = int.MinValue;
            List<int> picks = new List<int>();
            foreach (int column in Columns)
            {
                int at = Landing(board, column);
                if (at < 0) continue;
                board[at] = 2;
                int value = FourValue(board, 4, 1, -100000, 100000);
                board[at] = 0;
                if (value > best) { best = value; picks.Clear(); }
                if (value == best) picks.Add(column);
            }
            return picks.Count == 0 ? -1 : picks[dice == null ? 0 : dice.Next(picks.Count)];
        }

        private static int FourValue(int[] board, int depth, int turn, int alpha, int beta)
        {
            int state = Outcome(board, true);
            if (state != 0) return state == 2 ? 10000 + depth : state == 1 ? -10000 - depth : 0;
            if (depth == 0) return FourEstimate(board);
            int best = turn == 2 ? -100000 : 100000;
            foreach (int column in Columns)
            {
                int at = Landing(board, column);
                if (at < 0) continue;
                board[at] = turn;
                int value = FourValue(board, depth - 1, 3 - turn, alpha, beta);
                board[at] = 0;
                best = turn == 2 ? Math.Max(best, value) : Math.Min(best, value);
                if (turn == 2) alpha = Math.Max(alpha, best); else beta = Math.Min(beta, best);
                if (alpha >= beta) break;
            }
            return best;
        }

        private static int FourEstimate(int[] board)
        {
            int value = 0;
            int[] dx = { 1, 0, 1, -1 }, dy = { 0, 1, 1, 1 }, weights = { 0, 1, 8, 60, 10000 };
            for (int square = 0; square < 42; square++)
            {
                if (square % 7 == 3) value += board[square] == 2 ? 4 : board[square] == 1 ? -4 : 0;
                for (int way = 0; way < 4; way++)
                {
                    int x = square % 7 + 3 * dx[way], y = square / 7 + 3 * dy[way];
                    if (x < 0 || x >= 7 || y >= 6) continue;
                    int yours = 0, hers = 0;
                    for (int along = 0; along < 4; along++)
                    {
                        int cell = board[square + along * (dx[way] + 7 * dy[way])];
                        if (cell == 1) yours++;
                        if (cell == 2) hers++;
                    }
                    if (yours == 0) value += weights[hers];
                    if (hers == 0) value -= weights[yours];
                }
            }
            return value;
        }
    }

    internal sealed class MiniLine
    {
        internal string Board, Fallback, Said;
        internal int Result;
        internal double Began;
        internal bool Local;
    }

    internal sealed class MiniChat
    {
        private readonly Queue<MiniLine> waiting = new Queue<MiniLine>();
        private int quietMoves;
        internal double Last = -8;
        internal int Count { get { return waiting.Count; } }
        private static readonly string[] Moves = { "Oh, we're doing this?", "I've got my eye on that, choom.", "One of my minds has a plan.", "Your move. Make it interesting." };
        private static readonly string[] Wins = { "Mine. I am being so normal about this.", "That one's mine, choom. Rematch?" };
        private static readonly string[] Losses = { "You got me. I'm blaming the other minds.", "Okay, choom. That one hurt my ego." };
        private static readonly string[] Draws = { "Stalemate. We're both impossible.", "Nobody wins. My ego survives." };

        internal void Observe(MiniGame game, double now, Random dice)
        {
            KeepResults();
            quietMoves++;
            if (game.Result == 0 && (quietMoves < 2 || waiting.Count > 0 || now - Last < 8 || dice.Next(3) != 0)) return;
            string[] lines = game.Result == 1 ? Losses : game.Result == 2 ? Wins : game.Result == 3 ? Draws : Moves;
            waiting.Clear();
            waiting.Enqueue(new MiniLine { Board = game.BoardText(), Result = game.Result, Fallback = lines[dice.Next(lines.Length)] });
            quietMoves = 0;
        }

        internal void Observe(ChessGame game, Random dice)
        {
            string line = ChessLines.Pick(game.Moment, dice);
            if (line == null) return;
            waiting.Clear();
            waiting.Enqueue(new MiniLine { Board = game.BoardText(), Result = game.Result, Fallback = line, Said = line, Local = true });
        }

        internal MiniLine Take(double now) { return waiting.Count == 0 || now - Last < 8 ? null : waiting.Dequeue(); }
        internal void NewRound() { KeepResults(); quietMoves = 0; }
        internal void Clear() { waiting.Clear(); quietMoves = 0; }

        private void KeepResults()
        {
            MiniLine[] previous = waiting.ToArray();
            waiting.Clear();
            for (int old = previous.Length - 1; old >= 0; old--)
                if (previous[old].Result != 0) { waiting.Enqueue(previous[old]); break; }
        }
    }

    internal sealed class MiniBoard : Overlay
    {
        internal const int Wide = 372, Tall = 486;
        internal readonly MiniGame Game;
        private readonly System.Windows.Forms.Timer ticks = new System.Windows.Forms.Timer();
        private float scale = 1;
        private bool motion, placing;
        private int selected, held = -1, pulse;
        internal event Action Played;

        internal MiniBoard(MiniGame game) : base("Capri " + game.Title.ToLowerInvariant())
        {
            Game = game;
            AccessibleName = Text;
            KeyPreview = true;
            Cursor = Look.Pointer;
            ticks.Interval = 100;
            ticks.Tick += delegate { pulse = (pulse + 1) % 20; Redraw(); };
        }

        protected override bool ShowWithoutActivation { get { return false; } }

        protected override CreateParams CreateParams
        {
            get { CreateParams parameters = base.CreateParams; parameters.ExStyle &= ~0x08000000; return parameters; }
        }

        internal bool Motion
        {
            get { return motion; }
            set { if (motion == value) return; motion = value; pulse = 0; ticks.Enabled = motion && Visible; if (Visible) Redraw(); }
        }

        internal bool Ticking { get { return ticks.Enabled; } }
        internal float ScaleShown { get { return scale; } }

        internal void Open(Rectangle companion)
        {
            Rectangle area = Screen.FromRectangle(companion).WorkingArea;
            placing = true;
            Size = new Size(1, 1);
            Location = new Point(area.Left + area.Width / 2, area.Top + area.Height / 2);
            scale = BoardScale(Dpi(), area);
            int width = (int)Math.Round(Wide * scale), height = (int)Math.Round(Tall * scale);
            int left = companion.Right + 12;
            if (left + width > area.Right) left = companion.Left - width - 12;
            Location = new Point(Math.Max(area.Left, Math.Min(left, area.Right - width)), Math.Max(area.Top, Math.Min(companion.Bottom - height, area.Bottom - height)));
            Redraw();
            placing = false;
            Activate();
            ticks.Enabled = motion;
        }

        private float Dpi()
        {
            try { return Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { return 96; }
        }

        internal static float BoardScale(float dpi, Rectangle area) { return Math.Min(Math.Max(1, dpi / 96f), Math.Min((float)area.Width / Wide, (float)area.Height / Tall)); }

        private void Rescale(float dpi)
        {
            if (placing || !Visible || IsDisposed) return;
            placing = true;
            try
            {
                Rectangle area = Screen.FromHandle(Handle).WorkingArea;
                scale = BoardScale(dpi, area);
                Location = new Point(Math.Max(area.Left, Math.Min(Left, area.Right - (int)Math.Round(Wide * scale))), Math.Max(area.Top, Math.Min(Top, area.Bottom - (int)Math.Round(Tall * scale))));
                Redraw();
            }
            finally { placing = false; }
        }

        protected override void OnLocationChanged(EventArgs e)
        {
            base.OnLocationChanged(e);
            if (IsHandleCreated) Rescale(Dpi());
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x02E0) Rescale((int)(message.WParam.ToInt64() & 65535));
        }

        internal static RectangleF Part(string kind, int pick)
        {
            if (pick == 9) return new RectangleF(326, 14, 30, 30);
            if (pick == 10) return new RectangleF(24, 450, 170, 24);
            if (kind == "tic_tac_toe") return new RectangleF(48 + pick % 3 * 92, 138 + pick / 3 * 92, 92, 92);
            if (kind == "connect_four") return new RectangleF(25 + pick * 46, 138, 46, 276);
            return new RectangleF(24 + pick * 108, 171, 104, 130);
        }

        internal int PartAt(Point point)
        {
            PointF at = new PointF(point.X / scale, point.Y / scale);
            if (Part(Game.Kind, 9).Contains(at)) return 9;
            if (Part(Game.Kind, 10).Contains(at)) return 10;
            int count = Game.Kind == "tic_tac_toe" ? 9 : Game.Kind == "connect_four" ? 7 : 3;
            for (int part = 0; part < count; part++) if (Part(Game.Kind, part).Contains(at)) return part;
            return -1;
        }

        internal bool PlayMove(int move)
        {
            bool changed = Game.Move(move);
            Redraw();
            if (changed && Played != null) Played();
            return changed;
        }

        internal void Again() { Game.Again(); Redraw(); if (Played != null) Played(); }

        private void Press(int part)
        {
            if (part == 9) { Close(); return; }
            if (part == 10) { Again(); return; }
            if (part >= 0) { selected = part; PlayMove(part); }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Activate();
            held = PartAt(e.Location);
            Capture = held >= 0;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int pressed = held;
            held = -1;
            Capture = false;
            if (e.Button == MouseButtons.Left && pressed >= 0 && pressed == PartAt(e.Location)) Press(pressed);
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) held = -1; base.OnMouseCaptureChanged(e); }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hover = PartAt(e.Location);
            if (hover >= 0 && hover < 9 && hover != selected) { selected = hover; Redraw(); }
        }

        internal bool Key(Keys key)
        {
            if (key == Keys.Escape) { Close(); return true; }
            if (key == Keys.N) { Press(10); return true; }
            if (key == Keys.Enter || key == Keys.Space) { Press(selected); return true; }
            int count = Game.Kind == "tic_tac_toe" ? 9 : Game.Kind == "connect_four" ? 7 : 3;
            int row = Game.Kind == "tic_tac_toe" ? 3 : 1;
            if (key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down || key == Keys.Tab)
            {
                int step = key == Keys.Left ? -1 : key == Keys.Up ? -row : key == Keys.Down ? row : 1;
                selected = (selected + step + count) % count;
                Redraw();
                return true;
            }
            int number = key >= Keys.D1 && key <= Keys.D9 ? key - Keys.D1 : key >= Keys.NumPad1 && key <= Keys.NumPad9 ? key - Keys.NumPad1 : -1;
            if (Game.Kind == "rock_paper_scissors") number = key == Keys.R ? 0 : key == Keys.P ? 1 : key == Keys.S ? 2 : number;
            if (number < 0 || number >= count) return false;
            Press(number);
            return true;
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData) { return Key(keyData) || base.ProcessCmdKey(ref message, keyData); }

        internal void ClickAt(Point point)
        {
            OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
            OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0));
        }

        private void Redraw()
        {
            if (IsDisposed) return;
            Present(Render(Game, scale, selected, motion ? pulse : 0), Location);
        }

        internal static Bitmap Render(MiniGame game, float scale, int selected, int pulse)
        {
            Bitmap bitmap = new Bitmap((int)Math.Round(Wide * scale), (int)Math.Round(Tall * scale));
            using (Graphics paint = Graphics.FromImage(bitmap))
            using (Font title = Neon.Display(23, GraphicsUnit.Pixel))
            using (Font body = Neon.Type(16, false, GraphicsUnit.Pixel))
            using (Font small = Neon.Type(13, false, GraphicsUnit.Pixel))
            using (Font mark = Neon.Display(40, GraphicsUnit.Pixel))
            {
                paint.ScaleTransform(scale, scale);
                paint.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath clip = Neon.Cut(new RectangleF(0, 0, Wide - 1, Tall - 1), 20, 5, 20, 5)) paint.SetClip(clip);
                Neon.Backdrop(paint, new Size(Wide, Tall), 250);
                Neon.Frame(paint, new Size(Wide, Tall), 1, 20, 5, 55, 20);
                Words(paint, game.Title, title, Neon.Rose, new RectangleF(24, 22, 298, 32), false);
                Words(paint, "X", body, Neon.Rose, Part(game.Kind, 9), true);
                Words(paint, "SESSION  /  " + game.Score.Text, small, Neon.Cyan, new RectangleF(24, 72, 324, 22), false);
                string keys = game.Kind == "tic_tac_toe" ? "1-9 or arrows + Enter  /  say top left, middle..." : game.Kind == "connect_four" ? "1-7 or arrows + Enter  /  say column 3..." : "R / P / S or arrows + Enter  /  first to 3";
                Words(paint, keys, small, Neon.Faint, new RectangleF(24, 108, 324, 22), false);
                int count = game.Kind == "tic_tac_toe" ? 9 : game.Kind == "connect_four" ? 7 : 3;
                for (int pick = 0; pick < count; pick++)
                {
                    RectangleF tile = Part(game.Kind, pick);
                    tile.Inflate(-3, -3);
                    using (GraphicsPath shape = Neon.Cut(tile, 5, 2, 5, 2))
                    using (Brush fill = new SolidBrush(selected == pick ? Neon.Deep : Neon.Well))
                    using (Pen edge = new Pen(selected == pick ? Color.FromArgb(180 + Math.Abs(10 - pulse) * 7, Neon.Cyan) : Neon.WellEdge, selected == pick ? 2 : 1))
                    {
                        paint.FillPath(fill, shape);
                        paint.DrawPath(edge, shape);
                    }
                    if (game.Kind == "tic_tac_toe")
                    {
                        int cell = game.Cells[pick];
                        Words(paint, cell == 1 ? "X" : cell == 2 ? "O" : (pick + 1).ToString(), cell == 0 ? body : mark, cell == 1 ? Neon.Cyan : cell == 2 ? Neon.Rose : Neon.Faint, tile, true);
                    }
                    else if (game.Kind == "connect_four")
                    {
                        for (int row = 0; row < 6; row++)
                        {
                            int cell = game.Cells[row * 7 + pick];
                            RectangleF disc = new RectangleF(tile.X + 5, 146 + row * 44, 30, 30);
                            using (Brush ink = new SolidBrush(cell == 1 ? Neon.Cyan : cell == 2 ? Neon.Rose : Neon.Field)) paint.FillEllipse(ink, disc);
                            if (cell != 0) Words(paint, cell == 1 ? "X" : "O", small, Neon.OnInk, disc, true);
                            else if (row == 0) Words(paint, (pick + 1).ToString(), small, Neon.Faint, disc, true);
                        }
                    }
                    else
                    {
                        Words(paint, new[] { "R", "P", "S" }[pick], mark, Neon.Cyan, new RectangleF(tile.X, tile.Y + 15, tile.Width, 54), true);
                        Words(paint, new[] { "ROCK", "PAPER", "SCISSORS" }[pick], body, Neon.White, new RectangleF(tile.X, tile.Y + 75, tile.Width, 32), true);
                    }
                }
                if (game.Kind == "rock_paper_scissors") Words(paint, game.Last, body, Neon.White, new RectangleF(24, 326, 324, 74), false);
                Words(paint, game.Status, body, game.Result == 1 ? Neon.Cyan : game.Result == 2 ? Neon.Rose : Neon.White, new RectangleF(24, 421, 324, 25), false);
                Words(paint, "PLAY AGAIN  [N]", small, Neon.Cyan, Part(game.Kind, 10), false);
                Words(paint, "ESC  CLOSE", small, Neon.Faint, new RectangleF(246, 450, 104, 24), false);
            }
            return bitmap;
        }

        private static void Words(Graphics paint, string words, Font font, Color color, RectangleF box, bool centre)
        {
            using (Brush ink = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = centre ? StringAlignment.Center : StringAlignment.Near;
                format.LineAlignment = centre ? StringAlignment.Center : StringAlignment.Near;
                paint.DrawString(words, font, ink, box, format);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { ticks.Stop(); ticks.Dispose(); Played = null; }
            base.Dispose(disposing);
        }
    }

    internal sealed partial class Companion
    {
        private MiniBoard minis;
        private ChessBoard chess;
        private readonly Dictionary<string, MiniScore> miniScores = new Dictionary<string, MiniScore>();
        private readonly Random miniRandom = new Random();
        private readonly MiniChat miniChat = new MiniChat();
        private readonly System.Windows.Forms.Timer miniTicks = new System.Windows.Forms.Timer();
        private volatile string miniContext = "", miniKind = "";
        private MiniLine miniLine;
        private int miniTicket;
        private bool miniHeadBusy;

        private void SetUpMinis()
        {
            Hands.Mini = MiniTool;
            Hands.MiniInput = MiniInput;
            Hands.MiniBoard = delegate { return miniContext; };
            miniTicks.Interval = 200;
            miniTicks.Tick += delegate { MiniTick(); };
        }

        private ToolArguments MiniInput(string text)
        {
            string kind = miniKind;
            if (kind.Length == 0) return null;
            if (kind == "chess") return ChessGame.Input(text);
            string action = Regex.IsMatch(text, @"^(?:close|quit|exit|stop)(?: (?:the |our |your )?(?:board|mini ?game))?$", RegexOptions.IgnoreCase) ? "close"
                : Regex.IsMatch(text, @"^(?:again|play again|another round|new round|rematch)$", RegexOptions.IgnoreCase) ? "again"
                : MiniGame.Input(kind, text) >= 0 ? "move" : null;
            return action == null ? null : new ToolArguments { Name = kind, Action = action, Text = text };
        }

        private string MiniTool(ToolArguments args)
        {
            if (InvokeRequired) return (string)Invoke(new Func<ToolArguments, string>(MiniTool), args);
            if (!Hands.Valid("mini_game", args)) return "Couldn't find that desktop board.";
            if (args.Action == "close")
            {
                if (MidJob(job) && string.Equals((args.Text ?? "").Trim(), "stop", StringComparison.OrdinalIgnoreCase)) StopJob(Lines.Pick(Lines.Stopped));
                CloseMinis(args.Name == "chess");
                return "Board's away, choom.";
            }
            if (args.Name == "chess") return ChessTool(args);
            if (args.Action == "move" && (minis == null || minis.Game.Kind != args.Name)) return "That board isn't open.";
            if (minis == null || minis.Game.Kind != args.Name)
            {
                CloseMinis();
                MiniScore score;
                if (!miniScores.TryGetValue(args.Name, out score)) { score = new MiniScore(); miniScores.Add(args.Name, score); }
                MiniBoard opened = new MiniBoard(new MiniGame(args.Name, score, miniRandom));
                minis = opened;
                miniKind = args.Name;
                opened.Motion = preferences.Motion;
                opened.Above = preferences.OnTop;
                opened.Played += MiniPlayed;
                opened.FormClosed += delegate { if (minis == opened) { minis = null; ClearMini(); } };
                opened.Open(Bounds);
                miniTicks.Start();
            }
            if (args.Action == "move")
            {
                int move = MiniGame.Input(args.Name, args.Text);
                if (move < 0) return "Pick a square, column or hand on this board.";
                if (!minis.PlayMove(move)) return minis.Game.Last;
                return miniContext;
            }
            else if (args.Action == "again") minis.Again();
            MiniContext();
            return "Board's up, choom. " + minis.Game.Status;
        }

        private string ChessTool(ToolArguments args)
        {
            bool openedNow = chess == null;
            if (openedNow)
            {
                CloseMinis(true);
                MiniScore score;
                if (!miniScores.TryGetValue("chess", out score)) { score = new MiniScore(); miniScores.Add("chess", score); }
                ChessBoard opened = new ChessBoard(new ChessGame(score, args.Level, args.Side));
                chess = opened;
                miniKind = "chess";
                opened.Above = preferences.OnTop;
                opened.Played += ChessPlayed;
                opened.FormClosed += delegate { if (chess == opened) { chess = null; ClearMini(); } };
                opened.Open(Bounds);
                miniTicks.Start();
            }
            string action = args.Action ?? "open";
            if (action == "open" && !openedNow)
            {
                if (!string.IsNullOrEmpty(args.Side) && (args.Side == "white" ? 1 : -1) != chess.Game.You) action = "again";
                else if (!string.IsNullOrEmpty(args.Level)) action = "level";
            }
            if (action != "open" && !(openedNow && action == "again"))
            {
                if (action == "undo" || action == "again" || action == "level") { miniTicket++; miniLine = null; miniChat.Clear(); }
                chess.Command(action, args.Level, args.Side);
            }
            MiniContext();
            return action == "hint" ? "Looking for a hint on the board." : chess.Game.Status;
        }

        private void ChessPlayed()
        {
            MiniContext();
            if (chess == null) return;
            if (chess.Game.Moment == null) return;
            if (chess.Game.Moment == "reset")
            {
                miniTicket++; miniLine = null; miniChat.Clear(); chess.Game.Moment = null;
                return;
            }
            if (miniLine != null) { miniTicket++; miniLine = null; }
            int result = chess.Game.Result;
            if (result != 0)
            {
                Commentary.Stage.Mood(result == 2 ? GameMood.Happy : result == 1 ? GameMood.Hit : GameMood.Surprise);
                if (preferences.Motion) AnimateTool(result == 2 ? "grin" : "glitch");
            }
            miniChat.Observe(chess.Game, miniRandom);
            chess.Game.Moment = null;
        }

        private bool HearMini(string text)
        {
            string tool;
            ToolArguments args;
            if (!Hands.Heard(text, out tool, out args) || tool != "mini_game") return false;
            int ticket = ++asking;
            string done = DoTool(tool, args, ticket);
            if (args.Action != "move")
            {
                brain.Remember(text, done); heard = text; box.Prompt = "> you: ";
                if (args.Name == "chess") ChessSay(done, ticket); else Answer(ticket, done, TalkState.Talking);
            }
            return true;
        }

        private bool VoiceMini(string text)
        {
            string tool;
            ToolArguments args;
            if (!Hands.Heard(text, out tool, out args) || tool != "mini_game") return false;
            string done = DoTool(tool, args);
            lock (shortcutGate) { toolAsked = true; heardLast = text; toolDone = done; shortcutTool = tool; shortcutAt = Environment.TickCount; shortcutResult = done; }
            Hush();
            dropping = true;
            reply = null;
            replyDone = -1;
            talkState = TalkState.Off;
            Say(null);
            if (args.Action != "move")
            {
                brain.Remember(text, done);
                if (args.Name == "chess") ChessSay(done, asking); else Begin(asking, done, TalkState.Talking, null);
            }
            UpdateAnimation();
            return true;
        }

        private void MiniContext(bool localOnly = false)
        {
            miniContext = chess != null ? chess.Game.BoardText() : minis == null ? "" : minis.Game.BoardText();
            Agent told = agent;
            if (!localOnly && chess == null && told != null && told.Ready) told.Tell(miniContext.Length == 0 ? "Capri's desktop board is closed." : miniContext);
        }

        private void MiniPlayed()
        {
            MiniContext();
            if (minis == null) return;
            if (miniLine != null && miniLine.Result == 0) { miniTicket++; miniLine = null; }
            if (minis.Game.Last == "Your move, choom.") { miniChat.NewRound(); return; }
            int result = minis.Game.Result;
            if (result != 0)
            {
                Commentary.Stage.Mood(result == 2 ? GameMood.Happy : result == 1 ? GameMood.Hit : GameMood.Surprise);
                if (preferences.Motion) AnimateTool(result == 2 ? "grin" : "glitch");
            }
            miniChat.Observe(minis.Game, clock.Elapsed.TotalSeconds, miniRandom);
            if (miniChat.Count > 0 && miniLine != null) { miniTicket++; miniLine = null; }
        }

        private void MiniTick()
        {
            if (minis == null && chess == null) return;
            if (minis != null) minis.Motion = preferences.Motion;
            double now = clock.Elapsed.TotalSeconds;
            if (miniLine == null)
            {
                MiniLine next = miniChat.Take(now);
                if (next == null) return;
                miniLine = next;
                next.Began = now;
                int ticket = ++miniTicket;
                if (next.Local) { next.Said = next.Fallback; return; }
                if ((testing && headTest == null) || brain.Model == null || miniHeadBusy) { next.Said = next.Fallback; return; }
                miniHeadBusy = true;
                ThreadPool.QueueUserWorkItem(delegate
                {
                    string said = null;
                    try { said = Brain.Sparing(Brain.Clean(AskHead(Brain.Character + "\nYou're playing this little board game with them. React in one short line, under 14 words. Use the actual position and result; you're O, they're X. No labels, no invented moves.", next.Board, 100)), false, Brain.Nickname); }
                    catch (Exception) { }
                    Post(delegate { miniHeadBusy = false; if (ticket == miniTicket && miniLine == next && !string.IsNullOrWhiteSpace(said) && said != "SKIP") next.Said = said; });
                });
                return;
            }
            if (miniLine.Said == null && now - miniLine.Began < HeadWait / 1000.0) return;
            if (now - miniChat.Last < 8 || talkState == TalkState.Thinking || talkState == TalkState.Talking || voiceUntil > now) return;
            MiniLine spoken = miniLine;
            miniLine = null;
            miniTicket++;
            miniChat.Last = now;
            string line = spoken.Said ?? spoken.Fallback;
            brain.Remember("(Our desktop game: " + spoken.Board + ")", line);
            if (asideTest != null) { asideTest(line); return; }
            if (spoken.Local)
            {
                if (MidJob(job)) { asideShown = line; asideUntil = now + BubbleHold(line.Length); UpdateTalk(now); }
                else { if (hidden) SetHidden(false); heard = ""; box.Prompt = ""; remarkTicket = ++asking; ChessSay(line, remarkTicket); }
                return;
            }
            if (chatting && !local && agent != null && agent.Ready) { dropping = repeating = false; stirred = Environment.TickCount; agent.Say("(Say this word for word: " + Voice.Quote(line) + ")"); }
            else if (MidJob(job)) AsideSay(line, job, "(Our desktop game.)");
            else Remark("", line);
        }

        private void ChessSay(string line, int ticket)
        {
            Begin(ticket, line, TalkState.Talking, null);
            if (preferences.Silent || preferences.Quiet) return;
            Func<string, byte[]> speak = FreeSpeak ?? (testing || !kokoro.Installed ? null : new Func<string, byte[]>(kokoro.Speak));
            if (speak == null) return;
            int boardTicket = miniTicket;
            ThreadPool.QueueUserWorkItem(delegate
            {
                byte[] wav = null;
                try { wav = speak(Voice.Spoken(line)); } catch (Exception) { }
                Post(delegate { if (ticket == asking && boardTicket == miniTicket && reply == line && talkState != TalkState.Off) PlayReply(line, wav); });
            });
        }

        private void ClearMini(bool localOnly = false)
        {
            localOnly = localOnly || miniKind == "chess";
            miniKind = "";
            miniTicket++;
            miniLine = null;
            miniChat.Clear();
            miniTicks.Stop();
            MiniContext(localOnly);
        }

        private void CloseMinis(bool localOnly = false)
        {
            MiniBoard closing = minis;
            minis = null;
            if (closing != null) closing.Dispose();
            ChessBoard chessClosing = chess;
            chess = null;
            if (chessClosing != null) chessClosing.Dispose();
            ClearMini(localOnly);
        }
    }
}
