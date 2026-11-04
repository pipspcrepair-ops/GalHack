using System;

namespace Planets.Gen;

/// <summary>A point in noise space, in metres. Doubles so a 64 km world sampled at 1 m stays exact.</summary>
public readonly record struct P3(double X, double Y, double Z);

/// <summary>
/// Maps flat map coordinates onto a cylinder whose circumference is the map width. Sampling 3D
/// noise on it makes the east-west seam invisible: x = 0 and x = width are the same point.
/// </summary>
public readonly struct Cylinder
{
    public readonly double Width, Radius;

    public Cylinder(double width)
    {
        Width = width;
        Radius = width / (2 * Math.PI);
    }

    public P3 At(double x, double y)
    {
        double angle = x / Radius;
        return new P3(Radius * Math.Cos(angle), y, Radius * Math.Sin(angle));
    }
}

/// <summary>
/// Seeded 3D gradient noise (Perlin's improved noise) with a hashed lattice instead of a
/// permutation table, so it never repeats however far out it is sampled. Pure integer and
/// floating-point arithmetic: the same seed gives the same values on every run.
/// </summary>
public sealed class Noise
{
    readonly uint _seed;

    public Noise(ulong seed) { _seed = (uint)Hash.Mix(seed); }

    /// <summary>Roughly in [-1, 1].</summary>
    public float Sample(double x, double y, double z)
    {
        double fx = Math.Floor(x), fy = Math.Floor(y), fz = Math.Floor(z);
        int ix = (int)(long)fx, iy = (int)(long)fy, iz = (int)(long)fz;
        float dx = (float)(x - fx), dy = (float)(y - fy), dz = (float)(z - fz);
        float u = Fade(dx), v = Fade(dy), w = Fade(dz);

        float n000 = Grad(Lattice(ix, iy, iz), dx, dy, dz);
        float n100 = Grad(Lattice(ix + 1, iy, iz), dx - 1, dy, dz);
        float n010 = Grad(Lattice(ix, iy + 1, iz), dx, dy - 1, dz);
        float n110 = Grad(Lattice(ix + 1, iy + 1, iz), dx - 1, dy - 1, dz);
        float n001 = Grad(Lattice(ix, iy, iz + 1), dx, dy, dz - 1);
        float n101 = Grad(Lattice(ix + 1, iy, iz + 1), dx - 1, dy, dz - 1);
        float n011 = Grad(Lattice(ix, iy + 1, iz + 1), dx, dy - 1, dz - 1);
        float n111 = Grad(Lattice(ix + 1, iy + 1, iz + 1), dx - 1, dy - 1, dz - 1);

        float x00 = n000 + u * (n100 - n000), x10 = n010 + u * (n110 - n010);
        float x01 = n001 + u * (n101 - n001), x11 = n011 + u * (n111 - n011);
        float y0 = x00 + v * (x10 - x00), y1 = x01 + v * (x11 - x01);
        return y0 + w * (y1 - y0);
    }

    public float Sample(in P3 p, double wavelength) => Sample(p.X / wavelength, p.Y / wavelength, p.Z / wavelength);

    uint Lattice(int x, int y, int z)
    {
        uint h = _seed ^ (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        return h ^ (h >> 15);
    }

    static float Fade(float t) => t * t * t * (t * (t * 6 - 15) + 10);

    /// <summary>
    /// Perlin's twelve edge gradients (four repeated to make sixteen), as a table: the classic
    /// bit-twiddling version branches unpredictably and ran several times slower.
    /// </summary>
    static ReadOnlySpan<float> Gradients => new float[]
    {
        1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0,
        1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
        0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1,
        1, 1, 0, 0, -1, 1, -1, 1, 0, 0, -1, -1,
    };

    static float Grad(uint hash, float x, float y, float z)
    {
        ReadOnlySpan<float> g = Gradients.Slice((int)(hash & 15) * 3, 3);
        return g[0] * x + g[1] * y + g[2] * z;
    }
}

/// <summary>
/// Fractal sums of noise octaves. Every sum takes the sample spacing it is evaluated at and drops
/// octaves too fine to show (fading them in over one octave), so the 32 m world map and the 2 m
/// terrain near the player come from one function without aliasing or popping.
/// </summary>
public sealed class Fractal
{
    readonly Noise _noise;
    readonly double _wavelength;
    readonly int _octaves;
    readonly float _gain, _norm;

    public Fractal(ulong seed, double wavelength, int octaves, float gain = 0.5f)
    {
        _noise = new Noise(seed);
        _wavelength = wavelength;
        _octaves = octaves;
        _gain = gain;
        float amp = 1f;
        for (int i = 0; i < octaves; i++)
        {
            _norm += amp;
            amp *= gain;
        }
    }

    /// <summary>Signed fractal noise, roughly in [-1, 1]. Its scale never depends on spacing.</summary>
    public float Fbm(in P3 p, double spacing)
    {
        float sum = 0f, amp = 1f;
        double f = 1.0 / _wavelength;
        for (int i = 0; i < _octaves; i++)
        {
            float keep = OctaveWeight(f, spacing);
            if (keep <= 0f) break;
            sum += amp * keep * _noise.Sample(p.X * f + i * 31.7, p.Y * f - i * 17.3, p.Z * f + i * 11.9);
            amp *= _gain;
            f *= 2.0;
        }
        return sum / _norm * 1.6f;
    }

    /// <summary>Ridged multifractal in [0, 1]: sharp crests where the noise crosses zero.</summary>
    public float Ridged(in P3 p, double spacing, float sharpness = 2f)
    {
        float sum = 0f, amp = 1f, weight = 1f;
        double f = 1.0 / _wavelength;
        for (int i = 0; i < _octaves; i++)
        {
            float keep = OctaveWeight(f, spacing);
            if (keep <= 0f) break;
            float n = 1f - Math.Abs(_noise.Sample(p.X * f + i * 31.7, p.Y * f - i * 17.3, p.Z * f + i * 11.9));
            n = MathF.Pow(n, sharpness) * weight;
            weight = Math.Clamp(n * 1.5f, 0f, 1f); // crests carry their detail, valleys stay smooth
            sum += amp * keep * n;
            amp *= _gain;
            f *= 2.0;
        }
        return sum / _norm;
    }

    /// <summary>Raw single-octave noise at the base wavelength.</summary>
    public float Single(in P3 p) => _noise.Sample(p, _wavelength);

    /// <summary>Full weight for wavelengths of 4+ samples, none below 2 (Nyquist).</summary>
    static float OctaveWeight(double frequency, double spacing)
    {
        double t = (1.0 / (frequency * spacing) - 2.0) * 0.5;
        return t >= 1 ? 1f : t <= 0 ? 0f : (float)t;
    }
}

public static class Mathx
{
    public static float Smooth(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    public static float Lerp(float a, float b, float t) => a + (b - a) * t;

    /// <summary>Wraps a signed x distance onto the shorter way round a map of the given width.</summary>
    public static double WrapDelta(double dx, double width)
    {
        dx %= width;
        if (dx > width * 0.5) dx -= width;
        else if (dx < -width * 0.5) dx += width;
        return dx;
    }

    public static double WrapX(double x, double width)
    {
        x %= width;
        return x < 0 ? x + width : x;
    }
}
