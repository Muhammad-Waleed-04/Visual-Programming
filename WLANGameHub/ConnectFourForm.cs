using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanGameHub;

public class ConnectFourForm : GameForm
{
    const int Cell = 70, Cols = 7, Rows = 6;

    readonly int[,] board = new int[Rows, Cols];
    readonly BufferedPanel panel = new();
    readonly Label lbl;
    int turn;
    int hoverCol = -1;
    bool over;
    string endMsg = "";

    public ConnectFourForm(string opponent, int side) : base("Connect Four", opponent, side)
    {
        ClientSize = new Size(Cols * Cell + 20, Rows * Cell + 70);
        lbl = MakeStatusLabel(Cols * Cell);

        panel.SetBounds(10, 56, Cols * Cell, Rows * Cell);
        panel.Paint += Draw;
        panel.MouseMove += (s, e) =>
        {
            int c = e.X / Cell;
            if (c != hoverCol)
            {
                hoverCol = c;
                panel.Invalidate();
            }
        };
        panel.MouseLeave += (s, e) =>
        {
            hoverCol = -1;
            panel.Invalidate();
        };
        panel.MouseClick += (s, e) =>
        {
            if (over || turn != Side) return;
            int c = e.X / Cell;
            if (c < 0 || c >= Cols) return;
            int r = Drop(c, Side + 1);
            if (r < 0) return;
            Send(c.ToString());
            AfterMove(r, c, Side + 1);
        };
        Controls.Add(panel);
        UpdateStatus();
    }

    public override void ReceiveMove(string data)
    {
        if (over || !int.TryParse(data, out int c) || c < 0 || c >= Cols) return;
        int p = (1 - Side) + 1;
        int r = Drop(c, p);
        if (r >= 0) AfterMove(r, c, p);
    }

    int Drop(int c, int player)
    {
        for (int r = Rows - 1; r >= 0; r--)
        {
            if (board[r, c] == 0)
            {
                board[r, c] = player;
                return r;
            }
        }
        return -1;
    }

    void AfterMove(int r, int c, int player)
    {
        panel.Invalidate();
        if (Wins(r, c, player))
        {
            over = true;
            endMsg = player == Side + 1 ? "You win!" : Opponent + " wins!";
        }
        else if (IsFull())
        {
            over = true;
            endMsg = "It's a draw.";
        }
        else
        {
            turn = 1 - turn;
        }
        UpdateStatus();
    }

    bool IsFull()
    {
        for (int c = 0; c < Cols; c++)
            if (board[0, c] == 0) return false;
        return true;
    }

    bool Wins(int r, int c, int p)
    {
        int[,] dirs = { { 0, 1 }, { 1, 0 }, { 1, 1 }, { 1, -1 } };
        for (int d = 0; d < 4; d++)
        {
            int count = 1;
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int rr = r + sign * dirs[d, 0], cc = c + sign * dirs[d, 1];
                while (rr >= 0 && rr < Rows && cc >= 0 && cc < Cols && board[rr, cc] == p)
                {
                    count++;
                    rr += sign * dirs[d, 0];
                    cc += sign * dirs[d, 1];
                }
            }
            if (count >= 4) return true;
        }
        return false;
    }

    void UpdateStatus()
    {
        string colour = Side == 0 ? "Red" : "Yellow";
        if (over) lbl.Text = endMsg;
        else if (turn == Side) lbl.Text = "Your turn (" + colour + ") - click a column";
        else lbl.Text = "Waiting for " + Opponent + "...";
    }

    void Draw(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(
            panel.ClientRectangle, Color.FromArgb(40, 90, 220), Color.FromArgb(20, 50, 150),
            System.Drawing.Drawing2D.LinearGradientMode.Vertical))
            g.FillRectangle(br, panel.ClientRectangle);

        if (!over && turn == Side && hoverCol >= 0 && hoverCol < Cols)
        {
            using var hl = new SolidBrush(Color.FromArgb(55, 255, 255, 255));
            g.FillRectangle(hl, hoverCol * Cell, 0, Cell, Rows * Cell);
        }

        for (int r = 0; r < Rows; r++)
        {
            for (int c = 0; c < Cols; c++)
            {
                int x = c * Cell + 6, y = r * Cell + 6, d = Cell - 12;
                Color col = board[r, c] == 0 ? DarkBg
                    : board[r, c] == 1 ? Color.FromArgb(255, 64, 100)
                    : Color.FromArgb(255, 214, 0);
                using (var br = new SolidBrush(col)) g.FillEllipse(br, x, y, d, d);
                if (board[r, c] != 0)
                {
                    using var gloss = new SolidBrush(Color.FromArgb(80, 255, 255, 255));
                    g.FillEllipse(gloss, x + d * 0.18f, y + d * 0.12f, d * 0.4f, d * 0.28f);
                }
            }
        }
    }
}
