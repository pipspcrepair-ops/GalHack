using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Planets.Gen;
using TerrainDigging.Terrain;

namespace GalHack.Terrain;

/// <summary>
/// The real planet as diggable 1 m voxel terrain: terrain-digging's <see cref="ITerrainSource"/>
/// built on the planet generator's height function. Every cell is a pure function of the planet
/// spec and the stamps, so edits (stored as differences) land on identical ground on every
/// machine and after every reload.
///
/// What a column holds, top down:
/// - the natural surface: <see cref="SurfaceSampler"/> at 1 m spacing (mountains, plateaus,
///   canyons, river channels...), then any <see cref="TerrainStamp"/> pads on top;
/// - soil (sand, loam, ash or snow by archetype; thinner on slopes, none on cliffs), rock, deep rock;
/// - bedrock from <see cref="BedrockDepth"/> metres in: nothing cuts it;
/// - caves: winding tubes 3 to 36 m down where two periodic noise fields both cross zero, now and
///   then breaking the surface as a cave mouth;
/// - ore: a lumpy body under every deposit on the planet generator's deposit map, surface
///   outcrops breaking the ground, shallow ones a few metres down.
///
/// The map is a torus, so x and z wrap: chunks past either edge (the "ghost" chunks used near a
/// seam) generate exactly the cells of their counterpart on the other side.
/// Thread-safe: chunks generate on mesh workers.
/// </summary>
public sealed class PlanetTerrainSource : ITerrainSource
{
    public const float BedrockDepth = 30f;
    public const float CaveTop = 3f, CaveBottom = 36f;
    const int S = ChunkStore.Size, P = S + 2;
    const float Limit = ChunkStore.DensityLimit;

    public readonly WorldMap Map;
    public readonly SurfaceSampler Sampler;
    public readonly IReadOnlyList<TerrainStamp> Stamps;

    /// <summary>Planet size in metres (= cells), and in chunks.</summary>
    public readonly int Width, Height, ChunksX, ChunksZ;

    public int Seed { get; }
    public string GeneratorId { get; }
    public float WaterLevel => 0f;
    public IReadOnlyList<PoiSite> PointsOfInterest { get; }

    readonly PeriodicNoise _caveA, _caveB, _caveRegion, _soil, _ore;
    readonly float _soilBase;
    readonly ConcurrentDictionary<long, Lazy<Column>> _columns = new();

    /// <summary>Surface heights for one chunk column: its 16 x 16 cells plus a one-cell ring, at cell centres.</summary>
    public sealed class Column
    {
        public readonly float[] Heights = new float[P * P];
        public float Min = float.MaxValue, Max = float.MinValue;
    }

    public PlanetTerrainSource(WorldMap map, IReadOnlyList<TerrainStamp> stamps = null, IReadOnlyList<PoiSite> pois = null)
    {
        Map = map;
        Sampler = new SurfaceSampler(map);
        Stamps = stamps ?? Array.Empty<TerrainStamp>();
        PointsOfInterest = pois ?? Array.Empty<PoiSite>();
        Width = (int)Math.Round(map.WidthMeters);
        Height = (int)Math.Round(map.HeightMeters);
        if (Width % S != 0 || Height % S != 0) throw new ArgumentException($"planet size {Width} x {Height} m must be whole chunks");
        ChunksX = Width / S;
        ChunksZ = Height / S;

        PlanetSpec spec = map.Spec;
        Seed = (int)(uint)(spec.Seed ^ (spec.Seed >> 32));
        ulong stampHash = 14695981039346656037UL;
        foreach (TerrainStamp t in Stamps)
            stampHash = (stampHash ^ (ulong)(t.MinX * 73856093 ^ t.MinZ * 19349663 ^ t.PadY * 83492791 ^ t.MaxX * 2654435761)) * 1099511628211UL;
        // Saves record this and refuse to load against anything else: same planet, same stamps, same generator version.
        GeneratorId = $"planet-surface/1 {spec.Archetype}/{spec.Variant} seed {spec.Seed} mods {(int)spec.Modifiers} {spec.WidthKm} km stamps {stampHash:X16}";

        ulong salt = Hash.Of(spec.Seed, 0xCA7E);
        _caveA = new PeriodicNoise(salt + 1, Width, Height, 52);
        _caveB = new PeriodicNoise(salt + 2, Width, Height, 52);
        _caveRegion = new PeriodicNoise(salt + 3, Width, Height, 400);
        _soil = new PeriodicNoise(salt + 4, Width, Height, 24);
        _ore = new PeriodicNoise(salt + 5, Width, Height, 3);
        _soilBase = map.Def.Archetype switch
        {
            Archetype.Arid => 3.2f,     // deep sand
            Archetype.Ice => 2.2f,      // packed snow and firn
            Archetype.Lava => 0.8f,     // a skin of ash on basalt
            Archetype.Alien => 1.6f,
            _ => 1.6f,                  // loam
        };
    }

