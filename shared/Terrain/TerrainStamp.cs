using System;

namespace GalHack.Terrain;

/// <summary>
/// A level pad for something built on the ground, usually a point of interest. Inside the
/// footprint and a flat apron around it the ground is exactly at <see cref="PadY"/>: higher
/// ground is cut away, lower ground is filled, so the building's floor rests on solid ground at
/// every corner and its doors open onto level ground. Beyond the apron the pad blends back into
/// the natural terrain over <see cref="Blend"/> metres, wide enough that the cut and fill slopes
/// stay climbable. Cells listed in the carve mask (rooms a mine or a buried lab digs below the
/// pad) are kept open.
///
/// A stamp is part of the seeded terrain, not a player edit: it is a pure function of the planet
/// seed, so every machine generates the same pad, and digging rules still apply on top of it.
/// Coordinates are world cells on the torus; distances wrap across both seams.
/// </summary>
/// <summary>What a stamp does to the ground: see <see cref="TerrainStamp.Shape"/>.</summary>
public enum StampShape : byte { Pad, Bowl, Terraces, Cut, Tilt, Furrow }

public sealed class TerrainStamp
{
    public readonly string Name;

    /// <summary>Footprint in world cells, min inclusive, max exclusive (canonical: inside the map).</summary>
    public readonly int MinX, MinZ, MaxX, MaxZ;

    /// <summary>World y of the pad surface: cells below it are ground, cells from it up are open.</summary>
    public readonly int PadY;

    public readonly float Apron, Blend;

    readonly double _width, _height, _cx, _cz, _halfX, _halfZ;
    readonly int _carveX, _carveY, _carveZ, _carveW, _carveH, _carveD;
    readonly bool[] _carve;

    public TerrainStamp(string name, int minX, int minZ, int maxX, int maxZ, int padY, float apron, float blend,
        double planetWidth, double planetHeight, int carveX = 0, int carveY = 0, int carveZ = 0, int carveW = 0, int carveH = 0, int carveD = 0, bool[] carve = null)
    {
        Name = name;
        MinX = minX;
        MinZ = minZ;
        MaxX = maxX;
        MaxZ = maxZ;
        PadY = padY;
        Apron = apron;
        Blend = blend;
        _width = planetWidth;
        _height = planetHeight;
        _cx = (minX + maxX) * 0.5;
        _cz = (minZ + maxZ) * 0.5;
        _halfX = (maxX - minX) * 0.5;
        _halfZ = (maxZ - minZ) * 0.5;
        _carveX = carveX;
        _carveY = carveY;
        _carveZ = carveZ;
        _carveW = carveW;
        _carveH = carveH;
        _carveD = carveD;
        _carve = carve;
    }

    /// <summary>Half-size of the area the stamp can change, metres from its centre on each axis.</summary>
    public double ReachX => Math.Max(_halfX + Apron + Blend, ShapeReach);
    public double ReachZ => Math.Max(_halfZ + Apron + Blend, ShapeReach);

    /// <summary>Signed offsets from the footprint centre, taking the short way round both seams.</summary>
    public void Offset(double x, double z, out double dx, out double dz)
    {
        dx = Wrap(x - _cx, _width);
        dz = Wrap(z - _cz, _height);
    }

    /// <summary>How much of the pad applies at a point: 1 on the footprint and apron, easing to 0 at the blend's edge.</summary>
    public float Weight(double x, double z)
    {
        Offset(x, z, out double dx, out double dz);
        double ox = Math.Max(0, Math.Abs(dx) - _halfX), oz = Math.Max(0, Math.Abs(dz) - _halfZ);
        if (ox > Apron + Blend || oz > Apron + Blend) return 0f;
        float d = (float)Math.Sqrt(ox * ox + oz * oz);
        if (d <= Apron) return 1f;
        float t = Math.Clamp((d - Apron) / Blend, 0f, 1f);
        return 1f - t * t * (3f - 2f * t);
    }

    /// <summary>Whether a footprint cell (world x, z) lies on the stamp's footprint itself.</summary>
    public bool OnFootprint(double x, double z)
    {
        Offset(x + 0.5, z + 0.5, out double dx, out double dz);
        return Math.Abs(dx) < _halfX && Math.Abs(dz) < _halfZ;
    }

