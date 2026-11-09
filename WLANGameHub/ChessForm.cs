using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LanGameHub;

public static class ChessAssets
{
    static readonly Dictionary<string, Bitmap> cache = new();

    public static Bitmap Get(string name)
    {
        if (cache.TryGetValue(name, out var b)) return b;
        try
        {
            using var s = typeof(ChessAssets).Assembly.GetManifestResourceStream(name);
            if (s != null)
            {
                using var tmp = new Bitmap(s);
                b = new Bitmap(tmp);
            }
        }
        catch
        {
            b = null;
        }
        cache[name] = b;
        return b;
    }

    public static Bitmap Piece(int p)
    {
        if (p == 0) return null;
        return Get((p > 0 ? "w" : "b") + "pnbrqk"[Math.Abs(p) - 1] + ".png");
    }
}

public class ChessForm : GameForm
{
    readonly ChessEngine engine = new();
    readonly bool iAmWhite;
    readonly bool flipped;
    readonly int sq;
    readonly int frame;

    readonly BufferedPanel board = new();
    readonly BufferedPanel cardOpp = new();
    readonly BufferedPanel cardMe = new();
    readonly RichTextBox moveLog = new();
    readonly ChatPanel chat = new();
    readonly Button btnResign = new();
    readonly Button btnSound = new();
    readonly Label lblStatus;

    readonly List<string> sans = new();
    List<ChessMove> targets = new();
    int selected = -1;
    int lastFrom = -1, lastTo = -1;
    bool over;
    string endMsg = "";

    public ChessForm(string opponent, int side) : base("Chess", opponent, side)
    {
        iAmWhite = side == 0;
        flipped = !iAmWhite;
        var wa = Screen.PrimaryScreen.WorkingArea;
        int fit = (wa.Height - S(56 + 12 + 48)) * 7 / (8 * 7 + 6);
        sq = Math.Max(S(36), Math.Min(S(70), fit));
        frame = Math.Max(S(16), sq * 3 / 7);
        int bp = sq * 8 + frame * 2;
        int panelW = S(340);
        int gap = S(12);

        ClientSize = new Size(gap * 3 + bp + panelW, S(56) + bp + gap);
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(30, 20, 15);
        lblStatus = MakeStatusLabel(bp);
        lblStatus.Top = S(8);
        lblStatus.Height = S(40);

        board.SetBounds(gap, S(56), bp, bp);
        board.Paint += DrawBoard;
        board.MouseClick += BoardClicked;
        Controls.Add(board);

        int x = gap * 2 + bp;
        int y = S(56);
        int cardH = S(66);

        cardOpp.SetBounds(x, y, panelW, cardH);
        cardOpp.Paint += (s, e) => DrawCard(e.Graphics, !iAmWhite, Opponent, cardOpp.ClientSize);
        Controls.Add(cardOpp);
        y += cardH + S(8);

        int movesH = Math.Min(S(120), Math.Max(S(60), bp / 6));
        moveLog.SetBounds(x, y, panelW, movesH);
        moveLog.ReadOnly = true;
        moveLog.BorderStyle = BorderStyle.None;
        moveLog.BackColor = Color.FromArgb(36, 40, 72);
        moveLog.ForeColor = Color.White;
        moveLog.Font = new Font("Consolas", 10.5f);
        Controls.Add(moveLog);
        y += movesH + S(8);

        int bottom = S(56) + bp;
        int btnH = S(40);
        int chatH = Math.Max(S(70), bottom - y - S(8) - cardH - S(8) - btnH - S(8));
        chat.SetBounds(x, y, panelW, chatH);
        Controls.Add(chat);
        y += chatH + S(8);

        cardMe.SetBounds(x, y, panelW, cardH);
        cardMe.Paint += (s, e) => DrawCard(e.Graphics, iAmWhite, "You", cardMe.ClientSize);
        Controls.Add(cardMe);
        y += cardH + S(8);

        btnResign.SetBounds(x, y, panelW / 2 - S(4), btnH);
        StyleButton(btnResign, "Resign", Color.FromArgb(229, 57, 53));
        btnResign.Click += (s, e) => Resign();
        Controls.Add(btnResign);

        btnSound.SetBounds(x + panelW / 2 + S(4), y, panelW / 2 - S(4), btnH);
        StyleButton(btnSound, Sfx.Enabled ? "Sound: ON" : "Sound: OFF", Color.FromArgb(0, 145, 234));
        btnSound.Click += (s, e) =>
        {
            Sfx.Enabled = !Sfx.Enabled;
            btnSound.Text = Sfx.Enabled ? "Sound: ON" : "Sound: OFF";
            Sfx.Play("click");
        };
        Controls.Add(btnSound);

        chat.Submitted += text =>
        {
            SendChat?.Invoke(text);
            chat.Add("Me", text, true);
            Sfx.Play("click");
        };
        chat.Note("You are " + (iAmWhite ? "White" : "Black") + ". Chat with " + opponent + " here.");

        UpdateStatus();
        Sfx.Play("turn");
    }

