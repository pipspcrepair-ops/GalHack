using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Planets.Gen;
using Planets.Look;

namespace Planets.Surface;

/// <summary>Mesh and scatter data for one terrain chunk. Plain arrays, built off the main thread.</summary>
sealed class ChunkData
{
    public int Cx, Cz, Lod;
    public Vector3[] Vertices, Normals;
    public Color[] Colors;
    public Vector2[] Uvs;
    public int[] Indices;
    public Vector3[] WaterVertices;
    public int[] WaterIndices;
    public readonly List<(Flora Kind, float[] Buffer, int Count)> Flora = new();
    public float[] Deposits;   // MultiMesh buffer, same layout as the flora
    public int DepositCount;
    public double BuildMs;
}

/// <summary>
/// Builds terrain chunks: a heightfield grid from the fine sampler, normals, biome colours, skirts
/// that hide cracks between levels of detail, river water, and low-poly scatter. Pure functions of
/// (chunk, level of detail), so a chunk always rebuilds identically.
/// </summary>
static class ChunkBuilder
{
    public const float Size = 160f;                 // metres; divides every planet width exactly
    const float ScatterCell = 4f;                   // one scatter candidate per 4 x 4 m

    public static float Step(int lod) => lod switch { 0 => 2f, 1 => 4f, _ => 8f };

    public static ChunkData Build(SurfacePalette pal, int cx, int cz, int lod)
    {
        var watch = Stopwatch.StartNew();
        float step = Step(lod);
        int n = (int)(Size / step) + 1;   // vertices per side
        int g = n + 2;                    // plus a one-sample border, so normals match across chunks
        double x0 = cx * Size, z0 = cz * Size;

        var height = new float[g * g];
        var level = new float[g * g];
        var river = new bool[g * g];
        for (int j = 0; j < g; j++)
        for (int i = 0; i < g; i++)
        {
            GroundSample s = pal.Sampler.Sample(x0 + (i - 1) * step, z0 + (j - 1) * step, step);
            int k = j * g + i;
            height[k] = s.Height;
            level[k] = s.RiverLevel;
            river[k] = s.InRiver;
        }

        int skirt = 4 * n;
        var data = new ChunkData
        {
            Cx = cx, Cz = cz, Lod = lod,
            Vertices = new Vector3[n * n + skirt],
            Normals = new Vector3[n * n + skirt],
            Colors = new Color[n * n + skirt],
            Uvs = new Vector2[n * n + skirt],
        };
        var up = new float[n * n];
        for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            int k = j * n + i, s = (j + 1) * g + i + 1;
            float h = height[s];
            var normal = new Vector3(height[s - 1] - height[s + 1], 2f * step, height[s - g] - height[s + g]).Normalized();
            double x = x0 + i * step, z = z0 + j * step;
            data.Vertices[k] = new Vector3(i * step, h, j * step);
            data.Normals[k] = normal;
            data.Uvs[k] = new Vector2((float)x, (float)z);
            data.Colors[k] = GroundColor(pal, x, z, h, normal.Y, river[s]);
            up[k] = normal.Y;
        }