    /// <summary>Whether a cell must stay open: part of a room the building digs below its pad.</summary>
    public bool IsCarved(int x, int y, int z)
    {
        if (_carve == null) return false;
        int ly = y - _carveY;
        if ((uint)ly >= (uint)_carveH) return false;
        // Local x and z the short way round the seams, from the carve box's corner.
        int lx = (int)Math.Floor(Wrap(x - _carveX + 0.5 - _carveW * 0.5, _width) + _carveW * 0.5);
        int lz = (int)Math.Floor(Wrap(z - _carveZ + 0.5 - _carveD * 0.5, _height) + _carveD * 0.5);
        if ((uint)lx >= (uint)_carveW || (uint)lz >= (uint)_carveD) return false;
        return _carve[(ly * _carveD + lz) * _carveW + lx];
    }

    /// <summary>Lowest world y any carved cell reaches, or <see cref="PadY"/> when nothing is carved.</summary>
    public int CarveBottom => _carve != null ? _carveY : PadY - (Shape is StampShape.Bowl or StampShape.Furrow ? (int)Math.Ceiling(Depth) + 1 : 0);


    // ---- shapes beyond the flat pad ----------------------------------------------------------
    // A stamp is a pad unless it says otherwise. The others ground a place in its landscape and
    // compose with any pad under them (Compose applies them in list order): a crater bowl, terraces
    // stepping down a slope, an approach cut, a tilt that half-buries a ruin, an impact furrow.

    /// <summary>The stamp's shape: Pad (the default), Bowl, Terraces, Cut, Tilt or Furrow.</summary>
    public StampShape Shape { get; init; } = StampShape.Pad;
    /// <summary>Bowl: radius, depth below the pad and rim height above it. Furrow: depth.</summary>
    public float Radius { get; init; }
    public float Depth { get; init; }
    public float Rim { get; init; }
    /// <summary>Terraces, Cut, Tilt, Furrow: the way out from the place (unit vector), and the strip's length and width.</summary>
    public float DirX { get; init; }
    public float DirZ { get; init; }
    public float Length { get; init; }
    public float Width { get; init; }
    /// <summary>Terraces: the drop of each step. Cut: the grade (metres up per metre out). Tilt: metres up per metre in.</summary>
    /// <summary>Cut: how far across the footprint the strip runs from its centre line (to line it up with the way in).</summary>
    public float Shift { get; init; }
    public float Step { get; init; } = 1.5f;
    public float Grade { get; init; }
    /// <summary>Directional shapes: a box (world cells) they never touch, normally the place's own footprint, so they shape only the land round it.</summary>
    public int KeepMinX { get; init; }
    public int KeepMinZ { get; init; }
    public int KeepMaxX { get; init; }
    public int KeepMaxZ { get; init; }

    bool Kept(double x, double z)
    {
        if (KeepMaxX <= KeepMinX) return false;
        double dx = Wrap(x - (KeepMinX + KeepMaxX) * 0.5, _width), dz = Wrap(z - (KeepMinZ + KeepMaxZ) * 0.5, _height);
        return Math.Abs(dx) < (KeepMaxX - KeepMinX) * 0.5 && Math.Abs(dz) < (KeepMaxZ - KeepMinZ) * 0.5;
    }

    const float TreadWidth = 6f, StripBlend = 4f;

    /// <summary>How far from the footprint centre the stamp can reach on each axis, whatever its shape.</summary>
    double ShapeReach => Shape switch
    {
        StampShape.Bowl => Radius * 1.5 + Blend,
        StampShape.Terraces => Math.Max(_halfX, _halfZ) + Length * 2 + Width + 40,
        StampShape.Cut => Math.Max(_halfX, _halfZ) + Length * 2 + Width + 40,
        StampShape.Furrow => Math.Max(_halfX, _halfZ) + Length + Width + StripBlend,
        _ => 0,
    };

    /// <summary>Along the stamp's direction from the footprint's edge (t), and across it (s).</summary>
    void Strip(double x, double z, out double t, out double s)
    {
        Offset(x, z, out double dx, out double dz);
        double along = dx * DirX + dz * DirZ;
        double half = Math.Abs(DirX) * _halfX + Math.Abs(DirZ) * _halfZ;
        t = along - half;
        s = Math.Abs(-dx * DirZ + dz * DirX - Shift);
    }

    static float Smooth(float t) { t = Math.Clamp(t, 0f, 1f); return t * t * (3f - 2f * t); }