    static void StyleButton(Button b, string text, Color c)
    {
        b.Text = text;
        b.Font = new Font("Segoe UI", 10.5f, FontStyle.Bold);
        b.FlatStyle = FlatStyle.Flat;
        b.BackColor = c;
        b.ForeColor = Color.White;
        b.Cursor = Cursors.Hand;
        b.UseVisualStyleBackColor = false;
        b.FlatAppearance.BorderSize = 0;
    }

    bool MyTurn => engine.WhiteToMove == iAmWhite;

    Rectangle SquareRect(int index)
    {
        int r = index / 8, c = index % 8;
        int dc = flipped ? 7 - c : c;
        int dr = flipped ? 7 - r : r;
        return new Rectangle(frame + dc * sq, frame + dr * sq, sq, sq);
    }

    int SquareAt(Point p)
    {
        int dc = (p.X - frame) / sq;
        int dr = (p.Y - frame) / sq;
        if (p.X < frame || p.Y < frame || dc < 0 || dc > 7 || dr < 0 || dr > 7) return -1;
        int c = flipped ? 7 - dc : dc;
        int r = flipped ? 7 - dr : dr;
        return r * 8 + c;
    }

    void BoardClicked(object sender, MouseEventArgs e)
    {
        if (over || !MyTurn) return;
        int s = SquareAt(e.Location);
        if (s < 0) return;

        if (selected >= 0)
        {
            var hits = targets.Where(m => m.To == s).ToList();
            if (hits.Count > 0)
            {
                var m = hits[0];
                if (hits.Count > 1)
                {
                    int promo = AskPromotion();
                    m = hits.FirstOrDefault(x => x.Promo == promo);
                }
                selected = -1;
                targets = new List<ChessMove>();
                DoMove(m, true);
                return;
            }
        }

        int p = engine.Board[s];
        if (p != 0 && (p > 0) == iAmWhite)
        {
            selected = s;
            targets = engine.LegalFrom(s);
            Sfx.Play("click");
        }
        else
        {
            selected = -1;
            targets = new List<ChessMove>();
        }
        board.Invalidate();
    }

