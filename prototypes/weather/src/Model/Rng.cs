using System;

namespace Weather.Model;

/// <summary>
/// A small deterministic random generator (SplitMix64). Godot's RandomNumberGenerator is an
/// engine object; this one is plain C#, so the weather model runs in tests without a scene and
/// gives the same sequence for the same seed on every machine.
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
    public float NextFloat() => (NextULong() >> 40) * (1f / (1 << 24));

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    /// <summary>An exponentially distributed wait with mean 1: the gap between random events.</summary>
    public float Exponential() => -MathF.Log(1f - NextFloat() * 0.999f);
}

/// <summary>Smooth hash noise, for gusts. Stateless, so any time or place can be sampled.</summary>
public static class Noise
{
    static float Hash(int i, uint seed)
    {
        uint h = (uint)i * 0x27D4EB2Du ^ seed * 0x165667B1u;
        h ^= h >> 15;
        h *= 0x85EBCA77u;
        h ^= h >> 13;
        return (h & 0xFFFFFF) / (float)0xFFFFFF * 2f - 1f;
    }

    /// <summary>1D value noise in [-1, 1], smooth (C1) everywhere.</summary>
    public static float Value1D(float x, uint seed)
    {
        float f = MathF.Floor(x);
        int i = (int)f;
        float t = x - f;
        t = t * t * (3f - 2f * t);
        return Hash(i, seed) + (Hash(i + 1, seed) - Hash(i, seed)) * t;
    }
}
