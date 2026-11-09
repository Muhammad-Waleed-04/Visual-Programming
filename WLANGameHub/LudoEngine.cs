using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace LanGameHub;

public class LudoEngine
{
    public const int HomeStep = 56;

    public static readonly int[] Start = { 0, 13, 26, 39 };
    public static readonly int[] SafeCells = { 0, 8, 13, 21, 26, 34, 39, 47 };
    public static readonly Point[] Origin = { new Point(0, 0), new Point(9, 0), new Point(9, 9), new Point(0, 9) };
    public static readonly PointF[] YardSpot =
    {
        new PointF(2, 2), new PointF(4, 2), new PointF(2, 4), new PointF(4, 4)
    };
    public static readonly Point[] Track = BuildTrack();
    public static readonly Point[][] HomeCol =
    {
        new[] { new Point(1, 7), new Point(2, 7), new Point(3, 7), new Point(4, 7), new Point(5, 7) },
        new[] { new Point(7, 1), new Point(7, 2), new Point(7, 3), new Point(7, 4), new Point(7, 5) },
        new[] { new Point(13, 7), new Point(12, 7), new Point(11, 7), new Point(10, 7), new Point(9, 7) },
        new[] { new Point(7, 13), new Point(7, 12), new Point(7, 11), new Point(7, 10), new Point(7, 9) },
    };
    public static readonly Point[] Finish = { new Point(6, 7), new Point(7, 6), new Point(8, 7), new Point(7, 8) };

    public readonly int Count;
    public readonly int[] SeatColour;
    public readonly string[] Names;
    public readonly int[,] Pos = new int[4, 4];
    public readonly bool[] Active = new bool[4];
    public int Turn;
    public int Dice;
    public int DiceBy;
    public int Sixes;
    public int Winner = -1;
    public bool WaitMove;
    public bool Over;
    public string Note = "";

    readonly Random rnd = new Random();

    public LudoEngine(IList<string> names)
    {
        Count = Math.Max(2, Math.Min(4, names.Count));
        SeatColour = ColoursFor(Count);
        Names = new string[4];
        for (int i = 0; i < 4; i++) Names[i] = i < names.Count ? names[i] : "";
        for (int s = 0; s < 4; s++)
            for (int t = 0; t < 4; t++)
                Pos[s, t] = -1;
        for (int i = 0; i < Count; i++) Active[i] = true;
    }

    public static int[] ColoursFor(int n)
    {
        if (n <= 2) return new[] { 0, 2 };
        if (n == 3) return new[] { 0, 1, 2 };
        return new[] { 0, 1, 2, 3 };
    }

    static Point[] BuildTrack()
    {
        var t = new List<Point>();
        for (int c = 1; c <= 5; c++) t.Add(new Point(c, 6));
        for (int r = 5; r >= 0; r--) t.Add(new Point(6, r));
        t.Add(new Point(7, 0));
        t.Add(new Point(8, 0));
        for (int r = 1; r <= 5; r++) t.Add(new Point(8, r));
        for (int c = 9; c <= 14; c++) t.Add(new Point(c, 6));
        t.Add(new Point(14, 7));
        t.Add(new Point(14, 8));
        for (int c = 13; c >= 9; c--) t.Add(new Point(c, 8));
        for (int r = 9; r <= 14; r++) t.Add(new Point(8, r));
        t.Add(new Point(7, 14));
        t.Add(new Point(6, 14));
        for (int r = 13; r >= 9; r--) t.Add(new Point(6, r));
        for (int c = 5; c >= 0; c--) t.Add(new Point(c, 8));
        t.Add(new Point(0, 7));
        t.Add(new Point(0, 6));
        return t.ToArray();
    }

    public static Point CellOf(int colour, int p)
    {
        if (p <= 50) return Track[(Start[colour] + p) % 52];
        return HomeCol[colour][p - 51];
    }

    public int AbsCell(int seat, int p)
    {
        return (Start[SeatColour[seat]] + p) % 52;
    }

    public int HomeCount(int seat)
    {
        int n = 0;
        for (int t = 0; t < 4; t++)
            if (Pos[seat, t] == HomeStep) n++;
        return n;
    }

    public List<int> Movable(int seat)
    {
        var list = new List<int>();
        if (seat < 0 || seat >= Count) return list;
        for (int t = 0; t < 4; t++)
        {
            int p = Pos[seat, t];
            if (p == HomeStep) continue;
            if (p == -1)
            {
                if (Dice == 6) list.Add(t);
            }
            else if (p + Dice <= HomeStep)
            {
                list.Add(t);
            }
        }
        return list;
    }

    public bool CanRoll(int seat)
    {
        return !Over && seat >= 0 && seat < Count && Active[seat] && Turn == seat && !WaitMove;
    }

    public List<string> RollRandom(int seat)
    {
        return Roll(seat, rnd.Next(1, 7));
    }

    public List<string> Roll(int seat, int v)
    {
        if (!CanRoll(seat) || v < 1 || v > 6) return null;
        var ev = new List<string> { "roll" };
        Dice = v;
        DiceBy = seat;
        string who = Names[seat];
        if (v == 6) Sixes++;
        else Sixes = 0;

        if (Sixes == 3)
        {
            Note = who + " rolled three sixes in a row. Turn lost.";
            ev.Add("pass");
            PassTurn();
            return ev;
        }

        var list = Movable(seat);
        if (list.Count == 0)
        {
            Note = who + " rolled " + v + ". No move possible.";
            ev.Add("pass");
            PassTurn();
            return ev;
        }

        WaitMove = true;
        Note = who + " rolled " + v + ".";
        if (list.Count == 1) ApplyMove(seat, list[0], ev);
        return ev;
    }

