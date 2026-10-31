using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace CapriDesktop
{
    internal struct ChessMove
    {
        internal int From, To, Promotion;
        internal ChessMove(int from, int to, int promotion) { From = from; To = to; Promotion = promotion; }
        internal bool Same(ChessMove other) { return From == other.From && To == other.To && Promotion == other.Promotion; }
        internal string Text { get { return ChessPosition.Square(From) + ChessPosition.Square(To) + (Promotion == 0 ? "" : " pnbrqk"[Promotion].ToString()); } }
    }

    internal struct ChessUndo
    {
        internal ChessMove Move;
        internal int Piece, Captured, CaptureAt, Rights, Ep, Half, Full, WhiteKing, BlackKing, History;
    }

    internal sealed class ChessPosition
    {
        internal const string Start = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";
        internal readonly int[] Cells = new int[128];
        internal int Turn = 1, Rights, Ep = -1, Half, Full = 1, WhiteKing, BlackKing;
        internal readonly List<ulong> History = new List<ulong>();
        internal static readonly int[] Knights = { 33, 31, 18, 14, -14, -18, -31, -33 };
        internal static readonly int[] Diagonals = { 17, 15, -15, -17 };
        internal static readonly int[] Straights = { 16, 1, -1, -16 };
        internal static readonly int[] Kings = { 17, 16, 15, 1, -1, -15, -16, -17 };
        private static readonly ulong[,] Pieces = PieceKeys();
        private static readonly ulong[] States = StateKeys();

        internal ChessPosition(string fen)
        {
            string[] fields = fen.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            string[] rows = fields[0].Split('/');
            if (rows.Length != 8 || fields.Length < 4) throw new ArgumentException("Invalid chess position.");
            for (int row = 0; row < 8; row++)
            {
                int file = 0;
                foreach (char symbol in rows[row])
                {
                    if (symbol >= '1' && symbol <= '8') { file += symbol - '0'; continue; }
                    int kind = " pnbrqk".IndexOf(char.ToLowerInvariant(symbol));
                    if (kind < 1 || file > 7) throw new ArgumentException("Invalid chess piece.");
                    int at = (7 - row) * 16 + file++;
                    Cells[at] = char.IsUpper(symbol) ? kind : -kind;
                    if (Cells[at] == 6) WhiteKing = at;
                    if (Cells[at] == -6) BlackKing = at;
                }
                if (file != 8) throw new ArgumentException("Invalid chess rank.");
            }
            Turn = fields[1] == "w" ? 1 : -1;
            Rights = (fields[2].Contains("K") ? 1 : 0) | (fields[2].Contains("Q") ? 2 : 0) | (fields[2].Contains("k") ? 4 : 0) | (fields[2].Contains("q") ? 8 : 0);
            Ep = At(fields[3]);
            if (fields.Length > 4) Half = int.Parse(fields[4], System.Globalization.CultureInfo.InvariantCulture);
            if (fields.Length > 5) Full = int.Parse(fields[5], System.Globalization.CultureInfo.InvariantCulture);
            History.Add(Key());
        }

        private ChessPosition() { }

        internal ChessPosition Copy()
        {
            ChessPosition copy = new ChessPosition();
            Array.Copy(Cells, copy.Cells, Cells.Length);
            copy.Turn = Turn; copy.Rights = Rights; copy.Ep = Ep; copy.Half = Half; copy.Full = Full;
            copy.WhiteKing = WhiteKing; copy.BlackKing = BlackKing;
            copy.History.AddRange(History);
            return copy;
        }

        internal static bool On(int at) { return (at & 0x88) == 0; }
        internal static int At(string square) { return square != null && square.Length == 2 && square[0] >= 'a' && square[0] <= 'h' && square[1] >= '1' && square[1] <= '8' ? (square[1] - '1') * 16 + square[0] - 'a' : -1; }
        internal static string Square(int at) { return On(at) ? "abcdefgh"[at & 7].ToString() + (at / 16 + 1) : "-"; }
        internal bool Check { get { return Attacked(Turn == 1 ? WhiteKing : BlackKing, -Turn); } }

        internal bool Attacked(int target, int side)
        {
            int pawnLeft = target - side * 16 - 1, pawnRight = target - side * 16 + 1;
            if (On(pawnLeft) && Cells[pawnLeft] == side || On(pawnRight) && Cells[pawnRight] == side) return true;
            foreach (int jump in Knights) if (On(target + jump) && Cells[target + jump] == side * 2) return true;
            foreach (int ray in Kings)
            {
                int distance = 1;
                for (int scan = target + ray; On(scan); scan += ray, distance++)
                {
                    int piece = Cells[scan];
                    if (piece == 0) continue;
                    if (piece * side > 0)
                    {
                        int kind = Math.Abs(piece);
                        bool straight = ray == 16 || ray == -16 || ray == 1 || ray == -1;
                        if (kind == 5 || kind == (straight ? 4 : 3) || kind == 6 && distance == 1) return true;
                    }
                    break;
                }
            }
            return false;
        }

        internal List<ChessMove> Legal()
        {
            List<ChessMove> candidates = new List<ChessMove>(48), legal = new List<ChessMove>(48);
            for (int from = 0; from < 128; from++)
            {
                if (!On(from)) { from += 7; continue; }
                int piece = Cells[from] * Turn;
                if (piece <= 0) continue;
                if (piece == 1)
                {
                    int forward = from + Turn * 16;
                    if (On(forward) && Cells[forward] == 0)
                    {
                        PawnMove(candidates, from, forward);
                        int twice = forward + Turn * 16;
                        if (from / 16 == (Turn == 1 ? 1 : 6) && Cells[twice] == 0) candidates.Add(new ChessMove(from, twice, 0));
                    }
                    for (int flank = -1; flank <= 1; flank += 2)
                    {
                        int to = forward + flank;
                        if (On(to) && (Cells[to] * Turn < 0 && Math.Abs(Cells[to]) != 6 || to == Ep && Cells[to] == 0 && Cells[to - Turn * 16] == -Turn)) PawnMove(candidates, from, to);
                    }
                }
                else
                {
                    int[] steps = piece == 2 ? Knights : piece == 3 ? Diagonals : piece == 4 ? Straights : Kings;
                    foreach (int step in steps)
                        for (int destination = from + step; On(destination); destination += step)
                        {
                            int taken = Cells[destination] * Turn;
                            if (taken > 0 || taken == -6) break;
                            candidates.Add(new ChessMove(from, destination, 0));
                            if (taken < 0 || piece == 2 || piece == 6) break;
                        }
                    if (piece == 6) Castles(candidates, from);
                }
            }
            foreach (ChessMove candidate in candidates)
            {
                ChessUndo undo = Push(candidate, false);
                if (!Attacked(Turn == -1 ? WhiteKing : BlackKing, Turn)) legal.Add(candidate);
                Pop(undo);
            }
            return legal;
        }

        private static void PawnMove(List<ChessMove> moves, int from, int to)
        {
            if (to / 16 == 0 || to / 16 == 7)
                for (int promote = 5; promote >= 2; promote--) moves.Add(new ChessMove(from, to, promote));
            else moves.Add(new ChessMove(from, to, 0));
        }

        private void Castles(List<ChessMove> moves, int king)
        {
            int home = Turn == 1 ? 0 : 112, kingRight = Turn == 1 ? 1 : 4, queenRight = kingRight * 2;
            if (king != home + 4 || Attacked(king, -Turn)) return;
            if ((Rights & kingRight) != 0 && Cells[home + 7] == Turn * 4 && Cells[home + 5] == 0 && Cells[home + 6] == 0 && !Attacked(home + 5, -Turn) && !Attacked(home + 6, -Turn)) moves.Add(new ChessMove(king, home + 6, 0));
            if ((Rights & queenRight) != 0 && Cells[home] == Turn * 4 && Cells[home + 1] == 0 && Cells[home + 2] == 0 && Cells[home + 3] == 0 && !Attacked(home + 3, -Turn) && !Attacked(home + 2, -Turn)) moves.Add(new ChessMove(king, home + 2, 0));
        }

        internal ChessUndo Push(ChessMove move, bool remember)
        {
            int piece = Cells[move.From];
            int captureAt = Math.Abs(piece) == 1 && move.To == Ep && Cells[move.To] == 0 ? move.To - Turn * 16 : move.To;
            ChessUndo undo = new ChessUndo { Move = move, Piece = piece, Captured = Cells[captureAt], CaptureAt = captureAt, Rights = Rights, Ep = Ep, Half = Half, Full = Full, WhiteKing = WhiteKing, BlackKing = BlackKing, History = History.Count };
            Cells[move.From] = 0;
            Cells[captureAt] = 0;
            Cells[move.To] = move.Promotion == 0 ? piece : Turn * move.Promotion;
            if (Math.Abs(piece) == 6)
            {
                if (Turn == 1) { WhiteKing = move.To; Rights &= ~3; } else { BlackKing = move.To; Rights &= ~12; }
                if (Math.Abs(move.To - move.From) == 2)
                {
                    int rookFrom = move.To > move.From ? move.From + 3 : move.From - 4;
                    int rookTo = (move.From + move.To) / 2;
                    Cells[rookTo] = Cells[rookFrom]; Cells[rookFrom] = 0;
                }
            }
            Rights &= ~RookRight(move.From);
            Rights &= ~RookRight(move.To);
            Ep = Math.Abs(piece) == 1 && Math.Abs(move.To - move.From) == 32 ? (move.From + move.To) / 2 : -1;
            Half = Math.Abs(piece) == 1 || undo.Captured != 0 ? 0 : Half + 1;
            if (Turn == -1) Full++;
            Turn = -Turn;
            if (remember) History.Add(Key());
            return undo;
        }

        internal void Pop(ChessUndo undo)
        {
            ChessMove move = undo.Move;
            Turn = -Turn;
            if (Math.Abs(undo.Piece) == 6 && Math.Abs(move.To - move.From) == 2)
            {
                int rookTo = move.To > move.From ? move.From + 3 : move.From - 4;
                int rookFrom = (move.From + move.To) / 2;
                Cells[rookTo] = Cells[rookFrom]; Cells[rookFrom] = 0;
            }
            Cells[move.From] = undo.Piece;
            Cells[move.To] = 0;
            Cells[undo.CaptureAt] = undo.Captured;
            Rights = undo.Rights; Ep = undo.Ep; Half = undo.Half; Full = undo.Full;
            WhiteKing = undo.WhiteKing; BlackKing = undo.BlackKing;
            if (History.Count > undo.History) History.RemoveRange(undo.History, History.Count - undo.History);
        }

        private static int RookRight(int square) { return square == 0 ? 2 : square == 7 ? 1 : square == 112 ? 8 : square == 119 ? 4 : 0; }

        internal ChessMove Find(string text)
        {
            foreach (ChessMove move in Legal()) if (move.Text == text) return move;
            return new ChessMove(-1, -1, 0);
        }

        internal bool Play(string text)
        {
            ChessMove move = Find(text);
            if (move.From < 0) return false;
            Push(move, true);
            return true;
        }

        internal int Repetitions
        {
            get
            {
                if (History.Count == 0) return 0;
                ulong current = History[History.Count - 1];
                int count = 0;
                for (int past = History.Count - 1; past >= Math.Max(0, History.Count - Half - 1); past -= 2) if (History[past] == current) count++;
                return count;
            }
        }

        internal bool Insufficient
        {
            get
            {
                int minors = 0, knights = 0, bishopColor = -1;
                bool mixed = false;
                for (int square = 0; square < 128; square++)
                {
                    if (!On(square)) { square += 7; continue; }
                    int kind = Math.Abs(Cells[square]);
                    if (kind == 0 || kind == 6) continue;
                    if (kind == 1 || kind == 4 || kind == 5) return false;
                    minors++;
                    if (kind == 2) knights++;
                    else
                    {
                        int color = (square + square / 16) & 1;
                        if (bishopColor != -1 && bishopColor != color) mixed = true;
                        bishopColor = color;
                    }
                }
                return minors <= 1 || knights == 0 && !mixed;
            }
        }

        internal string Draw { get { return Half >= 100 ? "Fifty-move rule" : Repetitions >= 3 ? "Threefold repetition" : Insufficient ? "Insufficient material" : null; } }

        internal string Ending()
        {
            if (Legal().Count == 0) return Check ? "Checkmate" : "Stalemate";
            return Draw;
        }

        internal long Perft(int depth)
        {
            if (depth == 0) return 1;
            List<ChessMove> moves = Legal();
            if (depth == 1) return moves.Count;
            long nodes = 0;
            foreach (ChessMove move in moves)
            {
                ChessUndo undo = Push(move, false);
                nodes += Perft(depth - 1);
                Pop(undo);
            }
            return nodes;
        }

        internal ulong Key()
        {
            ulong value = States[Rights] ^ (Turn == -1 ? States[16] : 0);
            for (int square = 0; square < 128; square++)
            {
                if (!On(square)) { square += 7; continue; }
                int piece = Cells[square];
                if (piece != 0) value ^= Pieces[piece + 6, square];
            }
            if (Ep >= 0 && LegalEp()) value ^= States[17 + (Ep & 7)];
            return value;
        }

        private bool LegalEp()
        {
            int behind = Ep - Turn * 16;
            if (!On(behind) || Cells[Ep] != 0 || Cells[behind] != -Turn) return false;
            for (int direction = -1; direction <= 1; direction += 2)
            {
                int source = behind + direction;
                if (!On(source) || Cells[source] != Turn) continue;
                ChessUndo undo = Push(new ChessMove(source, Ep, 0), false);
                bool safe = !Attacked(Turn == -1 ? WhiteKing : BlackKing, Turn);
                Pop(undo);
                if (safe) return true;
            }
            return false;
        }

        private static ulong NextKey(ref ulong seed) { seed ^= seed << 13; seed ^= seed >> 7; seed ^= seed << 17; return seed; }
        private static ulong[,] PieceKeys()
        {
            ulong seed = 0xC4A91B93UL;
            ulong[,] keys = new ulong[13, 128];
            for (int piece = 0; piece < 13; piece++) for (int square = 0; square < 128; square++) keys[piece, square] = NextKey(ref seed);
            return keys;
        }
        private static ulong[] StateKeys()
        {
            ulong seed = 0x7F39DE842AUL;
            ulong[] keys = new ulong[25];
            for (int state = 0; state < keys.Length; state++) keys[state] = NextKey(ref seed);
            return keys;
        }

        internal string Fen()
        {
            StringBuilder text = new StringBuilder();
            for (int rank = 7; rank >= 0; rank--)
            {
                int empty = 0;
                for (int file = 0; file < 8; file++)
                {
                    int piece = Cells[rank * 16 + file];
                    if (piece == 0) { empty++; continue; }
                    if (empty > 0) { text.Append(empty); empty = 0; }
                    char letter = " pnbrqk"[Math.Abs(piece)];
                    text.Append(piece > 0 ? char.ToUpperInvariant(letter) : letter);
                }
                if (empty > 0) text.Append(empty);
                if (rank > 0) text.Append('/');
            }
            text.Append(Turn == 1 ? " w " : " b ");
            if (Rights == 0) text.Append('-');
            else for (int bit = 0; bit < 4; bit++) if ((Rights & (1 << bit)) != 0) text.Append("KQkq"[bit]);
            return text.Append(' ').Append(Square(Ep)).Append(' ').Append(Half).Append(' ').Append(Full).ToString();
        }
    }

    internal sealed class ChessChoice
    {
        internal ChessMove Move = new ChessMove(-1, -1, 0);
        internal int Score, Depth, Worker;
        internal long Nodes, Milliseconds;
    }

    internal sealed class ChessWork
    {
        internal volatile bool Cancelled, Done;
        internal ChessChoice Choice;
        internal Exception Error;

        internal static ChessWork Start(ChessPosition position, string level, int seed)
        {
            ChessWork work = new ChessWork();
            ChessPosition copy = position.Copy();
            ThreadPool.QueueUserWorkItem(delegate
            {
                try { work.Choice = new ChessSearch(copy, level, new Random(seed), delegate { return work.Cancelled; }).Run(); }
                catch (Exception error) { work.Error = error; }
                finally { work.Done = true; }
            });
            return work;
        }
    }

    internal sealed class ChessSearch
    {
        internal const int Mate = 30000;
        internal static readonly int[] Values = { 0, 100, 320, 335, 500, 900, 0 };
        private static readonly int[,] Tables = MakeTables();
        private readonly ChessPosition board;
        private readonly string level;
        private readonly Random dice;
        private readonly Func<bool> cancelled;
        private readonly Stopwatch clock = new Stopwatch();
        private readonly int[,] history = new int[128, 128];
        private readonly ChessMove[] killers = new ChessMove[128];
        private long nodes;
        private sealed class Finished : Exception { }
        private sealed class Ranked { internal ChessMove Move; internal int Score; }

        internal ChessSearch(ChessPosition position, string strength, Random random, Func<bool> stop)
        {
            board = position; level = strength; dice = random; cancelled = stop;
        }

        internal static int DepthFor(string level) { return level == "easy" ? 2 : level == "hard" ? 6 : 4; }
        internal static int TimeFor(string level) { return level == "easy" ? 250 : level == "hard" ? 2500 : 800; }

        internal ChessChoice Run()
        {
            clock.Start();
            ChessChoice result = new ChessChoice { Worker = Thread.CurrentThread.ManagedThreadId };
            List<ChessMove> moves = board.Legal();
            if (moves.Count == 0 || board.Draw != null) return result;
            result.Move = moves[0];
            List<Ranked> completed = null;
            try
            {
                for (int depth = 1; depth <= DepthFor(level); depth++)
                {
                    Order(moves, result.Move, 0);
                    List<Ranked> ranked = new List<Ranked>();
                    int best = -Mate - 1;
                    ChessMove bestMove = moves[0];
                    foreach (ChessMove move in moves)
                    {
                        Poll();
                        ChessUndo undo = board.Push(move, true);
                        int value;
                        try { value = -Search(depth - 1, -Mate - 1, level == "easy" ? Mate + 1 : -best, 1); }
                        finally { board.Pop(undo); }
                        ranked.Add(new Ranked { Move = move, Score = value });
                        if (value > best) { best = value; bestMove = move; }
                    }
                    ranked.Sort(delegate(Ranked left, Ranked right) { return right.Score.CompareTo(left.Score); });
                    completed = ranked;
                    result.Move = bestMove; result.Score = best; result.Depth = depth;
                    if (Math.Abs(result.Score) > Mate - 128) break;
                }
            }
            catch (Finished) { }
            if (level == "easy" && completed != null && completed.Count > 1 && Math.Abs(result.Score) < Mate - 128 && dice.Next(3) == 0)
            {
                List<Ranked> tempting = completed.FindAll(delegate(Ranked choice) { return choice.Score < result.Score && choice.Score >= result.Score - 140; });
                if (tempting.Count > 0)
                {
                    Ranked slip = tempting[dice.Next(Math.Min(3, tempting.Count))];
                    result.Move = slip.Move; result.Score = slip.Score;
                }
            }
            result.Nodes = nodes; result.Milliseconds = clock.ElapsedMilliseconds;
            return result;
        }

        private void Poll()
        {
            if (cancelled != null && cancelled() || clock.ElapsedMilliseconds >= TimeFor(level)) throw new Finished();
        }

        private int Search(int depth, int alpha, int beta, int ply)
        {
            nodes++;
            Poll();
            bool check = board.Check;
            List<ChessMove> moves = board.Legal();
            if (moves.Count == 0) return check ? -Mate + ply : 0;
            if (board.Draw != null) return 0;
            if (depth <= 0) return Quiet(alpha, beta, ply, moves, check);
            Order(moves, new ChessMove(-1, -1, 0), ply);
            int best = -Mate - 1;
            foreach (ChessMove move in moves)
            {
                bool capture = Capture(move);
                ChessUndo undo = board.Push(move, true);
                int value;
                try { value = -Search(depth - 1, -beta, -alpha, ply + 1); }
                finally { board.Pop(undo); }
                if (value > best) best = value;
                if (value > alpha) alpha = value;
                if (alpha >= beta)
                {
                    if (!capture)
                    {
                        if (ply < killers.Length) killers[ply] = move;
                        history[move.From, move.To] += depth * depth;
                    }
                    break;
                }
            }
            return best;
        }

        private int Quiet(int alpha, int beta, int ply, List<ChessMove> moves, bool check)
        {
            int best = check ? -Mate - 1 : Evaluate(board);
            if (!check)
            {
                if (best >= beta) return best;
                if (best > alpha) alpha = best;
            }
            Order(moves, new ChessMove(-1, -1, 0), ply);
            foreach (ChessMove move in moves)
            {
                if (!check && !Capture(move) && move.Promotion == 0) continue;
                ChessUndo undo = board.Push(move, true);
                int value;
                try { value = -Search(0, -beta, -alpha, ply + 1); }
                finally { board.Pop(undo); }
                if (value > best) best = value;
                if (value > alpha) alpha = value;
                if (alpha >= beta) break;
            }
            return best;
        }

        private bool Capture(ChessMove move) { return board.Cells[move.To] != 0 || Math.Abs(board.Cells[move.From]) == 1 && move.To == board.Ep; }

        private void Order(List<ChessMove> moves, ChessMove preferred, int ply)
        {
            Dictionary<int, int> scores = new Dictionary<int, int>();
            foreach (ChessMove move in moves)
            {
                int score = history[move.From, move.To];
                if (move.Same(preferred)) score += 1000000;
                if (Capture(move)) score += 10000 + 10 * Values[Math.Abs(board.Cells[move.To]) == 0 ? 1 : Math.Abs(board.Cells[move.To])] - Values[Math.Abs(board.Cells[move.From])];
                if (move.Promotion != 0) score += 20000 + Values[move.Promotion];
                if (ply < killers.Length && move.Same(killers[ply])) score += 9000;
                scores[move.From * 1024 + move.To * 8 + move.Promotion] = score;
            }
            moves.Sort(delegate(ChessMove left, ChessMove right)
            {
                return scores[right.From * 1024 + right.To * 8 + right.Promotion].CompareTo(scores[left.From * 1024 + left.To * 8 + left.Promotion]);
            });
        }

        internal static int Evaluate(ChessPosition position)
        {
            int score = 0, phase = 0, whiteBishops = 0, blackBishops = 0;
            int[,] pawns = new int[2, 8];
            for (int scan = 0; scan < 128; scan++)
            {
                if (!ChessPosition.On(scan)) { scan += 7; continue; }
                int man = position.Cells[scan], kind = Math.Abs(man);
                if (kind == 1) pawns[man > 0 ? 0 : 1, scan & 7]++;
                if (kind > 1 && kind < 6) phase += kind == 5 ? 4 : kind == 4 ? 2 : 1;
            }
            phase = Math.Min(24, phase);
            for (int square = 0; square < 128; square++)
            {
                if (!ChessPosition.On(square)) { square += 7; continue; }
                int piece = position.Cells[square], type = Math.Abs(piece);
                if (type == 0) continue;
                int side = piece > 0 ? 1 : -1, row = piece > 0 ? square / 16 : 7 - square / 16, file = square & 7;
                int relative = row * 16 + file;
                int worth = Values[type] + Tables[type, relative];
                if (type == 6) worth = (Tables[6, relative] * phase + Tables[0, relative] * (24 - phase)) / 24;
                if (type == 3) { if (side == 1) whiteBishops++; else blackBishops++; }
                if (type == 1)
                {
                    int own = side == 1 ? 0 : 1;
                    if (pawns[own, file] > 1) worth -= 12;
                    if ((file == 0 || pawns[own, file - 1] == 0) && (file == 7 || pawns[own, file + 1] == 0)) worth -= 14;
                    bool passed = true;
                    for (int ahead = square + side * 16; ChessPosition.On(ahead); ahead += side * 16)
                        for (int beside = -1; beside <= 1; beside++) if (ChessPosition.On(ahead + beside) && position.Cells[ahead + beside] == -side) passed = false;
                    if (passed) worth += row * row * 3;
                }
                if (type == 4 && pawns[side == 1 ? 0 : 1, file] == 0) worth += pawns[side == 1 ? 1 : 0, file] == 0 ? 24 : 12;
                score += side * worth;
            }
            if (whiteBishops >= 2) score += 30;
            if (blackBishops >= 2) score -= 30;
            return score * position.Turn + 8;
        }

        private static int[,] MakeTables()
        {
            int[,] values = new int[7, 128];
            for (int rank = 0; rank < 8; rank++)
                for (int file = 0; file < 8; file++)
                {
                    int at = rank * 16 + file, centre = 7 - Math.Abs(2 * file - 7) - Math.Abs(2 * rank - 7);
                    values[0, at] = centre * 8;
                    values[1, at] = rank * 7 + centre * 3 + (file == 3 || file == 4 ? rank > 1 ? 15 : -12 : 0);
                    values[2, at] = centre * 8 - (rank == 0 ? 12 : 0);
                    values[3, at] = centre * 4 + (rank > 0 ? 10 : 0);
                    values[4, at] = rank == 6 ? 25 : rank * 2;
                    values[5, at] = centre * 2 - (rank > 2 ? 8 : 0);
                    values[6, at] = -rank * 12 - centre * 3 + (rank == 0 && (file == 6 || file == 2) ? 35 : 0);
                }
            return values;
        }
    }
}
