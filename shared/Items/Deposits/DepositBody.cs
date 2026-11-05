using System;
using System.Collections.Generic;

namespace GalHack.Items.Deposits;

/// <summary>Seeded hashing and lumpy value noise: deterministic on every machine.</summary>
static class Rng
{
    public static ulong Mix(ulong x)
    {
        x += 0x9E3779B97F4A7C15UL;
        x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
        x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
        return x ^ (x >> 31);
    }

    public static ulong Of(ulong seed, long a, long b = 0, long c = 0) =>
        Mix(seed ^ Mix((ulong)a ^ Mix((ulong)b * 0x632BE59BD9B4E019UL ^ Mix((ulong)c))));

    public static double Unit(ulong h) => (h >> 11) * (1.0 / (1UL << 53));

    /// <summary>A stable hash of a string (string.GetHashCode changes every run).</summary>
    public static ulong OfText(string s)
    {
        ulong h = 14695981039346656037UL;
        foreach (char c in s) h = (h ^ c) * 1099511628211UL;
        return Mix(h);
    }
}

/// <summary>A stream of seeded random numbers.</summary>
sealed class Dice
{
    readonly ulong _seed;
    long _n;
    public Dice(ulong seed) => _seed = seed;
    public double Next() => Rng.Unit(Rng.Of(_seed, _n++));
    public double Range(double lo, double hi) => lo + (hi - lo) * Next();
    public int Int(int lo, int hiInclusive) => lo + (int)Math.Min(hiInclusive - lo, Math.Floor(Next() * (hiInclusive - lo + 1)));
}

/// <summary>Smooth 3D value noise in -1..1 at a given wavelength: the lumpy outline of an ore body.</summary>
sealed class Lumps
{
    readonly ulong _seed;
    readonly double _inv;

    public Lumps(ulong seed, double wavelength)
    {
        _seed = seed;
        _inv = 1 / wavelength;
    }

    public double Sample(double x, double y, double z)
    {
        x *= _inv; y *= _inv; z *= _inv;
        long ix = (long)Math.Floor(x), iy = (long)Math.Floor(y), iz = (long)Math.Floor(z);
        double fx = S(x - ix), fy = S(y - iy), fz = S(z - iz);
        double L(long dx, long dy, long dz) => Rng.Unit(Rng.Of(_seed, ix + dx, iy + dy, iz + dz)) * 2 - 1;
        double x00 = Lerp(L(0, 0, 0), L(1, 0, 0), fx), x10 = Lerp(L(0, 1, 0), L(1, 1, 0), fx);
        double x01 = Lerp(L(0, 0, 1), L(1, 0, 1), fx), x11 = Lerp(L(0, 1, 1), L(1, 1, 1), fx);
        return Lerp(Lerp(x00, x10, fy), Lerp(x01, x11, fy), fz);
    }

    static double S(double t) => t * t * (3 - 2 * t);
    static double Lerp(double a, double b, double t) => a + (b - a) * t;
}

/// <summary>The ground height over a deposit's footprint, sampled once when it grows (bilinear between samples).</summary>
sealed class HeightGrid
{
    readonly double _x0, _z0, _step;
    readonly int _nx, _nz;
    readonly double[] _h;

    public HeightGrid(Func<double, double, double> surface, double x0, double z0, double x1, double z1, double step)
    {
        _x0 = x0; _z0 = z0; _step = step;
        _nx = (int)Math.Ceiling((x1 - x0) / step) + 1;
        _nz = (int)Math.Ceiling((z1 - z0) / step) + 1;
        _h = new double[_nx * _nz];
        for (int j = 0; j < _nz; j++)
            for (int i = 0; i < _nx; i++)
                _h[i + _nx * j] = surface(x0 + i * step, z0 + j * step);
    }

    public double At(double x, double z)
    {
        double gx = Math.Clamp((x - _x0) / _step, 0, _nx - 1.0001), gz = Math.Clamp((z - _z0) / _step, 0, _nz - 1.0001);
        int i = (int)gx, j = (int)gz;
        double tx = gx - i, tz = gz - j;
        double a = _h[i + _nx * j], b = _h[i + 1 + _nx * j], c = _h[i + _nx * (j + 1)], d = _h[i + 1 + _nx * (j + 1)];
        return (a * (1 - tx) + b * tx) * (1 - tz) + (c * (1 - tx) + d * tx) * tz;
    }
}

/// <summary>One building block of an ore body, as a signed distance (negative inside).</summary>
abstract class Part
{
    public abstract double Distance(double x, double y, double z, HeightGrid ground);
    public abstract void Grow(ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ);
    /// <summary>Scales the part's thickness (radii), not its length: how volume is matched to the deposit's amount.</summary>
    public abstract void ScaleRadii(double s);
    public abstract double Size { get; }

