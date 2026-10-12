using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace LanGameHub;

/// <summary>
/// Chess with legal-move checking, check, checkmate, stalemate and auto-queen promotion.
/// (Castling and en passant are not included to keep the code simple.)
/// Board: row 0 is rank 8 (top). Upper case = white, lower case = black, '.' = empty.
/// </summary>
public class ChessForm : GameForm
{
    const int Cell = 64;
    const string Glyphs = "♚♛♜♝♞♟"; // K Q R B N P
    static readonly (int dr, int dc)[] KnightMoves =
        { (-2, -1), (-2, 1), (-1, -2), (-1, 2), (1, -2), (1, 2), (2, -1), (2, 1) };
    static readonly (int dr, int dc)[] KingMoves =
        { (-1, -1), (-1, 0), (-1, 1), (0, -1), (0, 1), (1, -1), (1, 0), (1, 1) };

    readonly char[,] b = new char[8, 8];
    readonly BufferedPanel panel = new();
    readonly Label lbl;
    readonly Font pieceFont = new Font("Segoe UI Symbol", 30, FontStyle.Regular, GraphicsUnit.Pixel);

    bool whiteToMove = true;
    bool over;
    bool check;
    string endMsg = "";
    int selR = -1, selC = -1;
    List<(int r, int c)> targets = new();
    (int r1, int c1, int r2, int c2)? last;

    bool IWhite => Side == 0;

    public ChessForm(string opponent, int side) : base("Chess", opponent, side)
    {
        string[] start = { "rnbqkbnr", "pppppppp", "........", "........", "........", "........", "PPPPPPPP", "RNBQKBNR" };
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                b[r, c] = start[r][c];

        ClientSize = new Size(8 * Cell + 20, 8 * Cell + 70);
        lbl = MakeStatusLabel(8 * Cell);

        panel.SetBounds(10, 56, 8 * Cell, 8 * Cell);
        panel.Paint += Draw;
        panel.MouseClick += OnClick;
        Controls.Add(panel);
        UpdateStatus();
    }

    // ---------------------------------------------------------------- input

    void OnClick(object sender, MouseEventArgs e)
    {
        if (over || whiteToMove != IWhite) return;
        int vc = e.X / Cell, vr = e.Y / Cell;
        if (vc < 0 || vc > 7 || vr < 0 || vr > 7) return;
        int r = IWhite ? vr : 7 - vr;
        int c = IWhite ? vc : 7 - vc;

        if (selR >= 0 && targets.Contains((r, c)))
        {
            int r1 = selR, c1 = selC;
            ClearSelection();
            DoMove(r1, c1, r, c);
            Send(r1 + "," + c1 + "," + r + "," + c);
        }
        else if (b[r, c] != '.' && IsWhite(b[r, c]) == IWhite)
        {
            selR = r;
            selC = c;
            targets = Legal(b, r, c);
        }
        else
        {
            ClearSelection();
        }
        panel.Invalidate();
    }

    void ClearSelection()
    {
        selR = -1;
        selC = -1;
        targets = new List<(int r, int c)>();
    }

    public override void ReceiveMove(string data)
    {
        string[] p = data.Split(',');
        if (over || p.Length != 4) return;
        if (!int.TryParse(p[0], out int r1) || !int.TryParse(p[1], out int c1) ||
            !int.TryParse(p[2], out int r2) || !int.TryParse(p[3], out int c2)) return;
        if (!In(r1, c1) || !In(r2, c2) || b[r1, c1] == '.') return;
        DoMove(r1, c1, r2, c2);
        panel.Invalidate();
    }

    // ---------------------------------------------------------------- game state

    void DoMove(int r1, int c1, int r2, int c2)
    {
        Apply(b, r1, c1, r2, c2);
        last = (r1, c1, r2, c2);
        whiteToMove = !whiteToMove;

        check = InCheck(b, whiteToMove);
        if (!AnyLegal(b, whiteToMove))
        {
            over = true;
            bool whiteJustMoved = !whiteToMove;
            if (check)
                endMsg = whiteJustMoved == IWhite ? "Checkmate - you win!" : "Checkmate - " + Opponent + " wins!";
            else
                endMsg = "Stalemate - it's a draw.";
        }
        UpdateStatus();
    }

