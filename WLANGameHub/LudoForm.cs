using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LanGameHub;

public class LudoForm : GameForm
{
    const int Cell = 36;
    const int BoardSize = Cell * 15;
    const int SideWidth = 220;

    static readonly string[] ColourName = { "Red", "Green", "Yellow", "Blue" };
    static readonly Color[] Colours =
    {
        Color.FromArgb(229, 57, 53),
        Color.FromArgb(67, 160, 71),
        Color.FromArgb(253, 216, 53),
        Color.FromArgb(30, 136, 229),
    };
    static readonly int[][] Pips =
    {
        new[] { 4 },
        new[] { 0, 8 },
        new[] { 0, 4, 8 },
        new[] { 0, 2, 6, 8 },
        new[] { 0, 2, 4, 6, 8 },
        new[] { 0, 2, 3, 5, 6, 8 },
    };

    readonly LudoEngine engine;
    readonly string sid;
    readonly bool host;
    readonly BufferedPanel board = new();
    readonly BufferedPanel dieBox = new();
    readonly BufferedPanel players = new();
    readonly Button btnRoll = new();
    readonly Button btnSound = new();
    readonly Label lblStatus;
    readonly Label lblInfo = new();
    readonly System.Windows.Forms.Timer rollTimer = new() { Interval = 70 };
    readonly Font starFont = new Font("Segoe UI Symbol", 20, FontStyle.Regular, GraphicsUnit.Pixel);

    int animTicks;
    int shownDice;
    bool rolling;
    bool awaiting;

    public Action<string, string> Outgoing;
    public bool IsHost => host;
    public int Seat => Side;
    public string Sid => sid;

    public LudoForm(string sid, int seat, string[] names, bool host)
        : base("Ludo", string.Join(", ", names.Where((n, i) => i != seat)), seat)
    {
        this.sid = sid;
        this.host = host;
        engine = new LudoEngine(names);

        ClientSize = new Size(BoardSize + 30 + SideWidth, BoardSize + 150);
        lblStatus = MakeStatusLabel(BoardSize + SideWidth);

        board.SetBounds(10, 56, BoardSize, BoardSize);
        board.Paint += DrawBoard;
        board.MouseClick += BoardClicked;
        Controls.Add(board);

        players.SetBounds(BoardSize + 20, 56, SideWidth, BoardSize);
        players.BackColor = TileBg;
        players.Paint += DrawPlayers;
        Controls.Add(players);

        int by = 56 + BoardSize + 12;
        int mine = engine.SeatColour[seat];

        btnRoll.SetBounds(10, by, 160, 64);
        btnRoll.Text = "ROLL DICE";
        btnRoll.Font = new Font("Segoe UI", 13, FontStyle.Bold);
        btnRoll.FlatStyle = FlatStyle.Flat;
        btnRoll.BackColor = Colours[mine];
        btnRoll.ForeColor = mine == 2 ? Color.Black : Color.White;
        btnRoll.FlatAppearance.BorderSize = 0;
        btnRoll.Cursor = Cursors.Hand;
        btnRoll.UseVisualStyleBackColor = false;
        btnRoll.Click += (s, e) => RollClicked();
        Controls.Add(btnRoll);

        dieBox.SetBounds(184, by, 64, 64);
        dieBox.BackColor = DarkBg;
        dieBox.Paint += DrawDie;
        Controls.Add(dieBox);

        lblInfo.SetBounds(260, by, BoardSize + 10 - 260, 64);
        lblInfo.Font = new Font("Segoe UI", 10.5f);
        lblInfo.ForeColor = Color.White;
        lblInfo.TextAlign = ContentAlignment.MiddleLeft;
        Controls.Add(lblInfo);

        btnSound.SetBounds(BoardSize + 20, by, SideWidth, 64);
        btnSound.Font = new Font("Segoe UI", 11, FontStyle.Bold);
        btnSound.FlatStyle = FlatStyle.Flat;
        btnSound.BackColor = Color.FromArgb(0, 145, 234);
        btnSound.ForeColor = Color.White;
        btnSound.FlatAppearance.BorderSize = 0;
        btnSound.Cursor = Cursors.Hand;
        btnSound.UseVisualStyleBackColor = false;
        btnSound.Click += (s, e) =>
        {
            Sfx.Enabled = !Sfx.Enabled;
            btnSound.Text = Sfx.Enabled ? "Sound: ON" : "Sound: OFF";
            Sfx.Play("click");
        };
        btnSound.Text = Sfx.Enabled ? "Sound: ON" : "Sound: OFF";
        Controls.Add(btnSound);

        rollTimer.Tick += RollTick;
        FormClosed += (s, e) =>
        {
            rollTimer.Stop();
            rollTimer.Dispose();
        };

        UpdateUi();
        Sfx.Play("turn");
    }

