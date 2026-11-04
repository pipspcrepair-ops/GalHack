using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;

namespace PlanetSurface.Terrain;

/// <summary>One far-ring heightfield chunk: plain arrays built off the main thread.</summary>
public sealed class FarJob
{
    public int Fx, Fz, Lod;     // canonical far-chunk coordinates
    public int Version;
    public Vector3[] Vertices, Normals;
    public Color[] Colors;
    public Vector2[] Flags;
    public int[] Indices;
    public Vector3[] WaterVertices;
    public int[] WaterIndices;
    public readonly List<(Flora Kind, float[] Buffer, int Count)> Flora = new();
    public double Milliseconds;
    public Exception Error;
}

/// <summary>
/// The ground beyond the voxel chunks: heightfield chunks sampled from the same terrain source
/// (planet generator heights plus stamps) at 2, 4 or 8 m, coloured with the same palette, with
/// skirts that hide cracks between levels of detail, river water, and flora scatter. Flora is
/// laid out here for the voxel area too, and skips spots that have been dug, covered by a pad or
/// opened by a cave mouth, so nothing ever floats.
/// </summary>
public static class FarBuilder
{
    public const int Size = 128;           // metres: eight voxel columns
    const float ScatterCell = 4f;

    public static float Step(int lod) => lod switch { 0 => 2f, 1 => 4f, _ => 8f };

    /// <param name="dug">columns (canonical x, z) whose surface has been dug: no flora there</param>
    public static void Build(FarJob job, PlanetTerrainSource source, GroundPalette palette, IReadOnlySet<long> dug)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            float step = Step(job.Lod);
            int n = (int)(Size / step) + 1, g = n + 2;
            double x0 = job.Fx * (double)Size, z0 = job.Fz * (double)Size;

            var height = new float[g * g];
            var level = new float[g * g];
            var river = new bool[g * g];
            for (int j = 0; j < g; j++)
            for (int i = 0; i < g; i++)
            {
                GroundSample s = source.Ground(x0 + (i - 1) * step, z0 + (j - 1) * step, step);
                int k = j * g + i;
                height[k] = s.Height;
                level[k] = s.RiverLevel;
                river[k] = s.InRiver;
            }

            int skirt = 4 * n;
            job.Vertices = new Vector3[n * n + skirt];
            job.Normals = new Vector3[n * n + skirt];
            job.Colors = new Color[n * n + skirt];
            job.Flags = new Vector2[n * n + skirt];
            var up = new float[n * n];
            for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i, s = (j + 1) * g + i + 1;
                float h = height[s];
                float gx = (height[s + 1] - height[s - 1]) / (2f * step), gz = (height[s + g] - height[s - g]) / (2f * step);
                var normal = new Vector3(-gx, 1f, -gz).Normalized();
                double x = x0 + i * step, z = z0 + j * step;
                job.Vertices[k] = new Vector3(i * step, h, j * step);
                job.Normals[k] = normal;
                job.Colors[k] = GroundColor(palette, x, z, h, gx * gx + gz * gz, river[s], out job.Flags[k]);
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
            AddSkirts(job, n, step, indices);
            job.Indices = indices.ToArray();

