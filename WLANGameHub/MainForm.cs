using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Windows.Forms;

namespace LanGameHub;

public class MainForm : Form
{
    record GameInfo(string Key, string Title)
    {
        public override string ToString() => Title;
    }

    record ChatLine(string Text, int Kind);

    static readonly GameInfo[] AllGames =
    {
        new GameInfo("ttt", "Tic Tac Toe"),
        new GameInfo("c4", "Connect Four"),
        new GameInfo("chess", "Chess"),
        new GameInfo("cards", "Card Duel"),
        new GameInfo("ludo", "Ludo"),
    };

    static readonly Color Bg = Color.FromArgb(24, 26, 48);
    static readonly Color PanelBg = Color.FromArgb(36, 40, 72);
    static readonly Color InputBg = Color.FromArgb(48, 53, 92);
    static readonly Color Purple = Color.FromArgb(124, 77, 255);
    static readonly Color Pink = Color.FromArgb(255, 64, 129);
    static readonly Color Green = Color.FromArgb(0, 190, 120);
    static readonly Color Blue = Color.FromArgb(0, 145, 234);
    static readonly Color Teal = Color.FromArgb(0, 172, 193);
    static readonly Color Orange = Color.FromArgb(255, 143, 0);
    static readonly Color Amber = Color.FromArgb(255, 213, 79);
    static readonly Color Cyan = Color.FromArgb(0, 229, 255);
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
    readonly Button btnWifi;
    readonly ComboBox cmbGame = new();
    readonly ComboBox cmbNet = new();
    readonly Label lblStatus = new();
    readonly Label lblPlayers = new();
    readonly ToolTip tips = new();
    readonly System.Windows.Forms.Timer netTimer = new() { Interval = 1500 };
    NetworkAddressChangedEventHandler netHandler;

    NetIf current;
    bool refreshing;
    bool loadingNets;
    bool scanning;
    int scanId;

    int S(int v) => (int)Math.Round(v * DeviceDpi / 96.0);

    string NetTitle => current == null ? "your network" : current.Title;

    public MainForm()
    {
        Text = "WLAN Game Hub";
        BackColor = Bg;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(S(920), S(690));
        MinimumSize = new Size(S(800), S(620));

        net.MyName = CleanName(Ask("Enter your player name:", Environment.MachineName));

        btnName = MakeButton("Change name", Purple);
        btnRefresh = MakeButton("Refresh", Orange);
        btnWifi = MakeButton("Wi-Fi", Teal);
        btnInvite = MakeButton("Invite to play", Green);
        btnSend = MakeButton("Send", Blue);

        BuildUi();
        LoadNetworks();

        net.PeersChanged += () => Ui(RefreshPeers);
        net.MessageReceived += (id, type, payload) => Ui(() => HandleMessage(id, type, payload));
        net.SendFailed += id => Ui(() => AddChat(id, "[Could not reach " + NameOf(id) + "]", 2));

        netHandler = (s, e) => Ui(() =>
        {
            netTimer.Stop();
            netTimer.Start();
        });
        NetworkChange.NetworkAddressChanged += netHandler;
        netTimer.Tick += (s, e) =>
        {
            netTimer.Stop();
            LoadNetworks();
            net.Refresh();
            StartScan();
        };

        Shown += (s, e) =>
        {
            try
            {
                net.Start();
                RefreshPeers();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Network could not start:\n" + ex.Message, "Error");
            }
        };
        FormClosing += (s, e) =>
        {
            if (e.CloseReason != CloseReason.WindowsShutDown && games.Values.Any(g => !g.IsDisposed))
            {
                e.Cancel = true;
                MessageBox.Show(this, "You cannot close the application while a game is running.",
                    "WLAN Game Hub", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            NetworkChange.NetworkAddressChanged -= netHandler;
            netTimer.Stop();
            net.Dispose();
        };
    }


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

    Label SectionLabel(string text)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            Font = fBold,
            ForeColor = Cyan,
            TextAlign = ContentAlignment.BottomLeft,
            Margin = Padding.Empty
        };
    }

