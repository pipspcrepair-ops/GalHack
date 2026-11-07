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
/// - bedrock <see cref="BedrockDepth"/> metres under the low ground nearby (not under the
///   surface right overhead, or a tunnel into a cliff's foot would meet it in a few steps):
///   nothing cuts it;
/// - caves: winding tubes 3 to 36 m down where two periodic noise fields both cross zero, kept under
///   their roof of rock everywhere; you get in by a <see cref="CaveMouth"/>, a passage chosen for its
///   spot in the land;
/// - ore: a lumpy body under every deposit on the planet generator's deposit map, surface
///   outcrops breaking the ground, shallow ones a few metres down.
///
/// The map is a torus, so x and z wrap: chunks past either edge (the "ghost" chunks used near a
/// seam) generate exactly the cells of their counterpart on the other side.
/// Thread-safe: chunks generate on mesh workers.
/// </summary>
public sealed partial class PlanetTerrainSource : ITerrainSource
{
    public const float BedrockDepth = 30f;
    /// <summary>The ground a tunnel's bore always leaves over it, metres: its mouth is a face in the slope, never a hole from above.</summary>
    public const float TunnelRoof = 1.2f;
    public const float CaveTop = 3f, CaveBottom = 36f;
    /// <summary>The least a carved cell's density is below zero: a wall cell's surface stays well back from it.</summary>
    const float CarvedLeast = 0.4f;
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
    readonly ConcurrentDictionary<long, float> _floors = new();

    // Stamps by 64 m bucket: each bucket lists, in list order, every stamp whose reach touches it,
    // so a height query composes only the few stamps near it (a planet's roads add hundreds).
    const int Bucket = 64;
    int _bucketsX, _bucketsZ;
    int[][] _buckets;

    /// <summary>Surface heights for one chunk column: its 16 x 16 cells plus a one-cell ring, at cell centres.</summary>
    public sealed class Column
    {
        public readonly float[] Heights = new float[P * P];
        public float Min = float.MaxValue, Max = float.MinValue;
        /// <summary>The low ground around each corner (x0z0, x1z0, x0z1, x1z1): bedrock lies under it.</summary>
        public float Floor00, Floor10, Floor01, Floor11;
        /// <summary>Squared slope over 8 m on a 4 m lattice (5 x 5 points, edges shared with the neighbours): for colours.</summary>
        public readonly float[] BroadSlope2 = new float[Lattice * Lattice];
    }

    const int Lattice = S / 4 + 1;