        var indices = new List<int>((n - 1) * (n - 1) * 6 + skirt * 12);
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(b); indices.Add(d); indices.Add(c);
        }
        AddSkirts(data, n, step, indices);
        data.Indices = indices.ToArray();

        BuildRiverWater(data, n, g, step, height, level, river);
        if (lod <= 1)
        {
            Scatter(pal, data, n, g, step, height, river, up);
            PlaceDeposits(pal, data, g, step, height);
        }
        data.BuildMs = watch.Elapsed.TotalMilliseconds;
        return data;
    }

    static Color GroundColor(SurfacePalette pal, double x, double z, float h, float up, bool inRiver)
    {
        Rgb c = pal.ColorAt(x, z, out float glow);
        c = Rgb.Lerp(c, pal.Cliff, Mathx.Smooth(0.78f, 0.6f, up) * 0.85f);       // bare rock on steep faces
        if (h < 0f) c = Rgb.Lerp(c, pal.Seabed, Mathx.Smooth(0f, -5f, h));
        else c *= 0.82f + 0.18f * Mathx.Smooth(0f, 1.5f, h);                      // damp at the waterline
        if (inRiver) c = Rgb.Lerp(c, pal.RiverBed, 0.7f);
        return new Color(c.R, c.G, c.B, glow * Mathx.Smooth(0.6f, 0.9f, up));
    }

    /// <summary>A short wall hanging under each edge, so a coarser neighbour never shows a crack.</summary>
    static void AddSkirts(ChunkData data, int n, float step, List<int> indices)
    {
        float drop = step * 2f + 2f;
        int baseIndex = n * n;
        for (int edge = 0; edge < 4; edge++)
        {
            for (int t = 0; t < n; t++)
            {
                int src = edge switch { 0 => t, 1 => (n - 1) * n + t, 2 => t * n, _ => t * n + n - 1 };
                int dst = baseIndex + edge * n + t;
                data.Vertices[dst] = data.Vertices[src] + Vector3.Down * drop;
                data.Normals[dst] = data.Normals[src];
                data.Colors[dst] = data.Colors[src];
                data.Uvs[dst] = data.Uvs[src];
                if (t == 0) continue;
                int a = edge switch { 0 => t - 1, 1 => (n - 1) * n + t - 1, 2 => (t - 1) * n, _ => (t - 1) * n + n - 1 };
                int b = src, c = dst - 1, d = dst;
                // Both windings: skirts are seen from either side depending on the neighbour.
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }
    }

    /// <summary>Flat water at the river's surface over every grid square that touches the channel.</summary>
    static void BuildRiverWater(ChunkData data, int n, int g, float step, float[] height, float[] level, bool[] river)
    {
        var verts = new List<Vector3>();
        var indices = new List<int>();
        var map = new Dictionary<int, int>();
        int Vertex(int i, int j)
        {
            int s = (j + 1) * g + i + 1;
            if (map.TryGetValue(s, out int v)) return v;
            v = verts.Count;
            verts.Add(new Vector3(i * step, level[s] - 0.15f, j * step));
            map[s] = v;
            return v;
        }
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            int s = (j + 1) * g + i + 1;
            if (!river[s] && !river[s + 1] && !river[s + g] && !river[s + g + 1]) continue;
            int a = Vertex(i, j), b = Vertex(i + 1, j), c = Vertex(i, j + 1), d = Vertex(i + 1, j + 1);
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(b); indices.Add(d); indices.Add(c);
        }
        if (indices.Count == 0) return;
        data.WaterVertices = verts.ToArray();
        data.WaterIndices = indices.ToArray();
    }

    /// <summary>
    /// Scatter on a jittered 4 m grid. Every candidate draws the same random numbers at every level
    /// of detail, so trees stay put when a chunk sharpens; the coarser level just skips small kinds.
    /// </summary>
    static void Scatter(SurfacePalette pal, ChunkData data, int n, int g, float step, float[] height, bool[] river, float[] up)
    {
        int cells = (int)(Size / ScatterCell);
        var rng = new Rng(Hash.Of(pal.Map.Spec.Seed, data.Cx, data.Cz, 0x5CA7));
        var buffers = new Dictionary<Flora, List<float>>();
        double x0 = data.Cx * Size, z0 = data.Cz * Size;
        for (int gj = 0; gj < cells; gj++)
        for (int gi = 0; gi < cells; gi++)
        {
            float lx = (gi + rng.NextFloat()) * ScatterCell, lz = (gj + rng.NextFloat()) * ScatterCell;
            float pick = rng.NextFloat(), chance = rng.NextFloat(), size = rng.NextFloat(), turn = rng.NextFloat(), shade = rng.NextFloat();
            double x = x0 + lx, z = z0 + lz;

            Biome biome = pal.BiomeAt(x, z);
            FloraRule[] rules = BiomeLook.FloraOf(biome);
            if (rules.Length == 0) continue;
            float total = 0f;
            foreach (FloraRule r in rules) total += r.Density;
            if (chance >= total * ScatterCell * ScatterCell / 1000f * pal.Clump(x, z)) continue;

            FloraRule rule = rules[^1];
            float acc = pick * total;
            foreach (FloraRule r in rules)
            {
                if ((acc -= r.Density) < 0f)
                {
                    rule = r;
                    break;
                }
            }
            if (data.Lod > 0 && (!FloraMeshes.IsLarge(rule.Kind) || shade > 0.6f)) continue;

            int i = Math.Clamp((int)MathF.Round(lx / step), 0, n - 1), j = Math.Clamp((int)MathF.Round(lz / step), 0, n - 1);
            int s = (j + 1) * g + i + 1;
            if (river[s] || up[j * n + i] < FloraMeshes.MinUp(rule.Kind)) continue;
            float h = Bilinear(height, g, lx / step + 1f, lz / step + 1f);
            if (h < 0.4f || pal.Map.Model.CapDepth(x, z) > -20f) continue;

            float scale = Mathx.Lerp(0.7f, 1.35f, size) * FloraMeshes.BaseScale(rule.Kind);
            float angle = turn * Mathf.Tau;
            float cos = MathF.Cos(angle) * scale, sin = MathF.Sin(angle) * scale;
            Rgb tint = rule.Color * Mathx.Lerp(0.82f, 1.15f, shade);
            float glow = rule.Kind == Flora.Crystal && biome is Biome.CrystalPlain or Biome.CrystalSpires or Biome.GlowMoss ? 0.35f
                : rule.Kind == Flora.Mushroom && biome is Biome.GlowMoss or Biome.SporeMarsh ? 0.3f : 0f;

            if (!buffers.TryGetValue(rule.Kind, out List<float> list)) buffers[rule.Kind] = list = new List<float>();
            // MultiMesh layout: a 3x4 row-major transform, then four custom floats (tint and glow).
            list.Add(cos); list.Add(0f); list.Add(sin); list.Add(lx);
            list.Add(0f); list.Add(scale); list.Add(0f); list.Add(h - 0.1f);
            list.Add(-sin); list.Add(0f); list.Add(cos); list.Add(lz);
            list.Add(tint.R); list.Add(tint.G); list.Add(tint.B); list.Add(glow);
        }
        foreach (var (kind, list) in buffers) data.Flora.Add((kind, list.ToArray(), list.Count / 16));
    }

    /// <summary>
    /// Resource deposits from the world map that fall in this chunk. Shallow ones sit lower, so
    /// only their shards break the surface: the tell a player learns to dig under.
    /// </summary>
    static void PlaceDeposits(SurfacePalette pal, ChunkData data, int g, float step, float[] height)
    {
        double x0 = data.Cx * Size, z0 = data.Cz * Size, width = pal.Map.WidthMeters;
        var list = new List<float>();
        foreach (Deposit d in pal.Map.Deposits.InRect(x0, z0, x0 + Size, z0 + Size))
        {
            float lx = (float)(Mathx.WrapDelta(d.X - x0 - Size / 2, width) + Size / 2), lz = (float)(d.Y - z0);
            if (lx < 0f || lx >= Size || lz < 0f || lz >= Size) continue;
            float h = Bilinear(height, g, lx / step + 1f, lz / step + 1f);
            if (h < 0f) continue;
            ulong hash = Hash.Of(pal.Map.Spec.Seed, (long)(d.X * 10), (long)(d.Y * 10), 0x0DE);
            float scale = 0.7f + 0.5f * d.Amount, angle = Hash.Unit(hash, 0) * Mathf.Tau;
            float cos = MathF.Cos(angle) * scale, sin = MathF.Sin(angle) * scale;
            float sink = d.Depth > 0f ? 0.4f + d.Depth * 0.2f : 0f;
            Rgb tint = DepositLook.Color(d.Kind);
            list.Add(cos); list.Add(0f); list.Add(sin); list.Add(lx);
            list.Add(0f); list.Add(scale); list.Add(0f); list.Add(h - sink);
            list.Add(-sin); list.Add(0f); list.Add(cos); list.Add(lz);
            list.Add(tint.R); list.Add(tint.G); list.Add(tint.B); list.Add(DepositLook.Glow(d.Kind));
        }
        if (list.Count == 0) return;
        data.Deposits = list.ToArray();
        data.DepositCount = list.Count / 16;
    }

    static float Bilinear(float[] grid, int g, float gx, float gy)
    {
        int i = Math.Clamp((int)gx, 0, g - 2), j = Math.Clamp((int)gy, 0, g - 2);
        float tx = gx - i, ty = gy - j;
        float a = grid[j * g + i], b = grid[j * g + i + 1], c = grid[(j + 1) * g + i], d = grid[(j + 1) * g + i + 1];
        return Mathx.Lerp(Mathx.Lerp(a, b, tx), Mathx.Lerp(c, d, tx), ty);
    }
}
