using System;
using System.Collections.Generic;
using System.Numerics;

namespace TerrainDigging.Terrain;

/// <summary>
/// A small seeded landscape for the prototype: rolling ground, flat-topped mesas, a lake basin,
/// rim mountains that hide the edge of the map, two winding generator caves, ore veins near the
/// surface and bedrock 30 m down.
/// </summary>
public sealed class SimpleTerrainSource : ITerrainSource
{
    public const float BedrockDepth = 30f;
    public const float OreMaxDepth = 10f;

    public static readonly Vector2 Spawn = new(0f, 6f);
    public static readonly Vector2 LakeCenter = new(30f, -44f);
    public static readonly Vector2 BeaconSite = new(-30f, -20f);

    // Ore fields: rich ground near spawn so the first pit finds something.
    static readonly Vector2[] OreFields = { new(10f, -6f), new(-46f, 34f), new(58f, 22f) };

    public int Seed { get; }
    public string GeneratorId => "simple-terrain/1";
    public float WaterLevel => 8f;
    public IReadOnlyList<PoiSite> PointsOfInterest { get; }

    readonly uint _hills, _mesa, _soil, _vein, _veinBreak, _cluster, _rim;
    readonly Cave[] _caves;

    public SimpleTerrainSource(int seed)
    {
        Seed = seed;
        var rng = new SplitMix64((ulong)(uint)seed * 0x9E3779B97F4A7C15ul + 17);
        _hills = (uint)rng.Next();
        _mesa = (uint)rng.Next();
        _soil = (uint)rng.Next();
        _vein = (uint)rng.Next();
        _veinBreak = (uint)rng.Next();
        _cluster = (uint)rng.Next();
        _rim = (uint)rng.Next();

        _caves = new[]
        {
            Cave.Wind(this, ref rng, new Vector2(-38f, 8f), heading: 2.6f, segments: 6),
            Cave.Wind(this, ref rng, new Vector2(46f, 44f), heading: 0.4f, segments: 6),
        };

        float beaconGround = MaxHeight(BeaconSite, 3f);
        int baseY = (int)MathF.Floor(beaconGround);
        PointsOfInterest = new[]
        {
            new PoiSite("Survey beacon", new CellBox(
                new Int3((int)BeaconSite.X - 3, baseY - 2, (int)BeaconSite.Y - 3),
                new Int3((int)BeaconSite.X + 3, baseY + 14, (int)BeaconSite.Y + 3))),
        };
    }

    float MaxHeight(Vector2 at, float radius)
    {
        float max = float.MinValue;
        for (float dx = -radius; dx <= radius; dx += 1f)
            for (float dz = -radius; dz <= radius; dz += 1f)
                max = MathF.Max(max, SurfaceHeight(at.X + dx, at.Y + dz));
        return max;
    }

