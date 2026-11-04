using System;
using System.Collections.Generic;

namespace Wildlife;

/// <summary>
/// A small deterministic random generator (SplitMix64). Godot's RandomNumberGenerator and
/// .NET's Random are not guaranteed to give the same sequence across versions, and
/// string.GetHashCode is randomised per process, so everything that must come out the same
/// for every friend on every machine (the fauna of a planet, its names) goes through this.
/// </summary>
public sealed class Rng
{
    ulong _state;

    public Rng(ulong seed) => _state = seed ^ 0x9E3779B97F4A7C15UL;

    /// <summary>A generator for one named purpose under a seed, so adding a roll in one place never shifts another.</summary>
    public static Rng For(ulong seed, string purpose, long index = 0) => new(Mix(Mix(seed, Hash(purpose)), (ulong)index));

    public ulong NextU64()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public float Float() => (NextU64() >> 40) * (1f / (1 << 24));

    public float Range(float min, float max) => min + (max - min) * Float();

    public float Range(float[] range) => range.Length >= 2 ? Range(range[0], range[1]) : range[0];

    /// <summary>Uniform integer in [min, max], both inclusive.</summary>
    public int Int(int min, int max) => min + (int)(NextU64() % (ulong)(max - min + 1));

    public int Int(int[] range) => range.Length >= 2 ? Int(range[0], range[1]) : range[0];

    public bool Chance(float p) => Float() < p;

    /// <summary>Biased towards the ends of [0, 1] as <paramref name="extreme"/> goes to 1: wilder proportions.</summary>
    public float Extreme(float extreme)
    {
        float u = Float();
        float pushed = u < 0.5f ? 0.5f * MathF.Pow(2f * u, 1f + 2f * extreme) : 1f - 0.5f * MathF.Pow(2f * (1f - u), 1f + 2f * extreme);
        return pushed;
    }

    public T Pick<T>(IReadOnlyList<T> items) => items[Int(0, items.Count - 1)];

    public T Weighted<T>(IReadOnlyList<T> items, Func<T, float> weight)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++) total += MathF.Max(0f, weight(items[i]));
        if (total <= 0f) return Pick(items);
        float roll = Float() * total;
        for (int i = 0; i < items.Count; i++)
        {
            roll -= MathF.Max(0f, weight(items[i]));
            if (roll < 0f) return items[i];
        }
        return items[^1];
    }

    public void Shuffle<T>(IList<T> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int j = Int(0, i);
            (items[i], items[j]) = (items[j], items[i]);
        }
    }

    public static ulong Mix(ulong a, ulong b)
    {
        ulong z = a ^ (b + 0x9E3779B97F4A7C15UL + (a << 6) + (a >> 2));
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>FNV-1a: a stable string hash, unlike string.GetHashCode.</summary>
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
