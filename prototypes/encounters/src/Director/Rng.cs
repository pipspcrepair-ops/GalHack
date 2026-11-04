using System;
using System.Numerics;

namespace Encounters.Director;

/// <summary>
/// A small deterministic random stream (SplitMix64). The director never touches System.Random
/// or Godot's random numbers, so the same seed and the same inputs always give the same events.
/// Each zone instance draws from its own stream, so what happens in one zone never shifts the
/// dice in another.
/// </summary>
public sealed class Rng
{
    ulong _state;

    public Rng(ulong seed) => _state = seed;

    public ulong NextULong()
    {
        ulong z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble() => (NextULong() >> 11) * (1.0 / 9007199254740992.0);

    public float NextFloat() => (float)NextDouble();

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public double Range(double min, double max) => min + (max - min) * NextDouble();

    /// <summary>Uniform integer in [min, max], both inclusive.</summary>
    public int Range(int min, int max) => max <= min ? min : min + (int)(NextULong() % (ulong)(max - min + 1));

    public int Index(int count) => (int)(NextULong() % (ulong)count);

    public bool Chance(float p) => NextFloat() < p;

    public float Sign() => (NextULong() & 1) == 0 ? -1f : 1f;

    /// <summary>A uniformly distributed unit vector.</summary>
    public Vector3 OnSphere()
    {
        float y = Range(-1f, 1f), phi = Range(0f, MathF.Tau), r = MathF.Sqrt(MathF.Max(0f, 1f - y * y));
        return new Vector3(r * MathF.Cos(phi), y, r * MathF.Sin(phi));
    }

    /// <summary>A unit vector whose vertical component stays within ±<paramref name="spread"/>.</summary>
    public Vector3 AroundHorizon(float spread)
    {
        float y = Range(-spread, spread), phi = Range(0f, MathF.Tau), r = MathF.Sqrt(1f - y * y);
        return new Vector3(r * MathF.Cos(phi), y, r * MathF.Sin(phi));
    }

    /// <summary>Combines two seeds into a new, well-mixed one.</summary>
    public static ulong Mix(ulong a, ulong b) => new Rng(a ^ (b * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL)).NextULong();
}

/// <summary>
/// FNV-1a. string.GetHashCode is randomised per process in .NET, so seeding from it would give
/// a different galaxy every launch.
/// </summary>
public static class StableHash
{
    public static ulong Of(string text)
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