            BuildRiverWater(job, n, g, step, level, river);
            if (job.Lod <= 1) Scatter(job, source, palette, dug, n, g, step, height, river, up);
            job.Milliseconds = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception e)
        {
            job.Error = e;
        }
    }

    /// <summary>The same soil-to-rock rule as the voxel source: soil thins out and slides off as slopes steepen.</summary>
    static Color GroundColor(GroundPalette pal, double x, double z, float h, float slope2, bool inRiver, out Vector2 flags)
    {
        Rgb soil = pal.SoilAt(x, z, out float glow);
        float bare = Smooth(0.35f, 1.2f, slope2);
        Rgb c = Rgb.Lerp(soil, pal.Rock, bare);
        if (h < 0.6f) c = Rgb.Lerp(c, pal.Seabed, Math.Clamp(0.6f - h, 0f, 1f));
        if (inRiver) c = Rgb.Lerp(c, pal.RiverBed, 0.7f);
        flags = new Vector2(glow * (1f - bare), 0f);
        return new Color(c.R, c.G, c.B, bare);
    }

    /// <summary>A short wall under each edge, so a coarser neighbour never shows a crack.</summary>
    static void AddSkirts(FarJob job, int n, float step, List<int> indices)
    {
        float drop = step * 2f + 2f;
        int baseIndex = n * n;
        for (int edge = 0; edge < 4; edge++)
        {
            for (int t = 0; t < n; t++)
            {
                int src = edge switch { 0 => t, 1 => (n - 1) * n + t, 2 => t * n, _ => t * n + n - 1 };
                int dst = baseIndex + edge * n + t;
                job.Vertices[dst] = job.Vertices[src] + Vector3.Down * drop;
                job.Normals[dst] = job.Normals[src];
                job.Colors[dst] = job.Colors[src];
                job.Flags[dst] = job.Flags[src];
                if (t == 0) continue;
                int a = edge switch { 0 => t - 1, 1 => (n - 1) * n + t - 1, 2 => (t - 1) * n, _ => (t - 1) * n + n - 1 };
                int b = src, c = dst - 1, d = dst;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }
    }

    /// <summary>Flat water at the river's surface over every grid square that touches the channel.</summary>
    static void BuildRiverWater(FarJob job, int n, int g, float step, float[] level, bool[] river)
    {
        var verts = new List<Vector3>();
        var indices = new List<int>();
        var map = new Dictionary<int, int>();
        int Vertex(int i, int j)
        {
            int s = (j + 1) * g + i + 1;
            if (map.TryGetValue(s, out int v)) return v;
            v = verts.Count;
            float l = level[s];
            if (!river[s])
            {
                // Off the channel the sample has no level of its own: borrow a neighbour's.
                for (int k = 0; k < 9 && !river[s]; k++)
                {
                    int t = s + (k % 3 - 1) + (k / 3 - 1) * g;
                    if (t >= 0 && t < river.Length && river[t]) { l = level[t]; break; }
                }
            }
            verts.Add(new Vector3(i * step, l - 0.15f, j * step));
            map[s] = v;
            return v;
        }
        // Water reaches one grid square past the channel, under the banks, so the ground hides its
        // edge instead of a staircase of quads. Its level comes from the nearest channel sample.
        bool Wet(int i, int j) => i >= 0 && j >= 0 && i < n && j < n && river[(j + 1) * g + i + 1];
        for (int j = 0; j < n - 1; j++)
        for (int i = 0; i < n - 1; i++)
        {
            bool wet = false;
            for (int dj = -1; dj <= 2 && !wet; dj++)
            for (int di = -1; di <= 2 && !wet; di++)
                wet = Wet(i + di, j + dj);
            if (!wet) continue;
            int a = Vertex(i, j), b = Vertex(i + 1, j), c = Vertex(i, j + 1), d = Vertex(i + 1, j + 1);
            indices.Add(a); indices.Add(b); indices.Add(c);
            indices.Add(b); indices.Add(d); indices.Add(c);
        }
        if (indices.Count == 0) return;
        job.WaterVertices = verts.ToArray();
        job.WaterIndices = indices.ToArray();
    }

    /// <summary>
    /// Scatter on a jittered 4 m grid. Every candidate draws the same random numbers at every
    /// level of detail, so trees stay put when a chunk sharpens; the coarser level skips small kinds.
    /// </summary>
    static void Scatter(FarJob job, PlanetTerrainSource source, GroundPalette pal, IReadOnlySet<long> dug,
        int n, int g, float step, float[] height, bool[] river, float[] up)
    {
        int cells = (int)(Size / ScatterCell);
        var rng = new Rng(Hash.Of(pal.Map.Spec.Seed, job.Fx, job.Fz, 0x5CA7));
        var clump = new Fractal(Hash.Of(pal.Map.Spec.Seed, 0x636C756D), 70, 3);
        var buffers = new Dictionary<Flora, List<float>>();
        double x0 = job.Fx * (double)Size, z0 = job.Fz * (double)Size;
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
            float groves = Math.Clamp(1f + 1.4f * clump.Fbm(pal.Map.Model.Torus.At(x, z), 4), 0.2f, 1.8f);
            if (chance >= total * ScatterCell * ScatterCell / 1000f * groves) continue;

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
            if (job.Lod > 0 && (!FloraMeshes.IsLarge(rule.Kind) || shade > 0.6f)) continue;

            int i = Math.Clamp((int)MathF.Round(lx / step), 0, n - 1), j = Math.Clamp((int)MathF.Round(lz / step), 0, n - 1);
            int s = (j + 1) * g + i + 1;
            if (river[s] || up[j * n + i] < FloraMeshes.MinUp(rule.Kind) || height[s] < 0.6f) continue;
            if (source.StampWeight(x, z) > 0f) continue; // pads and their aprons stay clear

            // Exact ground under the trunk (the voxel surface), not the coarse grid's.
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (dug != null && (dug.Contains(Key(cx, cz)) || dug.Contains(Key(cx + 1, cz)) || dug.Contains(Key(cx, cz + 1)) || dug.Contains(Key(cx - 1, cz)) || dug.Contains(Key(cx, cz - 1)))) continue;
            float h = source.SurfaceHeight(x, z);
            if (source.CellDensity(cx, (int)Math.Floor(h - 0.6f), cz) <= 0f) continue; // a cave mouth or overhang: nothing to root in

            float scale = Mathx.Lerp(0.7f, 1.35f, size) * FloraMeshes.BaseScale(rule.Kind);
            float angle = turn * Mathf.Tau;
            float cos = MathF.Cos(angle) * scale, sin = MathF.Sin(angle) * scale;
            Rgb tint = rule.Color.Saturate(0.78f) * Mathx.Lerp(0.72f, 1.0f, shade);
            float glow = rule.Kind == Flora.Crystal && biome is Biome.CrystalPlain or Biome.CrystalSpires or Biome.GlowMoss ? 0.35f
                : rule.Kind == Flora.Mushroom && biome is Biome.GlowMoss or Biome.SporeMarsh ? 0.3f : 0f;

            if (!buffers.TryGetValue(rule.Kind, out List<float> list)) buffers[rule.Kind] = list = new List<float>();
            // MultiMesh layout: a 3x4 row-major transform, then four custom floats (tint and glow).
            list.Add(cos); list.Add(0f); list.Add(sin); list.Add(lx);
            list.Add(0f); list.Add(scale); list.Add(0f); list.Add(h - 0.25f);
            list.Add(-sin); list.Add(0f); list.Add(cos); list.Add(lz);
            list.Add(tint.R); list.Add(tint.G); list.Add(tint.B); list.Add(glow);
        }
        foreach (var (kind, list) in buffers) job.Flora.Add((kind, list.ToArray(), list.Count / 16));
    }

    /// <summary>A canonical column (x, z in metres) as a set key.</summary>
    public static long Key(int x, int z) => (long)x << 32 | (uint)z;

    static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