    // ---- Heights --------------------------------------------------------------------------

    /// <summary>The planet generator's own ground height, without stamps. x and z wrap.</summary>
    public float NaturalHeight(double x, double z) => Sampler.Sample(x, z, 1).Height;

    /// <summary>Height of the undug surface at a point: the natural ground with every stamp applied.</summary>
    public float SurfaceHeight(float x, float z) => SurfaceHeight((double)x, z);

    public float SurfaceHeight(double x, double z, double spacing = 1)
    {
        float h = Sampler.Sample(x, z, spacing).Height;
        foreach (TerrainStamp t in Stamps)
        {
            float w = t.Weight(x, z);
            if (w > 0f) h += (t.PadY - h) * w;
        }
        return h;
    }

    /// <summary>Ground and river at a point, with stamps applied to the ground height.</summary>
    public GroundSample Ground(double x, double z, double spacing)
    {
        GroundSample s = Sampler.Sample(x, z, spacing);
        float h = s.Height;
        foreach (TerrainStamp t in Stamps)
        {
            float w = t.Weight(x, z);
            if (w > 0f) h += (t.PadY - h) * w;
        }
        return s with { Height = h };
    }

    /// <summary>How much any stamp covers a point (0 none, 1 on a pad).</summary>
    public float StampWeight(double x, double z)
    {
        float w = 0f;
        foreach (TerrainStamp t in Stamps) w = Math.Max(w, t.Weight(x, z));
        return w;
    }

    /// <summary>The stamp whose footprint or apron covers a point, if any.</summary>
    public TerrainStamp StampAt(double x, double z)
    {
        foreach (TerrainStamp t in Stamps)
            if (t.Weight(x, z) >= 1f) return t;
        return null;
    }

    /// <summary>Cached heights for a chunk column (any copy of it: the key wraps).</summary>
    public Column ColumnAt(int cx, int cz)
    {
        int wx = Mod(cx, ChunksX), wz = Mod(cz, ChunksZ);
        long key = (long)wx << 32 | (uint)wz;
        return _columns.GetOrAdd(key, static (k, self) => new Lazy<Column>(
            () => self.BuildColumn((int)(k >> 32), (int)(k & 0xFFFFFFFF)), LazyThreadSafetyMode.ExecutionAndPublication), this).Value;
    }

    Column BuildColumn(int cx, int cz)
    {
        var column = new Column();
        int ox = cx * S, oz = cz * S;
        for (int pz = 0; pz < P; pz++)
        for (int px = 0; px < P; px++)
        {
            float h = SurfaceHeight(ox + px - 0.5, oz + pz - 0.5);
            column.Heights[px + P * pz] = h;
            column.Min = Math.Min(column.Min, h);
            column.Max = Math.Max(column.Max, h);
        }
        return column;
    }

    /// <summary>Forgets cached columns the predicate picks (canonical chunk x, z). Safe at any time.</summary>
    public int ForgetColumns(Func<int, int, bool> drop)
    {
        int n = 0;
        foreach (long key in _columns.Keys)
            if (drop((int)(key >> 32), (int)(key & 0xFFFFFFFF)) && _columns.TryRemove(key, out _)) n++;
        return n;
    }

