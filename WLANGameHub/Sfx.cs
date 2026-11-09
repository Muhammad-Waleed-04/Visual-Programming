using System;
using System.Collections.Generic;
using System.IO;
using System.Media;

namespace LanGameHub;

public static class Sfx
{
    const int Rate = 22050;

    static readonly Dictionary<string, short[]> parts = new Dictionary<string, short[]>();
    static readonly Dictionary<string, byte[]> cache = new Dictionary<string, byte[]>();
    static readonly object gate = new object();
    static SoundPlayer current;

    public static bool Enabled = true;

    static Sfx()
    {
        var rnd = new Random(7);
        parts["click"] = Tone(1500, 900, 35, 0.5, 6, 0);
        parts["roll"] = BuildRoll(rnd);
        parts["move"] = Join(Tone(950, 520, 60, 0.55, 5, 0), Silence(8), Tone(660, 660, 50, 0.35, 6, 0));
        parts["out"] = Tone(420, 1050, 170, 0.5, 2, 0);
        parts["capture"] = Mix(Tone(820, 150, 280, 0.5, 2.2, 1), Noise(280, 0.35, rnd, 3));
        parts["home"] = Notes(90, 523, 659, 784, 1047);
        parts["win"] = Join(Notes(110, 523, 659, 784, 1047), Silence(30), Notes(110, 784, 1047), Tone(1319, 1319, 480, 0.5, 2.5, 0));
        parts["turn"] = Join(Tone(880, 880, 100, 0.35, 4, 0), Tone(1175, 1175, 140, 0.35, 4, 0));
        parts["pass"] = Tone(320, 200, 170, 0.45, 2, 1);
    }

    public static void Play(params string[] names)
    {
        if (!Enabled || names == null || names.Length == 0) return;
        try
        {
            string key = string.Join("+", names);
            byte[] wav;
            lock (gate)
            {
                if (!cache.TryGetValue(key, out wav))
                {
                    var all = new List<short>();
                    foreach (string n in names)
                    {
                        if (!parts.TryGetValue(n, out short[] p)) continue;
                        if (all.Count > 0) all.AddRange(Silence(70));
                        all.AddRange(p);
                    }
                    if (all.Count == 0) return;
                    wav = ToWav(all.ToArray());
                    cache[key] = wav;
                }
            }
            var player = new SoundPlayer(new MemoryStream(wav));
            player.Play();
            SoundPlayer old;
            lock (gate)
            {
                old = current;
                current = player;
            }
            if (old != null) old.Dispose();
        }
        catch
        {
        }
    }

    static short[] Tone(double f0, double f1, int ms, double vol, double decay, int wave)
    {
        int n = Rate * ms / 1000;
        var d = new short[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double f = f0 + (f1 - f0) * t;
            phase += 2 * Math.PI * f / Rate;
            double s = Math.Sin(phase);
            if (wave == 1) s = s >= 0 ? 0.7 : -0.7;
            double env = Math.Exp(-decay * t);
            env *= Math.Min(1.0, i / (Rate * 0.004));
            env *= Math.Min(1.0, (n - i) / (Rate * 0.006));
            d[i] = (short)(s * env * vol * 30000);
        }
        return d;
    }

    static short[] Noise(int ms, double vol, Random rnd, double decay)
    {
        int n = Rate * ms / 1000;
        var d = new short[n];
        double prev = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;
            double w = rnd.NextDouble() * 2 - 1;
            prev = prev * 0.55 + w * 0.45;
            double env = Math.Exp(-decay * t);
            env *= Math.Min(1.0, (n - i) / (Rate * 0.006));
            d[i] = (short)(prev * env * vol * 30000);
        }
        return d;
    }

    static short[] BuildRoll(Random rnd)
    {
        var all = new List<short>();
        for (int i = 0; i < 9; i++)
        {
            double f = 500 + rnd.Next(0, 700);
            all.AddRange(Mix(Tone(f, f * 0.5, 38, 0.5, 7, 0), Noise(38, 0.35, rnd, 9)));
            all.AddRange(Silence(18 + i * 6));
        }
        return all.ToArray();
    }

    static short[] Notes(int ms, params double[] freqs)
    {
        var all = new List<short>();
        foreach (double f in freqs) all.AddRange(Tone(f, f, ms, 0.5, 3, 0));
        return all.ToArray();
    }

    static short[] Silence(int ms)
    {
        return new short[Rate * ms / 1000];
    }

    static short[] Join(params short[][] arrays)
    {
        var all = new List<short>();
        foreach (var a in arrays) all.AddRange(a);
        return all.ToArray();
    }

    static short[] Mix(short[] a, short[] b)
    {
        int n = Math.Max(a.Length, b.Length);
        var d = new short[n];
        for (int i = 0; i < n; i++)
        {
            int v = (i < a.Length ? a[i] : 0) + (i < b.Length ? b[i] : 0);
            d[i] = (short)Math.Max(-32000, Math.Min(32000, v));
        }
        return d;
    }

    static byte[] ToWav(short[] samples)
    {
        int bytes = samples.Length * 2;
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write(new[] { 'R', 'I', 'F', 'F' });
        w.Write(36 + bytes);
        w.Write(new[] { 'W', 'A', 'V', 'E' });
        w.Write(new[] { 'f', 'm', 't', ' ' });
        w.Write(16);
        w.Write((short)1);
        w.Write((short)1);
        w.Write(Rate);
        w.Write(Rate * 2);
        w.Write((short)2);
        w.Write((short)16);
        w.Write(new[] { 'd', 'a', 't', 'a' });
        w.Write(bytes);
        foreach (short s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
