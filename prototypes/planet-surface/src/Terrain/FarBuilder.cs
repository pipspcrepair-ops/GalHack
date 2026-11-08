using System;
using System.Collections.Generic;
using System.Diagnostics;
using GalHack.Plants;
using GalHack.Terrain;
using Godot;
using Planets.Gen;
using Planets.Look;
using Planets.Surface;

namespace PlanetSurface.Terrain;

/// <summary>A plant set down on purpose rather than rolled (a tree through a ruin): canonical metres, its foot's height, its size, its turn and its colour.</summary>
public readonly record struct PlantedFlora(Flora Kind, double X, double Z, float Foot, float Scale, float Angle, Rgb Tint, Plant Species);

/// <summary>
/// One mesh's instances in a far chunk. Plant is the species' index in the world's herbarium (-1:
/// a rock, drawn with its kind's fixed mesh) and Variant which of its near meshes (-1: its far mesh).
/// Far: the far drawing of large plants, which a level-0 chunk carries beside the near one so the two
/// dissolve into each other by distance (flora.gdshader); a coarser chunk carries only it.
/// </summary>
public readonly record struct FloraBatch(Flora Kind, int Plant, int Variant, float[] Buffer, int Count, bool Far = false)
{
    public void Deconstruct(out Flora kind, out float[] buffer, out int count)
    {
        kind = Kind;
        buffer = Buffer;
        count = Count;
    }
}

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
    public readonly List<FloraBatch> Flora = new();

    /// <summary>The solid flora (trunks, boulders) of a level-0 chunk, for the colliders near the player. Null at coarser levels.</summary>
    public List<FloraSolid> Solids;
    public double Milliseconds;
    public Exception Error;
}