    void UpdateStatus()
    {
        string colour = IWhite ? "White" : "Black";
        if (over) lbl.Text = endMsg;
        else if (whiteToMove == IWhite) lbl.Text = "Your turn (" + colour + ")" + (check ? " - Check!" : "");
        else lbl.Text = "Waiting for " + Opponent + "..." + (check ? " (they are in check)" : "");
    }

    // ---------------------------------------------------------------- drawing

    void Draw(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        for (int vr = 0; vr < 8; vr++)
        {
            for (int vc = 0; vc < 8; vc++)
            {
                int r = IWhite ? vr : 7 - vr;
                int c = IWhite ? vc : 7 - vc;
                var rect = new Rectangle(vc * Cell, vr * Cell, Cell, Cell);

                Color sq = (r + c) % 2 == 0 ? Color.FromArgb(238, 238, 210) : Color.FromArgb(118, 150, 86);
                if (last.HasValue && ((last.Value.r1 == r && last.Value.c1 == c) || (last.Value.r2 == r && last.Value.c2 == c)))
                    sq = Color.FromArgb(186, 202, 68);
                if (r == selR && c == selC) sq = Color.FromArgb(255, 235, 59);
                using (var br = new SolidBrush(sq)) g.FillRectangle(br, rect);

                if (b[r, c] != '.') DrawPiece(g, b[r, c], rect, sf);

                if (targets.Contains((r, c)))
                {
                    using var hint = new SolidBrush(Color.FromArgb(120, 20, 120, 20));
                    if (b[r, c] == '.')
                        g.FillEllipse(hint, rect.X + Cell / 2 - 8, rect.Y + Cell / 2 - 8, 16, 16);
                    else
                        using (var pen = new Pen(Color.FromArgb(160, 20, 120, 20), 4))
                            g.DrawEllipse(pen, rect.X + 4, rect.Y + 4, Cell - 8, Cell - 8);
                }
            }
        }
    }

    void DrawPiece(Graphics g, char p, Rectangle rect, StringFormat sf)
    {
        string glyph = Glyphs[("KQRBNP").IndexOf(char.ToUpper(p))].ToString();
        bool white = IsWhite(p);
        using var fill = new SolidBrush(white ? Color.White : Color.Black);
        using var outline = new SolidBrush(white ? Color.Black : Color.White);
        var rf = new RectangleF(rect.X, rect.Y, rect.Width, rect.Height);
        foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
        {
            var o = new RectangleF(rf.X + dx, rf.Y + dy, rf.Width, rf.Height);
            g.DrawString(glyph, pieceFont, outline, o, sf);
        }
        g.DrawString(glyph, pieceFont, fill, rf, sf);
    }

    // ---------------------------------------------------------------- chess rules

    static bool IsWhite(char p) => char.IsUpper(p);

    static bool In(int r, int c) => r >= 0 && r < 8 && c >= 0 && c < 8;

    static void Apply(char[,] bd, int r1, int c1, int r2, int c2)
    {
        char p = bd[r1, c1];
        bd[r1, c1] = '.';
        if (p == 'P' && r2 == 0) p = 'Q';
        if (p == 'p' && r2 == 7) p = 'q';
        bd[r2, c2] = p;
    }

