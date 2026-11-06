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

    /// <summary>The chunk's extent each way, m: <see cref="FarBuilder.Size"/>, or less for the last one before a seam (<see cref="FarBuilder.Span"/>).</summary>
    public int SpanX = FarBuilder.Size, SpanZ = FarBuilder.Size;
    public Vector3[] Vertices, Normals;
    public Color[] Colors;
    public Vector2[] Flags;
    public int[] Indices;
    public Vector3[] WaterVertices;
    public Color[] WaterColors;     // alpha 1 on the channel, 0 on the ring past it: the shader frays the edge there
    public int[] WaterIndices;
    public readonly List<(Flora Kind, float[] Buffer, int Count)> Flora = new();

    /// <summary>The solid flora (trunks, boulders) of a level-0 chunk, for the colliders near the player. Null at coarser levels.</summary>
    public List<FloraSolid> Solids;
    public double Milliseconds;
    public Exception Error;
}

/// <summary>
/// The ground beyond the voxel chunks: heightfield chunks sampled from the same terrain source
/// (planet generator heights plus stamps) at 2, 4 or 8 m, coloured with the same palette, with
/// skirts that hide cracks between levels of detail, river water, and flora scatter. Flora is
/// laid out here for the voxel area too, and skips spots that have been dug, covered by a pad or
/// opened by a cave mouth, so nothing ever floats. At the finest level it also lists the solid
/// flora (trunks and boulders, <see cref="FloraSolidRules"/>) for the colliders near the player,
/// and leaves out trees that have been felled.
/// </summary>
public static class FarBuilder
{
    public const int Size = 128;           // metres: eight voxel columns
    public const float ScatterCell = 4f;
    public const int ScatterCells = (int)(Size / ScatterCell);

    /// <summary>Which flora is solid (data/flora-solids.json). Null: nothing is.</summary>
    public static FloraSolidRules Solids;

    public static float Step(int lod) => lod switch { 0 => 2f, 1 => 4f, _ => 8f };

    /// <summary>
    /// How many far chunks cross a planet this many metres across. A planet whose size isn't a
    /// multiple of <see cref="Size"/> (8 km is 62.5 chunks) has a shorter last chunk before the seam,
    /// so the far grid tiles it exactly, flora and all.
    /// </summary>
    public static int Count(int planetMetres) => (planetMetres + Size - 1) / Size;

    /// <summary>Far chunk <paramref name="f"/>'s extent, m: <see cref="Size"/>, or what is left of the planet before the seam.</summary>
    public static int Span(int f, int planetMetres) => Math.Min(Size, planetMetres - f * Size);

    /// <summary>
    /// A scatter instance's id: its 4 m scatter cell, canonical. The same instance draws the same
    /// random numbers at every level of detail, so the id holds across streaming and reloads.
    /// </summary>
    public static long ScatterId(int fx, int fz, int gi, int gj) => Key(fx * ScatterCells + gi, fz * ScatterCells + gj);

    /// <summary>The far chunk a scatter id belongs to.</summary>
    public static (int fx, int fz) ChunkOfScatter(long id) => ((int)(id >> 32) / ScatterCells, (int)(uint)id / ScatterCells);