    protected static void Box(double cx, double cy, double cz, double r, ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ)
    {
        minX = Math.Min(minX, cx - r); maxX = Math.Max(maxX, cx + r);
        minY = Math.Min(minY, cy - r); maxY = Math.Max(maxY, cy + r);
        minZ = Math.Min(minZ, cz - r); maxZ = Math.Max(maxZ, cz + r);
    }
}

/// <summary>A tube from A to B whose radius runs from <see cref="Ra"/> to <see cref="Rb"/>: veins, passages, stringers.</summary>
sealed class Capsule : Part
{
    public double Ax, Ay, Az, Bx, By, Bz, Ra, Rb;
    public override double Size => Math.Max(Ra, Rb);

    public override double Distance(double x, double y, double z, HeightGrid ground)
    {
        double dx = Bx - Ax, dy = By - Ay, dz = Bz - Az;
        double len2 = dx * dx + dy * dy + dz * dz;
        double t = len2 < 1e-9 ? 0 : Math.Clamp(((x - Ax) * dx + (y - Ay) * dy + (z - Az) * dz) / len2, 0, 1);
        double px = Ax + dx * t - x, py = Ay + dy * t - y, pz = Az + dz * t - z;
        return Math.Sqrt(px * px + py * py + pz * pz) - (Ra + (Rb - Ra) * t);
    }