    static List<(int r, int c)> Pseudo(char[,] bd, int r, int c)
    {
        var res = new List<(int r, int c)>();
        char p = bd[r, c];
        bool w = IsWhite(p);

        void Add(int rr, int cc)
        {
            if (!In(rr, cc)) return;
            char q = bd[rr, cc];
            if (q == '.' || IsWhite(q) != w) res.Add((rr, cc));
        }

        void Ray(int dr, int dc)
        {
            int rr = r + dr, cc = c + dc;
            while (In(rr, cc))
            {
                char q = bd[rr, cc];
                if (q == '.')
                {
                    res.Add((rr, cc));
                }
                else
                {
                    if (IsWhite(q) != w) res.Add((rr, cc));
                    break;
                }
                rr += dr;
                cc += dc;
            }
        }

        switch (char.ToUpper(p))
        {
            case 'P':
                {
                    int d = w ? -1 : 1;
                    int startRow = w ? 6 : 1;
                    if (In(r + d, c) && bd[r + d, c] == '.')
                    {
                        res.Add((r + d, c));
                        if (r == startRow && bd[r + 2 * d, c] == '.') res.Add((r + 2 * d, c));
                    }
                    foreach (int dc in new[] { -1, 1 })
                    {
                        int rr = r + d, cc = c + dc;
                        if (In(rr, cc) && bd[rr, cc] != '.' && IsWhite(bd[rr, cc]) != w) res.Add((rr, cc));
                    }
                    break;
                }
            case 'N':
                foreach (var (dr, dc) in KnightMoves) Add(r + dr, c + dc);
                break;
            case 'K':
                foreach (var (dr, dc) in KingMoves) Add(r + dr, c + dc);
                break;
            case 'B':
                Ray(-1, -1); Ray(-1, 1); Ray(1, -1); Ray(1, 1);
                break;
            case 'R':
                Ray(-1, 0); Ray(1, 0); Ray(0, -1); Ray(0, 1);
                break;
            case 'Q':
                Ray(-1, -1); Ray(-1, 1); Ray(1, -1); Ray(1, 1);
                Ray(-1, 0); Ray(1, 0); Ray(0, -1); Ray(0, 1);
                break;
        }
        return res;
    }

    static bool IsAttacked(char[,] bd, int r, int c, bool byWhite)
    {
        char Col(char upper) => byWhite ? upper : char.ToLower(upper);

        // pawns
        int pr = byWhite ? r + 1 : r - 1;
        foreach (int dc in new[] { -1, 1 })
            if (In(pr, c + dc) && bd[pr, c + dc] == Col('P')) return true;

        // knights and king
        foreach (var (dr, dc) in KnightMoves)
            if (In(r + dr, c + dc) && bd[r + dr, c + dc] == Col('N')) return true;
        foreach (var (dr, dc) in KingMoves)
            if (In(r + dr, c + dc) && bd[r + dr, c + dc] == Col('K')) return true;

        // sliding pieces
        foreach (var (dr, dc) in KingMoves)
        {
            bool diagonal = dr != 0 && dc != 0;
            int rr = r + dr, cc = c + dc;
            while (In(rr, cc))
            {
                char q = bd[rr, cc];
                if (q != '.')
                {
                    if (q == Col('Q') || (diagonal && q == Col('B')) || (!diagonal && q == Col('R'))) return true;
                    break;
                }
                rr += dr;
                cc += dc;
            }
        }
        return false;
    }

    static bool InCheck(char[,] bd, bool white)
    {
        char king = white ? 'K' : 'k';
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (bd[r, c] == king) return IsAttacked(bd, r, c, !white);
        return false;
    }

    static List<(int r, int c)> Legal(char[,] bd, int r, int c)
    {
        var res = new List<(int r, int c)>();
        bool white = IsWhite(bd[r, c]);
        foreach (var (rr, cc) in Pseudo(bd, r, c))
        {
            var copy = (char[,])bd.Clone();
            Apply(copy, r, c, rr, cc);
            if (!InCheck(copy, white)) res.Add((rr, cc));
        }
        return res;
    }

    static bool AnyLegal(char[,] bd, bool white)
    {
        for (int r = 0; r < 8; r++)
            for (int c = 0; c < 8; c++)
                if (bd[r, c] != '.' && IsWhite(bd[r, c]) == white && Legal(bd, r, c).Count > 0)
                    return true;
        return false;
    }
}
