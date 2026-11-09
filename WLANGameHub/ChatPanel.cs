using System;
using System.Drawing;
using System.Windows.Forms;

namespace LanGameHub;

public class ChatPanel : Panel
{
    readonly RichTextBox log = new();
    readonly TextBox input = new();
    readonly Button send = new();
    readonly Label title = new();

    public event Action<string> Submitted;

    public ChatPanel()
    {
        BackColor = Color.FromArgb(36, 40, 72);

        title.Text = "CHAT";
        title.Font = new Font("Segoe UI", 10, FontStyle.Bold);
        title.ForeColor = Color.FromArgb(0, 229, 255);
        title.TextAlign = ContentAlignment.MiddleLeft;

        log.ReadOnly = true;
        log.BorderStyle = BorderStyle.None;
        log.BackColor = Color.FromArgb(24, 26, 48);
        log.ForeColor = Color.White;
        log.Font = new Font("Segoe UI", 10);

        input.BorderStyle = BorderStyle.FixedSingle;
        input.BackColor = Color.FromArgb(48, 53, 92);
        input.ForeColor = Color.White;
        input.Font = new Font("Segoe UI", 10.5f);
        input.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                Submit();
            }
        };

        send.Text = "Send";
        send.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
        send.FlatStyle = FlatStyle.Flat;
        send.BackColor = Color.FromArgb(0, 145, 234);
        send.ForeColor = Color.White;
        send.Cursor = Cursors.Hand;
        send.UseVisualStyleBackColor = false;
        send.FlatAppearance.BorderSize = 0;
        send.Click += (s, e) => Submit();

        Controls.Add(title);
        Controls.Add(log);
        Controls.Add(input);
        Controls.Add(send);
        Resize += (s, e) => LayoutParts();
    }

    void LayoutParts()
    {
        int pad = 8;
        int bh = Math.Max(30, input.PreferredHeight + 4);
        title.SetBounds(pad, 2, Width - pad * 2, 24);
        log.SetBounds(pad, 28, Width - pad * 2, Math.Max(20, Height - 28 - bh - pad * 2));
        int by = Height - bh - pad;
        int bw = 70;
        input.SetBounds(pad, by, Width - pad * 3 - bw, bh);
        send.SetBounds(Width - pad - bw, by - 1, bw, bh + 2);
    }

    void Submit()
    {
        string t = input.Text.Trim();
        if (t.Length == 0) return;
        input.Clear();
        Submitted?.Invoke(t);
    }

    public void Add(string who, string text, bool mine)
    {
        log.SelectionStart = log.TextLength;
        log.SelectionLength = 0;
        log.SelectionColor = mine ? Color.FromArgb(100, 220, 255) : Color.FromArgb(255, 170, 210);
        log.SelectedText = who + ": ";
        log.SelectionColor = Color.White;
        log.SelectedText = text + Environment.NewLine;
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    public void Note(string text)
    {
        log.SelectionStart = log.TextLength;
        log.SelectionLength = 0;
        log.SelectionColor = Color.FromArgb(160, 170, 205);
        log.SelectedText = text + Environment.NewLine;
        log.SelectionStart = log.TextLength;
        log.ScrollToCaret();
    }

    public void FocusInput()
    {
        input.Focus();
    }
}
