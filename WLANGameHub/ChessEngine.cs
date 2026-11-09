using System;
using System.Collections.Generic;
using System.Text;

namespace LanGameHub;

public struct ChessMove
{
    public int From;
    public int To;
    public int Promo;

    public ChessMove(int from, int to, int promo = 0)
    {
        From = from;
        To = to;
        Promo = promo;
    }

    public string Code()
    {
        return From + "," + To + "," + Promo;
    }

    public static bool TryParse(string s, out ChessMove m)
    {
        m = default;
        string[] p = s.Split(',');
        if (p.Length != 3) return false;
        if (!int.TryParse(p[0], out int a) || !int.TryParse(p[1], out int b) || !int.TryParse(p[2], out int c)) return false;
        if (a < 0 || a > 63 || b < 0 || b > 63 || c < 0 || c > 6) return false;
        m = new ChessMove(a, b, c);
        return true;
    }
}

public class ChessEngine
{
    public const int Pawn = 1, Knight = 2, Bishop = 3, Rook = 4, Queen = 5, King = 6;

    public int[] Board = new int[64];
    public bool WhiteToMove = true;
    public bool CastleWK = true, CastleWQ = true, CastleBK = true, CastleBQ = true;
    public int EnPassant = -1;
    public int HalfMove;
    public int FullMove = 1;

    static readonly int[] KnightD = { -17, -15, -10, -6, 6, 10, 15, 17 };
    static readonly int[] KingD = { -9, -8, -7, -1, 1, 7, 8, 9 };

    public ChessEngine()
    {
        int[] back = { Rook, Knight, Bishop, Queen, King, Bishop, Knight, Rook };
        for (int c = 0; c < 8; c++)
        {
            Board[c] = -back[c];
            Board[8 + c] = -Pawn;
            Board[48 + c] = Pawn;
            Board[56 + c] = back[c];
        }
    }

    public ChessEngine Clone()
    {
        var e = new ChessEngine();
        Array.Copy(Board, e.Board, 64);
        e.WhiteToMove = WhiteToMove;
        e.CastleWK = CastleWK;
        e.CastleWQ = CastleWQ;
        e.CastleBK = CastleBK;
        e.CastleBQ = CastleBQ;
        e.EnPassant = EnPassant;
        e.HalfMove = HalfMove;
        e.FullMove = FullMove;
        return e;
    }

    public static string SquareName(int sq)
    {
        return "" + (char)('a' + sq % 8) + (char)('8' - sq / 8);
    }

    static bool Same(int a, int b)
    {
        return (a > 0) == (b > 0);
    }

