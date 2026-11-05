using System;

namespace Planets.Gen;

/// <summary>A point in noise space, in metres. Doubles so a 64 km world sampled at 1 m stays exact.</summary>
public readonly record struct P3(double X, double Y, double Z);

/// <summary>A point in 4D noise space, in metres: where a torus puts a map position.</summary>
public readonly record struct P4(double X, double Y, double Z, double W)
{
    /// <summary>The same point with the two circles swapped: an independent-looking second field from one fractal.</summary>
    public P4 Swap => new(Z, W, X, Y);
}

/// <summary>
/// Maps flat map coordinates onto a flat torus in 4D: x goes round one circle whose
/// circumference is the map width, y round another whose circumference is the map height.
/// Sampling 4D noise there makes both seams invisible (x = 0 and x = width are the same point,
/// and so are y = 0 and y = height), and because each circle's circumference equals its map
/// span, a metre on the map is a metre in noise space in both directions: nothing stretches.
/// </summary>
public readonly struct Torus
{
    public readonly double Width, Height, RadiusX, RadiusY;

    public Torus(double width, double height)
    {
        Width = width;
        Height = height;
        RadiusX = width / (2 * Math.PI);
        RadiusY = height / (2 * Math.PI);
    }

    public P4 At(double x, double y)
    {
        double ax = x / RadiusX, ay = y / RadiusY;
        return new P4(RadiusX * Math.Cos(ax), RadiusX * Math.Sin(ax), RadiusY * Math.Cos(ay), RadiusY * Math.Sin(ay));
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

    // ---- 4D simplex noise, for sampling on a torus ------------------------------------

    const double F4 = 0.30901699437494745; // (sqrt(5) - 1) / 4
    const double G4 = 0.1381966011250105;  // (5 - sqrt(5)) / 20

    /// <summary>
    /// 4D simplex noise (Perlin 2001, after Gustavson's notes): five lattice corners per
    /// sample instead of sixteen, so a torus costs about what the cylinder did. Scaled so its
    /// spread matches the 3D noise, which every threshold in the terrain model was tuned on.
    /// </summary>
    public float Sample4(double x, double y, double z, double w)
    {
        double s = (x + y + z + w) * F4;
        double fi = Math.Floor(x + s), fj = Math.Floor(y + s), fk = Math.Floor(z + s), fl = Math.Floor(w + s);
        double t = (fi + fj + fk + fl) * G4;
        float x0 = (float)(x - (fi - t)), y0 = (float)(y - (fj - t)), z0 = (float)(z - (fk - t)), w0 = (float)(w - (fl - t));
        int i = (int)(long)fi, j = (int)(long)fj, k = (int)(long)fk, l = (int)(long)fl;

        // Rank the offsets to find which simplex of the 24 the point is in.
        int rx = 0, ry = 0, rz = 0, rw = 0;
        if (x0 > y0) rx++; else ry++;
        if (x0 > z0) rx++; else rz++;
        if (x0 > w0) rx++; else rw++;
        if (y0 > z0) ry++; else rz++;
        if (y0 > w0) ry++; else rw++;
        if (z0 > w0) rz++; else rw++;
        int i1 = rx >= 3 ? 1 : 0, j1 = ry >= 3 ? 1 : 0, k1 = rz >= 3 ? 1 : 0, l1 = rw >= 3 ? 1 : 0;
        int i2 = rx >= 2 ? 1 : 0, j2 = ry >= 2 ? 1 : 0, k2 = rz >= 2 ? 1 : 0, l2 = rw >= 2 ? 1 : 0;
        int i3 = rx >= 1 ? 1 : 0, j3 = ry >= 1 ? 1 : 0, k3 = rz >= 1 ? 1 : 0, l3 = rw >= 1 ? 1 : 0;

        const float g = (float)G4;
        float n = Corner(Lattice4(i, j, k, l), x0, y0, z0, w0)
            + Corner(Lattice4(i + i1, j + j1, k + k1, l + l1), x0 - i1 + g, y0 - j1 + g, z0 - k1 + g, w0 - l1 + g)
            + Corner(Lattice4(i + i2, j + j2, k + k2, l + l2), x0 - i2 + 2 * g, y0 - j2 + 2 * g, z0 - k2 + 2 * g, w0 - l2 + 2 * g)
            + Corner(Lattice4(i + i3, j + j3, k + k3, l + l3), x0 - i3 + 3 * g, y0 - j3 + 3 * g, z0 - k3 + 3 * g, w0 - l3 + 3 * g)
            + Corner(Lattice4(i + 1, j + 1, k + 1, l + 1), x0 - 1 + 4 * g, y0 - 1 + 4 * g, z0 - 1 + 4 * g, w0 - 1 + 4 * g);
        return n * Simplex4Scale;
    }

    public float Sample(in P4 p, double wavelength) => Sample4(p.X / wavelength, p.Y / wavelength, p.Z / wavelength, p.W / wavelength);

    /// <summary>
    /// Brings 4D simplex noise to the same spread as the 3D gradient noise (standard deviation
    /// 0.27 over 200k random points; the textbook factor of 27 gives 0.30).
    /// </summary>
    const float Simplex4Scale = 24.5f;

    static float Corner(uint hash, float x, float y, float z, float w)
    {
        float t = 0.6f - x * x - y * y - z * z - w * w;
        if (t <= 0f) return 0f;
        t *= t;
        ReadOnlySpan<float> gr = Gradients4.Slice((int)(hash & 31) * 4, 4);
        return t * t * (gr[0] * x + gr[1] * y + gr[2] * z + gr[3] * w);
    }

    uint Lattice4(int x, int y, int z, int w)
    {
        uint h = _seed ^ (uint)x * 0x8DA6B343u ^ (uint)y * 0xD8163841u ^ (uint)z * 0xCB1AB31Fu ^ (uint)w * 0x9E3779B1u;
        h ^= h >> 15;
        h *= 0x2C1B3C6Du;
        h ^= h >> 12;
        h *= 0x297A2D39u;
        return h ^ (h >> 15);
    }

    /// <summary>The 32 edge midpoints of a 4D hypercube: one zero and three ±1s.</summary>
    static ReadOnlySpan<float> Gradients4 => new float[]
    {
        0, 1, 1, 1, 0, 1, 1, -1, 0, 1, -1, 1, 0, 1, -1, -1, 0, -1, 1, 1, 0, -1, 1, -1, 0, -1, -1, 1, 0, -1, -1, -1,
        1, 0, 1, 1, 1, 0, 1, -1, 1, 0, -1, 1, 1, 0, -1, -1, -1, 0, 1, 1, -1, 0, 1, -1, -1, 0, -1, 1, -1, 0, -1, -1,
        1, 1, 0, 1, 1, 1, 0, -1, 1, -1, 0, 1, 1, -1, 0, -1, -1, 1, 0, 1, -1, 1, 0, -1, -1, -1, 0, 1, -1, -1, 0, -1,
        1, 1, 1, 0, 1, 1, -1, 0, 1, -1, 1, 0, 1, -1, -1, 0, -1, 1, 1, 0, -1, 1, -1, 0, -1, -1, 1, 0, -1, -1, -1, 0,
    };

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

    /// <summary>Signed fractal noise on the torus, roughly in [-1, 1]. Its scale never depends on spacing.</summary>
    public float Fbm(in P4 p, double spacing)
    {
        float sum = 0f, amp = 1f;
        double f = 1.0 / _wavelength;
        for (int i = 0; i < _octaves; i++)
        {
            float keep = OctaveWeight(f, spacing);
            if (keep <= 0f) break;
            sum += amp * keep * _noise.Sample4(p.X * f + i * 31.7, p.Y * f - i * 17.3, p.Z * f + i * 11.9, p.W * f - i * 23.1);
            amp *= _gain;
            f *= 2.0;
        }
        return sum / _norm * 1.6f;
    }

    /// <summary>Ridged multifractal on the torus, in [0, 1]: sharp crests where the noise crosses zero.</summary>
    public float Ridged(in P4 p, double spacing, float sharpness = 2f)
    {
        float sum = 0f, amp = 1f, weight = 1f;
        double f = 1.0 / _wavelength;
        for (int i = 0; i < _octaves; i++)
        {
            float keep = OctaveWeight(f, spacing);
            if (keep <= 0f) break;
            float n = 1f - Math.Abs(_noise.Sample4(p.X * f + i * 31.7, p.Y * f - i * 17.3, p.Z * f + i * 11.9, p.W * f - i * 23.1));
            n = MathF.Pow(n, sharpness) * weight;
            weight = Math.Clamp(n * 1.5f, 0f, 1f); // crests carry their detail, valleys stay smooth
            sum += amp * keep * n;
            amp *= _gain;
            f *= 2.0;
        }
        return sum / _norm;
    }

    /// <summary>Raw single-octave noise on the torus at the base wavelength.</summary>
    public float Single(in P4 p) => _noise.Sample(p, _wavelength);

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

    /// <summary>Wraps a coordinate into [0, span): the same for x (width) and y (height) on a torus.</summary>
    public static double Wrap(double v, double span) => WrapX(v, span);
}
