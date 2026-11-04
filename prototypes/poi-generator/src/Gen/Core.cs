#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;

// The generator never touches Godot: everything under src/Gen is plain C#, so it can run in a
// server, a tool or a unit test, and the same seed gives the same place on every machine.
namespace PoiGenerator.Gen;

/// <summary>A cell on the block grid. One cell is one metre. +X east, +Y up, +Z south.</summary>
public readonly record struct Int3(int X, int Y, int Z)
{
    public static readonly Int3 Zero = new(0, 0, 0);
    public static readonly Int3 Up = new(0, 1, 0);
    public static readonly Int3 Down = new(0, -1, 0);

    public static Int3 operator +(Int3 a, Int3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Int3 operator -(Int3 a, Int3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Int3 operator *(Int3 a, int k) => new(a.X * k, a.Y * k, a.Z * k);
    public static Int3 Min(Int3 a, Int3 b) => new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));
    public static Int3 Max(Int3 a, Int3 b) => new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

    public int Manhattan(Int3 o) => Math.Abs(X - o.X) + Math.Abs(Y - o.Y) + Math.Abs(Z - o.Z);
    public float FlatDistance(Int3 o) => MathF.Sqrt((X - o.X) * (X - o.X) + (Z - o.Z) * (Z - o.Z));
    public override string ToString() => $"({X},{Y},{Z})";
}

/// <summary>Grid directions. North is -Z, which is Godot's forward.</summary>
public enum Dir : byte { North, East, South, West, Up, Down }

public static class Dirs
{
    public static readonly Dir[] Horizontal = { Dir.North, Dir.East, Dir.South, Dir.West };
    public static readonly Dir[] All = { Dir.North, Dir.East, Dir.South, Dir.West, Dir.Up, Dir.Down };

    public static Int3 Vec(this Dir d) => d switch
    {
        Dir.North => new Int3(0, 0, -1),
        Dir.East => new Int3(1, 0, 0),
        Dir.South => new Int3(0, 0, 1),
        Dir.West => new Int3(-1, 0, 0),
        Dir.Up => Int3.Up,
        _ => Int3.Down,
    };

    public static Dir Opposite(this Dir d) => d switch
    {
        Dir.North => Dir.South,
        Dir.South => Dir.North,
        Dir.East => Dir.West,
        Dir.West => Dir.East,
        Dir.Up => Dir.Down,
        _ => Dir.Up,
    };

    /// <summary>Turns a horizontal direction clockwise (seen from above) by quarter turns.</summary>
    public static Dir RotateCw(this Dir d, int quarterTurns)
    {
        if (d is Dir.Up or Dir.Down) return d;
        return (Dir)(((int)d + quarterTurns % 4 + 4) % 4);
    }

    public static bool IsHorizontal(this Dir d) => d <= Dir.West;
}

/// <summary>An sRGB colour, 8 bits a channel. Kits author colours as hex.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6) throw new FormatException($"colour '{hex}' is not 6 hex digits");
        uint v = uint.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return new Rgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public Rgb Scale(float k) => new(Clamp(R * k), Clamp(G * k), Clamp(B * k));

    public Rgb Lerp(Rgb o, float t) =>
        new(Clamp(R + (o.R - R) * t), Clamp(G + (o.G - G) * t), Clamp(B + (o.B - B) * t));

    static byte Clamp(float v) => (byte)Math.Clamp((int)MathF.Round(v), 0, 255);
    public override string ToString() => $"{R:x2}{G:x2}{B:x2}";
}

/// <summary>
/// Stable hashing. string.GetHashCode is randomised per process in .NET, so anything that
/// feeds a seed goes through here instead.
/// </summary>
public static class Hash
{
    public static ulong Of(string s)
    {
        ulong h = 14695981039346656037UL; // FNV-1a
        foreach (char c in s)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return Mix(h);
    }

    public static ulong Mix(ulong z)
    {
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static ulong Combine(ulong a, ulong b) => Mix(a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2)));
    public static ulong Combine(ulong a, ulong b, ulong c) => Combine(Combine(a, b), c);

    /// <summary>A cheap per-cell hash, used for colour jitter and prop variants.</summary>
    public static uint Cell(Int3 p, uint salt = 0)
    {
        unchecked
        {
            uint h = (uint)p.X * 73856093u ^ (uint)p.Y * 19349663u ^ (uint)p.Z * 83492791u ^ salt * 2654435761u;
            h ^= h >> 13;
            h *= 0x5bd1e995;
            h ^= h >> 15;
            return h;
        }
    }
}

/// <summary>Accumulates time per named phase, for the performance test. Cheap enough to leave in.</summary>
public static class Prof
{
    public static readonly Dictionary<string, long> Ticks = new();

    public readonly struct Scope : IDisposable
    {
        readonly string _name;
        readonly long _start;

        public Scope(string name)
        {
            _name = name;
            _start = System.Diagnostics.Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            long t = System.Diagnostics.Stopwatch.GetTimestamp() - _start;
            Ticks[_name] = Ticks.TryGetValue(_name, out long old) ? old + t : t;
        }
    }

    public static Scope Time(string name) => new(name);
    public static double Ms(long ticks) => ticks * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
}

/// <summary>
/// SplitMix64. Our own generator rather than System.Random, whose sequence is not promised to
/// stay the same between .NET versions.
/// </summary>
public sealed class Rng
{
    ulong _state;

    public Rng(ulong seed) => _state = seed;

    public ulong NextU64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        return Hash.Mix(z);
    }

    /// <summary>A uniform int in [min, maxExclusive).</summary>
    public int Range(int min, int maxExclusive)
    {
        if (maxExclusive <= min) return min;
        return min + (int)(NextU64() % (ulong)(maxExclusive - min));
    }

    /// <summary>A uniform int in [min, max], both ends included.</summary>
    public int Between(int min, int max) => Range(min, max + 1);

    public float Float() => (NextU64() >> 40) / (float)(1UL << 24);
    public float Float(float min, float max) => min + (max - min) * Float();
    public bool Chance(float p) => Float() < p;

    public T Pick<T>(IReadOnlyList<T> items) => items[Range(0, items.Count)];

    public int PickWeighted(IReadOnlyList<float> weights)
    {
        float total = 0f;
        foreach (float w in weights) total += Math.Max(0f, w);
        if (total <= 0f) return -1;
        float roll = Float() * total;
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= Math.Max(0f, weights[i]);
            if (roll < 0f) return i;
        }
        for (int i = weights.Count - 1; i >= 0; i--)
            if (weights[i] > 0f) return i;
        return -1;
    }

    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Range(0, i + 1);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    /// <summary>Orders items by a weighted random draw without replacement.</summary>
    public List<T> WeightedOrder<T>(IReadOnlyList<T> items, Func<T, float> weight)
    {
        var pool = new List<T>(items);
        var order = new List<T>(items.Count);
        while (pool.Count > 0)
        {
            var w = new float[pool.Count];
            for (int i = 0; i < pool.Count; i++) w[i] = Math.Max(0.0001f, weight(pool[i]));
            int k = PickWeighted(w);
            order.Add(pool[k]);
            pool.RemoveAt(k);
        }
        return order;
    }

    /// <summary>An independent stream, so adding a roll in one pass never shifts another pass.</summary>
    public Rng Fork(string salt) => new(Hash.Combine(_state, Hash.Of(salt)));
}