    void BuildUi()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, BackColor = Bg };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(80)));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, S(32)));
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
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(310)));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(body, 0, 1);

        var credits = new Label
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Font = fSmall,
            ForeColor = Amber,
            BackColor = Color.FromArgb(18, 20, 38),
            TextAlign = ContentAlignment.MiddleCenter,
            Text = "Developed by Waleed and Sir Muhammad Abdullah  |  WLAN Game Hub  |  Visual Programming Project"
        };
        root.Controls.Add(credits, 0, 2);

        var left = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            Margin = new Padding(0, 0, S(12), 0),
            BackColor = Bg
        };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(30)));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(40)));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(48)));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(34)));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(46)));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, S(60)));

        var netRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            BackColor = Bg
        };
        netRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        netRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, S(76)));
        netRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        cmbNet.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        cmbNet.Margin = new Padding(0, 0, S(6), 0);
        cmbNet.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbNet.FlatStyle = FlatStyle.Flat;
        cmbNet.BackColor = InputBg;
        cmbNet.ForeColor = Color.White;
        cmbNet.Font = fText;
        cmbNet.DropDownWidth = S(430);
        cmbNet.SelectedIndexChanged += (s, e) =>
        {
            if (loadingNets) return;
            current = cmbNet.SelectedItem as NetIf;
            net.SelectInterface(current);
            header.Invalidate();
            StartScan();
        };

        btnWifi.Margin = new Padding(0, S(4), 0, S(4));
        btnWifi.Click += (s, e) => OpenWifiList();
        tips.SetToolTip(btnWifi, "Open the Windows Wi-Fi list to connect to another Wi-Fi");
        tips.SetToolTip(cmbNet, "Choose the Wi-Fi / LAN you want to play on");

        netRow.Controls.Add(cmbNet, 0, 0);
        netRow.Controls.Add(btnWifi, 1, 0);

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

        btnName.Click += (s, e) =>
        {
            net.MyName = CleanName(Ask("Enter your player name:", net.MyName));
            header.Invalidate();
        };

        btnRefresh.Click += (s, e) =>
        {
            LoadNetworks();
            net.Refresh();
            StartScan();
        };

        lblPlayers.Text = "PLAYERS ON THIS NETWORK (0)";
        lblPlayers.Dock = DockStyle.Fill;
        lblPlayers.Font = fBold;
        lblPlayers.ForeColor = Cyan;
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

        left.Controls.Add(SectionLabel("WI-FI / NETWORK"), 0, 0);
        left.Controls.Add(netRow, 0, 1);
        left.Controls.Add(topRow, 0, 2);
        left.Controls.Add(lblPlayers, 0, 3);
        left.Controls.Add(lstPeers, 0, 4);
        left.Controls.Add(cmbGame, 0, 5);
        left.Controls.Add(btnInvite, 0, 6);
        body.Controls.Add(left, 0, 0);

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


    void LoadNetworks()
    {
        string keepIp = (cmbNet.SelectedItem as NetIf)?.Ip;
        var list = NetworkService.GetInterfaces();

        loadingNets = true;
        cmbNet.Items.Clear();
        foreach (var n in list) cmbNet.Items.Add(n);

        int idx = list.FindIndex(n => n.Ip == keepIp);
        if (idx < 0 && list.Count > 0) idx = 0;
        if (idx >= 0) cmbNet.SelectedIndex = idx;
        loadingNets = false;

        current = idx >= 0 ? list[idx] : null;
        net.SelectInterface(current, false);
        header.Invalidate();
    }

    void OpenWifiList()
    {
        try
        {
            Process.Start(new ProcessStartInfo("ms-availablenetworks:") { UseShellExecute = true });
        }
        catch
        {
            try
            {
                Process.Start(new ProcessStartInfo("ms-settings:network-wifi") { UseShellExecute = true });
            }
            catch
            {
                MessageBox.Show(this, "Open the Wi-Fi icon in the Windows taskbar to choose a Wi-Fi.", "WLAN Game Hub");
            }
        }
    }

    async void StartScan()
    {
        int my = ++scanId;
        scanning = true;
        btnRefresh.Enabled = false;
        lblStatus.Text = "Scanning " + NetTitle + " for players...";
        await System.Threading.Tasks.Task.Delay(2500);
        if (my != scanId) return;
        scanning = false;
        btnRefresh.Enabled = true;
        RefreshPeers();
    }


    void PaintHeader(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        var r = header.ClientRectangle;
        if (r.Width <= 0 || r.Height <= 0) return;

        using (var br = new LinearGradientBrush(r, Purple, Pink, LinearGradientMode.Horizontal))
            g.FillRectangle(br, r);

        TextRenderer.DrawText(g, "WLAN GAME HUB", fTitle,
            new Rectangle(S(16), S(6), r.Width, S(44)), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, "Chat and play with players on your Wi-Fi", fSmall,
            new Rectangle(S(18), S(48), r.Width, S(24)), Color.FromArgb(255, 235, 245),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        string netLine = current == null
            ? "Not connected to any Wi-Fi or LAN"
            : (current.IsWifi ? "Wi-Fi: " : "Network: ") + current.Title + "   |   IP " + current.Ip;

        TextRenderer.DrawText(g, "You: " + net.MyName, fBold,
            new Rectangle(0, S(16), r.Width - S(18), S(28)), Color.White,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(g, netLine, fSmall,
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
            Text = "WLAN Game Hub",
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
        s = s.Replace("|", "").Replace("\r", "").Replace("\n", "").Replace("\t", " ").Trim();
        if (s.Length == 0) s = "Player";
        return s.Length > 20 ? s.Substring(0, 20) : s;
    }

    void Ui(Action a)
    {
        if (IsHandleCreated && !IsDisposed) BeginInvoke(a);
    }


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

        lblPlayers.Text = "PLAYERS ON THIS NETWORK (" + lstPeers.Items.Count + ")";
        if (current == null)
            lblStatus.Text = "You are not connected to any Wi-Fi or LAN. Connect, then click Refresh.";
        else if (scanning)
            lblStatus.Text = "Scanning " + NetTitle + " for players...";
        else if (lstPeers.Items.Count == 0)
            lblStatus.Text = "No players found on " + NetTitle + ". Open the app on another PC on the same Wi-Fi, then click Refresh.";
        else
            lblStatus.Text = lstPeers.Items.Count + " player(s) on " + NetTitle + ". Select one to chat or play.";
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


    void InviteSelected()
    {
        if (((GameInfo)cmbGame.SelectedItem).Key == "ludo")
        {
            InviteLudo();
            return;
        }
        string id = SelectedId();
        if (id == null)
        {
            MessageBox.Show(this, "Select a player from the list first.", "WLAN Game Hub");
            return;
        }
        if (games.ContainsKey(id))
        {
            MessageBox.Show(this, "You are already playing with this player.", "WLAN Game Hub");
            return;
        }
        var g = (GameInfo)cmbGame.SelectedItem;
        net.Send(id, "INVITE", g.Key);
        AddChat(id, "[You invited " + NameOf(id) + " to play " + g.Title + "]", 2);
    }

    class LudoPending
    {
        public List<string> Invited = new();
        public List<string> Accepted = new();
        public HashSet<string> Answered = new();
    }

    readonly Dictionary<string, LudoPending> ludoPending = new();
    readonly Dictionary<LudoForm, string[]> ludoSeats = new();

    List<Peer> PickPlayers()
    {
        var peers = net.GetPeers().Where(p => !games.ContainsKey(p.Id)).ToList();
        if (peers.Count == 0)
        {
            MessageBox.Show(this, "No free players found on the network.", "Ludo");
            return null;
        }
        using var f = new Form
        {
            Text = "Ludo - choose players",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            BackColor = Bg,
            ClientSize = new Size(S(380), S(330))
        };
        var l = new Label
        {
            Text = "Select 1 to 3 players to invite:",
            Left = S(16),
            Top = S(12),
            Width = S(348),
            Height = S(26),
            ForeColor = Color.White,
            Font = fBold
        };
        var list = new CheckedListBox
        {
            Left = S(16),
            Top = S(44),
            Width = S(348),
            Height = S(200),
            BackColor = PanelBg,
            ForeColor = Color.White,
            BorderStyle = BorderStyle.None,
            Font = fText,
            CheckOnClick = true
        };
        foreach (var p in peers) list.Items.Add(p);
        list.ItemCheck += (s, e) =>
        {
            if (e.NewValue == CheckState.Checked && list.CheckedItems.Count >= 3) e.NewValue = CheckState.Unchecked;
        };
        var ok = new Button
        {
            Text = "Send invites",
            Left = S(204),
            Top = S(268),
            Width = S(160),
            Height = S(42),
            DialogResult = DialogResult.OK,
            FlatStyle = FlatStyle.Flat,
            BackColor = Green,
            ForeColor = Color.White,
            Font = fBold,
            UseVisualStyleBackColor = false
        };
        ok.FlatAppearance.BorderSize = 0;
        var cancel = new Button
        {
            Text = "Cancel",
            Left = S(16),
            Top = S(268),
            Width = S(120),
            Height = S(42),
            DialogResult = DialogResult.Cancel,
            FlatStyle = FlatStyle.Flat,
            BackColor = Purple,
            ForeColor = Color.White,
            Font = fBold,
            UseVisualStyleBackColor = false
        };
        cancel.FlatAppearance.BorderSize = 0;
        f.Controls.AddRange(new Control[] { l, list, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        if (f.ShowDialog(this) != DialogResult.OK) return null;
        return list.CheckedItems.Cast<Peer>().ToList();
    }

    async void InviteLudo()
    {
        var chosen = PickPlayers();
        if (chosen == null) return;
        if (chosen.Count == 0)
        {
            MessageBox.Show(this, "Select at least one player.", "Ludo");
            return;
        }
        string sid = Guid.NewGuid().ToString("N").Substring(0, 8);
        var pend = new LudoPending();
        ludoPending[sid] = pend;
        foreach (var p in chosen)
        {
            pend.Invited.Add(p.Id);
            net.Send(p.Id, "LUDO_INV", sid);
            AddChat(p.Id, "[You invited " + p.Name + " to play Ludo with " + chosen.Count + " other player(s)]", 2);
        }
        await System.Threading.Tasks.Task.Delay(45000);
        if (ludoPending.ContainsKey(sid)) StartLudo(sid);
    }

    void OnLudoInvite(string id, string sid)
    {
        if (games.ContainsKey(id))
        {
            net.Send(id, "LUDO_DEC", sid);
            return;
        }
        var answer = MessageBox.Show(this,
            NameOf(id) + " invited you to play Ludo (up to 4 players).\nDo you accept?",
            "Game invitation", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        net.Send(id, answer == DialogResult.Yes ? "LUDO_ACC" : "LUDO_DEC", sid);
    }

    void OnLudoAnswer(string id, string sid, bool accepted)
    {
        if (!ludoPending.TryGetValue(sid, out var pend) || !pend.Invited.Contains(id)) return;
        if (!pend.Answered.Add(id)) return;
        if (accepted) pend.Accepted.Add(id);
        else AddChat(id, "[" + NameOf(id) + " declined the game]", 2);
        if (pend.Answered.Count == pend.Invited.Count) StartLudo(sid);
    }

    void StartLudo(string sid)
    {
        if (!ludoPending.Remove(sid, out var pend)) return;
        var ids = pend.Accepted.Where(i => net.GetPeer(i) != null && !games.ContainsKey(i)).ToList();
        if (ids.Count == 0)
        {
            MessageBox.Show(this, "No player accepted the Ludo invitation.", "Ludo");
            return;
        }
        var names = new List<string> { CleanName(net.MyName) };
        foreach (var i in ids) names.Add(CleanName(NameOf(i)));
        var seats = new string[names.Count];
        seats[0] = "";
        for (int k = 0; k < ids.Count; k++)
        {
            seats[k + 1] = ids[k];
            net.Send(ids[k], "LUDO_START", sid + "|" + (k + 1) + "|" + string.Join("\t", names));
        }
        var f = new LudoForm(sid, 0, names.ToArray(), true);
        ludoSeats[f] = seats;
        f.Outgoing = (type, payload) =>
        {
            foreach (var pid in seats)
                if (pid.Length > 0) net.Send(pid, type, payload);
        };
        foreach (var pid in ids) games[pid] = f;
        lstPeers.Invalidate();
        f.FormClosed += (s, e) =>
        {
            ludoSeats.Remove(f);
            foreach (var pid in seats)
            {
                if (pid.Length == 0) continue;
                if (games.TryGetValue(pid, out var cur) && cur == f) games.Remove(pid);
                if (!f.RemoteQuit) net.Send(pid, "LUDO_END", sid);
            }
            lstPeers.Invalidate();
        };
        f.Show(this);
    }

    void OnLudoStart(string hostId, string payload)
    {
        string[] p = payload.Split('|', 3);
        if (p.Length != 3 || !int.TryParse(p[1], out int seat) || games.ContainsKey(hostId)) return;
        string[] names = p[2].Split('\t');
        if (seat < 1 || seat >= names.Length || names.Length > 4) return;
        string sid = p[0];
        var f = new LudoForm(sid, seat, names, false);
        f.Outgoing = (type, data) => net.Send(hostId, type, data);
        games[hostId] = f;
        lstPeers.Invalidate();
        f.FormClosed += (s, e) =>
        {
            if (games.TryGetValue(hostId, out var cur) && cur == f) games.Remove(hostId);
            lstPeers.Invalidate();
            if (!f.RemoteQuit) net.Send(hostId, "LUDO_LEAVE", sid);
        };
        f.Show(this);
    }

    void OnLudoGame(string id, string type, string payload)
    {
        if (!games.TryGetValue(id, out var g) || g is not LudoForm lf) return;
        if (type == "LUDO_END")
        {
            if (!lf.IsHost && payload == lf.Sid) lf.HostEnded();
        }
        else if (lf.IsHost)
        {
            if (ludoSeats.TryGetValue(lf, out var seats))
            {
                int seat = Array.IndexOf(seats, id);
                if (seat > 0) lf.Receive(type, seat, payload);
            }
        }
        else
        {
            lf.Receive(type, 0, payload);
        }
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
                    if (g == null || g.Key == "ludo") break;
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

            case "GCHAT":
                if (games.TryGetValue(id, out var chatGame)) chatGame.ReceiveChat(payload);
                break;

            case "QUIT":
                if (games.TryGetValue(id, out var quitGame) && quitGame is not LudoForm) quitGame.OpponentLeft();
                break;

            case "LUDO_INV":
                OnLudoInvite(id, payload);
                break;

            case "LUDO_ACC":
            case "LUDO_DEC":
                OnLudoAnswer(id, payload, type == "LUDO_ACC");
                break;

            case "LUDO_START":
                OnLudoStart(id, payload);
                break;

            case "LUDO_STATE":
            case "LUDO_ACT":
            case "LUDO_LEAVE":
            case "LUDO_END":
                OnLudoGame(id, type, payload);
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
        f.SendChat = text => net.Send(id, "GCHAT", text);
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