/// <summary>
/// The ground beyond the voxel chunks: heightfield chunks sampled from the same terrain source
/// (planet generator heights plus stamps) at 2, 4 or 8 m, coloured with the same palette, with
/// curtains that close the join between levels of detail, river water, and flora scatter. Flora is
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

    /// <summary>The scatter id of the cell a canonical point lies in.</summary>
    public static long ScatterIdAt(double x, double z)
    {
        int fx = (int)Math.Floor(x / Size), fz = (int)Math.Floor(z / Size);
        return ScatterId(fx, fz, (int)((x - fx * (double)Size) / ScatterCell), (int)((z - fz * (double)Size) / ScatterCell));
    }

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

            // A curtain has two rows of vertices along each edge: it is painted apart from the ground.
            int points = grid.Nx * grid.Nz, skirt = 2 * (grid.Nx + grid.Nz);
            job.Vertices = new Vector3[points + skirt * 2];
            job.Normals = new Vector3[points + skirt * 2];
            job.Colors = new Color[points + skirt * 2];
            job.Flags = new Vector2[points + skirt * 2];
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
                job.Colors[k] = GroundColor(source, palette, x, z, h, CliffSlope2(height, s, g, step, gx * gx + gz * gz), river[s], out job.Flags[k], step);
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
            AddCurtains(job, grid, step, indices, source, palette, x0, z0);
            job.Indices = indices.ToArray();

            BuildRiverWater(job, grid, step, level, river, height);
            if (job.Lod <= 1) Scatter(job, source, palette, dug, felled, grid, step, height, river, up);
            job.Milliseconds = watch.Elapsed.TotalMilliseconds;
        }
        catch (Exception e)
        {
            job.Error = e;
        }
    }

    /// <summary>
    /// The squared slope that picks a grid point's colour. A point on a spur or in a gully on a jagged
    /// cliff has neighbours far below (or above) it on both sides, so the difference across it says it
    /// is flat, and a point by a cliff only across a diagonal does the same: it was painted as soil, and
    /// every long triangle down the cliff from it showed as a pale streak, which at a distance looks
    /// like a tear in the rock with the sky behind it. So the steepest of the six triangle edges meeting
    /// at a point counts too, where it is a cliff and the plain gradient misses it. Ordinary slopes and
    /// crests, whose gradient tells the truth, keep their colours.
    /// </summary>
    static float CliffSlope2(float[] height, int s, int g, float step, float plain2)
    {
        float h = height[s], steepest = 0f;
        void Edge(int n, float length) => steepest = MathF.Max(steepest, MathF.Abs(height[n] - h) / (length * step));
        Edge(s + 1, 1f);
        Edge(s - 1, 1f);
        Edge(s + g, 1f);
        Edge(s - g, 1f);
        Edge(s + 1 - g, Mathf.Sqrt2);
        Edge(s - 1 + g, Mathf.Sqrt2);
        float cliff = Smooth(CliffFrom, CliffFrom * 2f, steepest) * Smooth(0.8f, 0.5f, MathF.Sqrt(plain2) / MathF.Max(steepest, 1e-4f));
        return plain2 + (MathF.Max(plain2, steepest * steepest) - plain2) * cliff;
    }

    /// <summary>The rise per metre over a triangle edge from which a point can count as on a cliff.</summary>
    const float CliffFrom = 0.75f;

    /// <summary>The same soil-to-rock rule as the voxel source: soil thins out and slides off as slopes steepen.</summary>
    static Color GroundColor(PlanetTerrainSource source, GroundPalette pal, double x, double z, float h, float slope2, bool inRiver, out Vector2 flags, float spacing = 1f)
    {
        Color c = pal.Natural(x, z, h, source.SoilCover(x, z, slope2), out flags);
        StampSurface made = source.MadeGround(x, z, out float madeWeight, out float wear, roads: false);
        if (madeWeight > 0f) c = pal.Made(c, made, madeWeight, wear, x, z, spacing);
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

    /// <summary>
    /// A curtain under each edge, so no level of detail shows a crack at its neighbour. Each level is
    /// its own surface (a coarser one has fewer points and fewer octaves of noise), so where a cliff
    /// crosses an edge one side can stand tens of metres above the other. Looking outward past the
    /// lower side's edge, the eye would go in under the higher side's ground, whose back faces aren't
    /// drawn, and out to whatever lies beyond. A level's neighbours change as the player moves, and
    /// stitching the two meshes would mean rebuilding them each time and bending the finer one to the
    /// coarser, so instead each chunk hangs a curtain from its edge to below where any level's edge runs
    /// there: short on ordinary ground, and on a cliff the height of the difference and painted as rock.
    /// The heights of the levels along the edge are sampled to know: no fixed depth is deep enough.
    /// </summary>
    static void AddCurtains(FarJob job, Grid grid, float step, List<int> indices, PlanetTerrainSource source, GroundPalette palette, double x0, double z0)
    {
        int nx = grid.Nx, nz = grid.Nz;
        int Edge(int edge, int t) => edge switch { 0 => t, 1 => (nz - 1) * nx + t, 2 => t * nx, _ => t * nx + nx - 1 };
        float least = step * 2f + 2f;
        float endX = (nx - 1) * step, endZ = (nz - 1) * step;
        int dst = nx * nz;
        for (int edge = 0; edge < 4; edge++)
        {
            int count = edge < 2 ? nx : nz;
            double ex = x0 + (edge == 3 ? endX : 0f), ez = z0 + (edge == 1 ? endZ : 0f);
            bool alongX = edge < 2;
            float[] extra = EdgeDepths(job, step, count, t => job.Vertices[Edge(edge, t)].Y, d => alongX ? (ex + d, ez) : (ex, ez + d), source);
            var outward = edge switch { 0 => Vector3.Forward, 1 => Vector3.Back, 2 => Vector3.Left, _ => Vector3.Right };
            for (int t = 0; t < count; t++, dst += 2)
            {
                int src = Edge(edge, t);
                Vector3 at = job.Vertices[src];
                float depth = extra[t];
                Color color = job.Colors[src];
                Vector3 normal = job.Normals[src];
                Vector2 flags = job.Flags[src];
                // A long curtain is a piece of cliff: bare rock facing out. A short one stays the ground it hangs from.
                float wall = Smooth(0f, 4f, depth);
                if (wall > 0f)
                {
                    double x = x0 + at.X, z = z0 + at.Z;
                    Color rock = palette.Natural(x, z, at.Y, 0f, out Vector2 rockFlags);
                    color = color.Lerp(rock, wall);
                    flags = flags.Lerp(rockFlags, wall);
                    normal = normal.Lerp(outward, wall).Normalized();
                }
                job.Vertices[dst] = at;
                job.Vertices[dst + 1] = at + Vector3.Down * (least + depth);
                job.Normals[dst] = job.Normals[dst + 1] = normal;
                job.Colors[dst] = job.Colors[dst + 1] = color;
                job.Flags[dst] = job.Flags[dst + 1] = flags;
                if (t == 0) continue;
                int a = dst - 2, b = dst, c = dst - 1, d = dst + 1;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
                indices.Add(a); indices.Add(c); indices.Add(b);
                indices.Add(b); indices.Add(c); indices.Add(d);
            }
        }
    }

    /// <summary>
    /// How far beyond its usual depth a curtain must reach at each point of an edge: enough that all
    /// along the edge it is below where every level draws that edge, its own and the others.
    /// </summary>
    static float[] EdgeDepths(FarJob job, float step, int count, Func<int, float> own, Func<float, (double x, double z)> place, PlanetTerrainSource source)
    {
        float span = (count - 1) * step, fine = Step(0);
        int points = (int)(span / fine) + 1;
        var mine = new float[points];
        var low = new float[points];
        for (int p = 0; p < points; p++)
        {
            float at = p * fine / step;
            int i = Math.Min((int)at, count - 2);
            mine[p] = low[p] = Mathx.Lerp(own(i), own(i + 1), at - i);
        }
        for (int level = 0; level <= 2; level++)
        {
            float levelStep = Step(level);
            if (levelStep == step) continue;
            int samples = (int)(span / levelStep) + 1;
            var line = new float[samples];
            for (int i = 0; i < samples; i++)
            {
                var (x, z) = place(i * levelStep);
                line[i] = source.Ground(x, z, levelStep).Height;
            }
            for (int p = 0; p < points; p++)
            {
                float at = p * fine / levelStep;
                int i = Math.Min((int)at, samples - 2);
                low[p] = MathF.Min(low[p], Mathx.Lerp(line[i], line[i + 1], at - i));
            }
        }
        // A curtain's foot is straight between its points, so both ends of a stretch cover the whole of it.
        var depths = new float[count];
        for (int p = 0; p < points; p++)
        {
            float need = mine[p] - low[p];
            if (need <= 0f) continue;
            float at = p * fine / step;
            int i = Math.Min((int)at, count - 2);
            depths[i] = MathF.Max(depths[i], need);
            depths[i + 1] = MathF.Max(depths[i + 1], need);
        }
        return depths;
    }

    /// <summary>Flat water at the river's surface over every grid square that touches the channel.</summary>
    static void BuildRiverWater(FarJob job, Grid grid, float step, float[] level, bool[] river, float[] height)
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
            float y = l - PlanetTerrainSource.RiverDrop;
            // The sheet's rim goes under this same ground, stamps and all, so it can't stand proud of it.
            if (!river[s]) y = Math.Min(y, height[s] - 0.3f);
            verts.Add(new Vector3(i * step, y, j * step));
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
        var buffers = new Dictionary<(Flora Kind, int Plant, int Variant, bool Far), List<float>>();
        PlanetFlora book = pal.Flora;
        double x0 = job.Fx * (double)Size, z0 = job.Fz * (double)Size;
        for (int gj = 0; gj < cellsZ; gj++)
        for (int gi = 0; gi < cellsX; gi++)
        {
            float lx = (gi + rng.NextFloat()) * ScatterCell, lz = (gj + rng.NextFloat()) * ScatterCell;
            float pick = rng.NextFloat(), chance = rng.NextFloat(), size = rng.NextFloat(), turn = rng.NextFloat(), shade = rng.NextFloat(), kin = rng.NextFloat();
            double x = x0 + lx, z = z0 + lz;
            long id = ScatterId(job.Fx, job.Fz, gi, gj);
            if (felled != null && felled.Contains(id)) continue; // felled: gone, at every level of detail

            // A tree through a ruin takes the cell's place in the scatter: it's the same flora from here on.
            if (pal.Planted != null && pal.Planted.TryGetValue(id, out PlantedFlora tree))
            {
                if (Dug(dug, (int)Math.Floor(tree.X), (int)Math.Floor(tree.Z))) continue; // the ground went from under it: gone, as any tree
                float tx = (float)(tree.X - x0), tz = (float)(tree.Z - z0), treeY = tree.Foot - FloraSolidRules.Sink;
                var treeBasis = new Basis(Vector3.Up, tree.Angle).Scaled(new Vector3(tree.Scale, tree.Scale, tree.Scale));
                if (job.Lod == 0) Add(tree.Kind, tree.Species, 0, tx, treeY, tz, treeBasis, tree.Tint, 0f, far: false);
                Add(tree.Kind, tree.Species, -1, tx, treeY, tz, treeBasis, tree.Tint, 0f, far: true);
                solids?.MakeAll(tree.Kind, tree.Species, 0, id, new Vector3((float)tree.X, treeY, (float)tree.Z), tree.Scale, tree.Angle, job.Solids);
                continue;
            }

            Biome biome = pal.BiomeAt(x, z);
            FloraRule[] rules = book?.RulesOf(biome) ?? BiomeLook.FloraOf(biome);
            if (rules.Length == 0) continue;
            float total = 0f;
            foreach (FloraRule r in rules) total += r.Density;
            float groves = Math.Clamp(1f + 2f * clump.Fbm(pal.Map.Model.Torus.At(x, z), 4), 0.12f, 1.8f); // thick stands and sunlit glades
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
            // Large growth has a far drawing (every level carries it); small growth only a near one (level 0).
            bool hasFar = FloraMeshes.IsLarge(rule.Kind);
            if (job.Lod > 0 && !hasFar) continue;

            // What grows here: the world's own species of this kind, for this biome and region. Rocks stay rocks.
            Plant plant = book?.Pick(rule.Kind, biome, x, z, kin);
            if (book != null && plant == null && book.Rolled(rule.Kind)) continue;

            int i = Math.Clamp((int)MathF.Round(lx / step), 0, grid.Nx - 1), j = Math.Clamp((int)MathF.Round(lz / step), 0, grid.Nz - 1);
            int s = grid.Sample(i, j);
            if (river[s] || up[j * grid.Nx + i] < FloraMeshes.MinUp(rule.Kind) || height[s] < 0.6f) continue;
            // Nothing grows back in a blast's dead zone, and little where it fades.
            if (pal.DeadZones.Count > 0 && turn < pal.Deadness(x, z)) continue;
            // Pads and their aprons stay clear, and roads, except that grass and shrubs push back
            // through a road nobody has kept up for a century (never a tree: the way stays open).
            // A park grows back: grass and shrubs always, trees once nobody has kept it.
            // Round a place the growth thins out into its made ground instead of stopping in a line: trees come in
            // across the outer blend, shrubs and rocks further, ever fewer the nearer the apron.
            float built = source.Built(x, z, out bool onRoad);
            bool large = FloraMeshes.IsLarge(rule.Kind);
            if ((onRoad || built > 0f)
                && !(onRoad && built <= 0f && !large && shade < 0.4f && source.GrowsOnRoad(x, z))
                && !(source.OnWild(x, z, out float wild) && (!large || wild > 0.7f))
                && !(!onRoad && built < 1f && Feather(turn) >= Smooth(large ? 0.1f : 0.4f, large ? 0.55f : 0.95f, built))) continue;

            // Exact ground under the trunk (the voxel surface), not the coarse grid's.
            int cx = (int)Math.Floor(x), cz = (int)Math.Floor(z);
            if (Dug(dug, cx, cz)) continue;
            float h = source.SurfaceHeight(x, z);
            if (source.CellDensity(cx, (int)Math.Floor(h - 0.6f), cz) <= 0f) continue; // a cave mouth or overhang: nothing to root in

            float scale = plant != null ? Mathx.Lerp(0.78f, 1.24f, size) : Mathx.Lerp(0.7f, 1.35f, size) * FloraMeshes.BaseScale(rule.Kind);
            float angle = turn * Mathf.Tau;
            Rgb tint = plant != null && !plant.Mineral ? book.Tint(plant, shade, x, z) : Tint(rule, shade, pal, x, z);
            // A wide crown keeps out of what's built, as its trunk does: a road's verge, a wall, a yard.
            if (plant is { Large: true } && CrownOver(source, pal, x, z, plant.CrownRadius * scale * 0.75f)) continue;
            float glow = rule.Kind == Flora.Crystal && biome is Biome.CrystalPlain or Biome.CrystalSpires or Biome.GlowMoss ? 0.12f
                : rule.Kind == Flora.Mushroom && biome is Biome.GlowMoss or Biome.SporeMarsh ? 0.3f : 0f;

            // On a slope nothing hangs over the low side: a stem is sunk by the drop across its foot, and a
            // rock lies along the ground (it would slide if it stood level), sunk by what's left of the slope.
            bool rock = rule.Kind is Flora.Rock or Flora.LavaRock;
            float foot = MathF.Max(plant?.FootRadius ?? 0f, FootRadius(rule.Kind)) * scale;
            float gx = (source.SurfaceHeight(x + foot, z) - h) / foot, gz = (source.SurfaceHeight(x, z + foot) - h) / foot;
            float slope = MathF.Sqrt(gx * gx + gz * gz), lean = rock ? MathF.Min(slope, MaxRockLean) : 0f;
            float rootY = h - FloraSolidRules.Sink - (slope - lean) * foot;
            var basis = new Basis(Vector3.Up, angle).Scaled(new Vector3(scale, scale, scale));
            if (lean > 0.01f && slope > 0f)
                basis = new Basis(new Vector3(-gz, 0f, gx) / slope, -MathF.Atan(lean)) * basis;

            if (job.Lod == 0) Add(rule.Kind, plant, plant == null ? 0 : (int)(kin * 977f) % plant.Near.Length, lx, rootY, lz, basis, tint, glow, far: false);
            // A share of the large thins out sooner far off (it was the share the far levels used to leave out).
            if (hasFar) Add(rule.Kind, plant, plant == null ? 0 : -1, lx, rootY, lz, basis, tint, glow + (shade > 0.6f ? 2f : 0f), far: true);

            // Trunks, stalks, pipes, shards and boulders are solid: their colliders, in planet coordinates, round
            // the parts of the near mesh this one draws (whatever the level of detail draws from here).
            solids?.MakeAll(rule.Kind, plant, plant == null ? 0 : (int)(kin * 977f) % plant.Near.Length, id, new Vector3((float)x, rootY, (float)z), scale, angle, job.Solids);
        }
        foreach (var (key, list) in buffers) job.Flora.Add(new FloraBatch(key.Kind, key.Plant, key.Variant, list.ToArray(), list.Count / 16, key.Far));

        void Add(Flora kind, Plant plant, int variant, float lx, float y, float lz, Basis basis, Rgb tint, float glow, bool far)
        {
            var key = (Kind: kind, Plant: plant?.Index ?? -1, Variant: plant == null ? 0 : variant, Far: far);
            if (!buffers.TryGetValue(key, out List<float> list)) buffers[key] = list = new List<float>();
            // MultiMesh layout: a 3x4 row-major transform, then four custom floats (tint and glow).
            list.Add(basis.X.X); list.Add(basis.Y.X); list.Add(basis.Z.X); list.Add(lx);
            list.Add(basis.X.Y); list.Add(basis.Y.Y); list.Add(basis.Z.Y); list.Add(y);
            list.Add(basis.X.Z); list.Add(basis.Y.Z); list.Add(basis.Z.Z); list.Add(lz);
            list.Add(tint.R); list.Add(tint.G); list.Add(tint.B); list.Add(glow);
        }
    }

    /// <summary>A plant's colour: its biome's, muted, each one a little lighter or darker (<paramref name="shade"/> 0..1).</summary>
    public static Rgb Tint(FloraRule rule, float shade) => rule.Color.Saturate(0.78f) * Mathx.Lerp(0.72f, 1.0f, shade);

    /// <summary>
    /// The colour of one scattered piece where it stands. Rock is the world's own rock, picking up a
    /// little of the ground it lies on and of its biome; crystal and ice keep their hue, but quietly.
    /// </summary>
    public static Rgb Tint(FloraRule rule, float shade, GroundPalette pal, double x, double z)
    {
        switch (rule.Kind)
        {
            case Flora.Rock or Flora.LavaRock:
                Rgb land = pal.SoilAt(x, z, out _);
                return Rgb.Lerp(Rgb.Lerp(pal.Rock, land, 0.18f), rule.Color.Saturate(0.45f), 0.14f) * Mathx.Lerp(0.82f, 1.04f, shade);
            case Flora.Crystal:
                return Rgb.Lerp(rule.Color.Saturate(0.42f), pal.Rock, 0.3f) * Mathx.Lerp(0.72f, 0.95f, shade);
            case Flora.IceShard:
                return rule.Color.Saturate(0.45f) * Mathx.Lerp(0.8f, 1.0f, shade);
            default:
                return Tint(rule, shade);
        }
    }

    /// <summary>Whether anything built lies under a crown's reach of a point: four spots round its rim (a road, or a place's ground well in from its blend).</summary>
    static bool CrownOver(PlanetTerrainSource source, GroundPalette pal, double x, double z, float reach)
    {
        double w = pal.Map.WidthMeters, h = pal.Map.HeightMeters;
        bool Over(double px, double pz) => source.Built(px, pz, out bool road) >= 0.5f || road;
        return Over(Mathx.WrapX(x + reach, w), z) || Over(Mathx.WrapX(x - reach, w), z) || Over(x, Mathx.Wrap(z + reach, h)) || Over(x, Mathx.Wrap(z - reach, h));
    }

    /// <summary>A plant's own roll for how far it may come into a place's ground, unrelated to its other rolls.</summary>
    internal static float Feather(float roll) => roll * 7.31f % 1f;

    /// <summary>Whether the ground has been dug under a root or beside it.</summary>
    internal static bool Dug(IReadOnlySet<long> dug, int x, int z) => dug != null && (dug.Contains(Key(x, z)) || dug.Contains(Key(x + 1, z)) || dug.Contains(Key(x, z + 1)) || dug.Contains(Key(x - 1, z)) || dug.Contains(Key(x, z - 1)));

    const float MaxRockLean = 0.8f;

    /// <summary>How far a kind's foot reaches from its root, at scale 1: what must stay in the ground on a slope.</summary>
    public static float FootRadius(Flora kind) => kind switch
    {
        Flora.Rock or Flora.LavaRock => 0.9f,
        Flora.Crystal or Flora.IceShard => 0.5f,
        Flora.Shrub => 0.6f,
        _ when FloraMeshes.IsLarge(kind) => 0.4f,
        _ => 0.2f,
    };

    /// <summary>A canonical column (x, z in metres) as a set key.</summary>
    public static long Key(int x, int z) => (long)x << 32 | (uint)z;

    internal static float Smooth(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