    /// <param name="dug">columns (canonical x, z) whose surface has been dug: no flora there</param>
    /// <param name="felled">scatter ids of trees that have been felled: gone for good</param>
    public static void Build(FarJob job, PlanetTerrainSource source, GroundPalette palette, IReadOnlySet<long> dug, IReadOnlySet<long> felled = null)
    {
        try
        {
            var watch = Stopwatch.StartNew();
            float step = Step(job.Lod);
            // Grid points each way (nx along x, nz along z), and the sampled grid with a ring round it.
            var grid = new Grid((int)(job.SpanX / step) + 1, (int)(job.SpanZ / step) + 1);
            int g = grid.G;
            double x0 = job.Fx * (double)Size, z0 = job.Fz * (double)Size;

            var height = new float[g * (grid.Nz + 2)];
            var level = new float[height.Length];
            var river = new bool[height.Length];
            for (int j = 0; j < grid.Nz + 2; j++)
            for (int i = 0; i < g; i++)
            {
                GroundSample s = source.Ground(x0 + (i - 1) * step, z0 + (j - 1) * step, step);
                int k = j * g + i;
                height[k] = s.Height;
                level[k] = s.RiverLevel;
                river[k] = s.InRiver;
            }

            int points = grid.Nx * grid.Nz, skirt = 2 * (grid.Nx + grid.Nz);
            job.Vertices = new Vector3[points + skirt];
            job.Normals = new Vector3[points + skirt];
            job.Colors = new Color[points + skirt];
            job.Flags = new Vector2[points + skirt];
            var up = new float[points];
            for (int j = 0; j < grid.Nz; j++)
            for (int i = 0; i < grid.Nx; i++)
            {
                int k = j * grid.Nx + i, s = grid.Sample(i, j);
                float h = height[s];
                float gx = (height[s + 1] - height[s - 1]) / (2f * step), gz = (height[s + g] - height[s - g]) / (2f * step);
                var normal = new Vector3(-gx, 1f, -gz).Normalized();
                double x = x0 + i * step, z = z0 + j * step;
                job.Vertices[k] = new Vector3(i * step, h, j * step);
                job.Normals[k] = normal;
                job.Colors[k] = GroundColor(source, palette, x, z, h, gx * gx + gz * gz, river[s], out job.Flags[k]);
                up[k] = normal.Y;
            }

            var indices = new List<int>((grid.Nx - 1) * (grid.Nz - 1) * 6 + skirt * 12);
            for (int j = 0; j < grid.Nz - 1; j++)
            for (int i = 0; i < grid.Nx - 1; i++)
            {
                int a = j * grid.Nx + i, b = a + 1, c = a + grid.Nx, d = c + 1;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
            }
            AddSkirts(job, grid, step, indices);
            job.Indices = indices.ToArray();

            BuildRiverWater(job, grid, step, level, river);
            if (job.Lod <= 1) Scatter(job, source, palette, dug, felled, grid, step, height, river, up);
            job.Milliseconds = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception e)
        {
            job.Error = e;
        }
    }

    /// <summary>The same soil-to-rock rule as the voxel source: soil thins out and slides off as slopes steepen.</summary>
    static Color GroundColor(PlanetTerrainSource source, GroundPalette pal, double x, double z, float h, float slope2, bool inRiver, out Vector2 flags)
    {
        Color c = pal.Natural(x, z, h, source.SoilCover(x, z, slope2), out flags);
        StampSurface made = source.MadeGround(x, z, out float madeWeight, out float wear);
        if (madeWeight > 0f) c = pal.Made(c, made, madeWeight, wear, x, z);
        if (inRiver)
        {
            Rgb bed = pal.RiverBed;
            c = new Color(c.R + (bed.R - c.R) * 0.7f, c.G + (bed.G - c.G) * 0.7f, c.B + (bed.B - c.B) * 0.7f, c.A);
        }
        return c;
    }

    /// <summary>A chunk's grid points each way, and its sampled grid: one more sample beyond each edge, for normals.</summary>
    readonly record struct Grid(int Nx, int Nz)
    {
        /// <summary>The sampled grid's row length.</summary>
        public int G => Nx + 2;

        /// <summary>Grid point (i, j)'s index in the sampled grid.</summary>
        public int Sample(int i, int j) => (j + 1) * G + i + 1;
    }

