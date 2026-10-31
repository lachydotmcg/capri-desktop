using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace CapriDesktop
{
    internal sealed class ChessGame
    {
        internal ChessPosition Board;
        internal readonly MiniScore Score;
        internal int You = 1, Result;
        internal string Level = "normal", Last = "Your move.", Moment;
        internal ChessMove LastMove = new ChessMove(-1, -1, 0);
        private readonly List<ChessUndo> played = new List<ChessUndo>();
        private int? expected;

        internal ChessGame(MiniScore score, string level, string side)
        {
            Score = score;
            Again(level, side);
        }

        internal void Again(string level, string side)
        {
            if (!string.IsNullOrEmpty(level)) Level = level;
            if (!string.IsNullOrEmpty(side)) You = side == "black" ? -1 : 1;
            Board = new ChessPosition(ChessPosition.Start);
            played.Clear();
            Result = 0; expected = null; Moment = "reset";
            LastMove = new ChessMove(-1, -1, 0);
            Last = You == 1 ? "Your move." : "Capri has white.";
        }

        internal bool Move(ChessMove move, ChessChoice thinking)
        {
            if (Result != 0) return false;
            bool legal = false;
            foreach (ChessMove candidate in Board.Legal()) if (candidate.Same(move)) { legal = true; break; }
            if (!legal) return false;
            bool yours = Board.Turn == You;
            ChessUndo undo = Board.Push(move, true);
            played.Add(undo);
            LastMove = move;
            Moment = null;
            Last = (yours ? "You: " : "Capri: ") + ChessPosition.Square(move.From) + " - " + ChessPosition.Square(move.To) + (move.Promotion == 0 ? "" : " = " + " PNBRQK"[move.Promotion]);
            string end = Board.Ending();
            if (end != null)
            {
                Last = end;
                End(end == "Checkmate" ? yours ? 1 : 2 : 3);
                Moment = Result == 1 ? "lost" : Result == 2 ? "won" : "draw";
            }
            else if (Math.Abs(undo.Captured) == 5) Moment = yours ? "her_queen" : "your_queen";
            else if (Board.Check) Moment = yours ? "her_check" : "your_check";
            else if (!yours && thinking != null && expected.HasValue && thinking.Score - expected.Value >= 200 && Math.Abs(thinking.Score) < ChessSearch.Mate - 128) Moment = "blunder";
            if (!yours && thinking != null) expected = thinking.Score;
            return true;
        }

        private void End(int result)
        {
            Result = result;
            if (result == 1) Score.You++;
            else if (result == 2) Score.Her++;
            else Score.Draws++;
        }

        internal void Resign()
        {
            if (Result != 0) return;
            Last = "You resigned.";
            Moment = "resign";
            End(2);
        }

        internal void Undo()
        {
            if (Result == 1) Score.You--;
            else if (Result == 2) Score.Her--;
            else if (Result == 3) Score.Draws--;
            Result = 0; Moment = "reset"; expected = null;
            int target = played.Count - 1;
            while (target > 0 && played[target].Piece * You < 0) target--;
            while (played.Count > 0 && played.Count > target)
            {
                Board.Pop(played[played.Count - 1]);
                played.RemoveAt(played.Count - 1);
            }
            LastMove = played.Count == 0 ? new ChessMove(-1, -1, 0) : played[played.Count - 1].Move;
            Last = "Taken back.";
        }

        internal string Status
        {
            get
            {
                if (Result != 0) return Last + (Result == 1 ? " You win." : Result == 2 ? " Capri wins." : " Draw.");
                return (Board.Check ? "Check. " : "") + (Board.Turn == You ? "Your turn." : "Capri is thinking.");
            }
        }

        internal string BoardText()
        {
            return "\nCapri's desktop board: CHESS. User is " + (You == 1 ? "white" : "black") + ", Capri is " + (You == 1 ? "black" : "white") + ". Level: " + Level + ".\nPosition (FEN): " + Board.Fen() + "\n" + Status + " " + Last + "\nSession: " + Score.Text + ".\nMoves use the mouse. mini_game also takes undo, again, hint, resign and close.\n";
        }

        internal static ToolArguments Input(string words)
        {
            string text = (words ?? "").Trim().ToLowerInvariant().Replace('\u2019', '\'');
            string action = Regex.IsMatch(text, @"^(?:take that back|undo)(?: (?:that|move|my move|last move))?$") ? "undo"
                : Regex.IsMatch(text, @"^(?:new game|again|play again|another round|new round|rematch)$") ? "again"
                : Regex.IsMatch(text, @"^(?:i resign|resign)$") ? "resign"
                : Regex.IsMatch(text, @"^(?:hint|give me a hint)$") ? "hint"
                : Regex.IsMatch(text, @"^(?:close|quit|exit|stop)(?: (?:the |our |your )?(?:board|mini ?game|chess))?$") ? "close" : null;
            Match side = Regex.Match(text, @"^(?:i'll|i will|let me) (?:be|play)(?: as)? (white|black)$");
            Match strength = Regex.Match(text, @"^(?:(?:play|set|switch)(?: chess)?(?: (?:on|to))? )?(easy|normal|hard)$");
            if (side.Success) return new ToolArguments { Name = "chess", Action = "again", Side = side.Groups[1].Value };
            if (strength.Success) return new ToolArguments { Name = "chess", Action = "level", Level = strength.Groups[1].Value };
            return action == null ? null : new ToolArguments { Name = "chess", Action = action, Text = words };
        }
    }

    internal static class ChessLines
    {
        internal static readonly string[][] Lines =
        {
            new[] { "Your queen just joined my payroll.", "Royalty has terrible survival instincts.", "That's one expensive parking spot.", "Queen collected. No receipt.", "Night City takes its cut. So do I." },
            new[] { "That was my favourite problem piece.", "My queen has left the group chat.", "You just made this personal. Mildly.", "Fine. I'll delegate.", "A hostile takeover. Cute." },
            new[] { "Check. Your king has a visitor.", "Your king's address just leaked.", "Tiny crown. Large problem.", "Check. Relocation might be wise.", "Your security detail missed something." },
            new[] { "Check. Yes, I noticed.", "My king enjoys unnecessary cardio.", "That's inconveniently competent.", "You found the executive suite.", "Fine. Moving management." },
            new[] { "Mate. I'll try to be unbearable quietly.", "Your king's out of exits.", "Board's mine. Ego also expanding.", "Mate. No chrome required.", "Good game, choom. Same time, same ego?" },
            new[] { "Mate. You earned that smug face.", "You got me. I'm keeping the board warm.", "My other minds would like a recount.", "That was clean. Annoyingly clean.", "King down. Dignity buffering." },
            new[] { "That move came with an invoice.", "You left something expensive unattended.", "I think your plan has a loose wire.", "That looked better before the consequences.", "I found a hole in your security." },
            new[] { "Draw. Two egos, no receipt.", "Nobody gets the crown today.", "We're calling that mutual inconvenience.", "All that plotting. Shared custody of nothing." },
            new[] { "Resignation accepted. Board's still warm.", "Fair. We can rerack whenever.", "I'll take the point without a speech.", "King clocks off. Understandable." }
        };
        private static readonly string[] Moments = { "your_queen", "her_queen", "your_check", "her_check", "won", "lost", "blunder", "draw", "resign" };

        internal static string Pick(string moment, Random dice)
        {
            int at = Array.IndexOf(Moments, moment);
            return at < 0 ? null : Lines[at][dice.Next(Lines[at].Length)];
        }
    }

    internal sealed class ChessBoard : Overlay
    {
        internal const int Wide = 480, Tall = 634;
        internal readonly ChessGame Game;
        internal event Action Played;
        private readonly Timer ticks = new Timer();
        private readonly Random dice = new Random();
        private ChessWork work;
        private bool hintWork, hintAfter, placing, dragging;
        private int selected = -1, held = -1, promotionFrom = -1, promotionTo = -1;
        private float scale = 1;
        private Point down;
        private ChessMove hint = new ChessMove(-1, -1, 0);
        private List<ChessMove> legal;
        private string note = "";
        internal bool Busy { get { return work != null; } }
        internal ChessWork Pending { get { return work; } }
        internal bool Ticking { get { return ticks.Enabled; } }
        internal float ScaleShown { get { return scale; } }

        internal ChessBoard(ChessGame game) : base("Capri chess")
        {
            Game = game;
            AccessibleName = Text;
            Cursor = Look.Pointer;
            legal = Game.Board.Legal();
            ticks.Interval = 40;
            ticks.Tick += delegate { FinishWork(); };
        }

        protected override bool ShowWithoutActivation { get { return false; } }
        protected override CreateParams CreateParams { get { CreateParams parameters = base.CreateParams; parameters.ExStyle &= ~0x08000000; return parameters; } }

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
            ticks.Start();
            Think(false);
        }

        internal static float BoardScale(float dpi, Rectangle area) { return Math.Min(Math.Max(1, dpi / 96f), Math.Min((float)area.Width / Wide, (float)area.Height / Tall)); }
        private float Dpi() { try { return Native.GetDpiForWindow(Handle); } catch (EntryPointNotFoundException) { return 96; } }

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

        protected override void OnLocationChanged(EventArgs e) { base.OnLocationChanged(e); if (IsHandleCreated) Rescale(Dpi()); }
        protected override void WndProc(ref Message message) { base.WndProc(ref message); if (message.Msg == 0x02E0) Rescale((int)(message.WParam.ToInt64() & 65535)); }

        internal static RectangleF Tile(int square, int you)
        {
            int file = square & 7, rank = square / 16;
            return new RectangleF(32 + (you == 1 ? file : 7 - file) * 52, 132 + (you == 1 ? 7 - rank : rank) * 52, 52, 52);
        }

        internal static RectangleF Button(int part)
        {
            if (part == 128) return new RectangleF(432, 14, 30, 30);
            if (part >= 133 && part <= 135) return new RectangleF(234 + (part - 133) * 76, 97, 70, 25);
            if (part >= 140 && part <= 143) return new RectangleF(112 + (part - 140) * 64, 307, 60, 66);
            return new RectangleF(24 + (part - 129) * 110, 588, 102, 28);
        }

        internal int PartAt(Point point)
        {
            PointF at = new PointF(point.X / scale, point.Y / scale);
            if (Button(128).Contains(at)) return 128;
            for (int button = 129; button <= 135; button++) if (Button(button).Contains(at)) return button;
            if (promotionFrom >= 0)
            {
                for (int pick = 140; pick <= 143; pick++) if (Button(pick).Contains(at)) return pick;
                return -1;
            }
            for (int square = 0; square < 128; square++) if (ChessPosition.On(square) && Tile(square, Game.You).Contains(at)) return square;
            return -1;
        }

        internal void Command(string action, string level, string side)
        {
            if (action == "close") { Close(); return; }
            if (action == "hint")
            {
                if (Game.Result != 0) { note = Game.Status; Redraw(); return; }
                if (Game.Board.Turn != Game.You) { hintAfter = true; note = "Hint after Capri's move."; Redraw(); return; }
                Cancel();
                Think(true);
                return;
            }
            Cancel();
            if (action == "undo") Game.Undo();
            else if (action == "resign") Game.Resign();
            else if (action == "level") { if (!string.IsNullOrEmpty(level)) Game.Level = level; Game.Moment = "reset"; }
            else if (action == "again") Game.Again(level, side);
            selected = promotionFrom = promotionTo = -1;
            Capture = false;
            hint = new ChessMove(-1, -1, 0);
            note = "";
            Changed();
            Think(false);
        }

        private void Cancel()
        {
            if (work != null) work.Cancelled = true;
            work = null;
            hintAfter = false;
        }

        private void Think(bool help)
        {
            if (work != null || Game.Result != 0 || !help && Game.Board.Turn == Game.You) return;
            hintWork = help;
            work = ChessWork.Start(Game.Board, help ? "hard" : Game.Level, dice.Next());
            if (help) note = "Looking for a hint...";
            Redraw();
        }

        private void FinishWork()
        {
            if (work == null || !work.Done) return;
            ChessWork finished = work;
            bool helping = hintWork, nextHint = hintAfter;
            work = null; hintAfter = false;
            if (finished.Cancelled) return;
            if (finished.Error != null || finished.Choice == null || finished.Choice.Move.From < 0)
            {
                note = "Couldn't finish that search. Try a level or new game.";
                Redraw();
                return;
            }
            if (helping)
            {
                hint = finished.Choice.Move;
                selected = hint.From;
                note = "Try " + ChessPosition.Square(hint.From) + " - " + ChessPosition.Square(hint.To) + (hint.Promotion == 0 ? "." : " = " + " PNBRQK"[hint.Promotion] + ".");
                Redraw();
            }
            else
            {
                note = "";
                Game.Move(finished.Choice.Move, finished.Choice);
                Changed();
                if (nextHint) Think(true);
            }
        }

        private void Changed()
        {
            legal = Game.Board.Legal();
            Redraw();
            if (Played != null) Played();
        }

        private void Play(int from, int to, int promotion)
        {
            if (Game.Result != 0 || Game.Board.Turn != Game.You) return;
            foreach (ChessMove move in legal)
            {
                if (move.From != from || move.To != to) continue;
                if (move.Promotion != 0 && promotion == 0)
                {
                    Cancel(); promotionFrom = from; promotionTo = to;
                    Redraw(); return;
                }
                if (move.Promotion != promotion) continue;
                Cancel();
                if (!Game.Move(move, null)) return;
                selected = promotionFrom = promotionTo = -1;
                hint = new ChessMove(-1, -1, 0); note = "";
                Changed();
                Think(false);
                return;
            }
        }

        private void Press(int part)
        {
            if (part == 128) { Close(); return; }
            if (part >= 129 && part <= 132) { Command(new[] { "undo", "again", "hint", "resign" }[part - 129], null, null); return; }
            if (part >= 133 && part <= 135) { Command("level", new[] { "easy", "normal", "hard" }[part - 133], null); return; }
            if (part >= 140 && part <= 143 && promotionFrom >= 0) { Play(promotionFrom, promotionTo, new[] { 5, 4, 3, 2 }[part - 140]); return; }
            if (part < 0 || part >= 128 || Game.Result != 0 || Game.Board.Turn != Game.You) return;
            if (Game.Board.Cells[part] * Game.You > 0) { selected = part; Redraw(); }
            else if (selected >= 0) Play(selected, part, 0);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            Activate(); down = e.Location; dragging = false;
            held = PartAt(e.Location);
            Capture = held >= 0;
            if (held >= 0 && held < 128 && Game.Board.Cells[held] * Game.You > 0 && Game.Board.Turn == Game.You) { selected = held; Redraw(); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (Capture && (Math.Abs(e.X - down.X) > 4 || Math.Abs(e.Y - down.Y) > 4)) dragging = true;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            int pressed = held, released = PartAt(e.Location);
            bool moved = dragging;
            held = -1; Capture = false;
            if (e.Button != MouseButtons.Left || pressed < 0) return;
            if (pressed < 128 && released >= 0 && released < 128 && (moved || pressed != released)) Play(pressed, released, 0);
            else if (pressed == released) Press(pressed);
        }

        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) { held = -1; dragging = false; } base.OnMouseCaptureChanged(e); }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData) { if (keyData == Keys.Escape) { Close(); return true; } return base.ProcessCmdKey(ref message, keyData); }
        internal void ClickAt(Point point) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0)); }
        internal void DragTo(Point from, Point to) { OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, from.X, from.Y, 0)); OnMouseMove(new MouseEventArgs(MouseButtons.Left, 0, to.X, to.Y, 0)); OnMouseUp(new MouseEventArgs(MouseButtons.Left, 1, to.X, to.Y, 0)); }

        private void Redraw()
        {
            if (!IsDisposed) Present(Render(Game, scale, selected, legal, hint, promotionFrom >= 0, note), Location);
        }

        internal static Bitmap Render(ChessGame game, float scale, int selected, List<ChessMove> legal, ChessMove hint, bool promotion, string note)
        {
            Bitmap bitmap = new Bitmap((int)Math.Round(Wide * scale), (int)Math.Round(Tall * scale));
            using (Graphics paint = Graphics.FromImage(bitmap))
            using (Font title = Neon.Display(23, GraphicsUnit.Pixel))
            using (Font body = Neon.Type(16, false, GraphicsUnit.Pixel))
            using (Font small = Neon.Type(13, false, GraphicsUnit.Pixel))
            {
                paint.ScaleTransform(scale, scale);
                paint.SmoothingMode = SmoothingMode.AntiAlias;
                using (GraphicsPath clip = Neon.Cut(new RectangleF(0, 0, Wide - 1, Tall - 1), 20, 5, 20, 5)) paint.SetClip(clip);
                Neon.Backdrop(paint, new Size(Wide, Tall), 250);
                Neon.Frame(paint, new Size(Wide, Tall), 1, 20, 5, 55, 20);
                Words(paint, "CHESS", title, Neon.Rose, new RectangleF(24, 22, 300, 32), false);
                Words(paint, "X", body, Neon.Rose, Button(128), true);
                Words(paint, "SESSION  /  " + game.Score.Text, small, Neon.Cyan, new RectangleF(24, 65, 430, 24), false);
                Words(paint, "YOU  /  " + (game.You == 1 ? "WHITE" : "BLACK"), small, Neon.White, new RectangleF(24, 101, 180, 24), false);
                for (int level = 0; level < 3; level++)
                {
                    string name = new[] { "easy", "normal", "hard" }[level];
                    Panel(paint, Button(133 + level), name == game.Level);
                    Words(paint, name.ToUpperInvariant(), small, name == game.Level ? Neon.Cyan : Neon.Faint, Button(133 + level), true);
                }
                int checkedKing = game.Board.Check ? game.Board.Turn == 1 ? game.Board.WhiteKing : game.Board.BlackKing : -1;
                for (int square = 0; square < 128; square++)
                {
                    if (!ChessPosition.On(square)) { square += 7; continue; }
                    RectangleF tile = Tile(square, game.You);
                    using (Brush fill = new SolidBrush(((square + square / 16) & 1) == 0 ? Neon.Well : Color.FromArgb(43, 38, 51))) paint.FillRectangle(fill, tile);
                    if (game.LastMove.From == square || game.LastMove.To == square)
                        using (Brush last = new SolidBrush(Color.FromArgb(42, Neon.Cyan))) paint.FillRectangle(last, tile);
                    if (checkedKing == square)
                        using (Brush danger = new SolidBrush(Color.FromArgb(110, Neon.Hot))) paint.FillRectangle(danger, tile);
                    if (selected == square || hint.To == square)
                        using (Pen edge = new Pen(Neon.Cyan, 2)) paint.DrawRectangle(edge, tile.X + 2, tile.Y + 2, tile.Width - 4, tile.Height - 4);
                    int piece = game.Board.Cells[square];
                    if (piece != 0) Piece(paint, piece, tile);
                    bool target = false;
                    if (game.Result == 0 && game.Board.Turn == game.You)
                        foreach (ChessMove move in legal) if (move.From == selected && move.To == square) { target = true; break; }
                    if (target)
                    {
                        if (piece == 0) using (Brush dot = new SolidBrush(Color.FromArgb(180, Neon.Cyan))) paint.FillEllipse(dot, tile.X + 22, tile.Y + 22, 8, 8);
                        else using (Pen ring = new Pen(Neon.Cyan, 2)) paint.DrawEllipse(ring, tile.X + 4, tile.Y + 4, 44, 44);
                    }
                }
                for (int coordinate = 0; coordinate < 8; coordinate++)
                {
                    Words(paint, "abcdefgh"[game.You == 1 ? coordinate : 7 - coordinate].ToString(), small, Neon.Faint, new RectangleF(32 + coordinate * 52, 548, 52, 16), true);
                    Words(paint, (game.You == 1 ? 8 - coordinate : coordinate + 1).ToString(), small, Neon.Faint, new RectangleF(9, 132 + coordinate * 52, 20, 52), true);
                }
                Words(paint, string.IsNullOrEmpty(note) ? game.Status : note, body, game.Result == 2 ? Neon.Rose : Neon.White, new RectangleF(24, 563, 434, 24), false);
                string[] buttons = { "UNDO", "NEW GAME", "HINT", "RESIGN" };
                for (int action = 0; action < buttons.Length; action++)
                {
                    Panel(paint, Button(129 + action), false);
                    Words(paint, buttons[action], small, action == 3 ? Neon.Rose : Neon.Cyan, Button(129 + action), true);
                }
                if (promotion)
                {
                    using (Brush veil = new SolidBrush(Color.FromArgb(215, Neon.Ground))) paint.FillRectangle(veil, 32, 132, 416, 416);
                    Words(paint, "PROMOTE TO", body, Neon.White, new RectangleF(80, 266, 320, 32), true);
                    int[] kinds = { 5, 4, 3, 2 };
                    string[] names = { "QUEEN", "ROOK", "BISHOP", "KNIGHT" };
                    for (int pick = 0; pick < kinds.Length; pick++)
                    {
                        RectangleF box = Button(140 + pick);
                        Panel(paint, box, false);
                        Piece(paint, kinds[pick] * game.You, new RectangleF(box.X + 4, box.Y, 52, 52));
                        Words(paint, names[pick], small, Neon.Cyan, new RectangleF(box.X, box.Y + 48, box.Width, 18), true);
                    }
                }
            }
            return bitmap;
        }

        private static void Panel(Graphics paint, RectangleF box, bool selected)
        {
            using (GraphicsPath shape = Neon.Cut(box, 5, 2, 5, 2))
            using (Brush fill = new SolidBrush(selected ? Neon.Deep : Neon.Well))
            using (Pen edge = new Pen(selected ? Neon.Cyan : Neon.WellEdge)) { paint.FillPath(fill, shape); paint.DrawPath(edge, shape); }
        }

        internal static void Piece(Graphics paint, int piece, RectangleF box)
        {
            GraphicsState state = paint.Save();
            paint.TranslateTransform(box.X, box.Y);
            paint.ScaleTransform(box.Width / 52, box.Height / 52);
            using (Brush fill = new SolidBrush(piece > 0 ? Neon.White : Neon.Rose))
            using (Pen edge = new Pen(piece > 0 ? Neon.OnInk : Neon.Well, 1.5f))
            using (GraphicsPath shape = new GraphicsPath(FillMode.Winding))
            {
                int kind = Math.Abs(piece);
                shape.AddPolygon(new[] { new PointF(13, 43), new PointF(16, 37), new PointF(21, 34), new PointF(22, 24), new PointF(30, 24), new PointF(31, 34), new PointF(36, 37), new PointF(39, 43) });
                if (kind == 1) shape.AddEllipse(19, 12, 14, 14);
                if (kind == 2) shape.AddPolygon(new[] { new PointF(18, 32), new PointF(20, 24), new PointF(14, 25), new PointF(12, 21), new PointF(21, 12), new PointF(23, 7), new PointF(30, 12), new PointF(35, 22), new PointF(33, 34) });
                if (kind == 3) shape.AddPolygon(new[] { new PointF(26, 7), new PointF(35, 19), new PointF(32, 27), new PointF(20, 27), new PointF(17, 19) });
                if (kind == 4) shape.AddPolygon(new[] { new PointF(15, 11), new PointF(21, 11), new PointF(21, 16), new PointF(24, 16), new PointF(24, 11), new PointF(29, 11), new PointF(29, 16), new PointF(32, 16), new PointF(32, 11), new PointF(37, 11), new PointF(35, 25), new PointF(17, 25) });
                if (kind == 5) shape.AddPolygon(new[] { new PointF(14, 13), new PointF(21, 19), new PointF(26, 9), new PointF(31, 19), new PointF(38, 13), new PointF(33, 28), new PointF(19, 28) });
                if (kind == 6)
                {
                    shape.Reset();
                    shape.AddPolygon(new[] { new PointF(13, 43), new PointF(16, 37), new PointF(21, 34), new PointF(22, 28), new PointF(18, 24), new PointF(18, 19), new PointF(23, 19), new PointF(23, 15), new PointF(18, 15), new PointF(18, 10), new PointF(23, 10), new PointF(23, 6), new PointF(29, 6), new PointF(29, 10), new PointF(34, 10), new PointF(34, 15), new PointF(29, 15), new PointF(29, 19), new PointF(34, 19), new PointF(34, 24), new PointF(30, 28), new PointF(31, 34), new PointF(36, 37), new PointF(39, 43) });
                }
                paint.FillPath(fill, shape);
                paint.DrawPath(edge, shape);
                if (kind == 3) paint.DrawLine(edge, 28, 11, 23, 19);
                if (kind == 2) using (Brush eye = new SolidBrush(Neon.Well)) paint.FillEllipse(eye, 23, 16, 3, 3);
                paint.DrawLine(edge, 17, 38, 35, 38);
            }
            paint.Restore(state);
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
            if (disposing) { Cancel(); ticks.Stop(); ticks.Dispose(); Played = null; }
            base.Dispose(disposing);
        }
    }
}
