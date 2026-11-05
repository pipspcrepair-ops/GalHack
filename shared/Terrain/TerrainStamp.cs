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
    public double ReachX => _halfX + Apron + Blend;
    public double ReachZ => _halfZ + Apron + Blend;

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
    public int CarveBottom => _carve == null ? PadY : _carveY;

    /// <summary>The ground height at a point with this stamp laid over <paramref name="height"/>: eased toward the pad by its weight.</summary>
    public float Apply(float height, double x, double z)
    {
        float w = Weight(x, z);
        return w > 0f ? height + (PadY - height) * w : height;
    }

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
