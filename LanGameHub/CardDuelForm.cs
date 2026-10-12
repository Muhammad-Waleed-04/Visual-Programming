using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LanGameHub;

/// <summary>
/// Card Duel: each player gets 5 random cards (A = 1 ... K = 13).
/// Both pick one card per round in secret; the higher card wins the round.
/// After 5 rounds the player with more round wins is the winner.
/// </summary>
public class CardDuelForm : GameForm
{
    const int Rounds = 5;
    static readonly char[] Suits = { '♥', '♦', '♠', '♣' }; // hearts, diamonds, spades, clubs

    readonly int[] hand;
    readonly Button[] buttons = new Button[Rounds];
    readonly bool[] used = new bool[Rounds];
    readonly Label lblStatus;
    readonly Label lblScore = new();
    readonly TextBox txtLog = new();
    int myCard, oppCard, round, myScore, oppScore;
    int pickedIdx = -1;
    bool over;

    public CardDuelForm(string opponent, int side) : base("Card Duel", opponent, side)
    {
        var rnd = new Random();
        hand = Enumerable.Range(1, 13).OrderBy(_ => rnd.Next()).Take(Rounds).OrderBy(x => x).ToArray();

        ClientSize = new Size(440, 400);
        lblStatus = MakeStatusLabel(420);

        for (int i = 0; i < Rounds; i++)
        {
            int idx = i;
            char suit = Suits[(hand[i] + i) % 4];
            bool red = suit == '♥' || suit == '♦';
            var b = new Button
            {
                Left = 10 + i * 84,
                Top = 56,
                Width = 78,
                Height = 120,
                Text = CardName(hand[i]) + "\n" + suit,
                Font = new Font("Segoe UI", 22, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = red ? Color.Crimson : Color.Black,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Color.FromArgb(255, 245, 200);
            b.Click += (s, e) => PickCard(idx);
            buttons[i] = b;
            Controls.Add(b);
        }

        lblScore.SetBounds(10, 188, 420, 28);
        lblScore.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        lblScore.ForeColor = Cyan;
        Controls.Add(lblScore);

        txtLog.SetBounds(10, 222, 420, 168);
        txtLog.Multiline = true;
        txtLog.ReadOnly = true;
        txtLog.ScrollBars = ScrollBars.Vertical;
        txtLog.BorderStyle = BorderStyle.None;
        txtLog.BackColor = Color.FromArgb(36, 40, 72);
        txtLog.ForeColor = Color.White;
        txtLog.Font = new Font("Segoe UI", 10.5f);
        Controls.Add(txtLog);

        UpdateStatus();
    }

    static string CardName(int v) => v switch { 1 => "A", 11 => "J", 12 => "Q", 13 => "K", _ => v.ToString() };

    void PickCard(int i)
    {
        if (over || myCard != 0 || used[i]) return;
        used[i] = true;
        pickedIdx = i;
        myCard = hand[i];
        buttons[i].BackColor = Gold;
        buttons[i].FlatAppearance.MouseOverBackColor = Gold;
        Send(myCard.ToString());
        TryResolve();
    }

    public override void ReceiveMove(string data)
    {
        if (over || oppCard != 0 || !int.TryParse(data, out int v) || v < 1 || v > 13) return;
        oppCard = v;
        TryResolve();
    }

    void TryResolve()
    {
        if (myCard == 0 || oppCard == 0)
        {
            UpdateStatus();
            return;
        }

        round++;
        string result;
        if (myCard > oppCard) { myScore++; result = "you win the round"; }
        else if (myCard < oppCard) { oppScore++; result = Opponent + " wins the round"; }
        else result = "tie";

        txtLog.AppendText("Round " + round + ": you " + CardName(myCard) + " vs " + Opponent + " " +
                          CardName(oppCard) + " - " + result + Environment.NewLine);

        // grey out the card that was just played
        if (pickedIdx >= 0)
        {
            var b = buttons[pickedIdx];
            b.BackColor = Color.FromArgb(70, 74, 110);
            b.ForeColor = Color.FromArgb(130, 135, 170);
            b.FlatAppearance.MouseOverBackColor = b.BackColor;
        }
        pickedIdx = -1;
        myCard = 0;
        oppCard = 0;

        if (round == Rounds) over = true;
        UpdateStatus();
    }

    void UpdateStatus()
    {
        lblScore.Text = "Score:  You " + myScore + "  -  " + oppScore + "  " + Opponent;
        if (over)
        {
            lblStatus.Text = myScore > oppScore ? "You win the duel!"
                : myScore < oppScore ? Opponent + " wins the duel!"
                : "The duel is a draw.";
        }
        else if (myCard != 0)
        {
            lblStatus.Text = "Waiting for " + Opponent + " to pick...";
        }
        else
        {
            lblStatus.Text = "Round " + (round + 1) + " of " + Rounds + ": pick a card" +
                             (oppCard != 0 ? " (" + Opponent + " has picked)" : "");
        }
    }
}