    /// <summary>The height this stamp wants at a point and how much it applies there, given the height so far.</summary>
    float Shaped(float height, double x, double z)
    {
        if (Shape != StampShape.Pad && Kept(x, z)) return height;
        switch (Shape)
        {
            case StampShape.Bowl:
            {
                // A crater: a glassy bowl, a thrown rim, then back to the land round it.
                Offset(x, z, out double dx, out double dz);
                float d = (float)Math.Sqrt(dx * dx + dz * dz), rw = Radius * 0.5f;
                float target, w = 1f;
                if (d <= Radius) target = PadY + Rim - (Depth + Rim) * (1f - d * d / (Radius * Radius));
                else if (d <= Radius + rw) target = PadY + Rim * (1f - (d - Radius) / rw);
                else { target = PadY; w = 1f - Smooth((d - Radius - rw) / Blend); }
                return height + (target - height) * w;
            }
            case StampShape.Terraces:
            {
                // Steps down the slope from the pad: level treads, smooth risers, only ever filling.
                Strip(x, z, out double t, out double s);
                if (t < 0) return height;
                float u = (float)t / TreadWidth, k = MathF.Floor(u);
                float target = PadY - Step * (k + Smooth((u - k - 0.4f) / 0.6f));
                if (target <= height) return height;
                // Past the strip's end and sides the fill eases off over a run long enough to stay walkable.
                float ease = MathF.Max(StripBlend, (target - height) * 3.5f);
                if (t > Length + ease || s > Width / 2 + ease) return height;
                float w = (1f - Smooth(((float)s - Width / 2) / ease)) * (1f - Smooth(((float)t - Length) / ease));
                return height + (target - height) * w;
            }
            case StampShape.Cut:
            {
                // An approach cut: a road bed out from the way in at an easy grade, cut into the hill or built up.
                Strip(x, z, out double t, out double s);
                if (t < -Apron) return height;
                float target = PadY + Grade * (float)Math.Max(0, Math.Min(t, Length));
                // Its banks and end ease back to the land over a run long enough to stay walkable.
                float ease = MathF.Max(StripBlend, MathF.Abs(target - height) * 2.5f);
                if (t > Length + ease || s > Width / 2 + ease) return height;
                float w = (1f - Smooth(((float)s - Width / 2) / ease)) * (1f - Smooth(((float)t - Length) / ease));
                return height + (target - height) * w;
            }
            case StampShape.Tilt:
            {
                // Half-buried: the ground rises across the footprint away from the way in.
                float w = Weight(x, z);
                if (w <= 0f) return height;
                Offset(x, z, out double dx, out double dz);
                double along = dx * DirX + dz * DirZ;
                double half = Math.Abs(DirX) * _halfX + Math.Abs(DirZ) * _halfZ;
                float target = PadY + Grade * (float)Math.Clamp(along + half, 0, 2 * half);
                return height + (target - height) * w;
            }
            case StampShape.Furrow:
            {
                // An impact gouge: a trench ploughed out behind a wreck, deepest where it came to rest.
                Strip(x, z, out double t, out double s);
                if (t < 0 || t > Length || s > Width / 2) return height;
                float across = (float)(s / (Width / 2));
                float rampIn = Math.Min(1f, (float)t / 6f);   // no step where it leaves the wreck
                return height - Depth * rampIn * (1f - (float)t / Length) * (1f - across * across);
            }
            default:
            {
                float w = Weight(x, z);
                return w > 0f ? height + (PadY - height) * w : height;
            }
        }
    }

    /// <summary>The ground height at a point with this stamp laid over <paramref name="height"/>: eased toward the pad by its weight.</summary>
    public float Apply(float height, double x, double z) => Shaped(height, x, z);

    /// <summary>
    /// How stamps combine, the one rule every height query uses: in list order, each eases the
    /// height so far toward its own target by its own weight. Off its footprint and apron a stamp
    /// only blends, so pads that overlap meet smoothly, and a later stamp (a road, a rail bed laid
    /// after the places it serves) wins wherever its weight is 1. Order the list by what should win.
    /// </summary>
    public static float Compose(System.Collections.Generic.IReadOnlyList<TerrainStamp> stamps, float natural, double x, double z)
    {
        float h = natural;
        for (int i = 0; i < stamps.Count; i++) h = stamps[i].Apply(h, x, z);
        return h;
    }

    static double Wrap(double d, double span)
    {
        d %= span;
        if (d > span * 0.5) d -= span;
        else if (d < -span * 0.5) d += span;
        return d;
    }
}