    int AskPromotion()
    {
        int[] kinds = { ChessEngine.Queen, ChessEngine.Rook, ChessEngine.Bishop, ChessEngine.Knight };
        string[] names = { "Queen", "Rook", "Bishop", "Knight" };
        int chosen = ChessEngine.Queen;
        using var f = new Form
        {
            Text = "Promote pawn",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ControlBox = false,
            BackColor = Color.FromArgb(30, 20, 15),
            ClientSize = new Size(S(4 * 92 + 20), S(130))
        };
        for (int i = 0; i < 4; i++)
        {
            int k = kinds[i];
            var b = new Button
            {
                Left = S(10 + i * 92),
                Top = S(10),
                Width = S(88),
                Height = S(110),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(150, 98, 58),
                ForeColor = Color.White,
                Text = names[i],
                TextAlign = ContentAlignment.BottomCenter,
                ImageAlign = ContentAlignment.TopCenter,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            var img = ChessAssets.Piece(iAmWhite ? k : -k);
            if (img != null) b.Image = new Bitmap(img, new Size(S(72), S(72)));
            b.FlatAppearance.BorderSize = 0;
            b.Click += (s, e) =>
            {
                chosen = k;
                f.DialogResult = DialogResult.OK;
            };
            f.Controls.Add(b);
        }
        f.ShowDialog(this);
        return chosen;
    }

    void DoMove(ChessMove m, bool mine)
    {
        int moved = engine.Board[m.From];
        bool capture = engine.Board[m.To] != 0 ||
            (Math.Abs(moved) == ChessEngine.Pawn && m.To == engine.EnPassant && m.From % 8 != m.To % 8);
        bool whiteMoved = engine.WhiteToMove;
        string san = engine.San(m);
        engine.Apply(m);
        lastFrom = m.From;
        lastTo = m.To;
        sans.Add(san);
        if (mine) Send(m.Code());

        string st = engine.Status();
        if (st == "checkmate")
        {
            over = true;
            endMsg = whiteMoved == iAmWhite ? "Checkmate - you win!" : "Checkmate - " + Opponent + " wins!";
            Sfx.Play("win");
        }
        else if (st == "stalemate" || st == "insufficient" || st == "fifty")
        {
            over = true;
            endMsg = st == "stalemate" ? "Stalemate - draw." : st == "fifty" ? "Draw by the fifty-move rule." : "Draw - not enough pieces.";
            Sfx.Play("pass");
        }
        else if (st == "check")
        {
            Sfx.Play(capture ? "capture" : "move", "turn");
        }
        else
        {
            Sfx.Play(capture ? "capture" : "move");
        }

        RefreshMoves();
        UpdateStatus();
    }

    public override void ReceiveMove(string data)
    {
        if (over) return;
        if (data == "resign")
        {
            over = true;
            endMsg = Opponent + " resigned - you win!";
            Sfx.Play("win");
            UpdateStatus();
            return;
        }
        if (MyTurn || !ChessMove.TryParse(data, out var m)) return;
        if (!engine.IsLegal(m, out var full)) return;
        selected = -1;
        targets = new List<ChessMove>();
        DoMove(full, false);
    }

    public override void ReceiveChat(string text)
    {
        chat.Add(Opponent, text, false);
        Sfx.Play("turn");
    }

    void Resign()
    {
        if (over) return;
        if (MessageBox.Show(this, "Do you want to resign this game?", "Chess", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        over = true;
        endMsg = "You resigned.";
        Send("resign");
        UpdateStatus();
    }

    void RefreshMoves()
    {
        moveLog.Clear();
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < sans.Count; i += 2)
        {
            sb.Append((i / 2 + 1).ToString().PadLeft(3)).Append(". ").Append(sans[i].PadRight(8));
            if (i + 1 < sans.Count) sb.Append(sans[i + 1]);
            sb.AppendLine();
        }
        moveLog.Text = sb.ToString();
        moveLog.SelectionStart = moveLog.TextLength;
        moveLog.ScrollToCaret();
    }

    void UpdateStatus()
    {
        if (over)
        {
            lblStatus.Text = endMsg;
        }
        else
        {
            bool check = engine.InCheck(engine.WhiteToMove);
            if (MyTurn) lblStatus.Text = (check ? "Check! " : "") + "Your turn (" + (iAmWhite ? "White" : "Black") + ")";
            else lblStatus.Text = (check ? "Check! " : "") + "Waiting for " + Opponent + " (" + (iAmWhite ? "Black" : "White") + ")";
        }
        board.Invalidate();
        cardOpp.Invalidate();
        cardMe.Invalidate();
    }

    static readonly Font cardName = new Font("Segoe UI", 11, FontStyle.Bold);
    static readonly Font cardAdv = new Font("Segoe UI", 10, FontStyle.Bold);
    static readonly int[] Initial = { 8, 2, 2, 2, 1 };

    List<int> Captured(bool whitePieces)
    {
        var have = new int[7];
        foreach (int p in engine.Board)
            if (p != 0 && (p > 0) == whitePieces) have[Math.Abs(p)]++;
        var list = new List<int>();
        for (int t = ChessEngine.Queen; t >= ChessEngine.Pawn; t--)
        {
            int missing = Initial[t - 1] - have[t];
            for (int i = 0; i < missing; i++) list.Add(t);
        }
        return list;
    }

    static int Value(int t) => t switch { 1 => 1, 2 => 3, 3 => 3, 4 => 5, 5 => 9, _ => 0 };

    void DrawCard(Graphics g, bool white, string name, Size size)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.FromArgb(30, 20, 15));
        var rect = new Rectangle(0, 0, size.Width - 1, size.Height - 1);
        bool turn = !over && engine.WhiteToMove == white;
        using (var bg = new SolidBrush(Color.FromArgb(48, 40, 62)))
        using (var path = RoundRect(rect, S(14)))
            g.FillPath(bg, path);
        if (turn)
        {
            using var pen = new Pen(Gold, 3);
            using var path = RoundRect(rect, S(14));
            g.DrawPath(pen, path);
        }

        var icon = ChessAssets.Piece(white ? ChessEngine.King : -ChessEngine.King);
        int isz = S(56);
        if (icon != null) g.DrawImage(icon, new Rectangle(S(6), (size.Height - isz) / 2 - S(4), isz, isz));

        int tx = S(66);
        TextRenderer.DrawText(g, name + (white ? "  (White)" : "  (Black)"), cardName,
            new Rectangle(tx, S(6), size.Width - tx - S(6), S(24)), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

        var taken = Captured(!white);
        int adv = taken.Sum(Value) - Captured(white).Sum(Value);
        int ix = tx;
        int ic = S(26);
        foreach (int t in taken)
        {
            var img = ChessAssets.Piece(white ? -t : t);
            if (img != null) g.DrawImage(img, new Rectangle(ix, S(32), ic, ic));
            ix += S(13);
        }
        if (adv > 0)
            TextRenderer.DrawText(g, "+" + adv, cardAdv,
                new Rectangle(Math.Max(ix + S(14), tx), S(34), S(50), S(24)), Gold,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
    }

    void DrawBoard(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        int bp = sq * 8 + frame * 2;
        var all = new Rectangle(0, 0, bp, bp);

        var fr = ChessAssets.Get("frame.jpg");
        if (fr != null) g.DrawImage(fr, all);
        else g.Clear(Color.FromArgb(92, 56, 30));
        using (var pen = new Pen(Color.FromArgb(40, 22, 10), 3))
            g.DrawRectangle(pen, 1, 1, bp - 3, bp - 3);
        using (var pen = new Pen(Color.FromArgb(150, 255, 220, 160), 1))
            g.DrawRectangle(pen, 4, 4, bp - 9, bp - 9);

        using var labelFont = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        for (int i = 0; i < 8; i++)
        {
            int c = flipped ? 7 - i : i;
            string file = ((char)('a' + c)).ToString();
            string rank = ((char)('8' - (flipped ? 7 - i : i))).ToString();
            var fx = new Rectangle(frame + i * sq, bp - frame, sq, frame);
            var fy = new Rectangle(0, frame + i * sq, frame, sq);
            var ft = new Rectangle(frame + i * sq, 0, sq, frame);
            var fy2 = new Rectangle(bp - frame, frame + i * sq, frame, sq);
            var col = Color.FromArgb(245, 222, 170);
            var fl = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter;
            TextRenderer.DrawText(g, file, labelFont, fx, col, fl);
            TextRenderer.DrawText(g, file, labelFont, ft, col, fl);
            TextRenderer.DrawText(g, rank, labelFont, fy, col, fl);
            TextRenderer.DrawText(g, rank, labelFont, fy2, col, fl);
        }

        var light = ChessAssets.Get("sqlight.jpg");
        var dark = ChessAssets.Get("sqdark.jpg");
        for (int i = 0; i < 64; i++)
        {
            int r = i / 8, c = i % 8;
            var rc = SquareRect(i);
            var tex = (r + c) % 2 == 0 ? light : dark;
            if (tex != null)
            {
                int sx = (c * 53 + r * 29) % 128;
                int sy = (r * 47 + c * 17) % 128;
                g.DrawImage(tex, rc, sx, sy, 128, 128, GraphicsUnit.Pixel);
            }
            else
            {
                using var b = new SolidBrush((r + c) % 2 == 0 ? Color.Wheat : Color.SaddleBrown);
                g.FillRectangle(b, rc);
            }
        }
        using (var pen = new Pen(Color.FromArgb(90, 20, 10, 5), 2))
            g.DrawRectangle(pen, frame, frame, sq * 8, sq * 8);

        if (lastFrom >= 0)
        {
            using var b = new SolidBrush(Color.FromArgb(95, 255, 220, 60));
            g.FillRectangle(b, SquareRect(lastFrom));
            g.FillRectangle(b, SquareRect(lastTo));
        }
        if (selected >= 0)
        {
            using var b = new SolidBrush(Color.FromArgb(130, 70, 200, 110));
            g.FillRectangle(b, SquareRect(selected));
        }
        if (!over && engine.InCheck(engine.WhiteToMove))
        {
            int ks = engine.KingSquare(engine.WhiteToMove);
            if (ks >= 0)
            {
                var rc = SquareRect(ks);
                using var path = new GraphicsPath();
                path.AddEllipse(rc);
                using var pb = new PathGradientBrush(path)
                {
                    CenterColor = Color.FromArgb(230, 230, 30, 30),
                    SurroundColors = new[] { Color.FromArgb(0, 230, 30, 30) }
                };
                g.FillRectangle(pb, rc);
            }
        }

        foreach (var m in targets)
        {
            var rc = SquareRect(m.To);
            bool occupied = engine.Board[m.To] != 0 || (m.To == engine.EnPassant && Math.Abs(engine.Board[m.From]) == ChessEngine.Pawn);
            if (occupied)
            {
                using var pen = new Pen(Color.FromArgb(190, 40, 130, 60), Math.Max(3, sq / 14));
                g.DrawEllipse(pen, rc.X + 4, rc.Y + 4, rc.Width - 8, rc.Height - 8);
            }
            else
            {
                int d = sq / 3;
                using var b = new SolidBrush(Color.FromArgb(150, 40, 110, 55));
                g.FillEllipse(b, rc.X + (sq - d) / 2, rc.Y + (sq - d) / 2, d, d);
            }
        }

        int size = (int)(sq * 1.45);
        for (int dr = 0; dr < 8; dr++)
        {
            for (int dc = 0; dc < 8; dc++)
            {
                int c = flipped ? 7 - dc : dc;
                int r = flipped ? 7 - dr : dr;
                int idx = r * 8 + c;
                var img = ChessAssets.Piece(engine.Board[idx]);
                if (img == null) continue;
                var rc = SquareRect(idx);
                int px = rc.X + (sq - size) / 2;
                int py = rc.Y + (int)(sq * 0.9) - (int)(size * 0.955);
                g.DrawImage(img, new Rectangle(px, py, size, size));
            }
        }
    }
}
