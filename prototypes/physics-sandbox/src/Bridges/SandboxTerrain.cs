using System;
using System.Collections.Generic;
using System.Numerics;
using TerrainDigging.Terrain;

namespace PhysicsSandbox.Bridges;

/// <summary>
/// The sandbox's landscape, behind terrain-digging's public <see cref="ITerrainSource"/>: the
/// same interface the planet generator will implement, so the two plug together later. Built
/// for physics, with real relief: rolling hills, a long rocky ridge to the north, a steep bank
/// dropping into a basin to the east with a deep lake in it, a level landing field at the centre
/// and a gentle lane to the south for the hover rover. Rim mountains hide the edge of the 256 m world.
///
/// Like terrain-digging's generator it uses only integer hashing and exact float operations, so
/// every machine generates the same cells and edits always land in the same places.
/// </summary>
public sealed class SandboxTerrain : ITerrainSource
{
    public const float BedrockDepth = 30f;

    /// <summary>The landing field: level ground the ships start on.</summary>
    public static readonly Vector2 Pad = new(0f, 0f);
    public const float PadHeight = 22f, PadRadius = 18f;

    /// <summary>The rover lane: gentle ground along z = <see cref="LaneZ"/>, where a trench gets dug.</summary>
    public const float LaneZ = 42f;

    /// <summary>The steep bank runs north to south about this far east.</summary>
    public const float BankX = 52f;

    /// <summary>
    /// The lake in the basin below the bank: an oval <see cref="LakeRadiusX"/> by
    /// <see cref="LakeRadiusZ"/> m round its centre, its surface at <see cref="LakeLevel"/>, a level
    /// bed <see cref="LakeDepth"/> m down in the middle rising smoothly to its shore, and a gentle
    /// beach beyond. Deep enough to dive 10 m and to sink a vessel in.
    /// </summary>
    public static readonly Vector2 LakeCenter = new(73f, 0f);
    public const float LakeRadiusX = 16f, LakeRadiusZ = 18f, LakeLevel = 11.5f, LakeDepth = 12f;

    public int Seed { get; }
    public string GeneratorId => "physics-sandbox/2";

    /// <summary>The lake's level. Only columns inside its shore hold water (<see cref="WaterSurfaceAt"/>).</summary>
    public float WaterLevel => LakeLevel;
    public IReadOnlyList<PoiSite> PointsOfInterest { get; } = Array.Empty<PoiSite>();

    readonly uint _hills, _ridge, _soil, _rim, _bank;

    public SandboxTerrain(int seed)
    {
        Seed = seed;
        var rng = new SplitMix64((ulong)(uint)seed * 0x9E3779B97F4A7C15ul + 41);
        _hills = (uint)rng.Next();
        _ridge = (uint)rng.Next();
        _soil = (uint)rng.Next();
        _rim = (uint)rng.Next();
        _bank = (uint)rng.Next();
    }

    public float SurfaceHeight(float x, float z)
    {
        float h = 20f + Noise.Fbm2(x * 0.011f, z * 0.011f, 4, _hills) * 11f;

        // A rocky ridge across the north: a narrow crest along a slanting line, jagged on top.
        float along = (x + 90f) / 180f;
        float lineZ = -58f + 14f * along + 6f * Noise.Value2(x * 0.02f, 3.7f, _ridge);
        float across = (z - lineZ) / 11f;
        float crest = MathF.Exp(-across * across) * (19f + 7f * Noise.Fbm2(x * 0.05f, z * 0.05f, 2, _ridge + 1u));
        h += crest * Noise.SmoothStep(-100f, -70f, x) * (1f - Noise.SmoothStep(70f, 100f, x));

        // The bank: the ground east of it drops 13 m over about 5 m, into a basin.
        float bankAt = BankX + 6f * Noise.Value2(1.3f, z * 0.03f, _bank);
        h -= 13f * Noise.SmoothStep(bankAt - 2.5f, bankAt + 2.5f, x) * (1f - Noise.SmoothStep(60f, 85f, MathF.Abs(z)));

        // The rover lane south of the field: hills there are eased to a gentle roll.
        float lane = 1f - Noise.SmoothStep(5f, 14f, MathF.Abs(z - LaneZ));
        lane *= 1f - Noise.SmoothStep(40f, 55f, MathF.Abs(x));
        h = h + (22f + 1.5f * Noise.Value2(x * 0.06f, z * 0.06f, _hills + 7u) - h) * lane * 0.85f;

        // The landing field: level, blended into the hills around it.
        float field = 1f - Noise.SmoothStep(PadRadius, PadRadius + 16f, Distance(x, z, Pad));
        h += (PadHeight - h) * field;

        // Rim mountains: the cut faces at the world edge face outwards, where nobody looks.
        float edge = MathF.Max(MathF.Abs(x), MathF.Abs(z));
        h += Noise.SmoothStep(92f, 124f, edge) * (24f + 10f * Noise.Value2(x * 0.045f, z * 0.045f, _rim));

        // The lake: inside its shore the ground comes down to its bed; a beach blends the basin
        // floor down to the waterline beyond it. Nothing outside the beach changes.
        float s = LakeReach(x, z);
        if (s < 1.3f)
        {
            float bed = LakeLevel - LakeDepth * (1f - Noise.SmoothStep(0.35f, 1f, s));
            h -= MathF.Max(0f, h - bed) * (1f - Noise.SmoothStep(1f, 1.3f, s));
        }
        return MathF.Min(h, 74f);
    }