    static Rectangle CellRect(int x, int y) => new Rectangle(x * Cell, y * Cell, Cell, Cell);

    void RollClicked()
    {
        if (rolling || awaiting || !engine.CanRoll(Side)) return;
        if (host)
        {
            HostCommit(engine.RollRandom(Side));
        }
        else
        {
            awaiting = true;
            Sfx.Play("click");
            Outgoing?.Invoke("LUDO_ACT", "roll");
            UpdateUi();
        }
    }

    void BoardClicked(object sender, MouseEventArgs e)
    {
        if (rolling || awaiting || engine.Over || engine.Turn != Side || !engine.WaitMove) return;
        int best = -1;
        float bestDist = Cell * 0.5f;
        foreach (int t in engine.Movable(Side))
        {
            var p = PointOf(Side, t);
            float d = (float)Math.Sqrt((p.X - e.X) * (p.X - e.X) + (p.Y - e.Y) * (p.Y - e.Y));
            if (d <= bestDist)
            {
                bestDist = d;
                best = t;
            }
        }
        if (best < 0) return;
        if (host)
        {
            HostCommit(engine.Move(Side, best));
        }
        else
        {
            awaiting = true;
            Sfx.Play("click");
            Outgoing?.Invoke("LUDO_ACT", "move," + best);
            UpdateUi();
        }
    }

    void HostCommit(List<string> ev)
    {
        if (ev == null) return;
        string evt = LudoEngine.Summarize(ev);
        Outgoing?.Invoke("LUDO_STATE", engine.Serialize(sid, evt));
        Present(evt);
    }

    public void Receive(string type, int fromSeat, string payload)
    {
        if (IsDisposed) return;
        if (host)
        {
            if (type == "LUDO_ACT")
            {
                string[] p = payload.Split(',');
                if (p[0] == "roll")
                {
                    HostCommit(engine.RollRandom(fromSeat));
                }
                else if (p[0] == "move" && p.Length == 2 && int.TryParse(p[1], out int t))
                {
                    HostCommit(engine.Move(fromSeat, t));
                }
            }
            else if (type == "LUDO_LEAVE")
            {
                HostCommit(engine.Leave(fromSeat));
            }
        }
        else if (type == "LUDO_STATE")
        {
            string[] f = payload.Split('\t');
            if (f.Length < 12 || f[0] != sid) return;
            string evt = engine.Apply(f);
            awaiting = false;
            Present(evt);
        }
    }

    public void HostEnded()
    {
        RemoteQuit = true;
        MessageBox.Show(this, "The host closed the Ludo game.", "Game over");
        Close();
    }

    void Present(string evt)
    {
        var sounds = new List<string>();
        foreach (string x in evt.Split(','))
            if (x.Length > 0) sounds.Add(x);
        if (!engine.Over && engine.Turn == Side && !engine.WaitMove && sounds.Count > 0) sounds.Add("turn");
        Sfx.Play(sounds.ToArray());

        if (sounds.Contains("roll"))
        {
            rolling = true;
            animTicks = 0;
            rollTimer.Start();
        }
        UpdateUi();
    }

    void RollTick(object sender, EventArgs e)
    {
        animTicks++;
        if (animTicks < 8)
        {
            shownDice = Random.Shared.Next(1, 7);
        }
        else
        {
            rollTimer.Stop();
            rolling = false;
            shownDice = engine.Dice;
            UpdateUi();
        }
        dieBox.Invalidate();
    }

    public override void ReceiveMove(string data)
    {
    }

    void UpdateUi()
    {
        string colour = ColourName[engine.SeatColour[Side]];
        string turnName = engine.Names[engine.Turn];
        if (engine.Over)
            lblStatus.Text = engine.Winner == Side ? "You win the game!" : engine.Names[Math.Max(0, engine.Winner)] + " wins the game!";
        else if (engine.Turn == Side)
            lblStatus.Text = "Your turn (" + colour + ") - " + (engine.WaitMove ? "click a glowing token" : "roll the dice");
        else
            lblStatus.Text = engine.WaitMove ? turnName + " is choosing a token..." : "Waiting for " + turnName + " to roll...";

        lblInfo.Text = engine.Note.Length > 0 ? engine.Note : "You are " + colour + ". Bring all 4 tokens home!";

        if (!rolling) shownDice = engine.Dice;
        btnRoll.Enabled = engine.CanRoll(Side) && !rolling && !awaiting;
        board.Invalidate();
        players.Invalidate();
        dieBox.Invalidate();
    }