    public int CachedColumns => _columns.Count;

    /// <summary>
    /// Chunk ys worth meshing in a column, lowest and highest: from below its lowest ground (deep
    /// enough to reach the cave band where the column has caves, and any rooms a stamp carves)
    /// to just above its highest ground. Edited chunks are added by the caller.
    /// </summary>
    public (int lo, int hi) MeshRange(int cx, int cz)
    {
        Column column = ColumnAt(cx, cz);
        int ox = cx * S, oz = cz * S;
        float deepest = 3f;
        for (int k = 0; k < 5; k++)
        {
            double x = ox + (k == 4 ? 8 : (k & 1) * 15 + 0.5), z = oz + (k == 4 ? 8 : (k >> 1) * 15 + 0.5);
            if (CaveStrength(x, z, column.Max) > 0f) deepest = CaveBottom + 6f;
        }
        float bottom = column.Min - deepest;
        List<TerrainStamp> stamps = StampsNear(ox, oz);
        if (stamps != null) foreach (TerrainStamp t in stamps) bottom = Math.Min(bottom, t.CarveBottom - 2);
        return (FloorDiv((int)MathF.Floor(bottom), S), FloorDiv((int)MathF.Ceiling(column.Max + 2f), S));
    }

    static int FloorDiv(int a, int b) => a >= 0 ? a / b : (a - b + 1) / b;

    // ---- Chunks ---------------------------------------------------------------------------

    public void GenerateChunk(Int3 chunk, Span<float> density, Span<CellMaterial> material)
    {
        Column column = ColumnAt(chunk.X, chunk.Z);
        int ox = chunk.X * S, oy = chunk.Y * S, oz = chunk.Z * S;

        // Above everything in the column: open sky, no work.
        if (oy > column.Max + Limit + 1)
        {
            density.Fill(-Limit);
            material.Fill(CellMaterial.Air);
            return;
        }

        List<OreBody> deposits = OreBodiesNear(ox, oz);
        List<TerrainStamp> stamps = StampsNear(ox, oz);
        for (int lz = 0; lz < S; lz++)
        for (int lx = 0; lx < S; lx++)
        {
            int x = ox + lx, z = oz + lz;
            int hi = lx + 1 + P * (lz + 1);
            float h = column.Heights[hi];

            // Vertical distance overstates distance on slopes: dividing by the slope length makes
            // density roughly the true distance to the surface, so cliffs mesh as smooth faces.
            float sx = (column.Heights[hi + 1] - column.Heights[hi - 1]) * 0.5f, sz = (column.Heights[hi + P] - column.Heights[hi - P]) * 0.5f;
            float slope2 = sx * sx + sz * sz;
            float slopeScale = 1f / MathF.Sqrt(1f + slope2);

            // Soil slides off steep ground, so slopes and cliffs show bare rock.
            float soilDepth = _soilBase * (0.7f + 0.6f * _soil.Sample(x, 0, z)) * (1f - Smooth(0.35f, 1.2f, slope2));
            float rockDepth = 12f + 4f * _soil.Sample(x * 0.3, 50, z * 0.3);
            float caves = CaveStrength(x + 0.5, z + 0.5, h);
            bool padded = false;
            if (stamps != null)
                foreach (TerrainStamp t in stamps)
                    padded |= t.Weight(x + 0.5, z + 0.5) > 0f;
            if (padded) caves = 0f; // a place's foundations are never undercut by a cave

            for (int ly = 0; ly < S; ly++)
            {
                int i = lx + S * (lz + S * ly);
                int y = oy + ly;
                float depth = h - (y + 0.5f);
                float dist = depth * slopeScale;

                // Rooms a place digs below its pad stay open, however deep they go.
                bool carved = false;
                if (padded)
                    foreach (TerrainStamp t in stamps)
                        carved |= t.IsCarved(x, y, z);
                if (carved)
                {
                    density[i] = -Limit;
                    material[i] = CellMaterial.Air;
                    continue;
                }
                if (dist > BedrockDepth)
                {
                    density[i] = Limit;
                    material[i] = CellMaterial.Bedrock;
                    continue;
                }

                float d = dist;
                if (caves > 0f && depth > -1f) d = MathF.Min(d, CaveDensity(x + 0.5, y + 0.5, z + 0.5, depth, caves));
                d = Math.Clamp(d, -Limit, Limit);
                density[i] = d;

                if (d <= 0f)
                {
                    material[i] = CellMaterial.Air;
                    continue;
                }
                CellMaterial m = depth < soilDepth ? CellMaterial.Soil : dist < rockDepth ? CellMaterial.Rock : CellMaterial.DeepRock;
                if (deposits != null && InOre(deposits, x + 0.5, y + 0.5, z + 0.5)) m = CellMaterial.Ore;
                material[i] = m;
            }
        }
    }

