using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LanGameHub;

public class BufferedPanel : Panel
{
    public BufferedPanel()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }
}

public abstract class GameForm : Form
{
    protected static readonly Color DarkBg = Color.FromArgb(24, 26, 48);
    protected static readonly Color TileBg = Color.FromArgb(48, 53, 92);
    protected static readonly Color Gold = Color.FromArgb(255, 213, 79);
    protected static readonly Color Cyan = Color.FromArgb(0, 229, 255);
    protected static readonly Color Pink = Color.FromArgb(255, 82, 130);

    public Action<string> SendMove;
    public Action<string> SendChat;
    public bool RemoteQuit;

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

    protected int S(int v)
    {
        return (int)Math.Round(v * DeviceDpi / 96.0);
    }

    protected static GraphicsPath RoundRect(Rectangle r, int d)
    {
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

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

    public abstract void ReceiveMove(string data);

    public virtual void ReceiveChat(string text)
    {
    }

    public void OpponentLeft()
    {
        RemoteQuit = true;
        MessageBox.Show(this, Opponent + " left the game.", "Game over");
        Close();
    }
}
