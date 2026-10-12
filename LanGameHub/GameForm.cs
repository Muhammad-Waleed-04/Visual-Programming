using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanGameHub;

/// <summary>Panel without flicker, used for drawing game boards.</summary>
public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}

/// <summary>
/// Base class for every game window.
/// Side 0 = the player who sent the invitation (moves first).
/// Side 1 = the player who accepted.
/// </summary>
public abstract class GameForm : Form
{
    protected static readonly Color DarkBg = Color.FromArgb(24, 26, 48);
    protected static readonly Color TileBg = Color.FromArgb(48, 53, 92);
    protected static readonly Color Gold = Color.FromArgb(255, 213, 79);
    protected static readonly Color Cyan = Color.FromArgb(0, 229, 255);
    protected static readonly Color Pink = Color.FromArgb(255, 82, 130);

    public Action<string> SendMove;   // set by MainForm, sends data to the opponent
    public bool RemoteQuit;           // true when the opponent closed the game

    protected readonly string Opponent;
    protected readonly int Side;

    protected GameForm(string title, string opponent, int side)
    {
        Opponent = opponent;
        Side = side;
        Text = title + " - vs " + opponent;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        BackColor = DarkBg;
    }

    /// <summary>Creates the coloured status label shown at the top of every game.</summary>
    protected Label MakeStatusLabel(int width)
    {
        var l = new Label
        {
            Left = 10,
            Top = 8,
            Width = width,
            Height = 40,
            Font = new Font("Segoe UI", 13, FontStyle.Bold),
            ForeColor = Gold,
            TextAlign = ContentAlignment.MiddleLeft
        };
        Controls.Add(l);
        return l;
    }

    protected void Send(string data) => SendMove?.Invoke(data);

    /// <summary>Called when a move arrives from the opponent.</summary>
    public abstract void ReceiveMove(string data);

    /// <summary>Called when the opponent closes the game window.</summary>
    public void OpponentLeft()
    {
        RemoteQuit = true;
        MessageBox.Show(this, Opponent + " left the game.", "Game over");
        Close();
    }
}