    // ---- Caves ----------------------------------------------------------------------------

    /// <summary>
    /// 0 where there are no caves, up to 1 in cave country. Caves keep out of the sea, river
    /// channels and pads, and only some regions are riddled with them.
    /// </summary>
    float CaveStrength(double x, double z, float surface)
    {
        if (surface < 3f) return 0f;
        float region = _caveRegion.Sample(x, 0, z);
        return Smooth(0.0f, 0.25f, region);
    }

    /// <summary>
    /// Distance-like density (metres, positive inside rock) of the cave tubes at a point: where
    /// two noise fields are both near zero their zero-surfaces cross in a winding line, and the
    /// tube is the neighbourhood of that line. Squashing y makes passages run mostly level.
    /// </summary>
    float CaveDensity(double x, double y, double z, float depth, float strength)
    {
        if (depth < 0f || depth > CaveBottom + 4f) return Limit;
        float a = _caveA.Sample(x, y * 1.8, z), b = _caveB.Sample(x, y * 1.8, z);
        float r = MathF.Sqrt(a * a + b * b);
        float radius = 0.085f * strength;
        // About 22 m of ground per noise unit at this wavelength.
        float tube = (r - radius) * 22f;
        // Fade out above and below the cave band; now and then a passage reaches the surface:
        // a cave mouth, where the region noise is strongest.
        float top = strength > 0.85f ? 0f : CaveTop;
        float keep = Smooth(top - 1f, top + 2f, depth) * Smooth(CaveBottom + 4f, CaveBottom - 4f, depth);
        return keep > 0f ? tube + (1f - keep) * 6f : Limit;
    }

    // ---- Ore ------------------------------------------------------------------------------

    /// <summary>One deposit's ore body: a squat, lumpy lens centred below its deposit.</summary>
    readonly record struct OreBody(Deposit Deposit, double Y, float Radius);

    /// <summary>Ore bodies that could reach a chunk column, or null.</summary>
    List<OreBody> OreBodiesNear(int ox, int oz)
    {
        List<OreBody> list = null;
        foreach (Deposit d in Map.Deposits.InRect(ox - 6, oz - 6, ox + S + 6, oz + S + 6))
        {
            double dx = Wrap(d.X - (ox + S * 0.5), Width), dz = Wrap(d.Y - (oz + S * 0.5), Height);
            if (Math.Abs(dx) > S * 0.5 + 6 || Math.Abs(dz) > S * 0.5 + 6) continue;
            OreCentre(d, out _, out double y, out _);
            (list ??= new List<OreBody>()).Add(new OreBody(d, y, OreRadius(d)));
        }
        return list;
    }

    List<TerrainStamp> StampsNear(int ox, int oz)
    {
        List<TerrainStamp> list = null;
        foreach (TerrainStamp t in Stamps)
        {
            t.Offset(ox + S * 0.5, oz + S * 0.5, out double dx, out double dz);
            if (Math.Abs(dx) > t.ReachX + S && Math.Abs(dz) > t.ReachZ + S) continue;
            (list ??= new List<TerrainStamp>()).Add(t);
        }
        return list;
    }