    PointF PointOf(int s, int t)
    {
        int c = engine.SeatColour[s];
        int p = engine.Pos[s, t];
        if (p < 0)
        {
            var o = LudoEngine.Origin[c];
            var y = LudoEngine.YardSpot[t];
            return new PointF((o.X + y.X) * Cell, (o.Y + y.Y) * Cell);
        }
        if (p == LudoEngine.HomeStep)
        {
            var f = LudoEngine.Finish[c];
            return new PointF(
                (f.X + 0.5f + ((t % 2) - 0.5f) * 0.5f) * Cell,
                (f.Y + 0.5f + ((t / 2) - 0.5f) * 0.5f) * Cell);
        }

        var cell = LudoEngine.CellOf(c, p);
        int count = 0, index = 0;
        for (int s2 = 0; s2 < engine.Count; s2++)
        {
            if (!engine.Active[s2]) continue;
            for (int t2 = 0; t2 < 4; t2++)
            {
                int p2 = engine.Pos[s2, t2];
                if (p2 < 0 || p2 == LudoEngine.HomeStep) continue;
                if (LudoEngine.CellOf(engine.SeatColour[s2], p2) != cell) continue;
                if (s2 == s && t2 == t) index = count;
                count++;
            }
        }
        float offset = count > 1 ? (index - (count - 1) / 2f) * 0.3f : 0f;
        return new PointF((cell.X + 0.5f + offset) * Cell, (cell.Y + 0.5f) * Cell);
    }

