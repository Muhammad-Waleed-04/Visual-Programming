using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LanGameHub;

/// <summary>Main window: player list, chat and game invitations.</summary>
public class MainForm : Form
{
    record GameInfo(string Key, string Title)
    {
        public override string ToString() => Title;
    }

    // Kind: 0 = my message, 1 = other player's message, 2 = system note
    record ChatLine(string Text, int Kind);

    static readonly GameInfo[] AllGames =
    {
        new GameInfo("ttt", "Tic Tac Toe"),
        new GameInfo("c4", "Connect Four"),
        new GameInfo("chess", "Chess"),
        new GameInfo("cards", "Card Duel"),
    };

    // ---- colours
    static readonly Color Bg = Color.FromArgb(24, 26, 48);
    static readonly Color PanelBg = Color.FromArgb(36, 40, 72);
    static readonly Color InputBg = Color.FromArgb(48, 53, 92);
    static readonly Color Purple = Color.FromArgb(124, 77, 255);
    static readonly Color Pink = Color.FromArgb(255, 64, 129);
    static readonly Color Green = Color.FromArgb(0, 190, 120);
    static readonly Color Blue = Color.FromArgb(0, 145, 234);
    static readonly Color Amber = Color.FromArgb(255, 213, 79);
    static readonly Color[] AvatarColors =
    {
        Color.FromArgb(255, 82, 82), Color.FromArgb(255, 152, 0), Color.FromArgb(0, 200, 120),
        Color.FromArgb(0, 188, 212), Color.FromArgb(124, 77, 255), Color.FromArgb(233, 30, 99),
    };

    readonly Font fTitle = new Font("Segoe UI", 20, FontStyle.Bold);
    readonly Font fBold = new Font("Segoe UI", 10.5f, FontStyle.Bold);
    readonly Font fText = new Font("Segoe UI", 10.5f);
    readonly Font fSmall = new Font("Segoe UI", 9f);

    readonly NetworkService net = new();
    readonly Dictionary<string, List<ChatLine>> history = new();
    readonly Dictionary<string, GameForm> games = new();

    readonly BufferedPanel header = new();
    readonly ListBox lstPeers = new();
    readonly RichTextBox txtChat = new();
    readonly TextBox txtInput = new();
    readonly Button btnSend;
    readonly Button btnInvite;
    readonly Button btnName;
    readonly Button btnRefresh;
    readonly ComboBox cmbGame = new();
    readonly Label lblStatus = new();
    readonly Label lblPlayers = new();
    readonly string myIp = NetworkService.GetMyIp();
    bool refreshing;
    bool scanning;

    int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    public MainForm()
    {
        Text = "LAN Game Hub";
        BackColor = Bg;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(900), S(580));
        MinimumSize = new Size(S(760), S(480));

        net.MyName = CleanName(Ask("Enter your player name:", Environment.MachineName));

        btnName = MakeButton("Change name", Purple);
        btnRefresh = MakeButton("Refresh", Color.FromArgb(255, 143, 0));
        btnInvite = MakeButton("Invite to play", Green);
        btnSend = MakeButton("Send", Blue);

        BuildUi();

        net.PeersChanged += () => Ui(RefreshPeers);
        net.MessageReceived += (id, type, payload) => Ui(() => HandleMessage(id, type, payload));
        net.SendFailed += id => Ui(() => AddChat(id, "[Could not reach " + NameOf(id) + "]", 2));