    /// <summary>Radius in metres of a deposit's ore body.</summary>
    public static float OreRadius(Deposit d) => 1.4f + 1.3f * d.Amount;

    /// <summary>Centre of a deposit's ore body: outcrops break the surface, shallow ones sit a few metres down.</summary>
    public void OreCentre(Deposit d, out double x, out double y, out double z)
    {
        x = d.X;
        z = d.Y;
        float r = OreRadius(d);
        y = SurfaceHeight(d.X, d.Y) - d.Depth - (d.Depth > 0f ? r : r * 0.35f);
    }

    bool InOre(List<OreBody> bodies, double x, double y, double z) => OreAt(bodies, x, y, z, out _);

    bool OreAt(List<OreBody> bodies, double x, double y, double z, out DepositKind kind)
    {
        kind = default;
        foreach (OreBody b in bodies)
        {
            float r = b.Radius;
            double dy = (y - b.Y) * 1.3; // ore bodies are squat lenses
            if (Math.Abs(dy) > r + 1.5) continue;
            double dx = Wrap(x - b.Deposit.X, Width), dz = Wrap(z - b.Deposit.Y, Height);
            if (Math.Abs(dx) > r + 1.5 || Math.Abs(dz) > r + 1.5) continue;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            // A lumpy outline, so a dug face shows an irregular vein rather than a sphere.
            if (dist < r * (0.8 + 0.35 * _ore.Sample(x, y, z)))
            {
                kind = b.Deposit.Kind;
                return true;
            }
        }
        return false;
    }

    /// <summary>Which resource an ore cell holds: the deposit whose body it belongs to.</summary>
    public DepositKind OreKindAt(Int3 cell)
    {
        var list = OreBodiesNear(cell.X >> 4 << 4, cell.Z >> 4 << 4);
        if (list != null && OreAt(list, cell.X + 0.5, cell.Y + 0.5, cell.Z + 0.5, out DepositKind kind)) return kind;
        double best = double.MaxValue;
        DepositKind nearest = DepositKind.Iron;
        foreach (Deposit d in Map.Deposits.InRect(cell.X - 8, cell.Z - 8, cell.X + 8, cell.Z + 8))
        {
            double dx = Wrap(cell.X - d.X, Width), dz = Wrap(cell.Z - d.Y, Height), d2 = dx * dx + dz * dz;
            if (d2 < best) { best = d2; nearest = d.Kind; }
        }
        return nearest;
    }

    // ---- Single cells -----------------------------------------------------------------------

    /// <summary>
    /// The base density at one cell centre, computed directly (no chunk): for spot checks off
    /// the main thread, such as whether a tree's roots are in solid ground.
    /// </summary>
    public float CellDensity(int x, int y, int z)
    {
        float h = SurfaceHeight(x + 0.5, z + 0.5);
        float hx = SurfaceHeight(x + 1.5, z + 0.5) - SurfaceHeight(x - 0.5, z + 0.5);
        float hz = SurfaceHeight(x + 0.5, z + 1.5) - SurfaceHeight(x + 0.5, z - 0.5);
        float slopeScale = 1f / MathF.Sqrt(1f + (hx * hx + hz * hz) * 0.25f);
        float depth = h - (y + 0.5f), d = depth * slopeScale;
        foreach (TerrainStamp t in Stamps)
            if (t.IsCarved(x, y, z)) return -Limit;
        if (d > BedrockDepth) return Limit;
        float caves = StampWeight(x + 0.5, z + 0.5) > 0f ? 0f : CaveStrength(x + 0.5, z + 0.5, h);
        if (caves > 0f && depth > -1f) d = MathF.Min(d, CaveDensity(x + 0.5, y + 0.5, z + 0.5, depth, caves));
        return Math.Clamp(d, -Limit, Limit);
    }

    // ---- Helpers --------------------------------------------------------------------------

    static int Mod(int v, int m) => (v % m + m) % m;

    static double Wrap(double d, double span)
    {
        d %= span;
        if (d > span * 0.5) d -= span;
        else if (d < -span * 0.5) d += span;
        return d;
    }

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