    /// <summary>A short wall under each edge, so a coarser neighbour never shows a crack.</summary>
    static void AddSkirts(FarJob job, Grid grid, float step, List<int> indices)
    {
        int nx = grid.Nx, nz = grid.Nz;
        int Edge(int edge, int t) => edge switch { 0 => t, 1 => (nz - 1) * nx + t, 2 => t * nx, _ => t * nx + nx - 1 };
        float drop = step * 2f + 2f;
        int dst = nx * nz;
        for (int edge = 0; edge < 4; edge++)
        {
            int count = edge < 2 ? nx : nz;
            for (int t = 0; t < count; t++, dst++)
            {
                int src = Edge(edge, t);
                job.Vertices[dst] = job.Vertices[src] + Vector3.Down * drop;
                job.Normals[dst] = job.Normals[src];
                job.Colors[dst] = job.Colors[src];
                job.Flags[dst] = job.Flags[src];
                if (t == 0) continue;
                int a = Edge(edge, t - 1), b = src, c = dst - 1, d = dst;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }
    }

    /// <summary>Flat water at the river's surface over every grid square that touches the channel.</summary>
    static void BuildRiverWater(FarJob job, Grid grid, float step, float[] level, bool[] river)
    {
        int g = grid.G;
        var verts = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        var map = new Dictionary<int, int>();
        int Vertex(int i, int j)
        {
            int s = grid.Sample(i, j);
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
            colors.Add(new Color(1, 1, 1, river[s] ? 1f : 0f));
            map[s] = v;
            return v;
        }
        // Water reaches one grid square past the channel, under the banks, so the ground hides its
        // edge instead of a staircase of quads. Its level comes from the nearest channel sample.
        // Where the banks are too low to hide it, the shader frays the edge across that square.
        bool Wet(int i, int j) => i >= 0 && j >= 0 && i < grid.Nx && j < grid.Nz && river[grid.Sample(i, j)];
        for (int j = 0; j < grid.Nz - 1; j++)
        for (int i = 0; i < grid.Nx - 1; i++)
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
        job.WaterColors = colors.ToArray();
        job.WaterIndices = indices.ToArray();
    }

    /// <summary>
    /// Scatter on a jittered 4 m grid. Every candidate draws the same random numbers at every
    /// level of detail, so trees stay put when a chunk sharpens; the coarser level skips small kinds.
    /// </summary>
    static void Scatter(FarJob job, PlanetTerrainSource source, GroundPalette pal, IReadOnlySet<long> dug, IReadOnlySet<long> felled,
        Grid grid, float step, float[] height, bool[] river, float[] up)
    {
        // A whole chunk is 32 cells each way; one cut short at a seam has fewer. Cells keep their
        // ids (and their random numbers, drawn in the same order) whatever the chunk's size.
        int cellsX = (int)(job.SpanX / ScatterCell), cellsZ = (int)(job.SpanZ / ScatterCell);
        FloraSolidRules solids = job.Lod == 0 ? Solids : null;
        if (solids != null) job.Solids = new List<FloraSolid>();
        var rng = new Rng(Hash.Of(pal.Map.Spec.Seed, job.Fx, job.Fz, 0x5CA7));
        var clump = new Fractal(Hash.Of(pal.Map.Spec.Seed, 0x636C756D), 70, 3);
        var buffers = new Dictionary<Flora, List<float>>();
        double x0 = job.Fx * (double)Size, z0 = job.Fz * (double)Size;
        for (int gj = 0; gj < cellsZ; gj++)
        for (int gi = 0; gi < cellsX; gi++)
        {
            float lx = (gi + rng.NextFloat()) * ScatterCell, lz = (gj + rng.NextFloat()) * ScatterCell;
            float pick = rng.NextFloat(), chance = rng.NextFloat(), size = rng.NextFloat(), turn = rng.NextFloat(), shade = rng.NextFloat();
            double x = x0 + lx, z = z0 + lz;
            long id = ScatterId(job.Fx, job.Fz, gi, gj);
            if (felled != null && felled.Contains(id)) continue; // felled: gone, at every level of detail

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

            int i = Math.Clamp((int)MathF.Round(lx / step), 0, grid.Nx - 1), j = Math.Clamp((int)MathF.Round(lz / step), 0, grid.Nz - 1);
            int s = grid.Sample(i, j);
            if (river[s] || up[j * grid.Nx + i] < FloraMeshes.MinUp(rule.Kind) || height[s] < 0.6f) continue;
            // Pads and their aprons stay clear, and roads, except that grass and shrubs push back
            // through a road nobody has kept up for a century (never a tree: the way stays open).
            if (source.StampWeight(x, z) > 0f
                && !(!FloraMeshes.IsLarge(rule.Kind) && shade < 0.4f && source.OnRoad(x, z, out float wear) && wear > 0.6f)) continue;

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

            // Trunks and boulders are solid: their colliders, in planet coordinates.
            if (solids != null && solids.TryMake(rule.Kind, id, new Vector3((float)x, h - FloraSolidRules.Sink, (float)z), scale, angle, out FloraSolid solid))
                job.Solids.Add(solid);
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