    public float SurfaceHeight(float x, float z)
    {
        float h = 15f + Noise.Fbm2(x * 0.013f, z * 0.013f, 4, _hills) * 9f;

        // Flat-topped, two-tier mesas: the classic paperback-cover silhouette.
        float m = Noise.Fbm2(x * 0.016f + 37.1f, z * 0.016f - 12.7f, 3, _mesa);
        float clearing = Noise.SmoothStep(16f, 34f, Distance(x, z, Spawn));
        clearing *= Noise.SmoothStep(8f, 18f, Distance(x, z, BeaconSite));
        h += (Noise.SmoothStep(0.08f, 0.13f, m) * 7f + Noise.SmoothStep(0.27f, 0.31f, m) * 8f) * clearing;

        float lake = 1f - Noise.SmoothStep(12f, 36f, Distance(x, z, LakeCenter));
        h -= lake * 13f;

        // Rim mountains: the cut faces at the world edge face outwards, where nobody looks.
        float edge = MathF.Max(MathF.Abs(x), MathF.Abs(z));
        h += Noise.SmoothStep(90f, 124f, edge) * (26f + 12f * Noise.Value2(x * 0.045f, z * 0.045f, _rim));
        return h;
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

        // Heights for the chunk's columns plus a one-column ring, for slopes by central difference.
        Span<float> heights = stackalloc float[P * P];
        for (int pz = 0; pz < P; pz++)
            for (int px = 0; px < P; px++)
                heights[px + P * pz] = SurfaceHeight(ox + px - 0.5f, oz + pz - 0.5f);

        for (int lz = 0; lz < S; lz++)
        {
            for (int lx = 0; lx < S; lx++)
            {
                float x = ox + lx + 0.5f, z = oz + lz + 0.5f;
                int hi = lx + 1 + P * (lz + 1);
                float h = heights[hi];

                // Vertical distance overstates distance on slopes; dividing by the slope length
                // makes density roughly the true distance to the surface. Without it, steep
                // ground hits the density clamp mid-edge and the mesh steps like a staircase.
                float sx = (heights[hi + 1] - heights[hi - 1]) * 0.5f, sz = (heights[hi + P] - heights[hi - P]) * 0.5f;
                float slope2 = sx * sx + sz * sz;
                float slopeScale = 1f / MathF.Sqrt(1f + slope2);

                // Soil slides off steep ground, so slopes and mesa walls show bare banded rock.
                float soilDepth = (1.4f + 0.9f * Noise.Value2(x * 0.09f, z * 0.09f, _soil)) * (1f - Noise.SmoothStep(0.3f, 1.1f, slope2));
                float rockDepth = 13f + 3f * Noise.Value2(x * 0.03f, z * 0.03f, _soil + 1u);
                float richness = OreRichness(x, z);

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

                    float d = depth * slopeScale;
                    if (d > -2f) d = MathF.Min(d, CaveDensity(x, y, z));
                    d = Math.Clamp(d, -ChunkStore.DensityLimit, ChunkStore.DensityLimit);
                    density[i] = d;

                    if (d <= 0f)
                    {
                        material[i] = CellMaterial.Air;
                        continue;
                    }

                    CellMaterial mat = depth < soilDepth ? CellMaterial.Soil
                        : depth < rockDepth ? CellMaterial.Rock
                        : CellMaterial.DeepRock;
                    // Ore starts under the soil: the surface stays clean, pits and cliffs reveal it.
                    if (richness > 0f && depth > soilDepth + 0.4f && depth < OreMaxDepth && IsVein(x, y, z, richness))
                        mat = CellMaterial.Ore;
                    material[i] = mat;
                }
            }
        }
    }

    /// <summary>Where veins may form: about half the map, and always around the ore fields.</summary>
    float OreRichness(float x, float z)
    {
        float r = Noise.Fbm2(x * 0.021f, z * 0.021f, 2, _cluster) - 0.2f;
        foreach (Vector2 field in OreFields)
            r = MathF.Max(r, 0.6f * (1f - Noise.SmoothStep(6f, 18f, Distance(x, z, field))));
        return r;
    }

    /// <summary>
    /// Ore forms thin, steep sheets (the zero set of a 3D noise) broken into patches, so pit
    /// walls show it as glowing bands that are easy to follow.
    /// </summary>
    bool IsVein(float x, float y, float z, float richness)
    {
        float sheet = Noise.Gradient3(x * 0.05f, y * 0.04f, z * 0.05f, _vein);
        float width = 0.04f + 0.08f * MathF.Min(richness, 0.6f);
        if (MathF.Abs(sheet) >= width) return false;
        return Noise.Gradient3(x * 0.11f, y * 0.11f, z * 0.11f, _veinBreak) > 0f;
    }

    float CaveDensity(float x, float y, float z)
    {
        float best = 1f;
        var p = new Vector3(x, y, z);
        foreach (Cave cave in _caves)
            best = MathF.Min(best, cave.Density(p));
        return best;
    }

    /// <summary>A winding tunnel from a mouth on the surface down to about 14 m.</summary>
    sealed class Cave
    {
        readonly Vector3[] _points;
        readonly float[] _radii;
        readonly Vector3 _min, _max;

        Cave(Vector3[] points, float[] radii)
        {
            _points = points;
            _radii = radii;
            _min = new Vector3(float.MaxValue);
            _max = new Vector3(float.MinValue);
            for (int i = 0; i < points.Length; i++)
            {
                _min = Vector3.Min(_min, points[i] - new Vector3(radii[i] + 1f));
                _max = Vector3.Max(_max, points[i] + new Vector3(radii[i] + 1f));
            }
        }

        public static Cave Wind(SimpleTerrainSource source, ref SplitMix64 rng, Vector2 mouth, float heading, int segments)
        {
            var points = new Vector3[segments + 1];
            var radii = new float[segments + 1];
            Vector2 at = mouth;
            for (int i = 0; i <= segments; i++)
            {
                // Start just above ground so the tunnel breaks the surface, then sink to ~14 m.
                float depth = i == 0 ? -1.5f : MathF.Min(3f + i * 3f, 14f);
                points[i] = new Vector3(at.X, source.SurfaceHeight(at.X, at.Y) - depth, at.Y);
                radii[i] = i == 0 ? 2.6f : rng.Range(2.2f, 3.2f);
                heading += rng.Range(-0.6f, 0.6f);
                // Cosine and sine come from a polynomial so the layout stays bit-exact everywhere.
                at += new Vector2(Cos(heading), Sin(heading)) * rng.Range(7f, 10f);
            }
            return new Cave(points, radii);
        }

        static float Sin(float a)
        {
            // Wrap to [-pi, pi] and use a short Taylor series: plenty for steering a tunnel.
            const float Pi = 3.14159265f, TwoPi = 6.28318531f;
            a -= TwoPi * MathF.Floor((a + Pi) / TwoPi);
            float a2 = a * a;
            return a * (1f - a2 / 6f * (1f - a2 / 20f * (1f - a2 / 42f * (1f - a2 / 72f))));
        }

        static float Cos(float a) => Sin(a + 1.57079633f);

        public float Density(Vector3 p)
        {
            if (p.X < _min.X || p.Y < _min.Y || p.Z < _min.Z || p.X > _max.X || p.Y > _max.Y || p.Z > _max.Z) return 1f;
            float best = 1f;
            for (int i = 0; i + 1 < _points.Length; i++)
            {
                // Scalar maths on purpose: SIMD dot products may sum in a different order on
                // another CPU, and the generator must be bit-exact on every machine.
                Vector3 a = _points[i], b = _points[i + 1];
                float abx = b.X - a.X, aby = b.Y - a.Y, abz = b.Z - a.Z;
                float apx = p.X - a.X, apy = p.Y - a.Y, apz = p.Z - a.Z;
                float t = (apx * abx + apy * aby + apz * abz) / (abx * abx + aby * aby + abz * abz);
                t = Math.Clamp(t, 0f, 1f);
                float dx = apx - abx * t, dy = apy - aby * t, dz = apz - abz * t;
                float distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                float radius = _radii[i] + (_radii[i + 1] - _radii[i]) * t;
                best = MathF.Min(best, distance - radius);
            }
            return best;
        }
    }
}