    /// <summary>How far out from the lake's centre a point is, in shore radii: under 1 is inside its shore.</summary>
    public static float LakeReach(float x, float z)
    {
        float dx = (x - LakeCenter.X) / LakeRadiusX, dz = (z - LakeCenter.Y) / LakeRadiusZ;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// The water surface over a column, if it holds water: inside the lake's shore, where the
    /// natural ground lies below the lake's level. Decided by the natural ground alone (terrain-
    /// digging's rule 6), so digging never moves water.
    /// </summary>
    public bool WaterSurfaceAt(float x, float z, out float surface)
    {
        surface = LakeLevel;
        return LakeReach(x, z) < 1f && SurfaceHeight(x, z) < LakeLevel;
    }

    static float Distance(float x, float z, Vector2 p)
    {
        float dx = x - p.X, dz = z - p.Y;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    public void GenerateChunk(Int3 chunk, Span<float> density, Span<CellMaterial> material)
    {
        const int S = ChunkStore.Size, P = S + 2;
        int ox = chunk.X * S, oy = chunk.Y * S, oz = chunk.Z * S;

        Span<float> heights = stackalloc float[P * P];
        for (int pz = 0; pz < P; pz++)
            for (int px = 0; px < P; px++)
                heights[px + P * pz] = SurfaceHeight(ox + px - 0.5f, oz + pz - 0.5f);

        for (int lz = 0; lz < S; lz++)
            for (int lx = 0; lx < S; lx++)
            {
                float x = ox + lx + 0.5f, z = oz + lz + 0.5f;
                int hi = lx + 1 + P * (lz + 1);
                float h = heights[hi];

                // Distance to the surface rather than height above it, so steep banks mesh smoothly.
                float sx = (heights[hi + 1] - heights[hi - 1]) * 0.5f, sz = (heights[hi + P] - heights[hi - P]) * 0.5f;
                float slope2 = sx * sx + sz * sz;
                float slopeScale = 1f / MathF.Sqrt(1f + slope2);
                float soilDepth = (1.3f + 0.8f * Noise.Value2(x * 0.09f, z * 0.09f, _soil)) * (1f - Noise.SmoothStep(0.35f, 1.2f, slope2));
                float rockDepth = 12f + 3f * Noise.Value2(x * 0.03f, z * 0.03f, _soil + 1u);

                for (int ly = 0; ly < S; ly++)
                {
                    int i = lx + S * (lz + S * ly);
                    float y = oy + ly + 0.5f;
                    float depth = h - y;
                    if (depth > BedrockDepth)
                    {
                        density[i] = ChunkStore.DensityLimit;
                        material[i] = CellMaterial.Bedrock;
                        continue;
                    }
                    float d = Math.Clamp(depth * slopeScale, -ChunkStore.DensityLimit, ChunkStore.DensityLimit);
                    density[i] = d;
                    material[i] = d <= 0f ? CellMaterial.Air
                        : depth < soilDepth ? CellMaterial.Soil
                        : depth < rockDepth ? CellMaterial.Rock
                        : CellMaterial.DeepRock;
                }
            }
    }
}