    public bool IsAttacked(int sq, bool byWhite)
    {
        int r = sq / 8, c = sq % 8;
        int sign = byWhite ? 1 : -1;
        int pr = r + (byWhite ? 1 : -1);
        if (pr >= 0 && pr < 8)
        {
            if (c > 0 && Board[pr * 8 + c - 1] == Pawn * sign) return true;
            if (c < 8 - 1 && Board[pr * 8 + c + 1] == Pawn * sign) return true;
        }
        for (int dr = -2; dr <= 2; dr++)
            for (int dc = -2; dc <= 2; dc++)
            {
                if (Math.Abs(dr) + Math.Abs(dc) != 3 || dr == 0 || dc == 0) continue;
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr > 7 || nc < 0 || nc > 7) continue;
                if (Board[nr * 8 + nc] == Knight * sign) return true;
            }
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                int nr = r + dr, nc = c + dc;
                if (nr < 0 || nr > 7 || nc < 0 || nc > 7) continue;
                if (Board[nr * 8 + nc] == King * sign) return true;
            }
        for (int dr = -1; dr <= 1; dr++)
            for (int dc = -1; dc <= 1; dc++)
            {
                if (dr == 0 && dc == 0) continue;
                bool diag = dr != 0 && dc != 0;
                int nr = r + dr, nc = c + dc;
                while (nr >= 0 && nr < 8 && nc >= 0 && nc < 8)
                {
                    int p = Board[nr * 8 + nc];
                    if (p != 0)
                    {
                        if (Same(p, sign) && p != 0)
                        {
                            int t = Math.Abs(p);
                            if (t == Queen || (diag && t == Bishop) || (!diag && t == Rook)) return true;
                        }
                        break;
                    }
                    nr += dr;
                    nc += dc;
                }
            }
        return false;
    }

    public int KingSquare(bool white)
    {
        int k = white ? King : -King;
        for (int i = 0; i < 64; i++)
            if (Board[i] == k) return i;
        return -1;
    }

    public bool InCheck(bool white)
    {
        int k = KingSquare(white);
        return k >= 0 && IsAttacked(k, !white);
    }

    void AddPseudo(List<ChessMove> list, int from)
    {
        int p = Board[from];
        bool white = p > 0;
        int t = Math.Abs(p);
        int r = from / 8, c = from % 8;
        if (t == Pawn)
        {
            int dir = white ? -1 : 1;
            int startRow = white ? 6 : 1;
            int lastRow = white ? 0 : 7;
            int nr = r + dir;
            if (nr < 0 || nr > 7) return;
            if (Board[nr * 8 + c] == 0)
            {
                AddPawn(list, from, nr * 8 + c, nr == lastRow);
                if (r == startRow && Board[(r + 2 * dir) * 8 + c] == 0) list.Add(new ChessMove(from, (r + 2 * dir) * 8 + c));
            }
            for (int dc = -1; dc <= 1; dc += 2)
            {
                int nc = c + dc;
                if (nc < 0 || nc > 7) continue;
                int to = nr * 8 + nc;
                int q = Board[to];
                if (q != 0 && !Same(q, p)) AddPawn(list, from, to, nr == lastRow);
                else if (q == 0 && to == EnPassant) list.Add(new ChessMove(from, to));
            }
        }
        else if (t == Knight)
        {
            for (int dr = -2; dr <= 2; dr++)
                for (int dc = -2; dc <= 2; dc++)
                {
                    if (Math.Abs(dr) + Math.Abs(dc) != 3 || dr == 0 || dc == 0) continue;
                    int nr = r + dr, nc = c + dc;
                    if (nr < 0 || nr > 7 || nc < 0 || nc > 7) continue;
                    int q = Board[nr * 8 + nc];
                    if (q == 0 || !Same(q, p)) list.Add(new ChessMove(from, nr * 8 + nc));
                }
        }
        else if (t == King)
        {
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    int nr = r + dr, nc = c + dc;
                    if (nr < 0 || nr > 7 || nc < 0 || nc > 7) continue;
                    int q = Board[nr * 8 + nc];
                    if (q == 0 || !Same(q, p)) list.Add(new ChessMove(from, nr * 8 + nc));
                }
            int home = white ? 60 : 4;
            if (from == home && !IsAttacked(home, !white))
            {
                bool ks = white ? CastleWK : CastleBK;
                bool qs = white ? CastleWQ : CastleBQ;
                int rook = white ? Rook : -Rook;
                if (ks && Board[home + 3] == rook && Board[home + 1] == 0 && Board[home + 2] == 0 &&
                    !IsAttacked(home + 1, !white) && !IsAttacked(home + 2, !white))
                    list.Add(new ChessMove(home, home + 2));
                if (qs && Board[home - 4] == rook && Board[home - 1] == 0 && Board[home - 2] == 0 && Board[home - 3] == 0 &&
                    !IsAttacked(home - 1, !white) && !IsAttacked(home - 2, !white))
                    list.Add(new ChessMove(home, home - 2));
            }
        }
        else
        {
            for (int dr = -1; dr <= 1; dr++)
                for (int dc = -1; dc <= 1; dc++)
                {
                    if (dr == 0 && dc == 0) continue;
                    bool diag = dr != 0 && dc != 0;
                    if (t == Bishop && !diag) continue;
                    if (t == Rook && diag) continue;
                    int nr = r + dr, nc = c + dc;
                    while (nr >= 0 && nr < 8 && nc >= 0 && nc < 8)
                    {
                        int q = Board[nr * 8 + nc];
                        if (q == 0) list.Add(new ChessMove(from, nr * 8 + nc));
                        else
                        {
                            if (!Same(q, p)) list.Add(new ChessMove(from, nr * 8 + nc));
                            break;
                        }
                        nr += dr;
                        nc += dc;
                    }
                }
        }
    }

    static void AddPawn(List<ChessMove> list, int from, int to, bool promote)
    {
        if (!promote)
        {
            list.Add(new ChessMove(from, to));
            return;
        }
        list.Add(new ChessMove(from, to, Queen));
        list.Add(new ChessMove(from, to, Rook));
        list.Add(new ChessMove(from, to, Bishop));
        list.Add(new ChessMove(from, to, Knight));
    }

    public List<ChessMove> LegalMoves()
    {
        var pseudo = new List<ChessMove>();
        for (int i = 0; i < 64; i++)
        {
            int p = Board[i];
            if (p != 0 && (p > 0) == WhiteToMove) AddPseudo(pseudo, i);
        }
        var legal = new List<ChessMove>();
        foreach (var m in pseudo)
        {
            var e = Clone();
            e.Apply(m);
            if (!e.InCheck(WhiteToMove)) legal.Add(m);
        }
        return legal;
    }

    public List<ChessMove> LegalFrom(int sq)
    {
        var res = new List<ChessMove>();
        foreach (var m in LegalMoves())
            if (m.From == sq) res.Add(m);
        return res;
    }

    public bool IsLegal(ChessMove m, out ChessMove full)
    {
        foreach (var x in LegalMoves())
        {
            if (x.From == m.From && x.To == m.To && x.Promo == m.Promo)
            {
                full = x;
                return true;
            }
        }
        full = default;
        return false;
    }

    public void Apply(ChessMove m)
    {
        int p = Board[m.From];
        int t = Math.Abs(p);
        bool white = p > 0;
        int captured = Board[m.To];
        bool ep = t == Pawn && m.To == EnPassant && captured == 0 && m.From % 8 != m.To % 8;
        if (ep) Board[m.To + (white ? 8 : -8)] = 0;
        Board[m.To] = p;
        Board[m.From] = 0;
        if (m.Promo != 0) Board[m.To] = white ? m.Promo : -m.Promo;
        if (t == King && Math.Abs(m.To - m.From) == 2)
        {
            if (m.To > m.From)
            {
                Board[m.From + 1] = Board[m.From + 3];
                Board[m.From + 3] = 0;
            }
            else
            {
                Board[m.From - 1] = Board[m.From - 4];
                Board[m.From - 4] = 0;
            }
        }
        if (t == King)
        {
            if (white) { CastleWK = false; CastleWQ = false; }
            else { CastleBK = false; CastleBQ = false; }
        }
        if (m.From == 63 || m.To == 63) CastleWK = false;
        if (m.From == 56 || m.To == 56) CastleWQ = false;
        if (m.From == 7 || m.To == 7) CastleBK = false;
        if (m.From == 0 || m.To == 0) CastleBQ = false;
        EnPassant = -1;
        if (t == Pawn && Math.Abs(m.To - m.From) == 16) EnPassant = (m.From + m.To) / 2;
        HalfMove = (t == Pawn || captured != 0 || ep) ? 0 : HalfMove + 1;
        if (!white) FullMove++;
        WhiteToMove = !WhiteToMove;
    }

    public string San(ChessMove m)
    {
        int p = Board[m.From];
        int t = Math.Abs(p);
        bool capture = Board[m.To] != 0 || (t == Pawn && m.To == EnPassant && m.From % 8 != m.To % 8);
        string s;
        if (t == King && Math.Abs(m.To - m.From) == 2)
        {
            s = m.To > m.From ? "O-O" : "O-O-O";
        }
        else
        {
            var sb = new StringBuilder();
            if (t == Pawn)
            {
                if (capture) sb.Append((char)('a' + m.From % 8));
            }
            else
            {
                sb.Append("_NBRQK"[t]);
                bool sameFile = false, sameRank = false, other = false;
                foreach (var x in LegalMoves())
                {
                    if (x.To == m.To && x.From != m.From && Board[x.From] == p)
                    {
                        other = true;
                        if (x.From % 8 == m.From % 8) sameFile = true;
                        if (x.From / 8 == m.From / 8) sameRank = true;
                    }
                }
                if (other)
                {
                    if (!sameFile) sb.Append((char)('a' + m.From % 8));
                    else if (!sameRank) sb.Append((char)('8' - m.From / 8));
                    else sb.Append(SquareName(m.From));
                }
            }
            if (capture) sb.Append('x');
            sb.Append(SquareName(m.To));
            if (m.Promo != 0) sb.Append('=').Append("_NBRQK"[m.Promo]);
            s = sb.ToString();
        }
        var e = Clone();
        e.Apply(m);
        if (e.InCheck(e.WhiteToMove)) s += e.LegalMoves().Count == 0 ? "#" : "+";
        return s;
    }

    public bool InsufficientMaterial()
    {
        int minors = 0;
        int bishopsLight = 0, bishopsDark = 0;
        for (int i = 0; i < 64; i++)
        {
            int t = Math.Abs(Board[i]);
            if (t == 0 || t == King) continue;
            if (t == Pawn || t == Rook || t == Queen) return false;
            minors++;
            if (t == Bishop)
            {
                if (((i / 8) + (i % 8)) % 2 == 0) bishopsLight++;
                else bishopsDark++;
            }
        }
        if (minors <= 1) return true;
        int bishops = bishopsLight + bishopsDark;
        if (bishops == minors && (bishopsLight == 0 || bishopsDark == 0)) return true;
        return false;
    }

    public string Status()
    {
        bool none = LegalMoves().Count == 0;
        bool check = InCheck(WhiteToMove);
        if (none && check) return "checkmate";
        if (none) return "stalemate";
        if (InsufficientMaterial()) return "insufficient";
        if (HalfMove >= 100) return "fifty";
        return check ? "check" : "";
    }

    public static long Perft(ChessEngine e, int depth)
    {
        if (depth == 0) return 1;
        long n = 0;
        foreach (var m in e.LegalMoves())
        {
            var c = e.Clone();
            c.Apply(m);
            n += depth == 1 ? 1 : Perft(c, depth - 1);
        }
        return n;
    }

    public static ChessEngine FromFen(string fen)
    {
        var e = new ChessEngine();
        Array.Clear(e.Board, 0, 64);
        string[] f = fen.Split(' ');
        int sq = 0;
        foreach (char ch in f[0])
        {
            if (ch == '/') continue;
            if (char.IsDigit(ch)) { sq += ch - '0'; continue; }
            int t = "pnbrqk".IndexOf(char.ToLower(ch)) + 1;
            e.Board[sq++] = char.IsUpper(ch) ? t : -t;
        }
        e.WhiteToMove = f[1] == "w";
        e.CastleWK = f[2].Contains('K');
        e.CastleWQ = f[2].Contains('Q');
        e.CastleBK = f[2].Contains('k');
        e.CastleBQ = f[2].Contains('q');
        e.EnPassant = f[3] == "-" ? -1 : (f[3][0] - 'a') + ('8' - f[3][1]) * 8;
        return e;
    }
}
