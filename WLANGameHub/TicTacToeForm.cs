using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanGameHub;

public class TicTacToeForm : GameForm
{
    static readonly int[][] Lines =
    {
        new[] { 0, 1, 2 }, new[] { 3, 4, 5 }, new[] { 6, 7, 8 },
        new[] { 0, 3, 6 }, new[] { 1, 4, 7 }, new[] { 2, 5, 8 },
        new[] { 0, 4, 8 }, new[] { 2, 4, 6 },
    };

    readonly Button[] cells = new Button[9];
    readonly char[] board = new char[9];
    readonly Label lbl;
    readonly char mine, theirs;
    int turn;
    bool over;
    string endMsg = "";

    public TicTacToeForm(string opponent, int side) : base("Tic Tac Toe", opponent, side)
    {
        mine = side == 0 ? 'X' : 'O';
        theirs = side == 0 ? 'O' : 'X';
        Array.Fill(board, ' ');

        ClientSize = new Size(340, 390);
        lbl = MakeStatusLabel(320);

        for (int i = 0; i < 9; i++)
        {
            int idx = i;
            var b = new Button
            {
                Left = 12 + (i % 3) * 105,
                Top = 58 + (i / 3) * 105,
                Width = 100,
                Height = 100,
                Font = new Font("Segoe UI", 36, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = TileBg,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(66, 72, 120);
            b.Click += (s, e) => CellClicked(idx);
            cells[i] = b;
            Controls.Add(b);
        }
        UpdateStatus();
    }

    void CellClicked(int i)
    {
        if (over || turn != Side || board[i] != ' ') return;
        Place(i, mine);
        Send(i.ToString());
    }

    public override void ReceiveMove(string data)
    {
        if (over || !int.TryParse(data, out int i) || i < 0 || i > 8 || board[i] != ' ') return;
        Place(i, theirs);
    }

    void Place(int i, char mark)
    {
        board[i] = mark;
        cells[i].Text = mark.ToString();
        cells[i].ForeColor = mark == 'X' ? Pink : Cyan;
        turn = 1 - turn;

        foreach (var line in Lines)
        {
            char a = board[line[0]];
            if (a != ' ' && a == board[line[1]] && a == board[line[2]])
            {
                over = true;
                foreach (int k in line)
                {
                    cells[k].BackColor = Gold;
                    cells[k].ForeColor = DarkBg;
                    cells[k].FlatAppearance.MouseOverBackColor = Gold;
                }
                endMsg = a == mine ? "You win!" : Opponent + " wins!";
                UpdateStatus();
                return;
            }
        }
        if (Array.IndexOf(board, ' ') < 0)
        {
            over = true;
            endMsg = "It's a draw.";
        }
        UpdateStatus();
    }

    void UpdateStatus()
    {
        if (over) lbl.Text = endMsg;
        else if (turn == Side) lbl.Text = "Your turn (" + mine + ")";
        else lbl.Text = "Waiting for " + Opponent + " (" + theirs + ")";
    }
}
