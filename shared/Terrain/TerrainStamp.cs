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
/// <summary>What the ground of a stamped place is made of: its own land, or a made surface laid on it.</summary>
public enum StampSurface : byte { Natural, Concrete, Tarmac, Gravel, Dirt }

/// <summary>What a stamp does to the ground: see <see cref="TerrainStamp.Shape"/>.</summary>
public enum StampShape : byte { Pad, Bowl, Terraces, Cut, Tilt, Furrow, Road }

/// <summary>What a stretch of a road stamp does: lies on the ground (cut and filled to its deck), crosses on a bridge (the ground is left alone), or runs in a tunnel (the ground is left alone and a bore is kept open).</summary>
public enum RoadSpan : byte { Ground, Bridge, Tunnel }

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

    /// <summary>The middle of the footprint in world metres (x, z; canonical unless the stamp straddles a seam).</summary>
    public double CenterX => _cx;
    public double CenterZ => _cz;

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
        if (Shape == StampShape.Road) return RoadWeight(x, z);
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
        if (Shape == StampShape.Road) return InBore(x + 0.5, y + 0.5, z + 0.5);
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
    public int CarveBottom => Shape == StampShape.Road ? (_boreBottom == float.MaxValue ? PadY : (int)MathF.Floor(_boreBottom) - 1)
        : _carve != null ? _carveY : PadY - (Shape is StampShape.Bowl or StampShape.Furrow ? (int)Math.Ceiling(Depth) + 1 : 0);


    // ---- shapes beyond the flat pad ----------------------------------------------------------
    // A stamp is a pad unless it says otherwise. The others ground a place in its landscape and
    // compose with any pad under them (Compose applies them in list order): a crater bowl, terraces
    // stepping down a slope, an approach cut, a tilt that half-buries a ruin, an impact furrow.

    /// <summary>The made ground on a pad: concrete or tarmac aprons, gravel or packed dirt yards, or the land's own ground (Natural).</summary>
    public StampSurface Surface { get; init; } = StampSurface.Natural;
    /// <summary>How long it has been left: 0 kept up, 1 a century of weather (cracked, sand and weeds drifted over).</summary>
    public float Wear { get; init; }

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
        StampShape.Road => Math.Max(_halfX, _halfZ) + HalfWidth + BankMax + StripBlend,
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
                // Shift: how far in from the low edge the rise starts (the way in stays at the pad's height).
                float target = PadY + Grade * (float)Math.Clamp(along + half - Shift, 0, 2 * half);
                return height + (target - height) * w;
            }
            case StampShape.Road:
            {
                // A road bed: its deck along the path (cut into rising ground, built up over dips),
                // banks easing back to the land at a walkable slope. Tunnels leave the ground alone
                // (their mouths are faces in the slope); under a bridge the ground is the land's own,
                // except where the embankment's end spills down under it round the abutment.
                if (!NearestOnPath(x, z, out _, out _, out RoadSpan span) || span == RoadSpan.Tunnel) return height;
                if (!NearestOnPath(x, z, out float s, out float target, out _, groundOnly: true)) return height;
                // A century of frost and roots buckles a neglected deck: a gentle heave, never a step.
                if (Buckle > 0f) target += Buckle * (Heave(x, z) * 2f - 1f);
                if (s <= HalfWidth) return target;
                float ease = MathF.Min(BankMax, MathF.Max(StripBlend, MathF.Abs(target - height) * 2.2f));
                if (s >= HalfWidth + ease) return height;
                float w = 1f - Smooth((s - HalfWidth) / ease);
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

    // ---- roads ---------------------------------------------------------------------------------
    // A road stamp lays a stretch of road, rail bed or track along a path of points (each with its
    // deck height), and says per stretch whether it lies on the ground, crosses a bridge or runs in a
    // tunnel. Points are metres from the footprint's centre, so the stamp is seam-safe like any other.

    float[] _px, _pz, _py;
    RoadSpan[] _spans;
    float _boreBottom = float.MaxValue;

    /// <summary>Road: half the width of the deck that is laid level across (carriageway and shoulders).</summary>
    public float HalfWidth { get; init; }
    /// <summary>Road: how far its banks may reach beyond the deck (past that, the road sits in a cutting or on an embankment steeper than its banks).</summary>
    public float BankMax { get; init; } = 28f;
    /// <summary>Road: how far its deck heaves up and down (a neglected road's buckling), metres.</summary>
    public float Buckle { get; init; }

    /// <summary>Smooth noise in [0, 1] on an 8 m lattice that repeats exactly round the planet (so a road heaves the same on both sides of a seam).</summary>
    float Heave(double x, double z)
    {
        int nx = Math.Max(1, (int)Math.Round(_width / 8)), nz = Math.Max(1, (int)Math.Round(_height / 8));
        double gx = (x % _width + _width) % _width / 8, gz = (z % _height + _height) % _height / 8;
        int ix = (int)Math.Floor(gx), iz = (int)Math.Floor(gz);
        float tx = (float)(gx - ix), tz = (float)(gz - iz);
        tx = tx * tx * (3 - 2 * tx);
        tz = tz * tz * (3 - 2 * tz);
        float L(int i, int k)
        {
            ulong h = (ulong)(((i % nx + nx) % nx) * 73856093L ^ ((k % nz + nz) % nz) * 19349663L ^ 0x5EAL);
            h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33;
            return (h & 0xFFFF) / 65535f;
        }
        float a = L(ix, iz), b = L(ix + 1, iz), c = L(ix, iz + 1), d = L(ix + 1, iz + 1);
        return (a + (b - a) * tx) * (1 - tz) + (c + (d - c) * tx) * tz;
    }

    /// <summary>Road: a tunnel's bore, half its width and its height over the deck.</summary>
    public float BoreHalfWidth { get; init; } = 4f;
    public float BoreHeight { get; init; } = 6f;

    /// <summary>
    /// A road stamp along a path of world points (x, z continuous across a seam, y the deck) with
    /// one span kind per stretch (point i to i + 1). Its footprint is the path's bounding box.
    /// </summary>
    public static TerrainStamp Road(string name, System.Collections.Generic.IReadOnlyList<double> xs, System.Collections.Generic.IReadOnlyList<double> zs,
        System.Collections.Generic.IReadOnlyList<float> ys, System.Collections.Generic.IReadOnlyList<RoadSpan> spans,
        float halfWidth, StampSurface surface, float wear, double planetWidth, double planetHeight, float boreHalfWidth = 4f, float boreHeight = 6f, float buckle = 0f)
    {
        double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
        for (int i = 0; i < xs.Count; i++)
        {
            x0 = Math.Min(x0, xs[i]); x1 = Math.Max(x1, xs[i]);
            z0 = Math.Min(z0, zs[i]); z1 = Math.Max(z1, zs[i]);
        }
        // Whole cells round the path, moved by whole planets so the centre is on the map.
        double shiftX = Math.Floor((x0 + x1) * 0.5 / planetWidth) * planetWidth, shiftZ = Math.Floor((z0 + z1) * 0.5 / planetHeight) * planetHeight;
        int minX = (int)Math.Floor(x0 - shiftX), maxX = (int)Math.Ceiling(x1 - shiftX) + 1, minZ = (int)Math.Floor(z0 - shiftZ), maxZ = (int)Math.Ceiling(z1 - shiftZ) + 1;
        float lowest = float.MaxValue;
        foreach (float y in ys) lowest = Math.Min(lowest, y);
        var t = new TerrainStamp(name, minX, minZ, maxX, maxZ, (int)MathF.Floor(lowest), 0f, 0f, planetWidth, planetHeight)
        {
            Shape = StampShape.Road, HalfWidth = halfWidth, Surface = surface, Wear = wear, BoreHalfWidth = boreHalfWidth, BoreHeight = boreHeight, Buckle = buckle,
        };
        int n = xs.Count;
        t._px = new float[n];
        t._pz = new float[n];
        t._py = new float[n];
        for (int i = 0; i < n; i++)
        {
            t._px[i] = (float)(xs[i] - shiftX - t._cx);
            t._pz[i] = (float)(zs[i] - shiftZ - t._cz);
            t._py[i] = ys[i];
        }
        t._spans = new RoadSpan[Math.Max(0, n - 1)];
        for (int i = 0; i + 1 < n; i++)
        {
            t._spans[i] = spans[i];
            if (spans[i] == RoadSpan.Tunnel) t._boreBottom = Math.Min(t._boreBottom, Math.Min(ys[i], ys[i + 1]));
        }
        return t;
    }

    /// <summary>The path's points (metres from the centre) and deck heights, and each stretch's span (read-only views).</summary>
    public int PathCount => _px?.Length ?? 0;
    public (float X, float Z, float Y) PathPoint(int i) => (_px[i], _pz[i], _py[i]);
    public RoadSpan PathSpan(int i) => _spans[i];

    /// <summary>
    /// The nearest point of the path to a world point: how far off it is, the deck height there and
    /// the span it is on. False when the point is beyond the stamp's reach or the path is empty.
    /// </summary>
    public bool NearestOnPath(double x, double z, out float distance, out float deck, out RoadSpan span, bool groundOnly = false, bool skipTunnels = false)
    {
        distance = float.MaxValue;
        deck = 0f;
        span = RoadSpan.Ground;
        if (_px == null || _px.Length < 2) return false;
        Offset(x, z, out double dxd, out double dzd);
        float reach = HalfWidth + BankMax + StripBlend;
        if (Math.Abs(dxd) > _halfX + reach || Math.Abs(dzd) > _halfZ + reach) return false;
        float dx = (float)dxd, dz = (float)dzd;
        float best = float.MaxValue;
        for (int i = 0; i + 1 < _px.Length; i++)
        {
            if (groundOnly && _spans[i] != RoadSpan.Ground || skipTunnels && _spans[i] == RoadSpan.Tunnel) continue;
            float ax = _px[i], az = _pz[i], bx = _px[i + 1], bz = _pz[i + 1];
            float ex = bx - ax, ez = bz - az, len2 = ex * ex + ez * ez;
            float u = len2 > 1e-6f ? Math.Clamp(((dx - ax) * ex + (dz - az) * ez) / len2, 0f, 1f) : 0f;
            float qx = ax + ex * u - dx, qz = az + ez * u - dz, d2 = qx * qx + qz * qz;
            if (d2 >= best) continue;
            best = d2;
            deck = _py[i] + (_py[i + 1] - _py[i]) * u;
            span = _spans[i];
        }
        if (best == float.MaxValue) return false;
        distance = MathF.Sqrt(best);
        return true;
    }

    float RoadWeight(double x, double z)
    {
        if (!NearestOnPath(x, z, out float s, out _, out RoadSpan span)) return 0f;
        // A tunnel only keeps caves out of its bore's way, and a bridge keeps trees from growing up through its
        // deck (a trace of weight each); the ground itself is left as it was.
        if (span == RoadSpan.Tunnel) return s <= BoreHalfWidth + 2f ? 0.01f : 0f;
        if (span == RoadSpan.Bridge) return s <= HalfWidth + 3f ? 0.01f : 0f;
        if (s <= HalfWidth) return 1f;
        const float Edge = 3f;
        return s >= HalfWidth + Edge ? 0f : 1f - Smooth((s - HalfWidth) / Edge);
    }

    /// <summary>Whether a point is inside a tunnel's bore: under an arched roof over the deck, within its width.</summary>
    bool InBore(double x, double y, double z)
    {
        if (_boreBottom == float.MaxValue || y < _boreBottom - 1) return false;
        if (!NearestOnPath(x, z, out float s, out float deck, out RoadSpan span) || span != RoadSpan.Tunnel || s > BoreHalfWidth) return false;
        float across = s / BoreHalfWidth, roof = BoreHeight - 1.5f * across * across;
        return y >= deck && y < deck + roof;
    }

    /// <summary>The ground height at a point with this stamp laid over <paramref name="height"/>: eased toward the pad by its weight.</summary>
    public float Apply(float height, double x, double z) => Shaped(height, x, z);

    /// <summary>
    /// How stamps combine, the one rule every height query uses: in list order, each eases the
    /// height so far toward its own target by its own weight. Off its footprint and apron a stamp
    /// only blends, so pads that overlap meet smoothly, and a later stamp (a road, a rail bed laid
    /// after the places it serves) wins wherever its weight is 1. Order the list by what should win.
    /// Of the road stamps only the one whose path is nearest applies (tunnels aside): where two
    /// roads' beds overlap (the legs of a switchback, a road beside a railway) each keeps its own
    /// deck, and they meet at a step halfway, as a retaining wall would.
    /// </summary>
    public static float Compose(System.Collections.Generic.IReadOnlyList<TerrainStamp> stamps, float natural, double x, double z)
    {
        int road = OnPad(stamps, null, x, z) ? -1 : NearestRoad(stamps, null, x, z);
        float h = natural;
        for (int i = 0; i < stamps.Count; i++)
            if (stamps[i].Shape != StampShape.Road || i == road) h = stamps[i].Apply(h, x, z);
        return h;
    }

    /// <summary>Whether a point is on a place's footprint or apron: roads never change the ground there (the place's pad rules it).</summary>
    public static bool OnPad(System.Collections.Generic.IReadOnlyList<TerrainStamp> stamps, int[] only, double x, double z)
    {
        int n = only?.Length ?? stamps.Count;
        for (int k = 0; k < n; k++)
        {
            TerrainStamp t = stamps[only != null ? only[k] : k];
            if (t.Shape == StampShape.Pad && t.Weight(x, z) >= 1f) return true;
        }
        return false;
    }

    /// <summary>The road stamp (index into <paramref name="stamps"/>, or among <paramref name="only"/> when given) whose path, tunnels aside, is nearest a point; -1 if none reaches it.</summary>
    public static int NearestRoad(System.Collections.Generic.IReadOnlyList<TerrainStamp> stamps, int[] only, double x, double z)
    {
        int best = -1;
        float bestD = float.MaxValue;
        int n = only?.Length ?? stamps.Count;
        for (int k = 0; k < n; k++)
        {
            int i = only != null ? only[k] : k;
            TerrainStamp t = stamps[i];
            if (t.Shape != StampShape.Road || !t.NearestOnPath(x, z, out float d, out _, out _, skipTunnels: true) || d >= bestD) continue;
            bestD = d;
            best = i;
        }
        return best;
    }

    static double Wrap(double d, double span)
    {
        d %= span;
        if (d > span * 0.5) d -= span;
        else if (d < -span * 0.5) d += span;
        return d;
    }
}