    public List<string> Move(int seat, int token)
    {
        if (Over || seat != Turn || !WaitMove || token < 0 || token > 3) return null;
        if (!Movable(seat).Contains(token)) return null;
        var ev = new List<string>();
        ApplyMove(seat, token, ev);
        return ev;
    }

    void ApplyMove(int s, int t, List<string> ev)
    {
        int from = Pos[s, t];
        int to = from == -1 ? 0 : from + Dice;
        Pos[s, t] = to;
        ev.Add(from == -1 ? "out" : "move");

        int captured = 0;
        if (to <= 50)
        {
            int a = AbsCell(s, to);
            if (Array.IndexOf(SafeCells, a) < 0)
            {
                for (int o = 0; o < Count; o++)
                {
                    if (o == s || !Active[o]) continue;
                    for (int k = 0; k < 4; k++)
                    {
                        int q = Pos[o, k];
                        if (q >= 0 && q <= 50 && AbsCell(o, q) == a)
                        {
                            Pos[o, k] = -1;
                            captured++;
                        }
                    }
                }
            }
        }

        var parts = new List<string>();
        if (captured > 0)
        {
            parts.Add("captured " + captured + (captured == 1 ? " token" : " tokens"));
            ev.Add("capture");
        }
        if (to == HomeStep)
        {
            parts.Add("got a token home");
            ev.Add("home");
        }
        Note = Names[s] + " rolled " + Dice + (parts.Count > 0 ? " and " + string.Join(" and ", parts) : "") + ".";

        if (HomeCount(s) == 4)
        {
            Over = true;
            Winner = s;
            WaitMove = false;
            ev.Add("win");
            Note += " " + Names[s] + " wins!";
        }
        else if (Dice == 6)
        {
            WaitMove = false;
            Note += " Extra turn.";
        }
        else
        {
            PassTurn();
        }
    }

    void PassTurn()
    {
        Sixes = 0;
        WaitMove = false;
        for (int i = 1; i <= Count; i++)
        {
            int c = (Turn + i) % Count;
            if (Active[c])
            {
                Turn = c;
                return;
            }
        }
    }

    public List<string> Leave(int seat)
    {
        if (seat < 0 || seat >= Count || !Active[seat]) return null;
        var ev = new List<string>();
        Active[seat] = false;
        for (int k = 0; k < 4; k++) Pos[seat, k] = -1;

        int left = 0, last = -1;
        for (int i = 0; i < Count; i++)
        {
            if (Active[i])
            {
                left++;
                last = i;
            }
        }

        if (Over) return ev;

        if (left < 2)
        {
            Over = true;
            Winner = last;
            WaitMove = false;
            ev.Add("win");
            Note = Names[seat] + " left the game." + (last >= 0 ? " " + Names[last] + " wins!" : "");
        }
        else
        {
            Note = Names[seat] + " left the game.";
            if (Turn == seat) PassTurn();
        }
        return ev;
    }

    public static string Summarize(List<string> ev)
    {
        string move = ev.Contains("win") ? "win"
            : ev.Contains("home") ? "home"
            : ev.Contains("capture") ? "capture"
            : ev.Contains("out") ? "out"
            : ev.Contains("move") ? "move"
            : ev.Contains("pass") ? "pass"
            : "";
        string roll = ev.Contains("roll") ? "roll" : "";
        return string.Join(",", new[] { roll, move }.Where(x => x.Length > 0));
    }

    public string Serialize(string sid, string evt)
    {
        var p = new List<string>();
        for (int i = 0; i < 16; i++) p.Add(Pos[i / 4, i % 4].ToString());
        string act = new string(Enumerable.Range(0, 4).Select(i => Active[i] ? '1' : '0').ToArray());
        return string.Join("\t", new[]
        {
            sid, Turn.ToString(), Dice.ToString(), DiceBy.ToString(), WaitMove ? "1" : "0", Over ? "1" : "0",
            Winner.ToString(), Sixes.ToString(), evt, string.Join(",", p), act, Note
        });
    }

    static int I(string s)
    {
        return int.TryParse(s, out int v) ? v : 0;
    }

    public string Apply(string[] f)
    {
        if (f.Length < 12) return "";
        Turn = Math.Max(0, Math.Min(Count - 1, I(f[1])));
        Dice = Math.Max(0, Math.Min(6, I(f[2])));
        DiceBy = Math.Max(0, Math.Min(Count - 1, I(f[3])));
        WaitMove = f[4] == "1";
        Over = f[5] == "1";
        Winner = I(f[6]);
        Sixes = I(f[7]);
        string[] pp = f[9].Split(',');
        for (int i = 0; i < 16 && i < pp.Length; i++) Pos[i / 4, i % 4] = Math.Max(-1, Math.Min(HomeStep, I(pp[i])));
        for (int i = 0; i < 4 && i < f[10].Length; i++) Active[i] = f[10][i] == '1' && i < Count;
        Note = string.Join("\t", f.Skip(11));
        return f[8];
    }
}
