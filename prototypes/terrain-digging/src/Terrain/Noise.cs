using System;

namespace TerrainDigging.Terrain;

/// <summary>
/// Seeded value and gradient noise built only from integer hashing, +, −, × and Floor.
/// Those operations are exact IEEE on every platform .NET runs on (no transcendental
/// functions, no fused multiply-add), so every machine in a session generates the same
/// terrain bit for bit from the same seed. Godot's FastNoiseLite isn't used for the
/// same reason, and so the terrain core stays free of engine types.
/// </summary>
public static class Noise
{
    public static uint Hash(int x, int y, int z, uint seed)
    {
        unchecked
        {
            uint h = seed ^ 0x9E3779B9u;
            h ^= (uint)x * 0x27D4EB2Du;
            h = Rotl(h, 15) * 0x85EBCA6Bu;
            h ^= (uint)y * 0x165667B1u;
            h = Rotl(h, 13) * 0xC2B2AE35u;
            h ^= (uint)z * 0x9E3779B1u;
            h = Rotl(h, 17) * 0x27D4EB2Fu;
            h ^= h >> 15;
            h *= 0x2C1B3C6Du;
            h ^= h >> 12;
            h *= 0x297A2D39u;
            h ^= h >> 15;
            return h;
        }
    }

    static uint Rotl(uint v, int r) => (v << r) | (v >> (32 - r));

    /// <summary>Maps a hash to [-1, 1).</summary>
    public static float Signed(uint h) => (h >> 8) * (2f / 16777216f) - 1f;

    /// <summary>Maps a hash to [0, 1).</summary>
    public static float Unit(uint h) => (h >> 8) * (1f / 16777216f);

    static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
    static float Lerp(float a, float b, float t) => a + (b - a) * t;

    public static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    /// <summary>2D value noise in [-1, 1].</summary>
    public static float Value2(float x, float z, uint seed)
    {
        int x0 = (int)MathF.Floor(x), z0 = (int)MathF.Floor(z);
        float u = Quintic(x - x0), v = Quintic(z - z0);
        float a = Signed(Hash(x0, 0, z0, seed)), b = Signed(Hash(x0 + 1, 0, z0, seed));
        float c = Signed(Hash(x0, 0, z0 + 1, seed)), d = Signed(Hash(x0 + 1, 0, z0 + 1, seed));
        return Lerp(Lerp(a, b, u), Lerp(c, d, u), v);
    }

    /// <summary>Fractal 2D value noise, roughly in [-1, 1].</summary>
    public static float Fbm2(float x, float z, int octaves, uint seed)
    {
        float sum = 0f, amplitude = 1f, norm = 0f, frequency = 1f;
        for (int i = 0; i < octaves; i++)
        {
            sum += amplitude * Value2(x * frequency, z * frequency, seed + (uint)i * 1013u);
            norm += amplitude;
            amplitude *= 0.5f;
            frequency *= 2.03f;
        }
        return sum / norm;
    }

    /// <summary>3D gradient (Perlin-style) noise, roughly in [-1, 1].</summary>
    public static float Gradient3(float x, float y, float z, uint seed)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y), z0 = (int)MathF.Floor(z);
        float fx = x - x0, fy = y - y0, fz = z - z0;
        float u = Quintic(fx), v = Quintic(fy), w = Quintic(fz);

        float n000 = Grad(Hash(x0, y0, z0, seed), fx, fy, fz);
        float n100 = Grad(Hash(x0 + 1, y0, z0, seed), fx - 1f, fy, fz);
        float n010 = Grad(Hash(x0, y0 + 1, z0, seed), fx, fy - 1f, fz);
        float n110 = Grad(Hash(x0 + 1, y0 + 1, z0, seed), fx - 1f, fy - 1f, fz);
        float n001 = Grad(Hash(x0, y0, z0 + 1, seed), fx, fy, fz - 1f);
        float n101 = Grad(Hash(x0 + 1, y0, z0 + 1, seed), fx - 1f, fy, fz - 1f);
        float n011 = Grad(Hash(x0, y0 + 1, z0 + 1, seed), fx, fy - 1f, fz - 1f);
        float n111 = Grad(Hash(x0 + 1, y0 + 1, z0 + 1, seed), fx - 1f, fy - 1f, fz - 1f);

        float x00 = Lerp(n000, n100, u), x10 = Lerp(n010, n110, u);
        float x01 = Lerp(n001, n101, u), x11 = Lerp(n011, n111, u);
        return Lerp(Lerp(x00, x10, v), Lerp(x01, x11, v), w);
    }

    static float Grad(uint h, float x, float y, float z) => (h % 12u) switch
    {
        0 => x + y,
        1 => -x + y,
        2 => x - y,
        3 => -x - y,
        4 => x + z,
        5 => -x + z,
        6 => x - z,
        7 => -x - z,
        8 => y + z,
        9 => -y + z,
        10 => y - z,
        _ => -y - z,
    };
}

/// <summary>SplitMix64: a tiny deterministic PRNG for laying out generator features from the seed.</summary>
public struct SplitMix64
{
    ulong _state;

    public SplitMix64(ulong seed) => _state = seed;

    public ulong Next()
    {
        unchecked
        {
            ulong z = _state += 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }
    }

    /// <summary>A float in [0, 1).</summary>
    public float NextFloat() => (Next() >> 40) * (1f / 16777216f);

    public float Range(float min, float max) => min + (max - min) * NextFloat();

    public int Range(int min, int maxExclusive) => min + (int)(Next() % (ulong)(maxExclusive - min));
}
