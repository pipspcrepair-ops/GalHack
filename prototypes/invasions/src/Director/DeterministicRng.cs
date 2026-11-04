using System;
using System.Collections.Generic;

namespace Invasions.Director;

/// <summary>
/// SplitMix64. Tiny, fast and the same on every machine and .NET version, which System.Random
/// does not promise. Everything random the director decides comes from one of these, seeded from
/// the server seed, so the same seed and the same inputs replay the same invasions.
/// </summary>
public sealed class DeterministicRng
{
    ulong _state;

    public DeterministicRng(ulong seed) => _state = seed;

    public ulong NextULong()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / (1UL << 53));

    public double Range(double min, double max) => min + (max - min) * NextDouble();

    public int Below(int count) => (int)(NextULong() % (ulong)Math.Max(1, count));

    public T Pick<T>(IReadOnlyList<T> items) => items[Below(items.Count)];

    /// <summary>Picks a key by weight. Callers pass keys in a fixed order so picks replay.</summary>
    public string PickWeighted(IReadOnlyList<KeyValuePair<string, double>> weights)
    {
        double total = 0;
        foreach (var w in weights) total += Math.Max(0, w.Value);
        double roll = NextDouble() * total;
        foreach (var w in weights)
        {
            roll -= Math.Max(0, w.Value);
            if (roll < 0) return w.Key;
        }
        return weights[^1].Key;
    }

    /// <summary>
    /// A seed for an independent stream, mixed from a parent seed and some labels. Each base and each
    /// invasion gets its own stream, so adding a second base never changes the first base's invasions.
    /// </summary>
    public static ulong Derive(ulong seed, params ulong[] labels)
    {
        ulong h = seed ^ 0x2545F4914F6CDD1DUL;
        foreach (ulong label in labels)
        {
            h ^= label + 0x9E3779B97F4A7C15UL + (h << 6) + (h >> 2);
            h = new DeterministicRng(h).NextULong();
        }
        return h;
    }

    /// <summary>FNV-1a. string.GetHashCode is randomised per process, so it can't seed a replay.</summary>
    public static ulong Hash(string text)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in text)
        {
            h ^= c;
            h *= 1099511628211UL;
        }
        return h;
    }
}