        Shown += (s, e) =>
        {
            try
            {
                net.Start();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Network could not start:\n" + ex.Message, "Error");
            }
        };
        FormClosing += (s, e) => net.Dispose();
    }

    // ---------------------------------------------------------------- UI

    Button MakeButton(string text, Color color)
    {
        var b = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = color,
            ForeColor = Color.White,
            Font = fBold,
            Cursor = Cursors.Hand,
            Margin = new Padding(0, S(4), 0, S(4)),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = ControlPaint.Light(color, 0.25f);
        b.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(color, 0.1f);
        return b;
    }

    void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(80)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        header.Dock = DockStyle.Fill;
        header.Margin = Padding.Empty;
        header.Paint += PaintHeader;
        root.Controls.Add(header, 0, 0);

        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(S(12)),
            Margin = Padding.Empty,
            BackColor = Bg
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(280)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        // ---- left side: players and games
        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            Margin = new Padding(0, 0, S(12), 0),
            BackColor = Bg
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(48)));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(46)));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(60)));

        btnName.Click += (s, e) =>
        {
            net.MyName = CleanName(Ask("Enter your player name:", net.MyName));
            header.Invalidate();
        };

        btnRefresh.Click += async (s, e) =>
        {
            scanning = true;
            btnRefresh.Enabled = false;
            lblStatus.Text = "Scanning your Wi-Fi for players...";
            net.Refresh();
            await Task.Delay(2500);
            scanning = false;
            btnRefresh.Enabled = true;
            RefreshPeers();
        };

        lblPlayers.Text = "PLAYERS ON YOUR NETWORK (0)";
        lblPlayers.Dock = DockStyle.Fill;
        lblPlayers.Font = fBold;
        lblPlayers.ForeColor = Color.FromArgb(0, 229, 255);
        lblPlayers.TextAlign = ContentAlignment.BottomLeft;
        lblPlayers.Margin = Padding.Empty;

        lstPeers.Dock = DockStyle.Fill;
        lstPeers.Margin = Padding.Empty;
        lstPeers.BorderStyle = BorderStyle.None;
        lstPeers.BackColor = PanelBg;
        lstPeers.ForeColor = Color.White;
        lstPeers.DrawMode = DrawMode.OwnerDrawFixed;
        lstPeers.ItemHeight = S(56);
        lstPeers.DrawItem += DrawPeerItem;
        lstPeers.SelectedIndexChanged += (s, e) =>
        {
            if (!refreshing) ShowHistory();
        };

        cmbGame.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        cmbGame.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbGame.FlatStyle = FlatStyle.Flat;
        cmbGame.BackColor = InputBg;
        cmbGame.ForeColor = Color.White;
        cmbGame.Font = fBold;
        cmbGame.Items.AddRange(AllGames);
        cmbGame.SelectedIndex = 0;

        btnInvite.Font = new Font("Segoe UI", 12, FontStyle.Bold);
        btnInvite.Click += (s, e) => InviteSelected();

        var topRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Bg
        };
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        topRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        topRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        btnName.Margin = new Padding(0, S(4), S(4), S(4));
        btnRefresh.Margin = new Padding(S(4), S(4), 0, S(4));
        topRow.Controls.Add(btnName, 0, 0);
        topRow.Controls.Add(btnRefresh, 1, 0);
        left.Controls.Add(topRow, 0, 0);
        left.Controls.Add(lblPlayers, 0, 1);
        left.Controls.Add(lstPeers, 0, 2);
        left.Controls.Add(cmbGame, 0, 3);
        left.Controls.Add(btnInvite, 0, 4);
        body.Controls.Add(left, 0, 0);

        // ---- right side: chat
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            BackColor = Bg
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, S(54)));

        lblStatus.Dock = DockStyle.Fill;
        lblStatus.Font = fBold;
        lblStatus.ForeColor = Amber;
        lblStatus.TextAlign = ContentAlignment.BottomLeft;
        lblStatus.Margin = Padding.Empty;
        lblStatus.Text = "Looking for players on your network...";

        txtChat.Dock = DockStyle.Fill;
        txtChat.Margin = new Padding(0, S(6), 0, 0);
        txtChat.ReadOnly = true;
        txtChat.BorderStyle = BorderStyle.None;
        txtChat.BackColor = PanelBg;
        txtChat.ForeColor = Color.White;
        txtChat.Font = fText;

        var inputRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Bg
        };
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        inputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(110)));
        inputRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        txtInput.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        txtInput.Margin = new Padding(0, 0, S(8), 0);
        txtInput.BorderStyle = BorderStyle.FixedSingle;
        txtInput.BackColor = InputBg;
        txtInput.ForeColor = Color.White;
        txtInput.Font = new Font("Segoe UI", 11);
        txtInput.KeyDown += (s, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                SendChat();
            }
        };

        btnSend.Click += (s, e) => SendChat();

        inputRow.Controls.Add(txtInput, 0, 0);
        inputRow.Controls.Add(btnSend, 1, 0);

        right.Controls.Add(lblStatus, 0, 0);
        right.Controls.Add(txtChat, 0, 1);
        right.Controls.Add(inputRow, 0, 2);
        body.Controls.Add(right, 1, 0);
    }

    void PaintHeader(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = header.ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        using (var br = new LinearGradientBrush(r, Purple, Pink, LinearGradientMode.Horizontal))
            g.FillRectangle(br, r);

        TextRenderer.DrawText(g, "LAN GAME HUB", fTitle,
            new Rectangle(S(16), S(6), r.Width, S(44)), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Chat and play with players on your Wi-Fi", fSmall,
            new Rectangle(S(18), S(48), r.Width, S(24)), Color.FromArgb(255, 235, 245),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "You: " + net.MyName, fBold,
            new Rectangle(0, S(16), r.Width - S(18), S(28)), Color.White,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Your IP: " + myIp, fSmall,
            new Rectangle(0, S(44), r.Width - S(18), S(24)), Color.FromArgb(255, 235, 245),
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
    }

    void DrawPeerItem(object sender, DrawItemEventArgs e)
    {
        if (e.Index < 0) return;
        var p = (Peer)lstPeers.Items[e.Index];
        bool sel = (e.State & DrawItemState.Selected) != 0;
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(sel ? Purple : PanelBg))
            g.FillRectangle(bg, e.Bounds);

        int d = S(38);
        var circle = new Rectangle(e.Bounds.X + S(10), e.Bounds.Y + (e.Bounds.Height - d) / 2, d, d);
        int sum = 0;
        foreach (char ch in p.Name) sum += ch;
        using (var cb = new SolidBrush(AvatarColors[sum % AvatarColors.Length]))
            g.FillEllipse(cb, circle);
        TextRenderer.DrawText(g, (p.Name + "?").Substring(0, 1).ToUpper(), fBold, circle, Color.White,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        int tx = circle.Right + S(12);
        int tw = e.Bounds.Right - tx - S(6);
        TextRenderer.DrawText(g, p.Name, fBold,
            new Rectangle(tx, e.Bounds.Y + S(7), tw, S(22)), Color.White,
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, p.Ip + (games.ContainsKey(p.Id) ? "  -  playing with you" : ""), fSmall,
            new Rectangle(tx, e.Bounds.Y + S(29), tw, S(20)),
            sel ? Color.FromArgb(230, 220, 255) : Color.FromArgb(170, 180, 225),
            TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    static string Ask(string prompt, string def)
    {
        using var f = new Form
        {
            Text = "LAN Game Hub",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            ClientSize = new Size(360, 140),
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Color.FromArgb(24, 26, 48)
        };
        var l = new Label
        {
            Text = prompt,
            Left = 16,
            Top = 14,
            Width = 328,
            Height = 24,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold)
        };
        var t = new TextBox
        {
            Text = def,
            Left = 16,
            Top = 46,
            Width = 328,
            Font = new Font("Segoe UI", 11),
            BackColor = Color.FromArgb(48, 53, 92),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        var ok = new Button
        {
            Text = "OK",
            Left = 244,
            Top = 88,
            Width = 100,
            Height = 36,
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(0, 190, 120),
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
            UseVisualStyleBackColor = false
        };
        ok.FlatAppearance.BorderSize = 0;
        f.Controls.AddRange(new Control[] { l, t, ok });
        f.AcceptButton = ok;
        f.ShowDialog();
        string s = t.Text.Trim();
        return s.Length == 0 ? def : s;
    }

    static string CleanName(string s)
    {
        s = s.Replace("|", "").Replace("\r", "").Replace("\n", "").Trim();
        if (s.Length == 0) s = "Player";
        return s.Length > 20 ? s.Substring(0, 20) : s;
    }

    void Ui(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }

    // ---------------------------------------------------------------- players and chat

    string NameOf(string id) => net.GetPeer(id)?.Name ?? "Player";

    string SelectedId() => (lstPeers.SelectedItem as Peer)?.Id;

    void RefreshPeers()
    {
        string selId = SelectedId();
        refreshing = true;
        lstPeers.BeginUpdate();
        lstPeers.Items.Clear();
        int idx = -1;
        foreach (var p in net.GetPeers())
        {
            int i = lstPeers.Items.Add(p);
            if (p.Id == selId) idx = i;
        }
        if (idx >= 0) lstPeers.SelectedIndex = idx;
        lstPeers.EndUpdate();
        refreshing = false;

        lblPlayers.Text = "PLAYERS ON YOUR NETWORK (" + lstPeers.Items.Count + ")";
        if (scanning)
            lblStatus.Text = "Scanning your Wi-Fi for players...";
        else if (lstPeers.Items.Count == 0)
            lblStatus.Text = "No players found. Open the app on another PC on the same Wi-Fi, then click Refresh.";
        else
            lblStatus.Text = lstPeers.Items.Count + " player(s) online. Select one to chat or play.";
        ShowHistory();
    }

    static Color ChatColor(int kind) => kind switch
    {
        0 => Color.FromArgb(100, 220, 255),
        1 => Color.FromArgb(255, 170, 210),
        _ => Color.FromArgb(160, 170, 205),
    };

    void AppendChatLine(ChatLine l)
    {
        txtChat.SelectionStart = txtChat.TextLength;
        txtChat.SelectionLength = 0;
        txtChat.SelectionColor = ChatColor(l.Kind);
        txtChat.SelectedText = l.Text + Environment.NewLine;
        txtChat.SelectionColor = txtChat.ForeColor;
        txtChat.SelectionStart = txtChat.TextLength;
        txtChat.ScrollToCaret();
    }

    void ShowHistory()
    {
        txtChat.Clear();
        string id = SelectedId();
        if (id != null && history.TryGetValue(id, out var lines))
        {
            foreach (var l in lines) AppendChatLine(l);
        }
    }

    void AddChat(string id, string text, int kind)
    {
        if (!history.TryGetValue(id, out var lines))
        {
            lines = new List<ChatLine>();
            history[id] = lines;
        }
        var line = new ChatLine(text, kind);
        lines.Add(line);

        if (SelectedId() == id)
            AppendChatLine(line);
        else
            lblStatus.Text = "New message from " + NameOf(id);
    }

    void SendChat()
    {
        string id = SelectedId();
        if (id == null)
        {
            lblStatus.Text = "Select a player first.";
            return;
        }
        string text = txtInput.Text.Trim();
        if (text.Length == 0) return;

        net.Send(id, "CHAT", text);
        AddChat(id, "Me: " + text, 0);
        txtInput.Clear();
    }

    // ---------------------------------------------------------------- invitations and games

    void InviteSelected()
    {
        string id = SelectedId();
        if (id == null)
        {
            MessageBox.Show(this, "Select a player from the list first.", "LAN Game Hub");
            return;
        }
        if (games.ContainsKey(id))
        {
            MessageBox.Show(this, "You are already playing with this player.", "LAN Game Hub");
            return;
        }
        var g = (GameInfo)cmbGame.SelectedItem;
        net.Send(id, "INVITE", g.Key);
        AddChat(id, "[You invited " + NameOf(id) + " to play " + g.Title + "]", 2);
    }

    static GameInfo Find(string key) => AllGames.FirstOrDefault(g => g.Key == key);

    void HandleMessage(string id, string type, string payload)
    {
        string name = NameOf(id);
        switch (type)
        {
            case "CHAT":
                AddChat(id, name + ": " + payload, 1);
                break;

            case "INVITE":
                {
                    var g = Find(payload);
                    if (g == null) break;
                    if (games.ContainsKey(id))
                    {
                        net.Send(id, "DECLINE", payload);
                        break;
                    }
                    var answer = MessageBox.Show(this,
                        name + " invited you to play " + g.Title + ".\nDo you accept?",
                        "Game invitation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                    if (answer == DialogResult.Yes && !games.ContainsKey(id))
                    {
                        net.Send(id, "ACCEPT", payload);
                        OpenGame(id, payload, 1);
                    }
                    else
                    {
                        net.Send(id, "DECLINE", payload);
                    }
                    break;
                }

            case "ACCEPT":
                if (!games.ContainsKey(id)) OpenGame(id, payload, 0);
                break;

            case "DECLINE":
                AddChat(id, "[" + name + " declined the game]", 2);
                break;

            case "MOVE":
                {
                    string[] p = payload.Split('|', 2);
                    if (p.Length == 2 && games.TryGetValue(id, out var gf)) gf.ReceiveMove(p[1]);
                    break;
                }

            case "QUIT":
                if (games.TryGetValue(id, out var quitGame)) quitGame.OpponentLeft();
                break;
        }
    }

    void OpenGame(string id, string key, int side)
    {
        string opp = NameOf(id);
        GameForm f = key switch
        {
            "ttt" => new TicTacToeForm(opp, side),
            "c4" => new ConnectFourForm(opp, side),
            "chess" => new ChessForm(opp, side),
            _ => new CardDuelForm(opp, side),
        };

        f.SendMove = data => net.Send(id, "MOVE", key + "|" + data);
        games[id] = f;
        lstPeers.Invalidate();
        f.FormClosed += (s, e) =>
        {
            if (games.TryGetValue(id, out var cur) && cur == f) games.Remove(id);
            lstPeers.Invalidate();
            if (!f.RemoteQuit) net.Send(id, "QUIT", key);
        };
        f.Show(this);
    }
}