    public PlanetTerrainSource(WorldMap map, IReadOnlyList<TerrainStamp> stamps = null, IReadOnlyList<PoiSite> pois = null)
    {
        Map = map;
        Sampler = new SurfaceSampler(map);
        Stamps = stamps ?? Array.Empty<TerrainStamp>();
        PointsOfInterest = pois ?? Array.Empty<PoiSite>();
        Width = (int)Math.Round(map.WidthMeters);
        Height = (int)Math.Round(map.HeightMeters);
        if (Width % S != 0 || Height % S != 0) throw new ArgumentException($"planet size {Width} x {Height} m must be whole chunks");
        IndexStamps();
        ChunksX = Width / S;
        ChunksZ = Height / S;

        PlanetSpec spec = map.Spec;
        Seed = (int)(uint)(spec.Seed ^ (spec.Seed >> 32));
        ulong stampHash = 14695981039346656037UL;
        foreach (TerrainStamp t in Stamps)
            stampHash = (stampHash ^ (ulong)(t.MinX * 73856093 ^ t.MinZ * 19349663 ^ t.PadY * 83492791 ^ t.MaxX * 2654435761)) * 1099511628211UL;
        // Saves record this and refuse to load against anything else: same planet, same stamps, same generator version.
        // Version 2: landforms (the same seed now gives different ground). Version 3: cave mouths.
        GeneratorId = $"planet-surface/3 {spec.Archetype}/{spec.Variant} seed {spec.Seed} mods {(int)spec.Modifiers} {spec.WidthKm} km stamps {stampHash:X16}";

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

    // ---- Stamp index ----------------------------------------------------------------------

    void IndexStamps()
    {
        _bucketsX = (Width + Bucket - 1) / Bucket;
        _bucketsZ = (Height + Bucket - 1) / Bucket;
        var lists = new List<int>[_bucketsX * _bucketsZ];
        for (int i = 0; i < Stamps.Count; i++)
        {
            TerrainStamp t = Stamps[i];
            double rx = t.ReachX + 8, rz = t.ReachZ + 8;
            bool allX = rx * 2 >= Width, allZ = rz * 2 >= Height;
            var xs = new List<int>();
            var zs = new List<int>();
            if (allX) for (int b = 0; b < _bucketsX; b++) xs.Add(b);
            else
                for (double px = t.CenterX - rx; ; px = Math.Min(px + Bucket / 2.0, t.CenterX + rx))
                {
                    int b = (int)(Mathx.WrapX(px, Width) / Bucket);
                    if (!xs.Contains(b)) xs.Add(b);
                    if (px >= t.CenterX + rx) break;
                }
            if (allZ) for (int b = 0; b < _bucketsZ; b++) zs.Add(b);
            else
                for (double pz = t.CenterZ - rz; ; pz = Math.Min(pz + Bucket / 2.0, t.CenterZ + rz))
                {
                    int b = (int)(Mathx.Wrap(pz, Height) / Bucket);
                    if (!zs.Contains(b)) zs.Add(b);
                    if (pz >= t.CenterZ + rz) break;
                }
            foreach (int bz in zs)
            foreach (int bx in xs)
                (lists[bz * _bucketsX + bx] ??= new List<int>()).Add(i);
        }
        _buckets = new int[lists.Length][];
        for (int k = 0; k < lists.Length; k++) _buckets[k] = lists[k]?.ToArray() ?? Array.Empty<int>();
    }

    /// <summary>The stamps (indices into <see cref="Stamps"/>, in order) that can touch a point. x and z wrap.</summary>
    public int[] StampsAt(double x, double z)
    {
        int bx = Math.Min(_bucketsX - 1, (int)(Mathx.WrapX(x, Width) / Bucket));
        int bz = Math.Min(_bucketsZ - 1, (int)(Mathx.Wrap(z, Height) / Bucket));
        return _buckets[bz * _bucketsX + bx];
    }

    /// <summary>The natural height with the stamps near a point laid over it, by <see cref="TerrainStamp.Compose"/>'s rule.</summary>
    float Stamped(float natural, double x, double z)
    {
        int[] near = StampsAt(x, z);
        float pad = TerrainStamp.PadWeight(Stamps, near, x, z);
        float h = natural;
        bool roads = false;
        foreach (int i in near)
        {
            if (Stamps[i].Shape != StampShape.Road) h = Stamps[i].Apply(h, x, z);
            else if (!roads)
            {
                roads = true;
                if (pad < 1f) h = TerrainStamp.ApplyRoads(Stamps, near, h, x, z, pad);
            }
        }
        return h;
    }

    // ---- Heights --------------------------------------------------------------------------

    /// <summary>The planet generator's own ground height, without stamps. x and z wrap.</summary>
    public float NaturalHeight(double x, double z) => Sampler.Sample(x, z, 1).Height;

    /// <summary>Height of the undug surface at a point: the natural ground with every stamp applied.</summary>
    public float SurfaceHeight(float x, float z) => SurfaceHeight((double)x, z);

    public float SurfaceHeight(double x, double z, double spacing = 1)
    {
        return Banked(Sampler.Sample(x, z, spacing), x, z);
    }

    /// <summary>Ground and river at a point, with stamps applied to the ground height.</summary>
    public GroundSample Ground(double x, double z, double spacing)
    {
        GroundSample s = Sampler.Sample(x, z, spacing);
        return s with { Height = Banked(s, x, z) };
    }

    /// <summary>
    /// The ground with its stamps, which may not cut a river's bank: water stays in its bed, so a pad's
    /// blend or a road's cutting stops at the bank. On a place's own footprint the place rules (siting
    /// keeps those back from rivers).
    /// </summary>
    float Banked(GroundSample s, double x, double z)
    {
        float h = Stamped(s.Height, x, z);
        if (h < s.Bank && !TerrainStamp.OnPad(Stamps, StampsAt(x, z), x, z)) h = s.Bank;
        // Land under sea level that the sea doesn't cover would leave the sea's edge hanging over it:
        // past the sea's own shore the land stands clear of the water.
        if (h < WaterLevel + ShoreRise && !s.InRiver && Map.Sample(Map.Elevation, Mathx.WrapX(x, Width), Mathx.Wrap(z, Height)) >= -SeaMaskDepth) h = WaterLevel + ShoreRise;
        return h;
    }

    /// <summary>
    /// The made ground at a point: the surface of the pad or road that applies most here, how much
    /// of it shows (eased off well inside the blend, so a made apron never ends in a hard edge), and how worn it is.
    /// Without <paramref name="roads"/>, only places' pads: a renderer that draws roads from their lines leaves them out.
    /// </summary>
    public StampSurface MadeGround(double x, double z, out float weight, out float wear, bool roads = true)
    {
        weight = 0f;
        wear = 0f;
        StampSurface best = StampSurface.Natural;
        foreach (int i in StampsAt(x, z))
        {
            TerrainStamp t = Stamps[i];
            if (t.Shape is not (StampShape.Pad or StampShape.Road) || !roads && t.Shape == StampShape.Road) continue;
            float w = t.Weight(x, z);
            if (w <= weight) continue;
            weight = w;
            wear = t.Wear;
            best = t.Surface;
        }
        return best;
    }

    /// <summary>
    /// Whether a point is on a road's deck or shoulder and on no place's pad, and how worn that road
    /// is: old roads let grass and shrubs back in.
    /// </summary>
    public bool OnRoad(double x, double z, out float wear)
    {
        wear = 0f;
        bool road = false;
        foreach (int i in StampsAt(x, z))
        {
            TerrainStamp t = Stamps[i];
            if (t.Shape == StampShape.Pad && t.Weight(x, z) > 0f) return false;
            if (t.Shape == StampShape.Road && t.Weight(x, z) > 0f) { road = true; wear = Math.Max(wear, t.Wear); }
        }
        return road;
    }

    /// <summary>How much any stamp covers a point (0 none, 1 on a pad).</summary>
    public float StampWeight(double x, double z)
    {
        float w = 0f;
        foreach (int i in StampsAt(x, z)) w = Math.Max(w, Stamps[i].Weight(x, z));
        return w;
    }

    // ---- Water ------------------------------------------------------------------------------

    /// <summary>
    /// The sea shows where the world map lies more than this far below sea level: the water
    /// shader discards its plane where the map's depth (elevation / -120 m) is under 0.004.
    /// </summary>
    public const float SeaMaskDepth = 0.48f;

    /// <summary>How far dry land beside the sea stands over it, at the least.</summary>
    public const float ShoreRise = 0.1f;

    /// <summary>River water is drawn this far below a river's level (FarBuilder's river sheets).</summary>
    public const float RiverDrop = 0.15f;

    /// <summary>
    /// The water over a column, if it holds any: a river's surface in its channel, or the sea at
    /// sea level where the world map lies below it, wherever the ground there is lower; exactly where
    /// that water is drawn. Water is a volume: everything under the surface and over the ground.
    /// Decided by the generator (and stamps) alone, never by digging, as terrain-digging's static
    /// water is: a pit dug below sea level inland stays dry. x and z wrap.
    /// </summary>
    public bool WaterSurfaceAt(double x, double z, out float surface)
    {
        GroundSample g = Ground(x, z, 1);
        if (g.InRiver && g.RiverLevel - RiverDrop > g.Height)
        {
            surface = g.RiverLevel - RiverDrop;
            return true;
        }
        surface = WaterLevel;
        return g.Height < WaterLevel && Map.Sample(Map.Elevation, Mathx.WrapX(x, Width), Mathx.Wrap(z, Height)) < -SeaMaskDepth;
    }

    /// <summary>The stamp whose footprint or apron covers a point, if any.</summary>
    public TerrainStamp StampAt(double x, double z)
    {
        foreach (int i in StampsAt(x, z))
            if (Stamps[i].Weight(x, z) >= 1f) return Stamps[i];
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
        // The slope that colours the ground, measured over 8 m: the 1 m slope that decides the cells'
        // materials wobbles with every bump, and soil and rock would mottle along the voxel grid.
        // The lattice's edge points are the same points the neighbouring column samples, so colours
        // run on across chunk borders without a seam.
        const int C = Lattice + 2;
        Span<float> coarse = stackalloc float[C * C];
        for (int j = 0; j < C; j++)
        for (int i = 0; i < C; i++)
            coarse[i + C * j] = SurfaceHeight(ox + (i - 1) * 4.0, oz + (j - 1) * 4.0, 4);
        for (int j = 0; j < Lattice; j++)
        for (int i = 0; i < Lattice; i++)
        {
            float gx = (coarse[i + 2 + C * (j + 1)] - coarse[i + C * (j + 1)]) / 8f;
            float gz = (coarse[i + 1 + C * (j + 2)] - coarse[i + 1 + C * j]) / 8f;
            column.BroadSlope2[i + Lattice * j] = gx * gx + gz * gz;
        }
        column.Floor00 = FloorAt(cx, cz);
        column.Floor10 = FloorAt(cx + 1, cz);
        column.Floor01 = FloorAt(cx, cz + 1);
        column.Floor11 = FloorAt(cx + 1, cz + 1);
        return column;
    }

    // Bedrock follows the low ground rather than the surface overhead: under a mountain it lies
    // as deep as under the valley beside it, so the mountain itself can be dug through.
    const int FloorReach = 3; // samples each way, a chunk apart: the lowest ground within 48 m

    /// <summary>The lowest ground within about 48 m of a chunk corner (any copy: it wraps), cached.</summary>
    float FloorAt(int cx, int cz)
    {
        int wx = Mod(cx, ChunksX), wz = Mod(cz, ChunksZ);
        long key = (long)wx << 32 | (uint)wz;
        if (_floors.TryGetValue(key, out float floor)) return floor;
        floor = float.MaxValue;
        for (int j = -FloorReach; j <= FloorReach; j++)
        for (int i = -FloorReach; i <= FloorReach; i++)
            floor = Math.Min(floor, SurfaceHeight((double)(wx + i) * S, (double)(wz + j) * S, S));
        _floors[key] = floor;
        return floor;
    }

    /// <summary>
    /// The level bedrock lies <see cref="BedrockDepth"/> under, at a cell of a column: the corners'
    /// low ground blended across the column (so it has no steps), and never above the surface here.
    /// </summary>
    static float BaseLevel(Column column, float lx, float lz, float surface)
    {
        float tx = lx / S, tz = lz / S;
        float f = (column.Floor00 * (1 - tx) + column.Floor10 * tx) * (1 - tz) + (column.Floor01 * (1 - tx) + column.Floor11 * tx) * tz;
        return MathF.Min(f, surface);
    }

    /// <summary>Forgets cached columns the predicate picks (canonical chunk x, z). Safe at any time.</summary>
    public int ForgetColumns(Func<int, int, bool> drop)
    {
        int n = 0;
        foreach (long key in _columns.Keys)
            if (drop((int)(key >> 32), (int)(key & 0xFFFFFFFF)) && _columns.TryRemove(key, out _)) n++;
        foreach (long key in _floors.Keys)
            if (drop((int)(key >> 32), (int)(key & 0xFFFFFFFF))) _floors.TryRemove(key, out _);
        return n;
    }

    public int CachedColumns => _columns.Count;

    /// <summary>How deep the soil lies on undug ground with this squared slope: thinner on slopes, none on cliffs.</summary>
    public float SoilDepth(double x, double z, float slope2) =>
        _soilBase * (0.7f + 0.6f * _soil.Sample(x, 0, z)) * (1f - Smooth(0.35f, 1.2f, slope2));

    /// <summary>
    /// How much of the undug ground's top is soil rather than bare rock, 0 to 1. The cells switch
    /// from soil to rock where the soil gets thinner than the top cell; this is that same rule
    /// made continuous, so ground colours blend smoothly instead of stepping along the voxel grid.
    /// </summary>
    public float SoilCover(double x, double z, float slope2) => Smooth(0.25f, 0.75f, SoilDepth(x, z, slope2));

    /// <summary>The broad squared slope (over 8 m) at a point inside a chunk column, metres from its corner: for colours.</summary>
    public static float BroadSlope2(Column column, float fx, float fz)
    {
        float gx = Math.Clamp(fx / 4f, 0f, Lattice - 1.001f), gz = Math.Clamp(fz / 4f, 0f, Lattice - 1.001f);
        int i = (int)gx, j = (int)gz;
        float tx = gx - i, tz = gz - j;
        float[] s = column.BroadSlope2;
        return (s[i + Lattice * j] * (1 - tx) + s[i + 1 + Lattice * j] * tx) * (1 - tz)
             + (s[i + Lattice * (j + 1)] * (1 - tx) + s[i + 1 + Lattice * (j + 1)] * tx) * tz;
    }

    /// <summary>
    /// The undug surface height and squared slope at a point inside the given chunk column (any
    /// copy: <paramref name="fx"/> and <paramref name="fz"/> are metres from the column's corner),
    /// from its cached heights: cheap enough to call for every vertex of a chunk.
    /// </summary>
    public float ColumnHeight(Column column, float fx, float fz, out float slope2)
    {
        // Heights are at cell centres, with a one-cell ring: index 0 is half a metre before the corner.
        float gx = Math.Clamp(fx + 0.5f, 0f, P - 1.001f), gz = Math.Clamp(fz + 0.5f, 0f, P - 1.001f);
        int i = (int)gx, j = (int)gz;
        float tx = gx - i, tz = gz - j;
        float[] h = column.Heights;
        float h00 = h[i + P * j], h10 = h[i + 1 + P * j], h01 = h[i + P * (j + 1)], h11 = h[i + 1 + P * (j + 1)];
        float sx = (h10 - h00) * (1 - tz) + (h11 - h01) * tz;
        float sz = (h01 - h00) * (1 - tx) + (h11 - h10) * tx;
        slope2 = sx * sx + sz * sz;
        return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
    }

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
        if (MouthsNear(ox, oz) != null) deepest = CaveBottom + 6f;
        float bottom = column.Min - deepest;
        // A mouth's passage runs as deep as it needs to reach the caves: its floor has to be there too.
        if (MouthsNear(ox, oz) is { } mouths)
            foreach (CaveMouth m in mouths)
                foreach (var p in m.Path) bottom = MathF.Min(bottom, m.Y + p.Y - p.R - 2f);
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
        List<CaveMouth> mouths = MouthsNear(ox, oz);
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
            float soilDepth = SoilDepth(x, z, slope2);
            float bedrockTop = BaseLevel(column, lx + 0.5f, lz + 0.5f, h) - BedrockDepth;
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

                // Rooms a place digs below its pad stay open, however deep they go; a tunnel's bore
                // keeps a metre of roof under the surface, so it never opens a hole in the ground.
                bool carved = false, wall = false;
                if (padded)
                    foreach (TerrainStamp t in stamps)
                    {
                        carved |= t.IsCarved(x, y, z) && (t.Shape != StampShape.Road || depth > TunnelRoof);
                        wall |= t.IsWall(x, y, z);
                    }
                if (carved)
                {
                    // As far under zero as the ground beside it is over: the surface between them lies on
                    // the cell's face. A flat -limit drew it back into the ground near the top, in a dip.
                    density[i] = dist > 0f ? -MathF.Min(MathF.Max(dist, CarvedLeast), Limit) : MathF.Max(MathF.Min(dist, -CarvedLeast), -Limit);
                    // Beside a wall it is the wall that holds the surface: right back inside it, or a room's corners fill in with a fillet of ground.
                    foreach (TerrainStamp t in stamps)
                        if (t.WalledIn(x, y, z)) density[i] = -Limit;
                    material[i] = CellMaterial.Air;
                    continue;
                }
                if (y + 0.5f < bedrockTop)
                {
                    density[i] = Limit;
                    material[i] = CellMaterial.Bedrock;
                    continue;
                }

                float d = dist;
                if (caves > 0f && depth > -1f) d = MathF.Min(d, CaveDensity(x + 0.5, y + 0.5, z + 0.5, dist, caves));
                if (mouths != null && depth > -4f && !padded) d = MathF.Min(d, MouthsAt(mouths, x + 0.5, y + 0.5, z + 0.5));
                d = Math.Clamp(d, -Limit, Limit);
                if (wall) d = TerrainStamp.InWall(d);
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
    /// tube is the neighbourhood of that line. Squashing y makes passages run mostly level. Depth is
    /// the true distance to the surface, so a passage keeps its roof under a slope and behind a cliff too.
    /// </summary>
    float CaveDensity(double x, double y, double z, float depth, float strength)
    {
        if (depth < 0f || depth > CaveBottom + 4f) return Limit;
        float a = _caveA.Sample(x, y * 1.8, z), b = _caveB.Sample(x, y * 1.8, z);
        float r = MathF.Sqrt(a * a + b * b);
        float radius = 0.085f * strength;
        // About 22 m of ground per noise unit at this wavelength.
        float tube = (r - radius) * 22f;
        // Fade out above and below the cave band: nothing underground reaches the surface by accident.
        float keep = Smooth(CaveTop - 1f, CaveTop + 2f, depth) * Smooth(CaveBottom + 4f, CaveBottom - 4f, depth);
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

    /// <summary>Stamps that could touch a chunk column (a chunk lies inside one 64 m bucket), or null.</summary>
    List<TerrainStamp> StampsNear(int ox, int oz)
    {
        int[] near = StampsAt(ox + S * 0.5, oz + S * 0.5);
        if (near.Length == 0) return null;
        var list = new List<TerrainStamp>(near.Length);
        foreach (int i in near) list.Add(Stamps[i]);
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
        foreach (int i in StampsAt(x + 0.5, z + 0.5))
            if (Stamps[i].IsCarved(x, y, z) && (Stamps[i].Shape != StampShape.Road || depth > TunnelRoof)) return -Limit;
        if (depth > BedrockDepth)
        {
            // Bedrock lies under the low ground nearby, which is never above the surface here: only
            // a cell this deep can be in it, so shallow queries (flora, cave mouths) never build a column.
            int cx = FloorDiv(x, S), cz = FloorDiv(z, S);
            if (y + 0.5f < BaseLevel(ColumnAt(cx, cz), x - cx * S + 0.5f, z - cz * S + 0.5f, h) - BedrockDepth) return Limit;
        }
        float caves = StampWeight(x + 0.5, z + 0.5) > 0f ? 0f : CaveStrength(x + 0.5, z + 0.5, h);
        if (caves > 0f && depth > -1f) d = MathF.Min(d, CaveDensity(x + 0.5, y + 0.5, z + 0.5, depth * slopeScale, caves));
        if (depth > -4f && StampWeight(x + 0.5, z + 0.5) <= 0f && MouthsNear(FloorDiv(x, S) * S, FloorDiv(z, S) * S) is { } mouths) d = MathF.Min(d, MouthsAt(mouths, x + 0.5, y + 0.5, z + 0.5));
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