    public override void Grow(ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ)
    {
        Box(Ax, Ay, Az, Ra, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        Box(Bx, By, Bz, Rb, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
    }

    public override void ScaleRadii(double s) { Ra *= s; Rb *= s; }
}

/// <summary>A squat ellipsoid turned about the vertical: pockets and cluster cores.</summary>
sealed class Blob : Part
{
    public double Cx, Cy, Cz, Rx, Ry, Rz, Turn;
    public override double Size => Math.Max(Rx, Math.Max(Ry, Rz));

    public override double Distance(double x, double y, double z, HeightGrid ground)
    {
        double c = Math.Cos(Turn), s = Math.Sin(Turn);
        double lx = (x - Cx) * c - (z - Cz) * s, lz = (x - Cx) * s + (z - Cz) * c, ly = y - Cy;
        double q = Math.Sqrt(lx * lx / (Rx * Rx) + ly * ly / (Ry * Ry) + lz * lz / (Rz * Rz));
        return (q - 1) * Math.Min(Rx, Math.Min(Ry, Rz));
    }

    public override void Grow(ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ) =>
        Box(Cx, Cy, Cz, Size, ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);

    public override void ScaleRadii(double s) { Rx *= s; Ry *= s; Rz *= s; }
}

/// <summary>
/// A layered bed: a sheet lying a set depth under the ground and following it, gently warped,
/// thinning out to its elliptical edge.
/// </summary>
sealed class Sheet : Part
{
    public double Cx, Cz, Rx, Rz, Turn, Depth, Half, Warp;
    public Lumps WarpNoise;
    public double TopY; // highest the sheet can reach, for bounds (set when grown)
    public double BottomY;
    public override double Size => Math.Max(Rx, Rz);

    public override double Distance(double x, double y, double z, HeightGrid ground)
    {
        double c = Math.Cos(Turn), s = Math.Sin(Turn);
        double lx = (x - Cx) * c - (z - Cz) * s, lz = (x - Cx) * s + (z - Cz) * c;
        double e = lx * lx / (Rx * Rx) + lz * lz / (Rz * Rz);
        double edge = (Math.Sqrt(e) - 1) * Math.Min(Rx, Rz);
        if (e >= 1) return Math.Max(edge, 0.01);
        double mid = ground.At(x, z) - Depth + Warp * WarpNoise.Sample(x, 0, z);
        double thick = Half * Math.Sqrt(1 - e * e);
        return Math.Max(Math.Abs(y - mid) - thick, edge);
    }

    public override void Grow(ref double minX, ref double minY, ref double minZ, ref double maxX, ref double maxY, ref double maxZ)
    {
        double r = Math.Max(Rx, Rz);
        minX = Math.Min(minX, Cx - r); maxX = Math.Max(maxX, Cx + r);
        minZ = Math.Min(minZ, Cz - r); maxZ = Math.Max(maxZ, Cz + r);
        minY = Math.Min(minY, BottomY - Depth - Half - Warp);
        maxY = Math.Max(maxY, TopY - Depth + Half + Warp);
    }

    /// <summary>A bed keeps its thickness (a cell or two, so it reads as a layer) and grows or shrinks its footprint.</summary>
    public override void ScaleRadii(double s) { Rx *= s; Rz *= s; }
}

/// <summary>A point inside a body around which ore gets richer.</summary>
readonly record struct HotSpot(double X, double Y, double Z, double Radius);

public enum TellKind
{
    /// <summary>The body breaks the ground or lies within a metre and a half of it.</summary>
    Outcrop,
    /// <summary>Discoloured ground over the whole footprint (centre and radius).</summary>
    Stain,
    /// <summary>Cracked rock where a deep cluster's stringer comes near the surface.</summary>
    Fracture,
}

/// <summary>A surface hint at a deposit: something a player can see from above ground.</summary>
public readonly record struct Tell(TellKind Kind, double X, double Z, double Radius);

/// <summary>
/// One deposit's ore body, ready for the terrain: is this cell ore, how rich, and what shows on
/// the surface. Cheap to ask per cell (a handful of distance tests and one noise sample, inside a
/// bounding box), deterministic from its seed, and never above the ground or deeper than the
/// rules' maximum depth under it.
/// </summary>
public sealed class DepositBody
{
    internal readonly List<Part> Parts = new();
    internal readonly List<HotSpot> Hot = new();
    internal HeightGrid Ground;
    internal Lumps Lumps;
    internal double LumpAmplitude = 0.35;
    internal double MaxDepth = 12;
    readonly double[] _yield = new double[4];

    public string Ore { get; init; }
    public DepositShape Shape { get; init; }
    public Grade BaseGrade { get; init; }
    public double Amount { get; init; }
    public double X { get; init; }
    public double Z { get; init; }
    /// <summary>The ore volume the rules asked for, m³.</summary>
    public double TargetVolume { get; init; }

    /// <summary>Cells to test: every ore cell centre lies in [Min, Max].</summary>
    public int MinX { get; private set; }
    public int MinY { get; private set; }
    public int MinZ { get; private set; }
    public int MaxX { get; private set; }
    public int MaxY { get; private set; }
    public int MaxZ { get; private set; }

    public List<Tell> Tells { get; } = new();

    internal void SetYields(DepositRules rules)
    {
        for (int g = 0; g < 4; g++) _yield[g] = rules.Yield((Grade)g);
    }

    internal void ComputeBounds()
    {
        double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
        foreach (Part p in Parts) p.Grow(ref minX, ref minY, ref minZ, ref maxX, ref maxY, ref maxZ);
        double pad = LumpAmplitude * 1.6 + 0.5;
        MinX = (int)Math.Floor(minX - pad); MinY = (int)Math.Floor(minY - pad); MinZ = (int)Math.Floor(minZ - pad);
        MaxX = (int)Math.Ceiling(maxX + pad); MaxY = (int)Math.Ceiling(maxY + pad); MaxZ = (int)Math.Ceiling(maxZ + pad);
    }

    /// <summary>The ground height the body was grown under.</summary>
    public double SurfaceAt(double x, double z) => Ground.At(x, z);

    /// <summary>True if the point (a cell centre, say) is ore.</summary>
    public bool Contains(double x, double y, double z)
    {
        if (x < MinX || x > MaxX + 1 || y < MinY || y > MaxY + 1 || z < MinZ || z > MaxZ + 1) return false;
        double ground = Ground.At(x, z);
        if (y > ground || y < ground - MaxDepth) return false;
        double d = double.MaxValue;
        foreach (Part p in Parts)
        {
            d = Math.Min(d, p.Distance(x, y, z, Ground));
            if (d < -LumpAmplitude) return true;
        }
        if (d > LumpAmplitude) return false;
        return d + LumpAmplitude * Lumps.Sample(x, y, z) < 0;
    }

    /// <summary>How rich the ore is at a point inside the body.</summary>
    public Grade GradeAt(double x, double y, double z)
    {
        double hot = 0;
        foreach (HotSpot h in Hot)
        {
            double dx = x - h.X, dy = y - h.Y, dz = z - h.Z;
            hot = Math.Max(hot, 1 - Math.Sqrt(dx * dx + dy * dy + dz * dz) / h.Radius);
        }
        Grade g = hot > 0.66 ? Grade.Bountiful : hot > 0.33 ? Grade.Rich : hot > 0 ? Grade.Medium : BaseGrade;
        return g < BaseGrade ? BaseGrade : g;
    }

    /// <summary>Ore units a dug cell gives at a point: the per-cell yield times its grade's multiplier.</summary>
    public double YieldAt(double x, double y, double z, int oreUnitsPerCell) => oreUnitsPerCell * _yield[(int)GradeAt(x, y, z)];

    /// <summary>Visits every ore cell (integer cell coordinates; the centre is +0.5).</summary>
    public int ForEachCell(Action<int, int, int, Grade> visit = null)
    {
        int n = 0;
        for (int z = MinZ; z <= MaxZ; z++)
            for (int x = MinX; x <= MaxX; x++)
                for (int y = MinY; y <= MaxY; y++)
                {
                    double cx = x + 0.5, cy = y + 0.5, cz = z + 0.5;
                    if (!Contains(cx, cy, cz)) continue;
                    n++;
                    visit?.Invoke(x, y, z, GradeAt(cx, cy, cz));
                }
        return n;
    }

    /// <summary>Total ore a fully dug body gives.</summary>
    public double TotalOre(int oreUnitsPerCell)
    {
        double total = 0;
        ForEachCell((x, y, z, g) => total += oreUnitsPerCell * _yield[(int)g]);
        return total;
    }
}