    void DrawPlayers(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(TileBg);
        using var head = new Font("Segoe UI", 12, FontStyle.Bold);
        using var name = new Font("Segoe UI", 11, FontStyle.Bold);
        using var small = new Font("Segoe UI", 9.5f);
        TextRenderer.DrawText(g, "PLAYERS", head, new Rectangle(12, 8, SideWidth - 24, 28), Gold,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

        int y = 46;
        for (int s = 0; s < engine.Count; s++)
        {
            var box = new Rectangle(8, y, SideWidth - 16, 78);
            bool turn = !engine.Over && engine.Turn == s && engine.Active[s];
            using (var bg = new SolidBrush(turn ? Color.FromArgb(70, 78, 130) : Color.FromArgb(36, 40, 72)))
            using (var path = RoundRect(box, 14))
                g.FillPath(bg, path);
            if (turn)
            {
                using var pen = new Pen(Gold, 3);
                using var path = RoundRect(box, 14);
                g.DrawPath(pen, path);
            }

            using (var cb = new SolidBrush(Colours[engine.SeatColour[s]]))
                g.FillEllipse(cb, box.X + 10, box.Y + 14, 26, 26);

            string label = engine.Names[s] + (s == Side ? " (You)" : "");
            TextRenderer.DrawText(g, label, name, new Rectangle(box.X + 44, box.Y + 8, box.Width - 50, 26), Color.White,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            string line = !engine.Active[s] ? "Left the game"
                : "Home: " + engine.HomeCount(s) + " / 4";
            TextRenderer.DrawText(g, line, small, new Rectangle(box.X + 44, box.Y + 34, box.Width - 50, 22),
                Color.FromArgb(190, 198, 235), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            string state = !engine.Active[s] ? "" : turn ? (engine.WaitMove ? "Choosing token" : "Rolling now") : "";
            if (engine.Over && engine.Winner == s) state = "Winner";
            TextRenderer.DrawText(g, state, small, new Rectangle(box.X + 44, box.Y + 54, box.Width - 50, 20),
                Gold, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            y += 86;
        }
    }

    void DrawBoard(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.White);

        for (int c = 0; c < 4; c++)
        {
            var o = LudoEngine.Origin[c];
            using var fill = new SolidBrush(Colours[c]);
            g.FillRectangle(fill, o.X * Cell, o.Y * Cell, 6 * Cell, 6 * Cell);
            using var white = new SolidBrush(Color.White);
            g.FillRectangle(white, (o.X + 1) * Cell, (o.Y + 1) * Cell, 4 * Cell, 4 * Cell);
            using var spot = new SolidBrush(Color.FromArgb(90, Colours[c]));
            foreach (var y in LudoEngine.YardSpot)
                g.FillEllipse(spot, (o.X + y.X - 0.5f) * Cell, (o.Y + y.Y - 0.5f) * Cell, Cell, Cell);
        }

        using var gridPen = new Pen(Color.FromArgb(120, 120, 120), 1);
        using var cellBrush = new SolidBrush(Color.White);
        foreach (var p in LudoEngine.Track)
        {
            var r = CellRect(p.X, p.Y);
            g.FillRectangle(cellBrush, r);
            g.DrawRectangle(gridPen, r);
        }

        for (int c = 0; c < 4; c++)
        {
            using var b = new SolidBrush(Colours[c]);
            var sp = LudoEngine.Track[LudoEngine.Start[c]];
            g.FillRectangle(b, CellRect(sp.X, sp.Y));
            g.DrawRectangle(gridPen, CellRect(sp.X, sp.Y));
            foreach (var h in LudoEngine.HomeCol[c])
            {
                g.FillRectangle(b, CellRect(h.X, h.Y));
                g.DrawRectangle(gridPen, CellRect(h.X, h.Y));
            }
        }

        var mid = new PointF(7.5f * Cell, 7.5f * Cell);
        PointF[][] tri =
        {
            new[] { new PointF(6 * Cell, 6 * Cell), new PointF(6 * Cell, 9 * Cell), mid },
            new[] { new PointF(6 * Cell, 6 * Cell), new PointF(9 * Cell, 6 * Cell), mid },
            new[] { new PointF(9 * Cell, 6 * Cell), new PointF(9 * Cell, 9 * Cell), mid },
            new[] { new PointF(6 * Cell, 9 * Cell), new PointF(9 * Cell, 9 * Cell), mid },
        };
        for (int c = 0; c < 4; c++)
        {
            using var b = new SolidBrush(Colours[c]);
            g.FillPolygon(b, tri[c]);
            g.DrawPolygon(gridPen, tri[c]);
        }

        using var fmt = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        foreach (int idx in LudoEngine.SafeCells)
        {
            var sc = LudoEngine.Track[idx];
            bool isStart = idx % 13 == 0;
            Color starColour = !isStart ? Color.FromArgb(150, 150, 150)
                : idx == 26 ? Color.FromArgb(120, 100, 0)
                : Color.White;
            using var sb = new SolidBrush(starColour);
            g.DrawString("★", starFont, sb, CellRect(sc.X, sc.Y), fmt);
        }

        var movable = (!engine.Over && engine.Turn == Side && engine.WaitMove && !rolling) ? engine.Movable(Side) : new List<int>();
        for (int s = 0; s < engine.Count; s++)
        {
            if (!engine.Active[s]) continue;
            for (int t = 0; t < 4; t++)
                DrawToken(g, s, t, s == Side && movable.Contains(t));
        }
    }

    void DrawToken(Graphics g, int s, int t, bool glow)
    {
        var pt = PointOf(s, t);
        float r = Cell * 0.34f;
        var colour = Colours[engine.SeatColour[s]];

        if (glow)
        {
            using var outer = new Pen(Color.Black, 2);
            using var ring = new Pen(Color.White, 3);
            g.DrawEllipse(outer, pt.X - r - 6, pt.Y - r - 6, (r + 6) * 2, (r + 6) * 2);
            g.DrawEllipse(ring, pt.X - r - 3, pt.Y - r - 3, (r + 3) * 2, (r + 3) * 2);
        }

        using var fill = new SolidBrush(colour);
        using var edge = new Pen(Color.FromArgb(40, 40, 40), 2);
        using var inner = new Pen(Color.White, 2);
        g.FillEllipse(fill, pt.X - r, pt.Y - r, r * 2, r * 2);
        g.DrawEllipse(edge, pt.X - r, pt.Y - r, r * 2, r * 2);
        g.DrawEllipse(inner, pt.X - r * 0.55f, pt.Y - r * 0.55f, r * 1.1f, r * 1.1f);
    }

    void DrawDie(object sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(DarkBg);

        var rect = new Rectangle(4, 4, 56, 56);
        int d = 16;
        using var path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        using (var white = new SolidBrush(Color.White)) g.FillPath(white, path);
        using (var rim = new Pen(Colours[engine.SeatColour[engine.DiceBy]], 4)) g.DrawPath(rim, path);

        if (shownDice < 1 || shownDice > 6)
        {
            using var q = new Font("Segoe UI", 22, FontStyle.Bold);
            using var gray = new SolidBrush(Color.FromArgb(150, 150, 150));
            using var f = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            g.DrawString("?", q, gray, rect, f);
            return;
        }

        using var pip = new SolidBrush(Color.FromArgb(30, 30, 30));
        foreach (int cellIndex in Pips[shownDice - 1])
        {
            int cx = rect.X + 14 + (cellIndex % 3) * 14;
            int cy = rect.Y + 14 + (cellIndex / 3) * 14;
            g.FillEllipse(pip, cx - 5, cy - 5, 10, 10);
        }
    }
}
